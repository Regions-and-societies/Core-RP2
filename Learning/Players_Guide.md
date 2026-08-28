# Player's Guide

What Regions and Societies does in your game, mechanic by mechanic. For the API side, see the [Developer's Guide](Developers_Guide).

---

## Geographic provinces

At world generation the planet is divided into contiguous provinces, shaped by terrain — biome, elevation, rivers and coastline — rather than drawn arbitrarily. A typical world produces 250–400 of them, varying with planet size and seed. A province starts life as "Region <id>" and is renamed to "<settlement> Region" once a settlement stands in it.

The province is the unit everything else reasons about: its tiles, the world objects standing on them, its population, and who holds it. See the [Overview](Regions_and_Societies_Overview).

## Territory ownership: the four-tier ladder

Who holds a province is a score, not a flag. Each faction present is scored on its settlements, its reach over the province's perimeter, and its outposts and camps, and the score places it on a ladder:

| Tier | Score | Meaning |
|---|---|---|
| Loose claim | below 30% | A presence, not a claim. |
| Legitimate claim | 30–50% | Real claim; contestable by other legitimate claims. |
| Loose ownership | 51–70% | Clear majority owner, still short of exclusive. |
| Exclusive | 71% and up | Owns the province outright — blocks even a player start there. |

A province with two or more legitimate claims and no majority owner is **contested**; one where nobody reaches a legitimate claim is **unclaimed wilderness**, which is a real state, not a rounding artefact. The expanded region details spell the status out in these terms. See [Territory Ownership](Territory_Ownership) for how the score is built.

## Map mode overlays

World-map overlays, drawn through the **Map Mode Framework** (a hard requirement — nothing draws without it): **Geographic Provinces** (the region boundaries and what each contains), **Faction Territory** (provinces colour-coded by holder, contested provinces shown as contested), **Population Density** (a terrain- and road-aware gradient of where people actually are), and — new in 0.2.0 — seven **demographic overlays**: age structure, sex ratio, xenotypes, ideology, wealth, education and employment, each shading every settled region by that axis. Details and troubleshooting in [Map Modes](Map_Modes).

On top of the modes, an owner-coloured **region-border overlay** can be drawn over any map mode — solid in the owner's colour for a firmly held region, alternating claimants' colours where contested — toggled from the Draw Settings panel or the mod settings. Faction **capital markers** flag each faction's principal settlement.

## Region comparison panels

Modifier-click a region on the world map (Ctrl+click by default; switchable to Shift+click in the mod settings) to open a draggable readout of its population, ownership and details. Open several at once to compare — the limit is configurable, and the oldest panel closes first when you exceed it.

## Regional demographics (0.2.0)

Every region carries a demographic profile across seven axes — **age structure** (children / working-age / elders and a median age), **sex ratio**, **xenotypes** (Biotech castes), **ideology** (primary and minor ideoligions, and how similar a region's beliefs are to its neighbours'), **wealth** (subsistence through affluent), **education** (illiterate through advanced) and **employment** (agriculture / industry / military / trade, with an employment rate).

The profile is *derived, never stored*: it is computed from the world seed, the factions pressing on the region, their tech levels, ideoligions and xenotypes, and the land itself — so the same planet always carries the same people, and nothing bloats your save. Societies read plausibly by construction: a tribal region runs young, poor and agricultural; a spacer polity runs older, educated and industrial; a pro-natalist creed skews its lands toward children; a long-lived caste accumulates elders.

Each axis has its own map overlay (see [Map Modes](Map_Modes)), and the expanded region panel shows the full breakdown. Where a DLC is missing the model degrades honestly: without Biotech the xenotype overlay states "all Baseliner"; without Ideology every region is secular — the map never renders a flat overlay as if it were data.

The sex ratio is the one axis other mods can bend over time: a companion mod can report a **draft in progress** (the ratio skews while it lasts — men first, unless that culture drafts women first) or **combat losses** (a lopsided toll leaves the region short of that sex, recovering over a configurable number of in-game years — default 15). Nothing in your game changes unless a mod drives those hooks.

## Faction placement at world generation

Faction bases are placed by geography rather than scattered: each faction weighs minerals, nutrition, forage, grazing and hunting according to its tech level and temperament, settles as contiguous territory, and shies away from ground rivals already hold. One knob controls overall density — the **claimed land area** slider (in the mod settings and on the world-generation screen), the target share of livable land claimed by faction territories. Whatever the density and world size, placement always leaves at least one settleable land province unclaimed, so the player has somewhere to land.

Since 0.2.0 domains also prefer to **square off rather than spider**: growth favours provinces already embedded in the faction's territory — filling pockets before extending tendrils — controlled by the **Territory compactness** slider. It is a preference, never a rule: a faction pinned against an ocean still takes the awkward province when its land is dramatically better.

After generation the same evaluator governs every new placement — yours and the AI's. A new permanent holding must keep a buffer from existing ones, stay within supply range of its faction, and expand outward from an existing foothold; only a rival's **exclusive** (71%+) hold refuses your starting colony, and settling merely claimed ground is allowed — expect it to anger the claimant. When a tile is refused, the world inspect pane tells you why.

## Settlement tiers and capitals

Settlements are classified into tiers — village, town, city, major city, metropolis — from population and, where a companion patch supplies it, the owning mod's own upgrade level. The tier drives production scaling, territory footprint and outpost allowance, and each faction's capital carries a star marker. Toggleable in the mod settings.

## Outpost seeding

At world generation, outposts can be seeded around settlements up to each territory's tier-based allowance — but core cannot build another mod's outposts by itself. Seeding takes effect when a compatibility patch that contributes an outpost creator is installed (the Vanilla Outposts Expanded patch). Without one, a generated world carries only settlements, which is correct rather than a fault.

## Population caps

A model-only mechanic: each settlement's population drifts toward a cap derived from its tier, scaled by a player-tunable multiplier. It never adds or removes your real colonists — it shapes the world's numbers, not your colony roster.

## Companion compatibility patches

Core by itself recognises vanilla world objects. Support for **Empire Refactored**, **World Domination**, the **Vanilla Expanded framework** and **Vanilla Outposts Expanded** each comes from a separate companion patch mod — install the patch alongside core and its target mod, and the integration is on; there is no toggle beyond having it installed. See the [Compatibility Matrix](Compatibility_Matrix).

## Compatibility mode for existing saves

A world generated with the mod installed gets everything, including placement rules. A world already in progress is **adopted in compatibility mode**: provinces are drawn and territory is owned and shown, but placement stays with vanilla or whichever mod owns it, so you are never suddenly unable to settle tiles that were legal yesterday. The mode is decided once per save, you are told when it happens, and it is shown under *Strict territorial ownership* in the settings. A new colony is still the recommended way to play. See [Save Compatibility](Save_Compatibility).

## Mod settings

In Options → Mod settings → Regions and Societies:

- **Claimed land area** — the worldgen settlement-density knob (applies to newly generated worlds).
- **Territory compactness (squaring)** — how strongly territories prefer squaring off over spidering (0% = legacy behaviour).
- **Ownership calculation breakdown** — show the derivation readout in the region panel without Dev Mode.
- **Region panel modifier** — Ctrl+click or Shift+click, and how many comparison panels may be open at once.
- **World-object integration (master)** — off means only vanilla objects are governed.
- **Settlement tiers & capitals**, **Seed outposts at world generation**, **Population caps** — per-mechanic switches, with a cap multiplier slider.
- **Demographic pressure tuning** — reach and falloff sliders shaping how far a settlement's make-up carries, plus the **war/draft skew recovery** slider (how many in-game years a region's sex ratio takes to recover from combat losses; default 15).
- **Draw region borders on the world map** — the border overlay toggle.

Planet region size and placement rules are also configured on the world-generation screen.
