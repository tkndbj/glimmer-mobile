using System;

namespace GlimmerGrove.Challenges
{
    /// <summary>
    /// The shape of <c>Content/challenges.json</c>, exactly as <c>JsonUtility</c> reads it.
    ///
    /// <para>
    /// <b>Its own file rather than a block of <c>progression.json</c>, deliberately.</b> The
    /// owner's instruction is that a challenge may be tuned, rewritten or withdrawn without the
    /// core game moving by a byte, and the cheapest way to make that true is for the two to be
    /// different files with different schema versions read by different readers: nothing in
    /// <c>ProgressionTable</c> knows this file exists, nothing here reads that one, and a re-seed
    /// of the reward table carries none of this (nothing here reaches a server at all).
    /// </para>
    /// <para>
    /// <b>One flat row for every genre</b>, because <c>JsonUtility</c> has no polymorphism: a
    /// row carries every field any genre could want and each genre reads the ones it needs. The
    /// reader (<see cref="ChallengeTable"/>) refuses a row that leaves out a field its genre
    /// requires, so a Sokoban with no gems is an error at read rather than an empty board.
    /// </para>
    /// <para>
    /// <b>Never test a class-typed field for null</b> — a <c>[Serializable]</c> field is never
    /// null after <c>JsonUtility</c>. Arrays are tested for length and strings for emptiness.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class ChallengeTableDto
    {
        public int schemaVersion;

        /// <summary>The one fixed line every challenge is fought on. See <see cref="ChallengeLineDto"/>.</summary>
        public ChallengeLineDto line;

        public ChallengeDto[] challenges;
    }

    /// <summary>
    /// The turrets, and they are the same four on every challenge by the owner's instruction:
    /// a challenge never reads the player's loadout, the shelf, a star ladder or a ward ledger.
    /// </summary>
    [Serializable]
    public sealed class ChallengeLineDto
    {
        /// <summary>What one bolt takes off a raider.</summary>
        public int damage;

        /// <summary>How many blows a ward stands before it falls.</summary>
        public int health;

        /// <summary>What a raider standing at the line takes off a ward each turn.</summary>
        public int strike;
    }

    [Serializable]
    public sealed class ChallengeDto
    {
        /// <summary>Permanent (invariant 1's shape): its loc keys derive from it.</summary>
        public string id;

        /// <summary>One of <see cref="ChallengeGenres"/>' spellings.</summary>
        public string genre;

        /// <summary>Seeds every draw the genre makes. Two devices on one row deal the same board.</summary>
        public int seed;

        public int width;
        public int height;

        /// <summary>The board, one string per row, in the genre's own alphabet.</summary>
        public string[] rows;

        /// <summary>Sokoban's second layer: where the gems start. Same size as <see cref="rows"/>.</summary>
        public string[] gems;

        /// <summary>Pipes: the colour entering each column from the top, or <c>.</c>.</summary>
        public string sources;

        /// <summary>Pipes: the turret each column feeds at the bottom, or <c>.</c>.</summary>
        public string sinks;

        /// <summary>Merge: the rank to reach.</summary>
        public int target;

        /// <summary>How many steps a raider walks from the top of the hill to the line.</summary>
        public int hill;

        /// <summary>
        /// The waves, one string each: <c>"&lt;turn&gt; &lt;colour&gt;&lt;health&gt; ..."</c>, so
        /// <c>"3 r2 g2"</c> musters a red and a green raider of two health after the third move.
        /// </summary>
        public string[] waves;

        /// <summary>How many bolts one unit of the puzzle is worth. Nought reads as one.</summary>
        public int bolts;
    }
}
