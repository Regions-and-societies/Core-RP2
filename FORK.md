# Regions and Societies — Realistic Planets 2 edition

This repo is a **fork** of [Regions-and-societies/Core-MMF](https://github.com/Regions-and-societies/Core-MMF),
published as a separate Workshop mod (id `3784666526`) for players who use **Realistic Planets 2**
(`koth.RealisticPlanets2`). It is the successor to `RimSynapse/Regions-and-Territories-RP2`,
which served the same purpose during R&T 0.8.

## Why two editions

RP2 does not ship a rival framework — it ships a fork of Map Mode Framework (same
`namespace MapModeFramework`, compiled into `Realistic_Planets_2.dll`) plus a type-forwarding
shim `MapModeFramework.dll` that forwards every public type into the fork. RP2 declares
`<incompatibleWith>NozoMe.MapModeFramework</incompatibleWith>`, so exactly one framework is
ever loaded. Because the shim keeps both the assembly name and the namespace, our compiled
`using MapModeFramework` references bind to either framework at runtime with no recompile —
the same `RegionsAndSocieties.dll` runs on both. The editions differ in *declared identity
and dependencies*, not in code.

## The invariant that must never break

Both editions ship the **same assembly name (`RegionsAndSocieties`) and the same public API**.
Companion compatibility patches (Empire-CP, VFE-CP, VOE-CP, World-Domination-CP) bind core by
assembly name with hard references, no reflection — so they hook whichever edition the player
has, **as long as neither edition renames the assembly or changes that public surface**. Do
not rename `AssemblyName`. Do not change signatures the companions consume without changing
them in both editions.

The two editions declare each other (and NozoMe's framework) `incompatibleWith`, because the
shared assembly name means only one may load at a time.

## What differs from upstream

**Only `About/` identity and this file:**

- `About/About.xml`: name, packageId (`RegionsAndSocieties.CoreRP2`), url, the hard
  `koth.RealisticPlanets2` dependency instead of MMF's, the `incompatibleWith` block, and
  `supportedVersions` (1.6 only, matching RP2).
- `About/PublishedFileId.txt`: this edition's Workshop item (`3784666526`), not MMF's.
- `About/steam_description.txt` / workshop imagery: rewritten per edition at release time.
- `FORK.md` (this file).

Everything else is upstream code. All RP2-specific runtime accommodations proven in R&T 0.8
were upstreamed into Core-MMF before the fork, so the *source* carries no RP2-only patches:

- `Source/MapModes/RegionPropertiesAccess.cs` reads `MapModeDef.RegionProperties` reflectively
  (RP2's shim does not forward `RegionProperties`; a direct TypeRef throws `TypeLoadException`
  under RP2 at JIT time).
- `Patch_Page_CreateWorldParamsRP_DoWindowContents` surfaces the Faction Geography button on
  RP2's replacement Create-World page, resolved reflectively and self-skipping without RP2.
- Def gating via `MayRequireAnyOf="NozoMe.MapModeFramework,koth.RealisticPlanets2"`.
- Region growth is value-budget-bounded (`GrowTerrainBoundedRegions`), so worldgen completes
  on RP2's natively resized planets (~1.77M tiles) without hanging or exhausting memory.
- `Patch_MapModeUI_DrawSettings` resolves its target dynamically and self-skips where RP2's
  fork deleted `MapModeUI.DoDrawSettingsExpanded`.

The build references NozoMe's `MapModeFramework.dll` (Workshop 3296654393) at compile time in
both editions; the shim makes that binding valid under RP2.

## Sync workflow — "pull upstream and deconflict"

`core-mmf` points at the standard repo. To pull a release forward:

```bash
git fetch core-mmf --tags
git merge <tag>
# resolve conflicts — expected only in About/ (keep this edition's identity:
# packageId, name, dependencies, incompatibleWith, PublishedFileId)
```

Then rebuild and redeploy. Keeping this edition's divergence confined to `About/` is what
keeps the deconflict cheap — resist adding RP2-only code here; push shared changes upstream
instead.
