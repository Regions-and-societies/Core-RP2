// Extended stub surface — the second half of the sandbox type-check story.
//
// RimWorldStubs.cs is enough to *run* the pure layers (Integration, Placement). This file adds
// the wider RimWorld/Verse/Unity/Harmony surface so the *impure* files — the ones that touch
// Find, Harmony, and the world grid — can at least be compiled. Visual Studio is unreachable
// from this sandbox, so a clean compile here is the only thing standing between a signature
// mistake and a broken build on the dev machine.
//
// These are shapes, not behaviour. Nothing here is executed by the test suites.
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public static class Time
    {
        public static float realtimeSinceStartup { get { return 0f; } }
    }

    public static class Mathf
    {
        public static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }
        public static float Max(float a, float b) { return a > b ? a : b; }
        public static int Max(int a, int b) { return a > b ? a : b; }
        public static float Min(float a, float b) { return a < b ? a : b; }
        public static int Min(int a, int b) { return a < b ? a : b; }
        public static int RoundToInt(float v) { return (int)Math.Round(v); }
    }
}

namespace HarmonyLib
{
    public enum MethodType { Normal, Getter, Setter, Constructor }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : Attribute
    {
        public HarmonyPatch() { }
        public HarmonyPatch(Type declaringType) { }
        public HarmonyPatch(Type declaringType, string methodName) { }
        public HarmonyPatch(Type declaringType, string methodName, MethodType methodType) { }
        public HarmonyPatch(string methodName) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPostfix : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPrefix : Attribute { }
}

namespace Verse
{
    using RimWorld.Planet;

    public interface IExposable { void ExposeData(); }

    public enum LookMode { Undefined, Value, Def, Deep, Reference, LocalTargetInfo, TargetInfo, GlobalTargetInfo, BodyPart }

    public enum ProgramState { Entry, MapInitializing, Playing }

    public static class Current
    {
        public static ProgramState ProgramState = ProgramState.Playing;
    }

    public enum LoadSaveMode { Inactive, Saving, LoadingVars, ResolvingCrossRefs, PostLoadInit }

    public static class Scribe
    {
        public static LoadSaveMode mode = LoadSaveMode.Inactive;
    }

    public static class Scribe_Collections
    {
        public static void Look<T>(ref List<T> list, string label, LookMode lookMode = LookMode.Undefined, params object[] ctorArgs) { }
        public static void Look<K, V>(ref Dictionary<K, V> dict, string label, LookMode keyLookMode = LookMode.Undefined, LookMode valueLookMode = LookMode.Undefined) { }
    }

    public static class Scribe_Defs
    {
        public static void Look<T>(ref T value, string label) where T : Def, new() { }
    }

    public static class Scribe_Deep
    {
        public static void Look<T>(ref T target, string label, params object[] ctorArgs) where T : IExposable { }
    }

    public enum Hilliness { Undefined, Flat, SmallHills, LargeHills, Mountainous, Impassable }

    public class BiomeDef : Def
    {
        public float plantDensity;
        public float forageability;
        public float TreeDensity;
    }

    /// <summary>1.6 turned Tile into a class; only the members the shipping code reads are present.</summary>
    public class Tile
    {
        public BiomeDef PrimaryBiome;
        public Hilliness hilliness;
    }

    public class TickManager
    {
        public int TicksGame;
    }

    public static partial class Find
    {
        public static WorldGrid WorldGrid = new WorldGrid();
        public static World World = new World();
        public static RimWorld.FactionManager FactionManager = new RimWorld.FactionManager();
        public static TickManager TickManager = new TickManager();
        public static WorldSelector WorldSelector = new WorldSelector();
    }
}

namespace RimWorld
{
    using System.Collections.Generic;
    using Verse;
    using RimWorld.Planet;

    public enum TechLevel { Undefined, Animal, Neolithic, Medieval, Industrial, Spacer, Ultra, Archotech }

    public class FactionDef : Def
    {
        public TechLevel techLevel;
    }

    public partial class Faction
    {
        public FactionDef def = new FactionDef();
        public string Name = "faction";

        public string GetUniqueLoadID() { return "Faction_" + Name; }

        public static Faction OfPlayer = new Faction { IsPlayer = true, Name = "player" };
    }

    public class FactionManager
    {
        public List<Faction> AllFactionsListForReading = new List<Faction>();
    }

    public static class TileFinder
    {
        public static bool IsValidTileForNewSettlement(PlanetTile tile, System.Text.StringBuilder reason = null) { return true; }
    }
}

namespace RimWorld.Planet
{
    using System.Collections.Generic;
    using Verse;

    public class WorldGrid
    {
        public int TilesCount = 1000;

        public Tile this[int tileId] { get { return new Tile(); } }
        public Tile this[PlanetTile tile] { get { return new Tile(); } }

        public int TraversalDistanceBetween(PlanetTile a, PlanetTile b) { return 0; }
        public int TraversalDistanceBetween(PlanetTile a, PlanetTile b, bool passImpassable, int maxDist) { return 0; }
        public float ApproxDistanceInTiles(PlanetTile a, PlanetTile b) { return 0f; }
        public void GetTileNeighbors(PlanetTile tile, List<PlanetTile> outList) { outList.Clear(); }
        public void OverlayRoad(PlanetTile fromTile, PlanetTile toTile, Verse.RoadDef roadDef) { }
    }

