using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Tasks;

namespace GlimmerGrove.Daily
{
    /// <summary>
    /// How many days in a row the player has finished a run, and what that is worth.
    ///
    /// <para>
    /// <b>Why a streak at all.</b> Every other reward in this game asks the player to want
    /// something they do not have. A streak is the only one that asks them to protect
    /// something they already do, and that is a categorically stronger pull: people work
    /// harder to keep six days than to earn a seventh. It costs nothing to hold and it is
    /// lost by doing nothing, which is exactly the shape that brings somebody back on the
    /// evening they were not otherwise going to open the game.
    /// </para>
    /// <para>
    /// <b>Why it is stored as dates and not as a count.</b> Invariant 11b. A count is
    /// not mergeable: a device holding 6 and one holding 1 are equally consistent with
    /// "one of them is behind" and "the streak broke and restarted", so every rule over the
    /// pair is wrong somewhere - and the wrong one here silently resurrects a streak the
    /// player really did break, or deletes one they really do hold. Dates have no such
    /// ambiguity. <see cref="StartDay"/> is the day the current run began and
    /// <see cref="LastPlayedDay"/> is the last day something was finished; both only ever
    /// rise, so the merge is <c>max</c> on each and the length is derived, exactly as XP,
    /// credits and hearts are.
    /// </para>
    /// <para>
    /// The one thing that join gives up is worth naming. Two devices that have not synced
    /// can each hold a start date, and taking the later of them can under-report a streak
    /// by a day or two - device A knows about Monday and Tuesday, device B started
    /// counting on Wednesday, and the merged answer says Wednesday. That is the safe
    /// direction: it can only ever shorten a streak, never invent one, and it corrects
    /// itself the moment the devices agree. The alternative, taking the <em>earlier</em>
    /// start, is not a join at all - a stale device could resurrect a streak that was
    /// genuinely broken, and streak rewards escalate.
    /// </para>
    /// <para>
    /// <b>Why there is a third date.</b> A night's reward is collected by hand - see
    /// <see cref="TryCollect"/> - so something has to say which nights have been taken.
    /// <see cref="CollectedThroughDay"/> is the last one that has, which makes it the
    /// same shape as the other two and mergeable for the same reason: it only ever
    /// rises, so the join is <c>max</c> and a rung already paid cannot come back. The
    /// two obvious alternatives both fail invariant 11b. A count of collected rungs is
    /// hearts' old mistake wearing a different name, and a set of flags per run has to
    /// be cleared when a streak breaks, which is not monotonic and therefore not a join
    /// at all.
    /// </para>
    /// <para>
    /// <b>And why there is a fourth.</b> A streak can be <em>protected</em>: a gem purchase
    /// buys a window of days that do not have to be played, for the player who is away from
    /// the game rather than done with it. The whole entitlement is the day it was bought
    /// (<see cref="ShieldFromDay"/>), which is the fourth monotonic date in this file and
    /// the reason the promise is exact rather than approximate - there is one number, so
    /// playing inside the window writes nothing to it and cannot extend it.
    /// </para>
    /// <para>
    /// <b>The shield never advances the night count.</b> A protected day nobody played is
    /// <em>forgiven</em>, not credited: the streak survives it, and when the player comes
    /// back <see cref="StartDay"/> is pushed forward by however many days were forgiven, so
    /// a player on night twenty who vanishes for five protected days comes back to night
    /// twenty-one. Crediting them instead would sell six chests for a hundred and twenty
    /// gems, which is a currency printer wearing a retention feature's clothes.
    /// </para>
    /// <para>
    /// <b>Nights are taken in any order, and the night tapped is the night opened</b> (v39).
    /// The collected floor alone made only the oldest night takeable, so a tap on a newer one
    /// was redirected to the oldest - which a player read as tapping the bottom chest and
    /// watching the one above it open instead. What is stored now is the floor and the short
    /// list of days taken above it (<see cref="CollectedDays"/>, the <see cref="FloorSet"/>
    /// shape), with the floor climbing over the list as the gaps fill, so a player who takes
    /// nights oldest first writes exactly what every earlier build wrote. The server needed
    /// nothing: <c>advances</c> already accepts a night claimed below the last one paid when
    /// the calendar adds up exactly, and <see cref="ClaimDayAt"/> is what keeps it adding up
    /// across a shield.
    /// </para>
    /// </summary>
    public static class DailyStreak
    {
        static int _startDay;
        static int _lastPlayedDay;
        static int _collectedThrough;
        static int _shieldFrom;
        static int[] _collectedDays = Array.Empty<int>();
        static int _collectedPeak;

        /// <summary>The seed tag a chest night is rolled under, shared with the server. Contract (9c).</summary>
        public const string SeedTag = "streak";

        /// <summary>
        /// Raised when the streak moves, including when it is found broken on a read.
        /// Screens follow this rather than polling, because a day turns over while a
        /// screen is open exactly as often as it turns over while it is not.
        /// </summary>
        public static event Action Changed;

        /// <summary>
        /// Raised when today's run extended the streak, carrying the new length and the rung
        /// that has been put aside. The hook a screen uses to celebrate; nothing depends on
        /// it, so a missed one costs a flourish and never a reward.
        /// </summary>
        public static event Action<int, StreakRung> Advanced;

        /// <summary>
        /// Raised when a night's reward was collected, carrying the night's length along
        /// the ladder and what it paid. For the page that is drawing the tile at the time.
        /// </summary>
        public static event Action<int, List<ChestDrop>> Collected;

        static StreakTable Table => ProgressionRules.Table.Streak;

        static int Today => DailyRules.DayKeyFor(GameClock.NowUnix());

        static string PlayerKey => RewardSeed.PlayerKey;

        // ------------------------------------------------------------- reading
        /// <summary>The day the current run of days began. 0 when there has never been one.</summary>
        public static int StartDay => _startDay;

        /// <summary>The last day a run was finished. 0 when the player has never finished one.</summary>
        public static int LastPlayedDay => _lastPlayedDay;

