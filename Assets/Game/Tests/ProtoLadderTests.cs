using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Every board of every prototype mode that ships: that it still asks what it was authored
    /// to ask.
    ///
    /// <para>
    /// <b>These boards author nothing that can be graded</b>, so everything a player is measured
    /// against is a property of <see cref="ProtoSearch"/> over the mode's own rules — par, both
    /// star lines and the allowance the run is dealt. A change to a settle order, a roll rule or a
    /// flood boundary therefore silently re-grades the level, which is <c>BudLadderTests</c>'
    /// argument for the mode that already shipped that bug.
    /// </para>
    /// <para>
    /// <b>The boards are held inline, and that is the whole point of the fixture.</b> Every rule
    /// here exists twice — once in C# and once in <c>Tools/verify/proto.py</c> — and the offline
    /// runner cannot read a JSON fixture, because <c>JsonUtility</c> is a native call and every
    /// <c>*VectorTests</c> in this project is reported as "needs the Editor" and skipped on the
    /// way past. A rule that exists twice needs at least one guard that runs where the code is
    /// edited (invariant 9a). The numbers below are the Python mirror's, so this fixture
    /// <em>is</em> the two copies being compared; it simply does it without a file.
    /// </para>
    /// <para>
    /// <b>What each number is for.</b> <c>Par</c> and the two thresholds are what the player is
    /// graded on. <c>Ways</c> is invariant 5d counted: how many shortest answers the board has, so
    /// a rule change that makes a board easier is as visible as one that makes it harder.
    /// <c>Careless</c> is what a player who always takes the biggest thing going spends — nought,
    /// deliberately, because none of these is commissioned to be effortless.
    /// <c>Nodes</c> is what the search costs on the phone that opens the level.
    /// </para>
    /// </summary>
    public sealed class ProtoLadderTests
    {
        /// <summary>One authored board and everything measured about it.</summary>
        sealed class Rung
        {
            public readonly string Id;
            public readonly GameMode Mode;
            public readonly string[] Rows;
            public readonly int Par, Ways, Careless, Nodes;

            /// <summary>
            /// Room above par this board is authored with, or nought for the shared default.
            ///
            /// <b>Pinned rather than defaulted</b>, because the careless reading below is
            /// measured <em>against the allowance</em>: a rung that quietly took five spare
            /// where its chapter body authors three would be measuring a board nobody ships.
            /// </summary>
            public readonly int Spare;

            /// <summary>
            /// The magazine, exactly as the chapter body deals it.
            ///
            /// <b>Pinned rather than defaulted</b>, for <see cref="Spare"/>'s reason and rather
            /// more sharply: the deal decides which moves exist at every depth, so a rung that
            /// quietly took the three-colour default where its body deals four would be proving
            /// a board nobody ships and would still answer a plausible par.
            /// </summary>
            public readonly string Cores;

            /// <summary>
            /// What the shortest answers actually do — see <see cref="MarchReading"/> and
            /// <see cref="EmberReading"/>.
            ///
            /// <b>Shared names rather than a set per mode</b>, because the question is the same
            /// one in both: how deep do the chains on the way to the answer go, and how much of
            /// what goes off did the player <em>make</em> (invariants 20m, 26h). What differs is
            /// the third column — Hollowmarch spends a Spark and Emberforge spends a star — so
            /// each mode's own test reads the one that belongs to it.
            /// </summary>
            public readonly int Chained, Forged, Lanced, Starred;

            /// <summary>
            /// Prismvale's own two, and they are the same question asked of a mode with no chain
            /// in it at all: <see cref="Dealt"/> counts the gems already lit as the board is
            /// dealt (invariant 5g - a board that starts half done passes every other gate), and
            /// <see cref="Used"/> counts the lantern colours a shortest answer really wakes a
            /// critter with. The second is this mode's <c>forged</c>: a board standing three
            /// lanterns whose answer only ever uses one is a board with two decorative lanterns
            /// on it, and colour decided nothing (invariant 5d).
            /// </summary>
            public readonly int Dealt, Used;

            public Rung(string id, GameMode mode, string[] rows,
                        int par, int ways, int careless, int nodes, int spare = 0,
                        string cores = null, int chained = 0, int forged = 0, int lanced = 0,
                        int starred = 0, int dealt = 0, int used = 0)
            {
                Dealt = dealt;
                Used = used;
                Cores = cores;
                Chained = chained;
                Forged = forged;
                Lanced = lanced;
                Starred = starred;
                Id = id;
                Mode = mode;
                Rows = rows;
                Par = par;
                Ways = ways;
                Careless = careless;
                Nodes = nodes;
                Spare = spare;
            }

            public int Width => Rows[0].Replace(" ", string.Empty).Length;
            public int Height => Rows.Length;
        }

        /// <summary>
        /// The shipped boards, exactly as <c>chapters/*.json</c> carries them.
        ///
        /// It held five while five prototypes were being judged, then Toppleglen's one and Nova
        /// Raid's three; every one of those modes was withdrawn after play and its rungs went
        /// with it, and every one of those ids is spent (see <c>GameMode</c>). What has never
        /// moved is the fixture, which is the argument for the shape.
        ///
        /// Copied rather than read, on purpose. A fixture that loaded the catalog would start
        /// passing or failing for reasons about the catalog; this one is about the rules.
        /// </summary>
        static readonly Rung[] Ladder =
        {
            // m01_firstcore. The teaching road: three colours, no raiders at all, and no
            // allowance either (invariant 24) - so the first thing anybody meets of this mode
            // cannot be lost. Careless play *does* finish it, deliberately: it is the rung where
            // the verb is learned. What makes it worth playing rather than merely surviving is
            // that a three-wave chain sits on the shortest answer, so the payoff is the first
            // thing seen rather than something discovered two levels later.
            new Rung("m01_firstcore", GameMode.March, new[]
            {
                "Z++++++",
                "......+",
                "BBg++++",
                "r......",
                "RGGRRbB",
                "......R",
                "......A",
            }, par: 4, ways: 14, careless: 4, nodes: 25,
               cores: "RGB", chained: 3, forged: 2, lanced: 1),

            // m01_haulroad. Two haulers standing in the line. They wear no colour, so no run can
            // span one and the line is cut into pieces that have to be worked separately - and a
            // blast beside one scraps it, which is the only way they come off. Greed loses here,
            // and the shortest answers set off a four-wave chain.
            new Rung("m01_haulroad", GameMode.March, new[]
            {
                "Z++++++++",
                "........+",
                "RrBrBBRH+",
                "G........",
                "HRbRR++++",
                "........+",
                "........A",
            }, par: 5, ways: 48, careless: 0, nodes: 186, spare: 5,
               cores: "RGB", chained: 4, forged: 3, lanced: 2),

            // m01_thegate. Ten squares a side, eight goals and a warden under plating, which two
            // blasts or one Spark will take. The line is authored ten steps from the gate against
            // an allowance of twelve, so a run that goes the distance really does watch pods go
            // through it - the march arriving rather than being a picture of a threat.
            new Rung("m01_thegate", GameMode.March, new[]
            {
                "Z+++++++++",
                ".........+",
                "BGHRGBrRGG",
                "B.........",
                "rBRWgHrYYg",
                ".........B",
                "++++++++++",
                "A.........",
            }, par: 7, ways: 56, careless: 0, nodes: 7117, spare: 5,
               cores: "RGBY", chained: 4, forged: 3, lanced: 2),

            // ------------------------------------------------------------------ Emberforge
            // The ten walls of e01_emberforge, exactly as the chapter body carries them. Nothing
            // is dealt into any of them - a wall is everything the level hands over, which is
            // what keeps the whole mode monotone and therefore searchable at all.
            //
            // `chained`, `forged` and `starred` are read over **every** shortest answer rather
            // than over the opening move, for the reason EmberReading gives: an opening-move
            // reading collapses exactly when a board is good.
            new Rung("e01_firstember", GameMode.Ember, new[]
            {
                "yrggyy",
                "ggygyr",
                "rr#r#y",
                "ggCybb",
                "brggbg",
                "ybbgyy",
            }, par: 3, ways: 35, careless: 0, nodes: 42,
               chained: 3, forged: 4, starred: 1),

            new Rung("e01_twinlocks", GameMode.Ember, new[]
            {
                "gbygyg",
                "rCbybg",
                "gr#y#y",
                "ryygbb",
                "ggyrCg",
                "byggbg",
            }, par: 3, ways: 14, careless: 8, nodes: 64,
               chained: 3, forged: 3, starred: 1),

            new Rung("e01_ironribs", GameMode.Ember, new[]
            {
                "byrrbrg",
                "bg#r#yg",
                "gCybbry",
                "gy#b#yg",
                "bryrbCr",
                "yg#y#bb",
                "ygyygby",
            }, par: 4, ways: 41, careless: 0, nodes: 150, spare: 3,
               chained: 3, forged: 5, starred: 1),

            new Rung("e01_frostvein", GameMode.Ember, new[]
            {
                "ybybryy",
                "yrg*gyy",
                "rCr*rCg",
                "ggr*bbr",
                "rg#r#gb",
                "grgrbgg",
                "ggrggby",
            }, par: 3, ways: 36, careless: 0, nodes: 114, spare: 3,
               chained: 3, forged: 3, starred: 1),

            new Rung("e01_chainfire", GameMode.Ember, new[]
            {
                "bygrrgy",
                "bCbgbCg",
                "gbr#rry",
                "gg#b#gg",
                "bgygbbg",
                "byggbCy",
                "rrbbygb",
            }, par: 4, ways: 15, careless: 0, nodes: 214, spare: 3,
               chained: 4, forged: 5, starred: 1),

            new Rung("e01_deepcell", GameMode.Ember, new[]
            {
                "bygrrgyb",
                "b#gbgg#b",
                "r#CrrC#r",
                "g#gbgg#b",
                "gygbbgby",
                "bg#gy#br",
                "rbygbgyb",
            }, par: 3, ways: 6, careless: 0, nodes: 65, spare: 3,
               chained: 3, forged: 4, starred: 1),

            new Rung("e01_ironward", GameMode.Ember, new[]
            {
                "bbgrbggy",
                "ryCgbCyy",
                "rrbgyryb",
                "brbWggby",
                "bgyybbyg",
                "yr#gb#yg",
                "yggygrbb",
            }, par: 4, ways: 15, careless: 0, nodes: 817, spare: 3,
               chained: 5, forged: 7, starred: 1),

            new Rung("e01_twinstars", GameMode.Ember, new[]
            {
                "bygrrgyb",
                "bCgbggCb",
                "rrb##rgg",
                "bggbgygb",
                "b#gbygrg",
                "ybrrbCyg",
                "bgybgbgy",
            }, par: 4, ways: 60, careless: 0, nodes: 1492, spare: 3,
               chained: 3, forged: 5, starred: 1),

            new Rung("e01_slaghold", GameMode.Ember, new[]
            {
                "gybbybyb",
                "r#ryr#yy",
                "bbCbryCg",
                "gbr*bgrr",
                "rg#br#gr",
                "bCybWgyg",
                "rbygyryg",
                "rybgbyrb",
            }, par: 5, ways: 46, careless: 0, nodes: 1024, spare: 4,
               chained: 4, forged: 5, starred: 1),

            new Rung("e01_emberheart", GameMode.Ember, new[]
            {
                "rybgyggy",
                "y#Cyry#g",
                "ggyr*rby",
                "bgCgyCby",
                "ybg##bgb",
                "yWygrCyr",
                "grgbybyb",
                "bbrygrry",
            }, par: 5, ways: 6, careless: 0, nodes: 927, spare: 4,
               chained: 3, forged: 5, starred: 1),

            // The teaching board: two lanterns, two critters, and one gem already lit beside
            // each lantern so the rule is shown rather than told - with no allowance at all
            // (invariant 24). Careless play *does* finish it, deliberately: it is the rung where
            // the verb is learned. Par 3 rather than 2 because at par 2 both star lines round
            // onto one number and two stars could never be scored.
            new Rung("p01_firstvein", GameMode.Prism, new[]
            {
                "Rrb.r.",
                "bg@bgg",
                "r.rg.r",
                "rrb.gr",
                ".rr@rb",
                "g.rbgG",
            }, par: 3, ways: 10, careless: 3, nodes: 208, spare: 0,
               dealt: 2, used: 2),

            // Three lanterns, three critters, every colour wanted somewhere - and the first
            // board a player who never looks ahead cannot finish. One of its four moves wakes
            // two critters at once, which is the only thing here better than the obvious move.
            new Rung("p01_twinlight", GameMode.Prism, new[]
            {
                "Rbrgg.",
                "br@rgG",
                "ggb.rr",
                "b.gg@r",
                "rb@grg",
                "Brbg.g",
            }, par: 4, ways: 120, careless: 0, nodes: 1722, spare: 3,
               dealt: 3, used: 3),
        };

        static ProtoLevelRules Read(Rung rung)
        {
            var dto = new LevelDto { id = rung.Id };
            var block = new ProtoDto
            {
                width = rung.Width,
                height = rung.Height,
                rows = rung.Rows,
                spare = rung.Spare,
                cores = rung.Cores,
            };

            // Named rather than assumed. A rung added for a second mode and quietly authored as
            // a cairn is a fixture that proves the wrong board, so the unknown case says so out
            // loud rather than defaulting to whichever block happened to be first.
            if (rung.Mode == GameMode.March) dto.march = block;
            else if (rung.Mode == GameMode.Ember) dto.ember = block;
            else if (rung.Mode == GameMode.Prism) dto.prism = block;
            else Assert.Fail($"{rung.Id}: '{rung.Mode}' has no block on LevelDto to author it in");

            var mode = LevelModes.Find(rung.Mode);
            Assert.NotNull(mode, $"{rung.Id}: '{rung.Mode}' is not a registered mode");

            var problems = new List<string>();
            Assert.IsTrue(mode.TryRead(dto, LevelId.Parse(rung.Id), problems, out var rules),
                          $"{rung.Id}: {string.Join("; ", problems)}");

            return (ProtoLevelRules)rules;
        }

        [Test]
        public void EveryShippedBoardStillHasTheParItWasAuthoredFor()
        {
            foreach (var rung in Ladder)
            {
                var answer = ProtoSearch.Solve(Read(rung).Opening());

                Assert.IsTrue(answer.Proved, $"{rung.Id}: could not be proved");
                Assert.AreEqual(rung.Par, answer.Par,
                    $"{rung.Id}: par moved. Both star lines and the allowance are multiples of "
                    + "it, so this re-grades the level for everybody who has already played it");
            }
        }

        /// <summary>
        /// The number invariant 5d asks for, and the one that catches a rule change in the
        /// direction nothing else does.
        ///
        /// A rule that makes a board <em>easier</em> leaves par plausible and every gate green —
        /// that is Budburst's wash bug from the other side — so what has to be pinned is how many
        /// shortest answers there are, which moves whenever the reachable state graph does.
        /// </summary>
        [Test]
        public void EveryShippedBoardStillHasTheSameNumberOfShortestAnswers()
        {
            foreach (var rung in Ladder)
            {
                var answer = ProtoSearch.Solve(Read(rung).Opening());

                Assert.AreEqual(rung.Ways, answer.Ways,
                    $"{rung.Id}: the count of shortest answers moved from {rung.Ways} to "
                    + $"{answer.Ways}, so a rule changed underneath this board");
            }
        }

        /// <summary>
        /// What a player who never looks ahead spends, held to the authored number.
        ///
        /// The reading is a warning in <c>ProtoValidator</c> rather than a gate, because early in
        /// a chapter thoughtlessness is supposed to work — and the opening floor is authored so
        /// that it does. What is pinned is the <em>number</em>, in both directions: a rule change
        /// that lets greedy play through the finale is as invisible as one that stops it getting
        /// through the first rung, and only this notices either.
        /// </summary>
        [Test]
        public void NoShippedBoardIsFinishedByCarelessPlay()
        {
            foreach (var rung in Ladder)
            {
                var rules = Read(rung);
                var tuning = new LevelTuning(() => ProtoSearch.Solve(rules.Opening()).Par,
                                             0f, 0f, 0f, rules.Spare);

                int careless = ProtoSearch.Careless(rules.Opening(), tuning.MoveBudget);

                Assert.AreEqual(rung.Careless, careless,
                    $"{rung.Id}: careless play now finishes in {careless} moves against "
                    + $"{rung.Careless} before");
            }
        }

        /// <summary>
        /// The proof has to stay affordable on the device that pays for it.
        ///
        /// Par is resolved lazily when somebody opens the level (invariant 26d), so this figure is
        /// the beat between tapping a node and the board arriving. Held to the authored number
        /// rather than to a ceiling, because a rule change that doubles the search is worth
        /// noticing long before it reaches the ceiling.
        /// </summary>
        [Test]
        public void EveryShippedBoardStillCostsWhatItCostToProve()
        {
            foreach (var rung in Ladder)
            {
                var answer = ProtoSearch.Solve(Read(rung).Opening());

                Assert.AreEqual(rung.Nodes, answer.Nodes,
                    $"{rung.Id}: proving it now costs {answer.Nodes} positions against "
                    + $"{rung.Nodes} before");
            }
        }

        /// <summary>
        /// Every board is authored at rest and none of them opens finished.
        ///
        /// Both are refusals in the mode's own reader, so this is really a test that the shipped
        /// boards still pass their own gate — which is what stops a rule change turning a settled
        /// cairn into one that collapses before the player touches it.
        /// </summary>
        [Test]
        public void EveryShippedBoardOpensPlayable()
        {
            foreach (var rung in Ladder)
            {
                var board = Read(rung).Fresh();

                Assert.IsFalse(board.IsFinished, $"{rung.Id}: opens finished");
                Assert.IsTrue(board.AnyMove, $"{rung.Id}: opens with no legal move");
                Assert.Greater(board.Goals, 0, $"{rung.Id}: opens asking for nothing");
            }
        }

        /// <summary>
        /// Every shipped board passes its own mode's validator, with no errors.
        ///
        /// <para>
        /// <b>This exists because the offline content check let a board through that the Editor's
        /// build gate refused.</b> <c>Tools/verify/content.py</c> is a <em>mirror</em> of the
        /// validators, not the validators — so a check that is wrong in C# and right in Python
        /// reads as green everywhere except twenty minutes into an APK build. That is exactly
        /// what happened: <c>RibbonValidator</c> looked for the companion by scanning the file
        /// for a literal <c>@</c>, and <c>r01_firstribbon</c> authored its wild as a lower-case
        /// letter instead. (Ribbonfall is retired and both names are gone; the reason this
        /// fixture exists is not.)
        /// </para>
        /// <para>
        /// It runs the real <c>ModeValidators</c> over the real shipped boards, which is the only
        /// arrangement that can see that class of mistake without opening Unity. Warnings are
        /// allowed through: they are readings for an author, and one of them is a fact about the
        /// board rather than a fault in it.
        /// </para>
        /// </summary>
        [Test]
        public void EveryShippedBoardPassesItsOwnModesValidator()
        {
            foreach (var rung in Ladder)
            {
                var rules = Read(rung);
                var validator = ModeValidators.Of(rung.Mode);

                Assert.NotNull(validator,
                    $"{rung.Id}: '{rung.Mode}' has no registered validator, so nothing looks at it");

                var level = new LevelDefinition(
                    LevelId.Parse(rung.Id), ChapterId.Parse("x01_probe"), rules,
                    new LevelTuning(() => rung.Par, 0f, 0f, 0f, rules.Spare),
                    new LevelPresentation(Vector2.zero, null, null, null));

                var issues = new List<LevelIssue>();
                validator.Validate(level, issues);

                var bad = new List<string>();
                foreach (var issue in issues)
                    if (issue.Severity == LevelIssueSeverity.Error) bad.Add(issue.Message);

                Assert.IsEmpty(bad, $"{rung.Id}: {string.Join(" | ", bad)}");
            }
        }

        /// <summary>
        /// What the <em>shortest answers</em> do, which is the reading that judges the mechanics
        /// rather than the board.
        ///
        /// <para>
        /// <b>The opening-move reading cannot do this job and pinning it would select for the
        /// wrong boards.</b> "Can the first shot set off a chain" collapses exactly when a road
        /// is good — a line whose very first core sets off a four-wave cascade is a line that is
        /// <em>over</em> in three shots. So what is held here is what a shortest run actually
        /// does: how deep its chains go, whether it forges a Spark, and whether it spends one.
        /// That is invariant 20m's <c>fired</c> and 26h's <c>kindled</c> asked of this mode, and
        /// it is the only thing in the suite that would notice a Spark quietly becoming
        /// unreachable or a chain quietly becoming free.
        /// </para>
        /// </summary>
        [Test]
        public void EveryShippedRoadStillChainsAndForgesOnItsShortestAnswers()
        {
            foreach (var rung in Ladder)
            {
                if (rung.Mode != GameMode.March) continue;

                var rules = (MarchRules)Read(rung);
                var reading = MarchReading.Of(rules.Layout);

                Assert.AreEqual(rung.Chained, reading.Chained,
                    $"{rung.Id}: the deepest chain on a shortest answer moved from "
                    + $"{rung.Chained} to {reading.Chained}");

                Assert.AreEqual(rung.Forged, reading.Forged,
                    $"{rung.Id}: Sparks forged on a shortest answer moved from {rung.Forged} "
                    + $"to {reading.Forged}");

                Assert.AreEqual(rung.Lanced, reading.Lanced,
                    $"{rung.Id}: Sparks spent on a shortest answer moved from {rung.Lanced} "
                    + $"to {reading.Lanced}");
            }
        }

        /// <summary>
        /// What an Emberforge wall's <em>shortest answers</em> do: how deep the chains go, how
        /// many embers the player has to make, and whether a star is ever worth spending.
        ///
        /// <para>
        /// The same question <c>EveryShippedRoadStillChainsAndForgesOnItsShortestAnswers</c>
        /// asks of Hollowmarch, and it is the only thing in the suite that would notice this
        /// mode's payoff quietly becoming free or quietly becoming unreachable. A rule change
        /// that let a beam through stone would leave par plausible and every gate green; it
        /// would move these.
        /// </para>
        /// </summary>
        [Test]
        public void EveryShippedWallStillChainsAndForgesOnItsShortestAnswers()
        {
            foreach (var rung in Ladder)
            {
                if (rung.Mode != GameMode.Ember) continue;

                var rules = (EmberRules)Read(rung);
                var reading = EmberReading.Of(rules.Layout);

                Assert.AreEqual(rung.Chained, reading.Chained,
                    $"{rung.Id}: the deepest chain on a shortest answer moved from "
                    + $"{rung.Chained} to {reading.Chained}");

                Assert.AreEqual(rung.Forged, reading.Forged,
                    $"{rung.Id}: embers made on a shortest answer moved from {rung.Forged} "
                    + $"to {reading.Forged}");

                Assert.AreEqual(rung.Starred, reading.Starred,
                    $"{rung.Id}: stars spent on a shortest answer moved from {rung.Starred} "
                    + $"to {reading.Starred}");
            }
        }

        /// <summary>
        /// A fuse cannot tell which way the finger went, on any wall that ships.
        ///
        /// <para>
        /// <b>This is the guard on an optimisation, and the optimisation is load-bearing.</b>
        /// <c>EmberBoard.Moves</c> offers a fuse once rather than twice because the two
        /// directions provably reach one wall — and if that ever stopped being true, the search
        /// would silently stop seeing half the boards reachable from every position, which moves
        /// par in the direction that hands out stars nobody earned. It is cheap to check and
        /// impossible to notice going wrong any other way.
        /// </para>
        /// <para>
        /// It was not true when this mode was first written: the swap's own cells were carried
        /// into every beat of the cascade, so a group four beats downstream could still tell the
        /// two apart. Thirty-nine of two and a half thousand pairs differed, and every one of
        /// them was double-counted in <c>ways</c>.
        /// </para>
        /// </summary>
        [Test]
        public void NoFuseOnAShippedWallCanTellItsTwoDirectionsApart()
        {
            var pairs = 0;

            foreach (var rung in Ladder)
            {
                if (rung.Mode != GameMode.Ember) continue;

                var layout = ((EmberRules)Read(rung)).Layout;
                var board = EmberBoard.Build(layout);

                for (int i = 0; i < layout.Pairs.Length; i += 2)
                {
                    int a = layout.Pairs[i], b = layout.Pairs[i + 1];
                    if (!board.Aim(a, b, out var aim) || aim != EmberAim.Fuse) continue;

                    pairs++;

                    var one = EmberBoard.Build(layout);
                    var other = EmberBoard.Build(layout);

                    Assert.NotNull(one.Fire(new EmberMove(a, b)));
                    Assert.NotNull(other.Fire(new EmberMove(b, a)));

                    var left = new List<byte>();
                    var right = new List<byte>();
                    one.Write(left);
                    other.Write(right);

                    CollectionAssert.AreEqual(left, right,
                        $"{rung.Id}: the fuse at {a}<->{b} reaches two different walls "
                        + "depending on which way the finger went, so Moves is offering half of "
                        + "them and the search cannot see the rest");
                }
            }

            Assert.Greater(pairs, 0, "no shipped wall had a fuse on it to check");
        }

        /// <summary>
        /// No wall is dealt more embers than invariant 20m allows, and every wall opens settled.
        ///
        /// Two facts a fixture can hold that a validator only warns about. An ember the player
        /// did not make is a payoff the author placed; a wall that fuses before anybody has
        /// touched it is not the wall that was authored, proved or graded.
        /// </summary>
        [Test]
        public void NoShippedWallIsDealtItsOwnPayoffOrOpensStirred()
        {
            foreach (var rung in Ladder)
            {
                if (rung.Mode != GameMode.Ember) continue;

                var rules = (EmberRules)Read(rung);
                var reading = EmberReading.Of(rules.Layout);

                Assert.Zero(reading.Dealt,
                    $"{rung.Id}: is dealt {reading.Dealt} ember(s), and every one of them is a "
                    + "payoff nobody earned");

                Assert.IsFalse(EmberBoard.Build(rules.Layout).Stirred,
                    $"{rung.Id}: opens with three alike already in a line, so it fuses before "
                    + "anybody has touched it");
            }
        }

        /// <summary>
        /// The star ladder is landable on every one of them.
        ///
        /// Invariant 22 arrived at from the allowance's side, which is how Groovekeeper found it:
        /// <c>par + spare</c> has to clear <c>ceil(par x 1.40)</c> or the bottom band is stranded
        /// and every clear is worth two stars or three.
        /// </summary>
        [Test]
        public void EveryShippedBoardCanScoreOneStar()
        {
            foreach (var rung in Ladder)
            {
                var rules = Read(rung);
                var tuning = new LevelTuning(() => rung.Par, 0f, 0f, 0f, rules.Spare);

                Assert.Less(tuning.GoldThreshold, tuning.SilverThreshold,
                            $"{rung.Id}: the two-star band is empty");
                Assert.Greater(tuning.MoveBudget, tuning.SilverThreshold,
                    $"{rung.Id}: the allowance is inside the two-star band, so one star could "
                    + "never be scored");
            }
        }
        /// <summary>
        /// What a Prismvale board's <em>shortest answers</em> do: how many of its lanterns are
        /// really wanted, and how little of it is already finished when it is dealt.
        ///
        /// <para>
        /// The same question the two tests above ask of Hollowmarch and Emberforge, and it is the
        /// only thing in the suite that would notice this mode's subject quietly going away.
        /// <c>Used</c> is the one that condemns a board: a field standing three lantern colours
        /// whose answer only ever uses one is a field with two decorative lanterns on it, and the
        /// colour rule — which is the whole mode — decided nothing (invariants 5d, 20m, 26h).
        /// </para>
        /// <para>
        /// <c>Dealt</c> is invariant 5g counted, and it is here rather than only in the validator
        /// because it is the reading that goes wrong <em>silently</em>: a board dealt with most
        /// of its veins already running is still solvable, still correctly par'd and still fully
        /// validated, and the player is simply handed a level somebody else half finished.
        /// </para>
        /// </summary>
        [Test]
        public void EveryShippedFieldStillWantsEveryLanternOnIt()
        {
            foreach (var rung in Ladder)
            {
                if (rung.Mode != GameMode.Prism) continue;

                var rules = (PrismRules)Read(rung);
                var budget = rung.Par + rules.Spare;
                var reading = PrismReading.Of(rules.Layout, budget);

                Assert.AreEqual(rung.Used, reading.Used,
                    $"{rung.Id}: lantern colours a shortest answer wakes with moved from "
                    + $"{rung.Used} to {reading.Used}");

                Assert.AreEqual(rung.Dealt, reading.Dealt,
                    $"{rung.Id}: gems already lit as the board is dealt moved from "
                    + $"{rung.Dealt} to {reading.Dealt}");
            }
        }

        /// <summary>
        /// No shipped Prismvale board may be dealt with a vein already standing against a
        /// sleeping critter.
        ///
        /// <para>
        /// Budburst's "authored settled" rule, and it bites harder here than the percentage the
        /// validator warns on: a critter touched at the deal has <em>already woken</em> before a
        /// finger arrives, so the goal count the player is graded against has moved and the board
        /// proved is not the board that opens. The mode's own reader refuses it, and this is what
        /// notices if that refusal is ever loosened.
        /// </para>
        /// <para>
        /// <b>And there is deliberately no <c>life</c> test here</b>, where Emberforge and the
        /// retired Kindlewake both have one. Nothing on this board is ever consumed — a gem is
        /// moved and never spent — so a run always has a legal move and the allowance is the only
        /// way to lose. A check asking "does the board outlast the meter" could only ever answer
        /// yes, and a check that cannot fail is not a check.
        /// </para>
        /// </summary>
        [Test]
        public void NoShippedFieldIsDealtWithLightAlreadyOnACritter()
        {
            foreach (var rung in Ladder)
            {
                if (rung.Mode != GameMode.Prism) continue;

                var rules = (PrismRules)Read(rung);

                // Bound to a local rather than read through the rules twice, which is the idiom
                // `compile.py`'s `.Layout.` guard is asking for - a glade's own board can be
                // absent, so nothing may walk into one without saying it knows that.
                var layout = rules.Layout;
                var board = PrismBoard.Build(layout);

                Assert.IsFalse(board.Stirred,
                    $"{rung.Id}: a vein is already touching a sleeping critter as this board is "
                    + "dealt, so it would wake before anybody had moved a gem");

                Assert.AreEqual(0, layout.Marooned.Length,
                    $"{rung.Id}: a critter on this board stands where no lantern could ever "
                    + "reach it, whatever the gems are arranged into");
            }
        }

    }
}
