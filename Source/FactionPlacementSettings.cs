using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RegionsAndSocieties
{
    public class FactionPlacementProfile : IExposable
    {
        public string factionDefName;
        public float mineralWeight = 1.0f;
        public float nutritionWeight = 1.0f;
        public float forageWeight = 1.0f;
        public float grazingWeight = 1.0f;
        public float huntingWeight = 1.0f;
        public float marginWeight = 0.0f;

        /// <summary>#47: the faction's share of the placed territories, a raw weight shown to the player as
        /// a "%". Shares across factions need not sum to 100 — worldgen normalises by their sum, so a share
        /// is a relative weight, not a hard quota. 0 = unset: resolved on first read to the migrated old
        /// range midpoint (existing saves) or the neutral default (new profiles).</summary>
        public float placementShare = 0f;

        /// <summary>Legacy Settlement Range (#47 removed it from the UI). Kept only so an old save's value
        /// can be read and converted to <see cref="placementShare"/> on load; it is no longer written back
        /// or consumed at worldgen.</summary>
        public IntRange baseCountRange = new IntRange(5, 15);
        public int placementOrder = 3;

        /// <summary>Kin/clustering: the MINIMUM number of regions a cluster must have to become its own kin
        /// faction (the min-cluster-size clamp — pirates 3, tribes 5, rough unions 7 by default). Combined
        /// with <see cref="numberOfClusters"/>: kin = min(numberOfClusters, floor(regions / clusterSize)).
        /// -1 = unset (resolve to the kind default on first read).</summary>
        public int clusterSize = -1;

        /// <summary>Kin/clustering: the NUMBER OF CLUSTERS the faction divides into (equal division / the max
        /// kin-faction cap — pirates 5, tribes 3, rough unions 2, cohesive factions 1). 0 = one cluster per
        /// region (maximum fragmentation). -1 = unset (resolve to the kind default). The Empire is always 1
        /// and not adjustable.</summary>
        public int numberOfClusters = -1;

        /// <summary>Per-faction kin toggle (replaces the blanket split switch): whether this faction, when
        /// its territory scatters, is organised into geographically separated regional groups. Clustering
        /// (largest contiguous body) is a separate idea. -1 = unset (resolve to the kind default), 0 = off,
        /// 1 = on. Stored as an int so "unset" is distinguishable and a per-kind default can fill it in.</summary>
        public int enableKinRaw = -1;

        public FactionPlacementProfile() { }

        public FactionPlacementProfile(string defName, float mineral, float nutrition, float forage, float grazing, float hunting, float margin, int minB, int maxB, int order)
        {
            this.factionDefName = defName;
            this.mineralWeight = mineral;
            this.nutritionWeight = nutrition;
            this.forageWeight = forage;
            this.grazingWeight = grazing;
            this.huntingWeight = hunting;
            this.marginWeight = margin;
            this.baseCountRange = new IntRange(minB, maxB);
            this.placementOrder = order;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref factionDefName, "factionDefName");
            Scribe_Values.Look(ref mineralWeight, "mineralWeight", 1.0f);
            Scribe_Values.Look(ref nutritionWeight, "nutritionWeight", 1.0f);
            Scribe_Values.Look(ref forageWeight, "forageWeight", 1.0f);
            Scribe_Values.Look(ref grazingWeight, "grazingWeight", 1.0f);
            Scribe_Values.Look(ref huntingWeight, "huntingWeight", 1.0f);
            Scribe_Values.Look(ref marginWeight, "marginWeight", 0.0f);
            Scribe_Values.Look(ref placementShare, "placementShare", 0f);
            // #47: still READ the legacy range so an old save can be migrated, but never WRITE it — new
            // saves carry the share only. On load, if the save predates the share, convert the range midpoint.
            if (Scribe.mode != LoadSaveMode.Saving)
            {
                Scribe_Values.Look(ref baseCountRange, "baseCountRange", new IntRange(5, 15));
                if (Scribe.mode == LoadSaveMode.LoadingVars && placementShare <= 0f)
                {
                    placementShare = Placement.PlacementShareRules.MigrateRangeToShareWeight(baseCountRange.min, baseCountRange.max);
                }
            }
            Scribe_Values.Look(ref placementOrder, "placementOrder", 3);
            Scribe_Values.Look(ref clusterSize, "clusterSize", -1);
            Scribe_Values.Look(ref numberOfClusters, "numberOfClusters", -1);
            Scribe_Values.Look(ref enableKinRaw, "enableKinRaw", -1);
        }
    }

    public class FactionPlacementSettings : ModSettings
    {
        public static Dictionary<string, FactionPlacementProfile> profiles = new Dictionary<string, FactionPlacementProfile>();

        /// <summary>Target tiles per region — the size the subdivision aims for. Sparse biomes scale UP
        /// automatically (a biome-size weight multiplies this: temperate ~1x, tundra ~2x, desert ~3x, ice
        /// ~10x), so a barren stretch makes fewer, larger regions from the same target. The merge floor
        /// (regions smaller than half the target are merged away) is derived from this, so it is the one
        /// region-size knob. Replaces the old separate min/max sliders.</summary>
        public static int targetRegionSize = 150;

        /// <summary>The world-partition algorithm applied to NEWLY generated worlds, by
        /// <see cref="Partition.IRegionPartitioner.AlgorithmId"/>. An existing save keeps the algorithm it
        /// was generated with (stamped on the world), so changing this never re-cuts a live map.</summary>
        public static string partitionAlgorithmId = Partition.RegionPartitionerRegistry.DefaultAlgorithmId;

        /// <summary>
        /// Dev-only knobs, not in the settings UI (set them in the mod-settings XML). They exist for the
        /// #38 worldgen perf matrix: <c>devQuicktestCoverage</c> above 0 overrides the planet coverage of a
        /// <c>-quicktest</c> launch (vanilla quicktest fixes it at 30%) and <c>devQuicktestSeed</c> pins the
        /// world seed, so a scripted run can generate the same world at 30/50/100%. Neither has any effect
        /// on a normal game.
        /// </summary>
        public static float devQuicktestCoverage = 0f;
        public static string devQuicktestSeed = "";
        // #54 RP2 calibration sweep: on a -quicktest launch under Realistic Planets 2, override RP2's
        // Planet Scale (subcount, ~5..11; 0 = leave default) and sea level (ordinal 0=Low..4=High; -1 =
        // leave default) so a scripted run can measure the tile count / land fraction at each. RP2-only.
        public static int devQuicktestSubcount = 0;
        public static int devQuicktestSeaLevel = -1;

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
        public static bool strictTerritorialOwnershipDefault = false;

        /// <summary>
        /// #18 default for the per-world <b>region lock</b> — whether placement refuses a holding in a
        /// region a rival holds exclusively (the hard territory refusal). On by default. A world with no
        /// explicit choice follows this; the flag itself lives on the world (SynapseRegionManager) and is
        /// toggleable mid-game, so changing this default never rewrites a world that already decided.
        /// </summary>
        public static bool regionLockDefault = true;

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
        public static int maxRegionPanels = 5;

        /// <summary>
        /// Set when the player dismisses the "no map-mode framework loaded" popup with "Don't show this
        /// again" (#81), so the either-or warning never nags on subsequent launches once acknowledged.
        /// </summary>
        public static bool mapFrameworkWarningDismissed = false;

        /// <summary>#53: the master switch for the whole Societies layer — population, demographics and
        /// economy. Off means Regions only: the partition, territories, borders, placement and their map
        /// modes still run, but nothing models or draws population/demographics/economy, and none of it
        /// ticks. Default on. Read everywhere through <see cref="RegionsAndSocietiesMod.SocietiesEnabled"/>.</summary>
        public static bool societiesEnabled = true;

        /// <summary>#51: keep tiny land regions (&le; TinyRegionMaxTiles) instead of dropping them. Off
        /// (default) drops a 1-6 tile region — too small to serve a regional society — so its tiles become
        /// unassigned. On keeps it as a real, settle-able region that simply earns NO regional benefits
        /// (marked <see cref="GeographicProvince.benefitsSuppressed"/>): settlements may spawn there, but it
        /// gets no demographics/economy. A settlement/outpost speck is never orphaned either way.</summary>
        public static bool enableSmallRegions = false;

        /// <summary>#47: which view the Geographic Placement Settings dialog opens in. Basic (default, false)
        /// shows one compact row per faction — share % and a live "≈ N regions" estimate — and fits the
        /// active faction list without scrolling. Advanced (true) is the full per-faction card: resource
        /// weights, placement order, clustering, and the same share row. Persisted so the choice sticks.</summary>
        public static bool placementUiAdvanced = false;

        /// <summary>Advanced-view layout: false = one card per faction, true = a dense table (every faction
        /// and setting in one grid, better for comparing while tuning). Persisted.</summary>
        public static bool placementUiTable = false;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref targetRegionSize, "targetRegionSize", 150);
            Scribe_Values.Look(ref devQuicktestCoverage, "devQuicktestCoverage", 0f);
            Scribe_Values.Look(ref devQuicktestSeed, "devQuicktestSeed", "");
            Scribe_Values.Look(ref devQuicktestSubcount, "devQuicktestSubcount", 0);
            Scribe_Values.Look(ref devQuicktestSeaLevel, "devQuicktestSeaLevel", -1);
            Scribe_Values.Look(ref claimedLandAreaPercent, "maxSettlementPercentOfRegions", 0.50f);
            Scribe_Values.Look(ref territoryCompactness, "territoryCompactness", 0.6f);
            Scribe_Values.Look(ref partitionAlgorithmId, "partitionAlgorithmId", Partition.RegionPartitionerRegistry.DefaultAlgorithmId);
            Scribe_Values.Look(ref strictTerritorialOwnershipDefault, "strictTerritorialOwnershipDefault", false);
            Scribe_Values.Look(ref regionLockDefault, "regionLockDefault", true);
            Scribe_Values.Look(ref showCalculationBreakdowns, "showCalculationBreakdowns", false);
            Scribe_Values.Look(ref regionPanelUseShift, "regionPanelUseShift", false);
            Scribe_Values.Look(ref maxRegionPanels, "maxRegionPanels", 5);
            Scribe_Values.Look(ref mapFrameworkWarningDismissed, "mapFrameworkWarningDismissed", false);
            Scribe_Values.Look(ref societiesEnabled, "societiesEnabled", true);
            Scribe_Values.Look(ref enableSmallRegions, "enableSmallRegions", false);
            Scribe_Values.Look(ref placementUiAdvanced, "placementUiAdvanced", false);
            Scribe_Values.Look(ref placementUiTable, "placementUiTable", false);

            // 0.7: world-object governance / mod-integration switches.
            Integration.WorldObjectIntegrationSettings.ExposeData();

            // Per-biome region-size overrides (empty = built-in defaults). The dict is the live store the
            // partition reads; scribe it so a player's biome tuning persists.
            Scribe_Collections.Look(ref Partition.BiomeRegionWeights.Overrides, "biomeRegionWeights", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars && Partition.BiomeRegionWeights.Overrides == null)
                Partition.BiomeRegionWeights.Overrides = new Dictionary<string, float>();

            List<FactionPlacementProfile> list = profiles.Values.ToList();
            Scribe_Collections.Look(ref list, "profiles", LookMode.Deep);
            // Rebuild the dict from the loaded list during LoadingVars — a Deep list is fully populated by
            // the time Look returns. RimWorld does not reliably re-invoke a mod-settings object's ExposeData
            // in the PostLoadInit pass, so gating the rebuild on PostLoadInit alone silently discarded every
            // saved profile and left GetProfile to lazily rebuild defaults (which is why saved placementShare
            // never took effect, #47). PostLoadInit is kept as a belt-and-braces second chance.
            if ((Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit) && list != null)
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
            // #46: a profile saved before cluster size existed carries 0; resolve it to the kind's default.
            if (p.clusterSize < 0) p.clusterSize = DefaultClusterSize(def);
            if (p.numberOfClusters < 0) p.numberOfClusters = DefaultClusterCount(def);
            // #47: a profile with no share yet (fresh, or from a save whose range midpoint was 0) gets the
            // kind's default share so the faction always has a slice.
            if (p.placementShare <= 0f) p.placementShare = DefaultShare(def);
            return p;
        }

        /// <summary>#47: the default placement share for a faction — the midpoint of the Settlement Range
        /// the old default profile would have carried, so the out-of-the-box distribution matches what
        /// players saw before shares existed (civil ~10, hostile ~5.5).</summary>
        public static float DefaultShare(FactionDef def)
        {
            var d = GetDefaultProfile(def);
            return Placement.PlacementShareRules.MigrateRangeToShareWeight(d.baseCountRange.min, d.baseCountRange.max);
        }

        /// <summary>The kin default for a faction (#63/#64): scattered low-tech factions (pirates, tribes,
        /// rough unions) form regional kin by default; the Empire never does (locked), spacer-tech factions
        /// default off (they still cluster), and a player can turn kin on for any non-Empire faction. The
        /// decision itself is the pure <see cref="Placement.SubFactionRules.KinDefault"/>.</summary>
        public static bool KinEnabledDefault(FactionDef def)
        {
            if (def == null) return false;
            var kind = Placement.ClusteringRules.ClassifyKind(def.defName, def.label, (int)def.techLevel, def.permanentEnemy, def.hostileToFactionlessHumanlikes);
            return Placement.SubFactionRules.KinDefault(kind, (int)def.techLevel, IsEmpire(def));
        }

        /// <summary>Whether kin is locked off (not player-overridable) for this faction — the Empire (#63).</summary>
        public static bool KinLocked(FactionDef def) => Placement.SubFactionRules.KinLocked(IsEmpire(def));

        /// <summary>Whether this faction forms regional kin — the per-faction choice, else the kind default.
        /// A locked faction (the Empire) is always off, whatever a stray override says.</summary>
        public static bool EffectiveEnableKin(FactionPlacementProfile p, FactionDef def)
        {
            if (KinLocked(def)) return false;
            if (p == null) return KinEnabledDefault(def);
            return p.enableKinRaw >= 0 ? p.enableKinRaw == 1 : KinEnabledDefault(def);
        }

        /// <summary>Default MINIMUM cluster size (min regions per kin faction) for a faction — pirates 3,
        /// tribes 5, rough unions 7, everyone else the kind default.</summary>
        public static int DefaultClusterSize(FactionDef def)
        {
            if (def == null) return Placement.ClusteringRules.Unbounded;
            var kind = Placement.ClusteringRules.ClassifyKind(def.defName, def.label, (int)def.techLevel, def.permanentEnemy, def.hostileToFactionlessHumanlikes);
            return Placement.ClusteringRules.DefaultClusterSize(kind);
        }

        /// <summary>Whether this def is the shattered Empire, which is always exactly one cluster (never kin)
        /// and whose cluster count is not adjustable.</summary>
        public static bool IsEmpire(FactionDef def) => def != null && def.defName == "Empire";

        /// <summary>Default NUMBER OF CLUSTERS (equal-division / max kin cap) for a faction — pirates 5,
        /// tribes 3, rough unions 2, cohesive factions 1. The Empire is always 1.</summary>
        public static int DefaultClusterCount(FactionDef def)
        {
            if (def == null) return 1;
            if (IsEmpire(def)) return 1;
            var kind = Placement.ClusteringRules.ClassifyKind(def.defName, def.label, (int)def.techLevel, def.permanentEnemy, def.hostileToFactionlessHumanlikes);
            return Placement.ClusteringRules.DefaultClusterCount(kind);
        }

        /// <summary>The number of clusters in force for a faction: the Empire is pinned to 1 (not
        /// adjustable), otherwise the per-faction value, else the kind default.</summary>
        public static int EffectiveClusterCount(FactionPlacementProfile p, FactionDef def)
        {
            if (IsEmpire(def)) return 1;
            if (p != null && p.numberOfClusters >= 0) return p.numberOfClusters;
            return DefaultClusterCount(def);
        }

        public static FactionPlacementProfile GetDefaultProfile(FactionDef def)
        {
            float mineral = 1.0f;
            float nutrition = 1.0f;
            float forage = 1.0f;
            float grazing = 1.0f;
            float hunting = 1.0f;
            float margin = 0.0f;
            int minB = 5;
            int maxB = 15;

            if (def.techLevel >= TechLevel.Spacer)
            {
                mineral = 2.5f;
                nutrition = 0.5f;
                forage = 0.1f;
                grazing = 0.1f;
                hunting = 0.2f;
                margin = 0.0f;
            }
            else if (def.techLevel == TechLevel.Industrial)
            {
                mineral = 1.0f;
                nutrition = 2.0f;
                forage = 0.2f;
                grazing = 0.8f;
                hunting = 0.8f;
                margin = 0.0f;
            }
            else
            {
                mineral = 0.2f;
                nutrition = 0.2f;
                forage = 2.0f;
                if (def.hostileToFactionlessHumanlikes || def.permanentEnemy)
                {
                    grazing = 0.2f;
                    hunting = 2.0f;
                }
                else
                {
                    grazing = 2.0f;
                    hunting = 0.2f;
                }
                margin = 0.1f;
            }

            int order = 3;
            if (def.defName == "Empire")
            {
                order = 2;
            }
            else if (def.techLevel == TechLevel.Industrial)
            {
                order = 1;
            }
            else if (def.techLevel >= TechLevel.Spacer)
            {
                order = 3;
            }
            else
            {
                order = 4;
            }

            if (def.hostileToFactionlessHumanlikes || def.permanentEnemy)
            {
                margin = 2.5f;
                minB = 3;
                maxB = 8;
            }

            var profile = new FactionPlacementProfile(def.defName, mineral, nutrition, forage, grazing, hunting, margin, minB, maxB, order);
            profile.clusterSize = DefaultClusterSize(def);   // #46 (min cluster size)
            profile.numberOfClusters = DefaultClusterCount(def);
            profile.placementShare = Placement.PlacementShareRules.MigrateRangeToShareWeight(minB, maxB);   // #47
            return profile;
        }
    }
}
