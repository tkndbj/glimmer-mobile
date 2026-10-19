using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Modes;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// <b>The wall</b> (<c>SiegeBoard.Rampart</c>): an energy barrier across the whole hill on the
    /// band the player tapped - a glass bar on the ground with a curtain of light standing off
    /// it - held while the model says it stands, and failing when its time runs out.
    ///
    /// <para>
    /// <b>Drawn off the model and never off the tap</b> (48l, the gravity well's rule): it rises
    /// on the first frame <see cref="SiegeBoard.WallLeft"/> reads above nought, flickers through
    /// its last <see cref="WallWarn"/> seconds, and goes on the frame it reads nought - so it
    /// cannot stand on a hill the model has stopped holding, or the reverse.
    /// </para>
    /// <para>
    /// <b>It stands among the raiders, not over them.</b> It is a child of the raiders' own layer
    /// and <see cref="Depth"/> sorts it by its march like a body: everything held behind it is
    /// drawn behind it, everything already past it in front.
    /// </para>
    /// <para>
    /// <b>See-through on purpose.</b> The bar is drawn at <see cref="WallAlpha"/> and the curtain
    /// is additive light, so the bodies pressed against it stay readable through it - a barrier
    /// that hid what it was holding would hide the thing the player bought it to see.
    /// </para>
    /// <para>
    /// <b>The bar is a bought piece</b> (CraftPix's <i>Merge Shooter Cartoon Asset Kit</i>, its
    /// gameplay-area wall, cut by <c>Tools/make_utility_art.py</c> into <c>Art/Fx/Wall</c>); the
    /// curtain is generated. It loads with every siege (<c>SiegeMode.ArtFor</c>), and an image
    /// whose sprite has not arrived is left switched off rather than drawn white (7b).
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        /// <summary>The curtain's height, as a share of one band of the aiming grid.</summary>
        const float WallTall = .62f;

        /// <summary>The cut bar's width over its height (<c>make_utility_art.WALL_W / WALL_H</c>).</summary>
        const float WallAspect = 512f / 44f;

        /// <summary>How opaque the bar is drawn: glass, not stone.</summary>
        const float WallAlpha = .82f;

        /// <summary>Seconds the barrier takes to rise, and to fade once it fails.</summary>
        const float WallRise = .28f, WallFall = .35f;

        /// <summary>Seconds before the end at which it starts to flicker, so the player is warned.</summary>
        const float WallWarn = 1f;

        /// <summary>The barrier's light: the bar's own cyan, lifted so it glows on a dark hill.</summary>
        static readonly Color WallHue = new Color(.42f, .90f, .95f);

        RectTransform _rampartNode;

        /// <summary>The bar and the curtain, kept so every frame can breathe and flicker them.</summary>
        Image _rampartBar, _rampartField;

        /// <summary>The march the standing wall was drawn at, which <see cref="Depth"/> sorts it by.</summary>
        float _rampartMarch;

        /// <summary>Whether the model's wall stood last frame: the edge it rises and falls on.</summary>
        bool _rampartWasUp;

        float _rampartAge;

        /// <summary>The board the wall was drawn for; a board dealt again takes it down unseen.</summary>
        SiegeBoard _rampartBoard;

        static Sprite WallPiece() => AssetLibrary.Sprite(AssetManifest.WallFx);

        /// <summary>
        /// Keeps the wall true to <see cref="SiegeBoard.WallLeft"/>. Called on every board
        /// (<c>SiegeView.Clock</c>); with no wall standing it is two comparisons.
        /// </summary>
        void Ramparts(float dt)
        {
            if (_board == null || _mobs == null) return;

            if (_rampartBoard != _board)
            {
                ScrapRampart();
                _rampartWasUp = false;
                _rampartBoard = _board;
            }

            float left = _board.WallLeft;
            bool up = left > 0f && _board.WallRow >= 0;

            if (up && !_rampartWasUp) RaiseRampart(_board.WallRow);
            if (!up && _rampartWasUp) DropRampart();
            _rampartWasUp = up;

            if (!up || !_rampartNode) return;

            _rampartAge += dt;

            // **The warning: a flicker that quickens toward the end**, the mantle's own
            // (`SiegeView.Mantle`). Off until the last `WallWarn` seconds.
            float flicker = 1f;
            if (left < WallWarn)
            {
                float k = 1f - left / WallWarn;
                float beat = Mathf.Abs(Mathf.Sin(_rampartAge * Mathf.Lerp(7f, 24f, k)));
                flicker = Mathf.Lerp(1f, .30f + .70f * beat, k);
            }

            float breathe = .85f + .15f * Mathf.Sin(_rampartAge * 3.1f);

            if (_rampartBar) _rampartBar.color = new Color(1f, 1f, 1f, WallAlpha * flicker);
            if (_rampartField) _rampartField.color = Pal.A(WallHue, .34f * breathe * flicker);
        }

        /// <summary>The barrier rising on band <paramref name="row"/>: a bar of light out of the ground.</summary>
        void RaiseRampart(int row)
        {
            ScrapRampart();

            var sprite = WallPiece();
            _rampartMarch = SiegeTuning.WallMarch(row);
            _rampartAge = 0f;

            float wide = Span.x * .98f;
            float bar = wide / WallAspect;
            float curtain = BoxTall * WallTall;
            float footY = MarchY(_rampartMarch);

            // Pivoted at its foot, so it grows up out of the ground and its foot is the wall's
            // line - the reading the rule holds bodies against.
            _rampartNode = UIKit.Node("Wall", _mobs);
            _rampartNode.anchorMin = _rampartNode.anchorMax = new Vector2(.5f, .5f);
            _rampartNode.pivot = new Vector2(.5f, 0f);
            _rampartNode.sizeDelta = new Vector2(wide, curtain);
            _rampartNode.anchoredPosition = new Vector2(0f, footY);

            // The curtain first, so the bar is drawn over its foot: light fading upward off the
            // bar, additive so it lifts the hill and the bodies behind it rather than veiling them.
            _rampartField = Lit("Field", _rampartNode, Art.FadeUp(64), Pal.A(WallHue, 0f),
                                new Vector2(wide * .97f, curtain));
            _rampartField.rectTransform.anchorMin = _rampartField.rectTransform.anchorMax = new Vector2(.5f, 0f);
            _rampartField.rectTransform.anchoredPosition = new Vector2(0f, curtain * .5f);

            // Anchored at the node's foot-centre: `UIKit.Img` reads an anchor of (0, 0) as "none
            // given" and draws at the middle, so the corner is never asked for.
            _rampartBar = UIKit.Img("Bar", _rampartNode, sprite, new Color(1f, 1f, 1f, WallAlpha),
                                    new Vector2(wide, bar), new Vector2(.5f, 0f),
                                    new Vector2(0f, bar * .5f));
            _rampartBar.raycastTarget = false;

            // An `Image` with no sprite is a white rectangle (7b): off until it has one.
            _rampartBar.enabled = sprite != null;

            var node = _rampartNode;
            node.localScale = new Vector3(1f, 0f, 1f);
            Tween.Run(WallRise, Ease.OutBack, t =>
            {
                if (node) node.localScale = new Vector3(1f, Mathf.Max(0f, t), 1f);
            }, node);

            // Light running along the bar and a ripple off it: something switched on.
            for (int i = 0; i < 5; i++)
            {
                var at = new Vector2(wide * (i / 4f - .5f), footY + bar * .5f);
                FxSparks(_fx, at, WallHue, 6, Cell * 1.4f, Cell * .18f, .45f);
            }

            Ripple(new Vector2(0f, footY), WallHue, wide * .9f, .45f, 0f);
            ShakeBoard(10f);

            Audio.Sfx("charge", .6f, 1.1f);
            Audio.Sfx("land", .5f, 1.2f);
        }

        /// <summary>The barrier's time running out: it sinks and fades, throwing sparks of its light.</summary>
        void DropRampart()
        {
            var node = _rampartNode;
            _rampartNode = null;
            _rampartBar = null;
            _rampartField = null;

            if (!node) return;

            float footY = node.anchoredPosition.y;
            float high = node.sizeDelta.y;
            float wide = node.sizeDelta.x;

            for (int i = 0; i < 9; i++)
            {
                var spot = new Vector2(Random.Range(-.48f, .48f) * wide, footY + Random.Range(.05f, .6f) * high);
                var drift = new Vector2(Random.Range(-.6f, .6f) * Cell, Cell * Random.Range(.4f, 1.2f));
                Cinder(spot, WallHue, Cell * Random.Range(.20f, .38f), .5f, drift);
            }

            var group = UIKit.Group(node);
            Tween.Run(WallFall, Ease.InQuad, t =>
            {
                if (!node) return;
                node.localScale = new Vector3(1f, 1f - t * .7f, 1f);
                if (group) group.alpha = 1f - t;
            }, node).OnDone(() => { if (node) Destroy(node.gameObject); });

            Audio.Sfx("shatter", .35f, 1.3f);
        }

        void ScrapRampart()
        {
            if (_rampartNode) Destroy(_rampartNode.gameObject);
            _rampartNode = null;
            _rampartBar = null;
            _rampartField = null;
        }

        /// <summary>
        /// Where the wall goes among the raiders' depth order: after every body further up the
        /// hill than its line, before every body already past it. -1 when no wall stands.
        /// </summary>
        float RampartDepth => _rampartNode ? _rampartMarch : -1f;
    }
}
