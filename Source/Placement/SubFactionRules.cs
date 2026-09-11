using System;
using System.Collections.Generic;

namespace RegionsAndSocieties.Placement
{
    /// <summary>A point on the world sphere (a body or section centroid), pure so grouping is testable.</summary>
    public struct GeoPoint
    {
        public double X, Y, Z;
        public GeoPoint(double x, double y, double z) { X = x; Y = y; Z = z; }
    }

    /// <summary>
    /// When a scattered faction's clusters become separate kin factions (#57). A faction once spanned the
    /// map; newer factions carved it up, so its far-flung clusters read as a north tribe and a south tribe
    /// — loosely related, no longer one polity. The <b>fractious</b> kinds split: pirates (and wasters),
    /// tribes, and rough unions. Cohesive polities — the Empire and the spacer/trader civilisations — hold
    /// together across distance and stay one faction. Pure: geometry and gates in, groupings and labels out.
    /// </summary>
    public static class SubFactionRules
    {
        /// <summary>Most sections one faction may split into (parent included): the owner's "2–3".</summary>
        public const int MaxSections = 3;

        /// <summary>
        /// The likely number of kin factions a faction forms, from its region count and cluster size: each
        /// contiguous body holds up to <paramref name="clusterSize"/> regions, so the territory falls into
        /// about ceil(regions / clusterSize) bodies, and (with kin on) each body is a kin faction. An
        /// unbounded cluster (or a count that fits one body) means one faction — no kin. Pure, so the dialog
        /// and worldgen agree on the arithmetic.
        /// </summary>
        public static int EstimateKinCount(int regions, int clusterSize)
        {
            if (regions <= 0) return 1;
            int cap = clusterSize <= 0 ? regions : clusterSize;
            if (cap >= regions) return 1;
            return (regions + cap - 1) / cap;   // ceil(regions / cap)
        }

        /// <summary>
        /// The number of kin factions a faction forms, combining the two clustering knobs (the owner's
        /// model): <paramref name="numberOfClusters"/> is the equal-division / max-kin cap, and
        /// <paramref name="minClusterSize"/> is the minimum regions a cluster must have to become its own kin
        /// faction. So kin = min(numberOfClusters, floor(regions / minClusterSize)), never below one. A
        /// <paramref name="numberOfClusters"/> of 0 means "no cap" → one cluster per <paramref name="minClusterSize"/>
        /// regions (with minClusterSize 1 that is one faction per region, the warned-about extreme).
        /// </summary>
        public static int PlannedKinCount(int regions, int numberOfClusters, int minClusterSize)
        {
            if (regions <= 0) return 1;
            int bySize = minClusterSize >= 1 ? regions / minClusterSize : regions;   // floor; 0/neg = no size clamp
            if (bySize < 1) bySize = 1;
            int cap = numberOfClusters <= 0 ? bySize : numberOfClusters;             // 0 = uncapped
            int k = bySize < cap ? bySize : cap;
            return k < 1 ? 1 : k;
        }

        /// <summary>
        /// Compose a sub-faction's name from a directional <paramref name="label"/> (North/South/East/West/
        /// Central, possibly empty) and the parent's <paramref name="baseName"/>. A bare prefix reads badly
        /// when the base already opens with an article — "West The Abene Tribe" — so the direction is slipped
        /// in AFTER a leading "The"/"A"/"An": "The West Abene Tribe". Any other base just takes the prefix:
        /// "West Toban Union". An empty label returns the base unchanged. Pure and unit-tested.
        /// </summary>
        public static string ComposeName(string label, string baseName)
        {
            baseName = baseName ?? string.Empty;
            if (string.IsNullOrEmpty(label)) return baseName;

            foreach (var article in Articles)
            {
                if (baseName.Length > article.Length &&
                    baseName.StartsWith(article, StringComparison.OrdinalIgnoreCase) &&
                    char.IsWhiteSpace(baseName[article.Length]))
                {
                    string lead = baseName.Substring(0, article.Length);          // preserve original casing
                    string rest = baseName.Substring(article.Length).TrimStart();
                    return lead + " " + label + " " + rest;
                }
            }
            return label + " " + baseName;
        }

        private static readonly string[] Articles = { "The", "A", "An" };
        /// <summary>Goodwill set between the kin factions a split produces — friendly, not merged.</summary>
        public const int LooseKinGoodwill = 60;

        /// <summary>The faction kinds that fracture when scattered: pirates, tribes, rough unions. The
        /// Empire and the unbounded civilisations stay whole.</summary>
        public static bool IsSplittableKind(FactionKind kind)
        {
            return kind == FactionKind.Pirate || kind == FactionKind.Tribe || kind == FactionKind.RoughUnion;
        }

