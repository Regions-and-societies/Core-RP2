using System.Collections.Generic;
using System.Linq;
using MapModeFramework;
using RimWorld;
using Verse;

namespace RegionsAndSocieties
{
    public class MapMode_SynapseGroup : MapMode
    {
        public override WorldLayer_MapMode WorldLayer => null;

        public MapMode_SynapseGroup() { }
        public MapMode_SynapseGroup(MapModeDef def) : base(def) { }

        public override void OnButtonClick()
        {
            if (MapModeComponent.Instance == null) return;

            List<FloatMenuOption> options = new List<FloatMenuOption>();

            // The Regions and Societies section is exactly two views: Territories (faction shading) and
            // Population/dwellings. Region division lines are a global overlay toggle in the map-mode
            // Draw Settings (see Patch_MapModeUI_RegionBorders), not a mode of their own, so they can
            // be shown on top of any map mode.
            var territoryMode = MapModeComponent.Instance.mapModes.FirstOrDefault(m => m.def.defName == "SynapseFactionTerritory");
            if (territoryMode != null)
            {
                options.Add(new FloatMenuOption(territoryMode.def.LabelCap, () => MapModeComponent.Instance.RequestMapModeSwitch(territoryMode)));
            }

            var popMode = MapModeComponent.Instance.mapModes.FirstOrDefault(m => m.def.defName == "SynapsePopulationDensity");
            if (popMode != null)
            {
                options.Add(new FloatMenuOption(popMode.def.LabelCap, () => MapModeComponent.Instance.RequestMapModeSwitch(popMode)));
            }

            if (options.Any())
            {
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }
    }
}
