using System;
using System.Collections.Generic;
using GlimmerGrove.Daily;

namespace GlimmerGrove.Offers
{
    /// <summary>
    /// What this device remembers about offers and play for one account (invariant 64): the days it
    /// was opened, the recent runs, the rewarded views, every offer dealt, closed and bought, and the
    /// one offer standing now.
    ///
    /// <para>
    /// <b>Device-local and in no save, deliberately.</b> Every figure here is a pacing hint or a
    /// reading of behaviour, never money: what a purchase grants is the store product's and the
    /// server's (invariant 18), and whether the account ever paid is the server's
    /// (<see cref="PayerLedger"/>). So nothing here crosses the save's wire (12a), nothing merges, and
    /// a player on two phones is simply paced on each. Kept per account
    /// (<see cref="OfferLedger"/> keys the store on the account id), so a switch never carries one
    /// player's history into another's offers.
    /// </para>
    /// <para>
    /// <b>Bounded</b>: the runs and views are windows, the offer marks are capped at
    /// <see cref="MostMarks"/> (least recently dealt evicted first), so the record cannot grow with
    /// play.
    /// </para>
    /// </summary>
    public sealed class OfferHistory
    {
        /// <summary>How many recent runs are kept for the loss rate.</summary>
        public const int RunWindow = 20;

        /// <summary>How many recent open days are kept: a week is asked about, two are kept.</summary>
        public const int DayWindow = 14;

        /// <summary>Rewarded views older than this are forgotten.</summary>
        public const long AdWindowSeconds = 7L * 86400L;

        /// <summary>The most views kept inside the window.</summary>
        public const int MostAds = 128;

        /// <summary>The most offers remembered.</summary>
        public const int MostMarks = 64;

        readonly OfferLogDto _log;

        public OfferHistory(OfferLogDto log)
        {
            _log = log ?? new OfferLogDto();
            _log.days ??= new List<int>();
            _log.runs ??= new List<int>();
            _log.ads ??= new List<long>();
            _log.marks ??= new List<OfferMarkDto>();
            _log.active ??= new OfferActiveDto();
            _log.lastLevel ??= string.Empty;
        }

        /// <summary>The record as it is to be stored.</summary>
        public OfferLogDto Log => _log;

        // ------------------------------------------------------------------ sessions and days
        /// <summary>
        /// A session began: a launch, or a return after long enough away. Works out how many whole
        /// days the player was gone and opens today's day.
        /// </summary>
        public void BeginSession(long nowUnix)
        {
            if (_log.firstUnix <= 0) _log.firstUnix = nowUnix;

            int today = DailyRules.DayKeyFor(nowUnix);
            int last = _log.lastSeenUnix > 0 ? DailyRules.DayKeyFor(_log.lastSeenUnix) : today;
            _log.awayDays = Math.Max(0, today - last);
            _log.sessionShows = 0;

            Seen(nowUnix);
        }

        /// <summary>The player is here now. Cheap; called often.</summary>
        public void Seen(long nowUnix)
        {
            if (_log.firstUnix <= 0) _log.firstUnix = nowUnix;
            _log.lastSeenUnix = Math.Max(_log.lastSeenUnix, nowUnix);

            int today = DailyRules.DayKeyFor(nowUnix);
            if (!_log.days.Contains(today))
            {
                _log.days.Add(today);
                _log.days.Sort();
                while (_log.days.Count > DayWindow) _log.days.RemoveAt(0);
            }
        }

        public long FirstUnix => _log.firstUnix;
        public int AwayDays => _log.awayDays;

        public int DaysPlaying(long nowUnix)
            => _log.firstUnix <= 0 ? 0 : Math.Max(0, DailyRules.DayKeyFor(nowUnix) - DailyRules.DayKeyFor(_log.firstUnix));

        /// <summary>How many of the last seven days, today included, the game was opened on.</summary>
        public int ActiveDays7(long nowUnix)
        {
            int today = DailyRules.DayKeyFor(nowUnix), count = 0;
            foreach (int day in _log.days) if (day <= today && day > today - 7) count++;
            return count;
        }

        // ------------------------------------------------------------------ runs and views
        /// <summary>A run ended on <paramref name="level"/>.</summary>
        public void NoteRun(string level, bool won)
        {
            _log.runs.Add(won ? 1 : 0);
            while (_log.runs.Count > RunWindow) _log.runs.RemoveAt(0);

            level ??= string.Empty;
            if (won) _log.lossStreak = 0;
            else _log.lossStreak = string.Equals(level, _log.lastLevel, StringComparison.Ordinal) ? _log.lossStreak + 1 : 1;
            _log.lastLevel = level;
        }

        public int Runs => _log.runs.Count;

        public int LossPercent
        {
            get
            {
                if (_log.runs.Count == 0) return 0;
                int losses = 0;
                foreach (int run in _log.runs) if (run == 0) losses++;
                return losses * 100 / _log.runs.Count;
            }
        }

        public int LossStreak => _log.lossStreak;

        /// <summary>A rewarded video was finished.</summary>
        public void NoteAdView(long nowUnix)
        {
            Prune(nowUnix);
            _log.ads.Add(nowUnix);
            while (_log.ads.Count > MostAds) _log.ads.RemoveAt(0);
        }

        public int AdViews7(long nowUnix)
        {
            int count = 0;
            foreach (long at in _log.ads) if (at > nowUnix - AdWindowSeconds && at <= nowUnix) count++;
            return count;
        }

