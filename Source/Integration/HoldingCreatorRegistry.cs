using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using RegionsAndSocieties.Sizing;
using Verse;

namespace RegionsAndSocieties.Integration
{
    /// <summary>
    /// Holds every registered <see cref="IHoldingCreator"/> in priority order and hands a creation
    /// request to the first active one that can satisfy it. The write-side mirror of
    /// <see cref="WorldObjectAdapterRegistry"/>, down to the exception guarding: a broken creator
    /// degrades that one integration, it never takes down world generation.
    /// </summary>
    public static class HoldingCreatorRegistry
    {
        private static readonly List<IHoldingCreator> creators = new List<IHoldingCreator>();
        private static bool initialized;

        public static IReadOnlyList<IHoldingCreator> Creators => creators;
        public static bool Initialized => initialized;

        /// <summary>Marks the registry ready. Safe to call more than once; later calls are no-ops.</summary>
        public static void Initialize()
        {
            if (initialized) return;
            initialized = true;

            // Core ships no creators of its own. Compatibility patches contribute them — the VOE
            // outpost creator lives in Regions-and-societies/VOE-CP and registers from that patch's
            // Mod constructor (Core-MMF#3).
            LogActiveCreators();
        }

        public static void Register(IHoldingCreator creator)
        {
            if (creator == null) return;

            for (int i = 0; i < creators.Count; i++)
            {
                if (string.Equals(creators[i].CreatorId, creator.CreatorId, StringComparison.Ordinal))
                {
                    Log.Warning("[RegionsAndSocieties] Holding creator '" + creator.CreatorId + "' registered twice; ignoring the duplicate.");
                    return;
                }
            }

            creators.Add(creator);
            creators.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        /// <summary>Used by tests and by settings changes that flip an integration on or off.</summary>
        public static void Clear()
        {
            creators.Clear();
            initialized = false;
        }

        /// <summary>Whether any active creator can build this kind — the seeding pass's precondition.</summary>
        public static bool AnyActiveFor(WorldObjectKind kind)
        {
            for (int i = 0; i < creators.Count; i++)
            {
                IHoldingCreator c = creators[i];
                try
                {
                    if (c.IsActive && c.CanCreate(kind)) return true;
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[RegionsAndSocieties] Holding creator '" + c.CreatorId + "' threw from IsActive/CanCreate: " + ex, 0x5B0001 ^ c.CreatorId.GetHashCode());
                }
            }
            return false;
        }

        /// <summary>First active creator that builds the kind wins. False means nobody made one.</summary>
        public static bool TryCreate(WorldObjectKind kind, OutpostArchetype archetype, Faction faction, int tile, out WorldObject created)
        {
            created = null;

            for (int i = 0; i < creators.Count; i++)
            {
                IHoldingCreator c = creators[i];
                try
                {
                    if (!c.IsActive || !c.CanCreate(kind)) continue;
                    if (c.TryCreate(kind, archetype, faction, tile, out created) && created != null)
                    {
                        return true;
                    }
                    created = null;
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[RegionsAndSocieties] Holding creator '" + c.CreatorId + "' threw from TryCreate: " + ex, 0x5B0002 ^ c.CreatorId.GetHashCode());
                    created = null;
                }
            }

            return false;
        }

        private static void LogActiveCreators()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("[RegionsAndSocieties] Holding creators: ");
            if (creators.Count == 0) sb.Append("(none)");
            for (int i = 0; i < creators.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(creators[i].CreatorId);
                bool active;
                try { active = creators[i].IsActive; } catch { active = false; }
                sb.Append(active ? " (active)" : " (inactive)");
            }
            Log.Message(sb.ToString());
        }
    }
}
