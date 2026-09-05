using System.Collections.Generic;

namespace RegionsAndSocieties.Placement
{
    /// <summary>Outcome of asking <see cref="PlacementHintCache"/> for a tile's placement hint.</summary>
    public enum HintLookup
    {
        /// <summary>A fresh hint for this tile is cached; use it.</summary>
        Hit,

        /// <summary>
        /// Nothing fresh is cached, the tile has stayed selected long enough, and nothing else is busy:
        /// evaluate now and <see cref="PlacementHintCache.Store"/> the answer.
        /// </summary>
        Evaluate,

        /// <summary>Nothing fresh is cached and this is not the moment to compute one: show nothing, ask again next frame.</summary>
        Wait
    }

    /// <summary>
    /// Decides WHEN the world inspect pane may run a placement evaluation for the selected tile, and
    /// remembers the answers it got (#44, #45).
    ///
    /// <para>The inspect string is rebuilt every GUI frame, and a placement evaluation walks every
    /// holding and flood-fills the world grid. Doing that on the first frame a tile is selected made
    /// rapid tile-hopping pay the full cost on every click (#44) and, with Map Preview generating on a
    /// worker thread, nested two flood fills on the one shared filler (#45). Three rules fix that
    /// without weakening the answer:</para>
    /// <list type="bullet">
    ///   <item><b>Dwell.</b> A tile must stay selected for <see cref="DwellSeconds"/> of real time before
    ///   it is evaluated. Hopping across tiles never evaluates; resting on one does, a third of a
    ///   second later.</item>
    ///   <item><b>Busy.</b> While the caller reports something else is using the world flood filler
    ///   (Map Preview), the answer is deferred, not skipped — it arrives when the preview is done.</item>
    ///   <item><b>Memory.</b> Answers are kept in a small least-recently-used cache keyed by tile, valid
    ///   while the world-object set is unchanged and at most <see cref="RefreshIntervalTicks"/> game
    ///   ticks old. Game ticks do not advance while paused, so on a paused world map (the normal
    ///   settling situation) a revisited tile answers instantly and never re-evaluates.</item>
    /// </list>
    /// <para>Pure by design — real time, game tick, world version and busy state are all supplied by the
    /// caller — so the timing rules are covered by the placement test suite.</para>
    /// </summary>
    public sealed class PlacementHintCache
    {
        public const int DefaultCapacity = 64;
        public const float DefaultDwellSeconds = 0.35f;
        public const int DefaultRefreshIntervalTicks = 120;

        private struct Entry
        {
            public int worldVersion;
            public int gameTick;
            public string hint;
            public LinkedListNode<int> node;
        }

        private readonly int capacity;
        private readonly Dictionary<int, Entry> entries;
        private readonly LinkedList<int> order = new LinkedList<int>(); // most recently used at the front

        private int dwellTile = -1;
        private float dwellSince;

        public float DwellSeconds { get; }
        public int RefreshIntervalTicks { get; }
        public int Count { get { return entries.Count; } }

        public PlacementHintCache()
            : this(DefaultCapacity, DefaultDwellSeconds, DefaultRefreshIntervalTicks) { }

        public PlacementHintCache(int capacity, float dwellSeconds, int refreshIntervalTicks)
        {
            this.capacity = capacity < 1 ? 1 : capacity;
            DwellSeconds = dwellSeconds < 0f ? 0f : dwellSeconds;
            RefreshIntervalTicks = refreshIntervalTicks < 1 ? 1 : refreshIntervalTicks;
            entries = new Dictionary<int, Entry>(this.capacity);
        }

        /// <summary>
        /// Ask for the hint of the currently selected <paramref name="tile"/>.
        /// </summary>
        /// <param name="tile">Selected tile id.</param>
        /// <param name="worldVersion">Anything that changes when the world-object set changes (e.g. its count).</param>
        /// <param name="gameTick">Current game tick; does not advance while paused.</param>
        /// <param name="nowSeconds">Real time in seconds; advances while paused.</param>
        /// <param name="busy">True while another user of the world flood filler is running (Map Preview).</param>
        /// <param name="hint">The cached hint on <see cref="HintLookup.Hit"/> (may be null for an allowed tile); null otherwise.</param>
        public HintLookup Lookup(int tile, int worldVersion, int gameTick, float nowSeconds, bool busy, out string hint)
        {
            if (tile != dwellTile)
            {
                dwellTile = tile;
                dwellSince = nowSeconds;
            }

            Entry e;
            if (entries.TryGetValue(tile, out e))
            {
                if (IsFresh(e, worldVersion, gameTick))
                {
                    order.Remove(e.node);
                    order.AddFirst(e.node);
                    hint = e.hint;
                    return HintLookup.Hit;
                }

                order.Remove(e.node);
                entries.Remove(tile);
            }

            hint = null;
            if (busy) return HintLookup.Wait;
            if (nowSeconds - dwellSince < DwellSeconds) return HintLookup.Wait;
            return HintLookup.Evaluate;
        }

        /// <summary>Remember the evaluated hint for a tile. A null or empty hint (the tile is allowed) is cached too.</summary>
        public void Store(int tile, int worldVersion, int gameTick, string hint)
        {
            Entry existing;
            if (entries.TryGetValue(tile, out existing))
            {
                order.Remove(existing.node);
                entries.Remove(tile);
            }

            while (entries.Count >= capacity && order.Last != null)
            {
                int evicted = order.Last.Value;
                order.RemoveLast();
                entries.Remove(evicted);
            }

            var node = order.AddFirst(tile);
            entries[tile] = new Entry { worldVersion = worldVersion, gameTick = gameTick, hint = hint, node = node };
        }

        /// <summary>True when a fresh hint for the tile is cached (no dwell-tracking side effects).</summary>
        public bool Contains(int tile, int worldVersion, int gameTick)
        {
            Entry e;
            return entries.TryGetValue(tile, out e) && IsFresh(e, worldVersion, gameTick);
        }

        public void Clear()
        {
            entries.Clear();
            order.Clear();
            dwellTile = -1;
            dwellSince = 0f;
        }

        private bool IsFresh(Entry e, int worldVersion, int gameTick)
        {
            if (e.worldVersion != worldVersion) return false;
            // A tick that went backwards means a different game was loaded under us: stale.
            if (gameTick < e.gameTick) return false;
            return gameTick - e.gameTick < RefreshIntervalTicks;
        }
    }
}
