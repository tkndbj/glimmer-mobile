using System;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Progression
{
    /// <summary>
    /// What one glade node on the map draws off the save: its star skin, the record beside it,
    /// and whether it is open. Compared before a redraw so a sync that changed nothing a node
    /// shows costs that node nothing.
    ///
    /// <para>
    /// <b>Why a reading rather than a list of fields to watch.</b> A node's <c>unlocked</c> is
    /// not a field of the save - it is <see cref="LevelUnlock.IsUnlocked"/>, which reads the
    /// glade behind it, the stars of the chapter behind that, and the keeper wall, which is
    /// derived from every XP source there is. Deciding "did anything this node reads move" by
    /// naming those fields would go stale the day an XP source is added (which has happened
    /// twice). Asking the same rule the node is drawn from, and comparing the answers, cannot.
    /// This is the shape <c>EndlessHub.Reading</c> already gave the Infinite hub for the same
    /// complaint: a map "reloading" under a player who was only looking at it.
    /// </para>
    /// <para>
    /// <b>Add a field here the day a node draws a fourth fact off the save</b>, or that fact
    /// will stop repainting after a sync. Content (the number, the seat, the accent) is not
    /// read here because content cannot change while the screen is open.
    /// </para>
    /// </summary>
    public readonly struct GladeReading : IEquatable<GladeReading>
    {
        public readonly int Stars;
        public readonly int BestMoves;
        public readonly bool Unlocked;

        public GladeReading(int stars, int bestMoves, bool unlocked)
        {
            Stars = stars;
            BestMoves = bestMoves;
            Unlocked = unlocked;
        }

        /// <summary>What the node for <paramref name="id"/> would draw right now.</summary>
        public static GladeReading Of(CatalogIndex index, LevelId id)
            => new GladeReading(PlayerProgress.Stars(id), PlayerProgress.BestMoves(id),
                                LevelUnlock.IsUnlocked(index, id));

        public bool Equals(GladeReading other)
            => Stars == other.Stars && BestMoves == other.BestMoves && Unlocked == other.Unlocked;

        public override bool Equals(object obj) => obj is GladeReading r && Equals(r);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Stars;
                h = h * 31 + BestMoves;
                return h * 31 + (Unlocked ? 1 : 0);
            }
        }

        public override string ToString()
            => $"{Stars} star(s), {BestMoves} move(s), {(Unlocked ? "open" : "locked")}";
    }

    /// <summary>
    /// What the node capping a chapter's chain draws off the save: whether the next chapter can
    /// be entered, and - when it cannot - the figures its plate prints, which are exactly the
    /// ones <c>ChapterGate</c> hands the sentence. A chapter with nothing after it reads as a
    /// constant, because its plate is one.
    ///
    /// <para>
    /// The keeper level itself is deliberately not part of the reading: the plate prints the
    /// level <em>required</em> when the wall is shut, not the level held, so a player gaining
    /// XP under a shut wall changes nothing the teaser shows until the wall opens - at which
    /// point <see cref="NeedsLevel"/> flips, and that is read.
    /// </para>
    /// </summary>
    public readonly struct ChapterEndReading : IEquatable<ChapterEndReading>
    {
        public readonly bool Onward;
        public readonly bool Reachable;
        public readonly bool NeedsLevel;
        public readonly int RequiredLevel;
        public readonly bool StarsGated;
        public readonly int Required;
        public readonly int Held;

        public ChapterEndReading(bool onward, bool reachable, bool needsLevel, int requiredLevel,
                                 bool starsGated, int required, int held)
        {
            Onward = onward;
            Reachable = reachable;
            NeedsLevel = needsLevel;
            RequiredLevel = requiredLevel;
            StarsGated = starsGated;
            Required = required;
            Held = held;
        }

        /// <summary>A chain with no chapter after it: the "more soon" seal.</summary>
        public static readonly ChapterEndReading End = new ChapterEndReading(false, false, false, 0, false, 0, 0);

        /// <summary>What the teaser after <paramref name="chapter"/> would draw right now.</summary>
        public static ChapterEndReading Of(CatalogIndex index, ChapterId chapter)
        {
            var next = LevelUnlock.ChapterAfter(index, chapter);
            if (next == null) return End;

            bool reachable = LevelUnlock.IsChapterUnlocked(index, next.Id);
            if (reachable) return new ChapterEndReading(true, true, false, 0, false, 0, 0);

            var gate = LevelUnlock.GateFor(index, next.Id);
            return new ChapterEndReading(true, false, gate.NeedsLevel, gate.RequiredLevel,
                                         gate.Required > 0 && gate.Behind.IsValid, gate.Required, gate.Held);
        }

        public bool Equals(ChapterEndReading other)
            => Onward == other.Onward && Reachable == other.Reachable
            && NeedsLevel == other.NeedsLevel && RequiredLevel == other.RequiredLevel
            && StarsGated == other.StarsGated && Required == other.Required && Held == other.Held;

        public override bool Equals(object obj) => obj is ChapterEndReading r && Equals(r);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Onward ? 1 : 0;
                h = h * 31 + (Reachable ? 1 : 0);
                h = h * 31 + (NeedsLevel ? 1 : 0);
                h = h * 31 + RequiredLevel;
                h = h * 31 + (StarsGated ? 1 : 0);
                h = h * 31 + Required;
                return h * 31 + Held;
            }
        }
    }

    /// <summary>
    /// Whether a piece drawn from <paramref name="drawn"/> has to be drawn again for
    /// <paramref name="now"/>: always when nothing is standing (a piece that is gone is never
    /// "unchanged"), otherwise exactly when the reading moved. One rule for every reading on
    /// the map, so the hub, the nodes and the teaser cannot disagree about what a redraw is.
    /// </summary>
    public static class MapRedraw
    {
        public static bool Needed<T>(bool standing, T drawn, T now) where T : struct, IEquatable<T>
            => !standing || !drawn.Equals(now);
    }
}
