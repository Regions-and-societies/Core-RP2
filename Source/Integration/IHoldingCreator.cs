using RimWorld;
using RimWorld.Planet;
using RegionsAndSocieties.Sizing;

namespace RegionsAndSocieties.Integration
{
    /// <summary>
    /// Per-mod maker of a world-object holding. The write-side counterpart to
    /// <see cref="IWorldObjectAdapter"/>: adapters read foreign world objects, creators build them.
    /// Like adapters, a creator is the ONLY place its mod's concrete types may be named.
    ///
    /// Kept off the read-only <see cref="IWorldObjectAdapter"/> contract on purpose. Creating a VOE
    /// outpost is imperative work — pick a def, generate occupant pawns, register the object — that a
    /// declarative reflection profile cannot express, and folding it into the adapter interface would
    /// give every reader a mutating method it does not want. A small parallel registry keeps the two
    /// concerns apart.
    /// </summary>
    public interface IHoldingCreator
    {
        /// <summary>Stable id, matching the sibling adapter where one exists, e.g. "voe".</summary>
        string CreatorId { get; }

        /// <summary>Lower runs first, mirroring adapter priorities.</summary>
        int Priority { get; }

        /// <summary>True only when the backing mod is loaded AND the player left this integration on.</summary>
        bool IsActive { get; }

        /// <summary>Whether this creator makes holdings of the given kind at all.</summary>
        bool CanCreate(WorldObjectKind kind);

        /// <summary>
        /// Build a holding of <paramref name="kind"/> for <paramref name="faction"/> on
        /// <paramref name="tile"/>, shaped to <paramref name="archetype"/> where the kind supports
        /// it. Return false (leaving <paramref name="created"/> null) when this creator declines or
        /// cannot build — never throw; the registry moves on to the next creator.
        /// </summary>
        bool TryCreate(WorldObjectKind kind, OutpostArchetype archetype, Faction faction, int tile, out WorldObject created);
    }
}
