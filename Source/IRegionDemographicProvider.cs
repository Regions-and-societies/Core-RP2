using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties
{
    public interface IRegionDemographicProvider
    {
        string ProviderName { get; }
        float GetDemographicMatchRatio(GeographicProvince province, Faction faction);
    }

    public static class RegionalDemographicRegistry
    {
        private static readonly List<IRegionDemographicProvider> providers = new List<IRegionDemographicProvider>();

        public static void RegisterProvider(IRegionDemographicProvider provider)
        {
            if (provider != null && !providers.Contains(provider))
            {
                providers.Add(provider);
            }
        }

        public static bool HasProviders => providers.Count > 0;

        public static float GetCombinedDemographicScore(GeographicProvince province, Faction faction)
        {
            if (providers.Count == 0) return -1f;
            float total = 0f;
            foreach (var provider in providers)
            {
                try
                {
                    total += provider.GetDemographicMatchRatio(province, faction);
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce($"[RegionsAndSocieties] Error in demographic provider {provider?.ProviderName}: {ex}", 88102938);
                }
            }
            return Mathf.Clamp01(total / providers.Count);
        }
    }
}
