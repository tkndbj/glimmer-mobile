using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Layout;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Tasks;
using GlimmerGrove.Wards;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The first steps: the table that reads them, the ledger that earns and hands them over, the
    /// record that survives a save and a merge, and the shipped block standing where the owner put
    /// it (2026-10-22: a turret on chapter one's second level, the welcome bonus's day on its third,
    /// two royal chests on chapter two's fifth).
    ///
    /// <para>
    /// Four properties carry the feature. <b>A step is earned only by a first clear noted on a
    /// build that knows it</b>, so a player who finished those boards before is handed nothing.
    /// <b>A turret is granted at the clear, not by its ceremony</b>, so a lost ceremony loses no
    /// turret. <b>A chest is taken before it pays and pays its currency as a claim</b> under a
    /// derived id the server re-rolls. <b>The record only grows</b>, so two devices join by union.
    /// </para>
    /// </summary>
    public sealed class FirstStepsTests
    {
        static readonly LevelId Turret = LevelId.Parse("s01_ironward");
        static readonly LevelId Welcome = LevelId.Parse("s01_stonewatch");
        static readonly LevelId Chests = LevelId.Parse("s03_blightfen");

        [SetUp]
        public void Start()
        {
            Publish(ShippedSteps());
            PlayerProgress.LoadFrom(new SaveFileDto());
            ProgressionStore.LoadFrom(new SaveFileDto());
            Wallet.LoadFrom(new SaveFileDto());
            WardLedger.LoadFrom(new SaveFileDto());
            WardLoadout.LoadFrom(new SaveFileDto());
            TaskLedger.Reset();
            TaskLedger.LoadFrom(new SaveFileDto());
            PlayerProgression.Invalidate();
        }

        [TearDown]
        public void Restore()
        {
            ProgressionRules.Reset();
            TaskLedger.Reset();
            WardLedger.LoadFrom(new SaveFileDto());
            WardLoadout.LoadFrom(new SaveFileDto());
            Wallet.LoadFrom(new SaveFileDto());
            PlayerProgression.Invalidate();
        }

        // ------------------------------------------------------------ the table
        [Test]
        public void TheBlockReadsAndEachKindCarriesOnlyItsOwnFields()
        {
            var table = Resolve(out var problems, Good());
            Assert.IsEmpty(problems, string.Join("; ", problems));
            Assert.AreEqual(3, table.Steps.Count);

            var turret = table.At(Turret);
            Assert.AreEqual(FirstStepKind.Turret, turret.Kind);
            Assert.AreEqual("siphon", turret.Ward.Id);
            Assert.AreEqual(WardLine.Colours.IndexOf('r'), turret.Colour);

            Assert.AreEqual(FirstStepKind.Welcome, table.At(Welcome).Kind);

            var chests = table.At(Chests);
            Assert.AreEqual(FirstStepKind.Chests, chests.Kind);
            Assert.AreEqual("royal", chests.Tier.Id);
            Assert.AreEqual(2, chests.Count);

            Assert.IsNull(table.At(LevelId.Parse("s01_firstwatch")), "a level with no step");
        }

        [Test]
        public void AnAbsentBlockIsTheFeatureOff()
        {
            var problems = new List<string>();
            Assert.AreSame(FirstStepTable.Empty, FirstStepTable.Resolve(null, WardCatalog.Default, TaskTable.Default, problems));
            Assert.AreSame(FirstStepTable.Empty, FirstStepTable.Resolve(new FirstStepsDto(), WardCatalog.Default, TaskTable.Default, problems));
            Assert.IsEmpty(problems, "absent is not an error");
        }

        [Test]
        public void AFaultRefusesTheWholeBlockAndSaysWhy()
        {
            string starter = WardCatalog.Default.Starter.Id;

            Refused("kind", Row("s01_ironward", "relic"));
            Refused("second time", Row("s01_ironward", "welcome"), Row("s01_ironward", "welcome"));
            Refused("level", Row("S01_ironward", "welcome"));
            Refused("chest tier", Turret_("s01_ironward", "siphon", "r", tier: "royal"));
            Refused("turret or a chest", new FirstStepDto { level = "s01_stonewatch", kind = "welcome", ward = "siphon" });
            Refused("turret or a seat", Chest_("s03_blightfen", "royal", 2, ward: "siphon"));
            Refused("starter", Turret_("s01_ironward", starter, "r"));
            Refused("roster", Turret_("s01_ironward", "nonesuch", "r"));
            Refused("seat", Turret_("s01_ironward", "siphon", "x"));
            Refused("tasks block", Chest_("s03_blightfen", "mythic", 2));
            Refused("outside", Chest_("s03_blightfen", "royal", 0));
            Refused("outside", Chest_("s03_blightfen", "royal", FirstStepTable.MaxChests + 1));

            var many = new FirstStepDto[FirstStepTable.MaxSteps + 1];
            for (int i = 0; i < many.Length; i++) many[i] = Row("l" + i, "welcome");
            Refused("at most", many);
        }

        // ------------------------------------------------------------ the shipped block
        [Test]
        public void TheShippedStepsStandWhereTheOwnerPutThemAndCanPay()
        {
            var steps = Resolve(out var problems, ShippedSteps().steps);
            Assert.IsEmpty(problems, string.Join("; ", problems));
            Assert.IsFalse(steps.IsEmpty, "progression.json carries no firstSteps block, or it was refused");

            var index = ShippedIndex();
            var first = index.FirstChapterIn(index.ModeOf(Turret));
            Assert.IsNotNull(first);
            var second = index.ChapterNeighbour(first.Id, +1);
            Assert.IsNotNull(second, "the siege ladder has a second chapter");

            Assert.AreEqual(Turret, index.LevelsOf(first.Id)[1], "the turret is chapter one's second level");
            Assert.AreEqual(Welcome, index.LevelsOf(first.Id)[2], "the welcome day is chapter one's third level");
            Assert.AreEqual(Chests, index.LevelsOf(second.Id)[4], "the chests are chapter two's fifth level (fifteenth overall)");

            var turret = steps.At(Turret);
            Assert.AreEqual(FirstStepKind.Turret, turret.Kind);
            Assert.AreEqual("siphon", turret.Ward.Id);
            Assert.AreEqual('r', WardLine.Colours[turret.Colour], "a red Siphon");

            Assert.AreEqual(FirstStepKind.Welcome, steps.At(Welcome).Kind);

            var chests = steps.At(Chests);
            Assert.AreEqual(FirstStepKind.Chests, chests.Kind);
            Assert.AreEqual("royal", chests.Tier.Id, "royal chests");
            Assert.AreEqual(2, chests.Count, "two of them");

            foreach (var step in steps.Steps)
                Assert.IsTrue(index.Contains(step.Level), $"{step.Level} is not a shipped level");
        }

        /// <summary>The owner, 2026-10-22: nobody loses a heart anywhere in the first chapter.</summary>
        [Test]
        public void TheWholeFirstChapterIsFreeToFail()
        {
            var hearts = HeartRuleTable.Resolve(new HeartsDto { graceLevels = ShippedGraceLevels() }, new List<string>());
            var index = ShippedIndex();
            var first = index.FirstChapterIn(index.ModeOf(Turret));

            Assert.GreaterOrEqual(hearts.GraceLevels, first.LevelIds.Count, "graceLevels is shorter than the first chapter");
            foreach (var level in first.LevelIds)
                Assert.IsTrue(HeartStake.IsOpening(index, level, hearts), $"{level} would cost a heart");

            var second = index.ChapterNeighbour(first.Id, +1);
            Assert.IsFalse(HeartStake.IsOpening(index, second.LevelIds[0], hearts),
                           "the second chapter is charged as it always was");
        }

        // ------------------------------------------------------------ the ledger
        [Test]
        public void AFirstClearHandsTheTurretOverOnItsSeatAndOwesItsCeremony()
        {
            var step = FirstStepsLedger.Table.At(Turret);
            var siphon = step.Ward;
            Assert.IsFalse(WardLedger.IsHeld(siphon, 'r'));

            FirstStepsLedger.NoteFirstClear(Turret);

            Assert.IsTrue(WardLedger.IsHeld(siphon, 'r'), "granted at the clear, not by the ceremony");
            Assert.IsFalse(WardLedger.IsHeld(siphon, 'g'), "one seat, as a purchase is one seat");
            Assert.IsTrue(FirstStepsLedger.IsEarned(step));
            Assert.IsTrue(FirstStepsLedger.TurretWaiting(step));
            Assert.AreSame(step, FirstStepsLedger.Owed, "a calm hub would raise it");

            FirstStepsLedger.MarkShown(step);
            Assert.IsFalse(FirstStepsLedger.TurretWaiting(step), "shown once");
            Assert.IsNull(FirstStepsLedger.Owed);

            FirstStepsLedger.NoteFirstClear(Turret);
            Assert.IsFalse(FirstStepsLedger.TurretWaiting(step), "a second note is nothing");
        }

        [Test]
        public void EquippingTheGiftStandsItOnItsSeat()
        {
            FirstStepsLedger.NoteFirstClear(Turret);

            Assert.IsTrue(WardLoadout.Choose('r', "siphon"));
            Assert.AreEqual("siphon", WardLoadout.IdFor('r'));
            Assert.AreEqual("siphon", WardLoadout.Line.At(WardLine.Colours.IndexOf('r')).Id, "the next board stands it");
        }

        [Test]
        public void ASeatAlreadyHeldIsKeptAndOwesNoCeremony()
        {
            var step = FirstStepsLedger.Table.At(Turret);
            Assert.IsTrue(WardLedger.Grant(step.Ward, step.Colour, "bought"));

            FirstStepsLedger.NoteFirstClear(Turret);

            Assert.IsTrue(FirstStepsLedger.IsEarned(step));
            Assert.IsFalse(FirstStepsLedger.TurretWaiting(step), "nothing new to celebrate");
            Assert.IsNull(FirstStepsLedger.Owed);
        }

        [Test]
        public void ALevelClearedBeforeTheStepsShippedEarnsNothing()
        {
            // A save holding every step's level cleared, with no step ever noted: an account that
            // finished these boards on an older build.
            var dto = new SaveFileDto
            {
                levels = new[]
                {
                    new LevelRecordDto { levelId = Turret.Value, stars = 3, clears = 1 },
                    new LevelRecordDto { levelId = Chests.Value, stars = 2, clears = 1 },
                },
            };
            PlayerProgress.LoadFrom(dto);
            TaskLedger.LoadFrom(dto);

            Assert.IsNull(FirstStepsLedger.Owed, "a returning player is not handed a pile of gifts");
            Assert.AreEqual(0, FirstStepsLedger.ChestsWaiting(FirstStepsLedger.Table.At(Chests)));
            Assert.IsFalse(WardLedger.IsHeld(FirstStepsLedger.Table.At(Turret).Ward, 'r'));
        }

        [Test]
        public void AWelcomeStepStoresNothing()
        {
            FirstStepsLedger.NoteFirstClear(Welcome);

            var dto = new SaveFileDto();
            TaskLedger.WriteInto(dto);
            CollectionAssert.IsEmpty(dto.tasks.stepsEarned);
            CollectionAssert.IsEmpty(dto.tasks.stepsTaken);
        }

        [Test]
        public void EachChestOpensOnceAndPaysItsCurrencyAsAClaim()
        {
            var step = FirstStepsLedger.Table.At(Chests);
            Assert.AreEqual(0, FirstStepsLedger.ChestsWaiting(step), "nothing before the clear");
            Assert.IsFalse(FirstStepsLedger.TryOpen(step, 0, out _), "an unearned chest does not open");

            FirstStepsLedger.NoteFirstClear(Chests);
            Assert.AreEqual(2, FirstStepsLedger.ChestsWaiting(step));
            Assert.AreEqual(0, FirstStepsLedger.NextChest(step));
            Assert.AreSame(step, FirstStepsLedger.Owed);

            var expected = FirstStepsLedger.Preview(step, 0);
            Assert.Greater(expected.Count, 0, "a royal chest pays something");

            Assert.IsTrue(FirstStepsLedger.TryOpen(step, 0, out var drops));
            Assert.AreEqual(Describe(expected), Describe(drops), "what was previewed is what was paid");
            Assert.IsTrue(FirstStepsLedger.IsOpened(step, 0));
            Assert.AreEqual(1, FirstStepsLedger.ChestsWaiting(step));
            Assert.AreEqual(1, FirstStepsLedger.NextChest(step));
            Assert.IsFalse(FirstStepsLedger.TryOpen(step, 0, out _), "twice is nothing");

            foreach (var drop in drops)
            {
                if (!drop.IsCurrency) continue;
                string currency = ChestDropKinds.CurrencyOf(drop.Kind);
                Assert.IsTrue(HoldsGrant(currency, GrantEntry.FirstStepId(Chests.Value, 0, currency)),
                              $"the chest's {currency} is a claim under its own id");
            }

            Assert.IsTrue(FirstStepsLedger.TryOpen(step, 1, out var second));
            Assert.AreNotEqual(Describe(drops) + "#0", Describe(second) + "#1");
            Assert.AreEqual(0, FirstStepsLedger.ChestsWaiting(step));
            Assert.IsNull(FirstStepsLedger.Owed, "both opened, nothing owed");
            Assert.IsFalse(FirstStepsLedger.TryOpen(step, 2, out _), "the step pays two");
        }

        [Test]
        public void TheRecordSurvivesASaveAndTwoDevicesJoinByUnion()
        {
            FirstStepsLedger.NoteFirstClear(Turret);
            FirstStepsLedger.NoteFirstClear(Chests);
            Assert.IsTrue(FirstStepsLedger.TryOpen(FirstStepsLedger.Table.At(Chests), 1, out _));

            var mine = new SaveFileDto();
            TaskLedger.WriteInto(mine);
            CollectionAssert.AreEqual(new[] { "s01_ironward", "s03_blightfen" }, mine.tasks.stepsEarned);
            CollectionAssert.AreEqual(new[] { "s03_blightfen:1" }, mine.tasks.stepsTaken);

            // Read back: the same answers.
            TaskLedger.Reset();
            TaskLedger.LoadFrom(mine);
            var chests = FirstStepsLedger.Table.At(Chests);
            Assert.IsTrue(FirstStepsLedger.IsOpened(chests, 1));
            Assert.AreEqual(0, FirstStepsLedger.NextChest(chests));

            // Another device opened chest 0 and showed the turret.
            var other = new TaskStateDto
            {
                stepsEarned = new[] { "s01_ironward", "s03_blightfen" },
                stepsTaken = new[] { "s01_ironward", "s03_blightfen:0" },
            };

            var joined = TaskLedger.Join(mine.tasks, other);
            CollectionAssert.AreEqual(new[] { "s01_ironward", "s03_blightfen" }, joined.stepsEarned);
            CollectionAssert.AreEqual(new[] { "s01_ironward", "s03_blightfen:0", "s03_blightfen:1" }, joined.stepsTaken);

            var again = TaskLedger.Join(joined, joined);
            CollectionAssert.AreEqual(joined.stepsTaken, again.stepsTaken, "idempotent");
            CollectionAssert.AreEqual(joined.stepsTaken, TaskLedger.Join(other, mine.tasks).stepsTaken, "commutative");
        }

        [Test]
        public void AMalformedTokenIsDroppedAndTheListsAreCapped()
        {
            Assert.IsTrue(FirstStepsLedger.IsTakenToken("s03_blightfen:3"));
            Assert.IsTrue(FirstStepsLedger.IsTakenToken("s01_ironward"));
            Assert.IsFalse(FirstStepsLedger.IsTakenToken("s03_blightfen:4"), "past the most a step pays");
            Assert.IsFalse(FirstStepsLedger.IsTakenToken("s03_blightfen:01"), "a second spelling");
            Assert.IsFalse(FirstStepsLedger.IsTakenToken("s03_blightfen:"));
            Assert.IsFalse(FirstStepsLedger.IsTakenToken("S03:0"));
            Assert.IsFalse(FirstStepsLedger.IsEarnedToken("s03_blightfen:0"), "an earned token is a bare level");
            Assert.IsFalse(FirstStepsLedger.IsEarnedToken(""));

            var many = new string[FirstStepsLedger.MaxTaken + 10];
            for (int i = 0; i < many.Length; i++) many[i] = $"l{i:000}";
            Assert.AreEqual(FirstStepsLedger.MaxTaken, FirstStepsLedger.JoinTaken(many, null).Length);
            Assert.AreEqual(FirstStepsLedger.MaxEarned, FirstStepsLedger.JoinEarned(many, null).Length);
            CollectionAssert.AreEqual(new[] { "a", "b" }, FirstStepsLedger.JoinEarned(new[] { "b", "Bad!", "a" }, null),
                                      "sorted, malformed dropped");
        }

        [Test]
        public void TheClaimIdAndTheSeedAreTheServersSpelling()
        {
            Assert.AreEqual("steps:s03_blightfen:1:credits", GrantEntry.FirstStepId("s03_blightfen", 1, Currency.Credits));
            Assert.AreEqual("steps", FirstStepsLedger.SeedTag, "steps.ts STEPS_SEED_TAG");
            Assert.AreEqual("s03_blightfen:1", FirstStepsLedger.Subject(Chests, 1), "steps.ts stepSubject");
            Assert.AreEqual(FirstStepTable.MaxSteps, FirstStepsLedger.MaxEarned);
        }

        // ------------------------------------------------------------ helpers
        /// <summary>
        /// The shipped file's <c>firstSteps</c> block, read with the test parser rather than
        /// <c>JsonUtility</c>, which is native and would make the fixture need the Editor.
        /// </summary>
        static FirstStepsDto ShippedSteps()
        {
            var map = ShippedProgression();
            Assert.IsTrue(map.ContainsKey("firstSteps"), "progression.json carries no firstSteps block");

            var rows = new List<FirstStepDto>();
            foreach (var child in TestJson.Children(TestJson.Object(map["firstSteps"]), "steps"))
            {
                var row = TestJson.Object(child);
                rows.Add(new FirstStepDto
                {
                    level = TestJson.Str(row, "level"),
                    kind = TestJson.Str(row, "kind"),
                    ward = TestJson.Str(row, "ward"),
                    colour = TestJson.Str(row, "colour"),
                    tier = TestJson.Str(row, "tier"),
                    count = TestJson.Int(row, "count"),
                });
            }
            return new FirstStepsDto { steps = rows.ToArray() };
        }

        static int ShippedGraceLevels() => TestJson.Int(TestJson.Object(ShippedProgression()["hearts"]), "graceLevels");

        static Dictionary<string, object> ShippedProgression()
            => TestJson.Object(TestJson.Parse(File.ReadAllText(ContentFile("progression.json"))));

        /// <summary>
        /// Installs a table holding these steps over the built-in roster and chest tiers (both
        /// carry the Siphon and the royal chest), as <c>WelcomeTests.Publish</c> does.
        /// </summary>
        static void Publish(FirstStepsDto steps)
        {
            var problems = new List<string>();
            var dto = new ProgressionDto
            {
                schemaVersion = ProgressionSchema.Version,
                maxLevel = 20,
                xpToNext = new[] { 100, 150 },
                tailXpToNext = 200,
                tailXpIncrement = 50,
                firstSteps = steps,
            };
            Assert.IsTrue(ProgressionTable.TryBuild(dto, out var table, problems), string.Join("; ", problems));
            Assert.IsFalse(table.FirstSteps.IsEmpty, string.Join("; ", problems));
            ProgressionRules.Publish(table);
        }

        /// <summary>The shipped manifest's index: every chapter the game would list, as the boot path builds it.</summary>
        static CatalogIndex ShippedIndex()
        {
            // Read with the test parser for ShippedSteps' reason; the builder drops a disabled
            // chapter itself, exactly as the boot path's index does.
            var manifest = TestJson.Object(TestJson.Parse(File.ReadAllText(ContentFile("manifest.json"))));

            var builder = new CatalogIndexBuilder();
            foreach (var child in TestJson.Children(manifest, "chapters"))
            {
                var row = TestJson.Object(child);
                var levels = new List<string>();
                foreach (var level in TestJson.Children(row, "levels")) levels.Add((string)level);

                builder.Add(new ManifestChapterDto
                {
                    id = TestJson.Str(row, "id"),
                    order = TestJson.Int(row, "order"),
                    version = TestJson.Int(row, "version", 1),
                    disabled = row.TryGetValue("disabled", out var off) && off is bool b && b,
                    minAppVersion = TestJson.Int(row, "minAppVersion"),
                    mode = TestJson.Str(row, "mode"),
                    track = TestJson.Str(row, "track"),
                    levels = levels.ToArray(),
                }, int.MaxValue);
            }
            return builder.Build();
        }

        static string ContentFile(string name)
            => Path.Combine(TestJson.RepoRoot(), "Assets", "StreamingAssets", "Content", name);

        static FirstStepDto Row(string level, string kind) => new FirstStepDto { level = level, kind = kind };

        static FirstStepDto Turret_(string level, string ward, string colour, string tier = null)
            => new FirstStepDto { level = level, kind = "turret", ward = ward, colour = colour, tier = tier };

        static FirstStepDto Chest_(string level, string tier, int count, string ward = null)
            => new FirstStepDto { level = level, kind = "chests", tier = tier, count = count, ward = ward };

        static FirstStepDto[] Good() => new[]
        {
            Turret_("s01_ironward", "siphon", "r"),
            Row("s01_stonewatch", "welcome"),
            Chest_("s03_blightfen", "royal", 2),
        };

        static FirstStepTable Resolve(out List<string> problems, params FirstStepDto[] rows)
        {
            problems = new List<string>();
            return FirstStepTable.Resolve(new FirstStepsDto { steps = rows }, WardCatalog.Default, TaskTable.Default, problems);
        }

        static void Refused(string why, params FirstStepDto[] rows)
        {
            var table = Resolve(out var problems, rows);
            Assert.AreSame(FirstStepTable.Empty, table, $"a block that should be refused ({why}) was read");
            Assert.IsTrue(problems.Exists(p => p.Contains(why)),
                          $"expected a problem naming '{why}', got: {string.Join("; ", problems)}");
        }

        static bool HoldsGrant(string currency, string id)
        {
            foreach (var entry in Wallet.Ledger(currency).PendingGrants)
                if (entry.Id == id) return true;
            return false;
        }

        static string Describe(IEnumerable<ChestDrop> drops)
        {
            var parts = new List<string>();
            foreach (var drop in drops) parts.Add(drop.ToString());
            return string.Join(",", parts);
        }
    }

    /// <summary>
    /// The first steps' ceremonies keep time (<see cref="GiftTempo"/>): every sequence in order, an
    /// impact exactly where its fall ends, and the way onward never further off than
    /// <see cref="GiftTempo.MostToKey"/>.
    /// </summary>
    public sealed class GiftTempoTests
    {
        [Test]
        public void TheTurretLandsWhereItsFallEndsAndTheKeyComesSoon()
        {
            Assert.AreEqual(GiftTempo.TurretDrop + GiftTempo.TurretFall, GiftTempo.TurretImpact, 1e-5f);
            Ordered(GiftTempo.TurretHush, GiftTempo.TurretDrop, GiftTempo.TurretImpact,
                    GiftTempo.TurretName, GiftTempo.TurretNote, GiftTempo.TurretKey);
            Assert.GreaterOrEqual(GiftTempo.TurretName, GiftTempo.TurretImpact + GiftTempo.TurretSettle * .5f,
                                  "the name waits for the squash to spring back");
            Assert.LessOrEqual(GiftTempo.TurretKey, GiftTempo.MostToKey);
        }

        [Test]
        public void ChestsLandOneAfterAnotherAndTheKeyComesSoon()
        {
            for (int count = 1; count <= FirstStepTable.MaxChests; count++)
            {
                for (int i = 1; i < count; i++)
                    Assert.Greater(GiftTempo.ChestImpact(i), GiftTempo.ChestImpact(i - 1), "two thumps, never one");

                Ordered(GiftTempo.ChestHush, GiftTempo.ChestDrop, GiftTempo.ChestImpact(0),
                        GiftTempo.ChestImpact(count - 1) + (count > 1 ? 0f : 0f), GiftTempo.ChestCaption(count),
                        GiftTempo.ChestKey(count));
                Assert.LessOrEqual(GiftTempo.ChestKey(count), GiftTempo.MostToKey + GiftTempo.ChestGap * 2f,
                                   $"{count} chest(s) keep the player waiting too long");
            }

            Assert.AreEqual(GiftTempo.ChestDrop + GiftTempo.ChestFall, GiftTempo.ChestImpact(0), 1e-5f);
            Assert.LessOrEqual(GiftTempo.ChestKey(2), GiftTempo.MostToKey, "the shipped pair");
        }

        [Test]
        public void TheStampLandsWhereItsFallEnds()
        {
            Assert.AreEqual(GiftTempo.StampDrop + GiftTempo.StampFall, GiftTempo.StampImpact, 1e-5f);
            Ordered(GiftTempo.StampDrop, GiftTempo.StampImpact, GiftTempo.StampLine, GiftTempo.StampKeys);
            Assert.LessOrEqual(GiftTempo.StampKeys, GiftTempo.MostToKey);
        }

        static void Ordered(params float[] beats)
        {
            for (int i = 1; i < beats.Length; i++)
                Assert.GreaterOrEqual(beats[i], beats[i - 1], $"beat {i} lands before beat {i - 1}");
        }
    }
}
