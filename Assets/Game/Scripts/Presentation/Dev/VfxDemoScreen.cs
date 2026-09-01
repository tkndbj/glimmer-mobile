#if GLIMMER_BENCH
using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove.Dev
{
    /// <summary>
    /// A bench for looking at the bought projectile pack on a real phone, one effect at a time,
    /// flying along a straight line.
    ///
    /// <para>
    /// <b>Behind <c>GLIMMER_BENCH</c>, whole file</b>, along with the one row in <c>ModeSwitch</c>
    /// that reaches it. It has to run on a device — an effect judged in the Editor's Game view is
    /// judged at the wrong size, brightness and frame rate — so it loads through Addressables
    /// like everything else, and the define is what keeps its bundle out of a store build. See
    /// <see cref="VfxBench"/> for the address convention and <c>VfxBenchGroup</c> for the switch.
    /// </para>
    /// <para>
    /// <b>Why an imported particle pack draws nothing here at all.</b> The UI canvas is
    /// <c>ScreenSpaceOverlay</c>, composited over whatever any camera drew; and
    /// <c>Boot.EnsureCamera</c> gives the only camera in the game <c>cullingMask = 0</c>, because
    /// this game has no world. So a particle dropped into the scene is not merely covered, it is
    /// never drawn. This screen brings its own stage, camera and <see cref="RenderTexture"/>, and
    /// touches nothing about the game's camera, canvas, layers or tags.
    /// </para>
    /// <para>
    /// <b>Nothing is post-processed, and that is a decision made against evidence rather than a
    /// gap.</b> This pack is authored for linear colour with bloom, and this project is Built-in
    /// RP in <b>Gamma</b> with no post stack, because it is a 2D game of flat sprites and changing
    /// that would re-grade every screen in it. A hand-rolled bloom was tried here to compensate
    /// and was <em>removed</em>: an 8-bit thresholded blur stamps a hard, stepped halo around
    /// every effect, and it was reported exactly as it looks — "a weird outer shell". Rendered
    /// straight, these effects are crisp and read correctly, which is what a bench is for. A real
    /// glow belongs in a real post stack (PPv2 for Built-in RP) or nowhere, and it is nowhere
    /// until something in the game actually wants it.
    /// </para>
    /// <para>
    /// <b>Projectiles only.</b> The hits and muzzles are supporting effects — a hit is a flash
    /// that is over in half a second — and this pack's whole subject is the thing that travels.
    /// They stay registered in the bundle, because showing them again is one call to
    /// <see cref="VfxBench.LabelFor"/>.
    /// </para>
    /// </summary>
    public sealed class VfxDemoScreen : View
    {
        /// <summary>Which of <see cref="VfxBench.Kinds"/> this bench shows.</summary>
        const int Kind = 2;   // Projectiles

        // ------------------------------------------------------------------ the stage
        /// <summary>
        /// The camera is a <b>perspective</b> one at the pack's own demo scene's field of view.
        ///
        /// Orthographic was the first cut and is wrong for this art: these effects are built from
        /// meshes with fresnel and scrolling shaders, and flattening the projection takes the
        /// roundness out of a fireball and turns a cone into a lozenge. 50 degrees is not a taste
        /// — it is read off <c>UniqueProjectiles05_Demo.unity</c>, which is the framing the pack
        /// was authored and marketed in.
        /// </summary>
        const float FieldOfView = 50f;

        /// <summary>
        /// How much lane a shot gets beyond the length of its own trail: room for the head in
        /// front and for the tail to thin out behind.
        /// </summary>
        const float LaneMargin = 1.7f;

        /// <summary>The shortest lane worth flying, for an effect that trails almost nothing.</summary>
        const float MinLane = 9f;

        /// <summary>How much room one effect is given across the line, as a fraction of the lane.</summary>
        const float CellFraction = .13f;

        /// <summary>
        /// How much of the flight the camera takes in: the whole lane, or a closer look at the
        /// head as it passes.
        /// </summary>
        static readonly float[] Views = { 1f, .45f, .2f };
        static readonly string[] ViewNames = { "WHOLE FLIGHT", "CLOSER", "HEAD" };

        /// <summary>Where the stage sits. Far from the origin so it can never share a frame with anything else.</summary>
        static readonly Vector3 StageOrigin = new Vector3(0f, -4000f, 0f);

        /// <summary>
        /// What the authored speed is multiplied by. <b>One is the default and the honest
        /// answer</b> — every other setting is for inspecting, because speed is not a display
        /// preference here: the trail systems are world-space and emit per <em>second</em>, so
        /// halving the speed halves the length the trail is smeared over and the comet collapses
        /// back onto the head. That is exactly the bug this control exists to let you see.
        /// </summary>
        static readonly float[] Rates = { 1f, .5f, 2f };
        static readonly string[] RateNames = { "AUTHORED", "HALF SPEED", "DOUBLE" };


        // ------------------------------------------------------------------ state
        readonly List<GameObject> _prefabs = new List<GameObject>();

        int _pick;
        int _count = 1;        // how many fly the line at once
        int _rate;
        int _view;
        bool _column = true;   // portrait has height to spare and no width, so upward by default
        bool _loop = true;

        Transform _stage;
        Camera _cam;
        RenderTexture _rt;
        RawImage _screen;
        Text _name, _counter;
        Btn _countBtn, _axisBtn, _rateBtn, _viewBtn, _loopBtn;

        Coroutine _play;
        int _rtW, _rtH;
        string _loaded;


        // ------------------------------------------------------------------ build
        protected override void Build()
        {
            UIKit.Fill(Content, Color.black);

            BuildStage();

            _screen = UIKit.Node("Stage", Content).gameObject.AddComponent<RawImage>();
            UIKit.StretchTo((RectTransform)_screen.transform, 0, 0, 0, 0);
            _screen.raycastTarget = true;
            _screen.gameObject.AddComponent<Btn>().Setup(Fire, silent: true);

            // Before anything is drawn, not on the first Update: a RawImage with no texture is a
            // solid white rectangle, which is the trap invariant 7b records for an Image with no
            // sprite — a blank is not blank, it is white.
            FitTexture();

            BuildChrome();

            Load();
            Repaint();
        }

        void BuildChrome()
        {
            UIKit.IconButton("Back", Safe, Skins.Nav, "ic_left", new Vector2(112f, 112f),
                             new Vector2(0f, 1f), new Vector2(92f, -104f),
                             () => Flow.Go<LevelsScreen>());

            _name = UIKit.Shrinkable(
                UIKit.Titled("Name", Safe, "", 34, Pal.Cream, TextAnchor.MiddleCenter,
                             new Vector2(720f, 52f), new Vector2(.5f, 1f), new Vector2(0f, -96f),
                             0f, 3f), 18);

            _counter = UIKit.Label("Counter", Safe, "", 24, Pal.A(Pal.Cream, .55f),
                                   TextAnchor.MiddleCenter, new Vector2(760f, 34f),
                                   new Vector2(.5f, 1f), new Vector2(0f, -140f));

            // Sixty effects walked with the thumb, without looking away from the line.
            UIKit.IconButton("Prev", Safe, Skins.Nav, "ic_left", new Vector2(124f, 124f),
                             new Vector2(0f, 0f), new Vector2(104f, 330f), () => Step(-1));
            UIKit.IconButton("Next", Safe, Skins.Nav, "ic_right", new Vector2(124f, 124f),
                             new Vector2(1f, 0f), new Vector2(-104f, 330f), () => Step(1));

            // Every control is a cycle rather than a menu: there is no state here worth a panel,
            // and a bench answers questions fastest when every control is one tap.
            var size = new Vector2(310f, 84f);

            _countBtn = UIKit.TextButton("Count", Safe, "btn_blue", "", 28, size,
                                         new Vector2(.5f, 0f), new Vector2(-166f, 214f),
                                         () => { _count = _count >= 3 ? 1 : _count + 1; Repaint(); });

            _axisBtn = UIKit.TextButton("Axis", Safe, "btn_blue", "", 28, size,
                                        new Vector2(.5f, 0f), new Vector2(166f, 214f),
                                        () => { _column = !_column; Repaint(); });

            _rateBtn = UIKit.TextButton("Rate", Safe, "btn_blue", "", 28, size,
                                        new Vector2(.5f, 0f), new Vector2(-166f, 116f),
                                        () => { _rate = (_rate + 1) % Rates.Length; Repaint(); });

            _viewBtn = UIKit.TextButton("View", Safe, "btn_violet", "", 28, size,
                                        new Vector2(.5f, 0f), new Vector2(166f, 116f),
                                        () => { _view = (_view + 1) % Views.Length; Repaint(); });

            _loopBtn = UIKit.TextButton("Loop", Safe, "btn_green", "", 28, size,
                                        new Vector2(.5f, 0f), new Vector2(0f, 18f),
                                        () => { _loop = !_loop; Repaint(); });
        }

        // ------------------------------------------------------------------ the rig
        void BuildStage()
        {
            var go = new GameObject("~VfxDemoStage");
            go.transform.position = StageOrigin;
            _stage = go.transform;

            var camGo = new GameObject("~VfxDemoCam", typeof(Camera));
            camGo.transform.SetParent(_stage, false);
            camGo.transform.localRotation = Quaternion.identity;

            _cam = camGo.GetComponent<Camera>();
            _cam.orthographic = false;
            _cam.fieldOfView = FieldOfView;
            _cam.nearClipPlane = .05f;
            _cam.farClipPlane = 200f;
            _cam.clearFlags = CameraClearFlags.SolidColor;

            // Pure black, and that is a requirement of the bloom rather than a preference. The
            // bright pass has a threshold; anything the ground sits above it by gets blurred and
            // added back, so a merely dark blue background blooms into a bright haze that swallows
            // the effect. Nothing under the threshold means nothing to lift.
            _cam.backgroundColor = Color.black;
            _cam.allowHDR = true;
            _cam.allowMSAA = false;
            _cam.useOcclusionCulling = false;
            _cam.depth = -50;

        }

        /// <summary>
        /// Keeps the texture the same shape as the screen, remaking it when the window moves.
        /// A stretched picture would misreport what an effect is shaped like, which is the one
        /// thing this screen exists to show.
        /// </summary>
        void FitTexture()
        {
            int w = Mathf.Max(64, Screen.width), h = Mathf.Max(64, Screen.height);
            if (_rt != null && w == _rtW && h == _rtH) return;

            var old = _rt;
            _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "~VfxDemo" };
            _rt.Create();
            _rtW = w; _rtH = h;

            _cam.targetTexture = _rt;
            if (_screen != null) _screen.texture = _rt;

            if (old != null) { old.Release(); Destroy(old); }
        }

        void Update()
        {
            if (_cam == null) return;
            FitTexture();
            _cam.transform.localPosition = new Vector3(0f, 0f, -Distance());
        }

        // ------------------------------------------------------------------ framing
        float Aspect() => _rtH <= 0 ? 9f / 16f : (float)_rtW / _rtH;

        /// <summary>Half the vertical extent the camera sees per unit of distance.</summary>
        static float HalfAngle => Mathf.Tan(FieldOfView * .5f * Mathf.Deg2Rad);

        /// <summary>
        /// How fast the shot currently in view flies, in world units a second.
        ///
        /// <para>
        /// <b>Read off the prefab, and that is the whole of what was wrong before.</b> The bench
        /// used to invent a speed that made a projectile fit a small lane — about 5.7 units a
        /// second against the 30 to 45 these are authored at. Every trail here is a world-space
        /// system emitting per <em>second</em>, so the length a trail smears over is speed times
        /// its particles' lifetime: fly one at a fifth of its speed and the flames and sparks
        /// that should stream out behind it pile up on the head instead. The effect stops being a
        /// comet and becomes an oval with debris round it, which is exactly how it was reported.
        /// </para>
        /// </summary>
        float Speed()
        {
            if (_prefabs.Count == 0) return 30f;
            return Mathf.Max(1f, SpeedOf(_prefabs[_pick])) * Rates[_rate];
        }

        /// <summary>
        /// The prefab's authored speed, found by reflection.
        ///
        /// The pack's <c>ProjectileMoveScript</c> compiles into <c>Assembly-CSharp</c>, which an
        /// asmdef assembly may never reference — so the field is read by name. Narrow and honest:
        /// one public float, with a sane fallback if a future pack names it something else.
        /// </summary>
        static float SpeedOf(GameObject prefab)
        {
            var behaviours = prefab.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] == null) continue;

                var field = behaviours[i].GetType().GetField("speed");
                if (field != null && field.FieldType == typeof(float))
                    return (float)field.GetValue(behaviours[i]);
            }
            return 30f;
        }

        /// <summary>One of the prefab's own companion effects — its muzzle flash or its hit.</summary>
        static GameObject Companion(GameObject prefab, string field)
        {
            var behaviours = prefab.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] == null) continue;

                var f = behaviours[i].GetType().GetField(field);
                if (f != null && f.FieldType == typeof(GameObject))
                    return (GameObject)f.GetValue(behaviours[i]);
            }
            return null;
        }

        /// <summary>
        /// How long the lane is: the length of this effect's own trail, plus room.
        ///
        /// <para>
        /// <b>Derived from the art, not from a flight time somebody liked.</b> A trail is a
        /// world-space system emitting per second, so the distance it smears over is the speed
        /// times how long its particles live — 30 units a second for half a second is fifteen
        /// units of flame, and no more however far the thing flies. Sizing the lane off a flat
        /// 1.25 second flight made it 38 units instead, which is correct and useless: the camera
        /// goes back far enough to hold two thirds of empty black, and the head everybody wants
        /// to look at ends up a twelfth of the screen.
        /// </para>
        /// </summary>
        float Span()
        {
            if (_prefabs.Count == 0) return MinLane;
            return Mathf.Max(MinLane, TrailOf(_prefabs[_pick]) * Speed() * LaneMargin);
        }

        /// <summary>
        /// How long, in seconds, this effect's trail lingers: the longest-lived of the systems
        /// that are simulated in <em>world</em> space, because those are the ones left behind.
        /// A system in local space rides along with the head and trails nothing.
        /// </summary>
        static float TrailOf(GameObject prefab)
        {
            float longest = 0f;
            var systems = prefab.GetComponentsInChildren<ParticleSystem>(true);

            for (int i = 0; i < systems.Length; i++)
            {
                var main = systems[i].main;
                if (main.simulationSpace != ParticleSystemSimulationSpace.World) continue;
                longest = Mathf.Max(longest, main.startLifetime.constantMax);
            }
            return longest;
        }

        /// <summary>How long the shot is in the air, which now falls out of the lane and the speed.</summary>
        float FlightTime => Span() / Speed();

        /// <summary>
        /// How far back the camera stands: far enough to hold the part of the lane
        /// <see cref="Views"/> asks for, and never so close that a line of shots overlaps.
        /// </summary>
        float Distance()
        {
            float along = Span() * Views[_view];
            float across = _count * Span() * CellFraction;

            // Along the lane is the screen's height in a column and its width in a row.
            float forAlong = _column
                ? along * .5f / HalfAngle
                : along * .5f / (HalfAngle * Mathf.Max(.2f, Aspect()));

            float forAcross = _column
                ? across * .5f / (HalfAngle * Mathf.Max(.2f, Aspect()))
                : across * .5f / HalfAngle;

            return Mathf.Max(forAlong, forAcross);
        }

        // ------------------------------------------------------------------ the pack
        /// <summary>
        /// Loads the projectiles, by label, through the game's own asset seam.
        ///
        /// <para>
        /// <b>Through <c>AssetLibrary.Provider</c> rather than <c>AssetLibrary</c> itself</b>, and
        /// that is the one deliberate departure. The library caches what it loads and never lets
        /// it go, which is right for art the game draws all session and wrong for sixty particle
        /// prefabs the bench wants to hand back. Everything invariant 7 is about still holds — the
        /// address is a string, the provider is the seam, and nothing here calls
        /// <c>Resources.Load</c> or Addressables by name.
        /// </para>
        /// </summary>
        void Load()
        {
            Unload();

            var provider = AssetLibrary.Provider;
            if (provider == null) return;

            _loaded = VfxBench.LabelFor(Kind);
            var found = provider.LoadAll<GameObject>(_loaded);

            if (found != null)
                for (int i = 0; i < found.Length; i++)
                    if (found[i] != null) _prefabs.Add(found[i]);

            _pick = _prefabs.Count == 0 ? 0 : Mathf.Clamp(_pick, 0, _prefabs.Count - 1);
        }

        /// <summary>
        /// Gives the pack back to the provider. Safe to call twice.
        ///
        /// <b>The stage is cleared first, and the order is the whole of it</b>: releasing a handle
        /// while copies of its prefabs are still flying hands Addressables back the last reference
        /// to assets that are being drawn.
        /// </summary>
        void Unload()
        {
            if (_play != null) { StopCoroutine(_play); _play = null; }
            Clear();

            _prefabs.Clear();

            if (_loaded == null) return;

            AssetLibrary.Provider?.Release(new[] { _loaded });
            _loaded = null;
        }

        void Step(int by)
        {
            if (_prefabs.Count == 0) return;
            _pick = (_pick + by + _prefabs.Count) % _prefabs.Count;
            Repaint();
        }

        // ------------------------------------------------------------------ playing
        void Repaint()
        {
            _countBtn.SetCaption(_count == 1 ? "ONE" : "LINE  " + _count);
            _axisBtn.SetCaption(_column ? "UPWARD" : "ACROSS");
            _rateBtn.SetCaption(RateNames[_rate]);
            _viewBtn.SetCaption(ViewNames[_view]);
            _loopBtn.SetCaption(_loop ? "LOOP ON" : "LOOP OFF");

            _name.text = _prefabs.Count == 0 ? "NOTHING FOUND" : _prefabs[_pick].name;

            // The empty case names the label rather than saying "none", because the only way to
            // reach it is the bundle not being in this build — a thing to fix in the Editor, not
            // something that went wrong on the phone.
            _counter.text = _prefabs.Count == 0
                ? "nothing addressed " + VfxBench.LabelFor(Kind)
                : (_pick + 1) + " / " + _prefabs.Count;

            Fire();
        }

        /// <summary>Starts the line again, clearing whatever is still in flight.</summary>
        void Fire()
        {
            if (_play != null) StopCoroutine(_play);
            _play = null;
            Clear();
            if (_prefabs.Count > 0) _play = StartCoroutine(Run());
        }

        void Clear()
        {
            if (_stage == null) return;

            for (int i = _stage.childCount - 1; i >= 0; i--)
            {
                var child = _stage.GetChild(i);
                if (_cam != null && child == _cam.transform) continue;
                Destroy(child.gameObject);
            }
        }

        IEnumerator Run()
        {
            // Short enough that a line reads as one volley with a ripple through it rather than as
            // three separate shots.
            const float Stagger = .16f;

            while (true)
            {
                var prefab = _prefabs[_pick];

                for (int i = 0; i < _count; i++)
                {
                    Launch(prefab, i);
                    if (i < _count - 1) yield return new WaitForSeconds(Stagger);
                }

                if (!_loop) { _play = null; yield break; }

                // Long enough for the shot to land and its hit to read before the lane clears.
                yield return new WaitForSeconds(FlightTime + 1.1f);
                Clear();
                yield return new WaitForSeconds(.12f);
            }
        }

        /// <summary>
        /// Sends one shot down the lane from slot <paramref name="slot"/>: its muzzle where it
        /// starts, the projectile at its authored speed, its hit where it lands.
        ///
        /// <para>
        /// <b>All three, because that is what the pack is.</b> Every projectile prefab names a
        /// <c>muzzlePrefab</c> and a <c>hitPrefab</c>, and the vendor's own demo fires the set as
        /// one event — a flash, a comet, an impact. Showing the middle one alone was judging a
        /// sentence by its verb.
        /// </para>
        /// <para>
        /// Driven here rather than by the pack's <c>ProjectileMoveScript</c>, which is still
        /// switched off, and for a sharper reason than before: 45 of these 60 carry a
        /// <c>buildUpTime</c>, and the script implements it by deactivating the GameObject and
        /// calling <c>Invoke</c> to switch it back on — but Unity does not run an <c>Invoke</c>
        /// queued on a behaviour that has been disabled, and deactivating the object disables it.
        /// Three quarters of the pack would simply never appear. What the script is read for is
        /// its <em>numbers</em>.
        /// </para>
        /// </summary>
        void Launch(GameObject prefab, int slot)
        {
            Vector3 axis = _column ? Vector3.up : Vector3.right;
            Vector3 across = _column ? Vector3.right : Vector3.up;

            float span = Span();
            float from = -(_count - 1) * .5f;
            Vector3 offset = across * ((from + slot) * span * CellFraction);

            Vector3 start = StageOrigin - axis * (span * .5f) + offset;
            Vector3 land  = StageOrigin + axis * (span * .5f) + offset;

            // Pointed along travel and rolled to face the camera: several of these meshes are
            // flat on one axis, and one edge-on to the viewer is a line.
            var facing = Quaternion.LookRotation(axis, Vector3.back);

            Fire(Companion(prefab, "muzzlePrefab"), start, facing);

            var go = Instantiate(prefab, start, facing, _stage);
            Strip(go);
            go.AddComponent<Drift>().Velocity = axis * Speed();
            Destroy(go, FlightTime);

            StartCoroutine(Land(prefab, land, facing));
        }

        /// <summary>The hit, when the shot gets there.</summary>
        IEnumerator Land(GameObject prefab, Vector3 where, Quaternion facing)
        {
            yield return new WaitForSeconds(FlightTime);
            Fire(Companion(prefab, "hitPrefab"), where, facing);
        }

        /// <summary>Plays one of the pack's one-shot effects and forgets about it.</summary>
        void Fire(GameObject prefab, Vector3 at, Quaternion facing)
        {
            if (prefab == null || _stage == null) return;

            var go = Instantiate(prefab, at, facing, _stage);
            Strip(go);
            Destroy(go, 3f);
        }

        /// <summary>
        /// Takes the pack's own demo driving off a copy.
        ///
        /// <para>
        /// Every projectile prefab carries <c>ProjectileMoveScript</c> and a Rigidbody, which is
        /// right for the pack's sample scene and wrong here: the script spawns a muzzle of its
        /// own, hides the object for a build-up delay and then waits on a physics collision the
        /// bench never gives it, while the body falls under gravity meanwhile.
        /// </para>
        /// <para>
        /// <b>Switched off rather than destroyed, and that is the whole of why this works.</b>
        /// <c>Destroy</c> on a component is applied at the <em>end of the frame</em>, while
        /// <c>Start</c> runs earlier in the same frame the copy is instantiated — so destroying
        /// the script still lets it run once, which is a stray muzzle and a hidden projectile per
        /// copy. Unity never calls <c>Start</c> on a disabled behaviour, and disabling takes
        /// effect on the line that does it.
        /// </para>
        /// </summary>
        static void Strip(GameObject go)
        {
            var behaviours = go.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
                if (behaviours[i] != null) behaviours[i].enabled = false;

            var bodies = go.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                bodies[i].isKinematic = true;
                bodies[i].detectCollisions = false;
                bodies[i].useGravity = false;
            }
        }

        sealed class Drift : MonoBehaviour
        {
            public Vector3 Velocity;
            void Update() { transform.position += Velocity * Time.deltaTime; }
        }

        // ------------------------------------------------------------------ leaving
        void OnDestroy()
        {
            Unload();

            if (_stage != null) Destroy(_stage.gameObject);

            if (_rt != null)
            {
                if (_cam != null) _cam.targetTexture = null;
                _rt.Release();
                Destroy(_rt);
                _rt = null;
            }
        }

        public override bool OnBack()
        {
            Flow.Go<LevelsScreen>();
            return true;
        }
    }
}
#endif
