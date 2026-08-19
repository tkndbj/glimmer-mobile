using System;
using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Content;
using GlimmerGrove.Homestead;
using GlimmerGrove.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The grove: what is held, where it stands, and the two merges underneath.
    ///
    /// <para>
    /// The feature costs the save file two fields, and they are joined by different rules on
    /// purpose. What was bought is an <em>entitlement</em> — union, invariant 15, the shape
    /// <c>companionsOwned</c> already proved. Where things stand is an <em>instruction</em> —
    /// recency per slot, invariant 11c, the shape the keeper's name needed after a year of
    /// losing itself. Most of this file pins the second, because it is the one that can lose
    /// something and the one this project has got wrong before.
    /// </para>
    /// <para>
    /// The last section reads the shipped catalog. That is deliberate: everything above it
    /// proves the machinery, and the machinery being right is no comfort if the content is
    /// missing a starter plot or has put a price on a resident.
    /// </para>
    /// </summary>
    public sealed class HomesteadTests
    {
        /// <summary>Progress a test can state outright, so no save file is involved.</summary>
        sealed class FakeProgress : IHomesteadProgress
        {
            public readonly HashSet<string> Cleared = new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> Finished = new HashSet<string>(StringComparer.Ordinal);

            public bool IsCleared(LevelId level) => Cleared.Contains(level.Value);
            public bool IsChapterFinished(ChapterId chapter) => Finished.Contains(chapter.Value);
        }

        FakeProgress _progress;

        /// <summary>
        /// The catalog, the ledger and the layout are process-wide statics, so a test that
        /// published one would leave it published for whatever runs next. The same guard
        /// <c>CompanionPurchaseTests</c> uses, for the same reason.
        /// </summary>
        [SetUp]
        public void Reset()
        {
            _progress = new FakeProgress();
            HomesteadProgress.Set(_progress);
            HomesteadLedger.ResetForTests();
            HomesteadLayout.ResetForTests();
            HomesteadService.ResetForTests();
        }

        [TearDown]
        public void Restore()
        {
            HomesteadProgress.Set(null);
            HomesteadLedger.ResetForTests();
            HomesteadLayout.ResetForTests();
            HomesteadService.ResetForTests();
        }

        // ------------------------------------------------------------- fixtures
        static HomesteadPiece Free(string id)
            => new HomesteadPiece(id, "Homestead/" + id, false, HomesteadPieceKind.Decor,
                                  0, LevelId.None, ChapterId.None, 1f, .5f);

        static HomesteadPiece Priced(string id, int cost)
            => new HomesteadPiece(id, "Homestead/" + id, false, HomesteadPieceKind.Decor,
                                  cost, LevelId.None, ChapterId.None, 1f, .5f);

        static HomesteadPiece EarnedByLevel(string id, string level)
            => new HomesteadPiece(id, "Homestead/" + id, false, HomesteadPieceKind.Decor,
                                  0, LevelId.Parse(level), ChapterId.None, 1f, .5f);

        static HomesteadPiece EarnedByChapter(string id, string chapter, int cost = 0)
            => new HomesteadPiece(id, "Homestead/" + id, false, HomesteadPieceKind.Decor,
                                  cost, LevelId.None, ChapterId.Parse(chapter), 1f, .5f);

        static HomesteadPlacementDto Row(string slot, string piece, long at)
            => new HomesteadPlacementDto { slot = slot, piece = piece, setUnix = at };

        static Dictionary<string, HomesteadPlacementDto> ById(HomesteadPlacementDto[] rows)
        {
            var map = new Dictionary<string, HomesteadPlacementDto>(StringComparer.Ordinal);
            foreach (var row in rows) map[row.slot] = row;
            return map;
        }

        // =========================================================== the rule
        [Test]
        public void APieceWithNoRequirementAndNoPriceIsHeldFromTheFirstLaunch()
        {
            // The starter furniture. Without at least one of these a new player opens the
            // picker onto an empty list, which is why ContentValidation errors on a catalog
            // that has none.
            Assert.IsTrue(HomesteadLedger.IsHeld(Free("pebble")));
            Assert.IsTrue(Free("pebble").IsStarter);
        }

        [Test]
        public void APricedPieceIsNotHeldUntilItIsBought()
        {
            var bench = Priced("bench", 400);

            Assert.IsFalse(HomesteadLedger.IsHeld(bench));
            Assert.IsFalse(bench.IsStarter, "a price is a gate, so it cannot also be a starter");
        }

        [Test]
        public void ADecorPieceIsHeldOnceItsGladeIsCleared()
        {
            // The earned half of the rule for decor. Residents no longer take this route at
            // all — they are the companion roster, gated by keeper level or bought — which is
            // what GroveResidentTests pins.
            var arch = EarnedByLevel("stone_arch", "c01_first_light");

            Assert.IsFalse(HomesteadLedger.IsHeld(arch));

            _progress.Cleared.Add("c01_first_light");

            Assert.IsTrue(HomesteadLedger.IsHeld(arch));
            Assert.IsTrue(HomesteadLedger.IsEarned(arch));
            Assert.IsFalse(HomesteadLedger.WasBought(arch),
                           "earned is not bought; a panel that says 'you paid for this' must be able to tell");
        }

        [Test]
        public void APieceIsHeldByEitherRouteAndTheLedgerOwnsTheWholeRule()
        {
            // Invariant 15a, the reason ReachedBy is named the way it is: the composite rule
            // lives in exactly one place, and a call site that checked only the earned half
            // would draw a padlock over something somebody paid for.
            var arch = EarnedByChapter("arch", "c01_shallows", cost: 900);

            Assert.IsFalse(HomesteadLedger.IsHeld(arch));

            var save = new SaveFileDto { homesteadOwned = new[] { "arch" } };
            HomesteadLedger.LoadFrom(save);

            Assert.IsTrue(HomesteadLedger.IsHeld(arch), "bought is one of the two routes");
            Assert.IsFalse(HomesteadLedger.IsEarned(arch), "and the earned half is still false");

            HomesteadLedger.ResetForTests();
            _progress.Finished.Add("c01_shallows");

            Assert.IsTrue(HomesteadLedger.IsHeld(arch), "finishing the chapter is the other");
        }

        [Test]
        public void AChapterNobodyCanPlayIsNeverFinished()
        {
            // LivePlayerProgress reads "every glade cleared", and a chapter the catalog does
            // not carry has none — so the naive reading is vacuously true and would hand out a
            // plot for a chapter that does not exist. The plot ladder is authored ahead of the
            // chapters that open it, so this is not hypothetical.
            var live = new LivePlayerProgress();

            Assert.IsFalse(live.IsChapterFinished(ChapterId.Parse("c99_nowhere")));
            Assert.IsFalse(live.IsChapterFinished(ChapterId.None));
        }

        // ================================================ purchases: a union
        [Test]
        public void PurchasesJoinAsAUnionInEitherOrder()
        {
            var a = new[] { "bench", "oak" };
            var b = new[] { "lantern", "oak" };

            var left = HomesteadLedger.Join(a, b);
            var right = HomesteadLedger.Join(b, a);

            CollectionAssert.AreEqual(new[] { "bench", "lantern", "oak" }, left);
            CollectionAssert.AreEqual(left, right, "a join is commutative or it is not a join");
        }

        [Test]
        public void JoiningPurchasesAgainstNothingStillSorts()
        {
            // No early return for an empty side, deliberately. Handing one array straight back
            // would skip the sort, and SaveDelta walks these in order — so an unsorted file
            // would read as changed on every launch and push a write for nothing, forever.
            var unsorted = new[] { "oak", "bench" };

            CollectionAssert.AreEqual(new[] { "bench", "oak" }, HomesteadLedger.Join(unsorted, null));
            CollectionAssert.AreEqual(new[] { "bench", "oak" }, HomesteadLedger.Join(null, unsorted));
        }

        [Test]
        public void AnIdThisBuildDoesNotKnowSurvivesTheJoin()
        {
            // A piece bought on a newer build must not be confiscated by a trip through an
            // older one. It costs one short string; losing it costs a purchase.
            var joined = HomesteadLedger.Join(new[] { "from_the_future" }, new[] { "bench" });

            CollectionAssert.Contains(joined, "from_the_future");
        }

        [Test]
        public void HoldingAPieceIsPermissionToDrawItEverywhereRatherThanOneCopy()
        {
            // The decision the whole save shape rests on. A count of copies would be hearts'
            // old mistake — two devices at 3 and 1 are equally consistent with "one bought two
            // more" and "one has not heard" — so a purchase buys the right to draw a piece and
            // nothing limits how often.
            HomesteadLedger.LoadFrom(new SaveFileDto { homesteadOwned = new[] { "fence" } });

            Assert.IsTrue(HomesteadLayout.Place("a", "fence"));
            Assert.IsTrue(HomesteadLayout.Place("b", "fence"));
            Assert.IsTrue(HomesteadLayout.Place("c", "fence"));

            Assert.AreEqual("fence", HomesteadLayout.At("a"));
            Assert.AreEqual("fence", HomesteadLayout.At("c"));

            var save = new SaveFileDto();
            HomesteadLedger.WriteInto(save);

            CollectionAssert.AreEqual(new[] { "fence" }, save.homesteadOwned,
                                      "three placements are still one entitlement");
        }

        // ============================================ placement: recency, per slot
        [Test]
        public void TheLaterDecisionWinsPerSlotAndOtherSlotsAreUntouched()
        {
            var mine = new[] { Row("a", "oak", 100), Row("b", "bench", 100) };
            var other = new[] { Row("a", "well", 200) };

            var joined = ById(HomesteadLayout.Join(mine, other));

            Assert.AreEqual("well", joined["a"].piece, "the later choice for that slot");
            Assert.AreEqual("bench", joined["b"].piece, "and nothing else moved");
        }

        [Test]
        public void ADeviceWithNoOpinionAboutASlotCannotFlattenOneThatHasOne()
        {
            // Invariant 11c's second half, and the mistake that lost the keeper's name for a
            // year: an untouched slot writes no row at all, so absence means "no opinion"
            // rather than "empty". A fresh install joins as nothing and takes the grove whole.
            var arranged = new[] { Row("a", "oak", 500), Row("b", "well", 500) };
            var fresh = Array.Empty<HomesteadPlacementDto>();

            var joined = ById(HomesteadLayout.Join(arranged, fresh));

            Assert.AreEqual(2, joined.Count);
            Assert.AreEqual("oak", joined["a"].piece);
            Assert.AreEqual("well", joined["b"].piece);
        }

        [Test]
        public void ClearingASlotIsAChoiceAndBeatsAStaleDeviceThatStillHasThePiece()
        {
            // Which is why a cleared slot keeps its row with an empty piece rather than being
            // deleted. A deletion would read as "never touched" and the stale side would put
            // the tree straight back on the next sync.
            var cleared = new[] { Row("a", string.Empty, 900) };
            var stale = new[] { Row("a", "oak", 400) };

            var joined = ById(HomesteadLayout.Join(cleared, stale));

            Assert.AreEqual(string.Empty, joined["a"].piece);
            Assert.AreEqual(900, joined["a"].setUnix);
        }

        [Test]
        public void ThePlacementJoinIsIdempotentCommutativeAndAssociative()
        {
            // Invariant 11's promise about every merge in the save file. Without all three a
            // sync is order-dependent, and two devices can push over each other for ever.
            var a = new[] { Row("a", "oak", 100), Row("b", "bench", 300) };
            var b = new[] { Row("a", "well", 200), Row("c", "lantern", 50) };
            var c = new[] { Row("b", string.Empty, 400) };

            var ab = HomesteadLayout.Join(a, b);
            var ba = HomesteadLayout.Join(b, a);
            CollectionAssert.AreEqual(Flat(ab), Flat(ba), "commutative");

            CollectionAssert.AreEqual(Flat(ab), Flat(HomesteadLayout.Join(ab, ab)), "idempotent");

            var left = HomesteadLayout.Join(HomesteadLayout.Join(a, b), c);
            var right = HomesteadLayout.Join(a, HomesteadLayout.Join(b, c));
            CollectionAssert.AreEqual(Flat(left), Flat(right), "associative");
        }

        static List<string> Flat(HomesteadPlacementDto[] rows)
        {
            var flat = new List<string>();
            foreach (var row in rows) flat.Add($"{row.slot}={row.piece}@{row.setUnix}");
            return flat;
        }

        [Test]
        public void TwoDevicesThatDecidedInTheSameSecondStillAgree()
        {
            // Two files that predate the stamps both carry zero, so ties are not exotic.
            // Ordinal order is not a better answer than the alternative, only a stable one —
            // and stability is what keeps the join commutative.
            var a = new[] { Row("a", "oak", 0) };
            var b = new[] { Row("a", "bench", 0) };

            Assert.AreEqual(HomesteadLayout.Join(a, b)[0].piece,
                            HomesteadLayout.Join(b, a)[0].piece);
        }

        [Test]
        public void RowsAreWrittenSortedBySlotSoAnUnchangedGroveSyncsNothing()
        {
            // SaveChecksum hashes the serialised file and SaveDelta walks these in order, so
            // dictionary order would make an unchanged grove read as changed on every launch.
            HomesteadLayout.Place("zebra", "oak");
            HomesteadLayout.Place("alpha", "bench");
            HomesteadLayout.Place("middle", "well");

            var save = new SaveFileDto();
            HomesteadLayout.WriteInto(save);

            var slots = new List<string>();
            foreach (var row in save.homesteadPlaced) slots.Add(row.slot);

            CollectionAssert.AreEqual(new[] { "alpha", "middle", "zebra" }, slots);
        }

        [Test]
        public void ARowForASlotThisBuildDoesNotKnowSurvivesTheJoin()
        {
            // A grove arranged on a device a content drop ahead must not be flattened by a
            // trip through an older build.
            var joined = ById(HomesteadLayout.Join(new[] { Row("future_plot_a", "oak", 100) },
                                                   new[] { Row("meadow_a", "bench", 100) }));

            Assert.IsTrue(joined.ContainsKey("future_plot_a"));
            Assert.IsTrue(joined.ContainsKey("meadow_a"));
        }

        [Test]
        public void PlacingWhatIsAlreadyThereChangesNothing()
        {
            HomesteadLayout.Place("a", "oak");

            Assert.IsFalse(HomesteadLayout.Place("a", "oak"),
                           "a repeat tap must not restamp the slot, or it would outrank a real "
                           + "choice made on another device a moment earlier");
        }

        [Test]
        public void AnUnknownPieceIdDrawsNothingAndIsNotErased()
        {
            // A save from a newer build naming a piece this one has never heard of. Drawing an
            // empty slot is right; deleting the row is not.
            var catalog = new HomesteadCatalog(GroveFloor.Empty, new[] { Free("bench") });

            HomesteadLayout.LoadFrom(new SaveFileDto
            {
                homesteadPlaced = new[] { Row("a", "from_the_future", 100) }
            });

            Assert.IsFalse(HomesteadLayout.PieceAt(catalog, "a").IsValid, "nothing to draw");
            Assert.AreEqual("from_the_future", HomesteadLayout.At("a"), "and the row is intact");

            var save = new SaveFileDto();
            HomesteadLayout.WriteInto(save);
            Assert.AreEqual("from_the_future", save.homesteadPlaced[0].piece);
        }

        // ========================================================= the mapper
        /// <summary>
        /// Reaches <c>JsonUtility</c> before the mapper does.
        ///
        /// <para>
        /// The mapper treats content as hostile input and turns every parse failure into a
        /// reported problem rather than an exception — which is right in the game and unhelpful
        /// here, because outside the Editor <c>JsonUtility</c> is a native call with no
        /// implementation and the mapper would report "not valid JSON" about JSON that is
        /// perfectly valid. Touching it directly lets the offline runner see the engine call
        /// and mark the test as needing the Editor, instead of failing it for the wrong reason.
        /// </para>
        /// </summary>
        static void RequiresJsonUtility() => JsonUtility.FromJson<HomesteadBodyDto>("{}");

        [Test]
        public void AnAuthoredResidentIsRefusedRatherThanReadAsDecor()
        {
            RequiresJsonUtility();

            // Residents are the companion roster, projected in — so a row claiming to be one
            // is a second creature list with its own price and its own unlock rule, which is
            // exactly the duplication projection removed. Refused rather than salvaged: read
            // as decor it would stand a critter on the fences shelf and sell it, and the whole
            // point is that there is one answer to "who lives here".
            var problems = new List<string>();
            string json = "{\"schemaVersion\":3,\"floor\":{\"cols\":4,\"rows\":4,\"hallTile\":\"t_000_000\",\"regions\":[{\"id\":\"home\",\"col\":0,\"row\":0,\"cols\":4,\"rows\":4}]},\"pieces\":[" +
                          "{\"id\":\"sunmote\",\"art\":\"Critters/c1\",\"animated\":true," +
                          "\"kind\":\"resident\",\"cost\":900}]}";

            Assert.IsTrue(HomesteadMapper.TryRead(json, problems, out var catalog));

            Assert.IsFalse(catalog.Find("sunmote").IsValid, "the row is dropped");
            Assert.IsTrue(problems.Count > 0, "and it is reported, not swallowed");
        }

        [Test]
        public void TwoTilesCanNeverShareAnId()
        {
            // A slot id keys a map in the save file, so two of them sharing one would put a tree
            // placed in one spot into another and the merge would treat two independent choices
            // as one. On the islands that was a rule the mapper had to *enforce*, because slots
            // were hand-authored and an author could type the same id twice. A tile id is a pure
            // function of its coordinates, so the collision is now unrepresentable rather than
            // merely refused — which is the stronger version of the same guarantee and the reason
            // the check that used to live in HomesteadMapper is gone.
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int col = 0; col < 40; col++)
                for (int row = 0; row < 40; row++)
                    Assert.IsTrue(seen.Add(GroveFloor.TileId(col, row)),
                                  $"({col},{row}) produced an id another tile already had");

            Assert.AreEqual(1600, seen.Count);
        }

        [Test]
        public void AnIdThatWouldNotSurviveASaveFileIsRefused()
        {
            RequiresJsonUtility();

            var problems = new List<string>();
            string json = "{\"schemaVersion\":3,\"floor\":{\"cols\":4,\"rows\":4,\"hallTile\":\"t_000_000\",\"regions\":[{\"id\":\"home\",\"col\":0,\"row\":0,\"cols\":4,\"rows\":4}]},\"pieces\":[" +
                          "{\"id\":\"Bad Id!\",\"kind\":\"decor\",\"cost\":10}]}";

            Assert.IsTrue(HomesteadMapper.TryRead(json, problems, out var catalog));

            Assert.AreEqual(0, catalog.PieceCount);
            Assert.IsTrue(problems.Count > 0);
        }

        // ========================================================= the floor
        static GroveFloor Field(int cols, int rows, params GroveRegion[] regions)
            => new GroveFloor(cols, rows, string.Empty, GroveFloor.TileId(0, 0),
                              GroveFloor.TileId(1, 0), regions);

        [Test]
        public void ATileIdRoundTripsAndIsFixedWidth()
        {
            // It is a key in the save file, so it is permanent (invariant 1) and it is walked in
            // order by SaveDelta - which is why it is zero-padded. An ordering that changed with
            // the size of a number would make an unchanged save look changed on every launch.
            Assert.AreEqual("t_007_003", GroveFloor.TileId(7, 3));
            Assert.AreEqual("t_012_140", GroveFloor.TileId(12, 140));

            Assert.IsTrue(GroveFloor.TryParse("t_007_003", out int col, out int row));
            Assert.AreEqual(7, col);
            Assert.AreEqual(3, row);

            Assert.IsFalse(GroveFloor.TryParse("meadow_a", out _, out _),
                           "an island's old slot id is not a tile");
            Assert.IsFalse(GroveFloor.TryParse(null, out _, out _));
        }

        [Test]
        public void TheIsometricTransformInverts()
        {
            // A tap has to land on the tile the finger is over, and the only way that holds for
            // every tile is if the inverse really is one.
            for (int col = 0; col < 6; col++)
                for (int row = 0; row < 6; row++)
                {
                    GroveFloor.TileAt(GroveFloor.TileX(col, row), GroveFloor.TileY(col, row),
                                      out int c, out int r);

                    Assert.AreEqual(col, c, $"column of ({col},{row})");
                    Assert.AreEqual(row, r, $"row of ({col},{row})");
                }
        }

        [Test]
        public void DrawOrderPutsNearerTilesLater()
        {
            // The one thing a field needs that the islands did not: what stands in front is a
            // consequence of where the player put things, so it has to be computed.
            Assert.Less(GroveFloor.DrawOrder(0, 0), GroveFloor.DrawOrder(1, 0));
            Assert.Less(GroveFloor.DrawOrder(0, 0), GroveFloor.DrawOrder(0, 1));
            Assert.Less(GroveFloor.DrawOrder(3, 1), GroveFloor.DrawOrder(2, 3));

            // Total, so two tiles at the same depth still have a stable order - a tie that
            // resolved differently between frames would flicker.
            Assert.AreNotEqual(GroveFloor.DrawOrder(2, 1), GroveFloor.DrawOrder(1, 2));
        }

        [Test]
        public void DepthOrderIsTotalSoAWholeWindowCanBeSorted()
        {
            // The field is drawn by sorting the visible tiles once and applying sibling indices
            // in order. That only works if the ordering is a strict total order — the first
            // version assigned an index per tile as it was realised, and SetSiblingIndex
            // *inserts*, so every tile behind the one just placed shifted and the field came out
            // looking sorted while the hall drew in front of the companion one tile nearer.
            var tiles = new List<(int c, int r)>();
            for (int c = 0; c < 8; c++)
                for (int r = 0; r < 8; r++) tiles.Add((c, r));

            var order = new HashSet<int>();
            foreach (var t in tiles)
                Assert.IsTrue(order.Add(GroveFloor.DrawOrder(t.c, t.r)),
                              $"({t.c},{t.r}) shares a depth with another tile");

            // And it agrees with the eye: further down the screen is later.
            foreach (var a in tiles)
                foreach (var b in tiles)
                    if (a.c + a.r < b.c + b.r)
                        Assert.Less(GroveFloor.DrawOrder(a.c, a.r), GroveFloor.DrawOrder(b.c, b.r),
                                    $"({a.c},{a.r}) must draw behind ({b.c},{b.r})");
        }

        [Test]
        public void StarterLandIsOwnedAndPricedLandIsNot()
        {
            var floor = Field(4, 4,
                              new GroveRegion("home", 0, 0, 2, 4, 0),
                              new GroveRegion("east", 2, 0, 2, 4, 3000));

            Assert.IsTrue(GroveLand.IsOwned(floor, 0, 0), "the ground a new grove starts on");
            Assert.IsFalse(GroveLand.IsOwned(floor, 3, 0), "and the ground it does not");

            GroveLand.LoadFrom(new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                groveLandOwned = new[] { "east" },
            });

            Assert.IsTrue(GroveLand.IsOwned(floor, 3, 0));
            Assert.IsTrue(GroveLand.WasBought(floor.Region("east")));
            Assert.IsFalse(GroveLand.WasBought(floor.Region("home")), "free is not bought");
        }

        [Test]
        public void StarterLandIsNeverWrittenDown()
        {
            // A region with no price is owned by everyone, so writing it into the save would be
            // a stored default that says nothing - and "absent" and "bought nothing" have to
            // stay the same fact for the union merge to need no sentinel.
            GroveLand.LoadFrom(new SaveFileDto { schemaVersion = SaveSchema.Version });

            var file = new SaveFileDto();
            GroveLand.WriteInto(file);

            CollectionAssert.IsEmpty(file.groveLandOwned);
        }

        [Test]
        public void LandIsJoinedByUnionAndSorted()
        {
            var joined = GroveLand.Join(new[] { "east", "north" }, new[] { "north", "west" });

            CollectionAssert.AreEqual(new[] { "east", "north", "west" }, joined,
                                      "buying cannot be undone, so between two devices the " +
                                      "player owns whatever either bought");

            CollectionAssert.AreEqual(joined, GroveLand.Join(joined, joined), "idempotent");
            CollectionAssert.AreEqual(joined, GroveLand.Join(new[] { "west", "east", "north" }, null),
                                      "and sorted, or an unchanged save reads as changed for ever");
        }

        [Test]
        public void NothingCanBePlacedOnTheHallsTile()
        {
            var floor = Field(4, 4, new GroveRegion("home", 0, 0, 4, 4, 0));

            Assert.IsFalse(GroveLand.IsBuildable(floor, 0, 0), "the hall draws itself there");
            Assert.IsTrue(GroveLand.IsBuildable(floor, 2, 2));
        }

        // ================================================== the shipped catalog
        static string CatalogPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "StreamingAssets",
                                          "Content", "homestead.json"));

        static HomesteadCatalog Shipped(out List<string> problems)
        {
            problems = new List<string>();
            Assert.IsTrue(File.Exists(CatalogPath), "no grove catalog at " + CatalogPath);
            Assert.IsTrue(HomesteadMapper.TryRead(File.ReadAllText(CatalogPath), problems, out var catalog));
            return catalog;
        }

        [Test]
        public void TheShippedCatalogReadsCleanly()
        {
            var catalog = Shipped(out var problems);

            CollectionAssert.IsEmpty(problems);
            Assert.Greater(catalog.Floor.Regions.Count, 0);
            Assert.Greater(catalog.PieceCount, 0);
            Assert.Greater(catalog.SlotCount, 0);
        }

        [Test]
        public void TheShippedCatalogOpensOntoSomethingOnAFirstLaunch()
        {
            // The two states that ship looking broken: a grove of nothing but padlocks, and a
            // picker with an empty list. Both are errors in ContentValidation as well; this is
            // the half that runs without an Editor.
            var catalog = Shipped(out _);

            bool anyLand = false;
            foreach (var region in catalog.Floor.Regions) if (region.IsStarter) anyLand = true;

            bool anyPiece = false;
            foreach (var piece in catalog.Pieces) if (piece.IsStarter) anyPiece = true;

            Assert.IsTrue(anyLand, "no ground is free from the first launch");
            Assert.IsTrue(anyPiece, "no piece is free from the first launch");

            // And the hall has to stand on ground a new player already owns, or the feature
            // opens onto a padlock where the house should be.
            var hall = catalog.Floor.RegionOf(catalog.Floor.HallTile);
            Assert.IsNotNull(hall, "the hall stands on no region at all");
            Assert.IsTrue(hall.IsStarter, "the hall stands on ground a new player must buy");
        }

        [Test]
        public void TheShippedCatalogAuthorsNoResidents()
        {
            // The file is decor and the home ladder; the creatures come from the manifest's
            // companion roster. Asserted against the shipped body rather than only against the
            // mapper, because the failure this catches is somebody re-adding the rows by hand
            // after reading an old comment.
            var catalog = Shipped(out _);

            foreach (var piece in catalog.Authored)
                Assert.IsFalse(piece.IsResident,
                               $"'{piece.Id}' is authored as a resident; residents are the roster");
        }

        [Test]
        public void EveryShippedSlotIdIsUniqueAndPlain()
        {
            // Both are enforced by the mapper, which drops what it cannot accept — so a catalog
            // that reads cleanly cannot violate them. This asserts against the file's own count
            // instead, which is what catches a slot silently disappearing.
            var catalog = Shipped(out _);

            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var region in catalog.Floor.Regions)
                for (int c = region.Col; c < region.Col + region.Cols; c++)
                    for (int r = region.Row; r < region.Row + region.Rows; r++)
                        Assert.IsTrue(seen.Add(GroveFloor.TileId(c, r)),
                                      $"tile {GroveFloor.TileId(c, r)} is in two regions, so who " +
                                      "owns it would depend on the order of the file");

            Assert.LessOrEqual(seen.Count, catalog.SlotCount,
                               "a region runs off the field");
        }
    }
}
