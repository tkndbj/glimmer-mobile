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

            // Derived from the roster rather than typed. The ladder is content: it was retuned
            // to gate its last two companions at 61 and 66, at which point a hard-coded 60 made
            // this permanently red — and a suite with a standing failure in it is a suite
            // nobody reads. Whatever the top gate becomes, the claim stays the same one.
            //
            // What the claim is stopped being "every companion is reachable" when the rule
            // became keeper level AND purchase: levelling alone now reaches only the ones the
            // roster puts no price on. The monotonicity is the part worth pinning either way —
            // a count that goes down as a player levels up is the shape of bug this catches.
            int top = 0;
            foreach (var avatar in AvatarCatalog.All)
                if (avatar.UnlockLevel > top) top = avatar.UnlockLevel;

            int previous = 0;
            for (int level = 0; level <= top; level++)
            {
                int count = CompanionLedger.HeldCount(level);
                Assert.GreaterOrEqual(count, previous, "unlocking must never go backwards");
                Assert.LessOrEqual(count, AvatarCatalog.All.Count);
                previous = count;
            }

            int free = 0;
            foreach (var avatar in AvatarCatalog.All)
                if (!avatar.IsForSale) free++;

            Assert.AreEqual(free, previous,
                            "levelling to the top gate reaches exactly the unpriced companions");
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

        /// <summary>
        /// The bug this file's whole preference section now exists to keep out.
        ///
        /// <para>
        /// The merge reads recency off <c>updatedUnix</c>, and
        /// <c>SaveService.Snapshot</c> stamps that with the current moment every time the
        /// cloud sync asks for one. So the local side was newer in every comparison it
        /// ever took part in, and "the newest choice wins" quietly meant "this device
        /// wins": a phone that had never been renamed pushed its default over a name
        /// chosen on a tablet, and a fresh install overwrote the name it had just
        /// downloaded. Per-field stamps are what make the comparison mean what it says.
        /// </para>
        /// </summary>
        [Test]
        public void AFilesOwnDateNoLongerDecidesWhoseNameSurvives()
        {
            // The rename is old. The file carrying it was written long ago.
            var renamed = File(updatedUnix: 100, name: "Fern");

            // The other device renamed *earlier still*, but its file is a fresh snapshot —
            // which is what every local side looks like, every sync, for ever.
            var stale = File(updatedUnix: 9_000_000, name: "Bracken");
            stale.wallet.displayNameSetUnix = 50;

            foreach (var merged in new[] { SaveMerge.Join(renamed, stale), SaveMerge.Join(stale, renamed) })
            {
                Assert.AreEqual("Fern", merged.wallet.displayName,
                                "the later *choice* wins, not the later snapshot");
                Assert.AreEqual(100, merged.wallet.displayNameSetUnix);
            }
        }

        /// <summary>
        /// The shape of the live report: rename on a phone, then install on a tablet.
        ///
        /// A device that has never been renamed stores nothing at all, so it cannot be
        /// mistaken for one that chose the default. Storing <c>DefaultName</c> is what
        /// used to make the two indistinguishable, and the tablet then won on recency and
        /// pushed "Grovekeeper" back over the server's copy.
        /// </summary>
        [Test]
        public void AFreshInstallDoesNotOverwriteTheNameOnTheServer()
        {
            var server = File(updatedUnix: 1000, name: "Fern");

            var fresh = File(updatedUnix: 9_000_000);
            Assert.AreEqual(string.Empty, fresh.wallet.displayName,
                            "an unnamed keeper stores nothing — DefaultName is shown, never written");

            foreach (var merged in new[] { SaveMerge.Join(fresh, server), SaveMerge.Join(server, fresh) })
                Assert.AreEqual("Fern", merged.wallet.displayName);
        }

        /// <summary>
        /// A v14 file carries a real name and no stamp, which reads as the oldest possible
        /// choice — so it survives against a device that never chose, and yields to any
        /// rename made since. Nothing has to detect the upgrade.
        /// </summary>
        [Test]
        public void AnUndatedNameOutranksNoneAndYieldsToADatedOne()
        {
            var legacy = File(updatedUnix: 500, name: "Bracken");
            legacy.wallet.displayNameSetUnix = 0;

            var never = File(updatedUnix: 800);
            Assert.AreEqual("Bracken", SaveMerge.Join(never, legacy).wallet.displayName);
            Assert.AreEqual("Bracken", SaveMerge.Join(legacy, never).wallet.displayName);

            var renamed = File(updatedUnix: 200, name: "Fern");   // stamped, and much older
            Assert.AreEqual("Fern", SaveMerge.Join(legacy, renamed).wallet.displayName);
            Assert.AreEqual("Fern", SaveMerge.Join(renamed, legacy).wallet.displayName);
        }

        /// <summary>
        /// Two names dated the same second — two devices renamed at once, or, far more
        /// likely, two files that both predate the stamps. The answer is arbitrary and
        /// must be <em>stable</em>: an order-dependent one would leave the two devices
        /// pushing over each other for ever.
        /// </summary>
        [Test]
        public void ATiedRenameResolvesTheSameWayOnBothDevices()
        {
            var a = File(updatedUnix: 100, name: "Fern");
            var b = File(updatedUnix: 100, name: "Bracken");

            Assert.AreEqual(SaveMerge.Join(a, b).wallet.displayName,
                            SaveMerge.Join(b, a).wallet.displayName,
                            "the answer cannot depend on which device is running the merge");

            var once = SaveMerge.Join(a, b);
            Assert.AreEqual(once.wallet.displayName, SaveMerge.Join(once, a).wallet.displayName);
            Assert.AreEqual(once.wallet.displayName, SaveMerge.Join(once, b).wallet.displayName);
        }

        /// <summary>
        /// A rename has to be sent, and it has to be sent with its date — a name that
        /// travelled without one would be re-dated by whichever device asked last, which
        /// is the whole failure schema v15 removes.
        /// </summary>
        [Test]
        public void RenamingIsWorthASyncAndTheStampGoesWithIt()
        {
            var remote = File(updatedUnix: 100, name: "Bracken");
            var local = File(updatedUnix: 200, name: "Fern");

            Assert.IsTrue(SaveDelta.Between(remote, local).ScalarsChanged,
                          "a name the server has not seen must be sent");

            var restored = FirestoreSaveMapper.FromDocument(FirestoreSaveMapper.ToDocument(local));
            Assert.AreEqual("Fern", restored.wallet.displayName);
            Assert.AreEqual(200, restored.wallet.displayNameSetUnix);
            Assert.IsFalse(SaveDelta.Between(restored, local).ScalarsChanged,
                           "and once it has landed, the next sync writes nothing");
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

            // Same companion, chosen at the same moment: the file's own date has moved on
            // — a snapshot is stamped with now — and that must still send nothing, which
            // is the whole reason SaveDelta compares fields rather than timestamps.
            var unchanged = File(updatedUnix: 300, avatar: "timber");
            unchanged.wallet.avatarSetUnix = remote.wallet.avatarSetUnix;

            Assert.IsFalse(SaveDelta.Between(remote, unchanged).ScalarsChanged,
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

        // --------------------------------------------------------------- wallet
        /// <summary>
        /// The half of the fix that lives in the wallet: <c>DefaultName</c> is what an
        /// unnamed keeper is <em>shown</em>, and is never what is stored.
        ///
        /// Writing it down is what made a device with no opinion indistinguishable from
        /// one that had chosen — after which no merge rule could have been right, because
        /// the information it needed had already been thrown away.
        /// </summary>
        [Test]
        public void AnUnnamedKeeperStoresNothingAndIsStillCalledSomething()
        {
            Wallet.LoadFrom(new SaveFileDto { wallet = WalletDto.Unwritten() });

            Assert.AreEqual(Wallet.DefaultName, Wallet.DisplayName, "there is always something to draw");
            Assert.IsFalse(Wallet.HasChosenName);

            var written = new SaveFileDto();
            Wallet.WriteInto(written);

            Assert.AreEqual(string.Empty, written.wallet.displayName,
                            "storing the default is what used to overwrite a real name on the server");
            Assert.AreEqual(0, written.wallet.displayNameSetUnix);

            Wallet.SetDisplayName("Fern", 4242);
            Wallet.WriteInto(written);

            Assert.AreEqual("Fern", written.wallet.displayName);
            Assert.AreEqual(4242, written.wallet.displayNameSetUnix, "a choice is dated when it is made");
            Assert.IsTrue(Wallet.HasChosenName);

            Wallet.LoadFrom(new SaveFileDto { wallet = WalletDto.Unwritten() });
        }

        /// <summary>
        /// A pre-v15 file holding the default name and no stamp is ambiguous — never
        /// chosen, or chosen and it happened to be the default — and reading it as never
        /// chosen is the safe half: the player still sees Grovekeeper, and a device that
        /// was never renamed stops outranking one that was.
        /// </summary>
        [Test]
        public void ADefaultNameFromAnOlderBuildIsReadAsNeverChosen()
        {
            Wallet.LoadFrom(new SaveFileDto
            {
                schemaVersion = 14,
                wallet = new WalletDto { coins = -1, gems = -1, hearts = -1, displayName = Wallet.DefaultName },
            });

            Assert.AreEqual(Wallet.DefaultName, Wallet.DisplayName);
            Assert.IsFalse(Wallet.HasChosenName, "an unstamped default cannot be told from silence");

            // A stamped one is believed exactly as written, default or not.
            Wallet.LoadFrom(new SaveFileDto
            {
                wallet = new WalletDto
                {
                    coins = -1, gems = -1, hearts = -1,
                    displayName = Wallet.DefaultName, displayNameSetUnix = 99,
                },
            });

            Assert.IsTrue(Wallet.HasChosenName);

            Wallet.LoadFrom(new SaveFileDto { wallet = WalletDto.Unwritten() });
        }

        // ------------------------------------------------------------ fixture
        /// <summary>
        /// A save whose preferences were chosen at <paramref name="updatedUnix"/>.
        ///
        /// The two dates are the same here purely because it reads well; they are not the
        /// same thing, and the whole of schema v15 is that they must not be conflated.
        /// The file's date moves every time a snapshot is taken, and a choice's does not.
        /// <see cref="AFilesOwnDateNoLongerDecidesWhoseNameSurvives"/> pulls them apart.
        /// </summary>
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
                    displayNameSetUnix = string.IsNullOrEmpty(name) ? 0L : updatedUnix,
                    avatarId = avatar ?? string.Empty,
                    avatarSetUnix = string.IsNullOrEmpty(avatar) ? 0L : updatedUnix,
                },
                levels = new LevelRecordDto[0],
                progression = ProgressionStateDto.Unwritten(),
            };
    }
}
