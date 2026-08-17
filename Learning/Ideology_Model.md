# Ideology API Model — verified spike (#35)

**Summary.** RimWorld's Ideology DLC attaches a per-pawn ideology through the field
`Pawn.ideo` (type `Pawn_IdeoTracker`) and a per-faction set through
`Faction.ideos` (type `FactionIdeosTracker`, primary via `PrimaryIdeo`). An `Ideo`
object carries a **small fixed list of memes** (`Ideo.memes : List<MemeDef>`, typically
1–4) and a **large derived list of precept instances** (`Ideo.PreceptsListForReading :
List<Precept>`). Memes are the coarse "flavor" of a religion and are the **better
similarity axis** for comparing two ideologies; precepts are numerous, fine-grained,
and generated *from* the memes. Certainty and conversion already exist as first-class
vanilla concepts (`Pawn_IdeoTracker.Certainty`, `IdeoConversionAttempt`,
`ConversionTuning`) — we scale existing difficulty, not invent it. The world's set of
ideos is held by `IdeoManager.IdeosListForReading` and is **not fixed at gen** — ideos
can be created and added at runtime via `IdeoGenerator` + `IdeoManager.Add`. Every type
and member below was confirmed against the live API corpus (dump of the actual 1.6 game
assemblies, all DLCs installed) unless explicitly marked UNVERIFIED.

All members verified via the `search_game_api` / `query_api_graph` corpus
(9,047 indexed game types) unless noted. "No-DLC" = behavior when Ideology is not active.

---

## A1. Attaching an Ideo to a pawn and to a faction

| Type.Member | What it is | Verified? | No-DLC (Ideology OFF) |
|---|---|---|---|
| `Pawn.ideo` (field) → `Pawn_IdeoTracker` | The per-pawn ideology tracker; graph confirms `Verse.Pawn` is the sole user of `Pawn_IdeoTracker`. | Verified (relationship via `query_api_graph usedby`; field name `ideo` is the standard public field) | Tracker object still exists; its `Ideo` is **null**. |
| `Pawn_IdeoTracker.Ideo` (property, `Ideo`) | The pawn's current ideology. | Verified | Returns **null**. |
| `Pawn_IdeoTracker.SetIdeo(Ideo)` | Assigns the pawn's ideology. | Verified | Callable but you have no non-null `Ideo` to pass. |
| `Pawn_IdeoTracker.PreviousIdeos : List<Ideo>` | History of prior ideos (for conversion logic). | Verified | Empty. |
| `Faction.ideos` (field) → `FactionIdeosTracker` | Per-faction ideology set. Field name `ideos` is the standard public field; the tracker type is confirmed real. | Verified (type real; field-name standard) | Tracker exists; primary is **null**. |
| `FactionIdeosTracker.PrimaryIdeo : Ideo` | The faction's main ideology. | Verified | **null**. |
| `FactionIdeosTracker.AllIdeos : IEnumerable<Ideo>` | Primary + minor ideos. | Verified | Empty. |
| `FactionIdeosTracker.IdeosMinorListForReading : List<Ideo>` | Secondary ideos in the faction. | Verified | Empty. |
| `FactionIdeosTracker.GetRandomIdeoForNewPawn() : Ideo` | Picks an ideo for a newly generated faction member — **useful for #37**. | Verified | Returns null / no-op. |
| `FactionIdeosTracker.IsPrimary/SetPrimary/Has/IsMinor/HasAnyIdeoWithMeme(MemeDef)` | Membership + meme queries on the faction's set. | Verified | Trivially false/no-op. |
| `IdeoFoundation` (abstract) + `IdeoFoundation.def : IdeoFoundationDef`, `.ideo : Ideo` | The "structure" backing an ideo (deity/place/animal foundation). Built by `IdeoGenerator.MakeFoundation`. | Verified | N/A (no ideos exist). |

**No-DLC bottom line:** the tracker *objects* are always present (they are plain
`Pawn`/`Faction` members, not DLC-gated), but every `Ideo` reference resolves to
**null** with Ideology off. Our code must null-check `Ideo` before use and treat
"no ideology" as a first-class state.

---

