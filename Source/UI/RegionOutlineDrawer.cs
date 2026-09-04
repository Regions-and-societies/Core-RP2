using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties.UI
{
    /// <summary>
    /// Draws a small map of a province inside a UI rect (#26): the region's hexes in its owner colour,
    /// ringed by a one-tile halo of its neighbours in THEIR colours (water and unclaimed land tinted too),
    /// so it reads in the context of what borders it. Every tile is projected through the SAME world
    /// camera the planet is drawn with (<see cref="GenWorldUI.WorldToUIPosition"/>), so the shape matches
    /// the region's on-screen orientation exactly. Perimeter tiles are shaded so the border reads. Cheap
    /// enough per-frame: a province and its ring are a few hundred tiles.
    /// </summary>
    [StaticConstructorOnStartup]   // builds the hex texture on the main thread at startup
    public static class RegionOutlineDrawer
    {
        private static readonly Color Background = new Color(0.10f, 0.11f, 0.13f, 1f);
        private static readonly Color WaterTint = new Color(0.24f, 0.34f, 0.50f);
        private static readonly Color UnclaimedLand = new Color(0.34f, 0.50f, 0.32f);
        private static readonly Color Unknown = new Color(0.42f, 0.44f, 0.46f);

        public static void Draw(Rect rect, GeographicProvince province, Color fill)
        {
            Widgets.DrawBoxSolid(rect, new Color(Background.r, Background.g, Background.b, 0.9f));
            Widgets.DrawBox(rect);

            WorldGrid grid = Find.WorldGrid;
            if (grid == null || province?.tiles == null || province.tiles.Count == 0) return;

            var mgr = Find.World?.GetComponent<SynapseRegionManager>();
            List<int> focal = province.tiles;
            var focalSet = new HashSet<int>(focal);

            // A one-ring halo of the neighbouring provinces' tiles, so the region reads in context.
            var ring = new List<int>();
            var ringSeen = new HashSet<int>();
            var nbBuf = new List<PlanetTile>();
            for (int i = 0; i < focal.Count; i++)
            {
                grid.GetTileNeighbors(focal[i], nbBuf);
                for (int k = 0; k < nbBuf.Count; k++)
                {
                    int nid = nbBuf[k].tileId;
                    if (focalSet.Contains(nid) || !ringSeen.Add(nid)) continue;
                    ring.Add(nid);
                }
            }

            // Project focal + ring together for one shared fit. UI space is y-down like our rect, so the
            // points normalise straight in with no flip and the map sits the same way up as the world.
            int fn = focal.Count, total = fn + ring.Count;
            var px = new float[total];
            var py = new float[total];
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < total; i++)
            {
                int tile = i < fn ? focal[i] : ring[i - fn];
                Vector2 ui = GenWorldUI.WorldToUIPosition(grid.GetTileCenter(tile));
                px[i] = ui.x; py[i] = ui.y;
                if (ui.x < minX) minX = ui.x; if (ui.x > maxX) maxX = ui.x;
                if (ui.y < minY) minY = ui.y; if (ui.y > maxY) maxY = ui.y;
            }

            float spanX = Mathf.Max(1e-4f, maxX - minX);
            float spanY = Mathf.Max(1e-4f, maxY - minY);
            const float pad = 6f;
            float scale = Mathf.Min((rect.width - 2f * pad) / spanX, (rect.height - 2f * pad) / spanY);
            float cell = Mathf.Clamp(GenWorldUI.CurUITileSize() * scale, 2f, rect.width * 0.25f);
            float offX = rect.x + (rect.width - spanX * scale) / 2f;
            float offY = rect.y + (rect.height - spanY * scale) / 2f;
            float hexSize = cell * 1.12f;   // slight overlap so the hexes tessellate without gaps

            var perimeter = province.perimeterTiles != null ? new HashSet<int>(province.perimeterTiles) : null;
            Color edge = new Color(fill.r * 0.55f, fill.g * 0.55f, fill.b * 0.55f, 1f);

            // Neighbours first (behind), dimmed toward the background so the focal region reads on top.
            for (int i = fn; i < total; i++)
            {
                Color nc = Color.Lerp(Background, ColorForTile(mgr, ring[i - fn]), 0.62f);
                DrawHex(offX + (px[i] - minX) * scale, offY + (py[i] - minY) * scale, hexSize, nc);
            }

            // Focal region on top; perimeter tiles shaded so its own border reads against the halo.
            for (int i = 0; i < fn; i++)
            {
                Color c = perimeter != null && perimeter.Contains(focal[i]) ? edge : fill;
                DrawHex(offX + (px[i] - minX) * scale, offY + (py[i] - minY) * scale, hexSize, c);
            }
            GUI.color = Color.white;
        }

        private static void DrawHex(float cx, float cy, float size, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(new Rect(cx - size / 2f, cy - size / 2f, size, size), HexTex);
        }

        /// <summary>The map colour of the province a tile belongs to: its owner's colour on land, a water
        /// tint for ocean/lake/river, and greens/greys for unclaimed or unknown.</summary>
        private static Color ColorForTile(SynapseRegionManager mgr, int tile)
        {
            GeographicProvince p = mgr?.GetProvinceForTile(tile);
            if (p == null) return Unknown;
            if (p.provinceType != ProvinceType.Land) return WaterTint;
            Faction owner = p.ownershipData?.PrimaryOwner;
            return owner != null ? owner.Color : UnclaimedLand;
        }

        // A white pointy-top hexagon on transparent, built once at startup and tinted per tile at draw
        // time. Point-in-convex-polygon against the six edges (vertices wound CCW, interior on the left).
        private static readonly Texture2D HexTex = BuildHexTexture();
        private static Texture2D BuildHexTexture()
        {
            const int S = 48;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            float cx = (S - 1) / 2f, cy = (S - 1) / 2f, R = S / 2f - 1f;

            var vx = new float[6];
            var vy = new float[6];
            for (int i = 0; i < 6; i++)
            {
                float ang = (90f + 60f * i) * Mathf.Deg2Rad;   // pointy top
                vx[i] = cx + Mathf.Cos(ang) * R;
                vy[i] = cy + Mathf.Sin(ang) * R;
            }

            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    bool inside = true;
                    for (int i = 0; i < 6; i++)
                    {
                        int j = (i + 1) % 6;
                        float ex = vx[j] - vx[i], ey = vy[j] - vy[i];
                        float rx = x - vx[i], ry = y - vy[i];
                        if (ex * ry - ey * rx < 0f) { inside = false; break; }   // right of a CCW edge = outside
                    }
                    px[y * S + x] = inside ? Color.white : Color.clear;
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }
}
