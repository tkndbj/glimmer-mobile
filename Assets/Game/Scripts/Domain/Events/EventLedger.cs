namespace GlimmerGrove.Events
{
    /// <summary>How far up one track a player is, and how much of it is still waiting.</summary>
    public readonly struct SeasonTrackProgress
    {
        /// <summary>Rungs reached by play, claimed or not.</summary>
        public readonly int Reached;

        /// <summary>Rungs reached and claimed.</summary>
        public readonly int Claimed;

        /// <summary>Rungs reached and not yet claimed. What a badge counts.</summary>
        public readonly int Waiting;

        public SeasonTrackProgress(int reached, int claimed, int waiting)
        {
            Reached = reached;
            Claimed = claimed;
            Waiting = waiting;
        }

        public bool AnyWaiting => Waiting > 0;
    }

    /// <summary>How far through one season a player is, across both tracks.</summary>
    public readonly struct EventProgress
    {
        /// <summary>Marks grown inside the window.</summary>
        public readonly int Marks;

        /// <summary>Rungs the mark count has reached, whatever either track has paid.</summary>
        public readonly int Rungs;

        /// <summary>Marks still needed for the next rung, or 0 once the ladder is topped.</summary>
        public readonly int ToNext;

        /// <summary>The goal of the next rung, or 0 once the ladder is topped.</summary>
        public readonly int NextGoal;

        /// <summary>The goal of the rung below, so a bar can be drawn between two rungs.</summary>
        public readonly int LastGoal;

        public readonly SeasonTrackProgress Free;
        public readonly SeasonTrackProgress Pass;

        public EventProgress(int marks, int rungs, int toNext, int nextGoal, int lastGoal,
                             SeasonTrackProgress free, SeasonTrackProgress pass)
        {
            Marks = marks;
            Rungs = rungs;
            ToNext = toNext;
            NextGoal = nextGoal;
            LastGoal = lastGoal;
            Free = free;
            Pass = pass;
        }

        public static readonly EventProgress None = default;

        public bool IsComplete => NextGoal == 0;

        public SeasonTrackProgress On(SeasonTrack track)
            => track == SeasonTrack.Pass ? Pass : Free;

        /// <summary>
        /// Rungs waiting on <em>either</em> track. What the hub's badge counts, and what keeps
        /// a closed season's box on the hub.
        /// </summary>
        public int Waiting => Free.Waiting + Pass.Waiting;

        public bool AnyWaiting => Waiting > 0;

        /// <summary>
        /// How far along the run between the last rung and the next, for a bar. One when the
        /// ladder is topped, so a finished track draws full rather than empty.
        /// </summary>
        public float ToNext01
        {
            get
            {
                if (NextGoal <= 0) return 1f;

                int span = NextGoal - LastGoal;
                if (span <= 0) return 1f;

                float done = (Marks - LastGoal) / (float)span;
                return done < 0f ? 0f : done > 1f ? 1f : done;
            }
        }
    }

    /// <summary>
    /// The arithmetic of a season: what a mark count and two claim floors add up to.
    ///
    /// <para>
    /// <b>A pure function of its arguments, and that is the point.</b> Nothing here reads the
    /// live save, the live catalog or the clock, so the whole of a season's reward reasoning
    /// can be run against a table — which is what <c>MarkVectorTests</c> and the shared
    /// vectors do on both sides of the client/server pair. <see cref="SeasonLedger"/> is the
    /// one type that hands it the live state.
    /// </para>
    /// <para>
    /// <b>Each track carries its own floor, and both are clamped to the marks actually
    /// grown before they are used.</b> That clamp is the whole of the security property on
    /// the client side: the floors are written by the client, and clamping them here means
    /// the most an edited one can do is take early what the season was going to pay anyway.
    /// The server applies its own, stronger bound — a rung is paid once per account for the
    /// life of the season, and the pass track additionally needs a receipt it verified —
    /// see <c>firebase/functions/src/season.ts</c> and invariant 13.
    /// </para>
    /// <para>
    /// Rungs are assumed sorted by goal, which the reader guarantees: an out-of-order ladder
    /// is refused there rather than sorted here, because a ladder whose rungs were silently
    /// reordered is a ladder paying different rewards than the one that was authored.
    /// </para>
    /// </summary>
    public static class EventLedger
    {
        /// <summary>
        /// The whole state of one season for one player.
        /// </summary>
        /// <param name="marks">Marks grown inside the window.</param>
        /// <param name="freeFloor">The largest free-track goal already claimed.</param>
        /// <param name="passFloor">The largest pass-track goal already claimed.</param>
        public static EventProgress ProgressOf(GroveEvent season, int marks,
                                               int freeFloor, int passFloor)
        {
            if (season == null || !season.IsValid) return EventProgress.None;

            int grown = marks < 0 ? 0 : marks;
            int free = Clamp(freeFloor, grown);
            int pass = Clamp(passFloor, grown);

            int rungs = 0, nextGoal = 0, lastGoal = 0;
            int freeReached = 0, freeClaimed = 0, freeWaiting = 0;
            int passReached = 0, passClaimed = 0, passWaiting = 0;

            for (int i = 0; i < season.Milestones.Count; i++)
            {
                var rung = season.Milestones[i];

                if (grown < rung.Goal)
                {
                    nextGoal = rung.Goal;
                    break;
                }

                rungs++;
                lastGoal = rung.Goal;

                if (rung.Pays(SeasonTrack.Free))
                {
                    freeReached++;
                    if (rung.Goal <= free) freeClaimed++; else freeWaiting++;
                }

                if (rung.Pays(SeasonTrack.Pass))
                {
                    passReached++;
                    if (rung.Goal <= pass) passClaimed++; else passWaiting++;
                }
            }

            int toNext = nextGoal == 0 ? 0 : nextGoal - grown;

            return new EventProgress(
                grown, rungs, toNext < 0 ? 0 : toNext, nextGoal, lastGoal,
                new SeasonTrackProgress(freeReached, freeClaimed, freeWaiting),
                new SeasonTrackProgress(passReached, passClaimed, passWaiting));
        }

        /// <summary>
        /// Whether tapping this rung on this track would hand something over: reached by
        /// play, paid on that track, and not already claimed.
        /// </summary>
        public static bool IsClaimable(GroveEvent season, EventMilestone rung, SeasonTrack track,
                                       int marks, int floor)
        {
            if (season == null || !season.IsValid) return false;
            if (!rung.Pays(track)) return false;
            if (marks < rung.Goal) return false;

            return rung.Goal > Clamp(floor, marks);
        }

        /// <summary>
        /// A floor, clamped to what was actually grown.
        ///
        /// <b>Never trusted above the marks.</b> A floor is a record of what has been asked
        /// for, and one that outruns the play behind it is either a merge from a device that
        /// was further along on a season it also has fewer marks for — impossible, since both
        /// only rise and both merge by <c>max</c> — or an edited file. Either way the honest
        /// reading is the smaller.
        /// </summary>
        static int Clamp(int floor, int marks)
            => floor < 0 ? 0 : floor > marks ? marks : floor;
    }
}
