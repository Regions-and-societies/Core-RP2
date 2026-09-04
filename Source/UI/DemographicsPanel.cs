using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using RegionsAndSocieties.Demographics;

namespace RegionsAndSocieties.UI
{
    /// <summary>
    /// The visual demographic inspection panel (#26) — the user-facing face of the demographic models.
    /// Renders a <see cref="RegionDemographics"/> as labelled charts: stacked bars for the band/tier
    /// axes (age, sex, education, socioeconomic, employment) and pies for the categorical ones
    /// (xenotype, ideology). Degrades cleanly — no Biotech / no Ideology axes say so rather than drawing
    /// a blank chart, and an unsettled region renders a single "no population" line. Pure layout over a
    /// derived aggregate, so the same panel can later render a tile sample.
    /// </summary>
    public static class DemographicsPanel
    {
        private const float SectionGap = 10f;
        private const float HeaderH = 20f;
        private const float PieSize = 66f;

        /// <summary>
        /// Draw the panel from the top of <paramref name="rect"/> and return the height used, so a
        /// scrolling host can size its view. <paramref name="cacheKeyBase"/> (e.g. the province id) keys
        /// the pie textures; include a mix signature so a changed make-up rebuilds them.
        /// </summary>
        public static float Draw(Rect rect, RegionDemographics demo, int population, string cacheKeyBase)
        {
            float y = rect.y;
            if (demo == null || demo.settledTiles <= 0)
            {
                Widgets.Label(new Rect(rect.x, y, rect.width, HeaderH), "No settled population in this region.");
                return HeaderH;
            }

            // Residences: the region's people resolved into homes by how urban it is (population is a head
            // count; residences are where they live). Rural extended families to dense urban households.
            if (population > 0)
            {
                ResidenceProfile res = ResidenceRules.For(population);
                y = NoteSection(rect, y, $"Residences  —  {res.tier}",
                    $"{res.residences} homes · {res.occupancy:0.0} people per home · {population} residents\nland per person {res.landPerPawn:0.00} (relative)");
                y += SectionGap;
            }

            y = BarSection(rect, y, $"Age  —  median {demo.medianAge}", new List<BarSegment>
            {
                new BarSegment("Children", demo.ageShares[(int)AgeBucket.Child], DemographicColors.Age[0]),
                new BarSegment("Working-age", demo.ageShares[(int)AgeBucket.WorkingAge], DemographicColors.Age[1]),
                new BarSegment("Elders", demo.ageShares[(int)AgeBucket.Elder], DemographicColors.Age[2]),
            });

            y = BarSection(rect, y, "Sex ratio", new List<BarSegment>
            {
                new BarSegment("Female", demo.femaleFraction, DemographicColors.Female),
                new BarSegment("Male", 1f - demo.femaleFraction, DemographicColors.Male),
            });

            // Education — each band carries the real-world level's meaning (skills, passion, economic
            // role) from EducationRules.Profiles, and the header names the level most people reached.
            EducationProfile[] edu = EducationRules.Profiles;
            int eduModal = ModalIndex(demo.educationShares);
            y = BarSection(rect, y, $"Education  —  index {demo.educationIndex}/100 · mostly {edu[eduModal].label}", new List<BarSegment>
            {
                new BarSegment("Illiterate", demo.educationShares[(int)EducationTier.Illiterate], DemographicColors.Education[0], EduTip(edu, (int)EducationTier.Illiterate)),
                new BarSegment("Primary", demo.educationShares[(int)EducationTier.Primary], DemographicColors.Education[1], EduTip(edu, (int)EducationTier.Primary)),
                new BarSegment("Secondary", demo.educationShares[(int)EducationTier.Secondary], DemographicColors.Education[2], EduTip(edu, (int)EducationTier.Secondary)),
                new BarSegment("Undergrad", demo.educationShares[(int)EducationTier.Undergrad], DemographicColors.Education[3], EduTip(edu, (int)EducationTier.Undergrad)),
                new BarSegment("Postgrad", demo.educationShares[(int)EducationTier.Postgrad], DemographicColors.Education[4], EduTip(edu, (int)EducationTier.Postgrad)),
            });

            y = BarSection(rect, y, $"Socioeconomic  —  index {demo.sesIndex}/100", new List<BarSegment>
            {
                new BarSegment("Subsistence", demo.sesShares[(int)SesTier.Subsistence], DemographicColors.Ses[0], "hand-to-mouth; no surplus to invest"),
                new BarSegment("Modest", demo.sesShares[(int)SesTier.Modest], DemographicColors.Ses[1], "getting by; small savings"),
                new BarSegment("Prosperous", demo.sesShares[(int)SesTier.Prosperous], DemographicColors.Ses[2], "comfortable; disposable income"),
                new BarSegment("Affluent", demo.sesShares[(int)SesTier.Affluent], DemographicColors.Ses[3], "wealthy; capital to invest"),
            });

            y = BarSection(rect, y, $"Employment  —  {demo.employmentRate}% employed", new List<BarSegment>
            {
                new BarSegment("Agriculture", demo.occupationShares[(int)OccupationSector.Agriculture], DemographicColors.Employment[0], "farming, foraging, herding — working the land"),
                new BarSegment("Industry", demo.occupationShares[(int)OccupationSector.Industry], DemographicColors.Employment[1], "mining, crafting, manufacture"),
                new BarSegment("Military", demo.occupationShares[(int)OccupationSector.Military], DemographicColors.Employment[2], "garrisons, standing forces"),
                new BarSegment("Trade", demo.occupationShares[(int)OccupationSector.Trade], DemographicColors.Employment[3], "merchants, hauling, services"),
            });

            // Xenotype and ideology are DLC features: with Biotech / Ideology absent the whole section is
            // omitted (not shown as a "not active" note), so the panel degrades to exactly what this game
            // can express.
            if (demo.biotechActive)
            {
                y += SectionGap;
                if (demo.raceShares.Count == 0)
                    y = NoteSection(rect, y, "Xenotypes", "No data.");
                else
                    y = PieSection(rect, y, "Xenotypes", demo.raceShares
                        .OrderByDescending(k => k.Value)
                        .Select(k => new PieSlice { label = k.Key.LabelCap, fraction = k.Value, color = DemographicColors.Xenotype(k.Key) })
                        .ToList(), cacheKeyBase + "_xeno");
            }

            if (demo.ideologyActive)
            {
                y += SectionGap;
                if (demo.ideoShares.Count == 0)
                    y = NoteSection(rect, y, "Ideology", "No data.");
                else
                    y = PieSection(rect, y, "Ideology", demo.ideoShares
                        .OrderByDescending(k => k.Value)
                        .Select(k => new PieSlice { label = k.Key.name, fraction = k.Value, color = DemographicColors.Ideology(k.Key) })
                        .ToList(), cacheKeyBase + "_ideo");
            }

            return y - rect.y;
        }

