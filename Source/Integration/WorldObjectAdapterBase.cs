using System;
using RimWorld.Planet;

namespace RegionsAndSocieties.Integration
{
    /// <summary>
    /// Convenience base for adapters. Every optional member no-ops, so an adapter only overrides
    /// what its mod actually exposes and later 0.7 epics can extend <see cref="IWorldObjectAdapter"/>
    /// without breaking anything already written.
    /// </summary>
    public abstract class WorldObjectAdapterBase : IWorldObjectAdapter
    {
        public abstract string AdapterId { get; }

        public virtual string DisplayName
        {
            get { return AdapterId; }
        }

        public virtual int Priority
        {
            get { return 150; }
        }

        public virtual bool IsActive
        {
            get { return true; }
        }

        /// <summary>Adapters with no separate "installed" notion are present whenever they are active.</summary>
        public virtual bool IsPresent
        {
            get { return IsActive; }
        }

        public virtual bool PlayerOwnedByDefault
        {
            get { return false; }
        }

        public virtual bool TryClassify(WorldObject obj, out WorldObjectKind kind)
        {
            kind = WorldObjectKind.Unknown;
            return false;
        }

        public virtual bool TryGetPopulation(WorldObject obj, out int population)
        {
            population = 0;
            return false;
        }

        public virtual bool TryGetLevel(WorldObject obj, out int level, out int maxLevel)
        {
            level = 0;
            maxLevel = 0;
            return false;
        }

        public virtual void PostProcessProductionMultiplier(WorldObject obj, ref float multiplier)
        {
        }

        public virtual void OnWorldLoaded()
        {
        }
    }
}
