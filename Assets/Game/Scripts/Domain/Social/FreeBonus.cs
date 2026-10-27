using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Analytics;
using GlimmerGrove.Cloud;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using UnityEngine;

namespace GlimmerGrove.Social
{
    /// <summary>
    /// A free-bonus task (invariant 67). <b>Code, never content</b>: what counts as done is a fact the
    /// server reads off its own records, so a task is a verb both sides know. Its wire spelling is
    /// <see cref="FreeBonusTasks.Spelling"/>; the gems it pays are content (<c>progression.json</c>'s
    /// <c>freeBonus</c> block), and reach the client only in the server's answer.
    /// </summary>
    public enum FreeBonusTask
    {
        /// <summary>Link a permanent sign-in (Apple or Google) to the account.</summary>
        Link = 0,

        /// <summary>Choose a keeper name of one's own, reserved by the server.</summary>
        Name = 1,
    }

    /// <summary>The tasks' wire spellings, <c>FREE_BONUS_TASKS</c> in <c>free-bonus.ts</c>.</summary>
    public static class FreeBonusTasks
    {
        /// <summary>Every task, in the order the panel lists them.</summary>
        public static readonly FreeBonusTask[] All = { FreeBonusTask.Link, FreeBonusTask.Name };

        public static string Spelling(FreeBonusTask task)
        {
            switch (task)
            {
                case FreeBonusTask.Link: return "link";
                case FreeBonusTask.Name: return "name";
                default: throw new ArgumentOutOfRangeException(nameof(task), task, "a free-bonus task with no spelling");
            }
        }

        /// <summary>Reads a spelling; false for anything this build does not know, which is then ignored.</summary>
        public static bool TryParse(string spelling, out FreeBonusTask task)
        {
            switch (spelling)
            {
                case "link": task = FreeBonusTask.Link; return true;
                case "name": task = FreeBonusTask.Name; return true;
                default: task = default; return false;
            }
        }
    }

    /// <summary>One task as the server listed it. Exempt tasks are never listed.</summary>
    public readonly struct FreeBonusRow
    {
        public readonly FreeBonusTask Task;

        /// <summary>The gems the offer promised - the server's figure, never one this build holds.</summary>
        public readonly int Gems;

        /// <summary>Paid already, on this device or another.</summary>
        public readonly bool Paid;

        /// <summary>Done by the server's facts when it answered.</summary>
        public readonly bool Done;

        public FreeBonusRow(FreeBonusTask task, int gems, bool paid, bool done)
        {
            Task = task;
            Gems = gems < 0 ? 0 : gems;
            Paid = paid;
            Done = done;
        }

        public bool Equals(FreeBonusRow other)
            => Task == other.Task && Gems == other.Gems && Paid == other.Paid && Done == other.Done;
    }

    /// <summary><c>getFreeBonus</c>'s answer.</summary>
    public sealed class FreeBonusStanding
    {
        /// <summary>False when the config publishes no bonus: nothing is offered, and nothing is cached.</summary>
        public bool Live;

        /// <summary>False until the server has a wallet to write the offer on - asked again shortly.</summary>
        public bool Enrolled;

        public readonly List<FreeBonusRow> Rows = new List<FreeBonusRow>();
    }

    /// <summary>What a claim came to. The wire spellings are <c>claimFreeBonus</c>'s.</summary>
    public enum FreeBonusClaimOutcome
    {
        /// <summary>No answer: offline, signed out, a claim already out, or a reply this build could not read.</summary>
        Unavailable = 0,

        /// <summary>Paid by this call; <see cref="FreeBonusClaim.Gems"/> is what it paid.</summary>
        Paid,

        /// <summary>Already paid - on this device or another. Nothing new arrives.</summary>
        Spent,

        /// <summary>The server cannot see the task done yet. Never permanent: do it, then ask again.</summary>
        NotDone,

        /// <summary>This sign-in already earned the link bonus for another account; the task is withdrawn.</summary>
        Used,

        /// <summary>Never offered to this account, or the bonus is switched off.</summary>
        None,

        /// <summary>The nickname was refused by the server (taken, or not publishable): choose another.</summary>
        NameRefused,
    }

