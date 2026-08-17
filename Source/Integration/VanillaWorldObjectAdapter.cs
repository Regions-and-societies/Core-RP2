using System;
using RimWorld;
using RimWorld.Planet;

namespace RegionsAndSocieties.Integration
{
    /// <summary>
    /// Always-present fallback. Runs last, so any mod adapter gets first say, and guarantees that
    /// base-game objects classify correctly even with every integration disabled.
    /// </summary>
    public class VanillaWorldObjectAdapter : WorldObjectAdapterBase
    {
        public override string AdapterId
        {
            get { return "vanilla"; }
        }

        public override string DisplayName
        {
            get { return "RimWorld (base game)"; }
        }

        public override int Priority
        {
            get { return 1000; }
        }

        public override bool IsActive
        {
            get { return true; }
        }

        public override bool TryClassify(WorldObject obj, out WorldObjectKind kind)
        {
            kind = WorldObjectKind.Unknown;
            if (obj == null) return false;

            // Concrete base-game types first — cheap and unambiguous.
            if (obj is Settlement)
            {
                kind = WorldObjectKind.Settlement;
                return true;
            }

            if (obj is Caravan)
            {
                kind = WorldObjectKind.Caravan;
                return true;
            }

            if (obj is Site)
            {
                kind = WorldObjectKind.Site;
                return true;
            }

            // Referenced by name rather than by type so that a rename between RimWorld versions
            // degrades to Unknown instead of failing to load the assembly.
            for (Type t = obj.GetType(); t != null; t = t.BaseType)
            {
                string n = t.Name;
                if (n == "DestroyedSettlement" || n == "AbandonedBaseWorldObject")
                {
                    kind = WorldObjectKind.Ignored;
                    return true;
                }
                if (n == "TravelingTransportPods" || n == "TravellingTransporters" || n == "PeaceTalks")
                {
                    kind = WorldObjectKind.Ignored;
                    return true;
                }
            }

            return false;
        }

        public override bool TryGetPopulation(WorldObject obj, out int population)
        {
            population = 0;

            Caravan caravan = obj as Caravan;
            if (caravan != null)
            {
                population = caravan.PawnsListForReading != null ? caravan.PawnsListForReading.Count : 0;
                return true;
            }

            return false;
        }
    }
}
