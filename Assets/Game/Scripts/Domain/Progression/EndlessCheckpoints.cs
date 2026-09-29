using System;
using System.Collections.Generic;
using GlimmerGrove.Persistence;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;

namespace GlimmerGrove.Progression
{
    /// <summary>The bounds a <c>endlessCheckpoints</c> block is checked against. Mirrored by <c>content.py</c>.</summary>
    public static class EndlessCheckpointLimits
    {
        /// <summary>
        /// The most checkpoints a lane may carry. A handful of starts, not a wave picker - and the
        /// number the sheet is laid out for (<c>EndlessCheckpointOverlay</c>), which stands every
        /// row on one screen with no scroller.
        /// </summary>
        public const int MaxRows = 8;

        /// <summary>The earliest wave a checkpoint may open on. Wave one is the ordinary opening.</summary>
        public const int LowestWave = 2;
    }

    /// <summary>One checkpoint, as resolved: the wave it opens on, the best that opens it and the head start.</summary>
    public readonly struct EndlessCheckpoint
    {
        /// <summary>The wave a run opens on, counting from one.</summary>
        public readonly int Wave;

        /// <summary>The best - waves cleared on the lane - at or above which it is open.</summary>
        public readonly int UnlockAt;

        /// <summary>Cogs spent on every turret before the first wave, which is the rank each starts at.</summary>
        public readonly int Cogs;

        public EndlessCheckpoint(int wave, int unlockAt, int cogs)
        {
            Wave = wave;
            UnlockAt = unlockAt;
            Cogs = cogs;
        }

        /// <summary>Whether this is a checkpoint at all. The default is "from the beginning".</summary>
        public bool IsValid => Wave >= EndlessCheckpointLimits.LowestWave;

        /// <summary>Whether a lane whose best is <paramref name="best"/> may open here.</summary>
        public bool OpenAt(int best) => IsValid && best >= UnlockAt;

        /// <summary>Where a run opening here begins.</summary>
        public SiegeStart Start => IsValid ? new SiegeStart(Wave, Cogs) : SiegeStart.Opening;
    }

    /// <summary>
    /// The Infinite lane's checkpoints - the content half of MODES.md 43f.
    ///
    /// <para>
    /// <b>A checkpoint is derived from the best and stored nowhere.</b> Which ones a player may
    /// open at is a pure function of <see cref="EndlessLedger.BestFor"/> - a monotonic floor
    /// already on the wire and joined by <c>max</c> (invariant 11b) - so it costs no save field,
    /// no schema version, no rules release and no server, and it can never be taken back: a best
    /// cannot fall. What a device keeps is only which one the player last <em>chose</em>
    /// (<see cref="EndlessCheckpoints"/>), and that is a hint, re-asked against the best on every
    /// read (8b's shape).
    /// </para>
    /// <para>
    /// <b>Every checkpoint opens on the wave after a boss wave</b>, at the owner's instruction on
    /// 2026-09-29: a run opened one wave before a duel meets two bosses before it has picked up a
    /// single cog. The reader refuses a row that breaks it, asking <see cref="SiegeEndless.IsBossWave"/>
    /// rather than a copy of the schedule, so a retuned schedule fails the build rather than
    /// quietly standing a checkpoint in front of a boss.
    /// </para>
    /// <para>
    /// <b>Refused whole on any fault</b>, like the milestone table: a half-read ladder of starts
    /// would offer a checkpoint the next build withdraws. Absent offers none, and the lane opens at
    /// wave one exactly as it always did.
    /// </para>
    /// </summary>
    public sealed class EndlessCheckpointTable
    {
        readonly EndlessCheckpoint[] _rows;

        EndlessCheckpointTable(EndlessCheckpoint[] rows) => _rows = rows ?? Array.Empty<EndlessCheckpoint>();

        /// <summary>The table that offers nothing - the default, and what a broken block resolves to.</summary>
        public static readonly EndlessCheckpointTable Empty = new EndlessCheckpointTable(Array.Empty<EndlessCheckpoint>());