        /// <summary>
        /// How long the streak is right now, or 0 when there is not one.
        ///
        /// Derived against today on every read rather than reset by a timer, for the reason
        /// <c>DailyChests.Sync</c> gives: a timer that has to fire at midnight has to
        /// survive a backgrounded app, a suspended process and a device asleep in a drawer,
        /// in every timezone. A comparison cannot be forgotten by a caller and cannot
        /// arrive late.
        /// </summary>
        public static int Days => LengthOf(_startDay, _lastPlayedDay, Today, _shieldFrom, ShieldDays);

        /// <summary>
        /// Whether a streak that last saw a run on <paramref name="lastPlayedDay"/> is still
        /// alive on <paramref name="today"/>.
        ///
        /// <para>
        /// <b>Yesterday still counts.</b> A streak breaks only once a whole day has passed
        /// with nothing finished in it - otherwise the flame would go out at midnight in
        /// front of a player who is mid-session, and the one thing a streak must never do is
        /// punish somebody who is playing right now.
        /// </para>
        /// <para>
        /// <b>And a shielded day does not count against it.</b> The days that would break the
        /// streak are the ones strictly between the last one played and today; the shield
        /// forgives them, so the streak survives exactly when every one of them is covered.
        /// Both ranges are contiguous, so testing the two ends tests all of it.
        /// </para>
        /// </summary>
        public static bool Survives(int lastPlayedDay, int today, int shieldFrom, int shieldDays)
        {
            if (lastPlayedDay >= today - 1) return true;

            return ShieldCovers(shieldFrom, lastPlayedDay + 1, shieldDays)
                && ShieldCovers(shieldFrom, today - 1, shieldDays);
        }

        /// <summary>
        /// How many of the unplayed days between two dates a shield covered.
        ///
        /// Pure interval arithmetic, and it is what <see cref="Advance"/> pushes
        /// <see cref="StartDay"/> forward by: a forgiven day keeps the streak and buys no
        /// night, so the run's start has to slide with it or the length would count days the
        /// player never played.
        /// </summary>
        public static int ForgivenBetween(int lastPlayedDay, int today, int shieldFrom, int shieldDays)
        {
            int lo = lastPlayedDay + 1;
            int hi = today - 1;
            if (hi < lo || shieldFrom <= 0 || shieldDays < 1) return 0;

            int slo = lo > shieldFrom ? lo : shieldFrom;
            int shi = hi < shieldFrom + shieldDays - 1 ? hi : shieldFrom + shieldDays - 1;

            return shi < slo ? 0 : shi - slo + 1;
        }

        /// <summary>
        /// How long a streak is, given the stored dates and what day it is now.
        ///
        /// Pure, and takes <paramref name="today"/> rather than reading a clock, for the
        /// same reason every method on <see cref="Persistence.Hearts"/> takes <c>now</c>:
        /// it is what lets the whole rule be exercised across midnights, gaps and merged
        /// files without waiting a day, and it keeps the question of <em>whose</em> clock
        /// it is out of the rule entirely.
        /// </summary>
        public static int LengthOf(int startDay, int lastPlayedDay, int today,
                                   int shieldFrom, int shieldDays)
        {
            if (startDay <= 0 || lastPlayedDay <= 0) return 0;
            if (!Survives(lastPlayedDay, today, shieldFrom, shieldDays)) return 0;

            int length = lastPlayedDay - startDay + 1;
            return length < 1 ? 0 : length;
        }

        /// <summary>
        /// What the dates become when a run is finished on <paramref name="today"/>.
        ///
        /// <para>
        /// Pure, for the reason <see cref="LengthOf"/> is, and separate from
        /// <see cref="Record"/> because this is the part with the rule in it: a run yesterday
        /// continues the streak, a run after a gap the shield covered continues it too,
        /// anything older starts a new one, and a second run today changes nothing at all.
        /// Both returned values are greater than or equal to the ones passed in, which is the
        /// property the merge depends on - see <see cref="Join"/>.
        /// </para>
        /// <para>
        /// <paramref name="forgiven"/> is how much of the gap the shield covered, and it is
        /// added to the start rather than to the length: the length is a subtraction, so
        /// sliding the start is the only way to say "those days kept the streak and bought no
        /// night" without storing a count of them.
        /// </para>
        /// </summary>
        public static void Advance(int startDay, int lastPlayedDay, int today,
                                   int shieldFrom, int shieldDays,
                                   out int nextStart, out int nextLast, out int forgiven)
        {
            nextStart = startDay;
            nextLast = lastPlayedDay;
            forgiven = 0;

            if (today <= 0 || lastPlayedDay >= today) return;

            bool continues = startDay > 0 && Survives(lastPlayedDay, today, shieldFrom, shieldDays);

            if (!continues)
            {
                nextStart = today;
                nextLast = today;
                return;
            }

            forgiven = ForgivenBetween(lastPlayedDay, today, shieldFrom, shieldDays);

            nextStart = startDay + forgiven;
            if (nextStart > today) nextStart = today;
            nextLast = today;
        }

        /// <summary>True when a run has already been finished today.</summary>
        public static bool PlayedToday => _lastPlayedDay >= Today;

        /// <summary>
        /// True when a streak is being held, has not been extended today, <em>and</em> doing
        /// nothing today would lose it - the one state worth putting in front of a player,
        /// because it is the only one where doing nothing costs them something.
        ///
        /// <para>
        /// <b>Asked as "would it survive tomorrow", never as "is a shield running".</b> A
        /// protected streak is not at risk, which is the whole of what was paid for - the
        /// page must not spend the window it sold telling the player to hurry - but the
        /// shield's <em>last</em> day is not urgent either: yesterday always counts, so a
        /// window ending tonight still leaves tomorrow to play. Reading the shield directly
        /// put the clock up a day early with the row beside it still reporting a day left,
        /// which is the page contradicting itself on the one state it was paid to handle.
        /// </para>
        /// </summary>
        public static bool AtRisk
            => Days > 0 && !PlayedToday
            && !Survives(_lastPlayedDay, Today + 1, _shieldFrom, ShieldDays);

        /// <summary>
        /// What the next rung pays - today's run when the streak has not yet been extended,
        /// tomorrow's when it has.
        ///
        /// One expression covers all three states because <see cref="Days"/> already reads
        /// 0 for a broken streak, so "one more than what is held" is the next rung whether
        /// the player is continuing a week or starting over.
        /// </summary>
        public static StreakRung NextReward => Table.Rung(Days + 1);