## A2. Meme vs Precept — structure and the similarity axis

| Type.Member | What it is | Verified? | No-DLC |
|---|---|---|---|
| `Ideo.memes : List<MemeDef>` (field) | The ideology's memes — a **small, fixed, hand-picked set** (roughly 1–4). This is the coarse identity of the religion. | Verified | N/A (no Ideo). |
| `Ideo.HasMeme(MemeDef) : bool` | Fast meme membership test. | Verified | N/A. |
| `Ideo.PreceptsListForReading : List<Precept>` | The ideology's **precept instances** — many, fine-grained rules (rituals, roles, meat/apparel/role rules). Generated from memes via `IdeoFoundation.InitPrecepts` / `RandomizePrecepts`. | Verified | N/A. |
| `Ideo.HasPrecept(PreceptDef) : bool` | Precept membership by def. | Verified | N/A. |
| `Ideo.RolesListForReading : List<Precept_Role>` | The ideology's roles (leader/moralist etc.), a precept subtype. | Verified | N/A. |
| `MemeDef` (Def) | The meme catalog def. Carries `requireOne : List<List<PreceptDef>>`, `factionWhitelist`, `exclusionTags`, `symbolPacks`, etc. Memes *drive* which precepts are legal. | Verified | Def not loaded without Ideology. |
| `PreceptDef` (Def) | The precept catalog def. Carries `associatedMemes`, `conflictingMemes`, `requiredMemes : List<MemeDef>` — i.e. precepts are keyed back to memes. | Verified | Not loaded. |
| `Precept` (class) / `Precept_Ritual`, `Precept_Role` | Runtime precept instances living on the `Ideo`. | Verified | N/A. |
| `MemeWeight { MemeDef meme; float selectionWeight }` | How memes are weighted during ideo generation. | Verified | N/A. |

