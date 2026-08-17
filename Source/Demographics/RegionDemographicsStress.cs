using System.Collections.Generic;
using Verse;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>
    /// The sparse mutation layer over the deterministic demographic baseline (0.8, #36). The baseline
    /// is free (regenerated from the seed); only regions something has deliberately *stressed* — an
    /// event that impoverishes it, a shift in its make-up — cost memory, as one small override each.
    ///
    /// <para>This is the write side of the demographics API the Factions spread layer, WorldNews and
    /// the storyteller LLM call to change the world over time. It is scribed through
    /// <see cref="SynapseRegionManager"/> so overrides survive save/load. First cut carries a wealth
    /// multiplier; more axes (race-share nudge, ideology drift) slot in the same way.</para>
    /// </summary>
    public static class RegionDemographicsStress
    {
        private static Dictionary<int, DemographicOverride> overrides = new Dictionary<int, DemographicOverride>();

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
        }

        /// <summary>
        /// Stress a region's wealth by a multiplier (0.5 = an event halved it; 1.5 = a boom). Compounds
        /// with any existing stress. The change persists and takes effect on the next demographics read.
        /// </summary>
        public static void StressWealth(int regionId, float multiplier)
        {
            if (multiplier <= 0f) return;
            if (!overrides.TryGetValue(regionId, out DemographicOverride ov) || ov == null)
            {
                ov = new DemographicOverride();
                overrides[regionId] = ov;
            }
            ov.wealthMultiplier *= multiplier;
            RegionDemographicsUtility.InvalidateRegionCache();
        }

        /// <summary>Clear a region's overrides (revert to the deterministic baseline).</summary>
        public static void ClearRegion(int regionId)
        {
            if (overrides.Remove(regionId)) RegionDemographicsUtility.InvalidateRegionCache();
        }

        public static bool HasOverride(int regionId) => overrides.ContainsKey(regionId);
        public static int OverrideCount => overrides.Count;

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

    /// <summary>One region's sparse demographic override. Only what was changed is stored.</summary>
    public class DemographicOverride : IExposable
    {
        public float wealthMultiplier = 1f;

        public void ExposeData()
        {
            Scribe_Values.Look(ref wealthMultiplier, "wealthMultiplier", 1f);
        }
    }
}
