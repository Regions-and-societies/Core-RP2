# Behaviour tests

195 assertions that run without RimWorld, Unity, Harmony, or a game install.

```
sudo apt-get install -y mono-mcs mono-runtime   # once, on WSL or Linux
Tests/run-tests.sh
```

Exit code is zero only if every suite builds and every assertion holds.

## Why this can exist at all

The 0.7 rules layers are deliberately dependency-free. `Source/Integration/`, `Source/Placement/`
and `Source/Economy/` contain no `Find`, no Harmony attributes, no Unity types and no `TechLevel`;
they receive world state as plain numbers or `Func` delegates, and exactly one façade file per
subsystem touches the game. That separation is the reason the rules can be compiled
against the hand-written doubles in `RimWorldStubs.cs` and executed anywhere. It is also the
precondition for 0.8's Logic Externalization, which needs to move one rule table per subsystem
rather than hunt constants through patch files.

## What is and is not covered

| Suite | Covers |
|---|---|
| `IntegrationTests` | world-object classification and the known-mod profile table |
| `PlacementTests` | where a faction may and may not put a holding |
| `ResourceTests` | resource pools, depletion, renewal, scanning and sustainable population |
| `ResidencyTests` | who counts as living in a generated dwelling, migration off Core's comp, and publishing the answer back to Core |

Sizing, production, taxation, military reach and standing moved to the **Factions** repo in 0.7
along with their code, and their suites went with them — see `Factions/Tests`. What stayed here is
the world layer: what a world object *is*, where it may stand, and what is in the ground under it.
`ResourceTests` is the half of the old `EconomyTests` that describes province state; the half that
described what a faction extracts is now `Factions/Tests/ProductionTests.cs`.

The type-check at the end of the run is not a behaviour test. It compiles the impure files —
`WorldObjectPlacementUtility`, `SettlementSizeUtility`, `ProductionScalingUtility`, `TaxationUtility`,
`MilitaryReachUtility`, `ProvinceAdjacency`, `FactionStandingUtility`, the Empire patch
— against stub signatures written from the real ones. It cannot tell you a patch is correct. It can
tell you a patch calls a method that no longer exists, which is otherwise invisible until RimWorld
loads the assembly and Harmony throws.

Not covered, and not pretended to be: anything that needs a live world. Worldgen, save/load round
trips, map modes, and whether another mod's reflection targets still resolve are all in-game checks.
`MapMode_GeographicProvinces` is outside even the type-check because it inherits from
MapModeFramework; player-facing wording was moved into `Economy/ResourceDisplay.cs` so the part that
can be tested is.

## Layout note

`Tests/` is a sibling of `Source/`, and the csproj lives in `Source/`. SDK-style projects glob
`**/*.cs` relative to the project directory, so nothing here is picked up by the mod build. Do not
move this folder under `Source/`.

The stubs are not a mock framework. They are the smallest real types that make the callers compile,
written from the actual RimWorld signatures the code meets. Where a stub returns a fixed value
(`GetProvinceForTile` returning null, `GetPopulationAtTile` returning 0), that is the case being
tested — the fallback path — not a shortcut.
