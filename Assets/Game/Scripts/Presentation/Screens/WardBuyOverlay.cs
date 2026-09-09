using System;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Wards;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The panel behind a turret the player does not hold: what it is, what it does, what it
    /// costs, and the button that pays.
    ///
    /// <para>
    /// <b>One purchase, no stepper.</b> A turret is an <em>entitlement</em> — bought once and held
    /// for ever (<c>WardLedger</c>) — where a utility is a consumable somebody stocks up on, which
    /// is why that panel counts out loud and this one does not.
    /// </para>
    /// <para>
    /// <b>The gate is said before the price</b>, because <c>WardLedger.OfferFor</c> asks it first
    /// (invariant 15a): a keeper both a rung short and out of credits is told about the wall money
    /// cannot climb, rather than being shown a price they could pay and a refusal they could not
    /// explain.
    /// </para>
    /// <para>
    /// <b>A short balance keeps a live button</b>, which is <c>HomesteadBuyOverlay</c>'s rule and
    /// its reason: this is the moment a player has decided they want something, and a greyed
    /// control spends it on teaching them the feature is broken. A gem-priced turret stacks the
    /// gem shelf on top rather than navigating away, so the loadout behind is still there when
    /// they come back.
    /// </para>
    /// </summary>
    public sealed class WardBuyOverlay : ModalView
    {
        /// <summary>
        /// The turret being offered.
        ///
        /// A property rather than a field for <c>UtilityBuyOverlay.Item</c>'s reason:
        /// <see cref="WardModel"/> is not <c>[Serializable]</c>, so a public field earns a warning
        /// about serialisation that will never happen.
        /// </summary>
        public WardModel Model { get; set; }

        /// <summary>Raised after a purchase lands, so the shelf behind can repaint.</summary>
        public Action Bought { get; set; }

        const float PanelW = 780f, PanelH = 900f;

        // The panel is parchment, so it is written in ink rather than in the cream the board uses
        // — `UtilityBuyOverlay`'s note, and the same measured accents.
        static readonly Color Ink = new Color(.36f, .25f, .18f);
        static readonly Color Short = new Color(.58f, .31f, .06f);
        static readonly Color Held = new Color(.18f, .42f, .21f);

        Text _status;
        Btn _buy;
        Text _price;
        Image _coin;

        protected override void Build()
        {
            if (Model == null) { Close(); return; }

            MakePanel(new Vector2(PanelW, PanelH), Loc.Get(Model.NameKey));

            var panel = Panel;

            UIKit.IconButton("Close", panel, Skins.Nav, "ic_close", new Vector2(92f, 92f),
                             new Vector2(1f, 1f), new Vector2(-44f, -44f), () => Close());

            // The uncoloured thumbnail, which is what the shelf browses with: a turret's colour on
            // this panel would be a fifth answer to a question the four slots already ask.
            var icon = UIKit.Img("Turret", panel, AssetLibrary.Sprite(AssetManifest.WardThumb(Model.Id)), Color.white,
                                 new Vector2(300f, 300f), new Vector2(.5f, 1f),
                                 new Vector2(0f, -170f));
            icon.preserveAspect = true;

            var note = UIKit.Label("Note", panel, Loc.Get(Model.NoteKey), 28, Pal.A(Ink, .82f),
                                   TextAnchor.UpperCenter, new Vector2(PanelW - 140f, 120f),
                                   new Vector2(.5f, 1f), new Vector2(0f, -490f));
            UIKit.Shrinkable(note, 20);

            _status = UIKit.Label("Status", panel, string.Empty, 26, Short,
                                  TextAnchor.MiddleCenter, new Vector2(PanelW - 140f, 44f),
                                  new Vector2(.5f, 1f), new Vector2(0f, -624f));

            BuildButton(panel);
            Paint();

            // The ledger's own event rather than a callback, for `LoadoutScreen`'s reason: a
            // balance can move while this is open (a gem shelf stacked on top), and a panel that
            // only repainted on its own taps would keep a dead button over a purse that could now
            // pay for it.
            PlayerProgression.Changed += Paint;
            WardLedger.Changed += Paint;
        }

        void OnDestroy()
        {
            PlayerProgression.Changed -= Paint;
            WardLedger.Changed -= Paint;
        }

        void BuildButton(RectTransform panel)
        {
            _buy = UIKit.Button("Buy", panel, Art.S("Ui/btn_green"), new Vector2(460f, 118f),
                                new Vector2(.5f, 1f), new Vector2(0f, -700f), Pay);

            float lift = 118f * UIKit.PillFaceLift;

            _price = UIKit.Titled("Price", _buy.transform, string.Empty, 36, Pal.Cream,
                                  TextAnchor.MiddleCenter, new Vector2(300f, 60f),
                                  new Vector2(.5f, .5f), new Vector2(18f, lift), 0f, 3f);
            UIKit.Shrinkable(_price, 22);

            _coin = UIKit.Img("Coin", _buy.transform, null, Color.white, new Vector2(46f, 46f),
                              new Vector2(.5f, .5f), new Vector2(-108f, lift));
            _coin.preserveAspect = true;
        }

        void Paint()
        {
            if (this == null || Model == null || _buy == null) return;

            var offer = WardLedger.OfferFor(Model, PlayerProgression.Level.Level);

            switch (offer.State)
            {
                case WardPurchaseState.AlreadyHeld:
                    _status.text = Loc.Get("ui.loadout.held_one");
                    _status.color = Held;
                    _price.text = Loc.Get("ui.ok");
                    _coin.enabled = false;
                    break;

                case WardPurchaseState.LevelLocked:
                    _status.text = Loc.Format("ui.loadout.level_note", offer.RequiredLevel);
                    _status.color = Short;
                    _price.text = Loc.Format("ui.loadout.level", offer.RequiredLevel);
                    _coin.enabled = false;
                    break;

                case WardPurchaseState.NotForSale:
                    _status.text = Loc.Get("ui.loadout.locked");
                    _status.color = Short;
                    _price.text = Loc.Get("ui.ok");
                    _coin.enabled = false;
                    break;

                default:
                    bool gems = offer.Currency == Currency.Gems;

                    _status.text = offer.Shortfall > 0
                        ? Loc.Format(gems ? "ui.shop.short_gems" : "ui.shop.short_coins",
                                     offer.Shortfall)
                        : string.Empty;

                    _status.color = Short;
                    _price.text = offer.Cost.ToString("N0");
                    _coin.enabled = true;
                    _coin.sprite = gems ? Art.S("Ui/ic_gem") : null;
                    break;
            }
        }

        /// <summary>
        /// Pays, or opens the shelf that could.
        ///
        /// <b>A short balance is answered rather than refused</b> — the gem shelf stacks over this
        /// panel and steps out when the gems land, which is <c>GemShopOverlay</c>'s own rule. A
        /// credit shortfall has no shelf to open, so it says the number and nothing else: credits
        /// are earned by playing and there is nothing here to sell.
        /// </summary>
        void Pay()
        {
            var offer = WardLedger.OfferFor(Model, PlayerProgression.Level.Level);

            if (offer.State == WardPurchaseState.AlreadyHeld
                || offer.State == WardPurchaseState.NotForSale
                || offer.State == WardPurchaseState.LevelLocked)
            {
                Close();
                return;
            }

            if (offer.Shortfall > 0)
            {
                if (offer.Currency == Currency.Gems) Flow.Modal<GemShopOverlay>();
                else Audio.Sfx("blocked", .45f);

                return;
            }

            if (!WardLedger.TryBuy(Model, PlayerProgression.Level.Level))
            {
                Audio.Sfx("blocked", .45f);
                return;
            }

            Audio.Sfx("unlock", .6f);
            Burst.Confetti(Content, 24);

            Bought?.Invoke();
            Close();
        }
    }
}
