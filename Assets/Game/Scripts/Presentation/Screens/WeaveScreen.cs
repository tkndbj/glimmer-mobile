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
    /// prototype: it costs a heart, it can be lost, and it pays stars, credits and XP.
    ///
    /// <para>
    /// Everything about the ending goes through <see cref="RunLedger"/> — the record, the daily
    /// chests, the streak, the reward and the analytics — so this screen holds no second copy of
    /// what a finished run does. That is invariant 20b's whole demand of a mode: bring your own
    /// board, share the run.
    /// </para>
    /// <para>
    /// <b>The cell count is the grade and the fail state, and it is one number.</b> A weave has
    /// no turns, so what it is measured on is the light its channels spent — a cell per cell
    /// covered — against the same three lines every glade is held to, over a par that is the sum
    /// of the pairs' own shortest routes plus a cell of looking for each decision
    /// (<c>WeaveLayout.Par</c>). A taut arrangement comes in well under and sprawl does not,
    /// which is the mode's own difficulty reading seen from the player's side (invariant 20f).
    /// </para>
    /// <para>
    /// <b>The third line is new and it is why this file grew.</b> Until it arrived a weave could
    /// not be lost at all — only forfeited — which invariant 22a wrote down as the thing to fix
    /// before the mode grew, and named the fix: a budget in the unit it is graded in, never a
    /// clock coming back. So the same <c>par × budgetFactor</c> is dealt as ink
    /// (<c>WeaveInk</c>), the readout counts it down, and the run ends the moment the grove
    /// provably cannot be finished with what is left. Two channels a grove may be handed back
    /// free; everything past that is paid for.
    /// </para>
    /// </summary>
    public sealed class WeaveScreen : ModeScreen
    {
        WeaveView _view;

        bool _finished, _closing;
        float _startedAt;

        RectTransform _notice;
        Text _noticeLine;
        int _noticeWaiting = -1;

        WeaveRules Rules => Level.RulesAs<WeaveRules>();

        /// <summary>
        /// The band under the board carries the undo key and, when the grove needs it, the
        /// standing line — so it is deeper here than the 190 this used to ask for. A permanent
        /// control the player reaches for constantly is worth the cells it costs the grove.
        ///
        /// Where the three things in that band sit is <c>WeaveBand</c>, in Domain, so that "they
        /// do not overlap" is a test rather than a paragraph. See the remarks there for why that
        /// is not over-engineering: the paragraph was wrong the first time it was written.
        /// </summary>
        protected override Vector4 HostInset
            => new Vector4(24f, WeaveBand.BoardFloor, 24f, 350f);

        /// <summary>Which slot the ink sits in — the only one, and what a lesson rings.</summary>
        const int InkReadout = 0;

        /// <summary>
        /// The top-right key pauses rather than restarting: a restart deals a fresh pot of ink,
        /// so on this mode it is the cheapest way out of a grove going wrong and must not be a
        /// single tap beside a board being dragged across. The restart is still there, one
        /// deliberate tap inside — <c>PauseOverlay</c> is entirely mode-agnostic.
        /// </summary>
        protected override HeaderKey RightKey => new HeaderKey("ic_pause", Pause);

        void Pause()
        {
            if (_finished || _closing) return;

            // Latched here and handed back by the panel's OnDestroy, which is the only way out
            // it has that every exit takes — see PauseOverlay.
            if (_view != null) _view.Locked = true;
            Flow.Modal<PauseOverlay>(v => v.Screen = this);
        }

        /// <summary>
        /// The light this grove is dealt: the ordinary <c>par × budgetFactor</c>, in cells.
        /// <c>int.MaxValue</c> — which is <c>WeaveInk.Unlimited</c> — for a grove authored
        /// without one.
        /// </summary>
        int InkBudget => Level != null ? Level.Tuning.MoveBudget : WeaveInk.Unlimited;

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
            _view.Finishing = () => { _closing = true; Teaching.Refresh(); PaintUndo(); };

            _closing = false;
            _view.Begin(Host, rules.LayoutFor(Level.Id), InkBudget);

            BuildNotice();
            BuildUndo();
            PaintNotice();
            PaintUndo();

            _startedAt = Time.unscaledTime;

            PlayerProgress.NoteOpened(Level.Id);
            LevelAnalytics.TrackStarted(Level, PlayerProgress.Record(Level.Id).Clears + 1);
        }

        void OnChanged()
        {
            Repaint();
            PaintNotice();
            PaintUndo();
            Teaching.Refresh();

            // Asked here rather than from Update, because this is raised on exactly the edges
            // that can end a run — a channel landing, a channel handed back, a restart — and a
            // poll would be asking a board that has not moved, hundreds of times a second, a
            // question that walks every pair.
            CheckLost();
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
        /// no board can demonstrate. But a lesson read once,
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
        /// <para>
        /// It sits <em>above</em> the undo key rather than beside it. Side by side is how a wide
        /// plate and a round button end up overlapping on the one aspect ratio nobody checked,
        /// and stacking costs nothing: the plate is up for a handful of seconds in a run that has
        /// one, and the key below it is the control this line is asking the player to reach for.
        /// </para>
        /// </summary>
        void BuildNotice()
        {
            if (_notice) return;

            _notice = UIKit.Box("Unfinished", Safe, new Vector2(0f, WeaveBand.NoticeHeight),
                                new Vector2(.5f, 0f), new Vector2(0f, WeaveBand.NoticeCentre));
            _notice.anchorMin = new Vector2(0f, 0f);
            _notice.anchorMax = new Vector2(1f, 0f);
            _notice.sizeDelta = new Vector2(-56f, WeaveBand.NoticeHeight);

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

        // ------------------------------------------------------------------ taking one back
        Btn _undo;
        Text _undoCount;
        bool _undoLive = true;

        /// <summary>
        /// The undo key: bottom centre, with what is left of it written on its shoulder.
        ///
        /// <para>
        /// <b>Bottom centre because it is the only control on this screen a hand reaches for
        /// mid-thought.</b> Everything else here — back, pause, the "i" — is a decision about the
        /// session, and those belong in the corners a thumb does not brush. Undo is part of
        /// playing, it is pressed with the same hand that just drew the channel it is undoing,
        /// and it is the one thing the mode now offers in place of the tap-to-erase it took away.
        /// </para>
        /// <para>
        /// <b>The badge is not decoration.</b> Two per grove is a real bound (<c>WeaveStrokes.Allowance</c>)
        /// and the difference between the second press and the third is the difference between a
        /// free correction and paying for one — so the count has to be readable before the press,
        /// not discovered by the key going dead. It is the hint pool's badge, in the same place on
        /// the same square, because a player who has met one should not have to learn the other.
        /// </para>
        /// </summary>
        void BuildUndo()
        {
            if (_undo) return;

            _undo = UIKit.IconButton("Undo", Safe, "sq_blue", "ic_undo",
                                     new Vector2(WeaveBand.UndoSize, WeaveBand.UndoSize),
                                     new Vector2(.5f, 0f),
                                     new Vector2(0f, WeaveBand.UndoCentre), Undo);

            var badge = UIKit.Img("Badge", _undo.transform, Art.Disc(64), Pal.Rose,
                                  new Vector2(58f, 58f), new Vector2(1f, 1f),
                                  new Vector2(-16f, -16f));
            _undoCount = UIKit.Titled("N", badge.transform, "0", 34, Pal.Cream,
                                      TextAnchor.MiddleCenter, outline: 0f, shadow: 2f);
        }

        void Undo()
        {
            if (_view == null || _finished || _closing) return;

            // The model owns whether this can happen, and the key is already greyed when it
            // cannot — so a refusal here is a race rather than a state the player is in, and it
            // says so quietly rather than doing nothing at all.
            if (!_view.Undo()) Audio.Sfx("blocked", .3f);
        }

        /// <summary>
        /// Greys the key when there is nothing to undo, and keeps the badge honest.
        ///
        /// Greyed rather than hidden, which is <c>RunScreen.RefreshReview</c>'s rule for the same
        /// reason: a control that comes and goes is one the player cannot learn the position of,
        /// and this one they are meant to find without looking.
        /// </summary>
        void PaintUndo()
        {
            var run = _view != null ? _view.Run : null;
            if (run == null) return;

            // Guarded against setting the same value twice, because Btn.Interactable repaints
            // the face — and this is called on every drag that lands. RunScreen.RefreshReview's
            // rule, on the control right beside it.
            bool live = run.CanUndo && !_finished && !_closing;
            if (_undo && live != _undoLive)
            {
                _undoLive = live;
                _undo.Interactable = live;
            }

            if (!_undoCount) return;

            string text = run.UndosLeft.ToString();
            if (_undoCount.text == text) return;

            _undoCount.text = text;
            Tween.Punch(_undoCount.transform, .22f, .3f);
        }

        // ------------------------------------------------------------------ the stake
        /// <summary>
        /// What this run is staked on, and how it is written down when it is walked away from.
        ///
        /// <para>
        /// Everything else about the stake — the heart, the confirmation, <c>RunGuard</c> and
        /// what a restart costs — is <see cref="RunScreen"/>'s, and it is worth saying why. This
        /// screen used to carry its own copy of all four, and its restart quietly never called
        /// them: a restart here was free, and since a restart also deals a fresh pot of ink, the
        /// mode's whole fail state could be walked out of for nothing. It was found by playing
        /// it, which is the only way it could have been found.
        /// </para>
        /// <para>
        /// The closing cascade counts as over. That second and a half after the last channel
        /// lands is a window in which the run is decided and the screen does not know it yet, so
        /// a forfeit taken during it would charge a heart for a grove already won.
        /// </para>
        /// </summary>
        protected internal override LevelId StakeLevel => Level != null ? Level.Id : LevelId.None;

        protected override bool RunOver => _finished || _closing;

        protected override void NoteAbandoned(string reason)
        {
            if (Level == null) return;

            LevelAnalytics.TrackAbandoned(Level, _view?.Run?.Joined ?? 0,
                                          Time.unscaledTime - _startedAt, reason);
        }

        // ------------------------------------------------------------------ endings
        /// <summary>
        /// Ends the run the moment the grove provably cannot be finished with the light left.
        ///
        /// <para>
        /// <b>At the moment it becomes true, not when the pot hits nought.</b> Those are
        /// different, and the gap between them is the state this mode must never produce: a
        /// player drawing and redrawing on a board that cannot be finished, with a number on
        /// screen that has not reached zero yet and nothing telling them it is over. The model
        /// answers both halves — the light left cannot cover the cheapest possible finish, or
        /// there is no channel left anywhere that could be afforded — and both are lower bounds,
        /// so a grove that could still be won is never the one that ends. See
        /// <c>WeaveRun.IsLost</c>.
        /// </para>
        /// <para>
        /// <b>Only once the run has been paid for.</b> An unbounded grove is never lost, and a
        /// board that somehow arrived unwinnable before the player had drawn anything would be a
        /// content fault — <c>WeaveMode.Validate</c> fails the build on one — and must not
        /// charge a heart for it.
        /// </para>
        /// </summary>
        void CheckLost()
        {
            var run = _view != null ? _view.Run : null;
            if (run == null) return;

            // The whole condition, in one Domain predicate that a test can hold to it. It used
            // to be three booleans in an if on this line, which is the shape this project keeps
            // paying for: every one of them is an edge where the run is decided and the screen
            // has not caught up, and a condition spread across them cannot be proved.
            if (run.Verdict.EndsTheRun(live: !RunOver, committed: Committed))
                Defeat();
        }

        /// <summary>
        /// The grove ran out of light. Charged, recorded and reported exactly as a glade's
        /// defeat is — <see cref="RunLedger.Loss"/> owns the heart, the streak, the chest count
        /// and the analytics, so there is no second copy here of what losing a run does.
        /// </summary>
        void Defeat()
        {
            if (_finished) return;

            // Locked before anything else and left locked for as long as the player is
            // deciding: this mode's board takes drags rather than taps, so a scrim is not on
            // its own enough to keep a finger off it.
            if (_view != null) _view.Locked = true;
            PaintNotice();
            PaintUndo();

            // The offer first, the defeat only if it is declined — see
            // RunContinueFlow.OfferOrLose. Nothing below runs until the player has said no, which
            // is what keeps a continued grove from being recorded as a loss, counted towards a
            // chest or charged a heart.
            Continue.OfferOrLose(Concede);
        }

        /// <summary>A weave is measured in cells of light, so that is what a continue sells.</summary>
        protected internal override ContinueUnit MeasuredIn => ContinueUnit.Ink;

        /// <summary>
        /// How much light has to be restored before a bought cell is a usable cell.
        ///
        /// <para>
        /// <b>Not nought, unlike a glade's</b>, and this is the reason the whole notion of a
        /// deficit exists. A grove is not lost when the meter reads zero — it is lost when
        /// what is left cannot cover the cheapest possible finish, so there is usually light in
        /// the pot and none of it spendable. Selling the authored twenty cells alone would put
        /// the player back on a board that is still provably unwinnable and end the run again
        /// in the same frame, having taken their gems. <c>WeaveVerdict</c> already computes
        /// both lower bounds to decide the loss; this is that same reading, kept.
        /// </para>
        /// </summary>
        protected internal override int ContinueDeficit
        {
            get
            {
                var run = _view != null ? _view.Run : null;
                return run == null ? RunContinue.NoContinue : run.Verdict.Deficit;
            }
        }

        /// <summary>
        /// The light was paid for: deal it into the pot and hand the grove back.
        ///
        /// <para>
        /// <see cref="OnChanged"/> rather than a bare repaint, deliberately. It is the edge
        /// every other change on this board raises, so the readout, the standing line, the undo
        /// key and — crucially — <see cref="CheckLost"/> all run. If a grant somehow left the
        /// grove unwinnable the run reaches its fail state again and the player is <em>asked
        /// again</em> rather than silently left on a dead board.
        /// </para>
        /// </summary>
        protected internal override void ContinueWith(int cells)
        {
            var run = _view != null ? _view.Run : null;
            if (run == null) return;

            run.Ink.Grant(cells);

            _view.Locked = false;
            Audio.SfxVaried("whoosh", .45f);

            OnChanged();
        }

        /// <summary>
        /// The grove ran out of light for good. Charged, recorded and reported exactly as a
        /// glade's defeat is — <c>RunLedger.Loss</c> owns the heart, the streak, the chest
        /// count and the analytics, so there is no second copy here of what losing a run does.
        ///
        /// This was <c>Defeat</c> in full until a lost run could be carried on; nothing in it
        /// changed, it simply runs after the offer rather than instead of one.
        /// </summary>
        void Concede()
        {
            if (_finished) return;

            _finished = true;
            Resolve();

            if (_view != null) _view.Locked = true;
            PaintNotice();
            PaintUndo();

            // Read off the board before the panel that offers a retry rebuilds it.
            var run = _view.Run;

            // No near miss, and it is not an omission. That line is measured in turns from the
            // solution, which a weave has no notion of — a grove is one drag from finished or
            // twenty depending on nothing the player can be told in a sentence — so it is left
            // at nought, where RunOutcome.NearMiss reads it as "not close" and says nothing.
            var done = RunLedger.Loss(Level, DefeatReason.OutOfInk, Math.Max(1, run.Ink.Spent),
                                      Time.unscaledTime - _startedAt, 0, route: 0,
                                      stepsToSolution: 0,
                                      lit: run.Joined, wanted: run.Pairs,
                                      price: Price);

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

        void Solve()
        {
            if (_finished) return;
            _finished = true;

            Resolve();
            if (_view != null) _view.Locked = true;
            PaintNotice();
            PaintUndo();

            // Graded on the light this run spent, against the same thresholds every glade uses,
            // over a par that is the sum of every pair's own shortest route plus a cell of
            // looking per pair and per bead (WeaveLayout.Par). A taut arrangement comes in well
            // under it and sprawl does not, which is exactly what the mode's own difficulty
            // reading measures (invariant 20f).
            //
            // It is the ink rather than `Occupied`, and the difference is only ever a redraw:
            // WeaveTests.ARunWithNoRedrawInItSpendsExactlyWhatItOccupies is that claim, pinned.
            // Note what this quietly re-points, because nothing else would say so: a weave record
            // written before the ink existed was `Occupied`, which for a run with redraws in it
            // is the smaller number — so an old record is at worst marginally harder to beat, and
            // the published deciles carry both readings until the next rebuild. Nothing earned
            // moves: stars are stored and only ever promoted, and credits derive from the star
            // ledger rather than from the count (invariant 22).
            // for a run with none they are the same number, because a channel costs a cell per
            // cell it covers. Where they part is the run that drew a channel, thought better of
            // it and drew it again — and the ink is the honest reading of that one, because the
            // light really was spent. It also means the meter on screen and the grade at the end
            // are one number rather than two that can disagree, which is what makes "keep some
            // ink back and you keep your stars" a thing a player can hold in their head.
            int cells = Math.Max(1, _view.Run.Ink.Spent);
            int stars = Level.Tuning.StarsFor(cells);

            // No route, deliberately, and it is the same argument that took "56 points" off
            // the map node. The victory panel compares a run against the board's own carved
            // solution, and a weave's is one arrangement out of many that are equally good —
            // so a route bar here would print the same verdict for every player who ever
            // finished. What the run does carry now is a real count: the cells it took, which
            // is what it was graded on and what its record and its standing are measured in.
            var done = RunLedger.Win(Level, stars, Math.Max(1, cells),
                                     Time.unscaledTime - _startedAt, 0,
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
                v.ChapterOpened = done.ChapterOpened;
            });
        }

        // ------------------------------------------------------------------ readouts
        /// <summary>
        /// One number: the light left to draw with.
        ///
        /// <para>
        /// <b>Remaining rather than spent</b>, which is the glade's rule for its move budget and
        /// is the same argument — a budget the player has to subtract in their head is not one
        /// they can plan against, and once it is low the number itself is the tension. The two
        /// readings are the same fact and only one of them can be acted on.
        /// </para>
        /// <para>
        /// <b>What it replaced.</b> A ring count, a cell count and a star projection. The rings
        /// are already said on the board, in colour, by the rings — and said again in words by
        /// the standing line on the one state where that is not enough. The projection was a pure
        /// function of the cell count sitting next to it, so two of the three numbers were one
        /// number. This is the third, inverted, and it is the only one of them that can end a
        /// run.
        /// </para>
        /// <para>
        /// The colour comes from <c>WeaveInk.Pressure</c> rather than from a threshold written
        /// here: fractions of the grove's own budget, in Domain, where a test can hold them to
        /// what they claim. A <c>switch</c> in a <c>MonoBehaviour</c> is the one place in this
        /// project nothing can be proved.
        /// </para>
        /// </summary>
        protected override void Readouts(System.Collections.Generic.List<Readout> into)
        {
            var run = _view != null ? _view.Run : null;

            if (run == null || !run.Ink.Bounded)
            {
                into.Add(new Readout(Loc.Get("mode.cap.ink"),
                                     run == null ? "0" : Loc.Get("mode.cap.ink_free")));
                return;
            }

            var tint = run.Ink.Pressure == InkPressure.Critical ? Pal.Ember
                     : run.Ink.Pressure == InkPressure.Low ? Pal.Gold
                     : Pal.Cream;

            into.Add(new Readout(Loc.Get("mode.cap.ink"), run.Ink.Left.ToString(), tint));
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
        protected internal override void Lessons(System.Collections.Generic.List<Lesson> into)
        {
            if (Level == null || _view == null || _view.Run == null) return;

            // The verb first, which is teaching order rather than a preference: a player who has
            // met none of these has to know the mode is dragged before anything on the ground can
            // mean anything at all. Which of them is new is RunScreen's question — this says only
            // what the grove holds, so a drop that puts hedges on an opening grove teaches them
            // there with nothing to remember.
            Teach(into, Mechanic.WeaveJoin);

            // Second, and only on a grove that can actually be lost. It is a rule about a number
            // in the header rather than about anything on the board, so it is asked of the level's
            // tuning the way the bead lesson is asked of the board — a grove authored without ink
            // must not spend a once-in-a-lifetime lesson teaching a meter it does not have.
            //
            // Ahead of the bead deliberately. Both are rules a board cannot show, and this is the
            // one that decides whether the run survives long enough for a ring to matter.
            if (_view.Run.Ink.Bounded) Teach(into, Mechanic.WeaveInk);

            // The ground before the things standing on it. A hedge changes where a channel may go
            // at all, so a player who has not been told what one is will read a bar on the board
            // as another thing to be threaded and spend a drag finding out — and on this mode a
            // spent drag is spent light. In practice it is the only one of the three a Wildhedge
            // player has not met, and the chapter it is introduced in opens with a single hedge
            // for exactly that reason.
            if (_view.Run.Grove.HasHedges) Teach(into, Mechanic.WeaveHedge);

            if (_view.Run.Grove.HasBeads) Teach(into, Mechanic.WeaveBead);
        }

        /// <summary>Adds one lesson with whatever demonstration this grove can support.</summary>
        void Teach(System.Collections.Generic.List<Lesson> into, Mechanic lesson)
        {
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

        /// <summary>
        /// A lesson may go up while the grove is being drawn on and at no other time. The
        /// closing sequence latches the view itself and hands nothing back, so teaching over it
        /// would unlatch a grove whose run is already decided.
        /// </summary>
        protected internal override bool Runnable
            => _view != null && !_view.Locked && !_finished && !_closing;

        protected internal override void Running(bool running)
        {
            if (_view != null) _view.Held = !running;
        }

        protected internal override bool Teachable
            => _view != null && !_view.Locked && !_finished && !_closing;

        /// <summary>Long enough for the grove to have settled under the hand about to cross it.</summary>
        protected internal override float LessonDelay => .55f;

        /// <summary>
        /// Holds the grove while a lesson is up, and hands it back afterwards — but never to a
        /// run that ended underneath the panel.
        /// </summary>
        protected internal override void Latch(bool latched)
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

            // The ink lives in the header, so what is ringed is the readout. The same answer the
            // move budget's lesson gives one screen over, and for the same reason: a lesson about
            // a number has to point at the number, or the player is left hunting the board for
            // something that was never on it.
            if (lesson.Equals(Mechanic.WeaveInk)) return ReadoutAt(InkReadout);

            // A hedge is ringed and nothing is traced, which is the one lesson here that is
            // deliberately not demonstrated. Every other trace in this mode shows a move the
            // player could make; there is no move to show for a barrier, and a hand walking round
            // one would be showing a *route* on a board whose routes are the puzzle. The board
            // demonstrates it perfectly well by itself the first time a finger is pushed at one
            // — see WeaveView.Walled, which is why that refusal had to be drawn at all.
            if (lesson.Equals(Mechanic.WeaveHedge))
            {
                for (int h = 0; h < grove.Hedges.Count; h++)
                {
                    var bar = _view.HedgeAt(h);
                    if (bar) return bar;
                }

                return null;
            }

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

            // Handed on as corners rather than cells: the stroke is drawn a leg at a time, so
            // the points between two turns would only be seams down a straight line. Pacing
            // still comes from the cell count, because that is how far the finger travels.
            var bends = grove.Corners(walk);
            if (bends.Length < 2) return null;

            // Every cell of the walk, endpoints included, so the hand steps the way a finger
            // has to. Degrades to nothing rather than to a shortcut if the board has not built:
            // a partial route would be a wrong demonstration, which is worse than no hand.
            var steps = new RectTransform[bends.Length];
            for (int i = 0; i < bends.Length; i++)
            {
                steps[i] = _view.CellAt(bends[i]);
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
        /// <summary>
        /// Puts the grove back as it was dealt — the channels, the ink and the undos, which
        /// <c>WeaveRun.Restart</c> hands back together.
        ///
        /// <para>
        /// A fresh pot of ink is exactly why a restart here has to be paid for: it is the
        /// cheapest way out of a grove going wrong, and a free one would leave the meter
        /// deciding nothing for anybody who noticed. What it costs is
        /// <c>RunScreen.RestartLevel</c>'s, which asks before this runs.
        /// </para>
        /// </summary>
        protected override void Rewind()
        {
            if (_view == null || RunOver) return;

            _view.Clear();
            Audio.Sfx("rotate_a", .55f);

            // A fresh run: the old one was just paid for, so nothing carries over into this
            // one — the play clock, and any continues bought on it. WeaveRun.Restart deals the
            // pot the level authored rather than the pot that was topped up, which is the same
            // rule from the model's side.
            _startedAt = Time.unscaledTime;
            ResetPlayed();
            Continue.Reset();

            Repaint();
            PaintNotice();
            PaintUndo();
        }

        public override void RetryAfterDefeat()
        {
            _finished = false;
            Resolve();
            Continue.Reset();
            _closing = false;
            _startedAt = Time.unscaledTime;

            _view.Begin(Host, Rules.LayoutFor(Level.Id), InkBudget);
            Repaint();
            PaintNotice();
            PaintUndo();
        }

        public override bool OnBack()
        {
            if (_finished || _closing) return false;

            LeaveToMap();
            return true;
        }
    }
}
