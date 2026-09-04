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
        // (0.8): a tile at that ceiling is bright yellow, most settlements — which drift to two-thirds of
        // their cap — land in the red-orange bands, and small pawn-dwelling pockets stay a faded violet.
        // Five fraction bands run violet → magenta → hot red → orange → bright yellow (the "magma" ramp),
        // each darkened by elevation. 0.3.0: this replaces green-to-red, which dissolved into the green
        // terrain; blue was tried and read as water. Violet/magenta/yellow occur nowhere in the planet's
        // own palette (green land, blue sea, tan desert, grey rock), and the ramp matches the density
        // heatmap in the mod's preview art.
        private static readonly Color[] SegmentBase = new Color[]
        {
            new Color(0.45f, 0.20f, 0.75f, 0.50f),   // 0: violet — low, pawn dwellings
            new Color(0.75f, 0.20f, 0.70f, 0.55f),   // 1: magenta
            new Color(0.95f, 0.30f, 0.42f, 0.60f),   // 2: hot red-pink — where most settlements sit (~2/3 cap)
            new Color(0.98f, 0.58f, 0.15f, 0.65f),   // 3: orange
            new Color(1.00f, 0.90f, 0.25f, 0.72f)    // 4: bright yellow — at the theoretical max
        };

        // Band thresholds on the LOG scale (fraction = log(1+pop) / log(1+densest tile)). Against a
        // 150-person core: violet up to ~3 people (a hamlet), magenta to ~11 (outskirts, pockets), red
        // to ~33 (a village core), orange to ~80 (a town core), yellow above (city and metropolis cores).
        private static readonly float[] SegmentThresholds = new float[] { 0.30f, 0.50f, 0.70f, 0.88f };

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

            // Normalise against the densest tile actually in the world (0.3.0): the sprawl field is
            // conserved, so a settlement tile holds its core share, never the theoretical cap, and a
            // fixed reference left the whole map in the bottom band. Relative to the densest tile, the
            // biggest city is always the top colour and everything else reads against it.
            // Logarithmic, not linear: a city's outskirts hold a few percent of its core, so a linear
            // scale put every tile but the core in the bottom band. On a log scale (against the densest
            // tile) a hamlet is violet, a village core red, a town core orange and only the biggest
            // cities yellow — the hotspots visibly step up through the ramp.
            int referenceMax = PopulationDensityUtility.MaxTilePopulation();
            float fraction = referenceMax > 1
                ? Mathf.Log(1f + pop) / Mathf.Log(1f + referenceMax)
                : (referenceMax > 0 ? (float)pop / referenceMax : 0f);

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
            // Every habitable tile answers, zero included, with its neighbours around it and the
            // hovered tile bolded in the middle — so an empty stretch of map reads as "0 here",
            // not as the tooltip being broken. Ocean and ice still show nothing.
            return PopulationDensityUtility.GetDwellingsDisplay(tile);
        }
    }
}
