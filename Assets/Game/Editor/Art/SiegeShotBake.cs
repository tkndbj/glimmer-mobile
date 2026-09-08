using System.Collections.Generic;
using System.IO;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Dev;
using UnityEditor;
using UnityEngine;

namespace GlimmerGrove.EditorTools
{
    /// <summary>
    /// Bakes the bought projectile pack's own effects into the 2D flipbooks Thornwatch's ward line
    /// fires — one projectile, one muzzle flash and one impact per ward colour.
    ///
    /// <para>
    /// <b>Why a bake and not the prefabs.</b> This game has no world: the canvas is
    /// <c>ScreenSpaceOverlay</c> and <c>Boot.EnsureCamera</c> gives the only camera in it
    /// <c>cullingMask = 0</c>, so a particle system dropped into the scene is not merely covered,
    /// it is never drawn at all. The one route that does draw is a stage of its own with its own
    /// camera and a <see cref="RenderTexture"/> — which is exactly what <c>VfxDemoScreen</c> is,
    /// and what its own note says never to couple a mode to: it costs a camera, a render texture
    /// and a resize path <em>per screen</em>, for art that is identical every time it is drawn. So
    /// the render happens once, here, and what ships is sprites. A ward fires every
    /// <c>SiegeTuning.FireEvery</c>, which is seven bolts a second each and twenty-eight across a
    /// lit line; a board doing that cannot afford anything else.
    /// </para>
    /// <para>
    /// <b>It is the pack's real animation and not an impression of one.</b> The alternative — the
    /// route Budburst's explosions took — is to cut the pack's flat textures and compose our own
    /// effect out of them. That is right when the pack has the picture you want lying in its
    /// <c>Textures/</c> folder; it is wrong here, because what was bought is sixty <em>motions</em>
    /// and the textures are only their raw material. Unity rasterises the motion; this freezes it.
    /// </para>
    /// <para>
    /// <b>The colour is read off <see cref="Pal"/> here rather than matched by eye</b>, which is
    /// what makes invariant 37f hold by construction: a ward, its bolts, its muzzle flash, its
    /// impact and the gems that feed it all take one <c>Pal</c> entry, so a palette retune moves
    /// every one of them and none of them can drift. The grade keeps a hot core white — a fireball
    /// whose middle is pure red is a fireball nobody has seen — and pulls everything round it onto
    /// the ward's hue.
    /// </para>
    /// <para>
    /// <b>What this cannot be is an offline gate.</b> Every other art tool in this project is a
    /// Python script with <c>--check</c>, and none of them can rasterise a particle system.
    /// <see cref="Verify"/> is the check that is available: it re-bakes into memory and holds the
    /// result to what is on disk, within a tolerance, because two GPUs are not obliged to
    /// rasterise the same triangle identically. <c>Tools/render_siege.py</c> is the other half and
    /// the more important one — it draws the shipped frames on the real board at the size a phone
    /// draws them, and looking at that is the only check in this project that has ever caught a
    /// picture being wrong (invariant 37g).
    /// </para>
    /// </summary>
    public static class SiegeShotBake
    {
        // ------------------------------------------------------------------ what each ward fires
        /// <summary>
        /// One ward's projectile: the pack prefab it is rendered from and the colour it burns.
        ///
        /// <para>
        /// <b>Four elements rather than four tints of one shape</b> — fire, venom, ice and
        /// lightning — which is CRAFT.md's rule about the board's vocabulary applied to the thing
        /// crossing the hill. A player who cannot separate red from green has to be able to
        /// separate a comet from a shard, and at the size a bolt is drawn a silhouette is the only
        /// difference that survives. It is also why three of the four already wear roughly the hue
        /// they are graded to: the pack's own fireball is warm and its icicle is cold, so the grade
        /// is mostly agreeing with the art rather than overruling it.
        /// </para>
        /// <para>
        /// <b>Keyed by colour and never by ward index.</b> Which colour a ward burns is content
        /// (<c>SiegeLayout</c>'s ward letters), so a table by post would put a fireball on whichever
        /// ward happened to stand first and disagree with the turret beside it.
        /// </para>
        /// </summary>
        struct Shot
        {
            public string Key;      // r, g, b, y — the order SiegeView.Tints is in
            public string Prefab;   // under VfxBench.PackRoot/Projectiles
            public Color Hue;
        }

        static readonly Shot[] Shots =
        {
            new Shot { Key = "r", Prefab = "vfx_Projectile_Fireball01",   Hue = Pal.Poppy },
            new Shot { Key = "g", Prefab = "vfx_Projectile_PoisonDart01", Hue = Pal.Mint  },
            new Shot { Key = "b", Prefab = "vfx_Projectile_Icicle01",     Hue = Pal.Azure },
            new Shot { Key = "y", Prefab = "vfx_Projectile_Lightning01",  Hue = Pal.Sun   },
        };

        static string PrefabPath(string name)
            => VfxBench.PackRoot + "/Projectiles/" + name + ".prefab";

        // ------------------------------------------------------------------ the reels
        /// <summary>
        /// <b>The lives are short on purpose.</b> Twenty-eight bolts a second means twenty-eight of
        /// each of these a second; a half-second impact would put fourteen on screen at once and
        /// the hill would be a wall of light with nothing readable in it. Each reel is cut to the
        /// part of the pack's effect that is the event.
        /// </summary>
        const int ShotFrames = 14;
        const float ShotSeconds = .47f;    // 30fps, looping while the bolt is in the air

        // The two bursts author only their frame *count*; how long they are on screen is
        // `SiegeView.Flash` and `SiegeView.Land`, which play them at 33 and 35 a second.
        const int MuzzleFrames = 8;
        const int HitFrames = 12;

        /// <summary>
        /// How much of a burst is <em>sampled</em>, against how long it is <em>played</em>.
        ///
        /// <para>
        /// <b>Two different questions, and answering them with one number cost two of the four
        /// muzzle flashes.</b> How long the reel is on screen is a board decision — twenty-eight
        /// of these a second means each has to be over quickly. How long the effect *takes* is the
        /// pack's decision, and several of these open with a wind-up: the fireball's muzzle draws a
        /// ring inward before it goes off, and the lightning's builds for a third of a second
        /// before there is anything to see. Sampled over a fixed quarter-second both of them baked
        /// their run-up and threw away the event.
        /// </para>
        /// <para>
        /// So the window is the effect's own length and the playback is the board's, which means a
        /// long burst is played back fast. That is the ordinary bargain of a baked flipbook and it
        /// reads correctly, because what is lost is duration rather than any part of the motion.
        /// </para>
        /// </summary>
        const float ShortestBurst = .18f, LongestBurst = .60f;

        /// <summary>
        /// How much longer than its own trail a comet is flown before the first frame is kept.
        ///
        /// <para>
        /// <b>Warming up for exactly one trail lifetime is not enough, and a looping reel is where
        /// that shows.</b> The trail was still lengthening across the whole capture, so frame
        /// thirteen carried a longer tail than frame nought and the loop snapped back every half
        /// second. Twice over plus a little settles every one of the four.
        /// </para>
        /// </summary>
        static float WarmFor(float trail) => trail * 2f + .2f;

