using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Modes;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace GlimmerGrove.Tests
{
    /// <summary>Runs the actual old and new emitters, then compares their rendered frames.</summary>
    public sealed class ShuffleFxTests
    {
        sealed class TestArt : IAssetProvider
        {
            public string Name => "shuffle-render-fixture";
            public bool IsAsynchronous => false;
            public T Load<T>(string address) where T : Object
                => AssetDatabase.LoadAssetAtPath<T>("Assets/Game/" + address + ".png");
            public T[] LoadAll<T>(string address) where T : Object => Array.Empty<T>();
            public Task<T> LoadAsync<T>(string address, CancellationToken cancellation) where T : Object
                => Task.FromResult(Load<T>(address));
            public Task<T[]> LoadAllAsync<T>(string address, CancellationToken cancellation) where T : Object
                => Task.FromResult(LoadAll<T>(address));
            public void Release(IEnumerable<string> addresses) { }
        }

        IAssetProvider _provider;
        Random.State _random;
        readonly List<GameObject> _roots = new List<GameObject>();
        readonly List<ShuffleFx> _renderers = new List<ShuffleFx>();

        [SetUp]
        public void SetUp()
        {
            _random = Random.state;
            _provider = AssetLibrary.Provider;
            AssetLibrary.UseProvider(new TestArt());
        }

        [TearDown]
        public void TearDown()
        {
            // EditMode does not reliably deliver OnDestroy to components created in a test.
            foreach (var renderer in _renderers)
                if (renderer != null) typeof(ShuffleFx).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(renderer, null);
            foreach (var root in _roots) if (root != null) Object.DestroyImmediate(root);
            _roots.Clear();
            _renderers.Clear();
            AssetLibrary.UseProvider(_provider);
            Random.state = _random;
        }

        RectTransform Root(string name)
        {
            var root = new GameObject(name, typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            _roots.Add(root);
            var rt = root.GetComponent<RectTransform>();
            rt.sizeDelta = Vector2.one * 256f;
            return rt;
        }

        ShuffleFx Attach(RectTransform field, bool atlas = true, bool gpu = true)
        {
            var fx = ShuffleFx.Attach(field, atlas, gpu: gpu);
            _renderers.Add(fx);
            return fx;
        }

        SiegeView View(RectTransform field, ShuffleFx renderer)
        {
            var view = field.gameObject.AddComponent<SiegeView>();
            Set(typeof(ProtoView), view, "<Field>k__BackingField", field);
            Set(typeof(ProtoView), view, "<Cell>k__BackingField", 48f);
            Set(typeof(ProtoView), view, "<Width>k__BackingField", 4);
            Set(typeof(ProtoView), view, "<Height>k__BackingField", 4);
            Set(typeof(SiegeView), view, "_fx", UIKit.Node("Fx", field));
            Set(typeof(SiegeView), view, "_batchFx", renderer);
            return view;
        }

        static void Set(Type type, object target, string field, object value)
            => type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(target, value);

        static object Get(object target, string field)
            => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(target);

        static void Call(SiegeView view, string method, params object[] args)
        {
            foreach (var candidate in typeof(SiegeView).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (candidate.Name != method) continue;
                var parameters = candidate.GetParameters();
                if (parameters.Length != args.Length) continue;
                bool matches = true;
                for (int i = 0; i < args.Length; i++)
                    if (args[i] != null && !parameters[i].ParameterType.IsInstanceOfType(args[i])) matches = false;
                if (matches) { candidate.Invoke(view, args); return; }
            }
            throw new MissingMethodException(method);
        }

        static FxParticle Cinder() => new FxParticle
        {
            Motion = FxMotion.Cinder, At = new Vector2(5f, 8f), Size = Vector2.one * 20f,
            Tint = Pal.Gold, Velocity = new Vector2(40f, 70f), Duration = .5f,
        };

        [Test]
        public void OnlyShuffleBuildsTheRendererAndRebuildingClearsOldParticles()
        {
            var field = Root("Boundary");
            var view = View(field, null);
            var lane = ShuffleTests.Lane();
            var chapter = new SiegeLayout(lane.Grid, "rgby", "rgby", new[] { "rgby" }, null);
            var infinite = new SiegeLayout(lane.Grid, "rgby", "rgby", Array.Empty<string>(), null,
                                          endless: new SiegeEndless("rgby", 4, 20, .55f));
            foreach (var layout in new[] { chapter, infinite })
            {
                Set(typeof(SiegeView), view, "_layout", layout);
                Call(view, "BuildBatches");
                Assert.IsNull(Get(view, "_batchFx"));
                Assert.IsEmpty(field.GetComponents<ShuffleFx>());
            }
            Set(typeof(SiegeView), view, "_layout", lane);
            Call(view, "BuildBatches");
            var renderer = (ShuffleFx)Get(view, "_batchFx");
            _renderers.Add(renderer);
            renderer.Emit(field, Art.Spark(64), null, Cinder());
            Assert.AreEqual(1, renderer.ParticleCount);
            // Compose has a fresh field; emulate that without dealing the game or touching a save.
            var replacement = Root("Replacement");
            Set(typeof(ProtoView), view, "<Field>k__BackingField", replacement);
            Call(view, "BuildBatches");
            _renderers.Add((ShuffleFx)Get(view, "_batchFx"));
            Assert.AreEqual(0, renderer.ParticleCount);
        }

        [Test]
        public void InterveningImagesMaterialsAndParentsKeepTheirDrawingOrder()
        {
            var field = Root("Ordering");
            var fx = Attach(field, false);
            var sprite = Art.Spark(64);
            fx.Emit(field, sprite, null, Cinder());
            fx.Emit(field, sprite, null, Cinder());
            Assert.AreEqual(1, fx.ActivePages);
            var image = UIKit.Img("Between", field, sprite, Color.white, Vector2.one * 20f);
            fx.Emit(field, sprite, null, Cinder());
            Assert.AreEqual(2, fx.ActivePages);
            Assert.Less(image.transform.GetSiblingIndex(), field.GetChild(field.childCount - 1).GetSiblingIndex());
            fx.Emit(field, sprite, Additive.Material, Cinder());
            Assert.AreEqual(3, fx.ActivePages);
            var other = UIKit.Node("Other layer", field);
            fx.Emit(other, sprite, null, Cinder());
            Assert.AreEqual(4, fx.ActivePages);
            fx.Sample(Tween.Clock + 1f);
            Assert.AreEqual(0, fx.ActivePages);
            Assert.AreEqual(0, fx.ParticleCount);
            Assert.IsNotNull(image, "expiry must not remove an unrelated image");
        }

        [Test]
        public void AThousandParticlesReuseFourMeshPagesWithoutDroppingAny()
        {
            var field = Root("Stress");
            var fx = Attach(field, false);
            var sprite = Art.Spark(64);
            for (int round = 0; round < 10; round++)
            {
                for (int i = 0; i < 1000; i++) fx.Emit(field, sprite, null, Cinder());
                Assert.AreEqual(1000, fx.ParticleCount);
                Assert.AreEqual(4, fx.ActivePages);
                fx.Sample(Tween.Clock + 1f);
            }
            Assert.AreEqual(8, fx.CreatedPages, "the initial warm pages should cover every round");
            TestContext.WriteLine("1000 particles: 4 active mesh pages, 8 reusable pages allocated at setup.");
        }

        [Test]
        public void GpuAnimationDoesNotRebuildGeometryAsTimePasses()
        {
            var field = Root("Static GPU geometry");
            field.gameObject.AddComponent<Canvas>();
            var fx = Attach(field);
            fx.Emit(field, Art.Spark(64), null, Cinder());
            var page = field.GetComponentInChildren<FxBatchGraphic>();
            Assert.IsTrue(page.GpuAnimated, "the test must exercise the shader path");
            Assert.AreEqual(page.gameObject, page.canvas.gameObject, "each ordered run isolates canvas rebuilds");
            Assert.IsFalse(page.canvas.overrideSorting);
            Canvas.ForceUpdateCanvases();
            int builds = page.GeometryBuilds;
            for (int i = 1; i < 20; i++)
            {
                fx.Sample(Tween.Clock + i * .01f);
                Canvas.ForceUpdateCanvases();
            }
            Assert.AreEqual(builds, page.GeometryBuilds);
            fx.Emit(field, Art.Spark(64), null, Cinder());
            Canvas.ForceUpdateCanvases();
            Assert.Greater(page.GeometryBuilds, builds, "new records still upload their geometry");
        }

        [Test]
        public void WarmedEmissionAndSamplingAllocateNoManagedMemory()
        {
            var field = Root("Allocation");
            var fx = Attach(field, false);
            var sprite = Art.Spark(64);
            var particle = Cinder();
            for (int warm = 0; warm < 3; warm++)
            {
                for (int i = 0; i < 200; i++) fx.Emit(field, sprite, null, particle);
                fx.Sample(Tween.Clock + 1f);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int round = 0; round < 16; round++)
            {
                for (int i = 0; i < 200; i++) fx.Emit(field, sprite, null, particle);
                fx.Sample(Tween.Clock + .2f);
                fx.Sample(Tween.Clock + 1f);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated, "the warmed renderer must not allocate per particle/frame");
        }

        [Test]
        public void AnExceptionalBurstDoesNotLeaveAnUnboundedIdlePool()
        {
            var field = Root("Idle pool bound");
            var fx = Attach(field, false);
            var sprite = Art.Spark(64);
            for (int i = 0; i < 200; i++)
            {
                fx.Emit(field, sprite, null, Cinder());
                UIKit.Node("Ordering barrier", field);
            }
            Assert.AreEqual(200, fx.ParticleCount, "the bound must never drop active particles");
            Assert.AreEqual(200, fx.ActivePages);
            fx.Sample(Tween.Clock + 1f);
            Assert.AreEqual(0, fx.ActivePages);
            Assert.LessOrEqual(fx.IdlePages, ShuffleFx.MaxIdlePages);
        }

        [Test]
        public void RealSparkBurstsShareTheirMaskPageAndKeepEveryParticle()
        {
            if ((SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.Basic) == 0)
                Assert.Ignore("This graphics device uses the separately tested original-texture fallback.");
            var field = Root("Burst batching");
            var fx = Attach(field);
            var view = View(field, fx);
            for (int i = 0; i < 100; i++) EmitFixture(view, "FxSparks");
            Assert.AreEqual(900, fx.ParticleCount, "100 bursts must keep all eight sparks and the core");
            Assert.AreEqual(4, fx.ActivePages);
            Assert.AreEqual(8, fx.CreatedPages);
            TestContext.WriteLine("100 real eight-spark bursts: 900 particles, 4 active mesh pages; legacy emitter creates 1000 GameObjects.");
        }

        [TestCase(true, false)]
        [TestCase(true, true)]
        [TestCase(false, true)]
        public void ProjectileFramesHaloAndFlightMatchTheOriginalImages(bool gpu, bool masked)
        {
            var cameraRoot = Root("Projectile comparison camera");
            var camera = cameraRoot.gameObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 128f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.08f, .12f, .2f, 1f);
            camera.cullingMask = 1 << 31;
            var target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            var read = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            var contact = gpu && !masked ? new Texture2D(1024, 3072, TextureFormat.RGBA32, false) : null;
            var previousTarget = RenderTexture.active;
            try
            {
                int contactRow = 0;
                foreach (string reel in new[] { "shot_arcstorm_r", "shot_ember_r", "boom_burst" })
                foreach (int flightMode in new[] { 0, 1, 2, 3 })
                {
                    bool flying = flightMode != 0;
                    var paths = Directory.GetFiles("Assets/Game/Art/Fx/Siege/" + reel, "*.png");
                    Array.Sort(paths, StringComparer.Ordinal);
                    var frames = new Sprite[paths.Length];
                    for (int i = 0; i < paths.Length; i++) frames[i] = AssetDatabase.LoadAssetAtPath<Sprite>(paths[i].Replace('\\', '/'));
                    Assert.Greater(frames.Length, 1);
                    var legacyRoot = Root("Legacy projectile");
                    var batchRoot = Root("GPU projectile");
                    foreach (var root in new[] { legacyRoot, batchRoot })
                    {
                        var canvas = root.gameObject.AddComponent<Canvas>();
                        canvas.renderMode = RenderMode.WorldSpace;
                        canvas.worldCamera = camera;
                        if (masked)
                        {
                            root.gameObject.AddComponent<RectMask2D>().padding = Vector4.one * 40f;
                            root.gameObject.AddComponent<CanvasGroup>().alpha = .63f;
                        }
                    }
                    var legacy = View(legacyRoot, null);
                    var fx = Attach(batchRoot, gpu: gpu);
                    var batched = View(batchRoot, fx);
                    var lend = typeof(SiegeView).GetMethod("Lend", BindingFlags.Instance | BindingFlags.NonPublic);
                    var origin = flightMode == 3 ? new Vector2(-150f, -150f) : new Vector2(-35f, -40f);
                    const float angle = -28f, width = 50f;
                    var tint = new Color(.8f, .65f, .9f, .85f);
                    var arguments = new object[] { frames, tint, width, origin, angle, 30f, true, SiegeView.HeadAt };
                    var oldPuff = lend.Invoke(legacy, arguments);
                    var newPuff = lend.Invoke(batched, arguments);
                    var oldImage = (Image)Get(oldPuff, "Reel");
                    var book = (FxFlipbookGraphic)Get(newPuff, "Book");
                    Assert.AreEqual(gpu, book.GpuAnimated, "the requested rendering path must actually run");
                    typeof(SiegeView).GetMethod("Glow", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new[] { oldPuff, (object)Pal.Gold, 65f });
                    typeof(SiegeView).GetMethod("Glow", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new[] { newPuff, (object)Pal.Gold, 65f });
                    var oldNode = (RectTransform)Get(oldPuff, "Node");
                    var newNode = (RectTransform)Get(newPuff, "Node");
                    var travel = flightMode == 3 ? new Vector2(180f, 185f) : new Vector2(70f, 85f);
                    var side = new Vector2(-travel.y, travel.x).normalized * 48f;
                    float span = travel.magnitude, tall = oldImage.rectTransform.sizeDelta.y;
                    if (flying && gpu)
                    {
                        if (flightMode == 2) book.Throw(travel, side, angle, .6f, true);
                        else book.Fly(travel, angle, 1.15f, span, 48f, tall);
                    }
                    float phase = 0f;
                    SetLayer(legacyRoot);
                    SetLayer(batchRoot);
                    int builds = 0;
                    for (int step = 0; step < 24; step++)
                    {
                        if (step > 0)
                        {
                            // Include a hitch: the original Flipbook uses raw time, not Tween's clamp.
                            float raw = step == 10 ? .19f : 1f / 60f;
                            phase += raw * 30f;
                            fx.AdvanceBooks(raw);
                            oldImage.sprite = frames[Mathf.FloorToInt(phase) % frames.Length];
                        }
                        if (flying && step > 0)
                        {
                            float t = step / 24f;
                            oldNode.anchoredPosition = origin + travel * t;
                            oldNode.localScale = Vector3.one * (Mathf.Lerp(.86f, 1.12f, t) * 1.15f);
                            if (flightMode == 2)
                            {
                                oldNode.anchoredPosition += side * .6f * 1.4f * Mathf.Sin(t * Mathf.PI);
                                oldNode.anchoredPosition += side * .55f * Mathf.Sin(t * Mathf.PI * 2f) * (1f - t);
                                oldNode.localScale = Vector3.one * Mathf.Lerp(.8f, 1.25f, t);
                            }
                            else
                            {
                                oldImage.type = Image.Type.Filled;
                                oldImage.fillMethod = Image.FillMethod.Vertical;
                                oldImage.fillOrigin = (int)Image.OriginVertical.Top;
                                oldImage.fillAmount = SiegeView.Emerged(span * t, 48f, tall);
                            }
                            if (gpu) book.Progress(t);
                            else
                            {
                                newNode.anchoredPosition = oldNode.anchoredPosition;
                                newNode.localScale = oldNode.localScale;
                                book.type = oldImage.type;
                                book.fillMethod = oldImage.fillMethod;
                                book.fillOrigin = oldImage.fillOrigin;
                                book.fillAmount = oldImage.fillAmount;
                            }
                        }
                        book.SyncTransform();
                        var expected = Render(camera, read, legacyRoot, batchRoot);
                        var actual = Render(camera, read, batchRoot, legacyRoot);
                        AssertFrames(expected, actual, reel + " flight=" + flightMode + " step=" + step);
                        if (contact != null && step % 6 == 0)
                            contact.SetPixels32(step / 6 * 256, (11 - contactRow) * 256, 256, 256, actual);
                        if (step == 0) builds = book.GeometryBuilds;
                        else if (gpu) Assert.AreEqual(builds, book.GeometryBuilds, "shader frame changes and flight must not rebuild geometry");
                    }
                    // Pool reuse must reset the frame, tint, halo and any flight/crop state.
                    Call(batched, "Give", newPuff);
                    var rebound = lend.Invoke(batched, arguments);
                    Assert.AreSame(newPuff, rebound);
                    Assert.IsNull(book.GetComponent<Flipbook>());
                    Call(batched, "Give", rebound);
                    typeof(ShuffleFx).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(fx, null);
                    Object.DestroyImmediate(legacyRoot.gameObject);
                    Object.DestroyImmediate(batchRoot.gameObject);
                    contactRow++;
                }
                if (contact != null)
                {
                    string folder = Path.Combine(TestJson.RepoRoot(), "Tools", "out");
                    Directory.CreateDirectory(folder);
                    File.WriteAllBytes(Path.Combine(folder, "shuffle_projectiles.png"), contact.EncodeToPNG());
                }
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previousTarget;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(read);
                if (contact != null) Object.DestroyImmediate(contact);
            }
        }

        static void AssertFrames(Color32[] expected, Color32[] actual, string context)
        {
            long error = 0;
            int worst = 0, edges = 0;
            for (int i = 0; i < expected.Length; i++)
            {
                int delta = Math.Max(Math.Abs(expected[i].r-actual[i].r), Math.Max(Math.Abs(expected[i].g-actual[i].g), Math.Abs(expected[i].b-actual[i].b)));
                error += delta;
                worst = Math.Max(worst, delta);
                if (delta > 8) edges++;
            }
            if (error / (double)expected.Length > .15 || worst > 32 || edges > expected.Length * .0025)
            {
                var image = new Texture2D(512, 256, TextureFormat.RGBA32, false);
                image.SetPixels32(0, 0, 256, 256, expected);
                image.SetPixels32(256, 0, 256, 256, actual);
                string folder = Path.Combine(TestJson.RepoRoot(), "Tools", "out");
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, "shuffle_projectile_mismatch.png"), image.EncodeToPNG());
                Object.DestroyImmediate(image);
            }
            Assert.LessOrEqual(error / (double)expected.Length, .15, context);
            Assert.LessOrEqual(worst, 32, context);
            Assert.LessOrEqual(edges, expected.Length * .0025, context);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ProjectileCompletionDelayAndReuseDoNotLeaveStaleCallbacks(bool gpu)
        {
            var root = Root("Projectile lifecycle");
            root.gameObject.AddComponent<Canvas>();
            var fx = Attach(root, gpu: gpu);
            var view = View(root, fx);
            var sprite = Art.Glow(96);
            var frames = new[] { sprite, sprite, sprite };
            var lend = typeof(SiegeView).GetMethod("Lend", BindingFlags.Instance | BindingFlags.NonPublic);
            var arguments = new object[] { frames, Color.white, 25f, Vector2.zero, 0f, 30f, false, .5f };
            var puff = lend.Invoke(view, arguments);
            var book = (FxFlipbookGraphic)Get(puff, "Book");
            var node = (RectTransform)Get(puff, "Node");
            Call(view, "Ends", puff, .3f);
            var oldTimer = (Tw)Get(puff, "Timer");
            node.gameObject.SetActive(false);
            fx.AdvanceBooks(.5f);
            Assert.IsTrue((bool)Get(puff, "Out"), "an inactive delayed book cannot finish");
            node.gameObject.SetActive(true);
            fx.AdvanceBooks(.11f);
            Assert.IsFalse((bool)Get(puff, "Out"));
            Assert.IsFalse(oldTimer.Running, "frame completion cancels the backup timer directly");
            var rebound = lend.Invoke(view, arguments);
            Assert.AreSame(puff, rebound);
            Tween.Inst.Tick(.4f, .4f);
            Assert.IsTrue((bool)Get(puff, "Out"), "a returned borrow's timer must not return its replacement");
            Assert.IsNull(book.GetComponent<Flipbook>());
            Call(view, "Give", rebound);
        }

        [Test]
        public void CardProjectilesKeepTheirArrivalAndReuseIgnoresOldBackupTimers()
        {
            var root = Root("Card projectile lifecycle");
            var view = View(root, Attach(root));
            int landed = 0;
            Action arrival = () => landed++;
            var destination = new Vector2(60f, 80f);
            Call(view, "Fly", Vector2.zero, destination, Pal.Gold, 14f, .2f, .1f, Color.clear, arrival);
            RectTransform node = null;
            foreach (var child in root.GetComponentsInChildren<RectTransform>(true))
                if (child.name == "Dart") node = child;
            Assert.IsNotNull(node);
            Tween.Inst.Tick(.25f, .25f);
            Assert.AreEqual(1, landed);
            Assert.IsFalse(node.gameObject.activeSelf);
            Call(view, "Fly", Vector2.zero, destination, Pal.Gold, 14f, 1f, .1f, Color.clear, arrival);
            Tween.Inst.Tick(.25f, .25f);
            Assert.IsTrue(node.gameObject.activeSelf, "the previous borrow's backup timer cannot return this projectile");
            Assert.AreEqual(1, landed);
            Tween.Inst.Tick(.75f, .75f);
            Assert.AreEqual(2, landed);
            Assert.IsFalse(node.gameObject.activeSelf);
        }

        [Test]
        public void ExplosionReuseCannotChangeTheNormalProjectilesSiblingOrBorrow()
        {
            var root = Root("Separate projectile and explosion pools");
            root.gameObject.AddComponent<Canvas>();
            var fx = Attach(root);
            var view = View(root, fx);
            var frames = new[] { Art.Glow(96) };
            var lend = typeof(SiegeView).GetMethod("Lend", BindingFlags.Instance | BindingFlags.NonPublic);
            var arguments = new object[] { frames, Color.white, 25f, Vector2.zero, 0f, 30f, true, .5f };
            var round = lend.Invoke(view, arguments);
            var node = (RectTransform)Get(round, "Node");
            int sibling = node.GetSiblingIndex();
            Call(view, "Give", round);
            UIKit.Img("Existing overlapping effect", node.parent, Art.Pixel, Pal.Gold, Vector2.one * 30f);
            Call(view, "Boom", Vector2.zero, frames, 70f, 26f);
            fx.AdvanceBooks(.1f);
            var rebound = lend.Invoke(view, arguments);
            Assert.AreSame(round, rebound, "an explosion must not take a normal projectile's pool slot");
            Assert.AreEqual(sibling, node.GetSiblingIndex(), "the normal projectile retains its original layering");
            Call(view, "Give", rebound);
        }

        [Test]
        public void DirectRecoilAndDamageNumberHandlesPreserveTheExistingTweenStates()
        {
            var oldRoot = Root("Legacy tween channels");
            var newRoot = Root("Direct tween handles");
            var legacy = View(oldRoot, null);
            var batched = View(newRoot, Attach(newRoot));
            var tallyType = typeof(SiegeView).GetNestedType("Tally", BindingFlags.NonPublic);
            var postType = typeof(SiegeView).GetNestedType("Post", BindingFlags.NonPublic);
            var oldTally = Activator.CreateInstance(tallyType, true);
            var newTally = Activator.CreateInstance(tallyType, true);
            var oldPost = Activator.CreateInstance(postType, true);
            var newPost = Activator.CreateInstance(postType, true);
            var oldLabel = UIKit.Node("Number", oldRoot).gameObject.AddComponent<Text>();
            var newLabel = UIKit.Node("Number", newRoot).gameObject.AddComponent<Text>();
            var oldBody = UIKit.Img("Turret", oldRoot, Art.Pixel, Color.white, Vector2.one * 20f);
            var newBody = UIKit.Img("Turret", newRoot, Art.Pixel, Color.white, Vector2.one * 20f);
            Set(postType, oldPost, "Body", oldBody);
            Set(postType, newPost, "Body", newBody);
            Set(tallyType, oldTally, "Rt", oldLabel.rectTransform);
            Set(tallyType, newTally, "Rt", newLabel.rectTransform);
            Set(tallyType, oldTally, "Label", oldLabel);
            Set(tallyType, newTally, "Label", newLabel);
            Set(tallyType, oldTally, "Drift", .7f);
            Set(tallyType, newTally, "Drift", .7f);
            for (int frame = 0; frame < 18; frame++)
            {
                if (frame % 5 == 0)
                {
                    Call(legacy, "Kick", oldPost);
                    Call(batched, "Kick", newPost);
                    Call(legacy, "Punch", oldTally);
                    Call(batched, "Punch", newTally);
                    Call(legacy, "Rise", oldTally);
                    Call(batched, "Rise", newTally);
                }
                Tween.Inst.Tick(.02f, .02f);
                Assert.AreEqual(oldBody.rectTransform.anchoredPosition, newBody.rectTransform.anchoredPosition);
                Assert.AreEqual(oldBody.rectTransform.localScale, newBody.rectTransform.localScale);
                Assert.AreEqual(oldLabel.rectTransform.anchoredPosition, newLabel.rectTransform.anchoredPosition);
                Assert.AreEqual(oldLabel.rectTransform.localScale, newLabel.rectTransform.localScale);
                Assert.AreEqual(oldLabel.color, newLabel.color);
            }
        }

        [TestCase(false, false, false, 100f)]
        [TestCase(false, false, true, 100f)]
        [TestCase(true, false, true, 100f)]
        [TestCase(true, true, true, 100f)]
        [TestCase(true, false, true, 50f)]
        public void BatchedFramesMatchTheExistingEmittersAndConsumeTheSameRandomDraws(bool atlas, bool masked, bool gpu, float referencePixels)
        {
            var cameraRoot = new GameObject("FX comparison camera") { hideFlags = HideFlags.HideAndDontSave };
            _roots.Add(cameraRoot);
            var camera = cameraRoot.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 128f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.08f, .12f, .2f, 1f);
            camera.cullingMask = 1 << 31;
            var target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            var read = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            var methods = new[] { "FxSparks", "Cinder", "Struck", "Flash", "Ripple", "Embers", "Twinkles", "Lightup", "FxPop", "FxWave", "Mixed", "Boom" };
            var contact = new Texture2D(256 * 6, 256 * methods.Length, TextureFormat.RGBA32, false);
            var previousTarget = RenderTexture.active;
            try
            {
                int worst = 0;
                for (int row = 0; row < methods.Length; row++)
                {
                    var legacyRoot = Root("Legacy " + methods[row]);
                    var batchRoot = Root("Batch " + methods[row]);
                    foreach (var root in new[] { legacyRoot, batchRoot })
                    {
                        var canvas = root.gameObject.AddComponent<Canvas>();
                        canvas.renderMode = RenderMode.WorldSpace;
                        canvas.worldCamera = camera;
                        if (masked)
                        {
                            root.gameObject.AddComponent<RectMask2D>().padding = Vector4.one * 64f;
                            root.gameObject.AddComponent<CanvasGroup>().alpha = .63f;
                        }
                    }
                    var legacy = View(legacyRoot, null);
                    var fx = Attach(batchRoot, atlas, gpu);
                    legacyRoot.GetComponent<Canvas>().referencePixelsPerUnit = referencePixels;
                    batchRoot.GetComponent<Canvas>().referencePixelsPerUnit = referencePixels;
                    var batched = View(batchRoot, fx);
                    if (masked)
                        foreach (var view in new[] { legacy, batched })
                        {
                            var layer = (RectTransform)Get(view, "_fx");
                            layer.anchoredPosition = new Vector2(13f, -9f);
                            layer.localRotation = Quaternion.Euler(0f, 0f, 17f);
                            layer.localScale = new Vector3(.9f, 1.1f, 1f);
                        }
                    Random.InitState(19321);
                    EmitFixture(legacy, methods[row]);
                    float next = Random.value;
                    Random.InitState(19321);
                    EmitFixture(batched, methods[row]);
                    Assert.AreEqual(next, Random.value, methods[row] + " changed the shared visual random stream");
                    SetLayer(legacyRoot);
                    SetLayer(batchRoot);
                    int visible = 0;
                    int frameCount = methods[row] == "Embers" ? 21 : 6;
                    for (int frame = 0; frame < frameCount; frame++)
                    {
                        // Stop before legacy Destroy callbacks: those need PlayMode. Expiry
                        // and reuse are exercised separately without running the live game.
                        if (frame > 0)
                        {
                            Tween.Inst.Tick(.04f, .04f);
                            if (methods[row] == "Boom")
                            {
                                fx.AdvanceBooks(.04f);
                                var frames = Frames("boom_burst");
                                legacyRoot.GetComponentInChildren<Flipbook>().GetComponent<Image>().sprite = frames[Mathf.FloorToInt(frame * .04f * 26f)];
                            }
                        }
                        fx.Sample(Tween.Clock);
                        var expected = Render(camera, read, legacyRoot, batchRoot);
                        var actual = Render(camera, read, batchRoot, legacyRoot);
                        if (frame < 6) contact.SetPixels32(frame * 256, (methods.Length - row - 1) * 256, 256, 256, actual);
                        long error = 0;
                        int edgePixels = 0, frameWorst = 0;
                        for (int p = 0; p < expected.Length; p++)
                        {
                            int delta = Math.Max(Math.Abs(expected[p].r - actual[p].r),
                                        Math.Max(Math.Abs(expected[p].g - actual[p].g), Math.Abs(expected[p].b - actual[p].b)));
                            worst = Math.Max(worst, delta);
                            frameWorst = Math.Max(frameWorst, delta);
                            if (delta > 8) edgePixels++;
                            error += delta;
                            if (!expected[p].Equals(expected[0])) visible++;
                        }
                        if (error / (double)expected.Length > .15 || frameWorst > 32 || edgePixels > expected.Length * .0025)
                        {
                            var mismatch = new Texture2D(512, 256, TextureFormat.RGBA32, false);
                            mismatch.SetPixels32(0, 0, 256, 256, expected);
                            mismatch.SetPixels32(256, 0, 256, 256, actual);
                            string diagnosticFolder = Path.Combine(TestJson.RepoRoot(), "Tools", "out");
                            Directory.CreateDirectory(diagnosticFolder);
                            File.WriteAllBytes(Path.Combine(diagnosticFolder, "shuffle_fx_mismatch.png"), mismatch.EncodeToPNG());
                            Object.DestroyImmediate(mismatch);
                        }
                        // Consolidating transforms and UVs changes edge-sample rounding. Bound
                        // both overall error and the number/magnitude of differing edge pixels;
                        // a shifted, resized or recolored effect fails these limits.
                        Assert.LessOrEqual(error / (double)expected.Length, .15, methods[row] + " frame " + frame);
                        Assert.LessOrEqual(frameWorst, 32, methods[row] + " frame " + frame + " exceeds edge-sample tolerance");
                        Assert.LessOrEqual(edgePixels, expected.Length * .0025, methods[row] + " frame " + frame + " changes too many pixels");
                    }
                    Assert.Greater(visible, 8, methods[row] + " reference did not render an effect");
                    typeof(ShuffleFx).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(fx, null);
                    Object.DestroyImmediate(legacyRoot.gameObject);
                    Object.DestroyImmediate(batchRoot.gameObject);
                }
                string folder = Path.Combine(TestJson.RepoRoot(), "Tools", "out");
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, "shuffle_fx_frames.png"), contact.EncodeToPNG());
                TestContext.WriteLine(methods.Length + " original emitters, including the full ember lifetime, matched within rasterization tolerance; maximum RGB channel delta: " + worst);
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previousTarget;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(read);
                Object.DestroyImmediate(contact);
            }
        }

        static void SetLayer(RectTransform root, int layer = 31)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
        }

        static Color32[] Render(Camera camera, Texture2D read, RectTransform shown, RectTransform hidden)
        {
            // Select by camera layer so OnEnable does not dirty every mesh between frames.
            SetLayer(hidden, 30);
            SetLayer(shown, 31);
            camera.cullingMask = 1 << 31;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = camera.targetTexture;
            read.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
            return read.GetPixels32();
        }

        static void EmitFixture(SiegeView view, string method)
        {
            var at = new Vector2(-12f, -15f);
            var tint = new Color(.3f, .7f, .9f, 1f);
            switch (method)
            {
                case "FxSparks": Call(view, method, (Transform)Get(view, "_fx"), at, tint, 8, 80f, 14f, .38f); break;
                case "Cinder": Call(view, method, at, tint, 30f, .4f, new Vector2(20f, 40f)); break;
                case "Struck": Call(view, method, new Vector2(-80f, 70f), new Vector2(75f, -75f), tint, 3f, .4f, 2, .42f); break;
                case "Flash": Call(view, method, at, tint, 2f); break;
                case "Ripple": Call(view, method, at, tint, 140f, .4f, .06f, .45f); break;
                case "Embers": Call(view, method, at, tint, 12, 70f, 10f); break;
                case "Twinkles": Call(view, method, at, tint, 8, 70f); break;
                case "Lightup": Call(view, method, tint, .3f, .4f); break;
                case "FxPop": Call(view, method, at, tint, 2f, .4f); break;
                case "FxWave": Call(view, method, at, tint, 2f, .4f); break;
                case "Boom": Call(view, method, at, Frames("boom_burst"), 96f, 26f); break;
                case "Mixed":
                    Call(view, "FxPop", at, Pal.Ember, 2f, .4f);
                    UIKit.Img("Intervening image", view.GetComponent<RectTransform>(), Art.Glow(96),
                              Pal.A(Pal.Verdant, .6f), Vector2.one * 80f);
                    Call(view, "FxPop", at + Vector2.one * 10f, Pal.Azure, 1.5f, .4f);
                    break;
            }
        }

        static Sprite[] Frames(string reel)
        {
            var paths = Directory.GetFiles("Assets/Game/Art/Fx/Siege/" + reel, "*.png");
            Array.Sort(paths, StringComparer.Ordinal);
            var frames = new Sprite[paths.Length];
            for (int i = 0; i < paths.Length; i++) frames[i] = AssetDatabase.LoadAssetAtPath<Sprite>(paths[i].Replace('\\', '/'));
            return frames;
        }
    }
}
