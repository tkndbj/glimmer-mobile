using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// What every shipped Groovekeeper groove is held to, and what the chapter's ladder claims.
    ///
    /// <para>
    /// <b>These are the readings no gate can take.</b> <c>KeeperValidator</c> proves a groove is
    /// solvable, that its star bands are ordered and that proving it is cheap enough for a phone;
    /// what it cannot say is whether the ten of them are a <em>chapter</em> — whether the first
    /// one can be lost, whether the mechanics arrive in an order that teaches, and whether the
    /// board the mode exists for is still the board the mode exists for.
    /// </para>
    /// <para>
    /// Held to one set of rules rather than one copy per level, for <c>WeaveLadderTests</c>'
    /// reason: a claim written out ten times is a claim nine of which will be edited and one of
    /// which will not.
    /// </para>
    /// </summary>
    public sealed class KeeperLadderTests
    {
        const string ChapterId = "k01_grovekeeper";

        static string PathOf(string chapter) =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "StreamingAssets", "Content",
                                          "chapters", chapter + ".json"));

        /// <summary>
        /// Reads the shipped chapter. Editor-only, because <c>JsonUtility</c> is a native call —
        /// the offline runner says so rather than failing these for the wrong reason.
        /// </summary>
        static ChapterBody Body()
        {
            string path = PathOf(ChapterId);
            Assert.IsTrue(File.Exists(path), "no Groovekeeper chapter at " + path);

            var problems = new List<string>();
            Assert.IsTrue(ContentMapper.TryReadChapter(File.ReadAllText(path), problems,
                                                       out var body),
                          ChapterId + " did not read: " + string.Join("; ", problems));
            CollectionAssert.IsEmpty(problems);
            return body;
        }

        static IReadOnlyList<LevelDefinition> Grooves() => Body().Levels;

        static LevelDefinition Groove(string id)
        {
            foreach (var level in Grooves())
                if (level.Id.Value == id) return level;

            Assert.Fail(id + " is missing from the chapter");
            return null;
        }

        static KeeperRules RulesOf(LevelDefinition level)
        {
            var rules = level.RulesAs<KeeperRules>();
            Assert.IsNotNull(rules, level.Id + " is not a Groovekeeper level");
            return rules;
        }

        [Test]
        public void TheChapterIsTenGroovesAndEveryOneOfThemIsPlayable()
        {
            var grooves = Grooves();
            Assert.AreEqual(10, grooves.Count);

            foreach (var level in grooves)
            {
                // Held in a local rather than read through `.Layout.`, which is the shape
                // compile.py refuses: `LevelDefinition.Layout` is null on anything that is not a
                // glade, and the check is textual on purpose (see ALTERNATIVES there).
                var layout = RulesOf(level).Layout;

                Assert.Greater(layout.Beds, 0, level.Id + " has no bed to open");
                Assert.Greater(layout.Sprigs, 0, level.Id + " has nothing to grow from");
                Assert.Greater(KeeperSolver.Par(layout), 0,
                               level.Id + " cannot be finished by anybody");
            }
        }

        [Test]
        public void OnlyTheFirstGrooveCannotBeLost()
        {
            // The heart gate is the only thing in this game that can stop somebody playing, and
            // the worst moment to meet it is while they are still working out what the verb is.
            // Every groove after it carries a basket, or the fail state is decoration.
            var grooves = Grooves();

            Assert.IsFalse(grooves[0].Tuning.HasBudget,
                           "the first groove of the mode is authored without a basket, exactly "
                           + "as the first glade and the first well are");

            for (int i = 1; i < grooves.Count; i++)
                Assert.IsTrue(grooves[i].Tuning.HasBudget, grooves[i].Id + " cannot be lost");
        }

        [Test]
        public void EveryGrooveIsGradedOnThreeBandsThatCanAllBeLandedIn()
        {
            // Invariant 22 from the budget's side. `par + spare` has to clear the two-star line
            // or the bottom band is stranded and every clear is worth two stars or three - which
            // is exactly what a spare of four did to the finale, and nothing but this noticed.
            foreach (var level in Grooves())
            {
                var tuning = level.Tuning;
                if (!tuning.HasBudget) continue;

                Assert.Less(tuning.GoldThreshold, tuning.SilverThreshold,
                            level.Id + ": the two-star band is empty");
                Assert.Less(tuning.SilverThreshold, tuning.MoveBudget,
                            level.Id + ": the basket is at or inside the two-star band, so one "
                            + "star can never be scored");
            }
        }

        [Test]
        public void ParWandersRatherThanClimbingEveryRung()
        {
            // Par is length, not difficulty, and ten rising numbers read as a treadmill. The
            // house rule every chapter here follows.
            var grooves = Grooves();
            bool dips = false;

            for (int i = 1; i < grooves.Count; i++)
                if (grooves[i].Tuning.Par < grooves[i - 1].Tuning.Par) dips = true;

            Assert.IsTrue(dips, "par climbs on every rung of this chapter, which reads as a "
                                + "treadmill - a chapter wants at least one groove that is "
                                + "shorter than the one before it");
        }

        [Test]
        public void TheChapterAsksMoreOfItsFinaleThanOfItsOpening()
        {
            var grooves = Grooves();
            var first = RulesOf(grooves[0]).Layout;
            var last = RulesOf(grooves[grooves.Count - 1]).Layout;

            Assert.Greater(grooves[grooves.Count - 1].Tuning.Par, grooves[0].Tuning.Par,
                           "the finale is not longer than the opening");
            Assert.Greater(last.Heartbeds, first.Heartbeds,
                           "the finale brings nothing the opening did not");
        }

        [Test]
        public void EveryMechanicArrivesOnceAndInAnOrderThatTeaches()
        {
            // A mechanic that first appears on a groove *after* one that already used it is a
            // lesson shown late, and a lesson can only ever be shown once.
            var grooves = Grooves();

            int stone = -1, heartbed = -1, prism = -1;

            for (int i = 0; i < grooves.Count; i++)
            {
                var layout = RulesOf(grooves[i]).Layout;

                if (stone < 0 && layout.Room < layout.Count) stone = i;
                if (heartbed < 0 && layout.Heartbeds > 0) heartbed = i;
                if (prism < 0 && layout.Deal.Prisms > 0) prism = i;
            }

            Assert.GreaterOrEqual(stone, 0, "no groove in this chapter has stone on it");
            Assert.GreaterOrEqual(heartbed, 0, "no groove in this chapter has a heartbed");
            Assert.GreaterOrEqual(prism, 0, "no groove in this chapter deals a prism");

            Assert.Less(stone, heartbed, "the heartbed arrives before stone does");
            Assert.Less(heartbed, prism, "the prism arrives before the heartbed does");

            Assert.Greater(stone, 0, "stone is on the very first groove, which has the verb to "
                                     + "teach and nothing else");
        }

        [Test]
        public void TheGrooveTheModeExistsForStillOpensFourBedsWithOneTile()
        {
            // Four beds around one bare cell, each a channel short of the same colour. It is the
            // largest flourish the rules allow and it is also the shortest answer the board has,
            // which is the whole design - the prettiest play and the most efficient one are the
            // same play. A re-seed or a stray edit that loses it loses the chapter's best moment
            // and nothing else would say so.
            var level = Groove("k01_four_petals");

            var fresh = RulesOf(level).Layout;
            var board = new KeeperBoard(fresh);
            int best = 0;

            for (int colour = 1; colour <= Energy.All; colour++)
            {
                if (colour != Energy.R && colour != Energy.G && colour != Energy.B) continue;

                for (int at = 0; at < board.Count; at++)
                {
                    // Read against a board with the four beds already planted, which is what the
                    // answer does: the flourish is the *last* tile, not the first.
                    var played = new KeeperBoard(fresh);
                    var openings = new List<int>();

                    played.Openings(Energy.R, openings);
                    foreach (int bed in openings) played.Plant(Energy.R, bed, null);

                    var gain = played.Preview(colour, at);
                    if (gain.Blooms > best) best = gain.Blooms;
                }
            }

            Assert.AreEqual(4, best,
                            "no single tile on k01_four_petals opens its four beds at once");

            // Four rather than five, and the difference is worth stating: the tile in the middle
            // is surrounded by the four it opens, so it gathers only their colour and its own and
            // does not bloom itself. Five is the ceiling the rules allow (KeeperFlourish.Most) and
            // is reachable on a board arranged for it; four beds together is what *this* one
            // promises, and what its name and its line say.
            Assert.LessOrEqual(best, KeeperFlourish.Most);
        }

        [Test]
        public void NoGrooveIsSoOpenThatAnyTidyPlayFinishesIt()
        {
            // Invariant 5d, counted. A groove with a great many shortest answers is one where the
            // ground and the procession are deciding nothing, however pretty it looks.
            foreach (var level in Grooves())
            {
                var layout = RulesOf(level).Layout;
                var survey = KeeperSolver.Survey(layout);

                Assert.IsTrue(survey.Proved, level.Id + " could not be proved");
                Assert.LessOrEqual(survey.Ways, 300,
                                   level.Id + " has " + survey.Ways + " shortest answers");
            }
        }

        [Test]
        public void EveryGrooveIsCheapEnoughToProveOnAPhone()
        {
            // The player's device runs this same search once, when somebody opens the level
            // (invariant 26d). The refusal is the validator's; this is the claim that the ten
            // that ship are inside it.
            foreach (var level in Grooves())
            {
                var layout = RulesOf(level).Layout;
                var survey = KeeperSolver.Survey(layout);

                Assert.LessOrEqual(survey.Nodes, 90_000,
                                   level.Id + " took " + survey.Nodes + " positions to prove");
            }
        }
    }
}
