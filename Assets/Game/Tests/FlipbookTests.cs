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
