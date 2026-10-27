using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Cloud;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Social;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The free bonus, the client half (invariant 67).
    ///
    /// <para>
    /// <b>The ledger</b> is a mirror of the wallet's record: nothing is offered until an answer (or this
    /// account's cache) says so, an account with nothing left is cached as finished and never asked again,
    /// a transient answer (feature off, not yet enrolled) is never cached, the cache belongs to one
    /// account, and a claim adopts the server's balances and standing only for the account that asked.
    /// <b>The medal</b> stands in the column beside the BATTLE key and can touch nothing around it in either
    /// shape of the hub's foot. The server half is <c>firebase/functions/test/free-bonus.mjs</c>.
    /// </para>
    /// </summary>
    public sealed class FreeBonusTests
    {
        const string Uid = "uid-bonus";

        Cloud _cloud;
        EndlessCoins.MemoryStore _store;

        [SetUp]
        public void Open()
        {
            SaveService.Unload();
            EndlessCoins.UseStore(new EndlessCoins.MemoryStore());
            SaveService.LoadWith(new AccountSwitchTests.MemoryStore());
            CloudState.SignIn(Uid);

            _cloud = new Cloud { Session = Uid };
            CloudSaveService.UseBackend(_cloud);

            _store = new EndlessCoins.MemoryStore();
            FreeBonusLedger.UseStore(_store);
        }

        [TearDown]
        public void Close()
        {
            FreeBonusLedger.UseStore(null);
            CloudSaveService.UseBackend(null);
            SaveService.Unload();
            EndlessCoins.UseStore(null);
        }

        static FreeBonusStanding Standing(bool live, bool enrolled, params FreeBonusRow[] rows)
        {
            var standing = new FreeBonusStanding { Live = live, Enrolled = enrolled };
            standing.Rows.AddRange(rows);
            return standing;
        }

        static FreeBonusRow Link(bool paid = false, bool done = false) => new FreeBonusRow(FreeBonusTask.Link, 60, paid, done);
        static FreeBonusRow Name(bool paid = false, bool done = false) => new FreeBonusRow(FreeBonusTask.Name, 60, paid, done);

        static T Run<T>(Task<T> task)
        {
            Assert.IsTrue(task.Wait(5000), "the call finished");
            return task.Result;
        }

        // ------------------------------------------------------------ the tasks
        [Test]
        public void EveryTaskHasOneSpellingAndNothingElseParses()
        {
            foreach (var task in FreeBonusTasks.All)
            {
                Assert.IsTrue(FreeBonusTasks.TryParse(FreeBonusTasks.Spelling(task), out var back));
                Assert.AreEqual(task, back);
            }
            Assert.AreEqual("link", FreeBonusTasks.Spelling(FreeBonusTask.Link), "the wire spelling is free-bonus.ts's");
            Assert.AreEqual("name", FreeBonusTasks.Spelling(FreeBonusTask.Name));
            Assert.IsFalse(FreeBonusTasks.TryParse("email", out _));
            Assert.IsFalse(FreeBonusTasks.TryParse(null, out _));
        }

        // ------------------------------------------------------------ the mirror
        [Test]
        public void NothingIsOfferedBeforeAnAnswer()
        {
            Assert.IsFalse(FreeBonusLedger.Offered, "a build ahead of the functions offers nothing");
            Assert.AreEqual(0, FreeBonusLedger.Rows.Count);
        }

        [Test]
        public void AnUnpaidTaskWearsTheBadgeAndOnlyTheOffersAreListed()
        {
            FreeBonusLedger.ApplyForTests(Standing(true, true, Name()));

            Assert.IsTrue(FreeBonusLedger.Offered);
            Assert.AreEqual(1, FreeBonusLedger.Rows.Count, "a linked account is offered the name alone");
            Assert.IsFalse(FreeBonusLedger.TryGet(FreeBonusTask.Link, out _), "an exempt task is never listed");
        }

        [Test]
        public void AnAccountWithNothingLeftIsCachedAndNeverAskedAgain()
        {
            FreeBonusLedger.ApplyForTests(Standing(true, true, Link(paid: true), Name(paid: true)));
            Assert.IsFalse(FreeBonusLedger.Offered, "both taken: no badge");

            Assert.IsFalse(Run(FreeBonusLedger.RefreshAsync()), "a finished account is not asked");
            Assert.AreEqual(0, _cloud.Reads);

            // A veteran - both done before the offer - is answered with no rows at all, and is finished too.
            FreeBonusLedger.UseStore(_store);
            FreeBonusLedger.ApplyForTests(Standing(true, true));
            Assert.IsFalse(FreeBonusLedger.Offered);
            Assert.IsFalse(Run(FreeBonusLedger.RefreshAsync()));
            Assert.AreEqual(0, _cloud.Reads);

            Assert.IsTrue(Run(FreeBonusLedger.RefreshAsync(force: true)), "the panel still asks");
            Assert.AreEqual(1, _cloud.Reads);
        }

        [Test]
        public void ATransientAnswerIsNeverCached()
        {
            FreeBonusLedger.ApplyForTests(Standing(false, false));
            Assert.IsFalse(FreeBonusLedger.Offered);
            Assert.AreEqual(string.Empty, _store.Read("glimmer.freebonus.v1." + Uid), "the feature off is not remembered");

            FreeBonusLedger.ApplyForTests(Standing(true, false));
            Assert.AreEqual(string.Empty, _store.Read("glimmer.freebonus.v1." + Uid), "nor an account not yet enrolled");

            _cloud.Standing = Standing(true, true, Link());
            Assert.IsTrue(Run(FreeBonusLedger.RefreshAsync()), "and the next ask is made");
            Assert.IsTrue(FreeBonusLedger.Offered);
        }

        [Test]
        public void TheCacheBelongsToOneAccount()
        {
            FreeBonusLedger.ApplyForTests(Standing(true, true, Link(), Name()));
            Assert.IsTrue(FreeBonusLedger.Offered);

            CloudState.SignIn("uid-other");
            _cloud.Session = "uid-other";
            Assert.IsFalse(FreeBonusLedger.Offered, "another account is offered nothing until it is answered");

            CloudState.SignIn(Uid);
            _cloud.Session = Uid;
            Assert.IsTrue(FreeBonusLedger.Offered, "coming back reads this account's cache");
            Assert.AreEqual(2, FreeBonusLedger.Rows.Count);
        }

        [Test]
        public void ACacheLineReadsBackAndGarbageIsNoCache()
        {
            var rows = new List<FreeBonusRow> { Link(paid: true, done: true), Name() };
            string line = FreeBonusLedger.Encode(false, rows);
            Assert.AreEqual("-|link,60,1,1|name,60,0,0", line);

            var back = new List<FreeBonusRow>();
            Assert.IsTrue(FreeBonusLedger.TryDecode(line, out bool finished, back));
            Assert.IsFalse(finished);
            Assert.AreEqual(2, back.Count);
            Assert.IsTrue(back[0].Equals(rows[0]) && back[1].Equals(rows[1]));

            Assert.IsTrue(FreeBonusLedger.TryDecode("F", out finished, back));
            Assert.IsTrue(finished);
            Assert.AreEqual(0, back.Count);

            Assert.IsTrue(FreeBonusLedger.TryDecode("-|email,5,0,0|name,60,0,0", out _, back), "a later build's task is skipped");
            Assert.AreEqual(1, back.Count);

            foreach (var bad in new[] { "", "{\"finished\":true}", "-|name,sixty,0,0", "-|name,60" })
                Assert.IsFalse(FreeBonusLedger.TryDecode(bad, out _, back), bad);
        }

        [Test]
        public void ALinkIsDoneByThisDevicesOwnSignIn()
        {
            FreeBonusLedger.ApplyForTests(Standing(true, true, Link()));

            _cloud.Linked = false;
            Assert.IsFalse(FreeBonusLedger.IsDone(FreeBonusTask.Link));

            _cloud.Linked = true;
            Assert.IsTrue(FreeBonusLedger.IsDone(FreeBonusTask.Link), "back from the account panel: CLAIM at once");
        }

        // ------------------------------------------------------------ claiming
        [Test]
        public void AClaimAdoptsTheServersAnswer()
        {
            FreeBonusLedger.ApplyForTests(Standing(true, true, Link(done: true), Name()));
            _cloud.Answer = new FreeBonusClaim(FreeBonusClaimOutcome.Paid, 60);
            _cloud.Standing = Standing(true, true, Link(paid: true, done: true), Name());

            var claim = Run(FreeBonusLedger.ClaimAsync(FreeBonusTask.Link));

            Assert.AreEqual(FreeBonusClaimOutcome.Paid, claim.Outcome);
            Assert.AreEqual(60, claim.Gems);
            Assert.AreEqual(FreeBonusTask.Link, _cloud.Asked);
            Assert.IsTrue(FreeBonusLedger.TryGet(FreeBonusTask.Link, out var row) && row.Paid, "the mirror is the server's answer");
            Assert.IsTrue(FreeBonusLedger.Offered, "the name is still offered");
            Assert.IsFalse(FreeBonusLedger.IsClaiming(FreeBonusTask.Link), "no answer leaves a claim marked running");
        }

        [Test]
        public void TheLastClaimTakesTheBadgeAwayForGood()
        {
            FreeBonusLedger.ApplyForTests(Standing(true, true, Link(done: true)));
            _cloud.Answer = new FreeBonusClaim(FreeBonusClaimOutcome.Paid, 60);
            _cloud.Standing = Standing(true, true, Link(paid: true, done: true));

            Run(FreeBonusLedger.ClaimAsync(FreeBonusTask.Link));

            Assert.IsFalse(FreeBonusLedger.Offered);
            Assert.AreEqual("F|link,60,1,1", _store.Read("glimmer.freebonus.v1." + Uid), "cached as finished");
        }

        [Test]
        public void AClaimForAnotherAccountIsNeverAdopted()
        {
            FreeBonusLedger.ApplyForTests(Standing(true, true, Link(done: true)));
            _cloud.Answer = new FreeBonusClaim(FreeBonusClaimOutcome.Paid, 60);
            _cloud.Standing = Standing(true, true, Link(paid: true, done: true));
            _cloud.SwitchTo = "uid-somebody-else";

            var claim = Run(FreeBonusLedger.ClaimAsync(FreeBonusTask.Link));

            Assert.AreEqual(FreeBonusClaimOutcome.Unavailable, claim.Outcome,
                            "the server paid the account that asked; this one learns on its own ask");
            Assert.IsFalse(FreeBonusLedger.IsClaiming(FreeBonusTask.Link));
        }

        [Test]
        public void AFailedClaimChangesNothing()
        {
            FreeBonusLedger.ApplyForTests(Standing(true, true, Link(done: true)));
            _cloud.Fails = true;

            var claim = Run(FreeBonusLedger.ClaimAsync(FreeBonusTask.Link));

            Assert.AreEqual(FreeBonusClaimOutcome.Unavailable, claim.Outcome);
            Assert.IsTrue(FreeBonusLedger.Offered, "the row can be tapped again");
            Assert.IsFalse(FreeBonusLedger.IsClaiming(FreeBonusTask.Link));
        }

        // ------------------------------------------------------------ the medal
        /// <summary>Canvas heights the hub is drawn on: the squarest (7:4 widened), 16:9, 19.5:9, 20:9, 21:9.</summary>
        static readonly float[] Canvases = { HubFoot.ShortestCanvas, 1920f, 2340f, 2400f, 2520f };

        /// <summary>Safe-area tops in canvas units: none, a notch, a tall punch-hole bar.</summary>
        static readonly float[] SafeTops = { 0f, 90f, 140f };

        [Test]
        public void TheMedalTouchesNothingOnAnyCanvas()
        {
            foreach (bool welcome in new[] { false, true })
            foreach (bool paired in new[] { false, true })
            foreach (float canvas in Canvases)
            foreach (float safeTop in SafeTops)
            {
                var foot = HubFoot.For(welcome);
                var spot = foot.BonusAt(canvas, safeTop, paired);
                string where = $"{(welcome ? "welcome" : "plain")}, {(paired ? "paired" : "alone")}, {canvas} tall, safe {safeTop}";
                float breath = 1f + HubFoot.KeyBreath;
                float half = spot.H * .5f * breath;

                if (spot.Under)
                {
                    float rowFoot = canvas - safeTop - HubFoot.FeatureBottom;   // up from the bottom
                    Assert.LessOrEqual(spot.Y + half, rowFoot, $"{where}: under the streak box, never over it");
                    Assert.GreaterOrEqual(spot.Y - half, foot.Top, $"{where}: and never over the BATTLE key");
                    Assert.AreEqual(HubFoot.StreakX(paired), spot.X, .01f, $"{where}: centred under the streak box");
                    Assert.LessOrEqual(spot.H, HubFoot.UnderMaxH);
                }
                else
                {
                    float medalRight = spot.X + spot.W * .5f * breath;
                    Assert.Less(medalRight, -foot.PlayW * .5f * breath, $"{where}: beside the key, off it at its widest");
                    Assert.GreaterOrEqual(spot.X - spot.W * .5f * breath, -HubFoot.Width * .5f, $"{where}: inside the stack");
                    Assert.LessOrEqual(spot.H * breath, foot.PlayH + 2f * foot.Gap, $"{where}: within the key's band");
                    Assert.AreEqual(foot.PlayY, spot.Y, $"{where}: centred on the key");
                }

                Assert.GreaterOrEqual(spot.W, 120f, $"{where}: drawn at a readable size");
                Assert.GreaterOrEqual(spot.CaptionH, 48f, $"{where}: two lines fit the disc");
            }
        }

        [Test]
        public void OnAnOrdinaryPhoneTheMedalStandsUnderTheStreakBox()
        {
            foreach (bool welcome in new[] { false, true })
            {
                var spot = HubFoot.For(welcome).BonusAt(2400f, 90f, true);
                Assert.IsTrue(spot.Under, "a 20:9 phone leaves room under the streak box");
                Assert.AreEqual(HubFoot.UnderMaxH, spot.H, "and the medal is drawn at its full size there");
            }

            Assert.IsFalse(HubFoot.For(true).BonusAt(HubFoot.ShortestCanvas, 0f, true).Under,
                           "the squarest canvas leaves no room, so it stands beside the key");
        }

        [Test]
        public void TheCaptionSitsInsideTheMedalsDisc()
        {
            foreach (var spot in new[] { HubFoot.For(false).BonusAt(2400f, 90f, true),
                                         HubFoot.For(true).BonusAt(HubFoot.ShortestCanvas, 0f, true) })
            {
                float r = spot.W * HubFoot.MedalDiscW * .5f;
                Assert.LessOrEqual(spot.CaptionW, 2f * r + .01f, "no wider than the disc");
                Assert.Less(Math.Abs(spot.CaptionX) + spot.CaptionW * .5f, spot.W * .5f, "inside the medal across");
                Assert.Less(Math.Abs(spot.CaptionY) + spot.CaptionH * .5f, spot.H * .5f, "and down");
            }
        }

        [Test]
        public void TheMedalsAspectIsThePictures()
        {
            string png = Path.Combine(TestJson.RepoRoot(), "Assets", "Game", "Art", "Ui", "ic_free_bonus.png");
            if (!File.Exists(png)) Assert.Inconclusive("ic_free_bonus.png is not on disk");

            // The PNG header: width and height are big-endian at bytes 16 and 20.
            var head = new byte[24];
            using (var file = File.OpenRead(png)) Assert.AreEqual(24, file.Read(head, 0, 24));
            int w = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
            int h = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];

            Assert.AreEqual((float)w / h, HubFoot.MedalAspect, 1e-4f,
                            "re-run make_free_bonus_art.py --measure and copy its figures into HubFoot");
        }

        // ------------------------------------------------------------ the double
        sealed class Cloud : ICloudSaveBackend, IFreeBonusBackend
        {
            public string Session;
            public bool Linked = true;
            public bool Fails;
            public string SwitchTo;
            public int Reads;
            public FreeBonusTask? Asked;
            public FreeBonusStanding Standing = new FreeBonusStanding { Live = true, Enrolled = true };
            public FreeBonusClaim Answer = new FreeBonusClaim(FreeBonusClaimOutcome.Unavailable);

            public bool IsAvailable => true;

            public CloudIdentity CurrentIdentity
                => string.IsNullOrEmpty(Session) ? CloudIdentity.None : new CloudIdentity(Session, Linked);

            public Task<(CloudResult result, FreeBonusStanding standing)> ReadFreeBonusAsync(CancellationToken c = default)
            {
                Reads++;
                return Task.FromResult(Fails ? (CloudResult.Failed(CloudFailure.Offline, "down"), (FreeBonusStanding)null)
                                             : (CloudResult.Success, Standing));
            }

            public Task<(CloudResult result, FreeBonusClaim claim, FreeBonusStanding standing, List<CloudWalletState> wallets)>
                ClaimFreeBonusAsync(FreeBonusTask task, CancellationToken c = default)
            {
                Asked = task;
                if (SwitchTo != null) { Session = SwitchTo; CloudState.SignIn(SwitchTo); }

                return Task.FromResult(Fails
                    ? (CloudResult.Failed(CloudFailure.Offline, "down"), new FreeBonusClaim(FreeBonusClaimOutcome.Unavailable),
                       (FreeBonusStanding)null, new List<CloudWalletState>())
                    : (CloudResult.Success, Answer, Standing, new List<CloudWalletState>()));
            }

            static Task<(CloudResult, List<CloudWalletState>)> No()
                => Task.FromResult((CloudResult.Failed(CloudFailure.Rejected, "not this fixture"), new List<CloudWalletState>()));

            public Task<(CloudResult result, List<CloudWalletState> wallets, CloudRedemption redemption)>
                RedeemPurchaseAsync(string userId, PurchaseReceipt receipt, CancellationToken c = default)
                => Task.FromResult((CloudResult.Failed(CloudFailure.Rejected, "not this fixture"),
                                    new List<CloudWalletState>(), CloudRedemption.Nothing));

            public Task<(CloudResult result, CloudIdentity identity)> SignInAsync(CancellationToken c = default)
                => Task.FromResult((CloudResult.Success, CurrentIdentity));

            public Task<(CloudResult result, CloudIdentity identity)> ResumeAsync(CancellationToken c = default)
                => Task.FromResult((CloudResult.Success, CurrentIdentity));

            public Task<(CloudResult result, CloudIdentity identity)> LinkAsync(
                LinkCredential cr, CancellationToken c = default)
                => Task.FromResult((CloudResult.Failed(CloudFailure.Rejected, "not this fixture"), CloudIdentity.None));

            public Task<(CloudResult result, CloudIdentity identity)> SignInWithCredentialAsync(
                LinkCredential cr, CancellationToken c = default)
                => Task.FromResult((CloudResult.Failed(CloudFailure.Rejected, "not this fixture"), CloudIdentity.None));

            public Task<(CloudResult result, string appleAuthorizationCode)> ReauthenticateAsync(
                LinkCredential credential, CancellationToken c = default)
                => Task.FromResult((CloudResult.Failed(CloudFailure.Rejected, "not this fixture"), string.Empty));

            public Task<CloudResult> DeleteAccountAsync(
                string userId, string appleAuthorizationCode = null, CancellationToken c = default)
                => Task.FromResult(CloudResult.Failed(CloudFailure.Rejected, "not this fixture"));

            public Task<(CloudResult result, CloudSnapshot snapshot)> PullAsync(string userId, CancellationToken c = default)
                => Task.FromResult((CloudResult.Failed(CloudFailure.Rejected, "not this fixture"), CloudSnapshot.Missing));

            public Task<CloudResult> PushAsync(string userId, SaveFileDto snapshot, SaveDelta delta, CancellationToken c = default)
                => Task.FromResult(CloudResult.Failed(CloudFailure.Rejected, "not this fixture"));

            public Task<(CloudResult result, List<CloudWalletState> wallets)> ReadWalletAsync(
                string userId, CancellationToken c = default) => No();

            public Task<(CloudResult result, List<CloudWalletState> wallets)> SubmitSpendsAsync(
                string userId, IReadOnlyList<SpendSubmission> spends, CancellationToken c = default) => No();

            public Task<(CloudResult result, List<CloudWalletState> wallets)> SubmitAwardsAsync(
                string userId, IReadOnlyList<GrantEntryDto> awards, CancellationToken c = default) => No();

            public Task<(CloudResult result, Release.ReleaseRequirement requirement)> ReadReleaseAsync(
                string platform, CancellationToken c = default)
                => Task.FromResult((CloudResult.Failed(CloudFailure.Offline, "not this fixture"),
                                    Release.ReleaseRequirement.None));
        }
    }
}
