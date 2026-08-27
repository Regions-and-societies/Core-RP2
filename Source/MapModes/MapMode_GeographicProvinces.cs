using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MapModeFramework;
using Region = MapModeFramework.Region;
using RegionsAndSocieties.Economy;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties
{
    public class MapMode_GeographicProvinces : MapMode_Region
    {
        public override Material RegionMaterial => BaseContent.ClearMat;

        public MapMode_GeographicProvinces() { }
        public MapMode_GeographicProvinces(MapModeDef def) : base(def) { }

        /// <summary>
        /// When true the region bodies are left transparent and only their outlines are drawn, so the
        /// division lines overlay the real terrain (biomes, hills) instead of washing it out (#53).
        /// </summary>
        protected virtual bool BordersOnly => false;

        public override void SetRegions()
        {
            if (!UnityData.IsInMainThread) return;

            ClearCache();
            regions.Clear();

            if (Find.World == null) return;
            var regionManager = Find.World.GetComponent<SynapseRegionManager>();
            if (regionManager == null) return;

            regionManager.RecalculateProvinceOwners();

            foreach (var province in regionManager.Provinces)
            {
                if (province.tiles.Count == 0) continue;

                // Borders-only overlay leaves every body transparent; the coloured fills below are only
                // for the standard "regions" view.
                Material bodyMat = BaseContent.ClearMat;
                if (!BordersOnly)
                {
                    if (province.provinceType == ProvinceType.Ocean)
                    {
                        bodyMat = BaseContent.ClearMat;
                    }
                    else if (province.provinceType == ProvinceType.Lake)
                    {
                        bodyMat = SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.12f, 0.45f, 0.8f, 0.25f));
                    }
                    else if (province.provinceType == ProvinceType.River)
                    {
                        bodyMat = SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.12f, 0.65f, 0.8f, 0.3f));
                    }
                    else if (province.provinceType == ProvinceType.MountainRange)
                    {
                        bodyMat = SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.48f, 0.48f, 0.48f, 0.35f));
                    }
                    else
                    {
                        Color uniqueColor = GetUniqueColor(province.id);
                        bodyMat = SolidColorMaterials.SimpleSolidColorMaterial(uniqueColor);
                    }
                }

                // A borders-only overlay has no fill to read against, so draw the outline at full
                // opacity; the filled view keeps the softer border so the colours stay legible.
                Color borderColor = BordersOnly ? new Color(1f, 1f, 1f, 1f) : new Color(1f, 1f, 1f, 0.8f);
                Material borderMat = null;
                if (ShaderDatabase.MetaOverlay != null && BaseContent.WhiteTex != null)
                {
                    borderMat = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.MetaOverlay, borderColor, 3510);
                }
                if (borderMat == null)
                {
                    borderMat = SolidColorMaterials.SimpleSolidColorMaterial(borderColor);
                }
                if (borderMat == null)
                {
                    borderMat = BaseContent.WhiteMat;
                }

                float borderWidth = RegionPropertiesAccess.BorderWidth(def);
                bool doBorders = RegionPropertiesAccess.DoBorders(def);

                string ownerString = "Unaffiliated Wilderness";
                if (province.owningFactionIds.Any())
                {
                    var factions = province.owningFactionIds
                        .Select(id => Find.FactionManager.AllFactions.FirstOrDefault(f => f.GetUniqueLoadID() == id))
                        .Where(f => f != null)
                        .ToList();

                    if (factions.Count == 1)
                    {
                        ownerString = "Domain of: " + TextureUtility.GetFactionDisplayName(factions[0]);
                    }
                    else if (factions.Count > 1)
                    {
                        ownerString = "Contested Domain of:\n" + string.Join("\n", factions.Select(f => "- " + TextureUtility.GetFactionDisplayName(f)));
                    }
                }

                string tooltipText = $"Region: {province.name}\nType: {province.provinceType}\nBiome: {province.primaryBiome?.LabelCap ?? "Unknown"}\nStatus: {ownerString}\nTiles: {province.tiles.Count}";

                Region region = new Region(
                    province.name,
                    province.tiles,
                    false,
                    bodyMat,
                    doBorders,
                    borderMat,
                    borderWidth,
                    tooltipText
                );

                regions.Add(region);
            }
        }

        // Retained only for its static tooltip helpers; this mode is no longer registered (the section
        // is Territories + Population). Selection/hover UI lives on MapMode_FactionTerritory.
        public override string GetTooltip(int tile)
        {
            if (Find.World == null) return base.GetTooltip(tile);
            var regionManager = Find.World.GetComponent<SynapseRegionManager>();
            var province = regionManager?.GetProvinceForTile(tile);
            if (province == null) return base.GetTooltip(tile);
            return GetProvinceTooltipShort(province);
        }

        public static string GetProvinceTooltip(GeographicProvince province, int tile)
        {
            if (province == null) return string.Empty;

            if (!province.initializedEconomics)
            {
                province.InitializeProvinceEconomics();
            }

            var data = province.ownershipData ?? RegionalOwnershipUtility.CalculateOwnership(province);

            string ownerString = RegionalDomainUtility.GetStatusDescription(data);

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine($"Region: {province.name}");
            sb.AppendLine($"Type: {province.provinceType}");
            sb.AppendLine($"Biome: {province.primaryBiome?.LabelCap ?? "Unknown"}");
            sb.AppendLine($"Status: {ownerString}");
            sb.AppendLine($"Tiles: {province.tiles.Count}");
            
            if (data != null && data.factionScores.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("--- Regional Influence Breakdown ---");
                foreach (var fs in data.factionScores)
                {
                    if (fs.TotalScore < 0.01f) continue;   // hide negligible border grazes (a few edges)
                    string fname = fs.faction != null ? TextureUtility.GetFactionDisplayName(fs.faction) : "Unknown";
                    sb.AppendLine($"- {fname}: {fs.TotalScore:P0}");
                    sb.AppendLine($"  (Settlements: {fs.settlementScore:P0}, Borders: {fs.perimeterCoverageScore + fs.externalPerimeterScore:P0}, Outposts: {fs.outpostCoverageScore + fs.mostOutpostsScore:P0}, Ideology: {fs.demographicScore:P0})");
                }
                if (data.unclaimedScore > 0.01f)
                {
                    sb.AppendLine($"- Unclaimed: {data.unclaimedScore:P0}");
                }

                // The ownership derivation is a developer readout — show it only when the mod option is
                // explicitly on (not merely because Dev Mode is), and only here in the expanded panel,
                // never in the hover tooltip (#53/#54).
                if (FactionPlacementSettings.showCalculationBreakdowns && !string.IsNullOrEmpty(data.debugBreakdown))
                {
                    sb.AppendLine();
                    sb.Append(data.debugBreakdown);
                }
            }

            sb.AppendLine();
            sb.AppendLine("--- Regional Economics ---");
            sb.AppendLine($"Population: {province.currentPopulation}");
            sb.AppendLine($"Housing Capacity: {province.totalDwellings * 2}");
            // 0.7: show the stock against the ceiling the terrain imposes. A bare number cannot
            // tell the player whether a province is rich or merely untouched, and depletion is
            // invisible without something to measure it against.
            sb.AppendLine(ResourceDisplay.Line(province.Pool(ResourceKind.Nutrition), "Nutrition"));
            sb.AppendLine(ResourceDisplay.Line(province.Pool(ResourceKind.Biomass), "Biomass"));
            sb.AppendLine(ResourceDisplay.Line(province.Pool(ResourceKind.Minerals), "Minerals"));
            sb.AppendLine(ResourceDisplay.Line(province.Pool(ResourceKind.Textiles), "Textiles"));
            sb.AppendLine();
            sb.AppendLine("--- Produced Goods ---");
            sb.AppendLine($"Pre-Industrial Goods: {province.preIndustrialGoods:F0}");
            sb.AppendLine($"Industrial Goods: {province.industrialGoods:F0}");
            sb.AppendLine($"Spacer Goods: {province.spacerGoods:F0}");

            // Demographic reads (#10 age, #11 sex, #12 xenotypes, #13 ideology, #14 wealth, #15
            // education): deterministic, each self-labeled so they need no extra header. Sex shows any
            // active draft/war skew inline; ideology shows belief similarity to neighbours.
            string ageSummary = Demographics.RegionDemographicsUtility.AgeStructureSummary(province);
            string sexSummary = Demographics.RegionDemographicsUtility.SexRatioSummary(province);
            string xenoSummary = Demographics.RegionDemographicsUtility.XenotypeSummary(province);
            string ideoSummary = Demographics.RegionDemographicsUtility.IdeologySummary(province);
            string eduSummary = Demographics.RegionDemographicsUtility.EducationSummary(province);
            string sesSummary = Demographics.RegionDemographicsUtility.SocioeconomicSummary(province);
            string employSummary = Demographics.RegionDemographicsUtility.EmploymentSummary(province);
            if (!string.IsNullOrEmpty(ageSummary) || !string.IsNullOrEmpty(sexSummary)
                || !string.IsNullOrEmpty(xenoSummary) || !string.IsNullOrEmpty(ideoSummary)
                || !string.IsNullOrEmpty(eduSummary) || !string.IsNullOrEmpty(sesSummary)
                || !string.IsNullOrEmpty(employSummary))
            {
                sb.AppendLine();
                if (!string.IsNullOrEmpty(ageSummary)) sb.AppendLine(ageSummary);
                if (!string.IsNullOrEmpty(sexSummary)) sb.AppendLine(sexSummary);
                if (!string.IsNullOrEmpty(xenoSummary)) sb.AppendLine(xenoSummary);
                if (!string.IsNullOrEmpty(ideoSummary)) sb.AppendLine(ideoSummary);
                if (!string.IsNullOrEmpty(eduSummary)) sb.AppendLine(eduSummary);
                if (!string.IsNullOrEmpty(sesSummary)) sb.AppendLine(sesSummary);
                if (!string.IsNullOrEmpty(employSummary)) sb.AppendLine(employSummary);
            }

            if (province.activeCrises.Any())
            {
                sb.AppendLine();
                sb.AppendLine("--- Active Crises ---");
                foreach (var crisis in province.activeCrises)
                {
                    sb.AppendLine($"- {crisis.crisisType} (Severity: {crisis.currentSeverity:P0})");
                }
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// The quick-glance hover tooltip (#53): a few lines only — name, biome, owner, size — so it can
        /// sit at the cursor without wrapping or covering the map. The full breakdown is the selection
        /// panel (RegionInfoWindow), not this.
        /// </summary>
        public static string GetProvinceTooltipShort(GeographicProvince province)
        {
            if (province == null) return string.Empty;
            string owner = province.ownershipData != null
                ? RegionalDomainUtility.GetStatusDescription(province.ownershipData)
                : "Unclaimed";
            string biome = province.primaryBiome?.LabelCap ?? "Unknown";
            return $"{province.name}\n{biome} — {owner}\nTiles: {province.tiles.Count}  (click for details)";
        }

        private Color GetUniqueColor(int id)
        {
            UnityEngine.Random.State state = UnityEngine.Random.state;
            UnityEngine.Random.InitState(id * 1337 + 42);
            float h = UnityEngine.Random.value;
            float s = UnityEngine.Random.Range(0.4f, 0.7f);
            float v = UnityEngine.Random.Range(0.7f, 0.9f);
            UnityEngine.Random.state = state;

            Color c = Color.HSVToRGB(h, s, v);
            return new Color(c.r, c.g, c.b, 0.35f);
        }
    }
}
