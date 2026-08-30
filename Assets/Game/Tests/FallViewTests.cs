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
    }
}
