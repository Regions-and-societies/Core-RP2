# Biotech / Xenotype API Model — verified spike (#35)

**Summary.** A pawn's race axis lives on `Pawn.genes` (type `Pawn_GeneTracker`); its
xenotype is read via `Pawn_GeneTracker.Xenotype : XenotypeDef` (named/preset) or
`Pawn_GeneTracker.CustomXenotype : CustomXenotype` (player/ad-hoc). Xenotypes are
assigned by weighted distribution held in an `XenotypeSet` on **`FactionDef.xenotypeSet`**
and **`PawnKindDef.xenotypeSet`**, gated by **`PawnKindDef.useFactionXenotypes`**. The
generation hook (#37) forces a xenotype through `PawnGenerationRequest.ForcedXenotype`
(named) or `ForcedCustomXenotype`, via `Verse.PawnGenerator.GeneratePawn`. Reading the
actual 1.6 Def XML (all DLCs installed) confirms the **functional-archetype** picture the
owner asked for: factions do use a handful of xenotype "castes," not the full list —
e.g. the pirate/Waster faction is **72.5% Waster** disposable tox-soldiers with a small
tail of Hussar/other modified castes, the Empire runs **15% Hussar + 10% Genie**
(professional-soldier + engineer castes) over a baseliner majority, and civilian
outlander/tribal factions are baseliner-dominant with only a ~5–10% modified minority.
**No VFE/mod xenotypes are installed** — only the 11 Biotech + 1 Odyssey vanilla
xenotypes exist in this environment. Everything below is corpus- or live-XML-verified
unless marked UNVERIFIED. "No-DLC" = Biotech not active.

---

## B5. Reading a pawn's xenotype

| Type.Member | What it is | Verified? | No-DLC (Biotech OFF) |
|---|---|---|---|
| `Pawn.genes` (field) → `Pawn_GeneTracker` | Per-pawn gene/xenotype tracker; graph confirms `Verse.Pawn` is the sole user of `Pawn_GeneTracker`. | Verified (relationship via `query_api_graph usedby`; field name `genes` is standard) | Tracker exists but is effectively inert. |
| `Pawn_GeneTracker.Xenotype : XenotypeDef` (property) | The pawn's named xenotype. | Verified | Reports **Baseliner** (the fallback xenotype); no real genes. |
| `Pawn_GeneTracker.CustomXenotype : CustomXenotype` (property) | Set when the pawn is a custom (unique) xenotype rather than a named def. | Verified | **null**. |
| `Pawn_GeneTracker.UniqueXenotype : bool` | True when custom rather than a named def. | Verified | false. |
| `Pawn_GeneTracker.SetXenotype(XenotypeDef)` / `SetXenotypeDirect(XenotypeDef)` | Assign xenotype (Direct = no gene re-resolution). | Verified | Would be a no-op / meaningless without Biotech genes. |
| `Pawn_GeneTracker.GenesListForReading / Endogenes / Xenogenes : List<Gene>` | The pawn's active genes split by endo/xeno origin. | Verified | Empty. |
| `Pawn_GeneTracker.XenotypeLabel / XenotypeLabelCap / XenotypeIcon` | Display helpers. | Verified | "Baseliner". |
| `XenotypeDef.genes : List<GeneDef>` and `XenotypeDef.AllGenes : List<GeneDef>` | The genes that define a xenotype. | Verified | Defs not loaded. |
| `XenotypeDef.combatPowerFactor : float`, `factionlessGenerationWeight`, `doubleXenotypeChances`, `generateWithXenogermReplicatingHediffChance` | Balance knobs; `combatPowerFactor` matters for raid-value math. | Verified | N/A. |
| `Verse.GeneDef` (Def): `geneClass : Type`, `AptitudeFor(SkillDef)`, `ConflictsWith(GeneDef)`, `labelShortAdj`, `customEffectDescriptions` | The gene catalog def. | Verified (subset of members surfaced) | Not loaded. |

**No-DLC bottom line:** `Pawn.genes` and `Pawn_GeneTracker` are always present but with
Biotech off every pawn resolves to **Baseliner**, `CustomXenotype` is null, and gene
lists are empty. A demographic race axis must degrade to a single "baseliner" bucket
when Biotech is absent.

---

## B6. How a xenotype is assigned (the #37 seam)

| Type.Member | What it is | Verified? | No-DLC |
|---|---|---|---|
| `FactionDef.xenotypeSet : XenotypeSet` (field) | Faction-wide weighted xenotype distribution. | Verified (API + live XML) | Field present; ignored (all baseliner). |
| `PawnKindDef.xenotypeSet : XenotypeSet` (field) | Per-kind override distribution. | Verified (API + live XML) | Ignored. |
| `PawnKindDef.useFactionXenotypes : bool` (field) | If true, the kind draws from the faction's set instead of its own. | Verified | Moot. |
| `XenotypeSet.Contains(XenotypeDef) : bool`, `Item : XenotypeChance` (indexer) | The set container. Backing XML element is `<xenotypeChances>` (a `List<XenotypeChance>`). | Verified | N/A. |
| `XenotypeChance { XenotypeDef xenotype; float chance }` | One weighted entry. In XML the value is a **weight/probability**: large values like `999` mean "always" and fractional values (`0.05`, `0.725`) are probabilities. | Verified | N/A. |
| `PawnGenerationRequest.ForcedXenotype : XenotypeDef` (property) | **Forces a named xenotype** — the primary #37 seam. | Verified | Ignored without Biotech. |
| `PawnGenerationRequest.ForcedCustomXenotype : CustomXenotype` (property) | Forces a custom xenotype. | Verified | Ignored. |
| `PawnGenerationRequest.ForcedXenogenes : List<GeneDef>` / `ForcedEndogenes : List<GeneDef>` | Force specific genes (finer than whole xenotype). | Verified | Ignored. |
| `PawnGenerationRequest.AllowedDevelopmentalStages : DevelopmentalStage` | Gate child/adult — relevant since children can be `ForceBaselineChild`-style baseliners. | Verified | Core. |
| `PawnGenerationRequest.PawnKindDefGetter : Func<XenotypeDef, PawnKindDef>` | Lets a caller map the chosen xenotype back to a kind. | Verified | N/A. |
| `PawnGenerationRequest.ForceBaselineChild` | Task-named field to force baseliner children. | **UNVERIFIED — did not surface in the corpus.** Do not rely on it; use `ForcedXenotype = Baseliner` + `AllowedDevelopmentalStages` instead. | — |
| `PawnGenerator.GetXenotypeForGeneratedPawn(PawnGenerationRequest) : XenotypeDef` (static) | The exact vanilla decision method for a pawn's xenotype. | Verified | Returns Baseliner. |
| `PawnGenerator.XenotypesAvailableFor(PawnKindDef kind, FactionDef factionDef, Faction faction) : Dictionary<XenotypeDef,float>` (static) | **The weighted table vanilla rolls against** — ideal read for a region-biased picker. | Verified | Baseliner only. |
| `PawnGenerator.AdjustXenotypeForFactionlessPawn(Pawn, ref PawnGenerationRequest, ref XenotypeDef)` (static) | Handles factionless pawns' xenotype. | Verified | Baseliner. |
| `GeneUtility` (static): `GenerateGeneSet`, `ReimplantXenogerm`, `IsBloodfeeder`, `CanDeathrest` | Gene/xenogerm helpers. | Verified | N/A. |

**Seam bottom line:** for #37, set `PawnGenerationRequest.ForcedXenotype` (or
`ForcedCustomXenotype`) before/inside `PawnGenerator.GeneratePawn`, choosing from a
region-weighted variant of the `XenotypeSet` that vanilla already exposes via
`XenotypesAvailableFor`. The faction/pawnkind `xenotypeSet` + `useFactionXenotypes` are
the data we read to know the baseline distribution.

