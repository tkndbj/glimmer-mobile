using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Modes;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// Everything a prototype mode's screen needs, which is everything except the board.
    ///
    /// <para>
    /// <b>Three screens were written before this one and they were the same file three times.</b>
    /// <c>FallScreen</c>, <c>BudScreen</c> and the retired <c>KeeperScreen</c> differ in perhaps
    /// forty lines each and agree on five hundred: how a win is recorded, how a defeat is offered
    /// a continue before it becomes a defeat, what a restart costs, what an abandonment notes,
    /// which latch holds the board while a lesson is up. Every one of those is a place where "a
    /// run is decided once" can stop being true, so every prototype mode shares one.
    /// </para>
    /// <para>
    /// <b>What a subclass supplies is four things</b>: which view to add, what its two lessons
    /// are, what its readouts are called, and the one sentence the board cannot say for itself.
    /// </para>
    /// <para>
    /// Everything about the ending goes through <c>RunLedger</c> — the record, the daily chests,
    /// the streak, the reward and the analytics — so this screen holds no second copy of what a
    /// finished run does. That is invariant 20b's whole demand of a mode: bring your own board,
    /// share the run. The five that shipped on it cost the save file no schema version, no merge
    /// rule and no server work between them, because a prototype level is an ordinary level with
    /// its own permanent id (invariant 20a) — which is also why withdrawing four of them cost
    /// none of those things either.
    /// </para>
    /// </summary>
    public abstract class ProtoScreen : ModeScreen
    {
        ProtoView _view;
        bool _finished, _closing;
        float _startedAt;
        bool _coaching;
        float _noticeUntil;

        protected ProtoLevelRules Rules => Level != null ? Level.RulesAs<ProtoLevelRules>() : null;

        /// <summary>The view this mode draws its board with. Added to the host, never pooled.</summary>
        protected abstract ProtoView Attach(GameObject host);

        /// <summary>What this mode calls the thing it is counting down to. A loc key.</summary>
        protected abstract string GoalCaption { get; }

        /// <summary>What this mode's one unexplainable refusal says. A loc key, or null for none.</summary>
        protected virtual string RefusalKey => null;

        /// <summary>The lesson about the verb, and the lesson about the companion.</summary>
        protected abstract Mechanic Verb { get; }
        protected abstract Mechanic Friend { get; }

        /// <summary>
        /// The board sits high, because these modes want room under them for the
        /// readouts and nothing between the board and the header.
        /// </summary>
        protected override Vector4 HostInset => new Vector4(24f, 250f, 24f, 330f);

        /// <summary>
        /// The top-right key pauses rather than restarting, which is every mode's rule since
        /// Lightfall: a restart deals a fresh board, so it is the cheapest way out of a run going
        /// wrong and must not sit under a thumb that is already reaching across the board.
        /// </summary>
        protected override HeaderKey RightKey => new HeaderKey("ic_pause", Pause);

        void Pause()
        {
            if (_finished || _closing) return;

            if (_view != null) _view.Locked = true;
            Flow.Modal<PauseOverlay>(v => v.Screen = this);
        }

        /// <summary>
        /// The moves this board is dealt: par plus the room it forgives.
        /// <see cref="ProtoBudget.Unlimited"/> for a board authored without one, which is how the
        /// opening board of each of these modes cannot be lost (invariant 24).
        /// </summary>
        int Budget => Level != null && Level.Tuning.HasBudget
                    ? Level.Tuning.MoveBudget : ProtoBudget.Unlimited;

        protected override void Play()
        {
            var rules = Rules;
            if (rules == null) return;

            _view = Attach(Host.gameObject);
            _view.Changed = OnChanged;
            _view.Solved = Solve;
            _view.Lost = Concede;
            _view.Committed = Commit;
            _view.Refused = Refuse;

            // The run is decided when the last goal is met and the panel arrives a beat later
            // while the celebration is still playing. Everything that could still end the run has
            // to stop at the first of those two moments - see ProtoView.Finishing.
            _view.Finishing = () => { _closing = true; Teaching.Refresh(); };

            _finished = false;
            _closing = false;

            _view.Begin(Host, rules, Budget);

            // Asked here because here is the last moment the answer is still "never": the lessons
            // run from OnPresented, a beat later, and showing one is what marks it.
            _coaching = !TipLedger.HasSeen(Verb);

            _startedAt = Time.unscaledTime;

            PlayerProgress.NoteOpened(Level.Id);
            LevelAnalytics.TrackStarted(Level, PlayerProgress.Record(Level.Id).Clears + 1);
        }

        void OnChanged()
        {
            Repaint();
            Teaching.Refresh();
        }

        /// <summary>
        /// Asks, every frame, whether this run may be under way at all — and puts the answer on
        /// the board. The board is live from the frame it exists, so without this a player could
        /// move while the iris was still opening, which is a run they were charged for and never
        /// saw begin.
        /// </summary>
        protected internal override bool Runnable => _view != null && _view.TakingInput;

        protected internal override void Running(bool running)
        {
            if (_view != null) _view.Held = !running;

            // The first frame a run is allowed to advance is the first frame after the last
            // lesson closed, which is exactly when the pointer is owed - and reading it from here
            // rather than from the tip's own dismissal means it cannot be raised over a pause
            // menu, a defeat panel or a board that is still arriving.
            if (!running || !_coaching) return;

            _coaching = false;
            if (_view != null) _view.CoachTap();
        }

        // ------------------------------------------------------------------ the one refusal
        const float NoticeHold = 3f, NoticeRest = 1f;

        /// <summary>
        /// Says the one rule this mode's board cannot show for itself.
        ///
        /// Rate-limited rather than stacked: a player tapping about would otherwise pile several
        /// copies of one sentence on top of each other, which is a screen shouting rather than a
        /// screen answering. Held off for a beat past the fade, so a second tap after the first
        /// notice has gone gets a fresh one instead of silence.
        /// </summary>
        void Refuse()
        {
            string key = RefusalKey;
            if (string.IsNullOrEmpty(key)) return;

            if (Time.unscaledTime < _noticeUntil) return;
            _noticeUntil = Time.unscaledTime + NoticeHold + NoticeRest;

            Scenery.Toast(Safe, Loc.Get(key), Pal.Cream, NoticeHold,
                          new Vector2(.5f, 1f), -(HostInset.w + 40f));
        }

        // ------------------------------------------------------------------ readouts
        /// <summary>
        /// Three numbers, and each answers a different question: how far there is to go, how much
        /// is left to go with, and how well it has gone.
        ///
        /// The allowance is the only one that is coloured, because it is the only one that can end
        /// the run — and the thresholds come from <see cref="ProtoBudget.Pressure"/> rather than
        /// from a comparison written here, so they are fractions of this board's own allowance and
        /// a test can hold them to what they claim.
        /// </summary>
        protected override void Readouts(List<Readout> into)
        {
            var run = _view != null ? _view.Run : null;

            into.Add(new Readout(Loc.Get(GoalCaption), run == null ? "0" : run.Left.ToString()));

            if (run == null || !run.Budget.Bounded)
            {
                into.Add(new Readout(Loc.Get("mode.cap.moves"), Loc.Get("mode.proto.moves_free")));
            }
            else
            {
                var tint = run.Budget.Pressure == ProtoPressure.Critical ? Pal.Ember
                         : run.Budget.Pressure == ProtoPressure.Low ? Pal.Gold
                         : Pal.Cream;

                into.Add(new Readout(Loc.Get("mode.cap.moves"), run.Budget.Left.ToString(), tint));
            }

            into.Add(new Readout(Loc.Get("mode.cap.best"), run == null ? "0" : run.Best.ToString()));
        }

        /// <summary>Which slot the allowance sits in — what a lesson about it rings.</summary>
        const int MovesReadout = 1;

        // ------------------------------------------------------------------ the stake
        protected internal override LevelId StakeLevel => Level != null ? Level.Id : LevelId.None;

        protected override bool RunOver => _finished || _closing;

        protected override void NoteAbandoned(string reason)
        {
            if (Level == null) return;

            var run = _view != null ? _view.Run : null;
            int done = run == null ? 0 : run.Goals - run.Left;

            LevelAnalytics.TrackAbandoned(Level, done, Time.unscaledTime - _startedAt, reason);
        }

        /// <summary>
        /// Puts the board back as it was authored. What a restart costs is
        /// <c>RunScreen.RestartLevel</c>'s, which asks before this runs — a mode never gets at the
        /// price.
        /// </summary>
        protected override void Rewind()
        {
            if (_view == null || RunOver) return;

            _view.Begin(Host, Rules, Budget);
            Audio.Sfx("rotate_a", .55f);

            _startedAt = Time.unscaledTime;
            ResetPlayed();
            Continue.Reset();

            Repaint();
        }

        public override void RetryAfterDefeat()
        {
            if (_view == null) return;

            _finished = false;
            _closing = false;
            Resolve();
            Continue.Reset();

            _startedAt = Time.unscaledTime;
            ResetPlayed();

            _view.Begin(Host, Rules, Budget);
            Repaint();
        }

        public override bool OnBack()
        {
            if (_finished || _closing) return false;

            LeaveToMap();
            return true;
        }

        // ------------------------------------------------------------------ one more go
        /// <summary>
        /// What this mode calls a board that has no legal move left on it.
        ///
        /// <para>
        /// <b>A hook rather than a constant, because the sentence a player reads is on the other
        /// end of it.</b> Every board on this shape reaches the same *reading* — no move left —
        /// but they reach it for reasons that want different words and that analytics has to be
        /// able to tell apart. A prototype board ran out of *board*; a Thornwatch line fell while
        /// the hill was still full, and "nothing left to do" over that reads as a bug.
        /// </para>
        /// <para>
        /// The two also differ in whether a purchase helps, which is <c>IProtoBoard.Stranded</c>'s
        /// answer and not this one's — a cairn with nothing left to pull is beyond rescue, and a
        /// fallen line is put back up by a continue. So this panel is reached on a siege only when
        /// that offer was declined, or never made because the run was free.
        /// </para>
        /// </summary>
        protected virtual DefeatReason StuckReason => DefeatReason.Stuck;

        /// <summary>A prototype board is measured in moves, so that is what a continue sells.</summary>
        protected internal override ContinueUnit MeasuredIn => ContinueUnit.Moves;

        /// <summary>
        /// The count this run is graded on.
        ///
        /// <para>
        /// <b>Moves, everywhere but one lane.</b> Every board on this shape is graded on something
        /// the player <em>spends</em>, so fewer is better and par is a floor. A run that can never
        /// be won has nothing to spend against — what it is graded on is how far it got — so a
        /// mode with such a lane answers with that instead, and the direction inverts with it
        /// (<c>LevelTuning.Climbs</c>).
        /// </para>
        /// <para>
        /// A hook rather than a branch here, because which count a mode is graded on is the mode's
        /// business and this class deliberately knows nothing about any of them.
        /// </para>
        /// </summary>
        protected virtual int Scored(ProtoRun run) => run.Spent;

        /// <summary>
        /// Called once with the graded count, before anything is recorded.
        ///
        /// The hook a mode uses to keep a reading of its own — an endless lane's high-water wave
        /// and its lifetime tally, both floors rather than grades, with nowhere else to live.
        ///
        /// <b>A mode may pay XP from in here</b>, and it does not have to say so: <see cref="Solve"/>
        /// reads <c>PlayerProgression.EndlessXp</c> either side of this call and hands the
        /// difference to the ledger. That keeps this class mode-blind — it never learns what was
        /// banked, only that the derived total moved.
        /// </summary>
        protected virtual void Finished(int count) { }

        /// <summary>
        /// Called once however the run ended, after the latch and before anything is recorded.
        ///
        /// <para>
        /// Separate from <see cref="Finished"/>, which a win alone reaches and which is handed
        /// the graded count. What a mode wants here is the other ending too — a reading taken
        /// only from runs that were won is a reading of the players who did not need it.
        /// </para>
        /// </summary>
        protected virtual void RunEnded(bool won) { }

        /// <summary>
        /// How much allowance has to be restored before a bought move is a usable move.
        ///
        /// Nought whenever an offer is honest at all, and that is not the same as "always nought".
        /// A board that has run out of allowance always has a legal move — running out of
        /// <em>board</em> is checked first and is a different ending — so any move at all is a
        /// playable move. What a shortfall would otherwise have covered is handled by refusing
        /// outright instead: a board that can be proved unfinishable is one no purchase rescues.
        /// </summary>
        protected internal override int ContinueDeficit
        {
            get
            {
                var run = _view != null ? _view.Run : null;
                if (run == null) return RunContinue.NoContinue;

                int deficit = run.Verdict.Deficit;
                return deficit == RunContinueDeficit.None ? RunContinue.NoContinue : deficit;
            }
        }

        protected internal override void ContinueWith(int moves)
        {
            if (_view == null) return;

            _view.Grant(moves);
            Audio.SfxVaried("whoosh", .45f);
        }

        // ------------------------------------------------------------------ endings
        /// <summary>
        /// The board is finished.
        ///
        /// Graded on the moves this run spent, against the same thresholds every glade uses, over
        /// a par that is the fewest moves that could have finished it.
        /// </summary>
        void Solve()
        {
            if (_finished || Level == null) return;
            _finished = true;

            RunEnded(won: true);

            Resolve();
            if (_view != null) _view.Locked = true;

            var run = _view.Run;

            int moves = Math.Max(1, Scored(run));
            int stars = Level.Tuning.StarsFor(moves);

            // Measured either side of the fold, for WinRecord.ChapterOpened's reason one line of
            // reasoning over: by the time a panel is built the transition is over, and a derived
            // total read afterwards cannot say how much of it this run put there. `Finished` is
            // where a mode banks anything it keeps, so the window is exactly that one call.
            long bonusBefore = PlayerProgression.EndlessXp;

            Finished(moves);

            long bonusXp = PlayerProgression.EndlessXp - bonusBefore;

            // No route, deliberately, and it is the weave's argument: the victory panel's route
            // bar compares a run against the board's own carved solution, and these boards have
            // many par-length answers that are equally good - so it would print the same verdict
            // for everybody. What the run carries instead is the count it was graded on.
            var done = RunLedger.Win(Level, stars, moves,
                                     Time.unscaledTime - _startedAt, 0,
                                     route: 0,
                                     lit: run.Goals, wanted: run.Goals,
                                     bonusXp: bonusXp);

            // No fanfare here: the board already played one. ProtoView.Triumph sounds `win` and
            // then waits a beat before handing control back, so a copy at this point is the same
            // clip twice a third of a second apart - a flam and 6 dB, not a bigger celebration.
            Flow.Flash(new Color(1f, .99f, .92f), .5f, .5f);
            Burst.Confetti(Content, 60);

            Flow.Modal<WinOverlay>(v =>
            {
                v.Run = done.Run;
                v.Streak = done.Streak;
                v.XpGained = done.Xp;
                v.CreditsGained = done.Credits;
                v.GoldenPercent = done.GoldenPercent;
                v.ChapterOpened = done.ChapterOpened;
            });
        }

        /// <summary>
        /// The run reached a fail state. The offer first, the defeat only if it is declined —
        /// nothing below runs until the player has said no, which is what keeps a continued run
        /// from being recorded as a loss, counted towards a chest or charged a heart.
        /// </summary>
        void Concede()
        {
            if (_finished) return;

            if (_view != null) _view.Locked = true;
            Continue.OfferOrLose(Lose);
        }

        void Lose()
        {
            var record = RecordLoss();
            if (!record.HasValue) return;

            var done = record.Value;

            Flow.Modal<DefeatOverlay>(v =>
            {
                v.Screen = this;
                v.Run = done.Run;
                v.Streak = done.Streak;
                v.HeartsLeft = done.HeartsLeft;
                v.HeartWasCharged = done.HeartCharged;
                v.Price = done.Price;
            });
        }

        RunLedger.LossRecord? RecordLoss()
        {
            if (_finished || Level == null) return null;
            _finished = true;

            RunEnded(won: false);

            Resolve();
            if (_view != null) _view.Locked = true;

            var run = _view.Run;

            // The two ways a board ends want opposite fixes, so they are told apart in the one
            // place that can still see the difference.
            var reason = run.Verdict.Ending == ProtoEnding.Stuck
                       ? StuckReason : DefeatReason.OutOfMoves;

            // No near miss. That line is measured in turns from the solution, which these boards
            // have no notion of - one is a lucky move from finished or five from it, depending on
            // nothing anybody can be told in a sentence.
            return RunLedger.Loss(Level, reason, Math.Max(1, run.Spent),
                                  Time.unscaledTime - _startedAt, 0, route: 0,
                                  stepsToSolution: 0,
                                  lit: run.Goals - run.Left, wanted: run.Goals,
                                  price: Price);
        }

        // ------------------------------------------------------------------ the lessons
        /// <summary>
        /// Everything this board brings that a player arriving from four chapters of turning
        /// conduits cannot be expected to work out, declared as facts about <em>this</em> board.
        ///
        /// <b>Declared, not shown.</b> What goes up, in what order, and whether this particular
        /// player has met any of it is <c>RunScreen</c>'s to arrange. This says only what the board
        /// holds — which is what lets the review key in the header work at all, since a list
        /// filtered by "never seen" is empty exactly when somebody asks to be reminded.
        /// </summary>
        protected internal override void Lessons(List<Lesson> into)
        {
            if (Level == null || _view == null || _view.Run == null) return;

            // The verb first. A player who has met neither this nor the allowance has to know
            // what a move does before a number counting them down can mean anything.
            var verb = _view.VerbAnchor;
            if (verb != null) into.Add(Lesson.At(Verb, verb));

            // Only on a board that can actually run out. The opening board of each of these modes
            // is authored without an allowance, and a lesson shown over a meter that is not there
            // is one that can never be shown again.
            if (_view.Run.Budget.Bounded)
                into.Add(Lesson.At(Mechanic.ProtoMoves, ReadoutAt(MovesReadout)));

            // **A mode may teach only its verb.** `Friend` used to be required, so a mode with
            // nothing worth a second lesson had to invent one - and a lesson is shown once in a
            // player's life, so an invented one is spent for ever. An empty id means there is no
            // second lesson, which is what Thornwatch answers after its own was withdrawn.
            var friend = _view.FriendAnchor;
            if (friend != null && !string.IsNullOrEmpty(Friend.Id))
                into.Add(Lesson.At(Friend, friend));
        }

        /// <summary>
        /// A lesson may go up while the board is being played on and at no other time. An
        /// animation latches the board itself and hands it back itself, so teaching over one would
        /// end with <see cref="Latch"/> unlatching a board its own animation still owns.
        /// </summary>
        protected internal override bool Teachable
            => _view != null && _view.TakingInput && !_finished && !_closing;

        /// <summary>Long enough for the board to have finished arriving.</summary>
        protected internal override float LessonDelay => ProtoView.Entrance + .15f;

        /// <summary>
        /// Holds the board while a lesson is up, and hands it back afterwards — but never to a run
        /// that ended underneath the panel.
        /// </summary>
        protected internal override void Latch(bool latched)
        {
            if (_view == null) return;
            if (!latched && (_finished || _closing)) return;

            _view.Locked = latched;
        }
    }
}
