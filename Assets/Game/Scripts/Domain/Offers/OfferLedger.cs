using System;
using GlimmerGrove.Analytics;
using GlimmerGrove.Cloud;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Store;
using UnityEngine;

namespace GlimmerGrove.Offers
{
    /// <summary>The offer standing now, as a screen draws it.</summary>
    public sealed class StandingOffer
    {
        public StandingOffer(string id, OfferLook look, StoreProduct product, long shownUnix, long endsUnix)
        {
            Id = id;
            Look = look;
            Product = product;
            ShownUnix = shownUnix;
            EndsUnix = endsUnix;
        }

        public string Id { get; }
        public OfferLook Look { get; }
        public StoreProduct Product { get; }
        public long ShownUnix { get; }
        public long EndsUnix { get; }

        public long SecondsLeft(long nowUnix) => Math.Max(0L, EndsUnix - nowUnix);

        public bool SameAs(StandingOffer other)
            => other != null && other.Id == Id && other.EndsUnix == EndsUnix && other.Product == Product;
    }

    /// <summary>
    /// <b>The offer engine's state and its one door</b> (invariant 64): it hears what happens in play
    /// (runs, shortfalls, views, purchases), keeps this device's <see cref="OfferHistory"/> for the
    /// signed-in account, builds the player's <see cref="OfferProfile"/>, asks
    /// <see cref="OfferRules.Choose"/> when <c>OfferTrigger</c> finds a calm screen, and holds the one
    /// offer that stands until it is bought or runs out.
    ///
    /// <para>
    /// <b>It never moves money.</b> An offer is a real-money store product on the offers shelf; what
    /// it grants is that product's, granted by the server against a validated receipt (18, 18a), and a
    /// purchase arrives here as <see cref="StoreService.Granted"/> like any other. So everything the
    /// engine decides is a choice of <em>which</em> product to show and <em>when</em>, and a forged
    /// decision buys only a published product at its published price (13).
    /// </para>
    /// </summary>
    public static class OfferLedger
    {
        /// <summary>The device-local key the history is kept under, one per account.</summary>
        public const string HistoryKeyPrefix = "glimmer.offers.log.";

        static bool _hooked;
        static string _account = string.Empty;
        static OfferHistory _history;
        static EndlessCoins.ITallyStore _store = new PrefsStore();
        static bool _armSaid;

        static OfferTrigger _pending;
        static long _gap;

        /// <summary>Raised when the standing offer comes, goes, or is bought.</summary>
        public static event Action Changed;

        public static void Hook()
        {
            if (_hooked) return;
            _hooked = true;

            PayerLedger.Hook();
            StoreService.Granted += OnGranted;
            CloudSaveService.IdentityChanged += OnIdentityChanged;
        }

        // ------------------------------------------------------------------ the account
        static string Account => CloudState.UserId ?? string.Empty;

        /// <summary>This account's history, loaded on first ask and again after a switch. Null while signed out.</summary>
        static OfferHistory History
        {
            get
            {
                string account = Account;
                if (account.Length == 0) return null;
                if (_history != null && account == _account) return _history;

                _account = account;
                _history = Load(account);
                _history.BeginSession(GameClock.NowUnix());
                _pending = OfferTrigger.Session;
                _armSaid = false;
                return _history;
            }
        }

        static void OnIdentityChanged()
        {
            if (_history != null) Save();
            _history = null;
            _account = string.Empty;
            _pending = OfferTrigger.None;
            _gap = 0L;
            Raise();
        }

        static OfferHistory Load(string account)
        {
            OfferLogDto log = null;
            try
            {
                string text = _store.Read(HistoryKeyPrefix + account);
                if (!string.IsNullOrEmpty(text)) log = JsonUtility.FromJson<OfferLogDto>(text);
            }
            catch (Exception e)
            {
                // A pacing hint that will not read costs pacing, never a session.
                Debug.LogWarning("[Offers] the device's offer history did not read and starts afresh: " + e.Message);
            }
            return new OfferHistory(log);
        }

