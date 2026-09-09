using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;

namespace RegionsAndSocieties
{
    public class SynapseRegionManager : WorldComponent
    {
        private List<GeographicProvince> provinces = new List<GeographicProvince>();
        private int[] tileToProvinceId;
        private Dictionary<int, int> settlementPlacementOrder = new Dictionary<int, int>();

        // Modeled population per NPC settlement (keyed by tile id), grown over time by the birthrate
        // model (#6). Scribed so a settlement's size persists across saves. The player colony is never
        // in here — its size is the real free-colonist count.
        private Dictionary<int, float> settlementModeledPop = new Dictionary<int, float>();

        // One in-game day between growth ticks — growth is measured in years, so a daily step is smooth
        // and keeps the per-settlement sweep off the hot path.
        private const int GrowthTickInterval = 60000;

        public int GetSettlementPlacementOrder(int tileId)
        {
            if (settlementPlacementOrder != null && settlementPlacementOrder.TryGetValue(tileId, out int order))
            {
                return order;
            }
            return -1;
        }

        public void SetSettlementPlacementOrder(int tileId, int order)
        {
            if (settlementPlacementOrder == null)
            {
                settlementPlacementOrder = new Dictionary<int, int>();
            }
            settlementPlacementOrder[tileId] = order;
        }

        public int GetNextPlacementOrderForFaction(Faction faction)
        {
            int count = 0;
            foreach (var obj in Find.WorldObjects.AllWorldObjects)
            {
                // 0.7: classification is mod-agnostic — see Integration.WorldObjectClassifier.
                if (Integration.WorldObjectClassifier.IsSettlement(obj) && obj.Faction == faction)
                {
                    count++;
                }
            }
            return count + 1;
        }

        public List<GeographicProvince> Provinces
        {
            get
            {
                if (provinces == null || provinces.Count == 0)
                {
                    GenerateProvinces();
                }
                return provinces;
            }
        }

        public SynapseRegionManager(World world) : base(world)
        {
            InitializeData();
        }

        private void InitializeData()
        {
            if (tileToProvinceId == null && Find.WorldGrid != null)
            {
                tileToProvinceId = new int[Find.WorldGrid.TilesCount];
                for (int i = 0; i < tileToProvinceId.Length; i++)
                {
                    tileToProvinceId[i] = -1;
                }
            }
        }

        public int GetProvinceId(int tileId)
        {
            InitializeData();
            if (tileId < 0 || tileId >= tileToProvinceId.Length) return -1;
            return tileToProvinceId[tileId];
        }

        // O(1) id -> province index, rebuilt lazily whenever the province count changes (worldgen, merges).
        // After generation the list is stable, so this stays valid; the count check catches any change
        // without a manual invalidation call. GetProvince was an O(provinces) LINQ scan called per tile in
        // the placement rules — an O(tiles × provinces) storm on the settle screen of a large world.
        private Dictionary<int, GeographicProvince> _provinceById;

        public GeographicProvince GetProvince(int provinceId)
        {
            if (provinces == null) return null;
            if (_provinceById == null || _provinceById.Count != provinces.Count)
            {
                _provinceById = new Dictionary<int, GeographicProvince>(provinces.Count);
                for (int i = 0; i < provinces.Count; i++) _provinceById[provinces[i].id] = provinces[i];
            }
            return _provinceById.TryGetValue(provinceId, out var prov) ? prov : null;
        }

        public GeographicProvince GetProvinceForTile(int tileId)
        {
            int pid = GetProvinceId(tileId);
            if (pid == -1) return null;
            return GetProvince(pid);
        }

        // The per-region dynamic population offset the #5/#8 passes accumulate on top of the derived base.
        // Scribed, so a save keeps how its map has drifted. Effective population = base + this.
        private Dictionary<int, float> regionPopulationDelta = new Dictionary<int, float>();

        /// <summary>The dynamic population offset a region has accumulated from migration/accretion (#5/#8),
        /// on top of its derived base. Zero for a region that has never moved.</summary>
        public float PopulationDeltaOf(int regionId)
            => regionPopulationDelta != null && regionPopulationDelta.TryGetValue(regionId, out float v) ? v : 0f;

        /// <summary>Run a population-dynamics pass right now (the on-request path for the #5 endpoint and the
        /// debug action), so a consumer never reads a stale number after an event. Returns people migrated.</summary>
        public float RunPopulationDynamicsNow() => Integration.PopulationDynamics.RunPasses(this, regionPopulationDelta);

        // -1 unresolved, 0 compatibility (non-strict), 1 strict. An int rather than a bool because
        // "absent from this save" has to be distinguishable from "saved as false" — that
        // distinction is the whole mechanism for adopting a save R&T was not present for.
        private int strictTerritorialOwnershipRaw = -1;

        // Which population-density algorithm this world uses. Population is derived, not scribed, so
        // it recomputes on every load — which means a 0.7.1 world loaded under 0.7.2 would silently
        // switch to the new numbers. Stamping the world lets an existing save keep the density it was
        // built with. -1 unresolved; 1 legacy (0.7.1 and earlier: uncapped pockets, smeared totals
        // incl. the #55 overcount); 2 current (0.7.2+: capped/landmark-biased pockets, source totals).
        public const int DensityAlgorithmLegacy = 1;
        public const int DensityAlgorithmCurrent = 2;
        private int densityAlgorithmVersionRaw = -1;

        // Which region-partition algorithm built this world's provinces. Provinces ARE scribed, so an old
        // save keeps its shapes on load without re-partitioning; this stamp exists so a REGEN (the debug
        // action, or any future forced rebuild) reproduces the world with the algorithm it was born under,
        // and so new worlds get the new method by default. -1 unresolved; 1 legacy (anchor-Voronoi
        // PartitionLand, 0.2.x–early 0.3.0); 2 current (contain-then-subdivide PartitionByBasins).
        public const int PartitionAlgorithmLegacy = 1;
        public const int PartitionAlgorithmCurrent = 2;
        private int partitionAlgorithmVersionRaw = -1;

        /// <summary>
        /// The partition algorithm in force for this world. Only a save explicitly resolved to legacy (a
        /// world whose provinces predate the stamp) reports legacy; an unstamped live new world defaults
        /// to current, so new games get the contain-then-subdivide partition.
        /// </summary>
        public int PartitionAlgorithmVersion
        {
            get { return partitionAlgorithmVersionRaw == PartitionAlgorithmLegacy ? PartitionAlgorithmLegacy : PartitionAlgorithmCurrent; }
        }

        // The mod's worldgen/rendering version, stamped onto a world when its provinces are generated, so a
        // save records which build rendered it ("this is a 0.4.0 rendering"). Human-readable and finer than
        // the binary partition selector above — bump it with each release that changes worldgen. Persisted;
        // a save that predates the stamp resolves to a legacy label on load. 0.4.0: the map-handling
        // overhaul — shore-split inland lakes (#48), island absorb/chain (#49), tiny-region fold/drop (#51)
        // and ridge-aware ownership walls (#50) — is a materially different map from the 0.3.0 partition.
        public const string WorldGenVersion = "0.4.0";
        private string worldGenVersionRaw;

        /// <summary>The worldgen version this world was rendered by: the stamped value, or a legacy label
        /// for a save generated before the stamp existed.</summary>
        public string WorldGenVersionLabel
        {
            get { return string.IsNullOrEmpty(worldGenVersionRaw) ? "0.2.x or earlier" : worldGenVersionRaw; }
        }

        // The partition algorithm this world was generated with (an IRegionPartitioner.AlgorithmId). This
        // is the canonical, extensible successor to the binary PartitionAlgorithmVersion above: it is
        // stamped at generation, scribed, and drives any regenerate — so a world always re-cuts with the
        // algorithm it was born under, whichever mod supplied it. Empty until stamped / resolved on load.
        private string regionAlgorithmId;

        /// <summary>The id of the partition algorithm that generated this world (see
        /// <see cref="Partition.IRegionPartitioner"/>). Falls back to the current default if unresolved.</summary>
        public string RegionAlgorithmId
        {
            get { return string.IsNullOrEmpty(regionAlgorithmId) ? Partition.RegionPartitionerRegistry.DefaultAlgorithmId : regionAlgorithmId; }
        }

        // The partition inputs this world was generated with, stamped at first generation and scribed
        // beside regionAlgorithmId, so a regenerate (or a seed reproduction) rebuilds the SAME map even
        // if the mod settings have since changed — the reproducibility guarantee regionAlgorithmId
        // already gives, extended to the target region size and the small-region option. -1 = unset
        // (pre-stamp save, or a fresh world before its first generation stamps it). The other partition
        // knobs (lake cap, island reach, tiny cap, chain size, ridge class) are compile-time constants, so
        // they are pinned by WorldGenVersion rather than needing a per-world stamp.
        private int worldTargetRegionSize = -1;
        private int worldEnableSmallRegionsRaw = -1;   // -1 unset, 0 false, 1 true

        // #18 holding-seeding inputs. Maturity + the enable are worldgen inputs, stamped when seeding first
        // runs (ResolveSeedingInputs) so a regenerate reproduces the same buildout — mirrors the partition
        // stamps above. The region lock is different: a live, mid-game-toggleable governance flag (like
        // StrictTerritorialOwnership), unset meaning "follow the settings default" rather than "unstamped".
        private int worldSeedingMaturityRaw = -1;   // -1 unset, else round(maturity*1000)
        private int worldHoldingSeedingRaw = -1;    // -1 unset, 0 off, 1 on
        private int regionLockRaw = -1;             // -1 unset (use setting default), 0 off, 1 on

        /// <summary>Target tiles per region this world was cut with (stamped value, else the live setting).
        /// The subdivision aims for this (×biome weight); the merge floor is half of it.</summary>
        public int EffectiveTargetRegionSize
        {
            get { return worldTargetRegionSize > 0 ? worldTargetRegionSize : FactionPlacementSettings.targetRegionSize; }
        }

        /// <summary>Regions smaller than this are merged away — half the target, the one derived floor that
        /// the old separate "min size" slider used to set.</summary>
        public int EffectiveMergeFloor
        {
            get { return System.Math.Max(1, EffectiveTargetRegionSize / 2); }
        }

        /// <summary>Whether this world was cut keeping small regions (stamped value, else the live setting).</summary>
        public bool EffectiveEnableSmallRegions
        {
            get { return worldEnableSmallRegionsRaw >= 0 ? worldEnableSmallRegionsRaw == 1 : FactionPlacementSettings.enableSmallRegions; }
        }

        /// <summary>#18 world maturity this world seeded at (stamped value, else the live setting).</summary>
        public float EffectiveSeedingMaturity
        {
            get { return worldSeedingMaturityRaw >= 0 ? worldSeedingMaturityRaw / 1000f : Integration.WorldObjectIntegrationSettings.seedingMaturity; }
        }

        /// <summary>#18 whether this world seeds holdings at generation (stamped value, else the live setting).</summary>
        public bool EffectiveHoldingSeedingEnabled
        {
            get { return worldHoldingSeedingRaw >= 0 ? worldHoldingSeedingRaw == 1 : Integration.WorldObjectIntegrationSettings.OutpostSeedingActive; }
        }

        /// <summary>Stamp the seeding inputs (maturity + enable) from the live settings the first time
        /// seeding runs on this world, so a regenerate reproduces the same buildout even if the settings
        /// later change — the reproducibility guarantee the partition inputs already carry.</summary>
        public void ResolveSeedingInputs()
        {
            if (worldSeedingMaturityRaw < 0)
            {
                int raw = (int)System.Math.Round(Integration.WorldObjectIntegrationSettings.seedingMaturity * 1000f);
                worldSeedingMaturityRaw = raw < 0 ? 0 : (raw > 1000 ? 1000 : raw);
            }
            if (worldHoldingSeedingRaw < 0)
                worldHoldingSeedingRaw = Integration.WorldObjectIntegrationSettings.OutpostSeedingActive ? 1 : 0;
        }

        /// <summary>
        /// #18 region lock: whether placement refuses a holding in a region a rival holds exclusively (the
        /// hard territory refusal in <see cref="Placement.PlacementEvaluator"/>). The rest of the ownership
        /// rules — buffer, separation, supply range, foothold — are unaffected. Toggleable mid-game, like
        /// <see cref="StrictTerritorialOwnership"/>; unset follows <see cref="FactionPlacementSettings.regionLockDefault"/>.
        /// </summary>
        public bool RegionLock
        {
            get { return EffectiveRegionLock; }
            set { regionLockRaw = value ? 1 : 0; }
        }

        /// <summary>The region-lock flag in force for this world — the per-world choice, else the default.</summary>
        public bool EffectiveRegionLock
        {
            get { return regionLockRaw >= 0 ? regionLockRaw == 1 : FactionPlacementSettings.regionLockDefault; }
        }

        /// <summary>
        /// The density algorithm in force for this world. Only a save explicitly resolved to legacy
        /// (a pre-0.7.2 world) reports legacy; an unstamped live new world defaults to current.
        /// </summary>
        public int DensityAlgorithmVersion
        {
            get { return densityAlgorithmVersionRaw == DensityAlgorithmLegacy ? DensityAlgorithmLegacy : DensityAlgorithmCurrent; }
        }

        /// <summary>
        /// Whether this world enforces R&amp;T's placement rules for settlements and outposts.
        ///
        /// <para><b>Strict</b> (worlds generated with R&amp;T): buffers, supply, footholds and the
        /// one-holding-per-province assumptions all apply, as they have since 0.7.</para>
        ///
        /// <para><b>Compatibility</b> (R&amp;T added to a world already in progress): placement is
        /// left entirely to vanilla and to whatever other mods are doing it. Provinces are still
        /// generated and territory is still owned and drawn — only the rules that would refuse a
        /// placement stand down, because a world that was built without them is already full of
        /// settlements those rules would have forbidden.</para>
        /// </summary>
        public bool StrictTerritorialOwnership
        {
            get { return strictTerritorialOwnershipRaw != 0; }
            set { strictTerritorialOwnershipRaw = value ? 1 : 0; }
        }

        /// <summary>True once the mode has been decided for this world, either on load or at worldgen.</summary>
        public bool StrictOwnershipResolved
        {
            get { return strictTerritorialOwnershipRaw != -1; }
        }

        /// <summary>
        /// Decide the mode for a save that predates the flag.
        ///
        /// <para>The discriminator is <b>provinces, not the flag</b>. A save made with R&amp;T 0.7
        /// also has no flag yet, but it does have generated provinces — that world was built under
        /// the placement rules and must keep them. A save with neither is one R&amp;T has just been
        /// added to, and its existing settlements were placed with no regard for our rules, so
        /// enforcing them now would refuse placements next to towns that already exist.</para>
        /// </summary>
        /// <summary>
        /// Test seam: the province list without the lazy generation the <see cref="Provinces"/>
        /// getter performs. A case that needs to simulate "this save had no provinces" cannot use
        /// the getter, because reading it is what builds them.
        /// <para>Public rather than internal because the TestRunner is a separate assembly.</para>
        /// </summary>
        public List<GeographicProvince> ProvincesRaw
        {
            get { return provinces; }
        }

