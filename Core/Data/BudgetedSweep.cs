using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Wonderland.Core.Data
{
    /// <summary>
    /// A world-wide prefab round-robin that never takes more than a slice of one server frame.
    ///
    /// Up to 0.10.8 every background engine (vacuum, container rows, sort, production supply) advanced
    /// its PrefabSetScanner N chunks per interval and then visited every ZDO those chunks found in the
    /// same frame. A chunk is up to 400 non-empty sectors of ONE prefab, so a pass that landed on a
    /// common prefab ('piece_chest_wood') loaded, spatially queried and cache-drained every such chest
    /// in the world at once: 250-440 ms frames every 2 s (vacuum) and 120-140 ms every 5 s (rows) on
    /// the live 745k-ZDO world, players or not - the "stutter" the profiler was added to find.
    ///
    /// Here the operator's coverage rate is kept exactly (<see cref="Grant"/> hands the sweep the same
    /// N chunks per interval) but the visiting is spread over the frames that follow: each frame
    /// <see cref="Run"/> works until <c>budgetMs</c> is spent and stops, whatever is left waits. The
    /// queue holds ZDOIDs, not ZDOs: ZDOPool recycles a destroyed ZDO object for the next created one,
    /// so a reference kept across frames may silently become a different object. Each id is resolved
    /// again through ZDOMan when its turn comes and one that no longer exists is skipped.
    /// </summary>
    public sealed class BudgetedSweep
    {
        private readonly ZdoSpatialQuery.PrefabSetScanner _scanner;
        private readonly List<ZDO> _chunk = new List<ZDO>();
        private readonly Queue<ZDOID> _queue = new Queue<ZDOID>();
        private readonly Stopwatch _clock = new Stopwatch();
        private int _chunkAllowance;

        public BudgetedSweep(IEnumerable<string> prefabNames)
        {
            _scanner = new ZdoSpatialQuery.PrefabSetScanner(prefabNames);
        }

        public int TrackedPrefabCount => _scanner.TrackedPrefabCount;

        /// <summary>ZDOs found by the scanner and still waiting for their visit.</summary>
        public int Pending => _queue.Count;

        /// <summary>
        /// The interval's coverage: the sweep may advance the scanner this many more chunks before the
        /// next grant. Not cumulative - a grant the frame budget could not spend by the next interval is
        /// replaced, not added to, so a world too big for the budget is swept at the budget's pace rather
        /// than piling up an ever-growing debt.
        /// </summary>
        public void Grant(int chunks)
        {
            _chunkAllowance = Math.Max(0, chunks);
        }

        /// <summary>
        /// Visits queued ZDOs, refilling the queue from the scanner while chunks are granted, until
        /// <paramref name="budgetMs"/> of this frame is spent. The budget is checked after every visit and
        /// every chunk, so one visit (a container load) can overrun it by its own length and no more.
        /// Returns the number of ZDOs visited.
        /// </summary>
        public int Run(float budgetMs, Action<ZDO> visit)
        {
            if (ZDOMan.instance == null)
            {
                return 0;
            }

            long budgetTicks = (long)(Math.Max(0.05f, budgetMs) * Stopwatch.Frequency / 1000f);
            _clock.Restart();
            int visited = 0;
            while (true)
            {
                if (_queue.Count == 0)
                {
                    if (_chunkAllowance <= 0)
                    {
                        break;
                    }
                    _chunk.Clear();
                    _scanner.Advance(_chunk);
                    _chunkAllowance--;
                    for (int i = 0; i < _chunk.Count; i++)
                    {
                        _queue.Enqueue(_chunk[i].m_uid);
                    }
                    _chunk.Clear();
                    if (_queue.Count == 0)
                    {
                        if (_clock.ElapsedTicks >= budgetTicks)
                        {
                            break;
                        }
                        continue; // an empty chunk (a prefab with no instances in these sectors) - look further while the budget lasts
                    }
                }

                ZDO? zdo = ZDOMan.instance.GetZDO(_queue.Dequeue());
                if (zdo != null)
                {
                    visit(zdo);
                    visited++;
                }
                if (_clock.ElapsedTicks >= budgetTicks)
                {
                    break;
                }
            }
            return visited;
        }
    }
}
