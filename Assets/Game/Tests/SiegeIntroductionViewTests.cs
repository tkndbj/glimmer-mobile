using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Modes;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GlimmerGrove.Tests
{
    /// <summary>Native layout, input, pause and restart checks against the actual scene widgets.</summary>
    public sealed class SiegeIntroductionViewTests
    {
        sealed class PreviewArt : IAssetProvider
        {
            public string Name => "firstwatch-preview";
            public bool IsAsynchronous => false;
            public T Load<T>(string address) where T : Object
            {
                string extension = typeof(T) == typeof(Font) ? ".ttf"
                                 : typeof(T) == typeof(AudioClip) ? ".wav" : ".png";
                return AssetDatabase.LoadAssetAtPath<T>("Assets/Game/" + address + extension);
            }
            public T[] LoadAll<T>(string address) where T : Object
            {
                string folder = "Assets/Game/" + address;
                if (!Directory.Exists(folder)) return Array.Empty<T>();
                var paths = Directory.GetFiles(folder, "*.png");
                Array.Sort(paths, StringComparer.Ordinal);
                var frames = new List<T>(paths.Length);
                foreach (var path in paths)
                {
                    var frame = AssetDatabase.LoadAssetAtPath<T>(path.Replace('\\', '/'));
                    if (frame != null) frames.Add(frame);
                }
                return frames.ToArray();
            }
            public Task<T> LoadAsync<T>(string address, CancellationToken cancellation) where T : Object
                => Task.FromResult(Load<T>(address));
            public Task<T[]> LoadAllAsync<T>(string address, CancellationToken cancellation) where T : Object
                => Task.FromResult(LoadAll<T>(address));
            public void Release(IEnumerable<string> addresses) { }
        }

        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        readonly List<GameObject> _roots = new List<GameObject>();
        IAssetProvider _provider;
        object _active, _fallback;
        UnityEngine.Random.State _random;

        [SetUp]
        public void SetUp()
        {
            _random = UnityEngine.Random.state;
            _provider = AssetLibrary.Provider;
            AssetLibrary.UseProvider(new PreviewArt());
            var flags = BindingFlags.Static | BindingFlags.NonPublic;
            _active = typeof(Loc).GetField("_active", flags).GetValue(null);
            _fallback = typeof(Loc).GetField("_fallback", flags).GetValue(null);
            var table = LocTable.Parse(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,
                                                                     "Content/loc/en.json")), out var error);
            Assert.IsNull(error);
            typeof(Loc).GetField("_active", flags).SetValue(null, table);
            typeof(Loc).GetField("_fallback", flags).SetValue(null, table);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var root in _roots)
            {
                if (!root) continue;
                foreach (var view in root.GetComponentsInChildren<SiegeView>(true)) Tween.KillAll(view);
                Object.DestroyImmediate(root);
            }
            _roots.Clear();
            var flags = BindingFlags.Static | BindingFlags.NonPublic;
            typeof(Loc).GetField("_active", flags).SetValue(null, _active);
            typeof(Loc).GetField("_fallback", flags).SetValue(null, _fallback);
            AssetLibrary.UseProvider(_provider);
            UnityEngine.Random.state = _random;
        }

        SiegeView Build(float wide = 1080f, float tall = 2340f, bool introduction = true)
        {
            var root = new GameObject("Firstwatch fixture", typeof(RectTransform), typeof(Canvas))
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            _roots.Add(root);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(wide, tall);
            var sky = UIKit.Img("Sky", rect, AssetLibrary.Sprite(AssetManifest.Backdrop("sky_00")), Color.white);
            sky.preserveAspect = false;
            var host = UIKit.Box("Board", rect, new Vector2(wide - 48f, tall - 540f),
                                  new Vector2(.5f, .5f), Vector2.zero);
            var problems = new List<string>();
            Assert.IsTrue(ContentMapper.TryReadChapter(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,
                                      "Content/chapters/s01_thornwatch.json")), problems, out var chapter),
                                      string.Join("\n", problems));
            var rules = (SiegeRules)chapter.Levels[introduction ? 0 : 1].Rules;
            var view = host.gameObject.AddComponent<SiegeView>();
            view.Begin(host, rules, 0);
            // In EditMode Screen describes the Editor's Game View, not this offscreen viewport.
            // Recompose with the shipping fit function's result for the requested device.
            float cell = Mathf.Floor(SiegeView.CellFor(host.rect.size, 1080f / wide, 8, 5));
            var cellField = typeof(ProtoView).GetField("<Cell>k__BackingField", Private);
            if (Mathf.Abs((float)cellField.GetValue(view) - cell) > .01f)
            {
                var field = host.Find("Plate/Field");
                for (int i = field.childCount - 1; i >= 0; i--) Object.DestroyImmediate(field.GetChild(i).gameObject);
                cellField.SetValue(view, cell);
                typeof(SiegeView).GetMethod("Compose", Private).Invoke(view, null);
            }
            view.Held = false;
            var plate = host.Find("Plate");
            plate.localScale = Vector3.one;
            plate.GetComponent<CanvasGroup>().alpha = 1f;
            return view;
        }

        static object Scene(SiegeView view)
            => typeof(SiegeView).GetField("_introduction", Private).GetValue(view);
        static T Read<T>(object scene, string field)
            => (T)scene.GetType().GetField(field).GetValue(scene);
        static Func<float, bool> Clock(SiegeView view)
            => (Func<float, bool>)Delegate.CreateDelegate(typeof(Func<float, bool>), view,
                typeof(SiegeView).GetMethod("TickIntroduction", Private));

        [Test]
        public void TheOpeningHoldsCombatThenHandsBackTheGemField()
        {
            var view = Build();
            var tick = Clock(view);
            Assert.IsFalse(view.AtRest);
            for (int i = 0; i < 320; i++) tick(1f / 60f);
            Assert.IsFalse(view.AtRest);
            for (int i = 0; i < 8; i++) tick(1f / 60f);
            Assert.IsTrue(view.AtRest);
            Assert.IsTrue(view.Playable);
            Assert.IsFalse(Read<Image>(Scene(view), "Veil").raycastTarget);
            Assert.IsFalse(Read<GraphicRaycaster>(Scene(view), "Raycaster").enabled);
            Assert.AreEqual(0, view.Siege.Wave, "the scene must not advance the combat model itself");
        }

        [Test]
        public void PauseFreezesTheEntranceAndCannotConsumeItOnResume()
        {
            var view = Build();
            var tick = Clock(view);
            tick(.1f);
            var age = Read<float>(Scene(view), "Age");
            view.Held = true;
            for (int i = 0; i < 600; i++) tick(.1f);
            Assert.AreEqual(age, Read<float>(Scene(view), "Age"));
            view.Held = false;
            tick(20f);
            Assert.Less(Read<float>(Scene(view), "Age"), .3f, "a resumed frame cannot skip the entrance");
        }

        [Test]
        public void RestartWithdrawsTheOldSceneAndReplaysAFreshEntrance()
        {
            var view = Build();
            var old = Read<RectTransform>(Scene(view), "Root");
            var rules = (ProtoLevelRules)typeof(ProtoView).GetProperty("Rules", Private).GetValue(view);
            // Begin deliberately uses deferred destruction at runtime. The Editor reports it
            // because this fixture invokes the same path without entering PlayMode.
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Plate: Destroy may not be called from edit mode"));
            view.Begin(view.GetComponent<RectTransform>(), rules, 0);
            Assert.IsFalse(old.gameObject.activeSelf);
            Assert.AreNotSame(old, Read<RectTransform>(Scene(view), "Root"));
            Assert.AreEqual(0f, Read<float>(Scene(view), "Age"));
            Assert.IsFalse(view.AtRest);
        }

        [Test]
        public void AnOrdinaryLevelCreatesNoCinematicWidgets()
        {
            var view = Build(introduction: false);
            Assert.IsNull(Scene(view));
            Assert.IsTrue(view.AtRest);
            Assert.IsFalse(Clock(view)(.1f));
            Assert.IsNotNull(typeof(SiegeView).GetField("_forecastGroup", Private).GetValue(view),
                             "ordinary levels keep their between-wave forecast");
        }

        [Test]
        public void FirstwatchHasNoBlackLayerOrMiddleWaveCaptions()
        {
            var view = Build();
            var tick = Clock(view);
            for (int i = 0; i < 150; i++) tick(1f / 60f);
            Assert.AreEqual(0f, Read<Image>(Scene(view), "Veil").color.a);
            foreach (var image in view.GetComponentsInChildren<Image>(true))
                Assert.AreNotEqual("Cinema matte", image.name);
            Assert.IsNull(typeof(SiegeView).GetField("_forecastGroup", Private).GetValue(view));

            var arrival = typeof(SiegeView).GetMethod("Arrival", Private);
            arrival.Invoke(view, new object[] { 0 });
            arrival.Invoke(view, new object[] { 1 });
            var caption = (Text)typeof(SiegeView).GetField("_waveLabel", Private).GetValue(view);
            Assert.IsTrue(string.IsNullOrEmpty(caption.text));
        }

        [Test]
        public void WinningBossReturnsIntoTheOpenPortalBeforeTheCelebration()
        {
            var view = Build();
            var tick = Clock(view);
            for (int i = 0; i < 360; i++) tick(1f / 60f);
            var scene = Scene(view);
            var ending = (IEnumerator)typeof(SiegeView).GetMethod("IntroductionTriumph", Private).Invoke(view, null);
            Assert.IsTrue(ending.MoveNext());
            var retreat = scene.GetType().GetField("Retreat");
            var draw = typeof(SiegeView).GetMethod("DrawIntroduction", Private);
            var actor = Read<RectTransform>(scene, "Actor");
            var target = Read<Vector2>(scene, "At");
            float from = Vector2.Distance(Read<Vector2>(scene, "Stand"), target);

            retreat.SetValue(scene, 2.1f);
            draw.Invoke(view, new[] { scene });
            Assert.Less(Vector2.Distance(actor.anchoredPosition, target), from * .75f);
            Assert.Greater(Read<CanvasGroup>(scene, "ActorAlpha").alpha, .9f,
                           "the boss travels visibly rather than fading out where it stood");
            Capture(view.transform.root.GetComponent<RectTransform>(), 1080, 2340, "returning");

            retreat.SetValue(scene, 2.7f);
            draw.Invoke(view, new[] { scene });
            Assert.Less(Vector2.Distance(actor.anchoredPosition, target), .01f);
            Assert.Less(Read<CanvasGroup>(scene, "ActorAlpha").alpha, .001f);
            var portal = Read<object>(scene, "Portal");
            Assert.Greater(Read<RectTransform>(portal, "Under").localScale.x, .9f,
                           "the portal cannot close before the boss reaches it");
            Assert.AreEqual(0f, Read<Image>(scene, "Veil").color.a);

            retreat.SetValue(scene, 3.3f);
            draw.Invoke(view, new[] { scene });
            Assert.AreEqual(Vector3.zero, Read<RectTransform>(portal, "Under").localScale);
            retreat.SetValue(scene, SiegeIntroduction.RetreatFor);
            Assert.IsTrue(ending.MoveNext());
            Assert.IsFalse(Read<RectTransform>(scene, "Root").gameObject.activeSelf);
            Assert.IsInstanceOf<IEnumerator>(ending.Current, "the normal celebration follows the portal's departure");
        }

        [Test]
        public void SteadyCinematicFramesAllocateNothingAndCreateNoWidgets()
        {
            var view = Build();
            var tick = Clock(view);
            for (int i = 0; i < 120; i++) tick(.1f);
            int widgets = view.GetComponentsInChildren<Transform>(true).Length;
            var random = UnityEngine.Random.state;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 600; i++) tick(1f / 60f);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0L, allocated);
            Assert.AreEqual(widgets, view.GetComponentsInChildren<Transform>(true).Length);
            Assert.AreEqual(random, UnityEngine.Random.state, "cinema must not consume gameplay randomness");
        }

        [TestCase(1080, 1920)]
        [TestCase(1080, 2340)]
        [TestCase(1620, 2160)]
        public void SpeechFitsTheBoardAndNativeFramesCanBeReviewed(int wide, int tall)
        {
            var view = Build(wide, tall);
            var tick = Clock(view);
            for (int i = 0; i < 150; i++) tick(1f / 60f);
            Canvas.ForceUpdateCanvases();
            var scene = Scene(view);
            var portalAt = Read<Vector2>(scene, "At");
            var bossAt = Read<Vector2>(scene, "Stand");
            Assert.Less(bossAt.y, portalAt.y, "the boss must deploy below the portal");
            float foot = (float)typeof(SiegeView).GetField("_hillFoot", Private).GetValue(view);
            float top = (float)typeof(SiegeView).GetField("_hillTop", Private).GetValue(view);
            Assert.Greater(portalAt.y, Mathf.Lerp(foot, top, .65f));
            var bubble = Read<RectTransform>(scene, "Bubble");
            var speech = Read<Text>(scene, "Speech");
            Assert.LessOrEqual(speech.preferredHeight, speech.rectTransform.rect.height + 1f);
            var corners = new Vector3[4];
            bubble.GetWorldCorners(corners);
            var host = view.GetComponent<RectTransform>();
            foreach (var corner in corners)
                Assert.IsTrue(host.rect.Contains(host.InverseTransformPoint(corner)), "speech left the board");
            Assert.IsNotNull(Read<Image>(scene, "Body").sprite);
            var bodyCorners = new Vector3[4];
            Read<Image>(scene, "Body").rectTransform.GetWorldCorners(bodyCorners);
            Assert.Less(bodyCorners[2].x, corners[0].x, "speech must not cover the boss on short displays");
            Capture(view.transform.root.GetComponent<RectTransform>(), wide, tall, "taunt");
            for (int i = 0; i < 210; i++) tick(1f / 60f);
            view.Siege.Advance(1f / 60f);
            var raider = view.Siege.Raiders[0];
            var spawn = (Vector2)typeof(SiegeView).GetMethod("IntroductionPosition", Private)
                .Invoke(view, new object[] { raider, Vector2.zero });
            Assert.Greater(spawn.y, Mathf.Lerp(foot, top, .65f), "raiders must emerge on the upper battlefield");
            for (int i = 0; i < 180; i++)
            {
                view.Siege.Advance(1f / 60f);
                typeof(SiegeView).GetMethod("Follow", Private).Invoke(view, null);
                typeof(SiegeView).GetMethod("Charge", Private, null, Type.EmptyTypes, null).Invoke(view, null);
                tick(1f / 60f);
            }
            // Raider entrances use the runtime tween pump; EditMode has no Update to finish it.
            foreach (var group in view.GetComponentsInChildren<CanvasGroup>())
                if (group.name == "Raider")
                {
                    group.alpha = 1f;
                    group.transform.localScale = Vector3.one;
                }
            Capture(view.transform.root.GetComponent<RectTransform>(), wide, tall, "raiders");
        }

        static void Capture(RectTransform root, int wide, int tall, string beat)
        {
            var go = new GameObject("Firstwatch capture", typeof(Camera));
            var camera = go.GetComponent<Camera>();
            var target = new RenderTexture(wide / 2, tall / 2, 24);
            var previous = RenderTexture.active;
            Texture2D pixels = null;
            try
            {
                camera.orthographic = true;
                camera.orthographicSize = tall * .5f;
                camera.transform.position = new Vector3(0f, 0f, -2000f);
                camera.farClipPlane = 4000f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.03f, .05f, .10f);
                camera.targetTexture = target;
                root.GetComponent<Canvas>().worldCamera = camera;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../.utmp/firstwatch"));
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, $"{wide}x{tall}-{beat}.png"), pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(target);
                if (pixels) Object.DestroyImmediate(pixels);
            }
        }
    }
}
