using System.Collections.Generic;
using MapModeFramework;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties.UI
{
    /// <summary>
    /// Draggable expanded region readout — the influence pie plus the full text — opened by
    /// DOUBLE-CLICKING a region (#53). Each window is pinned to the region it was opened for and stays
    /// open until closed with its X, so several can be open at once to compare regions for territory
    /// acquisition. New windows cascade so they don't stack exactly on top of each other.
    /// </summary>
    public class RegionInfoWindow : Window
    {
        private static readonly List<RegionInfoWindow> Open = new List<RegionInfoWindow>();

        private readonly GeographicProvince province;
        private readonly int tile;
        private Vector2 scroll;
        private static int cascade;

        public override Vector2 InitialSize => new Vector2(460f, 560f);
        protected override float Margin => 14f;

        public RegionInfoWindow(GeographicProvince province, int tile)
        {
            this.province = province;
            this.tile = tile;
            draggable = true;
            doCloseX = true;                 // each window closes independently
            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = false;
            preventCameraMotion = false;
            drawShadow = true;
            absorbInputAroundWindow = false; // clicks on the map still work (select / modifier-click others)
            onlyOneOfTypeAllowed = false;    // allow several open at once to compare regions
        }

        public static void OpenFor(GeographicProvince province, int tile)
        {
            if (province == null) return;

            Open.RemoveAll(w => w == null || !Find.WindowStack.IsOpen(w));

            // One panel per region: if this region already has a panel open, keep that one rather than
            // stacking a duplicate.
            foreach (var w in Open)
            {
                if (w.province != null && w.province.id == province.id) return;
            }

            int cap = Mathf.Max(1, FactionPlacementSettings.maxRegionPanels);
            while (Open.Count >= cap)
            {
                RegionInfoWindow oldest = Open[0];
                Open.RemoveAt(0);
                oldest.Close(false);   // FIFO: the oldest panel makes room for the new one
            }

            var window = new RegionInfoWindow(province, tile);
            Open.Add(window);
            Find.WindowStack.Add(window);
        }

        public override void PostClose()
        {
            base.PostClose();
            Open.Remove(this);
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            // Only exist while the Territories (region) map mode is actually being viewed — don't linger
            // and render over the colony map or other map modes (#53).
            var mode = MapModeComponent.Instance?.currentMapMode as MapMode_Region;
            bool inRegionView = WorldRendererUtility.WorldRendered
                && mode != null && RegionPropertiesAccess.OverrideSelector(mode.def);
            if (!inRegionView)
            {
                Close(false);
            }
        }

        protected override void SetInitialSizeAndPosition()
        {
            base.SetInitialSizeAndPosition();
            float step = (cascade % 6) * 32f;
            cascade++;
            windowRect.x = 40f + step;
            windowRect.y = 60f + step;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (province == null)
            {
                Close(false);
                return;
            }

            Text.Font = GameFont.Small;
            var data = province.ownershipData ?? RegionalOwnershipUtility.CalculateOwnership(province);

            Widgets.Label(new Rect(0f, 0f, inRect.width, 24f), $"Region {province.id}:  {province.name}");

            var slices = RegionalPieChartWindow.BuildPieSlices(data);
            Rect pieRect = new Rect(0f, 28f, 100f, 100f);
            if (slices.Count > 0)
            {
                PieChartDrawer.DrawPieChart(pieRect, slices,
                    $"RegionInfo_{province.id}_{data.factionScores.Count}_{data.unclaimedScore:F2}");
            }
            RegionalPieChartWindow.DrawLegend(new Rect(pieRect.xMax + 10f, 28f, inRect.width - pieRect.xMax - 10f, 100f), slices);

            const float textTop = 138f;
            string text = MapMode_GeographicProvinces.GetProvinceTooltip(province, tile);
            float viewW = inRect.width - 16f;
            float viewH = Text.CalcHeight(text, viewW);
            Rect outRect = new Rect(0f, textTop, inRect.width, inRect.height - textTop);
            Rect viewRect = new Rect(0f, 0f, viewW, viewH);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);
            Widgets.Label(viewRect, text);
            Widgets.EndScrollView();
        }
    }
}
