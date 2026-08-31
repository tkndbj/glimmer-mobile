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
            public readonly int Waves, Width;

            public Tap(string id, BudScore score, BudPulse[] pulses, BudDrop[] drops,
                       int waves, int width)
            {
                Id = id;
                Score = score;
                Pulses = pulses;
                Drops = drops;
                Waves = waves;
                Width = width;
            }
        }

        /// <summary>
        /// Every shipped grove, tapped where it goes off hardest.
        ///
        /// The deepest opening tap rather than an arbitrary one, because that is the board this
        /// mode is judged on: the finale's runs eight waves, bursts twenty-seven flowers and
        /// frees ten critters, and it is where every ordering fault is worst.
        /// </summary>
        static IEnumerable<Tap> Taps()
        {
            foreach (var rung in BudLadderTests.Ladder)
            {
                var layout = rung.Layout();

                BudChainResult best = default;
                BudPulse[] pulses = null;
                BudDrop[] drops = null;

                // The first colour the basket deals, which is what an opening tap is made with.
                int colour = layout.Deal.At(0);

                for (int i = 0; i < layout.Count; i++)
                {
                    var board = new BudBoard(layout);
                    if (!board.CanTap(i, colour)) continue;

                    var p = new List<BudPulse>();
                    var w = new List<BudWash>();
                    var d = new List<BudDrop>();

                    var chain = board.Tap(i, colour, p, w, d);
                    if (pulses != null && chain.Waves <= best.Waves) continue;

                    best = chain;
                    pulses = p.ToArray();
                    drops = d.ToArray();
                }

                Assert.IsNotNull(pulses, rung.Id + ": no legal opening tap");

                var score = BudStage.Of(best.Waves, pulses, System.Array.Empty<BudWash>(),
                                        drops, layout.Width);

                yield return new Tap(rung.Id, score, pulses, drops, best.Waves, layout.Width);
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
                var burst = new Dictionary<int, float>();
                foreach (var cue in tap.Score.Cues)
                    if (cue.Kind == BudCueKind.Burst) burst[Key(cue.Wave, cue.Cell)] = cue.At;

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
                            + $"but the flower at {cell} it falls past does not go off until "
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
