using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GlimmerGrove.EditorTools
{
    /// <summary>
    /// Bakes a rigged 3D character into the sprite reels this board draws, one per ward colour.
    ///
    /// <para>
    /// <b>Why a bake rather than a bought sprite sheet.</b> The cast has to be seen from above,
    /// and the 2D market has almost none of that — "top-down" nearly always means a three-quarter
    /// RPG view where you still see a face, and true overhead only reads for creatures whose
    /// silhouette <em>is</em> their back (invariant 37ar: fifteen insects were the entire budget on
    /// this machine). Rendering a 3D model instead makes the angle a decision rather than a
    /// purchase: point the camera where the board wants it, step the animation, keep the frames.
    /// It is the oldest trick in the genre — Clash Royale's units are sprites baked from 3D — and
    /// it is what <c>SiegeShotBake</c> already does for the bought VFX pack, one step over from
    /// particles onto characters.
    /// </para>
    /// <para>
    /// <b>Nothing about the runtime changes.</b> What ships is PNG reels under
    /// <c>Art/Siege/&lt;key&gt;/f00.png</c>, addressed and drawn exactly as the insects are — no
    /// model, no rig and no animator ever reaches a build. The models live under an
    /// <c>Editor</c> folder, which is a guarantee rather than a convention: Unity excludes that
    /// folder from players, so a source FBX cannot be shipped by accident.
    /// </para>
    /// <para>
    /// <b>The models are CC0</b> (KayKit, Kay Lousberg), which is why they are committed where the
    /// CraftPix packs are gitignored: those may be built into a game and not redistributed as art,
    /// so every other art tool here passes when its source is absent. This one does not have to —
    /// its source is in the repo, so any checkout can re-bake, and the licence text sits beside it.
    /// </para>
    /// </summary>
    public static class SiegeCastBake
    {
        // ------------------------------------------------------------------ the roster

        /// <summary>One body: which model, which walk, and which reel it becomes.</summary>
        struct Body
        {
            public string Key;        // the reel prefix — `SiegeView.Skin` names these
            public string Model;      // the FBX under Source
            public string Clip;       // the animation to run through
            public string Rig;        // the animation file whose bone paths this body matches
            public float Lean;        // degrees of camera pitch, if this one wants its own
        }

        const string Source = "Assets/Game/Editor/Art/KayKit";

        /// <summary>
        /// The library every character's animations come from.
        ///
        /// <b>They run rather than walk, and that is legibility rather than pace.</b> From above, a
        /// humanoid's limbs are largely hidden by its own torso and helmet — rendered side by side,
        /// eight frames of `Walking_A` and `Walking_C` are almost indistinguishable at any pitch,
        /// where `Running_A` visibly rocks the helm and swings the arms. Reported from a device as
        /// units that "move in a static position", which was true.
        ///
        /// <b>It is the same structural fact as the camera angle, one level deeper</b>: overhead
        /// does not only decide whether a body can be <em>recognised</em> (see `Pitch`), it decides
        /// whether its motion survives the projection at all. The insects animate well from here
        /// because their legs and wings splay sideways into the plan view; a person's swing along
        /// their own axis, straight into the occluded direction. A run is the most a humanoid
        /// gives you from this camera, and it is still less than a beetle gives you for free.
        ///
        /// <b>One rig, shared.</b> KayKit animates a `Rig_Medium` skeleton and every humanoid in
        /// the packs is bound to it, so a clip sampled onto one body samples onto all of them —
        /// which is what makes a second character cost a row here and no work at all.
        /// </summary>
        const string Clips = Source + "/Rig_Medium_MovementBasic.fbx";

        /// <summary>
        /// The Adventurers pack's copy of the same animations.
        ///
        /// <b>Two files rather than one, and assuming otherwise shipped a raider that never
        /// moved.</b> KayKit animates a `Rig_Medium` skeleton and every humanoid in the packs is
        /// bound to it — which is true of the *rig* and not of the <em>bone paths</em>: a clip
        /// binds by transform path, so the Skeletons pack's clips bind to a skeleton's hierarchy
        /// and silently miss on a knight. Nothing throws. The model renders its bind pose, twelve
        /// frames come out byte-identical, and the bake reports success.
        /// </summary>
        const string AdventurerClips = Source + "/Adventurers_MovementBasic.fbx";

        /// <summary>
        /// The three kinds a chapter draws, as models.
        ///
        /// <b>Picked for what projects sideways, which is the only thing that survives this
        /// camera.</b> Surveyed on the board's own floor at three pitches: the hooded
        /// <c>Skeleton_Minion</c> is a featureless dome from above and reads as a skittle at every
        /// angle and every tint, where the warrior's horned helm and the blade down its back read
        /// immediately. That is the insects' own lesson (37ar) asked of a humanoid — a silhouette
        /// has to be made of something — and it is the rule for picking any future body here.
        ///
        /// The knight is the one borrowed from the Adventurers pack rather than the Skeletons,
        /// because a bulwark has to say it is carrying armour before it is in range.
        /// </summary>
        static readonly Body[] Roster =
        {
            new Body { Key = "kayMon",     Model = "Skeleton_Rogue",   Clip = "Running_A",
                       Rig = Clips },
            new Body { Key = "kayBrute",   Model = "Skeleton_Warrior", Clip = "Running_A",
                       Rig = Clips },
            // **The knight is back, and what it was withdrawn for never existed.** It was replaced
            // by a third skeleton because it "would not animate" — its bones moved exactly as much
            // as a skeleton's and its mesh did not follow them, which was recorded as unexplained.
            // It is explained: nothing was skinning any of these bodies (see `Skin`), and a knight
            // is the one model in the pack with **no static parts at all** — no helmet hung off a
            // bone, no hood, no cape — so where a skeleton at least rocked its hat, a knight stood
            // perfectly still and read as broken. Measured since: the knight's skinned leg moves
            // 1.065 units through `Running_A`, with either pack's clips and no missing bindings.
            // It was the healthiest body here and it was the one thrown out, because it had
            // nothing to hide the bug behind.
            new Body { Key = "kayBulwark", Model = "Knight",           Clip = "Running_A",
                       Rig = Clips },
        };

        // ------------------------------------------------------------------ the camera

        /// <summary>
        /// How far the camera is tilted from horizontal, in degrees.
        ///
        /// <para>
        /// <b>Not ninety, and that is the whole finding this tool was built around.</b> Straight
        /// down is what the insects are and what the board is drawn for — but an insect read from
        /// directly above is its carapace, which is its whole silhouette, where a humanoid is a
        /// skull and two shoulders. Rendered at 90, 65 and 45 and looked at on the board's own
        /// floor: ninety is unrecognisable, sixty-five reads as a figure, forty-five reads as a
        /// figure <em>walking toward you</em>.
        /// </para>
        /// <para>
        /// <b>Twenty-two, and it was fifty-two until a device said the cast did not animate.</b>
        /// Recognising a body and reading its <em>motion</em> are two different bars, and the
        /// second is far higher: at 52 the helmet is most of the figure and the legs are nubs, so
        /// a run reads as a head rocking. Rendered as a control at 0, 20, 35 and 52 — at 0 and 20
        /// the legs visibly alternate and it reads as running, and by 35 the helmet has taken over.
        /// </para>
        /// <para>
        /// <b>A shallow camera over a top-down floor is this genre's own answer rather than a
        /// compromise.</b> It is what Clash Royale and most tower defence games do, because a
        /// humanoid seen from overhead has nothing to show. The insects are drawn from directly
        /// above and stay that way: what has to agree with the floor is the <b>ground plane</b>,
        /// not every actor standing on it.
        /// </para>
        /// <para>
        /// <b>Moving it costs a re-bake and nothing else</b>, which is the entire argument for
        /// baking rather than buying.
        /// </para>
        /// </summary>
        const float Pitch = 22f;

        /// <summary>
        /// Which side of the body the camera stands on, in degrees.
        ///
        /// <b>Behind it by default, which is the wrong side.</b> Unity's convention is that a
        /// character faces <c>+Z</c>, and a camera built from <c>Euler(pitch, 0, 0)</c> looks along
        /// <c>+Z</c> too — so the first bake shipped a hill of raiders walking at the ward line
        /// with their backs to it, reported from a device in one line. They come <em>down</em> the
        /// hill toward the player, so what has to be facing the player is the front of them, which
        /// is also what the insects do (this pack draws them head-down and nothing is turned).
        ///
        /// The key light carries this too, or a body lit for one side of itself is rendered from
        /// the other and comes out flat.
        /// </summary>
        const float Yaw = 180f;

        /// <summary>How much of the frame the body fills, across its widest pass.</summary>
        const float Fill = .86f;

        /// <summary>
        /// How tall a cast frame is cut.
        ///
        /// <para>
        /// <b>Not the insects' 180, because these are not drawn at an insect's size.</b> That
        /// number is <c>make_siege_art.CAST</c> and it is right for a creeper, which stands 1.15
        /// cells; a bulwark stands 1.85 and a brute 1.55, so the same cut is drawn back out at
        /// about 1.6× on a phone and further on a tall one. Upscaling a sprite is the plainest
        /// "cheap" signal there is, and next to the gems — which are cut at their own size and are
        /// crisp — a soft body reads as a worse asset rather than as a smaller one. Held beside a
        /// native-resolution flower on the same board, it was the first thing wrong with it.
        /// </para>
        /// <para>
        /// <b>384 rather than more, because <c>ArtImportRules</c> caps <c>/Art/Siege/</c> at
        /// 512</b> and a cut that the importer then halves is worse than one that was never taken:
        /// it costs the disk and gives back nothing, silently (that rule's own warning). 384 leaves
        /// room for a body drawn larger later without touching the cap.
        /// </para>
        /// </summary>
        const int Tall = 384;

        /// <summary>Frames kept from a walk cycle. <c>make_siege_art.FRAMES</c>, matched.</summary>
        const int Frames = 12;

        /// <summary>Rendered this many times oversize and brought down, which is the anti-aliasing.</summary>
        const int Super = 4;

        /// <summary>Where the stage stands, far from anything a scene might hold.</summary>
        static readonly Vector3 StageOrigin = new Vector3(0f, -8000f, 0f);

        /// <summary>The environment light a body is rendered under. See <see cref="Ambience"/>.</summary>
        static readonly Color CastAmbient = new Color(.34f, .37f, .44f);

        // ------------------------------------------------------------------ the look

        /// <summary>
        /// The keyline every body is given, in pixels of the finished sprite, and its colour.
        ///
        /// <para>
        /// <b>Without it a baked model is visibly pasted onto this board.</b> Every other thing on
        /// the hill — the gems, the insects, the turrets — is cartoon art with a heavy dark
        /// outline, and a flat-shaded 3D render has none: held up beside a beetle the first bake
        /// read as a different game's asset dropped in. So the silhouette is grown and filled
        /// behind the body, which is how a cartoon keyline is got out of 3D.
        /// </para>
        /// <para>
        /// The colour is the kit's own keyline navy rather than black, for the reason 44h gives
        /// about the interface: a pure black edge against saturated faces reads as a hole, where a
        /// very dark blue reads as a line.
        /// </para>
        /// </summary>
        /// <b>In pixels of the cut, so it moves with <c>Tall</c>.</b> It was 3 against a cut of
        /// 180; the line is a fraction of a body and not a number of pixels, so a sharper cut with
        /// the old figure is a body wearing a hairline.
        const int Keyline = Tall * 3 / 180;

        static readonly Color32 KeylineInk = new Color32(6, 24, 56, 255);

        /// <summary>
        /// Which colour each ward is painted, in <c>WardLine.Colours</c> order.
        ///
        /// <b>The albedo is tinted before the render rather than the pixels after it</b>, which is
        /// the one thing baking buys over a bought sprite: a red material lit by a white key gives
        /// genuinely lit red shading, where multiplying a finished picture can only ever darken it
        /// (invariant 37l, which cost this mode two attempts on the turrets). The hues are the
        /// board's own, so a raider, its gem and the ward that answers it cannot drift.
        /// </summary>
        static readonly Color[] Hues =
        {
            new Color(.95f, .25f, .31f),   // Pal.Poppy
            new Color(.48f, .85f, .42f),   // Pal.Mint
            new Color(.31f, .76f, 1.00f),  // Pal.Azure
            new Color(1.00f, .54f, .17f),  // Pal.Amber
        };

        static readonly char[] Letters = { 'r', 'g', 'b', 'y' };

        /// <summary>How far a body's own paint is pulled toward its ward colour.</summary>
        ///
        /// <b>About half, and much gentler than the wards get</b> — 37p's rule, met from the other
        /// end. A turret is bought art in colours of its own, so pulling it 80% of the way leaves
        /// plenty behind; these models are mostly bone and steel, which carry almost no colour for
        /// a pull to preserve, so the same figure flattens them into one amber mass. Swept at 0.30,
        /// 0.55 and 0.82 on the board's floor: at 0.55 the cloth takes the ward's colour and the
        /// metal stays metal, which says the colour and keeps the body.
        const float Pull = .88f, SatGain = .55f, SatFloor = .26f;

        // ------------------------------------------------------------------ the menu

        [MenuItem("Glimmer Grove/Art/Bake Siege Cast (3D)", false, 40)]
        public static void Bake() => Run(write: true, contact: false);

        [MenuItem("Glimmer Grove/Art/Verify Siege Cast (3D)", false, 41)]
        public static void Verify() => Run(write: false, contact: false);

        [MenuItem("Glimmer Grove/Art/Siege Cast Contact Sheet (3D)", false, 42)]
        public static void Contact() => Run(write: false, contact: true);

        /// <summary>
        /// Renders one body at several pitches onto one sheet, for choosing <see cref="Pitch"/>.
        ///
        /// <b>It exists because that number cannot be reasoned about.</b> No gate in this project
        /// opens a PNG (invariant 32b), and the difference between an angle that reads as a figure
        /// and one that reads as a skull is not a number — it is a picture, looked at, on this
        /// board's own floor.
        /// </summary>
        [MenuItem("Glimmer Grove/Art/Survey Siege Cast Angles (3D)", false, 43)]
        public static void Angles()
        {
            GameObject stage = null;
            var ambience = Ambience.Pin(CastAmbient);
            try
            {
                stage = BuildStage(out var cam, out var key);

                // Pitch across, how hard the body is painted down. **Both in one grid**, because
                // they are not independent: a steep pitch shows less of the body, so it survives
                // less paint before it stops reading as a figure at all.
                // **Body across, pitch down**, which is the comparison that matters. A sweep of
                // pitch and paint on one body said the same thing nine times: the minion is a
                // hooded dome and reads as a skittle from every angle at every tint. What decides
                // whether a humanoid survives this camera is not the camera — it is whether the
                // model has anything that projects sideways for the silhouette to be made of,
                // which is the insects' own lesson (37ar) asked of a man instead of a beetle.
                // **Bracketing `Pitch`, which means these move when it does.** They were 62/52/42,
                // left behind when the constant went to 22 chasing a bug that was not the camera
                // at all (see `Skin`) — so the one tool for choosing the angle was surveying three
                // angles nobody was considering, all of them steeper than the shipped one. The
                // question worth a picture now is whether a cast that really animates wants to be
                // seen from further up than a cast that could not.
                var shots = new List<Texture2D>();
                foreach (float pitch in new[] { Pitch, Pitch + 12f, Pitch + 24f })
                    foreach (var body in Roster)
                        shots.Add(Still(stage.transform, cam, key, body, pitch));

                Sheet(shots, "siege_cast_angles.png", 3);
                foreach (var s in shots) Object.DestroyImmediate(s);
            }
            finally
            {
                ambience.Restore();
                if (stage != null) Object.DestroyImmediate(stage);
            }
        }

        // ------------------------------------------------------------------ the run

        static void Run(bool write, bool contact)
        {
            GameObject stage = null;
            var made = new Dictionary<string, Texture2D[]>();
            var ambience = Ambience.Pin(CastAmbient);

            try
            {
                stage = BuildStage(out var cam, out var key);

                foreach (var body in Roster)
                {
                    var reels = Reels(stage.transform, cam, key, body);
                    if (reels == null)
                    {
                        Debug.LogError("SiegeCastBake: could not build " + body.Model);
                        continue;
                    }

                    for (int i = 0; i < Letters.Length; i++)
                        made[body.Key + "_" + Letters[i]] = reels[i];
                }

                if (made.Count == 0) { Debug.LogError("SiegeCastBake: nothing baked."); return; }

                if (contact) Sheet(made.OrderBy(p => p.Key).Select(p => p.Value[0]).ToList(),
                                   "siege_cast_contact.png");
                else if (write) Write(made);
                else Check(made);
            }
            finally
            {
                foreach (var reel in made.Values)
                    foreach (var frame in reel) Object.DestroyImmediate(frame);

                ambience.Restore();
                if (stage != null) Object.DestroyImmediate(stage);
            }
        }

        /// <summary>Four reels of one body, one per ward colour, all framed identically.</summary>
        static Texture2D[][] Reels(Transform stage, Camera cam, Light key, Body body)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Source + "/" + body.Model + ".fbx");
            var clip = ClipNamed(body.Rig, body.Clip);
            if (model == null || clip == null) return null;

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.transform.SetParent(stage, false);
            var skin = new Skin(inst);

            try
            {
                // **Framed from the animation rather than from one pose.** A walk swings an axe
                // and a stride reaches; measuring the first frame and cutting to it clips whatever
                // the rest of the cycle does, which is the fault `one_canvas` exists to stop on the
                // insect side (invariant 37ar).
                var box = Extent(skin, clip);
                float pitch = body.Lean > 0f ? body.Lean : Pitch;

                var out4 = new Texture2D[Hues.Length][];
                for (int c = 0; c < Hues.Length; c++) out4[c] = new Texture2D[Frames];

                // **Rendered once and tinted four times**, rather than rendered four times with a
                // painted albedo. Which is a retreat from something better and is worth saying
                // why: a lit material genuinely shaded in its colour beats a hue rotation, and it
                // is what the turrets get — but these imported FBX materials do not answer
                // `_Color` at all. Measured: the material reads back as the colour it was set to
                // and the render comes out pixel-identical for red, green, blue and amber, on the
                // skeletons but not the knight, with no difference in shader, emission or texture
                // setup between them. Rather than ship a cast where one body takes its colour and
                // another silently does not, the colour is put on in post — which is exactly what
                // the insects do (`make_siege_art.hued`), so the whole cast is now coloured one
                // way rather than two.
                for (int f = 0; f < Frames; f++)
                {
                    skin.Pose(clip, clip.length * f / Frames);

                    var raw = Render(cam, key, box, pitch);
                    for (int c = 0; c < Hues.Length; c++) out4[c][f] = Finish(raw, Hues[c]);
                    Object.DestroyImmediate(raw);
                }

                Trim(out4);
                Moves(body, out4[0]);
                return out4;
            }
            finally { skin.Dispose(); Object.DestroyImmediate(inst); }
        }

        /// <summary>
        /// Refuses a reel whose frames are all the same picture.
        ///
        /// <para>
        /// <b>The one thing a bake of an animation must prove, and it was not being asked.</b> A
        /// clip binds to bone <em>paths</em>, so pointing a body at another pack's animation file
        /// misses every binding <b>silently</b> — nothing throws, the model renders its bind pose,
        /// and twelve identical frames come out. Everything downstream is happy: they trim, tint,
        /// outline, address, label, load as twelve distinct sprites and play through a flipbook
        /// that has nothing to show. It shipped to a device exactly that way.
        /// </para>
        /// <para>
        /// <b>And "anything, anywhere" is not enough, which cost a whole session to find out.</b>
        /// That bar passed a reel in which the <em>only</em> thing moving was a hood: KayKit hangs
        /// a helmet, a hood, a hat and a cape off a bone as plain meshes, so those animate under
        /// any conditions at all, while the skinned body they sit on was drawing its bind pose
        /// (see <c>Skin</c>). Twelve frames differ, the guard is satisfied, and what ships is a
        /// figure sliding down a hill with a rocking hat — reported from a device, correctly, as
        /// "only the hoodie moves, not even the head".
        /// </para>
        /// <para>
        /// So the reel is asked about its <b>lower half</b>, where nothing on any of these bodies
        /// is anything but skinned. The bar there is still deliberately low — this is not a
        /// judgement about whether a run reads well, it is the difference between an animation and
        /// a photograph — but it is a bar no hat can clear on a body's behalf.
        /// </para>
        /// </summary>
        static void Moves(Body body, Texture2D[] reel)
        {
            if (reel == null || reel.Length < 2) return;

            int w = reel[0].width, h = reel[0].height;
            var first = reel[0].GetPixels32();

            // `GetPixels32` runs bottom-up, so the lower half is the front of the array — the
            // legs and feet, whatever the body and whatever the camera pitch.
            int half = h / 2 * w;

            bool anywhere = false, below = false;
            for (int f = 1; f < reel.Length && !below; f++)
            {
                var here = reel[f].GetPixels32();
                for (int i = 0; i < here.Length; i++)
                {
                    if (here[i].r == first[i].r && here[i].g == first[i].g
                        && here[i].b == first[i].b && here[i].a == first[i].a) continue;

                    anywhere = true;
                    if (i < half) { below = true; break; }
                }
            }

            if (below) return;

            Debug.LogError(anywhere
                ? string.Format(
                    "SiegeCastBake: '{0}' moves only in its upper half — the skinned body of "
                    + "'{1}' is drawing its bind pose and what is animating is a helmet, hood or "
                    + "cape hung off a bone. Sampling a clip poses the bones and nothing skins "
                    + "them in edit mode; `Skin` is what turns a pose into pixels, so this means "
                    + "something is rendering the SkinnedMeshRenderers again.", body.Key, body.Model)
                : string.Format(
                    "SiegeCastBake: every frame of '{0}' is identical — '{1}' does not bind to "
                    + "{2}. A clip binds by transform path, so a body needs its own pack's "
                    + "animation file (see Body.Rig).", body.Key, body.Clip, body.Model));
        }

        // ------------------------------------------------------------------ the stage

        static GameObject BuildStage(out Camera cam, out Light key)
        {
            var go = new GameObject("~SiegeCastStage") { hideFlags = HideFlags.HideAndDontSave };
            go.transform.position = StageOrigin;

            var camGo = new GameObject("~SiegeCastCam", typeof(Camera));
            camGo.transform.SetParent(go.transform, false);

            cam = camGo.GetComponent<Camera>();

            // **Orthographic, because the board is.** A perspective camera splays a body outward
            // the further it is from the middle of the frame, which is invisible on one render and
            // is exactly what makes a row of raiders look like it is standing on a dome.
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.useOcclusionCulling = false;
            cam.nearClipPlane = .05f;
            cam.farClipPlane = 400f;
            cam.enabled = false;             // rendered by hand, never by the editor loop

            // A key and a fill. **Flat rather than dramatic**: this board is lit from nowhere in
            // particular and every other thing on it is drawn with even light, so a hard key would
            // make one cast member the only object on the hill with a shadow side.
            var keyGo = new GameObject("~SiegeCastKey", typeof(Light));
            keyGo.transform.SetParent(go.transform, false);
            key = keyGo.GetComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.05f;
            key.color = new Color(1f, .98f, .93f);

            var fillGo = new GameObject("~SiegeCastFill", typeof(Light));
            fillGo.transform.SetParent(go.transform, false);
            var fill = fillGo.GetComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = .55f;
            fill.color = new Color(.78f, .84f, 1f);
            // Kept opposite the key by turning with it (see `Yaw`), or the two collapse onto one
            // side and the body loses the only thing separating its parts from each other.
            fillGo.transform.rotation = Quaternion.Euler(18f, Yaw + 214f, 0f);

            // **A rim, which is the one light that is about the silhouette rather than the
            // form.** A key and a fill shade a body and leave its edge exactly as bright as
            // whatever it is standing on, so a flat-shaded render dropped on a lit hill reads as a
            // sticker — the keyline says *where* the body ends and nothing says it is round. A
            // low, cold light from behind catches the top of the helm, the shoulders and the
            // outside of a leg, which is the whole of why a baked cast in this genre looks
            // expensive; it is Clash Royale's own trick and it costs one light.
            //
            // **It has to come from behind the body, and getting that wrong makes it a second
            // key.** The first cut put it a few degrees off the *camera's* own yaw, which lights
            // the face — so instead of an edge it added a flat wash to the front and the whole
            // cast came out paler and softer than before. A rim is `Yaw + 180` and nothing else:
            // pointing down and back toward the lens, so what it catches is the top of the helm,
            // the shoulders and the outside of an arm, which is the geometry the camera can see
            // and the key cannot reach.
            //
            // It is deliberately *not* tinted toward the ward colour — `Tint` rotates hue and
            // keeps value (see `Finish`), so a rim is read as light rather than as paint whatever
            // colour the body ends up.
            var rimGo = new GameObject("~SiegeCastRim", typeof(Light));
            rimGo.transform.SetParent(go.transform, false);
            var rim = rimGo.GetComponent<Light>();
            rim.type = LightType.Directional;
            rim.intensity = .75f;
            rim.color = new Color(.82f, .90f, 1f);
            rimGo.transform.rotation = Quaternion.Euler(44f, Yaw + 190f, 0f);

            return go;
        }

        /// <summary>
        /// The environment lighting a bake runs under, pinned and then put back.
        ///
        /// <para>
        /// <b>It was whatever scene happened to be open, which is a bug in two directions.</b>
        /// Ambient is not part of the stage — it is a project/scene setting — so a bake took its
        /// floor light from the map scene, the boot scene or an empty one depending on what
        /// somebody had double-clicked, and <c>Verify Siege Cast (3D)</c> re-bakes in the same
        /// session and so agrees with itself no matter what. Two machines could disagree and the
        /// gate could not see it.
        /// </para>
        /// <para>
        /// <b>And the value is a look decision worth making rather than inheriting.</b> A
        /// skybox ambient washes a matte model toward the sky's colour and flattens exactly the
        /// shaded side the fill is there to keep; a flat, dim, slightly cool ambient leaves the
        /// key and the fill doing the shaping and stops the darks going to black, which is where
        /// this board's own art sits.
        /// </para>
        /// </summary>
        struct Ambience
        {
            UnityEngine.Rendering.AmbientMode _mode;
            Color _sky, _equator, _ground, _light;
            float _intensity;

            /// <summary>Pins the environment light to <paramref name="light"/> and remembers
            /// what was there. A floor and a body want different amounts of it, so the value is
            /// the caller's and only the capturing is shared.</summary>
            public static Ambience Pin(Color light)
            {
                var was = new Ambience
                {
                    _mode = RenderSettings.ambientMode,
                    _sky = RenderSettings.ambientSkyColor,
                    _equator = RenderSettings.ambientEquatorColor,
                    _ground = RenderSettings.ambientGroundColor,
                    _light = RenderSettings.ambientLight,
                    _intensity = RenderSettings.ambientIntensity,
                };

                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = light;
                RenderSettings.ambientIntensity = 1f;
                return was;
            }

            public void Restore()
            {
                RenderSettings.ambientMode = _mode;
                RenderSettings.ambientSkyColor = _sky;
                RenderSettings.ambientEquatorColor = _equator;
                RenderSettings.ambientGroundColor = _ground;
                RenderSettings.ambientLight = _light;
                RenderSettings.ambientIntensity = _intensity;
            }
        }

        static AnimationClip ClipNamed(string rig, string name)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(rig ?? Clips))
                if (o is AnimationClip c && c.name == name) return c;
            return null;
        }

        /// <summary>
        /// Holds a sampled pose so that it reaches the <em>pixels</em>, which sampling alone does
        /// not.
        ///
        /// <para>
        /// <b>This is the whole reason the cast shipped standing still.</b>
        /// <c>AnimationClip.SampleAnimation</c> poses the bone <em>transforms</em>, and in edit
        /// mode that is all it does — skinning is dispatched by the player loop, which is not
        /// running, so a <c>Camera.Render()</c> driven from a menu item draws every
        /// <c>SkinnedMeshRenderer</c> in its bind pose however the bones stand. Measured on
        /// <c>Skeleton_Warrior</c> across two poses of <c>Running_A</c>: the foot bone travels
        /// 0.75 units, the leg mesh's own skinned vertices travel 1.07, and the rendered legs
        /// travel <b>0.000</b>.
        /// </para>
        /// <para>
        /// <b>What disguised it as a camera problem is which parts of a KayKit body are not
        /// skinned.</b> A helmet, a hood, a hat and a cape are plain meshes parented to a bone, so
        /// a transform moves them and they render perfectly — and every one of them is on the head
        /// or the shoulders. So a reel showed a hood rocking above a body that never moved, which
        /// is indistinguishable from a figure whose motion the projection has eaten, and it sent
        /// two sessions into the camera pitch (<c>Pitch</c> went 52 → 22 chasing it, and the
        /// legibility argument there is still right — it simply was not this). It is also why the
        /// knight "would not animate at all" and was swapped out as unexplained: every part of a
        /// knight is skinned, so it had no moving hat to hide behind.
        /// </para>
        /// <para>
        /// <b>The fix is to do the skinning here rather than hope the engine does it.</b>
        /// <c>BakeMesh</c> is CPU skinning on demand from the bones as they stand, so it needs no
        /// player loop: each skinned renderer is switched off and a plain mesh renderer stands in
        /// its place, under the same transform and with the same materials, re-baked every frame.
        /// At one pose the two render pixel-identically — measured at 0.076 of 255, which is
        /// antialiasing — so this buys the motion and changes nothing else about the look.
        /// </para>
        /// <para>
        /// <b>Before believing a bake of an animation, compare the pixels rather than the
        /// bones.</b> Every transform-level measurement taken here was correct and told nobody
        /// anything: the clip binds, the bones move, the skin weights are right, and none of that
        /// is evidence that any of it was drawn.
        /// </para>
        /// </summary>
        sealed class Skin
        {
            readonly GameObject _inst;
            readonly SkinnedMeshRenderer[] _skinned;
            readonly MeshFilter[] _stand;
            readonly Mesh[] _mesh;
            readonly MeshRenderer[] _drawn;

            public Skin(GameObject inst)
            {
                _inst = inst;
                _skinned = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                _stand = new MeshFilter[_skinned.Length];
                _mesh = new Mesh[_skinned.Length];

                for (int i = 0; i < _skinned.Length; i++)
                {
                    var smr = _skinned[i];

                    // Parented *under* the skinned renderer rather than beside it, so the space
                    // `BakeMesh` writes into is the space the stand-in is drawn in, whatever the
                    // body's hierarchy turns out to look like.
                    var go = new GameObject("~baked_" + smr.name,
                                            typeof(MeshFilter), typeof(MeshRenderer));
                    go.transform.SetParent(smr.transform, false);
                    go.GetComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;

                    _stand[i] = go.GetComponent<MeshFilter>();
                    _mesh[i] = new Mesh { name = "~baked_" + smr.name };
                    smr.enabled = false;
                }

                // Everything the camera will actually draw: the stand-ins, and the static parts
                // that were always fine because a bone carries them.
                _drawn = inst.GetComponentsInChildren<MeshRenderer>(true);
            }

            /// <summary>Poses the body, and puts that pose where the camera can see it.</summary>
            public void Pose(AnimationClip clip, float time)
            {
                clip.SampleAnimation(_inst, time);

                for (int i = 0; i < _skinned.Length; i++)
                {
                    _skinned[i].BakeMesh(_mesh[i]);
                    _stand[i].sharedMesh = _mesh[i];
                }
            }

            /// <summary>The world box of the pose standing now.</summary>
            public Bounds Box()
            {
                Bounds box = default;
                bool any = false;

                foreach (var r in _drawn)
                {
                    if (!any) { box = r.bounds; any = true; }
                    else box.Encapsulate(r.bounds);
                }

                return box;
            }

            public void Dispose()
            {
                foreach (var m in _mesh)
                    if (m != null) Object.DestroyImmediate(m);
            }
        }

        /// <summary>
        /// The world box a whole animation sweeps, which is what the camera is set to.
        ///
        /// <b>Measured off the posed stand-ins, and it has to be.</b> A
        /// <c>SkinnedMeshRenderer</c>'s <c>bounds</c> are as stale as its vertices in edit mode
        /// (see <c>Skin</c>), so the box this used to return was the bind pose's — which framed a
        /// body that stands still perfectly well and would crop the legs off one that runs.
        /// </summary>
        static Bounds Extent(Skin skin, AnimationClip clip)
        {
            Bounds box = default;
            bool any = false;

            for (int f = 0; f < Frames; f++)
            {
                skin.Pose(clip, clip.length * f / Frames);

                var here = skin.Box();
                if (!any) { box = here; any = true; }
                else box.Encapsulate(here);
            }

            return box;
        }

        static Texture2D Render(Camera cam, Light key, Bounds box, float pitch)
        {
            float reach = Mathf.Max(box.size.x, box.size.y, box.size.z);

            var rot = Quaternion.Euler(pitch, Yaw, 0f);
            cam.transform.position = box.center - (rot * Vector3.forward) * (reach * 4f + 10f);
            cam.transform.rotation = rot;
            cam.orthographicSize = reach * .5f / Fill;

            // The key rides with the camera, so a body is lit the same whatever pitch is chosen.
            key.transform.rotation = Quaternion.Euler(pitch - 16f, Yaw + 32f, 0f);

            int side = Tall * Super;
            var rt = RenderTexture.GetTemporary(side, side, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.aspect = 1f;
            cam.Render();

            var full = new Texture2D(side, side, TextureFormat.RGBA32, false);
            RenderTexture.active = rt;
            full.ReadPixels(new Rect(0f, 0f, side, side), 0, 0);
            full.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);

            return full;
        }

        /// <summary>
        /// One rendered frame turned into a shipped one: painted its ward colour, given its
        /// keyline, and brought down to size.
        ///
        /// <b>The colour goes on before the outline, which is not an ordering detail.</b> The
        /// keyline is a flat navy and must stay navy on all four — rotate the hue of a finished
        /// frame and the outline turns red on the red raider, which reads as a glow rather than as
        /// ink.
        /// </summary>
        static Texture2D Finish(Texture2D raw, Color hue)
        {
            var tinted = Tint(raw, hue);
            var lined = Outline(tinted);
            Object.DestroyImmediate(tinted);

            var small = Shrink(lined, Tall);
            Object.DestroyImmediate(lined);
            return small;
        }

        /// <summary>
        /// Paints a rendered body one hue, keeping its shading.
        ///
        /// <b>The same rotation the insects get</b> (`make_siege_art.hued`): the hue is turned most
        /// of the way to the target by the short way round, saturation is pushed up from a floor
        /// rather than replaced — so a near-grey helm stays greyish and a coloured cloth becomes
        /// strongly coloured — and value is left exactly alone, because value is what the lighting
        /// put there and it is the only thing making these read as solid.
        /// </summary>
        static Texture2D Tint(Texture2D raw, Color hue)
        {
            float want;
            {
                Color.RGBToHSV(hue, out want, out _, out _);
            }

            var px = raw.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                if (px[i].a == 0) continue;

                Color.RGBToHSV(new Color(px[i].r / 255f, px[i].g / 255f, px[i].b / 255f),
                               out float was, out float sat, out float val);

                float step = ((want - was + 1.5f) % 1f) - .5f;
                float h = (was + step * Pull + 1f) % 1f;

                var outC = Color.HSVToRGB(h, Mathf.Clamp01(sat * SatGain + SatFloor), val);
                px[i] = new Color32((byte)(outC.r * 255f), (byte)(outC.g * 255f),
                                    (byte)(outC.b * 255f), px[i].a);
            }

            var t = new Texture2D(raw.width, raw.height, TextureFormat.RGBA32, false);
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        // ------------------------------------------------------------------ the pixels

        /// <summary>
        /// Grows the silhouette and fills it behind the body, which is the cartoon keyline.
        ///
        /// Done at <see cref="Super"/> size and brought down afterwards, so the line is soft at the
        /// edge rather than stepped — a one-pixel hard outline on a 180-tall sprite reads as
        /// aliasing rather than as ink.
        /// </summary>
        static Texture2D Outline(Texture2D src)
        {
            int w = src.width, h = src.height, grow = Keyline * Super;
            var from = src.GetPixels32();
            var to = new Color32[from.Length];

            // The distance transform is separable and this is a small image, so two passes of a
            // box maximum over alpha is both quick and exact enough for an outline.
            var near = new bool[from.Length];
            for (int i = 0; i < from.Length; i++) near[i] = from[i].a > 32;

            var wide = new bool[from.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool hit = false;
                    for (int d = -grow; d <= grow && !hit; d++)
                    {
                        int u = x + d;
                        if (u >= 0 && u < w && near[y * w + u]) hit = true;
                    }
                    wide[y * w + x] = hit;
                }

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool hit = false;
                    for (int d = -grow; d <= grow && !hit; d++)
                    {
                        int v = y + d;
                        if (v >= 0 && v < h && wide[v * w + x]) hit = true;
                    }

                    int i = y * w + x;
                    var here = from[i];

                    if (here.a > 250) { to[i] = here; continue; }

                    var ink = KeylineInk;
                    if (!hit) { to[i] = new Color32(ink.r, ink.g, ink.b, 0); continue; }

                    // The body's own antialiased rim blended over the ink, so the edge is the
                    // model's shape and not the dilation's.
                    float a = here.a / 255f;
                    to[i] = new Color32((byte)(ink.r * (1f - a) + here.r * a),
                                        (byte)(ink.g * (1f - a) + here.g * a),
                                        (byte)(ink.b * (1f - a) + here.b * a), 255);
                }

            var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            outTex.SetPixels32(to);
            outTex.Apply();
            return outTex;
        }

        /// <summary>A box average down to the shipped size, which is the anti-aliasing.</summary>
        static Texture2D Shrink(Texture2D src, int tall)
        {
            int n = src.width / tall;
            if (n <= 1) return Copy(src);

            int w = src.width / n, h = src.height / n;
            var from = src.GetPixels32();
            var to = new Color32[w * h];

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float r = 0f, g = 0f, b = 0f, a = 0f;
                    for (int j = 0; j < n; j++)
                        for (int i = 0; i < n; i++)
                        {
                            var p = from[(y * n + j) * src.width + (x * n + i)];
                            // Premultiplied, or a transparent pixel's colour bleeds into the rim.
                            float pa = p.a / 255f;
                            r += p.r * pa; g += p.g * pa; b += p.b * pa; a += pa;
                        }

                    float area = n * n;
                    byte alpha = (byte)Mathf.Clamp(Mathf.RoundToInt(a / area * 255f), 0, 255);
                    if (a <= 0.0001f) { to[y * w + x] = new Color32(0, 0, 0, 0); continue; }

                    to[y * w + x] = new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(r / a), 0, 255),
                                                (byte)Mathf.Clamp(Mathf.RoundToInt(g / a), 0, 255),
                                                (byte)Mathf.Clamp(Mathf.RoundToInt(b / a), 0, 255),
                                                alpha);
                }

            var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            outTex.SetPixels32(to);
            outTex.Apply();
            return outTex;
        }

        static Texture2D Copy(Texture2D src)
        {
            var t = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            t.SetPixels32(src.GetPixels32());
            t.Apply();
            return t;
        }

        /// <summary>
        /// Cuts every colour of one body to the same box, taken over the whole walk.
        ///
        /// <b>One box for all four, which is the point.</b> Trimming each colour to its own alpha
        /// would frame four identical bodies four slightly different ways, and the view sizes a
        /// body by its frame — so the red raider would be drawn a hair bigger than the blue one for
        /// no reason anybody could ever find.
        /// </summary>
        static void Trim(Texture2D[][] reels)
        {
            int left = int.MaxValue, right = int.MinValue, low = int.MaxValue, high = int.MinValue;

            foreach (var reel in reels)
                foreach (var frame in reel)
                {
                    var px = frame.GetPixels32();
                    for (int y = 0; y < frame.height; y++)
                        for (int x = 0; x < frame.width; x++)
                        {
                            // Anything below this cannot be seen and must never set a frame's
                            // extent — the insect side paid for that one in a hill of raiders
                            // drawn at half size (invariant 37as).
                            if (px[y * frame.width + x].a < 8) continue;
                            if (x < left) left = x;
                            if (x > right) right = x;
                            if (y < low) low = y;
                            if (y > high) high = y;
                        }
                }

            if (left > right || low > high) return;

            int w = right - left + 1, h = high - low + 1;

            for (int r = 0; r < reels.Length; r++)
                for (int f = 0; f < reels[r].Length; f++)
                {
                    var src = reels[r][f];
                    var cut = new Texture2D(w, h, TextureFormat.RGBA32, false);
                    cut.SetPixels32(src.GetPixels(left, low, w, h).Select(c => (Color32)c).ToArray());
                    cut.Apply();
                    Object.DestroyImmediate(src);
                    reels[r][f] = cut;
                }
        }

        // ------------------------------------------------------------------ the drop

        static string FolderOf(string key) =>
            Path.Combine(Application.dataPath, "Game", "Art", "Siege", key);

        static void Write(Dictionary<string, Texture2D[]> made)
        {
            try
            {
                AssetDatabase.StartAssetEditing();

                foreach (var pair in made)
                {
                    string dir = FolderOf(pair.Key);
                    Directory.CreateDirectory(dir);

                    for (int f = 0; f < pair.Value.Length; f++)
                        File.WriteAllBytes(Path.Combine(dir, string.Format("f{0:00}.png", f)),
                                           pair.Value[f].EncodeToPNG());
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }

            AssetDatabase.Refresh();
            Debug.Log(string.Format("SiegeCastBake: wrote {0} reels ({1} frames each) under "
                                    + "Art/Siege. Run Addressables > Sync All Assets and save.",
                                    made.Count, Frames));
        }

        /// <summary>
        /// Re-bakes and holds what is on disk to it, within a tolerance.
        ///
        /// <b>A tolerance rather than a byte comparison</b>, which is <c>SiegeShotBake</c>'s own
        /// bargain: two GPUs are not obliged to rasterise a triangle identically, so an exact match
        /// would fail on a different machine for a reason that is nobody's fault.
        /// </summary>
        static void Check(Dictionary<string, Texture2D[]> made)
        {
            int missing = 0, differ = 0;

            foreach (var pair in made)
                for (int f = 0; f < pair.Value.Length; f++)
                {
                    string path = Path.Combine(FolderOf(pair.Key), string.Format("f{0:00}.png", f));
                    if (!File.Exists(path)) { missing++; continue; }

                    var had = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    had.LoadImage(File.ReadAllBytes(path));

                    if (had.width != pair.Value[f].width || had.height != pair.Value[f].height)
                        differ++;
                    else
                    {
                        var a = had.GetPixels32();
                        var b = pair.Value[f].GetPixels32();
                        long sum = 0;
                        for (int i = 0; i < a.Length; i++)
                            sum += Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g)
                                 + Mathf.Abs(a[i].b - b[i].b) + Mathf.Abs(a[i].a - b[i].a);

                        if (sum / (double)(a.Length * 4) > 3.0) differ++;
                    }

                    Object.DestroyImmediate(had);
                }

            if (missing > 0 || differ > 0)
                Debug.LogError(string.Format("SiegeCastBake: {0} missing, {1} differ — re-bake.",
                                             missing, differ));
            else
                Debug.Log(string.Format("SiegeCastBake: {0} reels are what this tool bakes.",
                                        made.Count));
        }

        static Texture2D Still(Transform stage, Camera cam, Light key, Body body, float pitch)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Source + "/" + body.Model + ".fbx");
            var clip = ClipNamed(body.Rig, body.Clip);
            if (model == null || clip == null) return null;

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.transform.SetParent(stage, false);
            var skin = new Skin(inst);

            try
            {
                var box = Extent(skin, clip);
                skin.Pose(clip, clip.length * .3f);

                var raw = Render(cam, key, box, pitch);
                var shot = Finish(raw, Hues[3]);
                Object.DestroyImmediate(raw);
                return shot;
            }
            finally { skin.Dispose(); Object.DestroyImmediate(inst); }
        }

        /// <summary>
        /// Lays frames out on the board's own floor at the size a phone draws them.
        ///
        /// <b>On the floor rather than on a swatch</b>, because the question this answers is never
        /// "is the render clean" — it is whether the thing sits on this board beside the cast that
        /// is already there. A contact sheet on grey says yes to everything.
        /// </summary>
        static void Sheet(List<Texture2D> shots, string file, int rows = 1)
        {
            shots = shots.Where(s => s != null).ToList();
            if (shots.Count == 0) return;

            const int Pane = 300;
            int cols = Mathf.CeilToInt(shots.Count / (float)rows);
            var sheet = new Texture2D(Pane * cols, Pane * rows, TextureFormat.RGBA32, false);

            var floor = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Game/Art/Siege/hill1.png");

            var ground = new Color32[Pane * Pane];
            for (int i = 0; i < ground.Length; i++) ground[i] = new Color32(58, 66, 52, 255);
            if (floor != null && floor.isReadable)
                for (int y = 0; y < Pane; y++)
                    for (int x = 0; x < Pane; x++)
                        ground[y * Pane + x] = floor.GetPixelBilinear(x / (float)Pane,
                                                                     y / (float)Pane);

            for (int s = 0; s < shots.Count; s++)
            {
                int cx = (s % cols) * Pane, cy = (rows - 1 - s / cols) * Pane;
                sheet.SetPixels32(cx, cy, Pane, Pane, ground);

                var src = shots[s];
                float scale = Mathf.Min(Pane * .88f / src.width, Pane * .88f / src.height);
                int w = Mathf.Max(1, (int)(src.width * scale)), h = Mathf.Max(1, (int)(src.height * scale));
                int ox = cx + (Pane - w) / 2, oy = cy + (Pane - h) / 2;

                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        var p = src.GetPixelBilinear(x / (float)w, y / (float)h);
                        var under = sheet.GetPixel(ox + x, oy + y);
                        sheet.SetPixel(ox + x, oy + y, Color.Lerp(under, p, p.a));
                    }
            }

            sheet.Apply();
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Tools", file);
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            Debug.Log("SiegeCastBake: wrote " + path + " — look at it.");
        }
    }
}
