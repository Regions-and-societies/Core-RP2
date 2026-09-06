using System;
using System.Collections.Generic;
using Verse;

namespace RegionsAndSocieties
{
    /// <summary>
    /// Curated default placement profiles, keyed by FactionDef defName (#55). Core registers its own
    /// entries (the Empire order-2 case that used to be a string compare); a compatibility patch
    /// registers profiles for its factions from its Mod constructor, which runs after core's because
    /// patches loadAfter core and before any world generation. Consulted by
    /// <see cref="FactionPlacementSettings.GetDefaultProfile"/> before the tech-level fallback.
    ///
    /// <para>Later registrations for the same defName win, so a patch loaded later can refine one loaded
    /// earlier. A profile the user has already saved in settings is never overridden — the settings
    /// store only asks here when it has no saved profile for a faction. Registrations are static for the
    /// process lifetime; there is no invalidation.</para>
    ///
    /// <para>Pure: dictionary + arithmetic, no Find, no Harmony — so it compiles against the test stubs and
    /// the tech-level fallback (<see cref="TechLevelDefault"/>) is unit-tested in the placement suite.</para>
    /// </summary>
    public static class FactionPlacementDefaults
    {
        // RimWorld's TechLevel ordinals, spelled out so this file needs no game enum:
        // Undefined 0, Animal 1, Neolithic 2, Medieval 3, Industrial 4, Spacer 5, Ultra 6, Archotech 7.
        public const int TechIndustrial = 4;
        public const int TechSpacer = 5;
        public const int TechUltra = 6;

        private static readonly Dictionary<string, FactionPlacementProfile> registered =
            new Dictionary<string, FactionPlacementProfile>(StringComparer.Ordinal);

        static FactionPlacementDefaults()
        {
            RegisterCoreDefaults();
        }

        /// <summary>
        /// Register (or replace) the default profile for a faction. The profile is copied, so the caller
        /// may keep editing its instance; <paramref name="factionDefName"/> is stamped onto the copy.
        /// Null or empty names and null profiles are ignored.
        /// </summary>
        public static void Register(string factionDefName, FactionPlacementProfile profile)
        {
            if (string.IsNullOrEmpty(factionDefName) || profile == null) return;
            FactionPlacementProfile copy = profile.Clone();
            copy.factionDefName = factionDefName;
            bool replaced = registered.ContainsKey(factionDefName);
            registered[factionDefName] = copy;
            Log.Message("[RegionsAndSocieties] Placement profile " + (replaced ? "replaced" : "registered") + " for faction '" + factionDefName + "'"
                + " (order " + copy.placementOrder + ", holdings " + copy.baseCountRange.min + "-" + copy.baseCountRange.max + ").");
        }

        /// <summary>The registered default for a faction, as a fresh copy, or false if none is registered.</summary>
        public static bool TryGet(string factionDefName, out FactionPlacementProfile profile)
        {
            profile = null;
            if (string.IsNullOrEmpty(factionDefName)) return false;
            if (!registered.TryGetValue(factionDefName, out FactionPlacementProfile stored)) return false;
            profile = stored.Clone();
            return true;
        }

        public static bool IsRegistered(string factionDefName)
        {
            return !string.IsNullOrEmpty(factionDefName) && registered.ContainsKey(factionDefName);
        }

        /// <summary>Every registered defName, for the debug report.</summary>
        public static IEnumerable<string> RegisteredDefNames
        {
            get { return registered.Keys; }
        }

        /// <summary>Test-only: drop every registration and put core's own entries back.</summary>
        public static void Reset()
        {
            registered.Clear();
            RegisterCoreDefaults();
        }

        /// <summary>
        /// The profile a faction gets when nothing is registered for it: weights from its tech level, a
        /// raider margin and a smaller holding count when it is hostile, and the placement order
        /// (Industrial first, then registered specials such as Empire, then spacer, then tribal).
        /// <paramref name="techLevel"/> is RimWorld's TechLevel ordinal; <paramref name="hostile"/> is
        /// <c>hostileToFactionlessHumanlikes || permanentEnemy</c>.
        /// </summary>
        public static FactionPlacementProfile TechLevelDefault(string factionDefName, int techLevel, bool hostile)
        {
            float mineral, nutrition, forage, grazing, hunting, margin;
            int minB = 5;
            int maxB = 15;

            if (techLevel >= TechSpacer)
            {
                mineral = 2.5f;
                nutrition = 0.5f;
                forage = 0.1f;
                grazing = 0.1f;
                hunting = 0.2f;
                margin = 0.0f;
            }
            else if (techLevel == TechIndustrial)
            {
                mineral = 1.0f;
                nutrition = 2.0f;
                forage = 0.2f;
                grazing = 0.8f;
                hunting = 0.8f;
                margin = 0.0f;
            }
            else
            {
                mineral = 0.2f;
                nutrition = 0.2f;
                forage = 2.0f;
                if (hostile)
                {
                    grazing = 0.2f;
                    hunting = 2.0f;
                }
                else
                {
                    grazing = 2.0f;
                    hunting = 0.2f;
                }
                margin = 0.1f;
            }

            int order;
            if (techLevel == TechIndustrial) order = 1;
            else if (techLevel >= TechSpacer) order = 3;
            else order = 4;

            if (hostile)
            {
                margin = 2.5f;
                minB = 3;
                maxB = 8;
            }

            return new FactionPlacementProfile(factionDefName, mineral, nutrition, forage, grazing, hunting, margin, minB, maxB, order);
        }

        /// <summary>
        /// Core's own table, expressed the way a patch's is. Empire (Royalty, Ultra tech, not hostile) is
        /// the spacer default with placement order 2 — it lands after the industrial unions and before
        /// the other spacer factions, which the old code did with a defName compare.
        /// </summary>
        private static void RegisterCoreDefaults()
        {
            FactionPlacementProfile empire = TechLevelDefault("Empire", TechUltra, false);
            empire.placementOrder = 2;
            registered["Empire"] = empire;
        }
    }
}
