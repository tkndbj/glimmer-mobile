using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Homestead;
using GlimmerGrove.Persistence;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The home, the slot roles, and the island's own answer to "did I build this".
    ///
    /// <para>
    /// Three rules arrived together because they are one change: the grove had no centre, no
    /// composition and no before-and-after, so it read as stickers on a lawn however much was
    /// placed on it. What they have in common is the reason they are testable offline — none of
    /// them stores anything. The home is a maximum over an entitlement set, a fit is a question
    /// about two enums, and a tended stage is a function of the arrangement already in the save
    /// file. No new field, no new merge rule, no schema bump.
    /// </para>
    /// </summary>
    public sealed class HomeLadderTests
    {
        sealed class FakeProgress : IHomesteadProgress
        {
            public readonly HashSet<string> Cleared = new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> Finished = new HashSet<string>(StringComparer.Ordinal);

            public bool IsCleared(LevelId level) => Cleared.Contains(level.Value);
            public bool IsChapterFinished(ChapterId chapter) => Finished.Contains(chapter.Value);
        }

        [SetUp]
        public void Reset()
        {
            HomesteadProgress.Set(new FakeProgress());
            HomesteadLedger.ResetForTests();
            HomesteadLayout.ResetForTests();
        }

        [TearDown]
        public void Restore()
        {
            HomesteadProgress.Set(null);
            HomesteadLedger.ResetForTests();
            HomesteadLayout.ResetForTests();
        }

        // ------------------------------------------------------------- fixtures
        static HomesteadPiece Home(string id, int tier, int cost)
            => new HomesteadPiece(id, "Homestead/cottage", false, HomesteadPieceKind.Dwelling,
                                  cost, LevelId.None, ChapterId.None, 1f, .45f,
                                  HomesteadSlotKind.Ground, tier);

        static HomesteadPiece Decor(string id, HomesteadSlotKind slot, int cost = 0)
            => new HomesteadPiece(id, "Homestead/" + id, false, HomesteadPieceKind.Decor,
                                  cost, LevelId.None, ChapterId.None, 1f, .5f, slot);

        static HomesteadPiece Resident(string id)
            => new HomesteadPiece(id, "Critters/c1", true, HomesteadPieceKind.Resident,
                                  0, LevelId.None, ChapterId.None, 1f, .45f);

        /// <summary>A 2x2 field owned outright, with the hall on its first tile.</summary>
        static GroveFloor Field()
            => new GroveFloor(2, 2, string.Empty, GroveFloor.TileId(0, 0), string.Empty,
                              new[] { new GroveRegion("home", 0, 0, 2, 2, 0) });

        static HomesteadCatalog Ladder()
            => new HomesteadCatalog(
                Field(),
                new[] { Home("cottage", 1, 0), Home("lodge", 2, 2500), Home("hall", 3, 6000) });

        static void Own(params string[] ids)
            => HomesteadLedger.LoadFrom(new SaveFileDto { homesteadOwned = ids });

        // ============================================================= the home
        [Test]
        public void ANewGroveLivesInTheFirstRungWithoutOwningAnything()
        {
            // The first rung is free, so the hearth is never empty. ContentValidation errors on
            // a catalog whose cheapest home has a price for exactly this reason: an island with
            // a ring where the house goes is the emptiest possible first impression.
            var best = HomesteadLedger.BestDwelling(Ladder());

            Assert.AreEqual("cottage", best.Id);
            Assert.AreEqual(1, best.Tier);
        }

        [Test]
        public void TheHomeIsTheBestTierOwnedAndNotTheLastOneBought()
        {
            var catalog = Ladder();

            // Bought out of order, which a player cannot do today but a support tool, a rollback
            // or a future "skip a rung" offer all can. The answer is a maximum over the set, so
            // the order they arrived in cannot matter — the same property every other join in
            // the save file has.
            Own("hall", "cottage", "lodge");

            Assert.AreEqual("hall", HomesteadLedger.BestDwelling(catalog).Id);

            HomesteadLedger.ResetForTests();
            Own("lodge", "hall", "cottage");

            Assert.AreEqual("hall", HomesteadLedger.BestDwelling(catalog).Id,
                            "the home must not depend on the order the set is read in");
        }

        [Test]
        public void OwningAHigherRungNeverTakesTheLowerOneAway()
        {
            // Union is the join and buying is irreversible, so the ladder is monotonic: a
            // player who owns the hall owns the cottage too, and a merge with a device that
            // has only the cottage cannot demote them. That is invariant 15's whole argument
            // for storing entitlements rather than a level.
            var catalog = Ladder();
            Own("cottage", "lodge", "hall");

            Assert.IsTrue(HomesteadLedger.IsHeld(catalog.Find("cottage")));
            Assert.IsTrue(HomesteadLedger.IsHeld(catalog.Find("lodge")));

            // The ladder is a set of ids in the same section every other purchase lives in, so
            // "best owned" is a maximum over what is held — idempotent, order-independent and
            // impossible to lose in a merge. A rung is bought once, so its count is one.
            var joined = HomesteadLedger.Join(
                new[] { new HomesteadStockDto { id = "cottage", copies = 1 } },
                new[] { new HomesteadStockDto { id = "cottage", copies = 1 },
                        new HomesteadStockDto { id = "lodge", copies = 1 },
                        new HomesteadStockDto { id = "hall", copies = 1 } });

            CollectionAssert.AreEqual(new[] { "cottage", "hall", "lodge" },
                                      System.Array.ConvertAll(joined, r => r.id));
        }

        [Test]
        public void TheNextRungIsTheLowestUnownedOneAboveTheHome()
        {
            var catalog = Ladder();

            Assert.AreEqual("lodge", HomesteadLedger.NextDwelling(catalog).Id);

            Own("cottage", "lodge");

            Assert.AreEqual("hall", HomesteadLedger.NextDwelling(catalog).Id);

            Own("cottage", "lodge", "hall");

            Assert.IsFalse(HomesteadLedger.NextDwelling(catalog).IsValid,
                           "the top of the ladder has no next rung, which is what the panel " +
                           "renders as praise rather than as a dead button");
        }

        [Test]
        public void ADwellingIsNeverPlacedByHandAndEverythingElseIs()
        {
            // What makes the hall safe to derive: a tile the player can place into is a tile
            // whose contents live in the save file, and the home deliberately does not.
            Assert.IsFalse(Home("cottage", 1, 0).CanBePlaced);

            Assert.IsTrue(Decor("fence", HomesteadSlotKind.Edge).CanBePlaced);
            Assert.IsTrue(Resident("sunmote").CanBePlaced);
        }

        [Test]
        public void EveryPieceFitsEveryTileNow()
        {
            // The slot-kind rule went with the islands. It existed to stop a sprinkle of
            // pre-placed dots looking accidental; a field has no dots, so where a thing goes is
            // the player's decision and narrowing it would take the feature back out. The kind
            // survives as a shop shelf - see GroveShelf.
            var fence = Decor("fence_low", HomesteadSlotKind.Edge);

            Assert.IsTrue(fence.CanBePlaced);
            Assert.AreEqual(HomesteadSlotKind.Edge, fence.Slot, "still a shelf, just not a rule");
            Assert.AreEqual(GroveShelf.Edge, GroveShelves.Of(fence));
        }

        [Test]
        public void APieceFromACatalogThatPredatesSlotKindsIsGround()
        {
            // Every optional content field here has to keep an older catalog working, because
            // remote delivery means a client can be a drop behind.
            var old = new HomesteadPiece("pebble", "Homestead/pebble", false, HomesteadPieceKind.Decor,
                                         0, LevelId.None, ChapterId.None, 1f, .5f);

            Assert.AreEqual(HomesteadSlotKind.Ground, old.Slot);
            Assert.IsTrue(old.CanBePlaced);
        }

        // ========================================================== the tending
        [Test]
        public void TheHallsTileIsNotCountedTowardsARegionBeingFinished()
        {
            // Four tiles and one of them is the hall. Counting it would make a region that can
            // never read as finished, because nothing is ever placed there.
            var floor = Field();
            var region = floor.Region("home");

            Assert.AreEqual(0f, HomesteadLayout.FillOf(floor, region));

            HomesteadLayout.Place(GroveFloor.TileId(1, 0), "daisies");
            HomesteadLayout.Place(GroveFloor.TileId(0, 1), "fence_low");

            Assert.AreEqual(2f / 3f, HomesteadLayout.FillOf(floor, region), .001f,
                            "three placeable tiles, not four");

            HomesteadLayout.Place(GroveFloor.TileId(1, 1), "pebble");

            Assert.AreEqual(1f, HomesteadLayout.FillOf(floor, region), .001f);
            Assert.AreEqual(TendedStage.Bloomed, GroveTending.Of(floor, region));
        }

        [Test]
        public void ARegionWithNowhereToPlaceIsNeverFinished()
        {
            // A hall-only region, which the catalog should not contain but a drop could produce.
            // Zero over zero is 0 rather than 1: ground nobody can furnish must not award the
            // finished state for free.
            var floor = new GroveFloor(1, 1, string.Empty, GroveFloor.TileId(0, 0), string.Empty,
                                       new[] { new GroveRegion("perch", 0, 0, 1, 1, 0) });

            Assert.AreEqual(0f, HomesteadLayout.FillOf(floor, floor.Region("perch")));
            Assert.AreEqual(TendedStage.Bare, GroveTending.Of(floor, floor.Region("perch")));
        }
    }
}
