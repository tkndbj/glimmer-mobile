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

        Image _veil, _vignette, _fan, _fan2, _glow, _flash, _rim;
        Image _disc, _face;
        RectTransform _discRt, _fanRt, _fan2Rt;
        Text _name, _sub;
        Image[] _stars;
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
        Color _tint;
        bool _settled;

        protected override void Build()
        {
            _tier = TierOf(Avatar);
            _tint = TintOf(_tier);

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

        /// <summary>
        /// The tier's colour: pale, green, blue, purple, gold.
        ///
        /// <para>
        /// Deliberately the rarity ladder every player already knows from every other game
        /// they have installed — common through to legendary — because this is the one piece of
        /// the reveal that has to be understood without being taught. The first version ran
        /// cream → mint → sun → gold → magenta, which put the game's own premium colour in
        /// fourth place and ended on a pink nobody reads as "the best one". Gold last is worth
        /// more than gold in the middle, and it agrees with what gold means everywhere else in
        /// this UI.
        /// </para>
        /// </summary>
        static Color TintOf(int tier)
        {
            switch (tier)
            {
                case 1: return Pal.Cream;
                case 2: return Pal.Mint;
                case 3: return Pal.Azure;
                case 4: return Pal.Bloom;
                default: return Pal.Gold;
            }
        }

        // --------------------------------------------------------------- stage
        /// <summary>
        /// The dark room the friend arrives into: a veil, a vignette to pull the eye to the
        /// middle, a rotating fan of light and a warm core.
        /// </summary>
        void BuildStage()
        {
            // The veil is also the skip target. It is the bottom layer and covers the screen,
            // so a tap anywhere that is not the button lands here.
            _veil = UIKit.Img("Veil", Content, Art.Pixel, new Color(.02f, .04f, .06f, 0f));
            UIKit.StretchTo((RectTransform)_veil.transform, 0, 0, 0, 0);
            _veil.raycastTarget = true;
            _veil.gameObject.AddComponent<Btn>().Setup(Skip);

            _vignette = UIKit.Img("Vignette", Content, Art.Vignette(256), new Color(.01f, .03f, .05f, 0f));
            UIKit.StretchTo((RectTransform)_vignette.transform, 0, 0, 0, 0);
            _vignette.raycastTarget = false;

            // Ray count rises with the tier, so a rarer friend arrives through a busier fan.
            _fan = UIKit.Img("Fan", Content, Art.Rays(512, 10 + _tier * 4), Pal.A(_tint, 0f),
                             Vector2.one * FanSize, new Vector2(.5f, .5f), new Vector2(0f, 90f));
            _fanRt = (RectTransform)_fan.transform;
            _fanRt.localScale = Vector3.zero;
            _fan.raycastTarget = false;

            // The top two tiers get a second fan turning the other way. Two counter-rotating
            // sets read as volume rather than as a faster spin — one fan turning quickly just
            // looks like a fan turning quickly, however fast it goes — and it costs one more
            // Image on the two reveals a player sees least often and remembers most.
            if (_tier >= 4)
            {
                _fan2 = UIKit.Img("Fan2", Content, Art.Rays(512, 6 + _tier), Pal.A(_tint, 0f),
                                  Vector2.one * (FanSize * .74f), new Vector2(.5f, .5f),
                                  new Vector2(0f, 90f));
                _fan2Rt = (RectTransform)_fan2.transform;
                _fan2Rt.localScale = Vector3.zero;
                _fan2.raycastTarget = false;
            }

            _glow = UIKit.Img("Glow", Content, Art.Glow(256, 1.7f), Pal.A(_tint, 0f),
                              Vector2.one * 900f, new Vector2(.5f, .5f), new Vector2(0f, 90f));
            _glow.raycastTarget = false;
        }

        /// <summary>The portrait, on its plate, with the rim that will breathe once it lands.</summary>
        void BuildFriend()
        {
            _disc = UIKit.Img("Disc", Content, Art.Disc(300), Pal.A(Pal.Hex("#0A3F49"), .96f),
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
            var cross = UIKit.IconButton("Dismiss", Content, "sq_dark", "ic_close",
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
                Tween.Fade(_veil, .995f, .30f);
                Tween.Fade(_vignette, .86f, .40f);
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

                Audio.Sfx("unlock", .8f);
                Audio.Sfx("shatter", .32f, 1.25f);
                Haptic.Tap();

                Tween.Shake((RectTransform)Content, 26f, .42f);

                // The fan arrives with the bang and then never stops turning. A still sunburst
                // reads as a decal; a turning one reads as light.
                _fanRt.localScale = Vector3.one * .35f;
                Tween.Scale(_fanRt, 1f, .70f, Ease.OutQuint);
                Tween.Fade(_fan, .34f, .55f);
                Tween.Run(64f, Ease.Linear, t =>
                {
                    if (_fanRt) _fanRt.localRotation = Quaternion.Euler(0, 0, t * 360f);
                }, _fanRt).Loop(-1, false);

                if (_fan2Rt != null) Counterspin();

                Tween.Fade(_glow, .46f, .5f);

                for (int i = 0; i < 2 + _tier; i++) Shockwave(i * .07f);

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
                UIKit.Halo(_disc.transform, _tint, DiscSize * 1.5f, .34f);
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
                Burst.Sparks((RectTransform)_stars[i].transform, Vector2.zero, Pal.Gold,
                             7, 130f, 15f, .45f);
            });

            // -- settling ----------------------------------------------------
            cue.Then(.26f, () =>
            {
                Tween.Fade(_sub, .86f, .34f);

                Tween.Pop(_doneRt, .6f, .46f);
                Tween.Fade(_dismiss, 1f, .3f);
                _dismiss.blocksRaycasts = true;

                Audio.Sfx("chime2", .5f);
            });

            cue.Then(.12f, Idle);
        }

        /// <summary>
        /// The second fan, opening and turning against the first. Its own method because both
        /// the sequence and the skip path need it, and a rotation defined twice is a rotation
        /// that will one day disagree with itself.
        /// </summary>
        void Counterspin()
        {
            _fan2Rt.localScale = Vector3.one * .3f;
            Tween.Scale(_fan2Rt, 1f, .82f, Ease.OutQuint);
            Tween.Fade(_fan2, .26f, .6f);

            Tween.Run(47f, Ease.Linear, t =>
            {
                if (_fan2Rt) _fan2Rt.localRotation = Quaternion.Euler(0, 0, -t * 360f);
            }, _fan2Rt).Loop(-1, false);
        }

        /// <summary>A ring rushing in from off-screen and vanishing at the middle.</summary>
        void Collapse(int index)
        {
            var ring = UIKit.Img("In" + index, Content, Art.Ring(256, 7f), Pal.A(_tint, 0f),
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
        void Shockwave(float delay)
        {
            var ring = UIKit.Img("Wave", Content, Art.Ring(256, 10f), Pal.A(_tint, 0f),
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
            if (_rim) Tween.Breathe(_rim.transform, .035f, 2.4f);

            Fireflies.Spawn(Content, 16, Pal.A(_tint, .9f), 7f, 26f);
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

            if (_veil) _veil.color = new Color(.02f, .04f, .06f, .995f);
            if (_vignette) SetAlpha(_vignette, .86f);
            if (_flash) SetAlpha(_flash, 0f);

            if (_fanRt)
            {
                _fanRt.localScale = Vector3.one;
                SetAlpha(_fan, .34f);

                // Restarted rather than left dead: KillAll took the endless rotation with the
                // beats, and a fan frozen mid-turn is the one part of this that would read as
                // a bug rather than as a fast-forward.
                Tween.Run(64f, Ease.Linear, t =>
                {
                    if (_fanRt) _fanRt.localRotation = Quaternion.Euler(0, 0, t * 360f);
                }, _fanRt).Loop(-1, false);
            }

            if (_fan2Rt != null)
            {
                _fan2Rt.localScale = Vector3.one;
                SetAlpha(_fan2, .26f);
                Counterspin();
            }

            if (_glow) SetAlpha(_glow, .46f);

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
