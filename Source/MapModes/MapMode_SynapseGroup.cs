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

            // Residences: population resolved into homes by urbanization (0.3.0). Population is people;
            // residences are where they live — rural extended-family homesteads to dense urban households.
            var residenceMode = MapModeComponent.Instance.mapModes.FirstOrDefault(m => m.def.defName == "SynapseResidence");
            if (residenceMode != null)
            {
                options.Add(new FloatMenuOption(residenceMode.def.LabelCap, () => MapModeComponent.Instance.RequestMapModeSwitch(residenceMode)));
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

            // Xenotypes: regions tinted by dominant caste (#12). Offered only with Biotech — without the
            // DLC every pawn is Baseliner, so the overlay would be a flat, meaningless wash. The def stays
            // loaded (so a save that had it selected still resolves); it is simply not listed here.
            var xenoMode = MapModeComponent.Instance.mapModes.FirstOrDefault(m => m.def.defName == "SynapseXenotype");
            if (xenoMode != null && ModsConfig.BiotechActive)
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

            // Ideology: regions tinted by dominant ideo (#13). Offered only with the Ideology DLC — without
            // it every region is secular, so the overlay is a flat wash. The def stays loaded (save-safe);
            // it is simply not listed here.
            var ideoMode = MapModeComponent.Instance.mapModes.FirstOrDefault(m => m.def.defName == "SynapseIdeology");
            if (ideoMode != null && ModsConfig.IdeologyActive)
            {
                options.Add(new FloatMenuOption(ideoMode.def.LabelCap, () => MapModeComponent.Instance.RequestMapModeSwitch(ideoMode)));
            }

            // Employment: regions tinted by dominant occupation sector (#16).
            var employMode = MapModeComponent.Instance.mapModes.FirstOrDefault(m => m.def.defName == "SynapseEmployment");
            if (employMode != null)
            {
                options.Add(new FloatMenuOption(employMode.def.LabelCap, () => MapModeComponent.Instance.RequestMapModeSwitch(employMode)));
            }

            // Biomes & walls: the terrain/partition debug overlay (#20).
            var barriersMode = MapModeComponent.Instance.mapModes.FirstOrDefault(m => m.def.defName == "SynapseNaturalBarriers");
            if (barriersMode != null)
            {
                options.Add(new FloatMenuOption(barriersMode.def.LabelCap, () => MapModeComponent.Instance.RequestMapModeSwitch(barriersMode)));
            }

            if (options.Any())
            {
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }
    }
}
