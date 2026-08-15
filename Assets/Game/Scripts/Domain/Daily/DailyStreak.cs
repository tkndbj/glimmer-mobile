using System;
using GlimmerGrove.Analytics;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

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
    /// <b>Why it is stored as two dates and not as a count.</b> Invariant 11b. A count is
    /// not mergeable: a device holding 6 and one holding 1 are equally consistent with
    /// "one of them is behind" and "the streak broke and restarted", so every rule over the
    /// pair is wrong somewhere — and the wrong one here silently resurrects a streak the
    /// player really did break, or deletes one they really do hold. Two dates have no such
    /// ambiguity. <see cref="StartDay"/> is the day the current run began and
    /// <see cref="LastPlayedDay"/> is the last day something was finished; both only ever
    /// rise, so the merge is <c>max</c> on each and the length is derived, exactly as XP,
    /// credits and hearts are.
    /// </para>
    /// <para>
    /// The one thing that join gives up is worth naming. Two devices that have not synced
    /// can each hold a start date, and taking the later of them can under-report a streak
    /// by a day or two — device A knows about Monday and Tuesday, device B started
    /// counting on Wednesday, and the merged answer says Wednesday. That is the safe
    /// direction: it can only ever shorten a streak, never invent one, and it corrects
    /// itself the moment the devices agree. The alternative, taking the <em>earlier</em>
    /// start, is not a join at all — a stale device could resurrect a streak that was
    /// genuinely broken, and streak rewards escalate.
    /// </para>
    /// <para>
    /// <b>Why there is a third date.</b> A night's reward is collected by hand — see
    /// <see cref="Collect"/> — so something has to say which nights have been taken.
    /// <see cref="CollectedThroughDay"/> is the last one that has, which makes it the
    /// same shape as the other two and mergeable for the same reason: it only ever
    /// rises, so the join is <c>max</c> and a rung already paid cannot come back. The
    /// two obvious alternatives both fail invariant 11b. A count of collected rungs is
    /// hearts' old mistake wearing a different name, and a set of flags per run has to
    /// be cleared when a streak breaks, which is not monotonic and therefore not a join
    /// at all.
    /// </para>
    /// </summary>
    public static class DailyStreak
    {
        static int _startDay;
        static int _lastPlayedDay;
        static int _collectedThrough;

        /// <summary>
        /// Raised when the streak moves, including when it is found broken on a read.
        /// Screens follow this rather than polling, because a day turns over while a
        /// screen is open exactly as often as it turns over while it is not.
        /// </summary>
        public static event Action Changed;

        /// <summary>
        /// Raised when today's run extended the streak, carrying the new length and what
        /// the rung has put aside. The hook a screen uses to celebrate; nothing depends on
        /// it, so a missed one costs a flourish and never a reward.
        /// </summary>
        public static event Action<int, ChestDrop> Advanced;

        /// <summary>
        /// Raised when a night's reward was collected, carrying the night's length along
        /// the ladder and what it paid. For the page that is drawing the tile at the time.
        /// </summary>
        public static event Action<int, ChestDrop> Collected;

        static StreakTable Table => ProgressionRules.Table.Streak;

        static int Today => DailyRules.DayKeyFor(GameClock.NowUnix());

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
        public static int Days => LengthOf(_startDay, _lastPlayedDay, Today);

        /// <summary>
        /// How long a streak is, given the two stored dates and what day it is now.
        ///
        /// <para>
        /// Pure, and takes <paramref name="today"/> rather than reading a clock, for the
        /// same reason every method on <see cref="Persistence.Hearts"/> takes <c>now</c>:
        /// it is what lets the whole rule be exercised across midnights, gaps and merged
        /// files without waiting a day, and it keeps the question of <em>whose</em> clock
        /// it is out of the rule entirely.
        /// </para>
        /// <para>
        /// Yesterday still counts. A streak breaks only once a whole day has passed with
        /// nothing finished in it — otherwise the flame would go out at midnight in front
        /// of a player who is mid-session, and the one thing a streak must never do is
        /// punish somebody who is playing right now.
        /// </para>
        /// </summary>
        public static int LengthOf(int startDay, int lastPlayedDay, int today)
        {
            if (startDay <= 0 || lastPlayedDay <= 0) return 0;
            if (lastPlayedDay < today - 1) return 0;

            int length = lastPlayedDay - startDay + 1;
            return length < 1 ? 0 : length;
        }

        /// <summary>
        /// What the two dates become when a run is finished on <paramref name="today"/>.
        ///
        /// <para>
        /// Pure, for the reason <see cref="LengthOf"/> is, and separate from
        /// <see cref="Record"/> because this is the part with the rule in it: a run
        /// yesterday continues the streak, anything older starts a new one, and a second
        /// run today changes nothing at all. Both returned values are greater than or equal
        /// to the ones passed in, which is the property the merge depends on — see
        /// <see cref="Join"/>.
        /// </para>
        /// </summary>
        public static void Advance(int startDay, int lastPlayedDay, int today,
                                   out int nextStart, out int nextLast)
        {
            nextStart = startDay;
            nextLast = lastPlayedDay;

            if (today <= 0 || lastPlayedDay >= today) return;

            bool continues = startDay > 0 && lastPlayedDay == today - 1;

            nextStart = continues ? startDay : today;
            nextLast = today;
        }

        /// <summary>True when a run has already been finished today.</summary>
        public static bool PlayedToday => _lastPlayedDay >= Today;

        /// <summary>
        /// True when a streak is being held but has not yet been extended today — the one
        /// state worth putting in front of a player, because it is the only one where doing
        /// nothing costs them something.
        /// </summary>
        public static bool AtRisk => Days > 0 && !PlayedToday;

        /// <summary>
        /// What the next rung pays — today's run when the streak has not yet been extended,
        /// tomorrow's when it has. <see cref="ChestDrop.None"/> when that rung pays nothing.
        ///
        /// One expression covers all three states because <see cref="Days"/> already reads
        /// 0 for a broken streak, so "one more than what is held" is the next rung whether
        /// the player is continuing a week or starting over.
        /// </summary>
        public static ChestDrop NextReward => Table.Rung(Days + 1).AsDrop();

        /// <summary>What today's run paid, or nothing when today has not been played.</summary>
        public static ChestDrop TodaysReward
            => PlayedToday ? Table.Rung(Days).AsDrop() : ChestDrop.None;

        /// <summary>The ladder, for a panel that wants to print it.</summary>
        public static StreakTable Ladder => Table;

        // ------------------------------------------------------------ collecting
        /// <summary>The last night whose reward has been handed over. 0 before any.</summary>
        public static int CollectedThroughDay => _collectedThrough;

        /// <summary>
        /// Which calendar day the <paramref name="rung"/>th night of a run beginning on
        /// <paramref name="startDay"/> was, or 0 when there is no run.
        ///
        /// The join between the ladder — which counts from one — and the dates, which is
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
        {
            int day = DayOfRung(startDay, rung);
            return day <= 0 || day <= collectedThrough;
        }

        /// <summary>
        /// True when tapping this night would pay something out, over a given ladder.
        ///
        /// A rung that pays nothing — day one, where the flame merely lights — is never
        /// collectable, so it can never sit on the board asking to be tapped for nothing.
        /// It is swept along silently when a later night is collected.
        /// </summary>
        public static bool CollectableAt(int startDay, int lastPlayedDay, int collectedThrough,
                                         int today, int rung, StreakTable ladder)
        {
            if (ladder == null) return false;
            if (rung < 1 || rung > LengthOf(startDay, lastPlayedDay, today)) return false;
            if (CollectedAt(startDay, collectedThrough, rung)) return false;

            return ladder.Rung(rung).AsDrop().IsValid;
        }

        /// <summary>How many nights are waiting to be collected, over a given ladder.</summary>
        public static int PendingAt(int startDay, int lastPlayedDay, int collectedThrough,
                                    int today, StreakTable ladder)
        {
            int days = LengthOf(startDay, lastPlayedDay, today);
            int count = 0;

            for (int rung = 1; rung <= days; rung++)
                if (CollectableAt(startDay, lastPlayedDay, collectedThrough, today, rung, ladder))
                    count++;

            return count;
        }

        /// <summary>
        /// The earliest night still waiting, or 0 when nothing is.
        ///
        /// What the board pages to. A streak runs on past the end of the ladder — a player
        /// on night forty is the one this feature is for — so the board shows one lap of it
        /// at a time, and the lap it shows has to be the one holding the oldest thing the
        /// player has not taken. Showing the *current* lap instead is how night seven's
        /// reward gets stranded off the board the moment night eight arrives.
        /// </summary>
        public static int FirstPendingAt(int startDay, int lastPlayedDay, int collectedThrough,
                                         int today, StreakTable ladder)
        {
            int days = LengthOf(startDay, lastPlayedDay, today);

            for (int rung = 1; rung <= days; rung++)
                if (CollectableAt(startDay, lastPlayedDay, collectedThrough, today, rung, ladder))
                    return rung;

            return 0;
        }

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
        /// labelled with its <em>absolute</em> night — night eight reads "night 8", not
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
                int pending = FirstPendingAt(_startDay, _lastPlayedDay, _collectedThrough, Today, Table);
                int anchor = pending > 0 ? pending : Days;
                return CycleStart(anchor < 1 ? 1 : anchor, CycleLength);
            }
        }

        /// <summary>Which lap of the ladder the streak is on now. One for the first.</summary>
        public static int Cycle => CycleOf(Days < 1 ? 1 : Days, CycleLength);

        /// <summary>
        /// The collected floor a run starting today seeds, or the one already held.
        ///
        /// Pure counterpart of the rule in <see cref="Record"/>. Two jobs, named there:
        /// nights from a lapsed streak stop being offered, and a live file's floor is
        /// never zero, which is what makes zero mean "written before rewards were
        /// collected by hand".
        /// </summary>
        public static int SeedCollected(int collectedThrough, int today, bool continues)
            => continues || today - 1 <= collectedThrough ? collectedThrough : today - 1;

        /// <summary>
        /// What a floor read off disk becomes. Pure counterpart of <see cref="LoadFrom"/>.
        ///
        /// Zero is a file written before rungs were collected by hand, and everything it
        /// had earned it had also been paid — so the floor is the last day it played.
        /// Anything past that day is a file that has been edited, and is pulled back.
        /// </summary>
        public static int RepairCollected(int collectedThrough, int lastPlayedDay)
        {
            int floor = collectedThrough <= 0 ? lastPlayedDay : collectedThrough;
            return floor > lastPlayedDay ? lastPlayedDay : floor;
        }

        /// <summary>True when the streak has already reached this night.</summary>
        public static bool IsEarned(int rung) => rung >= 1 && rung <= Days;

        /// <summary>True when this night's reward has been taken.</summary>
        public static bool IsCollected(int rung) => CollectedAt(_startDay, _collectedThrough, rung);

        /// <summary>True when tapping this night would pay something out.</summary>
        public static bool IsCollectable(int rung)
            => CollectableAt(_startDay, _lastPlayedDay, _collectedThrough, Today, rung, Table);

        /// <summary>How many nights are waiting to be collected. What a badge counts.</summary>
        public static int Pending
            => PendingAt(_startDay, _lastPlayedDay, _collectedThrough, Today, Table);

        /// <summary>Whether anything is waiting, for a line that wants to mention it.</summary>
        public static bool AnyPending => Pending > 0;

        /// <summary>
        /// Hands over every uncollected night up to and including <paramref name="rung"/>,
        /// and returns how many nights that swept.
        ///
        /// <para>
        /// Collecting is a floor rather than a per-night flag, so taking a later night
        /// necessarily takes the earlier ones with it. That is the only reading that cannot
        /// lose a reward: the alternative — pay just the one tapped, leave the gap — would
        /// need a set of collected nights to be mergeable, and a set that has to be cleared
        /// when a streak breaks is not monotonic and so cannot be joined at all. In
        /// practice the difference is invisible, because a player who opens this page daily
        /// only ever has one night waiting.
        /// </para>
        /// </summary>
        public static int Collect(int rung)
        {
            int days = Days;
            if (rung < 1 || rung > days) return 0;

            int through = DayOf(rung);
            if (through <= _collectedThrough) return 0;

            int swept = 0;

            for (int k = 1; k <= rung; k++)
            {
                if (IsCollected(k)) continue;
                swept++;

                // The floor moves *before* the reward is handed over, one night at a time,
                // and the ordering is load-bearing rather than tidy. Applying a currency
                // rung writes the save — an award has to be durable the moment the player
                // is shown it — so a process killed mid-sweep would otherwise come back
                // with the floor still behind a night whose hearts had already been
                // granted. Currency survives that: the award carries a derived id and the
                // second attempt collides with the first. Hearts carry nothing, so they
                // would simply be paid twice. Paying late is recoverable on the next tap;
                // paying twice is not recoverable at all.
                _collectedThrough = DayOf(k);

                var drop = Table.Rung(k).AsDrop();
                if (!drop.IsValid) continue;

                Apply(drop, DayOf(k), k);

                Telemetry.Track("streak_collected", "rung", k, "day", DayOf(k),
                                "reward", drop.ToString());

                try { Collected?.Invoke(k, drop); }
                catch (Exception e) { UnityEngine.Debug.LogException(e); }
            }

            _collectedThrough = through;

            SaveService.Save();
            Raise();

            return swept;
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
        /// The same event <c>DailyChests.RecordRun</c> counts, and deliberately the same
        /// bar: losing keeps a streak alive. A streak that only counts wins punishes the
        /// player on the day they were struggling, which is the day they most needed a
        /// reason to come back tomorrow — and it would make the hardest glade in a chapter
        /// the place streaks go to die.
        /// </para>
        /// <para>
        /// <b>The reward is set aside, not paid.</b> It waits on the streak page until the
        /// player taps it — see <see cref="Collect"/>. What that buys is the moment: a
        /// reward that lands silently while a defeat screen is animating is a number the
        /// player never sees arrive, and a number nobody watches arrive is not a reward, it
        /// is an accounting entry. It also gives the page a reason to be opened, which is
        /// the whole point of a streak.
        /// </para>
        /// <para>
        /// What is <em>not</em> a reason to defer it is safety. Every kind on the ladder
        /// survives being handed over twice: hearts and boosts merge idempotently — two
        /// devices that both extend the streak offline grant the same hearts, and
        /// <c>max(produced)</c> keeps one set rather than two — and currency is claimed
        /// under an id derived from the night's calendar day, so the two devices produce
        /// one entry between them. Deferring buys the moment, not the correctness.
        /// </para>
        /// </summary>
        public static void Record()
        {
            int today = Today;

            Advance(_startDay, _lastPlayedDay, today, out int nextStart, out int nextLast);

            // Nothing moved: the day has already been counted, which is what makes this
            // idempotent within a day and lets the second run of an evening cost nothing.
            if (nextStart == _startDay && nextLast == _lastPlayedDay) return;

            bool continues = nextStart == _startDay;

            _startDay = nextStart;
            _lastPlayedDay = nextLast;

            // A run that starts a new streak seeds the collected floor to the day before
            // it, which does two jobs at once. Nights from a streak the player let lapse
            // stop being offered, and — because a day key is a five-figure number — the
            // floor of a live file is never zero, which is what lets a zero mean "written
            // before rewards were collected by hand". See StreakStateDto.
            _collectedThrough = SeedCollected(_collectedThrough, today, continues);

            int length = LengthOf(_startDay, _lastPlayedDay, today);
            var reward = Table.Rung(length).AsDrop();

            SaveService.Save();
            Raise();

            Telemetry.Track("streak_advanced",
                            "day", today,
                            "length", length,
                            "continued", continues,
                            "reward", reward.ToString());

            try { Advanced?.Invoke(length, reward); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        /// <summary>
        /// Hands over one night's rung. Reached only from <see cref="Collect"/>.
        ///
        /// <para>
        /// The same split <c>DailyChests.Apply</c> makes, for the same reason. Hearts and
        /// boosts are applied here and now: a heart clamps at five and a boost expires, so
        /// trusting the client with them costs at most a few extra runs today. Currency is
        /// queued as an identified claim for the server to adjudicate, because currency is
        /// the thing real money buys and therefore the thing an attacker forges — the
        /// client never raises <c>grantedBaseline</c> itself. Invariant 10a.
        /// </para>
        /// <para>
        /// The award lands in the ledger immediately either way, so a night collected on a
        /// plane is spendable on that plane. What the server later decides can only revise
        /// it downward, and it does that by replacing a baseline rather than by taking
        /// anything back — see <c>CurrencyLedger.BalanceFrom</c>.
        /// </para>
        /// </summary>
        static void Apply(ChestDrop reward, int dayKey, int night)
        {
            if (!reward.IsValid) return;

            switch (reward.Kind)
            {
                case ChestDropKind.Credits:
                case ChestDropKind.Gems:
                    string currency = ChestDropKinds.CurrencyOf(reward.Kind);
                    PlayerProgression.Award(
                        currency, reward.Amount,
                        GrantEntry.StreakNightId(dayKey, night, currency),
                        GrantEntry.StreakNightReason, GameClock.NowUnix());
                    break;

                case ChestDropKind.Hearts:
                    Wallet.GrantHearts(reward.Amount);
                    break;

                case ChestDropKind.HeartBoost:
                    Wallet.GrantHeartBoost(reward.Amount);
                    break;
            }
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

            // A file written by hand, or one merged from a device whose clock disagreed,
            // could name a start after the last day played. Repaired on read rather than
            // trusted, because the length is a subtraction and a negative one would show
            // the player a streak that counts backwards.
            if (_startDay > _lastPlayedDay) _startDay = _lastPlayedDay;

            // Pre-v10: rungs were applied at the end of a run, so everything this file has
            // earned it has also been paid. Left at zero, every night of a live streak
            // would light up as collectable on the first launch of this build and pay a
            // second time. A live v10 file cannot say zero here — see StreakStateDto.
            _collectedThrough = RepairCollected(_collectedThrough, _lastPlayedDay);

            Raise();
        }

        internal static void WriteInto(SaveFileDto dto)
        {
            dto.streak = new StreakStateDto
            {
                startDay = _startDay,
                lastPlayedDay = _lastPlayedDay,
                collectedThroughDay = _collectedThrough,
            };
        }

        /// <summary>
        /// Joins two devices' streaks: <c>max</c> on both dates and nothing else.
        ///
        /// <para>
        /// Both fields are counters of things that happened rather than balances, so the
        /// larger is always the one that knows more — a later last-played day has seen a
        /// session the other missed, and a later start day has seen a break the other
        /// missed. That makes this idempotent, commutative and associative like every other
        /// merge in this file, with no opinion needed about which device is "right".
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
                // — the same direction the start date already errs in, and for the same
                // reason: the alternative pays a night twice, and two devices claiming the
                // same hearts is the failure this whole file is shaped to avoid.
                collectedThroughDay = Math.Max(mine.collectedThroughDay, other.collectedThroughDay),
            };
        }

        static StreakStateDto Copy(StreakStateDto s)
            => new StreakStateDto
            {
                startDay = s.startDay,
                lastPlayedDay = s.lastPlayedDay,
                collectedThroughDay = s.collectedThroughDay,
            };

        /// <summary>Forgets the streak. Dev only, and used by the wipe.</summary>
        internal static void Reset()
        {
            _startDay = 0;
            _lastPlayedDay = 0;
            _collectedThrough = 0;
        }
    }
}
