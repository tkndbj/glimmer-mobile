using System;
using System.Collections.Generic;
using GlimmerGrove.Tasks;

namespace GlimmerGrove.Events
{
    /// <summary>
    /// Which of a season's two tracks a rung belongs to.
    ///
    /// <para>
    /// The ids are <b>contract</b>: they go into the chest seed's subject and into the claim
    /// id the server parses (invariant 9c), so they are never renamed and never renumbered.
    /// </para>
    /// </summary>
    public enum SeasonTrack
    {
        /// <summary>Everybody's track. No purchase, no entitlement to check.</summary>
        Free = 0,

        /// <summary>The pass's track. Claimable only against a receipt the server verified.</summary>
        Pass = 1,
    }

    /// <summary>The two tracks, named once.</summary>
    public static class SeasonTracks
    {
        public static readonly SeasonTrack[] All = { SeasonTrack.Free, SeasonTrack.Pass };

        /// <summary>Contract with the server's <c>season.ts</c>. Never renamed.</summary>
        public static string Id(SeasonTrack track) => track == SeasonTrack.Pass ? "pass" : "free";

        /// <summary>Null for a name this build has never heard of, never a guess.</summary>
        public static SeasonTrack? Parse(string id)
        {
            if (string.Equals(id, "free", StringComparison.Ordinal)) return SeasonTrack.Free;
            if (string.Equals(id, "pass", StringComparison.Ordinal)) return SeasonTrack.Pass;
            return null;
        }
    }

    /// <summary>The ceilings that bound whatever a manifest asks a season to be.</summary>
    public static class EventRules
    {
        /// <summary>Rungs one season may carry. A track nobody can read is not a track.</summary>
        public const int MaxMilestones = 40;

        /// <summary>
        /// The largest mark count a rung may ask for.
        ///
        /// A sanity bound rather than a tuning one: a track whose last rung asks for more
        /// marks than a season can physically deal is a track with a rung nobody reaches,
        /// and that is a typo every time.
        /// </summary>
        public const int MaxGoal = 100000;

        /// <summary>
        /// Longest window a content file may author, in days.
        ///
        /// Ninety. The point of a season is that it ends — a "limited time" that outlives
        /// the player's interest in it is just content with a countdown attached, and a
        /// window authored with a typo'd year would be exactly that.
        /// </summary>
        public const int MaxWindowDays = 90;

        /// <summary>
        /// The dearest a pass may be, in gems.
        ///
        /// A sanity bound rather than a tuning one, and it is here rather than in the shop's
        /// ladder because a pass is not on a shelf: nothing else prices it, so nothing else
        /// would catch a typo'd nought.
        /// </summary>
        public const int MaxPassGems = 100000;

        public const long SecondsPerDay = 24L * 60L * 60L;
    }

    /// <summary>
    /// One rung of a season's ladder: how many marks it asks for, and the chest each track
    /// pays for reaching it.
    ///
    /// <para>
    /// <b>A rung names tiers, never amounts.</b> The same argument
    /// <see cref="TaskDefinition"/> makes, arriving in the one place it matters most: a
    /// season has eighty rewards on it, and eighty authored amounts would be eighty numbers
    /// nobody can disclose the odds of and eighty places a retune has to reach. A rung names
    /// a <see cref="ChestTier"/>, so one disclosure per tier is the odds for every rung that
    /// pays it (invariant 10b) and retuning a tier retunes the whole ladder at once.
    /// </para>
    /// <para>
    /// <b>The pass tier may be absent, and that is a real state rather than a degenerate
    /// one</b> — a season authored with no premium product has forty free rungs and no
    /// second column, and a rung on a season that <em>has</em> a product but leaves one of
    /// its own premium slots empty is refused by the reader, because a paid column with a
    /// hole in it is a player looking at what they bought and seeing nothing.
    /// </para>
    /// </summary>
    public readonly struct EventMilestone
    {
        /// <summary>How many marks this rung asks for. Always at least one.</summary>
        public readonly int Goal;

        /// <summary>The free track's tier id. Never empty on a rung the reader accepted.</summary>
        public readonly string FreeTier;

        /// <summary>The pass track's tier id, or empty on a season with no pass.</summary>
        public readonly string PassTier;

        public EventMilestone(int goal, string freeTier, string passTier)
        {
            Goal = goal < 1 ? 1 : goal > EventRules.MaxGoal ? EventRules.MaxGoal : goal;
            FreeTier = freeTier ?? string.Empty;
            PassTier = passTier ?? string.Empty;
        }

        /// <summary>The tier id one track pays, or empty when that track pays nothing here.</summary>
        public string TierIdOn(SeasonTrack track) => track == SeasonTrack.Pass ? PassTier : FreeTier;

        public bool Pays(SeasonTrack track) => !string.IsNullOrEmpty(TierIdOn(track));

        /// <summary>
        /// The chest one track pays, read from the <em>live</em> tier table, or null when
        /// this build's table has never heard of it.
        ///
        /// <para>
        /// <b>Resolved on every read rather than frozen when the calendar was parsed</b>, and
        /// that is invariant 9b made real: <c>progression.json</c> versions independently of
        /// the manifest, so a tier retuned by a progression push has to reach a season that
        /// was already resident. Freezing the object here would make a season carry whatever
        /// the tier meant on the morning it was read.
        /// </para>
        /// <para>
        /// Null is a real answer and the callers fail closed on it: a season naming a tier
        /// this build's table does not hold is a content pack that reached a client before
        /// the progression file did, which is a rung that cannot be claimed yet rather than a
        /// season that is broken. Both content gates <em>error</em> on it, which is where a
        /// typo is supposed to be caught.
        /// </para>
        /// </summary>
        public ChestTier TierOn(SeasonTrack track)
        {
            string id = TierIdOn(track);
            return string.IsNullOrEmpty(id) ? null : Progression.ProgressionRules.Table.Tasks.Tier(id);
        }

        public override string ToString()
            => $"{Goal} marks -> {(FreeTier.Length == 0 ? "-" : FreeTier)}"
             + $" / {(PassTier.Length == 0 ? "-" : PassTier)}";
    }