---

## B7. Functional xenotype archetypes (owner's emphasis) — REAL defNames + live-XML evidence

**Installed xenotype defs (verified via `search_defs` — this is the complete list here):**

| defName | Module | Functional role |
|---|---|---|
| `Baseliner` | Biotech | Unmodified human — the **civilian default** everywhere. |
| `Hussar` | Biotech | **Engineered soldier caste.** Go-juice-dependent, aggressive, fast-healing; "less likely to rebel." The disposable/professional combat archetype. |
| `Waster` | Biotech | **Tox raider caste.** Pollution-immune, psychite/wake-up tolerant, drug-fueled. Dominant pirate xenotype. |
| `Genie` | Biotech | **Engineer/tech caste.** Machine aptitude, emotionally cold, physically frail. |
| `Dirtmole` | Biotech | **Labor/mining caste.** Dark-adapted, great at digging, close-quarters. |
| `Neanderthal` | Biotech | Hardy melee/labor stock; injury- and infection-resistant. |
| `Pigskin` | Biotech | Hardy cheap labor; eats anything, poor manipulation. |
| `Impid` | Biotech | Desert combat caste; very fast, fire-breathing. |
| `Yttakin` | Biotech | Cold-world raider; large, furry, beast-summoning. |
| `Highmate` | Biotech | **Companion/concubine caste** (Royalty/spacer contexts); social, inept at labor. |
| `Sanguophage` | Biotech | **Archotech elite** — near-immortal, combat spines, self-heal; not a mass caste. |
| `Starjack` | Odyssey | Space-adapted; small tail chance across many factions. |

