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
    }
}
