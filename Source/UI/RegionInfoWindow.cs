using System.Collections.Generic;
using MapModeFramework;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using RegionsAndSocieties.Demographics;

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
        private static int cascade;

        private enum Tab { Region, Demographics, Economy }
        private Tab currentTab = Tab.Region;
        private readonly Vector2[] scrolls = new Vector2[3];
        private readonly float[] contentHeights = { 700f, 700f, 700f };   // self-correcting per tab

        public override Vector2 InitialSize => new Vector2(480f, 600f);
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
            Widgets.Label(new Rect(0f, 0f, inRect.width, 24f), $"Region {province.id}:  {province.name}");

            // Tab row sits below the header; the body box hangs under it.
            const float headerH = 30f;
            const float tabH = 32f;
            Rect body = new Rect(0f, headerH + tabH, inRect.width, inRect.height - headerH - tabH);

            var tabs = new List<TabRecord>
            {
                new TabRecord("Region", () => currentTab = Tab.Region, currentTab == Tab.Region),
                new TabRecord("Population", () => currentTab = Tab.Demographics, currentTab == Tab.Demographics),
                new TabRecord("Economy", () => currentTab = Tab.Economy, currentTab == Tab.Economy),
            };
            TabDrawer.DrawTabs(body, tabs, 200f);

            Rect content = body.ContractedBy(8f);
            switch (currentTab)
            {
                case Tab.Demographics: DrawDemographicsTab(content); break;
                case Tab.Economy: DrawEconomyTab(content); break;
                default: DrawRegionTab(content); break;
            }
        }

        // --- tabs ---------------------------------------------------------------

        private void DrawRegionTab(Rect content)
        {
            int idx = (int)Tab.Region;
            float viewW = content.width - 16f;
            var view = new Rect(0f, 0f, viewW, contentHeights[idx]);
            Widgets.BeginScrollView(content, ref scrolls[idx], view);

            var data = province.ownershipData ?? RegionalOwnershipUtility.CalculateOwnership(province);
            Faction owner = data != null ? data.PrimaryOwner : null;
            Color fill = owner != null ? owner.Color : new Color(0.34f, 0.5f, 0.32f);   // land green if unclaimed

            // Region shape (left) beside the ownership-influence pie (right).
            const float mapSize = 150f;
            RegionOutlineDrawer.Draw(new Rect(0f, 0f, mapSize, mapSize), province, fill);

            var slices = RegionalPieChartWindow.BuildPieSlices(data);
            float rightX = mapSize + 14f;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(rightX, 0f, viewW - rightX, 18f), "Ownership claims");
            Text.Font = GameFont.Small;
            var pieRect = new Rect(rightX, 20f, 84f, 84f);
            if (slices.Count > 0)
                PieChartDrawer.DrawPieChart(pieRect, slices, $"RegionInfo_{province.id}_{slices.Count}_{data.unclaimedScore:F2}");
            RegionalPieChartWindow.DrawLegend(new Rect(pieRect.xMax + 8f, 20f, viewW - pieRect.xMax - 8f, mapSize - 20f), slices);

            float y = mapSize + 10f;
            y = TextBlock(viewW, y, RegionPanelText.IdentityAndFeatures(province));
            y = TextBlock(viewW, y, RegionPanelText.NaturalResources(province));
            y = TextBlock(viewW, y, RegionPanelText.BiomeProperties(province));
            y = TextBlock(viewW, y, RegionPanelText.Wildlife(province));

            Widgets.EndScrollView();
            contentHeights[idx] = y;
        }

        private void DrawDemographicsTab(Rect content)
        {
            int idx = (int)Tab.Demographics;
            float viewW = content.width - 16f;
            var view = new Rect(0f, 0f, viewW, contentHeights[idx]);
            Widgets.BeginScrollView(content, ref scrolls[idx], view);

            RegionDemographics demo = RegionDemographicsUtility.ForRegion(province);
            float y = DemographicsPanel.Draw(new Rect(0f, 0f, viewW, 0f), demo, province.currentPopulation, $"RegionInfo_{province.id}");

            Widgets.EndScrollView();
            contentHeights[idx] = y;
        }

        private void DrawEconomyTab(Rect content)
        {
            int idx = (int)Tab.Economy;
            float viewW = content.width - 16f;
            var view = new Rect(0f, 0f, viewW, contentHeights[idx]);
            Widgets.BeginScrollView(content, ref scrolls[idx], view);

            float y = TextBlock(viewW, 0f, RegionPanelText.Economy(province));

            Widgets.EndScrollView();
            contentHeights[idx] = y;
        }

        /// <summary>Draw a wrapped text block at the cursor and return the advanced y (with a gap), or the
        /// same y when the block is empty.</summary>
        private static float TextBlock(float viewW, float y, string text)
        {
            if (string.IsNullOrEmpty(text)) return y;
            float h = Text.CalcHeight(text, viewW);
            Widgets.Label(new Rect(0f, y, viewW, h), text);
            return y + h + 10f;
        }
    }
}
