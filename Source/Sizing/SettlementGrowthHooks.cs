using System;
using RimWorld.Planet;
using Verse;

namespace RegionsAndSocieties.Sizing
{
    /// <summary>One reported change in a settlement's modeled population (#6).</summary>
    public struct SettlementGrowthEvent
    {
        public WorldObject Settlement;
        public int PreviousPopulation;
        public int CurrentPopulation;
        public int Delta => CurrentPopulation - PreviousPopulation;
    }

    /// <summary>
    /// The public endpoint for settlement birthrate growth (#6). Core models the demography and
    /// publishes it; it does not decide what growth means for gameplay — a consumer mod (a settlement
    /// economy, a war/expansion driver, a UI) subscribes to react. Same no-op-without-consumer contract
    /// as the demographic hooks: with nothing subscribed, <see cref="Report"/> does nothing and the
    /// model runs purely as R&amp;T's own state.
    /// </summary>
    public static class SettlementGrowthHooks
    {
        /// <summary>Raised when a settlement's modeled population changes (integer level), each growth
        /// tick. Null — and so a no-op — until a consumer subscribes.</summary>
        public static event Action<SettlementGrowthEvent> GrowthReported;

        /// <summary>Whether any consumer is listening for growth events.</summary>
        public static bool HasConsumer => GrowthReported != null;

        /// <summary>The current modeled population of an NPC settlement (0 for the player colony, which
        /// is never modeled, or when there is no world).</summary>
        public static int CurrentPopulation(WorldObject settlement)
        {
            if (settlement == null) return 0;
            if (settlement.Faction != null && settlement.Faction.IsPlayer) return 0;
            var mgr = Find.World?.GetComponent<SynapseRegionManager>();
            return mgr != null ? mgr.GetModeledSettlementPopulation(settlement) : 0;
        }

        /// <summary>Publish a growth event. No-op when the population did not change or nothing is
        /// subscribed. Called by the growth tick.</summary>
        public static void Report(WorldObject settlement, int previousPopulation, int currentPopulation)
        {
            if (previousPopulation == currentPopulation) return;
            var handler = GrowthReported;
            if (handler == null) return;
            handler(new SettlementGrowthEvent
            {
                Settlement = settlement,
                PreviousPopulation = previousPopulation,
                CurrentPopulation = currentPopulation,
            });
        }
    }
}
