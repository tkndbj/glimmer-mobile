using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The panel a refused restart raises, and the one thing about it that no amount of reading
    /// settles: whether the board is handed back.
    ///
    /// <para>
    /// <b>Why this fixture exists.</b> <c>RestartGateOverlay</c> holds the board latched while it
    /// is up and releases it from <c>OnDestroy</c> — unless it declared a hand-off, in which case
    /// whatever it handed the run on to takes the latch instead. That is <c>PauseOverlay</c>'s
    /// rule, and it is wrong in two directions with two different symptoms, neither of which any
    /// compile, validator or screenshot can see. Declare a hand-off and let nothing take the
    /// board over, and the player is left on a grove that never thaws: every control dead, the
    /// run neither over nor running. Fail to declare one and the board is <em>resumed under the
    /// question standing over it</em> — because <c>Close</c>'s continuation runs before
    /// <c>Destroy</c> lands, so <c>RestartLevel</c> re-latches first and <c>OnDestroy</c> then
    /// unlatches behind it, leaving a live, tappable board under a modal.
    /// </para>
    /// <para>
    /// <b>It builds the real panel rather than reasoning about it.</b> Nothing else here had ever
    /// run this code: the gate's arithmetic is proved offline against plain integers
    /// (<c>HeartStakeTests</c>) and its answer at a screen with a real wallet is proved next door
    /// (<c>RunStakeLifecycleTests</c>), but the panel itself — its <c>Build</c>, its latch, its
    /// hand-off — had only been read. <c>Flow.Init</c> and <c>View.Init</c> are both
    /// <c>internal</c>, and Presentation's <c>InternalsVisibleTo</c> is what makes standing the
    /// whole thing up here possible at all.
    /// </para>
    /// <para>
    /// Editor-only, like <c>BudCanvasTests</c> and for the same reason: the subject is Unity's own
    /// object lifetime — when <c>OnDestroy</c> runs relative to everything else — and a faked pair
    /// would prove the arithmetic rather than the thing that actually goes wrong.
    /// </para>
    /// </summary>
    public sealed class RestartGateTests
    {
        /// <summary>
        /// A <c>RunScreen</c> with no board that records what was done to its latch.
        ///
        /// <para>
        /// In the test assembly deliberately, exactly as <c>RunStakeLifecycleTests.StakeProbe</c>
        /// is: <c>RunStakeTests</c> scans <c>typeof(RunScreen).Assembly</c> for modes carrying
        /// part of the stake and <c>RunFrameTests</c> scans it for modes declaring their own
        /// frame, so a probe declared over here cannot be mistaken for either.
        /// </para>
        /// </summary>
        sealed class GateProbe : RunScreen
        {
            public LevelId Level = LevelId.None;

            /// <summary>Every <c>Latch</c> the base class asked for, in order.</summary>
            public readonly List<bool> Latches = new List<bool>();

            /// <summary>How many times a board was actually put back.</summary>
            public int Rewinds;

            protected override void Build() { }
            public override void RetryAfterDefeat() { }

            protected internal override LevelId StakeLevel => Level;
            protected override bool RunOver => false;
            protected override void Rewind() => Rewinds++;
            protected override void NoteAbandoned(string reason) { }

            protected internal override bool Runnable => false;
            protected internal override void Running(bool running) { }

            protected internal override void Latch(bool latched) => Latches.Add(latched);

            public void Begin() => Commit();

            /// <summary>Whether the board was last told to hold rather than to carry on.</summary>
            public bool Latched => Latches.Count > 0 && Latches[Latches.Count - 1];

            /// <summary>Whether anything has handed the board back since <paramref name="from"/>.</summary>
            public bool ResumedSince(int from)
            {
                for (int i = from; i < Latches.Count; i++)
                    if (!Latches[i]) return true;

                return false;
            }
        }

        const string Paid = "g4";

        GameObject _canvas;
        GateProbe _probe;

        Canvas _canvasBefore;
        RectTransform _screensBefore, _overlaysBefore, _effectsBefore;
        View _currentBefore;
        LevelCatalog _catalogBefore;
        int _heartsBefore;
        View[] _modalsBefore;

        /// <summary>
        /// <c>Flow</c>'s modal stack, which is a private static and has to be handed back
        /// exactly as it was found.
        ///
        /// <para>
        /// <b>Dismissing what this fixture raised is not enough, and that is worth stating.</b>
        /// A panel leaves the stack in <c>Flow.Dismiss</c>, which <c>Close</c> only reaches from
        /// its exit tween's completion — and edit mode runs no tweens, so every panel this
        /// fixture closes stays in the list for ever. <c>LiveModal</c> cannot find them either,
        /// because it skips anything already leaving. Left alone they are dead entries in a
        /// static list every other fixture shares, which is precisely the kind of debt that
        /// produces a mystery failure three fixtures away.
        /// </para>
        /// </summary>
        static List<View> Stack()
            => (List<View>)typeof(Flow)
                .GetField("_modals", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);

        [SetUp]
        public void Stand()
        {
            // Flow is a static, so what it was holding is taken and handed back rather than
            // assumed — the offline runner promises no order and other fixtures share the
            // process. Only Init's four public roots can be restored; the iris and the flash it
            // also builds are private and are never asked for here.
            _canvasBefore = Flow.Canvas;
            _screensBefore = Flow.Screens;
            _overlaysBefore = Flow.Overlays;
            _effectsBefore = Flow.Effects;
            _currentBefore = Flow.Current;

            _canvas = new GameObject("RestartGateProbe", typeof(Canvas), typeof(CanvasScaler),
                                     typeof(GraphicRaycaster));

            var canvas = _canvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = _canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 2340);
            scaler.matchWidthOrHeight = 0f;

            Flow.Init(canvas);
            Canvas.ForceUpdateCanvases();

            _catalogBefore = GameContent.Catalog;
            GameContent.Publish(LevelCatalog.FromLoaded(Catalog(), Array.Empty<ChapterBody>()));

            Grace(3);
            PlayerProgress.LoadFrom(new SaveFileDto());
            RunGuard.Resolve();
            RunGuard.NoteReported();

            _heartsBefore = Wallet.Hearts.Count;
            _modalsBefore = Stack().ToArray();

            _probe = new GameObject("GateProbe").AddComponent<GateProbe>();
            _probe.Level = LevelId.Parse(Paid);
        }

        [TearDown]
        public void Strike()
        {
            // Every panel this fixture put on the stack comes off it, whether or not it was
            // closing — see Stack(). Walked against the snapshot rather than by type, so a
            // panel raised indirectly (a forfeit confirmation the door itself put up) is caught
            // as surely as one this fixture named.
            var stack = Stack();
            var kept = new List<View>(_modalsBefore ?? new View[0]);

            for (int i = stack.Count - 1; i >= 0; i--)
            {
                if (kept.Contains(stack[i])) continue;

                var mine = stack[i];
                stack.RemoveAt(i);
                if (mine) UnityEngine.Object.DestroyImmediate(mine.gameObject);
            }

            if (_probe) UnityEngine.Object.DestroyImmediate(_probe.gameObject);
            if (_canvas) UnityEngine.Object.DestroyImmediate(_canvas);

            Flow.Canvas = _canvasBefore;
            Flow.Screens = _screensBefore;
            Flow.Overlays = _overlaysBefore;
            Flow.Effects = _effectsBefore;
            Flow.Current = _currentBefore;

            Holding(_heartsBefore);

            RunGuard.Resolve();
            RunGuard.NoteReported();
            ProgressionRules.Reset();
            PlayerProgress.LoadFrom(new SaveFileDto());
            GameContent.Publish(_catalogBefore);
        }

        /// <summary>One five-glade chapter, so the window covers the first three of it.</summary>
        static CatalogIndex Catalog()
        {
            var builder = new CatalogIndexBuilder();
            builder.Add(new ManifestChapterDto
            {
                id = "c01_one", order = 10, version = 1,
                levels = new[] { "g1", "g2", "g3", "g4", "g5" },
            }, 1);
            return builder.Build();
        }

        static void Grace(int levels)
        {
            var dto = new ProgressionDto
            {
                schemaVersion = ProgressionSchema.Version,
                xpToNext = new[] { 100 },
                tailXpToNext = 100,
                tailXpIncrement = 10,
                hearts = new HeartsDto { graceLevels = levels },
            };

            Assert.IsTrue(ProgressionTable.TryBuild(dto, out var table, new List<string>()));
            ProgressionRules.Publish(table);
        }

        /// <summary>
        /// Puts the wallet at exactly this many hearts. Spent down and granted back up, which is
        /// the only pair of doors it offers — there is deliberately no setter on a heart count.
        /// </summary>
        static void Holding(int hearts)
        {
            Wallet.TrySpendHeart(Wallet.Hearts.Count);
            if (hearts > 0) Wallet.GrantHearts(hearts);

            Assume.That(Wallet.Hearts.Count, Is.EqualTo(hearts), "the fixture could not set the wallet");
        }

        /// <summary>
        /// One frame of the panel, which is what the clock's way onward rides on.
        ///
        /// <para>
        /// Through reflection because <c>Update</c> is a Unity message rather than an API, and
        /// edit mode does not tick <c>MonoBehaviour</c>s — so a test that wanted the panel to
        /// notice a heart landing would otherwise have to wait for a frame that never comes.
        /// Calling it is exactly what the engine does.
        /// </para>
        /// </summary>
        static void Tick(RestartGateOverlay panel)
        {
            var update = typeof(RestartGateOverlay)
                .GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.IsNotNull(update, "the panel has stopped taking a frame");
            update.Invoke(panel, null);
        }

        /// <summary>
        /// The panel going away, with the message the engine would send it.
        ///
        /// <para>
        /// <b>Edit mode dispatches no <c>MonoBehaviour</c> messages at all</b> unless a script
        /// asks for them with <c>[ExecuteAlways]</c>, and this one has no business asking — so
        /// <c>DestroyImmediate</c> alone tears the object down without <c>OnDestroy</c> ever
        /// running. That is worth stating plainly because it is a trap that produces
        /// <em>passing</em> tests: a case asserting the board was <em>not</em> handed back is
        /// satisfied by the method never being called, so the one direction of this rule that is
        /// hardest to get right would have been proved by nothing at all. It was, until the
        /// other direction failed and said so. Sending the message and then destroying is what
        /// the engine does, in that order.
        /// </para>
        /// </summary>
        static void Dismiss(RestartGateOverlay panel)
        {
            var destroy = typeof(RestartGateOverlay)
                .GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.IsNotNull(destroy, "the panel no longer hands the board back on its way out");
            destroy.Invoke(panel, null);

            UnityEngine.Object.DestroyImmediate(panel.gameObject);
        }

        /// <summary>A committed run on a charged glade with <paramref name="hearts"/> in hand.</summary>
        RestartGateOverlay Refused(int hearts)
        {
            Holding(hearts);
            _probe.Begin();

            Assume.That(_probe.MayRestart, Is.False, "the fixture wants a refused restart");

            _probe.RestartLevel();

            var panel = Flow.LiveModal<RestartGateOverlay>();
            Assert.IsNotNull(panel, "a refused restart raised no offer at all");
            return panel;
        }

        // ---------------------------------------------------------------- it exists
        [Test]
        public void ARefusedRestartRaisesTheOfferAndHoldsTheBoardBehindIt()
        {
            // The whole panel is built here for the first time anywhere — so this case is as
            // much "Build does not throw" as it is about the latch, and both are worth having.
            var panel = Refused(0);

            Assert.IsTrue(_probe.Latched, "the board was left live under the offer");
            Assert.IsNotNull(panel.Content, "the panel was never initialised");
            Assert.Greater(panel.Content.childCount, 0, "the panel drew nothing");
        }

        [Test]
        public void TheOfferIsRaisedForAPlayerHoldingAHeartTooAndSaysSo()
        {
            // The commonest refusal, and the one OutOfHeartsOverlay could not have served: it
            // closes itself the moment Profile.CanPlay reads true, which is true here.
            var panel = Refused(1);

            Assert.IsTrue(Profile.CanPlay, "the fixture wants a player who can still play");
            Assert.IsNotNull(panel);
            Assert.IsTrue(_probe.Latched);
        }

        // ---------------------------------------------------------------- the hand-off
        [Test]
        public void DismissingTheOfferHandsTheBoardBack()
        {
            // Every way out that is not the way onward: KEEP PLAYING, the scrim, the hardware
            // back key, and the screen underneath being torn down with this still open. All four
            // arrive at OnDestroy, which is the whole reason the safe outcome lives there rather
            // than on the buttons — a panel with several exits reports through none of them
            // reliably.
            var panel = Refused(0);
            int from = _probe.Latches.Count;

            Dismiss(panel);

            Assert.IsTrue(_probe.ResumedSince(from),
                          "the offer went away without handing the board back, so it never thaws");
        }

        [Test]
        public void AnOfferThatHandsTheRunOnDoesNotAlsoThawTheBoard()
        {
            // The other direction, and the one with the subtler symptom. Close's continuation
            // runs before Destroy lands, so RestartLevel has already re-latched by the time
            // OnDestroy fires — an undeclared hand-off would unlatch behind it and leave a live,
            // tappable board under the question standing over it.
            var panel = Refused(0);

            // The clock's way onward: hearts arrive, the panel notices on its next frame. Two,
            // because a charged restart pays for the run being left and then needs one more.
            Holding(2);
            Tick(panel);

            Assert.IsTrue(panel.IsLeaving, "the panel did not act on the gate lifting");

            int from = _probe.Latches.Count;
            Dismiss(panel);

            Assert.IsFalse(_probe.ResumedSince(from),
                           "a panel that handed the run on also handed the board back, so the "
                           + "board is live under whatever took the run over");
        }

        [Test]
        public void ThePanelWaitsWhileSomethingIsStackedOverIt()
        {
            // Flow.IsTopModal, and it is not defensive. The hearts land the instant a video
            // finishes — seconds before the celebration over this panel has been collected — so
            // without it this closes out from under PrizeOverlay and puts a forfeit confirmation
            // up behind somebody's confetti. Driven with a real second modal rather than a mock,
            // because what is being tested is Flow's own idea of what is on top.
            var panel = Refused(0);

            // Stacked *before* the hearts land, which is the order the real case happens in — a
            // celebration is standing over this panel and the hearts were banked by the redeem
            // behind it. Writing it the other way round is what found this: granting first fires
            // PlayerProgression.Changed, the rescue redraws the panel, and Build calls Paint —
            // so the gate was noticed as lifted through a route that has nothing to do with the
            // frame, and the case proved nothing about being covered.
            var over = Flow.Modal<ForfeitOverlay>(v =>
            {
                v.Choice = ForfeitOverlay.Kind.Restart;
                v.OnConfirm = () => { };
                v.OnCancel = () => { };
            });

            Assert.IsFalse(panel.IsLeaving, "the fixture wants a panel that is still standing");
            Assert.IsFalse(Flow.IsTopModal(panel), "and something stacked over it");

            Holding(2);

            Assert.IsFalse(panel.IsLeaving,
                           "the offer closed out from under the panel above it the moment the "
                           + "hearts landed, which is a forfeit confirmation behind a celebration");

            Tick(panel);
            Assert.IsFalse(panel.IsLeaving, "and again on its next frame while still covered");

            // Flow.Dismiss ends a panel the way the game does, with Object.Destroy — which is
            // correct in a build and *refused* in edit mode, where it logs an error and does
            // nothing. Hence the DestroyImmediate underneath it, which is what actually removes
            // the object here. The expectation is what stops NUnit failing the case on that
            // error log: the complaint is a fact about the runner, not about the panel, and
            // teaching Flow to branch on Application.isPlaying would be shipping code bent
            // around a test.
            LogAssert.Expect(LogType.Error, new Regex("Destroy may not be called from edit mode"));

            Flow.Dismiss(over);
            UnityEngine.Object.DestroyImmediate(over.gameObject);

            Tick(panel);
            Assert.IsTrue(panel.IsLeaving, "but it did not carry on once it was uncovered again");
        }

        // ---------------------------------------------------------------- the way onward
        [Test]
        public void TheOfferGoesBackThroughTheDoorRatherThanRewindingTheBoard()
        {
            // Hearts arriving do not imply the gate has lifted — a rescue of one heart to a
            // player holding none leaves a charged restart still refused — so the panel re-enters
            // RestartLevel rather than calling the mode's rewind. Here it lifts, and what proves
            // the door was used rather than bypassed is that no board was put back without the
            // player being asked: a restart is one of the three confirmations in this game.
            var panel = Refused(0);

            Holding(2);
            Tick(panel);

            Assert.AreEqual(0, _probe.Rewinds,
                            "the offer put the board back itself, skipping the gate and the "
                            + "confirmation both");
        }

        [Test]
        public void AnOfferIsNeverRaisedOverAFreeRun()
        {
            // A run that costs nothing to lose cannot coherently be refused for lack of something
            // to lose, so the panel has no business existing on one. The opening window covers
            // g1; the board is committed and the bar is empty, which is every other condition the
            // refusal needs.
            UnityEngine.Object.DestroyImmediate(_probe.gameObject);

            _probe = new GameObject("GateProbe").AddComponent<GateProbe>();
            _probe.Level = LevelId.Parse("g1");

            Holding(0);
            _probe.Begin();

            Assert.IsTrue(_probe.MayRestart, "a free run is always restartable");

            _probe.RestartLevel();

            Assert.IsNull(Flow.LiveModal<RestartGateOverlay>(),
                          "a free run was sold hearts it does not need");
        }
    }
}
