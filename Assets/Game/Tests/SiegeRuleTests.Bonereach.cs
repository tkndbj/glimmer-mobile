using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The seventh chapter's two verbs, one rule at a time: a harrower takes a rank and leaves it
    /// on the ground, and a hollowking strikes whatever has gone quiet.
    ///
    /// <b>Its own file for the fifth and sixth chapters' reason</b>
    /// (<c>SiegeRuleTests.Thundercrag</c>, <c>.Dustcrown</c>): each rule is asked on a duel built
    /// by <c>DuelWith</c>, so a fault reads as the rule that broke and not as a chapter that lost
    /// a run. The chapter itself is measured by <c>TheSeventhChapterIsFoughtOnABoughtLine</c> and
    /// the fight by <c>EveryShippedBossRungIsAFight</c>, which walk the enum and needed no arm
    /// for either.
    /// </summary>
    public sealed partial class SiegeRuleTests
    {
        // ----------------------------------------------------------------- the harrower
        [Test]
        public void AHarrowTakesARankAndLeavesItOnTheHill()
        {
            var board = SiegeBoard.Build(DuelWith(SiegeKind.Harrower));
            var boss = Standing(board);
            Assert.AreEqual(SiegeSpell.Harrow, boss.Spellcraft);

            // Ranks to take. A harrow aimed at an unranked line falls through to the freshest
            // post and smites it like any other spell, which is a different rule and is asked
            // for on its own below.
            for (int w = 0; w < board.Wards.Count; w++) board.Wards[w].Rank = 2;

            int before = board.Cogs.Count;

            var landed = Landed(board, SiegeSpell.Harrow);
            var ward = board.Wards[landed.Ward];

            Assert.AreEqual(1, ward.Rank,
                            "a harrow landed and the ward kept its rank, so the verb did "
                            + "nothing but smite");

            Assert.Greater(board.Cogs.Count, before,
                           "a harrow took a rank and dropped nothing on the hill, which is an "
                           + "overlord's sunder wearing a different name");
        }

        [Test]
        public void TheRankAHarrowTookIsBoughtBackByTakingTheCog()
        {
            var board = SiegeBoard.Build(DuelWith(SiegeKind.Harrower));
            Standing(board);

            for (int w = 0; w < board.Wards.Count; w++) board.Wards[w].Rank = 2;

            var landed = Landed(board, SiegeSpell.Harrow);
            var ward = board.Wards[landed.Ward];

            Assert.AreEqual(1, ward.Rank);
            Assert.IsNotEmpty(board.Cogs, "nothing was dropped to pick up");

            // **The cog it dropped is bound to the ward it robbed**, which is what makes the
            // verb answerable rather than merely visible: a cog that paid some other post back
            // would be a rank moved rather than a rank returned.
            var cog = board.Cogs[board.Cogs.Count - 1];
            Assert.AreEqual(landed.Ward, cog.Ward,
                            "the cog a harrow dropped pays a different ward, so the rank it "
                            + "took cannot be bought back");

            var taken = board.Take(cog.Id);

            Assert.AreEqual(landed.Ward, taken.Ward);
            Assert.AreEqual(2, ward.Rank,
                            "taking the cog a harrow dropped did not put the rank back, so what "
                            + "it takes is a loss rather than a decision");
        }

        [Test]
        public void AHarrowOnAnUnrankedLineStillSmites()
        {
            var board = SiegeBoard.Build(DuelWith(SiegeKind.Harrower));
            Standing(board);

            for (int w = 0; w < board.Wards.Count; w++) board.Wards[w].Rank = 0;

            int before = board.Cogs.Count;

            var landed = Landed(board, SiegeSpell.Harrow);

            // **It finds nothing to take and throws anyway**, which is the colossus's clause
            // (`SiegeTuning.CastRetry`): a boss that can find nothing to do holds its cast for
            // ever and reads as broken.
            Assert.AreEqual(before, board.Cogs.Count,
                            "a harrow dropped a cog off an unranked ward, which is a rank the "
                            + "player never earned");

            Assert.Greater(landed.Damage, 0,
                           "a harrow with no rank to take took nothing at all, so the boss "
                           + "stands there doing nothing");
        }

        // ---------------------------------------------------------------- the hollowking
        /// <summary>
        /// How many posts one cast of <paramref name="craft"/> struck in this report.
        ///
        /// A count rather than the first record, because a wane is booked one record per post
        /// exactly as a rally is - and what the verb is about is <em>which</em> posts, so a
        /// fixture reading only the first would pass on a rule that struck one at random.
        /// </summary>
        static int Struck(SiegeReport report, SiegeSpell craft)
        {
            int made = 0;
            for (int i = 0; i < report.Spells.Count; i++)
                if (report.Spells[i].Craft == craft) made++;

            return made;
        }

        /// <summary>
        /// How many posts the <b>first</b> wane to land struck, or nought if none did inside a
        /// minute.
        ///
        /// <b>The first cast rather than a window of them</b>, and that is not a detail: a wane
        /// is booked one record per post, so a loop that ran a whole cadence collected the
        /// second cast as well and read four posts as eight. What is being measured here is one
        /// cast's reach.
        /// </summary>
        static int WanedPosts(SiegeBoard board)
        {
            for (int i = 0; i < 60 * 60; i++)
            {
                int struck = Struck(board.Advance(1f / 60f), SiegeSpell.Wane);
                if (struck > 0) return struck;
            }

            return 0;
        }

        [Test]
        public void AWaneSparesEveryWardThatHasBeenFiring()
        {
            var board = SiegeBoard.Build(DuelWith(SiegeKind.Hollowking));
            var boss = Standing(board);
            Assert.AreEqual(SiegeSpell.Wane, boss.Spellcraft);

            // Every post kept fed, so every post is working - a boss wears no colour and is
            // reached by the whole line at full weight (37dn), so there is nothing else to
            // explain about why they all fire.
            int struck = 0;
            int steps = (int)(60f * (SiegeTuning.HollowkingCastEvery + SiegeTuning.BossTell + 2f));

            for (int i = 0; i < steps; i++)
            {
                for (int w = 0; w < board.Wards.Count; w++)
                    board.Wards[w].Fuel = board.Wards[w].Capacity;

                struck += Struck(board.Advance(1f / 60f), SiegeSpell.Wane);
            }

            Assert.Zero(struck,
                        "a wane struck a post that had been firing, so what it takes is not "
                        + "idleness - which makes it a rally with extra steps and the seventh "
                        + "chapter has repeated a fight");
        }

        [Test]
        public void AWaneStrikesEveryWardThatHasNot()
        {
            var board = SiegeBoard.Build(DuelWith(SiegeKind.Hollowking));
            Standing(board);

            // Every post dry, so nothing leaves a barrel and every one of them is hollow.
            for (int w = 0; w < board.Wards.Count; w++) board.Wards[w].Fuel = 0f;

            Assert.AreEqual(board.Wards.Count, WanedPosts(board),
                            "a wane over a line that fired nothing did not reach every post, "
                            + "so a player who did nothing pays less than the rule says");
        }

        [Test]
        public void AWaneIsAimedAtNoWard()
        {
            // The drawing half, and it is the fault `SiegeView.Storm`'s `default` arm has
            // already paid for twice (invariant 44e): a spell that reads as aimed at a ward is
            // handed one by `SiegeBoard.Wanted` and drawn at a post nobody chose.
            Assert.IsFalse(SiegeTuning.AimsAtAWard(SiegeKind.Hollowking),
                           "a wane reads as aimed at a ward, so the view draws four records at "
                           + "one post");

            Assert.IsTrue(SiegeTuning.EndangersTheLine(SiegeKind.Hollowking),
                          "a wane reads as harmless, so it would ride a wave for company "
                          + "rather than walking on alone (invariant 37ad)");
        }
    }
}
