using System.Collections.Generic;
using MapModeFramework;
using RegionsAndSocieties.Demographics;
using RimWorld;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties
{
    /// <summary>
    /// The regional ideology overlay (0.2.0, #13): every settled region is tinted by its dominant ideo,
    /// using that ideo's own in-game colour, with the tint strengthening as its share grows — a
    /// single-belief region reads solid, a contested one pale. With Ideology off there are no ideos —
    /// everyone is secular — so the region is left unshaded and the tooltip says so rather than painting
    /// a flat map as if it were data. Categorical, so it builds on <see cref="MapMode_RegionDemographic"/>
    /// directly. Materials are built lazily, one small set per ideo.
    /// </summary>
    [StaticConstructorOnStartup]
    public class MapMode_Ideology : MapMode_RegionDemographic
    {
        // One material per (ideo, dominance band). Ideos are per-save objects; the set in a world is
        // small, so this stays tiny.
        private static readonly Dictionary<Ideo, Material[]> ideoMats = new Dictionary<Ideo, Material[]>();

        // Alpha per dominance band: a barely-dominant region is faint, a near-uniform one solid.
        private static readonly float[] BandAlpha = new float[] { 0.35f, 0.52f, 0.70f };
        // Upper bounds (inclusive) of the first two dominant-share bands; above the last is band 2.
        private static readonly float[] BandUpperShare = new float[] { 0.45f, 0.70f };

        public MapMode_Ideology() { }
        public MapMode_Ideology(MapModeDef def) : base(def) { }

        private static int BandForShare(float share)
        {
            for (int i = 0; i < BandUpperShare.Length; i++)
                if (share <= BandUpperShare[i]) return i;
            return BandAlpha.Length - 1;
        }

        private static Material MaterialFor(Ideo ideo, float share)
        {
            if (!ideoMats.TryGetValue(ideo, out Material[] bands))
            {
                bands = new Material[BandAlpha.Length];
                Color rgb = ideo.Color;   // the ideo's own colour, so it matches the rest of the UI
                for (int b = 0; b < BandAlpha.Length; b++)
                    bands[b] = MakeOverlayMaterial(new Color(rgb.r, rgb.g, rgb.b, BandAlpha[b]));
                ideoMats[ideo] = bands;
            }
            return bands[BandForShare(share)];
        }

        protected override void EnsureMaterials()
        {
            // Unity materials MUST be created on the main thread; MapModeFramework builds its sub-meshes
            // (and calls GetMaterial → MaterialForRegion) on a worker thread. DoPreRegenerate runs on the
            // main thread, so pre-build every ideo's material set here instead of lazily off-thread.
            if (!ModLister.IdeologyInstalled || Find.IdeoManager == null) return;
            List<Ideo> all = Find.IdeoManager.IdeosListForReading;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && !ideoMats.ContainsKey(all[i]))
                    MaterialFor(all[i], 1f);   // builds + caches the band set on the main thread
        }

        protected override Material MaterialForRegion(RegionDemographics demo)
        {
            if (!demo.ideologyActive) return null;   // secular; tooltip states it
            Ideo dominant = RegionDemographicsUtility.DominantIdeo(demo, out float share);
            return dominant != null ? MaterialFor(dominant, share) : null;
        }

        protected override string LabelForRegion(RegionDemographics demo)
        {
            if (!demo.ideologyActive) return null;
            Ideo dominant = RegionDemographicsUtility.DominantIdeo(demo, out float share);
            return dominant != null ? $"{dominant.name} {Mathf.RoundToInt(share * 100f)}%" : null;
        }

        protected override string SummaryFor(GeographicProvince province)
            => RegionDemographicsUtility.IdeologySummary(province);
    }
}
