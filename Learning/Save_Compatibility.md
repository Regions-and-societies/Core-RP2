# Save Compatibility

**Short version: start a new colony.** An existing save is adopted in compatibility mode rather than refused, but a world generated with the mod installed is the only way to get everything.

---

## Why a new colony

Provinces are built during **world generation**. A world that was generated without this mod has no province data, and there is no way to reconstruct it faithfully afterwards — the terrain-driven division that produces regions happens once, as the planet is made.

Adding this mod to an existing save therefore gives you a world with no regions, which means no territory, no ownership, and no meaningful overlays.

---

## Compatibility mode

Rather than refusing to load such a save, the mod **adopts** it.

When a save contains no province data, strict territorial ownership stands down: placement rules that depend on regions stop applying, so you are not suddenly unable to settle tiles that were legal yesterday. You are told when this happens rather than left to guess, and the mode is shown under *Strict territorial ownership* in the mod settings — it is decided once per save.

A save that **does** contain provinces keeps strict rules, unchanged.

Compatibility mode is a safety net, not a supported way to play. You get the mod loaded without breaking your colony; you do not get the features, because the data they need was never generated.

---

## Migrating from Regions and Territories

There is no migration. Regions and Societies has a **new package ID** (`RegionsAndSocieties.Core`), so it installs as a new mod side by side with the old Regions and Territories rather than updating it in place. A save that lists the old package ID keeps needing the old mod; swapping this one in does not adopt it.

If you are mid-colony on the old mod and want to stay there, finish that colony before switching. Translating saves across the rebrand is not planned; the effort is better spent on the world layer.

---

## Updating between versions of this mod

Ownership figures can shift when the scoring changes between releases — some provinces that read as **held** under one release may read as **unclaimed** or **contested** under the next. That is the model being corrected rather than data being lost, and any system reading ownership (including companion compatibility patches, such as Empire production figures) moves with it. Release notes in the [Changelog](Changelog) call out when a release changes what is stored on a province.
