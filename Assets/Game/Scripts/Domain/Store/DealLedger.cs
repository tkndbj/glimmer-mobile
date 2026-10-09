using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using GlimmerGrove.Analytics;
using GlimmerGrove.Cloud;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Store
{
    /// <summary>What happened when a deal was tapped to buy.</summary>
    public enum DealBuy
    {
        /// <summary>The gems are taken and the coins are in the wallet, both pending the server.</summary>
        Bought,

        /// <summary>The window closed while the panel was open.</summary>
        Ended,

        /// <summary>This account already holds it - on this device or another.</summary>
        AlreadyBought,

        /// <summary>Not enough gems.</summary>
        TooPoor,

        /// <summary>
        /// The server has not yet said what this account owns, so a purchase could be a second
        /// one. Never shown in practice: a deal is not offered until it has.
        /// </summary>
        Unavailable,
    }

    /// <summary>
    /// The shop's limited-time deals on this device (invariant 60): what is on sale, what this
    /// account has bought, and the purchase.
    ///
    /// <para>
    /// <b>Fetched, never listened to.</b> The deals are one public document read with a single
    /// get, at most once every <see cref="StaleSeconds"/> and only when a screen that draws them
    /// asks (<see cref="Refresh"/>). A snapshot listener would bill a read to every open client on
    /// every write, which is the cost curve the owner ruled out (no listeners, ever); a deal made
    /// on the admin page reaches a player on their next launch or within a quarter of an hour, and
    /// its end needs no fetch at all because the clock already knows it.
    /// </para>
    /// <para>
    /// <b>What is bought is the server's</b> (<c>dealsBought</c> on every wallet reply), plus
    /// whatever this device bought that the server has not answered yet - the pending debit and
    /// the pending coins. Nothing about a deal is in the save: the wallet document is the
    /// entitlement, the spend and grant logs are the record, and a device that has not heard from
    /// the server this session offers nothing (<see cref="Offered"/>), so a reinstall cannot sell
    /// a deal twice.
    /// </para>
    /// <para>
    /// <b>A purchase is the season pass's shape</b> (<c>SeasonLedger.TryBuyPass</c>): the debit
    /// under a derived id first, then the coins as a claim under another, so both land at once
    /// on this device and the server pays the coins in the transaction that takes the gems. A
    /// refused debit takes the coins back (<see cref="OnSpendRejected"/>), for 47o's reason.
    /// </para>
    /// </summary>
    public static class DealLedger
    {
        /// <summary>The longest a fetched list is trusted before a screen that draws it asks again.</summary>
        public const long StaleSeconds = 15 * 60;

        /// <summary>How soon a failed fetch may be retried. Short, but not every frame.</summary>
        public const long RetrySeconds = 60;

        /// <summary>Raised when what is on sale, or what is bought, changes. Main thread.</summary>
        public static event Action Changed;

        static List<ShopDeal> _deals = new List<ShopDeal>();
        static readonly HashSet<string> _bought = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Whether a wallet reply has said what this account owns, since the last account change.</summary>
        static bool _known;

        static bool _hooked;

        /// <summary>The read in flight, or the last one. Never two at once.</summary>
        static Task<bool> _inflight;

        /// <summary>When the list may next be fetched, on the monotonic clock; nought means now.</summary>
        static long _nextFetch;

        static DealLedger() => Hook();

        /// <summary>
        /// Installs the refusal and account listeners. Idempotent; called from
        /// <c>PlayerProgression</c>'s hook as well as here, so a refusal heard before any screen
        /// has asked this class anything is still heard (<c>KeeperLedger.Hook</c>'s reason).
        /// </summary>
        public static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            CurrencyLedger.SpendRejected += OnSpendRejected;
            CloudSaveService.IdentityChanged += OnIdentityChanged;
        }

        // ---------------------------------------------------------------- reading
        /// <summary>Every deal the last fetch listed, ended ones included.</summary>
        public static IReadOnlyList<ShopDeal> All => _deals;

        /// <summary>
        /// The deal to draw: on sale, not bought by this account, the one closing soonest - or
        /// null. Null as well until the server has said what this account owns, because a deal
        /// offered before that answer could be sold to somebody who already holds it.
        /// </summary>
        public static ShopDeal Offered => OfferedAt(GameClock.NowUnix());

        public static ShopDeal OfferedAt(long nowUnix)
        {
            if (!_known) return null;

            ShopDeal best = null;
            foreach (var deal in _deals)
            {
                if (!deal.IsLive(nowUnix) || IsBought(deal.Id)) continue;
                if (best == null || deal.EndUnix < best.EndUnix) best = deal;
            }
            return best;
        }

        /// <summary>
        /// Every deal on offer at <paramref name="nowUnix"/> - on sale and not held - closing
        /// soonest first, the id breaking a tie so the order is the same on every call. Empty until
        /// the server has said what this account owns (<see cref="Offered"/>'s reason). What the
        /// deals panel lists, however many the server ever allows at once.
        /// </summary>
        public static List<ShopDeal> OfferedAllAt(long nowUnix)
        {
            var offered = new List<ShopDeal>();
            if (!_known) return offered;

            foreach (var deal in _deals)
                if (deal.IsLive(nowUnix) && !IsBought(deal.Id)) offered.Add(deal);

            offered.Sort((a, b) => a.EndUnix != b.EndUnix ? a.EndUnix.CompareTo(b.EndUnix)
                                                          : string.CompareOrdinal(a.Id, b.Id));
            return offered;
        }

        /// <summary>
        /// Whether this account holds the deal: the server says so, or this device bought it and
        /// the server has not answered yet.
        /// </summary>
        public static bool IsBought(string dealId)
        {
            if (string.IsNullOrEmpty(dealId)) return false;
            if (_bought.Contains(dealId)) return true;

            var gems = Wallet.Ledger(Currency.Gems);
            if (gems != null && gems.HasPending(SpendEntry.ShopDealId(dealId))) return true;

            var credits = Wallet.Ledger(Currency.Credits);
            return credits != null && credits.HasGranted(GrantEntry.ShopDealId(dealId));
        }

        // ---------------------------------------------------------------- fetching
        /// <summary>
        /// Asks for the published deals if the list is stale, nobody is asking already, and an
        /// account is signed in to ask with (the rules grant <c>config/*</c> to signed-in clients
        /// only). Cheap and safe to call from any screen's build: it is a clock comparison on
        /// every call but the one in <see cref="StaleSeconds"/> that reads.
        /// </summary>
        public static void Refresh(bool force = false)
        {
            Hook();
            if (Fetching) return;
            if (!force && Monotonic() < _nextFetch) return;

            var backend = Backend();
            if (backend == null) return;

            _inflight = FetchAsync(backend);
        }

        /// <summary>
        /// Reads the published deals now, whatever the cadence says, and answers whether the read
        /// succeeded. Asked immediately before every purchase: a deal ended from the admin page is
        /// refused by the server from that second, and this is what lets the panel say so before
        /// the player is charged and refunded. A read already in flight is joined rather than
        /// doubled, since it started no earlier than the tap that is waiting on it.
        /// </summary>
        public static Task<bool> RefreshNowAsync()
        {
            Hook();
            if (Fetching) return _inflight;

            var backend = Backend();
            if (backend == null) return Task.FromResult(false);

            _inflight = FetchAsync(backend);
            return _inflight;
        }

        /// <summary>The latest copy of a deal the last read listed, or null. A row asks this rather than trust the copy it was handed.</summary>
        public static ShopDeal Find(string dealId)
        {
            if (string.IsNullOrEmpty(dealId)) return null;
            foreach (var deal in _deals) if (deal.Id == dealId) return deal;
            return null;
        }

        static bool Fetching => _inflight != null && !_inflight.IsCompleted;

        /// <summary>The backend to read with, or null when nobody is signed in to read (the rules grant <c>config/*</c> to signed-in clients only).</summary>
        static IDealBackend Backend()
        {
            if (!CloudSaveService.IsAvailable) return null;
            var backend = CloudSaveService.Backend;
            if (!(backend is IDealBackend deals)) return null;
            return string.IsNullOrEmpty(backend.CurrentIdentity.UserId) ? null : deals;
        }

        static async Task<bool> FetchAsync(IDealBackend backend)
        {
            try
            {
                var (result, deals) = await backend.ReadDealsAsync();

                if (!result.Ok)
                {
                    _nextFetch = Monotonic() + RetrySeconds;
                    return false;
                }

                _nextFetch = Monotonic() + StaleSeconds;
                Adopt(deals);
                return true;
            }
            catch (Exception e)
            {
                _nextFetch = Monotonic() + RetrySeconds;
                UnityEngine.Debug.LogException(e);
                return false;
            }
        }

        /// <summary>Replaces the list, and says so only when it differs.</summary>
        internal static void Adopt(List<ShopDeal> deals)
        {
            deals = deals ?? new List<ShopDeal>();

            bool same = deals.Count == _deals.Count;
            for (int i = 0; same && i < deals.Count; i++) same = deals[i].SameAs(_deals[i]);
            if (same) return;

            _deals = deals;
            Raise();
        }

        /// <summary>Seconds on a clock the player cannot wind, for the fetch cadence only.</summary>
        static long Monotonic() => Stopwatch.GetTimestamp() / Stopwatch.Frequency;

        // ---------------------------------------------------------------- buying
        /// <summary>
        /// Buys a deal: the gems under <see cref="SpendEntry.ShopDealId"/>, then the coins under
        /// <see cref="GrantEntry.ShopDealId"/>, then a sync asked for at once.
        ///
        /// <b>The debit goes first and the coins only if it succeeded</b>, which is
        /// <c>KeeperLedger.TryBuy</c>'s ordering and its argument: a process killed between the
        /// two leaves a player who paid and did not receive - and here the server pays the coins
        /// with the debit anyway, so what the device dropped the server still delivers.
        /// </summary>
        public static DealBuy TryBuy(ShopDeal deal)
        {
            if (deal == null) return DealBuy.Ended;
            if (!deal.IsLive(GameClock.NowUnix())) return DealBuy.Ended;
            if (!_known) return DealBuy.Unavailable;
            if (IsBought(deal.Id)) return DealBuy.AlreadyBought;

            if (!PlayerProgression.TrySpend(Currency.Gems, deal.Gems, SpendEntry.ShopDealReason,
                                            SpendEntry.ShopDealId(deal.Id)))
                return DealBuy.TooPoor;

            PlayerProgression.Award(Currency.Credits, deal.Credits, GrantEntry.ShopDealId(deal.Id),
                                    SpendEntry.ShopDealReason, GameClock.NowUnix());

            // Now rather than eventually: the coins are on screen and the server is the only
            // thing that can make them stay.
            CloudSaveService.RequestSync();

            Telemetry.Track("shop_deal_bought", "deal", deal.Id, "gems", deal.Gems, "credits", deal.Credits);

            Raise();
            return DealBuy.Bought;
        }

        // ---------------------------------------------------------------- the server
        /// <summary>
        /// Folds in what the server says this account bought. <paramref name="carried"/> is asked
        /// apart from the list for <c>KeeperLedger.ApplyServerState</c>'s reason: a deployment
        /// that predates deals sends nothing, and an account that bought none sends an empty
        /// list, and only the second may open the shop's deal band.
        /// </summary>
        public static void ApplyServerState(bool carried, IReadOnlyCollection<string> bought)
        {
            if (!carried) return;

            bool changed = !_known;
            _known = true;

            var incoming = new HashSet<string>(StringComparer.Ordinal);
            if (bought != null) foreach (string id in bought) if (ShopDeals.IsId(id)) incoming.Add(id);

            if (!incoming.SetEquals(_bought))
            {
                _bought.Clear();
                _bought.UnionWith(incoming);
                changed = true;
            }

            if (changed) Raise();
        }

        /// <summary>
        /// A deal debit the server refused takes its coins back. The gems are already back (the
        /// ledger dropped the debit before announcing it); the coins were queued beside it on
        /// this device and nothing else would remove them before the deal closed.
        /// </summary>
        internal static void OnSpendRejected(string currency, string spendId)
        {
            string dealId = SpendEntry.DealOfShopDealId(spendId);
            if (dealId == null) return;

            var credits = Wallet.Ledger(Currency.Credits);
            if (credits != null && credits.WithdrawGrant(GrantEntry.ShopDealId(dealId)))
            {
                UnityEngine.Debug.LogWarning($"[Deals] the purchase of deal {dealId} was refused by the " +
                                             "server; the gems are back and the coins are withdrawn");
                SaveService.Save();
                PlayerProgression.Invalidate();
            }

            Raise();
        }

        /// <summary>What is bought belongs to the account just left; the next wallet reply says the new one's.</summary>
        static void OnIdentityChanged()
        {
            if (!_known && _bought.Count == 0) return;
            _known = false;
            _bought.Clear();
            Raise();
        }

        /// <summary>For tests: forgets everything, as a fresh process would.</summary>
        internal static void ResetForTests()
        {
            _deals = new List<ShopDeal>();
            _bought.Clear();
            _known = false;
            _inflight = null;
            _nextFetch = 0;
        }

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }
    }
}