        /// <summary>
        /// Test seam: put the flag back to unresolved so the load-time decision can be exercised.
        /// Not part of normal operation — a live world has already decided.
        /// </summary>
        public void ResetStrictOwnershipForTesting()
        {
            strictTerritorialOwnershipRaw = -1;

            // A test that exercises the compat branch would otherwise arm the player notice and
            // drop a letter into the live test colony on the next tick. Restore what we touch.
            pendingCompatibilityNotice = false;
        }

        /// <summary>Test seam: run the load-time decision directly, without a save round trip.</summary>
        public void ResolveStrictOwnershipForTesting()
        {
            ResolveStrictOwnershipForLoadedSave();

            // The compat branch arms a player-facing letter. Tests run inside a live colony, so
            // leaving it armed would drop that letter on the next tick. The decision is what these
            // cases exercise; the notice is deliberately not.
            pendingCompatibilityNotice = false;
        }

        private void ResolveStrictOwnershipForLoadedSave()
        {
            if (strictTerritorialOwnershipRaw != -1) return;

            bool hadProvinces = provinces != null && provinces.Count > 0;
            strictTerritorialOwnershipRaw = hadProvinces ? 1 : 0;

            Log.Message(hadProvinces
                ? "[RegionsAndSocieties] Save predates the territorial-ownership flag but has generated provinces: treating as strict."
                : "[RegionsAndSocieties] Save has no province data: adopting it in compatibility mode. Regions will be generated; placement rules stand down.");

            // Tell the player, not just the log. Somebody who installs mid-playthrough gets a
            // reduced mode and would otherwise have no way to know: the map modes look right, so
            // nothing on screen says placement governance is off. Deferred rather than shown here
            // because PostLoadInit runs before the UI is ready to take a letter.
            if (!hadProvinces) pendingCompatibilityNotice = true;
        }

        /// <summary>
        /// Decide the density algorithm for a save that predates the stamp. Same discriminator as the
        /// ownership mode: <b>provinces, not the flag</b>. A pre-0.7.2 world already has generated
        /// provinces and a population the player has been living with, so it keeps the legacy
        /// algorithm. A save with no provinces is one R&amp;T is generating regions for now, for the
        /// first time, so it gets the current algorithm — there is no prior population to preserve.
        /// </summary>
        private void ResolveDensityAlgorithmForLoadedSave()
        {
            if (densityAlgorithmVersionRaw != -1) return;

            bool hadProvinces = provinces != null && provinces.Count > 0;
            densityAlgorithmVersionRaw = hadProvinces ? DensityAlgorithmLegacy : DensityAlgorithmCurrent;

            Log.Message(hadProvinces
                ? "[RegionsAndSocieties] Save predates the density-algorithm stamp but has provinces: keeping the legacy (pre-0.7.2) population algorithm so this world's numbers do not shift."
                : "[RegionsAndSocieties] Save has no province data: regions will be generated with the current population algorithm.");
        }

        /// <summary>Test seam: force the density algorithm back to unresolved so the load-time decision can be exercised.</summary>
        public void ResetDensityAlgorithmForTesting()
        {
            densityAlgorithmVersionRaw = -1;
        }

        /// <summary>Test seam: run the density load-time decision directly, without a save round trip.</summary>
        public void ResolveDensityAlgorithmForTesting()
        {
            ResolveDensityAlgorithmForLoadedSave();
        }

        /// <summary>
        /// Decide the partition algorithm for a save that predates the stamp. Same discriminator as the
        /// density stamp — <b>provinces, not the flag</b>: a world that already has generated provinces
        /// was built by the legacy partition and keeps it (its shapes are scribed and must not shift if
        /// regenerated), while a save with no provinces is one R&amp;T is partitioning now for the first
        /// time and gets the current contain-then-subdivide algorithm.
        /// </summary>
        private void ResolvePartitionAlgorithmForLoadedSave()
        {
            if (partitionAlgorithmVersionRaw != -1) return;

            bool hadProvinces = provinces != null && provinces.Count > 0;
            partitionAlgorithmVersionRaw = hadProvinces ? PartitionAlgorithmLegacy : PartitionAlgorithmCurrent;

            Log.Message(hadProvinces
                ? "[RegionsAndSocieties] Save predates the partition-algorithm stamp but has provinces: keeping the legacy (anchor-Voronoi) region shapes so a regenerate would not repartition this world."
                : "[RegionsAndSocieties] Save has no province data: regions will be built with the current contain-then-subdivide partition.");
        }

        /// <summary>Resolve the worldgen-version stamp for a save that predates it: a save that already has
        /// provinces was rendered by an early build (label it), one with none is being rendered now.</summary>
        private void ResolveWorldGenVersionForLoadedSave()
        {
            if (!string.IsNullOrEmpty(worldGenVersionRaw)) return;
            bool hadProvinces = provinces != null && provinces.Count > 0;
            worldGenVersionRaw = hadProvinces ? "0.2.x or earlier" : WorldGenVersion;
        }

        /// <summary>Resolve the partition-algorithm id for a save that predates it: derive it from the
        /// (already-resolved) binary partition-version stamp — legacy worlds map to anchor-Voronoi, all
        /// others to the contain-then-subdivide default — so a regenerate still reproduces the same map.</summary>
        private void ResolveRegionAlgorithmForLoadedSave()
        {
            if (!string.IsNullOrEmpty(regionAlgorithmId)) return;
            regionAlgorithmId = PartitionAlgorithmVersion == PartitionAlgorithmLegacy
                ? Partition.RegionPartitionerRegistry.LegacyAlgorithmId
                : Partition.RegionPartitionerRegistry.DefaultAlgorithmId;
        }

        /// <summary>Test seam: force the partition algorithm back to unresolved.</summary>
        public void ResetPartitionAlgorithmForTesting()
        {
            partitionAlgorithmVersionRaw = -1;
        }

        /// <summary>Test seam: run the partition load-time decision directly, without a save round trip.</summary>
        public void ResolvePartitionAlgorithmForTesting()
        {
            ResolvePartitionAlgorithmForLoadedSave();
        }

        /// <summary>Set when a save is adopted into compatibility mode; cleared once the player has been told.</summary>
        private bool pendingCompatibilityNotice;

        // WorldComponent has no FinalizeInit, so the notice rides the first tick instead. Ticks only
        // run once the game is actually playing, which is exactly when the letter stack is ready.
        // Coarse cadence for decaying demographic skews (#11): ~2500 ticks (an in-game hour) is ample
        // granularity for a decay measured in years, and keeps the override sweep off the hot path.
        private const int DemographicDecayInterval = 2500;

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();

            // Self-heal: a world whose provinces are empty regenerates here on a tick, so the partition is
            // always present without waiting for an overlay to ask for it. Normal worlds build their
            // provinces at worldgen, so this only fires for a save deliberately blanked to re-test the
            // partition on frozen terrain (the reproducible-test fixture).
            if ((provinces == null || provinces.Count == 0) && Find.WorldGrid != null)
            {
                var _ = Provinces;
            }

            // #53: the whole Societies tick — demographic stress decay, settlement growth, population
            // dynamics — runs only when Societies is on. Off, none of it ticks (Regions-only worlds pay
            // nothing here beyond the province self-heal above).
            if (RegionsAndSocietiesMod.SocietiesEnabled)
            {
                if (Find.TickManager != null && Find.TickManager.TicksGame % DemographicDecayInterval == 0)
                {
                    Demographics.RegionDemographicsStress.Tick(DemographicDecayInterval);
                }

                if (Find.TickManager != null && Find.TickManager.TicksGame % GrowthTickInterval == 0)
                {
                    AdvanceSettlementGrowth(GrowthTickInterval);
                }

                // Population dynamics (#5/#8): every 10 days, AFTER growth, so the write order is grow →
                // accrete → migrate on the shared delta. Governance-off is handled inside RunPasses.
                if (Find.TickManager != null && Find.TickManager.TicksGame % Integration.PopulationDynamics.CadenceTicks == 0)
                {
                    Integration.PopulationDynamics.RunPasses(this, regionPopulationDelta);
                }
            }

            if (!pendingCompatibilityNotice) return;
            pendingCompatibilityNotice = false;

            Find.LetterStack?.ReceiveLetter(
                "Regions and Territories: compatibility mode",
                "This world was created before Regions and Territories was installed, so it has been adopted in " +
                "compatibility mode.\n\n" +
                "Provinces have been generated and territory ownership is drawn on the world map as normal. What is " +
                "switched off is placement: the mod will not decide where settlements and outposts may be built. Your " +
                "world is already full of settlements that were placed with no regard for those rules, and applying " +
                "them now would refuse ground that has been settled since long before the mod arrived. Vanilla and " +
                "your other mods keep control of placement, and more than one settlement may share a province.\n\n" +
                "For the full experience — including faction placement governed by region occupancy, border buffers " +
                "and sequential expansion — start a new colony with the mod already installed. That is what the mod " +
                "is designed around; compatibility mode exists so an existing save is usable, not equivalent.\n\n" +
                "You can review this under 'Strict territorial ownership' in the mod settings.",
                LetterDefOf.NeutralEvent);
        }

        /// <summary>
        /// The modeled population of an NPC settlement (#6), seeding a fresh one at a third of its
        /// target on first read and clamping to its current cap. The player colony is never modeled —
        /// callers read its real free-colonist count instead.
        /// </summary>
        public int GetModeledSettlementPopulation(WorldObject settlement)
        {
            if (settlement == null) return 0;
            if (settlementModeledPop == null) settlementModeledPop = new Dictionary<int, float>();

            int tile = settlement.Tile;
            if (!settlementModeledPop.TryGetValue(tile, out float pop))
            {
                pop = Sizing.SettlementGrowthUtility.SeedPopulation(settlement);
                settlementModeledPop[tile] = pop;
            }

            // Growth capacity is the ⅔-max TARGET, not the tier max. Full births run up to the target;
            // above it births taper, stagnating at 150% of the target — which, since target = ⅔ max, is
            // exactly the tier max. So a healthy settlement crowds toward but never past its tier max.
            int capacity = Sizing.SettlementSizeUtility.TargetPopulationOf(settlement);
            return ClampToCeiling((int)Math.Round(pop, MidpointRounding.AwayFromZero), capacity);
        }

        /// <summary>
        /// Advance every NPC settlement's modeled population one growth step (#6): net rate from the
        /// birthrate factor model, applied as a logistic drift toward the settlement's ⅔-max target over
        /// the elapsed years. Prunes settlements that no longer exist and marks the population cache
        /// dirty so overlays reflect the new sizes. The player colony is skipped — real pawns only.
        /// </summary>
        // Population may crowd above the ⅔-max target up to the birth-stagnation ceiling (150% of the
        // target = the tier max); clamp at the ceiling, so a well-fed settlement can grow past its
        // comfortable size but never past its tier max (#6).
        private static int ClampToCeiling(int v, int capacity)
        {
            if (v < 0) v = 0;
            int ceil = (int)Math.Round(capacity * Sizing.BirthrateRules.BirthStagnationRatio, MidpointRounding.AwayFromZero);
            if (capacity > 0 && v > ceil) v = ceil;
            return v;
        }

        private void AdvanceSettlementGrowth(int intervalTicks)
        {
            if (Find.WorldObjects == null) return;
            if (settlementModeledPop == null) settlementModeledPop = new Dictionary<int, float>();

            float years = intervalTicks / (float)GenDate.TicksPerYear;
            var live = new HashSet<int>();

            foreach (var obj in Find.WorldObjects.AllWorldObjects)
            {
                if (obj == null || !Integration.WorldObjectClassifier.IsSettlement(obj)) continue;
                if (obj.Faction != null && obj.Faction.IsPlayer) continue;   // player = real pawns

                int tile = obj.Tile;
                live.Add(tile);

                if (!settlementModeledPop.TryGetValue(tile, out float pop))
                    pop = Sizing.SettlementGrowthUtility.SeedPopulation(obj);

                // Capacity is the ⅔-max target; births taper above it and stagnate at 150% of it (= tier max).
                int capacity = Sizing.SettlementSizeUtility.TargetPopulationOf(obj);
                var inputs = Sizing.SettlementGrowthUtility.BuildInputs(obj);
                // Scale births and deaths together by the pacing multiplier — the balance point is
                // unchanged, only the speed. Growth runs toward the target and stagnates at the tier max.
                float mult = Integration.WorldObjectIntegrationSettings.growthRateMultiplier;
                float fertility = Sizing.BirthrateRules.Fertility(inputs) * mult;
                float mortality = Sizing.BirthrateRules.Mortality(inputs) * mult;
                float next = Sizing.BirthrateRules.GrowStep(pop, capacity, fertility, mortality, years);
                settlementModeledPop[tile] = next;

                // Publish the change at the integer level so a consumer sees growth events (no-op with
                // no consumer). Rounded+clamped the same way GetModeledSettlementPopulation reports it.
                int before = ClampToCeiling((int)Math.Round(pop, MidpointRounding.AwayFromZero), capacity);
                int after = ClampToCeiling((int)Math.Round(next, MidpointRounding.AwayFromZero), capacity);
                Sizing.SettlementGrowthHooks.Report(obj, before, after);
            }

            if (settlementModeledPop.Count > live.Count)
            {
                var stale = settlementModeledPop.Keys.Where(k => !live.Contains(k)).ToList();
                foreach (int k in stale) settlementModeledPop.Remove(k);
            }

            PopulationDensityUtility.MarkCacheDirty();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref strictTerritorialOwnershipRaw, "strictTerritorialOwnership", -1);

            // Stamp worlds generated under 0.7.2+ as they are first saved. If the algorithm is still
            // unresolved at save time this is a live world running current code that never went
            // through the load-time resolver (i.e. a new game), so it is current by construction. A
            // loaded pre-0.7.2 save has already been resolved to legacy before any save happens.
            if (Scribe.mode == LoadSaveMode.Saving && densityAlgorithmVersionRaw == -1)
            {
                densityAlgorithmVersionRaw = DensityAlgorithmCurrent;
            }
            Scribe_Values.Look(ref densityAlgorithmVersionRaw, "densityAlgorithmVersion", -1);

            // Same stamp-on-first-save rule as the density version: an unresolved algorithm at save time
            // is a live new world running current code (it never hit the load-time resolver), so it is
            // current by construction; a loaded pre-stamp save was resolved to legacy before any save.
            if (Scribe.mode == LoadSaveMode.Saving && partitionAlgorithmVersionRaw == -1)
            {
                partitionAlgorithmVersionRaw = PartitionAlgorithmCurrent;
            }
            Scribe_Values.Look(ref partitionAlgorithmVersionRaw, "partitionAlgorithmVersion", -1);

            // Worldgen/rendering version stamp — persisted; resolved for pre-stamp saves in PostLoadInit.
            Scribe_Values.Look(ref worldGenVersionRaw, "worldGenVersion");
            // The partition algorithm id this world was generated with — persisted; a save predating the
            // id derives it from the binary partition-version stamp in PostLoadInit.
            Scribe_Values.Look(ref regionAlgorithmId, "regionAlgorithmId");

