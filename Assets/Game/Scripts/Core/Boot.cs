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
            Save.Load();
            Audio.Boot(root.transform);
            Flow.Init(canvas);
            root.AddComponent<Pump>();

            Flow.Go<SplashScreen>(instant: true);
        }

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

        /// <summary>Routes the hardware back button into the screen stack.</summary>
        sealed class Pump : MonoBehaviour
        {
            void Update()
            {
                if (Input.GetKeyDown(KeyCode.Escape)) Flow.HandleBack();
            }
        }
    }
}
