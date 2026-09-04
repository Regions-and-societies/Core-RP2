using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties.UI
{
    /// <summary>One coloured segment of a stacked demographic bar (#26).</summary>
    public struct BarSegment
    {
        public string label;
        public float fraction;   // 0..1, of the whole bar
        public Color color;
        public string tooltip;   // optional context shown on hover; falls back to "label: pct"

        public BarSegment(string label, float fraction, Color color, string tooltip = null)
        {
            this.label = label;
            this.fraction = fraction;
            this.color = color;
            this.tooltip = tooltip;
        }
    }

    /// <summary>
    /// The reusable visual primitive behind the demographic inspection panel (#26): a horizontal stacked
    /// bar for the band/tier axes (age, sex, education, socioeconomic, employment), plus a compact swatch
    /// legend beneath it. The categorical axes (xenotype, ideology) use <see cref="PieChartDrawer"/>
    /// instead. Pure drawing — hand it segments, it renders them; no game state, so the same primitive
    /// serves a region panel and, later, a tile readout.
    /// </summary>
    public static class DemographicBars
    {
        public const float BarHeight = 18f;
        private static readonly Color BarBackground = new Color(0.14f, 0.14f, 0.16f, 0.9f);
        private static readonly Color BarBorder = new Color(0f, 0f, 0f, 0.5f);

        /// <summary>
        /// Draw a proportional stacked bar filling <paramref name="rect"/>. Each segment takes width in
        /// proportion to its fraction; a non-zero segment always gets at least a visible sliver so a
        /// small-but-present tier does not vanish. Hovering a segment shows its label and share. Renders
        /// a faint "no data" when nothing sums above zero.
        /// </summary>
        public static void DrawStackedBar(Rect rect, IList<BarSegment> segments)
        {
            Widgets.DrawBoxSolid(rect, BarBackground);

            float total = 0f;
            if (segments != null)
                for (int i = 0; i < segments.Count; i++)
                    if (segments[i].fraction > 0f) total += segments[i].fraction;

            if (total <= 0.0001f)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(1f, 1f, 1f, 0.5f);
                Widgets.Label(rect, "no data");
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                DrawBorder(rect);
                return;
            }

            const float minSliver = 2f;
            float x = rect.x;
            for (int i = 0; i < segments.Count; i++)
            {
                BarSegment seg = segments[i];
                if (seg.fraction <= 0f) continue;

                float w = rect.width * (seg.fraction / total);
                if (w < minSliver) w = minSliver;
                if (x + w > rect.xMax) w = rect.xMax - x;
                if (w <= 0f) break;

                Rect segRect = new Rect(x, rect.y, w, rect.height);
                Widgets.DrawBoxSolid(segRect, seg.color);
                if (Mouse.IsOver(segRect))
                {
                    Widgets.DrawBox(segRect);
                    string tip = string.IsNullOrEmpty(seg.tooltip)
                        ? $"{seg.label}: {seg.fraction:P0}"
                        : $"{seg.label} — {seg.fraction:P0}\n{seg.tooltip}";
                    TooltipHandler.TipRegion(segRect, tip);
                }
                x += w;
            }

            DrawBorder(rect);
        }

        private static void DrawBorder(Rect rect)
        {
            GUI.color = BarBorder;
            Widgets.DrawBox(rect);
            GUI.color = Color.white;
        }

        /// <summary>
        /// A compact wrapped legend of swatch + "label pct" for the segments of a bar, laid out left to
        /// right and wrapping within <paramref name="rect"/>. Returns the height used so the caller can
        /// advance its layout cursor.
        /// </summary>
        public static float DrawSwatchLegend(Rect rect, IList<BarSegment> segments)
        {
            Text.Font = GameFont.Tiny;
            const float rowH = 16f, swatch = 9f, colGap = 12f;
            float x = rect.x, y = rect.y;

            for (int i = 0; i < segments.Count; i++)
            {
                BarSegment seg = segments[i];
                if (seg.fraction <= 0f) continue;

                string text = $"{seg.label} {seg.fraction:P0}";
                float w = swatch + 4f + Text.CalcSize(text).x + colGap;
                if (x + w > rect.xMax && x > rect.x) { x = rect.x; y += rowH; }

                Widgets.DrawBoxSolid(new Rect(x, y + 3f, swatch, swatch), seg.color);
                Widgets.Label(new Rect(x + swatch + 4f, y, w - swatch - 4f, rowH), text);
                x += w;
            }

            Text.Font = GameFont.Small;
            return (y - rect.y) + rowH;
        }
    }
}
