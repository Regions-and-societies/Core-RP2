# Player's Guide

What Regions and Societies does in your game, mechanic by mechanic. For the API side, see the [Developer's Guide](Developers_Guide).

---

## Geographic provinces

At world generation the planet is divided into contiguous provinces, shaped by terrain — biome, elevation, rivers and coastline — rather than drawn arbitrarily, each a single connected landmass. The number produced varies with planet size, sea level and the **Target region size** you set; a province starts life as "Region <id>" and is renamed to "<settlement> Region" once a settlement stands in it.

As of 0.4.0 the default region shape is a relaxed **honeycomb** — even, rounded cells that follow a biome's outline — rather than boxy cells. Two older looks remain selectable from the **World partition algorithm** dropdown: the 0.3.0 balanced-cells layout and the 0.2.x anchor-Voronoi boxes. The partition also reads the land more faithfully now: inland lakes are shared between their shores, small islands join the nearest mainland, tiny slivers are folded away, and a region no longer flows through a mountain pass or one-tile land bridge to span two places (see [Map corrections](#map-corrections) below).

Every world is stamped **Worldgen: v0.4.0** and records the partition settings it was generated with, so regenerating a seed reproduces the same map. The way the world is cut is selectable in the mod settings, and the region panel shows the worldgen version each world was made with; switching it only affects new worlds — an existing save keeps the algorithm and stamp it was made with and is never re-cut.

The province is the unit everything else reasons about: its tiles, the world objects standing on them, its population, and who holds it. See the [Overview](Regions_and_Societies_Overview).

## Map corrections

0.4.0 tidies the places where the old partition read the land badly, so a coastline or a mountain reads the way it looks:

- **Inland lakes are shared between their shores.** An enclosed body of water up to a size cap no longer rings itself with an unowned border; it is split across the surrounding regions along a clean midline that meets at the lake's centre, so a shoreline reads as one province's coast rather than a hole in the map.
- **Small islands join the mainland; archipelagos stand alone.** An island under ten tiles attaches to the nearest landmass instead of spinning off as its own speck. A chain of small islands that adds up to a real landmass (about thirty tiles or more) becomes a single archipelago region.
- **Tiny slivers are cleaned up.** One-to-six-tile scraps of land are folded into a neighbouring region — a settlement on such a scrap goes along with it — or dropped when truly isolated. Turn on **Enable small regions** (mod settings) to keep them instead as real, settle-able regions; they are too small to sustain a society, so they carry no demographics or economy.
- **Mountain passes and isthmuses split regions.** A region no longer flows through a narrow ridge saddle or a one-tile land bridge to span two separate places; the pass is read as a border and the region divides along it.

## Territory ownership: the four-tier ladder

Who holds a province is a score, not a flag. Each faction present is scored on its settlements, its reach over the province's perimeter, and its outposts and camps, and the score places it on a ladder:

| Tier | Score | Meaning |
|---|---|---|
| Loose claim | below 30% | A presence, not a claim. |
| Legitimate claim | 30–50% | Real claim; contestable by other legitimate claims. |
| Loose ownership | 51–70% | Clear majority owner, still short of exclusive. |
| Exclusive | 71% and up | Owns the province outright — blocks even a player start there. |

A province with two or more legitimate claims and no majority owner is **contested**; one where nobody reaches a legitimate claim is **unclaimed wilderness**, which is a real state, not a rounding artefact. The expanded region details spell the status out in these terms. See [Territory Ownership](Territory_Ownership) for how the score is built.

## Ownership earns its walls

Since 0.4.0, a faction that holds at least half of a region counts the region's own natural barriers — coasts, impassable rock and mountain ridges — as its secure border. A nation defined by its geography therefore reads as fully held right out to its cliffs and shorelines, instead of showing an open frontier where only tiles are scored. Below the half-share threshold nothing changes: the ordinary ladder above decides the status.

## Map mode overlays

World-map overlays, drawn through the **Map Mode Framework** (a hard requirement — nothing draws without it): **Geographic Provinces** (the region boundaries and what each contains), **Faction Territory** (provinces colour-coded by holder, contested provinces shown as contested), **Population Density** (a terrain- and road-aware gradient of where people actually are), and — new in 0.2.0 — seven **demographic overlays**: age structure, sex ratio, xenotypes, ideology, wealth, education and employment, each shading every settled region by that axis. Details and troubleshooting in [Map Modes](Map_Modes).

On top of the modes, an owner-coloured **region-border overlay** can be drawn over any map mode — solid in the owner's colour for a firmly held region, alternating claimants' colours where contested — toggled from the Draw Settings panel or the mod settings. Faction **capital markers** flag each faction's principal settlement.

## Region comparison panels

Modifier-click a region on the world map (Ctrl+click by default; switchable to Shift+click in the mod settings) to open a draggable, three-tab readout — **Region** (a shape mini-map, ownership, named world features, resource pools and wildlife), **Population** (the seven demographic axes as bars and pies), and **Economy** (manufactured goods, housing and any active crises). Open several at once to compare — the limit is configurable, and the oldest panel closes first when you exceed it.

## Regional demographics (0.2.0)

Every region carries a demographic profile across seven axes — **age structure** (children / working-age / elders and a median age), **sex ratio**, **xenotypes** (Biotech castes), **ideology** (primary and minor ideoligions, and how similar a region's beliefs are to its neighbours'), **wealth** (subsistence through affluent), **education** (illiterate through advanced) and **employment** (agriculture / industry / military / trade, with an employment rate).

The profile is *derived, never stored*: it is computed from the world seed, the factions pressing on the region, their tech levels, ideoligions and xenotypes, and the land itself — so the same planet always carries the same people, and nothing bloats your save. Societies read plausibly by construction: a tribal region runs young, poor and agricultural; a spacer polity runs older, educated and industrial; a pro-natalist creed skews its lands toward children; a long-lived caste accumulates elders.

Each axis has its own map overlay (see [Map Modes](Map_Modes)), and the expanded region panel shows the full breakdown. Where a DLC is missing the model degrades honestly: without Biotech the xenotype overlay states "all Baseliner"; without Ideology every region is secular — the map never renders a flat overlay as if it were data.

The sex ratio is the one axis other mods can bend over time: a companion mod can report a **draft in progress** (the ratio skews while it lasts — men first, unless that culture drafts women first) or **combat losses** (a lopsided toll leaves the region short of that sex, recovering over a configurable number of in-game years — default 15). Nothing in your game changes unless a mod drives those hooks.

## Faction placement at world generation

Faction bases are placed by geography rather than scattered: each faction weighs minerals, nutrition, forage, grazing and hunting according to its tech level and temperament, settles as contiguous territory, and shies away from ground rivals already hold. Overall density is set by the **Claimed land area** knob — the target share of livable land the factions collectively claim — now on the new-game Geographic Placement dialog. Whatever the density and world size, placement always leaves at least one settleable land province unclaimed, so the player has somewhere to land.

Since 0.4.0 factions also weigh a biome's real **habitability** — its movement difficulty, disease load and vanilla settlement weighting — not just its plant density, and avoid crowding onto biomes that are already full. Raiders stop homesteading on the ice sheet; boreal and temperate land fill in the way you would expect.

Since 0.2.0 domains also prefer to **square off rather than spider**: growth favours provinces already embedded in the faction's territory — filling pockets before extending tendrils — controlled by the **Territory compactness** slider. It is a preference, never a rule: a faction pinned against an ocean still takes the awkward province when its land is dramatically better.

After generation the same evaluator governs every new placement — yours and the AI's. A new permanent holding must keep a buffer from existing ones, stay within supply range of its faction, and expand outward from an existing foothold; only a rival's **exclusive** (71%+) hold refuses your starting colony, and settling merely claimed ground is allowed — expect it to anger the claimant. When a tile is refused, the world inspect pane tells you why. Since 0.3.2 that line appears once a tile has stayed selected for about a third of a second (so hopping across the map costs nothing), it is remembered per tile while the world is unchanged, and with Map Preview installed it waits until the preview has finished generating. The settle and outpost buttons check immediately, as before.

## The Geographic Placement dialog

New in 0.4.0, this dialog lets you set the world up **before** you generate it, reached from the world-generation screen. It gives every faction a **relative-size share** of the world's regions, shown against a live estimate so you can see the consequences as you tune.

- **Relative size.** Each faction gets a weight in the split of the land. In the **Basic** view this is a row of size presets (Tiny, Small, Med, Large, V.Large — roughly 1× through 16×); weights need not sum to anything, they normalise across whoever is present and then fill the claimed area. Every faction is guaranteed at least one region.
- **The "≈ N regions" estimate.** Each faction's row shows about how many regions its share works out to, and a global readout shows the expected region count and how much of the planet's capacity the factions are claiming (comfortable, crowded, or over-capacity with placement scaled down to fit). The estimate is computed from the real partition model and a calibrated planet-coverage curve, so what you see before generating matches what the world produces — it still varies a little with sea level.
- **The share pie.** A pie chart shows the split, viewable two ways: **by faction** (one slice each) or **by hostility** (grouped into hostile / neutral / friendly plus wilderness — the shape of the threat you will face).
- **Hostility dots.** A coloured dot beside each faction marks it hostile, neutral or friendly to the player.
- **Basic vs Advanced views.** Basic fits every faction on one screen — just the size each gets. **Advanced** exposes the full per-faction controls: resource weights (mineral, nutrition, forage, grazing, hunting, margin), clustering and kin, plus the world-object seeding and mod-integration governance. Advanced can be laid out as cards (one per faction) or as a dense table for comparing factions side by side.

The **Target region size** and **Claimed land area** knobs sit at the top of this dialog; a *Strict territorial ownership* status line points to the mod settings where that switch lives.

## Territory clustering and regional kin

Each faction now settles with a distinct **shape**, set per faction (Advanced view of the placement dialog):

- **Clustering.** Some factions sprawl into one contiguous nation; others hold a set number of separate footholds; pirates scatter. The **Number of clusters** knob caps how many separate footholds a faction spreads into, and **Min cluster size** sets the fewest regions a foothold must have to count. New footholds are kept spaced apart, so a faction reads as distinct clusters rather than a smear. The Empire is always a single cluster.
- **Regional kin sub-factions.** A scattered, low-tech faction that ends up in several corners of the world can split into related **kin factions**, one per region group, compass-named off the parent (for example *North* and *Southeast* of the base name) and friendly to one another. Each faction's row shows how many kin it will produce; **Split into regional kin** toggles it per faction (default on for pirates, tribes and rough unions; off for cohesive civilisations). A faction that resolves to a single cluster stays one faction even with kin on. Worldgen caps a world at 40 factions, so beyond that the last factions' kin are truncated.

## Settlement tiers and capitals

Settlements are classified into tiers — village, town, city, major city, metropolis — from population and, where a companion patch supplies it, the owning mod's own upgrade level. The tier drives production scaling, territory footprint and outpost allowance, and each faction's capital carries a star marker. Toggleable in the mod settings.

## World maturity and holding seeding

World generation can pre-place NPC outposts and other holdings around settlements, governed by the **World maturity** slider on the Geographic Placement dialog (labelled *NPC outposts & world objects to pre-place*). At **Off** it seeds none; at **Full** it pre-places each territory's whole allowance — a ready-to-play, fully-settled world. Intermediate settings give a part-built world between an empty frontier and a finished one. The setting is stamped per world, so regenerating the seed reproduces the same build-out.

Core cannot build another mod's outposts by itself: seeding takes effect only with a compatibility patch that contributes a holding creator (for example the Vanilla Outposts Expanded patch), and companion patches can register their own holding types. A generated world carrying only settlements is correct, not a fault.

## Region lock

The **region lock** governs whether a faction (and holding seeding) may settle inside a rival's territory: with it on, a settlement or outpost is refused in a region a rival holds **exclusively** (71%+); with it off, that hard refusal stands down while buffers, spacing, supply range and footholds still apply. It is a per-world setting and can be toggled **mid-game** from the mod settings (*Enforce region locks*). New worlds take their starting value from the corresponding default there.

## Settlement growth (0.3.0)

NPC settlements grow over time from in-game birthrates, fed by the region's real make-up (fertile share, wealth), converging on a target for their tier and never exceeding the tier maximum. Like the caps below, this is model-only — it shapes the world's numbers, never your colony roster — and, being part of the Societies layer, it stops entirely when the Societies master toggle is off.

## Population caps

A model-only mechanic: each settlement's population drifts toward a cap derived from its tier, scaled by a player-tunable **population cap multiplier** (and a separate growth-rate multiplier). It never adds or removes your real colonists — it shapes the world's numbers, not your colony roster. As of 0.4.0 there is no separate on/off checkbox (the model always applies when the Societies layer is on); the multipliers are shown directly in the mod settings, and are hidden entirely when the Societies master toggle is off.

## Societies master toggle

A single switch — **Societies: population, demographics & economy** in the mod settings — turns the entire Societies layer on or off. With it off you keep the geography: the partition, territories, borders, placement and their map modes all still work, but nothing models or draws population, demographics or economy, the demographic overlays and the Population/Economy panel tabs go away, growth and caps stop, and none of it ticks. It is the master gate every demographic and economic feature reads, so turn it off if you want only the map framework, or to save the load-time and tick cost.

## Companion compatibility patches

Core by itself recognises vanilla world objects. Support for **Empire Refactored**, **World Domination**, the **Vanilla Expanded framework** and **Vanilla Outposts Expanded** each comes from a separate companion patch mod — install the patch alongside core and its target mod, and the integration is on; there is no toggle beyond having it installed. See the [Compatibility Matrix](Compatibility_Matrix).

## Compatibility mode for existing saves

A world generated with the mod installed gets everything, including placement rules. A world already in progress is **adopted in compatibility mode**: provinces are drawn and territory is owned and shown, but placement stays with vanilla or whichever mod owns it, so you are never suddenly unable to settle tiles that were legal yesterday. The mode is decided once per save, you are told when it happens, and it is shown under *Strict territorial ownership* in the settings. A new colony is still the recommended way to play. See [Save Compatibility](Save_Compatibility).

An existing save also keeps the map it was made with: it holds its original worldgen stamp (0.4.0 stamps new worlds **Worldgen: v0.4.0**), and the 0.4.0 partition changes — the honeycomb shape, the lake/island/sliver/pass corrections — apply only to worlds generated under 0.4.0. Your live world is never re-cut under you.

## Mod settings

In Options → Mod settings → Regions and Societies:

- **Territory compactness (squaring)** — how strongly territories prefer squaring off over spidering (0% = legacy behaviour).
- **World partition algorithm** — how the globe is cut into regions (the honeycomb default, the 0.3.0 balanced-cells look, or the 0.2.x anchor-Voronoi boxes; other mods can add their own). Applies to newly generated worlds only — an existing save keeps the algorithm it was made with.
- **Biome region sizes…** — an editor to retune how big each biome's regions are (ice and desert default larger). Applies to newly generated worlds.
- **Region panel modifier** — Ctrl+click or Shift+click, and how many comparison panels may be open at once.
- **Societies: population, demographics & economy** — the master toggle for the whole Societies layer (see above).
- **Enable small regions (< 7 tiles)** — keep tiny 1–6 tile slivers as real, settle-able regions instead of dropping them (they carry no demographics or economy).
- **World-object integration (master)** — off means only vanilla objects are governed.
- **Placement rules for modded world objects** — apply region ownership, buffer distance and supply range to where modded world objects may be built.
- **Settlement tiers & capitals** — structural tiers (village → metropolis) and the capital star marker.
- **Strict territorial ownership** and **Enforce region locks** — the two placement switches. Both are per-world and safe to change **mid-game** (with a save loaded you edit that world's own flag; from the main menu you set the default for new worlds). Default **off** for compatibility on new worlds.
- **Population cap multiplier** and **Population growth rate** — the model-only multipliers (shown only when the Societies layer is on; the old separate on/off checkbox was removed).
- **Demographic pressure tuning** — reach and falloff sliders shaping how far a settlement's make-up carries, plus the **war/draft skew recovery** slider (how many in-game years a region's sex ratio takes to recover from combat losses; default 15). Societies-layer only.
- **Draw region borders on the world map** — the border overlay toggle.

The **Target region size** and **Claimed land area** knobs, the per-faction placement (relative size, resource weights, clustering, kin) and the **World maturity** seeding slider all live on the Geographic Placement dialog on the world-generation screen (this settings window only tunes per-biome sizing, via the Biome region sizes editor). Default settings on new worlds start conservative for compatibility — strict territorial ownership, placement governance and settlement tiers all begin **off**.