        /// <summary>
        /// The default regional-kin setting for a faction (#63/#64). Kin (bodies becoming SEPARATE factions)
        /// is a different idea from clustering (territory scattering into bodies) — a kin-off faction still
        /// clusters, it just stays one faction. Defaults:
        /// <list type="bullet">
        /// <item>the Empire never forms kin — it is one highly-fragmented polity (and it is <see cref="KinLocked"/>);</item>
        /// <item>spacer-tech factions default OFF — pod/shuttle mobility means distance does not fracture them
        /// into separate polities (a player can still opt in); they continue to cluster;</item>
        /// <item>otherwise the fractious low-tech kinds (pirate / tribe / rough union) form kin by default.</item>
        /// </list>
        /// Pure: kind, tech and the Empire flag in, the default out — so the dialog and worldgen agree.
        /// </summary>
        public static bool KinDefault(FactionKind kind, int techLevel, bool isEmpire)
        {
            if (isEmpire) return false;
            if (techLevel >= ClusteringRules.TechSpacer) return false;   // #64
            return IsSplittableKind(kind);
        }

        /// <summary>Whether regional kin is LOCKED off (never player-overridable): the Empire, a single
        /// highly-fragmented polity that many mods base off, must never split into kin factions (#63).</summary>
        public static bool KinLocked(bool isEmpire) => isEmpire;

        /// <summary>A faction splits when it is a fractious kind, has a finite cluster cap, and its
        /// settlements form two or more separate bodies.</summary>
        public static bool ShouldSplit(FactionKind kind, int clusterCap, int bodyCount)
        {
            return bodyCount >= 2
                && !ClusteringRules.IsUnbounded(clusterCap)
                && IsSplittableKind(kind);
        }

        /// <summary>How many sections a faction with this many bodies splits into: at least 1, at most
        /// <paramref name="maxSections"/>, never more than the bodies it has.</summary>
        public static int SectionCount(int bodyCount, int maxSections)
        {
            int k = Math.Min(maxSections, bodyCount);
            return k < 1 ? 1 : k;
        }

        /// <summary>
        /// Assign each body to one of <paramref name="sections"/> geographic sections — farthest-point
        /// seeding (the two, then three, most-separated bodies as seeds), then each body to its nearest
        /// seed. Deterministic: ties go to the lower index. Returns a section index per body.
        /// </summary>
        public static int[] AssignSections(IList<GeoPoint> bodies, int sections)
        {
            int n = bodies?.Count ?? 0;
            var result = new int[n];
            int k = Math.Min(sections, n);
            if (k <= 1) return result;   // all zeros

            var seeds = new List<int>(k);

            // Seed 0: the body farthest from the overall centroid.
            GeoPoint c = Centroid(bodies);
            seeds.Add(Farthest(bodies, i => Dist2(bodies[i], c)));
            // Seed 1: farthest from seed 0.
            seeds.Add(Farthest(bodies, i => Dist2(bodies[i], bodies[seeds[0]])));
            // Seed 2 (if wanted): farthest from its nearest existing seed.
            if (k >= 3)
                seeds.Add(Farthest(bodies, i => MinDistToSeeds(bodies, i, seeds)));

            for (int i = 0; i < n; i++)
            {
                int best = 0;
                double bestD = Dist2(bodies[i], bodies[seeds[0]]);
                for (int s = 1; s < seeds.Count; s++)
                {
                    double d = Dist2(bodies[i], bodies[seeds[s]]);
                    if (d < bestD) { bestD = d; best = s; }
                }
                result[i] = best;
            }
            return result;
        }

        /// <summary>
        /// A direction label per section, from the geography of the section centroids. When the sections
        /// spread mainly north–south the labels are South / North (and Central for three); when they
        /// spread mainly east–west, West / East / Central. Labels are aligned to section index.
        /// </summary>
        public static string[] SectionLabels(IList<GeoPoint> sectionCentroids)
        {
            int k = sectionCentroids?.Count ?? 0;
            var labels = new string[k];
            if (k <= 0) return labels;
            if (k == 1) { labels[0] = ""; return labels; }

            // Dominant spread axis: north–south (Y) versus the wider of the two longitudinal axes (X, Z).
            double ry = Range(sectionCentroids, p => p.Y);
            double rx = Range(sectionCentroids, p => p.X);
            double rz = Range(sectionCentroids, p => p.Z);
            bool northSouth = ry >= rx && ry >= rz;
            Func<GeoPoint, double> axis = northSouth ? (Func<GeoPoint, double>)(p => p.Y) : (rx >= rz ? (p => p.X) : (p => p.Z));

            // Section indices ordered along the axis, low to high.
            var order = new List<int>();
            for (int i = 0; i < k; i++) order.Add(i);
            order.Sort((a, b) => { int c = axis(sectionCentroids[a]).CompareTo(axis(sectionCentroids[b])); return c != 0 ? c : a.CompareTo(b); });

            string[] ordered = OrderedLabels(k, northSouth);
            for (int rank = 0; rank < k; rank++) labels[order[rank]] = ordered[rank];
            return labels;
        }

