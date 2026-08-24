using GlimmerGrove.Content;
using GlimmerGrove.Progression;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// A briar is a conduit with two of its four ways thorned shut, and one tap swaps which.
    ///
    /// <para>
    /// It is the crossing's opposite number and it costs the light model even less: a crossing
    /// splits a cell into two strands, where a briar leaves the graph alone and changes only
    /// <em>which of a tile's arms conduct</em>. What it buys is the one thing arms cannot buy.
    /// All four of a briar's neighbours mate it at every angle, so nothing about the
    /// pipe-fitting settles it and only colour or the dark can — which is exactly the property
    /// <c>Tools/verify/difficulty.py</c> counts, and the property twenty-two of the game's
    /// first thirty glades turned out not to have anywhere.
    /// </para>
    /// <para>
    /// Three failure modes are pinned here because all three look perfectly authored. A briar
    /// wears all four arms at every angle, so the mask comparison that used to serve as "is
    /// this tile solved" would call every briar solved and derive a par short by one per
    /// briar. A briar whose thorns close nothing off is a tile the player cannot place and has
    /// no reason to place either way. And a briar's shut arms mate straight across the divide
    /// between the light and an island of dark, which is the one thing that could ever make
    /// <see cref="Puzzle.TurnsToSolution"/> generous — see
    /// <see cref="AMisturnedBriarThatLightsTheDarkIsCountedAsADistance"/>, which is the whole
    /// reason <c>Puzzle.Matters</c> has a second clause.
    /// </para>
    /// </summary>
    public sealed class BriarTests
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

        static Cell One(string token)
        {
            var parsed = LevelGridParser.Parse(new LevelLayout(1, 1, new[] { token }));
            Assert.IsTrue(parsed.Ok, string.Join("; ", parsed.Errors));
            return parsed.Cells[0];
        }

        // A heart at each end of one axis of a briar and a critter at each end of the other,
        // so the tile has something to carry whichever way it is turned. Open north to south,
        // it hands the red heart to the critter below and leaves the green heart and the
        // critter east of it dark behind the thorns.
        static readonly string[] Gate =
        {
            ". *S#R/0 .",
            "*E#G/0 %NS+EW/0 @W#A/0",
            ". @N#A/0 ."
        };

        // ------------------------------------------------------------------ parsing
        [Test]
        public void ABriarNamesItsOpenPairFirst()
        {
            var cell = One("%NS+EW/0");
            Assert.AreEqual(Kind.Briar, cell.kind);
            Assert.AreEqual(Puzzle.N | Puzzle.S, cell.gate, "the pair before the '+' is the open one");
            Assert.AreEqual(15, cell.solved, "a briar draws every way it has, open or shut");
            Assert.AreEqual(0, cell.cross, "a briar has one flow, not two");
        }

        [Test]
        public void ABriarMustSayWhichWayIsOpen()
            => StringAssert.Contains("which way is open", ParseError("%NSEW/0"));

        [Test]
        public void ABriarCarriesTwoOpenWaysAndTwoShutOnes()
        {
            StringAssert.Contains("two open arms and two thorned ones", ParseError("%NES+W/0"));
            StringAssert.Contains("two open arms and two thorned ones", ParseError("%N+ESW/0"));
        }

        [Test]
        public void AWayCannotBeBothOpenAndShut()
            => StringAssert.Contains("cannot be both open and shut", ParseError("%NS+SE/0"));

        [Test]
        public void ABriarTakesNoColour()
            => StringAssert.Contains("takes no colour", ParseError("%NS+EW#R/0"));

        [Test]
        public void ABriarIsAConduitSoItMayBeBrittleAndBoundAndRooted()
        {
            Assert.IsNull(ParseError("%NE+SW/1~3"), "a briar may crumble");
            Assert.IsNull(ParseError("%NE+SW/1&A"), "a briar may share a taproot");
            Assert.IsNull(ParseError("%NS+EW/0!"), "a briar may be rooted in place");
        }

        // --------------------------------------------------------------- what it costs
        /// <summary>
        /// The one sentence that separates the two four-armed tiles, in the one place it is
        /// decided. A crossing's pairs are interchangeable labels, so swapping them is not a
        /// turn at all; a briar's are a way through and a wall, so swapping them is the turn.
        /// </summary>
        [Test]
        public void AStraightCrossingIsInertAndAStraightBriarIsWorthOneTap()
        {
            Assert.IsTrue(Puzzle.Alike(One("=NS+EW/0"), 1), "a straight crossing reads the same turned");
            Assert.IsFalse(Puzzle.Alike(One("%NS+EW/0"), 1), "a briar's thorns have moved");
            Assert.IsTrue(Puzzle.Alike(One("%NS+EW/0"), 2), "and are back where they started");

            Assert.AreEqual(1, PuzzleFactory.TurnsOwed(One("%NS+EW/1")));
        }

        [Test]
        public void ATwistedBriarHasFourStatesAndCanOweThreeTurns()
        {
            for (int k = 1; k < 4; k++)
                Assert.IsFalse(Puzzle.Alike(One("%NE+SW/0"), k),
                               $"a twisted briar is a different tile {k} turn(s) round");

            Assert.AreEqual(3, PuzzleFactory.TurnsOwed(One("%NE+SW/1")));
        }

        [Test]
        public void NoBriarIsEverInert()
        {
            var board = Board(Gate);
            Assert.IsFalse(board.InertAlone(board.Idx(1, 1)));
            Assert.IsTrue(board.CanTurn(board.Idx(1, 1)));
        }

        [Test]
        public void ParChargesABriarTheTurnsItOwes()
        {
            var open = Board(Gate);
            Assert.AreEqual(0, PuzzleFactory.MinimumMoves(open.C), "authored at its solution");

            var turned = Board(new[]
            {
                ". *S#R/0 .",
                "*E#G/0 %NS+EW/1 @W#A/0",
                ". @N#A/0 ."
            });
            Assert.AreEqual(1, PuzzleFactory.MinimumMoves(turned.C));
        }

        // -------------------------------------------------------------------- the light
        [Test]
        public void LightPassesTheOpenWayAndStopsAtTheThorns()
        {
            var board = Board(Gate);

            Assert.AreEqual(Energy.R, board.Energy(board.Idx(1, 2)), "the way south is open");
            Assert.AreEqual(0, board.Energy(board.Idx(2, 1)), "the way east is thorned shut");
        }

        [Test]
        public void TurningABriarSwapsWhichWayCarriesTheLight()
        {
            var board = Board(Gate);
            board.Turn(board.Idx(1, 1));
            board.Evaluate();

            Assert.AreEqual(0, board.Energy(board.Idx(1, 2)), "the thorns have closed the way south");
            Assert.AreEqual(Energy.G, board.Energy(board.Idx(2, 1)),
                            "and opened the way east, which is fed by the other heart");
        }

        /// <summary>
        /// A briar carries one flow, not two. The drawing leans on this — a closed way is put
        /// on a strand the tile does not have, so it can never light — and so does every rule
        /// that asks a cell for an exact colour.
        /// </summary>
        [Test]
        public void ABriarHasOneStrandHoweverManyArmsItDraws()
        {
            var board = Board(Gate);
            int i = board.Idx(1, 1);

            Assert.AreEqual(1, board.StrandCount(i));
            Assert.AreEqual(0, board.EnergyOn(i, 1), "there is no second flow to light");
            Assert.AreEqual(board.Energy(i), board.EnergyOn(i, 0));
        }

        // ------------------------------------------------------------------ the dark
        // The grove above, an island of dark below, and a briar between them whose thorns are
        // the whole of what keeps them apart: its open way runs east and west into two dark
        // stubs, so opening the other way joins the critter's network to the duskcap's.
        static readonly string[] Ford =
        {
            "*E#R/0 @WS#A/0 .",
            "-E/0 %EW+NS/0 -W/0",
            ". xN/0 ."
        };

        [Test]
        public void ThornsCanHoldAnIslandOfDarkAgainstTheLight()
        {
            var board = Board(Ford);

            Assert.IsTrue(board.Won, "every critter awake and the shadow still asleep");
            Assert.AreEqual(0, board.DuskcapsWoken);
        }

        [Test]
        public void OpeningTheThornedWayWakesTheShadow()
        {
            var board = Board(Ford);
            board.Turn(board.Idx(1, 1));
            board.Evaluate();

            Assert.AreEqual(1, board.LampsLit, "the critter is still lit, so nothing warns the player");
            Assert.AreEqual(1, board.DuskcapsWoken);
            Assert.IsFalse(board.Won, "a woken duskcap is an unfinished glade however many critters are awake");
        }

        /// <summary>
        /// The reason <c>Puzzle.Matters</c> has a second clause, and a regression test rather
        /// than a description: this board reads 0 under the rule that shipped before briars
        /// existed, on a board that is not won.
        ///
        /// <para>
        /// Before briars it could not happen. Joining the light to an island of dark needs a
        /// mated pair of arms, the authored solution mates none across that divide, so one of
        /// the two tiles always had to be a lit one turned off its solution — and lit tiles
        /// were already counted. A briar's shut arms mate straight across it, so the tile that
        /// leaks the light can be one the solution leaves dark, and the near-miss line would
        /// have told a player they had finished a glade that would not settle.
        /// </para>
        /// </summary>
        [Test]
        public void AMisturnedBriarThatLightsTheDarkIsCountedAsADistance()
        {
            var board = Board(Ford);
            Assert.AreEqual(0, board.TurnsToSolution, "authored at the solution");

            board.Turn(board.Idx(1, 1));
            board.Evaluate();

            Assert.IsFalse(board.Won);
            Assert.AreEqual(1, board.TurnsToSolution,
                            "one turn provably finishes it, and the count must never say none");

            board.Turn(board.Idx(1, 1));
            board.Evaluate();
            Assert.IsTrue(board.Won, "and that one turn is this one");
        }

        // ----------------------------------------------------------------- validation
        [Test]
        public void ABriarWhoseThornsCloseNothingOffIsCalledOut()
        {
            // A ring of conduit all the way round, so both of the briar's ways lead back into
            // the same network: the thorns shut a door onto the room they are already in.
            var report = LevelValidator.Validate(Level(new[]
            {
                "-ES/0 -ESW/0 -SW/0",
                "*NES#R/0 %EW+NS/0 @NSW#A/0",
                "-NE/0 -NEW/0 -NW/0"
            }));

            Assert.IsTrue(Has(report, LevelIssueSeverity.Warning, "close nothing off"));
        }

        [Test]
        public void ABriarThatHoldsTheDarkApartIsNotCalledOut()
        {
            var report = LevelValidator.Validate(Level(Ford));

            Assert.IsFalse(Has(report, LevelIssueSeverity.Warning, "close nothing off"),
                           "an unlit briar is not evidence of anything; what matters is what its ways touch");
            foreach (var issue in report.Issues)
                Assert.AreNotEqual(LevelIssueSeverity.Error, issue.Severity, issue.Message);
        }

        [Test]
        public void ARootedBriarMustAlreadyReadAsSolved()
        {
            var report = LevelValidator.Validate(Level(new[]
            {
                "*E#R/0 @WS#A/0 .",
                "-E/0 %EW+NS/1! -W/0",
                ". xN/0 ."
            }));

            Assert.IsTrue(Has(report, LevelIssueSeverity.Error, "can never be turned"));
        }

        [Test]
        public void ABrittleBriarMustSurviveTheTurnsItOwes()
        {
            var report = LevelValidator.Validate(Level(new[]
            {
                "*E#R/0 @WS#A/0 .",
                "-E/0 %NE+SW/1~2 -W/0",
                ". xN/0 ."
            }));

            Assert.IsTrue(Has(report, LevelIssueSeverity.Error, "survives only 2"),
                          "a twisted briar one turn out owes three, which is one more than it has");
        }

        [Test]
        public void BriarsOnOneTaprootMustAgree()
        {
            // A straight briar reads as itself every half turn round and a twisted one only
            // every whole turn, so one of each — the straight one authored a turn out, the
            // twisted one already right — can never both be right at the same moment.
            var report = LevelValidator.Validate(Level(new[]
            {
                "*E#R/0 @WS#A/0 .",
                "-E/0 %EW+NS/1&A -W/0",
                "-E/0 %NE+SW/0&A -W/0",
                ". xN/0 ."
            }));

            Assert.IsTrue(Has(report, LevelIssueSeverity.Error, "never all be right at once"));
        }

        // ---------------------------------------------------------------- the lesson
        [Test]
        public void ABoardWithABriarTeachesIt()
        {
            var sightings = MechanicScan.InBoard(Board(Gate));

            bool found = false;
            foreach (var s in sightings)
                if (s.Mechanic.Equals(Mechanic.Briar))
                {
                    found = true;
                    Assert.AreEqual(4, s.CellIndex, "and points at the briar itself");
                }

            Assert.IsTrue(found, "a briar cannot be worked out from the board alone");
        }
    }
}