        /// <summary>The checkpoints, earliest wave first.</summary>
        public IReadOnlyList<EndlessCheckpoint> Rows => _rows;

        /// <summary>Whether the lane offers any checkpoint at all.</summary>
        public bool Any => _rows.Length > 0;

        /// <summary>The checkpoint opening on exactly <paramref name="wave"/>, or the default.</summary>
        public EndlessCheckpoint At(int wave)
        {
            for (int i = 0; i < _rows.Length; i++)
                if (_rows[i].Wave == wave) return _rows[i];

            return default;
        }

        /// <summary>How many checkpoints a best of <paramref name="best"/> has opened.</summary>
        public int OpenCount(int best)
        {
            int n = 0;
            for (int i = 0; i < _rows.Length; i++) if (_rows[i].OpenAt(best)) n++;
            return n;
        }

        /// <summary>The first checkpoint a best of <paramref name="best"/> has not opened yet, or the default.</summary>
        public EndlessCheckpoint NextLocked(int best)
        {
            for (int i = 0; i < _rows.Length; i++)
                if (!_rows[i].OpenAt(best)) return _rows[i];

            return default;
        }

        /// <summary>
        /// Where a run opens, given the wave the player chose and the lane's best.
        ///
        /// <b>The choice is a hint and the best is the authority</b>: a chosen wave that is not a
        /// checkpoint of this table, or one the best has not opened - a retuned table, an account
        /// switched to one that never went that far - is the default, which opens at wave one.
        /// </summary>
        public EndlessCheckpoint Resolve(int chosenWave, int best)
        {
            var row = At(chosenWave);
            return row.OpenAt(best) ? row : default;
        }

        /// <summary>
        /// Reads the optional <c>endlessCheckpoints</c> block. Never throws and never returns
        /// null: anything wrong is named in <paramref name="problems"/> and the table resolves to
        /// <see cref="Empty"/>.
        /// </summary>
        public static EndlessCheckpointTable Resolve(EndlessCheckpointsDto dto, List<string> problems)
        {
            problems ??= new List<string>();
            if (dto == null || !dto.IsAuthored) return Empty;                // absent offers none

            var rows = dto.rows;
            if (rows.Length > EndlessCheckpointLimits.MaxRows)
            {
                problems.Add($"endlessCheckpoints lists {rows.Length} rows; at most " +
                             $"{EndlessCheckpointLimits.MaxRows} are supported, and the sheet is laid " +
                             "out for no more");
                return Empty;
            }

            var resolved = new EndlessCheckpoint[rows.Length];
            bool ok = true;
            int lastWave = 0, lastUnlock = 0;

            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                string where = $"endlessCheckpoints row {i}";

                if (row == null) { problems.Add($"{where} is empty"); ok = false; continue; }

                if (row.wave < EndlessCheckpointLimits.LowestWave || row.wave > EndlessLedger.MaxWave)
                {
                    problems.Add($"{where} opens on wave {row.wave}, outside " +
                                 $"{EndlessCheckpointLimits.LowestWave}..{EndlessLedger.MaxWave}");
                    ok = false;
                }
                else if (!SiegeEndless.IsBossWave(row.wave - 1))
                {
                    problems.Add($"{where} opens on wave {row.wave}, and wave {row.wave - 1} sends no " +
                                 "boss; a checkpoint opens on the wave after a boss wave, or a run " +
                                 "can meet a boss before it has picked up a cog");
                    ok = false;
                }

                if (row.wave <= lastWave)
                {
                    problems.Add($"{where} opens on wave {row.wave} after wave {lastWave}; rows must climb");
                    ok = false;
                }

                // A checkpoint is somewhere the player has already been: opening one below the
                // wave it starts on would hand a start to somebody who never reached it.
                if (row.unlockAt < row.wave)
                {
                    problems.Add($"{where} opens at a best of {row.unlockAt}, below its own wave " +
                                 $"{row.wave}; a checkpoint must be somewhere the player has been");
                    ok = false;
                }

                if (row.unlockAt <= lastUnlock)
                {
                    problems.Add($"{where} opens at a best of {row.unlockAt} after {lastUnlock}; " +
                                 "a later checkpoint must ask for more");
                    ok = false;
                }

                if (row.cogs < 0 || row.cogs > SiegeTuning.MaxRank)
                {
                    problems.Add($"{where} spends {row.cogs} cogs on every turret, outside " +
                                 $"0..{SiegeTuning.MaxRank}, the ranks a turret has");
                    ok = false;
                }

                lastWave = row.wave;
                lastUnlock = row.unlockAt;
                resolved[i] = new EndlessCheckpoint(row.wave, row.unlockAt, row.cogs);
            }

