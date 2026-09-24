using System;
using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Challenges;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The daily challenges: the reader, the hill, the four genres, and every shipped row
    /// played end to end.
    ///
    /// <para>
    /// <b>The playthroughs are the gate that matters</b> (invariant 53d's shape, four times
    /// over): a challenge board reaches no content gate that can solve it, so the only thing
    /// that can say a row is winnable is a bot playing it against the real rules with the real
    /// hill walking. Each one asserts the run is <em>won</em>, that the line was standing when
    /// it was, and prints the turns it took and the wards it had left — the margin the owner
    /// tunes against. A row the bot cannot win is a row a player is not promised.
    /// </para>
    /// <para>
    /// <b>The file is read through <c>TestJson</c></b>, not <c>JsonUtility</c>, so the whole
    /// fixture runs offline (invariant 29e); the same DTO then goes through the same
    /// <c>ChallengeTable.TryBuild</c> a device runs.
    /// </para>
    /// </summary>
    public sealed class ChallengeTests
    {
        // ------------------------------------------------------------------ the file
        /// <summary>The shipped file through the shipped reader. Shared with <c>ChallengeLedgerTests</c>.</summary>
        internal static ChallengeTable Shipped()
        {
            string path = Path.Combine(TestJson.RepoRoot(), "Assets", "StreamingAssets", "Content", "challenges.json");
            var map = TestJson.Object(TestJson.Parse(File.ReadAllText(path)));

            var dto = new ChallengeTableDto
            {
                schemaVersion = TestJson.Int(map, "schemaVersion"),
                line = new ChallengeLineDto(),
                challenges = new ChallengeDto[0],
            };

            var line = TestJson.Child(map, "line");
            dto.line.damage = TestJson.Int(line, "damage");
            dto.line.health = TestJson.Int(line, "health");
            dto.line.strike = TestJson.Int(line, "strike");

            // The v2 blocks, read the long way for the same reason the rows are: through the
            // shipped reader rather than `JsonUtility`, so the fixture runs offline (29e).
            dto.allowance = new ChallengeAllowanceDto();
            if (map.ContainsKey("allowance"))
                dto.allowance.freePlays = TestJson.Int(TestJson.Child(map, "allowance"), "freePlays");

            dto.rewards = new ChallengeRewardDto();
            if (map.ContainsKey("rewards"))
            {
                var rewards = TestJson.Child(map, "rewards");
                dto.rewards.coins = TestJson.Int(rewards, "coins");
                dto.rewards.xp = TestJson.Int(rewards, "xp");
                dto.rewards.maxClears = TestJson.Int(rewards, "maxClears");
            }

            var tiers = new List<ChallengeTierDto>();
            if (map.ContainsKey("tiers"))
                foreach (var item in TestJson.Children(map, "tiers"))
                {
                    var tier = TestJson.Object(item);
                    tiers.Add(new ChallengeTierDto
                    {
                        id = TestJson.Str(tier, "id", string.Empty),
                        gems = TestJson.Int(tier, "gems"),
                        plays = TestJson.Int(tier, "plays"),
                        days = TestJson.Int(tier, "days"),
                    });
                }
            dto.tiers = tiers.ToArray();

            var rows = new List<ChallengeDto>();
            foreach (var item in TestJson.Children(map, "challenges"))
            {
                var row = TestJson.Object(item);
                rows.Add(new ChallengeDto
                {
                    id = TestJson.Str(row, "id", string.Empty),
                    genre = TestJson.Str(row, "genre", string.Empty),
                    seed = TestJson.Int(row, "seed"),
                    width = TestJson.Int(row, "width"),
                    height = TestJson.Int(row, "height"),
                    rows = Strings(row, "rows"),
                    gems = Strings(row, "gems"),
                    sources = TestJson.Str(row, "sources", string.Empty),
                    sinks = TestJson.Str(row, "sinks", string.Empty),
                    target = TestJson.Int(row, "target"),
                    hill = TestJson.Int(row, "hill"),
                    waves = Strings(row, "waves"),
                    bolts = TestJson.Int(row, "bolts"),
                });
            }
            dto.challenges = rows.ToArray();

            var problems = new List<string>();
            bool ok = ChallengeTable.TryBuild(dto, out var table, problems);

            Assert.IsTrue(ok && problems.Count == 0,
                          "challenges.json must read clean:\n  " + string.Join("\n  ", problems));
            return table;
        }

        static string[] Strings(Dictionary<string, object> map, string key)
        {
            if (!map.ContainsKey(key)) return new string[0];
            var list = new List<string>();
            foreach (var item in TestJson.Array(map[key])) list.Add(item as string ?? string.Empty);
            return list.ToArray();
        }

        static ChallengeDto Row(string id, string genre, int w, int h, string[] rows, string[] waves,
                                int hill = 5, int bolts = 1)
            => new ChallengeDto { id = id, genre = genre, width = w, height = h, rows = rows,
                                  waves = waves, hill = hill, bolts = bolts, seed = 7 };

        static ChallengeTableDto Table(params ChallengeDto[] rows)
            => new ChallengeTableDto
            {
                schemaVersion = ChallengeTable.Version,
                line = new ChallengeLineDto { damage = 1, health = 3, strike = 1 },
                challenges = rows,
            };

        static ChallengeDefinition Pairs4(string[] waves = null, int hill = 5)
        {
            var problems = new List<string>();
            ChallengeTable.TryBuild(Table(Row("t_pairs", "pairs", 2, 2, new[] { "rg", "gr" },
                                             waves ?? new[] { "0 r1" }, hill, 1)),
                                    out var table, problems);
            Assert.AreEqual(0, problems.Count, string.Join("; ", problems));
            return table.Find("t_pairs");
        }

        // ------------------------------------------------------------------ the spent names
        /// <summary>
        /// A withdrawn genre spelling and a withdrawn deal id are refused by name (invariant 5f),
        /// with a message that says why rather than "unknown", so a re-mint is caught at read.
        /// </summary>
        [Test]
        public void ARetiredGenreSpellingIsRefusedAsRetired()
        {
            foreach (var spelling in ChallengeGenres.Retired)
            {
                Assert.IsFalse(ChallengeGenres.TryParse(spelling, out _), $"'{spelling}' still parses");
                Assert.IsTrue(ChallengeGenres.IsRetired(spelling));

                var problems = new List<string>();
                ChallengeTable.TryBuild(Table(Row("t", spelling, 2, 2, new[] { "rg", "gr" }, new[] { "0 r1" })), out var table, problems);
                Assert.AreEqual(0, table.Count);
                Assert.IsTrue(problems.Exists(p => p.Contains("withdrawn")), string.Join("; ", problems));
            }

            Assert.IsFalse(ChallengeGenres.IsRetired("pairs"));
            Assert.IsFalse(ChallengeTable.IsRetiredTierId("bronze"));
        }

        // ------------------------------------------------------------------ the registry
        [Test]
        public void EveryGenreIsRegistered()
        {
            Assert.AreEqual(ChallengeGenres.Count, Enum.GetValues(typeof(ChallengeGenre)).Length,
                            "a genre added to the enum needs a content spelling");

            foreach (ChallengeGenre genre in Enum.GetValues(typeof(ChallengeGenre)))
            {
                Assert.IsTrue(ChallengePuzzles.Knows(genre), $"{genre} has no puzzle registered");
                Assert.IsTrue(ChallengeGenres.TryParse(ChallengeGenres.NameOf(genre), out var back) && back == genre,
                              $"{genre} does not round-trip through its spelling");
            }
        }

        [Test]
        public void TheShippedSlateHasOneOfEveryGenre()
        {
            var table = Shipped();
            var seen = new HashSet<ChallengeGenre>();

            foreach (var row in table.All)
                Assert.IsTrue(seen.Add(row.Genre), $"{row.Genre} is shipped twice; the first slate is one of each");

            Assert.AreEqual(ChallengeGenres.Count, seen.Count, "every genre ships one challenge");
        }

        // ------------------------------------------------------------------ the reader
        [Test]
        public void AnUnknownGenreIsRefusedByName()
        {
            var problems = new List<string>();
            ChallengeTable.TryBuild(Table(Row("t", "chess", 2, 2, new[] { "rg", "gr" }, new[] { "0 r1" })),
                                    out var table, problems);

            Assert.AreEqual(0, table.Count);
            Assert.IsTrue(problems.Exists(p => p.Contains("chess")), string.Join("; ", problems));
        }

        [Test]
        public void AWaveNamingNoColourIsRefused()
        {
            var problems = new List<string>();
            ChallengeTable.TryBuild(Table(Row("t", "pairs", 2, 2, new[] { "rg", "gr" }, new[] { "0 p2" })),
                                    out var table, problems);

            Assert.AreEqual(0, table.Count);
            Assert.IsTrue(problems.Exists(p => p.Contains("p2")), string.Join("; ", problems));
        }

        [Test]
        public void WavesMustClimb()
        {
            var problems = new List<string>();
            ChallengeTable.TryBuild(Table(Row("t", "pairs", 2, 2, new[] { "rg", "gr" }, new[] { "3 r1", "3 g1" })),
                                    out _, problems);

            Assert.IsTrue(problems.Exists(p => p.Contains("out of order")), string.Join("; ", problems));
        }

        [Test]
        public void ADuplicatedIdFailsTheFile()
        {
            var problems = new List<string>();
            bool ok = ChallengeTable.TryBuild(Table(Row("t", "pairs", 2, 2, new[] { "rg", "gr" }, new[] { "0 r1" }),
                                                    Row("t", "pairs", 2, 2, new[] { "rg", "gr" }, new[] { "0 r1" })),
                                              out _, problems);

            Assert.IsFalse(ok);
        }

        [Test]
        public void AFileFromTheFutureIsRefusedWhole()
        {
            var dto = Table(Row("t", "pairs", 2, 2, new[] { "rg", "gr" }, new[] { "0 r1" }));
            dto.schemaVersion = ChallengeTable.Version + 1;

            var problems = new List<string>();
            Assert.IsFalse(ChallengeTable.TryBuild(dto, out var table, problems));
            Assert.IsTrue(table.IsEmpty);
        }

        [Test]
        public void ABadRowCostsThatRowAndNotTheSlate()
        {
            var problems = new List<string>();
            bool ok = ChallengeTable.TryBuild(Table(Row("good", "pairs", 2, 2, new[] { "rg", "gr" }, new[] { "0 r1" }),
                                                    Row("odd", "pairs", 3, 1, new[] { "rgb" }, new[] { "0 r1" })),
                                              out var table, problems);

            Assert.IsTrue(ok);
            Assert.AreEqual(1, table.Count);
            Assert.IsNotNull(table.Find("good"));
            Assert.IsTrue(problems.Exists(p => p.Contains("odd")));
        }

        [Test]
        public void LocKeysDeriveFromTheId()
        {
            var def = Pairs4();
            Assert.AreEqual("challenge.t_pairs.name", def.NameKey);
            Assert.AreEqual("challenge.t_pairs.blurb", def.BlurbKey);
        }

        // ------------------------------------------------------------------ the hill
        [Test]
        public void AWardFiresOnlyAtItsOwnColour()
        {
            var hill = new ChallengeHill(new ChallengeLine(1, 3, 1), 5,
                                         new[] { new ChallengeWave(0, new[] { new ChallengeRaiderSpec(1, 1) }) });

            hill.Feed(0, 3);
            var events = new List<ChallengeEvent>();
            hill.Resolve(events);

            Assert.AreEqual(3, hill.Wards[0].Banked, "red bolts bank with no red raider to fire at");
            Assert.AreEqual(1, hill.Standing);
            Assert.IsFalse(events.Exists(e => e.Kind == ChallengeEventKind.Bolt));
        }

        [Test]
        public void BankedBoltsFireTheTurnATargetAppears()
        {
            var hill = new ChallengeHill(new ChallengeLine(1, 3, 1), 5,
                                         new[] { new ChallengeWave(1, new[] { new ChallengeRaiderSpec(0, 2) }) });

            hill.Feed(0, 2);
            hill.Resolve(null);                       // turn 1: musters the red raider after firing
            Assert.AreEqual(2, hill.Wards[0].Banked);
            Assert.AreEqual(1, hill.Standing);

            var events = new List<ChallengeEvent>();
            hill.Resolve(events);                     // turn 2: the bank lands
            Assert.AreEqual(0, hill.Wards[0].Banked);
            Assert.AreEqual(0, hill.Standing);
            Assert.AreEqual(2, events.FindAll(e => e.Kind == ChallengeEventKind.Bolt).Count);
            Assert.IsTrue(events.Exists(e => e.Kind == ChallengeEventKind.Bolt && e.Ended));
        }

        [Test]
        public void ARaiderStrikesFromTheTurnItReachesTheLine()
        {
            var hill = new ChallengeHill(new ChallengeLine(1, 3, 1), 2,
                                         new[] { new ChallengeWave(0, new[] { new ChallengeRaiderSpec(2, 9) }) });

            hill.Resolve(null);
            Assert.AreEqual(3, hill.Wards[2].Health, "one step short of the line, no blow yet");

            hill.Resolve(null);
            Assert.AreEqual(2, hill.Wards[2].Health, "it arrives and strikes in the same turn");

            hill.Resolve(null);
            hill.Resolve(null);
            Assert.IsFalse(hill.Wards[2].Alive);
            Assert.AreEqual(3, hill.WardsStanding);
            Assert.IsTrue(hill.LineStanding);
        }

        [Test]
        public void AFallenWardsRaiderTurnsOnTheNearestStandingOne()
        {
            var hill = new ChallengeHill(new ChallengeLine(1, 1, 1), 1,
                                         new[] { new ChallengeWave(0, new[] { new ChallengeRaiderSpec(3, 9) }) });

            hill.Resolve(null);                       // yellow falls
            Assert.IsFalse(hill.Wards[3].Alive);

            hill.Resolve(null);                       // blue, the nearest post, takes the next blow
            Assert.IsFalse(hill.Wards[2].Alive);
            Assert.IsTrue(hill.Wards[1].Alive);
        }

        [Test]
        public void TheLineFallsWhenTheLastWardDoes()
        {
            var hill = new ChallengeHill(new ChallengeLine(1, 1, 1), 1,
                                         new[] { new ChallengeWave(0, new[] { new ChallengeRaiderSpec(0, 9) }) });

            for (int i = 0; i < 4; i++) hill.Resolve(null);
            Assert.IsFalse(hill.LineStanding);
        }

        [Test]
        public void TheSolvingMoveWinsBeforeTheHillWalks()
        {
            // One pair, and a raider standing at the line that would fell the last ward this turn.
            var def = Pairs4(new[] { "0 r9" }, 1);
            var run = new ChallengeRun(def, new ChallengeLine(1, 2, 1));

            run.Play(ChallengeInput.Tap(0));          // r
            run.Play(ChallengeInput.Tap(3));          // r: a pair, and the hill walks
            Assert.AreEqual(ChallengeState.Playing, run.State);
            Assert.AreEqual(1, run.Hill.Wards[0].Health, "the raider arrives and strikes once; red stands");

            run.Play(ChallengeInput.Tap(1));
            var report = run.Play(ChallengeInput.Tap(2));

            Assert.AreEqual(ChallengeState.Won, report.State);
            Assert.IsFalse(report.Walked, "the winning move never lets the raider strike");
        }

        [Test]
        public void ARunIsDecidedOnce()
        {
            var def = Pairs4(new[] { "0 r9" }, 1);
            var run = new ChallengeRun(def, new ChallengeLine(1, 1, 1));

            // Miss on purpose until the line falls: r at 0, g at 1.
            for (int i = 0; i < 8 && run.State == ChallengeState.Playing; i++)
            {
                run.Play(ChallengeInput.Tap(0));
                run.Play(ChallengeInput.Tap(1));
            }

            Assert.AreEqual(ChallengeState.Lost, run.State);

            var after = run.Play(ChallengeInput.Tap(0));
            Assert.IsNull(after.Move, "a decided run refuses every input");
            Assert.AreEqual(ChallengeState.Lost, after.State);
        }

        // ------------------------------------------------------------------ pairs
        [Test]
        public void AFirstFlipIsFreeAndAMissStillCostsATurn()
        {
            var def = Pairs4(new[] { "9 r1" });
            var run = new ChallengeRun(def, new ChallengeLine(1, 3, 1));

            Assert.IsFalse(run.Play(ChallengeInput.Tap(0)).Move.Turn);
            var miss = run.Play(ChallengeInput.Tap(1));
            Assert.IsTrue(miss.Move.Turn);
            Assert.AreEqual(0, miss.Move.Feeds.Count);
            Assert.AreEqual(1, run.Turns);

            var pairs = (PairsPuzzle)run.Puzzle;
            Assert.AreEqual(PairsPuzzle.Face.Hidden, pairs.FaceAt(0));
            Assert.AreEqual(PairsPuzzle.Face.Hidden, pairs.FaceAt(1));
        }

        // ------------------------------------------------------------------ the glade
        /// <summary>
        /// A critter lit in its colour feeds its turret every turn it stays lit — the pipes'
        /// sentence, said of the glade — and the light is the real board's: a red crystal wakes
        /// a red critter through a conduit turned home, and the turn that wakes it pays the red
        /// turret. The board is a 3x1 the mode's own parser reads.
        /// </summary>
        [Test]
        public void ALitCritterFeedsItsTurretEveryTurnItStaysLit()
        {
            var problems = new List<string>();
            var dto = Row("g", "glade", 3, 1, new[] { "*E#R/0 -EW/1 @W#R/0" }, new[] { "0 r1" }, hill: 6);
            ChallengeTable.TryBuild(Table(dto), out var table, problems);
            Assert.AreEqual(0, problems.Count, string.Join("; ", problems));

            var run = new ChallengeRun(table.Find("g"), new ChallengeLine(1, 3, 1));
            var glade = (GladePuzzle)run.Puzzle;
            Assert.AreEqual(0, glade.LampsLit);

            // A one-armed crystal is not inert (four angles, four pictures), so it turns and
            // costs a step — and wakes nobody, so it feeds nothing.
            var idle = run.Play(ChallengeInput.Tap(0));
            Assert.IsFalse(idle.Move.Refused);
            Assert.IsTrue(idle.Move.Turn);
            Assert.AreEqual(0, idle.Move.Feeds.Count, "a dark critter feeds nothing");
            for (int k = 0; k < 3; k++) run.Play(ChallengeInput.Tap(0));   // and back home

            var wake = run.Play(ChallengeInput.Tap(1));
            Assert.IsFalse(wake.Move.Refused);
            Assert.IsTrue(wake.Move.Turn, "a turn costs a step");
            Assert.AreEqual(1, wake.Move.Feeds.Count, "the woken critter fed its lane");
            Assert.AreEqual(0, wake.Move.Feeds[0].Colour, "red light is the red turret");
            Assert.AreEqual(ChallengeState.Won, wake.State, "the last critter waking wins before the hill walks");
        }

        [Test]
        public void AGladeIsAuthoredSolvedOrRefused()
        {
            var problems = new List<string>();
            // The conduit's solved arms are N-S: nothing meets them, and zeroed it wakes nobody.
            ChallengeTable.TryBuild(Table(Row("g", "glade", 3, 1, new[] { "*E#R/0 -NS/1 @W#R/0" }, new[] { "0 r1" })),
                                    out var table, problems);
            Assert.AreEqual(0, table.Count);
            Assert.IsTrue(problems.Exists(p => p.Contains("arm")), string.Join("; ", problems));

            // A critter wanting a light no turret fires is refused by name.
            problems.Clear();
            ChallengeTable.TryBuild(Table(Row("g", "glade", 3, 1, new[] { "*E#R/0 -EW/1 @W#M/0" }, new[] { "0 r1" })),
                                    out table, problems);
            Assert.AreEqual(0, table.Count);
            Assert.IsTrue(problems.Exists(p => p.Contains("no turret")), string.Join("; ", problems));

            // The pipes' edge strings are refused on any row that still carries them (5f).
            problems.Clear();
            var stale = Row("g", "glade", 3, 1, new[] { "*E#R/0 -EW/1 @W#R/0" }, new[] { "0 r1" });
            stale.sources = "r..";
            ChallengeTable.TryBuild(Table(stale), out table, problems);
            Assert.AreEqual(0, table.Count);
            Assert.IsTrue(problems.Exists(p => p.Contains("sources")), string.Join("; ", problems));
        }

        // ------------------------------------------------------------------ merge
        [Test]
        public void AMergeFeedsTheColourOfTheRankItMade()
        {
            var problems = new List<string>();
            var dto = Row("g", "merge", 4, 4, new[] { "1..1", "....", "....", "...." }, new[] { "9 r1" });
            dto.target = 3;
            ChallengeTable.TryBuild(Table(dto), out var table, problems);
            Assert.AreEqual(0, problems.Count, string.Join("; ", problems));

            var run = new ChallengeRun(table.Find("g"), new ChallengeLine(1, 3, 1));
            var merge = (MergePuzzle)run.Puzzle;

            Assert.IsTrue(run.Play(ChallengeInput.Swipe(0, 1)).Move.Refused, "nothing moves up");

            var left = run.Play(ChallengeInput.Swipe(-1, 0));
            Assert.IsTrue(left.Move.Turn);
            Assert.AreEqual(2, merge.RankAt(0));
            Assert.AreEqual(1, left.Move.Feeds.Count);
            Assert.AreEqual(MergePuzzle.ColourOf(2), left.Move.Feeds[0].Colour);
            Assert.IsTrue(merge.LastDealt >= 0, "a slide deals one gem");
        }

        /// <summary>
        /// The trace the view animates from: every gem that moved or met another, with both
        /// ends and the rank it carried, so a slide can be drawn rather than repainted.
        /// </summary>
        [Test]
        public void ASlideSaysWhereEveryGemWentAndWhichPairMet()
        {
            var problems = new List<string>();
            var dto = Row("g", "merge", 4, 2, new[] { "1..1", "2..." }, new[] { "9 r1" });
            dto.target = 4;
            ChallengeTable.TryBuild(Table(dto), out var table, problems);
            Assert.AreEqual(0, problems.Count, string.Join("; ", problems));

            var run = new ChallengeRun(table.Find("g"), new ChallengeLine(1, 3, 1));
            var merge = (MergePuzzle)run.Puzzle;

            Assert.AreEqual(0, merge.LastSlides.Count, "nothing has slid before the first move");

            var left = run.Play(ChallengeInput.Swipe(-1, 0));
            Assert.IsTrue(left.Move.Turn);

            // The two ones met at the left wall: the standing one is listed although it did
            // not move, the travelling one crossed three cells, both carry the rank they had
            // on the way and both name the cell they met in. The two on the row below stood
            // still and met nothing, so it is not in the trace at all.
            var slides = merge.LastSlides;
            Assert.AreEqual(2, slides.Count, "the two halves of the merge, and nothing else");
            Assert.AreEqual(0, slides[0].From);
            Assert.AreEqual(0, slides[0].To);
            Assert.IsFalse(slides[0].Moved);
            Assert.AreEqual(3, slides[1].From);
            Assert.AreEqual(0, slides[1].To);
            Assert.IsTrue(slides[1].Moved);
            Assert.AreEqual(1, slides[0].Rank);
            Assert.AreEqual(1, slides[1].Rank);
            Assert.AreEqual(2, merge.RankAt(0), "and the cell they met in holds the rank they made");
            Assert.AreEqual(1, merge.LastMerged.Count);
            Assert.AreEqual(0, merge.LastMerged[0]);

            // The deal is separate from the slide and says what it dealt.
            Assert.IsTrue(merge.LastDealt >= 0);
            Assert.IsTrue(merge.LastDealtRank == 1 || merge.LastDealtRank == 2);
            Assert.AreEqual(merge.LastDealtRank, merge.RankAt(merge.LastDealt));
            for (int i = 0; i < slides.Count; i++)
                Assert.AreNotEqual(merge.LastDealt, slides[i].To, "a gem is never dealt onto a cell a slide filled");

            // A refused slide leaves the trace empty rather than stale.
            var up = run.Play(ChallengeInput.Swipe(0, 1));
            if (up.Move.Refused) Assert.AreEqual(0, merge.LastSlides.Count);
        }

        // ------------------------------------------------------------------ sokoban
        [Test]
        public void APushIntoAWallIsRefusedAndASeatedGemFiresEveryStep()
        {
            var problems = new List<string>();
            var dto = Row("k", "sokoban", 5, 3, new[] { "#####", "#@.R#", "#####" }, new[] { "9 r1" });
            dto.gems = new[] { ".....", "..r..", "....." };
            ChallengeTable.TryBuild(Table(dto), out var table, problems);
            Assert.AreEqual(0, problems.Count, string.Join("; ", problems));

            var run = new ChallengeRun(table.Find("k"), new ChallengeLine(1, 3, 1));
            Assert.IsTrue(run.Play(ChallengeInput.Swipe(0, 1)).Move.Refused);

            var push = run.Play(ChallengeInput.Swipe(1, 0));
            Assert.AreEqual(ChallengeState.Won, push.State, "one push seats the gem");
        }

        // ------------------------------------------------------------------ the shipped four
        [Test]
        public void ShippedPairsIsWonByAPlayerWithAMemory()
        {
            var table = Shipped();
            var run = new ChallengeRun(table.Find("d01_pairs"), table.Line);
            var pairs = (PairsPuzzle)run.Puzzle;

            var known = new Dictionary<int, List<int>>();
            int cells = pairs.Width * pairs.Height;
            int cursor = 0;
            int guard = 0;

            while (run.State == ChallengeState.Playing && guard++ < 200)
            {
                int a = -1, b = -1;
                foreach (var kv in known)
                {
                    kv.Value.RemoveAll(c => pairs.FaceAt(c) != PairsPuzzle.Face.Hidden);
                    if (kv.Value.Count >= 2) { a = kv.Value[0]; b = kv.Value[1]; break; }
                }

                if (a < 0)
                {
                    while (cursor < cells && (pairs.FaceAt(cursor) != PairsPuzzle.Face.Hidden || Seen(known, cursor))) cursor++;
                    a = cursor;
                    Learn(known, a, pairs.ColourAt(a));

                    int partner = -1;
                    foreach (int c in known[pairs.ColourAt(a)])
                        if (c != a && pairs.FaceAt(c) == PairsPuzzle.Face.Hidden) { partner = c; break; }

                    if (partner >= 0) b = partner;
                    else
                    {
                        int next = cursor + 1;
                        while (next < cells && (pairs.FaceAt(next) != PairsPuzzle.Face.Hidden || Seen(known, next))) next++;
                        b = next;
                        Learn(known, b, pairs.ColourAt(b));
                    }
                }

                run.Play(ChallengeInput.Tap(a));
                run.Play(ChallengeInput.Tap(b));
            }

            Won(run, "d01_pairs");
        }

        static bool Seen(Dictionary<int, List<int>> known, int cell)
        {
            foreach (var kv in known) if (kv.Value.Contains(cell)) return true;
            return false;
        }

        static void Learn(Dictionary<int, List<int>> known, int cell, int colour)
        {
            if (!known.TryGetValue(colour, out var list)) known[colour] = list = new List<int>();
            if (!list.Contains(cell)) list.Add(cell);
        }

        [Test]
        public void ShippedGladeIsWonByTurningEachTileHome()
        {
            var table = Shipped();
            var run = new ChallengeRun(table.Find("d08_glade"), table.Line);
            var glade = (GladePuzzle)run.Puzzle;

            int expected = glade.Board.TurnsToSolution;
            Assert.Greater(expected, 0);

            // A player wakes one critter at a time, starting with the colour whose raiders are
            // nearest (the lanes in wave order): its solved network, turned home crystal-first.
            // The wake order is what the waves are timed against, so it is printed beside the
            // margin (the Push route's seat order, said of a glade).
            var network = new List<int>();
            var awake = new HashSet<int>();
            for (int lane = 0; lane < ChallengeColours.Count && run.State == ChallengeState.Playing; lane++)
            {
                for (int lamp = 0; lamp < glade.Board.C.Length && run.State == ChallengeState.Playing; lamp++)
                {
                    if (glade.Board.C[lamp].kind != Kind.Lamp || GladePuzzle.LaneOf(glade.Board.C[lamp].colour) != lane) continue;

                    glade.SolutionNetwork(lamp, network);
                    Assert.Greater(network.Count, 1, $"critter {lamp} has no network in the solution");

                    foreach (int cell in network)
                    {
                        int taps = glade.Board.TurnsOwed(cell);
                        for (int t = 0; t < taps && run.State == ChallengeState.Playing; t++)
                            Assert.IsFalse(run.Play(ChallengeInput.Tap(cell)).Move.Refused, $"tap on {cell} refused");
                    }

                    for (int i = 0; i < glade.Board.C.Length; i++)
                        if (glade.Board.C[i].kind == Kind.Lamp && glade.Board.Lit[i] && awake.Add(i))
                            Console.WriteLine($"d08_glade: critter at {i % glade.Width},{i / glade.Width} " +
                                              $"({ChallengeColours.LetterOf(GladePuzzle.LaneOf(glade.Board.C[i].colour))}) awake by turn {run.Turns}");
                }
            }

            Won(run, "d08_glade");
            Assert.LessOrEqual(run.Turns, expected, "turning each tile home never costs more than the solution's distance");
        }

        [Test]
        public void ShippedMergeIsWonByACornerPlayer()
        {
            var table = Shipped();
            var run = new ChallengeRun(table.Find("d05_merge"), table.Line);

            var order = new[] { ChallengeInput.Swipe(0, -1), ChallengeInput.Swipe(-1, 0),
                                ChallengeInput.Swipe(1, 0), ChallengeInput.Swipe(0, 1) };
            int guard = 0;

            while (run.State == ChallengeState.Playing && guard++ < 2000)
            {
                bool moved = false;
                for (int i = 0; i < order.Length && !moved; i++)
                {
                    var report = run.Play(order[i]);
                    moved = report.Move != null && !report.Move.Refused;
                }
                Assert.IsTrue(moved, "no slide was accepted on a board the rules say is not stuck");
            }

            Won(run, "d05_merge");
        }

        [Test]
        public void ShippedSokobanIsWonByItsAuthoredRoute()
        {
            var table = Shipped();
            var run = new ChallengeRun(table.Find("d06_sokoban"), table.Line);

            // Found by breadth-first search over the authored board; the shortest route.
            const string route = "DRRURULLDRRRRURDLDRDLLLULURRRURDD";

            foreach (char step in route)
            {
                if (run.State != ChallengeState.Playing) break;
                var input = step == 'U' ? ChallengeInput.Swipe(0, 1)
                          : step == 'D' ? ChallengeInput.Swipe(0, -1)
                          : step == 'L' ? ChallengeInput.Swipe(-1, 0)
                          : ChallengeInput.Swipe(1, 0);
                Assert.IsFalse(run.Play(input).Move.Refused, $"the route's '{step}' was refused");
            }

            Won(run, "d06_sokoban");

            // The hill walks once per move except the winning one (it never gets that step).
            Assert.AreEqual(route.Length - 1, run.Turns);
        }

        static void Won(ChallengeRun run, string id)
        {
            int health = 0;
            for (int i = 0; i < run.Hill.Wards.Count; i++) health += run.Hill.Wards[i].Health;

            Console.WriteLine($"{id}: won after the hill walked {run.Turns} turn(s), {run.Hill.WardsStanding} ward(s) standing on " +
                              $"{health} of {run.Hill.Wards.Count * run.Hill.Line.Health} health, " +
                              $"{run.Hill.Standing} raider(s) still walking");

            Assert.AreEqual(ChallengeState.Won, run.State, $"{id} was not won");
            Assert.IsTrue(run.Hill.LineStanding, $"{id} was won with no ward standing");
        }
    }
}
