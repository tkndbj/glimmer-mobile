using GlimmerGrove.Content;
using UnityEngine;

namespace GlimmerGrove.Layout
{
    /// <summary>
    /// How wide the canvas is, in its own reference units, on the display it is drawn on.
    ///
    /// <para>
    /// <b>The canvas is width-matched, so the aspect decides the height and nothing else
    /// does.</b> <c>Boot.BuildCanvas</c> pins the reference <em>width</em> and lets the height
    /// fall out of the display, which is exactly right for a portrait game: a control is sized
    /// against the width of the thing it is drawn on, and every phone this game has ever run on
    /// is 1080 units across. What varies is how many units of <em>height</em> that buys — 2400
    /// on a 20:9 phone, 2340 on a 19.5:9, 1920 on a 16:9, and <b>1440 on a 4:3 tablet</b>.
    /// </para>
    /// <para>
    /// <b>Every screen here is a vertical stack of fixed-height chrome, so that number is the
    /// whole layout.</b> The hub spends 1338 units on chrome and hangs a 500-unit companion in
    /// what is left; a well gives up 796 units to a header, a floor and a tray before its board
    /// is measured; the tallest explanatory panel is 1228 plus its ribbon. None of it is wrong,
    /// and all of it was chosen against a phone. Handed 1440 units instead of 2340, the same
    /// arithmetic draws the hub's companion through the streak box, a modal filling 88% of the
    /// display, and a 6x10 well at a cell of 62 units against a phone's 151. Reported from an
    /// iPad as everything being too big and overlapping, which is exactly what it is — the
    /// chrome did not grow, the room shrank.
    /// </para>
    /// <para>
    /// <b>So the canvas is widened until it is as tall as the layouts need.</b> A short display
    /// is given <see cref="ShortHeight"/> units of height and the width follows from its aspect.
    /// Nothing moves relative to anything else — every size, offset and margin in the game is
    /// untouched, in units — and the whole interface is simply drawn smaller against a screen
    /// that is physically much larger, which is what a tablet wants anyway. It is one rule in
    /// one place rather than a tablet variant of every constant in Presentation, and it is the
    /// only shape that could be: a second hand-tuned layout is a second thing to keep in step,
    /// and this project already records what happens to a margin chosen by eye.
    /// </para>
    /// <para>
    /// <b>Phones are left exactly as they are, and that is a decision rather than a
    /// consequence.</b> The obvious implementation is <c>CanvasScaler.ScreenMatchMode.Expand</c>
    /// against a reference height, and it would quietly shrink a 16:9 phone as well — the
    /// iPhone SE is 1.778 and would lose a tenth of its scale for a problem it does not have.
    /// So the rule switches at <see cref="PhoneFloor"/> instead, which is a threshold no
    /// shipping device sits near: every phone is 16:9 (1.778) or taller, every tablet 16:10
    /// (1.6) or squarer.
    /// </para>
    /// <para>
    /// <b>Here rather than beside the canvas, for <c>ChapterMap</c>'s reason</b> (invariant 8a),
    /// which <c>ReadoutRow</c>, <c>FallBand</c> and <c>PanelStack</c> have already earned:
    /// whether two things on a screen overlap is arithmetic, and arithmetic inside a
    /// <c>MonoBehaviour</c> is arithmetic nothing can check. It is also what
    /// <c>PanelStack.TightestCanvas</c> now reads, since that constant would otherwise go on
    /// claiming a canvas height no display produces any more.
    /// </para>
    /// </summary>
    public static class CanvasFit
    {
        /// <summary>
        /// The reference width a phone is drawn at, which is the width everything in this game
        /// was laid out against.
        ///
        /// Taken from <see cref="ChapterMap.Width"/> for <c>Boot.RefWidth</c>'s reason — the map
        /// validator measures glade collisions against that number, so the two have to be the
        /// same one.
        /// </summary>
        public const float PhoneWidth = ChapterMap.Width;

        /// <summary>
        /// The aspect (height over width, in portrait) at or above which a display is left
        /// alone.
        ///
        /// <para>
        /// 7:4, and where it sits matters more than the value: there is a real gap in the
        /// devices this game runs on between the squarest phone (16:9, 1.778 — the iPhone SE)
        /// and the tallest tablet (16:10, 1.6), and this is inside it. So the rule is a
        /// threshold rather than a ramp, and the step across it — about 14% — is one no shipping
        /// display can be on both sides of.
        /// </para>
        /// </summary>
        public const float PhoneFloor = 1.75f;

