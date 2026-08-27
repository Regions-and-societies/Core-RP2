using MapModeFramework;
using RegionsAndSocieties.Demographics;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties
{
    /// <summary>
    /// The regional sex-ratio overlay (0.2.0, #11): every settled region is shaded by how its sex
    /// balance departs from the ~50/50 baseline — blue where men outnumber women, magenta where women
    /// outnumber men, a faint neutral where it is even. The baseline is deterministic and genuinely
    /// near-even, so a mostly-neutral map is honest data, not a blank; the colour appears where a
    /// mod-driven skew is in force (a draft in progress, a war's generational scar — see
    /// <see cref="DemographicHooks"/>). Plumbing lives in <see cref="MapMode_RegionScalarBanded"/>.
    /// </summary>
    [StaticConstructorOnStartup]
    public class MapMode_SexRatio : MapMode_RegionScalarBanded
    {
        // Five bands across the female fraction, male-heavy to female-heavy, centred on an even split.
        private static readonly Color[] Bands = new Color[]
        {
            new Color(0.20f, 0.45f, 0.85f, 0.60f),   // 0: strongly male
            new Color(0.45f, 0.65f, 0.90f, 0.50f),   // 1: male-leaning
            new Color(0.60f, 0.60f, 0.62f, 0.30f),   // 2: even — faint neutral
            new Color(0.85f, 0.55f, 0.80f, 0.50f),   // 3: female-leaning
            new Color(0.80f, 0.20f, 0.60f, 0.60f)    // 4: strongly female
        };

        // Upper bounds (inclusive) of the first four female-fraction bands; above the last is band 4.
        private static readonly float[] Uppers = new float[] { 0.40f, 0.47f, 0.53f, 0.60f };

        public MapMode_SexRatio() { }
        public MapMode_SexRatio(MapModeDef def) : base(def) { }

        protected override Color[] BandColors => Bands;
        protected override float[] BandUpperBounds => Uppers;
        protected override float ValueFor(RegionDemographics demo) => demo.femaleFraction;

        protected override string LabelForRegion(RegionDemographics demo)
            => Mathf.RoundToInt(demo.femaleFraction * 100f) + "%";   // percent female

        protected override string SummaryFor(GeographicProvince province)
            => RegionDemographicsUtility.SexRatioSummary(province);
    }
}
