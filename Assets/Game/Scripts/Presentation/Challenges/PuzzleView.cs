using System;
using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.Challenges;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The furniture the four puzzle boards share: a plate, a grid of cells laid out to the
    /// band they are given, an optional strip of keys under it, and the one door every input
    /// goes through.
    ///
    /// <para>
    /// <b>A view draws a state and hands inputs back.</b> Nothing here decides whether a tap
    /// was legal, what it fed or whether the hill walks — <see cref="Send"/> gives the input to
    /// <c>ChallengeScreen</c>, which asks the rules and then asks this class to
    /// <see cref="Animate"/> what the rules said. That is the split every board in this game
    /// keeps (invariant 3), and it is what lets <c>ChallengeTests</c> play every board with no
    /// view at all.
    /// </para>
    /// <para>
    /// <b>Laid out to the band, capped per cell</b>: a 6x3 grid and a 10x6 room get whatever
    /// band the screen gives them, so the cell is whichever of width, height and
    /// <see cref="MaxCell"/> binds first, and the grid is centred in what is left above the
    /// strip. <b>The width is meant to bind</b>: <see cref="BandWanted"/> tells the screen how
    /// tall the board is at the cell the width allows, and the screen gives the hill the rest
    /// — which is what makes a wide, short board a bigger hill rather than a taller plate.
    /// </para>
    /// <para>
    /// <b>The band is a framed plate, and the frame is as big as the band</b> (the owner's
    /// "put some nice frames or something, I don't want to see empty gaps", 2026-09-26). The
    /// first cut stood the grid's own plate in the middle of a bare ground, so on a tall phone
    /// the board floated between two slabs of nothing. Now the kit's panel
    /// (<see cref="Skins.Panel"/>) fills the band to <see cref="FrameInset"/> of its edges,
    /// the grid's plate stands <see cref="FrameRim"/> inside it as the well, and whatever the
    /// band has over what the grid wants is frame rather than gap. Both rims are counted
    /// into the cell the width allows and into the band the board asks for, so the frame
    /// never costs the grid a pixel it was not told about.
    /// </para>
    /// </summary>
    public abstract class PuzzleView : MonoBehaviour
    {
        /// <summary>The frame's air from the band's edges, on every side.</summary>
        public const float FrameInset = 10f;

        /// <summary>From the frame's edge to the plate's edge: the bezel the panel art draws plus breathing room.</summary>
        public const float FrameRim = 28f;

        /// <summary>What the frame takes off the band on each side before a board is laid out in it.</summary>
        public const float FrameSide = FrameInset + FrameRim;

        /// <summary>The plate's overhang past the grid, in cells (each side is half of it).</summary>
        protected const float PlateRim = .34f;

        protected ChallengeRun Run { get; private set; }
        protected RectTransform Host { get; private set; }

        /// <summary>The frame filling the band: the kit's panel, nine-sliced.</summary>
        protected Image Frame { get; private set; }

        /// <summary>
        /// The frame's inside — the band minus <see cref="FrameSide"/> on every side — which
        /// is what a board is laid out in. A board bringing its own floor (the glade's
        /// <c>BoardView</c>) builds into this rather than into <see cref="Host"/>.
        /// </summary>
        protected RectTransform Inner { get; private set; }

        protected RectTransform Field { get; private set; }
        protected RectTransform Strip { get; private set; }
        protected Image Plate { get; private set; }
        protected float Cell { get; private set; }

        Action<ChallengeInput> _send;

        /// <summary>
        /// Where a colour's post stands, for a board to fly a feed at, or null. Wired by the
        /// screen, because the hill is the screen's and a board knows nothing about it - the
        /// same one-way seam the input goes through the other way.
        /// </summary>
        public Func<int, RectTransform> PostOf;

        /// <summary>Told when a feed a board flew has reached a colour's post.</summary>
        public Action<int> FedPost;

        /// <summary>The readout saying what the puzzle is won by, for a lesson to ring, or null.</summary>
        public RectTransform GoalReadout;

        /// <summary>Height of the key strip under the grid, or nought for none.</summary>
        public virtual float StripHeight => 0f;

        protected virtual float MaxCell => 200f;

        /// <summary>
        /// Furniture a board hangs past its plate, above and below together, in cells — the
        /// pipes' sources and sinks. Counted into the band the board asks for, so it is not
        /// drawn into the rampart above or the foot of the screen below.
        /// </summary>
        protected virtual float EdgeRows => 0f;

        /// <summary>
        /// How much of <see cref="EdgeRows"/> hangs <em>below</em> the plate, in cells. The
        /// field is shifted up by half the difference, so furniture on one side alone (the
        /// merge ladder) is counted where it is drawn rather than centred as if it were on both.
        /// </summary>
        protected virtual float EdgeBelow => EdgeRows * .5f;

        /// <summary>
        /// Whether the shared plate is drawn under the grid. False for a board that brings
        /// its own floor (the glade's <c>BoardView</c>), or two plates stand one on the other.
        /// </summary>
        protected virtual bool DrawsPlate => true;

        /// <summary>
        /// Whether a win is celebrated by the board itself, so the screen's curtain arrives
        /// without its own flash and confetti — the glade's fanfare is the glade, and a second
        /// celebration a second later reads as one stuttering (<c>BoardView.Celebrate</c>).
        /// </summary>
        public virtual bool CelebratesItself => false;

        protected int Columns => Run.Puzzle.Width;
        protected int Rows => Run.Puzzle.Height;

        /// <summary>The cell a board of <c>columns</c> gets across <c>hostWidth</c>, frame, plate and cap counted in.</summary>
        float CellAcross(int columns, float hostWidth)
            => Mathf.Floor(Mathf.Min((hostWidth - FrameSide * 2f) / (columns + PlateRim), MaxCell));

        /// <summary>
        /// How tall a band this board wants when laid out across <c>hostWidth</c>: its rows at
        /// the cell the width allows, the plate's rim, the frame and the key strip. The screen
        /// asks this before it sizes the hill, so the hill can take everything the board does
        /// not need.
        /// </summary>
        public float BandWanted(int columns, int rows, float hostWidth)
        {
            float cell = CellAcross(columns, hostWidth);
            return (rows + PlateRim + EdgeRows) * cell + FrameSide * 2f + StripHeight;
        }

        public void Attach(ChallengeRun run, RectTransform host, Action<ChallengeInput> send)
        {
            Run = run;
            Host = host;
            _send = send;

            // The frame first, filling the band; everything else stands inside it.
            var panel = Art.S("Ui/" + Skins.Panel);
            Frame = UIKit.Img("Frame", host, panel != null ? panel : Art.Round(34),
                              panel != null ? Color.white : Skins.Plate);
            Frame.type = Image.Type.Sliced;
            Frame.raycastTarget = false;
            UIKit.StretchTo(Frame.rectTransform, FrameInset, FrameInset, FrameInset, FrameInset);

            Inner = UIKit.Node("Inner", host);
            UIKit.StretchTo(Inner, FrameSide, FrameSide, FrameSide, FrameSide);

            float innerW = host.rect.width - FrameSide * 2f;
            float innerH = host.rect.height - FrameSide * 2f;
            float h = innerH - StripHeight;

            Cell = Mathf.Floor(Mathf.Min(CellAcross(Columns, host.rect.width), h / (Rows + PlateRim + EdgeRows)));
            if (Cell < 8f) Cell = 8f;

            var span = new Vector2(Columns * Cell, Rows * Cell);
            var centre = new Vector2(0f, StripHeight * .5f + (2f * EdgeBelow - EdgeRows) * Cell * .5f);

            if (DrawsPlate)
            {
                var plate = ChallengeArt.Plate();
                Plate = UIKit.Img("Plate", Inner, plate != null ? plate : Art.Round(34),
                                  plate != null ? Color.white : Pal.Board,
                                  span + Vector2.one * Cell * PlateRim, new Vector2(.5f, .5f), centre);
                Plate.type = Image.Type.Sliced;
                Plate.raycastTarget = false;
            }

            Field = UIKit.Box("Field", Inner, span, new Vector2(.5f, .5f), centre);

            if (StripHeight > 0f)
                Strip = UIKit.Box("Strip", Inner, new Vector2(innerW, StripHeight),
                                  new Vector2(.5f, 0f), new Vector2(0f, StripHeight * .5f));

            Build();
            Repaint();
        }

        protected void Send(ChallengeInput input) => _send?.Invoke(input);

        /// <summary>Where the centre of a cell sits inside <see cref="Field"/>. Row nought is the top.</summary>
        protected Vector2 CentreOf(int index)
        {
            int x = index % Columns, y = index / Columns;
            return new Vector2((x - (Columns - 1) * .5f) * Cell, ((Rows - 1) * .5f - y) * Cell);
        }

        // ------------------------------------------------------------------ shared pieces
        /// <summary>The dark sockets a gem stands in, one per cell.</summary>
        protected Image[] Sockets(float inset = .92f)
        {
            var slots = new Image[Columns * Rows];
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = UIKit.Img("Slot", Field, Art.Round(16), Pal.Slot, new Vector2(Cell * inset, Cell * inset));
                slot.raycastTarget = false;
                slot.type = Image.Type.Sliced;
                slot.rectTransform.anchoredPosition = CentreOf(i);
                slots[i] = slot;
            }
            return slots;
        }

        /// <summary>One invisible tap target per cell.</summary>
        protected void Targets(Action<int> tap)
        {
            for (int cell = 0; cell < Columns * Rows; cell++)
            {
                int at = cell;
                var img = UIKit.Img("hit", Field, Art.Pixel, new Color(0f, 0f, 0f, 0f), new Vector2(Cell, Cell));
                img.raycastTarget = true;
                img.rectTransform.anchoredPosition = CentreOf(at);

                var btn = img.gameObject.AddComponent<Btn>();
                btn.PressScale = 1f;
                btn.Setup(() => tap(at), silent: true);
            }
        }

        /// <summary>
        /// A swipe anywhere on the field. <c>dir.y</c> is up-positive.
        ///
        /// <b>The handler sits on the field, not on a catcher</b>: Unity finds a drag handler
        /// by walking up from whatever the finger hit, so a board that also has tap targets
        /// per cell still swipes — the target is hit, the field above it handles the drag. The
        /// catcher only exists so a board with no targets has something to hit.
        /// </summary>
        protected void Swipes(Action<Vector2Int> swiped)
        {
            var catcher = UIKit.Img("swipe", Field, Art.Pixel, new Color(0f, 0f, 0f, 0f));
            catcher.raycastTarget = true;
            catcher.transform.SetAsFirstSibling();

            var drag = Field.gameObject.AddComponent<CellDrag>();
            drag.Threshold = Cell * .35f;
            drag.Dragged = swiped;
        }

        /// <summary>A key in the strip, at an x measured from the strip's centre.</summary>
        protected Btn Key(string name, string text, float x, float width, Action onClick, string skin = null)
            => UIKit.TextButton(name, Strip, skin ?? Skins.Alternate, text.ToUpperInvariant(), 28,
                                new Vector2(width, StripHeight * .66f), new Vector2(.5f, .5f),
                                new Vector2(x, 0f), onClick);

        protected Image GemAt(int cell, int colour, float scale = .82f, string name = "Gem")
        {
            var gem = UIKit.Img(name, Field, ChallengeArt.Gem(colour), Color.white, Vector2.one * Cell * scale);
            gem.raycastTarget = false;
            gem.preserveAspect = true;
            gem.rectTransform.anchoredPosition = CentreOf(cell);
            gem.enabled = gem.sprite != null;
            return gem;
        }

        /// <summary>
        /// A feed leaving the board: a mote in the colour flies from a cell to that colour's
        /// post and the hill is told when it lands. <b>The bridge between the two halves of a
        /// challenge, drawn.</b> A merge feeds a turret by rule and nothing on the screen used
        /// to say so - the bank pip changed, or a bolt left a post a beat later, and which
        /// swipe bought it was not readable. Nothing flies when the screen has wired no posts.
        /// </summary>
        /// <returns>How long the flight takes, so a caller can wait for it.</returns>
        protected float FlyFeed(int cell, int colour, float duration = .30f)
        {
            var post = PostOf?.Invoke(colour);
            if (post == null || Field == null) return 0f;

            var from = CentreOf(cell);
            var to = CentreIn(post, Field);
            var tint = ChallengeArt.Tint(colour);

            var mote = UIKit.Img("Feed", Field, Art.Glow(96, 1.6f), tint, Vector2.one * Cell * .46f,
                                 new Vector2(.5f, .5f), from);
            mote.raycastTarget = false;
            var core = UIKit.Img("Core", mote.transform, Art.Disc(48), Pal.Cream, Vector2.one * Cell * .14f);
            core.raycastTarget = false;

            // A lift off the straight line, so the mote reads as thrown rather than dragged.
            var dir = to - from;
            var side = new Vector2(-dir.y, dir.x).normalized * Cell * .55f;
            var rt = mote.rectTransform;
            var fed = FedPost;

            Tween.Run(duration, Ease.InOutSine, t =>
            {
                if (!rt) return;
                rt.anchoredPosition = Vector2.Lerp(from, to, t) + side * Mathf.Sin(t * Mathf.PI);
                rt.localScale = Vector3.one * (1f + .35f * Mathf.Sin(t * Mathf.PI));
            }, rt).OnDone(() =>
            {
                if (rt) Destroy(rt.gameObject);
                fed?.Invoke(colour);
            });

            return duration;
        }

        /// <summary>A widget's centre in another node's space - <c>TipOverlay.RectOf</c>'s arithmetic.</summary>
        protected static Vector2 CentreIn(RectTransform target, RectTransform space)
        {
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            var min = (Vector2)space.InverseTransformPoint(corners[0]);
            var max = (Vector2)space.InverseTransformPoint(corners[2]);
            return (min + max) * .5f;
        }

        protected abstract void Build();

        /// <summary>Put what is drawn back in step with the puzzle, without animating anything.</summary>
        public abstract void Repaint();

        /// <summary>
        /// The lessons this board teaches at its opening, for a first-timer. A board says what
        /// it wants taught and about which of its own widgets; <c>ScreenLessons</c> owns the
        /// order and the chaining (invariant 6a). Offered through <c>ScreenLessons.Offer</c>,
        /// so a lesson already seen is never queued. The default teaches nothing.
        /// </summary>
        public virtual void Lessons(List<ScreenLesson> into) { }

        /// <summary>
        /// The lessons a move has just made true, taught at the event rather than at the
        /// opening (invariant 6b: a ring goes round a thing that exists). Asked after
        /// <see cref="Animate"/> has landed and before the hill replays, so what is ringed is
        /// what the move just did. The default teaches nothing.
        /// </summary>
        public virtual void LessonsAfter(ChallengeMove move, List<ScreenLesson> into) { }

        /// <summary>Show the last move landing. The default is a plain repaint.</summary>
        public virtual IEnumerator Animate(ChallengeMove move)
        {
            Repaint();
            yield break;
        }

        /// <summary>Say a refused input, without changing anything.</summary>
        public virtual void Refuse()
        {
            if (Field) Tween.Shake(Field, Cell * .06f, .22f);
            Audio.Sfx("blocked", .5f);
        }
    }
}
