using System;
using GlimmerGrove.Ads;
using GlimmerGrove.Daily;
using GlimmerGrove.Layout;
using GlimmerGrove.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The bonus wheel: spin for a multiplier, then watch a video to keep it.
    ///
    /// <para>
    /// <b>Which half is the offer.</b> The spin is free and the collection is not, and that
    /// ordering is the whole feature. A player who spins and does not like the answer has lost
    /// nothing and is not being sold anything; a player who spins well is choosing to spend
    /// thirty seconds on a figure they can already see. The reverse — watch first, then find
    /// out — is the shape that makes people feel tricked, because the thing they paid for was
    /// decided after they paid.
    /// </para>
    /// <para>
    /// <b>Nothing about the spin is random at the moment it happens.</b> The slice is a pure
    /// function of (account, day, spin index) and was decided before the panel opened — see
    /// <see cref="BonusWheel"/>. So backing out and coming back lands on the same slice, which
    /// is invariant 9c's anti-reroll property, and the number shown before the video is the
    /// number the server independently grants after it. This panel is a picture of an answer
    /// that already exists, not the thing that decides it.
    /// </para>
    /// <para>
    /// <b>It replaces <see cref="AdOfferOverlay"/> for this one placement, and only when the
    /// stand is open.</b> A wheel needs an account to seed from and a server that understands
    /// it; without either, the caller falls back to the flat offer, which is what that server
    /// will actually pay. See <see cref="WheelStand.IsOpen"/> and <c>OpenFor</c>.
    /// </para>
    /// </summary>
    public sealed class BonusWheelOverlay : ModalView
    {
        /// <summary>
        /// Which placement is being spun for. A field rather than a constant because the wheel
        /// is a way of paying a rewarded placement rather than a feature of one — a second
        /// wheel on a second placement would need nothing here changed but this.
        /// </summary>
        public string PlacementId = AdPlacement.WinBonus;

        /// <summary>Raised after a reward actually lands, so the panel behind can repaint.</summary>
        public Action Rewarded;

        /// <summary>
        /// Raised when the panel goes away having paid nothing.
        ///
        /// Exactly one of this and <see cref="Rewarded"/> fires, for every way out this panel
        /// has — the collect, the corner cross, the scrim, the back key, and the screen
        /// underneath being torn down mid-spin. The house rule about panels with several exits:
        /// the safe outcome goes on <c>OnDestroy</c> and the latch is what stops the ordinary
        /// exit reporting twice.
        /// </summary>
        public Action Dismissed;

        /// <summary>
        /// Opens the wheel where it can be honoured, and the flat offer where it cannot.
        ///
        /// <para>
        /// The one place that decision is made, so no screen has to remember the three things
        /// that have to be true. Both destinations are real offers for the same placement, so
        /// the fallback is a quieter panel rather than a refusal — which is what makes the
        /// wheel safe to ship before the functions that grant it are deployed (invariant 12a).
        /// </para>
        /// </summary>
        public static void OpenFor(string placementId, Action rewarded = null)
        {
            if (placementId == AdPlacement.WinBonus && WheelStand.IsOpen && WheelStand.Landing() >= 0)
            {
                Flow.Modal<BonusWheelOverlay>(v =>
                {
                    v.PlacementId = placementId;
                    v.Rewarded = rewarded;
                });
                return;
            }

            Flow.Modal<AdOfferOverlay>(v =>
            {
                v.PlacementId = placementId;
                v.Rewarded = rewarded;
            });
        }

        // The geometry lives in Domain — see WheelPanel — because whether two things on a
        // screen overlap is arithmetic, and arithmetic in a MonoBehaviour is the one place
        // nothing can prove it. WheelPanelTests holds the panel under the shortest canvas this
        // game is drawn on and checks that no row is drawn through the one above it; neither
        // fails on a machine if the numbers stay here.

        WheelFace _face;
        Btn _spin;
        Text _status;
        Text _odds;
        RectTransform _wheelHost;

        int _landing = -1;
        int _percent = WheelRules.MinPercent;

        bool _watching, _paid;

        /// <summary>
        /// True once the button has actually become the video offer — see
        /// <see cref="BecomeTheOffer"/>.
        ///
        /// <para>
        /// The repaint is gated on this rather than on the wheel having landed, and the
        /// difference is a whole beat wide: <c>WheelSpin.CelebrationSeconds</c> passes between
        /// the slice being decided and the button changing hands, and the tick runs four times a
        /// second throughout it. Gated on the landing, the first thing to write
        /// "WATCH A VIDEO TO COLLECT" was that tick — onto a button that still had no play
        /// glyph, so the caption was fitted to the whole face and drawn nearly edge to edge.
        /// <c>BecomeTheOffer</c> then added the glyph and re-fitted it smaller, and what the
        /// player saw was a caption that overflowed its button and then tidied itself up as the
        /// button popped. One writer, once, after the glyph exists.
        /// </para>
        /// </summary>
        bool _offering;

        /// <summary>The offer being multiplied. Read once — a table swap mid-panel would move
        /// the figures under a wheel the player has already been shown.</summary>
        AdOffer _offer;
        BonusWheel _wheel;

        protected override void Build()
        {
            _offer = RewardedAds.Table.Offer(PlacementId);
            _wheel = WheelStand.Wheel;
            _landing = WheelStand.Landing();

            // Nothing to draw. Reached only if the stand shut between OpenFor and here — a sync
            // landing in the same frame — and the honest answer is the flat offer rather than a
            // wheel with no slice under its pointer.
            if (_landing < 0 || !_wheel.IsUsable || !_offer.IsValid)
            {
                Flow.Modal<AdOfferOverlay>(v => { v.PlacementId = PlacementId; v.Rewarded = Rewarded; });
                Rewarded = null;
                Flow.Dismiss(this);
                return;
            }

            _percent = _wheel.SliceAt(_landing).Percent;

            var stack = WheelPanel.Of();

            // Never dismissed by a stray tap on the scrim. A thumb resting on the panel while a
            // wheel is turning is the ordinary way to hold a phone, and losing a spin to it
            // would look exactly like the game taking a prize back.
            MakePanel(new Vector2(WheelPanel.Width, stack.Height), Loc.Get("ui.wheel.title"),
                      dismissOnScrim: false);

            BuildWheel(stack.WheelCentre, stack.WheelSize);

            _odds = UIKit.Titled("Odds", Panel, OddsLine(), 27, new Color(.40f, .30f, .22f),
                                 TextAnchor.MiddleCenter,
                                 new Vector2(WheelPanel.ContentWidth, WheelPanel.OddsHeight),
                                 new Vector2(.5f, 1f), new Vector2(0f, -stack.OddsCentre),
                                 outline: 0f, shadow: 0f, wrap: true);
            UIKit.Shrinkable(_odds, 18);

            _status = UIKit.Titled("Status", Panel, string.Empty, 30, new Color(.36f, .25f, .18f),
                                   TextAnchor.UpperCenter,
                                   new Vector2(WheelPanel.ContentWidth, WheelPanel.StatusHeight),
                                   new Vector2(.5f, 1f), new Vector2(0f, -stack.StatusCentre),
                                   outline: 0f, shadow: 0f, wrap: true);
            UIKit.Shrinkable(_status, 22);

            _spin = UIKit.TextButton("Spin", Panel, "btn_violet", Loc.Get("ui.wheel.spin"), 48,
                                     new Vector2(620f, WheelPanel.ButtonHeight), new Vector2(.5f, 1f),
                                     new Vector2(0f, -stack.ButtonCentre), OnSpin, "ic_play");
            // The play glyph belongs to the video, not to the spin. It is added back the moment
            // the button becomes an offer, which is also the moment it starts being one.
            if (_spin.Icon) { Destroy(_spin.Icon.gameObject); _spin.Icon = null; }

            // Through UIKit.OneLine rather than by raising the flag, and the difference is the
            // whole of the bug it fixes. TextButton turns Unity's best-fit on for any button
            // carrying a glyph, and best-fit concedes the *line* before it concedes the size —
            // so a long caption folds, and the fold is measured, re-measured when the font
            // texture rebuilds a frame later, and re-laid out again by FitLabel against the box
            // that measurement produced. What the player sees is WATCH A VIDEO TO COLLECT
            // arriving crushed and then springing out to its real width. OneLine switches
            // best-fit off and sizes the caption once, from Text.preferredWidth, in the frame it
            // was set. The flag alone leaves best-fit running underneath it.
            UIKit.OneLine(_spin, 24);

            UIKit.Halo(_spin.transform, Pal.Bloom, 700f, .30f);
            Sheen.Attach((RectTransform)_spin.transform, 2.6f);
            Tween.Breathe(_spin.transform, .028f, 1.9f);

            // A corner cross rather than a "no thanks" button. Nobody taps decline, and a whole
            // button spent on the option to refuse reads as a panel expecting to be refused —
            // but a modal whose only exit is the scrim is one players experience as being
            // trapped by an advert, which is a store-review problem as much as a decency one.
            UIKit.IconButton("Dismiss", Panel, Skins.Nav, "ic_close", new Vector2(84f, 84f),
                             new Vector2(1f, 1f), new Vector2(-58f, -58f), () => Close());

            RewardedAds.Changed += Repaint;
            Repaint();
        }

        void BuildWheel(float y, float size)
        {
            _wheelHost = UIKit.Node("Wheel", Panel);
            _wheelHost.anchorMin = _wheelHost.anchorMax = new Vector2(.5f, 1f);
            _wheelHost.pivot = new Vector2(.5f, .5f);
            _wheelHost.sizeDelta = Vector2.one * size;
            _wheelHost.anchoredPosition = new Vector2(0f, -y);

            _face = WheelFace.Attach(_wheelHost, _wheel, _offer.Amount, size);

            _wheelHost.localScale = Vector3.zero;
            Tween.Pop(_wheelHost, 0f, .62f, .12f);
        }

        void OnDestroy()
        {
            RewardedAds.Changed -= Repaint;

            // The backstop, not the normal path — including the two endings no button knows
            // about: the back key pressed during the payout, and the screen underneath being
            // torn down with this still open.
            Report(_paid);
        }

        bool _reported;

        /// <summary>Tells the caller how this ended, exactly once. See <see cref="Dismissed"/>.</summary>
        void Report(bool paid)
        {
            if (_reported) return;
            _reported = true;

            var callback = paid ? Rewarded : Dismissed;
            Rewarded = null;
            Dismissed = null;

            // Swallowed, because this can run during teardown: a caller that throws would leave
            // the rest of the destroy chain unrun, and one of the things it unruns is the event
            // unsubscription above.
            try { callback?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // ------------------------------------------------------------- the words
        /// <summary>
        /// The odds, printed because they can be.
        ///
        /// <para>
        /// Every slice is the same size and every slice is equally likely, so the whole
        /// disclosure is one sentence with a number in it — the property invariant 10b protects
        /// for the daily chest, here for the same reason. It is read off the live table rather
        /// than written into the copy, so a content push that changes the wheel changes the
        /// sentence, which is what stops the panel that explains the game being the first thing
        /// to rot when the game is retuned.
        /// </para>
        /// </summary>
        string OddsLine() => Loc.Format("ui.wheel.odds", _wheel.Count);

        // ------------------------------------------------------------- painting
        /// <summary>
        /// Ticks four times a second rather than sixty, for <c>AdOfferOverlay</c>'s reason:
        /// every live thing here has one-second granularity, so a per-frame repaint rebuilds
        /// the same strings fifty-nine times out of sixty for pixels nobody can tell apart.
        /// </summary>
        const float TickSeconds = .25f;
        float _tick;

        void Update()
        {
            if (_paid || _watching || (_face != null && _face.Spinning)) return;

            _tick += Time.unscaledDeltaTime;
            if (_tick < TickSeconds) return;

            _tick = 0f;
            Repaint();
        }

        void Repaint()
        {
            if (_paid || _watching || _status == null) return;
            if (_face != null && _face.Spinning) return;

            var status = RewardedAds.Status(PlacementId);

            if (_spin != null) _spin.Interactable = status.CanShow;

            // Before the spin the button says SPIN whatever the network is doing, because the
            // spin is free and the refusal below it is about the video. Once the button has
            // become the video it takes the ad's own caption — cooldowns and allowances
            // included. See _offering for why that is not the same moment as the wheel landing.
            if (_offering && _spin != null)
                AdOfferButton.Paint(_spin, PlacementId, "ui.wheel.collect");

            // The line under the wheel says something only when there is something to say. A
            // ready video needs no sentence: before the landing the button says SPIN and the
            // spin is free, and after it the button *is* the video and carries its own caption,
            // so "watch a short video and it is yours" underneath it was the same instruction a
            // third time — printed over the odds line, which had already said it too. What is
            // left is the half a caption cannot carry: loading, a cooldown, a cap, no account.
            _status.text = status.State == AdOfferState.Ready
                ? string.Empty
                : AdOfferButton.Explain(status);
        }

        // ------------------------------------------------------------- spinning
        void OnSpin()
        {
            if (_face == null || _face.Spinning || _face.Landed) return;

            // Asked here as well as painted, because a cooldown can expire — or a cap arrive —
            // between the paint and the tap. The spin itself costs nothing, but selling a
            // player a result they cannot collect is worse than refusing the spin.
            if (!RewardedAds.CanOffer(PlacementId)) { Repaint(); return; }

            _spin.Interactable = false;
            _spin.SetCaption(Loc.Get("ui.wheel.spinning"));
            if (_odds) Tween.Fade(_odds, .35f, .3f);
            if (_status) _status.text = string.Empty;

            _face.Spin(_landing, Landed);
        }

        /// <summary>
        /// The wheel has stopped. Everything loud happens here, once, and then the button turns
        /// into the question.
        ///
        /// <para>
        /// The celebration's length comes from <see cref="WheelSpin.CelebrationSeconds"/> rather
        /// than being a constant, so the wheel's best result is allowed to be the loudest thing
        /// on the screen and its most ordinary one gets out of the way. One haptic, on the
        /// landing itself: <c>Handheld.Vibrate</c> is a single fixed-length pulse on Android, so
        /// several inside a second are one rumble rather than several taps.
        /// </para>
        /// </summary>
        void Landed()
        {
            if (this == null || _face == null) return;

            var slice = _wheel.SliceAt(_landing);
            var tint = WheelPaint.For(_wheel, _landing);
            var won = _face.Won(_landing);

            _face.Celebrate(_landing);

            // No haptic, here or on the payout. Handheld.Vibrate is one fixed-length pulse on
            // Android with no way to make it lighter, and this panel fires twice inside a few
            // seconds on an event a player meets several times a session — which is one rumble
            // rather than two taps. The victory panel's payout lost its buzz for the same
            // reason; the sound and the light are what mark the moment.
            // One sound for every landing rather than two bells chosen by tier. The
            // pair before it were struck metal, which is what a wheel of fortune must
            // not sound like; how good the slice was is carried by the confetti below
            // and by the figure that follows, not by swapping the instrument.
            Audio.Sfx("wheel", .62f);

            if (won != null) Burst.Sparks(won, Vector2.zero, tint, slice.IsBonus ? 26 : 14,
                                          460f, 30f, .85f);

            // The wash and the confetti are for a slice that is genuinely worth it. Spending
            // them on every spin spends them on most spins and marks out none — the same
            // argument that keeps the victory panel's flash for a full star row alone.
            // Confetti on every landing, sized by what was won. Spending it only on the top
            // slice marked one spin in eight and left the rest looking like a near miss -
            // every slice on this wheel pays, so every landing is worth something. The
            // hierarchy is kept by *how much* falls and by the flash, which is still the
            // bonus tiers' alone.
            if (slice.Percent >= _wheel.TopPercent && slice.IsBonus)
            {
                Flow.Flash(new Color(1f, .95f, .78f), .34f, .55f);
                Burst.Confetti(Content, 54);
            }
            else if (slice.IsBonus)
            {
                Flow.Flash(Pal.A(tint, 1f), .16f, .40f);
                Burst.Confetti(Content, 32);
            }
            else
            {
                Burst.Confetti(Content, 18);
            }

            var cue = new Cue(this);

            cue.Then(WheelSpin.CelebrationSeconds(_percent), () =>
            {
                if (this == null) return;

                if (_odds)
                {
                    _odds.text = Loc.Format("ui.wheel.won",
                                            Compact.Number(slice.Pays(_offer.Amount)));
                    _odds.color = new Color(.42f, .28f, .12f);
                    Tween.Fade(_odds, 1f, .25f);
                    Tween.Pop(_odds.transform, .8f, .34f);
                }

                BecomeTheOffer();
            });
        }

        /// <summary>
        /// The spin button becomes the video button.
        ///
        /// <para>
        /// The same control rather than a second one appearing below it, and that is
        /// deliberate: the player's thumb is already there, and a panel that grows a new button
        /// at the moment of a win is one where the new button gets pressed by the tap that was
        /// meant for the old one. The glyph comes back with it, because from here on it really
        /// does lead to a video.
        /// </para>
        /// </summary>
        void BecomeTheOffer()
        {
            if (_spin == null) return;

            // The breath is stopped and the scale put back before anything reads it. Three
            // things here take the button's local scale as a resting value — Setup latches it
            // as what a press returns to, Pop takes it as what it is springing towards, and
            // Breathe borrows it — so with the old breath still running each of them would
            // capture a size a few percent off and hand the next one something worse. The house
            // rule about a tween that reads its own target's value; the fix is to have exactly
            // one of them own the scale at a time.
            Tween.KillChannel(_spin.transform, "breathe");
            _spin.transform.localScale = Vector3.one;

            _spin.Setup(OnWatch);

            if (_spin.Icon == null)
            {
                var glyph = UIKit.Img("Icon", _spin.transform, Art.S("Ui/ic_play"), Pal.Cream,
                                      Vector2.one * (WheelPanel.ButtonHeight * .34f), new Vector2(.5f, .5f),
                                      new Vector2(0f, WheelPanel.ButtonHeight * UIKit.PillFaceLift));
                glyph.preserveAspect = true;
                _spin.Icon = glyph;
            }

            // Only now, with the glyph in place, is the caption allowed to change: the fit is
            // measured against the room the glyph leaves, and SetCaption re-fits only when the
            // words actually change — so a caption written a beat early is one that never gets
            // measured again.
            _offering = true;
            AdOfferButton.Paint(_spin, PlacementId, "ui.wheel.collect");

            Tween.Pop(_spin.transform, .86f, .38f);

            // After the pop rather than beside it, for the reason above.
            Tween.After(.40f, () =>
            {
                if (_spin) Tween.Breathe(_spin.transform, .028f, 1.9f);
            }, gameObject);

            Repaint();
        }

        // ------------------------------------------------------------- watching
        void OnWatch()
        {
            if (_watching || _paid) return;
            if (!RewardedAds.CanOffer(PlacementId)) { Repaint(); return; }

            _watching = true;
            _spin.Interactable = false;
            if (_status) _status.text = Loc.Get("ui.ads.opening");

            Show();
        }

        /// <summary>
        /// Shows the ad and banks what it paid.
        ///
        /// <para>
        /// The credits themselves are not applied here and never could be: an ad grant is the
        /// server's to make (invariant 10d), and it is the server that independently recomputes
        /// which slice this spin landed on. <c>RewardedAds.Redeem</c> asks for the sync; the
        /// figure this panel shows is the same arithmetic run on the phone, which is why the two
        /// agree without either telling the other anything. The show itself is
        /// <see cref="RewardedVideo.Watch"/>, one copy for the three panels that need it.
        /// </para>
        /// </summary>
        async void Show()
        {
            var payment = await RewardedVideo.Watch(PlacementId);

            // Asked before the liveness check, and deliberately: a prize that was paid for has
            // to be shown to somebody, and Paid raises a panel of its own rather than repainting
            // this one. The refusal branch below is the one that needs a panel to talk on, so it
            // still gives up when this has gone.
            if (payment.Paid) { Paid(payment.Drop, payment.Flight); return; }

            if (this == null) return;

            _watching = false;
            if (_status) _status.text = RewardedVideo.Refusal(payment);
            Repaint();
        }

        /// <summary>
        /// The video paid. Note what is handed on: the <em>wheel's</em> figure, not the drop's.
        ///
        /// <para>
        /// They are the same number and that is exactly why the wheel's is the one printed. The
        /// drop is the flat placement amount — <c>RewardedAds.Redeem</c> has no opinion about
        /// multipliers and should not — while the multiplier is applied by the server on the
        /// callback. Printing the drop here would show a player two hundred under a wheel that
        /// had just stopped on a thousand, and the thousand is what arrives.
        /// </para>
        /// <para>
        /// <b>The payoff is a panel of its own</b> — see <see cref="PrizeOverlay"/>. It used
        /// to be a caption change on this button: the wheel stayed up with its question answered
        /// and its own COLLECT where WATCH had been, which drew the largest moment in the
        /// placement as the smallest change on the screen. The wheel asks; the celebration
        /// answers; and because the answer owns its own panel it can be shown to a player who is
        /// no longer standing here.
        /// </para>
        /// </summary>
        void Paid(ChestDrop drop, RewardFlight flight)
        {
            _paid = true;
            _watching = false;

            var slice = _wheel.SliceAt(_landing);
            var prize = new ChestDrop(drop.Kind, slice.Pays(_offer.Amount));

            // Raised before this panel is asked whether it still exists, because the prize does
            // not depend on it: a player who backgrounded the app during the video may be
            // standing somewhere else entirely by now, and the celebration is the only thing
            // that tells them what the video was worth. Nothing below this line is load-bearing
            // either — the grant is the server's (invariant 10d) and Redeem has already asked
            // for it.
            Flow.Modal<PrizeOverlay>(v =>
            {
                v.Drop = prize;
                v.TitleKey = "ui.wheel.prize_title";
                v.Tint = WheelPaint.For(_wheel, _landing);
                v.Loud = slice.IsBonus;
                v.Loudest = slice.Percent >= _wheel.TopPercent;
                v.Flight = flight;
            });

            if (this == null) return;

            // Reported the instant the reward is banked rather than when the celebration closes,
            // because that is when it becomes true: the screen underneath carries the offer
            // button that opened this wheel, and it must stop offering a video this player has
            // now watched. Report is latched, so the backstop in OnDestroy stays harmless.
            Report(true);

            // Quiet, because the next thing the player hears is the celebration and a backing-out
            // whoosh underneath it is one sound too many. The wheel has nothing left to say: its
            // answer is standing on the panel that just opened over it.
            Close(quiet: true);
        }

        /// <summary>
        /// Back closes, except while the wheel is turning.
        ///
        /// <para>
        /// A spin that is interrupted costs nothing — the slice is a fact about the day rather
        /// than about the moment, so reopening the panel finds it exactly where it was. It is
        /// swallowed anyway, because a modal that vanishes mid-animation reads as a crash, and
        /// the player is three seconds from an answer they can then decline.
        /// </para>
        /// </summary>
        public override bool OnBack()
        {
            if (_face != null && _face.Spinning) return true;

            Close();
            return true;
        }
    }
}
