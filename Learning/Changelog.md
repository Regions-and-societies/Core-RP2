# Changelog

Full version history. The mod page and Workshop description show only the latest release; earlier versions are recorded here. Versions before 0.1.0 shipped under the former identity, **RimSynapse - Regions and Territories**, and are kept below as the predecessor's history.

## v0.2.3 - Worldgen resilience with missing DLC content

- **Fixed: black world when DLC content is unresolved.** Classic (no-expansion) ideoligion role naming crashed world generation when the Ideology DLC was missing but other content added classic leader-role precepts — including for the player faction, which the 0.2.2 guard could not reach. Role naming is now guarded directly: a missing foundation gets a fallback title, and any naming failure degrades to the fallback instead of a dead world.
- **Graceful DLC degradation in faction generation.** A faction needing Royalty title content that isn't resolved is skipped with a single log line and the world generates without it; repeated skips log one line each instead of a wall of stacks.
- **Marked incompatible with Layered Atmosphere and Orbit (LAO).** It restructures the planet-layer stack that faction placement hooks into, breaking world generation. See [Compatibility Matrix](Compatibility_Matrix).
- **Dev quality-of-life.** The map-mode test helper now only runs on `-quicktest` launches, so playing with dev mode on no longer pulls you to the world map after starting a colony.

## v0.2.2 - Worldgen fixes and performance

- **Fixed: black world on generate.** Faction generation built each faction's ideoligion without the faction context and planet layer vanilla supplies, and classic (no-expansion) ideoligion role naming crashed world generation — the planet never rendered. Generation now mirrors vanilla's own call exactly, and a per-faction guard degrades any future faction failure to a logged skip instead of a dead world.
- **The player always has somewhere to land.** At small worlds or low coverage, NPC placement could claim every settleable province and the starting-site chooser found no valid tile. Placement now always leaves at least one settleable land province unclaimed.
- **Worldgen performance at scale.** Terrain features are read from the world grid once instead of once per faction, and the tribal betweenness bonus no longer rebuilds its industrial-base map per candidate province — large planets (high planet scale, 100% coverage) generate dramatically faster.

## v0.2.1 - Hotfix

- **Fixed: the in-game debug actions menu (dev mode) failed to open while the mod was installed.** A debug action carried a parameter, which RimWorld cannot bind, so building the menu threw and it never opened. Removed the offending action. No gameplay or save changes.

## v0.2.0 - Regional Demographics

Every region now carries a full demographic profile — seven axes, each deterministic from the world seed (nothing stored in the save), each with its own map overlay, a region-panel breakdown, and a public endpoint for other mods.

- **Age structure** — children / working-age / elders and a median age, from faction tech level (tribal birth-heavy pyramids vs. spacer flat), pro-natalist ideology memes, and xenotype longevity genes.
- **Sex ratio** — a deterministic ~50/50 baseline plus a mod-facing hook API (`DemographicHooks`): a companion mod can report a draft in progress (transient skew, men-first unless the caller says otherwise) or combat losses (a generational scar that decays back over a configurable number of years — default 15, slider provided).
- **Race (xenotypes)** — caste shares from the owning factions' xenotype sets; mod-added xenotypes flow through automatically with stable overlay colours. With Biotech off the overlay says "all Baseliner" rather than painting a flat map.
- **Ideology** — deepened from the meme layer into primary + minor ideoligion shares per region, plus meme-level belief similarity between neighbouring regions. Overlay tints each region in its dominant ideo's own colour. Secular (and says so) with Ideology off.
- **Income / socioeconomic status** — subsistence / modest / prosperous / affluent tiers with a 0–100 index, from per-tile wealth lifted by the region's resource richness and trade-road access.
- **Education** — illiterate / basic / skilled / advanced tiers with a 0–100 index, from tech level, research-vs-primitivist memes, and engineered-intellect xenotype genes.
- **Employment** — an agriculture / industry / military / trade occupation mix and an employment rate, from the region's world-object mix (garrisons pull military, extraction outposts industry, cities trade) and its terrain.
- **Territory compactness** — faction domains now square off instead of spidering: candidate provinces poorly embedded in the domain are down-weighted (a preference, never a rule), tunable with a new **Territory compactness** slider. Public `TerritoryCompactnessUtility` lets expansion mods rank candidates with the same metric.
- **Dwellings readout** — the population tooltip and inspect pane now answer for every habitable tile (zero included) as a compass block of the tile and its neighbours, so empty land reads as "0 here" rather than as a broken tooltip.

