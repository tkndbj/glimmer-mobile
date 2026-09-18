using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The sixth chapter's two verbs, one rule at a time: a gorgon's glare wastes what is poured
    /// into a ward, and a sunlord's seal takes a ward the player does not fill.
    ///
    /// <b>Its own file for the fifth chapter's reason</b> (<c>SiegeRuleTests.Thundercrag</c>):
    /// each rule is asked on a duel built by <c>DuelWith</c>, so a fault reads as the rule that
    /// broke and not as a chapter that lost a run. The chapter itself is measured by
    /// <c>TheSixthChapterIsFoughtOnABoughtLine</c> and the fight by
    /// <c>EveryShippedBossRungIsAFight</c>, which walk the enum and needed no arm for either.
    /// </summary>
    public sealed partial class SiegeRuleTests
    {
        // ------------------------------------------------------------------ the gorgon
        [Test]
        public void AGorgonsGlareLeavesTheWardFiringAndLandingNothing()
        {
            var board = SiegeBoard.Build(DuelWith(SiegeKind.Gorgon));
            var boss = Standing(board);
            Assert.AreEqual(SiegeSpell.Glare, boss.Spellcraft);

            var landed = Landed(board, SiegeSpell.Glare);
            var ward = board.Wards[landed.Ward];

            Assert.IsTrue(ward.Glared, "a glare landed and the ward is not stone-struck");

            // **Fuelled, and that is the verb.** A douse and a chain both answer false here; this
            // one is a turret working perfectly and achieving nothing, so anything that read it
            // as "out" would be drawing it as one of the other two.
            ward.Fuel = ward.Capacity;
            Assert.IsTrue(ward.Fuelled,
                          "a stone-struck ward reads as unfuelled, so it is a chain wearing a "
                          + "different name");
        }

        [Test]
        public void AStoneStruckWardBurnsItsFuelAndFiresNoBolt()
        {
            var board = SiegeBoard.Build(DuelWith(SiegeKind.Gorgon));
            Standing(board);

            var landed = Landed(board, SiegeSpell.Glare);
            var ward = board.Wards[landed.Ward];

            ward.Fuel = ward.Capacity;
            float held = ward.Fuel;

            int bolts = 0, wasted = 0;

            for (int i = 0; i < 60 * 3 && ward.Glared; i++)
            {
                var report = board.Advance(1f / 60f);

                for (int k = 0; k < report.Bolts.Count; k++)
                    if (report.Bolts[k].Ward == landed.Ward) bolts++;

                for (int k = 0; k < report.Stoned.Count; k++)
                    if (report.Stoned[k] == landed.Ward) wasted++;
            }

            Assert.Zero(bolts, "a stone-struck ward landed a bolt");
            Assert.Greater(wasted, 0,
                           "a stone-struck ward with a full tube and a hill in front of it took "
                           + "no shot at all, so there is nothing for the player to see going "
                           + "wrong");
            Assert.Less(ward.Fuel, held,
                        "a stone-struck ward kept its fuel, so a glare is a chain that banks - "
                        + "which is the shackler's verb and not this one");
        }

        [Test]
        public void AGlareOverAnEmptyHillCostsNothing()
        {
            var board = SiegeBoard.Build(DuelWith(SiegeKind.Gorgon));
            var boss = Standing(board);

            var landed = Landed(board, SiegeSpell.Glare);
            var ward = board.Wards[landed.Ward];

            // The boss is the only thing on the hill, and a boss resting on its stand's floor is
            // not a target (`SiegeBoard.Aim`) - so there is nothing this ward would have fired at.
            //
            // **Stood on the floor by hand**, because the only way to reach it by playing is to
            // out-damage the stand's own clock: once a stand has settled, the frame the line
            // reaches the floor is the frame the next stand opens and the floor slides away
            // (`SiegeBoard.Fights`). That is the mode working - a player's bolts are never idle -
            // and it leaves this fixture nothing to observe unless it arranges the window itself.
            boss.Health = boss.PhaseFloor;
            Assert.IsTrue(boss.Impervious, "the boss on its stand's floor could still be hurt");

            board.Wards[landed.Ward].Stone = SiegeTuning.GorgonGlare;
            ward.Fuel = ward.Capacity;
            float held = ward.Fuel;

            for (int i = 0; i < 30 && boss.Impervious; i++) board.Advance(1f / 60f);

            Assert.AreEqual(held, ward.Fuel, 0.001f,
                            "a glare burned fuel with nothing to fire at, so it takes from a "
                            + "player who was not being asked for anything");
        }

        [Test]
        public void AGlareIsNeverLaidTwiceOnOneWard()
        {
            var board = SiegeBoard.Build(DuelWith(SiegeKind.Gorgon));
            Standing(board);

            var first = Landed(board, SiegeSpell.Glare);
            var second = Landed(board, SiegeSpell.Glare);

            Assert.AreNotEqual(first.Ward, second.Ward,
                               "two glares in a row landed on one ward, so the second one took "
                               + "nothing and read as the boss doing nothing");
        }

        // ------------------------------------------------------------- the anvil, on a boss
        /// <summary>
        /// **A boss does not move, and that is a fact about the fight rather than about the charm**
        /// (invariant 37ed). Its phases, its guard and its floor are all measured from where it
        /// stands (37di), so a boss driven off its ground is a boss whose fight restarts.
        ///
        /// <b>Here rather than in `SiegeCharmTests`</b>, because this is the file that can build
        /// a duel - and because what it is really asking about is the boss.
        /// </summary>
        [Test]
        public void AnAnvilDoesNotMoveABoss()
        {
            var board = SiegeBoard.Build(DuelWith(SiegeKind.Sunlord));

            SiegeRaider boss = null;
            for (int i = 0; i < 60 * 30 && boss == null; i++)
            {
                board.Advance(1f / 60f);
                foreach (var raider in board.Raiders)
                    if (raider.Boss && raider.InPlace) boss = raider;
            }

            Assert.IsNotNull(boss, "no boss reached its ground");

            float stood = boss.March;
            Assert.IsFalse(boss.Shove(SiegeTuning.AnvilHeave),
                           "a boss accepted a shove, so its fight can be restarted by a gem");

            for (int i = 0; i < 60; i++) board.Advance(1f / 60f);

            Assert.AreEqual(stood, boss.March, 1e-5f,
                            "a boss moved off the ground its phases, its guard and its floor are "
                            + "all measured from");
        }

        // ------------------------------------------------------------------ the sunlord
        [Test]
        public void ASunlordsSealTakesTheWardWhenNobodyFillsIt()
        {
            var board = SiegeBoard.Build(DuelWith(SiegeKind.Sunlord));
            var boss = Standing(board);
            Assert.AreEqual(SiegeSpell.Doom, boss.Spellcraft);

            var landed = Landed(board, SiegeSpell.Doom);
            var ward = board.Wards[landed.Ward];

            Assert.IsTrue(ward.Doomed, "a seal landed and the ward is not sealed");
            Assert.IsTrue(ward.Alive, "a seal took the ward on the frame it landed");

            bool fell = false;

            for (int i = 0; i < 60 * 30 && !fell; i++)
            {
                var report = board.Advance(1f / 60f);

                for (int k = 0; k < report.Spells.Count; k++)
                    if (report.Spells[k].Craft == SiegeSpell.Doom
                        && report.Spells[k].Ward == landed.Ward
                        && report.Spells[k].Felled)
                        fell = true;
            }

            Assert.IsTrue(fell, "a seal nobody answered ran out and took nothing");
            Assert.IsFalse(ward.Alive, "a seal ran out and the ward is still standing");
        }

        [Test]
        public void ASealFilledInTimeIsBrokenAndTheWardKept()
        {
            var board = SiegeBoard.Build(DuelWith(SiegeKind.Sunlord));
            Standing(board);

            var landed = Landed(board, SiegeSpell.Doom);
            var ward = board.Wards[landed.Ward];

            // A tube's worth of fuel, through the one door fuel comes through
            // (<c>SiegeWard.Fill</c>) - so this is the same arithmetic a player pays.
            Assert.IsTrue(ward.Fill(ward.Capacity, out bool redeemed),
                          "a full tube did not bank a charge, so this case is not paying a toll");

            Assert.IsTrue(redeemed, "a tube's worth of fuel did not pay off the seal");
            Assert.IsFalse(ward.Doomed, "the seal is still on a ward that was filled");

            for (int i = 0; i < 60 * 30; i++) board.Advance(1f / 60f);

            Assert.IsTrue(ward.Alive || board.Wards[landed.Ward].Health <= 0,
                          "a ward that answered its seal was taken by it anyway");
        }

        [Test]
        public void ASealIsNeverLaidOnTheLastWardStanding()
        {
            var board = SiegeBoard.Build(DuelWith(SiegeKind.Sunlord));
            Standing(board);

            // Everything but one post gone, which is the arrangement the rule exists to refuse.
            int alone = 1;
            for (int w = 0; w < board.Wards.Count; w++)
            {
                if (w == alone) continue;
                board.Wards[w].Alive = false;
                board.Wards[w].Health = 0;
            }

            for (int i = 0; i < 60 * 40; i++)
            {
                board.Advance(1f / 60f);
                Assert.IsFalse(board.Wards[alone].Doomed,
                               "a sunlord sealed the last ward standing, so its verb can end a "
                               + "run on its own - which is a fail state nobody was playing "
                               + "against");
            }
        }

        [Test]
        public void ASealIsLongEnoughToBeAnswered()
        {
            // A tube is `WardCapacity` of fuel and a match delivers `MatchGemsTenths` tenths of a
            // gem at `FuelPerGem` each - so the toll is this many matches *of the sealed ward's
            // own colour*. The window has to hold several of them, or the verb is a devour with a
            // countdown drawn on it.
            float match = SiegeTuning.MatchGemsTenths / 10f * SiegeTuning.FuelPerGem;
            float matches = SiegeTuning.WardCapacity / match;

            Assert.Less(matches, 4f,
                        "a seal's toll is four matches or more of one colour, which is not an "
                        + "answer inside any window this mode could give");

            Assert.Greater(SiegeTuning.DoomFor, matches * SiegeTuning.BeatFor * 4f,
                           "a seal's window is too short for the matches it asks for, so the "
                           + "verb cannot be answered and is a gravemaw's devour with extra steps");
        }
    }
}
