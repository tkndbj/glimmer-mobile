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

        static HomesteadSlot Slot(string id, HomesteadSlotKind kind)
            => new HomesteadSlot(id, .5f, .7f, 1f, kind);

        static HomesteadCatalog Ladder()
            => new HomesteadCatalog(
                new[] { new HomesteadPlot("meadow", "Homestead/plot_meadow", .5f, .9f, ChapterId.None,
                                          new[] { Slot("meadow_hearth", HomesteadSlotKind.Hearth),
                                                  Slot("meadow_a", HomesteadSlotKind.Bed),
                                                  Slot("meadow_b", HomesteadSlotKind.Edge),
                                                  Slot("meadow_c", HomesteadSlotKind.Ground) }) },
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

            var joined = HomesteadLedger.Join(new[] { "cottage" }, new[] { "cottage", "lodge", "hall" });

            CollectionAssert.AreEqual(new[] { "cottage", "hall", "lodge" }, joined);
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
        public void ADwellingBelongsToTheHearthAndNothingElseDoes()
        {
            var home = Home("cottage", 1, 0);

            Assert.IsTrue(home.Fits(HomesteadSlotKind.Hearth));
            Assert.IsFalse(home.Fits(HomesteadSlotKind.Ground));

            // Nothing else may stand there, which is what makes the hearth safe to derive: a
            // slot the player can place into is a slot whose contents live in the save file,
            // and the home deliberately does not.
            Assert.IsFalse(Decor("fence", HomesteadSlotKind.Edge).Fits(HomesteadSlotKind.Hearth));
            Assert.IsFalse(Resident("sunmote").Fits(HomesteadSlotKind.Hearth));
        }

        // ============================================================= the fits
        [Test]
        public void DecorFitsItsOwnKindOfSlotAndNoOther()
        {
            var fence = Decor("fence_low", HomesteadSlotKind.Edge);

            Assert.IsTrue(fence.Fits(HomesteadSlotKind.Edge));
            Assert.IsFalse(fence.Fits(HomesteadSlotKind.Bed));
            Assert.IsFalse(fence.Fits(HomesteadSlotKind.Ground));
        }

        [Test]
        public void AResidentStandsAnywhereButTheHearth()
        {
            // The one exception, and it is a design decision rather than a shortcut: a creature
            // on a path, in a flower bed or under a tree is right in every case, and telling
            // somebody where their own rescued critter may not stand turns a toy into a form.
            var sunmote = Resident("sunmote");

            Assert.IsTrue(sunmote.Fits(HomesteadSlotKind.Ground));
            Assert.IsTrue(sunmote.Fits(HomesteadSlotKind.Bed));
            Assert.IsTrue(sunmote.Fits(HomesteadSlotKind.Path));
            Assert.IsTrue(sunmote.Fits(HomesteadSlotKind.Canopy));
            Assert.IsFalse(sunmote.Fits(HomesteadSlotKind.Hearth));
        }

        [Test]
        public void APieceFromACatalogThatPredatesSlotKindsIsGround()
        {
            // Every optional content field here has to keep an older catalog working, because
            // remote delivery means a client can be a drop behind. A piece with no `slot` is
            // ground, which is what every piece was before the field existed.
            var old = new HomesteadPiece("pebble", "Homestead/pebble", false, HomesteadPieceKind.Decor,
                                         0, LevelId.None, ChapterId.None, 1f, .5f);

            Assert.AreEqual(HomesteadSlotKind.Ground, old.Slot);
            Assert.IsTrue(old.Fits(HomesteadSlotKind.Ground));

            var slot = new HomesteadSlot("meadow_a", .5f, .7f, 1f);
            Assert.AreEqual(HomesteadSlotKind.Ground, slot.Kind);
        }

        // ========================================================== the tending
        [Test]
        public void TheHearthIsNotCountedTowardsAnIslandBeingFinished()
        {
            // Three placeable slots and a hearth. Counting the hearth would make an island that
            // can never read as finished, because nothing is ever placed on it.
            var plot = Ladder().Plots[0];

            Assert.AreEqual(3, plot.PlaceableCount);
            Assert.AreEqual(0f, HomesteadLayout.FillOf(plot));

            HomesteadLayout.Place("meadow_a", "daisies");
            HomesteadLayout.Place("meadow_b", "fence_low");
            HomesteadLayout.Place("meadow_c", "pebble");

            Assert.AreEqual(1f, HomesteadLayout.FillOf(plot));
            Assert.AreEqual(TendedStage.Bloomed, GroveTending.Of(plot));
        }

        [Test]
        public void TheFirstThingPlacedMovesTheIslandOffBare()
        {
            // The moment the habit forms, so it is deliberately the cheapest threshold in the
            // rule: one piece out of eleven is enough to stop an island looking untouched.
            Assert.AreEqual(TendedStage.Bare, GroveTending.Of(0f));
            Assert.AreEqual(TendedStage.Started, GroveTending.Of(.05f));
            Assert.AreEqual(TendedStage.Growing, GroveTending.Of(GroveTending.GrowingAt));
            Assert.AreEqual(TendedStage.Lush, GroveTending.Of(GroveTending.LushAt));
            Assert.AreEqual(TendedStage.Bloomed, GroveTending.Of(1f));
        }

        [Test]
        public void AnIslandOneSlotShortIsNotBloomed()
        {
            // Bloomed has to mean finished — it is the only stage that lights the island and
            // spawns fireflies, and a stage that arrives early is a reward for nothing.
            var plot = Ladder().Plots[0];

            HomesteadLayout.Place("meadow_a", "daisies");
            HomesteadLayout.Place("meadow_b", "fence_low");

            Assert.AreEqual(TendedStage.Growing, GroveTending.Of(plot),
                            "two of three is .67, which is under the .75 the top-but-one stage asks for");

            HomesteadLayout.Place("meadow_c", "pebble");
            Assert.AreEqual(TendedStage.Bloomed, GroveTending.Of(plot));

            // And taking something away takes the island back down, because the stage is
            // derived rather than a floor. That is the right way round for an arrangement: a
            // player who empties an island has emptied it.
            HomesteadLayout.Clear("meadow_c");
            Assert.AreNotEqual(TendedStage.Bloomed, GroveTending.Of(plot));
        }

        [Test]
        public void AnIslandWithNowhereToPlaceIsNeverFinished()
        {
            // A hearth-only island, which the catalog should not contain but a drop could
            // produce. Zero over zero is 0 rather than 1: an island nobody can furnish must not
            // award the finished state for free.
            var plot = new HomesteadPlot("perch", "Homestead/plot_perch", .8f, .3f, ChapterId.None,
                                         new[] { Slot("perch_hearth", HomesteadSlotKind.Hearth) });

            Assert.AreEqual(0, plot.PlaceableCount);
            Assert.AreEqual(0f, HomesteadLayout.FillOf(plot));
            Assert.AreEqual(TendedStage.Bare, GroveTending.Of(plot));
        }
    }
}
