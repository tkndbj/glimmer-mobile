using GlimmerGrove.Persistence;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Hearts gate play, so the arithmetic here is the difference between a fair wait
    /// and a player locked out of a game they paid attention to.
    ///
    /// Two properties matter more than the rest and are pinned hardest: refills must
    /// not <em>drift</em> (closing the app mid-timer neither loses nor gains the
    /// remainder), and the merge must never <em>mint</em> (two devices cannot refill
    /// each other). Everything else is a consequence of those two.
    /// </summary>
    public sealed class HeartsTests
    {
        static readonly long P = HeartRules.RefillSeconds;
        const long T0 = 1_700_000_000;      // an arbitrary fixed "now"

        [Test]
        public void AFullSetRunsNoTimer()
        {
            var hearts = Hearts.Full;

            Assert.AreEqual(HeartRules.RefillCap, hearts.Count);
            Assert.AreEqual(0, hearts.NextRefillUnix, "a full player has no deadline to show");
            Assert.AreEqual(0, hearts.SecondsToNext(T0));
        }

        [Test]
        public void SpendingFromFullStartsTheClock()
        {
            var hearts = Hearts.Full.Spend(1, T0);

            Assert.AreEqual(HeartRules.RefillCap - 1, hearts.Count);
            Assert.AreEqual(T0 + P, hearts.NextRefillUnix);
            Assert.AreEqual(P, hearts.SecondsToNext(T0));
        }

        /// <summary>
        /// Losing again must not push the pending heart away. If it did, a bad run of
        /// defeats would silently reset a timer the player had already half-waited.
        /// </summary>
        [Test]
        public void SpendingAgainDoesNotPushTheRefillBack()
        {
            var first = Hearts.Full.Spend(1, T0);
            var second = first.Spend(1, T0 + 60);

            Assert.AreEqual(HeartRules.RefillCap - 2, second.Count);
            Assert.AreEqual(first.NextRefillUnix, second.NextRefillUnix,
                            "the pending heart keeps its original deadline");
        }

        [Test]
        public void OnePeriodReturnsExactlyOneHeart()
        {
            var spent = Hearts.Full.Spend(2, T0);      // 3/5, next at T0+P

            var justBefore = spent.At(T0 + P - 1);
            Assert.AreEqual(HeartRules.RefillCap - 2, justBefore.Count, "not yet");

            var onTime = spent.At(T0 + P);
            Assert.AreEqual(HeartRules.RefillCap - 1, onTime.Count);
            Assert.AreEqual(T0 + 2 * P, onTime.NextRefillUnix);
        }

        [Test]
        public void SeveralElapsedPeriodsReturnSeveralHearts()
        {
            var spent = Hearts.Full.Spend(4, T0);      // 1/5, next at T0+P

            // refills land at T0+P and T0+2P; the third is not due until T0+3P
            var later = spent.At(T0 + 2 * P);
            Assert.AreEqual(3, later.Count);
            Assert.AreEqual(T0 + 3 * P, later.NextRefillUnix);
        }

        [Test]
        public void ReachingTheCapClearsTheDeadline()
        {
            var spent = Hearts.Full.Spend(1, T0);
            var back = spent.At(T0 + 10 * P);

            Assert.AreEqual(HeartRules.RefillCap, back.Count);
            Assert.AreEqual(0, back.NextRefillUnix, "no timer once full, however long we waited");
        }

        /// <summary>
        /// The anti-drift property. Reading the state repeatedly must be identical to
        /// reading it once at the end — otherwise every HUD tick would shave the timer.
        /// </summary>
        [Test]
        public void ReadingRepeatedlyDoesNotDrift()
        {
            var spent = Hearts.Full.Spend(3, T0);

            var stepped = spent;
            for (long t = T0; t < T0 + 2 * P; t += 37) stepped = stepped.At(t);

            // land the last read on the same instant as the direct one, or the two are
            // being asked different questions rather than the same one twice
            stepped = stepped.At(T0 + 2 * P);

            var direct = spent.At(T0 + 2 * P);

            Assert.AreEqual(direct.Count, stepped.Count);
            Assert.AreEqual(direct.NextRefillUnix, stepped.NextRefillUnix,
                            "polling the timer must not move it");
        }

        [Test]
        public void SpendingAtZeroChangesNothingAndBlocksPlay()
        {
            var empty = Hearts.Full.Spend(HeartRules.RefillCap, T0);
            Assert.IsTrue(empty.IsEmpty);
            Assert.IsFalse(empty.CanPlay, "zero hearts is the gate");

            var again = empty.Spend(1, T0 + 5);
            Assert.AreEqual(0, again.Count);
            Assert.AreEqual(empty.NextRefillUnix, again.NextRefillUnix);
        }

        [Test]
        public void GrantingBeyondTheCeilingClamps()
        {
            var granted = Hearts.Full.Spend(2, T0).Grant(999, T0);

            Assert.AreEqual(HeartRules.Ceiling, granted.Count);
            Assert.AreEqual(0, granted.NextRefillUnix);

            Assert.AreEqual(HeartRules.Ceiling, granted.Grant(10, T0).Count,
                            "a grant into a full ledger is simply dropped");
        }

        // ------------------------------------------------- stacking past the cap
        /// <summary>
        /// The rule the refill cap and the holding ceiling exist to separate: a chest, a
        /// streak night or a watched video pays a player who is already at five, instead of
        /// evaporating at exactly the moment they were most engaged.
        /// </summary>
        [Test]
        public void GrantsStackPastTheRefillCap()
        {
            var full = Hearts.Full;
            Assert.IsTrue(full.IsRefilled, "the clock has nothing left to do");
            Assert.IsFalse(full.IsAtCeiling, "which is not the same as having nowhere to put a reward");

            var richer = full.Grant(3, T0);

            Assert.AreEqual(HeartRules.RefillCap + 3, richer.Count);
            Assert.IsTrue(richer.IsRefilled);
            Assert.AreEqual(0, richer.NextRefillUnix, "and still no timer, because none is owed");
        }

        /// <summary>
        /// The clock is a floor, not a drain. A surplus has to survive any amount of time
        /// passing — the failure this guards against is a catch-up loop that "corrects"
        /// somebody back down to five.
        /// </summary>
        [Test]
        public void TheClockNeitherAddsToNorEatsASurplus()
        {
            var rich = Hearts.Full.Grant(4, T0);

            var week = rich.At(T0 + 21 * P);

            Assert.AreEqual(HeartRules.RefillCap + 4, week.Count);
            Assert.AreEqual(rich, week, "nothing about the ledger moved at all");
        }

        /// <summary>
        /// The one case the two-number design had to get right, and the reason
        /// <see cref="Hearts.Spend"/> asks <c>NextRefillUnix</c> rather than comparing to
        /// the cap. While a surplus is held the stored deadline idles in the past; if the
        /// spend that finally drops the player under the cap resumed from that stale value,
        /// the very next read would pay a heart the player never waited for — and repeating
        /// it would turn a surplus into an unlimited supply.
        /// </summary>
        [Test]
        public void SpendingBackThroughTheCapRestartsTheClockRatherThanPayingInstantly()
        {
            // eight hearts, and a deadline left far behind by the last refill
            var rich = Hearts.Full.Spend(1, T0).At(T0 + P).Grant(4, T0 + P);
            Assert.AreEqual(HeartRules.RefillCap + 4, rich.Count);

            long later = T0 + 30 * P;
            Assert.Less(rich.DueUnix, later, "the stored deadline really is stale");

            // spend down to one under the cap, a long time afterwards
            var spent = rich.Spend(5, later);
            Assert.AreEqual(HeartRules.RefillCap - 1, spent.Count);
            Assert.AreEqual(later + P, spent.NextRefillUnix, "a fresh period, not a stale deadline");

            Assert.AreEqual(HeartRules.RefillCap - 1, spent.At(later).Count,
                            "and reading it back pays nothing");
        }

        /// <summary>
        /// The timer still stops at five however the player got there, which is what keeps
        /// the pace of free play exactly as it was. A surplus is something you collect, not
        /// something you can wait for.
        /// </summary>
        [Test]
        public void TheClockNeverCarriesAnybodyPastTheRefillCap()
        {
            var empty = Hearts.Full.Spend(HeartRules.RefillCap, T0);

            var forever = empty.At(T0 + 100 * P);

            Assert.AreEqual(HeartRules.RefillCap, forever.Count);
        }

        /// <summary>
        /// A v3 save carries a count and no deadline. It must start waiting from now,
        /// not be paid for all the time that passed before hearts regenerated at all.
        /// </summary>
        [Test]
        public void AStateWithNoDeadlineStartsTheClockRatherThanBackPaying()
        {
            var legacy = new Hearts(2, 0);
            var caught = legacy.At(T0);

            Assert.AreEqual(2, caught.Count, "no free hearts for time nobody waited");
            Assert.AreEqual(T0 + P, caught.NextRefillUnix);
        }

        // -------------------------------------------------------------- the merge
        /// <summary>
        /// The bug this whole shape exists to end, as it was reported: a player holding
        /// three hearts pulls down the notification shade, which backgrounds the app,
        /// which starts a sync against a cloud snapshot written before the refills landed.
        ///
        /// <para>
        /// A sync is pull → join → push, so the join runs <em>before</em> the local value
        /// has ever been uploaded. Under the old rule — the smaller stored count wins —
        /// the stale zero won, the zero was then pushed, and both sides agreed on nothing.
        /// Reproduced live on 2026-08-14, and it is what a timer refill met every single
        /// time it was followed by the app being backgrounded.
        /// </para>
        /// </summary>
        [Test]
        public void ARefillSurvivesASyncAgainstAStaleCloudSave()
        {
            // the state that reached the server: empty, next heart due in eight hours
            var cloud = Hearts.Full.Spend(HeartRules.RefillCap, T0);
            Assert.AreEqual(0, cloud.Count);

            // the device waited out three of them and never got to push
            var local = cloud.At(T0 + 3 * P);
            Assert.AreEqual(3, local.Count, "three periods, three hearts");

            var merged = Hearts.Join(local, cloud);

            Assert.AreEqual(3, merged.Count, "a stale snapshot must not delete a refill");
            Assert.AreEqual(local.DueUnix, merged.DueUnix);
        }

        /// <summary>
        /// The same, for a heart that arrived from a chest or a rewarded ad rather than
        /// from the clock. Both are grants the client applies locally, so both died the
        /// same way.
        /// </summary>
        [Test]
        public void AGrantSurvivesASyncAgainstAStaleCloudSave()
        {
            var cloud = Hearts.Full.Spend(HeartRules.RefillCap, T0);
            var local = cloud.Grant(2, T0 + 60);

            Assert.AreEqual(2, Hearts.Join(local, cloud).Count);
            Assert.AreEqual(2, Hearts.Join(cloud, local).Count);
        }

        /// <summary>
        /// The failure mode in full: syncing repeatedly against a snapshot that never
        /// learns anything. The old rule drained the player to zero and kept them there —
        /// the timer would deliver a heart, the next foreground would take it away, and no
        /// amount of waiting could ever put one on screen.
        /// </summary>
        [Test]
        public void RepeatedSyncsAgainstAStaleSnapshotConverge()
        {
            var stale = Hearts.Full.Spend(HeartRules.RefillCap, T0);
            var device = stale;

            for (int round = 1; round <= 4; round++)
            {
                device = device.At(T0 + round * P);
                device = Hearts.Join(device, stale);     // background, sync, come back

                Assert.AreEqual(round, device.Count,
                                "the player must keep every heart the clock has paid them");
            }
        }

        /// <summary>
        /// A device at the cap carries a refill deadline that has already paid out and is
        /// therefore idle. Taking the larger of the two must not push the other device's
        /// pending heart away — and it cannot, because the deadline only ever moves to one
        /// period past a refill that has already happened.
        /// </summary>
        [Test]
        public void AFullDeviceDoesNotEraseTheOthersCountdown()
        {
            var full = Hearts.Full;                                       // idle, deadline 0
            var waiting = Hearts.Ledger(produced: 5, spent: 4, dueUnix: T0 + 500);

            var joined = Hearts.Join(full, waiting);

            Assert.AreEqual(1, joined.Count, "the four spends survive");
            Assert.AreEqual(T0 + 500, joined.NextRefillUnix, "and so does the countdown");
        }

        [Test]
        public void JoinIsIdempotentCommutativeAndAssociative()
        {
            var a = Hearts.Ledger(produced: 9, spent: 5, dueUnix: T0 + 100);
            var b = Hearts.Ledger(produced: 7, spent: 5, dueUnix: T0 + 900);
            var c = Hearts.Ledger(produced: 9, spent: 8, dueUnix: 0);

            Assert.AreEqual(Hearts.Join(a, b), Hearts.Join(b, a), "commutative");

            var once = Hearts.Join(a, b);
            Assert.AreEqual(once, Hearts.Join(once, Hearts.Join(a, b)), "idempotent");

            // associative, so three devices converge regardless of sync order
            Assert.AreEqual(Hearts.Join(Hearts.Join(a, b), c),
                            Hearts.Join(a, Hearts.Join(b, c)), "associative");
        }

        [Test]
        public void TwoDevicesCannotRefillEachOther()
        {
            // both start full, each loses two, then they sync
            var deviceA = Hearts.Full.Spend(2, T0);
            var deviceB = Hearts.Full.Spend(2, T0);

            var joined = Hearts.Join(deviceA, deviceB);

            Assert.AreEqual(HeartRules.RefillCap - 2, joined.Count,
                            "a merge must never hand back a heart somebody spent");
        }

        /// <summary>
        /// Two devices that each spend independently: neither loses its own spend, and
        /// the merge cannot mint one back. It is deliberately not the <em>sum</em> — with
        /// no shared history there is nothing that distinguishes "two runs each" from
        /// "one device heard about the other's run", and charging for both would be the
        /// mirror of the bug being fixed. Forgiving at most a cap's worth of hearts across
        /// a genuine offline split is the cheap side of that trade.
        /// </summary>
        [Test]
        public void ConcurrentSpendsAreNeverForgivenBelowTheLargerSide()
        {
            var shared = Hearts.Full;

            var phone = shared.Spend(3, T0);
            var tablet = shared.Spend(1, T0);

            var joined = Hearts.Join(phone, tablet);

            Assert.AreEqual(HeartRules.RefillCap - 3, joined.Count);
            Assert.LessOrEqual(joined.Count, tablet.Count, "no device gains from a merge");
        }

        /// <summary>
        /// The two invariants the whole design rests on, checked over every combination
        /// this game can produce rather than argued for in a comment. If either can be
        /// broken by a join then hearts can be minted or deleted by syncing, which is the
        /// only thing that actually matters here.
        /// </summary>
        [Test]
        public void TheJoinPreservesTheInvariantsForEveryPairing()
        {
            var states = new System.Collections.Generic.List<Hearts>();

            // The whole holding range, not a sample of it. Grants stack fifty deep now, so
            // this is 459 states and ~210k pairs — cheap, because a join is three
            // comparisons. What is *not* cheap is an assertion message: an interpolated
            // string is built before the assert runs, whether or not it fails, so passing
            // one per pair would mean nearly a million Hearts.ToString() calls. The
            // condition is therefore tested first and the message built only to report a
            // failure, which is what keeps full coverage affordable rather than trading it
            // away for a list of boundaries.
            for (int spent = 0; spent <= HeartRules.RefillCap + 3; spent++)
                for (int held = 0; held <= HeartRules.Ceiling; held++)
                    states.Add(Hearts.Ledger(spent + held, spent, T0 + spent * 37 + held));

            foreach (var a in states)
                foreach (var b in states)
                {
                    var j = Hearts.Join(a, b);

                    if (j.Count < 0)
                        Assert.Fail($"{a} joined {b} went negative");

                    if (j.Count > HeartRules.Ceiling)
                        Assert.Fail($"{a} joined {b} exceeded the ceiling");

                    // the join knows at least as much as either side did
                    if (j.Produced < System.Math.Max(a.Produced, b.Produced))
                        Assert.Fail($"{a} joined {b} forgot a grant");

                    if (j.Spent < System.Math.Max(a.Spent, b.Spent))
                        Assert.Fail($"{a} joined {b} forgot a spend");
                }
        }

        // ------------------------------------------------------ the v7 upgrade
        /// <summary>
        /// A pre-v8 save holds a count and no history. Read on its own it is simply that
        /// many hearts, spent nothing — the only reading available, and the one that keeps
        /// what the player was holding.
        /// </summary>
        [Test]
        public void APreLedgerSaveKeepsItsCount()
        {
            var upgraded = new Hearts(3, T0 + 500);

            Assert.AreEqual(3, upgraded.Count);
            Assert.AreEqual(T0 + 500, upgraded.NextRefillUnix);
            Assert.AreEqual(0, upgraded.Spent, "nothing is known to have been spent yet");
        }

        /// <summary>
        /// Joined against a device that <em>does</em> keep a ledger, the old count has to
        /// be rebased first. Without that its zero spend would look like a device that had
        /// spent nothing at all, and every heart the modern side had used would come back.
        /// </summary>
        [Test]
        public void APreLedgerCountIsRebasedBeforeItIsJoined()
        {
            var modern = Hearts.Ledger(produced: 40, spent: 38, dueUnix: T0 + 500);   // holding 2
            var legacy = Hearts.Observation(count: 1, dueUnix: T0 + 900, spentAnchor: modern.Spent);

            var joined = Hearts.Join(modern, legacy);

            Assert.AreEqual(2, joined.Count, "the old build's smaller count must not mint 38 hearts back");

            // and the other way round: the old build holding more is believed, because its
            // count is the only evidence there is and it is bounded by the ceiling
            var richer = Hearts.Observation(count: 5, dueUnix: T0 + 900, spentAnchor: modern.Spent);
            Assert.AreEqual(5, Hearts.Join(modern, richer).Count);
        }
    }

    /// <summary>
    /// The same rules seen through the save file, which is where they actually run and
    /// where the loss actually happened. <see cref="Hearts.Join"/> being correct is not
    /// worth much if <see cref="SaveMerge"/> reads the wrong fields into it — and during
    /// the upgrade it is reading two files written by two different builds.
    /// </summary>
    public sealed class HeartMergeTests
    {
        const long T0 = 1_700_000_000;

        static SaveFileDto WithLedger(long produced, long spent, long due) => new SaveFileDto
        {
            schemaVersion = SaveSchema.Version,
            updatedUnix = T0,
            wallet = new WalletDto
            {
                coins = -1, gems = -1,
                heartsProduced = produced, heartsSpent = spent, heartsDueUnix = due,
                hearts = (int)(produced - spent),
                heartsNextRefillUnix = produced - spent >= HeartRules.RefillCap ? 0 : due,
            },
        };

        /// <summary>A file from before the ledger existed: a count, a deadline, no history.</summary>
        static SaveFileDto PreLedger(int hearts, long due) => new SaveFileDto
        {
            schemaVersion = 7,
            updatedUnix = T0,
            wallet = new WalletDto { coins = -1, gems = -1, hearts = hearts, heartsNextRefillUnix = due },
        };

        static int HeartsIn(SaveFileDto dto) => dto.wallet.hearts;

        /// <summary>
        /// The reported failure at the level it was reported: three hearts locally, a
        /// cloud document that predates them, and a merge that used to answer zero and
        /// then upload it.
        /// </summary>
        [Test]
        public void AStaleCloudSaveCannotDeleteLocalHearts()
        {
            var local = WithLedger(produced: 8, spent: 5, due: T0 + 400);   // holding 3
            var cloud = WithLedger(produced: 5, spent: 5, due: T0 + 400);   // holding 0

            Assert.AreEqual(3, HeartsIn(SaveMerge.Join(local, cloud)));
            Assert.AreEqual(3, HeartsIn(SaveMerge.Join(cloud, local)),
                            "and the answer cannot depend on which device is syncing");
        }

        /// <summary>
        /// A surplus collected past the refill cap has to survive a sync like anything
        /// else, and the mirror written for older clients has to carry it. It is the same
        /// <c>max</c> that protects an ordinary refill — the point of the test is that
        /// nothing in the file path re-imposes the cap the ledger deliberately does not.
        /// </summary>
        [Test]
        public void ASurplusPastTheRefillCapSurvivesASync()
        {
            var local = WithLedger(produced: 12, spent: 4, due: T0 + 400);   // holding 8
            var cloud = WithLedger(produced: 5, spent: 4, due: T0 + 400);    // holding 1

            Assert.AreEqual(8, HeartsIn(SaveMerge.Join(local, cloud)));
            Assert.AreEqual(8, HeartsIn(SaveMerge.Join(cloud, local)));

            var merged = SaveMerge.Join(cloud, local).wallet;
            Assert.AreEqual(0, merged.heartsNextRefillUnix,
                            "above the cap the mirror shows no timer, because none is running");
        }

        /// <summary>
        /// The upgrade itself, and the moment the old bug did its damage: a device that
        /// has just installed v8 still holds a v7 file, and the cloud document was last
        /// written by a v7 build too. Neither side has a ledger, so the count is all there
        /// is — and the larger one wins, which repairs a player the old rule had drained
        /// rather than preserving the drain.
        /// </summary>
        [Test]
        public void TwoPreLedgerFilesKeepTheLargerCount()
        {
            var local = PreLedger(3, T0 + 400);
            var cloud = PreLedger(0, T0 + 400);

            Assert.AreEqual(3, HeartsIn(SaveMerge.Join(local, cloud)));
            Assert.AreEqual(3, HeartsIn(SaveMerge.Join(cloud, local)));
        }

        /// <summary>
        /// A v7 file must never be mistaken for a ledger holding nothing. JsonUtility
        /// fills the absent fields with zero, so this is one <c>&gt;=</c> away from taking
        /// every heart from every player on the day v8 ships.
        /// </summary>
        [Test]
        public void AnAbsentLedgerIsNotAnEmptyOne()
        {
            var upgrading = PreLedger(HeartRules.RefillCap, 0);

            Assert.AreEqual(0, upgrading.wallet.heartsProduced,
                            "the field really is zero — that is the case being defended against");

            Assert.AreEqual(HeartRules.RefillCap, HeartsIn(SaveMerge.Join(upgrading, PreLedger(HeartRules.RefillCap, 0))));
            Assert.AreEqual(HeartRules.RefillCap, HeartsIn(SaveMerge.Join(upgrading, WithLedger(5, 0, 0))));
        }

        /// <summary>
        /// One device updated, the other has not. The old build's count is rebased onto
        /// the new one's spend before the join, so its missing history cannot hand back
        /// hearts the updated device has already used.
        /// </summary>
        [Test]
        public void APreLedgerFileDoesNotResurrectSpentHearts()
        {
            var updated = WithLedger(produced: 40, spent: 38, due: T0 + 500);   // holding 2
            var old = PreLedger(1, T0 + 900);

            Assert.AreEqual(2, HeartsIn(SaveMerge.Join(updated, old)));
            Assert.AreEqual(2, HeartsIn(SaveMerge.Join(old, updated)));
        }

        /// <summary>
        /// A wallet section that was never written holds no opinion at all, which is not
        /// the same as holding zero. Getting this wrong empties a brand-new install.
        /// </summary>
        [Test]
        public void AnUnwrittenWalletDefersToTheOtherSide()
        {
            var fresh = new SaveFileDto
            {
                schemaVersion = SaveSchema.Version, updatedUnix = T0, wallet = WalletDto.Unwritten(),
            };
            var real = WithLedger(produced: 9, spent: 7, due: T0 + 100);       // holding 2

            Assert.AreEqual(2, HeartsIn(SaveMerge.Join(fresh, real)));
            Assert.AreEqual(2, HeartsIn(SaveMerge.Join(real, fresh)));

            var bothFresh = SaveMerge.Join(fresh, fresh);
            Assert.AreEqual(HeartRules.RefillCap, HeartsIn(bothFresh), "a new account is seeded full");
        }

        /// <summary>
        /// The count and deadline written beside the ledger are a mirror for older
        /// clients, so they have to agree with it. A mirror that drifts is worse than no
        /// mirror, because a rolled-back client would read it and believe it.
        /// </summary>
        [Test]
        public void TheMirrorAgreesWithTheLedgerItMirrors()
        {
            var merged = SaveMerge.Join(WithLedger(9, 7, T0 + 100), WithLedger(9, 5, T0 + 900)).wallet;

            Assert.AreEqual(merged.heartsProduced - merged.heartsSpent, merged.hearts);
            Assert.AreEqual(merged.heartsDueUnix, merged.heartsNextRefillUnix,
                            "below the cap the mirror shows the real deadline");

            var full = SaveMerge.Join(WithLedger(5, 0, T0 + 100), WithLedger(5, 0, T0 + 900)).wallet;
            Assert.AreEqual(HeartRules.RefillCap, full.hearts);
            Assert.AreEqual(0, full.heartsNextRefillUnix, "at the cap the mirror shows no timer");
            Assert.AreEqual(T0 + 900, full.heartsDueUnix, "while the ledger keeps the idling deadline");
        }

        /// <summary>
        /// Merging the same pair twice must change nothing. A sync runs every time the app
        /// is foregrounded, so anything that shifts on a repeat merge shifts a few dozen
        /// times a day.
        /// </summary>
        [Test]
        public void MergingIsIdempotentThroughTheFile()
        {
            var mine = WithLedger(12, 9, T0 + 300);
            var theirs = WithLedger(11, 10, T0 + 800);

            var once = SaveMerge.Join(mine, theirs).wallet;
            var twice = SaveMerge.Join(SaveMerge.Join(mine, theirs), theirs).wallet;

            Assert.AreEqual(once.heartsProduced, twice.heartsProduced);
            Assert.AreEqual(once.heartsSpent, twice.heartsSpent);
            Assert.AreEqual(once.heartsDueUnix, twice.heartsDueUnix);
        }

        /// <summary>
        /// The delta decides whether a sync writes anything at all, so it has to notice a
        /// ledger that moved — including the case where a refill landed and the count did
        /// not change because the player spent one in the same window.
        /// </summary>
        [Test]
        public void TheDeltaNoticesALedgerThatMovedBehindAnUnchangedCount()
        {
            var before = WithLedger(9, 6, T0 + 300);        // holding 3
            var after = WithLedger(10, 7, T0 + 900);        // still holding 3

            Assert.AreEqual(HeartsIn(before), HeartsIn(after), "the same count either side");
            Assert.IsTrue(SaveDelta.Between(before, after).ScalarsChanged,
                          "a refill and a spend that cancel out are still two facts to send");

            Assert.IsFalse(SaveDelta.Between(before, WithLedger(9, 6, T0 + 300)).ScalarsChanged,
                           "and an unchanged ledger still sends nothing");
        }
    }

    /// <summary>
    /// The clock guard. A backwards jump is ordinary on a real device — an NTP
    /// correction, a timezone fix — and without the guard it would strand a refill
    /// deadline in the future and show a countdown that grows.
    /// </summary>
    public sealed class GameClockTests
    {
        sealed class FixedClock : IGameClock
        {
            public long Now;
            public long UtcNowUnix => Now;
            public bool IsTrusted => false;
        }

        [TearDown]
        public void Restore() => GameClock.Set(new DeviceClock());

        [Test]
        public void TimeNeverGoesBackwards()
        {
            var clock = new FixedClock { Now = 1_000 };
            GameClock.Set(clock);

            Assert.AreEqual(1_000, GameClock.NowUnix());

            clock.Now = 400;        // the player winds the device back
            Assert.AreEqual(1_000, GameClock.NowUnix(), "the guard holds the high-water mark");

            clock.Now = 1_500;      // and forwards again
            Assert.AreEqual(1_500, GameClock.NowUnix());
        }

        [Test]
        public void TheDeviceClockIsNeverTrusted()
        {
            GameClock.Set(new DeviceClock());
            Assert.IsFalse(GameClock.IsTrusted,
                           "a user-settable clock must never be evidence for a purchase");
        }

        [Test]
        public void AServerAnchoredClockReportsServerTimeAndIsTrusted()
        {
            var device = new FixedClock { Now = 1_000 };
            var server = new ServerAnchoredClock(device);

            server.Anchor(5_000);                       // server says it is really 5000
            Assert.AreEqual(5_000, server.UtcNowUnix);
            Assert.IsTrue(server.IsTrusted);

            device.Now = 1_060;                         // a minute of device time passes
            Assert.AreEqual(5_060, server.UtcNowUnix, "the offset rides along");
        }

        /// <summary>
        /// Moving the device clock moves the reading and the offset together, so the
        /// corrected time does not budge. That is the whole point of anchoring.
        /// </summary>
        [Test]
        public void MovingTheDeviceClockDoesNotMoveAnchoredTime()
        {
            var device = new FixedClock { Now = 1_000 };
            var server = new ServerAnchoredClock(device);
            server.Anchor(5_000);

            device.Now = 999_000;                       // a big jump forward
            long farFuture = server.UtcNowUnix;

            device.Now = 1_000;                         // and back
            server.Anchor(5_000);                       // next sync re-anchors

            Assert.AreEqual(5_000, server.UtcNowUnix);
            Assert.AreNotEqual(farFuture, server.UtcNowUnix,
                               "an un-resynced jump is exactly what the server anchor exists to correct");
        }
    }
}
