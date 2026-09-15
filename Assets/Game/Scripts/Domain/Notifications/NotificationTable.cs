using System;
using System.Collections.Generic;
using GlimmerGrove.Content;

namespace GlimmerGrove.Notifications
{
    /// <summary>
    /// Which reminders this game sends, at what hours, and how many a day — content, with a
    /// working built-in table behind it.
    ///
    /// <para>
    /// <b>Content for <c>TaskTable</c>'s reason and one sharper one.</b> The slate is a
    /// live-ops surface: the single most likely thing to need changing after launch is *how
    /// often this game speaks*, and a build that has to clear two store reviews to quieten
    /// down is a build that annoys people for a fortnight. It rides in <c>progression.json</c>
    /// as an optional block, so a client that predates it keeps its built-in table and a
    /// client that has it reading an older file does the same.
    /// </para>
    /// <para>
    /// <b>It reaches no server, and that is worth saying plainly</b>, because every other
    /// block in that file does. Nothing here is adjudicated, nothing is paid and nothing is
    /// claimed — a notification is a thing a phone says to its owner — so
    /// <c>seed-config.mjs</c> does not publish it and <c>firestore.rules</c> has nothing to
    /// learn. The push path for this block is the remote-content one
    /// (<c>ContentConfig.RemoteBaseUrl</c>), which is the same path a chapter takes.
    /// </para>
    /// <para>
    /// <b>Refused whole on a structural fault, degraded on an unknown</b> — the split
    /// <c>DailyChestTable.Resolve</c> draws. A table with no live rows, a duplicated kind or
    /// an hour outside the day would send nothing or send it at three in the morning, so the
    /// built-in one stands; a row naming a kind this build has never heard of is a newer
    /// content pack reaching an older client, and is dropped by name.
    /// </para>
    /// </summary>
    public sealed class NotificationTable
    {
        readonly NotificationEntry[] _entries;
        readonly Dictionary<NotificationKind, NotificationEntry> _byKind;

        NotificationTable(NotificationEntry[] entries, int[] hours, int perDay,
                          int horizonDays, int taperAfterDays)
        {
            _entries = entries ?? new NotificationEntry[0];
            Hours = hours;
            PerDay = perDay;
            HorizonDays = horizonDays;
            TaperAfterDays = taperAfterDays;

            _byKind = new Dictionary<NotificationKind, NotificationEntry>();
            foreach (var entry in _entries) _byKind[entry.Kind] = entry;
        }

        /// <summary>The slate, in ranking order. Retired and disabled rows included.</summary>
        public IReadOnlyList<NotificationEntry> Entries => _entries;

        /// <summary>
        /// The local hour of each slot, in minutes past midnight, indexed by
        /// <see cref="NotificationSlot"/> minus one. Always three, always inside the waking day.
        /// </summary>
        public int[] Hours { get; }

        /// <summary>
        /// The most this game may say on one of the <em>early</em> days. Never more than three
        /// slots exist. After <see cref="TaperAfterDays"/> the cap drops to <see cref="TaperPerDay"/>.
        /// </summary>
        public int PerDay { get; }

        /// <summary>
        /// How many days keep the full <see cref="PerDay"/> before the schedule thins.
        ///
        /// <para>
        /// <b>This is what buys a three-week reach out of a budget that only ever allowed one
        /// week.</b> The binding constraint is iOS's pending-notification ceiling, and a flat
        /// three a day spends it in three weeks' worth of allowance in seven days. Tapering
        /// costs nothing and roughly triples how long a lapsed player stays reachable — which
        /// matters because the player worth reminding is precisely the one who has not opened
        /// the game, and past the horizon this scheme is simply silent.
        /// </para>
        /// <para>
        /// <b>It also happens to be the right shape rather than merely the affordable one.</b>
        /// Somebody three weeks away does not need telling three times a day; the frequency
        /// that reads as attentive on day one reads as desperate on day fifteen. So the tail
        /// is one a night, in the evening slot, which is what the fill order already hands a
        /// single-slot day.
        /// </para>
        /// </summary>
        public int TaperAfterDays { get; }

        /// <summary>
        /// What a day past the taper may hold. One, and deliberately not content.
        ///
        /// A dial here would be a second way to spend the same ceiling, and the ceiling is the
        /// thing a content push must not be able to overrun — see <see cref="HorizonDays"/>.
        /// </summary>
        public const int TaperPerDay = 1;

        /// <summary>The cap on a given day of the plan, counting from nought.</summary>
        public int PerDayOn(int day) => day < TaperAfterDays ? PerDay : TaperPerDay;

        /// <summary>
        /// The most notifications a plan built from this table can ever hold.
        ///
        /// Computed rather than assumed, because with a taper it is no longer
        /// <c>perDay x horizon</c> — and the number it is checked against is enforced by
        /// silence on iOS, so getting the arithmetic wrong here fails nothing and loses the
        /// back half of the schedule on one platform.
        /// </summary>
        public int MostPending => Pending(PerDay, HorizonDays, TaperAfterDays);

