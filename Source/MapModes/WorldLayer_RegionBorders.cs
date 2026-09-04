using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MapModeFramework;
using RimWorld;
using RimWorld.Planet;
using RegionsAndSocieties.Placement;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties
{
    /// <summary>
    /// Auto-discovered world draw layer that renders the global region-border overlay (#53): the outline
    /// of every land province, coloured by its owning faction (white when unclaimed), drawn on top of
    /// whatever map mode is active. RimWorld instantiates every <see cref="WorldDrawLayer"/> subclass
    /// and renders it each frame, so no registration is needed. Geometry is built as submeshes (one per
    /// colour) the same way Map Mode Framework outlines its own regions — the proven path — and the base
    /// Render draws them; the overlay is hidden simply by skipping that Render when the toggle is off.
    /// </summary>
    public class WorldLayer_RegionBorders : WorldDrawLayer
    {
        private int builtVersion = -1;
        private bool built;
        private static bool loggedCtor;
        private static bool loggedRegen;
        private const float Lift = 0.015f;   // raise above the surface so lines beat z-fighting with terrain
        private const float Width = 0.6f;   // line thickness (inset distance toward the tile centre)

        private static readonly Color Unclaimed = new Color(1f, 1f, 1f, 0.85f);

        public WorldLayer_RegionBorders()
        {
            if (!loggedCtor)
            {
                loggedCtor = true;
                Log.Message("[RegionsAndSocieties] WorldLayer_RegionBorders constructed (auto-discovered).");
            }
        }

        // Retry every frame until the mesh is actually built (provinces may not exist the first time the
        // world layers regenerate), then only rebuild when the layout version changes.
        public override bool ShouldRegenerate => !built || builtVersion != UI.RegionBorderOverlay.Version;

        public override IEnumerable Regenerate()
        {
            foreach (object item in base.Regenerate())
            {
                yield return item;
            }
            if (!loggedRegen)
            {
                loggedRegen = true;
                Log.Message("[RegionsAndSocieties] WorldLayer_RegionBorders.Regenerate invoked.");
            }
            bool ok = false;
            try
            {
                ok = BuildBorders();
            }
            catch (Exception ex)
            {
                Log.Warning($"[RegionsAndSocieties] Region border overlay build failed: {ex}");
                ok = true;   // don't spin forever on a hard error
            }
            if (ok)
            {
                built = true;
                builtVersion = UI.RegionBorderOverlay.Version;
            }
            FinalizeMesh(MeshParts.All);
        }

        // Registered as a GLOBAL draw layer, but this class extends the surface WorldDrawLayer whose
        // Position/Rotation getters dereference per-planet-layer state a global layer never gets (it
        // NREs in WorldDrawLayer.get_Position). The border mesh is already built in absolute world
        // space, so pin the layer transform to origin/identity to bypass that surface state.
        public override Vector3 Position => Vector3.zero;
        protected override Quaternion Rotation => Quaternion.identity;

        // A global draw layer carries a null planetLayer by design, which NREs anything that dereferences
        // it — notably Layered Atmosphere and Orbit's WorldDrawLayer.Visible/Raycastable postfixes, which
        // read planetLayer.Def and, unguarded, take down the whole world-render loop (a black world). Pin
        // this global overlay to the root surface layer the first time visibility is queried, so those
        // reads are null-safe. The mesh is absolute-world-space and the Position/Rotation overrides above
        // keep it pinned regardless, so the association only decides which layer view it shows on — the
        // surface, which is where region borders belong.
        public override bool Visible
        {
            get
            {
                if (planetLayer == null && Find.WorldGrid != null) planetLayer = Find.WorldGrid.Surface;
                return base.Visible;
            }
        }

        private static bool loggedRenderError;

        public override void Render()
        {
            if (!UI.RegionBorderOverlay.Enabled)
            {
                return;
            }
            try
            {
                base.Render();
            }
            catch (Exception ex)
            {
                if (!loggedRenderError)
                {
                    loggedRenderError = true;
                    Log.Warning($"[RegionsAndSocieties] Region border overlay render error: {ex.Message}");
                }
            }
        }

        private bool BuildBorders()
        {
            if (Find.World == null)
            {
                return false;
            }
            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            if (mgr == null)
            {
                return false;
            }
            var provinces = mgr.Provinces;   // triggers generation if needed
            if (provinces == null || provinces.Count == 0)
            {
                return false;   // not ready yet — ShouldRegenerate keeps retrying
            }

            // Ensure ownership is current before colouring by it (#72). The overlay draws over ANY map
            // mode, so unlike the Territory/Geographic modes nothing else guarantees a recompute has run
            // — without this the very first build reads null ownershipData and paints every seam white.
            // The call is gated internally (epoch/faction-count), so it is cheap when nothing changed.
            mgr.RecalculateProvinceOwners();

            WorldGrid grid = Find.WorldGrid;
            var neighbors = new List<PlanetTile>();
            int edges = 0;

            for (int p = 0; p < provinces.Count; p++)
            {
                GeographicProvince province = provinces[p];
                if (province.provinceType != ProvinceType.Land || province.tiles == null || province.tiles.Count == 0)
                {
                    continue;
                }

                int pid = province.id;
                // Resolve the border style once per region (#72): a >50% owner paints solid, a contested
                // region alternates its two claimants' colours, and a lone sub-50% claim alternates the
                // claimant's colour with white. GetSubMesh caches one submesh per colour (shared across
                // regions), so alternating just distributes this region's edges between two colour buckets.
                BorderStyle style = StyleFor(province);
                LayerSubMesh meshA = GetSubMesh(UI.RegionBorderOverlay.MaterialFor(style.a));
                LayerSubMesh meshB = style.alternate ? GetSubMesh(UI.RegionBorderOverlay.MaterialFor(style.b)) : meshA;
                int edgeParity = 0;

                List<int> tiles = province.tiles;
                for (int i = 0; i < tiles.Count; i++)
                {
                    int t = tiles[i];
                    Vector3 center = grid.GetTileCenter(t);
                    neighbors.Clear();
                    grid.GetTileNeighbors(t, neighbors);
                    for (int k = 0; k < neighbors.Count; k++)
                    {
                        if (mgr.GetProvinceId(neighbors[k].tileId) == pid)
                        {
                            continue;   // interior edge, not a border
                        }
                        List<Vector3> shared = TileUtilities.GetSharedVertices(t, neighbors[k].tileId);
                        if (shared.Count < 2)
                        {
                            continue;
                        }
                        // Alternate colour buckets per consecutive border edge, so the seam reads as a
                        // two-colour dashed line when the region is contested or only loosely held.
                        LayerSubMesh target = (edgeParity++ & 1) == 0 ? meshA : meshB;
                        AddEdge(target, shared[0], shared[1], center);
                        edges++;
                    }
                }
            }

            Log.Message($"[RegionsAndSocieties] Region border overlay built: {edges} edge lines across {provinces.Count} provinces.");
            return true;
        }

        /// <summary>How a region's border reads at a glance (#72), used by both the overlay and its
        /// debug report so the two never disagree.</summary>
        internal enum BorderStyleKind { Unclaimed, SolidOwner, Contested, LooseClaim }

        /// <summary>The one or two colours a region's border is painted with, and whether they alternate.</summary>
        internal struct BorderStyle
        {
            public BorderStyleKind kind;
            public Color a;              // primary colour (also the sole colour when not alternating)
            public Color b;              // secondary colour, alternated with a
            public bool alternate;
            public Faction primary;      // strongest claimant (null when unclaimed)
            public Faction secondary;    // second claimant (contested only)
        }

        /// <summary>
        /// Decide a province's border style from the same legitimate-claim set (&gt;=30%) the Territories
        /// fill uses, so overlay and fill never disagree (#72):
        ///   • a claimant over 50% (<see cref="PlacementRules.LooseOwnershipThreshold"/>) — SOLID in its colour;
        ///   • two or more legitimate claims, none dominant — CONTESTED, alternating the top two claimants;
        ///   • a single legitimate claim under 50% — a LOOSE hold, alternating the claimant's colour with white;
        ///   • no legitimate claim — unclaimed white.
        /// The old code always painted <c>claims[0]</c> solid, so a 30% loose claim and a 90% owner looked
        /// identical and a contested seam showed only one side.
        /// </summary>
        internal static BorderStyle StyleFor(GeographicProvince province)
        {
            var claims = RegionalDomainUtility.LegitimateClaimsOrdered(province.ownershipData);
            if (claims.Count == 0)
            {
                return new BorderStyle { kind = BorderStyleKind.Unclaimed, a = Unclaimed, b = Unclaimed, alternate = false };
            }

            Faction top = claims[0].faction;
            Color topColor = Solid(top.Color);

            // A clear majority owner (>50%): solid, regardless of any minor second claim.
            if (claims[0].TotalScore >= PlacementRules.LooseOwnershipThreshold)
            {
                return new BorderStyle { kind = BorderStyleKind.SolidOwner, a = topColor, b = topColor, alternate = false, primary = top };
            }

            // Two or more legitimate claims, none over 50%: contested — alternate the top two claimants.
            if (claims.Count >= 2)
            {
                Faction second = claims[1].faction;
                return new BorderStyle { kind = BorderStyleKind.Contested, a = topColor, b = Solid(second.Color), alternate = true, primary = top, secondary = second };
            }

            // A single legitimate claim under 50%: a loose hold — alternate the claimant's colour with white.
            return new BorderStyle { kind = BorderStyleKind.LooseClaim, a = topColor, b = Unclaimed, alternate = true, primary = top };
        }

        private static Color Solid(Color c) => new Color(c.r, c.g, c.b, 0.9f);

        private void AddEdge(LayerSubMesh subMesh, Vector3 a, Vector3 b, Vector3 tileCenter)
        {
            // Inset the two edge VERTICES toward the tile centre (not a per-edge perpendicular): two
            // boundary edges that meet at a hex corner then share the same inset point, so the strip
            // stays continuous around the tile instead of breaking into dashes. This also keeps the
            // line inside the region's hex, which is the point of the double.
            float inset = 0.05f * Width;
            Vector3 aIn = a + (tileCenter - a).normalized * inset;
            Vector3 bIn = b + (tileCenter - b).normalized * inset;

            a += a.normalized * Lift;
            b += b.normalized * Lift;
            aIn += aIn.normalized * Lift;
            bIn += bIn.normalized * Lift;

            int n = subMesh.verts.Count;
            subMesh.verts.Add(a);
            subMesh.verts.Add(b);
            subMesh.verts.Add(aIn);
            subMesh.verts.Add(bIn);
            // Both windings, so the strip renders regardless of which way the face points (no culling
            // gaps).
            subMesh.tris.Add(n);
            subMesh.tris.Add(n + 1);
            subMesh.tris.Add(n + 2);
            subMesh.tris.Add(n + 2);
            subMesh.tris.Add(n + 1);
            subMesh.tris.Add(n + 3);
            subMesh.tris.Add(n);
            subMesh.tris.Add(n + 2);
            subMesh.tris.Add(n + 1);
            subMesh.tris.Add(n + 1);
            subMesh.tris.Add(n + 2);
            subMesh.tris.Add(n + 3);
        }
    }
}
