using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RegionsAndSocieties.Integration
{
    /// <summary>What a completed population-dynamics pass reports to a consumer (#5).</summary>
    public class PopulationMigrationArgs
    {
        public int colonyRegionId;   // the attractor this pass drifted toward, or -1 if none
        public float totalMoved;     // people migrated this pass (conserved: this left the source regions)
        public int passSerial;       // increments each pass, so a consumer can tell passes apart
    }

    /// <summary>
    /// Makes the population field respond to play (#5 migration, #8 accretion). Both passes mutate one
    /// shared per-region "dynamic delta" the region manager scribes, so they COMPOSE instead of fighting;
    /// the effective population of a region is its derived base plus this delta. The write order is fixed:
    /// growth (#6, elsewhere) → accrete (#8) → migrate (#5).
    ///
    /// <para><b>Scaffold.</b> The structure, cadence, governance gate, conservation and the public endpoint
    /// are in; every RATE / THRESHOLD / FALLOFF is a placeholder to tune in a later milestone (#30). The
    /// accretion body and the migration distance-falloff are stubbed and marked. The delta is not yet fused
    /// into the visible per-tile heatmap — that wiring is the follow-up.</para>
    ///
    /// <para>Public endpoint, reflection-friendly like <see cref="TerritoryClaimHooks"/>: core holds no
    /// reference to a consumer, so a consumer (Empire-CP, a storyteller) sets <see cref="OnMigrationPass"/>
    /// after load. A no-op with nothing hooked.</para>
    /// </summary>
    public static class PopulationDynamics
    {
        /// <summary>Optional consumer, fired at the end of every pass. Unset = no-op.</summary>
        public static Action<PopulationMigrationArgs> OnMigrationPass;

        // ---- placeholder tuning; deferred to 0.4.0/0.5.0 (#30) ----
        /// <summary>Cadence: every 10 in-game days (the region manager ticks at 60000/day).</summary>
        public const int CadenceTicks = 600000;
        /// <summary>Fraction of a region's movable population drawn toward the colony per pass. TODO tune.</summary>
        public const float MigrationRate = 0.05f;
        /// <summary>A region never migrates below this effective population. TODO tune.</summary>
        public const float RegionFloor = 5f;
        /// <summary>People a growing settlement accretes into each neighbour per pass. TODO tune (0 = stub off).</summary>
        public const float AccretionStep = 0f;

        private static int passSerial;

        /// <summary>Run the dynamics passes in the documented order (accrete → migrate) on the shared delta.
        /// No-op when placement governance is off — the field then never moves. Returns people migrated.</summary>
        public static float RunPasses(SynapseRegionManager mgr, Dictionary<int, float> delta)
        {
            if (mgr == null || delta == null) return 0f;
            if (!mgr.StrictTerritorialOwnership) return 0f;   // governance off → the heatmap never moves

            Accrete(mgr, delta);
            int colony = ColonyRegion(mgr);
            float moved = Migrate(mgr, delta, colony);

            passSerial++;
            Fire(new PopulationMigrationArgs { colonyRegionId = colony, totalMoved = moved, passSerial = passSerial });
            return moved;
        }

        /// <summary>#8 — surrounding regions fill in around a growing settlement, up to the tier/region cap.
        /// STUB until tuned: the write order and cap sharing with migration are established here, the actual
        /// accretion is deferred (AccretionStep 0 → no-op).</summary>
        private static void Accrete(SynapseRegionManager mgr, Dictionary<int, float> delta)
        {
            if (AccretionStep <= 0f) return;
            // TODO (#30): for each region whose settlement is below its dwelling target, add AccretionStep
            // to each adjacent region's delta, bounded so (base + delta) never exceeds the region/tier cap.
            // Must read the same caps migration respects so the two passes cannot double-count.
        }

        /// <summary>#5 — move movable population from every other region toward the colony, conserving the
        /// total (what leaves the sources is added to the colony) and never taking a region below its floor.
        /// Distance falloff is stubbed (uniform for now); tuning adds the adjacency-hop falloff and a colony
        /// ceiling so a long game cannot pile the planet onto one tile.</summary>
        private static float Migrate(SynapseRegionManager mgr, Dictionary<int, float> delta, int colonyRegion)
        {
            if (colonyRegion < 0) return 0f;
            float totalMoved = 0f;
            var provinces = mgr.Provinces;
            for (int i = 0; i < provinces.Count; i++)
            {
                var p = provinces[i];
                if (p.provinceType != ProvinceType.Land || p.id == colonyRegion) continue;

                float here = p.currentPopulation + Get(delta, p.id);
                float movable = here - RegionFloor;
                if (movable <= 0f) continue;

                // TODO (#30): scale by distance to the colony (adjacency hops) instead of a flat rate.
                float move = MigrationRate * movable;
                if (move <= 0f) continue;

                Add(delta, p.id, -move);
                Add(delta, colonyRegion, +move);
                totalMoved += move;
            }
            return totalMoved;   // conserved: every unit removed above was added to the colony
        }

        /// <summary>The region holding the player's colony — the migration attractor. -1 if the player has
        /// no world settlement yet.</summary>
        public static int ColonyRegion(SynapseRegionManager mgr)
        {
            var settlements = Find.WorldObjects?.Settlements;
            if (settlements == null) return -1;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s?.Faction != null && s.Faction.IsPlayer)
                {
                    var prov = mgr.GetProvinceForTile(s.Tile);
                    if (prov != null) return prov.id;
                }
            }
            return -1;
        }

        private static float Get(Dictionary<int, float> d, int id) => d.TryGetValue(id, out float v) ? v : 0f;
        private static void Add(Dictionary<int, float> d, int id, float v) => d[id] = Get(d, id) + v;

        private static void Fire(PopulationMigrationArgs a)
        {
            try { OnMigrationPass?.Invoke(a); }
            catch (Exception e) { Log.Error("[RegionsAndSocieties] PopulationDynamics consumer threw: " + e); }
        }
    }
}
