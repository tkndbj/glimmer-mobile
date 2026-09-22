using System.Collections;
using GlimmerGrove.Challenges;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Push: walls are dark stone, pads are rings in their colour, the gems are gems, and the
    /// keeper is a gold disc with the profile mark on it. A swipe anywhere on the board is one
    /// step, and the keeper and whatever it pushed glide rather than jump.
    /// </summary>
    public sealed class SokobanView : PuzzleView
    {
        protected override float MaxCell => 150f;

        SokobanPuzzle _sokoban;
        Image[] _gem;
        RectTransform _keeper;

        protected override void Build()
        {
            _sokoban = (SokobanPuzzle)Run.Puzzle;
            int n = Columns * Rows;

            for (int i = 0; i < n; i++)
            {
                if (_sokoban.IsWall(i))
                {
                    var wall = UIKit.Img("Wall", Field, Art.Round(14), new Color(.20f, .17f, .16f, 1f),
                                         Vector2.one * Cell * .98f);
                    wall.raycastTarget = false;
                    wall.type = Image.Type.Sliced;
                    wall.rectTransform.anchoredPosition = CentreOf(i);

                    var face = UIKit.Img("Face", wall.transform, Art.Round(12), new Color(.36f, .30f, .27f, 1f),
                                         Vector2.one * Cell * .82f);
                    face.raycastTarget = false;
                    face.type = Image.Type.Sliced;
                    continue;
                }

                var slot = UIKit.Img("Slot", Field, Art.Round(16), Pal.Slot, Vector2.one * Cell * .92f);
                slot.raycastTarget = false;
                slot.type = Image.Type.Sliced;
                slot.rectTransform.anchoredPosition = CentreOf(i);

                int pad = _sokoban.PadAt(i);
                if (pad >= 0)
                {
                    var ring = UIKit.Img("Pad", Field, Art.Ring(128, 12f), Pal.A(ChallengeArt.Tint(pad), .9f),
                                         Vector2.one * Cell * .72f);
                    ring.raycastTarget = false;
                    ring.rectTransform.anchoredPosition = CentreOf(i);
                    Tween.Breathe(ring.transform, .05f, 2.4f, i * .2f);
                }
            }

            _gem = new Image[n];
            for (int i = 0; i < n; i++)
            {
                _gem[i] = GemAt(i, 0, .74f);
                _gem[i].enabled = false;
            }

            _keeper = UIKit.Box("Keeper", Field, Vector2.one * Cell * .74f, new Vector2(.5f, .5f), CentreOf(_sokoban.Keeper));
            var disc = UIKit.Img("Disc", _keeper, Art.Disc(96), Pal.Gold, Vector2.one * Cell * .74f);
            disc.raycastTarget = false;
            var mark = UIKit.Img("Mark", _keeper, Art.S("Ui/ic_profile"), Pal.Ink, Vector2.one * Cell * .42f);
            mark.raycastTarget = false;
            mark.preserveAspect = true;
            mark.enabled = mark.sprite != null;

            Swipes(dir => Send(ChallengeInput.Swipe(dir.x, dir.y)));
        }

        public override void Repaint()
        {
            for (int i = 0; i < _gem.Length; i++)
            {
                int colour = _sokoban.GemAt(i);
                _gem[i].sprite = colour >= 0 ? ChallengeArt.Gem(colour) : null;
                _gem[i].enabled = colour >= 0 && _gem[i].sprite != null;
                _gem[i].rectTransform.anchoredPosition = CentreOf(i);
                _gem[i].transform.localScale = Vector3.one * (_sokoban.Seated(i) ? 1.08f : 1f);
            }

            _keeper.anchoredPosition = CentreOf(_sokoban.Keeper);
        }

        public override IEnumerator Animate(ChallengeMove move)
        {
            if (!move.Turn) { Repaint(); yield break; }

            const float step = .16f;

            if (_sokoban.LastFrom >= 0) _keeper.anchoredPosition = CentreOf(_sokoban.LastFrom);
            Tween.Move(_keeper, CentreOf(_sokoban.Keeper), step, Ease.OutQuad);

            int from = _sokoban.LastPushFrom, to = _sokoban.LastPushTo;
            if (from >= 0 && to >= 0)
            {
                // The gem's widget for its old cell slides to the new one, then the repaint
                // re-homes every widget.
                var rt = _gem[from].rectTransform;
                _gem[from].sprite = ChallengeArt.Gem(_sokoban.GemAt(to));
                _gem[from].enabled = _gem[from].sprite != null;
                Tween.Move(rt, CentreOf(to), step, Ease.OutQuad);
                Audio.SfxVaried("whoosh", .4f);
            }
            else
            {
                Audio.Sfx("tick", .25f);
            }

            yield return new WaitForSecondsRealtime(step);
            Repaint();

            if (from >= 0 && _sokoban.Seated(to))
            {
                Tween.Punch(_gem[to].transform, .2f, .24f);
                Burst.Sparks(Field, CentreOf(to), ChallengeArt.Tint(_sokoban.GemAt(to)), 10, Cell * 1.3f, Cell * .1f, .4f);
                Audio.Sfx("chime", .6f);
            }
        }
    }
}
