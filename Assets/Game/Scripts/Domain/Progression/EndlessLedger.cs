using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Progression
{
    /// <summary>
    /// How far an endless run has ever got, per level.
    ///
    /// <para>
    /// <b>The one number an endless level leaves behind, and it is a floor.</b> Everything else
    /// about a run is already recorded by the machinery every other level uses — the heart, the
    /// chest count, the streak, the star ledger — and none of it fits a board that is never won.
    /// A wave count is not a grade: it is a high-water mark, so it is one monotonic integer per
    /// level id joined by <c>max</c>, which is invariant 14a's floor exactly and the only shape
    /// invariant 11b permits for a number two devices both write.
    /// </para>
    /// <para>
    /// <b>It pays nothing, deliberately.</b> Credits and XP derive from the star ledger and from
    /// nothing else (invariant 9), so an endless run buys a place on a board and a number on a map
    /// node. That is what makes it safe for the client to write it without the server being told:
    /// a forged wave moves a reading, never a balance — and the public half is clamped by
    /// <c>publishGrove</c> the way every public number in this game is (invariant 19a).
    /// </para>
    /// <para>
    /// <b>A level id is permanent and invariant 1 reaches this</b>, the way it reaches the star
    /// ledger: rows naming a level this build has never heard of are carried through untouched,
    /// so a best set on a newer build survives a trip through an older one.
    /// </para>
    /// </summary>
    public static class EndlessLedger
    {
        /// <summary>
        /// The most rows this will keep.
        ///
        /// A bound because the client controls the length, and it matches the <c>endlessBest</c>
        /// size guard in <c>firestore.rules</c> — it has to, because <c>hasOnly</c> is an
        /// allow-list over the whole document, so a save the client writes and the rules refuse
        /// loses <em>every</em> save write rather than the extra rows (invariant 12a).
        /// </summary>
        public const int MaxRows = 64;

        static readonly Dictionary<string, int> _best = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Raised when a best moved, so an open map can redraw its badge.</summary>
        public static event Action Changed;

        /// <summary>The furthest wave this level has ever reached, or nought.</summary>
        public static int BestFor(LevelId level)
            => level.IsValid && _best.TryGetValue(level.Value, out int wave) ? wave : 0;

        /// <summary>Whether any endless run has ever been finished at all.</summary>
        public static bool Any => _best.Count > 0;

        /// <summary>
        /// Records a run, and answers whether it was a new best.
        ///
        /// <b>A floor and never an assignment.</b> Two devices offline reach wave 14 and wave 9;
        /// the merge takes 14 whichever order they sync in, and a device that has just been handed
        /// a better number from the cloud must not push its own worse one back over it.
        /// </summary>
        public static bool Record(LevelId level, int wave)
        {
            if (!level.IsValid || wave <= 0) return false;
            if (_best.TryGetValue(level.Value, out int held) && held >= wave) return false;

            if (!_best.ContainsKey(level.Value) && _best.Count >= MaxRows) return false;

            _best[level.Value] = wave;
            Raise();
            return true;
        }

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        // --------------------------------------------------- file bridge (internal)
        internal static void LoadFrom(SaveFileDto dto)
        {
            _best.Clear();

            var rows = dto?.endlessBest;
            if (rows != null)
                foreach (var row in rows)
                {
                    if (row == null || string.IsNullOrEmpty(row.level) || row.wave <= 0) continue;
                    if (_best.TryGetValue(row.level, out int held) && held >= row.wave) continue;

                    _best[row.level] = row.wave;
                }

            Raise();
        }

        internal static void WriteInto(SaveFileDto dto) => dto.endlessBest = Sorted(_best);

        /// <summary>
        /// The larger of each side's bests.
        ///
        /// <b>No early return for an empty side</b>, which is <c>CompanionLedger.Join</c>'s trap:
        /// handing one array straight back would skip the sort, and <c>SaveDelta</c> walks these
        /// in order — so an unsorted file joined against nothing would read as changed on every
        /// launch and push a write for nothing, for ever.
        /// </summary>
        public static EndlessBestDto[] Join(EndlessBestDto[] mine, EndlessBestDto[] other)
        {
            var best = new Dictionary<string, int>(StringComparer.Ordinal);

            Absorb(best, mine);
            Absorb(best, other);

            return Sorted(best);
        }

        static void Absorb(Dictionary<string, int> into, EndlessBestDto[] rows)
        {
            if (rows == null) return;

            foreach (var row in rows)
            {
                if (row == null || string.IsNullOrEmpty(row.level) || row.wave <= 0) continue;
                if (into.TryGetValue(row.level, out int held) && held >= row.wave) continue;

                into[row.level] = row.wave;
            }
        }

        static EndlessBestDto[] Sorted(Dictionary<string, int> best)
        {
            var keys = new List<string>(best.Keys);
            keys.Sort(StringComparer.Ordinal);

            int take = keys.Count > MaxRows ? MaxRows : keys.Count;
            var rows = new EndlessBestDto[take];

            for (int i = 0; i < take; i++)
                rows[i] = new EndlessBestDto { level = keys[i], wave = best[keys[i]] };

            return rows;
        }
    }
}