> **VFE / other-mod xenotypes: NONE present.** `search_defs defType=XenotypeDef`
> returns exactly the 12 above (11 Biotech + 1 Odyssey). Any "soldier-caste from VFE
> modules" the task hypothesizes is **not installed in this environment** — flag as
> UNVERIFIED/absent; if VFE is added later, re-run `search_defs` to enumerate.

**Live-XML evidence of caste mixes (real weights from the 1.6 Def files):**

- **Pirate faction (Biotech-replaced "Waster" pirates)** — `Biotech/.../Factions_Misc.xml`:
  `Waster 0.725`, `Hussar 0.05`, `Neanderthal 0.05`, `Dirtmole 0.05`, `Genie 0.025`,
  `Pigskin 0.025`, `Yttakin 0.025`, `Impid 0.025`, `Starjack 0.01`.
  → the "disposable genetically-modified addicted soldiers for raids" pattern: a dominant
  drug-dependent combat xenotype with a modified-caste tail.
- **Empire (Royalty)** — `Faction_Empire.xml`: `Hussar 0.15`, `Genie 0.10`,
  `Neanderthal 0.05`, `Starjack 0.025` over a baseliner majority.
  → professional **soldier (Hussar) + engineer (Genie)** castes atop civilian baseliners.
- **Outlander/tribal civilian factions (Core)** — `Core/.../Factions_Misc.xml`: baseliner
  majority with only `Hussar 0.05`, `Dirtmole 0.05`, `Genie 0.025`, `Neanderthal 0.025`,
  `Starjack 0.025`. → **baseliner-dominant civilians** with a small modified minority.
- **Mono-xenotype factions** — Neanderthal / Yttakin / Impid / Pigskin factions each pin
  their xenotype at weight `999` (i.e. ~100%): e.g. `<Neanderthal>999</Neanderthal>`.
- **Special pawnkinds pin directly** — `PawnKinds_Special.xml`: `SanguophageBase` sets
  `xenotypeSet {Sanguophage 999}` **and** `useFactionXenotypes false` (ignore faction mix).

**Recommended archetype categories for the demographic "race" axis** (each backed by real
defNames above):
1. **Baseliner civilian** — `Baseliner` (default majority).
2. **Soldier caste** — `Hussar` (drug-dependent engineered soldier; the raid/disposable
   archetype), secondary `Impid`/`Yttakin` (regional combat).
3. **Labor/utility caste** — `Dirtmole` (mining), `Pigskin`/`Neanderthal` (hardy labor).
4. **Tech/engineer caste** — `Genie`.
5. **Tox/raider caste** — `Waster` (pollution-adapted drug soldiers; pirate-dominant).
6. **Companion caste** — `Highmate`.
7. **Archotech elite** — `Sanguophage` (rare, not mass).
8. **(Region/DLC flavor)** — `Starjack` (Odyssey space caste).

