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
        private List<FactionDef> activeFactions;

        // 0.7 added the world-object integration panel, so the window needs a little more height.
        public override Vector2 InitialSize => new Vector2(860f, 780f);

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
            Widgets.Label(new Rect(0f, 0f, inRect.width, 35f), "Regions and Societies - Geographic Placement Settings");
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

            int totalTiles = Mathf.RoundToInt(100000f * coverage);
            int landTiles = Mathf.RoundToInt(totalTiles * 0.38f); // ~38% of tiles are land on average

            float avgSize = (FactionPlacementSettings.minRegionSize + FactionPlacementSettings.maxRegionSize) / 2f;
            int estIdeal = Mathf.RoundToInt(landTiles / avgSize);

            // Due to biome fragmentation, the actual count is ~1.4x to 2.0x of the ideal
            int estMin = Mathf.RoundToInt(estIdeal * 1.4f);
            int estMax = Mathf.RoundToInt(estIdeal * 2.0f);

            if (estMin < 1) estMin = 1;
            if (estMax < 1) estMax = 1;

            // Global Map Region Parameters Panel
            Rect globalBoxRect = new Rect(0f, 40f, inRect.width - 15f, 160f);
            Widgets.DrawMenuSection(globalBoxRect);

            Rect globalTitleRect = new Rect(10f, 44f, 300f, 22f);
            Widgets.Label(globalTitleRect, "<b>Global Map Region Parameters</b>");

            // Left Column (Min size)
            float colWidth = (globalBoxRect.width - 30f) / 2f;
            Rect minLabelRect = new Rect(10f, 68f, 150f, 22f);
            Widgets.Label(minLabelRect, $"Min Size: {FactionPlacementSettings.minRegionSize} tiles");
            Rect minSliderRect = new Rect(165f, 70f, colWidth - 170f, 18f);
            float tempMin = Widgets.HorizontalSlider(minSliderRect, FactionPlacementSettings.minRegionSize, 20f, 150f, false, null, null, null, 1f);
            FactionPlacementSettings.minRegionSize = Mathf.RoundToInt(tempMin);

            // Right Column (Max size)
            float rightColStart = 10f + colWidth + 10f;
            Rect maxLabelRect = new Rect(rightColStart, 68f, 150f, 22f);
            Widgets.Label(maxLabelRect, $"Max Size: {FactionPlacementSettings.maxRegionSize} tiles");
            Rect maxSliderRect = new Rect(rightColStart + 165f, 70f, colWidth - 170f, 18f);
            float tempMax = Widgets.HorizontalSlider(maxSliderRect, FactionPlacementSettings.maxRegionSize, 50f, 400f, false, null, null, null, 1f);
            FactionPlacementSettings.maxRegionSize = Mathf.RoundToInt(tempMax);

            // Second Row (Max Threat / Max Occupancy)
            Rect threatLabelRect = new Rect(10f, 98f, 150f, 22f);
            Widgets.Label(threatLabelRect, $"Max Threat: {Mathf.RoundToInt(FactionPlacementSettings.maxThreatPercent * 100f)}%");
            Rect threatSliderRect = new Rect(165f, 100f, colWidth - 170f, 18f);
            float tempThreat = Widgets.HorizontalSlider(threatSliderRect, FactionPlacementSettings.maxThreatPercent, 0.10f, 1.00f, false, null, null, null, 0.01f);
            FactionPlacementSettings.maxThreatPercent = tempThreat;

            Rect occupLabelRect = new Rect(rightColStart, 98f, 165f, 22f);
            Widgets.Label(occupLabelRect, $"Claimed land area: {Mathf.RoundToInt(FactionPlacementSettings.claimedLandAreaPercent * 100f)}%");
            Rect occupSliderRect = new Rect(rightColStart + 165f, 100f, colWidth - 170f, 18f);
            float tempOccup = Widgets.HorizontalSlider(occupSliderRect, FactionPlacementSettings.claimedLandAreaPercent, 0.10f, 0.90f, false, null, null, null, 0.01f);
            FactionPlacementSettings.claimedLandAreaPercent = tempOccup;

            // Estimates row
            Rect estRect = new Rect(10f, 135f, globalBoxRect.width - 20f, 22f);
            Widgets.Label(estRect, $"Estimated Land Tiles: <color=cyan>{landTiles}</color> (at {Mathf.RoundToInt(coverage * 100f)}% coverage) | Expected Region Count: <color=green>{estMin} - {estMax}</color> (Avg Size: {avgSize:F0} tiles)");

            // Box is tall enough for the "Detected:" status line at the bottom (title + master +
            // four 24px rows + the status row need ~178px); outRect starts below it so the label
            // can't spill onto the Faction Geography scroll panel (#47).
            DrawIntegrationPanel(new Rect(0f, 205f, inRect.width - 15f, 178f));

            Rect outRect = new Rect(0f, 388f, inRect.width, inRect.height - 443f);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 25f, activeFactions.Count * 265f);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            float curY = 0f;

            foreach (var def in activeFactions)
            {
                var profile = FactionPlacementSettings.GetProfile(def);

                Rect boxRect = new Rect(0f, curY, viewRect.width, 255f);
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
                    profile.baseCountRange = defaultProfile.baseCountRange;
                    profile.placementOrder = defaultProfile.placementOrder;
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

                // Bases counts
                Rect basesRect = new Rect(10f, curY + 180f, boxRect.width - 20f, 24f);
                Widgets.Label(new Rect(basesRect.x, basesRect.y, 250f, 24f), $"Settlement Range: {profile.baseCountRange.min} - {profile.baseCountRange.max}");
                Widgets.IntRange(new Rect(basesRect.x + 260f, basesRect.y, basesRect.width - 270f, 24f), def.GetHashCode(), ref profile.baseCountRange, 1, 50, null, 0);

                // Placement Order
                Rect orderRect = new Rect(10f, curY + 215f, boxRect.width - 20f, 24f);
                Widgets.Label(new Rect(orderRect.x, orderRect.y, 250f, 24f), $"Placement Turn Order (Priority): {profile.placementOrder}");
                float tempOrder = Widgets.HorizontalSlider(new Rect(orderRect.x + 260f, orderRect.y, orderRect.width - 270f, 18f), (float)profile.placementOrder, 1f, 10f, false, null, null, null, 1f);
                profile.placementOrder = Mathf.RoundToInt(tempOrder);

                curY += 265f;
            }

            Widgets.EndScrollView();

            Rect closeButtonRect = new Rect(inRect.width / 2f - 75f, inRect.height - 45f, 150f, 35f);
            if (Widgets.ButtonText(closeButtonRect, "Close"))
            {
                this.Close();
            }
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
            Widgets.Label(titleRect, "<b>World Object Mod Integration</b>");

            Rect masterRect = new Rect(box.x + 10f, box.y + 28f, box.width - 20f, 22f);
            Widgets.CheckboxLabeled(masterRect, "Govern world objects added by other mods",
                ref Integration.WorldObjectIntegrationSettings.masterEnabled);
            TooltipHandler.TipRegion(masterRect,
                "Master switch. When off, Regions & Territories recognises only vanilla settlements and leaves modded outposts, camps, and bases entirely alone.");

            bool on = Integration.WorldObjectIntegrationSettings.masterEnabled;
            float colW = (box.width - 20f) / 4f;

            // Row 1 — per-mod integrations.
            float y = box.y + 52f;
            // Row 1 held the four per-mod integration toggles until the compatibility inversion
            // (Core-MMF#3) moved every foreign-mod integration into its own patch. A patch is
            // enabled by being installed, so no per-mod toggles remain — only the master switch
            // and the per-mechanic rows below.

            // Row 2 — which mechanics the integration is allowed to drive.
            y += 24f;
            IntegrationToggle(new Rect(box.x + 10f + colW * 0f, y, colW - 8f, 22f), "Placement rules",
                ref Integration.WorldObjectIntegrationSettings.placementGovernance, on,
                "Apply region ownership, buffer distance, and supply range to where modded objects can be built.");
            IntegrationToggle(new Rect(box.x + 10f + colW * 1f, y, colW - 8f, 22f), "Economy rules",
                ref Integration.WorldObjectIntegrationSettings.economyGovernance, on,
                "Scale modded production by regional security, local resource richness, and surrounding population.");
            IntegrationToggle(new Rect(box.x + 10f + colW * 2f, y, colW - 8f, 22f), "Military rules",
                ref Integration.WorldObjectIntegrationSettings.militaryGovernance, on,
                "Apply adjacency and supply-line limits to modded military and expansion actions.");
            IntegrationToggle(new Rect(box.x + 10f + colW * 3f, y, colW - 8f, 22f), "Settlement tiers",
                ref Integration.WorldObjectIntegrationSettings.settlementTiers, on,
                "Classify settlements as village, town, city, or major city based on population.");

            // Row 3 — territorial ownership mode.
            y += 24f;
            var manager = Find.World?.GetComponent<SynapseRegionManager>();
            if (manager != null)
            {
                // A world is loaded, so edit that world's own flag rather than the default for new
                // ones. Changing it mid-playthrough is legitimate but not free: the rules start or
                // stop applying around settlements that were placed under the other regime.
                bool strict = manager.StrictTerritorialOwnership;
                bool before = strict;
                Rect worldRect = new Rect(box.x + 10f, y, box.width - 20f, 22f);
                Widgets.CheckboxLabeled(worldRect, "Strict territorial ownership (this world)", ref strict);
                TooltipHandler.TipRegion(worldRect,
                    "On: Regions & Territories decides where settlements and outposts may be built — buffers, supply range and footholds.\n\n" +
                    "Off (compatibility): placement is left entirely to vanilla and other mods, and more than one settlement may share a province. " +
                    "Regions are still generated and territory is still owned and drawn.\n\n" +
                    "Worlds that existed before Regions & Territories was installed start in compatibility mode, because their settlements were " +
                    "placed with no regard for these rules. Turning it on now will start refusing placements near towns that are already there.\n\n" +
                    "Compatibility mode exists so an existing save is usable, not so it is equivalent. For the full experience, start a new " +
                    "colony with the mod already installed.");
                if (strict != before) manager.StrictTerritorialOwnership = strict;
            }
            else
            {
                Rect defRect = new Rect(box.x + 10f, y, box.width - 20f, 22f);
                Widgets.CheckboxLabeled(defRect, "Strict territorial ownership (new worlds)",
                    ref FactionPlacementSettings.strictTerritorialOwnershipDefault);
                TooltipHandler.TipRegion(defRect,
                    "Whether newly generated worlds enforce Regions & Territories' placement rules. " +
                    "Worlds already in progress keep whatever mode they were adopted under; load a save to change that world's setting.");
            }

            // Row 4 — diagnostics.
            y += 24f;
            Rect logRect = new Rect(box.x + 10f, y, box.width - 20f, 22f);
            Widgets.CheckboxLabeled(logRect, "Log world object types no integration recognises",
                ref Integration.WorldObjectIntegrationSettings.logUnknownWorldObjects, !on);
            TooltipHandler.TipRegion(logRect,
                "Writes one message per unrecognised type. Useful when reporting a mod that Regions & Territories should support.");

            // Status line — what actually got detected in this game.
            y += 24f;
            Rect statusRect = new Rect(box.x + 10f, y, box.width - 20f, 22f);
            Widgets.Label(statusRect, "Detected: " + DescribeActiveIntegrations());
        }

        private static void IntegrationToggle(Rect rect, string label, ref bool value, bool enabled, string tooltip)
        {
            Widgets.CheckboxLabeled(rect, label, ref value, !enabled);
            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }
        }

        private static string DescribeActiveIntegrations()
        {
            var names = new List<string>();
            foreach (var adapter in Integration.WorldObjectAdapterRegistry.Adapters)
            {
                bool active;
                try { active = adapter.IsActive; }
                catch (Exception) { active = false; }

                if (active && adapter.AdapterId != "vanilla")
                {
                    names.Add(adapter.DisplayName);
                }
            }

            if (names.Count == 0)
            {
                return "<color=grey>vanilla world objects only</color>";
            }
            return "<color=green>" + string.Join(", ", names.ToArray()) + "</color>";
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
