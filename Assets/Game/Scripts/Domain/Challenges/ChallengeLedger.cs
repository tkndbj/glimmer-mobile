using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Challenges
{
    /// <summary>One play dealt by <see cref="ChallengeLedger.Begin"/>: which level, on which day, as which slot.</summary>
    public sealed class ChallengePlay
    {
        public readonly ChallengeGenre Genre;
        public readonly ChallengeDefinition Definition;

        /// <summary>The day the play was dealt on. A win is paid against this day, whatever the clock says later.</summary>
        public readonly int Day;

        /// <summary>The slot of the day's sequence this play is, which is the wins the day had when it was dealt.</summary>
        public readonly int Slot;

        /// <summary>Which attempt of the day this was, one-based.</summary>
        public readonly int Attempt;

        internal ChallengePlay(ChallengeGenre genre, ChallengeDefinition definition, int day, int slot, int attempt)
        {
            Genre = genre;
            Definition = definition;
            Day = day;
            Slot = slot;
            Attempt = attempt;
        }
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

        /// <summary>A larger deal is running; buying a smaller one under it would be gems for nothing.</summary>
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
    /// 11b). Today's rows are period counters — the later day wins outright, and within a shared
    /// day each genre's attempts and wins take the larger value, which is the task ledger's rule
    /// one level over. The lifetime clears are a monotonic tally per genre joined by <c>max</c>,
    /// the storable-count exception for the fifth time. A deal is one date per tier id, the day
    /// it was last bought, joined by <c>max</c>; the window it covers is derived from that date
    /// and the tier's authored length, so there is one number and nothing to disagree about
    /// (48c's shape).
    /// </para>
    /// <para>
    /// <b>What a forged file buys, field by field.</b> Today's rows buy plays, which pay nothing
    /// by themselves. The tally buys XP inside a bounded range and no currency (invariant 13's
    /// fourth clause; <see cref="ChallengeRewardRule"/>). A tier row buys a page that offers
    /// more plays — and the coin claim for every play past the free allowance is priced by the
    /// server against the deal <em>it</em> recorded when the gems were taken, so a tier the
    /// server never sold pays exactly the free figure. The client's copy draws the page and
    /// gates nothing that pays, which is <see cref="Events.SeasonLedger.OwnsPass"/>'s sentence.
    /// </para>
    /// <para>
    /// <b>A play is spent when it is dealt</b> (<see cref="Begin"/>), never when it ends, or
    /// leaving a losing board before it lost would be a free retry for ever. A win pays against
    /// the day and slot the play was dealt as (<see cref="ChallengePlay"/>), so a run that
    /// crosses midnight is still paid — as the last win of the day it began, which is the day
    /// the server's window accepts it on.
    /// </para>
    /// <para>
    /// <b>Independent of the core game by construction.</b> Nothing here reads a level record,
    /// a star, a heart or a ward; what it touches outside its own block is the wallet — a claim
    /// under a derived id and a gem debit under another — and <see cref="XpBoost.Bank"/>, which
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
        static readonly Dictionary<string, int> _tiers = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Raised when anything a page draws off this ledger moved, including the day turning.</summary>
        public static event Action Changed;

        static ChallengeTable Table => ChallengeRules.Table;

        static ChallengeLedger()
        {
            // A deal is bought optimistically (`TryBuyTier` writes the date beside the debit),
            // so the one thing that can take it back is the debit being refused — the season
            // pass's shape, for its reason (47o). The ledger says so by id; this is the listener.
            CurrencyLedger.SpendRejected += OnSpendRejected;
        }

        // ------------------------------------------------------------- the day
        /// <summary>
        /// Brings the rows up to the trusted clock. The day turning is the one change here that
        /// nobody makes, so it is detected on read rather than announced — a page that asks
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
        /// The deal running today with the most plays, or null. Two can overlap — a larger one
        /// bought under a smaller — and the larger governs while it runs, the smaller resuming
        /// after, because a date per tier is what the file holds.
        /// </summary>
        public static ChallengeTier HeldTier
        {
            get
            {
                Sync();
                return ChallengeAllowance.Governing(Table.Tiers, _tiers, _day);
            }
        }

        /// <summary>Whether a deal is running today, whichever one governs.</summary>
        public static bool Holds(ChallengeTier tier)
            => tier != null && _tiers.TryGetValue(tier.Id, out int from) && tier.Covers(from, Day);

        /// <summary>Days a running deal has left, today included. Nought when it is not running.</summary>
        public static int DaysLeft(ChallengeTier tier)
        {
            if (!Holds(tier)) return 0;
            return _tiers[tier.Id] + tier.Days - _day;
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

        /// <summary>Plays of a genre still allowed today. Nought once the allowance is spent.</summary>
        public static int PlaysLeft(ChallengeGenre genre)
        {
            int left = Allowance - AttemptsToday(genre);
            return left > 0 ? left : 0;
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
        /// Levels ever cleared, every genre summed — the number the XP is a function of.
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
        /// Deals the next play of a genre, spending one of today's, or answers null when none
        /// is left or the genre has no rows.
        ///
        /// Saved at once rather than marked dirty: an attempt is the one thing here a player
        /// could want to lose, and a process killed on the board must find it spent on relaunch.
        /// </summary>
        public static ChallengePlay Begin(ChallengeGenre genre)
        {
            Sync();
            if (!CanPlay(genre)) return null;

            var row = Mutable(genre);
            var def = ChallengeCalendar.Slot(Table, genre, _day, row.Wins);
            if (def == null) return null;

            row.Attempts = Bounded(row.Attempts + 1);
            var play = new ChallengePlay(genre, def, _day, row.Wins, row.Attempts);

            SaveService.Save();
            Raise();

            Telemetry.Track("challenge_started",
                            "genre", ChallengeGenres.NameOf(genre),
                            "level", def.Id,
                            "slot", play.Slot,
                            "attempt", play.Attempt,
                            "allowance", Allowance);

            return play;
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
        /// A win the day's row cannot place — the slot already won on another device and merged
        /// in, or a play dealt on a day that has since turned — still raises the tally and still
        /// raises the claim, because the level was cleared; what it does not do is move a row it
        /// no longer describes.
        /// </para>
        /// </summary>
        public static ChallengeReward Win(ChallengePlay play)
        {
            if (play == null) return ChallengeReward.None;
            Sync();

            string name = ChallengeGenres.NameOf(play.Genre);
            var rewards = Table.Rewards;

            if (play.Day == _day)
            {
                var row = Mutable(play.Genre);
                if (row.Wins == play.Slot && row.Wins < row.Attempts) row.Wins = Bounded(row.Wins + 1);
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
                string id = GrantEntry.ChallengeClearId(play.Day, name, play.Slot + 1, Currency.Credits);
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

        /// <summary>A lost play is already spent; this is the record of it, for the funnel.</summary>
        public static void Lose(ChallengePlay play, int turns)
        {
            if (play == null) return;
            Telemetry.Track("challenge_lost",
                            "genre", ChallengeGenres.NameOf(play.Genre),
                            "level", play.Definition.Id,
                            "slot", play.Slot,
                            "turns", turns);
        }

        // ------------------------------------------------------------- buying
        /// <summary>
        /// Buys a deal with gems, running from today.
        ///
        /// <para>
        /// <b>The debit goes first and the date is written only if it succeeded</b>, which is
        /// <see cref="Events.SeasonLedger.TryBuyPass"/>'s ordering and its argument: a process
        /// killed between the two leaves a player who paid and did not receive, which the spend
        /// log can see and support can put right, where the other order leaves a deal nobody
        /// paid for.
        /// </para>
        /// <para>
        /// <b>Refused under a deal at least as large</b>, because the larger governs and the
        /// gems would buy nothing today; a larger one over a smaller is an upgrade and allowed,
        /// with the smaller resuming after. The id carries today, so two devices buying offline
        /// on one day write one entry (48e).
        /// </para>
        /// </summary>
        public static TierBuy TryBuyTier(ChallengeTier tier)
        {
            if (tier == null || Table.FindTier(tier.Id) == null) return TierBuy.NotSold;
            Sync();

            var held = HeldTier;
            if (held != null && held.Plays >= tier.Plays) return held.Id == tier.Id ? TierBuy.Held : TierBuy.Lower;

            if (!PlayerProgression.TrySpend(Currency.Gems, tier.Gems, SpendEntry.ChallengeTierReason,
                                            SpendEntry.ChallengeTierId(tier.Id, _day)))
                return TierBuy.TooPoor;

            if (!_tiers.TryGetValue(tier.Id, out int from) || from < _day) _tiers[tier.Id] = _day;

            SaveService.Save();
            Raise();

            Telemetry.Track("challenge_tier_bought", "tier", tier.Id, "gems", tier.Gems, "days", tier.Days);

            return TierBuy.Bought;
        }

        /// <summary>
        /// A deal debit the server refused takes the deal with it.
        ///
        /// The gems are already back (the ledger dropped the entry before announcing it); what
        /// is left is the date the purchase wrote beside them. Only the exact purchase moves —
        /// a later date under the same id is a later purchase and stands.
        /// </summary>
        internal static void OnSpendRejected(string currency, string spendId)
        {
            if (!SpendEntry.TryParseChallengeTierId(spendId, out string tierId, out int fromDay)) return;
            if (!_tiers.TryGetValue(tierId, out int held) || held != fromDay) return;

            _tiers.Remove(tierId);
            UnityEngine.Debug.LogWarning($"[Challenges] the '{tierId}' deal bought on day {fromDay} was refused " +
                                         "by the server; it is no longer held and the gems are back");
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
            var tiers = new Dictionary<string, int>(StringComparer.Ordinal);

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

            // The cap bounds the *walk*, malformed rows included — the endless rule's reading,
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

        static void ReadTiers(Dictionary<string, int> into, ChallengeTierStateDto[] rows)
        {
            if (rows == null) return;

            int walk = rows.Length < MaxTierRows ? rows.Length : MaxTierRows;
            for (int i = 0; i < walk; i++)
            {
                var row = rows[i];
                if (row == null || !ChallengeTable.IsValidTierId(row.id) || row.fromDay <= 0) continue;
                if (!into.TryGetValue(row.id, out int held) || row.fromDay > held) into[row.id] = row.fromDay;
            }
        }

        /// <summary>A genre spelling is a key on the wire, so it is held to the shape a tier id is.</summary>
        static bool IsGenreName(string name) => ChallengeTable.IsValidTierId(name);

        static ChallengeStateDto Write(int day, Dictionary<string, DayRow> today,
                                       Dictionary<string, int> clears, Dictionary<string, int> tiers)
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
                if (pair.Value > 0) tierRows.Add(new ChallengeTierStateDto { id = pair.Key, fromDay = pair.Value });
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
