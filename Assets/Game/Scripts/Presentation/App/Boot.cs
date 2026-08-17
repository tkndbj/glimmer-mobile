using GlimmerGrove.Ads;
using GlimmerGrove.Analytics;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Cloud;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Entry point. The game builds its entire scene graph in code, so any scene
    /// (even an empty one) boots straight into Glimmer Grove.
    /// </summary>
    public static class Boot
    {
        /// <summary>
        /// Taken from <see cref="ChapterMap.Width"/> rather than written again here.
        /// The map validator measures glade collisions against that number, so the two
        /// have to be the same one: a canvas widened without it would leave the gate
        /// passing layouts that overlap on a real screen.
        /// </summary>
        public const int RefWidth = (int)ChapterMap.Width;

        public const int RefHeight = 1920;

        static bool _started;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Launch()
        {
            if (_started) return;
            _started = true;

            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            UnityEngine.Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Input.multiTouchEnabled = false;

            var root = new GameObject("Glimmer Grove");
            Object.DontDestroyOnLoad(root);

            EnsureCamera(root.transform);
            EnsureEventSystem(root.transform);

            var canvas = BuildCanvas(root.transform);

            // Progress first: the splash and every screen after it read from it, and
            // it is cheap enough to be worth having ready before anything draws.
            SaveService.Load();

            // Then settle any run the last launch never finished — a force-quit, a crash, a
            // flat battery. Immediately after the save loads, because the heart has to come
            // out of a wallet that exists, and before anything can start a new run, because
            // one marker can only describe one run. See RunGuard.
            RunGuard.Claim();

            ContentConfig.AppVersion = ParseBuildNumber(Application.version);

            // Asset delivery is chosen once, here, before anything loads. Everything
            // downstream goes through AssetLibrary and never learns which it got.
#if GLIMMER_ADDRESSABLES
            AssetLibrary.UseProvider(new AddressablesAssetProvider());
#endif

            // Cloud save is chosen the same way, and ships inert until a backend is
            // installed. The game is fully playable through the null one.
#if GLIMMER_FIREBASE
            CloudSaveService.UseBackend(new FirebaseCloudSaveBackend());
#endif

            // A name or a companion the player picked is worth a sync of its own rather
            // than waiting for the app to be backgrounded — which is the least reliable
            // moment there is to start a network call, since the process is being frozen
            // as it goes out. Hung on the event rather than called from the rename panel
            // so a second way to change either cannot forget it; the request is debounced
            // and retried, so this is cheap however often it fires. See SyncScheduler.
            Wallet.ProfileChanged += CloudSaveService.RequestSync;

            // Rewarded ads, chosen the same way and inert by the same default. Two gates,
            // not one: the SDK has to be compiled in *and* a real app key has to exist.
            // Without the second, LevelPlay would start, fail, and leave the game showing
            // offers that can never fill — which is worse than showing none.
#if GLIMMER_ADS
            if (AdConfig.IsConfigured)
            {
                var ads = new LevelPlayAdProvider(AdConfig.AppKey, AdConfig.AdUnits());
                RewardedAds.Install(ads);

                // Not awaited. Mediation start-up is a network round trip and nothing on
                // the splash depends on it; the offers simply become available when they
                // become available, which is what ReadinessChanged is for.
                _ = ads.InitializeAsync();
            }
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Telemetry.AddSink(new DebugAnalyticsSink());
#endif

            Audio.Boot(root.transform);
            Flow.Init(canvas);
            root.AddComponent<Pump>();

            // Content is loaded on the splash, where there is already a progress bar.
            Flow.Go<SplashScreen>(instant: true);
        }

        /// <summary>
        /// Turns "1.4.2" into 10402 so a chapter can require a minimum client without
        /// anyone having to keep a second version number in step by hand.
        /// </summary>
        static int ParseBuildNumber(string version)
        {
            if (string.IsNullOrEmpty(version)) return 1;

            var parts = version.Split('.');
            int major = SafeInt(parts.Length > 0 ? parts[0] : null);
            int minor = SafeInt(parts.Length > 1 ? parts[1] : null);
            int patch = SafeInt(parts.Length > 2 ? parts[2] : null);
            return major * 10000 + minor * 100 + patch;
        }

        static int SafeInt(string s) => int.TryParse(s, out int v) ? v : 0;

        static void EnsureCamera(Transform parent)
        {
            if (Camera.main != null) return;
            var go = new GameObject("MainCamera", typeof(Camera), typeof(AudioListener));
            go.tag = "MainCamera";
            go.transform.SetParent(parent, false);
            var cam = go.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Pal.Slate;
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.cullingMask = 0;
        }

        static void EnsureEventSystem(Transform parent)
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            go.transform.SetParent(parent, false);
        }

        static Canvas BuildCanvas(Transform parent)
        {
            var go = new GameObject("UICanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(parent, false);
            go.layer = LayerMask.NameToLayer("UI");

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = false;
            canvas.sortingOrder = 100;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;      // portrait: width is the constant
            scaler.referencePixelsPerUnit = 100f;

            return canvas;
        }

        /// <summary>
        /// Routes the hardware back button, and makes sure progress reaches disk.
        ///
        /// Being backgrounded is the last moment a mobile app is reliably told
        /// anything — Android may kill the process afterwards without another
        /// callback — so the save is flushed there rather than only on quit.
        /// </summary>
        sealed class Pump : MonoBehaviour
        {
            void Update()
            {
                if (Input.GetKeyDown(KeyCode.Escape)) Flow.HandleBack();

                // Connectivity is read here rather than inside the sync, because
                // Application is a Presentation type and the policy it feeds is meant to
                // be runnable in the test suite without one. Unscaled, since a paused game
                // is still a device that may have just found a signal.
                CloudSaveService.Tick(Time.unscaledDeltaTime,
                                      Application.internetReachability != NetworkReachability.NotReachable);
            }

            void OnApplicationPause(bool paused)
            {
                if (paused) Persist();
                else
                {
                    // Returning: pick up another device's work, and drop any backoff the
                    // last failure left. A player who reopens the game has quite often
                    // just done something about the connection.
                    CloudSaveService.ResetBackoff();
                    CloudSaveService.BeginSync();
                }
            }

            void OnApplicationFocus(bool focused)
            {
                if (!focused) Persist();
            }

            void OnApplicationQuit() => SaveService.Flush();

            /// <summary>
            /// Disk first, then the network. The write is synchronous and certain; the
            /// sync may not survive the process being killed a moment later, and must
            /// never be what the local save is waiting on.
            /// </summary>
            static void Persist()
            {
                SaveService.Flush();
                CloudSaveService.BeginSync();
            }
        }
    }
}
