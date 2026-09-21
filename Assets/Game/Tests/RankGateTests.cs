using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Ranks;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The rank ladder opens no earlier than the lane it ranks (invariant 52i).
    ///
    /// <para>
    /// <b>This fixture is half the point of the rule living in <c>GlimmerGrove.Authoring</c>.</b>
    /// The first cut of <see cref="RankGate"/> was written inside <c>ContentValidation</c>, where
    /// the suite cannot reach it — so it shipped compiled and never once executed, which is a
    /// gate that cannot fail and therefore is not a gate. Every branch below is one this file
    /// would otherwise be trusting.
    /// </para>
    /// <para>
    /// It says nothing about the <em>shipped</em> ladder, deliberately: a fixture that read
    /// <c>progression.json</c> would need <c>Application.dataPath</c> and would stop running
    /// offline (invariant 29e). The shipped figures are `check_ranks`' job, and it prints them.
    /// </para>
    /// </summary>
    public sealed class RankGateTests
    {
        // ---------------------------------------------------------------- the fixture
        /// <summary>
        /// Two laddered chapters and an Infinite lane behind a wall, which is the shipped shape.
        /// The wall is the whole point of this catalog, so it is a parameter.
        /// </summary>
        static CatalogIndex Catalog(int wall, bool infinite = true)
        {
            var builder = new CatalogIndexBuilder();

            builder.Add(new ManifestChapterDto
            {
                id = "s01_one", order = 10, version = 1, mode = "siege",
                levels = new[] { "one_a", "one_b" },
            }, 1);

            builder.Add(new ManifestChapterDto
            {
                id = "s02_lane", order = 20, version = 1, mode = "siege",
                track = infinite ? "infinite" : "main",
                minKeeperLevel = wall,
                levels = new[] { "lane_a" },
            }, 1);

            return builder.Build();
        }

        static RankLadder Ladder(params RankRungDto[] rungs)
        {
            var problems = new List<string>();
            var ladder = RankLadder.Resolve(new RanksDto { rungs = rungs }, problems);

            Assert.IsEmpty(problems, "the fixture's own ladder must parse cleanly");
            return ladder;
        }

        static RankRungDto Rung(string id, params RankRequirementDto[] requires)
            => new RankRungDto { id = id, requires = requires };

        static RankRequirementDto Needs(string measure, int target, string scope = null)
            => new RankRequirementDto { measure = measure, target = target, scope = scope };

        static RankLadder Opening(int at)
            => at <= 0
                   ? Ladder(Rung("first", Needs("stars", 4)), Rung("second", Needs("stars", 9)))
                   : Ladder(Rung("first", Needs("keeper_level", at), Needs("stars", 4)),
                            Rung("second", Needs("stars", 9)));

        sealed class Report
        {
            public readonly List<string> Errors = new List<string>();
            public readonly List<string> Warnings = new List<string>();
        }

        static Report Check(RankLadder ladder, CatalogIndex index)
        {
            var report = new Report();
            RankGate.Check(ladder, index, report.Errors, report.Warnings);
            return report;
        }

        // ---------------------------------------------------------------- the readings
        [Test]
        public void TheWallIsTheLowestOfEveryInfiniteLane()
        {
            Assert.AreEqual(10, RankGate.WallOf(Catalog(10)));
            Assert.AreEqual(0, RankGate.WallOf(Catalog(0)), "an ungated lane walls nothing");
            Assert.AreEqual(0, RankGate.WallOf(Catalog(10, infinite: false)),
                            "a laddered chapter's wall is not a wall on ranked play");
            Assert.AreEqual(0, RankGate.WallOf(null));
        }

        /// <summary>
        /// **The lowest wall, not the first chapter listed.** The rule is "the ladder may not open
        /// before ranked play can be reached", so a second lane opening sooner moves the anchor
        /// down — and reading the *first* entry would leave a window in which the cheaper lane is
        /// playable and the ladder is still shut.
        /// </summary>
        [Test]
        public void ASecondLaneOpeningSoonerMovesTheWallDown()
        {
            var builder = new CatalogIndexBuilder();

            builder.Add(new ManifestChapterDto
            {
                id = "s02_late", order = 10, version = 1, mode = "siege", track = "infinite",
                minKeeperLevel = 20, levels = new[] { "late_a" },
            }, 1);

            builder.Add(new ManifestChapterDto
            {
                id = "s04_early", order = 20, version = 1, mode = "siege", track = "infinite",
                minKeeperLevel = 6, levels = new[] { "early_a" },
            }, 1);

            Assert.AreEqual(6, RankGate.WallOf(builder.Build()));
        }

        [Test]
        public void TheLadderOpensAtWhatItsFirstRungAsksFor()
        {
            Assert.AreEqual(10, RankGate.OpensAt(Opening(10)));
            Assert.AreEqual(0, RankGate.OpensAt(Opening(0)));
            Assert.AreEqual(0, RankGate.OpensAt(RankLadder.Empty));
            Assert.AreEqual(0, RankGate.OpensAt(null));
        }

        /// <summary>
        /// A keeper line on the *second* rung gates the rungs above it and nothing below, so it is
        /// not an opening — which is the one way a ladder could look gated and not be.
        /// </summary>
        [Test]
        public void AKeeperLineAboveTheFirstRungOpensNothing()
        {
            var ladder = Ladder(Rung("first", Needs("stars", 4)),
                                Rung("second", Needs("keeper_level", 10), Needs("stars", 9)));

            Assert.AreEqual(0, RankGate.OpensAt(ladder));
            Assert.IsNotEmpty(Check(ladder, Catalog(10)).Errors);
        }

        // ---------------------------------------------------------------- the judgement
        [Test]
        public void ALadderOpeningWithItsLaneIsAccepted()
        {
            var report = Check(Opening(10), Catalog(10));

            Assert.IsEmpty(report.Errors);
            Assert.IsEmpty(report.Warnings);
        }

        /// <summary>The fault itself: Cinderling at keeper 7 against a lane that opens at 10.</summary>
        [Test]
        public void ALadderOpeningBeforeItsLaneIsRefused()
        {
            foreach (var opens in new[] { 0, 1, 9 })
            {
                var report = Check(Opening(opens), Catalog(10));

                Assert.AreEqual(1, report.Errors.Count, $"opening at {opens}");
                StringAssert.Contains("keeper level 10", report.Errors[0]);
                Assert.IsEmpty(report.Warnings);
            }
        }

        /// <summary>
        /// Above the wall is legal and said out loud: the ladder no longer opens *with* its lane,
        /// which somebody may well want and which nothing else would ever mention.
        /// </summary>
        [Test]
        public void ALadderOpeningAfterItsLaneIsAWarningRatherThanAnError()
        {
            var report = Check(Opening(14), Catalog(10));

            Assert.IsEmpty(report.Errors);
            Assert.AreEqual(1, report.Warnings.Count);
            StringAssert.Contains("no longer opens with the lane", report.Warnings[0]);
        }

        /// <summary>
        /// Nothing to anchor to is nothing to say. A build shipping no Infinite lane has no
        /// ranked play to gate, and refusing its ladder would be this gate inventing a rule.
        /// </summary>
        [Test]
        public void ACatalogWithNoRankedLaneJudgesNothing()
        {
            var report = Check(Opening(0), Catalog(10, infinite: false));

            Assert.IsEmpty(report.Errors);
            Assert.IsEmpty(report.Warnings);
        }

        [Test]
        public void AnEmptyLadderJudgesNothing()
        {
            var report = Check(RankLadder.Empty, Catalog(10));

            Assert.IsEmpty(report.Errors);
            Assert.IsEmpty(report.Warnings);
        }

        /// <summary>
        /// The gate must not be the thing that throws. It runs inside the build gate and inside
        /// `Sync Manifest`, and a null catalog is what both hold while content is still loading.
        /// </summary>
        [Test]
        public void ANullCatalogJudgesNothingRatherThanThrowing()
        {
            var report = Check(Opening(10), null);

            Assert.IsEmpty(report.Errors);
            Assert.IsEmpty(report.Warnings);
        }

        /// <summary>
        /// **The gate is exactly what closes the ladder**, which is the claim the whole design
        /// rests on: one line on the first rung, and the walk up from the bottom does the rest.
        /// Asserted against the runtime rather than reasoned about, because if this were ever
        /// untrue the gate would be a sentence on a page and nothing else.
        /// </summary>
        [Test]
        public void TheGateIsWhatShutsTheWholeLadder()
        {
            var ladder = Opening(10);
            var index = Catalog(10);

            var below = new Reading(index, keeperLevel: 9, stars: 99);
            var above = new Reading(index, keeperLevel: 10, stars: 99);

            Assert.IsNull(ladder.Held(below),
                          "every other line is met many times over, and the gate alone shuts it");
            Assert.AreEqual("second", ladder.Held(above)?.Id,
                            "and crossing the wall opens what the play already earned");
        }

        /// <summary>
        /// A source that answers whatever the case needs. Narrow on purpose: the interface is six
        /// readings and this fixture is about two of them.
        /// </summary>
        sealed class Reading : IRankSource
        {
            readonly long _stars;

            public Reading(CatalogIndex index, long keeperLevel, long stars)
            {
                KeeperLevel = keeperLevel;
                _stars = stars;
            }

            public long KeeperLevel { get; }
            public long Cleared(string scope) => _stars;
            public long Stars(string scope) => _stars;
            public long ThreeStars(string scope) => _stars;
            public long BestWave(string scope) => _stars;
            public long Lifetime(Tasks.TaskGoal goal) => _stars;
        }
    }
}