A region's race axis is naturally modeled as a **weighted `XenotypeSet`-shaped
distribution** over these categories, which maps 1:1 onto the vanilla
`FactionDef.xenotypeSet` / `XenotypesAvailableFor` data we already read.

---

## C (wealth). Median-wealth generation seam

Confirmed **live in the 1.6 PawnKindDef XML** (counts = distinct kinds using the field in
`Core/Defs/PawnKindDefs_Humanlikes`): `weaponMoney` (24), `apparelMoney` (27),
`techHediffsMoney` (18), `itemQuality` (13), `combatPower` (28), `gearHealthRange` (21),
`weaponTags` (26), `biocodeWeaponChance` (6), `apparelRequired` (9), `invNutrition` (8).
Sample (outlander kinds): `<weaponMoney>65~250</weaponMoney>`,
`<apparelMoney>200~400</apparelMoney>`, `<techHediffsMoney>50~600</techHediffsMoney>` —
values are `FloatRange` (min~max, silver).

| Field (PawnKindDef) | What it drives | Verified? | No-DLC |
|---|---|---|---|
| `apparelMoney : FloatRange` | Budget for generated apparel → clothing quality/tier. | Verified (live XML) | Core. |
| `weaponMoney : FloatRange` | Budget for the generated weapon → weapon tier. | Verified (live XML) | Core. |
| `techHediffsMoney : FloatRange` | Budget for bionics/implants (with `techHediffsChance`). | Verified (live XML) | Core. |
| `itemQuality` | Quality category bias for generated gear. | Verified (live XML) | Core. |
| `gearHealthRange` | Condition (hit points %) of spawned gear. | Verified (live XML) | Core. |
| `weaponTags` / `apparelRequired` / `apparelTags` | Which weapon/apparel pools are eligible. | Verified (live XML) | Core. |
| `biocodeWeaponChance` | Chance the weapon is biocoded (bound to the pawn). | Verified (live XML) | Core. |
| `combatPower` | Threat-point value used by raid budgeting. | Verified (live XML) | Core. |
| `PawnGenerationRequest.BiocodeApparelChance : float` | Per-request biocode-apparel probability. | Verified (API corpus) | Core. |
| `PawnGenerationRequest.BiologicalAgeRange : Nullable<FloatRange>` / `ExcludeBiologicalAgeRange` | Age window (age correlates with skill/wealth). | Verified (API corpus) | Core. |

**What a "median pawn wealth" axis can actually drive:** scale the FloatRange gear
budgets — primarily `apparelMoney`, `weaponMoney`, and `techHediffsMoney` (plus
`itemQuality` and `techHediffsChance`) — up or down per region, so a wealthy region's
pawns generate with better/pricier gear and more implants, and a poor region's with
cheaper gear. These are `PawnKindDef` fields (not on the request), so the #37 hook either
(a) selects a richer/poorer PawnKindDef variant, or (b) post-processes generated gear —
`PawnGenerator.PostProcessGeneratedGear(Thing, Pawn)` is a verified static seam. Age
skew via `PawnGenerationRequest.BiologicalAgeRange` is the on-request lever.

---

## Explicitly UNVERIFIED / flagged
- `Pawn.genes` **field name**: type `Pawn_GeneTracker` and ownership are corpus-verified;
  the lowercase `genes` identifier is the standard public field, not independently dumped.
- `PawnGenerationRequest.ForceBaselineChild` — **not found** in the corpus. Use
  `ForcedXenotype = Baseliner` (+ `AllowedDevelopmentalStages`) instead.
- **VFE / mod xenotypes** — none installed; only the 12 vanilla defs above exist here.
  Any soldier-caste-from-VFE assumption is unverifiable in this environment.
- `XenotypeSet` internal storage — the XML element is `<xenotypeChances>` and the API
  exposes `Contains`/`Item`(`XenotypeChance`); the exact backing field name
  (`xenotypeChances`) is inferred from XML, not from a dumped C# field.