        /// <summary>The hover context for an education band: the skills and passion a pawn of that level
        /// brings (feeds #28) and the economic capability it unlocks (feeds the 0.4.0 economy).</summary>
        private static string EduTip(EducationProfile[] p, int tier)
            => $"skills {p[tier].skillLow}–{p[tier].skillHigh}, {p[tier].passion}\n{p[tier].economicRole}";

        /// <summary>Index of the largest share — the tier/band most people fall in.</summary>
        private static int ModalIndex(float[] shares)
        {
            int best = 0;
            float bv = -1f;
            if (shares != null)
                for (int i = 0; i < shares.Length; i++)
                    if (shares[i] > bv) { bv = shares[i]; best = i; }
            return best;
        }

        private static float BarSection(Rect rect, float y, string header, List<BarSegment> segs)
        {
            y += SectionGap;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(rect.x, y, rect.width, HeaderH), header);
            y += HeaderH;

            DemographicBars.DrawStackedBar(new Rect(rect.x, y, rect.width, DemographicBars.BarHeight), segs);
            y += DemographicBars.BarHeight + 2f;
            y += DemographicBars.DrawSwatchLegend(new Rect(rect.x, y, rect.width, 40f), segs);
            return y;
        }

        private static float PieSection(Rect rect, float y, string header, List<PieSlice> slices, string cacheKeyBase)
        {
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(rect.x, y, rect.width, HeaderH), header);
            y += HeaderH;

            var pieRect = new Rect(rect.x, y, PieSize, PieSize);
            // Signature the key on the mix so a changed make-up (e.g. via population growth) rebuilds it.
            string key = $"{cacheKeyBase}_{slices.Count}_{(slices.Count > 0 ? slices[0].fraction : 0f):F2}";
            PieChartDrawer.DrawPieChart(pieRect, slices, key);
            RegionalPieChartWindow.DrawLegend(new Rect(pieRect.xMax + 10f, y, rect.width - PieSize - 10f, PieSize), slices);
            return y + PieSize + 2f;
        }

        private static float NoteSection(Rect rect, float y, string header, string note)
        {
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(rect.x, y, rect.width, HeaderH), header);
            y += HeaderH;

            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(rect.x, y, rect.width, HeaderH), note);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            return y + HeaderH;
        }
    }
}
