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
        internal HeartGateStack(float watch, float shop, float ok, float height)
        {
            Watch = watch;
            Shop = shop;
            Ok = ok;
            Height = height;
        }

        /// <summary>What a row that is not drawn reads.</summary>
        public const float Absent = -1f;

        /// <summary>The rewarded video, when one is loaded and the day's allowance has room.</summary>
        public readonly float Watch;

        /// <summary>To the shop, where hearts are sold for gems. Always drawn.</summary>
        public readonly float Shop;

        /// <summary>Away. Always drawn — it is the exit that costs nothing.</summary>
        public readonly float Ok;

        /// <summary>How tall the panel has to be.</summary>
        public readonly float Height;

        public bool HasWatch => Watch > Absent;
    }

    /// <summary>
    /// The out-of-hearts panel's column of ways out.
    ///
    /// <para>
    /// <b>Here rather than beside the panel, for <c>ChapterMap</c>'s reason</b> (invariant 8a),
    /// which <c>ReadoutRow</c>, <c>WeaveBand</c>, <c>PanelStack</c> and <see cref="DefeatPanel"/>
    /// have each earned: whether two things on a screen overlap is arithmetic, and arithmetic
    /// inside a <c>MonoBehaviour</c> is arithmetic nothing can check. This panel's height was
    /// two typed constants (900 and 780) with three button offsets written out under them, which
    /// was survivable while there were two buttons and stops being so at three.
    /// </para>
    /// <para>
    /// <b>The free way out is always above the paid one</b>, which is <see cref="DefeatPanel"/>'s
    /// rule and is not a layout preference: a panel that puts a price above a rewarded video at
    /// the moment somebody has been stopped from playing is the shape a store reviewer is right
    /// to object to. The shop sits below the video and above the way out that costs nothing.
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
        /// <paramref name="watching"/> is whether a rewarded video is worth offering. The shop
        /// is not conditional on one: hearts are sold there for <em>gems</em>, which a player
        /// may already hold and which need no store connection at all, so the button works in a
        /// build with no IAP and on a plane.
        /// </summary>
        public static HeartGateStack Of(bool watching)
        {
            float y = StackTop;
            float watch = HeartGateStack.Absent;

            if (watching)
            {
                watch = y + ActionHeight * .5f;
                y += ActionHeight + Gap;
            }

            float shop = y + ActionHeight * .5f;
            y += ActionHeight + Gap;

            float ok = y + OkHeight * .5f;
            y += OkHeight + FootRoom;

            return new HeartGateStack(watch, shop, ok, y);
        }

        /// <summary>
        /// The tallest this panel ever gets. Derived by asking rather than reasoned about, so a
        /// row added above cannot leave a stale number behind it.
        /// </summary>
        public static float Tallest
        {
            get
            {
                float with = Of(true).Height, without = Of(false).Height;
                return with > without ? with : without;
            }
        }
    }
}
