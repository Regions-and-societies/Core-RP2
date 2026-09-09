using System;
using System.Reflection;

namespace RegionsAndSocieties.Integration
{
    /// <summary>
    /// Reflective reads of Realistic Planets 2's world-shaping settings, used to correct the
    /// PRE-GENERATION region-count estimate (#54 RP2 tail). RP2 is never hard-referenced — the mod
    /// builds and runs against Map Mode Framework alone — so every RP2 type/field is looked up by
    /// name through plain <see cref="System.Reflection"/> (no HarmonyLib, so this file also compiles in
    /// the pure test suites), cached once, and every accessor no-ops cleanly when RP2 is absent (the
    /// standard edition). Nothing here runs per-tick; it is read only when the placement dialog draws
    /// its estimate on the world-creation screen (and set only by the dev calibration sweep).
    ///
    /// <para>RP2 keeps the player's world-shaping choices on static fields of
    /// <c>Planets.Core.Planets_GameComponent</c>, set live from its create-world page:
    /// <c>seaLevel</c> (enum <c>Planets.WorldGen.SeaLevel</c>: Low, SlightlyLow, Normal, SlightlyHigh,
    /// High — higher = more ocean = less land) and <c>subcount</c> (the icosahedron subdivision level,
    /// shown as "Planet Scale", ~5..11; the world's tile count multiplies by ~3 per step, replacing
    /// vanilla coverage as the size knob). The pure fraction/size math lives in
    /// <see cref="RegionsAndSocieties.Placement.PlacementEstimates"/>.</para>
    /// </summary>
    public static class RealisticPlanetsProbe
    {
        private const string GameComponentTypeName = "Planets.Core.Planets_GameComponent";

        private static bool resolved;
        private static FieldInfo seaLevelField;   // static Planets.WorldGen.SeaLevel Planets_GameComponent.seaLevel
        private static FieldInfo subcountField;   // static int Planets_GameComponent.subcount
        private static Type seaLevelEnumType;

        private const BindingFlags StaticAny = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>Find a type by full name across every loaded assembly (RP2 lives in its own), or null.</summary>
        private static Type FindType(string fullName)
        {
            Type t = Type.GetType(fullName);
            if (t != null) return t;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { t = asm.GetType(fullName); }
                catch (Exception) { t = null; }
                if (t != null) return t;
            }
            return null;
        }

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;
            Type gc = FindType(GameComponentTypeName);
            if (gc == null) return;   // RP2 not loaded (standard edition) — stays a clean no-op
            seaLevelField = gc.GetField("seaLevel", StaticAny);
            subcountField = gc.GetField("subcount", StaticAny);
            if (seaLevelField != null) seaLevelEnumType = seaLevelField.FieldType;
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
        /// RP2's planet-scale subdivision count (<c>subcount</c>, ~5..11; the world's tile count multiplies
        /// by ~3 per step). Returns -1 when RP2 is absent or the field could not be read, so the caller
        /// falls back to the vanilla coverage-only tile curve.
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

        // ---- Dev-only setters, for the #54 calibration sweep (never called in normal play) ----------
        // RP2's RegisterPlanetLayer prefix reads Planets_GameComponent.subcount at world-gen time and
        // forces the Surface subdivisions to it, so setting the field before generation is enough — no
        // need to touch PlanetLayerSettingsDefOf here. seaLevel is likewise read during ocean/elevation
        // generation. Both are set by the -quicktest worldgen override so a scripted run can measure the
        // tile count / land fraction at any Planet Scale and sea level.

        /// <summary>Set RP2's Planet Scale (subcount / subdivisions). No-op without RP2. Returns success.</summary>
        public static bool TrySetSubcount(int subcount)
        {
            Resolve();
            if (subcountField == null) return false;
            try { subcountField.SetValue(null, subcount); return true; }
            catch (Exception) { return false; }
        }

        /// <summary>Set RP2's sea level by its 0..4 ordinal (Low..High). No-op without RP2. Returns success.</summary>
        public static bool TrySetSeaLevelOrdinal(int ordinal)
        {
            Resolve();
            if (seaLevelField == null || seaLevelEnumType == null) return false;
            try { seaLevelField.SetValue(null, Enum.ToObject(seaLevelEnumType, ordinal)); return true; }
            catch (Exception) { return false; }
        }
    }
}
