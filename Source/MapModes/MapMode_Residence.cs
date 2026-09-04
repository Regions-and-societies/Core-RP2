using MapModeFramework;
using RegionsAndSocieties.Demographics;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties
{
    /// <summary>
    /// Shades each populated tile by how its people are housed — the residence tier from
    /// <see cref="ResidenceRules"/>. A rural homestead (an extended family on wide land) reads green; a
    /// city (nuclear households packed onto small lots) reads red; village and town sit between. The label
    /// shows the number of residences, so the overlay reads population as HOMES, not head count.
    ///
    /// <para>Materials — one per tier — are pre-built on the main thread in <see cref="DoPreRegenerate"/>,
    /// so the worker-thread mesh build only reads them.</para>
    /// </summary>
    [StaticConstructorOnStartup]
    public class MapMode_Residence : MapMode
    {
        // One colour per tier, rural to urban: land-rich green -> packed-in red.
        private static readonly Color[] TierBase =
        {
            new Color(0.35f, 0.62f, 0.30f, 0.55f),   // Homestead — rural, land-rich
            new Color(0.62f, 0.72f, 0.24f, 0.58f),   // Village
            new Color(0.88f, 0.58f, 0.18f, 0.62f),   // Town
            new Color(0.84f, 0.24f, 0.28f, 0.68f),   // City — dense, small lots
        };
        private static Material[] tierMats;

        public MapMode_Residence() { }
        public MapMode_Residence(MapModeDef def) : base(def) { }

        public override WorldLayer_MapMode WorldLayer => WorldLayer_MapMode_Terrain.Instance;
        public override bool CanToggleWater => false;

        public override void DoPreRegenerate()
        {
            base.DoPreRegenerate();
            PopulationDensityUtility.EnsureCache();
            if (tierMats != null) return;
            tierMats = new Material[TierBase.Length];
            for (int i = 0; i < TierBase.Length; i++)
            {
                Color c = TierBase[i];
                Material m = (ShaderDatabase.MetaOverlay != null && BaseContent.WhiteTex != null)
                    ? MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.MetaOverlay, c, 3510)
                    : SolidColorMaterials.SimpleSolidColorMaterial(c);
                tierMats[i] = m ?? BaseContent.WhiteMat;
            }
        }

        public override Material GetMaterial(int tile)
        {
            if (tierMats == null || Find.WorldGrid == null || tile < 0 || tile >= Find.WorldGrid.TilesCount)
                return BaseContent.ClearMat;
            Tile t = Find.WorldGrid[tile];
            if (t == null || t.WaterCovered) return BaseContent.ClearMat;
            int pop = PopulationDensityUtility.GetPopulationAtTile(tile);
            if (pop <= 0) return BaseContent.ClearMat;
            return tierMats[(int)ResidenceRules.TierFor(ResidenceRules.Urbanization(pop))];
        }

        public override string GetTileLabel(int tile)
        {
            int pop = PopulationDensityUtility.GetSourcePopulationAtTile(tile);
            if (pop <= 0) return null;
            return ResidenceRules.For(pop).residences.ToString();
        }

        public override string GetTooltip(int tile)
        {
            if (Find.WorldGrid == null || tile < 0 || tile >= Find.WorldGrid.TilesCount) return null;
            Tile t = Find.WorldGrid[tile];
            if (t == null || t.WaterCovered) return null;
            int pop = PopulationDensityUtility.GetPopulationAtTile(tile);
            if (pop <= 0) return "No residents here";
            ResidenceProfile r = ResidenceRules.For(pop);
            return $"{r.tier}\n{r.residences} residences · {r.occupancy:0.0} per home\n{pop} people · land/person {r.landPerPawn:0.00}";
        }
    }
}
