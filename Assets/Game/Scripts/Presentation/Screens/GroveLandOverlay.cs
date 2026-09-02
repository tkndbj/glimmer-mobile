using System;
using GlimmerGrove.Ads;
using GlimmerGrove.Homestead;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The panel behind a locked stretch of ground: how big it is, what it costs, and the
    /// button that pays.
    ///
    /// <para>
    /// Built like <c>HomesteadBuyOverlay</c> because it is the same transaction wearing
    /// different words, and deliberately not merged with it: what a player is judging here is
    /// <em>room</em> rather than an object, so the panel leads with how many tiles they are
    /// getting and shows no picture at all. A thumbnail of a patch of grass would be a picture
    /// of nothing.
    /// </para>
    /// <para>
    /// A player who is short opens the coin offer rather than meeting a dead button, which is
    /// <c>CompanionUnlockOverlay</c>'s call and for its reason: this is the moment somebody has
    /// decided they want something, which is the best moment in the game to offer a video and
    /// the worst to teach them a control does nothing.
    /// </para>
    /// </summary>
    public sealed class GroveLandOverlay : ModalView
    {
        /// <summary>
        /// The land being offered. Set by the caller before Build runs.
        ///
        /// A property rather than a field for <c>CompanionUnlockOverlay.Avatar</c>'s reason:
        /// nothing here is assigned through the inspector, and a public field of a type Unity
        /// cannot serialise earns a warning about serialisation that will never happen.
        /// </summary>
        public GroveRegion Region { get; set; }

        const float PanelW = 860f;
        const float PanelH = 720f;

        Btn _action;
        Text _status, _size;
        bool _paid, _buying;

        /// <summary>
        /// The floor this region belongs to, so the offer can answer the ladder as well as the
        /// price. Read once rather than per repaint: the catalog does not change under an open
        /// panel, and <c>HomesteadCatalog.Current</c> is a property that rebuilds nothing but is
        /// still a lookup on a path a balance change runs down.
        /// </summary>
        GroveFloor Floor => HomesteadCatalog.IsLoaded ? HomesteadCatalog.Current.Floor : null;

        HomesteadOffer Offer => GroveLand.OfferFor(Region, Floor);

        protected override void Build()
        {
            MakePanel(new Vector2(PanelW, PanelH),
                      Region == null ? Loc.Get("ui.land.title") : Loc.Get(Region.NameKey));

            UIKit.IconButton("Close", Panel, Skins.Nav, "ic_close", new Vector2(92f, 92f),
                             new Vector2(1f, 1f), new Vector2(-44f, -44f), () => Close());

            UIKit.Halo(Panel, Pal.Verdant, 380f, .16f, new Vector2(0f, 96f));

            // How much room, in the unit the player is actually buying. A region's dimensions
            // rather than its area alone, because "6 x 5" is a shape somebody can picture and
            // "30 tiles" is a number.
            _size = UIKit.Shrinkable(
                UIKit.Titled("Size", Panel, SizeText(), 52, Pal.Cream, TextAnchor.MiddleCenter,
                             new Vector2(640f, 72f), new Vector2(.5f, .5f), new Vector2(0f, 110f), 3f, 3f), 32);

            UIKit.Shrinkable(
                UIKit.Titled("Note", Panel, Loc.Get("ui.land.note"), 26,
                             new Color(1f, .96f, .88f, .70f), TextAnchor.MiddleCenter,
                             new Vector2(640f, 76f), new Vector2(.5f, .5f), new Vector2(0f, 22f), 3f, 0f), 18);

            _status = UIKit.Shrinkable(
                UIKit.Titled("Status", Panel, string.Empty, 30, Pal.Cream, TextAnchor.MiddleCenter,
                             new Vector2(660f, 44f), new Vector2(.5f, .5f), new Vector2(0f, -66f), 3f, 3f), 20);

            BuildAction();

            // A balance can move under an open panel: a chest opened elsewhere, a sync landing
            // the server's figure, an ad paying out through the offer this panel opened.
            PlayerProgression.Changed += Repaint;
        }

        void OnDestroy() => PlayerProgression.Changed -= Repaint;

        public override bool OnBack() { Close(); return true; }

        string SizeText()
            => Region == null ? string.Empty : Loc.Format("ui.land.size", Region.Cols, Region.Rows);

        // ---------------------------------------------------------------- action
        void BuildAction()
        {
            var offer = Offer;

            var size = new Vector2(560f, 122f);
            var anchor = new Vector2(.5f, 0f);
            var at = new Vector2(0f, 104f);

            // Ground further up the ladder is a dead end without this: the panel would stand
            // there naming a stretch the player cannot buy, with no way on but the close cross.
            // So the button becomes the stretch that *is* for sale — which is the one thing they
            // can act on, and it is what they came here to find out.
            if (offer.State == HomesteadPurchaseState.EarlierFirst)
            {
                var next = GroveLand.NextForSale(Floor);

                _action = UIKit.TextButton("Next", Panel, "btn_blue",
                                           Loc.Format("ui.land.open_next",
                                                      next == null ? string.Empty : Loc.Get(next.NameKey)),
                                           40, size, anchor, at, () => OnOpenNext(next));
                _action.Interactable = next != null;
            }
            else if (offer.State == HomesteadPurchaseState.TooExpensive)
            {
                // Two different shortfalls and two different ways out of them. Credits have a
                // video that pays them; gems do not and never will (invariant 10d), so the free
                // way is offered where one exists and the shelf is brought to the player where
                // it does not — never a dead button, which is this panel's whole rule.
                bool gems = Region != null && Region.IsGemPriced;

                _action = UIKit.TextButton("Earn", Panel, "btn_blue",
                                           Loc.Get(gems ? "ui.land.get_gems" : "ui.companion.get_coins"),
                                           40, size, anchor, at,
                                           gems ? (Action)OnGetGems : OnGetCoins,
                                           gems ? "ic_gem" : "ic_play");
            }
            else
            {
                // The price carries its own glyph, because "BUY FOR 600" over a stretch of
                // ground says nothing about which 600 — and half this floor is sold in each
                // currency, so the two cards sit on the same shelf a scroll apart. Which is why
                // the credit half carrying *no* glyph was the half that broke it: a bare number
                // beside a gem-marked one reads as the same currency with the mark left off.
                bool priced = Region != null && Region.IsGemPriced;

                _action = UIKit.TextButton("Buy", Panel, "btn_green",
                                           Loc.Format("ui.grove.buy_for", Compact.Number(offer.Cost)), 40,
                                           size, anchor, at, OnBuy,
                                           priced ? Art.S("Ui/ic_gem") : Art.CoinFace(),
                                           iconTrails: true);
                _action.Interactable = offer.CanBuy;
            }

            UIKit.Shrinkable(_action.Label, 24);
            UIKit.FitLabel(_action);

            Repaint();
        }

        /// <summary>
        /// Swaps this panel for the stretch that is actually on offer.
        ///
        /// Closed and reopened rather than rebound, because <c>ModalView</c> builds once: a
        /// second panel is four lines and a re-entrant rebuild is a class of bug.
        /// </summary>
        void OnOpenNext(GroveRegion next)
        {
            if (next == null) return;
            Close(() => Flow.Modal<GroveLandOverlay>(v => v.Region = next), quiet: true);
        }

        /// <summary>
        /// Redraws the status line and swaps the button when the balance moves under the panel.
        ///
        /// Guarded on <see cref="_buying"/> for <c>CompanionUnlockOverlay</c>'s reason: the
        /// debit is booked before the region is recorded as owned, and booking it raises
        /// <c>PlayerProgression.Changed</c> — so there is a moment inside the purchase where the
        /// balance has fallen and the id has not arrived, and a repaint landing there would
        /// destroy the buy button from inside that button's own click handler.
        /// </summary>
        void Repaint()
        {
            if (!this || _paid || _buying) return;

            var offer = Offer;

            if (_status)
            {
                bool gems = Region != null && Region.IsGemPriced;

                switch (offer.State)
                {
                    // Names the stretch that comes first rather than quoting a price, because
                    // the price is not what is stopping them and printing it would say it was.
                    case HomesteadPurchaseState.EarlierFirst:
                        var next = GroveLand.NextForSale(Floor);
                        _status.text = next == null ? string.Empty
                            : Loc.Format("ui.land.earlier_first", Loc.Get(next.NameKey));
                        _status.color = Pal.A(Pal.Aqua, .90f);
                        break;

                    case HomesteadPurchaseState.TooExpensive:
                        _status.text = Loc.Format(gems ? "ui.grove.price_gems" : "ui.grove.price",
                                                  Compact.Number(offer.Cost));
                        _status.color = Pal.A(gems ? Pal.Bloom : Pal.Sun, .90f);
                        break;

                    // The balance is the one this stretch is bought with — HomesteadOffer
                    // carries it, so nothing here has to pick a wallet by hand.
                    default:
                        _status.text = Loc.Format("ui.land.balance", Compact.Number(offer.Balance));
                        _status.color = new Color(1f, .96f, .88f, .72f);
                        break;
                }
            }

            // The ladder is the one refusal that does not become live when money arrives, so it
            // is the one state where the button stays dead-lettered — except that it is not a
            // buy button at all there: it opens the stretch that is on offer, and that is always
            // live. Which is why this asks about the *offer* rather than about the button.
            if (_action)
                _action.Interactable = offer.CanBuy
                                    || offer.State == HomesteadPurchaseState.TooExpensive
                                    || offer.State == HomesteadPurchaseState.EarlierFirst;
        }

        void OnBuy()
        {
            if (_paid || _buying) return;

            // Read before the money moves, because it cannot be read afterwards: the star row
            // on the Grovement celebrates what was not there a moment ago, and this is the last
            // moment that exists. See HomesteadScreen.ArrivingStars.
            int stars = HomesteadCatalog.IsLoaded
                ? GroveScore.Of(HomesteadCatalog.Current).Stars
                : -1;

            _buying = true;
            bool bought;

            try
            {
                // Re-checked here rather than trusted from the button, because the balance can
                // have moved since it was painted.
                bought = GroveLand.TryBuy(Region, Floor);
            }
            finally
            {
                _buying = false;
            }

            if (!bought) { Repaint(); return; }

            _paid = true;

            // The coin is the money leaving. What the land opening sounds like belongs to the
            // ceremony, which is about to play it — see GroveRise.
            Audio.Sfx("coin", .6f);

            // Ground is the one thing in this shop that is not an object the player then
            // places, so it is the one purchase with nowhere to be looked at afterwards. They
            // are taken back to their grove and shown it arriving instead of walking back to a
            // floor that is quietly bigger. Quiet, because the celebration starts a beat later
            // and a backing-out whoosh under it is one sound too many.
            var region = Region;
            Close(() => Flow.Go<HomesteadScreen>(v =>
            {
                v.Arriving = region;
                v.ArrivingStars = stars;
            }), quiet: true);
        }

        void OnGetCoins()
        {
            Flow.Modal<AdOfferOverlay>(v =>
            {
                v.PlacementId = AdPlacement.CoinBonus;
                v.Rewarded = () => { if (this) Repaint(); };
            });
        }

        /// <summary>
        /// The gem shelf, stacked on this panel rather than navigated to.
        ///
        /// <para>
        /// Nothing is frozen behind this one — the reason <c>GemShopOverlay</c> was written —
        /// so walking to the shop would not lose anything. It is stacked anyway because of what
        /// it comes back to: a keeper who has just decided they want a particular stretch of
        /// ground, and who would otherwise pay for gems and then have to find their way back
        /// through two screens to the panel that asked. The shelf steps out from under its own
        /// receipt, so buying leaves the offer standing with the price now met.
        /// </para>
        /// </summary>
        void OnGetGems()
        {
            Flow.Modal<GemShopOverlay>(v => v.Bought = () => { if (this) Repaint(); });
        }
    }
}