        /// <summary>What today's run put aside, or nothing when today has not been played.</summary>
        public static StreakRung TodaysReward
            => PlayedToday ? Table.Rung(Days) : StreakRung.None;

        /// <summary>The ladder, for a panel that wants to print it.</summary>
        public static StreakTable Ladder => Table;

        // ------------------------------------------------------------- the shield
        /// <summary>The day a shield was bought, or 0. See <c>StreakStateDto.shieldFromDay</c>.</summary>
        public static int ShieldFromDay => _shieldFrom;

        /// <summary>How many days one shield covers, counting the day it was bought.</summary>
        public static int ShieldDays => Table == null ? StreakRules.DefaultShieldDays : Table.ShieldDays;

        /// <summary>What a shield costs in gems. Zero when this build sells none.</summary>
        public static int ShieldGems => Table == null ? 0 : Table.ShieldGems;

        /// <summary>Whether a shield may be offered at all on this content.</summary>
        public static bool SellsShield => Table != null && Table.SellsShield;

        /// <summary>
        /// Whether a shield bought on <paramref name="shieldFrom"/> covers
        /// <paramref name="day"/>.
        ///
        /// Inclusive of the day it was bought, which is what makes "seven days" seven and not
        /// eight, and what lets a player whose streak is already at risk buy one and be safe
        /// the same evening.
        /// </summary>
        public static bool ShieldCovers(int shieldFrom, int day, int shieldDays)
            => shieldFrom > 0 && shieldDays > 0
            && day >= shieldFrom && day < shieldFrom + shieldDays;

        /// <summary>True when a shield is running right now.</summary>
        public static bool IsProtected => ShieldCovers(_shieldFrom, Today, ShieldDays);

        /// <summary>
        /// How many days of protection are left, counting today. 0 when none is running.
        ///
        /// Days rather than a clock, because the thing being protected turns over on a
        /// calendar day - a countdown to the hour would be a second, more precise-looking
        /// answer to a question the rule does not ask that precisely.
        /// </summary>
        public static int ShieldDaysLeft
        {
            get
            {
                if (!IsProtected) return 0;
                return _shieldFrom + ShieldDays - Today;
            }
        }

        /// <summary>The last day a running shield covers, or 0 when none is.</summary>
        public static int ShieldThroughDay => IsProtected ? _shieldFrom + ShieldDays - 1 : 0;

        /// <summary>What a shield purchase can answer.</summary>
        public enum ShieldBuy
        {
            Bought,

            /// <summary>One is already running. Not a failure; nothing is charged.</summary>
            Held,

            /// <summary>This build sells none - the content authored no price.</summary>
            NotSold,

            /// <summary>There is no streak to protect.</summary>
            NoStreak,

            /// <summary>Not enough gems.</summary>
            TooPoor,
        }

        /// <summary>
        /// Buys a window of days the streak survives without being played.
        ///
        /// <para>
        /// <b>The debit goes first and the date is only written if it succeeded</b>, which is
        /// <c>SeasonLedger.TryBuyPass</c>'s ordering and its argument: a process killed
        /// between the two leaves a player who paid and did not receive, which the spend log
        /// can see and support can put right - where the other order leaves protection nobody
        /// paid for, which is indistinguishable from a forgery and therefore invisible.
        /// </para>
        /// <para>
        /// <b>A running shield is never sold a second one.</b> The entitlement is one date, so
        /// buying again would move it and silently extend the window - which is exactly the
        /// thing this was asked not to do - and charging for a window already held is worse.
        /// It becomes buyable again the day the last one lapses.
        /// </para>
        /// <para>
        /// <b>And there has to be a streak.</b> Selling protection for nothing is a hundred
        /// and twenty gems for a date nobody will ever read: <see cref="Days"/> is zero, the
        /// next run starts a fresh run whatever this says, and the window would quietly expire
        /// unused.
        /// </para>
        /// <para>
        /// The spend id is <b>derived</b> - <c>shield:{day}</c> - which is the second derived
        /// spend id in this game and is here for the first one's reason read sideways: two
        /// devices that both buy on the same day offline write byte-identical entries, the
        /// union keeps one, and the player is charged once. It is not a permission the server
        /// grants; see <c>StreakStateDto.shieldFromDay</c> for why it does not need to be.
        /// </para>
        /// </summary>
        public static ShieldBuy TryBuyShield()
        {
            var table = Table;
            if (table == null || !table.SellsShield) return ShieldBuy.NotSold;
            if (Days <= 0) return ShieldBuy.NoStreak;
            if (IsProtected) return ShieldBuy.Held;

            int today = Today;

            if (!PlayerProgression.TrySpend(Currency.Gems, table.ShieldGems,
                                            SpendEntry.StreakShieldReason,
                                            SpendEntry.StreakShieldId(today)))
                return ShieldBuy.TooPoor;

            _shieldFrom = today;

            SaveService.Save();
            Raise();

            Telemetry.Track("streak_shield_bought",
                            "day", today,
                            "gems", table.ShieldGems,
                            "days", table.ShieldDays,
                            "length", Days);

            return ShieldBuy.Bought;
        }

        // ------------------------------------------------------------ collecting
        /// <summary>The last night whose reward has been handed over. 0 before any.</summary>
        public static int CollectedThroughDay => _collectedThrough;

        /// <summary>
        /// Which calendar day the <paramref name="rung"/>th night of a run beginning on
        /// <paramref name="startDay"/> was, or 0 when there is no run.
        ///
        /// The join between the ladder - which counts from one - and the dates, which is
        /// what everything else keys on. Pure, and takes the start rather than reading it,
        /// for the reason <see cref="LengthOf"/> is: it is what lets the collection rules
        /// be exercised over merged files and broken streaks without a save or a clock.
        /// </summary>
        public static int DayOfRung(int startDay, int rung)
            => rung < 1 || startDay <= 0 ? 0 : startDay + rung - 1;

        /// <summary>Which calendar day the <paramref name="rung"/>th night of this run was.</summary>
        public static int DayOf(int rung) => DayOfRung(_startDay, rung);

