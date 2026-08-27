using System;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>
    /// The deterministic core of the demographics model (0.8, #36). A tile's people are a pure
    /// function of the world seed and the tile id, so nothing has to be stored: the same seed always
    /// regenerates the same sample, and a region's makeup is those tile samples aggregated. Only
    /// deliberate changes (the stress API) cost memory, as sparse overrides on top of this baseline.
    ///
    /// <para>Pure by design, like <see cref="Sizing.TierPyramidRules"/>: a small reproducible RNG plus
    /// weighted picking and integer ranges, working on plain numbers so the whole thing is testable
    /// without a game and identical on every machine (no <c>UnityEngine.Random</c>, whose global state
    /// would make results depend on call order).</para>
    /// </summary>
    public static class DemographicsRules
    {
        /// <summary>
        /// A stable per-tile seed from the world seed and tile id (plus a salt so independent draws —
        /// race vs wealth vs sex — don't correlate). Deterministic across save/load and machines.
        /// </summary>
        public static uint TileSeed(int worldSeed, int tileId, int salt = 0)
        {
            unchecked
            {
                uint h = Mix((uint)worldSeed ^ 0x9E3779B9u);
                h = Mix(h ^ (uint)tileId);
                h = Mix(h ^ (uint)salt);
                return h == 0u ? 0x6D2B79F5u : h;   // xorshift must never be seeded with 0
            }
        }

        // fmix32 (MurmurHash3 finalizer) — good avalanche, cheap, deterministic.
        private static uint Mix(uint x)
        {
            unchecked
            {
                x ^= x >> 16; x *= 0x7FEB352Du;
                x ^= x >> 15; x *= 0x846CA68Bu;
                x ^= x >> 16;
                return x;
            }
        }

        /// <summary>Advance an xorshift32 state and return the next value. State must be non-zero.</summary>
        public static uint Next(ref uint state)
        {
            unchecked
            {
                uint x = state == 0u ? 0x6D2B79F5u : state;
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                state = x;
                return x;
            }
        }

        /// <summary>A float in [0,1) from the RNG.</summary>
        public static float NextFloat(ref uint state)
        {
            return (Next(ref state) & 0xFFFFFFu) / (float)0x1000000;
        }

        /// <summary>An int in [min, max] inclusive.</summary>
        public static int RangeInt(ref uint state, int min, int max)
        {
            if (max <= min) return min;
            uint span = (uint)(max - min + 1);
            return min + (int)(Next(ref state) % span);
        }

        /// <summary>
        /// Pick an index in proportion to <paramref name="weights"/> (negatives treated as 0).
        /// Returns -1 when every weight is non-positive.
        /// </summary>
        public static int WeightedPick(ref uint state, float[] weights)
        {
            if (weights == null || weights.Length == 0) return -1;

            float total = 0f;
            for (int i = 0; i < weights.Length; i++)
                if (weights[i] > 0f) total += weights[i];
            if (total <= 0f) return -1;

            float r = NextFloat(ref state) * total;
            float acc = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] <= 0f) continue;
                acc += weights[i];
                if (r < acc) return i;
            }
            // Floating-point slop: fall through to the last positive-weight index.
            for (int i = weights.Length - 1; i >= 0; i--)
                if (weights[i] > 0f) return i;
            return -1;
        }

        /// <summary>
        /// The shape a settlement's demographic pressure follows from its centre (full population) out
        /// to its reach (zero). Selectable so tuning can move off linear if the border balance needs a
        /// different curve — the whole point of keeping this a pure, swappable function.
        /// </summary>
        public enum FalloffModel
        {
            Linear,        // (1-x)^s — constant-ish gradient
            Smoothstep,    // S-curve: solid core, contested middle, solid rim
            Logarithmic,   // drops fast near the city then flattens
            Exponential,   // convex: holds high, then falls away
            InverseSquare  // physical 1/(1+s·x²) decay
        }

        /// <summary>
        /// A settlement's pressure at <paramref name="distance"/>, given its <paramref name="population"/>
        /// (the value at the centre) and <paramref name="reachDistance"/> (where it reaches zero). All
        /// models are normalised: full population at the centre, zero at the reach, monotonically
        /// decreasing between. <paramref name="shape"/> tunes the steepness of the chosen model.
        /// Pure — same inputs, same output, no game state.
        /// </summary>
        public static float Pressure(FalloffModel model, float population, float distance, float reachDistance, float shape)
        {
            if (population <= 0f || reachDistance <= 0f) return 0f;
            float x = distance / reachDistance;
            if (x <= 0f) return population;
            if (x >= 1f) return 0f;

            double s = Math.Max(0.01, shape);
            double f;
            switch (model)
            {
                case FalloffModel.Smoothstep:
                {
                    double t = 1.0 - (3.0 * x * x - 2.0 * x * x * x);
                    f = Math.Pow(t, s);
                    break;
                }
                case FalloffModel.Logarithmic:
                    f = 1.0 - Math.Log(1.0 + s * x) / Math.Log(1.0 + s);
                    break;
                case FalloffModel.Exponential:
                {
                    double e = Math.Exp(-s);
                    f = (Math.Exp(-s * x) - e) / (1.0 - e);
                    break;
                }
                case FalloffModel.InverseSquare:
                {
                    double g1 = 1.0 / (1.0 + s);
                    double gx = 1.0 / (1.0 + s * x * x);
                    f = (gx - g1) / (1.0 - g1);
                    break;
                }
                default: // Linear
                    f = Math.Pow(1.0 - x, s);
                    break;
            }

            if (f < 0.0) f = 0.0; else if (f > 1.0) f = 1.0;
            return (float)(population * f);
        }

        /// <summary>
        /// Cosine similarity of two equal-length non-negative vectors, clamped to [0,1]. Used to compare
        /// two regions' meme-share profiles (#13): 1 = identical belief mix, 0 = no shared memes. Returns
        /// 0 for null, mismatched-length, empty, or all-zero inputs. Pure — same inputs, same output.
        /// </summary>
        public static float Cosine(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length != b.Length || a.Length == 0) return 0f;
            double dot = 0, na = 0, nb = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += (double)a[i] * b[i];
                na += (double)a[i] * a[i];
                nb += (double)b[i] * b[i];
            }
            if (na <= 0 || nb <= 0) return 0f;
            double c = dot / (Math.Sqrt(na) * Math.Sqrt(nb));
            if (c < 0) c = 0; else if (c > 1) c = 1;
            return (float)c;
        }

        /// <summary>
        /// The median of a sorted-or-unsorted integer sample (used for per-race median wealth). Returns
        /// 0 for an empty sample. Copies and sorts, so callers pass a throwaway array.
        /// </summary>
        public static int Median(int[] values, int count)
        {
            if (values == null || count <= 0) return 0;
            Array.Sort(values, 0, count);
            int mid = count / 2;
            return (count & 1) == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2;
        }
    }
}
