using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Content;
using UnityEngine;

namespace GlimmerGrove.Homestead
{
    /// <summary>
    /// Reads the grove catalog once and publishes it. The one thing screens call.
    ///
    /// <para>
    /// A body is read on entering the feature and would ordinarily be dropped on leaving,
    /// exactly as a chapter is. This keeps the <em>catalog</em> — a few kilobytes of parsed
    /// structs — and drops only the <em>art</em>, which is where the megabytes are. That is
    /// a deliberate departure from <c>ChapterResidency</c> and it is worth naming: a chapter
    /// body carries every board's grid and there are forty of them, while there is exactly
    /// one grove, and re-reading it would put a file read in front of the shop, the picker
    /// and the hub badge — three navigations a player makes in a row.
    /// </para>
    /// <para>
    /// Concurrent callers share one load. The screen and whatever else asks are both likely
    /// to fire in the same frame, and two readers of one file is one wasted parse plus a
    /// window where the second publishes over the first.
    /// </para>
    /// </summary>
    public static class HomesteadService
    {
        static Task<HomesteadLoadResult> _running;

        /// <summary>The last read's problems, for the Editor and for diagnostics.</summary>
        public static IReadOnlyList<string> Problems { get; private set; } = Array.Empty<string>();

        /// <summary>
        /// True when a read has completed and produced nothing usable — no plots at all.
        ///
        /// Distinct from "not loaded yet", which is what <c>HomesteadCatalog.IsLoaded</c>
        /// answers, because the two render different screens: one is a spinner and the other
        /// is a sentence explaining that the grove is not available. A screen that shows the
        /// spinner for a failure spins forever.
        /// </summary>
        public static bool IsUnavailable => HomesteadCatalog.IsLoaded && HomesteadCatalog.Current.PlotCount == 0;

        /// <summary>
        /// Loads and publishes the catalog if it has not been already. Returns immediately
        /// once loaded, so a screen may call it in <c>Build</c> without checking.
        /// </summary>
        public static Task<HomesteadLoadResult> EnsureAsync(CancellationToken cancellation = default)
        {
            if (HomesteadCatalog.IsLoaded)
                return Task.FromResult(new HomesteadLoadResult(HomesteadCatalog.Current, Problems));

            return _running ?? (_running = LoadAsync(cancellation));
        }

        static async Task<HomesteadLoadResult> LoadAsync(CancellationToken cancellation)
        {
            try
            {
                var source = ContentBootstrap.LocalSource;
                if (source == null)
                {
                    // Boot has not run. Publishing an empty catalog would make IsLoaded true
                    // and permanently latch the "no grove" screen, so this stays unloaded and
                    // the next call tries again.
                    Debug.LogWarning("[Grove] asked for the catalog before content was ready");
                    return new HomesteadLoadResult(null, Problems);
                }

                var result = await new HomesteadLoader(source).LoadAsync(cancellation);

                Problems = result.Problems;
                foreach (var problem in result.Problems) Debug.LogWarning("[Grove] " + problem);

                // Published even when the read failed, so IsLoaded becomes true and the screen
                // can say what happened instead of spinning. Empty is a legible state; a
                // permanent spinner is not.
                HomesteadCatalog.Publish(result.Catalog);
                return result;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                HomesteadCatalog.Publish(HomesteadCatalog.Empty);
                return new HomesteadLoadResult(null, new[] { e.Message });
            }
            finally
            {
                _running = null;
            }
        }

        /// <summary>Test seam: forgets the catalog, as a fresh launch would.</summary>
        internal static void ResetForTests()
        {
            _running = null;
            Problems = Array.Empty<string>();
            HomesteadCatalog.ResetForTests();
        }
    }
}
