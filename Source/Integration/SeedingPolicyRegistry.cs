using System;
using System.Collections.Generic;
using Verse;

namespace RegionsAndSocieties.Integration
{
    /// <summary>
    /// Holds every registered <see cref="ISeedingPolicy"/> and resolves the winning policy for a kind: the
    /// lowest-priority active policy that claims it, or a generic <see cref="DefaultSeedingPolicy"/> when a
    /// kind has a creator but no registered policy. The sizing-side mirror of
    /// <see cref="HoldingCreatorRegistry"/> — same exception guarding and idempotent initialise.
    /// </summary>
    public static class SeedingPolicyRegistry
    {
        private static readonly List<ISeedingPolicy> policies = new List<ISeedingPolicy>();
        private static readonly Dictionary<WorldObjectKind, ISeedingPolicy> defaults =
            new Dictionary<WorldObjectKind, ISeedingPolicy>();
        private static bool initialized;

        public static IReadOnlyList<ISeedingPolicy> Policies => policies;
        public static bool Initialized => initialized;

        /// <summary>Marks the registry ready and registers Core's own policies. Safe to call repeatedly.</summary>
        public static void Initialize()
        {
            if (initialized) return;
            initialized = true;

            // Core ships the Outpost policy (pure allowance + archetype rules). Other kinds are seeded by a
            // CP mod's registered policy, or the generic default when a creator exists without one.
            Register(new OutpostSeedingPolicy());
        }

        public static void Register(ISeedingPolicy policy)
        {
            if (policy == null) return;

            policies.Add(policy);
            policies.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        /// <summary>Used by tests and by settings changes that flip an integration on or off.</summary>
        public static void Clear()
        {
            policies.Clear();
            defaults.Clear();
            initialized = false;
        }

        /// <summary>The policy that governs <paramref name="kind"/>: the lowest-priority active registered
        /// policy for it, else a cached generic default. Never null.</summary>
        public static ISeedingPolicy PolicyFor(WorldObjectKind kind)
        {
            for (int i = 0; i < policies.Count; i++)
            {
                ISeedingPolicy p = policies[i];
                try
                {
                    if (p.Kind == kind && p.IsActive) return p;
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[RegionsAndSocieties] Seeding policy for '" + kind + "' threw from Kind/IsActive: " + ex, 0x5B0101 ^ (int)kind);
                }
            }

            if (!defaults.TryGetValue(kind, out ISeedingPolicy def))
            {
                def = new DefaultSeedingPolicy(kind);
                defaults[kind] = def;
            }
            return def;
        }
    }
}
