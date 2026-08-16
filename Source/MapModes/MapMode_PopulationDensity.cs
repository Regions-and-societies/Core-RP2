using MapModeFramework;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties
{
    [StaticConstructorOnStartup]
    public class MapMode_PopulationDensity : MapMode
    {
        private static Material[] densityMats = null;

        // The heatmap is normalised against the highest THEORETICAL settlement population in the world
        // (0.8): a tile at that ceiling is rich red, most settlements — which drift to two-thirds of
        // their cap — land in yellow, and small pawn-dwelling pockets stay a faded green. Five
        // fraction bands run green → yellow-green → yellow → orange → red, each darkened by elevation.
        private static readonly Color[] SegmentBase = new Color[]
        {
            new Color(0.35f, 0.65f, 0.25f, 0.50f),   // 0: faded green — low, pawn dwellings
            new Color(0.65f, 0.78f, 0.18f, 0.55f),   // 1: yellow-green
            new Color(0.92f, 0.85f, 0.12f, 0.60f),   // 2: yellow — where most settlements sit (~2/3 cap)
            new Color(0.95f, 0.50f, 0.10f, 0.65f),   // 3: orange
            new Color(0.90f, 0.12f, 0.10f, 0.70f)    // 4: rich red — at the theoretical max
        };

        // Fraction-of-reference-max thresholds for the five bands. Tuned in-game against the heatmap.
        private static readonly float[] SegmentThresholds = new float[] { 0.15f, 0.35f, 0.60f, 0.85f };

        public static void InitializeMaterials()
        {
            if (densityMats != null) return;

            // 5 density segments * 4 elevation bands = 20 materials.
            densityMats = new Material[20];

            for (int seg = 0; seg < 5; seg++)
            {
                Color baseColor = SegmentBase[seg];
                for (int band = 0; band < 4; band++)
                {
                    // Darken toward the mountains and lift alpha a little, so terrain still reads through.
                    float dim = 1f - 0.16f * band;
                    Color color = new Color(baseColor.r * dim, baseColor.g * dim, baseColor.b * dim,
                        Mathf.Min(0.9f, baseColor.a + 0.05f * band));

                    int index = seg * 4 + band;
                    densityMats[index] = null;
                    if (ShaderDatabase.MetaOverlay != null && BaseContent.WhiteTex != null)
                    {
                        densityMats[index] = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.MetaOverlay, color, 3510);
                    }
                    if (densityMats[index] == null)
                    {
                        densityMats[index] = SolidColorMaterials.SimpleSolidColorMaterial(color);
                    }
                    if (densityMats[index] == null)
                    {
                        densityMats[index] = BaseContent.WhiteMat;
                    }
                }
            }
        }

        // The density model lives in PopulationDensityUtility. This map mode used to carry a
        // near-verbatim clone of the whole thing (baseline, propagation, step multiplier), which
        // meant the heatmap and the inspect pane could disagree and every fix had to be made twice
        // (#62). It now reads the one shared cache; CacheData just makes sure it is warm.
        public static void CacheData()
        {
            InitializeMaterials();
            PopulationDensityUtility.EnsureCache();
        }

        public override WorldLayer_MapMode WorldLayer => WorldLayer_MapMode_Terrain.Instance;
        public override bool CanToggleWater => false;

        public override void DoPreRegenerate()
        {
            base.DoPreRegenerate();
            CacheData();
        }

        public MapMode_PopulationDensity() { }
        public MapMode_PopulationDensity(MapModeDef def) : base(def) { }

        public override Material GetMaterial(int tile)
        {
            if (Find.WorldGrid == null || tile >= Find.WorldGrid.TilesCount)
            {
                return BaseContent.ClearMat;
            }

            Tile tileData = Find.WorldGrid[tile];
            if (tileData.WaterCovered)
            {
                return BaseContent.ClearMat;
            }

            // Colour by the smeared influence field so the heatmap still fades outward from cities.
            int pop = PopulationDensityUtility.GetPopulationAtTile(tile);
            if (pop <= 0)
            {
                return BaseContent.ClearMat;   // no dwellings here — leave the terrain unshaded
            }

            // Normalise against the highest theoretical settlement population in this world (0.8), so
            // the ramp means the same thing regardless of the multiplier or the factions' tech levels.
            int referenceMax = Sizing.SettlementSizeUtility.ReferenceMaxPopulation();
            float fraction = referenceMax > 0 ? (float)pop / referenceMax : 0f;

            int densitySegment = 0;
            for (int s = SegmentThresholds.Length - 1; s >= 0; s--)
            {
                if (fraction >= SegmentThresholds[s]) { densitySegment = s + 1; break; }
            }

            float elevation = tileData.elevation;
            int elevationBand = 0;
            if (elevation >= 2200f) elevationBand = 3;
            else if (elevation >= 1200f) elevationBand = 2;
            else if (elevation >= 600f) elevationBand = 1;

            int index = densitySegment * 4 + elevationBand;

            if (densityMats == null || index >= densityMats.Length)
            {
                return BaseContent.ClearMat;
            }

            return densityMats[index];
        }

        public override string GetTileLabel(int tile)
        {
            // Label with the dwellings actually on the tile, not the smeared field (#55).
            int pop = PopulationDensityUtility.GetSourcePopulationAtTile(tile);
            return pop > 0 ? pop.ToString() : null;
        }

        public override string GetTooltip(int tile)
        {
            int pop = PopulationDensityUtility.GetSourcePopulationAtTile(tile);
            return pop > 0 ? $"Pawn dwellings: {pop}" : null;
        }
    }
}