    public readonly struct FreeBonusClaim
    {
        public readonly FreeBonusClaimOutcome Outcome;
        public readonly int Gems;

        public FreeBonusClaim(FreeBonusClaimOutcome outcome, int gems = 0)
        {
            Outcome = outcome;
            Gems = gems < 0 ? 0 : gems;
        }

        public bool Paid => Outcome == FreeBonusClaimOutcome.Paid;
    }

    /// <summary>The two free-bonus calls, beside <see cref="ICloudSaveBackend"/> for <c>IDailyPassBackend</c>'s reason.</summary>
    public interface IFreeBonusBackend
    {
        /// <summary>Asks what the account is offered. The first ask enrols it (<c>getFreeBonus</c>).</summary>
        Task<(CloudResult result, FreeBonusStanding standing)> ReadFreeBonusAsync(CancellationToken cancellation = default);

        /// <summary>
        /// Asks the server to pay one task. Answers the outcome, what it paid, the standing after it and
        /// the balances - every time, so a device that thought it was owed something learns the truth in
        /// the same round trip.
        /// </summary>
        Task<(CloudResult result, FreeBonusClaim claim, FreeBonusStanding standing, List<CloudWalletState> wallets)>
            ClaimFreeBonusAsync(FreeBonusTask task, CancellationToken cancellation = default);
    }

    /// <summary>
    /// The free bonus on this device (invariant 67): which tasks the server offers this account, whether
    /// each is done, and the door that asks the server to pay one (<c>functions/src/free-bonus.ts</c>).
    ///
    /// <para>
    /// <b>A mirror, never a record.</b> The offer, its amount and what it paid live on the wallet
    /// document no client can write; this holds the last answer about them and nothing else - nothing in
    /// the save (11e). A claim is the server paying and this device adopting the balances it answered
    /// with, so no state here can strand or forge a reward: the worst a stale mirror does is draw a task
    /// another phone has already taken, and the claim's answer corrects it.
    /// </para>
    /// <para>
    /// <b>Cached per account on the device</b> (<c>glimmer.freebonus.v1.{uid}</c>), the referral's rule:
    /// the hub's badge is drawn from the cache the moment the hub opens, and an account the server has
    /// nothing left to offer is never asked again by this build - zero cost for everybody who has
    /// finished, or was never offered anything. Everything else is re-asked at most every
    /// <see cref="RefreshSeconds"/>, and whenever the panel opens.
    /// </para>
    /// <para>
    /// <b>Done is the server's fact or this device's</b>: a link is this device's own sign-in state
    /// (<see cref="CloudSaveService.IsLinked"/>), a name is a chosen name (<see cref="Wallet.HasChosenName"/>),
    /// so the panel turns a row to CLAIM the moment the player comes back from doing it. The server judges
    /// again before it pays, and a nickname is made sure of first (<see cref="KeeperNames.ClaimAsync"/>):
    /// a rename made offline is reserved now rather than at the next publish.
    /// </para>
    /// </summary>
    public static class FreeBonusLedger
    {
        /// <summary>How long an answer stands before the hub asks again. The panel always asks.</summary>
        public const float RefreshSeconds = 600f;

        /// <summary>How soon to ask again when the server had no wallet to enrol on yet.</summary>
        public const float NotEnrolledRetrySeconds = 60f;

        const string KeyPrefix = "glimmer.freebonus.v1.";

        /// <summary>Raised when what is offered or done changes, or a claim starts or ends. Main thread.</summary>
        public static event Action Changed;

        static readonly List<FreeBonusRow> _rows = new List<FreeBonusRow>();
        static readonly HashSet<FreeBonusTask> _claiming = new HashSet<FreeBonusTask>();

        static EndlessCoins.ITallyStore _store = new PrefsStore();
        static string _account = string.Empty;
        static bool _known, _finished, _hooked, _fetching;
        static float _fetchedAt = float.NegativeInfinity;

        /// <summary>Seconds since this process began - managed, so the fixtures run it offline.</summary>
        static readonly System.Diagnostics.Stopwatch _watch = System.Diagnostics.Stopwatch.StartNew();
        static float Clock => (float)_watch.Elapsed.TotalSeconds;

        static FreeBonusLedger() => Hook();

