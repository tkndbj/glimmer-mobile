using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GlimmerGrove.Shuffle;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The Shuffle run grade (MODES.md 59o): the score, the floors, the stats, and the rarity the
    /// owner asked of the top three grades - "genuinely really hard ... users should barely see
    /// them, only if they got the best combination and amount of upgrades".
    ///
    /// <para>
    /// <b>The floors are calibrated here and nowhere else.</b> <see cref="TheGradeTableIsPrinted"/>
    /// deals thousands of seeded runs through the real deck (<see cref="ShuffleRun"/>, the shipped
    /// tuning) to three choosers - the first card of every hand, a random card, and an expert who
    /// takes whatever scores most and chases bonds - and prints each grade's share by how many
    /// hands a run saw. <see cref="TheTopGradesStayRare"/> then holds the shape that table was
    /// read for, so a new card, bond or deck table that inflates the scale goes red rather than
    /// quietly handing out S++.
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class ShuffleGradeTests
    {
        // ------------------------------------------------------------------ the table
        [Test]
        public void TheFloorsRiseFromNoughtAndNameEveryGrade()
        {
            var floors = ShuffleGrades.Floors;
            Assert.AreEqual((int)ShuffleGrades.Most + 1, floors.Length, "one floor per grade");
            Assert.AreEqual(0, floors[0], "D is every score");

            for (int g = 1; g < floors.Length; g++)
                Assert.Greater(floors[g], floors[g - 1], $"floor {g} does not rise");

            var bonds = ShuffleGrades.BondFloors;
            Assert.AreEqual(floors.Length, bonds.Length, "one combination floor per grade");
            for (int g = 1; g < bonds.Length; g++)
            {
                Assert.GreaterOrEqual(bonds[g], bonds[g - 1], $"combination floor {g} falls");
                Assert.LessOrEqual(bonds[g], floors[g], $"combination floor {g} above its points floor");
            }

            var ids = new HashSet<string>();
            foreach (ShuffleGrade grade in Enum.GetValues(typeof(ShuffleGrade)))
            {
                int g = (int)grade;
                Assert.IsTrue(ids.Add(ShuffleGrades.Id(grade)), $"two grades share the id {ShuffleGrades.Id(grade)}");
                Assert.AreEqual(grade, ShuffleGrades.Of(floors[g], bonds[g]), $"{grade}'s own floors grade elsewhere");
                if (grade == ShuffleGrade.D) continue;

                Assert.AreEqual(grade - 1, ShuffleGrades.Of(floors[g] - 1, int.MaxValue), $"one point under {grade}");
                if (bonds[g] > bonds[g - 1])
                    Assert.Less(ShuffleGrades.Of(int.MaxValue, bonds[g] - 1), grade, $"one bond point under {grade}");
            }
        }

        /// <summary>
        /// The combination floor is the whole of why S and up need the right cards: the entire deck
        /// held to its most, with no bond unlocked, is not an S - and it stays true however many
        /// cards a drop adds, because the floor counts bond points alone.
        /// </summary>
        [Test]
        public void NoAmountOfCardsAloneIsAnS()
        {
            long everyCard = ShuffleCards.All.Sum(card => (long)card.Most * ShuffleGrades.CardPoints(card.Tier));
            Assert.Less(ShuffleGrades.Of((int)Math.Min(everyCard, ShuffleGrades.MaxPoints), 0), ShuffleGrade.S,
                        $"every card at its most ({everyCard} points) is graded S without a single bond");

            long everyBond = ShuffleBonds.All.Sum(bond => (long)ShuffleGrades.BondPoints(bond));
            Assert.GreaterOrEqual(everyBond, ShuffleGrades.BondFloors[(int)ShuffleGrades.Most], "S++ is unreachable");
            Assert.AreEqual(ShuffleGrades.Most, ShuffleGrades.Of((int)(everyCard + everyBond), (int)everyBond),
                            "the whole deck and every bond is not S++");
        }

        /// <summary>The ordinals are stored and published, so they may never move.</summary>
        [Test]
        public void TheGradeOrdinalsArePermanent()
        {
            Assert.AreEqual(0, (int)ShuffleGrade.D);
            Assert.AreEqual(1, (int)ShuffleGrade.C);
            Assert.AreEqual(2, (int)ShuffleGrade.B);
            Assert.AreEqual(3, (int)ShuffleGrade.A);
            Assert.AreEqual(4, (int)ShuffleGrade.AA);
            Assert.AreEqual(5, (int)ShuffleGrade.S);
            Assert.AreEqual(6, (int)ShuffleGrade.SPlus);
            Assert.AreEqual(7, (int)ShuffleGrade.SPlusPlus);
            Assert.AreEqual(ShuffleGrade.SPlusPlus, ShuffleGrades.Sane(99), "a newer grade reads as the top");
            Assert.AreEqual(ShuffleGrade.D, ShuffleGrades.Sane(-3));
        }

        [Test]
        public void EveryCardAndBondTierIsScored()
        {
            foreach (var card in ShuffleCards.All)
                Assert.Greater(ShuffleGrades.CardPoints(card.Tier), 0, card.Id);

            foreach (var bond in ShuffleBonds.All)
                Assert.Greater(ShuffleGrades.BondPoints(bond), 0, bond.Id);

            // A bond is worth more than its parts were, which is what makes a combination count.
            foreach (var bond in ShuffleBonds.All.Where(b => b.IsSecret))
            {
                int parts = bond.Parts.Sum(id => ShuffleGrades.CardPoints(ShuffleCards.Find(id).Tier));
                Assert.GreaterOrEqual(ShuffleGrades.BondPoints(bond), parts, $"{bond.Id} pays less than its cards");
            }
        }

        /// <summary>59e asked of the score: the same cards, any order, the same score and stats.</summary>
        [Test]
        public void AScoreIsAFunctionOfTheCardsAndNotTheirOrder()
        {
            string[] ids = { "frostbite", "kindling", "heavy_bolts", "heavy_bolts", "titan_pact", "quick_barrels", "keen_eyes" };

            var forward = Build(ids);
            var backward = Build(ids.Reverse().ToArray());

            Assert.AreEqual(ShuffleGrades.Score(forward), ShuffleGrades.Score(backward));
            Assert.IsTrue(ShuffleRecord.Same(ShuffleRecord.Of(forward, 20), ShuffleRecord.Of(backward, 20)));

            // Frostbite and kindling are the frostfire secret: the bond is scored on top of the cards.
            int cards = ids.Sum(id => ShuffleGrades.CardPoints(ShuffleCards.Find(id).Tier));
            Assert.AreEqual(cards + ShuffleGrades.BondPoints(ShuffleBonds.Find("frostfire")), ShuffleGrades.Score(forward));
        }

        [Test]
        public void TheStatsAreTheFiguresTheBoardReads()
        {
            var plain = ShuffleRecord.Of(Build("long_rest"), 3);
            Assert.IsNotNull(plain);
            Assert.AreEqual(100, Value(plain, "damage"));
            Assert.AreEqual(100, Value(plain, "speed"));
            Assert.AreEqual(0, Value(plain, "crit"));
            Assert.AreEqual(100, Value(plain, "health"));
            Assert.AreEqual(1, Value(plain, "cards"));
            Assert.AreEqual(0, Value(plain, "bonds"));

            // Two heavy bolts (+15% each) under a titan pact (x2 damage, half the fire rate).
            var built = Build("heavy_bolts", "heavy_bolts", "titan_pact", "keen_eyes");
            var boosts = built.Boosts;
            var record = ShuffleRecord.Of(built, 9);
            Assert.AreEqual(260, Value(record, "damage"));
            Assert.AreEqual(boosts.EffectiveFirePercent, Value(record, "speed"));
            Assert.AreEqual(10, Value(record, "crit"));
            Assert.AreEqual(4, Value(record, "cards"));

            Assert.IsNull(ShuffleRecord.Of(new ShuffleBuild(1u), 9), "a build with no card banks no record");

            foreach (var stat in ShuffleStats.All)
            {
                Assert.IsTrue(ShuffleStats.WellFormed(stat.Id), stat.Id);
                Assert.IsNotNull(stat.Icon, stat.Id);
            }
        }

        [Test]
        public void AFigureIsWrittenTheWayAPlayerReadsIt()
        {
            Assert.AreEqual("1", ShuffleStats.Figure(ShuffleStatKind.Times, 100));
            Assert.AreEqual("2.5", ShuffleStats.Figure(ShuffleStatKind.Times, 250));
            Assert.AreEqual("2.5", ShuffleStats.Figure(ShuffleStatKind.Times, 259));
            Assert.AreEqual("9.9", ShuffleStats.Figure(ShuffleStatKind.Times, 999));
            Assert.AreEqual("12", ShuffleStats.Figure(ShuffleStatKind.Times, 1240));
            Assert.AreEqual("1,240", ShuffleStats.Figure(ShuffleStatKind.Times, 124000));
            Assert.AreEqual("35", ShuffleStats.Figure(ShuffleStatKind.Percent, 35));
            Assert.AreEqual("1,400", ShuffleStats.Figure(ShuffleStatKind.Count, 1400));
        }

        // ------------------------------------------------------------------ the record
        [Test]
        public void TheBestRunIsATotalOrderSoTheMergeIsAMax()
        {
            var rows = new[]
            {
                Record(3, 900, 20, ("damage", 400)),
                Record(3, 900, 20, ("damage", 410)),
                Record(3, 900, 21, ("damage", 100)),
                Record(3, 901, 12),
                Record(4, 1500, 2),
                Record(3, 900, 20, ("damage", 400), ("speed", 1)),
                Record(3, 900, 20, ("apex", 1)),
                null,
            };

            foreach (var a in rows)
                foreach (var b in rows)
                {
                    Assert.AreEqual(Math.Sign(ShuffleRecord.Compare(a, b)), -Math.Sign(ShuffleRecord.Compare(b, a)), $"{a} vs {b}");
                    Assert.IsTrue(ShuffleRecord.Same(ShuffleRecord.Better(a, b), ShuffleRecord.Better(b, a)), $"join of {a} and {b}");
                    Assert.IsTrue(ShuffleRecord.Same(ShuffleRecord.Better(a, a), a));

                    foreach (var c in rows)
                        if (ShuffleRecord.Compare(a, b) <= 0 && ShuffleRecord.Compare(b, c) <= 0)
                            Assert.LessOrEqual(ShuffleRecord.Compare(a, c), 0, $"{a} <= {b} <= {c}");
                }

            Assert.AreEqual(ShuffleGrade.AA, ShuffleRecord.Better(rows[3], rows[4]).Grade, "the grade decides first");
        }

        [Test]
        public void ARecordIsBoundedAndCanonicalWhereverItIsRead()
        {
            var wild = ShuffleRecord.Create(40, int.MaxValue, int.MaxValue, new[]
            {
                new ShuffleStatValue("speed", 300), new ShuffleStatValue("damage", -9),
                new ShuffleStatValue("speed", 200), new ShuffleStatValue("Bad Id", 5),
                new ShuffleStatValue("future_stat", int.MaxValue),
            });

            Assert.AreEqual(ShuffleGrades.Most, wild.Grade, "a grade this build does not know draws as its top");
            Assert.AreEqual(40, wild.Ordinal, "and is carried as written (11f)");
            Assert.AreEqual(ShuffleGrades.MaxOrdinal, ShuffleRecord.Create(900, 1, 1, null).Ordinal);
            Assert.AreEqual(ShuffleGrades.MaxPoints, wild.Points);
            Assert.AreEqual(ShuffleLedger.MaxWave, wild.Wave);
            CollectionAssert.AreEqual(new[] { "damage", "future_stat", "speed" }, wild.Stats.Select(s => s.Id).ToArray());
            Assert.AreEqual(0, Value(wild, "damage"));
            Assert.AreEqual(300, Value(wild, "speed"), "a duplicate keeps the larger");
            Assert.AreEqual(ShuffleStats.MaxValue, Value(wild, "future_stat"), "an unknown stat is carried, bounded");

            Assert.IsNull(ShuffleRecord.Create(5, 0, 9, null), "no points is no run");

            var many = Enumerable.Range(0, 40).Select(i => new ShuffleStatValue("s" + i.ToString("D2"), i));
            Assert.AreEqual(ShuffleStats.MaxRows, ShuffleRecord.Create(1, 10, 1, many).Stats.Count);

            // Through the file and the wire and back, unchanged.
            var run = ShuffleRecord.Of(Build("frostbite", "kindling", "phoenix"), 14);
            Assert.IsTrue(ShuffleRecord.Same(run, ShuffleRecord.FromDto(ShuffleRecord.ToDto(run))));
            Assert.IsTrue(ShuffleRecord.Same(run, ShuffleRecord.FromWire(ShuffleRecord.ToWire(run))));
            Assert.IsNull(ShuffleRecord.FromDto(ShuffleRecord.ToDto(null)));
            Assert.IsNull(ShuffleRecord.FromWire("nonsense"));
        }

        // ------------------------------------------------------------------ the shared contract
        /// <summary>
        /// The best-graded run is read off a save twice - here, to decide whether the card owes a
        /// publish, and by <c>shuffleTop</c> in functions/src/grove.ts, to build the card a stranger
        /// reads - and both run <c>shuffleTopCases</c> (Tools/make_shuffle_top_vectors.py). Read
        /// through the real wire path: the save mapper first, then the ledger's own walk.
        /// </summary>
        [Test]
        public void EveryVectorCaseReadsTheWayTheServerReads()
        {
            var file = TestJson.ReadShared("grove-vectors.json");
            var constants = TestJson.Child(file, "shuffleTopConstants");

            Assert.AreEqual(ShuffleLedger.MaxRows, TestJson.Long(constants, "maxRows"));
            Assert.AreEqual(Content.LevelId.MaxLength, TestJson.Long(constants, "maxLevelId"));
            Assert.AreEqual(ShuffleGrades.MaxOrdinal, TestJson.Long(constants, "maxGrade"));
            Assert.AreEqual(ShuffleGrades.MaxPoints, TestJson.Long(constants, "maxPoints"));
            Assert.AreEqual(ShuffleLedger.MaxWave, TestJson.Long(constants, "maxWave"));
            Assert.AreEqual(ShuffleStats.MaxRows, TestJson.Long(constants, "maxStats"));
            Assert.AreEqual(ShuffleStats.MaxValue, TestJson.Long(constants, "maxStat"));
            Assert.AreEqual(ShuffleStats.MaxIdLength, TestJson.Long(constants, "maxStatId"));

            var failures = new List<string>();
            int count = 0;

            foreach (object raw in TestJson.Children(file, "shuffleTopCases"))
            {
                var c = TestJson.Object(raw);
                count++;

                var save = Cloud.FirestoreSaveMapper.FromDocument(TestJson.Object(c["save"]));
                var read = ShuffleLedger.TopIn(save);
                var want = c["top"] == null ? null : ShuffleRecord.FromWire(c["top"]);

                if (!ShuffleRecord.Same(read, want))
                    failures.Add($"{TestJson.Str(c, "name")}: read {Show(read)}, want {Show(want)}");
            }

            Assert.Greater(count, 0, "the vector file has no shuffleTop cases");
            Assert.IsEmpty(failures, "the client reads a save's best run differently:\n  " + string.Join("\n  ", failures));
        }

        static string Show(ShuffleRecord record) => record == null ? "none" : record.Fingerprint();

        /// <summary>
        /// The ledger keeps the best run per level beside the best wave, each by its own join, and
        /// both survive the file and the wire. A worse run after a better one changes nothing.
        /// </summary>
        [Test]
        public void TheLedgerKeepsTheBestRunBesideTheBestWave()
        {
            var level = Content.LevelId.Parse("s13_shuffle");
            var save = new Persistence.SaveFileDto();
            ShuffleLedger.LoadFrom(save);

            int beaten = 0;
            Action count = () => beaten++;
            ShuffleLedger.Beaten += count;
            try
            {
                var good = ShuffleRecord.Of(Build("frostbite", "kindling", "phoenix"), 20);
                var worse = ShuffleRecord.Of(Build("long_rest"), 40);

                Assert.IsTrue(ShuffleLedger.Record(level, 20));
                Assert.IsTrue(ShuffleLedger.RecordRun(level, good), "a first run is a best");
                Assert.IsFalse(ShuffleLedger.RecordRun(level, good), "the same run twice");
                Assert.IsTrue(ShuffleLedger.Record(level, 40), "a further wave is a best");
                Assert.IsFalse(ShuffleLedger.RecordRun(level, worse), "a further but weaker run is not the best run");
                Assert.AreEqual(3, beaten, "a sync is asked for on each new best and nothing else");

                Assert.AreEqual(40, ShuffleLedger.BestFor(level));
                Assert.IsTrue(ShuffleRecord.Same(good, ShuffleLedger.TopFor(level)));
                Assert.IsTrue(ShuffleRecord.Same(good, ShuffleLedger.Top));

                // The file and the wire, both ways.
                ShuffleLedger.WriteInto(save);
                Assert.IsTrue(ShuffleRecord.Same(good, ShuffleLedger.TopIn(save)));

                var back = Cloud.FirestoreSaveMapper.FromDocument(Cloud.FirestoreSaveMapper.ToDocument(save));
                Assert.IsTrue(ShuffleRecord.Same(good, ShuffleLedger.TopIn(back)), "the run did not survive the wire");
                Assert.AreEqual(40, ShuffleLedger.BestIn(back));

                // Two devices: one went further, the other built better. The merge keeps both bests.
                var other = new[] { new Persistence.ShuffleBestDto { level = "s13_shuffle", wave = 55,
                                                                     top = ShuffleRecord.ToDto(worse) } };
                var joined = ShuffleLedger.Join(save.shuffleBest, other);
                Assert.AreEqual(55, joined[0].wave);
                Assert.IsTrue(ShuffleRecord.Same(good, ShuffleRecord.FromDto(joined[0].top)));
                Assert.IsTrue(ShuffleRecord.Same(good, ShuffleRecord.FromDto(ShuffleLedger.Join(other, save.shuffleBest)[0].top)));
            }
            finally
            {
                ShuffleLedger.Beaten -= count;
                ShuffleLedger.LoadFrom(new Persistence.SaveFileDto());
            }
        }

        /// <summary>A card carries the best run off the file the server holds, and owes a publish when it moves.</summary>
        [Test]
        public void TheCardCarriesTheBestRunAndAMovedRunIsAChange()
        {
            var good = ShuffleRecord.Of(Build("frostbite", "kindling", "phoenix"), 20);
            var save = new Persistence.SaveFileDto
            {
                shuffleBest = new[] { new Persistence.ShuffleBestDto { level = "s13_shuffle", wave = 20, top = ShuffleRecord.ToDto(good) } },
            };

            var card = Social.GroveCard.OfSave(save, "uid", 6, 1_700_000_000L);
            Assert.IsTrue(ShuffleRecord.Same(good, card.ShuffleTop));

            var plain = Social.GroveCard.OfSave(new Persistence.SaveFileDto
            {
                shuffleBest = new[] { new Persistence.ShuffleBestDto { level = "s13_shuffle", wave = 20 } },
            }, "uid", 6, 1_700_000_000L);
            Assert.IsNull(plain.ShuffleTop);
            Assert.AreNotEqual(plain.Fingerprint(), card.Fingerprint(), "a graded run is not a change a card sees");
            StringAssert.DoesNotContain("g:", plain.Fingerprint(), "a card with no graded run moved its fingerprint");
            StringAssert.Contains("g:", card.Fingerprint(), "a graded run is not in the card's fingerprint");
        }

        // ------------------------------------------------------------------ the words
        [Test]
        public void EveryGradeAndStatIsWordedInEveryLanguage()
        {
            var keys = new List<string> { "ui.shuffle.score", "ui.shuffle.new_best", "ui.shuffle.best",
                                          "ui.shuffle.run_wave", "ui.shuffle.times", "ui.shuffle.percent",
                                          "ui.shuffle.best_run", "ui.shuffle.no_grade" };
            foreach (ShuffleGrade grade in Enum.GetValues(typeof(ShuffleGrade)))
                keys.Add(ShuffleGrades.NameKey(grade));

            foreach (var stat in ShuffleStats.All) keys.Add(stat.NameKey);

            foreach (string language in ShippedStrings.Languages())
            {
                var table = ShippedStrings.Table(language);
                foreach (string key in keys)
                    Assert.IsTrue(table.ContainsKey(key) && table[key].Length > 0, $"{language} has no {key}");
            }
        }

        /// <summary>Every grade has a colour and a light, and only the top three are lit (44e).</summary>
        [Test]
        public void EveryGradeIsDrawnAndOnlyTheTopThreeShine()
        {
            foreach (ShuffleGrade grade in Enum.GetValues(typeof(ShuffleGrade)))
            {
                Assert.DoesNotThrow(() => ShuffleGradeLook.Ink(grade), $"{grade} has no colour");
                Assert.AreEqual(ShuffleGrades.IsTop(grade), ShuffleGradeLook.Light(grade) > 0f, $"{grade}'s light");
            }
        }

        /// <summary>
        /// Every grade's letter is on disk with its `.meta` and in the boot preload, so neither the
        /// victory panel nor a profile can ask for a picture that is not there (7b). The address is
        /// derived (`AssetManifest.ShuffleGrade`), which `artnames.py` cannot see, so this is the gate.
        /// </summary>
        [Test]
        public void EveryGradeHasItsPictureOnDisk()
        {
            var global = new HashSet<string>(AssetPipeline.AssetManifest.GlobalAssets("en").Select(r => r.Address));
            string art = System.IO.Path.Combine(TestJson.RepoRoot(), "Assets", "Game");

            foreach (ShuffleGrade grade in Enum.GetValues(typeof(ShuffleGrade)))
            {
                string address = AssetPipeline.AssetManifest.ArtRoot + AssetPipeline.AssetManifest.ShuffleGrade(grade);
                string file = System.IO.Path.Combine(art, address + ".png");

                Assert.IsTrue(System.IO.File.Exists(file), $"{grade} has no picture at {file}");
                Assert.IsTrue(System.IO.File.Exists(file + ".meta"), $"{grade}'s picture has no .meta");
                Assert.IsTrue(global.Contains(address), $"{grade}'s picture is not preloaded");
            }
        }

        [Test]
        public void TheReportFitsItsBlockForEveryCountOfStats()
        {
            var layout = Layout.ShuffleReportLayout.Default;
            for (int n = 0; n <= ShuffleStats.MaxRows; n++)
                Assert.IsTrue(layout.IsClear(n, out string fault), $"{n} stat(s): {fault}");

            Assert.LessOrEqual(layout.Width, VictoryFrame.PanelWidth - 100f, "the block is wider than the victory window's column");
        }

        // ------------------------------------------------------------------ the calibration
        /// <summary>Hands a run sees by the waves it saw off: one every two (59f).</summary>
        static readonly int[] HandsAt = { 5, 10, 15, 20, 30, 40, 50, 60, 80, 100, 120, 150 };

        /// <summary>
        /// Seeded runs per chooser. Enough that a share under a few per cent is read off dozens of
        /// runs, few enough that the fixture stays a few seconds.
        /// </summary>
        const int Runs = 600;

        /// <summary>What a run's build is graded after one hand.</summary>
        readonly struct Sample
        {
            public readonly int Score;
            public readonly ShuffleGrade Grade;

            public Sample(int score, ShuffleGrade grade)
            {
                Score = score;
                Grade = grade;
            }
        }

        /// <summary>
        /// The calibration the floors were read off. Prints, never fails: the shape it is read for
        /// is <see cref="TheTopGradesStayRare"/>'s.
        /// </summary>
        [Test]
        public void TheGradeTableIsPrinted()
        {
            var table = new StringBuilder();
            table.Append("Shuffle grades: share of runs at each grade, by hands seen (wave ~ 2x hands)\n");
            table.Append("floors " + string.Join(" ", ShuffleGrades.Floors)
                         + "; bond floors " + string.Join(" ", ShuffleGrades.BondFloors) + "\n");

            foreach (var chooser in Choosers)
            {
                table.Append($"\n{chooser.Name}\n  hands   p10    p50    p90    max  |");
                foreach (ShuffleGrade g in Enum.GetValues(typeof(ShuffleGrade))) table.Append($" {ShuffleGrades.Id(g),6}");
                table.Append('\n');

                var runs = Simulate(chooser.Pick, HandsAt[HandsAt.Length - 1]);

                foreach (int hands in HandsAt)
                {
                    var at = runs.Select(run => run[hands - 1]).OrderBy(s => s.Score).ToArray();
                    table.Append($"  {hands,5} {Pct(at, 10),6} {Pct(at, 50),6} {Pct(at, 90),6} {at[at.Length - 1].Score,6}  |");

                    foreach (ShuffleGrade g in Enum.GetValues(typeof(ShuffleGrade)))
                    {
                        double share = at.Count(s => s.Grade == g) * 100.0 / at.Length;
                        table.Append(share <= 0 ? "      -" : $" {share,5:0.0}%");
                    }

                    table.Append('\n');
                }
            }

            Console.WriteLine(table);
        }

        /// <summary>
        /// The shape the floors were read for (the printed table, 2026-10-15). A run is a number of
        /// hands - one every two waves - and the shipped lane lets a strong line outlast the whole
        /// deck, so every bound here is "not before hand N", asked of two choosers: one who takes
        /// the first card of every hand, and an expert who chases score and bonds with every pick
        /// and is the fastest climber this fixture knows.
        /// </summary>
        [Test]
        public void TheTopGradesStayRare()
        {
            var expert = Simulate(Choosers[2].Pick, 150);
            var first = Simulate(Choosers[0].Pick, 150);

            double Share(List<Sample[]> runs, int hands, ShuffleGrade atLeast)
                => runs.Count(run => run[hands - 1].Grade >= atLeast) * 100.0 / runs.Count;

            // The ladder climbs with a run that takes what it is dealt: C by wave 20, B by wave 40.
            Assert.Greater(Share(first, 10, ShuffleGrade.C), 50.0, "a run to wave 20 is not graded C");
            Assert.Greater(Share(first, 20, ShuffleGrade.B), 50.0, "a run to wave 40 is not graded B");
            Assert.Less(Share(first, 20, ShuffleGrade.AA), 1.0, "AA is ordinary by wave 40");

            // S and up are for deep runs that chose well. The expert, the fastest climber here:
            Assert.Less(Share(expert, 30, ShuffleGrade.S), 2.0, "S by wave 60");
            Assert.Less(Share(expert, 40, ShuffleGrade.SPlus), 5.0, "S+ by wave 80");
            Assert.Less(Share(expert, 50, ShuffleGrade.SPlusPlus), 5.0, "S++ by wave 100");

            // A run that never chose: S not before wave 120, S++ not before wave 160.
            Assert.Less(Share(first, 60, ShuffleGrade.S), 5.0, "S by wave 120 without choosing");
            Assert.Less(Share(first, 80, ShuffleGrade.SPlusPlus), 5.0, "S++ by wave 160 without choosing");

            // But each is reachable, or it is decoration (5d).
            Assert.Greater(Share(expert, 150, ShuffleGrade.SPlusPlus), 50.0, "S++ is out of reach");
        }

        sealed class Chooser
        {
            public string Name;
            public Func<IReadOnlyList<ShuffleCard>, ShuffleBuild, Func<int>, ShuffleCard> Pick;
        }

        static readonly Chooser[] Choosers =
        {
            new Chooser { Name = "first card of every hand", Pick = (hand, build, roll) => hand[0] },
            new Chooser { Name = "a random card", Pick = (hand, build, roll) => hand[roll() % hand.Count] },
            new Chooser { Name = "an expert chasing score and bonds", Pick = Expert },
        };

        /// <summary>
        /// The strongest scorer this fixture can write: the card whose take adds the most points
        /// now, plus a share of every bond it moves closer, tier breaking ties.
        /// </summary>
        static ShuffleCard Expert(IReadOnlyList<ShuffleCard> hand, ShuffleBuild build, Func<int> roll)
        {
            ShuffleCard best = null;
            double bestWorth = double.MinValue;

            foreach (var card in hand)
            {
                double worth = ShuffleGrades.CardPoints(card.Tier);

                foreach (var bond in ShuffleBonds.All)
                {
                    if (!bond.Names(card) || bond.Completes(build)) continue;
                    if (bond.IsSecret && build.CopiesOf(card.Id) > 0) continue;

                    int held = bond.Parts.Count(p => build.CopiesOf(p) >= bond.Needs);
                    int missing = bond.IsSecret ? bond.Parts.Length - held : bond.Needs - build.CopiesOf(card.Id);
                    if (missing <= 0) continue;

                    worth += ShuffleGrades.BondPoints(bond) / (double)missing;
                }

                worth += (int)card.Tier * .01;
                if (worth > bestWorth) { bestWorth = worth; best = card; }
            }

            return best;
        }

        /// <summary>Each run's score and grade after every hand, for <see cref="Runs"/> seeded runs.</summary>
        static List<Sample[]> Simulate(Func<IReadOnlyList<ShuffleCard>, ShuffleBuild, Func<int>, ShuffleCard> pick, int hands)
        {
            var runs = new List<Sample[]>(Runs);

            for (int r = 0; r < Runs; r++)
            {
                uint seed = unchecked((uint)r * 2654435761u + 7u);
                var run = new ShuffleRun(seed == 0u ? 1u : seed);
                var samples = new Sample[hands];

                // The chooser's own stream, apart from the run's, so choosing never moves a deal.
                uint state = seed ^ 0x9E3779B9u;
                Func<int> roll = () =>
                {
                    state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                    return (int)(state % 100u);
                };

                for (int h = 0; h < hands; h++)
                {
                    var hand = run.Deal();
                    if (hand.Count > 0) Assert.IsTrue(run.Take(pick(hand, run.Build, roll)), "a chooser picked off the table");

                    int score = ShuffleGrades.Score(run.Build, out _, out int bonds);
                    samples[h] = new Sample(score, ShuffleGrades.Of(score, bonds));
                }

                runs.Add(samples);
            }

            return runs;
        }

        static int Pct(Sample[] sorted, int p) => sorted[Math.Min(sorted.Length - 1, sorted.Length * p / 100)].Score;

        // ------------------------------------------------------------------ helpers
        internal static ShuffleBuild Build(params string[] ids)
        {
            var build = new ShuffleBuild(5u);
            foreach (var id in ids)
            {
                var card = ShuffleCards.Find(id);
                Assert.IsNotNull(card, id);
                Assert.IsTrue(build.Take(card), id);
            }

            return build;
        }

        static ShuffleRecord Record(int grade, int points, int wave, params (string id, int value)[] stats)
            => ShuffleRecord.Create(grade, points, wave, stats.Select(s => new ShuffleStatValue(s.id, s.value)));

        static int Value(ShuffleRecord record, string id)
        {
            Assert.IsTrue(record.TryGet(id, out int value), $"{record} carries no {id}");
            return value;
        }
    }
}