        /// <summary>
        /// A bolt is drawn tall, and the trail is why.
        ///
        /// <para>
        /// <b>A square would either shrink the head to nothing or cut the tail off.</b> Measured on
        /// the pack: a fireball's head is about 3.3 units across and the flame it leaves behind
        /// runs 21; a poison dart is 1.4 against 20. Framing a square that holds the trail puts a
        /// head four pixels wide in the middle of it — which is exactly how the first bake came out
        /// and what the contact sheet showed.
        /// </para>
        /// <para>
        /// <b>But the shape is measured rather than fixed, and fixing it was the second version of
        /// the same mistake.</b> Forced to three-to-one, a comet whose real proportions are eight
        /// to one is padded sideways until it is a quarter the width of its own frame — and since
        /// the view sizes a bolt by its frame's <em>width</em>, that padding came off the thing on
        /// the board: on the render the fireball crossed the hill as a twelve-pixel sliver. So the
        /// frame is as tall as its own trail wants and no wider than its own head, clamped only to
        /// keep a texture a sane shape. <c>SiegeView.Lend</c> reads the proportions off the sprite,
        /// so nothing has to be told what they came out as.
        /// </para>
        /// </summary>
        const float LeanestShot = 1.6f, LongestShot = 8f;

                // **Raised once the haze stopped being baked in.** A bolt is drawn about 70 points wide
        // on a 1080 phone, so 48 was an upscale on the one thing in this mode the eye follows;
        // held beside a straight render of the same prefab the head was visibly soft. Held down
        // rather than raised further by what four reels of it cost resident in the chapter scope.
        const int ShotTall = 384;
        const int NarrowestShot = 56, WidestShot = 224;

        /// <summary>
        /// The shape the bake aims a comet's <em>flight</em> at, before measuring what it got.
        ///
        /// Only <see cref="SlowestBake"/> and <see cref="FastestBake"/> of it are available, so
        /// this is a target rather than a promise — and it is deliberately near the middle of the
        /// band above, so a reel that misses it lands somewhere still comet-shaped.
        /// </summary>
        const float WantedShot = 2.6f;

        /// <summary>
        /// How much tail a comet is framed with, counted in its own head-widths.
        ///
        /// <para>
        /// <b>This pack's trails are about six times the head, and that cannot all be shown.</b>
        /// The view sizes a bolt by its frame's <em>width</em>, so a head worth looking at means a
        /// frame roughly a head wide — and at six to one the sprite is then longer than the flight
        /// it has to cross, which reads as a static ribbon rather than as something travelling.
        /// Flying it slower to shorten the trail only goes so far before the flames pile onto the
        /// head and the comet becomes an oval with debris round it, which is a bug this project
        /// has shipped once already.
        /// </para>
        /// <para>
        /// So the far tail is left out of the frame instead — and <b>dissolved rather than cut</b>
        /// (<see cref="TailFade"/>), because the end of one of these is faint and thinning anyway,
        /// so a ramp over the last of it is invisible where a straight edge would be a line drawn
        /// across the sky.
        /// </para>
        /// </summary>
        const float TailHeads = 2.8f;

        /// <summary>How much of the frame's bottom the tail dissolves over.</summary>
        const float TailFade = .18f;

        /// <summary>
        /// An impact is radial, so it is square, and 192 as every other reel in <c>Fx/Siege</c>.
        ///
        /// <b>A muzzle flash is not radial and was framed as though it were.</b> It comes *out* of
        /// a barrel, so all of it is in front of the point it is drawn on — and a square centred on
        /// that point spends half its picture on the empty air behind the turret. Measured: the
        /// venom flash filled 42% of its own frame and the fireball's 60%. So a muzzle is anchored
        /// near its own bottom edge (<c>SiegeView.MuzzleAt</c>) and allowed to come out taller than
        /// it is wide, which is what a flash is.
        /// </summary>
        const int BurstSide = 192;
        const int NarrowestMuzzle = 80;
        const float LeanestMuzzle = 1f, LongestMuzzle = 2.4f;

        /// <summary>
        /// What the render is supersampled by before it is reduced.
        ///
        /// Particle art is nearly all soft edges and thin trails, and a thin bright trail sampled
        /// once per output pixel crawls as it moves. Four times the area costs a few seconds here
        /// and buys every frame of every bolt.
        /// </summary>
        const int Super = 2;

        /// <summary>The pack's own demo framing. Orthographic flattens these meshes into lozenges.</summary>
        const float FieldOfView = 50f;

        /// <summary>Far enough from the origin that the stage can never share a frame with anything.</summary>
        static readonly Vector3 StageOrigin = new Vector3(0f, -4000f, 0f);

        /// <summary>
        /// The simulation substep.
        ///
        /// <b>Much finer than a captured frame, and that is the trail.</b> Every trailing system in
        /// this pack is simulated in <em>world</em> space and emits per second, so what smears a
        /// comet out behind its head is the head having moved between emissions. Step the transform
        /// once per captured frame and the trail arrives as fourteen discrete blobs.
        /// </summary>
        const float Substep = 1f / 240f;

        /// <summary>
        /// How far the bake may move a projectile from the speed it was authored at, to make its
        /// trail fit the frame.
        ///
        /// <para>
        /// <b>Bounded rather than free, because speed is not a display preference here.</b> These
        /// trails are world-space systems emitting per second, so the length a trail smears over is
        /// speed times particle lifetime — fly one at a fifth of its speed and the flames that
        /// should stream out behind it pile onto the head, and the comet becomes an oval with
        /// debris round it. That is a bug the bench shipped once and it is worth not shipping
        /// twice. Inside this band the tail shortens and stays a tail; outside it, the frame grows
        /// instead and the head is allowed to be smaller. Nothing is ever clipped.
        /// </para>
        /// </summary>
        const float SlowestBake = .30f, FastestBake = 1.4f;

        // ------------------------------------------------------------------ menu
        [MenuItem("Glimmer Grove/Art/Bake Siege Projectiles", false, 30)]
        public static void Bake() => Run(write: true, contact: false);

        [MenuItem("Glimmer Grove/Art/Verify Siege Projectiles", false, 31)]
        public static void Verify() => Run(write: false, contact: false);

        [MenuItem("Glimmer Grove/Art/Siege Projectile Contact Sheet", false, 32)]
        public static void Contact() => Run(write: false, contact: true);

        // ------------------------------------------------------------------ the run
        /// <summary>One baked reel: its frames, stacked bottom-up, and the size one of them is.</summary>
        sealed class Book
        {
            public Texture2D Sheet;
            public int Wide, Tall, Frames;
        }

