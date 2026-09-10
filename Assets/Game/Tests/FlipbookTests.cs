using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// One flipbook per image, however many were attached in one frame.
    ///
    /// <para>
    /// The grove's tiles are rebound on every event, and two events land in one frame
    /// whenever a placement's event and the art's arrival coincide — which is what "working
    /// rapidly" does. A flipbook attached over a running one used to be stopped with a single
    /// <c>GetComponent</c>, which finds the first and leaves the second running; that survivor
    /// went on painting its frames into an image later re-sized and re-sprited for another
    /// piece. Reported as objects drawing much smaller than they should, because a lantern's
    /// flame inside a fence's box is a lantern a third of its size.
    /// </para>
    /// </summary>
    public sealed class FlipbookTests
    {
        static Sprite[] Frames(int count)
        {
            var frames = new Sprite[count];
            var texture = new Texture2D(4, 4);
            for (int i = 0; i < count; i++)
            {
                frames[i] = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f));
                frames[i].name = "f" + i;
            }
            return frames;
        }

        /// <summary>
        /// <c>Detach</c> ends a flipbook with <c>Object.Destroy</c>, which is right in a build
        /// and refused in edit mode with an error log NUnit fails the case on. Declared here
        /// rather than taught to the shipping code — the rule <c>Flow.Dismiss</c>'s tests
        /// follow, for its reason.
        /// </summary>
        static void ExpectEditModeDestroy(int count)
        {
            for (int i = 0; i < count; i++)
                LogAssert.Expect(LogType.Error, new Regex("Destroy may not be called from edit mode"));
        }

        /// <summary>
        /// A flipbook drives its image only while enabled — <c>Detach</c> disables before it
        /// destroys, because destruction lands at the end of the frame — so "running" is
        /// exactly "enabled", and the test reads the component's own state rather than a
        /// counter added to the shipping code for it.
        /// </summary>
        static int Running(Image img)
        {
            int n = 0;
            foreach (var f in img.GetComponents<Flipbook>())
                if (f != null && f.enabled) n++;
            return n;
        }

        [Test]
        public void AttachingTwiceInOneFrameLeavesExactlyOneFlipbookRunning()
        {
            var go = new GameObject("img", typeof(Image));
            try
            {
                var img = go.GetComponent<Image>();

                if (!Application.isPlaying) ExpectEditModeDestroy(3);

                Flipbook.Attach(img, Frames(3), 12f);
                Flipbook.Attach(img, Frames(3), 12f);
                Flipbook.Attach(img, Frames(3), 12f);

                Assert.AreEqual(1, Running(img), "every earlier one is stopped, not only the first");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// The reason <c>Ensure</c> exists, stated as the symptom that bought it.
        ///
        /// <para>
        /// A grove tile and a shop cell are rebound on every repaint, and a repaint is raised by
        /// the ledger, the layout, the wallet and the art scope — three of which a sync raises a
        /// few seconds after every placement, because adopting a merge re-reads the whole save.
        /// Restarting there snapped every animated piece on the screen back to its first frame
        /// at once. Reported from a device as the tiles reloading while the player was building,
        /// and as the shop refreshing when they bought something.
        /// </para>
        /// <para>
        /// "Not restarted" is read as the image still showing the frame it had reached, which is
        /// the thing a player actually sees, rather than as a counter added to the shipping code
        /// for the test to look at.
        /// </para>
        /// </summary>
        [Test]
        public void EnsuringTheReelAlreadyPlayingDoesNotRewindIt()
        {
            var go = new GameObject("img", typeof(Image));
            try
            {
                var img = go.GetComponent<Image>();
                var reel = Frames(4);

                var first = Flipbook.Attach(img, reel, 12f);

                // Where Update would have carried it by the time anything repainted.
                img.sprite = reel[2];

                var again = Flipbook.Ensure(img, reel, 12f);

                Assert.AreSame(first, again, "the reel already running is the one that carries on");
                Assert.AreSame(reel[2], img.sprite, "it was rewound to frame nought");
                Assert.AreEqual(1, Running(img));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// A browse atlas assembles its frames per call (<c>HomesteadArt.ThumbFrames</c>), so
        /// the two arrays a shop cell hands in on consecutive binds are never the same array. A
        /// reference test alone would therefore leave the shop restarting exactly as before,
        /// which is half of what this was written for.
        /// </summary>
        [Test]
        public void TheSameFramesInADifferentArrayAreTheSameReel()
        {
            var go = new GameObject("img", typeof(Image));
            try
            {
                var img = go.GetComponent<Image>();
                var reel = Frames(3);

                var first = Flipbook.Attach(img, reel, 12f);
                img.sprite = reel[1];

                var again = Flipbook.Ensure(img, (Sprite[])reel.Clone(), 12f);

                Assert.AreSame(first, again);
                Assert.AreSame(reel[1], img.sprite);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>Different pictures are a different reel, however alike the call looks.</summary>
        [Test]
        public void EnsuringADifferentReelReplacesTheOneRunning()
        {
            var go = new GameObject("img", typeof(Image));
            try
            {
                var img = go.GetComponent<Image>();

                if (!Application.isPlaying) ExpectEditModeDestroy(1);

                var first = Flipbook.Attach(img, Frames(3), 12f);
                var second = Flipbook.Ensure(img, Frames(3), 12f);

                Assert.AreNotSame(first, second);
                Assert.AreEqual(1, Running(img));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// <b>A loop is a state and a one-shot is an event</b>, which is the whole of the rule.
        /// A burst asked for a second time is a second burst — the pooled effects on the siege
        /// board lend one widget to event after event — so nothing that does not loop is ever
        /// adopted.
        /// </summary>
        [Test]
        public void EnsuringAOneShotAlwaysPlaysItAgain()
        {
            var go = new GameObject("img", typeof(Image));
            try
            {
                var img = go.GetComponent<Image>();
                var reel = Frames(3);

                if (!Application.isPlaying) ExpectEditModeDestroy(1);

                var first = Flipbook.Attach(img, reel, 12f, loop: false);
                img.sprite = reel[2];

                var again = Flipbook.Ensure(img, reel, 12f, loop: false);

                Assert.AreNotSame(first, again, "a one-shot asked for again is asked for again");
                Assert.AreSame(reel[0], img.sprite);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// <c>Attach</c> keeps its meaning. Both verbs exist so a caller can say which it means,
        /// and a redraw quietly becoming the only behaviour would take the other one away.
        /// </summary>
        [Test]
        public void AttachingTheSameReelStillStartsItOver()
        {
            var go = new GameObject("img", typeof(Image));
            try
            {
                var img = go.GetComponent<Image>();
                var reel = Frames(4);

                if (!Application.isPlaying) ExpectEditModeDestroy(1);

                var first = Flipbook.Attach(img, reel, 12f);
                img.sprite = reel[3];

                var again = Flipbook.Attach(img, reel, 12f);

                Assert.AreNotSame(first, again);
                Assert.AreSame(reel[0], img.sprite, "an event replays from the beginning");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// A stack of two is the fault <see cref="Flipbook.Attach"/>'s own note is about, and
        /// adopting one of them would be this fix quietly keeping the bug it was built beside.
        /// </summary>
        [Test]
        public void EnsureCollapsesAStackRatherThanAdoptingOneOfIt()
        {
            var go = new GameObject("img", typeof(Image));
            try
            {
                var img = go.GetComponent<Image>();
                var reel = Frames(3);

                if (!Application.isPlaying) ExpectEditModeDestroy(2);

                // Two at once, as a caller reaching past Attach could leave them.
                go.AddComponent<Flipbook>();
                go.AddComponent<Flipbook>();

                Flipbook.Ensure(img, reel, 12f);

                Assert.AreEqual(1, Running(img));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void DetachStopsEveryFlipbookSoAStillSpriteIsNotOverwritten()
        {
            var go = new GameObject("img", typeof(Image));
            try
            {
                var img = go.GetComponent<Image>();

                if (!Application.isPlaying) ExpectEditModeDestroy(1);

                Flipbook.Attach(img, Frames(2), 12f);
                Flipbook.Detach(img);

                Assert.AreEqual(0, Running(img));

                var still = Frames(1)[0];
                img.sprite = still;
                Assert.AreSame(still, img.sprite);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
