using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Persistence;
using UnityEngine;

namespace GlimmerGrove.Cloud
{
    /// <summary>
    /// What this device owes the server, and getting it there when the player leaves.
    ///
    /// <para>
    /// <b>Why this file exists.</b> A streak shield bought on one phone was not held on the
    /// other (2026-09-28), and it was one case of a whole class: a run, a purchase, a claim or
    /// any other change reached the server only on a sync, and the sync started as the app was
    /// backgrounded never finished while it was backgrounded. Every <c>await</c> in a sync resumes
    /// on Unity's main thread (see <c>FirebaseCloudSaveBackend</c>), and Unity stops pumping that
    /// thread the moment the app is paused - so that sync sent its read, and its merge and its
    /// push waited for the player to come back. On the other device the change simply was not
    /// there. Two halves fix it, and they are the two halves of this file.
    /// </para>
    /// <para>
    /// <b>What is owed</b> (<see cref="Owes"/>) is a comparison, never a flag: the device's file
    /// against the last state both sides agreed on (<see cref="SaveDelta"/>, the same reading a
    /// push is built from), plus any wallet entry the server has not been offered. It is asked
    /// of every local write (<see cref="SaveService.Written"/>), which is the one door every
    /// persistent change passes through - so a feature added next year is synced without being
    /// taught to be - and a merge arriving from the cloud does not pass through that door, so it
    /// cannot schedule the next sync (<c>SyncTriggers</c>' warning about <c>Changed</c>).
    /// </para>
    /// <para>
    /// <b>The departure</b> (<see cref="Depart"/>) is a pull, a join and a push run entirely off
    /// the main thread over a snapshot taken on it. Everything it touches is data: the backend,
    /// <see cref="SaveMerge.Join"/> and <see cref="SaveDelta"/> are pure over the files they are
    /// handed, and nothing in the game's own state is read or written after the snapshot. The
    /// device adopts nothing - the next foreground sync learns whatever the server knew, exactly
    /// as it always has - so the only thing a departure can do is put this device's work on the
    /// server sooner. It also costs <em>less</em> than what it replaced: a background with
    /// nothing owed now sends nothing at all, where it used to cost a read and a callable.
    /// </para>
    /// </summary>
    public static partial class CloudSaveService
    {
        // --------------------------------------------------------------- the latch
        // Who holds it, because a departure has to tell the two apart. A sync holding it is safe
        // to run beside - both sides of a merge are monotonic, so two pushes of one account's
        // save can only disagree about which of two true things lands first, and the next sync
        // joins them. An account change holding it is not: the grove being pushed may be the one
        // being left, and that operation already ends with a sync of its own.
        const int LatchFree = 0;
        const int LatchSync = 1;
        const int LatchIdentity = 2;

        // ---------------------------------------------------------- what is owed
        // The last file both sides agreed on, which account it belonged to, and which wallet
        // entries were still waiting on the server at that moment. Main thread only: written by
        // a sync as it settles and read by the writes that follow it.
        static SaveFileDto _agreed;
        static string _agreedUser;
        static HashSet<string> _agreedPending = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Whether <paramref name="file"/> holds anything the server has not been told.
        ///
        /// <para>
        /// True when no sync has settled this session, or the last one settled for a different
        /// account - "unknown" has to read as owed, because the cost of a wrong yes is one read
        /// and the cost of a wrong no is a purchase on one phone. Otherwise true exactly when the
        /// file differs from the agreed one in anything a push carries, or the wallet holds an
        /// entry that was not already waiting when the two last agreed: an entry the server has
        /// been offered and left unconfirmed (13a) is resubmitted by every sync anyway, and
        /// counting it again would turn every local write into a sync for as long as it waits.
        /// </para>
        /// </summary>
        internal static bool Owes(SaveFileDto file)
        {
            if (file == null) return false;
            if (_agreed == null) return true;
            if (!string.Equals(_agreedUser, file.cloud?.userId ?? string.Empty, StringComparison.Ordinal))
                return true;

            if (!SaveDelta.Between(_agreed, file).IsEmpty) return true;

            foreach (var key in PendingKeys())
                if (!_agreedPending.Contains(key)) return true;

            return false;
        }

        /// <summary>
        /// Records the state a sync has just left both sides holding. Called on the main thread
        /// by the sync, after its wallet reconcile and before the flush that follows it, so the
        /// flush's own write finds nothing owed.
        /// </summary>
        static void Agree(string userId, SaveFileDto merged)
        {
            _agreed = merged;
            _agreedUser = userId ?? string.Empty;
            _agreedPending = new HashSet<string>(PendingKeys(), StringComparer.Ordinal);
        }

