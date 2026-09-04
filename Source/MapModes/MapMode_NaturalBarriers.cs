using System.Collections.Generic;
using MapModeFramework;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties
{
    /// <summary>
    /// A debug overlay (#20) that paints every land tile by its BIOME — each biome its own colour — with
    /// the partition's two hard non-biome walls, impassable rock and open water, shown distinctly on top.
    /// Because biomes are the thing the contain-then-subdivide partition actually walls on, this shows the
    /// real division of the map: a region border that follows a biome colour change is a true biome edge;
    /// one that runs through a single colour is a same-biome size cut. It also labels each tile with its
    /// actual biome, so a tile can never be misread (e.g. an AridShrubland tile mislabelled "swamp").
    ///
    /// <para>Materials are pre-built on the main thread in <see cref="DoPreRegenerate"/> — one per biome
    /// plus impassable/water — so the worker-thread mesh build (<c>GetMaterial</c>) only ever reads them;
    /// Unity forbids creating a material off the main thread.</para>
    /// </summary>
    [StaticConstructorOnStartup]
    public class MapMode_NaturalBarriers : MapMode
    {
        private static Material impassableMat;
        private static Material waterMat;
        private static Dictionary<BiomeDef, Material> biomeMats;

        public MapMode_NaturalBarriers() { }
        public MapMode_NaturalBarriers(MapModeDef def) : base(def) { }

        public override WorldLayer_MapMode WorldLayer => WorldLayer_MapMode_Terrain.Instance;

        public override void DoPreRegenerate()
        {
            base.DoPreRegenerate();
            if (biomeMats != null) return;

            impassableMat = MakeMat(new Color(0.10f, 0.10f, 0.12f, 0.92f));   // near-black rock / sea ice
            waterMat = MakeMat(new Color(0.20f, 0.42f, 0.75f, 0.85f));        // blue

            biomeMats = new Dictionary<BiomeDef, Material>();
            var biomes = DefDatabase<BiomeDef>.AllDefsListForReading;
            for (int i = 0; i < biomes.Count; i++)
            {
                BiomeDef b = biomes[i];
                biomeMats[b] = MakeMat(ColorForBiome(b, i));
            }
        }

        private static Material MakeMat(Color c)
        {
            Material m = (ShaderDatabase.MetaOverlay != null && BaseContent.WhiteTex != null)
                ? MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.MetaOverlay, c, 3510)
                : SolidColorMaterials.SimpleSolidColorMaterial(c);
            return m ?? BaseContent.WhiteMat;
        }

        public override Material GetMaterial(int tile)
        {
            if (biomeMats == null) return BaseContent.ClearMat;   // pre-build hasn't run; never create off-thread
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || tile < 0 || tile >= grid.TilesCount) return BaseContent.ClearMat;
            Tile t = grid[tile];
            if (t == null) return BaseContent.ClearMat;

            BiomeDef biome = t.PrimaryBiome;
            if (t.hilliness == Hilliness.Impassable || (biome != null && (biome.impassable || biome.defName == "SeaIce")))
                return impassableMat;
            if (t.WaterCovered) return waterMat;
            if (biome != null && biomeMats.TryGetValue(biome, out var m)) return m;
            return BaseContent.ClearMat;
        }

        public override string GetTileLabel(int tile)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || tile < 0 || tile >= grid.TilesCount) return null;
            Tile t = grid[tile];
            return t?.PrimaryBiome?.label;
        }

        public override string GetTooltip(int tile)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || tile < 0 || tile >= grid.TilesCount) return null;
            Tile t = grid[tile];
            if (t == null) return null;
            BiomeDef biome = t.PrimaryBiome;
            string name = biome != null ? biome.label + " (" + biome.defName + ")" : "no biome";
            bool impassable = t.hilliness == Hilliness.Impassable || (biome != null && (biome.impassable || biome.defName == "SeaIce"));
            if (t.WaterCovered) return name + " — water (partition wall)";
            if (impassable) return name + " — impassable (partition wall)";
            return name + " — " + t.hilliness;
        }

        /// <summary>A stable, reasonably intuitive colour per biome: curated tones for the vanilla biomes
        /// (deserts sandy, forests green, cold pale) so the map reads naturally, and an even golden-ratio hue
        /// spread for anything else (modded biomes) so no two adjacent unknowns collide.</summary>
        private static Color ColorForBiome(BiomeDef b, int index)
        {
            if (b != null)
            {
                switch (b.defName)
                {
                    case "Desert":             return new Color(0.83f, 0.69f, 0.40f, 0.80f);
                    case "ExtremeDesert":      return new Color(0.93f, 0.82f, 0.52f, 0.80f);
                    case "AridShrubland":      return new Color(0.68f, 0.66f, 0.34f, 0.80f);
                    case "TemperateForest":    return new Color(0.28f, 0.55f, 0.24f, 0.80f);
                    case "TemperateSwamp":     return new Color(0.20f, 0.40f, 0.30f, 0.80f);
                    case "TropicalRainforest": return new Color(0.12f, 0.45f, 0.18f, 0.80f);
                    case "TropicalSwamp":      return new Color(0.16f, 0.42f, 0.36f, 0.80f);
                    case "BorealForest":       return new Color(0.30f, 0.50f, 0.45f, 0.80f);
                    case "ColdBog":            return new Color(0.34f, 0.46f, 0.48f, 0.80f);
                    case "Tundra":             return new Color(0.55f, 0.58f, 0.55f, 0.80f);
                    case "IceSheet":           return new Color(0.85f, 0.90f, 0.95f, 0.80f);
                }
            }
            float hue = (index * 0.61803398875f) % 1f;   // golden-ratio hue spread for modded biomes
            Color c = Color.HSVToRGB(hue, 0.55f, 0.78f);
            c.a = 0.80f;
            return c;
        }
    }
}
