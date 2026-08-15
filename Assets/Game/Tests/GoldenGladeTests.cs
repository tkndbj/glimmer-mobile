using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The golden bonus: the picker, the arithmetic, and the rules that keep it payable.
    ///
    /// <para>
    /// The expected percentages here are the same ones in
    /// <c>firebase/shared/reward-vectors.json</c>, spelled out inline. That duplication is
    /// deliberate. The vector test that reads the file needs <c>JsonUtility</c> and so only
    /// runs inside the Editor; these run anywhere, which means the generator is checked on
    /// every offline compile rather than only when somebody opens Unity. They were produced
    /// by a third implementation — a Python transcription of the same algorithm — so a
    /// green run here means C#, the vector file and that transcription all agree.
    /// </para>
    /// <para>
    /// If these go red, do not "fix" them. Every percentage is a glade somebody has already
    /// been paid for, and the server recomputes the same number from the same two facts —
    /// see invariant 9c. A change here that is not mirrored in
    /// <c>functions/src/progression.ts</c> makes a player's balance move after a sync.
    /// </para>
    /// </summary>
    public sealed class GoldenTests
    {
        /// <summary>
        /// The synthetic table the vectors are computed against. Separate from the shipped
        /// one on purpose: what is under contract is the generator, not this month's odds,
        /// and retuning the live table must not turn these red.
        /// </summary>
        static GoldenTable Vectors()
        {
            var problems = new List<string>();
            var table = GoldenTable.Resolve(new GoldenDto
            {
                bands = new[]
                {
                    new GoldenBandDto { percent = 100, weight = 7 },
                    new GoldenBandDto { percent = 150, weight = 2 },
                    new GoldenBandDto { percent = 400, weight = 1 },
                },
            }, problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems));
            return table;
        }

        static LevelId L(string id) => LevelId.Parse(id);

        // ------------------------------------------------------------- vectors
        [Test]
        public void ThePickerMatchesTheSharedVectors()
        {
            var table = Vectors();

            var expected = new (string player, string level, int percent)[]
            {
                ("uid_alpha", "plain_one", 100),
                ("uid_alpha", "plain_two", 150),
                ("uid_alpha", "generous_one", 100),
                ("uid_alpha", "free_one", 100),
                ("uid_alpha", "c01_first_light", 150),

                ("uid_beta", "plain_one", 400),
                ("uid_beta", "plain_two", 100),
                ("uid_beta", "generous_one", 100),
                ("uid_beta", "free_one", 400),
                ("uid_beta", "c01_first_light", 100),

                ("0123456789abcdef0123456789abcdef", "plain_one", 400),
                ("0123456789abcdef0123456789abcdef", "plain_two", 100),
                ("0123456789abcdef0123456789abcdef", "generous_one", 100),
                ("0123456789abcdef0123456789abcdef", "free_one", 100),
                ("0123456789abcdef0123456789abcdef", "c01_first_light", 100),
            };

            var failures = new List<string>();
            foreach (var (player, level, percent) in expected)
            {
                int got = table.PercentFor(player, L(level));
                if (got != percent) failures.Add($"{player}@{level}: expected {percent}, got {got}");
            }

            Assert.IsEmpty(failures,
                           "the golden generator no longer matches the shared vectors. Every one of " +
                           "these is a glade somebody has been paid for, and the server derives the " +
                           "same number — see invariant 9c.\n" + string.Join("\n", failures));
        }

        /// <summary>
        /// A non-ASCII account id has to hash the same way on both sides. Firebase uids are
        /// ASCII today, but the hash is spelled out per UTF-16 code unit precisely so that
        /// this can never become the thing that quietly diverges.
        /// </summary>
        [Test]
        public void ANonAsciiKeyMatchesTheSharedVectors()
        {
            Assert.AreEqual(100, Vectors().PercentFor("uid_ünïcode", L("plain_one")));
            Assert.AreEqual(400, Vectors().PercentFor("uid_ünïcode", L("c01_first_light")));
        }

        // ------------------------------------------------------------ the rule
        /// <summary>
        /// The state before the first sign-in. The client cannot know the server's seed
        /// until it has spoken to the server, so it pays the base — the one direction that
        /// cannot cost anybody anything, because the earned floor means the number can only
        /// rise afterwards.
        /// </summary>
        [Test]
        public void NoAccountMeansNoBonus()
        {
            var table = Vectors();

            Assert.AreEqual(100, table.PercentFor(null, L("plain_one")));
            Assert.AreEqual(100, table.PercentFor(string.Empty, L("plain_one")));
            Assert.AreEqual(100, table.PercentFor("uid_alpha", LevelId.None));
        }

        /// <summary>
        /// The bonus belongs to the glade, not to the run. That is what makes it
        /// unfarmable — replaying pays nothing, and force-quitting re-rolls nothing — and
        /// it is the property the server relies on to recompute without an attempt counter.
        /// </summary>
        [Test]
        public void TheSameGladeAlwaysPaysTheSameMultiplier()
        {
            var table = Vectors();

            for (int i = 0; i < 8; i++)
                Assert.AreEqual(150, table.PercentFor("uid_alpha", L("plain_two")),
                                "a glade's multiplier must not move between reads");
        }

        [Test]
        public void DifferentPlayersGetDifferentGlades()
        {
            var table = Vectors();

            Assert.AreNotEqual(table.PercentFor("uid_alpha", L("free_one")),
                               table.PercentFor("uid_beta", L("free_one")),
                               "the bonus is per account, so two players must not share a lucky glade");
        }

        // ------------------------------------------------------- the arithmetic
        [Test]
        public void TheBaseIsNeverReduced()
        {
            Assert.AreEqual(100, GoldenTable.Apply(100, 100));
            Assert.AreEqual(100, GoldenTable.Apply(100, 50), "a band under 100 must not bite");
            Assert.AreEqual(0, GoldenTable.Apply(0, 400));
        }

        [Test]
        public void TheMultiplyHappensBeforeTheDivide()
        {
            // 45 × 150 / 100 is 67 with integer arithmetic; 45 × (150/100) is 45.
            Assert.AreEqual(67, GoldenTable.Apply(45, 150));
            Assert.AreEqual(180, GoldenTable.Apply(45, 400));
        }

        // ------------------------------------------------------------ the reader
        /// <summary>
        /// The rule that keeps the published reward honest: a band below 100 would pay a
        /// player less for a glade than the reward rule promises, which turns the rule
        /// into a maximum and the store listing into a lie.
        /// </summary>
        [Test]
        public void ABandBelowTheBaseIsRefused()
        {
            var problems = new List<string>();
            var table = GoldenTable.Resolve(new GoldenDto
            {
                bands = new[]
                {
                    new GoldenBandDto { percent = 80, weight = 5 },
                    new GoldenBandDto { percent = 100, weight = 5 },
                },
            }, problems);

            Assert.AreSame(GoldenTable.Default, table);
            Assert.IsTrue(problems.Count > 0, "and it must say why");
        }

        [Test]
        public void AZeroWeightBandIsRefused()
        {
            var problems = new List<string>();
            var table = GoldenTable.Resolve(new GoldenDto
            {
                bands = new[] { new GoldenBandDto { percent = 200, weight = 0 } },
            }, problems);

            Assert.AreSame(GoldenTable.Default, table);
        }

        [Test]
        public void AnAbsentBlockIsNotAnError()
        {
            var problems = new List<string>();

            Assert.AreSame(GoldenTable.Default, GoldenTable.Resolve(null, problems));
            Assert.AreEqual(0, problems.Count);
        }

        [Test]
        public void ABandOverTheCeilingIsClamped()
        {
            var problems = new List<string>();
            var table = GoldenTable.Resolve(new GoldenDto
            {
                bands = new[] { new GoldenBandDto { percent = 100_000, weight = 1 } },
            }, problems);

            Assert.AreEqual(GoldenRules.MaxPercent, table.Bands[0].Percent);
            Assert.IsTrue(problems.Count > 0, "clamping silently is how a typo ships");
        }

        // -------------------------------------------------------- the shipped odds
        /// <summary>
        /// The shipped table has to keep an ordinary case, or the bonus stops being a bonus
        /// and becomes an unannounced retune of every credit figure in the game.
        /// </summary>
        [Test]
        public void TheShippedTableStillPaysTheBaseMostOfTheTime()
        {
            var table = GoldenTable.Default;

            int plainWeight = 0;
            for (int i = 0; i < table.Bands.Count; i++)
                if (!table.Bands[i].IsBonus) plainWeight += table.Bands[i].Weight;

            Assert.Greater(plainWeight * 2, table.TotalWeight,
                           "most glades must pay exactly what the reward rule says");
        }

        [Test]
        public void TheShippedOddsSumToAHundred()
        {
            var table = GoldenTable.Default;

            float total = 0f;
            for (int i = 0; i < table.Bands.Count; i++) total += table.ChanceOf(i);

            Assert.AreEqual(100f, total, 0.001f,
                            "the odds are printed on a panel, so they have to be a list that adds up");
        }
    }
}
