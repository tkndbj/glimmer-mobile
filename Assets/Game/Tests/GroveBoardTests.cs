using System;
using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Social;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The client half of the public boards' shared contract.
    ///
    /// <para>
    /// The public form of a keeper's name is derived in two places — here, so the game can
    /// draw it offline, and in <c>functions/src/grove.ts</c>, so a forged save cannot rank.
    /// Two implementations of one rule drift, and this one drifts silently: nothing crashes,
    /// nothing is refused, and a player simply sees one spelling on their own screen and a
    /// different one beside their name on a board. So both sides run
    /// <c>firebase/shared/grove-vectors.json</c>; <c>firebase/functions/test/grove.mjs</c>
    /// is the other half. Invariant 9a, for the boards.
    /// </para>
    /// <para>
    /// <b>The worth half went with the Grovement on 2026-09-21.</b> <c>worthCases</c>,
    /// <c>starCases</c> and <c>starLadder</c> are still in the vector file and are still run
    /// by the server's half, because <c>groveWorth</c> is still deployed and still reads the
    /// saves it already holds — what is gone is the client that computed a second opinion.
    /// There is nothing to pair any more, so nothing here pairs it.
    /// </para>
    /// </summary>
    public sealed class GroveBoardTests
    {
        // ------------------------------------------------------------- the file
        [Serializable]
        public sealed class VectorFile
        {
            public NameCase[] nameCases;
        }

        /// <summary>
        /// One name case, read as code points rather than as strings.
        ///
        /// <para>
        /// <b><c>JsonUtility</c> does not survive these strings, and the failure is silent.</b>
        /// It truncated <c>Fern‮Willow</c> at the escape, so <c>stored</c> came back as
        /// four characters while <c>public</c> came back whole — which reads exactly like a bug
        /// in the sanitiser and is not one. The bidi and zero-width cases are the most
        /// important ones in the file, so the encoding they are carried in has to be the
        /// boring one. The server half reads the strings and asserts they agree with these
        /// codes, so neither encoding can drift away from the other.
        /// </para>
        /// </summary>
        [Serializable]
        public sealed class NameCase
        {
            public int[] storedCodes;
            public int[] publicCodes;
            public int[] keyCodes;

            /// <summary>Server-only: the word filter is not shipped in a client.</summary>
            public bool allowed;

            /// <summary>
            /// Whether the name may be reserved and published: the word filter <em>and</em> a
            /// non-empty fold. The client can only check the second half, which is
            /// <see cref="GroveNames.IsPublishable"/>.
            /// </summary>
            public bool claimable;

            public string Stored => Rebuild(storedCodes);
            public string Public => Rebuild(publicCodes);
            public string Key => Rebuild(keyCodes);

            static string Rebuild(int[] codes)
            {
                if (codes == null) return string.Empty;

                var builder = new System.Text.StringBuilder(codes.Length);
                foreach (int code in codes) builder.Append((char)code);

                return builder.ToString();
            }
        }

        static string SharedPath(string file)
            => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "firebase", "shared", file));

        static VectorFile Load()
        {
            string path = SharedPath("grove-vectors.json");
            Assert.IsTrue(File.Exists(path), $"shared grove vectors not found at {path}");

            var file = JsonUtility.FromJson<VectorFile>(File.ReadAllText(path));
            Assert.IsNotNull(file, "the vector file did not parse");
            Assert.IsNotNull(file.nameCases, "the vector file has no name cases");
            Assert.Greater(file.nameCases.Length, 0);

            return file;
        }

        // ------------------------------------------------------------- the name
        [Test]
        public void ThePublicNameAgreesWithTheServerOnEveryVector()
        {
            var cases = Load().nameCases;
            Assert.Greater(cases.Length, 0, "the vector file has no name cases");

            foreach (var c in cases)
            {
                Assert.AreEqual(c.Public, GroveNames.Public(c.Stored),
                                $"the public form of {Describe(c.Stored)}");
            }
        }

        /// <summary>
        /// The collision key is the id of the document that holds a reservation, so the two
        /// halves folding differently would make the client read one document and the server
        /// write another — a wrong hint rather than a duplicate name, because the transaction
        /// is still the authority, but invisible from either side alone. That is what these
        /// cases are for; `Ｆｅｒｎ`, `İzmir` and `ﬁre` are the ones no reading catches.
        /// </summary>
        [Test]
        public void TheCollisionKeyAgreesWithTheServerOnEveryVector()
        {
            var cases = Load().nameCases;
            Assert.Greater(cases.Length, 0, "the vector file has no name cases");

            foreach (var c in cases)
            {
                Assert.AreEqual(c.Key, GroveNames.Key(c.Stored),
                                $"the collision key of {Describe(c.Stored)}");
            }
        }

        /// <summary>
        /// Every name the server would reserve is one this client calls publishable, and the
        /// reverse — because the panel refuses a name locally rather than spending a read on
        /// it, and a client that let through something the server would not reserve would show
        /// somebody a name as free and then refuse to save it.
        ///
        /// <para>
        /// Only checked in the direction a client can see. The word filter lives on the server
        /// alone, so a name refused for a word is <em>expected</em> to look publishable here;
        /// what must agree is the pair of measurements, which is what
        /// <c>isNameClaimable</c> adds over <c>isNameAllowed</c>.
        /// </para>
        /// </summary>
        [Test]
        public void APublishableNameIsExactlyOneWithBothLengths()
        {
            foreach (var c in Load().nameCases)
            {
                // `allowed` without `claimable` is precisely the case this pair exists for: two
                // visible characters and an empty fold.
                if (c.allowed && !c.claimable)
                {
                    Assert.IsFalse(GroveNames.IsPublishable(c.Stored),
                                   $"{Describe(c.Stored)} folds to nothing and must not publish");
                    continue;
                }

                if (c.claimable)
                {
                    Assert.IsTrue(GroveNames.IsPublishable(c.Stored),
                                  $"{Describe(c.Stored)} is claimable and must be publishable");
                }
            }
        }

        /// <summary>
        /// The fold's whole job, stated as the thing a player would notice: these are one name.
        /// </summary>
        [Test]
        public void CaseWidthAndSeparatorsDoNotMakeASecondName()
        {
            string[] sameName =
            {
                "Fern", "fern", "FERN", "F e r n", "Ｆｅｒｎ",
                "Fern-Willow".Replace("-Willow", ""), " Fern ", "F.e.r.n",
            };

            foreach (string spelling in sameName)
            {
                Assert.AreEqual("fern", GroveNames.Key(spelling),
                                $"{Describe(spelling)} is the same name as Fern");
            }

            Assert.AreNotEqual(GroveNames.Key("Fern"), GroveNames.Key("Fern2"),
                               "a digit is a different name");
        }

        /// <summary>Spells the invisible characters, so a failure message can be read.</summary>
        static string Describe(string text)
        {
            if (text == null) return "<null>";

            var builder = new System.Text.StringBuilder(text.Length + 8).Append('"');

            foreach (char c in text)
            {
                if (c >= ' ' && c <= '~') builder.Append(c);
                else builder.Append("\\u").Append(((int)c).ToString("X4"));
            }

            return builder.Append('"').ToString();
        }

        // --------------------------------------------------------------- boards
        [Test]
        public void EveryBoardThisBuildAsksForIsOneTheServerWrites()
        {
            // `BOARD_IDS` in functions/src/grove.ts is what actually exists, and it is what
            // `LeaderboardBoard.All` mirrors. A board named here and not there is a screen
            // that draws an empty list for ever with nothing to say why; a board named there
            // and not here is a nightly write nobody can read.
            CollectionAssert.AreEqual(new[] { "global", "endless" }, LeaderboardBoard.All);

            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (string id in LeaderboardBoard.All)
            {
                Assert.IsTrue(seen.Add(id), $"board id {id} is used twice");
                Assert.IsTrue(LeaderboardBoard.IsKnown(id), id);
                Assert.IsNotEmpty(id);
            }
        }

        [Test]
        public void ARetiredBoardIdIsRefusedRatherThanFetched()
        {
            // The nine league boards are gone. What matters is not that they are absent from
            // the list but that `IsKnown` refuses them: an id this client composed and the
            // server does not write is a document read nobody should pay for, and a deep link
            // from an older build is exactly where one would come from.
            foreach (string retired in new[] { "l0", "l4", "l8", "league", "" })
                Assert.IsFalse(LeaderboardBoard.IsKnown(retired), retired);

            Assert.IsFalse(LeaderboardBoard.IsKnown(null));
        }

        [Test]
        public void OnlyTheEndlessBoardIsReadInWaves()
        {
            // Which figure a row prints is the board's decision and not the row's, so this is
            // the one predicate standing between the endless list and a column of numbers it
            // is not sorted by.
            Assert.IsTrue(LeaderboardBoard.IsEndless(LeaderboardBoard.Endless));
            Assert.IsFalse(LeaderboardBoard.IsEndless(LeaderboardBoard.Global));
            Assert.IsFalse(LeaderboardBoard.IsEndless(null));
            Assert.IsFalse(LeaderboardBoard.IsEndless("l3"));
        }

        [Test]
        public void ARowCarriesBothFiguresWhicheverBoardItCameOff()
        {
            var row = new LeaderboardEntry(1, "uid", "Fern", "coral", 7, 4200L, 3, 41);

            Assert.AreEqual(4200L, row.Score);
            Assert.AreEqual(41, row.Wave);

            // Absent reads as nought rather than as missing, which is what a global row is:
            // the server writes the field on every row and most of them have never played
            // the lane.
            Assert.AreEqual(0, new LeaderboardEntry(1, "uid", "Fern", "coral", 7, 4200L, 3).Wave);
            Assert.AreEqual(0, new LeaderboardEntry(1, "uid", "Fern", "coral", 7, 0L, 0, -9).Wave);
        }

        // ---------------------------------------------------------- the distribution
        [Test]
        public void APercentileNeedsEnoughKeepersToMeanSomething()
        {
            var deciles = new long[] { 1000, 2000, 3000, 4000, 5000, 6000, 7000, 8000, 9000 };

            var thin = new GroveRankTable(GroveRankTable.MinimumSamples - 1, deciles);
            Assert.IsFalse(thin.IsUsable);
            Assert.AreEqual(-1, thin.PercentBelow(5000));
            Assert.AreEqual(-1, thin.TopPercent(5000));

            var enough = new GroveRankTable(GroveRankTable.MinimumSamples, deciles);
            Assert.IsTrue(enough.IsUsable);
            Assert.AreEqual(50, enough.PercentBelow(5000));
            Assert.AreEqual(50, enough.TopPercent(5000));
        }

        [Test]
        public void AGroveWorthNothingIsNotToldItIsBehindEverybody()
        {
            // Zero is not in the population the deciles describe — see GroveRankTable — and
            // "you are behind everybody" is the one thing a progress screen must never say.
            var table = new GroveRankTable(1000,
                new long[] { 1000, 2000, 3000, 4000, 5000, 6000, 7000, 8000, 9000 });

            Assert.AreEqual(-1, table.PercentBelow(0));
            Assert.AreEqual(-1, table.PercentBelow(-5));
        }

        [Test]
        public void AStandingIsNeverZeroOrAHundred()
        {
            var table = new GroveRankTable(1000,
                new long[] { 1000, 2000, 3000, 4000, 5000, 6000, 7000, 8000, 9000 });

            Assert.AreEqual(GroveRankTable.MinRank, table.PercentBelow(1));
            Assert.AreEqual(GroveRankTable.MaxRank, table.PercentBelow(long.MaxValue));
            Assert.AreEqual(GroveRankTable.MinRank, table.TopPercent(long.MaxValue));
            Assert.AreEqual(GroveRankTable.MaxRank, table.TopPercent(1));
        }

        [Test]
        public void TheStandingRisesWithTheScore()
        {
            var table = new GroveRankTable(1000,
                new long[] { 1000, 2000, 3000, 4000, 5000, 6000, 7000, 8000, 9000 });

            int previous = -1;

            for (long score = 500; score <= 10000; score += 250)
            {
                int below = table.PercentBelow(score);
                Assert.GreaterOrEqual(below, previous, $"the standing fell at {score}");
                previous = below;
            }
        }

        [Test]
        public void ADecileTableThatIsNotNineNumbersSaysNothing()
        {
            Assert.IsFalse(new GroveRankTable(1000, new long[] { 1, 2, 3 }).IsUsable);
            Assert.IsFalse(new GroveRankTable(1000, null).IsUsable);
            Assert.IsFalse(GroveRankTable.None.IsUsable);
            Assert.IsFalse(default(GroveRankTable).IsUsable);
        }

        // -------------------------------------------------------------- the names
        [Test]
        public void AnUnpublishableNameIsRecognisedBeforeItIsSent()
        {
            Assert.IsFalse(GroveNames.IsPublishable(null));
            Assert.IsFalse(GroveNames.IsPublishable(""));
            Assert.IsFalse(GroveNames.IsPublishable("   "));
            Assert.IsFalse(GroveNames.IsPublishable("A"));

            // Sixteen zero-width joiners is an empty name wearing a length, which is the
            // whole reason the check is made on the public form rather than on the stored one.
            Assert.IsFalse(GroveNames.IsPublishable(new string('\u200D', 16)));

            Assert.IsTrue(GroveNames.IsPublishable("Ab"));
            Assert.IsTrue(GroveNames.IsPublishable("Mossfoot"));
        }

        [Test]
        public void APublicNameIsNeverLongerThanTheLimit()
        {
            Assert.LessOrEqual(GroveNames.Public(new string('x', 200)).Length, GroveNames.MaxLength);

            // Padding must not be usable to push real characters past the limit, which is
            // why the cut happens after whitespace is collapsed rather than before.
            Assert.AreEqual(GroveNames.MaxLength,
                            GroveNames.Public("          " + new string('x', 40)).Length);
        }
    }
}
