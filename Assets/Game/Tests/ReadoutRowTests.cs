using GlimmerGrove.Layout;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The row of numbers under a mode's header: whether it fits itself, and whether it fits
    /// beside the two keys it now shares a band with.
    ///
    /// <para>
    /// <b>The second question is new and is the one worth a fixture.</b> The row used to have a
    /// band of its own below the header bar, so the only thing its spacing could collide with was
    /// itself; it was moved up level with the bar's own keys to buy the siege board sixty-odd
    /// units of height, and the failure that buys is one nothing on screen would report — a
    /// number drawn over a button is perfectly legible and is simply somebody's tap going
    /// somewhere else.
    /// </para>
    /// </summary>
    public sealed class ReadoutRowTests
    {
        /// <summary>
        /// The narrowest canvas any display can produce, which is the only width that binds.
        ///
        /// The slots are placed from the row's middle and the keys from its edges, so the gap
        /// between them is the one thing on this row that shrinks as the display narrows.
        /// <c>CanvasFit</c> gives a phone <see cref="CanvasFit.PhoneWidth"/> and anything squarer
        /// something wider, so there is nothing under this.
        /// </summary>
        const float Narrowest = CanvasFit.PhoneWidth;

        [Test]
        public void EveryRowThisCanHoldLeavesClearAirBetweenItsOwnNumbers()
        {
            for (int n = 1; n <= ReadoutRow.Most; n++)
                Assert.IsTrue(ReadoutRow.IsClear(n, out string fault), $"{n} readouts: {fault}");
        }

        [Test]
        public void EveryRowThisCanHoldClearsTheHeaderKeysOnTheNarrowestDisplay()
        {
            for (int n = 1; n <= ReadoutRow.Most; n++)
                Assert.IsTrue(ReadoutRow.ClearsTheKeys(n, Narrowest, out string fault),
                              $"{n} readouts: {fault}");
        }

        /// <summary>
        /// Wider is only ever slack, so a display that is not a phone cannot fail where a phone
        /// passes. Stated rather than assumed, because it is the reason one width is enough.
        /// </summary>
        [Test]
        public void AWiderDisplayIsOnlyEverSlack()
        {
            foreach (float width in new[] { Narrowest, 1200f, 1440f, 2048f })
                for (int n = 1; n <= ReadoutRow.Most; n++)
                    Assert.IsTrue(ReadoutRow.ClearsTheKeys(n, width, out string fault),
                                  $"{n} readouts on {width}: {fault}");
        }

        /// <summary>
        /// The check has teeth: squeeze the display until the row cannot fit and it says so,
        /// naming the readout rather than answering a bare false.
        /// </summary>
        [Test]
        public void ItRefusesADisplayTooNarrowToHoldTheRowBesideTheKeys()
        {
            float tight = (ReadoutRow.TripleStep + ReadoutRow.Width * .5f
                           + ReadoutRow.KeyReach) * 2f - 40f;

            Assert.IsFalse(ReadoutRow.ClearsTheKeys(3, tight, out string fault));
            Assert.IsNotNull(fault);
            Assert.IsTrue(fault.Contains("header key"), fault);
        }
    }
}
