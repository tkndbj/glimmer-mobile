using System;
using System.Collections.Generic;
using GlimmerGrove.Ads;
using GlimmerGrove.Analytics;
using GlimmerGrove.Cloud;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Referral;
using GlimmerGrove.Store;
using GlimmerGrove.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The shop. Gems and credits for money, hearts and faster hearts for gems.
    ///
    /// <para>
    /// The second nav tab, and the one screen in the game where a mistake is charged to
    /// somebody's card. That shapes every decision on it. <b>Nothing here ever draws a
    /// price it made up</b> — every figure with a currency symbol comes from the store SDK
    /// already formatted for the player's own storefront, and a card whose price has not
    /// arrived says so rather than guessing. <b>Nothing here is greyed out without a
    /// sentence</b>, which is <c>AdOfferState</c>'s rule: six of the states a card can be
    /// in are not failures, and a dead button with no explanation is how a player decides
    /// the shop is broken and stops opening it.
    /// </para>
    /// <para>
    /// <b>Five shelves, and the last two are a different kind of thing.</b> Gems, coins and
    /// bundles are bought with money and adjudicated by the server; supplies — hearts and
    /// boosts — are bought with gems and applied on the phone. They share a screen because
    /// they are one decision from the player's side, and they share nothing else: see
    /// <c>StoreProduct</c> for why a real-money product may only ever grant currency.
    /// </para>
    /// <para>
    /// <b>The kit shelf lists neither a product nor a good.</b> It draws
    /// <c>UtilityCatalog</c> — the same roster the action bar draws from, with the same
    /// prices, the same ceiling and the same stock behind it — because the utilities were
    /// already written down once and a second copy in the store block would be two records
    /// of one thing (<c>StoreShelf.Utilities</c>). Nothing on it can be bought with money,
    /// and nothing on it needs the store to have answered, so it is the one shelf that
    /// works with the connection down.
    /// </para>
    /// <para>
    /// <b>It pages by shelf</b>, exactly as the Grovement's shop does, for the reason
    /// <c>GridView</c> exists — and here the bound is on the *store* rather than on memory:
    /// every product id has to be fetched from Apple or Google at launch, and that call
    /// slows as the list grows. A catalog is the one thing in a live game that only ever
    /// gets longer.
    /// </para>
    /// </summary>
    public sealed class ShopScreen : View
    {
        // "hub" was not a clip. Every other screen off the map and the board names
        // mus_menu, and an address nothing can resolve throws InvalidKeyException on the
        // frame the shop opens and then plays nothing — on the one screen in the game
        // that takes money. Nothing catches a track name: Validate Art walks the assets
        // the *catalog* asks for, and a track is named by a screen.
        public override string Track => "mus_menu";

        const float HeaderHeight = 300f;
        const float TabRow = 156f;

        const int Columns = 2;
        const float CellW = 508f;
        const float CellH = 560f;

        /// <summary>
        /// The standing "not signed in" bar, and the gap under it. Reserved out of the grid's
        /// viewport only while it is drawn, so a linked player and the gem-priced shelf stay
        /// pixel-identical to what shipped rather than carrying a hole where a warning would
        /// have gone.
        /// </summary>
        const float NoticeH = 74f, NoticeGap = 14f;

        /// <summary>
        /// The band under the tab row that the store's one sentence lives in, and the room
        /// either side of it.
        ///
        /// <para>
        /// <b>It was 44 with the line sitting 21 units inside the tabs.</b> The tab row is
        /// anchored to the top of the safe area with a <em>top</em> pivot, so it runs from
        /// <c>-HeaderHeight</c> down to <c>-(HeaderHeight + TabRow)</c> and its cells are the
        /// full height of it — while the sentence was placed at <c>-HeaderHeight - TabRow + 4</c>,
        /// which is four units <em>above</em> that lower edge before its own box is counted.
        /// It drew through the bottom of five buttons, on every shelf that had anything to
        /// say, for as long as the line has existed.
        /// </para>
        /// <para>
        /// <b>Reserved rather than squeezed into the gap that was there.</b> Sized so the
        /// sentence clears the buttons above and the first row of cards below by the same
        /// <see cref="SummaryGap"/>, which is what makes it a band rather than a number that
        /// happened to fit. Everything below is measured from it, so the grid, the guest
        /// notice and the empty plate cannot fall out of step with it.
        /// </para>
        /// </summary>
        const float SummaryH = 48f, SummaryGap = 14f;

        /// <summary>The whole band when the line has something to say: gap, line, gap.</summary>
        const float SummaryLine = SummaryGap + SummaryH + SummaryGap;

        /// <summary>
        /// And what stands there when it has not.
        ///
        /// <para>
        /// <b>Reserved-always was wrong and the owner caught it by playing.</b> A band is the
        /// right shape for a sentence that <em>is</em> there — it is what stops the line being
        /// drawn through the buttons above or the cards below, which is the fault
        /// <see cref="SummaryH"/> records. But this screen is silent on almost every visit: the
        /// store answers, every card has a price, and there is no news. So the common case was
        /// paying 76 units of dead air for a sentence nobody was being shown, right under a
        /// banner that is already the biggest thing on the page.
        /// </para>
        /// <para>
        /// So the band collapses, and it collapses to <em>something</em> rather than to nought:
        /// the grid's first row would otherwise sit 16 units under the invite banner, or 16
        /// under the tab buttons on an account with no invite page, and a card touching the
        /// control above it reads as a layout that has come apart rather than as a tidy one.
        /// </para>
        /// </summary>
        const float QuietRow = 20f;

        /// <summary>
        /// Whether the line under the tabs is saying anything right now — set by
        /// <see cref="PaintNews"/>, which is the one place that decides it, and read by
        /// <see cref="SummaryRow"/>.
        /// </summary>
        bool _saying;

        /// <summary>
        /// The band under the tabs as it stands this frame.
        ///
        /// <b>An instance property rather than a constant</b>, because it moves — everything
        /// measured from it (the guest bar, the shelf's top edge, the empty plate) is therefore
        /// re-read on every repaint rather than written down once. <see cref="PaintNotice"/> is
        /// where that happens, for the reason the sum lives in <see cref="ShelfTop"/>: one
        /// place decides the shelf's top edge and everything else follows it.
        /// </summary>
        float SummaryRow => _saying ? SummaryLine : QuietRow;

        /// <summary>
        /// The invite banner under the tabs, and the air either side of it.
        ///
        /// <para>
        /// <b>A reserved band, for <see cref="SummaryH"/>'s reason and with its history.</b> The
        /// store's one sentence used to be squeezed into the gap that happened to be under the
        /// tabs, and it drew through the bottom of five buttons for as long as the line existed.
        /// The owner's instruction here was "under the tab buttons, and make sure it never
        /// overlaps with the texts" — so this is a band that everything below is measured from,
        /// rather than a card placed at a number that happens to clear things today.
        /// </para>
        /// <para>
        /// As wide as the grid it sits over — <c>CellW * Columns</c> rather than a figure — so a
        /// shelf retuned to three columns takes the banner with it.
        /// </para>
        /// <para>
        /// <b>The height was cut from 308 after the owner played it</b>, and what decides how far
        /// it may be cut is the picture rather than the room. The banner covers its window
        /// width-led (the art is 2.67:1 against a plate half again as wide), so every unit taken
        /// off the height is a unit cropped off the top and bottom of the art: at 308 the crop
        /// was a ninth at each end, at 256 it is a sixth, and below about 240 it starts eating
        /// the megaphone's cone and the chests' feet. Measured against the source rather than
        /// argued — <c>Tools/render_shop.py</c> draws the crop this number produces.
        /// </para>
        /// </summary>
        // **The height is a crop, not a scale.** The banner is fitted to the window's
        // *width* and masked, so shortening this shows less of the picture rather than a
        // smaller one — the width is untouched. At 200 the window keeps about half the
        // art's height, which still holds the whole wordmark and the chests beside it;
        // measured with `render_shop.py`, which is the only thing that can answer whether
        // a crop has eaten the lettering.
        const float ReferW = CellW * Columns, ReferH = 200f, ReferGap = 16f;

        /// <summary>
        /// The whole band, or nought where there is no invite page to reach.
        ///
        /// <para>
        /// Drawn on exactly the rule the profile's card is drawn on
        /// (<c>ReferralLedger.IsAvailable</c>): a card promising chests a server cannot pay is
        /// the one lie a storefront must not tell. Nought rather than hidden, because a band
        /// nothing stands in is a hole in the middle of the shop.
        /// </para>
        /// </summary>
        static float ReferRow => ReferralLedger.IsAvailable ? ReferGap + ReferH + ReferGap : 0f;

        /// <summary>
        /// The top of everything under the chrome: the header, the tabs, and the invite band if
        /// there is one. <b>Written once</b>, because the summary line, the guest notice, the
        /// grid and the empty plate all hang off it and four copies of a sum is four places for
        /// a band to be forgotten.
        /// </summary>
        static float ShelfTop => HeaderHeight + TabRow + ReferRow;

        RectTransform _viewport, _tabs;
        GridView _grid;
        Text _summary;

        /// <summary>
        /// The centred sentence an empty shelf carries. Built once and left blank, because a
        /// label created when a shelf empties is a label that arrives a frame after the cards
        /// leave. See <see cref="PaintNews"/> for when it is the one that speaks.
        /// </summary>
        Text _empty;

        /// <summary>The plate that sentence stands on. Shown and hidden with it.</summary>
        Image _emptyPlate;
        Btn _restore, _notice;

        readonly List<StoreProduct> _products = new List<StoreProduct>();
        readonly List<StoreGood> _goods = new List<StoreGood>();
        readonly List<UtilityItem> _kit = new List<UtilityItem>();

        /// <summary>
        /// The rewarded offer standing in this shelf's first spot, or <c>AdOffer.None</c>.
        ///
        /// <para>
        /// <b>Two shelves have one</b>, and they are the two the hub already offers a video for:
        /// coins and hearts (<see cref="ShopAdShelf"/>). It was reachable only from the
        /// <c>+</c> on the hub's own pills, which is the one place in the game a player is
        /// <em>not</em> thinking about buying anything — somebody who has come to the shop for
        /// coins has already decided they want coins, and the free way to get some was on
        /// another screen.
        /// </para>
        /// <para>
        /// <b>First, not last.</b> Every other card on these shelves is sorted cheapest first
        /// and this one costs nothing, so the top-left cell is where the ladder already says it
        /// goes — and a free offer buried under six prices is an offer nobody scrolls to.
        /// </para>
        /// <para>
        /// Resolved in <see cref="Reload"/> rather than asked for on every bind, because
        /// whether the row exists at all decides how many rows the grid has: an offer the
        /// content table does not carry takes the card off the shelf outright, which is how a
        /// config push switches the whole thing off with no build (<c>AdRewardTable</c>).
        /// </para>
        /// </summary>
        /// <summary>
        /// The free spots at the head of this shelf, in the order they are drawn.
        ///
        /// <b>A list because a shelf may stand more than one</b> (see <c>ShopAdShelf.All</c>), and
        /// every row below them is shifted by however many are *valid* — a placement the
        /// published table does not carry is not a hole, it is simply absent, so switching one
        /// off stays a content push.
        /// </summary>
        readonly List<AdOffer> _ads = new List<AdOffer>();

        readonly Dictionary<StoreShelf, ShelfTab> _tabViews = new Dictionary<StoreShelf, ShelfTab>();

        /// <summary>
        /// Which shelf is showing.
        ///
        /// Gems lead, and that is a merchandising decision worth stating: gems are what
        /// hearts and boosts are bought with, so they are the shelf every other shelf
        /// eventually points back at. Reset on every visit, deliberately — a shop that
        /// opens where you left it opens somewhere you have to notice.
        /// </summary>
        StoreShelf _shelf = StoreShelf.Gems;

        bool OnSupplies => _shelf == StoreShelf.Supplies;

        /// <summary>
        /// The kit shelf, which is drawn from the utility catalog rather than from the store.
        ///
        /// Asked in every place <see cref="OnSupplies"/> is, because it is the other shelf whose
        /// rows are not <see cref="_products"/> — and it is asked <em>first</em> everywhere, since
        /// a shelf carrying no products at all must never reach a path that measures itself
        /// against the store's answer.
        /// </summary>
        bool OnUtilities => _shelf == StoreShelf.Utilities;

        // Money first, then the two shelves priced in gems, and the kit last. That is the
        // order somebody arrives in rather than a ranking: a player on this screen at all is
        // usually there for gems, and the kit is what gems are *for* on the one mode that
        // ships — so it sits at the far end of the row where a player who came looking for it
        // will still find it, and where nobody is asked to step over it on the way to a price.
        static readonly StoreShelf[] Shelves =
        {
            StoreShelf.Gems, StoreShelf.Coins, StoreShelf.Bundles, StoreShelf.Supplies,
            StoreShelf.Utilities,
        };

        protected override void Build()
        {
            // The kit's own machine room rather than the flat page this screen used to be,
            // and rather than the forest every other screen stands in.
            //
            // <para>
            // The flat page was right while the cards were saturated blocks of colour: the
            // ground's one job was not to compete, which is CRAFT.md's plate rule asked of a
            // storefront. The cards are teal machinery now, so the argument runs the other way
            // - a shop floating on nothing reads as a menu, and what makes a storefront a
            // *place* is that it is somewhere. The room is dark, out of focus and has nothing
            // in its middle, which is what lets it be a place and still not compete.
            // </para>
            Scenery.Plain(Content);

            BuildGrid();
            BuildHeader();
            NavBar.Build(Content, NavBar.Tab.Shop);

            Reload();

            // Every one of these is a repaint rather than a rebuild — see GridView.Refresh.
            //
            // Note what is *not* here: the thank-you panel. A grant can land while the player
            // is anywhere — the payment sheet outlives the screen that opened it, and an
            // interrupted purchase is credited on the next launch, from the splash — so the
            // panel is raised by `Boot` for every screen at once. A screen that raised its
            // own would be a celebration nobody sees on the two occasions it matters most.
            StoreService.Changed += OnStoreChanged;
            StoreService.Granted += OnGranted;
            StoreService.Failed += OnFailed;

            // The supplies shelf is priced in gems and gated on hearts, so both move it — the
            // *cards*, that is. The three balance pills above them are watched by the
            // `WalletWatch` that `BuildBalances` attaches, which is the one place in the game
            // that subscribes to the wallet's own two cues.
            PlayerProgression.Changed += Repaint;
            Wallet.HeartsChanged += OnHeartsChanged;

            // And a container bought — or refunded by a sync — moves what the shelf says the
            // player's limit is, and turns the card that sold it into YOURS. A repaint rather
            // than a reload: the same cards, redrawn, at the moment the player is watching
            // one of them land. See the house rule about Show and Refresh.
            HeartContainerLedger.Changed += Repaint;

            // A content push can retune the whole shop, including which products exist —
            // and which utilities do, since the kit shelf is the catalog itself.
            ProgressionRules.Changed += Reload;

            // What the player is carrying is on every kit card, and it moves from three places
            // this screen cannot see: a chest opened on the hub, a run that spent one, and a
            // sync landing another device's pack. A repaint rather than a reload — the same
            // cards, redrawn (invariant 16d).
            UtilityLedger.Changed += Repaint;

            // The notice is a claim about the account, so it has to follow the account. A
            // player taps it, links, and comes back to a shelf that would otherwise still be
            // telling them their purchases are stranded on this phone — and the panel they
            // linked from has four exits, so an event is the only thing that catches all of
            // them. See CloudSaveService.IdentityChanged.
            CloudSaveService.IdentityChanged += Repaint;
        }

        void OnDestroy()
        {
            StoreService.Changed -= OnStoreChanged;
            StoreService.Granted -= OnGranted;
            StoreService.Failed -= OnFailed;
            PlayerProgression.Changed -= Repaint;
            Wallet.HeartsChanged -= OnHeartsChanged;
            HeartContainerLedger.Changed -= Repaint;
            ProgressionRules.Changed -= Reload;
            UtilityLedger.Changed -= Repaint;
            CloudSaveService.IdentityChanged -= Repaint;
        }

        public override bool OnBack() { Flow.Go<HomeScreen>(); return true; }

        void OnHeartsChanged(Hearts hearts) => Repaint();

        /// <summary>
        /// The store's own state changed: it connected, prices arrived, or a purchase moved
        /// into or out of the queue waiting to be credited.
        ///
        /// <para>
        /// A repaint in every ordinary case, because none of that changes which products exist
        /// and a rebuild would replay every card's entrance at the moment a player is watching
        /// a purchase land. The exception is the store answering for the first time: the shelf
        /// now lists only what the store carries, so the set genuinely grows when a connection
        /// completes, and a repaint alone would leave those cards missing until the player
        /// changed tabs. <see cref="ShelfCount"/> is compared rather than a connection flag
        /// because a storefront can also drop a product it previously offered.
        /// </para>
        /// </summary>
        void OnStoreChanged()
        {
            // The kit shelf lists nothing the store has ever heard of, so a connection landing
            // cannot change what is on it — and ShelfCount would answer nought against a page
            // of cards and reload the shelf out from under a player mid-scroll.
            if (OnUtilities) return;

            // Supplies is included now, and it has to be: since heart containers went on that
            // shelf it carries real-money products too, so its cards genuinely appear when the
            // store first answers — which is the exact case this comparison exists for. Its
            // gem-priced half never moves, so the count still only changes for the reason
            // described above.
            if (ShelfCount() != _products.Count) Reload();
            else Repaint();
        }

        /// <summary>
        /// How many products the store would show on this shelf right now.
        ///
        /// Counted rather than rebuilt, so the common case — a price arriving, a purchase
        /// settling — costs a walk of the catalog and no allocation, and never disturbs the
        /// cells a player is looking at.
        /// </summary>
        int ShelfCount()
        {
            int count = 0;
            foreach (var product in StoreRules.Catalog.Products)
            {
                if (product.Shelf != _shelf) continue;
                if (StoreService.OfferFor(product).State == StoreOfferState.Missing) continue;
                count++;
            }
            return count;
        }

        // ---------------------------------------------------------------- header
        void BuildHeader()
        {
            // A wash under the header, so the title, the balances and the tabs are read against
            // something rather than against whatever part of the world happens to be behind
            // them.
            //
            // **.62 rather than .88, and it is the world that moved rather than the taste.**
            // Under the last kit what was behind this band was a flat near-black ground, so an
            // almost-opaque wash cost nothing and bought contrast for free. Over an illustrated
            // world it is the difference between a header that sits *in* the picture and a
            // black bar laid across the top of it — and the pieces standing on it are opaque
            // navy plates with their own keylines now, so most of the contrast it used to buy
            // is already paid for.
            var fade = UIKit.Img("TopFade", Content, Art.FadeUp(64), new Color(.02f, .05f, .09f, .62f));
            var frt = (RectTransform)fade.transform;
            frt.anchorMin = new Vector2(0f, 1f); frt.anchorMax = new Vector2(1f, 1f);
            frt.pivot = new Vector2(.5f, 1f);
            frt.sizeDelta = new Vector2(0f, HeaderHeight + TabRow);
            frt.anchoredPosition = Vector2.zero;
            frt.localRotation = Quaternion.Euler(0, 0, 180f);

            UIKit.IconButton("Back", Safe, Skins.Nav, "ic_left", new Vector2(112f, 112f),
                             new Vector2(0f, 1f), new Vector2(94f, -128f), () => Flow.Go<HomeScreen>());

            // The kit's title plate, which is the same rim a currency readout sits in one row
            // below. That is deliberate rather than thrifty: a word the game owns and a number
            // the game owns are the same kind of thing, and one rim is one thing to learn. It
            // replaced a tab chip stretched into a banner, which was the nearest shape the last
            // kit had and read as a tab that had grown.
            var banner = UIKit.Img("Banner", Safe, Art.S("Ui/" + Skins.Title), Color.white,
                                   new Vector2(440f, 104f), new Vector2(.5f, 1f), new Vector2(0f, -112f));
            // Bent to the plate's own top edge rather than set on a straight baseline. The
            // kit draws this ribbon with a raised middle and tails that fall away, so a
            // straight word inside it reads as a label that happens to be sitting on a curved
            // thing. 620 is the radius the plate's own arc is drawn at; see `UIKit.Arced`.
            //
            // Not `Shrinkable`, because best-fit works on one label's box and this is one label
            // per character — a translated title that outgrew the plate would need a smaller
            // `size` here, which is a decision rather than something to leave to a fitter.
            UIKit.Arced("Title", banner.transform, Loc.Get("ui.nav.shop").ToUpperInvariant(), 40,
                        Pal.Sun, 620f, new Vector2(.5f, .5f), new Vector2(0f, 104f * Skins.RibbonLift), 3f, 3f, 2f);

            BuildBalances();
            BuildTabs();
            BuildInvite();
            BuildNotice();
        }

        /// <summary>
        /// The invite banner, directly under the tabs: the profile's card, on the storefront.
        ///
        /// <para>
        /// <b>The same control twice rather than a second drawing of one idea</b> — the same
        /// painted banner, the same plate, the same window cut with a <c>Mask</c>, the same
        /// slow swell, the same destination. A shop is where somebody is already thinking about
        /// what things cost, which is the one place a free source of chests is worth saying out
        /// loud; and it costs no art, because the picture is already resident
        /// (<c>AssetManifest</c>).
        /// </para>
        /// <para>
        /// See <c>ProfileScreen.BuildInviteCard</c> for why the banner is cut smaller than its
        /// window rather than larger, and for the one thing this shape costs: the words are
        /// painted into the picture, so neither of these two is translated until the art is
        /// re-cut.
        /// </para>
        /// </summary>
        void BuildInvite()
        {
            if (!ReferralLedger.IsAvailable) return;

            var card = UIKit.Button("Invite", Safe, Art.S("Ui/" + Skins.PlateBlue),
                                    new Vector2(ReferW, ReferH), new Vector2(.5f, 1f),
                                    new Vector2(0f, -(HeaderHeight + TabRow + ReferGap + ReferH * .5f)),
                                    () => Flow.Go<ReferralScreen>());
            card.PressScale = .985f;

            var clip = UIKit.Img("Clip", card.transform, Art.S("Ui/" + Skins.PlateBlue),
                                 new Color(1f, 1f, 1f, .004f));
            var crt = (RectTransform)clip.transform;
            UIKit.StretchTo(crt, BannerInset, BannerInset, BannerInset, BannerInset);
            clip.type = Image.Type.Sliced;
            clip.raycastTarget = false;
            clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            var banner = Art.S("Ui/refer");
            float aspect = banner != null && banner.rect.height > 0f
                         ? banner.rect.width / banner.rect.height
                         : 0f;

            float windowW = ReferW - BannerInset * 2f, windowH = ReferH - BannerInset * 2f;
            float drawnW = windowW, drawnH = aspect > 0f ? drawnW / aspect : 0f;
            if (drawnH < windowH) { drawnH = windowH; drawnW = drawnH * aspect; }

            // `seated` cuts the picture so the *crest* of its breath is the cover fit exactly
            // — at the top of the swell the banner is the window and never a pixel past it, which
            // is the whole reason the swell is safe inside a mask. `BannerFill` is the separate
            // decision on top: how much of that fit the picture actually draws at.
            float seated = BannerFill / (1f + BannerSwell);
            drawnW *= seated;
            drawnH *= seated;

            var art = UIKit.Img("Banner", crt, banner, Color.white,
                                new Vector2(drawnW, drawnH), new Vector2(.5f, .5f), Vector2.zero);
            art.raycastTarget = false;
            art.enabled = aspect > 0f;

            if (art.enabled) Tween.Breathe(art.transform, BannerSwell, BannerPeriod);

            card.transform.localScale = Vector3.zero;
            Tween.Pop(card.transform, 0f, .55f, .18f);
        }

        /// <summary>The banner's window inset, its swell and how long the swell takes.</summary>
        const float BannerInset = 8f;
        const float BannerSwell = .03f, BannerPeriod = 3.6f;

        /// <summary>
        /// How much of the cover fit the picture draws at.
        ///
        /// <para>
        /// <b>1 is the fit exactly</b> — the banner is the window and never a pixel past it —
        /// and anything under it does two things at once, which is worth knowing before reaching
        /// for it: the picture pulls in from the window's edges, *and* it un-crops, because a
        /// cover fit is only cropped in the first place by being larger than what shows. At .9
        /// the art's own sky reads as a margin rather than the plate showing through, which is
        /// why it stays a scale rather than becoming a padding.
        /// </para>
        /// </summary>
        const float BannerFill = .90f;

        /// <summary>
        /// The standing warning on the shelves priced in real money: this phone is not signed
        /// in, so anything bought here is tied to an account that dies with the installation.
        ///
        /// <para>
        /// <b>Why a bar and not a dialog.</b> Everything else about a purchase on this screen
        /// is deliberately un-interrupted — the payment sheet is the confirmation and nothing
        /// stands in front of it — so the honest way to warn somebody is to have the warning
        /// already there when they arrive, rather than to stop them once they have decided. It
        /// costs no tap, it is true every time it is drawn, and it is what allows the panel
        /// that <em>does</em> interrupt to be as rare as <c>AccountPromptPolicy</c> makes it.
        /// </para>
        /// <para>
        /// <b>Only the money shelves.</b> Supplies are priced in gems, and hearts and boosts
        /// live in the save, which merges into whatever account this device eventually links —
        /// nothing bought there can be lost. Warning about it anyway would put the sentence on
        /// a shelf where it is false, and a warning that is sometimes false is the fastest way
        /// to teach somebody to read past it.
        /// </para>
        /// <para>
        /// Built once and shown or hidden, never created on demand: this sits on a repaint
        /// path, and a bar destroyed and rebuilt flashes every time a price arrives.
        /// </para>
        /// </summary>
        void BuildNotice()
        {
            _notice = UIKit.Button("GuestNotice", Safe, Art.Round(18), new Vector2(1000f, NoticeH),
                                   new Vector2(.5f, 1f),
                                   new Vector2(0f, -(ShelfTop + SummaryRow) - NoticeH * .5f),
                                   OnNoticeTapped);

            var plate = _notice.GetComponent<Image>();
            if (plate) plate.color = new Color(.17f, .11f, .05f, .92f);

            var edge = UIKit.Img("Edge", _notice.transform, Art.RoundOutline(18, 2.5f),
                                 Pal.A(Pal.Sun, .55f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            var glyph = UIKit.Img("Icon", _notice.transform, Art.S("Ui/ic_profile"), Pal.Sun,
                                  new Vector2(42f, 42f), new Vector2(0f, .5f), new Vector2(46f, 0f));
            glyph.preserveAspect = true;

            // Wrapped and shrinkable, because it is a translated sentence on a fixed bar — the
            // lesson the victory panel's two lines cost twice. UIKit.Label overflows rather
            // than clipping, so an over-long line is not truncated, it simply keeps drawing.
            UIKit.Shrinkable(
                UIKit.Titled("Label", _notice.transform, Loc.Get("ui.shop.guest_notice"), 25,
                             Pal.A(Pal.Cream, .94f), TextAnchor.MiddleLeft,
                             new Vector2(788f, 58f), new Vector2(0f, .5f), new Vector2(486f, 0f),
                             3f, 0f, wrap: true), 17);

            var chevron = UIKit.Img("More", _notice.transform, Art.S("Ui/ic_right"),
                                    Pal.A(Pal.Sun, .82f), new Vector2(32f, 32f),
                                    new Vector2(1f, .5f), new Vector2(-40f, 0f));
            chevron.preserveAspect = true;

            _notice.gameObject.SetActive(false);
        }

        /// <summary>
        /// The bar was tapped, which is a player asking rather than the game asking — so it
        /// spends no budget and starts no quiet period, and it is counted separately from
        /// <c>account_prompt_shown</c> for exactly that reason. Telling the two apart is what
        /// answers whether the standing notice does the work on its own.
        /// </summary>
        void OnNoticeTapped()
        {
            Telemetry.Track("account_notice_tapped", "shelf", _shelf.ToString());
            Flow.Modal<AccountOverlay>();
        }

        /// <summary>
        /// Shows or hides the notice and gives the grid back the room when it is hidden, and
        /// reports whether the viewport actually moved.
        ///
        /// <para>
        /// Called before the grid is told to lay out, because <c>GridView</c> measures the
        /// viewport to decide how many rows it needs. The return value matters for the same
        /// reason: <c>Refresh</c> rebinds the cells that are already live and deliberately does
        /// <em>not</em> re-measure, so a repaint that changes the viewport's height has to ask
        /// for a full relayout or the grid is left sized for the height it used to have — a
        /// content rect shorter than its window makes a <c>ScrollRect</c> bounce against
        /// nothing. There is exactly one way to reach that: linking an account while standing
        /// on a money shelf, which is the whole flow this notice exists to start.
        /// </para>
        /// </summary>
        bool PaintNotice()
        {
            if (_notice == null || _viewport == null) return false;

            // Every shelf, since the supplies shelf started carrying heart containers.
            //
            // It used to be hidden here, and the reasoning was right at the time: hearts and
            // boosts live in the save, which merges into whatever account this device links,
            // so nothing bought on this shelf could be lost and warning about it would have
            // put a false sentence on the one page where it was false. A container is also in
            // the save and also merges — but it is bought with real money, and "anything you
            // buy stays on this phone only" is a sentence that has to be true wherever money
            // changes hands. What makes it true rather than merely cautious: the receipt is
            // redeemed against *this* account, so a guest who reinstalls without linking gets
            // the container back from the store's own Restore and never gets the gems back.
            // Never on the kit shelf, for the reason it used to be off the supplies shelf and
            // still would be but for heart containers: everything here is priced in gems and
            // lands in the save, which merges into whatever account this device eventually
            // links, so nothing bought on it can be stranded. A warning that is sometimes false
            // is the fastest way to teach somebody to read past it.
            bool show = AccountPrompts.ShouldWarn && !OnUtilities
                        && (!OnSupplies || HasMoneyOnShelf());
            if (_notice.gameObject.activeSelf != show) _notice.gameObject.SetActive(show);

            // **<see cref="ShelfTop"/>, not the two terms it is made of.** This sum used to be
            // spelled out here and left the invite band out of it, so the first repaint after
            // the page was built pulled the shelf up over the banner and the top row of cards
            // was drawn through it — the exact failure the "written once" note on `ShelfTop`
            // exists to prevent, committed in the one place that did not read it. The mirror
            // could not see it either, because `render_shop.py` draws no guest notice and so
            // never walks this path.
            // The guest bar rides on the band as well, so it is placed here rather than where
            // it was built: a bar written down once sits at the height the band had on the
            // frame the screen was made, and the band moves.
            if (_notice)
                ((RectTransform)_notice.transform).anchoredPosition =
                    new Vector2(0f, -(ShelfTop + SummaryRow) - NoticeH * .5f);

            float top = -ShelfTop - SummaryRow - (show ? NoticeH + NoticeGap : 0f);

            // **The empty sentence follows the shelf's top edge from here, because here is the
            // one place that edge is decided.** Written anywhere else it would be a second copy
            // of this sum, and the two would agree right up until the day somebody sees the
            // guest notice over an unreachable shelf — at which point the message explaining
            // why the page is blank would be drawn straight through the bar above it. Set
            // before the early return, so it is right even on the call where nothing moved.
            //
            // **The plate, not the label on it.** This moved `_empty` for as long as the plate
            // has existed: the sentence used to be a sibling of the viewport and was given a
            // plate to stand on, which made it a *child* — and `EmptyY` is an absolute y under
            // the safe area, so writing it onto a child wrote about nine hundred units of local
            // offset and threw the sentence off the bottom of the plate it was standing on.
            // Every repaint did it, so the one state this label exists for never drew right.
            // `EmptyY`'s own summary says "where the plate is anchored", which is what it has
            // always been for.
            if (_emptyPlate)
                _emptyPlate.rectTransform.anchoredPosition = new Vector2(0f, EmptyY(top));

            if (Mathf.Approximately(_viewport.offsetMax.y, top)) return false;

            _viewport.offsetMax = new Vector2(_viewport.offsetMax.x, top);
            return true;
        }

        /// <summary>
        /// Whether the shelf being shown has anything on it priced in real money.
        ///
        /// Only ever false on a supplies shelf whose containers the store has not answered
        /// for — which is a shelf of gem-priced goods and nothing else, exactly what it was
        /// before containers existed.
        /// </summary>
        bool HasMoneyOnShelf() => _products.Count > 0;

        /// <summary>
        /// The three balances, because every price on this page is measured against one of
        /// them.
        ///
        /// <para>
        /// Hearts are here as well as coins and gems, and that is not symmetry for its own
        /// sake: the supplies shelf sells hearts, and the one thing a player has to know
        /// before buying five is how many they are already holding. It is also what makes
        /// the "your hearts are nearly full" refusal read as a fact rather than as an
        /// excuse.
        /// </para>
        /// <para>
        /// No <c>+</c> buttons on these, unlike the hub's. On the hub a <c>+</c> opens the
        /// panel for that resource; here the panel <em>is</em> the screen, and a control
        /// that scrolls you to a different tab of the page you are already on is a control
        /// that answers a question nobody asked.
        /// </para>
        /// </summary>
        void BuildBalances()
        {
            // -228 rather than -214: the banner is 140 tall about y=-116, so its lower edge
            // sits at -186 and a 76-tall row centred at -214 climbs to -176 — ten units
            // *inside* the ribbon. Nothing else moves; the row still clears the tab strip
            // at -300 by 34.
            var row = UIKit.Row("Balances", Safe, new Vector2(1000f, 76f), new Vector2(.5f, 1f),
                                new Vector2(0f, -228f), 14f);

            BalancePill(row, Pal.Gold, null, Compact.Number(Profile.Coins),
                        ResourceSlots.Kind.Credits, Compact.Number);
            BalancePill(row, Pal.Bloom, "ic_gem", Compact.Number(Profile.Gems),
                        ResourceSlots.Kind.Gems, Compact.Number);
            BalancePill(row, Pal.Rose, "ic_heart", Profile.HeartsLabel(),
                        ResourceSlots.Kind.Hearts, n => Profile.HeartsLabel((int)n));

            WalletWatch.Attach(this, ResourceSlots.Kind.Credits, ResourceSlots.Kind.Gems,
                               ResourceSlots.Kind.Hearts);
        }

        /// <remarks>
        /// Each pill registers itself with <see cref="ResourceSlots"/> as it is built, for the
        /// reason the hub's do: it is what lets a panel drawn on top of this screen fly what it
        /// paid into this row. The shop is the second row to register and the reason
        /// <c>ResourceSlots.Slot.Rest</c> exists — the halo here is narrower and dimmer than the
        /// hub's, and a flare that returned to a figure the registry had assumed would leave one
        /// of the two rows permanently the wrong brightness.
        /// </remarks>
        static void BalancePill(Transform row, Color tint, string icon, string value,
                                ResourceSlots.Kind kind, Func<long, string> format)
        {
            // The kit's trough, nine-sliced and drawn at white: it carries its own dark
            // interior and its own orange rim, so there is nothing here to tint and nothing to
            // trace. It used to be a flat chip tinted by hand. The "+" that used to sit on the
            // end has gone: on the hub it is a control that opens a panel, and here the panel
            // *is* the screen, so it was a button promising to take you where you already are.
            var pill = UIKit.Img("Pill", row, Art.S("Ui/" + Skins.Trough), Color.white,
                                 new Vector2(228f, 74f), new Vector2(.5f, .5f), Vector2.zero);

            // 62 rather than 38, and it is the trough's own geometry rather than taste: the
            // kit clips each end of the plate and a clip is 43 units wide whatever width the
            // plate is drawn at, so a glyph 38 in from the edge is a glyph standing on the pipe.
            var glow = UIKit.Img("Glow", pill.transform, Art.Glow(96, 2f), Pal.A(tint, .22f),
                                 new Vector2(96f, 96f), new Vector2(0f, .5f), new Vector2(62f, 0f));

            var glyph = UIKit.Img("Icon", pill.transform, icon == null ? null : Art.S("Ui/" + icon),
                                  Color.white, new Vector2(48f, 48f), new Vector2(0f, .5f),
                                  new Vector2(62f, 0f));
            glyph.preserveAspect = true;

            // The coin is the hub's own spinning one. Consistency here is worth the two
            // extra draws: the pile on a card is made of this coin, so the pill and the
            // product read as the same thing.
            if (icon == null) Flipbook.Attach(glyph, "Ui/Coin", 11f);

            var text = UIKit.Shrinkable(
                UIKit.Titled("V", pill.transform, value, 30, Pal.Cream, TextAnchor.MiddleCenter,
                             new Vector2(120f, 44f), new Vector2(.5f, .5f), new Vector2(26f, 0f), 3f, 3f), 18);

            ResourceSlots.Register(kind, (RectTransform)glyph.transform, text, glow, tint, format);
        }

        /// <summary>
        /// One tab per shelf, built once and restyled — <c>HomesteadShopScreen</c>'s rule,
        /// for its reason: a row rebuilt on every repaint flashes every time a price
        /// arrives, and nothing about a tab depends on what the store said.
        /// </summary>
        void BuildTabs()
        {
            _tabs = UIKit.Node("Tabs", Safe);
            _tabs.anchorMin = new Vector2(0f, 1f);
            _tabs.anchorMax = new Vector2(1f, 1f);
            _tabs.pivot = new Vector2(.5f, 1f);
            _tabs.sizeDelta = new Vector2(0f, TabRow);
            _tabs.anchoredPosition = new Vector2(0f, -HeaderHeight);

            float step = Mathf.Min(230f, 1020f / Shelves.Length);

            for (int i = 0; i < Shelves.Length; i++)
            {
                var shelf = Shelves[i];
                float x = (i - (Shelves.Length - 1) * .5f) * step;

                _tabViews[shelf] = new ShelfTab(_tabs, shelf, step, x, () => Show(shelf));
            }

            // Centred in its own reserved band, so the only way it can touch the buttons above
            // or the cards below is if `SummaryRow` stops being the sum of its own parts.
            // Written as that sum rather than as a figure for exactly that reason — a position
            // typed as a number is a position that survives the band being retuned.
            //
            // The box is centre-pivoted (`UIKit.Box` always is, 44d), so this is the middle of
            // the line and the gap above it has to be counted in: the band's top edge is the
            // tab row's bottom, then `SummaryGap`, then half the line.
            _summary = UIKit.Shrinkable(
                UIKit.Titled("Summary", Safe, string.Empty, 30,
                             new Color(1f, .96f, .88f, .86f), TextAnchor.MiddleCenter,
                             new Vector2(880f, SummaryH), new Vector2(.5f, 1f),
                             new Vector2(0f, -(ShelfTop + SummaryGap + SummaryH * .5f)),
                             3f, 0f), 20);

            PaintTabs();
        }

        void Show(StoreShelf shelf)
        {
            if (_shelf == shelf) return;
            _shelf = shelf;
            Reload();
        }

        // ------------------------------------------------------------------ grid
        void BuildGrid()
        {
            _viewport = UIKit.Node("Viewport", Safe);
            _viewport.offsetMin = new Vector2(0f, NavBar.Height + RestoreRow);
            _viewport.offsetMax = new Vector2(0f, -ShelfTop - SummaryRow);

            _grid = GridView.Attach(_viewport, Columns, CellW, CellH,
                                    parent => new ShopCell(this, parent));

            // The centred sentence for a shelf with nothing on it.
            //
            // **A sibling of the viewport rather than a child of it**, which is the difference
            // between a message and a row: the viewport is a `ScrollRect` whose content the
            // grid owns and recycles, so anything parented inside it either scrolls away or is
            // rebound out of existence the next time the shelf changes.
            //
            // It cannot overlap the cards, because it is only ever written to when there are
            // none (`PaintNews`), and it cannot overlap the notice bar, because its y is set
            // from the viewport's own top edge in the one place that edge is decided. Both are
            // structural rather than a margin somebody chose.
            // **On a plate, because a sentence this screen is *about* is not a caption.** The
            // boards draw their empty line straight onto the ground and get away with it: it
            // is one of five states a list can be in and it is read in passing. This one is
            // the entire content of the page — a shelf with nothing on it and no explanation
            // is the blank screen invariant 18f and `AdOfferState` both exist to prevent — so
            // it is given the furniture every other sentence in this game stands on.
            //
            // The ground underneath is `Scenery.Plain` (see `Build`), which is quiet enough
            // that the plate is a choice rather than a rescue. That is worth writing down,
            // because the render mirror drew this screen on `Scenery.Room` — a painted forest
            // the shop has not stood on — for as long as it has existed, and a plate argued
            // for against *that* picture would have been an argument about a screen the game
            // does not draw. Fixed in `render_shop.py` in the same change (44d).
            _emptyPlate = UIKit.Img("EmptyPlate", Safe, Art.Round(24),
                                    new Color(.05f, .09f, .18f, .86f),
                                    new Vector2(EmptyBoxW, EmptyBoxH), new Vector2(.5f, 1f),
                                    new Vector2(0f, EmptyY(_viewport.offsetMax.y)));

            var emptyEdge = UIKit.Img("Edge", _emptyPlate.transform, Art.RoundOutline(24, 2.5f),
                                      Pal.A(Pal.Sun, .40f));
            UIKit.StretchTo((RectTransform)emptyEdge.transform, 0, 0, 0, 0);

            // Centred in its own plate, which is the arrangement with no pivot arithmetic in
            // it at all: `UIKit.Box` always pivots at centre (44d), so a `MiddleCenter` label
            // filling a centre-pivoted plate lands where the plate is and nowhere else. The
            // same sentence anchored `UpperCenter` against a drop would start half a box high
            // — the sign error that put the update wall's line through its own mark (49h).
            _empty = UIKit.Shrinkable(
                UIKit.Titled("Empty", _emptyPlate.transform, string.Empty, 30,
                             Pal.A(Pal.Cream, .94f), TextAnchor.MiddleCenter,
                             new Vector2(EmptyBoxW - 60f, EmptyBoxH - 28f),
                             new Vector2(.5f, .5f), Vector2.zero, 3f, 0f, wrap: true), 19);

            _emptyPlate.gameObject.SetActive(false);

            BuildRestore();
        }

        /// <summary>
        /// How far under the top of the shelf the empty sentence's plate begins. Far enough
        /// not to read as a caption hanging off the tab row, and nowhere near far enough to be
        /// mistaken for centred in the page — a message a player has to hunt for is the fault
        /// it exists to fix.
        /// </summary>
        const float EmptyDrop = 120f;

        /// <summary>
        /// The plate the sentence stands on. Deep enough for three lines of a translation at
        /// the floor size, because <c>UIKit.Shrinkable</c> truncates what will not fit and
        /// does it silently (invariant 19n).
        /// </summary>
        const float EmptyBoxW = 880f, EmptyBoxH = 140f;

        /// <summary>
        /// Where the empty sentence's plate is anchored, given the top edge of the shelf.
        /// Half a plate, because <c>UIKit.Box</c> pivots at centre and the drop is measured to
        /// the plate's top edge.
        /// </summary>
        static float EmptyY(float shelfTop) => shelfTop - EmptyDrop - EmptyBoxH * .5f;

        const float RestoreRow = 92f;

        /// <summary>
        /// Restore purchases.
        ///
        /// <para>
        /// Apple requires a control for this in any app selling a non-consumable, and the
        /// starter bundle is one — so this is not optional furniture, it is a review item.
        /// It is also the manual form of what the game already does by itself on every
        /// launch, which is why it is a quiet line rather than a button: the honest thing to
        /// tell somebody whose purchase has not landed is "it will", and this is for the
        /// cases where they would rather not wait to find out.
        /// </para>
        /// <para>
        /// It cannot double-grant. Every re-delivered transaction carries the id the server
        /// has already recorded, so a restore either credits something that was genuinely
        /// missed or does nothing at all.
        /// </para>
        /// </summary>
        void BuildRestore()
        {
            _restore = UIKit.TextButton("Restore", Content, Skins.Resting,
                                        Loc.Get("ui.shop.restore"), 26,
                                        new Vector2(420f, 72f), new Vector2(.5f, 0f),
                                        new Vector2(0f, NavBar.Height + RestoreRow * .5f), OnRestore);
            UIKit.Shrinkable(_restore.Label, 16);
        }

        void OnRestore()
        {
            var result = StoreService.Restore();

            Scenery.Toast(Content,
                          result.Ok ? Loc.Get("ui.shop.restore_started")
                                    : Loc.Get("ui.shop.offline"),
                          result.Ok ? Pal.Aqua : Pal.Sun);
        }

        /// <summary>
        /// Rebuilds the list this shelf shows and hands it to the grid as a new page.
        ///
        /// Called when the shelf changes and when a content push replaces the catalog — the
        /// two moments the <em>contents</em> of the page differ. Everything else, including
        /// prices arriving, is a <see cref="Repaint"/>.
        /// </summary>
        void Reload()
        {
            if (_grid == null) return;

            var catalog = StoreRules.Catalog;

            _products.Clear();
            _goods.Clear();
            _kit.Clear();

            // The free spot, resolved before anything else because it shifts every row under
            // it. `Offer` answers `None` for a placement the published table does not carry,
            // and `AdOffer.IsValid` is what the card and the row count both ask — so switching
            // the offer off is a content push and not a build, and nothing here has to know
            // that it happened.
            _ads.Clear();
            foreach (string placement in ShopAdShelf.All(_shelf))
            {
                var offer = RewardedAds.Table.Offer(placement);
                if (offer.IsValid) _ads.Add(offer);
            }

            // The goods this shelf sells, whichever shelf it is. Filled before the branch below
            // because both shelves have some now: hearts and heart boosts are supplies, an XP
            // boost is a utility (`StoreGoodKinds.ShelfFor`).
            foreach (var good in catalog.Goods)
                if (StoreGoodKinds.ShelfFor(good.Kind) == _shelf) _goods.Add(good);

            if (OnUtilities)
            {
                // In the catalog's own authored order, which is the order the action bar draws
                // them in (`UtilityItem.Order`) — so the shelf and the bar are one row of the
                // same four things and nobody has to learn a second arrangement. Deliberately
                // not sorted by price: the money shelves sort that way because a rung's picture
                // is derived from its price, and these are four different objects rather than
                // four sizes of one.
                //
                // A chest-only utility is *listed*, not hidden. It has a picture, a sentence and
                // a stock, and the card says where it comes from — where leaving it out would be
                // a player wondering why the thing in their pack is not in the shop.
                foreach (var item in UtilityLedger.Catalog.Items) _kit.Add(item);
            }
            else if (OnSupplies)
            {
                // Goods first, containers after, and that order is the merchandising
                // decision on this tab. Somebody who opened the hearts shelf is almost
                // always here to top up now; the permanent upgrade is what they find while
                // they are looking, which is the right way round for a purchase ten times
                // the price of anything else in the shop. It also leaves the shelf that
                // shipped exactly where it was.
                // The one shelf that carries real-money products *and* gem-priced goods, so
                // it is the one shelf that fills both lists. A container the store has never
                // heard of is left out for the reason a gem pack is: hiding it inside its own
                // cell keeps the slot, and a hole that answers taps is how an unreleased
                // product comes to look like a broken screen.
                foreach (var product in catalog.Products)
                {
                    if (product.Shelf != StoreShelf.Supplies) continue;
                    if (StoreService.OfferFor(product).State == StoreOfferState.Missing) continue;
                    _products.Add(product);
                }

                _products.Sort((a, b) => a.Tier.CompareTo(b.Tier));
            }
            else
            {
                // Cheapest first. Deliberately not the file's order: the tier a card's
                // picture is drawn from is derived from the price, so an authored order that
                // disagreed would put a gold chest above a pouch on the same shelf.
                // A product the store has never heard of is left out of the list entirely
                // rather than added and then hidden by its own cell. Hiding it kept the slot:
                // the grid was still sized to it, so the shelf drew a hole, and the hole still
                // answered taps — which is how an unreleased product came to look like a
                // broken screen. Products not yet created in a console, and products not sold
                // in this storefront, both land here and neither is anything a player can act
                // on. The cell keeps its own Missing guard as a backstop for the race between
                // this list being built and the store answering.
                foreach (var product in catalog.Products)
                {
                    if (product.Shelf != _shelf) continue;
                    if (StoreService.OfferFor(product).State == StoreOfferState.Missing) continue;
                    _products.Add(product);
                }

                _products.Sort((a, b) => a.Tier.CompareTo(b.Tier));
            }

            PaintTabs();
            PaintNews();
            PaintNotice();

            _grid.Show(ShelfRows());
        }

        /// <summary>
        /// How many cells this shelf shows. Every shelf but one is a single list; supplies is
        /// the goods followed by the heart containers, so its rows are the sum.
        /// </summary>
        int ShelfRows() => _ads.Count
                         + (OnUtilities ? _goods.Count + _kit.Count
                          : OnSupplies ? _goods.Count + _products.Count
                          : _products.Count);

        /// <summary>Redraws what is on screen: same cells, same place, no entrance.</summary>
        void Repaint()
        {
            if (_grid == null) return;

            // News first: it decides whether the line under the tabs is reserving a band, and
            // `PaintNotice` measures the shelf's top edge from that answer. See `PaintNews`.
            PaintNews();

            bool reflowed = PaintNotice();

            // Same list either way, and no entrance either way — Show(animate: false) is the
            // re-measuring form of Refresh, not a rebuild. See PaintNotice for when this is
            // reachable at all.
            if (reflowed) _grid.Show(ShelfRows(), animate: false);
            else _grid.Refresh();

            PaintTabs();

            // The three balance pills are deliberately not written here. They are watched by
            // `WalletWatch`, which repaints through the registry rather than onto the labels —
            // which is what lets the receipt panel own a pill while it walks it forward, since
            // a wallet change landing mid-flight would otherwise jump the number to the truth
            // and have the next token drag it back down. See ResourceSlots.Claim.
        }

        /// <summary>
        /// The line under the tab row, which is the only place on this screen that can say
        /// something about the shop as a whole.
        ///
        /// Each state is a different sentence, for the reason every state on a card is: a
        /// shop that cannot reach the store and a shop that is simply still loading look
        /// identical from a blank card, and only one of them is worth waiting for.
        /// </summary>
        /// <summary>
        /// What this screen has to say about the store as a whole, or nothing.
        ///
        /// <para>
        /// **Silent unless it has news, which is what "remove the captions" means without also
        /// removing the reporting.** It used to name the shelf on every shelf — a caption
        /// repeating the word already written on the tab above it — and the tabs now carry
        /// their own names, so the routine cases have nothing to say. What is left are the
        /// states a player genuinely cannot work out from the cards: the ways the store itself
        /// can be unreachable. A shop that cannot say "we cannot reach the store" is a shop
        /// whose buttons look broken.
        /// </para>
        /// <para>
        /// It answers the sentence and not where it is drawn, because there are two places it
        /// can go and only one rule for choosing between them. See <see cref="PaintNews"/>.
        /// </para>
        /// </summary>
        (string text, Color colour) StoreNews()
        {
            // The supplies shelf used to answer with the refill its containers are measured
            // against. It says nothing now: it was the last of the captions and the cards do
            // carry their own numbers. Its money half is the heart containers, and when the
            // store has not answered they are simply not on the shelf — so there is never a
            // dead card here for a sentence to have to explain.
            if (OnSupplies) return (string.Empty, Pal.Cream);

            // The kit shelf never asks the store anything, so it must never be labelled with
            // the store's state: "we cannot reach the shop" over a page of cards that work is
            // the sentence that teaches somebody the screen is broken. It says nothing at all,
            // which is the same rule with nothing left to say.
            if (OnUtilities) return (string.Empty, Pal.Cream);

            switch (StoreService.Status)
            {
                case StoreStatus.Unavailable:
                    return (Loc.Get("ui.shop.unavailable"), Pal.A(Pal.Cream, .60f));

                case StoreStatus.Connecting:
                    return (Loc.Get("ui.shop.connecting"), Pal.A(Pal.Aqua, .90f));

                case StoreStatus.Offline:
                    // **Two sentences, because the player can do something about one of them.**
                    // The store refuses to connect for a phone in a tunnel and for a store
                    // having a bad afternoon, and `StoreStatus` cannot tell them apart — the
                    // SDK reports one failure either way. "The store cannot be reached right
                    // now" is true of both and actionable for neither; a player with no signal
                    // is owed the half they can fix. The radio is read only *after* the connect
                    // has already failed, which is the one thing `Net` may be used for.
                    return (Loc.Get(Net.Offline ? "ui.shop.no_connection" : "ui.shop.offline"),
                            Pal.A(Pal.Sun, .90f));

                default:
                    // A purchase the server has not finished with is the one thing here worth
                    // interrupting for; a shelf that is simply working says nothing.
                    if (!StoreService.HasUnredeemed) return (string.Empty, Pal.Cream);
                    return (Loc.Get("ui.shop.awaiting"), Pal.A(Pal.Sun, .95f));
            }
        }

        /// <summary>
        /// Draws <see cref="StoreNews"/> in whichever of its two places is right, and blanks
        /// the other.
        ///
        /// <para>
        /// <b>One sentence, two places, one rule — never both.</b> A shelf with cards on it
        /// carries the news in the thin line under the tabs, where it is a footnote to a page
        /// that is working. A shelf with <em>nothing</em> on it is a blank page, and a blank
        /// page is itself the question the player is asking, so the answer belongs in the
        /// middle of it at a size somebody will read. Two labels holding one sentence at once
        /// is how a screen comes to look like it is repeating itself.
        /// </para>
        /// <para>
        /// <b>Nothing on it means nothing, including the free card.</b> The coins and hearts
        /// shelves each stand a rewarded video in their first spot and it is drawn live
        /// whatever the network is doing (invariant 18g) — so those shelves are not empty when
        /// the store is unreachable, they are a shelf with one card on it, and a banner across
        /// the middle of them would be drawn straight over it. <see cref="ShelfRows"/> counts
        /// that card, which is exactly why this asks it rather than counting products.
        /// </para>
        /// </summary>
        void PaintNews()
        {
            var (text, colour) = StoreNews();
            bool centre = text.Length > 0 && ShelfRows() == 0;

            // **What decides the band's height, and why this has to run before
            // <see cref="PaintNotice"/>.** The line under the tabs is reserved space only while
            // it is drawn; the sentence in the middle of an empty shelf is not under the tabs at
            // all, so it collapses the band exactly as silence does. Both callers order the two
            // this way round deliberately — the other order paints the shelf's top edge from
            // last frame's answer, which is a band that lags one repaint behind its own line.
            _saying = !centre && text.Length > 0;

            if (_summary)
            {
                _summary.text = centre ? string.Empty : text;
                _summary.color = colour;
            }

            if (_empty) _empty.text = centre ? text : string.Empty;

            // The plate goes with the sentence rather than standing empty, which is the whole
            // of why it is hidden rather than merely blanked: a dark slab across a working
            // shelf reads as a card that failed to load.
            if (_emptyPlate && _emptyPlate.gameObject.activeSelf != centre)
                _emptyPlate.gameObject.SetActive(centre);
        }

        void PaintTabs()
        {
            foreach (var pair in _tabViews) pair.Value.Restyle(pair.Key == _shelf);
        }

        // -------------------------------------------------------------- outcomes
        /// <summary>
        /// A purchase landed. Only the repaint belongs here; the panel is <c>Boot</c>'s, so
        /// that a grant arriving on the hub or the map is celebrated too.
        /// </summary>
        void OnGranted(StoreGrant grant) => Repaint();

        /// <summary>
        /// A purchase attempt ended without a transaction.
        ///
        /// <para>
        /// A toast rather than a panel, and cancelling says nothing at all. A player who
        /// closed the payment sheet does not need to be told they closed the payment sheet,
        /// and a modal apologising for it is how a shop teaches somebody to dismiss the
        /// dialog that actually mattered.
        /// </para>
        /// </summary>
        void OnFailed(string productId, StoreFailure failure, string message)
        {
            Repaint();

            if (failure == StoreFailure.Cancelled) return;

            // Shared with the panel a lost run raises, which offers the same products without
            // navigating anywhere — see StoreWording.
            var (key, tint) = StoreWording.Failure(failure);
            Scenery.Toast(Content, Loc.Get(key), tint, 2.6f);
        }

        /// <summary>
        /// The one word on a tab.
        ///
        /// <para>
        /// Separate from <see cref="ShelfNameKey"/>, which answers with a whole sentence —
        /// "Gems buy hearts, boosts and time". That is a caption and this is a name, and the
        /// tabs briefly wore the sentence, shrunk to fit, which is how five buttons came to
        /// carry five lines of small print.
        /// </para>
        /// </summary>
        static string TabNameKey(StoreShelf shelf)
        {
            switch (shelf)
            {
                case StoreShelf.Gems: return "ui.shop.tab_gems";
                case StoreShelf.Coins: return "ui.shop.tab_coins";
                case StoreShelf.Bundles: return "ui.shop.tab_bundles";
                case StoreShelf.Utilities: return "ui.shop.tab_utilities";
                default: return "ui.shop.tab_supplies";
            }
        }

        static string ShelfNameKey(StoreShelf shelf)
        {
            switch (shelf)
            {
                case StoreShelf.Gems: return "ui.shop.shelf_gems";
                case StoreShelf.Coins: return "ui.shop.shelf_coins";
                case StoreShelf.Bundles: return "ui.shop.shelf_bundles";
                case StoreShelf.Utilities: return "ui.shop.shelf_utilities";
                default: return "ui.shop.shelf_supplies";
            }
        }

        // --------------------------------------------------------------- tapping
        /// <summary>
        /// Tapping a product opens the store's own payment sheet, and nothing in between.
        ///
        /// <para>
        /// <b>There is deliberately no confirmation panel.</b> The sheet <em>is</em> the
        /// confirmation — it names the product, states the price in the player's own
        /// currency, and on both platforms asks for a password, a fingerprint or a face
        /// before a penny moves. A panel of ours in front of it would be a tap for a
        /// question already being asked a second later, and this project has recorded twice
        /// what that costs: a control labelled with a price has to charge that price, the
        /// same way the button labelled "next glade" has to go to the next glade.
        /// </para>
        /// <para>
        /// Every refusal is a toast rather than a panel, because none of them is a decision
        /// — they are all statements about the store, and three of the four resolve by
        /// waiting.
        /// </para>
        /// </summary>
        void Tap(StoreProduct product)
        {
            // The sheet, the six refusals and the reasoning behind having no confirmation all
            // live in StoreTap now — the panel a lost run raises has to do exactly this, and
            // six sentences maintained twice on the screen where money changes hands is
            // invariant 9a's argument at its smallest scale. Repainting afterwards is this
            // screen's own business: it draws cards whose state the tap may have moved.
            StoreTap.Buy(this, product);
            Repaint();
        }

        /// <summary>
        /// Tapping the free spot opens the same panel the hub's <c>+</c> opens, and nothing
        /// else happens here.
        ///
        /// <para>
        /// <b>The same panel, deliberately</b> — the argument the kit shelf already makes about
        /// <c>UtilityBuyOverlay</c>. It knows how to say all five ways a rewarded ad can fail to
        /// happen, how to count the day's allowance down, how to hand the reward over and how to
        /// fly it into the pills above. A second route that showed a video itself would be a
        /// second copy of every one of those, on the screen where the first copy is already the
        /// game's only honest account of them.
        /// </para>
        /// <para>
        /// It is a modal over the shop rather than a navigation, which is this screen's own
        /// rule for anything that answers "I want more of this": the shelf is still underneath
        /// when the panel closes, so a player who watched a video for coins is standing in front
        /// of the coins.
        /// </para>
        /// </summary>
        void TapAd(int slot)
        {
            if (slot < 0 || slot >= _ads.Count) return;

            Flow.Modal<AdOfferOverlay>(panel =>
            {
                panel.PlacementId = _ads[slot].PlacementId;

                // The cards carry no balance of their own, but the supplies shelf greys a heart
                // pack at a full pool and the gem prices are measured against a wallet the
                // video may have just moved. A repaint rather than a reload: the same cards,
                // redrawn, at the moment the player is watching the reward land.
                panel.Rewarded = Repaint;
            });
        }

        /// <summary>
        /// Tapping a supply.
        ///
        /// <para>
        /// Short of gems opens the gem shelf rather than greying the cell out, which is
        /// this project's rule everywhere a price is short — see <c>CompanionUnlockOverlay</c>.
        /// That is the moment a player has decided they want something, which is the best
        /// moment in the game to show them how to get it and the worst to teach them a
        /// control is dead.
        /// </para>
        /// </summary>
        void TapGood(StoreGood good)
        {
            if (good == null) return;

            var state = StoreService.OfferForGood(good);

            if (state == GoodOfferState.ShortOfGems)
            {
                Scenery.Toast(Content, Loc.Get("ui.shop.need_gems"), Pal.Bloom);
                Show(StoreShelf.Gems);
                return;
            }

            if (state != GoodOfferState.Ready)
            {
                Scenery.Toast(Content, Loc.Get(StoreWording.GoodRefusal(state)), Pal.Sun, 2.6f);
                return;
            }

            Flow.Modal<ShopSupplyOverlay>(v => v.Good = good);
        }

        /// <summary>
        /// Tapping a utility opens the same panel the empty slot on the action bar opens.
        ///
        /// <para>
        /// <b>The same panel, deliberately.</b> Two would be two prices, two ceilings and two
        /// chances to disagree about what a player is carrying — <c>RunContinueFlow</c>'s
        /// argument, on the screen where the price is actually charged. It already knows how to
        /// stack the gem shelf on a short balance and how to say the three refusals, so a shelf
        /// that navigated to the gem tab instead would be the one route to buying gems behaving
        /// differently in the shop from in a run.
        /// </para>
        /// <para>
        /// A chest-only utility opens it too and is told so there rather than refused here: the
        /// panel's own sentence is the answer, and a toast over a card that then does nothing is
        /// how a shelf teaches somebody that tapping is pointless.
        /// </para>
        /// </summary>
        void TapUtility(UtilityItem item)
        {
            if (item == null) return;

            Flow.Modal<UtilityBuyOverlay>(v => { v.Item = item; v.Bought = Repaint; });
        }

        // ------------------------------------------------------------------ tab
        sealed class ShelfTab
        {
            readonly Image _plate, _mark, _lit;
            readonly Text _name;
            readonly StoreShelf _shelf;

            public ShelfTab(RectTransform row, StoreShelf shelf, float step, float x, Action onTap)
            {
                _shelf = shelf;

                var cell = UIKit.Button("T_" + shelf, row, Art.Pixel, new Vector2(step - 6f, TabRow),
                                        new Vector2(.5f, .5f), new Vector2(x, 0f), onTap);
                cell.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);

                // The bar at the foot of the screen, one level down - the same rounded plate,
                // the same seat rim, the same gold frame when it is the live one, and the same
                // glyph hanging over the top edge with the name inside. A row of tabs is the
                // same question a nav bar asks (*which page*), so it should not be a second
                // answer to it: this replaced a pair of the kit's round caps, which read as
                // five coins in a row above a row of five buttons that were not.
                _plate = UIKit.Img("P", cell.transform, Art.Round(30), Skins.Plate,
                                   new Vector2(step - 12f, TabRow - 18f), new Vector2(.5f, .5f),
                                   new Vector2(0f, -4f));

                var seat = UIKit.Img("Seat", _plate.transform, Art.RoundOutline(30, 4f),
                                     new Color(.02f, .06f, .13f, .85f));
                UIKit.StretchTo((RectTransform)seat.transform, 0, 0, 0, 0);

                _lit = UIKit.Img("Lit", _plate.transform, Art.RoundOutline(30, 6f), Skins.PlateEdge);
                UIKit.StretchTo((RectTransform)_lit.transform, -2, -2, -2, -2);
                _lit.enabled = false;

                _mark = UIKit.Img("A", _plate.transform, Mark(shelf), Color.white,
                                  new Vector2(100f, 100f), new Vector2(.5f, .5f), new Vector2(0f, 30f));
                _mark.preserveAspect = true;
                _mark.raycastTarget = false;

                // Inside the plate, under the glyph. The names moved *here* from the caption
                // line that used to run below this row, which is what stops five picture-only
                // tabs asking the player to guess what a pouch means.
                _name = UIKit.Shrinkable(
                    UIKit.Titled("L", _plate.transform, Loc.Get(TabNameKey(shelf)).ToUpperInvariant(),
                                 23, Pal.Cream, TextAnchor.MiddleCenter,
                                 new Vector2(step - 26f, 30f), new Vector2(.5f, 0f),
                                 new Vector2(0f, 22f), 3f, 2f), 15);
                _name.raycastTarget = false;
            }

            /// <summary>
            /// The glyph on a tab, and every one of them is a sprite the game already draws
            /// somewhere else. A tab that invented its own icon would be teaching a second
            /// name for a thing the player already recognises from the hub.
            /// </summary>
            static Sprite Mark(StoreShelf shelf)
            {
                switch (shelf)
                {
                    case StoreShelf.Gems: return Art.S("Ui/ic_gem");
                    case StoreShelf.Coins: return Art.S("Ui/Shop/pouch");
                    case StoreShelf.Bundles: return Art.S("Ui/ic_gift");
                    // **A satchel holding an XP mark, a bomb and a potion**, which is the one
                    // glyph on this row that says what the whole tab is rather than what one
                    // thing on it is. It used to be the firepot — a fair choice while the shelf
                    // was four combat consumables and the tab was called KIT, and the wrong one
                    // the moment an XP boost moved to the front of it: a bomb described the
                    // least representative card on the shelf.
                    case StoreShelf.Utilities: return Art.S("Ui/ic_utilities");
                    default: return Art.S("Ui/ic_heart");
                }
            }

            public void Restyle(bool live)
            {
                if (!_plate) return;

                // Selection is the plate and its frame, never the glyph - the nav bar's rule,
                // and it matters more here because two of these five marks are painted pictures
                // and three are flat glyphs, so anything leaning on a tint reads differently
                // depending on which tab you are standing on.
                _plate.color = live ? new Color(.098f, .467f, .757f, 1f) : Skins.Plate;
                _lit.enabled = live;
                _mark.color = Color.white;
                _name.color = live ? Pal.Sun : Pal.Cream;

                // **No pop and no scale.** The live tab used to spring from .86, which left the
                // one you were standing on visibly smaller than its neighbours for the length of
                // the tween — and on the shelf the screen opens on, for as long as nothing had
                // restyled it since. Selection is the plate's colour and its gold frame; a row
                // of tabs that changes size as you cross it is a row that never sits still.
            }
        }

        // ----------------------------------------------------------------- cell
        /// <summary>
        /// One product card, built once and rebound as it is recycled.
        ///
        /// <para>
        /// Every part that can change with the row is a field, because the alternative —
        /// destroying and rebuilding — is what made the Grovement's shop flicker and what
        /// would make a growing catalog stutter on every tap. See <c>GridView</c>.
        /// </para>
        /// <para>
        /// The layout is one shape for both kinds of row, which is what lets the supplies
        /// shelf share a cell with the money shelves: a plate, a picture, an amount, a
        /// second line, and a button that is either a store price or a gem price. Two cell
        /// classes would be two places to get the four states of a disabled button wrong.
        /// </para>
        /// </summary>
        sealed class ShopCell : IGridCell
        {
            readonly ShopScreen _screen;
            readonly ProductCard _card;

            StoreProduct _product;
            StoreGood _good;
            UtilityItem _kit;

            public RectTransform Root => _card.Root;

            /// <summary>Set when this cell is one of the shelf's free spots, cleared on every other bind.</summary>
            bool _isAd;

            /// <summary>Which free spot, when this cell is one. -1 otherwise.</summary>
            int _adSlot = -1;

            public ShopCell(ShopScreen screen, RectTransform parent)
            {
                _screen = screen;
                _card = new ProductCard(parent,
                                        new ProductCard.Look(CellW, CellH, decorated: true),
                                        () => { if (_isAd) _screen.TapAd(_adSlot);
                                                else if (_kit != null) _screen.TapUtility(_kit);
                                                else if (_good != null) _screen.TapGood(_good);
                                                else _screen.Tap(_product); });
            }

            /// <summary>
            /// Draws whichever kind of thing this shelf sells.
            ///
            /// <para>
            /// The card knows how a sellable thing looks; this knows which one row
            /// <paramref name="index"/> is and what tapping it does. That split is why the same
            /// face can be drawn by a panel raised over a run that must not be navigated away
            /// from — see <see cref="ProductCard"/>.
            /// </para>
            /// </summary>
            public void Bind(int index)
            {
                // The free spot, and everything else on the shelf shifted down by it. Taken
                // first so no other branch has to know it exists, and the flag is cleared on
                // every other bind rather than only set on this one — a cell is rebound as the
                // grid scrolls (invariant 16d), so a latch left standing is a coin pack that
                // opens a video panel.
                _adSlot = index < _screen._ads.Count ? index : -1;
                _isAd = _adSlot >= 0;

                if (_isAd)
                {
                    _product = null;
                    _good = null;
                    _kit = null;

                    _card.Draw(_screen._ads[_adSlot], _screen._shelf);
                    return;
                }

                index -= _screen._ads.Count;

                if (_screen.OnUtilities)
                {
                    _product = null;

                    // **The goods first, then the kit**, which is the order `Reload` fills them
                    // and the order the shelf is meant to read in: the thing that makes every
                    // other thing on the tab work faster stands at the front of it.
                    if (index >= 0 && index < _screen._goods.Count)
                    {
                        _kit = null;
                        _good = _screen._goods[index];

                        _card.Draw(_good, StoreService.OfferForGood(_good));
                        return;
                    }

                    index -= _screen._goods.Count;

                    _good = null;
                    _kit = index >= 0 && index < _screen._kit.Count ? _screen._kit[index] : null;

                    if (_kit == null) { _card.Hide(); return; }

                    _card.Draw(_kit, UtilityLedger.WhyNotBuy(_kit, 1), UtilityLedger.Held(_kit));
                    return;
                }

                _kit = null;

                if (_screen.OnSupplies && index < _screen._goods.Count)
                {
                    _product = null;
                    _good = index >= 0 ? _screen._goods[index] : null;

                    if (_good == null) _card.Hide();
                    else _card.Draw(_good, StoreService.OfferForGood(_good));
                    return;
                }

                // Past the goods on the supplies shelf, and from nought on every other one.
                // The containers are real-money products, so they fall through to exactly the
                // path the gem and coin cards already take.
                if (_screen.OnSupplies) index -= _screen._goods.Count;

                _good = null;
                _product = index >= 0 && index < _screen._products.Count ? _screen._products[index] : null;

                if (_product == null) { _card.Hide(); return; }

                var offer = StoreService.OfferFor(_product);

                // A product the store has never heard of leaves an empty slot rather than a
                // dead card. It means a product not yet created in a console, or one not for
                // sale in this storefront, and there is nothing a player can do about either.
                if (offer.State == StoreOfferState.Missing) { _card.Hide(); return; }

                _card.Draw(_product, offer);
            }
        }
    }
}
