# World Object Integration

This mod has to reason about settlements, outposts, camps and military installations that belong to **other mods**, without hardcoding which mods exist. That is what the integration layer does.

As of 0.1.0 the model is **inverted**: core carries no knowledge of any foreign mod. It ships the classification contract, a vanilla adapter, and a public registration API. Knowledge of a specific mod lives in a dedicated **compatibility patch** — a small companion mod that hard-depends on both core and its target, and registers an adapter at load time.

---

## The problem it solves

Before this layer, territory rules matched on type names with string comparisons scattered through the code. Every new world mod meant another special case, and a mod nobody had special-cased was invisible to the territory system.

Now there is one classifier. Every world object is resolved to a **kind** — settlement, outpost, camp, military installation, caravan, site, ignored, or unknown — and the rules only ever ask about kinds.

---

## How classification works

`WorldObjectClassifier.Classify` is the single place the mod asks "what kind of thing is this world object?". Resolution order:

1. **Registered adapters, in priority order.** Mod-specific knowledge wins. Mod adapters use priorities 100–199; the vanilla adapter runs last at 1000.
2. **Generic name heuristics** — type or def names containing "Outpost", "Settlement", "Garrison" or "Camp" — so an unknown mod's objects still get governed.
3. **Unknown**, logged once per type (when the diagnostic setting is on) so an unrecognised mod is easy to write a patch for.

Two opt-out gates come before the heuristics: an object belonging to a mod whose integration the player switched off is `Ignored` rather than recaptured by name matching, and with the master integration switch off only vanilla-derived objects are governed at all.

Data queries (population, upgrade level) are gated on classification: an adapter is only asked about objects it recognises, so one mod's generic member name can never silently answer for another mod's object.

Every registry call is exception-guarded. A broken third-party adapter degrades that one integration; it never takes down world generation or a tick.

---

## Core's contract

Core ships, in the `RegionsAndSocieties.Integration` namespace:

| Type | Role |
|---|---|
| `WorldObjectKind` | The mod-agnostic classification enum all territory rules key off. |
| `IWorldObjectAdapter` | Per-mod translator between a foreign world object and the data governance needs. |
| `WorldObjectAdapterBase` | Convenience base class; every optional member no-ops. Derive from this, not the interface. |
| `WorldObjectAdapterRegistry` | Holds registered adapters in priority order and fans queries out to them. **`Register` is the public registration API.** |
| `VanillaWorldObjectAdapter` | The built-in default: classifies base-game settlements, caravans, sites and the ignorable objects. Always present, always last. |
| `WorldObjectClassifier` | The public query surface call sites use (`Classify`, `IsSettlement`, `IsTerritorial`, `GetPopulation`, ...). |
| `ReflectionWorldObjectAdapter` + `WorldObjectAdapterProfile` | An optional loose-binding convenience: a declarative, string-driven adapter for patches that prefer not to reference their target assembly. |
| `IHoldingCreator` + `HoldingCreatorRegistry` | The write-side mirror: creators *build* holdings (e.g. outposts seeded at worldgen) where adapters only read them. Core ships no creators of its own. |

The vanilla adapter guarantees base-game objects classify correctly even with every integration disabled or no patch installed.

---

## The compatibility patches

Foreign-mod support ships as companion mods under the [Regions-and-societies](https://github.com/Regions-and-societies) organisation:

| Patch repo | Target mod |
|---|---|
| `Empire-CP` | Empire Refactored |
| `World-Domination-CP` | World Domination 2.0 |
| `VFE-CP` | Vanilla Expanded framework (VEF) |
| `VOE-CP` | Vanilla Outposts Expanded |

A patch hard-depends on both core and its target, so it loads only when both are present — installing the patch *is* the enable switch. Each patch applies its own typed Harmony patches against its target and contributes its adapter (and, for VOE, an outpost creator) through the public registries.

The old model — a `KnownModProfiles` table of string-based reflection profiles inside core — is gone. Its hard-won lessons (three of four shipped profiles named members that did not exist, and nothing said so) are why the patches bind their targets **directly and typed**: a patch that references the real assembly fails to compile against a wrong name instead of silently returning zero.

---

## Writing a compatibility patch

The short version: make a tiny mod that hard-depends on core and the target, subclass `WorldObjectAdapterBase`, and register it from your `Mod` constructor.

**1. Declare the dependencies** in your patch's `About.xml`, and load after both:

```xml
<modDependencies>
    <li><packageId>RegionsAndSocieties.Core</packageId></li>
    <li><packageId>Some.TargetMod</packageId></li>
</modDependencies>
<loadAfter>
    <li>RegionsAndSocieties.Core</li>
    <li>Some.TargetMod</li>
</loadAfter>
```

**2. Write the adapter.** Reference both assemblies and name the target's types directly:

```csharp
using RegionsAndSocieties.Integration;
using RimWorld.Planet;

public class TargetModAdapter : WorldObjectAdapterBase
{
    public override string AdapterId => "targetmod";       // stable, unique
    public override string DisplayName => "Target Mod";
    public override int Priority => 150;                   // mod adapters: 100-199

    public override bool TryClassify(WorldObject obj, out WorldObjectKind kind)
    {
        kind = WorldObjectKind.Unknown;
        if (obj is TargetMod.FrontierOutpost) { kind = WorldObjectKind.Outpost; return true; }
        if (obj is TargetMod.FrontierTown)    { kind = WorldObjectKind.Settlement; return true; }
        return false;                                      // defer to the next adapter
    }

    public override bool TryGetPopulation(WorldObject obj, out int population)
    {
        population = 0;
        if (obj is TargetMod.FrontierTown town) { population = town.Residents.Count; return true; }
        return false;
    }
}
```

**3. Register it from your patch's `Mod` constructor:**

```csharp
public class TargetModPatch : Mod
{
    public TargetModPatch(ModContentPack content) : base(content)
    {
        WorldObjectAdapterRegistry.Register(new TargetModAdapter());
    }
}
```

Core initialises the registry in its own `Mod` constructor, and your patch loads after core, so `Register` merges your adapter into the priority order that is already live. Registering the same `AdapterId` twice logs a warning and keeps the first.

**Rules of thumb**, learned the hard way by the old profile table:

- Return `false` (leaving `kind` at `Unknown`) for anything that is not yours. The registry takes the first non-Unknown answer, so an over-broad adapter claims other mods' objects.
- Prefer exact type checks over name matching. If you must bind loosely, use `ReflectionWorldObjectAdapter` with a `WorldObjectAdapterProfile` — and read the real type and member names off the loaded assembly, never from documentation.
- Declare only data that genuinely exists. Answering `false` from `TryGetPopulation` is a truthful "this mod publishes no headcount"; reflecting on a member name that does not resolve is a plausible-looking zero forever.
- Override `IsActive`/`IsPresent` only if your patch has its own enable toggle; for a hard-dependent patch the defaults (always active) are correct, because being installed is the switch.

For the full API — including the write-side `IHoldingCreator` for mods whose holdings core should be able to *create*, and the demographic and territory-claim extension points — see the [Developer's Guide](Developers_Guide).
