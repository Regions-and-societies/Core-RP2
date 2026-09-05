using System;
using System.Collections.Generic;
using RimWorld;
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
    }
}
