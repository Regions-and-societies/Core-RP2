using System;
using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace RegionsAndSocieties.Integration
{
    /// <summary>
    /// The single place Regions &amp; Territories asks "what kind of thing is this world object?".
    ///
    /// Resolution order:
    ///   1. Registered adapters, in priority order (mod-specific knowledge wins).
    ///   2. Generic name heuristics, so an unknown mod's objects still get governed.
    ///   3. Unknown — logged once per type so an unrecognised mod is easy to add a profile for.
    ///
    /// Replaces the ad-hoc <c>GetType().Name.Contains("Outpost")</c> checks that were scattered
    /// across placement, ownership, and population code.
    /// </summary>
    public static class WorldObjectClassifier
    {
        private static readonly HashSet<Type> loggedUnknownTypes = new HashSet<Type>();

        /// <summary>Called when adapters change so stale reflection lookups are dropped.</summary>
        public static void InvalidateCache()
        {
            loggedUnknownTypes.Clear();
            ReflectionHelper.ClearCaches();
        }

        public static WorldObjectKind Classify(WorldObject obj)
        {
            if (obj == null) return WorldObjectKind.Unknown;

            WorldObjectKind kind;
            if (WorldObjectAdapterRegistry.TryClassify(obj, out kind)) return kind;

            return ClassifyUnrecognised(obj);
        }

        /// <summary>
        /// What to do with an object no active adapter claimed. Two opt-out gates come first, so a
        /// switched-off integration really is off rather than being recaptured by name heuristics.
        /// </summary>
        private static WorldObjectKind ClassifyUnrecognised(WorldObject obj)
        {
            // The owning mod is installed but the player disabled its integration.
            if (WorldObjectAdapterRegistry.IsSuppressed(obj)) return WorldObjectKind.Ignored;

            // Master switch off: govern vanilla-derived objects only, exactly as before 0.7.
            if (!WorldObjectIntegrationSettings.masterEnabled) return WorldObjectKind.Ignored;

            WorldObjectKind kind = HeuristicClassify(obj);
            if (kind != WorldObjectKind.Unknown) return kind;

            NoteUnknown(obj);
            return WorldObjectKind.Unknown;
        }

        /// <summary>
        /// Last-resort naming heuristics for mods with no profile. Deliberately mirrors the
        /// substring checks the pre-0.7 code used, so behaviour never regresses for mods that
        /// were previously caught by them.
        /// </summary>
        private static WorldObjectKind HeuristicClassify(WorldObject obj)
        {
            for (Type t = obj.GetType(); t != null; t = t.BaseType)
            {
                string n = t.Name;
                if (n.IndexOf("Outpost", StringComparison.OrdinalIgnoreCase) >= 0) return WorldObjectKind.Outpost;
                if (n.IndexOf("Settlement", StringComparison.OrdinalIgnoreCase) >= 0) return WorldObjectKind.Settlement;
                if (n.IndexOf("Garrison", StringComparison.OrdinalIgnoreCase) >= 0) return WorldObjectKind.Military;
                if (n.IndexOf("Camp", StringComparison.OrdinalIgnoreCase) >= 0) return WorldObjectKind.Camp;
            }

            string defName = obj.def != null ? obj.def.defName : null;
            if (!string.IsNullOrEmpty(defName))
            {
                if (defName.IndexOf("Outpost", StringComparison.OrdinalIgnoreCase) >= 0) return WorldObjectKind.Outpost;
                if (defName.IndexOf("Settlement", StringComparison.OrdinalIgnoreCase) >= 0) return WorldObjectKind.Settlement;
                if (defName.IndexOf("Camp", StringComparison.OrdinalIgnoreCase) >= 0) return WorldObjectKind.Camp;
            }

            return WorldObjectKind.Unknown;
        }

        private static void NoteUnknown(WorldObject obj)
        {
            if (!WorldObjectIntegrationSettings.logUnknownWorldObjects) return;

            Type t = obj.GetType();
            if (!loggedUnknownTypes.Add(t)) return;

            Log.Message("[RegionsAndSocieties] Unclassified world object type '" + t.FullName
                        + "' (def " + (obj.def != null ? obj.def.defName : "null")
                        + "). Register an adapter via WorldObjectAdapterRegistry to govern it.");
        }

        // ---------------------------------------------------------------------
        // Convenience predicates — these are what call sites should use.
        // ---------------------------------------------------------------------

        public static bool IsSettlement(WorldObject obj)
        {
            return Classify(obj) == WorldObjectKind.Settlement;
        }

        public static bool IsOutpost(WorldObject obj)
        {
            return Classify(obj) == WorldObjectKind.Outpost;
        }

        /// <summary>Settlement, outpost, camp, or military installation — anything that holds ground.</summary>
        public static bool IsTerritorial(WorldObject obj)
        {
            return Classify(obj).IsTerritorial();
        }

        /// <summary>Permanent holdings only. Placement buffer and supply-range rules key off this.</summary>
        public static bool IsPermanentHolding(WorldObject obj)
        {
            return Classify(obj).IsPermanentHolding();
        }

        /// <summary>Anything that contributes residents to regional population density.</summary>
        public static bool HasPopulation(WorldObject obj)
        {
            return Classify(obj).HasPopulation();
        }

        /// <summary>
        /// A holding the player controls — used for "do I have a foothold here?" checks.
        /// Covers empire-style mods that run the player's colonies under their own world-object
        /// type rather than under the player Faction.
        /// </summary>
        public static bool IsPlayerHolding(WorldObject obj)
        {
            if (obj == null) return false;

            WorldObjectKind kind;
            IWorldObjectAdapter source;
            if (!WorldObjectAdapterRegistry.TryClassify(obj, out kind, out source))
            {
                kind = ClassifyUnrecognised(obj);
                source = null;
            }

            if (!kind.IsTerritorial()) return false;

            if (obj.Faction != null && obj.Faction.IsPlayer) return true;

            return source != null && source.PlayerOwnedByDefault;
        }

        // ---------------------------------------------------------------------
        // Bulk queries
        // ---------------------------------------------------------------------

        public static List<WorldObject> AllOfKind(WorldObjectKind kind)
        {
            var result = new List<WorldObject>();
            if (Find.WorldObjects == null) return result;

            List<WorldObject> all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (Classify(all[i]) == kind) result.Add(all[i]);
            }
            return result;
        }

        public static List<WorldObject> AllTerritorial()
        {
            var result = new List<WorldObject>();
            if (Find.WorldObjects == null) return result;

            List<WorldObject> all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (Classify(all[i]).IsTerritorial()) result.Add(all[i]);
            }
            return result;
        }

        /// <summary>
        /// Population of a world object, asking adapters first and falling back to the pre-0.7
        /// helpers so vanilla settlements keep their existing numbers.
        /// </summary>
        public static int GetPopulation(WorldObject obj)
        {
            if (obj == null) return 0;

            int pop;
            if (WorldObjectAdapterRegistry.TryGetPopulation(obj, out pop) && pop > 0) return pop;

            var settlement = obj as RimWorld.Planet.Settlement;
            if (settlement != null) return PopulationDensityUtility.GetSettlementPopulation(settlement);

            return 0;
        }
    }
}
