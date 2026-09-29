using System.Collections.Generic;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The eighth chapter's one new rule, one clause at a time: a rung may end on a <b>duel</b> -
    /// two bosses walking on together - written as two boss tokens joined by
    /// <see cref="SiegeLayout.BossJoin"/>.
    ///
    /// <para>
    /// <b>Its own file for the fifth to seventh chapters' reason</b>: each clause is asked of a
    /// board built for it, so a fault reads as the rule that broke rather than as a chapter that
    /// lost a run. The chapter itself is measured by
    /// <c>TheEighthChapterIsFoughtOnABoughtLine</c>; that both bosses of every shipped duel stand
    /// and cast is <c>EveryShippedBossRungIsAFight</c>.
    /// </para>
    /// <para>
    /// <b>What is not new is the board.</b> The Infinite lane has sent pairs since it shipped, so
    /// the muster, the lanes, the stands and the floors are already played; what is proved here is
    /// that the authored grammar reaches them and says exactly what it means.
    /// </para>
    /// </summary>
    public sealed partial class SiegeRuleTests
    {
        /// <summary>A duel of these two, as the last wave of the fixture's own field, cogs dealt.</summary>
        static SiegeLayout DuelOf(SiegeKind first, SiegeKind second)
            => Layout(Field, Gems, Wards, new[] { "rgbyrgby" },
                      SiegeTuning.NameOf(first) + ":r" + SiegeLayout.BossJoin
                      + SiegeTuning.NameOf(second) + ":b", Cogs);

        // ----------------------------------------------------------------- the grammar
        [Test]
        public void ADuelIsTwoBossesAloneAsTheLastWave()
        {
            var layout = DuelOf(SiegeKind.Gravemaw, SiegeKind.Harrower);

            Assert.IsNull(layout.Fault, layout.Fault);
            Assert.IsTrue(layout.HasBoss);
            Assert.IsTrue(layout.IsDuel);
            CollectionAssert.AreEqual(new[] { SiegeKind.Gravemaw, SiegeKind.Harrower },
                                      layout.BossKinds);
            CollectionAssert.AreEqual(new[] { 'r', 'b' }, layout.BossWears);

            // **Everything a lone boss already meant, the first of the pair still means**, so a
            // reading that only ever asked for "the boss" is not quietly wrong about a duel.
            Assert.AreEqual(SiegeKind.Gravemaw, layout.BossKind);
            Assert.AreEqual('r', layout.Boss);

            int last = layout.Waves.Length - 1;
            Assert.AreEqual(last, layout.BossWave, "a duel is the last wave, by rule");
            Assert.AreEqual(2, layout.SizeOf(last), "a duel walks on with company");
            Assert.AreEqual(2, layout.BossesIn(last));
            Assert.AreEqual(SiegeKind.Gravemaw, layout.KindAt(last, 0));
            Assert.AreEqual(SiegeKind.Harrower, layout.KindAt(last, 1));

            // The authored wave in front of it is untouched.
            Assert.AreEqual(0, layout.BossesIn(0));
        }

        [Test]
        public void ALoneBossStillReadsAsOneBoss()
        {
            var layout = Duel("warlord:r");

            Assert.IsNull(layout.Fault, layout.Fault);
            Assert.IsFalse(layout.IsDuel);
            CollectionAssert.AreEqual(new[] { SiegeKind.Boss }, layout.BossKinds);
            Assert.AreEqual(1, layout.SizeOf(layout.BossWave));
        }

        /// <summary>
        /// Every malformed duel is refused at read, by name - invariant 5f's rule: a token this
        /// build cannot mean was written for rules that are not these, and salvaging one boss out
        /// of it would ship a fight nobody authored.
        /// </summary>
        [Test]
        public void AMalformedDuelIsRefusedRatherThanSalvaged()
        {
            string[] refused =
            {
                "warlord:r+warlord:b",                     // one verb at twice the health
                "warlord:r+",                              // an empty half
                "+warlord:r",
                "warlord:r+overlord:b+gorgon:g",           // a third boss nobody measured
                "warlord:r+dragon:b",                      // a half this mode does not know
                "warlord:r + overlord:b",                  // no trimming inside the halves
            };

            foreach (string token in refused)
            {
                var layout = Duel(token);

                Assert.IsNotNull(layout.Fault, $"'{token}' was read as something");
                Assert.IsFalse(layout.HasBoss, $"'{token}' salvaged a boss out of itself");
                Assert.IsEmpty(layout.BossKinds, $"'{token}' kept a half");
            }
        }

        // ----------------------------------------------------------------- the board
        [Test]
        public void ADuelWalksOnTogetherOnAClearedHillEitherSideOfTheMiddle()
        {
            var board = SiegeBoard.Build(DuelOf(SiegeKind.Blightcaller, SiegeKind.Gorgon));

            var seen = new Dictionary<int, SiegeRaider>();

            for (int i = 0; i < 60 * 240 && seen.Count < 2; i++)
            {
                // Fed, so the wave in front is cleared and the duel is let on.
                for (int w = 0; w < board.Wards.Count; w++)
                    board.Wards[w].Fuel = board.Wards[w].Capacity;

                board.Advance(1f / 60f);

                foreach (var raider in board.Raiders)
                    if (raider.Boss && raider.Alive) seen[raider.Id] = raider;
            }

            Assert.AreEqual(2, seen.Count, "the duel never walked on whole");

            var kinds = new HashSet<SiegeKind>();
            var lanes = new HashSet<int>();

            foreach (var boss in seen.Values)
            {
                kinds.Add(boss.Kind);
                lanes.Add(boss.Lane);
            }

            CollectionAssert.AreEquivalent(new[] { SiegeKind.Blightcaller, SiegeKind.Gorgon }, kinds);
            Assert.AreEqual(2, lanes.Count, "the pair was stood in one lane, on top of itself");

            // **Alone on the hill** (37dn): nothing but the two of them.
            foreach (var raider in board.Raiders)
                if (raider.Alive)
                    Assert.IsTrue(raider.Boss, "a duel walked on with a raider beside it");
        }

        [Test]
        public void TheForecastNamesADuelAsAPair()
        {
            var layout = DuelOf(SiegeKind.Thunderer, SiegeKind.Shackler);
            var coming = SiegeForecast.Of(layout, layout.BossWave);

            Assert.IsTrue(coming.HasBoss);
            Assert.IsTrue(coming.IsDuel);
            Assert.AreEqual(SiegeKind.Thunderer, coming.Boss);
            Assert.AreEqual(SiegeKind.Shackler, coming.Partner);

            var alone = Duel("thunderer:b");
            Assert.IsFalse(SiegeForecast.Of(alone, alone.BossWave).IsDuel);
        }

        /// <summary>
        /// Par counts both bosses, because the hill really holds both - a duel that paid par for
        /// one would put three stars out of reach on every duel rung with every number plausible.
        /// </summary>
        [Test]
        public void ParCountsBothBossesOfADuel()
        {
            var pair = DuelOf(SiegeKind.Sunlord, SiegeKind.Hollowking);
            var first = Layout(Field, Gems, Wards, new[] { "rgbyrgby" }, "sunlord:r", Cogs);
            var second = Layout(Field, Gems, Wards, new[] { "rgbyrgby" }, "hollowking:b", Cogs);
            var none = Layout(Field, Gems, Wards, new[] { "rgbyrgby" }, "", Cogs);

            int body = SiegeTuning.Par(none);

            Assert.Greater(SiegeTuning.Par(pair), SiegeTuning.Par(first));
            Assert.Greater(SiegeTuning.Par(pair), SiegeTuning.Par(second));

            // **Each of the pair at its duel share** (`SiegeLayout.DuelSharePercent`), and par
            // reads the same share the muster stands them with - within a match either side, which
            // is all a ceiling division can promise.
            int alone = SiegeTuning.Par(first) + SiegeTuning.Par(second) - 2 * body;
            int shared = body + alone * SiegeLayout.DuelSharePercent / 100;
            Assert.LessOrEqual(System.Math.Abs(SiegeTuning.Par(pair) - shared), 2);
        }

        [Test]
        public void EachBossOfADuelStandsWithItsShare()
        {
            var layout = DuelOf(SiegeKind.Sunlord, SiegeKind.Hollowking);
            var board = SiegeBoard.Build(layout);

            var seen = new Dictionary<int, SiegeRaider>();
            for (int i = 0; i < 60 * 240 && seen.Count < 2; i++)
            {
                for (int w = 0; w < board.Wards.Count; w++)
                    board.Wards[w].Fuel = board.Wards[w].Capacity;

                board.Advance(1f / 60f);

                foreach (var raider in board.Raiders)
                    if (raider.Boss) seen[raider.Id] = raider;
            }

            Assert.AreEqual(2, seen.Count, "the duel never walked on whole");

            foreach (var boss in seen.Values)
                Assert.AreEqual(SiegeTuning.Shared(layout.Tough.Health(SiegeTuning.HealthOf(boss.Kind)),
                                                   SiegeLayout.DuelSharePercent),
                                boss.MaxHealth, $"the {boss.Kind} of a duel stands with the wrong share");

            // And a lone boss is untouched.
            var lone = Duel("sunlord:r");
            Assert.AreEqual(100, lone.ShareAt(lone.BossWave, 0));
        }

        // ----------------------------------------------------------------- the surge
        /// <summary>
        /// The eighth chapter trades a tenth of surge for its crowd, and the trade is the one row
        /// in <see cref="SiegeTuning.Traded"/> - every other chapter is still the arithmetic.
        /// </summary>
        [Test]
        public void TheEighthChapterTradesSurgeForItsCrowd()
        {
            Assert.AreEqual(15, SiegeTuning.ToughnessFor(6), "Bonereach moved");
            Assert.AreEqual(14, SiegeTuning.ToughnessFor(7), "Cloudkeep is not the traded 1.4");
            Assert.AreEqual(15, SiegeTuning.ToughnessFor(8), "Cogspire is not the traded 1.5");
            Assert.AreEqual(16, SiegeTuning.ToughnessFor(9), "Windwreck is not the traded 1.6");
            Assert.AreEqual(19, SiegeTuning.ToughnessFor(10), "the ladder past the trades moved");

            foreach (var rung in Cogspire)
                Assert.AreEqual(SiegeTuning.ToughnessFor(8), rung.Built().Tough.HealthTenths,
                                $"{rung.Id} deals a surge the rule does not give it");

            foreach (var rung in Cloudkeep)
                Assert.AreEqual(SiegeTuning.ToughnessFor(7), rung.Built().Tough.HealthTenths,
                                $"{rung.Id} deals a surge the rule does not give it");
        }
    }
}
