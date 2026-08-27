using MapModeFramework;
using RegionsAndSocieties.Demographics;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties
{
    /// <summary>
    /// The regional employment overlay (0.2.0, #16): every settled region is tinted by its dominant
    /// occupation sector — green for agriculture, steel for industry, red for military, gold for trade —
    /// with the tint strengthening as that sector dominates more strongly. The mix is deterministic,
    /// driven by the region's world-object mix (military bases, extraction outposts, cities), its
    /// terrain, and its dominant faction's tech level. Categorical, so it builds on
    /// <see cref="MapMode_RegionDemographic"/>; materials are the four fixed sector colours across three
    /// dominance bands, built once.
    /// </summary>
    [StaticConstructorOnStartup]
    public class MapMode_Employment : MapMode_RegionDemographic
    {
        // One colour per occupation sector, indexed by (int)OccupationSector.
        private static readonly Color[] SectorColor = new Color[]
        {
            new Color(0.40f, 0.70f, 0.30f),   // Agriculture — green
            new Color(0.55f, 0.60f, 0.68f),   // Industry — steel
            new Color(0.80f, 0.25f, 0.25f),   // Military — red
            new Color(0.90f, 0.72f, 0.25f)    // Trade — gold
        };

        // Alpha per dominance band: a barely-dominant region is faint, a one-sector region solid.
        private static readonly float[] BandAlpha = new float[] { 0.35f, 0.52f, 0.70f };
        // Upper bounds (inclusive) of the first two dominant-share bands; above the last is band 2.
        private static readonly float[] BandUpperShare = new float[] { 0.40f, 0.60f };

        // [sector, band] materials, built once.
        private static Material[][] sectorMats;

        public MapMode_Employment() { }
        public MapMode_Employment(MapModeDef def) : base(def) { }

        protected override void EnsureMaterials()
        {
            if (sectorMats != null) return;
            sectorMats = new Material[SectorColor.Length][];
            for (int s = 0; s < SectorColor.Length; s++)
            {
                sectorMats[s] = new Material[BandAlpha.Length];
                Color rgb = SectorColor[s];
                for (int b = 0; b < BandAlpha.Length; b++)
                    sectorMats[s][b] = MakeOverlayMaterial(new Color(rgb.r, rgb.g, rgb.b, BandAlpha[b]));
            }
        }

        private static int BandForShare(float share)
        {
            for (int i = 0; i < BandUpperShare.Length; i++)
                if (share <= BandUpperShare[i]) return i;
            return BandAlpha.Length - 1;
        }

        protected override Material MaterialForRegion(RegionDemographics demo)
        {
            if (sectorMats == null) EnsureMaterials();
            OccupationSector sector = RegionDemographicsUtility.DominantSector(demo, out float share);
            if (share <= 0f) return null;
            return sectorMats[(int)sector][BandForShare(share)];
        }

        protected override string LabelForRegion(RegionDemographics demo)
        {
            OccupationSector sector = RegionDemographicsUtility.DominantSector(demo, out float share);
            return share > 0f ? $"{sector} {Mathf.RoundToInt(share * 100f)}%" : null;
        }

        protected override string SummaryFor(GeographicProvince province)
            => RegionDemographicsUtility.EmploymentSummary(province);
    }
}
