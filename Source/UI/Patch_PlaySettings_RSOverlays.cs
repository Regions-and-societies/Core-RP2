using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties.UI
{
    /// <summary>
    /// R&amp;S controls in the world-view play-settings row (the bottom-right icon strip): individual
    /// toggleable icons for the region-border overlay and the Territories / Dwellings map modes — no
    /// submenu, one click each, exactly like the vanilla roof/zone toggles. The frameworks bury these
    /// controls — NozoMe's MMF keeps the border toggle inside its Draw Settings panel, and RP2's fork
    /// deleted that panel entirely (Patch_MapModeUI_DrawSettings self-skips there) while filing our
    /// map modes away under its "Mods" tab — so under RP2 these icons are the ONLY border toggle, and
    /// under both frameworks they are the discoverable one. The row is vanilla, so the same controls
    /// ship in both editions. Toggling a mode off returns to the framework's Default map mode.
    /// </summary>
    [StaticConstructorOnStartup]
    [HarmonyPatch(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls))]
    public static class Patch_PlaySettings_RSOverlays
    {
        private static readonly Texture2D BordersIcon =
            ContentFinder<Texture2D>.Get("UI/PlaySettings/RS_Borders", false) ?? BaseContent.BadTex;
        private static readonly Texture2D TerritoriesIcon =
            ContentFinder<Texture2D>.Get("UI/PlaySettings/RS_Territories", false) ?? BaseContent.BadTex;
        private static readonly Texture2D DwellingsIcon =
            ContentFinder<Texture2D>.Get("UI/PlaySettings/RS_Dwellings", false) ?? BaseContent.BadTex;

        [HarmonyPostfix]
        public static void Postfix(WidgetRow row, bool worldView)
        {
            // Without a map-mode framework there are no overlays to control; also keeps every
            // MapModeFramework type out of the JIT path (the framework-touching code sits behind
            // NoInlining below, so this guard is what decides whether it is ever compiled).
            if (!worldView || row == null || !MapFrameworkGate.Present) return;

            bool borders = RegionBorderOverlay.Enabled;
            row.ToggleableIcon(ref borders, BordersIcon,
                "Regions and Societies: show region borders on top of any map mode.");
            RegionBorderOverlay.Enabled = borders;

            DoMapModeToggle(row, "SynapseFactionTerritory", TerritoriesIcon,
                "Regions and Societies: Territories map mode (faction shading).");
            DoMapModeToggle(row, "SynapsePopulationDensity", DwellingsIcon,
                "Regions and Societies: Dwellings map mode (population density).");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DoMapModeToggle(WidgetRow row, string defName, Texture2D icon, string tooltip)
        {
            var component = MapModeFramework.MapModeComponent.Instance;
            if (component?.mapModes == null) return;

            MapModeFramework.MapMode mode = null;
            MapModeFramework.MapMode fallback = null;
            foreach (var m in component.mapModes)
            {
                if (m?.def == null) continue;
                if (m.def.defName == defName) mode = m;
                else if (m.def.defName == "Default") fallback = m;
            }
            if (mode == null) return;

            bool active = component.currentMapMode == mode;
            bool toggled = active;
            row.ToggleableIcon(ref toggled, icon, tooltip);
            if (toggled == active) return;

            if (toggled)
            {
                component.RequestMapModeSwitch(mode);
            }
            else if (fallback != null)
            {
                component.RequestMapModeSwitch(fallback);
            }
        }
    }
}
