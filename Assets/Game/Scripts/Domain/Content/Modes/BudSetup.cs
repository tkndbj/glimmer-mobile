using System.Collections.Generic;
using GlimmerGrove.Modes;
using UnityEngine;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// A thicket's par, searched once and remembered.
    ///
    /// <para>
    /// Asked through <c>LevelTuning</c>'s lazy resolver, so nothing pays for it until something
    /// actually needs par — which is the run screen and the validator, and never the map
    /// (invariant 26d).
    /// </para>
    /// <para>
    /// A thicket that cannot be proved is graded against its own cocoon count and reported
    /// loudly. It is the shape the build gate should already have refused, so the fallback exists
    /// to keep a shipped level <em>winnable</em> rather than to make an unprovable one
    /// acceptable.
    /// </para>
    /// </summary>
    public static class BudSetup
    {
        static readonly Dictionary<string, int> _par = new Dictionary<string, int>();

        public static int Par(string levelId, BudLayout layout)
        {
            if (layout == null) return 1;

            string key = levelId ?? string.Empty;
            if (key.Length > 0 && _par.TryGetValue(key, out int cached)) return cached;

            int par = BudSolver.Par(layout);

            if (par < 1)
            {
                par = layout.Cocoons;
                Debug.LogError(
                    $"[Budburst] '{key}' could not be proved solvable inside " +
                    $"{BudSolver.NodeBudget} positions, which the build gate should have " +
                    $"refused. Grading it against the critters it holds ({par}) so it stays " +
                    "winnable; run Validate Content.");
            }

            if (key.Length > 0) _par[key] = par;
            return par;
        }

        /// <summary>
        /// Forgets everything. For the test suite and for the Editor's content refresh, which
        /// rebuilds the catalog inside one process - the only two places a level id can come to
        /// name a different thicket.
        /// </summary>
        public static void Forget() => _par.Clear();
    }
}
