using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using RimWorld.Planet;
using Verse;

namespace RegionsAndSocieties.Integration
{
    /// <summary>
    /// Registers read-only introspection tools with RimSynapse Core's game-tool bridge, so the
    /// region system can be queried over MCP (sizes, biomes, populations, ownership) instead of
    /// read off the screen. Registered entirely by reflection — R&amp;T holds no reference to Core,
    /// so if Core is absent this is a no-op, exactly like the provider registration.
    /// </summary>
    public static class RegionMcpTools
    {
        public static void RegisterWithCore()
        {
            var registry = GenTypes.GetTypeInAnyAssembly("RimSynapse.SynapseToolRegistry");
            if (registry == null) return;   // standalone: no Core, no bridge

            var register = registry.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "RegisterTool" && m.GetParameters().Length == 6);
            if (register == null)
            {
                Log.Warning("[RegionsAndSocieties] SynapseToolRegistry.RegisterTool(6-arg) not found; region tools not exposed.");
                return;
            }

            TryRegister(register, "get_region_info",
                "Regions & Territories region data: sizes, biome, population, ownership and border edges. " +
                "Args (all optional): {} = summary (counts, size stats, oversized + barren regions, top by population); " +
                "{\"all\":true} = every region; {\"provinceId\":N} = one region in full.",
                new { type = "object", properties = new { all = new { type = "boolean" }, provinceId = new { type = "integer" } } },
                (Func<string, string>)GetRegionInfoHandler);

            TryRegister(register, "show_world_map",
                "Switch the game camera to the planet / world view.",
                new { type = "object", properties = new { } },
                (Func<string, string>)ShowWorldMapHandler);

            // Debug-validation bridges for the 0.7.2 playtest fixes. Each shares its code path with
            // the matching "Regions and Societies" [DebugAction] via RegionDebugReports, so the agent's headless
            // trigger and the human menu produce the same report (mod CLAUDE.md validation gate).
            TryRegister(register, "rt_density_report",
                "R&T density (#62/#55): source-pop histogram, natural-pocket caps (off-landmark <=5), and per-province population totals vs the old smear sum.",
                new { type = "object", properties = new { } },
                (Func<string, string>)(_ => Safe(RegionDebugReports.DensityReport)));

            TryRegister(register, "rt_shading_report",
                "R&T territory shading (#60): counts of faint / solid / cross-hatch provinces under the legitimate-claim (>=30%) rule, with examples.",
                new { type = "object", properties = new { } },
                (Func<string, string>)(_ => Safe(RegionDebugReports.ShadingReport)));

            TryRegister(register, "rt_holdings_report",
                "R&T holdings (#67): province coverage of the land, and per-settlement classification / faction / province mapping — for diagnosing zero-ownership on loaded saves.",
                new { type = "object", properties = new { } },
                (Func<string, string>)(_ => Safe(RegionDebugReports.HoldingsReport)));

            TryRegister(register, "rt_placement_probe",
                "R&T player placement (#61): whether the player may settle a tile. Args: {} = sample one province per rival ownership tier; {\"tileId\":N} = probe one tile.",
                new { type = "object", properties = new { tileId = new { type = "integer" } } },
                (Func<string, string>)PlacementProbeHandler);

            TryRegister(register, "rt_partition_audit",
                "R&T border-first partition (#20): land coverage, province-size distribution + histogram, average shape index, and a tail/neck detector (pendant-tile count; target 0). The numeric check on the new river-basin generator.",
                new { type = "object", properties = new { } },
                (Func<string, string>)(_ => Safe(RegionDebugReports.PartitionAuditReport)));

            TryRegister(register, "rt_ownership_derivation",
                "R&T ownership derivation (#69) for one province by id. Args: {\"provinceId\":N}. The headless channel for province-by-id ownership (the human menu uses the selected-tile 'R&S: ownership derivation' action). A [DebugAction] can't take an id argument without breaking RimWorld's debug menu, so this lives here.",
                new { type = "object", properties = new { provinceId = new { type = "integer" } } },
                (Func<string, string>)OwnershipDerivationHandler);
        }

