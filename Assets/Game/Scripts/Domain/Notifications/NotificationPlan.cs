using System.Collections.Generic;
using GlimmerGrove.Daily;

namespace GlimmerGrove.Notifications
{
    /// <summary>One thing the phone will say, and when. Local-time free: the instant is UTC.</summary>
    public readonly struct PlannedNotification
    {
        public readonly NotificationKind Kind;
        public readonly long FireUnix;
        public readonly int Id;

        public PlannedNotification(NotificationKind kind, long fireUnix, int id)
        {
            Kind = kind;
            FireUnix = fireUnix;
            Id = id;
        }

        public string TitleKey => NotificationKinds.TitleKey(Kind);
        public string BodyKey => NotificationKinds.BodyKey(Kind);
    }

    /// <summary>
    /// What this game will say over the next week, worked out in one pass with no clock, no
    /// save and no platform underneath it.
    ///
    /// <para>
    /// <b>The whole feature is this function.</b> Everything around it is plumbing: a
    /// snapshot in, a list of (kind, instant) out, and a platform seam that hands the list to
    /// Android or iOS. So the part that can actually be wrong — the pacing, the ordering, the
    /// cooldowns, the quiet hours, whether a sentence will still be true when it is read —
    /// is a pure function that the offline suite runs in full, on a machine with no phone
    /// attached. That is the same bargain <c>ReleaseWatch</c> and <c>SyncScheduler</c> strike
    /// and it is why they are the two policies in this project nobody is afraid of.
    /// </para>
    /// <para>
    /// <b>Slots are filled evening-first, and that is the design rather than an
    /// optimisation.</b> Ranking a whole day at once hands the best window — the evening — to
    /// whatever is left over after the morning has taken the best candidate, which is
    /// precisely backwards. Filling the evening first means the most urgent thing the planner
    /// holds goes where it will be read, and the morning gets the second best.
    /// </para>
    /// <para>
    /// <b>An empty slot is a feature.</b> There is no filler of last resort beyond
    /// <see cref="NotificationKind.GroveIdle"/> and its own cooldown, so a day on which
    /// nothing is genuinely true is a day with one or two notifications rather than three.
    /// "Two to three a day" is an outcome of the table, never a quota the table has to be
    /// kept consistent with — which means retuning the table cannot accidentally produce a
    /// day of six.
    /// </para>
    /// </summary>
    public static class NotificationPlan
    {
        /// <summary>The order slots are offered a candidate. See the class note.</summary>
        static readonly NotificationSlot[] FillOrder =
        {
            NotificationSlot.Evening, NotificationSlot.Morning, NotificationSlot.Afternoon,
        };

        /// <summary>
        /// The schedule, in the order it will fire.
        ///
        /// Deterministic in the strict sense — the same state and table give byte-identical
        /// output on every runtime — because the candidate walk is over an array in ranking
        /// order and the ties break on position. Nothing here enumerates a dictionary or a
        /// set, for the reason invariant 37w spells out about a contested cog.
        /// </summary>
        public static List<PlannedNotification> Build(NotificationState state, NotificationTable table)
        {
            var plan = new List<PlannedNotification>();
            if (table == null) return plan;

            // The last day each kind was used, so a cooldown is a subtraction. Indexed by the
            // enum's ordinal rather than kept in a dictionary, so the walk below cannot depend
            // on hash order; int.MinValue/2 so "never sent" is arithmetically far enough away
            // that no cooldown can reach it and nothing has to special-case it.
            var lastDay = new int[NotificationKinds.All.Length + 1];
            for (int i = 0; i < lastDay.Length; i++) lastDay[i] = int.MinValue / 2;

            long earliest = state.NowUnix + NotificationWindow.QuietSeconds;
            int localDay = LocalDayOf(state.NowUnix, state.UtcOffsetSeconds);

            for (int day = 0; day < table.HorizonDays; day++)
            {
                int sent = 0;

                // Per day rather than a constant, because the schedule thins past the taper —
                // three a day for the first week, one a night after that. See
                // NotificationTable.TaperAfterDays for why the reach matters more than the
                // rate once somebody has been away a fortnight.
                int allowed = table.PerDayOn(day);

                foreach (var slot in FillOrder)
                {
                    if (sent >= allowed) break;
                    if (plan.Count >= NotificationWindow.MaxPending) break;

                    long fire = InstantOf(localDay + day, slot, table.Hours, state.UtcOffsetSeconds);
                    if (fire < earliest) continue;

                    var pick = Best(state, table, slot, fire, day, lastDay);
                    if (pick == null) continue;

                    plan.Add(new PlannedNotification(pick.Kind, fire, IdOf(day, slot)));
                    lastDay[(int)pick.Kind] = day;
                    sent++;
                }

                if (plan.Count >= NotificationWindow.MaxPending) break;
            }

            plan.Sort((a, b) => a.FireUnix.CompareTo(b.FireUnix));
            return plan;
        }

        /// <summary>
        /// The best candidate for one slot: highest priority among the rows that are enabled,
        /// want this slot (or any), are off cooldown, and whose sentence is true at the instant
        /// it would be read.
        /// </summary>
        static NotificationEntry Best(NotificationState state, NotificationTable table,
                                      NotificationSlot slot, long fire, int day, int[] lastDay)
        {
            NotificationEntry best = null;

            foreach (var entry in table.Entries)
            {
                if (!entry.Enabled) continue;
                if (entry.Slot != slot && entry.Slot != NotificationSlot.Any) continue;
                if (day - lastDay[(int)entry.Kind] < entry.MinDaysBetween) continue;
                if (!state.IsTrueAt(entry.Kind, fire)) continue;

                // The table is sorted by priority, so the first match is the answer. The
                // comparison is kept anyway: `Entries` is public and a caller that built a
                // table by hand — every fixture does — would otherwise get whatever order it
                // happened to write, silently.
                if (best == null || entry.Priority > best.Priority) best = entry;
            }

            return best;
        }

        /// <summary>
        /// The UTC instant of one slot on one <em>local</em> day.
        ///
        /// Local days are counted in "local epoch space" — UTC seconds shifted by the offset —
        /// so the whole calculation is integer arithmetic on seconds and there is no
        /// <c>DateTime</c>, no timezone table and nothing that answers differently on Mono,
        /// .NET and IL2CPP. That matters here for the reason it matters everywhere in this
        /// project: a plan a fixture cannot reproduce exactly is a plan nobody can pin.
        /// </summary>
        public static long InstantOf(int localDay, NotificationSlot slot, int[] hours, int offsetSeconds)
        {
            int minute = NotificationWindow.MinuteOf(slot, hours);
            if (minute < 0) return long.MaxValue;

            return (long)localDay * DailyRules.SecondsPerDay + minute * 60L - offsetSeconds;
        }

        /// <summary>Whole local days since the epoch. Never negative.</summary>
        public static int LocalDayOf(long unix, int offsetSeconds)
        {
            long local = unix + offsetSeconds;
            return local <= 0L ? 0 : (int)(local / DailyRules.SecondsPerDay);
        }

        /// <summary>
        /// A stable id per (day, slot).
        ///
        /// Stable rather than generated because a schedule is always <em>replaced</em> — every
        /// arm cancels the lot and writes the plan again — and an id that meant something
        /// different each time would make a log of what a device actually holds unreadable.
        /// It is not used to reconcile anything: reconciling would need the previous plan,
        /// which is state, and this feature deliberately stores none.
        /// </summary>
        public static int IdOf(int day, NotificationSlot slot) => day * 10 + (int)slot;
    }
}
