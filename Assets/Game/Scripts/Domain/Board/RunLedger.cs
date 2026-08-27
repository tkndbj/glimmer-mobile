using GlimmerGrove.Analytics;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

namespace GlimmerGrove
{
    /// <summary>
    /// What the streak did on the run that just ended: how long it now is, and whether
    /// this run is what extended it.
    ///
    /// Both are needed and neither implies the other. A second run of the evening leaves a
    /// six-day streak at six and extends nothing, and a panel that congratulated the player
    /// every time would be congratulating them for a thing they did an hour ago.
    /// </summary>
    public readonly struct StreakNote
    {
        public readonly int Days;
        public readonly bool Advanced;

        public StreakNote(int days, bool advanced)
        {
            Days = days < 0 ? 0 : days;
            Advanced = advanced;
        }

        /// <summary>Worth a line only when this run moved it. See the type summary.</summary>
        public bool WorthSaying => Advanced && Days > 0;
    }

    /// <summary>
    /// What happens to a player's account when a run ends, in one place for every mode.
    ///
    /// <para>
    /// <b>This is the half of a run that must never be copied.</b> Invariant 20b says a mode may
    /// bring a whole screen and must share everything about being a run, and this is that
    /// sharing made real: the record, the daily chests, the streak, the reward and the analytics
    /// are counted here or nowhere. A second mode with its own copy would be a second place a
    /// loss can stop feeding the streak, or a win can stop paying — and the failure is silent,
    /// because a mode that pays nothing looks exactly like a mode nobody plays.
    /// </para>
    /// <para>
    /// It also owns an ordering that was a comment before it was a rule. The
    /// <see cref="RunOutcome"/> is built <em>before</em> the record is folded in, because half
    /// of what it describes — the previous best, whether this was a first clear — stops being
    /// true the moment it is. Reading the record afterwards produces a panel that says "new
    /// best" never, or "first clear" always, depending on which way round the caller got it.
    /// Having one caller-proof order is most of why this exists.
    /// </para>
    /// <para>
    /// Nothing here draws anything. It returns what it did so the screen can say so, which is
    /// what keeps two very different-looking panels honest about the same numbers.
    /// </para>
    /// </summary>
    public static class RunLedger
    {
        public readonly struct WinRecord
        {
            public readonly RunOutcome Run;
            public readonly StreakNote Streak;
            public readonly long Xp, Credits;
            public readonly int GoldenPercent;

            /// <summary>
            /// The chapter this run's stars opened, or none.
            ///
            /// <para>
            /// Answered here rather than by the victory panel because it is a
            /// <em>transition</em>, and by the time a panel is built the transition is over —
            /// the record has been folded in and the gate simply reads open, which is
            /// indistinguishable from a gate that was already open an hour ago. That is the
            /// same trap the streak's <c>Advanced</c> exists to avoid, so it is answered the
            /// same way: measured either side of the fold, on the one line that owns the
            /// ordering.
            /// </para>
            /// <para>
            /// It is news rather than a reward — nothing is granted, nothing is stored and
            /// nothing is claimed. A player who never sees this line finds the chapter open on
            /// the map, which is why it is safe for it to be a line on a panel that can be
            /// skipped.
            /// </para>
            /// </summary>
            public readonly ChapterId ChapterOpened;

            public WinRecord(RunOutcome run, StreakNote streak, long xp, long credits, int golden,
                             ChapterId chapterOpened)
            {
                Run = run;
                Streak = streak;
                Xp = xp;
                Credits = credits;
                GoldenPercent = golden;
                ChapterOpened = chapterOpened;
            }
        }

        public readonly struct LossRecord
        {
            public readonly RunOutcome Run;
            public readonly StreakNote Streak;
            public readonly int HeartsLeft;
            public readonly bool HeartCharged;

            /// <summary>
            /// What the run was priced at, and if nothing, why — a free opening or a glade this
            /// player had already finished. See <see cref="HeartStake"/>.
            ///
            /// <para>
            /// Carried rather than left to be inferred from <see cref="HeartCharged"/>, because
            /// the two false cases are opposites and the panel says opposite things about them:
            /// nothing was taken because nothing was owed, or nothing was taken because there
            /// was nothing left to take. Read the second as the first and the panel offers a
            /// retry button to a player who cannot use one.
            /// </para>
            /// <para>
            /// The <em>reason</em> travels with it for the same argument one step further on:
            /// the panel prints a sentence about it, and a panel that says "one of the free
            /// levels" over the fortieth glade of a chapter somebody has finished is a panel
            /// nobody believes twice. It is told rather than working the reason out again for
            /// <see cref="Loss"/>'s standing reason — a second reading, taken later, able to
            /// disagree with the first.
            /// </para>
            /// </summary>
            public readonly HeartPrice Price;

            /// <summary>True when nothing was owed for the loss, whichever clause said so.</summary>
            public bool WasFree => Price != HeartPrice.Charged;

            public LossRecord(RunOutcome run, StreakNote streak, int heartsLeft, bool charged,
                              HeartPrice price)
            {
                Run = run;
                Streak = streak;
                HeartsLeft = heartsLeft;
                HeartCharged = charged;
                Price = price;
            }
        }