    public class WorldComponent
    {
        public World world;
        public WorldComponent(World world) { this.world = world; }
        public virtual void ExposeData() { }
        public virtual void WorldComponentTick() { }
    }

    public class World
    {
        private readonly List<object> components = new List<object>();
        public T GetComponent<T>() where T : WorldComponent { return null; }
    }

    public class WorldSelector
    {
        public PlanetTile SelectedTile = PlanetTile.Invalid;
    }

    public class WorldInspectPane
    {
        public string TileInspectString { get { return string.Empty; } }
    }
}

namespace RegionsAndSocieties
{
    using System.Collections.Generic;
    using RimWorld;
    using RimWorld.Planet;

    /// <summary>
    /// Stand-in for the real WorldComponent, written from its actual signatures (SynapseRegionManager.cs
    /// lines 49, 78, 85, 90, 1218) so the callers in WorldObjectPlacementUtility and the patches are
    /// checked against the shapes they will really meet.
    /// </summary>
    public class SynapseRegionManager : WorldComponent
    {
        public SynapseRegionManager(World world) : base(world) { }

        public List<GeographicProvince> Provinces = new List<GeographicProvince>();

        public bool StrictTerritorialOwnership = false;

        public int GetProvinceId(int tileId) { return -1; }
        public GeographicProvince GetProvince(int provinceId) { return null; }
        public GeographicProvince GetProvinceForTile(int tileId) { return null; }
        public bool AreProvincesAdjacent(GeographicProvince a, GeographicProvince b) { return false; }
        public int GetSettlementPlacementOrder(int tileId) { return -1; }
        public void SetSettlementPlacementOrder(int tileId, int order) { }
        public int GetNextPlacementOrderForFaction(Faction faction) { return 1; }
    }
}

// ---------------------------------------------------------------------------
// Enough of the item/reward surface to type-check RegionsAndSocieties_EmpiresPatch.
//
// That file was outside the compile set until 0.7 Epic 3 child 5, which is when it stopped
// carrying its own economy arithmetic and started calling ProductionScalingUtility. Those two
// call sites are the whole point of the child, so they are worth stubbing for. Everything below
// exists only so the file's signatures resolve — none of it has behaviour, and none of it is
// exercised by any test.
// ---------------------------------------------------------------------------
namespace Verse
{
    using System.Collections.Generic;

    public class Thing
    {
        public int stackCount = 1;
        public float MarketValue { get { return 0f; } }
    }

    public class ThingFilter
    {
        public void SetAllow(object def, bool allow) { }
    }

    public class QualityGenerator { }

    public struct FloatRange
    {
        public float min;
        public float max;
        public FloatRange(float min, float max) { this.min = min; this.max = max; }
    }

    public struct IntRange
    {
        public int min;
        public int max;
    }

    public class RoadDef : Def { }

    public class ThingCategoryDef : Def { }
    public class StuffCategoryDef : Def { }
    public class ThingDef : Def { }

    public static class TranslatorStub
    {
        public static string Translate(this string key) { return key; }
    }

    public static class Messages
    {
        public static void Message(string text, object type) { }
    }

    public static class GenDefDatabase
    {
        public static Def GetDef(System.Type type, string defName, bool errorOnFail = true) { return null; }
    }

    public static class DefDatabase<T> where T : Def
    {
        public static List<T> AllDefsListForReading { get { return new List<T>(); } }
    }
}

namespace RimWorld
{
    using System.Collections.Generic;
    using Verse;

    public class ThingSetMakerParams
    {
        public FloatRange? totalMarketValueRange;
        public TechLevel? techLevel;
        public ThingFilter filter;
        public IntRange? countRange;
        public QualityGenerator qualityGenerator;
    }

    public class ThingSetMaker
    {
        public List<Thing> Generate(ThingSetMakerParams parms) { return new List<Thing>(); }
    }

    public class ThingSetMaker_MarketValue : ThingSetMaker { }

    public static class MessageTypeDefOf
    {
        public static readonly object RejectInput = new object();
    }
}

namespace HarmonyLib
{
    using System;
    using System.Reflection;

    public static class AccessTools
    {
        public static MethodBase Method(Type type, string name, Type[] parameters = null) { return null; }
        public static Type TypeByName(string name) { return null; }
        public static PropertyInfo Property(Type type, string name) { return null; }
    }
}

namespace RegionsAndSocieties
{
    using RimWorld;

    /// <summary>
    /// Stand-in, as with PopulationDensityUtility. The real file reaches deep into worldgen and
    /// Unity vectors; only Empire's two debug-settlement patches call it, and nothing in 0.7 changed
    /// either of them.
    /// </summary>
    public static class FactionPlacementUtility
    {
        public static int FindBestTileForFaction(Faction faction) { return -1; }
    }

    /// <summary>
    /// Stand-in for the settings class (a ModSettings in the real file). RegionalOwnershipUtility
    /// consults the breakdown toggle when building the developer derivation (#53/#54).
    /// </summary>
    public static class FactionPlacementSettings
    {
        public static bool showCalculationBreakdowns = false;
    }

    /// <summary>
    /// Stand-in for MapModes/TextureUtility, which is Unity-coupled (Texture2D). The tier
    /// vocabulary in RegionalDomainStatus names factions through its display-name helper (#64).
    /// </summary>
    public static class TextureUtility
    {
        public static string GetFactionDisplayName(Faction faction) { return faction?.Name ?? "None"; }
    }
}
