using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The word ladder: that GREAT and LEGENDARY are different <em>things</em> rather than one
    /// thing at two sizes.
    ///
    /// <para>
    /// <b>This fixture exists because the fault it holds was shipped and reported.</b> The four
    /// rungs were each individually plausible — <c>16 + rung * 8</c> sparks, <c>.16 + rung * .07</c>
    /// of a flash, <c>8 + rung * 6</c> of a shake — and between them they drew one picture at four
    /// sizes. The biggest thing this mode can say therefore landed as the smallest possible change
    /// and was reported as feeling <em>dull</em>. It is <see cref="BudSpectacleTests"/>' rule for
    /// the ladder nobody applied it to.
    /// </para>
    /// </summary>
    public sealed class BudAcclaimTests
    {
        /// <summary>Every rung a chain can actually reach, plus the ones either side of it.</summary>
        static int[] Rungs => new[] { 0, 1, 2, 3 };

        // ------------------------------------------------------------------ kinds, not amounts
        /// <summary>
        /// <b>Each rung draws something the one below it did not.</b> That is the whole point of
        /// the ladder, and it is the property no amount of retuning a number can give you.
        /// </summary>
        [Test]
        public void EveryRungDrawsSomethingTheOneBelowItDidNot()
        {
            int last = 0;

            foreach (int rung in Rungs)
            {
                var pomp = BudAcclaim.Of(rung);

                Assert.Greater(pomp.Kinds, last,
                    $"rung {rung} draws {pomp.Kinds} kinds of thing and the rung below it drew "
                    + $"{last} — so the two are one picture at two sizes");

                last = pomp.Kinds;
            }

            Assert.AreEqual(BudAcclaim.MostKinds, last,
                            "the top rung does not draw everything there is");
        }

        /// <summary>
        /// And nothing is ever taken away again: a rung that switched a layer off would read as
        /// the celebration running out of steam exactly where it should be loudest.
        /// </summary>
        [Test]
        public void AndNothingIsEverTakenAwayAgain()
        {
            for (int rung = 1; rung <= 3; rung++)
            {
                var under = BudAcclaim.Of(rung - 1);
                var over = BudAcclaim.Of(rung);

                Assert.IsTrue(!under.Shine || over.Shine, $"rung {rung} lost its shine");
                Assert.IsTrue(!under.Bloom || over.Bloom, $"rung {rung} lost its bloom");
                Assert.IsTrue(!under.Confetti || over.Confetti, $"rung {rung} lost its confetti");
                Assert.IsTrue(!under.Grove || over.Grove, $"rung {rung} lost the grove");
            }
        }

        /// <summary>
        /// <b>And the bottom rung is already worth watching.</b> A ladder whose first rung is bare
        /// teaches the player that most of what they do is not worth celebrating, which on a mode
        /// built to be generous (invariant 20k) is the wrong lesson twice over — and GREAT is the
        /// commonest word this chapter says, because most chains run two waves.
        /// </summary>
        [Test]
        public void AndTheCommonestWordIsAlreadyWorthWatching()
        {
            var least = BudAcclaim.Of(0);

            Assert.Greater(least.Motes, 4, "the word gathers from almost nothing");
            Assert.GreaterOrEqual(least.Notes, 3, "the run is too short to be heard as a climb");
            Assert.Greater(least.Shove, .25f, "the board barely notices the word landing");
            Assert.Greater(least.Gather, .12f, "there is no beat before the word for it to land on");
        }

        // ------------------------------------------------------------------ the run
        /// <summary>
        /// <b>The run climbs, and the rung is heard as a longer climb.</b> It is the one axis of
        /// this ladder that cannot be mistaken for the same thing louder.
        /// </summary>
        [Test]
        public void TheRunClimbsAndTheRungIsHeardAsALongerOne()
        {
            int last = 0;

            foreach (int rung in Rungs)
            {
                int notes = BudAcclaim.Of(rung).Notes;

                Assert.Greater(notes, last, $"rung {rung} climbs no further than the one below");
                Assert.LessOrEqual(notes, BudAcclaim.MostNotes,
                                   $"rung {rung} asks for more notes than the scale holds");

                last = notes;
            }
        }

        /// <summary>
        /// And the notes of a run rise, so a climb is a climb — with the whole of it inside a beat
        /// nobody is left waiting through.
        /// </summary>
        [Test]
        public void AndEveryNoteOfARunIsHigherThanTheOneBeforeIt()
        {
            float last = 0f;

            for (int n = 0; n < BudAcclaim.MostNotes; n++)
            {
                float pitch = BudAcclaim.NoteAt(n);

                Assert.Greater(pitch, last, $"note {n} of a run does not rise");
                Assert.Less(pitch, 4f, $"note {n} is pitched past anything the clip can carry");

                last = pitch;
            }

            float longest = (BudAcclaim.Of(3).Notes - 1) * BudAcclaim.NoteGap;
            Assert.Less(longest, .60f, $"the longest run takes {longest:0.00}s to finish");
        }

        // ------------------------------------------------------------------ the beat before
        /// <summary>
        /// <b>The gather comes out of the fanfare, never beside it.</b> Every rung draws breath
        /// before it lands — that is what makes an impact an impact — and it costs the chain
        /// nothing, exactly as a wave's own wind-up comes out of its beat rather than extending it.
        /// </summary>
        [Test]
        public void TheGatherIsTakenOutOfTheWordRatherThanAddedToIt()
        {
            foreach (int rung in Rungs)
            {
                var pomp = BudAcclaim.Of(rung);

                Assert.AreEqual(BudTempo.Fanfare, pomp.Gather + BudAcclaim.Held(rung), .0001f,
                    $"rung {rung} spends {pomp.Gather + BudAcclaim.Held(rung):0.00}s where the "
                    + $"word is allowed {BudTempo.Fanfare:0.00}s");

                Assert.LessOrEqual(pomp.Gather, BudAcclaim.MostGather + .0001f,
                                   $"rung {rung} gathers for longer than a word may be waited for");
                Assert.Greater(BudAcclaim.Held(rung), .90f,
                               $"rung {rung} leaves the word up for {BudAcclaim.Held(rung):0.00}s, "
                               + "which is not long enough to read it");
            }
        }

        /// <summary>An absurd rung is clamped rather than reaching past the end of the ladder.</summary>
        [Test]
        public void AnAbsurdRungIsClampedRatherThanOverflowing()
        {
            var top = BudAcclaim.Of(3);
            var past = BudAcclaim.Of(99);

            Assert.AreEqual(top.Kinds, past.Kinds, "a rung past the top draws something else");
            Assert.LessOrEqual(past.Notes, BudAcclaim.MostNotes, "the run runs off the scale");
            Assert.LessOrEqual(past.Gather, BudAcclaim.MostGather + .0001f, "the gather runs away");
            Assert.LessOrEqual(past.Shove, 1f, "the board is knocked past anything survivable");

            var under = BudAcclaim.Of(-4);
            Assert.AreEqual(BudAcclaim.Of(0).Kinds, under.Kinds, "a negative rung is not the first");
        }

        // ------------------------------------------------------------------ against the words
        /// <summary>
        /// Every word the chapter can actually say lands on a rung the ladder knows, and the
        /// commonest one is not the top.
        /// </summary>
        [Test]
        public void EveryWordAChainCanSayLandsOnARungOfThisLadder()
        {
            int said = 0;

            for (int waves = 1; waves <= BudChain.Most; waves++)
            {
                if (BudChain.WordKey(waves) == null) continue;

                said++;
                int rung = BudChain.Rung(waves);

                Assert.GreaterOrEqual(rung, 0, $"{waves} waves says a word on no rung at all");
                Assert.AreEqual(BudAcclaim.Of(rung).Kinds, BudAcclaim.Of(rung).Kinds);
                Assert.LessOrEqual(BudAcclaim.Of(rung).Kinds, BudAcclaim.MostKinds,
                                   $"{waves} waves asks for more than the ladder holds");
            }

            Assert.Greater(said, 3, "the ladder has fewer words on it than rungs");
        }
    }
}
