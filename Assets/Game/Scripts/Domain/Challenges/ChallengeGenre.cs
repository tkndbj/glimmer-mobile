namespace GlimmerGrove.Challenges
{
    /// <summary>
    /// The four puzzle genres a daily challenge can be, each fused with the ward line the same
    /// way the live mode fuses match-three with it: the puzzle move is the fuel, the turrets do
    /// the fighting.
    ///
    /// <para>
    /// <b>Three were built and withdrawn on 2026-09-22</b> after the owner played all seven:
    /// Sudoku, Minefield (minesweeper) and Stack (tetris). Their spellings (<c>sudoku</c>,
    /// <c>mines</c>, <c>tetris</c>) are refused at read like any unknown genre; nothing stores a
    /// challenge id, so nothing is spent.
    /// </para>
    ///
    /// <para>
    /// <b>A genre is code and a challenge names one</b> — MODES.md invariant 20, said of this
    /// smaller thing. Content can never add a way of playing; a row of <c>challenges.json</c>
    /// says which of these it is and hands the genre its board. Adding a genre is a build
    /// (<see cref="ChallengePuzzles"/> is the one registry), adding a challenge is a content push.
    /// </para>
    /// <para>
    /// <b>Parsed by name and refused by name</b> (invariant 5f): a genre this build has never
    /// heard of is an error at read, never a silent fall-through to the first one, because
    /// <c>JsonUtility</c> would otherwise hand a Sokoban's rows to the Pairs board.
    /// </para>
    /// </summary>
    public enum ChallengeGenre
    {
        /// <summary>Memory. Turn two gems; a pair fires its turret.</summary>
        Pairs,

        /// <summary>Pipe Mania. A colour connected from its source to its turret fires every turn.</summary>
        Pipes,

        /// <summary>2048. Two gems of a rank merge into the next, which fires the colour of the rank made.</summary>
        Merge,

        /// <summary>Sokoban. A gem pushed onto its pad arms that turret for as long as it stands there.</summary>
        Sokoban,
    }

    public static class ChallengeGenres
    {
        /// <summary>
        /// The content spelling of each genre. <b>The order is the enum's</b>, and both are
        /// permanent: a challenge id is not keyed on it, but a loc key and a render mirror both
        /// read the spelling.
        /// </summary>
        static readonly string[] Names = { "pairs", "pipes", "merge", "sokoban" };

        /// <summary>
        /// Spellings that were shipped and withdrawn, refused by name at read (invariant 5f) and
        /// listed in CLAUDE.md's spent table. A spelling is a wire name — a lifetime row in the
        /// save keeps counting under it after the genre is gone — so one may never come back
        /// meaning something else. <c>content.py</c> and the seeder hold the same list.
        /// </summary>
        public static readonly string[] Retired = { "sudoku", "mines", "tetris" };

        public static bool IsRetired(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < Retired.Length; i++)
                if (Retired[i] == name) return true;
            return false;
        }

        public static string NameOf(ChallengeGenre genre)
        {
            int i = (int)genre;
            return i >= 0 && i < Names.Length ? Names[i] : string.Empty;
        }

        /// <summary>Every genre, in enum order, for a registry test to walk.</summary>
        public static int Count => Names.Length;

        public static bool TryParse(string name, out ChallengeGenre genre)
        {
            genre = ChallengeGenre.Pairs;
            if (string.IsNullOrEmpty(name)) return false;

            for (int i = 0; i < Names.Length; i++)
            {
                if (Names[i] != name) continue;
                genre = (ChallengeGenre)i;
                return true;
            }

            return false;
        }
    }
}
