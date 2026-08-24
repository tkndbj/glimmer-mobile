using GlimmerGrove.Homestead;
using GlimmerGrove.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Something bought in the grove's shop arriving: it falls out of a lit room onto a patch
    /// of ground, lands hard, and is named.
    ///
    /// <para>
    /// <b>Why it is a drop and not a fade-in, and why the ground is a diamond.</b> The grove's
    /// own vocabulary is isometric tiles, and land arriving already means <em>ground rising
    /// with weight behind it</em> (see <c>GroveRise</c>). A piece bought in the same shop
    /// arriving as a soft cross-fade would be a different game's animation played inside this
    /// one. So it falls, it lands on a tile, the impact throws a ring and dust, and the two
    /// ceremonies read as one system — which is what makes the shop feel like part of the
    /// grove rather than a catalogue in front of it.
    /// </para>
    /// <para>
    /// <b>It is deliberately not <c>CompanionRevealOverlay</c>, and residents still use that
    /// one.</b> A creature deserves a portrait in a dark room; a fence does not, and 146 decor
    /// pieces going through a ten-second reveal would be unbearable long before the twentieth.
    /// What the two share is the rarity ladder (<c>Chroma</c>) and the principle that the
    /// spectacle scales — see <see cref="GroveUnveil"/> for the tiering and why a price is the
    /// right thing to scale it by.
    /// </para>
    /// <para>
    /// <b>Everything is built hidden and then revealed</b>, never built by the beats, which is
    /// what lets <see cref="Skip"/> be one pass of assignments instead of a second choreography
    /// that would drift out of agreement with the first. Every effect is generated art for
    /// <c>Art.Bloom</c>'s reason, with one exception the shop guarantees: the piece's own
    /// thumbnail, which is already resident because the grid the player just tapped is drawing
    /// it.
    /// </para>
    /// </summary>
    public sealed class GroveUnveilOverlay : ModalView
    {
        /// <summary>
        /// What arrived. Set by the caller before Build runs.
        ///
        /// A property rather than a field for <c>HomesteadBuyOverlay.Piece</c>'s reason:
        /// <see cref="HomesteadPiece"/> is not <c>[Serializable]</c>, so a public field of that
        /// type earns a warning about serialisation that will never happen.
        /// </summary>
        public HomesteadPiece Piece { get; set; }

        // ------------------------------------------------------------------ shape
        const float ArtBox = 400f;
        const float GroundW = 520f;
        const float ArtY = 30f, GroundY = -170f;
        const float FallFrom = 640f;
        const float FanSize = 1060f;

        /// <summary>How long the piece is in the air. The fall accelerates; see <see cref="Play"/>.</summary>
        const float FallAt = .10f, FallFor = .34f;

        /// <summary>Resting brightnesses, held as constants because <see cref="Skip"/> assigns them.</summary>
        const float VignetteAlpha = .66f, FanAlpha = .30f, Fan2Alpha = .22f, GlowAlpha = .42f;
        const float GroundAlpha = .30f, ShadowAlpha = .38f, PlateLineAlpha = .85f;

        const float PlateWidth = 560f;

        // ------------------------------------------------------------------ state
        int _tier;
        GroveUnveil.Fanfare _f;
        Chroma _c;

        Image _sky, _vignette, _fan, _fan2, _glow, _flash, _ground, _shadow, _seal;
        Image[] _aurora;
        RectTransform _fanRt, _fan2Rt, _artRt, _plate;
        Image _art;
        Text _name, _note;
        Image _line;

        bool _settled, _closing, _painted;

        protected override void Build()
        {
            _tier = GroveUnveil.TierOf(Piece);
            _f = GroveUnveil.FanfareOf(_tier);
            _c = Chroma.Of(_tier);

            BuildStage();
            BuildPiece();
            BuildPlate();

            // Both sources are asked to arrive, because which one answers depends on where
            // this was raised from — see PaintPiece. Neither is guaranteed to be in hand in the
            // frame this is built, and a ceremony that plays around an invisible object is the
            // one failure worse than no ceremony at all (invariant 7b).
            HomesteadArt.OpenShelfAsync(GroveShelves.Of(Piece), PaintPiece);
            HomesteadArt.Changed += PaintPiece;

            Play();
        }

        void OnDestroy() => HomesteadArt.Changed -= PaintPiece;

        // ------------------------------------------------------------------ stage
        /// <summary>
        /// The room: a coloured sky, slow masses of light behind it, a vignette, two crossing
        /// fans and a warm core. Built dark — every alpha here is zero until <see cref="Play"/>
        /// or <see cref="Skip"/> raises it.
        /// </summary>
        void BuildStage()
        {
            // Lit from below by the partner colour and falling to night above. A flat field of
            // one colour is only marginally better than a flat field of black; the gradient is
            // what makes it read as depth. One draw — see Art.Gradient.
            _sky = UIKit.Img("Sky", Content,
                             Art.Gradient(Color.Lerp(_c.Deep, _c.Partner, .28f),
                                          _c.Deep,
                                          Color.Lerp(_c.Deep, Color.black, .42f)),
                             new Color(1f, 1f, 1f, 0f));
            UIKit.StretchTo((RectTransform)_sky.transform, 0, 0, 0, 0);

            // It blocks from the first frame and answers from the plate beat, and those are two
            // separate needs rather than one.
            //
            // Blocking, because the home panel is still open underneath this — it deliberately
            // stays open so the player can see the rung they just climbed — and its buy button
            // is live and pointed at the next rung up. A ceremony that let taps through would
            // make an impatient double tap on a 6,000-credit upgrade buy the 13,000-credit one.
            //
            // Deaf, because the button that raised this is under the thumb that just pressed
            // it, and a ceremony a stray second tap deletes is one the player never sees.
            _sky.raycastTarget = true;
            _sky.gameObject.AddComponent<Btn>().Setup(OnTapped, silent: true);

            BuildAurora();

            _vignette = UIKit.Img("Vignette", Content, Art.Vignette(256),
                                  Pal.A(Color.Lerp(_c.Deep, Color.black, .55f), 0f));
            UIKit.StretchTo((RectTransform)_vignette.transform, 0, 0, 0, 0);

            _fan = UIKit.Img("Fan", Content, Art.Rays(512, _f.Rays), Pal.A(_c.Tint, 0f),
                             Vector2.one * FanSize, new Vector2(.5f, .5f), new Vector2(0f, ArtY));
            _fanRt = (RectTransform)_fan.transform;
            _fanRt.localScale = Vector3.zero;

            if (_f.HasSecondFan)
            {
                _fan2 = UIKit.Img("Fan2", Content, Art.Rays(256, 6 + _tier), Pal.A(_c.Partner, 0f),
                                  Vector2.one * (FanSize * .72f), new Vector2(.5f, .5f),
                                  new Vector2(0f, ArtY));
                _fan2Rt = (RectTransform)_fan2.transform;
                _fan2Rt.localScale = Vector3.zero;
            }

            _glow = UIKit.Img("Glow", Content, Art.Glow(256, 1.8f), Pal.A(_c.Tint, 0f),
                              Vector2.one * 820f, new Vector2(.5f, .5f), new Vector2(0f, ArtY));

            // The ground it lands on: the grove's own tile, which is the single cheapest thing
            // that says this belongs to the Grovement rather than to a generic shop.
            _ground = UIKit.Img("Ground", Content, Art.IsoTile(256, 2f), Pal.A(_c.Tint, 0f),
                                new Vector2(GroundW, GroundW * .5f), new Vector2(.5f, .5f),
                                new Vector2(0f, GroundY));
            _ground.transform.localScale = Vector3.zero;

            // Under the piece and above the ground. It is the whole depth cue for the fall —
            // wide and faint while the piece is high, tight and dark as it lands — and it is
            // what stops a sprite sliding down the screen from reading as a sprite sliding down
            // the screen.
            _shadow = UIKit.Img("Shadow", Content, Art.Glow(128, 2.6f), new Color(0f, 0f, 0f, 0f),
                                new Vector2(GroundW * .62f, GroundW * .30f), new Vector2(.5f, .5f),
                                new Vector2(0f, GroundY));

            _flash = UIKit.Img("Flash", Content, Art.Pixel, new Color(1f, 1f, 1f, 0f));
            UIKit.StretchTo((RectTransform)_flash.transform, 0, 0, 0, 0);
        }

        // Where the masses of light sit and how bright they rest. A composition rather than a
        // scatter: one high and left, one across the middle, one low, so the frame is lit
        // unevenly the way a place is. CompanionRevealOverlay's arrangement, at this room's
        // smaller scale.
        static readonly Vector2[] AuroraHome = { new Vector2(-340f, 480f), new Vector2(380f, 20f), new Vector2(-220f, -560f) };
        static readonly float[] AuroraSize = { 1040f, 880f, 1120f };
        static readonly float[] AuroraAlpha = { .20f, .16f, .13f };

        void BuildAurora()
        {
            if (_f.Aurora <= 0) return;

            _aurora = new Image[Mathf.Min(_f.Aurora, AuroraHome.Length)];

            for (int i = 0; i < _aurora.Length; i++)
            {
                _aurora[i] = UIKit.Img("Aurora" + i, Content, Art.Glow(128, 1.5f),
                                       Pal.A(_c.Nth(i + 1), 0f),
                                       Vector2.one * AuroraSize[i], new Vector2(.5f, .5f),
                                       AuroraHome[i]);
                Drift(i);
            }
        }

        /// <summary>
        /// One mass wandering forever, on its own object so <see cref="Skip"/>'s
        /// <c>KillAll</c> cannot stop the room moving — the trap the companion reveal records.
        /// </summary>
        void Drift(int i)
        {
            var rt = (RectTransform)_aurora[i].transform;
            var home = AuroraHome[i];
            var away = home + new Vector2(90f - i * 70f, 70f + i * 40f);

            Tween.Run(7f + i * 2.4f, Ease.InOutSine,
                      t => { if (rt) rt.anchoredPosition = Vector2.LerpUnclamped(home, away, t); },
                      _aurora[i], "drift").Loop(-1, true);
        }

        // ------------------------------------------------------------------ piece
        void BuildPiece()
        {
            _art = UIKit.Img("Piece", Content, null, Color.white,
                             Vector2.one * ArtBox, new Vector2(.5f, .5f),
                             new Vector2(0f, ArtY + FallFrom));
            _art.preserveAspect = true;
            _artRt = (RectTransform)_art.transform;

            // Every overlay that skips MakePanel has to name its own Panel, because Close()
            // scales it — leaving it null makes the exit a hard cut from a lit room back to the
            // shop, since a null tween finishes in a millisecond and dismisses the view with it.
            // CompanionRevealOverlay names its portrait disc; this names the piece, so what
            // shrinks on the way out is the thing that arrived.
            Panel = _artRt;

            PaintPiece();
        }

        /// <summary>
        /// Puts the piece on screen out of whichever art source actually has it.
        ///
        /// <para>
        /// <b>Two sources, because this ceremony is reachable from two screens holding
        /// different scopes.</b> From the shop it is a shelf atlas — thumbnails cut at 256,
        /// drawn here at 400, which is a hair of softness on flat vector art and is what the
        /// buy panel already accepts. From the <em>grove</em>, where tapping the hall opens the
        /// home panel, there is no shelf atlas at all and the full-size art is resident
        /// instead. Preferring the real thing gets a sharper picture where one is available and
        /// is the only reason a home bought from the grove is not unveiled as an empty room.
        /// </para>
        /// <para>
        /// Latched once something has landed, so art arriving mid-ceremony cannot swap the
        /// picture under the player — a resolution upgrade nobody asked for, played as a pop.
        /// </para>
        /// </summary>
        void PaintPiece()
        {
            if (!this || _art == null || _painted) return;

            if (HomesteadArt.HasArt(Piece)) HomesteadArt.Paint(_art, Piece);
            else HomesteadArt.PaintThumb(_art, Piece);

            // Both painters hide the image rather than leaving it white when they have nothing,
            // so its own opacity is the honest answer to whether this worked.
            _painted = _art.color.a > 0f;
        }

        // ------------------------------------------------------------------ plate
        void BuildPlate()
        {
            _plate = UIKit.Box("Plate", Safe, new Vector2(880f, 260f), new Vector2(.5f, .5f),
                               new Vector2(0f, GroundY - 190f));

            _name = UIKit.Shrinkable(
                UIKit.Titled("Name", _plate, Loc.Get(Piece.NameKey), 58, Pal.Cream,
                             TextAnchor.MiddleCenter, new Vector2(PlateWidth + 200f, 78f),
                             new Vector2(.5f, .5f), new Vector2(0f, 44f), 4f, 4f), 30);
            SetAlpha(_name, 0f);
            _name.transform.localScale = Vector3.zero;

            // SoftCapsule rather than a sharp bar, matching the companion reveal's rule: it is
            // a glow under a name, not a table border.
            _line = UIKit.Img("Line", _plate, Art.SoftCapsule(10, 120), Pal.A(_c.Tint, 0f),
                              new Vector2(0f, 10f), new Vector2(.5f, .5f), new Vector2(0f, -4f));

            // What a purchase actually bought. A piece is permission to draw it in as many
            // spots as you like and a home simply *is* your home now — two different sentences,
            // and printing the wrong one is how the shop's copy already misled players once.
            _note = UIKit.Shrinkable(
                UIKit.Titled("Note", _plate, Loc.Get(Piece.IsDwelling ? "ui.grove.home_moved"
                                                                     : "ui.grove.bought_note"),
                             28, new Color(1f, .96f, .88f, .0f), TextAnchor.MiddleCenter,
                             new Vector2(760f, 44f), new Vector2(.5f, .5f), new Vector2(0f, -62f), 3f, 0f), 19);

            if (!_f.HasSeal) return;

            _seal = UIKit.Img("Seal", _plate, Art.S("Ui/seal_gold"), Color.white,
                              new Vector2(132f, 132f), new Vector2(.5f, .5f),
                              new Vector2(PlateWidth * .5f - 24f, 34f));
            _seal.transform.localScale = Vector3.zero;
            _seal.transform.localRotation = Quaternion.Euler(0f, 0f, 14f);
        }

        // ------------------------------------------------------------------- play
        void Play()
        {
            var cue = new Cue(this);

            cue.With(() =>
            {
                Tween.Fade(_sky, 1f, .22f);
                Tween.Fade(_vignette, VignetteAlpha, .30f);

                if (_aurora != null)
                    for (int i = 0; i < _aurora.Length; i++)
                        Tween.Fade(_aurora[i], AuroraAlpha[i], .55f);

                Tween.Scale(_fanRt, 1f, .55f, Ease.OutCubic);
                Tween.Fade(_fan, FanAlpha, .45f);
                Spin();

                if (_fan2Rt)
                {
                    Tween.Scale(_fan2Rt, 1f, .62f, Ease.OutCubic);
                    Tween.Fade(_fan2, Fan2Alpha, .5f);
                    Counterspin();
                }

                Tween.Scale(_ground.transform, 1f, .40f, Ease.OutBack);
                Tween.Fade(_ground, GroundAlpha, .30f);
            });

            // The fall. Accelerating rather than eased out, because what has to be felt is
            // weight arriving — an OutCubic drop is a thing being placed, and a thing being
            // placed has no impact to celebrate.
            cue.Then(FallAt, () =>
            {
                Tween.Run(FallFor, Ease.InQuad, t =>
                {
                    if (!_artRt) return;
                    _artRt.anchoredPosition = new Vector2(0f, Mathf.Lerp(ArtY + FallFrom, ArtY, t));

                    // Tightening and darkening as the gap closes. Both, because either alone
                    // reads as a stain rather than as a shadow.
                    if (!_shadow) return;
                    _shadow.transform.localScale = Vector3.one * Mathf.Lerp(1.45f, .72f, t);
                    SetAlpha(_shadow, ShadowAlpha * t * t);
                }, this);
            });

            cue.Then(FallFor, Land);

            cue.Then(GroveUnveil.PlateAt - cue.Playhead, Plate);
            cue.Then(_f.Hold, () => Leave());
        }

        /// <summary>
        /// The landing: the loudest frame in the sequence.
        ///
        /// <para>
        /// <b>No haptic, deliberately.</b> It had one and it was wrong for the same reason the
        /// victory panel's payout lost its buzz: <c>Handheld.Vibrate</c> is one fixed-length
        /// buzz on Android with no way to make it lighter, and this fires on every purchase in
        /// a shop of 150 cells. A rumble that cannot be scaled down is a rumble that turns
        /// into noise the twentieth time. The flash, the shockwaves and the squash carry the
        /// impact on their own.
        /// </para>
        /// </summary>
        void Land()
        {
            if (_settled) return;

            Audio.Sfx("unlock", .78f, 1.06f - _tier * .02f);

            SetAlpha(_flash, _f.Flash);
            Tween.Fade(_flash, 0f, .34f);

            Tween.Fade(_glow, GlowAlpha, .18f)
                 .OnDone(() => { if (_glow) Tween.Fade(_glow, GlowAlpha * .62f, .5f); });

            // Squash, then over-recover. The whole reason the fall accelerates.
            if (_artRt)
            {
                _artRt.localScale = new Vector3(1.16f, .82f, 1f);
                Tween.Scale(_artRt, Vector3.one, .34f, Ease.OutBack);
            }

            if (_ground) Tween.Punch(_ground.transform, .22f, .40f);

            for (int i = 0; i < _f.Shockwaves; i++) Shockwave(i * .10f, _c.Nth(i), 2.1f + i * .5f);

            Burst.Sparks(Content, new Vector2(0f, GroundY + 30f), _c.Tint, _f.Sparks, 260f, 26f, .7f);

            // Dust leaving along the ground rather than out of it: a flat ring on the tile,
            // which is what an isometric floor does when something lands on it.
            Dust();
        }

        void Shockwave(float delay, Color colour, float to)
        {
            var ring = UIKit.Img("Wave", Content, Art.Ring(256, 10f), Pal.A(colour, 0f),
                                 Vector2.one * 300f, new Vector2(.5f, .5f), new Vector2(0f, ArtY));

            Tween.Run(.68f, Ease.OutQuint, t =>
            {
                if (!ring) return;
                ring.transform.localScale = Vector3.one * Mathf.Lerp(.2f, to, t);
                ring.color = Pal.A(colour, .8f * (1f - t));
            }, ring).Delay(delay)
                    .OnDone(() => { if (ring) Destroy(ring.gameObject); });
        }

        void Dust()
        {
            var ring = UIKit.Img("Dust", Content, Art.IsoTile(256, 2f), Pal.A(_c.Accent, .55f),
                                 new Vector2(GroundW, GroundW * .5f), new Vector2(.5f, .5f),
                                 new Vector2(0f, GroundY));

            Tween.Run(.62f, Ease.OutQuint, t =>
            {
                if (!ring) return;
                ring.transform.localScale = Vector3.one * Mathf.Lerp(.55f, 2.2f, t);
                ring.color = Pal.A(_c.Accent, .55f * (1f - t));
            }, ring).OnDone(() => { if (ring) Destroy(ring.gameObject); });
        }

        /// <summary>The name, the rule under it, and the point at which a tap means "enough".</summary>
        void Plate()
        {
            if (_settled) return;

            Audio.Sfx("pop", .5f);

            _name.transform.localScale = Vector3.zero;
            SetAlpha(_name, 1f);
            Tween.Pop(_name.transform, 0f, .42f);

            Tween.Run(.42f, Ease.OutCubic, t =>
            {
                if (!_line) return;
                _line.rectTransform.sizeDelta = new Vector2(PlateWidth * t, 10f);
                _line.color = Pal.A(_c.Tint, PlateLineAlpha * t);
            }, _line);

            Tween.Fade(_note, .80f, .40f);

            if (_seal)
            {
                Tween.Scale(_seal.transform, 1f, .42f, Ease.OutBack).Delay(.12f);
                Tween.RotateBy((RectTransform)_seal.transform, -14f, .42f, Ease.OutBack).Delay(.12f);
            }

            if (_f.HasConfetti) Burst.Confetti(Content, 44);

            // From here a tap ends it. Before here the sky blocks but does not answer — see
            // BuildStage, and note the gate is _settled itself rather than a second flag, so
            // there is no state in which one is true and the other is not.
            _settled = true;

            Bob();
        }

        // ------------------------------------------------------------------ loops
        // Each owned by the thing it moves and channelled, so Skip's KillAll cannot stop the
        // room and reaching the resting state twice replaces a loop rather than running two out
        // of step. The companion reveal's rule, and it was found the hard way there.
        void Spin()
        {
            if (!_fanRt) return;
            Tween.Run(26f, Ease.Linear,
                      t => { if (_fanRt) _fanRt.localRotation = Quaternion.Euler(0f, 0f, 360f * t); },
                      _fan, "spin").Loop(-1, false);
        }

        void Counterspin()
        {
            if (!_fan2Rt) return;
            Tween.Run(34f, Ease.Linear,
                      t => { if (_fan2Rt) _fan2Rt.localRotation = Quaternion.Euler(0f, 0f, -360f * t); },
                      _fan2, "spin").Loop(-1, false);
        }

        /// <summary>The piece breathing where it landed, so the held frame is not a still.</summary>
        void Bob()
        {
            if (!_artRt) return;
            Tween.Run(2.2f, Ease.InOutSine,
                      t => { if (_artRt) _artRt.anchoredPosition = new Vector2(0f, ArtY + 12f * t); },
                      _art, "bob").Loop(-1, true);
        }

        // ------------------------------------------------------------------- end
        /// <summary>
        /// Jumps to the finished picture and then leaves, which is what a tap and the back key
        /// both mean here.
        ///
        /// <para>
        /// One pass of assignments rather than a second choreography, which is only possible
        /// because everything was built hidden and revealed. A skip that had to <em>construct</em>
        /// the end state would be a second description of it, and the two would drift.
        /// </para>
        /// </summary>
        /// <summary>
        /// A tap on the room. Ignored until the piece has landed and been named — see
        /// <see cref="BuildStage"/> for why the sky blocks long before it answers.
        /// </summary>
        void OnTapped() { if (_settled) Skip(); }

        void Skip()
        {
            if (_closing) return;

            if (!_settled)
            {
                Tween.KillAll(this);

                if (_sky) _sky.color = Color.white;
                SetAlpha(_vignette, VignetteAlpha);
                SetAlpha(_flash, 0f);

                if (_aurora != null)
                    for (int i = 0; i < _aurora.Length; i++) SetAlpha(_aurora[i], AuroraAlpha[i]);

                if (_fanRt) { _fanRt.localScale = Vector3.one; SetAlpha(_fan, FanAlpha); Spin(); }
                if (_fan2Rt) { _fan2Rt.localScale = Vector3.one; SetAlpha(_fan2, Fan2Alpha); Counterspin(); }

                SetAlpha(_glow, GlowAlpha * .62f);

                if (_ground) { _ground.transform.localScale = Vector3.one; SetAlpha(_ground, GroundAlpha); }
                if (_shadow) { _shadow.transform.localScale = Vector3.one * .72f; SetAlpha(_shadow, ShadowAlpha); }

                if (_artRt)
                {
                    _artRt.anchoredPosition = new Vector2(0f, ArtY);
                    _artRt.localScale = Vector3.one;
                }

                if (_name) { SetAlpha(_name, 1f); _name.transform.localScale = Vector3.one; }
                if (_line) { _line.rectTransform.sizeDelta = new Vector2(PlateWidth, 10f); _line.color = Pal.A(_c.Tint, PlateLineAlpha); }
                if (_note) SetAlpha(_note, .80f);
                if (_seal) { _seal.transform.localScale = Vector3.one; _seal.transform.localRotation = Quaternion.identity; }

                _settled = true;
                Bob();
            }

            Leave();
        }

        /// <summary>
        /// Puts the room away. Quiet, because the shop is underneath and the fanfare has only
        /// just finished — a backing-out whoosh over the tail of it is one sound too many.
        /// </summary>
        void Leave()
        {
            if (_closing) return;
            _closing = true;

            Close(quiet: true);
        }

        public override bool OnBack() { Skip(); return true; }

        static void SetAlpha(Graphic g, float a)
        {
            if (g == null) return;
            var c = g.color; c.a = a; g.color = c;
        }
    }
}
