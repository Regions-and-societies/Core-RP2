using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RegionsAndSocieties.Partition
{
    /// <summary>
    /// Holds every registered <see cref="IRegionPartitioner"/> and resolves the one a world should use.
    /// The read-side mirror of the mod's other extension registries (<c>HoldingCreatorRegistry</c>,
    /// <c>WorldObjectAdapterRegistry</c>): Core registers its own defaults on init, compatibility/expansion
    /// mods contribute more from their Mod constructor, and an unknown id degrades to the default rather
    /// than breaking world generation.
    /// </summary>
    public static class RegionPartitionerRegistry
    {
        /// <summary>The id of Core's default algorithm (contain-then-subdivide, 0.3.0).</summary>
        public const string DefaultAlgorithmId = "contain_subdivide";
        /// <summary>The id of Core's legacy algorithm (anchor-Voronoi boxes, 0.2.x).</summary>
        public const string LegacyAlgorithmId = "anchor_voronoi";

        private static readonly List<IRegionPartitioner> partitioners = new List<IRegionPartitioner>();
        private static bool initialized;

        public static IReadOnlyList<IRegionPartitioner> All
        {
            get { if (!initialized) Initialize(); return partitioners; }
        }

        public static bool Initialized => initialized;

        /// <summary>Register Core's built-in algorithms. Safe to call more than once; later calls no-op.</summary>
        public static void Initialize()
        {
            if (initialized) return;
            initialized = true;

            Register(new ContainSubdividePartitioner());
            Register(new AnchorVoronoiPartitioner());

            Log.Message("[RegionsAndSocieties] Region partitioners: " + string.Join(", ", partitioners.Select(p => p.AlgorithmId)));
        }

        /// <summary>Add a partitioner. Deduped by <see cref="IRegionPartitioner.AlgorithmId"/>; kept sorted
        /// by <see cref="IRegionPartitioner.Order"/> for the dropdown.</summary>
        public static void Register(IRegionPartitioner partitioner)
        {
            if (partitioner == null) return;
            if (string.IsNullOrEmpty(partitioner.AlgorithmId))
            {
                Log.Warning("[RegionsAndSocieties] A region partitioner with no AlgorithmId was ignored.");
                return;
            }

            for (int i = 0; i < partitioners.Count; i++)
            {
                if (string.Equals(partitioners[i].AlgorithmId, partitioner.AlgorithmId, StringComparison.Ordinal))
                {
                    Log.Warning($"[RegionsAndSocieties] Region partitioner '{partitioner.AlgorithmId}' registered twice; ignoring the duplicate.");
                    return;
                }
            }

            partitioners.Add(partitioner);
            partitioners.Sort((a, b) => a.Order.CompareTo(b.Order));
        }

        /// <summary>The partitioner with this id, or the <see cref="Default"/> if the id is unknown (e.g. a
        /// mod that added it is no longer installed) — logged, never null unless nothing is registered.</summary>
        public static IRegionPartitioner Get(string algorithmId)
        {
            if (!initialized) Initialize();

            IRegionPartitioner match = partitioners.FirstOrDefault(p => string.Equals(p.AlgorithmId, algorithmId, StringComparison.Ordinal));
            if (match != null) return match;

            IRegionPartitioner fallback = Default;
            if (!string.IsNullOrEmpty(algorithmId) && fallback != null)
                Log.Warning($"[RegionsAndSocieties] Unknown region partitioner '{algorithmId}' (a mod that added it may be missing); falling back to '{fallback.AlgorithmId}'.");
            return fallback;
        }

        /// <summary>Core's default partitioner (contain-then-subdivide), or the first registered if that is
        /// somehow absent.</summary>
        public static IRegionPartitioner Default
        {
            get
            {
                if (!initialized) Initialize();
                return partitioners.FirstOrDefault(p => p.AlgorithmId == DefaultAlgorithmId) ?? partitioners.FirstOrDefault();
            }
        }

        /// <summary>Tests and settings-reset support.</summary>
        public static void Clear()
        {
            partitioners.Clear();
            initialized = false;
        }
    }
}
