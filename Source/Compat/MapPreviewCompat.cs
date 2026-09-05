using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RegionsAndSocieties.Compat
{
    /// <summary>
    /// Soft detection of <b>Map Preview</b> (m00nl1ght) and its "a preview is generating right now" flag (#45).
    ///
    /// <para>Map Preview runs map generation for the selected world tile on a worker thread. Parts of
    /// that generation flood-fill the world grid through <c>Find.WorldFloodFiller</c>, which is one
    /// shared, non-re-entrant instance. If R&amp;S flood-fills on the main thread at the same moment
    /// (a placement evaluation for the same selected tile), vanilla logs
    /// <c>Nested FloodFill calls are not allowed</c> and both results are suspect. Callers that can
    /// defer their flood (the inspect pane) check <see cref="IsGeneratingPreview"/> first.</para>
    ///
    /// <para>Resolved once, by name, through reflection — no assembly reference, no hard dependency.
    /// Map Preview's public API is <c>MapPreview.MapPreviewAPI.IsGeneratingPreview</c>. Anything
    /// missing or unexpected degrades to "not generating", i.e. the pre-0.3.2 behaviour.</para>
    /// </summary>
    public static class MapPreviewCompat
    {
        private const string ApiTypeName = "MapPreview.MapPreviewAPI";
        private const string GeneratingProperty = "IsGeneratingPreview";

        private static bool resolved;
        private static Func<bool> isGenerating;

        /// <summary>Whether Map Preview is loaded and its API was found.</summary>
        public static bool Present
        {
            get
            {
                Resolve();
                return isGenerating != null;
            }
        }

        /// <summary>True while Map Preview is generating a preview on its worker thread. False when Map Preview is absent.</summary>
        public static bool IsGeneratingPreview
        {
            get
            {
                Resolve();
                if (isGenerating == null) return false;
                try
                {
                    return isGenerating();
                }
                catch (Exception ex)
                {
                    Log.Warning("[RegionsAndSocieties] Map Preview generating-flag read failed; disabling the check: " + ex.Message);
                    isGenerating = null;
                    return false;
                }
            }
        }

        /// <summary>Test seam / manual override: replace the probe (null restores the reflection lookup).</summary>
        public static void OverrideProbe(Func<bool> probe)
        {
            isGenerating = probe;
            resolved = probe != null;
        }

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;

            try
            {
                Type api = AccessTools.TypeByName(ApiTypeName);
                if (api == null) return;

                PropertyInfo prop = AccessTools.Property(api, GeneratingProperty);
                MethodInfo getter = prop != null ? prop.GetGetMethod(true) : null;
                if (getter == null || !getter.IsStatic || getter.ReturnType != typeof(bool))
                {
                    Log.Warning("[RegionsAndSocieties] Map Preview is loaded but " + ApiTypeName + "." + GeneratingProperty +
                                " was not found in the expected shape; placement hints will not wait for previews.");
                    return;
                }

                isGenerating = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), getter);
                Log.Message("[RegionsAndSocieties] Map Preview detected; inspect-pane placement hints will wait while a preview generates.");
            }
            catch (Exception ex)
            {
                Log.Warning("[RegionsAndSocieties] Map Preview detection failed (continuing without it): " + ex.Message);
                isGenerating = null;
            }
        }
    }
}
