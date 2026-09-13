using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Homestead;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The keeper gates up the home ladder — the rule that a rung is opened by playing and
    /// bought with credits, and that neither half alone is enough.
    ///
    /// <para>
    /// <b>Why the ladder is gated at all.</b> It is the longest goal in the game and credits
    /// are a poor clock for it: they accumulate whatever a player does, so a saver could stand
    /// in a citadel having played a fraction of the game, and a rung reachable on the day the
    /// shop first opens was never a goal.
    /// </para>
    /// <para>
    /// <b>What it must not become is a second way of paying.</b> That is invariant 15a, which
    /// companions learned the expensive way: if reaching the level handed the home over, the
    /// price would be unreachable code — and if the price alone settled it, the level would be
    /// decoration (invariant 5d). So both are required, and the gate is asked first, because
    /// when two refusals apply the one to say is the one credits cannot answer.
    /// </para>
    /// <para>
    /// <b>Why the gates here are built around the live keeper level rather than typed.</b>
    /// <c>HomesteadLedger</c> reads the level off <c>PlayerProgression</c> rather than taking
    /// it as an argument, deliberately — every caller is a grove screen asking about the player
    /// in front of it. Driving that singleton would mean authoring a star ledger to assert
    /// something about a shop, so each case reads the level it is really running at and puts
    /// its gate above or below it. What is then asserted is the <em>decision</em>, which is the
    /// half that can be wrong.
    /// </para>
    /// </summary>
    public sealed class HomeLadderGateTests
    {
        const int Cheap = 100;

        [SetUp]
        public void Open()
        {
            SaveService.Unload();
            SaveService.LoadWith(new MemoryStore());

            HomesteadLedger.ResetForTests();
            HomesteadLayout.ResetForTests();
            HomesteadCatalog.Publish(Catalog());
        }

        [TearDown]
        public void Close()
        {
            HomesteadLedger.ResetForTests();
            HomesteadLayout.ResetForTests();
            HomesteadCatalog.Publish(null);
            SaveService.Unload();
        }

        // ------------------------------------------------------------- fixtures
        /// <summary>The level this account is really at, which every gate below is placed around.</summary>
        static int Rank => PlayerProgression.Level.Level;

        static HomesteadPiece Home(string id, int tier, int cost, int level)
            => new HomesteadPiece(id, "Homestead/" + id, false, HomesteadPieceKind.Dwelling,
                                  cost, LevelId.None, ChapterId.None, 1f, .45f,
                                  HomesteadSlotKind.Ground, tier, requiresKeeperLevel: level);

        /// <summary>
        /// A cottage everybody holds, a rung this account has reached, and two it has not.
        ///
        /// <para>
        /// <b>The two locked rungs differ only in price, and that is what makes the cases
        /// separable.</b> <c>keep</c> is locked and perfectly <em>affordable</em>, so a refusal
        /// there can only be the gate's doing — priced out of reach it would go on being refused
        /// with the gate deleted, which is a test that cannot fail. <c>citadel</c> is the
        /// opposite corner: both refusals apply at once, which is the only shape that can say
        /// which of the two is reported.
        /// </para>
        /// </summary>
        static HomesteadCatalog Catalog()
            => new HomesteadCatalog(
                HomesteadCatalog.Current.Floor,
                new List<HomesteadPiece>
                {
                    Home("cottage", 1, 0, 0),
                    Home("lodge", 2, Cheap, Rank),
                    Home("keep", 3, Cheap, Rank + 5),
                    Home("citadel", 4, int.MaxValue, Rank + 9),
                },
                null);

        static HomesteadPiece Find(string id) => HomesteadCatalog.Current.Find(id);

        /// <summary>
        /// A save file that never reaches a disk — <c>GroveStockPurchaseTests.MemoryStore</c>,
        /// which is itself <c>AccountSwitchTests</c>' copy, and kept as it was given rather than
        /// round tripped through JSON for their reason: serialisation is <c>SaveStoreTests</c>'
        /// subject, and borrowing it here would put <c>JsonUtility</c> in the way of a question
        /// about a shop.
        /// </summary>
        sealed class MemoryStore : ISaveStore
        {
            SaveFileDto _file;

            public SaveFileDto Load() => _file ?? new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                settings = new SettingsDto(),
                wallet = WalletDto.Unwritten(),
                levels = new LevelRecordDto[0],
                progression = ProgressionStateDto.Unwritten(),
                cloud = new CloudStateDto(),

                // Otherwise the load reaches LegacyPlayerPrefsImport, which is PlayerPrefs,
                // which is the Editor. There is no legacy build to import from in a test.
                legacyImportDone = true,
            };

            public bool Save(SaveFileDto dto)
            {
                _file = dto;
                return true;
            }

            public void Delete() => _file = null;
        }

        // ============================================================ the refusal
        [Test]
        public void ARungAboveTheKeepersLevelIsLockedAndNamesTheLevel()
        {
            var offer = HomesteadLedger.OfferFor(Find("keep"));

            Assert.AreEqual(HomesteadPurchaseState.LevelLocked, offer.State);
            Assert.AreEqual(Rank + 5, offer.RequiredLevel,
                            "the panel has to name the level, or the refusal is unactionable");
            Assert.IsFalse(offer.CanBuy);
        }

        [Test]
        public void TheGateIsAskedBeforeThePrice()
        {
            // `citadel` is both above the level and far beyond any balance, so both refusals
            // apply and only one of them can be reported. Invariant 15a's ordering: leading with
            // the price sends somebody to the coin shelf for a house the coins could not buy.
            Assert.Less(PlayerProgression.Credits, (long)int.MaxValue, "the fixture is not poor");

            Assert.AreEqual(HomesteadPurchaseState.LevelLocked,
                            HomesteadLedger.OfferFor(Find("citadel")).State);
        }

        [Test]
        public void AReachedRungFallsThroughToThePriceAndCarriesItsLevelAnyway()
        {
            var offer = HomesteadLedger.OfferFor(Find("lodge"));

            Assert.AreNotEqual(HomesteadPurchaseState.LevelLocked, offer.State,
                               "a gate the player has passed must not go on refusing them");
            Assert.AreEqual(Cheap, offer.Cost);

            // The level rides the offer even when it is not what is refusing, so a panel can
            // say what a rung asked for without putting a second question to the catalog.
            Assert.AreEqual(Rank, offer.RequiredLevel);
        }

        [Test]
        public void ALockedRungIsNotHeldAndCannotBeBought()
        {
            long before = PlayerProgression.Credits;

            // `keep` is one the player could pay for outright, so nothing but the gate can be
            // what refuses it — the point of pricing it inside the seed.
            Assert.GreaterOrEqual(before, (long)Cheap, "the fixture cannot afford the rung");

            Assert.IsFalse(HomesteadLedger.IsHeld(Find("keep")),
                           "reaching a gate is permission to pay, never a way of paying — a rung "
                           + "that arrived free would make its own price unreachable code");
            Assert.IsFalse(HomesteadLedger.TryBuy(Find("keep")));
            Assert.AreEqual(before, PlayerProgression.Credits, "a refused purchase charged");
        }

        [Test]
        public void PayingForAReachedRungWorksExactlyAsItDidBeforeTheGate()
        {
            var lodge = Find("lodge");
            long before = PlayerProgression.Credits;

            Assert.IsTrue(HomesteadLedger.TryBuy(lodge));
            Assert.AreEqual(before - Cheap, PlayerProgression.Credits);
            Assert.IsTrue(HomesteadLedger.IsHeld(lodge));
            Assert.AreEqual("lodge", HomesteadLedger.BestDwelling(HomesteadCatalog.Current).Id);
        }

        // ============================================================ the ladder
        [Test]
        public void ALockedRungIsStillTheNextOne()
        {
            // The panel answers "where am I on the ladder", so a rung the level has not opened
            // is still the one it names. Skipping it would offer the keep to somebody who cannot
            // yet buy the lodge, which is a ladder read as a shelf.
            Assert.AreEqual("lodge", HomesteadLedger.NextDwelling(HomesteadCatalog.Current).Id);

            Assert.IsTrue(HomesteadLedger.TryBuy(Find("lodge")));

            Assert.AreEqual("keep", HomesteadLedger.NextDwelling(HomesteadCatalog.Current).Id,
                            "the locked rung is what comes next, and the panel says so");
        }

        [Test]
        public void AGatedRungHasNoFreeRoute()
        {
            // The shop hangs a leaf on anything playing alone can get. A gate is not one of
            // those: the keep still costs credits the day the level arrives, so a leaf over it
            // would promise a route that does not exist.
            Assert.IsFalse(HomesteadLedger.HasFreeRoute(Find("keep")));
            Assert.IsFalse(HomesteadLedger.HasFreeRoute(Find("citadel")));
            Assert.IsFalse(HomesteadLedger.HasFreeRoute(Find("lodge")));

            Assert.IsTrue(HomesteadLedger.HasFreeRoute(Find("cottage")),
                          "the free first rung is held by everybody and always was");
        }

        [Test]
        public void TheFirstRungIsUngatedSoANewGroveIsNeverEmpty()
        {
            var cottage = Find("cottage");

            Assert.AreEqual(0, cottage.RequiresKeeperLevel);
            Assert.IsTrue(cottage.IsStarter);
            Assert.IsTrue(HomesteadLedger.IsHeld(cottage));
            Assert.AreEqual("cottage", HomesteadLedger.BestDwelling(HomesteadCatalog.Current).Id);
        }

        // ============================================================ the reader
        /// <summary>
        /// Reaches <c>JsonUtility</c> before the mapper does — <c>HomesteadTests</c>' bargain,
        /// for its reason: the mapper turns every parse failure into a reported problem, so
        /// offline it would report "not valid JSON" about JSON that is perfectly valid. Touching
        /// the engine call directly lets the runner mark the case as needing the Editor instead
        /// of failing it for the wrong reason.
        /// </summary>
        static void RequiresJsonUtility() => JsonUtility.FromJson<HomesteadBodyDto>("{}");

        const string Floor = "{\"schemaVersion\":3,\"floor\":{\"cols\":4,\"rows\":4," +
                             "\"hallTile\":\"t_000_000\",\"hallCols\":1,\"hallRows\":1," +
                             "\"regions\":[{\"id\":\"home\",\"col\":0,\"row\":0,\"cols\":4,\"rows\":4}]},";

        [Test]
        public void OnlyAHomeMayCarryAGate()
        {
            RequiresJsonUtility();

            // Decor is earned by clearing a named thing, which a player can go and do; a level
            // gate on a bench would be a wait with nothing to aim at. Dropped and *reported*
            // rather than swallowed, because a catalogue describing a rule the game does not run
            // is a rule its author has no way to discover is missing.
            var problems = new List<string>();
            string json = Floor + "\"pieces\":[{\"id\":\"bench\",\"kind\":\"decor\"," +
                          "\"cost\":50,\"requiresKeeperLevel\":9}]}";

            Assert.IsTrue(HomesteadMapper.TryRead(json, problems, out var catalog));

            Assert.AreEqual(0, catalog.Find("bench").RequiresKeeperLevel, "the gate is dropped");
            Assert.AreEqual(1, problems.Count, "and it is reported");
            StringAssert.Contains("bench", problems[0]);
        }

        [Test]
        public void AHomeKeepsTheGateItAuthored()
        {
            RequiresJsonUtility();

            var problems = new List<string>();
            string json = Floor + "\"pieces\":[{\"id\":\"manor\",\"kind\":\"dwelling\"," +
                          "\"tier\":2,\"cost\":2500,\"requiresKeeperLevel\":10}]}";

            Assert.IsTrue(HomesteadMapper.TryRead(json, problems, out var catalog));

            Assert.AreEqual(10, catalog.Find("manor").RequiresKeeperLevel);
            CollectionAssert.IsEmpty(problems);
        }

        [Test]
        public void ACatalogueWrittenBeforeTheLadderWasGatedReadsAsUngated()
        {
            RequiresJsonUtility();

            // Remote delivery means a client can be a drop behind in either direction, and
            // JsonUtility writes a zero into every field an older file never had — so absent and
            // "no gate" have to be the same value. It is what keeps the first rung free.
            var problems = new List<string>();
            string json = Floor + "\"pieces\":[{\"id\":\"hut\",\"kind\":\"dwelling\",\"tier\":1}]}";

            Assert.IsTrue(HomesteadMapper.TryRead(json, problems, out var catalog));

            Assert.AreEqual(0, catalog.Find("hut").RequiresKeeperLevel);
            Assert.IsTrue(catalog.Find("hut").IsStarter);
        }

        // ========================================================== the shipped ladder
        [Test]
        public void TheShippedLaddersGatesClimbWithItsTiers()
        {
            RequiresJsonUtility();

            // What the build gate proves, pinned here as well because the two say it for
            // different reasons: the gate refuses a catalogue that cannot ship, and this refuses
            // a *reader* that stops carrying the field. A rung whose gate is not above the one
            // below it refuses nobody, and a gated rung with no price could never be held at all.
            var problems = new List<string>();
            string json = System.IO.File.ReadAllText(
                System.IO.Path.Combine(Application.streamingAssetsPath, "Content", "homestead.json"));

            Assert.IsTrue(HomesteadMapper.TryRead(json, problems, out var catalog));

            var ladder = new List<HomesteadPiece>();
            foreach (var piece in catalog.Pieces)
                if (piece.IsDwelling) ladder.Add(piece);

            ladder.Sort((a, b) => a.Tier.CompareTo(b.Tier));
            Assert.Greater(ladder.Count, 1, "there is no ladder to check");

            Assert.AreEqual(0, ladder[0].RequiresKeeperLevel, "the first rung opens to everybody");
            Assert.IsTrue(ladder[0].IsStarter, "and it is free, or a new grove has no home");

            for (int i = 1; i < ladder.Count; i++)
            {
                Assert.Greater(ladder[i].RequiresKeeperLevel, ladder[i - 1].RequiresKeeperLevel,
                               $"'{ladder[i].Id}' asks for a level '{ladder[i - 1].Id}' has passed");
                Assert.IsTrue(ladder[i].IsForSale,
                              $"'{ladder[i].Id}' is gated and unpriced, so nothing would grant it");
            }
        }
    }
}
