using GlimmerGrove.Homestead;

namespace GlimmerGrove.Social
{
    /// <summary>
    /// Which board a grove is ranked on, and it is the star count the player already has.
    ///
    /// <para>
    /// <b>A league is not a new ladder — it is the one on the grove screen.</b> The obvious
    /// implementation invents score bands, which means a second tuning table that has to be
    /// retuned every time the catalog grows, kept in step with <c>GroveScoreTable</c> by
    /// hand, and explained to the player as a thing distinct from the stars over their own
    /// grove. Deriving it from <see cref="GroveScoreTable.StarsFor"/> instead costs no
    /// content, no validation and no explanation: a player in the three-star league is a
    /// player wearing three stars, and a drop that retunes the ladder moves both at once.
    /// That is invariant 16g's argument applied one level up, and the same reason
    /// <c>RankTier</c> owns the map's band floor rather than the victory panel repeating it.
    /// </para>
    /// <para>
    /// <b>Why leagues at all.</b> One worldwide list is unreachable for all but a few
    /// thousand accounts, so it motivates nobody and demoralises the rest — and maintaining
    /// an exact global rank is a write on a path that must stay cheap forever. A band a
    /// player can actually move within, plus a percentile read off a published distribution,
    /// answers "where do I stand" in two documents and O(1) reads at any player count. See
    /// <see cref="GroveRanks"/> for the percentile half.
    /// </para>
    /// <para>
    /// <b>The id is permanent and the server writes it.</b> <c>l0</c>..<c>l8</c> keys a
    /// published board document, so invariant 1 applies to it in full: renaming one orphans
    /// whatever the last job wrote. It is deliberately not the star count spelled as a bare
    /// integer, because a document path of <c>0</c> is indistinguishable from an absent one.
    /// </para>
    /// </summary>
    public static class GroveLeague
    {
        /// <summary>
        /// Most leagues there can ever be: one per rung of the longest legal ladder, plus
        /// the league below the first rung. Pinned to <see cref="GroveScoreTable.MaxStars"/>
        /// so a content change that lengthens the ladder cannot leave a league with no id
        /// and no name — the build gate refuses a longer ladder, and this is the reason.
        /// </summary>
        public const int Count = GroveScoreTable.MaxStars + 1;

        /// <summary>
        /// The permanent id of each league, indexed by star count.
        ///
        /// Written out rather than composed, because these key a document the server writes
        /// and invariant 6's argument about loc keys is the same argument: a string built by
        /// concatenation is a string no search can find and no validator can check.
        /// </summary>
        static readonly string[] Ids =
        {
            "l0", "l1", "l2", "l3", "l4", "l5", "l6", "l7", "l8",
        };

        /// <summary>
        /// The name of each league. Written out for invariant 6 — the build gate scans for
        /// key-shaped literals and cannot see one that was assembled at runtime.
        /// </summary>
        static readonly string[] NameKeys =
        {
            "ui.league.l0", "ui.league.l1", "ui.league.l2", "ui.league.l3", "ui.league.l4",
            "ui.league.l5", "ui.league.l6", "ui.league.l7", "ui.league.l8",
        };

        /// <summary>The league a grove wearing this many stars is ranked in.</summary>
        public static string IdFor(int stars)
            => Ids[stars < 0 ? 0 : stars >= Ids.Length ? Ids.Length - 1 : stars];

        /// <summary>The league a score falls in, against a ladder. The whole rule.</summary>
        public static string IdFor(long score, GroveScoreTable table)
            => IdFor((table ?? GroveScoreTable.Default).StarsFor(score));

        /// <summary>What to call a league on screen.</summary>
        public static string NameKey(int stars)
            => NameKeys[stars < 0 ? 0 : stars >= NameKeys.Length ? NameKeys.Length - 1 : stars];

        /// <summary>The star count an id names, or -1 when it names nothing.</summary>
        public static int StarsOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;

            for (int i = 0; i < Ids.Length; i++)
                if (string.Equals(Ids[i], id, System.StringComparison.Ordinal)) return i;

            return -1;
        }

        /// <summary>Whether this is a league this build knows how to draw.</summary>
        public static bool IsKnown(string id) => StarsOf(id) >= 0;

        /// <summary>Every league id, ascending. For a screen that draws the ladder.</summary>
        public static System.Collections.Generic.IReadOnlyList<string> All => Ids;
    }
}
