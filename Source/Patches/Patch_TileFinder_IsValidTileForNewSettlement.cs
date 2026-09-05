using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RegionsAndSocieties.Integration;
using RegionsAndSocieties.Placement;
using Verse;

namespace RegionsAndSocieties.Patches
{
    [HarmonyPatch(typeof(TileFinder), nameof(TileFinder.IsValidTileForNewSettlement))]
    public static class Patch_TileFinder_IsValidTileForNewSettlement
    {
        [HarmonyPostfix]
        public static void Postfix(PlanetTile tile, ref bool __result, StringBuilder reason)
        {
            // If it's already invalid, do nothing
            if (!__result) return;

            // 0.3.2 (#37): govern only the checks a player is about to read, not the searches.
            //
            // IsValidTileForNewSettlement has two kinds of caller. The settle button and the starting-site
            // page pass a StringBuilder because they will show the player why a tile is refused. Site
            // SEARCHES — vanilla's TryFindNewSiteTile, quest nodes, other mods' site finders — pass no
            // reason and call this for every candidate tile of a flood. Running a full placement evaluation
            // per candidate turned those searches into long main-thread stalls, and refusing candidates on
            // the player's supply rule starved quests of sites altogether. The objects such searches place
            // are not player settlements, so our rules have no say in them. Mod-specific symptoms of this
            // (a dialog that runs such a search when it opens) are tracked in that mod's compatibility patch.
            if (reason == null) return;
            // PlanetTile is a struct in 1.6, so the old `tile == null` guard was dead code the
            // compiler warned about. This is what it was reaching for: reject the invalid tile.
            if (tile.tileId < 0) return;
            if (Find.World == null) return;

            // No player faction yet means we are still generating the world; faction bases are
            // placed by FactionPlacementUtility, which has its own rules.
            // OfPlayerSilentFail, not OfPlayer: this postfix runs during world gen (via
            // WorldGenStep_AncientSites probing tiles), and Faction.OfPlayer logs
            // "Could not find player faction." on every null return — 183 error-level lines per
            // world gen, invisible until Repo-MCP#17 made Log.Error classifiable (R&T#46).
            Faction player = Faction.OfPlayerSilentFail;
            if (player == null) return;

            // 0.7: all four rules — buffer, foreign territory, supply range, sequential expansion —
            // now come from one evaluator shared with every other placement path, so settling and
            // outpost-building can no longer disagree about the same tile.
            PlacementDecision decision = WorldObjectPlacementUtility.Evaluate(
                tile.tileId, player, WorldObjectKind.Settlement);

            if (!decision.Allowed)
            {
                __result = false;
                reason?.AppendLine(decision.Reason);
            }
        }
    }
}
