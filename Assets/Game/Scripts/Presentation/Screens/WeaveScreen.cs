using System;
using GlimmerGrove.Analytics;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// Lightweave's screen, and the first of the new modes to be a <em>run</em> rather than a
    /// prototype: it costs a heart, it is timed, and it pays stars, credits and XP.
    ///
    /// <para>
    /// Everything about the ending goes through <see cref="RunLedger"/> — the record, the daily
    /// chests, the streak, the reward and the analytics — so this screen holds no second copy of
    /// what a finished run does. That is invariant 20b's whole demand of a mode: bring your own
    /// board, share the run.
    /// </para>
    /// <para>
    /// <b>The clock is the grade.</b> The move thresholds are switched off and the star lines are
    /// derived from the length of the grove's own solution, so a knottier board allows more time
    /// for the same three stars and nobody has to author a number. What is stored is elapsed
    /// play time, never time left — the same property <c>CountdownTests</c> protects for glades,
    /// which is why the map badge and the published deciles needed no change to accept a mode
    /// that is graded entirely on speed.
    /// </para>
    /// </summary>
    public sealed class WeaveScreen : ModeScreen
    {
        WeaveView _view;
        readonly RunClock _clock = new RunClock();

        bool _committed, _finished;
        float _startedAt;
        int _paintedSeconds = -1;

        WeaveRules Rules => Level.RulesAs<WeaveRules>();

        protected override Vector4 HostInset => new Vector4(24f, 190f, 24f, 330f);

        protected override void Play()
        {
            var rules = Rules;
            if (rules == null) return;

            _view = Host.gameObject.AddComponent<WeaveView>();
            _view.Changed = OnChanged;
            _view.Solved = Solve;
            _view.Committed = Commit;
            _view.Begin(Host, rules.LayoutFor(Level.Id));

            _startedAt = Time.unscaledTime;
            _clock.Reset(Level.Tuning.HasTimeLimit ? Level.Tuning.TimeLimitMillis : 0);

            PlayerProgress.NoteOpened(Level.Id);
            LevelAnalytics.TrackStarted(Level, PlayerProgress.Record(Level.Id).Clears + 1);
        }

        void OnChanged()
        {
            _paintedSeconds = -1;
            Repaint();
        }

        // ------------------------------------------------------------------ the clock
        /// <summary>
        /// Time accrues while the board can actually be acted on, so a panel over the top costs
        /// nothing and a backgrounded app contributes nothing because no frames run.
        ///
        /// It starts when the grove becomes playable rather than on the first line drawn: a
        /// countdown a player can hold at full by not touching anything lets them plan the whole
        /// route for free and then trace it, which removes exactly the pressure the limit is for.
        /// </summary>
        void Update()
        {
            if (_view == null || _finished) return;
            if (_view.Locked) return;

            if (!_clock.HasStarted) _clock.Start();
            _clock.Advance(Time.unscaledDeltaTime);

            int seconds = Remaining / 1000;
            if (seconds != _paintedSeconds)
            {
                _paintedSeconds = seconds;
                Repaint();
            }

            if (Level.Tuning.HasTimeLimit && _clock.Millis >= Level.Tuning.TimeLimitMillis)
                TimeUp();
        }

        int Remaining
        {
            get
            {
                if (!Level.Tuning.HasTimeLimit) return 0;

                int left = Level.Tuning.TimeLimitMillis - _clock.Millis;
                return left < 0 ? 0 : left;
            }
        }

        // ------------------------------------------------------------------ the stake
        /// <summary>
        /// Noted on disk the moment the first channel lands, so the process dying does not make
        /// the run free. See <see cref="RunGuard"/> — <c>Boot</c> charges anything still written
        /// down at the next launch.
        /// </summary>
        void Commit()
        {
            if (_committed || Level == null) return;

            _committed = true;
            RunGuard.Begin(Level.Id);
        }

        void Resolve()
        {
            _committed = false;
            RunGuard.Resolve();
        }

        /// <summary>
        /// The player walked away from a run that had begun. It costs exactly what losing it
        /// costs, because that is what it is — and note what it does not do: a forfeit feeds
        /// neither the chests nor the streak, which are for runs that were finished.
        /// </summary>
        void Forfeit(string reason)
        {
            if (!_committed) return;

            LevelAnalytics.TrackAbandoned(Level, _view?.Run?.Joined ?? 0,
                                          Time.unscaledTime - _startedAt, reason);
            Wallet.TrySpendHeart();
            Resolve();
        }

        void ConfirmForfeit(ForfeitOverlay.Kind kind, string reason, Action then)
        {
            if (!_committed || _finished) { then(); return; }

            if (_view != null) _view.Locked = true;

            Flow.Modal<ForfeitOverlay>(v =>
            {
                v.Choice = kind;
                v.OnConfirm = () => { Forfeit(reason); then(); };
                v.OnCancel = Resume;
            });
        }

        // ------------------------------------------------------------------ endings
        void Solve()
        {
            if (_finished) return;
            _finished = true;

            Resolve();
            _clock.Stop();
            if (_view != null) _view.Locked = true;

            // Graded on the clock alone: the move count is not a thing this mode has, so it is
            // handed the one that cannot cost a star and the time decides everything.
            int stars = Level.Tuning.StarsFor(1, _clock.Millis);

            var done = RunLedger.Win(Level, stars, Math.Max(1, _view.Run.Grove.SolutionLength),
                                     _clock.Millis, Time.unscaledTime - _startedAt, 0,
                                     _view.Run.Grove.SolutionLength,
                                     _view.Run.Pairs, _view.Run.Pairs);

            Audio.Sfx("win", .9f);
            Flow.Flash(new Color(1f, .99f, .92f), .5f, .5f);
            Burst.Confetti(Content, 60);

            Flow.Modal<WinOverlay>(v =>
            {
                v.Run = done.Run;
                v.Streak = done.Streak;
                v.XpGained = done.Xp;
                v.CreditsGained = done.Credits;
                v.GoldenPercent = done.GoldenPercent;
            });
        }

        void TimeUp()
        {
            if (_finished) return;
            _finished = true;

            Resolve();
            _clock.Stop();
            if (_view != null) _view.Locked = true;

            var run = _view.Run;
            var done = RunLedger.Loss(Level, DefeatReason.OutOfTime, run.Joined, _clock.Millis,
                                      Time.unscaledTime - _startedAt, 0,
                                      run.Grove.SolutionLength,
                                      run.Pairs - run.Joined, run.Joined, run.Pairs);

            Flow.Modal<DefeatOverlay>(v =>
            {
                v.Screen = this;
                v.Run = done.Run;
                v.Streak = done.Streak;
                v.HeartsLeft = done.HeartsLeft;
                v.HeartWasCharged = done.HeartCharged;
            });
        }

        // ------------------------------------------------------------------ readouts
        protected override void Readouts(out string leftCap, out string left, out string middleCap,
                                         out string middle, out string rightCap, out string right)
        {
            leftCap = Loc.Get("mode.cap.joined");
            middleCap = Loc.Get("mode.cap.time");
            rightCap = Loc.Get("mode.cap.stars");

            var run = _view != null ? _view.Run : null;
            left = run == null ? "0" : $"{run.Joined}/{run.Pairs}";
            middle = Level != null && Level.Tuning.HasTimeLimit
                ? RunClock.Format(Remaining) : "-";

            // Shown live, because the whole reward is speed and a player who cannot see the
            // threshold slipping has no reason to hurry.
            right = Level == null ? "0" : Level.Tuning.StarsFor(1, _clock.Millis).ToString();
        }

        // ------------------------------------------------------------------ the way out
        public override void RestartLevel()
        {
            if (_view == null || _finished) return;

            _view.Clear();
            Audio.Sfx("rotate_a", .55f);
            Repaint();
        }

        public override void RetryAfterDefeat()
        {
            _finished = false;
            _committed = false;
            _startedAt = Time.unscaledTime;

            _view.Begin(Host, Rules.LayoutFor(Level.Id));
            _clock.Reset(Level.Tuning.HasTimeLimit ? Level.Tuning.TimeLimitMillis : 0);
            Repaint();
        }

        public override void Resume()
        {
            if (_finished || _view == null) return;
            _view.Locked = false;
        }

        public override void LeaveToMap()
            => ConfirmForfeit(ForfeitOverlay.Kind.Leave, "back", () => Flow.Go<LevelsScreen>());

        public override void LeaveToHome()
            => ConfirmForfeit(ForfeitOverlay.Kind.Leave, "home", () => Flow.Go<HomeScreen>());

        public override bool OnBack()
        {
            if (_finished) return false;

            LeaveToMap();
            return true;
        }
    }
}
