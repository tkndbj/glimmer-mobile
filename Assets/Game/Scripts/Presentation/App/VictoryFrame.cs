using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The victory panel's furniture — the fit layer, the fan and bloom behind, the green
    /// window, the light pooling under its top edge, and the crest of a crown over a banner
    /// carrying one word — built once here for every panel that wears it.
    ///
    /// <para>
    /// <b>Two screens made of the same furniture get one builder</b> (invariant 44d's shape,
    /// said of the code rather than the mirror). <c>WinOverlay</c> drew this for a year and
    /// the challenge deal sheet was asked to wear the same design on 2026-09-23; copying
    /// forty lines would have left two crowns to drift apart the next time the banner's face
    /// was re-measured (<see cref="WordLift"/>). Everything measured off the art lives here,
    /// and a caller gets handles to the three pieces its own sequence animates.
    /// </para>
    /// <para>
    /// <b>Everything animated is built at scale nought and left there.</b> The panel, the crown,
    /// the banner and the word are each a beat in the caller's own <c>Cue</c> — the victory
    /// panel lands its word after the stars, the deal sheet after the banner — so the frame
    /// decides nothing about timing. A panel rebuilt in place (<c>ModalView.Rebuild</c>) sets
    /// them to one with <see cref="Settle"/> rather than replaying an entrance.
    /// </para>
    /// <para>
    /// <b>It never runs off the screen.</b> The canvas is width-matched at 1080, so its height
    /// is whatever the device's aspect makes it — 1920 on a 16:9 phone, 2400 on a tall one and
    /// 1440 on a 4:3 tablet. A panel whose height depends on what it says cannot be laid out
    /// against a fixed screen, so the whole block is fitted; see <see cref="Fit"/>.
    /// </para>
    /// </summary>
    public sealed class VictoryFrame
    {
        // ------------------------------------------------------------- geometry
        /// <summary>
        /// The panel, and how far the crown reaches above its top edge.
        ///
        /// <para>
        /// The crest deliberately breaks the frame — a crown over a banner sitting <em>on</em>
        /// the panel rather than inside it is what stops a tall rectangle reading as a
        /// rectangle. <see cref="CrestReach"/> is what the fit has to allow for, so it is a
        /// constant rather than something measured: the art is fixed and a measured version
        /// would only be a slower way of writing 202 down. It has to move whenever
        /// <see cref="CrownY"/> does, or the fit stops reserving the room the crown needs.
        /// </para>
        /// </summary>
        public const float PanelWidth = 900f, CrestReach = 202f;

        /// <summary>
        /// The frame's own tint.
        ///
        /// The window art is a mid teal-green; driven down to about 60% it becomes the deep
        /// forest the gold and the cream sing against. Left at white the panel is brighter
        /// than the stars on it, which is the wrong way round for the loudest screen in the
        /// game.
        /// </summary>
        public static readonly Color PanelInk = new Color(.588f, .722f, .690f, 1f);

        /// <summary>
        /// Where the crest's two pieces sit, measured from the panel's top edge.
        ///
        /// The crown is lifted until its base rests on the banner's top edge rather than
        /// sinking into it — at 88 it sat inside the ribbon and read as one lumpy shape.
        /// Raising it costs <see cref="CrestReach"/> the same 26px; the two move together.
        /// </summary>
        public const float CrownY = 114f, BannerY = -30f;

        /// <summary>How large the banner is drawn, at the art's own aspect.</summary>
        public static readonly Vector2 BannerSize = new Vector2(566f, 157f);

        /// <summary>
        /// Where the word sits on the banner, and how much of it it may use.
        ///
        /// <para>
        /// <b>The banner's flat face is not the banner's centre</b>, and centring the word on
        /// the sprite is what had it hanging off the bottom edge onto the draped tails. Measured
        /// from the art: in the 361&#215;100 source the face runs from y&#8239;2 to y&#8239;54, so
        /// its middle is 22px above the sprite's middle, and the sprite is drawn at
        /// 566/361&#8239;=&#8239;1.568&#215;. That is where 34 comes from.
        /// </para>
        /// <para>
        /// The width is measured the same way. At the face's own middle the red runs 231 source
        /// pixels wide — 362 drawn — so a box of 430 let a long translation run out over the
        /// folds. <see cref="UIKit.Shrinkable"/> then keeps it inside 356 rather than letting it
        /// spill, which for a word this short only ever affects a translation.
        /// </para>
        /// </summary>
        public const float WordLift = 34f;
        public static readonly Vector2 WordBox = new Vector2(356f, 74f);

        /// <summary>Air kept between the block and the top and bottom of the screen.</summary>
        public const float FitMargin = 20f;

        // ------------------------------------------------------------- handles
        /// <summary>The scaled layer everything sits in; a caller's own furniture goes here too.</summary>
        public RectTransform Fit { get; private set; }

        /// <summary>The window, and its transform — what <c>ModalView</c> calls <c>Backing</c> and <c>Panel</c>.</summary>
        public Image Backing { get; private set; }
        public RectTransform Panel { get; private set; }

        /// <summary>The crest's three pieces, each at scale nought until the caller's beat.</summary>
        public Image Crown { get; private set; }
        public Image Banner { get; private set; }
        public Text Word { get; private set; }

        /// <summary>
        /// Builds the frame under <paramref name="content"/>, sized <paramref name="panelH"/>
        /// tall, with <paramref name="word"/> on the banner. <paramref name="width"/> is the
        /// window's, <see cref="PanelWidth"/> unless a caller asks — the deal sheet stands
        /// wider than the victory panel because its rows carry a stone, a sentence and a price
        /// side by side, and the crest is the same size on either.
        /// </summary>
        public static VictoryFrame Build(Transform content, float panelH, string word, float width = PanelWidth)
        {
            var frame = new VictoryFrame { Fit = MakeFit(content, panelH) };

            // ------------------------------------------------------ the light show
            // On the fit rather than on the content, so a panel scaled down on a short screen
            // takes its own halo with it instead of sitting in a fan sized for a taller one.
            // Built before the panel, which is what puts it behind.
            float crestY = panelH * .5f - CrestReach * .5f;

            var fan = UIKit.Img("Rays", frame.Fit, Art.Rays(256, 14), new Color(1f, .80f, .30f, .20f),
                                Vector2.one * 1680f, new Vector2(.5f, .5f), new Vector2(0f, crestY - 240f));
            Tween.Run(46f, Ease.Linear,
                      t => { if (fan) fan.transform.localRotation = Quaternion.Euler(0f, 0f, t * 360f); },
                      fan.gameObject, "spin").Loop(-1, false);

            UIKit.Img("Bloom", frame.Fit, Art.Glow(128, 2.4f), new Color(1f, .82f, .38f, .22f),
                      new Vector2(1240f, 1000f), new Vector2(.5f, .5f), new Vector2(0f, crestY - 200f));

            // ---------------------------------------------------------- the frame
            // Nine-sliced, which was a real defect rather than a refinement: the window sprite
            // had no border, so an 880x1330 panel stretched a 720x642 image to twice its aspect
            // and smeared its corners and its inner hairline. It also carried a header tab
            // nothing ever drew. Both are fixed in the art.
            frame.Backing = UIKit.Img("Panel", frame.Fit, Art.S("Ui/Win/window"), PanelInk,
                                      new Vector2(width, panelH), new Vector2(.5f, .5f),
                                      new Vector2(0f, -CrestReach * .5f));
            frame.Panel = (RectTransform)frame.Backing.transform;
            frame.Panel.localScale = Vector3.zero;

            // Light pooling under the crest, inside the frame. A soft gradient rather than a
            // plate, for the reason the feature beacon's seat is one: it reads as light around
            // an award and can never be mistaken for a mislaid rectangle.
            UIKit.Img("Pool", frame.Panel, Art.Glow(128, 1.9f), new Color(1f, .96f, .82f, .11f),
                      new Vector2(width - 60f, 700f), new Vector2(.5f, 1f), new Vector2(0f, -70f));

            // ---------------------------------------------------------- the crest
            // The banner is deliberately blank artwork with the word drawn on it as text. The
            // pack ships a matching "VICTORY" graphic and it is the one piece not imported: a
            // word painted into a texture cannot be translated, and invariant 6 says every
            // player-facing string is a loc key. Blank ribbon plus a key is the same picture
            // and ships in every language.
            //
            // Two herald's horns flanked this and were cut. They read as a fanfare in a still
            // frame and as clutter on the device: the crest is the one thing on the panel that
            // has to be legible in a quarter of a second, and three gold shapes at three angles
            // is not that. The art is out of the project with them, because an addressed sprite
            // nothing draws is still built into the bundle and preloaded at every launch.
            frame.Crown = UIKit.Img("Crown", frame.Panel, Art.S("Ui/Win/crown"), Color.white,
                                    new Vector2(180f, 162f), new Vector2(.5f, 1f), new Vector2(0f, CrownY));
            frame.Crown.preserveAspect = true;
            frame.Crown.transform.localScale = Vector3.zero;

            frame.Banner = UIKit.Img("Banner", frame.Panel, Art.S("Ui/Win/banner"), Color.white,
                                     BannerSize, new Vector2(.5f, 1f), new Vector2(0f, BannerY));
            frame.Banner.preserveAspect = true;
            frame.Banner.transform.localScale = Vector3.zero;

            // Lifted onto the ribbon's flat face rather than centred on the sprite — see WordLift.
            frame.Word = UIKit.Titled("Word", frame.Banner.transform, word, 58,
                                      Pal.Cream, TextAnchor.MiddleCenter, WordBox,
                                      new Vector2(.5f, .5f), new Vector2(0f, WordLift), 5f, 5f);
            UIKit.Shrinkable(frame.Word, 32);
            frame.Word.transform.localScale = Vector3.zero;

            return frame;
        }

        /// <summary>
        /// The frame as it stands once every entrance beat has landed, for a panel rebuilt in
        /// place: a rebuild is the same panel in a new state, not a new panel, and replaying
        /// the crown would pop at a player who tapped a button on the panel already in front
        /// of them.
        /// </summary>
        public void Settle()
        {
            Panel.localScale = Vector3.one;
            Crown.transform.localScale = Vector3.one;
            Banner.transform.localScale = Vector3.one;
            Word.transform.localScale = Vector3.one;
        }

        /// <summary>
        /// A layer between the scrim and the panel, scaled so the whole block — crest included
        /// — fits the screen it landed on.
        ///
        /// <para>
        /// <b>Why a layer rather than a scale on the panel itself.</b> The panel is what
        /// <c>ModalView.Close</c> scales out, and it does so to an absolute value; a panel
        /// resting at 0.94 would visibly <em>grow</em> on the way out. Everything that animates
        /// a child — <see cref="Tween.Pop"/>, <see cref="Tween.Punch"/> — writes absolute local
        /// scales too. Keeping the fit on a parent means every one of those numbers stays what
        /// it was written as.
        /// </para>
        /// <para>
        /// The panel is offset upward by half the crest inside this layer, so what is centred
        /// on the screen is the block the player sees rather than the frame's own rectangle.
        /// </para>
        /// </summary>
        static RectTransform MakeFit(Transform content, float panelH)
        {
            var host = UIKit.Node("Fit", content);

            float reach = panelH + CrestReach;
            float room = Flow.Size.y - FitMargin * 2f;
            if (reach > room && reach > 1f) host.localScale = Vector3.one * (room / reach);

            return host;
        }
    }
}