        void Prune(long nowUnix)
        {
            for (int i = _log.ads.Count - 1; i >= 0; i--)
                if (_log.ads[i] <= nowUnix - AdWindowSeconds) _log.ads.RemoveAt(i);
        }

        // ------------------------------------------------------------------ offers
        /// <summary>The offer standing now, or null - one at a time, until bought or run out.</summary>
        public OfferActiveDto Active => _log.active != null && !string.IsNullOrEmpty(_log.active.id) ? _log.active : null;

        /// <summary>Runs the standing offer out if its time is up. Answers whether it did.</summary>
        public bool Expire(long nowUnix)
        {
            var active = Active;
            if (active == null || nowUnix < active.endsUnix) return false;
            _log.active = new OfferActiveDto();
            return true;
        }

        /// <summary>An offer was dealt: it stands until <paramref name="endsUnix"/>, and every pace counts it.</summary>
        public void NoteShown(string offerId, string productId, string look, long nowUnix, long endsUnix)
        {
            var mark = Mark(offerId, create: true);
            mark.shows++;
            mark.lastShownUnix = nowUnix;

            int today = DailyRules.DayKeyFor(nowUnix);
            if (_log.showDay != today) { _log.showDay = today; _log.showsToday = 0; }
            _log.showsToday++;
            _log.sessionShows++;
            _log.lastPopupUnix = Math.Max(_log.lastPopupUnix, nowUnix);

            _log.active = new OfferActiveDto
            {
                id = offerId, product = productId, look = look, shownUnix = nowUnix, endsUnix = endsUnix,
            };
        }

        /// <summary>Any other money popup was raised (a limited-time deal): it paces offers too.</summary>
        public void NotePopup(long nowUnix) => _log.lastPopupUnix = Math.Max(_log.lastPopupUnix, nowUnix);

        /// <summary>The offer panel was closed without buying.</summary>
        public void NoteDismissed(long nowUnix)
        {
            _log.dismissals++;
            _log.lastDismissUnix = Math.Max(_log.lastDismissUnix, nowUnix);
        }

        /// <summary>
        /// A real-money purchase landed. Any purchase quietens offers and ends a run of closes; one of
        /// the standing offer's product is that offer bought, and it stops standing. Answers the offer
        /// it was, or null.
        /// </summary>
        public string NoteBought(string productId, long nowUnix)
        {
            _log.lastPurchaseUnix = Math.Max(_log.lastPurchaseUnix, nowUnix);
            _log.dismissals = 0;

            var active = Active;
            if (active == null || !string.Equals(active.product, productId, StringComparison.Ordinal)) return null;

            Mark(active.id, create: true).buys++;
            string id = active.id;
            _log.active = new OfferActiveDto();
            return id;
        }

        public int ShowsOf(string offerId) => Mark(offerId, create: false)?.shows ?? 0;
        public int BuysOf(string offerId) => Mark(offerId, create: false)?.buys ?? 0;
        public long LastShownOf(string offerId) => Mark(offerId, create: false)?.lastShownUnix ?? 0L;

        public int ShowsOn(int dayKey) => _log.showDay == dayKey ? _log.showsToday : 0;
        public int SessionShows => _log.sessionShows;
        public long LastPopupUnix => _log.lastPopupUnix;
        public long LastPurchaseUnix => _log.lastPurchaseUnix;
        public int Dismissals => _log.dismissals;
        public long LastDismissUnix => _log.lastDismissUnix;

        OfferMarkDto Mark(string offerId, bool create)
        {
            if (string.IsNullOrEmpty(offerId)) return null;
            foreach (var mark in _log.marks)
                if (string.Equals(mark.id, offerId, StringComparison.Ordinal)) return mark;
            if (!create) return null;

            if (_log.marks.Count >= MostMarks)
            {
                int oldest = 0;
                for (int i = 1; i < _log.marks.Count; i++)
                    if (_log.marks[i].lastShownUnix < _log.marks[oldest].lastShownUnix) oldest = i;
                _log.marks.RemoveAt(oldest);
            }

            var made = new OfferMarkDto { id = offerId };
            _log.marks.Add(made);
            return made;
        }
    }

    /// <summary>The stored shape of <see cref="OfferHistory"/>. Device-local, read with <c>JsonUtility</c>.</summary>
    [Serializable]
    public sealed class OfferLogDto
    {
        public int v = 1;
        public long firstUnix;
        public long lastSeenUnix;
        public int awayDays;
        public List<int> days = new List<int>();
        public List<int> runs = new List<int>();
        public string lastLevel = string.Empty;
        public int lossStreak;
        public List<long> ads = new List<long>();
        public List<OfferMarkDto> marks = new List<OfferMarkDto>();
        public int showDay;
        public int showsToday;
        public int sessionShows;
        public long lastPopupUnix;
        public int dismissals;
        public long lastDismissUnix;
        public long lastPurchaseUnix;
        public OfferActiveDto active = new OfferActiveDto();
    }

    [Serializable]
    public sealed class OfferMarkDto
    {
        public string id = string.Empty;
        public int shows;
        public int buys;
        public long lastShownUnix;
    }

    /// <summary>The offer standing now. An empty <see cref="id"/> is none (<c>JsonUtility</c> never writes null).</summary>
    [Serializable]
    public sealed class OfferActiveDto
    {
        public string id = string.Empty;
        public string product = string.Empty;
        public string look = string.Empty;
        public long shownUnix;
        public long endsUnix;
    }
}
