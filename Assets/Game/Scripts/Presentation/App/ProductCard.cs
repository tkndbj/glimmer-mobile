using System;
using GlimmerGrove.Layout;
using GlimmerGrove.Localization;
using GlimmerGrove.Store;
using GlimmerGrove.Utilities;
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
        /// <em>how does this compare with the others</em> — and that question only exists on a
        /// screen where somebody is choosing between shelves. A player who opened a card list
        /// to buy a specific number of gems has already chosen, so every one of them would be
        /// noise, and letting a caller take three of the four would be an invitation to invent
        /// a fifth appearance nobody designed. That is what keeps the rarity light out of the
        /// panel a lost run raises: it belongs to browsing, not to paying.
        /// </para>
        /// </summary>
        public readonly struct Look
        {
            public readonly float Width, Height;
            public readonly bool Decorated;

            // No corner radius: the plate is a nine-sliced frame out of the kit now, so the
            // corner is a property of the sprite rather than a number a caller chooses. It
            // was carried for a while after the frames arrived, read by nothing.
            public Look(float width, float height, bool decorated)
            {
                Width = width;
                Height = height;
                Decorated = decorated;
            }

            /// <summary>The browse screen's card: full size, and wearing everything.</summary>
            public static Look Shelf => new Look(RefWidth, RefHeight, true);
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

        readonly Image _plate, _spot, _ribbon, _seal, _priceFace, _priceMark;
        readonly RectTransform _art;
        readonly Text _amount, _sub, _price, _sealText;
        readonly RectTransform _ribbonArc;
        readonly int _ribbonFont;
        readonly float _ribbonRadius;
        string _ribbonSaid;

        /// <summary>
        /// How wide the price caption and any glyph beside it may be, together.
        ///
        /// Kept because <see cref="UIKit.CentreGlyph"/> needs it on every repaint and the box it
        /// would otherwise be read from is the one that call is about to narrow — measuring from
        /// the last centring is how a caption walks off centre a little further each time it is
        /// drawn.
        /// </summary>
        readonly float _priceWidth;

        public RectTransform Root { get; }

        public ProductCard(RectTransform parent, in Look look, Action tapped)
        {
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

            // The kit's own card, nine-sliced: one teal plate on every shelf.
            //
            // It used to be three coloured frames keyed on the shelf, and losing them is what
            // this restyle is really about. Five saturated blocks of colour side by side read
            // as five different games rather than as one shop, and what the colour was *for* —
            // telling one shelf from another — is now said twice over by things that say it
            // better: a lit tab, which the old dark chips could not do at all, and a coloured
            // light under the goods, where a player is already looking.
            _plate = UIKit.Img("Plate", Root, Art.S("Ui/" + Skins.Card), Color.white,
                               new Vector2(look.Width - PlateInsetX, look.Height - PlateInsetY),
                               new Vector2(.5f, .5f), Vector2.zero);

            // 300 rather than 236, and the stack under it moved down to pay for it. The
            // picture is what a card is for — it is the only part a player reads before the
            // price — and against a kit frame that carries its own colour there is nothing
            // else on the plate for the empty room above it to be doing.
            // A pool of light under the picture, and it is back after being taken off with
            // everything else behind an item. The distinction that makes it work where the
            // rest did not: a *fan of rays* and a lighter panel across the top of the frame
            // are patterns on the card, so on an opaque frame they read as decoration behind
            // the object; a soft round light centred on the object reads as light on it. One
            // colour, one strength, no rung and no rotation — nothing here is saying how much,
            // because the picture already does.
            // It carries the shelf's colour now rather than plain white, which is the job the
            // frame used to do. Still one strength and no rotation - nothing here says *how
            // much*, because the picture already does.
            _spot = UIKit.Img("Spot", _plate.transform, Art.Glow(160, 1.6f),
                              Pal.A(Pal.Bloom, SpotAlpha), Vector2.one * (370f * kv),
                              new Vector2(.5f, 1f), new Vector2(0f, -168f * kv));
            _spot.raycastTarget = false;

            _art = UIKit.Box("Art", _plate.transform, Vector2.one * (300f * kv),
                             new Vector2(.5f, 1f), new Vector2(0f, -168f * kv));

            _amount = UIKit.Shrinkable(
                UIKit.Titled("A", _plate.transform, string.Empty, Font(46, kh), Pal.Cream,
                             TextAnchor.MiddleCenter,
                             new Vector2(look.Width - 80f * kh, 58f * kv),
                             new Vector2(.5f, 0f), new Vector2(0f, 196f * kv), 4f, 4f),
                Font(24, kh));

            _sub = UIKit.Shrinkable(
                UIKit.Titled("S", _plate.transform, string.Empty, Font(26, kh), Pal.Cream,
                             TextAnchor.MiddleCenter,
                             new Vector2(look.Width - 76f * kh, 40f * kv),
                             new Vector2(.5f, 0f), new Vector2(0f, 150f * kv), 3f, 0f),
                Font(16, kh));

            float faceH = 96f * kv;
            _priceFace = UIKit.Img("PriceFace", _plate.transform,
                                   Art.S("Ui/" + Skins.Buy), Color.white,
                                   new Vector2(look.Width - 110f * kh, faceH),
                                   new Vector2(.5f, 0f), new Vector2(0f, 74f * kv));

            _priceWidth = look.Width - 160f * kh;

            _price = UIKit.Shrinkable(
                UIKit.Titled("P", _priceFace.transform, string.Empty, Font(34, kh), Pal.Cream,
                             TextAnchor.MiddleCenter,
                             new Vector2(_priceWidth, 56f * kv),
                             new Vector2(.5f, .5f), new Vector2(0f, faceH * UIKit.PillFaceLift),
                             3f, 3f),
                Font(18, kh));

            // The gem in front of a gem price. Built once and hidden, rather than created and
            // destroyed as the shelf scrolls: this cell is rebound rather than rebuilt
            // (invariant 16d), so a glyph that came and went would be a new object on most
            // binds on the one screen that must not stutter.
            //
            // It is on the price face rather than beside the caption because the face is what
            // moves when the card is pressed — a glyph parented anywhere else would stay put
            // while the price it belongs to squashed away from it.
            _priceMark = UIKit.Img("PriceMark", _priceFace.transform, Art.S("Ui/ic_gem"), Pal.Cream,
                                   Vector2.one * (faceH * .34f), new Vector2(.5f, .5f),
                                   new Vector2(0f, faceH * UIKit.PillFaceLift));
            _priceMark.preserveAspect = true;
            _priceMark.raycastTarget = false;
            _priceMark.gameObject.SetActive(false);

            if (!look.Decorated) return;

            // The bonus mark, across the top-left corner: the kit's own ribbon, which is a real
            // one with tails and a heavy keyline. The two kits before this one had nothing of
            // the sort, so this was a machined title plate standing in for cloth.
            //
            // **It hangs again**, at `ProductCardBadges.RibbonTilt`. That went to nought while
            // the mark was a plate — a plate off square reads as one that has come loose — and
            // the reason expired with the art. The angle is applied here and the *reach* it
            // costs is arithmetic over in `ProductCardBadges`, which is what keeps a tilted
            // ribbon from quietly growing into the seal on the card beside it.
            _ribbon = UIKit.Img("Ribbon", _plate.transform, Art.S("Ui/" + Skins.Title), Color.white,
                                new Vector2(ProductCardBadges.RibbonWidth * kh,
                                            ProductCardBadges.RibbonHeight * kv),
                                new Vector2(0f, 1f),
                                new Vector2(ProductCardBadges.RibbonInset * kh,
                                            -ProductCardBadges.RibbonDrop * kv));
            _ribbon.transform.localRotation =
                Quaternion.Euler(0f, 0f, ProductCardBadges.RibbonTilt);

            // **Bent to the cloth, and lifted onto the flag.** A straight word inside a ribbon
            // reads as a label that happens to be sitting on a curved thing, which is what this
            // was; and the sprite's own centre is not the writable band's centre, because the
            // tails hang below it — see `Skins.RibbonLift`, which is measured off the picture.
            //
            // The radius is the ribbon's own width, which is what keeps every card's mark
            // bending by the same amount however wide the grid draws a column. It is not
            // `Shrinkable`, because best-fit works on one label's box and an arc is one box per
            // character; the caption is "+12% EXTRA" at its longest, which the flag holds.
            _ribbonFont = Font(23, kh);
            // 1.41x the drawn width, which is the same ratio the storefront's own title uses
            // (620 over a 440-wide plate). It has to be a ratio rather than a number: this is
            // the same sprite drawn smaller, so its curve is proportionally identical and a
            // fixed radius would bend a card's mark far harder than the header's.
            _ribbonRadius = ProductCardBadges.RibbonWidth * kh * 1.41f;
            _ribbonArc = UIKit.Box("RT", _ribbon.transform, Vector2.zero, new Vector2(.5f, .5f),
                                   new Vector2(0f, ProductCardBadges.RibbonHeight * kv * Skins.RibbonLift));

            // The badge, top right, on the seal the win panel already uses for a record. Where
            // it sits is ProductCardBadges' — it has to clear the *next column's* ribbon, which
            // is a fact about the grid rather than about this card, and it was drawn straight
            // through one for as long as the shop has had two shelves.
            _seal = UIKit.Img("Seal", _plate.transform, Art.S("Ui/" + Skins.Badge), Pal.Rose,
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

        /// <summary>
        /// The line under the headline figure when it is a unit label rather than an amount —
        /// deliberately quiet, because it names what the number above it is and competing with
        /// that number is the one thing it must not do.
        /// </summary>
        static readonly Color Unit = new Color(1f, .96f, .88f, .70f);

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
        /// <remarks>
        /// There is no <c>featured</c> flag any more. It used to buy a lift on the rung's own
        /// light and a gold seat behind the plate, and both went with the decoration the owner
        /// asked to have taken off: what is *worth pointing at* is now said by the badge and by
        /// the badge alone. A parameter that reaches nothing is worse than no parameter — it
        /// reads as a knob somebody can turn.
        /// </remarks>
        public void Draw(StoreProduct product, StoreOffer offer)
        {
            if (product == null) { Hide(); return; }

            _plate.gameObject.SetActive(true);

            Shelf(product.Shelf);

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
                _sub.color = Unit;
            }
            else
            {
                // The headline figure is the currency, never the price. A bundle leads with its
                // gems and says the credits underneath, because gems are the scarcer of the two
                // and the reason somebody is on this shelf.
                bool gemLed = product.Gems > 0;
                bool bundle = product.Gems > 0 && product.Credits > 0;

                _amount.text = Compact.Number(gemLed ? product.Gems : product.Credits);
                _amount.color = gemLed ? Pal.A(Pal.Bloom, 1f) : Pal.A(Pal.Gold, 1f);

                _sub.text = bundle
                    ? Loc.Format("ui.shop.plus_coins", Compact.Number(product.Credits))
                    : Loc.Get(gemLed ? "ui.shop.gems" : "ui.shop.coins");

                // A currency is always drawn in its own colour, and on a bundle this line is a
                // currency rather than a unit. That is the whole distinction: on every other
                // card the line under the figure is the *label* for the figure — "GEMS" beneath
                // a violet 8,500 — so colouring it violet too would say one thing twice and
                // leave the card monochrome. A bundle's line is a *second amount in a second
                // currency*, and the coins were being drawn in the same faint cream as a unit
                // label, which is the one reading that makes an extra 42,000 credits look like
                // small print. Gold, because that is what coins are everywhere else in the game.
                _sub.color = bundle ? Pal.A(Pal.Gold, .95f) : Unit;
            }

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

            Shelf(StoreShelf.Supplies);


            ShopArt.PaintGood(_art, good);

            _amount.text = good.Kind == StoreGoodKind.HeartBoost
                ? Loc.Format("ui.shop.boost_hours", good.Amount)
                : Compact.Number(good.Amount);
            _amount.color = good.Kind == StoreGoodKind.HeartBoost ? Pal.A(Pal.Sun, 1f)
                                                                  : Pal.A(Pal.Rose, 1f);

            _sub.text = Loc.Get(good.Kind == StoreGoodKind.HeartBoost
                                ? "ui.shop.boost_note" : "ui.shop.hearts");
            _sub.color = Unit;

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

            Face(Skins.Gem, ready);
            _price.color = ready ? Pal.Cream : Pal.A(Pal.Cream, .72f);

            // The gem rides with the number and only with the number. A price on this face is
            // the one figure in the shop with no currency written beside it — every money card
            // carries the store's own formatted string, symbol and all — so without the glyph
            // "280" is a quantity of nothing, sitting under a card whose *other* number is a
            // quantity of hearts. It comes off for the two full refusals, which are sentences
            // rather than prices: a gem in front of "your hearts are full" prices the refusal.
            SetPrice(priced ? Loc.Format("ui.shop.gem_price", Compact.Number(good.Gems))
                            : Loc.Get(StoreWording.GoodRefusal(state)),
                     gem: priced);

            PaintRibbon(0);
            PaintSeal(null);
        }

        /// <summary>
        /// A utility: the picture the action bar draws, its name, how many are in the pack, and
        /// what one more costs in gems.
        ///
        /// <para>
        /// <b>The headline is the name rather than a figure, and that is the one place this card
        /// breaks its own grammar on purpose.</b> Everywhere else the big line is <em>what
        /// arrives</em> — 8,500 gems, 20 capacity, 5 hearts — because the amount is what
        /// distinguishes one rung from the next. A utility shelf has no rungs: four different
        /// objects, each bought one at a time, so the amount would read "1" on all four and the
        /// only thing that tells them apart is which one it is.
        /// </para>
        /// <para>
        /// <b>The count goes underneath because a ceiling of a hundred makes it a real
        /// question.</b> At nine it was a ration and the shelf could have said nothing; at a
        /// hundred what a player wants to know before paying is what they are already carrying,
        /// and it is the same sentence the panel behind the empty slot says (invariant 5a's rule
        /// about one thing being said in one place).
        /// </para>
        /// <para>
        /// The gem price behaves exactly as a good's does: a short balance still shows it and
        /// greys the face, because the amount is what turns "no" into a target and the tap is not
        /// refused either — it opens the gem shelf. A full pack is the one state that trades the
        /// price for a sentence, for the good's own reason: it is not a price problem, and
        /// printing a cost beside something the shop is turning down invites the one purchase it
        /// exists to prevent.
        /// </para>
        /// </summary>
        public void Draw(UtilityItem item, UtilityRefusal refusal, int held)
        {
            if (item == null) { Hide(); return; }

            _plate.gameObject.SetActive(true);

            bool ready = refusal == UtilityRefusal.None;
            bool priced = ready || refusal == UtilityRefusal.Poor;

            Shelf(StoreShelf.Utilities);

            var tint = ShopRarity.Of(item);

            ShopArt.PaintUtility(_art, item);

            _amount.text = Loc.Get(item.NameKey);
            _amount.color = Pal.A(tint, 1f);

            _sub.text = Loc.Format("ui.shop.utility_held", held, item.MaxHeld);
            _sub.color = Unit;

            Face(Skins.Gem, ready);
            _price.color = ready ? Pal.Cream : Pal.A(Pal.Cream, .72f);

            SetPrice(priced ? Loc.Format("ui.shop.gem_price", Compact.Number(item.GemPrice))
                            : refusal == UtilityRefusal.Locked
                              ? Loc.Format("ui.loadout.level", item.MinLevel)
                            : Loc.Get(refusal == UtilityRefusal.NotForSale
                                      ? "ui.utility.chest_only"
                                      : "ui.shop.utility_full"),
                     gem: priced);

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
            // Never a gem: everything drawn through here is bought with money, and the string is
            // the store's own with the player's own currency symbol already in it. The glyph is
            // taken off explicitly rather than left alone, because the same cell object is
            // rebound between a gem-priced good and a real-money container as the supplies shelf
            // scrolls (invariant 16d) — leaving it would put a gem in front of a dollar sign.
            SetGemMark(false);

            switch (offer.State)
            {
                case StoreOfferState.Ready:
                    Face(Skins.Buy, live: true);
                    _price.text = offer.Price;
                    _price.color = Pal.Cream;
                    break;

                case StoreOfferState.Owned:
                    Face(Skins.Buy, live: false);
                    _price.text = Loc.Get("ui.shop.owned");
                    _price.color = Pal.A(Pal.Cream, .85f);
                    break;

                // Grey like Owned, and a different word: the rung is included in what they
                // hold rather than bought. See StoreOfferState.Included.
                case StoreOfferState.Included:
                    Face(Skins.Buy, live: false);
                    _price.text = Loc.Get("ui.shop.included");
                    _price.color = Pal.A(Pal.Cream, .85f);
                    break;

                case StoreOfferState.AwaitingGrant:
                    Face(Skins.Gem, live: true);
                    _price.text = Loc.Get("ui.shop.awaiting_short");
                    _price.color = Pal.Cream;
                    break;

                case StoreOfferState.Purchasing:
                    Face(Skins.Buy, live: false);
                    _price.text = Loc.Get("ui.shop.purchasing");
                    _price.color = Pal.A(Pal.Cream, .85f);
                    break;

                default:
                    Face(Skins.Buy, live: false);
                    _price.text = Loc.Get("ui.shop.price_pending");
                    _price.color = Pal.A(Pal.Cream, .70f);
                    break;
            }

            // Re-centred after the caption changed, on the same rule a pill button follows: the
            // caption and the glyph are one block, and the block moves when either does. Cheap
            // enough to run on the no-glyph path too, and running it there is what puts the box
            // back to full width after a gem price has narrowed it.
            UIKit.CentreGlyph(_price, _priceMark, _priceWidth);
        }

        /// <summary>
        /// Writes the price line, with or without the gem in front of it, and re-centres the
        /// pair.
        ///
        /// One method rather than three assignments at each call site, because the caption and
        /// the glyph have to be measured together and a caller that sets one and forgets the
        /// other leaves a price shoved half a glyph off centre — the failure
        /// <see cref="UIKit.CentreGlyph"/> exists to make unforgettable.
        /// </summary>
        void SetPrice(string text, bool gem)
        {
            _price.text = text;
            SetGemMark(gem);
            UIKit.CentreGlyph(_price, _priceMark, _priceWidth);
        }

        void SetGemMark(bool on)
        {
            if (_priceMark && _priceMark.gameObject.activeSelf != on)
                _priceMark.gameObject.SetActive(on);
        }

        /// <summary>
        /// The price bar: which kit button it is, and whether it is turned down.
        ///
        /// One place rather than a sprite assignment at each of eight branches, because the
        /// tint has to be cleared as reliably as it is set — a cell is rebound rather than
        /// rebuilt (invariant 16d), so a muted face left behind by the row this cell used to
        /// be is a live price nobody believes they can tap.
        /// </summary>
        void Face(string skin, bool live)
        {
            _priceFace.sprite = Art.S("Ui/" + skin);
            _priceFace.color = live ? Color.white : Skins.Muted;
        }

        /// <summary>
        /// The one thing on the card that is the shelf's: the light under the goods.
        ///
        /// <para>
        /// Set on every draw rather than once, because a cell is rebound as the grid scrolls
        /// (invariant 16d) - and the supplies shelf mixes a gem-priced good with a real-money
        /// container, so two shelves' worth of colour can reach one cell object in one session.
        /// </para>
        /// </summary>
        void Shelf(StoreShelf shelf)
        {
            _plate.color = Color.white;
            if (_spot) _spot.color = Pal.A(Skins.Accent(shelf), SpotAlpha);
        }

        /// <summary>
        /// How strong the shelf's light is. Low on purpose: it is a wash under a painted
        /// object, and the moment it competes with the object it stops reading as light *on*
        /// it and starts reading as decoration *behind* it - which is the reading that took
        /// the ray fan and the coloured seats off this card in the first place.
        /// </summary>
        const float SpotAlpha = .22f;

        void PaintRibbon(int bonusPercent)
        {
            if (!_ribbon) return;

            bool show = bonusPercent >= 5;
            _ribbon.gameObject.SetActive(show);
            if (!show) return;

            // An arc is one label per character, so re-laying it out is destroying and
            // rebuilding them. This card is recycled and rebound as the grid scrolls and
            // repainted whenever a price arrives, so the caption is remembered and the work is
            // skipped when it has not moved — which is most binds, since a shelf of coin packs
            // carries the same handful of percentages.
            string said = Loc.Format("ui.shop.bonus", bonusPercent);
            if (said == _ribbonSaid) return;

            _ribbonSaid = said;
            UIKit.Arc(_ribbonArc, said, _ribbonFont, Pal.Sun, _ribbonRadius, 3f, 2f, 1f);
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
