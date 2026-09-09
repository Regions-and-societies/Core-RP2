using System;

namespace RegionsAndSocieties.Placement
{
    /// <summary>The biome numbers placement reads, lifted off the BiomeDef so the rule is pure (#56).</summary>
    public struct BiomeTraits
    {
        public float PlantDensity;             // BiomeDef.plantDensity, 0..1
        public float Forageability;            // BiomeDef.forageability, 0..1
        public float TreeDensity;              // BiomeDef.TreeDensity (guarded read), roughly 0..1
        public float MovementDifficulty;       // BiomeDef.movementDifficulty, 1 = open ground, 4 = swamp
        public float DiseaseMtbDays;           // BiomeDef.diseaseMtbDays, 30 = jungle, 90 = desert / ice
        public float SettlementSelectionWeight; // BiomeDef.settlementSelectionWeight, vanilla's own NPC-base preference

        /// <summary>A middle-of-the-road biome, for a tile with no biome at all.</summary>
        public static BiomeTraits Neutral
        {
            get
            {
                return new BiomeTraits
                {
                    PlantDensity = 0.5f, Forageability = 0.5f, TreeDensity = 0.5f,
                    MovementDifficulty = 1f, DiseaseMtbDays = BiomeHabitabilityRules.DiseaseReferenceDays, SettlementSelectionWeight = 1f,
                };
            }
        }
    }

    /// <summary>The faction-independent land values of one tile, weighted by a placement profile.</summary>
    public struct PlacementTileFeatures
    {
        public float Mineral;     // hills and mountains
        public float Nutrition;   // plant density
        public float Forage;      // forageability
        public float Grazing;     // plant density, doubled on flat ground
        public float Biomass;     // tree density (hunting / wood)
        public float Margin;      // max(0, 3 - hospitability): the marginal-land preference input
    }

    /// <summary>A faction's placement weights — the six numbers of its placement profile, as a plain
    /// value so this rule needs no settings type.</summary>
    public struct PlacementWeights
    {
        public float Mineral, Nutrition, Forage, Grazing, Hunting, Margin;

        public PlacementWeights(float mineral, float nutrition, float forage, float grazing, float hunting, float margin)
        {
            Mineral = mineral; Nutrition = nutrition; Forage = forage; Grazing = grazing; Hunting = hunting; Margin = margin;
        }
    }

    /// <summary>
    /// How livable a biome is for a society of a given tech level, and the per-tile land values that
    /// placement weighs (#56). Pure: biome numbers in, scalars out, no game types.
    ///
    /// <para>Before this the placement score read raw <c>plantDensity</c> three times over (nutrition,
    /// grazing and the tree density that tracks it), so swamps and rainforest won outright and boreal
    /// forest sat near the bottom. Habitability multiplies the whole score by three things the old
    /// score never read: vanilla's own <c>settlementSelectionWeight</c> (the designer's intent for where
    /// NPC bases go, which mod biomes set too), the <b>toil</b> of the terrain (<c>movementDifficulty</c>:
    /// clearing jungle or wading a bog is exhausting for a society without machines) and <b>health</b>
    /// (<c>diseaseMtbDays</c>). Tech shrinks the two penalties: an industrial society has machines and
    /// medicine, a spacer one more so; a tribe eats them in full.</para>
    ///
    /// <para><see cref="Crowding"/> is the second half of "more evenly split": as a biome fills up with
    /// settlements relative to the provinces it offers, its candidates score lower, so the next faction
    /// takes the next-best land instead of every nation stacking into the one richest biome.</para>
    /// </summary>
    public static class BiomeHabitabilityRules
    {
        // RimWorld's TechLevel ordinals: Undefined 0, Animal 1, Neolithic 2, Medieval 3, Industrial 4,
        // Spacer 5, Ultra 6, Archotech 7.
        public const int TechIndustrial = 4;
        public const int TechSpacer = 5;

        /// <summary>The disease interval that reads as neutral; longer is healthier, shorter sicker.</summary>
        public const float DiseaseReferenceDays = 60f;
        /// <summary>Health factor floor and cap, so a 90-day desert is only mildly healthier and a 30-day
        /// swamp is halved, never zeroed.</summary>
        public const float HealthFloor = 0.5f;
        public const float HealthCap = 1.25f;
        /// <summary>How hard a biome's occupancy pushes back: at weight 1 a biome whose settlements equal
        /// its settleable provinces scores half.</summary>
        public const float CrowdingWeight = 1f;