        /// <summary>
        /// A level was finished. Records it, feeds the daily loop, and works out what the run
        /// was worth.
        ///
        /// <para>
        /// The reward is the difference between the record before and the record after, not a
        /// payout for the run — so a replay that does not beat the old result is worth nothing,
        /// and that falls out of the subtraction rather than needing a rule anybody has to
        /// remember. It is also why the order above matters.
        /// </para>
        /// </summary>
        public static WinRecord Win(LevelDefinition level, int stars, int moves,
                                    float seconds, int hintsUsed, int route, int lit, int wanted)
        {
            var before = PlayerProgress.Record(level.Id);
            var tuning = level.Tuning;

            // Read before the fold for WinRecord.ChapterOpened's reason: a gate that is open
            // afterwards says nothing about whether this run is what opened it.
            var index = GameContent.Index;
            var chapter = index.ChapterOf(level.Id);
            bool wasOpen = LevelUnlock.GateAfter(index, chapter).IsOpen;

            var run = RunOutcome.Win(level.Id, stars, moves, tuning.GoldThreshold,
                                     before.BestMoves, !before.IsCleared, before.Clears + 1,
                                     lit, wanted, hintsUsed, seconds, route);

            PlayerProgress.RecordRun(level.Id, stars, moves);

            // Counted here and in the loss, which are the two places a run actually ends.
            // PlayerProgress hears about wins only — a defeat is not a worse clear, it simply
            // did not happen — so there is no single hook further down to hang this on, and
            // pretending otherwise would silently stop counting losses.
            DailyChests.RecordRun();
            var streak = Record();

            var reward = PlayerProgression.RewardFor(before, PlayerProgress.Record(level.Id));

            LevelAnalytics.TrackCompleted(level, moves, stars, hintsUsed, seconds, run.FirstClear);

            // The other half of the reading. Only a chapter that was shut a moment ago is news,
            // and only the chapter directly after this one can have moved — a gate counts the
            // stars of the chapter behind it and nothing else.
            var opened = !wasOpen && LevelUnlock.GateAfter(index, chapter).IsOpen
                ? index.ChapterNeighbour(chapter, +1)
                : null;

            return new WinRecord(run, streak, reward.Xp, reward.EarnedCredits,
                                 PlayerProgression.GoldenPercentFor(level.Id),
                                 opened?.Id ?? ChapterId.None);
        }

        /// <summary>
        /// A run was lost. Charges what the run was staked for, and feeds the daily loop
        /// whether or not anything was owed.
        ///
        /// <para>
        /// <paramref name="price"/> is <c>RunScreen.Price</c> — what this run was staked at,
        /// which is free for a mode's opening glades and for any glade the player has already
        /// finished (<see cref="HeartStake"/>). It is <b>told</b> rather than worked out here, and that
        /// is the whole of the ordering: the answer is latched at the instant the run became
        /// owed for, so a content push landing mid-run cannot turn a board the player was told
        /// was free into one they are charged for on the way out of it. Asking again here
        /// would be a second reading of one run's price, taken later than the first and able to
        /// disagree with it — which is the shape invariant 9a exists to refuse, and which the
        /// forfeit path next door had already got right.
        /// </para>
        /// <para>
        /// Required rather than defaulted, so a third mode has to answer it. A default would
        /// pick a price on behalf of a caller that never thought about one, and every possible
        /// default is wrong: <c>Charged</c> charges for free boards, and either free value
        /// silently turns the heart gate off for a whole mode.
        /// </para>
        ///
        /// <para>
        /// No star, no record and no reward: a defeat is not a worse clear, it simply did not
        /// happen, and <c>PlayerProgress</c> never hears about it. It still counts as a run for
        /// the chests and the streak, because it cost a heart — a daily loop that only rewards
        /// winning takes hearts from exactly the players who most need what the chests hold.
        /// </para>
        /// <para>
        /// <paramref name="stepsToSolution"/> is the near-miss reading and carries that line's
        /// promise: exact when it answers, and -1 rather than generous when it cannot.
        /// </para>
        /// </summary>
        public static LossRecord Loss(LevelDefinition level, DefeatReason reason, int moves,
                                      float seconds, int hintsUsed, int route,
                                      int stepsToSolution, int lit, int wanted, HeartPrice price)
        {
            var record = PlayerProgress.Record(level.Id);
            var tuning = level.Tuning;

            var run = RunOutcome.Loss(level.Id, reason, moves, tuning.GoldThreshold,
                                      record.BestMoves, record.Clears + 1, stepsToSolution,
                                      lit, wanted, hintsUsed, seconds, route);

            bool charged = price == HeartPrice.Charged && Wallet.TrySpendHeart();
            int left = Wallet.Hearts.Count;

            DailyChests.RecordRun();
            var streak = Record();

            LevelAnalytics.TrackDefeated(level, run.Moves, run.Seconds, left, reason.ToString());

            return new LossRecord(run, streak, left, charged, price);
        }

        /// <summary>
        /// Feeds the streak and reports what happened, so the panel that follows can say so.
        ///
        /// Measured either side of the call rather than read from an event, because the panel
        /// needs the answer synchronously — it is built on the next line — and an event handler
        /// would have to stash the result somewhere for it to be found again. Two reads of a
        /// derived number is the cheapest correct version.
        /// </summary>
        static StreakNote Record()
        {
            int before = DailyStreak.Days;
            DailyStreak.Record();
            return new StreakNote(DailyStreak.Days, DailyStreak.Days > before);
        }
    }
}
