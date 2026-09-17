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
