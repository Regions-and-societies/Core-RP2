namespace RegionsAndSocieties.Sizing
{
    /// <summary>
    /// The kind of outpost a tile is suited to, chosen so a seeded outpost reads as belonging where it
    /// stands: a mine in the mountains, a farm near the capital, a scavenger den on a pirate frontier.
    ///
    /// This is R&amp;S's own vocabulary, deliberately NOT a Vanilla Outposts Expanded type — the mapping
    /// from an archetype onto a concrete VOE def lives in the patch (<c>VoeOutpostCreator</c>), the one
    /// place allowed to name a foreign mod's types. Keeping the choice abstract here is what lets the
    /// rule stay pure and testable, and lets a second creator (Empire, vanilla) reuse the judgement.
    /// </summary>
    public enum OutpostArchetype
    {
        /// <summary>No specialisation fits — a plain habitation. The constraint-free fallback (index 0).</summary>
        Encampment = 0,
        Mining,
        Logging,
        Farming,
        Hunting,
        Drilling,     // extraction from arid/desert ground
        Trading,      // a market post — near a capital, developed factions
        Production,   // workshops — industrial+
        Science,      // a research post — industrial+
        Town,         // a civic satellite of the capital
        Scavenging,   // salvage — interior/frontier, favoured by raiders
        Factory,      // heavy manufacture — industrial+
        Artillery,    // a gun emplacement — frontier, industrial+
        Defensive,    // a fortlet — frontier, favoured by raiders
    }

    /// <summary>
    /// The tile facts the archetype choice reads, as plain numbers so the rule needs no <c>Find</c>, no
    /// <c>WorldGrid</c>, no Unity. The seeding facade fills this from the world; the rule decides. The
    /// position/faction fields (#18) are optional: when no anchor was resolved (<see cref="anchorTier"/>
    /// left <see cref="SettlementTier.None"/>), the choice degrades to terrain only, the pre-#18 behaviour.
    /// </summary>
    public struct TileFeatures
    {
        /// <summary>0 flat, 1 small hills, 2 large hills, 3 mountainous.</summary>
        public int hilliness;
        /// <summary>Biome plant density, ~0..1.</summary>
        public float plantDensity;
        /// <summary>Biome tree density, ~0..1.</summary>
        public float treeDensity;
        /// <summary>Biome animal density, ~0..1 (wildlife abundance).</summary>
        public float animalDensity;
        /// <summary>How mineable the tile reads, 0..1 (from hilliness / mineral resource pool).</summary>
        public float mineralsFraction;
        /// <summary>Whether the tile touches water.</summary>
        public bool coastal;

        // --- position & faction context (#18); absent when anchorTier == None ---

        /// <summary>Normalised distance from the province's anchor settlement: 0 at the capital core,
        /// 1 at the province edge.</summary>
        public float distanceToAnchor;
        /// <summary>The tier of the settlement anchoring this province. <see cref="SettlementTier.None"/>
        /// means no anchor context — the choice falls back to terrain only.</summary>
        public SettlementTier anchorTier;
        /// <summary>The anchor faction's tech level (RimWorld TechLevel ordinal: 2 Neolithic .. 7 Archotech).</summary>
        public int techLevel;
        /// <summary>Whether the anchor faction is a permanent enemy (pirates / hostiles).</summary>
        public bool permanentEnemy;
    }

    /// <summary>
    /// Chooses an outpost archetype for a tile (#18). Pure: same inputs, same answer, always.
    ///
    /// <para>With no anchor context it is a terrain priority chain (the pre-#18 behaviour, kept as the
    /// graceful-degradation path). With context it is a weighted scorer: for each archetype,
    /// <c>score = terrainAllowed ? positionWeight × factionWeight : 0</c>, and the argmax wins with a
    /// deterministic enum-order tiebreak, falling back to Encampment. So cropland and civic posts cluster
    /// near capitals, extraction sits on the periphery, and tribal vs industrial vs pirate factions carry
    /// visibly different outpost mixes. First-pass weights, tuned in-game against the seeding report.</para>
    /// </summary>
    public static class OutpostArchetypeRules
    {
        public const int ArchetypeCount = 14;

        // Terrain thresholds. Named so a tuning pass changes a number, not a buried literal.
        public const int MiningHilliness = 2;          // large hills or mountainous
        public const float MiningMineralsFraction = 0.60f;
        public const float LoggingTreeDensity = 0.40f;
        public const float FarmingPlantDensity = 0.50f;
        public const int FarmingMaxHilliness = 1;      // farms want flat-to-rolling ground
        public const float HuntingAnimalDensity = 0.50f;
        public const float AridPlantDensity = 0.25f;   // desert = little plant cover ...
        public const float AridTreeDensity = 0.20f;    // ... and few trees -> a drilling site

        // Faction tech bands (RimWorld TechLevel ordinals).
        public const int IndustrialTech = 4;   // Production/Science/Factory/Artillery need this or better
        public const int PreIndustrialTech = 3;   // Neolithic/Medieval get the rural bonus

        public static OutpostArchetype Choose(TileFeatures f)
        {
            // No anchor resolved for this province -> terrain only (pre-#18 behaviour, the degrade path).
            if (f.anchorTier == SettlementTier.None)
                return ChooseTerrainOnly(f);
            return ChooseWeighted(f);
        }

        // --- terrain-only priority chain (degrade path) ------------------------

        private static OutpostArchetype ChooseTerrainOnly(TileFeatures f)
        {
            if (f.hilliness >= MiningHilliness || f.mineralsFraction >= MiningMineralsFraction)
                return OutpostArchetype.Mining;
            if (f.treeDensity >= LoggingTreeDensity)
                return OutpostArchetype.Logging;
            if (f.plantDensity >= FarmingPlantDensity && f.hilliness <= FarmingMaxHilliness)
                return OutpostArchetype.Farming;
            if (f.animalDensity >= HuntingAnimalDensity)
                return OutpostArchetype.Hunting;
            return OutpostArchetype.Encampment;
        }

        // --- weighted scorer (with context) ------------------------------------

        private static OutpostArchetype ChooseWeighted(TileFeatures f)
        {
            OutpostArchetype best = OutpostArchetype.Encampment;
            float bestScore = -1f;
            // Enum order, strict '>' — the first archetype at the max score wins, so ties are deterministic.
            for (int i = 0; i < ArchetypeCount; i++)
            {
                OutpostArchetype a = (OutpostArchetype)i;
                if (!TerrainAllows(a, f)) continue;
                float score = PositionWeight(a, f.distanceToAnchor) * FactionWeight(a, f.techLevel, f.permanentEnemy);
                if (score > bestScore) { bestScore = score; best = a; }
            }
            return best;
        }

        /// <summary>Whether the tile physically supports an archetype. Extraction/agriculture are terrain-
        /// gated; the civic and industrial posts can stand on any habitable tile and are placed by position
        /// and faction instead.</summary>
        public static bool TerrainAllows(OutpostArchetype a, TileFeatures f)
        {
            switch (a)
            {
                case OutpostArchetype.Mining:   return f.hilliness >= MiningHilliness || f.mineralsFraction >= MiningMineralsFraction;
                case OutpostArchetype.Logging:  return f.treeDensity >= LoggingTreeDensity;
                case OutpostArchetype.Farming:  return f.plantDensity >= FarmingPlantDensity && f.hilliness <= FarmingMaxHilliness;
                case OutpostArchetype.Hunting:  return f.animalDensity >= HuntingAnimalDensity;
                case OutpostArchetype.Drilling: return f.plantDensity < AridPlantDensity && f.treeDensity < AridTreeDensity;
                default:                        return true;   // Encampment + civic/industrial: any habitable tile
            }
        }

        private enum Zone { Core = 0, Interior = 1, Frontier = 2 }

        /// <summary>The band of the province an archetype belongs in: civic/cropland at the capital core,
        /// generic habitation in the interior, extraction and fortification on the frontier.</summary>
        private static Zone ZoneOf(OutpostArchetype a)
        {
            switch (a)
            {
                case OutpostArchetype.Farming:
                case OutpostArchetype.Town:
                case OutpostArchetype.Trading:
                case OutpostArchetype.Science:
                case OutpostArchetype.Production:
                case OutpostArchetype.Factory:
                    return Zone.Core;
                case OutpostArchetype.Hunting:
                case OutpostArchetype.Encampment:
                case OutpostArchetype.Scavenging:
                    return Zone.Interior;
                default:   // Mining, Logging, Drilling, Artillery, Defensive
                    return Zone.Frontier;
            }
        }

        private static Zone ZoneAt(float distanceToAnchor)
        {
            if (distanceToAnchor < 0.34f) return Zone.Core;
            if (distanceToAnchor < 0.67f) return Zone.Interior;
            return Zone.Frontier;
        }

        /// <summary>How well an archetype's preferred band matches where the tile sits: full weight in its
        /// own band, less in an adjacent one, least at the opposite end.</summary>
        private static float PositionWeight(OutpostArchetype a, float distanceToAnchor)
        {
            int gap = ZoneOf(a) - ZoneAt(distanceToAnchor);
            if (gap < 0) gap = -gap;
            return gap == 0 ? 1.6f : (gap == 1 ? 0.9f : 0.4f);
        }

        /// <summary>How well a faction runs an archetype: an industrial gate on the advanced posts, a rural
        /// bonus for pre-industrial factions, and a raider profile that favours salvage/defence/mining over
        /// civic work. 0 means the faction cannot field it at all.</summary>
        private static float FactionWeight(OutpostArchetype a, int techLevel, bool permanentEnemy)
        {
            if (RequiresIndustrial(a) && techLevel < IndustrialTech) return 0f;

            float w = 1f;
            if (permanentEnemy)
            {
                if (RaiderFavoured(a)) w *= 1.6f;
                else if (Civic(a)) w *= 0.35f;
            }
            if (techLevel <= PreIndustrialTech && RuralFavoured(a)) w *= 1.35f;
            if (techLevel >= IndustrialTech && DevelopedFavoured(a)) w *= 1.25f;
            return w;
        }

        private static bool RequiresIndustrial(OutpostArchetype a)
            => a == OutpostArchetype.Production || a == OutpostArchetype.Science
            || a == OutpostArchetype.Factory || a == OutpostArchetype.Artillery;

        private static bool RaiderFavoured(OutpostArchetype a)
            => a == OutpostArchetype.Scavenging || a == OutpostArchetype.Defensive || a == OutpostArchetype.Mining;

        private static bool Civic(OutpostArchetype a)
            => a == OutpostArchetype.Trading || a == OutpostArchetype.Science || a == OutpostArchetype.Town
            || a == OutpostArchetype.Production || a == OutpostArchetype.Factory || a == OutpostArchetype.Farming;

        private static bool RuralFavoured(OutpostArchetype a)
            => a == OutpostArchetype.Hunting || a == OutpostArchetype.Farming || a == OutpostArchetype.Logging;

        private static bool DevelopedFavoured(OutpostArchetype a)
            => a == OutpostArchetype.Trading || a == OutpostArchetype.Science || a == OutpostArchetype.Town
            || a == OutpostArchetype.Production || a == OutpostArchetype.Factory;
    }
}
