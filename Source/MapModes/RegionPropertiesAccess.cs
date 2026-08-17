using System.Reflection;
using HarmonyLib;
using MapModeFramework;
using Verse;

namespace RegionsAndSocieties
{
    /// <summary>
    /// #81: reads <c>MapModeFramework.MapModeDef.RegionProperties</c> reflectively so R&amp;T's assembly
    /// carries NO direct TypeRef to <c>MapModeFramework.RegionProperties</c>.
    /// <para>
    /// Realistic Planets 2 supplies the Map Mode Framework types through a type-forwarding shim
    /// (<c>MapModeFramework.dll</c> → <c>Realistic_Planets_2.dll</c>). That shim forwards every MMF type
    /// R&amp;T uses EXCEPT <c>RegionProperties</c> — so a direct reference resolves fine against NozoMe's
    /// original framework but throws <see cref="System.TypeLoadException"/> under RP2 the moment the
    /// referencing method (e.g. <c>MapMode_FactionTerritory.SetRegions</c> or the world-click postfix)
    /// is JIT-compiled. Going through reflection here sidesteps the missing forward and behaves
    /// identically on both frameworks. <c>MapModeDef</c> itself IS forwarded, so <c>typeof(MapModeDef)</c>
    /// is safe; the returned value is handled as <see cref="object"/> and its fields are read by name.
    /// </para>
    /// The XML <c>&lt;RegionProperties&gt;</c> block on the def is kept — the framework side still reads it —
    /// this only removes the reference from R&amp;T's own compiled IL. Field defaults match that XML.
    /// </summary>
    internal static class RegionPropertiesAccess
    {
        private static readonly FieldInfo RegionPropsField =
            AccessTools.Field(typeof(MapModeDef), "RegionProperties");

        private static object Props(MapModeDef def) =>
            def == null ? null : RegionPropsField?.GetValue(def);

        internal static bool OverrideSelector(MapModeDef def) =>
            GetBool(Props(def), "overrideSelector", false);

        internal static bool DoBorders(MapModeDef def) =>
            GetBool(Props(def), "doBorders", true);

        internal static float BorderWidth(MapModeDef def) =>
            GetFloat(Props(def), "borderWidth", 0.7f);

        private static bool GetBool(object props, string field, bool fallback)
        {
            if (props == null) return fallback;
            FieldInfo f = AccessTools.Field(props.GetType(), field);
            return f != null ? (bool)f.GetValue(props) : fallback;
        }

        private static float GetFloat(object props, string field, float fallback)
        {
            if (props == null) return fallback;
            FieldInfo f = AccessTools.Field(props.GetType(), field);
            return f != null ? (float)f.GetValue(props) : fallback;
        }
    }
}
