using System.Collections;
using GlimmerGrove.Challenges;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Pairs: sixteen face-down gems. The back is a socket with a glint on it, the face is the
    /// gem, and a matched pair stays up under a ring in its colour.
    ///
    /// <b>A miss is shown before it is taken back.</b> The rules hide a mismatched pair the
    /// instant it is judged; a player has to <em>see</em> the second gem to remember it, so
    /// <see cref="Animate"/> holds both faces up for a beat before the repaint hides them.
    /// </summary>
    public sealed class PairsView : PuzzleView
    {
        const float Beat = .62f;

        Image[] _back, _face, _ring;
        PairsPuzzle _pairs;

        protected override void Build()
        {
            _pairs = (PairsPuzzle)Run.Puzzle;
            int n = Columns * Rows;

            Sockets();
            _back = new Image[n];
            _face = new Image[n];
            _ring = new Image[n];

            for (int i = 0; i < n; i++)
            {
                _ring[i] = UIKit.Img("Ring", Field, Art.Ring(128, 9f), Pal.A(ChallengeArt.Tint(_pairs.ColourAt(i)), .9f),
                                     Vector2.one * Cell * .9f);
                _ring[i].raycastTarget = false;
                _ring[i].rectTransform.anchoredPosition = CentreOf(i);

                _face[i] = GemAt(i, _pairs.ColourAt(i));

                _back[i] = UIKit.Img("Back", Field, Art.Round(18), new Color(.12f, .22f, .34f, 1f),
                                     Vector2.one * Cell * .86f);
                _back[i].raycastTarget = false;
                _back[i].type = Image.Type.Sliced;
                _back[i].rectTransform.anchoredPosition = CentreOf(i);

                var glint = UIKit.Img("Glint", _back[i].transform, Art.Glint(96, 4), Pal.A(Pal.Cream, .35f),
                                      Vector2.one * Cell * .34f);
                glint.raycastTarget = false;
            }

            Targets(cell => Send(ChallengeInput.Tap(cell)));
        }

        public override void Repaint()
        {
            for (int i = 0; i < _back.Length; i++) Paint(i, _pairs.FaceAt(i));
        }

        void Paint(int i, PairsPuzzle.Face face)
        {
            _back[i].enabled = face == PairsPuzzle.Face.Hidden;
            _face[i].enabled = face != PairsPuzzle.Face.Hidden && _face[i].sprite != null;
            _face[i].color = face == PairsPuzzle.Face.Matched ? new Color(1f, 1f, 1f, .78f) : Color.white;
            _ring[i].enabled = face == PairsPuzzle.Face.Matched;
        }

        public override IEnumerator Animate(ChallengeMove move)
        {
            if (!move.Turn)
            {
                // The first flip: show it and pop it.
                Repaint();
                if (_pairs.First >= 0) Tween.Punch(_face[_pairs.First].transform, .18f, .22f);
                Audio.Sfx("gem", .5f);
                yield break;
            }

            int a = _pairs.LastA, b = _pairs.LastB;
            if (a >= 0 && b >= 0)
            {
                Paint(a, PairsPuzzle.Face.Up);
                Paint(b, PairsPuzzle.Face.Up);
                Tween.Punch(_face[b].transform, .18f, .22f);

                if (_pairs.LastMatched)
                {
                    Audio.Sfx("chime", .6f);
                    Burst.Sparks(Field, CentreOf(b), ChallengeArt.Tint(_pairs.ColourAt(b)), 10, Cell * 1.4f, Cell * .12f, .4f);
                    yield return new WaitForSecondsRealtime(.18f);
                }
                else
                {
                    Audio.Sfx("blocked", .4f);
                    yield return new WaitForSecondsRealtime(Beat);
                }
            }

            Repaint();
        }
    }
}
