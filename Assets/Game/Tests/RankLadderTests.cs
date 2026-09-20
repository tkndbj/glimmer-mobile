using System.Collections.Generic;
using System.Linq;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Ranks;
using GlimmerGrove.Tasks;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The rank ladder: what a measure reads, how a rung is held, what the reader refuses, and
    /// the two properties the whole feature rests on.
    ///
    /// <para>
    /// <b>Not to be confused with <see cref="RankTests"/></b>, which is about the percentile
    /// band a map node wears (<c>Social.RankTier</c>) and predates this by a year. Two things
    /// in this game are called a rank: that one is a standing against other keepers, and this
    /// one is a badge earned against a ladder. The nav bar's tab reads BOARDS now for the same
    /// reason.
    /// </para>
    ///
    /// <para>
    /// Those two are pinned hardest. <b>A rank can never fall</b> (invariant 52) — every measure
    /// is monotone, so more play can only ever move somebody up, and a rank being <em>derived</em>
    /// rather than stored is only sound because of it. And <b>the ladder is consecutive</b>: the
    /// held rung is the top of an unbroken run from the bottom, so an authoring slip cannot hand
    /// out a badge over unmet lines whatever content says.
    /// </para>
    /// <para>
    /// Everything here runs offline: the ladder is built from DTOs by hand, so nothing needs
    /// <c>JsonUtility</c>, a content file or <c>Application.dataPath</c> — invariant 29e's rule,
    /// because a fixture only the Editor can run is not a guard on the rule it pins.
    /// </para>
    /// </summary>
    public sealed class RankLadderTests
    {
        // ---------------------------------------------------------------- the fixture
        /// <summary>
        /// Two laddered chapters and an Infinite lane, which is the shape the shipped catalog
        /// has and the one every scoped measure needs in order to mean anything.
        /// </summary>
        static CatalogIndex Catalog()
        {
            var builder = new CatalogIndexBuilder();

            builder.Add(new ManifestChapterDto
            {
                id = "s01_one", order = 10, version = 1, mode = "siege",
                levels = new[] { "one_a", "one_b", "one_c" },
            }, 1);

            builder.Add(new ManifestChapterDto
            {
                id = "s03_two", order = 20, version = 1, mode = "siege",
                levels = new[] { "two_a", "two_b" },
            }, 1);

            builder.Add(new ManifestChapterDto
            {
                id = "s02_endless", order = 30, version = 1, mode = "siege", track = "infinite",
                levels = new[] { "endless_a" },
            }, 1);

            return builder.Build();
        }

        static RanksDto Ladder(params RankRungDto[] rungs) => new RanksDto { rungs = rungs };

        static RankRungDto Rung(string id, params RankRequirementDto[] requires)
            => new RankRungDto { id = id, requires = requires };

        static RankRequirementDto Needs(string measure, int target, string scope = null)
            => new RankRequirementDto { measure = measure, target = target, scope = scope };

        /// <summary>Drives the save directly, exactly as <c>ChapterGateTests.Holding</c> does.</summary>
        static void Holding(params (string level, int stars)[] records)
        {
            var dto = new SaveFileDto { levels = new LevelRecordDto[records.Length] };

            for (int i = 0; i < records.Length; i++)
                dto.levels[i] = new LevelRecordDto
                {
                    levelId = records[i].level,
                    stars = records[i].stars,
                    bestMoves = 10,
                    clears = 1,
                };

            PlayerProgress.LoadFrom(dto);
        }

        [SetUp]
        public void Start()
        {
            ProgressionRules.Reset();
            PlayerProgress.LoadFrom(new SaveFileDto());
            TaskLedger.Reset();
            TaskLedger.LoadFrom(new SaveFileDto());
            RankLedger.Reset();
        }

        [TearDown]
        public void Finish()
        {
            ProgressionRules.Reset();
            PlayerProgress.LoadFrom(new SaveFileDto());
            TaskLedger.Reset();
            RankLedger.Reset();
        }

        // ---------------------------------------------------------------- the measures
        [Test]
        public void EveryMeasureIsKnownToTheGate()
        {
            // The offline gate carries its own copy of this list (`content.py`'s RANK_DERIVED
            // and RANK_MEASURES), because it has no C# to ask. A measure added here and not
            // there is a rung the gate waves through — which is the one direction that matters,
            // since the gate is what proves a shipped ladder can be climbed at all.
            var expected = new[]
            {
                "levels_cleared", "stars", "three_stars", "keeper_level", "best_wave",
                "runs", "wins", "matches", "raiders", "bosses", "charms", "cogs", "bombs",
                "utilities", "waves", "streak",
            };

            CollectionAssert.AreEquivalent(expected, RankMeasures.All(),
                "the measure registry moved; Tools/verify/content.py carries a copy of this " +
                "list and has to move with it");

            foreach (string id in expected)
                Assert.IsFalse(RankMeasures.Parse(id).IsNone, $"'{id}' should resolve");
        }

        [Test]
        public void TheDerivedReadingsShadowTheirGoals()
        {
            // `stars` and `three_stars` exist in both registries, and the derived reading is
            // deliberately the one a rung gets: it is retroactive, cannot be inflated by
            // replaying an easy glade, and is the figure every other screen already prints.
            Assert.AreEqual(RankMeasureKind.Stars, RankMeasures.Parse("stars").Kind);
            Assert.AreEqual(RankMeasureKind.ThreeStars, RankMeasures.Parse("three_stars").Kind);

            Assert.AreEqual(RankMeasureKind.Lifetime, RankMeasures.Parse("runs").Kind);
            Assert.AreEqual(TaskGoal.Runs, RankMeasures.Parse("runs").Goal);
        }

        [Test]
        public void AnUnknownMeasureResolvesToNothing()
            => Assert.IsTrue(RankMeasures.Parse("flumph").IsNone);

        [Test]
        public void AScopedMeasureCountsOnlyItsOwnChapter()
        {
            var index = Catalog();
            Holding(("one_a", 3), ("one_b", 1), ("two_a", 2));

            var cleared = RankMeasures.Parse("levels_cleared");
            Assert.AreEqual(2L, RankMeasures.Read(cleared, "s01_one", index));
            Assert.AreEqual(1L, RankMeasures.Read(cleared, "s03_two", index));
            Assert.AreEqual(3L, RankMeasures.Read(cleared, string.Empty, index),
                            "unscoped is the whole account");

            var stars = RankMeasures.Parse("stars");
            Assert.AreEqual(4L, RankMeasures.Read(stars, "s01_one", index));
            Assert.AreEqual(6L, RankMeasures.Read(stars, string.Empty, index));

            var three = RankMeasures.Parse("three_stars");
            Assert.AreEqual(1L, RankMeasures.Read(three, "s01_one", index));
            Assert.AreEqual(0L, RankMeasures.Read(three, "s03_two", index));
        }

        /// <summary>
        /// An Infinite lane's level is in the flat catalog like any other, which is what
        /// <c>ContentValidation.ValidateRanks</c> leans on when it checks that a
        /// <c>best_wave</c> scope names something that ships.
        ///
        /// Pinned because the index keeps <em>three</em> orderings and only one of them is flat
        /// — a lane cannot chain into another lane — so "is this level in the catalog" and "is
        /// this level next" are different questions with different answers.
        /// </summary>
        [Test]
        public void AnInfiniteLanesLevelIsStillInTheCatalog()
        {
            var index = Catalog();

            Assert.IsTrue(index.Contains(LevelId.Parse("endless_a")));
            Assert.IsTrue(index.ContainsChapter(ChapterId.Parse("s02_endless")));
        }

        [Test]
        public void AScopeNothingNamesReadsAsNothingRatherThanEverything()
        {
            var index = Catalog();
            Holding(("one_a", 3), ("one_b", 3), ("two_a", 3));

            // The dangerous failure would be falling back to the unscoped reading, which turns
            // "clear three battles of a chapter that no longer ships" into a rung that is
            // already met. Both gates refuse such a ladder; this is what the runtime does if
            // one ever slips through.
            Assert.AreEqual(0L, RankMeasures.Read(RankMeasures.Parse("levels_cleared"),
                                                  "s99_gone", index));
        }

        // ---------------------------------------------------------------- the tally
        [Test]
        public void ALifetimeCountIsFlooredByWhatTheSaveAlreadyProves()
        {
            var index = Catalog();
            Holding(("one_a", 3), ("one_b", 2), ("two_a", 1));

            // Nothing has been counted — this is an account older than the feature — and three
            // cleared glades are three runs that happened and three that were won.
            Assert.AreEqual(0, LifetimeTally.Counted(TaskGoal.Runs), "nothing counted");
            Assert.AreEqual(3L, LifetimeTally.Count(TaskGoal.Runs), "three clears are three runs");
            Assert.AreEqual(3L, LifetimeTally.Count(TaskGoal.Wins));

            // A verb with nothing to prove it answers what it counted and no more.
            Assert.AreEqual(0L, LifetimeTally.Count(TaskGoal.Raiders));
        }

        [Test]
        public void ALifetimeCountTakesTheLargerOfWhatWasCountedAndWhatIsProved()
        {
            var index = Catalog();
            Holding(("one_a", 3));

            TaskLedger.Note(TaskGoal.Runs, 9);

            Assert.AreEqual(9L, LifetimeTally.Count(TaskGoal.Runs),
                            "nine played beats one proved");

            // And the floor still wins when it is the bigger of the two, which is the case an
            // account restored onto a fresh device lands in.
            Holding(("one_a", 3), ("one_b", 3), ("two_a", 3), ("two_b", 3), ("endless_a", 3),
                    ("one_c", 3), ("endless_a", 3));
            Assert.IsTrue(LifetimeTally.Count(TaskGoal.Runs) >= 6,
                          "the proved floor wins when it is the larger");
        }

        [Test]
        public void TheTallyMergesByTakingTheLargerOfEachGoal()
        {
            var mine = new[]
            {
                new TaskCountDto { goal = TaskGoals.Runs, count = 40 },
                new TaskCountDto { goal = TaskGoals.Raiders, count = 900 },
            };
            var other = new[]
            {
                new TaskCountDto { goal = TaskGoals.Runs, count = 12 },
                new TaskCountDto { goal = TaskGoals.Bosses, count = 7 },
            };

            var joined = LifetimeTally.Join(mine, other);
            var by = joined.ToDictionary(r => r.goal, r => r.count);

            Assert.AreEqual(40, by[TaskGoals.Runs], "the larger wins");
            Assert.AreEqual(900, by[TaskGoals.Raiders], "a row only one side has survives");
            Assert.AreEqual(7, by[TaskGoals.Bosses]);

            // Commutative and idempotent, which is what makes it a join rather than a decision
            // (invariant 11b) — a merge that depended on which device asked would converge on
            // different answers on two phones.
            var back = LifetimeTally.Join(other, mine).ToDictionary(r => r.goal, r => r.count);
            CollectionAssert.AreEquivalent(by, back);

            var again = LifetimeTally.Join(joined, joined).ToDictionary(r => r.goal, r => r.count);
            CollectionAssert.AreEquivalent(by, again);
        }

        [Test]
        public void TheTallyIsWrittenSortedSoADeltaCanWalkIt()
        {
            var joined = LifetimeTally.Join(
                new[] { new TaskCountDto { goal = TaskGoals.Waves, count = 3 } },
                new[] { new TaskCountDto { goal = TaskGoals.Bosses, count = 1 } });

            var goals = joined.Select(r => r.goal).ToArray();
            CollectionAssert.AreEqual(goals.OrderBy(g => g, System.StringComparer.Ordinal).ToArray(),
                                      goals,
                                      "an unsorted writer makes every launch look changed");
        }

        [Test]
        public void ARowNamingAGoalThisBuildCannotCountIsDropped()
        {
            var joined = LifetimeTally.Join(
                new[] { new TaskCountDto { goal = "flumph", count = 5 } },
                null);

            Assert.AreEqual(0, joined.Length,
                            "a newer build's verb is dropped rather than carried; it buys " +
                            "nothing, so nothing is confiscated");
        }

        [Test]
        public void ANoteIsCountedForEverEvenWhenNoSlateWantsIt()
        {
            // `TaskLedger.Note` declines a goal already at its slate's ceiling; for ever, it
            // still happened. The tee sits before that early-out precisely so this holds.
            for (int i = 0; i < 40; i++) TaskLedger.Note(TaskGoal.Bosses);

            Assert.AreEqual(40, LifetimeTally.Counted(TaskGoal.Bosses));
        }

        // ---------------------------------------------------------------- the ladder
        [Test]
        public void ARungIsHeldOnlyWhenEveryLineOfItIsMet()
        {
            var index = Catalog();
            var problems = new List<string>();
            var ladder = RankLadder.Resolve(Ladder(
                Rung("first", Needs("levels_cleared", 2, "s01_one"), Needs("stars", 5))),
                problems);

            CollectionAssert.IsEmpty(problems);

            Holding(("one_a", 3), ("one_b", 1));
            Assert.IsNull(ladder.Held(index), "two clears but only four stars");

            Holding(("one_a", 3), ("one_b", 2));
            Assert.AreEqual("first", ladder.Held(index)?.Id);
        }

        [Test]
        public void TheHeldRungIsTheTopOfAnUnbrokenRun()
        {
            var index = Catalog();
            var problems = new List<string>();

            // The middle rung asks for more than the top one, which is an authoring slip both
            // gates refuse. `Held` walks upward, so the top rung is unreachable until the
            // middle one is met and no badge is ever handed out over unmet lines.
            var ladder = RankLadder.Resolve(Ladder(
                Rung("a", Needs("stars", 1)),
                Rung("b", Needs("stars", 99)),
                Rung("c", Needs("stars", 2))),
                problems);

            Holding(("one_a", 3));
            Assert.AreEqual("a", ladder.Held(index)?.Id,
                            "c's own lines are met and it is not held, because b is not");
            Assert.AreEqual("b", ladder.Next(index)?.Id);
        }

        [Test]
        public void ARankNeverFalls()
        {
            var index = Catalog();
            var ladder = RankLadder.Resolve(Ladder(
                Rung("a", Needs("levels_cleared", 1, "s01_one")),
                Rung("b", Needs("stars", 6)),
                Rung("c", Needs("runs", 4))),
                new List<string>());

            Holding(("one_a", 3), ("one_b", 3));
            TaskLedger.Note(TaskGoal.Runs, 4);
            Assert.AreEqual(3, ladder.OrdinalHeld(index), "everything met");

            // The one thing a player can do that looks like going backwards: replay a glade and
            // score worse. Records are bests, so the ledger does not move — which is the whole
            // reason a derived badge is safe (invariant 52).
            Holding(("one_a", 3), ("one_b", 3));
            Assert.AreEqual(3, ladder.OrdinalHeld(index), "a worse replay changes nothing");

            // And more play only ever adds.
            Holding(("one_a", 3), ("one_b", 3), ("one_c", 3));
            TaskLedger.Note(TaskGoal.Runs, 12);
            Assert.AreEqual(3, ladder.OrdinalHeld(index));
        }

        [Test]
        public void ARungAsksForEverythingAndNotOneOfThem()
        {
            var index = Catalog();
            var ladder = RankLadder.Resolve(Ladder(
                Rung("a", Needs("stars", 3), Needs("runs", 50))),
                new List<string>());

            Holding(("one_a", 3));
            Assert.IsNull(ladder.Held(index), "the stars are there and the battles are not");
        }

        // ---------------------------------------------------------------- the reader
        [Test]
        public void AnAbsentBlockIsNotAnError()
        {
            var problems = new List<string>();
            Assert.IsTrue(RankLadder.Resolve(null, problems).IsEmpty);
            Assert.IsTrue(RankLadder.Resolve(new RanksDto(), problems).IsEmpty);
            CollectionAssert.IsEmpty(problems, "a game with no ranks is a game");
        }

        [Test]
        public void AMalformedRungTakesTheWholeLadderDown()
        {
            // Refused whole rather than degraded, and that is the point: a ladder missing one
            // line of one rung is a badge handed out for less than it asks for, which nobody
            // would ever notice.
            foreach (var bad in new[]
            {
                Ladder(Rung("Bad Id", Needs("stars", 1))),
                Ladder(Rung("a", Needs("stars", 1)), Rung("a", Needs("stars", 2))),
                Ladder(Rung("a")),
                Ladder(Rung("a", Needs("stars", 0))),
                Ladder(Rung("a", Needs("keeper_level", 3, "s01_one"))),
            })
            {
                var problems = new List<string>();
                Assert.IsTrue(RankLadder.Resolve(bad, problems).IsEmpty,
                              "a structural fault refuses the ladder");
                CollectionAssert.IsNotEmpty(problems, "and names itself");
            }
        }

        [Test]
        public void AScopeOnAMeasureThatTakesNoneIsRefusedRatherThanIgnored()
        {
            var problems = new List<string>();
            RankLadder.Resolve(Ladder(Rung("a", Needs("runs", 5, "s01_one"))), problems);

            Assert.IsTrue(problems.Any(p => p.Contains("scope")),
                          "a scope silently dropped would be a sentence the ladder does not mean");
        }

        [Test]
        public void ARungNamingAnUnknownMeasureIsDroppedRatherThanFatal()
        {
            var problems = new List<string>();
            var ladder = RankLadder.Resolve(Ladder(
                Rung("a", Needs("stars", 1)),
                Rung("newer", Needs("flumph", 1)),
                Rung("c", Needs("stars", 2))),
                problems);

            // A newer content pack reaching an older client. Dropping the whole ladder would
            // take the feature off that player's screen; dropping the rung can only make the
            // ladder easier, never harder.
            Assert.AreEqual(2, ladder.Count);
            CollectionAssert.AreEqual(new[] { "a", "c" }, ladder.Rungs.Select(r => r.Id).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 2 }, ladder.Rungs.Select(r => r.Ordinal).ToArray(),
                                      "the ordinals close up rather than leaving a hole");
        }

        [Test]
        public void ATargetAboveTheCounterCeilingIsRefused()
        {
            var problems = new List<string>();
            Assert.IsTrue(RankLadder.Resolve(
                Ladder(Rung("a", Needs("runs", LifetimeTally.Ceiling + 1))), problems).IsEmpty);
            CollectionAssert.IsNotEmpty(problems);
        }

        // ---------------------------------------------------------------- the art and copy
        [Test]
        public void ABadgeAndAStringAreDerivedFromTheRungsId()
        {
            var ladder = RankLadder.Resolve(Ladder(Rung("gemfire", Needs("stars", 1))),
                                            new List<string>());
            var rung = ladder.At(1);

            Assert.AreEqual("Ui/Rank/gemfire", rung.Icon);
            Assert.AreEqual("rank.gemfire.name", rung.NameKey);
            Assert.AreEqual("rank.gemfire.blurb", rung.BlurbKey);
        }

        /// <summary>
        /// Every rung's badge is in the global preload set, which is the one thing that stops a
        /// badge on disk being a white rectangle on the map (invariant 7b).
        ///
        /// <para>
        /// <b>A fixture rather than a content gate, and that was learned the hard way.</b> The
        /// Editor's validator asked this first, against <c>AssetManifest.GlobalAssets()</c> —
        /// which reads the <em>published</em> table, and nothing publishes the shipped one
        /// during validation, so it reported all seven badges undeclared on a file that was
        /// correct. Here the table is published deliberately, so the question is about the
        /// wiring rather than about whatever the Editor happened to be holding.
        /// </para>
        /// </summary>
        [Test]
        public void EveryRungsBadgeIsInTheGlobalPreloadSet()
        {
            var dto = new ProgressionDto
            {
                schemaVersion = ProgressionSchema.Version,
                xpToNext = new[] { 100 },
                tailXpToNext = 100,
                tailXpIncrement = 10,
                ranks = Ladder(Rung("cinderling", Needs("stars", 1)),
                               Rung("gemfire", Needs("stars", 2))),
            };

            var problems = new List<string>();
            Assert.IsTrue(ProgressionTable.TryBuild(dto, out var table, problems),
                          string.Join("; ", problems));
            Assert.AreEqual(2, table.Ranks.Count);

            ProgressionRules.Publish(table);

            var declared = new HashSet<string>();
            foreach (var request in AssetPipeline.AssetManifest.GlobalAssets())
                declared.Add(request.Address);

            foreach (var rung in table.Ranks.Rungs)
                Assert.IsTrue(declared.Contains(AssetPipeline.AssetManifest.ArtRoot + rung.Icon),
                              $"'{rung.Icon}' is not preloaded; the map would draw a white rectangle");
        }

        [Test]
        public void ASentenceKeyIsDerivedFromTheMeasure()
        {
            var cleared = RankMeasures.Parse("levels_cleared");
            Assert.AreEqual("rank.req.levels_cleared", RankMeasures.SentenceKey(cleared, false));
            Assert.AreEqual("rank.req.levels_cleared.in", RankMeasures.SentenceKey(cleared, true));
        }
    }
}
