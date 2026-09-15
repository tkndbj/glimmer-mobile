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
    /// <b>It pays nothing, deliberately, and that is what makes it publishable.</b> Credits and XP
    /// derive from the star ledger and from nothing else (invariant 9), so an endless run buys a
    /// place on a board and a number on a map node. Unlike a grove's worth this cannot be
    /// <em>recomputed</em> by the server — nothing it holds implies how far a run got, which is
    /// invariant 10d's shape — so the only two defences a public wave has are that it is
    /// <see cref="MaxWave">bounded</see> and that forging it buys nothing at all. <b>Never make the
    /// endless board pay</b>: the moment a wave decides currency it becomes a claim with no way to
    /// adjudicate it (invariant 13).
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

        /// <summary>
        /// The furthest wave this will ever record or publish.
        ///
        /// <para>
        /// <b>A ceiling rather than a clamp on a derivation</b>, because there is no derivation to
        /// clamp against: a wave count comes out of a run this server never saw. What a bound is
        /// worth is that it keeps a forged number inside the range a real one is drawn in, so a
        /// tampered save takes a row on a board rather than making every honest row unreadable
        /// beside a ten-digit one. It is mirrored by <c>MAX_WAVE</c> in <c>functions/src/grove.ts</c>
        /// and the two must move together, or the client's prediction and the server's card
        /// disagree for the one account that reaches it.
        /// </para>
        /// <para>
        /// Four figures is far past anything the mode can produce — a wave is a muster on a clock,
        /// so ten thousand of them is a run measured in days — and it is deliberately not tuned any
        /// tighter than that: a ceiling a real player could ever meet is a ceiling that silently
        /// stops recording their best.
        /// </para>
        /// </summary>
        public const int MaxWave = 9999;

        static readonly Dictionary<string, int> _best = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Raised when a best moved, so an open map can redraw its badge.</summary>
        public static event Action Changed;

        /// <summary>
        /// Raised when the player's own run set a new best, and by nothing else.
        ///
        /// <para>
        /// <b>An intent, where <see cref="Changed"/> is a state.</b> <see cref="Changed"/> fires on
        /// every <see cref="LoadFrom"/>, which is every sync that adopts a merge — so a sync asked
        /// for on it is a sync every few seconds for the life of the process, which is exactly the
        /// trap <c>SyncTriggers</c> is written around. This fires once, from <see cref="Record"/>,
        /// when a person actually did something.
        /// </para>
        /// </summary>
        public static event Action Beaten;

        /// <summary>The furthest wave this level has ever reached, or nought.</summary>
        public static int BestFor(LevelId level)
            => level.IsValid && _best.TryGetValue(level.Value, out int wave) ? wave : 0;

        /// <summary>Whether any endless run has ever been finished at all.</summary>
        public static bool Any => _best.Count > 0;

        /// <summary>
        /// The furthest wave reached anywhere on the endless lane — the one number the public
        /// board is ordered on.
        ///
        /// <para>
        /// <b>The best of every row rather than one named level, and that is a decision about what
        /// the board is about.</b> Nothing but an endless run ever writes a row here, so this is
        /// "the furthest this keeper has ever held out", which stays the right sentence if the
        /// Infinite lane ever grows a second level (invariant 43 — the lane is a track, and a track
        /// is one ladder). A board about one named level would have to carry that level's id into
        /// the save, into the server's config and into a board id, and would answer nothing better.
        /// </para>
        /// </summary>
        public static int Best
        {
            get
            {
                int best = 0;
                foreach (var pair in _best) if (pair.Value > best) best = pair.Value;
                return best > MaxWave ? MaxWave : best;
            }
        }

        /// <summary>
        /// The same reading taken off a <em>save file</em> rather than off this ledger.
        ///
        /// <para>
        /// <b>What a publish is judged on has to come from the file the server holds</b>, never
        /// from the live ledger: a run finished while a push was in flight is on the device and not
        /// on the server, and a fingerprint taken from the device would mark it published when it
        /// never was. That is <see cref="Social.GroveCard.OfSave"/>'s whole argument, arriving on
        /// the one field of a card that is not a grove.
        /// </para>
        /// </summary>
        public static int BestIn(SaveFileDto save)
        {
            int best = 0;
            var rows = save?.endlessBest;
            if (rows == null) return 0;

            foreach (var row in rows)
            {
                if (row == null || string.IsNullOrEmpty(row.level)) continue;
                if (row.wave > best) best = row.wave;
            }

            return best <= 0 ? 0 : best > MaxWave ? MaxWave : best;
        }

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

            _best[level.Value] = wave > MaxWave ? MaxWave : wave;
            Raise();

            // After Changed, so anything redrawing off the badge has the new number before the
            // sync this asks for can possibly come back and load a save over it.
            try { Beaten?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }

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
