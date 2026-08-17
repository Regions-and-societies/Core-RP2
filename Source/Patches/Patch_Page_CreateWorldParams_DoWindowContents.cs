using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties.Patches
{
    /// <summary>
    /// Adds the "Faction Geography" button to the vanilla Create-World page, opening R&amp;T's placement
    /// settings dialog. See <see cref="Patch_Page_CreateWorldParamsRP_DoWindowContents"/> for the
    /// Realistic Planets 2 variant.
    /// </summary>
    [HarmonyPatch(typeof(Page_CreateWorldParams), "DoWindowContents")]
    public static class Patch_Page_CreateWorldParams_DoWindowContents
    {
        public static void Postfix(Rect rect) => DrawFactionGeographyButton(rect);

        /// <summary>Draw R&amp;T's faction-geography button and open its settings dialog on click. Shared by
        /// the vanilla and Realistic Planets 2 create-world pages so both menus expose the same control.</summary>
        internal static void DrawFactionGeographyButton(Rect rect)
        {
            Rect buttonRect = new Rect(rect.xMax - 320f, rect.yMax - 38f, 150f, 38f);
            if (Widgets.ButtonText(buttonRect, "Faction Geography"))
            {
                Find.WindowStack.Add(new Dialog_FactionPlacementSettings());
            }
        }
    }

    /// <summary>
    /// #81: Realistic Planets 2 replaces the vanilla Create-World page with its own
    /// <c>Planets.UI.Pages.Page_CreateWorldParamsRP</c> (a subclass whose <c>DoWindowContents</c> override
    /// does NOT call base), so the vanilla patch above never fires under RP2 and R&amp;T's faction-geography
    /// button disappears from the world-gen menu. Patch RP2's override too — reflectively (R&amp;T holds no
    /// reference to RP2) and gated on RP2 being present, so this is a clean no-op without it. The dialog's
    /// own page lookup already works because RP2's page derives from <c>Page_CreateWorldParams</c>.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_Page_CreateWorldParamsRP_DoWindowContents
    {
        private static MethodBase RP2PageDoWindowContents() =>
            AccessTools.Method(AccessTools.TypeByName("Planets.UI.Pages.Page_CreateWorldParamsRP"), "DoWindowContents");

        private static bool Prepare() => RP2PageDoWindowContents() != null;
        private static MethodBase TargetMethod() => RP2PageDoWindowContents();

        public static void Postfix(Rect rect) => Patch_Page_CreateWorldParams_DoWindowContents.DrawFactionGeographyButton(rect);
    }
}
