namespace RegionsAndSocieties.Demographics
{
    /// <summary>
    /// The public seam companion mods drive to skew a region's demographics over time (0.2.0, #11).
    /// Core models the deterministic baseline and owns nothing about drafting or war; a mod that knows
    /// those events happened — World Domination, Empire, a drafting patch, the storyteller — reports
    /// them here and Core folds the skew into every read (endpoint, overlay, region panel).
    ///
    /// <para>This is a thin, stable facade over <see cref="RegionDemographicsStress"/>: the method
    /// shapes here are the contract external mods bind to, insulated from how the override layer stores
    /// or decays things internally. All calls are no-ops until a world is loaded and take a region
    /// (province) id; they cost memory only for regions actually stressed, and persist through save/load.</para>
    /// </summary>
    public static class DemographicHooks
    {
        /// <summary>
        /// A draft (or any temporary pull on one sex) is in progress in a region: shift its sex ratio
        /// now and hold the shift until <see cref="EndDraft"/> clears it. <paramref name="femaleDelta"/>
        /// is the shift on the female fraction — positive when men are the ones pulled away (the default
        /// men-first draft), negative when a culture drafts women first. Call again to update the amount;
        /// the <paramref name="tag"/> keeps repeat calls from stacking.
        /// </summary>
        public static void BeginDraft(int regionId, float femaleDelta, string tag = "draft")
        {
            RegionDemographicsStress.SkewSexRatio(regionId, femaleDelta, durationTicks: 0, tag: tag);
        }

        /// <summary>End a transient draft skew previously begun with <see cref="BeginDraft"/> (by tag, or
        /// all sex skews when tag is null). The ratio returns to baseline immediately.</summary>
        public static void EndDraft(int regionId, string tag = "draft")
        {
            RegionDemographicsStress.ClearSexSkew(regionId, tag);
        }

        /// <summary>
        /// A battle in or over a region killed <paramref name="maleDeaths"/> men and
        /// <paramref name="femaleDeaths"/> women. A lopsided toll (men, in a men-first draft) leaves the
        /// region short of that sex and recovers over the configured generation length (default 15 years,
        /// player-tunable). Repeated battles compound the scar and restart its recovery.
        /// </summary>
        public static void RecordCombatLosses(int regionId, int maleDeaths, int femaleDeaths)
        {
            RegionDemographicsStress.RecordCombatLosses(regionId, maleDeaths, femaleDeaths);
        }

        /// <summary>
        /// The general seam: apply an arbitrary sex-ratio skew to a region. <paramref name="durationTicks"/>
        /// of 0 holds until cleared (transient); a positive value decays it linearly to zero over that many
        /// ticks (generational). For most callers <see cref="BeginDraft"/> and
        /// <see cref="RecordCombatLosses"/> are the intended entry points; this is the escape hatch.
        /// </summary>
        public static void SkewSexRatio(int regionId, float femaleDelta, int durationTicks = 0, string tag = null)
        {
            RegionDemographicsStress.SkewSexRatio(regionId, femaleDelta, durationTicks, tag);
        }

        /// <summary>The net sex skew currently in force on a region (delta on the female fraction, positive
        /// = more female than the deterministic baseline). Zero when nothing stresses it.</summary>
        public static float CurrentFemaleDelta(int regionId)
        {
            return RegionDemographicsStress.CurrentFemaleDelta(regionId);
        }
    }
}