        /// <summary>Installs the account and name listeners. Idempotent.</summary>
        public static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            CloudSaveService.IdentityChanged += OnIdentityChanged;
            Wallet.ProfileChanged += Raise;
        }

        static IFreeBonusBackend Backend => CloudSaveService.Backend as IFreeBonusBackend;

        /// <summary>Whether the bonus can be asked about at all: a cloud backend that speaks it.</summary>
        public static bool IsAvailable => CloudSaveService.IsAvailable && Backend != null;

        /// <summary>
        /// True when the hub should wear the badge: an answer (or this account's cache) says a task is
        /// offered and unpaid. False before any answer, so a build ahead of the functions offers nothing.
        /// </summary>
        public static bool Offered
        {
            get
            {
                Follow();
                if (!_known || _finished) return false;
                foreach (var row in _rows) if (!row.Paid) return true;
                return false;
            }
        }

        /// <summary>The tasks offered, paid ones included, in the panel's order.</summary>
        public static IReadOnlyList<FreeBonusRow> Rows
        {
            get { Follow(); return _rows; }
        }

        /// <summary>The row for <paramref name="task"/>, if the server offered it.</summary>
        public static bool TryGet(FreeBonusTask task, out FreeBonusRow row)
        {
            Follow();
            foreach (var r in _rows) if (r.Task == task) { row = r; return true; }
            row = default;
            return false;
        }

        /// <summary>Whether <paramref name="task"/> is done: the server said so, or this device knows it.</summary>
        public static bool IsDone(FreeBonusTask task)
        {
            if (TryGet(task, out var row) && row.Done) return true;
            return DoneHere(task);
        }

        /// <summary>This device's own fact for <paramref name="task"/>.</summary>
        static bool DoneHere(FreeBonusTask task)
            => task == FreeBonusTask.Link ? CloudSaveService.IsLinked : Wallet.HasChosenName;

        /// <summary>True while a claim for <paramref name="task"/> is out.</summary>
        public static bool IsClaiming(FreeBonusTask task) => _claiming.Contains(task);

