using System;
using GlimmerGrove.Daily;
using GlimmerGrove.Layout;
using GlimmerGrove.Localization;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// What a video paid, handed over.
    ///
    /// <para>
    /// <b>A panel of its own rather than a third state of whatever asked for the ad.</b> The
    /// wheel is a question — spin, see the multiplier, decide whether it is worth thirty
    /// seconds — and by the time this is raised the question has been answered and the video
    /// watched. Keeping the two on one panel meant the payoff was a caption change on the
    /// button that had just asked for the ad, under a wheel whose job was finished: the
    /// biggest moment in the placement drawn as the smallest change on the screen. So the
    /// wheel steps out and this arrives, which is the shape <c>ShopGrantOverlay</c> and
    /// <c>ChestOverlay</c> already use for the other two places currency is handed over — one
    /// gesture the player recognises wherever it happens.
    /// </para>
    /// <para>
    /// <b>It is one panel for every placement that pays into a celebration, and the second one
    /// is what generalised it.</b> A lost run with no hearts left offers a video, and the same
    /// argument applies twice over: the reward used to be a caption change on the button of an
    /// explanatory panel the player had not asked to see, at the one moment in a session when
    /// the only thing they want is to be back on the board. What differs between the two
    /// callers is a title, a colour, how loud the ending is and where COLLECT leads —
    /// <see cref="TitleKey"/>, <see cref="Tint"/>, <see cref="Loud"/> and
    /// <see cref="Collected"/> — and nothing else, which is why there is one of these rather
    /// than two.
    /// </para>
    /// <para>
    /// <b>It is raised through <c>Flow.Modal</c> by a caller that may already be gone</b>, and
    /// that is deliberate: a player who backgrounds the app during a video can come back to a
    /// different screen entirely, and the prize is theirs either way. Nothing here reaches back
    /// into whoever asked for it — the way onward is the <see cref="Collected"/> callback, and
    /// it fires however the panel ended.
    /// </para>
    /// <para>
    /// <b>Nothing here is load-bearing, and that is the design.</b> The grant is the server's
    /// (invariant 10d): <c>RewardedAds.Redeem</c> has already asked for it by the time this
    /// panel exists, and LevelPlay's signed callback is what actually pays. So COLLECT buys
    /// nothing, closing early costs nothing, and a player who force-quits during the confetti
    /// is paid exactly the same as one who taps the button — the panel is a picture of a
    /// transaction that has already happened. That is why the button appears with the tokens
    /// rather than after them, why the back key works from the first frame, and why the
    /// cascade degrades to an ordinary close wherever there is nothing to fly to.
    /// </para>
    /// <para>
    /// <b>The figure is the wheel's, not the drop's.</b> They are the same number, arrived at
    /// independently — the phone from <c>BonusWheel.SliceAt</c> and the server from the same
    /// arithmetic on the callback — and the wheel's is the one printed because the drop is the
    /// flat placement amount, which has no opinion about multipliers.
    /// </para>
    /// </summary>
    public sealed class PrizeOverlay : ModalView
    {
        /// <summary>
        /// What was won: the kind the placement pays, and the multiplied amount.
        ///
        /// A property rather than a field, like <c>ShopGrantOverlay.Grant</c> and
        /// <c>DefeatOverlay.Run</c>: neither of the two payloads here is a serializable type, and
        /// a public field of one is a compile-time warning about an Inspector slot that nothing
        /// was ever going to fill — every one of these is set through <c>Flow.Modal</c>'s
        /// configure step.
        /// </summary>
        public ChestDrop Drop { get; set; }

        /// <summary>
        /// What the ribbon says. Written out by each caller rather than built from the
        /// placement, so the loc gate can see every key — a concatenated one is invisible to
        /// the scanner and ships missing (invariant 6).
        ///
        /// <para>
        /// There is no default, and a panel raised without one dismisses itself rather than
        /// drawing a ribbon with a raw key in it. Both callers live in this assembly, so the
        /// omission is a compile away from being noticed and never reaches a player.
        /// </para>
        /// </summary>
        public string TitleKey;

        /// <summary>
        /// The celebration's colour: the winning wedge's, or the resource's own.
        ///
        /// It is the caller's rather than <c>RewardArt.Tint(Drop.Kind)</c>'s, because the wheel
        /// pays credits in the colour of the slice it stopped on and that is the whole point of
        /// the wheel. Everything else hands over its resource's own tint and the two agree.
        /// </summary>
        public Color Tint = Pal.Gold;

        /// <summary>
        /// How loud the ending is: confetti and a fuller spark burst.
        ///
        /// <para>
        /// Not spent on every prize, which is the victory panel's rule for its star row —
        /// spending it on most of them marks out none. The wheel raises it for a slice that
        /// beat the flat offer; the heart refill raises it because being handed the game back
        /// after being stopped is the moment it exists for.
        /// </para>
        /// </summary>
        public bool Loud;

        /// <summary>The loudest ending there is. The wheel's top slice, and nothing else yet.</summary>
        public bool Loudest;

        /// <summary>
        /// Where the panel leads once the prize has been taken, or null to simply close.
        ///
        /// <para>
        /// <b>Raised exactly once, however the panel ended</b> — COLLECT, the back key, or the
        /// screen underneath being torn down with this still open. That completeness is the
        /// whole point of it and it is <c>AdOfferOverlay.Report</c>'s lesson: a panel with
        /// several exits reports through none of them reliably, so the safe outcome goes on the
        /// destroy and the one exception is declared (see <c>Onward</c>, which an ordinary close
        /// runs early so the panel underneath leaves with this one).
        /// </para>
        /// <para>
        /// It matters most where it is least visible. The heart refill leaves the panel
        /// underneath stale — a defeat screen saying "you are out of hearts" over a wallet that
        /// now holds two — so a dismissal has to lead onward exactly as a collect does. A
        /// callback wired to the button alone would leave that player looking at a lie.
        /// </para>
        /// </summary>
        public Action Collected;

        /// <summary>
        /// The pill snapshot taken before the reward was redeemed, handed over by the wheel.
        ///
        /// May be null — a panel raised with nothing to fly to still celebrates and still
        /// closes. See <see cref="RewardFlight"/> for why it has to be taken before the grant
        /// rather than derived at collect time.
        /// </summary>
        public RewardFlight Flight { get; set; }

        /// <summary>
        /// The rhythm, and it is the design.
        ///
        /// <para>
        /// <see cref="ArriveAt"/> is the beat the panel has to itself before the coin lands in
        /// it, so the shockwave breaks against something that already exists.
        /// <see cref="PayoutAt"/> is late enough that the impact has been seen and early enough
        /// that nobody is waiting; it is also when the button arrives, so there is never as
        /// much as a second in which this panel cannot be left.
        /// </para>
        /// </summary>
        const float ArriveAt = .12f, PayoutAt = .54f;

        RectTransform _coin;
        Payout _chip;
        Btn _collect;
        bool _collecting;

        protected override void Build()
        {
            // Nothing to hand over, or nothing to head it with. Reachable only if a caller
            // raised this with an empty drop or forgot its title, and a celebration of nothing
            // is worse than no celebration — as is a ribbon carrying a raw loc key.
            //
            // Dismissed rather than closed, so Collected still fires from OnDestroy: a caller
            // that was going to be led somewhere is led there whatever went wrong here.
            if (!Drop.IsValid || string.IsNullOrEmpty(TitleKey)) { Flow.Dismiss(this); return; }

            var stack = PrizePanel.Of();

            // Never dismissed by a stray tap on the scrim: this is the one screen that says
            // what a video was worth, and one flicked away by a thumb landing anywhere is one a
            // player can miss entirely. The back key still works throughout — see OnBack.
            var panel = MakePanel(new Vector2(PrizePanel.Width, stack.Height),
                                  Loc.Get(TitleKey), dismissOnScrim: false);

            BuildRays(panel, stack.CoinCentre);
            UIKit.Halo(panel, Tint, 640f, .26f,
                       new Vector2(0f, stack.Height * .5f - stack.CoinCentre));

            _coin = UIKit.Box("Coin", panel, Vector2.one * stack.CoinSize, new Vector2(.5f, 1f),
                              new Vector2(0f, -stack.CoinCentre));

            var face = UIKit.Img("Face", _coin, RewardArt.Icon(Drop.Kind), Color.white,
                                 Vector2.one * stack.CoinSize, new Vector2(.5f, .5f), Vector2.zero);
            face.preserveAspect = true;
            face.raycastTarget = false;

            // Credits have no sprite of their own — they are the spinning flipbook — and this
            // is also the call that covers the frames not having arrived: an Image with no
            // sprite is a white rectangle, not a blank. See RewardArt.Glyph.
            RewardArt.Glyph(face, Drop.Kind, 12f);

            // Enters after the panel is standing, so it reads as the prize being handed over
            // rather than as part of the furniture.
            _coin.localScale = Vector3.zero;

            BuildChip(panel, stack.AmountCentre);

            // Always COLLECT, which is the one place this panel parts company with
            // <c>ShopGrantOverlay</c> — that one says "Lovely" wherever the currency has no pill
            // to fly into, on the reasoning that calling it collect promises an animation the
            // player then does not see. The reasoning does not survive here: this panel is
            // raised over the victory screen, which carries no pills at all, so the rule would
            // mean the word never appears in the path the feature exists for. And unlike a
            // receipt, what is being collected is the point of the panel rather than a note
            // about a payment made elsewhere. The flight still happens wherever there is
            // somewhere to fly to; where there is not, the button closes and the prize is
            // already banked either way.
            _collect = UIKit.TextButton("Collect", panel, "btn_green",
                                        Loc.Get("ui.daily.collect"), 42,
                                        new Vector2(520f, PrizePanel.ButtonHeight),
                                        new Vector2(.5f, 1f), new Vector2(0f, -stack.ButtonCentre),
                                        OnCollect);

            // Sized once, deterministically, rather than left to Unity's best-fit — which
            // concedes the line before it concedes the size, so a long caption folds in half on
            // a pill instead of shrinking. See UIKit.OneLine.
            UIKit.OneLine(_collect, 24);

            _collect.transform.localScale = Vector3.zero;

            Schedule();
        }

        /// <summary>
        /// Light behind the prize, turning slowly. The chest opens on this figure and so does
        /// the shop's receipt, which is the point: this is the third place in the game where
        /// something is handed over, and a player should recognise it as one.
        /// </summary>
        void BuildRays(RectTransform panel, float coinY)
        {
            var host = UIKit.Box("Rays", panel, Vector2.one * 480f, new Vector2(.5f, 1f),
                                 new Vector2(0f, -coinY));
            host.SetAsFirstSibling();

            for (int i = 0; i < 8; i++)
            {
                var ray = UIKit.Img("r" + i, host, Art.SoftCapsule(40, 200), Pal.A(Tint, .13f),
                                    new Vector2(40f, 560f), new Vector2(.5f, .5f), Vector2.zero);
                ray.raycastTarget = false;
                ray.transform.localRotation = Quaternion.Euler(0, 0, i * 22.5f);
            }

            Tween.Run(18f, Ease.Linear,
                      t => { if (host) host.localRotation = Quaternion.Euler(0, 0, t * 360f); },
                      host.gameObject, "spin").Loop(-1, false);
        }

        /// <summary>
        /// The figure, drawn through <c>RewardArt</c> so the colour and the token are the ones
        /// the chest and the shop already use for the same currency.
        ///
        /// <para>
        /// <b>No glyph on the chip</b> (<c>glyphSize: 0</c>), which is the one way this differs
        /// from every other payout in the game. Everywhere else the chip's icon is what names
        /// the currency; here the prize is already drawn three times the size directly above it,
        /// so a second coin beside the number says the same thing twice and shoulders the figure
        /// off the panel's centre line to do it. The tokens land on the number instead — see
        /// <c>Payout</c>'s seat.
        /// </para>
        /// </summary>
        void BuildChip(RectTransform panel, float y)
        {
            RewardArt.Token(Drop.Kind, out var token, out var tokenTint);

            _chip = Payout.Chip("Prize", panel, new Vector2(.5f, 1f), new Vector2(0f, -y),
                                null, RewardArt.Tint(Drop.Kind),
                                n => "+" + Compact.Number(n), Drop.Amount,
                                token, tokenTint, "coin", glyphSize: 0f);
        }

        // ------------------------------------------------------------- the beats
        /// <summary>
        /// The ceremony, scheduled off the panel rather than chained through each other's
        /// callbacks, so retiming the middle does not mean re-deriving every delay after it.
        /// </summary>
        void Schedule()
        {
            // A wash in the slice's own colour at the instant the panel lands. Peaked low: this
            // sits on top of a victory panel, and a white-out over one is a transition rather
            // than a flourish.
            Flow.Flash(Pal.A(Tint, 1f), Loudest ? .30f : .18f, .52f);

            Tween.After(ArriveAt, Arrive, this);

            Tween.After(PayoutAt, () =>
            {
                if (this == null) return;

                if (_chip != null) _chip.Play(_coin);
            }, this);

            // With the tokens, not with their landing: a way out that arrives at the end of the
            // ceremony is a ceremony the player has to sit through.
            Tween.After(PayoutAt, () =>
            {
                if (this == null || !_collect) return;

                Tween.Pop(_collect.transform, 0f, .5f).OnDone(() =>
                {
                    if (!_collect) return;

                    // The pop is what the button's resting scale becomes: Setup latched a scale
                    // of zero when it was built hidden, and a press that returns to zero is a
                    // button that disappears when it is tapped.
                    _collect.Rehome();
                    Sheen.Attach((RectTransform)_collect.transform, 3.2f);
                });
            }, this);

            Tween.After(PayoutAt + (_chip != null ? _chip.Duration : 0f), Payoff, this);
        }

        /// <summary>The prize lands: a spring, two shockwaves and a burst.</summary>
        void Arrive()
        {
            if (_coin == null) return;

            Tween.Scale(_coin, 1f, .42f, Ease.OutBack).OnDone(() =>
            {
                // Started only once the entrance is over, because both write localScale and two
                // tweens on one value fight for it every frame they share.
                if (_coin) Tween.Breathe(_coin, .035f, 3.1f);
            });

            Shockwave(0f, 1.9f, .52f, .80f);
            Shockwave(.10f, 2.6f, .62f, .42f);

            Burst.Sparks(_coin, Vector2.zero, Tint, Loud ? 24 : 16, 400f, 30f, .74f);
            Audio.Sfx("chest", .58f);
        }

        /// <summary>
        /// A ring breaking outwards from the prize.
        ///
        /// The starting alpha is captured rather than the live colour being scaled down each
        /// frame — scaling the live value compounds, so the fade would depend on how many frames
        /// it got. <c>Payout.Ping</c> records the same trap.
        /// </summary>
        void Shockwave(float delay, float to, float dur, float alpha)
        {
            if (_coin == null) return;

            var ring = UIKit.Img("Wave", _coin, Art.Ring(128, 8f),
                                 Pal.A(Pal.Lift(Tint, .4f), alpha),
                                 Vector2.one * PrizePanel.CoinSize * .9f,
                                 new Vector2(.5f, .5f), Vector2.zero);
            ring.raycastTarget = false;

            var rt = (RectTransform)ring.transform;

            Tween.Run(dur, Ease.OutQuad, t =>
            {
                if (!rt) return;
                rt.localScale = Vector3.one * Mathf.Lerp(.35f, to, t);
                var c = ring.color; c.a = alpha * (1f - t); ring.color = c;
            }, ring).Delay(delay)
             .OnDone(() => { if (ring) Destroy(ring.gameObject); });
        }

        /// <summary>
        /// The figure has finished climbing. Everything loud is here and nowhere else.
        ///
        /// <para>
        /// The confetti is for a slice that is genuinely worth it, which is the victory panel's
        /// rule for its star row: spending it on every spin spends it on most spins and marks
        /// out none. And there is no haptic anywhere on this panel — <c>Handheld.Vibrate</c> is
        /// one fixed-length pulse on Android, so it cannot be made lighter for an event a player
        /// meets several times a session.
        /// </para>
        /// </summary>
        void Payoff()
        {
            if (this == null || _collecting) return;

            if (Loud) Burst.Confetti(Content, Loudest ? 54 : 34);

            Audio.Sfx("win", .55f);

            if (_coin) { _coin.localScale = Vector3.one; Tween.Punch(_coin, .12f, .38f); }
        }

        // ------------------------------------------------------------- collecting
        /// <summary>
        /// The player took the prize. Where there is a balance row underneath, the chip empties
        /// into it — the same cascade the chest and the shop's receipt use.
        ///
        /// Nothing about the reward depends on this: it is banked before this panel exists, and
        /// a player who never presses the button is paid exactly the same.
        /// </summary>
        void OnCollect()
        {
            if (_collecting) return;

            var from = _chip != null ? _chip.Root : null;

            // Asked before the latch, so a panel with nowhere to fly to is still an ordinary
            // close — including its sound and its scale-out, which the cascade does not use.
            if (Flight == null || from == null || !Flight.Add(Drop, from))
            {
                // Led onward at the top of the close rather than at the end of it, so the panel
                // this was raised over goes at the same time. Half a second of a defeat screen
                // still saying "you are out of hearts", over a wallet that now holds two, is the
                // one frame of this feature a player could read as a bug — and COLLECT that
                // visibly does nothing for a beat reads as a tap that missed.
                Onward();
                Close();
                return;
            }

            _collecting = true;
            Fly(from);
        }

        /// <summary>
        /// Clears the panel out of the way and throws the prize at what is underneath.
        ///
        /// The chip is lifted out of the panel first — it is the thing the tokens come out of,
        /// so it has to outlive its own parent by a beat, while everything else here is chrome
        /// the moment the figure has been read. <c>SetParent</c> keeps its world position, so
        /// nothing moves as it changes hands. The scrim stops taking taps as well as fading: one
        /// at zero alpha still swallows everything aimed at what is now visible through it.
        /// </summary>
        void Fly(RectTransform from)
        {
            if (_collect) _collect.Interactable = false;
            if (from) from.SetParent(Content, true);

            if (Scrim)
            {
                Scrim.raycastTarget = false;
                Tween.Fade(Scrim, 0f, RewardFlight.ClearAt);
            }

            if (Panel)
            {
                var group = UIKit.Group(Panel);
                group.interactable = false;
                group.blocksRaycasts = false;
                Tween.Fade(group, 0f, RewardFlight.ClearAt * .8f);
            }

            Flight.Play(Content, () => { if (this) Flow.Dismiss(this); });
        }

        /// <summary>
        /// Swallowed once the payout has started, for <c>ChestOverlay.OnBack</c>'s reason: the
        /// prize is banked either way, but <see cref="ModalView.Close"/> fades the whole content
        /// group and the tokens are in it, so the back key would delete the animation mid-flight
        /// and leave the balance row rewound to its old figure.
        ///
        /// Before that it closes, which is what gives this panel a way out from the first frame
        /// — the scrim does not dismiss it and the button takes half a second to arrive.
        /// </summary>
        public override bool OnBack()
        {
            if (_collecting) return true;
            Close();
            return true;
        }

        // ------------------------------------------------------------- leading onward
        bool _reported;

        /// <summary>
        /// Hands the player on, exactly once, however this panel ended.
        ///
        /// <para>
        /// <c>AdOfferOverlay.Report</c>'s latch and for its reason: this panel has three exits —
        /// COLLECT, the back key, and the screen underneath being destroyed with it open — and a
        /// caller that hears twice is as broken as one that never hears. Putting it on the
        /// destroy is what makes "exactly one, always" something the type enforces rather than
        /// something the exits agree about.
        /// </para>
        /// <para>
        /// Swallowed, because this runs during teardown: a caller that throws would leave the
        /// rest of the destroy chain unrun.
        /// </para>
        /// </summary>
        void OnDestroy() => Onward();

        /// <summary>
        /// Runs the way onward, exactly once.
        ///
        /// <para>
        /// Called at the top of an ordinary close so the panel underneath leaves with this one,
        /// and from <see cref="OnDestroy"/> for every other ending — the back key, and the screen
        /// beneath being torn down with this open. A payout in flight is the one case that waits:
        /// its tokens are landing on a readout that has to still be there, so the destroy is the
        /// honest moment for it and the cascade's own callback is what gets there.
        /// </para>
        /// </summary>
        void Onward()
        {
            if (_reported) return;
            _reported = true;

            var onward = Collected;
            Collected = null;

            // Swallowed, because this can run during teardown: a caller that throws would leave
            // the rest of the destroy chain unrun.
            try { onward?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
