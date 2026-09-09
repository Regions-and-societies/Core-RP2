using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RegionsAndSocieties
{
    /// <summary>
    /// Guarded reads of <see cref="BiomeDef"/> values whose vanilla getters can throw on a malformed def.
    /// <see cref="BiomeDef.TreeDensity"/> builds the biome's plant-commonality cache on first use, and a
    /// duplicated wild-plant record makes that build throw (see <see cref="Compat.BiomeDefRepair"/>, which
    /// fixes the lists at startup). World generation reads tree density for every land tile, so a single
    /// bad biome must degrade to a number, never abort the caller.
    /// </summary>
    public static class BiomeSafe
    {
        private static readonly Dictionary<BiomeDef, float> treeDensity = new Dictionary<BiomeDef, float>();

        public static float TreeDensity(BiomeDef biome)
        {
            if (biome == null) return 0f;
            if (treeDensity.TryGetValue(biome, out float cached)) return cached;
            float value;
            try
            {
                value = biome.TreeDensity;
            }
            catch (Exception ex)
            {
                // Vanilla leaves its partially built cache in place after the throw, so a second read
                // returns what did build; if even that fails, the biome counts as treeless.
                try { value = biome.TreeDensity; }
                catch { value = 0f; }
                Log.Warning($"[RegionsAndSocieties] BiomeDef '{biome.defName}': TreeDensity threw ({ex.GetType().Name}: {ex.Message}); using {value:0.00}. The biome's wild-plant list is malformed (usually a plant patched in twice).");
            }
            treeDensity[biome] = value;
            return value;
        }

        private static readonly Dictionary<BiomeDef, Placement.BiomeTraits> traits = new Dictionary<BiomeDef, Placement.BiomeTraits>();

        /// <summary>The placement-relevant numbers of a biome (#56), cached per def; a null biome reads
        /// as neutral, middle-of-the-road land.</summary>
        public static Placement.BiomeTraits Traits(BiomeDef biome)
        {
            if (biome == null) return Placement.BiomeTraits.Neutral;
            if (traits.TryGetValue(biome, out Placement.BiomeTraits cached)) return cached;
            var t = new Placement.BiomeTraits
            {
                PlantDensity = biome.plantDensity,
                Forageability = biome.forageability,
                TreeDensity = TreeDensity(biome),
                MovementDifficulty = biome.movementDifficulty,
                DiseaseMtbDays = biome.diseaseMtbDays,
                SettlementSelectionWeight = biome.settlementSelectionWeight,
            };
            traits[biome] = t;
            return t;
        }

        /// <summary>Hilliness as the 0..3 class the placement rules read: flat, small hills, large hills,
        /// mountainous. Impassable is never a placement candidate and reads as mountainous.</summary>
        public static int HillClass(Hilliness hilliness)
        {
            switch (hilliness)
            {
                case Hilliness.SmallHills: return 1;
                case Hilliness.LargeHills: return 2;
                case Hilliness.Mountainous: return 3;
                case Hilliness.Impassable: return 3;
                default: return 0;
            }
        }
    }
}
