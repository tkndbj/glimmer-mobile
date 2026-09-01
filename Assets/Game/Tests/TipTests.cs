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
        static LevelTuning NoBudget => new LevelTuning(3, 0f, 0f, LevelTuning.Unlimited);

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

        /// <summary>The sighting for one mechanic, or an invalid one when the board has none.</summary>
        static MechanicSighting Sighting(List<MechanicSighting> found, Mechanic wanted)
        {
            foreach (var s in found) if (s.Mechanic.Equals(wanted)) return s;
            return default;
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

        /// <summary>
        /// The lesson says two hearts join and their light mixes, so it has to be able to
        /// point at the two hearts. Ringing the gold critter alone shows the question and
        /// none of the answer, and leaves a first-timer hunting the board for the rest of
        /// the sentence.
        /// </summary>
        [Test]
        public void TheHeartsBehindABlendArePointedAtTheSame()
        {
            var board = Board(3, 1, new[] { "*E#R/0 @EW#M/0 *W#B/0" }, NoBudget);
            var found = MechanicScan.InBoard(board);

            var blend = Sighting(found, Mechanic.ColourMixing);
            Assert.AreEqual(new[] { 0, 2 }, blend.Alongside, "both hearts, in reading order");

            // Every other lesson here is a fact about one tile and names nothing else.
            var rooted = Sighting(found, Mechanic.RootedTile);
            Assert.IsFalse(rooted.Mechanic.IsValid);
        }

        /// <summary>
        /// A heart of the right colour that the solution never joins to the critter is not
        /// where its light comes from, however near it stands. Pointing at one would teach a
        /// rule this glade does not follow — and this board is exactly the trap, because the
        /// stray heart is nearer than the one that really feeds it.
        /// </summary>
        [Test]
        public void AHeartTheSolutionNeverJoinsIsNotNamed()
        {
            var board = Board(4, 2, new[]
            {
                "*E#R/0 -EW/0 @EW#M/0 *W#B/0",
                ". . *N#R/0 .",
            }, NoBudget);

            var blend = Sighting(MechanicScan.InBoard(board), Mechanic.ColourMixing);

            Assert.AreEqual(new[] { 0, 3 }, blend.Alongside,
                            "the stray red heart at 6 is the nearest one and mates with nothing");
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
        public void ACrossingIsFoundAndPointedAt()
        {
            var board = Board(3, 3, Crossed, NoBudget);
            var found = MechanicScan.InBoard(board);

            Assert.IsTrue(Contains(found, Mechanic.Crossing));

            foreach (var s in found)
                if (s.Mechanic.Equals(Mechanic.Crossing))
                    Assert.AreEqual(4, s.CellIndex, "the tip should ring the crossing itself");
        }

        /// <summary>Green passing north to south through the red passing east to west.</summary>
        static readonly string[] Crossed =
        {
            ". *S#G/0 .",
            "*E#R/0 =EW+NS/0 @W#R/0",
            ". @N#G/0 .",
        };

        /// <summary>The same crossing, with a taproot reaching either side of it.</summary>
        static readonly string[] CrossedAndBound =
        {
            ". . *S#G/0 . .",
            "*E#R/0 -EW/1&A =EW+NS/0 -EW/1&A @W#R/0",
            ". . @N#G/0 . .",
        };

        [Test]
        public void BoundConduitsAreFound()
        {
            var board = Board(4, 1, new[] { "*E#R/0 -EW/1&A -EW/1&A @W#R/0" }, NoBudget);
            Assert.IsTrue(Contains(MechanicScan.InBoard(board), Mechanic.BoundConduit));
        }

        /// <summary>
        /// A crossing cannot be worked out and can be misread — a four-armed tile is a
        /// crossroads everywhere else in this game — whereas a taproot announces itself the
        /// first time it is tapped, because two tiles visibly move. So the tile that says
        /// nothing about itself goes first.
        /// </summary>
        [Test]
        public void TheCrossingIsTaughtBeforeTheTaproot()
        {
            var board = Board(5, 3, CrossedAndBound, NoBudget);

            var queue = MechanicScan.Unseen(board, _ => false);

            Assert.AreEqual(2, queue.Count);
            Assert.IsTrue(queue[0].Mechanic.Equals(Mechanic.Crossing), queue[0].Mechanic.ToString());
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

        // ---------------------------------------------------- a critter that is not fussy

        /// <summary>
        /// The lesson is a contrast, so it needs both kinds standing on one board: the
        /// unfussy critter it rings, and a fussy one for "any" to be the absence of.
        /// </summary>
        [Test]
        public void AnUnfussyCritterBesideAFussyOneIsPointedAt()
        {
            var board = Board(4, 1, new[] { "@E#A/0 *EW#R/0 -EW/0 @W#R/0" }, NoBudget);
            var found = MechanicScan.InBoard(board);

            Assert.IsTrue(Contains(found, Mechanic.AnyLight));
            Assert.AreEqual(0, Sighting(found, Mechanic.AnyLight).CellIndex,
                            "the tip should ring the critter that is not asking for a colour");
        }

        /// <summary>
        /// The opening glade is every critter unfussy and no colour rule anywhere on it, so
        /// there is nothing for "any light" to be the absence of. Taught there, a once-in-a-
        /// lifetime lesson could never be shown on the first board that mixes the two.
        /// </summary>
        [Test]
        public void ABoardOfNothingButUnfussyCrittersTeachesNothingAboutColour()
        {
            var board = Board(3, 1, new[] { "@E#A/0 *EW#R/0 @W#A/0" }, NoBudget);

            Assert.IsFalse(Contains(MechanicScan.InBoard(board), Mechanic.AnyLight));
        }

        /// <summary>And nor does a board where every critter is asking for something.</summary>
        [Test]
        public void ABoardOfNothingButFussyCrittersDoesNotClaimTheLesson()
        {
            var board = Board(3, 1, new[] { "@E#R/0 *EW#R/0 @W#R/0" }, NoBudget);

            Assert.IsFalse(Contains(MechanicScan.InBoard(board), Mechanic.AnyLight));
        }

        /// <summary>
        /// A critter wanting a blend is a fussy critter, so it is the other half of the
        /// contrast — a board mixing the two teaches both, in that order.
        /// </summary>
        [Test]
        public void ABlendedCritterIsWhatAnUnfussyOneIsContrastedWith()
        {
            var board = Board(4, 1, new[] { "@E#A/0 *EW#R/0 @EW#M/0 *W#B/0" }, NoBudget);

            var queue = MechanicScan.Unseen(board, _ => false);

            Assert.AreEqual(2, queue.Count);
            Assert.IsTrue(queue[0].Mechanic.Equals(Mechanic.AnyLight), queue[0].Mechanic.ToString());
            Assert.IsTrue(queue[1].Mechanic.Equals(Mechanic.ColourMixing), queue[1].Mechanic.ToString());
        }

        // ------------------------------------------------- what a board teaches at all

        /// <summary>
        /// The review key is offered on a glade whose lessons the player has already been
        /// shown — which is every glade it will ever be offered on, since a first-timer meets
        /// the tips on the way in. So the scan behind it must be blind to the ledger.
        /// </summary>
        [Test]
        public void WhatABoardTeachesIsAFactAboutTheBoardAndNotAboutThePlayer()
        {
            var board = Board(3, 1, new[] { "*E#R/0 @EW#M/0! *W#B/0" });

            Assert.AreEqual(0, MechanicScan.Unseen(board, _ => true).Count,
                            "nothing is new to a player who has met everything");
            Assert.AreEqual(3, MechanicScan.Taught(board).Count,
                            "the board still teaches all three, and that is what the review shows");
        }

        /// <summary>
        /// The two readings walk one list. A second walk could come to disagree about what a
        /// glade contains or what order it is taught in, and the disagreement would show up as
        /// a review that teaches a mechanic the opening sequence never mentioned.
        /// </summary>
        [Test]
        public void TheUnseenQueueIsAFilterOfWhatTheBoardTeaches()
        {
            var board = Board(3, 1, new[] { "*E#R/0 @EW#M/0! *W#B/0" });

            var all = MechanicScan.Taught(board);
            var unseen = MechanicScan.Unseen(board, m => m.Equals(Mechanic.RootedTile));

            Assert.AreEqual(all.Count - 1, unseen.Count);

            // Same order, same cells, one entry short.
            Assert.IsTrue(unseen[0].Mechanic.Equals(all[0].Mechanic));
            Assert.IsTrue(unseen[1].Mechanic.Equals(all[2].Mechanic));
            Assert.AreEqual(all[2].CellIndex, unseen[1].CellIndex);
        }

        /// <summary>
        /// A glade with nothing to teach gets no review key, and that is the overwhelming
        /// majority of them. An empty list is the only thing that can say so.
        /// </summary>
        [Test]
        public void ABoardWithNothingToTeachOffersNoReview()
        {
            var board = Board(2, 1, new[] { "*E#R/0 @W#R/0" }, NoBudget);

            Assert.AreEqual(0, MechanicScan.Taught(board).Count);
        }

        [Test]
        public void WhatABoardTeachesComesBackInTeachingOrder()
        {
            var board = Board(5, 3, CrossedAndBound, NoBudget);

            var all = MechanicScan.Taught(board);

            Assert.AreEqual(2, all.Count);
            Assert.IsTrue(all[0].Mechanic.Equals(Mechanic.Crossing), all[0].Mechanic.ToString());
            Assert.IsTrue(all[1].Mechanic.Equals(Mechanic.BoundConduit), all[1].Mechanic.ToString());
        }

        // ------------------------------------------------------- choosing the one


        static bool InOrder(Mechanic m)
        {
            foreach (var o in Mechanic.TeachingOrder) if (o.Equals(m)) return true;
            return false;
        }

        [Test]
        public void EveryBoardMechanicHasAPlaceInTheTeachingOrder()
        {
            // A mechanic missing from the order can be detected but never taught, which
            // is the kind of gap that only shows up as "why did nobody see this tip".
            // Everything MechanicScan can report belongs here; the crossing was added to
            // the scan a chapter after this list was written and went unlisted for it.
            var board = new[] { Mechanic.FragileConduit, Mechanic.MoveBudget,
                                Mechanic.RootedTile, Mechanic.AnyLight,
                                Mechanic.ColourMixing,
                                Mechanic.Crossing, Mechanic.Briar,
                                Mechanic.BoundConduit };

            foreach (var m in board)
                Assert.IsTrue(InOrder(m), $"'{m}' is not in TeachingOrder and can never be shown");

            Assert.AreEqual(board.Length, Mechanic.TeachingOrder.Length,
                            "the teaching order is the board's queue and holds nothing else");
        }

        /// <summary>
        /// A lesson about a screen must never be queued by a board.
        ///
        /// Both live on <see cref="Mechanic"/> because everything about a lesson is already
        /// there — a permanent id, strings derived from it, and a union-joined ledger that
        /// reaches the cloud with no new save field. What separates them is the queue:
        /// <see cref="Mechanic.TeachingOrder"/> is what a glade walks, and a grove tip
        /// appearing on a board would be a modal about a shop over a puzzle.
        /// </summary>
        [Test]
        public void AScreensLessonIsNeverQueuedByAGlade()
        {
            Assert.IsFalse(InOrder(Mechanic.Grove));
            Assert.IsFalse(InOrder(Mechanic.GroveShop));
            Assert.IsFalse(InOrder(Mechanic.ModeSwitch));
        }

        /// <summary>
        /// The mode lesson's id is not a near-miss of another one.
        ///
        /// <para>
        /// A lesson id travels in the save file exactly like a level id, so a typo is not a
        /// compile error and not a wrong string — it is a lesson silently sharing a ledger
        /// entry with a different lesson, which reads as "that tip never appears" for one of
        /// them and can only be repaired by re-teaching everybody. The switcher's obvious id
        /// was <c>modes</c>, one letter from the move budget's <c>moves</c>; this pins the
        /// distance rather than the spelling, so it also catches the next pair.
        /// </para>
        /// </summary>
        [Test]
        public void NoTwoLessonIdsAreOneLetterApart()
        {
            foreach (var a in Mechanic.All)
                foreach (var b in Mechanic.All)
                {
                    if (a.Equals(b)) continue;

                    Assert.Greater(Distance(a.Id, b.Id), 1,
                                   $"'{a}' and '{b}' are one edit apart, so a typo in either " +
                                   "is a lesson quietly recorded against the other");
                }
        }

        /// <summary>Levenshtein distance, capped at 2 — nothing here needs a larger answer.</summary>
        static int Distance(string a, string b)
        {
            if (System.Math.Abs(a.Length - b.Length) > 1) return 2;

            var previous = new int[b.Length + 1];
            var current = new int[b.Length + 1];

            for (int j = 0; j <= b.Length; j++) previous[j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                current[0] = i;

                for (int j = 1; j <= b.Length; j++)
                    current[j] = System.Math.Min(System.Math.Min(current[j - 1] + 1, previous[j] + 1),
                                                 previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));

                var swap = previous; previous = current; current = swap;
            }

            return System.Math.Min(2, previous[b.Length]);
        }

        /// <summary>
        /// <c>Mechanic.All</c> is what the build gate walks to prove every lesson has its two
        /// strings, so a mechanic missing from it ships with its loc keys printed on screen.
        /// </summary>
        [Test]
        public void EveryLessonIsListedOnceInAll()
        {
            var ids = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (var m in Mechanic.All)
            {
                Assert.IsTrue(m.IsValid, "a mechanic with no id can never be recorded as seen");
                Assert.IsTrue(ids.Add(m.Id), $"'{m}' is listed in All twice");
            }

            foreach (var m in Mechanic.TeachingOrder)
                Assert.IsTrue(ids.Contains(m.Id), $"'{m}' is taught and is not in All, so nothing " +
                                                  "proves it has strings");

            Assert.IsTrue(ids.Contains(Mechanic.Grove.Id));
            Assert.IsTrue(ids.Contains(Mechanic.GroveShop.Id));
            Assert.IsTrue(ids.Contains(Mechanic.ModeSwitch.Id));
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
