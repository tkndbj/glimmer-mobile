using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// A tip is shown once in a player's entire life with the game, so both halves have
    /// to be right: the scan must find the mechanic that is actually on the board, and
    /// the ledger must never forget that a lesson was taught.
    ///
    /// The scan is derived from the board rather than declared per level, which is the
    /// property worth protecting — it means a chapter shipped a year from now teaches
    /// its mechanics with no authoring and no list to keep in step.
    /// </summary>
    public sealed class TipTests
    {
        static Puzzle Board(int w, int h, string[] rows, LevelTuning tuning = null)
        {
            var parsed = LevelGridParser.Parse(new LevelLayout(w, h, rows));
            Assert.IsTrue(parsed.Ok, string.Join("; ", parsed.Errors));

            return new Puzzle(LevelId.Parse("t_level"), w, h,
                              tuning ?? LevelTuning.Default(3), parsed.Cells);
        }

        /// <summary>A board with no budget, so the budget tip does not crowd the test.</summary>
        static LevelTuning NoBudget => new LevelTuning(3, 0f, 0f, 3, LevelTuning.Unlimited);

        /// <summary>The head of the teaching queue, or an invalid sighting when empty.</summary>
        static MechanicSighting First(Puzzle board, System.Func<Mechanic, bool> seen)
        {
            var queue = MechanicScan.Unseen(board, seen);
            return queue.Count == 0 ? default : queue[0];
        }

        static bool Contains(List<MechanicSighting> found, Mechanic wanted)
        {
            foreach (var s in found) if (s.Mechanic.Equals(wanted)) return true;
            return false;
        }

        // -------------------------------------------------------------- the scan

        [Test]
        public void AFragileConduitIsFound()
        {
            var board = Board(3, 1, new[] { "*E#R/0 -EW/0~3 @W#R/0" }, NoBudget);
            Assert.IsTrue(Contains(MechanicScan.InBoard(board), Mechanic.FragileConduit));
        }

        [Test]
        public void ABoardWithoutAMechanicDoesNotClaimIt()
        {
            var board = Board(2, 1, new[] { "*E#R/0 @W#R/0" }, NoBudget);
            var found = MechanicScan.InBoard(board);

            Assert.IsFalse(Contains(found, Mechanic.FragileConduit));
            Assert.IsFalse(Contains(found, Mechanic.MoveBudget));
            Assert.IsFalse(Contains(found, Mechanic.RootedTile));
        }

        /// <summary>
        /// A glade bringing two ideas queues both rather than holding one back for a
        /// later glade that happens to repeat it — the player would otherwise meet the
        /// second one unexplained in between.
        /// </summary>
        [Test]
        public void EveryUnseenMechanicOnABoardIsQueued()
        {
            // a rooted tile and a critter wanting a blend, on one board
            var board = Board(3, 1, new[] { "*E#R/0 @EW#M/0! *W#B/0" });

            var queue = MechanicScan.Unseen(board, _ => false);

            Assert.AreEqual(3, queue.Count, "budget, rooted and blending are all new here");
            Assert.IsTrue(queue[0].Mechanic.Equals(Mechanic.MoveBudget), queue[0].Mechanic.ToString());
            Assert.IsTrue(queue[1].Mechanic.Equals(Mechanic.RootedTile), queue[1].Mechanic.ToString());
            Assert.IsTrue(queue[2].Mechanic.Equals(Mechanic.ColourMixing), queue[2].Mechanic.ToString());
        }

        [Test]
        public void AlreadyTaughtMechanicsAreLeftOutOfTheQueue()
        {
            var board = Board(3, 1, new[] { "*E#R/0 @EW#M/0! *W#B/0" });

            var queue = MechanicScan.Unseen(board, m => m.Equals(Mechanic.MoveBudget));

            Assert.AreEqual(2, queue.Count);
            Assert.IsFalse(queue.Exists(s => s.Mechanic.Equals(Mechanic.MoveBudget)));
        }

        [Test]
        public void ACritterWantingABlendIsPointedAt()
        {
            var board = Board(3, 1, new[] { "*E#R/0 @EW#M/0 *W#B/0" }, NoBudget);
            var found = MechanicScan.InBoard(board);

            Assert.IsTrue(Contains(found, Mechanic.ColourMixing));

            foreach (var s in found)
                if (s.Mechanic.Equals(Mechanic.ColourMixing))
                    Assert.AreEqual(1, s.CellIndex, "the tip should ring the critter that wants the blend");
        }

        /// <summary>Two hearts alone are not a blend — twin_streams wants them apart.</summary>
        [Test]
        public void TwoHeartColoursWithNoBlendedCritterTeachNothing()
        {
            var board = Board(3, 1, new[] { "*E#R/0 @EW#R/0 *W#B/0" }, NoBudget);
            Assert.IsFalse(Contains(MechanicScan.InBoard(board), Mechanic.ColourMixing));
        }

        /// <summary>Brittle conduits lead: they are the only lesson that costs something.</summary>
        [Test]
        public void TheBrittleConduitIsTaughtBeforeTheBudget()
        {
            var board = Board(3, 1, new[] { "*E#R/0 -EW/0~3 @W#R/0" });

            var first = First(board, _ => false);
            Assert.IsTrue(first.Mechanic.Equals(Mechanic.FragileConduit), first.Mechanic.ToString());

            var next = First(board, m => m.Equals(Mechanic.FragileConduit));
            Assert.IsTrue(next.Mechanic.Equals(Mechanic.MoveBudget), next.Mechanic.ToString());

            var none = First(board, _ => true);
            Assert.IsFalse(none.Mechanic.IsValid, "a veteran player is never interrupted");
        }

        [Test]
        public void ADuskcapIsFoundAndPointedAt()
        {
            var board = Board(3, 2, new[] { "*E#R/0 @W#R/0 .", "-E/0 xW/0 ." }, NoBudget);
            var found = MechanicScan.InBoard(board);

            Assert.IsTrue(Contains(found, Mechanic.Duskcap));

            foreach (var s in found)
                if (s.Mechanic.Equals(Mechanic.Duskcap))
                    Assert.AreEqual(4, s.CellIndex, "the tip should ring the duskcap itself");
        }

        [Test]
        public void BoundConduitsAreFound()
        {
            var board = Board(4, 1, new[] { "*E#R/0 -EW/1&A -EW/1&A @W#R/0" }, NoBudget);
            Assert.IsTrue(Contains(MechanicScan.InBoard(board), Mechanic.BoundConduit));
        }

        /// <summary>
        /// A duskcap changes what winning is and nothing on screen can explain that; a
        /// taproot announces itself the first time it is tapped, because two tiles visibly
        /// move. So the rule the board cannot demonstrate goes first.
        /// </summary>
        [Test]
        public void TheDuskcapIsTaughtBeforeTheTaproot()
        {
            var board = Board(4, 2, new[]
            {
                "*E#R/0 -EW/1&A -EW/1&A @W#R/0",
                "-E/0 xW/0 . .",
            }, NoBudget);

            var queue = MechanicScan.Unseen(board, _ => false);

            Assert.AreEqual(2, queue.Count);
            Assert.IsTrue(queue[0].Mechanic.Equals(Mechanic.Duskcap), queue[0].Mechanic.ToString());
            Assert.IsTrue(queue[1].Mechanic.Equals(Mechanic.BoundConduit), queue[1].Mechanic.ToString());
        }




        [Test]
        public void ABudgetedBoardTeachesTheBudgetWithNoCellToPointAt()
        {
            var board = Board(2, 1, new[] { "*E#R/0 @W#R/0" });   // default tuning has a budget
            var found = MechanicScan.InBoard(board);

            Assert.IsTrue(Contains(found, Mechanic.MoveBudget));

            foreach (var s in found)
                if (s.Mechanic.Equals(Mechanic.MoveBudget))
                    Assert.IsFalse(s.HasCell, "the budget lives in the HUD, not in a cell");
        }

        // ------------------------------------------------------- choosing the one



        [Test]
        public void EveryMechanicHasAPlaceInTheTeachingOrder()
        {
            // A mechanic missing from the order can be detected but never taught, which
            // is the kind of gap that only shows up as "why did nobody see this tip".
            var all = new[] { Mechanic.FragileConduit, Mechanic.MoveBudget,
                              Mechanic.RootedTile, Mechanic.ColourMixing,
                              Mechanic.Duskcap, Mechanic.BoundConduit };

            foreach (var m in all)
            {
                bool listed = false;
                foreach (var o in Mechanic.TeachingOrder) if (o.Equals(m)) listed = true;
                Assert.IsTrue(listed, $"'{m}' is not in TeachingOrder and can never be shown");
            }
        }

        // ------------------------------------------------------------ the ledger
        [Test]
        public void JoiningSeenTipsIsAUnion()
        {
            var joined = TipLedger.Join(new[] { "fragile" }, new[] { "moves" });

            Assert.AreEqual(2, joined.Length);
            Assert.Contains("fragile", joined);
            Assert.Contains("moves", joined);
        }

        [Test]
        public void TheUnionIsIdempotentAndOrderIndependent()
        {
            var a = new[] { "rooted", "moves" };
            var b = new[] { "moves", "fragile" };

            var ab = TipLedger.Join(a, b);
            var ba = TipLedger.Join(b, a);
            Assert.AreEqual(ab, ba);

            Assert.AreEqual(ab, TipLedger.Join(ab, TipLedger.Join(a, b)));
        }

        [Test]
        public void AnEmptyOrMissingSideKeepsTheOther()
        {
            Assert.AreEqual(1, TipLedger.Join(new[] { "fragile" }, null).Length);
            Assert.AreEqual(1, TipLedger.Join(null, new[] { "fragile" }).Length);
            Assert.AreEqual(0, TipLedger.Join(null, null).Length);
        }

        /// <summary>
        /// A lesson learned on a newer build must survive a trip through an older one,
        /// or the player is taught it again the moment they come back.
        /// </summary>
        [Test]
        public void AnUnknownMechanicIdIsCarriedThroughRatherThanDropped()
        {
            var joined = TipLedger.Join(new[] { "fragile" }, new[] { "some_future_mechanic" });

            Assert.Contains("some_future_mechanic", joined);
        }

        [Test]
        public void TheUnionIsSortedSoAnUnchangedSetProducesNoSyncTraffic()
        {
            var joined = TipLedger.Join(new[] { "moves", "rooted" }, new[] { "fragile" });

            var sorted = new List<string>(joined);
            sorted.Sort(System.StringComparer.Ordinal);
            Assert.AreEqual(sorted.ToArray(), joined, "an unsorted union would look like a change every sync");
        }
    }
}
