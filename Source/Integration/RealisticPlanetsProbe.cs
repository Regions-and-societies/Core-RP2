using System;
using System.Reflection;
using HarmonyLib;

namespace RegionsAndSocieties.Integration
{
    /// <summary>
    /// Reflective reads of Realistic Planets 2's world-shaping settings, used to correct the
    /// PRE-GENERATION region-count estimate (#54 RP2 tail). RP2 is never hard-referenced — the mod
    /// builds and runs against Map Mode Framework alone — so every RP2 type/field is looked up by
    /// name through <see cref="AccessTools"/>, cached once, and every accessor no-ops cleanly when
    /// RP2 is absent (the standard edition). Nothing here runs per-tick; it is read only when the
    /// placement dialog draws its estimate on the world-creation screen.
    ///
    /// <para>RP2 keeps the player's world-shaping choices on static fields of
    /// <c>Planets.Core.Planets_GameComponent</c>, set live from its create-world page:
    /// <c>seaLevel</c> (enum <c>Planets.SeaLevel</c>: Low, SlightlyLow, Normal, SlightlyHigh, High —
    /// higher = more ocean = less land) and <c>subcount</c> (the icosahedron subdivision level, shown
    /// as "Planet Scale", ~5..11; it drives the world's tile count, replacing vanilla coverage as the
    /// size knob). Both are read here; the pure fraction/size math lives in
    /// <see cref="RegionsAndSocieties.Placement.PlacementEstimates"/>.</para>
    /// </summary>
    public static class RealisticPlanetsProbe
    {
        private const string GameComponentTypeName = "Planets.Core.Planets_GameComponent";

        private static bool resolved;
        private static FieldInfo seaLevelField;   // static Planets.SeaLevel Planets_GameComponent.seaLevel
        private static FieldInfo subcountField;   // static int Planets_GameComponent.subcount

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;
            Type gc = AccessTools.TypeByName(GameComponentTypeName);
            if (gc == null) return;   // RP2 not loaded (standard edition) — stays a clean no-op
            seaLevelField = AccessTools.Field(gc, "seaLevel");
            subcountField = AccessTools.Field(gc, "subcount");
        }

        /// <summary>True when Realistic Planets 2 is present and its settings component was found.</summary>
        public static bool IsActive
        {
            get { Resolve(); return seaLevelField != null || subcountField != null; }
        }

        /// <summary>
        /// RP2's current sea level as a 0-based ordinal over the <c>SeaLevel</c> enum (0 = Low, up to
        /// 4 = High; 2 = Normal is the vanilla-like middle). Returns -1 when RP2 is absent or the field
        /// could not be read, so the caller falls back to the flat land fraction.
        /// </summary>
        public static int TryGetSeaLevelOrdinal()
        {
            Resolve();
            if (seaLevelField == null) return -1;
            try
            {
                object v = seaLevelField.GetValue(null);   // static field
                if (v == null) return -1;
                return Convert.ToInt32(v);   // enum -> its underlying ordinal
            }
            catch (Exception) { return -1; }
        }

        /// <summary>
        /// RP2's planet-scale subdivision count (<c>subcount</c>, ~5..11; the world's tile count grows
        /// with it). Returns -1 when RP2 is absent or the field could not be read, so the caller falls
        /// back to the vanilla coverage-only tile curve.
        /// </summary>
        public static int TryGetSubcount()
        {
            Resolve();
            if (subcountField == null) return -1;
            try
            {
                object v = subcountField.GetValue(null);   // static field
                return v is int ? (int)v : -1;
            }
            catch (Exception) { return -1; }
        }
    }
}
