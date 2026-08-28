using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RegionsAndSocieties.Patches
{
    /// <summary>
    /// Worldgen-survival guard for classic-ideoligion role naming. Vanilla's
    /// <c>IdeoGenerator.GenerateClassicIdeo</c> only creates <c>ideo.foundation</c> when the
    /// Ideology DLC is active, but then adds EVERY <c>PreceptDef</c> marked <c>classic</c> in the
    /// DefDatabase — including leader-role precepts contributed by other mods' content. Naming a
    /// leader role calls <c>ideo.foundation.GenerateLeaderTitle()</c>, so on a game without
    /// Ideology that combination NREs — and because it fires per faction during
    /// <c>WorldGenStep_Factions</c> AND for the player faction in
    /// <c>ScenPart_PlayerFaction.PostWorldGenerate</c>, the whole world generation dies and the
    /// player is left staring at an unrendered ("black") world. Our own faction generation already
    /// guards its calls (0.2.2), but the player-faction path is vanilla's — only patching the
    /// naming method itself heals every caller.
    ///
    /// <para>Two layers: a Prefix that detects the KNOWN null (leader role, no foundation) and
    /// substitutes a fallback title without entering the vanilla body, and a Finalizer that
    /// converts ANY other throw inside role naming into the same fallback — a mod's role precept
    /// with broken naming data should cost that role its fancy title, never the whole world.
    /// The fallback is a constant, which is safe: <c>Precept.GenerateNewName</c>'s uniqueness
    /// loop is capped at 50 iterations.</para>
    /// </summary>
    [HarmonyPatch(typeof(Precept_Role), nameof(Precept_Role.GenerateNameRaw))]
    public static class Patch_Precept_Role_GenerateNameRaw
    {
        private static bool warnedFoundation;
        private static bool warnedThrow;

        [HarmonyPrefix]
        public static bool Prefix(Precept_Role __instance, ref string __result)
        {
            if (__instance?.def == null || !__instance.def.leaderRole)
            {
                return true;
            }
            Ideo ideo = __instance.ideo;
            if (ideo != null && ideo.foundation != null)
            {
                return true;   // vanilla path is safe — foundation exists to generate the title
            }

            __result = FallbackName(__instance);
            if (ideo != null)
            {
                // Vanilla's leader branch also fills the ideo's leader titles via the foundation;
                // mirror that with the fallback so downstream readers never see an empty title.
                if (ideo.leaderTitleMale.NullOrEmpty()) ideo.leaderTitleMale = __result;
                if (ideo.leaderTitleFemale.NullOrEmpty()) ideo.leaderTitleFemale = __result;
            }
            if (!warnedFoundation)
            {
                warnedFoundation = true;
                Log.Warning("[RegionsAndSocieties] Classic ideoligion has no foundation (Ideology " +
                            "DLC inactive) but a leader-role precept was added — likely by another " +
                            $"mod's classic PreceptDef. Using fallback role title '{__result}' so " +
                            "world generation can continue. (Warned once.)");
            }
            return false;
        }

        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception, Precept_Role __instance, ref string __result)
        {
            if (__exception == null)
            {
                return null;
            }
            __result = FallbackName(__instance);
            if (!warnedThrow)
            {
                warnedThrow = true;
                Log.Warning("[RegionsAndSocieties] Role-name generation threw for precept " +
                            $"'{__instance?.def?.defName ?? "null"}' — using fallback title " +
                            $"'{__result}' so world generation can continue. (Warned once.) " +
                            $"Original error: {__exception}");
            }
            return null;   // swallowed: a broken role title must never kill worldgen
        }

        private static string FallbackName(Precept_Role p)
        {
            string label = p?.def?.label;
            return GenText.CapitalizeAsTitle(label.NullOrEmpty() ? "Leader" : label);
        }
    }
}
