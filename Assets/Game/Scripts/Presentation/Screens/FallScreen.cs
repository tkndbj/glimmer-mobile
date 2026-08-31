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
    /// Lightfall's screen, and the second of the new modes to become a <em>run</em> rather than
    /// a prototype: it has a goal, it costs a heart, it can be lost two ways, and it pays stars,
    /// credits and XP.
    ///
    /// <para>
    /// <b>What it was.</b> An endless score attack — random colours into an empty well until a
    /// column filled up. No goal, so nothing to reach; no par, so nothing to grade; no fixed
    /// future, so nothing a validator could prove and no ladder a chapter could climb. It is now
    /// a well that starts full and has to be emptied, with an authored procession to empty it
    /// with, and everything graded derives from a search over the two (see <c>FallSolver</c>).
    /// </para>
    /// <para>
    /// <b>Everything about the ending goes through <see cref="RunLedger"/></b> — the record, the
    /// daily chests, the streak, the reward and the analytics — so this screen holds no second
    /// copy of what a finished run does. That is invariant 20b's whole demand of a mode: bring
    /// your own board, share the run. Adding it cost the save file no schema version, no merge
    /// rule and no server work, because a Lightfall level is an ordinary level with its own
    /// permanent id (invariant 20a).
    /// </para>
    /// <para>
    /// <b>Two fail states, and only one of them may be sold a continue.</b> Running out of motes
    /// is a shortage, so more motes fix it; a flooded well is not, and no amount of supply
    /// empties a well that has already reached its brim. So <see cref="ContinueDeficit"/>
    /// answers <c>NoContinue</c> for a flood — the honest answer, and the one invariant 23
    /// sanctions. It also means the mistake money cannot fix is the spatial one, which is the
    /// half this mode is actually about.
    /// </para>
    /// </summary>
    public sealed class FallScreen : ModeScreen
    {
        FallView _view;
        bool _finished, _closing;
        float _startedAt;

        FallRules Rules => Level != null ? Level.RulesAs<FallRules>() : null;

        /// <summary>
        /// The board's floor, read from <c>FallBand</c> rather than repeated here, so the legend
        /// below it and the well above it cannot come to disagree about where the floor is —
        /// which is exactly how a carefully measured band ends up under a tray.
        /// </summary>
        protected override Vector4 HostInset
            => new Vector4(24f, FallBand.BoardFloor, 24f, 350f);

        /// <summary>
        /// The top-right key pauses rather than restarting, which is Lightweave's rule for
        /// Lightweave's reason: a restart deals a fresh supply <em>and</em> puts the well back
        /// under its brim, so it is by some distance the cheapest way out of a run going wrong
        /// and must not sit under a thumb that is already reaching across the board. The restart
        /// is still there, one deliberate tap inside — <c>PauseOverlay</c> is mode-agnostic.
        /// </summary>
        protected override HeaderKey RightKey => new HeaderKey("ic_pause", Pause);

        void Pause()
        {
            if (_finished || _closing) return;

            // Latched here and handed back by the panel's OnDestroy, which is the only way out
            // it has that every exit takes.
            if (_view != null) _view.Locked = true;
            Flow.Modal<PauseOverlay>(v => v.Screen = this);
        }

        /// <summary>
        /// The motes this well is dealt: the ordinary <c>par x budgetFactor</c>, counted in
        /// drops. <see cref="FallSupply.Unlimited"/> for a well authored without one, which is
        /// how the first level of the chapter cannot be lost.
        /// </summary>
        int Budget => Level != null && Level.Tuning.HasBudget
                    ? Level.Tuning.MoveBudget : FallSupply.Unlimited;

        protected override void Play()
        {
            var rules = Rules;
            if (rules == null) return;

            _view = Host.gameObject.AddComponent<FallView>();
            _view.Changed = OnChanged;
            _view.Solved = Solve;
            _view.Lost = Concede;
            _view.Committed = Commit;

            // The run is decided when the last mote bursts and the panel opens a beat later
            // while the chain plays out. Everything that could still end the run has to stop at
            // the first of those two moments — see FallView.Finishing.
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

        /// <summary>
        /// Asks, every frame, whether this run may be under way at all — and puts the answer on
        /// the board.
        ///
        /// <para>
        /// <b>Through <c>Tick</c> rather than by reading the hold directly</b>, because that is
        /// the funnel the question is supposed to go through: a mode cannot let a run advance
        /// without it being asked. It had two callers while both the glade and the weave ran
        /// clocks and has had one since the countdown went, so the guarantee it exists to give
        /// was being given by one mode out of four — <c>CLAUDE.md</c> says to put a mode back
        /// through it before adding anything that reads it, and this is a mode being added.
        /// </para>
        /// <para>
        /// What it buys here is the window between the board being built and the first lesson
        /// going up. The board is live from the frame it exists, so without this a player could
        /// drop a mote while the iris was still opening, or in the beat before a first-timer's
        /// tip arrives — which is a run they were charged for and never saw begin.
        /// </para>
        /// </summary>
        protected internal override bool Runnable => _view != null && _view.TakingInput;

        protected internal override void Running(bool running)
        {
            if (_view != null) _view.Held = !running;
        }

        // ------------------------------------------------------------------ the legend
        /// <summary>
        /// The colour arithmetic, drawn under the tray, permanently.
        ///
        /// <para>
        /// <b>Recall is not difficulty.</b> "A mote adds its colour rather than matching it" is
        /// one sentence and the whole mode, but a player still has to remember mid-drop that
        /// yellow is the one waiting for blue — and being asked to hold three pairs in your head
        /// is not a puzzle, it is a tax on the puzzle. It was reported exactly that way: "I
        /// always forget which colour blends with which". So the board answers it, always, and
        /// what is left to think about is *where*.
        /// </para>
        /// <para>
        /// Each chip says both halves of one recipe — the two pures that make a blend, and the
        /// one colour that then bursts it — because those are the same fact read forwards and
        /// backwards and a player needs it both ways: forwards while building, backwards while
        /// finishing. The recipes come from <c>FallMixing</c>, which derives them from the same
        /// masks the board does, so there is no table here to fall out of step with the rules.
        /// </para>
        /// <para>
        /// It sits in the band <c>FallBand</c> reserves below the board and is built once, in
        /// <see cref="Safe"/> rather than on the host — so a restart or a retry rebuilds the
        /// well and leaves the legend exactly where it was.
        /// </para>
        /// </summary>
        void BuildLegend()
        {
            var band = UIKit.Box("Blends", Safe,
                                 new Vector2(FallBand.LegendWidth, FallBand.LegendHeight),
                                 new Vector2(.5f, 0f), new Vector2(0f, FallBand.LegendCentre));
            band.anchorMin = band.anchorMax = new Vector2(.5f, 0f);

            var plate = UIKit.Img("Plate", band, Art.Round(24),
                                  new Color(.045f, .065f, .125f, .58f));
            UIKit.StretchTo((RectTransform)plate.transform, 0f, 0f, 0f, 0f);
            plate.raycastTarget = false;

            var edge = UIKit.Img("Edge", band, Art.RoundOutline(24, 2f), new Color(1, 1, 1, .09f));
            UIKit.StretchTo((RectTransform)edge.transform, 0f, 0f, 0f, 0f);
            edge.raycastTarget = false;

            var recipes = FallMixing.Recipes;
            for (int i = 0; i < recipes.Count; i++) Chip(band, FallBand.ChipCentre(i), recipes[i]);
        }

        /// <summary>
        /// One recipe: two pures, the blend they make, and the colour that bursts it.
        ///
        /// Laid out from a table of offsets rather than by stacking, for <c>ReadoutRow</c>'s
        /// reason — the pieces are small and close, so where each one sits is arithmetic and
        /// belongs where it can be read at a glance rather than accumulated down a method.
        /// </summary>
        void Chip(RectTransform parent, float centre, FallRecipe recipe)
        {
            Dot(parent, centre - 114f, recipe.First, 30f);
            Glyph(parent, centre - 88f, "+");
            Dot(parent, centre - 62f, recipe.Second, 30f);
            Glyph(parent, centre - 34f, "=");
            Dot(parent, centre + 2f, recipe.Blend, 42f);
            Glyph(parent, centre + 42f, "+");
            Dot(parent, centre + 70f, recipe.Finish, 30f);

            // The burst, drawn as the thing it is rather than written as a word: a spark needs
            // no translating and reads at this size, where a caption would not.
            var spark = UIKit.Img("Burst", parent, Art.Spark(64), Pal.A(Pal.Radiance, .96f),
                                  Vector2.one * 38f, new Vector2(.5f, .5f),
                                  new Vector2(centre + 112f, 0f));
            spark.raycastTarget = false;

            var glow = UIKit.Img("Glow", spark.transform, Art.Glow(128, 2.2f),
                                 Pal.A(Pal.Radiance, .30f), Vector2.one * 62f,
                                 new Vector2(.5f, .5f), Vector2.zero);
            glow.raycastTarget = false;
            glow.transform.SetAsFirstSibling();
        }

        void Dot(RectTransform parent, float x, int colour, float size)
        {
            var dot = UIKit.Img("Dot", parent, Art.Disc(96), Pal.EnergyColour(colour),
                                Vector2.one * size, new Vector2(.5f, .5f), new Vector2(x, 0f));
            dot.raycastTarget = false;

            var sheen = UIKit.Img("Sheen", dot.transform, Art.Glow(128, 2.4f),
                                  new Color(1, 1, 1, .16f), Vector2.one * size * 1.5f,
                                  new Vector2(.5f, .5f), Vector2.zero);
            sheen.raycastTarget = false;
            sheen.transform.SetAsFirstSibling();
        }

        void Glyph(RectTransform parent, float x, string mark)
        {
            var text = UIKit.Titled("Op", parent, mark, 30, new Color(.92f, .96f, 1f, .58f),
                                    TextAnchor.MiddleCenter, new Vector2(30f, 40f),
                                    new Vector2(.5f, .5f), new Vector2(x, 0f), 0f, 0f);
            text.raycastTarget = false;
        }

        // ------------------------------------------------------------------ readouts
        /// <summary>
        /// Three numbers, and each answers a different question: how far there is to go, how
        /// much is left to go with, and how well it has gone.
        ///
        /// <para>
        /// The supply is the only one that is coloured, because it is the only one that can end
        /// the run — and the thresholds come from <c>FallSupply.Pressure</c> rather than from a
        /// comparison written here, so they are fractions of this well's own budget and a test
        /// can hold them to what they claim. The other fail state has no number at all: it is
        /// the brim line on the board, which is where a rule the board can show belongs.
        /// </para>
        /// </summary>
        protected override void Readouts(List<Readout> into)
        {
            var run = _view != null ? _view.Run : null;

            into.Add(new Readout(Loc.Get("mode.cap.motes"),
                                 run == null ? "0" : run.Left.ToString()));

            if (run == null || !run.Supply.Bounded)
            {
                into.Add(new Readout(Loc.Get("mode.cap.supply"),
                                     Loc.Get("mode.fall.supply_free")));
            }
            else
            {
                var tint = run.Supply.Pressure == FallPressure.Critical ? Pal.Ember
                         : run.Supply.Pressure == FallPressure.Low ? Pal.Gold
                         : Pal.Cream;

                into.Add(new Readout(Loc.Get("mode.cap.supply"), run.Supply.Left.ToString(), tint));
            }

            into.Add(new Readout(Loc.Get("mode.cap.chain"),
                                 run == null ? "0" : run.Best.ToString()));
        }

        /// <summary>Which slot the supply sits in — what a lesson about it rings.</summary>
        const int SupplyReadout = 1;

        // ------------------------------------------------------------------ the stake
        protected internal override LevelId StakeLevel => Level != null ? Level.Id : LevelId.None;

        protected override bool RunOver => _finished || _closing;

        protected override void NoteAbandoned(string reason)
        {
            if (Level == null) return;

            var run = _view != null ? _view.Run : null;
            int cleared = run == null ? 0 : run.Started - run.Left;

            LevelAnalytics.TrackAbandoned(Level, cleared, Time.unscaledTime - _startedAt, reason);
        }

        /// <summary>
        /// Puts the well back as it was authored.
        ///
        /// A fresh supply and a well back under its brim is exactly why a restart here has to be
        /// paid for. What it costs is <c>RunScreen.RestartLevel</c>'s, which asks before this
        /// runs — a mode never gets at the price.
        /// </summary>
        protected override void Rewind()
        {
            if (_view == null || RunOver) return;

            _view.Begin(Host, Rules.Layout, Budget);
            Audio.Sfx("rotate_a", .55f);

            // A fresh run: the old one has just been paid for, so nothing carries over — not
            // the play clock, and not any continues bought on it.
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
        /// <summary>A well is measured in motes, so that is what a continue sells.</summary>
        protected internal override ContinueUnit MeasuredIn => ContinueUnit.Motes;

        /// <summary>
        /// How much supply has to be restored before a bought mote is a usable mote.
        ///
        /// <para>
        /// <b>Two answers, and the second is why this exists.</b> A well that simply ran dry has
        /// a deficit of nought — any mote at all is a playable mote — but a well can also be
        /// lost while there are still motes to come, because what is left of the procession
        /// cannot supply a channel some mote is missing. Selling six motes into that would put
        /// the player back on a board that is still provably unfinishable and end the run again
        /// in the same frame, having taken their gems. <c>FallVerdict</c> already works out the
        /// shortfall to decide the loss; this is that same reading, kept.
        /// </para>
        /// <para>
        /// A flooded well answers <c>NoContinue</c> and is never sold one. That is the honest
        /// answer rather than a gap: more motes do not empty a well that has already reached its
        /// brim, and invariant 23 is explicit that a mode which cannot be rescued at any price
        /// must say so.
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
        /// The motes were paid for: deal them and hand the well back.
        ///
        /// The view raises <see cref="OnChanged"/> and re-reads its own verdict, so if a grant
        /// somehow left the run lost the fail state fires again and the player is <em>asked
        /// again</em> rather than silently left on a dead board.
        /// </summary>
        protected internal override void ContinueWith(int motes)
        {
            if (_view == null) return;

            _view.Grant(motes);
            Audio.SfxVaried("whoosh", .45f);
        }

        // ------------------------------------------------------------------ endings
        /// <summary>
        /// The well is empty.
        ///
        /// Graded on the motes this run dropped, against the same thresholds every glade uses,
        /// over a par that is the fewest drops that could have emptied it. A tidy run comes in
        /// well under and flailing does not, which is the mode's own difficulty reading seen
        /// from the player's side.
        /// </summary>
        void Solve()
        {
            if (_finished || Level == null) return;
            _finished = true;

            Resolve();
            if (_view != null) _view.Locked = true;

            var run = _view.Run;

            int drops = Math.Max(1, run.Drops);
            int stars = Level.Tuning.StarsFor(drops);

            // No route, deliberately, and it is the weave's argument: the victory panel's route
            // bar compares a run against the board's own carved solution, and a well has many
            // par-length answers that are equally good — so it would print the same verdict for
            // everybody. What the run carries instead is the count it was graded on.
            var done = RunLedger.Win(Level, stars, drops,
                                     Time.unscaledTime - _startedAt, 0,
                                     route: 0,
                                     lit: run.Started, wanted: run.Started);

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
        /// The run reached a fail state. The offer first, the defeat only if it is declined —
        /// see <c>RunContinueFlow.OfferOrLose</c>. Nothing below runs until the player has said
        /// no, which is what keeps a continued run from being recorded as a loss, counted
        /// towards a chest or charged a heart.
        /// </summary>
        void Concede()
        {
            if (_finished) return;

            if (_view != null) _view.Locked = true;
            Continue.OfferOrLose(Lose);
        }

        /// <summary>
        /// The run is lost for good. Charged, recorded and reported exactly as a glade's defeat
        /// is — <c>RunLedger.Loss</c> owns the heart, the streak, the chest count and the
        /// analytics, so there is no second copy here of what losing a run does.
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
        /// <para>
        /// Split from the panel because two exits now share it and only one of them raises
        /// anything. <c>RunLedger.Loss</c> still owns the heart, the streak, the chest count and
        /// the analytics, so there is no second copy here of what losing a run does; this only
        /// decides when.
        /// </para>
        /// </summary>
        RunLedger.LossRecord? RecordLoss()
        {
            if (_finished || Level == null) return null;
            _finished = true;

            Resolve();
            if (_view != null) _view.Locked = true;

            var run = _view.Run;

            // The two ways a well ends want opposite fixes, so they are told apart in the one
            // place that can still see the difference. See DefeatReason.
            var reason = run.Verdict.Ending == FallEnding.Flooded
                       ? DefeatReason.WellFlooded : DefeatReason.OutOfMotes;

            // No near miss. That line is measured in turns from the solution, which a well has
            // no notion of — a board is one lucky chain from finished or six drops from it,
            // depending on nothing anybody can be told in a sentence — so it is left at nought,
            // where RunOutcome.NearMiss reads it as "not close" and says nothing.
            return RunLedger.Loss(Level, reason, Math.Max(1, run.Drops),
                                  Time.unscaledTime - _startedAt, 0, route: 0,
                                  stepsToSolution: 0,
                                  lit: run.Started - run.Left, wanted: run.Started,
                                  price: Price);
        }

        // ------------------------------------------------------------------ the lessons
        /// <summary>
        /// The four things a well brings that a player arriving from four chapters of tapping
        /// tiles cannot be expected to work out, declared as facts about <em>this</em> board.
        ///
        /// <para>
        /// <b>Declared, not shown.</b> What goes up, in what order, and whether this particular
        /// player has met any of it is <c>RunScreen</c>'s to arrange. This says only what the
        /// board holds — which is what lets the review key in the header work at all, since a
        /// list filtered by "never seen" is empty exactly when somebody asks to be reminded.
        /// </para>
        /// <para>
        /// <b>Each one is conditional on something real, and that is what stops this being a
        /// tutorial.</b> The cooking lesson is on every well because every well is made of
        /// motes. The supply lesson is only on a well that can actually run dry, so the first
        /// level of the chapter — which is authored without a budget, exactly as the first glade
        /// in the game is — does not spend a once-in-a-lifetime lesson teaching a meter it does
        /// not have. And the brim lesson waits for a well where the brim is genuinely in reach:
        /// on a board with six rows of clearance it is a line nobody can touch, and a modal
        /// about it would be a modal about nothing.
        /// </para>
        /// </summary>
        protected internal override void Lessons(List<Lesson> into)
        {
            if (Level == null || _view == null || _view.Run == null) return;

            var run = _view.Run;

            // The verb first. A player who has met neither this nor the meter has to know what
            // a drop does before a number counting them down can mean anything.
            into.Add(Lesson.At(Mechanic.FallCook, _view.RipeAnchor));

            // Then the one thing on the board that is not a mote. Conditional on the board
            // actually standing one, which every well of the first chapter does not — and a
            // lesson spent over a board with no glass on it is one that can never be spent
            // again. Second rather than last because it is about what is *there*, where the two
            // below are about meters and fail lines.
            if (run.Board.Lenses > 0)
                into.Add(Lesson.At(Mechanic.FallLens, _view.LensAnchor));

            if (run.Supply.Bounded)
                into.Add(Lesson.At(Mechanic.FallSupply, ReadoutAt(SupplyReadout)));

            if (run.Board.Headroom <= BrimTeachAt)
                into.Add(Lesson.At(Mechanic.FallBrim, _view.BrimAnchor));
        }

        /// <summary>
        /// How little clearance a well needs before the brim is worth teaching.
        ///
        /// Three careless drops is close enough that a player will meet it in this run; more
        /// than that and the line is scenery, and a lesson spent on scenery is one that can
        /// never be spent again.
        /// </summary>
        const int BrimTeachAt = 3;

        /// <summary>
        /// A lesson may go up while the well is being played on and at no other time. A cascade
        /// latches the board itself and hands it back itself, so teaching over one would end
        /// with <c>Latch</c> unlatching a board its own animation still owns.
        /// </summary>
        protected internal override bool Teachable
            => _view != null && _view.TakingInput && !_finished && !_closing;

        /// <summary>Long enough for the well to have finished arriving.</summary>
        protected internal override float LessonDelay => FallTempo.Entrance + .15f;

        /// <summary>
        /// Holds the well while a lesson is up, and hands it back afterwards — but never to a
        /// run that ended underneath the panel.
        /// </summary>
        protected internal override void Latch(bool latched)
        {
            if (_view == null) return;
            if (!latched && (_finished || _closing)) return;

            _view.Locked = latched;
        }
    }
}
