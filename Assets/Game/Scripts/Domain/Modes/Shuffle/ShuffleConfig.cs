using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Analytics;
using GlimmerGrove.Cloud;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Shuffle
{
    /// <summary>
    /// The one read the Shuffle tuning needs: the public document, once in a while, never
    /// listened to. Implemented by the Firebase backend; a backend without it leaves every run on
    /// what the device already holds.
    /// </summary>
    public interface IShuffleTuningBackend
    {
        /// <summary>
        /// The published document's body for <paramref name="schema"/>, or null when nothing is
        /// published for it (no document, or no field for this schema). Only a failure to reach
        /// the database fails, so the caller can tell "nothing is published" from "did not ask".
        /// </summary>
        Task<(CloudResult result, string body)> ReadShuffleTuningAsync(int schema, CancellationToken cancellation = default);
    }

    /// <summary>
    /// <b>Which Shuffle tuning the next run opens on</b> (MODES.md 59l): the latest good published
    /// one this device has read, else the build's own.
    ///
    /// <para>
    /// <b>Never in a run's way.</b> <see cref="Current"/> answers at once from memory - it never
    /// waits on the network and never throws - and a run takes it as a snapshot when it opens
    /// (<c>ShuffleScreen</c>), so a document that lands mid-run changes nothing until the next.
    /// The read happens beside play: once after the first settled sync of a launch
    /// (<see cref="Attach"/>, the moment an account exists to read with - the rules grant
    /// <c>config/*</c> to signed-in clients) and whenever the Shuffle hub is drawn
    /// (<see cref="Refresh"/>), at most every <see cref="StaleSeconds"/>. Never a listener: a
    /// listener bills every open client on every write (the owner's rule, invariant 60).
    /// </para>
    /// <para>
    /// <b>Offline is the ordinary case, not a failure.</b> The last good document is kept on the
    /// device (<see cref="CacheKey"/>) and read back - and re-checked - on the next launch, so a
    /// player who goes offline keeps the tuning they last saw; a device that has never read one
    /// plays <see cref="ShuffleTuning.Shipped"/>. The cache is device-local and in no save: the
    /// tuning is the same for every account, and the save's wire (invariant 12a) is no place for
    /// a fact about the deployment.
    /// </para>
    /// <para>
    /// <b>A bad document changes nothing.</b> One the device cannot read whole
    /// (<see cref="ShuffleTuning.TryRead"/>) is refused: the device keeps what it had, says so
    /// once to analytics (<c>shuffle_tuning_refused</c>, with the fault) and to the log, and asks
    /// again on the normal cadence. A cached document the build can no longer read is dropped
    /// for the build's own figures. <b>Deleting the document is the stop</b>: a successful read
    /// that finds nothing published returns every device to the build's own figures and clears
    /// its cache - <c>config/release</c>'s rule for a wall, asked of a tuning.
    /// </para>
    /// </summary>
    public static class ShuffleConfig
    {
        /// <summary>The device-local key holding the last good document's body.</summary>
        public const string CacheKey = "glimmer.shuffle.tuning";

        /// <summary>The longest a read is trusted before the hub asks again.</summary>
        public const long StaleSeconds = 15 * 60;

        /// <summary>How soon a failed read may be retried. Short, but not every frame.</summary>
        public const long RetrySeconds = 60;

        public const string RefusedEvent = "shuffle_tuning_refused";

        static ShuffleTuning _current;
        static string _body;
        static bool _loaded;
        static bool _attached;
        static bool _launchRead;
        static string _refusedBody;
        static Task _inflight;
        static long _nextFetch;
        static EndlessCoins.ITallyStore _store = new PrefsStore();

        /// <summary>
        /// The tuning the next run opens on. Immediate, never null, never throws: the last good
        /// published document this device has read, else <see cref="ShuffleTuning.Shipped"/>.
        /// </summary>
        public static ShuffleTuning Current
        {
            get
            {
                try
                {
                    Load();
                    return _current ?? ShuffleTuning.Shipped;
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogException(e);
                    return ShuffleTuning.Shipped;
                }
            }
        }

        /// <summary>
        /// Hooks the launch read: the first settled sync of the process asks once. Idempotent;
        /// called from <c>Boot</c>.
        /// </summary>
        public static void Attach()
        {
            if (_attached) return;
            _attached = true;

            CloudSaveService.Settled += OnSettled;
        }

        static void OnSettled(SyncReceipt receipt)
        {
            if (_launchRead || !receipt.IsValid) return;
            _launchRead = true;

            Refresh();
        }

        /// <summary>
        /// Reads the published document if the last read is older than <see cref="StaleSeconds"/>
        /// (or failed more than <see cref="RetrySeconds"/> ago). Returns at once; the read runs
        /// beside play and only ever changes what the <em>next</em> run opens on.
        /// </summary>
        public static void Refresh()
        {
            try
            {
                Load();

                if (_inflight != null && !_inflight.IsCompleted) return;
                if (_nextFetch != 0 && Monotonic() < _nextFetch) return;

                var backend = Backend();
                if (backend == null) return;

                _inflight = FetchAsync(backend);
            }
            catch (Exception e)
            {
                // Nothing a read can do may reach the hub that asked for it.
                UnityEngine.Debug.LogException(e);
            }
        }

        /// <summary>The backend to read with, or null when nobody is signed in to read with.</summary>
        static IShuffleTuningBackend Backend()
        {
            if (!CloudSaveService.IsAvailable) return null;

            var backend = CloudSaveService.Backend;
            if (!(backend is IShuffleTuningBackend tuning)) return null;

            return string.IsNullOrEmpty(backend.CurrentIdentity.UserId) ? null : tuning;
        }

        static async Task FetchAsync(IShuffleTuningBackend backend)
        {
            try
            {
                var (result, body) = await backend.ReadShuffleTuningAsync(ShuffleTuning.Schema);

                if (!result.Ok)
                {
                    _nextFetch = Monotonic() + RetrySeconds;
                    return;
                }

                _nextFetch = Monotonic() + StaleSeconds;
                Adopt(body);
            }
            catch (Exception e)
            {
                _nextFetch = Monotonic() + RetrySeconds;
                UnityEngine.Debug.LogException(e);
            }
        }

        /// <summary>
        /// Takes what a successful read found: null for nothing published (back to the build's own
        /// figures), else a body that is adopted whole or refused whole. Answers whether the
        /// tuning the next run opens on changed. The seam a fixture drives.
        /// </summary>
        internal static bool Adopt(string body)
        {
            Load();

            if (string.IsNullOrEmpty(body))
            {
                bool had = _body != null;

                _current = ShuffleTuning.Shipped;
                _body = null;
                _store.Delete(CacheKey);

                return had;
            }

            if (body == _body) return false;

            if (!ShuffleTuning.TryRead(body, false, out var tuning, out string fault))
            {
                // Said once per document, not once per read: the same bad document is read every
                // quarter hour by every device until somebody publishes a good one.
                if (body != _refusedBody)
                {
                    _refusedBody = body;
                    UnityEngine.Debug.LogWarning($"[Shuffle] the published tuning was refused, keeping revision {Current.Revision}: {fault}");
                    Telemetry.Track(RefusedEvent, "fault", Clip(fault), "kept", Current.Revision);
                }

                return false;
            }

            _current = tuning;
            _body = body;
            _store.Write(CacheKey, body);

            return true;
        }

        /// <summary>Reads the cached document once per process, keeping it only if this build can still read it whole.</summary>
        static void Load()
        {
            if (_loaded) return;
            _loaded = true;

            string cached = _store.Read(CacheKey);
            if (string.IsNullOrEmpty(cached)) return;

            if (ShuffleTuning.TryRead(cached, false, out var tuning, out string fault))
            {
                _current = tuning;
                _body = cached;
                return;
            }

            // Written by a build whose rules were not these. The build's own figures, and the
            // next read replaces it with what this build can read.
            UnityEngine.Debug.LogWarning($"[Shuffle] the cached tuning no longer reads and was dropped: {fault}");
            _store.Delete(CacheKey);
        }

        static string Clip(string text)
            => string.IsNullOrEmpty(text) || text.Length <= 100 ? text : text.Substring(0, 100);

        static long Monotonic() => Stopwatch.GetTimestamp() / Stopwatch.Frequency;

        // ------------------------------------------------------------------ the store
        /// <summary>The game's store: a compared, flushed preferences write (<see cref="DevicePrefs"/>).</summary>
        sealed class PrefsStore : EndlessCoins.ITallyStore
        {
            public string Read(string key) => UnityEngine.PlayerPrefs.GetString(key, string.Empty);
            public void Write(string key, string value) => DevicePrefs.WriteString(key, value);

            public void Delete(string key)
            {
                if (!UnityEngine.PlayerPrefs.HasKey(key)) return;
                UnityEngine.PlayerPrefs.DeleteKey(key);
                UnityEngine.PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// Installs a store and forgets everything held, as a fresh process would; the game's
        /// own store for null. For fixtures, which must never reach the native one.
        /// </summary>
        internal static void UseStore(EndlessCoins.ITallyStore store)
        {
            _store = store ?? new PrefsStore();
            _current = null;
            _body = null;
            _loaded = false;
            _refusedBody = null;
            _inflight = null;
            _nextFetch = 0;
        }
    }
}
