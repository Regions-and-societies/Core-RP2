using RimWorld;
using UnityEngine;

namespace RegionsAndSocieties.UI
{
    /// <summary>
    /// One place for the demographic category colours (#26), so the visual inspection panel and the
    /// map-mode overlays read as one system. The categorical axes (age bands, education/SES tiers,
    /// employment sectors, sex) get fixed palettes whose direction matches their overlay's gradient;
    /// xenotypes reuse the xenotype overlay's stable defName hash so a caste keeps one recognisable
    /// colour across the panel and the map; ideologies use their own in-game colour.
    /// </summary>
    public static class DemographicColors
    {
        // Age: child -> elder, green -> red (matches MapMode_AgeStructure's youthful->elderly gradient).
        public static readonly Color[] Age =
        {
            new Color(0.34f, 0.72f, 0.38f),   // Child — green
            new Color(0.90f, 0.80f, 0.22f),   // WorkingAge — yellow
            new Color(0.85f, 0.28f, 0.34f),   // Elder — red
        };

        // Sex: female / male, matching the sex-ratio overlay ends (magenta / blue).
        public static readonly Color Female = new Color(0.82f, 0.32f, 0.66f);
        public static readonly Color Male = new Color(0.28f, 0.52f, 0.86f);

        // Education: illiterate -> postgrad, brown -> indigo (matches MapMode_Education's gradient).
        public static readonly Color[] Education =
        {
            new Color(0.52f, 0.37f, 0.22f),   // Illiterate — brown
            new Color(0.78f, 0.66f, 0.36f),   // Primary — tan
            new Color(0.42f, 0.68f, 0.52f),   // Secondary — sage green
            new Color(0.28f, 0.56f, 0.84f),   // Undergrad — blue
            new Color(0.45f, 0.42f, 0.80f),   // Postgrad — indigo
        };

        // Socioeconomic: subsistence -> affluent, drab -> gold.
        public static readonly Color[] Ses =
        {
            new Color(0.44f, 0.40f, 0.36f),   // Subsistence — drab
            new Color(0.60f, 0.58f, 0.40f),   // Modest — muted
            new Color(0.78f, 0.68f, 0.34f),   // Prosperous — warm
            new Color(0.94f, 0.80f, 0.28f),   // Affluent — gold
        };

        // Employment: the exact per-sector colours from MapMode_Employment.
        public static readonly Color[] Employment =
        {
            new Color(0.40f, 0.70f, 0.30f),   // Agriculture — green
            new Color(0.55f, 0.60f, 0.68f),   // Industry — steel
            new Color(0.80f, 0.25f, 0.25f),   // Military — red
            new Color(0.90f, 0.72f, 0.25f),   // Trade — gold
        };

        /// <summary>The xenotype's stable colour — the same FNV-1a defName hash the xenotype overlay
        /// uses, so a caste keeps one recognisable colour in the panel and on the map.</summary>
        public static Color Xenotype(XenotypeDef xenotype)
        {
            uint h = 2166136261u;
            string name = xenotype?.defName ?? "";
            for (int i = 0; i < name.Length; i++) { h ^= name[i]; h *= 16777619u; }
            float hue = (h % 3600u) / 3600f;                 // 0..1 around the wheel
            float sat = 0.55f + ((h >> 12) % 100u) / 400f;   // 0.55..0.80
            float val = 0.80f + ((h >> 20) % 100u) / 500f;   // 0.80..1.00
            return Color.HSVToRGB(hue, Mathf.Clamp01(sat), Mathf.Clamp01(val));
        }

        /// <summary>An ideology's own in-game colour (matches the ideology overlay and the rest of the UI).</summary>
        public static Color Ideology(Ideo ideo)
        {
            return ideo != null ? ideo.Color : new Color(0.6f, 0.6f, 0.62f);
        }
    }
}
