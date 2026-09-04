// Runnable stubs of the RimWorld / Verse surface used by Source/Integration/*.cs.
// NOT shipped. Used solely to type-check AND behaviour-test the new integration layer
// in an environment without the game's Managed assemblies.
using System;
using System.Collections.Generic;

namespace Verse
{
    public class Def { public string defName; }

    public static class Log
    {
        public static readonly List<string> Captured = new List<string>();
        public static void Message(string s) { Captured.Add("MSG  " + s); }
        public static void Warning(string s) { Captured.Add("WARN " + s); }
        public static void Error(string s) { Captured.Add("ERR  " + s); }
        public static void ErrorOnce(string s, int key) { Captured.Add("ERR  " + s); }
        public static void WarningOnce(string s, int key) { Captured.Add("WARN " + s); }
    }

    public static class GenTypes
    {
        // Real lookup so the adapters' mod-presence detection can be exercised: the test
        // assembly declares fake FactionColonies.* / Outposts.* types.
        public static Type GetTypeInAnyAssembly(string name)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(name, false);
                if (t != null) return t;
            }
            return null;
        }
    }

    public static class Scribe_Values
    {
        public static void Look<T>(ref T value, string label, T defaultValue = default(T), bool forceSave = false) { }
    }

    public class ModSettings { public virtual void ExposeData() { } }

    // The real Pawn is Verse.Pawn. It lived under RimWorld.Planet in these stubs until the
    // residency suite needed the same type the source actually names.
    public partial class Pawn { }

    // Partial so RimWorldStubsExt.cs can bolt on the wider surface (WorldGrid, World,
    // FactionManager, ...) needed to type-check the impure files, without the two behaviour
    // suites having to drag that surface in.
    public static partial class Find
    {
        public static RimWorld.Planet.WorldObjectsHolder WorldObjects = new RimWorld.Planet.WorldObjectsHolder();
    }
}

namespace RimWorld
{
    public partial class Faction
    {
        public bool IsPlayer;
        public static Faction OfPlayerSilentFail { get { return null; } }
    }
}

namespace RimWorld.Planet
{
    using Verse;

    public class WorldObjectDef : Def { }

    /// <summary>
    /// Mirrors the 1.6 struct closely enough to catch the mistakes that matter: it converts
    /// implicitly to and from int in both directions, which is why the shipping code can pass a
    /// raw tile id where a PlanetTile is expected and vice versa.
    /// </summary>
    // Minimal planet-layer double: a stub tile is always on the root surface, so surface-only guards
    // behave as they did before multi-layer worlds existed (#24).
    public class PlanetLayer
    {
        public bool IsRootSurface = true;
        public static readonly PlanetLayer Surface = new PlanetLayer();
    }

    public struct PlanetTile
    {
        public int tileId;
        public PlanetTile(int tileId) { this.tileId = tileId; }

        public static readonly PlanetTile Invalid = new PlanetTile(-1);
        public bool Valid { get { return tileId >= 0; } }
        public PlanetLayer Layer { get { return PlanetLayer.Surface; } }

        public static implicit operator PlanetTile(int id) { return new PlanetTile(id); }
        public static implicit operator int(PlanetTile t) { return t.tileId; }

        public static bool operator ==(PlanetTile a, PlanetTile b) { return a.tileId == b.tileId; }
        public static bool operator !=(PlanetTile a, PlanetTile b) { return a.tileId != b.tileId; }
        public override bool Equals(object o) { return o is PlanetTile && ((PlanetTile)o).tileId == tileId; }
        public override int GetHashCode() { return tileId; }
    }

    public class WorldObject
    {
        public WorldObjectDef def;
        public Faction Faction;
        public PlanetTile Tile;
        public virtual string Label { get { return def != null ? def.defName : string.Empty; } }
    }

    public class Settlement : WorldObject { }
    public class Site : WorldObject { }
    public class Caravan : WorldObject
    {
        public List<Pawn> PawnsListForReading = new List<Pawn>();
    }

    public class WorldObjectsHolder
    {
        public List<WorldObject> AllWorldObjects = new List<WorldObject>();
        public List<Settlement> Settlements = new List<Settlement>();

        public List<WorldObject> ObjectsAt(int tileId) { return new List<WorldObject>(); }
        public bool AnyWorldObjectAt(int tileId) { return false; }
    }
}

namespace RegionsAndSocieties
{
    public static class PopulationDensityUtility
    {
        public static int GetSettlementPopulation(RimWorld.Planet.Settlement s) { return 42; }
        public static int GetPopulationAtTile(int tileId) { return 0; }
        public static int GetSourcePopulationAtTile(int targetTile) { return 0; }
        public static string GetDwellingsDisplay(int tileId) { return null; }
        public static int CacheVersion { get; set; }
    }
}
