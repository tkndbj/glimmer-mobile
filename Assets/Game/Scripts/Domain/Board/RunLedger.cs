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

            public WinRecord(RunOutcome run, StreakNote streak, long xp, long credits, int golden)
            {
                Run = run;
                Streak = streak;
                Xp = xp;
                Credits = credits;
                GoldenPercent = golden;
            }
        }

        public readonly struct LossRecord
        {
            public readonly RunOutcome Run;
            public readonly StreakNote Streak;
            public readonly int HeartsLeft;
            public readonly bool HeartCharged;

            public LossRecord(RunOutcome run, StreakNote streak, int heartsLeft, bool charged)
            {
                Run = run;
                Streak = streak;
                HeartsLeft = heartsLeft;
                HeartCharged = charged;
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
        public static WinRecord Win(LevelDefinition level, int stars, int moves, int millis,
                                    float seconds, int hintsUsed, int route, int lit, int wanted)
        {
            var before = PlayerProgress.Record(level.Id);
            var tuning = level.Tuning;

            var run = RunOutcome.Win(level.Id, stars, moves, tuning.GoldThreshold,
                                     before.BestMoves, !before.IsCleared, before.Clears + 1,
                                     lit, wanted, hintsUsed, seconds, millis, route,
                                     tuning.HasTimeLimit ? tuning.TimeLimitMillis : 0,
                                     tuning.HasTimeLimit ? tuning.TimeGoldMillis : 0);

            PlayerProgress.RecordRun(level.Id, stars, moves, run.Millis);

            // Counted here and in the loss, which are the two places a run actually ends.
            // PlayerProgress hears about wins only — a defeat is not a worse clear, it simply
            // did not happen — so there is no single hook further down to hang this on, and
            // pretending otherwise would silently stop counting losses.
            DailyChests.RecordRun();
            var streak = Record();

            var reward = PlayerProgression.RewardFor(before, PlayerProgress.Record(level.Id));

            LevelAnalytics.TrackCompleted(level, moves, stars, hintsUsed, seconds, run.FirstClear);

            return new WinRecord(run, streak, reward.Xp, reward.EarnedCredits,
                                 PlayerProgression.GoldenPercentFor(level.Id));
        }

        /// <summary>
        /// A run was lost. Charges the heart and feeds the daily loop.
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
                                      int millis, float seconds, int hintsUsed, int route,
                                      int stepsToSolution, int lit, int wanted)
        {
            var record = PlayerProgress.Record(level.Id);
            var tuning = level.Tuning;

            var run = RunOutcome.Loss(level.Id, reason, moves, tuning.GoldThreshold,
                                      record.BestMoves, record.Clears + 1, stepsToSolution,
                                      lit, wanted, hintsUsed, seconds, millis, route,
                                      tuning.HasTimeLimit ? tuning.TimeLimitMillis : 0,
                                      tuning.HasTimeLimit ? tuning.TimeGoldMillis : 0);

            bool charged = Wallet.TrySpendHeart();
            int left = Wallet.Hearts.Count;

            DailyChests.RecordRun();
            var streak = Record();

            LevelAnalytics.TrackDefeated(level, run.Moves, run.Seconds, left, reason.ToString());

            return new LossRecord(run, streak, left, charged);
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