        /// <summary>Writes the history. A compared, flushed preferences write, so an unchanged one costs nothing.</summary>
        public static void Save()
        {
            if (_history == null || _account.Length == 0) return;
            try { _store.Write(HistoryKeyPrefix + _account, JsonUtility.ToJson(_history.Log)); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // ------------------------------------------------------------------ what play says
        /// <summary>A session began on this account: a return after long enough away.</summary>
        public static void BeginSession()
        {
            var history = History;
            if (history == null) return;
            history.BeginSession(GameClock.NowUnix());
            _pending = Stronger(_pending, OfferTrigger.Session);
            Save();
        }

        /// <summary>The player is here. Cheap; the history is written when the app is put away.</summary>
        public static void Seen() => History?.Seen(GameClock.NowUnix());

        /// <summary>
        /// A run ended. Feeds the loss rate and the streak of losses, and leaves the moment it was for
        /// the next calm screen: a near miss when a lost run got <see cref="OfferTuning.NearMissPercent"/>
        /// of the way (<paramref name="reached"/> of <paramref name="wanted"/>).
        /// </summary>
        public static void NoteRun(string level, bool won, int reached, int wanted, bool openedChapter)
        {
            try
            {
                var history = History;
                if (history == null) return;
                history.NoteRun(level, won);

                OfferTrigger moment;
                if (won) moment = openedChapter ? OfferTrigger.Chapter : OfferTrigger.Win;
                else
                {
                    int percent = OfferConfig.Current.NearMissPercent;
                    moment = wanted > 0 && (long)reached * 100L >= (long)percent * wanted ? OfferTrigger.NearMiss : OfferTrigger.Loss;
                }

                _pending = Stronger(_pending, moment);
                Save();
            }
            catch (Exception e)
            {
                // Nothing an offer can do may reach the run that ended.
                Debug.LogException(e);
            }
        }

        /// <summary>A coin price was shown that the player is <paramref name="coins"/> short of.</summary>
        public static void NoteShortfall(long coins)
        {
            if (coins <= 0) return;
            _gap = coins;
            _pending = Stronger(_pending, OfferTrigger.Shortfall);
        }

        /// <summary>A rewarded video was finished.</summary>
        public static void NoteAdView()
        {
            var history = History;
            if (history == null) return;
            history.NoteAdView(GameClock.NowUnix());
            Save();
        }

        /// <summary>A limited-time deals popup was raised: it paces offers as one of their own does.</summary>
        public static void NotePopup()
        {
            var history = History;
            if (history == null) return;
            history.NotePopup(GameClock.NowUnix());
            Save();
        }

        /// <summary>The moment waiting for a calm screen, strongest kept.</summary>
        public static OfferTrigger Pending => _pending;

        /// <summary>Forgets the waiting moment once it has been asked about.</summary>
        public static void Spend()
        {
            _pending = OfferTrigger.None;
            _gap = 0L;
        }

        /// <summary>Which of two moments is the better reason to speak: a shortfall over a near miss over a loss, and so on.</summary>
        static OfferTrigger Stronger(OfferTrigger a, OfferTrigger b) => Rank(b) > Rank(a) ? b : a;

        static int Rank(OfferTrigger moment)
        {
            switch (moment)
            {
                case OfferTrigger.Shortfall: return 6;
                case OfferTrigger.NearMiss: return 5;
                case OfferTrigger.Loss: return 4;
                case OfferTrigger.Chapter: return 3;
                case OfferTrigger.Win: return 2;
                case OfferTrigger.Session: return 1;
                default: return 0;
            }
        }

        // ------------------------------------------------------------------ the profile
        /// <summary>The player as the rules see them right now.</summary>
        public static OfferProfile Profile()
        {
            long now = GameClock.NowUnix();
            var history = History;
            var profile = new OfferProfile();

            if (history != null)
            {
                profile.Set(OfferMeasure.DaysPlaying, history.DaysPlaying(now))
                       .Set(OfferMeasure.ActiveDays7, history.ActiveDays7(now))
                       .Set(OfferMeasure.Runs, history.Runs)
                       .Set(OfferMeasure.LossPercent, history.LossPercent)
                       .Set(OfferMeasure.LossStreak, history.LossStreak)
                       .Set(OfferMeasure.AdViews7, history.AdViews7(now))
                       .Set(OfferMeasure.DaysAway, history.AwayDays)
                       .Set(OfferMeasure.Dismissals, history.Dismissals);
            }

            profile.Set(OfferMeasure.Purchases, PayerLedger.Purchases)
                   .Set(OfferMeasure.SpendCents, PayerLedger.SpendCents)
                   .Set(OfferMeasure.TopCents, PayerLedger.TopCents)
                   .Set(OfferMeasure.DaysSincePurchase, PayerLedger.LastUnix > 0
                        ? Math.Max(0L, (now - PayerLedger.LastUnix) / 86400L) : OfferProfile.Never)
                   .Set(OfferMeasure.CoinGap, _gap)
                   .Set(OfferMeasure.KeeperLevel, PlayerProgression.Level.Level)
                   .Set(OfferMeasure.ClearedLevels, PlayerProgress.ClearedCount);

            return profile;
        }

        // ------------------------------------------------------------------ dealing
        /// <summary>
        /// Asks the rules whether <paramref name="moment"/> deals an offer, and if it does, makes it
        /// the standing one. Nothing is dealt before a wallet reply has said what the account paid.
        /// </summary>
        public static OfferDeal TryDeal(OfferTrigger moment)
        {
            try
            {
                var history = History;
                if (history == null) return OfferDeal.No(OfferRefusal.NoAccount);
                if (!PayerLedger.Known) return OfferDeal.No(OfferRefusal.Off);

                var tuning = OfferConfig.Current;
                long now = GameClock.NowUnix();
                SayArm(tuning);

                var profile = Profile();
                var deal = OfferRules.Choose(tuning, profile, moment, history, Shelf.Instance, _account, now);
                if (!deal.Dealt) return deal;

                long ends = now + deal.Offer.LastsMinutes * 60L;
                history.NoteShown(deal.Offer.Id, deal.ProductId, OfferLooks.Id(deal.Offer.Look), now, ends);
                Save();

                Telemetry.Track("offer_shown",
                                "offer", deal.Offer.Id,
                                "product", deal.ProductId,
                                "moment", OfferTriggers.Id(moment),
                                "look", OfferLooks.Id(deal.Offer.Look),
                                "segments", Clip(OfferRules.SegmentsOf(tuning, profile)),
                                "revision", tuning.Revision);

                Raise();
                return deal;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return OfferDeal.No(OfferRefusal.Off);
            }
        }

        /// <summary>The offer standing now, or null. Runs it out if its time is up.</summary>
        public static StandingOffer Standing
        {
            get
            {
                var history = History;
                if (history == null) return null;

                long now = GameClock.NowUnix();
                var active = history.Active;
                if (active != null && history.Expire(now))
                {
                    Telemetry.Track("offer_expired", "offer", active.id, "product", active.product);
                    Save();
                    Raise();
                    return null;
                }

                if (active == null) return null;

                var product = StoreRules.Find(active.product);
                if (product == null || product.Shelf != StoreShelf.Offers) return null;

                return new StandingOffer(active.id, OfferLooks.Parse(active.look), product, active.shownUnix, active.endsUnix);
            }
        }

        /// <summary>The offer's panel was closed without buying. Each close makes the next offer wait longer.</summary>
        public static void Dismissed(StandingOffer offer)
        {
            var history = History;
            if (history == null || offer == null) return;
            history.NoteDismissed(GameClock.NowUnix());
            Save();
            Telemetry.Track("offer_dismissed", "offer", offer.Id, "product", offer.Product?.Id ?? string.Empty,
                            "dismissals", history.Dismissals);
        }

        /// <summary>The offer's price key was tapped and the store's sheet asked for.</summary>
        public static void Tapped(StandingOffer offer)
        {
            if (offer == null) return;
            Telemetry.Track("offer_tapped", "offer", offer.Id, "product", offer.Product?.Id ?? string.Empty);
        }

        static void OnGranted(StoreGrant grant)
        {
            try
            {
                var history = History;
                if (history == null || !grant.IsValid) return;

                string bought = history.NoteBought(grant.Product.Id, GameClock.NowUnix());
                Save();

                if (bought != null)
                    Telemetry.Track("offer_bought", "offer", bought, "product", grant.Product.Id);

                Raise();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// The arm this account is measured in, for analytics: <c>holdout</c> or <c>offers</c>, and
        /// <c>none</c> while signed out (asked before the tuning, so a signed-out caller reads nothing).
        /// </summary>
        public static string Arm
        {
            get
            {
                string account = Account;
                return account.Length == 0 ? "none" : OfferRules.Arm(account, OfferConfig.Current.HoldoutPercent);
            }
        }

        static void SayArm(OfferTuning tuning)
        {
            if (_armSaid) return;
            _armSaid = true;
            Telemetry.Track("offer_arm", "arm", OfferRules.Arm(_account, tuning.HoldoutPercent),
                            "holdout_percent", tuning.HoldoutPercent, "revision", tuning.Revision);
        }

        static string Clip(string text) => text == null || text.Length <= 100 ? text : text.Substring(0, 100);

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // ------------------------------------------------------------------ the store, as the rules see it
        sealed class Shelf : IOfferShelf
        {
            public static readonly Shelf Instance = new Shelf();

            public bool CanSell(string productId)
            {
                var product = StoreRules.Find(productId);
                if (product == null || product.Shelf != StoreShelf.Offers) return false;
                return StoreService.OfferFor(product).State == StoreOfferState.Ready;
            }

            public long CreditsOf(string productId) => StoreRules.Find(productId)?.Credits ?? 0L;
        }

        sealed class PrefsStore : EndlessCoins.ITallyStore
        {
            public string Read(string key) => PlayerPrefs.GetString(key, string.Empty);
            public void Write(string key, string value) => DevicePrefs.WriteString(key, value);

            public void Delete(string key)
            {
                if (!PlayerPrefs.HasKey(key)) return;
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Installs a store and forgets everything held; the game's own for null. For fixtures.</summary>
        internal static void UseStore(EndlessCoins.ITallyStore store)
        {
            _store = store ?? new PrefsStore();
            _history = null;
            _account = string.Empty;
            _pending = OfferTrigger.None;
            _gap = 0L;
            _armSaid = false;
        }
    }
}
