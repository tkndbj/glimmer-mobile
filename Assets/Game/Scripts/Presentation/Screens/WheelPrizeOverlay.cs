using GlimmerGrove.Daily;
using GlimmerGrove.Layout;
using GlimmerGrove.Localization;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// What the wheel's video paid, handed over.
    ///
    /// <para>
    /// <b>A panel of its own rather than a third state of the wheel.</b> The wheel is a
    /// question — spin, see the multiplier, decide whether it is worth thirty seconds — and by
    /// the time this is raised the question has been answered and the video watched. Keeping
    /// the two on one panel meant the payoff was a caption change on the button that had just
    /// asked for the ad, under a wheel whose job was finished: the biggest moment in the
    /// placement drawn as the smallest change on the screen. So the wheel steps out and this
    /// arrives, which is the shape <c>ShopGrantOverlay</c> and <c>ChestOverlay</c> already use
    /// for the other two places currency is handed over — one gesture the player recognises
    /// wherever it happens.
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
    public sealed class WheelPrizeOverlay : ModalView
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

        /// <summary>The winning slice's colour, so the celebration matches the wedge it came from.</summary>
        public Color Tint = Pal.Gold;

        /// <summary>True when the slice paid more than the flat offer would have.</summary>
        public bool IsBonus;

        /// <summary>True when it was the best slice on the wheel. The loudest ending, once.</summary>
        public bool IsTop;

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
            // Nothing to hand over. Reachable only if a caller raised this with an empty drop,
            // and a celebration of nothing is worse than no celebration.
            if (!Drop.IsValid) { Flow.Dismiss(this); return; }

            var stack = WheelPrizePanel.Of();

            // Never dismissed by a stray tap on the scrim: this is the one screen that says
            // what a video was worth, and one flicked away by a thumb landing anywhere is one a
            // player can miss entirely. The back key still works throughout — see OnBack.
            var panel = MakePanel(new Vector2(WheelPrizePanel.Width, stack.Height),
                                  Loc.Get("ui.wheel.prize_title"), dismissOnScrim: false);

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
                                        new Vector2(520f, WheelPrizePanel.ButtonHeight),
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
            Flow.Flash(Pal.A(Tint, 1f), IsTop ? .30f : .18f, .52f);

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

            Burst.Sparks(_coin, Vector2.zero, Tint, IsBonus ? 24 : 16, 400f, 30f, .74f);
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
                                 Vector2.one * WheelPrizePanel.CoinSize * .9f,
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

            if (IsBonus) Burst.Confetti(Content, IsTop ? 54 : 34);

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
            if (Flight == null || from == null || !Flight.Add(Drop, from)) { Close(); return; }

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

        /// <summary>
        /// Raises the celebration for a slice that has just been paid for.
        ///
        /// <para>
        /// Static and taking everything it needs, because the wheel is usually gone by the time
        /// this runs — a player who backgrounds the app during a video can come back to a
        /// different screen entirely, and the prize is theirs either way. The panel is the only
        /// thing that tells them so.
        /// </para>
        /// </summary>
        public static void Celebrate(ChestDrop drop, Color tint, bool isBonus, bool isTop,
                                     RewardFlight flight)
        {
            if (!drop.IsValid) return;

            Flow.Modal<WheelPrizeOverlay>(v =>
            {
                v.Drop = drop;
                v.Tint = tint;
                v.IsBonus = isBonus;
                v.IsTop = isTop;
                v.Flight = flight;
            });
        }
    }
}
