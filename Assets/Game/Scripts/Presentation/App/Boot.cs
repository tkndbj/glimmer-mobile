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
        public const int RefWidth = 1080;
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
            }

            void OnApplicationPause(bool paused)
            {
                if (paused) Persist();
                else CloudSaveService.BeginSync();     // returning: pick up another device's work
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
