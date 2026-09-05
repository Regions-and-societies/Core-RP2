using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RegionsAndSocieties.Compat
{
    /// <summary>
    /// Repairs <see cref="BiomeDef"/>s whose wild-plant or wild-animal list carries the same def twice —
    /// the usual cause is a patch that adds a plant the biome already lists. Vanilla builds its
    /// commonality caches with <c>Dictionary.Add</c>, so the first read of such a biome throws
    /// <c>ArgumentException: An item with the same key has already been added</c> and leaves a half-built
    /// cache behind. Whoever reads first eats the throw: in 0.3.0 that became R&amp;S's world partition
    /// (<c>BiomeDef.TreeDensity</c> during region merging), which aborted the whole faction world-gen
    /// step and produced worlds with no factions at all.
    ///
    /// <para>Duplicates are merged into the first record (commonalities averaged, matching vanilla's own
    /// merge for <c>plant.wildBiomes</c>) and every repair is logged naming the biome and the def, so the
    /// conflicting patch can be reported upstream. Nothing is removed from a well-formed biome.</para>
    /// </summary>
    [StaticConstructorOnStartup]
    public static class BiomeDefRepair
    {
        static BiomeDefRepair()
        {
            try
            {
                RepairAll();
            }
            catch (Exception ex)
            {
                Log.Warning("[RegionsAndSocieties] BiomeDef duplicate-entry repair failed (worldgen is still guarded): " + ex);
            }
        }

        /// <summary>Merge duplicate wild-plant / wild-animal records on every biome. Returns the number of merges.</summary>
        public static int RepairAll()
        {
            var plantsField = AccessTools.Field(typeof(BiomeDef), "wildPlants");
            var animalsField = AccessTools.Field(typeof(BiomeDef), "wildAnimals");
            var plantCacheField = AccessTools.Field(typeof(BiomeDef), "cachedPlantCommonalities");
            var animalCacheField = AccessTools.Field(typeof(BiomeDef), "cachedAnimalCommonalities");
            int merged = 0;
            foreach (BiomeDef biome in DefDatabase<BiomeDef>.AllDefsListForReading)
            {
                int plants = Dedupe(biome, plantsField?.GetValue(biome) as List<BiomePlantRecord>,
                    r => r.plant, r => r.commonality, (r, c) => r.commonality = c, "wild plant");
                int animals = Dedupe(biome, animalsField?.GetValue(biome) as List<BiomeAnimalRecord>,
                    r => r.animal, r => r.commonality, (r, c) => r.commonality = c, "wild animal");
                // Drop any cache a load-time reader may already have built from the broken list.
                if (plants > 0) plantCacheField?.SetValue(biome, null);
                if (animals > 0) animalCacheField?.SetValue(biome, null);
                merged += plants + animals;
            }
            if (merged > 0)
            {
                Log.Message($"[RegionsAndSocieties] Merged {merged} duplicated biome wild-plant/animal record(s); see the warnings above for the biomes and defs involved.");
            }
            return merged;
        }

        private static int Dedupe<TRec, TDef>(BiomeDef biome, List<TRec> records, Func<TRec, TDef> key,
            Func<TRec, float> getCommonality, Action<TRec, float> setCommonality, string kind)
            where TRec : class where TDef : Def
        {
            if (records == null || records.Count < 2) return 0;
            var firstByDef = new Dictionary<TDef, TRec>();
            var counts = new Dictionary<TDef, int>();
            int merged = 0;
            for (int i = 0; i < records.Count; i++)
            {
                TRec rec = records[i];
                TDef def = rec != null ? key(rec) : null;
                if (def == null) continue;
                if (firstByDef.TryGetValue(def, out TRec first))
                {
                    setCommonality(first, (getCommonality(first) + getCommonality(rec)) / 2f);
                    counts[def]++;
                    records.RemoveAt(i);
                    i--;
                    merged++;
                }
                else
                {
                    firstByDef[def] = rec;
                    counts[def] = 1;
                }
            }
            foreach (var kv in counts)
            {
                if (kv.Value < 2) continue;
                Log.Warning($"[RegionsAndSocieties] BiomeDef '{biome.defName}' lists {kind} '{kv.Key.defName}' {kv.Value} times — a patch added a def the biome already had. Merged the duplicates so the game's commonality cache can build; without this repair the biome's map generation and R&S world generation throw 'An item with the same key has already been added'. Report the duplicate to the mod that patches '{kv.Key.defName}' into '{biome.defName}'.");
            }
            return merged;
        }
    }
}
