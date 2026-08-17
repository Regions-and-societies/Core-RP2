using System.Reflection;
using RimWorld;
using Verse;

namespace RegionsAndSocieties
{
    /// <summary>
    /// Either-or gate for the Map Mode Framework capability (#81). R&amp;T's world overlays are built on
    /// Map Mode Framework, but that capability is satisfied by EITHER NozoMe's original mod
    /// (packageId <c>NozoMe.MapModeFramework</c>) OR Realistic Planets 2 (<c>koth.RealisticPlanets2</c>),
    /// which bundles a fork behind a type-forwarding <c>MapModeFramework.dll</c> shim and forbids the
    /// original via <c>incompatibleWith</c>.
    ///
    /// <para>Detection is by TYPE, not packageId: both the original and RP2's shim expose
    /// <c>MapModeFramework.MapModeComponent</c>, so a type probe is authoritative where a packageId list
    /// would rot as new forks appear. This file deliberately never names a MapModeFramework type in code
    /// (only as a string), so it loads cleanly even when neither framework is present.</para>
    /// </summary>
    [StaticConstructorOnStartup]
    public static class MapFrameworkGate
    {
        /// <summary>True when a Map Mode Framework implementation is loaded (original mod or a fork's shim).</summary>
        public static bool Present => GenTypes.GetTypeInAnyAssembly("MapModeFramework.MapModeComponent") != null;

        static MapFrameworkGate()
        {
            // Startup diagnostic (#81). Records which framework is providing the overlay capability and
            // whether the fork-sensitive method exists, straight into Player.log — so the either-or path
            // is verifiable from the log alone, without the in-game debug bridge. Never red: a Message.
            bool nozome = ModsConfig.IsActive("NozoMe.MapModeFramework");
            bool rp2 = ModsConfig.IsActive("koth.RealisticPlanets2");
            bool present = Present;

            System.Type uiType = GenTypes.GetTypeInAnyAssembly("MapModeFramework.MapModeUI");
            bool drawSettingsExpanded = uiType != null &&
                uiType.GetMethod("DoDrawSettingsExpanded", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) != null;

            string provider = nozome ? "NozoMe (original)"
                : (rp2 ? "Realistic Planets 2 (forked shim)"
                : (present ? "unknown fork" : "NONE"));

            Log.Message($"[RegionsAndSocieties] Map-framework gate (#81): provider={provider} " +
                        $"frameworkPresent={present} DoDrawSettingsExpanded={drawSettingsExpanded} " +
                        $"(under RP2, DoDrawSettingsExpanded=false is expected — the border-toggle patch self-skips via Prepare()).");

            if (!present && !FactionPlacementSettings.mapFrameworkWarningDismissed)
            {
                // Deferred so the WindowStack is up when it shows. Once per launch, and permanently
                // dismissible — the either-or warning the vanilla dependency system can't express (#81).
                LongEventHandler.QueueLongEvent(ShowMissingFrameworkDialog,
                    "RegionsAndSocieties_MapFrameworkWarning", false, null);
            }
        }

        private static void ShowMissingFrameworkDialog()
        {
            if (Find.WindowStack == null) return;
            Find.WindowStack.Add(new Dialog_MessageBox(
                "Regions and Societies needs a map-mode framework for its world overlays: " +
                "either Map Mode Framework or Realistic Planets 2. Neither is active, so the province, " +
                "territory, and population overlays are disabled. The rest of the mod still works — " +
                "install either framework to turn the overlays on.",
                buttonAText: "OK",
                buttonBText: "Don't show this again",
                buttonBAction: () =>
                {
                    FactionPlacementSettings.mapFrameworkWarningDismissed = true;
                    RegionsAndSocietiesMod.Settings?.Write();
                }));
        }
    }
}
