using Verse;

namespace RegionsAndSocieties.Integration
{
    /// <summary>
    /// Player-facing switches for the 0.7 world-object governance layer.
    ///
    /// Everything here defaults to ON when the backing mod is present, and every switch is a pure
    /// no-op path when off — that is the "optional additions" requirement for 0.7. Persisted by
    /// <see cref="FactionPlacementSettings"/>, which owns the mod's ModSettings instance.
    /// </summary>
    public static class WorldObjectIntegrationSettings
    {
        /// <summary>Master switch. Off means R&amp;T governs only vanilla objects, as before 0.7.</summary>
        public static bool masterEnabled = true;

        // --- Per-mod integrations -------------------------------------------------
        // Empire, VFE, World Domination and VOE moved to their compatibility patches
        // (Regions-and-societies/{Empire,VFE,World-Domination,VOE}-CP); a patch is enabled by
        // being installed, so per-mod toggles are gone.

        // --- Per-mechanic switches ------------------------------------------------
        /// <summary>Gate placement of foreign world objects on region ownership and supply range.</summary>
        public static bool placementGovernance = true;

        /// <summary>Apply security/ownership, resource-cap, and local-richness modifiers to production.</summary>
        public static bool economyGovernance = true;

        /// <summary>Apply adjacency and supply-line restrictions to military and expansion actions.</summary>
        public static bool militaryGovernance = true;

        /// <summary>Classify settlements into village/town/city/major-city tiers.</summary>
        public static bool settlementTiers = true;

        /// <summary>
        /// Seed VOE outposts around settlements at world generation, up to each territory's
        /// tier-based allowance (0.8). Off means a generated world carries only the settlements
        /// vanilla and the faction placer produced, as before 0.8.
        /// </summary>
        public static bool outpostSeeding = true;

        /// <summary>
        /// Model a per-tier population cap (0.8): a settlement's size drifts toward two-thirds of
        /// <c>territories-for-tier × multiplier × tech-factor</c>. Model-only for the player — never
        /// adds or removes real colonists. Off means population is left to the pre-0.8 estimate.
        /// </summary>
        public static bool populationCaps = true;

        /// <summary>Cap multiplier — a tier's cap is its required-territories count times this.
        /// Player-tunable via the mod-menu slider. Default 30 (0.3.0; was 10) → T1 caps at 30, T5 at 450
        /// (industrial), so a settled region reads in the hundreds.</summary>
        public static float populationCapMultiplier = DefaultPopulationCapMultiplier;

        /// <summary>Mirror of <c>Sizing.PopulationCapRules.DefaultMultiplier</c> — kept as a literal here so
        /// this file stays compilable in the dependency-free test suites. Keep the two equal.</summary>
        public const float DefaultPopulationCapMultiplier = 30f;

        /// <summary>Set once the 0.3.0 cap-multiplier rescale has been applied to a saved settings file,
        /// so a value of exactly 10 left over from the old default is lifted to 30 once and a player who
        /// later chooses 10 on purpose keeps it.</summary>
        private static bool capMultiplierRescaled030;

        /// <summary>How fast settlement populations grow, as a multiple of real-world demographic rates
        /// (#6). Real growth (~1-2%/yr) is invisible over a playthrough, so the default 10× makes a
        /// healthy town grow ~10-15%/yr. Scales births and deaths together, so the balance point is
        /// unchanged — only the pace. Player-tunable 0.5×–20×.</summary>
        public static float growthRateMultiplier = 10f;
        public const float GrowthRateMultiplierMin = 0.5f;
        public const float GrowthRateMultiplierMax = 20f;

        /// <summary>Demographic pressure reach multiplier: a settlement's radius is its population × this.
        /// Higher = beliefs carry further; lower = borders contest sooner. Live-tunable.</summary>
        public static float demographicReach = 1.0f;

        /// <summary>Demographic pressure falloff shape parameter (steepness of the chosen model). Live-tunable.</summary>
        public static float demographicFalloff = 1.0f;

        /// <summary>Which falloff curve the demographic pressure follows — index into
        /// <c>DemographicsRules.FalloffModel</c> (0 Linear, 1 Smoothstep, 2 Logarithmic, 3 Exponential,
        /// 4 InverseSquare). Live-tunable so tuning can move off linear.</summary>
        public static int demographicFalloffModel = 0;

        /// <summary>
        /// How many in-game years a "generational" demographic skew takes to decay back to baseline —
        /// the scar a mod records through <c>DemographicHooks.RecordCombatLosses</c> when a war
        /// devastates a region (#11). Transient skews (a draft in progress) don't use this; they hold
        /// until cleared. Live-tunable; default 15.
        /// </summary>
        public static float demographicGenerationYears = 15f;

        // --- Diagnostics ----------------------------------------------------------
        /// <summary>Log each world-object type that no adapter or heuristic could classify (once per type).</summary>
        public static bool logUnknownWorldObjects = true;

        public static void ExposeData()
        {
            Scribe_Values.Look(ref masterEnabled, "integration_masterEnabled", true);

            Scribe_Values.Look(ref placementGovernance, "integration_placementGovernance", true);
            Scribe_Values.Look(ref economyGovernance, "integration_economyGovernance", true);
            Scribe_Values.Look(ref militaryGovernance, "integration_militaryGovernance", true);
            Scribe_Values.Look(ref settlementTiers, "integration_settlementTiers", true);
            Scribe_Values.Look(ref outpostSeeding, "integration_outpostSeeding", true);
            Scribe_Values.Look(ref populationCaps, "integration_populationCaps", true);
            Scribe_Values.Look(ref populationCapMultiplier, "integration_populationCapMultiplier", DefaultPopulationCapMultiplier);
            Scribe_Values.Look(ref capMultiplierRescaled030, "integration_capMultiplierRescaled030", false);
            if (!capMultiplierRescaled030)
            {
                // One-time 0.3.0 rescale: a settings file written under the old default (10) carries that
                // value explicitly, so it would otherwise pin the old scale forever. Runs once whichever
                // way the file is being scribed; the flag is then written true.
                if (populationCapMultiplier == 10f) populationCapMultiplier = DefaultPopulationCapMultiplier;
                capMultiplierRescaled030 = true;
            }
            Scribe_Values.Look(ref growthRateMultiplier, "integration_growthRateMultiplier", 10f);
            Scribe_Values.Look(ref demographicReach, "integration_demographicReach", 1.0f);
            Scribe_Values.Look(ref demographicFalloff, "integration_demographicFalloff", 1.0f);
            Scribe_Values.Look(ref demographicFalloffModel, "integration_demographicFalloffModel", 0);
            Scribe_Values.Look(ref demographicGenerationYears, "integration_demographicGenerationYears", 15f);

            Scribe_Values.Look(ref logUnknownWorldObjects, "integration_logUnknownWorldObjects", true);
        }

        // Convenience accessors so call sites read as intent rather than as boolean algebra.

        public static bool PlacementGovernanceActive
        {
            get { return masterEnabled && placementGovernance; }
        }

        public static bool EconomyGovernanceActive
        {
            get { return masterEnabled && economyGovernance; }
        }

        public static bool MilitaryGovernanceActive
        {
            get { return masterEnabled && militaryGovernance; }
        }

        public static bool SettlementTiersActive
        {
            get { return masterEnabled && settlementTiers; }
        }

        public static bool OutpostSeedingActive
        {
            get { return masterEnabled && outpostSeeding; }
        }

        public static bool PopulationCapsActive
        {
            get { return masterEnabled && populationCaps; }
        }

    }
}