    /// <summary>
    /// A time-boxed season with a two-track reward ladder.
    ///
    /// <para>
    /// <b>Why this exists.</b> The hardest retention number in a puzzle game is the
    /// thirty-day one, and it is not a design problem — it is a calendar problem. A player
    /// who has finished the chapters has nothing to come back for until the next drop, and
    /// a drop is weeks away. A season is the cheapest possible answer: it gives what the
    /// player already does a second meaning for six weeks, and it puts something on the
    /// calendar to come back to.
    /// </para>
    /// <para>
    /// <b>It is content, entirely.</b> A season is rows in <c>manifest.json</c> — a window,
    /// a mark, a product and a ladder of tier names — so running one is a content push, with
    /// no build, no store review and no code. That is the whole architectural claim, and it
    /// is the reason this type holds no behaviour beyond reading its own fields: the moment
    /// a season needs a code change to run, the cadence that makes seasons work stops being
    /// achievable.
    /// </para>
    /// <para>
    /// <b>What it is graded on is marks, and that is the lesson the first one bought.</b>
    /// The original ladder counted <em>glades finished inside the window</em>, which tied a
    /// season to a named list of levels: the modes those levels belonged to were withdrawn
    /// and the season went with them, and it could never have worked for a returning player
    /// anyway, because a first clear happens once and a finite catalog runs out. Marks come
    /// from claimed chests (<see cref="ChestTier.Marks"/>), which every mode feeds and no
    /// mode owns — so a season now survives a mode being deleted, exactly as the star ledger
    /// does (invariant 20a).
    /// </para>
    /// <para>
    /// The window is in absolute Unix seconds, compared against <see cref="GameClock"/>
    /// rather than the device clock, because a season a player can enter by changing their
    /// date is a season that is always running.
    /// </para>
    /// </summary>
    public sealed class GroveEvent
    {
        public readonly string Id;
        public readonly long StartUnix;
        public readonly long EndUnix;
        public readonly IReadOnlyList<EventMilestone> Milestones;

        /// <summary>
        /// The mark this season wears, or empty for the default.
        ///
        /// A name the client resolves to something it can draw, never an art path — see
        /// <c>ManifestEventDto.icon</c> for why that distinction is the only one that
        /// survives contact with the asset pipeline. Domain deliberately does not know
        /// which names exist: that is a question about what has been drawn, and the answer
        /// lives with the drawing.
        /// </summary>
        public readonly string Icon;

        /// <summary>What the pass track costs in gems, or 0 for a season without one.</summary>
        public readonly int PassGems;

        public bool HasPremium => PassGems > 0;

        public GroveEvent(string id, long startUnix, long endUnix,
                          IReadOnlyList<EventMilestone> milestones,
                          string icon = null, int passGems = 0)
        {
            Id = id ?? string.Empty;
            StartUnix = startUnix;
            EndUnix = endUnix;
            Milestones = milestones ?? Array.Empty<EventMilestone>();
            Icon = icon ?? string.Empty;
            PassGems = passGems < 0 ? 0 : passGems > EventRules.MaxPassGems ? EventRules.MaxPassGems : passGems;
        }

        public bool IsValid => !string.IsNullOrEmpty(Id) && EndUnix > StartUnix && Milestones.Count > 0;

        /// <summary>
        /// This season's name, derived from its id and not overridable.
        ///
        /// The same rule a level's name follows — invariant 5a — and for the same reason:
        /// it is what lets anything holding a season id name it without reading the
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

        /// <summary>The last rung's goal — finishing the track. 0 when there is none.</summary>
        public int FinalGoal => Milestones.Count == 0 ? 0 : Milestones[Milestones.Count - 1].Goal;

        /// <summary>
        /// The rung at <paramref name="goal"/>, or a default one when no rung asks for
        /// exactly that.
        ///
        /// A lookup rather than an index, because a claim carries the <em>goal</em> and not
        /// the rung's position: positions move when a ladder is retuned and a goal does not,
        /// which is the same reason a level record keys on an id rather than on an order.
        /// </summary>
        public bool TryRung(int goal, out EventMilestone rung)
        {
            for (int i = 0; i < Milestones.Count; i++)
            {
                if (Milestones[i].Goal != goal) continue;
                rung = Milestones[i];
                return true;
            }

            rung = default;
            return false;
        }

        public override string ToString()
            => $"{Id} ({Milestones.Count} rungs, {FinalGoal} marks)";
    }
}
