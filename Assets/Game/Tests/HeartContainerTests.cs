using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Store;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Heart containers: the one real-money product in this game that grants something other
    /// than currency, and the rules that make that safe.
    ///
    /// <para>
    /// Three properties are pinned here and none of them can be seen by reading the catalog.
    /// The <b>cap is derived</b> from the ids held, so it is the largest container rather than
    /// the sum and it moves with a retune. <b>Granting is idempotent</b>, which is the whole
    /// reason a real-money product may hand over an entitlement at all — it is what removes
    /// the "did I already apply this transaction" record whose absence invariant 18 protects,
    /// and it is what lets a Restore rebuild the entitlement on a phone with no save at all.
    /// And <b>a refund reaches every device</b>, because a permanent upgrade that outlived its
    /// refund is invariant 18c's leak with a bigger price on it.
    /// </para>
    /// <para>
    /// The ladder itself is exercised against <see cref="StoreCatalog.Default"/> rather than a
    /// fixture, deliberately: what ships is what has to work, and a fixture would prove the
    /// arithmetic while letting the three real products drift out from under it.
    /// </para>
    /// </summary>
    public sealed class HeartContainerTests
    {
        [SetUp]
        public void Fresh()
        {
            ProgressionRules.Reset();
            HeartContainerLedger.Reset();
        }

        [TearDown]
        public void Restore()
        {
            HeartContainerLedger.Reset();
            ProgressionRules.Reset();
        }

        static StoreProduct Vessel(string id)
        {
            var product = StoreRules.Find(id);
            Assert.IsNotNull(product, $"the shipped catalog no longer carries '{id}'");
            Assert.IsTrue(product.IsContainer, $"'{id}' is no longer a heart container");
            return product;
        }

        static StoreProductDto Dto(string id, string shelf, string kind, int capacity,
                                   long credits = 0, long gems = 0, int cents = 1999)
            => new StoreProductDto
            {
                id = id, shelf = shelf, kind = kind, heartCapacity = capacity,
                credits = credits, gems = gems, referenceUsdCents = cents,
            };

        // ----------------------------------------------------------------- the cap
        [Test]
        public void AnAccountHoldingNoContainerRefillsToThePublishedCap()
        {
            Assert.AreEqual(HeartRules.RefillCap, HeartContainerLedger.RefillCap);
        }

        [Test]
        public void BuyingAContainerRaisesTheCap()
        {
            HeartContainerLedger.Grant(Vessel("gg_heart_vessel_2"));

            Assert.AreEqual(20, HeartContainerLedger.RefillCap);
            Assert.AreEqual(20, Wallet.MaxHearts, "every screen draws its denominator from here");
        }

        /// <summary>
        /// The largest, never the sum — which is what makes buying the rungs out of order,
        /// buying one twice through a restore, or restoring onto a device that already holds a
        /// better one all resolve to the same number with no special case anywhere.
        /// </summary>
        [Test]
        public void TheCapIsTheLargestContainerHeldAndNotTheSum()
        {
            HeartContainerLedger.Grant(Vessel("gg_heart_vessel_3"));
            HeartContainerLedger.Grant(Vessel("gg_heart_vessel_1"));

            Assert.AreEqual(50, HeartContainerLedger.RefillCap);
        }

        [Test]
        public void TheCapIsNeverAboveThePublishedCeiling()
        {
            foreach (var product in StoreRules.Catalog.Products)
                if (product.IsContainer) HeartContainerLedger.Grant(product);

            Assert.LessOrEqual(HeartContainerLedger.RefillCap, HeartRules.Ceiling,
                "a timer carrying a player past the most they may hold would leave every " +
                "grant refused while the clock kept paying");
        }

        /// <summary>
        /// An id this build's catalog has never heard of is kept and ignored, exactly as
        /// <c>tipsSeen</c> and <c>companionsOwned</c> keep theirs. Here the stake is higher
        /// than a lost tip: dropping it would be a real payment silently undone by a trip
        /// through an older build.
        /// </summary>
        [Test]
        public void AContainerFromTheFutureIsCarriedThroughAndIgnored()
        {
            HeartContainerLedger.LoadFrom(new SaveFileDto
            {
                heartContainersOwned = new[] { "gg_heart_vessel_from_the_future" },
            });

            Assert.AreEqual(HeartRules.RefillCap, HeartContainerLedger.RefillCap);

            var written = new SaveFileDto();
            HeartContainerLedger.WriteInto(written);

            CollectionAssert.Contains(written.heartContainersOwned,
                                      "gg_heart_vessel_from_the_future");
        }

        // ------------------------------------------------------------- idempotence
        /// <summary>
        /// The property the whole feature rests on. Applying a container twice is applying it
        /// once, so no record of "have I already applied this transaction" has to exist — and
        /// without that record, invariant 18's argument against a real-money product granting
        /// something other than currency simply does not apply.
        /// </summary>
        [Test]
        public void GrantingTheSameContainerTwiceChangesNothingTheSecondTime()
        {
            var vessel = Vessel("gg_heart_vessel_1");

            Assert.IsTrue(HeartContainerLedger.Grant(vessel));
            Assert.IsFalse(HeartContainerLedger.Grant(vessel),
                "a re-delivered receipt must not read as something new to celebrate");

            Assert.AreEqual(10, HeartContainerLedger.RefillCap);
        }

        [Test]
        public void ACurrencyProductIsNeverRecordedAsAContainer()
        {
            Assert.IsFalse(HeartContainerLedger.Grant(StoreRules.Find("gg_gems_3")));
            Assert.AreEqual(HeartRules.RefillCap, HeartContainerLedger.RefillCap);
        }

        // ---------------------------------------------------------------- refunds
        [Test]
        public void ARevokedContainerStopsCounting()
        {
            HeartContainerLedger.Grant(Vessel("gg_heart_vessel_2"));
            Assert.AreEqual(20, HeartContainerLedger.RefillCap);

            HeartContainerLedger.ApplyServerRevocations(new[] { "gg_heart_vessel_2" });

            Assert.AreEqual(HeartRules.RefillCap, HeartContainerLedger.RefillCap);
            Assert.IsFalse(HeartContainerLedger.IsHeld("gg_heart_vessel_2"));
            Assert.IsTrue(HeartContainerLedger.WasRevoked("gg_heart_vessel_2"));
        }

        /// <summary>
        /// The safety property of the whole refund path: the server reports what it
        /// <em>revoked</em>, never what it thinks the account owns. An empty answer therefore
        /// means "nothing was refunded" and can never confiscate a purchase — which is what a
        /// short reply, a cold account or a deployment predating the field all look like.
        /// </summary>
        [Test]
        public void AnEmptyServerAnswerRevokesNothing()
        {
            HeartContainerLedger.Grant(Vessel("gg_heart_vessel_3"));

            HeartContainerLedger.ApplyServerRevocations(new List<string>());
            HeartContainerLedger.ApplyServerRevocations(null);

            Assert.AreEqual(50, HeartContainerLedger.RefillCap);
        }

        [Test]
        public void BuyingARefundedContainerAgainLiftsTheRevocation()
        {
            var vessel = Vessel("gg_heart_vessel_2");

            HeartContainerLedger.Grant(vessel);
            HeartContainerLedger.ApplyServerRevocations(new[] { vessel.Id });

            Assert.IsTrue(HeartContainerLedger.Grant(vessel),
                "re-buying is a real receipt and has to read as something new");

            Assert.AreEqual(20, HeartContainerLedger.RefillCap);
            Assert.IsFalse(HeartContainerLedger.WasRevoked(vessel.Id));
        }

        // ------------------------------------------------------------------ merge
        /// <summary>
        /// Both sets are monotonic, so both are joined by union and the answer cannot depend
        /// on which device merged first. That is what makes a refund converge: "the newer
        /// device is right" would have the two phones handing a revoked container back and
        /// forth for ever.
        /// </summary>
        [Test]
        public void TheJoinIsAUnionAndOrderCannotChangeIt()
        {
            var mine = new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                heartContainersOwned = new[] { "gg_heart_vessel_1" },
                heartContainersRevoked = new string[0],
            };

            var other = new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                heartContainersOwned = new[] { "gg_heart_vessel_2" },
                heartContainersRevoked = new[] { "gg_heart_vessel_1" },
            };

            var joined = SaveMerge.Join(mine, other);
            var reversed = SaveMerge.Join(other, mine);

            CollectionAssert.AreEqual(new[] { "gg_heart_vessel_1", "gg_heart_vessel_2" },
                                      joined.heartContainersOwned);
            CollectionAssert.AreEqual(new[] { "gg_heart_vessel_1" }, joined.heartContainersRevoked);

            CollectionAssert.AreEqual(joined.heartContainersOwned, reversed.heartContainersOwned);
            CollectionAssert.AreEqual(joined.heartContainersRevoked, reversed.heartContainersRevoked);
        }

        /// <summary>
        /// A refund honoured on one device is not undone by a sync from another. This is the
        /// case that decides whether the revocation set was worth a schema field at all — the
        /// cloud save still names the container, so a plain union over the owned set alone
        /// would hand it straight back.
        /// </summary>
        [Test]
        public void AMergeWithADeviceThatHasNotHeardOfTheRefundKeepsTheRefund()
        {
            var refunded = new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                heartContainersOwned = new[] { "gg_heart_vessel_3" },
                heartContainersRevoked = new[] { "gg_heart_vessel_3" },
            };

            var stale = new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                heartContainersOwned = new[] { "gg_heart_vessel_3" },
            };

            HeartContainerLedger.LoadFrom(SaveMerge.Join(stale, refunded));

            Assert.AreEqual(HeartRules.RefillCap, HeartContainerLedger.RefillCap);
        }

        [Test]
        public void TheSetsSurviveARoundTripThroughTheSaveFile()
        {
            HeartContainerLedger.Grant(Vessel("gg_heart_vessel_1"));
            HeartContainerLedger.Grant(Vessel("gg_heart_vessel_2"));
            HeartContainerLedger.ApplyServerRevocations(new[] { "gg_heart_vessel_1" });

            var dto = new SaveFileDto();
            HeartContainerLedger.WriteInto(dto);

            HeartContainerLedger.Reset();
            HeartContainerLedger.LoadFrom(dto);

            Assert.AreEqual(20, HeartContainerLedger.RefillCap);
            Assert.IsTrue(HeartContainerLedger.WasRevoked("gg_heart_vessel_1"));
        }

        /// <summary>
        /// Sorted on the way out, and not for tidiness: <c>SaveChecksum</c> hashes the
        /// serialised file and <c>SaveDelta</c> decides whether to sync by walking these in
        /// order, so ids in hash-set order would make an unchanged save read as changed and
        /// push a write on every launch, for ever.
        /// </summary>
        [Test]
        public void TheSetsAreWrittenSorted()
        {
            HeartContainerLedger.LoadFrom(new SaveFileDto
            {
                heartContainersOwned = new[] { "z_last", "a_first", "m_middle" },
            });

            var dto = new SaveFileDto();
            HeartContainerLedger.WriteInto(dto);

            CollectionAssert.AreEqual(new[] { "a_first", "m_middle", "z_last" },
                                      dto.heartContainersOwned);
        }

        // ------------------------------------------------------------- the timer
        /// <summary>
        /// The point of the whole purchase, and the one thing no other test here would catch:
        /// the refill clock actually carries the player past the free cap.
        /// </summary>
        [Test]
        public void TheRefillClockCarriesAPlayerToTheContainersCap()
        {
            long start = 1_700_000_000;

            // A day and a half of waiting at eight hours a heart is far more than five and
            // comfortably short of twenty, so the two readings cannot both come from the
            // clock running out of time.
            long later = start + HeartRules.RefillSeconds * 40;

            var empty = Hearts.Ledger(0, 0, start);

            Assert.AreEqual(HeartRules.RefillCap, empty.At(later).Count,
                            "the free cap is where the clock stops for an ordinary account");

            HeartContainerLedger.Grant(Vessel("gg_heart_vessel_2"));

            Assert.AreEqual(20, empty.At(later).Count);
            Assert.IsTrue(empty.At(later).IsRefilled);
        }

        /// <summary>
        /// Buying a container hands over the hearts the player really did wait for while they
        /// were sitting at their old cap — the refill deadline idles in the past, so the
        /// catch-up walk pays it out the moment there is room. Bounded by the cap that was
        /// paid for, and unrepeatable without buying again.
        /// </summary>
        [Test]
        public void AKeeperWhoHasBeenSittingAtTheirCapArrivesAtTheNewOneAtOnce()
        {
            long start = 1_700_000_000;
            long later = start + HeartRules.RefillSeconds * 30;

            // Full, with a deadline long since passed — a player who has not lost a run in a
            // fortnight, which is the commonest state a container is bought from.
            var full = Hearts.Ledger(HeartRules.RefillCap, 0, start);
            Assert.AreEqual(HeartRules.RefillCap, full.At(later).Count);

            HeartContainerLedger.Grant(Vessel("gg_heart_vessel_1"));

            Assert.AreEqual(10, full.At(later).Count,
                "the wait already happened; the cap is what was in the way");
        }

        /// <summary>
        /// And a player who has been spending gets the ceiling without the windfall, because
        /// their deadline is a real one in the future rather than an idling one in the past.
        /// </summary>
        [Test]
        public void AKeeperWhoHasBeenPlayingGetsTheCeilingAndNoWindfall()
        {
            long now = 1_700_000_000;

            var spent = Hearts.Ledger(4, 3, now + HeartRules.RefillSeconds);

            HeartContainerLedger.Grant(Vessel("gg_heart_vessel_3"));

            Assert.AreEqual(1, spent.At(now).Count);
            Assert.AreEqual(50, Wallet.MaxHearts);
        }

        [Test]
        public void AFullBarStopsBeingFullWhenTheCapRises()
        {
            var five = Hearts.Ledger(HeartRules.RefillCap, 0, 0);
            Assert.IsTrue(five.IsRefilled);

            HeartContainerLedger.Grant(Vessel("gg_heart_vessel_1"));

            Assert.IsFalse(five.IsRefilled, "the timer has work to do again");
            Assert.AreEqual(HeartRules.RefillCap, five.Count, "and nothing was taken away");
        }

        // ------------------------------------------------------------------ cache
        /// <summary>
        /// The derived cap is cached, because the walk behind it is a dictionary lookup per
        /// container per HUD tick and it would fall entirely on the players who paid. A cache
        /// is a new way to be silently wrong, so the two things that can stale it are pinned
        /// rather than trusted: the sets changing, which every other test here exercises, and
        /// a content push, which is this one.
        ///
        /// <para>
        /// It is keyed on the catalog by <em>reference</em>, so a push swapping in a whole new
        /// immutable table invalidates it with no event to subscribe to and no install step to
        /// forget. Retuning a shipped container upward is a legitimate thing to want — it
        /// reaches everybody who already paid for it, which is the whole reason the cap is
        /// derived from the id rather than stored beside it.
        /// </para>
        /// </summary>
        [Test]
        public void AContentPushThatRetunesAContainerReachesSomebodyAlreadyHoldingIt()
        {
            HeartContainerLedger.Grant(Vessel("gg_heart_vessel_2"));
            Assert.AreEqual(20, HeartContainerLedger.RefillCap, "read once, so it is now cached");

            // The smallest table TryRead will accept, plus the one product this is about. The
            // curve is required and is not the subject; the store block is.
            const string retuned = @"{
              ""schemaVersion"": 1,
              ""maxLevel"": 5,
              ""xpToNext"": [100, 200, 300, 400],
              ""tailXpToNext"": 500,
              ""tailXpIncrement"": 50,
              ""store"": { ""products"": [
                { ""id"": ""gg_gems_1"", ""kind"": ""consumable"", ""shelf"": ""gems"",
                  ""gems"": 100, ""referenceUsdCents"": 99 },
                { ""id"": ""gg_heart_vessel_2"", ""kind"": ""nonconsumable"", ""shelf"": ""supplies"",
                  ""heartCapacity"": 30, ""referenceUsdCents"": 2999 }
              ] }
            }";

            var problems = new List<string>();
            Assert.IsTrue(ProgressionTable.TryRead(retuned, out var table, problems),
                          string.Join("; ", problems));

            ProgressionRules.Publish(table);

            Assert.AreEqual(30, HeartContainerLedger.RefillCap,
                "a cache the catalog cannot invalidate is a retune that reaches nobody");
        }

        // ---------------------------------------------------------- the catalog
        /// <summary>
        /// The "never both" half of the rule, and it is load-bearing rather than tidy: a
        /// container that also paid gems would put a stored <em>amount</em> back onto the
        /// client's side of a purchase, which is the exact thing invariant 18 exists to keep
        /// off it.
        /// </summary>
        [Test]
        public void AProductThatSellsACapacityAndCurrencyIsRefused()
        {
            var problems = new List<string>();
            var catalog = StoreCatalog.Resolve(new StoreDto
            {
                products = new[]
                {
                    Dto("gems_a", "gems", "consumable", 0, gems: 100, cents: 99),
                    Dto("mixed", "supplies", "nonconsumable", 10, gems: 100),
                },
            }, problems);

            Assert.IsNull(catalog.Find("mixed"));
            Assert.IsTrue(problems.Exists(p => p.Contains("mixed")));
        }

        [Test]
        public void ACapacitySoldAsAConsumableIsRefused()
        {
            var problems = new List<string>();
            var catalog = StoreCatalog.Resolve(new StoreDto
            {
                products = new[]
                {
                    Dto("gems_a", "gems", "consumable", 0, gems: 100, cents: 99),
                    Dto("repeatable", "supplies", "consumable", 10),
                },
            }, problems);

            // The store itself is what stops a permanent upgrade being sold twice, and only a
            // non-consumable gets that guarantee — it is also what makes Restore able to bring
            // the entitlement back on a phone with no save at all.
            Assert.IsNull(catalog.Find("repeatable"));
        }

        [Test]
        public void ACapacityOnTheWrongShelfIsRefused()
        {
            var problems = new List<string>();
            var catalog = StoreCatalog.Resolve(new StoreDto
            {
                products = new[]
                {
                    Dto("gems_a", "gems", "consumable", 0, gems: 100, cents: 99),
                    Dto("hidden", "bundles", "nonconsumable", 10),
                },
            }, problems);

            Assert.IsNull(catalog.Find("hidden"),
                "a capacity filed anywhere but the hearts shelf is one nobody browsing hearts finds");
        }

        [Test]
        public void ACurrencyProductOnTheSuppliesShelfIsRefused()
        {
            var problems = new List<string>();
            var catalog = StoreCatalog.Resolve(new StoreDto
            {
                products = new[]
                {
                    Dto("gems_a", "gems", "consumable", 0, gems: 100, cents: 99),
                    Dto("misfiled", "supplies", "consumable", 0, gems: 500, cents: 299),
                },
            }, problems);

            Assert.IsNull(catalog.Find("misfiled"));
        }

        [Test]
        public void ACapacityOutsideTheSupportedRangeIsDroppedRatherThanClamped()
        {
            var problems = new List<string>();
            var catalog = StoreCatalog.Resolve(new StoreDto
            {
                products = new[]
                {
                    Dto("gems_a", "gems", "consumable", 0, gems: 100, cents: 99),
                    Dto("too_small", "supplies", "nonconsumable", StoreLimits.MinHeartCapacity - 1),
                    Dto("too_big", "supplies", "nonconsumable", StoreLimits.MaxHeartCapacity + 1),
                },
            }, problems);

            // Clamping would sell a capacity nobody authored against a ledger that honours a
            // different one — a card promising more than it gives.
            Assert.IsNull(catalog.Find("too_small"));
            Assert.IsNull(catalog.Find("too_big"));
        }

        /// <summary>
        /// The client's two ceilings agree. A container selling a cap the ledger clamps away
        /// is a player charged for a number they never receive, and the two constants live in
        /// different files for good reasons — one bounds content, the other bounds the ledger.
        /// </summary>
        [Test]
        public void TheCatalogCeilingAndTheLedgerCeilingAgree()
        {
            Assert.AreEqual(HeartLimits.MaxRefillCap, StoreLimits.MaxHeartCapacity);
        }

        /// <summary>
        /// The shipped ladder: every rung is worth buying, and each is worth more than the one
        /// below it. Both halves are invisible in the file, because the price and the capacity
        /// sit in different columns.
        /// </summary>
        [Test]
        public void TheShippedContainerLadderOnlyEverGetsBetter()
        {
            var vessels = new List<StoreProduct>();
            foreach (var product in StoreCatalog.Default.Products)
                if (product.IsContainer) vessels.Add(product);

            Assert.IsNotEmpty(vessels);
            vessels.Sort((a, b) => a.ReferenceUsdCents.CompareTo(b.ReferenceUsdCents));

            for (int i = 0; i < vessels.Count; i++)
            {
                Assert.Greater(vessels[i].HeartCapacity, HeartLimits.DefaultRefillCap,
                    $"{vessels[i].Id} takes real money and changes nothing the player can see");

                Assert.LessOrEqual(vessels[i].HeartCapacity, HeartLimits.DefaultCeiling,
                    $"{vessels[i].Id} promises more than the ledger will honour");

                if (i == 0) continue;

                Assert.Greater(vessels[i].HeartCapacity, vessels[i - 1].HeartCapacity,
                    $"{vessels[i].Id} costs more than {vessels[i - 1].Id} and holds no more");
            }
        }
    }
}
