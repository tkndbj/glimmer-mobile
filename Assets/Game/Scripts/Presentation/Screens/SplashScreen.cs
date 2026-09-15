using System;
using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Cloud;
using GlimmerGrove.Content;
using GlimmerGrove.Layout;
using GlimmerGrove.Localization;
using GlimmerGrove.Notifications;
using GlimmerGrove.Progression;
using GlimmerGrove.Ads;
using GlimmerGrove.Store;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Launch screen: the cover, and a bar under the word.
    ///
    /// <para>
    /// The bar tracks genuine work — every sprite, clip and generated texture the game needs is
    /// pulled into memory here, so the first tap on PLAY never stutters.
    /// </para>
    ///
    /// <para>
    /// <b>It is the key art, moving, and that is the point rather than a shortcut.</b> This
    /// screen used to compose itself: a generated sky, generated stars, three painted layers
    /// bobbing on parallax, drifting mist, two companions asleep on an island and a wisp
    /// walking a spline to a cottage door as the load ran. Every part of it was real work and
    /// none of it was the picture the game is sold with. A launch screen is the one place a
    /// player meets the game before playing it, and the strongest thing to put there is the
    /// art it is sold with — the same frame that stands on the store page — rather than a
    /// second, necessarily weaker composition of the same world. What went with it is the whole
    /// apparatus: <c>Parallax</c>, <c>MistDrift</c>, the sleeping residents, the spline, the
    /// flare, and the three <c>splash_*</c> layers <c>Tools/make_splash_art.py</c> used to cut.
    /// </para>
    ///
    /// <para>
    /// <b>It is a still, and the moving version of it has been withdrawn.</b> The key art used to
    /// be laid over with a four-second clip of itself out of <c>StreamingAssets</c> — same frame,
    /// so the handover was invisible — and what that cost was a platform decoder, a texture the
    /// size of the display, a release path that had to be idempotent because it was reached two
    /// ways, and four megabytes in every build, all on the one screen guaranteed to be built at
    /// every launch and never returned to. The picture is the picture. Everything below is placed
    /// against it, which means the bar's arithmetic is checkable offline against a PNG rather
    /// than against a decoder.
    /// </para>
    ///
    /// <para>
    /// <b>What the player meets first is not this screen but the curtain over it.</b> The black
    /// plate this screen already used to cover its own settling frames now carries the publisher
    /// card — see <see cref="StudioIdent"/> — so the ident costs the launch nothing: the content
    /// loader runs underneath it, and the curtain lifts when the card is finished rather than on
    /// the first frame that holds still.
    /// </para>
    ///
    /// <para>
    /// <b>The wordmark is in the texture, so the bar's place is arithmetic.</b> There is no
    /// rect to measure and nothing at runtime that knows where the lettering ends — see
    /// <see cref="SplashCover"/>, which owns the fit and the clearance and is tested, because a
    /// number typed by eye against one phone is wrong on every other one and wrong invisibly.
    /// This screen asks it every frame rather than once: iOS reports its safe area a frame or
    /// two after a cold start, which is exactly the window this screen lives in.
    /// </para>
    ///
    /// <para>
    /// <b>Nothing the bar is made of waits on the load.</b> The trough, the fill, the sheen and
    /// the head are generated shapes, so the screen is complete in the frame it is built even
    /// though the picture itself is a delivered sprite — and if that sprite is somehow not
    /// there, the night sky behind it is generated too, which is the difference between a dark
    /// screen and a white one.
    /// </para>
    /// </summary>
    public sealed class SplashScreen : View
    {
        public override string Track => "mus_menu";

        /// <summary>
        /// How long the screen stands, and how fast the bar is allowed to fill.
        ///
        /// <para>
        /// The rate is a ceiling rather than a speed: <c>_shown</c> chases the real
        /// <c>_target</c> and can only ever be behind it, so a slow device fills the bar
        /// honestly while a warm Editor cannot finish it in a blink. Without the ceiling
        /// everything below happens in under a second and is then waited out, which is a
        /// loading screen that lies in the flattering direction.
        /// </para>
        /// <para>
        /// <b><see cref="MinimumShow"/> is counted from the moment the curtain lifts, not from
        /// the moment the screen is built</b>, and that is the whole of what the ident changed
        /// here. Counted from the build it would be spent behind the publisher card, so on a warm
        /// device the loading screen would appear and be gone inside half a second — a bar the
        /// player never sees fill, which is worse than no bar. It is shorter than it was for the
        /// same reason: the launch now carries two beats and only one of them used to exist.
        /// </para>
        /// </summary>
        const float MinimumShow = 1.7f;
        const float FillRate = .55f;
        const float FinaleHold = .55f;

        /// <summary>How long one pass of the sheen takes, and how wide it is drawn.</summary>
        const float SheenPeriod = 1.45f, SheenWidth = 150f;

        /// <summary>
        /// How long the picture takes to come up out of black.
        ///
        /// <para>
        /// <b>Because this screen is not entered, it <em>begins</em>.</b> Every other screen in
        /// the game arrives through the iris, which is what makes a change of place read as a
        /// change of place rather than as a jump cut; the launch screen is raised by
        /// <c>Flow.Go(instant: true)</c> from <c>Boot</c>, with nothing before it but the
        /// operating system's own black window, so without this it is a hard cut from black to
        /// a full-brightness illustration in one frame. Reported, correctly, as too sudden.
        /// </para>
        /// <para>
        /// It is a curtain lifted rather than the content faded, so it is black specifically —
        /// the colour that was already there — instead of "whatever happens to be behind the
        /// canvas". Half a second: long enough to read as a fade and short enough that it is
        /// finished well inside <see cref="MinimumShow"/>, so it costs the launch nothing.
        /// </para>
        /// </summary>
        const float FadeIn = .55f;

        /// <summary>
        /// Longest the curtain waits for the layout to settle before it lifts anyway.
        ///
        /// <para>
        /// The curtain is not only a fade, it is also the cover over the one or two frames in
        /// which the canvas has not finished telling anybody how big it is — a scale factor
        /// that arrives at the end of the first frame, an orientation some Android devices
        /// report as landscape before they lock to portrait. Lifting on the first frame shows
        /// that settling; lifting when nothing has moved since the last frame shows a picture
        /// that is already in its final place. This is the ceiling on that wait, because a
        /// device that keeps changing its mind must not leave a black screen up for ever.
        /// </para>
        /// </summary>
        const float CurtainHold = .50f;

        static readonly Color Trough = new Color(.02f, .05f, .09f, .72f);
        static readonly Color Rim = new Color(1f, .78f, .36f, .40f);
        static readonly Color FillLow = Pal.Hex("#FFA930");
        static readonly Color FillHigh = Pal.Hex("#FFE27A");

        /// <summary>
        /// The picture's own top row, and a shade of it for the sky band to darken into.
        ///
        /// <para>
        /// <c>#02388F</c> is measured off the frame rather than picked: the top row averages
        /// exactly that. It is seen on any canvas tall enough that the capped zoom leaves a band
        /// of sky above the picture — which, with this frame's wide wordmark, is every phone
        /// taller than about 19:9 — and behind the picture if the sprite is missing altogether.
        /// <b>Re-measure all three whenever the cover is re-cut</b>; a join that is nearly right
        /// is a seam, and a seam across the top of the launch screen is the one place there is
        /// nothing else to look at.
        /// </para>
        /// </summary>
        static readonly Color SkyJoin = Pal.Hex("#02388F");
        static readonly Color SkyMid = Pal.Hex("#022C72");
        static readonly Color SkyTop = Pal.Hex("#021F52");

        Image _cover, _mirror, _veil, _fill, _head, _sheen, _halo, _scrim;
        RectTransform _coverRT, _mirrorRT, _veilRT, _barRT, _fillRT, _headRT, _sheenRT, _haloRT, _scrimRT;

        Image _curtain;
        CanvasGroup _curtainGroup;
        StudioIdent _ident;

        float _shown, _target;
        float _fitW, _fitH, _fitInset = -1f;
        float _builtAt, _lastScale = -1f;
        float _revealedAt = -1f;
        bool _fitApplied, _fitSettled, _lifting, _identDone;
        bool _flared;

        protected override void Build()
        {
            BuildCover();
            BuildBar();
            Fit();
            BuildCurtain();
            StartCoroutine(Run());
        }

        // ----------------------------------------------------------------- cover
        /// <summary>
        /// The key art, full-bleed. Sized and placed by <see cref="Fit"/>, because the shape it
        /// has to cover is not known until the canvas has one.
        /// </summary>
        void BuildCover()
        {
            // Under everything: the picture's own sky, continued. On most phones it is entirely
            // covered and never seen; on the tallest ones it is the band above the picture that
            // the capped zoom leaves open (see SplashCover.WordMargin), and if the sprite is
            // missing altogether it is the whole screen — which is the difference between a
            // launch that looks dim for a moment and one that flashes white.
            UIKit.Img("Sky", Content, Art.Gradient(SkyJoin, SkyMid, SkyTop, 256), Color.white);

            // Claimed before it is fetched, so the synchronous load lands in something this
            // screen holds rather than in the global set — see AssetHold.Claim. Without it a
            // full-screen texture stays resident for the life of the process, for a screen
            // nobody sees twice.
            _art = AssetLibrary.Hold("splash");
            _art.Claim(AssetManifest.SplashBackdrop);

            // The publisher card's mark, on the same scope and claimed in the same breath. It is
            // claimed here rather than in StudioIdent because a scope has to own an address
            // *before* anything asks for it, and the curtain is built after this method — so a
            // claim made where the card is built would be a claim made too late, and the mark
            // would go into the global set and stay there for the life of the process.
            _art.Claim(AssetManifest.IdentWord);

            var sprite = AssetLibrary.Sprite(AssetManifest.SplashBackdrop);
            if (sprite == null) return;

            // The picture again, upside down, standing on its own top edge — so the sky above
            // the band's join is the picture's own sky continued, exactly, rather than a colour
            // chosen to look like it. Row nought meets row nought, so there is no seam to get
            // right. Only ever visible on a canvas the capped zoom left a band on; it sits off
            // the top of the screen on everything else, and is disabled there.
            _mirror = UIKit.Img("Mirror", Content, sprite, Color.white,
                                new Vector2(1080f, 1920f), new Vector2(.5f, .5f), Vector2.zero);
            _mirrorRT = (RectTransform)_mirror.transform;
            _mirrorRT.localScale = new Vector3(1f, -1f, 1f);

            // …and a wash over it that comes up from nothing at the join to solid sky at the
            // top, because a mirror is only sky for the first hundred units: above that it
            // starts handing back upside-down mace. The middle stop is high (.87 rather than
            // .5) on purpose — a straight ramp is still 20% transparent a third of the way up,
            // which is exactly where the ghost is.
            _veil = UIKit.Img("Veil", Content, Art.Gradient(Pal.A(SkyJoin, 0f), Pal.A(SkyJoin, .87f),
                                                            SkyJoin, 128),
                              Color.white, new Vector2(1080f, 0f), new Vector2(.5f, 1f), Vector2.zero);
            _veilRT = (RectTransform)_veil.transform;

            _cover = UIKit.Img("Cover", Content, sprite, Color.white,
                               new Vector2(1080f, 1920f), new Vector2(.5f, .5f), Vector2.zero);
            _coverRT = (RectTransform)_cover.transform;
        }

        /// <summary>
        /// The launch screen is the one screen in the game that is never returned to, so it is
        /// also the one whose art would otherwise sit in memory for the whole session. Both
        /// halves of it go here: the decoder, and the scope holding the picture.
        /// </summary>
        AssetHold _art;

        void OnDestroy() => _art?.Dispose();

        // ------------------------------------------------------------------- bar
        /// <summary>
        /// The loading bar: a trough with a warm rim, a lozenge of light that grows in it, a
        /// head that rides the light's edge and a sheen that sweeps the part already filled.
        ///
        /// <para>
        /// <b>The sheen and the head are what make a stalled bar readable.</b> Progress here is
        /// genuine, so it moves in steps and can sit still for a second on a cold device while
        /// a chapter body is read. A bar that only moves when the number does is
        /// indistinguishable from a bar that has died — so the two things that never stop are
        /// the ones a player reads as "still working", and they cost nothing because neither
        /// touches the load.
        /// </para>
        /// <para>
        /// The sheen lives inside a <see cref="RectMask2D"/> on the fill, so it is clipped to
        /// however much of the bar is lit and can never be seen running along the empty part —
        /// which is the version that reads as a barber's pole rather than as light.
        /// </para>
        /// </summary>
        void BuildBar()
        {
            float h = SplashCover.BarHeight;

            _barRT = UIKit.Box("Bar", Content, new Vector2(600f, h), new Vector2(.5f, .5f), Vector2.zero);

            // A soft darkness under the bar, before the warm halo over it.
            //
            // **The bar used to be able to rely on the picture behind it and can no longer.** It
            // stands at the foot of the canvas and the bottom eighth of this cover is deep
            // shadow, so on every phone this draws over something already black and is invisible.
            // It is not invisible on a canvas squarer than about 5:4 — a foldable opened, a
            // tablet in split view — where the crop needed to keep the wordmark on screen lifts
            // the picture far enough that the bar lands on lit rock and grass instead. Measured:
            // the bar sits at 0.96 of the picture's height on a phone and 0.68 on a 1:1 canvas.
            //
            // A scrim rather than moving the picture, because there is no placement that keeps
            // both: at 1:1 nearly half the frame is cropped and any fit that puts dark ground
            // under the bar has put the logo off the top. And a scrim is the version that stays
            // right when the art is next re-cut, which is exactly how this was found.
            _scrim = UIKit.Img("Scrim", _barRT, Art.Glow(128, 1.15f), new Color(0f, .02f, .06f, .58f),
                               new Vector2(980f, 210f), new Vector2(.5f, .5f), Vector2.zero);
            _scrimRT = (RectTransform)_scrim.transform;

            _halo = UIKit.Img("Halo", _barRT, Art.Glow(128, 2f), new Color(1f, .80f, .38f, .22f),
                              new Vector2(760f, 120f), new Vector2(.5f, .5f), Vector2.zero);
            _haloRT = (RectTransform)_halo.transform;
            Tween.Breathe(_haloRT, .06f, 3.4f);

            var track = UIKit.Img("Track", _barRT, Art.Round(Mathf.RoundToInt(h * .5f)), Trough);
            UIKit.StretchTo((RectTransform)track.transform, 0, 0, 0, 0);

            var rim = UIKit.Img("Rim", _barRT, Art.RoundOutline(Mathf.RoundToInt(h * .5f), 3f), Rim);
            UIKit.StretchTo((RectTransform)rim.transform, 0, 0, 0, 0);

            // The lit part. A node rather than an Image so the mask has something to be, and so
            // the lozenge inside it keeps its own rounded ends at every width.
            float inner = h - 8f;
            _fillRT = UIKit.Box("Fill", _barRT, new Vector2(0f, inner), new Vector2(0f, .5f),
                                new Vector2(4f, 0f));
            _fillRT.pivot = new Vector2(0f, .5f);
            _fillRT.gameObject.AddComponent<RectMask2D>();

            _fill = UIKit.Img("Lit", _fillRT, Art.Round(Mathf.RoundToInt(inner * .5f)), FillLow);
            UIKit.StretchTo((RectTransform)_fill.transform, 0, 0, 0, 0);

            _sheen = UIKit.Img("Sheen", _fillRT, Art.Glow(64, 1.35f), new Color(1f, 1f, 1f, .38f),
                               new Vector2(SheenWidth, inner * 2.4f), new Vector2(0f, .5f), Vector2.zero);
            _sheenRT = (RectTransform)_sheen.transform;

            // Outside the mask, so it stands proud of the trough's end rather than being cut
            // off by it — the head of the light, not part of the fill.
            _head = UIKit.Img("Head", _barRT, Art.Glow(64, 1.7f), Pal.A(Pal.Radiance, .85f),
                              new Vector2(56f, 56f), new Vector2(0f, .5f), Vector2.zero);
            _headRT = (RectTransform)_head.transform;
        }

        /// <summary>
        /// The black the app launches on, and the publisher card standing on it. Built last so it
        /// is over everything — the fade is of the whole screen arriving, bar included, not of
        /// the artwork alone.
        ///
        /// <para>
        /// <b>A <c>CanvasGroup</c> rather than the plate's own colour</b>, because the curtain is
        /// no longer one <c>Image</c>: the ident's rule and thirty-odd strokes hang off it, and
        /// fading the plate alone would leave TEKOWORLD standing over the key art while the black
        /// went out from under it.
        /// </para>
        /// </summary>
        void BuildCurtain()
        {
            _curtain = UIKit.Img("Curtain", Content, Art.Pixel, Color.black);
            _curtain.raycastTarget = false;
            _curtainGroup = _curtain.gameObject.AddComponent<CanvasGroup>();
            _curtainGroup.blocksRaycasts = false;
            _builtAt = Time.unscaledTime;

            // Boot.CanvasWidth rather than the rect, for the reason Fit is made of: this is the
            // frame the canvas was created in, so nothing under it can be measured yet.
            _ident = StudioIdent.Raise(_curtain.rectTransform, Boot.CanvasWidth,
                                       () => _identDone = true);
        }

        /// <summary>
        /// Lifts the curtain the first frame in which nothing has moved since the last one.
        ///
        /// <para>
        /// <b>The canvas's own scale is watched as well as the layout, and it is the half that
        /// matters.</b> Everything this screen positions is in canvas units, so a scale factor
        /// that arrives late does not change a single number here — it rescales what has
        /// already been drawn, which is the whole interface arriving oversized and settling.
        /// `Boot` now forces the scaler to apply before anything is built, so this should never
        /// fire; it is here because "should never" is not a thing to hand a launch screen, and
        /// because the same guard covers the Android devices that report landscape for a frame
        /// before locking to portrait.
        /// </para>
        /// </summary>
        void HoldCurtainUntilNothingMoves()
        {
            float scale = Flow.Canvas != null ? Flow.Canvas.rootCanvas.scaleFactor : 1f;
            bool steady = _fitSettled && Mathf.Approximately(scale, _lastScale);
            _lastScale = scale;

            // The card is the floor, and it is far longer than <see cref="CurtainHold"/> — so the
            // settling this method was written to hide is now over before anybody could see it,
            // and the timeout below has stopped being the thing that decides when the curtain
            // goes. It is kept because it still answers the question it was asked: a device that
            // never stops changing its mind must not hold black for ever.
            if (!_identDone) return;

            if (steady || Time.unscaledTime - _builtAt >= CurtainHold) LiftCurtain();
        }

        /// <summary>
        /// Starts the fade, once — see <see cref="CurtainHold"/> for when, and
        /// <see cref="StudioIdent"/> for what has to have finished first.
        ///
        /// <para>
        /// <b>The clock the loading screen is measured by starts here</b>, not when this screen
        /// was built: everything before this moment happened behind the card. See
        /// <see cref="MinimumShow"/>.
        /// </para>
        /// </summary>
        void LiftCurtain()
        {
            _lifting = true;
            _revealedAt = Time.unscaledTime;
            if (_curtainGroup == null) return;

            Tween.Run(FadeIn, Ease.InOutSine,
                      t => { if (_curtainGroup) _curtainGroup.alpha = 1f - t; }, _curtain)
                 .OnDone(() => { if (_curtain) _curtain.gameObject.SetActive(false); });
        }

        // ------------------------------------------------------------------- fit
        /// <summary>
        /// Puts the picture and the bar where <see cref="SplashCover"/> says, and re-applies
        /// itself whenever the canvas or the system's insets change.
        ///
        /// <para>
        /// Re-applied rather than measured once for <c>SafeAreaFitter</c>'s reason: iOS reports
        /// its safe area a frame or two after a cold start, and this screen's whole life is a
        /// couple of seconds beginning at that moment. Measured once, the bar would sit in the
        /// home indicator on exactly the launch somebody is watching. The check is three float
        /// comparisons a frame.
        /// </para>
        /// </summary>
        void Fit()
        {
            if (Content == null) return;

            // ------------------------------------------------------------------------------
            // **Not measured from the canvas, and that is the whole of this method.**
            //
            // Every rect under a `Canvas` is one frame behind: `CanvasScaler` applies its scale
            // factor on `Canvas.willRenderCanvases`, which runs after every `Update` in the
            // frame, and this screen is built inside the same frame the canvas is *created* in
            // (see `Boot.Run`). So the first thing `Content.rect` ever reports is not the
            // canvas the player is about to see — it is raw device pixels, or the rect's own
            // default — and a full-bleed picture fitted to it is laid out for the wrong shape
            // and then snaps to the right one a frame later. That snap is the launch reading as
            // a lurch: the picture arrives stretched sideways and settles. It is invisible on a
            // 1080-wide phone, where the wrong answer and the right one happen to coincide,
            // which is exactly why it survives a desk full of checks.
            //
            // There is nothing to measure. The scaler is width-matched (`Boot.BuildCanvas`), so
            // the canvas is *always* `Boot.CanvasWidth` across and its height is the display's
            // aspect times that width — a pure function of `Screen`, correct in the first frame
            // and in every frame after it. The same division converts the safe area, which
            // `SafeArea` would otherwise divide by a scale factor that has not been set yet. The
            // app is portrait-locked (`defaultScreenOrientation: 0`), so this answer does not
            // change during a launch, and the picture is placed once.
            //
            // `Boot.CanvasWidth` rather than `Boot.RefWidth`, and the two part on a tablet: the
            // canvas is widened on anything squarer than a phone (`Layout.CanvasFit`), so the
            // design width and the drawn width are no longer the same number. Reading the design
            // one here would lay the key art out for a 1080-wide canvas and then draw it on a
            // 1620-wide one — the launch arriving stretched and settling, which is the exact
            // failure the paragraph above exists to prevent, moved onto a different device.
            // ------------------------------------------------------------------------------
            if (Screen.width <= 0 || Screen.height <= 0) return;

            float canvasW = Boot.CanvasWidth;
            float units = canvasW / Screen.width;
            float w = canvasW, h = Screen.height * units;
            float inset = Mathf.Max(0f, Screen.safeArea.yMin) * units;

            if (Mathf.Approximately(w, _fitW) && Mathf.Approximately(h, _fitH)
                && Mathf.Approximately(inset, _fitInset))
            {
                // A whole frame in which nothing moved: the canvas has finished making up its
                // mind, and the picture is where it is going to stay.
                if (_fitApplied) _fitSettled = true;
                return;
            }

            _fitW = w; _fitH = h; _fitInset = inset;
            _fitApplied = true;
            _fitSettled = false;

            var plan = SplashCover.Fit(w, h, inset);
            if (plan.Height <= 0f) return;

            if (_coverRT != null)
            {
                _coverRT.sizeDelta = new Vector2(plan.Width, plan.Height);
                _coverRT.anchoredPosition = new Vector2(0f, plan.PictureY);
            }

            bool banded = plan.SkyHeight > .5f;

            if (_mirrorRT != null)
            {
                _mirror.enabled = banded;
                _mirrorRT.sizeDelta = new Vector2(plan.Width, plan.Height);
                _mirrorRT.anchoredPosition = new Vector2(0f, plan.PictureY + plan.Height);
            }

            if (_veilRT != null)
            {
                _veil.enabled = banded;
                _veilRT.sizeDelta = new Vector2(w, plan.SkyHeight);
                _veilRT.anchoredPosition = new Vector2(0f, -plan.SkyHeight * .5f);
            }

            _barRT.sizeDelta = new Vector2(plan.BarWidth, SplashCover.BarHeight);
            _barRT.anchoredPosition = new Vector2(plan.BarX, plan.BarY);
            _haloRT.sizeDelta = new Vector2(plan.BarWidth + 160f, 120f);
            _scrimRT.sizeDelta = new Vector2(plan.BarWidth + 380f, 210f);
        }

        // ------------------------------------------------------------- the light
        void Update()
        {
            Fit();

            if (!_lifting) HoldCurtainUntilNothingMoves();

            // The card's neon, driven from here because StudioIdent is not a behaviour — it is
            // built onto the curtain rather than owning a node of its own, so there is nothing
            // for Unity to send a message to. A no-op once the curtain has gone.
            _ident?.Tick();

            _shown = Mathf.MoveTowards(_shown, _target, Time.unscaledDeltaTime * FillRate);
            DrawBar();

            if (!_flared && _shown > .999f) { _flared = true; Flare(); }
        }

        /// <summary>
        /// Moves the fill to wherever the load has got to and keeps the two things that do not
        /// depend on it — the sheen and the head's breath — running.
        /// </summary>
        void DrawBar()
        {
            if (_fillRT == null) return;

            float inner = SplashCover.BarHeight - 8f;
            float span = _barRT.sizeDelta.x - 8f;

            // Never narrower than its own height while there is any progress at all: a lozenge
            // squashed below its rounded ends reads as a scratch rather than as light.
            float lit = _shown <= 0f ? 0f : Mathf.Max(inner, span * _shown);
            _fillRT.sizeDelta = new Vector2(lit, inner);

            // Only while the bar is still filling. A readout has one writer, and after the
            // flare that writer is the tween: assigning here as well would stamp the ramp back
            // over the white flash in the same frame it was raised.
            if (!_flared) _fill.color = Color.Lerp(FillLow, FillHigh, _shown);

            float breath = .80f + .20f * Mathf.Sin(Time.unscaledTime * 5.4f);
            _headRT.anchoredPosition = new Vector2(4f + lit, 0f);
            _headRT.sizeDelta = Vector2.one * (52f * breath);
            _head.color = Pal.A(Pal.Radiance, (_shown <= 0f ? 0f : .75f) * breath);

            // Swept in the fill's own space, so it is clipped to the lit part and starts off
            // its left edge rather than appearing out of nothing.
            float t = (Time.unscaledTime % SheenPeriod) / SheenPeriod;
            _sheenRT.anchoredPosition =
                new Vector2(Mathf.Lerp(-SheenWidth, lit, Ease.InOutSine(t)), 0f);
            _sheen.color = new Color(1f, 1f, 1f, .34f * Mathf.Sin(t * Mathf.PI));
        }

        /// <summary>
        /// The moment the bar fills: the light goes white for an instant and a ring leaves it.
        ///
        /// <para>
        /// <b>This is the only sound the launch screen makes.</b> The screen it replaced chimed
        /// once per stop, pitched up the scale — eight of them inside two seconds, which is not
        /// a melody, and it was the first thing anybody said about it. A launch screen is two
        /// and a half seconds long and the music is already playing; one arrival is all the
        /// punctuation it can carry.
        /// </para>
        /// </summary>
        void Flare()
        {
            var ring = UIKit.Img("Flare", _barRT, Art.Ring(128, 9f), Pal.A(Pal.Radiance, .85f),
                                 Vector2.one * 60f, new Vector2(.5f, .5f), Vector2.zero);
            var rt = (RectTransform)ring.transform;
            Tween.Run(.8f, Ease.OutCubic, t =>
            {
                if (!rt) return;
                rt.sizeDelta = new Vector2(Mathf.Lerp(60f, _barRT.sizeDelta.x * 1.35f, t),
                                           Mathf.Lerp(60f, 300f, t));
                ring.color = Pal.A(Pal.Radiance, .7f * (1f - t));
            }, ring).OnDone(() => { if (ring) Destroy(ring.gameObject); });

            if (_fill != null)
                Tween.Tint(_fill, Pal.Radiance, .12f)
                     .OnDone(() => { if (_fill) Tween.Tint(_fill, FillHigh, .38f); });

            Audio.Sfx("chime2", .42f, 1.02f);
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

            // All four of these are fire-and-forget for the same reason: nothing between
            // tapping the icon and playing a glade is allowed to wait on a network.
            CloudSaveService.BeginSync();

            // Prices. Deliberately fetched now rather than when somebody opens the shop:
            // asking the store for product metadata is a round trip that takes a second or
            // more on a cold cellular connection, and a shop whose cards are blank for that
            // second is a shop players back out of. It also picks up anything bought on a
            // previous launch and never credited, which is the recovery path for a purchase
            // interrupted by a crash — the one thing here worth starting early even if
            // nobody ever opens the tab. Started after the content has loaded, because the
            // list of products to ask about comes out of it.

            // The population's move counts, for the one line on the victory panel that
            // compares a player to everybody else. It is the most disposable request the
            // game makes — no sign-in, no writes, and an outcome nothing waits on — which
            // is why it is started here and never checked again.
            CloudSaveService.BeginStatsRefresh();

            // Whether this build is still one the deployment allows to be played. Fire and
            // forget like everything else on this list and for the same rule — nothing between
            // tapping the icon and playing may wait on a network — which costs nothing here,
            // because a device that has been walled before enforces it from the frame the hub
            // draws without asking anybody. Only the first launch on a stale build waits for
            // this, and the wall lands on the hub a moment later (see UpdateGate).
            CloudSaveService.BeginReleaseCheck();

            StoreService.BeginConnect();

            // Consent, then mediation, in that order and never the other one. This is the
            // only thing on the splash that can put a dialog in front of the player — the
            // CMP's form, and on iOS Apple's tracking prompt — and it is here rather than in
            // Boot because neither belongs before the first scene has loaded. Nothing waits
            // on it: the offer buttons light up when readiness arrives, which is what
            // RewardedAds.Changed is for. See RewardedAds.StartAsync for why the order is
            // owned there rather than written out at this call site.
            RewardedAds.BeginStart();

            // Against the reveal rather than against the build, so a launch is the card and then
            // a bar somebody can actually watch fill. A device still holding the curtain has
            // `_revealedAt` at its sentinel and waits here, which is the correct answer — there
            // is no honest way to finish a loading screen nobody has seen.
            while (_revealedAt < 0f || Time.unscaledTime - _revealedAt < MinimumShow
                   || _shown < .999f) yield return null;

            yield return new WaitForSecondsRealtime(FinaleHold);
            Flow.Go<HomeScreen>();
        }

        /// <summary>Shared chrome: buttons, icons, critters, sounds, the font.</summary>
        IEnumerator LoadGlobalAssets()
        {
            var progress = new Progress<float>(t => _target = .12f + .70f * t);
            var task = AssetLibrary.PreloadAsync(AssetManifest.GlobalAssets(), progress);

            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) Debug.LogException(task.Exception);

            // The worn companion, and only that one. The rest of the roster is loaded
            // by the screens that show it and dropped when they close, which is what
            // keeps launch costing the same whether there are five companions or a
            // hundred. Warmed here rather than lazily so the hub's first frame has it.
            Profile.WarmWornAvatar();
        }

        /// <summary>
        /// The art of whichever chapter the player will land in — usually the first,
        /// or wherever they left off. Every other chapter's art stays on disk.
        /// </summary>
        IEnumerator LoadOpeningChapter()
        {
            var catalog = GameContent.Catalog;

            // The catalog's own default rather than the classic mode: with every glade chapter
            // disabled the classic one has no levels at all, and both of these would answer
            // nothing — so the one chapter body the game reads at launch would be no chapter,
            // and the player would meet the map with its art still on disk.
            var opening = catalog.Index != null ? catalog.Index.DefaultMode : GameMode.Default;

            var target = LevelUnlock.NextToPlay(catalog.Index, opening);
            if (!target.IsValid) target = catalog.Index.FirstIn(opening);

            var chapterId = catalog.ChapterOf(target);
            if (!chapterId.IsValid) { _target = 1f; yield break; }

            // The one chapter body the game reads at launch, and it lands here on
            // purpose: the splash already has a progress readout, and it is the same
            // chapter whose art is about to be fetched anyway. Every other chapter's
            // grids stay on disk until the player walks into them.
            var bodyTask = catalog.ChapterAsync(chapterId);
            while (!bodyTask.IsCompleted) yield return null;

            if (bodyTask.IsFaulted) { Debug.LogException(bodyTask.Exception); _target = 1f; yield break; }
            if (bodyTask.Result == null) { _target = 1f; yield break; }

            var progress = new Progress<float>(t => _target = .90f + .10f * t);
            var task = AssetLibrary.EnsureChapterAsync(bodyTask.Result, progress);

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

            // The reward table reads through the same layered source, so a downloaded
            // pack can retune the curve exactly the way it can add a chapter.
            var rules = ProgressionRules.LoadAsync(ContentBootstrap.LocalSource);
            while (!rules.IsCompleted) yield return null;
            if (rules.IsFaulted) Debug.LogException(rules.Exception);

            // Reminders, bound here rather than in Boot because both halves of what they need
            // have only just arrived: the Android channel is named with a player-facing string,
            // so it waits on the loc table, and the slate the planner reads rides in
            // `progression.json`, so it waits on the rules above. Binding does not schedule
            // anything and does not ask the player anything — it registers the channel, reads
            // what the OS already allows and finds out whether this launch came from a tap.
            // The schedule itself is written when the app is backgrounded (`Boot.Pump`), which
            // is the only moment the state it has to be built from is final.
            Notify.Bind(new MobileNotificationScheduler(Loc.Get("ui.notify.channel"),
                                                        Loc.Get("ui.notify.channel_note")));

            _target = .12f;
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
