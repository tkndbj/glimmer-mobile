using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Store;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The limited-time deals panel (invariant 60): every deal it was handed, one row each, with
    /// what it pays, a clock to the second and a key that buys it for gems.
    ///
    /// <para>
    /// <b>One panel for both doors.</b> The shop's deal band opens it with every deal on offer;
    /// <c>DealMoment</c> raises it on its own with the deals this player has never been shown
    /// (invariant 60c). It is told which (<see cref="Trigger"/>) only so analytics can say what a
    /// popup earned; it behaves the same either way.
    /// </para>
    /// <para>
    /// <b>As many deals as it is handed.</b> The server sells one at a time today
    /// (<c>DEAL_MAX_LIVE</c>), and nothing here depends on that: the rows are a
    /// <see cref="GridView"/> (44ma), the panel grows to three and then scrolls, and every row is
    /// bought, ends and repaints on its own. A row that has been bought or has ended stays where it
    /// is, saying so, so the list never jumps under a thumb.
    /// </para>
    /// <para>
    /// <b>Short of gems is a detour, not a dead end.</b> The gem shelf is stacked over this panel
    /// (<see cref="GemShopOverlay"/>, which steps out once the server has granted the gems), the
    /// receipt is raised over everything by <c>Boot</c>, and closing it leaves this panel standing
    /// with the row the player wanted pulsing, now affordable. Nothing is bought on the player's
    /// behalf: a gem purchase has no store sheet, so the tap on the row is the confirmation.
    /// </para>
    /// <para>
    /// <b>The welcome bundle stands at the top</b> (the owner, 2026-10-11) - the one-time starter
    /// bundle, with its value seal (<see cref="DealValue.BundlePercent"/>) - for as long as this
    /// account has not bought it, and never once it has: it is decided when the panel opens, from
    /// the store's own answer (<see cref="WelcomeOffer"/>). Bought while the panel is open, its row
    /// says so and stays put, the rule every deal row follows, and is gone the next time. It is a
    /// real-money product, so its key opens the store's sheet (<see cref="StoreTap"/>) and the
    /// receipt is raised by <c>Boot</c>, as everywhere else in the game.
    /// </para>
    /// <para>
    /// <b>Every buy is re-checked at the tap</b> (<see cref="DealLedger.TryBuy"/>): a deal can end,
    /// another phone can buy it, or a sync can move the balance while the panel is open. The coins
    /// fly home when the panel closes (<see cref="DealPayout"/>), from whatever screen is under it.
    /// </para>
    /// </summary>
    public sealed class DealOverlay : ModalView
    {
        /// <summary>The deals to list, closing soonest first. Set by the caller's configure callback.</summary>
        public IReadOnlyList<ShopDeal> Deals { get; set; }

        /// <summary>What raised it: <see cref="DealTrigger.None"/> from the shop.</summary>
        public DealTrigger Trigger { get; set; }

        // ------------------------------------------------------------------ geometry
        const float PanelW = 1030f;
        const int MostRows = 3;
        /// <summary>A row: the open deal card (<see cref="DealCard.BuildOpen"/>), with air between rows.</summary>
        const float CellW = 950f, CellH = DealRow.CardH + 30f;
        const float ListPadTop = 6f, ListPadBottom = 6f;

        /// <summary>
        /// The Daily Challenges deals sheet's layout on <see cref="VictoryFrame"/>
        /// (<c>ChallengeTierOverlay</c>): the rows start under the crest and banner, then the air
        /// under the last row, then the band the closing key stands in at the foot.
        /// </summary>
        const float RowsTop = 196f, Tail = 30f, FootH = 170f;
        const float KeyW = 400f, KeyH = 110f, KeyY = 85f;

        readonly List<ShopDeal> _deals = new List<ShopDeal>();
        readonly List<DealRow> _rows = new List<DealRow>();

        /// <summary>The welcome bundle, when it heads the list (<see cref="WelcomeOffer"/>); null otherwise.</summary>
        StoreProduct _welcome;

        /// <summary>How many rows sit above the first deal: one while the welcome bundle heads the list.</summary>
        int Lead => _welcome != null ? 1 : 0;
        GridView _grid;

        /// <summary>The deals bought from this panel, paid out when it closes.</summary>
        readonly List<ShopDeal> _paid = new List<ShopDeal>();

        /// <summary>The row the player went to buy gems for; it pulses once they land.</summary>
        string _wanted;

        float _nextCheck;
        bool _leaving;

        protected override void Build()
        {
            _deals.Clear();
            if (Deals != null) foreach (var deal in Deals) if (deal != null) _deals.Add(deal);
            if (_deals.Count == 0) { Flow.Dismiss(this); return; }

            // Decided once, here: a row that appeared or left under a thumb is the jump this panel
            // exists not to make. See the type's remarks.
            _welcome = WelcomeOffer();

            int visible = Mathf.Min(Lead + _deals.Count, MostRows);
            float listH = ListPadTop + visible * CellH + ListPadBottom;

            float panelH = RowsTop + listH + Tail + FootH;

            // The Daily Challenges deals sheet's frame (the owner, 2026-10-09): the green window,
            // the fan behind it and the crown over a banner carrying the title, built by
            // `VictoryFrame` so the two sheets of deals cannot drift. Not dismissed by the scrim -
            // the one way this differs from that sheet - because a stray tap beside a purchase must
            // not throw the offer away; the way out is the key at the foot (and the back key).
            Scrim = UIKit.Scrim(Content, .72f, null);
            var frame = VictoryFrame.Build(Content, panelH, Loc.Get("ui.deal.title"), PanelW);
            Backing = frame.Backing;
            Panel = frame.Panel;

            float listY = RowsTop;

            var viewport = UIKit.Node("Viewport", Panel);
            viewport.anchorMin = viewport.anchorMax = new Vector2(.5f, 1f);
            viewport.pivot = new Vector2(.5f, 1f);
            viewport.sizeDelta = new Vector2(CellW + 20f, listH);
            viewport.anchoredPosition = new Vector2(0f, -listY);

            _grid = GridView.Attach(viewport, 1, CellW, CellH, parent =>
            {
                var row = new DealRow(this, parent);
                _rows.Add(row);
                return row;
            }, padTop: ListPadTop, padBottom: ListPadBottom);
            _grid.Show(Lead + _deals.Count);

            // The deals sheet's closing key: the kit's blue, at its foot.
            UIKit.TextButton("NotNow", Panel, Skins.Alternate, Loc.Get("ui.common.cancel").Upper(), 36,
                             new Vector2(KeyW, KeyH), new Vector2(.5f, 0f), new Vector2(0f, KeyY),
                             () => Leave());

            // The entrance, in the deals sheet's order (`ChallengeTierOverlay`): the crown first,
            // the window under a crown still settling so the two read as one movement, then the
            // banner and its word. One sound for one tap - see `ModalView.MakePanel`.
            Audio.Hush("click");
            Audio.Sfx("menu", .55f);

            var cue = new Cue(this);
            cue.With(() => { if (frame.Crown) Tween.Pop(frame.Crown.transform, 0f, .5f); });
            cue.Then(.30f, () => { if (Panel) Tween.Scale(Panel, 1f, .5f, Ease.OutBack); });
            cue.Then(.18f, () => { if (frame.Banner) Tween.Pop(frame.Banner.transform, 0f, .5f); });
            cue.Then(.16f, () => { if (frame.Word) Tween.Pop(frame.Word.transform, 0f, .55f); });

            DealLedger.Changed += Repaint;
            PlayerProgression.Changed += Repaint;

            // The welcome row's key follows the store: a price arriving, the sheet opening and
            // closing, the purchase landing.
            StoreService.Changed += Repaint;
        }

        void OnDestroy()
        {
            DealLedger.Changed -= Repaint;
            PlayerProgression.Changed -= Repaint;
            StoreService.Changed -= Repaint;
        }

        /// <summary>
        /// The welcome bundle to head the list, or null: the one-time product badged as the starter,
        /// while the store can sell it to this account right now (or a purchase of it is already in
        /// flight). Owned - by this store account or this game account - is never shown; nor is a
        /// product the store has not priced, because a key with no price is a key that cannot work.
        /// </summary>
        static StoreProduct WelcomeOffer()
        {
            foreach (var product in StoreRules.Catalog.Products)
            {
                if (product == null || product.Badge != StoreBadge.Starter || !product.IsOneTime) continue;
                if (product.IsContainer || product.IsDailyPass) continue;

                switch (StoreService.OfferFor(product).State)
                {
                    case StoreOfferState.Ready:
                    case StoreOfferState.Purchasing:
                    case StoreOfferState.AwaitingGrant:
                        return product;
                }
            }
            return null;
        }

        public override bool OnBack() { Leave(); return true; }

        /// <summary>Every row redrawn: a sync moved the balance, another phone bought one, a deal ended.</summary>
        void Repaint()
        {
            if (this && _grid != null) _grid.Refresh();
        }

        void Update()
        {
            if (_leaving) return;

            float now = Time.unscaledTime;
            for (int i = 0; i < _rows.Count; i++) _rows[i].Animate(now);

            if (now < _nextCheck) return;
            _nextCheck = now + .25f;

            long unix = GameClock.NowUnix();
            for (int i = 0; i < _rows.Count; i++) _rows[i].Tick(unix, now);
        }

        // ------------------------------------------------------------------ the states
        enum RowState { Ready, Bought, Ended, Waiting }

        /// <summary>
        /// The welcome row's state, from the store. Waiting covers the sheet being open, a purchase
        /// paid and not yet credited, and prices still loading - none of them a key to press.
        /// </summary>
        static RowState WelcomeState(StoreProduct product)
        {
            switch (StoreService.OfferFor(product).State)
            {
                case StoreOfferState.Ready: return RowState.Ready;
                case StoreOfferState.Owned: return RowState.Bought;
                case StoreOfferState.Purchasing:
                case StoreOfferState.AwaitingGrant:
                case StoreOfferState.Loading: return RowState.Waiting;
                // Missing or offline after it was offered: said as closed, never sold (44e).
                default: return RowState.Ended;
            }
        }

        /// <summary>The welcome bundle's key: the store's own sheet, or the store's own refusal.</summary>
        void BuyWelcome(StoreProduct product)
        {
            if (_leaving || product == null) return;
            StoreTap.Buy(this, product);
            Repaint();
        }

        /// <summary>
        /// The deal as the server last published it: a row is handed a copy when the panel opens,
        /// and a deal ended from the admin page since then has a new end the copy does not know.
        /// </summary>
        static ShopDeal Latest(ShopDeal deal) => DealLedger.Find(deal.Id) ?? deal;

        RowState StateOf(ShopDeal deal, long nowUnix)
            => DealLedger.IsBought(deal.Id) ? RowState.Bought
             : Latest(deal).IsLive(nowUnix) ? RowState.Ready
             : RowState.Ended;

        // ------------------------------------------------------------------ buying
        /// <summary>True while a tap is waiting on the fresh read; further taps are ignored, not queued.</summary>
        bool _checking;

        void Buy(ShopDeal deal)
        {
            if (_leaving || _checking || deal == null) return;

            // Asked before the ledger, because a deal is the one gem purchase the server must see
            // promptly: bought offline it would be judged at the next sync, possibly after the
            // window closed, and taken back with the coins already spent.
            if (Net.Offline)
            {
                Scenery.Toast(Content, Loc.Get("ui.deal.needs_connection"), Pal.Sun, 2.6f);
                return;
            }

            _checking = true;
            _ = BuyWhenChecked(deal);
        }

        /// <summary>
        /// Reads the published deals once more before charging anything (<see cref="DealLedger.RefreshNowAsync"/>),
        /// so a deal ended from the admin page a moment ago is said as ended rather than bought and
        /// taken back. Observed rather than fire-and-forget: a fault on this path is logged, never
        /// swallowed, and always lets the panel take taps again.
        /// </summary>
        async System.Threading.Tasks.Task BuyWhenChecked(ShopDeal deal)
        {
            bool fresh;
            try
            {
                fresh = await DealLedger.RefreshNowAsync();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                fresh = false;
            }
            finally
            {
                _checking = false;
            }

            // The panel may have gone while the read was out - closed, or the screen changed under it.
            if (!this || _leaving) return;

            if (!fresh)
            {
                Scenery.Toast(Content, Loc.Get("ui.deal.needs_connection"), Pal.Sun, 2.6f);
                return;
            }

            Purchase(Latest(deal));
        }

        void Purchase(ShopDeal deal)
        {
            switch (DealLedger.TryBuy(deal))
            {
                case DealBuy.Bought:
                    // A sound and no haptic: `ShopSupplyOverlay.Confirm`'s rule.
                    Audio.Sfx("coin", .6f);
                    _paid.Add(deal);
                    if (_wanted == deal.Id) _wanted = null;
                    if (Trigger != DealTrigger.None)
                        Telemetry.Track("deal_offer_bought", "trigger", DealPrompt.Id(Trigger), "deal", deal.Id);

                    // The last one this panel can still sell: nothing is left to decide here, so it
                    // gets out of the way of the coins.
                    if (!AnythingLeftToBuy()) { Leave(); return; }

                    // Others are still on sale: the row says BOUGHT, and the coins come home with
                    // the rest when the panel closes.
                    Repaint();
                    return;

                case DealBuy.TooPoor:
                    // The detour: the gem shelf over this panel, and back to this row once the
                    // gems have landed.
                    _wanted = deal.Id;
                    Scenery.Toast(Content, Loc.Get("ui.shop.need_gems"), Pal.Bloom);
                    Flow.Modal<GemShopOverlay>(v => v.Bought = () => { if (this) Repaint(); });
                    return;

                case DealBuy.AlreadyBought:
                    Scenery.Toast(Content, Loc.Get("ui.deal.owned"), Pal.Sun, 2.6f);
                    Repaint();
                    return;

                case DealBuy.Unavailable:
                    Scenery.Toast(Content, Loc.Get("ui.deal.needs_connection"), Pal.Sun, 2.6f);
                    return;

                default:
                    // `Ended`, and anything added later: said as a closed deal rather than charged (44e).
                    Scenery.Toast(Content, Loc.Get("ui.deal.ended"), Pal.Sun, 2.6f);
                    Repaint();
                    return;
            }
        }

        bool AnythingLeftToBuy()
        {
            if (_welcome != null && WelcomeState(_welcome) == RowState.Ready) return true;

            long now = GameClock.NowUnix();
            foreach (var deal in _deals)
                if (StateOf(deal, now) == RowState.Ready) return true;
            return false;
        }

        /// <summary>
        /// Closes the panel, and pays the coins of everything bought from it once it has gone - so
        /// the coins land on the readout of whatever screen is underneath rather than behind a scrim.
        /// </summary>
        void Leave()
        {
            if (_leaving) return;
            _leaving = true;

            if (_paid.Count == 0 && Trigger != DealTrigger.None)
                Telemetry.Track("deal_offer_dismissed", "trigger", DealPrompt.Id(Trigger), "deals", _deals.Count);

            long credits = 0L;
            foreach (var deal in _paid) credits += deal.Credits;

            Close(() => { if (credits > 0L) DealPayout.Play(credits); }, quiet: credits > 0L);
        }

        // ------------------------------------------------------------------ one row
        /// <summary>
        /// One deal, built once and rebound as the list recycles (44mc: every field written on every
        /// bind). The open card (<see cref="DealCard.BuildOpen"/>) - the coffer big at the left with
        /// the value seal over it, no box - and down the right column the amount, the clock and the
        /// price key. Mirrored by <c>Tools/render_deals.py</c>.
        /// <para>
        /// <b>Or the welcome bundle</b>, on the first row while it heads the list: its chest, its gems
        /// over its coins, ONE TIME ONLY where a deal's clock stands, and the store's price on the key.
        /// A thin white rule sits between rows (the owner, 2026-10-11), drawn by every row but the first.
        /// </para>
        /// </summary>
        sealed class DealRow : IGridCell
        {
            // The open shape, not the shop's boxed card (the owner, 2026-10-11: the green box looked
            // too thin on a green window). The amount and seal still come from `DealCard`, so the
            // two can never say different things about a deal.
            public const float CardH = 330f;
            const float CardW = 940f, ColumnW = 520f;
            const float TimerY = -16f, TimerW = 360f, TimerH = 62f, KeyY = -108f, KeyW = 400f, KeyH = 96f;
            const float Pulse = .12f, PulseTime = .25f;

            /// <summary>The rule between two rows: across most of the card, a few units thick, on the gap's middle.</summary>
            const float RuleW = 860f, RuleH = 3f;
            static readonly Color Rule = new Color(1f, 1f, 1f, .85f);

            static readonly Color Calm = Color.white;
            static readonly Color Urgent = new Color(1f, .36f, .28f);
            static readonly Color Spent = new Color(.62f, .66f, .72f);

            readonly DealOverlay _panel;
            readonly RectTransform _root, _timerBox;
            readonly DealCard.Parts _card;
            readonly Text _timer;
            readonly Btn _key;
            readonly Image _keyPlate, _rule;

            ShopDeal _deal;

            /// <summary>The welcome bundle, when this row is drawing it; null when it is drawing a deal.</summary>
            StoreProduct _bundle;
            RowState _state;
            long _shownSeconds = -1;
            float _tickAt;

            public RectTransform Root => _root;

            public DealRow(DealOverlay panel, RectTransform parent)
            {
                _panel = panel;
                _root = UIKit.Box("Deal", parent, new Vector2(CellW, CellH), new Vector2(.5f, 1f), Vector2.zero);

                // The rule on the cell's top edge, which is the middle of the air between this card
                // and the one above (each card is centred in its cell). Behind the card, so a seal
                // rising over the edge is drawn over it.
                _rule = UIKit.Img("Rule", _root, Art.Pixel, Rule, new Vector2(RuleW, RuleH),
                                  new Vector2(.5f, .5f), new Vector2(0f, CellH * .5f));
                _rule.raycastTarget = false;

                _card = DealCard.BuildOpen("Card", _root, new Vector2(CardW, CardH), new Vector2(.5f, .5f),
                                           Vector2.zero, ColumnW);

                // The clock and the key under the amount, on the column's middle.
                _timerBox = (RectTransform)UIKit.Img("Trough", _card.Root, Art.S("Ui/" + Skins.Trough), Color.white,
                                                     new Vector2(TimerW, TimerH), new Vector2(0f, .5f),
                                                     new Vector2(_card.ColumnX, TimerY)).transform;
                _timer = UIKit.Shrinkable(
                    UIKit.Titled("Timer", _timerBox, string.Empty, 36, Calm, TextAnchor.MiddleCenter,
                                 new Vector2(TimerW - 24f, TimerH), new Vector2(.5f, .5f), Vector2.zero, 3f, 2f), 22);

                _key = UIKit.TextButton("Buy", _card.Root, Skins.Gem, string.Empty, 42,
                                        new Vector2(KeyW, KeyH), new Vector2(0f, .5f), new Vector2(_card.ColumnX, KeyY),
                                        Tapped, "ic_gem");
                _keyPlate = _key.GetComponent<Image>();
            }

            public void Bind(int index)
            {
                // The welcome bundle on the first row while it heads the list; the deals after it.
                int lead = _panel.Lead;
                _bundle = lead > 0 && index == 0 ? _panel._welcome : null;

                int dealIndex = index - lead;
                _deal = _bundle == null && dealIndex >= 0 && dealIndex < _panel._deals.Count
                    ? _panel._deals[dealIndex] : null;

                bool drawn = _bundle != null || _deal != null;
                _root.gameObject.SetActive(drawn);
                if (!drawn) return;

                // Every row but the first carries the rule above it.
                _rule.enabled = index > 0;

                _shownSeconds = -1;
                _key.transform.localScale = Vector3.one;
                _timer.rectTransform.localScale = Vector3.one;

                if (_bundle != null)
                {
                    DealCard.PaintBundle(_card, _bundle);
                    PaintBundle(force: true);
                    return;
                }

                DealCard.Paint(_card, _deal);
                Paint(GameClock.NowUnix(), force: true);
            }

            /// <summary>The clock and the key, from the deal and the ledger. Called on bind and four times a second.</summary>
            public void Tick(long nowUnix, float now)
            {
                if (_bundle != null) { PaintBundle(force: false); return; }
                if (_deal == null) return;

                long left = Latest(_deal).SecondsLeft(nowUnix);
                if (left != _shownSeconds) _tickAt = now;
                Paint(nowUnix, force: false);
            }

            /// <summary>
            /// The clock always; the key only when its state moved, or <paramref name="force"/>d by a
            /// bind - a recycled row must never keep the last deal's key (44mc).
            /// </summary>
            void Paint(long nowUnix, bool force)
            {
                var state = _panel.StateOf(_deal, nowUnix);
                long left = Latest(_deal).SecondsLeft(nowUnix);

                _shownSeconds = left;
                // An ended row's clock stops at nought and greys; the key below it says ENDED.
                _timer.text = DealClock.Timer(state == RowState.Ended ? 0L : left);
                _timer.color = state == RowState.Ended ? Spent
                             : state == RowState.Ready && left < DealClock.UrgentSeconds ? Urgent : Calm;

                if (!force && state == _state) return;
                _state = state;

                switch (state)
                {
                    case RowState.Bought:
                        _key.SetCaption(Loc.Get("ui.deal.bought_row").Upper());
                        // Blue rather than `Skins.Settled`'s green: the panel is a green window, and
                        // a green key on it would read as no key at all.
                        _keyPlate.sprite = Art.S("Ui/" + Skins.Alternate);
                        _key.Interactable = false;
                        break;

                    case RowState.Ended:
                        _key.SetCaption(Loc.Get("ui.deal.ended_row").Upper());
                        _keyPlate.sprite = Art.S("Ui/" + Skins.Shut);
                        _key.Interactable = false;
                        break;

                    default:
                        _key.SetCaption(Loc.Format("ui.shop.gem_price", Compact.Number(_deal.Gems)));
                        _keyPlate.sprite = Art.S("Ui/" + Skins.Gem);
                        _key.Interactable = true;
                        break;
                }

                if (_key.Icon) _key.Icon.enabled = state == RowState.Ready;
            }

            /// <summary>
            /// The welcome row: ONE TIME ONLY where a deal's clock stands, and a key that carries the
            /// store's own price string (never one this game formats), waits, or says bought. Written
            /// whole on a bind (<paramref name="force"/>), and again whenever its state or price moved.
            /// </summary>
            void PaintBundle(bool force)
            {
                var offer = StoreService.OfferFor(_bundle);
                var state = WelcomeState(_bundle);

                _timer.text = Loc.Get("ui.shop.badge_starter").Upper();
                _timer.color = state == RowState.Ended ? Spent : Calm;

                string caption = state == RowState.Ready ? offer.Price
                               : state == RowState.Bought ? Loc.Get("ui.deal.bought_row").Upper()
                               : state == RowState.Waiting ? Loc.Get(offer.State == StoreOfferState.AwaitingGrant
                                                                     ? "ui.shop.awaiting_short" : "ui.shop.purchasing")
                               : Loc.Get("ui.deal.ended_row").Upper();

                if (!force && state == _state && caption == _bundleCaption) return;
                _state = state;
                _bundleCaption = caption;

                _key.SetCaption(caption);
                // The money face for a price, the deal rows' blue for bought, shut otherwise.
                _keyPlate.sprite = Art.S("Ui/" + (state == RowState.Ready ? Skins.Buy
                                                 : state == RowState.Bought ? Skins.Alternate
                                                 : Skins.Shut));
                _key.Interactable = state == RowState.Ready;
                if (_key.Icon) _key.Icon.enabled = false;
            }

            /// <summary>The caption the welcome key last wore, so a price arriving repaints it.</summary>
            string _bundleCaption;

            /// <summary>The key: the welcome bundle's sheet, or the deal's gem purchase.</summary>
            void Tapped()
            {
                if (_bundle != null) _panel.BuyWelcome(_bundle);
                else _panel.Buy(_deal);
            }

            /// <summary>Every frame: an urgent second lands with a pulse, the wanted key breathes.</summary>
            public void Animate(float now)
            {
                if (_deal == null) return;

                float k = _state == RowState.Ready && _shownSeconds >= 0 && _shownSeconds < DealClock.UrgentSeconds
                    ? Mathf.Clamp01(1f - (now - _tickAt) / PulseTime) : 0f;
                _timer.rectTransform.localScale = Vector3.one * (1f + Pulse * k * k);

                bool wanted = _state == RowState.Ready && _panel._wanted == _deal.Id
                              && PlayerProgression.CanAfford(Currency.Gems, _deal.Gems);
                _key.transform.localScale = Vector3.one * (wanted ? 1f + .06f * (.5f + .5f * Mathf.Sin(now * 6f)) : 1f);
            }
        }
    }

    /// <summary>
    /// A deal's coins flying into the coin readout of whatever screen is showing, once the panel that
    /// sold them has gone. <see cref="RewardFlight.AfterGrant"/>, because the coins are already in
    /// the wallet (the ledger queued them with the debit) and credits are one of the two things
    /// nothing clamps, so the rewind is exact. A screen with no coin readout gets the toast alone -
    /// the coins are banked either way.
    /// </summary>
    public static class DealPayout
    {
        public static void Play(long credits)
        {
            var host = Flow.Current;
            if (!host || credits <= 0L) return;

            Scenery.Toast(host.Content, Loc.Format("ui.deal.bought", credits.ToString("N0")), Pal.Gold, 2.4f);

            var flight = RewardFlight.AfterGrant(credits, 0L);
            flight.Hold(host);

            var source = UIKit.Box("DealCoins", host.Content, new Vector2(200f, 200f), new Vector2(.5f, .5f), Vector2.zero);
            flight.Add(new Daily.ChestDrop(Daily.ChestDropKind.Credits, (int)Math.Min(credits, int.MaxValue)), source);
            flight.Play(host.Content, () => { if (source) UnityEngine.Object.Destroy(source.gameObject); });
        }
    }
}
