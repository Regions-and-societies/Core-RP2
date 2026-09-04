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

        public override string SettingsCategory() => "Regions and Societies";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var l = new Listing_Standard();
            l.Begin(inRect);

            l.Label($"Claimed land area (settlement density): {Mathf.RoundToInt(FactionPlacementSettings.claimedLandAreaPercent * 100f)}%",
                tooltip: "The single density knob (#51): the target share of livable land area claimed by faction territories at world generation. Higher means more settlements. Replaces the old tile-count scaling; also settable on the world-generation screen. Applies to newly generated worlds.");
            FactionPlacementSettings.claimedLandAreaPercent = l.Slider(FactionPlacementSettings.claimedLandAreaPercent, 0.10f, 0.90f);

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
            l.GapLine();

            l.CheckboxLabeled("Show ownership calculation breakdown in the region panel",
                ref FactionPlacementSettings.showCalculationBreakdowns,
                "Adds the developer ownership-derivation readout to the expanded region panel (opened with the modifier + click chosen below). Off by default, and never shown in the hover tooltip.");

            l.Gap();
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
            FactionPlacementSettings.maxRegionPanels = Mathf.RoundToInt(l.SliderLabeled(
                $"Max comparison panels open at once: {FactionPlacementSettings.maxRegionPanels}",
                FactionPlacementSettings.maxRegionPanels, 1f, 8f));

            l.GapLine();
            l.Label("Regions and Societies features — toggle any off to avoid conflicts with other mods:");
            l.CheckboxLabeled("World-object integration (master)", ref Integration.WorldObjectIntegrationSettings.masterEnabled,
                "Master switch for the 0.7+ integration layer. Off means R&T governs only vanilla objects.");
            l.CheckboxLabeled("Settlement tiers & capitals", ref Integration.WorldObjectIntegrationSettings.settlementTiers,
                "Structural tiers (village → metropolis) from each faction's settlement pyramid, and the capital star marker.");
            l.CheckboxLabeled("Seed outposts at world generation", ref Integration.WorldObjectIntegrationSettings.outpostSeeding,
                "Place outposts around settlements up to each territory's tier-based allowance during world generation. Needs a compatibility patch that contributes an outpost creator (e.g. the Outposts Expanded patch).");
            l.CheckboxLabeled("Population caps (model only)", ref Integration.WorldObjectIntegrationSettings.populationCaps,
                "Model a per-tier population cap that settlements drift toward. Never adds or removes the player's real colonists.");

            if (Integration.WorldObjectIntegrationSettings.populationCaps)
            {
                float mult = Integration.WorldObjectIntegrationSettings.populationCapMultiplier;
                mult = Mathf.RoundToInt(l.SliderLabeled(
                    $"   Population cap multiplier: {mult:0}  (metropolis ≈ {15 * mult:0} pawns, village ≈ {mult:0})",
                    mult, 5f, 60f));
                Integration.WorldObjectIntegrationSettings.populationCapMultiplier = mult;

                float growth = Integration.WorldObjectIntegrationSettings.growthRateMultiplier;
                growth = l.SliderLabeled(
                    $"   Population growth rate: {growth:0.0}× real  (a healthy town grows ~{1.5f * growth:0}%/yr)",
                    growth,
                    Integration.WorldObjectIntegrationSettings.GrowthRateMultiplierMin,
                    Integration.WorldObjectIntegrationSettings.GrowthRateMultiplierMax);
                Integration.WorldObjectIntegrationSettings.growthRateMultiplier = (float)System.Math.Round(growth, 1);
            }

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