        /// <summary>
        /// True when this night's reward has been taken, given a collected floor.
        ///
        /// A night that does not exist reads as collected too, which is deliberate: it
        /// makes "collected" the answer to "is there anything here for me", and every
        /// caller wants that rather than a tri-state it would have to re-combine.
        /// </summary>
        public static bool CollectedAt(int startDay, int collectedThrough, int rung)
            => CollectedAt(startDay, collectedThrough, null, rung);

        /// <summary>The same, with the days taken above the floor out of order.</summary>
        public static bool CollectedAt(int startDay, int collectedThrough, int[] collectedDays, int rung)
        {
            int day = DayOfRung(startDay, rung);
            return day <= 0 || FloorSet.Holds(collectedThrough, collectedDays, day);
        }

        /// <summary>
        /// True when this night has been reached, pays something, and has not been taken.
        ///
        /// A rung that pays nothing is never waiting, so it can never sit on the board asking
        /// to be tapped for nothing. It is swept along silently when a later night is taken.
        /// </summary>
        public static bool WaitingAt(int startDay, int lastPlayedDay, int collectedThrough,
                                     int today, int rung, StreakTable ladder,
                                     int shieldFrom, int shieldDays)
            => WaitingAt(startDay, lastPlayedDay, collectedThrough, null, today, rung, ladder,
                         shieldFrom, shieldDays);

        /// <summary>The same, with the days taken above the floor out of order.</summary>
        public static bool WaitingAt(int startDay, int lastPlayedDay, int collectedThrough,
                                     int[] collectedDays, int today, int rung, StreakTable ladder,
                                     int shieldFrom, int shieldDays)
        {
            if (ladder == null) return false;
            if (rung < 1 || rung > LengthOf(startDay, lastPlayedDay, today, shieldFrom, shieldDays))
                return false;
            if (CollectedAt(startDay, collectedThrough, collectedDays, rung)) return false;

            return ladder.Rung(rung).IsValid;
        }

        /// <summary>
        /// The earliest night still waiting, or 0 when nothing is.
        ///
        /// <para>
        /// What the board pages to and opens on. A streak runs on past the end of the ladder,
        /// so the board shows one lap of it at a time, and the lap it shows has to be the one holding
        /// the oldest thing the player has not taken. Showing the <em>current</em> lap instead
        /// is how night seven's reward gets stranded off the board the moment night eight
        /// arrives.
        /// </para>
        /// </summary>
        public static int FirstPendingAt(int startDay, int lastPlayedDay, int collectedThrough,
                                         int today, StreakTable ladder,
                                         int shieldFrom, int shieldDays)
            => FirstPendingAt(startDay, lastPlayedDay, collectedThrough, null, today, ladder,
                              shieldFrom, shieldDays);

        /// <summary>The same, with the days taken above the floor out of order.</summary>
        public static int FirstPendingAt(int startDay, int lastPlayedDay, int collectedThrough,
                                         int[] collectedDays, int today, StreakTable ladder,
                                         int shieldFrom, int shieldDays)
        {
            int days = LengthOf(startDay, lastPlayedDay, today, shieldFrom, shieldDays);

            for (int rung = 1; rung <= days; rung++)
                if (WaitingAt(startDay, lastPlayedDay, collectedThrough, collectedDays, today, rung,
                              ladder, shieldFrom, shieldDays))
                    return rung;

            return 0;
        }

        /// <summary>
        /// True when tapping this night would pay something out.
        ///
        /// <para>
        /// <b>Any waiting night, in any order, and exactly that night.</b> Each night is its own
        /// ceremony, so taking one never takes another with it - the floor used to be the whole
        /// record, which made only the oldest takeable and turned a tap on a newer night into the
        /// oldest night opening instead. A player holding three waiting nights taps three times,
        /// in whatever order they like, and opens three chests (invariants 45, 47g).
        /// </para>
        /// <para>
        /// The oldest is always takeable, because it moves the floor rather than the list. Any
        /// other is takeable while the list has room (<see cref="FloorSet.MaxTaken"/>), which only
        /// a backlog of more than sixty-four nights taken newest first can exhaust - and then the
        /// answer is oldest first again, which is the direction a bound may fail in.
        /// </para>
        /// </summary>
        public static bool CollectableAt(int startDay, int lastPlayedDay, int collectedThrough,
                                         int today, int rung, StreakTable ladder,
                                         int shieldFrom, int shieldDays)
            => CollectableAt(startDay, lastPlayedDay, collectedThrough, null, today, rung, ladder,
                             shieldFrom, shieldDays);

        /// <summary>The same, with the days taken above the floor out of order.</summary>
        public static bool CollectableAt(int startDay, int lastPlayedDay, int collectedThrough,
                                         int[] collectedDays, int today, int rung, StreakTable ladder,
                                         int shieldFrom, int shieldDays)
        {
            if (!WaitingAt(startDay, lastPlayedDay, collectedThrough, collectedDays, today, rung,
                           ladder, shieldFrom, shieldDays))
                return false;

            return FloorSet.HasRoom(collectedDays)
                || FirstPendingAt(startDay, lastPlayedDay, collectedThrough, collectedDays, today,
                                  ladder, shieldFrom, shieldDays) == rung;
        }

        /// <summary>How many nights are waiting to be collected, over a given ladder.</summary>
        public static int PendingAt(int startDay, int lastPlayedDay, int collectedThrough,
                                    int today, StreakTable ladder, int shieldFrom, int shieldDays)
            => PendingAt(startDay, lastPlayedDay, collectedThrough, null, today, ladder,
                         shieldFrom, shieldDays);

        /// <summary>The same, with the days taken above the floor out of order.</summary>
        public static int PendingAt(int startDay, int lastPlayedDay, int collectedThrough,
                                    int[] collectedDays, int today, StreakTable ladder,
                                    int shieldFrom, int shieldDays)
        {
            int days = LengthOf(startDay, lastPlayedDay, today, shieldFrom, shieldDays);
            int count = 0;

            for (int rung = 1; rung <= days; rung++)
                if (WaitingAt(startDay, lastPlayedDay, collectedThrough, collectedDays, today, rung,
                              ladder, shieldFrom, shieldDays))
                    count++;

            return count;
        }

