using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties
{
    public class Dialog_FactionPlacementSettings : Window
    {
        private Vector2 scrollPosition = Vector2.zero;
        private Vector2 tableScroll = Vector2.zero;
        private List<FactionDef> activeFactions;

        // Per-cell edit buffers for the experimental table view, keyed "defName:column".
        private readonly Dictionary<string, string> tableBuffers = new Dictionary<string, string>();

        // 0.7 added the world-object integration panel (height); the #47 follow-up added a right-hand pie
        // strip showing each faction's share of the world (width).
        public override Vector2 InitialSize => new Vector2(1080f, 780f);

        /// <summary>Width of the right-hand strip that holds the mode toggles, the share pie and its legend.</summary>
        private const float PieStripW = 220f;

        // The "pressed" fill for the currently-selected size category in the basic view.
        private static readonly Color SelectedFill = new Color(0.24f, 0.42f, 0.30f);

        /// <summary>Compact button label for a size category (the prose form lives in PlacementShareRules).</summary>
        private static string CategoryButtonLabel(Placement.ShareCategory cat)
        {
            switch (cat)
            {
                case Placement.ShareCategory.Tiny: return "Tiny";
                case Placement.ShareCategory.Small: return "Small";
                case Placement.ShareCategory.Medium: return "Med";
                case Placement.ShareCategory.Large: return "Large";
                default: return "V.Large";
            }
        }

        public Dialog_FactionPlacementSettings()
        {
            this.closeOnClickedOutside = true;
            this.absorbInputAroundWindow = true;

            activeFactions = DefDatabase<FactionDef>.AllDefs
                .Where(f => !f.isPlayer && !f.hidden)
                .OrderBy(f => f.defName)
                .ToList();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            // Title kept clear of the top-right Layout/View buttons (it used to run under them).
            Widgets.Label(new Rect(0f, 2f, inRect.width - 320f, 34f), "Geographic Placement Settings");
            Text.Font = GameFont.Small;

            // Retrieve current planet coverage from Page_CreateWorldParams if open
            float coverage = 0.3f;
            var page = Find.WindowStack.WindowOfType<Page_CreateWorldParams>();
            if (page != null)
            {
                var field = typeof(Page_CreateWorldParams).GetField("planetCoverage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (field != null)
                {
                    coverage = (float)field.GetValue(page);
                }
            }

            int target = FactionPlacementSettings.targetRegionSize;

            // Land tiles: count the real grid when a world exists (exact); otherwise estimate from planet
            // size × a typical land fraction (pre-gen — shown to the player as an assumption).
            int landTiles;
            bool landFromGrid = false;
            float estLandFraction = Placement.PlacementEstimates.TypicalLandFraction;   // land fraction the pre-gen estimate used (for the display line)
            int rp2SeaLevel = -1;
            // Find.WorldGrid dereferences Find.World internally, so it THROWS (not returns null) when no
            // world exists yet — which is exactly this dialog's common case, opened pre-generation from the
            // world-creation screen. Guard on Find.World first, then fall through to the pre-gen estimate.
            if (Find.World != null && Find.WorldGrid != null && Find.WorldGrid.TilesCount > 0)
            {
                int lt = 0, tc = Find.WorldGrid.TilesCount;
                for (int i = 0; i < tc; i++) if (!Find.WorldGrid[i].WaterCovered) lt++;
                landTiles = lt;
                landFromGrid = true;
            }
            else
            {
                // Under Realistic Planets 2, sea level shifts the land fraction and Planet Scale (subcount)
                // sets the tile count, which the vanilla coverage curve cannot see; read them reflectively
                // (#54 RP2 tail). Without RP2 this stays the vanilla curve + flat land fraction.
                rp2SeaLevel = Integration.RealisticPlanetsProbe.TryGetSeaLevelOrdinal();
                int rp2Subcount = Integration.RealisticPlanetsProbe.TryGetSubcount();
                bool rp2 = Integration.RealisticPlanetsProbe.IsActive && (rp2SeaLevel >= 0 || rp2Subcount > 0);
                int totalTiles = rp2
                    ? Placement.PlacementEstimates.EstimateTotalTilesRP2(rp2Subcount, coverage)
                    : Placement.PlacementEstimates.EstimateTotalTiles(coverage);
                if (rp2 && rp2SeaLevel >= 0)
                    estLandFraction = Placement.PlacementEstimates.LandFractionForSeaLevel(rp2SeaLevel);
                landTiles = Placement.PlacementEstimates.EstimateLandTiles(totalTiles, estLandFraction);
            }

            // If the world's regions are already generated, report the ACTUAL count; else the estimate band.
            int actualRegions = -1;
            var regionMgr = Find.World != null ? Find.World.GetComponent<SynapseRegionManager>() : null;
            if (regionMgr != null && regionMgr.Provinces != null)
            {
                int c = 0;
                foreach (var pr in regionMgr.Provinces)
                    if (pr.provinceType == ProvinceType.Land && pr.tiles != null && pr.tiles.Count > 0) c++;
                if (c > 0) actualRegions = c;
            }
            int estMid = Placement.PlacementEstimates.ExpectedRegionCount(landTiles, target);

            // #47: the region basis is the actual count once a world exists, else the mid estimate. The value
            // MODE + BASIS then decide how each faction's stored number becomes a region count. Build the
            // aligned weight list once and run the SAME pure distribution the pie chart and worldgen use, so
            // the estimate, the pie and the generated world all agree.
            int regionBasis = actualRegions > 0 ? actualRegions : Placement.PlacementEstimates.ExpectedRegionCount(landTiles, target);
            // One combined relative-size model (the percent/count switch was dropped): shares self-scale to
            // the land, normalised, with a guaranteed minimum of one region per faction.
            const Placement.PlacementValueMode valueMode = Placement.PlacementValueMode.Percent;
            const Placement.PlacementPercentBasis percentBasis = Placement.PlacementPercentBasis.SettledNormalized;
            float claimedFraction = FactionPlacementSettings.claimedLandAreaPercent;
            var shareWeights = new List<float>(activeFactions.Count);
            foreach (var d in activeFactions) shareWeights.Add(FactionPlacementSettings.GetProfile(d).placementShare);
            int[] dist = Placement.PlacementShareRules.DistributeRegions(valueMode, percentBasis, shareWeights, regionBasis, claimedFraction);
            int demand = Placement.PlacementShareRules.DemandRegions(valueMode, percentBasis, shareWeights, regionBasis, claimedFraction);

            // Total resulting factions = each active faction's base 1, plus its kin offshoots.
            int totalFactions = 0, totalOffshoots = 0;
            for (int i = 0; i < activeFactions.Count; i++)
            {
                var pf = FactionPlacementSettings.GetProfile(activeFactions[i]);
                int rc = i < dist.Length ? dist[i] : 0;
                int kc = FactionPlacementSettings.EffectiveEnableKin(pf, activeFactions[i])
                    ? Placement.SubFactionRules.PlannedKinCount(rc, FactionPlacementSettings.EffectiveClusterCount(pf, activeFactions[i]), pf.clusterSize)
                    : 1;
                totalFactions += kc;
                totalOffshoots += kc - 1;
            }
            int placedTotal = 0; foreach (int v in dist) placedTotal += v;
            int wilderness = Placement.PlacementShareRules.WildernessRegions(regionBasis, dist);

            // Left content is narrowed to leave a fixed pie strip on the right.
            float contentW = inRect.width - PieStripW;

            // Global Map Region Parameters Panel
            Rect globalBoxRect = new Rect(0f, 40f, contentW - 15f, 186f);
            Widgets.DrawMenuSection(globalBoxRect);

            Rect globalTitleRect = new Rect(10f, 44f, 300f, 22f);
            Widgets.Label(globalTitleRect, "<b>Global Map Region Parameters</b>");

            // #47: basic/advanced view toggle — moved ABOVE the global box (the title row's right side) so it
            // no longer sits on top of the box's own note.
            bool advanced = FactionPlacementSettings.placementUiAdvanced;
            Rect toggleRect = new Rect(inRect.width - 165f, 4f, 155f, 30f);
            if (Widgets.ButtonText(toggleRect, advanced ? "View: Advanced" : "View: Basic"))
            {
                FactionPlacementSettings.placementUiAdvanced = !advanced;
                advanced = FactionPlacementSettings.placementUiAdvanced;
            }
            TooltipHandler.TipRegion(toggleRect,
                "Basic: one row per faction — just the size of the map each faction gets.\n\n" +
                "Advanced: the full per-faction controls (resource weights, clustering) plus mod-integration governance.");

            // Left Column: one Target size knob (the merge floor derives as half of it). The full explanation
            // is a tooltip rather than an inline note, which used to overflow onto the row below.
            float colWidth = (globalBoxRect.width - 30f) / 2f;
            Rect targetLabelRect = new Rect(10f, 68f, 135f, 22f);
            Widgets.Label(targetLabelRect, $"Target size: {FactionPlacementSettings.targetRegionSize}");
            Rect targetSliderRect = new Rect(150f, 70f, colWidth - 155f, 18f);
            float tempTarget = Widgets.HorizontalSlider(targetSliderRect, FactionPlacementSettings.targetRegionSize, 50f, 400f, false, null, null, null, 1f);
            FactionPlacementSettings.targetRegionSize = Mathf.RoundToInt(tempTarget);

            // Right Column: a short one-line note; the detail lives in the target-size tooltip.
            float rightColStart = 10f + colWidth + 10f;
            Rect noteRect = new Rect(rightColStart, 68f, globalBoxRect.width - rightColStart - 10f, 22f);
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(noteRect, "tiles/region — sparse biomes auto-scale up");
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            TooltipHandler.TipRegion(new Rect(10f, 68f, colWidth - 10f, 22f),
                "The size the subdivision aims for, in tiles per region. Sparse biomes (desert, tundra, ice) scale up automatically to fewer, larger regions; the merge floor (regions smaller than half the target are merged away) derives from this.");

            // Second Row: claimed land area (density) — the single fill knob. The Max Threat cap was dropped;
            // hostility is now shown per faction (the coloured dot on each row) so the player caps threats by
            // sizing them directly.
            Rect occupLabelRect = new Rect(10f, 98f, 235f, 22f);
            Widgets.Label(occupLabelRect, $"Claimed land area: {Mathf.RoundToInt(FactionPlacementSettings.claimedLandAreaPercent * 100f)}%");
            TooltipHandler.TipRegion(occupLabelRect,
                "The share of livable land the factions collectively claim — the single fill knob. Raise it for a busier world, lower it for a frontier. The rest of the map is wilderness.");
            Rect occupSliderRect = new Rect(250f, 100f, colWidth - 255f, 18f);
            float tempOccup = Widgets.HorizontalSlider(occupSliderRect, FactionPlacementSettings.claimedLandAreaPercent, 0.10f, 0.90f, false, null, null, null, 0.01f);
            FactionPlacementSettings.claimedLandAreaPercent = tempOccup;

            // Estimates row
            Rect estRect = new Rect(10f, 135f, globalBoxRect.width - 20f, 22f);
            string[] seaLevelNames = { "low", "slightly low", "normal", "slightly high", "high" };
            string seaNote = rp2SeaLevel >= 0 && rp2SeaLevel < seaLevelNames.Length ? $", {seaLevelNames[rp2SeaLevel]} sea level" : "";
            string landPart = landFromGrid
                ? $"Land tiles: <color=cyan>{landTiles}</color>"
                : $"Est. land tiles: <color=cyan>{landTiles}</color> (~{Mathf.RoundToInt(estLandFraction * 100f)}% of a {Mathf.RoundToInt(coverage * 100f)}%-coverage planet{seaNote})";
            string countPart = actualRegions > 0
                ? $"Regions: <color=green>{actualRegions}</color> (this world)"
                : $"Expected regions: <color=green>~{estMid}</color> <color=grey>(rough — varies with sea level)</color>";
            Widgets.Label(estRect, landPart + "  |  " + countPart);

            // Total-factions readout: base factions + kin offshoots, so the player sees how the kin settings
            // inflate the world's faction count against the 40-faction cap.
            Rect kinHintRect = new Rect(10f, 160f, globalBoxRect.width - 20f, 22f);
            GUI.color = totalFactions > 40 ? new Color(1f, 0.5f, 0.5f) : new Color(0.75f, 0.85f, 0.75f);
            Widgets.Label(kinHintRect, $"Total factions: <b>{totalFactions}</b>  ({activeFactions.Count} base + {totalOffshoots} kin offshoots){(totalFactions > 40 ? "  — over the 40-faction cap; some kin will be truncated" : "")}");
            GUI.color = Color.white;
            TooltipHandler.TipRegion(kinHintRect,
                "Every faction contributes 1, plus its regional-kin offshoots (set per faction in Advanced). Worldgen caps the world at 40 factions, so beyond that the last factions' kin are truncated.");

            // Regions-claimed capacity readout, right below Total factions (green / yellow at 80% crowding /
            // red over the planet's regions). Lives in the top card now so it reads with the other totals.
            bool over = Placement.PlacementShareRules.IsOverCapacity(demand, regionBasis);
            bool warn = Placement.PlacementShareRules.IsCrowdingWarning(demand, regionBasis);
            Rect capRect = new Rect(10f, 184f, globalBoxRect.width - 20f, 22f);
            GUI.color = over ? new Color(1f, 0.4f, 0.4f) : (warn ? new Color(1f, 0.85f, 0.3f) : new Color(0.6f, 0.85f, 0.6f));
            Widgets.Label(capRect, over
                ? $"Regions claimed: {demand} of ~{regionBasis} — OVER CAPACITY, placement scaled down to fit ({wilderness} wilderness)."
                : warn
                    ? $"Regions claimed: {demand} of ~{regionBasis} ({Mathf.RoundToInt(Placement.PlacementShareRules.CapacityFraction(demand, regionBasis) * 100f)}%) — crowded, tight borders ({wilderness} wilderness)."
                    : $"Regions claimed: {demand} of ~{regionBasis} the planet supports  ·  {wilderness} left wilderness.");
            GUI.color = Color.white;

            // The faction editors lay out inside the narrowed content area; the pie strip owns the rest.
            Rect contentRect = new Rect(0f, 0f, contentW, inRect.height);
            if (advanced)
            {
                // Layout toggle (cards ↔ table) sits on the title row, left of the view toggle.
                Rect layoutRect = new Rect(inRect.width - 165f - 130f, 4f, 125f, 30f);
                if (Widgets.ButtonText(layoutRect, FactionPlacementSettings.placementUiTable ? "Layout: Table" : "Layout: Cards"))
                    FactionPlacementSettings.placementUiTable = !FactionPlacementSettings.placementUiTable;
                TooltipHandler.TipRegion(layoutRect,
                    "Cards: one tall card per faction.\nTable: every faction and setting in one grid — denser, better for comparing factions while tuning.");

                // The World Object Integration panel now scrolls WITH the faction editor instead of taking a
                // fixed slab of the window, so the whole area below the global box is usable for tuning (#47).
                if (FactionPlacementSettings.placementUiTable)
                    DrawAdvancedTable(contentRect, 232f);
                else
                    DrawAdvancedCards(contentRect, 232f);
            }
            else
            {
                DrawBasicRows(contentRect, 232f, dist, demand, regionBasis);
            }

            // Right strip: value-mode + basis toggles, then the share pie and its legend.
            DrawPieStrip(new Rect(contentW, 40f, PieStripW - 10f, inRect.height - 40f - 50f), dist, wilderness, regionBasis);

            Rect closeButtonRect = new Rect(contentW / 2f - 75f, inRect.height - 45f, 150f, 35f);
            if (Widgets.ButtonText(closeButtonRect, "Close"))
            {
                this.Close();
            }
        }

        /// <summary>
        /// #47 basic view: one compact row per faction — its share of the placed territories and the live
        /// "≈ N regions" that share works out to. Nothing else; the point of basic mode is that most
        /// players only want to say how much of the map each faction gets. Fits ≤ 12 factions without
        /// scrolling.
        /// </summary>
        private void DrawBasicRows(Rect inRect, float top, int[] dist, int demand, int regionBasis)
        {
            // Preset header shows the relative multiplier (1×/2×/4×/8×/16×).
            const string presetUnit = "×";

            // Column layout, shared by the header band and every row. Labels live in the header so each row
            // is just numbers (dot → name → presets → ≈regions → kin), keeping every line short.
            const float colNameW = 205f, colBtnW = 62f;   // wide enough for "Civil outlander union" + the dot
            // Preset buttons in each row start at (dot 8 + gap 18 → name 26 .. name+width) + 6; this x0 must
            // match that exactly or the header labels drift off their columns.
            float colPickX0 = 26f + (colNameW - 18f) + 6f;
            // Kin column comes BEFORE the region column (left-to-right): checkbox, → kin count, then the
            // region total with per-kin size in parentheses.
            float kinCheckX = colPickX0 + 5 * colBtnW + 18f;   // kin checkbox
            float kinNumX = kinCheckX + 26f;                   // kin faction count (→ N)
            float regX = kinNumX + 44f;                        // ≈ regions "total (perKin kin)"
            var headerCats = new[] { Placement.ShareCategory.Tiny, Placement.ShareCategory.Small, Placement.ShareCategory.Medium, Placement.ShareCategory.Large, Placement.ShareCategory.VeryLarge };

            // Column header band at the very top of the basic area, in a tall-enough strip that it never clips.
            // (The capacity readout and the "Faction Relative Size" caption moved out — up to the top card and
            // down to a footer respectively.)
            TextAnchor hdrAnchor = Text.Anchor;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.LowerCenter;
            GUI.color = new Color(0.6f, 0.85f, 0.6f);
            for (int i = 0; i < headerCats.Length; i++)
                Widgets.Label(new Rect(colPickX0 + i * colBtnW, top + 4f, colBtnW - 3f, 18f),
                    $"{Placement.PlacementShareRules.CategoryToRegionCap(headerCats[i])}{presetUnit}");
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Text.Anchor = TextAnchor.LowerLeft;
            Widgets.Label(new Rect(kinCheckX, top + 4f, 70f, 18f), "kin → #");
            Widgets.Label(new Rect(regX, top + 4f, 120f, 18f), "≈reg (per kin)");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = hdrAnchor;

            float rowH = 34f;
            // Rows start below the header band; reserve room at the bottom for the size caption + Close button.
            float rowsTop = top + 28f;
            Rect outRect = new Rect(0f, rowsTop, inRect.width, inRect.height - rowsTop - 80f);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 25f, activeFactions.Count * rowH);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            float curY = 0f;
            for (int fi = 0; fi < activeFactions.Count; fi++)
            {
                var def = activeFactions[fi];
                var profile = FactionPlacementSettings.GetProfile(def);

                Rect rowRect = new Rect(0f, curY, viewRect.width, rowH - 4f);
                Widgets.DrawMenuSection(rowRect);

                // Hostility indicator toward the player: green friendly / yellow neutral / red hostile.
                var host = HostilityOf(def);
                Rect dotRect = new Rect(rowRect.x + 8f, rowRect.y + 9f, 12f, 12f);
                Widgets.DrawBoxSolid(dotRect, HostilityColor(host));
                TooltipHandler.TipRegion(dotRect, HostilityTooltip(host, def));

                Rect nameRect = new Rect(rowRect.x + 26f, rowRect.y + 3f, colNameW - 18f, 24f);
                Widgets.Label(nameRect, $"<b>{def.LabelCap}</b>");
                TooltipHandler.TipRegion(nameRect, $"{def.LabelCap} ({def.defName})");

                // Size preset picker — the current size drawn "pressed"; advanced view types any integer.
                var current = Placement.PlacementShareRules.CategoryForShare(profile.placementShare);
                float pickX = nameRect.xMax + 6f;
                foreach (Placement.ShareCategory cat in System.Enum.GetValues(typeof(Placement.ShareCategory)))
                {
                    Rect btn = new Rect(pickX, rowRect.y + 3f, colBtnW - 3f, 24f);
                    bool clicked;
                    if (cat == current)
                    {
                        Widgets.DrawBoxSolid(btn, SelectedFill);
                        Widgets.DrawBox(btn);
                        TextAnchor prevAnchor = Text.Anchor;
                        Text.Anchor = TextAnchor.MiddleCenter;
                        Widgets.Label(btn, CategoryButtonLabel(cat));
                        Text.Anchor = prevAnchor;
                        clicked = Widgets.ButtonInvisible(btn);
                    }
                    else
                    {
                        clicked = Widgets.ButtonText(btn, CategoryButtonLabel(cat));
                    }
                    if (clicked) profile.placementShare = Placement.PlacementShareRules.CategoryToRegionCap(cat);
                    pickX += colBtnW;
                }

                // ≈ regions this faction receives under the current mode (matches the pie); guaranteed ≥ 1.
                int est = fi < dist.Length ? dist[fi] : 0;

                // Kin checkbox + the kin-faction count (→ N) — drawn FIRST (left of the region column). The
                // Empire's kin is locked off (#63): it stays one faction (however scattered), so its box is
                // disabled.
                bool kinLocked = FactionPlacementSettings.KinLocked(def);
                bool kinOn = FactionPlacementSettings.EffectiveEnableKin(profile, def);
                if (kinLocked)
                {
                    bool locked = false;
                    Widgets.Checkbox(kinCheckX, rowRect.y + 2f, ref locked, 22f, disabled: true);
                }
                else
                {
                    bool kinBefore = kinOn;
                    Widgets.Checkbox(kinCheckX, rowRect.y + 2f, ref kinOn, 22f);
                    if (kinOn != kinBefore) profile.enableKinRaw = kinOn ? 1 : 0;
                }
                int clustersCfg = FactionPlacementSettings.EffectiveClusterCount(profile, def);
                int kinCount = kinOn ? Placement.SubFactionRules.PlannedKinCount(est, clustersCfg, profile.clusterSize) : 1;
                bool actuallySplits = kinOn && kinCount >= 2;
                GUI.color = actuallySplits ? Color.white : new Color(0.55f, 0.55f, 0.55f);
                Widgets.Label(new Rect(kinNumX, rowRect.y + 3f, 40f, 24f),
                    actuallySplits ? $"→ <color=cyan>{kinCount}</color>" : "→ 1");
                GUI.color = Color.white;
                TooltipHandler.TipRegion(new Rect(kinCheckX, rowRect.y, 66f, rowRect.height - 4f),
                    kinLocked
                        ? $"The Empire never forms kin — it stays one faction, but scatters into small clusters (cluster size {Placement.ClusteringRules.Label(profile.clusterSize)}, set in Advanced)."
                        : actuallySplits
                            ? $"Kin on: this faction's ~{est} regions split into {kinCount} kin factions (up to {clustersCfg} max — set in Advanced)."
                            : kinOn
                                ? $"Kin is on, but this faction resolves to a single cluster (≈{est} regions), so it stays ONE faction. Give it more land or raise its cluster count (Advanced)."
                                : "Kin is off — this faction stays whole (it may still scatter into clusters).");

                // Region column: total regions, and when it splits, the per-kin faction size in parentheses.
                int perKin = actuallySplits ? Mathf.Max(1, Mathf.RoundToInt((float)est / kinCount)) : est;
                Widgets.Label(new Rect(regX, rowRect.y + 3f, 110f, 24f),
                    actuallySplits ? $"<color=#A6FF9E>{est}</color> <color=grey>({perKin}/kin)</color>" : $"<color=#A6FF9E>{est}</color>");

                Rect resetRect = new Rect(rowRect.xMax - 96f, rowRect.y + 3f, 88f, 22f);
                if (Widgets.ButtonText(resetRect, "Reset"))
                    profile.placementShare = FactionPlacementSettings.DefaultShare(def);

                curY += rowH;
            }
            Widgets.EndScrollView();

            // Caption below the chart (moved down from the top per review) — what the size buttons mean.
            GUI.color = new Color(0.6f, 0.6f, 0.6f);
            Widgets.Label(new Rect(0f, outRect.yMax + 4f, inRect.width - 15f, 22f),
                "Faction Relative Size (Min 1) — each faction is guaranteed at least one region.");
            GUI.color = Color.white;
        }

        /// <summary>
        /// #47 advanced view: the full per-faction card — resource weights, placement order, clustering —
        /// with the old Settlement Range replaced by the same share row basic mode shows.
        /// </summary>
        private void DrawAdvancedCards(Rect inRect, float top)
        {
            const float integrationH = 264f;
            Rect outRect = new Rect(0f, top, inRect.width, inRect.height - top - 55f);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 25f, integrationH + 6f + activeFactions.Count * 250f);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);

            // The World Object Integration panel scrolls as the first block, so it no longer eats a fixed
            // slab of the window.
            DrawIntegrationPanel(new Rect(0f, 0f, viewRect.width, integrationH));
            float curY = integrationH + 6f;

            foreach (var def in activeFactions)
            {
                var profile = FactionPlacementSettings.GetProfile(def);

                Rect boxRect = new Rect(0f, curY, viewRect.width, 240f);
                Widgets.DrawMenuSection(boxRect);

                Rect titleRect = new Rect(10f, curY + 10f, boxRect.width - 20f, 25f);
                Widgets.Label(titleRect, $"<b>{def.LabelCap} ({def.defName}) - Tech: {def.techLevel}</b>");

                Rect resetRect = new Rect(boxRect.width - 120f, curY + 8f, 100f, 22f);
                if (Widgets.ButtonText(resetRect, "Reset Default"))
                {
                    var defaultProfile = FactionPlacementSettings.GetDefaultProfile(def);
                    profile.mineralWeight = defaultProfile.mineralWeight;
                    profile.nutritionWeight = defaultProfile.nutritionWeight;
                    profile.forageWeight = defaultProfile.forageWeight;
                    profile.grazingWeight = defaultProfile.grazingWeight;
                    profile.huntingWeight = defaultProfile.huntingWeight;
                    profile.marginWeight = defaultProfile.marginWeight;
                    profile.placementShare = defaultProfile.placementShare;
                    profile.placementOrder = defaultProfile.placementOrder;
                    profile.clusterSize = defaultProfile.clusterSize;
                }

                // Left column sliders
                float leftY = curY + 40f;
                DrawWeightSlider(ref leftY, boxRect.width / 2f - 15f, 10f, "Mineral (Mountains/Hills)", ref profile.mineralWeight, 0f, 5f);
                DrawWeightSlider(ref leftY, boxRect.width / 2f - 15f, 10f, "Nutrition (Agricultural Plains)", ref profile.nutritionWeight, 0f, 5f);
                DrawWeightSlider(ref leftY, boxRect.width / 2f - 15f, 10f, "Forage (Neolithic Foraging)", ref profile.forageWeight, 0f, 5f);

                // Right column sliders
                float rightY = curY + 40f;
                DrawWeightSlider(ref rightY, boxRect.width / 2f - 15f, boxRect.width / 2f + 5f, "Grazing (Open Grasslands)", ref profile.grazingWeight, 0f, 5f);
                DrawWeightSlider(ref rightY, boxRect.width / 2f - 15f, boxRect.width / 2f + 5f, "Hunting (Forests/Wilds)", ref profile.huntingWeight, 0f, 5f);
                DrawWeightSlider(ref rightY, boxRect.width / 2f - 15f, boxRect.width / 2f + 5f, "Margin (Desert/Tundra Edges)", ref profile.marginWeight, 0f, 5f);

                // The four kin/size knobs in a 2×2 grid so the card stays short. Left column x=10, right column
                // starts at mid; each cell is a label with a 56-px numeric field at its right edge.
                float halfW = boxRect.width / 2f - 15f;
                float leftX = 10f, rightX = boxRect.width / 2f + 5f;
                float rowAy = curY + 180f, rowBy = curY + 210f;
                bool kinLocked = FactionPlacementSettings.KinLocked(def);   // #63: the Empire

                // Row A left — Relative size (weight).
                Rect shareLabelRect = new Rect(leftX, rowAy, halfW - 62f, 24f);
                Widgets.Label(shareLabelRect, "Relative size (weight):");
                TooltipHandler.TipRegion(shareLabelRect,
                    "This faction's relative weight in the split of the land. Weights need not sum to anything — they normalise across whoever is present, then fill the claimed area. Every faction is guaranteed at least one region. The basic presets (1×/2×/4×/8×/16×) fill this in.");
                int rc = Mathf.RoundToInt(profile.placementShare);
                string rkey = def.defName + ":adv_reg";
                if (!tableBuffers.TryGetValue(rkey, out var rbuf)) rbuf = rc.ToString();
                Widgets.TextFieldNumeric(new Rect(leftX + halfW - 56f, rowAy, 52f, 24f), ref rc, ref rbuf, 0f, 300f);
                tableBuffers[rkey] = rbuf;
                profile.placementShare = rc;

                // Row A right — Regional kin toggle. The Empire is locked off (#63): it stays one polity,
                // however fragmented, and many mods base off it — but it still scatters (cluster size below).
                Rect kinRect = new Rect(rightX, rowAy, halfW, 24f);
                bool kinOn = FactionPlacementSettings.EffectiveEnableKin(profile, def);
                if (kinLocked)
                {
                    bool locked = false;
                    Widgets.CheckboxLabeled(kinRect, "Split into regional kin", ref locked, disabled: true, placeCheckboxNearText: true);
                    TooltipHandler.TipRegion(kinRect,
                        "The Empire is a single, highly-fragmented polity — it never splits into regional kin. It still scatters into small clusters all over the map; set how small with the cluster size on the right.");
                }
                else
                {
                    bool kinBefore = kinOn;
                    Widgets.CheckboxLabeled(kinRect, "Split into regional kin", ref kinOn, placeCheckboxNearText: true);
                    if (kinOn != kinBefore) profile.enableKinRaw = kinOn ? 1 : 0;
                    TooltipHandler.TipRegion(kinRect,
                        "When on, this faction's territory is divided into geographically separate kin factions (loosely-related, not merged). " +
                        "How many is capped by 'Max kin factions' below. Default on for scattered low-tech factions (pirates, tribes, rough unions), off for the Empire and spacer-tech factions.");
                }

                // Row B left — Max kin factions (the kin cap; field is numberOfClusters). Only meaningful
                // when kin is ON; greyed and inert otherwise (the Empire, and any faction with kin off),
                // which is where the scatter is set by cluster size instead. NOTE: this caps KIN FACTIONS,
                // not the physical clusters on the map (those are ~regions/clusterSize) — the old "Number of
                // clusters" label conflated the two and misled players (0.4.1 rename).
                Rect clustersLabelRect = new Rect(leftX, rowBy, halfW - 62f, 24f);
                bool zeroClusters = kinOn && FactionPlacementSettings.EffectiveClusterCount(profile, def) == 0;
                GUI.color = !kinOn ? new Color(0.6f, 0.6f, 0.6f) : (zeroClusters ? new Color(1f, 0.7f, 0.3f) : Color.white);
                Widgets.Label(clustersLabelRect, "Max kin factions:");
                GUI.color = Color.white;
                TooltipHandler.TipRegion(clustersLabelRect,
                    "The most kin factions this faction splits into — only applies when 'Split into regional kin' is on. This does NOT set the number of clusters on the map (that comes from the cluster size); it just caps how many separate factions those clusters are grouped into. 0 = no cap. Defaults: pirates 5, tribes 3, rough unions 2.");
                if (kinOn)
                {
                    int nc = FactionPlacementSettings.EffectiveClusterCount(profile, def);
                    string nkey = def.defName + ":adv_nc";
                    if (!tableBuffers.TryGetValue(nkey, out var nbuf)) nbuf = nc.ToString();
                    Widgets.TextFieldNumeric(new Rect(leftX + halfW - 56f, rowBy, 52f, 24f), ref nc, ref nbuf, 0f, 40f);
                    tableBuffers[nkey] = nbuf;
                    profile.numberOfClusters = nc;
                }
                else
                {
                    tableBuffers.Remove(def.defName + ":adv_nc");
                }

                // Row B right — Cluster size: the largest a single contiguous cluster (body) grows. Applies
                // ALWAYS (kin on or off), so it is the knob that scatters the Empire. 0 = one nation.
                Rect minLabelRect = new Rect(rightX, rowBy, halfW - 62f, 24f);
                Widgets.Label(minLabelRect, "Cluster size:");
                TooltipHandler.TipRegion(minLabelRect,
                    "The largest a single contiguous cluster (body) grows — the faction scatters into bodies of at most this many regions, whether or not it forms kin. 0 = one nation (no scatter). Defaults: pirates & Empire 3, tribes 5, rough unions 7, cohesive factions none.");
                int cl = Placement.ClusteringRules.Snap(profile.clusterSize);
                string ckey = def.defName + ":adv_cl";
                if (!tableBuffers.TryGetValue(ckey, out var cbuf)) cbuf = cl.ToString();
                Widgets.TextFieldNumeric(new Rect(rightX + halfW - 56f, rowBy, 52f, 24f), ref cl, ref cbuf, 0f, 99f);
                tableBuffers[ckey] = cbuf;
                profile.clusterSize = cl;

                curY += 250f;
            }

            Widgets.EndScrollView();
        }

        // Column layout for the table: x positions and widths, index-aligned with the headers. Spaced out with
        // fuller names, and a "Max kin" column (kin-faction cap) beside "Cluster sz" (body size) (indices:
        // 0 Faction, 1-6 resource weights, 7 Size, 8 Max kin, 9 Cluster size, 10 Kin, 11 Reset).
        private static readonly float[] TblX = { 4f, 160f, 214f, 268f, 322f, 376f, 430f, 486f, 540f, 596f, 652f, 686f };
        private static readonly float[] TblW = { 150f, 50f, 50f, 50f, 50f, 50f, 50f, 50f, 50f, 50f, 30f, 56f };
        private static readonly string[] TblHead = { "Faction", "Mineral", "Nutrition", "Forage", "Grazing", "Hunting", "Margin", "Size", "Max kin", "Cluster sz", "Kin", "" };

        /// <summary>Experimental table layout for the advanced faction editor (#47): every faction a row,
        /// every tuning value a column, so a custom setup can be compared across factions at a glance.</summary>
        private void DrawAdvancedTable(Rect inRect, float top)
        {
            // Fixed column-header row above the scroll.
            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            Text.Font = GameFont.Tiny;
            TextAnchor prev = Text.Anchor;
            Text.Anchor = TextAnchor.LowerCenter;
            for (int i = 1; i < TblHead.Length; i++)
                Widgets.Label(new Rect(TblX[i], top, TblW[i], 22f), TblHead[i]);
            Text.Anchor = TextAnchor.LowerLeft;
            Widgets.Label(new Rect(TblX[0], top, TblW[0], 22f), TblHead[0]);
            Text.Anchor = prev;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            const float integrationH = 264f, rowH = 28f;
            Rect outRect = new Rect(0f, top + 24f, inRect.width, inRect.height - (top + 24f) - 55f);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 25f, activeFactions.Count * rowH + 16f + integrationH);
            Widgets.BeginScrollView(outRect, ref tableScroll, viewRect);

            // Faction rows first, right under the fixed column header; the integration panel follows below.
            float curY = 0f;
            for (int fi = 0; fi < activeFactions.Count; fi++)
            {
                var def = activeFactions[fi];
                var profile = FactionPlacementSettings.GetProfile(def);
                Rect row = new Rect(0f, curY, viewRect.width, rowH - 2f);
                if (fi % 2 == 0) Widgets.DrawLightHighlight(row);

                Widgets.Label(new Rect(TblX[0] + 2f, curY + 4f, TblW[0], 22f), def.LabelCap);
                TooltipHandler.TipRegion(new Rect(TblX[0], curY, TblW[0], rowH), $"{def.LabelCap} ({def.defName})");

                float cy = curY + 3f, ch = rowH - 6f;
                NumCell(new Rect(TblX[1], cy, TblW[1], ch), def.defName + ":min", ref profile.mineralWeight, 0f, 5f);
                NumCell(new Rect(TblX[2], cy, TblW[2], ch), def.defName + ":nut", ref profile.nutritionWeight, 0f, 5f);
                NumCell(new Rect(TblX[3], cy, TblW[3], ch), def.defName + ":for", ref profile.forageWeight, 0f, 5f);
                NumCell(new Rect(TblX[4], cy, TblW[4], ch), def.defName + ":grz", ref profile.grazingWeight, 0f, 5f);
                NumCell(new Rect(TblX[5], cy, TblW[5], ch), def.defName + ":hun", ref profile.huntingWeight, 0f, 5f);
                NumCell(new Rect(TblX[6], cy, TblW[6], ch), def.defName + ":mrg", ref profile.marginWeight, 0f, 5f);
                NumCell(new Rect(TblX[7], cy, TblW[7], ch), def.defName + ":shr", ref profile.placementShare, 0f, 300f);

                bool kinLocked = FactionPlacementSettings.KinLocked(def);   // #63: the Empire
                bool kinOn = FactionPlacementSettings.EffectiveEnableKin(profile, def);

                // Max kin factions (kin cap; numberOfClusters): editable only when kin is on; greyed and
                // inert otherwise (the Empire and any kin-off faction, whose scatter comes from cluster size).
                if (kinOn)
                {
                    int ncv = FactionPlacementSettings.EffectiveClusterCount(profile, def);
                    string nkey = def.defName + ":nc";
                    if (!tableBuffers.TryGetValue(nkey, out var nbuf)) nbuf = ncv.ToString();
                    Widgets.TextFieldNumeric(new Rect(TblX[8], cy, TblW[8], ch), ref ncv, ref nbuf, 0f, 40f);
                    tableBuffers[nkey] = nbuf;
                    profile.numberOfClusters = ncv;
                }
                else
                {
                    tableBuffers.Remove(def.defName + ":nc");
                    var pa = Text.Anchor; Text.Anchor = TextAnchor.MiddleCenter;
                    GUI.color = new Color(0.5f, 0.5f, 0.5f);
                    Widgets.Label(new Rect(TblX[8], cy, TblW[8], ch), "—");
                    GUI.color = Color.white; Text.Anchor = pa;
                }

                // Cluster size (body-size cap): applies always, so it is the Empire's scatter knob. 0 = whole.
                int clv = Placement.ClusteringRules.Snap(profile.clusterSize);
                string ckey = def.defName + ":cl";
                if (!tableBuffers.TryGetValue(ckey, out var cbuf)) cbuf = clv.ToString();
                Widgets.TextFieldNumeric(new Rect(TblX[9], cy, TblW[9], ch), ref clv, ref cbuf, 0f, 99f);
                tableBuffers[ckey] = cbuf;
                profile.clusterSize = clv;

                // Kin toggle — locked off for the Empire (#63).
                if (kinLocked)
                {
                    bool locked = false;
                    Widgets.Checkbox(TblX[10] + 6f, curY + 3f, ref locked, 20f, disabled: true);
                }
                else
                {
                    bool kb = kinOn;
                    Widgets.Checkbox(TblX[10] + 6f, curY + 3f, ref kinOn, 20f);
                    if (kinOn != kb) profile.enableKinRaw = kinOn ? 1 : 0;
                }

                if (Widgets.ButtonText(new Rect(TblX[11], curY + 2f, TblW[11], rowH - 6f), "Reset"))
                {
                    var dp = FactionPlacementSettings.GetDefaultProfile(def);
                    profile.mineralWeight = dp.mineralWeight; profile.nutritionWeight = dp.nutritionWeight;
                    profile.forageWeight = dp.forageWeight; profile.grazingWeight = dp.grazingWeight;
                    profile.huntingWeight = dp.huntingWeight; profile.marginWeight = dp.marginWeight;
                    profile.placementShare = dp.placementShare; profile.clusterSize = dp.clusterSize;
                    profile.numberOfClusters = dp.numberOfClusters; profile.enableKinRaw = -1;
                    foreach (var k in new[] { "min", "nut", "for", "grz", "hun", "mrg", "shr", "cl", "nc" }) tableBuffers.Remove(def.defName + ":" + k);
                }

                curY += rowH;
            }

            // World Object Integration below the faction table, scrolling with it.
            DrawIntegrationPanel(new Rect(0f, curY + 16f, viewRect.width, integrationH));

            Widgets.EndScrollView();
        }

        private void NumCell(Rect r, string key, ref float v, float min, float max)
        {
            if (!tableBuffers.TryGetValue(key, out var buf)) buf = v.ToString("0.##");
            Widgets.TextFieldNumeric(r, ref v, ref buf, min, max);
            tableBuffers[key] = buf;
        }

        /// <summary>
        /// 0.7: switches for the world-object governance layer. Every mod integration and every
        /// governed mechanic is optional, so a player who only wants vanilla behaviour can get it
        /// from this panel without uninstalling anything.
        /// </summary>
        private void DrawIntegrationPanel(Rect box)
        {
            Widgets.DrawMenuSection(box);

            Rect titleRect = new Rect(box.x + 10f, box.y + 4f, box.width - 20f, 22f);
            Widgets.Label(titleRect, "<b>World Objects to Add During World Generation:</b>");

            float y = box.y + 30f;

            // Strict ownership shown here as a read-only status; the control itself lives in the mod's
            // settings (Options → Mod Settings → Regions and Societies), where it can be changed at any
            // time, including mid-game. Reads the loaded world's flag when there is one, else the new-world
            // default.
            var manager = Find.World?.GetComponent<SynapseRegionManager>();
            bool strictActive = manager != null ? manager.StrictTerritorialOwnership
                                                : FactionPlacementSettings.strictTerritorialOwnershipDefault;
            string strictWord = strictActive ? "<color=#7CFC7C>enabled</color>" : "<color=#FF7C7C>disabled</color>";
            Rect ownRect = new Rect(box.x + 10f, y, box.width - 20f, 22f);
            Widgets.Label(ownRect, $"Strict territorial ownership is {strictWord} — change it in mod settings. (default off, for compatibility)");
            TooltipHandler.TipRegion(ownRect,
                "Whether Regions & Societies governs where settlements and outposts may be built (buffers, supply range, footholds, region locks). " +
                "Set it under Options → Mod Settings → Regions and Societies; it can be changed mid-game.");
            y += 26f;

            // World maturity: how many NON-PLAYER outposts and other holdings are pre-placed at world
            // generation. Off = none; Full = each territory's whole allowance (a ready-to-play, fully
            // settled world). Seeding is implied by maturity > 0 — no separate on/off switch.
            // World maturity slider — label on its own line, slider full-width below it (the Off/Full end
            // labels need the whole width or they clip against the caption).
            bool on = Integration.WorldObjectIntegrationSettings.masterEnabled;
            Rect matLabelRect = new Rect(box.x + 10f, y, box.width - 20f, 22f);
            GUI.color = on ? Color.white : new Color(1f, 1f, 1f, 0.4f);
            Widgets.Label(matLabelRect, $"NPC outposts & world objects to pre-place:  <b>{Sizing.SeedingMaturityRules.Label(Integration.WorldObjectIntegrationSettings.seedingMaturity)}</b>");
            GUI.color = Color.white;
            TooltipHandler.TipRegion(matLabelRect,
                "How built-up a new world starts — how many non-player (NPC) outposts and other holdings are pre-placed around settlements at world generation. " +
                "Off pre-places none; Full pre-places each territory's whole allowance — a ready-to-play, fully-settled world (e.g. a World Domination start). " +
                "Requires a compatibility mod that builds the outposts. Stamped per world so a regenerate reproduces it.");
            y += 30f;
            // "Off"/"Full" drawn as our own flanking labels around a plain slider, so they can't clip against
            // the card edges or the caption (the slider's built-in end labels hug the ends and overlapped).
            var prevAnchor = Text.Anchor;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(box.x + 12f, y, 34f, 18f), "Off");
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(box.xMax - 46f, y, 34f, 18f), "Full");
            Text.Anchor = prevAnchor;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Rect matSliderRect = new Rect(box.x + 52f, y + 1f, box.width - 104f, 18f);
            float tempMat = Widgets.HorizontalSlider(matSliderRect, Integration.WorldObjectIntegrationSettings.seedingMaturity, 0f, 1f, false, null, null, null, 0.05f);
            if (on) Integration.WorldObjectIntegrationSettings.seedingMaturity = tempMat;
            y += 30f;

            // Detected compatibility mods and the world objects each contributes. FRAMEWORK PREVIEW: no
            // compatibility patch populates these columns yet, so with none installed we show a dimmed
            // example of the shape a patch would fill. See Design/world-object-integration.md.
            DrawDetectedModsTable(new Rect(box.x + 10f, y, box.width - 20f, box.yMax - y - 6f));
        }

        /// <summary>The detected-mods framework table (design: Design/world-object-integration.md). Lists the
        /// active world-object integrations and, per mod, which world objects it contributes. The capability
        /// columns are a preview — no compatibility patch reports them yet — so with nothing detected we draw
        /// a dimmed worked example rather than an empty grid.</summary>
        private void DrawDetectedModsTable(Rect area)
        {
            var active = new List<string>();
            foreach (var adapter in Integration.WorldObjectAdapterRegistry.Adapters)
            {
                bool ok;
                try { ok = adapter.IsActive; }
                catch (Exception) { ok = false; }
                if (ok && adapter.AdapterId != "vanilla") active.Add(adapter.DisplayName);
            }

            Text.Font = GameFont.Tiny;
            float y = area.y;
            Widgets.Label(new Rect(area.x, y, area.width, 18f), "<b>Detected compatibility mods</b>");
            y += 18f;

            if (active.Count == 0)
            {
                GUI.color = new Color(0.7f, 0.7f, 0.7f);
                Widgets.Label(new Rect(area.x, y, area.width, 18f),
                    "Nothing to post here without a compatibility patch. With one installed it would look like:");
                GUI.color = Color.white;
                y += 18f;
            }

            // Columns: Mod | Economy | Military bases | Roads.
            float[] cx = { area.x, area.x + 190f, area.x + 300f, area.x + 430f };
            string[] head = { "Mod", "Economy", "Military bases", "Roads" };
            GUI.color = new Color(0.85f, 0.85f, 0.85f);
            for (int i = 0; i < head.Length; i++) Widgets.Label(new Rect(cx[i], y, 130f, 18f), head[i]);
            GUI.color = Color.white;
            y += 16f;
            Widgets.DrawLineHorizontal(area.x, y, area.width);
            y += 2f;

            if (active.Count == 0)
            {
                // Dimmed worked example — none of this is wired; it illustrates the table's shape only.
                GUI.color = new Color(0.55f, 0.55f, 0.55f);
                DrawExampleRow(cx, y, "VOE-CP", true, false, true);   y += 16f;
                DrawExampleRow(cx, y, "Empire-CP", true, true, true); y += 16f;
                DrawExampleRow(cx, y, "VFE-CP", false, true, false);  y += 16f;
                GUI.color = new Color(0.5f, 0.5f, 0.5f);
                Widgets.Label(new Rect(area.x, y, area.width, 18f), "<i>Framework example — not yet implemented.</i>");
                GUI.color = Color.white;
            }
            else
            {
                // A real integration is present but no patch reports capability columns yet, so we can only
                // confirm the mod, not which world objects it contributes.
                foreach (var name in active)
                {
                    Widgets.Label(new Rect(cx[0], y, 180f, 18f), "<color=#7CFC7C>" + name + "</color>");
                    for (int i = 1; i < cx.Length; i++) Widgets.Label(new Rect(cx[i], y, 120f, 18f), "—");
                    y += 16f;
                }
            }

            Text.Font = GameFont.Small;
        }

        private static void DrawExampleRow(float[] cx, float y, string mod, bool eco, bool mil, bool road)
        {
            Widgets.Label(new Rect(cx[0], y, 180f, 18f), mod);
            Widgets.Label(new Rect(cx[1], y, 120f, 18f), eco ? "Yes" : "No");
            Widgets.Label(new Rect(cx[2], y, 120f, 18f), mil ? "Yes" : "No");
            Widgets.Label(new Rect(cx[3], y, 120f, 18f), road ? "Yes" : "No");
        }

        // Pie view: 0 = by faction, 1 = by hostility. Session-sticky (static), not persisted.
        private static int pieView = 0;

        private enum Hostility { Hostile, Neutral, Friendly }

        /// <summary>A faction's likely starting stance toward the player, read from its def before any world
        /// exists (1.6 has no starting-goodwill range, so this reads the stance flags): permanent/natural
        /// enemies are hostile; of the rest, those that raid unaligned humanlikes read as neutral (prickly
        /// but dealable), and the genuinely civil ones as friendly.</summary>
        private static Hostility HostilityOf(FactionDef def)
        {
            if (def == null) return Hostility.Neutral;
            if (def.permanentEnemy || def.naturalEnemy) return Hostility.Hostile;
            return def.hostileToFactionlessHumanlikes ? Hostility.Neutral : Hostility.Friendly;
        }

        private static Color HostilityColor(Hostility h)
        {
            switch (h)
            {
                case Hostility.Hostile: return new Color(0.85f, 0.35f, 0.35f);
                case Hostility.Friendly: return new Color(0.45f, 0.80f, 0.45f);
                default: return new Color(0.85f, 0.78f, 0.38f);
            }
        }

        private static string HostilityTooltip(Hostility h, FactionDef def)
        {
            string word = h == Hostility.Hostile ? "Hostile" : h == Hostility.Friendly ? "Friendly" : "Neutral";
            string basis = def == null ? ""
                : def.permanentEnemy ? " (permanent enemy)"
                : def.naturalEnemy ? " (natural enemy)"
                : def.hostileToFactionlessHumanlikes ? " (raids the unaligned — prickly, but can be dealt with)"
                : " (starts on good terms)";
            return $"{word} toward the player at world generation{basis}. Starting stance only — it can change in play.";
        }

        // A distinct, stable colour per faction slice — golden-ratio hue spacing keeps adjacent slices apart.
        private static Color SliceColor(int index)
        {
            float hue = (index * 0.61803399f) % 1f;
            return Color.HSVToRGB(hue, 0.55f, 0.92f);
        }

        private static readonly Color WildernessColor = new Color(0.42f, 0.42f, 0.42f);

        /// <summary>The right-hand strip: the value-mode + pie-view toggles up top (co-located with the pie
        /// they drive), then the share pie chart and a legend. The pie reads the SAME distribution as the
        /// faction rows and worldgen, so it is an honest preview of how the world's regions will divide —
        /// either one slice per faction, or grouped into hostile / neutral / friendly.</summary>
        private void DrawPieStrip(Rect strip, int[] dist, int wilderness, int regionBasis)
        {
            float y = strip.y;

            // Pie-view toggle (By faction ↔ By hostility). The value-mode switch was dropped — sizes are
            // always the combined relative-share model.
            Rect pieViewRect = new Rect(strip.x, y, strip.width, 28f);
            if (Widgets.ButtonText(pieViewRect, pieView == 0 ? "Pie: By faction" : "Pie: By hostility"))
                pieView = pieView == 0 ? 1 : 0;
            TooltipHandler.TipRegion(pieViewRect,
                "By faction: one slice per faction.\n\nBy hostility: slices grouped into hostile / neutral / friendly, plus wilderness — the shape of the threat you'll face.");
            y += 34f;

            // Build the slices for the chosen view (colour, label, region count).
            var slices = new List<(Color color, string label, int count)>();
            if (pieView == 0)
            {
                for (int i = 0; i < activeFactions.Count; i++)
                    if (i < dist.Length && dist[i] > 0)
                        slices.Add((SliceColor(i), activeFactions[i].LabelCap, dist[i]));
            }
            else
            {
                int hostile = 0, neutral = 0, friendly = 0;
                for (int i = 0; i < activeFactions.Count; i++)
                {
                    int c = i < dist.Length ? dist[i] : 0;
                    if (c <= 0) continue;
                    switch (HostilityOf(activeFactions[i]))
                    {
                        case Hostility.Hostile: hostile += c; break;
                        case Hostility.Friendly: friendly += c; break;
                        default: neutral += c; break;
                    }
                }
                if (hostile > 0) slices.Add((HostilityColor(Hostility.Hostile), "Hostile", hostile));
                if (neutral > 0) slices.Add((HostilityColor(Hostility.Neutral), "Neutral", neutral));
                if (friendly > 0) slices.Add((HostilityColor(Hostility.Friendly), "Friendly", friendly));
            }
            if (wilderness > 0) slices.Add((WildernessColor, "Wilderness", wilderness));

            // Pie.
            float radius = Mathf.Min(82f, (strip.width - 20f) / 2f);
            Vector2 center = new Vector2(strip.x + strip.width / 2f, y + radius + 4f);
            if (regionBasis > 0 && slices.Count > 0)
            {
                float ang = -90f;
                foreach (var s in slices)
                {
                    float sweep = 360f * s.count / regionBasis;
                    DrawWedge(center, radius, ang, ang + sweep, s.color);
                    ang += sweep;
                }
            }
            else
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = new Color(0.6f, 0.6f, 0.6f);
                Widgets.Label(new Rect(strip.x, y, strip.width, radius * 2f), "no regions yet");
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
            }
            y = center.y + radius + 10f;

            // Legend.
            Text.Font = GameFont.Tiny;
            float rowH = 18f, maxY = strip.yMax - 4f;
            int hidden = 0;
            foreach (var s in slices)
            {
                if (y + rowH > maxY) { hidden++; continue; }
                DrawLegendRow(strip.x, ref y, strip.width, rowH, s.color, s.label, s.count, regionBasis);
            }
            if (hidden > 0 && y + rowH <= maxY)
            {
                GUI.color = new Color(0.6f, 0.6f, 0.6f);
                Widgets.Label(new Rect(strip.x + 16f, y, strip.width - 16f, rowH), $"+{hidden} more…");
                GUI.color = Color.white;
            }
            Text.Font = GameFont.Small;
        }

        // Fill a pie wedge with fine radial lines — no texture or mesh needed, robust in IMGUI.
        private static void DrawWedge(Vector2 center, float radius, float startDeg, float endDeg, Color color)
        {
            for (float a = startDeg; a < endDeg; a += 0.6f)
            {
                float rad = a * Mathf.Deg2Rad;
                Vector2 edge = new Vector2(center.x + Mathf.Cos(rad) * radius, center.y + Mathf.Sin(rad) * radius);
                Widgets.DrawLine(center, edge, color, 2.4f);
            }
        }

        private static void DrawLegendRow(float x, ref float y, float width, float rowH, Color color, string label, int count, int total)
        {
            Widgets.DrawBoxSolid(new Rect(x, y + 3f, 11f, 11f), color);
            int pct = total > 0 ? Mathf.RoundToInt(100f * count / total) : 0;
            string txt = label != null && label.Length > 16 ? label.Substring(0, 15) + "…" : label;
            Widgets.Label(new Rect(x + 16f, y, width - 16f, rowH), $"{txt}  <color=grey>{count} ({pct}%)</color>");
            y += rowH;
        }

        private void DrawWeightSlider(ref float y, float width, float startX, string label, ref float value, float min, float max)
        {
            Rect labelRect = new Rect(startX, y, width, 22f);
            Widgets.Label(labelRect, $"{label}: {value:F2}");
            y += 20f;

            Rect sliderRect = new Rect(startX, y, width, 18f);
            value = Widgets.HorizontalSlider(sliderRect, value, min, max, false, null, null, null, -1f);
            y += 25f;
        }
    }
}
