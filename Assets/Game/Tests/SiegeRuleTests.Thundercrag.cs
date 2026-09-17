using System.Collections.Generic;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The fifth chapter's two verbs, one rule at a time: a thunderer drains what a ward banked,
    /// and a colossus buries a ward the player digs out.
    ///
    /// <b>Its own file for the fight's reason</b> (<c>SiegeRuleTests.Fight</c>): each rule is
    /// asked on a duel built by <c>DuelWith</c>, so a fault reads as the rule that broke and not
    /// as a chapter that lost a run. The chapter itself is measured by
    /// <c>TheFifthChapterIsFoughtOnABoughtLine</c> and the fight by
    /// <c>EveryShippedBossRungIsAFight</c>, which walk the enum and needed no arm for either.
    /// </summary>
    public sealed partial class SiegeRuleTests
    {
        /// <summary>The first spell of <paramref name="craft"/> to land, or fails after a minute.</summary>
        static SiegeSpellLanded Landed(SiegeBoard board, SiegeSpell craft)
        {
            for (int i = 0; i < 60 * 60; i++)
            {
                var report = board.Advance(1f / 60f);
                for (int k = 0; k < report.Spells.Count; k++)
                    if (report.Spells[k].Craft == craft) return report.Spells[k];
            }

            Assert.Fail($"no {craft} landed inside a minute");
            return default;
        }

        // ------------------------------------------------------------------ the thunderer
        [Test]
        public void AThundererTakesTheChargesAWardBankedAndThrowsThemBack()
        {
            var board = SiegeBoard.Build(DuelWith(SiegeKind.Thunderer));
            var boss = Standing(board);
            Assert.AreEqual(SiegeSpell.Drain, boss.Spellcraft);

            // Two banked on one ward and nothing anywhere else - the arrangement a drain exists
            // to reject. The board is otherwise quiet: no fuel, so nothing throws them first.
            const int held = 1;
            board.Wards[held].Charges = SiegeTuning.MostCharges;
            int whole = board.Wards[held].Health;

            var landed = Landed(board, SiegeSpell.Drain);

            Assert.AreEqual(held, landed.Ward, "a drain did not land on the ward holding the most");
            Assert.AreEqual(SiegeTuning.MostCharges, landed.Taken, "it did not take every charge");
            Assert.AreEqual(0, board.Wards[held].Charges, "the charges were still there afterwards");
            Assert.IsFalse(board.Wards[held].Armed);

            int expect = SiegeTuning.ThundererCast + SiegeTuning.MostCharges * SiegeTuning.ThundererDrain;
            Assert.AreEqual(expect, landed.Damage,
                            "a drain's blow is the base plus what it took, and nothing else");
            Assert.AreEqual(whole - expect, board.Wards[held].Health);
        }

        [Test]
        public void AThundererWithNothingToDrainIsASmiteAndNoWorse()
        {
            var board = SiegeBoard.Build(DuelWith(SiegeKind.Thunderer));
            Standing(board);

            for (int w = 0; w < board.Wards.Count; w++)
                Assert.AreEqual(0, board.Wards[w].Charges, "this case assumes nothing is banked");

            var landed = Landed(board, SiegeSpell.Drain);

            Assert.AreEqual(0, landed.Taken);
            Assert.AreEqual(SiegeTuning.ThundererCast, landed.Damage,
                            "a drain over an empty line took more than its base figure, so the "
                            + "decision it asks - throw before it lands - has no right answer");
        }

        [Test]
        public void AChargeThrownBeforeTheDrainLandsIsNotTaken()
        {
            var board = SiegeBoard.Build(DuelWith(SiegeKind.Thunderer));
            var boss = Standing(board);

            // Wait out the guard so a charge has something it may land on (`Furthest` skips an
            // untouchable boss), then bank one and throw it on the frame the tell begins.
            for (int i = 0; i < 60 * 10 && boss.Guarded; i++) board.Advance(1f / 60f);
            Assert.IsFalse(boss.Guarded);

            const int held = 2;
            board.Wards[held].Charges = 1;

            bool thrown = false;
            SiegeSpellLanded landed = default;
            bool seen = false;

            for (int i = 0; i < 60 * 60 && !seen; i++)
            {
                var report = board.Advance(1f / 60f);

                for (int k = 0; k < report.Casts.Count && !thrown; k++)
                    if (report.Casts[k].Craft == SiegeSpell.Drain && report.Casts[k].Ward == held)
                        thrown = board.Overcharge(held, null).Landed;

                for (int k = 0; k < report.Spells.Count; k++)
                    if (report.Spells[k].Craft == SiegeSpell.Drain) { landed = report.Spells[k]; seen = true; }
            }

            Assert.IsTrue(seen, "no drain landed");
            Assert.IsTrue(thrown, "the charge could not be thrown during the tell, so the decision is not one");
            Assert.AreEqual(0, landed.Taken, "a charge thrown during the tell was taken anyway");
            Assert.AreEqual(SiegeTuning.ThundererCast, landed.Damage);
        }

        [Test]
        public void ADrainCanNeverTakeMoreThanHalfAWardInOneBlow()
        {
            int heaviest = SiegeTuning.ThundererCast + SiegeTuning.MostCharges * SiegeTuning.ThundererDrain;
            Assert.Less(heaviest * 2, SiegeTuning.WardHealth,
                        "the heaviest drain takes half a ward or more, so two casts fell a full "
                        + "turret before the player has had a tell to answer");
        }

        // ------------------------------------------------------------------ the colossus
        [Test]
        public void AColossusBuriesAWardAndOnlyDiggingFreesIt()
        {
            var board = SiegeBoard.Build(DuelWith(SiegeKind.Colossus));
            var boss = Standing(board);
            Assert.AreEqual(SiegeSpell.Bury, boss.Spellcraft);

            var landed = Landed(board, SiegeSpell.Bury);
            var ward = board.Wards[landed.Ward];

            Assert.IsTrue(ward.Buried, "a boulder landed and buried nothing");
            Assert.AreEqual(SiegeTuning.RubbleTaps, ward.Rubble);
            Assert.AreEqual(SiegeTuning.ColossusCast, landed.Damage, "a boulder takes its base figure");

            // **Fuel poured in banks and cannot be fired**, which is the chain's rule under stone.
            ward.Fill(ward.Capacity * .5f);
            Assert.Greater(ward.Fuel, 0f);
            Assert.IsFalse(ward.Fuelled, "a buried ward fired");
            ward.Charges = 1;
            Assert.IsFalse(ward.Armed, "a buried ward threw a charge");
            Assert.IsFalse(board.Overcharge(landed.Ward, null).Landed);

            // **It never lifts on the clock.** Ten seconds later, with nothing tapped, it stands.
            for (int i = 0; i < 60 * 10; i++) board.Advance(1f / 60f);
            Assert.IsTrue(ward.Buried, "rubble ran out on the clock, which is a chain wearing stone");
            Assert.AreEqual(SiegeTuning.RubbleTaps, ward.Rubble);

            for (int tap = SiegeTuning.RubbleTaps; tap > 0; tap--)
            {
                Assert.IsTrue(board.Dig(landed.Ward), $"tap {tap} was refused on a buried post");
                Assert.AreEqual(tap - 1, ward.Rubble);
            }

            Assert.IsFalse(ward.Buried, "three taps did not clear three pieces");
            Assert.IsTrue(ward.Armed, "the charge banked under the rubble was lost");
            Assert.IsFalse(board.Dig(landed.Ward), "a tap on a clear post was counted");
        }

        [Test]
        public void ADigIsAnsweredOnTheCallAndNeverOnTheNextReport()
        {
            var board = SiegeBoard.Build(DuelWith(SiegeKind.Colossus));
            Standing(board);
            var landed = Landed(board, SiegeSpell.Bury);

            // The overcharge's shape: a tap lands between two steps of the clock, and the step
            // that follows clears the report on its way in - so the answer has to be the call's.
            Assert.IsTrue(board.Dig(landed.Ward));
            Assert.AreEqual(SiegeTuning.RubbleTaps - 1, board.Wards[landed.Ward].Rubble);
            board.Advance(1f / 60f);
            Assert.AreEqual(SiegeTuning.RubbleTaps - 1, board.Wards[landed.Ward].Rubble,
                            "a step of the clock moved the rubble, which only a tap may");
        }

        [Test]
        public void ASecondBoulderOnABuriedPostGoesElsewhere()
        {
            var board = SiegeBoard.Build(DuelWith(SiegeKind.Colossus));
            Standing(board);

            var first = Landed(board, SiegeSpell.Bury);
            var second = Landed(board, SiegeSpell.Bury);

            Assert.AreNotEqual(first.Ward, second.Ward,
                               "a colossus buried the same post twice, so the pile was set back to "
                               + "full and the player's digging was thrown away");
        }

        [Test]
        public void AColossusCastsSlowerThanAPlayerCanDig()
        {
            // Three taps at a phone's pace is well under two seconds; a cadence that left less
            // than that between boulders would bury the line faster than hands can clear it.
            Assert.GreaterOrEqual(SiegeTuning.ColossusCastEvery, 4f,
                                  "a colossus throws faster than a buried post can be dug");
            Assert.AreEqual(3, SiegeTuning.RubbleTaps, "a burial is three taps by design (SiegeTuning.RubbleTaps)");
        }

        // ------------------------------------------------------------------ the hourglass and the fight
        /// <summary>
        /// A small field with one known swap on it - `SiegeCharmTests.Crossed`, which springs
        /// whatever stands on cell 7 when 2 and 7 are traded - and a warlord as its only wave.
        /// </summary>
        static SiegeLayout CrossedDuel(SiegeKind kind)
            => Layout(new[] { "rrbgy", "gyrbg", "bgyrb" }, "rgby", "rgby", new string[0],
                      SiegeTuning.NameOf(kind) + ":r", 0, 0, "plsfh");

        [Test]
        public void AnHourglassHoldsABosssSpellAndNotItsGuard()
        {
            var board = SiegeBoard.Build(CrossedDuel(SiegeKind.Boss));
            var boss = Standing(board);
            Assert.IsTrue(boss.Guarded, "the boss stood with no guard");

            // Stopped on the frame it stands: the guard must still run down to its deadline
            // (invariant 37dl), and no spell may leave while the hill stands still. Stood on the
            // one cell the fixture's swap clears, so what is under test is the stop, not the deal.
            int casts = boss.Casts;
            board.Stand(7, SiegeCharm.Hourglass);
            Assert.IsNotNull(board.Swap(2, 7), "the fixture's swap no longer lines anything up");

            for (int i = 0; i < 60 * 2 && !board.Stilled; i++) board.Advance(1f / 60f);
            Assert.IsTrue(board.Stilled, "the hourglass never landed");

            float held = 0f;
            while (board.Stilled)
            {
                board.Advance(1f / 60f);
                held += 1f / 60f;
                Assert.LessOrEqual(held, SiegeTuning.HourglassFor + .1f, "the hill never walked again");
                Assert.AreEqual(casts, boss.Casts, "a boss cast into a stopped hill");
            }

            Assert.AreEqual(SiegeTuning.HourglassFor, held, .05f);

            // The guard's deadline is `GuardMost` from the plant, which the stop ran through
            // rather than paused; two more seconds is past it whatever the stop cost.
            for (int i = 0; i < 60 * 2 && boss.Guarded; i++) board.Advance(1f / 60f);
            Assert.IsFalse(boss.Guarded, "the guard stood past its deadline under an hourglass");
        }
    }
}