        /// <summary>
        /// The calendar day a night is <b>claimed under</b>: the day in its claim id and its
        /// chest's seed.
        ///
        /// <para>
        /// The night's own day (<see cref="DayOfRung"/>) in every case but one. The server
        /// remembers the day and night it last paid and accepts a night below that one only when
        /// the calendar adds up exactly (<c>advances</c>: <c>night == paidNight + elapsed</c>).
        /// A shield's forgiven days slide the run's start and every night's day with it, so a
        /// night left waiting under a taken one would, after a slide, name a day the server's
        /// arithmetic cannot reach - and a refused claim is dropped with the coins it showed
        /// (45d).
        /// </para>
        /// <para>
        /// So a night below the highest taken one is dated in the calendar that one was paid in:
        /// the highest taken day is in today's dating, <paramref name="peakDay"/> is the latest
        /// day anything was really claimed under (which is that same night's claim day), and the
        /// difference is exactly how far the run has slid since. With no slide - every streak
        /// nobody protected - it is the night's own day, so the ids are the ones every earlier
        /// build wrote.
        /// </para>
        /// </summary>
        public static int ClaimDayAt(int startDay, int highestTaken, int peakDay, int rung)
        {
            int day = DayOfRung(startDay, rung);
            if (day <= 0 || highestTaken <= 0 || peakDay <= 0 || day >= highestTaken) return day;

            int slide = highestTaken - peakDay;
            return slide > 0 ? day - slide : day;
        }

        /// <summary>The highest day in a canonical list, or 0.</summary>
        public static int TopOf(int[] collectedDays)
            => collectedDays == null || collectedDays.Length == 0 ? 0 : collectedDays[collectedDays.Length - 1];

        /// <summary>Which lap of the ladder a night falls on, counting from one.</summary>
        public static int CycleOf(int night, int cycleLength)
        {
            int length = cycleLength < 1 ? 1 : cycleLength;
            return night < 1 ? 1 : (night - 1) / length + 1;
        }

        /// <summary>
        /// The first night of the lap that contains <paramref name="night"/>.
        ///
        /// The board draws <c>cycleLength</c> nights starting here, and every tile is
        /// labelled with its <em>absolute</em> night - night eight reads "night 8", not
        /// "night 1 of week 2". A board that restarted its numbering while the flame above
        /// it counted on would read as the streak having been reset, which is the one thing
        /// this screen must never appear to do.
        /// </summary>
        public static int CycleStart(int night, int cycleLength)
        {
            int length = cycleLength < 1 ? 1 : cycleLength;
            return (CycleOf(night, length) - 1) * length + 1;
        }

        /// <summary>How many nights one lap of the ladder is.</summary>
        public static int CycleLength
        {
            get
            {
                var table = Table;
                return table == null || table.Length < 1 ? 1 : table.Length;
            }
        }

        /// <summary>
        /// The first night of the lap the board should be showing.
        ///
        /// The lap holding the oldest uncollected night, or the one the streak is on when
        /// there is nothing waiting.
        /// </summary>
        public static int BoardFirstNight
        {
            get
            {
                int pending = FirstPendingAt(_startDay, _lastPlayedDay, _collectedThrough,
                                             _collectedDays, Today, Table, _shieldFrom, ShieldDays);
                int anchor = pending > 0 ? pending : Days;
                return CycleStart(anchor < 1 ? 1 : anchor, CycleLength);
            }
        }

        /// <summary>Which lap of the ladder the streak is on now. One for the first.</summary>
        public static int Cycle => CycleOf(Days < 1 ? 1 : Days, CycleLength);

        /// <summary>
        /// The collected floor a run seeds, or the one already held.
        ///
        /// <para>
        /// Pure counterpart of the rule in <see cref="Record"/>, and it does three jobs.
        /// Nights from a lapsed streak stop being offered; a live file's floor is never zero,
        /// which is what makes zero mean "written before rewards were collected by hand"; and
        /// - new with the shield - a run that continued across forgiven days carries its floor
        /// forward by the same amount its start moved, because the floor is a <em>day</em> and
        /// every night's day has just slid.
        /// </para>
        /// <para>
        /// Without that last clause a protected player comes back to nights they have already
        /// been paid for sitting on the board waiting to be paid again - the floor would still
        /// name the old calendar day while night one now names a later one.
        /// </para>
        /// </summary>
        public static int SeedCollected(int collectedThrough, int today, bool continues, int forgiven)
        {
            if (!continues) return today - 1 <= collectedThrough ? collectedThrough : today - 1;
            if (collectedThrough <= 0 || forgiven <= 0) return collectedThrough;

            // The ceiling is belt and braces rather than arithmetic: a floor is never past
            // the last day played and the forgiven days are all strictly before today, so a
            // reachable pair cannot exceed it. What is *not* belt and braces is the second
            // clamp - without it a floor already at or past yesterday would be pulled back by
            // the ceiling, which is the one thing this field may never do, since every merge
            // in this file rests on it only ever rising (invariant 11b).
            int moved = collectedThrough + forgiven;
            int ceiling = today - 1;
            if (moved > ceiling) moved = ceiling;

            return moved < collectedThrough ? collectedThrough : moved;
        }

        /// <summary>
        /// What a floor read off disk becomes. Pure counterpart of <see cref="LoadFrom"/>.
        ///
        /// Zero is a file written before rungs were collected by hand, and everything it
        /// had earned it had also been paid - so the floor is the last day it played.
        /// Anything past that day is a file that has been edited, and is pulled back.
        /// </summary>
        public static int RepairCollected(int collectedThrough, int lastPlayedDay)
        {
            int floor = collectedThrough <= 0 ? lastPlayedDay : collectedThrough;
            return floor > lastPlayedDay ? lastPlayedDay : floor;
        }

        /// <summary>True when the streak has already reached this night.</summary>
        public static bool IsEarned(int rung) => rung >= 1 && rung <= Days;

        /// <summary>The days taken above the collected floor, out of order. Canonical.</summary>
        public static IReadOnlyList<int> CollectedDays => _collectedDays;

        /// <summary>True when this night's reward has been taken.</summary>
        public static bool IsCollected(int rung)
            => CollectedAt(_startDay, _collectedThrough, _collectedDays, rung);

