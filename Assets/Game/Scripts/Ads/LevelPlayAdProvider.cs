#if GLIMMER_ADS
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Persistence;
using Unity.Services.LevelPlay;
using UnityEngine;

namespace GlimmerGrove.Ads
{
    /// <summary>
    /// The LevelPlay half of <see cref="IAdProvider"/>.
    ///
    /// <para>
    /// Compiled only when the mediation package is installed — <c>GLIMMER_ADS</c> comes
    /// from this assembly's <c>versionDefines</c>, never from Player Settings, for the
    /// reason <c>GLIMMER_ADDRESSABLES</c> already documents: a Player Settings define is
    /// per build target, so one added on Standalone is silently absent on Android and iOS.
    /// For an ad SDK that would mean a mobile build that compiles, ships, and earns nothing.
    /// </para>
    /// <para>
    /// Everything here is plumbing. No policy lives in this file — not the reward amounts,
    /// not the caps, not the cooldown, not whether an offer should be made. Those are
    /// <see cref="RewardedAds"/> and the content table, and they are testable without an
    /// SDK precisely because this class is the only thing that knows LevelPlay exists.
    /// Swapping to AdMob later is a new file beside this one and a line in <c>Boot</c>.
    /// </para>
    /// </summary>
    public sealed class LevelPlayAdProvider : IAdProvider
    {
        readonly Dictionary<string, LevelPlayRewardedAd> _units =
            new Dictionary<string, LevelPlayRewardedAd>(StringComparer.Ordinal);

        readonly Dictionary<string, string> _adUnitIds;

        /// <summary>Consecutive failed loads per placement, which drives the retry backoff.</summary>
        readonly Dictionary<string, int> _failures = new Dictionary<string, int>(StringComparer.Ordinal);

        TaskCompletionSource<bool> _ready;
        TaskCompletionSource<AdShowResult> _pending;
        AdImpression _showing;
        bool _starting;

        /// <summary>
        /// <paramref name="adUnitIds"/> maps our permanent placement ids onto the ad unit
        /// ids from the LevelPlay dashboard. Kept as a constructor argument rather than
        /// baked in because those ids differ per platform and per app, and a dashboard id
        /// is exactly the sort of value that should never be a literal in game logic.
        /// </summary>
        public LevelPlayAdProvider(string appKey, Dictionary<string, string> adUnitIds)
        {
            AppKey = appKey ?? string.Empty;
            _adUnitIds = adUnitIds ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }

        public string AppKey { get; }

        public bool IsInitialized { get; private set; }

        public event Action<string> ReadinessChanged;

        public bool IsReady(string placementId)
        {
            if (!IsInitialized || string.IsNullOrEmpty(placementId)) return false;
            return _units.TryGetValue(placementId, out var unit) && unit != null && unit.IsAdReady();
        }

        // ------------------------------------------------------------ starting
        public Task InitializeAsync(CancellationToken cancellation = default)
        {
            if (IsInitialized) return Task.CompletedTask;
            if (_starting) return _ready.Task;

            if (string.IsNullOrEmpty(AppKey))
            {
                Debug.LogWarning("[Ads] no LevelPlay app key configured; ads stay off");
                return Task.CompletedTask;
            }

            _starting = true;
            _ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            Debug.Log($"[Ads] starting LevelPlay, appKey '{AppKey}', uid '{CloudState.UserId}'");

            LevelPlay.OnInitSuccess += OnInitSuccess;
            LevelPlay.OnInitFailed += OnInitFailed;

            // The account id is passed at start-up when there is one, and refreshed before
            // every impression by ShowAsync — see the note there about sign-in landing late.
            LevelPlay.Init(AppKey, CloudState.UserId);

            return _ready.Task;
        }

        void OnInitSuccess(LevelPlayConfiguration configuration)
        {
            IsInitialized = true;

            Debug.Log($"[Ads] LevelPlay started; creating {_adUnitIds.Count} ad unit(s)");

            foreach (var pair in _adUnitIds) Create(pair.Key, pair.Value);

            _ready?.TrySetResult(true);
            Raise(string.Empty);
        }

        void OnInitFailed(LevelPlayInitError error)
        {
            Debug.LogWarning($"[Ads] LevelPlay failed to start: {error?.ErrorMessage} ({error?.ErrorCode})");

            _starting = false;
            _ready?.TrySetResult(false);
        }

        /// <summary>
        /// Builds one ad unit and starts it loading.
        ///
        /// Every unit reloads the moment it is finished with — shown, closed or failed —
        /// because a rewarded ad that is not preloaded is an offer the player taps and then
        /// waits ten seconds for, which is indistinguishable from a broken button.
        /// </summary>
        void Create(string placementId, string adUnitId)
        {
            if (string.IsNullOrEmpty(adUnitId))
            {
                Debug.LogWarning($"[Ads] placement '{placementId}' has no ad unit id for this platform");
                return;
            }

            var unit = new LevelPlayRewardedAd(adUnitId);

            unit.OnAdLoaded += _ =>
            {
                _failures[placementId] = 0;
                Debug.Log($"[Ads] loaded '{placementId}'");
                Raise(placementId);
            };

            unit.OnAdLoadFailed += error =>
            {
                int attempt = Fail(placementId);

                // Only the first few are logged. No fill is the ordinary state of an ad unit
                // in most of the world for most of the day, and once the backoff stretches
                // out there is nothing new to say — but during bring-up the difference
                // between "no demand" and "this ad unit id is wrong" is the only thing
                // anyone wants to know, and it is invisible without this.
                if (attempt <= 3)
                    Debug.Log($"[Ads] '{placementId}' did not load: {error?.ErrorMessage} ({error?.ErrorCode})");

                Raise(placementId);
                RetryLater(placementId, attempt);
            };

            unit.OnAdRewarded += (info, reward) => Settle(new AdShowResult(AdOutcome.Rewarded, _showing));

            unit.OnAdDisplayFailed += (info, error) =>
            {
                Settle(AdShowResult.Failed(AdOutcome.Error, _showing, error?.ErrorMessage));
                Reload(placementId);
            };

            unit.OnAdClosed += _ =>
            {
                // Only reaches a waiting caller when OnAdRewarded did not fire first, which
                // is exactly the definition of a player who closed the video early.
                Settle(AdShowResult.Failed(AdOutcome.Dismissed, _showing));
                Reload(placementId);
            };

            _units[placementId] = unit;
            unit.LoadAd();
        }

