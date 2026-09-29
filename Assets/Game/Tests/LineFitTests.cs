using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// <see cref="UIKit.FitOneLine"/>: the arithmetic behind every one-line caption on a pill
    /// (<c>UIKit.OneLine</c>, <c>Btn.SetCaption</c>). The measure is passed in, so this runs
    /// offline; uGUI's own <c>preferredWidth</c> runs in no fixture here.
    ///
    /// <para>
    /// The promise under test is the one the challenge advert's key broke on a device: <b>a
    /// caption never draws wider than its room</b>, whatever its length - shrunk to the floor
    /// first, and past the floor scaled to exactly the room.
    /// </para>
    /// </summary>
    public sealed class LineFitTests
    {
        /// <summary>A width that grows with the size and the length, as a real face's roughly does.</summary>
        static System.Func<int, float> Measure(int characters) => size => characters * size * .6f;

        [Test]
        public void ACaptionThatFitsKeepsItsDesignSize()
        {
            var fit = UIKit.FitOneLine(36, 24, 240f, Measure(5));
            Assert.AreEqual(36, fit.Size);
            Assert.AreEqual(1f, fit.Scale);
        }

        [Test]
        public void ALongerCaptionShrinksToTheLargestSizeThatFits()
        {
            // 14 characters: 36 is 302 wide against 240; 28 is 235, 29 is 243.
            var fit = UIKit.FitOneLine(36, 24, 240f, Measure(14));
            Assert.AreEqual(28, fit.Size);
            Assert.AreEqual(1f, fit.Scale, "no scaling above the floor");
        }

        [Test]
        public void ACaptionTooWideAtTheFloorIsScaledToExactlyTheRoom()
        {
            // 33 characters at the floor of 24 is 475 wide against 240.
            var fit = UIKit.FitOneLine(36, 24, 240f, Measure(33));
            Assert.AreEqual(24, fit.Size, "the size stops at the floor");
            Assert.AreEqual(240f, Measure(33)(fit.Size) * fit.Scale, .01f, "and the line is scaled to the room");
        }

        /// <summary>The whole promise, swept: no length, room or floor draws past the room.</summary>
        [Test]
        public void NoCaptionEverDrawsWiderThanItsRoom()
        {
            foreach (float room in new[] { 40f, 120f, 240f, 600f })
                foreach (int floor in new[] { 1, 16, 24, 36 })
                    for (int characters = 1; characters <= 80; characters++)
                    {
                        var measure = Measure(characters);
                        var fit = UIKit.FitOneLine(36, floor, room, measure);

                        Assert.LessOrEqual(measure(fit.Size) * fit.Scale, room + .01f,
                                           $"{characters} characters in {room} at floor {floor}");
                        Assert.GreaterOrEqual(fit.Size, System.Math.Min(floor, 36));
                        Assert.LessOrEqual(fit.Scale, 1f);
                        Assert.IsTrue(fit.Scale >= 1f || fit.Size == System.Math.Min(floor, 36),
                                      "a line is only scaled once it is at the floor");
                    }
        }

        [Test]
        public void ADegenerateRoomOrMeasureChangesNothing()
        {
            Assert.AreEqual(1f, UIKit.FitOneLine(36, 24, 0f, Measure(10)).Scale);
            Assert.AreEqual(36, UIKit.FitOneLine(36, 24, 240f, null).Size);
            Assert.AreEqual(36, UIKit.FitOneLine(36, 48, 1000f, Measure(3)).Size, "a floor above the size is clamped to it");
        }
    }
}
