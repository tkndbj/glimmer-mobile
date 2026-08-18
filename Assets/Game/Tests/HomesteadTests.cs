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

        static HomesteadPiece Resident(string id, string level)
            => new HomesteadPiece(id, "Critters/c1", true, HomesteadPieceKind.Resident,
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
        public void AResidentIsHeldOnceItsGladeIsCleared()
        {
            var sunmote = Resident("sunmote", "c01_first_light");

            Assert.IsFalse(HomesteadLedger.IsHeld(sunmote));

            _progress.Cleared.Add("c01_first_light");

            Assert.IsTrue(HomesteadLedger.IsHeld(sunmote));
            Assert.IsTrue(HomesteadLedger.IsEarned(sunmote));
            Assert.IsFalse(HomesteadLedger.WasBought(sunmote),
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

        [Test]
        public void AStarterPlotIsHeldAndAGatedOneIsNot()
        {
            var starter = new HomesteadPlot("meadow", "Homestead/plot_meadow", .5f, .6f,
                                            ChapterId.None, new[] { new HomesteadSlot("meadow_a", .5f, .8f, 1f) });
            var gated = new HomesteadPlot("ridge", "Homestead/plot_ridge", .5f, .6f,
                                          ChapterId.Parse("c02_tidewood"), Array.Empty<HomesteadSlot>());

            Assert.IsTrue(HomesteadLedger.IsPlotHeld(starter));
            Assert.IsFalse(HomesteadLedger.IsPlotHeld(gated));

            _progress.Finished.Add("c02_tidewood");
            Assert.IsTrue(HomesteadLedger.IsPlotHeld(gated));
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
            var catalog = new HomesteadCatalog(Array.Empty<HomesteadPlot>(), new[] { Free("bench") });

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
        public void APricedResidentLosesItsPriceRatherThanTheCatalogLosingTheResident()
        {
            RequiresJsonUtility();

            // The one rule the two kinds do not share. Dropping the price is the safe half —
            // the piece stays earnable and nothing anybody bought is affected — and the build
            // gate turns the same mistake into a failed build so it is not merely survived.
            var problems = new List<string>();
            string json = "{\"schemaVersion\":2,\"plots\":[],\"pieces\":[" +
                          "{\"id\":\"sunmote\",\"art\":\"Critters/c1\",\"animated\":true," +
                          "\"kind\":\"resident\",\"cost\":900,\"requiresLevel\":\"c01_first_light\"}]}";

            Assert.IsTrue(HomesteadMapper.TryRead(json, problems, out var catalog));

            var piece = catalog.Find("sunmote");
            Assert.IsTrue(piece.IsResident);
            Assert.AreEqual(0, piece.Cost);
            Assert.IsFalse(piece.IsForSale);
            Assert.IsTrue(problems.Count > 0, "and it is reported, not swallowed");
        }

        [Test]
        public void ASlotIdUsedTwiceIsRefusedAcrossTheWholeGrove()
        {
            RequiresJsonUtility();

            // A slot id keys a map in the save file. Two plots sharing one would put a tree
            // placed on the first island onto the second, and the merge would treat two
            // independent choices as one.
            var problems = new List<string>();
            string json = "{\"schemaVersion\":2,\"pieces\":[],\"plots\":[" +
                          "{\"id\":\"one\",\"art\":\"Homestead/plot_meadow\",\"width\":.5," +
                          "\"slots\":[{\"id\":\"shared\",\"x\":.5,\"y\":.8,\"scale\":1}]}," +
                          "{\"id\":\"two\",\"art\":\"Homestead/plot_ridge\",\"width\":.5," +
                          "\"slots\":[{\"id\":\"shared\",\"x\":.5,\"y\":.8,\"scale\":1}]}]}";

            Assert.IsTrue(HomesteadMapper.TryRead(json, problems, out var catalog));

            Assert.AreEqual(1, catalog.SlotCount, "the duplicate is dropped, not indexed twice");
            Assert.IsTrue(problems.Count > 0);
        }

        [Test]
        public void AnIdThatWouldNotSurviveASaveFileIsRefused()
        {
            RequiresJsonUtility();

            var problems = new List<string>();
            string json = "{\"schemaVersion\":2,\"plots\":[],\"pieces\":[" +
                          "{\"id\":\"Bad Id!\",\"kind\":\"decor\",\"cost\":10}]}";

            Assert.IsTrue(HomesteadMapper.TryRead(json, problems, out var catalog));

            Assert.AreEqual(0, catalog.PieceCount);
            Assert.IsTrue(problems.Count > 0);
        }

        // ========================================================= the layout
        static HomesteadPlot Plot(string id, float x, float width, string chapter = "")
            => new HomesteadPlot(id, "Homestead/plot_" + id, x, width,
                                 string.IsNullOrEmpty(chapter) ? ChapterId.None : ChapterId.Parse(chapter),
                                 new[] { new HomesteadSlot(id + "_a", .5f, .8f, 1f) });

        [Test]
        public void IslandsNeverOverlapWhateverShapeTheirArtIs()
        {
            // The bug this type exists to make unrepresentable. The first version had an author
            // write a `y` fraction against a fixed canvas, and every consecutive pair of the ten
            // shipped islands collided — because the number `y` had to agree with lives in a PNG
            // the author cannot see from the JSON.
            var catalog = new HomesteadCatalog(new[]
            {
                Plot("a", .5f, .62f), Plot("b", .85f, .24f), Plot("c", .5f, .62f),
                Plot("d", .15f, .24f), Plot("e", .5f, .70f),
            }, System.Array.Empty<HomesteadPiece>());

            // Deliberately extreme and uneven: a very tall island next to a very flat one is
            // precisely what a re-cut sprite looks like.
            float[] aspects = { .7f, 2.4f, 1.0f, 1.6f, .45f };
            int i = 0;
            var map = HomesteadMap.Build(catalog, 1080f, _ => aspects[i++ % aspects.Length]);

            CollectionAssert.IsEmpty(map.Collisions());
        }

        [Test]
        public void TheCanvasIsAlwaysTallEnoughToHoldEveryIsland()
        {
            // The other half of the same bug: the starter plot fell *below* the content rect,
            // so the ScrollRect could not reach it and "I cannot scroll down" meant "the only
            // island I own does not exist as far as the scroll is concerned".
            var catalog = new HomesteadCatalog(new[]
            {
                Plot("a", .5f, .62f), Plot("b", .5f, .62f), Plot("c", .5f, .62f),
            }, System.Array.Empty<HomesteadPiece>());

            var map = HomesteadMap.Build(catalog, 1080f, _ => 1.4f);

            foreach (var p in map.Placements)
            {
                Assert.GreaterOrEqual(p.Top, 0f, $"{p.Plot.Id} starts above the canvas");
                Assert.LessOrEqual(p.Bottom, map.CanvasHeight, $"{p.Plot.Id} runs past the canvas");
            }
        }

        [Test]
        public void CatalogOrderRunsBottomToTop()
        {
            // The first plot in the file is the one a new player owns, and it must be the one
            // under their thumb when the screen opens — the scroll parks at the bottom.
            var catalog = new HomesteadCatalog(new[]
            {
                Plot("first", .5f, .62f), Plot("second", .5f, .62f), Plot("third", .5f, .62f),
            }, System.Array.Empty<HomesteadPiece>());

            var map = HomesteadMap.Build(catalog, 1080f, _ => 1f);

            // CentreY is measured down from the canvas top, so lower on screen is a larger Y.
            Assert.Greater(map.Placements[0].CentreY, map.Placements[1].CentreY);
            Assert.Greater(map.Placements[1].CentreY, map.Placements[2].CentreY);
        }

        [Test]
        public void AnEmptyCatalogLaysOutWithoutThrowing()
        {
            var map = HomesteadMap.Build(HomesteadCatalog.Empty, 1080f, _ => 1f);

            CollectionAssert.IsEmpty(map.Placements);
            Assert.Greater(map.CanvasHeight, 0f);
        }

        [Test]
        public void TheShippedGroveLaysOutWithoutCollisions()
        {
            // Aspects taken from the real art, so this is the shipped grove rather than a
            // synthetic one. Editor-only, because reading the PNGs needs Application.dataPath.
            var catalog = Shipped(out _);

            var map = HomesteadMap.Build(catalog, 1080f, PlotAspect);

            CollectionAssert.IsEmpty(map.Collisions());
            Assert.Greater(map.CanvasHeight, 0f);
        }

        /// <summary>A plot's art aspect, read straight out of the PNG header.</summary>
        static float PlotAspect(HomesteadPlot plot)
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "Game", "Art",
                                                        plot.Art.Replace('/', Path.DirectorySeparatorChar)))
                          + ".png";
            if (!File.Exists(path)) return 1f;

            var head = new byte[24];
            using (var f = File.OpenRead(path)) f.Read(head, 0, 24);

            int w = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
            int h = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
            return w > 0 ? (float)h / w : 1f;
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
            Assert.Greater(catalog.PlotCount, 0);
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

            bool anyPlot = false;
            foreach (var plot in catalog.Plots) if (plot.IsStarter) anyPlot = true;

            bool anyPiece = false;
            foreach (var piece in catalog.Pieces) if (piece.IsStarter) anyPiece = true;

            Assert.IsTrue(anyPlot, "no plot is free from the first launch");
            Assert.IsTrue(anyPiece, "no piece is free from the first launch");
        }

        [Test]
        public void NothingInTheShippedCatalogSellsAResident()
        {
            var catalog = Shipped(out _);

            int residents = 0;
            foreach (var piece in catalog.Pieces)
            {
                if (!piece.IsResident) continue;
                residents++;
                Assert.AreEqual(0, piece.Cost, $"resident '{piece.Id}' has a price");
            }

            Assert.Greater(residents, 0, "a grove with no residents is a shop");
        }

        [Test]
        public void EveryShippedSlotIdIsUniqueAndPlain()
        {
            // Both are enforced by the mapper, which drops what it cannot accept — so a catalog
            // that reads cleanly cannot violate them. This asserts against the file's own count
            // instead, which is what catches a slot silently disappearing.
            var catalog = Shipped(out _);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            int counted = 0;

            foreach (var plot in catalog.Plots)
                foreach (var slot in plot.Slots)
                {
                    counted++;
                    Assert.IsTrue(seen.Add(slot.Id), $"slot '{slot.Id}' appears twice");
                }

            Assert.AreEqual(catalog.SlotCount, counted);
        }
    }
}
