namespace GlimmerGrove.Events
{
    /// <summary>How far up one track a player is, and how much of it is still waiting.</summary>
    public readonly struct SeasonTrackProgress
    {
        /// <summary>Rungs reached by play, claimed or not, whoever may take them.</summary>
        public readonly int Reached;

        /// <summary>Rungs reached and claimed.</summary>
        public readonly int Claimed;

        /// <summary>
        /// Whether this account may take anything off this track at all.
        ///
        /// Always true of the free track. True of the paid one only for somebody holding the
        /// season's pass — see <see cref="EventLedger.Opens"/>, which is where that is decided
        /// for every reading in the game.
        /// </summary>
        public readonly bool Open;

        public SeasonTrackProgress(int reached, int claimed, bool open)
        {
            Reached = reached;
            Claimed = claimed;
            Open = open;
        }

        /// <summary>
        /// Rungs a tap would hand over right now. What a badge counts.
        ///
        /// <para>
        /// <b>Derived rather than counted, so it cannot drift from the other two.</b> The
        /// rungs are sorted by goal and the floor is the goal of the highest one taken, so
        /// every reached rung is either at or below the floor (claimed) or above it (waiting)
        /// and the subtraction is exact.
        /// </para>
        /// <para>
        /// <b>Nought on a track this account cannot claim from</b>, which is the whole reason
        /// <see cref="Open"/> exists. A count of rungs nobody may take is not a count of
        /// anything a player can act on: it lit the hub's event box, put a number on its
        /// corner and told the player to collect something no screen would hand over, and it
        /// kept <see cref="GroveEvents.Featured"/> pointing at a season that would never
        /// settle.
        /// </para>
        /// </summary>
        public int Waiting => Open ? Reached - Claimed : 0;

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
        ///
        /// A track this account cannot claim from contributes nothing, so for a player without
        /// the pass this is the free column alone — see <see cref="SeasonTrackProgress.Waiting"/>.
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
    /// The arithmetic of a season: what a mark count, two claim floors and a pass add up to.
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
    /// <b>Whether the paid track is open is an argument like the floors are, and every
    /// reading takes it.</b> It was once asked only at the moment of claiming, so
    /// <see cref="ProgressOf"/> reported rungs waiting on a track the same type would refuse
    /// to pay from — two answers to one question, and the drawing read the wrong one. There is
    /// one predicate now (<see cref="Opens"/>) and both entry points run it.
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
        /// Whether a track is open to an account, which for the paid one means holding the
        /// season's pass.
        ///
        /// <b>The one place that decides it</b> (invariant 47n). Both readings below run it, and
        /// <see cref="SeasonLedger"/> supplies the entitlement rather than asking the question
        /// itself, so "can this be taken" has exactly one answer however it is reached.
        /// </summary>
        public static bool Opens(SeasonTrack track, bool passHeld)
            => track != SeasonTrack.Pass || passHeld;

        /// <summary>
        /// The whole state of one season for one player.
        /// </summary>
        /// <param name="marks">Marks grown inside the window.</param>
        /// <param name="freeFloor">The largest free-track goal already claimed.</param>
        /// <param name="passFloor">The largest pass-track goal already claimed.</param>
        /// <param name="passHeld">Whether this account bought the season's pass.</param>
        public static EventProgress ProgressOf(GroveEvent season, int marks,
                                               int freeFloor, int passFloor, bool passHeld)
        {
            if (season == null || !season.IsValid) return EventProgress.None;

            int grown = marks < 0 ? 0 : marks;
            int free = Clamp(freeFloor, grown);
            int pass = Clamp(passFloor, grown);

            int rungs = 0, nextGoal = 0, lastGoal = 0;
            int freeReached = 0, freeClaimed = 0;
            int passReached = 0, passClaimed = 0;

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
                    if (rung.Goal <= free) freeClaimed++;
                }

                if (rung.Pays(SeasonTrack.Pass))
                {
                    passReached++;
                    if (rung.Goal <= pass) passClaimed++;
                }
            }

            int toNext = nextGoal == 0 ? 0 : nextGoal - grown;

            return new EventProgress(
                grown, rungs, toNext < 0 ? 0 : toNext, nextGoal, lastGoal,
                new SeasonTrackProgress(freeReached, freeClaimed,
                                        Opens(SeasonTrack.Free, passHeld)),
                new SeasonTrackProgress(passReached, passClaimed,
                                        Opens(SeasonTrack.Pass, passHeld)));
        }

        /// <summary>
        /// Whether tapping this rung on this track would hand something over: the track is
        /// open to this account, the rung is reached by play, it is paid on that track, and it
        /// has not already been taken.
        /// </summary>
        public static bool IsClaimable(GroveEvent season, EventMilestone rung, SeasonTrack track,
                                       int marks, int floor, bool passHeld)
        {
            if (season == null || !season.IsValid) return false;
            if (!Opens(track, passHeld)) return false;
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
