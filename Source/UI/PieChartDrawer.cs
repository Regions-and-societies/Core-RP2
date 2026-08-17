using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties.UI
{
    public class PieSlice
    {
        public string label;
        public float fraction; // 0.0 to 1.0
        public Color color;
    }

    public static class PieChartDrawer
    {
        private static readonly Dictionary<string, Texture2D> chartTextureCache = new Dictionary<string, Texture2D>();

        public static Texture2D GetOrCreatePieChartTexture(string cacheKey, List<PieSlice> slices, int width = 96, int height = 96)
        {
            if (!string.IsNullOrEmpty(cacheKey) && chartTextureCache.TryGetValue(cacheKey, out Texture2D existing) && existing != null)
            {
                return existing;
            }

            Texture2D tex = GeneratePieChartTexture(slices, width, height);
            if (!string.IsNullOrEmpty(cacheKey))
            {
                chartTextureCache[cacheKey] = tex;
            }
            return tex;
        }

        private static Texture2D GeneratePieChartTexture(List<PieSlice> slices, int width, int height)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            Color[] pixels = new Color[width * height];
            float centerX = width / 2f;
            float centerY = height / 2f;
            float radius = Mathf.Min(centerX, centerY) - 2f;

            float totalFraction = slices?.Sum(s => s.fraction) ?? 0f;
            if (totalFraction <= 0.001f)
            {
                for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;
                tex.SetPixels(pixels);
                tex.Apply();
                return tex;
            }

            // Build angle ranges
            List<Tuple<float, float, Color>> ranges = BuildAngleRanges(slices, totalFraction);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float dx = x - centerX;
                    float dy = y - centerY;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    if (dist > radius + 1f)
                    {
                        pixels[y * width + x] = Color.clear;
                    }
                    else
                    {
                        float angle = Mathf.Atan2(dy, dx);
                        if (angle < 0f) angle += 2f * Mathf.PI;

                        Color c = GetColorForAngle(angle, ranges);
                        if (dist > radius - 1f)
                        {
                            float alpha = Mathf.Clamp01(radius + 1f - dist);
                            c.a *= alpha;
                        }
                        pixels[y * width + x] = c;
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private static List<Tuple<float, float, Color>> BuildAngleRanges(List<PieSlice> slices, float totalFraction)
        {
            List<Tuple<float, float, Color>> ranges = new List<Tuple<float, float, Color>>();
            float currentAngle = 0f;

            foreach (var slice in slices)
            {
                if (slice.fraction <= 0f) continue;
                float sweep = (slice.fraction / totalFraction) * (2f * Mathf.PI);
                ranges.Add(new Tuple<float, float, Color>(currentAngle, currentAngle + sweep, slice.color));
                currentAngle += sweep;
            }
            return ranges;
        }

        private static Color GetColorForAngle(float angle, List<Tuple<float, float, Color>> ranges)
        {
            foreach (var r in ranges)
            {
                if (angle >= r.Item1 && angle <= r.Item2)
                {
                    return r.Item3;
                }
            }
            return ranges.LastOrDefault()?.Item3 ?? Color.gray;
        }

        public static void DrawPieChart(Rect rect, List<PieSlice> slices, string cacheKey)
        {
            if (slices == null || slices.Count == 0) return;
            Texture2D tex = GetOrCreatePieChartTexture(cacheKey, slices, (int)rect.width, (int)rect.height);
            if (tex != null)
            {
                GUI.DrawTexture(rect, tex);
            }
        }
    }
}
