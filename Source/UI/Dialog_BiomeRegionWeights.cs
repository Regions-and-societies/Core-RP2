using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using RegionsAndSocieties.Partition;

namespace RegionsAndSocieties.UI
{
    /// <summary>
    /// The biome region-size weighter: a slider per naturally-generating biome that scales how big that
    /// biome's regions are (higher = fewer, larger regions). Ours are the defaults; a slider only writes an
    /// override when the player moves it away from the default, and snaps back to "no override" near it.
    /// Writes <see cref="BiomeRegionWeights.Overrides"/>, which the settings scribe persists.
    /// </summary>
    public class Dialog_BiomeRegionWeights : Window
    {
        private Vector2 scroll;
        private readonly List<BiomeDef> biomes;

        public override Vector2 InitialSize => new Vector2(560f, 640f);

        public Dialog_BiomeRegionWeights()
        {
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            biomes = DefDatabase<BiomeDef>.AllDefs
                .Where(b => b != null && b.generatesNaturally && !b.impassable)
                .OrderByDescending(BiomeRegionWeights.DefaultFor)
                .ThenBy(b => b.label)
                .ToList();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width - 120f, 34f), "Biome region sizes");
            Text.Font = GameFont.Small;
            if (Widgets.ButtonText(new Rect(inRect.width - 110f, 4f, 100f, 28f), "Reset all"))
                BiomeRegionWeights.Overrides.Clear();

            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Widgets.Label(new Rect(0f, 38f, inRect.width, 40f),
                "Multiplier on each biome's region size — higher = fewer, larger regions. Sparse biomes (ice, desert) default higher. Applies to newly generated worlds.");
            GUI.color = Color.white;

            Rect outRect = new Rect(0f, 84f, inRect.width, inRect.height - 84f - 42f);
            const float rowH = 52f;
            Rect viewRect = new Rect(0f, 0f, outRect.width - 20f, biomes.Count * rowH);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);
            float y = 0f;
            foreach (var b in biomes)
            {
                float def = BiomeRegionWeights.DefaultFor(b);
                bool overridden = BiomeRegionWeights.Overrides.ContainsKey(b.defName);
                float cur = BiomeRegionWeights.Weight(b);

                Widgets.Label(new Rect(0f, y, viewRect.width - 90f, 24f),
                    $"{b.LabelCap}:  <color=#A6FF9E>{cur:0.0}×</color>  <color=grey>(default {def:0.0}×{(overridden ? ", changed" : "")})</color>");
                if (overridden && Widgets.ButtonText(new Rect(viewRect.width - 84f, y, 84f, 22f), "Default"))
                    BiomeRegionWeights.Overrides.Remove(b.defName);

                float nv = Widgets.HorizontalSlider(new Rect(0f, y + 26f, viewRect.width, 18f), cur,
                    BiomeRegionWeights.MinWeight, BiomeRegionWeights.MaxWeight, false, null, null, null, 0.25f);
                if (Mathf.Abs(nv - cur) > 0.001f)
                {
                    if (Mathf.Abs(nv - def) < 0.13f) BiomeRegionWeights.Overrides.Remove(b.defName);   // snap back to default
                    else BiomeRegionWeights.Overrides[b.defName] = nv;
                }
                y += rowH;
            }
            Widgets.EndScrollView();

            if (Widgets.ButtonText(new Rect(inRect.width / 2f - 60f, inRect.height - 34f, 120f, 30f), "Close"))
                Close();
        }
    }
}
