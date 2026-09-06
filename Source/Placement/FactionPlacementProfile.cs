using Verse;

namespace RegionsAndSocieties
{
    /// <summary>
    /// How one faction weighs land when it chooses territory, how many holdings it wants, and where in
    /// the placement order it goes. One per FactionDef. Lives in the pure Placement layer (no Find, no
    /// Harmony) so the defaults registry (#55) and its tests can build and compare profiles without a
    /// game; the settings store in <see cref="FactionPlacementSettings"/> scribes the user's copies.
    /// </summary>
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

        /// <summary>An independent copy, so a registered template is never mutated by the settings UI
        /// editing the profile it handed out.</summary>
        public FactionPlacementProfile Clone()
        {
            return new FactionPlacementProfile(factionDefName, mineralWeight, nutritionWeight, forageWeight, grazingWeight, huntingWeight, marginWeight, baseCountRange.min, baseCountRange.max, placementOrder);
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
}
