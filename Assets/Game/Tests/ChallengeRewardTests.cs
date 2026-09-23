using System.Collections.Generic;
using GlimmerGrove.Challenges;
using GlimmerGrove.Persistence;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The client half of the two challenge rules both sides compute, against the shared
    /// vectors in <c>firebase/shared/grove-vectors.json</c>; <c>functions/test/grove.mjs</c>
    /// is the other half.
    ///
    /// <para>
    /// <b>The XP rule</b> turns a save's <c>challenges.clears</c> rows into XP with no star
    /// behind it (<see cref="ChallengeRewardRule"/>, invariant 9d's shape). A drift is silent:
    /// <c>buildCard</c> drops whatever the lower keeper level gated (19a). <b>The allowance
    /// rule</b> turns held deals into plays a day (<see cref="ChallengeAllowance"/>); a drift is
    /// a coin claim refused for a play the page offered (45d).
    /// </para>
    /// <para>
    /// Read through <c>TestJson</c> and located without <c>Application.dataPath</c>, so the
    /// whole fixture runs offline (invariant 29e) — the reason <c>EndlessRewardTests</c> gives.
    /// </para>
    /// </summary>
    public sealed class ChallengeRewardTests
    {
        static Dictionary<string, object> File() => TestJson.ReadShared("grove-vectors.json");

        static ChallengeRewardRule Rule(Dictionary<string, object> config)
        {
            var problems = new List<string>();
            // Through the shipped reader rather than around it. `maxClears` nought is the
            // "withdrawn" shape a written block takes, so the dto is authored by giving it a rate
            // as well; a block with both at nought is unauthored and would read as the defaults.
            var dto = new ChallengeRewardDto
            {
                coins = 1,
                xp = TestJson.Int(config, "xp"),
                maxClears = TestJson.Int(config, "maxClears"),
            };
            var rule = ChallengeRewardRule.Resolve(dto, problems);
            return rule;
        }

        static SaveFileDto SaveOf(List<object> rows)
        {
            var save = new SaveFileDto { challenges = new ChallengeStateDto { clears = new ChallengeCountDto[rows.Count] } };
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] == null) continue;
                var row = TestJson.Object(rows[i]);
                save.challenges.clears[i] = new ChallengeCountDto
                {
                    genre = TestJson.Str(row, "genre", string.Empty),
                    count = TestJson.Int(row, "count"),
                };
            }
            return save;
        }

        [Test]
        public void TheBuiltInFiguresAreTheOnesTheVectorFileNames()
        {
            var defaults = TestJson.Child(File(), "challengeDefaults");
            Assert.AreEqual(ChallengeLimits.DefaultFreePlays, TestJson.Int(defaults, "freePlays"));
            Assert.AreEqual(ChallengeLimits.DefaultCoinsPerClear, TestJson.Int(defaults, "coins"));
            Assert.AreEqual(ChallengeLimits.DefaultXpPerClear, TestJson.Int(defaults, "xp"));
            Assert.AreEqual(ChallengeLimits.DefaultMaxClears, TestJson.Int(defaults, "maxClears"));
        }

        [Test]
        public void EveryXpCaseReadsTheSameClearCountAndPaysTheSameXpTheServerDoes()
        {
            var cases = TestJson.Children(File(), "challengeCases");
            Assert.Greater(cases.Count, 0);

            foreach (object raw in cases)
            {
                var map = TestJson.Object(raw);
                string name = TestJson.Str(map, "name", "(unnamed)");
                var save = SaveOf(TestJson.Children(map, "rows"));

                long clears = ChallengeLedger.LifetimeClearsIn(save);
                Assert.AreEqual(TestJson.Long(map, "clears"), clears, name + " — clears");

                long xp = Rule(TestJson.Child(map, "config")).XpFor(clears);

                Assert.AreEqual(TestJson.Long(map, "xp"), xp, name + " — xp");
            }
        }

        /// <summary>
        /// A written ceiling of nought withdraws the XP rather than inheriting the default,
        /// because the server reads the same published block as nought and the two must agree
        /// byte for byte. Only a wholly unwritten block inherits.
        /// </summary>
        [Test]
        public void AWrittenCeilingOfNoughtIsAWithdrawalNotAnInheritance()
        {
            var problems = new List<string>();
            var rule = ChallengeRewardRule.Resolve(new ChallengeRewardDto { coins = 1, xp = 7, maxClears = 0 }, problems);
            Assert.AreEqual(0, rule.MaxClears);
            Assert.IsFalse(rule.PaysXp);
            Assert.AreEqual(0L, rule.XpFor(500));

            var inherited = ChallengeRewardRule.Resolve(new ChallengeRewardDto(), problems);
            Assert.AreEqual(ChallengeLimits.DefaultMaxClears, inherited.MaxClears);
        }

        [Test]
        public void EveryAllowanceCaseAnswersThePlaysTheServerAnswers()
        {
            var file = File();
            int free = TestJson.Int(file, "challengeFreePlays");

            var tiers = new List<ChallengeTier>();
            foreach (object raw in TestJson.Children(file, "challengeTiers"))
            {
                var t = TestJson.Object(raw);
                tiers.Add(new ChallengeTier(TestJson.Str(t, "id", string.Empty), TestJson.Int(t, "gems"),
                                            TestJson.Int(t, "plays"), TestJson.Int(t, "days")));
            }

            var cases = TestJson.Children(file, "challengeAllowanceCases");
            Assert.Greater(cases.Count, 0);

            foreach (object raw in cases)
            {
                var map = TestJson.Object(raw);
                string name = TestJson.Str(map, "name", "(unnamed)");

                var held = new Dictionary<string, long>();
                foreach (var pair in TestJson.Child(map, "held"))
                    held[pair.Key] = System.Convert.ToInt64(pair.Value);

                int allowance = ChallengeAllowance.On(tiers, held, TestJson.Long(map, "now"), free);
                Assert.AreEqual(TestJson.Int(map, "allowance"), allowance, name);
            }
        }

        /// <summary>
        /// The upgrade price: the full figure with nothing running, the difference under a
        /// running smaller deal, nought when refused. Mirrored by <c>dealPrice</c> on the server
        /// and pinned by <c>challengeUpgradeCases</c>; a drift is a purchase the page priced at
        /// one figure and the server refused at another, which is gems taken and given back.
        /// </summary>
        [Test]
        public void EveryUpgradeCasePricesWhatTheServerPrices()
        {
            var file = File();
            var tiers = new List<ChallengeTier>();
            foreach (object raw in TestJson.Children(file, "challengeTiers"))
            {
                var t = TestJson.Object(raw);
                tiers.Add(new ChallengeTier(TestJson.Str(t, "id", string.Empty), TestJson.Int(t, "gems"),
                                            TestJson.Int(t, "plays"), TestJson.Int(t, "days")));
            }

            var cases = TestJson.Children(file, "challengeUpgradeCases");
            Assert.Greater(cases.Count, 0);

            foreach (object raw in cases)
            {
                var map = TestJson.Object(raw);
                string name = TestJson.Str(map, "name", "(unnamed)");

                var held = new Dictionary<string, long>();
                foreach (var pair in TestJson.Child(map, "held"))
                    held[pair.Key] = System.Convert.ToInt64(pair.Value);

                var target = tiers.Find(t => t.Id == TestJson.Str(map, "target", string.Empty));
                Assert.IsNotNull(target, name + " names a tier the vector table does not hold");

                int price = ChallengeAllowance.Price(tiers, held, TestJson.Long(map, "now"), target, out var upgraded);
                Assert.AreEqual(TestJson.Int(map, "price"), price, name + " — price");
                Assert.AreEqual(TestJson.Str(map, "upgrades", string.Empty), upgraded == null ? string.Empty : upgraded.Id,
                                name + " — upgrades");
            }
        }
    }
}
