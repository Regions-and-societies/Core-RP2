using System.Linq;
using System.Reflection;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using RegionsAndSocieties.Integration;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties.UI
{
    /// <summary>
    /// Debug-menu entries for the 0.7.2 playtest fixes, grouped under "Regions and Societies". Each
    /// just logs the matching <see cref="RegionDebugReports"/> report, so the human menu path and
    /// the agent's headless bridge path (RegionMcpTools) exercise the exact same code (see the mod
    /// CLAUDE.md debug-validation gate).
    /// </summary>
    public static class DebugActions_RegionsAndSocieties
    {
        [DebugAction("Regions and Societies", "R&S: adapter registry dump (Core-MMF#3)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Entry | AllowedGameStates.Playing)]
        private static void AdapterRegistryDump()
        {
            // The compatibility-inversion acceptance check: which adapters are registered, from
            // which assembly (core's reflection profiles vs a compatibility patch), in what priority
            // order, and whether each is present/active. This is how "which patch claimed which
            // object" is verified headlessly after an extraction.
            WorldObjectAdapterRegistry.Initialize();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("--- adapter registry dump (Core-MMF#3) ---");
            foreach (var a in WorldObjectAdapterRegistry.Adapters)
            {
                string assembly = a.GetType().Assembly.GetName().Name;
                sb.AppendLine($"  [{a.Priority,4}] {a.AdapterId,-16} {a.DisplayName,-28} type={a.GetType().Name} asm={assembly} present={a.IsPresent} active={a.IsActive}");
            }
            sb.AppendLine($"{WorldObjectAdapterRegistry.Adapters.Count} adapter(s) registered.");
            Log.Message(sb.ToString());
        }

        [DebugAction("Regions and Societies", "R&S: rebrand back-compat report (Core-MMF#2)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void RebrandBackCompatReport()
        {
            // Proves a pre-rebrand save was resurrected through the type mapping rather than
            // silently dropped: the patch counts old-name resolutions, and the manager's contents
            // show whether the scribed territory data actually arrived.
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("--- rebrand back-compat report (Core-MMF#2) ---");
            int n = Patches.Patch_BackCompatibility_TypeRename.ResurrectionCount;
            sb.AppendLine(n > 0
                ? $"old-name resolutions this session: {n} (last: {Patches.Patch_BackCompatibility_TypeRename.LastResurrectedName})"
                : "old-name resolutions this session: 0 (save was written post-rebrand, or no save loaded)");
            var manager = Find.World?.GetComponent<SynapseRegionManager>();
            if (manager == null)
            {
                sb.AppendLine("SynapseRegionManager: MISSING from the world's components — territory data was lost.");
            }
            else
            {
                int provinces = manager.Provinces?.Count ?? 0;
                sb.AppendLine($"SynapseRegionManager: present, {provinces} province(s).");
                if (n > 0 && provinces == 0)
                    sb.AppendLine("WARNING: resurrected through the mapping but empty — scribing did not restore the data.");
            }
            Log.Message(sb.ToString());
        }

        [DebugAction("Regions and Societies", "R&S: density report (#62/#55)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void DensityReport()
        {
            Log.Message(RegionDebugReports.DensityReport());
        }

        [DebugAction("Regions and Societies", "R&S: shading tiers report (#60)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void ShadingReport()
        {
            Log.Message(RegionDebugReports.ShadingReport());
        }

        [DebugAction("Regions and Societies", "R&S: verify source-culling (#20 perf)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void VerifySourceCulling()
        {
            Log.Message(Demographics.RegionDemographicsUtility.VerifyCulling());
        }

        [DebugAction("Regions and Societies", "R&S: holdings report (#67)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void HoldingsReport()
        {
            Log.Message(RegionDebugReports.HoldingsReport());
        }

        [DebugAction("Regions and Societies", "R&S: placement probe (#61)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void PlacementProbe()
        {
            // Probe the selected world tile if there is one; otherwise sample one province per tier.
            int tileId = -1;
            if (Find.WorldSelector != null && Find.WorldSelector.SelectedTile != PlanetTile.Invalid)
            {
                tileId = Find.WorldSelector.SelectedTile.tileId;
            }
            Log.Message(RegionDebugReports.PlacementProbe(tileId));
        }

        [DebugAction("Regions and Societies", "R&S: border overlay report (#72)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void BorderOverlayReport()
        {
            Log.Message(RegionDebugReports.BorderOverlayReport());
        }

        [DebugAction("Regions and Societies", "R&S: partition audit (#20)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void PartitionAudit()
        {
            Log.Message(RegionDebugReports.PartitionAuditReport());
        }

        [DebugAction("Regions and Societies", "R&S: world + region shape report (#20)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void WorldShapeReport()
        {
            // The world's reproduction key (seed + settings) + the worst-shaped regions. Because the
            // partition is deterministic from the terrain, this is what lets a "region N is horrid" report
            // be regenerated and fixed. Also auto-logged at worldgen.
            Log.Message(RegionDebugReports.WorldShapeReport());
        }

        [DebugAction("Regions and Societies", "R&S: dump partition to CSV (#20)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void DumpPartitionCsv()
        {
            Log.Message(RegionDebugReports.DumpPartitionCsv());
        }

        [DebugAction("Regions and Societies", "R&S: regenerate provinces (#20)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void RegenerateProvinces()
        {
            Log.Message(RegionDebugReports.RegenerateAndAudit());
        }

        [DebugAction("Regions and Societies", "R&S: run population dynamics pass (#5/#8)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void RunPopulationDynamicsPass()
        {
            // Force one accrete->migrate pass now and dump the before/after per-region dynamic delta, so the
            // conservation and drift-toward-the-colony properties are verifiable headlessly (required by #5/#8).
            var mgr = Find.World?.GetComponent<SynapseRegionManager>();
            if (mgr == null) { Log.Warning("[R&S] population dynamics: no region manager"); return; }

            int colony = Integration.PopulationDynamics.ColonyRegion(mgr);
            var land = mgr.Provinces.FindAll(p => p.provinceType == ProvinceType.Land);

            float beforeSum = 0f;
            var before = new System.Collections.Generic.Dictionary<int, float>();
            foreach (var p in land) { float d = mgr.PopulationDeltaOf(p.id); before[p.id] = d; beforeSum += d; }

            float moved = mgr.RunPopulationDynamicsNow();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[R&S] population dynamics pass (#5/#8): governance={mgr.StrictTerritorialOwnership}, colony region={colony}, moved={moved:0.0} people");
            float afterSum = 0f;
            var changed = new System.Collections.Generic.List<(int id, float before, float after)>();
            foreach (var p in land)
            {
                float a = mgr.PopulationDeltaOf(p.id);
                afterSum += a;
                if (System.Math.Abs(a - before[p.id]) > 0.001f) changed.Add((p.id, before[p.id], a));
            }
            sb.AppendLine($"total delta before={beforeSum:0.0} after={afterSum:0.0} (conserved if equal), regions changed={changed.Count}");
            changed.Sort((x, y) => System.Math.Abs(y.after - y.before).CompareTo(System.Math.Abs(x.after - x.before)));
            for (int i = 0; i < changed.Count && i < 12; i++)
                sb.AppendLine($"  region {changed[i].id}: delta {changed[i].before:0.0} -> {changed[i].after:0.0}");
            Log.Message(sb.ToString());
        }

        // Headless visual validation (0.3.0): switch the world view to an overlay, then grab the rendered
        // frame with Unity's own capture — no OS-level screenshot needed, and it works while the game sits
        // on another virtual desktop. Two parameterless actions (a parameter breaks the whole debug menu).
        [DebugAction("Regions and Societies", "R&S: show population density overlay", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void ShowPopulationDensityOverlay()
        {
            if (Find.World == null) return;
            Find.World.renderer.wantedMode = WorldRenderMode.Planet;
            var comp = MapModeFramework.MapModeComponent.Instance;
            var mode = comp?.mapModes?.FirstOrDefault(m => m.def.defName == "SynapsePopulationDensity");
            if (mode == null) { Log.Warning("[R&S] density overlay: map mode 'SynapsePopulationDensity' not found"); return; }
            comp.SwitchMapMode(mode);

            // Clear the stage: the debug log auto-opens on any warning and would cover the map.
            Find.WindowStack?.TryRemove(typeof(EditWindow_Log), false);

            // Frame the densest tile in the world (the biggest city and its sprawl), zoomed in.
            int best = -1, bestPop = 0;
            int tiles = Find.WorldGrid.TilesCount;
            for (int t = 0; t < tiles; t++)
            {
                int p = PopulationDensityUtility.GetSourcePopulationAtTile(t);
                if (p > bestPop) { bestPop = p; best = t; }
            }
            if (best >= 0)
            {
                Find.WorldCameraDriver.JumpTo(best);
                // Altitude is a private field on the driver; nudge it close so a city and its outskirts fill the view.
                var drv = Find.WorldCameraDriver;
                foreach (string f in new[] { "desiredAltitude", "altitude" })
                {
                    var fi = typeof(WorldCameraDriver).GetField(f, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (fi != null && fi.FieldType == typeof(float)) fi.SetValue(drv, 170f);
                }
            }
            Log.Message($"[R&S] density overlay: world view opened, population density map mode active, camera on tile {best} (pop {bestPop}).");
        }

        [DebugAction("Regions and Societies", "R&S: screenshot current view (Unity capture)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void ScreenshotCurrentView()
        {
            string dir = System.IO.Path.Combine(GenFilePaths.SaveDataFolderPath, "Screenshots");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "rs-view-" + System.DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Log.Message("[R&S] screenshot requested: " + path + " (written by Unity at the end of the frame)");
        }

        [DebugAction("Regions and Societies", "R&S: open region demographics panel (#26)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void OpenRegionDemographicsPanel()
        {
            // Opens the visual demographic panel (#26) for the selected land province, or the first one
            // found when nothing is selected — so the panel's render path is exercised headlessly (any
            // draw-time exception surfaces in the log) and a human can eyeball it from the debug menu.
            var mgr = Find.World?.GetComponent<SynapseRegionManager>();
            if (mgr == null) { Log.Warning("[R&S] open region panel: no region manager"); return; }

            int tile = SelectedWorldTile();
            GeographicProvince province = tile >= 0 ? mgr.GetProvinceForTile(tile) : null;
            if (province == null || province.provinceType != ProvinceType.Land)
            {
                province = null;
                var all = mgr.Provinces;
                if (all != null)
                    for (int i = 0; i < all.Count; i++)
                        if (all[i] != null && all[i].provinceType == ProvinceType.Land) { province = all[i]; break; }
                tile = province != null && province.tiles != null && province.tiles.Count > 0 ? province.tiles[0] : -1;
            }
            if (province == null) { Log.Warning("[R&S] open region panel: no land province found"); return; }

            RegionInfoWindow.OpenFor(province, tile);
            Log.Message($"[R&S] opened region demographics panel for region {province.id} ({province.name}).");
        }

        [DebugAction("Regions and Societies", "R&S: faction character matrix (#27)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void FactionCharacterReport()
        {
            // #27: the canonical modifier matrix, one row per humanlike faction DEF across the base game
            // and every active DLC — its module, tech level, archetype, and the knowledge/wealth skews it
            // carries, plus the resulting education index and characteristic wealth. This is the data
            // behind the faction-character infographic; deterministic, no live world needed.
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== R&S faction character matrix (#27) ===");
            sb.AppendLine("defName | module | tech | archetype | knowSkew | wealthMult | eduIdx | wealth");
            foreach (var def in DefDatabase<FactionDef>.AllDefs)
            {
                if (def == null || !def.humanlikeFaction || def.isPlayer) continue;
                int tech = (int)def.techLevel;
                var arch = Demographics.FactionCharacterRules.Classify(def.defName, tech, def.permanentEnemy);
                var ch = Demographics.FactionCharacterRules.CharacterOf(arch);
                int eduIdx = Demographics.EducationRules.Index(Demographics.EducationRules.Pyramid(tech, ch.knowledgeSkew, 0f));
                int wealth = (int)System.Math.Round(BaseWealthFor(tech) * ch.wealthMultiplier);
                string module = def.modContentPack?.Name ?? "?";
                sb.AppendLine($"{def.defName} | {module} | {def.techLevel} | {arch} | {ch.knowledgeSkew:+0.00;-0.00} | {ch.wealthMultiplier:0.00} | {eduIdx} | {wealth}");
            }
            Log.Message(sb.ToString());
        }

        // Mirror of FactionDemographicProfile.BaseWealth for the matrix report (kept local so the report
        // has no reason to widen that private table's visibility).
        private static int BaseWealthFor(int tech)
        {
            switch (tech)
            {
                case 1: case 2: return 120;   // Animal / Neolithic
                case 3: return 250;           // Medieval
                case 4: return 500;           // Industrial
                case 5: return 1000;          // Spacer
                case 6: case 7: return 2000;  // Ultra / Archotech
                default: return 400;
            }
        }

        [DebugAction("Regions and Societies", "R&S: settlement growth curve (#6)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void SettlementGrowth()
        {
            Log.Message(RegionDebugReports.SettlementGrowthReport(SelectedWorldTile()));
        }

        [DebugAction("Regions and Societies", "R&S: outpost archetype preview (#18)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void OutpostArchetypePreview()
        {
            // #18: for the selected land province (or the first found), report the archetype the
            // position/faction-aware scorer would pick per candidate tile — without placing anything, so
            // it works with no outpost creator (VOE) installed. Validates the worldgen-fed inputs + choice.
            var mgr = Find.World?.GetComponent<SynapseRegionManager>();
            if (mgr == null) { Log.Warning("[R&S] outpost preview: no region manager"); return; }

            int tile = SelectedWorldTile();
            GeographicProvince province = tile >= 0 ? mgr.GetProvinceForTile(tile) : null;
            if (province == null || province.provinceType != ProvinceType.Land)
            {
                province = null;
                var all = mgr.Provinces;
                if (all != null)
                    for (int i = 0; i < all.Count; i++)
                        if (all[i] != null && all[i].provinceType == ProvinceType.Land) { province = all[i]; break; }
            }
            if (province == null) { Log.Warning("[R&S] outpost preview: no land province found"); return; }

            // Prefer a province that has an anchor settlement, so the preview shows the position/faction
            // pattern rather than the terrain-only degrade path.
            string preview = OutpostSeedingUtility.PreviewArchetypes(province);
            if (preview.Contains("no anchor") && mgr.Provinces != null)
            {
                foreach (var p in mgr.Provinces)
                {
                    if (p == null || p.provinceType != ProvinceType.Land) continue;
                    string pr = OutpostSeedingUtility.PreviewArchetypes(p);
                    if (!pr.Contains("no anchor")) { preview = pr; break; }
                }
            }
            Log.Message(preview);
        }

        // #72 border-overlay test tooling. Each reads the selected world tile (select a province on the
        // planet, then run) and falls back to the first land province when nothing is selected, so the
        // menu path and the headless run_debug_action path both work. Forced styles survive the repaint
        // until "clear ownership overrides" recomputes from real holdings.

        [DebugAction("Regions and Societies", "R&S: TEST force CONTESTED (selected province)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void ForceContested()
        {
            Log.Message(RegionDebugReports.ForceOwnershipStyle(SelectedWorldTile(), "contested"));
        }

        [DebugAction("Regions and Societies", "R&S: TEST force SOLID owner (selected province)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void ForceSolid()
        {
            Log.Message(RegionDebugReports.ForceOwnershipStyle(SelectedWorldTile(), "solid"));
        }

        [DebugAction("Regions and Societies", "R&S: TEST force LOOSE claim (selected province)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void ForceLoose()
        {
            Log.Message(RegionDebugReports.ForceOwnershipStyle(SelectedWorldTile(), "loose"));
        }

        [DebugAction("Regions and Societies", "R&S: TEST clear ownership overrides (recompute)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void ClearOwnershipOverrides()
        {
            Log.Message(RegionDebugReports.ClearOwnershipOverrides());
        }

        [DebugAction("Regions and Societies", "R&S: TEST drop rival settlement (selected province)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void DropRivalSettlement()
        {
            Log.Message(RegionDebugReports.DropRivalSettlement(SelectedWorldTile()));
        }

        private static int SelectedWorldTile()
        {
            if (Find.WorldSelector != null && Find.WorldSelector.SelectedTile != PlanetTile.Invalid)
            {
                return Find.WorldSelector.SelectedTile.tileId;
            }
            return -1;
        }

        /// <summary>
        /// #81 either-or validation. Reports which Map Mode Framework implementation is providing the
        /// overlay capability — NozoMe's original, Realistic Planets 2's forked shim, or neither — and
        /// whether the fork-sensitive method (<c>MapModeUI.DoDrawSettingsExpanded</c>) that would otherwise
        /// crash <c>PatchAll</c> is present. Under RP2 the expectation is: frameworkTypePresent=true,
        /// DoDrawSettingsExpanded=false (the border-toggle patch must self-skip via its Prepare()), and the
        /// mod loads with no red errors. Runnable at the main menu (Entry) as well as in-world.
        /// </summary>
        [DebugAction("Regions and Societies", "R&S: map-framework compat probe (#81)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Entry | AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void MapFrameworkCompatProbe()
        {
            bool nozome = ModsConfig.IsActive("NozoMe.MapModeFramework");
            bool rp2 = ModsConfig.IsActive("koth.RealisticPlanets2");
            bool typePresent = MapFrameworkGate.Present;

            var uiType = GenTypes.GetTypeInAnyAssembly("MapModeFramework.MapModeUI");
            bool drawSettingsExpanded = uiType != null &&
                uiType.GetMethod("DoDrawSettingsExpanded", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) != null;

            string provider = nozome ? "NozoMe (original)" : (rp2 ? "Realistic Planets 2 (forked shim)" : (typePresent ? "unknown fork" : "NONE"));

            Log.Message($"[SYNAPSE-TEST] {(typePresent ? "PASS" : "WARN")} RT_MapFramework_Probe | provider={provider} " +
                        $"NozoMe.MapModeFramework={nozome} koth.RealisticPlanets2={rp2} frameworkTypePresent={typePresent} " +
                        $"DoDrawSettingsExpanded={drawSettingsExpanded}. Expect NO red errors above regardless of provider; " +
                        $"under RP2, DoDrawSettingsExpanded=false is correct (border-toggle patch self-skips).");
        }

        [DebugAction("Regions and Societies", "R&S: ownership derivation (#69)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void OwnershipDerivation()
        {
            // Derive the selected province if one is picked; otherwise scan every holdingless province
            // and assert none exceeds the 0.70 border-only cap (the #69 regression).
            int tileId = -1;
            if (Find.WorldSelector != null && Find.WorldSelector.SelectedTile != PlanetTile.Invalid)
            {
                tileId = Find.WorldSelector.SelectedTile.tileId;
            }
            Log.Message(RegionDebugReports.OwnershipDerivationReport(tileId));
        }

        [DebugAction("Regions and Societies", "R&S: settlement tiers & outpost allowance (#56)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void SettlementTierAllowanceReport()
        {
            Log.Message(RegionDebugReports.SettlementTierAllowanceReport());
        }

        [DebugAction("Regions and Societies", "R&S: force outpost seeding (#56)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void ForceOutpostSeeding()
        {
            Log.Message(RegionDebugReports.OutpostSeedingReport());
        }

        [DebugAction("Regions and Societies", "R&S: tier pyramid & capitals (0.8)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void TierPyramidReport()
        {
            Log.Message(RegionDebugReports.TierPyramidReport());
        }

        [DebugAction("Regions and Societies", "R&S: region demographics (#36)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void DemographicsReport()
        {
            Log.Message(RegionDebugReports.DemographicsReport(SelectedWorldTile()));
        }

        // Live demographic-falloff tuning: nudge a knob, recompute, and reprint the selected region's
        // shares — no reload. Select a border province, then step reach/falloff until "own" reads ~50-60%.
        [DebugAction("Regions and Societies", "R&S: demo reach +0.1", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void DemoReachUp() { NudgeDemographics(0.1f, 0f); }

        [DebugAction("Regions and Societies", "R&S: demo reach -0.1", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void DemoReachDown() { NudgeDemographics(-0.1f, 0f); }

        [DebugAction("Regions and Societies", "R&S: demo falloff +0.25", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void DemoFalloffUp() { NudgeDemographics(0f, 0.25f); }

        [DebugAction("Regions and Societies", "R&S: demo falloff -0.25", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void DemoFalloffDown() { NudgeDemographics(0f, -0.25f); }

        [DebugAction("Regions and Societies", "R&S: faction demographics (#36)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void FactionDemographicsReport()
        {
            Log.Message(RegionDebugReports.FactionDemographicsReport());
        }

        [DebugAction("Regions and Societies", "R&S: demo cycle falloff model", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void DemoCycleModel()
        {
            int count = System.Enum.GetValues(typeof(Demographics.DemographicsRules.FalloffModel)).Length;
            Integration.WorldObjectIntegrationSettings.demographicFalloffModel =
                (Integration.WorldObjectIntegrationSettings.demographicFalloffModel + 1) % count;
            NudgeDemographics(0f, 0f);
        }

        [DebugAction("Regions and Societies", "R&S: demo refresh (recompute, no reload)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void DemoRefresh() { NudgeDemographics(0f, 0f); }

        private static void NudgeDemographics(float reachDelta, float falloffDelta)
        {
            var s = Integration.WorldObjectIntegrationSettings.demographicReach + reachDelta;
            Integration.WorldObjectIntegrationSettings.demographicReach = Mathf.Clamp((float)System.Math.Round(s, 2), 0.2f, 3f);
            var f = Integration.WorldObjectIntegrationSettings.demographicFalloff + falloffDelta;
            Integration.WorldObjectIntegrationSettings.demographicFalloff = Mathf.Clamp((float)System.Math.Round(f, 2), 0.25f, 4f);

            // Recompute everything that depends on the field, and re-render the map, without a reload.
            Demographics.RegionDemographicsUtility.InvalidateCache();
            PopulationDensityUtility.MarkCacheDirty();

            Log.Message(RegionDebugReports.DemographicsReport(SelectedWorldTile()));
        }

        [DebugAction("Regions and Societies", "R&S: TEST lone-settlement ownership (#42)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void LoneSettlementOwnership()
        {
            Log.Message(RegionDebugReports.LoneSettlementOwnershipReport());
        }

        [DebugAction("Regions and Societies", "R&S: ownership tier distribution (#64)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void OwnershipTierReport()
        {
            Log.Message(RegionDebugReports.OwnershipTierReport());
        }

        [DebugAction("Regions and Societies", "R&S: NPC loose-ownership barriers (#65)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void NpcBarrierReport()
        {
            Log.Message(RegionDebugReports.NpcBarrierReport());
        }

        [DebugAction("Regions and Societies", "R&S: TEST anger-on-claim hook (#66)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void TerritoryClaimHookTest()
        {
            Log.Message(RegionDebugReports.TerritoryClaimReport(SelectedWorldTile()));
        }

        [DebugAction("Regions and Societies", "R&S: adapter recon — modded WorldObjects (#71)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Entry | AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void AdapterRecon()
        {
            Log.Message(RegionDebugReports.AdapterReconReport());
        }

        [DebugAction("Regions and Societies", "R&S: density slider report (#51)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void DensitySliderReport()
        {
            Log.Message(RegionDebugReports.DensitySliderReport());
        }

        [DebugAction("Regions and Societies", "R&S: settlement placement check (#65)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void SettlementPlacementCheck()
        {
            int tileId = -1;
            if (Find.WorldSelector != null && Find.WorldSelector.SelectedTile != PlanetTile.Invalid)
            {
                tileId = Find.WorldSelector.SelectedTile.tileId;
            }
            Log.Message(RegionDebugReports.SettlementPlacementCheck(tileId));
        }

        // A [DebugAction] method MUST be parameterless. LudeonTK builds the debug actions menu by
        // binding every Action-type action with Delegate.CreateDelegate(typeof(Action), method), which
        // throws for a method with parameters — inside the Dialog_Debug constructor, so the ENTIRE debug
        // actions menu fails to open for anyone with the mod installed. The old IntVec3 province-id
        // helper here is removed; use the parameterless "R&S: ownership derivation (#69)" (selected
        // tile), or the headless MCP tool rt_ownership_derivation (RegionMcpTools) for a province by id.
        // Never give a [DebugAction] method a parameter.

        /// <summary>
        /// #77 validation. The demographic pressure field is surface-only; before the fix an off-surface or
        /// out-of-range tile (routine on an Odyssey planet with extra <see cref="PlanetLayer"/>s) was fed to
        /// the surface grid, and vanilla <c>PlanetLayer.GetTileCenter</c> logged "Attempted to access a tile
        /// ... out of range (count: N)" once per call — spamming the log around pawn generation.
        ///
        /// <para>This forces the exact bug shape headlessly: it runs <see cref="Demographics.RegionDemographicsUtility.SampleTile"/>
        /// with an out-of-range id of the observed magnitude (~surface+55000) — which, unguarded, would reach
        /// <c>GetTileCenter</c> — and checks the shipping guard (<see cref="Demographics.RegionDemographicsUtility.IsSurfaceSampleTile"/>,
        /// the one the pawn-gen prefix and settlement sourcing use) rejects the out-of-range id and a real
        /// orbital tile while accepting a genuine surface tile. Confirm from read_rimworld_log that NO
        /// "Attempted to access a tile" error appears after the [SYNAPSE-TEST] line.</para>
        /// </summary>
        [DebugAction("Regions and Societies", "R&S: TEST demographics off-surface tile (#77)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void TestDemographicsOffSurfaceTile()
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) { Log.Message("[SYNAPSE-TEST] FAIL RT_Demographics_OffSurfaceTile | no WorldGrid"); return; }

            int surfaceCount = grid.TilesCount;
            int oobId = surfaceCount + 55000;   // ~175000 on a default planet — the reported magnitude, genuinely out of range

            // A real, valid, non-surface tile (orbital layer) — the correctness case the layer check covers.
            PlanetTile orbitTile = PlanetTile.Invalid;
            if (grid.Orbit != null && grid.Orbit.TilesCount > 0)
            {
                orbitTile = new PlanetTile(0, grid.Orbit.LayerID);
            }
            PlanetTile surfaceTile = new PlanetTile(0);   // implicit surface (layerId 0)

            bool rejectsOob = !Demographics.RegionDemographicsUtility.IsSurfaceSampleTile(new PlanetTile(oobId));
            bool rejectsOrbit = !orbitTile.Valid || !Demographics.RegionDemographicsUtility.IsSurfaceSampleTile(orbitTile);
            bool acceptsSurface = Demographics.RegionDemographicsUtility.IsSurfaceSampleTile(surfaceTile);

            // Drive the real demographics entry point with the bad id. With the guard in place this returns a
            // bare sample without ever indexing the surface grid; without it, GetTileCenter would log here.
            var sample = Demographics.RegionDemographicsUtility.SampleTile(oobId);
            bool sampleSafe = sample.owner == null;   // out-of-range tile carries no pressure

            bool pass = rejectsOob && rejectsOrbit && acceptsSurface && sampleSafe;
            Log.Message($"[SYNAPSE-TEST] {(pass ? "PASS" : "FAIL")} RT_Demographics_OffSurfaceTile | surface={surfaceCount} oobId={oobId} " +
                        $"orbitTiles={(grid.Orbit != null ? grid.Orbit.TilesCount : 0)} rejectsOob={rejectsOob} rejectsOrbit={rejectsOrbit} " +
                        $"acceptsSurface={acceptsSurface} sampleOwnerNull={sampleSafe}. Expect NO 'Attempted to access a tile' error above (#77).");
        }
    }
}
