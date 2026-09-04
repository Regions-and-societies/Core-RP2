# Developer's Guide

The public extension points of Regions and Societies core, for authors of compatibility patches and consumer mods. Everything here is real, shipped API on the `RegionsAndSocieties` assembly — namespaces are given per entry.

Core has no custom `Def` types; all extension is through C# registration at load time. Reference the assembly (or bind by reflection) and call the registries from your `Mod` constructor — core initialises them in its own constructor and patches load after core, so registration always lands in a live registry.

Contents:

- [Adapter registration API](#adapter-registration-api) — teach core to read another mod's world objects
- [World-object classification queries](#world-object-classification-queries) — ask core what an object is
- [Loose-binding adapters](#loose-binding-adapters) — reflection profiles for patches without an assembly reference
- [Holding creators](#holding-creators) — teach core to *build* another mod's holdings
- [Demographic providers](#demographic-providers) — contribute a demographics component to ownership
- [Territory-claim hook](#territory-claim-hook) — consume the contested-settlement event
- [Ownership vocabulary](#ownership-vocabulary) — tiers, thresholds and placement rules
- [Settlement tiers](#settlement-tiers)
- [Settings surfaces](#settings-surfaces)

---

## Adapter registration API

Namespace `RegionsAndSocieties.Integration`. An **adapter** translates a foreign mod's world objects into the data territory governance needs. The registry holds every adapter in priority order and takes the first non-`Unknown` answer.

### WorldObjectAdapterRegistry.Register

```csharp
public static void Register(IWorldObjectAdapter adapter)
```

| Parameter | Type | Meaning |
|---|---|---|
| `adapter` | `IWorldObjectAdapter` | The adapter to add. `null` is ignored. |

**Returns:** nothing. The adapter is inserted into priority order (lower `Priority` first) and the classification cache is invalidated. A second registration with the same `AdapterId` logs a warning and is dropped.

**Call it from your patch's `Mod` constructor.** Core registers only `VanillaWorldObjectAdapter` (priority 1000); everything else arrives through this call.

```csharp
public class MyPatchMod : Mod
{
    public MyPatchMod(ModContentPack content) : base(content)
    {
        WorldObjectAdapterRegistry.Register(new MyModAdapter());
    }
}
```

Other registry members you may need:

| Member | Signature | Notes |
|---|---|---|
| `Adapters` | `IReadOnlyList<IWorldObjectAdapter> Adapters { get; }` | The registered set, priority-ordered. |
| `Initialized` | `bool Initialized { get; }` | True once core's constructor has run. |
| `TryClassify` | `bool TryClassify(WorldObject obj, out WorldObjectKind kind)` | First active adapter that recognises the object wins; `false` means nobody knows. An overload also outs the answering `IWorldObjectAdapter source`. |
| `TryGetPopulation` | `bool TryGetPopulation(WorldObject obj, out int population)` | Asks only adapters that recognise the object. |
| `TryGetLevel` | `bool TryGetLevel(WorldObject obj, out int level, out int maxLevel)` | The owning mod's own upgrade ladder. |
| `PostProcessProductionMultiplier` | `void PostProcessProductionMultiplier(WorldObject obj, ref float multiplier)` | Lets the owning mod's adapter clamp a multiplier core computed. |
| `IsSuppressed` | `bool IsSuppressed(WorldObject obj)` | True when the object's mod is installed but its integration is switched off. |
| `Clear` | `void Clear()` | Empties the registry. For tests and integration toggles only. |

Every fan-out call is exception-guarded: an adapter that throws is logged once and skipped, never fatal.

### IWorldObjectAdapter

The contract. **Derive from `WorldObjectAdapterBase` rather than implementing this directly** — the base no-ops every optional member, so interface growth cannot break your patch.

```csharp
public interface IWorldObjectAdapter
{
    string AdapterId { get; }        // stable unique id, e.g. "empire", "voe"
    string DisplayName { get; }      // for settings UI and logs
    int Priority { get; }            // lower runs first; mod adapters 100-199, vanilla 1000
    bool IsActive { get; }           // mod loaded AND player left the integration on
    bool IsPresent { get; }          // mod loaded, regardless of the toggle
    bool PlayerOwnedByDefault { get; } // Empire-style: player holdings not owned by the player Faction

    bool TryClassify(WorldObject obj, out WorldObjectKind kind);
    bool TryGetPopulation(WorldObject obj, out int population);
    bool TryGetLevel(WorldObject obj, out int level, out int maxLevel);
    void PostProcessProductionMultiplier(WorldObject obj, ref float multiplier);
    void OnWorldLoaded();            // once per world load, after all adapters are registered
}
```

Every `Try*` member is optional by convention: return `false` to defer to the next adapter, ending at the always-present vanilla adapter. `WorldObjectAdapterBase` defaults: `Priority` 150, `IsActive` true, `IsPresent` = `IsActive`, `PlayerOwnedByDefault` false, every `Try*` returns false.

**Worked example** — a hard-dependent patch adapter:

```csharp
using RegionsAndSocieties.Integration;
using RimWorld.Planet;

public class FrontierAdapter : WorldObjectAdapterBase
{
    public override string AdapterId => "frontier";
    public override string DisplayName => "Frontier Nations";
    public override int Priority => 150;

    public override bool TryClassify(WorldObject obj, out WorldObjectKind kind)
    {
        kind = WorldObjectKind.Unknown;
        if (obj is Frontier.TownWorldObject)  { kind = WorldObjectKind.Settlement; return true; }
        if (obj is Frontier.FortWorldObject)  { kind = WorldObjectKind.Military;   return true; }
        return false;
    }

    public override bool TryGetLevel(WorldObject obj, out int level, out int maxLevel)
    {
        level = 0; maxLevel = 0;
        if (obj is Frontier.TownWorldObject town)
        {
            level = town.growthStage;   // the target mod's real member
            maxLevel = 5;
            return true;
        }
        return false;
    }
}
```

### WorldObjectKind

The mod-agnostic classification every rule keys off:

```csharp
public enum WorldObjectKind
{
    Unknown = 0,     // could not be classified; governance treats it as inert
    Settlement = 1,  // permanent population centre
    Outpost = 2,     // production/extraction holding
    Camp = 3,        // temporary encampment
    Military = 4,    // installation / force projection point
    Site = 5,        // quest/map site; not territorial
    Caravan = 6,     // moving group; never territorial
    Ignored = 7      // explicitly excluded from governance
}
```

Extension predicates (`WorldObjectKindExtensions`):

| Method | Signature | True for |
|---|---|---|
| `IsTerritorial` | `bool IsTerritorial(this WorldObjectKind kind)` | Settlement, Outpost, Camp, Military |
| `IsPermanentHolding` | `bool IsPermanentHolding(this WorldObjectKind kind)` | Settlement, Outpost, Military |
| `HasPopulation` | `bool HasPopulation(this WorldObjectKind kind)` | Settlement, Outpost, Camp, Military |

---

## World-object classification queries

Namespace `RegionsAndSocieties.Integration`, class `WorldObjectClassifier` — the read surface consumer mods should use instead of touching the registry directly. Resolution order: registered adapters, then generic name heuristics, then `Unknown` (logged once per type when the diagnostic setting is on).

| Method | Signature | Returns |
|---|---|---|
| `Classify` | `WorldObjectKind Classify(WorldObject obj)` | The object's kind; `Unknown` if nothing recognises it. |
| `IsSettlement` | `bool IsSettlement(WorldObject obj)` | Kind == Settlement. |
| `IsOutpost` | `bool IsOutpost(WorldObject obj)` | Kind == Outpost. |
| `IsTerritorial` | `bool IsTerritorial(WorldObject obj)` | Anything that holds ground. |
| `IsPermanentHolding` | `bool IsPermanentHolding(WorldObject obj)` | Buffer/supply rules key off this. |
| `HasPopulation` | `bool HasPopulation(WorldObject obj)` | Contributes residents to density. |
| `IsPlayerHolding` | `bool IsPlayerHolding(WorldObject obj)` | Player-faction objects, plus objects whose adapter says `PlayerOwnedByDefault`. |
| `AllOfKind` | `List<WorldObject> AllOfKind(WorldObjectKind kind)` | All current world objects of that kind. |
| `AllTerritorial` | `List<WorldObject> AllTerritorial()` | All territorial objects. |
| `GetPopulation` | `int GetPopulation(WorldObject obj)` | Adapter answer first, falling back to core's vanilla settlement estimate; 0 if nothing knows. |
| `InvalidateCache` | `void InvalidateCache()` | Drops cached lookups; called for you when adapters change. |

**Worked example** — a consumer mod counting a faction's real footprint:

```csharp
int holdings = WorldObjectClassifier.AllTerritorial()
    .Count(o => o.Faction == faction && WorldObjectClassifier.IsPermanentHolding(o));
```

---

## Loose-binding adapters

For a patch that cannot (or prefers not to) reference its target assembly, core keeps a declarative adapter driven entirely by strings. Namespace `RegionsAndSocieties.Integration`.

### ReflectionWorldObjectAdapter

```csharp
public ReflectionWorldObjectAdapter(WorldObjectAdapterProfile profile)
```

| Parameter | Type | Meaning |
|---|---|---|
| `profile` | `WorldObjectAdapterProfile` | The declarative description below. Must not be null. |

Resolves nothing at construction time, so an unloaded mod costs nothing. `IsPresent` is true when any `markerTypes` entry resolves in a loaded assembly; classification walks `typeRules` in order and caches per type; population and level are read by member name through a cached reflection helper.

### WorldObjectAdapterProfile

Public fields (set what you need, chain rules with `Rule(...)`):

| Field | Type | Meaning |
|---|---|---|
| `adapterId` | `string` | Stable id; also the settings key. |
| `displayName` | `string` | For logs and UI. |
| `priority` | `int` | Default 150. |
| `markerTypes` | `string[]` | Full type names whose presence proves the mod is loaded. |
| `packageId` | `string` | The target as it appears in ModsConfig.xml. Unused at runtime; lets a test tell "mod absent" apart from "marker names wrong". |
| `typeRules` | `List<WorldObjectTypeRule>` | Evaluated in order; first match wins. |
| `populationMembers` | `string[]` | Candidate member names holding population (int or collection). |
| `levelMembers` / `maxLevelMembers` | `string[]` | Candidate member names for the mod's upgrade level and its cap. |
| `assumedMaxLevel` | `int` | Fallback max level; 0 means "unknown" and the level is ignored for tiering. |
| `playerOwnedByDefault` | `bool` | Empire-style player holdings. |
| `enabledGetter` | `Func<bool>` | Player-facing toggle; null means always on when present. |

`Rule` signature: `WorldObjectAdapterProfile Rule(TypeMatch match, string value, WorldObjectKind kind)` where `TypeMatch` is `ExactType`, `NamespacePrefix`, `TypeNameContains`, or `DefNameContains`.

**Worked example:**

```csharp
var profile = new WorldObjectAdapterProfile
{
    adapterId = "frontier",
    displayName = "Frontier Nations",
    priority = 150,
    packageId = "someone.frontiernations",
    markerTypes = new[] { "Frontier.TownWorldObject" },
    populationMembers = new[] { "residentCount" },
}
.Rule(TypeMatch.ExactType, "Frontier.TownWorldObject", WorldObjectKind.Settlement)
.Rule(TypeMatch.ExactType, "Frontier.FortWorldObject", WorldObjectKind.Military);

WorldObjectAdapterRegistry.Register(new ReflectionWorldObjectAdapter(profile));
```

A caution earned by history: a wrong string here costs no error at runtime — it just returns a plausible zero forever. Read every name off the loaded assembly, prefer `ExactType` over substring matches, and verify against the live game. Typed adapters in a hard-dependent patch are the safer default.

---

## Holding creators

The write-side mirror of adapters: a **creator** builds a foreign mod's holding (core's worldgen outpost-seeding pass asks it to). Namespace `RegionsAndSocieties.Integration`. Core ships no creators; the VOE compatibility patch contributes the outpost creator.

### IHoldingCreator

```csharp
public interface IHoldingCreator
{
    string CreatorId { get; }    // stable id, matching the sibling adapter where one exists
    int Priority { get; }        // lower runs first
    bool IsActive { get; }       // mod loaded AND integration enabled

    bool CanCreate(WorldObjectKind kind);
    bool TryCreate(WorldObjectKind kind, OutpostArchetype archetype,
                   Faction faction, int tile, out WorldObject created);
}
```

`TryCreate` parameters:

| Parameter | Type | Meaning |
|---|---|---|
| `kind` | `WorldObjectKind` | What to build (currently `Outpost` is what the seeding pass requests). |
| `archetype` | `RegionsAndSocieties.Sizing.OutpostArchetype` | The shape core wants (mining, farming, ...), where the kind supports one. |
| `faction` | `Faction` | The owner. |
| `tile` | `int` | The world tile to stand on. |
| `created` | `out WorldObject` | The built object, or null. |

**Returns:** `true` with `created` non-null on success; `false` to decline. Never throw — the registry guards and moves on.

### HoldingCreatorRegistry

| Member | Signature | Notes |
|---|---|---|
| `Register` | `void Register(IHoldingCreator creator)` | Call from your patch's `Mod` constructor. Duplicate `CreatorId` is dropped with a warning. |
| `AnyActiveFor` | `bool AnyActiveFor(WorldObjectKind kind)` | The seeding pass's precondition. |
| `TryCreate` | `bool TryCreate(WorldObjectKind kind, OutpostArchetype archetype, Faction faction, int tile, out WorldObject created)` | First active creator that builds the kind wins. |
| `Creators` / `Initialized` / `Clear` | as on the adapter registry | |

**Worked example:**

```csharp
public class FrontierOutpostCreator : IHoldingCreator
{
    public string CreatorId => "frontier";
    public int Priority => 150;
    public bool IsActive => true;   // hard-dependent patch: installed == on

    public bool CanCreate(WorldObjectKind kind) => kind == WorldObjectKind.Outpost;

    public bool TryCreate(WorldObjectKind kind, OutpostArchetype archetype,
                          Faction faction, int tile, out WorldObject created)
    {
        created = null;
        if (kind != WorldObjectKind.Outpost) return false;
        var outpost = (Frontier.OutpostWorldObject)WorldObjectMaker
            .MakeWorldObject(FrontierDefOf.FrontierOutpost);
        outpost.Tile = tile;
        outpost.SetFaction(faction);
        Find.WorldObjects.Add(outpost);
        created = outpost;
        return true;
    }
}

// in the patch's Mod constructor:
HoldingCreatorRegistry.Register(new FrontierOutpostCreator());
```

---

## Region partition algorithms (0.3.0)

How the globe is cut into land provinces is an extension point. Namespace `RegionsAndSocieties.Partition`. Core ships two — `contain_subdivide` (the default) and `anchor_voronoi` (the 0.2.x look); a mod contributes its own and it appears in the **World partition algorithm** dropdown in Regions and Societies' settings.

The chosen algorithm's `AlgorithmId` is stamped onto every world it generates, so the setting only affects **new** worlds — an existing save keeps the algorithm it was generated with, and a regenerate reproduces it. If a world's stamped algorithm is not registered (the mod that added it was removed), it falls back to the default.

### IRegionPartitioner

```csharp
public interface IRegionPartitioner
{
    string AlgorithmId { get; }   // stable id, persisted in saves + the settings value — never change once shipped
    string Label { get; }         // dropdown name
    string Description { get; }    // dropdown tooltip
    int Order { get; }            // dropdown sort; Core's default is 0

    // Water/impassable tiles are already claimed in tileToProvinceId (entries >= 0) and are hard walls;
    // return one tile list per land province. Must be deterministic from the world (regenerate fidelity).
    List<List<int>> Partition(int[] tileToProvinceId, int minRegionTiles, int maxRegionTiles);
}
```

### RegionPartitionerRegistry

| Member | Signature | Notes |
|---|---|---|
| `Register` | `void Register(IRegionPartitioner p)` | Call from your `Mod` constructor. Duplicate `AlgorithmId` is dropped with a warning. |
| `Get` | `IRegionPartitioner Get(string algorithmId)` | The match, or the default if the id is unknown (logged). |
| `All` / `Default` | `IReadOnlyList<IRegionPartitioner> All`, `IRegionPartitioner Default` | The dropdown reads `All`; `Default` is `contain_subdivide`. |
| `DefaultAlgorithmId` / `LegacyAlgorithmId` | `const string` | `"contain_subdivide"` / `"anchor_voronoi"`. |

**Worked example:**

```csharp
public class HexGridPartitioner : IRegionPartitioner
{
    public string AlgorithmId => "hexgrid";
    public string Label => "Uniform hex grid";
    public string Description => "Ignores terrain; cuts the globe into equal hex cells.";
    public int Order => 20;

    public List<List<int>> Partition(int[] tileToProvinceId, int minRegionTiles, int maxRegionTiles)
    {
        // ... flood the unclaimed land (tileToProvinceId[t] < 0) into groups, never crossing a claimed
        //     (>= 0) tile, and return one List<int> per province ...
    }
}

// in your Mod constructor:
RegionPartitionerRegistry.Register(new HexGridPartitioner());
```

Downstream cleanup (contiguity enforcement, tiny-region merging, ownership) runs on whatever groups you return, so a partitioner only has to produce reasonable land groups — it does not have to be perfect.

---

## Demographic providers

Namespace `RegionsAndSocieties` (root). A provider contributes the **demographics component** of a province's ownership score — "what share of this region's people match this faction".

### IRegionDemographicProvider

```csharp
public interface IRegionDemographicProvider
{
    string ProviderName { get; }
    float GetDemographicMatchRatio(GeographicProvince province, Faction faction);
}
```

| Parameter | Type | Meaning |
|---|---|---|
| `province` | `GeographicProvince` | The province being scored. |
| `faction` | `Faction` | The faction being scored in it. |

**Returns:** a match ratio, expected in 0..1.

### RegionalDemographicRegistry

| Member | Signature | Notes |
|---|---|---|
| `RegisterProvider` | `void RegisterProvider(IRegionDemographicProvider provider)` | Null and duplicates ignored. |
| `HasProviders` | `bool HasProviders { get; }` | |
| `GetCombinedDemographicScore` | `float GetCombinedDemographicScore(GeographicProvince province, Faction faction)` | Average of all providers, clamped 0..1. Returns **-1** when no providers are registered — the ownership calculation treats -1 as "component absent" rather than "zero match". |

A provider that throws is logged and contributes 0 for that call; it never breaks the score.

**Worked example:**

```csharp
public class BeliefMatchProvider : IRegionDemographicProvider
{
    public string ProviderName => "MyMod belief match";

    public float GetDemographicMatchRatio(GeographicProvince province, Faction faction)
    {
        var ideo = faction?.ideos?.PrimaryIdeo;
        if (ideo == null) return 0f;
        return MyRegionBeliefs.ShareOf(province.id, ideo);   // your data, 0..1
    }
}

// at load:
RegionalDemographicRegistry.RegisterProvider(new BeliefMatchProvider());
```

---

## Territory-claim hook

Namespace `RegionsAndSocieties.Integration`, class `TerritoryClaimHooks`. Raised when the **player** plants a settlement in a province a rival legitimately claims (>=30%).

```csharp
public static Func<TerritoryClaimContestedArgs, bool> Handler;
public static bool Fire(TerritoryClaimContestedArgs args);
public static int DefaultPenaltyFor(OwnershipTier tier);
```

`TerritoryClaimContestedArgs` fields:

| Field | Type | Meaning |
|---|---|---|
| `settler` | `Faction` | The faction that placed the holding (the player). |
| `claimant` | `Faction` | The strongest rival claiming the province. |
| `provinceId` | `int` | |
| `tier` | `OwnershipTier` | The claimant's ownership tier. |
| `claimStrength` | `float` | The claimant's score, 0..1. |

**Contract:** assign `Handler` after load to consume the event. Return `true` to **consume** it and suppress the default consequence; `false` (or leaving `Handler` unset) lets core apply its default goodwill penalty to every legitimate claimant: -15 for a legitimate claim (30–50%), -40 for loose ownership or better (>=51%). Only one handler slot exists; last assignment wins.

**Worked example** — a storyteller mod reacting instead of the flat penalty:

```csharp
TerritoryClaimHooks.Handler = args =>
{
    MyStoryteller.QueueBorderIncident(args.claimant, args.provinceId, args.claimStrength);
    return true;   // consumed: no default goodwill penalty
};
```

---

## Ownership vocabulary

Namespace `RegionsAndSocieties` for the tiers, `RegionsAndSocieties.Placement` for the rules. Read-only surfaces a consumer can rely on.

### OwnershipTier and RegionalDomainUtility

```csharp
public enum OwnershipTier { LooseClaim, LegitimateClaim, LooseOwnership, Exclusive }

public static OwnershipTier RegionalDomainUtility.TierOf(float score);
public static ProvinceDomainStatus RegionalDomainUtility.GetDomainStatus(RegionalOwnershipData data);
public static Faction RegionalDomainUtility.GetDominantOwner(RegionalOwnershipData data);
public static List<FactionOwnershipScore> RegionalDomainUtility.LegitimateClaimsOrdered(RegionalOwnershipData data);
public static FactionOwnershipScore RegionalDomainUtility.ExclusiveOwner(RegionalOwnershipData data);
public static string RegionalDomainUtility.GetStatusDescription(RegionalOwnershipData data);
```

Tier cutoffs (from `PlacementRules`): below 30% loose claim, 30–50% legitimate claim, 51–70% loose ownership, >=71% exclusive. `ProvinceDomainStatus` is `Wilderness` / `DominantOwner` / `Contested` / `Conflict`.

### PlacementRules

The numeric placement rules in one table — constants, safe to read:

| Constant | Value | Meaning |
|---|---|---|
| `MaxSupplyDistance` | 8 | Max tiles a new permanent holding may sit from its faction's nearest holding or border. |
| `PermanentHoldingSeparation` | 2 | Minimum traversal distance between permanent holdings. |
| `OwnershipThreshold` | 0.30 | Legitimate-claim floor. |
| `LooseOwnershipThreshold` | 0.51 | Clear-majority floor. |
| `ExclusiveThreshold` | 0.71 | Exclusive-owner floor; blocks even a player start. |
| `ContestMargin` | 0.10 | Runner-up proximity that makes a province contested. |
| `PresenceFloor` | 0.05 | Below this a faction has no meaningful presence at all. |

Predicates: `MinSeparation(WorldObjectKind a, WorldObjectKind b)`, `RequiresSupplyLine(kind)`, `RequiresAdjacentFoothold(kind)`, `BlockedByForeignTerritory(kind)` — all keyed on `IsPermanentHolding`; camps are exempt by design.

---

## Settlement tiers

Namespace `RegionsAndSocieties.Sizing`.

```csharp
public enum SettlementTier { None = 0, Village = 1, Town = 2, City = 3, MajorCity = 4, Metropolis = 5 }
```

The tier is **derived, never stored**, so it cannot go stale, and it means the same thing for a vanilla settlement, an Empire colony, or a VOE outpost — adapters feed it through `TryGetLevel` and population. Extensions: `Label()`, `LabelCapitalized()`, `IsAtLeast(SettlementTier other)`, `Max(SettlementTier b)`.

---

## Regional demographics read API (0.2.0)

Namespace `RegionsAndSocieties.Demographics`. Everything here is **derived on read** from the world seed and current world state — deterministic, cached against the population cache version, never scribed.

### RegionDemographicsUtility.ForRegion / ForFaction

```csharp
public static RegionDemographics ForRegion(GeographicProvince province)
public static RegionDemographics ForFaction(Faction faction)
```

Returns the aggregated make-up of a region (or a faction's whole territory). Never null; a region with `settledTiles == 0` is wilderness. `RegionDemographics` fields:

| Field | Type | Meaning |
|---|---|---|
| `tileCount`, `settledTiles` | `int` | Region size and how many tiles are under demographic pressure. |
| `factionShares` | `Dictionary<Faction,float>` | Dominant-pressure owner share per tile. |
| `raceShares`, `medianWealthByRace` | `Dictionary<XenotypeDef,...>` | Xenotype mix (#12). Empty with Biotech off. |
| `ideoShares` | `Dictionary<Ideo,float>` | Primary + minor ideoligion shares (#13). Empty with Ideology off. |
| `memeShares` | `Dictionary<MemeDef,float>` | Meme-level belief mix. |
| `femaleFraction` | `float` | Sex ratio (#11), baseline ~0.5 plus any hook-driven skew. |
| `ageShares[3]`, `medianAge` | `float[]`, `int` | Child / working-age / elder shares and median age (#10). Index by `(int)AgeBucket`. |
| `educationShares[4]`, `educationIndex` | `float[]`, `int` | Illiterate→advanced shares and 0–100 index (#15). Index by `(int)EducationTier`. |
| `sesShares[4]`, `sesIndex` | `float[]`, `int` | Subsistence→affluent shares and 0–100 index (#14). Index by `(int)SesTier`. |
| `occupationShares[4]`, `employmentRate` | `float[]`, `int` | Agriculture/industry/military/trade mix and 0–100 rate (#16). Index by `(int)OccupationSector`. |
| `overallMedianWealth` | `int` | Silver-ish median wealth. |
| `biotechActive`, `ideologyActive` | `bool` | Which DLC axes are live. |

Formatted one-call summaries (the same text the region panel and overlay tooltips show — null when the region is unsettled): `AgeStructureSummary`, `SexRatioSummary`, `XenotypeSummary`, `IdeologySummary`, `EducationSummary`, `SocioeconomicSummary`, `EmploymentSummary` — each `(GeographicProvince) → string`.

Cross-region comparison (#13): `MemeSimilarity(GeographicProvince a, GeographicProvince b) → float` (cosine of the meme-share vectors, 0..1) and `AverageNeighborSimilarity(GeographicProvince) → float` (mean similarity to adjacent land regions; −1 when no comparable neighbour).

Example — a spread mod deciding whether a region would welcome a faction's culture:

```csharp
var demo = RegionDemographicsUtility.ForRegion(province);
bool educated = demo.educationIndex > 60;
float similarity = RegionDemographicsUtility.AverageNeighborSimilarity(province);
```

## Demographic hooks (write side, 0.2.0)

`RegionsAndSocieties.Demographics.DemographicHooks` — the seam a companion mod (a drafting system, a war mod, a storyteller) drives to bend a region's sex ratio over time. Core models the deterministic baseline and owns nothing about drafting or war; skews are sparse per-region overrides, persist through save/load, and decay on the world tick.

```csharp
// A draft is in progress: shift the female fraction and HOLD it until EndDraft.
// Positive delta = men pulled away (the default men-first draft); negative = women first.
public static void BeginDraft(int regionId, float femaleDelta, string tag = "draft")
public static void EndDraft(int regionId, string tag = "draft")

// A battle's toll: a lopsided loss leaves the region short of that sex, recovering
// linearly over the configured generation length (default 15 in-game years, player slider).
// Repeat calls compound the scar and restart its recovery.
public static void RecordCombatLosses(int regionId, int maleDeaths, int femaleDeaths)

// Raw escape hatch: durationTicks 0 = hold until cleared; >0 = linear decay to zero.
public static void SkewSexRatio(int regionId, float femaleDelta, int durationTicks = 0, string tag = null)

// The net skew currently in force (delta on femaleFraction; 0 when unstressed).
public static float CurrentFemaleDelta(int regionId)
```

The pre-existing wealth stress (`RegionDemographicsStress.StressWealth(regionId, multiplier)`) is unchanged and composes with these.

## Territory compactness (0.2.0)

How faction domains avoid spidering, exposed so expansion mods rank candidate provinces with the same metric core uses. Pure maths in `RegionsAndSocieties.Placement.CompactnessRules`; world reads in `RegionsAndSocieties.TerritoryCompactnessUtility`.

```csharp
// Fraction of a candidate's claimable borders (Land-type neighbours only — ocean and
// impassable ranges are geography's free wall) the faction already holds, 0..1.
public static float TerritoryCompactnessUtility.Embeddedness(GeographicProvince candidate, Faction faction)

// A whole domain's shape, 0..1: 1 = closed blob, near 0 = pure spider.
public static float TerritoryCompactnessUtility.DomainCompactness(Faction faction)

// The scoring rule (pure): suitability scaled toward (embeddedness/desiredRatio) below the
// ratio, blended by weight (0 = ignore shape). CompactnessRules.DefaultDesiredRatio = 0.4.
public static float CompactnessRules.EffectiveScore(float suitability, float embeddedness,
                                                    float desiredRatio, float weight)
```

The player-facing weight is `FactionPlacementSettings.territoryCompactness` (0..1). An expansion mod choosing its next province should rank by `EffectiveScore(suitability, Embeddedness(p, faction), CompactnessRules.DefaultDesiredRatio, FactionPlacementSettings.territoryCompactness)` to inherit the player's setting.

## Settings surfaces

Two public static settings classes. Consumers should read the **composed gates**, not the raw fields, so the master switch is always respected.

### WorldObjectIntegrationSettings (`RegionsAndSocieties.Integration`)

| Gate property | Meaning when true |
|---|---|
| `PlacementGovernanceActive` | Foreign placement is gated on ownership and supply range. |
| `EconomyGovernanceActive` | Production modifiers apply. |
| `MilitaryGovernanceActive` | Adjacency/supply restrictions on military actions apply. |
| `SettlementTiersActive` | Village→metropolis tiering runs. |
| `OutpostSeedingActive` | Outpost seeding runs (needs a creator from a patch). Worldgen seeding is deferred to 0.4.0, so the switch is inert in 0.3.0. |
| `PopulationCapsActive` | The per-tier population-cap model runs. |

Raw fields (`masterEnabled`, `placementGovernance`, ..., `populationCapMultiplier`, `demographicReach`, `demographicFalloff`, `demographicFalloffModel`, `demographicGenerationYears` (how many in-game years a generational sex skew takes to decay; default 15), `logUnknownWorldObjects`) are public and persisted, but flip them only from a settings UI acting for the player.

### FactionPlacementSettings (`RegionsAndSocieties`)

Public statics a patch may read: `claimedLandAreaPercent` (the worldgen density knob), `territoryCompactness` (how strongly domains prefer squaring over spidering, 0..1 — see Territory compactness above), `strictTerritorialOwnershipDefault` (whether newly generated worlds enforce placement rules; in-progress worlds decide on load — see [Save Compatibility](Save_Compatibility)), `minRegionSize` / `maxRegionSize`, `showCalculationBreakdowns` / `ShowCalculations`, and the per-faction `FactionPlacementProfile` table via `GetProfile(FactionDef def)`.

---

## RimSynapse Core bridge (outbound)

When RimSynapse Core is installed, this mod publishes its population-density capability to Core's provider registry and registers read-only region introspection tools (`RegionMcpTools`) with Core's game-tool bridge — all by reflection, with no assembly reference in either direction. This is an outbound integration, not an extension point of this mod; it is listed here so the startup log lines make sense.
