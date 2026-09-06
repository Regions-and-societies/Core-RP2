using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RegionsAndSocieties
{
    // FactionPlacementProfile moved to Source/Placement/FactionPlacementProfile.cs (#55) so the defaults
    // registry and its tests can build profiles without the game. Same namespace, same public shape.

    public class FactionPlacementSettings : ModSettings
    {
        public static Dictionary<string, FactionPlacementProfile> profiles = new Dictionary<string, FactionPlacementProfile>();
        public static int minRegionSize = 75;
        public static int maxRegionSize = 150;

        /// <summary>The world-partition algorithm applied to NEWLY generated worlds, by
        /// <see cref="Partition.IRegionPartitioner.AlgorithmId"/>. An existing save keeps the algorithm it
        /// was generated with (stamped on the world), so changing this never re-cuts a live map.</summary>
        public static string partitionAlgorithmId = Partition.RegionPartitionerRegistry.DefaultAlgorithmId;
        public static float maxThreatPercent = 0.50f;

        /// <summary>
        /// Dev-only knobs, not in the settings UI (set them in the mod-settings XML). They exist for the
        /// #38 worldgen perf matrix: <c>devQuicktestCoverage</c> above 0 overrides the planet coverage of a
        /// <c>-quicktest</c> launch (vanilla quicktest fixes it at 30%) and <c>devQuicktestSeed</c> pins the
        /// world seed, so a scripted run can generate the same world at 30/50/100%. Neither has any effect
        /// on a normal game.
        /// </summary>
        public static float devQuicktestCoverage = 0f;
        public static string devQuicktestSeed = "";

        /// <summary>
        /// #51: the single density knob — the target fraction of livable LAND area claimed by territories.
        /// Worldgen sizes total settlement volume to this (against the count of land provinces, the unit of
        /// claimed ground), instead of the old raw tile-count scaling that made planets wall-to-wall and
        /// exploded on large worlds. It is area-weighted (land provinces, ocean excluded) and drives the
        /// total both up and down, so the per-faction counts set only the distribution. Scribed under the
        /// legacy key so existing saves keep their value.
        /// </summary>
        public static float claimedLandAreaPercent = 0.50f;

        /// <summary>
        /// #19: how strongly territory growth prefers squaring off over spidering, 0..1. Candidate
        /// provinces below the desired embeddedness ratio have their suitability scaled down in
        /// proportion, blended in by this weight — 0 is the legacy purely-greedy behaviour, 1 the full
        /// shape penalty. A preference, never a rule: a cornered faction still takes the awkward
        /// province when its land is dramatically better.
        /// </summary>
        public static float territoryCompactness = 0.6f;

        /// <summary>
        /// Whether <b>newly generated</b> worlds enforce R&amp;T's settlement and outpost placement
        /// rules. Worlds already in progress decide for themselves on load and are not affected by
        /// this — a world built without the rules keeps compatibility mode, and one built with them
        /// keeps strict. See <c>SynapseRegionManager.StrictTerritorialOwnership</c>.
        /// </summary>
        public static bool strictTerritorialOwnershipDefault = true;

        /// <summary>
        /// Show the derivation breakdowns in region tooltips (ownership now; economics and produced
        /// goods later) so the numbers can be inspected without Development mode. Off by default (#54).
        /// </summary>
        public static bool showCalculationBreakdowns = false;

        /// <summary>True when calculation breakdowns should be shown — the setting, or Dev Mode.</summary>
        public static bool ShowCalculations => showCalculationBreakdowns || Prefs.DevMode;

        /// <summary>
        /// Which modifier opens a region comparison panel on click (#53): Shift+click when true,
        /// Ctrl+click when false. Configurable so it can be moved off a key that conflicts.
        /// </summary>
        public static bool regionPanelUseShift = false;

        /// <summary>
        /// How many region comparison panels may be open at once (#53). Default 2 for a side-by-side
        /// compare; raise it to experiment with more. When exceeded the oldest panel closes (FIFO).
        /// </summary>
        public static int maxRegionPanels = 2;

        /// <summary>
        /// Set when the player dismisses the "no map-mode framework loaded" popup with "Don't show this
        /// again" (#81), so the either-or warning never nags on subsequent launches once acknowledged.
        /// </summary>
        public static bool mapFrameworkWarningDismissed = false;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref minRegionSize, "minRegionSize", 75);
            Scribe_Values.Look(ref maxRegionSize, "maxRegionSize", 150);
            Scribe_Values.Look(ref maxThreatPercent, "maxThreatPercent", 0.50f);
            Scribe_Values.Look(ref devQuicktestCoverage, "devQuicktestCoverage", 0f);
            Scribe_Values.Look(ref devQuicktestSeed, "devQuicktestSeed", "");
            Scribe_Values.Look(ref claimedLandAreaPercent, "maxSettlementPercentOfRegions", 0.50f);
            Scribe_Values.Look(ref territoryCompactness, "territoryCompactness", 0.6f);
            Scribe_Values.Look(ref partitionAlgorithmId, "partitionAlgorithmId", Partition.RegionPartitionerRegistry.DefaultAlgorithmId);
            Scribe_Values.Look(ref strictTerritorialOwnershipDefault, "strictTerritorialOwnershipDefault", true);
            Scribe_Values.Look(ref showCalculationBreakdowns, "showCalculationBreakdowns", false);
            Scribe_Values.Look(ref regionPanelUseShift, "regionPanelUseShift", false);
            Scribe_Values.Look(ref maxRegionPanels, "maxRegionPanels", 2);
            Scribe_Values.Look(ref mapFrameworkWarningDismissed, "mapFrameworkWarningDismissed", false);

            // 0.7: world-object governance / mod-integration switches.
            Integration.WorldObjectIntegrationSettings.ExposeData();


            List<FactionPlacementProfile> list = profiles.Values.ToList();
            Scribe_Collections.Look(ref list, "profiles", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && list != null)
            {
                profiles.Clear();
                foreach (var p in list)
                {
                    if (p.factionDefName != null)
                    {
                        profiles[p.factionDefName] = p;
                    }
                }
            }
        }

        public static FactionPlacementProfile GetProfile(FactionDef def)
        {
            if (def == null) return null;
            if (!profiles.TryGetValue(def.defName, out var p))
            {
                p = GetDefaultProfile(def);
                profiles[def.defName] = p;
            }
            return p;
        }

        /// <summary>
        /// The default profile for a faction (#55): a curated registration — core's own (Empire) or a
        /// compatibility patch's for its factions — when one exists, otherwise the tech-level guess.
        /// Always a fresh instance; the caller may edit it. A profile the user has already saved wins
        /// over both, because <see cref="GetProfile"/> only asks here when nothing is saved.
        /// </summary>
        public static FactionPlacementProfile GetDefaultProfile(FactionDef def)
        {
            if (def == null) return null;
            if (FactionPlacementDefaults.TryGet(def.defName, out var registered)) return registered;
            bool hostile = def.hostileToFactionlessHumanlikes || def.permanentEnemy;
            return FactionPlacementDefaults.TechLevelDefault(def.defName, (int)def.techLevel, hostile);
        }
    }
}
