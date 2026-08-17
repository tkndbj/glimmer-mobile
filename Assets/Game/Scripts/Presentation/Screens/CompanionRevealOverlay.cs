using System;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The moment a companion joins the grove.
    ///
    /// <para>
    /// <b>Why this is a whole screen and not a line on the purchase panel.</b> The first
    /// version applied the unlock where the transaction happened: the price button became
    /// "WEAR", a few sparks fired, and the balance moved. Everything about that was correct
    /// and nothing about it was a reward — it is the same mistake the streak shipped and undid
    /// in save schema v10, and the event track in v11. A player has just spent somewhere
    /// between two and thirty days of income on one thing; the game has to react like it
    /// noticed. So the transaction panel closes and this takes over the screen.
    /// </para>
    /// <para>
    /// <b>It is a <see cref="Cue"/>, not a pile of delays.</b> The timing <em>is</em> the
    /// design here — the anticipation only works if the flash lands after the rings have
    /// collapsed and before the portrait arrives — and <c>Cue</c> exists because absolute
    /// delays had already drifted into a collision on the victory panel. Every beat below says
    /// only how long after the previous one it happens, so the sequence reads in the order the
    /// player sees it and inserting a beat cannot desynchronise the rest.
    /// </para>
    /// <para>
    /// <b>Everything is built up front and hidden, then revealed.</b> Beats change opacity and
    /// scale; none of them creates anything the sequence depends on later. That is what makes
    /// <see cref="Settle"/> possible — one method that puts every element in its final state —
    /// and skipping is then a single call rather than a second choreography that has to agree
    /// with the first.
    /// </para>
    /// <para>
    /// <b>It is skippable from the first frame.</b> This will be seen thirty times in the life
    /// of an account, and a celebration that cannot be dismissed stops being a celebration
    /// somewhere around the fourth. Tapping anywhere finishes it instantly.
    /// </para>
    /// <para>
    /// <b>No new art.</b> The fan, the shockwaves, the glow, the vignette and the stars are all
    /// procedural — <see cref="Art.Rays"/>, <see cref="Art.Ring"/>, <see cref="Art.Glow"/>,
    /// <see cref="Art.Vignette"/> — for the reason <c>Art.Bloom</c> is: a reveal that scales
    /// with the roster cannot depend on a sprite somebody has to draw per companion, and the
    /// spectacle has to be a function of the companion rather than a fixed animation.
    /// </para>
    /// <para>
    /// <b>It happens in a coloured room, and that is not decoration.</b> The first version staged
    /// everything against near-black, which is exactly what a screen looks like when its art has
    /// failed to load — so the most expensive thing a player can buy arrived looking like a
    /// loading error with a portrait on it. A single tint over black does not fix it either: one
    /// colour and nothing else is a monochrome image however bright the colour is. See
    /// <see cref="Chroma"/> for the three-colour scheme that replaced it.
    /// </para>
    /// </summary>
    public sealed class CompanionRevealOverlay : ModalView
    {
        /// <summary>
        /// Who is being revealed. A property rather than a field for the reason
        /// <c>CompanionUnlockOverlay.Avatar</c> is one — see there.
        /// </summary>
        public AvatarDefinition Avatar { get; set; }

        // ------------------------------------------------------------- geometry
        const float DiscSize = 380f;
        const float FaceSize = 292f;
        const float FanSize = 1180f;
        const float StarSize = 62f;
        const float StarGap = 74f;

        // Resting brightnesses of the stage. Named because Play fades up to them and Skip
        // assigns them directly, and the two agreeing is the whole reason Skip works — they
        // were four pairs of hand-repeated literals before.
        const float VignetteAlpha = .70f;
        const float FanAlpha = .34f;
        const float Fan2Alpha = .24f;
        const float GlowAlpha = .46f;

        Image _sky, _vignette, _fan, _fan2, _glow, _flash, _rim;
        Image _disc, _face;
        RectTransform _discRt, _fanRt, _fan2Rt;
        Text _name, _sub;
        Image[] _stars;
        Image[] _aurora;
        Image _underline;
        Btn _done;
        RectTransform _doneRt;

        /// <summary>
        /// The corner cross, held as its group rather than as its <see cref="Image"/>.
        ///
        /// An icon button's glyph is a <em>child</em> of the plate, so fading the plate's own
        /// Image leaves the cross drawn at full strength over the reveal — and still clickable,
        /// which put a live exit on screen through the whole sequence it was meant to appear
        /// after. One group fades both and gates the taps with it.
        /// </summary>
        CanvasGroup _dismiss;

        int _tier;
        Chroma _c;
        Color _tint;
        bool _settled;

        protected override void Build()
        {
            _tier = TierOf(Avatar);
            _c = ChromaOf(_tier);

            // The friend's own colour, cached because a dozen places below read it and
            // `_c.Tint` at every one of them is noise.
            _tint = _c.Tint;

            BuildStage();
            BuildFriend();
            BuildCaptions();
            BuildButtons();

            // Above everything, and last, so the impact whites out the whole composition.
            _flash = UIKit.Img("Flash", Content, Art.Pixel, new Color(1f, .99f, .94f, 0f));
            UIKit.StretchTo((RectTransform)_flash.transform, 0, 0, 0, 0);
            _flash.raycastTarget = false;

            Play();
        }

        // ---------------------------------------------------------------- tier
        /// <summary>
        /// How grand this reveal should be, 1 to 5, from the companion's own unlock level.
        ///
        /// <para>
        /// A <em>presentation</em> decision and deliberately not a content field. It buys
        /// nothing and gates nothing — it decides how many stars pop, how wide the fan opens
        /// and how much confetti falls — so making it authorable would be inventing a rarity
        /// system nobody asked for and another number per companion to keep in step with the
        /// price. Derived from the gate, it is automatically right for every companion a drop
        /// adds, including ones added after this file was last read.
        /// </para>
        /// <para>
        /// It exists because a reveal that is identical for an 800-coin friend and a
        /// 30,000-coin one tells the player their thirty days of play bought the same thing as
        /// their first afternoon. Escalation is most of what makes a collection worth
        /// finishing.
        /// </para>
        /// </summary>
        static int TierOf(AvatarDefinition avatar)
        {
            int gate = avatar.UnlockLevel;

            if (gate <= 10) return 1;
            if (gate <= 20) return 2;
            if (gate <= 32) return 3;
            if (gate <= 50) return 4;
            return 5;
        }

        // -------------------------------------------------------------- chroma
        /// <summary>
        /// A tier's whole colour scheme: the friend's own colour, two more that light the room
        /// around it, and the deep hue the room is built out of.
        ///
        /// <para>
        /// Three colours rather than one, because that is the difference between a light and a
        /// place. A tint over black gives a bright shape floating on nothing; a partner lighting
        /// the ground, an accent crossing it and a deep hue underneath give somewhere for the
        /// friend to arrive into — which is the entire job of this screen.
        /// </para>
        /// <para>
        /// Every colour is one already in <see cref="Pal"/>, so the loudest moment in the game
        /// cannot drift away from the game's own palette by inventing shades of its own, and
        /// retuning the palette retunes the reveal. Derived from the tier like everything else
        /// here, so a companion a content drop adds is dressed without a code change.
        /// </para>
        /// </summary>
        readonly struct Chroma
        {
            public readonly Color Tint, Partner, Accent, Deep;

            public Chroma(Color tint, Color partner, Color accent, Color deep)
            {
                Tint = tint; Partner = partner; Accent = accent; Deep = deep;
            }

            /// <summary>
            /// The three lights in order, wrapping — for anything spawning a run of them, so a
            /// row of rings or sparks cycles the scheme instead of repeating one colour.
            /// </summary>
            public Color Nth(int i)
            {
                switch (((i % 3) + 3) % 3)
                {
                    case 0: return Tint;
                    case 1: return Partner;
                    default: return Accent;
                }
            }
        }

        /// <summary>
        /// The tier's scheme: pale, green, blue, purple, gold.
        ///
        /// <para>
        /// The <see cref="Chroma.Tint"/> ladder is deliberately the rarity ladder every player
        /// already knows from every other game they have installed — common through to
        /// legendary — because this is the one piece of the reveal that has to be understood
        /// without being taught. The first version ran cream → mint → sun → gold → magenta,
        /// which put the game's own premium colour in fourth place and ended on a pink nobody
        /// reads as "the best one". Gold last is worth more than gold in the middle, and it
        /// agrees with what gold means everywhere else in this UI.
        /// </para>
        /// <para>
        /// The partner is always across the wheel from the tint and the accent always warm,
        /// because a scheme built from neighbours is the monochrome problem again wearing three
        /// names. The deep hue is the tint's own family driven down to about a tenth of its
        /// value — dark enough for cream text and a lit rim to read against, and still
        /// unmistakably a colour rather than the absence of one.
        /// </para>
        /// </summary>
        static Chroma ChromaOf(int tier)
        {
            switch (tier)
            {
                case 1: return new Chroma(Pal.Cream, Pal.Aqua, Pal.Sun, Pal.Hex("#0B2230"));
                case 2: return new Chroma(Pal.Mint, Pal.Aqua, Pal.Sun, Pal.Hex("#0A2A22"));
                case 3: return new Chroma(Pal.Azure, Pal.Bloom, Pal.Aqua, Pal.Hex("#111A46"));
                case 4: return new Chroma(Pal.Bloom, Pal.Azure, Pal.Sun, Pal.Hex("#2B0E3E"));
                default: return new Chroma(Pal.Gold, Pal.Ember, Pal.Bloom, Pal.Hex("#331409"));
            }
        }

        // --------------------------------------------------------------- stage
        /// <summary>
        /// The room the friend arrives into: a coloured sky, drifting light behind it, a
        /// vignette to pull the eye to the middle, two crossing fans of light and a warm core.
        /// </summary>
        void BuildStage()
        {
            // The sky is also the skip target. It is the bottom layer and covers the screen, so
            // a tap anywhere that is not the button lands here.
            //
            // Lit from below by the partner colour and falling to night above, because a flat
            // field of one colour is only marginally better than a flat field of black — a
            // gradient is what makes it read as depth rather than as a backing plate. One draw:
            // see Art.Gradient for why it is not three washes.
            _sky = UIKit.Img("Sky", Content,
                             Art.Gradient(Color.Lerp(_c.Deep, _c.Partner, .30f),
                                          _c.Deep,
                                          Color.Lerp(_c.Deep, Color.black, .40f)),
                             new Color(1f, 1f, 1f, 0f));
            UIKit.StretchTo((RectTransform)_sky.transform, 0, 0, 0, 0);
            _sky.raycastTarget = true;
            _sky.gameObject.AddComponent<Btn>().Setup(Skip);

            BuildAurora();

            // A quiet field of motes from the first frame, in the partner colour. The bright set
            // arrives with the friend (see Idle); this one says the room has air in it before
            // anything has happened, which is what stops the gathering reading as a dead screen.
            Fireflies.Spawn(Content, 14, Pal.A(_c.Partner, .6f), 4f, 13f);

            // Tinted with the room rather than with ink. A black vignette over a coloured sky
            // desaturates exactly the part of the frame the aurora is lighting, and the corners
            // end up the one grey thing on screen.
            _vignette = UIKit.Img("Vignette", Content, Art.Vignette(256),
                                  Pal.A(Color.Lerp(_c.Deep, Color.black, .55f), 0f));
            UIKit.StretchTo((RectTransform)_vignette.transform, 0, 0, 0, 0);
            _vignette.raycastTarget = false;

            // Ray count rises with the tier, so a rarer friend arrives through a busier fan.
            _fan = UIKit.Img("Fan", Content, Art.Rays(512, 10 + _tier * 4), Pal.A(_tint, 0f),
                             Vector2.one * FanSize, new Vector2(.5f, .5f), new Vector2(0f, 90f));
            _fanRt = (RectTransform)_fan.transform;
            _fanRt.localScale = Vector3.zero;
            _fan.raycastTarget = false;

            // A second fan turning the other way, in the partner colour — two colours of light
            // crossing, which is what a single fan can never be however fast it spins. It used
            // to be a top-two-tiers luxury and that was the wrong economy: the cost is one Image
            // and a 256² mask, and the tiers that needed the help most were the pale ones.
            //
            // Generated at 256 rather than 512 because it is behind everything at three quarters
            // the size; the wedge count still climbs with the tier so the two sets never beat
            // out a regular pattern against each other.
            _fan2 = UIKit.Img("Fan2", Content, Art.Rays(256, 6 + _tier), Pal.A(_c.Partner, 0f),
                              Vector2.one * (FanSize * .74f), new Vector2(.5f, .5f),
                              new Vector2(0f, 90f));
            _fan2Rt = (RectTransform)_fan2.transform;
            _fan2Rt.localScale = Vector3.zero;
            _fan2.raycastTarget = false;

            _glow = UIKit.Img("Glow", Content, Art.Glow(256, 1.7f), Pal.A(_tint, 0f),
                              Vector2.one * 900f, new Vector2(.5f, .5f), new Vector2(0f, 90f));
            _glow.raycastTarget = false;
        }

        // -------------------------------------------------------------- aurora
        // Where the three blobs sit, how big they are and how bright they rest. Static because
        // they are a composition rather than a random scatter: the point is one mass of colour
        // high and left, one across the middle, one low — so the frame is lit unevenly, the way
        // a place is, instead of evenly, the way a backdrop is.
        static readonly Vector2[] AuroraHome = { new Vector2(-360f, 560f), new Vector2(400f, 60f), new Vector2(-250f, -650f) };
        static readonly float[] AuroraSize = { 1180f, 980f, 1240f };
        static readonly float[] AuroraAlpha = { .20f, .16f, .13f };

        /// <summary>
        /// Slow masses of coloured light behind everything, one per colour of the scheme.
        ///
        /// <para>
        /// These do most of the work of making the room a room. They are cheap for what they
        /// buy — three soft blobs reusing the mask the core glow already generated — and they
        /// are deliberately huge and dim rather than small and bright: a small bright blob is a
        /// light with an edge, which reads as an object nobody put there.
        /// </para>
        /// </summary>
        void BuildAurora()
        {
            _aurora = new Image[3];

            for (int i = 0; i < 3; i++)
            {
                _aurora[i] = UIKit.Img("Aurora" + i, Content, Art.Glow(256, 1.7f),
                                       Pal.A(_c.Nth(i + 1), 0f), Vector2.one * AuroraSize[i],
                                       new Vector2(.5f, .5f), AuroraHome[i]);
                _aurora[i].raycastTarget = false;
                Drift(i);
            }
        }

        /// <summary>
        /// One blob's endless wander.
        ///
        /// <para>
        /// Both axes are whole multiples of the loop, which is not fussiness: a drift whose
        /// period does not divide the tween's would snap back every time the tween wrapped, and
        /// on something this slow and this large a snap is the most visible thing on screen.
        /// </para>
        /// <para>
        /// Owned by the blob and channelled, so it survives the <see cref="Skip"/> path's
        /// <c>KillAll</c> — a fast-forward should land on the resting state, not on a room whose
        /// lights have stopped moving.
        /// </para>
        /// </summary>
        void Drift(int i)
        {
            var img = _aurora[i];
            var rt = (RectTransform)img.transform;
            Vector2 home = AuroraHome[i];

            float ax = 90f + i * 34f, ay = 130f - i * 26f;
            float phase = i * 2.1f;

            Tween.Run(11f + i * 3.7f, Ease.Linear, t =>
            {
                if (!rt) return;

                float a = t * Mathf.PI * 2f;
                rt.anchoredPosition = home + new Vector2(Mathf.Sin(a + phase) * ax,
                                                         Mathf.Cos(a * 2f + phase) * ay);

                float s = 1f + .12f * Mathf.Sin(a + phase);
                rt.localScale = new Vector3(s, s, 1f);
            }, img, "drift").Loop(-1, false);
        }

        /// <summary>
        /// The blobs lit by the bang and easing back down. The room reacting to the impact is
        /// what stops the backdrop reading as a still image with an animation in front of it.
        /// </summary>
        void Flare()
        {
            if (_aurora == null) return;

            for (int i = 0; i < _aurora.Length; i++)
            {
                var blob = _aurora[i];
                float rest = AuroraAlpha[i];

                Tween.Fade(blob, Mathf.Min(1f, rest * 2.6f), .12f)
                     .OnDone(() => Tween.Fade(blob, rest, .85f));
            }
        }

        /// <summary>The portrait, on its plate, with the rim that will breathe once it lands.</summary>
        void BuildFriend()
        {
            // The plate is the room's own deep hue lifted towards the friend's colour, not a
            // fixed teal. A fixed one belonged to whichever scheme it was picked against and sat
            // in every other as a stray colour — a teal disc in a plum room is the one thing on
            // screen that looks like it came from a different screen. Lifted rather than equal,
            // because a plate the value of its backdrop is not a plate.
            _disc = UIKit.Img("Disc", Content, Art.Disc(300),
                              Pal.A(Color.Lerp(_c.Deep, Pal.Lift(_tint, .10f), .18f), .97f),
                              Vector2.one * DiscSize, new Vector2(.5f, .5f), new Vector2(0f, 90f));
            _discRt = (RectTransform)_disc.transform;
            _discRt.localScale = Vector3.zero;
            _disc.raycastTarget = false;

            // What ModalView.Close() scales out — the same hand-off ChestOverlay, WinOverlay
            // and TipOverlay make. Every overlay that skips MakePanel has to name its own
            // Panel, because Close() scales it unconditionally: leaving it null threw out of
            // the click handler *after* the content had been faded to nothing and *before*
            // Flow.Dismiss ran, stranding an invisible full-screen blocker over the game with
            // _closing already latched, so nothing could dismiss it. The friend is the right
            // choice here — the composition shrinks away around the portrait it was staged for.
            Panel = _discRt;

            _rim = UIKit.Img("Rim", _disc.transform, Art.Ring(300, 9f), Pal.A(_tint, .95f));
            UIKit.StretchTo((RectTransform)_rim.transform, 0, 0, 0, 0);
            _rim.raycastTarget = false;

            _face = UIKit.Img("Face", _disc.transform, null, Color.white,
                              Vector2.one * FaceSize, new Vector2(.5f, .5f), new Vector2(0f, 4f));
            _face.preserveAspect = true;
            _face.raycastTarget = false;

            // The roster's art lives in a scope and this can open over any screen, so the
            // portrait may not be resident yet — and an Image with no sprite is a white disc,
            // not a blank one. Invariant 7b; CompanionArt.Paint hides the frame until it has
            // something to draw and the callback repaints when it arrives.
            CompanionArt.Paint(_face, Avatar);
            CompanionArt.OpenAsync(() =>
            {
                if (!this || !_face) return;

                CompanionArt.Paint(_face, Avatar);

                // Re-darken if the silhouette beat has not run yet, because Paint resets the
                // colour to full white when it finds a sprite.
                if (!_settled && _revealing) _face.color = Silhouette;
            });
        }

        static readonly Color Silhouette = new Color(.06f, .10f, .13f, 1f);
        bool _revealing = true;

        void BuildCaptions()
        {
            _name = UIKit.Shrinkable(
                UIKit.Titled("Name", Content, Loc.Get(Avatar.NameKey), 96, Pal.Cream,
                             TextAnchor.MiddleCenter, new Vector2(900f, 130f), new Vector2(.5f, .5f),
                             new Vector2(0f, -210f), outline: 5f, shadow: 6f), 54);
            SetAlpha(_name, 0f);
            _name.transform.localScale = Vector3.zero;

            _underline = UIKit.Img("Rule", Content, Art.SoftCapsule(10, 120), Pal.A(_tint, 0f),
                                   new Vector2(0f, 10f), new Vector2(.5f, .5f), new Vector2(0f, -286f));
            _underline.raycastTarget = false;

            _sub = UIKit.Shrinkable(
                UIKit.Titled("Sub", Content, Loc.Get("ui.companion.joined"), 38,
                             Pal.A(Pal.Cream, .82f), TextAnchor.MiddleCenter,
                             new Vector2(860f, 56f), new Vector2(.5f, .5f), new Vector2(0f, -340f),
                             outline: 3f, shadow: 3f), 26);
            SetAlpha(_sub, 0f);

            _stars = new Image[_tier];
            float left = -(_tier - 1) * StarGap * .5f;

            for (int i = 0; i < _tier; i++)
            {
                _stars[i] = UIKit.Img("St" + i, Content, Art.S("Ui/ic_star"), Pal.A(Pal.Gold, .98f),
                                      Vector2.one * StarSize, new Vector2(.5f, .5f),
                                      new Vector2(left + i * StarGap, -128f));
                _stars[i].preserveAspect = true;
                _stars[i].raycastTarget = false;
                _stars[i].transform.localScale = Vector3.zero;
            }
        }

        void BuildButtons()
        {
            _done = UIKit.TextButton("Done", Content, "btn_green", Loc.Get("ui.companion.continue"), 46,
                                     new Vector2(620f, 150f), new Vector2(.5f, .5f),
                                     new Vector2(0f, -470f), () => Close());
            _doneRt = (RectTransform)_done.transform;
            _doneRt.localScale = Vector3.zero;

            // Appears with the button rather than at the start. Before the payoff there is
            // nothing to dismiss and a cross would only invite skipping past the reward;
            // tapping the veil already skips, which is the affordance that matters early.
            var cross = UIKit.IconButton("Dismiss", Content, Skins.Nav, "ic_close",
                                         new Vector2(84f, 84f), new Vector2(1f, 1f),
                                         new Vector2(-72f, -96f), () => Close());

            _dismiss = UIKit.Group((RectTransform)cross.transform);
            _dismiss.alpha = 0f;
            _dismiss.blocksRaycasts = false;
        }

        // ---------------------------------------------------------------- beats
        /// <summary>
        /// The sequence, in the order it is seen. Gaps, never absolute times.
        ///
        /// Read as three movements: the room darkens and something gathers, it breaks, and the
        /// friend is standing there. The pause before the flash is the whole trick — take it
        /// out and the reveal becomes an announcement.
        /// </summary>
        void Play()
        {
            var cue = new Cue(this);

            // -- gathering ---------------------------------------------------
            cue.With(() =>
            {
                Tween.Fade(_sky, 1f, .30f);
                Tween.Fade(_vignette, VignetteAlpha, .40f);

                // The room lights before the rings do, slower than everything else in this beat,
                // so the colour is established as the place rather than as part of the effect.
                if (_aurora != null)
                    for (int i = 0; i < _aurora.Length; i++)
                        Tween.Fade(_aurora[i], AuroraAlpha[i], .70f);

                Audio.Sfx("whoosh", .55f, .78f);
            });

            // Rings collapsing inward. Anticipation is the only thing on screen for half a
            // second, which is what makes the flash feel earned rather than sudden.
            cue.Then(.10f, () => Collapse(0));
            cue.Then(.13f, () => Collapse(1));
            cue.Then(.13f, () => Collapse(2));

            // -- the break ---------------------------------------------------
            cue.Then(.30f, () =>
            {
                _flash.color = new Color(1f, .99f, .94f, .92f);
                Tween.Fade(_flash, 0f, .38f, Ease.OutQuad);

                // The colour the white leaves behind, outliving it by a quarter of a second, so
                // the impact resolves into the friend's own hue instead of back into the room.
                ChromaPulse();

                Audio.Sfx("unlock", .8f);
                Audio.Sfx("shatter", .32f, 1.25f);
                Haptic.Tap();

                Tween.Shake((RectTransform)Content, 26f, .42f);

                // The fans arrive with the bang and then never stop turning. A still sunburst
                // reads as a decal; a turning one reads as light.
                _fanRt.localScale = Vector3.one * .35f;
                Tween.Scale(_fanRt, 1f, .70f, Ease.OutQuint);
                Tween.Fade(_fan, FanAlpha, .55f);
                Spin();

                if (_fan2Rt != null) Counterspin();

                Tween.Fade(_glow, GlowAlpha, .5f);
                Flare();

                // Each wave a different colour of the scheme rather than all of them the tint:
                // a stack of rings in one colour reads as one ring drawn badly.
                for (int i = 0; i < 2 + _tier; i++) Shockwave(i * .07f, _c.Nth(i));

                Burst.Confetti(Content, 40 + _tier * 22);
            });

            // -- the friend --------------------------------------------------
            cue.Then(.20f, () =>
            {
                _revealing = false;

                _discRt.localScale = Vector3.one * .2f;
                Tween.Scale(_discRt, 1f, .78f, Ease.OutElastic);

                // Silhouette into colour. The plate lands first and the face resolves inside
                // it, so the eye has somewhere to be while the companion appears.
                _face.color = Silhouette;
                Tween.Tint(_face, Color.white, .46f, Ease.OutQuad).Delay(.12f);

                Audio.Sfx("win", .62f);
                Audio.Sfx("bell", .45f, 1.05f, .10f);

                Burst.Sparks(_discRt, Vector2.zero, _tint, 22 + _tier * 5, 520f, 34f, .95f);

                // Two haloes, the outer one in the partner colour. Halo puts itself behind its
                // siblings, so the second call lands behind the first and the plate sits in a
                // pool of light that changes hue outwards rather than fading to the backdrop.
                UIKit.Halo(_disc.transform, _tint, DiscSize * 1.5f, .34f);
                UIKit.Halo(_disc.transform, _c.Partner, DiscSize * 2.3f, .18f);
            });

            // -- the name ----------------------------------------------------
            cue.Then(.42f, () =>
            {
                SetAlpha(_name, 1f);
                _name.transform.localScale = Vector3.one * 2.1f;
                Tween.Scale(_name.transform, 1f, .30f, Ease.InCubic).OnDone(() =>
                {
                    Tween.Punch(_name.transform, .16f, .34f);
                    Audio.Sfx("pop2", .55f, .95f);
                    Haptic.Tap();
                });
            });

            cue.Then(.24f, () =>
            {
                Tween.Fade(_underline, .85f, .26f);
                Tween.Run(.34f, Ease.OutCubic, t =>
                {
                    if (!_underline) return;
                    var rt = _underline.rectTransform;
                    rt.sizeDelta = new Vector2(Mathf.Lerp(0f, 380f, t), rt.sizeDelta.y);
                }, _underline);
            });

            // -- the stars ---------------------------------------------------
            // One per tier, each a little later and a little higher than the last. The rising
            // pitch is most of the effect: it says "and there is more" without a word.
            cue.Then(.16f, null).Repeat(_tier, .13f, i =>
            {
                if (_stars == null || i >= _stars.Length || !_stars[i]) return;

                Tween.Pop(_stars[i].transform, 0f, .46f);
                Audio.Sfx("star", .5f, .92f + i * .09f);

                // The stars stay gold — they are the rarity count, and counting in five colours
                // would read as five kinds of thing — but each one throws its own colour of the
                // scheme, so a five-star reveal walks the whole palette on the way up.
                Burst.Sparks((RectTransform)_stars[i].transform, Vector2.zero,
                             Pal.Lift(_c.Nth(i), .2f), 7, 130f, 15f, .45f);
            });

            // -- settling ----------------------------------------------------
            cue.Then(.26f, () =>
            {
                Tween.Fade(_sub, .86f, .34f);

                Tween.Pop(_doneRt, .6f, .46f);
                Tween.Fade(_dismiss, 1f, .3f);
                _dismiss.blocksRaycasts = true;

                // A second, smaller fall for the tiers that earned one. Two waves a second and a
                // half apart read as longer than one wave twice the size, and the top of the
                // ladder is where length is the point.
                if (_tier >= 3) Burst.Confetti(Content, 20 + _tier * 8);

                Audio.Sfx("chime2", .5f);
            });

            cue.Then(.12f, Idle);
        }

        /// <summary>
        /// The main fan's endless turn. Its own method because both the sequence and the skip
        /// path need it, and a rotation defined twice is a rotation that will one day disagree
        /// with itself.
        ///
        /// Channelled, so reaching the resting state twice — the sequence, then a skip landing
        /// after it — replaces the turn rather than running two of them a frame out of step
        /// against the same rotation.
        /// </summary>
        void Spin()
        {
            Tween.Run(64f, Ease.Linear, t =>
            {
                if (_fanRt) _fanRt.localRotation = Quaternion.Euler(0, 0, t * 360f);
            }, _fanRt, "spin").Loop(-1, false);
        }

        /// <summary>The second fan, opening and turning against the first. See <see cref="Spin"/>.</summary>
        void Counterspin()
        {
            _fan2Rt.localScale = Vector3.one * .3f;
            Tween.Scale(_fan2Rt, 1f, .82f, Ease.OutQuint);
            Tween.Fade(_fan2, Fan2Alpha, .6f);

            Tween.Run(47f, Ease.Linear, t =>
            {
                if (_fan2Rt) _fan2Rt.localRotation = Quaternion.Euler(0, 0, -t * 360f);
            }, _fan2Rt, "spin").Loop(-1, false);
        }

        /// <summary>
        /// A full-screen wash of the friend's own colour, left behind by the white flash.
        ///
        /// <para>
        /// Built here rather than up front and hidden, like the rings and unlike everything
        /// else, because it is genuinely transient: nothing in the resting state contains it, so
        /// <see cref="Skip"/> has nothing to set and a skip that lands before this beat simply
        /// never sees it.
        /// </para>
        /// <para>
        /// Above the whole composition on purpose. A wash underneath tints the backdrop, which
        /// the backdrop already is; over the top it tints the light, so the portrait arrives out
        /// of colour and resolves as the wash clears.
        /// </para>
        /// </summary>
        void ChromaPulse()
        {
            var wash = UIKit.Img("Pulse", Content, Art.Pixel, Pal.A(_tint, .44f));
            UIKit.StretchTo((RectTransform)wash.transform, 0, 0, 0, 0);
            wash.raycastTarget = false;

            // Owned by the wash rather than by this view, so a skip cannot strand a coloured
            // sheet at half strength over the reveal it was meant to introduce.
            Tween.Fade(wash, 0f, .62f, Ease.OutQuad)
                 .OnDone(() => { if (wash) Destroy(wash.gameObject); });
        }

        /// <summary>A ring rushing in from off-screen and vanishing at the middle.</summary>
        void Collapse(int index)
        {
            var ring = UIKit.Img("In" + index, Content, Art.Ring(256, 7f), Pal.A(_c.Nth(index), 0f),
                                 Vector2.one * 240f, new Vector2(.5f, .5f), new Vector2(0f, 90f));
            ring.raycastTarget = false;

            var rt = (RectTransform)ring.transform;

            Tween.Run(.52f, Ease.InCubic, t =>
            {
                if (!rt) return;
                rt.localScale = Vector3.one * Mathf.Lerp(6.2f, .18f, t);
                var c = ring.color;
                c.a = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * .70f;
                ring.color = c;
            }, ring).OnDone(() => { if (ring) Destroy(ring.gameObject); });
        }

        /// <summary>A shockwave leaving the impact, thinning as it goes.</summary>
        void Shockwave(float delay, Color colour)
        {
            var ring = UIKit.Img("Wave", Content, Art.Ring(256, 10f), Pal.A(colour, 0f),
                                 Vector2.one * 360f, new Vector2(.5f, .5f), new Vector2(0f, 90f));
            ring.raycastTarget = false;

            var rt = (RectTransform)ring.transform;

            Tween.Run(.72f, Ease.OutQuint, t =>
            {
                if (!rt) return;
                rt.localScale = Vector3.one * Mathf.Lerp(.25f, 5.4f, t);
                var c = ring.color; c.a = .62f * (1f - t) * (1f - t); ring.color = c;
            }, ring).Delay(delay).OnDone(() => { if (ring) Destroy(ring.gameObject); });
        }

        /// <summary>
        /// The living state the reveal rests in: the friend bobbing, the rim breathing,
        /// fireflies drifting up through the fan.
        ///
        /// Started at the end of the sequence and by <see cref="Settle"/>, and guarded so the
        /// two cannot both run it — a second Bob would fight the first over the same
        /// anchoredPosition and the portrait would jitter.
        /// </summary>
        void Idle()
        {
            if (_settled) return;
            _settled = true;

            if (_discRt) Tween.Bob(_discRt, 9f, 3.4f);
            if (_rim) { Tween.Breathe(_rim.transform, .035f, 2.4f); Hue(); }

            Fireflies.Spawn(Content, 16, Pal.A(_tint, .9f), 7f, 26f);
        }

        /// <summary>
        /// The rim drifting between the friend's colour and the room's accent, for ever.
        ///
        /// The cheapest colour in the whole screen: the brightest edge in the frame is already
        /// the thing the eye rests on once everything has settled, so moving its hue is what
        /// keeps the resting state alive rather than merely animated. Reached only through
        /// <see cref="Idle"/>, which both the sequence and the skip path go through, and
        /// channelled so arriving twice replaces rather than doubles.
        /// </summary>
        void Hue()
        {
            Tween.Run(4.8f, Ease.InOutSine, t =>
            {
                if (_rim) _rim.color = Pal.A(Color.Lerp(_tint, _c.Accent, t), .95f);
            }, _rim, "hue").Loop(-1, true);
        }

        // ----------------------------------------------------------------- skip
        /// <summary>
        /// Ends the sequence now, in the state it was heading for.
        ///
        /// <para>
        /// Possible only because every element already exists — the beats reveal things rather
        /// than build them — so this is one pass of assignments instead of a second
        /// choreography that would have to be kept in agreement with <see cref="Play"/>. That
        /// agreement is exactly what a skip path normally gets wrong.
        /// </para>
        /// <para>
        /// Pending beats are killed by owner. <see cref="Cue"/> schedules every one of them
        /// against this component, which is what makes them cancellable as a group.
        /// </para>
        /// </summary>
        void Skip()
        {
            if (_settled) { return; }

            Tween.KillAll(this);
            _revealing = false;

            // The sprite carries the colour, so the sky's own tint is white and only its opacity
            // was ever being animated.
            if (_sky) _sky.color = Color.white;
            if (_vignette) SetAlpha(_vignette, VignetteAlpha);
            if (_flash) SetAlpha(_flash, 0f);

            // The blobs keep drifting — Drift is owned by each blob rather than by this view, so
            // KillAll never touched it — and only their brightness has to be caught up.
            if (_aurora != null)
                for (int i = 0; i < _aurora.Length; i++)
                    SetAlpha(_aurora[i], AuroraAlpha[i]);

            if (_fanRt)
            {
                _fanRt.localScale = Vector3.one;
                SetAlpha(_fan, FanAlpha);

                // Restarted rather than left dead: a skip landing before the impact beat killed
                // the beat that would have started the turn, and a fan frozen mid-turn is the one
                // part of this that would read as a bug rather than as a fast-forward.
                Spin();
            }

            if (_fan2Rt != null)
            {
                _fan2Rt.localScale = Vector3.one;
                SetAlpha(_fan2, Fan2Alpha);
                Counterspin();
            }

            if (_glow) SetAlpha(_glow, GlowAlpha);

            if (_discRt) _discRt.localScale = Vector3.one;
            if (_face) { CompanionArt.Paint(_face, Avatar); _face.color = Color.white; }

            if (_name) { SetAlpha(_name, 1f); _name.transform.localScale = Vector3.one; }
            if (_sub) SetAlpha(_sub, .86f);

            if (_underline)
            {
                SetAlpha(_underline, .85f);
                var rt = _underline.rectTransform;
                rt.sizeDelta = new Vector2(380f, rt.sizeDelta.y);
            }

            if (_stars != null)
                foreach (var star in _stars)
                    if (star) star.transform.localScale = Vector3.one;

            if (_doneRt) _doneRt.localScale = Vector3.one;
            if (_dismiss) { _dismiss.alpha = 1f; _dismiss.blocksRaycasts = true; }

            // Sets _settled, so this cannot be re-entered and Idle cannot double up.
            Idle();
        }

        static void SetAlpha(Graphic g, float a)
        {
            if (g == null) return;
            var c = g.color; c.a = a; g.color = c;
        }

        /// <summary>
        /// Back finishes the sequence rather than closing, once. A player pressing back
        /// halfway through almost always means "get to it", and closing would throw away the
        /// reveal of something they just paid for; pressing it again then leaves.
        /// </summary>
        public override bool OnBack()
        {
            if (!_settled) { Skip(); return true; }

            Close();
            return true;
        }
    }
}
