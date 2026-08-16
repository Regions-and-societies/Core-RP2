using UnityEngine;
using Verse;

namespace RegionsAndSocieties
{
    public static class TextureUtility
    {
        public static Texture2D MakeTextureReadableAndTransparent(Texture2D originalTex)
        {
            if (originalTex == null) return null;

            RenderTexture rt = RenderTexture.GetTemporary(
                originalTex.width,
                originalTex.height,
                0,
                RenderTextureFormat.Default,
                RenderTextureReadWrite.Linear);

            Graphics.Blit(originalTex, rt);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D readableText = new Texture2D(originalTex.width, originalTex.height);
            readableText.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            readableText.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            Color[] pixels = readableText.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                float brightness = pixels[i].r + pixels[i].g + pixels[i].b;
                if (brightness < 0.15f)
                {
                    pixels[i] = Color.clear;
                }
                else
                {
                    float gray = brightness / 3f;
                    pixels[i] = new Color(1f, 1f, 1f, gray);
                }
            }
            readableText.SetPixels(pixels);
            readableText.Apply();
            return readableText;
        }

        /// <summary>
        /// A compatibility patch may supply a nicer display name for factions its mod owns —
        /// Empire's patch names the player's empire from the faction component, for example. First
        /// non-null answer wins; a throwing provider is skipped. Core itself names no foreign
        /// faction (the old hardcoded PColony/FindFC branch moved to Empire-CP with the rest of
        /// that integration, Core-MMF#3).
        /// </summary>
        public static System.Func<RimWorld.Faction, string> FactionDisplayNameOverride;

        public static string GetFactionDisplayName(RimWorld.Faction faction)
        {
            if (faction == null) return "Unknown";

            try
            {
                string overridden = FactionDisplayNameOverride?.Invoke(faction);
                if (!overridden.NullOrEmpty()) return overridden;
            }
            catch
            {
                // A broken provider must never take the map-mode legend down with it.
            }

            return faction.Name;
        }
    }
}
