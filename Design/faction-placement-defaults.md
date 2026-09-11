# Faction placement defaults (#65)

How Regions & Societies decides a faction's out-of-the-box placement profile — the values a
player sees in **Geographic Placement Settings** before touching anything. Everything here is
**mod-neutral**: a faction's defaults come from its *kind* and *tech level*, never from a
hardcoded list of specific defNames (the one exception is the Empire, keyed by `defName ==
"Empire"` because the vanilla shattered Empire is a named, one-of-a-kind polity that many mods
base off).

Source of truth: `FactionPlacementSettings.GetDefaultProfile`, `ClusteringRules`,
`SubFactionRules`. This doc is the human-readable summary; the code wins if they ever disagree.

## The three ideas (kept separate as of 0.4.1)

| Idea | Field | What it controls |
|---|---|---|
| **Clustering** | `clusterSize` | The largest a single contiguous cluster (body) grows. The faction's territory scatters into bodies of at most this many regions. **Applies always**, whether or not the faction forms kin. `0` = one contiguous nation (no scatter). |
| **Kin** | `enableKin` | Whether those bodies become **separate factions** (loosely-related "North / South" kin). A kin-off faction still clusters — it just stays one faction. |
| **Number of clusters** | `numberOfClusters` | The **maximum number of kin factions** the bodies are grouped into. Only meaningful when kin is on. `0` = no cap. |

Before 0.4.1 the worldgen body-size cap was derived from the kin count, so **kin-off ⇒ one
giant blob**. That collapsed the shattered Empire into a single nation (#63). Now the body cap
is `clusterSize` when kin is off (matching the incremental placer), so a kin-off faction still
scatters. Kin-on factions keep the old `ceil(regions / kinCount)` cap — no behaviour change.

## Classification — how any faction gets a kind

`ClusteringRules.ClassifyKind(defName, label, techLevel, permanentEnemy, hostileToFactionless)`,
first match wins:

1. name (defName or label) contains **"pirate"** → `Pirate`
2. `defName == "Empire"` → `Empire`
3. techLevel **< Industrial** → `Tribe`
4. name contains **"Rough"** → `RoughUnion`
5. techLevel **== Industrial**, hostile to factionless humanlikes, **not** a permanent enemy → `RoughUnion`
6. otherwise → `Other` (cohesive civil / trader / spacer nation)

A modded faction is classified by the same rules, so it inherits sensible defaults with no patch.

## Defaults by kind

| Kind | Cluster size (`clusterSize`) | Number of clusters (`numberOfClusters`) | Kin default (`KinDefault`) |
|---|---|---|---|
| **Pirate** | 3 | 5 | **on** (unless spacer-tech — see below) |
| **Empire** | 3 | n/a — kin locked, so this knob never applies | **off, LOCKED** |
| **Tribe** | 5 | 3 | **on** (unless spacer-tech) |
| **RoughUnion** | 7 | 2 | **on** (unless spacer-tech) |
| **Other** | 0 (one nation) | 1 | **off** |

### Two overrides on the kin default

- **#63 Empire lock** — the Empire never forms kin, and the toggle is disabled in the UI. It
  is one highly-fragmented polity: many 1–3 region clusters scattered planet-wide. The player
  can change its **cluster size** (0 = cluster the whole faction together) and its size weight,
  but never turn it into kin factions. The Empire `FactionDef` is **never modified** — this is
  purely R&S placement logic — so mods that base a faction off the Empire are unaffected.
- **#64 spacer-tech kin off** — any faction with **techLevel ≥ Spacer** defaults to **no kin**
  (pod/shuttle mobility means distance does not fracture it into separate polities). Its
  **clustering still applies** (a spacer pirate still scatters into 3-region hideout bodies, it
  just stays one gang). Still player-overridable per faction.

## Resource-weight profile by tech (`GetDefaultProfile`)

| Tech | Mineral | Nutrition | Forage | Grazing | Hunting | Margin | Placement order |
|---|---|---|---|---|---|---|---|
| Spacer+ | 2.5 | 0.5 | 0.1 | 0.1 | 0.2 | 0.0 | 3 |
| Industrial | 1.0 | 2.0 | 0.2 | 0.8 | 0.8 | 0.0 | 1 |
| Sub-industrial, peaceful | 0.2 | 0.2 | 2.0 | 2.0 | 0.2 | 0.1 | 4 |
| Sub-industrial, raider | 0.2 | 0.2 | 2.0 | 0.2 | 2.0 | 0.1 | 4 |

"Raider" = `hostileToFactionlessHumanlikes || permanentEnemy`. A raider faction also gets
`margin` 2.5 and a smaller default **size** (share weight ~5.5 vs ~10 for a civil faction — the
midpoints of the old 3–8 and 5–15 ranges). The Empire's placement order is 2.

## Resolved examples (the active factions in a typical Royalty+Ideology+Biotech world)

| Faction | Kind | Cluster size | Kin default |
|---|---|---|---|
| Shattered empire | Empire | 3 (adjustable) | off, **locked** |
| Cannibal / Pirate / Waster / Yttakin pirate gangs | Pirate | 3 | on, **or off if the faction is spacer-tech** (#64) |
| Rough outlander union, Rough pig union | RoughUnion | 7 | on |
| Civil outlander union, Traders guild | Other | one nation | off |
| Gentle / Fierce / Savage / Nudist / Cannibal / Neanderthal / Impid tribes | Tribe | 5 | on |

The exact kin outcome for the pirate factions depends on each one's techLevel: an
industrial-tech pirate (e.g. the base Pirate gang, waster bands) keeps kin on; a spacer-tech
pirate defaults kin off (#64). This is decided automatically from the def, not per name.

## Mod-added factions

Classification is automatic, so a modded faction gets a reasonable default with **no patch**.
When a mod faction needs a **bespoke** default that kind + tech cannot infer (a thematic
scatter, a fixed kin count, a hand-tuned resource profile), that override belongs in the mod's
**compatibility patch repo** (VFE-CP / Empire-CP / VOE-CP …), never in Core — Core stays
mod-neutral. The delivery mechanism is the placement-profile / archetype **registration hook
(#55)**, held to a later feature milestone; until it lands, document the desired override as a
CP issue.

### Bespoke overrides to file as CP issues
_(none yet — add rows as mod factions are found to need hand-tuned defaults)_

| Mod faction | CP repo | Desired override | Why kind+tech can't infer it |
|---|---|---|---|
| _example: VFE_SomeFaction_ | _VFE-CP_ | _cluster size 4, kin off_ | _classified Other, but should scatter_ |

## Porting note

This model (0.4.1) must be carried to the RP2 / base edition too — see [port.md](port.md).
