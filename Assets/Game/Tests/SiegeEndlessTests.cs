using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The lane whose waves never stop: the boss schedule, the ramp, and the three things a run
    /// that can never be won still has to be.
    ///
    /// <para>
    /// <b>These are the only gate there is on the ramp.</b> An endless lane authors no waves, so
    /// there is nothing in a content file for a reader to be wrong about — what wave forty sends
    /// is a rule, and a rule with no test is a rule nothing checks.
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class SiegeEndlessTests
    {
        static LevelId Id(string raw)
        {
            Assert.IsTrue(LevelId.TryParse(raw, out var id, out string error), error);
            return id;
        }

        static SiegeLayout Layout(int goldWave = 20)
        {
            Assert.IsTrue(ProtoGrid.TryRead(new[]
            {
                "ryybgyyg",
                "bybgrgyy",
                "rbryyggr",
                "grgrgbbr",
                "yybgrrbg",
            }, 8, 5, SiegeLayout.Cells, out var grid, out string error), error);

            return new SiegeLayout(grid, "rgby", "rgby", null, null, 4,
                                   new SiegeEndless("rgby", 4, goldWave, .55f));
        }

        // ------------------------------------------------------------------ the schedule
        /// <summary>
        /// A boss every fourth wave to sixteen, then a <b>pair</b> every fifth — every unordered
        /// pair of the four, which is thirty waves before anything repeats.
        ///
        /// <b>Written out rather than derived here</b>, because a test that re-derived the rule
        /// would agree with a wrong rule. This is the schedule as it was asked for.
        /// </summary>
        [Test]
        public void TheBossScheduleIsFourSinglesThenEveryPair()
        {
            var wanted = new Dictionary<int, SiegeKind[]>
            {
                {  4, new[] { SiegeKind.Blightcaller } },
                {  8, new[] { SiegeKind.Boss } },
                { 12, new[] { SiegeKind.Warbringer } },
                { 16, new[] { SiegeKind.Overlord } },

                { 21, new[] { SiegeKind.Blightcaller, SiegeKind.Boss } },
                { 26, new[] { SiegeKind.Blightcaller, SiegeKind.Warbringer } },
                { 31, new[] { SiegeKind.Blightcaller, SiegeKind.Overlord } },
                { 36, new[] { SiegeKind.Boss, SiegeKind.Warbringer } },
                { 41, new[] { SiegeKind.Boss, SiegeKind.Overlord } },
                { 46, new[] { SiegeKind.Warbringer, SiegeKind.Overlord } },
            };

            var found = new List<SiegeKind>();

            for (int wave = 1; wave <= 50; wave++)
            {
                SiegeEndless.BossesAt(wave, found);

                if (!wanted.TryGetValue(wave, out var expected))
                {
                    Assert.IsEmpty(found, $"wave {wave} sends a boss and should not");
                    continue;
                }

                Assert.AreEqual(expected, found, $"wave {wave}");
            }

            // And it starts again rather than running out, because a lane that ran out of schedule
            // would have to invent something — and by then the ramp has moved so far that the same
            // pair is a different fight.
            SiegeEndless.BossesAt(51, found);
            Assert.AreEqual(new[] { SiegeKind.Blightcaller, SiegeKind.Boss }, found);
        }

        /// <summary>
        /// Every one of the four is met alone before any pair arrives.
        ///
        /// <b>The whole argument for four different bosses</b> (invariant 37z): each takes a
        /// different thing and has to be learned on its own, so a pair before wave sixteen would
        /// be somebody's first meeting with two fights at once.
        /// </summary>
        [Test]
        public void EveryBossIsMetAloneBeforeAnyPair()
        {
            var alone = new HashSet<SiegeKind>();
            var found = new List<SiegeKind>();

            for (int wave = 1; wave <= SiegeEndless.PairsAfter; wave++)
            {
                SiegeEndless.BossesAt(wave, found);
                if (found.Count == 0) continue;

                Assert.AreEqual(1, found.Count, $"wave {wave} sends a pair before wave "
                                                + SiegeEndless.PairsAfter);
                alone.Add(found[0]);
            }

            Assert.AreEqual(SiegeEndless.Bosses.Length, alone.Count,
                            "not every boss is met on its own first");
        }

        // ------------------------------------------------------------------ the ramp
        /// <summary>
        /// A wave is never easier than the one before it, and the climb is a straight line.
        ///
        /// <b>Linear rather than compounding</b>: a multiplier per wave doubles every few waves
        /// and puts every run's ceiling within a minute of every other, which makes every run the
        /// same length.
        /// </summary>
        [Test]
        public void EveryWaveIsTougherThanTheOneBeforeIt()
        {
            for (int wave = 2; wave <= 60; wave++)
            {
                var here = SiegeEndless.SurgeAt(wave);
                var before = SiegeEndless.SurgeAt(wave - 1);

                Assert.Greater(here.HealthTenths, before.HealthTenths, $"wave {wave}");
                Assert.Greater(here.BlowTenths, before.BlowTenths, $"wave {wave}");
            }
        }

        /// <summary>
        /// The ramp never sends a colour the level's own line has no answer to.
        ///
        /// <b>Invariant 5d asked of a hill nobody authored.</b> A raider no ward is strong against
        /// is answered at half rate, which on a lane that never lets up is a wall rather than a
        /// wave.
        /// </summary>
        [Test]
        public void TheRampNeverSendsAColourTheLineCannotAnswer()
        {
            var layout = Layout();
            Assert.IsNull(layout.Fault, layout.Fault);

            var wards = new HashSet<char>(layout.Wards);

            for (int wave = 1; wave <= SiegeEndless.PairsAfter * 3; wave++)
            {
                var coming = layout.Endless.WaveAt(wave, layout.Seed);

                Assert.IsNotEmpty(coming, $"wave {wave} sends nothing");

                foreach (var spec in coming)
                    Assert.IsTrue(wards.Contains(spec.Colour),
                                  $"wave {wave} sends a '{spec.Colour}' "
                                  + SiegeTuning.NameOf(spec.Kind));
            }
        }

        /// <summary>
        /// The two field raiders arrive last, and never more than one of each in a wave.
        ///
        /// <b>Two of a kind share one answer</b> — the field only comes back when the last of them
        /// is dead — so a wave dealing three weavers would be a wave whose field is simply gone.
        /// </summary>
        [Test]
        public void AWaveNeverSendsTwoOfTheSameFieldRaider()
        {
            var layout = Layout();

            for (int wave = 1; wave <= SiegeEndless.PairsAfter * 3; wave++)
            {
                int bombers = 0;

                foreach (var spec in layout.Endless.WaveAt(wave, layout.Seed))
                    if (spec.Kind == SiegeKind.Bomber) bombers++;

                // **At most one, and never before its depth.** A wave of bombers is a wave that
                // pays the player rather than pressing them, which is the one shape this lane may
                // not deal - see `SiegeTuning.MostBombs`.
                Assert.LessOrEqual(bombers, 1, $"wave {wave}");

                if (wave < SiegeEndless.BombersFrom) Assert.AreEqual(0, bombers, $"wave {wave}");
            }
        }

        /// <summary>
        /// A duel sends its bosses and nothing else.
        ///
        /// A duel stacked on a wave still swinging at the line is two fail states arriving
        /// together, which is what invariant 37t moved the quiet before a warlord to avoid — so
        /// an empty hill is the <em>point</em> for the three bosses that shell the line.
        /// </summary>
        [Test]
        public void ABossWaveThatCanTakeHealthSendsNothingButItsBosses()
        {
            var layout = Layout();
            var bosses = new List<SiegeKind>();

            for (int wave = 1; wave <= 50; wave++)
            {
                SiegeEndless.BossesAt(wave, bosses);
                if (bosses.Count == 0) continue;

                bool bites = false;
                foreach (var kind in bosses)
                    if (SiegeTuning.EndangersTheLine(kind)) bites = true;

                if (!bites) continue;

                var coming = layout.Endless.WaveAt(wave, layout.Seed);
                Assert.AreEqual(bosses.Count, coming.Length, $"wave {wave}");

                foreach (var spec in coming)
                    Assert.IsTrue(SiegeTuning.IsBoss(spec.Kind), $"wave {wave}");
            }
        }

        /// <summary>
        /// A boss that cannot take a ward's health never arrives on an empty hill.
        ///
        /// <para>
        /// <b>Reported from play as "the first boss does no damage", and it was true.</b> Wave
        /// four is a lone blightcaller, which takes a ward's <em>fire</em> and never its health
        /// (37z) — so with nothing walking, the five seconds of dark it buys cost the player
        /// exactly nothing and the wave rejects no play at all (invariant 5d). The escort is what
        /// gives the fire something to be worth.
        /// </para>
        /// <para>
        /// Asked as <see cref="SiegeTuning.EndangersTheLine"/> rather than by naming the
        /// blightcaller, so a fifth boss taking something other than health inherits the answer.
        /// </para>
        /// </summary>
        [Test]
        public void EveryBossWaveComesAlone()
        {
            // A boss comes in alone (37dn), on the Infinite lane as on the ladder: no escort,
            // whatever it takes, because every boss takes health now.
            var layout = Layout();
            var bosses = new List<SiegeKind>();
            int met = 0;

            for (int wave = 1; wave <= 50; wave++)
            {
                SiegeEndless.BossesAt(wave, bosses);
                if (bosses.Count == 0) continue;

                met++;

                var coming = layout.Endless.WaveAt(wave, layout.Seed);

                int escort = 0;
                foreach (var spec in coming)
                    if (!SiegeTuning.IsBoss(spec.Kind)) escort++;

                Assert.AreEqual(0, escort, $"wave {wave} sends a boss with company");

                Assert.LessOrEqual(coming.Length, SiegeLayout.MaxRaiders, $"wave {wave}");
            }

            Assert.Greater(met, 0, "no wave in fifty sends a boss that takes no health");
        }

        /// <summary>
        /// The same wave is dealt twice the same way, and a different level deals a different one.
        ///
        /// <b>A hash rather than a stream</b>, so wave forty does not depend on how many rolls
        /// waves one to thirty-nine happened to take — a rule change anywhere would otherwise move
        /// a hill somebody had already learned.
        /// </summary>
        [Test]
        public void AWaveIsDealtTheSameWayTwiceAndDifferentlyPerLevel()
        {
            var layout = Layout();

            for (int wave = 1; wave <= 20; wave++)
            {
                var once = layout.Endless.WaveAt(wave, layout.Seed);
                var again = layout.Endless.WaveAt(wave, layout.Seed);

                Assert.AreEqual(once.Length, again.Length, $"wave {wave}");

                for (int i = 0; i < once.Length; i++)
                {
                    Assert.AreEqual(once[i].Colour, again[i].Colour, $"wave {wave}, raider {i}");
                    Assert.AreEqual(once[i].Kind, again[i].Kind, $"wave {wave}, raider {i}");
                }
            }

            bool differs = false;
            for (int wave = 1; wave <= 20 && !differs; wave++)
            {
                var mine = layout.Endless.WaveAt(wave, layout.Seed);
                var other = layout.Endless.WaveAt(wave, layout.Seed ^ 0x5bf03635u);

                for (int i = 0; i < mine.Length && i < other.Length; i++)
                    if (mine[i].Colour != other[i].Colour || mine[i].Kind != other[i].Kind)
                        { differs = true; break; }
            }

            Assert.IsTrue(differs, "two seeds deal the same hill, so the seed does nothing");
        }

        // ------------------------------------------------------------------ the run
        /// <summary>
        /// An endless lane is never finished by clearing the hill, and always finished when the
        /// line falls.
        ///
        /// <b>Not a euphemism</b>: a run that can never be won still has to end, and the ending it
        /// has is the only one it has. Routing it through <c>IsFinished</c> is what makes an
        /// endless run an ordinary run in every way that matters.
        /// </summary>
        [Test]
        public void AnEndlessRunEndsWhenTheLineFallsAndNeverBefore()
        {
            var board = SiegeBoard.Build(Layout());

            Assert.IsFalse(board.IsFinished, "an endless lane opens finished");

            // Long enough for several waves to arrive and nobody to answer them.
            for (int i = 0; i < 60 * 600 && !board.IsFinished; i++) board.Advance(1f / 60f);

            Assert.IsTrue(board.IsFinished, "the line never fell with nobody playing");
            Assert.AreEqual(0, board.WardsStanding);
            Assert.Greater(board.WavesCleared, 0, "no wave ever arrived");
        }

        /// <summary>
        /// An endless level is graded on a count that climbs: three stars asks for a bigger wave
        /// than two.
        /// </summary>
        [Test]
        public void AnEndlessLevelIsGradedOnACountThatClimbs()
        {
            var tuning = LevelTuning.Climbing(20, .55f);

            Assert.IsTrue(tuning.Climbs);
            Assert.AreEqual(20, tuning.GoldThreshold);
            Assert.Less(tuning.SilverThreshold, tuning.GoldThreshold);

            Assert.AreEqual(3, tuning.StarsFor(20));
            Assert.AreEqual(3, tuning.StarsFor(40));
            Assert.AreEqual(2, tuning.StarsFor(tuning.SilverThreshold));
            Assert.AreEqual(1, tuning.StarsFor(1));
            Assert.AreEqual(0, tuning.StarsFor(0));

            Assert.IsFalse(tuning.HasBudget, "an endless lane has no allowance to run out of");
        }

        /// <summary>
        /// Two bosses arriving together stand either side of the middle, never in one column.
        ///
        /// <para>
        /// <b>The only placement rule a pair wave brought</b>, and the one thing about it a number
        /// can check. A boss is three cells tall and stands still for the rest of its life, so two
        /// of them in the middle lane would be one silhouette with two health bars over it — and
        /// the bars were the half that really did overlap until <c>SiegeView.FreeCrown</c> gave
        /// the second one a rung of its own (invariant 37u).
        /// </para>
        /// <para>
        /// Asked of the rule rather than of a mustered board on purpose: reaching wave 21 through
        /// <c>SiegeBoard.Advance</c> means surviving twenty waves first, so a test that played
        /// there would be a test of the tuning wearing a placement check's clothes.
        /// </para>
        /// </summary>
        [Test]
        public void TwoBossesArrivingTogetherStandEitherSideOfTheMiddle()
        {
            int middle = SiegeTuning.Lanes / 2;

            Assert.AreEqual(middle, SiegeTuning.BossLane(0, 1),
                            "a lone boss walks down the middle");

            int left = SiegeTuning.BossLane(0, 2), right = SiegeTuning.BossLane(1, 2);

            Assert.AreNotEqual(left, right, "a pair shares one lane");
            Assert.AreNotEqual(middle, left, "a pair stands where a lone boss stands");
            Assert.AreNotEqual(middle, right, "a pair stands where a lone boss stands");

            foreach (int lane in new[] { left, right })
            {
                Assert.GreaterOrEqual(lane, 0, "a boss is off the hill");
                Assert.Less(lane, SiegeTuning.Lanes, "a boss is off the hill");
            }
        }

        /// <summary>
        /// A pair wave really is a pair, so the wave's own size is how many bosses it holds.
        ///
        /// <c>SiegeBoard.Muster</c> passes the wave's size to <see cref="SiegeTuning.BossLane"/>
        /// as the boss count, which is exact only while a boss wave sends nothing else — the
        /// clause is checked next door, and this is the half that says the two facts are joined.
        /// </summary>
        [Test]
        public void EveryPairWaveSendsExactlyTwoBossesAndNothingElse()
        {
            var layout = Layout();

            for (int wave = SiegeEndless.PairsAfter + 1;
                 wave <= SiegeEndless.PairsAfter + SiegeEndless.PairEvery * 6; wave++)
            {
                if (!SiegeEndless.IsBossWave(wave)) continue;

                var coming = layout.Endless.WaveAt(wave, layout.Seed);

                Assert.AreEqual(2, coming.Length, $"wave {wave} is not a pair");

                foreach (var spec in coming)
                    Assert.IsTrue(SiegeTuning.IsBoss(spec.Kind),
                                  $"wave {wave} sends a " + SiegeTuning.NameOf(spec.Kind));
            }
        }

        /// <summary>
        /// A lane that authors waves as well as a ramp is refused rather than half-read.
        ///
        /// Two answers to what the second wave is, and the reader picking one would ship a lane
        /// nobody authored.
        /// </summary>
        [Test]
        public void ALaneMayNotAuthorBothARampAndItsOwnWaves()
        {
            var dto = new LevelDto
            {
                id = "s99_both",
                siege = new SiegeDto
                {
                    width = 8, height = 5,
                    rows = new[]
                    {
                        "ryybgyyg", "bybgrgyy", "rbryyggr", "grgrgbbr", "yybgrrbg",
                    },
                    gems = "rgby",
                    wards = "rgby",
                    waves = new[] { "rgby" },
                    endless = new SiegeEndlessDto { goldWave = 20, silverFactor = .55f },
                },
            };

            var problems = new List<string>();
            var mode = new SiegeMode();

            Assert.IsFalse(mode.TryRead(dto, Id("s99_both"), problems, out _),
                           "a lane carrying both was read");

            Assert.IsNotEmpty(problems);
        }
    }
}
