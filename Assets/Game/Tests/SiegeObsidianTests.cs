using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The curse (<see cref="SiegeLayout.Obsidian"/>), one clause at a time: how the stone is
    /// dealt, what it lines up with, what a break takes, when the curse falls on the hill, who it
    /// marks, and what a mark is worth.
    ///
    /// <para>
    /// <b>Its own fixture for <c>SiegeCharmTests</c>' reason</b>: these are rules about the
    /// field and the one door harm goes through, and every case needs a board with stones stood
    /// on known cells, which is a seam (<c>SiegeBoard.Lay</c>) nothing else uses. The chapter
    /// that deals them is measured by <c>SiegeRuleTests.TheNinthChapterIsFoughtOnABoughtLine</c>.
    /// </para>
    /// <para>
    /// <b>The first two cases are the ones that let it ship without touching a shipped
    /// chapter</b>: a field that deals no curse deals exactly what it always did, and a cursed
    /// field draws from the stream exactly as often as a plain one (invariant 41).
    /// </para>
    /// </summary>
    public sealed class SiegeObsidianTests
    {
        const string Gems = "rgby";
        const string Wards = "rgby";
        static readonly string[] Waves = { "rgbyrgbyrgby", "rgbyrgbyrgby" };

        /// <summary>
        /// A settled field with no three alike anywhere: every row is the last shifted by one, so
        /// no column repeats either.
        /// </summary>
        static readonly string[] Field =
        {
            "rgbyrgby",
            "gbyrgbyr",
            "byrgbyrg",
            "yrgbyrgb",
            "rgbyrgby",
        };

        static SiegeLayout Layout(bool cursed, string charms = null)
        {
            Assert.IsTrue(ProtoGrid.TryRead(Field, Field[0].Length, Field.Length,
                                            SiegeLayout.Cells, out var grid, out string error),
                          error);

            var layout = new SiegeLayout(grid, Gems, Wards, Waves, null, 0, null, 0, charms,
                                         cursed);
            Assert.IsNull(layout.Fault, layout.Fault);
            return layout;
        }

        static SiegeBoard Board(bool cursed = true) => SiegeBoard.Build(Layout(cursed));

        // ----------------------------------------------------------------- the deal
        /// <summary>
        /// **A cursed field draws from the stream exactly as often as a plain one**: every deal
        /// either is the gem the plain field dealt from the same draw, or is an obsidian in its
        /// place - never a different gem, which would be a second draw somewhere.
        /// </summary>
        [Test]
        public void DealingAnObsidianCostsNoExtraDraw()
        {
            var plain = Board(false);
            var cursed = Board(true);

            int stones = 0;
            for (int i = 0; i < 4000; i++)
            {
                char a = plain.Deal(9, out _);
                char b = cursed.Deal(9, out _);

                if (b == SiegeLayout.Obsidian) { stones++; continue; }
                Assert.AreEqual(a, b, $"draw {i}: the cursed field dealt a different gem, so it "
                                      + "drew from the stream a different number of times");
            }

            Assert.Greater(stones, 0, "a cursed field never dealt an obsidian");
        }

        [Test]
        public void APlainFieldNeverDealsAnObsidian()
        {
            var plain = Board(false);
            for (int i = 0; i < 4000; i++)
                Assert.AreNotEqual(SiegeLayout.Obsidian, plain.Deal(i % 40, out _),
                                   "a field that does not deal the curse dealt a stone");
        }

        /// <summary>
        /// About <see cref="SiegeTuning.ObsidianPercent"/> in a hundred, and never more - the
        /// "like the other gems, but fewer" of the brief. A little under the rate, because a
        /// stone that would land lined deals the gem instead.
        /// </summary>
        [Test]
        public void ObsidiansAreDealtLikeGemsButFewer()
        {
            var cursed = Board(true);
            int stones = 0, draws = 20000;

            for (int i = 0; i < draws; i++)
                if (cursed.Deal(i % 40, out _) == SiegeLayout.Obsidian) stones++;

            float share = stones * 100f / draws;
            Assert.That(share, Is.InRange(SiegeTuning.ObsidianPercent * .6f,
                                          SiegeTuning.ObsidianPercent * 1.1f),
                        $"obsidians were {share:F1}% of the deal against a rate of "
                        + $"{SiegeTuning.ObsidianPercent}%");

            // Rarer than any one of the four colours, which is what "fewer" means.
            Assert.Less(share, 100f / Gems.Length);
        }

        /// <summary>
        /// **A stone never lands already lined** (invariant 37el, strictly): two obsidians beside
        /// a cell and the deal into it is always a gem, or a curse would break that nobody
        /// gathered.
        /// </summary>
        [Test]
        public void ADealtObsidianNeverLandsLined()
        {
            var cursed = Board(true);
            cursed.Lay(0, SiegeLayout.Obsidian);
            cursed.Lay(1, SiegeLayout.Obsidian);

            for (int i = 0; i < 4000; i++)
                Assert.AreNotEqual(SiegeLayout.Obsidian, cursed.Deal(2, out _),
                                   "an obsidian was dealt into a cell where it made three");
        }

        /// <summary>A charm rides a colour and an obsidian has none, so the two never share a cell.</summary>
        [Test]
        public void AnObsidianNeverCarriesACharm()
        {
            var board = SiegeBoard.Build(Layout(true, SiegeCharms.Letters));

            for (int i = 0; i < 20000; i++)
            {
                char gem = board.Deal(i % 40, out var charm);
                if (gem == SiegeLayout.Obsidian)
                    Assert.AreEqual(SiegeCharm.None, charm, "a charm was dealt onto an obsidian");
            }
        }

        // ----------------------------------------------------------------- what lines up
        [Test]
        public void ThreeObsidiansAreARunAndNothingJoinsThem()
        {
            char[] row = "ooorgbyr".ToCharArray();
            var hit = SiegeLayout.Runs(row, 8, 1);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, hit);

            // **A prism does not join a curse** - a wild joins a run of any colour, and an
            // obsidian is not one. "o P o" is two stones and a wild, and nothing.
            char[] wild = "orogbygb".ToCharArray();
            var charms = new SiegeCharm[8];
            charms[1] = SiegeCharm.Prism;
            Assert.IsEmpty(SiegeLayout.Runs(wild, 8, 1, charms),
                           "a prism completed a run of obsidians");

            // Two is not three.
            Assert.IsEmpty(SiegeLayout.Runs("oorgbyrg".ToCharArray(), 8, 1));
        }

        /// <summary>
        /// <see cref="SiegeLayout.Lined"/> is <see cref="SiegeLayout.Runs"/> asked of one cell, and
        /// the two may not disagree on a cursed field (invariant 5b: one rule, written once).
        /// </summary>
        [Test]
        public void OneCellReadsTheSameAsTheWholeFieldOnACursedField()
        {
            var rng = new System.Random(20260928);
            const string alphabet = "rgbyo";

            for (int trial = 0; trial < 3000; trial++)
            {
                var cells = new char[40];
                for (int i = 0; i < cells.Length; i++) cells[i] = alphabet[rng.Next(alphabet.Length)];

                var hit = SiegeLayout.Runs(cells, 8, 5);

                for (int i = 0; i < cells.Length; i++)
                    Assert.AreEqual(hit.Contains(i), SiegeLayout.Lined(cells, 8, 5, null, i),
                                    $"trial {trial}, cell {i}: the one-cell reading and the "
                                    + $"whole-field reading disagree on '{new string(cells)}'");
            }
        }

        // ----------------------------------------------------------------- the break
        /// <summary>
        /// **Three in a line break the curse and take every obsidian on the field**, and a stone
        /// pays no ward - so the beat that broke it fuels nothing.
        /// </summary>
        [Test]
        public void ABreakTakesEveryObsidianOnTheFieldAndFuelsNothing()
        {
            var board = Board(true);

            // "o o b o" on the top row: swapping the third and fourth cells makes three.
            board.Lay(0, SiegeLayout.Obsidian);
            board.Lay(1, SiegeLayout.Obsidian);
            board.Lay(3, SiegeLayout.Obsidian);

            // Two more, nowhere near it.
            board.Lay(21, SiegeLayout.Obsidian);
            board.Lay(38, SiegeLayout.Obsidian);

            var turn = board.Swap(2, 3);
            Assert.IsNotNull(turn, "three obsidians in a line is not a legal swap");

            var beat = turn.Beats[0];
            Assert.IsTrue(beat.Unbound, "the curse did not break");
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 21, 38 }, beat.Broken,
                                           "the break did not take every stone on the field");
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, beat.Breakers);

            for (int w = 0; w < beat.Fuel.Length; w++)
                Assert.AreEqual(0f, beat.Fuel[w], $"an obsidian paid ward {w}");

            foreach (int cell in beat.Broken)
                Assert.IsTrue(beat.Cleared.Contains(cell), $"stone {cell} broke and was not cleared");
        }

        /// <summary>A stone swept up by anything but a run of stones breaks nothing.</summary>
        [Test]
        public void ARunOfGemsBesideAStoneBreaksNothing()
        {
            var board = Board(true);
            board.Lay(8, SiegeLayout.Obsidian);

            // Make a red run in the top row: "r g b y r g b y" -> put reds at 0,1,2.
            board.Lay(1, 'r');
            board.Lay(3, 'r');

            var turn = board.Swap(2, 3);
            Assert.IsNotNull(turn);
            Assert.IsFalse(turn.Beats[0].Unbound, "a run of gems broke the curse");
        }

        // ----------------------------------------------------------------- the hill
        /// <summary>
        /// **The curse falls with the fuel and not before** (invariant 37s), and it marks every
        /// body standing on the hill at that moment and nothing still in the wings.
        /// </summary>
        [Test]
        public void TheCurseFallsWithTheFuelAndMarksOnlyWhatIsStanding()
        {
            var board = Board(true);

            // Walk the first wave on.
            for (int i = 0; i < 60 * 8 && board.OnTheHill < 3; i++) board.Advance(1f / 60f);
            Assert.GreaterOrEqual(board.OnTheHill, 3, "the first wave never walked on");

            board.Lay(0, SiegeLayout.Obsidian);
            board.Lay(1, SiegeLayout.Obsidian);
            board.Lay(3, SiegeLayout.Obsidian);
            Assert.IsNotNull(board.Swap(2, 3));

            float lands = SiegeTuning.FuelLands(0);
            float t = 0f;
            float hex = 0f;
            var marked = new List<int>();
            var standing = new HashSet<int>();

            while (t < lands + .5f && hex <= 0f)
            {
                // **Who is standing as the step begins**, because the curse lands first in a step
                // (`SiegeBoard.Land`) and a body that walks on later in the same step was, by the
                // rule, in the wings when it fell.
                standing.Clear();
                foreach (var raider in board.Raiders)
                    if (raider.Alive && raider.OnTheHill) standing.Add(raider.Id);

                var report = board.Advance(1f / 60f);
                t += 1f / 60f;

                if (report.Hex > 0f)
                {
                    hex = report.Hex;
                    marked.AddRange(report.Hexed);
                    Assert.GreaterOrEqual(t, lands - 1f / 30f, "the curse fell before its fuel");
                }
                else
                {
                    foreach (var raider in board.Raiders)
                        Assert.IsFalse(raider.Cursed, "a body was hexed before the curse fell");
                }
            }

            Assert.AreEqual(SiegeTuning.HexFor(3), hex, "a break of three did not lay its hex");
            Assert.IsNotEmpty(marked, "the curse fell on a hill with raiders and marked nobody");
            CollectionAssert.AreEquivalent(standing, marked,
                                           "the curse marked other than exactly who was standing");
        }

        /// <summary>
        /// **A hexed body takes half again from every bolt** - two boards dealt alike, one of them
        /// cursed, and the first bolt of each compared.
        /// </summary>
        [Test]
        public void AHexedBodyTakesHalfAgainFromTheLine()
        {
            var plain = Board(true);
            var cursed = Board(true);

            for (int i = 0; i < 60 * 30; i++)
            {
                for (int w = 0; w < 4; w++)
                {
                    plain.Wards[w].Fuel = plain.Wards[w].Capacity;
                    cursed.Wards[w].Fuel = cursed.Wards[w].Capacity;
                }

                // Hexed *before* the step, and only those: a body minted and shot inside one
                // step was never standing when anything could mark it (`SiegeRaider.Hex`).
                var marked = new HashSet<int>();
                foreach (var raider in cursed.Raiders)
                    if (raider.Hex(5f)) marked.Add(raider.Id);

                var a = plain.Advance(1f / 60f);
                var b = cursed.Advance(1f / 60f);

                if (a.Bolts.Count == 0) continue;

                Assert.AreEqual(a.Bolts.Count, b.Bolts.Count, "the two boards stopped moving alike");

                var first = a.Bolts[0];
                var hexed = b.Bolts[0];
                Assert.AreEqual(first.Raider, hexed.Raider);

                // Unless the target was never marked, or the hexed bolt was clamped by what the
                // body had left - in either case the next bolt is asked instead.
                if (!marked.Contains(hexed.Raider) || hexed.Killed || first.Killed) continue;

                Assert.AreEqual(SiegeTuning.Hexing(first.Damage), hexed.Damage,
                                "a hexed body took what an unhexed one took");
                return;
            }

            Assert.Fail("no bolt ever landed");
        }

        [Test]
        public void AHexIsExtendedNeverStackedAndRunsOut()
        {
            var board = Board(true);
            for (int i = 0; i < 60 * 8 && board.OnTheHill < 1; i++) board.Advance(1f / 60f);

            SiegeRaider body = null;
            foreach (var raider in board.Raiders) if (raider.Alive && raider.OnTheHill) { body = raider; break; }
            Assert.IsNotNull(body);

            Assert.IsTrue(body.Hex(6f));
            body.Hex(3f);
            Assert.AreEqual(6f, body.Hexed, 1e-4, "a shorter hex cut a longer one short");
            body.Hex(8f);
            Assert.AreEqual(8f, body.Hexed, 1e-4, "a longer hex did not extend");

            for (int i = 0; i < 60 * 9 && body.Alive; i++) board.Advance(1f / 60f);
            if (body.Alive) Assert.IsFalse(body.Cursed, "a hex never ran out");
        }

        [Test]
        public void TheHexGrowsWithTheStonesAndIsCapped()
        {
            Assert.AreEqual(0f, SiegeTuning.HexFor(2), "two stones broke a curse");
            Assert.AreEqual(SiegeTuning.HexBase, SiegeTuning.HexFor(SiegeTuning.MinRun));
            Assert.Greater(SiegeTuning.HexFor(5), SiegeTuning.HexFor(4));
            Assert.AreEqual(SiegeTuning.HexMost, SiegeTuning.HexFor(40));

            Assert.AreEqual(30, SiegeTuning.Hexing(20));
            Assert.AreEqual(1, SiegeTuning.Hexing(1), "a hex made a point of damage worth less");
            Assert.AreEqual(0, SiegeTuning.Hexing(0));
        }

        // ----------------------------------------------------------------- the content
        /// <summary>
        /// **The curse is taught by the ninth chapter and kept by every chapter after it** - no
        /// shipped rung before the ninth deals one, so nothing the owner signed off has moved;
        /// every rung of the ninth and of every chapter since does, because a mechanic a chapter
        /// teaches is a tool the next chapter keeps, as the charms are (`SiegeCharms.Upto`).
        /// Read off the file names: the siege chapters are numbered in ladder order (`s02`, the
        /// Infinite lane, is under `s10` and deals none).
        /// </summary>
        [Test]
        public void EveryChapterFromTheNinthDealsTheCurse()
        {
            string chapters = Path.Combine(TestJson.RepoRoot(), "Assets", "StreamingAssets",
                                           "Content", "chapters");

            foreach (string path in Directory.GetFiles(chapters, "s*.json"))
            {
                string text = File.ReadAllText(path);
                string name = Path.GetFileNameWithoutExtension(path);

                // `s10_cogspire` is the ninth chapter of the ladder, so `s10` and every later
                // number keeps the curse.
                bool keeps = int.Parse(name.Substring(1, 2)) >= 10;

                int dealt = Regex.Matches(text, "\"obsidian\"\\s*:\\s*true").Count;
                int levels = Regex.Matches(text, "\"siege\"\\s*:").Count;

                if (keeps)
                    Assert.AreEqual(levels, dealt, $"a rung of {name} deals no curse");
                else
                    Assert.AreEqual(0, dealt, $"{Path.GetFileName(path)} deals the curse");
            }
        }

        /// <summary>
        /// **The view's colours are the art tool's**: the stone's cracks and the sigil are
        /// painted by <c>Tools/make_obsidian_art.py</c>, every piece of light the view adds is
        /// tinted from <see cref="CurseLight"/> and every piece of dark it lays on is its ink -
        /// two copies of one palette, held together.
        /// </summary>
        [Test]
        public void TheViewAndTheArtToolShareOnePalette()
        {
            string tool = File.ReadAllText(Path.Combine(TestJson.RepoRoot(), "Tools",
                                                        "make_obsidian_art.py"));

            void Same(string name, UnityEngine.Color32 view)
            {
                var m = Regex.Match(tool, "^" + name + @" = \((\d+), (\d+), (\d+)\)",
                                    RegexOptions.Multiline);
                Assert.IsTrue(m.Success, $"make_obsidian_art.py has no {name}");
                Assert.AreEqual(int.Parse(m.Groups[1].Value), view.r, $"{name} red");
                Assert.AreEqual(int.Parse(m.Groups[2].Value), view.g, $"{name} green");
                Assert.AreEqual(int.Parse(m.Groups[3].Value), view.b, $"{name} blue");
            }

            Same("VIOLET", CurseLight.Violet);
            Same("LILAC", CurseLight.Lilac);
            Same("DEEP", CurseLight.Deep);
            Same("INK", CurseLight.Ink);
        }

        /// <summary>
        /// The curse's art is on disk and is named by the mode's scope, so a cursed chapter never
        /// draws a white rectangle where a stone should be (invariant 7b).
        /// </summary>
        [Test]
        public void TheCursesArtIsOnDiskAndScoped()
        {
            string art = Path.Combine(TestJson.RepoRoot(), "Assets", "Game", "Art", "Siege");

            Assert.IsTrue(File.Exists(Path.Combine(art, "gem_obsidian.png")), "no gem_obsidian.png");
            Assert.IsTrue(File.Exists(Path.Combine(art, "hex_sigil.png")), "no hex_sigil.png");

            var names = new HashSet<string>();
            foreach (var request in new SiegeMode().Art) names.Add(request.Address);

            foreach (string key in new[] { "gem_obsidian", "hex_sigil" })
                Assert.IsTrue(names.Contains(AssetPipeline.AssetManifest.SiegeArt(key)),
                              $"the siege never names {key}, so it ships addressed and unloadable");
        }

        /// <summary>
        /// The front the curse used to ride up the hill on is gone, with its twenty-four frames,
        /// their addresses and their label (invariant 8d). A curse reaches the hill as a lash
        /// now (<c>SiegeView.Cursed</c>, MODES.md 37ex), which is drawn and ships no picture -
        /// so a frame of the reel coming back is dead weight in the global bundle, and a row of
        /// it coming back with no frame behind it fails <c>BuildPlayer</c>.
        /// </summary>
        [Test]
        public void TheFrontIsGone()
        {
            string reel = Path.Combine(TestJson.RepoRoot(), "Assets", "Game", "Art", "Siege", "hexwave");
            Assert.IsFalse(Directory.Exists(reel), "Art/Siege/hexwave is back - nothing draws it");

            foreach (var request in new SiegeMode().Art)
                Assert.IsFalse(request.Address.Contains("hexwave"), "the siege still asks for the front");

            string data = Path.Combine(TestJson.RepoRoot(), "Assets", "AddressableAssetsData");

            Assert.IsFalse(File.ReadAllText(Path.Combine(data, "AssetGroups", "Glimmer Global.asset"))
                               .Contains("Art/Siege/hexwave"), "the group still addresses the front");
            Assert.IsFalse(File.ReadAllText(Path.Combine(data, "AddressableAssetSettings.asset"))
                               .Contains("Art/Siege/hexwave"), "the settings still carry the front's label");
        }
    }
}
