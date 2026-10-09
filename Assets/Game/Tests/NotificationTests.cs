using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Notifications;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The whole notification feature that can be wrong, run with no phone attached.
    ///
    /// <para>
    /// The planner is a pure function of a snapshot and a table, which is what makes this
    /// possible and is the reason it was built that way: the part of a reminder system that
    /// actually fails - the pacing, the ordering, the quiet hours, a sentence that has stopped
    /// being true by the time somebody reads it - is arithmetic here rather than something you
    /// find out about from a review.
    /// </para>
    /// <para>
    /// Nothing in this file needs <c>JsonUtility</c>, which is deliberate: every
    /// <c>*VectorTests</c> in this project is skipped by the offline runner because it makes a
    /// native call, and those are the fixtures nobody runs on the way past (invariant 29e).
    /// </para>
    /// </summary>
    public sealed class NotificationTests
    {
        // A Thursday, 00:00 UTC, so day arithmetic is easy to read against.
        const long Midnight = 1_700_000_000L / DailyRules.SecondsPerDay * DailyRules.SecondsPerDay;

        static NotificationState Quiet(long now = Midnight, int offset = 0)
            => new NotificationState(now, offset,
                                     heartsFullUnix: 0L,
                                     streakChestWaiting: false,
                                     streakLastPlayedDay: 0, streakDays: 0,
                                     shieldFromDay: 0, shieldDays: 0,
                                     seasonOpen: false, seasonEndDay: 0, seasonRungsReady: false,
                                     onBoards: false, endlessOpen: false);

        static NotificationState Busy(long now = Midnight, int offset = 0)
        {
            int today = DailyRules.DayKeyFor(now);
            return new NotificationState(now, offset,
                                         heartsFullUnix: now + 4 * 3600L,
                                         streakChestWaiting: true,
                                         streakLastPlayedDay: today, streakDays: 6,
                                         shieldFromDay: 0, shieldDays: 0,
                                         seasonOpen: true, seasonEndDay: today + 30,
                                         seasonRungsReady: true,
                                         onBoards: true, endlessOpen: true);
        }

        // ------------------------------------------------------------- the ceiling
        /// <summary>
        /// The one limit that is enforced by silence rather than by an error: iOS keeps the 64
        /// soonest pending local notifications and drops the rest without a word, so a horizon
        /// that overflowed it would work everywhere except on iPhone, a week out, for a player
        /// who had stopped playing. The built-in table has to be comfortably under it.
        /// </summary>
        [Test]
        public void TheBuiltInTableCannotOverflowTheDeviceLimit()
        {
            var table = NotificationTable.Built;

            Assert.LessOrEqual(table.PerDay, NotificationWindow.Slots,
                               "a day cannot send more than it has slots for");
            Assert.LessOrEqual(table.MostPending, NotificationWindow.MaxPending,
                               "iOS drops pending notifications past its own limit in silence");

            // The taper is what makes a three-week reach affordable at all: flat, this horizon
            // would ask for 63 pending against a ceiling of 60 and lose its own back end on
            // iOS with nothing reporting it.
            Assert.Greater(table.HorizonDays, table.TaperAfterDays,
                           "a taper that never begins is a flat schedule with extra arithmetic");
            Assert.Greater(table.HorizonDays * table.PerDay, NotificationWindow.MaxPending,
                           "if a flat schedule would fit, the taper is buying nothing");
        }

        [Test]
        public void EveryPlanStaysUnderTheDeviceLimit()
        {
            var plan = NotificationPlan.Build(Busy(), NotificationTable.Built);

            Assert.LessOrEqual(plan.Count, NotificationWindow.MaxPending);
            Assert.Greater(plan.Count, 0, "a player with everything waiting should hear something");
        }

        // ---------------------------------------------------------------- the day
        /// <summary>
        /// Nothing is ever scheduled while somebody is asleep. The hours are content and can be
        /// retuned by a push, so this is asked of the plan rather than of the table - a slate
        /// that moved an hour past the bound would otherwise fail nothing.
        /// </summary>
        [Test]
        public void NothingLandsOutsideTheWakingDay()
        {
            foreach (int offset in new[] { 0, -8 * 3600, 5 * 3600 + 1800, 13 * 3600 })
            {
                var plan = NotificationPlan.Build(Busy(offset: offset), NotificationTable.Built);

                foreach (var planned in plan)
                {
                    long local = planned.FireUnix + offset;
                    long minute = local % DailyRules.SecondsPerDay / 60L;

                    Assert.GreaterOrEqual(minute, NotificationWindow.EarliestMinute,
                                          $"{planned.Kind} lands at local minute {minute}");
                    Assert.LessOrEqual(minute, NotificationWindow.LatestMinute,
                                       $"{planned.Kind} lands at local minute {minute}");
                }
            }
        }

        [Test]
        public void NothingLandsBeforeThePlayerHasHadTimeToPutThePhoneDown()
        {
            var state = Busy();
            var plan = NotificationPlan.Build(state, NotificationTable.Built);

            foreach (var planned in plan)
                Assert.GreaterOrEqual(planned.FireUnix, state.NowUnix + NotificationWindow.QuietSeconds,
                                      $"{planned.Kind} arrives over the session it was armed in");
        }

        [Test]
        public void ThePlanIsInTheOrderItWillFire()
        {
            var plan = NotificationPlan.Build(Busy(), NotificationTable.Built);

            for (int i = 1; i < plan.Count; i++)
                Assert.LessOrEqual(plan[i - 1].FireUnix, plan[i].FireUnix);
        }

        /// <summary>
        /// Two to three a day is the brief, and it has to hold on the player who has
        /// <em>everything</em> waiting - that is the one whose day the table could fill.
        /// </summary>
        [Test]
        public void NoDayEverHoldsMoreThanTheTableAllows()
        {
            var table = NotificationTable.Built;
            var state = Busy();
            var plan = NotificationPlan.Build(state, table);
            var perDay = new Dictionary<int, int>();

            foreach (var planned in plan)
            {
                int day = NotificationPlan.LocalDayOf(planned.FireUnix, state.UtcOffsetSeconds);
                perDay[day] = perDay.TryGetValue(day, out int n) ? n + 1 : 1;
            }

            int first = NotificationPlan.LocalDayOf(state.NowUnix, state.UtcOffsetSeconds);

            foreach (var pair in perDay)
                Assert.LessOrEqual(pair.Value, table.PerDayOn(pair.Key - first),
                                   $"day {pair.Key - first} holds {pair.Value}");
        }

        /// <summary>
        /// The reach is the point of the taper, and the horizon is the one number a lapsed
        /// player's whole experience of this feature rests on: past it the game is silent.
        /// </summary>
        [Test]
        public void ALapsedPlayerIsStillReachedInTheThirdWeek()
        {
            var state = Busy();
            var plan = NotificationPlan.Build(state, NotificationTable.Built);

            Assert.IsNotEmpty(plan);

            long last = plan[plan.Count - 1].FireUnix;
            int days = (int)((last - state.NowUnix) / DailyRules.SecondsPerDay);

            Assert.GreaterOrEqual(days, 13, "a fortnight is the least this is worth doing for");
        }

        /// <summary>
        /// And the tail is thin as well as long. A player three weeks away being told three
        /// times a day is the behaviour that gets an app muted rather than reopened. Which slot
        /// the single one lands in is not asserted: it goes to whichever candidate is off
        /// cooldown, which is the evening on most days and the morning when the evening kinds
        /// are all resting.
        /// </summary>
        [Test]
        public void TheTailNeverHoldsMoreThanOneADay()
        {
            var table = NotificationTable.Built;
            var state = Busy();
            var plan = NotificationPlan.Build(state, table);

            int first = NotificationPlan.LocalDayOf(state.NowUnix, state.UtcOffsetSeconds);
            var late = new Dictionary<int, int>();

            foreach (var planned in plan)
            {
                int day = NotificationPlan.LocalDayOf(planned.FireUnix, state.UtcOffsetSeconds) - first;
                if (day < table.TaperAfterDays) continue;
                late[day] = late.TryGetValue(day, out int n) ? n + 1 : 1;
            }

            Assert.IsNotEmpty(late, "the taper should still be saying something");
            foreach (var pair in late)
                Assert.LessOrEqual(pair.Value, NotificationTable.TaperPerDay,
                                   $"day {pair.Key} past the taper holds {pair.Value}");
        }

        /// <summary>
        /// Twice in one day is the failure everybody has met: the same sentence at 09:30 and
        /// again at 19:30 reads as a bug in the game rather than a nudge.
        /// </summary>
        [Test]
        public void NoKindIsSaidTwiceInOneDay()
        {
            var plan = NotificationPlan.Build(Busy(), NotificationTable.Built);
            var seen = new HashSet<(int, NotificationKind)>();

            foreach (var planned in plan)
            {
                var key = (DailyRules.DayKeyFor(planned.FireUnix), planned.Kind);
                Assert.IsTrue(seen.Add(key), $"{planned.Kind} is said twice on one day");
            }
        }

        [Test]
        public void ACooldownIsHonoured()
        {
            var table = NotificationTable.Built;
            var plan = NotificationPlan.Build(Busy(), table);
            var lastDay = new Dictionary<NotificationKind, int>();

            foreach (var planned in plan)
            {
                int day = DailyRules.DayKeyFor(planned.FireUnix);
                var entry = table.Find(planned.Kind);

                if (lastDay.TryGetValue(planned.Kind, out int previous))
                    Assert.GreaterOrEqual(day - previous, entry.MinDaysBetween,
                                          $"{planned.Kind} came round after {day - previous} day(s)");

                lastDay[planned.Kind] = day;
            }
        }

        // ------------------------------------------------------------- the truth
        /// <summary>
        /// The rule the whole scheme rests on: a notification is written now and read up to a
        /// week later, so what it says has to be true <em>then</em>. Nothing else in this
        /// project has to hold a claim about the future.
        /// </summary>
        [Test]
        public void EverythingPlannedIsStillTrueWhenItFires()
        {
            var state = Busy();

            foreach (var planned in NotificationPlan.Build(state, NotificationTable.Built))
                Assert.IsTrue(state.IsTrueAt(planned.Kind, planned.FireUnix),
                              $"{planned.Kind} would arrive saying something that is not true");
        }

        /// <summary>
        /// A streak is nagged about on exactly the day it can still be saved - never a day
        /// later, when it has already broken and the sentence is a lie.
        /// </summary>
        [Test]
        public void AStreakIsOnlyNaggedOnTheDayItCanStillBeSaved()
        {
            var state = Busy();
            int played = state.StreakLastPlayedDay;

            Assert.IsFalse(state.IsTrueAt(NotificationKind.StreakRisk,
                                          DailyRules.DayStartUnix(played) + 3600L),
                           "they played today; there is nothing at risk yet");
            Assert.IsTrue(state.IsTrueAt(NotificationKind.StreakRisk,
                                         DailyRules.DayStartUnix(played + 1) + 3600L));
            Assert.IsFalse(state.IsTrueAt(NotificationKind.StreakRisk,
                                          DailyRules.DayStartUnix(played + 2) + 3600L),
                           "by then it has already broken, so the sentence would be a lie");
        }

        /// <summary>
        /// A shield is bought to stop exactly this. Nagging through one is charging somebody
        /// gems for a week of being told the thing they paid to prevent.
        /// </summary>
        [Test]
        public void AShieldedStreakIsNeverNagged()
        {
            int today = DailyRules.DayKeyFor(Midnight);
            var state = new NotificationState(Midnight, 0, 0L, false,
                                              streakLastPlayedDay: today, streakDays: 9,
                                              shieldFromDay: today, shieldDays: 7,
                                              seasonOpen: false, seasonEndDay: 0,
                                              seasonRungsReady: false,
                                              onBoards: false, endlessOpen: false);

            foreach (var planned in NotificationPlan.Build(state, NotificationTable.Built))
                Assert.AreNotEqual(NotificationKind.StreakRisk, planned.Kind);
        }

        /// <summary>
        /// Hearts that were already full when the player closed the game are not news that
        /// evening - they were holding them an hour ago.
        /// </summary>
        [Test]
        public void AFullHeartBarIsNotNewsOnTheDayItWasSeen()
        {
            var state = Quiet(Midnight + 12 * 3600L);

            Assert.IsFalse(state.IsTrueAt(NotificationKind.HeartsFull, Midnight + 19 * 3600L));
            Assert.IsTrue(state.IsTrueAt(NotificationKind.HeartsFull,
                                         Midnight + DailyRules.SecondsPerDay + 13 * 3600L));
        }

        [Test]
        public void ASeasonThatIsNotRunningIsNeverMentioned()
        {
            foreach (var planned in NotificationPlan.Build(Quiet(), NotificationTable.Built))
            {
                Assert.AreNotEqual(NotificationKind.SeasonRungs, planned.Kind);
                Assert.AreNotEqual(NotificationKind.SeasonEnding, planned.Kind);
            }
        }

        /// <summary>
        /// The quiet player - nothing waiting, no streak, no season, no boards - still hears
        /// from the game, and hears from it less often than the busy one. That gap is the whole
        /// of what "two to three a day" means here: it is an outcome of the cooldowns rather
        /// than a quota anybody enforces.
        /// </summary>
        [Test]
        public void AQuietAccountIsRemindedLessOftenThanABusyOne()
        {
            var quiet = NotificationPlan.Build(Quiet(), NotificationTable.Built);
            var busy = NotificationPlan.Build(Busy(), NotificationTable.Built);

            Assert.Greater(quiet.Count, 0, "even a quiet account is worth inviting back");
            Assert.Less(quiet.Count, busy.Count,
                        "a player with nothing waiting should hear from us less, not the same");
        }

        // -------------------------------------------------------------- the copy
        /// <summary>
        /// Every kind's copy is derived from its permanent id, so a kind that exists has two
        /// keys and nothing has to remember to write them down.
        /// </summary>
        [Test]
        public void EveryKindNamesItsOwnCopy()
        {
            foreach (var kind in NotificationKinds.All)
            {
                string id = NotificationKinds.Id(kind);

                Assert.IsNotEmpty(id, $"{kind} has no permanent id");
                Assert.AreEqual(kind, NotificationKinds.Parse(id), "an id must round-trip");
                Assert.AreEqual($"notify.{id}.title", NotificationKinds.TitleKey(kind));
                Assert.AreEqual($"notify.{id}.body", NotificationKinds.BodyKey(kind));
            }
        }

        /// <summary>
        /// An id this build has never heard of is dropped rather than throwing, because a
        /// newer content pack reaching an older client is ordinary - and refusing the whole
        /// table would mean one new reminder takes every reminder down.
        /// </summary>
        [Test]
        public void AnUnknownKindIsDroppedRatherThanFatal()
        {
            Assert.AreEqual(NotificationKind.None, NotificationKinds.Parse("a_kind_from_2028"));
            Assert.AreEqual(NotificationKind.None, NotificationKinds.Parse(null));
        }

        // ------------------------------------------------------------- the table
        [Test]
        public void AnAbsentBlockLeavesTheBuiltInTableStanding()
        {
            var problems = new List<string>();

            Assert.AreSame(NotificationTable.Built, NotificationTable.Resolve(null, problems));
            Assert.IsEmpty(problems, "an unwritten block is not a fault");
        }

        [Test]
        public void ATableThatWouldOverflowTheDeviceIsRefused()
        {
            var problems = new List<string>();
            var dto = new NotificationsDto
            {
                perDay = 3,
                horizonDays = 90,
                taperAfterDays = 30,
                entries = new[] { Row("grove_idle", "any", 10, 1) },
            };

            Assert.AreSame(NotificationTable.Built, NotificationTable.Resolve(dto, problems));
            Assert.IsNotEmpty(problems);
        }

        [Test]
        public void AnHourOutsideTheWakingDayIsRefused()
        {
            var problems = new List<string>();
            var dto = new NotificationsDto
            {
                hours = new NotificationHoursDto { morning = 3 * 60, afternoon = 13 * 60, evening = 19 * 60 },
                entries = new[] { Row("grove_idle", "any", 10, 1) },
            };

            Assert.AreSame(NotificationTable.Built, NotificationTable.Resolve(dto, problems));
            Assert.IsNotEmpty(problems, "three in the morning is an uninstall, not a reminder");
        }

        [Test]
        public void ATableThatEnablesNothingIsRefused()
        {
            var problems = new List<string>();
            var dto = new NotificationsDto
            {
                entries = new[] { new NotificationEntryDto { kind = "grove_idle", disabled = true } },
            };

            Assert.AreSame(NotificationTable.Built, NotificationTable.Resolve(dto, problems));
            Assert.IsNotEmpty(problems);
        }

        [Test]
        public void ADisabledKindIsNeverSent()
        {
            var problems = new List<string>();
            var dto = new NotificationsDto
            {
                entries = new[]
                {
                    new NotificationEntryDto { kind = "grove_idle", slot = "any", priority = 10, minDaysBetween = 1, disabled = true },
                    Row("endless_call", "evening", 30, 1),
                },
            };

            var table = NotificationTable.Resolve(dto, problems);
            Assert.IsEmpty(problems);

            foreach (var planned in NotificationPlan.Build(Busy(), table))
                Assert.AreNotEqual(NotificationKind.GroveIdle, planned.Kind);

            NotificationTable.Resolve(null, new List<string>());   // leave the static as found
        }

        // ------------------------------------------------------------- asking (50p)
        /// <summary>
        /// <b>Our panel's yes goes where the OS can still say yes</b>: its own dialog while it
        /// will draw one (never asked, iOS provisional, Android's second chance), its settings
        /// page once it never will, and nowhere at all when there is nothing to ask - granted,
        /// mid-dialog, unsupported, or switched off by the player in our own row.
        /// </summary>
        [Test]
        public void TheRouteIsWhereTheOsCanStillSayYes()
        {
            var P = System.Enum.GetValues(typeof(NotificationPermission));
            foreach (NotificationPermission permission in P)
            foreach (bool again in new[] { false, true })
            {
                Assert.AreEqual(AskRoute.None, NotificationAsk.Route(permission, again, wanted: false),
                                $"{permission}/{again}: a player who switched reminders off is never asked");

                AskRoute expected;
                switch (permission)
                {
                    case NotificationPermission.Unasked:
                    case NotificationPermission.Provisional:
                        expected = AskRoute.System; break;
                    case NotificationPermission.Denied:
                        expected = again ? AskRoute.System : AskRoute.Settings; break;
                    case NotificationPermission.Granted:
                    case NotificationPermission.Pending:
                    case NotificationPermission.Unsupported:
                        expected = AskRoute.None; break;
                    default:
                        Assert.Fail($"{permission} has no expected route; add it here"); return;
                }

                Assert.AreEqual(expected, NotificationAsk.Route(permission, again, wanted: true),
                                $"{permission}, second chance {again}");
                Assert.AreEqual(expected == AskRoute.System, NotificationAsk.OsWillPrompt(permission, again),
                                $"{permission}/{again}: OsWillPrompt and the System route are one definition");
            }
        }

        /// <summary>
        /// <b>Spaced, growing, and capped for the life of the install.</b> Due at once the first
        /// time; then 3, 7 and 14 days after each panel, never a second sooner; never after the
        /// fourth. Walked as a player who says "not now" the moment each one is due.
        /// </summary>
        [Test]
        public void ThePanelIsSpacedAndCapped()
        {
            const long Day = DailyRules.SecondsPerDay;
            var log = default(AskLog);
            long now = Midnight;

            Assert.IsTrue(NotificationAsk.Due(log, now), "the first panel is due the first time it can be shown");

            int[] gaps = { 3, 7, 14 };
            for (int shown = 0; shown < NotificationAsk.MaxShown; shown++)
            {
                log = log.After(now);
                Assert.AreEqual(shown + 1, log.Shown);

                if (shown + 1 == NotificationAsk.MaxShown) break;

                long gap = gaps[shown] * Day;
                Assert.AreEqual(gaps[shown], NotificationAsk.GapAfter(log.Shown));
                Assert.IsFalse(NotificationAsk.Due(log, now + gap - 1), $"panel {shown + 2} came a second early");
                Assert.IsTrue(NotificationAsk.Due(log, now + gap), $"panel {shown + 2} did not come on its day");
                now += gap;
            }

            Assert.AreEqual(NotificationAsk.MaxShown, log.Shown);
            Assert.IsFalse(NotificationAsk.Due(log, now + 3650 * Day), "the cap is for the life of the install");
            Assert.AreEqual(NotificationAsk.MaxShown, log.After(now).Shown, "the count never passes the cap");

            Assert.AreEqual(AskRoute.None,
                            NotificationAsk.Decide(NotificationPermission.Unasked, false, true, log, now + 3650 * Day),
                            "Decide honours the cap whatever the route");
            Assert.AreEqual(AskRoute.Settings,
                            NotificationAsk.Decide(NotificationPermission.Denied, false, true, default, now),
                            "Decide passes the route through when due");
        }

        /// <summary>
        /// <b>A stamp from a clock that has since been corrected does not silence the feature</b>
        /// until the calendar catches up with it - a day's tolerance, then treated as old.
        /// </summary>
        [Test]
        public void AStampFromTheFutureIsNotTrusted()
        {
            const long Day = DailyRules.SecondsPerDay;
            var log = new AskLog(1, Midnight + 400 * Day);

            Assert.IsTrue(NotificationAsk.Due(log, Midnight), "a year-ahead stamp would have silenced every panel");
            Assert.IsFalse(NotificationAsk.Due(new AskLog(1, Midnight + Day / 2), Midnight),
                           "inside a day is ordinary clock drift and still waits its gap");
        }

        /// <summary>
        /// <b>The device record round-trips, and anything it cannot read is the empty log</b> -
        /// which can cost one panel more than was owed, never one fewer than the cap allows.
        /// </summary>
        [Test]
        public void TheAskLogRoundTripsAndForgivesGarbage()
        {
            var log = new AskLog(2, 1_760_000_123L);
            var back = AskLog.Parse(log.Format());
            Assert.AreEqual(2, back.Shown);
            Assert.AreEqual(1_760_000_123L, back.LastUnix);
            Assert.AreEqual("2:1760000123", log.Format(), "the spelling is a device-stored contract");

            foreach (var junk in new[] { null, "", ":", "2:", ":5", "x:5", "2:y", "-1:5", "2:-5", " 2:5", "2:5:6" })
            {
                var read = AskLog.Parse(junk);
                Assert.AreEqual(0, read.Shown, $"'{junk}'");
                Assert.AreEqual(0L, read.LastUnix, $"'{junk}'");
            }

            Assert.AreEqual(NotificationAsk.MaxShown, AskLog.Parse("99:5").Shown, "a count past the cap reads as the cap");
        }

        /// <summary>
        /// <b>Android's second chance travels only with a refusal</b>, and <c>CanAsk</c> is the
        /// same definition the panel's route uses.
        /// </summary>
        [Test]
        public void TheSecondChanceTravelsOnlyWithARefusal()
        {
            try
            {
                NotificationOptIn.SetPermission(NotificationPermission.Granted, canPromptAgain: true);
                Assert.IsFalse(NotificationOptIn.CanPromptAgain, "only a refusal carries a second chance");
                Assert.IsFalse(NotificationOptIn.CanAsk);

                NotificationOptIn.SetPermission(NotificationPermission.Denied, canPromptAgain: true);
                Assert.IsTrue(NotificationOptIn.CanAsk, "Android draws its dialog once more after one refusal");

                NotificationOptIn.SetPermission(NotificationPermission.Denied);
                Assert.IsFalse(NotificationOptIn.CanAsk, "after the second refusal only the settings page is left");

                NotificationOptIn.SetPermission(NotificationPermission.Provisional);
                Assert.IsTrue(NotificationOptIn.CanAsk, "provisional still has the full dialog to give");
            }
            finally
            {
                NotificationOptIn.SetPermission(NotificationPermission.Unsupported);
            }
        }

        /// <summary>Every state and route has an analytics spelling; a new member without one throws here, not on a phone.</summary>
        [Test]
        public void EveryStateHasAnAnalyticsSpelling()
        {
            var seen = new HashSet<string>();
            foreach (NotificationPermission p in System.Enum.GetValues(typeof(NotificationPermission)))
                Assert.IsTrue(seen.Add(NotificationOptIn.Id(p)), $"{p} shares a spelling");

            seen.Clear();
            foreach (AskRoute r in System.Enum.GetValues(typeof(AskRoute)))
                Assert.IsTrue(seen.Add(NotificationAsk.Id(r)), $"{r} shares a spelling");
        }

        // ------------------------------------------------------------- the launch
        /// <summary>
        /// <b>The OS is never asked at launch</b> (invariant 50h). The package's iOS setting
        /// "request authorization on app launch" puts Apple's notification dialog in front of
        /// the first frame, before the splash - ahead of the tutorial, the consent form and
        /// everything else - and it shipped switched on while the code carefully asked at a
        /// chest. It lives in a project-settings file no code path reads, so this reads it.
        /// </summary>
        [Test]
        public void TheOsIsNeverAskedForNotificationsAtLaunch()
        {
            string path = Path.Combine(CheckoutRoot(), "ProjectSettings", "NotificationsSettings.asset");
            Assert.IsTrue(File.Exists(path), $"notification settings not found at {path}");

            var ios = Regex.Match(File.ReadAllText(path),
                                  @"""m_iOSNotificationSettingsValues""\s*:\s*\{\s*""m_Keys""\s*:\s*\[(?<keys>[^\]]*)\]\s*,\s*""m_Values""\s*:\s*\[(?<values>[^\]]*)\]");
            Assert.IsTrue(ios.Success, "the iOS block of NotificationsSettings.asset has changed shape");

            var keys = Strings(ios.Groups["keys"].Value);
            var values = Strings(ios.Groups["values"].Value);
            Assert.AreEqual(keys.Count, values.Count, "keys and values out of step");

            foreach (var key in new[]
                     {
                         "UnityNotificationRequestAuthorizationOnAppLaunch",
                         "UnityNotificationRequestAuthorizationForRemoteNotificationsOnAppLaunch",
                     })
            {
                int at = keys.IndexOf(key);
                Assert.GreaterOrEqual(at, 0, $"{key} is missing; the package may have renamed it");
                Assert.AreEqual("False", values[at], $"{key} must be off: the permission is asked at a chest");
            }
        }

        static List<string> Strings(string list)
        {
            var found = new List<string>();
            foreach (Match m in Regex.Matches(list, @"""(?<s>[^""]*)"""))
                found.Add(m.Groups["s"].Value);
            return found;
        }

        /// <summary>
        /// The checkout, found the way <c>CloudWireTests.RepoRoot</c> finds it: the engine's
        /// answer when there is an engine, otherwise up from the working directory, because the
        /// offline runner has none and a test that "needs the Editor" is not a gate (29e).
        /// </summary>
        static string CheckoutRoot()
        {
            string root = null;

            try { root = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..")); }
            catch (System.Exception) { }

            if (root != null && Directory.Exists(Path.Combine(root, "ProjectSettings"))) return root;

            string dir = Directory.GetCurrentDirectory();
            while (!string.IsNullOrEmpty(dir) && !Directory.Exists(Path.Combine(dir, "ProjectSettings")))
                dir = Path.GetDirectoryName(dir);

            Assert.IsFalse(string.IsNullOrEmpty(dir), "could not find the checkout from " +
                                                       Directory.GetCurrentDirectory());
            return dir;
        }

        static NotificationEntryDto Row(string kind, string slot, int priority, int gap)
            => new NotificationEntryDto
            {
                kind = kind, slot = slot, priority = priority, minDaysBetween = gap,
            };
    }
}
