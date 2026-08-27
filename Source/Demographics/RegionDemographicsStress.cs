using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>
    /// The sparse mutation layer over the deterministic demographic baseline (0.8 #36, extended 0.2.0
    /// #11). The baseline is free (regenerated from the seed); only regions something has deliberately
    /// *stressed* — an event that impoverishes it, a war that skews its sex ratio — cost memory, as one
    /// small override each.
    ///
    /// <para>This is the write side of the demographics API that other systems call to change the world
    /// over time: the Factions spread layer, WorldNews, the storyteller LLM, and — through the
    /// <see cref="DemographicHooks"/> seam — companion mods (World Domination, Empire, …) that drive
    /// drafting and combat. It is scribed through <see cref="SynapseRegionManager"/> so overrides
    /// survive save/load, and decayed once per <see cref="SynapseRegionManager.WorldComponentTick"/>.</para>
    ///
    /// <para>Two override flavours, both sparse:
    /// a <b>wealth multiplier</b> (permanent until changed), and a list of <b>sex-ratio skews</b>
    /// (<see cref="DemographicSkew"/>) that are either transient — held until the caller clears them,
    /// for "a draft is in progress" — or generational — decaying linearly back to baseline over the
    /// configured number of years, for "this region lost a generation of men to a war".</para>
    /// </summary>
    public static class RegionDemographicsStress
    {
        private static Dictionary<int, DemographicOverride> overrides = new Dictionary<int, DemographicOverride>();

        // Sex ratio must never reach a degenerate all-one-sex value however hard it is stressed.
        private const float MinFemaleFraction = 0.05f;
        private const float MaxFemaleFraction = 0.95f;

        /// <summary>Apply any override for a region on top of its freshly aggregated baseline.</summary>
        public static void Apply(int regionId, RegionDemographics demo)
        {
            if (demo == null || overrides.Count == 0) return;
            if (!overrides.TryGetValue(regionId, out DemographicOverride ov) || ov == null) return;

            if (ov.wealthMultiplier > 0f && ov.wealthMultiplier != 1f)
            {
                demo.overallMedianWealth = (int)(demo.overallMedianWealth * ov.wealthMultiplier);
                var keys = new List<XenotypeDefKey>();
                foreach (var kv in demo.medianWealthByRace) keys.Add(new XenotypeDefKey(kv.Key));
                foreach (var k in keys) demo.medianWealthByRace[k.def] = (int)(demo.medianWealthByRace[k.def] * ov.wealthMultiplier);
            }

            float sexDelta = ov.CurrentFemaleDelta();
            if (sexDelta != 0f)
                demo.femaleFraction = Mathf.Clamp(demo.femaleFraction + sexDelta, MinFemaleFraction, MaxFemaleFraction);
        }

        // --- sex-ratio skews (the #11 hook surface) ---------------------------

        /// <summary>The net sex skew currently in force on a region, as a delta on the female fraction
        /// (positive = more female than the baseline). Zero when nothing stresses it. For reports/overlay.</summary>
        public static float CurrentFemaleDelta(int regionId)
        {
            return overrides.TryGetValue(regionId, out DemographicOverride ov) && ov != null ? ov.CurrentFemaleDelta() : 0f;
        }

        /// <summary>
        /// Add or replace a sex-ratio skew on a region. <paramref name="femaleDelta"/> shifts the female
        /// fraction (positive = fewer men present, e.g. men drafted or lost). <paramref name="durationTicks"/>
        /// of 0 makes it <b>transient</b> — it holds at full strength until cleared; a positive value makes
        /// it <b>decay linearly to zero</b> over that many ticks. A <paramref name="tag"/> identifies the
        /// source so repeat calls replace rather than pile up (and <see cref="ClearSexSkew"/> can target it).
        /// </summary>
        public static void SkewSexRatio(int regionId, float femaleDelta, int durationTicks = 0, string tag = null)
        {
            if (femaleDelta == 0f) return;
            DemographicOverride ov = GetOrCreate(regionId);
            ov.AddSexSkew(new DemographicSkew(femaleDelta, durationTicks, tag), replaceSameTag: true, mergeMagnitude: false);
            RegionDemographicsUtility.InvalidateRegionCache();
        }

        /// <summary>
        /// Record combat losses in a region as a generational sex skew (#11): if an attacking or
        /// defending force loses far more of one sex — men, in the default men-first draft — the region
        /// reads short of that sex and recovers over the configured generation length. Magnitude scales
        /// with the imbalance against the region's modelled population. Repeated battles compound and
        /// refresh the scar rather than adding endless separate entries.
        /// </summary>
        public static void RecordCombatLosses(int regionId, int maleDeaths, int femaleDeaths)
        {
            int net = maleDeaths - femaleDeaths;
            if (net == 0) return;

            float pop = Mathf.Max(50f, RegionPopulation(regionId));
            float delta = Mathf.Clamp(net / (2f * pop), -0.3f, 0.3f);   // men-heavy losses => more female

            DemographicOverride ov = GetOrCreate(regionId);
            ov.AddSexSkew(new DemographicSkew(delta, GenerationTicks(), "combat-losses"),
                replaceSameTag: false, mergeMagnitude: true);
            RegionDemographicsUtility.InvalidateRegionCache();
        }

        /// <summary>Clear a region's sex skews — one <paramref name="tag"/> (e.g. a draft that just ended)
        /// or all of them when tag is null. Leaves any wealth stress in place.</summary>
        public static void ClearSexSkew(int regionId, string tag = null)
        {
            if (!overrides.TryGetValue(regionId, out DemographicOverride ov) || ov == null) return;
            if (ov.ClearSexSkews(tag))
            {
                if (ov.IsEmpty) overrides.Remove(regionId);
                RegionDemographicsUtility.InvalidateRegionCache();
            }
        }

        /// <summary>The configured generation length in ticks, from the live mod setting (default 15 years).</summary>
        public static int GenerationTicks()
        {
            float years = Mathf.Max(0.1f, Integration.WorldObjectIntegrationSettings.demographicGenerationYears);
            return Mathf.RoundToInt(years * GenDate.TicksPerYear);
        }

        /// <summary>Advance every decaying skew by <paramref name="ticksPassed"/>, drop what has expired,
        /// and prune emptied overrides. Called (throttled) from the world-component tick. Invalidates the
        /// region cache only when something actually changed, so a quiet world costs nothing.</summary>
        public static void Tick(int ticksPassed)
        {
            if (overrides.Count == 0 || ticksPassed <= 0) return;

            bool changed = false;
            List<int> emptied = null;
            foreach (var kv in overrides)
            {
                DemographicOverride ov = kv.Value;
                if (ov == null) continue;
                if (ov.Decay(ticksPassed)) changed = true;
                if (ov.IsEmpty)
                {
                    (emptied ?? (emptied = new List<int>())).Add(kv.Key);
                }
            }
            if (emptied != null)
                foreach (int id in emptied) overrides.Remove(id);

            if (changed) RegionDemographicsUtility.InvalidateRegionCache();
        }

        // --- wealth stress (unchanged 0.8 surface) ---------------------------

        /// <summary>
        /// Stress a region's wealth by a multiplier (0.5 = an event halved it; 1.5 = a boom). Compounds
        /// with any existing stress. The change persists and takes effect on the next demographics read.
        /// </summary>
        public static void StressWealth(int regionId, float multiplier)
        {
            if (multiplier <= 0f) return;
            GetOrCreate(regionId).wealthMultiplier *= multiplier;
            RegionDemographicsUtility.InvalidateRegionCache();
        }

        /// <summary>Clear a region's overrides (revert to the deterministic baseline).</summary>
        public static void ClearRegion(int regionId)
        {
            if (overrides.Remove(regionId)) RegionDemographicsUtility.InvalidateRegionCache();
        }

        public static bool HasOverride(int regionId) => overrides.ContainsKey(regionId);
        public static int OverrideCount => overrides.Count;

        private static DemographicOverride GetOrCreate(int regionId)
        {
            if (!overrides.TryGetValue(regionId, out DemographicOverride ov) || ov == null)
            {
                ov = new DemographicOverride();
                overrides[regionId] = ov;
            }
            return ov;
        }

        private static float RegionPopulation(int regionId)
        {
            var mgr = Find.World?.GetComponent<SynapseRegionManager>();
            if (mgr?.Provinces == null) return 0f;
            foreach (GeographicProvince p in mgr.Provinces)
                if (p != null && p.id == regionId) return p.currentPopulation;
            return 0f;
        }

        /// <summary>Scribed by <see cref="SynapseRegionManager.ExposeData"/> so overrides survive save/load.</summary>
        public static void ExposeData()
        {
            Scribe_Collections.Look(ref overrides, "demographicOverrides", LookMode.Value, LookMode.Deep);
            if (overrides == null) overrides = new Dictionary<int, DemographicOverride>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit) RegionDemographicsUtility.InvalidateRegionCache();
        }

        // Small helper so we can mutate the dictionary while iterating its keys.
        private struct XenotypeDefKey
        {
            public readonly RimWorld.XenotypeDef def;
            public XenotypeDefKey(RimWorld.XenotypeDef d) { def = d; }
        }
    }

    /// <summary>One decaying (or transient) skew on a demographic axis. First axis is the sex ratio.</summary>
    public class DemographicSkew : IExposable
    {
        public float magnitude;      // full-strength delta on the female fraction
        public int ticksRemaining;   // counts down for a decaying skew; unused when durationTicks <= 0
        public int durationTicks;    // 0 => transient (holds until cleared); > 0 => linear decay to zero
        public string tag;           // source label, so callers can replace/clear/merge by source

        public DemographicSkew() { }

        public DemographicSkew(float magnitude, int durationTicks, string tag)
        {
            this.magnitude = magnitude;
            this.durationTicks = durationTicks > 0 ? durationTicks : 0;
            this.ticksRemaining = this.durationTicks;
            this.tag = tag;
        }

        /// <summary>The delta in force right now: full magnitude for a transient skew, or magnitude scaled
        /// by how much of its life remains for a decaying one.</summary>
        public float CurrentDelta => durationTicks <= 0
            ? magnitude
            : magnitude * Mathf.Clamp01((float)ticksRemaining / durationTicks);

        /// <summary>A decaying skew whose life has run out. Transient skews never expire on their own.</summary>
        public bool Expired => durationTicks > 0 && ticksRemaining <= 0;

        /// <summary>Advance a decaying skew; returns true if its current strength changed.</summary>
        public bool Decay(int ticksPassed)
        {
            if (durationTicks <= 0 || ticksRemaining <= 0) return false;
            ticksRemaining -= ticksPassed;
            if (ticksRemaining < 0) ticksRemaining = 0;
            return true;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref magnitude, "magnitude", 0f);
            Scribe_Values.Look(ref ticksRemaining, "ticksRemaining", 0);
            Scribe_Values.Look(ref durationTicks, "durationTicks", 0);
            Scribe_Values.Look(ref tag, "tag");
        }
    }

    /// <summary>One region's sparse demographic override. Only what was changed is stored.</summary>
    public class DemographicOverride : IExposable
    {
        public float wealthMultiplier = 1f;
        public List<DemographicSkew> sexSkews = new List<DemographicSkew>();

        /// <summary>Nothing left to store: baseline wealth and no live sex skews. Such an override is pruned.</summary>
        public bool IsEmpty => wealthMultiplier == 1f && (sexSkews == null || sexSkews.Count == 0);

        /// <summary>The net current sex delta from all this region's skews.</summary>
        public float CurrentFemaleDelta()
        {
            if (sexSkews == null) return 0f;
            float total = 0f;
            for (int i = 0; i < sexSkews.Count; i++)
                if (sexSkews[i] != null) total += sexSkews[i].CurrentDelta;
            return total;
        }

        public void AddSexSkew(DemographicSkew skew, bool replaceSameTag, bool mergeMagnitude)
        {
            if (skew == null) return;
            if (sexSkews == null) sexSkews = new List<DemographicSkew>();

            if (skew.tag != null && (replaceSameTag || mergeMagnitude))
            {
                for (int i = 0; i < sexSkews.Count; i++)
                {
                    if (sexSkews[i]?.tag != skew.tag) continue;
                    if (mergeMagnitude)
                    {
                        // Compound the scar and refresh its clock: a fresh loss deepens the shortfall and
                        // restarts the recovery from now.
                        sexSkews[i].magnitude = Mathf.Clamp(sexSkews[i].magnitude + skew.magnitude, -0.5f, 0.5f);
                        sexSkews[i].durationTicks = skew.durationTicks;
                        sexSkews[i].ticksRemaining = skew.durationTicks;
                    }
                    else
                    {
                        sexSkews[i] = skew;   // replace
                    }
                    return;
                }
            }
            sexSkews.Add(skew);
        }

        /// <summary>Remove sex skews by tag (all when tag is null). Returns true if anything was removed.</summary>
        public bool ClearSexSkews(string tag)
        {
            if (sexSkews == null || sexSkews.Count == 0) return false;
            if (tag == null) { sexSkews.Clear(); return true; }
            return sexSkews.RemoveAll(s => s != null && s.tag == tag) > 0;
        }

        /// <summary>Decay every skew and drop the expired ones. Returns true if any strength changed.</summary>
        public bool Decay(int ticksPassed)
        {
            if (sexSkews == null || sexSkews.Count == 0) return false;
            bool changed = false;
            for (int i = sexSkews.Count - 1; i >= 0; i--)
            {
                DemographicSkew s = sexSkews[i];
                if (s == null) { sexSkews.RemoveAt(i); continue; }
                if (s.Decay(ticksPassed)) changed = true;
                if (s.Expired) { sexSkews.RemoveAt(i); changed = true; }
            }
            return changed;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref wealthMultiplier, "wealthMultiplier", 1f);
            Scribe_Collections.Look(ref sexSkews, "sexSkews", LookMode.Deep);
            if (sexSkews == null) sexSkews = new List<DemographicSkew>();
        }
    }
}