        /// <summary>
        /// <b>Reports rather than throws, and never leaves the stage standing.</b> Everything here
        /// is created with <see cref="HideFlags.HideAndDontSave"/> and torn down in a
        /// <c>finally</c>, because a camera left in the scene at y = -4000 is invisible, saved with
        /// the scene, and renders into whatever is next asked to render.
        /// </summary>
        static void Run(bool write, bool contact)
        {
            var made = new Dictionary<string, Book>();
            GameObject stage = null;
            Camera cam = null;

            try
            {
                stage = BuildStage(out cam);

                for (int i = 0; i < Shots.Length; i++)
                {
                    var shot = Shots[i];
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(shot.Prefab));

                    if (prefab == null)
                    {
                        // The pack is a bought asset and a checkout may not have it. Say which one
                        // is missing and carry on, exactly as the Python art tools pass when the
                        // licensed zips are absent.
                        Debug.LogWarning($"[siege shots] {shot.Prefab} is not in this project — skipped.");
                        continue;
                    }

                    BakeOne(stage.transform, cam, prefab, shot, made);
                }

                if (made.Count == 0)
                {
                    Debug.LogWarning("[siege shots] nothing baked — is the projectile pack imported?");
                    return;
                }

                if (contact) WriteContact(made);
                else if (write) WriteAll(made);
                else CompareAll(made);
            }
            finally
            {
                if (cam != null && cam.targetTexture != null)
                {
                    var rt = cam.targetTexture;
                    cam.targetTexture = null;
                    rt.Release();
                    Object.DestroyImmediate(rt);
                }
                if (stage != null) Object.DestroyImmediate(stage);
                foreach (var book in made.Values)
                    if (book != null && book.Sheet != null) Object.DestroyImmediate(book.Sheet);
            }
        }

        static void BakeOne(Transform stage, Camera cam, GameObject prefab, Shot shot,
                            Dictionary<string, Book> made)
        {
            float authored = Mathf.Max(1f, Reflected(prefab, "speed", 30f));
            float warm = WarmFor(TrailOf(prefab));

            // **Two passes, because the framing and the flight decide each other.** How long a
            // trail is depends on how fast the thing is flying, and how fast it should fly depends
            // on how much room its trail has — so the first pass flies it as authored purely to
            // find out how big the head reads and how far the tail runs, and the second one flies
            // it at whatever makes that tail fit a comet-shaped frame.
            var seen = Sample(stage, prefab, authored, warm, ShotSeconds, SiegeView.HeadAt);

            float want = seen.Across * 2f * WantedShot * SiegeView.HeadAt;
            float speed = seen.Behind > .05f
                ? authored * Mathf.Clamp(want / seen.Behind, SlowestBake, FastestBake)
                : authored;

            made["shot_" + shot.Key] =
                Capture(stage, cam, prefab, shot.Hue, ShotFrames, ShotSeconds, speed, warm,
                        SiegeView.HeadAt, ShotTall, NarrowestShot, WidestShot,
                        LeanestShot, LongestShot, comet: true);

            // The pack fires as three parts and the vendor's own demo plays all three. Showing the
            // middle one alone was judging a sentence by its verb.
            //
            // The impact stays square — it is radial and it is drawn where something happened —
            // while the flash is anchored at the barrel and free to be taller than it is wide.
            var muzzle = Companion(prefab, "muzzlePrefab");
            if (muzzle != null)
                made["muzzle_" + shot.Key] =
                    Capture(stage, cam, muzzle, shot.Hue, MuzzleFrames, Burst(muzzle), 0f, 0f,
                            SiegeView.MuzzleAt, BurstSide, NarrowestMuzzle, BurstSide,
                            LeanestMuzzle, LongestMuzzle, comet: false);

            var hit = Companion(prefab, "hitPrefab");
            if (hit != null)
                made["hit_" + shot.Key] =
                    Capture(stage, cam, hit, shot.Hue, HitFrames, Burst(hit), 0f, 0f,
                            .5f, BurstSide, BurstSide, BurstSide, 1f, 1f, comet: false);
        }

        // ------------------------------------------------------------------ the rig
        static GameObject BuildStage(out Camera cam)
        {
            var go = new GameObject("~SiegeShotStage") { hideFlags = HideFlags.HideAndDontSave };
            go.transform.position = StageOrigin;

            var camGo = new GameObject("~SiegeShotCam", typeof(Camera))
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            camGo.transform.SetParent(go.transform, false);

            cam = camGo.GetComponent<Camera>();
            cam.orthographic = false;
            cam.fieldOfView = FieldOfView;
            cam.nearClipPlane = .05f;
            cam.farClipPlane = 500f;
            cam.clearFlags = CameraClearFlags.SolidColor;

            // Pure black and fully transparent. The colour is the part that matters: these effects
            // are additive over whatever is behind them, so rendered on black a pixel *is* the
            // light they put there, which is what makes the key below exact rather than a guess.
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.useOcclusionCulling = false;
            cam.cullingMask = ~0;
            cam.enabled = false;   // rendered by hand, never by the editor loop

            return go;
        }

        /// <summary>
        /// Points the camera's render texture at a frame of this shape, remaking it when the shape
        /// changes. The camera's aspect follows the texture, so a tall frame is a tall picture and
        /// not a squashed wide one.
        /// </summary>
        static RenderTexture Target(Camera cam, int wide, int tall)
        {
            var rt = cam.targetTexture;
            if (rt != null && rt.width == wide * Super && rt.height == tall * Super) return rt;

            if (rt != null) { cam.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt); }

            rt = new RenderTexture(wide * Super, tall * Super, 24, RenderTextureFormat.ARGB32)
            {
                name = "~SiegeShotRT",
                hideFlags = HideFlags.HideAndDontSave,
                antiAliasing = 1,
            };
            rt.Create();

