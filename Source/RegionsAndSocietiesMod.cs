using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties
{
    public class RegionsAndSocietiesMod : Mod
    {
        public static FactionPlacementSettings Settings;
        private static bool demographicTuningExpanded;
        private static string maxPanelsBuffer;

        /// <summary>#53: whether the Societies layer (population, demographics, economy) runs at all.
        /// The single gate every societies entry point reads; off means Regions only.</summary>
        public static bool SocietiesEnabled => FactionPlacementSettings.societiesEnabled;

        public override string SettingsCategory() => "Regions and Societies";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var l = new Listing_Standard();
            l.Begin(inRect);

            // Claimed land area (settlement density) moved to the world-creation Geographic Placement dialog.

            l.Label($"Territory compactness (squaring): {Mathf.RoundToInt(FactionPlacementSettings.territoryCompactness * 100f)}%",
                tooltip: "How strongly territories prefer squaring off over spidering (#19). Growth favors provinces already embedded in the domain — filling pockets before extending tendrils. 0% is the old purely-greedy behavior; 100% means a poorly-connected province is chosen only when its land is dramatically better. Applies to newly generated worlds and to expansion mods that read the compactness endpoint.");
            FactionPlacementSettings.territoryCompactness = l.Slider(FactionPlacementSettings.territoryCompactness, 0f, 1f);

            // World partition algorithm (pluggable — mods can add their own via IRegionPartitioner).
            var currentPartitioner = Partition.RegionPartitionerRegistry.Get(FactionPlacementSettings.partitionAlgorithmId)
                ?? Partition.RegionPartitionerRegistry.Default;
            l.Label("World partition algorithm:",
                tooltip: "How the globe is cut into regions. Applies to NEWLY generated worlds only — an existing save keeps the algorithm it was generated with, so switching never re-cuts a live map. Expansion mods can register their own algorithms here.");
            if (l.ButtonText(currentPartitioner != null ? currentPartitioner.Label : FactionPlacementSettings.partitionAlgorithmId))
            {
                var options = new List<FloatMenuOption>();
                foreach (var p in Partition.RegionPartitionerRegistry.All)
                {
                    var picked = p;   // capture per-iteration
                    options.Add(new FloatMenuOption(picked.Label, () => FactionPlacementSettings.partitionAlgorithmId = picked.AlgorithmId));
                }
                if (options.Count > 0) Find.WindowStack.Add(new FloatMenu(options));
            }
            if (currentPartitioner != null && !string.IsNullOrEmpty(currentPartitioner.Description))
            {
                GUI.color = new Color(0.7f, 0.7f, 0.7f);
                l.Label(currentPartitioner.Description);
                GUI.color = Color.white;
            }

            // Biome region sizes — per-biome multipliers on the region size band, so a player can make sparse
            // biomes (ice, desert) subdivide more or less. Ours are the defaults; opens a dedicated editor.
            if (l.ButtonText("Biome region sizes…"))
                Find.WindowStack.Add(new UI.Dialog_BiomeRegionWeights());
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            l.Label("Tune how big each biome's regions are (ice & desert default larger). Applies to newly generated worlds.");
            GUI.color = Color.white;

            l.GapLine();

            l.Label("Open a region's comparison panel with:");
            if (l.RadioButton("Ctrl + click", !FactionPlacementSettings.regionPanelUseShift))
            {
                FactionPlacementSettings.regionPanelUseShift = false;
            }
            if (l.RadioButton("Shift + click", FactionPlacementSettings.regionPanelUseShift))
            {
                FactionPlacementSettings.regionPanelUseShift = true;
            }

            l.Gap();
            // Max comparison panels: a free-entry number, 0 = no limit.
            l.Label($"Max comparison panels open at once ({(FactionPlacementSettings.maxRegionPanels <= 0 ? "no limit" : FactionPlacementSettings.maxRegionPanels.ToString())}):",
                tooltip: "How many region comparison panels can be open together. Type 0 for no limit.");
            string mpBuf = maxPanelsBuffer ?? FactionPlacementSettings.maxRegionPanels.ToString();
            var mpRect = l.GetRect(28f);
            mpRect.width = 90f;
            Widgets.TextFieldNumeric(mpRect, ref FactionPlacementSettings.maxRegionPanels, ref mpBuf, 0f, 999f);
            maxPanelsBuffer = mpBuf;

            l.GapLine();
            l.Label("Regions and Societies features — toggle any off to avoid conflicts with other mods:");
            l.CheckboxLabeled("Societies: population, demographics & economy", ref FactionPlacementSettings.societiesEnabled,
                "The whole Societies layer. Off means Regions only — the partition, territories, borders, placement and their map modes still work, but nothing models or draws population, demographics or economy, and none of it ticks. Turn it off if you only want the map framework, or to save the load-time and tick cost.");
            l.CheckboxLabeled("Enable small regions (< 7 tiles)", ref FactionPlacementSettings.enableSmallRegions,
                "Keep tiny 1-6 tile regions on the map instead of dropping them at world generation. They are real, settle-able regions, but too small to sustain a regional society — so they earn no regional benefits (no demographics or economy). Off by default: such slivers are dropped and their tiles left unassigned.");
            l.CheckboxLabeled("World-object integration (master)", ref Integration.WorldObjectIntegrationSettings.masterEnabled,
                "Master switch for the world-object integration layer. Off means Regions & Societies governs only vanilla objects and leaves modded outposts, camps and bases alone.");
            l.CheckboxLabeled("Placement rules for modded world objects", ref Integration.WorldObjectIntegrationSettings.placementGovernance,
                "Apply region ownership, buffer distance and supply range to where modded world objects may be built.");
            l.CheckboxLabeled("Settlement tiers & capitals", ref Integration.WorldObjectIntegrationSettings.settlementTiers,
                "Structural tiers (village → metropolis) from each faction's settlement pyramid, and the capital star marker.");

            // Territorial ownership and the region lock. Both are per-world and safe to change mid-game: with
            // a world loaded we edit that world's own flag; from the main menu we set the default for newly
            // generated worlds. (Presented as a status line on the world-generation placement dialog, which
            // points here.)
            var mgr = Find.World?.GetComponent<SynapseRegionManager>();
            if (mgr != null)
            {
                bool strict = mgr.StrictTerritorialOwnership, beforeStrict = strict;
                l.CheckboxLabeled("Strict territorial ownership (this world)", ref strict,
                    "On: Regions & Societies decides where settlements and outposts may be built — buffers, supply range and footholds. Off (compatibility): placement is left to vanilla and other mods. Regions are still generated and territory still owned and drawn. Safe to change mid-game.");
                if (strict != beforeStrict) mgr.StrictTerritorialOwnership = strict;

                bool locked = mgr.RegionLock, beforeLock = locked;
                l.CheckboxLabeled("Enforce region locks (this world)", ref locked,
                    "On: a faction (and holding seeding) is refused a settlement/outpost in a region a rival holds exclusively (≥71%). Off: that hard refusal stands down — buffers, spacing, supply range and footholds still apply. Safe to change mid-game.");
                if (locked != beforeLock) mgr.RegionLock = locked;
            }
            else
            {
                l.CheckboxLabeled("Strict territorial ownership (new worlds)", ref FactionPlacementSettings.strictTerritorialOwnershipDefault,
                    "Whether newly generated worlds enforce Regions & Societies' placement rules (buffers, supply range, footholds). Load a save to change that world's own setting.");
                l.CheckboxLabeled("Enforce region locks (new worlds)", ref FactionPlacementSettings.regionLockDefault,
                    "Whether newly generated worlds refuse a holding in a region a rival holds exclusively. Load a save to change that world's own setting.");
            }

            l.CheckboxLabeled("Log world object types no integration recognises", ref Integration.WorldObjectIntegrationSettings.logUnknownWorldObjects,
                "Writes one message per unrecognised type. Useful when reporting a mod that Regions & Societies should support.");
            // #53: the population model's tuning belongs to the Societies layer; hide it when off. The old
            // "Population caps" on/off checkbox was removed — it gated nothing (the model always applies);
            // only these multipliers actually do something, so they are shown directly.
            if (FactionPlacementSettings.societiesEnabled)
            {
                float mult = Integration.WorldObjectIntegrationSettings.populationCapMultiplier;
                mult = Mathf.RoundToInt(l.SliderLabeled(
                    $"Population cap multiplier: {mult:0}  (metropolis ≈ {15 * mult:0} pawns, village ≈ {mult:0})",
                    mult, 5f, 60f));
                Integration.WorldObjectIntegrationSettings.populationCapMultiplier = mult;

                float growth = Integration.WorldObjectIntegrationSettings.growthRateMultiplier;
                growth = l.SliderLabeled(
                    $"Population growth rate: {growth:0.0}× real  (a healthy town grows ~{1.5f * growth:0}%/yr)",
                    growth,
                    Integration.WorldObjectIntegrationSettings.GrowthRateMultiplierMin,
                    Integration.WorldObjectIntegrationSettings.GrowthRateMultiplierMax);
                Integration.WorldObjectIntegrationSettings.growthRateMultiplier = (float)System.Math.Round(growth, 1);

            l.CheckboxLabeled("Demographic pressure tuning", ref demographicTuningExpanded, "Show the reach/falloff sliders that shape how far a settlement's make-up carries and how contested its borders are.");
            if (demographicTuningExpanded)
            {
                float reach = Integration.WorldObjectIntegrationSettings.demographicReach;
                reach = l.SliderLabeled($"   Demographic reach ×{reach:0.00}  (a city's radius = population × this)", reach, 0.2f, 3f);
                Integration.WorldObjectIntegrationSettings.demographicReach = (float)System.Math.Round(reach, 2);

                float fall = Integration.WorldObjectIntegrationSettings.demographicFalloff;
                fall = l.SliderLabeled($"   Demographic falloff ^{fall:0.00}  (higher = borders flip more easily)", fall, 0.25f, 4f);
                Integration.WorldObjectIntegrationSettings.demographicFalloff = (float)System.Math.Round(fall, 2);

                float genYears = Integration.WorldObjectIntegrationSettings.demographicGenerationYears;
                genYears = Mathf.Round(l.SliderLabeled(
                    $"   War/draft skew recovery: {genYears:0} years  (how long a region's sex ratio takes to recover from combat losses)",
                    genYears, 1f, 30f));
                Integration.WorldObjectIntegrationSettings.demographicGenerationYears = genYears;
            }
            }   // #53: end of societies-only settings

            l.Gap();
            l.CheckboxLabeled("Draw region borders on the world map",
                ref UI.RegionBorderOverlay.Enabled,
                "Draws province division lines over any map mode so ownership reads at a glance. Also toggleable from Map Mode Framework's Draw Settings panel when present; this keeps it reachable under frameworks that lack that panel (e.g. Realistic Planets 2's fork). (#81)");

            l.Gap();
            l.Label("Planet region size and placement rules are also configured on the world-generation screen.");
            l.End();
        }

        public RegionsAndSocietiesMod(ModContentPack content) : base(content)
        {
            Log.Message("[RegionsAndSocieties] Initializing Regions and Territories Mod...");
            Settings = GetSettings<FactionPlacementSettings>();

            var harmony = new Harmony("regionsandsocieties.core");
            try
            {
                harmony.PatchAll();
            }
            catch (Exception ex)
            {
                // Defense-in-depth (#81). The known break — RP2's MMF fork removed
                // MapModeUI.DoDrawSettingsExpanded — is handled cleanly by that patch's Prepare(), so
                // PatchAll should not throw. This catch exists so any *unforeseen* single patch failure
                // can never again sink the whole constructor and the manual VOE/Empire/provider
                // registration below it (which is exactly what left R&T inert under RP2).
                Log.Warning($"[RegionsAndSocieties] Harmony PatchAll reported a failure; continuing so the rest of the mod loads. {ex}");
            }

            foreach (var m in harmony.GetPatchedMethods())
            {
                Log.Message($"[RegionsAndSocieties] Successfully patched method: {m.DeclaringType.FullName}.{m.Name}");
            }

            // 0.7: build the mod-agnostic world-object adapter set before any integration patching,
            // so the patches below can classify through the registry instead of naming mod types.
            Integration.WorldObjectAdapterRegistry.Initialize();

            // 0.8: the write-side counterpart — creators that build holdings (VOE outposts) for the
            // outpost-seeding pass. Registered here so the seeding postfix can stay mod-agnostic.
            Integration.HoldingCreatorRegistry.Initialize();

            // #18: the sizing-side counterpart — per-kind seeding policies (how many holdings to seed and
            // where). Core registers the Outpost policy; a CP mod adds its own from its Mod constructor.
            Integration.SeedingPolicyRegistry.Initialize();

            // 0.3.0: the pluggable world-partition algorithms. Core registers its two built-ins; expansion
            // mods add their own IRegionPartitioner from their Mod constructor and it appears in the
            // world-partition dropdown in settings.
            Partition.RegionPartitionerRegistry.Initialize();

            RegisterProvidersWithCore();

            // Region introspection tools over Core's MCP bridge (get_region_info, show_world_map).
            // Deferred so Core has registered its own tools first — both run via ExecuteWhenFinished
            // and Core loads before this mod, so its callback is queued first.
            LongEventHandler.ExecuteWhenFinished(Integration.RegionMcpTools.RegisterWithCore);

            // Foreign-mod patching lives in the compatibility patches now (Empire-CP, VOE-CP,
            // VFE-CP, World-Domination-CP), each applying its own typed Harmony patches and
            // registering its adapters/creators through the public registries above.
        }

        /// <summary>
        /// Publish the capabilities this mod owns to RimSynapse Core, if Core is installed.
        ///
        /// <para>All by reflection, and this mod holds no assembly reference to Core — it has to
        /// build and run on its own, with nothing but Map Mode Framework. Every branch logs,
        /// because a provider that quietly failed to register is indistinguishable from one
        /// answering "nothing", which is the same failure class as an unbound Harmony patch.</para>
        /// </summary>
        private void RegisterProvidersWithCore()
        {
            var providers = GenTypes.GetTypeInAnyAssembly("RimSynapse.SynapseCoreProviders");
            if (providers == null)
            {
                // Either Core is absent, or it predates the provider registry. Fall back so an
                // older Core still gets population density.
                TryRegisterLegacyPopulationDelegate();
                return;
            }

            // Expose the honest per-tile population (dwellings at the tile), matching province totals
            // and the inspect label; the smeared field is an internal heatmap detail only (#55/#62).
            // The living inhabitants this count drives — dwellings, residents, residency — moved to the
            // Living World companion mod (0.8); R&T keeps only the abstract count, published here.
            TryRegisterProvider(providers, "PopulationDensity",
                (Func<int, int>)PopulationDensityUtility.GetSourcePopulationAtTile);
        }

        private void TryRegisterProvider(Type providers, string slotName, Delegate provider)
        {
            try
            {
                var slot = providers.GetProperty(slotName, BindingFlags.Public | BindingFlags.Static);
                if (slot == null || !slot.CanWrite)
                {
                    Log.Warning($"[RegionsAndSocieties] SynapseCoreProviders has no writable '{slotName}' slot; that capability will not be visible to other mods.");
                    return;
                }

                slot.SetValue(null, provider);
                Log.Message($"[RegionsAndSocieties] Registered '{slotName}' provider to RimSynapse Core successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"[RegionsAndSocieties] Error registering '{slotName}' provider: {ex}");
            }
        }

        /// <summary>
        /// The pre-registry registration path, for a copy of Core older than the provider surface.
        /// Remove when that Core's shim field goes.
        /// </summary>
        private void TryRegisterLegacyPopulationDelegate()
        {
            try
            {
                var coreWorldCompType = GenTypes.GetTypeInAnyAssembly("RimSynapse.SynapseCoreWorldComponent");
                if (coreWorldCompType == null)
                {
                    Log.Message("[RegionsAndSocieties] RimSynapse Core not detected. Running standalone; no providers registered.");
                    return;
                }

                var field = coreWorldCompType.GetField("GetPopulationDensityDelegate", BindingFlags.Public | BindingFlags.Static);
                if (field != null)
                {
                    Func<int, int> del = PopulationDensityUtility.GetSourcePopulationAtTile;
                    field.SetValue(null, del);
                    Log.Message("[RegionsAndSocieties] Registered population delegate to RimSynapse Core (legacy field) successfully.");
                }
                else
                {
                    Log.Warning("[RegionsAndSocieties] Could not find GetPopulationDensityDelegate field in SynapseCoreWorldComponent.");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RegionsAndSocieties] Error registering population delegate: {ex.Message}");
            }
        }

    }
}