        /// <summary>
        /// A distinct compass label per body for the one-faction-per-cluster kin split (#47 follow-up). The
        /// body that keeps the parent faction (<paramref name="keepIndex"/>) gets an empty label — the clean
        /// base name — and every other body is named by its compass bearing from the faction's overall
        /// centroid (North / Northeast / … / Northwest), with a numeric suffix when two bodies share a
        /// bearing ("North", "North 2"). Reads far better than the old "Central 1 … Central 15" for a faction
        /// that scatters into many clusters. Pure and unit-tested.
        /// </summary>
        public static string[] BodyLabels(IList<GeoPoint> centroids, int keepIndex)
        {
            int n = centroids?.Count ?? 0;
            var labels = new string[n];
            if (n == 0) return labels;

            double cx = 0, cy = 0, cz = 0;
            foreach (var p in centroids) { cx += p.X; cy += p.Y; cz += p.Z; }
            cx /= n; cy /= n; cz /= n;

            var used = new Dictionary<string, int>();
            for (int i = 0; i < n; i++)
            {
                if (i == keepIndex) { labels[i] = ""; continue; }
                string dir = Compass(centroids[i].X - cx, centroids[i].Y - cy);
                if (!used.ContainsKey(dir)) { used[dir] = 1; labels[i] = dir; }
                else { used[dir]++; labels[i] = dir + " " + used[dir]; }
            }
            return labels;
        }

        /// <summary>Eight-point compass of a horizontal offset: easting = ΔX, northing = ΔY. A near-zero
        /// offset still resolves to a stable bearing (atan2(0,0)=0 → East), which the numeric dedup then
        /// disambiguates.</summary>
        private static string Compass(double east, double north)
        {
            string[] names = { "East", "Northeast", "North", "Northwest", "West", "Southwest", "South", "Southeast" };
            double ang = Math.Atan2(north, east) * 180.0 / Math.PI;   // 0 = East, 90 = North
            int idx = (int)Math.Round(ang / 45.0);
            idx = ((idx % 8) + 8) % 8;
            return names[idx];
        }

        /// <summary>The ordered label list, low axis value to high.</summary>
        private static string[] OrderedLabels(int k, bool northSouth)
        {
            string low = northSouth ? "South" : "West";
            string high = northSouth ? "North" : "East";
            if (k <= 2) return new[] { low, high };
            if (k == 3) return new[] { low, "Central", high };
            // More than three (not used by MaxSections, kept total): number the middle ones.
            var arr = new string[k];
            arr[0] = low; arr[k - 1] = high;
            for (int i = 1; i < k - 1; i++) arr[i] = "Central " + i;
            return arr;
        }

        private static GeoPoint Centroid(IList<GeoPoint> pts)
        {
            double x = 0, y = 0, z = 0;
            foreach (var p in pts) { x += p.X; y += p.Y; z += p.Z; }
            int n = pts.Count > 0 ? pts.Count : 1;
            return new GeoPoint(x / n, y / n, z / n);
        }

        private static int Farthest(IList<GeoPoint> pts, Func<int, double> score)
        {
            int best = 0; double bestD = double.NegativeInfinity;
            for (int i = 0; i < pts.Count; i++)
            {
                double d = score(i);
                if (d > bestD) { bestD = d; best = i; }
            }
            return best;
        }

        private static double MinDistToSeeds(IList<GeoPoint> pts, int i, List<int> seeds)
        {
            double m = double.PositiveInfinity;
            foreach (int s in seeds) { double d = Dist2(pts[i], pts[s]); if (d < m) m = d; }
            return m;
        }

        private static double Dist2(GeoPoint a, GeoPoint b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        private static double Range(IList<GeoPoint> pts, Func<GeoPoint, double> sel)
        {
            double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
            foreach (var p in pts) { double v = sel(p); if (v < lo) lo = v; if (v > hi) hi = v; }
            return hi - lo;
        }
    }
}
