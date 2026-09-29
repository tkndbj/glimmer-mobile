using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Challenges
{
    /// <summary>
    /// One play dealt by <see cref="ChallengeLedger.Begin"/>: which level, on which day, as
    /// which slot - and whether it has been <see cref="Spent"/> yet, which happens at the
    /// first move (<see cref="ChallengeLedger.Commit"/>), never at the deal.
    /// </summary>
    public sealed class ChallengePlay
    {
        public readonly ChallengeGenre Genre;
        public readonly ChallengeDefinition Definition;

        /// <summary>The day the play was dealt on. A win is paid against this day, whatever the clock says later.</summary>
        public readonly int Day;

        /// <summary>The slot of the day's sequence this play is, which is the wins the day had when it was dealt.</summary>
        public readonly int Slot;

        /// <summary>Which attempt of the day this was, one-based. Nought until <see cref="Spent"/>.</summary>
        public int Attempt { get; internal set; }

        /// <summary>
        /// Which deal of the row this play is: a pure function of the day and the attempt it
        /// will be (<see cref="DealOf"/>), never stored. A genre that shuffles what its row
        /// authors (Pairs, 56m) deals from it, so every player's first try of a day is one
        /// board, a retry is a fresh one, and a board opened and left untouched - which spends
        /// nothing - is dealt again exactly as it was. Never nought, which means "as written".
        /// </summary>
        public readonly uint Deal;

        /// <summary>Whether one of the day's plays has been taken for this. False on a board nobody has touched.</summary>
        public bool Spent { get; internal set; }

        /// <summary>
        /// The day whose plays paid for this, set when it is <see cref="Spent"/>: the day of the
        /// first move, which is the dealt <see cref="Day"/> unless midnight fell between the deal
        /// and the move. **A win is claimed against this day and never another** - see
        /// <see cref="ChallengeLedger.Commit"/> for the fault that rule closes.
        /// </summary>
        public int ChargedDay { get; internal set; }

        /// <summary>
        /// The slot of <see cref="ChargedDay"/>'s sequence this play wins as: <see cref="Slot"/>
        /// when it was charged to the day it was dealt on, else the wins that day had when it
        /// was charged. A win is claimed as ordinal <c>ClaimSlot + 1</c>.
        /// </summary>
        public int ClaimSlot { get; internal set; }

        internal ChallengePlay(ChallengeGenre genre, ChallengeDefinition definition, int day, int slot, int attempt)
        {
            Genre = genre;
            Definition = definition;
            Day = day;
            Slot = slot;
            Deal = DealOf(day, attempt);
        }

        /// <summary>
        /// The deal of a day's <paramref name="attempt"/>-th play (one-based): mixed, so two
        /// neighbouring attempts are unrelated boards, and never nought.
        /// </summary>
        public static uint DealOf(int day, int attempt)
            => ChallengeCalendar.Mix(unchecked((uint)day * 0x9E3779B1u + (uint)attempt * 0x85EBCA6Bu)) | 1u;
    }

    /// <summary>What a win paid. Nought all round when the rule pays nothing or the claim already existed.</summary>
    public readonly struct ChallengeReward
    {
        public readonly int Coins;
        public readonly int Xp;

        /// <summary>What a running XP boost added on top of <see cref="Xp"/>.</summary>
        public readonly long BonusXp;

        public ChallengeReward(int coins, int xp, long bonusXp)
        {
            Coins = coins;
            Xp = xp;
            BonusXp = bonusXp;
        }

        public static readonly ChallengeReward None = new ChallengeReward(0, 0, 0L);
        public bool Any => Coins > 0 || Xp > 0;
    }

    /// <summary>Why a deal could not be bought, so a page can say which wall it is.</summary>
    public enum TierBuy
    {
        Bought,

        /// <summary>This very deal is running. Nothing is charged.</summary>
        Held,

        /// <summary>A deal at least as large is running; buying this one under it would be gems for nothing.</summary>
        Lower,

        /// <summary>The file sells no such deal.</summary>
        NotSold,

        TooPoor,
    }

    /// <summary>
    /// The daily challenges' save block: how many plays of each genre have been spent and won
    /// today, how many levels this account has ever cleared, and which deals it has bought.
    ///
    /// <para>
    /// <b>Three kinds of number, each with the one merge rule its shape allows</b> (invariant
    /// 11b). Today's rows are period counters - the later day wins outright, and within a shared
    /// day each genre's attempts and wins take the larger value, which is the task ledger's rule
    /// one level over. The lifetime clears are a monotonic tally per genre joined by <c>max</c>,
    /// the storable-count exception for the fifth time. A deal is one instant per tier id, when
    /// its window began, joined by <c>max</c>; the window it covers is derived from that instant
    /// and the tier's authored length - exactly that many days of the clock - so there is one
    /// number and nothing to disagree about (48c's shape).
    /// </para>
    /// <para>
    /// <b>What a forged file buys, field by field.</b> Today's rows buy plays, which pay nothing
    /// by themselves. The tally buys XP inside a bounded range and no currency (invariant 13's
    /// fourth clause; <see cref="ChallengeRewardRule"/>). A tier row buys a page that offers
    /// more plays - and the coin claim for every play past the free allowance is priced by the
    /// server against the deal <em>it</em> recorded when the gems were taken, so a tier the
    /// server never sold pays exactly the free figure. The client's copy draws the page and
    /// gates nothing that pays, which is <see cref="Events.SeasonLedger.OwnsPass"/>'s sentence.
    /// </para>
    /// <para>
    /// <b>A play is spent at the first move</b> (<see cref="Commit"/>), never at the deal and
    /// never at the ending. Not at the ending, or leaving a losing board before it lost would be
    /// a free retry for ever; and not at the deal - which is where it was until 2026-09-26 -
    /// because a player who opens a board, looks at it and backs out has learned nothing they
    /// could use: the calendar deals the same level again (56f), so an untouched deal is not
    /// information and charging for it read as the game stealing a play (the owner's
    /// instruction). <see cref="Begin"/> deals and <see cref="Commit"/> spends; a win or a loss
    /// commits an uncommitted play first, so nothing can be won for free. A win pays against
    /// the day and slot the play was dealt as (<see cref="ChallengePlay"/>), so a run that
    /// crosses midnight is still paid - as the last win of the day it began, which is the day
    /// the server's window accepts it on.
    /// </para>
    /// <para>
    /// <b>Independent of the core game by construction.</b> Nothing here reads a level record,
    /// a star, a heart or a ward; what it touches outside its own block is the wallet - a claim
    /// under a derived id and a gem debit under another - and <see cref="XpBoost.Bank"/>, which
    /// is the one multiplier on XP in the game and the only door a new XP source is allowed to
    /// use. Retuning a challenge moves nothing in <c>progression.json</c>.
    /// </para>
    /// </summary>
    public static class ChallengeLedger
    {
        public const int MaxTodayRows = ChallengeLimits.MaxTodayRows;
        public const int MaxClearRows = ChallengeLimits.MaxClearRows;
        public const int MaxTierRows = ChallengeLimits.MaxTierRows;

        sealed class DayRow
        {
            public int Attempts, Wins;
        }

        static int _day;
        static readonly Dictionary<string, DayRow> _today = new Dictionary<string, DayRow>(StringComparer.Ordinal);
        static readonly Dictionary<string, int> _clears = new Dictionary<string, int>(StringComparer.Ordinal);
        static readonly Dictionary<string, long> _tiers = new Dictionary<string, long>(StringComparer.Ordinal);

        /// <summary>Raised when anything a page draws off this ledger moved, including the day turning.</summary>
        public static event Action Changed;

        static ChallengeTable Table => ChallengeRules.Table;

        static ChallengeLedger()
        {
            // A deal is bought optimistically (`TryBuyTier` writes the date beside the debit),
            // so the one thing that can take it back is the debit being refused - the season
            // pass's shape, for its reason (47o). The ledger says so by id; this is the listener.
            CurrencyLedger.SpendRejected += OnSpendRejected;

            // A play an advert earned lands on its own clock - seconds after the video, when the
            // server's count comes back - so a page drawing plays listens here for it.
            ChallengeAdPlays.Changed += Raise;
        }

        // ------------------------------------------------------------- the day
        /// <summary>
        /// Brings the rows up to the trusted clock. The day turning is the one change here that
        /// nobody makes, so it is detected on read rather than announced - a page that asks
        /// after midnight is told, and the hub's badge repaints off the event this raises.
        /// </summary>
        static void Sync()
        {
            int today = ChallengeCalendar.Today();
            if (today == _day) return;

            _day = today;
            _today.Clear();
            SaveService.MarkDirty();
            Raise();
        }

        /// <summary>Today's key, as the rows count it.</summary>
        public static int Day
        {
            get { Sync(); return _day; }
        }

        // ------------------------------------------------------------- reading
        /// <summary>Plays of each genre allowed today: the running deal's figure, else the free one.</summary>
        public static int Allowance
        {
            get
            {
                Sync();
                var held = HeldTier;
                return held != null ? held.Plays : Table.FreePlays;
            }
        }

        /// <summary>
        /// The deal running now with the most plays, or null. Two can overlap - a larger one
        /// bought as an upgrade shares the smaller's start - and the larger governs while it
        /// runs, because an instant per tier is what the file holds.
        /// </summary>
        public static ChallengeTier HeldTier
        {
            get
            {
                Sync();
                return ChallengeAllowance.Governing(Table.Tiers, _tiers, GameClock.NowUnix());
            }
        }

        /// <summary>Whether a deal is running now, whichever one governs.</summary>
        public static bool Holds(ChallengeTier tier)
            => tier != null && _tiers.TryGetValue(tier.Id, out long from) && tier.Covers(from, GameClock.NowUnix());

        /// <summary>Seconds a running deal has left. Nought when it is not running.</summary>
        public static long SecondsLeft(ChallengeTier tier)
        {
            if (!Holds(tier)) return 0L;
            long left = tier.EndsAt(_tiers[tier.Id]) - GameClock.NowUnix();
            return left > 0L ? left : 0L;
        }

        /// <summary>Whole days a running deal has left, rounded up, so the last day reads as one. Nought when not running.</summary>
        public static int DaysLeft(ChallengeTier tier)
        {
            long left = SecondsLeft(tier);
            if (left <= 0L) return 0;
            return (int)((left + Daily.DailyRules.SecondsPerDay - 1) / Daily.DailyRules.SecondsPerDay);
        }

        /// <summary>
        /// What buying a deal would cost now: the full price, or the difference under a running
        /// smaller deal (an upgrade). Nought when it would be refused.
        /// </summary>
        public static int PriceOf(ChallengeTier tier)
            => ChallengeAllowance.Price(Table.Tiers, _tiers, GameClock.NowUnix(), tier, out _);

        /// <summary>The running deal a purchase of <paramref name="tier"/> would upgrade, or null.</summary>
        public static ChallengeTier Upgrades(ChallengeTier tier)
        {
            ChallengeAllowance.Price(Table.Tiers, _tiers, GameClock.NowUnix(), tier, out var running);
            return running;
        }

        public static int AttemptsToday(ChallengeGenre genre)
        {
            Sync();
            return _today.TryGetValue(ChallengeGenres.NameOf(genre), out var row) ? row.Attempts : 0;
        }

        public static int WinsToday(ChallengeGenre genre)
        {
            Sync();
            return _today.TryGetValue(ChallengeGenres.NameOf(genre), out var row) ? row.Wins : 0;
        }

        /// <summary>
        /// Plays of a genre still allowed today: what is left of the genre's own allowance, plus
        /// the advert plays nobody has spent yet, which any genre may take. Nought once both are
        /// gone.
        /// </summary>
        public static int PlaysLeft(ChallengeGenre genre)
            => ChallengeAdPlays.PlaysLeft(Allowance, AdPlays, AttemptsToday(genre), TodaysAttempts());

        /// <summary>What is left of a genre's own allowance today, the advert pool aside.</summary>
        public static int OwnPlaysLeft(ChallengeGenre genre)
        {
            int left = Allowance - AttemptsToday(genre);
            return left > 0 ? left : 0;
        }

        // ------------------------------------------------------------- the advert pool
        /// <summary>
        /// Extra plays the <c>challenge_play</c> advert earned today, as the server counts them
        /// (<see cref="ChallengeAdPlays"/>). One pool for every genre: the player picks the genre
        /// by playing it.
        /// </summary>
        public static int AdPlays => ChallengeAdPlays.GrantedToday;

        /// <summary>
        /// Advert plays spent today: every play of a genre past its own allowance, summed over
        /// the genres. Derived from the day's rows rather than stored, so it needs no field, no
        /// merge rule and nothing to keep in step - and it reads a play committed on another
        /// phone the moment the rows merge.
        ///
        /// <para>
        /// <b>Counted in plays, where the server counts wins</b> - deliberately the stricter of
        /// the two. The server pays a win past the allowance only while the day's advert count
        /// covers it (<c>drawAdPlay</c>), and every win is a play, so a device that stops
        /// offering when its plays reach the count can never have raised a claim the count does
        /// not cover. A lost play spends the pool here and costs the server nothing, which is
        /// the right way round.
        /// </para>
        /// </summary>
        public static int AdPlaysUsed => ChallengeAdPlays.Used(Allowance, TodaysAttempts());

        /// <summary>Advert plays still to spend today, on any genre.</summary>
        public static int AdPlaysLeft => ChallengeAdPlays.Left(Allowance, AdPlays, TodaysAttempts());

        /// <summary>Every genre's attempts today, for the pool rule (<see cref="ChallengeAdPlays.Used"/>).</summary>
        static IEnumerable<int> TodaysAttempts()
        {
            Sync();
            foreach (var pair in _today) yield return pair.Value.Attempts;
        }

        /// <summary>Whether a genre can be dealt right now: it has rows and plays are left.</summary>
        public static bool CanPlay(ChallengeGenre genre)
            => Table.RowsOf(genre).Count > 0 && PlaysLeft(genre) > 0;

        /// <summary>The level the next play of a genre would deal: today's slot after the wins so far.</summary>
        public static ChallengeDefinition Current(ChallengeGenre genre)
            => ChallengeCalendar.Slot(Table, genre, Day, WinsToday(genre));

        /// <summary>How many genres still have a play left today. What the hub's badge says.</summary>
        public static int ReadyCount
        {
            get
            {
                int n = 0;
                foreach (var genre in Table.Genres)
                    if (PlaysLeft(genre) > 0) n++;
                return n;
            }
        }

        /// <summary>Levels of a genre ever cleared by this account, on any device.</summary>
        public static long ClearsOf(ChallengeGenre genre)
            => _clears.TryGetValue(ChallengeGenres.NameOf(genre), out int n) ? n : 0;

        /// <summary>
        /// Levels ever cleared, every genre summed - the number the XP is a function of.
        ///
        /// Rows a build cannot name still count, because a genre withdrawn from the enum was
        /// played and paid for, and its clears merged across devices under the spelling; the
        /// server sums the same rows. Bounded per row by <see cref="ChallengeLimits.HardMaxClears"/>
        /// at every write, so the sum of sixty-four rows fits a <c>long</c> with room to spare.
        /// </summary>
        public static long LifetimeClears
        {
            get
            {
                long total = 0;
                foreach (var pair in _clears) total += pair.Value;
                return total;
            }
        }

        /// <summary>The lifetime count off a file rather than the live ledger, for a gate or a card.</summary>
        public static long LifetimeClearsIn(SaveFileDto save)
        {
            var clears = new Dictionary<string, int>(StringComparer.Ordinal);
            ReadClears(clears, save?.challenges?.clears);

            long total = 0;
            foreach (var pair in clears) total += pair.Value;
            return total;
        }

        // ------------------------------------------------------------- playing
        /// <summary>
        /// Deals the next play of a genre without spending anything, or answers null when none
        /// is left or the genre has no rows. Nothing is written: a deal nobody touches costs
        /// nothing (the class note), so a screen opened and closed leaves the ledger as it was.
        /// </summary>
        public static ChallengePlay Begin(ChallengeGenre genre)
        {
            Sync();
            if (!CanPlay(genre)) return null;

            var row = Mutable(genre);
            var def = ChallengeCalendar.Slot(Table, genre, _day, row.Wins);
            if (def == null) return null;

            // The attempt this play will be once its first move spends it, so the deal is known
            // before the board is drawn and does not move when the play is committed.
            return new ChallengePlay(genre, def, _day, row.Wins, row.Attempts + 1);
        }

        /// <summary>
        /// Spends one of today's plays on a dealt board, at its first move. Idempotent: a play
        /// is spent once however many times this is asked.
        ///
        /// Saved at once rather than marked dirty: an attempt is the one thing here a player
        /// could want to lose, and a process killed on the board must find it spent on relaunch.
        /// The row charged is today's, whatever day the play was dealt on - a board opened
        /// before midnight and first moved after it is a play of the day it was moved on.
        ///
        /// <para>
        /// <b>And so is its win.</b> The server pays a win past a day's allowance out of that
        /// day's advert plays (<c>drawAdPlay</c>), and the device spends those plays by the
        /// day's attempts - so an attempt charged to one day and a win claimed against another
        /// is a claim the first day's pool never paid for. It was refused once the window
        /// closed and the coins drawn for it taken back (45d). So the play is re-slotted onto
        /// the day that charged it: <see cref="ChallengePlay.ChargedDay"/> and
        /// <see cref="ChallengePlay.ClaimSlot"/>, which is what <see cref="Win"/> claims as.
        /// The server never sees which board was played (56), so a board dealt from yesterday's
        /// calendar and won as today's win costs nothing but the ordinal it takes.
        /// </para>
        /// </summary>
        public static void Commit(ChallengePlay play)
        {
            if (play == null || play.Spent) return;
            Sync();

            var row = Mutable(play.Genre);
            row.Attempts = Bounded(row.Attempts + 1);
            play.Attempt = row.Attempts;
            play.ChargedDay = _day;
            play.ClaimSlot = _day == play.Day ? play.Slot : row.Wins;
            play.Spent = true;

            SaveService.Save();
            Raise();

            Telemetry.Track("challenge_started",
                            "genre", ChallengeGenres.NameOf(play.Genre),
                            "level", play.Definition.Id,
                            "slot", play.Slot,
                            "attempt", play.Attempt,
                            "allowance", Allowance,
                            "ad_plays", AdPlays,
                            "ad_plays_used", AdPlaysUsed);
        }

        /// <summary>
        /// Records that a play was won and pays for it.
        ///
        /// <para>
        /// <b>Credits leave as a claim</b> under an id derived from the day, the genre and the
        /// win's ordinal (<see cref="GrantEntry.ChallengeClearId"/>), so two devices winning one
        /// slot produce one entry and a resubmission confirms instead of paying; the server
        /// prices it against the published rate and bounds the ordinal by the allowance it sold
        /// this account for that day. <b>XP arrives by derivation</b>: the genre's tally rises
        /// here and <see cref="PlayerProgression"/> reads the rule over it; what is banked is
        /// only the boost's share, through the one door a boost is allowed (<see cref="XpBoost.Bank"/>).
        /// </para>
        /// <para>
        /// A win the day's row cannot place - the slot already won on another device and merged
        /// in, or a play dealt on a day that has since turned - still raises the tally and still
        /// raises the claim, because the level was cleared; what it does not do is move a row it
        /// no longer describes.
        /// </para>
        /// </summary>
        public static ChallengeReward Win(ChallengePlay play)
        {
            if (play == null) return ChallengeReward.None;
            Commit(play);
            Sync();

            string name = ChallengeGenres.NameOf(play.Genre);
            var rewards = Table.Rewards;

            // Everything below is the charged day's (`Commit` for why), which is the dealt day
            // unless midnight fell between the deal and the first move.
            if (play.ChargedDay == _day)
            {
                var row = Mutable(play.Genre);
                if (row.Wins == play.ClaimSlot && row.Wins < row.Attempts) row.Wins = Bounded(row.Wins + 1);
            }

            // The tally first, and bounded at the structural ceiling rather than the published one
            // (`ChallengeLimits.HardMaxClears`): a lowered content ceiling stops paying past the new
            // figure and takes nothing out of the file.
            long before = LifetimeClears;
            int held = _clears.TryGetValue(name, out int n) ? n : 0;
            if (held < ChallengeLimits.HardMaxClears) _clears[name] = held + 1;

            int xp = 0;
            long bonus = 0L;
            if (rewards.PaysXp && before < rewards.MaxClears)
            {
                xp = rewards.Xp;
                bonus = XpBoost.Bank(xp);
            }

            int coins = 0;
            if (rewards.PaysCoins)
            {
                long now = GameClock.NowUnix();
                string id = GrantEntry.ChallengeClearId(play.ChargedDay, name, play.ClaimSlot + 1, Currency.Credits);
                if (PlayerProgression.Award(Currency.Credits, rewards.Coins, id, GrantEntry.ChallengeClearReason, now))
                    coins = rewards.Coins;
            }

            // `Award` saves when it pays; a win that paid no coins still moved the tally and a row.
            if (coins <= 0) SaveService.Save();
            Raise();

            Telemetry.Track("challenge_won",
                            "genre", name,
                            "level", play.Definition.Id,
                            "slot", play.Slot,
                            "coins", coins,
                            "xp", xp + bonus);

            return new ChallengeReward(coins, xp, bonus);
        }

        /// <summary>A lost play is spent (a loss takes moves, and the first one spent it); this is the record of it, for the funnel.</summary>
        public static void Lose(ChallengePlay play, int turns)
        {
            if (play == null) return;
            Commit(play);
            Telemetry.Track("challenge_lost",
                            "genre", ChallengeGenres.NameOf(play.Genre),
                            "level", play.Definition.Id,
                            "slot", play.Slot,
                            "turns", turns);
        }

        // ------------------------------------------------------------- buying
        /// <summary>
        /// Buys a deal with gems: a fresh window from this instant, or an upgrade of the deal
        /// running now for the difference, sharing its window (<see cref="ChallengeAllowance.Price"/>).
        ///
        /// <para>
        /// <b>The debit goes first and the instant is written only if it succeeded</b>, which is
        /// <see cref="Events.SeasonLedger.TryBuyPass"/>'s ordering and its argument: a process
        /// killed between the two leaves a player who paid and did not receive, which the spend
        /// log can see and support can put right, where the other order leaves a deal nobody
        /// paid for.
        /// </para>
        /// <para>
        /// <b>The id carries the window's start day and the purchase day</b>
        /// (<see cref="SpendEntry.ChallengeTierId"/>): the first is what the server records and
        /// prices an upgrade against, the second is what it windows the purchase on, and two
        /// devices buying offline on one day still write one entry (48e).
        /// </para>
        /// </summary>
        public static TierBuy TryBuyTier(ChallengeTier tier)
        {
            if (tier == null || Table.FindTier(tier.Id) == null) return TierBuy.NotSold;
            Sync();

            long now = GameClock.NowUnix();
            var running = HeldTier;
            if (running != null && running.Plays >= tier.Plays) return running.Id == tier.Id ? TierBuy.Held : TierBuy.Lower;

            int price = ChallengeAllowance.Price(Table.Tiers, _tiers, now, tier, out var upgraded);
            long fromUnix = upgraded != null ? _tiers[upgraded.Id] : now;

            if (!PlayerProgression.TrySpend(Currency.Gems, price, SpendEntry.ChallengeTierReason,
                                            SpendEntry.ChallengeTierId(tier.Id, ChallengeCalendar.DayOf(fromUnix), _day)))
                return TierBuy.TooPoor;

            if (!_tiers.TryGetValue(tier.Id, out long from) || from < fromUnix) _tiers[tier.Id] = fromUnix;

            SaveService.Save();
            Raise();

            Telemetry.Track("challenge_tier_bought", "tier", tier.Id, "gems", price, "days", tier.Days,
                            "upgraded", upgraded == null ? string.Empty : upgraded.Id);

            return TierBuy.Bought;
        }

        /// <summary>
        /// A deal debit the server refused takes the deal with it.
        ///
        /// The gems are already back (the ledger dropped the entry before announcing it); what
        /// is left is the instant the purchase wrote beside them. Only the exact purchase moves -
        /// a window starting on a later day under the same id is a later purchase and stands.
        /// </summary>
        internal static void OnSpendRejected(string currency, string spendId)
        {
            if (!SpendEntry.TryParseChallengeTierId(spendId, out string tierId, out int fromDay, out _)) return;
            if (!_tiers.TryGetValue(tierId, out long held) || ChallengeCalendar.DayOf(held) != fromDay) return;

            _tiers.Remove(tierId);
            UnityEngine.Debug.LogWarning($"[Challenges] the '{tierId}' deal whose window began on day {fromDay} was " +
                                         "refused by the server; it is no longer held and the gems are back");
            SaveService.Save();
            Raise();
        }

        // ------------------------------------------------------------- internals
        static DayRow Mutable(ChallengeGenre genre)
        {
            string name = ChallengeGenres.NameOf(genre);
            if (!_today.TryGetValue(name, out var row))
            {
                row = new DayRow();
                _today[name] = row;
            }
            return row;
        }

        static int Bounded(int plays)
            => plays < 0 ? 0 : plays > ChallengeLimits.MaxPlaysPerDay ? ChallengeLimits.MaxPlaysPerDay : plays;

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        // --------------------------------------------------- file bridge (internal)
        internal static void LoadFrom(SaveFileDto dto)
        {
            var block = dto?.challenges;
            _day = block == null || block.day < 0 ? 0 : block.day;
            ReadToday(_today, block?.today);
            ReadClears(_clears, block?.clears);
            _tiers.Clear();
            ReadTiers(_tiers, block?.tiers);
            Raise();
        }

        internal static void WriteInto(SaveFileDto dto)
        {
            if (dto == null) return;
            dto.challenges = Write(_day, _today, _clears, _tiers);
        }

        /// <summary>
        /// Joins two devices' blocks. The later day wins outright; within a shared day each
        /// genre's attempts and wins take the larger value; the tallies and the deal dates take
        /// the larger per key. Read through the same reader as the load, so a malformed row is
        /// judged once, and written sorted, so <c>SaveDelta</c>'s ordered walk means something.
        /// </summary>
        public static ChallengeStateDto Join(ChallengeStateDto mine, ChallengeStateDto other)
        {
            var today = new Dictionary<string, DayRow>(StringComparer.Ordinal);
            var clears = new Dictionary<string, int>(StringComparer.Ordinal);
            var tiers = new Dictionary<string, long>(StringComparer.Ordinal);

            int myDay = mine == null || mine.day < 0 ? 0 : mine.day;
            int otherDay = other == null || other.day < 0 ? 0 : other.day;
            int day = myDay > otherDay ? myDay : otherDay;

            if (myDay == day) ReadToday(today, mine?.today);
            if (otherDay == day)
            {
                var theirs = new Dictionary<string, DayRow>(StringComparer.Ordinal);
                ReadToday(theirs, other?.today);
                foreach (var pair in theirs)
                {
                    if (!today.TryGetValue(pair.Key, out var row))
                    {
                        today[pair.Key] = pair.Value;
                        continue;
                    }
                    if (pair.Value.Attempts > row.Attempts) row.Attempts = pair.Value.Attempts;
                    if (pair.Value.Wins > row.Wins) row.Wins = pair.Value.Wins;
                }
            }

            ReadClears(clears, mine?.clears);
            ReadClears(clears, other?.clears);
            ReadTiers(tiers, mine?.tiers);
            ReadTiers(tiers, other?.tiers);

            return Write(day, today, clears, tiers);
        }

        static void ReadToday(Dictionary<string, DayRow> into, ChallengeDayDto[] rows)
        {
            into.Clear();
            if (rows == null) return;

            // The cap bounds the *walk*, malformed rows included - the endless rule's reading,
            // pinned by the shared vectors: a document written past the rules' cap must not
            // cost either side an unbounded walk, and both must stop at the same row.
            int walk = rows.Length < MaxTodayRows ? rows.Length : MaxTodayRows;
            for (int i = 0; i < walk; i++)
            {
                var row = rows[i];
                if (row == null || !IsGenreName(row.genre)) continue;
                int attempts = Bounded(row.attempts);
                int wins = Bounded(row.wins);
                if (attempts <= 0 && wins <= 0) continue;

                // Wins can never exceed attempts: a row saying otherwise is a malformed file, and
                // the reading that cannot be exploited is the one where the attempts are raised.
                if (wins > attempts) attempts = wins;

                if (!into.TryGetValue(row.genre, out var held)) into[row.genre] = new DayRow { Attempts = attempts, Wins = wins };
                else
                {
                    if (attempts > held.Attempts) held.Attempts = attempts;
                    if (wins > held.Wins) held.Wins = wins;
                }
            }
        }

        static void ReadClears(Dictionary<string, int> into, ChallengeCountDto[] rows)
        {
            if (rows == null) return;

            int walk = rows.Length < MaxClearRows ? rows.Length : MaxClearRows;
            for (int i = 0; i < walk; i++)
            {
                var row = rows[i];
                if (row == null || !IsGenreName(row.genre) || row.count <= 0) continue;
                int count = row.count > ChallengeLimits.HardMaxClears ? ChallengeLimits.HardMaxClears : row.count;
                if (!into.TryGetValue(row.genre, out int held) || count > held) into[row.genre] = count;
            }
        }

        static void ReadTiers(Dictionary<string, long> into, ChallengeTierStateDto[] rows)
        {
            if (rows == null) return;

            int walk = rows.Length < MaxTierRows ? rows.Length : MaxTierRows;
            for (int i = 0; i < walk; i++)
            {
                var row = rows[i];
                if (row == null || !ChallengeTable.IsValidTierId(row.id) || row.fromUnix <= 0L) continue;
                if (!into.TryGetValue(row.id, out long held) || row.fromUnix > held) into[row.id] = row.fromUnix;
            }
        }

        /// <summary>A genre spelling is a key on the wire, so it is held to the shape a tier id is.</summary>
        static bool IsGenreName(string name) => ChallengeTable.IsValidTierId(name);

        static ChallengeStateDto Write(int day, Dictionary<string, DayRow> today,
                                       Dictionary<string, int> clears, Dictionary<string, long> tiers)
        {
            var todayRows = new List<ChallengeDayDto>(today.Count);
            foreach (var pair in today)
                if (pair.Value.Attempts > 0 || pair.Value.Wins > 0)
                    todayRows.Add(new ChallengeDayDto { genre = pair.Key, attempts = pair.Value.Attempts, wins = pair.Value.Wins });
            todayRows.Sort((a, b) => string.CompareOrdinal(a.genre, b.genre));
            if (todayRows.Count > MaxTodayRows) todayRows.RemoveRange(MaxTodayRows, todayRows.Count - MaxTodayRows);

            var clearRows = new List<ChallengeCountDto>(clears.Count);
            foreach (var pair in clears)
                if (pair.Value > 0) clearRows.Add(new ChallengeCountDto { genre = pair.Key, count = pair.Value });
            clearRows.Sort((a, b) => string.CompareOrdinal(a.genre, b.genre));
            if (clearRows.Count > MaxClearRows) clearRows.RemoveRange(MaxClearRows, clearRows.Count - MaxClearRows);

            var tierRows = new List<ChallengeTierStateDto>(tiers.Count);
            foreach (var pair in tiers)
                if (pair.Value > 0L) tierRows.Add(new ChallengeTierStateDto { id = pair.Key, fromUnix = pair.Value });
            tierRows.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            if (tierRows.Count > MaxTierRows) tierRows.RemoveRange(MaxTierRows, tierRows.Count - MaxTierRows);

            return new ChallengeStateDto
            {
                day = todayRows.Count == 0 ? 0 : day,
                today = todayRows.ToArray(),
                clears = clearRows.ToArray(),
                tiers = tierRows.ToArray(),
            };
        }

        /// <summary>Test seam: forgets everything, as a fresh install would.</summary>
        internal static void ResetForTests()
        {
            _day = 0;
            _today.Clear();
            _clears.Clear();
            _tiers.Clear();
        }
    }
}