            cam.targetTexture = rt;
            cam.aspect = (float)wide / tall;
            return rt;
        }

        /// <summary>What one pass over an effect found out about its shape.</summary>
        struct Seen { public float Across, Behind, Ahead; }

        /// <summary>
        /// Renders one effect to a reel.
        ///
        /// <b>Framed from the art rather than from a number somebody liked.</b> The effect is
        /// simulated once with nothing recorded, its renderers are measured, and the camera is set
        /// back far enough to hold what was measured. A lane sized off a flight time instead was
        /// the bench's old bug: two thirds of the frame empty and the head a twelfth of it.
        /// </summary>
        static Book Capture(Transform stage, Camera cam, GameObject prefab, Color hue, int frames,
                            float seconds, float speed, float warm, float head, int tallPx,
                            int narrowest, int widest, float leanest, float longest,
                            bool comet)
        {
            // **Rendered twice, and the second one is what ships.** The first pass is framed off
            // the renderers' bounds, which is the only thing available before anything has been
            // drawn and is systematically too generous: this pack's prefabs carry lights, empty
            // emitters and quads whose bounds are far bigger than anything they put on screen, so a
            // poison dart's impact came out as a speck in the middle of an empty square. What is
            // drawn is the honest measurement, so the second pass — and the shape of the texture
            // itself — is decided by the *first pass's own alpha*, and a frame measured from the
            // picture cannot be wrong about the picture.
            var seen = Sample(stage, prefab, speed, warm, seconds, head);

            float rough = Shape(seen, head, leanest, longest);
            int roughPx = Pixels(tallPx, rough, narrowest, widest);

            var rt = Target(cam, roughPx, tallPx);
            var pixels = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, false);

            Color[][] raw;
            int wide = roughPx;
            float window = seconds;

            try
            {
                float ratio = (float)tallPx / roughPx;
                float height = Frame(seen, head, ratio, comet);

                raw = Roll(stage, cam, rt, pixels, prefab, frames, seconds, speed, warm, head,
                           height);

                // Measured over the frames the window trim keeps, and only those. Sized over all
                // of them instead, a burst whose last drifting smoke is its widest moment reserves
                // room for a frame the reel is about to stop carrying — which is how three of these
                // came out filling a third of their own picture, and it is invisible in every
                // number except the fill.
                var live = Alive(raw, seconds);
                var shown = Drawn(raw, rt.width, rt.height, height, head, live.First, live.Last);

                // Only ever tighter, and only ever shorter. If what was drawn wants more room or
                // more time than predicted, the prediction was not the thing that was wrong and
                // re-framing on it would crop the effect.
                // The *shape* is taken from the alpha outright, and is deliberately not held to
                // the first pass's. "Only ever tighter" is a rule about the world frame, and
                // applying it to the pixel width means the opposite of what it says: a narrower
                // frame is a taller aspect, not a smaller one, so clamping the width down forced a
                // shape the content did not want and `Frame` padded the height back out — which is
                // how a flash came to fill three tenths of its own picture.
                if (shown.HasValue)
                    wide = Pixels(tallPx, Shape(shown.Value, head, leanest, longest),
                                  narrowest, widest);

                window = Mathf.Min(seconds, live.Window);

                float finalRatio = (float)tallPx / wide;
                float take = shown.HasValue
                    ? Mathf.Min(height, Frame(shown.Value, head, finalRatio, comet))
                    : height;

                if (wide != roughPx || take < height * .97f || window < seconds * .97f ||
                    live.Skip > 0f)
                {
                    if (wide != roughPx)
                    {
                        Object.DestroyImmediate(pixels);
                        rt = Target(cam, wide, tallPx);
                        pixels = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32,
                                               false, false);
                    }

                    raw = Roll(stage, cam, rt, pixels, prefab, frames, window, speed,
                               warm + live.Skip, head, take);
                }
            }
            finally { Object.DestroyImmediate(pixels); }

            return new Book
            {
                Sheet = Reel(raw, hue, wide, tallPx, comet),
                Wide = wide,
                Tall = tallPx,
                Frames = frames,
            };
        }

        /// <summary>
        /// How many times taller than wide this effect wants its frame, clamped to what makes a
        /// sane texture. Equal bounds make it square, which is what the two bursts pass.
        /// </summary>
        static float Shape(Seen seen, float head, float leanest, float longest)
        {
            if (leanest >= longest) return leanest;

            float wide = Mathf.Max(.02f, seen.Across * 2f);
            float behind = Mathf.Min(seen.Behind, wide * TailHeads);

            float tall = Mathf.Max(behind / Mathf.Max(.05f, head),
                                   seen.Ahead / Mathf.Max(.05f, 1f - head));

            return Mathf.Clamp(tall / wide, leanest, longest);
        }

        /// <summary>
        /// A frame's width in pixels for a given shape.
        ///
        /// Rounded to a multiple of sixteen and clamped so no reel is a hair or a slab.
        ///
        /// <b>Sixteen rather than four, because a fine step is a knife edge.</b> A reel whose
        /// measured shape lands near a boundary flips width between two bakes on nothing more than
        /// rasterisation noise, and a texture that changes size is a change <see cref="Verify"/>
        /// can only report as total. A coarse step costs a few pixels of padding and makes the
        /// bake reproducible, which is the whole point of seeding it.
        /// </summary>
        static int Pixels(int tallPx, float aspect, int narrowest, int widest)
        {
            int wide = Mathf.RoundToInt(tallPx / Mathf.Max(.05f, aspect) / 16f) * 16;
            return Mathf.Clamp(wide, narrowest, widest);
        }

        /// <summary>
        /// The smallest frame of this shape that holds what was measured, with the head at
        /// <paramref name="head"/>. Returns its world height; the width is that over the aspect.
        /// </summary>
        static float Frame(Seen seen, float head, float aspect, bool comet)
        {
            // A comet shows its head and the tail nearest it; the rest runs off the bottom and is
            // faded out there. Everything else is framed round all of itself.
            float behind = comet
                ? Mathf.Min(seen.Behind, seen.Across * 2f * TailHeads)
                : seen.Behind;

            float w = Mathf.Max(seen.Across * 2f,
                      Mathf.Max(behind / (Mathf.Max(.05f, head) * aspect),
                                seen.Ahead / (Mathf.Max(.05f, 1f - head) * aspect)));

            return Mathf.Max(1.2f, w * aspect) * 1.10f;
        }

        /// <summary>One pass of the effect, recorded into a stack of renders.</summary>
        static Color[][] Roll(Transform stage, Camera cam, RenderTexture rt, Texture2D pixels,
                              GameObject prefab, int frames, float seconds, float speed, float warm,
                              float head, float height)
        {
            float back = height * .5f / Mathf.Tan(FieldOfView * .5f * Mathf.Deg2Rad);

            var shot = Spawn(stage, prefab);
            var roots = Roots(shot);
            var raw = new Color[frames][];

            try
            {
                Start(roots);
                Advance(shot, roots, warm, speed);

                for (int f = 0; f < frames; f++)
                {
                    // The camera rides with the head, and the head sits `head` of the way up the
                    // frame — so the trail has the whole rest of the frame to lie in.
                    Vector3 at = shot.transform.position;
                    cam.transform.position =
                        new Vector3(at.x, at.y - (head - .5f) * height, at.z - back);
                    cam.transform.rotation = Quaternion.identity;

                    cam.Render();
                    raw[f] = ReadBack(rt, pixels);

                    Advance(shot, roots, seconds / frames, speed);
                }
            }
            finally { Object.DestroyImmediate(shot); }

            return raw;
        }

        /// <summary>When an effect starts and stops being worth a frame.</summary>
        struct Live { public float Skip, Window; public int First, Last; }

        /// <summary>
        /// The part of the window an effect is actually doing something in.
        ///
        /// <para>
        /// <b>The pack's bursts do not fill their own lifetimes, at either end.</b> An impact's
        /// light is over in the first third and the last of its smoke drifts for another half a
        /// second; a lightning muzzle does the opposite and builds for a third of its life before
        /// there is anything to see. Sampled across the whole thing, one reel spends nine of its
        /// twelve frames on a stain and the other spends three on nothing at all — frames not
        /// spent on the part anybody sees, which is a hit that reads as a fizzle and a muzzle
        /// flash that starts late.
        /// </para>
        /// <para>
        /// So the window is cut to the frames carrying a sixth of the brightest one. It cannot
        /// shorten a comet and needs no special case to be safe: a bolt's trail is at steady state
        /// across every frame it is recorded in, so every frame carries the peak and the answer is
        /// the whole window.
        /// </para>
        /// </summary>
        static Live Alive(Color[][] raw, float seconds)
        {
            var weight = new float[raw.Length];
            float peak = 0f;

            for (int f = 0; f < raw.Length; f++)
            {
                var src = raw[f];
                double sum = 0d;

                for (int i = 0; i < src.Length; i++)
                    sum += Mathf.Max(src[i].r, Mathf.Max(src[i].g, src[i].b));

                weight[f] = (float)sum;
                if (weight[f] > peak) peak = weight[f];
            }

            if (peak <= 0f)
                return new Live { Skip = 0f, Window = seconds, First = 0, Last = raw.Length - 1 };

            int first = weight.Length - 1, last = 0;
            for (int f = 0; f < weight.Length; f++)
                if (weight[f] >= peak / 6f) { if (f < first) first = f; last = f; }

            float step = seconds / weight.Length;

            // One frame of run-up is kept, because a flash that is already at full brightness in
            // its first frame has no arrival in it.
            float skip = Mathf.Max(0f, (first - 1) * step);

            // Never below a third of what was asked for: a burst whose whole event is one frame is
            // still an event, and a reel of one frame is a still picture.
            return new Live
            {
                Skip = skip,
                Window = Mathf.Max(seconds / 3f, (last + 1) * step - skip),
                First = Mathf.Max(0, first - 1),
                Last = last,
            };
        }

        /// <summary>
        /// What a pass actually drew, in world units from the head — the box every frame's visible
        /// pixels fit inside.
        ///
        /// <b>Judged against a floor rather than against nothing</b>, because these renders have a
        /// faint haze over most of the frame and a box round every pixel above zero is the whole
        /// frame again. A twentieth of full brightness is under what anybody can see against grass
        /// and well over the haze.
        /// </summary>
        static Seen? Drawn(Color[][] raw, int wide, int tall, float height, float head,
                           int first, int last)
        {
            const float Floor = .05f;

            int x0 = wide, x1 = -1, y0 = tall, y1 = -1;

            for (int f = Mathf.Max(0, first); f <= Mathf.Min(raw.Length - 1, last); f++)
            {
                var src = raw[f];
                for (int y = 0; y < tall; y++)
                    for (int x = 0; x < wide; x++)
                    {
                        var c = src[y * wide + x];
                        if (Mathf.Max(c.r, Mathf.Max(c.g, c.b)) < Floor) continue;

                        if (x < x0) x0 = x;
                        if (x > x1) x1 = x;
                        if (y < y0) y0 = y;
                        if (y > y1) y1 = y;
                    }
            }

            if (x1 < x0 || y1 < y0) return null;

            float world = height / tall;                 // world units per rendered pixel
            float headX = wide * .5f, headY = tall * head;

            return new Seen
            {
                Across = Mathf.Max(headX - x0, x1 + 1 - headX) * world,
                Behind = Mathf.Max(0f, headY - y0) * world,
                Ahead = Mathf.Max(0f, y1 + 1 - headY) * world,
            };
        }

        /// <summary>
        /// Flies a throwaway copy and reports how much room it wants.
        ///
        /// <b>A copy of its own, so the reel starts from the same clean seed the probe did</b> and
        /// the picture is the one that was measured.
        /// </summary>
        static Seen Sample(Transform stage, GameObject prefab, float speed, float warm, float seconds,
                           float head)
        {
            var probe = Spawn(stage, prefab);
            var roots = Roots(probe);
            var box = new Extent();

            try
            {
                Start(roots);
                Advance(probe, roots, warm, speed);

                const int Samples = 6;
                for (int s = 0; s <= Samples; s++)
                {
                    box.Fold(probe);
                    if (s < Samples) Advance(probe, roots, seconds / Samples, speed);
                }
            }
            finally { Object.DestroyImmediate(probe); }

            return new Seen { Across = box.Across, Behind = box.Behind, Ahead = box.Ahead };
        }

        /// <summary>
        /// Starts the systems.
        ///
        /// <para>
        /// <b>Its own step, and leaving it out is a bake of nothing.</b> <c>Simulate</c> with
        /// <c>restart: false</c> continues from where a system is — and a system that has been
        /// stopped and cleared so its seed could be set is <em>stopped</em>, so it goes on emitting
        /// nothing however long it is advanced. The first bake ran to completion, wrote twelve
        /// reels and reported success, and every one of them held only the two mesh renderers that
        /// draw whether or not anything is playing. Nothing threw and nothing warned; the contact
        /// sheet is what said so.
        /// </para>
        /// </summary>
        static void Start(ParticleSystem[] roots)
        {
            for (int i = 0; i < roots.Length; i++)
                if (roots[i] != null) roots[i].Simulate(0f, true, true, false);
        }

        /// <summary>
        /// Walks the effect forward by <paramref name="time"/>, moving it as it goes.
        ///
        /// <para>
        /// <b>The move and the simulation step together</b>, in substeps far finer than a frame,
        /// because a world-space trail is drawn by where its emitter <em>was</em>.
        /// </para>
        /// <para>
        /// <b>And never on a fixed time step.</b> <c>Simulate</c>'s last argument quantises to
        /// <c>Time.fixedDeltaTime</c>, which is a fiftieth of a second here — four times coarser
        /// than the substep, so asking for one would round every step to nothing or to four of
        /// them.
        /// </para>
        /// </summary>
        static void Advance(GameObject go, ParticleSystem[] roots, float time, float speed)
        {
            if (time <= 0f) return;

            int steps = Mathf.Max(1, Mathf.RoundToInt(time / Substep));
            float dt = time / steps;

            for (int i = 0; i < steps; i++)
            {
                if (speed > 0f) go.transform.position += Vector3.up * (speed * dt);

                for (int r = 0; r < roots.Length; r++)
                    if (roots[r] != null) roots[r].Simulate(dt, true, false, false);
            }
        }

        /// <summary>
        /// How far what is drawn gets from the head: sideways, behind it and in front of it.
        ///
        /// <para>
        /// <b>Off the renderers' bounds and never off the particles.</b> The obvious reading —
        /// walk <c>GetParticles</c> and take position plus <c>GetCurrentSize</c> — is wrong for
        /// exactly the systems that matter: a system rendering a <em>mesh</em> reports its size as
        /// the scale it multiplies that mesh by, and this pack's fireball heads answer 120. Every
        /// measurement came back as the same number in all three directions, which is a fault
        /// that reads as a suspiciously round result rather than as an error.
        /// </para>
        /// <para>
        /// Travel is world up, so behind and ahead are −Y and +Y; sideways takes X and Z together,
        /// because the camera looks down Z and a wide flat effect turned toward it is as wide as
        /// its depth.
        /// </para>
        /// </summary>
        sealed class Extent
        {
            public float Across, Behind, Ahead;

            public void Fold(GameObject go)
            {
                Vector3 head = go.transform.position;
                var renderers = go.GetComponentsInChildren<Renderer>(true);

                for (int i = 0; i < renderers.Length; i++)
                {
                    var r = renderers[i];
                    if (r == null || !r.enabled) continue;

                    var b = r.bounds;
                    if (b.size.sqrMagnitude <= 1e-6f) continue;

                    Across = Mathf.Max(Across, Mathf.Abs(b.max.x - head.x));
                    Across = Mathf.Max(Across, Mathf.Abs(head.x - b.min.x));
                    Across = Mathf.Max(Across, Mathf.Abs(b.max.z - head.z));
                    Across = Mathf.Max(Across, Mathf.Abs(head.z - b.min.z));

                    Behind = Mathf.Max(Behind, head.y - b.min.y);
                    Ahead = Mathf.Max(Ahead, b.max.y - head.y);
                }
            }
        }

        // ------------------------------------------------------------------ the copy
        /// <summary>
        /// One copy of a pack prefab, seeded, stripped and standing at the stage's origin.
        ///
        /// <para>
        /// <b>Every behaviour is disabled rather than destroyed</b>, which is the pack's own trap:
        /// <c>ProjectileMoveScript</c> spawns a muzzle of its own, hides the object for a build-up
        /// delay and waits on a physics collision that never comes, and <c>Destroy</c> on a
        /// component lands at the end of the frame — too late to stop it.
        /// </para>
        /// <para>
        /// <b>And every seed is fixed</b>, which is what makes <see cref="Verify"/> mean anything:
        /// a system left on <c>useAutoRandomSeed</c> draws a different comet every time it is
        /// baked, and no check could tell that from art having drifted.
        /// </para>
        /// </summary>
        static GameObject Spawn(Transform stage, GameObject prefab)
        {
            var go = Object.Instantiate(prefab, stage);

            go.transform.position = stage.position;

            // **Pointed along travel and rolled to face the camera.** These prefabs fly along their
            // own forward axis, not along world up, so an identity rotation would fly a comet
            // sideways out of its own trail — and several of the meshes are flat on one axis, so
            // one left edge-on to the camera is a line. This is the pack's own demo framing.
            go.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.back);

            Hide(go.transform);

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

            var systems = go.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                systems[i].useAutoRandomSeed = false;
                systems[i].randomSeed = (uint)(1000 + i * 37);
            }

            return go;
        }

        static void Hide(Transform t)
        {
            t.gameObject.hideFlags = HideFlags.HideAndDontSave;
            for (int i = 0; i < t.childCount; i++) Hide(t.GetChild(i));
        }

        /// <summary>
        /// The particle systems that are not inside another one.
        ///
        /// <c>Simulate(..., withChildren: true)</c> walks down from where it is called, so calling
        /// it on every system would advance the nested ones once per ancestor.
        /// </summary>
        static ParticleSystem[] Roots(GameObject go)
        {
            var all = go.GetComponentsInChildren<ParticleSystem>(true);
            var roots = new List<ParticleSystem>(all.Length);

            for (int i = 0; i < all.Length; i++)
            {
                var parent = all[i].transform.parent;
                if (parent == null || parent.GetComponentInParent<ParticleSystem>() == null)
                    roots.Add(all[i]);
            }
            return roots.ToArray();
        }

        /// <summary>
        /// A number off the pack's own driver script, by name.
        ///
        /// The pack compiles into <c>Assembly-CSharp</c>, which an asmdef assembly may never
        /// reference, so the field is read by reflection — the same narrow lookup
        /// <c>VfxDemoScreen</c> makes, for the same reason.
        /// </summary>
        static float Reflected(GameObject prefab, string field, float fallback)
        {
            var behaviours = prefab.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] == null) continue;

                var f = behaviours[i].GetType().GetField(field);
                if (f != null && f.FieldType == typeof(float)) return (float)f.GetValue(behaviours[i]);
            }
            return fallback;
        }

        static GameObject Companion(GameObject prefab, string field)
        {
            var behaviours = prefab.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] == null) continue;

                var f = behaviours[i].GetType().GetField(field);
                if (f != null && f.FieldType == typeof(GameObject))
                {
                    var found = (GameObject)f.GetValue(behaviours[i]);
                    if (found != null) return found;
                }
            }
            return null;
        }

        /// <summary>
        /// How long a one-shot burst takes to happen: the longest a system of it goes on emitting
        /// plus the longest one of its particles lives, which is when the last of it has gone.
        ///
        /// Every system rather than the world-space ones, because a burst does not travel and its
        /// whole job is over in place.
        /// </summary>
        static float Burst(GameObject prefab)
        {
            float longest = 0f;
            var systems = prefab.GetComponentsInChildren<ParticleSystem>(true);

            for (int i = 0; i < systems.Length; i++)
            {
                var main = systems[i].main;
                longest = Mathf.Max(longest,
                                    main.startDelay.constantMax + main.duration +
                                    main.startLifetime.constantMax);
            }
            return Mathf.Clamp(longest, ShortestBurst, LongestBurst);
        }

        /// <summary>How long the longest world-space trail lingers: how far to warm up before recording.</summary>
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
            return Mathf.Clamp(longest, .12f, 1.2f);
        }

        // ------------------------------------------------------------------ pixels
        static Color[] ReadBack(RenderTexture rt, Texture2D into)
        {
            var was = RenderTexture.active;
            RenderTexture.active = rt;
            into.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
            into.Apply(false);
            RenderTexture.active = was;
            return into.GetPixels();
        }

        /// <summary>
        /// Turns a stack of renders into a reel: keyed off black, graded onto the ward's hue,
        /// reduced from the supersample and normalised as one set.
        ///
        /// <para>
        /// <b>The key is exact rather than a threshold, because the background was black.</b> These
        /// effects draw additively, so a rendered pixel is the light they put there and nothing
        /// else. The brightest channel is therefore how much of that pixel the effect covers, and
        /// dividing the colour by it recovers the colour the effect meant — which is what lets a
        /// bolt be composited over a bright green hill and still look like the thing that was
        /// rendered on black. Keying by a threshold instead would eat every wisp in the trail,
        /// which is most of what makes these read as motion.
        /// </para>
        /// <para>
        /// <b>Normalised across the whole reel and never per frame.</b> Several of this pack's
        /// systems peak well under full brightness, so the set is scaled up to reach it — but a
        /// frame at a time would make the last dying spark as bright as the muzzle flash, which is
        /// the animation inverted.
        /// </para>
        /// <para>
        /// <b>And the curve bends the other way from the one this shipped with, which is the whole
        /// difference between a fireball and a red smudge.</b> The first cut lifted faint coverage
        /// (an exponent below one) on the argument that a trail at a tenth of an alpha disappears
        /// into grass. What that actually did was promote the near-black haze every one of these
        /// effects sits in — invisible in the pack's own render, because it is additive over black
        /// and adds nothing — into a translucent cloud twice the size of the flame. Held up beside
        /// a straight render of the same prefab the difference was not subtle: a small crisp
        /// yellow head with sparks, against a blurred column. So haze below <see cref="Haze"/> is
        /// dropped outright and what survives is bent <em>down</em>, which keeps a core solid and
        /// lets a wisp be a wisp.
        /// </para>
        /// </summary>
        const float Haze = .05f;
        const float Lift = 1.25f;
        static Texture2D Reel(Color[][] raw, Color hue, int wide, int tall, bool comet)
        {
            int bigW = wide * Super;
            int frames = raw.Length;

            var keyed = new Color[frames][];
            float peak = 0f;

            for (int f = 0; f < frames; f++)
            {
                var src = raw[f];
                var dst = new Color[src.Length];

                for (int i = 0; i < src.Length; i++)
                {
                    var c = src[i];
                    float cover = Mathf.Max(c.r, Mathf.Max(c.g, c.b));

                    if (cover <= Haze) { dst[i] = new Color(0f, 0f, 0f, 0f); continue; }

                    dst[i] = Grade(new Color(c.r / cover, c.g / cover, c.b / cover, 1f), hue);
                    dst[i].a = cover;

                    if (cover > peak) peak = cover;
                }
                keyed[f] = dst;
            }

            float gain = peak > .02f ? 1f / peak : 1f;

            var sheet = new Texture2D(wide, tall * frames, TextureFormat.RGBA32, false, false);
            var outPix = new Color32[wide * tall * frames];

            for (int f = 0; f < frames; f++)
            {
                var src = keyed[f];

                for (int y = 0; y < tall; y++)
                    for (int x = 0; x < wide; x++)
                    {
                        // Box down the supersample. Colour is averaged weighted by coverage, so a
                        // transparent neighbour cannot wash a bright pixel toward black.
                        float r = 0f, g = 0f, b = 0f, a = 0f;

                        for (int sy = 0; sy < Super; sy++)
                            for (int sx = 0; sx < Super; sx++)
                            {
                                var c = src[(y * Super + sy) * bigW + x * Super + sx];
                                r += c.r * c.a; g += c.g * c.a; b += c.b * c.a; a += c.a;
                            }

                        // The tail runs off the bottom of a comet's frame, so it is thinned out
                        // over the last of it: a hard edge there is a line drawn across the sky.
                        float ramp = comet
                            ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, tall * TailFade, y))
                            : 1f;

                        Color32 outc;
                        if (a <= 1e-5f || ramp <= 0f) outc = new Color32(255, 255, 255, 0);
                        else
                        {
                            float alpha = Mathf.Pow(
                                Mathf.Clamp01(a / (Super * Super) * gain), Lift) * ramp;

                            outc = new Color32((byte)(Mathf.Clamp01(r / a) * 255f),
                                               (byte)(Mathf.Clamp01(g / a) * 255f),
                                               (byte)(Mathf.Clamp01(b / a) * 255f),
                                               (byte)(Mathf.Clamp01(alpha) * 255f));
                        }

                        outPix[(f * tall + y) * wide + x] = outc;
                    }
            }

            sheet.SetPixels32(outPix);
            sheet.Apply(false);
            return sheet;
        }

        /// <summary>
        /// Pulls one pixel onto the ward's hue, keeping a white-hot core white.
        ///
        /// <para>
        /// Saturation is raised rather than replaced, so a pixel that was nearly grey smoke stays
        /// nearly grey and a pixel that was strongly coloured becomes strongly the ward's colour —
        /// the rule <c>make_siege_art.hued</c> already follows for the turrets. What is added here
        /// is the core: the middle of a fireball, the flash inside a lightning bolt and the glint
        /// off an icicle are all near-white, and grading those onto a saturated hue is what makes a
        /// baked effect look like a coloured cut-out. Whiteness is measured as brightness that is
        /// <em>not</em> already carrying colour, so a saturated bright orange is graded and a
        /// desaturated bright white is left alone.
        /// </para>
        /// <para>
        /// <b>The protection is capped, and the first cut without a cap was wrong for half the
        /// pack.</b> An icicle and a lightning bolt are near-white nearly all over — that is what
        /// they are — so a rule that leaves white alone left both of them white, and a ward
        /// firing something colourless is the one thing invariant 37f is about. So it takes a
        /// glint back to almost no colour and never takes a *body* past
        /// <see cref="MostWhite"/>: the shard stays blue with white edges, which is what an
        /// icicle looks like. The cap is measured rather than chosen — the saturation of a
        /// bolt's lit pixels, where a fireball and a venom dart come out near one and the icicle
        /// came out at <b>0.18</b>, which on the board is a white streak fired by a blue turret.
        /// </para>
        /// </summary>
        const float MostWhite = .62f;

        /// <summary>
        /// How far a pixel is pulled toward the ward's hue.
        ///
        /// <b>A nudge, because a re-hue is what made these look like stickers.</b> Replacing the
        /// hue outright turns a fireball's yellow-hot head and orange body into one flat red — and
        /// the white-core rule does not save it, because a flame's core is *saturated yellow*
        /// rather than white, so it is graded like everything else. What is actually wanted is
        /// small: these four were chosen because the pack already draws them roughly the colours
        /// the wards burn (a warm fireball, a green dart, a cold icicle, a gold bolt), so agreement
        /// with <c>Pal</c> is a lean rather than a repaint. The rest of invariant 37f's job is done
        /// by the halo under the bolt's head, which <c>SiegeView</c> tints from the same entry.
        /// </summary>
        const float Toward = .38f;

        static Color Grade(Color lit, Color hue)
        {
            Color.RGBToHSV(lit, out _, out float s, out float v);
            Color.RGBToHSV(hue, out float h, out _, out _);

            float white = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.86f, 1f, v * (1f - s)))
                          * MostWhite;

            float sat = Mathf.Clamp01(s * .45f + .55f) * (1f - white);
            var wanted = Color.HSVToRGB(h, sat, Mathf.Clamp01(v * 1.06f + .04f));

            return Color.Lerp(lit, wanted, Toward * (1f - white));
        }

        // ------------------------------------------------------------------ on disk
        /// <summary>
        /// A reel's frames as PNGs under <c>Art/Fx/Siege/&lt;key&gt;/fNN.png</c>.
        ///
        /// <b>A folder of frames rather than a sheet</b>, because that is what
        /// <c>AssetLibrary.Frames</c> asks Addressables for — a label, one sprite per file — and
        /// what <c>AddressableAutoRegister</c> files on import.
        /// </summary>
        static string FolderOf(string key) => "Assets/Game/" + AssetManifest.SiegeFx(key);

        static IEnumerable<KeyValuePair<string, byte[]>> Files(string key, Book book)
        {
            var all = book.Sheet.GetPixels32();

            for (int f = 0; f < book.Frames; f++)
            {
                var one = new Color32[book.Wide * book.Tall];
                System.Array.Copy(all, f * book.Wide * book.Tall, one, 0, one.Length);

                var frame = new Texture2D(book.Wide, book.Tall, TextureFormat.RGBA32, false, false);
                frame.SetPixels32(one);
                frame.Apply(false);

                byte[] png = frame.EncodeToPNG();
                Object.DestroyImmediate(frame);

                yield return new KeyValuePair<string, byte[]>(
                    FolderOf(key) + "/f" + f.ToString("00") + ".png", png);
            }
        }

        static void WriteAll(Dictionary<string, Book> made)
        {
            int count = 0;

            // Through the asset database rather than the file system: deleting a folder from under
            // Unity leaves its .meta files behind, and a stale .meta is an Addressables entry
            // pointing at nothing — which does not break the game, it breaks the *build*
            // (`BundleBuildContent` refuses an entry whose asset has gone).
            foreach (var pair in made)
            {
                string dir = FolderOf(pair.Key);
                if (AssetDatabase.IsValidFolder(dir)) AssetDatabase.DeleteAsset(dir);
            }

            try
            {
                AssetDatabase.StartAssetEditing();

                foreach (var pair in made)
                {
                    Directory.CreateDirectory(FolderOf(pair.Key));

                    foreach (var file in Files(pair.Key, pair.Value))
                    {
                        File.WriteAllBytes(file.Key, file.Value);
                        count++;
                    }
                }
            }
            finally
            {
                // Not optional: an exception between the two leaves the asset database in editing
                // mode, which looks exactly like the freeze it prevents.
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.Refresh();
            Debug.Log($"[siege shots] wrote {count} frames across {made.Count} reels under Art/Fx/Siege.");
        }

        /// <summary>
        /// Holds what is on disk to what this tool would bake now.
        ///
        /// <b>Within a tolerance, and that is honest rather than lax.</b> Particle simulation here
        /// is seeded and deterministic, but rasterisation is not promised to be identical across
        /// two GPUs or two driver versions, so a byte comparison would fail on a colleague's
        /// machine for a reason that has nothing to do with the art. A mean difference of more than
        /// a couple of levels is a real change; a fraction of one is the graphics card.
        /// </summary>
        static void CompareAll(Dictionary<string, Book> made)
        {
            var missing = new List<string>();
            var differ = new List<string>();

            foreach (var pair in made)
                foreach (var file in Files(pair.Key, pair.Value))
                {
                    if (!File.Exists(file.Key)) { missing.Add(file.Key); continue; }

                    // Six levels out of 255. Measured: a re-bake of the same seed lands within
                    // three on the faintest frames of a trail, where a handful of dim particles
                    // fall either side of a pixel; a real change moves whole shapes.
                    float drift = Drift(File.ReadAllBytes(file.Key), file.Value);
                    if (drift > 6f) differ.Add($"{file.Key} (mean {drift:0.0}/255)");
                }

            if (missing.Count == 0 && differ.Count == 0)
            {
                Debug.Log($"[siege shots] {made.Count} reels on disk are what this tool bakes.");
                return;
            }

            foreach (var m in missing) Debug.LogError("[siege shots] missing: " + m);
            foreach (var d in differ) Debug.LogError("[siege shots] differs: " + d);
            Debug.LogError($"[siege shots] {missing.Count} missing, {differ.Count} differ — " +
                           "re-run Bake Siege Projectiles.");
        }

        static float Drift(byte[] a, byte[] b)
        {
            var left = new Texture2D(2, 2);
            var right = new Texture2D(2, 2);

            try
            {
                if (!left.LoadImage(a) || !right.LoadImage(b)) return 255f;
                if (left.width != right.width || left.height != right.height) return 255f;

                var p = left.GetPixels32();
                var q = right.GetPixels32();

                // Summed scaled by 255 so the colour term can stay integer, and divided by that
                // again at the end — the answer is a mean difference in ordinary 0-255 levels, so
                // two identical files are nought and two files sharing no pixel are 255.
                long sum = 0;
                for (int i = 0; i < p.Length; i++)
                {
                    // Colour is weighted by alpha on both sides: the colour of a fully transparent
                    // pixel is never drawn, and comparing it would report noise nobody can see.
                    int weight = Mathf.Max(p[i].a, q[i].a);
                    sum += Mathf.Abs(p[i].a - q[i].a) * 255;
                    sum += (Mathf.Abs(p[i].r - q[i].r) + Mathf.Abs(p[i].g - q[i].g) +
                            Mathf.Abs(p[i].b - q[i].b)) * weight;
                }
                return sum / (float)(p.Length * 4 * 255);
            }
            finally
            {
                Object.DestroyImmediate(left);
                Object.DestroyImmediate(right);
            }
        }

        /// <summary>
        /// Every reel laid out as one sheet, on the ground colour the board draws them over.
        ///
        /// <b>This is the check that matters</b> — no number in this project can see that a bolt
        /// reads as a smudge, and looking at a sheet is what caught a lantern shaped like a
        /// crosshair, a road that read as a row of sockets and a fuel tube behind a plate. It
        /// caught this tool baking twelve reels of nothing, too.
        /// </summary>
        static void WriteContact(Dictionary<string, Book> made)
        {
            const int Row = 150;   // how tall one row of the sheet is drawn

            var keys = new List<string>(made.Keys);
            keys.Sort();

            int width = 0;
            foreach (var key in keys)
            {
                var book = made[key];
                int cell = Mathf.Max(8, Row * book.Wide / book.Tall);
                width = Mathf.Max(width, cell * book.Frames);
            }

            int height = keys.Count * Row;
            var pix = new Color32[width * height];
            var ground = new Color32(46, 44, 42, 255);   // the hill, roughly
            for (int i = 0; i < pix.Length; i++) pix[i] = ground;

            for (int row = 0; row < keys.Count; row++)
            {
                var book = made[keys[row]];
                var all = book.Sheet.GetPixels32();
                int cell = Mathf.Max(8, Row * book.Wide / book.Tall);

                for (int f = 0; f < book.Frames; f++)
                    for (int y = 0; y < Row; y++)
                        for (int x = 0; x < cell; x++)
                        {
                            int sx = x * book.Wide / cell;
                            int sy = y * book.Tall / Row;
                            var c = all[(f * book.Tall + sy) * book.Wide + sx];

                            int ox = f * cell + x;
                            int oy = (keys.Count - 1 - row) * Row + y;
                            if (ox >= width) continue;

                            float alpha = c.a / 255f;
                            var under = pix[oy * width + ox];
                            pix[oy * width + ox] = new Color32(
                                (byte)(c.r * alpha + under.r * (1f - alpha)),
                                (byte)(c.g * alpha + under.g * (1f - alpha)),
                                (byte)(c.b * alpha + under.b * (1f - alpha)), 255);
                        }
            }

            var sheet = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            sheet.SetPixels32(pix);
            sheet.Apply(false);

            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".",
                                       "Tools", "siege_shots_contact.png");
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            Debug.Log("[siege shots] contact sheet at " + path + " — rows, top down: " +
                      string.Join(", ", keys));
        }
    }
}
