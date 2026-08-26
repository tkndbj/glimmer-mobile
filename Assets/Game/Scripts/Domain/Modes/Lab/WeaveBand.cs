namespace GlimmerGrove.Modes
{
    /// <summary>
    /// The band under a Lightweave grove: where the undo key sits, where the standing line sits,
    /// and where the board is allowed to start.
    ///
    /// <para>
    /// <b>Here, in Domain, for <c>ChapterMap</c>'s reason.</b> Whether two things on a screen
    /// overlap is arithmetic, and arithmetic in a <c>MonoBehaviour</c> is arithmetic nothing can
    /// check — the map's node positions were moved here precisely so a validator could prove
    /// nodes do not collide, and a screen's furniture is the same problem one size down. It was
    /// a paragraph of prose explaining why three numbers cleared each other, and the paragraph
    /// was wrong the first time it was written: <c>UIKit.Box</c> always pivots at centre, so a
    /// plate anchored to the floor of the screen sits half below where its author expected. That
    /// is not a mistake a screenshot on one aspect ratio reliably catches, and it is not a
    /// mistake a comment can prevent.
    /// </para>
    /// <para>
    /// Everything is a <b>centre</b> and a <b>height</b>, in reference units above the safe
    /// area's floor, because that is what <c>UIKit.Box</c> actually takes — a table of edges
    /// would be a second set of numbers to keep in step with the first.
    /// </para>
    /// </summary>
    public static class WeaveBand
    {
        /// <summary>The undo key: bottom centre, where a hand already is.</summary>
        public const float UndoSize = 140f;
        public const float UndoCentre = 98f;

        /// <summary>
        /// The standing line that says a ring is still waiting — above the key, never beside it.
        ///
        /// Side by side is how a full-width plate and a round button come to overlap on the one
        /// aspect ratio nobody checked. Stacking costs nothing: the plate is up for a handful of
        /// seconds in a run that has it at all, and the key below is the control it is asking the
        /// player to reach for.
        /// </summary>
        public const float NoticeHeight = 132f;
        public const float NoticeCentre = 250f;

        /// <summary>How far above the safe area's floor the grove itself may start.</summary>
        public const float BoardFloor = 330f;

        /// <summary>
        /// The least clear air allowed between two things in this band.
        ///
        /// Not zero. Two controls that merely fail to overlap still read as one shape, and a
        /// finger aimed at the top of the key on a small screen lands on the plate above it.
        /// </summary>
        public const float MinGap = 12f;

        public static float UndoTop => UndoCentre + UndoSize * .5f;
        public static float UndoBottom => UndoCentre - UndoSize * .5f;

        public static float NoticeTop => NoticeCentre + NoticeHeight * .5f;
        public static float NoticeBottom => NoticeCentre - NoticeHeight * .5f;

        /// <summary>Clear air between the key and the plate above it.</summary>
        public static float KeyToNotice => NoticeBottom - UndoTop;

        /// <summary>Clear air between the plate and the board above it.</summary>
        public static float NoticeToBoard => BoardFloor - NoticeTop;

        /// <summary>
        /// Whether the band is laid out legally: nothing off the bottom of the screen, and
        /// nothing sitting on anything else.
        ///
        /// <paramref name="fault"/> names the first thing wrong, so a failure reads as an
        /// instruction rather than as a boolean.
        /// </summary>
        public static bool IsClear(out string fault)
        {
            fault = null;

            if (UndoBottom < MinGap)
            {
                fault = $"the undo key reaches {UndoBottom:0} above the safe area's floor, " +
                        $"which is under the {MinGap:0} of clear air anything here needs";
                return false;
            }

            if (KeyToNotice < MinGap)
            {
                fault = $"the undo key ends at {UndoTop:0} and the standing line begins at " +
                        $"{NoticeBottom:0}, leaving {KeyToNotice:0} — they read as one shape, " +
                        $"and under nought they are drawn on top of each other";
                return false;
            }

            if (NoticeToBoard < 0f)
            {
                fault = $"the standing line reaches {NoticeTop:0} and the grove starts at " +
                        $"{BoardFloor:0}, so the line is drawn over cells the player has to " +
                        "drag through";
                return false;
            }

            return true;
        }
    }
}