        /// <summary>True when this night has been reached and not yet taken.</summary>
        public static bool IsWaiting(int rung)
            => WaitingAt(_startDay, _lastPlayedDay, _collectedThrough, _collectedDays, Today, rung,
                         Table, _shieldFrom, ShieldDays);

        /// <summary>True when tapping this night would pay something out.</summary>
        public static bool IsCollectable(int rung)
            => CollectableAt(_startDay, _lastPlayedDay, _collectedThrough, _collectedDays, Today,
                             rung, Table, _shieldFrom, ShieldDays);

        /// <summary>The earliest night waiting to be taken, or 0. What the board opens on.</summary>
        public static int FirstPending
            => FirstPendingAt(_startDay, _lastPlayedDay, _collectedThrough, _collectedDays, Today,
                              Table, _shieldFrom, ShieldDays);

        /// <summary>How many nights are waiting to be collected. What a badge counts.</summary>
        public static int Pending
            => PendingAt(_startDay, _lastPlayedDay, _collectedThrough, _collectedDays, Today, Table,
                         _shieldFrom, ShieldDays);

        /// <summary>The day this night is claimed under. See <see cref="ClaimDayAt"/>.</summary>
        static int ClaimDayOf(int rung)
            => ClaimDayAt(_startDay, TopOf(_collectedDays), _collectedPeak, rung);

        /// <summary>Whether anything is waiting, for a line that wants to mention it.</summary>
        public static bool AnyPending => Pending > 0;

        /// <summary>
        /// Whether a night may be claimed yet.
        ///
        /// The tasks' gate, for the tasks' reason, and it binds here only because a night can
        /// pay a chest: a chest is rolled from the account id so the server can recompute it,
        /// and before the first sign-in there is no account id to roll from. A currency night
        /// needs no such thing, which is why this is asked of the rung rather than of the
        /// feature - see <see cref="CanCollect"/>.
        /// </summary>
        public static bool CanClaimChests => RewardSeed.IsAdjudicable;

        /// <summary>True when the night waiting could actually be handed over right now.</summary>
        public static bool CanCollect(int rung)
        {
            if (!IsCollectable(rung)) return false;
            return !Table.Rung(rung).IsChest || CanClaimChests;
        }

        /// <summary>
        /// What a chest night holds, without claiming it. For the opening overlay and for a
        /// page that wants to show the odds; empty for a night that pays a figure.
        /// </summary>
        public static List<ChestDrop> Preview(int rung)
        {
            var night = Table.Rung(rung);
            if (!night.IsChest) return new List<ChestDrop>();
            return night.Tier.Chest.Roll(SeedFor(ClaimDayOf(rung), rung));
        }

        /// <summary>
        /// The seed a night's chest is rolled from: the player, this feature, and the
        /// calendar day and night that earned it. The subject layout is contract with the
        /// server's <c>subjectSeed</c>; see <see cref="ChestSeed"/>.
        /// </summary>
        public static ChestSeed SeedFor(int dayKey, int night)
            => ChestSeed.ForSubject(PlayerKey, SeedTag, Subject(dayKey, night));

        /// <summary>The subject half of the seed and of the claim id: <c>{day}:{night}</c>.</summary>
        public static string Subject(int dayKey, int night) => dayKey + ":" + night;

        /// <summary>
        /// Hands over one night and returns what it paid.
        ///
        /// <para>
        /// <b>One night, never a sweep.</b> Exactly the night asked for is recorded
        /// (<see cref="RecordCollected"/>), in any order - which is what lets a night open the
        /// same chest ceremony the tasks page and the season do, instead of granting or marking
        /// several chests behind one animation.
        /// </para>
        /// <para>
        /// Nights that pay nothing are stepped over silently as the floor climbs, because there
        /// is nothing to show for them.
        /// </para>
        /// <para>
        /// Two independent guards stop a night paying twice. The floor refuses a second
        /// attempt; and every currency award carries an id derived from the night's own
        /// calendar day, so even a save edited to lower the floor collides with an entry
        /// already in the ledger, and the server refuses it a third time on top.
        /// </para>
        /// </summary>
        public static bool TryCollect(int rung, out List<ChestDrop> drops)
        {
            drops = null;
            if (!IsCollectable(rung)) return false;

            var night = Table.Rung(rung);

            // Checked here as well as in the UI. A reward the server would recompute
            // differently must not be claimable through any path, and a guard that lives only
            // in a screen is a guard the next screen forgets.
            if (night.IsChest && !CanClaimChests) return false;

            int day = DayOf(rung);
            if (FloorSet.Holds(_collectedThrough, _collectedDays, day)) return false;

            // Dated before the record moves, because the date reads the record: a night below
            // the highest taken one is claimed in that one's calendar (ClaimDayAt).
            int through = ClaimDayOf(rung);

            // The record moves *before* the reward is handed over, and the ordering is
            // load-bearing rather than tidy. Applying a rung writes the save - an award has to
            // be durable the moment the player is shown it - so a process killed mid-payout
            // would otherwise come back with the night still waiting whose utilities had
            // already been banked. Currency survives that: the award carries a derived id and
            // the second attempt collides with the first. A banked drop carries nothing, so it
            // would simply be paid twice. Paying late is recoverable on the next tap; paying
            // twice is not recoverable at all.
            RecordCollected(day, through);

            var paid = night.IsChest
                ? night.Tier.Chest.Roll(SeedFor(through, rung))
                : new List<ChestDrop> { night.AsDrop() };

            Apply(paid, through, rung);

            // A night taken is a thing that happened, whatever the rung paid.
            TaskLedger.Note(TaskGoal.Streak);

            // The season grows on a claimed chest and nowhere else, so a chest night feeds it
            // exactly as a task's does - which is invariant 47's whole bargain: every future
            // source of chests feeds the season by naming a tier rather than by growing a
            // second rule. A currency night grows nothing, correctly.
            if (night.IsChest) Events.SeasonLedger.NoteChest(night.Tier);

            SaveService.Save();
            Raise();

            Telemetry.Track("streak_collected",
                            "rung", rung,
                            "day", through,
                            "chest", night.IsChest ? night.Tier.Id : string.Empty,
                            "reward", Describe(paid));

            try { Collected?.Invoke(rung, paid); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }

            drops = paid;
            return true;
        }

