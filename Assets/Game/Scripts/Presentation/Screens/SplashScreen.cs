using System;
using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Launch screen. The bar tracks genuine work: every sprite, clip and generated
    /// texture the game needs is pulled into memory here, so the first tap on PLAY
    /// never stutters. The logo lights letter by letter as that work completes.
    /// </summary>
    public sealed class SplashScreen : View
    {
        public override string Track => "mus_menu";

        const float MinimumShow = 2.3f;

        readonly List<Text> _letters = new List<Text>();
        readonly List<Color> _letterTint = new List<Color>();
        Image _fill, _fillShine, _barGlow;
        Text _percent, _flavour;
        RectTransform _fillRT;
        float _shown, _target;
        int _litLetters;

        static readonly string[] Flavour =
        {
            "waking the fireflies",
            "polishing the heart-crystals",
            "teaching critters to snore",
            "untangling the conduits",
            "brewing the morning dew",
            "letting the light in",
        };

        protected override void Build()
        {
            BuildBackdrop();
            BuildLogo();
            BuildBar();
            StartCoroutine(Run());
        }

        void BuildBackdrop()
        {
            var far = Art.S("Bg/splash_far");
            if (far != null)
            {
                var img = UIKit.Img("Far", Content, far, Color.white);
                var rt = (RectTransform)img.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
                rt.anchoredPosition = Vector2.zero;
                var fit = img.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fit.aspectRatio = far.rect.width / far.rect.height;
            }
            var near = Art.S("Bg/grove_near");
            if (near != null)
            {
                var img = UIKit.Img("Near", Content, near, new Color(1f, 1f, 1f, .85f));
                var rt = (RectTransform)img.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
                rt.anchoredPosition = Vector2.zero;
                var fit = img.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fit.aspectRatio = near.rect.width / near.rect.height;
                rt.localScale = Vector3.one * 1.05f;
                Parallax.Attach(rt, 16f);
            }
            Fireflies.Spawn(Content, 30, new Color(1f, .94f, .70f), 6f, 24f);
            var vig = UIKit.Img("Vignette", Content, Art.Vignette(256), new Color(.01f, .04f, .06f, .7f));
            vig.type = Image.Type.Simple;
        }

        void BuildLogo()
        {
            var host = UIKit.Box("Logo", Content, new Vector2(1000f, 460f), new Vector2(.5f, .5f), new Vector2(0f, 250f));
            UIKit.Img("Bloom", host, Art.Glow(128, 1.6f), new Color(1f, .84f, .42f, .16f),
                      new Vector2(1100f, 620f), new Vector2(.5f, .5f), new Vector2(0f, 20f));

            Word(host, "GLIMMER", 148, Pal.Gold, new Vector2(0f, 66f));
            Word(host, "GROVE", 100, Pal.Cream, new Vector2(0f, -62f));

            UIKit.Titled("Sub", host, "a puzzle of light", 34, new Color(1f, .95f, .82f, .55f),
                         TextAnchor.MiddleCenter, new Vector2(900f, 50f), new Vector2(.5f, .5f),
                         new Vector2(0f, -150f), 3f, 3f);
        }

        void Word(Transform parent, string word, int size, Color colour, Vector2 pos)
        {
            var row = UIKit.Box("W_" + word, parent, new Vector2(10f, size * 1.4f), new Vector2(.5f, .5f), pos);
            var g = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.childAlignment = TextAnchor.MiddleCenter;
            g.childControlWidth = true; g.childControlHeight = true;
            g.childForceExpandWidth = false; g.childForceExpandHeight = false;
            g.spacing = size * .02f;
            var fit = row.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            foreach (var ch in word)
            {
                var t = UIKit.Titled("L", row, ch.ToString(), size, colour, TextAnchor.MiddleCenter,
                                     outline: 7f, shadow: 6f);
                var csf = t.gameObject.AddComponent<ContentSizeFitter>();
                csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                t.color = new Color(colour.r * .30f, colour.g * .32f, colour.b * .36f, .55f);  // asleep
                _letters.Add(t);
                _letterTint.Add(colour);
            }
        }

        void BuildBar()
        {
            var host = UIKit.Box("Bar", Content, new Vector2(720f, 200f), new Vector2(.5f, 0f), new Vector2(0f, 300f));

            _flavour = UIKit.Titled("Flavour", host, Flavour[0], 32, new Color(1f, .95f, .84f, .8f),
                                    TextAnchor.MiddleCenter, new Vector2(760f, 46f), new Vector2(.5f, .5f),
                                    new Vector2(0f, 66f), 3f, 3f);

            var track = UIKit.Img("Track", host, Art.Round(22), new Color(.03f, .07f, .10f, .82f),
                                  new Vector2(660f, 40f), new Vector2(.5f, .5f), Vector2.zero);
            var edge = UIKit.Img("Edge", track.transform, Art.RoundOutline(22, 3f), new Color(1, 1, 1, .18f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            _barGlow = UIKit.Img("Glow", track.transform, Art.Glow(128, 1.9f), new Color(1f, .8f, .35f, 0f),
                                 new Vector2(760f, 190f), new Vector2(.5f, .5f), Vector2.zero);
            _barGlow.transform.SetAsFirstSibling();

            // the fill grows from the left edge, so pivot and anchor both sit there
            _fill = UIKit.Img("Fill", track.transform, Art.Round(18), Pal.Ember, new Vector2(0f, 28f),
                              new Vector2(0f, .5f), new Vector2(6f, 0f));
            _fillRT = (RectTransform)_fill.transform;
            _fillRT.pivot = new Vector2(0f, .5f);

            _fillShine = UIKit.Img("Shine", _fill.transform, Art.Glow(64, 1.3f), new Color(1, 1, 1, .5f),
                                   new Vector2(70f, 26f), new Vector2(1f, .5f), new Vector2(-6f, 0f));

            _percent = UIKit.Titled("Pct", host, "0%", 28, new Color(1f, .96f, .86f, .9f),
                                    TextAnchor.MiddleCenter, new Vector2(200f, 40f), new Vector2(.5f, .5f),
                                    new Vector2(0f, -48f), 3f, 3f);

            UIKit.Titled("Credit", Content, "art & audio by CraftPix", 22, new Color(1, 1, 1, .3f),
                         TextAnchor.MiddleCenter, new Vector2(700f, 34f), new Vector2(.5f, 0f),
                         new Vector2(0f, 70f), 0f, 0f);
        }

        // ------------------------------------------------------------- the load
        /// <summary>
        /// Content first, because it names the chapters; then the shared chrome; then
        /// only the art of the chapter the player is actually about to see. Nothing
        /// here grows as the catalog does — the fiftieth chapter costs the same to
        /// launch as the first.
        /// </summary>
        IEnumerator Run()
        {
            float started = Time.unscaledTime;
            StartCoroutine(RotateFlavour());

            yield return LoadContent();                    // → .12

            yield return LoadGlobalAssets();               // → .82

            // generated shapes: real CPU work, spread over a few frames
            var shapes = Preload.Shapes();
            for (int i = 0; i < shapes.Count; i++)
            {
                shapes[i]();
                _target = .82f + .08f * ((i + 1) / (float)shapes.Count);
                if ((i & 1) == 0) yield return null;
            }

            yield return LoadOpeningChapter();             // → 1.0

            _target = 1f;
            ContentBootstrap.BeginBackgroundRefresh();

            while (Time.unscaledTime - started < MinimumShow || _shown < .999f) yield return null;

            Audio.Sfx("chime2", .5f, 1.05f);
            yield return new WaitForSecondsRealtime(.45f);
            Flow.Go<HomeScreen>();
        }

        /// <summary>Shared chrome: buttons, icons, critters, sounds, the font.</summary>
        IEnumerator LoadGlobalAssets()
        {
            var progress = new Progress<float>(t => _target = .12f + .70f * t);
            var task = AssetLibrary.PreloadAsync(AssetManifest.GlobalAssets(), progress);

            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) Debug.LogException(task.Exception);
        }

        /// <summary>
        /// The art of whichever chapter the player will land in — usually the first,
        /// or wherever they left off. Every other chapter's art stays on disk.
        /// </summary>
        IEnumerator LoadOpeningChapter()
        {
            var catalog = GameContent.Catalog;
            var target = LevelUnlock.NextToPlay(catalog) ?? catalog.First;
            var chapter = catalog.ChapterOf(target);

            if (chapter == null) { _target = 1f; yield break; }

            var progress = new Progress<float>(t => _target = .90f + .10f * t);
            var task = AssetLibrary.EnsureChapterAsync(chapter, catalog, progress);

            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) Debug.LogException(task.Exception);
        }

        /// <summary>
        /// Levels and strings, read from the on-device cache or the bundled files.
        ///
        /// Deliberately offline: the network is never on the path between tapping the
        /// icon and playing. Once the game is up, a background refresh pulls anything
        /// newer into the cache for the next launch.
        /// </summary>
        IEnumerator LoadContent()
        {
            var content = ContentBootstrap.LoadAsync();
            while (!content.IsCompleted) yield return null;
            _target = .06f;

            if (content.IsFaulted)
                Debug.LogException(content.Exception);
            else if (content.Result.Catalog.IsEmpty)
                Debug.LogError("[Boot] no levels available; check Assets/StreamingAssets/Content");

            var loc = Loc.LoadAsync(ContentBootstrap.LocalSource);
            while (!loc.IsCompleted) yield return null;
            if (loc.IsFaulted) Debug.LogException(loc.Exception);

            _target = .12f;
        }

        IEnumerator RotateFlavour()
        {
            int i = 0;
            while (true)
            {
                yield return new WaitForSecondsRealtime(.95f);
                if (_flavour == null) yield break;
                i = (i + 1) % Flavour.Length;
                var text = _flavour;
                Tween.Tint(text, Pal.A(text.color, 0f), .2f).OnDone(() =>
                {
                    if (!text) return;
                    text.text = Flavour[i];
                    Tween.Tint(text, new Color(1f, .95f, .84f, .8f), .25f);
                });
            }
        }

        void Update()
        {
            _shown = Mathf.MoveTowards(_shown, _target, Time.unscaledDeltaTime * 1.35f);
            if (_fillRT == null) return;

            float w = Mathf.Max(0f, 648f * _shown);
            _fillRT.sizeDelta = new Vector2(w, 28f);

            // the fill warms through the energy palette as it goes
            var col = _shown < .5f
                ? Color.Lerp(Pal.Ember, Pal.Sun, _shown * 2f)
                : Color.Lerp(Pal.Sun, Pal.Aqua, (_shown - .5f) * 2f);
            _fill.color = Pal.Lift(col, .18f);
            _barGlow.color = Pal.A(col, .10f + .30f * _shown);
            _fillShine.color = new Color(1, 1, 1, w > 40f ? .35f + .25f * Mathf.Sin(Time.unscaledTime * 6f) : 0f);
            _percent.text = Mathf.RoundToInt(_shown * 100f) + "%";

            int want = Mathf.FloorToInt(_shown * _letters.Count + .0001f);
            while (_litLetters < want && _litLetters < _letters.Count) LightLetter(_litLetters++);
        }

        void LightLetter(int i)
        {
            var t = _letters[i];
            if (t == null) return;
            Tween.Tint(t, _letterTint[i], .3f);
            Tween.Punch(t.transform, .3f, .42f);
            Audio.Sfx("tick", .3f, .85f + i * .055f);
            Burst.Sparks(t.transform, Vector2.zero, _letterTint[i], 6, 90f, 16f, .5f);
        }
    }

    /// <summary>
    /// The generated shapes the splash warms up.
    ///
    /// The list of *delivered* assets used to live here too, hardcoded down to the
    /// individual backdrop names — which meant every content drop needed someone to
    /// remember to edit this screen. That list now comes from
    /// <see cref="AssetManifest"/>, derived from the catalog.
    /// </summary>
    public static class Preload
    {
        /// <summary>Generated shapes, warmed so no frame pays for them mid-game.</summary>
        public static List<System.Action> Shapes()
        {
            return new List<System.Action>
            {
                () => Art.Round(6), () => Art.Round(18), () => Art.Round(22), () => Art.Round(24),
                () => Art.Round(28), () => Art.Round(30), () => Art.Round(40),
                () => Art.RoundOutline(22, 3f), () => Art.RoundOutline(22, 5f),
                () => Art.RoundOutline(28, 3f), () => Art.RoundOutline(30, 3f), () => Art.RoundOutline(40, 4f),
                () => Art.Disc(64), () => Art.Disc(96), () => Art.Disc(256),
                () => Art.Ring(128, 9f), () => Art.Glow(64, 1.2f), () => Art.Glow(64, 1.3f),
                () => Art.Glow(64, 1.7f), () => Art.Glow(96, 1.8f), () => Art.Glow(96, 1.9f),
                () => Art.Glow(128, 1.5f), () => Art.Glow(128, 1.6f), () => Art.Glow(128, 1.9f),
                () => Art.Glow(128, 2f), () => Art.Glow(128, 2.1f), () => Art.Glow(128, 2.4f),
                () => Art.Capsule(24, 96), () => Art.SoftCapsule(40, 120),
                () => Art.Spark(64), () => Art.Crystal(128), () => Art.Vignette(256),
                () => Art.FadeUp(64), () => { var _ = Art.Pixel; },
            };
        }
    }
}
