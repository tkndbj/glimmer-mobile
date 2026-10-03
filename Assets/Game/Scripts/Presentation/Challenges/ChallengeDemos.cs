using GlimmerGrove.Challenges;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The four demonstrations the challenge preview plays (<see cref="ChallengePreviewOverlay"/>):
    /// a board of three to five cells in a genre's own pieces, played by a coaching hand over and
    /// over, with the four starter turrets standing above it and the one the move feeds firing.
    ///
    /// <para>
    /// <b>A picture of the verb rather than a sentence about it</b>, which is the loadout's
    /// bargain (<c>WardPreviewOverlay</c>): a turret is bought from a panel that shows it
    /// firing, and a puzzle is now entered from a panel that shows it being played. What each
    /// board shows is the genre's one verb and its one consequence - turn two cards and the
    /// pair fires its turret; slide a gem into its twin and the size it makes fires; turn a
    /// conduit and the critter it wakes fires; push a gem onto its pad and the pad fires.
    /// Nothing else: a combo, a curse, a rock, UNDO and the hill are met on the board, where
    /// they happen, rather than described before anything has.
    /// </para>
    /// <para>
    /// <b>One looping tween, no state</b> - <c>CoachHand</c>'s rule, kept for its reason: every
    /// frame reads the whole scene out of one elapsed time, so a panel closed at any instant
    /// leaves nothing scheduled, and the loop's seam is a fade rather than a cut. The hand's
    /// timing is the coaching hand's own (<see cref="Gesture"/>, <c>CoachStroke</c>'s beats),
    /// so a demonstration here cannot run at a different pace from the one the tutorial draws.
    /// </para>
    /// <para>
    /// <b>Every piece is the genre's own picture</b>: the card backs and stones Pairs deals,
    /// the gems and numbers Merge slides, the cannon Push walks, the gem a glade critter wears
    /// (<c>BoardView.LampFace</c>) - all resident through the screen's own hold by the time the
    /// panel can be raised, and each drawn as the view draws it when its sprite has not arrived
    /// (invariant 7b: a plain disc, never a white rectangle). The only drawn pieces are the
    /// glade's conduit arms, which the mode itself draws as capsules (<c>TileView</c>).
    /// </para>
    /// </summary>
    public static class ChallengeDemos
    {
        /// <summary>A demo cell. Close to what a phone draws a real cell at.</summary>
        public const float Cell = 150f;

        /// <summary>The line row and the board row, measured from the stage's centre.</summary>
        const float LineY = 168f, BoardY = -72f;

        /// <summary>A mini turret, and the air between two of them.</summary>
        const float PostSize = 128f, PostPitch = 190f;

        /// <summary>How long a post's firing reel runs, and how big its flash blooms.</summary>
        const float FireFor = .55f, FlashSize = 240f;

        /// <summary>The hand: the coaching hand's own size and rise.</summary>
        const float HandSize = 156f, Rise = 38f;

        /// <summary>The fade in and out at the loop's seam, so a repeat starts on a clean board.</summary>
        const float Seam = .30f;

        // ------------------------------------------------------------------ the gesture
        /// <summary>
        /// One frame of the demonstrating hand: where its fingertip is, how visible it is, how
        /// far it is lifted off the board, how hard it presses, and how far along its drag it
        /// has got. <see cref="Trace"/> reads it out of the clock.
        /// </summary>
        public readonly struct Gesture
        {
            public readonly Vector2 At;
            public readonly float Alpha, Lift, Press, Along;

            public Gesture(Vector2 at, float alpha, float lift, float press, float along)
            {
                At = at;
                Alpha = alpha;
                Lift = lift;
                Press = press;
                Along = along;
            }
        }

        /// <summary>The coaching hand's beats (<c>CoachStroke</c>): fading in above the spot, settling onto it, lifting away.</summary>
        public const float ReachFor = CoachStroke.ReachSeconds, PressFor = CoachStroke.PressSeconds,
                           LiftFor = CoachStroke.LiftSeconds;

        /// <summary>When a gesture begun at <paramref name="start"/> is pressed onto the board - the instant the board answers a tap.</summary>
        public static float Pressed(float start) => start + ReachFor + PressFor;

        /// <summary>
        /// When a drag begun at <paramref name="start"/> and drawn for <paramref name="draw"/>
        /// seconds lets go - or a tap (nought) lifts after its <see cref="TapHold"/>. The one
        /// place the hold is added, so <see cref="Trace"/> and the callers agree.
        /// </summary>
        public static float Released(float start, float draw) => Pressed(start) + (draw > 0f ? draw : TapHold);

        /// <summary>When the hand is gone again.</summary>
        public static float Gone(float start, float draw) => Released(start, draw) + LiftFor;

        /// <summary>
        /// The hand <paramref name="t"/> seconds into the loop, for a gesture begun at
        /// <paramref name="start"/> that presses at <paramref name="from"/> and drags to
        /// <paramref name="to"/> over <paramref name="draw"/> seconds (nought for a tap, which
        /// holds the press for <see cref="TapHold"/> instead). Invisible outside its own beats.
        /// </summary>
        public static Gesture Trace(float t, float start, Vector2 from, Vector2 to, float draw)
        {
            float u = t - start;
            float hold = draw > 0f ? draw : TapHold;

            if (u < 0f) return new Gesture(from, 0f, 1f, 0f, 0f);

            if (u < ReachFor)
            {
                float s = Smooth(u / ReachFor);
                return new Gesture(from, s, 1f - s, 0f, 0f);
            }
            u -= ReachFor;

            if (u < PressFor)
                return new Gesture(from, 1f, 0f, Smooth(u / PressFor), 0f);
            u -= PressFor;

            if (u < hold)
            {
                float s = draw > 0f ? Smooth(u / hold) : 0f;
                return new Gesture(Vector2.Lerp(from, to, s), 1f, 0f, 1f, s);
            }
            u -= hold;

            if (u < LiftFor)
            {
                float s = Smooth(u / LiftFor);
                return new Gesture(to, 1f - s, s, 1f - s, 1f);
            }

            return new Gesture(to, 0f, 1f, 0f, 1f);
        }

        /// <summary>How long a tap stays down. Long enough to be a press, short enough not to hide what it did.</summary>
        public const float TapHold = .16f;

        static float Smooth(float t) => t <= 0f ? 0f : t >= 1f ? 1f : t * t * (3f - 2f * t);

        // ------------------------------------------------------------------ the stage
        sealed class Stage
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public readonly Image[] Post = new Image[ChallengeColours.Count];
            public readonly Sprite[] Idle = new Sprite[ChallengeColours.Count];
            public readonly Sprite[][] Fire = new Sprite[ChallengeColours.Count][];
            public Image Flash;
            public Image Hand;
            public RectTransform HandRt;
        }

        /// <summary>
        /// Builds the genre's demonstration under <paramref name="host"/>, <paramref name="size"/>
        /// large, and starts it looping. Killed with <paramref name="owner"/>.
        /// </summary>
        public static RectTransform Build(ChallengeGenre genre, RectTransform host, Vector2 size, Object owner)
        {
            var stage = new Stage
            {
                Root = UIKit.Box("Demo", host, size, new Vector2(.5f, .5f), Vector2.zero),
            };
            stage.Group = UIKit.Group(stage.Root);

            Line(stage);

            System.Action<float> play;
            float cycle;

            // A `switch` whose default refuses (invariant 44e), exactly as the screen's own
            // `Make` does: a fifth genre is a demonstration written, never one skipped.
            switch (genre)
            {
                case ChallengeGenre.Pairs: cycle = Pairs(stage, out play); break;
                case ChallengeGenre.Glade: cycle = Glade(stage, out play); break;
                case ChallengeGenre.Merge: cycle = Merge(stage, out play); break;
                case ChallengeGenre.Sokoban: cycle = Push(stage, out play); break;
                default:
                    throw new System.InvalidOperationException($"genre '{genre}' has no demonstration");
            }

            // The flash and the hand are built last, so they draw over every piece.
            stage.Flash = UIKit.Img("Flash", stage.Root, Art.Glow(128, 1.8f), Color.clear, Vector2.one * FlashSize);
            stage.Flash.raycastTarget = false;
            stage.Flash.enabled = false;

            stage.Hand = UIKit.Img("Hand", stage.Root, Art.Hand(160), Color.white, Vector2.one * HandSize);
            stage.Hand.raycastTarget = false;
            stage.HandRt = stage.Hand.rectTransform;
            stage.HandRt.pivot = Art.HandFingertip;
            stage.Hand.enabled = false;

            Tween.Run(cycle, Ease.Linear, t =>
            {
                if (!stage.Root) return;

                float at = t * cycle;
                stage.Group.alpha = SeamAlpha(at, cycle);
                play(at);
            }, owner, "demo").Loop(-1, false);

            return stage.Root;
        }

        /// <summary>The loop's alpha: in over the first <see cref="Seam"/> seconds, out over the last.</summary>
        public static float SeamAlpha(float at, float cycle)
        {
            if (at < Seam) return Mathf.Clamp01(at / Seam);
            if (at > cycle - Seam) return Mathf.Clamp01((cycle - at) / Seam);
            return 1f;
        }

        /// <summary>The four starter turrets, one per colour, standing over the board.</summary>
        static void Line(Stage stage)
        {
            for (int c = 0; c < ChallengeColours.Count; c++)
            {
                var sprite = ChallengeArt.Ward(c);
                var post = UIKit.Img("Post" + c, stage.Root, sprite, Color.white, Vector2.one * PostSize,
                                     new Vector2(.5f, .5f), new Vector2(PostX(c), LineY));
                post.preserveAspect = true;
                post.raycastTarget = false;
                post.enabled = sprite != null;

                stage.Post[c] = post;
                stage.Idle[c] = sprite;
                stage.Fire[c] = ChallengeArt.Fire(c);
            }
        }

        static float PostX(int colour) => (colour - (ChallengeColours.Count - 1) * .5f) * PostPitch;

        /// <summary>
        /// Every post at rest but the one firing, which runs its reel from <paramref name="t0"/>
        /// for <see cref="FireFor"/> under a bloom of its colour. Written in full every frame,
        /// so a loop restarted mid-reel shows no stale frame (invariant 48l).
        /// </summary>
        static void Firing(Stage stage, int colour, float t0, float t)
        {
            float u = (t - t0) / FireFor;
            bool firing = u >= 0f && u < 1f;

            for (int c = 0; c < stage.Post.Length; c++)
            {
                var post = stage.Post[c];
                if (!post) continue;

                var frames = c == colour && firing ? stage.Fire[c] : null;
                var sprite = frames != null && frames.Length > 0
                    ? frames[Mathf.Min(frames.Length - 1, Mathf.FloorToInt(u * frames.Length))]
                    : stage.Idle[c];

                if (post.sprite != sprite) post.sprite = sprite;
                post.enabled = sprite != null;
            }

            var flash = stage.Flash;
            if (!flash) return;

            flash.enabled = firing;
            if (!firing) return;

            flash.rectTransform.anchoredPosition = new Vector2(PostX(colour), LineY);
            flash.transform.localScale = Vector3.one * Mathf.Lerp(.5f, 1.5f, u);
            flash.color = Pal.A(ChallengeArt.Tint(colour), (1f - u) * .85f);
        }

        /// <summary>The hand, drawn from one <see cref="Gesture"/>.</summary>
        static void Hand(Stage stage, Gesture g)
        {
            var hand = stage.Hand;
            if (!hand) return;

            hand.enabled = g.Alpha > .001f;
            if (!hand.enabled) return;

            stage.HandRt.anchoredPosition = g.At + new Vector2(0f, g.Lift * Rise);
            stage.HandRt.localScale = Vector3.one * (1f + g.Lift * .10f - g.Press * .09f);
            hand.color = new Color(1f, 1f, 1f, g.Alpha);
        }

        // ------------------------------------------------------------------ furniture
        /// <summary>Where cell <paramref name="i"/> of a row of <paramref name="n"/> sits.</summary>
        static Vector2 CellAt(int i, int n) => new Vector2((i - (n - 1) * .5f) * Cell, BoardY);

        /// <summary>The dark socket every board stands its pieces in.</summary>
        static void Slot(Stage stage, Vector2 at)
        {
            var slot = UIKit.Img("Slot", stage.Root, Art.Round(16), Pal.Slot, Vector2.one * Cell * .92f,
                                 new Vector2(.5f, .5f), at);
            slot.type = Image.Type.Sliced;
            slot.raycastTarget = false;
        }

        /// <summary>A gem of a colour, or a disc in its tint while the picture is not resident (7b).</summary>
        static Image Gem(Stage stage, int colour, float scale, Vector2 at, string name = "Gem")
        {
            var sprite = ChallengeArt.Gem(colour);
            var gem = UIKit.Img(name, stage.Root, sprite != null ? sprite : Art.Disc(96),
                                sprite != null ? Color.white : ChallengeArt.Tint(colour),
                                Vector2.one * Cell * scale, new Vector2(.5f, .5f), at);
            gem.preserveAspect = true;
            gem.raycastTarget = false;
            return gem;
        }

        static Image Glow(Stage stage, Color tint, float size, Vector2 at)
        {
            var glow = UIKit.Img("Glow", stage.Root, Art.Glow(128, 1.6f), Pal.A(tint, 0f), Vector2.one * size,
                                 new Vector2(.5f, .5f), at);
            glow.raycastTarget = false;
            return glow;
        }

        /// <summary>0 before <paramref name="from"/>, 1 after <paramref name="from"/> + <paramref name="over"/>, eased between.</summary>
        static float Ramp(float t, float from, float over) => Smooth((t - from) / over);

        /// <summary>A pop: 1 at rest, swelling to <paramref name="peak"/> and settling over <paramref name="over"/> seconds from <paramref name="from"/>.</summary>
        static float Pop(float t, float from, float over, float peak = 1.22f)
        {
            float u = (t - from) / over;
            if (u <= 0f || u >= 1f) return 1f;
            return 1f + (peak - 1f) * Mathf.Sin(u * Mathf.PI);
        }

        // ------------------------------------------------------------------ pairs
        /// <summary>
        /// Four cards face down; the hand turns the first and the third, which wear the same red
        /// stone, and the red turret fires. Cards flip as the real ones do - the back scaled away
        /// to nothing and the face scaled up - and a found pair lights its rims.
        /// </summary>
        static float Pairs(Stage stage, out System.Action<float> play)
        {
            const int n = 4;
            const float CardShare = .86f, GemShare = .50f, FlipFor = .32f;

            // Stone kinds: the first stone of red, green, red, blue (PairsGems: six stones a colour).
            int[] kind = { 0, PairsGems.Variants, 0, PairsGems.Variants * 2 };

            var node = new RectTransform[n];
            var back = new Image[n];
            var face = new Image[n];
            var rim = new Image[n];

            var size = Vector2.one * Cell * CardShare;
            var backArt = ChallengeArt.CardBack();
            var faceArt = ChallengeArt.CardFace();

            for (int i = 0; i < n; i++)
            {
                Slot(stage, CellAt(i, n));

                node[i] = UIKit.Box("Card" + i, stage.Root, size, new Vector2(.5f, .5f), CellAt(i, n));

                var shadow = UIKit.Img("Shadow", node[i], Art.Round(22), new Color(0f, .02f, .08f, .45f), size,
                                       new Vector2(.5f, .5f), new Vector2(0f, -Cell * .04f));
                shadow.type = Image.Type.Sliced;
                shadow.raycastTarget = false;

                face[i] = UIKit.Img("Face", node[i], faceArt != null ? faceArt : Art.Round(22),
                                    faceArt != null ? Color.white : Pal.Cream, size);
                if (faceArt == null) face[i].type = Image.Type.Sliced;
                face[i].raycastTarget = false;

                var stone = ChallengeArt.PairGem(kind[i]);
                var gem = UIKit.Img("Gem", face[i].transform, stone != null ? stone : Art.Disc(96),
                                    stone != null ? Color.white : ChallengeArt.Tint(PairsGems.ColourOf(kind[i])),
                                    Vector2.one * Cell * GemShare);
                gem.preserveAspect = true;
                gem.raycastTarget = false;

                rim[i] = UIKit.Img("Rim", face[i].transform, Art.RoundOutline(22, 6f),
                                   Pal.A(ChallengeArt.Tint(PairsGems.ColourOf(kind[i])), 0f), size * 1.03f);
                rim[i].raycastTarget = false;

                back[i] = UIKit.Img("Back", node[i], backArt != null ? backArt : Art.Round(22),
                                    backArt != null ? Color.white : new Color(.15f, .38f, .86f, 1f), size);
                if (backArt == null) back[i].type = Image.Type.Sliced;
                back[i].raycastTarget = false;
            }

            // The beats.
            const float tap1 = .30f, tap2 = 1.75f;
            float flip1 = Pressed(tap1), flip2 = Pressed(tap2);
            float paired = flip2 + FlipFor + .10f;
            float fire = paired + .20f;
            const float cycle = 4.6f;

            int colour = PairsGems.ColourOf(kind[0]);

            play = t =>
            {
                for (int i = 0; i < n; i++)
                {
                    float flipAt = i == 0 ? flip1 : i == 2 ? flip2 : float.PositiveInfinity;
                    float u = Mathf.Clamp01((t - flipAt) / FlipFor);

                    // The back scales down to the spine and the face scales up from it. Whole
                    // nodes, because the stone and the rim are the face's children and an
                    // Image switched off still draws what is under it.
                    bool up = u >= .5f;
                    float x = Mathf.Abs(1f - 2f * u);
                    if (back[i].gameObject.activeSelf == up) back[i].gameObject.SetActive(!up);
                    if (face[i].gameObject.activeSelf != up) face[i].gameObject.SetActive(up);
                    node[i].localScale = new Vector3(x, 1f, 1f);

                    float lit = i == 0 || i == 2 ? Ramp(t, paired, .20f) : 0f;
                    rim[i].color = Pal.A(ChallengeArt.Tint(PairsGems.ColourOf(kind[i])), lit * .95f);
                    if (lit > 0f) node[i].localScale = new Vector3(x, 1f, 1f) * Pop(t, paired, .36f, 1.12f);
                }

                Firing(stage, colour, fire, t);

                var g = t < tap2 - .01f
                    ? Trace(t, tap1, CellAt(0, n), CellAt(0, n), 0f)
                    : Trace(t, tap2, CellAt(2, n), CellAt(2, n), 0f);
                Hand(stage, g);
            };

            return cycle;
        }

        // ------------------------------------------------------------------ glade
        /// <summary>
        /// A crystal, a conduit and a sleeping critter in a row. The hand turns the conduit a
        /// quarter turn, the light runs through it, the critter wakes in its colour and its
        /// turret fires. The arms are capsules, as <c>TileView</c> draws them; the critter is the
        /// gem its turret fires, dim asleep and lit awake (<c>BoardView.LampFace</c>).
        /// </summary>
        static float Glade(Stage stage, out System.Action<float> play)
        {
            const int n = 3;
            const int colour = 0;
            const float Thick = 26f, TurnFor = .24f;

            var tint = ChallengeArt.Tint(colour);
            var dark = Color.Lerp(Pal.Slate, Pal.Cream, .22f);

            for (int i = 0; i < n; i++) Slot(stage, CellAt(i, n));

            // The light: a short arm to its right edge, always lit, and the mode's own
            // heart-crystal over a glow (TileView.BuildCrystal, piece for piece) - a light,
            // never a gem, because the gem is what a critter wears and two gems in a row read
            // as two critters.
            Arm(stage, CellAt(0, n) + new Vector2(Cell * .23f, 0f), Cell * .46f, Thick, 0f, tint);
            Crystal(stage, tint, CellAt(0, n));

            // The conduit: one arm across the whole tile, standing upright until it is turned.
            var pipe = Arm(stage, CellAt(1, n), Cell * .92f, Thick, 90f, dark);

            // The critter: a short arm to its left edge and the gem, asleep.
            var mouth = Arm(stage, CellAt(2, n) - new Vector2(Cell * .23f, 0f), Cell * .46f, Thick, 0f, dark);
            var wakeGlow = Glow(stage, tint, Cell * 1.5f, CellAt(2, n));
            var critter = Gem(stage, colour, .70f, CellAt(2, n), "Critter");

            const float tap = .30f;
            float turn = Pressed(tap);
            float lit = turn + TurnFor;
            float wake = lit + .18f;
            float fire = wake + .22f;
            const float cycle = 3.9f;

            play = t =>
            {
                // Upright (nought) until the tap, then a quarter turn to lie along the row and
                // join the two arms either side. It shipped the other way round for a day.
                pipe.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 90f * Ramp(t, turn, TurnFor));

                float flow = Ramp(t, lit, .18f);
                pipe.color = Color.Lerp(dark, tint, flow);
                mouth.color = Color.Lerp(dark, tint, Ramp(t, lit + .08f, .14f));

                float awake = Ramp(t, wake, .22f);
                critter.color = Color.Lerp(new Color(.45f, .45f, .5f, .55f), Color.white, awake);
                critter.transform.localScale = Vector3.one * Pop(t, wake, .40f, 1.25f);
                wakeGlow.color = Pal.A(tint, awake * .55f);

                Firing(stage, colour, fire, t);
                Hand(stage, Trace(t, tap, CellAt(1, n), CellAt(1, n), 0f));
            };

            return cycle;
        }

        /// <summary>The mode's heart-crystal, a light in <paramref name="tint"/>: glow, rim, crystal and core, as <c>TileView.BuildCrystal</c> draws it.</summary>
        static void Crystal(Stage stage, Color tint, Vector2 at)
        {
            float size = Cell * .92f;

            var glow = UIKit.Img("SourceGlow", stage.Root, Art.Glow(128, 2f), Pal.A(tint, .45f),
                                 Vector2.one * size * 1.22f, new Vector2(.5f, .5f), at);
            glow.raycastTarget = false;

            var rim = UIKit.Img("Rim", stage.Root, Art.Crystal(128), new Color(.09f, .15f, .21f, .85f),
                                Vector2.one * size * .56f, new Vector2(.5f, .5f), at);
            rim.raycastTarget = false;

            var crystal = UIKit.Img("Crystal", stage.Root, Art.Crystal(128), Pal.Lift(tint, .45f),
                                    Vector2.one * size * .46f, new Vector2(.5f, .5f), at);
            crystal.raycastTarget = false;

            var core = UIKit.Img("Core", crystal.transform, Art.Crystal(128), new Color(1f, 1f, 1f, .8f));
            UIKit.StretchTo((RectTransform)core.transform, size * .12f, size * .12f, size * .12f, size * .12f);
            core.raycastTarget = false;
        }

        /// <summary>A conduit arm: a capsule of <paramref name="length"/> centred at <paramref name="at"/>, lying along <paramref name="angle"/>.</summary>
        static Image Arm(Stage stage, Vector2 at, float length, float thick, float angle, Color colour)
        {
            var arm = UIKit.Img("Arm", stage.Root, Art.Capsule(24, 96), colour, new Vector2(thick, length),
                                new Vector2(.5f, .5f), at);
            arm.raycastTarget = false;
            // A capsule is cut upright, so lying along the row is a quarter turn.
            arm.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 90f + angle);
            return arm;
        }

        // ------------------------------------------------------------------ merge
        /// <summary>
        /// Two gems of the smallest size at the ends of a row. The hand drags the left one; it
        /// slides until its twin stops it, the two join into the next size, and that size's
        /// turret fires. The numbers are the board's own (<c>MergeView.Dress</c>).
        /// </summary>
        static float Merge(Stage stage, out System.Action<float> play)
        {
            const int n = 4;
            const float SlideFor = .14f, JoinFor = .34f;

            for (int i = 0; i < n; i++) Slot(stage, CellAt(i, n));

            var twin = Piece(stage, 1, CellAt(3, n), "Twin");
            var mover = Piece(stage, 1, CellAt(0, n), "Mover");
            var made = Piece(stage, 2, CellAt(3, n), "Made");
            made.Node.gameObject.SetActive(false);
            var joinGlow = Glow(stage, ChallengeArt.Tint(MergePuzzle.ColourOf(2)), Cell * 1.6f, CellAt(3, n));

            const float drag = .30f, draw = .55f;
            float release = Released(drag, draw);
            float slide = release + .10f;
            float join = slide + SlideFor;
            float fire = join + .12f;
            const float cycle = 4.3f;

            int colour = MergePuzzle.ColourOf(2);

            play = t =>
            {
                var g = Trace(t, drag, CellAt(0, n), CellAt(2, n), draw);

                // Under the finger the gem follows it; let go, it slides the last cell into its twin.
                Vector2 at = t < release
                    ? Vector2.Lerp(CellAt(0, n), CellAt(2, n), g.Along)
                    : Vector2.Lerp(CellAt(2, n), CellAt(3, n), Ramp(t, slide, SlideFor));

                bool joined = t >= join;
                mover.Node.anchoredPosition = at;
                mover.Node.localScale = Vector3.one * (g.Press > 0f && t < release ? 1.10f : 1f);
                mover.Node.gameObject.SetActive(!joined);
                twin.Node.gameObject.SetActive(!joined);

                made.Node.gameObject.SetActive(joined);
                made.Node.localScale = Vector3.one * Pop(t, join, JoinFor, 1.28f);
                joinGlow.color = Pal.A(ChallengeArt.Tint(colour), joined ? Mathf.Max(0f, 1f - (t - join) / .6f) * .6f : 0f);

                Firing(stage, colour, fire, t);
                Hand(stage, g);
            };

            return cycle;
        }

        sealed class MergePiece
        {
            public RectTransform Node;
        }

        /// <summary>A Merge gem of <paramref name="rank"/>: the gem of its colour with its value on it, sized as the board sizes it.</summary>
        static MergePiece Piece(Stage stage, int rank, Vector2 at, string name)
        {
            var piece = new MergePiece
            {
                Node = UIKit.Box(name, stage.Root, Vector2.one * Cell, new Vector2(.5f, .5f), at),
            };

            int colour = MergePuzzle.ColourOf(rank);
            var sprite = ChallengeArt.Gem(colour);
            float share = .66f + Mathf.Min(rank, 8) * .03f;

            var gem = UIKit.Img("Gem", piece.Node, sprite != null ? sprite : Art.Disc(96),
                                sprite != null ? Color.white : ChallengeArt.Tint(colour), Vector2.one * Cell * share);
            gem.preserveAspect = true;
            gem.raycastTarget = false;

            var value = UIKit.Titled("Value", piece.Node, (1 << rank).ToString(), Mathf.RoundToInt(Cell * .30f), Pal.Cream,
                                     TextAnchor.MiddleCenter, Vector2.one * Cell, default, default, 2f, 2f);
            value.raycastTarget = false;

            return piece;
        }

        // ------------------------------------------------------------------ push
        /// <summary>
        /// The keeper, a gem and the gem's pad in a row. Two swipes to the right: the keeper
        /// walks into the gem and pushes it a cell, then pushes it onto its pad, which lights
        /// and fires its turret. The cannon faces the way it walks, as the board turns it.
        /// </summary>
        static float Push(Stage stage, out System.Action<float> play)
        {
            const int n = 5;
            const int colour = 0;
            const float StepFor = .18f, KeeperCells = 1.5f;

            var tint = ChallengeArt.Tint(colour);

            for (int i = 0; i < n; i++) Slot(stage, CellAt(i, n));

            // The pad: its colour poured into the slot and a ring over it (SokobanView).
            var well = UIKit.Img("Well", stage.Root, Art.Glow(128, 1.4f), Pal.A(tint, .40f), Vector2.one * Cell * .86f,
                                 new Vector2(.5f, .5f), CellAt(3, n));
            well.raycastTarget = false;
            var pad = UIKit.Img("Pad", stage.Root, Art.Ring(128, 12f), Pal.A(tint, .9f), Vector2.one * Cell * .74f,
                                new Vector2(.5f, .5f), CellAt(3, n));
            pad.raycastTarget = false;

            var gem = Gem(stage, colour, .78f, CellAt(1, n));

            // The keeper: a shadow that stays put and the cannon, cut barrel up and turned to face right.
            var keeper = UIKit.Box("Keeper", stage.Root, Vector2.one * Cell, new Vector2(.5f, .5f), CellAt(0, n));
            var shadow = UIKit.Img("Shadow", keeper, Art.Disc(96), new Color(0f, 0f, 0f, .32f),
                                   new Vector2(Cell * .74f, Cell * .60f), new Vector2(.5f, .5f), new Vector2(0f, -Cell * .06f));
            shadow.raycastTarget = false;

            var cannon = ChallengeArt.Keeper();
            var body = cannon != null
                ? UIKit.Img("Body", keeper, cannon, Color.white, Vector2.one * Cell * KeeperCells)
                : UIKit.Img("Body", keeper, Art.Disc(96), Pal.Gold, Vector2.one * Cell * .70f);
            body.preserveAspect = true;
            body.raycastTarget = false;
            body.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -90f);

            const float swipe1 = .30f, swipe2 = 1.95f, draw = .28f;
            float step1 = Released(swipe1, draw);
            float step2 = Released(swipe2, draw);
            float seated = step2 + StepFor;
            float fire = seated + .18f;
            const float cycle = 4.9f;

            var reach = new Vector2(Cell * .55f, 0f);

            play = t =>
            {
                float s1 = Ramp(t, step1, StepFor), s2 = Ramp(t, step2, StepFor);

                keeper.anchoredPosition = CellAt(0, n) + (CellAt(1, n) - CellAt(0, n)) * (s1 + s2);
                gem.rectTransform.anchoredPosition = CellAt(1, n) + (CellAt(2, n) - CellAt(1, n)) * (s1 + s2);

                float lit = Ramp(t, seated, .22f);
                pad.color = Color.Lerp(Pal.A(tint, .9f), Pal.A(Pal.Cream, .95f), lit);
                well.color = Pal.A(tint, .40f + .35f * lit);
                gem.transform.localScale = Vector3.one * Pop(t, seated, .36f, 1.18f);

                Firing(stage, colour, fire, t);

                // A swipe starts on the keeper and runs a little way right: the gesture is the direction.
                var g = t < swipe2 - .01f
                    ? Trace(t, swipe1, CellAt(0, n), CellAt(0, n) + reach, draw)
                    : Trace(t, swipe2, CellAt(1, n), CellAt(1, n) + reach, draw);
                Hand(stage, g);
            };

            return cycle;
        }
    }
}