            // The partition INPUTS this world was cut with (#0.4.0): the size band and the small-region
            // option, so a regenerate reproduces the same map even after the mod settings change. -1 for a
            // save predating the stamp; such a world falls back to the live setting via the Effective*
            // accessors, exactly as it did before the stamp existed.
            Scribe_Values.Look(ref worldTargetRegionSize, "worldTargetRegionSize", -1);
            Scribe_Values.Look(ref worldEnableSmallRegionsRaw, "worldEnableSmallRegions", -1);

            // #18 holding-seeding inputs (maturity + enable, stamped for regenerate reproducibility) and the
            // mid-game region-lock flag. -1 = unset; the Effective* accessors fall back to the live settings.
            Scribe_Values.Look(ref worldSeedingMaturityRaw, "worldSeedingMaturity", -1);
            Scribe_Values.Look(ref worldHoldingSeedingRaw, "worldHoldingSeeding", -1);
            Scribe_Values.Look(ref regionLockRaw, "regionLock", -1);

            Scribe_Collections.Look(ref provinces, "provinces", LookMode.Deep);
            if (provinces == null)
            {
                provinces = new List<GeographicProvince>();
            }

            // Dynamic population offsets accumulated by the #5/#8 passes — scribed so a save keeps how its
            // map has drifted from the derived baseline.
            Scribe_Collections.Look(ref regionPopulationDelta, "regionPopulationDelta", LookMode.Value, LookMode.Value);
            if (regionPopulationDelta == null)
            {
                regionPopulationDelta = new Dictionary<int, float>();
            }

            // 0.8: sparse demographic stress overrides. The demographic baseline is deterministic
            // (regenerated from the world seed), so only deliberate changes are stored here.
            Demographics.RegionDemographicsStress.ExposeData();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                ResolveStrictOwnershipForLoadedSave();
                ResolveDensityAlgorithmForLoadedSave();
                ResolvePartitionAlgorithmForLoadedSave();
                ResolveWorldGenVersionForLoadedSave();
                ResolveRegionAlgorithmForLoadedSave();

