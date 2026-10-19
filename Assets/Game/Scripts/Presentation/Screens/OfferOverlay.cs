using System;
using GlimmerGrove.Localization;
using GlimmerGrove.Offers;
using GlimmerGrove.Store;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// <b>A personal offer</b> (invariant 64): one real-money pack of gems and coins, dealt to this
    /// player by the offer engine, with what it is worth over the shop's own prices, a real clock and
    /// the store's own price on the key.
    ///
    /// <para>
    /// <b>Two doors, one panel.</b> <c>OfferMoment</c> raises it the moment an offer is dealt; the
    /// shop's "for you" band raises it again for as long as the offer stands. Closing it is a "not
    /// now" (<see cref="OfferLedger.Dismissed"/>), which makes the next offer wait longer; the offer
    /// itself keeps standing in the shop until its clock runs out.
    /// </para>
    /// <para>
    /// <b>The money is the store's and the server's.</b> The key opens the store's own sheet
    /// (<see cref="StoreTap"/>), the server grants the product's fixed contents against the receipt,
    /// and the thank-you is raised by <c>Boot</c> for every screen at once - this panel closes itself
    /// once the purchase has landed. Nothing here can charge or grant anything.
    /// </para>
    /// <para>
    /// <b>It fits every screen it can be drawn on.</b> The deals sheet's frame
    /// (<see cref="VictoryFrame"/>) scales the whole panel down on a short screen, every caption is
    /// shrinkable inside a fixed box, and <c>Tools/render_offers.py --measure</c> renders every look
    /// in every language at the phone and tablet extremes and fails on a caption at its floor (19n).
    /// The geometry is <see cref="OfferLayout"/>'s, which that mirror copies.
    /// </para>
    /// </summary>
    public sealed class OfferOverlay : ModalView
    {
        /// <summary>The offer to show. Set by the caller's configure callback.</summary>
        public StandingOffer Offer { get; set; }

        /// <summary>True when the shop's band opened it rather than the moment dealing it.</summary>
        public bool FromShop { get; set; }

        Btn _key;
        Image _keyPlate;
        Text _timer;
        string _caption;
        float _nextTick;
        bool _leaving, _bought;

        protected override void Build()
        {
            var offer = Offer;
            if (offer == null || offer.Product == null) { Flow.Dismiss(this); return; }

            var product = offer.Product;
            var L = OfferLayout.Panel;

            Scrim = UIKit.Scrim(Content, .74f, null);
            var frame = VictoryFrame.Build(Content, L.Height, Loc.Get(OfferLooks.TitleKey(offer.Look)).Upper(), L.Width);
            Backing = frame.Backing;
            Panel = frame.Panel;

            // The line under the banner: why this, and why now.
            UIKit.Shrinkable(
                UIKit.Titled("Line", Panel, Loc.Get(OfferLooks.LineKey(offer.Look)), L.LineSize, Pal.Cream,
                             TextAnchor.MiddleCenter, new Vector2(L.LineW, L.LineH), new Vector2(.5f, 1f),
                             new Vector2(0f, -L.LineY), 3f, 2f), L.LineFloor);

            // The picture on its own light at the left, breathing; what it pays beside it, so the
            // panel is one row shorter than a picture over its figures (the owner, 2026-10-19).
            UIKit.Img("Glow", Panel, Art.Glow(128, 2.4f), new Color(1f, .82f, .38f, .55f),
                      new Vector2(L.GlowSize, L.GlowSize), new Vector2(.5f, 1f), new Vector2(L.ArtX, -L.RowY));
            var art = UIKit.Img("Art", Panel, Art.S(ShopArt.OfferPicture(product)), Color.white,
                                new Vector2(L.ArtSize, L.ArtSize), new Vector2(.5f, 1f), new Vector2(L.ArtX, -L.RowY));
            art.preserveAspect = true;
            art.enabled = art.sprite != null;
            Tween.Breathe(art.transform, .035f, 2.4f);

            // The seal over the picture's top-left: "+330%", derived (OfferValue).
            int extra = OfferValue.Extra(product, StoreRules.Catalog);
            if (extra > 0)
            {
                var seal = UIKit.Img("Seal", Panel, Art.S("Ui/" + Skins.Badge), Pal.Poppy,
                                     new Vector2(L.SealSize, L.SealSize), new Vector2(.5f, 1f),
                                     new Vector2(L.SealX, -L.SealY));
                seal.transform.localRotation = Quaternion.Euler(0f, 0f, 8f);
                var word = UIKit.Shrinkable(
                    UIKit.Titled("Word", seal.transform, Loc.Format("ui.offer.bonus", extra), L.SealWordSize, Color.white,
                                 TextAnchor.MiddleCenter, new Vector2(L.SealSize * .72f, L.SealSize * .5f),
                                 new Vector2(.5f, .5f), new Vector2(0f, 2f), 3f, 2f), L.SealWordFloor);
                word.transform.localRotation = Quaternion.Euler(0f, 0f, -8f);
                Tween.Breathe(seal.transform, .05f, 1.6f);
            }

            // The column at the right: the gems, big, with the shop's gem before them; the coins
            // under, bright; then ENDS IN over the time left, large and on the window itself.
            var row = UIKit.Box("Gems", Panel, new Vector2(L.ColumnW, L.AmountH), new Vector2(.5f, 1f),
                                new Vector2(L.ColumnX, -L.AmountY));
            var gems = UIKit.Shrinkable(
                UIKit.Titled("Amount", row, product.Gems.ToString("N0"), L.AmountSize, Pal.Bloom, TextAnchor.MiddleCenter,
                             new Vector2(L.ColumnW, L.AmountH), new Vector2(.5f, .5f), Vector2.zero, 5f, 3f), L.AmountFloor);
            var glyph = UIKit.Img("Gem", row, null, Color.white, new Vector2(L.GemSize, L.GemSize),
                                  new Vector2(.5f, .5f), new Vector2(0f, -DealClock.DigitLift(L.AmountSize)));
            glyph.enabled = false;
            glyph.raycastTarget = false;
            var stone = UIKit.Img("Stone", glyph.transform, Art.S("Ui/Shop/gems_1"), Color.white,
                                  new Vector2(L.GemArt, L.GemArt), new Vector2(.5f, .5f), Vector2.zero);
            stone.preserveAspect = true;
            stone.raycastTarget = false;
            UIKit.CentreGlyph(gems, glyph, L.ColumnW);

            UIKit.Shrinkable(
                UIKit.Titled("Coins", Panel, Loc.Format("ui.shop.plus_coins", product.Credits.ToString("N0")).Upper(),
                             L.CoinsSize, Pal.Pollen, TextAnchor.MiddleCenter, new Vector2(L.ColumnW, L.CoinsH),
                             new Vector2(.5f, 1f), new Vector2(L.ColumnX, -L.CoinsY), 4f, 3f), L.CoinsFloor);

            UIKit.Shrinkable(
                UIKit.Titled("Ends", Panel, Loc.Get("ui.deal.ends_label").Upper(), L.EndsSize, Pal.Cream,
                             TextAnchor.MiddleCenter, new Vector2(L.ColumnW, L.EndsH), new Vector2(.5f, 1f),
                             new Vector2(L.ColumnX, -L.EndsY), 3f, 2f), L.EndsFloor);
            _timer = UIKit.Shrinkable(
                UIKit.Titled("Timer", Panel, string.Empty, L.TimerSize, Color.white, TextAnchor.MiddleCenter,
                             new Vector2(L.ColumnW, L.TimerH), new Vector2(.5f, 1f),
                             new Vector2(L.ColumnX, -L.TimerY), 4f, 3f), L.TimerFloor);

            // The price key: the store's own price string, never one this game formats.
            _key = UIKit.TextButton("Buy", Panel, Skins.Buy, string.Empty, L.KeySize,
                                    new Vector2(L.KeyW, L.KeyH), new Vector2(.5f, 1f), new Vector2(0f, -L.KeyY),
                                    TapBuy);
            _keyPlate = _key.GetComponent<Image>();
            Sheen.Attach((RectTransform)_key.transform, 2.6f);

            // The way out, at the foot - never the scrim, so a stray tap beside a purchase does
            // not throw the offer away (the deals panel's rule).
            UIKit.TextButton("NotNow", Panel, Skins.Alternate, Loc.Get("ui.common.cancel").Upper(), L.FootKeySize,
                             new Vector2(L.FootKeyW, L.FootKeyH), new Vector2(.5f, 0f), new Vector2(0f, L.FootKeyY),
                             () => Leave(dismissed: true));

            Repaint();

            Audio.Hush("click");
            Audio.Sfx("menu", .55f);

            var cue = new Cue(this);
            cue.With(() => { if (frame.Crown) Tween.Pop(frame.Crown.transform, 0f, .5f); });
            cue.Then(.30f, () => { if (Panel) Tween.Scale(Panel, 1f, .5f, Ease.OutBack); });
            cue.Then(.18f, () => { if (frame.Banner) Tween.Pop(frame.Banner.transform, 0f, .5f); });
            cue.Then(.16f, () => { if (frame.Word) Tween.Pop(frame.Word.transform, 0f, .55f); });

            StoreService.Changed += Repaint;
            StoreService.Granted += OnGranted;
        }

        void OnDestroy()
        {
            StoreService.Changed -= Repaint;
            StoreService.Granted -= OnGranted;
        }

        public override bool OnBack() { Leave(dismissed: true); return true; }

        void Update()
        {
            if (_leaving || Offer == null) return;
            float now = Time.unscaledTime;
            if (now < _nextTick) return;
            _nextTick = now + .25f;
            Repaint();
        }

        /// <summary>The clock and the key, from the store's state and the offer's end.</summary>
        void Repaint()
        {
            if (!this || _leaving || Offer == null || _key == null) return;

            long left = Offer.SecondsLeft(GameClock.NowUnix());
            _timer.text = DealClock.Timer(left);
            _timer.color = left <= 0 ? Spent : left < DealClock.UrgentSeconds ? Urgent : Color.white;

            var store = StoreService.OfferFor(Offer.Product);
            string caption;
            string skin;
            bool live = false;

            if (_bought) { caption = Loc.Get("ui.deal.bought_row").Upper(); skin = Skins.Settled; }
            else if (left <= 0) { caption = Loc.Get("ui.deal.ended_row").Upper(); skin = Skins.Shut; }
            else
            {
                switch (store.State)
                {
                    case StoreOfferState.Ready: caption = store.Price; skin = Skins.Buy; live = true; break;
                    case StoreOfferState.Purchasing: caption = Loc.Get("ui.shop.purchasing"); skin = Skins.Shut; break;
                    case StoreOfferState.AwaitingGrant: caption = Loc.Get("ui.shop.awaiting_short"); skin = Skins.Shut; break;
                    case StoreOfferState.Loading: caption = Loc.Get("ui.shop.purchasing"); skin = Skins.Shut; break;
                    // Offline or unpriced after it was dealt: said as closed, never sold (44e).
                    default: caption = Loc.Get("ui.deal.ended_row").Upper(); skin = Skins.Shut; break;
                }
            }

            if (caption == _caption && _key.Interactable == live) return;
            _caption = caption;
            _key.SetCaption(caption);
            _keyPlate.sprite = Art.S("Ui/" + skin);
            _key.Interactable = live;
        }

        static readonly Color Urgent = new Color(1f, .36f, .28f);
        static readonly Color Spent = new Color(.62f, .66f, .72f);

        void TapBuy()
        {
            if (_leaving || Offer == null) return;
            OfferLedger.Tapped(Offer);
            StoreTap.Buy(this, Offer.Product);
            Repaint();
        }

        void OnGranted(StoreGrant grant)
        {
            if (!this || _leaving || Offer == null || !grant.IsValid) return;
            if (!string.Equals(grant.Product.Id, Offer.Product.Id, StringComparison.Ordinal)) return;

            // Bought: the key says so for a beat, and the receipt Boot raises takes over.
            _bought = true;
            _caption = null;
            Repaint();
            Leave(dismissed: false, quiet: true);
        }

        void Leave(bool dismissed, bool quiet = false)
        {
            if (_leaving) return;
            _leaving = true;

            if (dismissed && !_bought) OfferLedger.Dismissed(Offer);
            Close(quiet: quiet);
        }
    }

    /// <summary>
    /// The offer panel's geometry (invariant 64), in reference units from the panel's top edge, in
    /// one place so <c>Tools/render_offers.py</c> can mirror it number for number.
    /// </summary>
    public static class OfferLayout
    {
        public sealed class PanelGeometry
        {
            public float Width = 1030f, Height = 990f;
            public float LineY = 184f, LineW = 900f, LineH = 64f;
            public int LineSize = 40, LineFloor = 24;

            /// <summary>The picture row: its centre from the top, the picture at the left, the column at the right.</summary>
            public float RowY = 470f, ArtX = -240f, ArtSize = 380f, GlowSize = 580f;
            public float ColumnX = 210f, ColumnW = 480f;
            public float SealX = -400f, SealY = 300f, SealSize = 176f;
            public int SealWordSize = 44, SealWordFloor = 22;
            public float AmountY = 382f, AmountH = 110f, GemSize = 88f, GemArt = 170f;
            public int AmountSize = 96, AmountFloor = 48;
            public float CoinsY = 462f, CoinsH = 64f;
            public int CoinsSize = 50, CoinsFloor = 26;
            public float EndsY = 534f, EndsH = 42f;
            public int EndsSize = 30, EndsFloor = 20;
            public float TimerY = 594f, TimerH = 80f;
            public int TimerSize = 62, TimerFloor = 32;
            public float KeyY = 762f, KeyW = 480f, KeyH = 124f;
            public int KeySize = 48;
            public float FootKeyY = 70f, FootKeyW = 340f, FootKeyH = 92f;
            public int FootKeySize = 30;
        }

        public static readonly PanelGeometry Panel = new PanelGeometry();
    }
}