            return ok ? new EndlessCheckpointTable(resolved) : Empty;
        }
    }

    /// <summary>
    /// Which checkpoint the player has chosen, per account and per lane, and where the next run
    /// therefore opens.
    ///
    /// <para>
    /// <b>Device-local and never in the save</b>, for the same reason the map's chapter is
    /// (invariant 8b): it is a preference about where to begin, it moves both ways, so it cannot
    /// be joined by a merge - and nothing it decides pays anything. It is kept per account, so a
    /// switch lands on the other account's choice, and every read re-asks the best, so a choice
    /// the best no longer supports opens at wave one rather than somewhere unearned.
    /// </para>
    /// <para>
    /// <b>Behind a store seam</b>, <see cref="EndlessCoins.ITallyStore"/>'s, so the rule runs
    /// offline without <c>PlayerPrefs</c> (the "needs the Editor" lesson of 2026-09-22).
    /// </para>
    /// </summary>
    public static class EndlessCheckpoints
    {
        const string Key = "glimmer.endless.checkpoint";

        static EndlessCoins.ITallyStore _store;

        /// <summary>Raised when the chosen checkpoint moved, so a hub standing open repaints its bar.</summary>
        public static event Action Changed;

        /// <summary>The published table. <see cref="EndlessCheckpointTable.Empty"/> when the file carries none.</summary>
        public static EndlessCheckpointTable Table => ProgressionRules.Table.EndlessCheckpoints;

        /// <summary>The wave the player last chose on this lane, or one. A hint; see <see cref="Chosen"/>.</summary>
        public static int ChosenWave(LevelId level)
        {
            if (!level.IsValid) return 1;

            string raw = Store.Read(Slot(level));
            return int.TryParse(raw, out int wave) && wave > 1 ? wave : 1;
        }

        /// <summary>
        /// The checkpoint the next run on <paramref name="level"/> opens at, or the default for
        /// wave one: the choice, re-asked against the lane's best and the published table.
        /// </summary>
        public static EndlessCheckpoint Chosen(LevelId level)
            => Table.Resolve(ChosenWave(level), EndlessLedger.BestFor(level));

        /// <summary>Where the next run on <paramref name="level"/> begins.</summary>
        public static SiegeStart StartFor(LevelId level) => Chosen(level).Start;

        /// <summary>
        /// Chooses where the next run opens: a checkpoint's wave, or one for the beginning.
        /// Answers false, and changes nothing, for a wave that is not an open checkpoint.
        /// </summary>
        public static bool Choose(LevelId level, int wave)
        {
            if (!level.IsValid) return false;

            if (wave <= 1)
            {
                if (ChosenWave(level) <= 1) return true;
                Store.Delete(Slot(level));
                Raise();
                return true;
            }

            var row = Table.At(wave);
            if (!row.OpenAt(EndlessLedger.BestFor(level))) return false;
            if (ChosenWave(level) == wave) return true;

            Store.Write(Slot(level), wave.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Raise();
            return true;
        }

        static EndlessCoins.ITallyStore Store => _store ?? EndlessCoins.Store;

        /// <summary>For a fixture: a store that is not <c>PlayerPrefs</c>. Null puts the shared one back.</summary>
        internal static void UseStore(EndlessCoins.ITallyStore store) => _store = store;

        static string Slot(LevelId level)
        {
            string uid = CloudState.UserId ?? string.Empty;
            return uid.Length == 0 ? Key + ":" + level.Value : Key + ":" + uid + ":" + level.Value;
        }

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }
    }
}
