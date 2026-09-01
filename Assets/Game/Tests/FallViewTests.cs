using GlimmerGrove.Modes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The one thing a board must be true of the instant it exists: that it is taking input.
    ///
    /// <para>
    /// <b>This is a bug reported from play, and it is the second time this project has paid for
    /// a latch with more than one writer.</b> A player ran out of motes on the second well, was
    /// offered a continue, opened the gem shelf, backed out of it, declined the offer and then
    /// pressed TRY AGAIN. The well was rebuilt in front of them and every tap was ignored, for
    /// the rest of the screen's life; leaving the chapter and coming back was the only way out.
    /// </para>
    /// <para>
    /// The cause is one missing line and it is worth stating exactly, because nothing else could
    /// have found it. Every way a run ends latches the board — <c>FallView.Settle</c> latches it,
    /// and <c>Concede</c> and <c>Lose</c> each latch it again before their panel goes up — and
    /// <c>FallView.Begin</c> rebuilt everything <em>except</em> that flag. So the retry produced
    /// a fresh well behind a latch belonging to a run that no longer existed. It compiled, it
    /// validated, the whole suite was green, and the board looked perfectly correct: the only
    /// symptom was that nothing happened.
    /// </para>
    /// <para>
    /// <b>The fix belongs in <c>Begin</c> rather than in the caller</b>, which is what makes this
    /// test worth having rather than a note. There are three callers — the first build, a restart
    /// and a retry — and only two of them happened to unlatch: <c>RunScreen.RestartLevel</c> runs
    /// <c>Rewind(); Resume();</c> and the <c>Resume</c> is what cleared it, while
    /// <c>RetryAfterDefeat</c> is a mode's own override with no such pairing. A rule that holds
    /// only when the caller remembers is a rule the fourth caller breaks. <c>RippleView.Begin</c>
    /// has had the line since it shipped; this is the copy that did not.
    /// </para>
    /// <para>
    /// Needs the Editor: a <c>GameObject</c> and a real component are ECalls the offline runner
    /// cannot make.
    /// </para>
    /// </summary>
    public sealed class FallViewTests
    {
        GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root) Object.DestroyImmediate(_root);
        }

        static FallLayout Layout()
        {
            Assert.IsTrue(FallDeal.TryParse("BGR", out var deal, out string dealError), dealError);

            var rows = new[] { "....", "....", "....", "....", "....", "RYY." };
            Assert.IsTrue(FallLayout.TryReadRows(rows, 4, rows.Length, out var fill,
                                                 out string fillError), fillError);

            return new FallLayout(4, rows.Length, fill, deal);
        }

        /// <summary>Lantern Row, which is the board the leak was reported on: two panes.</summary>
        static FallLayout Lanterns()
        {
            Assert.IsTrue(FallDeal.TryParse("GBR", out var deal, out string dealError), dealError);

            var rows = new[]
            {
                ".......", ".......", ".......", "M.....Y",
                "M.....Y", "Mb...yM", "MY...MM", "YYYYMMM"
            };

            Assert.IsTrue(FallLayout.TryReadRows(rows, 7, rows.Length, out var fill,
                                                 out string fillError), fillError);

            return new FallLayout(7, rows.Length, fill, deal);
        }


        /// <summary>
        /// The chapter-three shape: a whorl with a yellow on one side and a cyan on the other,
        /// which is the pair whose union is white. One drop opens it, and on the next wave it
        /// draws both in and leaves a white where it stood — so this board exercises the one
        /// event in the mode that moves two widgets to a cell neither of them was in.
        /// </summary>
        static FallLayout Whorled()
        {
            Assert.IsTrue(FallDeal.TryParse("BRG", out var deal, out string dealError), dealError);

            var rows = new[] { "......", "......", "......", "......", "......", ".Y@CG." };

            Assert.IsTrue(FallLayout.TryReadRows(rows, 6, rows.Length, out var fill,
                                                 out string fillError), fillError);

            return new FallLayout(6, rows.Length, fill, deal);
        }

        /// <summary>
        /// A host with a real rect, because <c>Begin</c> sizes the well from it — a board built
        /// against a zero rect is a board of nothing and would prove nothing here.
        /// </summary>
        RectTransform Host()
        {
            _root = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));

            var host = new GameObject("Board", typeof(RectTransform))
                .GetComponent<RectTransform>();

            host.SetParent(_root.transform, false);
            host.anchorMin = host.anchorMax = new Vector2(.5f, .5f);
            host.sizeDelta = new Vector2(720f, 1100f);

            return host;
        }

        [Test]
        public void AFreshlyBuiltWellIsPlayableHoweverTheLastRunEnded()
        {
            // Begin tidies the previous board away with Destroy, which is an error log outside
            // play mode. BoardLatchTests' bargain, for the same reason: every claim below is an
            // explicit assertion, so nothing rests on the absence of a log line.
            LogAssert.ignoreFailingMessages = true;

            var host = Host();
            var view = host.gameObject.AddComponent<FallView>();

            view.Begin(host, Layout(), 8);
            Assert.IsTrue(view.TakingInput, "a board should take input the moment it exists");

            // Exactly what every ending leaves behind. Settle latches it, and Concede and Lose
            // each latch it again before their panel goes up.
            view.Locked = true;
            Assert.IsFalse(view.TakingInput, "a latched board takes nothing, which is the point");

            // TRY AGAIN.
            view.Begin(host, Layout(), 8);

            Assert.IsFalse(view.Locked,
                           "the well was rebuilt behind a latch belonging to a run that no " +
                           "longer exists, so every tap on it is ignored for the rest of the " +
                           "screen's life");

            Assert.IsTrue(view.TakingInput, "and so the retry is dead on arrival");
        }

        /// <summary>
        /// The other half of the same latch, and the reason it is a second flag rather than more
        /// uses of <c>Locked</c>: a board handed back has still not been <em>allowed</em> to
        /// start, and only the screen knows that.
        /// </summary>
        [Test]
        public void AFreshlyBuiltWellIsStillHeldUntilTheScreenSaysOtherwise()
        {
            LogAssert.ignoreFailingMessages = true;

            var host = Host();
            var view = host.gameObject.AddComponent<FallView>();

            view.Begin(host, Layout(), 8);

            Assert.IsTrue(view.Held,
                          "a frame of a run the player has not been shown is a frame they did " +
                          "not get, so the safe direction is held");

            Assert.IsFalse(view.Playable, "which is the half TakingInput deliberately excludes");
        }

        /// <summary>
        /// <b>A widget handed back to the pool carries no live tween, on either of the objects a
        /// tween here can be filed under.</b> Reported from play as a lens that sometimes refused
        /// to fall.
        ///
        /// <para>
        /// A <c>Tween</c> is filed under the <c>UnityEngine.Object</c> its caller named, so
        /// <c>KillAll(mote.Body)</c> says nothing at all about a tween owned by <c>mote.Rt</c> —
        /// they are two different objects and neither call reaches the other. The pool called the
        /// first; the collapse (<c>Slide</c>) uses the second, because a slide moves the
        /// transform. So a mote or a lens recycled while its slide was still running went into
        /// the pool with a live tween writing its position, came back out as the next falling
        /// drop or as a cell <c>Sync</c> had just placed, and was dragged to wherever the old
        /// cell had been.
        /// </para>
        /// <para>
        /// It is easy to hit rather than a corner: a slide is dealt a stagger by column, so it
        /// finishes up to a third of a beat after the wave that threw it, and the next wave is
        /// already bursting by then. Nothing else here could have caught it — the model settles
        /// correctly (fuzzed at thirty thousand drops across the shipped chapter with no floating
        /// cell), every gate is green, and only the drawing is wrong.
        /// </para>
        /// <para>
        /// Driven behaviourally rather than by asking the tween system what it holds: the claim
        /// is that a recycled widget is not <em>moved</em>, and the honest way to say that is to
        /// put it somewhere and let time pass. Reflection reaches the pool because it is private
        /// on purpose — this is a fact about the pool rather than about its interface.
        /// </para>
        /// </summary>
        [Test]
        public void AWidgetHandedBackToThePoolCarriesNoLiveTween()
        {
            LogAssert.ignoreFailingMessages = true;

            var host = Host();
            var view = host.gameObject.AddComponent<FallView>();
            view.Begin(host, Layout(), 8);

            var flags = System.Reflection.BindingFlags.Instance
                      | System.Reflection.BindingFlags.NonPublic;

            var widgets = (System.Array)typeof(FallView).GetField("_at", flags).GetValue(view);

            object widget = null;
            foreach (var candidate in widgets) if (candidate != null) { widget = candidate; break; }
            Assert.IsNotNull(widget, "the well should have drawn something to recycle");

            var kind = widget.GetType();
            var rt = (RectTransform)kind.GetField("Rt").GetValue(widget);
            var body = (UnityEngine.UI.Image)kind.GetField("Body").GetValue(widget);

            // Exactly what a wave leaves behind: the collapse on the transform, and a tint on the
            // image. Only the second was ever being killed.
            Tween.Move(rt, new Vector2(999f, 999f), 4f);
            Tween.Tint(body, Color.red, 4f);

            typeof(FallView).GetMethod("Give", flags).Invoke(view, new[] { widget });

            // Where the pool's next caller would put it.
            var placed = new Vector2(12f, 34f);
            rt.anchoredPosition = placed;

            Tween.Inst.Tick(1f, 1f);

            Assert.AreEqual(placed, rt.anchoredPosition,
                            "a recycled widget was dragged off the cell it was just placed in by " +
                            "a slide belonging to the run before it — which is a lens that " +
                            "refuses to fall, on a board the model settled perfectly");
        }

        /// <summary>
        /// <b>Every widget drawn on the board is one the view still owns.</b> The census the leak
        /// was found by, and the one thing that could have caught it: the model was right, par was
        /// right, every validator and the whole suite were green, and the only wrong thing was a
        /// picture nobody was counting.
        ///
        /// <para>
        /// A drop taken in by glass is absorbed — the stack does not grow — so the falling widget
        /// must be handed back and the lens's own widget left standing. <c>FallView.Drop</c> asked
        /// <c>Enriches</c>, which is false for glass, so it took the "came to rest on top" branch
        /// instead: the falling mote was written into the view's index over the lens, and the
        /// lens's widget fell out of it. Nothing owned it after that, so nothing repainted it,
        /// nothing moved it when the column collapsed, and nothing ever took it off the board.
        /// Reported from play as a pane hanging in the air showing the charge it held before the
        /// drop.
        /// </para>
        /// <para>
        /// Driven through the real <c>PlayDrop</c> rather than by inspection, because the claim is
        /// about what is on the screen. Edit mode runs no coroutines, so the body is stepped by
        /// hand — a wait is satisfied at once and a nested coroutine is pumped, which is what
        /// Unity does with a longer clock.
        /// </para>
        /// </summary>
        [Test]
        public void EveryWidgetOnTheBoardIsOneTheViewStillOwns()
        {
            LogAssert.ignoreFailingMessages = true;

            var host = Host();
            var view = host.gameObject.AddComponent<FallView>();
            view.Begin(host, Lanterns(), 8);

            Census(view, "the board as it opens");

            // The exact sequence it was reported on: green onto the left pane, then blue onto the
            // right one — which fires, strikes the left pane, and takes the whole board with it.
            int lens = view.Run.Board.Index(1, 5);
            object before = WidgetAt(view, lens);
            Assert.IsNotNull(before, "the left pane should be drawn before anybody touches it");

            PumpDrop(view, 1);
            Census(view, "after a drop the left pane took in");

            // **The identity is the half a head-count cannot see.** A drop glass takes in is
            // absorbed, so the pane keeps its own widget and the falling mote is handed back.
            // Routed down the "came to rest on top" branch instead, the falling mote is written
            // into the index over the pane — which draws the cell as a plain green mote until the
            // next repaint, plays the note for a mote that only stacked, and (before the branch
            // learned to reclaim) left the pane's own widget on the board for ever.
            Assert.AreSame(before, WidgetAt(view, lens),
                           "the pane's own widget was replaced by the mote that was dropped on " +
                           "it, so the cell is no longer being drawn by the thing that is in it");

            PumpDrop(view, 5);
            Census(view, "after the shot that empties the well");

            Assert.AreEqual(0, view.Run.Board.Lenses,
                            "both panes fired, so neither is still standing in the model");

            Assert.IsNull(WidgetAt(view, lens),
                          "and a pane that fired empties fully: nothing of it is left on the board");
        }


        /// <summary>
        /// <b>The same census over a whorl, which leaves the board by a different route from
        /// everything else.</b> A mote and a pane are handed back by the wave that removed them;
        /// a whorl opens on one wave and, on the next, takes <em>two other cells</em> with it and
        /// hands its own widget on to the mote they became. That is three more places a widget
        /// can be dropped out of the index and left standing — the exact failure the lens shipped
        /// with, on the one structure in this mode with more of them than glass had.
        /// </summary>
        [Test]
        public void AWhorlThatTurnsTakesItsPairOffTheBoardWithIt()
        {
            LogAssert.ignoreFailingMessages = true;

            var host = Host();
            var view = host.gameObject.AddComponent<FallView>();
            view.Begin(host, Whorled(), 9);

            Census(view, "the board as it opens");

            int whorl = view.Run.Board.Index(2, 5);
            int left = view.Run.Board.Index(1, 5);
            int right = view.Run.Board.Index(3, 5);

            Assert.IsNotNull(WidgetAt(view, whorl), "the whorl should be drawn from the start");
            Assert.AreEqual(1, view.Run.Board.Whorls, "and standing in the model");

            // Blue straight onto the whorl: it opens, and on the next wave it draws the yellow
            // and the cyan together into a white that bursts where it stands.
            PumpDrop(view, 2);

            Census(view, "after the whorl turned");

            Assert.AreEqual(0, view.Run.Board.Whorls,
                            "the whorl turned, so it is spent and gone from the model");

            // **The half that only a census can catch.** A merge is the one event here that moves
            // two widgets to a cell neither of them was in, so it is the one place a widget can
            // be left drawing a cell the board has emptied - still on screen, owned by nothing,
            // never repainted and never falling. That is the fault a pane hanging over an emptied
            // column was reported as one mechanic earlier.
            Assert.IsNull(WidgetAt(view, whorl),
                          "and nothing of it is left on the board");
            Assert.IsNull(WidgetAt(view, left),
                          "nor of the mote it drew in from the left");
            Assert.IsNull(WidgetAt(view, right),
                          "nor of the one on its right");
        }

        /// <summary>Whatever widget the view currently has drawing one cell, or null.</summary>
        static object WidgetAt(FallView view, int cell)
        {
            var flags = System.Reflection.BindingFlags.Instance
                      | System.Reflection.BindingFlags.NonPublic;

            var at = (System.Array)typeof(FallView).GetField("_at", flags).GetValue(view);
            return at.GetValue(cell);
        }

        /// <summary>
        /// What the view is drawing, what it still owns, and what the model says are all one
        /// number. Anything else is a widget on a board that nothing can move or remove.
        /// </summary>
        static void Census(FallView view, string when)
        {
            var flags = System.Reflection.BindingFlags.Instance
                      | System.Reflection.BindingFlags.NonPublic;

            var well = (RectTransform)typeof(FallView).GetField("_well", flags).GetValue(view);
            var at = (System.Array)typeof(FallView).GetField("_at", flags).GetValue(view);

            int owned = 0;
            foreach (var widget in at) if (widget != null) owned++;

            int drawn = 0;
            foreach (Transform child in well) if (child.gameObject.activeSelf) drawn++;

            Assert.AreEqual(owned, drawn,
                            when + ": " + drawn + " widget(s) are drawn on the board and the view " +
                            "owns " + owned + ". The difference is orphaned — still on screen, in " +
                            "nobody's index, so it will never repaint, never fall and never leave");

            Assert.AreEqual(view.Run.Board.Motes, owned,
                            when + ": the view owns " + owned + " widget(s) against " +
                            view.Run.Board.Motes + " occupied cell(s) in the model");
        }

        /// <summary>
        /// One drop, played through the real coroutine. Edit mode runs none, so it is stepped by
        /// hand: a wait is satisfied at once (<c>WaitForSecondsRealtime</c> is itself an
        /// <c>IEnumerator</c>, so it has to be told apart from a nested coroutine or the pump
        /// spins on it for ever), and the tweens are then run out.
        /// </summary>
        static void PumpDrop(FallView view, int column)
        {
            var flags = System.Reflection.BindingFlags.Instance
                      | System.Reflection.BindingFlags.NonPublic;

            int colour = view.Run.Next;
            int row = view.Run.Board.Landing(colour, column);
            bool taken = view.Run.Board.Takes(colour, column);

            var result = view.Run.Drop(column);
            Assert.IsNotNull(result, "the drop was refused");

            var body = (System.Collections.IEnumerator)typeof(FallView)
                .GetMethod("PlayDrop", flags)
                .Invoke(view, new object[] { column, row, colour, taken, result });

            var stack = new System.Collections.Generic.Stack<System.Collections.IEnumerator>();
            stack.Push(body);

            for (int step = 0; stack.Count > 0; step++)
            {
                Assert.Less(step, 20000, "the drop never finished");

                var top = stack.Peek();
                if (!top.MoveNext()) { stack.Pop(); continue; }

                object now = top.Current;
                if (now is YieldInstruction || now is CustomYieldInstruction) continue;

                if (now is System.Collections.IEnumerator nested) stack.Push(nested);
            }

            for (int frame = 0; frame < 200; frame++) Tween.Inst.Tick(.05f, .05f);
        }
    }
}