        static int Pending(int perDay, int horizon, int taperAfter)
        {
            int full = taperAfter < horizon ? taperAfter : horizon;
            return full * perDay + (horizon - full) * TaperPerDay;
        }

        /// <summary>
        /// How many days ahead the plan reaches.
        ///
        /// <para>
        /// <b>This is the number that has to survive the player it is for.</b> Notifications
        /// are armed when the app is backgrounded and nothing can run afterwards, so the
        /// horizon is exactly how long somebody may stay away and still be reminded — past it
        /// this scheme is simply silent, which is the one thing a server-side push could do
        /// that this cannot.
        /// </para>
        /// <para>
        /// <b>What bounds it is a ceiling enforced by silence.</b> iOS keeps the 64 soonest
        /// pending local notifications and drops the rest with no error, no callback and no
        /// log line, so <see cref="MostPending"/> has to stay under
        /// <see cref="NotificationWindow.MaxPending"/> with room to spare. A flat three a day
        /// spends that whole allowance in a week; tapering after
        /// <see cref="TaperAfterDays"/> spends it over three, at 21 + 14 = <b>35</b>.
        /// </para>
        /// </summary>
        public int HorizonDays { get; }

        public NotificationEntry Find(NotificationKind kind)
            => _byKind.TryGetValue(kind, out var entry) ? entry : null;

        // ------------------------------------------------------------- built in
        /// <summary>
        /// The slate that ships inside the build.
        ///
        /// <para>
        /// The shape is the design. <b>Everything above the line is news</b> — something
        /// changed while the player was away and it is waiting for them — and those carry
        /// short cooldowns because the thing they are about really did happen again.
        /// <b>Everything below it is an invitation</b>, true whenever anybody cares to say
        /// it, and those carry long cooldowns because a sentence that is always true gets
        /// stale the second time it is read.
        /// </para>
        /// <para>
        /// The one that is always true — <see cref="NotificationKind.GroveIdle"/> — is last
        /// on purpose and on a two-day cooldown, so it fills a gap rather than filling the
        /// week. That is what makes the daily count *vary*: on a quiet day the planner
        /// genuinely finds nothing for the third slot and leaves it empty.
        /// </para>
        /// </summary>
        public static NotificationTable Built { get; } = new NotificationTable(
            new[]
            {
                //                  kind                            slot                        pri  gap  on
                Row(NotificationKind.SeasonEnding, NotificationSlot.Evening,   100, 1),
                Row(NotificationKind.StreakRisk,   NotificationSlot.Evening,    90, 1),
                // Two days rather than one, and it is the only cooldown here chosen against
                // *staleness* rather than against frequency. A waiting chest really does go on
                // waiting — the sentence never stops being true — so at a one-day gap a player
                // who stays away gets the identical morning seven times. Alternating it with
                // the daily slate is what keeps the morning worth reading.
                Row(NotificationKind.ChestWaiting, NotificationSlot.Morning,    80, 2),
                Row(NotificationKind.TasksWeekly,  NotificationSlot.Morning,    70, 7),
                Row(NotificationKind.TasksDaily,   NotificationSlot.Morning,    60, 1),
                Row(NotificationKind.HeartsFull,   NotificationSlot.Afternoon,  50, 1),
                Row(NotificationKind.SeasonRungs,  NotificationSlot.Afternoon,  40, 2),
                // ------------------------------------------------------ invitations
                Row(NotificationKind.EndlessCall,  NotificationSlot.Evening,    30, 3),
                Row(NotificationKind.BoardClimb,   NotificationSlot.Evening,    20, 4),
                Row(NotificationKind.GroveIdle,    NotificationSlot.Any,        10, 2),
            },
            // 09:30, 13:30, 19:30 local. The morning one is after the commute rather than
            // during it, the afternoon one is the lull, and the evening one is before the
            // hour anybody would call late — see NotificationWindow for the floor and ceiling
            // these are held to.
            new[] { 9 * 60 + 30, 13 * 60 + 30, 19 * 60 + 30 },
            perDay: 3,
            horizonDays: 21,
            taperAfterDays: 7);

        static NotificationEntry Row(NotificationKind kind, NotificationSlot slot,
                                     int priority, int minDaysBetween)
            => new NotificationEntry(kind, slot, priority, minDaysBetween, true);

        // -------------------------------------------------------------- reading
        /// <summary>
        /// The table this build will use, replaced once content has been read.
        ///
        /// A static of its own rather than a field threaded through <c>ProgressionTable</c>,
        /// which is <c>WardStars</c>' shape and for its reason: nothing that asks
        /// <c>ProgressionTable</c> a question needs this, so putting it in that constructor
        /// would be one more argument on a signature seventeen long for the benefit of no
        /// caller.
        /// </summary>
        public static NotificationTable Active { get; private set; } = Built;

