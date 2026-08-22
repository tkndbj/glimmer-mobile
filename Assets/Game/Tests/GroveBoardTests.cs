using System;
using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Homestead;
using GlimmerGrove.Progression;
using GlimmerGrove.Social;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The client half of the public boards' shared contract.
    ///
    /// <para>
    /// A grove's worth, the public form of a keeper's name and the league a score ranks in
    /// are all derived in two places — here, so the game can draw them offline, and in
    /// <c>functions/src/grove.ts</c>, so a forged save cannot rank. Two implementations of
    /// one rule drift, and this one drifts silently: nothing crashes, nothing is refused,
    /// and a player simply sees one number over their own grove and a different one beside
    /// their name on a board. So both sides run
    /// <c>firebase/shared/grove-vectors.json</c>; <c>firebase/functions/test/grove.mjs</c>
    /// is the other half. Invariant 9a, for the boards.
    /// </para>
    /// <para>
    /// <b>What is under contract, and what deliberately is not.</b> The summation is —
    /// which pieces count, that a free one counts for nothing, that starter land is not a
    /// purchase — because that is what has to produce the same number on both sides. The
    /// <em>clamp</em> is not: it exists to disbelieve a client, so there is nothing here to
    /// mirror and the cases carrying one are marked server-only in the vector file. Nor is
    /// "does this player hold it", which the two sides answer with different evidence — the
    /// client asks its ledgers and the server asks a save it does not believe. That is why
    /// <see cref="IGroveHoldings"/> exists and why it is the only thing this file supplies.
    /// </para>
    /// <para>
    /// The catalog is a real <c>homestead.json</c> read through <c>HomesteadMapper</c>, the
    /// reader that ships. A vector proved against a parser written for the test proves
    /// nothing about the game.
    /// </para>
    /// </summary>
    public sealed class GroveBoardTests
    {
        // ------------------------------------------------------------- the file
        [Serializable]
        public sealed class VectorFile
        {
            public WorthCase[] worthCases;
            public StarCase[] starCases;
            public NameCase[] nameCases;
            public CompanionCase[] companions;
            public long[] starLadder;
        }

        [Serializable]
        public sealed class WorthCase
        {
            public string name;
            public int keeperLevel;
            public string[] pieces;
            public string[] land;
            public string[] companions;

            /// <summary>The server's ceiling on the bought half. Ignored here — see the remarks.</summary>
            public long affordable;

            public long earned;
            public long bought;
            public long score;
            public int stars;

            /// <summary>True when the ceiling bit, which makes the case server-only.</summary>
            public bool clamped;

            /// <summary>
            /// True when the two halves legitimately disagree, so only the server runs it.
            ///
            /// One thing puts a case here: a save naming a companion whose gate its own keeper
            /// level has not reached. The server drops it, because no honest save can hold one
            /// — the rule is level AND purchase. This side counts it, because it is scoring
            /// holdings it knows to be real and a purchase is permanent, so a companion kept
            /// through a gate retune is still the player's. Both are right; the case cannot
            /// pin both.
            /// </summary>
            public bool serverOnly;
        }

        [Serializable]
        public sealed class StarCase
        {
            public long score;
            public int stars;
            public string league;
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

        [Serializable]
        public sealed class CompanionCase
        {
            public string id;
            public int unlockLevel;
            public int unlockCost;
        }

        static string SharedPath(string file)
            => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "firebase", "shared", file));

        static VectorFile Load()
        {
            string path = SharedPath("grove-vectors.json");
            Assert.IsTrue(File.Exists(path), $"shared grove vectors not found at {path}");

            var file = JsonUtility.FromJson<VectorFile>(File.ReadAllText(path));
            Assert.IsNotNull(file, "the vector file did not parse");
            Assert.IsNotNull(file.worthCases, "the vector file has no worth cases");
            Assert.Greater(file.worthCases.Length, 0);

            return file;
        }

        static HomesteadCatalog LoadCatalog(VectorFile file)
        {
            string path = SharedPath("grove-catalog.json");
            Assert.IsTrue(File.Exists(path), $"shared grove catalog not found at {path}");

            var problems = new List<string>();
            Assert.IsTrue(HomesteadMapper.TryRead(File.ReadAllText(path), problems, out var catalog),
                          "the shared catalog did not read: " + string.Join("; ", problems));

            // Residents are projected from the roster rather than authored, so the catalog
            // only knows about them once it is given one. This is the same call the game
            // makes when the manifest's companions arrive.
            var roster = new List<AvatarDefinition>();
            foreach (var companion in file.companions ?? Array.Empty<CompanionCase>())
                roster.Add(new AvatarDefinition(companion.id, companion.id, string.Empty,
                                                companion.unlockLevel, companion.unlockCost));

            return catalog.WithResidents(roster);
        }

        /// <summary>
        /// The holdings a vector case describes.
        ///
        /// <para>
        /// The unlock rule is spelled out here rather than delegated, and that is deliberate:
        /// the shipped one reads <c>PlayerProgression</c> and <c>CompanionLedger</c>, which are
        /// process-wide statics a vector cannot set without an Editor. What is being proved is
        /// the <em>summation</em> over the catalog — see the type's remarks — so the holdings
        /// are supplied and the walk is the shipped one.
        /// </para>
        /// <para>
        /// <b>Nothing here reads <c>AvatarCatalog</c>, and the first version did.</b> It asked
        /// <c>AvatarCatalog.ReachedBy</c>, which resolves against whichever roster the process
        /// happens to hold — the manifest's in a running game, the built-in fallback in a test
        /// run — so a vector's own roster was being scored against gates it had never named,
        /// and the case failed with a number nothing in the file explained. The gate is read
        /// off the projected piece instead, which is where the catalog put the vector's own.
        /// </para>
        /// </summary>
        sealed class CaseHoldings : IGroveHoldings
        {
            readonly HashSet<string> _pieces;
            readonly HashSet<string> _land;
            readonly HashSet<string> _companions;
            readonly int _level;

            public CaseHoldings(WorthCase c)
            {
                _pieces = new HashSet<string>(c.pieces ?? Array.Empty<string>(), StringComparer.Ordinal);
                _land = new HashSet<string>(c.land ?? Array.Empty<string>(), StringComparer.Ordinal);
                _companions = new HashSet<string>(c.companions ?? Array.Empty<string>(), StringComparer.Ordinal);
                _level = c.keeperLevel;
            }

            public bool Holds(HomesteadPiece piece)
            {
                if (!piece.IsValid) return false;

                if (piece.IsResident)
                {
                    // <b>Bought, and only bought, for anything with a price on it.</b> This
                    // read `level >= gate || bought` for as long as the unlock rule was level
                    // OR purchase, and it is the last place in the game that still did — the
                    // shipped rule became keeper level AND purchase, so reaching a gate now
                    // grants nothing and only opens the shop cell.
                    //
                    // Mirrors CompanionLedger.IsHeld rather than restating it, because this
                    // double cannot reach a ledger. The free clause is kept even though
                    // GroveScore.Value only ever counts a piece whose cost is positive: an
                    // adapter that silently answers a *different* question from the shipped
                    // one is how this case came to disagree with the server in the first
                    // place, and the day a free companion is given a price it has to be the
                    // price that decides, not this method's shape.
                    if (_companions.Contains(GroveResidents.CompanionIdOf(piece.Id))) return true;

                    return piece.Cost <= 0 && _level >= piece.RequiresKeeperLevel;
                }

                return _pieces.Contains(piece.Id);
            }

            public bool Owns(GroveRegion region)
                => region != null && region.IsValid && (region.IsStarter || _land.Contains(region.Id));
        }

        // ------------------------------------------------------------ the worth
        [Test]
        public void TheGroveWorthAgreesWithTheServerOnEveryVector()
        {
            var file = Load();
            var catalog = LoadCatalog(file);

            int ran = 0;

            foreach (var c in file.worthCases)
            {
                // A clamped case is the server disbelieving a save. There is nothing on this
                // side to compare it against — the client is scoring holdings it knows to be
                // real — so it is skipped rather than approximated. `serverOnly` is the same
                // situation arriving one step earlier: a companion dropped by its gate rather
                // than cut down by the ceiling.
                if (c.clamped || c.serverOnly) continue;

                long value = GroveScore.Value(catalog, new CaseHoldings(c));

                Assert.AreEqual(c.earned + c.bought, value,
                                $"{c.name}: the client and the server disagree about what this grove is worth");
                ran++;
            }

            Assert.Greater(ran, 0, "every worth case was skipped; the vectors prove nothing");
        }

        [Test]
        public void TheStarLadderAgreesWithTheServerOnEveryVector()
        {
            var file = Load();
            var table = new GroveScoreTable(file.starLadder);

            foreach (var c in file.starCases)
            {
                Assert.AreEqual(c.stars, table.StarsFor(c.score), $"stars for {c.score}");
                Assert.AreEqual(c.league, GroveLeague.IdFor(c.score, table), $"league for {c.score}");
            }
        }

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

        // -------------------------------------------------------------- leagues
        [Test]
        public void EveryLeagueTheLadderCanReachHasAnIdAndAName()
        {
            // The ladder may be up to GroveScoreTable.MaxStars long, so there has to be an
            // id and a name for every star count from none to all of them. A ladder that
            // outgrew the id list would put players on a board that does not exist.
            Assert.AreEqual(GroveScoreTable.MaxStars + 1, GroveLeague.Count);
            Assert.AreEqual(GroveLeague.Count, GroveLeague.All.Count);

            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int stars = 0; stars < GroveLeague.Count; stars++)
            {
                string id = GroveLeague.IdFor(stars);

                Assert.IsTrue(seen.Add(id), $"league id {id} is used twice");
                Assert.IsTrue(GroveLeague.IsKnown(id));
                Assert.AreEqual(stars, GroveLeague.StarsOf(id));
                Assert.IsNotEmpty(GroveLeague.NameKey(stars));
            }
        }

        [Test]
        public void AnUnknownLeagueIsNotMistakenForARealOne()
        {
            Assert.AreEqual(-1, GroveLeague.StarsOf("l9"));
            Assert.AreEqual(-1, GroveLeague.StarsOf(""));
            Assert.AreEqual(-1, GroveLeague.StarsOf(null));
            Assert.IsFalse(GroveLeague.IsKnown("global"));

            // Out of range clamps rather than throwing: a content drop that lengthened the
            // ladder past the ids would otherwise take the screen down rather than draw the
            // top league.
            Assert.AreEqual(GroveLeague.IdFor(GroveLeague.Count - 1), GroveLeague.IdFor(99));
            Assert.AreEqual(GroveLeague.IdFor(0), GroveLeague.IdFor(-3));
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