        /// <summary>How hard toil and disease bite at a tech level: fully pre-industrial, half with
        /// machines and medicine, a quarter for a spacer society.</summary>
        public static float PenaltyExponent(int techLevel)
        {
            if (techLevel >= TechSpacer) return 0.25f;
            if (techLevel >= TechIndustrial) return 0.5f;
            return 1f;
        }

        /// <summary>1 on open ground, falling with movement difficulty: a swamp (4) is a quarter for a
        /// tribe, a half for an industrial society.</summary>
        public static float Toil(float movementDifficulty, int techLevel)
        {
            float md = movementDifficulty < 1f ? 1f : movementDifficulty;
            return (float)Math.Pow(md, -PenaltyExponent(techLevel));
        }

        /// <summary>Disease interval against the 60-day reference, clamped, raised to the tech penalty.
        /// An unset (non-positive) interval reads as neutral.</summary>
        public static float Health(float diseaseMtbDays, int techLevel)
        {
            if (diseaseMtbDays <= 0f) return 1f;
            float ratio = diseaseMtbDays / DiseaseReferenceDays;
            if (ratio < HealthFloor) ratio = HealthFloor;
            if (ratio > HealthCap) ratio = HealthCap;
            return (float)Math.Pow(ratio, PenaltyExponent(techLevel));
        }

        /// <summary>The livability multiplier on a tile's score: vanilla's settlement weight times toil
        /// times health. A biome vanilla never places bases in scores 0.</summary>
        public static float Habitability(BiomeTraits b, int techLevel)
        {
            float weight = b.SettlementSelectionWeight < 0f ? 0f : b.SettlementSelectionWeight;
            return weight * Toil(b.MovementDifficulty, techLevel) * Health(b.DiseaseMtbDays, techLevel);
        }

        /// <summary>Mineral value by hilliness class: 0 flat, 1 small hills, 2 large hills, 3 mountainous.</summary>
        public static float Mineral(int hillClass)
        {
            switch (hillClass)
            {
                case 1: return 1.0f;
                case 2: return 2.0f;
                case 3: return 3.0f;
                default: return 0.5f;
            }
        }

        /// <summary>The faction-independent land values of a tile.</summary>
        public static PlacementTileFeatures Features(BiomeTraits b, int hillClass)
        {
            float nutrition = b.PlantDensity;
            float forage = b.Forageability;
            float hospitability = nutrition * 2f + forage;
            return new PlacementTileFeatures
            {
                Mineral = Mineral(hillClass),
                Nutrition = nutrition,
                Forage = forage,
                Grazing = hillClass == 0 ? nutrition * 2f : nutrition,
                Biomass = b.TreeDensity,
                Margin = Math.Max(0f, 3.0f - hospitability),
            };
        }

        /// <summary>
        /// A tile's placement score for a profile: habitability times the weighted resources plus the
        /// marginal-land term. Margin sits inside the multiplier on purpose — a raider band wants the
        /// fringe of livable land (arid shrubland, a desert edge), not an ice sheet; with the margin
        /// term outside, near-zero habitability ground was exactly where the marginal preference won.
        /// </summary>
        public static float Score(PlacementTileFeatures f, float habitability, PlacementWeights w)
        {
            float land = w.Mineral * f.Mineral
                + w.Nutrition * f.Nutrition
                + w.Forage * f.Forage
                + w.Grazing * f.Grazing
                + w.Hunting * f.Biomass;
            if (w.Margin > 0f) land += w.Margin * f.Margin;
            return habitability * land;
        }

        /// <summary>
        /// The occupancy push-back on a candidate in a biome that already holds <paramref name="settled"/>
        /// settlements against <paramref name="available"/> settleable provinces: <c>1 / (1 + weight ×
        /// settled / available)</c>. 1 while a biome is empty, half when it is as full as it is big, and
        /// never zero, so a biome is discouraged, not fenced off. A biome with no provinces on offer
        /// (unknown availability) is not pushed back.
        /// </summary>
        public static float Crowding(int settled, int available)
        {
            if (available <= 0 || settled <= 0) return 1f;
            return 1f / (1f + CrowdingWeight * settled / available);
        }
    }
}
