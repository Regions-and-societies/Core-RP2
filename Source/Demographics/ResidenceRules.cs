using System;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>How a settled population resolves into homes. A region's population is a head count of
    /// people; this derives the RESIDENCES (dwellings) that hold them, their occupancy, and the land each
    /// takes, from a single driver — how urban the place is. Rural land is a homestead of an extended
    /// family on a wide plot; a city is nuclear households packed onto small lots. So as a tile urbanises,
    /// occupancy falls (7 → ~2), residence count rises for the same population, and land per pawn shrinks.
    ///
    /// <para>Pure and deterministic (no game types), so it unit-tests against the hand-written doubles.
    /// Every number below is a tunable endpoint, not a hidden constant.</para></summary>
    public static class ResidenceRules
    {
        /// <summary>People per residence in a fully rural place — an extended family under one roof.</summary>
        public const float RuralOccupancy = 7f;
        /// <summary>People per residence in a fully urban place — nuclear households and singles.</summary>
        public const float UrbanOccupancy = 1.8f;

        /// <summary>At or below this population a tile reads as fully rural (urbanization 0).</summary>
        public const int RuralPopulation = 15;
        /// <summary>At or above this population a tile reads as fully urban (urbanization 1).</summary>
        public const int CityPopulation = 250;

        /// <summary>Land a residence occupies in a fully rural place (relative units — a sprawling plot).</summary>
        public const float RuralLandPerResidence = 1f;
        /// <summary>Land a residence occupies in a fully urban place (relative units — a tight lot).</summary>
        public const float UrbanLandPerResidence = 0.12f;

        /// <summary>How urban a place is, 0 (rural) to 1 (city), from the population concentrated on it.
        /// Smoothstepped between the rural and city population endpoints so the transition eases at both
        /// ends rather than snapping.</summary>
        public static float Urbanization(int population)
        {
            if (CityPopulation <= RuralPopulation) return population >= CityPopulation ? 1f : 0f;
            float t = (population - RuralPopulation) / (float)(CityPopulation - RuralPopulation);
            t = Clamp01(t);
            return t * t * (3f - 2f * t);   // smoothstep
        }

        /// <summary>Average people per residence at an urbanization level.</summary>
        public static float Occupancy(float urbanization) => Lerp(RuralOccupancy, UrbanOccupancy, Clamp01(urbanization));

        /// <summary>Relative land a single residence occupies at an urbanization level.</summary>
        public static float LandPerResidence(float urbanization) => Lerp(RuralLandPerResidence, UrbanLandPerResidence, Clamp01(urbanization));

        /// <summary>The full residence picture for a population: how many homes, how full, how much land.</summary>
        public static ResidenceProfile For(int population)
        {
            if (population <= 0)
                return new ResidenceProfile { population = 0, urbanization = 0f, occupancy = RuralOccupancy, residences = 0, landPerResidence = RuralLandPerResidence, landPerPawn = RuralLandPerResidence / RuralOccupancy, tier = ResidenceTier.Homestead };

            float u = Urbanization(population);
            float occ = Occupancy(u);
            int residences = Math.Max(1, (int)Math.Round(population / occ, MidpointRounding.AwayFromZero));
            float landRes = LandPerResidence(u);
            return new ResidenceProfile
            {
                population = population,
                urbanization = u,
                occupancy = occ,
                residences = residences,
                landPerResidence = landRes,
                landPerPawn = landRes / occ,
                tier = TierFor(u),
            };
        }

        /// <summary>The named settlement tier for an urbanization level.</summary>
        public static ResidenceTier TierFor(float urbanization)
        {
            if (urbanization < 0.20f) return ResidenceTier.Homestead;
            if (urbanization < 0.45f) return ResidenceTier.Village;
            if (urbanization < 0.75f) return ResidenceTier.Town;
            return ResidenceTier.City;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }

    /// <summary>The settled tier of a place, from a lone rural homestead to a dense city.</summary>
    public enum ResidenceTier { Homestead, Village, Town, City }

    /// <summary>The derived residence picture for a population.</summary>
    public struct ResidenceProfile
    {
        public int population;          // people (the input head count)
        public float urbanization;      // 0 rural .. 1 city
        public int residences;          // dwellings that hold them
        public float occupancy;         // average people per residence
        public float landPerResidence;  // relative land one residence occupies
        public float landPerPawn;       // relative personal land per person
        public ResidenceTier tier;
    }
}
