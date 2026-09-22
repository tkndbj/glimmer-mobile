using System;
using System.Collections;
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
    /// <b>Laid out to the band, capped per cell</b>: a 4x4 board and a 6x10 well get the same
    /// band, so the cell is whichever of width, height and <see cref="MaxCell"/> binds first,
    /// and the grid is centred in what is left above the strip.
    /// </para>
    /// </summary>
    public abstract class PuzzleView : MonoBehaviour
    {
        protected const float Margin = 18f;

        protected ChallengeRun Run { get; private set; }
        protected RectTransform Host { get; private set; }
        protected RectTransform Field { get; private set; }
        protected RectTransform Strip { get; private set; }
        protected Image Plate { get; private set; }
        protected float Cell { get; private set; }

        Action<ChallengeInput> _send;

        /// <summary>Height of the key strip under the grid, or nought for none.</summary>
        public virtual float StripHeight => 0f;

        protected virtual float MaxCell => 200f;

        protected int Columns => Run.Puzzle.Width;
        protected int Rows => Run.Puzzle.Height;

        public void Attach(ChallengeRun run, RectTransform host, Action<ChallengeInput> send)
        {
            Run = run;
            Host = host;
            _send = send;

            float w = host.rect.width - Margin * 2f;
            float h = host.rect.height - StripHeight - Margin * 2f;

            Cell = Mathf.Floor(Mathf.Min(w / Columns, h / Rows, MaxCell));
            if (Cell < 8f) Cell = 8f;

            var span = new Vector2(Columns * Cell, Rows * Cell);
            var centre = new Vector2(0f, StripHeight * .5f);

            var plate = ChallengeArt.Plate();
            Plate = UIKit.Img("Plate", host, plate != null ? plate : Art.Round(34),
                              plate != null ? Color.white : Pal.Board,
                              span + Vector2.one * Cell * .34f + Vector2.one * Margin, new Vector2(.5f, .5f), centre);
            Plate.type = Image.Type.Sliced;
            Plate.raycastTarget = false;

            Field = UIKit.Box("Field", host, span, new Vector2(.5f, .5f), centre);

            if (StripHeight > 0f)
                Strip = UIKit.Box("Strip", host, new Vector2(host.rect.width, StripHeight),
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

        protected abstract void Build();

        /// <summary>Put what is drawn back in step with the puzzle, without animating anything.</summary>
        public abstract void Repaint();

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
