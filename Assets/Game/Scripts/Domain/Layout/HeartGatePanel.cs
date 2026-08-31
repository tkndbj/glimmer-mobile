namespace GlimmerGrove.Layout
{
    /// <summary>
    /// Where the ways out of an empty heart bar sit, and how tall the panel has to be to hold
    /// them.
    ///
    /// <para>
    /// Centres, in canvas reference units measured <em>down</em> from the panel's top edge —
    /// the direction a panel is read in and the opposite of the sign <c>UIKit.Box</c> takes, so
    /// a caller negates once at the point of placement. A row that is not drawn reads
    /// <see cref="Absent"/>; ask <see cref="HasWatch"/> rather than testing the number.
    /// </para>
    /// </summary>
    public readonly struct HeartGateStack
    {
        internal HeartGateStack(float watch, float paid, float ok, float height)
        {
            Watch = watch;
            Paid = paid;
            Ok = ok;
            Height = height;
        }

        /// <summary>What a row that is not drawn reads.</summary>
        public const float Absent = -1f;

        /// <summary>The rewarded video, when one is loaded and the day's allowance has room.</summary>
        public readonly float Watch;

        /// <summary>
        /// The paid way back, which is two different controls depending on where the player
        /// met this.
        ///
        /// <para>
        /// With nothing standing behind the panel it is the shop, and it is always drawn:
        /// hearts sell for <em>gems</em>, which need no store connection and may already be in
        /// hand. Over a run it is the rescue — gems for hearts, without leaving the board — and
        /// it is conditional, because that offer can genuinely not exist (no gems, no store to
        /// buy them from, a bar too full to take them, or the price withdrawn from content).
        /// One slot either way, because the rule that matters about it is the same: it goes
        /// under the free way and above the way out that costs nothing.
        /// </para>
        /// </summary>
        public readonly float Paid;

        /// <summary>Away. Always drawn — it is the exit that costs nothing.</summary>
        public readonly float Ok;

        /// <summary>How tall the panel has to be.</summary>
        public readonly float Height;

        public bool HasWatch => Watch > Absent;

        /// <summary>Whether there is a paid way back worth drawing.</summary>
        public bool HasPaid => Paid > Absent;
    }

    /// <summary>
    /// The column of ways back to playing, on either panel that draws one.
    ///
    /// <para>
    /// <b>Here rather than beside the panel, for <c>ChapterMap</c>'s reason</b> (invariant 8a),
    /// which <c>ReadoutRow</c>, <c>RippleBand</c>, <c>PanelStack</c> and <see cref="DefeatPanel"/>
    /// have each earned: whether two things on a screen overlap is arithmetic, and arithmetic
    /// inside a <c>MonoBehaviour</c> is arithmetic nothing can check. This panel's height was
    /// two typed constants (900 and 780) with three button offsets written out under them, which
    /// was survivable while there were two buttons and stops being so at three.
    /// </para>
    /// <para>
    /// <b>The free way out is always above the paid one</b>, which is <see cref="DefeatPanel"/>'s
    /// rule and is not a layout preference: a panel that puts a price above a rewarded video at
    /// the moment somebody has been stopped from playing is the shape a store reviewer is right
    /// to object to. The paid way sits below the video and above the way out that costs nothing.
    /// </para>
    /// <para>
    /// <b>Two panels draw it and the arithmetic is shared rather than copied.</b>
    /// <c>OutOfHeartsOverlay</c> is raised where nothing is standing behind it — a refused map
    /// node, an event tile, the victory panel's next — so its paid row leaves for the shop.
    /// <c>RestartGateOverlay</c> is raised over a run in progress, where leaving would abandon
    /// that run without resolving it, so its paid row is the rescue and its gem shelf is
    /// stacked rather than navigated to (invariant 23). What the two share is every number
    /// here, which is the point: a second copy of this column is a second panel that can come
    /// to put a price above a video.
    /// </para>
    /// </summary>
    public static class HeartGatePanel
    {
        /// <summary>The panel's width. Unchanged from when the heights were typed.</summary>
        public const float Width = 860f;

        /// <summary>
        /// Where the column begins, clear of everything above it.
        ///
        /// The deepest thing above is the countdown, whose box is centred 500 down and is 84
        /// tall, so it ends 542 down. The clear air below it is wider than the gap between
        /// buttons for <c>PanelStack.FootGap</c>'s reason: a coloured pill the width of the
        /// panel eats visual space that a line of text does not.
        /// </summary>
        public const float StackTop = 566f;

        /// <summary>A way back to playing: the rewarded video, or the shop.</summary>
        public const float ActionHeight = 136f;

        /// <summary>Away. Shorter than the two above it, because it is not what is being offered.</summary>
        public const float OkHeight = 120f;

        /// <summary>Clear air between one row and the next.</summary>
        public const float Gap = 14f;

        /// <summary>Clear air under the last row, inside the panel.</summary>
        public const float FootRoom = 90f;

        /// <summary>
        /// The column for one visit.
        ///
        /// <para>
        /// <paramref name="watching"/> is whether a rewarded video is worth offering, and
        /// <paramref name="buying"/> whether there is a paid way back. Both are conditional and
        /// the order between them is not: see <see cref="HeartGateStack.Paid"/> for why one
        /// caller always passes <c>true</c> for the second and the other does not.
        /// </para>
        /// <para>
        /// A cursor rather than four offsets, so a row that is not drawn gives its room back to
        /// the rows under it and the panel is exactly as tall as what it holds. That is what
        /// the two typed constants this replaced could not do.
        /// </para>
        /// </summary>
        public static HeartGateStack Of(bool watching, bool buying)
        {
            float y = StackTop;
            float watch = HeartGateStack.Absent;
            float paid = HeartGateStack.Absent;

            if (watching)
            {
                watch = y + ActionHeight * .5f;
                y += ActionHeight + Gap;
            }

            if (buying)
            {
                paid = y + ActionHeight * .5f;
                y += ActionHeight + Gap;
            }

            float ok = y + OkHeight * .5f;
            y += OkHeight + FootRoom;

            return new HeartGateStack(watch, paid, ok, y);
        }

        /// <summary>
        /// The tallest this panel ever gets. Derived by asking every shape rather than reasoned
        /// about, so a row added above cannot leave a stale number behind it — and so that a
        /// second caller drawing a different combination cannot exceed a bound taken over the
        /// first caller's two.
        /// </summary>
        public static float Tallest
        {
            get
            {
                float tallest = 0f;

                foreach (bool watching in Both)
                    foreach (bool buying in Both)
                    {
                        float h = Of(watching, buying).Height;
                        if (h > tallest) tallest = h;
                    }

                return tallest;
            }
        }

        /// <summary>Both answers, so <see cref="Tallest"/> can walk every shape.</summary>
        static readonly bool[] Both = { false, true };
    }
}
