using RimWorld;
using RegionsAndSocieties.Integration;

namespace RegionsAndSocieties
{
    /// <summary>
    /// Kept as the outpost-shaped door onto the general placement rules.
    ///
    /// Up to 0.6 this class held the rules themselves and applied them to Vanilla Outposts
    /// Expanded alone. 0.7 moved them into <see cref="Placement.PlacementEvaluator"/> and
    /// generalised them to every kind of holding, from any mod. Existing call sites keep working
    /// unchanged; new ones should call <see cref="WorldObjectPlacementUtility"/> with the kind they
    /// actually mean.
    /// </summary>
    public static class OutpostPlacementUtility
    {
        /// <summary>Retained for source compatibility. The value now lives in <see cref="Placement.PlacementRules"/>.</summary>
        public const int MAX_SUPPLY_DISTANCE = Placement.PlacementRules.MaxSupplyDistance;

        /// <summary>Retained for source compatibility. The value now lives in <see cref="Placement.PlacementRules"/>.</summary>
        public const int MIN_BUFFER_DISTANCE = Placement.PlacementRules.PermanentHoldingSeparation;

        public static bool CanPlaceOutpostAt(int tileId, Faction faction, out string reason)
        {
            return WorldObjectPlacementUtility.CanPlaceAt(tileId, faction, WorldObjectKind.Outpost, out reason);
        }
    }
}
