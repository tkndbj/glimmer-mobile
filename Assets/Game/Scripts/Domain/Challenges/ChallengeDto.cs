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
    /// of the reward table carries none of this.
    /// </para>
    /// <para>
    /// <b>What the server is told</b> (invariant 56g). The seeder reads this file and publishes
    /// only the figures a claim is priced against — the genre spellings, the allowance, the deal
    /// rows and the two reward rates — as the <c>challenges</c> block of <c>config/progression</c>.
    /// The boards never leave the device; a level is content, not a fact the server needs.
    /// </para>
    /// <para>
    /// <b>One flat row for every genre</b>, because <c>JsonUtility</c> has no polymorphism: a
    /// row carries every field any genre could want and each genre reads the ones it needs. The
    /// reader (<see cref="ChallengeTable"/>) refuses a row that leaves out a field its genre
    /// requires, so a Sokoban with no gems is an error at read rather than an empty board.
    /// </para>
    /// <para>
    /// <b>Never test a class-typed field for null</b> — a <c>[Serializable]</c> field is never
    /// null after <c>JsonUtility</c>. Arrays are tested for length and strings for emptiness;
    /// an unwritten number reads as nought, so every block below has a "not written" shape a
    /// real one cannot take (<c>IsAuthored</c>).
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class ChallengeTableDto
    {
        public int schemaVersion;

        /// <summary>The one fixed line every challenge is fought on. See <see cref="ChallengeLineDto"/>.</summary>
        public ChallengeLineDto line;

        /// <summary>How many plays a genre allows a day before a deal is needed. See <see cref="ChallengeAllowanceDto"/>.</summary>
        public ChallengeAllowanceDto allowance;

        /// <summary>The deals, cheapest first. See <see cref="ChallengeTierDto"/>.</summary>
        public ChallengeTierDto[] tiers;

        /// <summary>What a cleared level pays. See <see cref="ChallengeRewardDto"/>.</summary>
        public ChallengeRewardDto rewards;

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

    /// <summary>
    /// The free allowance: how many plays of <em>each</em> genre a day cost nothing.
    ///
    /// <b>A play is an attempt, spent when the board is dealt</b> — a win moves the player to
    /// the next level of the day's sequence and a loss lets them try the same one again, and
    /// either way one play is gone. Spent at the deal rather than at the ending, or leaving a
    /// losing board before it lost would be a free retry for ever.
    /// </summary>
    [Serializable]
    public sealed class ChallengeAllowanceDto
    {
        /// <summary>Plays of each genre a day. Nought reads as unwritten and takes the built-in figure.</summary>
        public int freePlays;

        public bool IsAuthored => freePlays > 0;
    }

    /// <summary>
    /// One deal: a window of days during which every genre allows more plays a day.
    ///
    /// <para>
    /// <b>Bought with gems, so it is an ordinary spend</b> (invariant 18) — under a derived id
    /// the server recognises and prices against this row (<c>SpendEntry.ChallengeTierId</c>,
    /// the season pass's shape, 47e). The <em>server's</em> copy of the entitlement is what a
    /// coin claim is bounded by; the client's copy draws the page and gates nothing that pays.
    /// </para>
    /// <para>
    /// <b>The id is permanent</b> (invariant 1's shape): it names a spend id, a wallet field and
    /// a loc key (<c>challenge.tier.{id}.name</c>). Retire a deal by removing the row; never
    /// re-mint its id for a different deal.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class ChallengeTierDto
    {
        public string id;

        /// <summary>The price, in gems.</summary>
        public int gems;

        /// <summary>Plays of each genre a day while the deal runs. Must exceed the free figure.</summary>
        public int plays;

        /// <summary>How many days the deal runs from the day it is bought, that day included.</summary>
        public int days;
    }

    /// <summary>
    /// What clearing a level pays, in credits and in XP.
    ///
    /// <para>
    /// <b>The two are paid in opposite shapes</b>, which is invariant 9f's sentence said of a
    /// puzzle. XP is derived from a lifetime tally of clears (<see cref="ChallengeRewardRule.XpFor"/>,
    /// invariant 9d's shape) and needs no claim; credits cannot copy that, so a cleared level
    /// raises a claim the server prices against this rate and bounds by the day's allowance.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class ChallengeRewardDto
    {
        /// <summary>Credits a cleared level pays. Nought withdraws the payment.</summary>
        public int coins;

        /// <summary>XP a cleared level pays. Nought withdraws the payment.</summary>
        public int xp;

        /// <summary>The most lifetime clears ever paid for. Nought reads as unwritten and takes the built-in figure.</summary>
        public int maxClears;

        /// <summary>
        /// Whether the block was written at all. A block of three noughts is unauthored rather
        /// than "pays nothing" — withdrawing a payment is <c>coins: 0</c> beside a written
        /// <c>maxClears</c>, so the intent is visible in a diff.
        /// </summary>
        public bool IsAuthored => coins > 0 || xp > 0 || maxClears > 0;
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

        /// <summary>
        /// <b>Retired with the pipes genre (2026-09-23) and refused by name when written</b>
        /// (invariant 5f): <c>JsonUtility</c> drops a field it does not know without a word, so
        /// the two stay declared and the reader refuses a row that carries either.
        /// </summary>
        public string sources;

        /// <summary>Retired with <see cref="sources"/>; refused when written.</summary>
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
