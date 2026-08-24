using GlimmerGrove.Content;
using GlimmerGrove.Progression;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// A crossing carries two flows through one tile and never lets them meet.
    ///
    /// <para>
    /// It is the first rule in this game to touch the light graph itself, and it does so by
    /// splitting a cell rather than by changing what a join means — the traversal walks
    /// <em>strands</em>, of which every other tile has one. Everything above that walk is
    /// untouched, which is what these tests are mostly here to pin: colour, winning, par, the
    /// near-miss reading and the duskcap rule all still behave exactly as they did, and the
    /// only difference is which cells are in which network.
    /// </para>
    /// <para>
    /// Two failure modes are worth naming because both look perfectly authored. A crossing
    /// wears all four arms at <em>every</em> angle, so the mask comparison that used to serve
    /// as "is this tile solved" calls every crossing solved — which derives a par short by
    /// however many crossings a board carries, and lets a twisted one that must be turned
    /// count as free. And a crossing whose two strands are joined elsewhere on the board is a
    /// tile telling the player a lie in the one place the game asks them to trust their eyes.
    /// </para>
    /// </summary>
    public sealed class CrossingTests
    {
        static Puzzle Board(string[] rows)
        {
            int width = rows[0].Split(' ').Length;
            var layout = new LevelLayout(width, rows.Length, rows);
            var parsed = LevelGridParser.Parse(layout);
            Assert.IsTrue(parsed.Ok, string.Join("; ", parsed.Errors));

            int par = Mathf.Max(1, PuzzleFactory.MinimumMoves(parsed.Cells));
            return new Puzzle(LevelId.Parse("t_level"), width, rows.Length,
                              LevelTuning.Default(par), parsed.Cells);
        }

        static LevelDefinition Level(string[] rows)
        {
            int width = rows[0].Split(' ').Length;
            var layout = new LevelLayout(width, rows.Length, rows);
            var parsed = LevelGridParser.Parse(layout);
            int par = parsed.Ok ? Mathf.Max(1, PuzzleFactory.MinimumMoves(parsed.Cells)) : 1;

            return new LevelDefinition(
                LevelId.Parse("t_level"), ChapterId.Parse("t_chapter"),
                layout, LevelTuning.Default(par),
                new LevelPresentation(new Vector2(.5f, .5f), null, null, null));
        }

        static bool Has(LevelValidationReport report, LevelIssueSeverity severity, string fragment)
        {
            foreach (var issue in report.Issues)
                if (issue.Severity == severity && issue.Message.Contains(fragment)) return true;
            return false;
        }

        static string ParseError(string token)
        {
            var parsed = LevelGridParser.Parse(new LevelLayout(1, 1, new[] { token }));
            return parsed.Ok ? null : string.Join("; ", parsed.Errors);
        }

        // A red heart above and a red critter below; a blue heart west and a blue critter
        // east. The two runs pass through one tile. With a crossroads there both critters
        // would be fed magenta and neither would wake.
        //
        //        *R
        //   *B   ===   @B
        //        @R
        static readonly string[] CleanCrossRows =
        {
            ". *S#R/0 .",
            "*E#B/0 =NS+EW/0 @W#B/0",
            ". @N#R/0 .",
        };

        // The same four neighbours, joined by an ordinary crossroads instead.
        static readonly string[] MuddledRows =
        {
            ". *S#R/0 .",
            "*E#B/0 -NESW/0 @W#B/0",
            ". @N#R/0 .",
        };

        // A twisted crossing: north pairs with west and south with east, so the red run
        // turns one corner and the blue run turns the other, inside one tile.
        static readonly string[] TwistRows =
        {
            ". *S#R/0 .",
            "@E#R/0 =NW+ES/0 @W#B/0",
            ". *N#B/0 .",
        };

        // ------------------------------------------------------------------ the light
        [Test]
        public void TheTwoStrandsOfACrossingAreDifferentNetworks()
        {
            var board = Board(CleanCrossRows);

            Assert.AreNotEqual(board.Comp(4, 0), board.Comp(4, 1),
                               "a crossing's strands must not share a network");
            Assert.AreEqual(Energy.R, board.EnergyOn(4, 0), "the north-south strand carries the red heart");
            Assert.AreEqual(Energy.B, board.EnergyOn(4, 1), "the east-west strand carries the blue heart");
            Assert.IsTrue(board.Won, "both critters get exactly the colour they asked for");
        }

        /// <summary>
        /// The same neighbours joined by a crossroads instead, which is the whole point of the
        /// mechanic stated from the other side: without the split these two runs merge, both
        /// critters are fed magenta, and neither of them wakes.
        /// </summary>
        [Test]
        public void ACrossroadsWithTheSameArmsMergesWhatACrossingKeepsApart()
        {
            var board = Board(MuddledRows);

            Assert.AreEqual(1, board.StrandCount(4), "a crossroads carries one flow, not two");
            Assert.AreEqual(-1, board.Comp(4, 1), "and so has no second strand to ask about");
            Assert.AreEqual(Energy.R | Energy.B, board.Energy(4));
            Assert.AreEqual(Energy.R | Energy.B, board.Energy(3), "which reaches the red critter as magenta");
            Assert.IsFalse(board.Won, "a red critter fed magenta stays asleep");
        }

        [Test]
        public void EnergyOnATileIsTheUnionOfItsStrands()
        {
            var board = Board(CleanCrossRows);
            Assert.AreEqual(Energy.R | Energy.B, board.Energy(4));
        }

        /// <summary>
        /// Light travel distance is measured along a strand, not through a tile. Without that
        /// the ripple would leap between two networks that never touch — and <c>Depth</c> is
        /// what staggers every animation on the board.
        /// </summary>
        [Test]
        public void DepthTravelsAlongAStrandAndNotAcrossTheTile()
        {
            var board = Board(CleanCrossRows);

            Assert.AreEqual(0, board.Depth[1], "the red heart is its own source");
            Assert.AreEqual(1, board.Depth[4], "the crossing is one step from either heart");
            Assert.AreEqual(2, board.Depth[7], "the red critter is two steps down the north-south strand");
        }

        // ------------------------------------------------------------------ turning
        /// <summary>
        /// A straight crossing reads the same however it is turned, so it can never be tapped
        /// and never owes a turn. That falls out of <see cref="Puzzle.Alike"/> treating the two
        /// strands as interchangeable labels rather than out of a rule about straightness.
        /// </summary>
        [Test]
        public void AStraightCrossingIsInertAtEveryRotation()
        {
            for (byte rot = 0; rot < 4; rot++)
            {
                var board = Board(new[]
                {
                    ". *S#R/0 .",
                    "*E#B/0 =NS+EW/" + rot + " @W#B/0",
                    ". @N#R/0 .",
                });

                Assert.IsTrue(board.InertAlone(4), $"a straight crossing at /{rot} should be inert");
                Assert.IsFalse(board.CanTurn(4), $"a straight crossing at /{rot} should refuse a tap");
                Assert.AreEqual(0, board.TurnsOwed(4), $"a straight crossing at /{rot} owes nothing");
                Assert.IsTrue(board.Won, $"a straight crossing at /{rot} is already carrying both runs");
            }
        }

        /// <summary>
        /// A twisted crossing has two readings rather than four: turning it twice swaps which
        /// strand is called which, and nothing on the board can tell. So it is worth exactly
        /// one tap however far out it is authored — the fact every derived number depends on.
        /// </summary>
        [Test]
        public void ATwistedCrossingIsWorthExactlyOneTapHoweverItIsAuthored()
        {
            Assert.AreEqual(0, Board(Twisted(0)).TurnsOwed(4));
            Assert.AreEqual(1, Board(Twisted(1)).TurnsOwed(4));
            Assert.AreEqual(0, Board(Twisted(2)).TurnsOwed(4), "two turns is the same tile again");
            Assert.AreEqual(1, Board(Twisted(3)).TurnsOwed(4));

            Assert.IsFalse(Board(Twisted(0)).InertAlone(4), "a twisted crossing is worth turning");
            Assert.IsTrue(Board(Twisted(0)).CanTurn(4));
        }

        // A twisted crossing and a straight conduit on one taproot, at opposite ends of the
        // board. Both are a quarter turn out, so one tap is owed and one tap is charged.
        static readonly string[] RootedCrossRows =
        {
            ". *S#R/0 . .",
            "@E#R/0 =NW+ES/1&A @W#B/0 .",
            ". *N#B/0 . .",
            "*E#G/0 -EW/1&A @W#G/0 .",
        };

        static string[] Twisted(int rot) => new[]
        {
            ". *S#R/0 .",
            "@E#R/0 =NW+ES/" + rot + " @W#B/0",
            ". *N#B/0 .",
        };

        [Test]
        public void TurningATwistedCrossingSwapsWhichRunGoesWhere()
        {
            var board = Board(Twisted(1));
            Assert.IsFalse(board.Won, "authored a quarter turn out, both critters are fed the wrong colour");

            board.Turn(4);
            board.Evaluate();

            Assert.IsTrue(board.Won, "one tap puts each run back on its own corner");
            Assert.AreEqual(Energy.R, board.Energy(3), "the red critter is fed red again");
            Assert.AreEqual(Energy.B, board.Energy(5), "the blue critter is fed blue again");
        }

        // --------------------------------------------------------------------- par
        /// <summary>
        /// Par counts a crossing by what it is, not by its arm mask.
        ///
        /// This is the regression that matters most. A crossing wears all four arms at every
        /// angle, so the mask comparison every owed-turn count used to make reads "already
        /// solved" for all of them — deriving a par short by one per twisted crossing, and with
        /// it a move budget and a clock the board cannot honour. Both are multiples of par.
        /// </summary>
        [Test]
        public void ParChargesATwistedCrossingAndNotAStraightOne()
        {
            Assert.AreEqual(1, Par(Twisted(1)), "a twisted crossing a quarter out costs one tap");
            Assert.AreEqual(0, Par(Twisted(0)), "and nothing when it is already right");

            Assert.AreEqual(0, Par(new[]
            {
                ". *S#R/0 .",
                "*E#B/0 =NS+EW/1 @W#B/0",
                ". @N#R/0 .",
            }), "a straight crossing is never owed a turn");
        }

        static int Par(string[] rows)
        {
            int width = rows[0].Split(' ').Length;
            var parsed = LevelGridParser.Parse(new LevelLayout(width, rows.Length, rows));
            Assert.IsTrue(parsed.Ok, string.Join("; ", parsed.Errors));
            return PuzzleFactory.MinimumMoves(parsed.Cells);
        }

        /// <summary>
        /// The near-miss reading counts a crossing too, and it has to: the defeat screen
        /// promises that if it says one turn, one turn finishes the glade.
        /// </summary>
        [Test]
        public void TheNearMissReadingCountsACrossingStillOwedATurn()
        {
            var board = Board(Twisted(1));
            Assert.AreEqual(1, board.TurnsToSolution);

            board.Turn(4);
            board.Evaluate();
            Assert.AreEqual(0, board.TurnsToSolution);
        }

        /// <summary>
        /// A crossing sits on a taproot like any other conduit, and the root's agreement check
        /// asks the same predicate — so a twisted crossing and an elbow can share a root only
        /// when one number of turns solves both.
        /// </summary>
        [Test]
        public void ACrossingCanShareATaprootAndIsChargedOnceWithIt()
        {
            var board = Board(RootedCrossRows);

            Assert.AreEqual(1, board.TurnsOwed(5), "one tap solves the whole root");
            Assert.AreEqual(1, board.TurnsOwed(13), "which is the same tap, from the other end of it");
            Assert.AreEqual(1, Par(RootedCrossRows),
                            "a root is charged once however many conduits ride on it");

            Assert.IsFalse(board.Won);
            board.Turn(5);
            board.Evaluate();
            Assert.IsTrue(board.Won, "one tap on the crossing rights the elbow across the board with it");
        }

        // ------------------------------------------------------------- what it unlocks
        /// <summary>
        /// The board shape chapter one could not author: a dark island running <em>through</em>
        /// a live network rather than beside it.
        ///
        /// Every arm mates in the solution, so a lit cell's neighbours are lit — which is why a
        /// duskcap and its conduits used to have to be their own separate island. A crossing is
        /// the exception, and the whole reason the rule was worth revisiting.
        /// </summary>
        [Test]
        public void ADarkIslandCanRunThroughALiveNetworkAcrossACrossing()
        {
            var rows = new[]
            {
                ". xS/0 .",
                "*E#R/0 =EW+NS/0 @W#R/0",
                ". xN/0 .",
            };

            var board = Board(rows);
            Assert.AreEqual(2, board.DuskcapCount);
            Assert.AreEqual(0, board.DuskcapsWoken, "the dark strand reaches no heart-crystal");
            Assert.IsTrue(board.Won);

            var report = LevelValidator.Validate(Level(rows));
            Assert.IsFalse(report.HasErrors, string.Join("; ", report.Issues));
        }

        // -------------------------------------------------------------- the validator
        /// <summary>
        /// A crossing whose strands are joined elsewhere separates nothing, and the player will
        /// spend turns routing around a separation that is not there. A warning rather than an
        /// error: a loop that leaves by one arm and returns by another has to close somewhere,
        /// and that is a judgement about intent.
        /// </summary>
        [Test]
        public void ACrossingWhoseStrandsMeetElsewhereIsReported()
        {
            var report = LevelValidator.Validate(Level(new[]
            {
                "-ES/0 -SW/0 .",
                "-NE/0 =NS+EW/0 *W#R/0",
                ". @N#R/0 .",
            }));

            Assert.IsTrue(Has(report, LevelIssueSeverity.Warning, "crosses nothing"),
                          string.Join("; ", report.Issues));
            Assert.IsFalse(report.HasErrors, "it is still a board that can be finished");
        }

        [Test]
        public void ACrossingNoLightEverReachesIsReported()
        {
            var report = LevelValidator.Validate(Level(new[]
            {
                "*E#R/0 @W#R/0 . .",
                ". . -ES/0 -SW/0",
                ". . =NE+SW/0 -NW/0",
                ". . -NE/0 -EW/0",
            }));

            Assert.IsTrue(Has(report, LevelIssueSeverity.Warning, "neither strand"),
                          string.Join("; ", report.Issues));
        }

        [Test]
        public void AnHonestCrossingRaisesNothing()
        {
            var report = LevelValidator.Validate(Level(CleanCrossRows));
            foreach (var issue in report.Issues)
                Assert.IsFalse(issue.Message.Contains("crossing"), issue.Message);
        }

        // ----------------------------------------------------------------- the grammar
        [Test]
        public void ACrossingMustSayWhichArmsBelongToWhichStrand()
            => StringAssert.Contains("which arms belong to which strand", ParseError("=NSEW/0"));

        [Test]
        public void TheStrandsOfACrossingCannotShareAnArm()
            => StringAssert.Contains("cannot share an arm", ParseError("=NS+SE/0"));

        [Test]
        public void EachStrandOfACrossingCarriesExactlyTwoArms()
        {
            StringAssert.Contains("exactly two arms", ParseError("=NES+W/0"));
            StringAssert.Contains("exactly two arms", ParseError("=NS+E/0"));
        }

        [Test]
        public void OnlyAFourArmedTileMaySplitItsArms()
            => StringAssert.Contains("'+' separates the two pairs of arms", ParseError("-NS+EW/0"));

        [Test]
        public void ACrossingTakesNoColour()
            => StringAssert.Contains("takes no colour", ParseError("=NS+EW#R/0"));

        [Test]
        public void ACrossingIsAConduitSoItMayBeBrittleAndBoundAndRooted()
        {
            Assert.IsNull(ParseError("=NW+ES/1~2"), "a crossing may crumble");
            Assert.IsNull(ParseError("=NW+ES/1&A"), "a crossing may share a taproot");
            Assert.IsNull(ParseError("=NW+ES/0!"), "a crossing may be rooted in place");
        }

        [Test]
        public void TheStrandsMayBeWrittenInEitherOrder()
        {
            var a = LevelGridParser.Parse(new LevelLayout(1, 1, new[] { "=NS+EW/1" })).Cells[0];
            var b = LevelGridParser.Parse(new LevelLayout(1, 1, new[] { "=EW+NS/1" })).Cells[0];

            Assert.AreEqual(a.solved, b.solved);
            for (int k = 0; k < 4; k++)
                Assert.AreEqual(Puzzle.Alike(a, k), Puzzle.Alike(b, k), $"disagreed at {k} turns");
        }

        // ------------------------------------------------------------------ teaching
        [Test]
        public void ABoardWithACrossingTeachesIt()
        {
            var sightings = MechanicScan.InBoard(Board(CleanCrossRows));

            bool found = false;
            foreach (var sighting in sightings)
                if (sighting.Mechanic.Equals(Mechanic.Crossing))
                {
                    Assert.AreEqual(4, sighting.CellIndex, "the tip should ring the crossing itself");
                    found = true;
                }

            Assert.IsTrue(found, "a board carrying a crossing has to offer its lesson");
        }

        [Test]
        public void ABoardWithoutOneDoesNot()
        {
            foreach (var sighting in MechanicScan.InBoard(Board(MuddledRows)))
                Assert.IsFalse(sighting.Mechanic.Equals(Mechanic.Crossing));
        }
    }
}
