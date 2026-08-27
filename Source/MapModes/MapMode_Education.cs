using MapModeFramework;
using RegionsAndSocieties.Demographics;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties
{
    /// <summary>
    /// The regional education overlay (0.2.0, #15): every settled region is shaded by its 0-100
    /// education index — the share-weighted mean attainment across the four tiers — from an unschooled
    /// brown through to a highly-educated blue. The index is deterministic, driven by faction tech
    /// level with ideology and xenotype-aptitude refinements. Plumbing lives in
    /// <see cref="MapMode_RegionScalarBanded"/>; this only names the bands and the value it shows.
    /// </summary>
    [StaticConstructorOnStartup]
    public class MapMode_Education : MapMode_RegionScalarBanded
    {
        // Five bands across the education index, low to high.
        private static readonly Color[] Bands = new Color[]
        {
            new Color(0.45f, 0.32f, 0.20f, 0.55f),   // 0: brown — largely illiterate
            new Color(0.72f, 0.60f, 0.30f, 0.52f),   // 1: tan — basic
            new Color(0.60f, 0.72f, 0.45f, 0.52f),   // 2: sage — mixed
            new Color(0.35f, 0.68f, 0.72f, 0.56f),   // 3: teal — skilled
            new Color(0.20f, 0.45f, 0.85f, 0.62f)    // 4: blue — highly educated
        };

        // Upper bounds (inclusive) of the first four index bands (0-100); above the last is band 4.
        private static readonly float[] Uppers = new float[] { 20f, 40f, 60f, 80f };

        public MapMode_Education() { }
        public MapMode_Education(MapModeDef def) : base(def) { }

        protected override Color[] BandColors => Bands;
        protected override float[] BandUpperBounds => Uppers;
        protected override float ValueFor(RegionDemographics demo) => demo.educationIndex;

        protected override string LabelForRegion(RegionDemographics demo) => demo.educationIndex.ToString();

        protected override string SummaryFor(GeographicProvince province)
            => RegionDemographicsUtility.EducationSummary(province);
    }
}
