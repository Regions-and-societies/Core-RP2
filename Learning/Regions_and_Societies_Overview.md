# Regions and Societies Overview

This mod adds a world-map layer with three jobs: divide the planet into **provinces**, decide **who holds** each one, and govern **where new world objects may stand**.

Regions and Societies is the continuation of **Regions and Territories** (which shipped through v0.8.0) under a new identity, package ID and repository. See the [Changelog](Changelog) for what changed in the migration.

---

## Geographic provinces

At world generation the planet is divided into contiguous geographic provinces, built from terrain rather than drawn arbitrarily — biome, elevation, rivers and coastline all shape where one region ends and the next begins. A generated world typically produces somewhere between 250 and 400 provinces, varying with planet size and seed.

A province is the unit everything else in this mod reasons about. It holds its tiles, the world objects standing on them, its resource pools, and the ownership picture described below.

---

## Who holds a region

Ownership is not a flag. Each faction present in a province is **scored**, and the score places the faction on a four-tier ladder — from a loose claim, through a legitimate claim and loose ownership, up to an exclusive hold. Two factions can both hold legitimate claims, in which case the province is **contested** rather than owned.

Scores come from several independent components — the settlements and military installations present, how much of the province perimeter each faction sits nearest to, and the outposts and camps supporting them. A province where nobody clears the claim threshold stays **unclaimed**, and unclaimed is a real state rather than a rounding artefact.

See [Territory Ownership](Territory_Ownership) for the detail.

---

## Placement governance

One evaluator decides whether a world object may stand on a given tile, and every placement path routes through it — settling, outpost building, and worldgen placement all ask the same question so they cannot disagree about the same tile.

The rules cover:

- **Buffer distance** between permanent holdings, so settlements do not stack on top of each other.
- **Foreign territory**, so you are not quietly founding inside somebody else's claim.
- **Supply range**, so holdings stay within reach of what supports them.
- **Sequential expansion**, so territory grows outward rather than appearing in disconnected patches.

When a tile is refused, the world inspect pane tells you **why**. A refusal without a reason is a bug; please report one if you see it.

Camps are deliberately exempt from the separation rule — an expeditionary camp pitched beside a settlement is the point of a camp, not a mistake.

---

## World-object integration

The territory system has to reason about settlements, outposts and camps that belong to **other mods**. As of 0.1.0, core carries no knowledge of any specific foreign mod: it classifies vanilla objects itself and exposes a public **adapter registration API** that dedicated compatibility-patch mods call at load time. Support for Empire Refactored, World Domination, the Vanilla Expanded framework and Vanilla Outposts Expanded ships as separate companion patches, not inside this mod.

See [World Object Integration](World_Object_Integration) for the model and the [Developer's Guide](Developers_Guide) for the API.

---

## What this mod does not do

The boundary matters, because it is what keeps this from turning into one large mod:

- **This mod says what a world object is, who holds the region it stands in, and where new objects may stand.**
- **It does not simulate the factions themselves** — what a faction extracts, taxes, defends, or looks like from outside is left to other mods, which can read this mod's territory answers through the public extension points.
- **It does not bind to any foreign world mod directly.** That knowledge lives in the companion compatibility patches.

RimSynapse Core is **optional**. This mod runs standalone; when Core is present it publishes its population-density capability to it by reflection, and the two cooperate. The living-inhabitants layer (dwellings and residents) that earlier versions carried moved to the Living World companion mod during the predecessor's 0.8 cycle; core keeps only the abstract per-tile population count.
