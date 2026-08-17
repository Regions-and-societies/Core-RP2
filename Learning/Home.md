# Regions and Societies

Welcome to the documentation for **Regions and Societies**, a standalone world-map population and territory layer. It is the continuation of *Regions and Territories* (formerly published under the RimSynapse name) under a new identity: new mod name, new package ID (`RegionsAndSocieties.Core`), new repository ([Regions-and-societies/Core-MMF](https://github.com/Regions-and-societies/Core-MMF)).

This mod divides the planet into geographic provinces, works out who actually holds each one, and governs where new world objects may be placed. It is built to coexist with the other major world and faction mods rather than replace them — as of 0.1.0, that coexistence is delivered through dedicated **companion compatibility patches** rather than baked into this mod.

## Table of Contents

- [Regions and Societies Overview](Regions_and_Societies_Overview)
- [Player's Guide](Players_Guide)
- [Developer's Guide](Developers_Guide)
- [Compatibility Matrix](Compatibility_Matrix)
- [World Object Integration](World_Object_Integration)
- [Territory Ownership](Territory_Ownership)
- [Map Modes](Map_Modes)
- [Save Compatibility](Save_Compatibility)
- [Changelog](Changelog)

---

## Before you start

- **Requires Map Mode Framework.** The overlays will not draw without it.
- **Best on a new colony.** An existing save is adopted in compatibility mode; see [Save Compatibility](Save_Compatibility).
- **This is a new mod, not an update.** The package ID changed in the rebrand, so Regions and Societies installs side by side with the old Regions and Territories. Saves made with the old package ID are not migrated.
- **Foreign-mod support is separate.** Support for Empire Refactored, World Domination, the Vanilla Expanded framework and Vanilla Outposts Expanded ships as companion compatibility-patch mods. Core alone classifies only vanilla world objects (plus conservative name heuristics for unknown mods).

RimSynapse Core is **optional**. This mod runs standalone; when Core is present it registers its capabilities with it and the two cooperate. The old *RimSynapse - Factions* load-order rule does not apply here — Factions bound the former assembly and does not bind this mod.
