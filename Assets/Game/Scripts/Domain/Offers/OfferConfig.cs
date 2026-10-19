using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Analytics;
using GlimmerGrove.Cloud;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Offers
{
    /// <summary>
    /// The one read the offer tuning needs: the public document, once in a while, never listened to.
    /// Implemented by the Firebase backend; a backend without it leaves the device on what it holds.
    /// </summary>
    public interface IOfferTuningBackend
    {
        /// <summary>
        /// The published body for <paramref name="schema"/>, or null when nothing is published for it.
        /// Only a failure to reach the database fails, so "nothing published" and "did not ask" differ.
        /// </summary>
        Task<(CloudResult result, string body)> ReadOfferTuningAsync(int schema, CancellationToken cancellation = default);
    }

    /// <summary>
    /// <b>Which offer tuning the engine deals by</b> (invariant 64): the latest good published one this
    /// device has read, else <see cref="OfferTuning.Shipped"/>. <c>ShuffleConfig</c>'s shape (MODES.md
    /// 59l), for its reasons: <see cref="Current"/> answers at once and never throws; the read happens
    /// beside play - once after the first settled sync of a launch, then at most every
    /// <see cref="StaleSeconds"/> while a calm screen asks (<see cref="Refresh"/>) - and is never a
    /// listener (a listener bills every open client on every write). The last good document is kept on
    /// the device, a bad one is refused whole and said once (<see cref="RefusedEvent"/>), and a
    /// successful read finding nothing published returns the device to the build's own tuning.
    /// </summary>
    public static class OfferConfig
    {
        public const string CacheKey = "glimmer.offers.tuning";
        public const long StaleSeconds = 15 * 60;
        public const long RetrySeconds = 60;
        public const string RefusedEvent = "offer_tuning_refused";

        static OfferTuning _current;
        static string _body;
        static bool _loaded, _attached, _launchRead;
        static string _refusedBody;
        static Task _inflight;
        static long _nextFetch;
        static EndlessCoins.ITallyStore _store = new PrefsStore();

        /// <summary>The tuning offers are dealt by. Immediate, never null, never throws.</summary>
        public static OfferTuning Current
        {
            get
            {
                try
                {
                    Load();
                    return _current ?? OfferTuning.Shipped;
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogException(e);
                    return OfferTuning.Shipped;
                }
            }
        }

        /// <summary>Hooks the launch read. Idempotent; called from <c>Boot</c>.</summary>
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

        /// <summary>Reads the published document if the last read is stale. Returns at once.</summary>
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
                UnityEngine.Debug.LogException(e);
            }
        }

        static IOfferTuningBackend Backend()
        {
            if (!CloudSaveService.IsAvailable) return null;
            var backend = CloudSaveService.Backend;
            if (!(backend is IOfferTuningBackend tuning)) return null;
            return string.IsNullOrEmpty(backend.CurrentIdentity.UserId) ? null : tuning;
        }

        static async Task FetchAsync(IOfferTuningBackend backend)
        {
            try
            {
                var (result, body) = await backend.ReadOfferTuningAsync(OfferTuning.Schema);
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
        /// Takes what a successful read found: null for nothing published (the build's own tuning),
        /// else a body adopted whole or refused whole. Answers whether the tuning changed.
        /// </summary>
        internal static bool Adopt(string body)
        {
            Load();

            if (string.IsNullOrEmpty(body))
            {
                bool had = _body != null;
                _current = OfferTuning.Shipped;
                _body = null;
                _store.Delete(CacheKey);
                return had;
            }

            if (body == _body) return false;

            if (!OfferTuning.TryRead(body, false, out var tuning, out string fault))
            {
                if (body != _refusedBody)
                {
                    _refusedBody = body;
                    UnityEngine.Debug.LogWarning($"[Offers] the published tuning was refused, keeping revision {Current.Revision}: {fault}");
                    Telemetry.Track(RefusedEvent, "fault", Clip(fault), "kept", Current.Revision);
                }
                return false;
            }

            _current = tuning;
            _body = body;
            _store.Write(CacheKey, body);
            return true;
        }

        static void Load()
        {
            if (_loaded) return;
            _loaded = true;

            string cached = _store.Read(CacheKey);
            if (string.IsNullOrEmpty(cached)) return;

            if (OfferTuning.TryRead(cached, false, out var tuning, out string fault))
            {
                _current = tuning;
                _body = cached;
                return;
            }

            UnityEngine.Debug.LogWarning($"[Offers] the cached tuning no longer reads and was dropped: {fault}");
            _store.Delete(CacheKey);
        }

        static string Clip(string text)
            => string.IsNullOrEmpty(text) || text.Length <= 100 ? text : text.Substring(0, 100);

        static long Monotonic() => Stopwatch.GetTimestamp() / Stopwatch.Frequency;

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

        /// <summary>Installs a store and forgets everything held; the game's own for null. For fixtures.</summary>
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
