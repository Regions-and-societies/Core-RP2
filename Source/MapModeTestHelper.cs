using Verse;
using RimWorld;
using System.Linq;
using MapModeFramework;

namespace RegionsAndSocieties
{
    public class MapModeTestHelper : GameComponent
    {
        private bool triggered = false;

        public MapModeTestHelper(Game game)
        {
        }

        public override void GameComponentUpdate()
        {
            if (triggered) return;

            // Quicktest-only. This had no gate at all, so every player starting a game was yanked
            // to the world map and had their map mode switched 300 ticks in — and, with Empire
            // installed, had a foreign mod's destructive test suite run on them. A DevMode-only
            // gate still caught the many players who play with dev mode on, so the gate is the
            // -quicktest launch flag: it fires only on the automated test path, never a real game.
            if (!Prefs.DevMode || !GenCommandLine.CommandLineArgPassed("quicktest")) { triggered = true; return; }

            if (Find.CurrentMap == null) return;

            // Wait until 300 ticks to ensure map is fully loaded
            if (Find.TickManager.TicksGame > 300)
            {
                triggered = true;
                Log.Warning("[MapModeTestHelper] Auto-opening World Map and enabling Faction Territories overlay...");
                
                // Open World Map
                Find.World.renderer.wantedMode = RimWorld.Planet.WorldRenderMode.Planet;

                // Find MapModeComponent and set current map mode
                var mapModeComp = MapModeComponent.Instance;
                if (mapModeComp != null)
                {
                    var targetMode = mapModeComp.mapModes.FirstOrDefault(m => m.def.defName == "SynapseFactionTerritory");
                    if (targetMode != null)
                    {
                        mapModeComp.SwitchMapMode(targetMode);
                        Log.Warning("[MapModeTestHelper] Successfully activated Faction Territories map mode!");
                    }
                    else
                    {
                        Log.Error("[MapModeTestHelper] Could not find map mode def 'SynapseFactionTerritory'!");
                    }
                }
                else
                {
                    Log.Error("[MapModeTestHelper] MapModeComponent.Instance is null!");
                }

                // Empire's own test suite used to be invoked from here by reflection —
                // EmpireTestRunner.RunTests(null, false) and then RunTests(null, true), the second
                // being its *destructive* mode — synchronously, on the main thread, automatically.
                //
                // It did not return. The game was left burning a full core with Responding=false,
                // which stops every GameComponentUpdate consumer: rendering, ticking, and the tool
                // bridge's file poll. That is what made the in-game bridge look permanently broken
                // (Repo-MCP#12) when its paths were correct all along.
                //
                // Running another mod's tests is not this mod's business, and running the mode its
                // author labelled destructive is not something to do to a player's save. If Empire
                // integration needs exercising, it belongs in our own TestRunner cases against our
                // own patches. Do not reinstate this.
            }
        }
    }
}
