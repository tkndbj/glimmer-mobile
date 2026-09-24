namespace GlimmerGrove.Challenges
{
    /// <summary>
    /// The one registry of genres: which class plays each, and which reads a row for faults.
    ///
    /// <b>A <c>switch</c> whose <c>default</c> is a refusal</b> (invariant 44e): a genre added to
    /// the enum and not here is an error at read, never the first genre played by accident.
    /// <c>ChallengeTests.EveryGenreIsRegistered</c> walks the enum against it.
    /// </summary>
    public static class ChallengePuzzles
    {
        public static IChallengePuzzle Build(ChallengeDefinition def)
        {
            switch (def.Genre)
            {
                case ChallengeGenre.Pairs: return new PairsPuzzle(def);
                case ChallengeGenre.Glade: return new GladePuzzle(def);
                case ChallengeGenre.Merge: return new MergePuzzle(def);
                case ChallengeGenre.Sokoban: return new SokobanPuzzle(def);
                default:
                    throw new System.InvalidOperationException(
                        $"genre '{def.Genre}' has no puzzle registered in ChallengePuzzles");
            }
        }

        /// <summary>Why a row cannot be played, or null. Asked once at read by <see cref="ChallengeTable"/>.</summary>
        public static string Fault(ChallengeDefinition def)
        {
            switch (def.Genre)
            {
                case ChallengeGenre.Pairs: return PairsPuzzle.Fault(def);
                case ChallengeGenre.Glade: return GladePuzzle.Fault(def);
                case ChallengeGenre.Merge: return MergePuzzle.Fault(def);
                case ChallengeGenre.Sokoban: return SokobanPuzzle.Fault(def);
                default: return $"genre '{def.Genre}' has no puzzle registered";
            }
        }

        /// <summary>Whether a genre is registered at all, for the enum walk.</summary>
        public static bool Knows(ChallengeGenre genre)
        {
            switch (genre)
            {
                case ChallengeGenre.Pairs:
                case ChallengeGenre.Glade:
                case ChallengeGenre.Merge:
                case ChallengeGenre.Sokoban:
                    return true;
                default:
                    return false;
            }
        }

        // ------------------------------------------------------------------ shared reading
        /// <summary>
        /// Every genre's first question of its rows: are there <c>height</c> of them, each
        /// <c>width</c> long. Null when they are.
        /// </summary>
        internal static string RowsFault(ChallengeDefinition def, string[] rows, string what)
        {
            if (rows == null || rows.Length != def.Height)
                return $"{what} must have {def.Height} row(s), has {(rows == null ? 0 : rows.Length)}";

            for (int y = 0; y < rows.Length; y++)
            {
                if (rows[y] == null || rows[y].Length != def.Width)
                    return $"{what} row {y} must be {def.Width} wide, is {(rows[y] == null ? 0 : rows[y].Length)}";
            }

            return null;
        }
    }
}
