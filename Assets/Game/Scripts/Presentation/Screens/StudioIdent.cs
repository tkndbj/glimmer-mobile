using System;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Layout;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The publisher card: TEKOWORLD cut out of a black sheet, with light moving behind it.
    ///
    /// <para>
    /// <b>It is the launch screen's curtain, not a screen of its own, and that is what makes it
    /// free.</b> <see cref="SplashScreen"/> already covers its own first frames with a black
    /// plate — the canvas takes a frame or two to settle and a hard cut into a full-brightness
    /// illustration reads as a jolt — and the load runs underneath that plate regardless. An
    /// ident raised as a separate screen would be a second or two of a black screen doing
    /// nothing, added to the front of every launch; painted onto the curtain that already exists,
    /// it is a second or two the content loader was going to spend anyway. The player sees a
    /// studio card; the device reads chapter bodies behind it.
    /// </para>
    ///
    /// <para>
    /// <b>The letters are a hole, not an object, and every other decision here follows from
    /// that.</b> The first cut drew white lettering and passed a coloured band over it, which is
    /// a different thing wearing the same description: over white, a saturated colour is a colour
    /// <em>mixed with white</em>, so the brightest the card could ever be was pastel — and the
    /// letters stayed equally legible whether the light was on them or not, which is precisely
    /// what stops it reading as light at all. Here the mark is a <see cref="Mask"/> with its
    /// graphic <b>not drawn</b>, so nothing is ever painted in the shape of the word. What is
    /// seen through it is whatever is moving behind, and the sheet either side is the curtain's
    /// own black.
    /// </para>
    /// <para>
    /// <b>Nothing spills.</b> There is no glow around the word and no halo behind it, because an
    /// opaque sheet does not leak — the cut before this had both, and a bloom around a word is
    /// what a lit *object* does. The only thing outside the cut-out is the rule.
    /// </para>
    /// <para>
    /// <b>The ambient is what keeps the word a word.</b> A light narrow enough to travel is a
    /// light that leaves most of the letters unlit, and unlit here means invisible rather than
    /// dim — so a low fill sits behind the whole cut-out and the sweep moves across it. Without
    /// it the card spells out three letters at a time.
    /// </para>
    ///
    /// <para>
    /// <b>A missing mark leaves black, never a white rectangle.</b> The sprite is claimed onto
    /// the launch screen's own scope and fetched synchronously in the frame this is built, which
    /// is the one place in this game where an unresolved address would be full-screen and
    /// first — so nothing is built at all when it comes back null (invariant 7b), and the card
    /// simply holds its beat on black.
    /// </para>
    /// </summary>
    public sealed class StudioIdent
    {
        /// <summary>
        /// The beats. The rule draws while the light is already coming up, because a card whose
        /// moves happen in sequence takes three times as long to say the same thing.
        /// </summary>
        const float RuleGrow = .62f, LightRise = .70f, LightLead = .08f;

        /// <summary>How long the finished card stands before the caller is told it may go.</summary>
        const float Hold = .70f;

        /// <summary>How long one pass of the light takes, end to end.</summary>
        const float SweepPeriod = 2.15f;

        /// <summary>
        /// How wide the travelling light is against the mark, and how far past each end it goes.
        ///
        /// <para>
        /// Wide on purpose: the whole spectrum is laid across the band (see
        /// <see cref="Art.Neon"/>), so a band this size puts four or five colours through the
        /// word at once. A narrow one is a single hue crossing it, which is a scanner rather than
        /// a light.
        /// </para>
        /// </summary>
        const float BandWidth = .95f, BandOverrun = .62f;

        /// <summary>
        /// The light that is always behind the cut-out.
        ///
        /// <para>
        /// Cool and dark rather than grey: a letter the sweep has left reads as the same light
        /// seen from further away, where a grey one reads as a letter somebody forgot to light.
        /// </para>
        /// <para>
        /// <b>It was <c>#123E63</c> and was too dark to read the word by.</b> The sweep is
        /// narrow enough to travel, so at any instant most of the mark is lit by this and
        /// nothing else — and against the curtain's black, a navy that dark spelt out three
        /// letters at a time with the rest merely implied. Raised until every letter reads at
        /// every position of the band (<c>render_splash.py --ident</c>, four values drawn side
        /// by side), and no further: past about <c>#3A82B4</c> the fill starts competing with
        /// the sweep, and a card where the light does not visibly *arrive* is a gradient.
        /// </para>
        /// </summary>
        static readonly Color Ambient = Pal.Hex("#2A6E9E");

        static readonly Color RuleInk = Pal.Hex("#2F6E8C");

        RectTransform _bandRT;
        float _bandTravel, _startedAt;
        Image _rule;
        RectTransform _ruleRT;
        float _ruleWidth;
        CanvasGroup _markGroup;

        /// <summary>
        /// Builds the card onto <paramref name="parent"/> and runs it, calling
        /// <paramref name="done"/> once it has stood its hold.
        ///
        /// <para>
        /// <paramref name="canvasWidth"/> is passed in rather than measured for
        /// <c>SplashScreen.Fit</c>'s reason: this is built inside the frame the canvas was
        /// created in, so every rect under it still reports device pixels. <c>Boot.CanvasWidth</c>
        /// is a pure function of <c>Screen</c> and is correct immediately.
        /// </para>
        /// </summary>
        public static StudioIdent Raise(RectTransform parent, float canvasWidth, Action done)
        {
            var ident = new StudioIdent();
            ident.Build(parent, canvasWidth);
            ident.Play(parent, done);
            return ident;
        }

        /// <summary>
        /// Moves the light. Driven by the screen, because this is not a behaviour.
        ///
        /// <para>
        /// It travels one way and wraps rather than bouncing. A light that runs back the way it
        /// came reads as a machine scanning something; one that always comes from the same side
        /// reads as a light passing, which is what the card is of.
        /// </para>
        /// </summary>
        public void Tick()
        {
            if (_bandRT == null) return;

            float t = ((Time.unscaledTime - _startedAt) % SweepPeriod) / SweepPeriod;
            _bandRT.anchoredPosition = new Vector2(Mathf.Lerp(-_bandTravel, _bandTravel, t), 0f);
        }

        void Build(RectTransform parent, float canvasWidth)
        {
            var sprite = AssetLibrary.Sprite(AssetManifest.IdentWord);
            if (sprite == null || sprite.rect.height <= 0f) return;

            var plan = IdentWordmark.Fit(canvasWidth, sprite.rect.width / sprite.rect.height);
            if (plan.Width <= 0f) return;

            _rule = UIKit.Img("Rule", parent, Art.Pixel, Pal.A(RuleInk, 0f),
                              new Vector2(0f, 2f), new Vector2(.5f, .5f),
                              new Vector2(0f, plan.RuleY));
            _ruleRT = (RectTransform)_rule.transform;
            _ruleWidth = plan.RuleWidth;

            // The sheet's cut-out. `showMaskGraphic` is false, so the mark itself is never
            // painted — it writes the stencil and nothing else, and everything under it is seen
            // only where a letter is.
            var markRT = UIKit.Box("Cutout", parent, new Vector2(plan.Width, plan.Height),
                                   new Vector2(.5f, .5f), Vector2.zero);

            var stencil = markRT.gameObject.AddComponent<Image>();
            stencil.sprite = sprite;
            stencil.raycastTarget = false;
            markRT.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            // **The fade is of the light, not of the sheet**, and that is not tidiness. A `Mask`
            // clips on its own graphic's alpha, so fading the cut-out would be fading the thing
            // deciding where the hole *is* — the stencil and the light would come up together,
            // through a threshold, on a card whose first frame is the first frame of the game.
            // The hole is cut once and stays cut; what is turned up is what is behind it.
            var lightRT = UIKit.Box("Light", markRT, Vector2.zero, new Vector2(.5f, .5f), Vector2.zero);
            UIKit.StretchTo(lightRT, 0, 0, 0, 0);
            _markGroup = lightRT.gameObject.AddComponent<CanvasGroup>();
            _markGroup.alpha = 0f;

            // Behind everything in the hole: the light that never moves. See Ambient.
            var fill = UIKit.Img("Ambient", lightRT, Art.Pixel, Ambient);
            UIKit.StretchTo((RectTransform)fill.transform, 0, 0, 0, 0);

            // …and the light that does. Taller than the letters so the band's own vertical
            // falloff is never what is seen at the top and bottom of a stroke: the cut-out
            // decides the shape, and the light should only ever decide the colour.
            float bandWidth = plan.Width * BandWidth;
            _bandTravel = plan.Width * .5f + bandWidth * BandOverrun;
            _bandRT = UIKit.Box("Sweep", lightRT, new Vector2(bandWidth, plan.Height * 2.2f),
                                new Vector2(.5f, .5f), new Vector2(-_bandTravel, 0f));
            var band = _bandRT.gameObject.AddComponent<Image>();
            band.sprite = Art.Neon();
            band.raycastTarget = false;
        }

        void Play(RectTransform owner, Action done)
        {
            _startedAt = Time.unscaledTime;

            if (_markGroup == null)
            {
                // Nothing was built — see the class note. Hold the beat anyway rather than
                // cutting straight to the loading screen, so a launch that lost one address is
                // a plain one rather than a flicker.
                Tween.After(RuleGrow + Hold, done, owner);
                return;
            }

            Tween.Run(RuleGrow, Ease.OutCubic, t =>
            {
                if (!_rule) return;
                _ruleRT.sizeDelta = new Vector2(_ruleWidth * t, 2f);
                _rule.color = Pal.A(RuleInk, t);
            }, owner);

            // The card comes on rather than arriving: the light behind the sheet is turned up,
            // which is the only entrance that makes sense for a hole. Nothing scales and nothing
            // slides — a cut-out that moves is a cut-out in something that moved.
            Tween.Run(LightRise, Ease.OutCubic, t => { if (_markGroup) _markGroup.alpha = t; }, owner)
                 .Delay(LightLead);

            Tween.After(Mathf.Max(RuleGrow, LightLead + LightRise) + Hold, done, owner);
        }
    }
}
