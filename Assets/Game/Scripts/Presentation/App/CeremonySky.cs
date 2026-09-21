using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The room every ceremony arrives into: a lit, saturated wash instead of a near-black one.
    ///
    /// <para>
    /// <b>It exists because three screens were the same room and each one built its own.</b>
    /// <c>RankUpOverlay</c>, <c>WardRevealOverlay</c> and <c>WardUpgradeRevealOverlay</c> all
    /// stood their subject on a vertical gradient of the subject's own hue driven down to about
    /// a tenth of its value, under a vignette of near-black at seven tenths — so all three came
    /// back from the owner as <em>so dark</em>, all three would have had to be fixed separately,
    /// and they would have drifted apart the first time one of them was touched. That is
    /// invariant 5b's rule about a thing existing exactly once, and invariant 44d's about two
    /// screens made of the same furniture getting one mirror.
    /// </para>
    /// <para>
    /// <b>The colours are measured off a reference the owner supplied, not picked</b> (invariant
    /// 44b). The wash in that picture is a four-corner field — warm peach in one corner, orchid
    /// and magenta across the middle, deep blue in the opposite one — and the figures below are
    /// a robust bilinear fit to it with the badges standing on it masked out, which is the only
    /// way to measure a background through its own foreground. The fit explains the wash to a
    /// mean error of 23/255 and what it rejected is exactly the badges;
    /// <c>Tools/render_rank_ceremony.py --sky</c> puts it beside the reference so that can be
    /// looked at rather than believed.
    /// </para>
    /// <para>
    /// <b>What is deliberately <em>not</em> shared is the light on top of it.</b> The fans, the
    /// halo, the rim and the aurora still wear the colour of the thing being revealed — the seat
    /// a turret was bought for, the metal of the rung — because that is the one fact each of
    /// those screens exists to carry, and <c>WardRevealOverlay</c>'s own remarks argue it at
    /// length. The room is shared and the lighting is not: what changed is the ground the
    /// subject stands on, never what the subject is lit by.
    /// </para>
    /// <para>
    /// <b>It costs no art and no address.</b> The wash is <see cref="Art.Corners"/> — generated,
    /// cached by key, 64 pixels square and stretched by the hardware — so nothing here is
    /// imported, addressed, bundled, or capable of arriving as a white rectangle (invariant 7b).
    /// </para>
    /// <para>
    /// <b>Two calls rather than one, and the gap between them is the point.</b> Each of these
    /// screens builds drifting masses of light and a scatter of fireflies between its wash and
    /// its vignette, so that the vignette holds them in too. A single builder would have had to
    /// either take those over — three screens' worth of composition that genuinely differs — or
    /// put the vignette underneath them, which is a different picture.
    /// </para>
    /// </summary>
    public static class CeremonySky
    {
        // ------------------------------------------------------------------ the measurement
        /// <summary>
        /// The four corners of the wash, as fitted. Down the screen, left to right.
        ///
        /// <para>
        /// The deep blue is the one worth reading twice: it is what stops a bright room being a
        /// flat one, and it is the value every cream caption and every lit rim on these screens
        /// is read against. Take it up and they lose their contrast; take it down and they are
        /// back where they started.
        /// </para>
        /// </summary>
        public static readonly Color TopLeft = Pal.Hex("#F7B2A5");
        public static readonly Color TopRight = Pal.Hex("#AF3ED3");
        public static readonly Color BottomLeft = Pal.Hex("#DD3EAF");
        public static readonly Color BottomRight = Pal.Hex("#041F9E");

        /// <summary>
        /// The warm corner, which the wash alone cannot say.
        ///
        /// <para>
        /// A four-corner field is bilinear and the reference's warm corner is not: it is a tight
        /// glow that falls off well before the middle, so fitting it into a corner colour spreads
        /// it along the whole top edge and the picture loses the one thing that makes it read as
        /// <em>lit</em> rather than as printed. It is put back as what it actually is.
        /// </para>
        /// </summary>
        public static readonly Color Warm = Pal.Hex("#FFD79C");

        /// <summary>How large that glow is drawn, and how strongly.</summary>
        const float WarmSize = 1500f, WarmAlpha = .46f;

        /// <summary>Where it sits — the top-left corner, off the edge, as in the reference.</summary>
        static readonly Vector2 WarmHome = new Vector2(-470f, 880f);

        /// <summary>
        /// The vignette these screens wear now: a quarter, in the wash's own deep blue.
        ///
        /// <para>
        /// <b>Both halves of that are the fix.</b> It was about seven tenths, which is most of
        /// why the rooms read as black; and it was tinted toward <em>black</em>, which on a
        /// coloured screen makes the corners the one grey thing in the picture — the mistake all
        /// three screens had already written a comment against and then made anyway. A vignette
        /// is for holding the eye in the middle, and at a quarter it still does that.
        /// </para>
        /// </summary>
        public static Color VignetteInk => Color.Lerp(BottomRight, Color.black, .35f);

        public const float VignetteAlpha = .26f;

        /// <summary>
        /// The dark the room is read <em>against</em>, for anything that used to be read against
        /// black.
        ///
        /// <para>
        /// <b>A bright ground inverts which way contrast runs, and that is the half of this
        /// change that is not a colour.</b> Every one of these screens had furniture drawn as
        /// white at a low alpha — an empty pip, the trough under a rail, a faint rule — because
        /// that is what shows on a near-black room. On this one it is invisible: the rank
        /// ceremony's rail lost its trough and every unheld pip on it, and its eyebrow, drawn in
        /// the rung's own metal, went from gold-on-black to gold-on-peach. All of it is drawn in
        /// this instead, which reads on the light corner and on the deep one alike, because the
        /// wash never approaches it anywhere.
        /// </para>
        /// <para>
        /// Derived from the wash's own deep corner rather than being a neutral grey, for the
        /// reason <see cref="VignetteInk"/> is: a grey on a coloured screen is the one thing in
        /// the picture that does not belong to it.
        /// </para>
        /// </summary>
        public static Color Ink => Color.Lerp(BottomRight, Color.black, .58f);

        // ------------------------------------------------------------------ the building
        /// <summary>The wash itself. One sprite, cached, carrying its own colour.</summary>
        public static Sprite Wash() => Art.Corners(TopLeft, TopRight, BottomLeft, BottomRight);

        /// <summary>
        /// The ground: the wash, and the warm corner falling on it. Built at alpha nought, for
        /// the caller to fade up on its opening beat.
        /// </summary>
        /// <param name="onTapped">
        /// What a tap on the room does — the skip, on all three. Null makes the layer
        /// transparent to touches instead.
        /// </param>
        public static Image Ground(RectTransform content, System.Action onTapped,
                                   bool silent = false)
        {
            var sky = UIKit.Img("Sky", content, Wash(), new Color(1f, 1f, 1f, 0f));
            UIKit.StretchTo((RectTransform)sky.transform, 0, 0, 0, 0);

            sky.raycastTarget = onTapped != null;
            if (onTapped != null) sky.gameObject.AddComponent<Btn>().Setup(onTapped, silent);

            // Above the wash and below everything the caller adds next, so the corner reads as
            // light falling on the room rather than as a lamp hung in front of the subject.
            var warm = UIKit.Img("Warm", content, Art.Glow(256, 1.9f), Pal.A(Warm, WarmAlpha),
                                 Vector2.one * WarmSize, new Vector2(.5f, .5f), WarmHome);
            warm.raycastTarget = false;

            return sky;
        }

        /// <summary>
        /// The vignette, built last so it holds in whatever the caller put on the ground. Also
        /// at alpha nought; fade it to <see cref="VignetteAlpha"/>.
        /// </summary>
        public static Image Veil(RectTransform content)
        {
            var vignette = UIKit.Img("Vignette", content, Art.Vignette(256), Pal.A(VignetteInk, 0f));
            UIKit.StretchTo((RectTransform)vignette.transform, 0, 0, 0, 0);
            vignette.raycastTarget = false;

            return vignette;
        }
    }
}
