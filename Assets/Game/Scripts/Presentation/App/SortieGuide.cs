using System;
using GlimmerGrove.Analytics;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Sprocket, walking on at the foot of the first sortie's loadout to say one thing and walking
    /// off again when he is thanked (invariant 53h).
    ///
    /// <para>
    /// <b>He is a speaker at the edge of the screen, never a panel over it.</b> Nothing he draws
    /// dims, latches or covers the shelf: the figure catches no taps, the bubble catches only its
    /// own (so a tap on the paper cannot fall through to a cell behind it), and the screen raises
    /// its viewport by <see cref="Band"/> while he stands, so no cell is ever left under him. The
    /// player can buy, arrange, switch tabs or leave while he is talking, and the hardware key
    /// still means "back to the map".
    /// </para>
    /// <para>
    /// <b>Laid out by <see cref="GuideDock"/> and anchored to the bottom-right corner</b>, so a
    /// display that changes width while he stands - split view, a foldable - moves the screen
    /// under him and not him.
    /// </para>
    /// <para>
    /// <b>Every way it ends is one call.</b> <see cref="Dismissed"/> is raised exactly once, on
    /// the key, before he walks off - the screen raises its BATTLE key on that rather than on the
    /// walk finishing, so nothing waits on an animation. A screen torn down mid-walk takes him
    /// with it and nobody is waiting.
    /// </para>
    /// <para>
    /// <b>A missing reel leaves no white rectangle</b> (invariant 7b): the figure's image stays
    /// off and the bubble still speaks.
    /// </para>
    /// </summary>
    public sealed class SortieGuide : MonoBehaviour
    {
        /// <summary>How long he takes to walk on, and to walk off.</summary>
        const float WalkOn = .82f, WalkOff = .58f;

        /// <summary>Frames a second of each reel: the pack's own pace.</summary>
        const float WalkFps = 26f, IdleFps = 20f;

        /// <summary>A beat after he stops before he speaks, so the stop reads.</summary>
        const float Speak = .12f;

        /// <summary>How far past the corner he starts and ends, beyond his own width.</summary>
        const float Offstage = 160f;

        /// <summary>The sentence's size, and the least it may shrink to before the bubble grows.</summary>
        const int BodySize = 34, BodyFloor = 24;

        /// <summary>The most lines the bubble is drawn to hold at <see cref="BodySize"/>.</summary>
        const float BodyRoom = 132f;

        /// <summary>Near-black on white: <c>TipOverlay</c>'s ink on <c>TipOverlay</c>'s paper.</summary>
        static readonly Color Ink = new Color(.13f, .16f, .20f);

        /// <summary>Raised once, when the key is pressed.</summary>
        public Action Dismissed;

        /// <summary>How much of the foot the guide takes. See <see cref="GuideDock.Band"/>.</summary>
        public float Band { get; private set; }

        RectTransform _host, _figure, _bubble;
        Image _art;
        Btn _key;
        bool _leaving;

        float _aspect = GuideDock.ShippedAspect;

        /// <summary>Builds the guide off the right edge of <paramref name="host"/> and walks him on.</summary>
        public static SortieGuide Enter(RectTransform host)
        {
            var node = UIKit.Node("Guide", host);
            var guide = node.gameObject.AddComponent<SortieGuide>();
            guide.Build(node);
            return guide;
        }

        void Build(RectTransform host)
        {
            _host = host;

            var idle = AssetLibrary.Frames(AssetManifest.GuideIdle);
            if (idle != null && idle.Length > 0 && idle[0] != null && idle[0].rect.height > 0f)
                _aspect = idle[0].rect.width / idle[0].rect.height;

            float width = host.rect.width > 1f ? host.rect.width : Boot.CanvasWidth;

            // The sentence is measured before the paper exists, so the paper is cut to it -
            // `TipOverlay.BuildBubble`'s bargain, for its reason: a translation's length is not
            // this file's to guess.
            float bodyW = GuideDock.BodyWidth(width, _aspect);
            var body = UIKit.Titled("Body", host, Loc.Get("ui.guide.upgrade"), BodySize, Ink,
                                    TextAnchor.MiddleCenter, new Vector2(bodyW, 10f),
                                    new Vector2(.5f, .5f), Vector2.zero,
                                    outline: 0f, shadow: 0f, wrap: true);

            while (body.fontSize > BodyFloor && body.preferredHeight > BodyRoom) body.fontSize--;
            float bodyH = Mathf.Ceil(body.preferredHeight);

            var dock = GuideDock.For(width, _aspect, bodyH);
            Band = dock.Band;

            BuildBubble(dock, body, bodyH, width);
            BuildFigure(dock, width, idle);

            WalkOnStage(dock, width);
        }

        /// <summary>A position in <see cref="GuideDock"/>'s frame, anchored to the bottom-right corner.</summary>
        static Vector2 FromCorner(Vector2 centre, float width) => new Vector2(centre.x - width, centre.y);

        void BuildFigure(GuideDock dock, float width, Sprite[] idle)
        {
            _figure = UIKit.Box("Figure", _host, dock.FigureSize, new Vector2(1f, 0f),
                                FromCorner(dock.FigureCentre, width));

            // A soft shadow on the ground he stands on, so he is standing on the screen rather
            // than pasted over it. A child of the figure, so it walks with him.
            var shadow = UIKit.Img("Shadow", _figure, Art.Glow(64, 1.6f), new Color(0f, 0f, 0f, .38f),
                                   new Vector2(dock.FigureSize.x * .78f, 40f), new Vector2(.5f, 0f),
                                   new Vector2(0f, 8f));
            shadow.raycastTarget = false;

            var art = UIKit.Img("Art", _figure, null, Color.white);
            art.preserveAspect = true;
            art.raycastTarget = false;
            _art = art;

            if (idle == null || idle.Length == 0) art.enabled = false;
        }

        void BuildBubble(GuideDock dock, Text body, float bodyH, float width)
        {
            var bottomLeft = dock.BubbleCentre - dock.BubbleSize * .5f;

            // Scaled from the beak, so it grows out of him rather than out of its own middle.
            var pivot = new Vector2(
                Mathf.Clamp01((dock.BeakCentre.x - bottomLeft.x) / dock.BubbleSize.x),
                Mathf.Clamp01((dock.BeakCentre.y - bottomLeft.y) / dock.BubbleSize.y));

            var paper = UIKit.Img("Bubble", _host, Art.Round((int)GuideDock.Corner), Color.white,
                                  dock.BubbleSize, new Vector2(1f, 0f), Vector2.zero);
            _bubble = (RectTransform)paper.transform;
            _bubble.pivot = pivot;
            _bubble.anchoredPosition = FromCorner(bottomLeft + Vector2.Scale(dock.BubbleSize, pivot), width);

            // The paper takes its own taps, so one aimed at it cannot fall through to the cell
            // behind - and nothing else of the guide takes any.
            paper.raycastTarget = true;

            var edge = UIKit.Img("Edge", _bubble, Art.RoundOutline((int)GuideDock.Corner, 3f),
                                 new Color(.10f, .13f, .17f, .16f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            var beak = UIKit.Img("Beak", _bubble, Art.Crystal(48), Color.white,
                                 Vector2.one * GuideDock.Beak, new Vector2(0f, 0f),
                                 dock.BeakCentre - bottomLeft);
            beak.raycastTarget = false;
            beak.transform.SetAsFirstSibling();

            var brt = body.rectTransform;
            brt.SetParent(_bubble, false);
            brt.anchorMin = brt.anchorMax = new Vector2(.5f, 1f);
            brt.sizeDelta = new Vector2(dock.BubbleSize.x - GuideDock.BodyInset * 2f, bodyH);
            brt.anchoredPosition = new Vector2(0f, -GuideDock.BodyY(bodyH));

            _key = UIKit.TextButton("GotIt", _bubble, Skins.Affirm, Loc.Get("ui.common.got_it"), 36,
                                    new Vector2(GuideDock.ButtonW, GuideDock.ButtonH),
                                    new Vector2(.5f, 0f), new Vector2(0f, GuideDock.ButtonY), Thank);

            _bubble.localScale = Vector3.zero;
        }

        // ------------------------------------------------------------------ on and off
        void WalkOnStage(GuideDock dock, float width)
        {
            var home = _figure.anchoredPosition;
            _figure.anchoredPosition = home + new Vector2(dock.FigureSize.x + Offstage, 0f);

            Play(AssetManifest.GuideWalk, WalkFps);

            // Decelerating into his mark: a walk that stops dead reads as a sprite being moved.
            Tween.Move(_figure, home, WalkOn, Ease.OutSine).OnDone(() =>
            {
                if (!this || _leaving) return;

                Play(AssetManifest.GuideIdle, IdleFps);
                Tween.Punch(_figure, .07f, .32f);

                Tween.After(Speak, Say, this);
            });
        }

        void Say()
        {
            if (!this || _leaving || _bubble == null) return;

            Tween.Pop(_bubble, 0f, .44f);
            Audio.Sfx("tip", .5f);
            SortieAnalytics.Track(SortieAnalytics.GuideShown);
        }

        /// <summary>The key: thanked once, he goes, and the screen hears about it at once.</summary>
        void Thank()
        {
            if (_leaving) return;
            _leaving = true;

            _key.Interactable = false;

            var dismissed = Dismissed;
            Dismissed = null;
            dismissed?.Invoke();

            if (_bubble != null)
            {
                var paper = UIKit.Group(_bubble);
                paper.blocksRaycasts = false;
                Tween.Fade(paper, 0f, .2f);
                Tween.Scale(_bubble, .6f, .22f, Ease.InBack);
            }

            if (_figure == null) { Gone(); return; }

            // Turned to face the way out: the image only, so the shadow under him stays put.
            if (_art != null) _art.rectTransform.localScale = new Vector3(-1f, 1f, 1f);

            Play(AssetManifest.GuideWalk, WalkFps);
            Audio.Sfx("whoosh", .35f);

            var away = _figure.anchoredPosition + new Vector2(_figure.sizeDelta.x + Offstage, 0f);

            Tween.Move(_figure, away, WalkOff, Ease.InQuad).OnDone(Gone);
        }

        void Gone()
        {
            if (this) Destroy(gameObject);
        }

        void Play(string reel, float fps)
        {
            if (_art == null) return;

            var frames = AssetLibrary.Frames(reel);
            if (frames == null || frames.Length == 0)
            {
                Flipbook.Detach(_art);
                return;
            }

            _art.enabled = true;
            Flipbook.Attach(_art, frames, fps);
        }

        void OnDestroy()
        {
            Tween.KillAll(this);
            if (_figure) Tween.KillAll(_figure);
            if (_bubble) Tween.KillAll(_bubble);
        }
    }
}
