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
    /// <b>Budburst.</b> Tap a bud, it bursts, everything beside it ripens — and anything pushed
    /// past full bursts too, so one tap runs across the thicket in waves and cracks open whatever
    /// cocoons it passes.
    ///
    /// <para>
    /// Everything about being a <em>run</em> is <see cref="RunScreen"/>'s and never this file's —
    /// the heart, the stake, the pause, the forfeit and the continue. What is here is the mode:
    /// its readouts, what its record is counted in, what it teaches, and what a bought continue
    /// hands over.
    /// </para>
    /// </summary>
    public sealed class BudScreen : ModeScreen
    {
        BudView _view;
        bool _finished, _closing;
        float _startedAt;

        BudRules Rules => Level != null ? Level.RulesAs<BudRules>() : null;

        protected override Vector4 HostInset
            => new Vector4(24f, BudBand.BoardFloor, 24f, BudBand.BoardCeiling);

        protected override HeaderKey RightKey => new HeaderKey("ic_pause", Pause);

        void Pause()
        {
            if (_finished || _closing) return;

            if (_view != null) _view.Locked = true;
            Flow.Modal<PauseOverlay>(v => v.Screen = this);
        }

        int Budget => Level != null && Level.Tuning.HasBudget
                    ? Level.Tuning.MoveBudget : BudSatchel.Unlimited;

        protected override void Play()
        {
            var rules = Rules;
            if (rules == null) return;

            _view = Host.gameObject.AddComponent<BudView>();
            _view.Changed = OnChanged;
            _view.Solved = Solve;
            _view.Lost = Concede;
            _view.Committed = Commit;
            _view.Finishing = () => { _closing = true; Teaching.Refresh(); };

            _finished = false;
            _closing = false;

            _view.Begin(Host, rules.Layout, Budget);
            BuildLegend();

            _startedAt = Time.unscaledTime;
            PlayerProgress.NoteOpened(Level.Id);
            LevelAnalytics.TrackStarted(Level, PlayerProgress.Record(Level.Id).Clears + 1);
        }

        void OnChanged()
        {
            Repaint();
            Teaching.Refresh();
        }

        // ------------------------------------------------------------------ the legend
        /// <summary>
        /// The colour arithmetic, drawn above the grove and never taken away.
        ///
        /// <para>
        /// <b>Recall is not difficulty</b>, and this is that house rule for the second mode.
        /// "The colour in hand is added to the flower you tap" is one sentence and the whole
        /// game — and a player mid-grove still has to remember that the pink ones came from red
        /// and blue, which is a tax on the puzzle rather than the puzzle. Lightfall answered the
        /// same question under its tray and was reported as fixing exactly the complaint it was
        /// built for. What is left to think about here is <em>where</em>, which is the decision
        /// the mode is actually made of.
        /// </para>
        /// <para>
        /// <b>Above the grove rather than below it</b>, unlike Lightfall's, because this mode's
        /// band already carries the colour in hand: putting the recipes beside it would be two
        /// rows of coloured chips saying different things a thumb's width apart. Above the board
        /// it joins the readouts, which is a row that already exists.
        /// </para>
        /// <para>
        /// <b>The pieces are flowers, not dots.</b> Every one is the same <c>Art.Bloom</c> the
        /// grove and the band draw, at the same four sides, so the legend is made of the things
        /// it is describing — and it re-reads correctly the moment anything about how a flower
        /// is drawn changes, because there is only one answer to that question.
        /// </para>
        /// <para>
        /// Built on <see cref="Safe"/> rather than on the board host, so a restart or a retry
        /// rebuilds the grove and leaves the legend exactly where it was.
        /// </para>
        /// </summary>
        void BuildLegend()
        {
            if (_legend) return;

            _legend = UIKit.Box("Blends", Safe,
                                new Vector2(BudBand.LegendWidth, BudBand.LegendHeight),
                                new Vector2(.5f, 1f), new Vector2(0f, -BudBand.LegendCentre));
            _legend.anchorMin = _legend.anchorMax = new Vector2(.5f, 1f);

            // The container itself draws nothing. Each recipe gets its own card.
            var recipes = BudMixing.Recipes;
            for (int i = 0; i < recipes.Count; i++)
                Chip(_legend, BudBand.ChipCentre(i), recipes[i]);
        }

        RectTransform _legend;

        /// <summary>
        /// One recipe, on a card of its own: the flower on the board, the colour in hand, and
        /// what they make.
        ///
        /// <para>
        /// <b>Three cards rather than one plate, and that was reported rather than reasoned
        /// about.</b> All nine flowers sat in a single long box and it read as *confusing* —
        /// which is exactly right: nine coloured shapes and four operators inside one border are
        /// one row of thirteen things, and the eye has to work out the groupings itself every
        /// time it looks. Giving each statement its own edge does that work in the layout, so
        /// the legend is read as three facts rather than parsed as one.
        /// </para>
        /// <para>
        /// Laid out from a table of offsets in <c>BudBand</c> rather than by stacking, for
        /// <c>ReadoutRow</c>'s reason — the pieces are small and close, so whether they collide
        /// is arithmetic, and arithmetic inside a <c>MonoBehaviour</c> is arithmetic nothing can
        /// check.
        /// </para>
        /// </summary>
        void Chip(RectTransform parent, float centre, BudRecipe recipe)
        {
            var card = UIKit.Box("Chip", parent,
                                 new Vector2(BudBand.ChipPlateWidth, BudBand.ChipPlateHeight),
                                 new Vector2(.5f, .5f), new Vector2(centre, 0f));

            var plate = UIKit.Img("Plate", card, Art.Round(22),
                                  new Color(.04f, .075f, .05f, .62f));
            UIKit.StretchTo((RectTransform)plate.transform, 0f, 0f, 0f, 0f);
            plate.raycastTarget = false;

            var edge = UIKit.Img("Edge", card, Art.RoundOutline(22, 2f),
                                 new Color(.86f, 1f, .74f, .13f));
            UIKit.StretchTo((RectTransform)edge.transform, 0f, 0f, 0f, 0f);
            edge.raycastTarget = false;

            // Offsets are from the card's own centre now, not from a slot inside a long plate.
            Flower(card, BudBand.LeafX, recipe.Flower, BudBand.LeafSize);
            Glyph(card, BudBand.PlusX, "+");
            Flower(card, BudBand.HandX2, recipe.Hand, BudBand.LeafSize);
            Glyph(card, BudBand.EqualsX, "=");
            Flower(card, BudBand.MadeX, recipe.Made, BudBand.MadeSize);
        }

        /// <summary>
        /// One flower on the legend, drawn exactly as the grove draws one.
        ///
        /// The halo behind it is what stops a dark blend reading as a hole on the plate, and the
        /// bright heart is the same trick the board plays for the same reason.
        /// </summary>
        void Flower(RectTransform parent, float x, int colour, float size)
        {
            var tint = Pal.EnergyColour(colour);

            var glow = UIKit.Img("Glow", parent, Art.Glow(128, 2.2f), Pal.A(tint, .22f),
                                 Vector2.one * size * 1.55f, new Vector2(.5f, .5f),
                                 new Vector2(x, 0f));
            glow.raycastTarget = false;

            var bud = UIKit.Img("Flower", glow.transform, BudFlower.Petals(colour), tint,
                                Vector2.one * size, new Vector2(.5f, .5f), Vector2.zero);
            bud.raycastTarget = false;

            var heart = UIKit.Img("Heart", bud.transform, Art.Disc(96), Pal.Lift(tint, .55f),
                                  Vector2.one * size * .24f, new Vector2(.5f, .5f), Vector2.zero);
            heart.raycastTarget = false;
        }

        void Glyph(RectTransform parent, float x, string mark)
        {
            var text = UIKit.Titled("Op", parent, mark, 34, new Color(.92f, .96f, 1f, .55f),
                                    TextAnchor.MiddleCenter,
                                    new Vector2(BudBand.GlyphHalf * 2f, 44f),
                                    new Vector2(.5f, .5f), new Vector2(x, 0f), 0f, 0f);
            text.raycastTarget = false;
        }

        protected internal override bool Runnable => _view != null && _view.TakingInput;

        protected internal override void Running(bool running)
        {
            if (_view != null) _view.Held = !running;
        }

        protected override void Readouts(List<Readout> into)
        {
            var run = _view != null ? _view.Run : null;

            into.Add(new Readout(Loc.Get("mode.cap.critters"),
                                 run == null ? "0" : run.Left.ToString()));

            if (run == null || !run.Satchel.Bounded)
            {
                into.Add(new Readout(Loc.Get("mode.cap.taps"), Loc.Get("mode.bud.taps_free")));
            }
            else
            {
                var tint = run.Satchel.Pressure == BudPressure.Critical ? Pal.Ember
                         : run.Satchel.Pressure == BudPressure.Low ? Pal.Gold
                         : Pal.Cream;

                into.Add(new Readout(Loc.Get("mode.cap.taps"), run.Satchel.Left.ToString(), tint));
            }

            into.Add(new Readout(Loc.Get("mode.cap.chain"),
                                 run == null ? "0" : run.Best.ToString()));
        }

        /// <summary>Which readout the satchel lesson rings. The order above decides it.</summary>
        const int TapsReadout = 1;

        protected internal override LevelId StakeLevel => Level != null ? Level.Id : LevelId.None;

        protected override bool RunOver => _finished || _closing;

        protected override void NoteAbandoned(string reason)
        {
            if (Level == null) return;

            var run = _view != null ? _view.Run : null;
            int freed = run == null ? 0 : run.Critters - run.Left;

            LevelAnalytics.TrackAbandoned(Level, freed, Time.unscaledTime - _startedAt, reason);
        }

        protected override void Rewind()
        {
            if (_view == null || RunOver) return;

            _view.Begin(Host, Rules.Layout, Budget);
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

            _view.Begin(Host, Rules.Layout, Budget);
            Repaint();
        }

        public override bool OnBack()
        {
            if (_finished || _closing) return false;

            LeaveToMap();
            return true;
        }

        // ------------------------------------------------------------------ the continue
        protected internal override ContinueUnit MeasuredIn => ContinueUnit.Taps;

        /// <summary>
        /// A thicket is lost the tap its satchel empties, and one tap is a legal move again — so
        /// nothing is owed before the authored amount and the deficit is nought.
        ///
        /// The one case that answers <c>NoContinue</c> is a thicket with no bud left on it:
        /// nothing here ever grows one back, so no number of taps could reach the cocoons that
        /// remain, and selling stones for a board that cannot be finished is the one thing this
        /// offer must never do (invariant 28f).
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

        protected internal override void ContinueWith(int taps)
        {
            if (_view == null) return;

            _view.Grant(taps);
            Audio.SfxVaried("whoosh", .45f);
        }

        // ------------------------------------------------------------------ the endings
        void Solve()
        {
            if (_finished || Level == null) return;

            _finished = true;
            Resolve();

            if (_view != null) _view.Locked = true;

            var run = _view.Run;
            int taps = Math.Max(1, run.Spent);
            int stars = Level.Tuning.StarsFor(taps);

            var done = RunLedger.Win(Level, stars, taps,
                                     Time.unscaledTime - _startedAt, 0,
                                     route: 0,
                                     lit: run.Critters, wanted: run.Critters);

            Audio.Sfx("win", .9f);
            Flow.Flash(new Color(1f, .97f, .86f), .5f, .5f);
            Burst.Confetti(Content, 70);

            Flow.Modal<WinOverlay>(v =>
            {
                v.Run = done.Run;
                v.Streak = done.Streak;
                v.XpGained = done.Xp;
                v.CreditsGained = done.Credits;
                v.GoldenPercent = done.GoldenPercent;
                v.ChapterOpened = done.ChapterOpened;
            });

            // No route, deliberately, and it is Groovekeeper's argument: a route bar compares a
            // run against one arrangement out of many equally good ones, and a thicket has as
            // many shortest plays as it has orders that work.
        }

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
            Resolve();

            if (_view != null) _view.Locked = true;

            var run = _view.Run;
            var reason = run.Verdict.Ending == BudEnding.Barren
                       ? DefeatReason.Barren : DefeatReason.OutOfTaps;

            return RunLedger.Loss(Level, reason, Math.Max(1, run.Spent),
                                  Time.unscaledTime - _startedAt, 0, route: 0,
                                  stepsToSolution: 0,
                                  lit: run.Critters - run.Left, wanted: run.Critters,
                                  price: Price);
        }

        // ------------------------------------------------------------------ what it teaches
        protected internal override void Lessons(List<Lesson> into)
        {
            if (Level == null || _view == null || _view.Run == null) return;

            var run = _view.Run;

            into.Add(Lesson.At(Mechanic.BudChain, _view.ChainAnchor));

            var cocoon = _view.CocoonAnchor;
            if (cocoon != null) into.Add(Lesson.At(Mechanic.BudCocoon, cocoon));

            if (run.Satchel.Bounded)
                into.Add(Lesson.At(Mechanic.BudSatchel, ReadoutAt(TapsReadout)));
        }

        protected internal override bool Teachable
            => _view != null && _view.TakingInput && !_finished && !_closing;

        protected internal override float LessonDelay => BudTempo.Entrance + .15f;

        protected internal override void Latch(bool latched)
        {
            if (_view == null) return;
            if (!latched && (_finished || _closing)) return;

            _view.Locked = latched;
        }
    }
}
