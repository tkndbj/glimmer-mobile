using System.Collections.Generic;
using GlimmerGrove.Content;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// What a level says while it is played, and the two rules that keep it from becoming noise.
    ///
    /// <para>
    /// Pure Domain, so it runs in the offline runner. The half that cannot run there — a chapter
    /// body's <c>story</c> block surviving <c>JsonUtility</c>'s nested arrays — was proved in the
    /// Editor against the shipped chapter instead (three levels, 7/8/8 beats, every cue and line
    /// count as authored), because a fixture that needs the Editor is a fixture the offline
    /// runner skips on the way past.
    /// </para>
    /// </summary>
    public sealed class StoryTests
    {
        static StoryScript Script(params (StoryCue Cue, string Key)[] beats)
        {
            var list = new List<StoryBeat>();

            foreach (var beat in beats)
                list.Add(new StoryBeat(beat.Cue,
                                       new[] { new StoryLine(StoryCast.Bolt, beat.Key) }));

            return new StoryScript(list);
        }

        // ------------------------------------------------------------------ handing them out
        /// <summary>
        /// Several beats may share a cue, and they are handed out in order and then run dry.
        ///
        /// A deck that frees four critters and hears one sentence four times is a deck whose
        /// dialogue stops being read, so an author writes as many lines for a cue as they have
        /// things to say and the fifth rescue passes without comment.
        /// </summary>
        [Test]
        public void ACueIsHandedOutInOrderAndThenFallsSilent()
        {
            var story = Script((StoryCue.Freed, "a"), (StoryCue.Freed, "b"));

            Assert.AreEqual("a", story.Take(StoryCue.Freed, 0).Lines[0].Key);
            Assert.AreEqual("b", story.Take(StoryCue.Freed, 1).Lines[0].Key);
            Assert.IsNull(story.Take(StoryCue.Freed, 2), "the author ran out of things to say");
        }

        [Test]
        public void CuesAreCountedApartFromEachOther()
        {
            var story = Script((StoryCue.Intro, "i"), (StoryCue.Freed, "f"));

            Assert.AreEqual("f", story.Take(StoryCue.Freed, 0).Lines[0].Key,
                            "an earlier beat of a different cue must not consume this one");
            Assert.IsNull(story.Take(StoryCue.Lost, 0), "nothing was written for a loss");
        }

        [Test]
        public void ALevelThatSaysNothingIsSilentRatherThanNull()
        {
            Assert.IsFalse(StoryScript.Silent.Any);
            Assert.IsNull(StoryScript.Silent.Take(StoryCue.Intro, 0),
                          "silence answers nothing rather than throwing");
        }

        // ------------------------------------------------------------------ what may be authored
        [Test]
        public void EveryCueNameTheDocumentationOffersIsOneTheReaderKnows()
        {
            foreach (var name in StoryScript.CueNames.Split(','))
                Assert.IsTrue(StoryScript.TryReadCue(name.Trim(), out _),
                              $"'{name.Trim()}' is offered to authors and not understood");
        }

        [Test]
        public void ACueNobodyKnowsIsRefusedRatherThanGuessedAt()
        {
            Assert.IsFalse(StoryScript.TryReadCue("whenever", out _));
            Assert.IsFalse(StoryScript.TryReadCue(string.Empty, out _));
            Assert.IsFalse(StoryScript.TryReadCue(null, out _));
        }

        /// <summary>
        /// A speaker names a folder of frames, so a name nothing recognises is a portrait that
        /// does not load — and a missing sprite draws as a white rectangle rather than as nothing
        /// (invariant 7b). That is why the cast is a list a gate can walk.
        /// </summary>
        [Test]
        public void OnlyTheCastMaySpeak()
        {
            foreach (var who in StoryCast.All)
                Assert.IsTrue(StoryCast.Knows(who), $"'{who}' is in the cast and not recognised");

            Assert.IsFalse(StoryCast.Knows("keeper"));
            Assert.IsFalse(StoryCast.Knows(string.Empty));
            Assert.IsFalse(StoryCast.Knows(null));
        }

        [Test]
        public void ALineNeedsBothASpeakerAndAKey()
        {
            Assert.IsTrue(new StoryLine(StoryCast.Collector, "story.x").IsValid);
            Assert.IsFalse(new StoryLine("nobody", "story.x").IsValid, "an unknown speaker");
            Assert.IsFalse(new StoryLine(StoryCast.Collector, null).IsValid, "no key");
            Assert.IsFalse(new StoryLine(StoryCast.Collector, string.Empty).IsValid);
        }

        [Test]
        public void ABeatWithNoLinesIsEmptyRatherThanNull()
        {
            var beat = new StoryBeat(StoryCue.Won, null);

            Assert.NotNull(beat.Lines, "a caller walking the lines must not have to test for null");
            Assert.AreEqual(0, beat.Lines.Count);
        }
    }
}
