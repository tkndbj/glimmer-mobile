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
    /// Groovekeeper's screen, and the third of the new modes to become a <em>run</em> rather than
    /// a prototype: it has a goal, it costs a heart, it can be lost two ways, and it pays stars,
    /// credits and XP.
    ///
    /// <para>
    /// <b>What it was.</b> An endless score attack — random colours onto empty ground until the
    /// tiles ran out. No goal, so nothing to reach; no par, so nothing to grade; no fixed future,
    /// so nothing a validator could prove and no ladder a chapter could climb. It is now a grove
    /// with beds that have to be bloomed and an authored procession to bloom them with, and
    /// everything graded derives from a search over the two (see <c>KeeperSolver</c>).
    /// </para>
    /// <para>
    /// <b>Everything about the ending goes through <see cref="RunLedger"/></b> — the record, the
    /// daily chests, the streak, the reward and the analytics — so this screen holds no second
    /// copy of what a finished run does. That is invariant 20b's whole demand of a mode: bring
    /// your own board, share the run. Adding it cost the save file no schema version, no merge
    /// rule and no server work, because a Groovekeeper level is an ordinary level with its own
    /// permanent id (invariant 20a).
    /// </para>
    /// <para>
    /// <b>Two fail states, and only one of them may be sold a continue.</b> Running out of tiles
    /// is a shortage, so more tiles fix it; a grove with nowhere left to grow is not, and no
    /// number of tiles gives it somewhere to plant. So <see cref="ContinueDeficit"/> answers
    /// <c>NoContinue</c> for that ending — the honest answer, and the one invariant 23 sanctions.
    /// It also means the mistake money cannot fix is the spatial one, which is the half this mode
    /// is actually about.
    /// </para>
    /// </summary>
    public sealed class KeeperScreen : ModeScreen
    {
        KeeperView _view;
        bool _finished, _closing;
        float _startedAt;

        /// <summary>
        /// Whether this run still owes a first-timer the pointer at the opening bed, and the
        /// moment the last notice was raised. See <see cref="Unreachable"/> and
        /// <see cref="Running"/>.
        /// </summary>
        bool _coaching;
        float _noticeUntil;

        KeeperRules Rules => Level != null ? Level.RulesAs<KeeperRules>() : null;

        /// <summary>
        /// The board's floor, read from <c>KeeperBand</c> rather than repeated here, so the basket
        /// below it and the grove above it cannot come to disagree about where the floor is —
        /// which is exactly how a carefully measured band ends up under a tray.
        /// </summary>
        protected override Vector4 HostInset
            => new Vector4(24f, KeeperBand.BoardFloor, 24f, 350f);

        /// <summary>
        /// The top-right key pauses rather than restarting, which is Lightfall's rule for
        /// Lightfall's reason: a restart deals a fresh basket <em>and</em> puts the ground back, so
        /// it is by some distance the cheapest way out of a run going wrong and must not sit under
        /// a thumb that is already reaching across the board. The restart is still there, one
        /// deliberate tap inside — <c>PauseOverlay</c> is mode-agnostic.
        /// </summary>
        protected override HeaderKey RightKey => new HeaderKey("ic_pause", Pause);

        void Pause()
        {
            if (_finished || _closing) return;

            // Latched here and handed back by the panel's OnDestroy, which is the only way out it
            // has that every exit takes.
            if (_view != null) _view.Locked = true;
            Flow.Modal<PauseOverlay>(v => v.Screen = this);
        }

        /// <summary>
        /// The tiles this grove is dealt: par plus the room it forgives, counted in tiles.
        /// <see cref="KeeperBasket.Unlimited"/> for a grove authored without one, which is how the
        /// first level of the chapter cannot be lost.
        /// </summary>
        int Budget => Level != null && Level.Tuning.HasBudget
                    ? Level.Tuning.MoveBudget : KeeperBasket.Unlimited;

        protected override void Play()
        {
            var rules = Rules;
            if (rules == null) return;

            _view = Host.gameObject.AddComponent<KeeperView>();
            _view.Changed = OnChanged;
            _view.Solved = Solve;
            _view.Lost = Concede;
            _view.Committed = Commit;
            _view.Unreachable = Unreachable;

            // The run is decided when the last bed opens and the panel arrives a beat later while
            // the flowers are still opening. Everything that could still end the run has to stop
            // at the first of those two moments — see KeeperView.Finishing.
            _view.Finishing = () => { _closing = true; Teaching.Refresh(); };

            _finished = false;
            _closing = false;

            _view.Begin(Host, rules.Layout, Budget);

            // Asked here because here is the last moment the answer is still "never": the
            // lessons run from OnPresented, a beat later, and showing one is what marks it.
            // A player meeting the bloom rule for the first time is by construction on their
            // first grove of this mode, so this needs no reading of chapter order to say so.
            _coaching = !TipLedger.HasSeen(Mechanic.KeeperBloom);

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
        /// Asks, every frame, whether this run may be under way at all — and puts the answer on the
        /// board.
        ///
        /// What it buys here is the window between the board being built and the first lesson going
        /// up. The board is live from the frame it exists, so without this a player could lay a
        /// tile while the iris was still opening, or in the beat before a first-timer's tip
        /// arrives — which is a run they were charged for and never saw begin.
        /// </summary>
        protected internal override bool Runnable => _view != null && _view.TakingInput;

        protected internal override void Running(bool running)
        {
            if (_view != null) _view.Held = !running;

            // The first frame a run is allowed to advance is the first frame after the last
            // lesson closed, which is exactly when the pointer is owed — and reading it from
            // here rather than from the tip's own dismissal means it cannot be raised over a
            // pause menu, a defeat panel or a board that is still arriving. All three are
            // states where this run is not running.
            if (!running || !_coaching) return;

            _coaching = false;
            if (_view != null) _view.CoachTap();
        }

        // ------------------------------------------------------------------ the one refusal
        /// <summary>How long the notice stands, and the beat before a second tap may raise another.</summary>
        const float NoticeHold = 3f, NoticeRest = 1f;

        /// <summary>
        /// Says the rule the board cannot show: a tile goes beside the groove, never on its own.
        ///
        /// <para>
        /// Raised only for that one refusal — every other way a tap is turned down is written on
        /// the cell that turned it down, and <c>KeeperBoard.Adrift</c> is what tells them apart.
        /// The bloom lesson has always said it, but a lesson is offered once in a player's life
        /// and this is the moment they are asking.
        /// </para>
        /// <para>
        /// Rate-limited rather than stacked: a player tapping about the board would otherwise
        /// pile several copies of one sentence on top of each other, which is a screen shouting
        /// rather than a screen answering. Held off for a beat past the fade, so a second tap
        /// after the first notice has gone gets a fresh one instead of silence.
        /// </para>
        /// <para>
        /// Placed from the top of the <em>board's own room</em> rather than from a typed centre,
        /// which is what keeps it clear of the readouts above it by construction: the board
        /// begins where they stop. See <c>KeeperBand.NoticeDrop</c>.
        /// </para>
        /// </summary>
        void Unreachable()
        {
            if (Time.unscaledTime < _noticeUntil) return;
            _noticeUntil = Time.unscaledTime + NoticeHold + NoticeRest;

            Scenery.Toast(Safe, Loc.Get("mode.keeper.adrift"), Pal.Cream, NoticeHold,
                          new Vector2(.5f, 1f), -(HostInset.w + KeeperBand.NoticeDrop));
        }

        // ------------------------------------------------------------------ readouts
        /// <summary>
        /// Three numbers, and each answers a different question: how far there is to go, how much
        /// is left to go with, and how well it has gone.
        ///
        /// <para>
        /// The basket is the only one that is coloured, because it is the only one that can end the
        /// run — and the thresholds come from <c>KeeperBasket.Pressure</c> rather than from a
        /// comparison written here, so they are fractions of this grove's own basket and a test can
        /// hold them to what they claim. The other fail state has no number at all: it is the
        /// ground running out, which is on the board where a rule the board can show belongs.
        /// </para>
        /// </summary>
        protected override void Readouts(List<Readout> into)
        {
            var run = _view != null ? _view.Run : null;

            into.Add(new Readout(Loc.Get("mode.cap.beds"),
                                 run == null ? "0" : run.Left.ToString()));

            if (run == null || !run.Basket.Bounded)
            {
                into.Add(new Readout(Loc.Get("mode.cap.tiles"),
                                     Loc.Get("mode.keeper.basket_free")));
            }
            else
            {
                var tint = run.Basket.Pressure == KeeperPressure.Critical ? Pal.Ember
                         : run.Basket.Pressure == KeeperPressure.Low ? Pal.Gold
                         : Pal.Cream;

                into.Add(new Readout(Loc.Get("mode.cap.tiles"), run.Basket.Left.ToString(), tint));
            }

            into.Add(new Readout(Loc.Get("mode.cap.flourish"),
                                 run == null ? "0" : run.Best.ToString()));
        }

        /// <summary>Which slot the basket sits in — what a lesson about it rings.</summary>
        const int BasketReadout = 1;

        // ------------------------------------------------------------------ the stake
        protected internal override LevelId StakeLevel => Level != null ? Level.Id : LevelId.None;

        protected override bool RunOver => _finished || _closing;

        protected override void NoteAbandoned(string reason)
        {
            if (Level == null) return;

            var run = _view != null ? _view.Run : null;
            int opened = run == null ? 0 : run.Beds - run.Left;

            LevelAnalytics.TrackAbandoned(Level, opened, Time.unscaledTime - _startedAt, reason);
        }

        /// <summary>
        /// Puts the grove back as it was authored.
        ///
        /// A fresh basket and bare ground is exactly why a restart here has to be paid for. What it
        /// costs is <c>RunScreen.RestartLevel</c>'s, which asks before this runs — a mode never
        /// gets at the price.
        /// </summary>
        protected override void Rewind()
        {
            if (_view == null || RunOver) return;

            _view.Begin(Host, Rules.Layout, Budget);
            Audio.Sfx("rotate_a", .55f);

            // A fresh run: the old one has just been paid for, so nothing carries over — not the
            // play clock, and not any continues bought on it.
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

            _view.Begin(Host, Rules.Layout, Budget);
            Repaint();
        }

        public override bool OnBack()
        {
            if (_finished || _closing) return false;

            LeaveToMap();
            return true;
        }

        // ------------------------------------------------------------------ one more go
        /// <summary>A grove is measured in tiles, so that is what a continue sells.</summary>
        protected internal override ContinueUnit MeasuredIn => ContinueUnit.Tiles;

        /// <summary>
        /// How much basket has to be restored before a bought tile is a usable tile.
        ///
        /// <para>
        /// Nought whenever an offer is honest at all, and that is not the same as "always
        /// nought". A grove that has simply run out always has somewhere to plant — running out of
        /// <em>room</em> is checked first and is a different ending — so any tile at all is a
        /// playable tile. What a shortfall would otherwise have covered is handled by refusing
        /// outright instead: a grove with a bed that can be proved unopenable is one no purchase
        /// rescues, and <c>KeeperVerdict</c> answers <c>NoContinue</c> for it.
        /// </para>
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

        /// <summary>
        /// The tiles were paid for: deal them and hand the grove back.
        ///
        /// The view raises <see cref="OnChanged"/> and re-reads its own verdict, so if a grant
        /// somehow left the run lost the fail state fires again and the player is <em>asked
        /// again</em> rather than silently left on a dead board.
        /// </summary>
        protected internal override void ContinueWith(int tiles)
        {
            if (_view == null) return;

            _view.Grant(tiles);
            Audio.SfxVaried("whoosh", .45f);
        }

        // ------------------------------------------------------------------ endings
        /// <summary>
        /// Every bed is open.
        ///
        /// Graded on the tiles this run spent, against the same thresholds every glade uses, over a
        /// par that is the fewest tiles that could have opened them. A tidy run comes in well under
        /// and flailing does not, which is the mode's own difficulty reading seen from the player's
        /// side.
        /// </summary>
        void Solve()
        {
            if (_finished || Level == null) return;
            _finished = true;

            Resolve();
            if (_view != null) _view.Locked = true;

            var run = _view.Run;

            int tiles = Math.Max(1, run.Spent);
            int stars = Level.Tuning.StarsFor(tiles);

            // No route, deliberately, and it is the weave's argument: the victory panel's route bar
            // compares a run against the board's own carved solution, and a grove has many
            // par-length answers that are equally good — so it would print the same verdict for
            // everybody. What the run carries instead is the count it was graded on.
            var done = RunLedger.Win(Level, stars, tiles,
                                     Time.unscaledTime - _startedAt, 0,
                                     route: 0,
                                     lit: run.Beds, wanted: run.Beds);

            // No fanfare here: the board already played one. `*View.Triumph` sounds `win`
            // and then waits a beat before handing control back, so a copy at this point is
            // the same clip twice a third of a second apart - which is a flam and 6 dB, not a
            // bigger celebration. It is the fault `FallView.Overflow` names for the losing
            // path ("a flood arrived as two crashes") on the winning one, and the house rule
            // is to celebrate once: the glade has only ever sounded it from `BoardView`, and
            // `WinOverlay` deliberately adds nothing.
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
        /// The run reached a fail state. The offer first, the defeat only if it is declined — see
        /// <c>RunContinueFlow.OfferOrLose</c>. Nothing below runs until the player has said no,
        /// which is what keeps a continued run from being recorded as a loss, counted towards a
        /// chest or charged a heart.
        /// </summary>
        void Concede()
        {
            if (_finished) return;

            if (_view != null) _view.Locked = true;
            Continue.OfferOrLose(Lose);
        }

        /// <summary>
        /// The run is lost for good. Charged, recorded and reported exactly as a glade's defeat is —
        /// <c>RunLedger.Loss</c> owns the heart, the streak, the chest count and the analytics, so
        /// there is no second copy here of what losing a run does.
        /// </summary>
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

        /// <summary>
        /// Writes the run off, and hands back what happened so a caller can show it — or not.
        ///
        /// Split from the panel because two exits share it and only one of them raises anything.
        /// </summary>
        RunLedger.LossRecord? RecordLoss()
        {
            if (_finished || Level == null) return null;
            _finished = true;

            Resolve();
            if (_view != null) _view.Locked = true;

            var run = _view.Run;

            // The two ways a grove ends want opposite fixes, so they are told apart in the one
            // place that can still see the difference. See DefeatReason.
            var reason = run.Verdict.Ending == KeeperEnding.Overgrown
                       ? DefeatReason.Overgrown : DefeatReason.OutOfTiles;

            // No near miss. That line is measured in turns from the solution, which a grove has no
            // notion of — a board is one lucky tile from finished or five from it, depending on
            // nothing anybody can be told in a sentence — so it is left at nought, where
            // RunOutcome.NearMiss reads it as "not close" and says nothing.
            return RunLedger.Loss(Level, reason, Math.Max(1, run.Spent),
                                  Time.unscaledTime - _startedAt, 0, route: 0,
                                  stepsToSolution: 0,
                                  lit: run.Beds - run.Left, wanted: run.Beds,
                                  price: Price);
        }

        // ------------------------------------------------------------------ the lessons
        /// <summary>
        /// Everything this grove brings that a player arriving from four chapters of turning tiles
        /// cannot be expected to work out, declared as facts about <em>this</em> board.
        ///
        /// <para>
        /// <b>Declared, not shown.</b> What goes up, in what order, and whether this particular
        /// player has met any of it is <c>RunScreen</c>'s to arrange. This says only what the board
        /// holds — which is what lets the review key in the header work at all, since a list
        /// filtered by "never seen" is empty exactly when somebody asks to be reminded.
        /// </para>
        /// <para>
        /// <b>Each one is conditional on something real, and that is what stops this being a
        /// tutorial.</b> The bloom lesson is on every grove because every grove is made of tiles.
        /// The basket lesson is only on a grove that can actually run out, so the first level of
        /// the chapter — authored without one, exactly as the first glade and the first well are —
        /// does not spend a once-in-a-lifetime lesson teaching a meter it does not have. The rest
        /// wait for the board that first carries the thing they are about.
        /// </para>
        /// </summary>
        protected internal override void Lessons(List<Lesson> into)
        {
            if (Level == null || _view == null || _view.Run == null) return;

            var run = _view.Run;
            var layout = run.Layout;

            // The verb first. A player who has met neither this nor the basket has to know what a
            // tile does before a number counting them down can mean anything.
            into.Add(Lesson.At(Mechanic.KeeperBloom, _view.BedAnchor));

            if (run.Basket.Bounded)
                into.Add(Lesson.At(Mechanic.KeeperBasket, ReadoutAt(BasketReadout)));

            var stone = _view.StoneAnchor;
            if (stone != null) into.Add(Lesson.At(Mechanic.KeeperStone, stone));

            // Composting immediately before heartbeds, and only on a grove that has one. The two
            // are one idea in the wrong order otherwise — a bed that refuses every colour but its
            // own is alarming until you already know the procession can be moved on — and a
            // lesson about a key nothing on the board makes necessary is one that can never be
            // spent again (the basket lessons argument, for a control rather than a meter).
            var heart = _view.HeartbedAnchor;
            if (heart != null) into.Add(Lesson.At(Mechanic.KeeperCompost, _view.CompostAnchor));
            if (heart != null) into.Add(Lesson.At(Mechanic.KeeperHeartbed, heart));

            if (layout.Deal.Prisms > 0)
                into.Add(Lesson.At(Mechanic.KeeperPrism, _view.PrismAnchor));
        }

        /// <summary>
        /// A lesson may go up while the grove is being played on and at no other time. A cascade
        /// latches the board itself and hands it back itself, so teaching over one would end with
        /// <c>Latch</c> unlatching a board its own animation still owns.
        /// </summary>
        protected internal override bool Teachable
            => _view != null && _view.TakingInput && !_finished && !_closing;

        /// <summary>Long enough for the grove to have finished arriving.</summary>
        protected internal override float LessonDelay => KeeperTempo.Entrance + .15f;

        /// <summary>
        /// Holds the grove while a lesson is up, and hands it back afterwards — but never to a run
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
