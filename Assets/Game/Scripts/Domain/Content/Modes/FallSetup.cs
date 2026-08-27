using System.Collections.Generic;
using GlimmerGrove.Modes;
using UnityEngine;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// Searches a well for its par, once, and remembers the answer.
    ///
    /// <para>
    /// <b>Why the search runs on the phone at all.</b> Par decides both star lines and the
    /// supply a run is dealt, so it has to be known before the first mote falls — and it may not
    /// be authored, because a typed par drifts from the board it claims to describe and the
    /// drift has no symptom (invariant 5). The alternative is writing the number into the
    /// chapter body at authoring time, which is the same typed par with an extra step in front
    /// of it: nothing on the device would then be able to tell a stale one from a fresh one.
    /// </para>
    /// <para>
    /// <b>Why the cache is on the level id rather than the layout.</b> A chapter body is evicted
    /// when the player leaves the chapter and re-read when they come back (invariant 4a), so
    /// without this every trip in and out of the Deep Well would re-search all ten of its levels.
    /// A level id is permanent and the board behind it is frozen once shipped, so the answer
    /// cannot go stale within a session — and a content push that changed a board would arrive
    /// as a new process, because content is fetched at boot.
    /// </para>
    /// <para>
    /// <b>What happens when a search fails is the interesting half.</b> Nothing here can prove a
    /// board on a device that the build gate did not already prove on a build machine, so
    /// reaching the fallback means broken content shipped. The fallback is chosen to be
    /// impossible to lose to rather than to be accurate: par becomes the procession's own
    /// length, which puts three stars, two stars and the supply all above it. A player meets a
    /// generously graded level instead of an unwinnable one, and the log names the level.
    /// </para>
    /// </summary>
    public static class FallSetup
    {
        static readonly Dictionary<string, int> _par = new Dictionary<string, int>();

        /// <summary>
        /// The fewest drops that empty this well, cached on the level id.
        ///
        /// Never returns below one: <c>LevelTuning</c> clamps par to one anyway, and answering
        /// zero here would put every threshold on the same number and make the star ladder
        /// report a complaint about board size rather than about play.
        /// </summary>
        public static int Par(string levelId, FallLayout layout)
        {
            if (layout == null) return 1;

            string key = levelId ?? string.Empty;
            if (key.Length > 0 && _par.TryGetValue(key, out int cached)) return cached;

            int par = FallSolver.Par(layout);

            if (par < 1)
            {
                par = layout.Deal.Count;
                Debug.LogError(
                    $"[Lightfall] '{key}' could not be proved solvable inside " +
                    $"{FallSolver.NodeBudget} positions, which the build gate should have " +
                    $"refused. Grading it against its procession length ({par}) so it stays " +
                    "winnable; run Validate Content.");
            }

            if (key.Length > 0) _par[key] = par;
            return par;
        }

        /// <summary>
        /// Forgets everything. For the test suite and for the Editor's content refresh, which
        /// rebuilds the catalog inside one process — the only two places a level id can come to
        /// name a different board.
        /// </summary>
        public static void Forget() => _par.Clear();
    }
}