        /// <summary>
        /// Writes one night down as taken: into the list, then the floor climbs over every
        /// earned night above it that is in the list or pays nothing, stopping at the first one
        /// still owed. So a player taking nights oldest first never carries a list, and one
        /// taking them out of order carries a list that drains as the gaps fill.
        /// </summary>
        static void RecordCollected(int day, int claimDay)
        {
            Collect(_startDay, _lastPlayedDay, Days, _collectedThrough, _collectedDays, day, Table,
                    out _collectedThrough, out _collectedDays);
            if (claimDay > _collectedPeak) _collectedPeak = claimDay;
        }

        /// <summary>
        /// What the collected record becomes when the night on <paramref name="day"/> is taken.
        /// Pure counterpart of <see cref="RecordCollected"/>, for <see cref="LengthOf"/>'s reason.
        ///
        /// <para>
        /// The day goes in the list, then the floor climbs night by night over every earned
        /// night that is in the list or pays nothing, and stops at the first still owed. Both
        /// outputs are at least what went in - the floor rises and the set it means only grows -
        /// which is the property every merge in this file rests on (11b).
        /// </para>
        /// </summary>
        public static void Collect(int startDay, int lastPlayedDay, int days, int floor, int[] taken,
                                   int day, StreakTable ladder, out int nextFloor, out int[] nextTaken)
        {
            var held = FloorSet.With(floor, taken, day, lastPlayedDay);

            int from = floor - startDay + 2;
            for (int rung = from < 1 ? 1 : from; rung <= days; rung++)
            {
                int at = DayOfRung(startDay, rung);
                bool paysNothing = ladder != null && !ladder.Rung(rung).IsValid;
                if (!FloorSet.Holds(floor, held, at) && !paysNothing) break;
                floor = at;
            }

            nextFloor = floor;
            nextTaken = FloorSet.Normal(floor, held, lastPlayedDay);
        }

        static string Describe(List<ChestDrop> drops)
        {
            var parts = new string[drops.Count];
            for (int i = 0; i < drops.Count; i++) parts[i] = drops[i].ToString();
            return string.Join(",", parts);
        }

        /// <summary>Seconds until the streak would be lost, or 0 when today is already safe.</summary>
        public static long SecondsUntilLost
        {
            get
            {
                if (!AtRisk) return 0;
                return DailyRules.SecondsUntilReset(GameClock.NowUnix());
            }
        }

