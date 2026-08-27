using MapModeFramework;
using RegionsAndSocieties.Demographics;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties
{
    /// <summary>
    /// The regional age-structure overlay (0.2.0, #10): every settled region is shaded by its median
    /// age, from youthful green through mature yellow to elderly red, so the age model reads at a
    /// glance the way the dwellings heatmap reads population. All the plumbing — reading the shared
    /// per-region aggregate, painting the terrain layer, leaving water and wilderness unshaded — lives
    /// in <see cref="MapMode_RegionScalarBanded"/>; this only names the bands and the value it shows.
    /// </summary>
    [StaticConstructorOnStartup]
    public class MapMode_AgeStructure : MapMode_RegionScalarBanded
    {
        // Five median-age bands, young to old. Tuned so a typical industrial society (~mid-30s) sits in
        // the middle yellow and only genuinely young/old structures reach the ends.
        private static readonly Color[] Bands = new Color[]
        {
            new Color(0.30f, 0.70f, 0.35f, 0.55f),   // 0: green — youthful (< 25)
            new Color(0.62f, 0.78f, 0.25f, 0.55f),   // 1: yellow-green — young adult
            new Color(0.92f, 0.85f, 0.15f, 0.58f),   // 2: yellow — mature (mid-30s)
            new Color(0.95f, 0.55f, 0.12f, 0.62f),   // 3: orange — aging
            new Color(0.85f, 0.20f, 0.35f, 0.66f)    // 4: red — elderly / long-lived
        };

        // Upper bounds (inclusive) of the first four bands, in years; anything above the last is band 4.
        private static readonly float[] Uppers = new float[] { 24f, 34f, 44f, 59f };

        public MapMode_AgeStructure() { }
        public MapMode_AgeStructure(MapModeDef def) : base(def) { }

        protected override Color[] BandColors => Bands;
        protected override float[] BandUpperBounds => Uppers;
        protected override float ValueFor(RegionDemographics demo) => demo.medianAge;
        protected override bool HasValue(RegionDemographics demo) => demo.medianAge > 0;

        protected override string LabelForRegion(RegionDemographics demo)
            => demo.medianAge > 0 ? demo.medianAge.ToString() : null;

        protected override string SummaryFor(GeographicProvince province)
            => RegionDemographicsUtility.AgeStructureSummary(province);
    }
}