                // Population is cached statically and survives across loads within one process; drop
                // it so the next read rebuilds under the algorithm just resolved for this world.
                PopulationDensityUtility.MarkCacheDirty();
            }

            Scribe_Collections.Look(ref settlementPlacementOrder, "settlementPlacementOrder", LookMode.Value, LookMode.Value);
            if (settlementPlacementOrder == null)
            {
                settlementPlacementOrder = new Dictionary<int, int>();
            }

            // Modeled NPC settlement populations (#6): the size a settlement has grown to, persisted so
            // growth continues across saves rather than reseeding.
            Scribe_Collections.Look(ref settlementModeledPop, "settlementModeledPop", LookMode.Value, LookMode.Value);
            if (settlementModeledPop == null)
            {
                settlementModeledPop = new Dictionary<int, float>();
            }

            // One-time repair flag for the pre-0.3.0 "every faction generated hidden" bug (#32). Old saves
            // lack this key, so it loads false and the repair runs once on FinalizeInit; new/repaired worlds
            // scribe true and skip it forever.
            Scribe_Values.Look(ref factionHiddenMigrationDone, "factionHiddenMigrationDone", false);

            List<int> tempList = null;
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (tileToProvinceId != null)
                {
                    tempList = tileToProvinceId.ToList();
                }
            }
            Scribe_Collections.Look(ref tempList, "tileToProvinceId", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (tempList != null && Find.WorldGrid != null)
                {
                    tileToProvinceId = tempList.ToArray();
                }
                else
                {
                    InitializeData();
                }

                // Repair the reverse index from the authoritative province.tiles lists. Older saves
                // scribed a tile->province index that could fall out of sync with the provinces, or
                // never scribed one at all, leaving GetProvinceId returning -1 for tiles that plainly
                // belong to a province. Ownership buckets world objects by GetProvinceId, so that
                // silently zeroed all ownership on such saves (#67). Rebuilding here is a one-off
                // repair that then persists on the next save; it is idempotent on a healthy world.
                if (provinces != null && provinces.Count > 0 && Find.WorldGrid != null)
                {
                    RebuildTileIndexFromProvinces();
                    MarkOwnersDirty();
                }
            }
        }

        /// <summary>Set once the pre-0.3.0 hidden-faction repair (#32) has been attempted for this world.</summary>
        private bool factionHiddenMigrationDone;

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            MigrateHiddenFactions();

            // Precompute every land region's demographic aggregate now, on the loading screen, so opening
            // any demographic overlay (age, sex, wealth, education, …) later is an instant cache hit rather
            // than a cold O(tiles × sources) aggregation that freezes the frame it is opened on.
            Demographics.RegionDemographicsUtility.WarmAllRegions(this);
        }

        /// <summary>
        /// One-time repair for worlds generated before 0.3.0, where a bug (#32) created EVERY ordinary
        /// faction hidden — absent from the Factions tab, with no goodwill and no leader. Runs once per
        /// world (scribed flag) and is signature-gated so it never exposes factions another mod hides on
        /// purpose: it fires only when the bug's fingerprint is present — at least three ordinary
        /// (non-player, non-def-hidden) factions exist and at least half of them are instance-hidden.
        /// It only un-hides instances (and regenerates a missing leader); it never deletes a faction,
        /// since settlements, relations and quests may reference it.
        /// </summary>
        private void MigrateHiddenFactions()
        {
            if (factionHiddenMigrationDone) return;
            factionHiddenMigrationDone = true;   // attempt once, whatever the outcome

            var factionManager = Find.FactionManager;
            if (factionManager == null) return;

            var ordinary = factionManager.AllFactions
                .Where(f => f != null && !f.IsPlayer && f.def != null && !f.def.isPlayer && !f.def.hidden)
                .ToList();
            if (ordinary.Count < 3) return;

            var hiddenOnes = ordinary.Where(f => f.Hidden).ToList();
            // Signature: >= 3 ordinary factions hidden AND >= 50% of the ordinary factions hidden.
            if (hiddenOnes.Count < 3 || hiddenOnes.Count < ordinary.Count * 0.5f) return;

            int repaired = 0;
            var needLeaders = new List<Faction>();
            foreach (var faction in hiddenOnes)
            {
                faction.hidden = faction.def.hidden;   // = false for an ordinary def
                if (!faction.Hidden && faction.leader == null) needLeaders.Add(faction);
                repaired++;
            }

            // Un-hiding is the essential repair and is done above. Leader generation, however, walks pawn
            // and ideo state that is NOT fully wired up this early in a load (it NREs for some factions in
            // FinalizeInit), so defer it until the load long-event completes and the world is live.
            if (needLeaders.Count > 0)
            {
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    foreach (var f in needLeaders)
                    {
                        if (f == null || f.Hidden || f.leader != null) continue;
                        try { f.TryGenerateNewLeader(); }
                        catch (System.Exception e)
                        {
                            Log.Warning($"[RegionsAndSocieties] #32 migration: deferred leader regen for '{f.Name}' threw: {e.Message}");
                        }
                    }
                });
            }
            Log.Message($"[RegionsAndSocieties] #32 migration: restored {repaired} faction(s) that a pre-0.3.0 bug created hidden (visibility/goodwill; leaders generated once the load completes).");
        }

        /// <summary>
        /// Rebuild the tile-&gt;province reverse index from the provinces' own tile lists — the
        /// authoritative partition (deep-scribed). Idempotent on a healthy world; a repair on a save
        /// whose scribed index was stale or absent (#67).
        /// </summary>
        private void RebuildTileIndexFromProvinces()
        {
            if (Find.WorldGrid == null || provinces == null) return;

            int n = Find.WorldGrid.TilesCount;
            if (tileToProvinceId == null || tileToProvinceId.Length != n)
            {
                tileToProvinceId = new int[n];
            }
            for (int i = 0; i < n; i++) tileToProvinceId[i] = -1;

            int mapped = 0;
            foreach (var p in provinces)
            {
                if (p?.tiles == null) continue;
                foreach (int t in p.tiles)
                {
                    if (t >= 0 && t < n)
                    {
                        tileToProvinceId[t] = p.id;
                        mapped++;
                    }
                }
            }

            Log.Message($"[RegionsAndSocieties] Rebuilt tile->province index from {provinces.Count} provinces ({mapped} tiles mapped).");
        }

        /// <summary>True for a tile that is impassable rock (never traversable on the world map): the
        /// Impassable hilliness, or an impassable / sea-ice biome. These become non-owned MountainRange
        /// provinces, not territory. Passable Mountainous/LargeHills are NOT impassable and stay claimable.</summary>
        private static bool IsImpassableTile(Tile td)
        {
            if (td == null) return false;
            if (td.hilliness == Hilliness.Impassable) return true;
            BiomeDef b = td.PrimaryBiome;
            return b != null && (b.impassable || b.defName == "SeaIce");
        }

        private BiomeDef GetPrimaryBiome(List<int> chunk)
        {
            if (chunk == null || chunk.Count == 0) return null;
            return chunk
                .Select(t => Find.WorldGrid[t].PrimaryBiome)
                .Where(b => b != null)
                .GroupBy(b => b)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault();
        }

        public void GenerateProvinces()
        {
            Log.Message("[RegionsAndSocieties] Generating Geographic Domains (Boundary-First Priority)...");

            // Stamp the worldgen version the first time a world is rendered. Guarded so a legacy REGEN
            // (which loaded an already-stamped save) never relabels an old world as a new-build rendering.
            if (string.IsNullOrEmpty(worldGenVersionRaw)) worldGenVersionRaw = WorldGenVersion;

            // A world generating provinces with the flag still unresolved is a brand new world:
            // a loaded save resolves it in PostLoadInit, which runs before anything can reach the
            // lazy Provinces getter. New worlds take the configured default, which is strict.
            if (strictTerritorialOwnershipRaw == -1)
            {
                // Static on the settings class, like every other field there.
                bool strict = FactionPlacementSettings.strictTerritorialOwnershipDefault;
                strictTerritorialOwnershipRaw = strict ? 1 : 0;
                Log.Message($"[RegionsAndSocieties] New world: territorial ownership set to {(strict ? "strict" : "compatibility")}.");
            }

            if (Find.WorldGrid == null) return;
            int totalTiles = Find.WorldGrid.TilesCount;
            tileToProvinceId = new int[totalTiles];
            for (int i = 0; i < totalTiles; i++)
            {
                tileToProvinceId[i] = -1;
            }

            provinces.Clear();

            // The derived adjacency map describes the province layout we are about to replace, and
            // it is keyed on the world instance rather than on the provinces — so regenerating
            // inside one world is the one case the key cannot catch.
            ProvinceAdjacency.ClearCache();

            int provinceIdCounter = 0;

            // Rivers no longer form their own provinces (#20). Under the border-first, river-basin
            // model a river is the CENTRE of a land province, not a boundary — the old river-segment
            // provinces and their Phase 4.5 absorption (which produced the 1-tile river tails) are
            // gone. River tiles instead seed the basin markers inside BorderPartitioner.

            // Stamp the partition inputs on first generation; a regenerate of an already-stamped world
            // reuses them so it reproduces faithfully (mirrors regionAlgorithmId, resolved just below).
            if (worldTargetRegionSize <= 0) worldTargetRegionSize = FactionPlacementSettings.targetRegionSize;
            if (worldEnableSmallRegionsRaw < 0) worldEnableSmallRegionsRaw = FactionPlacementSettings.enableSmallRegions ? 1 : 0;

            int baseMax = EffectiveTargetRegionSize;
            int baseMin = EffectiveMergeFloor;

            int minWithFeatures = baseMin - 5;
            int minNoFeatures = baseMin + 5;

            // Phase 2.5: Water. Flood-fill every contiguous WATER body — ocean, sea ice, lakes — into
            // its own Ocean-type province and claim the tiles, so the land partition skips them (water
            // is the hard wall the border-first fill never spans) and the open sea is owned rather than
            // left as a black hole. NOTE: RimWorld's Ocean biome is itself flagged impassable, so this
            // must NOT filter on biome.impassable (that skipped the entire ocean and left it unclaimed,
            // #20) — WaterCovered alone selects water. Impassable LAND (mountain peaks) is a different
            // case and is left for AbsorbEnclosedGaps below. Enclosed inland lakes claimed here are
            // shared across their shore regions by SplitInlandLakes (#48); the big ocean bodies stay as
            // provinces.
            {
                var waterNbrs = new List<RimWorld.Planet.PlanetTile>();
                for (int i = 0; i < totalTiles; i++)
                {
                    if (tileToProvinceId[i] != -1) continue;
                    Tile td = Find.WorldGrid[i];
                    if (!td.WaterCovered) continue;

                    var body = new List<int>();
                    var bq = new Queue<int>();
                    bq.Enqueue(i);
                    tileToProvinceId[i] = provinceIdCounter;
                    while (bq.Count > 0)
                    {
                        int cur = bq.Dequeue();
                        body.Add(cur);
                        waterNbrs.Clear();
                        Find.WorldGrid.GetTileNeighbors(cur, waterNbrs);
                        foreach (var n in waterNbrs)
                        {
                            int nid = n.tileId;
                            if (tileToProvinceId[nid] != -1) continue;
                            if (!Find.WorldGrid[nid].WaterCovered) continue;
                            tileToProvinceId[nid] = provinceIdCounter;
                            bq.Enqueue(nid);
                        }
                    }

                    var waterDom = new GeographicProvince(provinceIdCounter);
                    waterDom.tiles = body;
                    waterDom.provinceType = ProvinceType.Ocean;
                    waterDom.primaryBiome = GetPrimaryBiome(body);
                    waterDom.name = GenerateProvinceName(provinceIdCounter, waterDom.primaryBiome, waterDom.provinceType);
                    provinces.Add(waterDom);
                    provinceIdCounter++;
                }
            }

            // Phase 2.6: Impassable mountains. Flood every contiguous body of unclaimed IMPASSABLE land
            // (Hilliness.Impassable, or an impassable / sea-ice biome that is not water) into its own
            // MountainRange province and claim the tiles. Like the ocean pass, this makes the land
            // partition treat impassable rock as a hard wall AND keeps it out of any faction's territory —
            // impassable peaks are terrain, not land anyone holds, and are excluded from ownership, the
            // territory overlay, population and economy alongside Ocean. Passable mountains and hills are
            // NOT claimed here; they stay claimable interior for the partition, so hills no longer fragment
            // the land into slivers.
            {
                var impNbrs = new List<RimWorld.Planet.PlanetTile>();
                for (int i = 0; i < totalTiles; i++)
                {
                    if (tileToProvinceId[i] != -1) continue;
                    Tile td = Find.WorldGrid[i];
                    if (td.WaterCovered || !IsImpassableTile(td)) continue;

                    var body = new List<int>();
                    var bq = new Queue<int>();
                    bq.Enqueue(i);
                    tileToProvinceId[i] = provinceIdCounter;
                    while (bq.Count > 0)
                    {
                        int cur = bq.Dequeue();
                        body.Add(cur);
                        impNbrs.Clear();
                        Find.WorldGrid.GetTileNeighbors(cur, impNbrs);
                        foreach (var n in impNbrs)
                        {
                            int nid = n.tileId;
                            if (tileToProvinceId[nid] != -1) continue;
                            Tile nt = Find.WorldGrid[nid];
                            if (nt.WaterCovered || !IsImpassableTile(nt)) continue;
                            tileToProvinceId[nid] = provinceIdCounter;
                            bq.Enqueue(nid);
                        }
                    }

                    var mtn = new GeographicProvince(provinceIdCounter);
                    mtn.tiles = body;
                    mtn.provinceType = ProvinceType.MountainRange;
                    mtn.primaryBiome = GetPrimaryBiome(body);
                    mtn.name = GenerateProvinceName(provinceIdCounter, mtn.primaryBiome, mtn.provinceType);
                    provinces.Add(mtn);
                    provinceIdCounter++;
                }
            }

            // Phase 4: border-first land partition (#20). The water/ocean provinces claimed above are
            // the hard walls; BorderPartitioner floods the remaining land into cells bounded by
            // natural feature transitions (ridges, biome edges, forest bands, coasts) and splits any
            // oversized cell into river basins by a marker-controlled watershed — so borders sit on
            // features, basins centre on rivers, and region size varies with the terrain. This
            // replaces the grow-first frontier and its Phase 4.5 river absorption in one pass.
            // New worlds (and regens of new-partition worlds) use the contain-then-subdivide partition:
            // flood each biome/barrier-bounded section into one container, then cut it into biome-weighted
            // squares. A legacy world keeps the anchor-Voronoi PartitionLand so a regenerate never reshapes
            // an existing save.
            // Resolve the partition algorithm from the registry (pluggable — a mod can add its own). A
            // world already stamped with an algorithm (a regenerate of an existing world) reproduces with
            // THAT algorithm; a brand-new world takes the one selected in mod settings. Either way the
            // resolved id is stamped now, so a later regenerate is faithful and the save records it.
            string requestedAlgo = !string.IsNullOrEmpty(regionAlgorithmId)
                ? regionAlgorithmId
                : FactionPlacementSettings.partitionAlgorithmId;
            var partitioner = Partition.RegionPartitionerRegistry.Get(requestedAlgo)
                ?? Partition.RegionPartitionerRegistry.Default;
            regionAlgorithmId = partitioner != null ? partitioner.AlgorithmId : requestedAlgo;
            // Keep the legacy binary stamp consistent with the chosen algorithm.
            partitionAlgorithmVersionRaw = regionAlgorithmId == Partition.RegionPartitionerRegistry.LegacyAlgorithmId
                ? PartitionAlgorithmLegacy : PartitionAlgorithmCurrent;

            var swPartition = System.Diagnostics.Stopwatch.StartNew();
            var landGroups = partitioner != null
                ? partitioner.Partition(tileToProvinceId, baseMin, baseMax)
                : Partition.BorderPartitioner.PartitionContainSubdivide(tileToProvinceId, baseMin, baseMax);
            swPartition.Stop();
            Log.Message($"[RegionsAndSocieties] Land partition: '{regionAlgorithmId}' produced {landGroups.Count} land groups in {swPartition.ElapsedMilliseconds} ms.");
            foreach (var group in landGroups)
            {
                if (group.Count == 0) continue;

                GeographicProvince domain = new GeographicProvince(provinceIdCounter);
                domain.tiles = group.ToList();
                domain.provinceType = ProvinceType.Land;
                domain.primaryBiome = GetPrimaryBiome(group);
                domain.name = GenerateProvinceName(provinceIdCounter, domain.primaryBiome, domain.provinceType);

                foreach (int tileId in group)
                {
                    tileToProvinceId[tileId] = provinceIdCounter;
                }
                provinces.Add(domain);
                provinceIdCounter++;
            }

            // Deduplicate tiles to ensure thread-safety for Map Mode Framework rendering
            HashSet<int> assignedTiles = new HashSet<int>();
            foreach (var p in provinces)
            {
                p.tiles = p.tiles.Distinct().ToList();
                List<int> uniqueTiles = new List<int>();
                foreach (int tileId in p.tiles)
                {
                    if (!assignedTiles.Contains(tileId))
                    {
                        assignedTiles.Add(tileId);
                        uniqueTiles.Add(tileId);
                        tileToProvinceId[tileId] = p.id;
                    }
                }
                p.tiles = uniqueTiles;
            }

            // Phase 5: Consolidation & Merging (Pass 2)
            Log.Message("[RegionsAndSocieties] Starting MergeTinyDomains...");
            MergeTinyDomains(minWithFeatures, minNoFeatures);
            Log.Message("[RegionsAndSocieties] Finished MergeTinyDomains.");

            // Phase 5a: fold a land region ENTIRELY enclosed by one other land region into that region —
            // an enclave/inclusion (e.g. a small biome patch inside a big region) reads as part of its
            // encloser, never its own territory. Judged over land-region neighbours only, so it runs
            // regardless of biome and after the biome-aware merge leaves such slivers behind.
            AbsorbEnclosedRegions();

            // Phase 5a2: a small passable speck sealed entirely by impassable rock (no land, no water)
            // reads as part of the massif, not a 1-tile territory — fold it into the surrounding
            // MountainRange. Islands (water neighbours) and genuine enclosed valleys (larger) are left.
            AbsorbMountainSealedSpecks(MountainSpeckMaxTiles);

            // Phase 5b2: fold impassable-mountain (and other unclaimed, non-water) pockets that are
            // fully enclosed by a single region INTO that region, so they read as owned terrain rather
            // than holes punched in the map (#3).
            AbsorbEnclosedGaps();

            // Phase 5b3: split ribbon-shaped provinces (#20). A cell sized just over the guide rounds
            // to one basin and can stay a long snaking valley; break any province whose principal-axis
            // ratio is too high into compact halves across its short axis. Runs AFTER the merge so the
            // halves are not immediately re-absorbed. The viability floor is deliberately below the
            // merge minimum so a moderately-sized ribbon still splits — a pair of small blobs reads
            // far better than one long snake.
            SplitElongatedProvinces(EffectiveMergeFloor * 2 / 3);

            // Phase 5c: erode pendant tails and single-tile protrusions (#20). Border-first cells
            // follow natural features, but the watershed clips and feature-edge zigzags still leave
            // 1-tile-wide appendages; a light majority-vote relaxation folds a tile wrapped more by a
            // neighbour than by its own province back into that neighbour, straightening the ragged
            // edges without touching feature borders (water/impassable neighbours never vote).
            SmoothRegionBoundaries(8);

            // Phase 5c.05: small-island handling (#49). Absorb lone small islands (<10 tiles, water-
            // surrounded, land within 4 hops) into the nearest mainland, but group island clusters into
            // chains first: a chain totalling >=30 tiles becomes its own archipelago region instead of
            // being scattered onto whatever coast each speck is nearest. Runs before the contiguity split,
            // which then exempts water-surrounded island components from being spun back off (#49).
            ResolveSmallIslands();

            // Phase 5c.1: enforce contiguity. A merge/absorb pass can leave a region as two DETACHED land
            // masses — a component sharing no hex edge with the rest of its region (separated by water or
            // another region). A region is by definition one connected landmass, so split any region with
            // more than one hex-connected component: the largest keeps the id, the rest become their own
            // regions (an island is legitimately its own region). Runs before the final AbsorbStrayFragments
            // so a sub-minimum spun-off piece that turns out to sit against a same-biome neighbour still
            // gets folded in.
            SplitDisconnectedRegions();

            // Phase 5d: final consolidation. Splitting and straightening run after the merge, so they can
            // leave a small same-biome fragment newly adjacent to the big region it belongs to (e.g. a
            // desert sliver against the main desert) that the earlier merge never got to see. Fold any
            // sub-minimum land region into its dominant same-biome passable neighbour.
            AbsorbStrayFragments();

            // Phase 5e: resolve tiny land regions (#51). A 1-6 tile region carries no region benefits and
            // nothing the demographic model can model. If it touches a land mass at all it JOINS it (folds
            // into the largest land neighbour) — a sliver next to real land should be part of that land, not
            // a standalone region or an unassigned hole, and a settlement on it goes along. Only a genuinely
            // isolated speck (no land neighbour — a mid-water island #49 could not reach) is dropped to
            // unassigned, or kept as a benefits-suppressed region when the "enable small regions" option is
            // on, or kept if it anchors a settlement that would otherwise be orphaned.
            DropTinyRegions();

            // Phase 5f: share inland lakes across their shore regions (#48). Runs LAST, over the FINAL land
            // regions — after islands have been absorbed (#49) and tiny land regions folded/dropped (#51) —
            // so a lake's water is handed only to land that is really there. Doing it earlier inflated a
            // 1-tile island into a many-tile region by giving it a wedge of the lake before #49/#51 could
            // see it was a speck, which then hid it from the island/tiny rules. Each below-cap enclosed
            // body's tiles go to the nearest shore region so the seam runs ACROSS the water, not around it.
            SplitInlandLakes();

            // Naming Phase: Contextual Name Resolution
            Log.Message("[RegionsAndSocieties] Running contextual province naming...");
            ResolveContextualNames();

            // Aggregate the now-fixed topology once, so every later draw/ownership pass reads
            // perimeters and border shares instead of rescanning tiles (#48).
            BuildProvinceTopology();

            Log.Message($"[RegionsAndSocieties] Generated {provinces.Count} Geographic Domains.");
        }

        /// <summary>
        /// Fold every sub-minimum land region into its dominant same-biome, passable neighbour. Runs LAST,
        /// after split + straighten, to catch a fragment whose same-biome connection to the region it
        /// belongs to only formed once those passes moved tiles — the ordering gap that left a stray sliver
        /// (601) next to the desert it is part of (235). Same-biome only, so it never mixes biomes; iterated
        /// because folding one fragment can bring another below the threshold's dominant share.
        /// </summary>
        private void AbsorbStrayFragments()
        {
            if (provinces == null || tileToProvinceId == null || Find.WorldGrid == null) return;
            int minR = EffectiveMergeFloor;
            var neighbors = new List<RimWorld.Planet.PlanetTile>();
            int folded = 0;
            bool changed = true; int guard = 0;
            while (changed && guard++ < 8)
            {
                changed = false;
                var byId = provinces.ToDictionary(p => p.id, p => p);
                var toRemove = new HashSet<GeographicProvince>();
                foreach (var p in provinces)
                {
                    if (p.provinceType != ProvinceType.Land || p.tiles == null || p.tiles.Count == 0 || toRemove.Contains(p)) continue;
                    if (p.tiles.Count >= minR) continue;
                    BiomeDef pb = p.primaryBiome;

                    var sameBiomeEdges = new Dictionary<int, int>();
                    foreach (int t in p.tiles)
                    {
                        if (IsBarrierTile(t) || BiomeOfTile(t) != pb) continue;
                        neighbors.Clear();
                        Find.WorldGrid.GetTileNeighbors(t, neighbors);
                        foreach (var n in neighbors)
                        {
                            if (IsBarrierTile(n.tileId) || BiomeOfTile(n.tileId) != pb) continue;
                            int np = GetProvinceId(n.tileId);
                            if (np == p.id || np == -1) continue;
                            if (!byId.TryGetValue(np, out var nprov) || nprov.provinceType != ProvinceType.Land
                                || nprov.primaryBiome != pb || toRemove.Contains(nprov)) continue;
                            int c; sameBiomeEdges.TryGetValue(np, out c); sameBiomeEdges[np] = c + 1;
                        }
                    }
                    if (sameBiomeEdges.Count == 0) continue;

                    int bestId = -1, bestC = 0;
                    foreach (var kv in sameBiomeEdges) if (kv.Value > bestC || (kv.Value == bestC && kv.Key < bestId)) { bestC = kv.Value; bestId = kv.Key; }
                    if (bestId >= 0 && byId.TryGetValue(bestId, out var host))
                    {
                        foreach (int tileId in p.tiles) { host.tiles.Add(tileId); tileToProvinceId[tileId] = host.id; }
                        toRemove.Add(p); changed = true; folded++;
                    }
                }
                if (toRemove.Count > 0) provinces.RemoveAll(p => toRemove.Contains(p));
            }
            Log.Message($"[RegionsAndSocieties] AbsorbStrayFragments: folded {folded} stray same-biome fragment(s).");
        }

        /// <summary>
        /// Terminal pass (#51): remove every land region no merge could place — 1-6 tiles, too small to
        /// carry region benefits or feed the demographic model. Runs LAST (after merge/split/absorb), so it
        /// only touches specks that survived every consolidation rule. Each dropped speck's tiles are
        /// unassigned (tileToProvinceId = -1, the impassable-hole state downstream already tolerates). A
        /// speck that anchors a settlement/outpost is never orphaned: it folds into its largest land
        /// neighbour of any biome (barrier check relaxed), or is kept when it has no land neighbour at all.
        /// The decision itself is the pure <see cref="Placement.TinyRegionRules"/>.
        /// </summary>
        private void DropTinyRegions()
        {
            if (provinces == null || tileToProvinceId == null || Find.WorldGrid == null) return;

            // Tiles carrying a permanent holding (settlement/outpost/military) — never orphaned by a drop.
            var holdingTiles = new HashSet<int>();
            if (Find.WorldObjects != null)
            {
                foreach (var obj in Find.WorldObjects.AllWorldObjects)
                {
                    if (obj != null && Integration.WorldObjectClassifier.IsPermanentHolding(obj))
                        holdingTiles.Add(obj.Tile.tileId);
                }
            }

            // #51 option: when on, tiny regions are KEPT as benefits-suppressed regions instead of dropped.
            bool keepSmall = EffectiveEnableSmallRegions;

            var byId = provinces.ToDictionary(p => p.id, p => p);
            var neighbors = new List<RimWorld.Planet.PlanetTile>();
            var toRemove = new HashSet<GeographicProvince>();
            int dropped = 0, droppedTiles = 0, folded = 0, kept = 0;

            foreach (var p in provinces)
            {
                if (p.provinceType != ProvinceType.Land || p.tiles == null || p.tiles.Count == 0) continue;
                if (p.tiles.Count > Placement.TinyRegionRules.TinyRegionMaxTiles) continue;

                bool hasHolding = false;
                foreach (int t in p.tiles) { if (holdingTiles.Contains(t)) { hasHolding = true; break; } }

                // Largest land neighbour of ANY biome; barrier check relaxed, so a crag reachable only
                // across a rock/water seam still counts as a fold target for a settlement speck.
                GeographicProvince bestLand = null; int bestSize = -1;
                var seen = new HashSet<int>();
                foreach (int t in p.tiles)
                {
                    neighbors.Clear();
                    Find.WorldGrid.GetTileNeighbors(t, neighbors);
                    foreach (var n in neighbors)
                    {
                        int np = GetProvinceId(n.tileId);
                        if (np == p.id || np == -1 || !seen.Add(np)) continue;
                        if (!byId.TryGetValue(np, out var nprov) || nprov.provinceType != ProvinceType.Land || toRemove.Contains(nprov)) continue;
                        if (nprov.tiles.Count > bestSize) { bestSize = nprov.tiles.Count; bestLand = nprov; }
                    }
                }

                var action = Placement.TinyRegionRules.Resolve(p.tiles.Count, hasHolding, bestLand != null, keepSmall);
                if (action == Placement.TinyRegionAction.Fold && bestLand != null)
                {
                    foreach (int tileId in p.tiles) { bestLand.tiles.Add(tileId); tileToProvinceId[tileId] = bestLand.id; }
                    toRemove.Add(p); folded++;
                }
                else if (action == Placement.TinyRegionAction.Drop)
                {
                    foreach (int tileId in p.tiles) tileToProvinceId[tileId] = -1;
                    toRemove.Add(p); dropped++; droppedTiles += p.tiles.Count;
                }
                else
                {
                    // Kept: a tiny region survives (option on, or a settlement speck with no fold target).
                    // It is real and settle-able but too small to sustain a society — no regional benefits.
                    p.benefitsSuppressed = true; kept++;
                }
            }

            if (toRemove.Count > 0) provinces.RemoveAll(p => toRemove.Contains(p));
            Log.Message($"[RegionsAndSocieties] DropTinyRegions (#51): dropped {dropped} region(s) ({droppedTiles} tile(s) unassigned), "
                + $"folded {folded} settlement speck(s), kept {kept} benefits-suppressed small region(s) (enableSmallRegions={keepSmall}).");
        }

        /// <summary>Largest passable speck (in tiles) folded into a surrounding impassable massif. Above
        /// this a fully mountain-sealed pocket is treated as a genuine enclosed valley and kept.</summary>
        private const int MountainSpeckMaxTiles = 15;

        /// <summary>
        /// Fold every small passable land region whose neighbours are ENTIRELY impassable MountainRange
        /// (no other land, no water, no unclaimed tile) into the surrounding mountain province. Such a
        /// speck is a habitable dot sealed inside a massif; it reads as terrain, not a territory. Islands
        /// (water neighbours) and larger enclosed valleys are untouched.
        /// </summary>
        private void AbsorbMountainSealedSpecks(int maxTiles)
        {
            if (provinces == null || tileToProvinceId == null || Find.WorldGrid == null) return;
            var byId = provinces.ToDictionary(p => p.id, p => p);
            var neighbors = new List<RimWorld.Planet.PlanetTile>();
            var toRemove = new HashSet<GeographicProvince>();
            foreach (var p in provinces)
            {
                if (p.provinceType != ProvinceType.Land || p.tiles == null || p.tiles.Count == 0) continue;
                if (p.tiles.Count > maxTiles) continue;

                bool sealedByMtn = true; int mtnProv = -1;
                foreach (int tile in p.tiles)
                {
                    neighbors.Clear();
                    Find.WorldGrid.GetTileNeighbors(tile, neighbors);
                    foreach (var n in neighbors)
                    {
                        int pid = GetProvinceId(n.tileId);
                        if (pid == p.id) continue;
                        if (pid == -1) { sealedByMtn = false; break; }
                        if (!byId.TryGetValue(pid, out var np) || np.provinceType != ProvinceType.MountainRange
                            || toRemove.Contains(np)) { sealedByMtn = false; break; }
                        if (mtnProv == -1) mtnProv = pid;   // fold into the first surrounding massif
                    }
                    if (!sealedByMtn) break;
                }

                if (sealedByMtn && mtnProv >= 0 && byId.TryGetValue(mtnProv, out var mprov))
                {
                    foreach (int tileId in p.tiles) { mprov.tiles.Add(tileId); tileToProvinceId[tileId] = mprov.id; }
                    toRemove.Add(p);
                }
            }
            if (toRemove.Count > 0) provinces.RemoveAll(p => toRemove.Contains(p));
            Log.Message($"[RegionsAndSocieties] AbsorbMountainSealedSpecks: folded {toRemove.Count} speck(s) into surrounding mountains.");
        }
        /// <summary>
        /// Share every enclosed inland lake across the land regions on its shores (#48). Phase 2.5 floods
        /// each water body into its own Ocean province; a lake ringed by land then reads as an unowned
        /// wedge with the region border drawn around it. A real inland lake is shared by the polities on
        /// its shores, so its tiles are handed to those regions in proportion to their shoreline (lake
        /// dominance = number of bordering tiles) and the border runs across the water instead. An inland
        /// body larger than <see cref="Partition.LakeSplitRules.LakeMaxTiles"/> stays Ocean — an inland sea
        /// is a genuine barrier; a body touching another water province is a sea inlet, not a lake. The
        /// proportional cut is the pure <see cref="Partition.LakeSplitRules"/>; this pass only gathers the
        /// lake's adjacency and shore labels and applies the result. Replaces the old whole-absorb rule — a
        /// pond wholly inside one region is just a lake with a single shore region and splits the same way.
        /// </summary>
        private void SplitInlandLakes()
        {
            if (provinces == null || tileToProvinceId == null || Find.WorldGrid == null) return;

            var byId = provinces.ToDictionary(p => p.id, p => p);
            var neighbors = new List<RimWorld.Planet.PlanetTile>();
            var toRemove = new List<GeographicProvince>();
            int splitLakes = 0, movedTiles = 0;

            foreach (var lake in provinces.ToList())
            {
                if (lake.provinceType != ProvinceType.Ocean || lake.tiles == null) continue;
                if (lake.tiles.Count == 0 || lake.tiles.Count > Partition.LakeSplitRules.LakeMaxTiles) continue;

                var lakeSet = new HashSet<int>(lake.tiles);
                var lakeAdj = new Dictionary<int, List<int>>();
                var shoreLabels = new Dictionary<int, List<int>>();
                bool touchesOtherWater = false;

                foreach (int t in lake.tiles)
                {
                    neighbors.Clear();
                    Find.WorldGrid.GetTileNeighbors(t, neighbors);
                    List<int> adj = null;
                    List<int> labels = null;
                    foreach (var n in neighbors)
                    {
                        int nid = n.tileId;
                        if (lakeSet.Contains(nid)) { if (adj == null) adj = new List<int>(); adj.Add(nid); continue; }
                        int npid = GetProvinceId(nid);
                        if (npid < 0 || npid == lake.id) continue;
                        if (!byId.TryGetValue(npid, out var np)) continue;
                        if (np.provinceType == ProvinceType.Ocean || np.provinceType == ProvinceType.Lake)
                        {
                            touchesOtherWater = true; break;   // a sea inlet, not an enclosed lake
                        }
                        // A benefits-suppressed speck (a kept isolated tiny region, #51) is NOT a lake shore:
                        // giving it water would balloon a 1-tile region back into a many-tile one. The water
                        // goes to the real shore regions instead.
                        if (np.provinceType == ProvinceType.Land && !np.benefitsSuppressed)
                        {
                            if (labels == null) labels = new List<int>();
                            labels.Add(npid);
                        }
                    }
                    if (touchesOtherWater) break;
                    if (adj != null) lakeAdj[t] = adj;
                    if (labels != null) shoreLabels[t] = labels;
                }

                if (touchesOtherWater) continue;
                if (shoreLabels.Count == 0) continue;   // no land shore at all — leave it as water

                var assign = Partition.LakeSplitRules.Split(lake.tiles, lakeAdj, shoreLabels);
                if (assign.Count == 0) continue;

                foreach (var kv in assign)
                {
                    if (!byId.TryGetValue(kv.Value, out var host)) continue;
                    host.tiles.Add(kv.Key);
                    tileToProvinceId[kv.Key] = host.id;
                    movedTiles++;
                }
                toRemove.Add(lake);
                splitLakes++;
            }

            foreach (var p in toRemove) provinces.Remove(p);
            if (splitLakes > 0)
                Log.Message($"[RegionsAndSocieties] SplitInlandLakes (#48): split {splitLakes} inland lake(s), "
                    + $"{movedTiles} water tile(s) shared among their shore regions.");
        }

        /// <summary>
        /// Fold unclaimed, non-water tile pockets that are fully enclosed by a single land region into
        /// that region (#3). Impassable mountains are excluded from region growth and otherwise sit as
        /// unowned holes; when such a pocket touches exactly one land region (and neither water nor an
        /// ocean province), it belongs to that region and is absorbed. A pocket bordering two or more
        /// regions is a genuine natural boundary and is left alone.
        /// </summary>
        private void AbsorbEnclosedGaps()
        {
            if (provinces == null || tileToProvinceId == null || Find.WorldGrid == null) return;

            var byId = provinces.ToDictionary(p => p.id, p => p);
            int total = tileToProvinceId.Length;
            var visited = new bool[total];
            var neighbors = new List<RimWorld.Planet.PlanetTile>();
            int absorbed = 0;

            for (int t = 0; t < total; t++)
            {
                if (visited[t]) continue;
                visited[t] = true;
                if (tileToProvinceId[t] != -1) continue;
                if (Find.WorldGrid[t].WaterCovered) continue;   // water gaps are not "enclosed by land"

                // Flood the connected unclaimed, non-water pocket, recording which land regions ring it.
                var pocket = new List<int> { t };
                var queue = new Queue<int>();
                queue.Enqueue(t);
                var ringRegions = new HashSet<int>();
                bool openToWater = false;

                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    neighbors.Clear();
                    Find.WorldGrid.GetTileNeighbors(cur, neighbors);
                    foreach (var n in neighbors)
                    {
                        int nid = n.tileId;
                        int npid = tileToProvinceId[nid];
                        if (npid == -1)
                        {
                            if (Find.WorldGrid[nid].WaterCovered) { openToWater = true; continue; }
                            if (!visited[nid]) { visited[nid] = true; pocket.Add(nid); queue.Enqueue(nid); }
                        }
                        else if (byId.TryGetValue(npid, out var np) && np.provinceType == ProvinceType.Land)
                        {
                            ringRegions.Add(npid);
                        }
                        else
                        {
                            openToWater = true;   // bordered by ocean / a water province
                        }
                    }
                }

                if (!openToWater && ringRegions.Count == 1)
                {
                    var region = byId[System.Linq.Enumerable.First(ringRegions)];
                    foreach (int c in pocket)
                    {
                        tileToProvinceId[c] = region.id;
                        region.tiles.Add(c);
                    }
                    absorbed += pocket.Count;
                }
            }

            if (absorbed > 0)
            {
                Log.Message($"[RegionsAndSocieties] Absorbed {absorbed} enclosed impassable/unclaimed tiles into their surrounding regions.");
            }
        }

        // Split a province when its principal-axis ratio exceeds this — a long ribbon rather than a
        // basin. ~1.7 is the target (golden-ish) shape; 2.2 is where it reads as a fail.
        private const float ElongationTrigger = 2.2f;
        private const float ElongationTarget = 1.7f;

        /// <summary>
        /// Break ribbon-shaped land provinces into compact pieces (#20). Region size is allowed to vary,
        /// but a province stretched into a long valley reads as a partition failure even at a normal
        /// size. For each land province whose <see cref="Partition.BorderPartitioner.Elongation"/>
        /// exceeds <see cref="ElongationTrigger"/> and which is big enough for the pieces to stay viable,
        /// split it across its short axis into 2-3 blobs. Deterministic; runs after the merge so the
        /// pieces survive, and its seams are tidied by the smoothing pass that follows.
        /// </summary>
        private void SplitElongatedProvinces(int minViable)
        {
            if (provinces == null || tileToProvinceId == null || Find.WorldGrid == null) return;
            if (minViable < 20) minViable = 20;

            int nextId = provinces.Count > 0 ? provinces.Max(p => p.id) + 1 : 0;
            var toAdd = new List<GeographicProvince>();
            int split = 0;

            // Snapshot: we mutate the list as we go.
            foreach (var p in provinces.ToList())
            {
                if (p.provinceType != ProvinceType.Land || p.tiles == null) continue;
                if (p.tiles.Count < 2 * minViable) continue;

                float aspect = Partition.BorderPartitioner.Elongation(p.tiles);
                if (aspect < ElongationTrigger) continue;

                int byAspect = Mathf.RoundToInt(aspect / ElongationTarget);
                int byViable = p.tiles.Count / minViable;
                int pieces = Mathf.Clamp(Mathf.Min(byAspect, byViable), 2, 3);
                if (pieces < 2) continue;

                var groups = Partition.BorderPartitioner.SplitTiles(p.tiles, pieces);
                if (groups.Count < 2) continue;

                // Largest piece keeps p's identity; the rest become new provinces.
                groups.Sort((a, b) => b.Count.CompareTo(a.Count));
                p.tiles = groups[0];
                foreach (int t in p.tiles) tileToProvinceId[t] = p.id;
                p.primaryBiome = GetPrimaryBiome(p.tiles);

                for (int g = 1; g < groups.Count; g++)
                {
                    var np = new GeographicProvince(nextId++);
                    np.tiles = groups[g];
                    np.provinceType = ProvinceType.Land;
                    np.primaryBiome = GetPrimaryBiome(groups[g]);
                    np.name = GenerateProvinceName(np.id, np.primaryBiome, np.provinceType);
                    foreach (int t in groups[g]) tileToProvinceId[t] = np.id;
                    toAdd.Add(np);
                }
                split++;
            }

            provinces.AddRange(toAdd);
            if (split > 0)
                Log.Message($"[RegionsAndSocieties] Split {split} elongated province(s) into {split + toAdd.Count} pieces.");
        }

        /// <summary>
        /// Enforce region contiguity (region-106 bug). A merge or absorb pass can leave a land region as
        /// two DETACHED masses — a component that shares no hex edge with the rest of its region (across
        /// water or another region), which reads on the map as one province spanning two separate
        /// landmasses. A region is by definition one connected landmass, so split any multi-component
        /// region: the largest component keeps the region's id and name, each other component becomes its
        /// own new region (an island is legitimately its own region). Uses TRUE hex adjacency
        /// (GetTileNeighbors), so it is exact where a spatial heuristic is not.
        /// </summary>
        private void SplitDisconnectedRegions()
        {
            if (provinces == null || tileToProvinceId == null || Find.WorldGrid == null) return;
            int nextId = provinces.Count > 0 ? provinces.Max(p => p.id) + 1 : 0;
            var neighbors = new List<RimWorld.Planet.PlanetTile>();
            var toAdd = new List<GeographicProvince>();
            int splitRegions = 0, newPieces = 0;
            // Land province ids, so a component's boundary can be tested for an adjacent land region:
            // a small component touching only water/impassable/feature tiles is a true island (#49).
            var landIds = new HashSet<int>();
            foreach (var lp in provinces) if (lp.provinceType == ProvinceType.Land) landIds.Add(lp.id);

            foreach (var p in provinces)
            {
                if (p.provinceType != ProvinceType.Land || p.tiles == null || p.tiles.Count <= 1) continue;

                var members = new HashSet<int>(p.tiles);
                var seen = new HashSet<int>();
                var components = new List<List<int>>();
                var stack = new Stack<int>();
                foreach (int start in p.tiles)
                {
                    if (seen.Contains(start)) continue;
                    var comp = new List<int>();
                    stack.Clear(); stack.Push(start); seen.Add(start);
                    while (stack.Count > 0)
                    {
                        int cur = stack.Pop();
                        comp.Add(cur);
                        neighbors.Clear();
                        Find.WorldGrid.GetTileNeighbors(cur, neighbors);
                        for (int i = 0; i < neighbors.Count; i++)
                        {
                            int nid = neighbors[i].tileId;
                            if (members.Contains(nid) && !seen.Contains(nid)) { seen.Add(nid); stack.Push(nid); }
                        }
                    }
                    components.Add(comp);
                }

                if (components.Count <= 1) continue;

                // Largest component keeps p's identity. A smaller component that is a true small island —
                // fewer than SmallIslandMaxTiles tiles, touching no OTHER land region (only water /
                // impassable / feature tiles) — is legitimately part of its host (either #49 absorbed it or
                // it is a natural coastal islet of the same region), so it STAYS attached rather than being
                // spun back off. A component cut off by another LAND region is still split, as before (#49).
                components.Sort((a, b) => b.Count.CompareTo(a.Count));
                var keep = new List<int>(components[0]);
                bool splitAny = false;
                for (int c = 1; c < components.Count; c++)
                {
                    var comp = components[c];
                    if (comp.Count < Placement.IslandRules.SmallIslandMaxTiles
                        && IsWaterSurroundedComponent(comp, members, landIds, neighbors))
                    {
                        keep.AddRange(comp);   // true island fragment: keep it in the host region
                        continue;
                    }
                    var np = new GeographicProvince(nextId++);
                    np.tiles = comp;
                    np.provinceType = ProvinceType.Land;
                    np.primaryBiome = GetPrimaryBiome(comp);
                    np.name = GenerateProvinceName(np.id, np.primaryBiome, np.provinceType);
                    foreach (int t in comp) tileToProvinceId[t] = np.id;
                    toAdd.Add(np);
                    newPieces++;
                    splitAny = true;
                }
                p.tiles = keep;
                p.primaryBiome = GetPrimaryBiome(p.tiles);
                if (splitAny) splitRegions++;
            }

            provinces.AddRange(toAdd);
            if (splitRegions > 0)
                Log.Message($"[RegionsAndSocieties] SplitDisconnectedRegions: split {splitRegions} region(s) into {newPieces} extra piece(s) to enforce contiguity.");
        }


        /// <summary>
        /// Small-island handling (#49): absorb lone small islands into the nearest mainland, but group
        /// island clusters into chains first so a &gt;=30-tile archipelago becomes its own region instead of
        /// being scattered onto whatever coast each speck is nearest. A "small island" is a Land region
        /// under <see cref="Placement.IslandRules.SmallIslandMaxTiles"/> tiles that touches no other land
        /// region (water / impassable / feature tiles only). Runs before SplitDisconnectedRegions, whose
        /// island exemption then keeps the absorbed/chained pieces attached. Size and chain decisions are
        /// the pure <see cref="Placement.IslandRules"/>.
        /// </summary>
        private void ResolveSmallIslands()
        {
            if (provinces == null || tileToProvinceId == null || Find.WorldGrid == null) return;

            var byId = provinces.ToDictionary(p => p.id, p => p);
            var landIds = new HashSet<int>();
            foreach (var lp in provinces) if (lp.provinceType == ProvinceType.Land) landIds.Add(lp.id);
            var neighbors = new List<RimWorld.Planet.PlanetTile>();

            // 1. Small water-surrounded island provinces.
            var islands = new List<GeographicProvince>();
            var smallIslandIds = new HashSet<int>();
            foreach (var p in provinces)
            {
                if (p.provinceType != ProvinceType.Land || p.tiles == null || p.tiles.Count == 0) continue;
                if (p.tiles.Count >= Placement.IslandRules.SmallIslandMaxTiles) continue;
                if (!IsWaterSurroundedComponent(p.tiles, new HashSet<int>(p.tiles), landIds, neighbors)) continue;
                islands.Add(p); smallIslandIds.Add(p.id);
            }
            if (islands.Count == 0) return;

            // 2. Per island: reachable sibling islands (to chain, union-find) and nearest mainland (absorb).
            var parent = new Dictionary<int, int>();
            foreach (var isl in islands) parent[isl.id] = isl.id;
            var nearestMainland = new Dictionary<int, int>();
            var nearestHops = new Dictionary<int, int>();
            foreach (var isl in islands)
            {
                IslandReachBFS(isl, smallIslandIds, landIds, neighbors,
                    out var reachedIslands, out int mainlandPid, out int mainlandHops);
                foreach (int oid in reachedIslands) UnionIslands(parent, isl.id, oid);
                nearestMainland[isl.id] = mainlandPid;
                nearestHops[isl.id] = mainlandHops;
            }

            // 3. Group into chains, resolve each by total size.
            var chains = new Dictionary<int, List<GeographicProvince>>();
            foreach (var isl in islands)
            {
                int r = FindIslandRoot(parent, isl.id);
                if (!chains.TryGetValue(r, out var list)) { list = new List<GeographicProvince>(); chains[r] = list; }
                list.Add(isl);
            }

            var toRemove = new HashSet<GeographicProvince>();
            int joined = 0, chainRegions = 0, kept = 0;
            foreach (var kv in chains)
            {
                var members = kv.Value;
                int chainTiles = 0; foreach (var m in members) chainTiles += m.tiles.Count;
                int chainHops = -1;
                foreach (var m in members) { int h = nearestHops[m.id]; if (h >= 0 && (chainHops < 0 || h < chainHops)) chainHops = h; }

                var res = Placement.IslandRules.ResolveChain(chainTiles, chainHops);
                if (res == Placement.IslandChainResolution.FormChainRegion)
                {
                    // Merge the chain's islands into their lowest-id member: one archipelago region whose
                    // water-separated parts SplitDisconnectedRegions leaves attached (the island exemption).
                    members.Sort((a, b) => a.id.CompareTo(b.id));
                    var host = members[0];
                    for (int i = 1; i < members.Count; i++)
                    {
                        foreach (int t in members[i].tiles) { host.tiles.Add(t); tileToProvinceId[t] = host.id; }
                        toRemove.Add(members[i]);
                    }
                    host.primaryBiome = GetPrimaryBiome(host.tiles);
                    host.name = GenerateProvinceName(host.id, host.primaryBiome, host.provinceType);
                    chainRegions++;
                }
                else if (res == Placement.IslandChainResolution.JoinMainland)
                {
                    foreach (var m in members)
                    {
                        int target = nearestMainland[m.id];
                        if (target != -1 && byId.TryGetValue(target, out var host) && !toRemove.Contains(host))
                        {
                            foreach (int t in m.tiles) { host.tiles.Add(t); tileToProvinceId[t] = host.id; }
                            host.primaryBiome = GetPrimaryBiome(host.tiles);
                            toRemove.Add(m); joined++;
                        }
                        else kept++;   // reach evaporated (target already consumed) — leave it standing
                    }
                }
                else kept += members.Count;   // KeepSeparate: no mainland in reach, chain too small
            }

            if (toRemove.Count > 0) provinces.RemoveAll(p => toRemove.Contains(p));
            Log.Message($"[RegionsAndSocieties] ResolveSmallIslands (#49): {joined} island(s) joined mainland, "
                + $"{chainRegions} island-chain region(s) formed, {kept} kept separate (of {islands.Count} small island(s)).");
        }

        private static int FindIslandRoot(Dictionary<int, int> parent, int x)
        {
            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
            return x;
        }

        private static void UnionIslands(Dictionary<int, int> parent, int a, int b)
        {
            int ra = FindIslandRoot(parent, a), rb = FindIslandRoot(parent, b);
            if (ra != rb) parent[ra] = rb;
        }

        /// <summary>True when no tile of <paramref name="comp"/> touches a DIFFERENT land region — the
        /// component borders only water, impassable rock, or non-Land feature provinces. <paramref
        /// name="members"/> is the full tile set the component belongs to (its own region); those tiles
        /// never count as an outside land neighbour.</summary>
        private bool IsWaterSurroundedComponent(List<int> comp, HashSet<int> members, HashSet<int> landIds,
            List<RimWorld.Planet.PlanetTile> neighbors)
        {
            foreach (int t in comp)
            {
                neighbors.Clear();
                Find.WorldGrid.GetTileNeighbors(t, neighbors);
                foreach (var n in neighbors)
                {
                    int nid = n.tileId;
                    if (members.Contains(nid)) continue;
                    int np = GetProvinceId(nid);
                    if (np != -1 && landIds.Contains(np)) return false;   // adjacent to another land region
                }
            }
            return true;
        }

        /// <summary>BFS over water from an island's tiles out to <see
        /// cref="Placement.IslandRules.IslandAbsorbHops"/> hops. Collects the ids of other small islands
        /// reached (to chain) and picks the nearest mainland land region (to absorb into), tie-broken by
        /// most shore tiles facing the island, then lowest id. A negative <paramref name="mainlandHops"/>
        /// means no mainland is in reach.</summary>
        private void IslandReachBFS(GeographicProvince island, HashSet<int> smallIslandIds, HashSet<int> landIds,
            List<RimWorld.Planet.PlanetTile> neighbors,
            out List<int> reachedIslands, out int mainlandPid, out int mainlandHops)
        {
            reachedIslands = new List<int>();
            mainlandPid = -1; mainlandHops = -1;

            var reachedSet = new HashSet<int>();
            var mainlandHits = new Dictionary<int, int>();
            var mainlandFirstHop = new Dictionary<int, int>();

            var queue = new Queue<KeyValuePair<int, int>>();
            var visited = new HashSet<int>();
            foreach (int t in island.tiles) { queue.Enqueue(new KeyValuePair<int, int>(t, 0)); visited.Add(t); }

            int maxHops = Placement.IslandRules.IslandAbsorbHops;
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                int tile = cur.Key, depth = cur.Value;
                neighbors.Clear();
                Find.WorldGrid.GetTileNeighbors(tile, neighbors);
                foreach (var n in neighbors)
                {
                    int nid = n.tileId;
                    if (visited.Contains(nid)) continue;
                    visited.Add(nid);

                    // A LAND tile classifies (sibling island to chain, or mainland to absorb into) and stops
                    // the flood — we never cross land. WATER tiles are flooded across regardless of which
                    // water province owns them: ocean and lake tiles carry a province id, so testing pid
                    // here (as the old code did) treated the whole sea as a wall and the search never left
                    // the island's shore — the bug that made every island "keep separate".
                    if (!Find.WorldGrid[nid].WaterCovered)
                    {
                        int pid = GetProvinceId(nid);
                        if (pid != -1 && pid != island.id)
                        {
                            if (smallIslandIds.Contains(pid)) { if (reachedSet.Add(pid)) reachedIslands.Add(pid); }
                            else if (landIds.Contains(pid))
                            {
                                int c; mainlandHits.TryGetValue(pid, out c); mainlandHits[pid] = c + 1;
                                if (!mainlandFirstHop.ContainsKey(pid)) mainlandFirstHop[pid] = depth;
                            }
                        }
                        continue;   // never expand through land or impassable rock
                    }
                    if (depth < maxHops)
                        queue.Enqueue(new KeyValuePair<int, int>(nid, depth + 1));
                }
            }

            foreach (var kv in mainlandHits)
            {
                int pid = kv.Key, hits = kv.Value, hop = mainlandFirstHop[pid];
                if (mainlandPid == -1
                    || hop < mainlandHops
                    || (hop == mainlandHops && (hits > mainlandHits[mainlandPid]
                        || (hits == mainlandHits[mainlandPid] && pid < mainlandPid))))
                {
                    mainlandPid = pid; mainlandHops = hop;
                }
            }
        }

        /// <summary>
        /// Erode pendant tails and 1-tile protrusions from land provinces (#20). A majority-vote
        /// relaxation: a land tile wrapped by a neighbouring land province more than by its own
        /// (bestCount &gt; same, with same &lt;= 2 so straight and gently-curved edges are left alone) sits
        /// on a spike or a chain-tip, and moving it to that neighbour shortens the border. Water,
        /// rivers-as-edges and impassable tiles never vote, so real coastlines and feature borders are
        /// preserved. Iterated over a few passes so multi-tile tails resolve from the tip inward. This
        /// is the border-first counterpart to the grow-first smoothing that was removed with the
        /// grower — kept deliberately light, targeting only the raggedness the audit flags.
        /// </summary>
        private void SmoothRegionBoundaries(int passes)
        {
            if (provinces == null || tileToProvinceId == null || Find.WorldGrid == null) return;

            var landIds = new HashSet<int>(provinces
                .Where(p => p.provinceType == ProvinceType.Land)
                .Select(p => p.id));
            if (landIds.Count < 2) return;

            var neighbors = new List<RimWorld.Planet.PlanetTile>();
            var counts = new Dictionary<int, int>();

            for (int pass = 0; pass < passes; pass++)
            {
                var reassign = new Dictionary<int, int>();
                for (int t = 0; t < tileToProvinceId.Length; t++)
                {
                    int pid = tileToProvinceId[t];
                    if (pid < 0 || !landIds.Contains(pid)) continue;
                    if (IsBarrierTile(t)) continue;                      // don't shuffle draped crest/coast tiles between regions

                    neighbors.Clear();
                    Find.WorldGrid.GetTileNeighbors(t, neighbors);
                    counts.Clear();
                    int same = 0, landNeighbours = 0, bestId = -1, bestCount = 0;
                    foreach (var n in neighbors)
                    {
                        int np = tileToProvinceId[n.tileId];
                        if (np < 0 || !landIds.Contains(np)) continue;   // coast/river/impassable edge: keep it
                        if (IsBarrierTile(n.tileId)) continue;           // never erode toward/across a hard wall (water/impassable)
                        // NOTE: biome edges are deliberately NOT blocked here. Shearing is bounded to
                        // protrusions (same<=2 + spike/tendril), so a straight biome border is never
                        // touched, but a 1-tile spider or a border snaking through a THICK biome-transition
                        // band gets shortened toward the straightest line through that band — the pure-biome
                        // cores stay put because their tiles have same>2 and never qualify.
                        landNeighbours++;
                        if (np == pid) { same++; continue; }
                        int c; counts.TryGetValue(np, out c); c++; counts[np] = c;
                        if (c > bestCount) { bestCount = c; bestId = np; }
                    }

                    // Two erosion cases, both requiring a foreign land neighbour to move into:
                    //   spike   — one neighbour wraps this tile more than its own province does;
                    //   tendril — more of this tile's land neighbours are foreign than are its own, i.e.
                    //             it sits on a 1-wide chain, even one running BETWEEN two provinces
                    //             (which the spike rule alone misses, since neither foreign province
                    //             need out-wrap the two chain neighbours). Both keep same<=2 so straight
                    //             and gently-curved borders are untouched; iterated, they shorten a
                    //             tail one tile per pass from the tip inward.
                    if (bestId != -1 && same <= 2)
                    {
                        int foreign = landNeighbours - same;
                        bool spike = landNeighbours >= 3 && bestCount > same;
                        bool tendril = foreign > same;
                        if (spike || tendril) reassign[t] = bestId;
                    }
                }

                if (reassign.Count == 0) break;
                foreach (var kv in reassign) tileToProvinceId[kv.Key] = kv.Value;
            }

            // Rebuild land tile lists from the corrected map; water/river provinces are untouched
            // above so their lists stay valid. Drop any land province emptied by the relaxation.
            var byId = provinces.ToDictionary(p => p.id, p => p);
            foreach (var p in provinces)
                if (landIds.Contains(p.id)) p.tiles = new List<int>();
            for (int t = 0; t < tileToProvinceId.Length; t++)
            {
                int pid = tileToProvinceId[t];
                GeographicProvince prov;
                if (pid >= 0 && landIds.Contains(pid) && byId.TryGetValue(pid, out prov))
                    prov.tiles.Add(t);
            }
            provinces.RemoveAll(p => landIds.Contains(p.id) && p.tiles.Count == 0);
        }

        /// <summary>True when a tile is a hard natural barrier the region passes must not merge or smooth
        /// across: water, or impassable rock / sea-ice. This matches the partition's wall set exactly —
        /// passable Mountainous / LargeHills are NOT barriers, they are claimable interior — so a region of
        /// passable mountain can still merge, and the recombine honours the same seams the fill did. An
        /// out-of-range or null tile reads as a barrier (safe default: never a merge seam).</summary>
        private static bool IsBarrierTile(int tile)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || tile < 0 || tile >= grid.TilesCount) return true;
            Tile t = grid[tile];
            if (t == null || t.WaterCovered) return true;
            if (t.hilliness == Hilliness.Impassable) return true;
            BiomeDef b = t.PrimaryBiome;
            return b != null && (b.impassable || b.defName == "SeaIce");
        }

        /// <summary>The biome of a tile, or null. Used to keep merges and smoothing within one biome.</summary>
        private static BiomeDef BiomeOfTile(int tile)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || tile < 0 || tile >= grid.TilesCount) return null;
            Tile t = grid[tile];
            return t?.PrimaryBiome;
        }

        /// <summary>Usable-tile count for a province, as an allocation-free loop (no LINQ closure).
        /// Called in the tight merge loop, where a per-call Count(predicate) closure was a memory sink.</summary>
        private int UsableTileCount(GeographicProvince p)
        {
            if (p?.tiles == null) return 0;
            int count = 0;
            List<int> tiles = p.tiles;
            for (int i = 0; i < tiles.Count; i++)
                if (IsTileUsable(tiles[i])) count++;
            return count;
        }

        private void MergeTinyDomains(int minWithFeatures, int minNoFeatures)
        {
            Log.Message($"[RegionsAndSocieties] MergeTinyDomains started. Initial region count: {provinces.Count}");
            List<RimWorld.Planet.PlanetTile> neighbors = new List<RimWorld.Planet.PlanetTile>();

            // Small-island absorption/chaining moved to its own pass (#49), ResolveSmallIslands, run
            // just before SplitDisconnectedRegions, whose island exemption then preserves it. The old
            // Pass 0 (islands < 5 tiles, 2 water hops) was silently undone by that split and is retired.
            int totalMerged = 0;

            int pass = 0;
            while (pass < 10) // Safety limit of 10 passes
            {
                pass++;
                bool mergedAnyInThisPass = false;
                // HashSet, not List: Contains() is hit once per province per pass, and an O(n) list scan
                // over 1000+ provinces across 10 passes was an O(n²) stall that helped starve worldgen.
                HashSet<GeographicProvince> toRemove = new HashSet<GeographicProvince>();

                // Build a quick map of province ID to the actual province object
                var provinceMap = provinces.ToDictionary(p => p.id, p => p);

                foreach (var p in provinces)
                {
                    if (p.provinceType == ProvinceType.Ocean || p.provinceType == ProvinceType.MountainRange) continue;
                    if (toRemove.Contains(p)) continue;

                    int pSize = p.tiles.Count;
                    bool isFeature = p.provinceType == ProvinceType.River || p.provinceType == ProvinceType.Lake || p.provinceType == ProvinceType.MountainRange;
                    int baseThreshold = isFeature ? 30 : minNoFeatures;

                    // Scale threshold dynamically based on tile resource density
                    float resWeight = GetResourceWeight(p);
                    float scale = Mathf.Clamp(1.5f / Mathf.Max(resWeight, 0.1f), 1f, 5f);
                    int threshold = Mathf.RoundToInt(baseThreshold * scale);

                    if (pSize >= threshold) continue;

                    // Find adjacent neighbors — but only across a shared edge that is genuinely mergeable:
                    // both tiles passable (not a range/coast/impassable) AND the same biome as this region.
                    // This is what stops the recombine from folding a shard across a barrier or into a
                    // different biome — the two things the grid partition was careful to separate. A shard
                    // with no same-biome passable neighbour simply survives (a small correct region beats a
                    // barrier-crossing merge).
                    BiomeDef pBiome = p.primaryBiome;
                    Dictionary<int, int> neighborWeights = new Dictionary<int, int>();

                    foreach (int tile in p.tiles)
                    {
                        if (IsBarrierTile(tile)) continue;                      // draped crest/coast tiles don't seek merges
                        if (BiomeOfTile(tile) != pBiome) continue;             // only merge from the region's own-biome body
                        neighbors.Clear();
                        Find.WorldGrid.GetTileNeighbors(tile, neighbors);
                        foreach (var n in neighbors)
                        {
                            int neighborId = n.tileId;
                            if (IsBarrierTile(neighborId)) continue;           // a wall between us = not a merge seam
                            if (BiomeOfTile(neighborId) != pBiome) continue;   // biome edge = seam, never merge across
                            int neighborProvinceId = GetProvinceId(neighborId);
                            if (neighborProvinceId != -1 && neighborProvinceId != p.id)
                            {
                                // If the neighbor province was already marked to be removed in this pass, ignore it
                                if (provinceMap.TryGetValue(neighborProvinceId, out var neighborProv))
                                {
                                    if (neighborProv.provinceType == ProvinceType.Ocean || toRemove.Contains(neighborProv)) continue;

                                    if (!neighborWeights.ContainsKey(neighborProvinceId))
                                    {
                                        neighborWeights[neighborProvinceId] = 0;
                                    }
                                    neighborWeights[neighborProvinceId] += 1;
                                }
                            }
                        }
                    }

                    if (neighborWeights.Any())
                    {
                        var sortedNeighbors = neighborWeights.OrderByDescending(kv => kv.Value).ToList();
                        GeographicProvince bestNeighbor = null;
                        GeographicProvince dominantLand = null;

                        // Compute p's usable-tile count once, not once per neighbour: the per-neighbour
                        // LINQ Count(predicate) allocated a closure every call and was the small-allocation
                        // storm the OOM crash dump showed.
                        int pUsable = UsableTileCount(p);

                        foreach (var kvp in sortedNeighbors)
                        {
                            if (provinceMap.TryGetValue(kvp.Key, out var neighborProv))
                            {
                                // Never adopt a target in a different biome — even the orphan rescue below
                                // stays in-biome, because its candidates come only from this same list.
                                if (neighborProv.primaryBiome != pBiome) continue;

                                // Remember the highest-weight (most shared edges) land neighbour as a
                                // rescue target, regardless of the size cap.
                                if (dominantLand == null && neighborProv.provinceType == ProvinceType.Land)
                                    dominantLand = neighborProv;

                                if (UsableTileCount(neighborProv) + pUsable <= EffectiveTargetRegionSize + 50)
                                {
                                    bestNeighbor = neighborProv;
                                    break;
                                }
                            }
                        }

                        // Orphan rescue (#3, widened #20): a small province whose only neighbours are
                        // already at or past the size cap would otherwise survive as a stranded sliver
                        // next to a big region. Fold it into its dominant land neighbour anyway — an
                        // oversized region reads far better than a too-small one, and large sparse
                        // regions are natural here. Bounded to genuinely small provinces (< the target
                        // minimum) so a medium region is never chained into a runaway monster.
                        if (bestNeighbor == null && dominantLand != null &&
                            p.tiles.Count < EffectiveMergeFloor)
                        {
                            bestNeighbor = dominantLand;
                        }

                        if (bestNeighbor != null)
                        {
                            // Merge p into bestNeighbor
                            foreach (int tileId in p.tiles)
                            {
                                bestNeighbor.tiles.Add(tileId);
                                tileToProvinceId[tileId] = bestNeighbor.id;
                            }
                            toRemove.Add(p);
                            mergedAnyInThisPass = true;
                            totalMerged++;
                        }
                    }

                    // Cross-biome fallback for a genuinely tiny sliver that found no same-biome, barrier-
                    // free neighbour (a pass fragment or a tiny inclusion touching several regions): fold it
                    // into its largest passable land neighbour of ANY biome. A few mixed tiles at the margin
                    // read far better than a 1-3 tile region of its own; bounded to very small p so normal
                    // regions stay biome-pure.
                    if (!toRemove.Contains(p) && p.tiles.Count < EffectiveMergeFloor / 3)
                    {
                        GeographicProvince bestAny = null; int bestAnySize = -1;
                        var seenN = new HashSet<int>();
                        foreach (int tile in p.tiles)
                        {
                            if (IsBarrierTile(tile)) continue;
                            neighbors.Clear(); Find.WorldGrid.GetTileNeighbors(tile, neighbors);
                            foreach (var n in neighbors)
                            {
                                if (IsBarrierTile(n.tileId)) continue;
                                int npid = GetProvinceId(n.tileId);
                                if (npid == -1 || npid == p.id || !seenN.Add(npid)) continue;
                                if (!provinceMap.TryGetValue(npid, out var nprov)) continue;
                                if (nprov.provinceType != ProvinceType.Land || toRemove.Contains(nprov)) continue;
                                if (nprov.tiles.Count > bestAnySize) { bestAnySize = nprov.tiles.Count; bestAny = nprov; }
                            }
                        }
                        if (bestAny != null)
                        {
                            foreach (int tileId in p.tiles) { bestAny.tiles.Add(tileId); tileToProvinceId[tileId] = bestAny.id; }
                            toRemove.Add(p); mergedAnyInThisPass = true; totalMerged++;
                        }
                    }
                }

                if (!mergedAnyInThisPass)
                {
                    break;
                }

                // Remove the merged provinces
                foreach (var p in toRemove)
                {
                    provinces.Remove(p);
                }
            }

            Log.Message($"[RegionsAndSocieties] MergeTinyDomains finished. Merged {totalMerged} regions in {pass} passes. Final region count: {provinces.Count}");
        }

        /// <summary>
        /// Fold every land region ENTIRELY enclosed by a single other land region into that region. A
        /// region's enclosure is judged over its LAND-region neighbours only — water, impassable-mountain
        /// (MountainRange) and off-map borders don't count against it — so a coastal or range-flanked
        /// sliver whose every land neighbour is one province q is an enclave of q and merges into it,
        /// regardless of biome. Iterated, because absorbing one enclave can enclose the next. Bounded to
        /// regions below the max size so a genuine large region is never swallowed.
        /// </summary>
        private void AbsorbEnclosedRegions()
        {
            if (provinces == null || tileToProvinceId == null || Find.WorldGrid == null) return;
            var neighbors = new List<RimWorld.Planet.PlanetTile>();
            int guard = 0, absorbed = 0;
            bool changed = true;
            while (changed && guard++ < 12)
            {
                changed = false;
                var byId = provinces.ToDictionary(p => p.id, p => p);
                var toRemove = new HashSet<GeographicProvince>();
                foreach (var p in provinces)
                {
                    if (p.provinceType != ProvinceType.Land || toRemove.Contains(p)) continue;
                    if (p.tiles == null || p.tiles.Count == 0) continue;
                    if (p.tiles.Count >= EffectiveTargetRegionSize) continue;   // never swallow a big region

                    int encloser = -2;   // -2 = none seen yet; -1 = more than one distinct land neighbour
                    foreach (int tile in p.tiles)
                    {
                        neighbors.Clear();
                        Find.WorldGrid.GetTileNeighbors(tile, neighbors);
                        foreach (var n in neighbors)
                        {
                            int npid = GetProvinceId(n.tileId);
                            if (npid == -1 || npid == p.id) continue;
                            if (!byId.TryGetValue(npid, out var nprov) || nprov.provinceType != ProvinceType.Land) continue;  // water / mountain don't break enclosure
                            if (toRemove.Contains(nprov)) continue;
                            if (encloser == -2) encloser = npid;
                            else if (encloser != npid) { encloser = -1; break; }
                        }
                        if (encloser == -1) break;
                    }

                    if (encloser >= 0 && byId.TryGetValue(encloser, out var q) && !toRemove.Contains(q))
                    {
                        foreach (int tileId in p.tiles) { q.tiles.Add(tileId); tileToProvinceId[tileId] = q.id; }
                        toRemove.Add(p); changed = true; absorbed++;
                    }
                }
                if (toRemove.Count > 0) provinces.RemoveAll(p => toRemove.Contains(p));
            }
            Log.Message($"[RegionsAndSocieties] AbsorbEnclosedRegions: folded {absorbed} enclave region(s).");
        }

        private float GetResourceWeight(GeographicProvince p)
        {
            if (p.tiles == null || p.tiles.Count == 0 || Find.WorldGrid == null) return 1.0f;
            float total = 0f;
            foreach (int tileId in p.tiles)
            {
                Tile t = Find.WorldGrid[tileId];
                var b = t.PrimaryBiome;
                if (b != null)
                {
                    total += b.plantDensity + b.forageability + BiomeSafe.TreeDensity(b);
                }
                if (t.hilliness == Hilliness.SmallHills) total += 0.5f;
                else if (t.hilliness == Hilliness.LargeHills) total += 1.0f;
                else if (t.hilliness == Hilliness.Mountainous) total += 1.5f;
            }
            return total / p.tiles.Count;
        }

        private void ResolveContextualNames()
        {
            if (Find.WorldFeatures == null || Find.WorldFeatures.features.NullOrEmpty()) return;

            // Cache centroids of all vanilla WorldFeatures
            var featureCentroids = new Dictionary<WorldFeature, Vector3>();
            foreach (var wf in Find.WorldFeatures.features)
            {
                if (!wf.Tiles.Any()) continue;
                Vector3 center = Vector3.zero;
                foreach (int t in wf.Tiles)
                {
                    center += Find.WorldGrid.GetTileCenter(t);
                }
                featureCentroids[wf] = center / wf.Tiles.Count();
            }

            foreach (var province in provinces)
            {
                if (province.tiles.Count == 0) continue;

                // Calculate province centroid
                Vector3 provinceCenter = Vector3.zero;
                foreach (int t in province.tiles)
                {
                    provinceCenter += Find.WorldGrid.GetTileCenter(t);
                }
                provinceCenter /= province.tiles.Count;

                // Find the closest WorldFeature
                WorldFeature closestFeature = null;
                float minSqrDist = float.MaxValue;
                foreach (var kvp in featureCentroids)
                {
                    float sqrDist = (provinceCenter - kvp.Value).sqrMagnitude;
                    if (sqrDist < minSqrDist)
                    {
                        minSqrDist = sqrDist;
                        closestFeature = kvp.Key;
                    }
                }

                if (closestFeature != null)
                {
                    // If directly overlapping a vanilla feature, use its name — but land regions keep the
                    // simple "Region <id>" / "<settlement> Region" scheme (0.7.3); only water/mountain
                    // features carry a geographic name.
                    var directOverlap = Find.WorldFeatures.features
                        .FirstOrDefault(wf => wf.Tiles.Any(t => province.tiles.Contains(t)));

                    if (directOverlap != null && province.provinceType != ProvinceType.Land)
                    {
                        province.name = directOverlap.name;
                    }
                    else
                    {
                        // Infer name based on closest feature
                        if (province.provinceType == ProvinceType.Lake)
                        {
                            province.name = closestFeature.name.Contains("Lake") || closestFeature.name.Contains("Sea") 
                                ? closestFeature.name 
                                : $"{closestFeature.name} Lake";
                        }
                        else if (province.provinceType == ProvinceType.Ocean)
                        {
                            province.name = closestFeature.name.Contains("Ocean") 
                                ? closestFeature.name 
                                : $"{closestFeature.name} Ocean";
                        }
                        else if (province.provinceType == ProvinceType.MountainRange)
                        {
                            province.name = closestFeature.name.Contains("Mountains") || closestFeature.name.Contains("Range") 
                                ? closestFeature.name 
                                : $"{closestFeature.name} Mountains";
                        }
                        else if (province.provinceType == ProvinceType.River)
                        {
                            province.name = GenerateRiverName(province.id, closestFeature.name);
                        }
                    }
                }
            }
        }

        private string GenerateRiverName(int id, string nearbyFeatureName)
        {
            var prefixes = new[] { "Silent", "Whispering", "Shimmering", "Roaring", "Winding", "Deep", "Swift", "Cold", "Grey", "Green", "Red", "Silver", "Golden", "Muddy", "Black", "Wild", "Broad", "Shadow", "Serpent", "Ghost", "Sun", "Moon", "Star", "Glimmering", "Ember", "Frost" };
            var suffixes = new[] { "River", "Creek", "Flow", "Fork", "Run", "Torrent", "Stream", "Waters", "Channel" };

            System.Random rand = new System.Random(id * 79 + 37);

            // 50% chance to name after nearby feature, 50% to generate a generic beautiful name
            if (rand.NextDouble() < 0.5f && !string.IsNullOrEmpty(nearbyFeatureName))
            {
                string cleanName = nearbyFeatureName
                    .Replace("Mountains", "")
                    .Replace("Mountain Range", "")
                    .Replace("Scrubland", "")
                    .Replace("Scrublands", "")
                    .Replace("Forest", "")
                    .Replace("Tangle", "")
                    .Replace("Basin", "")
                    .Replace("Swamp", "")
                    .Replace("Bog", "")
                    .Trim();

                string suffix = suffixes[rand.Next(suffixes.Length)];
                return $"{cleanName} {suffix}";
            }
            else
            {
                string prefix = prefixes[rand.Next(prefixes.Length)];
                string suffix = suffixes[rand.Next(suffixes.Length)];
                return $"{prefix} {suffix}";
            }
        }

        private string GenerateProvinceName(int provinceId, BiomeDef biome, ProvinceType type)
        {
            if (type == ProvinceType.Ocean) return "Ocean Region " + provinceId;
            if (type == ProvinceType.Lake) return "Lake Region " + provinceId;
            if (type == ProvinceType.River) return "River Region " + provinceId;
            if (type == ProvinceType.MountainRange) return "Mountain Region " + provinceId;

            return GenerateProvinceName(provinceId, biome);
        }

        private string GenerateProvinceName(int provinceId, BiomeDef biome)
        {
            // 0.7.3: a land region is simply "Region <id>". If it holds a settlement, RecalculateProvinceOwners
            // renames it "<settlement> Region"; the id is always shown in the expanded region details.
            return "Region " + provinceId;
        }

        /// <summary>
        /// 0.7.3 naming: a land region is named after the settlement standing in it ("&lt;settlement&gt; Region"),
        /// or "Region &lt;id&gt;" when it holds none. Called each ownership recompute so the name tracks a
        /// settlement being founded or lost. Water/mountain regions keep their geographic feature names.
        /// The id itself is always shown in the expanded region details, independent of the name.
        /// </summary>
        private void UpdateProvinceName(GeographicProvince province, List<RimWorld.Planet.WorldObject> regionObjects)
        {
            if (province == null || province.provinceType != ProvinceType.Land) return;

            RimWorld.Planet.WorldObject settlement = null;
            if (regionObjects != null)
            {
                foreach (var o in regionObjects)
                {
                    if (o != null && o.Faction != null && Integration.WorldObjectClassifier.IsSettlement(o)) { settlement = o; break; }
                }
            }
            province.name = settlement != null ? $"{settlement.LabelCap} Region" : "Region " + province.id;
        }

        private bool topologyBuilt;

        /// <summary>
        /// Precompute every province's perimeter tiles and per-neighbour border-edge counts in a
        /// single pass over the world grid. Province topology is fixed once the provinces exist, so
        /// this runs once — at generation, and rebuilt lazily after a load — and every later
        /// perimeter/border query reads the aggregate instead of rescanning tiles. Replaces the
        /// per-call flood-fill in <see cref="RegionalOwnershipUtility.GetPerimeterTiles"/> and gives
        /// the border-share data the ownership scoring consumes.
        /// </summary>
        public void BuildProvinceTopology()
        {
            if (provinces == null || tileToProvinceId == null || Find.WorldGrid == null) return;

            // Local id map: GetProvince is an O(provinces) scan, so calling it per tile would make
            // this O(tiles * provinces).
            var byId = new Dictionary<int, GeographicProvince>(provinces.Count);
            foreach (var p in provinces)
            {
                p.perimeterTiles = new List<int>();
                p.borderShares = new Dictionary<int, int>();
                p.perimeterEdgeCount = 0;
                p.naturalBorderEdges = 0;
                byId[p.id] = p;
            }

            var neighbors = new List<RimWorld.Planet.PlanetTile>();
            for (int t = 0; t < tileToProvinceId.Length; t++)
            {
                int pid = tileToProvinceId[t];
                if (pid < 0) continue;
                GeographicProvince prov;
                if (!byId.TryGetValue(pid, out prov)) continue;
                // Water provinces are never owned or contested, so they need no perimeter/border-share
                // topology. Skipping them also stops the (huge, claimed) ocean from accumulating a
                // border-share to every coastal land province — the source of the "coastal faction
                // holds the sea" ownership bleed once the ocean became a real province (#20).
                if (prov.provinceType == ProvinceType.Ocean || prov.provinceType == ProvinceType.MountainRange) continue;

                neighbors.Clear();
                Find.WorldGrid.GetTileNeighbors(t, neighbors);
                bool boundary = false;
                foreach (var n in neighbors)
                {
                    int npid = tileToProvinceId[n.tileId];
                    if (npid == pid) continue;      // interior edge
                    boundary = true;
                    prov.perimeterEdgeCount++;

                    // A frontier against water, an impassable mountain, or a RIDGE is a secure natural
                    // border — it counts for this region's own owner, not as a contestable land border
                    // (#44). Since 0.3.0 the border-first partition draws borders ON passable ridges
                    // (LargeHills+), treating them as interior terrain, so an enclosed basin's whole ring
                    // is high ground; counting LargeHills+ as a wall lets a region earn the ridge it sits
                    // behind, regardless of who is beyond it — "geography is a free wall" (#50). The edge
                    // is scored by the NEIGHBOUR tile's terrain, exactly as water is.
                    Tile nt = Find.WorldGrid[n.tileId];
                    bool naturalBarrier = nt.WaterCovered
                        || nt.hilliness >= Hilliness.LargeHills
                        || (nt.PrimaryBiome != null && nt.PrimaryBiome.impassable);
                    if (naturalBarrier)
                    {
                        prov.naturalBorderEdges++;
                    }
                    else if (npid >= 0)             // contestable edge to another land province
                    {
                        int c;
                        prov.borderShares.TryGetValue(npid, out c);
                        prov.borderShares[npid] = c + 1;
                    }
                    // else: unassigned non-natural land (rare) — not counted
                }
                if (boundary) prov.perimeterTiles.Add(t);
            }
            topologyBuilt = true;
        }

        /// <summary>Build the topology aggregate once per session (covers both generation and load).</summary>
        public void EnsureTopology()
        {
            if (!topologyBuilt) BuildProvinceTopology();
        }

        private static readonly List<RimWorld.Planet.WorldObject> EmptyWorldObjects = new List<RimWorld.Planet.WorldObject>();

        // Bumped whenever a territorial holding (settlement/outpost/military/camp) is added or
        // removed — the only world-object changes that alter ownership. Static so it survives the
        // fresh component a load creates and so the PostAdd/PostRemove patch can bump it without an
        // instance. Population changes (which do not affect ownership) deliberately do not bump it.
        private static int ownershipEpoch;
        public static void BumpOwnershipEpoch()
        {
            ownershipEpoch++;
            // A territorial holding changed, so the global border overlay's per-region colours are stale.
            // Bump its build version so the world layer rebuilds its mesh next frame (#72). Without this the
            // overlay — which only rebuilds on a version change — keeps whatever colours it first painted.
            UI.RegionBorderOverlay.Invalidate();
        }

        private int ownersComputedVersion = -1;
        private int ownersFactionCount = -1;

        /// <summary>Force the next <see cref="RecalculateProvinceOwners"/> to recompute rather than
        /// reuse the cache — for inputs the epoch/count gate does not observe (e.g. a demographic
        /// provider registering, or a settlement changing faction without an add/remove).</summary>
        /// <summary>
        /// Discard the partition (including a half-built one left by a throw during world generation) so
        /// the lazy <see cref="Provinces"/> getter rebuilds it from scratch on the next read.
        /// </summary>
        public void ResetProvinces()
        {
            provinces.Clear();
            _provinceById = null;
            if (tileToProvinceId != null)
            {
                for (int i = 0; i < tileToProvinceId.Length; i++) tileToProvinceId[i] = -1;
            }
            ProvinceAdjacency.ClearCache();
            MarkOwnersDirty();
        }

        public void MarkOwnersDirty()
        {
            ownersComputedVersion = -1;
        }

        public void RecalculateProvinceOwners()
        {
            if (Find.WorldObjects == null || provinces == null) return;
            EnsureTopology();

            // Ownership depends only on the territorial holdings present (add/remove bumps
            // ownershipEpoch) and on which factions exist (defeat/creation changes the count). When
            // neither has changed since the last pass the cached ownershipData/owningFactionIds are
            // still valid, so the entire recompute — bucketing, perimeter owner mapping, scoring — is
            // skipped. This is what turns "recompute on every draw" into "recompute only on change"
            // (#48). MarkOwnersDirty covers the inputs this gate cannot see.
            int epoch = ownershipEpoch;
            int factionCount = Find.FactionManager?.AllFactionsListForReading?.Count ?? 0;
            if (ownersComputedVersion == epoch && ownersFactionCount == factionCount) return;
            ownersComputedVersion = epoch;
            ownersFactionCount = factionCount;

            // Bucket every world object into its province in one pass (O(worldObjects)), so each
            // province's ownership reads its own objects instead of filtering AllWorldObjects with a
            // List.Contains over its tiles — which was O(worldObjects * tiles) per province (#48).
            var objectsByProvince = new Dictionary<int, List<RimWorld.Planet.WorldObject>>();
            foreach (var obj in Find.WorldObjects.AllWorldObjects)
            {
                if (obj == null) continue;
                int opid = GetProvinceId(obj.Tile);
                if (opid < 0) continue;
                List<RimWorld.Planet.WorldObject> bucket;
                if (!objectsByProvince.TryGetValue(opid, out bucket))
                {
                    bucket = new List<RimWorld.Planet.WorldObject>();
                    objectsByProvince[opid] = bucket;
                }
                bucket.Add(obj);
            }

            // Pass 1: each province's ownership from its own holdings only, plus its dominant owner —
            // what neighbours read when computing their border scores.
            var ownerByProvince = new Dictionary<int, Faction>(provinces.Count);
            foreach (var province in provinces)
            {
                // Open water is never owned — skip it so a coastal faction is not written in as
                // "holding" the sea (which would leak supply anchors and foothold adjacency along the
                // whole coastline now that the ocean is a real province, #20).
                if (province.provinceType == ProvinceType.Ocean || province.provinceType == ProvinceType.MountainRange) { province.owningFactionIds.Clear(); continue; }

                List<RimWorld.Planet.WorldObject> regionObjects;
                if (!objectsByProvince.TryGetValue(province.id, out regionObjects)) regionObjects = EmptyWorldObjects;
                province.ownershipData = RegionalOwnershipUtility.CalculateOwnershipBase(province, regionObjects);
                ownerByProvince[province.id] = RegionalOwnershipUtility.DominantBaseOwner(province.ownershipData);
                UpdateProvinceName(province, regionObjects);
            }

            // Pass 2: fold in border influence from neighbours' owners over the static borderShares,
            // normalize, and publish the owning-faction list. The geometry is precomputed, so this is
            // where "region 487 changed owner -> recompute 326's borders" stays cheap (#44).
            foreach (var province in provinces)
            {
                if (province.provinceType == ProvinceType.Ocean || province.provinceType == ProvinceType.MountainRange) continue;
                RegionalOwnershipUtility.ApplyBordersAndNormalize(province.ownershipData, province, ownerByProvince);

                province.owningFactionIds.Clear();
                var data = province.ownershipData;
                if (data != null && data.factionScores != null)
                {
                    foreach (var fs in data.factionScores)
                    {
                        if (fs.faction != null && fs.TotalScore > Placement.PlacementRules.PresenceFloor)
                        {
                            string fid = fs.faction.GetUniqueLoadID();
                            if (!province.owningFactionIds.Contains(fid))
                            {
                                province.owningFactionIds.Add(fid);
                            }
                        }
                    }
                }
            }
        }

        public bool AreProvincesAdjacent(GeographicProvince a, GeographicProvince b)
        {
            if (a == null || b == null) return false;
            if (a.id == b.id) return true;

            // Check if any tile in 'a' shares a neighbor with any tile in 'b'
            foreach (int tileA in a.tiles)
            {
                foreach (int tileB in b.tiles)
                {
                    if (Find.WorldGrid.IsNeighbor(tileA, tileB))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static bool IsTileUsable(int tileId)
        {
            if (Find.WorldGrid == null) return false;
            Tile tileData = Find.WorldGrid[tileId];
            if (tileData == null) return false;
            if (tileData.WaterCovered || tileData.hilliness == Hilliness.Impassable) return false;
            if (tileData.PrimaryBiome != null && (tileData.PrimaryBiome.impassable || tileData.PrimaryBiome.defName == "SeaIce")) return false;
            return true;
        }
    }
}
