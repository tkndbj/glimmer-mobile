using System.Collections.Generic;
using GlimmerGrove.Cloud;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The profile: the companion roster, the honorific, and the parts of the save that
    /// carry a player's choices rather than their progress.
    ///
    /// Preferences are the one place the merge is not a join on value, which makes them
    /// the one place it can quietly lose something. Most of this file is about that.
    /// </summary>
    public sealed class ProfileTests
    {
        /// <summary>
        /// The roster is a process-wide static, so a test that publishes one would
        /// otherwise leave it published for whatever runs next — and these tests would
        /// pass or fail on their order. Snapshot and restore makes each independent.
        /// </summary>
        AvatarDefinition[] _rosterBefore;
        bool _wasFromContent;

        [SetUp]
        public void SnapshotRoster()
        {
            _rosterBefore = new AvatarDefinition[AvatarCatalog.All.Count];
            for (int i = 0; i < _rosterBefore.Length; i++) _rosterBefore[i] = AvatarCatalog.All[i];
            _wasFromContent = AvatarCatalog.IsFromContent;
        }

        [TearDown]
        public void RestoreRoster()
            => AvatarCatalog.Publish(_wasFromContent ? _rosterBefore : null);

        // ------------------------------------------------------------- roster
        [Test]
        public void AvatarIdsAndArtKeysAreUnique()
        {
            var ids = new HashSet<string>();
            var arts = new HashSet<string>();

            foreach (var avatar in AvatarCatalog.All)
            {
                Assert.IsTrue(ids.Add(avatar.Id), $"duplicate avatar id '{avatar.Id}'");
                Assert.IsTrue(arts.Add(avatar.Portrait), $"two companions share art '{avatar.Portrait}'");
                Assert.IsFalse(string.IsNullOrEmpty(avatar.NameKey), $"'{avatar.Id}' has no name key");
            }
        }

        [Test]
        public void TheDefaultCompanionCostsNothing()
            => Assert.AreEqual(0, AvatarCatalog.Default.UnlockLevel,
                               "a brand-new player must already own the one they are wearing");

        [Test]
        public void AnUnknownCompanionFallsBackInsteadOfDrawingNothing()
        {
            // A save from a build one drop ahead, or a rollback, names one this build
            // has never heard of. It must still render something.
            Assert.AreEqual(AvatarCatalog.Default.Id, AvatarCatalog.Resolve("wisp_from_the_future").Id);
            Assert.AreEqual(AvatarCatalog.Default.Id, AvatarCatalog.Resolve(null).Id);
            Assert.AreEqual(AvatarCatalog.Default.Id, AvatarCatalog.Resolve(string.Empty).Id);
        }

        [Test]
        public void ResolveKeepsACompanionARetuneWouldHaveTakenAway()
        {
            // Resolve is deliberately not an unlock check: somebody who earned a
            // companion and was then caught by a rebalance keeps wearing it.
            var late = LastLockedCompanion();
            if (!late.IsValid) Assert.Ignore("the roster has no locked companion to test with");

            Assert.IsFalse(AvatarCatalog.ReachedBy(late, 0));
            Assert.AreEqual(late.Id, AvatarCatalog.Resolve(late.Id).Id);
        }

        [Test]
        public void HeldCountRisesWithLevelAndNeverExceedsTheRoster()
        {
            CompanionLedger.ResetForTests();

            int previous = 0;
            for (int level = 0; level <= 60; level++)
            {
                int count = CompanionLedger.HeldCount(level);
                Assert.GreaterOrEqual(count, previous, "unlocking must never go backwards");
                Assert.LessOrEqual(count, AvatarCatalog.All.Count);
                previous = count;
            }
            Assert.AreEqual(AvatarCatalog.All.Count, previous, "every companion is reachable");
        }

        static AvatarDefinition LastLockedCompanion()
        {
            var found = default(AvatarDefinition);
            foreach (var avatar in AvatarCatalog.All)
                if (avatar.UnlockLevel > 0) found = avatar;
            return found;
        }

        // ------------------------------------------------------ roster as content
        [Test]
        public void AnEmptyPortraitFallsBackToTheId()
        {
            var avatar = new AvatarDefinition("wisp", string.Empty, string.Empty, 3);

            Assert.AreEqual("wisp", avatar.Portrait, "authors leave it blank when it matches");
            Assert.IsFalse(avatar.HasAnimation);
            Assert.AreEqual("ui.avatar.wisp", avatar.NameKey);
        }

        [Test]
        public void TheBuilderRejectsIdsASaveFileCouldNotHold()
        {
            var builder = new CatalogIndexBuilder();

            Assert.IsFalse(builder.AddCompanion(new ManifestCompanionDto { id = "Has Space" }));
            Assert.IsFalse(builder.AddCompanion(new ManifestCompanionDto { id = "dots.bad" }));
            Assert.IsFalse(builder.AddCompanion(new ManifestCompanionDto { id = "" }));
            Assert.IsTrue(builder.AddCompanion(new ManifestCompanionDto { id = "good_one" }));

            Assert.IsTrue(builder.HasProblems, "a rejected companion is reported, not swallowed");
            Assert.AreEqual(1, builder.Build().Companions.Count);
        }

        [Test]
        public void ADuplicateOrDisabledCompanionIsDropped()
        {
            var builder = new CatalogIndexBuilder();

            Assert.IsTrue(builder.AddCompanion(new ManifestCompanionDto { id = "puff" }));
            Assert.IsFalse(builder.AddCompanion(new ManifestCompanionDto { id = "puff" }));
            Assert.IsFalse(builder.AddCompanion(new ManifestCompanionDto { id = "gone", disabled = true }));

            Assert.AreEqual(1, builder.Build().Companions.Count);
        }

        [Test]
        public void CompanionsComeOutInUnlockOrderAndTiesKeepManifestOrder()
        {
            var builder = new CatalogIndexBuilder();
            builder.AddCompanion(new ManifestCompanionDto { id = "late", unlockLevel = 9 });
            builder.AddCompanion(new ManifestCompanionDto { id = "first", unlockLevel = 0 });
            builder.AddCompanion(new ManifestCompanionDto { id = "tie_a", unlockLevel = 4 });
            builder.AddCompanion(new ManifestCompanionDto { id = "tie_b", unlockLevel = 4 });

            var order = new List<string>();
            foreach (var c in builder.Build().Companions) order.Add(c.Id);

            Assert.AreEqual(new[] { "first", "tie_a", "tie_b", "late" }, order.ToArray(),
                            "ties must not reshuffle the picker between runs");
        }

        [Test]
        public void AManifestWithNoRosterLeavesTheBuiltInOneStanding()
        {
            // Asserted against the contract rather than a count: in the Editor the
            // catalog has already published the roster from the manifest, so "what it
            // held a moment ago" is not the built-in list and pinning a number here
            // would make this test pass or fail on what ran before it.
            AvatarCatalog.Publish(new List<AvatarDefinition>());

            Assert.IsFalse(AvatarCatalog.IsFromContent, "an empty roster is not a content roster");
            Assert.Greater(AvatarCatalog.All.Count, 0, "an empty roster must not empty the game");
            Assert.AreEqual(0, AvatarCatalog.Default.UnlockLevel, "and the fallback must be wearable");

            AvatarCatalog.Publish(new List<AvatarDefinition>
            {
                new AvatarDefinition("only", "only", string.Empty, 0),
            });

            Assert.AreEqual(1, AvatarCatalog.All.Count);
            Assert.IsTrue(AvatarCatalog.IsFromContent);
            Assert.AreEqual("only", AvatarCatalog.Default.Id);
        }

        [Test]
        public void NextUnheldNamesTheNearestOneAndNothingAtTheEnd()
        {
            AvatarCatalog.Publish(new List<AvatarDefinition>
            {
                new AvatarDefinition("a", "a", string.Empty, 0),
                new AvatarDefinition("c", "c", string.Empty, 9),
                new AvatarDefinition("b", "b", string.Empty, 4),
            });

            CompanionLedger.ResetForTests();

            Assert.AreEqual("b", CompanionLedger.NextUnheld(0).Id);
            Assert.AreEqual("c", CompanionLedger.NextUnheld(4).Id);
            Assert.IsFalse(CompanionLedger.NextUnheld(99).IsValid, "nothing left to chase");
        }

        // ---------------------------------------------------------- honorific
        [Test]
        public void EveryLevelHasATitleAndTheTiersNeverGoBackwards()
        {
            string previous = null;
            var seen = new List<string>();

            for (int level = 0; level <= 80; level++)
            {
                string key = KeeperTitle.KeyFor(level);
                Assert.IsFalse(string.IsNullOrEmpty(key), $"level {level} has no title");

                if (key != previous)
                {
                    Assert.IsFalse(seen.Contains(key), $"title '{key}' came back at level {level}");
                    seen.Add(key);
                    previous = key;
                }
            }
        }

        [Test]
        public void TheNextTierIsAlwaysAheadOrAbsent()
        {
            for (int level = 0; level <= 80; level++)
            {
                int next = KeeperTitle.NextTierLevel(level);
                if (next == 0) continue;

                Assert.Greater(next, level, "a promotion the player already has is not news");
                Assert.AreNotEqual(KeeperTitle.KeyFor(level), KeeperTitle.KeyFor(next),
                                   "the next tier must actually be a different title");
            }
        }

        // -------------------------------------------------------------- merge
        [Test]
        public void TheMoreRecentCompanionChoiceWins()
        {
            var older = File(updatedUnix: 100, avatar: "sprocket");
            var newer = File(updatedUnix: 500, avatar: "monarch");

            Assert.AreEqual("monarch", SaveMerge.Join(older, newer).wallet.avatarId);
            Assert.AreEqual("monarch", SaveMerge.Join(newer, older).wallet.avatarId,
                            "the answer cannot depend on which device is running the merge");
        }

        [Test]
        public void ADeviceThatNeverChoseDoesNotEraseOneThatDid()
        {
            // The case that matters: a second device installed later has no companion
            // and no name, and its save is the newer one. Letting "" win would silently
            // undo a choice made on the first device.
            var chose = File(updatedUnix: 100, avatar: "thistle", name: "Fern");
            var fresh = File(updatedUnix: 900);

            foreach (var merged in new[] { SaveMerge.Join(chose, fresh), SaveMerge.Join(fresh, chose) })
            {
                Assert.AreEqual("thistle", merged.wallet.avatarId);
                Assert.AreEqual("Fern", merged.wallet.displayName);
            }
        }

        [Test]
        public void MergingIsIdempotent()
        {
            var mine = File(updatedUnix: 100, avatar: "timber", name: "Bracken");
            var theirs = File(updatedUnix: 500, avatar: "sprocket");

            var once = SaveMerge.Join(mine, theirs);
            var twice = SaveMerge.Join(once, theirs);

            Assert.AreEqual(once.wallet.avatarId, twice.wallet.avatarId);
            Assert.AreEqual(once.wallet.displayName, twice.wallet.displayName);
        }

        // --------------------------------------------------------------- sync
        [Test]
        public void ChangingCompanionIsWorthASync()
        {
            var remote = File(updatedUnix: 100, avatar: "timber");
            var local = File(updatedUnix: 200, avatar: "sprocket");

            Assert.IsTrue(SaveDelta.Between(remote, local).ScalarsChanged,
                          "a choice the server has not seen must be sent");
            Assert.IsFalse(SaveDelta.Between(remote, File(updatedUnix: 300, avatar: "timber")).ScalarsChanged,
                           "an unchanged save still sends nothing");
        }

        [Test]
        public void TheCompanionSurvivesTheFirestoreRoundTrip()
        {
            var original = File(updatedUnix: 1700000000, avatar: "monarch", name: "Fern");

            var restored = FirestoreSaveMapper.FromDocument(FirestoreSaveMapper.ToDocument(original));

            Assert.AreEqual("monarch", restored.wallet.avatarId);
            Assert.AreEqual("Fern", restored.wallet.displayName);
        }

        // ------------------------------------------------------------ fixture
        static SaveFileDto File(long updatedUnix, string avatar = null, string name = null)
            => new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                updatedUnix = updatedUnix,
                settings = new SettingsDto(),
                wallet = new WalletDto
                {
                    coins = -1, gems = -1, hearts = -1,
                    displayName = name ?? string.Empty,
                    avatarId = avatar ?? string.Empty,
                },
                levels = new LevelRecordDto[0],
                progression = ProgressionStateDto.Unwritten(),
            };
    }
}