        /// <summary>
        /// Forgets the agreed state, so the next write reads as owed. For an account change,
        /// and for the tests - the service is process-wide.
        /// </summary>
        internal static void ForgetAgreement()
        {
            _agreed = null;
            _agreedUser = null;
            _agreedPending = new HashSet<string>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Every wallet entry still waiting on the server, keyed by currency, side and id - a
        /// claim id carries its currency already, and a debit id does not, so the key does.
        /// </summary>
        static IEnumerable<string> PendingKeys()
        {
            foreach (var ledger in Wallet.Ledgers)
            {
                foreach (var grant in ledger.PendingGrants)
                    yield return ledger.Currency + "|g|" + grant.Id;
                foreach (var spend in ledger.PendingSpends)
                    yield return ledger.Currency + "|s|" + spend.Id;
            }
        }

        /// <summary>
        /// Asks for a sync within <see cref="SyncScheduler.AmbientSeconds"/>, for a change that
        /// is owed but was not a deliberate act. See <see cref="RequestSync"/> for the other.
        /// </summary>
        public static void RequestSyncEventually() => _schedule.RequestEventually();

        // ------------------------------------------------------------ the departure
        /// <summary>
        /// The most a departure may take, all of it. Inside the thirty seconds iOS grants a
        /// background task and the several seconds before Android freezes a cached process, and
        /// far above a read and a write on any connection that can do either.
        /// </summary>
        public const int DepartureSeconds = 25;

        /// <summary>One departure's cargo, built on the main thread and read off it.</summary>
        sealed class Departure
        {
            public readonly string UserId;
            public readonly SaveFileDto Local;
            public readonly List<GrantEntryDto> Awards;
            public readonly List<SpendSubmission> Spends;

            public Departure(string userId, SaveFileDto local,
                             List<GrantEntryDto> awards, List<SpendSubmission> spends)
            {
                UserId = userId;
                Local = local;
                Awards = awards;
                Spends = spends;
            }
        }

        // A departure asked for while one is already out waits here for it, newest wins: two
        // pauses a second apart are one push of the later file, never two racing pushes.
        static Departure _queued;
        static int _departing;
        static Task _departure = Task.CompletedTask;
        static IBackgroundGrace _grace = new NoGrace();

        /// <summary>
        /// True while a departure is on the wire. A sync asked for meanwhile answers
        /// <see cref="CloudFailure.Busy"/> and is asked for again, so the two never overlap.
        /// </summary>
        public static bool IsDeparting => Volatile.Read(ref _departing) != 0;

        /// <summary>
        /// How this platform asks the operating system for time to finish a departure. Chosen
        /// once, in <c>Boot</c>; the default asks for nothing.
        /// </summary>
        public static void UseBackgroundGrace(IBackgroundGrace grace) => _grace = grace ?? new NoGrace();

        /// <summary>
        /// Puts what this device owes on the server as the player leaves. Called from
        /// <c>Boot.Pump</c> when the app is paused, after the save has been flushed to disk.
        ///
        /// <para>
        /// Everything that decides whether to go is asked here, on the main thread, before
        /// anything leaves it: there is a backend and a loaded save; no account change is in
        /// flight; the save names an account and the session is that account
        /// (<see cref="AccountGate"/>, invariant 17 - a departure is a push, and a push addressed
        /// to anyone else is the one mistake here with no undo); and something is owed. Then the
        /// snapshot and the wallet's waiting entries are taken, and the rest runs on the thread
        /// pool under <see cref="DepartureSeconds"/> and whatever background time the platform
        /// grants.
        /// </para>
        /// <para>
        /// The task is returned for the tests and for nobody else. It never faults: every
        /// failure is logged and left for the next sync, which carries the same work.
        /// </para>
        /// </summary>
        public static Task Depart()
        {
            if (!IsAvailable || !SaveService.IsLoaded) return Task.CompletedTask;
            if (Volatile.Read(ref _syncing) == LatchIdentity) return Task.CompletedTask;

            string userId = CloudState.UserId;
            if (string.IsNullOrEmpty(userId)) return Task.CompletedTask;
            if (AccountGate.Decide(userId, _backend.CurrentIdentity.UserId) != AccountGateVerdict.Proceed)
                return Task.CompletedTask;

            var local = SaveService.Snapshot();
            if (!Owes(local)) return Task.CompletedTask;

            // The same order the sync submits in, and for its reason: a claim opened offline
            // must be able to pay for the debit that followed it.
            var awards = new List<GrantEntryDto>();
            var spends = new List<SpendSubmission>();
            foreach (var ledger in Wallet.Ledgers)
            {
                foreach (var grant in ledger.PendingGrants) awards.Add(grant.ToDto());
                foreach (var spend in ledger.PendingSpends) spends.Add(new SpendSubmission(ledger.Currency, spend.ToDto()));
            }

            Interlocked.Exchange(ref _queued, new Departure(userId, local, awards, spends));

            // One carrier at a time. If one is already out it will find this parcel when it
            // finishes the one in hand.
            if (Interlocked.CompareExchange(ref _departing, 1, 0) != 0) return _departure;

            var backend = _backend;
            var grace = BeginGrace();
            _departure = Task.Run(() => CarryAsync(backend, grace));
            return _departure;
        }

        static IDisposable BeginGrace()
        {
            try { return _grace.Begin(); }
            catch (Exception e)
            {
                Debug.LogWarning("[Cloud] no background time for the departure: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Carries every queued departure, then lets go. Runs on the thread pool, so every
        /// <c>await</c> below resumes on the pool rather than on a main thread that is paused.
        /// </summary>
        static async Task CarryAsync(ICloudSaveBackend backend, IDisposable grace)
        {
            try
            {
                while (true)
                {
                    var parcel = Interlocked.Exchange(ref _queued, null);

                    if (parcel == null)
                    {
                        Volatile.Write(ref _departing, 0);

                        // Something queued between the empty read and the release would
                        // otherwise wait for the next pause. Take the carrier back for it,
                        // unless a new one already has.
                        if (Volatile.Read(ref _queued) == null) return;
                        if (Interlocked.CompareExchange(ref _departing, 1, 0) != 0) return;
                        continue;
                    }

                    await DeliverAsync(backend, parcel);
                }
            }
            catch (Exception e)
            {
                // DeliverAsync catches its own; this is the loop itself, and the carrier must be
                // let go whatever happened or no sync would ever run again.
                Volatile.Write(ref _departing, 0);
                Debug.LogWarning("[Cloud] departure failed: " + e.Message);
            }
            finally
            {
                try { grace?.Dispose(); }
                catch (Exception e) { Debug.LogWarning("[Cloud] could not end the background task: " + e.Message); }
            }
        }

        /// <summary>
        /// Pull, join, push, then offer the wallet's waiting entries. The replies are dropped:
        /// applying a balance or a refusal touches ledgers, which is main-thread work, and the
        /// next sync resubmits the same ids - each is idempotent by construction (10a) - and
        /// applies the same answers.
        /// </summary>
        static async Task DeliverAsync(ICloudSaveBackend backend, Departure parcel)
        {
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(DepartureSeconds)))
            {
                var token = deadline.Token;

                try
                {
                    var (pull, snapshot) = await backend.PullAsync(parcel.UserId, token);
                    if (pull.Ok)
                    {
                        var remote = snapshot != null && snapshot.Exists ? snapshot.Save : null;
                        var merged = remote == null ? parcel.Local : SaveMerge.Join(parcel.Local, remote);
                        var delta = SaveDelta.Between(remote, merged);

                        if (!delta.IsEmpty)
                        {
                            var push = await backend.PushAsync(parcel.UserId, merged, delta, token);
                            if (!push.Ok) Debug.LogWarning("[Cloud] departure push failed: " + push);
                        }
                    }
                    else
                    {
                        Debug.LogWarning("[Cloud] departure pull failed: " + pull);
                    }

                    // Offered whether or not the save went. They travel by another road, and a
                    // debit that reaches the server is what makes the other device's balance
                    // agree with the thing it bought.
                    if (parcel.Awards.Count > 0) await backend.SubmitAwardsAsync(parcel.UserId, parcel.Awards, token);
                    if (parcel.Spends.Count > 0) await backend.SubmitSpendsAsync(parcel.UserId, parcel.Spends, token);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Cloud] departure did not finish: " + e.Message);
                }
            }
        }

        /// <summary>Asks for nothing. The Editor, Android, and any platform with no binding.</summary>
        sealed class NoGrace : IBackgroundGrace
        {
            public IDisposable Begin() => null;
        }

        /// <summary>
        /// Waits for a departure to finish and clears everything this file keeps. The service
        /// is process-wide, so a test that left a departure out would leak it into the next.
        /// </summary>
        internal static void ResetDepartureForTests()
        {
            try { _departure?.Wait(TimeSpan.FromSeconds(DepartureSeconds)); }
            catch (AggregateException) { }

            _queued = null;
            Volatile.Write(ref _departing, 0);
            _departure = Task.CompletedTask;
            _grace = new NoGrace();
            ForgetAgreement();
        }
    }

    /// <summary>
    /// Asks the operating system to keep the process running a little after it is backgrounded.
    ///
    /// <para>
    /// A departure is issued the moment the app is paused and finishes on the thread pool, which
    /// is enough on Android - a backgrounded process keeps running for several seconds before it
    /// is frozen - and not guaranteed on iOS, which suspends an app shortly after it leaves the
    /// foreground unless it asks for time. The iOS binding is <c>UIApplication
    /// beginBackgroundTask</c>; see <c>BackgroundGrace</c> in Presentation.
    /// </para>
    /// </summary>
    public interface IBackgroundGrace
    {
        /// <summary>
        /// Begins a background task, or answers null when the platform has none. Disposing the
        /// answer ends it, from any thread, exactly once.
        /// </summary>
        IDisposable Begin();
    }
}