        void Reload(string placementId)
        {
            if (_units.TryGetValue(placementId, out var unit) && unit != null) unit.LoadAd();
        }

        /// <summary>Records a failed load and returns how many have now failed in a row.</summary>
        int Fail(string placementId)
        {
            int attempt = _failures.TryGetValue(placementId, out int held) ? held + 1 : 1;
            _failures[placementId] = attempt;
            return attempt;
        }

        /// <summary>
        /// Reloads after a delay that grows with the number of consecutive failures.
        ///
        /// <para>
        /// Reloading immediately is the obvious thing and it is wrong: an ad unit with no
        /// demand fails in about a third of a second, so a straight retry becomes three
        /// network round trips per second for as long as the app is open. That flattens a
        /// battery, and from the network's side it is indistinguishable from abuse — which
        /// is a good way to get an app rate-limited before it has ever shown an ad.
        /// </para>
        /// <para>
        /// Doubling from two seconds to a two-minute ceiling. The ceiling matters more than
        /// the curve: fill returns when a market wakes up or a waterfall is reconfigured,
        /// neither of which this client can see, so it has to keep asking — just not often.
        /// </para>
        /// </summary>
        async void RetryLater(string placementId, int attempt)
        {
            int seconds = attempt >= 6 ? MaxRetrySeconds : 1 << attempt;
            if (seconds > MaxRetrySeconds) seconds = MaxRetrySeconds;

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds));

                // A load may have succeeded, or the unit been replaced, while we waited.
                if (_failures.TryGetValue(placementId, out int held) && held == 0) return;

                Reload(placementId);
            }
            catch (Exception e)
            {
                // async void swallows exceptions, and an ad unit that silently stops
                // retrying is a feature that silently stops existing.
                Debug.LogException(e);
            }
        }

        const int MaxRetrySeconds = 120;

        // ------------------------------------------------------------- showing
        public Task<AdShowResult> ShowAsync(AdImpression impression, CancellationToken cancellation = default)
        {
            if (!impression.IsValid)
                return Task.FromResult(AdShowResult.Failed(AdOutcome.Error, impression, "invalid impression"));

            if (!IsInitialized)
                return Task.FromResult(AdShowResult.Failed(AdOutcome.Unavailable, impression, "not started"));

            if (!_units.TryGetValue(impression.PlacementId, out var unit) || unit == null)
                return Task.FromResult(AdShowResult.Failed(AdOutcome.Unavailable, impression, "no such placement"));

            if (!unit.IsAdReady())
            {
                Reload(impression.PlacementId);
                return Task.FromResult(AdShowResult.Failed(AdOutcome.NoFill, impression, "nothing loaded"));
            }

            // One at a time. Two overlapping shows would race for _pending and the second
            // player-visible reward would be attributed to the first impression's nonce.
            if (_pending != null && !_pending.Task.IsCompleted)
                return Task.FromResult(AdShowResult.Failed(AdOutcome.Error, impression, "an ad is already showing"));

            _showing = impression;
            _pending = new TaskCompletionSource<AdShowResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            // The line the whole grant path depends on. `[USER_ID]` in the network's
            // server-to-server callback is whatever was last set here, and it is how the
            // server knows whose wallet to pay. Set per impression rather than once at
            // start-up because anonymous sign-in can land *after* the SDK does, and an ad
            // watched in that window would otherwise be credited to an empty account id.
            var uid = CloudState.UserId;
            if (!string.IsNullOrEmpty(uid)) LevelPlay.SetDynamicUserId(uid);

            // The placement name travels to the callback as `[PLACEMENT_NAME]`, which is
            // how the server knows which payout to apply. Our permanent placement ids are
            // used verbatim, so the LevelPlay dashboard's placement names must match them.
            unit.ShowAd(impression.PlacementId);

            return _pending.Task;
        }

        /// <summary>
        /// Completes the waiting caller exactly once.
        ///
        /// The SDK fires more than one terminal event for a single view — a rewarded ad
        /// that pays raises <c>OnAdRewarded</c> and then <c>OnAdClosed</c> — so whichever
        /// arrives first decides, and the rest are dropped. Without this the reward would
        /// be overwritten by the dismissal that always follows it.
        /// </summary>
        void Settle(AdShowResult result)
        {
            var pending = _pending;
            if (pending == null) return;

            pending.TrySetResult(result);
        }

        void Raise(string placementId)
        {
            try { ReadinessChanged?.Invoke(placementId); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
#endif