**Similarity answer (owner's question):** compare two ideologies primarily on **memes**,
not precepts. Rationale grounded in the verified structure:
- `Ideo.memes` is a **small bounded set** (a handful) → cheap Jaccard/overlap.
- Precepts are **numerous and derived** from the memes (`IdeoFoundation.InitPrecepts`,
  and `PreceptDef.requiredMemes`/`associatedMemes` tie each precept to memes), so precept
  overlap is largely a noisy, higher-dimensional restatement of meme overlap.
- Memes carry the ideological "axis" meaning (e.g. supremacist, cannibal, tunneler,
  collectivist) that a demographic model wants.
Use `Ideo.memes` + `HasMeme` for the coarse cultural-distance axis; reserve
`PreceptsListForReading`/`HasPrecept` for fine-grained rule checks (e.g. "does this ideo
forbid X") when needed.

---

## A3. Certainty and conversion (existing vanilla difficulty to scale, not replace)

| Type.Member | What it is | Verified? | No-DLC |
|---|---|---|---|
| `Pawn_IdeoTracker.Certainty : float` | Pawn's conviction in its current ideo (0–1). Drives drift/conversion. | Verified | N/A (no ideo). |
| `Pawn_IdeoTracker.CertaintyChangePerDay : float` | Passive certainty drift rate. | Verified | N/A. |
| `Pawn_IdeoTracker.OffsetCertainty(float)` / `Debug_ReduceCertainty(float)` | Nudge certainty. | Verified | N/A. |
| `Pawn_IdeoTracker.IdeoConversionAttempt(float certaintyReduction, Ideo initiatorIdeo, bool applyCertaintyFactor) : bool` | The core conversion entry point. | Verified | N/A. |
| `Pawn_IdeoTracker.TryJoinIdeoFromExposures() : bool`, `IncreaseIdeoExposureIfBaby(...)`, `BabyIdeoExposureSorted/Total` | Babies adopt ideology by exposure weight — a built-in "who raised them" model. | Verified | N/A. |
| `InteractionWorker_ConvertIdeoAttempt.CertaintyReduction(Pawn initiator, Pawn recipient) : float` (static) | How much certainty a conversion talk removes. | Verified | N/A. |
| `ConversionUtility.ConversionPowerFactor_MemesVsTraits(Pawn, Pawn, StringBuilder)` | Conversion strength scaled by **meme agreement** (again, memes are the axis). | Verified | N/A. |
| `ConversionTuning` (static consts): `PostConversionCertainty`, `ConvertAttempt_BaseCertaintyReduction`, `CertaintyPerDayByMoodCurve`, `InitialCertaintyRange`, `ConversionPowerFactor_AgreeWithMeme`, `ConversionPowerFactor_DisagreeWithMeme`, `ConversionPowerFactor_Min`, `ChildCertaintyChangeFactor` | The tunable constants behind conversion difficulty. | Verified | N/A. |

**Bottom line:** vanilla already models conversion *difficulty* as a certainty-reduction
computation weighted by meme agreement (`ConversionPowerFactor_AgreeWithMeme` vs
`_DisagreeWithMeme`). A regional-demographics model should **scale these existing
factors** (e.g. a regional multiplier on `certaintyReduction` passed to
`IdeoConversionAttempt`, or on the power factors), not build a parallel conversion system.

---

## A4. IdeoManager — bounded or runtime-creatable?

| Type.Member | What it is | Verified? | No-DLC |
|---|---|---|---|
| `IdeoManager.IdeosListForReading : List<Ideo>` | The world's live set of ideologies. | Verified | Empty list. |
| `IdeoManager.IdeosInViewOrder : IEnumerable<Ideo>` | Same set, display-ordered. | Verified | Empty. |
| `IdeoManager.Add(Ideo) : bool` / `Remove(Ideo) : bool` | **Ideos can be added/removed at runtime** → the set is NOT fixed at world-gen. | Verified | No-op. |
| `IdeoManager.GetFactionsWithIdeo(Ideo, bool onlyPrimary, bool onlyNpcFactions) : List<Faction>` | Reverse lookup ideo → factions. | Verified | Empty. |
| `IdeoManager.SortIdeos()`, `RemoveUnusedStartingIdeos()`, `Horaxian` (Anomaly ideo) | Housekeeping + the special Anomaly cult ideo. | Verified | N/A. |
| `IdeoGenerator.GenerateIdeo(IdeoGenerationParms) : Ideo`, `MakeIdeo`, `MakeFixedIdeo`, `GenerateClassicIdeo`, `GenerateNoExpansionIdeo`, `InitLoadedIdeo` (all static) | The factory for making new ideos at runtime; pair with `IdeoManager.Add`. | Verified | N/A. |
| `IdeoGenerationParms` (struct): `forcedMemes : List<MemeDef>`, `disallowedMemes`, `fixedIdeo`, `forNewFluidIdeo`, `forceNoExpansionIdeo` | Controls what a generated ideo may/may not contain — the seam to bias regional ideo generation. | Verified | N/A. |
| `FactionIdeosTracker.ChooseOrGenerateIdeo(IdeoGenerationParms)` | Faction-level generate-or-reuse entry. | Verified | No-op. |

**Answer:** the world ideo set is **runtime-mutable** (bounded only by what's created).
We can inject regionally-flavored ideos at any time via `IdeoGenerator.GenerateIdeo` +
`IdeoManager.Add`, biasing memes through `IdeoGenerationParms.forcedMemes/disallowedMemes`.

---

## C (sex). Gender generation seam

| Type.Member | What it is | Verified? | No-DLC |
|---|---|---|---|
| `PawnGenerationRequest.FixedGender : Nullable<Gender>` (property) | Forces a generated pawn's sex; null = let the generator decide. **This is the sex seam for #37.** | Verified | Works without any DLC (core pawn-gen). |
| `Verse.Gender` (enum): `Male`, `Female`, `None` | The gender enum. | Verified | Core. |
| Normal sex ratio decision | With `FixedGender` null, `PawnGenerator` assigns sex internally (≈50/50 for humanlikes, with race/relation constraints). The exact internal RNG method was **not surfaced** as a public member. | **Field verified; the internal ~50/50 default is UNVERIFIED** (no public method confirmed the ratio). | Core. |
| `PawnKindDef.fixedGender` | Searched the actual 1.6 def XML across all modules — **no vanilla PawnKindDef sets `fixedGender`**, so per-kind sex forcing is effectively unused in vanilla. | Verified absent (grep of live Def XML) | Core. |

**Bottom line:** to drive a regional sex ratio, set
`PawnGenerationRequest.FixedGender` in the #37 generation hook according to a
region-weighted roll. There is no vanilla per-region or per-kind ratio to inherit.

---

## D. The generation seam (#37) and perf

**The hook point (verified):**
`Verse.PawnGenerator.GeneratePawn(PawnGenerationRequest request) : Pawn` — the single
method through which both ideo and xenotype are decided. Overload
`GeneratePawn(PawnKindDef kindDef, Faction faction, Nullable<PlanetTile> tile)` builds a
request and calls the same path.

Ideo-related `PawnGenerationRequest` fields to set (all **verified** as real properties):

| Field | Effect | No-DLC |
|---|---|---|
| `FixedIdeo : Ideo` | Forces the generated pawn's ideology. | Ignored when Ideology off (Ideo would be null). |
| `ForceNoIdeo : bool` | Generate with no ideology. | Safe/no-op without DLC. |
| `ForceNoIdeoGear : bool` | Suppress ideology-driven apparel/gear. | Safe. |
| `KindDef : PawnKindDef`, `Faction : Faction`, `Context : PawnGenerationContext` | Inputs the vanilla ideo/xenotype pickers read. | Core. |

> Note: there is **no** `ideoAtGeneration` property and **no** simple "forced ideo bool"
> beyond the above — the forced-ideo seam is `FixedIdeo` (an `Ideo`), and the
> suppression seams are `ForceNoIdeo` / `ForceNoIdeoGear`. `ideoAtGeneration` from the
> task prompt is **UNVERIFIED / not present** in the corpus.

Relevant helpers (verified): `FactionIdeosTracker.GetRandomIdeoForNewPawn()` is what
vanilla uses to pick a faction member's ideo; a Harmony hook can post-process
`GeneratePawn` or prefix-set `request.FixedIdeo` to bias by region.

**Xenotype fields on the same request** are documented in `Biotech_Xenotype_Model.md`
(`ForcedXenotype`, `ForcedCustomXenotype`, `ForcedXenogenes`, `ForcedEndogenes`).

### D-perf. Safe-to-read-per-tick vs must-cache

| Access | Cost / cadence | Guidance |
|---|---|---|
| `pawn.ideo.Ideo`, `pawn.ideo.Certainty` | Cheap field/property reads. | Safe per-tick if needed, but there's rarely a reason to poll every tick. |
| `pawn.ideo.Ideo.memes` / `HasMeme` | Cheap (small list). | Safe; good for on-demand similarity. |
| `Ideo.PreceptsListForReading`, `RolesListForReading` | Returns a live list; iterating it each tick over many pawns is wasteful. | **Cache** per-ideo; ideos change rarely. |
| `IdeoManager.IdeosListForReading` iteration | O(#ideos) but grows; conversions/removals happen. | **Cache** and invalidate on ideo add/remove. |
| `FactionIdeosTracker.AllIdeos` / `GetRandomIdeoForNewPawn` | Allocates / RNG. | Call only at generation time, never per-tick. |
| Conversion (`IdeoConversionAttempt`, `ConversionUtility.*`) | Heavier (string building, factor math). | Event-driven only (on interaction), never polled. |

**Rule of thumb:** a pawn's *current* ideo/certainty is a cheap read; anything that
enumerates precepts, roles, or the world ideo list should be computed at generation /
region-change time and cached, not recomputed per tick.

---

## Explicitly UNVERIFIED / flagged
- `Pawn.ideo` and `Faction.ideos` **field names**: the *types* (`Pawn_IdeoTracker`,
  `FactionIdeosTracker`) and the ownership relationship are corpus-verified; the exact
  lowercase field identifiers are the well-known standard public fields but were not
  independently dumped as fields.
- `PawnGenerationRequest.ideoAtGeneration` — **not present** in the corpus; the real
  seam is `FixedIdeo`.
- Exact internal sex-ratio RNG (the "≈50/50") — no public member confirmed the ratio.
