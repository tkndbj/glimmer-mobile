using System.Collections.Generic;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The four rules a chain's score is held to, driven by every grove the chapter ships.
    ///
    /// <para>
    /// <b>These exist because the mode shipped without them and the faults they catch were
    /// reported from the sofa rather than found here.</b> Motion is the one subsystem whose
    /// failures only ever show up in play, so the arithmetic that decides it has to be reachable
    /// without an Editor — and until <see cref="BudStage"/> there was no arithmetic to reach:
    /// the ordering was an emergent property of two hundred independent tweens, each working out
    /// its own delay from its own share of a per-wave beat. Nothing could be asserted about it
    /// because nothing anywhere held the two numbers that had to be compared.
    /// </para>
    /// <para>
    /// <b>Every one of these was watched failing against the old rule before it was kept</b>,
    /// which is this repository's standing requirement of a check: measured on the ten shipped
    /// groves' best opening taps, seven of them dropped a flower into a hole before the burst
    /// that made the hole had been drawn, six times over on the finale.
    /// </para>
    /// <para>
    /// It drives the boards inline through <see cref="BudLadderTests.Ladder"/> rather than
    /// through the content files, for that fixture's reason: a guard that needs the Editor is a
    /// guard nobody runs on the way past.
    /// </para>
    /// </summary>
    public sealed class BudStageTests
    {
        /// <summary>One grove's best opening tap, played by the model and written out as a score.</summary>
        sealed class Tap
        {
            public readonly string Id;
            public readonly BudScore Score;
            public readonly BudPulse[] Pulses;
            public readonly BudDrop[] Drops;
            public readonly BudLayout Grove;
            public readonly int Waves, Width;

            public Tap(string id, BudScore score, BudPulse[] pulses, BudDrop[] drops,
                       BudLayout grove, int waves, int width)
            {
                Id = id;
                Score = score;
                Pulses = pulses;
                Drops = drops;
                Grove = grove;
                Waves = waves;
                Width = width;
            }
        }

        /// <summary>
        /// Every shipped grove, tapped where it goes off hardest.
        ///
        /// The deepest opening tap rather than an arbitrary one, because that is the board this
        /// mode is judged on: the Thicket's finale runs eight waves, bursts twenty-seven flowers
        /// and frees ten critters, and it is where every ordering fault is worst.
        ///
        /// **Both chapters**, because the Tanglewood puts cue kinds on the timeline that the
        /// Thicket never emits — a row sliding before the chain, a puffball's spores leaving it
        /// and landing, a hive's swarm — and every rule this fixture proves is about the order
        /// things happen in.
        ///
        /// **Every move, not only the taps**, because a Tanglewood grove's best opening is very
        /// often the object it teaches: the windmill's gust on the first, a graft on the third.
        /// Through <c>BudSolver.Opening</c>, so the move this fixture animates is the one
        /// <c>BudLadderTests</c> pins.
        /// </summary>
        static IEnumerable<Tap> Taps()
        {
            foreach (var chapter in BudLadderTests.Chapters)
            foreach (var rung in chapter.Rungs)
            {
                var layout = rung.Layout();

                var move = BudSolver.Opening(layout, out var best);
                Assert.IsTrue(move.Any, rung.Id + ": no legal opening move");

                var p = new List<BudPulse>();
                var w = new List<BudWash>();
                var d = new List<BudDrop>();

                var board = new BudBoard(layout);
                BudRun.Apply(board, move, layout.Deal.At(0), p, out var chain, w, d);
                Assert.AreEqual(best.Waves, chain.Waves, rung.Id + ": the opening replayed differently");

                var pulses = p.ToArray();
                var washes = w.ToArray();
                var drops = d.ToArray();

                var score = BudStage.Of(best.Waves, pulses, washes, drops, layout.Width);

                yield return new Tap(rung.Id, score, pulses, drops, layout, best.Waves,
                                     layout.Width);
            }
        }

        // ------------------------------------------------------------------ rule 1
        /// <summary>
        /// <b>Nothing is drawn before its cause.</b>
        ///
        /// <para>
        /// A piece may not begin to fall until every burst it falls <em>into or past</em> has
        /// gone off. The cells between where a piece stood and where it comes to rest are
        /// precisely the ones that emptied, so that set is the exact causal set rather than an
        /// approximation of one.
        /// </para>
        /// <para>
        /// <b>This is the reported bug.</b> The old rule started a wave's rain at a fixed offset
        /// into the wave's burn while the bursts of that same wave rippled out to 62% of it — and
        /// worse, it gave the <em>tallest</em> drops the shortest wait, so the most conspicuous
        /// movement on the board was the one most decoupled from its cause. What that looks like
        /// is flowers sliding in at random spots with nothing having caused them, and that is
        /// how it was reported.
        /// </para>
        /// </summary>
        [Test]
        public void NoPieceEverFallsIntoAHoleThatHasNotBeenMadeYet()
        {
            foreach (var tap in Taps())
            {
                // Every hole, which is a flower going off *or* a cocoon opening. Only the
                // bursts were read here for as long as this rule existed, and the mode shipped
                // dealing a flower into a cocoon's square 45ms before its shell broke.
                var burst = new Dictionary<int, float>();
                foreach (var cue in tap.Score.Cues)
                    if (cue.Kind == BudCueKind.Burst || cue.Kind == BudCueKind.Free)
                        burst[Key(cue.Wave, cue.Cell)] = cue.At;

                foreach (var cue in tap.Score.Cues)
                {
                    if (cue.Kind != BudCueKind.Fall) continue;

                    int column = cue.Cell % tap.Width;
                    int to = cue.Cell / tap.Width;
                    int came = cue.From < 0 ? -1 : cue.From / tap.Width;

                    for (int row = came + 1; row <= to; row++)
                    {
                        int cell = row * tap.Width + column;
                        if (!burst.TryGetValue(Key(cue.Wave, cell), out float at)) continue;

                        Assert.GreaterOrEqual(cue.At, at - .0001f,
                            $"{tap.Id}: a piece lands in cell {cue.Cell} starting at {cue.At:0.000}s, "
                            + $"but the square at {cell} it falls past is not emptied until "
                            + $"{at:0.000}s — it is sliding into a hole nobody has made");
                    }
                }
            }
        }

        /// <summary>
        /// And a cocoon never answers a bunch that has not gone off, which is the same rule
        /// where the player is most likely to be looking.
        /// </summary>
        [Test]
        public void NorDoesACocoonAnswerABunchThatHasNotBurst()
        {
            foreach (var tap in Taps())
            {
                float[] first = new float[tap.Waves + 1];
                for (int w = 0; w <= tap.Waves; w++) first[w] = float.PositiveInfinity;

                foreach (var cue in tap.Score.Cues)
                    if (cue.Kind == BudCueKind.Burst && cue.At < first[cue.Wave])
                        first[cue.Wave] = cue.At;

                foreach (var cue in tap.Score.Cues)
                {
                    if (cue.Kind != BudCueKind.Crack && cue.Kind != BudCueKind.Free) continue;

                    Assert.Greater(cue.At, first[cue.Wave] - .0001f,
                        $"{tap.Id}: a cocoon at {cue.Cell} answers wave {cue.Wave} at "
                        + $"{cue.At:0.000}s, before anything in that wave has gone off");
                }
            }
        }

        // ------------------------------------------------------------------ rule 2
        /// <summary>
        /// <b>One gravity.</b> Every falling piece in every wave of every grove moves at the
        /// same speed, so a fall's length is decided by its distance and by nothing else.
        ///
        /// <para>
        /// The rule this replaced clamped a piece into a leftover budget, so inside one wave a
        /// six-row drop fell half again faster than the one-row drop beside it — a board that
        /// falls at three speeds at once does not read as a board falling. Held here across the
        /// whole chapter rather than over one board, because the old fault only appeared when a
        /// wave happened to run short.
        /// </para>
        /// </summary>
        [Test]
        public void EveryPieceInTheChapterFallsAtExactlyTheSamePace()
        {
            foreach (var tap in Taps())
            {
                foreach (var cue in tap.Score.Cues)
                {
                    if (cue.Kind != BudCueKind.Fall) continue;

                    Assert.Greater(cue.Rows, 0, tap.Id + ": a fall that travels no distance");
                    Assert.AreEqual(BudTempo.Falling(cue.Rows), cue.Over, .0001f,
                        $"{tap.Id}: a {cue.Rows}-row drop into {cue.Cell} takes {cue.Over:0.000}s "
                        + $"where the mode's own pace makes it {BudTempo.Falling(cue.Rows):0.000}s");
                }
            }
        }

        // ------------------------------------------------------------------ rule 3
        /// <summary>
        /// <b>A column collapses from the bottom.</b> Within one column of one wave the lowest
        /// destination leaves first, and no two pieces of that column ever start together.
        ///
        /// <para>
        /// A correctness rule as much as a look: two pieces of one column moving at once are two
        /// pictures passing through each other, and a column whose top leaves before its bottom
        /// is not falling into anything.
        /// </para>
        /// </summary>
        [Test]
        public void AColumnLeavesFromTheBottomAndNeverAllAtOnce()
        {
            foreach (var tap in Taps())
            {
                // (wave, column) -> the deepest destination seen so far and when it left
                var seen = new Dictionary<int, KeyValuePair<int, float>>();

                foreach (var cue in tap.Score.Cues)
                {
                    if (cue.Kind != BudCueKind.Fall) continue;

                    int column = cue.Cell % tap.Width;
                    int row = cue.Cell / tap.Width;
                    int key = Key(cue.Wave, column);

                    if (seen.TryGetValue(key, out var last))
                    {
                        // Deepest first, so the row just dealt must be *below* this one.
                        Assert.Greater(last.Key, row,
                            $"{tap.Id}: column {column} of wave {cue.Wave} is dealt top-down, so "
                            + "its pieces fall out of a hole rather than into one");
                        Assert.Greater(cue.At, last.Value + .0001f,
                            $"{tap.Id}: two pieces of column {column} both leave at "
                            + $"{cue.At:0.000}s, so they pass through each other");
                    }

                    seen[key] = new KeyValuePair<int, float>(row, cue.At);
                }
            }
        }

        /// <summary>
        /// <b>A cell is emptied before anything is painted into it, so no square on the board
        /// ever draws two pieces at once.</b>
        ///
        /// <para>
        /// This is the Domain half of the fault reported as <em>"the flowers at top stay still,
        /// and new flowers fall through them"</em>. A fall is drawn by painting the arriving
        /// piece into the cell it is falling <em>into</em> and offsetting it up to where it came
        /// from — so the cell it came from has to stop drawing it in the same breath
        /// (<c>BudView.EmptyCell</c>), or the board shows the flower standing still and a copy of
        /// itself coming down through it.
        /// </para>
        /// <para>
        /// <b>What lives here is the ordering that makes that safe.</b> Within a column a cell is
        /// routinely both — the piece above it falls into the hole it leaves — so emptying it and
        /// filling it are two cues about one square, and only <see cref="BudStage"/> decides
        /// which is handed over first. It comes out right because <c>Rain</c> deals a column
        /// deepest-destination first, which is rule 3 read for a second reason: reverse that and
        /// a piece is painted into a square something is still standing in, and the fix in the
        /// view would then blank a flower that had legitimately just arrived.
        /// </para>
        /// <para>
        /// So it walks the score exactly as the view does, keeping one bit per cell, and holds
        /// two things over the whole chapter: nothing is ever painted into an occupied square,
        /// and nothing ever leaves an empty one.
        /// </para>
        /// </summary>
        [Test]
        public void ACellIsEmptiedBeforeAnythingIsPaintedIntoIt()
        {
            foreach (var tap in Taps())
            {
                var drawn = new bool[tap.Grove.Count];
                for (int i = 0; i < drawn.Length; i++)
                    drawn[i] = tap.Grove.IsFlower(i) || tap.Grove.IsCocoon(i);

                foreach (var cue in tap.Score.Cues)
                {
                    switch (cue.Kind)
                    {
                        // A flower going off and a cocoon opening both leave bare ground; a
                        // special firing clears its own cell too.
                        case BudCueKind.Burst:
                        case BudCueKind.Free:
                        case BudCueKind.Fire:
                            drawn[cue.Cell] = false;
                            break;

                        // A forge stands a special up where its bunch went off, which was
                        // drawn as bare a beat earlier.
                        case BudCueKind.Forge:
                            drawn[cue.Cell] = true;
                            break;

                        // A slide moves a piece sideways into a square the other piece of the
                        // same slide is leaving, so the pair stays full: what has to be true is
                        // only that something was standing where it came from.
                        case BudCueKind.Slide:
                            Assert.IsTrue(drawn[cue.From],
                                $"{tap.Id}: a piece slides out of cell {cue.From}, which nothing "
                                + "was standing in");
                            drawn[cue.Cell] = true;
                            break;

                        case BudCueKind.Fall:
                            if (cue.From >= 0)
                            {
                                Assert.IsTrue(drawn[cue.From],
                                    $"{tap.Id}: a piece leaves cell {cue.From} at {cue.At:0.000}s, "
                                    + "which nothing was standing in");

                                drawn[cue.From] = false;
                            }

                            Assert.IsFalse(drawn[cue.Cell],
                                $"{tap.Id}: a piece is painted into cell {cue.Cell} at "
                                + $"{cue.At:0.000}s while something is still drawn there — one "
                                + "square would draw two pieces, and emptying it afterwards would "
                                + "take down the one that had just arrived");

                            drawn[cue.Cell] = true;
                            break;
                    }
                }

                // And the grove the chain ends on is the grove the model holds, square for square.
                for (int i = 0; i < drawn.Length; i++)
                    Assert.IsTrue(drawn[i] || tap.Grove.IsStone(i),
                        $"{tap.Id}: cell {i} is left empty by the end of the chain, so the grove "
                        + "finishes with a hole in it that the regrowth never filled");
            }
        }

        // ------------------------------------------------------------------ rule 4
        /// <summary>
        /// <b>The ceiling is met by squeezing the slack, never the falls.</b>
        ///
        /// <para>
        /// "The rate gives way" is this mode's own doctrine and the code it replaced honoured it
        /// in the one place it must not — the individual piece. A chain that will not fit gives
        /// up its pauses, and one that still will not fit is allowed to run long, because the
        /// alternative is moving the pieces faster than the eye.
        /// </para>
        /// </summary>
        [Test]
        public void EveryShippedGroveFitsTheCeilingWithoutTouchingItsGravity()
        {
            foreach (var tap in Taps())
            {
                Assert.LessOrEqual(tap.Score.Body, BudTempo.Ceiling + .0001f,
                    $"{tap.Id}: its best tap runs {tap.Score.Body:0.00}s of chain");

                Assert.GreaterOrEqual(tap.Score.Squeeze, BudTempo.SlackFloor - .0001f,
                    $"{tap.Id}: squeezed to {tap.Score.Squeeze:0.00} of its slack, which is past "
                    + "the point where a pause is still a pause");
            }
        }

        /// <summary>And a squeeze never reaches gravity, however deep the chain.</summary>
        [Test]
        public void AndASqueezeNeverReachesGravity()
        {
            foreach (var tap in Taps())
                foreach (var cue in tap.Score.Cues)
                    if (cue.Kind == BudCueKind.Fall)
                        Assert.AreEqual(BudTempo.Falling(cue.Rows), cue.Over, .0001f,
                            tap.Id + ": a squeezed chain moved a piece faster");
        }

        // ------------------------------------------------------------------ the word
        /// <summary>
        /// <b>The word rides the climax rather than following it.</b>
        ///
        /// <para>
        /// It used to be raised after the last collapse had landed <em>and</em> after the whole
        /// board had been repainted, so the biggest thing this mode says arrived into dead air
        /// over a board that had visibly just reset — reported as the text turning up once the
        /// animation was over. It is scheduled on the last wave's answer, and the final collapse,
        /// the regrowth and the tidy-up all happen underneath it.
        /// </para>
        /// </summary>
        [Test]
        public void TheWordArrivesOnTheClimaxRatherThanAfterIt()
        {
            int said = 0;

            foreach (var tap in Taps())
            {
                float word = -1f, tidy = -1f, answer = -1f;

                foreach (var cue in tap.Score.Cues)
                {
                    if (cue.Kind == BudCueKind.Word) word = cue.At;
                    else if (cue.Kind == BudCueKind.Tidy) tidy = cue.At;
                    else if (cue.Kind == BudCueKind.Answer && cue.Wave == tap.Waves - 1)
                        answer = cue.At;
                }

                if (word < 0f) continue;
                said++;

                Assert.AreEqual(answer, word, .0001f,
                    $"{tap.Id}: the word lands at {word:0.00}s and the last wave answers at "
                    + $"{answer:0.00}s");

                Assert.Less(word, tidy - .0001f,
                    $"{tap.Id}: the board is put back in step at {tidy:0.00}s and the word does "
                    + $"not arrive until {word:0.00}s, so it lands on a board that has reset");
            }

            Assert.Greater(said, 0, "no shipped grove reaches a chain worth a word");
        }

        /// <summary>
        /// And nothing is put back in step while the grove is still moving, which is the other
        /// half of what "the board resets suddenly" was.
        /// </summary>
        [Test]
        public void AndTheBoardIsOnlyPutBackInStepOnceNothingIsMoving()
        {
            foreach (var tap in Taps())
            {
                float tidy = -1f, moving = 0f;

                foreach (var cue in tap.Score.Cues)
                {
                    if (cue.Kind == BudCueKind.Tidy) tidy = cue.At;
                    else if (cue.Kind != BudCueKind.Word && cue.Kind != BudCueKind.Done
                             && cue.Until > moving) moving = cue.Until;
                }

                Assert.GreaterOrEqual(tidy, moving - .0001f,
                    $"{tap.Id}: the board is repainted at {tidy:0.00}s with something still "
                    + $"moving until {moving:0.00}s");
            }
        }

        // ------------------------------------------------------------------ the score itself
        /// <summary>
        /// The cues come out in the order they happen, because the view plays them by walking
        /// the list and waiting out the difference — a cue out of order is a cue drawn late.
        /// </summary>
        [Test]
        public void AScoreIsHandedOverInOrder()
        {
            foreach (var tap in Taps())
            {
                float last = 0f;

                foreach (var cue in tap.Score.Cues)
                {
                    Assert.GreaterOrEqual(cue.At, last - .0001f, tap.Id + ": a cue is out of order");
                    Assert.GreaterOrEqual(cue.At, 0f, tap.Id + ": a cue before the tap");
                    last = cue.At;
                }

                Assert.AreEqual(BudCueKind.Done, tap.Score.Cues[tap.Score.Cues.Length - 1].Kind,
                                tap.Id + ": the score does not end by saying so");
            }
        }

        /// <summary>
        /// A flower goes off exactly when its own wind-up ends, so nothing on the board is ever
        /// motionless while its neighbours are bursting.
        /// </summary>
        [Test]
        public void EveryFlowerWindsUpRightUpToTheMomentItGoesOff()
        {
            foreach (var tap in Taps())
            {
                var wind = new Dictionary<int, float>();

                foreach (var cue in tap.Score.Cues)
                    if (cue.Kind == BudCueKind.Wind) wind[Key(cue.Wave, cue.Cell)] = cue.Until;

                foreach (var cue in tap.Score.Cues)
                {
                    if (cue.Kind != BudCueKind.Burst) continue;

                    // A cell a special clears is struck, not gathered: it has no wind-up.
                    if (cue.From >= 0) continue;

                    Assert.IsTrue(wind.TryGetValue(Key(cue.Wave, cue.Cell), out float until),
                                  tap.Id + ": a flower goes off having never wound up");
                    Assert.AreEqual(cue.At, until, .0001f,
                                    tap.Id + ": a flower stops winding up before it goes off");
                }
            }
        }

        /// <summary>
        /// And two critters never get out at once, which is the one spread here doing more than
        /// pacing: each carries a sound, a halo, a shockwave and a creature, and the chapter's
        /// finale frees ten on one wave.
        /// </summary>
        [Test]
        public void TwoCrittersNeverGetOutAtOnce()
        {
            int greeted = 0;

            foreach (var tap in Taps())
            {
                float last = float.NegativeInfinity;

                foreach (var cue in tap.Score.Cues)
                {
                    if (cue.Kind != BudCueKind.Free) continue;

                    greeted++;
                    Assert.GreaterOrEqual(cue.At, last + BudTempo.GreetLag - .0001f,
                        $"{tap.Id}: two critters get out {cue.At - last:0.000}s apart");
                    last = cue.At;
                }
            }

            Assert.Greater(greeted, 0, "no shipped grove frees anybody on its best tap");
        }

        // ------------------------------------------------------------------ the ripen
        /// <summary>
        /// <b>The grove ripening one for the player waits for a still board, and is never dealt
        /// as an ordinary wash.</b>
        ///
        /// <para>
        /// <c>BudBoard.Creep</c> moves one flower beside a still-shut cocoon a step on after every
        /// tap. It is the one event on this board with no cause anywhere near it, and it shipped
        /// drawn exactly like a wash — a flower turning because the bunch beside it went off. So a
        /// flower changed colour across the board with nothing to explain it, and it was reported
        /// as a suspected bug. It is held to the end of the chain, where it is the only thing
        /// moving, and it arrives before the tidy-up that would otherwise apply its colour
        /// silently.
        /// </para>
        /// </summary>
        [Test]
        public void TheGroveRipensOneForThePlayerOnceTheBoardHasStopped()
        {
            int seen = 0;

            foreach (var tap in Taps())
            {
                float ripen = -1f, tidy = -1f, moving = 0f;
                int count = 0;

                foreach (var cue in tap.Score.Cues)
                {
                    if (cue.Kind == BudCueKind.Ripen) { ripen = cue.At; count++; }
                    else if (cue.Kind == BudCueKind.Tidy) tidy = cue.At;
                    else if (cue.Kind != BudCueKind.Word && cue.Kind != BudCueKind.Done
                             && cue.Until > moving) moving = cue.Until;
                }

                Assert.LessOrEqual(count, 1, tap.Id + ": the grove ripened more than one flower");
                if (count == 0) continue;

                seen++;

                Assert.GreaterOrEqual(ripen, moving - .0001f,
                    $"{tap.Id}: the grove ripens a flower at {ripen:0.00}s with the board still "
                    + $"moving until {moving:0.00}s, so it is one change among twenty");

                Assert.LessOrEqual(ripen, tidy + .0001f,
                    $"{tap.Id}: the ripen lands at {ripen:0.00}s and the repaint at {tidy:0.00}s, "
                    + "so the colour is applied before anything says who did it");
            }

            Assert.Greater(seen, 0, "no shipped grove ripens anything on its best tap");
        }

        /// <summary>
        /// And a ripen is never counted among its wave's washes, or the ripple that spaces them
        /// is built for one more than there are.
        /// </summary>
        [Test]
        public void ARipenIsNotOneOfTheWashes()
        {
            foreach (var tap in Taps())
                foreach (var cue in tap.Score.Cues)
                    if (cue.Kind == BudCueKind.Wash)
                        Assert.Less(cue.Nth, cue.Of,
                                    tap.Id + ": a wash is dealt past the end of its own ripple");
        }

        /// <summary>
        /// A special fires at the head of its wave and the cells it clears go off after it,
        /// racing outward a fixed step apart — and a forge lands a beat after the bunch that
        /// made it has gone.
        ///
        /// <para>
        /// <b>Three things, and the first is that it happens at all.</b> Nothing else in this
        /// fixture would notice a <c>Fire</c> cue disappearing from the timeline: the chain
        /// would still be correct, the grove would still end where the model says, and the only
        /// symptom would be a whole row going off with nothing having caused it.
        /// </para>
        /// </summary>
        [Test]
        public void ASpecialFiresBeforeWhatItClearsAndAForgeLandsAfterItsBunch()
        {
            var grove = BudLadderTests.Grove(new[]
            {
                "RGBYRGB",
                "GoRBoBR",
                "BRGYBRG",
                "oGBRGBo",
                "RBYGRYB",
            }, "R", "RGBYMC", specials: new[] { "...|...", ".......", ".......", ".......", "......." });

            var board = new BudBoard(grove);
            var pulses = new List<BudPulse>();
            var washes = new List<BudWash>();
            var drops = new List<BudDrop>();

            var chain = board.Tap(grove.Index(3, 0), Energy.R, pulses, washes, drops);
            Assert.AreEqual(1, chain.Fired, "the tapped bolt did not fire");

            var score = BudStage.Of(chain.Waves, pulses.ToArray(), washes.ToArray(),
                                    drops.ToArray(), grove.Width);

            float fired = float.NaN;
            foreach (var cue in score.Cues)
                if (cue.Kind == BudCueKind.Fire) fired = cue.At;
            Assert.IsFalse(float.IsNaN(fired), "a fired special put no Fire cue on the timeline");

            int struck = 0;
            float last = float.NegativeInfinity;
            foreach (var cue in score.Cues)
            {
                if (cue.Kind != BudCueKind.Burst || cue.From < 0) continue;
                struck++;
                Assert.GreaterOrEqual(cue.At, fired - .0005f,
                                      "a cell the bolt cleared went off before the bolt fired");
                Assert.GreaterOrEqual(cue.At, last - .0005f,
                                      "the bolt's line is not laid out nearest first");
                last = cue.At;
            }

            Assert.AreEqual(grove.Width + grove.Height - 1, struck,
                            "the bolt did not clear its whole row and column");

            // And a forge: five alike on a grove that forges, then the special arrives after
            // the last burst of its bunch.
            var forge = BudLadderTests.Grove(new[]
            {
                "YYRYY",
                "GBRBG",
                "BoGoB",
                "RGBGR",
            }, "G", "RGBYMC", forges: true);

            var fb = new BudBoard(forge);
            pulses.Clear(); washes.Clear(); drops.Clear();
            var fc = fb.Tap(forge.Index(2, 0), Energy.G, pulses, washes, drops);
            Assert.AreEqual(1, fc.Forged, "five alike did not forge");

            var fs = BudStage.Of(fc.Waves, pulses.ToArray(), washes.ToArray(), drops.ToArray(),
                                 forge.Width);

            float lastBurst = float.NegativeInfinity, forged = float.NaN;
            foreach (var cue in fs.Cues)
            {
                if (cue.Kind == BudCueKind.Burst && cue.Wave == 0 && cue.At > lastBurst) lastBurst = cue.At;
                if (cue.Kind == BudCueKind.Forge) forged = cue.At;
            }

            Assert.IsFalse(float.IsNaN(forged), "a forge put no cue on the timeline");
            Assert.GreaterOrEqual(forged, lastBurst + BudTempo.ForgeLag * BudTempo.SlackFloor - .0005f,
                                  "the special arrived before the bunch that made it had gone");
        }

        /// <summary>
        /// A graft's slide comes first, alone, and nothing of the chain is drawn until it has
        /// landed.
        ///
        /// It is the player's own move, so it is drawn the way a tap's spin is: before the first
        /// wind-up. A wind-up that started under a flower still sliding would be a bunch winding
        /// up out of flowers that are not there yet.
        /// </summary>
        [Test]
        public void ASlideComesFirstAndTheChainWaitsForIt()
        {
            var grove = BudLadderTests.Grove(new[]
            {
                "RGYRG",
                "GYBYR",
                "BRGoB",
                "RoBGB",
            }, "G", "RGBYMC", grafts: true);

            var board = new BudBoard(grove);
            Assert.IsTrue(board.CanGraft(grove.Index(1, 1), grove.Index(2, 1)),
                          "the yellow and the blue do not trade into a bunch");

            var pulses = new List<BudPulse>();
            var washes = new List<BudWash>();
            var drops = new List<BudDrop>();

            var chain = board.Graft(grove.Index(1, 1), grove.Index(2, 1), pulses, washes, drops);
            var score = BudStage.Of(chain.Waves, pulses.ToArray(), washes.ToArray(),
                                    drops.ToArray(), grove.Width);

            float slid = float.NegativeInfinity;
            int slides = 0;

            foreach (var cue in score.Cues)
            {
                if (cue.Kind != BudCueKind.Slide) continue;
                slides++;
                Assert.AreEqual(0f, cue.At, .0005f, "a slide that did not start with the tap");
                Assert.AreEqual(BudTempo.Slide, cue.Over, .0005f, "a slide at a speed of its own");
                if (cue.Until > slid) slid = cue.Until;
            }

            Assert.AreEqual(2, slides, "a graft slides exactly two pieces");

            foreach (var cue in score.Cues)
            {
                if (cue.Kind == BudCueKind.Slide) continue;
                Assert.GreaterOrEqual(cue.At, slid - .0005f,
                                      cue.Kind + " was drawn before the pieces had finished sliding");
            }
        }

        /// <summary>An empty tap is a score with nothing in it but a full stop.</summary>
        [Test]
        public void ATapThatSetsNothingOffIsStillAScore()
        {
            var score = BudStage.Of(0, System.Array.Empty<BudPulse>(),
                                    System.Array.Empty<BudWash>(),
                                    System.Array.Empty<BudDrop>(), 5);

            Assert.AreEqual(0f, score.Body, .0001f);
            Assert.AreEqual(BudCueKind.Done, score.Cues[score.Cues.Length - 1].Kind);

            foreach (var cue in score.Cues)
                Assert.IsTrue(cue.Kind == BudCueKind.Tidy || cue.Kind == BudCueKind.Done,
                              "an empty tap draws something");
        }

        static int Key(int wave, int cell) => wave * 100000 + cell;
    }
}