## v0.1.0 - Migration and Rebrand

The first release as **Regions and Societies**.

- **Ported from Regions and Territories v0.8.0.** The full source tree — provinces, the four-tier ownership ladder, placement governance, map modes, demographics — continues here unchanged in behaviour.
- **Rebranded.** New mod name, new package ID (`RegionsAndSocieties.Core`), new repository ([Regions-and-societies/Core-MMF](https://github.com/Regions-and-societies/Core-MMF)). Because the package ID changed, this installs as a **new mod side by side with the old one** — it does not update Regions and Territories in place, and saves made with the old package ID are not migrated.
- **Compatibility model inverted.** Core no longer carries foreign-mod knowledge: the string-based reflection profiles for Empire Refactored, World Domination, the Vanilla Expanded framework and Vanilla Outposts Expanded are gone from core, extracted to dedicated companion compatibility patches (`Empire-CP`, `World-Domination-CP`, `VFE-CP`, `VOE-CP`). Core keeps the classification contract and exposes a public, priority-ordered adapter registration API that each patch calls at load; the vanilla adapter remains built in. Installing a patch is the enable switch for its integration.
- **Release provenance.** Every release now ships `Assemblies/CHECKSUMS.sha256`, generated from the final build by `harness/release-manifest.ps1` and verifiable — against the release or any deployed copy — with `harness/verify-binaries.ps1`.

---

# Predecessor history: RimSynapse - Regions and Territories

Everything below shipped under the former identity and package ID.

## v0.7.4 - Border overlay ownership fix
- Fixed: the global region-border overlay again shows faction ownership at a glance, so you no longer have to switch into the Territories map mode to read who holds what. It had stopped colouring borders after the 0.7.3 ownership rework - the overlay never recalculated owners and never repainted when they changed, so every border drew white.
- Improved: borders now read claim strength directly. A region held outright (over 50%) draws a solid line in its owner's colour; a contested region alternates the two claimants' colours; a loosely-held region (a single 30-50% claim) alternates the claimant's colour with white.

## v0.7.3 - Territory ownership rework
- Reworked: a region's ownership now combines the settlements standing in it with a split of its border. A settlement gives its faction the bulk of the region; the border makes up the rest - land you share with a rival-held region is that rival's pressure on you, while mountains, water and open frontier count for the region's own owner as secure, self-bordering ground. So a settled region is firmly held by that faction, shielded by whatever mountains and coast wrap around it, and pressed only where a rival's territory actually touches it - with no phantom "unclaimed" sliver on land you clearly hold.
- Fixed: a region with no settlement of its own can no longer read as almost entirely owned by a neighbour purely from bordering it - worst on mountain-ringed regions, where a faction touching a short stretch of border could jump to 70%+ "ownership" and fence a new colony out of empty land. An unsettled region's ownership is now capped, and its mountains and open frontier stay unclaimed rather than inflating whoever happens to border them.
- Fixed: a settlement in a barren biome (desert, ice) now grants normal ownership of its region. A "nobody truly holds a desert" rule was halving ownership on barren land, so a faction with a real town in a desert read as only weakly holding it. Barren terrain now affects only where settlements naturally form, never who owns a region once one is built.
- Changed: region names. A region is now simply "Region <id>", renamed to "<settlement> Region" once a settlement stands in it; the region id is always shown in the expanded region details.

## v0.7.2 - Territory ownership, placement and population fixes
- Fixed: Territories map shading. A region with a single clear owner now fills solid instead of reading as contested; only regions two or more factions genuinely claim (each with a real 30%+ hold) are cross-hatched. The owner-coloured border overlay follows the same rule.
- Fixed: new colonies are no longer fenced out of huge stretches of the map by a neighbouring faction's influence. You can now settle land a rival only loosely or partially holds; only a region a rival owns outright (70%+) refuses a starting colony. Settling contested ground is intended - expect it to carry consequences in a later update.
- NEW - Population realism: naturally-forming settlements are smaller and rarer, and form where pawns can actually survive - along rivers, roads and coasts - rather than deep in dangerous wilderness. Region population totals are also corrected; they previously over-counted badly.
- Fixed: region borders no longer draw through the far side of the planet.
- Save compatibility: existing worlds keep the population they were generated with, so an in-progress colony's world does not shift under it (the new population model applies to newly generated worlds). Territory ownership now computes correctly on existing saves - a bug could leave a loaded world reading as entirely unclaimed - and loading a save no longer produces a wall of red errors.
- The new population model and placement rules take full effect on newly generated worlds; a new colony is recommended.

## v0.7.1 - Region generation, map modes and performance
- NEW - Region generation overhaul: provinces now grow terrain-aware and value-budgeted, following rivers, coastlines and mountains for prettier, more natural borders instead of arbitrary straight lines, and region size is bounded so no single province swallows the map.
- NEW - Map modes: a faction-shaded Territories view and a Population / dwellings view, drawn through the Map Mode Framework.
- NEW - Region-border overlay: an owner-coloured border overlay in the main Draw Settings toggles that works over any map mode.
- NEW - Region comparison panels: modifier-click a region (Ctrl or Shift, rebindable in the mod settings) to open a draggable readout; open several at once to compare, each titled by its unique region number.
- Performance: region aggregates - population, ownership, perimeter and border shares - are materialised and cached instead of recomputed every frame, and world-object bucketing is now O(n), so large worlds draw and tick far cheaper.
- Changed: the influence pie opens on region selection rather than on stationary hover; an optional setting shows ownership calculation breakdowns in tooltips without Dev Mode.
- Fixed: the settings "Detected:" integration line drew over the Faction Geography panel.
- Fixed: a settlement-validity check queried the player faction during world generation and spammed the log with errors.
- The new region generation applies to newly generated worlds; a new colony is recommended for the full effect.

## v0.7.0 - Regions and Territories Compatibility
- NEW - Mod-agnostic world object integration: Empire Refactored, Vanilla Outposts Expanded, Vanilla Expanded Framework and World Domination are recognised through adapter profiles instead of by name, so territory rules no longer hardcode which mods exist.
- NEW - Placement and territory governance: one evaluator decides where settlements, outposts, military installations and camps may stand, and the world inspect pane tells you why a tile was refused.
- NEW - Compatibility mode: a world generated before this mod is adopted rather than refused, and you are told when that happens.
- Fixed: Empire settlement population always read as zero. Three of the four adapter profiles named members or types that do not exist on the real assemblies, and nothing said so - a wrong name cost no error, it just returned a plausible zero.
- Fixed: Vanilla Expanded Framework renamed its assembly from VFECore to VEF, so that profile had resolved to nothing for as long as it had existed.
- Changed: the demographic component of territory ownership contributes nothing this release. It was awarding a fifth of the score for simply owning a settlement, which the settlement score already counted. It returns in 0.8 reading real regional demographics.
- REQUIRES A NEW COLONY - not save-game compatible.

## v0.6.2
- No gameplay changes. This release exists so the save-compatibility notice reaches people who already have the mod installed.
- Regions and Territories has always needed a new colony, and the Workshop page has said so - but the in-game description did not, so anyone who subscribed and never went back to the page had no way to find out.
- The warning now appears here, on the Workshop page, and in the compatibility matrix, and it says the same thing in all three.
- Coming in 0.7: worlds saved by 0.7 will not read correctly if you go back to 0.6 - the new regional resource stocks are dropped and mined-out provinces come back full. Loading an existing 0.6 world in 0.7 is fine and stays fine.

## v0.6.1
- Fixed: the in-game mod list showed v0.5.2 with no 0.6.0 notes; version and changelog now agree everywhere.
- Roadmap updated: 0.7 is Regions and Territories compatibility (groundwork for Factions, which will require Empire). Everything after it shifts up one release.

## v0.6.0
- Moves in step with RimSynapse Core v0.6.0 (Agent and Tool Foundation).
- Requires Core v0.6.0; saves and settings carry over unchanged.
- In-game wiki guides updated; "MCP" renamed to game tools throughout.
