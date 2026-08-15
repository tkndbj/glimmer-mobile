using System;
using System.Collections.Generic;
using GlimmerGrove.Content;

namespace GlimmerGrove.Events
{
    /// <summary>The ceilings that bound whatever a manifest asks an event to be.</summary>
    public static class EventRules
    {
        /// <summary>Milestones one event may carry. A track nobody can read is not a track.</summary>
        public const int MaxMilestones = 8;

        /// <summary>Most credits a single milestone may pay.</summary>
        public const int MaxMilestoneCredits = 5000;

        /// <summary>
        /// Longest window a content file may author, in days.
        ///
        /// Ninety. The point of an event is that it ends — a "limited time" that outlives
        /// the player's interest in it is just content with a countdown attached, and a
        /// window authored with a typo'd year would be exactly that.
        /// </summary>
        public const int MaxWindowDays = 90;

        public const long SecondsPerDay = 24L * 60L * 60L;
    }

    /// <summary>
    /// One rung of an event's reward track: finish this many of its glades, earn this.
    ///
    /// Credits only, and derived rather than granted — see <see cref="EventLedger"/> for
    /// why that is the only shape an event reward can safely take here.
    /// </summary>
    public readonly struct EventMilestone
    {
        /// <summary>How many of the event's glades must be finished inside the window.</summary>
        public readonly int Goal;

        public readonly int Credits;

        public EventMilestone(int goal, int credits)
        {
            Goal = goal < 1 ? 1 : goal;
            Credits = credits < 0 ? 0
                    : credits > EventRules.MaxMilestoneCredits ? EventRules.MaxMilestoneCredits
                    : credits;
        }

        public override string ToString() => $"{Goal} glades → {Credits} credits";
    }

    /// <summary>
    /// A time-boxed run at a set of glades, with a reward track.
    ///
    /// <para>
    /// <b>Why this exists.</b> The hardest retention number in a puzzle game is the
    /// thirty-day one, and it is not a design problem — it is a calendar problem. A player
    /// who has finished the chapters has nothing to come back for until the next drop, and
    /// a drop is weeks away. An event is the cheapest possible answer: it makes glades that
    /// already exist worth replaying for a fortnight, and it puts something on the calendar
    /// to come back to.
    /// </para>
    /// <para>
    /// <b>It is content, entirely.</b> An event is rows in <c>manifest.json</c> — a window,
    /// a list of level ids, a reward track — so running one is a content push, with no
    /// build, no store review and no code. That is the whole architectural claim, and it
    /// is the reason this type holds no behaviour beyond reading its own fields: the
    /// moment an event needs a code change to run, the cadence that makes events work
    /// stops being achievable.
    /// </para>
    /// <para>
    /// The window is in absolute Unix seconds, compared against <see cref="GameClock"/>
    /// rather than the device clock, because an event a player can enter by changing their
    /// date is an event that is always running.
    /// </para>
    /// </summary>
    public sealed class GroveEvent
    {
        public readonly string Id;
        public readonly long StartUnix;
        public readonly long EndUnix;
        public readonly IReadOnlyList<LevelId> Levels;
        public readonly IReadOnlyList<EventMilestone> Milestones;

        /// <summary>
        /// The mark this event wears, or empty for the default.
        ///
        /// A name the client resolves to something it can draw, never an art path — see
        /// <c>ManifestEventDto.icon</c> for why that distinction is the only one that
        /// survives contact with the asset pipeline. Domain deliberately does not know
        /// which names exist: that is a question about what has been drawn, and the answer
        /// lives with the drawing.
        /// </summary>
        public readonly string Icon;

        public GroveEvent(string id, long startUnix, long endUnix,
                          IReadOnlyList<LevelId> levels, IReadOnlyList<EventMilestone> milestones,
                          string icon = null)
        {
            Id = id ?? string.Empty;
            StartUnix = startUnix;
            EndUnix = endUnix;
            Levels = levels ?? Array.Empty<LevelId>();
            Milestones = milestones ?? Array.Empty<EventMilestone>();
            Icon = icon ?? string.Empty;
        }

        public bool IsValid => !string.IsNullOrEmpty(Id) && EndUnix > StartUnix && Levels.Count > 0;

        /// <summary>
        /// This event's name, derived from its id and not overridable.
        ///
        /// The same rule a level's name follows — invariant 5a — and for the same reason:
        /// it is what lets anything holding an event id name it without reading the
        /// manifest entry back.
        /// </summary>
        public string NameKey => DefaultNameKey(Id);

        public string BlurbKey => "ui.event." + Id + ".blurb";

        public static string DefaultNameKey(string id) => "ui.event." + id + ".name";

        public bool IsLiveAt(long now) => IsValid && now >= StartUnix && now < EndUnix;

        public bool HasEndedAt(long now) => now >= EndUnix;

        public bool StartsAfter(long now) => now < StartUnix;

        /// <summary>Seconds until it closes, or 0 once it has.</summary>
        public long SecondsLeftAt(long now)
        {
            long left = EndUnix - now;
            return left < 0 ? 0 : left;
        }

        /// <summary>The last milestone's goal — finishing the track. 0 when there is none.</summary>
        public int FinalGoal => Milestones.Count == 0 ? 0 : Milestones[Milestones.Count - 1].Goal;

        /// <summary>Everything the track pays if it is finished.</summary>
        public long TotalCredits
        {
            get
            {
                long total = 0;
                for (int i = 0; i < Milestones.Count; i++) total += Milestones[i].Credits;
                return total;
            }
        }

        public override string ToString()
            => $"{Id} ({Levels.Count} glades, {Milestones.Count} milestones)";
    }
}
