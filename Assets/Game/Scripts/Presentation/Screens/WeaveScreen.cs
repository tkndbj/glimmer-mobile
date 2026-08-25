using System;
using GlimmerGrove.Analytics;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Modes;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

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

        bool _committed, _finished, _closing;
        float _startedAt;
        int _paintedSeconds = -1;

        RectTransform _notice;
        Text _noticeLine;
        int _noticeWaiting = -1;

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

            // The run is decided when the last channel lands, and the panel opens a second and a
            // half later while the cascade plays. Everything that could still end the run has to
            // stop at the first of those two moments, not the second — see WeaveView.Finishing.
            _view.Finishing = () => _closing = true;

            _closing = false;
            _view.Begin(Host, rules.LayoutFor(Level.Id));

            BuildNotice();
            PaintNotice();

            _startedAt = Time.unscaledTime;
            _clock.Reset(Level.Tuning.HasTimeLimit ? Level.Tuning.TimeLimitMillis : 0);

            PlayerProgress.NoteOpened(Level.Id);
            LevelAnalytics.TrackStarted(Level, PlayerProgress.Record(Level.Id).Clears + 1);
        }

        void OnChanged()
        {
            _paintedSeconds = -1;
            Repaint();
            PaintNotice();
        }

        // ------------------------------------------------------------------ the one sentence
        /// <summary>
        /// Says, in words, that the grove is not finished — and it is what is left of the fix
        /// for the only fault in this mode a player has ever reported as a bug.
        ///
        /// <para>
        /// <b>What was wrong, and what is left of it.</b> A weave used to be won only when every
        /// critter was awake <em>and</em> no bare ground was left anywhere, and the shortest
        /// route always wakes a critter — so the ordinary way to play a grove was to drag every
        /// crystal straight at its critter, collect the mode's biggest celebration six times, and
        /// watch nothing happen. Reported exactly that way: "even though I wake up all the
        /// critters, the game doesn't end". That rule is gone, and with it almost all of this
        /// state. What can still produce it is a bead: a channel may reach its critter without
        /// being threaded through the ring it owes, and then every critter is awake and the grove
        /// is not finished.
        /// </para>
        /// <para>
        /// <b>It is far rarer and it still has to be said.</b> The difference is that the reason
        /// is now <em>on the board</em> — an unthreaded bead is a ring standing there breathing,
        /// in a colour that names whose it is — so this line points at something visible rather
        /// than being the only evidence a rule exists at all. It is kept because "nearly always
        /// obvious" is not the standard a state that reads as a broken game is held to.
        /// </para>
        /// <para>
        /// <b>Why a standing line and not a toast.</b> The rule itself is taught once, as a
        /// modal, by <see cref="Mechanic.WeaveBead"/> — that is this project's answer for a rule
        /// no board can demonstrate, and the duskcap already has it. But a lesson read once,
        /// weeks ago, is not what somebody needs at the moment the screen appears to be stuck:
        /// they need the reason on screen, now, for as long as the state lasts. So this is drawn
        /// while the state holds and taken down the instant it clears.
        /// </para>
        /// <para>
        /// <b>Where it sits.</b> In the empty band below the board, which
        /// <see cref="HostInset"/> already reserves and nothing else uses, so it costs the grove
        /// not one pixel and never covers a cell the player has to drag through. The two
        /// alternatives were both worse: over the grove it hides ground that is the very thing
        /// being asked for, and shrinking the board for a line that is usually absent taxes every
        /// run for one state.
        /// </para>
        /// </summary>
        void BuildNotice()
        {
            if (_notice) return;

            _notice = UIKit.Box("Unfinished", Safe, new Vector2(0f, 132f), new Vector2(.5f, 0f),
                                new Vector2(0f, 100f));
            _notice.anchorMin = new Vector2(0f, 0f);
            _notice.anchorMax = new Vector2(1f, 0f);
            _notice.sizeDelta = new Vector2(-56f, 132f);

            var plate = UIKit.Img("Plate", _notice, Art.Round(28),
                                  new Color(.05f, .11f, .16f, .86f));
            UIKit.StretchTo((RectTransform)plate.transform, 0, 0, 0, 0);
            plate.raycastTarget = false;

            var edge = UIKit.Img("Edge", _notice, Art.RoundOutline(28, 3f), Pal.A(Pal.Amber, .5f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);
            edge.raycastTarget = false;

            _noticeLine = UIKit.Titled("Line", _notice, "", 30, Pal.Amber,
                                       TextAnchor.MiddleCenter, outline: 3f, shadow: 3f,
                                       wrap: true);
            UIKit.StretchTo((RectTransform)_noticeLine.transform, 26, 10, 26, 10);
            _noticeLine.raycastTarget = false;

            // Once, here, and never again from PaintNotice. Best-fit rewrites fontSize to the
            // size it settled on, and Shrinkable reads fontSize as its own ceiling — so calling
            // it a second time pins the ceiling to whatever the last string happened to need,
            // and the line ratchets smaller every time the count changes. Same shape as the tab
            // row's Tween.Pop, which read a half-sprung scale as its resting size.
            UIKit.Shrinkable(_noticeLine, 20);

            _notice.gameObject.SetActive(false);
        }

        /// <summary>
        /// Shows the line while every critter is awake and ground is still bare, and hides it
        /// otherwise.
        ///
        /// <para>
        /// The count is re-read every time the board moves, but the plate is only animated in on
        /// the edge into the state — <c>GridView</c>'s <c>Show</c>/<c>Refresh</c> rule, in a
        /// place where getting it wrong would replay an entrance on every single drag.
        /// </para>
        /// </summary>
        void PaintNotice()
        {
            if (!_notice) return;

            var run = _view != null ? _view.Run : null;
            bool wanted = run != null && !_finished && !_closing
                          && run.Joined >= run.Pairs && run.BeadsLeft > 0;

            if (!wanted)
            {
                _noticeWaiting = -1;
                if (_notice.gameObject.activeSelf) _notice.gameObject.SetActive(false);
                return;
            }

            if (_noticeWaiting == run.BeadsLeft) return;

            bool arriving = _noticeWaiting < 0;
            _noticeWaiting = run.BeadsLeft;

            // Spelled out rather than built by concatenation — invariant 6, and the build gate
            // scans for key-shaped literals, so a key assembled at runtime is one it cannot see.
            if (_noticeLine)
                _noticeLine.text = _noticeWaiting == 1
                    ? Loc.Get("mode.weave.unthreaded_one")
                    : Loc.Format("mode.weave.unthreaded", _noticeWaiting);

            if (!arriving) return;

            _notice.gameObject.SetActive(true);
            _notice.localScale = Vector3.one;
            Tween.Pop(_notice, .86f, .28f);
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
            if (_view == null || _finished || _closing) return;

            // The view's latch and the run's are two different questions. The first says the
            // grove cannot be drawn on right now — a panel, a cascade, the closing sequence —
            // and is this screen's to answer. The second says this run has not been allowed to
            // begin at all: the screen is still being presented, or a first-timer is reading a
            // lesson. Tick asks that one, and nothing else here may run the clock. See
            // RunScreen.Hold.
            if (!Tick(_clock, !_view.Locked)) return;

            // Handed the elapsed time and the limit rather than the time left: Remaining answers
            // zero for an untimed grove, which is indistinguishable from "out of light" to
            // anything reading it directly. WeaveTempo.Urgency cannot make that mistake.
            _view.Urgency = WeaveTempo.Urgency(
                _clock.Millis, Level.Tuning.HasTimeLimit ? Level.Tuning.TimeLimitMillis : 0);

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
            // A grove already won is not forfeited on the way out of it, so leaving during the
            // closing cascade costs nothing and asks nothing.
            if (!_committed || _finished || _closing) { then(); return; }

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
            PaintNotice();

            // Graded on the clock alone: the move count is not a thing this mode has, so it is
            // handed the one that cannot cost a star and the time decides everything.
            int stars = Level.Tuning.StarsFor(1, _clock.Millis);

            // No route, deliberately, and it is the same argument that took "56 points" off
            // the map node. The victory panel can compare a run against the board's own solution
            // and say something kind about a near-perfect one — but a weave has no move count to
            // compare, so this mode was handing it par as both the run and the route. Every
            // player who ever finished a grove therefore got the same sentence, in a unit this
            // mode does not have: "not one turn wasted". A line identical for everybody carries
            // no information, and one measured in turns on a board that has none is worse than
            // none. A weave is graded on the clock, which the panel already shows.
            var done = RunLedger.Win(Level, stars, Math.Max(1, _view.Run.Grove.Par),
                                     _clock.Millis, Time.unscaledTime - _startedAt, 0,
                                     route: 0,
                                     lit: _view.Run.Pairs, wanted: _view.Run.Pairs);

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
            PaintNotice();

            var run = _view.Run;
            // Routeless for Solve's reason, one panel over.
            var done = RunLedger.Loss(Level, DefeatReason.OutOfTime, run.Joined, _clock.Millis,
                                      Time.unscaledTime - _startedAt, 0, route: 0,
                                      stepsToSolution: run.Pairs - run.Joined,
                                      lit: run.Joined, wanted: run.Pairs);

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
            // Whichever of the two things left to do is the harder to read off the board. A
            // grove with beads counts beads, because a channel that reached its critter without
            // being threaded looks finished from across the room and is not; a grove without them
            // counts sleeping critters, which is the only thing left to count. The readout
            // follows the board rather than the mode, so the opening rungs are not given a
            // counter that would read zero for their whole run.
            var run = _view != null ? _view.Run : null;
            bool beaded = run != null && run.Grove.HasBeads;

            leftCap = beaded ? Loc.Get("mode.cap.beads") : Loc.Get("mode.cap.asleep");
            middleCap = Loc.Get("mode.cap.time");
            rightCap = Loc.Get("mode.cap.stars");

            left = run == null ? "0"
                 : beaded ? run.BeadsLeft.ToString()
                          : (run.Pairs - run.Joined).ToString();
            middle = Level != null && Level.Tuning.HasTimeLimit
                ? RunClock.Format(Remaining) : "-";

            // Shown live, because the whole reward is speed and a player who cannot see the
            // threshold slipping has no reason to hurry.
            right = Level == null ? "0" : Level.Tuning.StarsFor(1, _clock.Millis).ToString();
        }

        // ------------------------------------------------------------------ the lessons
        /// <summary>
        /// Teaches whatever this grove brings that the player has not met, once each in their
        /// life, before they touch it.
        ///
        /// <para>
        /// <c>ModeScreen</c>'s version of this shows the level's flavour line and nothing else,
        /// which is right for a mode whose rules a board can demonstrate. This one has two it
        /// cannot: that a weave is <em>dragged</em> at all, after four chapters of tapping tiles,
        /// and what a bead is — a ring that is a doorway to one colour and a wall to every other,
        /// which a player will otherwise read as one or the other and be wrong half the time.
        /// </para>
        /// <para>
        /// <b>One at a time, and the bead one only on a grove that has beads.</b> Two modals
        /// before the first drag is a tutorial rather than a lesson, which is <c>PlayScreen</c>'s
        /// rule; and the chapter opens with two beadless groves precisely so these never land
        /// together. Asked of the board rather than of the level's position in the chapter, so a
        /// drop that puts beads on an opening grove teaches them there with nothing to remember.
        /// The tip outranks the flavour line, because the tip is the one that is only ever
        /// offered once.
        /// </para>
        /// <para>
        /// <b>Declared, not shown.</b> What goes up, in what order, with the grove latched and
        /// the clock held until the last one is closed, is <see cref="RunScreen"/>'s to arrange —
        /// this only says what there is to teach and what on the board it is about. The
        /// demonstration is resolved here because only this mode knows what a channel is; see
        /// <see cref="Demonstrate"/>.
        /// </para>
        /// </summary>
        protected override void Lessons(System.Collections.Generic.List<Lesson> into)
        {
            if (Level == null || _view == null || _view.Run == null) return;

            var lesson = Mechanic.WeaveJoin;
            bool teaching = !TipLedger.HasSeen(lesson);

            if (!teaching && _view.Run.Grove.HasBeads && !TipLedger.HasSeen(Mechanic.WeaveBead))
            {
                lesson = Mechanic.WeaveBead;
                teaching = true;
            }

            if (!teaching) return;

            var ring = Demonstrate(lesson, out var route, out var tint, out int cells);

            into.Add(new Lesson
            {
                Mechanic = lesson,
                Target = ring,
                Trace = route,
                Tint = tint,
                Cells = cells,
            });
        }

        /// <summary>Long enough for the grove to have settled under the hand about to cross it.</summary>
        protected override float LessonDelay => .55f;

        /// <summary>
        /// Holds the grove while a lesson is up, and hands it back afterwards — but never to a
        /// run that ended underneath the panel.
        /// </summary>
        protected override void Latch(bool latched)
        {
            if (_view == null) return;
            if (!latched && (_finished || _closing)) return;

            _view.Locked = latched;
        }

        /// <summary>
        /// Picks what the hand traces for a lesson, and what — if anything — is ringed.
        ///
        /// <para>
        /// The join lesson traces <see cref="WeaveLayout.CoachRoute"/>, an <b>elbow</b> between one
        /// pair's ends, and both of the things it is not were shipped first. It began as two points
        /// — the crystal and the critter — which interpolates <em>diagonally</em>, a movement this
        /// mode has no input for, shown at the exact moment a player is being taught what the input
        /// is. It then traced the generator's carved walk, which is orthogonal and legal and still
        /// wrong: that walk exists to fill the grove, so it wanders, and a fingertip zig-zagging
        /// through twenty cells teaches that this mode is fiddly rather than that it is dragged.
        /// </para>
        /// <para>
        /// The lesson is a verb, so the route is the shortest thing that shows it — press, across,
        /// one turn, arrive. Which pair, and which way the corner falls, are decided in Domain
        /// because "may this demonstration cross that cell" is a fact about a board.
        /// </para>
        /// <para>
        /// The bead lesson rings the first bead that has room to be traced through and strokes
        /// straight across it. Straight across rather than along its channel's real route for
        /// <see cref="WeaveLayout.StrokeThrough"/>'s reason, and the first bead rather than a
        /// chosen one because a grove that brings beads at all opens with them spread out — any
        /// of them teaches the same thing.
        /// </para>
        /// <para>
        /// Everything here degrades to nothing rather than to something wrong: a board with no
        /// room for a stroke, or a view that has not built, leaves the tip exactly as it was
        /// before — a sentence in a box, which is still a working lesson.
        /// </para>
        /// </summary>
        RectTransform Demonstrate(Mechanic lesson, out RectTransform[] route, out Color tint,
                                  out int cells)
        {
            route = null;
            tint = Pal.Cream;
            cells = 1;

            var run = _view != null ? _view.Run : null;
            if (run == null) return null;

            var grove = run.Grove;

            if (lesson.Equals(Mechanic.WeaveBead))
            {
                for (int b = 0; b < grove.Beads.Count; b++)
                {
                    if (!grove.StrokeThrough(b, out int from, out int to)) continue;

                    var ring = _view.BeadAt(b);
                    var a = _view.CellAt(from);
                    var z = _view.CellAt(to);
                    if (!ring || !a || !z) continue;

                    route = new[] { a, ring, z };
                    tint = Pal.EnergyColour(grove.Pairs[grove.Beads[b].Pair].Colour);
                    cells = 2;
                    return ring;
                }

                return null;
            }

            if (grove.Pairs.Count == 0) return null;

            var walk = grove.CoachRoute();
            if (walk == null || walk.Length < 2) return null;

            int chosen = Mathf.Max(0, grove.EndpointAt(walk[0]));

            // Every cell of the walk, endpoints included, so the hand steps the way a finger
            // has to. Degrades to nothing rather than to a shortcut if the board has not built:
            // a partial route would be a wrong demonstration, which is worse than no hand.
            var steps = new RectTransform[walk.Length];
            for (int i = 0; i < walk.Length; i++)
            {
                steps[i] = _view.CellAt(walk[i]);
                if (!steps[i]) return null;
            }

            route = steps;
            tint = Pal.EnergyColour(grove.Pairs[chosen].Colour);
            cells = walk.Length - 1;

            // Nothing is ringed: the lesson is the movement between two things rather than
            // either of them, and an outline round one end would say "this one".
            return null;
        }

        // ------------------------------------------------------------------ the way out
        public override void RestartLevel()
        {
            if (_view == null || _finished || _closing) return;

            _view.Clear();
            Audio.Sfx("rotate_a", .55f);
            Repaint();
            PaintNotice();
        }

        public override void RetryAfterDefeat()
        {
            _finished = false;
            _committed = false;
            _closing = false;
            _startedAt = Time.unscaledTime;

            _view.Begin(Host, Rules.LayoutFor(Level.Id));
            _clock.Reset(Level.Tuning.HasTimeLimit ? Level.Tuning.TimeLimitMillis : 0);
            Repaint();
            PaintNotice();
        }

        public override void Resume()
        {
            if (_finished || _closing || _view == null) return;
            _view.Locked = false;
        }

        public override void LeaveToMap()
            => ConfirmForfeit(ForfeitOverlay.Kind.Leave, "back", () => Flow.Go<LevelsScreen>());

        public override void LeaveToHome()
            => ConfirmForfeit(ForfeitOverlay.Kind.Leave, "home", () => Flow.Go<HomeScreen>());

        public override bool OnBack()
        {
            if (_finished || _closing) return false;

            LeaveToMap();
            return true;
        }
    }
}
