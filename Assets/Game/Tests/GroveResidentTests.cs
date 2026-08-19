using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Homestead;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Residents are companions: one roster, one entitlement set, two screens.
    ///
    /// <para>
    /// The grove used to author five creatures of its own, earned by clearing five named
    /// glades and never for sale. That put two rosters of creatures in the game with two
    /// unlock rules and two prices, and a player who bought Coral on the profile could not
    /// stand her in their village. Projecting the roster into the grove removes the second
    /// list entirely — so what this file pins is that <b>nothing about a companion is written
    /// down twice</b>: one price, one gate, one purchased set, and both screens reading the
    /// same answer.
    /// </para>
    /// <para>
    /// The keeper-level half of the rule is deliberately tested through
    /// <c>CompanionLedger</c> rather than through a driven <c>PlayerProgression</c>: the level
    /// is read from the live progression singleton, which would mean building a save file to
    /// assert something <c>CompanionPurchaseTests</c> already proves. What matters here is
    /// that the grove <em>delegates</em> rather than deriving a second answer.
    /// </para>
    /// </summary>
    public sealed class GroveResidentTests
    {
        AvatarDefinition[] _rosterBefore;
        bool _wasFromContent;

        [SetUp]
        public void Snapshot()
        {
            _rosterBefore = new AvatarDefinition[AvatarCatalog.All.Count];
            for (int i = 0; i < _rosterBefore.Length; i++) _rosterBefore[i] = AvatarCatalog.All[i];
            _wasFromContent = AvatarCatalog.IsFromContent;

            CompanionLedger.ResetForTests();
            HomesteadLedger.ResetForTests();
            HomesteadLayout.ResetForTests();
        }

        [TearDown]
        public void Restore()
        {
            AvatarCatalog.Publish(_wasFromContent ? _rosterBefore : null);
            CompanionLedger.ResetForTests();
            HomesteadLedger.ResetForTests();
            HomesteadLayout.ResetForTests();
        }

        static AvatarDefinition Free(string id) => new AvatarDefinition(id, id, null, 0);

        static AvatarDefinition Gated(string id, int level, int cost, string animated = null)
            => new AvatarDefinition(id, id, animated, level, cost);

        static HomesteadPiece Decor(string id, int cost)
            => new HomesteadPiece(id, "Homestead/" + id, false, HomesteadPieceKind.Decor,
                                  cost, LevelId.None, ChapterId.None, 1f, .5f);

        static HomesteadCatalog Catalog(params HomesteadPiece[] authored)
            => new HomesteadCatalog(GroveFloor.Empty, authored, AvatarCatalog.All);

        // ---------------------------------------------------------- projection
        [Test]
        public void EveryCompanionBecomesAResidentAndCarriesItsOwnPriceAndGate()
        {
            AvatarCatalog.Publish(new[] { Free("monarch"), Gated("coral", 40, 14500) });

            var catalog = Catalog(Decor("bench", 400));
            var coral = catalog.Find(GroveResidents.PieceId("coral"));

            Assert.IsTrue(coral.IsValid, "the roster reaches the grove without being authored there");
            Assert.IsTrue(coral.IsResident);
            Assert.AreEqual(14500, coral.Cost, "one price, quoted in both places");
            Assert.AreEqual(40, coral.RequiresKeeperLevel, "and one gate");
            Assert.IsTrue(coral.IsForSale, "a resident is for sale now; that is the whole change");
        }

        [Test]
        public void AResidentIsNamedAfterTheCompanionRatherThanUnderASecondKey()
        {
            AvatarCatalog.Publish(new[] { Free("monarch") });

            var monarch = Catalog().Find(GroveResidents.PieceId("monarch"));

            Assert.AreEqual("ui.avatar.monarch", monarch.NameKey,
                            "a translated name that exists twice is one that will one day differ");
            Assert.AreEqual(HomesteadPiece.DefaultNameKey("bench"), Decor("bench", 100).NameKey,
                            "decor keeps its own prefix");
        }

        [Test]
        public void ACompanionSharingADecorNameIsStillItsOwnThing()
        {
            // 'pebble' is a rock in the grove catalog and a companion in the manifest. The two
            // id spaces were minted independently and can never be renamed, so a resident's
            // piece id is the companion's id prefixed — which makes the collision
            // unrepresentable rather than merely detected.
            AvatarCatalog.Publish(new[] { Free("pebble") });

            var catalog = Catalog(Decor("pebble", 400));

            Assert.IsFalse(catalog.Find("pebble").IsResident, "the rock is still the rock");
            Assert.AreEqual(400, catalog.Find("pebble").Cost);

            var friend = catalog.Find(GroveResidents.PieceId("pebble"));
            Assert.IsTrue(friend.IsResident, "and the companion is still on the shelf");
            Assert.AreEqual("ui.avatar.pebble", friend.NameKey, "named after the companion");
        }

        [Test]
        public void SwappingTheRosterRebuildsTheResidentsAndLeavesTheAuthoredHalfAlone()
        {
            AvatarCatalog.Publish(new[] { Free("monarch") });
            var catalog = Catalog(Decor("bench", 400));

            var swapped = catalog.WithResidents(new[] { Free("monarch"), Gated("coral", 40, 14500) });

            Assert.IsTrue(swapped.Find(GroveResidents.PieceId("coral")).IsValid, "the new roster is in");
            Assert.IsTrue(swapped.Find("bench").IsValid, "and the file's own pieces survived");
            Assert.AreEqual(1, swapped.Authored.Count, "the authored half is not grown by a swap");
        }

        // ------------------------------------------------------------ the rule
        [Test]
        public void BuyingOnTheProfileHoldsTheResidentInTheGrove()
        {
            AvatarCatalog.Publish(new[] { Free("monarch"), Gated("coral", 40, 14500) });
            var catalog = Catalog();
            var coral = catalog.Find(GroveResidents.PieceId("coral"));

            Assert.IsFalse(HomesteadLedger.IsHeld(coral), "unbought and far above the gate");

            // Exactly what a purchase leaves behind, and what a sync brings from the other
            // device: one id in companionsOwned. Nothing is written to homesteadOwned.
            CompanionLedger.LoadFrom(new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                companionsOwned = new[] { "coral" },
            });

            Assert.IsTrue(HomesteadLedger.IsHeld(coral), "and now she can stand in the village");
            Assert.IsTrue(HomesteadLedger.WasBought(coral));
        }

        [Test]
        public void AResidentIsNeverRecordedInTheGrovesOwnEntitlementSet()
        {
            // Two records of one purchase is two things a merge can disagree about, and the
            // grove's set is the forgeable half of the pair. The companion ledger is the only
            // place a companion is written down.
            AvatarCatalog.Publish(new[] { Free("monarch"), Gated("coral", 40, 14500) });

            CompanionLedger.LoadFrom(new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                companionsOwned = new[] { "coral" },
            });

            var file = new SaveFileDto();
            HomesteadLedger.WriteInto(file);

            CollectionAssert.DoesNotContain(file.homesteadOwned ?? Array.Empty<string>(), "coral");
        }

        [Test]
        public void AResidentsOfferIsTheCompanionsOfferUnderAnotherName()
        {
            AvatarCatalog.Publish(new[] { Free("monarch"), Gated("coral", 40, 14500) });
            var coral = Catalog().Find(GroveResidents.PieceId("coral"));

            var offer = HomesteadLedger.OfferFor(coral);

            Assert.AreEqual(14500L, offer.Cost);
            Assert.AreEqual(HomesteadPurchaseState.TooExpensive, offer.State,
                            "a fresh account cannot pay 14,500, and the refusal names why");
        }

        [Test]
        public void AStarterCompanionIsAStarterResident()
        {
            AvatarCatalog.Publish(new[] { Free("monarch"), Gated("coral", 40, 14500) });
            var monarch = Catalog().Find(GroveResidents.PieceId("monarch"));

            Assert.IsTrue(HomesteadLedger.IsHeld(monarch),
                          "a new player has one friend, and can stand them in the grove at once");
            Assert.IsFalse(monarch.HasRequirement);
            Assert.IsFalse(monarch.IsForSale);
        }

        // --------------------------------------------------------------- shelves
        [Test]
        public void EveryResidentIsOnTheResidentsShelfAndNoDecorIs()
        {
            AvatarCatalog.Publish(new[] { Free("monarch"), Gated("coral", 40, 14500) });

            var catalog = Catalog(Decor("bench", 400));

            Assert.AreEqual(GroveShelf.Residents, GroveShelves.Of(catalog.Find(GroveResidents.PieceId("coral"))));
            Assert.AreEqual(GroveShelf.Ground, GroveShelves.Of(catalog.Find("bench")));
        }

        [Test]
        public void AShelfKeyIsStableBecauseItReachesAnAssetAddress()
        {
            // These become atlas addresses, and an address is a permanent name. Renaming an
            // enum member must not silently orphan a bundle somebody already shipped.
            Assert.AreEqual("residents", GroveShelves.Key(GroveShelf.Residents));
            Assert.AreEqual("ground", GroveShelves.Key(GroveShelf.Ground));
            Assert.AreEqual("home", GroveShelves.Key(GroveShelf.Home));

            foreach (var shelf in GroveShelves.All)
                Assert.AreEqual(shelf, GroveShelves.FromKey(GroveShelves.Key(shelf)),
                                "a key has to round-trip, or the generator and the game disagree");
        }

        [Test]
        public void AResidentFitsEverySlotButTheHearth()
        {
            // The one place a slot kind and a shop shelf deliberately disagree, and the reason
            // both concepts exist. Telling somebody their own friend may not stand somewhere
            // turns a toy into a form.
            AvatarCatalog.Publish(new[] { Free("monarch") });
            var monarch = Catalog().Find(GroveResidents.PieceId("monarch"));

            Assert.IsTrue(monarch.CanBePlaced);
            Assert.IsTrue(Decor("bench", 400).CanBePlaced, "and so can a bench");
        }

        // ------------------------------------------------------------ retirement
        [Test]
        public void EveryRetiredResidentIsRewrittenToTheCompanionDrawingTheSameCreature()
        {
            // The five the grove used to author. Each drew one of the board's critter
            // flipbooks, and exactly one companion draws the same one — so the creature
            // standing on the island after the rewrite is the creature that was standing there
            // before it.
            Assert.AreEqual("friend_puff", GroveResidents.Rename("sunmote"));
            Assert.AreEqual("friend_timber", GroveResidents.Rename("ripple"));
            Assert.AreEqual("friend_sprocket", GroveResidents.Rename("prism"));
            Assert.AreEqual("friend_thistle", GroveResidents.Rename("burr"));
            Assert.AreEqual("friend_monarch", GroveResidents.Rename("dusk"));
        }

        [Test]
        public void AnIdNothingRetiredIsHandedStraightBack()
        {
            // Including ids this build has never heard of: a save written by a newer build must
            // survive a trip through an older one untouched.
            Assert.AreEqual("bench", GroveResidents.Rename("bench"));
            Assert.AreEqual("something_from_2027", GroveResidents.Rename("something_from_2027"));
            Assert.AreEqual(string.Empty, GroveResidents.Rename(string.Empty));
        }

        [Test]
        public void TheRewriteIsIdempotentSoAFileCanPassThroughItTwice()
        {
            // It runs at every load, for ever — a save from a device left in a drawer arrives
            // whenever it arrives. A rename that renamed its own output would walk the roster.
            foreach (string retired in GroveResidents.RetiredIds)
            {
                string once = GroveResidents.Rename(retired);
                Assert.AreEqual(once, GroveResidents.Rename(once));
            }
        }

        [Test]
        public void ARetiredResidentStandingInASlotBecomesItsCompanionOnLoad()
        {
            HomesteadLayout.LoadFrom(new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                homesteadPlaced = new[]
                {
                    new HomesteadPlacementDto { slot = "meadow_a", piece = "sunmote", setUnix = 1000 },
                    new HomesteadPlacementDto { slot = "meadow_b", piece = "bench", setUnix = 1000 },
                },
            });

            Assert.AreEqual("friend_puff", HomesteadLayout.At("meadow_a"),
                            "the arrangement survives; an unresolvable id would leave a hole that " +
                            "still counted as occupied");
            Assert.AreEqual("bench", HomesteadLayout.At("meadow_b"));
        }
    }
}
