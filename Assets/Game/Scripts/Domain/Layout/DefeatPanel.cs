namespace GlimmerGrove.Layout
{
    /// <summary>
    /// Where a defeat panel's ways out sit, and how tall the panel has to be to hold them.
    ///
    /// <para>
    /// Every number is a centre, in canvas reference units measured <em>down</em> from the
    /// panel's top edge — the direction a panel is read in and the opposite of the sign
    /// <c>UIKit.Box</c> takes, so a caller negates once at the point of placement. A row that
    /// is not being drawn reads <see cref="Absent"/>; ask the matching <c>Has</c> flag rather
    /// than testing the number.
    /// </para>
    /// </summary>
    public readonly struct DefeatStack
    {
        internal DefeatStack(float retry, float note, float watch, float rescue,
                             float glades, float height)
        {
            Retry = retry;
            Note = note;
            Watch = watch;
            Rescue = rescue;
            Glades = glades;
            Height = height;
        }

        /// <summary>What a row that is not drawn reads.</summary>
        public const float Absent = -1f;

        /// <summary>The try-again button, drawn only when there is a heart to spend.</summary>
        public readonly float Retry;

        /// <summary>The line that explains the wait, drawn only when there is not.</summary>
        public readonly float Note;

        /// <summary>The rewarded video, when one is loaded and the day's allowance has room.</summary>
        public readonly float Watch;

        /// <summary>Hearts for gems. See <c>HeartRescue</c>.</summary>
        public readonly float Rescue;

        /// <summary>Back to the map. Always drawn — it is the exit that always works.</summary>
        public readonly float Glades;

        /// <summary>How tall the panel has to be.</summary>
        public readonly float Height;

        public bool HasRetry => Retry > Absent;
        public bool HasNote => Note > Absent;
        public bool HasWatch => Watch > Absent;
        public bool HasRescue => Rescue > Absent;
    }

    /// <summary>
    /// The defeat panel's action stack: what it can hold, in what order, and how tall it comes
    /// out.
    ///
    /// <para>
    /// <b>Here rather than beside the panel, for <c>ChapterMap</c>'s reason</b> (invariant 8a),
    /// which <c>ReadoutRow</c>, <c>WeaveBand</c> and <c>PanelStack</c> have already earned three
    /// times: whether two things on a screen overlap is arithmetic, and arithmetic inside a
    /// <c>MonoBehaviour</c> is arithmetic nothing can check. It became worth separating when the
    /// panel grew a third way out — hearts for gems — because that took the number of shapes it
    /// can take from three to five, and the height had been a pair of hand-written constants
    /// (880 and 1010) with the button offsets under them written out one at a time. That is the
    /// arrangement <c>PanelStack</c> was lifted out of a panel that had been drawing its last
    /// paragraph 78 units into its own close button, and the lesson is the same: a panel whose
    /// section count varies with content must derive its height.
    /// </para>
    /// <para>
    /// <b>The order is deliberate and is not a layout decision.</b> The free way back
    /// (<see cref="DefeatStack.Watch"/>) is always above the paid one
    /// (<see cref="DefeatStack.Rescue"/>). A panel that puts a price above a video at the moment
    /// somebody has just been stopped from playing is the shape a store reviewer is right to
    /// call a dark pattern, and it is the shape that gets a build refused rather than a metric
    /// moved.
    /// </para>
    /// </summary>
    public static class DefeatPanel
    {
        /// <summary>The panel's width. Unchanged from when the heights were typed.</summary>
        public const float Width = 880f;

        /// <summary>
        /// Where the stack begins, clear of everything above it.
        ///
        /// <para>
        /// The deepest thing above is the free-glade line, whose box is centred 424 down and is
        /// 96 tall, so it ends 472 down; the heart row it replaces ends 460. Fourteen units of
        /// air, which is what the old hand-written offsets happened to leave and is kept so the
        /// two states that already shipped are drawn where they always were.
        /// </para>
        /// </summary>
        public const float StackTop = 486f;

        /// <summary>The line that says why there is no retry button. Wrapped, so it is two deep.</summary>
        public const float NoteHeight = 130f;

        /// <summary>A way back in: the rewarded video, or hearts for gems.</summary>
        public const float ActionHeight = 140f;

        /// <summary>Try again. Taller than the others because it is the one being pointed at.</summary>
        public const float RetryHeight = 148f;

        /// <summary>Back to the map.</summary>
        public const float GladesHeight = 132f;

        /// <summary>Clear air between one row and the next.</summary>
        public const float Gap = 16f;

        /// <summary>Clear air under the last row, inside the panel.</summary>
        public const float FootRoom = 90f;

        /// <summary>
        /// The stack for one defeat.
        ///
        /// <paramref name="watching"/> and <paramref name="rescuing"/> are ignored when there
        /// is a heart to spend: a player who can already play is not sold a way to play, which
        /// is the rule that keeps a defeat from being an advertisement.
        /// </summary>
        public static DefeatStack Of(bool canRetry, bool watching, bool rescuing)
        {
            float y = StackTop;
            float retry = DefeatStack.Absent, note = DefeatStack.Absent;
            float watch = DefeatStack.Absent, rescue = DefeatStack.Absent;

            if (canRetry)
            {
                retry = y + RetryHeight * .5f;
                y += RetryHeight + Gap;
            }
            else
            {
                note = y + NoteHeight * .5f;
                y += NoteHeight + Gap;

                // Free before paid. See the class remarks — this ordering is the reason the
                // rule is here rather than in the two `if` arms it used to be spread across.
                if (watching)
                {
                    watch = y + ActionHeight * .5f;
                    y += ActionHeight + Gap;
                }

                if (rescuing)
                {
                    rescue = y + ActionHeight * .5f;
                    y += ActionHeight + Gap;
                }
            }

            float glades = y + GladesHeight * .5f;
            y += GladesHeight + FootRoom;

            return new DefeatStack(retry, note, watch, rescue, glades, y);
        }

        /// <summary>
        /// The tallest this panel ever gets, over every shape it can take.
        ///
        /// Derived by asking rather than reasoned about, so a row added above cannot leave a
        /// stale number behind it. <c>DefeatPanelTests</c> holds it under
        /// <see cref="PanelStack.TallestPanel"/>, which is the shortest canvas this game is
        /// drawn on once the title ribbon's overhang is counted at both ends.
        /// </summary>
        public static float Tallest
        {
            get
            {
                float most = 0f;

                for (int i = 0; i < 8; i++)
                {
                    var stack = Of((i & 1) != 0, (i & 2) != 0, (i & 4) != 0);
                    if (stack.Height > most) most = stack.Height;
                }

                return most;
            }
        }
    }
}
