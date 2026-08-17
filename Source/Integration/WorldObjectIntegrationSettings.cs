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
        /// Player-tunable via the mod-menu slider. Default 10 → T1 caps at 10, T5 at 150 (industrial).</summary>
        public static float populationCapMultiplier = 10f;

        /// <summary>Demographic pressure reach multiplier: a settlement's radius is its population × this.
        /// Higher = beliefs carry further; lower = borders contest sooner. Live-tunable.</summary>
        public static float demographicReach = 1.0f;

        /// <summary>Demographic pressure falloff shape parameter (steepness of the chosen model). Live-tunable.</summary>
        public static float demographicFalloff = 1.0f;

        /// <summary>Which falloff curve the demographic pressure follows — index into
        /// <c>DemographicsRules.FalloffModel</c> (0 Linear, 1 Smoothstep, 2 Logarithmic, 3 Exponential,
        /// 4 InverseSquare). Live-tunable so tuning can move off linear.</summary>
        public static int demographicFalloffModel = 0;

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
            Scribe_Values.Look(ref populationCapMultiplier, "integration_populationCapMultiplier", 10f);
            Scribe_Values.Look(ref demographicReach, "integration_demographicReach", 1.0f);
            Scribe_Values.Look(ref demographicFalloff, "integration_demographicFalloff", 1.0f);
            Scribe_Values.Look(ref demographicFalloffModel, "integration_demographicFalloffModel", 0);

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
