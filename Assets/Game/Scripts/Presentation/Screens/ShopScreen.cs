using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Cloud;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Store;
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
    /// <b>Four shelves, and the fourth is a different kind of thing.</b> Gems, coins and
    /// bundles are bought with money and adjudicated by the server; supplies — hearts and
    /// boosts — are bought with gems and applied on the phone. They share a screen because
    /// they are one decision from the player's side, and they share nothing else: see
    /// <c>StoreProduct</c> for why a real-money product may only ever grant currency.
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
        const float TabRow = 132f;

        const int Columns = 2;
        const float CellW = 508f;
        const float CellH = 560f;
        // An int, because Art.Round takes one — the generated corner is rasterised at a
        // pixel radius and there is no such thing as half a pixel of corner.
        const int CellRadius = 30;

        /// <summary>
        /// The standing "not signed in" bar, and the gap under it. Reserved out of the grid's
        /// viewport only while it is drawn, so a linked player and the gem-priced shelf stay
        /// pixel-identical to what shipped rather than carrying a hole where a warning would
        /// have gone.
        /// </summary>
        const float NoticeH = 74f, NoticeGap = 14f;

        RectTransform _viewport, _tabs;
        GridView _grid;
        Text _summary;
        Btn _restore, _notice;

        readonly List<StoreProduct> _products = new List<StoreProduct>();
        readonly List<StoreGood> _goods = new List<StoreGood>();

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

        static readonly StoreShelf[] Shelves =
        {
            StoreShelf.Gems, StoreShelf.Coins, StoreShelf.Bundles, StoreShelf.Supplies,
        };

        protected override void Build()
        {
            Scenery.Layered(Content, "home", .30f);
            Fireflies.Spawn(Content, 12, new Color(1f, .93f, .70f), 6f, 20f);

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

            // The supplies shelf is priced in gems and gated on hearts, so both move it.
            PlayerProgression.Changed += Repaint;
            Wallet.HeartsChanged += OnHeartsChanged;

            // And a container bought — or refunded by a sync — moves what the shelf says the
            // player's limit is, and turns the card that sold it into YOURS. A repaint rather
            // than a reload: the same cards, redrawn, at the moment the player is watching
            // one of them land. See the house rule about Show and Refresh.
            HeartContainerLedger.Changed += Repaint;

            // A content push can retune the whole shop, including which products exist.
            ProgressionRules.Changed += Reload;

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
            var fade = UIKit.Img("TopFade", Content, Art.FadeUp(64), new Color(.02f, .06f, .09f, .84f));
            var frt = (RectTransform)fade.transform;
            frt.anchorMin = new Vector2(0f, 1f); frt.anchorMax = new Vector2(1f, 1f);
            frt.pivot = new Vector2(.5f, 1f);
            frt.sizeDelta = new Vector2(0f, HeaderHeight + TabRow);
            frt.anchoredPosition = Vector2.zero;
            frt.localRotation = Quaternion.Euler(0, 0, 180f);

            UIKit.IconButton("Back", Safe, Skins.Nav, "ic_left", new Vector2(118f, 118f),
                             new Vector2(0f, 1f), new Vector2(96f, -120f), () => Flow.Go<HomeScreen>());

            var banner = UIKit.Img("Banner", Safe, Art.S("Ui/banner"), Color.white,
                                   new Vector2(520f, 140f), new Vector2(.5f, 1f), new Vector2(0f, -116f));
            UIKit.Shrinkable(
                UIKit.Titled("Title", banner.transform, Loc.Get("ui.nav.shop").ToUpperInvariant(), 40,
                             new Color(.36f, .24f, .16f), TextAnchor.MiddleCenter,
                             new Vector2(360f, 58f), new Vector2(.5f, .5f),
                             new Vector2(0f, 140f * UIKit.PillFaceLift), 0f, 2f), 24);

            BuildBalances();
            BuildTabs();
            BuildNotice();
        }

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
                                   new Vector2(0f, -(HeaderHeight + TabRow + 44f) - NoticeH * .5f),
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
            bool show = AccountPrompts.ShouldWarn && (!OnSupplies || HasMoneyOnShelf());
            if (_notice.gameObject.activeSelf != show) _notice.gameObject.SetActive(show);

            float top = -HeaderHeight - TabRow - 44f - (show ? NoticeH + NoticeGap : 0f);
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
            var row = UIKit.Row("Balances", Safe, new Vector2(1000f, 76f), new Vector2(.5f, 1f),
                                new Vector2(0f, -214f), 14f);

            BalancePill(row, Pal.Gold, null, Compact.Number(Profile.Coins),
                        ResourceSlots.Kind.Credits, Compact.Number);
            BalancePill(row, Pal.Bloom, "ic_gem", Compact.Number(Profile.Gems),
                        ResourceSlots.Kind.Gems, Compact.Number);
            BalancePill(row, Pal.Rose, "ic_heart", Profile.HeartsLabel(),
                        ResourceSlots.Kind.Hearts, n => Profile.HeartsLabel((int)n));
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
            var pill = UIKit.Img("Pill", row, Art.Round(20), new Color(.04f, .09f, .12f, .82f),
                                 new Vector2(206f, 68f), new Vector2(.5f, .5f), Vector2.zero);

            var edge = UIKit.Img("Edge", pill.transform, Art.RoundOutline(20, 2.5f), Pal.A(tint, .45f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            var glow = UIKit.Img("Glow", pill.transform, Art.Glow(96, 2f), Pal.A(tint, .22f),
                                 new Vector2(96f, 96f), new Vector2(0f, .5f), new Vector2(38f, 0f));

            var glyph = UIKit.Img("Icon", pill.transform, icon == null ? null : Art.S("Ui/" + icon),
                                  Color.white, new Vector2(50f, 50f), new Vector2(0f, .5f),
                                  new Vector2(38f, 0f));
            glyph.preserveAspect = true;

            // The coin is the hub's own spinning one. Consistency here is worth the two
            // extra draws: the pile on a card is made of this coin, so the pill and the
            // product read as the same thing.
            if (icon == null) Flipbook.Attach(glyph, "Ui/Coin", 11f);

            var text = UIKit.Shrinkable(
                UIKit.Titled("V", pill.transform, value, 30, Pal.Cream, TextAnchor.MiddleCenter,
                             new Vector2(112f, 44f), new Vector2(.5f, .5f), new Vector2(16f, 0f), 3f, 3f), 18);

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

            _summary = UIKit.Shrinkable(
                UIKit.Titled("Summary", Safe, string.Empty, 26,
                             new Color(1f, .96f, .88f, .74f), TextAnchor.MiddleCenter,
                             new Vector2(880f, 34f), new Vector2(.5f, 1f),
                             new Vector2(0f, -HeaderHeight - TabRow + 4f), 3f, 0f), 17);

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
            _viewport.offsetMax = new Vector2(0f, -HeaderHeight - TabRow - 44f);

            _grid = GridView.Attach(_viewport, Columns, CellW, CellH,
                                    parent => new ShopCell(this, parent));

            BuildRestore();
        }

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

            if (OnSupplies)
            {
                // Goods first, containers after, and that order is the merchandising
                // decision on this tab. Somebody who opened the hearts shelf is almost
                // always here to top up now; the permanent upgrade is what they find while
                // they are looking, which is the right way round for a purchase ten times
                // the price of anything else in the shop. It also leaves the shelf that
                // shipped exactly where it was.
                foreach (var good in catalog.Goods) _goods.Add(good);

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
            PaintSummary();
            PaintNotice();

            _grid.Show(ShelfRows());
        }

        /// <summary>
        /// How many cells this shelf shows. Every shelf but one is a single list; supplies is
        /// the goods followed by the heart containers, so its rows are the sum.
        /// </summary>
        int ShelfRows() => OnSupplies ? _goods.Count + _products.Count : _products.Count;

        /// <summary>Redraws what is on screen: same cells, same place, no entrance.</summary>
        void Repaint()
        {
            if (_grid == null) return;

            bool reflowed = PaintNotice();

            // Same list either way, and no entrance either way — Show(animate: false) is the
            // re-measuring form of Refresh, not a rebuild. See PaintNotice for when this is
            // reachable at all.
            if (reflowed) _grid.Show(ShelfRows(), animate: false);
            else _grid.Refresh();

            PaintTabs();
            PaintSummary();

            // Through the registry rather than onto the labels, which makes it the one writer
            // of these three readouts. That is what lets the receipt panel own a pill while it
            // walks it forward: a wallet change landing mid-flight would otherwise jump the
            // number to the truth and have the next token drag it back down. Same rule as the
            // hub's PaintResources — see ResourceSlots.Claim.
            ResourceSlots.Repaint(ResourceSlots.Kind.Credits, Profile.Coins);
            ResourceSlots.Repaint(ResourceSlots.Kind.Gems, Profile.Gems);
            ResourceSlots.Repaint(ResourceSlots.Kind.Hearts, Profile.Hearts);
        }

        /// <summary>
        /// The line under the tab row, which is the only place on this screen that can say
        /// something about the shop as a whole.
        ///
        /// Each state is a different sentence, for the reason every state on a card is: a
        /// shop that cannot reach the store and a shop that is simply still loading look
        /// identical from a blank card, and only one of them is worth waiting for.
        /// </summary>
        void PaintSummary()
        {
            if (!_summary) return;

            // The supplies shelf answers with the number every card on it is measured
            // against — what this player's hearts refill to today. That is the context a
            // container needs and the one thing the cards themselves cannot say: "20" only
            // means something beside "yours is 5". It replaces a line that said nothing but
            // real money was involved, which stopped being true when containers arrived.
            if (OnSupplies)
            {
                _summary.text = Loc.Format("ui.shop.capacity_held", Wallet.MaxHearts);
                _summary.color = new Color(1f, .96f, .88f, .74f);
                return;
            }

            switch (StoreService.Status)
            {
                case StoreStatus.Unavailable:
                    _summary.text = Loc.Get("ui.shop.unavailable");
                    _summary.color = Pal.A(Pal.Cream, .60f);
                    break;

                case StoreStatus.Connecting:
                    _summary.text = Loc.Get("ui.shop.connecting");
                    _summary.color = Pal.A(Pal.Aqua, .90f);
                    break;

                case StoreStatus.Offline:
                    _summary.text = Loc.Get("ui.shop.offline");
                    _summary.color = Pal.A(Pal.Sun, .90f);
                    break;

                default:
                    _summary.text = StoreService.HasUnredeemed
                        ? Loc.Get("ui.shop.awaiting")
                        : Loc.Get(ShelfNameKey(_shelf));
                    _summary.color = StoreService.HasUnredeemed
                        ? Pal.A(Pal.Sun, .95f)
                        : new Color(1f, .96f, .88f, .74f);
                    break;
            }
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

        static string ShelfNameKey(StoreShelf shelf)
        {
            switch (shelf)
            {
                case StoreShelf.Gems: return "ui.shop.shelf_gems";
                case StoreShelf.Coins: return "ui.shop.shelf_coins";
                case StoreShelf.Bundles: return "ui.shop.shelf_bundles";
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

        // ------------------------------------------------------------------ tab
        sealed class ShelfTab
        {
            readonly Image _plate, _edge, _mark;
            readonly StoreShelf _shelf;

            public ShelfTab(RectTransform row, StoreShelf shelf, float step, float x, Action onTap)
            {
                _shelf = shelf;

                var cell = UIKit.Button("T_" + shelf, row, Art.Pixel, new Vector2(step - 8f, TabRow),
                                        new Vector2(.5f, .5f), new Vector2(x, 0f), onTap);
                cell.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);

                _plate = UIKit.Img("P", cell.transform, Art.Round(22), new Color(.06f, .12f, .16f, .72f),
                                   new Vector2(step - 24f, TabRow - 22f), new Vector2(.5f, .5f),
                                   Vector2.zero);

                _edge = UIKit.Img("E", _plate.transform, Art.RoundOutline(22, 2f),
                                  new Color(1f, .97f, .90f, .12f));
                UIKit.StretchTo((RectTransform)_edge.transform, 0, 0, 0, 0);

                _mark = UIKit.Img("A", _plate.transform, Mark(shelf), Color.white,
                                  new Vector2(TabRow - 58f, TabRow - 58f), new Vector2(.5f, .5f),
                                  Vector2.zero);
                _mark.preserveAspect = true;
                _mark.raycastTarget = false;
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
                    default: return Art.S("Ui/ic_heart");
                }
            }

            static Color Tint(StoreShelf shelf)
            {
                switch (shelf)
                {
                    case StoreShelf.Gems: return Pal.Bloom;
                    case StoreShelf.Coins: return Pal.Gold;
                    case StoreShelf.Bundles: return Pal.Aqua;
                    default: return Pal.Rose;
                }
            }

            public void Restyle(bool live)
            {
                if (!_plate) return;

                var tint = Tint(_shelf);

                _plate.color = live ? new Color(.10f, .26f, .27f, .96f)
                                    : new Color(.06f, .12f, .16f, .72f);

                _edge.sprite = Art.RoundOutline(22, live ? 3f : 2f);
                _edge.color = live ? Pal.A(tint, .78f) : new Color(1f, .97f, .90f, .12f);

                _mark.color = live ? Color.white : new Color(1f, 1f, 1f, .55f);

                if (live) Tween.Pop(_plate.transform, .86f, .3f);
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

            public RectTransform Root => _card.Root;

            public ShopCell(ShopScreen screen, RectTransform parent)
            {
                _screen = screen;
                _card = new ProductCard(parent,
                                        new ProductCard.Look(CellW, CellH, CellRadius, decorated: true),
                                        () => { if (_good != null) _screen.TapGood(_good);
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

                bool best = _product.Badge == StoreBadge.BestValue
                            || _product.Badge == StoreBadge.Starter;

                _card.Draw(_product, offer, best);
            }
        }
    }
}
