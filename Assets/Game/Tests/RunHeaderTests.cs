using GlimmerGrove.Layout;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Where the level's number sits in a run's header, and what it has to stay out of the way
    /// of.
    ///
    /// <para>
    /// <b>The tag has no horizontal room at all, which is why this is a fixture rather than a
    /// remark.</b> A header's middle is where the readouts live now (invariant 37an), and a row
    /// of two reaches within forty-odd units of where the tag ends while a row of three runs
    /// straight through it — so nothing about their x separates them and the whole arrangement
    /// rests on the tag being <em>short</em> and hanging from the corner key's own centre. That
    /// is arithmetic over four constants in two screens, which is exactly the shape
    /// <c>ReadoutRow</c> and <c>ChapterMap</c> exist for: a <c>MonoBehaviour</c> cannot be asked
    /// whether two things overlap, and the readout type has already been raised once and rolled
    /// back, which is the change that would land on this.
    /// </para>
    /// </summary>
    public sealed class RunHeaderTests
    {
        /// <summary>
        /// The two headers are built by two different classes — the glade draws its own and the
        /// other four share <c>ModeScreen</c>'s — so each is asked separately about the first
        /// thing it draws under the bar.
        /// </summary>
        [Test]
        public void TheLevelTagClearsWhateverEachHeaderDrawsUnderIt()
        {
            float shared = RunScreen.TagFoot(ModeScreen.BarHeight, ModeScreen.KeyY);
            Assert.Less(shared, ModeScreen.ReadoutTop,
                        $"the level tag reaches {shared:0} below the safe area's top edge and the " +
                        $"readout values start at {ModeScreen.ReadoutTop:0}, so a run's number is " +
                        "drawn over what the run is counting");

            float glade = RunScreen.TagFoot(PlayScreen.BarHeight, PlayScreen.KeyY);
            Assert.Less(glade, PlayScreen.StatusTop,
                        $"the level tag reaches {glade:0} below the safe area's top edge and the " +
                        $"glade's status row starts at {PlayScreen.StatusTop:0}");
        }

        /// <summary>
        /// It starts clear of the key it sits beside and finishes clear of the one in the other
        /// corner — the second half only binds on the narrowest canvas, for
        /// <c>ReadoutRow.ClearsTheKeys</c>' reason: the tag is placed from the left edge and the
        /// far key from the right, so the air between them is the only thing here that shrinks
        /// with the display.
        /// </summary>
        [Test]
        public void TheLevelTagStandsBetweenTheTwoCornerKeys()
        {
            float left = ReadoutRow.KeyReach + RunScreen.TagGap;
            Assert.GreaterOrEqual(left, ReadoutRow.KeyReach, "the tag starts inside the way back");

            float right = left + RunScreen.TagWidth;
            float farKey = CanvasFit.PhoneWidth - ReadoutRow.KeyReach;
            Assert.Less(right, farKey,
                        $"on a {CanvasFit.PhoneWidth:0}-unit display the tag reaches {right:0} and " +
                        $"the header's far key starts at {farKey:0}");
        }
    }
}
