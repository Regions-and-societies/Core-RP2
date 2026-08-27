using MapModeFramework;
using RegionsAndSocieties.Demographics;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties
{
    /// <summary>
    /// The regional wealth / socioeconomic overlay (0.2.0, #14): every settled region is shaded by its
    /// 0-100 SES index — the share-weighted standing across the subsistence → affluent tiers — from a
    /// poor deep red through to an affluent green. The index is deterministic: the engine's per-tile
    /// wealth (faction tech level, settlement size) classified into tiers, lifted by the region's
    /// resource richness and trade-road access. Plumbing lives in
    /// <see cref="MapMode_RegionScalarBanded"/>; this only names the bands and the value it shows.
    /// </summary>
    [StaticConstructorOnStartup]
    public class MapMode_Wealth : MapMode_RegionScalarBanded
    {
        // Five bands across the SES index, poor to rich.
        private static readonly Color[] Bands = new Color[]
        {
            new Color(0.75f, 0.20f, 0.18f, 0.60f),   // 0: deep red — subsistence
            new Color(0.90f, 0.50f, 0.20f, 0.56f),   // 1: orange — struggling
            new Color(0.92f, 0.85f, 0.30f, 0.52f),   // 2: yellow — modest
            new Color(0.60f, 0.78f, 0.35f, 0.55f),   // 3: light green — prosperous
            new Color(0.20f, 0.65f, 0.35f, 0.62f)    // 4: green — affluent
        };

        // Upper bounds (inclusive) of the first four index bands (0-100); above the last is band 4.
        private static readonly float[] Uppers = new float[] { 20f, 40f, 60f, 80f };

        public MapMode_Wealth() { }
        public MapMode_Wealth(MapModeDef def) : base(def) { }

        protected override Color[] BandColors => Bands;
        protected override float[] BandUpperBounds => Uppers;
        protected override float ValueFor(RegionDemographics demo) => demo.sesIndex;

        protected override string LabelForRegion(RegionDemographics demo) => demo.sesIndex.ToString();

        protected override string SummaryFor(GeographicProvince province)
            => RegionDemographicsUtility.SocioeconomicSummary(province);
    }
}
