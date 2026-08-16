using System;
using RimWorld.Planet;

namespace RegionsAndSocieties.Integration
{
    /// <summary>
    /// Per-mod translator between a foreign world object and the data Regions &amp; Territories
    /// governance needs. Adapters are the ONLY place a concrete mod type may be named.
    ///
    /// Every member is optional by convention: return false when the adapter cannot answer and
    /// the registry moves on to the next adapter, ending at the always-present vanilla adapter.
    /// Derive from <see cref="WorldObjectAdapterBase"/> rather than implementing this directly, so
    /// that members added in later 0.7 epics do not break existing adapters.
    /// </summary>
    public interface IWorldObjectAdapter
    {
        /// <summary>Stable identifier, e.g. "empire", "voe", "vfe", "worlddomination", "vanilla".</summary>
        string AdapterId { get; }

        /// <summary>Human-readable name for settings UI and logs.</summary>
        string DisplayName { get; }

        /// <summary>Lower runs first. Mod adapters use 100-199; the vanilla fallback uses 1000.</summary>
        int Priority { get; }

        /// <summary>
        /// True only when the backing mod is loaded AND the player has left this integration enabled.
        /// An inactive adapter is skipped entirely — this is what makes every integration a soft dependency.
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// True when the backing mod is loaded, regardless of whether the player enabled this
        /// integration. Lets the classifier tell "this mod isn't installed" apart from "the player
        /// switched this integration off", so a disabled integration is not silently re-caught by
        /// the generic name heuristics.
        /// </summary>
        bool IsPresent { get; }

        /// <summary>
        /// True for mods whose world objects are the player's own holdings even though they are not
        /// owned by the player Faction (Empire Refactored runs the player's colonies this way).
        /// Used so "is this a player foothold?" needs no mod-specific check at the call site.
        /// </summary>
        bool PlayerOwnedByDefault { get; }

        /// <summary>Classify a world object. Return false to defer to the next adapter.</summary>
        bool TryClassify(WorldObject obj, out WorldObjectKind kind);

        /// <summary>Resident population of the object. Return false if this adapter cannot tell.</summary>
        bool TryGetPopulation(WorldObject obj, out int population);

        /// <summary>
        /// The mod's own upgrade level for the object (Empire settlement level, VOE outpost level),
        /// plus the maximum that mod allows. Used by settlement size-tier mapping.
        /// </summary>
        bool TryGetLevel(WorldObject obj, out int level, out int maxLevel);

        /// <summary>
        /// Called after Regions &amp; Territories has computed a production multiplier for the object,
        /// giving the owning mod's adapter a chance to clamp or adjust it. Default: no change.
        /// </summary>
        void PostProcessProductionMultiplier(WorldObject obj, ref float multiplier);

        /// <summary>Called once after all adapters are registered and the world is loaded.</summary>
        void OnWorldLoaded();
    }
}
