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
        const float PanelW = 940f;
        const float HeadRoom = 150f;
        const float NoteH = 58f;
        const int MostRows = 3;
        const float CellW = 860f, CellH = 214f;
        const float ListPadTop = 6f, ListPadBottom = 10f;
        const float FootH = 92f, FootRoom = 44f;

        /// <summary>Ink for the light parchment panel, <c>ShopSupplyOverlay.Ink</c>'s reason.</summary>
        static readonly Color Ink = new Color(.36f, .25f, .18f);

        readonly List<ShopDeal> _deals = new List<ShopDeal>();
        readonly List<DealRow> _rows = new List<DealRow>();
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

            int visible = Mathf.Min(_deals.Count, MostRows);
            float listH = ListPadTop + visible * CellH + ListPadBottom;

            float y = HeadRoom;
            float noteY = y;                y += NoteH;
            float listY = y;                y += listH + 10f;
            float footY = y + FootH * .5f;  y += FootH + FootRoom;

            // Not dismissed by the scrim: a stray tap beside a purchase must not throw the offer
            // away, and the way out is a key with a word on it (and the back key).
            MakePanel(new Vector2(PanelW, y), Loc.Get("ui.deal.title"), dismissOnScrim: false);

            UIKit.Shrinkable(
                UIKit.Titled("Note", Panel, Loc.Get("ui.deal.note"), 28, Ink, TextAnchor.MiddleCenter,
                             new Vector2(CellW, NoteH), new Vector2(.5f, 1f), new Vector2(0f, -(noteY + NoteH * .5f)),
                             outline: 0f, shadow: 0f, wrap: true), 18);

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
            _grid.Show(_deals.Count);

            UIKit.TextButton("NotNow", Panel, "btn_red", Loc.Get("ui.common.cancel"), 32,
                             new Vector2(380f, FootH), new Vector2(.5f, 1f), new Vector2(0f, -footY),
                             () => Leave());

            DealLedger.Changed += Repaint;
            PlayerProgression.Changed += Repaint;
        }

        void OnDestroy()
        {
            DealLedger.Changed -= Repaint;
            PlayerProgression.Changed -= Repaint;
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
        enum RowState { Ready, Bought, Ended }

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
        /// bind). The shop band's frame in miniature - the gold rim round the navy window, the coffer
        /// on its turning burst, the amount in full - with the clock and the price key on the right.
        /// </summary>
        sealed class DealRow : IGridCell
        {
            const float FrameW = 840f, FrameH = 186f, Rim = 10f;
            const float ArtSize = 160f, ArtX = 112f, Burst = 200f;
            const float AmountX = 214f;
            const float RightX = 168f, TimerW = 292f, TimerH = 62f, KeyW = 292f, KeyH = 92f;
            const float Pulse = .12f, PulseTime = .25f;

            static readonly Color Calm = Color.white;
            static readonly Color Urgent = new Color(1f, .36f, .28f);
            static readonly Color Spent = new Color(.62f, .66f, .72f);

            readonly DealOverlay _panel;
            readonly RectTransform _root, _rays, _timerBox;
            readonly Text _amount, _timer;
            readonly Btn _key;
            readonly Image _keyPlate;

            ShopDeal _deal;
            RowState _state;
            long _shownSeconds = -1;
            float _tickAt;

            public RectTransform Root => _root;

            public DealRow(DealOverlay panel, RectTransform parent)
            {
                _panel = panel;
                _root = UIKit.Box("Deal", parent, new Vector2(CellW, CellH), new Vector2(.5f, 1f), Vector2.zero);

                var frame = UIKit.Img("Frame", _root, Art.S("Ui/" + Skins.PlateGold), Color.white,
                                      new Vector2(FrameW, FrameH), new Vector2(.5f, .5f), Vector2.zero);
                UIKit.Img("Window", frame.transform, Art.S("Ui/" + Skins.Card), Color.white,
                          new Vector2(FrameW - 2f * Rim, FrameH - 2f * Rim), new Vector2(.5f, .5f), Vector2.zero);

                var artAt = new Vector2(ArtX, 0f);
                UIKit.Img("Glow", frame.transform, Art.Glow(128, 2.1f), Pal.A(Pal.Sun, .55f),
                          new Vector2(Burst * 1.3f, Burst * 1.3f), new Vector2(0f, .5f), artAt);
                _rays = (RectTransform)UIKit.Img("Rays", frame.transform, Art.S("Ui/" + Skins.Badge),
                                                 Pal.A(Pal.Gold, .55f), new Vector2(Burst, Burst),
                                                 new Vector2(0f, .5f), artAt).transform;
                var art = UIKit.Img("Art", frame.transform, Art.S("Ui/Shop/coins_3"), Color.white,
                                    new Vector2(ArtSize, ArtSize), new Vector2(0f, .5f), artAt);
                art.preserveAspect = true;
                art.enabled = art.sprite != null;

                _amount = UIKit.Shrinkable(
                    UIKit.Titled("Amount", frame.transform, string.Empty, 60, Pal.Sun, TextAnchor.MiddleLeft,
                                 new Vector2(300f, 74f), new Vector2(0f, .5f), new Vector2(AmountX + 150f, 18f),
                                 4f, 3f), 34);
                UIKit.Shrinkable(
                    UIKit.Titled("Unit", frame.transform, Loc.Get("ui.endless.coins").Upper(), 28, Pal.Cream,
                                 TextAnchor.MiddleLeft, new Vector2(260f, 36f), new Vector2(0f, .5f),
                                 new Vector2(AmountX + 130f, -38f), 3f, 2f), 18);

                _timerBox = (RectTransform)UIKit.Img("Trough", frame.transform, Art.S("Ui/" + Skins.Trough), Color.white,
                                                     new Vector2(TimerW, TimerH), new Vector2(1f, .5f),
                                                     new Vector2(-RightX, 44f)).transform;
                _timer = UIKit.Shrinkable(
                    UIKit.Titled("Timer", _timerBox, string.Empty, 36, Calm, TextAnchor.MiddleCenter,
                                 new Vector2(TimerW - 24f, TimerH), new Vector2(.5f, .5f), Vector2.zero, 3f, 2f), 22);

                _key = UIKit.TextButton("Buy", frame.transform, Skins.Gem, string.Empty, 36,
                                        new Vector2(KeyW, KeyH), new Vector2(1f, .5f), new Vector2(-RightX, -40f),
                                        () => _panel.Buy(_deal), "ic_gem");
                _keyPlate = _key.GetComponent<Image>();
            }

            public void Bind(int index)
            {
                _deal = index >= 0 && index < _panel._deals.Count ? _panel._deals[index] : null;
                _root.gameObject.SetActive(_deal != null);
                if (_deal == null) return;

                _amount.text = _deal.Credits.ToString("N0");
                _shownSeconds = -1;
                _key.transform.localScale = Vector3.one;
                Paint(GameClock.NowUnix(), force: true);
            }

            /// <summary>The clock and the key, from the deal and the ledger. Called on bind and four times a second.</summary>
            public void Tick(long nowUnix, float now)
            {
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
                        _keyPlate.sprite = Art.S("Ui/" + Skins.Settled);
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

            /// <summary>Every frame: the burst turns, an urgent second lands with a pulse, the wanted key breathes.</summary>
            public void Animate(float now)
            {
                if (_deal == null) return;

                _rays.localRotation = Quaternion.Euler(0f, 0f, -now * 14f);

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
