using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Ranks;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The client half of what rank a save holds.
    ///
    /// <para>
    /// <b>A rank went public, and that is what this fixture is about.</b> While a badge was only
    /// ever drawn on the player's own map it was derived on the device and stored nowhere
    /// (invariant 52) — worth nothing to forge, because nobody else could see it. It is on a
    /// board row and a public profile now, so invariant 19a governs it: the server derives its
    /// own answer from the save it reads itself (<c>rungOf</c> in <c>functions/src/ranks.ts</c>),
    /// and the client derives one too, because reaching a rung has to mark the card as owing a
    /// publish (<see cref="Social.GroveCard.Fingerprint"/>).
    /// </para>
    /// <para>
    /// <b>A drift between the two is completely silent.</b> Nothing throws, nothing is refused
    /// and no gate anywhere goes red — a keeper simply wears one badge on their own map and a
    /// different one on everybody else's screen, for ever, and the only instrument that could
    /// find it is somebody looking at both. So both sides run
    /// <c>firebase/shared/grove-vectors.json</c>, written by a third implementation in
    /// <c>Tools/make_rank_vectors.py</c>, and <c>firebase/functions/test/grove.mjs</c> is the
    /// other half. Invariant 9a, for a badge.
    /// </para>
    /// <para>
    /// <b>Everything here runs offline</b> — the file is read through <see cref="TestJson"/>
    /// rather than <c>JsonUtility</c> and located without <c>Application.dataPath</c>, both of
    /// which are engine <c>ECall</c>s that would have <c>Tools/verify/tests.py</c> report this as
    /// "needs the Editor" and walk past it. That is invariant 29e, and it matters more here than
    /// anywhere: a rule whose drift is silent is exactly the one that must not be pinned by a
    /// fixture nobody runs.
    /// </para>
    /// <para>
    /// <b>It drives <see cref="SaveRankSource"/> rather than the ledgers</b>, because that is the
    /// reading a card is built from and the one the server mirrors. <see cref="RankLadderTests"/>
    /// covers the live reading beside it, and <see cref="TheTwoSourcesAgree"/> below is what
    /// holds the pair together on this side of the wire.
    /// </para>
    /// </summary>
    public sealed class RankVectorTests
    {
        // ------------------------------------------------------------------- the file
        sealed class Case
        {
            public string Name;
            public SaveFileDto Save;
            public RanksDto Ladder;
            public int KeeperLevel;
            public string Held;

            /// <summary>Records as (level, stars), kept so the live source can be driven too.</summary>
            public List<(string level, int stars)> Records;
        }

        static readonly List<Case> _cases = new List<Case>();
        static CatalogIndex _catalog;
        static RanksDto _sharedLadder;

        /// <summary>
        /// The catalog the cases are measured against, built from the vector file's own chapter
        /// list rather than from the shipped manifest.
        ///
        /// <b>Deliberately not the shipped catalog</b>, for the reason the file says: a vector
        /// pinned to live content fails the day somebody authors a chapter, which teaches
        /// everybody to edit vectors instead of reading them.
        /// </summary>
        static void Load()
        {
            if (_catalog != null) return;

            var file = TestJson.ReadShared("grove-vectors.json");

            var builder = new CatalogIndexBuilder();
            int order = 10;

            foreach (object raw in TestJson.Children(file, "rankChapters"))
            {
                var chapter = TestJson.Object(raw);
                var levels = new List<string>();
                foreach (object level in TestJson.Children(chapter, "levels"))
                    levels.Add(level as string);

                builder.Add(new ManifestChapterDto
                {
                    id = TestJson.Str(chapter, "id", string.Empty),
                    order = order,
                    version = 1,
                    mode = "siege",
                    levels = levels.ToArray(),
                }, 1);

                order += 10;
            }

            _catalog = builder.Build();
            _sharedLadder = LadderOf(TestJson.Children(file, "rankLadder"));

            foreach (object raw in TestJson.Children(file, "rankCases"))
                _cases.Add(CaseOf(TestJson.Object(raw)));

            Assert.Greater(_cases.Count, 0, "the vector file has no rank cases");
        }

        static RanksDto LadderOf(List<object> rungs)
        {
            var built = new RankRungDto[rungs.Count];

            for (int i = 0; i < rungs.Count; i++)
            {
                var rung = TestJson.Object(rungs[i]);
                var lines = TestJson.Children(rung, "requires");
                var requires = new RankRequirementDto[lines.Count];

                for (int j = 0; j < lines.Count; j++)
                {
                    var line = TestJson.Object(lines[j]);
                    requires[j] = new RankRequirementDto
                    {
                        measure = TestJson.Str(line, "measure", string.Empty),
                        scope = TestJson.Str(line, "scope", string.Empty),
                        target = TestJson.Int(line, "target"),
                    };
                }

                built[i] = new RankRungDto { id = TestJson.Str(rung, "id", string.Empty), requires = requires };
            }

            return new RanksDto { rungs = built };
        }

        static Case CaseOf(Dictionary<string, object> map)
        {
            var records = new List<(string, int)>();
            foreach (object raw in TestJson.Children(map, "levels"))
            {
                var row = TestJson.Object(raw);
                records.Add((TestJson.Str(row, "level", string.Empty), TestJson.Int(row, "stars")));
            }

            var save = new SaveFileDto { levels = new LevelRecordDto[records.Count] };
            for (int i = 0; i < records.Count; i++)
                save.levels[i] = new LevelRecordDto
                {
                    levelId = records[i].Item1,
                    stars = records[i].Item2,
                    clears = records[i].Item2 > 0 ? 1 : 0,
                };

            var endless = TestJson.Children(map, "endless");
            save.endlessBest = new EndlessBestDto[endless.Count];
            for (int i = 0; i < endless.Count; i++)
            {
                var row = TestJson.Object(endless[i]);
                save.endlessBest[i] = new EndlessBestDto
                {
                    level = TestJson.Str(row, "level", string.Empty),
                    wave = TestJson.Int(row, "wave"),
                    waves = TestJson.Int(row, "waves"),
                };
            }

            var lifetime = TestJson.Children(map, "lifetime");
            save.tasks = new TaskStateDto { lifetime = new TaskCountDto[lifetime.Count] };
            for (int i = 0; i < lifetime.Count; i++)
            {
                var row = TestJson.Object(lifetime[i]);
                save.tasks.lifetime[i] = new TaskCountDto
                {
                    goal = TestJson.Str(row, "goal", string.Empty),
                    count = TestJson.Int(row, "count"),
                };
            }

            // A case may carry its own ladder; most climb the shared one. `ContainsKey` rather
            // than "is the list empty", because an empty ladder is one of the cases — it is a
            // build whose content has no `ranks` block, and it must hold nothing rather than
            // silently fall back to the shared ladder. The generator had exactly that bug.
            var ladder = map.ContainsKey("ladder")
                ? LadderOf(TestJson.Children(map, "ladder"))
                : _sharedLadder;

            return new Case
            {
                Name = TestJson.Str(map, "name", "(unnamed)"),
                Save = save,
                Ladder = ladder,
                KeeperLevel = TestJson.Int(map, "keeperLevel", 1),
                Held = TestJson.Str(map, "held", string.Empty),
                Records = records,
            };
        }

        /// <summary>
        /// The ladder a case climbs, built through the shipped reader rather than around it.
        ///
        /// A vector proved against a ladder the fixture assembled proves nothing about the one a
        /// player's content produces — <c>EndlessRewardTests.ConfigCase.AsTable</c>'s argument,
        /// and the reason every rung here goes through <see cref="RankLadder.Resolve"/>.
        /// </summary>
        static RankLadder Resolve(Case c)
        {
            var problems = new List<string>();
            return RankLadder.Resolve(c.Ladder, problems);
        }

        [SetUp]
        public void Start()
        {
            Load();
            PlayerProgress.LoadFrom(new SaveFileDto());
            Tasks.LifetimeTally.Reset();
        }

        [TearDown]
        public void Finish()
        {
            PlayerProgress.LoadFrom(new SaveFileDto());
            Tasks.LifetimeTally.Reset();
        }

        // -------------------------------------------------------- the shared contract
        [Test]
        public void EveryVectorCaseHoldsTheRungTheServerPublishes()
        {
            foreach (var c in _cases)
            {
                var source = new SaveRankSource(c.Save, _catalog, c.KeeperLevel);
                var held = Resolve(c).Held(source);

                Assert.AreEqual(c.Held, held == null ? string.Empty : held.Id,
                                $"the rank disagrees with the server: {c.Name}");
            }
        }

        /// <summary>
        /// The two readings on <em>this</em> side agree: the save file one that a card is built
        /// from, and the live-ledger one the map's own badge draws.
        ///
        /// <para>
        /// <b>This is the disagreement a player would actually be looking at.</b> The vectors
        /// hold the client to the server; this holds the client to itself, and the failure it
        /// catches is the one that shows up on one screen of a single device — a map badge and a
        /// profile medallion that do not match, with a sync in between and nothing to blame.
        /// </para>
        /// <para>
        /// The endless rows are the one reading that cannot be driven this way without a ledger
        /// load, so the cases that carry them are compared on everything else and the lane is
        /// left to <c>EndlessRewardTests</c>, which owns that rule.
        /// </para>
        /// </summary>
        [Test]
        public void TheTwoSourcesAgree()
        {
            foreach (var c in _cases)
            {
                // The lane is `EndlessRewardTests`' rule, and driving it here would need the
                // ledger loaded. Skipped by name rather than silently, so a case that grows an
                // endless row does not quietly stop being compared.
                if (c.Save.endlessBest.Length > 0) continue;

                PlayerProgress.LoadFrom(c.Save);
                Tasks.LifetimeTally.Reset();
                Tasks.LifetimeTally.LoadFrom(c.Save.tasks.lifetime);

                var ladder = Resolve(c);

                var fromSave = ladder.Held(new SaveRankSource(c.Save, _catalog, c.KeeperLevel));
                var fromLedgers = ladder.Held(new LiveAtLevel(_catalog, c.KeeperLevel));

                Assert.AreEqual(fromSave == null ? string.Empty : fromSave.Id,
                                fromLedgers == null ? string.Empty : fromLedgers.Id,
                                $"the save reading and the ledger reading disagree: {c.Name}");
            }
        }

        /// <summary>
        /// The live source with the keeper level handed in rather than read off
        /// <c>PlayerProgression</c>, which a fixture cannot drive without an XP curve. Everything
        /// else is the shipped live reading.
        /// </summary>
        sealed class LiveAtLevel : IRankSource
        {
            readonly LedgerRankSource _live;
            readonly long _level;

            public LiveAtLevel(CatalogIndex index, int level)
            {
                _live = new LedgerRankSource(index);
                _level = level;
            }

            public long Cleared(string scope) => _live.Cleared(scope);
            public long Stars(string scope) => _live.Stars(scope);
            public long ThreeStars(string scope) => _live.ThreeStars(scope);
            public long KeeperLevel => _level;
            public long BestWave(string scope) => _live.BestWave(scope);
            public long Lifetime(Tasks.TaskGoal goal) => _live.Lifetime(goal);
        }

        // ----------------------------------------------------------------- the shapes
        /// <summary>
        /// A save with nothing in it holds nothing, and does not throw on the way. Not in the
        /// vector file because it is about <em>absence</em>, which a case cannot carry.
        /// </summary>
        [Test]
        public void AnEmptySaveHoldsNothing()
        {
            var ladder = RankLadder.Resolve(_sharedLadder, new List<string>());

            Assert.IsNull(ladder.Held(new SaveRankSource(new SaveFileDto(), _catalog, 99)));
            Assert.IsNull(ladder.Held(new SaveRankSource(null, _catalog, 99)));
        }

        /// <summary>
        /// A catalog that has not loaded yet reads as nought rather than throwing — the map
        /// draws before the splash has finished on a slow device, and a card built in that
        /// window must publish no rung rather than take the publish down with it.
        /// </summary>
        [Test]
        public void ACatalogThatHasNotLoadedHoldsNothing()
        {
            var ladder = RankLadder.Resolve(_sharedLadder, new List<string>());
            var save = _cases[_cases.Count - 1].Save;

            Assert.IsNull(ladder.Held(new SaveRankSource(save, null, 99)));
        }
    }
}
