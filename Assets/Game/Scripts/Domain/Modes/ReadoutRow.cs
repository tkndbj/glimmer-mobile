namespace GlimmerGrove.Modes
{
    /// <summary>
    /// Where the numbers under a mode's header sit, for a row of one, two or three of them.
    ///
    /// <para>
    /// <b>Here rather than beside the row, for <c>ChapterMap</c>'s reason</b> (invariant 8a):
    /// whether two things on a screen overlap is arithmetic, and arithmetic inside a
    /// <c>MonoBehaviour</c> is arithmetic nothing can check. It became worth separating the
    /// moment the row stopped always holding three — a count that varies is a spacing rule with
    /// cases in it, and a case nobody exercises is a case nobody has looked at.
    /// </para>
    /// <para>
    /// Reference units from the centre of the row, which is what <c>UIKit.Box</c> takes. A
    /// readout is <see cref="Width"/> wide whatever is written in it, because the text shrinks
    /// to fit rather than growing (<c>UIKit.Shrinkable</c>) — so overlap is decided by the
    /// spacing alone and can be settled here, once, for every mode.
    /// </para>
    /// </summary>
    public static class ReadoutRow
    {
        /// <summary>The most numbers a mode may show at once. A fourth does not fit the width.</summary>
        public const int Most = 3;

        /// <summary>How wide one readout is, and the least clear air two may leave between them.</summary>
        public const float Width = 220f, MinGap = 16f;

        /// <summary>How far apart a pair sits, and the step between three.</summary>
        public const float PairSpread = 170f, TripleStep = 250f;

        /// <summary>
        /// How far in from the safe area's edge a header key reaches.
        ///
        /// <para>
        /// <b>It matters because the row moved up beside them.</b> The readouts used to sit in a
        /// band of their own below the header bar, so the only question about their spacing was
        /// each other's; they are level with the bar's own two keys now, which bought the board
        /// sixty-odd units of height and made this a second question — and one that nothing on
        /// screen would answer, because a number drawn over a button is still perfectly legible
        /// and is simply somebody's tap going somewhere else.
        /// </para>
        /// <para>
        /// A key is 118 wide with its centre 102 in from the edge, so it reaches 161. Kept here
        /// rather than in the screen for this class's own reason: whether two things on a screen
        /// overlap is arithmetic, and arithmetic inside a <c>MonoBehaviour</c> is arithmetic
        /// nothing can check.
        /// </para>
        /// </summary>
        public const float KeyReach = 161f;

        /// <summary>
        /// The centre of one slot, in reference units either side of the row's middle.
        ///
        /// One number sits in the middle, where the eye already is. Two straddle it rather than
        /// taking the outer thirds, because a gap in the centre of a row of two reads as a third
        /// number that failed to draw.
        /// </summary>
        public static float XFor(int index, int count)
        {
            if (count <= 1) return 0f;
            if (count == 2) return index == 0 ? -PairSpread : PairSpread;

            return (index - 1) * TripleStep;
        }

        /// <summary>
        /// Whether a row of this many leaves clear air between every neighbour.
        ///
        /// <paramref name="fault"/> names the first pair that does not, so a failure reads as an
        /// instruction rather than as a boolean.
        /// </summary>
        public static bool IsClear(int count, out string fault)
        {
            fault = null;
            if (count < 1 || count > Most)
            {
                fault = $"a row of {count} is outside the one to {Most} this row can hold";
                return false;
            }

            for (int i = 1; i < count; i++)
            {
                float gap = XFor(i, count) - XFor(i - 1, count) - Width;
                if (gap >= MinGap) continue;

                fault = $"in a row of {count}, readouts {i - 1} and {i} leave {gap:0} between " +
                        $"them, which is under the {MinGap:0} two numbers need to read as two";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Whether a row of this many keeps clear of the header's two corner keys, on a safe area
        /// <paramref name="width"/> units across.
        ///
        /// <para>
        /// <b>Width-dependent, which is why it is asked separately from <see cref="IsClear"/>.</b>
        /// The slots are placed from the row's middle and the keys from its edges, so the gap
        /// between them is the only thing on this row that shrinks as the display narrows — and
        /// the narrowest this canvas ever is is its own reference width. Everything wider is
        /// slack.
        /// </para>
        /// </summary>
        public static bool ClearsTheKeys(int count, float width, out string fault)
        {
            fault = null;
            if (count < 1 || count > Most) return IsClear(count, out fault);

            float room = width * .5f - KeyReach;

            for (int i = 0; i < count; i++)
            {
                float edge = System.Math.Abs(XFor(i, count)) + Width * .5f;
                if (edge <= room) continue;

                fault = $"in a row of {count} on a {width:0}-unit display, readout {i} reaches " +
                        $"{edge:0} from the middle, which is {edge - room:0} past the {room:0} a " +
                        "header key leaves";
                return false;
            }

            return true;
        }
    }
}
