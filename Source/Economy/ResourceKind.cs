using System;

namespace RegionsAndSocieties.Economy
{
    /// <summary>
    /// The resource stocks a province carries. These are exactly the fields
    /// <c>GeographicProvince</c> has always had — naming them lets the economy talk about a
    /// resource without seven parallel code paths.
    /// </summary>
    public enum ResourceKind
    {
        Nutrition = 0,
        Biomass = 1,
        Minerals = 2,
        Textiles = 3,
        PreIndustrialGoods = 4,
        IndustrialGoods = 5,
        SpacerGoods = 6
    }

    /// <summary>
    /// How a resource comes back once it has been drawn down. This is the distinction the whole
    /// growth model turns on, and it is a property of the resource rather than of who owns it.
    /// </summary>
    public enum RenewalClass
    {
        /// <summary>Regrows on the land's own terms. Forests return, soil recovers, herds breed.</summary>
        Biological = 0,

        /// <summary>
        /// Does not regrow. New stock is found rather than grown, which in RimWorld's own fiction
        /// means deep drilling and long-range scanning — so recovery is bought with research and
        /// competent people, not with time.
        /// </summary>
        Geological = 1,

        /// <summary>
        /// Made, not extracted. Has no natural pool at all: production writes it and consumption
        /// reads it, so a cap-and-regrow model would be meaningless here.
        /// </summary>
        Manufactured = 2
    }

    public static class ResourceKindExtensions
    {
        public static RenewalClass Renewal(this ResourceKind kind)
        {
            switch (kind)
            {
                case ResourceKind.Nutrition:
                case ResourceKind.Biomass:
                case ResourceKind.Textiles:
                    return RenewalClass.Biological;

                case ResourceKind.Minerals:
                    return RenewalClass.Geological;

                default:
                    return RenewalClass.Manufactured;
            }
        }

        /// <summary>Whether this resource has a natural pool that can be depleted and recovered.</summary>
        public static bool IsExtracted(this ResourceKind kind)
        {
            return kind.Renewal() != RenewalClass.Manufactured;
        }

        public static string Label(this ResourceKind kind)
        {
            switch (kind)
            {
                case ResourceKind.Nutrition: return "nutrition";
                case ResourceKind.Biomass: return "biomass";
                case ResourceKind.Minerals: return "minerals";
                case ResourceKind.Textiles: return "textiles";
                case ResourceKind.PreIndustrialGoods: return "pre-industrial goods";
                case ResourceKind.IndustrialGoods: return "industrial goods";
                case ResourceKind.SpacerGoods: return "spacer goods";
                default: return "resources";
            }
        }
    }
}
