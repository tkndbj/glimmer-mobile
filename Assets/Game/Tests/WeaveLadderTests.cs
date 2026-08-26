using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Every Lightweave grove that ships: that they still ask what they were authored to ask.
    ///
    /// <para>
    /// <b>A weave level authors a shape, a bead count and a seed, and the board is generated</b> —
    /// so unlike a glade there is nothing in the content file to read to find out whether it is
    /// hard. Its difficulty is a property of <see cref="WeaveGenerator"/>, which means a change to
    /// the generator silently re-deals every grove in the game. Nothing else in this project can
    /// notice that: <c>Validate Content</c> would still pass, because every re-dealt board is
    /// still solvable. The chapter would simply stop being a ladder, and the first anybody would
    /// know is the retention curve.
    /// </para>
    /// <para>
    /// So the ladder is pinned twice over. <see cref="TheLadderStillDealsTheBoardsItWasAuthoredFor"/>
    /// runs the shipped generator and solver against the numbers the survey measured, and
    /// <see cref="TheShippedChapterAuthorsExactlyThisLadder"/> proves the content file still asks
    /// for those shapes and seeds. Either half alone is half a guard — the first would pass while
    /// the chapter authored something else entirely, and the second would pass while the
    /// generator dealt something else entirely.
    /// </para>
    /// <para>
    /// <b>Slack</b> is the least total detour any arrangement of a grove has, over and above every
    /// pair's own shortest possible route. Zero is the one shape of easy that survives any amount
    /// of size and colour: every pair goes as directly as it could, all at once, and the board is
    /// joined by drawing the obvious line at each critter in turn. All ten of the original seeds
    /// were in that state and the mode came back from play as "each critter is literally next to
    /// their matching light". It is meant to <em>climb</em> down the chapter.
    /// </para>
    /// <para>
    /// <b>Ways</b> is how many arrangements land within a couple of cells of the best one — how
    /// much of what a tidy player tries will work. It is meant to <em>fall</em>. A ladder needs
    /// both numbers and for a long time had neither: a grove can admit a handful of near-best
    /// arrangements and still let every pair go straight, and one can force a long detour and
    /// still leave a hundred ways to pay for it.
    /// </para>
    /// <para>
    /// A detour is always even — a route and the floor it is measured against share a parity — so
    /// <c>2</c> is the smallest slack there is and the bar reads as "not everybody at once"
    /// rather than as a number somebody picked.
    /// </para>
    /// </summary>
    public sealed class WeaveLadderTests
    {
        /// <summary>One authored rung: its shape, its beads, its seed, and what that board measured.</summary>
        readonly struct Rung
        {
            public readonly string Id;
            public readonly int Width, Height, Pairs, Beads, Seed, Slack, Ways, Par;

            public Rung(string id, int width, int height, int pairs, int beads, int seed,
                        int slack, int ways, int par)
            {
                Id = id;
                Width = width;
                Height = height;
                Pairs = pairs;
                Beads = beads;
                Seed = seed;
                Slack = slack;
                Ways = ways;
                Par = par;
            }
        }

        /// <summary>One chapter of the mode: the file it ships in, and the ladder inside it.</summary>
        sealed class Chapter
        {
            public readonly string File;
            public readonly Rung[] Rungs;

            public Chapter(string file, Rung[] rungs)
            {
                File = file;
                Rungs = rungs;
            }

            public Rung Opening => Rungs[0];
            public Rung Finale => Rungs[Rungs.Length - 1];
            public override string ToString() => File;
        }

        /// <summary>
        /// The Weftwood: the mode's first chapter, which teaches it.
        ///
        /// Every seed here was chosen by sweeping its shape for a board landing in that rung's
        /// intended band — not picked, and not left to the id hash, which deals a perfectly
        /// solvable board of entirely unknown difficulty.
        /// </summary>
        static readonly Rung[] Weftwood =
        {
            new Rung("w01_first_weave",       5, 6, 3, 0,    3, 2, 230, 19),
            new Rung("w01_two_threads",       5, 7, 4, 0,   16, 2, 140, 29),
            new Rung("w01_the_shuttle",       6, 6, 4, 1,  138, 4,  93, 33),
            new Rung("w01_tight_warp",        6, 7, 4, 2,    3, 4,  62, 38),
            new Rung("w01_five_lanterns",     6, 8, 5, 2,   60, 4,  46, 43),
            new Rung("w01_the_long_skein",    7, 7, 5, 3,  321, 6,  16, 47),
            new Rung("w01_close_quarters",    7, 8, 5, 3,  137, 6,  12, 56),
            new Rung("w01_six_sleepers",      7, 8, 6, 4, 2935, 6,   9, 56),
            new Rung("w01_the_tangle",        7, 9, 6, 4, 2358, 8,   4, 61),
            new Rung("w01_the_weftwood_knot", 7, 9, 6, 5, 3179, 8,   2, 64),
        };

        /// <summary>
        /// The Nightloom: the mode's second chapter, which asks.
        ///
        /// <para>
        /// <b>It opens on the hardest thing the Weftwood ever did and climbs from there.</b>
        /// Every grove here wears all six colours, where the Weftwood reached six only at its
        /// eighth; every grove forces at least the eight cells of detour the Weftwood's finale
        /// forced, and the last one forces sixteen. What is left to climb is exactly what
        /// invariant 20f says the mode's difficulty is — how much the pairs are in each other's
        /// way — so slack does most of the work, and the boards grow and gain their sixth ring
        /// underneath it.
        /// </para>
        /// <para>
        /// <b>Ways opens high and that is not a slip.</b> It counts arrangements within
        /// <c>WeaveSolver.Latitude</c> of the <em>best</em> one, and the best one here is already
        /// eight cells out of everybody's way — so the count is taken in a band this chapter's
        /// first grove reaches and the Weftwood's last never did. Comparing the two numbers across
        /// chapters compares two different bands; what is comparable is slack, and it is higher on
        /// every rung.
        /// </para>
        /// <para>
        /// Chosen by <c>Tools/weave_seeds.py</c>, which runs <c>WeaveSeedSearch</c> — the same
        /// rule <c>Survey Lightweave</c> sweeps with — over a hundred and forty thousand seeds per
        /// shape, and every one of these ten was then re-measured on <em>both</em> .NET 8 and
        /// Unity's own Mono and agreed (<c>weave_seeds.py confirm</c>).
        /// </para>
        /// </summary>
        static readonly Rung[] Nightloom =
        {
            new Rung("w02_dusk_threads",       7, 10, 6, 5,  23493,  8, 305, 65),
            new Rung("w02_the_narrow_loom",    7, 10, 6, 5,   9571, 10, 239, 63),
            new Rung("w02_the_pinch",          8,  9, 6, 5,  51479, 10, 189, 67),
            new Rung("w02_who_yields",         8,  9, 6, 5, 108184, 12, 141, 65),
            new Rung("w02_six_rings",          8,  9, 6, 6,  61031, 12, 118, 68),
            new Rung("w02_spindlewood",        8, 10, 6, 6,  13224, 12,  75, 72),
            new Rung("w02_the_long_way_round", 8, 10, 6, 6,  56254, 14,  40, 68),
            new Rung("w02_the_shuttered_loom", 8, 10, 6, 6,  58743, 14,  30, 72),
            new Rung("w02_thread_the_dark",    8, 10, 6, 6,  72099, 14,  13, 74),
            new Rung("w02_the_nightloom_knot", 8, 10, 6, 6, 104439, 16,   7, 72),
        };

        /// <summary>
        /// Every chapter of the mode, in play order.
        ///
        /// <para>
        /// <b>A chapter is data here and every rule below is asked of all of them.</b> The file
        /// held one chapter's ladder when the mode had one, and the obvious way to add a second
        /// was to copy it — which would have been two copies of "what makes a Lightweave grove
        /// worth playing" for a drop to put out of step, invariant 9a's lesson in the file that
        /// exists to stop exactly this mode drifting.
        /// </para>
        /// </summary>
        static readonly Chapter[] Chapters =
        {
            new Chapter("w01_lightweave", Weftwood),
            new Chapter("w02_nightloom", Nightloom),
        };

        /// <summary>Every grove the mode ships, whichever chapter it is in.</summary>
        static IEnumerable<Rung> Ladder
        {
            get
            {
                foreach (var chapter in Chapters)
                    foreach (var rung in chapter.Rungs)
                        yield return rung;
            }
        }

        /// <summary>
        /// How hard the measurement tries here, and it must match what <c>WeaveSurvey</c> sweeps
        /// with — a board the seed search could decide and this cannot is a rung that fails the
        /// moment it is authored.
        /// </summary>
        const int Cap = 500, Budget = 2_000_000;

        /// <summary>
        /// The least slack a shipped grove may have, and it is <c>WeaveGenerator.MinSlack</c>
        /// spelled out rather than referenced.
        ///
        /// A test that read the number it is checking from the code under test would agree with
        /// whatever that code did — which is exactly how a bar can be relaxed to nothing and take
        /// its own guard with it.
        /// </summary>
        const int MinSlack = 2;

        static WeaveLayout Grove(Rung rung)
            => WeaveGenerator.Build(rung.Width, rung.Height, rung.Pairs, (uint)rung.Seed,
                                    rung.Beads);

        // ------------------------------------------------------------------ the boards
        [Test]
        public void TheLadderStillDealsTheBoardsItWasAuthoredFor()
        {
            foreach (var rung in Ladder)
            {
                var grove = Grove(rung);
                var tally = WeaveSolver.Measure(grove, Cap, Budget);

                Assert.IsTrue(tally.Solved && tally.Exhausted,
                    $"'{rung.Id}' can no longer be measured inside {Budget} positions, so the "
                    + "generator has re-dealt it into something bigger than the survey measured");

                Assert.AreEqual(rung.Slack, tally.Slack,
                    $"'{rung.Id}' now forces {tally.Slack} cell(s) of detour rather than "
                    + $"{rung.Slack}. The generator has changed and every grove in the Weftwood "
                    + "has been re-dealt with it — re-run Survey Lightweave and re-choose the seeds.");

                Assert.AreEqual(rung.Ways, tally.Ways,
                    $"'{rung.Id}' now admits {tally.Ways} near-best arrangements rather than "
                    + $"{rung.Ways}, so the generator has re-dealt it");
            }
        }

        /// <summary>
        /// No grove of the chapter may let every pair take its shortest route at once.
        ///
        /// <para>
        /// This is the guard that did not exist, and the one that would have caught what shipped.
        /// It is deliberately separate from the exact-numbers assertion above: those numbers say
        /// the board has not changed, and this says the board is worth playing. The chapter was
        /// once authored against a count of arrangements alone, which is exactly how ten boards
        /// came out with a giveaway on every single one.
        /// </para>
        /// <para>
        /// <c>Exhausted</c> is asserted first and is not a formality. A capped search has seen
        /// some arrangements and not others, so its slack is an upper bound — it would call a
        /// board <em>harder</em> than it is, the one direction this must never be believed in.
        /// </para>
        /// </summary>
        [Test]
        public void NoGroveOfTheLadderLetsEveryChannelTakeTheShortWay()
        {
            foreach (var rung in Ladder)
            {
                var grove = Grove(rung);
                var tally = WeaveSolver.Measure(grove, Cap, Budget);

                Assert.IsTrue(tally.Solved && tally.Exhausted,
                    $"'{rung.Id}' could not be measured inside {Budget} positions, so how much it "
                    + "forces cannot be believed");

                Assert.GreaterOrEqual(tally.Slack, MinSlack,
                    $"'{rung.Id}' lets every crystal reach its critter by the shortest route "
                    + "there is, all at the same time, so the grove asks the player nothing — "
                    + "re-seed it with Survey Lightweave's SeedSearch");

                Assert.IsFalse(tally.Taut);
            }
        }

        /// <summary>
        /// The cheap half of the same bar, which is the half that runs on the player's phone.
        ///
        /// <see cref="WeaveSolver.AnyTautSolution"/> is this search with an excess budget of
        /// zero, which is small enough to deal a board on a handset — and it is what the
        /// generator holds out for. A rung failing this has been dealt by a generator that no
        /// longer does, which is the failure that would ship silently.
        /// </summary>
        [Test]
        public void EveryGroveWasDealtByAGeneratorHoldingOutForContention()
        {
            foreach (var rung in Ladder)
            {
                var grove = Grove(rung);

                bool taut = WeaveSolver.AnyTautSolution(grove, out bool decided);

                Assert.IsTrue(decided,
                    $"'{rung.Id}' could not be decided inside the bar's own budget, so the "
                    + "generator would have refused this board rather than dealt it");
                Assert.IsFalse(taut,
                    $"'{rung.Id}' admits an arrangement in which every pair goes straight");
            }
        }

        [Test]
        public void NoGroveOfTheLadderHasAPairJoinableByAReflex()
        {
            // The placement bar. A crystal a few cells from its critter is joined by a flick
            // rather than a decision, whatever the rest of the board is doing.
            foreach (var rung in Ladder)
            {
                var grove = Grove(rung);
                int bar = WeaveGenerator.MinReach(grove.Width, grove.Height);

                for (int p = 0; p < grove.Pairs.Count; p++)
                {
                    int apart = grove.Distance(grove.Pairs[p].Heart, grove.Pairs[p].Critter) + 1;
                    Assert.GreaterOrEqual(apart, bar,
                        $"'{rung.Id}' channel {p} has its ends {apart} cell(s) apart, under the "
                        + $"{bar} this grove requires");
                }
            }
        }

        [Test]
        public void EveryGroveOfTheLadderIsSolvableAndCarriesTheBeadsItAuthored()
        {
            // Checked by playing the generator's own arrangement through the rules the player is
            // held to, rather than trusting it — and played to IsSolved, which is what proves
            // the beads as well as the channels.
            foreach (var rung in Ladder)
            {
                var grove = Grove(rung);

                Assert.IsTrue(grove.IsComplete,
                    $"'{rung.Id}' leaves {grove.Count - grove.SolutionLength} cell(s) untouched, "
                    + "so its endpoints are not spread across the grove");

                Assert.AreEqual(rung.Beads, grove.Beads.Count,
                    $"'{rung.Id}' was dealt {grove.Beads.Count} bead(s) rather than {rung.Beads}");

                var board = new WeaveBoard(grove);
                Assert.IsTrue(board.DrawSolution(),
                              $"'{rung.Id}' cannot be solved by its own solution");
                Assert.IsTrue(board.IsSolved, $"'{rung.Id}' did not finish");
            }
        }

        [Test]
        public void EveryBeadOfTheLadderSitsOffItsOwnPairsDirectRoute()
        {
            // A bead a player threads on the way past is decoration — invariant 5d's rule for
            // this mode, checked on the boards that actually ship rather than on a sweep.
            foreach (var rung in Ladder)
            {
                var grove = Grove(rung);

                foreach (var bead in grove.Beads)
                {
                    var ends = grove.Pairs[bead.Pair];
                    int direct = grove.Distance(ends.Heart, ends.Critter);
                    int around = grove.Distance(ends.Heart, bead.Cell)
                               + grove.Distance(bead.Cell, ends.Critter);

                    Assert.Greater(around, direct,
                        $"'{rung.Id}' has a bead on cell {bead.Cell} that lies on a shortest "
                        + "route for its own pair, so threading it asks for nothing");
                }
            }
        }

        [Test]
        public void TheGrovesGrowAndTheWaysThroughThemNarrow()
        {
            // A chapter, in one assertion. Size, colour count and beads decide how much there is
            // to do; slack decides how much of it the board makes you pay for; ways decides how
            // much of what you try will work. The first four climb and the last falls, which is
            // what makes ten groves a chapter rather than ten groves.
            //
            // Asked of each chapter separately and never across the join, deliberately. Every
            // reading here is taken relative to that board's own best arrangement, so two rungs
            // are only comparable while the ladder is climbing continuously; a chapter that opens
            // where the last one closed starts a fresh band, and comparing the two chapters is
            // TheSecondChapterAsksMoreOfEveryGroveThanTheFirst's job.
            foreach (var chapter in Chapters)
                for (int i = 1; i < chapter.Rungs.Length; i++)
                {
                    var below = chapter.Rungs[i - 1];
                    var rung = chapter.Rungs[i];

                    Assert.GreaterOrEqual(rung.Width * rung.Height, below.Width * below.Height,
                        $"'{rung.Id}' is a smaller grove than '{below.Id}'");
                    Assert.GreaterOrEqual(rung.Pairs, below.Pairs,
                        $"'{rung.Id}' has fewer channels than '{below.Id}'");
                    Assert.GreaterOrEqual(rung.Beads, below.Beads,
                        $"'{rung.Id}' has fewer beads than '{below.Id}'");
                    Assert.GreaterOrEqual(rung.Slack, below.Slack,
                        $"'{rung.Id}' forces less detour than '{below.Id}'");
                    Assert.Less(rung.Ways, below.Ways,
                        $"'{rung.Id}' admits more arrangements than '{below.Id}', so the chapter "
                        + "gets easier here rather than harder");
                }
        }

        /// <summary>
        /// The Nightloom asks more of every grove than the Weftwood ever did.
        ///
        /// <para>
        /// <b>What is compared, and what deliberately is not.</b> Slack is the mode's difficulty
        /// in one integer (invariant 20f) and it is an absolute reading — how many cells of light,
        /// over and above every pair's own floor, the board makes somebody spend — so it is the
        /// one number that means the same thing in both chapters. Ways is not: it counts
        /// arrangements within <c>WeaveSolver.Latitude</c> of the <em>best</em> one, and the best
        /// one moves with slack, so a second chapter's opening count is taken in a band the first
        /// chapter's finale never reached. Asserting it fell across the join would be a rule about
        /// two different measurements, and the only way to satisfy it would be to author a
        /// gentler second chapter.
        /// </para>
        /// <para>
        /// The colour count is the other absolute reading and it is at its ceiling throughout —
        /// six is every mix the light makes, because white is what being awake looks like.
        /// </para>
        /// </summary>
        [Test]
        public void TheSecondChapterAsksMoreOfEveryGroveThanTheFirst()
        {
            for (int c = 1; c < Chapters.Length; c++)
            {
                var below = Chapters[c - 1];
                var chapter = Chapters[c];

                foreach (var rung in chapter.Rungs)
                {
                    Assert.GreaterOrEqual(rung.Slack, below.Finale.Slack,
                        $"'{rung.Id}' forces {rung.Slack} cell(s) of detour, less than the "
                        + $"{below.Finale.Slack} '{below.Finale.Id}' already forced at the end of "
                        + $"'{below.File}' — a later chapter that asks less is a ladder with a "
                        + "step down in it");

                    Assert.GreaterOrEqual(rung.Pairs, below.Finale.Pairs,
                        $"'{rung.Id}' wears fewer colours than '{below.Finale.Id}'");

                    Assert.GreaterOrEqual(rung.Width * rung.Height,
                                          below.Finale.Width * below.Finale.Height,
                        $"'{rung.Id}' is a smaller grove than '{below.Finale.Id}'");
                }

                Assert.Greater(chapter.Finale.Slack, below.Finale.Slack,
                    $"'{chapter.File}' closes on no more detour than '{below.File}' did");
            }
        }

        [Test]
        public void TheChapterOpensGentlyAndClosesOnEveryColourTheLightMakes()
        {
            // The mode's *first* chapter, which is the one that has to teach it. A later chapter
            // opens on what this one closed on and is held to that by
            // TheSecondChapterAsksMoreOfEveryGroveThanTheFirst.
            Assert.AreEqual(3, Weftwood[0].Pairs, "the first grove should teach with as few "
                                                  + "channels as the mode allows");
            Assert.AreEqual(0, Weftwood[0].Beads, "and with nothing added to the mode's own rule");
            Assert.AreEqual(0, Weftwood[1].Beads,
                "the second grove is beadless too, so the two lessons this mode has to teach "
                + "never land on the same board — see WeaveScreen.OnPresented");
            Assert.Greater(Weftwood[2].Beads, 0, "and the third is where a bead is met");

            foreach (var chapter in Chapters)
                Assert.AreEqual(WeaveGenerator.Palette.Length, chapter.Finale.Pairs,
                    $"'{chapter.File}' should close wearing every colour the light makes");
        }

        /// <summary>
        /// A later chapter teaches nothing, and its boards say so.
        ///
        /// <para>
        /// The mode's two lessons — that it is dragged, and that a channel goes <em>through</em>
        /// a ring rather than stopping at it — are shown once, on a board with room to show them
        /// (see <c>WeaveScreen.OnPresented</c>). A second chapter is met by a player who has
        /// already seen both, so it may open at full strength: every colour, and rings from its
        /// first grove. This is the assertion that would fail if somebody softened it into a
        /// second tutorial.
        /// </para>
        /// </summary>
        [Test]
        public void EveryChapterAfterTheFirstOpensAtFullStrength()
        {
            for (int c = 1; c < Chapters.Length; c++)
            {
                var opening = Chapters[c].Opening;

                Assert.AreEqual(WeaveGenerator.Palette.Length, opening.Pairs,
                    $"'{opening.Id}' opens on fewer than every colour, so it re-teaches the "
                    + "palette a player already met");
                Assert.Greater(opening.Beads, 0,
                    $"'{opening.Id}' opens with no ring on it, so it re-teaches the ring");
            }
        }

        // ------------------------------------------------------------------ the palette
        [Test]
        public void EveryColourAGroveCanDealIsDistinctAndNoneOfThemIsWhite()
        {
            // White is Energy.All, which is the colour a woken critter is tinted. A pair wearing
            // it would be a critter whose sleeping colour is the colour of being awake, so the
            // one thing the board most has to say at a glance is the one thing it could not.
            var seen = new HashSet<int>();

            foreach (int colour in WeaveGenerator.Palette)
            {
                Assert.AreNotEqual(Energy.None, colour, "a pair cannot be dealt the dark");
                Assert.AreNotEqual(Energy.All, colour,
                    "a pair was dealt white, which is what being awake looks like");
                Assert.IsTrue(seen.Add(colour),
                    "two pairs share a colour, so the player cannot tell which crystal belongs "
                    + "to which critter");
            }
        }

        // ------------------------------------------------------------------ the arithmetic
        [Test]
        public void AGroveIsDealtByIntegerArithmeticAndIsTheSameBoardOnEveryRuntime()
        {
            // The bug this pins was found by this very file passing in the Editor and failing
            // offline. The walk budget was (int)(free / (float)walksLeft * 1.3f), and 1.3 has no
            // exact binary form: thirty cells free across three walks computes 12.99999952...,
            // which truncates to 13 in single precision and to 12 once promoted to double. Both
            // are legal, and Unity's Mono and .NET 8 disagreed — so the same seed dealt two
            // different boards, and a level proved solvable on a desktop was not necessarily the
            // level generated on the phone.
            //
            // Asserted as the exact fraction rather than by re-deriving it, because a test that
            // recomputes the expression it is checking would have agreed with the bug.
            Assert.AreEqual(13, WeaveGenerator.SlackNumerator);
            Assert.AreEqual(10, WeaveGenerator.SlackDenominator);

            // The case that actually diverged: 30 free cells and 3 walks.
            Assert.AreEqual(13, 30 * WeaveGenerator.SlackNumerator / (3 * WeaveGenerator.SlackDenominator),
                            "the budget for a three-walk carve is no longer the exact 13");
        }

        [Test]
        public void DealingTheSameGroveTwiceGivesTheSameBoard()
        {
            // Cheap, and it is the property everything else here rests on: a retry has to meet
            // the board the player just failed, and a report about a bad grove has to be
            // reproducible from the level id alone.
            foreach (var rung in Ladder)
            {
                var a = Grove(rung);
                var b = Grove(rung);

                Assert.AreEqual(a.Par, b.Par, rung.Id);
                Assert.AreEqual(a.Beads.Count, b.Beads.Count, rung.Id);

                for (int p = 0; p < a.Pairs.Count; p++)
                {
                    Assert.AreEqual(a.Pairs[p].Heart, b.Pairs[p].Heart, rung.Id);
                    Assert.AreEqual(a.Pairs[p].Critter, b.Pairs[p].Critter, rung.Id);
                    Assert.AreEqual(a.Straight(p), b.Straight(p), rung.Id);
                }
                for (int i = 0; i < a.Beads.Count; i++)
                    Assert.AreEqual(a.Beads[i].Cell, b.Beads[i].Cell, rung.Id);
            }
        }

        // ------------------------------------------------------------------ the content file
        static string PathOf(Chapter chapter) =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "StreamingAssets", "Content",
                                          "chapters", chapter.File + ".json"));

        static ChapterBody Read(Chapter chapter)
        {
            string path = PathOf(chapter);
            Assert.IsTrue(File.Exists(path), "no Lightweave chapter at " + path);

            var problems = new List<string>();
            Assert.IsTrue(ContentMapper.TryReadChapter(File.ReadAllText(path), problems,
                                                       out var body),
                          $"'{chapter.File}' did not read: " + string.Join("; ", problems));
            CollectionAssert.IsEmpty(problems);
            return body;
        }

        /// <summary>
        /// Reads the shipped chapter. Editor-only, because <c>JsonUtility</c> is a native call —
        /// the offline runner says so rather than failing this for the wrong reason.
        /// </summary>
        [Test]
        public void TheShippedChapterAuthorsExactlyThisLadder()
        {
            foreach (var chapter in Chapters) AuthorsThisLadder(chapter);
        }

        static void AuthorsThisLadder(Chapter chapter)
        {
            var body = Read(chapter);

            Assert.AreEqual(chapter.Rungs.Length, body.Count,
                            $"'{chapter.File}' no longer has {chapter.Rungs.Length} groves in it");

            for (int i = 0; i < chapter.Rungs.Length; i++)
            {
                var rung = chapter.Rungs[i];
                var level = body.Levels[i];

                Assert.AreEqual(rung.Id, level.Id.Value, $"rung {i + 1} is a different level");

                var rules = level.RulesAs<WeaveRules>();
                Assert.IsNotNull(rules, $"'{rung.Id}' is not a weave level");

                Assert.AreEqual(rung.Width, rules.Width, $"'{rung.Id}' width");
                Assert.AreEqual(rung.Height, rules.Height, $"'{rung.Id}' height");
                Assert.AreEqual(rung.Pairs, rules.PairCount, $"'{rung.Id}' pairs");
                Assert.AreEqual(rung.Beads, rules.BeadCount, $"'{rung.Id}' beads");
                Assert.AreEqual(rung.Seed, rules.Seed,
                    $"'{rung.Id}' authors seed {rules.Seed} rather than the {rung.Seed} the "
                    + "survey chose, so its difficulty is back to whatever it happens to deal");

                Assert.AreEqual(rung.Par, level.Tuning.Par,
                    $"'{rung.Id}' now grades against a par of {level.Tuning.Par} rather than "
                    + $"{rung.Par}, which moves its clock and its star lines");
            }
        }

        [Test]
        public void EveryGroveIsGradedAndLostOnCellsAndNoneAuthorsANumber()
        {
            // A weave has no turns, so all three of the lines it is measured against are counted
            // in cells of light: it is graded on what its channels spent and it is lost when the
            // grove provably cannot be finished with what is left (WeaveInk). None of that is
            // visible in the JSON, because none of it is authored — par falls out of the board
            // and the three factors are the ordinary shared ones.
            //
            // This used to assert the opposite of the third clause below: that a weave had *no*
            // budget, which was true and was the mode's one real gap — with the clock gone
            // (invariant 22) a grove could not be lost at all, only forfeited. Invariant 22a
            // named the fix and this is it, so the assertion is inverted rather than deleted:
            // an unbudgeted grove is now the regression.
            foreach (var chapter in Chapters)
            foreach (var level in Read(chapter).Levels)
            {
                var tuning = level.Tuning;

                // Three stars has to stay reachable, and on this mode that is a fact about par
                // rather than about tuning: a taut arrangement occupies one cell per step plus
                // one per pair, which is par less a cell for every bead, so it always lands
                // under the gold line. A par of zero would make the whole ladder unreachable.
                Assert.Greater(tuning.Par, 0, $"'{level.Id}' has no par to grade against");
                Assert.GreaterOrEqual(tuning.GoldThreshold, tuning.Par,
                    $"'{level.Id}' asks for fewer cells than its own shortest routes need");
                Assert.Greater(tuning.SilverThreshold, tuning.GoldThreshold,
                    $"'{level.Id}' grades two stars harder than three");

                // The ink, and the three lines in the order that keeps every star band landable
                // — the same ordering LevelValidator.CheckStarBands proves on the build.
                Assert.IsTrue(tuning.HasBudget,
                    $"'{level.Id}' is dealt no ink, so it cannot be lost — see invariant 22a");
                Assert.Greater(tuning.MoveBudget, tuning.SilverThreshold,
                    $"'{level.Id}' runs out of light at or before its two-star line, so one star " +
                    "can never be scored on it");

                // And the ink has to cover the board. The floor is what a perfect arrangement
                // costs before the grove forces a single detour, and every shipped grove forces
                // some (WeaveGenerator.MinSlack), so ink that merely matched the floor would be
                // a grove nobody could finish.
                var grove = level.RulesAs<WeaveRules>().LayoutFor(level.Id);
                Assert.Less(grove.StraightTotal * 1.2f, tuning.MoveBudget,
                    $"'{level.Id}' is dealt {tuning.MoveBudget} cells of ink against a floor of " +
                    $"{grove.StraightTotal}, which leaves no room for the detour it forces");
            }
        }

        // ------------------------------------------------------------------ the coaching hand
        /// <summary>
        /// The route a demonstration traces is one a finger could actually make.
        ///
        /// <para>
        /// The join lesson used to hand <c>CoachHand</c> two points — the crystal and the critter
        /// — so the fingertip crossed the grove diagonally, which is a movement this mode has no
        /// input for, shown to a player at the moment they are being taught what the input is.
        /// Every step is now a single orthogonal move between real cells, checked on every shipped
        /// grove, because it is animation and therefore invisible in a compile and obvious only in
        /// motion.
        /// </para>
        /// </summary>
        [Test]
        public void TheDemonstratedRouteStepsTheWayAFingerCan()
        {
            foreach (var rung in Ladder)
            {
                var grove = Grove(rung);
                var walk = grove.CoachRoute();

                Assert.Greater(walk.Length, 1, $"'{rung.Id}' has nothing to demonstrate");

                int pair = grove.EndpointAt(walk[0]);
                Assert.GreaterOrEqual(pair, 0, $"'{rung.Id}' does not start on a crystal");
                Assert.AreEqual(grove.Pairs[pair].Critter, walk[walk.Length - 1],
                                $"'{rung.Id}' does not end at that pair's critter");

                for (int i = 1; i < walk.Length; i++)
                    Assert.IsTrue(grove.Adjacent(walk[i - 1], walk[i]),
                                  $"'{rung.Id}' step {i} is not a single orthogonal move");

                // Never over somebody else's crystal — an illegal demonstration reads as
                // permission, where an impossible one only reads as decoration.
                for (int i = 1; i < walk.Length - 1; i++)
                    Assert.AreEqual(-1, grove.EndpointAt(walk[i]),
                                    $"'{rung.Id}' runs over an endpoint at step {i}");

                // One corner. A demonstration that wanders teaches that the mode is fiddly.
                int turns = 0;
                for (int i = 1; i < walk.Length - 1; i++)
                {
                    int ax = walk[i] % grove.Width - walk[i - 1] % grove.Width;
                    int ay = walk[i] / grove.Width - walk[i - 1] / grove.Width;
                    int bx = walk[i + 1] % grove.Width - walk[i] % grove.Width;
                    int by = walk[i + 1] / grove.Width - walk[i] / grove.Width;
                    if (ax != bx || ay != by) turns++;
                }

                Assert.LessOrEqual(turns, 1, $"'{rung.Id}' turns {turns} times");

                // The corners are the ends and the turn, and nothing else — a stroke drawn
                // from them must be the same shape as the route they came from.
                var bends = grove.Corners(walk);
                Assert.AreEqual(turns + 2, bends.Length, $"'{rung.Id}' collapsed to {bends.Length} corners");
                Assert.AreEqual(walk[0], bends[0], $"'{rung.Id}' lost its start");
                Assert.AreEqual(walk[walk.Length - 1], bends[bends.Length - 1], $"'{rung.Id}' lost its end");

                // Whether it also clears every ring is not asserted here, and that is deliberate:
                // it depends on whether *this* board leaves a choice, which is a fact about how
                // crowded a grove is rather than about the rule. The rule itself —
                // ring-free wherever one is available — is
                // ARingIsAvoidedWhenTheBoardOffersARouteThatCan, on a board built to have the
                // choice, so it stays a check when the next chapter is denser still.
            }
        }
    

        /// <summary>
        /// <c>Corners</c>'s own edge cases, which no shipped board reaches and a demonstration
        /// would only ever show as a seam or a missing leg.
        /// </summary>
        [Test]
        public void CollapsingARouteToItsCornersHandlesItsEnds()
        {
            var grove = Grove(Weftwood[0]);
            int w = grove.Width;

            // A straight run keeps its two ends and gains nothing in between.
            var straight = new[] { grove.Index(1, 1), grove.Index(2, 1), grove.Index(3, 1), grove.Index(4, 1) };
            var bends = grove.Corners(straight);
            Assert.AreEqual(new[] { straight[0], straight[3] }, bends);

            // One turn is one corner, and it is the cell the turn happens on.
            var elbow = new[] { grove.Index(1, 1), grove.Index(2, 1), grove.Index(2, 2), grove.Index(2, 3) };
            Assert.AreEqual(new[] { elbow[0], elbow[1], elbow[3] }, grove.Corners(elbow));

            // Doubling back is a turn too, which a cross product alone would call straight.
            var back = new[] { grove.Index(1, 1), grove.Index(2, 1), grove.Index(1, 1) };
            Assert.AreEqual(3, grove.Corners(back).Length, "a route that reverses lost its corner");

            // Degenerate inputs answer rather than throw: nothing drawn beats a crash in a lesson.
            Assert.AreEqual(0, grove.Corners(new int[0]).Length);
            Assert.AreEqual(1, grove.Corners(new[] { grove.Index(0, 0) }).Length);
            Assert.AreEqual(2, grove.Corners(new[] { grove.Index(0, 0), grove.Index(1, 0) }).Length);
            Assert.AreEqual(0, grove.Corners(null).Length);

            Assert.Greater(w, 0);
        }
    }
}
