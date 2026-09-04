using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RegionsAndSocieties.Partition
{
    /// <summary>
    /// Per-biome region-size multiplier for the contain-then-subdivide partition. A biome's weight
    /// scales BOTH ends of the region size band ([baseMin, baseMax] × weight), so sparse, near-empty
    /// biomes get FEWER, LARGER regions and settled biomes stay at the base size. An ice-sheet basin at
    /// 10× stays one region where a temperate basin of the same tile count subdivides into ~10.
    ///
    /// <para>Defaults live in <see cref="DefaultFor"/>; the player overrides them per biome through the
    /// mod-settings sliders, which write <see cref="Overrides"/>. Reading is a pure lookup so the
    /// partition stays deterministic from terrain + settings.</para>
    /// </summary>
    public static class BiomeRegionWeights
    {
        /// <summary>Never scale a region band below/above these, whatever a slider says — keeps a stray
        /// setting from collapsing every region to a point or swallowing a continent.</summary>
        public const float MinWeight = 0.25f;
        public const float MaxWeight = 20f;

        /// <summary>Player overrides by biome <c>defName</c>, populated from the settings sliders. Empty =
        /// use the built-in defaults.</summary>
        public static readonly Dictionary<string, float> Overrides = new Dictionary<string, float>();

        /// <summary>The size multiplier in force for a biome: a player override if one is set, else the
        /// built-in default, clamped to the sane band.</summary>
        public static float Weight(BiomeDef biome)
        {
            if (biome == null) return 1f;
            float w = Overrides.TryGetValue(biome.defName, out float o) ? o : DefaultFor(biome);
            if (w < MinWeight) w = MinWeight;
            if (w > MaxWeight) w = MaxWeight;
            return w;
        }

        /// <summary>Built-in default multiplier for a biome. Keyed by defName for the vanilla biomes,
        /// with a fertility/coverage-based fallback so modded biomes still get a sensible size: barren,
        /// low-plant-density land reads as sparse (bigger regions), lush land as settled (base size).</summary>
        public static float DefaultFor(BiomeDef biome)
        {
            if (biome == null) return 1f;
            switch (biome.defName)
            {
                case "IceSheet": return 10f;
                case "SeaIce": return 8f;
                case "Tundra": return 2.5f;
                case "ColdBog": return 1.5f;
                case "ExtremeDesert": return 4f;
                case "Desert": return 3f;
                case "AridShrubland": return 2f;
                case "BorealForest": return 1.25f;
                case "TemperateForest": return 1f;
                case "TemperateSwamp": return 1f;
                case "TropicalRainforest": return 1f;
                case "TropicalSwamp": return 1f;
                default: return FallbackFromFertility(biome);
            }
        }

        // Sparse land = big regions. Plant density is the best single proxy the base game exposes for
        // "how much life/settlement this biome supports"; map its 0..1 range onto ~3×..1×.
        private static float FallbackFromFertility(BiomeDef biome)
        {
            float density = biome.plantDensity;   // ~0 for ice/desert, ~0.6+ for rainforest
            if (density <= 0.02f) return 4f;
            if (density >= 0.5f) return 1f;
            // Linear between the two anchors.
            return UnityEngine.Mathf.Lerp(4f, 1f, (density - 0.02f) / (0.5f - 0.02f));
        }
    }
}