        // ---------------------------------------------------------------- reading
        /// <summary>
        /// Asks the server what this account is offered, unless an answer is fresh
        /// (<see cref="RefreshSeconds"/>) or the account has nothing left to be offered.
        /// <paramref name="force"/> asks regardless (the panel opening), but never twice at once.
        /// Answers whether a fresh answer for this account arrived. Never throws.
        /// </summary>
        public static async Task<bool> RefreshAsync(bool force = false, CancellationToken cancellation = default)
        {
            Follow();
            if (!IsAvailable || _fetching || string.IsNullOrEmpty(_account)) return false;
            if (_finished && !force) return false;
            if (!force && Clock - _fetchedAt < RefreshSeconds) return false;

            _fetching = true;
            string account = _account;
            try
            {
                var authorised = await CloudSaveService.AuthoriseForCallAsync(cancellation);
                if (!authorised.Ok || CloudState.UserId != account) return false;

                var backend = Backend;
                if (backend == null) return false;

                var (result, standing) = await backend.ReadFreeBonusAsync(cancellation);
                if (!result.Ok || standing == null) return false;

                // Another account is signed in now: the answer belongs to the one that asked (17).
                if (CloudState.UserId != account || _account != account) return false;

                _fetchedAt = standing.Live && !standing.Enrolled
                    ? Clock - RefreshSeconds + NotEnrolledRetrySeconds
                    : Clock;
                Apply(standing);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
            finally
            {
                _fetching = false;
            }
        }

        /// <summary>Folds in an answer: replaces the rows whole, and caches what a live, enrolled answer said.</summary>
        static void Apply(FreeBonusStanding standing)
        {
            var incoming = new List<FreeBonusRow>();
            if (standing.Live && standing.Enrolled) incoming.AddRange(standing.Rows);

            bool finished = standing.Live && standing.Enrolled && AllPaid(incoming);
            bool changed = !_known || finished != _finished || !SameRows(incoming);

            _known = true;
            _finished = finished;
            _rows.Clear();
            _rows.AddRange(incoming);

            // Only an answer that is the account's settled standing is remembered. A feature switched off
            // or an account not yet enrolled is transient, and a cache of it would hide a later offer.
            if (standing.Live && standing.Enrolled) WriteCache();

            if (changed) Raise();
        }

        static bool AllPaid(List<FreeBonusRow> rows)
        {
            foreach (var row in rows) if (!row.Paid) return false;
            return true;
        }

        static bool SameRows(List<FreeBonusRow> incoming)
        {
            if (incoming.Count != _rows.Count) return false;
            for (int i = 0; i < incoming.Count; i++) if (!incoming[i].Equals(_rows[i])) return false;
            return true;
        }

        // ---------------------------------------------------------------- claiming
        /// <summary>
        /// Asks the server to pay <paramref name="task"/>.
        ///
        /// <para>
        /// <b>The server pays and this adopts</b>: the balances come back with the answer and go through
        /// the one door every wallet reply uses (<c>CloudSaveService.AdoptWalletStates</c>). Nothing is
        /// credited on the device first, so a reply lost on the way back costs a retry, which the server
        /// answers <see cref="FreeBonusClaimOutcome.Spent"/> with the balance the first call raised.
        /// </para>
        /// <para>
        /// <b>A nickname is reserved before it is claimed for.</b> The name the player chose is claimed
        /// once more (free when already held): a rename made offline lands now, and one the server will
        /// not take is said as <see cref="FreeBonusClaimOutcome.NameRefused"/> before anything is asked.
        /// </para>
        /// <para>
        /// Gated as a purchase redemption is (<c>AuthoriseForCallAsync</c>), and adopted only if the same
        /// account is still signed in (17). One claim per task at a time.
        /// </para>
        /// </summary>
        public static async Task<FreeBonusClaim> ClaimAsync(FreeBonusTask task, CancellationToken cancellation = default)
        {
            Follow();
            if (!IsAvailable || string.IsNullOrEmpty(_account)) return new FreeBonusClaim(FreeBonusClaimOutcome.Unavailable);
            if (!_claiming.Add(task)) return new FreeBonusClaim(FreeBonusClaimOutcome.Unavailable);
            Raise();

            string account = _account;
            try
            {
                var authorised = await CloudSaveService.AuthoriseForCallAsync(cancellation);
                if (!authorised.Ok || CloudState.UserId != account)
                    return new FreeBonusClaim(FreeBonusClaimOutcome.Unavailable);

                if (task == FreeBonusTask.Name && Wallet.HasChosenName)
                {
                    var name = await KeeperNames.ClaimAsync(Wallet.DisplayName, cancellation);
                    switch (name.Outcome)
                    {
                        case NameClaimOutcome.Refused:
                        case NameClaimOutcome.Taken:
                            return new FreeBonusClaim(FreeBonusClaimOutcome.NameRefused);
                        case NameClaimOutcome.Unavailable:
                            return new FreeBonusClaim(FreeBonusClaimOutcome.Unavailable);
                    }
                    if (CloudState.UserId != account) return new FreeBonusClaim(FreeBonusClaimOutcome.Unavailable);
                }

                var backend = Backend;
                if (backend == null) return new FreeBonusClaim(FreeBonusClaimOutcome.Unavailable);

                var (result, claim, standing, wallets) = await backend.ClaimFreeBonusAsync(task, cancellation);
                if (!result.Ok) return new FreeBonusClaim(FreeBonusClaimOutcome.Unavailable);

                // The server paid the account that asked. If somebody else is signed in now, that account
                // banks it on its next sync; adopting it here would put one account's gems in another's.
                if (CloudState.UserId != account || _account != account)
                    return new FreeBonusClaim(FreeBonusClaimOutcome.Unavailable);

                if (wallets != null && wallets.Count > 0)
                {
                    CloudSaveService.AdoptWalletStates(wallets);
                    SaveService.Save();
                }
                if (standing != null)
                {
                    _fetchedAt = Clock;
                    Apply(standing);
                }

                Telemetry.Track("free_bonus_claim", "task", FreeBonusTasks.Spelling(task),
                                "outcome", claim.Outcome.ToString(), "gems", claim.Gems);
                return claim;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return new FreeBonusClaim(FreeBonusClaimOutcome.Unavailable);
            }
            finally
            {
                _claiming.Remove(task);
                Raise();
            }
        }

        // ---------------------------------------------------------------- the account
        /// <summary>
        /// Makes the mirror describe the account signed in now: on a change it forgets the last one and
        /// reads the new one's cache. Called on every read, so a switch that raised nothing is still seen.
        /// </summary>
        static void Follow()
        {
            string account = CloudState.UserId ?? string.Empty;
            if (account == _account) return;

            _account = account;
            _rows.Clear();
            _claiming.Clear();
            _known = _finished = false;
            _fetchedAt = float.NegativeInfinity;
            ReadCache();
        }

        static void OnIdentityChanged()
        {
            // A switch is followed here; a link on the same account changes what is done. Both repaint.
            Follow();
            Raise();
        }

        // ---------------------------------------------------------------- the cache
        /// <summary>
        /// One account's cached answer as a line: <c>F</c> or <c>-</c> (finished), then a row per task,
        /// <c>|spelling,gems,paid,done</c> - e.g. <c>-|link,60,0,0|name,60,1,1</c>. Hand-written rather than
        /// <c>JsonUtility</c>, so the fixtures read it offline (a native call is "needs the Editor").
        /// </summary>
        internal static string Encode(bool finished, IReadOnlyList<FreeBonusRow> rows)
        {
            var line = new System.Text.StringBuilder(finished ? "F" : "-");
            foreach (var row in rows)
                line.Append('|').Append(FreeBonusTasks.Spelling(row.Task)).Append(',').Append(row.Gems)
                    .Append(',').Append(row.Paid ? '1' : '0').Append(',').Append(row.Done ? '1' : '0');
            return line.ToString();
        }

        /// <summary>The line back, or false for anything unreadable - which is treated as no cache.</summary>
        internal static bool TryDecode(string line, out bool finished, List<FreeBonusRow> rows)
        {
            finished = false;
            rows.Clear();
            if (string.IsNullOrEmpty(line) || (line[0] != 'F' && line[0] != '-')) return false;

            var parts = line.Split('|');
            for (int i = 1; i < parts.Length; i++)
            {
                var f = parts[i].Split(',');
                if (f.Length != 4 || !int.TryParse(f[1], out int gems)) { rows.Clear(); return false; }
                if (!FreeBonusTasks.TryParse(f[0], out var task)) continue;     // a later build's task
                rows.Add(new FreeBonusRow(task, gems, f[2] == "1", f[3] == "1"));
            }
            finished = line[0] == 'F';
            return true;
        }

        static void ReadCache()
        {
            if (string.IsNullOrEmpty(_account)) return;

            string line;
            try { line = _store.Read(KeyPrefix + _account); }
            catch (Exception e) { Debug.LogException(e); return; }

            var rows = new List<FreeBonusRow>();
            if (!TryDecode(line, out bool finished, rows)) return;

            _rows.AddRange(rows);
            _finished = finished;
            _known = true;
        }

        static void WriteCache()
        {
            if (string.IsNullOrEmpty(_account)) return;

            try { _store.Write(KeyPrefix + _account, Encode(_finished, _rows)); }
            catch (Exception e) { Debug.LogException(e); }
        }

        sealed class PrefsStore : EndlessCoins.ITallyStore
        {
            public string Read(string key) => PlayerPrefs.GetString(key, string.Empty);
            public void Write(string key, string value) => DevicePrefs.WriteString(key, value);

            public void Delete(string key)
            {
                if (!PlayerPrefs.HasKey(key)) return;
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            }
        }

        // ---------------------------------------------------------------- tests
        /// <summary>Installs a store and forgets everything held; the game's own for null. For fixtures.</summary>
        internal static void UseStore(EndlessCoins.ITallyStore store)
        {
            _store = store ?? new PrefsStore();
            _account = string.Empty;
            _rows.Clear();
            _claiming.Clear();
            _known = _finished = _fetching = false;
            _fetchedAt = float.NegativeInfinity;
        }

        /// <summary>For tests: folds in an answer as a reply would, for the account signed in now.</summary>
        internal static void ApplyForTests(FreeBonusStanding standing)
        {
            Follow();
            Apply(standing);
        }

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
