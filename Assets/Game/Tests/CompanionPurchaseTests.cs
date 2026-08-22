using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Buying a companion: the composite unlock rule, the union merge, and the two things
    /// that must never be representable — paying twice, and losing a purchase in a sync.
    ///
    /// <para>
    /// This is the first stored entitlement in the save file. Everything else here is either
    /// derived (XP, credits, the heart count, an event's payout) or a monotonic floor, and the
    /// arguments for those shapes are made at length elsewhere. A purchase can be neither: it
    /// is not a function of anything observable, so it is stored, and the whole safety of that
    /// rests on the set only ever growing. Most of this file pins that.
    /// </para>
    /// </summary>
    public sealed class CompanionPurchaseTests
    {
        AvatarDefinition[] _rosterBefore;
        bool _wasFromContent;

        /// <summary>
        /// The roster is a process-wide static, so a test that publishes one would leave it
        /// published for whatever runs next. Snapshot and restore makes each independent —
        /// the same guard <c>ProfileTests</c> uses, and for the same reason.
        /// </summary>
        [SetUp]
        public void Snapshot()
        {
            _rosterBefore = new AvatarDefinition[AvatarCatalog.All.Count];
            for (int i = 0; i < _rosterBefore.Length; i++) _rosterBefore[i] = AvatarCatalog.All[i];
            _wasFromContent = AvatarCatalog.IsFromContent;

            CompanionLedger.ResetForTests();
        }

        [TearDown]
        public void Restore()
        {
            AvatarCatalog.Publish(_wasFromContent ? _rosterBefore : null);
            CompanionLedger.ResetForTests();
        }

        static AvatarDefinition Free(string id) => new AvatarDefinition(id, id, null, 0);

        static AvatarDefinition Gated(string id, int level, int cost)
            => new AvatarDefinition(id, id, null, level, cost);

        static SaveFileDto File(params string[] owned)
            => new SaveFileDto { schemaVersion = SaveSchema.Version, companionsOwned = owned };

        // ------------------------------------------------------- the unlock rule
        [Test]
        public void TheStarterIsHeldAtLevelOneAndNothingElseIs()
        {
            // The whole point of the gate shift: a brand-new player wears exactly one
            // companion, so every other portrait on the grid is something to want.
            AvatarCatalog.Publish(new[] { Free("monarch"), Gated("cinder", 2, 800), Gated("thorn", 66, 30000) });

            Assert.IsTrue(CompanionLedger.IsHeld(AvatarCatalog.Find("monarch"), 1));
            Assert.IsFalse(CompanionLedger.IsHeld(AvatarCatalog.Find("cinder"), 1));
            Assert.IsFalse(CompanionLedger.IsHeld(AvatarCatalog.Find("thorn"), 1));

            Assert.AreEqual(1, CompanionLedger.HeldCount(1));
        }

        [Test]
        public void ReachingTheGateIsPermissionToPayAndNotAGrant()
        {
            // The rule is keeper level AND purchase. Both halves are pinned here because the
            // two obvious wrong versions each pass one of these lines: a build that kept the
            // old "or" grants at line two, and a build that also re-checked the gate on a
            // purchase confiscates at line four.
            AvatarCatalog.Publish(new[] { Free("monarch"), Gated("coral", 40, 14500) });
            var coral = AvatarCatalog.Find("coral");

            Assert.IsFalse(CompanionLedger.IsHeld(coral, 39), "one rank short and unbought");
            Assert.IsFalse(CompanionLedger.IsHeld(coral, 40),
                           "standing at the gate is permission to pay, not the companion");

            CompanionLedger.LoadFrom(File("coral"));
            Assert.IsTrue(CompanionLedger.IsHeld(coral, 40), "reached and paid for");
            Assert.IsTrue(CompanionLedger.IsHeld(coral, 1),
                          "and a purchase is permanent, so a gate retune never takes it back");
        }

        [Test]
        public void AnUnpricedCompanionIsStillHandedOverAtItsGate()
        {
            // The one thing the gate still grants on its own, and what keeps the starter
            // working: a companion the roster puts no price on has nothing to pay.
            AvatarCatalog.Publish(new[] { Free("monarch"), Gated("wren", 6, 0) });
            var wren = AvatarCatalog.Find("wren");

            Assert.IsFalse(CompanionLedger.IsHeld(wren, 5));
            Assert.IsTrue(CompanionLedger.IsHeld(wren, 6), "no price, so the gate is the whole rule");
        }

        [Test]
        public void ReachingTheGateDoesNotRecordAPurchase()
        {
            // The level half stays derived. Writing it down as well would create a second
            // answer that a retune could put out of step with the first — and would mean the
            // save grew every time somebody levelled up.
            AvatarCatalog.Publish(new[] { Free("monarch"), Gated("plum", 11, 0) });
            var plum = AvatarCatalog.Find("plum");

            Assert.IsTrue(CompanionLedger.IsHeld(plum, 11));
            Assert.IsFalse(CompanionLedger.WasBought(plum));

            var dto = new SaveFileDto();
            CompanionLedger.WriteInto(dto);
            Assert.IsEmpty(dto.companionsOwned, "a level unlock is not a purchase");
        }

        [Test]
        public void APurchaseSurvivesARetuneThatMovesTheGateOutOfReach()
        {
            // The mirror of ResolveKeepsACompanionARetuneWouldHaveTakenAway, one level up:
            // it is not enough for the worn id to survive, the entitlement has to.
            AvatarCatalog.Publish(new[] { Free("monarch"), Gated("olive", 27, 8000) });
            CompanionLedger.LoadFrom(File("olive"));

            AvatarCatalog.Publish(new[] { Free("monarch"), Gated("olive", 300, 8000) });

            Assert.IsTrue(CompanionLedger.IsHeld(AvatarCatalog.Find("olive"), 1),
                          "a companion somebody paid for cannot be taken back by a retune");
        }

        // ------------------------------------------------------------- the offer
        [Test]
        public void EveryRefusalIsADistinctState()
        {
            // Each renders a different sentence, and two of them resolve by different means:
            // TooExpensive resolves by playing or watching a video, NotForSale never resolves
            // at all. Collapsing them into one "unavailable" is how a player learns to stop
            // tapping.
            AvatarCatalog.Publish(new[]
            {
                Free("monarch"),
                Gated("earned_only", 20, 0),
                Gated("priced", 20, 500_000),
            });

            Assert.AreEqual(CompanionPurchaseState.AlreadyHeld,
                            CompanionLedger.OfferFor(AvatarCatalog.Find("monarch"), 1).State);

            Assert.AreEqual(CompanionPurchaseState.NotForSale,
                            CompanionLedger.OfferFor(AvatarCatalog.Find("earned_only"), 1).State,
                            "zero cost means earned by playing, never free");

            // The gate is tested before the price, so a player who is both short and too
            // junior is told about the wall credits cannot climb. Leading with the price would
            // sell them a rewarded video for a companion the video could not buy.
            var junior = CompanionLedger.OfferFor(AvatarCatalog.Find("priced"), 1);
            Assert.AreEqual(CompanionPurchaseState.LevelLocked, junior.State);
            Assert.AreEqual(20, junior.RequiredLevel);

            var dear = CompanionLedger.OfferFor(AvatarCatalog.Find("priced"), 20);
            Assert.AreEqual(CompanionPurchaseState.TooExpensive, dear.State);
            Assert.AreEqual(500_000L - dear.Balance, dear.Shortfall);

            Assert.AreEqual(CompanionPurchaseState.NotForSale,
                            CompanionLedger.OfferFor(default, 1).State);
        }

        [Test]
        public void AnUnaffordableCompanionIsNeverHandedOver()
        {
            AvatarCatalog.Publish(new[] { Free("monarch"), Gated("thorn", 66, int.MaxValue) });
            var thorn = AvatarCatalog.Find("thorn");

            Assert.IsFalse(CompanionLedger.TryBuy(thorn, 1));
            Assert.IsFalse(CompanionLedger.WasBought(thorn));
            Assert.IsFalse(CompanionLedger.IsHeld(thorn, 1));
        }

        [Test]
        public void ACompanionAlreadyHeldCannotBeBoughtAgain()
        {
            // The guard against a double tap, and against a stale button on a grid that has
            // not repainted. Re-entrancy is handled by the held check rather than a flag, so
            // the second pass must refuse rather than charge.
            AvatarCatalog.Publish(new[] { Free("monarch"), Gated("cinder", 2, 800) });

            CompanionLedger.LoadFrom(File("cinder"));

            Assert.IsFalse(CompanionLedger.TryBuy(AvatarCatalog.Find("cinder"), 1),
                           "already bought");
            Assert.IsFalse(CompanionLedger.TryBuy(AvatarCatalog.Find("monarch"), 1),
                           "already held by level");
        }

        [Test]
        public void ACompanionWithNoPriceCannotBeBoughtAtAnyBalance()
        {
            AvatarCatalog.Publish(new[] { Free("monarch"), Gated("earned_only", 20, 0) });

            Assert.IsFalse(CompanionLedger.TryBuy(AvatarCatalog.Find("earned_only"), 1));
            Assert.IsFalse(CompanionLedger.TryBuy(AvatarCatalog.Find("earned_only"), 19));
        }

        // -------------------------------------------------------- the goal ahead
        [Test]
        public void APurchasedCompanionIsNotTheNextGoal()
        {
            // UnlockGoal points the hub's progress bar at whatever comes back. Aiming it at
            // a companion the player is already wearing tells somebody who just spent 9,000
            // credits that they have four ranks to climb for the friend in front of them.
            AvatarCatalog.Publish(new[]
            {
                Free("monarch"), Gated("near", 5, 1400), Gated("far", 20, 5000),
            });

            Assert.AreEqual("near", CompanionLedger.NextUnheld(1).Id);

            CompanionLedger.LoadFrom(File("near"));
            Assert.AreEqual("far", CompanionLedger.NextUnheld(1).Id, "the bought one is skipped");

            CompanionLedger.LoadFrom(File("near", "far"));
            Assert.IsFalse(CompanionLedger.NextUnheld(1).IsValid, "nothing left to chase");
        }

        [Test]
        public void TheCheapestUnheldIsWhatAShopWouldLeadWith()
        {
            AvatarCatalog.Publish(new[]
            {
                Free("monarch"), Gated("dear", 40, 14500), Gated("cheap", 2, 800),
            });

            Assert.AreEqual("cheap", CompanionLedger.CheapestForSale(1).Id);

            CompanionLedger.LoadFrom(File("cheap"));
            Assert.AreEqual("dear", CompanionLedger.CheapestForSale(1).Id);
        }

        // -------------------------------------------------------------- the join
        [Test]
        public void TwoDevicesKeepEveryPurchaseEitherOfThemMade()
        {
            var joined = CompanionLedger.Join(new[] { "coral", "plum" }, new[] { "plum", "wisp" });

            CollectionAssert.AreEqual(new[] { "coral", "plum", "wisp" }, joined);
        }

        [Test]
        public void TheJoinIsIdempotentCommutativeAndAssociative()
        {
            // The three properties that make a merge safe to run in any order, however many
            // devices sync however often. A union has them for free, which is exactly why
            // this shape was chosen over a count.
            var a = new[] { "coral" };
            var b = new[] { "plum", "wisp" };
            var c = new[] { "coral", "thorn" };

            CollectionAssert.AreEqual(CompanionLedger.Join(a, a), a, "idempotent");
            CollectionAssert.AreEqual(CompanionLedger.Join(a, b), CompanionLedger.Join(b, a),
                                      "commutative");
            CollectionAssert.AreEqual(CompanionLedger.Join(CompanionLedger.Join(a, b), c),
                                      CompanionLedger.Join(a, CompanionLedger.Join(b, c)),
                                      "associative");
        }

        [Test]
        public void AnEmptySideNeverErasesTheOther()
        {
            // The direction that matters. A second device that has bought nothing must not
            // take a purchase off the first — which is the failure the stored hearts count
            // shipped, in the one other place a merge could lose something.
            CollectionAssert.AreEqual(new[] { "coral" }, CompanionLedger.Join(new[] { "coral" }, null));
            CollectionAssert.AreEqual(new[] { "coral" }, CompanionLedger.Join(null, new[] { "coral" }));
            CollectionAssert.AreEqual(new[] { "coral" },
                                      CompanionLedger.Join(new[] { "coral" }, new string[0]));
            Assert.IsEmpty(CompanionLedger.Join(null, null));
        }

        [Test]
        public void TheJoinSortsAndDeduplicatesEvenAgainstNothing()
        {
            // Not tidiness: SaveDelta walks these in order, so an unsorted array handed
            // straight back would read as changed on every launch and push a write forever.
            var joined = CompanionLedger.Join(new[] { "wisp", "coral", "wisp" }, null);

            CollectionAssert.AreEqual(new[] { "coral", "wisp" }, joined);
        }

        [Test]
        public void AnUnknownCompanionIsCarriedThroughRatherThanConfiscated()
        {
            // Bought on a newer build, then a trip through an older one. The id costs one
            // short string; dropping it costs somebody a companion they paid for.
            AvatarCatalog.Publish(new[] { Free("monarch") });
            CompanionLedger.LoadFrom(File("companion_from_the_future"));

            var dto = new SaveFileDto();
            CompanionLedger.WriteInto(dto);

            CollectionAssert.Contains(dto.companionsOwned, "companion_from_the_future");
        }

        [Test]
        public void EmptyAndAbsentSayTheSameTrueThing()
        {
            // What makes this mergeable with no sentinel, unlike the heart ledger. JsonUtility
            // writes a null array into a field an older file never had, and "bought nothing"
            // is exactly what a pre-v12 file means.
            //
            // Asserted on the purchased set rather than on HeldCount, deliberately: the
            // starter is held at every level and has been bought at none, so a count would be
            // measuring the level rule here and not this one.
            foreach (var dto in new[] { new SaveFileDto(), File(), File(new string[0]), null })
            {
                CompanionLedger.LoadFrom(dto);

                var written = new SaveFileDto();
                CompanionLedger.WriteInto(written);
                Assert.IsEmpty(written.companionsOwned, "an absent set means bought nothing");
            }
        }

        // ------------------------------------------------------- the file bridge
        [Test]
        public void APurchaseSurvivesAWriteAndReadUnchanged()
        {
            CompanionLedger.LoadFrom(File("coral", "plum"));

            var dto = new SaveFileDto();
            CompanionLedger.WriteInto(dto);
            CompanionLedger.LoadFrom(dto);

            var again = new SaveFileDto();
            CompanionLedger.WriteInto(again);

            CollectionAssert.AreEqual(new[] { "coral", "plum" }, again.companionsOwned);
        }

        [Test]
        public void TheWholeSaveMergeKeepsBothDevicesPurchases()
        {
            // Through SaveMerge rather than the ledger's own Join, so the wiring is pinned
            // too — a join nothing calls is a join that does not run.
            var mine = File("coral");
            mine.updatedUnix = 100;

            var other = File("wisp");
            other.updatedUnix = 200;

            var merged = SaveMerge.Join(mine, other);

            CollectionAssert.AreEqual(new[] { "coral", "wisp" }, merged.companionsOwned);
            CollectionAssert.AreEqual(new[] { "coral", "wisp" },
                                      SaveMerge.Join(other, mine).companionsOwned,
                                      "and in either order");
        }

        [Test]
        public void ADeltaNoticesAPurchaseTheServerHasNotHeardAbout()
        {
            // A purchase is the one thing in the save that cannot be re-derived, so a set
            // that never reached the server is a companion lost on reinstall.
            var remote = File("coral");
            var local = File("coral", "wisp");

            Assert.IsTrue(SaveDelta.Between(remote, local).ScalarsChanged);
            Assert.IsFalse(SaveDelta.Between(remote, File("coral")).ScalarsChanged,
                           "and an unchanged set sends nothing");
        }

        // ------------------------------------------------------------ the ladder
        [Test]
        public void TheShippedLadderPricesEveryGatedCompanionAndOnlyFreesTheStarter()
        {
            // Against the built-in roster, which is what a client whose content fetch failed
            // falls back to — and which must therefore obey the same rules as the manifest.
            AvatarCatalog.Publish(null);

            int starters = 0;
            foreach (var avatar in AvatarCatalog.All)
            {
                if (avatar.IsStarter) { starters++; continue; }

                Assert.IsTrue(avatar.IsForSale,
                              $"'{avatar.Id}' is gated at level {avatar.UnlockLevel} with no price, " +
                              "so a player below that gate has no route to it");
            }

            Assert.AreEqual(1, starters, "exactly one companion is free from the first launch");
        }

        [Test]
        public void ThePriceLadderRisesWithTheGate()
        {
            // A later companion that costs less inverts the ladder: the grid would show a
            // cheaper price beside a rarer friend and the roster stops reading as progress.
            AvatarCatalog.Publish(null);

            int lastLevel = -1, lastCost = -1;
            foreach (var avatar in AvatarCatalog.All)
            {
                if (!avatar.IsForSale) continue;

                if (lastLevel >= 0 && avatar.UnlockLevel > lastLevel)
                    Assert.GreaterOrEqual(avatar.UnlockCost, lastCost,
                                          $"'{avatar.Id}' unlocks later but costs less");

                lastLevel = avatar.UnlockLevel;
                lastCost = avatar.UnlockCost;
            }
        }

        [Test]
        public void NoGatedCompanionIsBuyableOutOfTheAccountSeedAlone()
        {
            // A gate the seed walks straight past is not a gate. Half the seed is the
            // threshold ContentValidation enforces on the manifest; the built-in roster is
            // held to it here, because nothing validates a fallback list at build time.
            AvatarCatalog.Publish(null);

            foreach (var avatar in AvatarCatalog.All)
            {
                if (!avatar.IsForSale || avatar.IsStarter) continue;

                Assert.Greater(avatar.UnlockCost, Currency.SeedCredits / 2,
                               $"'{avatar.Id}' is gated at level {avatar.UnlockLevel} but costs " +
                               $"{avatar.UnlockCost}, which a new account can pay before playing");
            }
        }

        [Test]
        public void ASetOfPurchasesIsNeverRepresentableAsACount()
        {
            // A guard on the shape rather than on a value, because the shape is the whole
            // argument. Two devices holding {coral} and {plum, wisp} union to three; a count
            // would have to choose between 1 and 2 and both answers lose a companion.
            var left = new[] { "coral" };
            var right = new[] { "plum", "wisp" };

            Assert.AreEqual(3, CompanionLedger.Join(left, right).Length);
            Assert.AreNotEqual(left.Length, CompanionLedger.Join(left, right).Length);
            Assert.AreNotEqual(right.Length, CompanionLedger.Join(left, right).Length);
        }
    }
}
