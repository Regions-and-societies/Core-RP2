using System;
using System.Collections;
using System.Collections.Generic;
using MapModeFramework;
using RimWorld;
using RimWorld.Planet;
using RegionsAndSocieties.Integration;
using RegionsAndSocieties.Sizing;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties
{
    /// <summary>
    /// Auto-discovered world draw layer that marks each faction's <b>capital</b> — its single
    /// most-protected settlement (<see cref="SettlementSizeUtility.CapitalOf"/>) — with a filled
    /// five-pointed star inside a ring, tinted the faction's colour. The star texture is white, so the
    /// faction colour comes straight from the material tint; one material is cached per colour.
    ///
    /// <para>Drawn as a textured decal on the capital's tile via <see cref="TileUtilities.DrawTile"/>
    /// — the same proven path Map Mode Framework uses — so it needs no per-object icon patching and
    /// works over any settlement mod's art. Modelled on <see cref="WorldLayer_RegionBorders"/>:
    /// origin-pinned transform (a global layer), retry-until-built regeneration, and a guarded Render
    /// so a draw error degrades the marker rather than the frame.</para>
    /// </summary>
    public class WorldLayer_CapitalMarkers : WorldDrawLayer
    {
        private int builtVersion = -1;
        private bool built;
        private static bool loggedRenderError;

        // Above the region-border overlay (3600) so the capital badge sits on top of everything.
        private const int RenderQueue = 3660;
        private const string StarTexPath = "RegionsAndSocieties/CapitalStar";

        public override Vector3 Position => Vector3.zero;
        protected override Quaternion Rotation => Quaternion.identity;

        // Capitals shift when settlements are added/removed (which also re-derives protection ranks);
        // the density cache version bumps on exactly those events, so it is the right regen signal.
        private static int CurrentVersion => PopulationDensityUtility.CacheVersion;

        public override bool ShouldRegenerate => !built || builtVersion != CurrentVersion;

        public override IEnumerable Regenerate()
        {
            foreach (object item in base.Regenerate())
            {
                yield return item;
            }

            bool ok = false;
            try
            {
                ok = BuildMarkers();
            }
            catch (Exception ex)
            {
                Log.Warning($"[RegionsAndSocieties] Capital marker build failed: {ex}");
                ok = true;   // don't spin forever on a hard error
            }
            if (ok)
            {
                built = true;
                builtVersion = CurrentVersion;
            }
            FinalizeMesh(MeshParts.All);
        }

        public override void Render()
        {
            if (!WorldObjectIntegrationSettings.SettlementTiersActive) return;
            try
            {
                base.Render();
            }
            catch (Exception ex)
            {
                if (!loggedRenderError)
                {
                    loggedRenderError = true;
                    Log.Warning($"[RegionsAndSocieties] Capital marker render error: {ex.Message}");
                }
            }
        }

        private bool BuildMarkers()
        {
            if (Find.World == null || Find.WorldObjects == null) return false;
            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            if (mgr?.Provinces == null || mgr.Provinces.Count == 0) return false;   // not ready — keep retrying

            var seen = new HashSet<Faction>();
            List<WorldObject> all = Find.WorldObjects.AllWorldObjects;
            int drawn = 0;

            for (int i = 0; i < all.Count; i++)
            {
                WorldObject o = all[i];
                if (o == null || o.Faction == null) continue;
                if (WorldObjectClassifier.Classify(o) != WorldObjectKind.Settlement) continue;
                if (!seen.Add(o.Faction)) continue;

                WorldObject capital = SettlementSizeUtility.CapitalOf(o.Faction);
                if (capital == null) continue;

                Material mat = MaterialFor(o.Faction.Color);
                LayerSubMesh subMesh = GetSubMesh(mat);
                TileUtilities.DrawTile(subMesh, capital.Tile.tileId);
                drawn++;
            }

            return drawn > 0 || seen.Count > 0;   // built even if nobody has a capital yet, so we stop retrying
        }

        private static Material MaterialFor(Color factionColor)
        {
            Shader shader = ShaderDatabase.WorldOverlayTransparent ?? ShaderDatabase.MetaOverlay;
            var tint = new Color(factionColor.r, factionColor.g, factionColor.b, 1f);
            return MaterialPool.MatFrom(StarTexPath, shader, tint, RenderQueue);
        }
    }
}
