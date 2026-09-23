using System.Collections;
using GlimmerGrove.Challenges;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Pipeworks: each tile is a hub with an arm to every edge its solved shape has, drawn once
    /// and turned by rotating the node, so a tap is a quarter turn you can see happen. A source
    /// gem sits above its column, a sink ring in the turret's colour sits below, and a tile a
    /// colour runs through lights up in it.
    /// </summary>
    public sealed class PipesView : PuzzleView
    {
        /// <summary>
        /// Capped lower than a tapped gem, because the board asks for its sources and sinks as
        /// well as its rows and a six-wide board at the pairs' cell would take the hill down
        /// with it. <see cref="EdgeRows"/> is what the gem above and the ring below reach past
        /// the plate: each sits .72 of a cell off its edge row's centre, half a cell across.
        /// </summary>
        protected override float MaxCell => 140f;
        protected override float EdgeRows => .7f;

        PipesPuzzle _pipes;
        RectTransform[] _tile;
        Image[][] _arms;
        Image[] _hub;
        Image[] _sinkRing = new Image[ChallengeColours.Count];

        protected override void Build()
        {
            _pipes = (PipesPuzzle)Run.Puzzle;
            int n = Columns * Rows;

            Sockets();
            _tile = new RectTransform[n];
            _arms = new Image[n][];
            _hub = new Image[n];

            float thick = Cell * .30f;

            for (int i = 0; i < n; i++)
            {
                int arms = _pipes.ArmsAt(i);
                if (arms == 0) continue;

                var node = UIKit.Box("Tile", Field, Vector2.one * Cell, new Vector2(.5f, .5f), CentreOf(i));
                _tile[i] = node;
                _arms[i] = new Image[4];

                for (int d = 0; d < 4; d++)
                {
                    if ((arms & (1 << d)) == 0) continue;

                    // N E S W: an arm from the hub to that edge. Bit 0 is north, clockwise.
                    var arm = UIKit.Img("Arm", node, Art.Round(10), Pal.Dormant,
                                        new Vector2(thick, Cell * .5f + thick * .5f), new Vector2(.5f, .5f),
                                        new Vector2(0f, Cell * .25f));
                    arm.raycastTarget = false;
                    arm.type = Image.Type.Sliced;
                    arm.rectTransform.pivot = new Vector2(.5f, 0f);
                    arm.rectTransform.anchoredPosition = Vector2.zero;
                    arm.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -90f * d);
                    _arms[i][d] = arm;
                }

                _hub[i] = UIKit.Img("Hub", node, Art.Disc(64), Pal.Dormant, Vector2.one * thick * 1.25f);
                _hub[i].raycastTarget = false;

                node.localRotation = Quaternion.Euler(0f, 0f, -90f * _pipes.RotationAt(i));
            }

            // Sources over the top row, sinks under the bottom one.
            for (int c = 0; c < ChallengeColours.Count; c++)
            {
                int sx = _pipes.SourceColumn(c), kx = _pipes.SinkColumn(c);
                if (sx >= 0)
                {
                    var gem = UIKit.Img("Source", Field, ChallengeArt.Gem(c), Color.white, Vector2.one * Cell * .5f,
                                        new Vector2(.5f, .5f), CentreOf(sx) + new Vector2(0f, Cell * .72f));
                    gem.raycastTarget = false;
                    gem.preserveAspect = true;
                    gem.enabled = gem.sprite != null;
                }

                if (kx >= 0)
                {
                    var at = CentreOf((Rows - 1) * Columns + kx) - new Vector2(0f, Cell * .72f);
                    _sinkRing[c] = UIKit.Img("Sink", Field, Art.Ring(128, 12f), Pal.A(ChallengeArt.Tint(c), .55f),
                                             Vector2.one * Cell * .5f, new Vector2(.5f, .5f), at);
                    _sinkRing[c].raycastTarget = false;
                }
            }

            Targets(cell => Send(ChallengeInput.Tap(cell)));
        }

        public override void Repaint()
        {
            for (int i = 0; i < _tile.Length; i++)
            {
                if (_tile[i] == null) continue;

                _tile[i].localRotation = Quaternion.Euler(0f, 0f, -90f * _pipes.RotationAt(i));

                var ink = InkOf(_pipes.LitAt(i));
                _hub[i].color = ink;
                for (int d = 0; d < 4; d++) if (_arms[i][d] != null) _arms[i][d].color = ink;
            }

            for (int c = 0; c < _sinkRing.Length; c++)
                if (_sinkRing[c] != null)
                    _sinkRing[c].color = Pal.A(ChallengeArt.Tint(c), _pipes.IsJoined(c) ? 1f : .45f);
        }

        /// <summary>One colour lights a tile in that colour; two or more read as white light.</summary>
        static Color InkOf(int lit)
        {
            if (lit == 0) return Pal.Dormant;
            int count = 0, only = -1;
            for (int c = 0; c < ChallengeColours.Count; c++)
                if ((lit & (1 << c)) != 0) { count++; only = c; }
            return count == 1 ? Pal.Lift(ChallengeArt.Tint(only), .15f) : Pal.Radiance;
        }

        public override IEnumerator Animate(ChallengeMove move)
        {
            // Which tile turned is whichever rotation the drawing does not yet show.
            for (int i = 0; i < _tile.Length; i++)
            {
                if (_tile[i] == null) continue;
                float want = -90f * _pipes.RotationAt(i);
                float have = _tile[i].localRotation.eulerAngles.z;
                if (Mathf.Abs(Mathf.DeltaAngle(have, want)) < 1f) continue;

                Tween.RotateBy(_tile[i], -90f, .18f, Ease.OutCubic);
                Audio.SfxVaried("rotate_a", .5f);
                yield return new WaitForSecondsRealtime(.18f);
                break;
            }

            Repaint();

            if (move.Feeds.Count > 0) Audio.Sfx("lit", .35f);
        }
    }
}
