using System;
using GlimmerGrove.Localization;
using GlimmerGrove.Store;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Confirms buying a limited-time shop deal (invariant 60): what it pays, how long it has
    /// left, and the gem price on the one key that charges it.
    ///
    /// <para>
    /// <b><c>ShopSupplyOverlay</c>'s shape and its reason.</b> A gem purchase has no store sheet
    /// and no authentication, so this panel is the only thing between a thumb on the shop's band
    /// and the price. It leads with what arrives and puts the cost on the key, and it re-checks
    /// everything at the tap rather than trusting the state it was built from: the deal can end,
    /// another phone can buy it, or a sync can move the balance while the panel is open.
    /// </para>
    /// </summary>
    public sealed class DealOverlay : ModalView
    {
        /// <summary>The deal, set by the caller's configure callback before <c>Build</c> runs.</summary>
        public ShopDeal Deal { get; set; }

        /// <summary>Run after the panel closes on a short balance: the shop shows its gem shelf.</summary>
        public Action ShortOfGems { get; set; }

        /// <summary>Run after the panel closes on a purchase: the shop flies the coins home.</summary>
        public Action<ShopDeal> Bought { get; set; }

        const float PanelW = 880f;
        const float PanelH = 1040f;
        const float ArtY = 300f;      // 300x300 -> 150..450
        const float AmountY = 496f;   // 700x76  -> 458..534
        const float NoteY = 600f;     // 700x96  -> 552..648
        const float NoteH = 96f;
        const float ClockY = 690f;    // 700x52  -> 664..716, clear of the key at 763

        /// <summary>Ink for the light parchment panel, <c>ShopSupplyOverlay.Ink</c>'s reason.</summary>
        static readonly Color Ink = new Color(.36f, .25f, .18f);
        static readonly Color Amber = new Color(.62f, .34f, .08f);

        Text _clock;
        float _next;

        protected override void Build()
        {
            if (Deal == null) { Flow.Dismiss(this); return; }

            var panel = MakePanel(new Vector2(PanelW, PanelH), Loc.Get("ui.deal.title"));

            var art = UIKit.Img("Art", panel, Art.S("Ui/Shop/coins_3"), Color.white, new Vector2(300f, 300f),
                                new Vector2(.5f, 1f), new Vector2(0f, -ArtY));
            art.preserveAspect = true;
            art.raycastTarget = false;
            art.enabled = art.sprite != null;

            // What arrives: the coin and the number, the one thing on the panel being decided.
            var row = UIKit.Box("AmountRow", panel, new Vector2(700f, 76f), new Vector2(.5f, 1f), new Vector2(0f, -AmountY));
            var coin = UIKit.Img("Coin", row, null, Color.white, new Vector2(64f, 64f), new Vector2(.5f, .5f),
                                 new Vector2(-170f, 0f));
            coin.preserveAspect = true;
            Flipbook.Attach(coin, "Ui/Coin", 11f);
            UIKit.Shrinkable(
                UIKit.Titled("Amount", row, Deal.Credits.ToString("N0"),
                             60, Amber, TextAnchor.MiddleLeft, new Vector2(460f, 76f), new Vector2(.5f, .5f),
                             new Vector2(100f, 0f), outline: 0f, shadow: 2f), 30);

            UIKit.Shrinkable(
                UIKit.Titled("Note", panel, Loc.Get("ui.deal.note"), 28, Ink, TextAnchor.MiddleCenter,
                             new Vector2(700f, NoteH), new Vector2(.5f, 1f), new Vector2(0f, -NoteY),
                             outline: 0f, shadow: 0f, wrap: true), 19);

            _clock = UIKit.Shrinkable(
                UIKit.Titled("Clock", panel, string.Empty, 32, Amber, TextAnchor.MiddleCenter,
                             new Vector2(700f, 52f), new Vector2(.5f, 1f), new Vector2(0f, -ClockY),
                             outline: 0f, shadow: 0f), 18);
            PaintClock();

            UIKit.TextButton("Buy", panel, Skins.Gem,
                             Loc.Format("ui.shop.gem_price", Compact.Number(Deal.Gems)), 36,
                             new Vector2(560f, 118f), new Vector2(.5f, 0f), new Vector2(0f, 210f),
                             Confirm, "ic_gem");

            UIKit.TextButton("Cancel", panel, "btn_red", Loc.Get("ui.common.cancel"), 30,
                             new Vector2(360f, 92f), new Vector2(.5f, 0f), new Vector2(0f, 96f),
                             () => Close());
        }

        void Update()
        {
            if (IsLeaving || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + .5f;
            PaintClock();
        }

        void PaintClock()
        {
            if (!_clock || Deal == null) return;
            long left = Deal.SecondsLeft(GameClock.NowUnix());
            _clock.text = left > 0L
                ? Loc.Format("ui.event.ends_in", Profile.LongCountdown(left))
                : Loc.Get("ui.deal.ended");
        }

        public override bool OnBack() { Close(); return true; }

        void Confirm()
        {
            // Asked before the ledger, because a deal is the one gem purchase the server must see
            // promptly: bought offline it would be judged at the next sync, possibly after the
            // window closed, and taken back with the coins already spent.
            if (Net.Offline)
            {
                Scenery.Toast(Content, Loc.Get("ui.deal.needs_connection"), Pal.Sun, 2.6f);
                return;
            }

            var deal = Deal;
            switch (DealLedger.TryBuy(deal))
            {
                case DealBuy.Bought:
                    // A sound and no haptic: `ShopSupplyOverlay.Confirm`'s rule.
                    Audio.Sfx("coin", .6f);
                    Close(() => Bought?.Invoke(deal), quiet: true);
                    return;

                case DealBuy.TooPoor:
                    Scenery.Toast(Content, Loc.Get("ui.shop.need_gems"), Pal.Bloom);
                    Close(() => ShortOfGems?.Invoke());
                    return;

                case DealBuy.Ended:
                    Scenery.Toast(Content, Loc.Get("ui.deal.ended"), Pal.Sun, 2.6f);
                    Close();
                    return;

                case DealBuy.AlreadyBought:
                    Scenery.Toast(Content, Loc.Get("ui.deal.owned"), Pal.Sun, 2.6f);
                    Close();
                    return;

                case DealBuy.Unavailable:
                    Scenery.Toast(Content, Loc.Get("ui.deal.needs_connection"), Pal.Sun, 2.6f);
                    return;

                default:
                    // Every outcome is named above (44e); one added later is said as a closed deal
                    // rather than charged.
                    Scenery.Toast(Content, Loc.Get("ui.deal.ended"), Pal.Sun, 2.6f);
                    Close();
                    return;
            }
        }
    }
}
