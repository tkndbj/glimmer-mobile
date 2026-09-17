using System;
using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The client half of what the Infinite lane pays.
    ///
    /// <para>
    /// <b>This is the only rule in the game that turns a save into XP with no star behind it</b>
    /// (invariant 9's one exception — see <see cref="EndlessRewardTable"/>), and it exists twice:
    /// here, so a device can draw its own keeper level offline, and as <c>endlessWaves</c> /
    /// <c>endlessXp</c> in <c>functions/src/grove.ts</c>, so a published card carries the same
    /// one. <b>A drift between them is completely silent.</b> Nothing throws and nothing is
    /// refused; <c>buildCard</c> simply <em>drops</em> whatever the lower level gated (invariant
    /// 19a), so the players who play the lane most are the ones whose profiles quietly lose
    /// turrets. So both sides run <c>firebase/shared/grove-vectors.json</c>, and
    /// <c>firebase/functions/test/grove.mjs</c> is the other half. Invariant 9a, for the lane.
    /// </para>
    /// <para>
    /// <b>What is under contract:</b> how a save's rows become a wave count — including both
    /// ceilings, the best-floors-the-tally rule and the row cap — and what that count is paid.
    /// The <em>banking</em> below is not shared, because only a device ever adds to the tally;
    /// it is pinned here because it is where a run's waves could be double-counted or lost.
    /// </para>
    /// </summary>
    public sealed class EndlessRewardTests
    {
        // ------------------------------------------------------------- the file
        //
        // Read through `TestJson` rather than `JsonUtility`, and located without
        // `Application.dataPath` — both of those are engine `ECall`s, so a fixture using them is
        // reported as "needs the Editor" by `Tools/verify/tests.py` and walked past. **That is
        // invariant 29e, and it is the whole reason this is written the long way**: every other
        // shared-vector fixture here is Editor-only, this project has twice shipped days with one
        // of them red, and the rule below is one whose drift is silent even when it is running.

        /// <summary>The rate and ceiling a case is measured under.</summary>
        sealed class ConfigCase
        {
            public int XpPerWave;
            public int MaxWaves;

            public static ConfigCase From(Dictionary<string, object> map)
                => new ConfigCase
                {
                    XpPerWave = TestJson.Int(map, "xpPerWave"),
                    MaxWaves = TestJson.Int(map, "maxWaves"),
                };

            /// <summary>
            /// The published block this case describes, built through the shipped reader.
            ///
            /// <b>Through <see cref="EndlessRewardTable.Resolve"/> rather than around it</b>: a
            /// vector proved against a table assembled by the test proves nothing about the one a
            /// content push produces, which is <c>GroveBoardTests</c>'s rule about reading the
            /// shared catalog with the shipped mapper, said one file over.
            /// </summary>
            public EndlessRewardTable AsTable()
            {
                var problems = new List<string>();
                var table = EndlessRewardTable.Resolve(
                    new EndlessRewardDto { xpPerWave = XpPerWave, maxWaves = MaxWaves }, problems);

                Assert.IsNotNull(table);
                return table;
            }
        }

        sealed class EndlessCase
        {
            public string Name;
            public ConfigCase Config;
            public SaveFileDto Save;

            /// <summary>Lifetime waves both sides must read off the rows.</summary>
            public long Waves;

            /// <summary>XP both sides must pay for them.</summary>
            public long Xp;

            public static EndlessCase From(Dictionary<string, object> map)
            {
                var rows = TestJson.Children(map, "rows");
                var save = new SaveFileDto { endlessBest = new EndlessBestDto[rows.Count] };

                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i] == null) continue;

                    var row = TestJson.Object(rows[i]);
                    save.endlessBest[i] = new EndlessBestDto
                    {
                        level = TestJson.Str(row, "level", string.Empty),
                        wave = TestJson.Int(row, "wave"),
                        waves = TestJson.Int(row, "waves"),
                    };
                }

                return new EndlessCase
                {
                    Name = TestJson.Str(map, "name", "(unnamed)"),
                    Config = ConfigCase.From(TestJson.Child(map, "config")),
                    Save = save,
                    Waves = TestJson.Long(map, "waves"),
                    Xp = TestJson.Long(map, "xp"),
                };
            }
        }

        sealed class VectorFile
        {
            public ConfigCase Defaults;
            public List<EndlessCase> Cases;
        }

        static VectorFile Load()
        {
            var file = TestJson.ReadShared("grove-vectors.json");

            var cases = new List<EndlessCase>();
            foreach (object raw in TestJson.Children(file, "endlessCases"))
                cases.Add(EndlessCase.From(TestJson.Object(raw)));

            Assert.Greater(cases.Count, 0, "the vector file has no endless cases");

            return new VectorFile
            {
                Defaults = ConfigCase.From(TestJson.Child(file, "endlessDefaults")),
                Cases = cases,
            };
        }

        static readonly LevelId Watch = LevelId.Parse("s02_endlesswatch");
        static readonly LevelId Other = LevelId.Parse("s09_elsewhere");

        [SetUp]
        public void Reset() => EndlessLedger.LoadFrom(new SaveFileDto());

        [TearDown]
        public void Restore() => EndlessLedger.LoadFrom(new SaveFileDto());

        // ------------------------------------------------------- the shared contract
        [Test]
        public void EveryVectorCaseReadsTheSameWaveCountTheServerReads()
        {
            var file = Load();

            foreach (var c in file.Cases)
            {
                Assert.AreEqual(c.Waves, EndlessLedger.LifetimeWavesIn(c.Save),
                                $"lifetime waves disagree with the server: {c.Name}");
            }
        }

        [Test]
        public void EveryVectorCasePaysWhatTheServerPays()
        {
            var file = Load();

            foreach (var c in file.Cases)
            {
                long waves = EndlessLedger.LifetimeWavesIn(c.Save);

                Assert.AreEqual(c.Xp, c.Config.AsTable().XpFor(waves),
                                $"the payment disagrees with the server: {c.Name}");
            }
        }

        /// <summary>
        /// The figures a client uses when no <c>endless</c> block has been published.
        ///
        /// <b>They agree with the server's on purpose rather than failing closed.</b> A server one
        /// deploy behind would otherwise derive a lower keeper level than the device, and 19a
        /// drops what that level gated rather than clamping it — so the failure is a profile
        /// quietly missing turrets, with nothing said anywhere. Pinned on both sides.
        /// </summary>
        [Test]
        public void TheBuiltInFiguresAreTheOnesTheServerFallsBackTo()
        {
            var file = Load();

            Assert.AreEqual(file.Defaults.XpPerWave, EndlessLimits.DefaultXpPerWave);
            Assert.AreEqual(file.Defaults.MaxWaves, EndlessLimits.DefaultMaxWaves);

            Assert.AreEqual(EndlessLimits.DefaultXpPerWave, EndlessRewardTable.Default.XpPerWave);
            Assert.AreEqual(EndlessLimits.DefaultMaxWaves, EndlessRewardTable.Default.MaxWaves);
        }

        /// <summary>
        /// The two ceilings, which are the only reason paying for a figure the server cannot
        /// recompute is defensible at all (invariant 13's fourth clause).
        /// </summary>
        [Test]
        public void TheStructuralCeilingIsTheOneTheServerHolds()
        {
            // Mirrored by `HARD_MAX_LIFETIME_WAVES` in functions/src/grove.ts, and asserted there.
            Assert.AreEqual(1000000, EndlessLimits.HardMaxWaves);
            Assert.AreEqual(EndlessLimits.HardMaxWaves, EndlessLedger.MaxLifetimeWaves);

            // And the published one, which predates this feature and must not have moved.
            Assert.AreEqual(9999, EndlessLedger.MaxWave);
        }

        // ------------------------------------------------------------ the reader
        /// <summary>
        /// A ceiling of nought stops the payment and is <b>not</b> repaired into the built-in one.
        ///
        /// <para>
        /// <b>This case exists because the first version did repair it</b>, and the shared vectors
        /// caught the two halves disagreeing on their first run: the server pays nought and the
        /// client paid three and a half thousand. A guess at what an author meant is never worth a
        /// published keeper level the two sides compute differently (invariant 19a), and nought
        /// errs toward paying less, which is the safe direction for a figure that cannot be taken
        /// back once floored.
        /// </para>
        /// </summary>
        [Test]
        public void ACeilingOfNoughtStopsThePaymentRatherThanBeingRepaired()
        {
            var problems = new List<string>();
            var table = EndlessRewardTable.Resolve(
                new EndlessRewardDto { xpPerWave = 15, maxWaves = 0 }, problems);

            Assert.AreEqual(0, table.MaxWaves);
            Assert.IsFalse(table.Pays);
            Assert.AreEqual(0L, table.XpFor(100000));
            Assert.IsEmpty(problems, "a nought is an authored decision, not a mistake to report");
        }

        /// <summary>
        /// And the shape that would really be dangerous is unreachable: an <em>unwritten</em>
        /// ceiling inherits the built-in one, so a rate can never be published with no bound.
        /// </summary>
        [Test]
        public void ARateCanNeverBePublishedWithNoBoundAtAll()
        {
            var problems = new List<string>();
            var table = EndlessRewardTable.Resolve(
                new EndlessRewardDto { xpPerWave = 15, maxWaves = -1 }, problems);

            Assert.AreEqual(EndlessLimits.DefaultMaxWaves, table.MaxWaves);
            Assert.IsTrue(table.Pays);
            Assert.IsEmpty(problems);
        }

        [Test]
        public void AnAbsentBlockKeepsTheBuiltInFiguresAndIsNotAnError()
        {
            var problems = new List<string>();
            var table = EndlessRewardTable.Resolve(null, problems);

            Assert.AreEqual(EndlessRewardTable.Default.XpPerWave, table.XpPerWave);
            Assert.AreEqual(EndlessRewardTable.Default.MaxWaves, table.MaxWaves);
            Assert.IsEmpty(problems);
        }

        [Test]
        public void AnUnwrittenFieldInheritsRatherThanZeroing()
        {
            var problems = new List<string>();

            // -1 is what `JsonUtility` leaves on a field the file never wrote. Nought is a
            // decision (it withdraws the payment) and the two must not be the same fact.
            var table = EndlessRewardTable.Resolve(
                new EndlessRewardDto { xpPerWave = -1, maxWaves = 500 }, problems);

            Assert.AreEqual(EndlessLimits.DefaultXpPerWave, table.XpPerWave);
            Assert.AreEqual(500, table.MaxWaves);
            Assert.IsEmpty(problems);
        }

        [Test]
        public void ARateOfNoughtWithdrawsThePaymentAndIsNotAnError()
        {
            var problems = new List<string>();
            var table = EndlessRewardTable.Resolve(
                new EndlessRewardDto { xpPerWave = 0, maxWaves = 99990 }, problems);

            Assert.IsFalse(table.Pays);
            Assert.AreEqual(0L, table.XpFor(10000));
            Assert.IsEmpty(problems, "withdrawing the payment is a decision, not a mistake");
        }

        /// <summary>
        /// The product at full stretch, and the clamp that is what really keeps it in range.
        ///
        /// <para>
        /// <b>The ceiling is applied to the <em>count</em> and not to the product</b>, which is
        /// what lets an absurd input come back as an exact figure rather than as a wrapped one:
        /// <c>long.MaxValue</c> waves is clamped to the published ceiling first and only then
        /// multiplied. The two published maxima multiply to a thousand million — comfortably
        /// inside an <c>int</c>, so this is not a case about wrapping today, it is the case that
        /// fails the day somebody raises either bound without widening what carries it.
        /// </para>
        /// </summary>
        [Test]
        public void TheLargestPayableTotalIsExactAndClampedRatherThanMultipliedOut()
        {
            var problems = new List<string>();
            var table = EndlessRewardTable.Resolve(
                new EndlessRewardDto
                {
                    xpPerWave = EndlessLimits.MaxXpPerWave,
                    maxWaves = EndlessLimits.MaxMaxWaves,
                }, problems);

            long ceiling = (long)EndlessLimits.MaxMaxWaves * EndlessLimits.MaxXpPerWave;

            Assert.AreEqual(ceiling, table.MaxXp);
            Assert.AreEqual(ceiling, table.XpFor(long.MaxValue));
            Assert.AreEqual(ceiling, table.XpFor(EndlessLimits.MaxMaxWaves));

            // One wave short pays one rate less, which is the arithmetic actually being pinned:
            // a clamp applied to the product instead would answer the same number here.
            Assert.AreEqual(ceiling - EndlessLimits.MaxXpPerWave,
                            table.XpFor(EndlessLimits.MaxMaxWaves - 1));
        }

        // ------------------------------------------------------------- the banking
        /// <summary>
        /// <b>The tally is banked whether or not the run was a best</b>, which is the whole reason
        /// it is a second call rather than a line inside <c>Record</c>. Folding the two together
        /// is how every run after a good one would have paid nothing.
        /// </summary>
        [Test]
        public void ARunThatBeatNothingStillPays()
        {
            EndlessLedger.Record(Watch, 40);
            EndlessLedger.Bank(Watch, 40);

            Assert.AreEqual(40L, EndlessLedger.LifetimeWaves);

            // A far worse run. The best does not move and the tally does.
            Assert.IsFalse(EndlessLedger.Record(Watch, 6));
            EndlessLedger.Bank(Watch, 6);

            Assert.AreEqual(40, EndlessLedger.BestFor(Watch));
            Assert.AreEqual(46L, EndlessLedger.LifetimeWaves);
        }

        [Test]
        public void BankingAddsRatherThanAssigning()
        {
            EndlessLedger.Bank(Watch, 10);
            EndlessLedger.Bank(Watch, 10);
            EndlessLedger.Bank(Watch, 5);

            Assert.AreEqual(25L, EndlessLedger.LifetimeWaves);
        }

        [Test]
        public void BankingNothingChangesNothing()
        {
            EndlessLedger.Bank(Watch, 12);
            long before = EndlessLedger.LifetimeWaves;

            // A run that saw off no waves at all. It happened, and it is worth nought.
            EndlessLedger.Bank(Watch, 0);
            EndlessLedger.Bank(Watch, -5);

            Assert.AreEqual(before, EndlessLedger.LifetimeWaves);
        }

        [Test]
        public void TheTallySaturatesRatherThanWrapping()
        {
            EndlessLedger.Bank(Watch, EndlessLimits.HardMaxWaves);
            EndlessLedger.Bank(Watch, 5000);

            // A tally that wrapped would *fall*, and every join downstream of this assumes it
            // cannot — which is the one property that makes a stored count mergeable at all.
            Assert.AreEqual((long)EndlessLimits.HardMaxWaves, EndlessLedger.LifetimeWaves);
        }

        [Test]
        public void ABestFloorsATallyThatWasNeverCounted()
        {
            // Exactly a v29 file: a best, and no tally beside it.
            var save = new SaveFileDto
            {
                endlessBest = new[] { new EndlessBestDto { level = Watch.Value, wave = 37 } },
            };

            EndlessLedger.LoadFrom(save);

            Assert.AreEqual(37L, EndlessLedger.LifetimeWaves,
                            "a player who reached wave 37 before this shipped must not read as never having played");

            // And it is idempotent: banking nothing on top leaves it exactly where it was.
            EndlessLedger.Bank(Watch, 0);
            Assert.AreEqual(37L, EndlessLedger.LifetimeWaves);
        }

        // --------------------------------------------------------------- the merge
        /// <summary>
        /// <b>Per field rather than per row</b>, because the two numbers move independently: one
        /// device set a new best offline and another played four ordinary runs, and taking
        /// whichever row looked bigger would throw one of them away.
        /// </summary>
        [Test]
        public void TheMergeTakesTheLargerOfEachFieldSeparately()
        {
            var mine = new[] { new EndlessBestDto { level = Watch.Value, wave = 40, waves = 55 } };
            var theirs = new[] { new EndlessBestDto { level = Watch.Value, wave = 12, waves = 300 } };

            var joined = EndlessLedger.Join(mine, theirs);

            Assert.AreEqual(1, joined.Length);
            Assert.AreEqual(40, joined[0].wave, "the better best has to survive");
            Assert.AreEqual(300, joined[0].waves, "the larger tally has to survive");
        }

        [Test]
        public void TheMergeIsTheSameWhicheverOrderTheDevicesSyncIn()
        {
            var mine = new[] { new EndlessBestDto { level = Watch.Value, wave = 40, waves = 55 } };
            var theirs = new[]
            {
                new EndlessBestDto { level = Watch.Value, wave = 12, waves = 300 },
                new EndlessBestDto { level = Other.Value, wave = 9, waves = 9 },
            };

            var a = EndlessLedger.Join(mine, theirs);
            var b = EndlessLedger.Join(theirs, mine);

            Assert.AreEqual(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
            {
                Assert.AreEqual(a[i].level, b[i].level);
                Assert.AreEqual(a[i].wave, b[i].wave);
                Assert.AreEqual(a[i].waves, b[i].waves);
            }
        }

        [Test]
        public void AMergeNeverLowersEitherFloor()
        {
            var held = new[] { new EndlessBestDto { level = Watch.Value, wave = 40, waves = 300 } };
            var worse = new[] { new EndlessBestDto { level = Watch.Value, wave = 1, waves = 1 } };

            var joined = EndlessLedger.Join(held, worse);

            Assert.AreEqual(40, joined[0].wave);
            Assert.AreEqual(300, joined[0].waves);
        }

        /// <summary>
        /// A row naming a level this build has never heard of is carried through untouched —
        /// invariant 1 — so a tally set on a newer build survives a trip through an older one as
        /// far as this ledger is concerned.
        /// </summary>
        [Test]
        public void ARowNamingAnUnknownLevelSurvivesTheMerge()
        {
            var mine = new[] { new EndlessBestDto { level = Watch.Value, wave = 5, waves = 5 } };
            var newer = new[] { new EndlessBestDto { level = "s99_not_shipped_yet", wave = 3, waves = 80 } };

            var joined = EndlessLedger.Join(mine, newer);

            Assert.AreEqual(2, joined.Length);

            bool found = false;
            foreach (var row in joined)
                if (row.level == "s99_not_shipped_yet") { found = true; Assert.AreEqual(80, row.waves); }

            Assert.IsTrue(found, "an unknown level's row must not be dropped on the way through");
        }

        /// <summary>
        /// The round trip a sync actually makes. Anything the ledger holds has to survive being
        /// written, merged against nothing and read back, or a tally is lost on every launch.
        /// </summary>
        [Test]
        public void ATallySurvivesTheRoundTripThroughASaveFile()
        {
            EndlessLedger.Record(Watch, 31);
            EndlessLedger.Bank(Watch, 31);
            EndlessLedger.Bank(Watch, 9);

            var dto = new SaveFileDto();
            EndlessLedger.WriteInto(dto);

            dto.endlessBest = EndlessLedger.Join(dto.endlessBest, null);
            EndlessLedger.LoadFrom(dto);

            Assert.AreEqual(31, EndlessLedger.BestFor(Watch));
            Assert.AreEqual(40L, EndlessLedger.LifetimeWaves);
        }

        /// <summary>
        /// The bound the rules enforce. A save one row over its cap is an account that never
        /// saves again and no screen says so (invariant 12b), so the writer caps what it sends.
        /// </summary>
        [Test]
        public void NoMoreRowsAreWrittenThanTheRulesAllow()
        {
            for (int i = 0; i < EndlessLedger.MaxRows + 20; i++)
            {
                var level = LevelId.Parse($"s02_lane{i:D2}");
                EndlessLedger.Record(level, 3);
                EndlessLedger.Bank(level, 3);
            }

            var dto = new SaveFileDto();
            EndlessLedger.WriteInto(dto);

            Assert.LessOrEqual(dto.endlessBest.Length, EndlessLedger.MaxRows);
        }
    }
}
