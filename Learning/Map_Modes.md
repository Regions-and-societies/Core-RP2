# Map Modes

This mod ships its world-map overlays through the **Map Mode Framework**. That framework is a hard dependency — without it the overlays do not draw at all.

Switch between them with the map mode selector on the world view (the Regions and Societies group button lists them all). On top of the modes, an owner-coloured **region-border overlay** can be drawn over any map mode (toggled from the Draw Settings panel or the mod settings), and faction **capital markers** flag each faction's principal settlement.

---

## Geographic Provinces

Draws the province boundaries themselves: the contiguous regions the planet was divided into at world generation.

The tooltip reports what the region contains — its tiles, the factions scoring in it, and its **unclaimed share**. If you want to understand why a tile was refused for settlement, this is the overlay to check first.

Useful when: you want to see the shape of the world's regions, or work out which province a tile belongs to.

---

## Faction Territory

Colour-codes provinces by the faction holding them, using each faction's own colour.

**Contested provinces are shown as contested**, listing every faction with a claim, rather than being handed to whichever is marginally ahead. A province nobody holds is drawn as unclaimed.

Useful when: you want to see the political shape of the planet, find a frontier, or understand where your own claim ends.

---

## Population Density

A gradient showing where people actually are, propagated outward from settlements through biomes, terrain and roads rather than drawn as flat circles. Mountains and water slow the spread; roads carry it further. The ramp runs violet (a few pawn dwellings) through magenta and red to orange and bright yellow (a settlement at the world's theoretical maximum), so it never blends into green land or blue sea; tile labels show the dwellings actually on the tile.

This is the layer that feeds population-derived figures elsewhere in the suite, so if a settlement tier or a density-based number looks wrong, this overlay is where to check the input.

Useful when: choosing where to settle, or working out why one region feels busier than another.

---

## Residences (0.3.0)

Shades each tile by how its people are housed — dwellings are modelled separately from raw population, so this reads the *homes*, not the head-count. A rural tile holds a few large extended-family residences on wide land; toward a city, homes get smaller and denser and occupancy drops toward nuclear families. Green (rural) through to red (city). The label reports the residence count on the tile.

Useful when: seeing at a glance where the world is rural versus urban.

---

## Demographic overlays (0.2.0)

Seven overlays, one per demographic axis, each shading every **settled** region (unsettled wilderness and water stay unshaded — an unshaded region means "no people", not "no data"). Hovering a tile shows that axis's full breakdown for the region; the same numbers appear in the expanded region panel.

- **Age structure** — median age, youthful green through mature yellow to elderly red. A tribal region reads young; a long-lived spacer caste reads old.
- **Sex ratio** — blue where men outnumber women, magenta the reverse, faint neutral where even. The baseline is genuinely near-even, so a mostly-neutral map is honest data; colour appears where a mod-driven skew (a draft, a war's losses) is in force.
- **Xenotypes** — each region tinted by its dominant Biotech caste in a colour stable for that xenotype, darker where the caste dominates more strongly. Requires Biotech; without it the tooltip states "all Baseliner" and the map stays unshaded.
- **Ideology** — each region tinted by its dominant ideoligion in that ideo's own colour, darker where belief is more uniform. The tooltip lists the top ideoligions and the region's belief similarity to its neighbours. Requires the Ideology DLC; without it every region is secular and says so.
- **Wealth** — the socioeconomic index, deep red (subsistence) to green (affluent).
- **Education** — the attainment index, brown (unschooled) to blue (highly educated).
- **Employment** — each region tinted by its dominant occupation sector: green agriculture, steel industry, red military, gold trade.

---

## If an overlay is missing or blank

- **Map Mode Framework not installed.** Nothing will draw. It is a requirement, not an optional integration.
- **A blank or uniform overlay on a freshly generated world** usually means worldgen did not complete this mod's steps — check `Player.log` for errors during world generation.
- **Territory shading that looks wrong** is usually the score, not the display — the map reads the same ownership answer as everything else. See [Territory Ownership](Territory_Ownership) for how the score is built, and enable the calculation-breakdown setting to inspect a province's derivation.
