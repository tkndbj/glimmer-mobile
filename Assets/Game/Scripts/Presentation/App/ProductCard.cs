using System;
using GlimmerGrove.Layout;
using GlimmerGrove.Localization;
using GlimmerGrove.Store;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The face of one thing the shop sells: a picture, what arrives, and what it costs.
    ///
    /// <para>
    /// <b>It is one class because there are two shops.</b> The browse screen is one, and a run
    /// that has just been lost is the other — a player short of gems for a continue is offered
    /// the gem shelf where they stand, because navigating to the shop would forfeit the board
    /// they are trying to save (invariant 23). Both draw the same objects: a plate, an edge,
    /// <c>ShopArt</c>'s picture, a headline figure, a note under it and a price face. Two copies
    /// of that would be two answers to questions the shop has already settled once and paid for
    /// settling — that a store's own formatted price is used verbatim and never rebuilt from a
    /// number and a currency code, that a short gem balance still shows the price and greys the
    /// face rather than replacing one with the other, and that the four money states each get
    /// their own colour.
    /// </para>
    /// <para>
    /// <b>The layout is one layout, scaled.</b> Every offset is a fraction of the reference
    /// plate this was lifted from, so at <see cref="Look.Shelf"/>'s size every number resolves
    /// to exactly what the shop screen drew before this class existed, and a smaller card is
    /// the same design rather than a second one. Vertical measurements scale by the plate's
    /// height and horizontal ones by its width, because a card is not always the same shape —
    /// scaling both by one factor is what made the picture and the headline overlap on the
    /// compact card the first time.
    /// </para>
    /// <para>
    /// <b>It draws and nothing else.</b> What is on a shelf, what a tap does, what a refusal is
    /// worded as and whether the store has answered are all the caller's — see
    /// <see cref="StoreWording"/> and <c>StoreTap</c>. This is why the same card can sit in a
    /// grid that navigates and in a grid that must not.
    /// </para>
    /// </summary>
    public sealed class ProductCard
    {
        /// <summary>
        /// How big a card is and how much of it is drawn.
        ///
        /// <para>
        /// <see cref="Decorated"/> is deliberately one flag rather than four. The seat, the
        /// spinning rays, the bonus ribbon and the badge seal all answer the same question —
        /// <em>which of these should you be looking at</em> — and that question only exists on
        /// a screen where somebody is choosing between shelves. A player who opened a card list
        /// to buy a specific number of gems has already chosen, so every one of them would be
        /// noise, and letting a caller take three of the four would be an invitation to invent
        /// a fifth appearance nobody designed.
        /// </para>
        /// </summary>
        public readonly struct Look
        {
            public readonly float Width, Height;
            public readonly int Radius;
            public readonly bool Decorated;

            public Look(float width, float height, int radius, bool decorated)
            {
                Width = width;
                Height = height;
                Radius = radius;
                Decorated = decorated;
            }

            /// <summary>The browse screen's card: full size, and wearing everything.</summary>
            public static Look Shelf => new Look(RefWidth, RefHeight, 30, true);
        }

        // ------------------------------------------------------------------ the reference
        // The card this class was lifted from, kept exactly so the shop screen is unchanged by
        // the extraction. Every number below is measured against these two and nothing else.
        //
        // Aliases rather than a second copy: ProductCardBadges works in these same units to
        // decide where a mark sits against the card opposite, and two plates of different sizes
        // in the two files would put the badge back where it was found.
        const float RefWidth = ProductCardBadges.CardWidth, RefHeight = ProductCardBadges.CardHeight;
        const float PlateInsetX = ProductCardBadges.PlateInsetX, PlateInsetY = ProductCardBadges.PlateInsetY;
        const float RefPlateW = RefWidth - PlateInsetX, RefPlateH = RefHeight - PlateInsetY;

        readonly Image _plate, _edge, _glow, _rays, _ribbon, _seal, _priceFace;
        readonly RectTransform _art;
        readonly Text _amount, _sub, _price, _ribbonText, _sealText;
        readonly int _radius;

        public RectTransform Root { get; }

        public ProductCard(RectTransform parent, in Look look, Action tapped)
        {
            _radius = look.Radius;

            float kv = (look.Height - PlateInsetY) / RefPlateH;
            float kh = (look.Width - PlateInsetX) / RefPlateW;

            // The whole card is the button, so a press squashes plate, picture and price as one
            // object. That is why the price sits on a painted face rather than on a real button
            // — the rule the hub's feature row and the nav caps both follow.
            var button = UIKit.Button("Cell", parent, Art.Pixel,
                                      new Vector2(look.Width - 16f, look.Height - 20f),
                                      new Vector2(.5f, 1f), Vector2.zero, tapped);
            button.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
            Root = (RectTransform)button.transform;

            if (look.Decorated)
            {
                // A gold seat behind the plate, lit only on the card worth pointing at. It is
                // the first layer of FeatureBeacon's argument: the seat is the part visible
                // from the far side of the screen, and the badge is what you read once you are
                // already looking.
                _glow = UIKit.Img("Seat", Root, Art.Glow(128, 1.8f), new Color(1f, .78f, .28f, 0f),
                                  new Vector2(look.Width + 40f, look.Height - 10f),
                                  new Vector2(.5f, .5f), Vector2.zero);
                _glow.raycastTarget = false;
            }

            _plate = UIKit.Img("Plate", Root, Art.Round(look.Radius), Color.white,
                               new Vector2(look.Width - PlateInsetX, look.Height - PlateInsetY),
                               new Vector2(.5f, .5f), Vector2.zero);

            _edge = UIKit.Img("Edge", _plate.transform, Art.RoundOutline(look.Radius, 2f), Color.white);
            UIKit.StretchTo((RectTransform)_edge.transform, 0, 0, 0, 0);

            if (look.Decorated)
            {
                _rays = UIKit.Img("Rays", _plate.transform, Art.Rays(256, 14), new Color(1f, 1f, 1f, 0f),
                                  new Vector2(look.Width * .96f, look.Width * .96f),
                                  new Vector2(.5f, 1f), new Vector2(0f, -196f * kv));
                _rays.raycastTarget = false;
                _rays.transform.SetAsFirstSibling();
            }

            _art = UIKit.Box("Art", _plate.transform, Vector2.one * (236f * kv),
                             new Vector2(.5f, 1f), new Vector2(0f, -170f * kv));

            _amount = UIKit.Shrinkable(
                UIKit.Titled("A", _plate.transform, string.Empty, Font(46, kh), Pal.Cream,
                             TextAnchor.MiddleCenter,
                             new Vector2(look.Width - 80f * kh, 58f * kv),
                             new Vector2(.5f, 0f), new Vector2(0f, 214f * kv), 4f, 4f),
                Font(24, kh));

            _sub = UIKit.Shrinkable(
                UIKit.Titled("S", _plate.transform, string.Empty, Font(26, kh), Pal.Cream,
                             TextAnchor.MiddleCenter,
                             new Vector2(look.Width - 76f * kh, 40f * kv),
                             new Vector2(.5f, 0f), new Vector2(0f, 166f * kv), 3f, 0f),
                Font(16, kh));

            float faceH = 96f * kv;
            _priceFace = UIKit.Img("PriceFace", _plate.transform, Art.S("Ui/btn_green"), Color.white,
                                   new Vector2(look.Width - 110f * kh, faceH),
                                   new Vector2(.5f, 0f), new Vector2(0f, 78f * kv));

            _price = UIKit.Shrinkable(
                UIKit.Titled("P", _priceFace.transform, string.Empty, Font(34, kh), Pal.Cream,
                             TextAnchor.MiddleCenter,
                             new Vector2(look.Width - 160f * kh, 56f * kv),
                             new Vector2(.5f, .5f), new Vector2(0f, faceH * UIKit.PillFaceLift),
                             3f, 3f),
                Font(18, kh));

            if (!look.Decorated) return;

            // The bonus ribbon, across the top-left corner. A real ribbon rather than a caption
            // because it has to survive being read at a glance on a scrolling page, and because
            // it is the one number on the card that is arithmetic over the ladder rather than a
            // claim.
            _ribbon = UIKit.Img("Ribbon", _plate.transform, Art.S("Ui/ribbon_orange"), Color.white,
                                new Vector2(ProductCardBadges.RibbonWidth * kh,
                                            ProductCardBadges.RibbonHeight * kv),
                                new Vector2(0f, 1f),
                                new Vector2(ProductCardBadges.RibbonInset * kh,
                                            -ProductCardBadges.RibbonDrop * kv));
            _ribbon.transform.localRotation = Quaternion.Euler(0f, 0f, ProductCardBadges.RibbonTilt);

            _ribbonText = UIKit.Shrinkable(
                UIKit.Titled("RT", _ribbon.transform, string.Empty, Font(26, kh), Pal.Cream,
                             TextAnchor.MiddleCenter, new Vector2(190f * kh, 40f * kv),
                             new Vector2(.5f, .5f), new Vector2(0f, 4f * kv), 3f, 2f),
                Font(15, kh));

            // The badge, top right, on the seal the win panel already uses for a record. Where
            // it sits is ProductCardBadges' — it has to clear the *next column's* ribbon, which
            // is a fact about the grid rather than about this card, and it was drawn straight
            // through one for as long as the shop has had two shelves.
            _seal = UIKit.Img("Seal", _plate.transform, Art.S("Ui/seal_gold"), Color.white,
                              new Vector2(ProductCardBadges.SealSize * kh,
                                          ProductCardBadges.SealSize * kv),
                              new Vector2(1f, 1f),
                              new Vector2(-ProductCardBadges.SealInset * kh,
                                          -ProductCardBadges.SealDrop * kv));
            _seal.transform.localRotation = Quaternion.Euler(0f, 0f, ProductCardBadges.SealTilt);

            // Cream, and inside the disc. It was dark brown in a box half again as wide as the
            // field it sits on, so a badge said its piece across the rim and onto the plate —
            // where lettering chosen to read on gold was being drawn on the darkest thing on
            // the card. The two faults were one fault: the box was sized against the sprite
            // rather than against the maroon field inside it.
            _sealText = UIKit.Shrinkable(
                UIKit.Titled("ST", _seal.transform, string.Empty,
                             Font(ProductCardBadges.TextSize, kh), Pal.Cream,
                             TextAnchor.MiddleCenter,
                             new Vector2(ProductCardBadges.TextWidth * kh,
                                         ProductCardBadges.TextHeight * kv),
                             new Vector2(.5f, .5f),
                             new Vector2(ProductCardBadges.TextShift * kh,
                                         ProductCardBadges.TextRise * kv),
                             0f, 0f, wrap: true),
                Font(ProductCardBadges.TextFloor, kh));
        }

        /// <summary>A point size scaled with the card, never below something readable.</summary>
        static int Font(int reference, float k) => Mathf.Max(10, Mathf.RoundToInt(reference * k));

        // ------------------------------------------------------------------ drawing
        /// <summary>
        /// Leaves the slot empty.
        ///
        /// For a row a grid has asked for and the caller has nothing to put in — a product the
        /// store has never heard of, or a cell scrolled past the end of a list. Hidden rather
        /// than left drawing the row it used to be, because a recycled cell keeps whatever it
        /// last showed.
        /// </summary>
        public void Hide() => _plate.gameObject.SetActive(false);

        /// <summary>
        /// A real-money product: a picture of what arrives, the currency it grants, and the
        /// store's own price.
        /// </summary>
        /// <param name="featured">
        /// Whether this is the card worth pointing at. Motion is the loudest thing on a
        /// scrolling page, so spending it on every card singles out none — the same argument
        /// the map's rank mark makes about which tier gets rays. Ignored on an undecorated
        /// card, which has nothing to light.
        /// </param>
        public void Draw(StoreProduct product, StoreOffer offer, bool featured)
        {
            if (product == null) { Hide(); return; }

            _plate.gameObject.SetActive(true);

            _plate.color = featured ? new Color(.09f, .17f, .19f, .95f)
                                    : new Color(.10f, .17f, .23f, .92f);

            _edge.sprite = Art.RoundOutline(_radius, featured ? 4f : 2f);
            _edge.color = featured ? Pal.A(Pal.Gold, .78f) : new Color(1f, .97f, .90f, .16f);

            Light(featured);

            ShopArt.Paint(_art, product);

            if (product.IsContainer)
            {
                // A container leads with the cap it sells rather than with a currency, because
                // the number *is* the product — "20" against a heart is the whole offer, and
                // there is nothing underneath it to add up. The colour is the hearts' own, so
                // a shelf that also sells five hearts for gems reads as one resource in two
                // shapes rather than as two things that happen to share a tab.
                _amount.text = Compact.Number(product.HeartCapacity);
                _amount.color = Pal.A(Pal.Rose, 1f);

                _sub.text = Loc.Get("ui.shop.capacity");
            }
            else
            {
                // The headline figure is the currency, never the price. A bundle leads with its
                // gems and says the credits underneath, because gems are the scarcer of the two
                // and the reason somebody is on this shelf.
                bool gemLed = product.Gems > 0;

                _amount.text = Compact.Number(gemLed ? product.Gems : product.Credits);
                _amount.color = gemLed ? Pal.A(Pal.Bloom, 1f) : Pal.A(Pal.Gold, 1f);

                _sub.text = product.Gems > 0 && product.Credits > 0
                    ? Loc.Format("ui.shop.plus_coins", Compact.Number(product.Credits))
                    : Loc.Get(gemLed ? "ui.shop.gems" : "ui.shop.coins");
            }

            _sub.color = new Color(1f, .96f, .88f, .70f);

            PaintPrice(offer);
            PaintRibbon(product.BonusPercent);
            PaintSeal(StoreWording.Badge(product.Badge));
        }

        /// <summary>
        /// A gem-priced good: hearts, or a faster clock.
        ///
        /// <para>
        /// The face is the shop's gem colour rather than its money colour, because the two are
        /// different kinds of transaction and a card should not pretend otherwise. No ribbon and
        /// no seal: a bonus percentage is arithmetic over a money ladder and a good is not on
        /// one.
        /// </para>
        /// </summary>
        public void Draw(StoreGood good, GoodOfferState state)
        {
            if (good == null) { Hide(); return; }

            _plate.gameObject.SetActive(true);

            bool ready = state == GoodOfferState.Ready;

            _plate.color = new Color(.10f, .17f, .23f, .92f);
            _edge.sprite = Art.RoundOutline(_radius, 2f);
            _edge.color = new Color(1f, .97f, .90f, .16f);

            Light(false);

            ShopArt.PaintGood(_art, good);

            _amount.text = good.Kind == StoreGoodKind.HeartBoost
                ? Loc.Format("ui.shop.boost_hours", good.Amount)
                : Compact.Number(good.Amount);
            _amount.color = good.Kind == StoreGoodKind.HeartBoost ? Pal.A(Pal.Sun, 1f)
                                                                  : Pal.A(Pal.Rose, 1f);

            _sub.text = Loc.Get(good.Kind == StoreGoodKind.HeartBoost
                                ? "ui.shop.boost_note" : "ui.shop.hearts");
            _sub.color = new Color(1f, .96f, .88f, .70f);

            // A short balance still shows the price. It used to replace it with "not enough
            // gems", which spends the one line the card has on a refusal and answers a question
            // nobody asked — a player looking at this cell wants to know what it costs, and the
            // state where that matters most is the one where they cannot yet afford it. The
            // amount is what turns "no" into a target, and it is not information the card was
            // withholding for any reason: the tap is not refused either, it opens the gem shelf.
            //
            // What still carries the "not yet" is the *face*, which stays grey — so the card
            // says both things at once instead of trading one for the other.
            //
            // The two *full* refusals keep their sentence, and the difference is the point: a
            // full heart pool is not a price problem, so a price is not the answer to it, and
            // printing a cost beside a thing this shop is deliberately turning down would be
            // inviting the one purchase it exists to prevent.
            bool priced = ready || state == GoodOfferState.ShortOfGems;

            _priceFace.sprite = Art.S("Ui/" + (ready ? "btn_violet" : "btn_gray"));
            _price.text = priced
                ? Loc.Format("ui.shop.gem_price", Compact.Number(good.Gems))
                : Loc.Get(StoreWording.GoodRefusal(state));
            _price.color = ready ? Pal.Cream : Pal.A(Pal.Cream, .72f);

            PaintRibbon(0);
            PaintSeal(null);
        }

        /// <summary>
        /// The price line, and the four things it can say.
        ///
        /// The store's own formatted string is used verbatim whenever there is one — never
        /// rebuilt from a number and a currency code, because there is no correct client-side
        /// rule for that and drawing anything else is a review risk as well as simply wrong in
        /// most of the world.
        /// </summary>
        void PaintPrice(StoreOffer offer)
        {
            switch (offer.State)
            {
                case StoreOfferState.Ready:
                    _priceFace.sprite = Art.S("Ui/btn_green");
                    _price.text = offer.Price;
                    _price.color = Pal.Cream;
                    break;

                case StoreOfferState.Owned:
                    _priceFace.sprite = Art.S("Ui/btn_gray");
                    _price.text = Loc.Get("ui.shop.owned");
                    _price.color = Pal.A(Pal.Cream, .85f);
                    break;

                case StoreOfferState.AwaitingGrant:
                    _priceFace.sprite = Art.S("Ui/btn_orange");
                    _price.text = Loc.Get("ui.shop.awaiting_short");
                    _price.color = Pal.Cream;
                    break;

                case StoreOfferState.Purchasing:
                    _priceFace.sprite = Art.S("Ui/btn_gray");
                    _price.text = Loc.Get("ui.shop.purchasing");
                    _price.color = Pal.A(Pal.Cream, .85f);
                    break;

                default:
                    _priceFace.sprite = Art.S("Ui/btn_gray");
                    _price.text = Loc.Get("ui.shop.price_pending");
                    _price.color = Pal.A(Pal.Cream, .70f);
                    break;
            }
        }

        /// <summary>
        /// The card worth pointing at breathes and wears a fan of light; nothing else does.
        /// </summary>
        /// <remarks>
        /// The turn is <b>channelled</b>, which is what makes it safe on a recycled cell: a card
        /// that scrolls off and comes back rebinding as a plain rung would otherwise leave the
        /// previous row's rotation running against the same transform, and two of those a frame
        /// out of step is the flicker <c>CompanionRevealOverlay</c> already had to name. Killing
        /// the channel first means at most one turn exists per cell, whatever it is rebound to.
        /// </remarks>
        void Light(bool best)
        {
            if (!_glow || !_rays) return;

            _glow.color = best ? new Color(1f, .78f, .28f, .30f) : new Color(1f, .78f, .28f, 0f);
            _rays.color = best ? new Color(1f, .90f, .58f, .16f) : new Color(1f, 1f, 1f, 0f);

            Tween.KillChannel(_rays.transform, "spin");
            _rays.transform.localRotation = Quaternion.identity;

            if (!best) return;

            Tween.Run(60f, Ease.Linear, t =>
            {
                if (_rays) _rays.transform.localRotation = Quaternion.Euler(0, 0, t * 360f);
            }, _rays.transform, "spin").Loop(-1, false);
        }

        void PaintRibbon(int bonusPercent)
        {
            if (!_ribbon) return;

            bool show = bonusPercent >= 5;
            _ribbon.gameObject.SetActive(show);
            if (show) _ribbonText.text = Loc.Format("ui.shop.bonus", bonusPercent);
        }

        void PaintSeal(string key)
        {
            if (!_seal) return;

            bool show = key != null;
            _seal.gameObject.SetActive(show);
            if (show) _sealText.text = Loc.Get(key).ToUpperInvariant();
        }
    }
}