        /// <summary>
        /// The canvas height, in reference units, a display squarer than <see cref="PhoneFloor"/>
        /// is given.
        ///
        /// <para>
        /// <b>2160 is a tablet being handed an 18:9 phone's canvas</b>, and it is measured rather
        /// than round. The deepest layout in the game is the hub, whose chrome runs 856 units
        /// down from the top and whose companion is centre-anchored 130 below the middle and
        /// reaches 261 above its own centre: it needs <c>2 x (856 + 131)</c> = 1974 units before
        /// the companion touches the feature row, and <c>2 x (206 + 691)</c> = 1794 before it
        /// touches the play key. Everything else in the game either scrolls or asks for less.
        /// This leaves 186 units of air over the binding one, which is about what a 19.5:9 phone
        /// has.
        /// </para>
        /// <para>
        /// It is deliberately not larger. Every unit of height bought here is bought by drawing
        /// the whole interface smaller, so the number wants to be the least that clears the
        /// layouts rather than the most a tablet would tolerate.
        /// </para>
        /// </summary>
        public const float ShortHeight = 2160f;

        /// <summary>
        /// The shortest canvas any display can now produce, in reference units.
        ///
        /// <para>
        /// Derived, and that is the point of it. A phone is drawn at <see cref="PhoneWidth"/>
        /// and is never squarer than <see cref="PhoneFloor"/>, so it can never offer less than
        /// the two multiplied; anything squarer is given <see cref="ShortHeight"/> outright,
        /// which is more. A bound rather than a device — the squarest real phone is 16:9 and
        /// offers 1920 — because a fit check wants the number nothing can go under, not the
        /// number some catalogue of handsets happens to stop at.
        /// </para>
        /// </summary>
        public const float ShortestCanvas = PhoneWidth * PhoneFloor;

        /// <summary>
        /// Whether a display is squarer than a phone, and so one the canvas widens for.
        ///
        /// <para>
        /// A degenerate reading answers "no". A zero-sized screen happens briefly during a
        /// resize and on some Android devices on the first frame after a rotation, and the
        /// honest response to one is the layout the game already had rather than a canvas
        /// derived from a divide by nothing. <c>SafeArea</c> treats its own degenerate readings
        /// the same way and for the same reason.
        /// </para>
        /// </summary>
        public static bool IsShort(float screenWidth, float screenHeight)
            => screenWidth > 0f && screenHeight > 0f && screenHeight / screenWidth < PhoneFloor;

        /// <summary>
        /// The canvas's reference width for a display of this size, in reference units.
        ///
        /// <para>
        /// A pure function of the screen, which is what lets it be asked in the frame the canvas
        /// is created in — <c>SplashScreen.Fit</c> needs an answer before <c>CanvasScaler</c>
        /// has run for the first time, and the whole reason that method measures nothing is that
        /// the rect it would measure is a frame behind.
        /// </para>
        /// </summary>
        public static float WidthFor(float screenWidth, float screenHeight)
        {
            if (!IsShort(screenWidth, screenHeight)) return PhoneWidth;

            // Wide enough that the height this aspect buys is ShortHeight. The Max is a backstop
            // rather than a case anybody reaches: at the floor the quotient is already 1234, so
            // it only guards a ShortHeight retuned below PhoneWidth's own aspect — which would
            // otherwise make a tablet's canvas narrower than a phone's and shrink nothing.
            return Mathf.Max(PhoneWidth, ShortHeight * screenWidth / screenHeight);
        }

        /// <summary>The canvas's height in reference units, which is the width times the aspect.</summary>
        public static float HeightFor(float screenWidth, float screenHeight)
        {
            if (screenWidth <= 0f || screenHeight <= 0f) return ShortestCanvas;
            return WidthFor(screenWidth, screenHeight) * screenHeight / screenWidth;
        }

        /// <summary>
        /// How much smaller the interface is drawn on this display than on a phone: 1 on every
        /// phone, and about .67 on a 4:3 tablet.
        ///
        /// Nothing is scaled <em>by</em> it — the shrink is the canvas being wider, and every
        /// control keeps its size in units. It is here so a screen that wants to say how much
        /// room it has relative to a phone can ask rather than divide.
        /// </summary>
        public static float ScaleFor(float screenWidth, float screenHeight)
            => PhoneWidth / WidthFor(screenWidth, screenHeight);
    }
}
