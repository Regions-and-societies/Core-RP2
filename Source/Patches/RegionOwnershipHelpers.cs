using System;
using RimWorld;
using RimWorld.Planet;
using RegionsAndSocieties.Integration;
using Verse;

namespace RegionsAndSocieties.Patches
{
    /// <summary>
    /// Region-ownership helpers shared by core's own patches (road overlay) and by compatibility
    /// patches (Empire's settlement-placement gate). Extracted from the old Empire patch file when
    /// that integration moved to its compatibility patch (Core-MMF#3).
    ///
    /// <para>"Is this a city" is now answered by the adapter registry instead of the old hardcoded
    /// FactionColonies/Outposts string checks — core no longer names foreign mods, and a patch that
    /// classifies its settlements through the registry automatically participates here.</para>
    /// </summary>
    public static class RegionOwnershipHelpers
    {
        /// <summary>
        /// A compatibility patch may override how "the player's faction" resolves — Empire runs the
        /// player's colonies under its own faction, which is not <see cref="Faction.OfPlayer"/>.
        /// First non-null answer wins; unset or throwing overrides fall back to OfPlayer.
        /// </summary>
        public static Func<Faction> PlayerFactionOverride;

        public static Faction GetPlayerFaction()
        {
            try
            {
                Faction overridden = PlayerFactionOverride?.Invoke();
                if (overridden != null) return overridden;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[RegionsAndSocieties] PlayerFactionOverride threw: {ex}", 991824);
            }
            return Faction.OfPlayerSilentFail;
        }

        /// <summary>The faction of the first settlement-classified object in the tile's province, or null.</summary>
        public static Faction GetRegionOwner(int tileId)
        {
            if (Find.World == null) return null;
            var regionManager = Find.World.GetComponent<SynapseRegionManager>();
            if (regionManager == null) return null;

            var province = regionManager.GetProvinceForTile(tileId);
            if (province == null) return null;

            foreach (int t in province.tiles)
            {
                var objects = Find.WorldObjects.ObjectsAt(t);
                foreach (var obj in objects)
                {
                    if (obj?.Faction == null) continue;
                    if (WorldObjectClassifier.Classify(obj) == WorldObjectKind.Settlement)
                    {
                        return obj.Faction;
                    }
                }
            }
            return null;
        }
    }
}
