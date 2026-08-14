using System;
using GlimmerGrove.Ads;
using GlimmerGrove.Daily;
using GlimmerGrove.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The panel behind every "watch a video for this" offer in the game.
    ///
    /// <para>
    /// One overlay for both entry points — the defeat screen's heart refill and the home
    /// screen's coin pill — because they are the same transaction seen from two moods, and
    /// the honest states below are the whole substance of it. Two panels would be two
    /// places to get "no fill" wrong.
    /// </para>
    /// <para>
    /// It is built around saying the true thing. A rewarded ad has five ways of not
    /// happening and a player meets all of them: the network has nothing loaded, the day's
    /// allowance is spent, another ad was watched a minute ago, hearts are already full, or
    /// there is no account to pay coins into yet. A panel that renders those as one greyed
    /// button teaches people the feature is broken, and they stop looking at it — which
    /// costs far more than the ad it failed to show.
    /// </para>
    /// </summary>
    public sealed class AdOfferOverlay : ModalView
    {
        /// <summary>Which placement is being offered. Set by the caller before Build runs.</summary>
        public string PlacementId = AdPlacement.HeartRefill;

        /// <summary>Raised after a reward actually lands, so the screen behind can repaint.</summary>
        public Action Rewarded;

        Btn _watch;
        Text _status;
        Text _reward;
        RectTransform _card;
        bool _watching;
        bool _paid;

        protected override void Build()
        {
            MakePanel(new Vector2(880f, 820f), Loc.Get(TitleKey(PlacementId)));

            BuildRewardCard();

            _status = UIKit.Titled("Status", Panel, string.Empty, 30,
                                   new Color(.36f, .25f, .18f), TextAnchor.UpperCenter,
                                   new Vector2(680f, 110f), new Vector2(.5f, 1f), new Vector2(0f, -516f),
                                   outline: 0f, shadow: 0f, wrap: true);

            _watch = UIKit.TextButton("Watch", Panel, "btn_green", Loc.Get("ui.ads.watch"), 46,
                                      new Vector2(600f, 140f), new Vector2(.5f, 1f), new Vector2(0f, -628f),
                                      OnWatch);

            UIKit.TextButton("Close", Panel, "btn_blue", Loc.Get("ui.common.not_now"), 40,
                             new Vector2(600f, 116f), new Vector2(.5f, 1f), new Vector2(0f, -760f),
                             () => Close());

            RewardedAds.Changed += Repaint;
            Repaint();
        }

        void OnDestroy() => RewardedAds.Changed -= Repaint;

        /// <summary>
        /// Written out rather than built from the placement id, so the loc gate can see
        /// every key. A concatenated key is invisible to the scanner and ships missing.
        /// </summary>
        static string TitleKey(string placementId)
            => placementId == AdPlacement.CoinBonus ? "ui.ads.coins_title" : "ui.ads.hearts_title";

        // ------------------------------------------------------------- the prize
        /// <summary>
        /// What the ad pays, drawn the way a chest draws a reward so the two read as the
        /// same kind of thing — which they are.
        /// </summary>
        void BuildRewardCard()
        {
            var offer = RewardedAds.Table.Offer(PlacementId);
            var kind = offer.IsValid ? offer.Kind : ChestDropKind.Credits;
            var tint = RewardArt.Tint(kind);

            var card = UIKit.Img("Card", Panel, Art.Round(26), new Color(.04f, .09f, .12f, .82f),
                                 new Vector2(300f, 300f), new Vector2(.5f, 1f), new Vector2(0f, -300f));
            _card = (RectTransform)card.transform;

            var edge = UIKit.Img("Edge", card.transform, Art.RoundOutline(26, 3f), Pal.A(tint, .55f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            UIKit.Halo(card.transform, tint, 330f, .30f);

            var icon = UIKit.Img("Icon", card.transform, RewardArt.Icon(kind), Color.white,
                                 new Vector2(128f, 128f), new Vector2(.5f, 1f), new Vector2(0f, -76f));
            icon.preserveAspect = true;
            if (kind == ChestDropKind.Credits) Flipbook.Attach(icon, "Ui/Coin", 11f);
            Tween.Breathe(icon.transform, .05f, 2.2f);

            _reward = UIKit.Titled("Amount", card.transform,
                                   offer.IsValid ? $"+{offer.Amount}" : string.Empty, 54, Pal.Cream,
                                   TextAnchor.MiddleCenter, new Vector2(260f, 66f),
                                   new Vector2(.5f, 1f), new Vector2(0f, -190f), outline: 3f, shadow: 3f);

            UIKit.Titled("Kind", card.transform, RewardArt.Name(kind), 30, Pal.A(tint, .95f),
                         TextAnchor.MiddleCenter, new Vector2(260f, 46f),
                         new Vector2(.5f, 1f), new Vector2(0f, -246f), outline: 2f, shadow: 2f);
        }

        // ------------------------------------------------------------- painting
        /// <summary>
        /// Kept live rather than painted once, because two of the reasons an offer is
        /// unavailable resolve by themselves — a cooldown runs out, and fill arrives — and
        /// a panel that had to be closed and reopened to notice is a panel players learn
        /// to distrust.
        /// </summary>
        void Update()
        {
            if (!_paid && !_watching) Repaint();
        }

        void Repaint()
        {
            if (_paid || _watching || _status == null || _watch == null) return;

            var status = RewardedAds.Status(PlacementId);

            _watch.Interactable = status.CanShow;
            _status.text = Explain(status);
        }

        /// <summary>
        /// The sentence under the button. Every branch names the real reason and, where
        /// there is one, when it stops being true.
        /// </summary>
        static string Explain(AdOfferStatus status)
        {
            switch (status.State)
            {
                case AdOfferState.Ready:
                    return Loc.Get("ui.ads.ready");

                case AdOfferState.NotLoaded:
                    return Loc.Get("ui.ads.loading");

                case AdOfferState.CoolingDown:
                    return string.Format(Loc.Get("ui.ads.cooling"),
                                         Profile.Countdown(status.SecondsRemaining));

                case AdOfferState.CapReached:
                    return string.Format(Loc.Get("ui.ads.cap_reached"),
                                         Profile.Countdown(status.SecondsRemaining));

                case AdOfferState.NothingToGain:
                    return Loc.Get("ui.ads.hearts_full");

                case AdOfferState.NeedsAccount:
                    return Loc.Get("ui.ads.needs_account");

                default:
                    return Loc.Get("ui.ads.unavailable");
            }
        }

        // ------------------------------------------------------------- watching
        void OnWatch()
        {
            if (_watching || _paid) return;
            if (!RewardedAds.CanOffer(PlacementId)) { Repaint(); return; }

            _watching = true;
            _watch.Interactable = false;
            _status.text = Loc.Get("ui.ads.opening");

            Show();
        }

        /// <summary>
        /// Shows the ad and pays for it.
        ///
        /// <para>
        /// The impression is generated here, before the SDK is asked for anything, because
        /// the nonce inside it has to reach the ad network as a custom parameter — it is
        /// what the network's verification callback will name, and therefore what the
        /// server will match a claim against. See <see cref="AdImpression"/>.
        /// </para>
        /// </summary>
        async void Show()
        {
            var impression = AdImpression.New(PlacementId);

            try
            {
                var result = await RewardedAds.Provider.ShowAsync(impression);

                // The overlay can be gone by now — a player who backgrounds the app during
                // a video may come back to a different screen entirely. The reward is still
                // banked, because Redeem does not touch the UI.
                var drop = RewardedAds.Redeem(result);

                if (this == null) return;

                if (drop.IsValid) { Paid(drop); return; }

                _watching = false;
                _status.text = Refusal(result.Outcome);
                Repaint();
            }
            catch (Exception e)
            {
                // async void swallows exceptions, and an ad that throws must not leave the
                // panel stuck on "opening" with a dead button.
                Debug.LogException(e);

                if (this == null) return;

                _watching = false;
                _status.text = Loc.Get("ui.ads.failed");
                Repaint();
            }
        }

        /// <summary>Why nothing was paid, in the player's terms rather than the SDK's.</summary>
        static string Refusal(AdOutcome outcome)
        {
            switch (outcome)
            {
                case AdOutcome.Dismissed: return Loc.Get("ui.ads.dismissed");
                case AdOutcome.NoFill: return Loc.Get("ui.ads.no_fill");
                case AdOutcome.Unavailable: return Loc.Get("ui.ads.unavailable");
                default: return Loc.Get("ui.ads.failed");
            }
        }

        /// <summary>
        /// The reward landed. Small ceremony, then the button becomes the way out.
        ///
        /// Deliberately quieter than a chest opening. This is a transaction the player will
        /// make many times, and a five-second animation that was delightful the first time
        /// is an obstacle by the fourth.
        /// </summary>
        void Paid(ChestDrop drop)
        {
            _paid = true;
            _watching = false;

            if (_reward) _reward.text = $"+{drop.Amount}";
            if (_card) Tween.Pop(_card, 0f, .34f);

            if (_status) _status.text = Loc.Get("ui.ads.thanks");

            if (_watch)
            {
                _watch.Interactable = true;
                _watch.Setup(() => Close(() => Rewarded?.Invoke()));

                var label = _watch.GetComponentInChildren<Text>();
                if (label) label.text = Loc.Get("ui.daily.collect");
            }

            Audio.Sfx("win", .6f);
            Haptic.Tap();
            Burst.Sparks(_card, Vector2.zero, RewardArt.Tint(drop.Kind), 20, 420f, 30f, .8f);
        }
    }
}
