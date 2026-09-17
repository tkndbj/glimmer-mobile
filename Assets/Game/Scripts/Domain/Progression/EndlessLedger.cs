using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Progression
{
    /// <summary>
    /// What the Infinite lane leaves behind: how far a run has ever got, and how many waves have
    /// been seen off altogether.
    ///
    /// <para>
    /// <b>Two numbers per level and both are floors.</b> Everything else about a run is already
    /// recorded by the machinery every other level uses — the heart, the chest count, the streak,
    /// the star ledger — and none of it fits a board that is never won. Neither of these is a
    /// grade: a <see cref="Row.Best">best</see> is a high-water mark and a
    /// <see cref="Row.Waves">lifetime count</see> is a tally of things that happened, so both are
    /// monotonic integers per level id joined by <c>max</c>, which is invariant 14a's floor
    /// exactly and the only shape invariant 11b permits for a number two devices both write.
    /// </para>
    /// <para>
    /// <b>The best pays nothing and the lifetime count does, and the split is the whole design.</b>
    /// The best is what the public board is ordered on, and it must stay unpaid: the server cannot
    /// recompute a wave, so the two defences a <em>published</em> number has are that it is
    /// <see cref="MaxWave">bounded</see> and that forging it buys nothing (invariant 19l). The
    /// lifetime count is never published, and it pays XP at a rate and under a ceiling that are
    /// both content (<see cref="EndlessRewardTable"/>) — so a forged one buys keeper levels inside
    /// a range an honest player is drawn in, and buys no currency at all, because credits still
    /// derive from the star ledger and from nothing else. <b>Never publish the lifetime count, and
    /// never make the ordered board pay.</b>
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

        /// <summary>
        /// The most lifetime waves one row may hold.
        ///
        /// <para>
        /// <b>A structural bound, not the one a player meets.</b> That one is
        /// <see cref="EndlessRewardTable.MaxWaves"/> and it is content, applied when the XP is
        /// derived. This is applied when the count is <em>stored</em>, and the two must never be
        /// the same number: clamping a stored monotonic count against a published one would cut
        /// it downward on whichever devices had fetched a lowered table, and a count that only
        /// ever rises is the whole of what makes the merge a <c>max</c> (invariant 11b). See
        /// <see cref="EndlessLimits.HardMaxWaves"/>.
        /// </para>
        /// </summary>
        public const int MaxLifetimeWaves = EndlessLimits.HardMaxWaves;

        /// <summary>
        /// One level's two floors.
        ///
        /// <para>
        /// A struct in one dictionary rather than two dictionaries keyed alike, so the pair cannot
        /// be written apart — which is <c>invariant 16x</c>'s fault said about a ledger: a value
        /// and the value derived beside it want one writer, or one of them is stale and nothing
        /// can see it.
        /// </para>
        /// </summary>
        public readonly struct Row
        {
            /// <summary>The furthest a single run has ever got. The board's number.</summary>
            public readonly int Best;

            /// <summary>Waves seen off across every run ever played here. The XP's number.</summary>
            public readonly int Waves;

            public Row(int best, int waves)
            {
                Best = best < 0 ? 0 : best > MaxWave ? MaxWave : best;
                Waves = waves < 0 ? 0 : waves > MaxLifetimeWaves ? MaxLifetimeWaves : waves;
            }

            /// <summary>
            /// What this row is worth to the reward derivation.
            ///
            /// <para>
            /// <b>The best is a floor under the lifetime count, and that is the migration.</b> A
            /// save written before this ledger counted lifetime waves has a best and no tally, and
            /// reading it as nought would tell a player who had already reached wave forty that
            /// they had never played. Taking the larger of the two is honest in both directions —
            /// a lifetime total is at least one run's worth by definition — it is idempotent, it
            /// can only ever rise, and it needs no sentinel and no migration, which is the
            /// property every other id-keyed section in the save file has.
            /// </para>
            /// <para>
            /// The server applies the identical rule. If it ever stops, the two halves disagree
            /// about a keeper level and a published card drops what that level gated (19a).
            /// </para>
            /// </summary>
            public int Payable => Waves > Best ? Waves : Best;

            public bool IsEmpty => Best <= 0 && Waves <= 0;
        }

        static readonly Dictionary<string, Row> _rows = new Dictionary<string, Row>(StringComparer.Ordinal);

        /// <summary>Raised when a best or a tally moved, so an open map can redraw its badge.</summary>
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
            => level.IsValid && _rows.TryGetValue(level.Value, out var row) ? row.Best : 0;

        /// <summary>Waves seen off on this level across every run, or nought.</summary>
        public static int WavesFor(LevelId level)
            => level.IsValid && _rows.TryGetValue(level.Value, out var row) ? row.Payable : 0;

        /// <summary>Whether any endless run has ever been finished at all.</summary>
        public static bool Any => _rows.Count > 0;

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
                foreach (var pair in _rows) if (pair.Value.Best > best) best = pair.Value.Best;
                return best > MaxWave ? MaxWave : best;
            }
        }

        /// <summary>
        /// Waves seen off across the whole lane, which is what the XP is derived from.
        ///
        /// <para>
        /// <b>A sum where <see cref="Best"/> is a maximum</b>, because the two answer different
        /// questions: the board asks how far one run got and the reward asks how much was played.
        /// Summed across rows rather than read off one named level for <see cref="Best"/>'s reason
        /// — nothing but an endless run writes here — and <c>long</c> because the ceiling is
        /// applied by <see cref="EndlessRewardTable.XpFor"/> afterwards rather than here, so this
        /// may legitimately exceed it.
        /// </para>
        /// </summary>
        public static long LifetimeWaves
        {
            get
            {
                long total = 0L;
                foreach (var pair in _rows) total += pair.Value.Payable;
                return total;
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
        /// Lifetime waves off a save file, read exactly as <c>endlessWaves</c> in
        /// <c>functions/src/grove.ts</c> reads them — the same rows, the same per-row clamp and
        /// the same <see cref="Row.Payable">best-as-a-floor</see> rule.
        ///
        /// <b>The two must agree</b>, or the server's keeper level and the device's differ and a
        /// published card drops whatever that level gated (invariant 19a). The shared vectors hold
        /// the pair.
        /// </summary>
        public static long LifetimeWavesIn(SaveFileDto save)
        {
            var rows = save?.endlessBest;
            if (rows == null) return 0L;

            long total = 0L;

            // **The cap bounds the walk, not the tally of rows it accepted**, which is the same
            // reading `endlessWaves` and `bestWave` take on the server and the same one
            // `firestore.rules` bounds (`size() <= 64`). The distinction only ever shows on a
            // malformed document — a refused row near the top would otherwise let this side read
            // a sixty-fifth that the other side never reaches — and the shared vectors caught
            // exactly that, which is what they are for. An honest save never has more than
            // `MaxRows`, because `Sorted` is the only thing that writes one.
            int walk = rows.Length < MaxRows ? rows.Length : MaxRows;

            for (int i = 0; i < walk; i++)
            {
                var row = rows[i];
                if (row == null || string.IsNullOrEmpty(row.level)) continue;

                // A level id this long cannot have come from a catalog we shipped
                // (<see cref="LevelId.MaxLength"/>), and the server's half refuses one by the
                // same measure. This rule is the one place the two sides must agree to the
                // integer, so the refusals have to match as well as the arithmetic.
                if (row.level.Length > LevelId.MaxLength) continue;

                total += new Row(row.wave, row.waves).Payable;
            }

            return total;
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

            _rows.TryGetValue(level.Value, out var held);
            if (held.Best >= wave) return false;

            if (!_rows.ContainsKey(level.Value) && _rows.Count >= MaxRows) return false;

            _rows[level.Value] = new Row(wave, held.Waves);
            Raise();

            // After Changed, so anything redrawing off the badge has the new number before the
            // sync this asks for can possibly come back and load a save over it.
            try { Beaten?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }

            return true;
        }

        /// <summary>
        /// Adds a finished run's waves to this level's lifetime tally, and answers the tally
        /// afterwards.
        ///
        /// <para>
        /// <b>Separate from <see cref="Record"/> because it is unconditional.</b> A run that did
        /// not beat the best still happened, and it is what the player is paid for — folding this
        /// into the best's early return is how the tenth run on a good day would have paid nothing.
        /// </para>
        /// <para>
        /// <b>Called once per run, from the one place a run ends</b> (<c>ProtoScreen.Solve</c> by
        /// way of <c>Finished</c>, guarded by that screen's own <c>_finished</c> latch). A continue
        /// puts the ward line back up and the same run carries on, so the waves of a continued run
        /// arrive here once, at the true ending, already totalled by the board.
        /// </para>
        /// <para>
        /// Adds rather than assigns, and saturates at <see cref="MaxLifetimeWaves"/> rather than
        /// wrapping — a tally that wrapped would fall, and everything downstream of this is a
        /// <c>max</c> that assumes it cannot.
        /// </para>
        /// </summary>
        public static long Bank(LevelId level, int waves)
        {
            if (!level.IsValid) return 0L;

            _rows.TryGetValue(level.Value, out var held);
            if (waves <= 0) return held.Payable;

            if (!_rows.ContainsKey(level.Value) && _rows.Count >= MaxRows) return held.Payable;

            // **Added to the tally, never to <see cref="Row.Payable"/>, and that distinction is a
            // bug `EndlessRewardTests.ARunThatBeatNothingStillPays` caught.** `Payable` floors the tally with the best, and `Record`
            // runs immediately before this on the same run — so a run that set a new best of forty
            // raised the floor to forty and then had its own forty added on top of it, paying
            // twice for one watch. The floor is materialised once, where it belongs, at the load
            // and merge boundary (`Absorb`); by the time anything is banked the tally already
            // carries it.
            long total = (long)held.Waves + waves;
            if (total > MaxLifetimeWaves) total = MaxLifetimeWaves;

            var next = new Row(held.Best, (int)total);
            if (next.Waves == held.Waves && next.Best == held.Best) return held.Payable;

            _rows[level.Value] = next;
            Raise();

            return next.Payable;
        }

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        // --------------------------------------------------- file bridge (internal)
        internal static void LoadFrom(SaveFileDto dto)
        {
            _rows.Clear();
            Absorb(_rows, dto?.endlessBest);
            Raise();
        }

        internal static void WriteInto(SaveFileDto dto) => dto.endlessBest = Sorted(_rows);

        /// <summary>
        /// The larger of each side's floors, taken field by field.
        ///
        /// <para>
        /// <b>Per field rather than per row</b>, because the two numbers move independently: a
        /// device that set a new best while offline and one that played four more ordinary runs
        /// have each moved one of them, and taking whichever row looked bigger would discard the
        /// other device's half. Both only ever rise, so a per-field <c>max</c> is well defined
        /// whichever order the two sides arrive in (invariant 11b).
        /// </para>
        /// <para>
        /// <b>No early return for an empty side</b>, which is <c>CompanionLedger.Join</c>'s trap:
        /// handing one array straight back would skip the sort, and <c>SaveDelta</c> walks these
        /// in order — so an unsorted file joined against nothing would read as changed on every
        /// launch and push a write for nothing, for ever.
        /// </para>
        /// </summary>
        public static EndlessBestDto[] Join(EndlessBestDto[] mine, EndlessBestDto[] other)
        {
            var rows = new Dictionary<string, Row>(StringComparer.Ordinal);

            Absorb(rows, mine);
            Absorb(rows, other);

            return Sorted(rows);
        }

        static void Absorb(Dictionary<string, Row> into, EndlessBestDto[] rows)
        {
            if (rows == null) return;

            foreach (var row in rows)
            {
                if (row == null || string.IsNullOrEmpty(row.level)) continue;

                // **The floor is materialised here and nowhere else.** `Row.Payable` is the rule
                // both halves of the wire derive with, and this is the one place a save is read,
                // so applying it on the way in leaves an invariant everything downstream can rely
                // on: an in-memory tally is never below its own best. `Bank` then simply adds,
                // and the v29 file that arrives with a best and no tally is migrated by being
                // read — no sentinel, no migration step, and idempotent because taking the
                // maximum twice is taking it once.
                var read = new Row(row.wave, row.waves);
                var arriving = new Row(read.Best, read.Payable);
                if (arriving.IsEmpty) continue;

                if (into.TryGetValue(row.level, out var held))
                {
                    into[row.level] = new Row(
                        arriving.Best > held.Best ? arriving.Best : held.Best,
                        arriving.Waves > held.Waves ? arriving.Waves : held.Waves);
                    continue;
                }

                into[row.level] = arriving;
            }
        }

        static EndlessBestDto[] Sorted(Dictionary<string, Row> rows)
        {
            var keys = new List<string>(rows.Keys);
            keys.Sort(StringComparer.Ordinal);

            int take = keys.Count > MaxRows ? MaxRows : keys.Count;
            var written = new EndlessBestDto[take];

            for (int i = 0; i < take; i++)
            {
                var row = rows[keys[i]];
                written[i] = new EndlessBestDto { level = keys[i], wave = row.Best, waves = row.Waves };
            }

            return written;
        }
    }
}