        private static string OwnershipDerivationHandler(string argsJson)
        {
            try
            {
                var m = Regex.Match(argsJson ?? "", "\"provinceId\"\\s*:\\s*(-?\\d+)");
                if (!m.Success) return Err("provinceId (integer) is required");
                return ReportJson(RegionDebugReports.OwnershipDerivationForProvinceId(int.Parse(m.Groups[1].Value)));
            }
            catch (Exception ex) { return Err(ex.Message); }
        }

        // The report methods return a human-readable multi-line string, but the game-tool bridge
        // contract requires every tool to answer with valid JSON on empty args (the whole-registry
        // Registry_HandlersReturnJsonOnEmptyArgs case invokes every registered tool). Wrap the report
        // text in a JSON object, preserving line breaks as \n.
        private static string ReportJson(string report) => "{\"report\":\"" + JsonEsc(report) + "\"}";

        private static string JsonEsc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
        }

        private static string Safe(Func<string> report)
        {
            try { return ReportJson(report()); }
            catch (Exception ex) { return Err(ex.Message); }
        }

        private static string PlacementProbeHandler(string argsJson)
        {
            try
            {
                var m = Regex.Match(argsJson ?? "", "\"tileId\"\\s*:\\s*(-?\\d+)");
                int tileId = m.Success ? int.Parse(m.Groups[1].Value) : -1;
                return ReportJson(RegionDebugReports.PlacementProbe(tileId));
            }
            catch (Exception ex) { return Err(ex.Message); }
        }

        private static void TryRegister(MethodInfo register, string name, string desc, object schema, Func<string, string> handler)
        {
            try
            {
                register.Invoke(null, new object[] { name, desc, schema, handler, false, null });
                Log.Message($"[RegionsAndSocieties] Registered game tool '{name}'.");
            }
            catch (Exception ex)
            {
                Log.Warning($"[RegionsAndSocieties] Failed to register tool '{name}': {ex.Message}");
            }
        }

        // ---- get_region_info ----------------------------------------------------

        private static string GetRegionInfoHandler(string argsJson)
        {
            try
            {
                if (!UnityData.IsInMainThread) return Err("must run on the main thread");

                var mgr = Find.World?.GetComponent<SynapseRegionManager>();
                if (mgr == null) return Err("no world loaded");

                var provinces = mgr.Provinces;   // lazily generates if needed
                if (provinces == null || provinces.Count == 0) return Err("no regions generated");

                mgr.RecalculateProvinceOwners();  // cheap when clean; ensures ownership is current

                bool all = Regex.IsMatch(argsJson ?? "", "\"all\"\\s*:\\s*true", RegexOptions.IgnoreCase);
                var pidMatch = Regex.Match(argsJson ?? "", "\"provinceId\"\\s*:\\s*(-?\\d+)");

                if (pidMatch.Success)
                {
                    int pid = int.Parse(pidMatch.Groups[1].Value);
                    var one = provinces.FirstOrDefault(p => p.id == pid);
                    if (one == null) return Err($"no region with id {pid}");
                    return RegionJson(one);
                }

                if (all)
                {
                    var sb = new StringBuilder();
                    sb.Append("{\"regionCount\":").Append(provinces.Count).Append(",\"regions\":[");
                    for (int i = 0; i < provinces.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(RegionJson(provinces[i]));
                    }
                    sb.Append("]}");
                    return sb.ToString();
                }

                return SummaryJson(provinces);
            }
            catch (Exception ex)
            {
                return Err(ex.Message);
            }
        }

        private static string SummaryJson(List<GeographicProvince> provinces)
        {
            int cap = FactionPlacementSettings.maxRegionSize;
            var land = provinces.Where(p => p.provinceType == ProvinceType.Land).ToList();

            int minT = land.Count > 0 ? land.Min(p => p.tiles.Count) : 0;
            int maxT = land.Count > 0 ? land.Max(p => p.tiles.Count) : 0;
            double avgT = land.Count > 0 ? land.Average(p => p.tiles.Count) : 0;
            long totalPop = provinces.Sum(p => (long)p.currentPopulation);

            var oversized = land.Where(p => !p.IsBarren && p.tiles.Count > cap + 30).OrderByDescending(p => p.tiles.Count).ToList();
            var barren = land.Where(p => p.IsBarren).OrderByDescending(p => p.tiles.Count).ToList();
            var topPop = provinces.OrderByDescending(p => p.currentPopulation).Take(5).ToList();

            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"regionCount\":").Append(provinces.Count).Append(',');
            sb.Append("\"landRegionCount\":").Append(land.Count).Append(',');
            sb.Append("\"maxRegionSize\":").Append(cap).Append(',');
            sb.Append("\"sizeTiles\":{\"min\":").Append(minT).Append(",\"max\":").Append(maxT)
              .Append(",\"avg\":").Append(avgT.ToString("0.0")).Append("},");
            sb.Append("\"fertileRegionsOverCap\":").Append(oversized.Count).Append(',');
            sb.Append("\"totalPopulation\":").Append(totalPop).Append(',');
            sb.Append("\"oversized\":["); AppendBrief(sb, oversized.Take(10)); sb.Append("],");
            sb.Append("\"barrenRegions\":["); AppendBrief(sb, barren.Take(10)); sb.Append("],");
            sb.Append("\"topByPopulation\":["); AppendBrief(sb, topPop); sb.Append(']');
            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendBrief(StringBuilder sb, IEnumerable<GeographicProvince> ps)
        {
            bool first = true;
            foreach (var p in ps)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"id\":").Append(p.id)
                  .Append(",\"name\":\"").Append(Esc(p.name)).Append('"')
                  .Append(",\"biome\":\"").Append(Esc(p.primaryBiome?.defName)).Append('"')
                  .Append(",\"barren\":").Append(p.IsBarren ? "true" : "false")
                  .Append(",\"tiles\":").Append(p.tiles.Count)
                  .Append(",\"population\":").Append(p.currentPopulation)
                  .Append('}');
            }
        }

        private static string RegionJson(GeographicProvince p)
        {
            int usable = p.tiles.Count(SynapseRegionManager.IsTileUsable);
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"id\":").Append(p.id).Append(',');
            sb.Append("\"name\":\"").Append(Esc(p.name)).Append("\",");
            sb.Append("\"type\":\"").Append(Esc(p.provinceType.ToString())).Append("\",");
            sb.Append("\"biome\":\"").Append(Esc(p.primaryBiome?.defName)).Append("\",");
            sb.Append("\"barren\":").Append(p.IsBarren ? "true" : "false").Append(',');
            sb.Append("\"tiles\":").Append(p.tiles.Count).Append(',');
            sb.Append("\"usableTiles\":").Append(usable).Append(',');
            sb.Append("\"population\":").Append(p.currentPopulation).Append(',');
            sb.Append("\"dwellings\":").Append(p.totalDwellings).Append(',');
            sb.Append("\"perimeterEdges\":").Append(p.perimeterEdgeCount).Append(',');
            sb.Append("\"naturalBorderEdges\":").Append(p.naturalBorderEdges).Append(',');
            sb.Append("\"landNeighbours\":").Append(p.borderShares?.Count ?? 0).Append(',');

            var data = p.ownershipData;
            sb.Append("\"unclaimed\":").Append(((data?.unclaimedScore) ?? 1f).ToString("0.000")).Append(',');
            sb.Append("\"owners\":[");
            if (data?.factionScores != null)
            {
                bool first = true;
                foreach (var fs in data.factionScores.Where(s => s.faction != null && s.TotalScore >= 0.01f)
                                                     .OrderByDescending(s => s.TotalScore))
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"faction\":\"").Append(Esc(fs.faction.Name))
                      .Append("\",\"influence\":").Append(fs.TotalScore.ToString("0.000")).Append('}');
                }
            }
            sb.Append("]}");
            return sb.ToString();
        }

        // ---- show_world_map -----------------------------------------------------

        private static string ShowWorldMapHandler(string argsJson)
        {
            try
            {
                if (!UnityData.IsInMainThread) return Err("must run on the main thread");
                if (Find.World == null) return Err("no world loaded");
                CameraJumper.TryShowWorld();
                return "{\"ok\":true}";
            }
            catch (Exception ex)
            {
                return Err(ex.Message);
            }
        }

        // ---- helpers ------------------------------------------------------------

        private static string Err(string msg) => "{\"error\":\"" + Esc(msg) + "\"}";

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
        }
    }
}
