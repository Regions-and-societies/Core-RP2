using System;

namespace RegionsAndSocieties.Integration
{
    /// <summary>
    /// Mod-agnostic classification of a world-map object.
    /// Regions &amp; Territories governance keys off this, never off a concrete mod type.
    /// </summary>
    public enum WorldObjectKind
    {
        /// <summary>Could not be classified. Governance treats these as inert.</summary>
        Unknown = 0,

        /// <summary>A permanent population centre (vanilla Settlement, Empire WorldSettlementFC, VFE settlements, ...).</summary>
        Settlement = 1,

        /// <summary>A production/extraction holding (VOE Outpost, Empire sub-holdings, ...).</summary>
        Outpost = 2,

        /// <summary>A temporary encampment (caravan camps, VFE camps, raid staging).</summary>
        Camp = 3,

        /// <summary>A military installation or standing force projection point.</summary>
        Military = 4,

        /// <summary>A quest/map site. Not territorial, but placement may still be gated.</summary>
        Site = 5,

        /// <summary>A moving group. Never territorial.</summary>
        Caravan = 6,

        /// <summary>Explicitly excluded from governance (destroyed settlements, markers, debug objects).</summary>
        Ignored = 7
    }

    public static class WorldObjectKindExtensions
    {
        /// <summary>
        /// Does this kind contribute to — and get governed by — regional territory ownership?
        /// </summary>
        public static bool IsTerritorial(this WorldObjectKind kind)
        {
            switch (kind)
            {
                case WorldObjectKind.Settlement:
                case WorldObjectKind.Outpost:
                case WorldObjectKind.Camp:
                case WorldObjectKind.Military:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Is this a permanent holding (as opposed to a transient camp or caravan)?
        /// Placement buffer/supply rules apply to these.
        /// </summary>
        public static bool IsPermanentHolding(this WorldObjectKind kind)
        {
            return kind == WorldObjectKind.Settlement
                || kind == WorldObjectKind.Outpost
                || kind == WorldObjectKind.Military;
        }

        /// <summary>Does this kind carry a resident population that feeds density/economy math?</summary>
        public static bool HasPopulation(this WorldObjectKind kind)
        {
            return kind == WorldObjectKind.Settlement
                || kind == WorldObjectKind.Outpost
                || kind == WorldObjectKind.Camp
                || kind == WorldObjectKind.Military;
        }
    }
}
