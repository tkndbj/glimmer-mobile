using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Shuffle
{
    /// <summary>
    /// How far a Shuffle run has ever got, per level: the one number the Shuffle board is
    /// ordered on - and, beside it, the best-graded run the level has seen.
    ///
    /// <para>
    /// <b>Why a ledger and not the level's own record.</b> The record keeps the best in
    /// <c>bestMoves</c>, which <c>LevelRecord.WithRun</c> folds the right way for a climbing level
    /// but <c>SaveMerge</c> joins by the <em>smaller</em>, because the merge is pure over the
    /// file and cannot know which levels climb. So every sync after a better run handed the worse
    /// one back. A best that two devices both write has exactly one legal shape, a monotonic
    /// integer per id joined by <c>max</c> (invariant 11b), and that is this.
    /// </para>
    /// <para>
    /// <b><c>EndlessLedger</c>'s best without its tally.</b> The Infinite lane counts lifetime
    /// waves because it pays XP for them; a Shuffle wave pays nothing (MODES.md 59), so the only
    /// figure kept is the published one. That is what keeps a number the server cannot recompute
    /// safe on a public board: it is <see cref="MaxWave">bounded</see> and buys nothing
    /// (invariant 19l). <b>Never make it pay.</b>
    /// </para>
    /// <para>
    /// <b>Each row also keeps its best-graded run</b> (<see cref="ShuffleRecord"/>, MODES.md 59o,
    /// save v43): the grade, score and figures the victory panel drew and the keeper's card
    /// publishes. A second fact on the same row with its own join - the wave by <c>max</c>, the run
    /// by <see cref="ShuffleRecord.Better"/>, a total order - so the two cannot tear, and the best
    /// run is kept whole even when another run went further. It pays nothing either.
    /// </para>
    /// <para>
    /// Rows naming a level this build does not know are carried through untouched (invariant 1),
    /// so a best set on a newer build survives a trip through an older one.
    /// </para>
    /// </summary>
    public static class ShuffleLedger
    {
        /// <summary>
        /// The most rows this will keep. Matches the <c>shuffleBest</c> size guard in
        /// <c>firestore.rules</c>, and has to: a save the rules refuse loses <em>every</em> save
        /// write (invariant 12b). <c>CloudWireTests</c> holds the pair.
        /// </summary>
        public const int MaxRows = 64;

        /// <summary>
        /// The furthest wave this will ever record or publish. The Infinite lane's ceiling, and
        /// <c>MAX_WAVE</c> in <c>functions/src/grove.ts</c> bounds the card with the same figure,
        /// for <see cref="Progression.EndlessLedger.MaxWave"/>'s reasons.
        /// </summary>
        public const int MaxWave = Progression.EndlessLedger.MaxWave;

        /// <summary>One level's two bests: how far, and the best-graded run (or null).</summary>
        struct Row
        {
            public int Wave;
            public ShuffleRecord Top;
        }

        static readonly Dictionary<string, Row> _rows = new Dictionary<string, Row>(StringComparer.Ordinal);

        /// <summary>Raised when a best moved, including on every load. A state, not an intent.</summary>
        public static event Action Changed;

        /// <summary>
        /// Raised when the player's own run set a new best - a wave or a graded run - and by
        /// nothing else: the intent <c>SyncTriggers</c> asks for a sync on. <see cref="Changed"/>
        /// fires on every load, so a sync asked for on it would be a sync every few seconds for ever.
        /// </summary>
        public static event Action Beaten;

        /// <summary>The furthest wave this level has ever reached, or nought.</summary>
        public static int BestFor(LevelId level)
            => level.IsValid && _rows.TryGetValue(level.Value, out var row) ? row.Wave : 0;

        /// <summary>The best-graded run this level has ever banked, or null.</summary>
        public static ShuffleRecord TopFor(LevelId level)
            => level.IsValid && _rows.TryGetValue(level.Value, out var row) ? row.Top : null;

        /// <summary>
        /// The furthest wave reached anywhere on the Shuffle lane - what the card publishes.
        /// The best of every row, for <c>EndlessLedger.Best</c>'s reason: the board is about the
        /// lane, so a second Shuffle level is a content decision rather than a new board id.
        /// </summary>
        public static int Best
        {
            get
            {
                int best = 0;
                foreach (var pair in _rows) if (pair.Value.Wave > best) best = pair.Value.Wave;
                return best;
            }
        }

        /// <summary>
        /// The best-graded run anywhere on the lane - what the card publishes, for <see cref="Best"/>'s
        /// reason. Null before any run took a card.
        /// </summary>
        public static ShuffleRecord Top
        {
            get
            {
                ShuffleRecord top = null;
                foreach (var pair in _rows) top = ShuffleRecord.Better(top, pair.Value.Top);
                return top;
            }
        }

        /// <summary>
        /// The same reading taken off a save file, read exactly as <c>shuffleWave</c> in
        /// <c>functions/src/grove.ts</c> reads it: the walk bounded at <see cref="MaxRows"/>, a
        /// row with no level or an over-long id refused, each wave clamped to
        /// <see cref="MaxWave"/>. What a publish is judged on has to come from the file the server
        /// holds (<c>GroveCard.OfSave</c>).
        /// </summary>
        public static int BestIn(SaveFileDto save)
        {
            var rows = save?.shuffleBest;
            if (rows == null) return 0;

            int best = 0;
            int walk = rows.Length < MaxRows ? rows.Length : MaxRows;

            for (int i = 0; i < walk; i++)
            {
                var row = rows[i];
                if (row == null || string.IsNullOrEmpty(row.level)) continue;
                if (row.level.Length > LevelId.MaxLength) continue;
                if (row.wave > best) best = row.wave;
            }

            return best <= 0 ? 0 : best > MaxWave ? MaxWave : best;
        }

        /// <summary>
        /// The best-graded run a save file holds, walked exactly as <see cref="BestIn"/> walks it
        /// and as <c>shuffleTop</c> in <c>functions/src/grove.ts</c> reads it: the same bounded
        /// walk, the same refused rows, the best by <see cref="ShuffleRecord.Compare"/>. Null for
        /// none.
        /// </summary>
        public static ShuffleRecord TopIn(SaveFileDto save)
        {
            var rows = save?.shuffleBest;
            if (rows == null) return null;

            ShuffleRecord top = null;
            int walk = rows.Length < MaxRows ? rows.Length : MaxRows;

            for (int i = 0; i < walk; i++)
            {
                var row = rows[i];
                if (row == null || string.IsNullOrEmpty(row.level)) continue;
                if (row.level.Length > LevelId.MaxLength) continue;

                top = ShuffleRecord.Better(top, ShuffleRecord.FromDto(row.top));
            }

            return top;
        }

        /// <summary>
        /// Records a finished run, and answers whether it was a new best. A floor and never an
        /// assignment: two devices offline reach wave 14 and wave 9, and the merge keeps 14
        /// whichever order they sync in.
        /// </summary>
        public static bool Record(LevelId level, int wave)
        {
            if (!level.IsValid || wave <= 0) return false;

            int next = Clamp(wave);
            _rows.TryGetValue(level.Value, out var held);
            if (held.Wave >= next) return false;

            if (!_rows.ContainsKey(level.Value) && _rows.Count >= MaxRows) return false;

            held.Wave = next;
            _rows[level.Value] = held;
            Raise(Changed);

            // After Changed, so anything redrawing off the best has the new number before the
            // sync this asks for can come back and load a save over it.
            Raise(Beaten);
            return true;
        }

        /// <summary>
        /// Banks a finished run's graded record, and answers whether it was a new best for the
        /// level - what the victory panel's NEW BEST says. A floor on the total order and never an
        /// assignment, like <see cref="Record"/>, and raised the same way: <see cref="Beaten"/>
        /// asks for the sync that carries it to the card.
        ///
        /// <para>
        /// <b>Its own call and not a clause of <see cref="Record"/></b>, for <c>EndlessLedger</c>'s
        /// reason about its tally: a run that fell short of the best wave can still be the
        /// best-graded run, and folding one into the other's early return would lose it.
        /// </para>
        /// </summary>
        public static bool RecordRun(LevelId level, ShuffleRecord run)
        {
            if (!level.IsValid || run == null) return false;

            _rows.TryGetValue(level.Value, out var held);
            if (ShuffleRecord.Compare(run, held.Top) <= 0) return false;

            if (!_rows.ContainsKey(level.Value) && _rows.Count >= MaxRows) return false;

            held.Top = run;
            if (held.Wave < run.Wave) held.Wave = Clamp(run.Wave);
            _rows[level.Value] = held;

            Raise(Changed);
            Raise(Beaten);
            return true;
        }

        static int Clamp(int wave) => wave < 0 ? 0 : wave > MaxWave ? MaxWave : wave;

        static void Raise(Action handler)
        {
            try { handler?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        // --------------------------------------------------- file bridge (internal)
        internal static void LoadFrom(SaveFileDto dto)
        {
            _rows.Clear();
            Absorb(_rows, dto?.shuffleBest);
            Raise(Changed);
        }

        internal static void WriteInto(SaveFileDto dto) => dto.shuffleBest = Sorted(_rows);

        /// <summary>
        /// The larger best per level, and the better run per level. No early return for an empty
        /// side, which is <c>CompanionLedger.Join</c>'s trap: handing one array straight back
        /// skips the sort, and <c>SaveDelta</c> compares these in order, so an unsorted file would
        /// read as changed on every launch.
        /// </summary>
        public static ShuffleBestDto[] Join(ShuffleBestDto[] mine, ShuffleBestDto[] other)
        {
            var rows = new Dictionary<string, Row>(StringComparer.Ordinal);

            Absorb(rows, mine);
            Absorb(rows, other);

            return Sorted(rows);
        }

        static void Absorb(Dictionary<string, Row> into, ShuffleBestDto[] rows)
        {
            if (rows == null) return;

            foreach (var row in rows)
            {
                if (row == null || string.IsNullOrEmpty(row.level)) continue;

                int wave = Clamp(row.wave);
                if (wave <= 0) continue;

                // Each fact joined by its own rule: the wave by `max`, the run by the total order.
                into.TryGetValue(row.level, out var held);
                if (held.Wave < wave) held.Wave = wave;
                held.Top = ShuffleRecord.Better(held.Top, ShuffleRecord.FromDto(row.top));
                into[row.level] = held;
            }
        }

        static ShuffleBestDto[] Sorted(Dictionary<string, Row> rows)
        {
            var keys = new List<string>(rows.Keys);
            keys.Sort(StringComparer.Ordinal);

            int take = keys.Count > MaxRows ? MaxRows : keys.Count;
            var written = new ShuffleBestDto[take];

            for (int i = 0; i < take; i++)
            {
                var row = rows[keys[i]];
                written[i] = new ShuffleBestDto
                {
                    level = keys[i],
                    wave = row.Wave,
                    top = ShuffleRecord.ToDto(row.Top),
                };
            }

            return written;
        }
    }
}
