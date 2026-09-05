# Compatibility Matrix

This mod patches world generation, tile ownership, roads, ruins placement and map-mode overlays — all surfaces other world mods also touch. This page records how compatibility is delivered and what has actually been run together.

Since 0.1.0 the model is inverted: **core carries no foreign-mod knowledge**. On its own it classifies vanilla world objects (settlements, caravans, sites), plus conservative name heuristics for mods nobody has written a patch for. Everything beyond that comes from a companion **compatibility patch** — a small mod that hard-depends on both core and its target and plugs into core's registration API. Installing the patch is the enable switch. See [World Object Integration](World_Object_Integration).

---

## Requirements

**Map Mode Framework** (`NozoMe.MapModeFramework`) — a hard dependency, not merely compatible. Overlays do not draw without it. (This is the MMF edition; a separate edition exists for Realistic Planets 2.)

---

## Supported through companion patches

Each of the following is supported by its own patch mod under the [Regions-and-societies](https://github.com/Regions-and-societies) organisation. Core alone does none of this.

**Empire Refactored** (`Matathias.Empire`) — via **Empire-CP**.
- Settlement population is read through the patch's adapter and feeds population density and tiers.
- Production and reward figures are extended rather than replaced.
- Empire settlements are player-founded; this mod does not generate them.

**Vanilla Outposts Expanded** (`vanillaexpanded.outposts`) — via **VOE-CP**.
- Outposts are recognised and count toward territorial claims.
- The patch also contributes the outpost *creator* that outpost seeding will use; worldgen seeding itself is deferred to 0.4.0, so in 0.3.0 the creator is registered but not called at world generation.
- Outposts are otherwise player-founded from a caravan, so a freshly generated world having none is correct, not a fault.

**Vanilla Expanded Framework** (`OskarPotocki.VanillaFactionsExpanded.Core`) — via **VFE-CP**.
- Contributes exactly one world object of its own, a moving base, which is classified as a caravan. A base that moves cannot hold a province stably.
- Note that VOE now ships inside this framework, so its outpost types arrive from VEF's assembly while belonging to the VOE patch.

**World Domination 2.0** (`TSA.WorldDominationExperimental`) — via **World-Domination-CP**.
- Outposts and travelling parties are recognised. Travellers are classified as caravans — an in-flight raid is not a territory-holding settlement.
- Settlement grade is encoded in def names rather than a numeric field, so no level ladder is read for this mod.

The behaviour described above was observed with all listed mods loaded simultaneously under the predecessor's integrated builds; the patches carry the same adapters forward.

---

## Without a patch

A world mod with no patch is not invisible: core's name heuristics still classify objects whose type or def names contain "Settlement", "Outpost", "Garrison" or "Camp", so they are governed conservatively. Anything the heuristics cannot place is treated as inert and, with the diagnostic setting on, logged once per type — which is exactly the information needed to write a patch for it.

---

## Load order

Core must load after Map Mode Framework and after any target mods (its `About.xml` declares this). Each compatibility patch loads after both core and its target — the patches declare that themselves.

RimWorld obeys the order written in your mod list. A declared dependency is advisory; it does not reorder anything for you. If you sort your mod list alphabetically, check these afterwards.

**The old RimSynapse - Factions load-order rule no longer applies.** Factions bound the former assembly name and does not bind this mod; any future Factions integration would arrive as a compatibility patch like the others.

No ordering constraint has been observed against Empire, VOE, VEF, World Domination or Map Mode Framework beyond the above.

---

## Known incompatibilities

- **Layered Atmosphere and Orbit (LAO)** (`MrHydralisk.LayeredAtmosphereOrbit`) — **compatible as of 0.3.0.** It was `incompatibleWith` in 0.2.3: LAO's `WorldDrawLayer.Visible` render patch dereferences each layer's planet layer, and this mod's region-border and capital overlays are *global* draw layers with none, so LAO null-referenced and black-worlded the map. Those overlays are now pinned to the surface layer, so LAO renders cleanly; the incompatibility has been removed. (LAO requires the Odyssey DLC.)

---

## Known limits

- **Best on a new colony.** An existing save is adopted in compatibility mode; see [Save Compatibility](Save_Compatibility).
- **Broader modlist coverage is not yet characterised.** The mods above are what has been run and verified together. Other world mods are not known to conflict — they are simply untested, which is a different statement.
- **Duplicated biome plants from other mods are repaired, not fatal.** Since 0.3.1, a biome that lists the same wild plant or animal twice (ReGrowth 2 and Fertile Planet both add pincushion cactus and drago trees to Extreme Desert, for example) is merged at startup with a logged warning naming the biome and the def. Before 0.3.1 that duplicate made world generation produce a world with no factions.

---

## Reporting a conflict

Useful reports include the full mod list in load order, the `Player.log` from the run, and whether the problem survives moving this mod earlier or later in the list. Ordering problems and genuine incompatibilities look identical from the outside, and that one detail separates them. Report against [Core-MMF](https://github.com/Regions-and-societies/Core-MMF/issues) unless the problem clearly lives in one of the patches.