        /// <summary>
        /// Reads the <c>notifications</c> block, or leaves the built-in table standing.
        ///
        /// Handed the whole block rather than its rows — <c>WardCatalog.Resolve</c>'s shape —
        /// so a DTO built by hand, which is what every test and every offline caller writes,
        /// cannot be dereferenced through.
        /// </summary>
        public static NotificationTable Resolve(NotificationsDto dto, List<string> problems)
        {
            Active = Built;
            if (dto == null || !dto.IsAuthored) return Active;

            var faults = new List<string>();

            int perDay = dto.perDay > 0 ? dto.perDay : Built.PerDay;
            if (perDay > NotificationWindow.Slots)
                faults.Add($"notifications perDay is {perDay}, above the {NotificationWindow.Slots} " +
                           "slots a day holds");

            int horizon = dto.horizonDays > 0 ? dto.horizonDays : Built.HorizonDays;
            int taper = dto.taperAfterDays > 0 ? dto.taperAfterDays : Built.TaperAfterDays;

            int pending = Pending(perDay, horizon, taper);
            if (horizon < 1 || pending > NotificationWindow.MaxPending)
                faults.Add($"notifications horizonDays {horizon} at perDay {perDay} tapering " +
                           $"after day {taper} is {pending} pending, and iOS silently drops " +
                           $"past {NotificationWindow.MaxPending}");

            int[] hours = ReadHours(dto.hours, faults);

            var rows = new List<NotificationEntry>();
            var seen = new HashSet<NotificationKind>();

            foreach (var row in dto.entries ?? new NotificationEntryDto[0])
            {
                if (row == null) continue;

                var kind = NotificationKinds.Parse(row.kind);
                if (kind == NotificationKind.None) continue;   // a newer pack; drop by name

                if (!seen.Add(kind))
                {
                    faults.Add($"notifications lists '{row.kind}' twice; one cooldown cannot " +
                               "govern two rows");
                    continue;
                }

                var slot = ReadSlot(row.slot, row.kind, faults);
                rows.Add(new NotificationEntry(kind, slot, row.priority,
                                               row.minDaysBetween, !row.disabled));
            }

            bool anyLive = false;
            foreach (var row in rows) if (row.Enabled) { anyLive = true; break; }
            if (!anyLive)
                faults.Add("notifications block enables nothing; the built-in slate ships " +
                           "instead. To send nothing at all, the player's own switch is the " +
                           "control — see NotificationOptIn");

            if (faults.Count > 0)
            {
                problems?.AddRange(faults);
                return Active;
            }

            rows.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            Active = new NotificationTable(rows.ToArray(), hours, perDay, horizon, taper);
            return Active;
        }

        static int[] ReadHours(NotificationHoursDto dto, List<string> faults)
        {
            if (dto == null || !dto.IsAuthored) return Built.Hours;

            var hours = new[]
            {
                dto.morning >= 0 ? dto.morning : Built.Hours[0],
                dto.afternoon >= 0 ? dto.afternoon : Built.Hours[1],
                dto.evening >= 0 ? dto.evening : Built.Hours[2],
            };

            for (int i = 0; i < hours.Length; i++)
            {
                if (hours[i] < NotificationWindow.EarliestMinute ||
                    hours[i] > NotificationWindow.LatestMinute)
                {
                    faults.Add($"notifications hour {hours[i]} is outside the waking day " +
                               $"({NotificationWindow.EarliestMinute}..{NotificationWindow.LatestMinute} " +
                               "minutes past local midnight)");
                }

                // Strictly rising, or two slots can land on one minute and the second is a
                // flam rather than a second reminder (invariant 37q, said about a day).
                if (i > 0 && hours[i] <= hours[i - 1])
                    faults.Add("notifications hours must rise through the day; " +
                               $"{hours[i]} does not follow {hours[i - 1]}");
            }

            return hours;
        }

        static NotificationSlot ReadSlot(string slot, string kind, List<string> faults)
        {
            if (string.IsNullOrEmpty(slot)) return NotificationSlot.Any;

            switch (slot)
            {
                case "any":       return NotificationSlot.Any;
                case "morning":   return NotificationSlot.Morning;
                case "afternoon": return NotificationSlot.Afternoon;
                case "evening":   return NotificationSlot.Evening;
                default:
                    // A fault rather than a fallback: a mistyped slot silently becoming "any"
                    // is a reminder that moved to a different time of day and nothing said so.
                    faults.Add($"notifications row '{kind}' names slot '{slot}', which is not " +
                               "one of any/morning/afternoon/evening");
                    return NotificationSlot.Any;
            }
        }
    }
}
