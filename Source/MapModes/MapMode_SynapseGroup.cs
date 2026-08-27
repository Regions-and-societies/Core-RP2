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

            // The Regions and Societies section lists the analytic views: Territories (faction shading),
            // Population/dwellings, and Age structure. Region division lines are a global overlay toggle
            // in the map-mode Draw Settings (see Patch_MapModeUI_RegionBorders), not a mode of their
            // own, so they can be shown on top of any map mode.
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

            // Age structure: regions shaded by median age (#10). Sits alongside dwellings as another
            // read of the same deterministic demographic model.
            var ageMode = MapModeComponent.Instance.mapModes.FirstOrDefault(m => m.def.defName == "SynapseAgeStructure");
            if (ageMode != null)
            {
                options.Add(new FloatMenuOption(ageMode.def.LabelCap, () => MapModeComponent.Instance.RequestMapModeSwitch(ageMode)));
            }

            // Sex ratio: regions shaded by sex balance (#11), revealing draft/war skews.
            var sexMode = MapModeComponent.Instance.mapModes.FirstOrDefault(m => m.def.defName == "SynapseSexRatio");
            if (sexMode != null)
            {
                options.Add(new FloatMenuOption(sexMode.def.LabelCap, () => MapModeComponent.Instance.RequestMapModeSwitch(sexMode)));
            }

            // Xenotypes: regions tinted by dominant caste (#12). Only meaningful with Biotech.
            var xenoMode = MapModeComponent.Instance.mapModes.FirstOrDefault(m => m.def.defName == "SynapseXenotype");
            if (xenoMode != null)
            {
                options.Add(new FloatMenuOption(xenoMode.def.LabelCap, () => MapModeComponent.Instance.RequestMapModeSwitch(xenoMode)));
            }

            // Education: regions shaded by education index (#15).
            var eduMode = MapModeComponent.Instance.mapModes.FirstOrDefault(m => m.def.defName == "SynapseEducation");
            if (eduMode != null)
            {
                options.Add(new FloatMenuOption(eduMode.def.LabelCap, () => MapModeComponent.Instance.RequestMapModeSwitch(eduMode)));
            }

            // Wealth: regions shaded by socioeconomic index (#14).
            var wealthMode = MapModeComponent.Instance.mapModes.FirstOrDefault(m => m.def.defName == "SynapseWealth");
            if (wealthMode != null)
            {
                options.Add(new FloatMenuOption(wealthMode.def.LabelCap, () => MapModeComponent.Instance.RequestMapModeSwitch(wealthMode)));
            }

            // Ideology: regions tinted by dominant ideo (#13). Only meaningful with the Ideology DLC.
            var ideoMode = MapModeComponent.Instance.mapModes.FirstOrDefault(m => m.def.defName == "SynapseIdeology");
            if (ideoMode != null)
            {
                options.Add(new FloatMenuOption(ideoMode.def.LabelCap, () => MapModeComponent.Instance.RequestMapModeSwitch(ideoMode)));
            }

            // Employment: regions tinted by dominant occupation sector (#16).
            var employMode = MapModeComponent.Instance.mapModes.FirstOrDefault(m => m.def.defName == "SynapseEmployment");
            if (employMode != null)
            {
                options.Add(new FloatMenuOption(employMode.def.LabelCap, () => MapModeComponent.Instance.RequestMapModeSwitch(employMode)));
            }

            if (options.Any())
            {
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }
    }
}
