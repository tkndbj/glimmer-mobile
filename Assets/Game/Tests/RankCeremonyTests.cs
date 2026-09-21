using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Ranks;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Which rungs are owed a ceremony — the half of <see cref="RankCeremony"/> that decides,
    /// as opposed to the half that draws.
    ///
    /// <para>
    /// <b>Everything here runs offline</b> (invariant 29e): the ladder is built from DTOs by
    /// hand, the catalog from a builder, and nothing touches a canvas, a sprite or
    /// <c>Application.dataPath</c>. That split is the reason <see cref="RankCeremony"/> is a
    /// static gate and <c>RankUpOverlay</c> is a view — a rule only the Editor can run is not a
    /// guard on the rule it pins, and every fault this gate exists to stop is a fault of
    /// arithmetic rather than of drawing.
    /// </para>
    /// <para>
    /// <b>The cases worth reading are the four that celebrate nothing.</b> A false ceremony is
    /// the game telling somebody they did something they did not do, over the full screen, with
    /// a fanfare; a missing one is a shrug. So the fixture is weighted towards the moments the
    /// ordinal moves without anybody having earned anything: the first reading of a session, the
    /// reading taken before the content arrived, an account switched under it, and a ladder
    /// retuned under it.
    /// </para>
    /// </summary>
    public sealed class RankCeremonyTests
    {
        // ------------------------------------------------------------------- the fixture
        const string Chapter = "s01_one";

        static readonly string[] Levels = { "one_a", "one_b", "one_c", "one_d" };

        static CatalogIndex Catalog()
        {
            var builder = new CatalogIndexBuilder();

            builder.Add(new ManifestChapterDto
            {
                id = Chapter, order = 10, version = 1, mode = "siege", levels = Levels,
            }, 1);

            return builder.Build();
        }

        static RankRequirementDto Needs(string measure, int target)
            => new RankRequirementDto { measure = measure, target = target };

        static RankRungDto Rung(string id, int stars)
            => new RankRungDto { id = id, requires = new[] { Needs("stars", stars) } };

        /// <summary>
        /// Publishes a ladder. The ids are the shipped ones so the badges resolve, though
        /// nothing here draws one.
        /// </summary>
        static void Publish(params RankRungDto[] rungs)
        {
            var dto = new ProgressionDto
            {
                schemaVersion = ProgressionSchema.Version,
                xpToNext = new[] { 100 },
                tailXpToNext = 100,
                tailXpIncrement = 10,
                ranks = new RanksDto { rungs = rungs },
            };

            var problems = new List<string>();
            Assert.IsTrue(ProgressionTable.TryBuild(dto, out var table, problems),
                          string.Join("; ", problems));

            ProgressionRules.Publish(table);
            RankLedger.Invalidate();
        }

        /// <summary>The shipped shape: three rungs at two, five and eight stars.</summary>
        static void PublishThree()
            => Publish(Rung("cinderling", 2), Rung("silverwatch", 5), Rung("goldbrand", 8));

        /// <summary>
        /// Drives the save directly, exactly as <c>RankLadderTests.Holding</c> does — and names
        /// the account, because whose save this is decides whether a rise is a promotion.
        /// </summary>
        static void Holding(string account, params int[] stars)
        {
            var dto = new SaveFileDto
            {
                levels = new LevelRecordDto[stars.Length],
                cloud = new CloudStateDto { userId = account, deviceId = "test-device" },
            };

            for (int i = 0; i < stars.Length; i++)
                dto.levels[i] = new LevelRecordDto
                {
                    levelId = Levels[i],
                    stars = stars[i],
                    bestMoves = 10,
                    clears = 1,
                };

            CloudState.LoadFrom(dto);
            PlayerProgress.LoadFrom(dto);
            RankLedger.Invalidate();
        }

        LevelCatalog _catalogBefore;

        [SetUp]
        public void Start()
        {
            _catalogBefore = GameContent.Catalog;
            GameContent.Publish(LevelCatalog.FromLoaded(Catalog(), System.Array.Empty<ChapterBody>()));

            ProgressionRules.Reset();
            RankLedger.Reset();
            RankCeremony.Reset();
            Holding(string.Empty);
        }

        [TearDown]
        public void Finish()
        {
            ProgressionRules.Reset();
            RankLedger.Reset();
            RankCeremony.Reset();
            CloudState.LoadFrom(new SaveFileDto());
            PlayerProgress.LoadFrom(new SaveFileDto());
            GameContent.Publish(_catalogBefore);
        }

        // -------------------------------------------------------------- celebrates nothing
        /// <summary>
        /// The first reading of a session, which every launch performs, and which every account
        /// above the first rung would otherwise be congratulated by.
        /// </summary>
        [Test]
        public void TheFirstReadingOfASessionOwesNothing()
        {
            PublishThree();
            Holding("keeper-a", 3, 3);              // six stars: Cinderling and Silverwatch

            RankCeremony.Sync();

            Assert.AreEqual(2, RankLedger.Ordinal, "the fixture should hold two rungs");
            Assert.AreEqual(0, RankCeremony.Owed,
                            "an account that already holds two ranks has not just earned them");
        }

        /// <summary>
        /// <b>The Boot-ordering case, and the one most likely to be broken by a later change.</b>
        /// <c>RankCeremony.Begin</c> runs from <c>Boot</c>, where the save has loaded and the
        /// content has not — so the first reading it can take is a nought that means "no ladder"
        /// rather than "no rank". If that nought were treated as a baseline, every rung the
        /// account already held would be celebrated the moment the splash published the
        /// content.
        /// </summary>
        [Test]
        public void ABaselineTakenBeforeTheContentArrivesOwesNothing()
        {
            Holding("keeper-a", 3, 3);
            RankCeremony.Sync();                    // Boot: a save, and no ladder at all

            Assert.AreEqual(0, RankLedger.Ordinal, "no ladder means no rank");

            PublishThree();                         // the splash, a moment later
            RankCeremony.Sync();

            Assert.AreEqual(2, RankLedger.Ordinal);
            Assert.AreEqual(0, RankCeremony.Owed,
                            "the ladder arriving is not two promotions");
        }

        /// <summary>
        /// An account switch is local, ordinary and reversible, so the ordinal walks back down
        /// and up again under a player who has earned nothing either way.
        /// </summary>
        [Test]
        public void SwitchingToAnotherAccountOwesNothing()
        {
            PublishThree();
            Holding("keeper-a", 1);                 // below the first rung
            RankCeremony.Sync();

            Holding("keeper-b", 3, 3, 3);           // somebody else's grove, three rungs up
            RankCeremony.Sync();

            Assert.AreEqual(3, RankLedger.Ordinal, "nine stars clears every rung of the fixture");
            Assert.AreEqual(0, RankCeremony.Owed,
                            "another account's ranks are not this player's promotions");
        }

        /// <summary>
        /// The ladder is content and retunes without a build (invariant 52a), so a push can move
        /// every ordinal under somebody who is standing still. A retune is not a promotion —
        /// and the cost of that rule is stated where it is paid: a retune that genuinely grants
        /// a rank passes in silence, which is the right way round.
        /// </summary>
        [Test]
        public void ARetunedLadderOwesNothing()
        {
            PublishThree();
            Holding("keeper-a", 3, 3);
            RankCeremony.Sync();

            Assert.AreEqual(2, RankLedger.Ordinal);

            // A rung inserted at the bottom: the same play now reads as three rungs held.
            Publish(Rung("emberling", 1), Rung("cinderling", 2),
                    Rung("silverwatch", 5), Rung("goldbrand", 8));
            RankCeremony.Sync();

            Assert.AreEqual(3, RankLedger.Ordinal, "the same stars, one rung further up");
            Assert.AreEqual(0, RankCeremony.Owed,
                            "a ladder that grew a rung underneath is not a promotion");
        }

        [Test]
        public void ABuildWithNoLadderOwesNothing()
        {
            Holding("keeper-a", 3, 3, 3);
            RankCeremony.Sync();
            RankCeremony.Sync();

            Assert.IsTrue(RankLedger.Ladder.IsEmpty);
            Assert.AreEqual(0, RankCeremony.Owed);
        }

        // ------------------------------------------------------------------ celebrates it
        [Test]
        public void ARungReachedAfterTheBaselineIsOwed()
        {
            PublishThree();
            Holding("keeper-a", 1);                 // one star: below Cinderling
            RankCeremony.Sync();

            Assert.AreEqual(0, RankLedger.Ordinal);
            Assert.AreEqual(0, RankCeremony.Owed);

            Holding("keeper-a", 1, 1);              // the run that earns it
            RankCeremony.Sync();

            Assert.AreEqual(1, RankLedger.Ordinal);
            Assert.AreEqual(1, RankCeremony.Owed, "Cinderling was just reached");
        }

        /// <summary>
        /// Two rungs crossed at once — a fortnight of another device's play landing in one
        /// merge. Both are owed, because showing the top one and swallowing the other would mean
        /// a badge the player never saw arrive.
        /// </summary>
        [Test]
        public void TwoRungsCrossedAtOnceAreBothOwed()
        {
            PublishThree();
            Holding("keeper-a", 1);
            RankCeremony.Sync();

            Holding("keeper-a", 3, 3);              // straight past Cinderling to Silverwatch
            RankCeremony.Sync();

            Assert.AreEqual(2, RankLedger.Ordinal);
            Assert.AreEqual(2, RankCeremony.Owed, "both rungs, in ladder order");
        }

        /// <summary>
        /// The baseline moves with the reading, so asking twice cannot queue the same rung
        /// twice. <see cref="RankCeremony.Sync"/> is called from three cues and from every run
        /// ending, so this is the ordinary case rather than a corner.
        /// </summary>
        [Test]
        public void ARungIsOwedOnlyOnceHoweverOftenItIsAskedFor()
        {
            PublishThree();
            Holding("keeper-a", 1);
            RankCeremony.Sync();

            Holding("keeper-a", 1, 1);
            RankCeremony.Sync();
            RankCeremony.Sync();
            RankCeremony.Sync();

            Assert.AreEqual(1, RankCeremony.Owed);
        }

        // --------------------------------------------------------------------- the toast
        /// <summary>
        /// <c>RankBadge</c>'s toast gives way to the ceremony, and this is the predicate that
        /// decides. It is asked from inside <c>RankLedger.Promoted</c>, which fires from the
        /// read <see cref="RankCeremony.Sync"/> itself performs — so it is deliberately a
        /// question about the <em>baseline</em> rather than about the queue, which at that
        /// moment has not been written yet.
        /// </summary>
        [Test]
        public void TheCeremonySpeaksForARungItIsAboutToShow()
        {
            PublishThree();
            Holding("keeper-a", 1);
            RankCeremony.Sync();

            var ladder = RankLedger.Ladder;

            Assert.IsTrue(RankCeremony.Speaks(ladder.At(1)),
                          "Cinderling is above the baseline, so the ceremony has it");

            Holding("keeper-a", 1, 1);
            RankCeremony.Sync();

            Assert.IsFalse(RankCeremony.Speaks(ladder.At(1)),
                           "once it has been accounted for, the toast is free to say it");
            Assert.IsTrue(RankCeremony.Speaks(ladder.At(2)), "the next one is still the ceremony's");
        }

        [Test]
        public void TheCeremonyStaysSilentBeforeItHasABaseline()
        {
            PublishThree();
            Holding("keeper-a", 1);

            Assert.IsFalse(RankCeremony.Speaks(RankLedger.Ladder.At(1)),
                           "with no baseline nothing is owed, so the toast is the only voice");
        }

        [Test]
        public void TheCeremonySaysNothingAboutAnotherAccountsRung()
        {
            PublishThree();
            Holding("keeper-a", 1);
            RankCeremony.Sync();

            var rung = RankLedger.Ladder.At(1);
            Assert.IsTrue(RankCeremony.Speaks(rung));

            Holding("keeper-b", 1);
            Assert.IsFalse(RankCeremony.Speaks(rung),
                           "the baseline describes a save that is no longer underneath");
        }
    }
}
