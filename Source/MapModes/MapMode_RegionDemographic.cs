using MapModeFramework;
using RegionsAndSocieties.Demographics;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties
{
    /// <summary>
    /// Shared plumbing for every region-demographic map overlay (0.2.0, #10–#16). Each axis overlay —
    /// age, sex, xenotype, and the tiered ones to come — reads the same per-region aggregate
    /// (<see cref="RegionDemographicsUtility.ForRegion"/>), paints the terrain layer, leaves water and
    /// unsettled land unshaded, and answers a hover with that axis's region summary. This base carries
    /// all of that so a concrete overlay only says what value it shows and how to colour it.
    /// </summary>
    public abstract class MapMode_RegionDemographic : MapMode
    {
        protected MapMode_RegionDemographic() { }
        protected MapMode_RegionDemographic(MapModeDef def) : base(def) { }

        public override WorldLayer_MapMode WorldLayer => WorldLayer_MapMode_Terrain.Instance;
        public override bool CanToggleWater => false;

        public override void DoPreRegenerate()
        {
            base.DoPreRegenerate();
            EnsureMaterials();
            PopulationDensityUtility.EnsureCache();   // ForRegion keys off the same population cache version
        }

        /// <summary>Build any materials this overlay paints with, once. May be a no-op for overlays that
        /// build their materials lazily (e.g. one per xenotype).</summary>
        protected virtual void EnsureMaterials() { }

        /// <summary>The settled land region under a tile, or null when there is nothing to shade — water,
        /// off-map, non-land, or unsettled wilderness.</summary>
        protected static RegionDemographics DemoForTile(int tile)
        {
            GeographicProvince province = ProvinceForTile(tile);
            if (province == null) return null;
            RegionDemographics demo = RegionDemographicsUtility.ForRegion(province);
            return demo.settledTiles > 0 ? demo : null;
        }

        /// <summary>The land province under a tile (any settlement state), or null for water/off-map/non-land.</summary>
        protected static GeographicProvince ProvinceForTile(int tile)
        {
            if (Find.World == null || Find.WorldGrid == null || tile < 0 || tile >= Find.WorldGrid.TilesCount) return null;
            if (Find.WorldGrid[tile].WaterCovered) return null;
            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            GeographicProvince province = mgr?.GetProvinceForTile(tile);
            return (province != null && province.provinceType == ProvinceType.Land) ? province : null;
        }

        public override Material GetMaterial(int tile)
        {
            RegionDemographics demo = DemoForTile(tile);
            if (demo == null) return BaseContent.ClearMat;
            return MaterialForRegion(demo) ?? BaseContent.ClearMat;
        }

        public override string GetTileLabel(int tile)
        {
            RegionDemographics demo = DemoForTile(tile);
            return demo != null ? LabelForRegion(demo) : null;
        }

        public override string GetTooltip(int tile)
        {
            GeographicProvince province = ProvinceForTile(tile);
            return province != null ? SummaryFor(province) : null;
        }

        /// <summary>The overlay colour for a region, or null to leave it unshaded (e.g. an axis with no
        /// data because its DLC is off).</summary>
        protected abstract Material MaterialForRegion(RegionDemographics demo);

        /// <summary>The short on-tile label for a region, or null for none.</summary>
        protected abstract string LabelForRegion(RegionDemographics demo);

        /// <summary>The hover/panel summary for a region's axis.</summary>
        protected abstract string SummaryFor(GeographicProvince province);

        /// <summary>The standard overlay material for a colour: a translucent meta-overlay quad, with the
        /// same solid-colour and white-material fallbacks every overlay used before this base existed.</summary>
        protected static Material MakeOverlayMaterial(Color color)
        {
            Material mat = null;
            if (ShaderDatabase.MetaOverlay != null && BaseContent.WhiteTex != null)
            {
                mat = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.MetaOverlay, color, 3510);
            }
            if (mat == null) mat = SolidColorMaterials.SimpleSolidColorMaterial(color);
            if (mat == null) mat = BaseContent.WhiteMat;
            return mat;
        }
    }

    /// <summary>
    /// A region overlay that shades by a single scalar falling into ordered bands — a median age, a sex
    /// fraction, an SES or education tier. A concrete overlay supplies the band colours, the band
    /// boundaries, and how to read the scalar off a region; this handles material construction, band
    /// selection and the unshaded "no value" case.
    /// </summary>
    public abstract class MapMode_RegionScalarBanded : MapMode_RegionDemographic
    {
        private Material[] bandMats;

        protected MapMode_RegionScalarBanded() { }
        protected MapMode_RegionScalarBanded(MapModeDef def) : base(def) { }

        /// <summary>One colour per band, low to high.</summary>
        protected abstract Color[] BandColors { get; }

        /// <summary>Inclusive upper bounds of every band except the last; length = BandColors.Length - 1.</summary>
        protected abstract float[] BandUpperBounds { get; }

        /// <summary>The scalar to shade by for a region.</summary>
        protected abstract float ValueFor(RegionDemographics demo);

        /// <summary>Whether this region has a value to shade at all; false leaves it unshaded (default true).</summary>
        protected virtual bool HasValue(RegionDemographics demo) => true;

        protected override void EnsureMaterials()
        {
            if (bandMats != null) return;
            Color[] colors = BandColors;
            bandMats = new Material[colors.Length];
            for (int i = 0; i < colors.Length; i++) bandMats[i] = MakeOverlayMaterial(colors[i]);
        }

        protected int BandFor(float value)
        {
            float[] uppers = BandUpperBounds;
            for (int i = 0; i < uppers.Length; i++)
                if (value <= uppers[i]) return i;
            return BandColors.Length - 1;
        }

        protected override Material MaterialForRegion(RegionDemographics demo)
        {
            if (!HasValue(demo)) return null;
            if (bandMats == null) EnsureMaterials();
            return bandMats[BandFor(ValueFor(demo))];
        }
    }
}
