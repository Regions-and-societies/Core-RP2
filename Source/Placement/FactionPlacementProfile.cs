using Verse;

namespace RegionsAndSocieties
{
    /// <summary>
    /// How one faction weighs land when it chooses territory, what share of the placed territories it
    /// wants, where in the placement order it goes, and how it clusters / splits into kin. One per
    /// FactionDef. Lives in the pure Placement layer (no Find, no Harmony) so the defaults registry (#55)
    /// and its tests can build and compare profiles without a game; the settings store in
    /// <see cref="FactionPlacementSettings"/> scribes the user's copies.
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

        /// <summary>#47: the faction's share of the placed territories, a raw weight shown to the player as
        /// a "%". Shares across factions need not sum to 100 — worldgen normalises by their sum, so a share
        /// is a relative weight, not a hard quota. 0 = unset: resolved on first read to the migrated old
        /// range midpoint (existing saves) or the neutral default (new profiles).</summary>
        public float placementShare = 0f;

        /// <summary>Legacy Settlement Range (#47 removed it from the UI). Kept only so an old save's value
        /// can be read and converted to <see cref="placementShare"/> on load; it is no longer written back
        /// or consumed at worldgen. A registration (#55) may still carry it: the range midpoint seeds the
        /// share when the registration leaves the share unset.</summary>
        public IntRange baseCountRange = new IntRange(5, 15);
        public int placementOrder = 3;

        /// <summary>Kin/clustering: the MINIMUM number of regions a cluster must have to become its own kin
        /// faction (the min-cluster-size clamp — pirates 3, tribes 5, rough unions 7 by default). Combined
        /// with <see cref="numberOfClusters"/>: kin = min(numberOfClusters, floor(regions / clusterSize)).
        /// -1 = unset (resolve to the kind default on first read).</summary>
        public int clusterSize = -1;

        /// <summary>Kin/clustering: the NUMBER OF CLUSTERS the faction divides into (equal division / the max
        /// kin-faction cap — pirates 5, tribes 3, rough unions 2, cohesive factions 1). 0 = one cluster per
        /// region (maximum fragmentation). -1 = unset (resolve to the kind default). The Empire is always 1
        /// and not adjustable.</summary>
        public int numberOfClusters = -1;

        /// <summary>Per-faction kin toggle (replaces the blanket split switch): whether this faction, when
        /// its territory scatters, is organised into geographically separated regional groups. Clustering
        /// (largest contiguous body) is a separate idea. -1 = unset (resolve to the kind default), 0 = off,
        /// 1 = on. Stored as an int so "unset" is distinguishable and a per-kind default can fill it in.</summary>
        public int enableKinRaw = -1;

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
        /// editing the profile it handed out. Copies every field, including the unset (-1 / 0) markers.</summary>
        public FactionPlacementProfile Clone()
        {
            var c = new FactionPlacementProfile(factionDefName, mineralWeight, nutritionWeight, forageWeight, grazingWeight, huntingWeight, marginWeight, baseCountRange.min, baseCountRange.max, placementOrder);
            c.placementShare = placementShare;
            c.clusterSize = clusterSize;
            c.numberOfClusters = numberOfClusters;
            c.enableKinRaw = enableKinRaw;
            return c;
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
            Scribe_Values.Look(ref placementShare, "placementShare", 0f);
            // #47: still READ the legacy range so an old save can be migrated, but never WRITE it — new
            // saves carry the share only. On load, if the save predates the share, convert the range midpoint.
            if (Scribe.mode != LoadSaveMode.Saving)
            {
                Scribe_Values.Look(ref baseCountRange, "baseCountRange", new IntRange(5, 15));
                if (Scribe.mode == LoadSaveMode.LoadingVars && placementShare <= 0f)
                {
                    placementShare = Placement.PlacementShareRules.MigrateRangeToShareWeight(baseCountRange.min, baseCountRange.max);
                }
            }
            Scribe_Values.Look(ref placementOrder, "placementOrder", 3);
            Scribe_Values.Look(ref clusterSize, "clusterSize", -1);
            Scribe_Values.Look(ref numberOfClusters, "numberOfClusters", -1);
            Scribe_Values.Look(ref enableKinRaw, "enableKinRaw", -1);
        }
    }
}
