using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Homestead;
using GlimmerGrove.Persistence;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Buying grove decor by the copy — save schema v20.
    ///
    /// <para>
    /// <b>What is being proved, and why it is worth a file of its own.</b> This section is the
    /// first stored <em>count</em> in the save file, and v16 spent a paragraph arguing that one
    /// could never live here. It was right about the shape it described and wrong that no shape
    /// existed: a count of copies <em>remaining</em> is unmergeable for hearts' reason, and a
    /// count of copies <b>ever bought</b> only rises, so the join is a per-id maximum. Every
    /// test below is either that distinction or a consequence of it.
    /// </para>
    /// <para>
    /// It runs offline, which matters more than usual here. Both failures this guards against —
    /// a merge that loses a purchase, and a subtraction that reads as a player having fewer
    /// than they paid for — are invisible in the Editor, because the Editor never has two
    /// devices. <c>GroveStock</c> holds no Unity types and no clock precisely so they can be
    /// driven from plain integers, which is <c>TweenCycle</c>'s and <c>AccountGate</c>'s
    /// argument for the third time.
    /// </para>
    /// </summary>
    public sealed class GroveStockTests
    {
        [SetUp]
        public void Reset()
        {
            HomesteadLedger.ResetForTests();
            HomesteadLayout.ResetForTests();
        }

        [TearDown]
        public void Restore()
        {
            HomesteadLedger.ResetForTests();
            HomesteadLayout.ResetForTests();
        }

        // ------------------------------------------------------------- fixtures
        static HomesteadPiece Bundled(string id, int cost, int bundle)
            => new HomesteadPiece(id, "Homestead/" + id, false, HomesteadPieceKind.Decor,
                                  cost, LevelId.None, ChapterId.None, 1f, .5f,
                                  HomesteadSlotKind.Edge, 0, 0, bundle);

        static HomesteadPiece Single(string id, int cost) => Bundled(id, cost, 1);

        static HomesteadPiece Free(string id)
            => new HomesteadPiece(id, "Homestead/" + id, false, HomesteadPieceKind.Decor,
                                  0, LevelId.None, ChapterId.None, 1f, .5f);

        static HomesteadPiece Home(string id, int tier, int cost)
            => new HomesteadPiece(id, "Homestead/" + id, false, HomesteadPieceKind.Dwelling,
                                  cost, LevelId.None, ChapterId.None, 1f, .5f,
                                  HomesteadSlotKind.Hearth, tier);

        static HomesteadStockDto[] Rows(params (string id, int copies)[] rows)
        {
            var dto = new HomesteadStockDto[rows.Length];
            for (int i = 0; i < rows.Length; i++)
                dto[i] = new HomesteadStockDto { id = rows[i].id, copies = rows[i].copies };

            return dto;
        }

        static string Describe(HomesteadStockDto[] rows)
        {
            var parts = new List<string>();
            foreach (var row in rows ?? Array.Empty<HomesteadStockDto>())
                parts.Add(row.id + "=" + row.copies);

            return string.Join(",", parts);
        }

        static void Hold(params (string id, int copies)[] rows)
            => HomesteadLedger.LoadFrom(new SaveFileDto { homesteadStock = Rows(rows) });

        // ================================================= what is and is not stock
        [Test]
        public void OnlyPricedDecorIsBoughtByTheCopy()
        {
            // The split is the design. Stock is the shop's half of the feature and it should
            // run out; an entitlement is play's half and it should not. Getting this backwards
            // in either direction is a feature that either never runs out or confiscates a
            // friend somebody paid for.
            Assert.IsTrue(Bundled("fence", 900, 10).IsStocked, "priced decor");
            Assert.IsTrue(Single("well", 1200).IsStocked, "priced decor sold singly");

            Assert.IsFalse(Free("pebble").IsStocked, "free decor is an entitlement");
            Assert.IsFalse(Home("hall", 3, 6000).IsStocked, "a home rung is a rung");
        }

        [Test]
        public void AnEntitlementIsUnlimitedAndAlwaysHasBeen()
        {
            // The twelve starter pieces and the eight earned ones behave exactly as the whole
            // catalog did before v20. That is the compatibility claim the schema note makes,
            // and it is the one a player would notice being broken.
            Assert.AreEqual(HomesteadLedger.Unlimited, HomesteadLedger.Available(Free("pebble")));

            Assert.IsTrue(HomesteadLayout.Place("a", "pebble"));
            Assert.IsTrue(HomesteadLayout.Place("b", "pebble"));
            Assert.IsTrue(HomesteadLayout.Place("c", "pebble"));

            Assert.AreEqual(HomesteadLedger.Unlimited, HomesteadLedger.Available(Free("pebble")),
                            "placing an entitlement spends nothing");
        }

        [Test]
        public void AnUnheldPieceHasNothingAvailableWhateverKindItIs()
        {
            Assert.AreEqual(0, HomesteadLedger.Available(Bundled("fence", 900, 10)));
            Assert.AreEqual(0, HomesteadLedger.Available(Home("hall", 3, 6000)));
        }

        // ======================================================= the subtraction
        [Test]
        public void AvailableIsWhatWasBoughtMinusWhatIsStanding()
        {
            var fence = Bundled("fence", 900, 10);
            Hold(("fence", 10));

            Assert.AreEqual(10, HomesteadLedger.Copies(fence));
            Assert.AreEqual(10, HomesteadLedger.Available(fence));

            HomesteadLayout.Place("a", "fence");
            HomesteadLayout.Place("b", "fence");
            HomesteadLayout.Place("c", "fence");

            Assert.AreEqual(10, HomesteadLedger.Copies(fence), "buying is what is written down");
            Assert.AreEqual(7, HomesteadLedger.Available(fence));
            Assert.AreEqual(3, HomesteadLayout.CountOf("fence"));
        }

        [Test]
        public void ClearingASlotGivesTheCopyBack()
        {
            // Placing is not spending. It is the one property that makes the whole feature
            // usable — a player rearranging their grove must never be charged for it.
            var fence = Bundled("fence", 900, 10);
            Hold(("fence", 2));

            HomesteadLayout.Place("a", "fence");
            HomesteadLayout.Place("b", "fence");
            Assert.AreEqual(0, HomesteadLedger.Available(fence));

            HomesteadLayout.Clear("a");
            Assert.AreEqual(1, HomesteadLedger.Available(fence));
        }

        [Test]
        public void MovingAPieceCostsNothing()
        {
            var fence = Bundled("fence", 900, 10);
            Hold(("fence", 1));

            HomesteadLayout.Place("a", "fence");
            Assert.AreEqual(0, HomesteadLedger.Available(fence));

            HomesteadLayout.Clear("a");
            HomesteadLayout.Place("b", "fence");

            Assert.AreEqual(0, HomesteadLedger.Available(fence));
            Assert.AreEqual(1, HomesteadLayout.CountOf("fence"), "one fence, somewhere else");
        }

        [Test]
        public void MoreStandingThanWasBoughtReadsAsNoneLeftAndTakesNothingDown()
        {
            // Reachable, and the reason the clamp is not defensive tidying. Two devices editing
            // offline can each place the last copy on a different tile; the placement map merges
            // by recency per slot (invariant 11c), so both survive and the grove holds one more
            // fence than it bought. Answering "none left" costs the player nothing; taking a
            // placement down to balance an identity would be the loss invariant 11 refuses.
            var fence = Bundled("fence", 900, 10);
            Hold(("fence", 1));

            HomesteadLayout.Place("a", "fence");
            HomesteadLayout.Place("b", "fence");
            HomesteadLayout.Place("c", "fence");

            Assert.AreEqual(0, HomesteadLedger.Available(fence), "never negative");
            Assert.AreEqual(3, HomesteadLayout.CountOf("fence"), "and nothing was removed");
            Assert.IsFalse(HomesteadLedger.CanPlace(fence));
        }

        // ============================================================ the merge
        [Test]
        public void TwoDevicesJoinToWhicheverBoughtMore()
        {
            var mine = Rows(("fence", 10), ("well", 1));
            var other = Rows(("fence", 30), ("oak", 2));

            Assert.AreEqual("fence=30,oak=2,well=1", Describe(GroveStock.Join(mine, other)));
            Assert.AreEqual("fence=30,oak=2,well=1", Describe(GroveStock.Join(other, mine)),
                            "order-independent, or it is not a join");
        }

        [Test]
        public void TheJoinIsIdempotent()
        {
            var mine = Rows(("fence", 10));

            var once = GroveStock.Join(mine, null);
            var twice = GroveStock.Join(once, once);

            Assert.AreEqual(Describe(once), Describe(twice));
        }

        [Test]
        public void ADuplicatedRowIsReadAsTheLargerRatherThanTheLastOne()
        {
            // Invariant 11a says a duplicated row is a malformed file. It is read the way two
            // devices holding it would have been merged rather than by whichever row happened
            // to come last, so a file that should have been impossible cannot silently cost
            // somebody a purchase.
            var stock = new GroveStock();
            stock.LoadFrom(Rows(("fence", 3), ("fence", 12), ("fence", 5)));

            Assert.AreEqual(12, stock.Of("fence"));
        }

        [Test]
        public void AMalformedRowIsDroppedRatherThanRepaired()
        {
            var stock = new GroveStock();
            stock.LoadFrom(Rows((null, 4), ("", 4), ("fence", 0), ("well", -3), ("oak", 2)));

            Assert.AreEqual("oak=2", Describe(stock.Write()),
                            "a row the writer would not emit must not come back out of the reader");
        }

        [Test]
        public void TheWriterSortsSoAnUnchangedSaveNeverLooksChanged()
        {
            // SaveDelta walks these in order and SaveChecksum hashes the serialised file, so
            // dictionary order would push a write for nothing on every launch, forever.
            var stock = new GroveStock();
            stock.Add("well", 1);
            stock.Add("fence", 1);
            stock.Add("oak", 1);

            Assert.AreEqual("fence=1,oak=1,well=1", Describe(stock.Write()));
        }

        // =========================================================== the ceilings
        [Test]
        public void CopiesClampAtTheStructuralCeilingRatherThanOverflowing()
        {
            var stock = new GroveStock();

            Assert.AreEqual(GroveStock.MaxCopies, stock.Add("fence", int.MaxValue));
            Assert.AreEqual(GroveStock.MaxCopies, stock.Of("fence"));

            Assert.AreEqual(0, stock.Add("fence", 500), "nothing more is added at the ceiling");
            Assert.AreEqual(GroveStock.MaxCopies, stock.Of("fence"));
        }

        [Test]
        public void TheNumberOfDistinctIdsIsBoundedSoTheWireStaysWithinTheRules()
        {
            // It matches the homesteadStock size guard in firestore.rules, and it has to: a
            // save the client is willing to write and the rules refuse loses the whole document
            // write rather than the extra rows (invariant 12a).
            var stock = new GroveStock();
            for (int i = 0; i < GroveStock.MaxIds + 50; i++) stock.Add("piece_" + i, 1);

            Assert.AreEqual(GroveStock.MaxIds, stock.Count);
            Assert.AreEqual(GroveStock.MaxIds, stock.Write().Length);
        }

        [Test]
        public void AnIdAlreadyHeldStillGrowsWhenTheIdListIsFull()
        {
            // The bound is on how many *ids* are recorded, never on buying more of one already
            // there. Getting that backwards would refuse a purchase the player had paid for.
            var stock = new GroveStock();
            for (int i = 0; i < GroveStock.MaxIds; i++) stock.Add("piece_" + i, 1);

            Assert.AreEqual(4, stock.Add("piece_0", 4));
            Assert.AreEqual(5, stock.Of("piece_0"));
        }

        // ============================================================= the offer
        //
        // What a player is *charged* is decided here, and it is the half of this feature that
        // has to be exactly right rather than merely reasonable. A successful purchase needs a
        // wallet — a process-wide static backed by a save file — so it is Editor-only, exactly
        // as CompanionLedger's is. The arithmetic in front of it is not, and it is what a
        // stepper draws and what the buy button takes.

        [Test]
        public void AnOrderCostsItsQuantityTimesTheBundlePrice()
        {
            var fence = Bundled("fence", 900, 10);

            Assert.AreEqual(900L, HomesteadLedger.OfferFor(fence, 1).Cost);
            Assert.AreEqual(2700L, HomesteadLedger.OfferFor(fence, 3).Cost);
            Assert.AreEqual(9000L, HomesteadLedger.OfferFor(fence, 10).Cost);
        }

        [Test]
        public void AnOrderDeliversItsQuantityTimesTheBundle()
        {
            // The number the panel has to print. A stepper reading "3" over a ten-piece bundle
            // is a player agreeing to thirty fences without being told so, which is every
            // complaint a shop ever gets about quantity.
            var fence = Bundled("fence", 900, 10);

            Assert.AreEqual(30, HomesteadLedger.OfferFor(fence, 3).Copies);
            Assert.AreEqual(3, HomesteadLedger.OfferFor(Single("well", 1200), 3).Copies);
        }

        [Test]
        public void AnOrderIsClampedToWhatMayBeBoughtAtOnce()
        {
            // An economy bound rather than a structural one, and it lives on the ledger for
            // that reason. A stepper cannot spend a whole balance in one held-down press.
            var fence = Bundled("fence", 900, 10);
            var offer = HomesteadLedger.OfferFor(fence, 5_000);

            Assert.AreEqual(HomesteadLedger.MaxPerPurchase, offer.Quantity);
            Assert.AreEqual(900L * HomesteadLedger.MaxPerPurchase, offer.Cost);
        }

        [Test]
        public void AnOrderIsNeverForLessThanOne()
        {
            var fence = Bundled("fence", 900, 10);

            Assert.AreEqual(1, HomesteadLedger.OfferFor(fence, 0).Quantity);
            Assert.AreEqual(1, HomesteadLedger.OfferFor(fence, -8).Quantity);
        }

        [Test]
        public void AStockedPieceIsNeverAlreadyHeldJustBecauseOneWasBought()
        {
            // The state means "there is nothing to buy", which stopped being true of the shop's
            // half of the catalog in v20 — a player with three fences may want three more. A
            // cell that read AlreadyHeld here would be the shop refusing to sell something it
            // is displaying a price for.
            var fence = Bundled("fence", 900, 10);
            HomesteadLedger.GrantForTests("fence", 10);

            Assert.AreNotEqual(HomesteadPurchaseState.AlreadyHeld,
                               HomesteadLedger.OfferFor(fence, 1).State);
            Assert.IsTrue(HomesteadLedger.IsHeld(fence), "held, and still for sale");
        }

        [Test]
        public void APlayerAtTheStructuralCeilingIsToldTheyAreFullRatherThanPoor()
        {
            // Room is checked before the price, which is the ordering CompanionLedger uses for
            // the keeper gate and for its reason: a refusal a player cannot act on must not be
            // dressed as one they can. Telling somebody to go and watch a video for coins they
            // could not spend is the mistake HintPrompt exists to prevent one screen over.
            var fence = Bundled("fence", 900, 10);
            HomesteadLedger.GrantForTests("fence", GroveStock.MaxCopies);

            var offer = HomesteadLedger.OfferFor(fence, 1);

            Assert.AreEqual(HomesteadPurchaseState.AlreadyHeld, offer.State);
            Assert.AreEqual(0, offer.Copies, "nothing would be delivered");
        }

        [Test]
        public void AnOrderNearTheCeilingIsCutToWhatWillFit()
        {
            // The clamp has to happen before the credits move, or the player is charged for
            // copies GroveStock.Add would silently drop.
            var fence = Bundled("fence", 900, 10);
            HomesteadLedger.GrantForTests("fence", GroveStock.MaxCopies - 25);

            var offer = HomesteadLedger.OfferFor(fence, 20);

            Assert.AreEqual(2, offer.Quantity, "two whole bundles fit under the ceiling");
            Assert.AreEqual(20, offer.Copies);
            Assert.AreEqual(1800L, offer.Cost);
        }

        [Test]
        public void AnUnpricedPieceIsNotForSaleAtAnyQuantity()
        {
            var offer = HomesteadLedger.OfferFor(Free("pebble"), 5);

            Assert.AreEqual(HomesteadPurchaseState.AlreadyHeld, offer.State,
                            "a starter piece is held from the first launch");
            Assert.AreEqual(1, offer.Quantity, "an entitlement is never an order");
        }

        // ======================================================== the v19 migration
        [Test]
        public void AV19SaveKeepsWhatItBuiltAndIsWorthWhatItPaid()
        {
            var file = new SaveFileDto
            {
                homesteadOwned = new[] { "fence", "oak" },
                homesteadPlaced = new[]
                {
                    new HomesteadPlacementDto { slot = "a", piece = "fence", setUnix = 5 },
                    new HomesteadPlacementDto { slot = "b", piece = "fence", setUnix = 5 },
                    new HomesteadPlacementDto { slot = "c", piece = "fence", setUnix = 5 },
                },
            };

            Assert.AreEqual("fence=3,oak=1", Describe(GroveStock.In(file)));
        }

        [Test]
        public void TheMirrorNamesEveryIdWithACopyAndNothingElse()
        {
            Assert.AreEqual(new[] { "fence", "oak" },
                            GroveStock.Mirror(Rows(("oak", 1), ("fence", 12))));

            CollectionAssert.IsEmpty(GroveStock.Mirror(Rows(("fence", 0))),
                                     "a row with no copies is not an entitlement");
            CollectionAssert.IsEmpty(GroveStock.Mirror(null));
        }

        [Test]
        public void AMirrorRoundTripsBackThroughTheMigrationToTheSameIds()
        {
            // The mirror is what a rolled-back client and a not-yet-redeployed server read. It
            // has to name the same pieces, and it deliberately cannot carry the counts — which
            // is why GroveStock.In prefers the stock section whenever it holds anything.
            var rows = Rows(("fence", 30), ("oak", 2));

            var rolledBack = new SaveFileDto { homesteadOwned = GroveStock.Mirror(rows) };

            Assert.AreEqual("fence=1,oak=1", Describe(GroveStock.In(rolledBack)));
            Assert.AreEqual("fence=30,oak=2",
                            Describe(GroveStock.In(new SaveFileDto
                            {
                                homesteadStock = rows,
                                homesteadOwned = GroveStock.Mirror(rows),
                            })),
                            "a file carrying both is the v20 file it is");
        }
    }
}
