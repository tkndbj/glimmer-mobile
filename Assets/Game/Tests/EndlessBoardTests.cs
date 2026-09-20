using System;
using GlimmerGrove.Cloud;
using GlimmerGrove.Content;
using GlimmerGrove.Homestead;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Social;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The chain that carries an endless run to a public board, joint by joint.
    ///
    /// <para>
    /// <b>Every link here fails silently if it breaks</b>, which is why they are pinned
    /// together rather than one per fixture. A run reaches the Endless Watch only if the
    /// ledger records it, the record asks for a sync, the sync's receipt builds a card
    /// carrying the wave, the wave reaches the card's <em>fingerprint</em> so a publish is
    /// judged owed, and the publish gate lets a keeper with nothing else through. Break any
    /// one and nothing throws, nothing logs and no gate goes red — a player simply holds out
    /// further than anybody alive and never appears on the list they did it for. That is
    /// invariant 19j's fault arriving through a field instead of a stale read.
    /// </para>
    /// <para>
    /// The server half of the same rule is <c>bestWave</c> in <c>functions/src/grove.ts</c>,
    /// driven by <c>firebase/functions/test/grove.mjs</c>. The two have to read a save the same
    /// way and bound it the same way, or the number a device draws and the number on the board
    /// disagree for the one account that reaches the ceiling.
    /// </para>
    /// </summary>
    public sealed class EndlessBoardTests
    {
        sealed class NoProgress : IHomesteadProgress
        {
            public bool IsCleared(LevelId level) => false;
            public bool IsChapterFinished(ChapterId chapter) => false;
        }

        static readonly LevelId Watch = LevelId.Parse("s02_endlesswatch");
        static readonly LevelId Other = LevelId.Parse("s09_elsewhere");

        [SetUp]
        public void Reset()
        {
            GroveRanks.Clear();
            HomesteadProgress.Set(new NoProgress());
            HomesteadLayout.ResetForTests();
            HomesteadLedger.ResetForTests();
            GroveLand.ResetForTests();

            // No `ResetForTests` of its own: reading an empty file is what clears this ledger
            // in the game too, so the test uses the door the game uses.
            EndlessLedger.LoadFrom(new SaveFileDto());

            CloudSaveService.ForgetSyncRequestForTests();
            SyncTriggers.Attach();
        }

        [TearDown]
        public void Restore()
        {
            GroveRanks.Clear();
            HomesteadProgress.Set(null);
            HomesteadLayout.ResetForTests();
            HomesteadLedger.ResetForTests();
            GroveLand.ResetForTests();
            EndlessLedger.LoadFrom(new SaveFileDto());
            CloudSaveService.ForgetSyncRequestForTests();
        }

        static SaveFileDto Saved(params (string level, int wave)[] rows)
        {
            var dto = new SaveFileDto { endlessBest = new EndlessBestDto[rows.Length] };

            for (int i = 0; i < rows.Length; i++)
                dto.endlessBest[i] = new EndlessBestDto { level = rows[i].level, wave = rows[i].wave };

            return dto;
        }

        // ------------------------------------------------------------- the reading
        [Test]
        public void TheLanesBestIsTheBestOfEveryRow()
        {
            EndlessLedger.Record(Watch, 12);
            EndlessLedger.Record(Other, 31);

            // The board is about the lane rather than about one level (invariant 43), so a
            // second Infinite level joining the ladder must not need a new board id or a new
            // field in the save.
            Assert.AreEqual(31, EndlessLedger.Best);
            Assert.AreEqual(12, EndlessLedger.BestFor(Watch));
        }

        [Test]
        public void AKeeperWhoHasNeverPlayedTheLaneReadsAsNought()
        {
            Assert.AreEqual(0, EndlessLedger.Best);
            Assert.AreEqual(0, EndlessLedger.BestIn(null));
            Assert.AreEqual(0, EndlessLedger.BestIn(new SaveFileDto()));

            // Nought is what keeps the field off the card, which is what keeps every card in
            // the game out of the endless board's index. It has to be a plain nought and not
            // merely "absent", because the server writes the field only when it is positive.
            Assert.AreEqual(0, EndlessLedger.BestIn(Saved(("", 400), ("s02_endlesswatch", 0))));
        }

        [Test]
        public void TheSaveIsReadTheWayTheServerReadsIt()
        {
            // Mirrors `bestWave` in functions/src/grove.ts. A publish is judged on the file
            // the server holds, so this is the reading that decides whether one is owed —
            // and if the two sides disagree, the device asks for a publish the server's card
            // will not match, for ever.
            Assert.AreEqual(31, EndlessLedger.BestIn(
                Saved(("s02_endlesswatch", 12), ("s09_elsewhere", 31), ("s10_lower", 4))));

            Assert.AreEqual(3, EndlessLedger.BestIn(Saved(("", 900), ("a", 3))));
            Assert.AreEqual(0, EndlessLedger.BestIn(Saved(("a", -9))));
        }

        [Test]
        public void TheWaveIsBoundedOnBothSidesOfThePublish()
        {
            // Not a clamp on a derivation — there is nothing to derive it from — but the one
            // defence a public number has when the server cannot recompute it. The ceiling
            // must be the same on the card, in the ledger and in `MAX_WAVE`.
            Assert.AreEqual(EndlessLedger.MaxWave, EndlessLedger.BestIn(Saved(("a", 1000000))));

            EndlessLedger.Record(Watch, int.MaxValue);
            Assert.AreEqual(EndlessLedger.MaxWave, EndlessLedger.Best);

            var card = new GroveCard("uid", "Fern", "coral", 4, 0L, 0, int.MaxValue, 1L,
                                     string.Empty, null, null);
            Assert.AreEqual(EndlessLedger.MaxWave, card.BestWave);
        }

        // ---------------------------------------------------------------- the chain
        [Test]
        public void ANewBestAsksForASync()
        {
            Assert.IsFalse(CloudSaveService.IsSyncPending);

            Assert.IsTrue(EndlessLedger.Record(Watch, 9));
            Assert.IsTrue(CloudSaveService.IsSyncPending,
                          "a run that beat the record never reaches the server promptly");
        }

        [Test]
        public void ARunThatBeatNothingAsksForNothing()
        {
            EndlessLedger.Record(Watch, 9);
            CloudSaveService.ForgetSyncRequestForTests();

            Assert.IsFalse(EndlessLedger.Record(Watch, 4));
            Assert.IsFalse(CloudSaveService.IsSyncPending);
        }

        [Test]
        public void ASaveBeingReadNeverAsksForASync()
        {
            // `Beaten` and never `Changed`, which is `SyncTriggers`' whole warning: a sync
            // adopts a merge by loading a save, so a request raised here is a request raised
            // every few seconds for the life of the process.
            EndlessLedger.LoadFrom(Saved(("s02_endlesswatch", 40)));

            Assert.AreEqual(40, EndlessLedger.Best);
            Assert.IsFalse(CloudSaveService.IsSyncPending);
        }

        // ------------------------------------------------------------- the publish
        [Test]
        public void TheWaveIsPartOfWhatAVisitorCanSee()
        {
            var bare = new GroveCard("uid", "Fern", "coral", 4, 0L, 0, 0, 1L,
                                     string.Empty, null, null);
            var held = new GroveCard("uid", "Fern", "coral", 4, 0L, 0, 17, 1L,
                                     string.Empty, null, null);

            // If the fingerprint does not move, the publish policy believes the card it
            // already sent is current and the new best never leaves the phone.
            Assert.AreNotEqual(bare.Fingerprint(), held.Fingerprint());
        }

        [Test]
        public void AKeeperWithNothingButAWaveIsStillWorthPublishing()
        {
            var nothing = new GroveCard("uid", "Fern", "coral", 4, 0L, 0, 0, 1L,
                                        string.Empty, null, null);
            var wave = new GroveCard("uid", "Fern", "coral", 4, 0L, 0, 17, 1L,
                                     string.Empty, null, null);
            var grove = new GroveCard("uid", "Fern", "coral", 4, GrovePublishPolicy.Worth, 0, 0, 1L,
                                      string.Empty, null, null);

            // The bar is still a bar — an account that has built nothing and played nothing
            // is a document, a write and a row in the decile sample for a keeper with nothing
            // to show — but it has two ways over it now, one per board.
            Assert.IsFalse(GrovePublishPolicy.WorthPublishing(nothing));
            Assert.IsFalse(GrovePublishPolicy.WorthPublishing(null));
            Assert.IsTrue(GrovePublishPolicy.WorthPublishing(wave));
            Assert.IsTrue(GrovePublishPolicy.WorthPublishing(grove));
        }

        /// <summary>
        /// The fourth joint, and the one that actually broke.
        ///
        /// <para>
        /// A settled sync is judged against the homestead catalog, and a receipt that arrives
        /// before the catalog is parked until it is published. For a year something always
        /// published it — the three grove screens load it on the way in — and the day those
        /// screens left the nav (the Grovement hold, 2026-09-15) no device loaded it again, so
        /// every receipt was parked for ever: no publish, no card, no row, and the boards
        /// stood at whatever the last grove visit had put on them. No test saw it because every
        /// fixture that reached <c>Consider</c> loaded a catalog first. The gate has to be the
        /// thing that asks for what it is waiting on.
        /// </para>
        /// <para>
        /// The content source is absent on the machine this runs on, so the load cannot
        /// complete; what is asserted is that it was <em>asked for</em>, which is the half that
        /// was missing.
        /// </para>
        /// </summary>
        [Test]
        public void AReceiptParkedForTheCatalogAsksForTheCatalog()
        {
            HomesteadService.ResetForTests();
            GroveBoard.Forget();
            CloudSaveService.UseBackend(new BoardsOnlyBackend());

            try
            {
                Assert.IsTrue(GroveBoard.IsAvailable, "the fixture's backend must count as available");
                Assert.IsFalse(HomesteadCatalog.IsLoaded);
                Assert.AreEqual(0, HomesteadService.LoadsAsked);

                GroveBoard.ConsiderForTests(new SyncReceipt(new SaveFileDto(), 7L, pushed: true));

                Assert.AreEqual(1, HomesteadService.LoadsAsked,
                                "a receipt held back for want of a catalog must ask for the catalog, " +
                                "or it is held back for the life of the process");
            }
            finally
            {
                CloudSaveService.UseBackend(null);
                GroveBoard.Forget();
                HomesteadService.ResetForTests();
            }
        }

        /// <summary>
        /// A backend that exists and nothing more. Every call is a fault, because the test
        /// above must never reach one: it stops at the catalog gate, and a call that got past
        /// it is the assertion failing in a different voice.
        /// </summary>
        sealed class BoardsOnlyBackend : ICloudSaveBackend, IGroveBoardBackend
        {
            static System.Threading.Tasks.Task<T> Never<T>() => throw new NotSupportedException("not reached");

            public bool IsAvailable => true;
            public CloudIdentity CurrentIdentity => new CloudIdentity("uid-boards", false);

            public System.Threading.Tasks.Task<(CloudResult result, CloudIdentity identity)> SignInAsync(System.Threading.CancellationToken c = default) => Never<(CloudResult, CloudIdentity)>();
            public System.Threading.Tasks.Task<(CloudResult result, CloudIdentity identity)> ResumeAsync(System.Threading.CancellationToken c = default) => Never<(CloudResult, CloudIdentity)>();
            public System.Threading.Tasks.Task<(CloudResult result, CloudIdentity identity)> LinkAsync(LinkCredential cr, System.Threading.CancellationToken c = default) => Never<(CloudResult, CloudIdentity)>();
            public System.Threading.Tasks.Task<(CloudResult result, CloudIdentity identity)> SignInWithCredentialAsync(LinkCredential cr, System.Threading.CancellationToken c = default) => Never<(CloudResult, CloudIdentity)>();
            public System.Threading.Tasks.Task<(CloudResult result, CloudSnapshot snapshot)> PullAsync(string u, System.Threading.CancellationToken c = default) => Never<(CloudResult, CloudSnapshot)>();
            public System.Threading.Tasks.Task<CloudResult> PushAsync(string u, SaveFileDto s, SaveDelta d, System.Threading.CancellationToken c = default) => Never<CloudResult>();
            public System.Threading.Tasks.Task<(CloudResult result, System.Collections.Generic.List<CloudWalletState> wallets)> ReadWalletAsync(string u, System.Threading.CancellationToken c = default) => Never<(CloudResult, System.Collections.Generic.List<CloudWalletState>)>();
            public System.Threading.Tasks.Task<(CloudResult result, System.Collections.Generic.List<CloudWalletState> wallets)> SubmitSpendsAsync(string u, System.Collections.Generic.IReadOnlyList<SpendEntryDto> s, System.Threading.CancellationToken c = default) => Never<(CloudResult, System.Collections.Generic.List<CloudWalletState>)>();
            public System.Threading.Tasks.Task<(CloudResult result, System.Collections.Generic.List<CloudWalletState> wallets)> SubmitAwardsAsync(string u, System.Collections.Generic.IReadOnlyList<GrantEntryDto> a, System.Threading.CancellationToken c = default) => Never<(CloudResult, System.Collections.Generic.List<CloudWalletState>)>();
            public System.Threading.Tasks.Task<(CloudResult result, System.Collections.Generic.List<CloudWalletState> wallets, CloudRedemption redemption)> RedeemPurchaseAsync(string u, PurchaseReceipt r, System.Threading.CancellationToken c = default) => Never<(CloudResult, System.Collections.Generic.List<CloudWalletState>, CloudRedemption)>();
            public System.Threading.Tasks.Task<(CloudResult result, System.Collections.Generic.Dictionary<LevelId, LevelStats> stats)> ReadGroveStatsAsync(System.Threading.CancellationToken c = default) => Never<(CloudResult, System.Collections.Generic.Dictionary<LevelId, LevelStats>)>();
            public System.Threading.Tasks.Task<(CloudResult result, Release.ReleaseRequirement requirement)> ReadReleaseAsync(string p, System.Threading.CancellationToken c = default) => Never<(CloudResult, Release.ReleaseRequirement)>();
            public System.Threading.Tasks.Task<(CloudResult result, string appleAuthorizationCode)> ReauthenticateAsync(LinkCredential cr, System.Threading.CancellationToken c = default) => Never<(CloudResult, string)>();
            public System.Threading.Tasks.Task<CloudResult> DeleteAccountAsync(string u, string code = null, System.Threading.CancellationToken c = default) => Never<CloudResult>();

            public System.Threading.Tasks.Task<(CloudResult result, GrovePublication published)> PublishGroveAsync(string u, System.Threading.CancellationToken c = default) => Never<(CloudResult, GrovePublication)>();
            public System.Threading.Tasks.Task<CloudResult> WithdrawGroveAsync(string u, System.Threading.CancellationToken c = default) => Never<CloudResult>();
            public System.Threading.Tasks.Task<(CloudResult result, string holderId)> ReadNameHolderAsync(string k, System.Threading.CancellationToken c = default) => Never<(CloudResult, string)>();
            public System.Threading.Tasks.Task<(CloudResult result, NameClaim claim)> ClaimNameAsync(string n, System.Threading.CancellationToken c = default) => Never<(CloudResult, NameClaim)>();
            public System.Threading.Tasks.Task<(CloudResult result, NameReportOutcome outcome)> ReportKeeperAsync(string k, ReportSubject s, System.Threading.CancellationToken c = default) => Never<(CloudResult, NameReportOutcome)>();
            public System.Threading.Tasks.Task<(CloudResult result, GroveCard card)> ReadGroveCardAsync(string o, System.Threading.CancellationToken c = default) => Never<(CloudResult, GroveCard)>();
            public System.Threading.Tasks.Task<(CloudResult result, LeaderboardBoard board)> ReadLeaderboardAsync(string b, System.Threading.CancellationToken c = default) => Never<(CloudResult, LeaderboardBoard)>();
            public System.Threading.Tasks.Task<(CloudResult result, GroveRankPublication published)> ReadGroveRanksAsync(System.Threading.CancellationToken c = default) => Never<(CloudResult, GroveRankPublication)>();
        }

        // ------------------------------------------------------------ the standing
        /// <summary>
        /// Nine wave counts and a sample big enough to mean them — what a night's job publishes.
        /// </summary>
        static GroveRankPublication Published(int samples, params long[] deciles)
            => new GroveRankPublication(GroveRankTable.None,
                                        new GroveRankTable(samples, deciles),
                                        null, 1L);

        [Test]
        public void AWaveStandsAgainstTheKeepersWhoHaveRunTheLane()
        {
            GroveRanks.Publish(Published(4000, 2, 4, 6, 8, 10, 12, 14, 16, 18));

            // Higher is better, exactly as grove worth is — the same table used twice rather
            // than copied or given a flag.
            Assert.AreEqual(GroveRankTable.MinRank, GroveRanks.Waves.TopPercent(400));
            Assert.AreEqual(GroveRankTable.MaxRank, GroveRanks.Waves.TopPercent(1));
            Assert.AreEqual(50, GroveRanks.Waves.TopPercent(10));

            // And the two distributions do not bleed into each other.
            Assert.IsFalse(GroveRanks.Table.IsUsable);
        }

        [Test]
        public void AWaveNobodyCanBeMeasuredAgainstSaysNothing()
        {
            // Never published at all — a first launch, an offline build, a game whose first day
            // it is. This is the state the board ships in and it must not invent a percentile.
            Assert.AreEqual(-1, GroveRanks.Waves.TopPercent(40));

            // Published, but over too few watchers to mean anything (invariant 19c's own bar).
            GroveRanks.Publish(Published(GroveRankTable.MinimumSamples - 1,
                                         2, 4, 6, 8, 10, 12, 14, 16, 18));
            Assert.AreEqual(-1, GroveRanks.Waves.TopPercent(40));

            // And a keeper who has never run the lane is not in the population being described.
            GroveRanks.Publish(Published(4000, 2, 4, 6, 8, 10, 12, 14, 16, 18));
            Assert.AreEqual(-1, GroveRanks.Waves.TopPercent(0));
        }

        [Test]
        public void TheNameplateSaysTheMostInformativeTrueThingItCan()
        {
            // Never run: the plate names the absence. "Best wave 0" is a bad score where a
            // player who has never run has no score.
            string unplayed = EndlessHub.CaptionFor(0);
            Assert.AreEqual(unplayed, EndlessHub.CaptionFor(-4));

            // Run, but nothing to stand against yet — which is every player on the day this
            // ships, and every player with no backend.
            string label = EndlessHub.CaptionFor(23);
            Assert.AreNotEqual(unplayed, label);

            // Run, and a population exists: the standing replaces the label, because the disc
            // above it is already a large number saying "best wave".
            GroveRanks.Publish(Published(4000, 2, 4, 6, 8, 10, 12, 14, 16, 18));

            string standing = EndlessHub.CaptionFor(23);
            Assert.AreNotEqual(label, standing);
            Assert.AreNotEqual(unplayed, standing);

            // Three states, three different sentences, none of them empty. **Whether the keys
            // resolve is deliberately not asserted here**: this runner loads no localisation, so
            // `Loc.Get` echoes the key back and every one of these would read as a missing
            // string. That question belongs to `Tools/verify/loc.py`, which resolves every
            // key-shaped literal in the source against `loc/en.json` and fails the build on one
            // that is absent (invariant 6) — a fixture asserting it here would be testing the
            // harness.
            foreach (string line in new[] { unplayed, label, standing }) Assert.IsNotEmpty(line);
        }

        [Test]
        public void ACardBuiltFromASaveCarriesThatSavesWave()
        {
            // The card a publish is judged on is built from the file the server holds, never
            // from the live ledger — a run finished while a push was in flight is on the
            // device and not on the server. So a ledger holding more than the save must not
            // leak into the fingerprint, or the device marks a card published that was never
            // built from what it is looking at.
            EndlessLedger.Record(Watch, 99);

            var card = GroveCard.OfSave(HomesteadCatalog.Empty, Saved(("s02_endlesswatch", 40)),
                                        "uid", 4, 1L);

            Assert.AreEqual(40, card.BestWave);
        }
    }
}
