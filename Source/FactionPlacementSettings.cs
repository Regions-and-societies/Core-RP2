using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RegionsAndSocieties
{
    public class FactionPlacementProfile : IExposable
    {
        public string factionDefName;
        public float mineralWeight = 1.0f;
        public float nutritionWeight = 1.0f;
        public float forageWeight = 1.0f;
        public float grazingWeight = 1.0f;
        public float huntingWeight = 1.0f;
        public float marginWeight = 0.0f;
        public IntRange baseCountRange = new IntRange(5, 15);
        public int placementOrder = 3;

        public FactionPlacementProfile() { }

        public FactionPlacementProfile(string defName, float mineral, float nutrition, float forage, float grazing, float hunting, float margin, int minB, int maxB, int order)
        {
            this.factionDefName = defName;
            this.mineralWeight = mineral;
            this.nutritionWeight = nutrition;
            this.forageWeight = forage;
            this.grazingWeight = grazing;
            this.huntingWeight = hunting;
            this.marginWeight = margin;
            this.baseCountRange = new IntRange(minB, maxB);
            this.placementOrder = order;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref factionDefName, "factionDefName");
            Scribe_Values.Look(ref mineralWeight, "mineralWeight", 1.0f);
            Scribe_Values.Look(ref nutritionWeight, "nutritionWeight", 1.0f);
            Scribe_Values.Look(ref forageWeight, "forageWeight", 1.0f);
            Scribe_Values.Look(ref grazingWeight, "grazingWeight", 1.0f);
            Scribe_Values.Look(ref huntingWeight, "huntingWeight", 1.0f);
            Scribe_Values.Look(ref marginWeight, "marginWeight", 0.0f);
            Scribe_Values.Look(ref baseCountRange, "baseCountRange", new IntRange(5, 15));
            Scribe_Values.Look(ref placementOrder, "placementOrder", 3);
        }
    }

    public class FactionPlacementSettings : ModSettings
    {
        public static Dictionary<string, FactionPlacementProfile> profiles = new Dictionary<string, FactionPlacementProfile>();
        public static int minRegionSize = 75;
        public static int maxRegionSize = 150;
        public static float maxThreatPercent = 0.50f;

        /// <summary>
        /// #51: the single density knob — the target fraction of livable LAND area claimed by territories.
        /// Worldgen sizes total settlement volume to this (against the count of land provinces, the unit of
        /// claimed ground), instead of the old raw tile-count scaling that made planets wall-to-wall and
        /// exploded on large worlds. It is area-weighted (land provinces, ocean excluded) and drives the
        /// total both up and down, so the per-faction counts set only the distribution. Scribed under the
        /// legacy key so existing saves keep their value.
        /// </summary>
        public static float claimedLandAreaPercent = 0.50f;

        /// <summary>
        /// #19: how strongly territory growth prefers squaring off over spidering, 0..1. Candidate
        /// provinces below the desired embeddedness ratio have their suitability scaled down in
        /// proportion, blended in by this weight — 0 is the legacy purely-greedy behaviour, 1 the full
        /// shape penalty. A preference, never a rule: a cornered faction still takes the awkward
        /// province when its land is dramatically better.
        /// </summary>
        public static float territoryCompactness = 0.6f;

        /// <summary>
        /// Whether <b>newly generated</b> worlds enforce R&amp;T's settlement and outpost placement
        /// rules. Worlds already in progress decide for themselves on load and are not affected by
        /// this — a world built without the rules keeps compatibility mode, and one built with them
        /// keeps strict. See <c>SynapseRegionManager.StrictTerritorialOwnership</c>.
        /// </summary>
        public static bool strictTerritorialOwnershipDefault = true;

        /// <summary>
        /// Show the derivation breakdowns in region tooltips (ownership now; economics and produced
        /// goods later) so the numbers can be inspected without Development mode. Off by default (#54).
        /// </summary>
        public static bool showCalculationBreakdowns = false;

        /// <summary>True when calculation breakdowns should be shown — the setting, or Dev Mode.</summary>
        public static bool ShowCalculations => showCalculationBreakdowns || Prefs.DevMode;

        /// <summary>
        /// Which modifier opens a region comparison panel on click (#53): Shift+click when true,
        /// Ctrl+click when false. Configurable so it can be moved off a key that conflicts.
        /// </summary>
        public static bool regionPanelUseShift = false;

        /// <summary>
        /// How many region comparison panels may be open at once (#53). Default 2 for a side-by-side
        /// compare; raise it to experiment with more. When exceeded the oldest panel closes (FIFO).
        /// </summary>
        public static int maxRegionPanels = 2;

        /// <summary>
        /// Set when the player dismisses the "no map-mode framework loaded" popup with "Don't show this
        /// again" (#81), so the either-or warning never nags on subsequent launches once acknowledged.
        /// </summary>
        public static bool mapFrameworkWarningDismissed = false;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref minRegionSize, "minRegionSize", 75);
            Scribe_Values.Look(ref maxRegionSize, "maxRegionSize", 150);
            Scribe_Values.Look(ref maxThreatPercent, "maxThreatPercent", 0.50f);
            Scribe_Values.Look(ref claimedLandAreaPercent, "maxSettlementPercentOfRegions", 0.50f);
            Scribe_Values.Look(ref territoryCompactness, "territoryCompactness", 0.6f);
            Scribe_Values.Look(ref strictTerritorialOwnershipDefault, "strictTerritorialOwnershipDefault", true);
            Scribe_Values.Look(ref showCalculationBreakdowns, "showCalculationBreakdowns", false);
            Scribe_Values.Look(ref regionPanelUseShift, "regionPanelUseShift", false);
            Scribe_Values.Look(ref maxRegionPanels, "maxRegionPanels", 2);
            Scribe_Values.Look(ref mapFrameworkWarningDismissed, "mapFrameworkWarningDismissed", false);

            // 0.7: world-object governance / mod-integration switches.
            Integration.WorldObjectIntegrationSettings.ExposeData();


            List<FactionPlacementProfile> list = profiles.Values.ToList();
            Scribe_Collections.Look(ref list, "profiles", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && list != null)
            {
                profiles.Clear();
                foreach (var p in list)
                {
                    if (p.factionDefName != null)
                    {
                        profiles[p.factionDefName] = p;
                    }
                }
            }
        }

        public static FactionPlacementProfile GetProfile(FactionDef def)
        {
            if (def == null) return null;
            if (!profiles.TryGetValue(def.defName, out var p))
            {
                p = GetDefaultProfile(def);
                profiles[def.defName] = p;
            }
            return p;
        }

        public static FactionPlacementProfile GetDefaultProfile(FactionDef def)
        {
            float mineral = 1.0f;
            float nutrition = 1.0f;
            float forage = 1.0f;
            float grazing = 1.0f;
            float hunting = 1.0f;
            float margin = 0.0f;
            int minB = 5;
            int maxB = 15;

            if (def.techLevel >= TechLevel.Spacer)
            {
                mineral = 2.5f;
                nutrition = 0.5f;
                forage = 0.1f;
                grazing = 0.1f;
                hunting = 0.2f;
                margin = 0.0f;
            }
            else if (def.techLevel == TechLevel.Industrial)
            {
                mineral = 1.0f;
                nutrition = 2.0f;
                forage = 0.2f;
                grazing = 0.8f;
                hunting = 0.8f;
                margin = 0.0f;
            }
            else
            {
                mineral = 0.2f;
                nutrition = 0.2f;
                forage = 2.0f;
                if (def.hostileToFactionlessHumanlikes || def.permanentEnemy)
                {
                    grazing = 0.2f;
                    hunting = 2.0f;
                }
                else
                {
                    grazing = 2.0f;
                    hunting = 0.2f;
                }
                margin = 0.1f;
            }

            int order = 3;
            if (def.defName == "Empire")
            {
                order = 2;
            }
            else if (def.techLevel == TechLevel.Industrial)
            {
                order = 1;
            }
            else if (def.techLevel >= TechLevel.Spacer)
            {
                order = 3;
            }
            else
            {
                order = 4;
            }

            if (def.hostileToFactionlessHumanlikes || def.permanentEnemy)
            {
                margin = 2.5f;
                minB = 3;
                maxB = 8;
            }

            return new FactionPlacementProfile(def.defName, mineral, nutrition, forage, grazing, hunting, margin, minB, maxB, order);
        }
    }
}
