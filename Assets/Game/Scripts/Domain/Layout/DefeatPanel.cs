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
    /// which <c>ReadoutRow</c>, <c>RippleBand</c> and <c>PanelStack</c> have already earned three
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
        /// <summary>
        /// The rows above the buttons, as centres counted down from the panel's top.
        ///
        /// <para>
        /// <b>They moved up when the reason line went.</b> The panel used to open with a
        /// sentence explaining the defeat — "the groove grew tired before the glade woke" — which
        /// restated the title underneath it and was reported from play as noise at the one moment
        /// nobody is reading prose. Taking it out left a hundred and fifty units of nothing, so
        /// everything below it came up by exactly that, and the numbers are named here rather
        /// than typed into the overlay so the shift is one edit and the check below can see it.
        /// </para>
        /// </summary>
        public const float CloseCentre = 150f, CloseHeight = 74f;
        public const float HeartsCentre = 250f, HeartsHeight = 120f;

        /// <summary>
        /// Where the panel's paper is clear of its own title ribbon, and the top of the room a
        /// row above the buttons has to itself.
        ///
        /// <para>
        /// Measured rather than guessed. <c>ModalView.MakePanel</c> hangs a 130-tall ribbon at
        /// +22 from the panel's top edge, so it reaches 43 units <em>down</em> into the paper;
        /// the rest is the air any row would want under it. Anything centred in a region that
        /// started at the panel's own edge would be centred partly behind the ribbon, which is
        /// how a row ends up looking pushed down onto whatever is below it.
        /// </para>
        /// </summary>
        public const float PaperTop = 70f;
        public const float FreeHeight = 96f;

        /// <summary>
        /// The free-glade line's centre: the middle of whatever room is actually left between
        /// the rows above it and the buttons below.
        ///
        /// <para>
        /// <b>Derived rather than typed, because the room it has is not a constant.</b> The
        /// near-miss line's slot is always reserved and only sometimes filled, so on the runs
        /// that were not close — which is most of them, and all of the early ones this line is
        /// written for — there are seventy-four units of empty paper above it that nothing was
        /// using. A typed centre spent that void above the sentence and left the sentence
        /// sitting on the try-again button, which is how it was reported: too close to the
        /// button, with a hole over it.
        /// </para>
        /// <para>
        /// The heart row this replaces is deliberately <em>not</em> derived the same way. It is
        /// a row of icons rather than a paragraph, so it reads as a block placed on the panel
        /// wherever it is put, and moving something that ships on every ordinary defeat to fix
        /// a complaint about something else is how a panel acquires two problems.
        /// </para>
        /// </summary>
        /// <param name="close">Whether the near-miss line is drawn above it.</param>
        public static float FreeCentre(bool close)
            => ((close ? CloseCentre + CloseHeight * .5f : PaperTop) + StackTop) * .5f;

        /// <summary>Where the buttons begin, clear of every row above them.</summary>
        public const float StackTop = 336f;

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
