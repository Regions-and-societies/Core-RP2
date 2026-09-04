using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using RegionsAndSocieties.Economy;
using Verse;

namespace RegionsAndSocieties.UI
{
    /// <summary>
    /// The text blocks for the region panel's Region and Economy tabs (#26). Pulls everything the
    /// surface layer of the world knows about a province — biome, named features, natural-resource pools,
    /// wildlife — for the Region tab, and produced goods / trade / crises for the Economy tab. The
    /// demographic axes are drawn visually by <see cref="DemographicsPanel"/>, not here.
    /// </summary>
    public static class RegionPanelText
    {
        /// <summary>Identity and the named world features (e.g. a named woodland from worldgen) the region
        /// sits in — the surface geography of the place.</summary>
        public static string IdentityAndFeatures(GeographicProvince province)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Type: {province.provinceType}");
            sb.AppendLine($"Biome: {province.primaryBiome?.LabelCap ?? "Unknown"}");
            sb.AppendLine($"Tiles: {province.tiles.Count}");

            var mgr = Find.World?.GetComponent<SynapseRegionManager>();
            if (mgr != null)
                sb.AppendLine($"Worldgen: v{mgr.WorldGenVersionLabel}");

            List<string> features = NamedFeatures(province);
            if (features.Count > 0)
                sb.AppendLine("Named features: " + string.Join(", ", features));

            return sb.ToString().TrimEnd();
        }

        /// <summary>The distinct named world features (WorldFeature.name) covering this province's tiles.</summary>
        private static List<string> NamedFeatures(GeographicProvince province)
        {
            var names = new List<string>();
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || province.tiles == null) return names;

            var seen = new HashSet<string>();
            for (int i = 0; i < province.tiles.Count; i++)
            {
                int t = province.tiles[i];
                if (t < 0 || t >= grid.TilesCount) continue;
                WorldFeature f = grid[t].feature;
                if (f != null && !string.IsNullOrEmpty(f.name) && seen.Add(f.name))
                    names.Add(f.name);
            }
            return names;
        }

        /// <summary>Biome properties that describe what the land is like to live on — forage, wildlife
        /// and plant density, travel difficulty, disease pressure.</summary>
        public static string BiomeProperties(GeographicProvince province)
        {
            BiomeDef b = province.primaryBiome;
            if (b == null) return null;

            var sb = new StringBuilder();
            sb.AppendLine("--- Land ---");
            sb.AppendLine($"Forageability: {b.forageability:P0}");
            sb.AppendLine($"Wildlife density: {Descriptor(b.animalDensity)}");
            sb.AppendLine($"Plant density: {Descriptor(b.plantDensity)}");
            sb.AppendLine($"Movement difficulty: {b.movementDifficulty:0.0}x");
            if (b.diseaseMtbDays > 0f)
                sb.AppendLine($"Disease interval: ~{b.diseaseMtbDays:0} days");
            return sb.ToString().TrimEnd();
        }

        /// <summary>The natural-resource pools of the region — the raw wealth of the land, drawn down by
        /// consumption over time.</summary>
        public static string NaturalResources(GeographicProvince province)
        {
            if (!province.initializedEconomics) province.InitializeProvinceEconomics();

            var sb = new StringBuilder();
            sb.AppendLine("--- Natural resources ---");
            sb.AppendLine(ResourceDisplay.Line(province.Pool(ResourceKind.Nutrition), "Nutrition"));
            sb.AppendLine(ResourceDisplay.Line(province.Pool(ResourceKind.Biomass), "Biomass"));
            sb.AppendLine(ResourceDisplay.Line(province.Pool(ResourceKind.Minerals), "Minerals"));
            sb.AppendLine(ResourceDisplay.Line(province.Pool(ResourceKind.Textiles), "Textiles"));
            return sb.ToString().TrimEnd();
        }

        /// <summary>The characteristic wildlife of the region's biome — the top species by commonality.</summary>
        public static string Wildlife(GeographicProvince province)
        {
            BiomeDef b = province.primaryBiome;
            if (b == null) return null;

            var ranked = b.AllWildAnimals
                .Select(a => new { a, c = b.CommonalityOfAnimal(a) })
                .Where(x => x.c > 0f)
                .OrderByDescending(x => x.c)
                .Take(8)
                .Select(x => x.a.label.CapitalizeFirst())
                .ToList();
            if (ranked.Count == 0) return null;

            return "--- Wildlife ---\n" + string.Join(", ", ranked);
        }

        /// <summary>Manufactured goods, trade access, housing and any active crises — the region's economy.</summary>
        public static string Economy(GeographicProvince province)
        {
            if (!province.initializedEconomics) province.InitializeProvinceEconomics();

            var sb = new StringBuilder();
            sb.AppendLine("--- Population ---");
            sb.AppendLine($"Population: {province.currentPopulation}");
            sb.AppendLine($"Housing capacity: {province.totalDwellings * 2}");

            sb.AppendLine();
            sb.AppendLine("--- Manufactured goods ---");
            sb.AppendLine($"Pre-industrial: {province.preIndustrialGoods:F0}");
            sb.AppendLine($"Industrial: {province.industrialGoods:F0}");
            sb.AppendLine($"Spacer: {province.spacerGoods:F0}");

            if (province.activeCrises != null && province.activeCrises.Any())
            {
                sb.AppendLine();
                sb.AppendLine("--- Active crises ---");
                foreach (var crisis in province.activeCrises)
                    sb.AppendLine($"- {crisis.crisisType} (severity {crisis.currentSeverity:P0})");
            }
            return sb.ToString().TrimEnd();
        }

        private static string Descriptor(float v)
        {
            if (v <= 0f) return "none";
            if (v < 0.5f) return "sparse";
            if (v < 1.0f) return "moderate";
            if (v < 2.0f) return "high";
            return "very high";
        }
    }
}
