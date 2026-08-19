using System.Globalization;
using System.Threading;
using GlimmerGrove.Localization;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The rule that decides how wide a balance is allowed to be.
    ///
    /// <para>
    /// This suite exists for the reason <c>TweenCycleTests</c> does: the failure it guards
    /// against cannot be seen in a screenshot taken today. A formatter that overstates a
    /// balance is invisible until somebody's purse passes ten thousand, and then it shows up
    /// as a player tapping BUY on something they cannot afford — a bug reported as "the shop
    /// is broken", three steps from the arithmetic that caused it. So the arithmetic is run
    /// here, offline, over the values nobody has reached yet.
    /// </para>
    /// </summary>
    public sealed class CompactTests
    {
        static string N(long value) => Compact.Number(value, "K", "M");

        [Test]
        public void EverythingUpToFourDigitsIsWrittenOutInFull()
        {
            Assert.AreEqual("0", N(0));
            Assert.AreEqual("7", N(7));
            Assert.AreEqual("1250", N(1250));
            Assert.AreEqual("9999", N(Compact.LargestExact));
        }

        /// <summary>
        /// The boundary is the whole specification, so it is pinned on both sides: 9,999 is
        /// the last figure shown in full and 10,000 is the first shown short.
        /// </summary>
        [Test]
        public void TenThousandIsTheFirstFigureToBeAbbreviated()
        {
            Assert.AreEqual("9999", N(9_999));
            Assert.AreEqual("10K", N(10_000));
        }

        [Test]
        public void ThousandsCarryOneDecimalAndDropItWhenItIsZero()
        {
            Assert.AreEqual("10K", N(10_000));
            Assert.AreEqual("10.1K", N(10_100));
            Assert.AreEqual("12.3K", N(12_345));
            Assert.AreEqual("500K", N(500_000));
            Assert.AreEqual("999.9K", N(999_999));
        }

        [Test]
        public void MillionsTakeOverAtSevenDigits()
        {
            Assert.AreEqual("1M", N(1_000_000));
            Assert.AreEqual("1.2M", N(1_250_000));
            Assert.AreEqual("9.9M", N(9_999_999));
        }

        /// <summary>
        /// The one decision in this file worth not re-litigating. A balance is a promise
        /// about what can be spent, so the error has to fall on the side of understating it:
        /// 12,999 must never read as "13K" beside a 13,000 price, or the player is told they
        /// can afford something and then refused.
        /// </summary>
        [Test]
        public void ItTruncatesAndNeverRoundsUp()
        {
            Assert.AreEqual("10.9K", N(10_999));
            Assert.AreEqual("12.9K", N(12_999));
            Assert.AreEqual("999.9K", N(999_999));
            Assert.AreEqual("9.9M", N(9_999_999));
        }

        /// <summary>
        /// The same claim as above, made over a range rather than at four points: whatever is
        /// shown, read back at face value, is never more than what is held.
        /// </summary>
        [Test]
        public void NothingItShowsIsEverMoreThanWhatIsHeld()
        {
            for (long value = 10_000; value < 2_000_000; value += 997)
            {
                string shown = N(value);
                long scale = shown.EndsWith("M") ? 1_000_000L : 1_000L;
                double head = double.Parse(shown.Substring(0, shown.Length - 1),
                                           CultureInfo.InvariantCulture);

                Assert.LessOrEqual(head * scale, value, "{0} shown as {1}", value, shown);
            }
        }

        /// <summary>
        /// The suffixes are handed in rather than baked in, because they are player-facing
        /// text and a thousand is not called "K" everywhere (invariant 6).
        /// </summary>
        [Test]
        public void TheSuffixesComeFromTheCaller()
        {
            Assert.AreEqual("10.1Tsd.", Compact.Number(10_100, "Tsd.", "Mio."));
            Assert.AreEqual("1.2Mio.", Compact.Number(1_250_000, "Tsd.", "Mio."));
        }

        /// <summary>
        /// A comma decimal separator would make "10,1K" read as a thousands separator next to
        /// a "1,250" price — the one misreading a currency display cannot afford. The device
        /// culture is whatever the player's phone is set to, so this is not hypothetical.
        /// </summary>
        [Test]
        public void TheDecimalPointDoesNotFollowTheDeviceCulture()
        {
            var before = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                Assert.AreEqual("10.1K", N(10_100));
                Assert.AreEqual("1.2M", N(1_250_000));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = before;
            }
        }

        /// <summary>
        /// Currency is never negative, but a sentinel is — <c>wallet.coins</c> is -1 on a save
        /// that has not derived one yet — and a formatter that garbles one hides the bug
        /// instead of showing it.
        /// </summary>
        [Test]
        public void NegativesKeepTheirSignAndTheirMagnitude()
        {
            Assert.AreEqual("-1", N(-1));
            Assert.AreEqual("-9999", N(-9_999));
            Assert.AreEqual("-12.3K", N(-12_345));
            Assert.AreEqual("-1.2M", N(-1_250_000));
        }

        /// <summary>
        /// <see cref="long.MinValue"/> has no positive counterpart, so any implementation that
        /// reaches for an absolute value wraps here and prints a positive number for a
        /// negative one. Nothing should ever hand it one; it must not lie if something does.
        /// </summary>
        [Test]
        public void TheExtremesDoNotWrap()
        {
            StringAssert.StartsWith("-", N(long.MinValue));
            StringAssert.EndsWith("M", N(long.MinValue));
            StringAssert.EndsWith("M", N(long.MaxValue));
            Assert.IsFalse(N(long.MaxValue).StartsWith("-"));
        }
    }
}