        // ------------------------------------------------------------- writing
        /// <summary>
        /// Records that a run resolved today, won or lost.
        ///
        /// <para>
        /// The same event <c>TaskLedger.RecordRun</c> counts, and deliberately the same
        /// bar: losing keeps a streak alive. A streak that only counts wins punishes the
        /// player on the day they were struggling, which is the day they most needed a
        /// reason to come back tomorrow - and it would make the hardest glade in a chapter
        /// the place streaks go to die.
        /// </para>
        /// <para>
        /// <b>The reward is set aside, not paid.</b> It waits on the streak page until the
        /// player taps it - see <see cref="TryCollect"/>. What that buys is the moment: a
        /// reward that lands silently while a defeat screen is animating is a number the
        /// player never sees arrive, and a number nobody watches arrive is not a reward, it
        /// is an accounting entry. It also gives the page a reason to be opened, which is
        /// the whole point of a streak.
        /// </para>
        /// </summary>
        public static void Record()
        {
            int today = Today;
            int shieldDays = ShieldDays;

            Advance(_startDay, _lastPlayedDay, today, _shieldFrom, shieldDays,
                    out int nextStart, out int nextLast, out int forgiven);

            // Nothing moved: the day has already been counted, which is what makes this
            // idempotent within a day and lets the second run of an evening cost nothing.
            if (nextStart == _startDay && nextLast == _lastPlayedDay) return;

            // A restart is the only thing that writes today into the start, and a continued
            // run can never land on it: the start moves forward by the forgiven days, which
            // are all strictly before today, so `startDay + forgiven` is at most yesterday.
            // That is what lets one comparison tell the two apart.
            bool continues = nextStart != today;

            _startDay = nextStart;
            _lastPlayedDay = nextLast;

            // A run that starts a new streak seeds the collected floor to the day before it,
            // and a run that continued across forgiven days carries its floor forward by the
            // same amount its start moved. See SeedCollected.
            int floorWas = _collectedThrough;
            _collectedThrough = SeedCollected(_collectedThrough, today, continues, forgiven);

            // The days taken out of order slide with the floor, for the floor's reason: they
            // name nights, and every night's day has just moved by the same amount. A restart
            // seeds the floor past every old day, which empties the list by itself.
            _collectedDays = FloorSet.Normal(_collectedThrough,
                                             Slid(_collectedDays, continues ? _collectedThrough - floorWas : 0),
                                             _lastPlayedDay);

            int length = LengthOf(_startDay, _lastPlayedDay, today, _shieldFrom, shieldDays);
            var reward = Table.Rung(length);

            SaveService.Save();
            Raise();

            Telemetry.Track("streak_advanced",
                            "day", today,
                            "length", length,
                            "continued", continues,
                            "forgiven", forgiven,
                            "reward", reward.ToString());

            try { Advanced?.Invoke(length, reward); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        /// <summary>
        /// Hands over one night's contents. Reached only from <see cref="TryCollect"/>.
        ///
        /// <para>
        /// The same split <c>TaskLedger.Apply</c> makes, for the same reason. Hearts, boosts
        /// and utilities are banked here and now: a heart clamps at its ceiling and a boost
        /// expires, so trusting the client with them costs at most a few extra runs today.
        /// Currency is queued as an identified claim for the server to adjudicate, because
        /// currency is the thing real money buys and therefore the thing an attacker forges -
        /// the client never raises <c>grantedBaseline</c> itself. Invariant 10a.
        /// </para>
        /// <para>
        /// The award lands in the ledger immediately either way, so a night collected on a
        /// plane is spendable on that plane. What the server later decides can only revise
        /// it downward, and it does that by replacing a baseline rather than by taking
        /// anything back - see <c>CurrencyLedger.BalanceFrom</c>.
        /// </para>
        /// </summary>
        static void Apply(List<ChestDrop> drops, int dayKey, int night)
        {
            long now = GameClock.NowUnix();

            for (int i = 0; i < drops.Count; i++)
            {
                var drop = drops[i];
                if (!drop.IsValid) continue;
                if (BankedDrop.Apply(drop)) continue;
                if (!drop.IsCurrency) continue;

                string currency = ChestDropKinds.CurrencyOf(drop.Kind);
                PlayerProgression.Award(
                    currency, drop.Amount,
                    GrantEntry.StreakNightId(dayKey, night, currency),
                    GrantEntry.StreakNightReason, now);
            }
        }

        /// <summary>A copy of <paramref name="days"/> moved forward by <paramref name="by"/>.</summary>
        static int[] Slid(int[] days, int by)
        {
            if (days == null || days.Length == 0 || by <= 0) return days;
            var moved = new int[days.Length];
            for (int i = 0; i < days.Length; i++) moved[i] = days[i] + by;
            return moved;
        }

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        // --------------------------------------------------- file bridge (internal)
        internal static void LoadFrom(SaveFileDto dto)
        {
            var streak = dto?.streak;

            _startDay = streak == null || streak.startDay < 0 ? 0 : streak.startDay;
            _lastPlayedDay = streak == null || streak.lastPlayedDay < 0 ? 0 : streak.lastPlayedDay;
            _collectedThrough = streak == null || streak.collectedThroughDay < 0
                              ? 0 : streak.collectedThroughDay;
            _shieldFrom = streak == null || streak.shieldFromDay < 0 ? 0 : streak.shieldFromDay;

            // A file written by hand, or one merged from a device whose clock disagreed,
            // could name a start after the last day played. Repaired on read rather than
            // trusted, because the length is a subtraction and a negative one would show
            // the player a streak that counts backwards.
            if (_startDay > _lastPlayedDay) _startDay = _lastPlayedDay;

            // Pre-v10: rungs were applied at the end of a run, so everything this file has
            // earned it has also been paid. Left at zero, every night of a live streak
            // would light up as collectable on the first launch of this build and pay a
            // second time. A live v10 file cannot say zero here - see StreakStateDto.
            _collectedThrough = RepairCollected(_collectedThrough, _lastPlayedDay);

            // v39. Canonical against the repaired floor and the last day played, which is the
            // ceiling every writer keeps - so a load writes back exactly what it read (11f).
            _collectedDays = FloorSet.Normal(_collectedThrough, streak?.collectedDays, _lastPlayedDay);
            _collectedPeak = streak == null || streak.collectedPeakDay < 0 ? 0 : streak.collectedPeakDay;

            Raise();
        }

        internal static void WriteInto(SaveFileDto dto)
        {
            dto.streak = new StreakStateDto
            {
                startDay = _startDay,
                lastPlayedDay = _lastPlayedDay,
                collectedThroughDay = _collectedThrough,
                shieldFromDay = _shieldFrom,
                collectedDays = (int[])_collectedDays.Clone(),
                collectedPeakDay = _collectedPeak,
            };
        }

        /// <summary>
        /// Joins two devices' streaks: <c>max</c> on every date and nothing else.
        ///
        /// <para>
        /// Every field is a counter of something that happened rather than a balance, so the
        /// larger is always the one that knows more - a later last-played day has seen a
        /// session the other missed, a later start day has seen a break or a forgiven gap the
        /// other missed, and a later shield date has seen a purchase the other missed. That
        /// makes this idempotent, commutative and associative like every other merge in this
        /// file, with no opinion needed about which device is "right".
        /// </para>
        /// <para>
        /// See the type summary for what taking the later start gives up and why the
        /// alternative is worse.
        /// </para>
        /// </summary>
        internal static StreakStateDto Join(StreakStateDto mine, StreakStateDto other)
        {
            if (mine == null && other == null) return new StreakStateDto();
            if (mine == null) return Copy(other);
            if (other == null) return Copy(mine);

            return new StreakStateDto
            {
                startDay = Math.Max(mine.startDay, other.startDay),
                lastPlayedDay = Math.Max(mine.lastPlayedDay, other.lastPlayedDay),

                // Larger wins here too, and here that means the device that has paid more
                // out. It can cost a player a rung they had not collected on either device
                // - the same direction the start date already errs in, and for the same
                // reason: the alternative pays a night twice, and two devices claiming the
                // same chest is the failure this whole file is shaped to avoid.
                collectedThroughDay = Math.Max(mine.collectedThroughDay, other.collectedThroughDay),

                // And the shield. A purchase cannot be undone, so the later date is the one
                // that has heard about it; there is no reading under which the earlier one
                // knows more.
                shieldFromDay = Math.Max(mine.shieldFromDay, other.shieldFromDay),

                // The nights taken out of order: union above the joined floor, under the joined
                // last day played - the same canonical form the loader writes, so a sync agrees
                // with what the device then holds (11f). The latest claim day by `max`.
                //
                // What this join gives up is the start date's own trade: a device that slid its
                // run across a shield and one that did not can each hold a list in a different
                // dating, and the union may then mark a night taken that neither took. That can
                // only ever withhold a night, never pay one twice, and it is gone the moment the
                // two agree.
                collectedDays = FloorSet.Union(
                    Math.Max(mine.collectedThroughDay, other.collectedThroughDay),
                    mine.collectedDays, other.collectedDays,
                    Math.Max(mine.lastPlayedDay, other.lastPlayedDay)),
                collectedPeakDay = Math.Max(mine.collectedPeakDay, other.collectedPeakDay),
            };
        }

        static StreakStateDto Copy(StreakStateDto s)
            => new StreakStateDto
            {
                startDay = s.startDay,
                lastPlayedDay = s.lastPlayedDay,
                collectedThroughDay = s.collectedThroughDay,
                shieldFromDay = s.shieldFromDay,
                collectedDays = FloorSet.Normal(s.collectedThroughDay, s.collectedDays, s.lastPlayedDay),
                collectedPeakDay = s.collectedPeakDay,
            };

        /// <summary>Forgets the streak. Dev only, and used by the wipe.</summary>
        internal static void Reset()
        {
            _startDay = 0;
            _lastPlayedDay = 0;
            _collectedThrough = 0;
            _shieldFrom = 0;
            _collectedDays = Array.Empty<int>();
            _collectedPeak = 0;
        }
    }
}
