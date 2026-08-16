using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties.UI
{
    /// <summary>
    /// One-stop "R&amp;S" button in the world-view play-settings row (the bottom-right icon strip):
    /// opens a menu that toggles the region-border overlay and switches directly to the Territories /
    /// Dwellings map modes. The frameworks bury these controls — NozoMe's MMF keeps the border toggle
    /// inside its Draw Settings panel, and RP2's fork deleted that panel entirely
    /// (Patch_MapModeUI_DrawSettings self-skips there) while filing our map modes away under its
    /// "Mods" tab — so under RP2 this button is the ONLY border toggle, and under both frameworks it
    /// is the discoverable one. The row is vanilla, so the same control ships in both editions.
    /// </summary>
    [StaticConstructorOnStartup]
    [HarmonyPatch(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls))]
    public static class Patch_PlaySettings_RSOverlays
    {
        private static readonly Texture2D Icon =
            ContentFinder<Texture2D>.Get("UI/MapModes/SynapseGroup", false) ?? BaseContent.BadTex;

        [HarmonyPostfix]
        public static void Postfix(WidgetRow row, bool worldView)
        {
            // Without a map-mode framework there are no overlays to control; also keeps every
            // MapModeFramework type out of the JIT path (the framework-touching code sits behind
            // NoInlining below, so this guard is what decides whether it is ever compiled).
            if (!worldView || row == null || !MapFrameworkGate.Present) return;

            if (row.ButtonIcon(Icon, "Regions and Societies: toggle overlays and map modes"))
            {
                var options = new List<FloatMenuOption>
                {
                    new FloatMenuOption(
                        RegionBorderOverlay.Enabled ? "Region borders: on (click to hide)"
                                                    : "Region borders: off (click to show)",
                        () => RegionBorderOverlay.Enabled = !RegionBorderOverlay.Enabled)
                };
                AddMapModeSwitches(options);
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AddMapModeSwitches(List<FloatMenuOption> options)
        {
            var component = MapModeFramework.MapModeComponent.Instance;
            if (component?.mapModes == null) return;

            foreach (string defName in new[] { "SynapseFactionTerritory", "SynapsePopulationDensity" })
            {
                foreach (var mode in component.mapModes)
                {
                    if (mode?.def?.defName != defName) continue;
                    bool current = component.currentMapMode == mode;
                    string label = current ? $"{mode.def.LabelCap} (current)" : mode.def.LabelCap.ToString();
                    var target = mode;
                    options.Add(new FloatMenuOption(label, () => component.RequestMapModeSwitch(target)));
                    break;
                }
            }
        }
    }
}
