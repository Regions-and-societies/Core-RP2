# Territory Ownership

Who holds a province is a **score**, not a flag. This page explains how that score is built and what the resulting states mean.

---

## The four-tier ladder

Each faction's score in a province, from 0 to 1, places it on a ladder:

| Tier | Score | Meaning |
|---|---|---|
| **Loose claim** | below 30% | A presence — border bleed or a stray camp — not a claim. |
| **Legitimate claim** | 30–50% | A real claim, contestable by other legitimate claims. |
| **Loose ownership** | 51–70% | The clear majority owner, still short of exclusive. |
| **Exclusive** | 71%+ | Owns the province outright. Blocks even a player start. |

The province's overall status follows from the claims present:

- **Held** — one faction has loose ownership or better.
- **Contested** — two or more factions hold legitimate claims and nobody has a majority.
- **Unclaimed wilderness** — nobody reaches a legitimate claim. This is a real outcome, not a gap in the model: a province with a lone trading post in the corner is genuinely not anybody's territory.

Every province also carries an **unclaimed share** — the portion of it no faction has accounted for. In a sparsely settled region that number should be substantial, and the province map mode shows it.

---

## What contributes to a faction's score

Several independent components, each a share of the whole rather than a flat bonus:

- **Primary holdings** (up to 30%) — settlements, plus military installations at reduced weight. A faction with two of the four settlements in a province takes half of this component, not all of it.
- **Secondary holdings** (up to 15%, plus a 5% bonus) — outposts, plus camps at reduced weight, with the bonus going to whoever has most.
- **Border influence** (up to 40%) — how much of the province's edge each faction presses on. Land you share with a rival-held region is that rival's pressure on you; mountains, water and open frontier count for the province's own owner as secure, self-bordering ground — but only once that owner already has a major claim from real holdings, so an empty mountain-ringed region does not inflate whoever happens to border it.
- **External perimeter** (10%) — a bonus to whichever faction dominates the province's outward-facing edge.
- **Demographics** — **contributes nothing.** See below.

Because each component is a share, the totals across all factions in a province cannot exceed the whole, and what is left over is the unclaimed share. A settled province reads as firmly held by its settlement's faction, shielded by whatever mountains and coast wrap around it, and pressed only where a rival's territory actually touches it. A province with no settlement of its own tops out well short of exclusive, so empty land can never fence a new colony out.

---

## Demographics is switched off

This component is meant to express what proportion of a region's people are a given faction's. It did not do that.

Underneath the real path sat a fallback that awarded the component's **full weight** for simply owning a settlement in the province — which the primary-holdings component already measures. The same fact was counted twice, the second time under a name implying something entirely different, and the fallback fired on most installs.

It now contributes zero, the fallback is removed rather than left dormant, and no weight is reserved for it — the other components fill the whole budget. The provider registry it will eventually read (`IRegionDemographicProvider` — see the [Developer's Guide](Developers_Guide)) is real and public, but the ownership component stays stubbed until a release restores it honestly. The practical effect is that ownership is scored only on things the mod actually models.

---

## Why this is one calculation

Placement, expansion, the map shading and the world inspect pane all read the **same** ownership answer, and the tier cutoffs live in one place. That is deliberate: when three systems each compute "who owns this" separately, they eventually disagree, and the player sees a tile refused for belonging to a faction the inspect pane says does not hold it. With the setting enabled (or Dev Mode), the region panel shows the full derivation of every score.
