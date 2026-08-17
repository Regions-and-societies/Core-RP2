using System;

namespace RegionsAndSocieties.Economy
{
    /// <summary>
    /// Every number in the resource model, in one table — the same treatment <c>PlacementRules</c>
    /// and <c>SettlementSizeRules</c> got, and for the same reason: 0.8 is Logic Externalization and
    /// these need to be one object to move, not constants scattered through an evaluator.
    ///
    /// The renewal rates are expressed as <b>fractions of the cap per year</b> rather than as
    /// absolute amounts. That is deliberate: a province's cap already scales with its tile count and
    /// terrain, so a fractional rate means a large province and a small one recover at the same
    /// *pace* while differing in absolute output, which is what makes provinces comparable at all.
    /// </summary>
    public static class ResourceRules
    {
        /// <summary>Ticks in a RimWorld year. The model reasons in years; the game ticks.</summary>
        public const int TicksPerYear = 3600000;

        // -- renewal ----------------------------------------------------------

        /// <summary>
        /// Fraction of cap a biological resource regrows per year. A quarter means a stripped
        /// province is back to full in about four years of being left alone — slow enough to hurt,
        /// fast enough that a recoverable mistake stays recoverable.
        /// </summary>
        public const float BiologicalRenewalPerYear = 0.25f;

        /// <summary>
        /// Fraction of cap a geological resource recovers per year <b>at full scanning capability</b>,
        /// and only then. Twenty years from empty to full with perfect research and the best people
        /// available — minerals are supposed to be the resource you can genuinely run out of, and a
        /// faction that cannot scan recovers none of this at all.
        /// </summary>
        public const float GeologicalRenewalPerYear = 0.05f;

        // -- scanning capability ----------------------------------------------

        /// <summary>
        /// Intellectual skill at which a pawn contributes nothing to scanning. Below this the
        /// long-range scanner is being operated by someone who cannot read it.
        /// </summary>
        public const float MinCompetenceSkill = 8f;

        /// <summary>Intellectual skill at which a pawn is a fully effective scanner.</summary>
        public const float FullCompetenceSkill = 16f;

        /// <summary>
        /// Research progress below which scanning is not happening at all. Deep drilling and
        /// long-range scanning sit well up the tech tree, so an early faction gets nothing from
        /// having a clever pawn: the equipment does not exist yet.
        /// </summary>
        public const float MinResearchForScanning = 0.35f;

        // -- extraction --------------------------------------------------------

        /// <summary>
        /// Units drawn per resident per year, before the settlement's tier multiplier.
        ///
        /// This is the one number in the table with no grounding behind it yet. The renewal rates
        /// are fractions of a cap the game already computes, and the competence thresholds are read
        /// off RimWorld's own 0-20 skill scale — but per-capita draw has to be calibrated against
        /// what Empire production and VOE yields actually consume, which needs the mods loaded.
        /// Treat it as the tuning knob, and tune it against
        /// <see cref="ResourceEvaluator.SustainablePopulation"/> rather than in isolation.
        /// </summary>
        public const float ExtractionPerResidentPerYear = 0.5f;

        /// <summary>
        /// Floor on how much of a province's stock a single year of extraction may remove, as a
        /// fraction of cap. Without it, a large enough population empties any province in one tick
        /// of the economy, and depletion stops being a curve the player can react to.
        /// </summary>
        public const float MaxAnnualDrawFraction = 0.5f;
    }
}
