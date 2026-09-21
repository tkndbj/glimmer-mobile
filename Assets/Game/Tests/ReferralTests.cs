using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Content;
using GlimmerGrove.Referral;
using GlimmerGrove.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Refer-a-friend on the client: the table's reader, the code fold both runtimes share,
    /// the landing rule that decides who banks a chest, and the milestone question.
    ///
    /// Everything here is a pure function on purpose (invariant 51): the state is the
    /// server's, and what the device decides is which of the server's answers to act on.
    /// </summary>
    public sealed class ReferralTests
    {
        static ChestTier Tier(string id) => TaskTable.Default.Tier(id);

        static ReferralTable Resolve(ReferralDto dto, List<string> problems)
            => ReferralTable.Resolve(dto, Tier, problems);

        static ReferralDto Shipped()
            => new ReferralDto
            {
                milestoneChapter = "s01_thornwatch",
                maxBound = 50,
                shareLink = "https://glimmergroove.app",
                invitee = new ReferralPaymentDto { tier = "royal", count = 2 },
                perInvitee = new ReferralPaymentDto { tier = "royal", count = 2 },
            };

        // ------------------------------------------------------------- the table
        [Test]
        public void TheShippedBlockReads()
        {
            var problems = new List<string>();
            var table = Resolve(Shipped(), problems);

            Assert.IsEmpty(problems);
            Assert.IsTrue(table.Offers);
            Assert.AreEqual("s01_thornwatch", table.Milestone.Value);
            Assert.AreEqual(50, table.MaxBound);
            Assert.AreEqual("royal", table.PerInvitee.Tier.Id);
            Assert.AreEqual(2, table.PerInvitee.Count);
            Assert.AreEqual("royal", table.Invitee.Tier.Id);
            Assert.AreEqual(2, table.Invitee.Count);
        }

        [Test]
        public void AnAbsentBlockIsTheBuiltInTable()
        {
            var problems = new List<string>();
            Assert.AreSame(ReferralTable.Default, Resolve(null, problems));
            Assert.AreSame(ReferralTable.Default, Resolve(new ReferralDto(), problems));
            Assert.IsEmpty(problems);
        }

        [Test]
        public void AWithdrawnBlockOffersNothing()
        {
            var problems = new List<string>();
            var dto = Shipped();
            dto.withdrawn = true;

            var table = Resolve(dto, problems);
            Assert.IsFalse(table.Offers);
            Assert.IsEmpty(problems, "withdrawing on purpose is not a problem");
        }

        [Test]
        public void AnAbsentCountIsOne()
        {
            var problems = new List<string>();
            var dto = Shipped();
            dto.invitee.count = 0;

            var table = Resolve(dto, problems);
            Assert.AreEqual(1, table.Invitee.Count);
            Assert.IsEmpty(problems);
        }

        [Test]
        public void ACountOverTheCeilingIsRefused()
        {
            var problems = new List<string>();
            var dto = Shipped();
            dto.perInvitee.count = ReferralTable.MaxCount + 1;

            Assert.AreSame(ReferralTable.Default, Resolve(dto, problems));
            Assert.IsNotEmpty(problems);
        }

        [Test]
        public void ATierNobodyDefinesIsRefused()
        {
            var problems = new List<string>();
            var dto = Shipped();
            dto.invitee.tier = "diamond";

            Assert.AreSame(ReferralTable.Default, Resolve(dto, problems));
            Assert.IsNotEmpty(problems);
        }

        [Test]
        public void ACapOverTheCeilingIsClamped()
        {
            var problems = new List<string>();
            var dto = Shipped();
            dto.maxBound = 9999;

            var table = Resolve(dto, problems);
            Assert.AreEqual(ReferralTable.MaxBoundCeiling, table.MaxBound);
            Assert.AreEqual(1, problems.Count);
        }

        [Test]
        public void AShareLinkThatIsNotHttpsIsDroppedNotFatal()
        {
            var problems = new List<string>();
            var dto = Shipped();
            dto.shareLink = "market://details?id=x";

            var table = Resolve(dto, problems);
            Assert.IsTrue(table.Offers);
            Assert.AreEqual(string.Empty, table.ShareLink);
            Assert.AreEqual(1, problems.Count);
        }

        [Test]
        public void TheBuiltInTableMatchesTheShippedContent()
        {
            var shipped = Resolve(Shipped(), new List<string>());
            var built = ReferralTable.Default;

            Assert.AreEqual(shipped.Milestone, built.Milestone);
            Assert.AreEqual(shipped.MaxBound, built.MaxBound);
            Assert.AreEqual(shipped.PerInvitee.Tier.Id, built.PerInvitee.Tier.Id);
            Assert.AreEqual(shipped.PerInvitee.Count, built.PerInvitee.Count);
            Assert.AreEqual(shipped.Invitee.Tier.Id, built.Invitee.Tier.Id);
            Assert.AreEqual(shipped.Invitee.Count, built.Invitee.Count);
        }

        // -------------------------------------------------------------- the code
        [Test]
        public void TheFoldAgreesWithTheSharedVectors()
        {
            string path = RepoPath("firebase", "shared", "referral-vectors.json");
            Assert.IsTrue(File.Exists(path), $"shared referral vectors not found at {path}");

            var vectors = JsonUtility.FromJson<Vectors>(File.ReadAllText(path));
            Assert.AreEqual(vectors.alphabet, ReferralCode.Alphabet);
            Assert.AreEqual(vectors.length, ReferralCode.Length);
            Assert.IsNotEmpty(vectors.cases);

            foreach (var c in vectors.cases)
            {
                Assert.AreEqual(c.folded, ReferralCode.Normalise(c.typed), $"fold of '{c.typed}'");
                Assert.AreEqual(c.valid, ReferralCode.IsValid(c.folded), $"validity of '{c.folded}'");
            }
        }

        [Test]
        public void ACodeIsShownInTwoGroups()
        {
            Assert.AreEqual("ABCD-EFGH", ReferralCode.Display("ABCDEFGH"));
            Assert.AreEqual(string.Empty, ReferralCode.Display(null));
            Assert.AreEqual("ABC", ReferralCode.Display("ABC"), "a string that is not a code is shown as it is");
        }

        // ----------------------------------------------------------- the landing
        [Test]
        public void APaidReplyIsBankedHere()
        {
            Assert.AreEqual(ReferralLanding.Verdict.Bank,
                            ReferralLanding.Decide(ReferralClaimOutcome.Paid, inFlightHere: false));
            Assert.AreEqual(ReferralLanding.Verdict.Bank,
                            ReferralLanding.Decide(ReferralClaimOutcome.Paid, inFlightHere: true));
        }

        [Test]
        public void AnAlreadyPaidReplyIsBankedOnlyByTheDeviceThatAsked()
        {
            Assert.AreEqual(ReferralLanding.Verdict.Bank,
                            ReferralLanding.Decide(ReferralClaimOutcome.AlreadyPaid, inFlightHere: true),
                            "a lost reply's retry banks");
            Assert.AreEqual(ReferralLanding.Verdict.Skip,
                            ReferralLanding.Decide(ReferralClaimOutcome.AlreadyPaid, inFlightHere: false),
                            "another device, a reinstall or a double tap does not");
        }

        [Test]
        public void ARefusalBanksNothing()
        {
            Assert.AreEqual(ReferralLanding.Verdict.Nothing,
                            ReferralLanding.Decide(ReferralClaimOutcome.NotYet, true));
            Assert.AreEqual(ReferralLanding.Verdict.Nothing,
                            ReferralLanding.Decide(ReferralClaimOutcome.Unknown, true));
            Assert.AreEqual(ReferralLanding.Verdict.Nothing,
                            ReferralLanding.Decide(ReferralClaimOutcome.Unavailable, true));
        }

        [Test]
        public void TheSubjectIsTheServersSpelling()
        {
            Assert.AreEqual("rung:5:1", ReferralLanding.Subject(ReferralClaimKind.Rung, 5, 1));
            Assert.AreEqual("rung:5:2", ReferralLanding.Subject(ReferralClaimKind.Rung, 5, 2));
            Assert.AreEqual("invitee:1", ReferralLanding.Subject(ReferralClaimKind.Invitee, 0, 1));
        }

        // ---------------------------------------------------------- the milestone
        [Test]
        public void TheMilestoneIsCompleteWhenEveryLevelIsCleared()
        {
            var chapter = ChapterId.Parse("ch_a");
            var a1 = LevelId.Parse("a1");
            var a2 = LevelId.Parse("a2");

            var builder = new CatalogIndexBuilder();
            builder.Add(new ManifestChapterDto { id = "ch_a", order = 10, version = 1, levels = new[] { "a1", "a2" } }, 1);
            var index = builder.Build();

            var cleared = new HashSet<LevelId> { a1 };
            Assert.IsFalse(ReferralMilestone.IsComplete(index, chapter, cleared.Contains));
            Assert.AreEqual((1, 2), ReferralMilestone.Progress(index, chapter, cleared.Contains));

            cleared.Add(a2);
            Assert.IsTrue(ReferralMilestone.IsComplete(index, chapter, cleared.Contains));

            Assert.IsFalse(ReferralMilestone.IsComplete(index, ChapterId.Parse("ch_z"), cleared.Contains),
                           "a chapter the index does not hold is never complete");
            Assert.IsFalse(ReferralMilestone.IsComplete(null, chapter, cleared.Contains));
        }

        // -------------------------------------------------------------- the state
        [Test]
        public void TheStateRoundTripsThroughItsCache()
        {
            var state = new ReferralState("ABCDEFGH", 3, 2, new[] { "rung:1:1", "rung:1:2", "rung:2:1" },
                                          true, true, false, 1700000000L);
            var back = ReferralState.FromDto(state.ToDto());

            Assert.AreEqual("ABCDEFGH", back.Code);
            Assert.AreEqual(3, back.Bound);
            Assert.AreEqual(2, back.Finished);
            Assert.IsTrue(back.HasPaid("rung:1:1") && back.HasPaid("rung:2:1") && !back.HasPaid("rung:2:2"));
            Assert.AreEqual(2, back.PaidCount(ReferralClaimKind.Rung, 1, 2));
            Assert.AreEqual(1, back.PaidCount(ReferralClaimKind.Rung, 2, 2));
            Assert.AreEqual(0, back.PaidCount(ReferralClaimKind.Invitee, 0, 2));
            Assert.IsTrue(back.Referred && back.MilestoneReached && !back.CanRedeem);
            Assert.IsTrue(back.IsKnown);
        }

        [Test]
        public void ACacheFromAnotherSchemaReadsAsNothing()
        {
            var dto = new ReferralState("ABCDEFGH", 1, 1, null, false, false, true, 5L).ToDto();
            dto.schema = ReferralState.Schema + 1;

            var back = ReferralState.FromDto(dto);
            Assert.IsFalse(back.IsKnown);
            Assert.AreEqual(string.Empty, back.Code);
        }

        // --------------------------------------------------- the state's own equality
        //
        // `Matches` is what stops the invite page redrawing itself under the player: the page
        // reads the server on every visit, the answer is almost always what the device already
        // had, and the ledger raises a change only when this says one happened. So a false
        // "different" is the whole bug back, and a false "same" is a page that has stopped
        // listening. Both directions are held here.

        static ReferralState State(string[] paid, long fetched = 1700000000L,
                                   string code = "ABCDEFGH", int bound = 3, int finished = 2,
                                   bool referred = true, bool milestone = true, bool canRedeem = false)
            => new ReferralState(code, bound, finished, paid, referred, milestone, canRedeem, fetched);

        [Test]
        public void ARereadThatBringsBackTheSameAnswerMatches()
        {
            var paid = new[] { "rung:1:1", "rung:1:2" };

            // A different array, a different instance and — the point — a later fetch stamp,
            // which is the one field that moves on every single read.
            Assert.IsTrue(State(paid).Matches(State(new[] { "rung:1:1", "rung:1:2" }, 1700009999L)));
        }

        [Test]
        public void AReorderedPaidListIsTheSameAnswer()
        {
            // The server filters rather than sorts (`referral.ts`), so nothing promises the
            // order. Comparing position by position would report a reshuffle as a change.
            Assert.IsTrue(State(new[] { "rung:1:1", "rung:2:1", "invitee:1" })
                          .Matches(State(new[] { "invitee:1", "rung:1:1", "rung:2:1" })));
        }

        [Test]
        public void AChestThatHasBeenPaidIsADifferentAnswer()
        {
            Assert.IsFalse(State(new[] { "rung:1:1" }).Matches(State(new[] { "rung:1:1", "rung:1:2" })));
            Assert.IsFalse(State(new[] { "rung:1:1", "rung:1:2" }).Matches(State(new[] { "rung:1:1" })));
        }

        [Test]
        public void ASwappedChestIsADifferentAnswer()
        {
            // Equal lengths and every entry accounted for on one side only. Caught by the
            // second walk; with one direction alone this reads as a match.
            Assert.IsFalse(State(new[] { "rung:1:1", "rung:1:1" })
                           .Matches(State(new[] { "rung:1:1", "rung:1:2" })));
        }

        [Test]
        public void EveryFieldAPageDrawsIsCompared()
        {
            var paid = new[] { "rung:1:1" };
            var baseline = State(paid);

            // One per field, so a field added to the state and forgotten here fails rather
            // than silently stopping the page from noticing it.
            Assert.IsFalse(baseline.Matches(State(paid, code: "ZZZZZZZZ")), "code");
            Assert.IsFalse(baseline.Matches(State(paid, bound: 4)), "bound");
            Assert.IsFalse(baseline.Matches(State(paid, finished: 3)), "finished");
            Assert.IsFalse(baseline.Matches(State(paid, referred: false)), "referred");
            Assert.IsFalse(baseline.Matches(State(paid, milestone: false)), "milestoneReached");
            Assert.IsFalse(baseline.Matches(State(paid, canRedeem: true)), "canRedeem");
        }

        [Test]
        public void NothingMatchesNothingAndEverythingMatchesItself()
        {
            Assert.IsTrue(ReferralState.Empty.Matches(ReferralState.Empty));
            Assert.IsFalse(ReferralState.Empty.Matches(null));

            var state = State(new[] { "invitee:1" });
            Assert.IsTrue(state.Matches(state));
        }

        // ------------------------------------------------- what the ledger does with an answer
        //
        // Four rules, each pulled out of the method that used to bury it so it can be asked
        // directly. Three of them decide something with no undo — whether a page redraws itself
        // under the player, whether one account's state is written over another's, and whose
        // wallet a chest is paid into — and none of them was reachable while it was four
        // operators inside a property.

        [Test]
        public void AnAnswerThatRepeatsWhatIsHeldSaysNothingNew()
        {
            var held = State(new[] { "rung:1:1" });
            var again = State(new[] { "rung:1:1" }, 1700009999L);

            Assert.IsFalse(ReferralLedger.SaysSomethingNew(held, again),
                           "the common case: the page must not be told a change happened");
        }

        [Test]
        public void AnAnswerThatMovesAnythingSaysSomethingNew()
        {
            var held = State(new[] { "rung:1:1" });
            Assert.IsTrue(ReferralLedger.SaysSomethingNew(held, State(new[] { "rung:1:1" }, finished: 3)));
            Assert.IsTrue(ReferralLedger.SaysSomethingNew(held, State(new[] { "rung:1:1", "rung:1:2" })));
        }

        [Test]
        public void TheFirstAnswerAlwaysSaysSomethingNew()
        {
            // It says exactly what `Empty` says and is still a change, because it is the one
            // that turns `IsKnown` on. Leave this clause out and a fresh account's offer row
            // never moves off the device's own guess.
            var first = new ReferralState(string.Empty, 0, 0, new string[0], false, false, false, 1700000000L);

            Assert.IsTrue(ReferralState.Empty.Matches(first), "nothing about it differs");
            Assert.IsTrue(ReferralLedger.SaysSomethingNew(ReferralState.Empty, first), "and it is still news");
        }

        [Test]
        public void ANullAnswerIsNotNews()
        {
            Assert.IsFalse(ReferralLedger.SaysSomethingNew(State(new string[0]), null));
            Assert.IsTrue(ReferralLedger.SaysSomethingNew(null, State(new string[0])));
        }

        [Test]
        public void AReadIsWantedWhileNothingHasMoved()
        {
            Assert.IsTrue(ReferralLedger.StillWanted("uid-a", 7, ordered: true, "uid-a", 7));
        }

        [Test]
        public void AReadOvertakenByAWriteIsDropped()
        {
            // The redeem landed while the read was out. The read is carrying the state from
            // before the code was typed, and adopting it would put the offer row back.
            Assert.IsFalse(ReferralLedger.StillWanted("uid-a", 7, ordered: true, "uid-a", 8));
        }

        [Test]
        public void AWriteIsNeverDroppedByAReadThatLandedFirst()
        {
            // The other order, and the one that cost a redeem: redeem out, read out, read back
            // (bumping the generation), redeem back. A write is the freshest word there is
            // about this account — the server has just acted on it — so nothing that merely
            // *asked* a question may discard it. Dropped here, the panel closes, the toast says
            // welcome, and the page goes on offering to type a code.
            Assert.IsTrue(ReferralLedger.StillWanted("uid-a", 7, ordered: false, "uid-a", 8));
        }

        [Test]
        public void NeitherKindSurvivesAnAccountSwitch()
        {
            // The switch is local and instant (invariant 17a); the call was not. Adopting here
            // caches one player's code and counts under the other's key.
            foreach (bool ordered in new[] { true, false })
            {
                Assert.IsFalse(ReferralLedger.StillWanted("uid-a", 7, ordered, "uid-b", 7), "another account");
                Assert.IsFalse(ReferralLedger.StillWanted("uid-a", 7, ordered, string.Empty, 7), "signed out");
                Assert.IsFalse(ReferralLedger.StillWanted("uid-a", 7, ordered, null, 7), "signed out, as null");
            }
        }

        [Test]
        public void AnOpenClaimIsAlwaysWanted()
        {
            // `Claim.Open`, for the one caller that has already proved the answer is wanted:
            // a payout that checked its own owner before it banked a thing.
            Assert.IsTrue(ReferralLedger.StillWanted(null, 0, ordered: true, "uid-b", 99));
            Assert.IsTrue(ReferralLedger.StillWanted(null, 0, ordered: false, "uid-b", 99));
        }

        [Test]
        public void AChestIsPaidOnlyIntoTheWalletItWasRolledFor()
        {
            Assert.IsTrue(ReferralLanding.PaysInto("uid-a", "uid-a"));
            Assert.IsFalse(ReferralLanding.PaysInto("uid-a", "uid-b"));

            // Signed out reads as empty in one place and null in the other, and they are the
            // same account — nobody.
            Assert.IsTrue(ReferralLanding.PaysInto(string.Empty, null));
            Assert.IsFalse(ReferralLanding.PaysInto("uid-a", null));
            Assert.IsFalse(ReferralLanding.PaysInto(null, "uid-b"));
        }

        [Test]
        public void TheInFlightCeilingIsEverySubjectTheTableCanMint()
        {
            var problems = new List<string>();
            var table = Resolve(Shipped(), problems);

            // 50 friends x 2 chests each, plus the invitee's own 2.
            Assert.AreEqual(50 * 2 + 2, ReferralLedger.NotesCeiling(table));
        }

        [Test]
        public void TheInFlightCeilingNeverFallsToNothing()
        {
            // A withdrawn or unreadable table must not bound the list at nought and evict a
            // note that is still owed — the note is what makes a lost reply bank on the retry.
            var withdrawn = Resolve(new ReferralDto { maxBound = 0 }, new List<string>());

            Assert.GreaterOrEqual(ReferralLedger.NotesCeiling(withdrawn), 16);
            Assert.GreaterOrEqual(ReferralLedger.NotesCeiling(null), 16);
        }

        [Test]
        public void AFirstAnswerIsNeverMistakenForNothing()
        {
            // The one case `Matches` cannot carry on its own, and the reason `Adopt` tests
            // `IsKnown` beside it: the server's first reply to a brand new account says
            // exactly what `Empty` says, and the page still has to hear about it — that flip
            // is what moves the offer row off the device's guess and onto the server's word.
            var first = new ReferralState(string.Empty, 0, 0, new string[0], false, false, false, 1700000000L);

            Assert.IsTrue(ReferralState.Empty.Matches(first), "the answer itself says nothing new");
            Assert.IsFalse(ReferralState.Empty.IsKnown);
            Assert.IsTrue(first.IsKnown, "but it is the first one that is known, and that is the change");
        }

        // ------------------------------------------------------------- plumbing
        [System.Serializable]
        sealed class Vectors
        {
            public string alphabet;
            public int length;
            public Case[] cases;
        }

        [System.Serializable]
        sealed class Case
        {
            public string typed;
            public string folded;
            public bool valid;
        }

        static string RepoPath(params string[] parts)
        {
            string root = null;
            try { root = Path.GetFullPath(Path.Combine(Application.dataPath, "..")); }
            catch (System.Exception) { }

            if (root == null || !Directory.Exists(Path.Combine(root, "firebase")))
            {
                string dir = Directory.GetCurrentDirectory();
                while (!string.IsNullOrEmpty(dir) && !Directory.Exists(Path.Combine(dir, "firebase")))
                    dir = Path.GetDirectoryName(dir);
                root = dir;
            }

            var path = new List<string> { root };
            path.AddRange(parts);
            return Path.GetFullPath(Path.Combine(path.ToArray()));
        }
    }
}
