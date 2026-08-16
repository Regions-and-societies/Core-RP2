using System;
using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace RegionsAndSocieties.Integration
{
    /// <summary>
    /// Holds every registered <see cref="IWorldObjectAdapter"/> in priority order and fans queries
    /// out to them. Mirrors the existing <see cref="RegionalDemographicRegistry"/> pattern.
    ///
    /// Every call is exception-guarded: a broken third-party adapter degrades that one integration,
    /// it never takes down world generation or a tick.
    /// </summary>
    public static class WorldObjectAdapterRegistry
    {
        private static readonly List<IWorldObjectAdapter> adapters = new List<IWorldObjectAdapter>();
        private static bool initialized;

        public static IReadOnlyList<IWorldObjectAdapter> Adapters
        {
            get { return adapters; }
        }

        public static bool Initialized
        {
            get { return initialized; }
        }

        /// <summary>
        /// Builds the built-in adapter set. Safe to call more than once; later calls are no-ops.
        /// Called from the mod constructor, after Harmony patching.
        /// </summary>
        public static void Initialize()
        {
            if (initialized) return;
            initialized = true;

            // Core ships only the vanilla adapter. Every foreign-mod adapter arrives through a
            // compatibility patch calling Register from its own Mod constructor — patches load
            // after core, so they merge into the priority order kept below (Core-MMF#3). The old
            // KnownModProfiles table is gone; ReflectionWorldObjectAdapter remains available for
            // patches that need loose binding.
            Register(new VanillaWorldObjectAdapter());

            LogActiveAdapters();
        }

        public static void Register(IWorldObjectAdapter adapter)
        {
            if (adapter == null) return;

            for (int i = 0; i < adapters.Count; i++)
            {
                if (string.Equals(adapters[i].AdapterId, adapter.AdapterId, StringComparison.Ordinal))
                {
                    Log.Warning("[RegionsAndSocieties] World-object adapter '" + adapter.AdapterId + "' registered twice; ignoring the duplicate.");
                    return;
                }
            }

            adapters.Add(adapter);
            adapters.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            WorldObjectClassifier.InvalidateCache();
        }

        /// <summary>Used by tests and by settings changes that flip an integration on or off.</summary>
        public static void Clear()
        {
            adapters.Clear();
            initialized = false;
            WorldObjectClassifier.InvalidateCache();
        }

        public static void LogActiveAdapters()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("[RegionsAndSocieties] World-object adapters: ");
            for (int i = 0; i < adapters.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(adapters[i].DisplayName);
                sb.Append(SafeIsActive(adapters[i]) ? " (active)" : " (inactive)");
            }
            Log.Message(sb.ToString());
        }

        private static bool SafeIsActive(IWorldObjectAdapter adapter)
        {
            try
            {
                return adapter.IsActive;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[RegionsAndSocieties] Adapter '" + adapter.AdapterId + "' threw from IsActive: " + ex, 0x5A0001 ^ adapter.AdapterId.GetHashCode());
                return false;
            }
        }

        /// <summary>
        /// Whether this adapter claims the object at all. Data queries are gated on this so an
        /// adapter never answers for a foreign mod's object — without it, one mod's generic member
        /// name (Empire's "level") silently captures another mod's object that happens to share it.
        /// </summary>
        private static bool SafeRecognises(IWorldObjectAdapter adapter, WorldObject obj)
        {
            try
            {
                WorldObjectKind kind;
                return adapter.TryClassify(obj, out kind) && kind != WorldObjectKind.Unknown;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[RegionsAndSocieties] Adapter '" + adapter.AdapterId + "' threw from TryClassify: " + ex, 0x5A0002 ^ adapter.AdapterId.GetHashCode());
                return false;
            }
        }

        private static bool SafeIsPresent(IWorldObjectAdapter adapter)
        {
            try
            {
                return adapter.IsPresent;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[RegionsAndSocieties] Adapter '" + adapter.AdapterId + "' threw from IsPresent: " + ex, 0x5A0007 ^ adapter.AdapterId.GetHashCode());
                return false;
            }
        }

        /// <summary>
        /// True when the object belongs to a mod that is installed but whose integration the player
        /// switched off. Such an object must be left alone entirely — without this check the generic
        /// name heuristics would recapture it and the off switch would do nothing.
        /// </summary>
        public static bool IsSuppressed(WorldObject obj)
        {
            if (obj == null) return false;

            for (int i = 0; i < adapters.Count; i++)
            {
                IWorldObjectAdapter adapter = adapters[i];
                if (SafeIsActive(adapter)) continue;
                if (!SafeIsPresent(adapter)) continue;

                try
                {
                    WorldObjectKind candidate;
                    if (adapter.TryClassify(obj, out candidate) && candidate != WorldObjectKind.Unknown)
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[RegionsAndSocieties] Adapter '" + adapter.AdapterId + "' threw from TryClassify: " + ex, 0x5A0002 ^ adapter.AdapterId.GetHashCode());
                }
            }

            return false;
        }

        /// <summary>First active adapter that recognises the object wins. False means "nobody knows".</summary>
        public static bool TryClassify(WorldObject obj, out WorldObjectKind kind)
        {
            IWorldObjectAdapter ignored;
            return TryClassify(obj, out kind, out ignored);
        }

        /// <summary>
        /// As <see cref="TryClassify(WorldObject, out WorldObjectKind)"/>, but also reports which
        /// adapter answered — needed when the caller cares about the owning mod's conventions
        /// (e.g. whether its holdings are player-run) without naming that mod.
        /// </summary>
        public static bool TryClassify(WorldObject obj, out WorldObjectKind kind, out IWorldObjectAdapter source)
        {
            kind = WorldObjectKind.Unknown;
            source = null;
            if (obj == null) return false;

            for (int i = 0; i < adapters.Count; i++)
            {
                IWorldObjectAdapter adapter = adapters[i];
                if (!SafeIsActive(adapter)) continue;

                try
                {
                    WorldObjectKind candidate;
                    if (adapter.TryClassify(obj, out candidate) && candidate != WorldObjectKind.Unknown)
                    {
                        kind = candidate;
                        source = adapter;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[RegionsAndSocieties] Adapter '" + adapter.AdapterId + "' threw from TryClassify: " + ex, 0x5A0002 ^ adapter.AdapterId.GetHashCode());
                }
            }

            return false;
        }

        public static bool TryGetPopulation(WorldObject obj, out int population)
        {
            population = 0;
            if (obj == null) return false;

            for (int i = 0; i < adapters.Count; i++)
            {
                IWorldObjectAdapter adapter = adapters[i];
                if (!SafeIsActive(adapter)) continue;
                if (!SafeRecognises(adapter, obj)) continue;

                try
                {
                    int value;
                    if (adapter.TryGetPopulation(obj, out value) && value > 0)
                    {
                        population = value;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[RegionsAndSocieties] Adapter '" + adapter.AdapterId + "' threw from TryGetPopulation: " + ex, 0x5A0003 ^ adapter.AdapterId.GetHashCode());
                }
            }

            return false;
        }

        public static bool TryGetLevel(WorldObject obj, out int level, out int maxLevel)
        {
            level = 0;
            maxLevel = 0;
            if (obj == null) return false;

            for (int i = 0; i < adapters.Count; i++)
            {
                IWorldObjectAdapter adapter = adapters[i];
                if (!SafeIsActive(adapter)) continue;
                if (!SafeRecognises(adapter, obj)) continue;

                try
                {
                    int l, m;
                    if (adapter.TryGetLevel(obj, out l, out m))
                    {
                        level = l;
                        maxLevel = m;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[RegionsAndSocieties] Adapter '" + adapter.AdapterId + "' threw from TryGetLevel: " + ex, 0x5A0004 ^ adapter.AdapterId.GetHashCode());
                }
            }

            return false;
        }

        /// <summary>Lets each owning mod's adapter clamp a multiplier R&amp;T computed for its object.</summary>
        public static void PostProcessProductionMultiplier(WorldObject obj, ref float multiplier)
        {
            if (obj == null) return;

            for (int i = 0; i < adapters.Count; i++)
            {
                IWorldObjectAdapter adapter = adapters[i];
                if (!SafeIsActive(adapter)) continue;
                if (!SafeRecognises(adapter, obj)) continue;

                try
                {
                    adapter.PostProcessProductionMultiplier(obj, ref multiplier);
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[RegionsAndSocieties] Adapter '" + adapter.AdapterId + "' threw from PostProcessProductionMultiplier: " + ex, 0x5A0005 ^ adapter.AdapterId.GetHashCode());
                }
            }
        }

        public static void NotifyWorldLoaded()
        {
            WorldObjectClassifier.InvalidateCache();

            for (int i = 0; i < adapters.Count; i++)
            {
                IWorldObjectAdapter adapter = adapters[i];
                try
                {
                    adapter.OnWorldLoaded();
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[RegionsAndSocieties] Adapter '" + adapter.AdapterId + "' threw from OnWorldLoaded: " + ex, 0x5A0006 ^ adapter.AdapterId.GetHashCode());
                }
            }
        }
    }
}
