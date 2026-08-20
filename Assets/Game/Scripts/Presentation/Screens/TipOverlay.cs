using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Teaches one mechanic, once, by pointing at it on the real board.
    ///
    /// <para>
    /// The board stays where it is and a hole is cut around the tile in question, rather
    /// than the lesson being illustrated somewhere else. A diagram has to be translated
    /// back onto the board by the player; a border around the actual tile it is talking
    /// about does not.
    /// </para>
    /// <para>
    /// It never dismisses itself. A tip that fades is a tip the player was still reading,
    /// and this one is only ever shown once in their whole life with the game — there is
    /// no second chance to catch it. The OK button is the only way out, and the board
    /// underneath stays locked until it is pressed.
    /// </para>
    /// <para>
    /// The hole is four dark quads rather than a masked cutout. That needs no shader, no
    /// render texture and no extra material, and it behaves identically on every device
    /// — which for one overlay is worth more than elegance.
    /// </para>
    /// </summary>
    public sealed class TipOverlay : ModalView
    {
        /// <summary>
        /// What is being taught. Its strings come from its id.
        ///
        /// A property rather than a field because it is handed over in code when the
        /// overlay is opened and never set in the inspector — as a public field Unity
        /// tries to serialise it, finds a struct it cannot, and warns on every compile.
        /// </summary>
        public Mechanic Mechanic { get; set; }

        /// <summary>The tile to ring, in this canvas's space. Null to teach without pointing.</summary>
        public RectTransform Target;

        /// <summary>Raised once the player has dismissed it, so the board can unlock.</summary>
        public System.Action Dismissed;

        const float Dim = .78f;
        const float Pad = 18f;

        protected override void Build()
        {
            var spot = SpotlightRect();

            if (spot.HasValue) BuildCutout(spot.Value);
            else UIKit.Scrim(Content, Dim, null);

            BuildBubble(spot);
        }

        /// <summary>
        /// The target's rectangle in this overlay's space, or null when there is nothing
        /// on the board to point at — a move budget lives in the HUD, not in a cell.
        /// </summary>
        Rect? SpotlightRect()
        {
            if (!Target) return null;

            var corners = new Vector3[4];
            Target.GetWorldCorners(corners);

            var min = (Vector2)Content.InverseTransformPoint(corners[0]);
            var max = (Vector2)Content.InverseTransformPoint(corners[2]);

            return new Rect(min.x - Pad, min.y - Pad,
                            max.x - min.x + Pad * 2f, max.y - min.y + Pad * 2f);
        }

        /// <summary>Four quads around the hole, so the tile beneath stays fully visible.</summary>
        void BuildCutout(Rect hole)
        {
            var shade = new Color(.02f, .04f, .06f, Dim);

            // Sized generously past the screen so no seam shows on any aspect ratio.
            const float Far = 4000f;

            Quad("Above", shade, new Vector2(-Far, hole.yMax), new Vector2(Far, Far));
            Quad("Below", shade, new Vector2(-Far, -Far), new Vector2(Far, hole.yMin));
            Quad("Left", shade, new Vector2(-Far, hole.yMin), new Vector2(hole.xMin, hole.yMax));
            Quad("Right", shade, new Vector2(hole.xMax, hole.yMin), new Vector2(Far, hole.yMax));

            // The hole itself still eats taps: the board is locked, and a player poking
            // the highlighted tile before reading is exactly who this is for.
            var catcher = Quad("Catcher", new Color(0, 0, 0, 0),
                               new Vector2(hole.xMin, hole.yMin), new Vector2(hole.xMax, hole.yMax));
            catcher.raycastTarget = true;

            // A border traced around the thing itself, not a halo floating over it.
            // RoundOutline is a sliced sprite, so it takes the target's proportions
            // instead of forcing everything into the same oval — a wide HUD pill and a
            // square tile each get an outline that actually fits them.
            var border = UIKit.Img("Border", Content, Art.RoundOutline(26, 5f),
                                   Pal.A(Pal.Gold, .95f),
                                   new Vector2(hole.width, hole.height),
                                   new Vector2(.5f, .5f), hole.center);
            border.raycastTarget = false;

            border.transform.localScale = Vector3.one * 1.10f;
            Tween.Scale(border.transform, 1f, .38f, Ease.OutBack);

            // Breathes on alpha alone. Scaling the outline would make it drift off the
            // edge it is supposed to be tracing.
            Tween.Run(1.4f, Ease.InOutSine, t =>
            {
                if (border) border.color = Pal.A(Pal.Gold, Mathf.Lerp(.55f, 1f, t));
            }, border, "tip").Loop(-1, true);
        }

        Image Quad(string name, Color colour, Vector2 min, Vector2 max)
        {
            var img = UIKit.Img(name, Content, Art.Pixel, colour,
                                new Vector2(max.x - min.x, max.y - min.y),
                                new Vector2(.5f, .5f), (min + max) * .5f);
            img.raycastTarget = true;
            return img;
        }

        /// <summary>
        /// The speech bubble. Placed below the ring when there is room and above it when
        /// there is not, so a tip about a tile near the bottom of the board is not drawn
        /// off the screen.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Its height is measured, never declared.</b> It used to be a constant, with the
        /// body given a fixed box and <c>resizeTextForBestFit</c> between 22 and 32 to keep
        /// it inside — and it did not shrink. At 22 the crossing's 231 characters fit that
        /// box with room to spare, so best fit plainly was not testing the height: a wrapped
        /// label never fails the width test, and <see cref="UIKit.Label"/> sets
        /// <c>verticalOverflow = Overflow</c>, which <c>Text.GetGenerationSettings</c> hands
        /// to the generator as "the height is not a constraint". <c>Fit</c> was therefore a
        /// no-op on the body: every tip drew at its full size and ran past the bottom of its
        /// box. That stayed invisible for six mechanics because they are 99–149 characters
        /// and fitted anyway; the crossing printed straight through the OK button, and a long
        /// translation would have done the same to any of them.
        /// </para>
        /// <para>
        /// Measuring is the bargain the win panel's route bubble already makes, for the same
        /// reason — sizing the paper first means guessing at a wrapped translation. The body
        /// is built before the bubble exists so its height can be read, then reparented into
        /// it; nothing is drawn in between, because this is all one pass of <see cref="Build"/>.
        /// Every row is then placed against a cursor, so the gap above the button is a gap
        /// whatever the string turns out to be.
        /// </para>
        /// </remarks>
        void BuildBubble(Rect? spot)
        {
            const float Width = 780f;

            // The stack, top to bottom: title, gap, body, gap, button, bottom margin.
            // Everything but the body is a fixed pitch; the body is whatever it measures.
            const float TitleTop = 28f, TitleHeight = 62f;
            const float Gap = 18f;
            const float BodyGap = 34f;
            const float ButtonHeight = 124f, ButtonBottom = 40f;

            const int BodyMin = 22, BodySize = 32;
            const float BodyWidth = Width - 120f;

            const float Chrome = TitleTop + TitleHeight + Gap + BodyGap + ButtonHeight + ButtonBottom;

            // Near-black on white, not the warm brown the wooden panels use — on a plain
            // white bubble that brown reads as washed out rather than as ink.
            var body = UIKit.Titled("Body", Content, Loc.Get(Mechanic.BodyKey), BodySize,
                                    new Color(.29f, .33f, .38f), TextAnchor.UpperCenter,
                                    new Vector2(BodyWidth, 10f), new Vector2(.5f, 1f), Vector2.zero,
                                    outline: 0f, shadow: 0f, wrap: true);

            float bodyHeight = FitBody(body, Mathf.Max(140f, MaxHeight() - Chrome), BodyMin);
            float height = Chrome + bodyHeight;

            float y = 0f;
            bool above = false;

            if (spot.HasValue)
            {
                var hole = spot.Value;
                float below = hole.yMin - height * .5f - 60f;
                float over = hole.yMax + height * .5f + 60f;

                // Content is centred, so half the canvas height is the edge.
                float limit = Content.rect.height * .5f - height * .5f - 40f;

                above = below < -limit;
                y = Mathf.Clamp(above ? over : below, -limit, limit);
            }

            // A plain white bubble rather than the carved wooden panel the other
            // overlays use. Those are furniture the player is meant to look at; this is
            // a note about something else on screen, and it should get out of the way.
            var panel = UIKit.Img("Bubble", Content, Art.Round(28), Color.white,
                                  new Vector2(Width, height), new Vector2(.5f, .5f), new Vector2(0f, y));
            var rt = (RectTransform)panel.transform;

            var edge = UIKit.Img("Edge", rt, Art.RoundOutline(28, 3f), new Color(.10f, .13f, .17f, .16f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            // This overlay builds its own bubble instead of calling MakePanel, because
            // the cutout has to be drawn under it. ModalView.Close still animates
            // Panel on the way out, so it has to be told which transform that is —
            // without this the OK button throws and the board never unlocks.
            Backing = panel;
            Panel = rt;

            if (spot.HasValue)
            {
                // A little pointer, so the bubble reads as belonging to the ring.
                var beak = UIKit.Img("Beak", rt, Art.Crystal(48), Color.white,
                                     new Vector2(46f, 46f), new Vector2(.5f, above ? 0f : 1f),
                                     new Vector2(Mathf.Clamp(spot.Value.center.x, -Width * .35f, Width * .35f),
                                                 above ? 14f : -14f));
                beak.raycastTarget = false;
            }

            // Stacked from the top edge downwards. UIKit.Box pivots every box at its centre
            // whatever the anchor, so a position is the middle of the box and not its top —
            // getting that backwards is what had the body starting level with the title and
            // printing straight through it. The cursor therefore always names an *edge*, and
            // half a row's height is added where it is turned into a position.
            float cursor = TitleTop;

            var title = UIKit.Titled("Title", rt, Loc.Get(Mechanic.TitleKey), 46,
                                     new Color(.13f, .16f, .20f), TextAnchor.UpperCenter,
                                     new Vector2(Width - 100f, TitleHeight), new Vector2(.5f, 1f),
                                     new Vector2(0f, -(cursor + TitleHeight * .5f)),
                                     outline: 0f, shadow: 0f);

            // Shrunk to fit rather than trusted to be short enough. Every one of these is
            // translated, and German or Turkish will run half as long again — a tip that
            // overflows its bubble in one market and not another is the kind of bug nobody
            // sees until a review mentions it. Measured rather than left to best fit for
            // the reason in the remarks above: this is the axis best fit is *supposed* to
            // handle, and the body proved it cannot be relied on to handle the other one,
            // so there is no reason to keep two mechanisms where one is known to work.
            Squeeze(title, Width - 100f, 30);

            cursor += TitleHeight + Gap;

            var brt = body.rectTransform;
            brt.SetParent(rt, false);
            brt.anchorMin = brt.anchorMax = new Vector2(.5f, 1f);
            brt.sizeDelta = new Vector2(BodyWidth, bodyHeight);
            brt.anchoredPosition = new Vector2(0f, -(cursor + bodyHeight * .5f));

            UIKit.TextButton("Ok", rt, "btn_green", Loc.Get("ui.common.got_it"), 46,
                             new Vector2(420f, ButtonHeight), new Vector2(.5f, 0f),
                             new Vector2(0f, ButtonBottom + ButtonHeight * .5f), Accept);

            rt.localScale = Vector3.zero;
            Tween.Scale(rt, 1f, .5f, Ease.OutBack).Delay(.12f);
            Audio.Sfx("chime2", .5f, 1.05f);
        }

        /// <summary>
        /// How tall the bubble may grow before the body is shrunk instead. Read off the
        /// canvas rather than fixed, because it is width-matched at 1080 — its height is
        /// 1920 on a 16:9 phone and 1440 on a 4:3 tablet, and what is left over has to
        /// keep the ring this is pointing at visible on both.
        /// </summary>
        float MaxHeight() => Mathf.Min(780f, Content.rect.height - 460f);

        /// <summary>
        /// Picks the largest size at which the body fits <paramref name="room"/> and
        /// returns the height it wants there.
        /// </summary>
        /// <remarks>
        /// A step at a time rather than a search: eleven sizes at most, each one a
        /// measurement uGUI answers from cached glyph metrics in the same frame, run once
        /// in the life of a tip. If even the smallest does not fit, the bubble grows rather
        /// than the text overlapping — which is the whole point of measuring, and needs a
        /// tip several times longer than any that exists.
        /// </remarks>
        static float FitBody(Text body, float room, int min)
        {
            while (body.fontSize > min && body.preferredHeight > room)
                body.fontSize--;

            return Mathf.Ceil(body.preferredHeight);
        }

        /// <summary>Narrows an unwrapped line until it fits <paramref name="room"/> across.</summary>
        static void Squeeze(Text line, float room, int min)
        {
            while (line.fontSize > min && line.preferredWidth > room)
                line.fontSize--;
        }

        void Accept()
        {
            // Marked here rather than on show, so a player who is interrupted mid-tip
            // — a call, a crash, the app swapped out — still gets taught next time.
            TipLedger.MarkSeen(Mechanic);

            Close(() => Dismissed?.Invoke());
        }

        /// <summary>The back gesture must not skip a lesson silently; treat it as OK.</summary>
        public override bool OnBack()
        {
            Accept();
            return true;
        }
    }
}
