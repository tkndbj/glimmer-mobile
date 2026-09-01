using System;
using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Cloud;
using GlimmerGrove.Content;
using GlimmerGrove.Layout;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using GlimmerGrove.Ads;
using GlimmerGrove.Store;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

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
    /// <b>The still and the clip are the same frame</b>, so there is no handover to hide and no
    /// state to get right — see <see cref="BuildVideo"/>. Everything below is placed against the
    /// still, which means the bar's arithmetic is checkable offline against a PNG rather than
    /// against a decoder.
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
        /// </summary>
        const float MinimumShow = 2.5f;
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
        /// <c>#30294D</c> is measured off the frame rather than picked: the top row averages
        /// exactly that. It is only ever seen on a canvas so tall that the capped zoom leaves a
        /// band of sky above the picture — which, with this frame's narrower wordmark, is past
        /// anything a phone is — and behind the picture on a device that cannot decode at all.
        /// </para>
        /// </summary>
        static readonly Color SkyJoin = Pal.Hex("#30294D");
        static readonly Color SkyMid = Pal.Hex("#2A2444");
        static readonly Color SkyTop = Pal.Hex("#231E3A");

        Image _cover, _mirror, _veil, _fill, _head, _sheen, _halo;
        RectTransform _coverRT, _mirrorRT, _veilRT, _barRT, _fillRT, _headRT, _sheenRT, _haloRT;

        Image _curtain;
        VideoPlayer _video;
        RawImage _screen;
        RectTransform _screenRT;

        float _shown, _target;
        float _fitW, _fitH, _fitInset = -1f;
        float _builtAt, _lastScale = -1f;
        bool _fitApplied, _fitSettled, _lifting;
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

            // Claimed before it is fetched, so the synchronous load lands in this screen's own
            // scope rather than in the global cache — see AssetLibrary.Claim. Without it a
            // full-screen texture stays resident for the life of the process, for a screen
            // nobody sees twice.
            AssetLibrary.Claim(AssetLibrary.SplashScope, AssetManifest.SplashBackdrop);

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

            BuildVideo();
        }

        /// <summary>
        /// The moving version of the same picture, laid over the still one.
        ///
        /// <para>
        /// <b>The still is not a placeholder, it is the first frame.</b> So the screen is
        /// complete and correct before the decoder has done anything, the handover has nothing
        /// to blend because both sides are the same image, and a device that cannot play the
        /// clip at all simply keeps the picture — which is the only acceptable failure for a
        /// launch screen, because there is nothing here for a player to retry.
        /// </para>
        /// <para>
        /// It plays from <c>StreamingAssets</c> by URL rather than as a <c>VideoClip</c>
        /// through <c>AssetLibrary</c>: the asset pipeline addresses sprites, clips and fonts,
        /// and a fourth kind would be a change to the manifest, the audit and the loader for
        /// one file that must be resident before any of them have run. It is
        /// <see cref="VideoRenderMode.APIOnly"/>, so the player owns its own texture and this
        /// screen never allocates a <c>RenderTexture</c> the size of the display.
        /// </para>
        /// <para>
        /// It <b>loops</b>, and that is what lets the launch screen stay as short as it was.
        /// The clip is four seconds and the screen is gone in about three on a warm device;
        /// padding the wait out to fit the video would be a loading screen lying in the
        /// flattering direction (see <see cref="MinimumShow"/>). The camera is locked and there
        /// is no beat to miss, so a cut anywhere reads the same — and the iris covers it.
        /// </para>
        /// </summary>
        void BuildVideo()
        {
            _screenRT = UIKit.Box("Screen", Content, new Vector2(1080f, 1920f),
                                  new Vector2(.5f, .5f), Vector2.zero);
            _screen = _screenRT.gameObject.AddComponent<RawImage>();
            _screen.raycastTarget = false;
            _screen.enabled = false;                       // until there is a frame to show

            _video = _screen.gameObject.AddComponent<VideoPlayer>();
            _video.source = VideoSource.Url;
            _video.url = Application.streamingAssetsPath + "/" + AssetManifest.SplashVideoFile;
            _video.renderMode = VideoRenderMode.APIOnly;
            _video.audioOutputMode = VideoAudioOutputMode.None;
            _video.playOnAwake = false;
            _video.isLooping = true;
            _video.waitForFirstFrame = true;
            _video.skipOnDrop = true;

            _video.errorReceived += OnVideoError;
            _video.prepareCompleted += OnVideoPrepared;
            _video.Prepare();
        }

        void OnVideoPrepared(VideoPlayer player) => player.Play();

        // A failure is a line in the log and nothing else. The picture underneath is the frame
        // the clip would have opened on, so there is nothing to fall back to and nothing for a
        // player to retry.
        void OnVideoError(VideoPlayer player, string message)
        {
            Debug.LogWarning("[Splash] video unavailable: " + message);
            ReleaseVideo();
        }

        /// <summary>
        /// Gives back the decoder, its texture and this screen's picture.
        ///
        /// <para>
        /// <b>Everything here is native and none of it is collected.</b> A <c>VideoPlayer</c>
        /// holds a platform decoder and a texture it allocated itself; both are released when
        /// the component is destroyed, but only if it is not still running — a player left
        /// playing keeps the decoder alive through the teardown on some Android drivers, which
        /// is a hardware decoder and a few megabytes held for the rest of the session, on the
        /// one screen guaranteed to be built at every launch. So it is stopped first, its
        /// handlers dropped so nothing fires into a half-destroyed screen, and the
        /// <c>RawImage</c>'s reference to its texture cleared before the texture goes.
        /// </para>
        /// <para>
        /// Idempotent, because it is reached two ways — an error while preparing, and the
        /// screen being swapped out — and the second follows the first whenever both happen.
        /// </para>
        /// </summary>
        void ReleaseVideo()
        {
            if (_screen != null)
            {
                _screen.texture = null;
                _screen.enabled = false;
            }

            if (_video == null) return;

            _video.errorReceived -= OnVideoError;
            _video.prepareCompleted -= OnVideoPrepared;

            if (_video.isPlaying) _video.Stop();
            _video.targetTexture = null;

            Destroy(_video);
            _video = null;
        }

        /// <summary>
        /// The launch screen is the one screen in the game that is never returned to, so it is
        /// also the one whose art would otherwise sit in memory for the whole session. Both
        /// halves of it go here: the decoder, and the scope holding the picture.
        /// </summary>
        void OnDestroy()
        {
            ReleaseVideo();
            AssetLibrary.ReleaseScope(AssetLibrary.SplashScope);
        }

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
        /// The black the app launches on, lifted off the picture. Built last so it is over
        /// everything — the fade is of the whole screen arriving, bar included, not of the
        /// artwork alone.
        /// </summary>
        void BuildCurtain()
        {
            _curtain = UIKit.Img("Curtain", Content, Art.Pixel, Color.black);
            _curtain.raycastTarget = false;
            _builtAt = Time.unscaledTime;
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

            if (steady || Time.unscaledTime - _builtAt >= CurtainHold) LiftCurtain();
        }

        /// <summary>Starts the fade, once — see <see cref="CurtainHold"/> for when.</summary>
        void LiftCurtain()
        {
            _lifting = true;
            if (_curtain == null) return;

            Tween.Run(FadeIn, Ease.InOutSine,
                      t => { if (_curtain) _curtain.color = new Color(0f, 0f, 0f, 1f - t); }, _curtain)
                 .OnDone(() => { if (_curtain) _curtain.enabled = false; });
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

            // Exactly the picture's rect, because it is the picture — same frame, same aspect,
            // so one plan places both and they can never drift apart by a unit.
            if (_screenRT != null)
            {
                _screenRT.sizeDelta = new Vector2(plan.Width, plan.Height);
                _screenRT.anchoredPosition = new Vector2(0f, plan.PictureY);
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
        }

        // ------------------------------------------------------------- the light
        void Update()
        {
            Fit();

            if (!_lifting) HoldCurtainUntilNothingMoves();

            ShowVideoOnceItHasAFrame();

            _shown = Mathf.MoveTowards(_shown, _target, Time.unscaledDeltaTime * FillRate);
            DrawBar();

            if (!_flared && _shown > .999f) { _flared = true; Flare(); }
        }

        /// <summary>
        /// Reveals the video the frame it actually has something to draw, and not before.
        ///
        /// <para>
        /// <c>prepareCompleted</c> is not that moment — the player is ready but its texture can
        /// still be blank for a frame, and a blank one drawn over the poster is a black flash
        /// on the launch screen, which is the one thing the poster exists to prevent. Waiting
        /// for a frame to have gone by is a two-term test and costs nothing.
        /// </para>
        /// </summary>
        void ShowVideoOnceItHasAFrame()
        {
            if (_screen == null || _screen.enabled) return;
            if (_video == null || !_video.isPlaying || _video.frame <= 0) return;

            _screen.texture = _video.texture;
            _screen.enabled = _screen.texture != null;
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
            float started = Time.unscaledTime;

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

            StoreService.BeginConnect();

            // Consent, then mediation, in that order and never the other one. This is the
            // only thing on the splash that can put a dialog in front of the player — the
            // CMP's form, and on iOS Apple's tracking prompt — and it is here rather than in
            // Boot because neither belongs before the first scene has loaded. Nothing waits
            // on it: the offer buttons light up when readiness arrives, which is what
            // RewardedAds.Changed is for. See RewardedAds.StartAsync for why the order is
            // owned there rather than written out at this call site.
            RewardedAds.BeginStart();

            while (Time.unscaledTime - started < MinimumShow || _shown < .999f) yield return null;

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

            var target = LevelUnlock.NextToPlay(catalog.Index);
            if (!target.IsValid) target = catalog.First;

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
