using System;
using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Challenges;
using GlimmerGrove.Content;
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
    /// it was, and prints the turns it took and the wards it had left - the margin the owner
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
            ChallengeTable.TryBuild(Table(Row("t_pairs", "pairs", 2, 2, new[] { "r1 g1", "g1 r1" },
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
                ChallengeTable.TryBuild(Table(Row("t", spelling, 2, 2, new[] { "r1 g1", "g1 r1" }, new[] { "0 r1" })), out var table, problems);
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

        /// <summary>
        /// Every genre deals something, and a genre may ship any number of levels: the first
        /// slate was one of each, and a genre is a ladder of rows the calendar walks
        /// (invariant 56f) - thirty-one glades since 2026-09-26.
        /// </summary>
        [Test]
        public void TheShippedSlateDealsEveryGenre()
        {
            var table = Shipped();
            var ids = new HashSet<string>();

            foreach (var row in table.All)
                Assert.IsTrue(ids.Add(row.Id), $"{row.Id} is shipped twice");

            for (int g = 0; g < ChallengeGenres.Count; g++)
                Assert.Greater(table.RowsOf((ChallengeGenre)g).Count, 0, $"{(ChallengeGenre)g} ships no level");
        }

        // ------------------------------------------------------------------ the reader
        [Test]
        public void AnUnknownGenreIsRefusedByName()
        {
            var problems = new List<string>();
            ChallengeTable.TryBuild(Table(Row("t", "chess", 2, 2, new[] { "r1 g1", "g1 r1" }, new[] { "0 r1" })),
                                    out var table, problems);

            Assert.AreEqual(0, table.Count);
            Assert.IsTrue(problems.Exists(p => p.Contains("chess")), string.Join("; ", problems));
        }

        [Test]
        public void AWaveNamingNoColourIsRefused()
        {
            var problems = new List<string>();
            ChallengeTable.TryBuild(Table(Row("t", "pairs", 2, 2, new[] { "r1 g1", "g1 r1" }, new[] { "0 p2" })),
                                    out var table, problems);

            Assert.AreEqual(0, table.Count);
            Assert.IsTrue(problems.Exists(p => p.Contains("p2")), string.Join("; ", problems));
        }

        [Test]
        public void WavesMustClimb()
        {
            var problems = new List<string>();
            ChallengeTable.TryBuild(Table(Row("t", "pairs", 2, 2, new[] { "r1 g1", "g1 r1" }, new[] { "3 r1", "3 g1" })),
                                    out _, problems);

            Assert.IsTrue(problems.Exists(p => p.Contains("out of order")), string.Join("; ", problems));
        }

        [Test]
        public void ADuplicatedIdFailsTheFile()
        {
            var problems = new List<string>();
            bool ok = ChallengeTable.TryBuild(Table(Row("t", "pairs", 2, 2, new[] { "r1 g1", "g1 r1" }, new[] { "0 r1" }),
                                                    Row("t", "pairs", 2, 2, new[] { "r1 g1", "g1 r1" }, new[] { "0 r1" })),
                                              out _, problems);

            Assert.IsFalse(ok);
        }

        [Test]
        public void AFileFromTheFutureIsRefusedWhole()
        {
            var dto = Table(Row("t", "pairs", 2, 2, new[] { "r1 g1", "g1 r1" }, new[] { "0 r1" }));
            dto.schemaVersion = ChallengeTable.Version + 1;

            var problems = new List<string>();
            Assert.IsFalse(ChallengeTable.TryBuild(dto, out var table, problems));
            Assert.IsTrue(table.IsEmpty);
        }

        [Test]
        public void ABadRowCostsThatRowAndNotTheSlate()
        {
            var problems = new List<string>();
            bool ok = ChallengeTable.TryBuild(Table(Row("good", "pairs", 2, 2, new[] { "r1 g1", "g1 r1" }, new[] { "0 r1" }),
                                                    Row("odd", "pairs", 3, 1, new[] { "r1 g1 b1" }, new[] { "0 r1" })),
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

        /// <summary>
        /// A volley - a steady source's turn of fire - lands this turn or is spent. Banked, a
        /// glade critter woken early stockpiled a turret against every raider to come and the
        /// hill stood empty for the rest of the run (the owner, 2026-09-26).
        /// </summary>
        [Test]
        public void AVolleyFiresThisTurnOrIsSpent()
        {
            var hill = new ChallengeHill(new ChallengeLine(1, 3, 1), 5,
                                         new[] { new ChallengeWave(1, new[] { new ChallengeRaiderSpec(0, 2) }) });

            hill.Volley(0, 3);
            hill.Resolve(null);                       // turn 1: nothing red to shoot; the red raider musters
            Assert.AreEqual(0, hill.Wards[0].Banked, "a volley never banks");
            Assert.AreEqual(1, hill.Standing);

            var events = new List<ChallengeEvent>();
            hill.Resolve(events);                     // turn 2: nothing fed, nothing fires
            Assert.IsFalse(events.Exists(e => e.Kind == ChallengeEventKind.Bolt), "the spent volley did not come back");

            hill.Volley(0, 1);
            events.Clear();
            hill.Resolve(events);                     // turn 3: one bolt lands
            Assert.AreEqual(1, events.FindAll(e => e.Kind == ChallengeEventKind.Bolt).Count);
            Assert.AreEqual(1, hill.Raiders[0].Health);
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

        static ChallengeDefinition PairsRow(int w, int h, string[] rows, string[] waves = null, int hill = 9, int bolts = 1)
        {
            var problems = new List<string>();
            ChallengeTable.TryBuild(Table(Row("p", "pairs", w, h, rows, waves ?? new[] { "0 r1" }, hill, bolts)),
                                    out var table, problems);
            Assert.AreEqual(0, problems.Count, string.Join("; ", problems));
            return table.Find("p");
        }

        /// <summary>
        /// <b>A pair is a gem, never a colour</b> (56m): two reds of different stones are a miss,
        /// and the same stone twice is a pair that feeds the red turret.
        /// </summary>
        [Test]
        public void APairIsTheSameGemNotTheSameColour()
        {
            var run = new ChallengeRun(PairsRow(2, 2, new[] { "r1 r2", "r2 r1" }), new ChallengeLine(1, 3, 1));
            var pairs = (PairsPuzzle)run.Puzzle;
            Assert.AreEqual(2, pairs.Pairs);

            run.Play(ChallengeInput.Tap(0));
            var miss = run.Play(ChallengeInput.Tap(1));
            Assert.IsTrue(miss.Move.Turn);
            Assert.AreEqual(0, miss.Move.Feeds.Count, "a red heart and a red drop are not a pair");

            run.Play(ChallengeInput.Tap(0));
            var pair = run.Play(ChallengeInput.Tap(3));
            Assert.AreEqual(1, pair.Move.Feeds.Count);
            Assert.AreEqual(0, pair.Move.Feeds[0].Colour, "a pair of red stones feeds the red turret");
            Assert.IsTrue(pair.Move.Feeds[0].Banks, "a pair is a burst, and a burst banks");
            Assert.AreEqual(PairsPuzzle.Face.Matched, pairs.FaceAt(0));
        }

        /// <summary>
        /// Pairs made back to back are a combo: the n-th in a row pays bolts x min(n, cap), and a
        /// miss starts the count again.
        /// </summary>
        [Test]
        public void PairsMadeBackToBackAreACombo()
        {
            var run = new ChallengeRun(PairsRow(6, 2, new[] { "r1 r1 g1 g1 b1 b1", "y1 y2 y1 y2 r2 r2" }, bolts: 2),
                                       new ChallengeLine(1, 3, 1));
            var pairs = (PairsPuzzle)run.Puzzle;

            int Pay(int a, int b)
            {
                run.Play(ChallengeInput.Tap(a));
                var report = run.Play(ChallengeInput.Tap(b));
                return report.Move.Feeds.Count == 0 ? 0 : report.Move.Feeds[0].Bolts;
            }

            Assert.AreEqual(2, Pay(0, 1), "the first pair pays its bolts");
            Assert.AreEqual(4, Pay(2, 3), "the second in a row pays twice");
            Assert.AreEqual(6, Pay(4, 5), "the third pays three times");
            Assert.AreEqual(3, pairs.Streak);
            Assert.AreEqual(0, Pay(6, 7), "a miss");
            Assert.AreEqual(0, pairs.Streak, "a miss ends the combo");
            Assert.AreEqual(2, Pay(10, 11), "and the next pair is a first again");
            Assert.AreEqual(1, pairs.LastCombo);
        }

        /// <summary>
        /// A cursed stone ends the turn the moment it is turned, first flip or second: a card
        /// already up goes back down, the combo breaks, and the hill walks a step more than a turn.
        /// </summary>
        [Test]
        public void ACursedStoneEndsTheTurnAndWalksTheHillAnExtraStep()
        {
            var run = new ChallengeRun(PairsRow(5, 1, new[] { "r1 o r1 g1 g1" }, new[] { "0 r9" }, hill: 9),
                                       new ChallengeLine(1, 3, 1));
            var pairs = (PairsPuzzle)run.Puzzle;
            Assert.AreEqual(1, pairs.Curses);
            Assert.AreEqual(2, pairs.Pairs);

            Assert.IsFalse(run.Play(ChallengeInput.Tap(0)).Move.Turn, "a first flip is free");
            var cursed = run.Play(ChallengeInput.Tap(1));
            Assert.IsTrue(cursed.Move.Turn);
            Assert.AreEqual(1, cursed.Move.Stumbles);
            Assert.AreEqual(2, run.Turns, "the hill walked the turn and the stumble");
            Assert.AreEqual(7, run.Hill.Raiders[0].Distance, "two steps down a hill of nine");
            Assert.AreEqual(PairsPuzzle.Face.Hidden, pairs.FaceAt(0), "the card that was up goes back down");
            Assert.AreEqual(PairsPuzzle.Face.Hidden, pairs.FaceAt(1), "and so does the curse");
            Assert.IsTrue(pairs.LastCursed);
            Assert.AreEqual(-1, pairs.First);

            // Turned first, it ends the turn on its own.
            var alone = run.Play(ChallengeInput.Tap(1));
            Assert.IsTrue(alone.Move.Turn);
            Assert.AreEqual(-1, pairs.LastA);
            Assert.AreEqual(4, run.Turns);

            // It never pairs, and the board is solved without it.
            run.Play(ChallengeInput.Tap(0));
            run.Play(ChallengeInput.Tap(2));
            run.Play(ChallengeInput.Tap(3));
            Assert.AreEqual(ChallengeState.Won, run.Play(ChallengeInput.Tap(4)).State);
        }

        /// <summary>A row is tokens; a bare colour, an odd stone and a fourth curse are each refused by name.</summary>
        [Test]
        public void APairsRowIsRefusedWithAReason()
        {
            string Fault(int w, int h, params string[] rows)
            {
                var problems = new List<string>();
                ChallengeTable.TryBuild(Table(Row("p", "pairs", w, h, rows, new[] { "0 r1" })), out var table, problems);
                Assert.AreEqual(0, table.Count, string.Join("; ", problems));
                return string.Join("; ", problems);
            }

            StringAssert.Contains("a colour and not a gem", Fault(2, 1, "r g"));
            StringAssert.Contains("gem token", Fault(2, 1, "rg"));
            StringAssert.Contains("cannot all pair", Fault(3, 1, "r1 r1 r1"));
            StringAssert.Contains("not a gem", Fault(2, 1, "r7 r7"));
            StringAssert.Contains("cursed stones", Fault(6, 1, "o o o o r1 r1"));
        }

        /// <summary>
        /// <b>The row authors which cards, never where</b>: a deal shuffles them - the same deal
        /// the same way on every device, a different deal differently - and changes nothing
        /// else; a deal of nought keeps the row as written. The ledger's deal is the day and the
        /// attempt, never nought, and the next attempt is a different board.
        /// </summary>
        [Test]
        public void ADealShufflesTheCardsAndNothingElse()
        {
            var table = Shipped();
            var def = ShippedPairs(table)[0];

            int[] Kinds(uint deal)
            {
                var p = (PairsPuzzle)new ChallengeRun(def, table.Line, deal).Puzzle;
                var k = new int[p.Width * p.Height];
                for (int i = 0; i < k.Length; i++) k[i] = p.KindAt(i);
                return k;
            }

            var written = Kinds(0u);
            var first = PairsGems.Tokens(def.Rows[0]);
            for (int x = 0; x < def.Width; x++)
            {
                PairsGems.TryParse(first[x], out int kind);
                Assert.AreEqual(kind, written[x], "deal nought is the row as written");
            }

            CollectionAssert.AreEqual(Kinds(5u), Kinds(5u), "one deal is one board");
            CollectionAssert.AreNotEqual(Kinds(5u), Kinds(6u), "two deals are two boards");
            CollectionAssert.AreEquivalent(written, Kinds(5u), "a deal moves the cards and deals no new one");

            Assert.AreEqual(ChallengePlay.DealOf(20000, 1), ChallengePlay.DealOf(20000, 1));
            Assert.AreNotEqual(ChallengePlay.DealOf(20000, 1), ChallengePlay.DealOf(20000, 2), "a retry is a fresh board");
            Assert.AreNotEqual(ChallengePlay.DealOf(20000, 1), ChallengePlay.DealOf(20001, 1), "tomorrow is a fresh board");
            for (int d = 0; d < 400; d++) Assert.AreNotEqual(0u, ChallengePlay.DealOf(20000 + d, 1 + d % 7));
        }

        // ------------------------------------------------------------------ the glade
        /// <summary>
        /// A critter lit in its colour feeds its turret every turn it stays lit - the pipes'
        /// sentence, said of the glade - and the light is the real board's: a red crystal wakes
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
            // costs a step - and wakes nobody, so it feeds nothing.
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
            Assert.IsFalse(wake.Move.Feeds[0].Banks, "a lit critter's fire is this turn's or nobody's (56l)");
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

        /// <summary>
        /// <b>Light never mixes on a challenge glade</b> (invariant 56l). The same three cells -
        /// a red crystal, a conduit, a green crystal - are one orange network on the map and a
        /// dark one here, because there a blend is a light no turret answers.
        /// </summary>
        [Test]
        public void TwoColoursThatMeetPutEachOtherOutRatherThanBlend()
        {
            var parsed = LevelGridParser.Parse(new LevelLayout(3, 1, new[] { "*E#R/0 -EW/0 *W#G/0" }));
            Assert.IsTrue(parsed.Ok);

            var map = new Puzzle(LevelId.None, 3, 1, LevelTuning.Default(1), (Cell[])parsed.Cells.Clone());
            var challenge = new Puzzle(LevelId.None, 3, 1, LevelTuning.Default(1), (Cell[])parsed.Cells.Clone(), blends: false);

            Assert.AreEqual(Energy.R | Energy.G, map.EnergyOn(1, 0), "the map's glade mixes red and green");
            Assert.AreEqual(Energy.None, challenge.EnergyOn(1, 0), "a challenge glade puts them out");
            Assert.AreEqual(Energy.None, challenge.EnergyOn(0, 0));
            Assert.AreEqual(Energy.None, challenge.EnergyOn(2, 0));

            // Two crystals of one colour are one light, not a clash.
            var twin = LevelGridParser.Parse(new LevelLayout(3, 1, new[] { "*E#Y/0 -EW/0 *W#Y/0" }));
            var amber = new Puzzle(LevelId.None, 3, 1, LevelTuning.Default(1), twin.Cells, blends: false);
            Assert.AreEqual(Energy.R | Energy.G, amber.EnergyOn(1, 0));
        }

        /// <summary>
        /// An amber critter is woken by an amber crystal and pays the amber turret; one that only
        /// a red and a green crystal could reach is a board with no answer, refused at read, and
        /// so is a solution in which two colours meet on a network with nobody on it.
        /// </summary>
        [Test]
        public void AnAmberCritterWantsAnAmberCrystal()
        {
            var problems = new List<string>();
            ChallengeTable.TryBuild(Table(Row("g", "glade", 3, 1, new[] { "*E#Y/0 -EW/1 @W#Y/0" }, new[] { "0 y1" })),
                                    out var table, problems);
            Assert.AreEqual(0, problems.Count, string.Join("; ", problems));

            var run = new ChallengeRun(table.Find("g"), new ChallengeLine(1, 3, 1));
            var wake = run.Play(ChallengeInput.Tap(1));
            Assert.AreEqual(ChallengeState.Won, wake.State);
            Assert.AreEqual(3, wake.Move.Feeds[0].Colour, "amber light is the amber turret");

            // The blend the map's glade would accept: red one side, green the other.
            problems.Clear();
            ChallengeTable.TryBuild(Table(Row("g", "glade", 5, 1, new[] { "*E#R/0 -EW/1 @EW#Y/0 -EW/0 *W#G/0" }, new[] { "0 y1" })),
                                    out table, problems);
            Assert.AreEqual(0, table.Count, "a critter only a blend could wake is refused");

            // Two colours meeting on a network with no critter on it: solved, and dark.
            problems.Clear();
            ChallengeTable.TryBuild(Table(Row("g", "glade", 3, 2,
                                              new[] { "*E#R/0 -EW/1 @W#R/0", "*E#B/0 -EW/0 *W#G/0" }, new[] { "0 r1" })),
                                    out table, problems);
            Assert.AreEqual(0, table.Count);
            Assert.IsTrue(problems.Exists(p => p.Contains("never mixes")), string.Join("; ", problems));
        }

        // ------------------------------------------------------------------ merge
        static MergePuzzle Merge(string[] rows, int target, out ChallengeRun run, int bolts = 1)
        {
            var problems = new List<string>();
            var dto = Row("g", "merge", rows[0].Length, rows.Length, rows, new[] { "40 r1" }, 5, bolts);
            dto.target = target;
            ChallengeTable.TryBuild(Table(dto), out var table, problems);
            Assert.AreEqual(0, problems.Count, string.Join("; ", problems));

            run = new ChallengeRun(table.Find("g"), new ChallengeLine(1, 3, 1));
            return (MergePuzzle)run.Puzzle;
        }

        /// <summary>
        /// Only the dragged gem moves: it slides until something stops it, the rest of the
        /// board stands still, nothing is dealt, and meeting its own size makes the next one
        /// and feeds that size's colour.
        /// </summary>
        [Test]
        public void OneGemSlidesAndMergesWithItsOwnSizeOnly()
        {
            var merge = Merge(new[] { "1..1#", "2..2." }, 3, out var run);

            var report = run.Play(ChallengeInput.Slide(3, -1, 0));
            Assert.IsTrue(report.Move.Turn);
            Assert.AreEqual(0, merge.RankAt(3));
            Assert.AreEqual(2, merge.RankAt(0), "two ones became a two where they met");
            Assert.AreEqual(3, merge.LastFrom);
            Assert.AreEqual(0, merge.LastTo);
            Assert.AreEqual(1, merge.LastRank);
            Assert.AreEqual(2, merge.LastMade);
            Assert.AreEqual(1, report.Move.Feeds.Count);
            Assert.AreEqual(MergePuzzle.ColourOf(2), report.Move.Feeds[0].Colour);

            // The row below never moved, and nothing was dealt anywhere.
            Assert.AreEqual(2, merge.RankAt(5));
            Assert.AreEqual(2, merge.RankAt(8));
            int gems = 0;
            for (int i = 0; i < 10; i++) if (merge.RankAt(i) > 0) gems++;
            Assert.AreEqual(3, gems, "a slide deals nothing");
        }

        [Test]
        public void ASlideStopsAgainstARockAnEdgeOrAStrangerAndFeedsNothing()
        {
            var merge = Merge(new[] { "1.#.2", "1...3" }, 4, out var run);

            var report = run.Play(ChallengeInput.Slide(0, 1, 0));
            Assert.IsTrue(report.Move.Turn);
            Assert.AreEqual(1, merge.RankAt(1), "stopped short of the rock");
            Assert.AreEqual(0, merge.LastMade);
            Assert.AreEqual(0, report.Move.Feeds.Count);

            Assert.IsTrue(run.Play(ChallengeInput.Slide(4, 0, -1)).Move.Refused, "a two on a three goes nowhere");

            run.Play(ChallengeInput.Slide(9, -1, 0));
            Assert.AreEqual(6, merge.LastTo, "a three stops against a one");

            run.Play(ChallengeInput.Slide(5, 0, 1));
            Assert.AreEqual(0, merge.LastTo, "a slide runs to the edge");

            Assert.IsTrue(run.Play(ChallengeInput.Slide(2, 1, 0)).Move.Refused, "a rock is not a gem");
            Assert.IsTrue(run.Play(ChallengeInput.Swipe(1, 0)).Move.Refused, "a swipe on no gem moves nothing");
        }

        /// <summary>
        /// Undo is a move: the board goes back, the hill still walks, and a merge re-made after
        /// a take-back pays nothing, so merge-undo-merge cannot farm the turrets.
        /// </summary>
        [Test]
        public void UndoCostsATurnAndAMergePaysOnce()
        {
            var merge = Merge(new[] { "1.1", "..2" }, 3, out var run);
            Assert.IsFalse(merge.CanUndo);
            Assert.IsTrue(run.Play(ChallengeInput.Undo()).Move.Refused, "nothing to take back");

            Assert.AreEqual(1, run.Play(ChallengeInput.Slide(2, -1, 0)).Move.Feeds.Count);
            Assert.AreEqual(2, merge.RankAt(0));

            int before = run.Hill.Turn;
            var back = run.Play(ChallengeInput.Undo());
            Assert.IsTrue(back.Move.Turn, "an undo walks the hill");
            Assert.AreEqual(before + 1, run.Hill.Turn);
            Assert.IsTrue(merge.LastUndone);
            Assert.AreEqual(1, merge.RankAt(0));
            Assert.AreEqual(1, merge.RankAt(2));
            Assert.IsFalse(merge.CanUndo);

            var again = run.Play(ChallengeInput.Slide(2, -1, 0));
            Assert.AreEqual(2, merge.RankAt(0));
            Assert.AreEqual(0, again.Move.Feeds.Count, "the same merge made twice pays once");
        }

        [Test]
        public void AMergeRowIsRefusedWhenItsGemsCannotReachTheTarget()
        {
            var problems = new List<string>();
            var dto = Row("g", "merge", 3, 2, new[] { "1.1", "#.." }, new[] { "0 r1" });
            dto.target = 3;
            ChallengeTable.TryBuild(Table(dto), out var table, problems);
            Assert.AreEqual(0, table.Count);
            Assert.IsTrue(problems.Exists(p => p.Contains("needs more gems")), string.Join("; ", problems));
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

        /// <summary>A board on the board's own edge: no ring of wall, and the edge refuses a step.</summary>
        static SokobanPuzzle Push(string[] rows, string[] gems, out ChallengeRun run, int bolts = 1)
        {
            var problems = new List<string>();
            var dto = Row("p", "sokoban", rows[0].Length, rows.Length, rows, new[] { "0 r3" }, hill: 9, bolts: bolts);
            dto.gems = gems;
            ChallengeTable.TryBuild(Table(dto), out var table, problems);
            Assert.AreEqual(0, problems.Count, string.Join("; ", problems));
            run = new ChallengeRun(table.Find("p"), new ChallengeLine(1, 3, 1));
            return (SokobanPuzzle)run.Puzzle;
        }

        [Test]
        public void TheBoardsEdgeIsAWall()
        {
            var push = Push(new[] { "@.R.", "...G" }, new[] { "..r.", ".g.." }, out var run);
            Assert.AreEqual((1, 0), (push.FacingX, push.FacingY), "a keeper in a corner opens facing into the board");

            Assert.IsTrue(run.Play(ChallengeInput.Swipe(0, 1)).Move.Refused, "a step off the top edge is refused");
            Assert.IsTrue(run.Play(ChallengeInput.Swipe(-1, 0)).Move.Refused, "a step off the left edge is refused");
            Assert.AreEqual(0, run.Turns, "a refusal costs nothing");
            Assert.AreEqual(0, push.Keeper);
        }

        /// <summary>
        /// <b>UNDO takes the last step back and is a turn</b> (Merge's rule): the keeper walks
        /// home still facing the way it faced, the gem it pushed comes back with it, and the
        /// hill walks. With nothing to take back it is refused and costs nothing.
        /// </summary>
        [Test]
        public void UndoTakesTheLastStepBackAndIsATurn()
        {
            var push = Push(new[] { "@..R", "...G" }, new[] { ".r..", ".g.." }, out var run);

            Assert.IsFalse(push.CanUndo);
            Assert.IsTrue(run.Play(ChallengeInput.Undo()).Move.Refused, "nothing to take back");
            Assert.AreEqual(0, run.Turns);

            Assert.IsFalse(run.Play(ChallengeInput.Swipe(1, 0)).Move.Refused);
            Assert.AreEqual(1, push.Keeper);
            Assert.AreEqual(0, push.GemAt(2), "the red gem was pushed a cell right");
            Assert.AreEqual((1, 0), (push.FacingX, push.FacingY));

            var back = run.Play(ChallengeInput.Undo());
            Assert.IsFalse(back.Move.Refused);
            Assert.IsTrue(back.Walked, "an undo is a turn, so the hill walks");
            Assert.AreEqual(2, run.Turns);
            Assert.AreEqual(0, push.Keeper, "the keeper is home");
            Assert.AreEqual(0, push.GemAt(1), "and the gem with it");
            Assert.AreEqual(-1, push.GemAt(2));
            Assert.IsTrue(push.LastUndone);
            Assert.AreEqual((2, 1), (push.LastPushFrom, push.LastPushTo), "drawn as the gem going home");
            Assert.AreEqual((1, 0), (push.FacingX, push.FacingY), "still facing the gem it pulled home");
            Assert.IsFalse(push.CanUndo);

            // A walk with no push is taken back as a walk.
            run.Play(ChallengeInput.Swipe(0, -1));
            Assert.AreEqual(4, push.Keeper);
            run.Play(ChallengeInput.Undo());
            Assert.AreEqual(0, push.Keeper);
            Assert.AreEqual((-1, -1), (push.LastPushFrom, push.LastPushTo));
        }

        /// <summary>
        /// <b>A seated gem streams</b>: it fires every step it stands on its pad and never banks,
        /// so a gem seated early is exactly as strong as it is now (56l's rule, said of Push).
        /// Lifted back off its pad, it fires nothing.
        /// </summary>
        [Test]
        public void ASeatedGemStreamsEveryStepAndNeverBanks()
        {
            var push = Push(new[] { "@.R..", "....G" }, new[] { ".r...", "..g.." }, out var run, bolts: 2);

            var seat = run.Play(ChallengeInput.Swipe(1, 0));
            Assert.IsTrue(push.Seated(2));
            Assert.AreEqual(1, seat.Move.Feeds.Count);
            Assert.AreEqual(0, seat.Move.Feeds[0].Colour);
            Assert.AreEqual(2, seat.Move.Feeds[0].Bolts);
            Assert.IsFalse(seat.Move.Feeds[0].Banks, "a seated gem is a steady fire, not a burst");

            var walk = run.Play(ChallengeInput.Swipe(0, -1));
            Assert.AreEqual(1, walk.Move.Feeds.Count, "it fires on a step that pushes nothing");
            Assert.AreEqual(0, run.Hill.Wards[0].Banked, "and nothing it fires is kept");

            run.Play(ChallengeInput.Undo());
            var lift = run.Play(ChallengeInput.Undo());
            Assert.IsFalse(push.Seated(2));
            Assert.AreEqual(0, lift.Move.Feeds.Count, "a gem lifted off its pad fires nothing");
        }

        // ------------------------------------------------------------------ the shipped four
        // ------------------------------------------------------------------ every shipped pairs
        /// <summary>
        /// How many deals of a Pairs row are played, and which: 1..<c>PairsDeals</c>, mixed by
        /// the puzzle with the row's seed. <c>Tools/make_pairs_challenges.py</c> plays the same
        /// deals (<c>DEALS</c>), so a figure printed here is the figure the tool prints.
        /// </summary>
        internal const int PairsDeals = 48;

        /// <summary>
        /// The band every Pairs row's slack must sit in, in hundredths of a perfect memory's
        /// turns, on its median deal (<c>SLACK_BAND</c>): a medium row forgives about 1.95x and a
        /// hard one about 1.75x, since every hill was lengthened a fifth on 2026-10-03 (the owner:
        /// still too hard, give a player more flips). The floor keeps a hard row from asking for a
        /// perfect memory; the ceiling keeps a miss costing something.
        /// </summary>
        const int PairsSlackFloor = 135, PairsSlackCeiling = 240;

        /// <summary>What the turrets must be worth: the same run with no bolt fed forgives this much less (<c>FIRE_WORTH</c>).</summary>
        const int PairsFireWorth = 25;

        /// <summary>The most raiders standing on an average turn of the median deal (<c>MOST_CROWD</c>).</summary>
        const float PairsMostCrowd = 4.5f;

        /// <summary>A row with this many pairs or more is hard (<c>HARD_PAIRS</c>).</summary>
        const int PairsHard = 13;

        /// <summary>The line health the unluckiest deal keeps at a perfect memory's pace (<c>LUCK_FLOOR</c>).</summary>
        static int LuckFloor(PairsPuzzle pairs) => pairs.Pairs >= PairsHard ? 3 : 6;

        static IReadOnlyList<ChallengeDefinition> ShippedPairs(ChallengeTable table)
        {
            var rows = table.RowsOf(ChallengeGenre.Pairs);
            Assert.Greater(rows.Count, 0, "challenges.json ships no pairs");
            return rows;
        }

        /// <summary>
        /// A player with a perfect memory who explores in reading order: cash a known pair, else
        /// turn the first unseen card, take its partner if it has been seen, else turn the next
        /// unseen one. Over a shuffled deal reading order is a random order, which is how a
        /// first-sight player explores. Returns what every walked resolve was fed - a curse's
        /// stumble is an empty one - and leaves the run at its end.
        /// </summary>
        static List<ChallengeFeed[]> PerfectMemory(ChallengeDefinition def, ChallengeLine line, uint deal, out ChallengeRun run)
        {
            run = new ChallengeRun(def, line, deal);
            var pairs = (PairsPuzzle)run.Puzzle;
            int n = pairs.Width * pairs.Height;
            var known = new bool[n];
            var fed = new List<ChallengeFeed[]>();
            var r = run;

            bool Hidden(int c) => pairs.FaceAt(c) == PairsPuzzle.Face.Hidden;

            bool Tap(int c)
            {
                known[c] = true;
                var report = r.Play(ChallengeInput.Tap(c));
                Assert.IsFalse(report.Move.Refused, $"{def.Id}: the bot's tap on {c} was refused");
                if (report.Walked)
                {
                    fed.Add(report.Move.Feeds.ToArray());
                    for (int s = 0; s < report.Move.Stumbles; s++) fed.Add(new ChallengeFeed[0]);
                }
                return pairs.IsCurse(c);
            }

            int guard = 0;
            while (run.State == ChallengeState.Playing && guard++ < 4 * n * n)
            {
                int a = -1, b = -1;
                for (int i = 0; i < n && a < 0; i++)
                {
                    if (!known[i] || !Hidden(i) || pairs.IsCurse(i)) continue;
                    for (int j = i + 1; j < n; j++)
                        if (known[j] && Hidden(j) && pairs.KindAt(j) == pairs.KindAt(i)) { a = i; b = j; break; }
                }

                if (a >= 0)
                {
                    Tap(a);
                    Tap(b);
                    continue;
                }

                a = 0;
                while (known[a] || !Hidden(a)) a++;
                if (Tap(a)) continue;

                b = -1;
                for (int j = 0; j < n && b < 0; j++)
                    if (j != a && known[j] && Hidden(j) && pairs.KindAt(j) == pairs.KindAt(a)) b = j;
                if (b < 0)
                {
                    b = 0;
                    while (b == a || known[b] || !Hidden(b)) b++;
                }
                Tap(b);
            }

            return fed;
        }

        /// <summary>The deal whose perfect-memory run is the median length, ties to the lower deal (<c>schedules</c>).</summary>
        static uint MedianDeal(ChallengeDefinition def, ChallengeLine line, out List<ChallengeFeed[]> fed)
        {
            var runs = new List<KeyValuePair<int, uint>>();
            for (uint d = 1; d <= PairsDeals; d++)
                runs.Add(new KeyValuePair<int, uint>(PerfectMemory(def, line, d, out _).Count, d));
            runs.Sort((x, y) => x.Key != y.Key ? x.Key.CompareTo(y.Key) : x.Value.CompareTo(y.Value));

            uint median = runs[runs.Count / 2].Value;
            fed = PerfectMemory(def, line, median, out _);
            return median;
        }

        static int Health(ChallengeRun run)
        {
            int health = 0;
            for (int i = 0; i < run.Hill.Wards.Count; i++) health += run.Hill.Wards[i].Health;
            return health;
        }

        static int SlackOf(ChallengeDefinition def, ChallengeLine line, List<ChallengeFeed[]> fed)
        {
            int slowest = 100;
            for (int pace = 105; pace <= 500; pace += 5)
            {
                if (!HoldsAt(def, line, fed, fed.Count + 1, pace, out _)) break;
                slowest = pace;
            }
            return slowest;
        }

        /// <summary>
        /// Every row is won on every one of its deals by a perfect memory, with the line whole
        /// but for one blow on the median deal and at least <see cref="LuckFloor"/> standing on
        /// the unluckiest - a shuffle may be unkind, but it may not decide the run. Prints the
        /// spread of turns across the deals, which is how much luck a row carries.
        /// </summary>
        [Test]
        public void EveryShippedPairsIsWonOnEveryDeal()
        {
            var table = Shipped();

            foreach (var def in ShippedPairs(table))
            {
                int fewest = int.MaxValue, most = 0, worst = int.MaxValue;
                PairsPuzzle pairs = null;

                for (uint d = 1; d <= PairsDeals; d++)
                {
                    var fed = PerfectMemory(def, table.Line, d, out var run);
                    pairs = (PairsPuzzle)run.Puzzle;
                    Assert.AreEqual(ChallengeState.Won, run.State, $"{def.Id} is lost by a perfect memory on deal {d}");
                    fewest = Math.Min(fewest, fed.Count + 1);
                    most = Math.Max(most, fed.Count + 1);
                    worst = Math.Min(worst, Health(run));
                }

                uint median = MedianDeal(def, table.Line, out var medianFed);
                PerfectMemory(def, table.Line, median, out var medianRun);

                Console.WriteLine($"{def.Id}: {def.Width}x{def.Height}, {pairs.Pairs} pairs, {pairs.Curses} curse(s); " +
                                  $"a perfect memory takes {fewest}-{most} turns ({medianFed.Count + 1} on the median deal), " +
                                  $"line {Health(medianRun)}/12 there and {worst}/12 on the unluckiest");

                Assert.GreaterOrEqual(Health(medianRun), 11, $"{def.Id}: the median deal's line took more than one blow");
                Assert.GreaterOrEqual(worst, LuckFloor(pairs),
                                      $"{def.Id}: the unluckiest deal leaves the line at {worst}/12; a shuffle decides it " +
                                      "(make_pairs_challenges.py --write)");
            }
        }

        /// <summary>
        /// <b>How poor a memory can a player have and still win</b> - the glade's slack, asked of
        /// a memory game: a player's extra turns are the misses a perfect memory would not make.
        /// Held to the band on the median deal, and the turrets held to being worth something:
        /// the same run with every bolt taken away must forgive <see cref="PairsFireWorth"/>
        /// less (invariant 5d - a fusion that changes nothing is decoration).
        /// </summary>
        [Test]
        public void EveryShippedPairsForgivesAPoorerMemory()
        {
            var table = Shipped();

            foreach (var def in ShippedPairs(table))
            {
                MedianDeal(def, table.Line, out var fed);
                int slack = SlackOf(def, table.Line, fed);

                var dry = new List<ChallengeFeed[]>(fed.Count);
                for (int i = 0; i < fed.Count; i++) dry.Add(new ChallengeFeed[0]);
                int bare = SlackOf(def, table.Line, dry);

                Console.WriteLine($"{def.Id}: a player may take {slack / 100f:0.00}x a perfect memory's turns " +
                                  $"({bare / 100f:0.00}x if no pair fed a turret) on a hill of {def.Hill}");

                Assert.GreaterOrEqual(slack, PairsSlackFloor, $"{def.Id} asks for a near-perfect memory (make_pairs_challenges.py --write)");
                Assert.LessOrEqual(slack, PairsSlackCeiling, $"{def.Id} forgives {slack / 100f:0.00}x; a miss costs nothing there");
                Assert.GreaterOrEqual(slack - bare, PairsFireWorth,
                                      $"{def.Id}: the turrets buy only {(slack - bare) / 100f:0.00}x; what a pair feeds barely matters");
            }
        }

        /// <summary>The hill is never empty at a perfect memory's pace, and never a smear of bodies.</summary>
        [Test]
        public void EveryShippedPairsKeepsTheHillPeopled()
        {
            var table = Shipped();

            foreach (var def in ShippedPairs(table))
            {
                MedianDeal(def, table.Line, out var fed);
                Assert.IsTrue(HoldsAt(def, table.Line, fed, fed.Count + 1, 100, out var crowd), $"{def.Id}: the bot's line fell");

                int empty = 0, standing = 0;
                foreach (int on in crowd) { if (on == 0) empty++; standing += on; }
                float density = standing / (float)Math.Max(1, crowd.Count);
                Console.WriteLine($"{def.Id}: {density:0.0} raider(s) on the hill on an average turn of {crowd.Count}");

                Assert.AreEqual(0, empty, $"{def.Id}: the hill stands empty on {empty} of the bot's {crowd.Count} turns");
                Assert.LessOrEqual(density, PairsMostCrowd, $"{def.Id}: {density:0.0} raiders stand on an average turn");
            }
        }

        /// <summary>
        /// Every stone a row can name, the curse and the two cards are on disk. The addresses are
        /// built from the kind (<c>PairsGems.ArtKey</c>), so <c>artnames.py</c> cannot see one and
        /// this is the gate that would: a missing stone is a card that turns over onto nothing.
        /// </summary>
        [Test]
        public void EveryPairsGemIsOnDisk()
        {
            string root = Path.Combine(TestJson.RepoRoot(), "Assets", "Game", "Art", "Challenge");
            var keys = ChallengeArt.PairsArtKeys();
            Assert.AreEqual(PairsGems.Kinds + 3, keys.Count, "two cards, every stone and the curse");

            foreach (var key in keys)
                Assert.IsTrue(File.Exists(Path.Combine(root, key + ".png")),
                              $"'{key}.png' is not on disk; cut it with Tools/make_pairs_art.py");
        }

        // ------------------------------------------------------------------ every shipped glade
        /// <summary>
        /// The band every glade row's slack must sit in, in hundredths of the bot's turns: a
        /// player that many times slower than the bot, every critter waking that much later,
        /// still wins, and one much slower does not. <b>The ceiling is the owner's</b>
        /// (2026-09-26: "I can rotate conduits a lot") - the first cut forgave 1.6-2.0x, which is
        /// a hill a player can ignore; the floor keeps a hard row from asking for the bot's own
        /// line. <c>Tools/make_glade_challenges.py</c> tunes a medium row to 1.50x and a hard one
        /// (all four lanes) to 1.35x inside this band (<c>SLACK_BAND</c>, <c>TARGET</c>).
        /// </summary>
        const int SlowestPaceFloor = 125, SlowestPaceCeiling = 165;

        static IReadOnlyList<ChallengeDefinition> ShippedGlades(ChallengeTable table)
        {
            var glades = table.RowsOf(ChallengeGenre.Glade);
            Assert.Greater(glades.Count, 0, "challenges.json ships no glade");
            return glades;
        }

        /// <summary>
        /// A player who wakes one critter at a time, lanes in the order their first raider
        /// musters (the nearest threat first), each critter's solved network turned home
        /// crystal-first. Returns what every turn the hill walked was fed, exactly as the run
        /// fed it (a volley or a bank), and leaves the run at its end for the caller to judge.
        /// </summary>
        static List<ChallengeFeed[]> WakeHome(ChallengeDefinition def, ChallengeLine line, out ChallengeRun run, bool print)
        {
            run = new ChallengeRun(def, line);
            var glade = (GladePuzzle)run.Puzzle;

            var first = new int[ChallengeColours.Count];
            for (int lane = 0; lane < first.Length; lane++) first[lane] = int.MaxValue;
            foreach (var wave in def.Waves)
                foreach (var raider in wave.Raiders)
                    first[raider.Colour] = Math.Min(first[raider.Colour], wave.Turn);

            var lanes = new List<int> { 0, 1, 2, 3 };
            lanes.Sort((a, b) => first[a] != first[b] ? first[a].CompareTo(first[b]) : a.CompareTo(b));

            var fed = new List<ChallengeFeed[]>();
            var network = new List<int>();
            var awake = new HashSet<int>();

            foreach (int lane in lanes)
            {
                for (int lamp = 0; lamp < glade.Board.C.Length && run.State == ChallengeState.Playing; lamp++)
                {
                    if (glade.Board.C[lamp].kind != Kind.Lamp || GladePuzzle.LaneOf(glade.Board.C[lamp].colour) != lane) continue;

                    glade.SolutionNetwork(lamp, network);
                    Assert.Greater(network.Count, 1, $"{def.Id}: critter {lamp} has no network in the solution");

                    foreach (int cell in network)
                    {
                        int taps = glade.Board.TurnsOwed(cell);
                        for (int t = 0; t < taps && run.State == ChallengeState.Playing; t++)
                        {
                            var report = run.Play(ChallengeInput.Tap(cell));
                            Assert.IsFalse(report.Move.Refused, $"{def.Id}: tap on {cell} refused");
                            if (!report.Walked) continue;

                            fed.Add(report.Move.Feeds.ToArray());
                        }
                    }

                    if (!print) continue;
                    for (int i = 0; i < glade.Board.C.Length; i++)
                        if (glade.Board.C[i].kind == Kind.Lamp && glade.Board.Lit[i] && awake.Add(i))
                            Console.WriteLine($"{def.Id}: critter at {i % glade.Width},{i / glade.Width} " +
                                              $"({ChallengeColours.LetterOf(GladePuzzle.LaneOf(glade.Board.C[i].colour))}) awake by turn {run.Turns}");
                }
            }

            return fed;
        }

        [Test]
        public void EveryShippedGladeIsWonByTurningEachTileHome()
        {
            var table = Shipped();

            foreach (var def in ShippedGlades(table))
            {
                var probe = new ChallengeRun(def, table.Line);
                int expected = ((GladePuzzle)probe.Puzzle).Board.TurnsToSolution;
                Assert.Greater(expected, 0, $"{def.Id} is dealt with nothing to do");

                WakeHome(def, table.Line, out var run, print: true);

                Won(run, def.Id);
                Assert.LessOrEqual(run.Turns, expected, $"{def.Id}: turning each tile home never costs more than the solution's distance");

                // A row the bot wins while the line takes a blow is a row that hurts a
                // perfect player; nothing is promised to anybody slower.
                int health = 0;
                for (int i = 0; i < run.Hill.Wards.Count; i++) health += run.Hill.Wards[i].Health;
                Assert.GreaterOrEqual(health, run.Hill.Wards.Count * run.Hill.Line.Health - 1,
                                      $"{def.Id}: the bot's line took more than one blow");
            }
        }

        /// <summary>
        /// <b>How slow can a player be and still win</b> - the hill's whole question, asked as a
        /// count (invariant 5d). The bot's run is replayed against a fresh hill at a slower
        /// pace: at pace <c>p</c> the player's <c>k</c>-th turn has done what the bot had done
        /// by turn <c>k·100/p</c>, so every critter wakes that much later and every raider has
        /// that much longer to walk. The printed figure is what a row is tuned against
        /// (<c>Tools/make_glade_challenges.py</c> mirrors it), and every row is held to the
        /// band: forgiving enough to be fair, tight enough that a wasted turn costs something.
        /// </summary>
        [Test]
        public void EveryShippedGladeForgivesASlowerPlayer()
        {
            var table = Shipped();

            foreach (var def in ShippedGlades(table))
            {
                var fed = WakeHome(def, table.Line, out var run, print: false);
                Assert.AreEqual(ChallengeState.Won, run.State, $"{def.Id} was not won by the bot");

                int total = fed.Count + 1;
                int slowest = 100;
                for (int pace = 105; pace <= 500; pace += 5)
                {
                    if (!HoldsAt(def, table.Line, fed, total, pace, out _)) break;
                    slowest = pace;
                }

                Console.WriteLine($"{def.Id}: {def.Width}x{def.Height}, won in {total} turn(s); a player may take " +
                                  $"{slowest / 100f:0.00}x that ({(total * slowest + 99) / 100} turns) on a hill of {def.Hill}");

                Assert.GreaterOrEqual(slowest, SlowestPaceFloor,
                                      $"{def.Id} is lost by a player {slowest / 100f + .05f:0.00}x the bot's pace; " +
                                      "lengthen the hill or lighten the waves (make_glade_challenges.py --write)");
                Assert.LessOrEqual(slowest, SlowestPaceCeiling,
                                   $"{def.Id} forgives a player {slowest / 100f:0.00}x the bot's pace; a wasted turn " +
                                   "costs nothing there (make_glade_challenges.py --write)");
            }
        }

        /// <summary>
        /// <b>The hill is never empty while the bot plays</b> - the owner's report (2026-09-26:
        /// "there are times that there are no enemies on the hill"), asked as a count. Every turn
        /// the bot walks starts with a raider standing, so every turn a player spends is a step
        /// somebody takes toward the line. Printed beside it: how many stand on an average turn.
        /// </summary>
        [Test]
        public void EveryShippedGladeKeepsTheHillPeopled()
        {
            var table = Shipped();

            foreach (var def in ShippedGlades(table))
            {
                var fed = WakeHome(def, table.Line, out var run, print: false);
                Assert.AreEqual(ChallengeState.Won, run.State, $"{def.Id} was not won by the bot");

                Assert.IsTrue(HoldsAt(def, table.Line, fed, fed.Count + 1, 100, out var crowd), $"{def.Id}: the bot's line fell");

                int empty = 0, standing = 0;
                foreach (int on in crowd) { if (on == 0) empty++; standing += on; }
                Console.WriteLine($"{def.Id}: {standing / (float)Math.Max(1, crowd.Count):0.0} raider(s) on the hill " +
                                  $"on an average turn of {crowd.Count}");

                Assert.AreEqual(0, empty, $"{def.Id}: the hill stands empty on {empty} of the bot's {crowd.Count} turns");
            }
        }

        /// <summary>
        /// The bot's run at <paramref name="pace"/> hundredths of its speed, against a fresh hill;
        /// <paramref name="crowd"/> is how many raiders stood on it as each walked turn began.
        /// </summary>
        static bool HoldsAt(ChallengeDefinition def, ChallengeLine line, List<ChallengeFeed[]> fed, int total, int pace,
                            out List<int> crowd)
        {
            var hill = new ChallengeHill(line, def.Hill, def.Waves);
            int finish = (total * pace + 99) / 100;
            crowd = new List<int>(finish);

            // The solving turn never walks the hill; every turn before it does.
            for (int k = 1; k < finish; k++)
            {
                int progress = Math.Min(total - 1, k * 100 / pace);
                if (progress >= 1)
                    foreach (var feed in fed[progress - 1])
                    {
                        if (feed.Banks) hill.Feed(feed.Colour, feed.Bolts);
                        else hill.Volley(feed.Colour, feed.Bolts);
                    }

                crowd.Add(hill.Standing);
                hill.Resolve(null);
                if (!hill.LineStanding) return false;
            }

            return true;
        }

        /// <summary>
        /// Every glade row is held to <b>the chapter validator</b>, with every warning an error.
        /// <c>GladePuzzle.Fault</c> asks what a device needs - the row parses, nothing crumbles,
        /// the solution wakes everything - and nothing more; the chapter's rules are the ones
        /// that ask whether a board is <em>fit</em>: a crossing that crosses nothing, a briar or a
        /// twist nothing on the board settles, a taproot that can never agree. A challenge is a
        /// glade a player meets once a day, so it is held to the same bar as one on the map.
        /// </summary>
        [Test]
        public void EveryShippedGladeMeetsTheChapterValidator()
        {
            var table = Shipped();

            foreach (var def in ShippedGlades(table))
            {
                var layout = new LevelLayout(def.Width, def.Height, def.Rows);
                var parsed = LevelGridParser.Parse(layout);
                Assert.IsTrue(parsed.Ok, $"{def.Id} does not parse");

                var level = new LevelDefinition(
                    LevelId.Parse(def.Id), ChapterId.Parse("challenges"), layout,
                    LevelTuning.Default(Math.Max(1, PuzzleFactory.MinimumMoves(parsed.Cells))),
                    new LevelPresentation(new UnityEngine.Vector2(.5f, .5f), null, null, null));

                var report = LevelValidator.Validate(level);
                var issues = new List<string>();
                foreach (var issue in report.Issues) issues.Add(issue.ToString());

                Assert.AreEqual(0, issues.Count, $"{def.Id}:\n  " + string.Join("\n  ", issues));
            }
        }

        /// <summary>
        /// A raider walks at the turret of its colour, and only a critter of that colour feeds
        /// that turret - so a wave naming a colour the board has no critter for sends a raider
        /// nothing on the board can answer. It would strike its post until the post fell and
        /// then walk on to the next, which is a loss the puzzle had no say in.
        /// </summary>
        [Test]
        public void EveryShippedGladeSendsOnlyColoursItCanAnswer()
        {
            var table = Shipped();

            foreach (var def in ShippedGlades(table))
            {
                var run = new ChallengeRun(def, table.Line);
                var board = ((GladePuzzle)run.Puzzle).Board;

                var fed = new bool[ChallengeColours.Count];
                for (int i = 0; i < board.C.Length; i++)
                    if (board.C[i].kind == Kind.Lamp) fed[GladePuzzle.LaneOf(board.C[i].colour)] = true;

                foreach (var wave in def.Waves)
                    foreach (var raider in wave.Raiders)
                        Assert.IsTrue(fed[raider.Colour],
                                      $"{def.Id}: the wave at turn {wave.Turn} sends a " +
                                      $"'{ChallengeColours.LetterOf(raider.Colour)}' raider and no critter feeds that turret");
            }
        }

        // ------------------------------------------------------------------ every shipped merge
        static IReadOnlyList<ChallengeDefinition> ShippedMerges(ChallengeTable table)
        {
            var merges = table.RowsOf(ChallengeGenre.Merge);
            Assert.Greater(merges.Count, 0, "challenges.json ships no merge");
            return merges;
        }

        /// <summary>
        /// A row's shortest route (<see cref="MergeRoutes"/>, written by
        /// <c>Tools/make_merge_challenges.py</c>) played against the real rules. Returns what
        /// every walked turn was fed and leaves the run at its end for the caller to judge.
        /// </summary>
        static List<ChallengeFeed[]> PlayRoute(ChallengeDefinition def, ChallengeLine line, out ChallengeRun run)
        {
            Assert.IsTrue(MergeRoutes.Shortest.TryGetValue(def.Id, out var route),
                          $"{def.Id} has no route; run make_merge_challenges.py --write");

            run = new ChallengeRun(def, line);
            var fed = new List<ChallengeFeed[]>();

            foreach (var step in route.Split(' '))
            {
                Assert.AreEqual(ChallengeState.Playing, run.State, $"{def.Id}: the run ended before its route's {step}");

                int cell = int.Parse(step.Substring(0, step.Length - 1));
                char d = step[step.Length - 1];
                int dx = d == 'L' ? -1 : d == 'R' ? 1 : 0;
                int dy = d == 'U' ? 1 : d == 'D' ? -1 : 0;

                var report = run.Play(ChallengeInput.Slide(cell, dx, dy));
                Assert.IsFalse(report.Move == null || report.Move.Refused, $"{def.Id}: the route's {step} was refused");
                if (report.Walked) fed.Add(report.Move.Feeds.ToArray());
            }

            return fed;
        }

        /// <summary>
        /// Every row is won by its route, with every gem joined into one (a shipped board's
        /// gems are exactly the target's worth, so par is the merges plus the set-up slides
        /// the board forces), and the route's line takes a blow at most.
        /// </summary>
        [Test]
        public void EveryShippedMergeIsWonByItsShortestRoute()
        {
            var table = Shipped();

            foreach (var def in ShippedMerges(table))
            {
                PlayRoute(def, table.Line, out var run);
                Won(run, def.Id);

                var merge = (MergePuzzle)run.Puzzle;
                int gems = 0;
                for (int i = 0; i < merge.Width * merge.Height; i++) if (merge.RankAt(i) > 0) gems++;
                Assert.AreEqual(1, gems, $"{def.Id}: the target is made with gems left over");

                int health = 0;
                for (int i = 0; i < run.Hill.Wards.Count; i++) health += run.Hill.Wards[i].Health;
                Assert.GreaterOrEqual(health, run.Hill.Wards.Count * run.Hill.Line.Health - 1,
                                      $"{def.Id}: the route's line took more than one blow");
            }

            foreach (var id in MergeRoutes.Shortest.Keys)
                Assert.IsNotNull(table.Find(id), $"MergeRoutes carries {id}, which is not shipped");
        }

        /// <summary>
        /// <b>How slow can a player be and still win</b>, asked of Merge exactly as of the glade
        /// (<see cref="EveryShippedGladeForgivesASlowerPlayer"/>), and held to the same band.
        /// </summary>
        [Test]
        public void EveryShippedMergeForgivesASlowerPlayer()
        {
            var table = Shipped();

            foreach (var def in ShippedMerges(table))
            {
                var fed = PlayRoute(def, table.Line, out var run);
                Assert.AreEqual(ChallengeState.Won, run.State, $"{def.Id} was not won by its route");

                int total = fed.Count + 1;
                int slowest = 100;
                for (int pace = 105; pace <= 500; pace += 5)
                {
                    if (!MergeHoldsAt(def, table.Line, fed, total, pace, out _)) break;
                    slowest = pace;
                }

                Console.WriteLine($"{def.Id}: {def.Width}x{def.Height} to {1 << def.Target}, won in {total} turn(s); " +
                                  $"a player may take {slowest / 100f:0.00}x that on a hill of {def.Hill}");

                Assert.GreaterOrEqual(slowest, SlowestPaceFloor,
                                      $"{def.Id} is lost by a player {slowest / 100f + .05f:0.00}x the route's pace " +
                                      "(make_merge_challenges.py --write)");
                Assert.LessOrEqual(slowest, SlowestPaceCeiling,
                                   $"{def.Id} forgives a player {slowest / 100f:0.00}x the route's pace " +
                                   "(make_merge_challenges.py --write)");
            }
        }

        [Test]
        public void EveryShippedMergeKeepsTheHillPeopled()
        {
            var table = Shipped();

            foreach (var def in ShippedMerges(table))
            {
                var fed = PlayRoute(def, table.Line, out _);
                Assert.IsTrue(MergeHoldsAt(def, table.Line, fed, fed.Count + 1, 100, out var crowd), $"{def.Id}: the route's line fell");

                int empty = 0, standing = 0;
                foreach (int on in crowd) { if (on == 0) empty++; standing += on; }
                Console.WriteLine($"{def.Id}: {standing / (float)Math.Max(1, crowd.Count):0.0} raider(s) on the hill " +
                                  $"on an average turn of {crowd.Count}");

                Assert.AreEqual(0, empty, $"{def.Id}: the hill stands empty on {empty} of the route's {crowd.Count} turns");
            }
        }

        /// <summary>
        /// <see cref="HoldsAt"/> for a board whose feeds <b>bank</b>: a step's merge is fed
        /// once, on the turn the slower player reaches it, where the glade's lit critter is a
        /// volley re-fired every turn it stays lit. Mirrored by the tool's <c>run</c>.
        /// </summary>
        static bool MergeHoldsAt(ChallengeDefinition def, ChallengeLine line, List<ChallengeFeed[]> fed, int total, int pace,
                                 out List<int> crowd)
        {
            var hill = new ChallengeHill(line, def.Hill, def.Waves);
            int finish = (total * pace + 99) / 100;
            crowd = new List<int>(finish);
            int done = 0;

            for (int k = 1; k < finish; k++)
            {
                int progress = Math.Min(total - 1, k * 100 / pace);
                for (; done < progress; done++)
                    foreach (var feed in fed[done])
                    {
                        if (feed.Banks) hill.Feed(feed.Colour, feed.Bolts);
                        else hill.Volley(feed.Colour, feed.Bolts);
                    }

                crowd.Add(hill.Standing);
                hill.Resolve(null);
                if (!hill.LineStanding) return false;
            }

            return true;
        }

        // ------------------------------------------------------------------ every shipped push
        static IReadOnlyList<ChallengeDefinition> ShippedPushes(ChallengeTable table)
        {
            var rows = table.RowsOf(ChallengeGenre.Sokoban);
            Assert.Greater(rows.Count, 0, "challenges.json ships no push");
            return rows;
        }

        /// <summary>
        /// A row's step-shortest route (<see cref="PushRoutes"/>, written by
        /// <c>Tools/make_push_challenges.py</c>) played against the real rules. Returns what
        /// every walked turn was fed and leaves the run at its end for the caller to judge.
        /// </summary>
        static List<ChallengeFeed[]> PlayPush(ChallengeDefinition def, ChallengeLine line, out ChallengeRun run)
        {
            Assert.IsTrue(PushRoutes.Shortest.TryGetValue(def.Id, out var route),
                          $"{def.Id} has no route; run make_push_challenges.py --write");

            run = new ChallengeRun(def, line);
            var fed = new List<ChallengeFeed[]>();

            foreach (char step in route)
            {
                Assert.AreEqual(ChallengeState.Playing, run.State, $"{def.Id}: the run ended before its route's '{step}'");

                var input = step == 'U' ? ChallengeInput.Swipe(0, 1)
                          : step == 'D' ? ChallengeInput.Swipe(0, -1)
                          : step == 'L' ? ChallengeInput.Swipe(-1, 0)
                          : ChallengeInput.Swipe(1, 0);
                var report = run.Play(input);
                Assert.IsFalse(report.Move == null || report.Move.Refused, $"{def.Id}: the route's '{step}' was refused");
                if (report.Walked) fed.Add(report.Move.Feeds.ToArray());
            }

            return fed;
        }

        /// <summary>
        /// Every row is won by its route on the last step, the hill walking once for every step
        /// before it, and the route's line takes a blow at most. A route the tool found and the
        /// C# refuses is the two copies of the rules disagreeing, which is what this is for.
        /// </summary>
        [Test]
        public void EveryShippedPushIsWonByItsShortestRoute()
        {
            var table = Shipped();

            foreach (var def in ShippedPushes(table))
            {
                PlayPush(def, table.Line, out var run);
                Won(run, def.Id);

                Assert.AreEqual(PushRoutes.Shortest[def.Id].Length - 1, run.Turns,
                                $"{def.Id}: the hill walks once per step but the winning one");

                int health = 0;
                for (int i = 0; i < run.Hill.Wards.Count; i++) health += run.Hill.Wards[i].Health;
                Assert.GreaterOrEqual(health, run.Hill.Wards.Count * run.Hill.Line.Health - 1,
                                      $"{def.Id}: the route's line took more than one blow");
            }

            foreach (var id in PushRoutes.Shortest.Keys)
                Assert.IsNotNull(table.Find(id), $"PushRoutes carries {id}, which is not shipped");
        }

        /// <summary>
        /// <b>How slow can a player be and still win</b>, asked of Push exactly as of the glade
        /// (<see cref="EveryShippedGladeForgivesASlowerPlayer"/>) and held to the same band: a
        /// seated gem is a steady fire, like a lit critter, so the same <see cref="HoldsAt"/>
        /// replays it.
        /// </summary>
        [Test]
        public void EveryShippedPushForgivesASlowerPlayer()
        {
            var table = Shipped();

            foreach (var def in ShippedPushes(table))
            {
                var fed = PlayPush(def, table.Line, out var run);
                Assert.AreEqual(ChallengeState.Won, run.State, $"{def.Id} was not won by its route");

                int total = fed.Count + 1;
                int slowest = 100;
                for (int pace = 105; pace <= 500; pace += 5)
                {
                    if (!HoldsAt(def, table.Line, fed, total, pace, out _)) break;
                    slowest = pace;
                }

                Console.WriteLine($"{def.Id}: {def.Width}x{def.Height}, won in {total} step(s); a player may take " +
                                  $"{slowest / 100f:0.00}x that on a hill of {def.Hill}");

                Assert.GreaterOrEqual(slowest, SlowestPaceFloor,
                                      $"{def.Id} is lost by a player {slowest / 100f + .05f:0.00}x the route's pace " +
                                      "(make_push_challenges.py --write)");
                Assert.LessOrEqual(slowest, SlowestPaceCeiling,
                                   $"{def.Id} forgives a player {slowest / 100f:0.00}x the route's pace " +
                                   "(make_push_challenges.py --write)");
            }
        }

        [Test]
        public void EveryShippedPushKeepsTheHillPeopled()
        {
            var table = Shipped();

            foreach (var def in ShippedPushes(table))
            {
                var fed = PlayPush(def, table.Line, out _);
                Assert.IsTrue(HoldsAt(def, table.Line, fed, fed.Count + 1, 100, out var crowd), $"{def.Id}: the route's line fell");

                int empty = 0, standing = 0;
                foreach (int on in crowd) { if (on == 0) empty++; standing += on; }
                Console.WriteLine($"{def.Id}: {standing / (float)Math.Max(1, crowd.Count):0.0} raider(s) on the hill " +
                                  $"on an average turn of {crowd.Count}");

                Assert.AreEqual(0, empty, $"{def.Id}: the hill stands empty on {empty} of the route's {crowd.Count} turns");
            }
        }

        /// <summary>A raider no gem on the board can answer is a lane nothing will ever fire down.</summary>
        [Test]
        public void EveryShippedPushSendsOnlyColoursItCanAnswer()
        {
            var table = Shipped();

            foreach (var def in ShippedPushes(table))
            {
                var held = new HashSet<int>();
                var push = (SokobanPuzzle)new ChallengeRun(def, table.Line).Puzzle;
                for (int i = 0; i < push.Width * push.Height; i++)
                    if (push.GemAt(i) >= 0) held.Add(push.GemAt(i));

                foreach (var wave in def.Waves)
                    foreach (var raider in wave.Raiders)
                        Assert.IsTrue(held.Contains(raider.Colour),
                                      $"{def.Id} sends a {ChallengeColours.LetterOf(raider.Colour)} raider and deals no gem of that colour");
            }
        }

        /// <summary>
        /// Every shipped room is drawn edge to edge - no row or column of it is solid wall - which
        /// is what the board's size on screen was bought back with (2026-09-27): a ring of wall
        /// round a row costs two columns and two rows of cell.
        /// </summary>
        [Test]
        public void EveryShippedPushUsesItsWholeBoard()
        {
            foreach (var def in ShippedPushes(Shipped()))
            {
                for (int y = 0; y < def.Height; y++)
                    Assert.AreNotEqual(new string('#', def.Width), def.Rows[y], $"{def.Id}: row {y} is solid wall");

                for (int x = 0; x < def.Width; x++)
                {
                    bool solid = true;
                    for (int y = 0; y < def.Height && solid; y++) solid = def.Rows[y][x] == '#';
                    Assert.IsFalse(solid, $"{def.Id}: column {x} is solid wall");
                }
            }
        }

        [Test]
        public void ThePushKeeperIsOnDisk()
        {
            string root = Path.Combine(TestJson.RepoRoot(), "Assets", "Game", "Art", "Challenge");
            Assert.IsTrue(File.Exists(Path.Combine(root, ChallengeArt.KeeperKey + ".png")),
                          $"'{ChallengeArt.KeeperKey}.png' is not on disk; cut it with Tools/make_push_art.py");
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
