using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties.UI
{
    public static class RegionalPieChartWindow
    {
        public static void DrawHoverWindow(GeographicProvince province, Vector2 mousePos)
        {
            if (province == null) return;
            var data = province.ownershipData ?? RegionalOwnershipUtility.CalculateOwnership(province);
            if (data == null) return;

            List<PieSlice> slices = BuildPieSlices(data);
            if (slices.Count == 0) return;

            float windowWidth = 260f;
            float windowHeight = 150f;
            float x = Mathf.Min(mousePos.x + 15f, Verse.UI.screenWidth - windowWidth - 10f);
            float y = Mathf.Min(mousePos.y + 15f, Verse.UI.screenHeight - windowHeight - 10f);
            Rect windowRect = new Rect(x, y, windowWidth, windowHeight);

            Find.WindowStack.ImmediateWindow(9928172, windowRect, WindowLayer.Super, () =>
            {
                Rect contentRect = new Rect(5f, 5f, windowWidth - 10f, windowHeight - 10f);
                Widgets.DrawBoxSolid(new Rect(0f, 0f, windowWidth, windowHeight), new Color(0.1f, 0.1f, 0.12f, 0.9f));
                Widgets.DrawBox(new Rect(0f, 0f, windowWidth, windowHeight));

                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(5f, 3f, contentRect.width, 20f), $"Influence: {province.name}");
                
                Text.Font = GameFont.Tiny;
                GUI.color = Color.yellow;
                string statusText = RegionalDomainUtility.GetStatusDescription(data);
                Widgets.Label(new Rect(5f, 22f, contentRect.width, 18f), statusText);
                GUI.color = Color.white;

                Rect chartRect = new Rect(10f, 42f, 90f, 90f);
                string cacheKey = $"Province_{province.id}_{data.factionScores.Count}_{data.unclaimedScore:F2}";
                PieChartDrawer.DrawPieChart(chartRect, slices, cacheKey);

                DrawLegend(new Rect(110f, 42f, windowWidth - 118f, 100f), slices);
            }, false, false, 1f);
        }

        public static List<PieSlice> BuildPieSlices(RegionalOwnershipData data)
        {
            List<PieSlice> slices = new List<PieSlice>();
            foreach (var fs in data.factionScores)
            {
                if (fs.TotalScore <= 0.001f) continue;
                Color factionColor = fs.faction?.Color ?? Color.cyan;
                slices.Add(new PieSlice
                {
                    label = fs.faction != null ? TextureUtility.GetFactionDisplayName(fs.faction) : "Faction",
                    fraction = fs.TotalScore,
                    color = factionColor
                });
            }

            if (data.unclaimedScore > 0.01f)
            {
                slices.Add(new PieSlice
                {
                    label = "Unclaimed",
                    fraction = data.unclaimedScore,
                    color = new Color(0.5f, 0.5f, 0.5f, 0.6f)
                });
            }
            return slices;
        }

        public static void DrawLegend(Rect rect, List<PieSlice> slices)
        {
            Text.Font = GameFont.Tiny;
            float y = rect.y;
            foreach (var slice in slices)
            {
                if (y + 18f > rect.yMax) break;
                Widgets.DrawBoxSolid(new Rect(rect.x, y + 2f, 10f, 10f), slice.color);
                string labelText = $"{slice.label}: {slice.fraction:P0}";
                Widgets.Label(new Rect(rect.x + 14f, y, rect.width - 14f, 18f), labelText);
                y += 18f;
            }
        }
    }
}
