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

        /// <summary>
        /// Anything else the lesson names, ringed and lit exactly as <see cref="Target"/> is.
        ///
        /// <para>
        /// <b>A second subject, not a bigger ring.</b> Blending is the lesson that needs it:
        /// the sentence says two hearts join and their light mixes, and a ring round the gold
        /// critter alone leaves the player hunting the board for the two hearts it is talking
        /// about. Ringing each of them says which, and the hole widens to keep all three lit
        /// at once — which is the same bargain a demonstration already makes with
        /// <see cref="Trace"/>: the hole covers everything, and a ring stays on a subject.
        /// </para>
        /// </summary>
        public RectTransform[] Alongside;

        /// <summary>
        /// An ordered route for a coaching hand to trace on the real board, or null for a
        /// lesson that is only a sentence.
        ///
        /// <para>
        /// <b>It is for a lesson about a gesture rather than about a rule.</b> Every glade tip
        /// names something on the board and says what it does, and a ring plus two sentences is
        /// exactly the right shape for that. Lightweave's opening lesson is not that shape: after
        /// four chapters of tapping tiles the first thing a player has to know is that this mode
        /// is <em>dragged</em>, and a sentence describing a movement has to be turned back into
        /// the movement by whoever reads it. So the hand does that half and the sentence is cut
        /// down to what it is actually good at — the rule the movement does not show.
        /// </para>
        /// <para>
        /// Every point of the route stays out of the dim, so the demonstration happens on the
        /// board itself rather than on a diagram of one. <see cref="Target"/> is still what gets
        /// the ring, which is what keeps the two ideas separate: the route says <em>do this</em>
        /// and the ring says <em>this is the thing</em>, and a lesson may want either, both or
        /// neither.
        /// </para>
        /// </summary>
        public RectTransform[] Trace;

        /// <summary>The colour the demonstration is drawn in — normally the pair's own.</summary>
        public Color TraceTint = Pal.Cream;

        /// <summary>How far the route reaches in board cells, which decides its pace.</summary>
        public int TraceCells;

        /// <summary>
        /// Raised once this tip is done with, so the run can be handed back.
        ///
        /// <para>
        /// <b>Exactly once, whatever the exit</b> — the OK button, the back gesture, or the
        /// panel simply being destroyed underneath itself. That is the house rule about panels
        /// with several exits, and here it is load-bearing rather than tidy: whoever is showing
        /// this holds the run's clock until it fires, so a tip that went away without reporting
        /// would leave a board that can never be lost on time. The safe outcome is therefore on
        /// <see cref="OnDestroy"/>, which every exit passes through, and the latch is what stops
        /// the ordinary exit reporting twice.
        /// </para>
        /// </summary>
        public System.Action Dismissed;

        /// <summary>
        /// Where this lesson sits in the modal stack, declared by whoever raises it.
        ///
        /// <para>
        /// The default is the bottom of it, so a lesson can never cover a panel the player asked
        /// for. See <see cref="ModalLayer"/> — this is the only overlay in the game that raises
        /// itself on a timer, and therefore the only one whose arrival order means nothing.
        /// </para>
        /// <para>
        /// The one exception is a lesson about a control that lives on a <em>panel</em> rather
        /// than on the board, which has to be told <see cref="ModalLayer.Coaching"/> — there the
        /// default hides the tip and the thing it is pointing at behind the same panel. It is a
        /// declaration rather than something worked out from <see cref="Target"/> because this
        /// overlay is handed a rectangle and nothing else: whose rectangle it is, and whether
        /// that owner is a panel or a board, is knowledge only the caller has.
        /// </para>
        /// </summary>
        public int Stack { get; set; } = ModalLayer.Teaching;

        /// <inheritdoc/>
        public override int Layer => Stack;

        bool _reported;

        const float Dim = .78f;
        const float Pad = 18f;

        /// <summary>How near the screen's edge the bubble may slide. See <see cref="BuildBubble"/>.</summary>
        const float EdgeMargin = 24f;

        /// <summary>
        /// How far from the bubble's centre the beak may sit: half the paper, less its rounded
        /// corner and half the beak's own width, so it always has flat paper under it.
        /// </summary>
        const float BeakReach = 780f * .5f - 28f - 23f;

        protected override void Build()
        {
            var rings = Rings();
            var spot = SpotlightRect(rings);

            if (spot.HasValue) BuildCutout(spot.Value, rings);
            else UIKit.Scrim(Content, Dim, null);

            BuildTrace();
            BuildBubble(spot);
        }

        /// <summary>
        /// Every rectangle this lesson is naming, in this overlay's space. Empty for a rule
        /// that lives off the board, or one whose tiles are not drawn.
        /// </summary>
        System.Collections.Generic.List<Rect> Rings()
        {
            var rings = new System.Collections.Generic.List<Rect>(2);

            var subject = RectOf(Target);
            if (subject.HasValue) rings.Add(subject.Value);

            if (Alongside != null)
                foreach (var other in Alongside)
                {
                    var r = RectOf(other);
                    if (r.HasValue) rings.Add(r.Value);
                }

            return rings;
        }

        /// <summary>
        /// What has to stay lit: everything being pointed at, and every point the hand visits.
        ///
        /// Null when there is nothing on the board at all — a move budget lives in the HUD, not
        /// in a cell.
        /// </summary>
        Rect? SpotlightRect(System.Collections.Generic.List<Rect> rings)
        {
            Rect? spot = null;

            foreach (var ring in rings)
                spot = spot.HasValue ? Union(spot.Value, ring) : ring;

            if (Trace != null)
                foreach (var step in Trace)
                {
                    var r = RectOf(step);
                    if (!r.HasValue) continue;

                    spot = spot.HasValue ? Union(spot.Value, r.Value) : r;
                }

            return spot;
        }

        /// <summary>A transform's rectangle in this overlay's space, with the ring's margin.</summary>
        Rect? RectOf(RectTransform target)
        {
            if (!target) return null;

            var corners = new Vector3[4];
            target.GetWorldCorners(corners);

            var min = (Vector2)Content.InverseTransformPoint(corners[0]);
            var max = (Vector2)Content.InverseTransformPoint(corners[2]);

            return new Rect(min.x - Pad, min.y - Pad,
                            max.x - min.x + Pad * 2f, max.y - min.y + Pad * 2f);
        }

        static Rect Union(Rect a, Rect b)
        {
            float x = Mathf.Min(a.xMin, b.xMin), y = Mathf.Min(a.yMin, b.yMin);
            return new Rect(x, y, Mathf.Max(a.xMax, b.xMax) - x, Mathf.Max(a.yMax, b.yMax) - y);
        }

        /// <summary>
        /// The hand, drawn over the hole and under the bubble.
        ///
        /// <para>
        /// Under the bubble deliberately, and it is the reason the bubble is placed against the
        /// hole rather than in the middle of the screen: a demonstration the player has to move a
        /// panel off to see is a demonstration nobody sees.
        /// </para>
        /// </summary>
        void BuildTrace()
        {
            if (Trace == null || Trace.Length < 2) return;

            var route = new System.Collections.Generic.List<Vector2>(Trace.Length);
            var corners = new Vector3[4];

            foreach (var step in Trace)
            {
                if (!step) continue;

                step.GetWorldCorners(corners);
                var min = (Vector2)Content.InverseTransformPoint(corners[0]);
                var max = (Vector2)Content.InverseTransformPoint(corners[2]);
                route.Add((min + max) * .5f);
            }

            if (route.Count < 2) return;

            CoachHand.Show(Content, route, TraceTint, Mathf.Max(1, TraceCells), this);
        }

        /// <summary>Four quads around the hole, so the tiles beneath stay fully visible.</summary>
        void BuildCutout(Rect hole, System.Collections.Generic.List<Rect> rings)
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

            // A ring goes round each thing being named, never round the hole. For a tip naming
            // one tile those are the same rectangle, and they are deliberately not the same as
            // soon as a lesson names two — or demonstrates a route: the hole is widened to keep
            // every subject lit, and an outline stretched to that would be pointing at a region
            // of the board rather than at the things the sentence is about.
            foreach (var ring in rings) Outline(ring);
        }

        /// <summary>
        /// A border traced around one thing being named, not a halo floating over it.
        ///
        /// RoundOutline is a sliced sprite, so it takes the target's proportions instead of
        /// forcing everything into the same oval — a wide HUD pill and a square tile each get
        /// an outline that actually fits them.
        /// </summary>
        void Outline(Rect box)
        {
            var border = UIKit.Img("Border", Content, Art.RoundOutline(26, 5f),
                                   Pal.A(Pal.Gold, .95f),
                                   new Vector2(box.width, box.height),
                                   new Vector2(.5f, .5f), box.center);
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

            float y = 0f, x = 0f;
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

                // Slid towards whatever it is naming, as far as the screen allows.
                //
                // It used to be hard-centred, which was invisible for as long as every tip
                // pointed at a board tile: a board is centred too, so the subject was always
                // within the beak's own clamp and the beak reached it. The first tip aimed at
                // a *corner* control breaks that — the mode switcher's centre is 356 from the
                // middle of a 1080 canvas, the beak clamps at 273, and the result is a pointer
                // aimed 83px to the left of the pill it is talking about, on a bubble whose
                // right edge stops 134px short of the thing under discussion.
                //
                // Sliding the paper fixes the general case rather than that one control: the
                // bubble goes as near its subject as it can without leaving the screen, and
                // the beak then has a short distance to cover instead of an impossible one.
                // For a centred board tile the slide is small and the beak lands exactly where
                // it always did, so nothing already shipped moves anywhere it should not.
                float room = Content.rect.width * .5f - Width * .5f - EdgeMargin;
                x = room > 0f ? Mathf.Clamp(hole.center.x, -room, room) : 0f;
            }

            // A plain white bubble rather than the carved wooden panel the other
            // overlays use. Those are furniture the player is meant to look at; this is
            // a note about something else on screen, and it should get out of the way.
            var panel = UIKit.Img("Bubble", Content, Art.Round(28), Color.white,
                                  new Vector2(Width, height), new Vector2(.5f, .5f), new Vector2(x, y));
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
                // Relative to the bubble, which has just been slid — an absolute position here
                // would put the beak back where the subject is *not*. Still clamped, so it
                // stays on the straight part of the paper rather than climbing a rounded
                // corner, for the case where even a fully slid bubble cannot reach.
                var beak = UIKit.Img("Beak", rt, Art.Crystal(48), Color.white,
                                     new Vector2(46f, 46f), new Vector2(.5f, above ? 0f : 1f),
                                     new Vector2(Mathf.Clamp(spot.Value.center.x - x, -BeakReach, BeakReach),
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

            // Its own sound rather than the panel one, because this builds its own bubble
            // rather than calling MakePanel — so nothing else would speak for it. The hush is
            // for the lessons key in a run's header, which is a button like any other.
            Audio.Hush("click");
            Audio.Sfx("tip", .5f);
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

            Close(Report);
        }

        /// <summary>Says this tip is finished with, once and once only. See <see cref="Dismissed"/>.</summary>
        void Report()
        {
            if (_reported) return;
            _reported = true;

            Dismissed?.Invoke();
        }

        /// <summary>
        /// The backstop. A tip torn down without being accepted — the screen navigating away
        /// underneath it, say — still reports, because the thing waiting on it is a run's
        /// clock and nothing else will ever come along to release it.
        /// </summary>
        void OnDestroy() => Report();

        /// <summary>The back gesture must not skip a lesson silently; treat it as OK.</summary>
        public override bool OnBack()
        {
            Accept();
            return true;
        }
    }
}
