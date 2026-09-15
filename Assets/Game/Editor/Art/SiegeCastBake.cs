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

        /// <summary>One body: which model, which clips, and which reel it becomes.</summary>
        struct Body
        {
            public string Key;        // the reel prefix — `SiegeView.Skin` names these
            public string Model;      // the FBX under Source
            public string Clip;       // the animation to run through
            public string Rig;        // the animation file whose bone paths this body matches
            public float Lean;        // degrees of camera pitch, if this one wants its own

            /// <summary>
            /// A second clip: what a <b>boss</b> throws in. Null for a raider, which has one reel.
            ///
            /// Its presence is what makes a row a boss — see <see cref="Bosses"/> — because every
            /// other difference follows from it: three reels on one canvas, no ward colour, and a
            /// height of its own.
            /// </summary>
            public string Cast;

            /// <summary>
            /// A third clip: what a <b>boss</b> walks on in. Null for a raider, whose one reel is
            /// its walk already.
            ///
            /// <para>
            /// <b>A boss is the only body on this hill that ever stops, which is why it is the
            /// only one that needs two.</b> A raider walks for its whole life, so the reel it
            /// walks in is also the reel it stands in and nothing has to choose. A boss comes
            /// down to <c>SiegeTuning.HoldOf</c> and then holds the middle of the hill for the
            /// rest of the fight (37t) — so one reel has to be wrong at one end of that, and the
            /// one it shipped with was the idle: a <em>standing</em> clip played over a widget
            /// the view was translating down the hill, which is a figure sliding rather than
            /// walking. Reported from play in exactly those words, and measurable after the fact:
            /// across the twelve frames of the shipped <c>caller</c> reel the mean pixel
            /// difference from the first frame is <b>2.2</b>, against 30 to 37 for every other
            /// body reel this mode draws. It was a photograph.
            /// </para>
            /// <para>
            /// <b>It comes out of a different animation file from <see cref="Cast"/>, which is
            /// why <see cref="WalkRig"/> exists</b> — the movement library holds no idle and the
            /// general library holds no walk, so a boss genuinely needs both and a single
            /// <see cref="Rig"/> cannot name them. That is the one place this pack will bite: a
            /// clip binds by transform <em>path</em>, so the wrong file misses every binding
            /// silently (see <see cref="AdventurerClips"/>), which is why <see cref="Moves"/> is
            /// now asked of this reel as well as of the stand.
            /// </para>
            /// </summary>
            public string Walk;

            /// <summary>The animation file <see cref="Walk"/> lives in. See that field.</summary>
            public string WalkRig;

            /// <summary>The height this body is cut at, or nought for <see cref="Tall"/>.</summary>
            public int Height;

            /// <summary>
            /// What this body is <b>holding</b>, hung off the rig's own hand sockets.
            ///
            /// <para>
            /// <b>Every KayKit character ships unarmed, and for two chapters this cast shipped
            /// that way with it</b> — reported in one line: <em>they don't have weapons or
            /// anything, they should</em>. It is not a shortcoming of the pack. KayKit models a
            /// weapon as a separate mesh and gives the rig two empty transforms,
            /// <c>handslot.l</c> and <c>handslot.r</c>, for somebody to hang one on; a body with
            /// nothing in them is a body nobody finished dressing.
            /// </para>
            /// <para>
            /// <b>So the socket is the seam and there is no offset anywhere in this file.</b> A
            /// weapon is instantiated under the slot at identity and the rig carries it — which
            /// is what makes an axe swing when an arm swings, and what would be a table of
            /// hand-tuned positions per body per weapon if the pack had not provided them. The
            /// one rule is that gear is fitted <em>before</em> <see cref="Skin"/> is built, so
            /// its renderer is in that class's drawn set: a weapon added afterwards renders and
            /// is invisible to <see cref="Skin.Box"/>, which frames the shot — so it would be cut
            /// off at the edge of every frame with nothing saying so.
            /// </para>
            /// <para>
            /// <b>A weapon carries its owner's material</b> — <c>sword_1handed</c> is painted in
            /// the knight's palette, <c>axe_2handed</c> in the barbarian's — so a body and what it
            /// holds cannot drift apart in colour, and <see cref="Tint"/> then pulls both the same
            /// way. Arming a body across packs is legal and is a decision: it reads as loot.
            /// </para>
            /// </summary>
            public Gear[] Hold;

            /// <summary>
            /// What this body <b>swings</b> when it reaches the ward line, or null for one that
            /// keeps walking.
            ///
            /// <para>
            /// <b>A raider that reaches the line stands there hitting it</b> every
            /// <c>SiegeTuning.BlowEvery</c> until something kills it, and what that looked like on
            /// every baked body was a run cycle looping in place against a turret — invariant
            /// 37u's complaint (a body doing the wrong thing where it stands) arriving through the
            /// art. The bone cast answered it from a bought sheet
            /// (<c>make_siege_art.walk_and_swing</c>); this answers it from the rig, which is
            /// strictly better, because the swing and the walk are then the same body posed by the
            /// same skeleton rather than two cuts of two drawings.
            /// </para>
            /// <para>
            /// <b>It is the reason <see cref="Gear"/> had to come first.</b> An empty hand swinging
            /// is a body waving at a turret; the gesture only reads because something long and
            /// bright travels through it, which is 37ar's rule (a silhouette has to be made of
            /// something) asked of six frames instead of a pose.
            /// </para>
            /// </summary>
            public string Swing;

            /// <summary>The animation file <see cref="Swing"/> lives in.</summary>
            public string SwingRig;
        }

        /// <summary>
        /// One thing a body holds, and which hand it is in.
        ///
        /// <b>A struct rather than a bare model name, because which hand is a decision and not a
        /// property of the weapon.</b> A shield in the off hand and a sword in the main is a
        /// bulwark; the same two swapped is a body that reads as fumbling. The rig's sockets are
        /// mirrored, so nothing else distinguishes them.
        /// </summary>
        struct Gear
        {
            public string Model;      // the FBX under Source/Gear
            public bool Left;         // the off hand rather than the main one
        }

        static Gear Main(string model) => new Gear { Model = model, Left = false };
        static Gear Off(string model) => new Gear { Model = model, Left = true };

        const string Source = "Assets/Game/Editor/Art/KayKit";

        /// <summary>Where the weapons and shields live, beside the bodies that hold them.</summary>
        const string Armoury = Source + "/Gear";

        /// <summary>The rig transforms a weapon may be hung from. KayKit's own names.</summary>
        const string MainHand = "handslot.r", OffHand = "handslot.l";

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
        /// The melee library: what a body does when it reaches the ward line.
        ///
        /// <para>
        /// <b>It is the shared <c>Rig_Medium</c> library rather than either character pack's own
        /// copy, and that is safe for exactly the reason <see cref="AdventurerClips"/> is not.</b>
        /// A clip binds by transform <em>path</em>, and the path that matters is the one under the
        /// rig root — which every KayKit humanoid shares, because they are all bound to one
        /// skeleton. What breaks is a clip authored against a <em>different</em> rig's hierarchy;
        /// the Character Animations pack is that one hierarchy's own library, so it binds to all
        /// of them. <see cref="Moves"/> is what proves that claim rather than this sentence:
        /// every reel cut from here is asked whether its lower half actually moved.
        /// </para>
        /// <para>
        /// <b>Which attack a body swings is a decision about its weapon</b>, not a default: a
        /// one-handed chop is a different gesture from a two-handed slice, and a body holding an
        /// axe in both hands playing the one-handed clip swings through its own off hand.
        /// </para>
        /// </summary>
        const string MeleeClips = Source + "/Rig_Medium_CombatMelee.fbx";

        /// <summary>
        /// The ranged library, which today is one boss's.
        ///
        /// <b>Kept separate from <see cref="MeleeClips"/> for the plainest reason</b>: a bow is
        /// drawn and loosed, and there is no melee clip that does not look like a body throwing
        /// its bow at something.
        /// </summary>
        const string RangedClips = Source + "/Rig_Medium_CombatRanged.fbx";

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
        /// <b>Every body here is armed, and until this drop not one of them was.</b> KayKit hangs a
        /// weapon off <c>handslot.r</c> rather than modelling it into the mesh, so an unarmed cast
        /// is the pack's default and not its intent — see <see cref="Body.Hold"/>. What it bought
        /// beyond the obvious is the <em>swing</em>: a body with nothing in its hands reaching the
        /// ward line has no gesture worth drawing, so the whole cast walked on the spot against a
        /// turret for two chapters (invariant 37u through the art). Each kind's weapon is chosen to
        /// say its kind a second time in <see cref="Body.Swing"/>'s own vocabulary — a blade
        /// creeps, a long axe is a brute, a shield is a bulwark (37bu) — so the silhouette now
        /// carries the kind twice and the colour is still said by the tint, the bake and the ring.
        static readonly Body[] Roster =
        {
            new Body { Key = "kayMon",     Model = "Skeleton_Rogue",   Clip = "Running_A",
                       Rig = Clips,
                       Hold = new[] { Main("Skeleton_Blade") },
                       Swing = "Melee_1H_Attack_Slice_Diagonal", SwingRig = MeleeClips },
            new Body { Key = "kayBrute",   Model = "Skeleton_Warrior", Clip = "Running_A",
                       Rig = Clips,
                       Hold = new[] { Main("Skeleton_Axe") },
                       Swing = "Melee_1H_Attack_Chop", SwingRig = MeleeClips },
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
                       Rig = Clips,
                       // The one body on this hill that carries a shield, and it carries it in the
                       // off hand so the sword is free — which is also why its swing is the block's
                       // own counter rather than a chop: a bulwark that drops its guard to hit is
                       // a bulwark for six frames a blow.
                       Hold = new[] { Main("sword_1handed"), Off("shield_square") },
                       Swing = "Melee_Block_Attack", SwingRig = MeleeClips },
        };

        /// <summary>
        /// The <b>iron</b> cast: the twelve bodies the fourth chapter draws.
        ///
        /// <para>
        /// <b>A living warband rather than a fourth kind of dead thing</b>, which is the one axis
        /// the three shipped casts leave open: insects, blobs and skeletons are all *things*, and
        /// what none of them is is somebody who chose to come. The chapter reads as a raid rather
        /// than an infestation, and the bodies are the whole of what says so.
        /// </para>
        /// <para>
        /// <b>Nothing here is borrowed from <see cref="Roster"/>, and the knight is the one that
        /// had to be argued.</b> It is the obvious bulwark in the pack and it is already the
        /// Infinite lane's — which is *one tap* from this chapter on the same map (invariant 43),
        /// so a player would meet the same armoured body in two places and read the second as the
        /// first. A cast is told apart by its bodies or it is not told apart at all (37bu), so the
        /// engineer takes the shield instead: a spiked pavise and a one-handed axe, which is a
        /// sapper rather than a knight and reads as a different trade at the same silhouette
        /// weight.
        /// </para>
        /// <para>
        /// <b>Every body is armed and every body swings</b> — see <see cref="Roster"/>'s note,
        /// which this cast is the first to be built under rather than retro-fitted to.
        /// </para>
        /// <para>
        /// <b>And the first cut of it was re-bodied on the strength of one render, which is what
        /// a contact sheet is for</b> (32b). The adventurer pack's bodies share one chibi torso and
        /// at this camera almost nothing about a *body* separates them — held up beside the kay
        /// cast, whose hooded skull, horned helm and closed visor read instantly, the three came
        /// out as "a woman in a dress, a teddy bear and a man in goggles". So the silhouette is
        /// made of the two things that do survive the projection: a **hood** where the rogue had
        /// hair, and gear a size up. The oversized axe and the skeletons' large shield are
        /// deliberately out of scale for a medium body — a brute whose reach you can read while it
        /// is still walking is the whole of what a brute is for (37ar: a silhouette has to be made
        /// of something), and at the true size the two-handed axe was a sliver behind the torso.
        /// </para>
        /// </summary>
        static readonly Body[] IronRoster =
        {
            // Two daggers rather than one, which is the cheapest legible difference in the set: a
            // dual-wield slice throws a blade out on *both* sides of the body, so a creeper's
            // swing is the only one on the hill that is symmetrical and it reads at a glance
            // against the brute's single arc.
            new Body { Key = "ironMon",     Model = "Rogue_Hooded", Clip = "Running_A",
                       Rig = AdventurerClips,
                       Hold = new[] { Main("dagger"), Off("dagger") },
                       Swing = "Melee_Dualwield_Attack_Slice", SwingRig = MeleeClips },

            // The two-handed axe is the longest thing any raider carries, and length is what a
            // brute is for: it is the body the player has to answer before it is in range, so its
            // reach has to be visible while it is still walking.
            new Body { Key = "ironBrute",   Model = "Barbarian", Clip = "Running_A",
                       Rig = AdventurerClips,
                       Hold = new[] { Main("axe_2handed_Large") },
                       Swing = "Melee_2H_Attack_Chop", SwingRig = MeleeClips },

            new Body { Key = "ironBulwark", Model = "Engineer",  Clip = "Running_A",
                       Rig = AdventurerClips,
                       Hold = new[] { Main("axe_1handed_Large"), Off("Skeleton_Shield_Large_A") },
                       Swing = "Melee_Block_Attack", SwingRig = MeleeClips },
        };

        /// <summary>Every raider baked here, in the order a player meets their chapters.</summary>
        static Body[] AllRaiders => Roster.Concat(IronRoster).ToArray();

        /// <summary>
        /// The bosses baked here, which today is the one the third chapter ends on.
        ///
        /// <para>
        /// <b>A separate roster rather than a flag on <see cref="Roster"/>, because almost nothing
        /// about the two is the same.</b> A raider is one reel, tinted four ways, cut at the cast's
        /// own height and drawn at a cell and a bit. A boss is <em>two</em> reels on one canvas so
        /// it cannot change size when it throws, in the colours the pack painted, cut at the height
        /// the view really draws it, and there is exactly one of it on the hill.
        /// </para>
        /// <para>
        /// <b>Why the bonecaller is baked at all, when the other five bosses are cut from 2D
        /// packs.</b> It was the survival pack's own robed caster and the owner's verdict on it was
        /// one line: wrong, use something else. There is nothing else — surveyed, the 2D character
        /// packs on this machine hold eighty-odd small cartoon monsters and ten neighbourhood
        /// zombies, none of them boss-shaped, and the four blob bosses and the gravemaw already
        /// take every body the top-down monster pack has. That is invariant 37at arriving for the
        /// second time and for the same structural reason: the market has no more of these, so the
        /// answer is to render one.
        /// </para>
        /// <para>
        /// <b>The skeleton mage, and it was chosen on the board rather than on a contact sheet.</b>
        /// Five candidates were rendered at this camera and then <em>on the hill at true relative
        /// scale</em>, which is the only comparison that answers anything: the hooded rogue is the
        /// minion's fault again (a featureless dome), the large barbarian reads as a lump, the mage
        /// as a girl in a hat, and the <b>druid</b> — which wins the silhouette test outright, its
        /// antlers being the one thing in the set that projects sideways — reads on the hill as a
        /// <em>friendly RPG mascot</em>. That is the finding worth keeping: <b>projecting sideways
        /// is necessary and is not sufficient, and the second half is only visible on the board.</b>
        /// </para>
        /// <para>
        /// <b>It is a skeleton in front of skeletons, which is the one thing that had to be argued
        /// rather than assumed</b> (invariant 37z: a boss may not be the wave behind it drawn
        /// bigger). Three things separate them and all three are visible in one frame: the raiders
        /// are <em>flat</em> 2D cartoon bone, hue-rotated into the four ward colours, at a cell and
        /// a bit; this is shaded, plum-robed, and drawn three cells tall. What it reads as is the
        /// thing that raised them, which is exactly what its spell does.
        /// </para>
        /// <para>
        /// <b>It stands <em>and</em> walks, and shipping only the first half of that was the
        /// fault.</b> A boss walks to the middle of the hill and then holds it, so what it is
        /// drawn doing for most of the fight is standing — which is why the stand is the reel it
        /// was given, and a run cycle looping under a body that has stopped is invariant 37u's
        /// fault in one word. The half nobody wrote down is that the other three seconds are a
        /// walk, and a stand played over them is the same fault pointing the other way: a figure
        /// sliding down a hill. **Two reels rather than a compromise between them**, chosen by
        /// <c>SiegeRaider.InPlace</c>, which is the model's own answer to "has it arrived".
        /// See <see cref="Body.Walk"/>.
        /// </para>
        /// </summary>
        static readonly Body[] Bosses =
        {
            // **The walk is `Running_A`, and that is <see cref="Clips"/>'s own finding applied to
            // the one body that was exempted from it by accident.** That doc has said since the
            // cast was built that a humanoid's walk does not survive this camera — the limbs swing
            // along the body's own axis, straight into the occluded direction, so `Walking_A` and
            // `Walking_C` are almost indistinguishable at any pitch. Every raider here therefore
            // runs. The boss then authored `Walking_A` because the field is called `Walk`, and
            // what shipped is exactly what that doc predicts: measured on the reel, its silhouette
            // bobbed **0.80%** of its own height across the cycle and its feet swung **0.8%** of
            // their spread, against 3.9-7.7% and 16-36% for every running body on this hill. A
            // figure swaying on the spot while it is translated down a slope is a figure sliding,
            // and it was reported from play as exactly that. <see cref="Strides"/> is the gate.
            new Body { Key = "caller", Model = "Skeleton_Mage", Clip = "Idle_A", Cast = "Throw",
                       Walk = "Running_A", WalkRig = Clips,
                       Rig = SkeletonGeneral, Height = BossTall },

            // ---------------------------------------------------------- the fourth chapter's two
            // **The shackler, and its whole silhouette is the thing in its hands.** A ranger is
            // the slightest body in either pack — narrower than the rogue and a head shorter than
            // the knight — and at three cells tall that is a stick. A drawn longbow is nearly as
            // wide as the body is high and sits *across* it, which is the one thing in the set
            // that projects sideways without being an antler (37ar, 37bx). It is also the only
            // ranged body on any hill in this mode, so what it reads as is a thing that reaches
            // the line without walking to it — which is exactly what a bind does.
            //
            // **The bow is in the off hand**, which is not a preference: KayKit's `Ranged_Bow_*`
            // clips draw with the right and hold with the left, so a bow in the main hand is a
            // body pulling a string that is not there.
            //
            // Its stand is an *aim* rather than an idle. A boss that has reached its ground holds
            // the middle of the hill for the rest of the fight (37t), and an archer standing at
            // ease for thirty seconds between loosings is the bonecaller's photograph (37cm)
            // wearing a bow.
            new Body { Key = "snare", Model = "Ranger",
                       Clip = "Ranged_Bow_Aiming_Idle", Rig = RangedClips,
                       Cast = "Ranged_Bow_Release",
                       Walk = "Running_A", WalkRig = Clips,
                       Hold = new[] { Off("bow_withString") },
                       Height = BossTall },

            // **The ironclad is the first body in this mode on a different rig, and that is what
            // buys the size.** Every other humanoid here is `Rig_Medium` and stands 2.4-2.8 units;
            // the large barbarian is `Rig_Large` and stands 4.5 by 5.8 — wider than it is tall,
            // which no `Rig_Medium` body is at any pose. So it does not need a boss's height
            // multiplier to read as a boss: it is one, in the geometry, beside a cast it shares a
            // palette with.
            //
            // **`Rig_Large` is a genuinely different skeleton and the failure is silent**, which
            // is this pack's one trap (see <see cref="AdventurerClips"/>) and was measured here
            // rather than assumed: `Rig_Medium/Running_A` sampled onto this body moves its foot
            // **0.00** units and renders a perfect bind pose. Every clip below comes from the
            // `Rig_Large` libraries, and <see cref="Moves"/> is what holds that to the pixels.
            //
            // Its stand is the two-handed guard rather than `Idle_A` — measured at 0.03 units of
            // travel, `Idle_A` on this rig is the photograph 37cm is about.
            new Body { Key = "clad", Model = "Barbarian_Large",
                       Clip = "Melee_2H_Idle", Rig = LargeMelee,
                       Cast = "Melee_2H_Slam",
                       Walk = "Running_A", WalkRig = LargeClips,
                       Hold = new[] { Main("axe_2handed_Large") },
                       Height = BossTall },
        };

        /// <summary>The <c>Rig_Large</c> movement library. See the ironclad in <see cref="Bosses"/>.</summary>
        const string LargeClips = Source + "/Rig_Large_MovementBasic.fbx";

        /// <summary>The <c>Rig_Large</c> melee library — the ironclad's guard and its slam.</summary>
        const string LargeMelee = Source + "/Rig_Large_CombatMelee.fbx";

        /// <summary>
        /// The Skeletons pack's general animations — idles, throws, hits.
        ///
        /// <b>A third animation file, and it is here because the movement library has no idle in
        /// it at all.</b> Every raider above runs, so <see cref="Clips"/> was enough; a boss walks
        /// to the middle of the hill and then holds it, so what it is drawn doing for most of the
        /// fight is standing still. <c>Idle_A</c> and <c>Throw</c> both live here.
        ///
        /// <b>The Adventurers pack's copy of this file is deliberately not imported.</b> It would
        /// be needed by a boss taken from that pack and there is not one — see
        /// <see cref="AdventurerClips"/> for why the two cannot be shared: a clip binds by
        /// transform path, so the wrong file misses every binding <em>silently</em>.
        /// </summary>
        const string SkeletonGeneral = Source + "/Rig_Medium_General.fbx";

        /// <summary>
        /// How tall a boss is cut, against a raider's <see cref="Tall"/>.
        ///
        /// <b>The height the view really draws it at, so nothing is ever upscaled</b>, and it is
        /// the same number every 2D boss in this mode is cut at: `SiegeTuning.TallOf` gives a
        /// bonecaller 3.6 cells, and the cut heights of the six run about 110 pixels a cell.
        /// A raider's 384 is far denser because a raider is drawn at a cell and a bit.
        /// </summary>
        const int BossTall = 400;

        /// <summary>Frames kept from a boss's stand, from its throw, and from its walk.</summary>
        ///
        /// <b>The walk takes the cast's own <see cref="Frames"/>, because it is the same kind of
        /// thing</b> — a cycle that has to loop seamlessly — where the stand and the throw are cut
        /// at whatever their gesture needs.
        const int BossFrames = 12, BossCastFrames = 14, BossWalkFrames = 12;

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

        /// <summary>
        /// The same, for the <see cref="Bosses"/> alone.
        ///
        /// <b>Because the two rosters are baked for different reasons and a fix to one should not
        /// rewrite the other.</b> A GPU is not obliged to rasterise a triangle the same way twice
        /// (see <see cref="Check"/>, which is why that holds a tolerance rather than bytes), so a
        /// full bake run to change one boss writes a hundred and forty-four raider PNGs that
        /// differ from the committed ones by a level here and there — a diff nobody can read, over
        /// art nobody touched. This writes the three reels the boss roster owns and nothing else.
        /// </summary>
        [MenuItem("Glimmer Grove/Art/Bake Siege Bosses (3D)", false, 41)]
        public static void BakeBosses() => Run(write: true, contact: false, raiders: false);

        [MenuItem("Glimmer Grove/Art/Verify Siege Cast (3D)", false, 42)]
        public static void Verify() => Run(write: false, contact: false);

        [MenuItem("Glimmer Grove/Art/Siege Cast Contact Sheet (3D)", false, 43)]
        public static void Contact() => Run(write: false, contact: true);

        /// <summary>
        /// Renders one body at several pitches onto one sheet, for choosing <see cref="Pitch"/>.
        ///
        /// <b>It exists because that number cannot be reasoned about.</b> No gate in this project
        /// opens a PNG (invariant 32b), and the difference between an angle that reads as a figure
        /// and one that reads as a skull is not a number — it is a picture, looked at, on this
        /// board's own floor.
        /// </summary>
        [MenuItem("Glimmer Grove/Art/Survey Siege Cast Angles (3D)", false, 44)]
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
                    foreach (var body in AllRaiders)
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

        /// <summary>An empty roster, so a partial run is a different list rather than a branch.</summary>
        static readonly Body[] NoBodies = new Body[0];

        static void Run(bool write, bool contact, bool raiders = true, bool bosses = true)
        {
            GameObject stage = null;
            var made = new Dictionary<string, Texture2D[]>();
            var ambience = Ambience.Pin(CastAmbient);

            try
            {
                stage = BuildStage(out var cam, out var key);

                foreach (var body in raiders ? AllRaiders : NoBodies)
                {
                    var reels = Reels(stage.transform, cam, key, body);
                    if (reels == null)
                    {
                        Debug.LogError("SiegeCastBake: could not build " + body.Model);
                        continue;
                    }

                    for (int i = 0; i < Letters.Length; i++)
                        made[body.Key + "_" + Letters[i]] = reels[i];

                    // **An armed body brings four more**, and the suffix is written out here
                    // rather than assembled at the call site for `Tools/verify/artnames.py`'s
                    // reason - it is `SiegeMode.CastSwing`'s own literal, at both ends.
                    if (reels.Length <= Letters.Length) continue;

                    for (int i = 0; i < Letters.Length; i++)
                        made[body.Key + "_" + Letters[i] + "_swing"] = reels[Letters.Length + i];
                }

                // **The bosses, which are the same stage and nothing else the same.** See
                // `Bosses`. Three reels, one canvas, no ward colour, and a height of their own.
                foreach (var body in bosses ? Bosses : NoBodies)
                {
                    var reels = BossReels(stage.transform, cam, key, body);
                    if (reels == null)
                    {
                        Debug.LogError("SiegeCastBake: could not build boss " + body.Model);
                        continue;
                    }

                    made[body.Key] = reels[0];
                    made[body.Key + "_cast"] = reels[1];

                    // The suffix is `SiegeView.BossWalk`'s literal, written out at both ends for
                    // `Tools/verify/artnames.py`'s reason: a name assembled at the call site is a
                    // name that gate cannot hold to disk.
                    if (reels.Length > 2) made[body.Key + "_walk"] = reels[2];
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

            // Refused rather than silently skipped: a body that authors a swing this rig has never
            // heard of is a typo, and a cast that quietly ships one reel short is a raider walking
            // on the spot at the ward line with every gate green.
            var swing = ClipNamed(body.SwingRig ?? MeleeClips, body.Swing);
            if (!string.IsNullOrEmpty(body.Swing) && swing == null)
            {
                Debug.LogError(string.Format(
                    "SiegeCastBake: '{0}' asks for the swing clip '{1}', which is not in {2}.",
                    body.Key, body.Swing, body.SwingRig ?? MeleeClips));
                return null;
            }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.transform.SetParent(stage, false);

            // **Before the skin, and that ordering is load-bearing** — see <see cref="Fit"/>.
            if (!Fit(inst, body)) { Object.DestroyImmediate(inst); return null; }

            var skin = new Skin(inst);

            try
            {
                // **Framed from the animation rather than from one pose.** A walk swings an axe
                // and a stride reaches; measuring the first frame and cutting to it clips whatever
                // the rest of the cycle does, which is the fault `one_canvas` exists to stop on the
                // insect side (invariant 37ar).
                var box = Extent(skin, clip);
                float pitch = body.Lean > 0f ? body.Lean : Pitch;

                if (swing != null) return Armed(cam, key, skin, body, clip, swing, box, pitch);

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
        /// Hangs this body's gear off the rig's own hand sockets.
        ///
        /// <para>
        /// <b>It must run before <see cref="Skin"/> is constructed, and that is the only rule
        /// here.</b> That class collects the renderers it will draw and the renderers it will
        /// measure once, in its constructor; a weapon parented afterwards is drawn but is not in
        /// <see cref="Skin.Box"/>, so it never reaches <see cref="Extent"/> and the camera is
        /// framed as though the body were empty-handed. What ships then is an axe cropped at the
        /// edge of every frame — which imports, addresses, audits and animates, and is only
        /// visible if somebody looks (invariant 32b).
        /// </para>
        /// <para>
        /// <b>A missing socket or a missing weapon is an error rather than a shrug</b>, for
        /// invariant 7b's reason read one step back: a body that silently declines to pick up its
        /// weapon is the unarmed cast this drop exists to replace, and it would ship green.
        /// </para>
        /// <para>
        /// <b>Nothing is offset, rotated or scaled.</b> KayKit authors <c>handslot.l</c> and
        /// <c>handslot.r</c> at the grip, so identity is the right transform and any number typed
        /// here would be a number that has to be re-tuned per body per weapon — thirty-odd of them
        /// across this roster, each invisible to every gate.
        /// </para>
        /// </summary>
        static bool Fit(GameObject inst, Body body)
        {
            if (body.Hold == null || body.Hold.Length == 0) return true;

            Transform main = null, off = null;
            foreach (var t in inst.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == MainHand) main = t;
                else if (t.name == OffHand) off = t;
            }

            foreach (var gear in body.Hold)
            {
                var slot = gear.Left ? off : main;
                if (slot == null)
                {
                    Debug.LogError(string.Format(
                        "SiegeCastBake: '{0}' has no {1} to hold '{2}' in.",
                        body.Key, gear.Left ? OffHand : MainHand, gear.Model));
                    return false;
                }

                var mesh = AssetDatabase.LoadAssetAtPath<GameObject>(
                    Armoury + "/" + gear.Model + ".fbx");

                if (mesh == null)
                {
                    Debug.LogError(string.Format(
                        "SiegeCastBake: '{0}' asks for the gear '{1}', which is not in {2}.",
                        body.Key, gear.Model, Armoury));
                    return false;
                }

                var held = (GameObject)PrefabUtility.InstantiatePrefab(mesh);
                held.transform.SetParent(slot, false);
            }

            return true;
        }

        /// <summary>Frames kept from a swing. <c>make_siege_art.SWING_FRAMES</c>, matched.</summary>
        const int SwingFrames = 8;

        /// <summary>
        /// The largest frame this folder may ship, which is <c>ProjectSetup</c>'s own cap on
        /// <c>/Art/Siege/</c>.
        ///
        /// <b>Named here so a swing canvas can be held under it rather than discovered over
        /// it.</b> A texture above the cap is not refused — the importer halves it, silently, so
        /// what ships is a reel drawn at half the resolution of the walk it belongs to, costing
        /// the disk and giving nothing back (that rule's own warning, and <c>Tall</c>'s note).
        /// </summary>
        const int MostPixels = 512;

        /// <summary>
        /// A body's eight reels: four colours of walk, and four of the swing it makes at the line.
        ///
        /// <para>
        /// <b>The difficulty is entirely that the two reels must share a pixel scale and must not
        /// share a canvas</b>, which is <c>make_siege_art.walk_and_swing</c>'s finding arriving on
        /// the 3D side. A swing throws a two-handed axe far outside the box the walk sweeps:
        /// framing both to the union would draw every raider in the chapter at a fraction of its
        /// size <em>for the whole run</em>, for the sake of six frames at the ward line, and
        /// framing each to its own box would make the body jump the moment it arrives.
        /// </para>
        /// <para>
        /// <b>So there is one camera box and one square, and the two reels are cut out of it
        /// differently.</b> The box is the walk's, expanded symmetrically about the walk's own
        /// centre until it contains the swing; the square grows in exact proportion, which is what
        /// keeps world-units-per-pixel identical to what the walk alone would have had —
        /// <c>orthographicSize</c> scales with the box and the render target scales with it, so
        /// the two cancel. The walk is then cut tight to its own alpha and the swing to a box
        /// mirrored about the walk body's middle, so the body sits at the same pixels in the same
        /// place in both and <c>SiegeView.Wear</c>'s <c>Grown</c> reads the difference straight off
        /// the sprites.
        /// </para>
        /// <para>
        /// <b>The square is capped at <see cref="MostPixels"/> rather than allowed to run past
        /// it.</b> Over the cap the importer halves the reel without saying so; under it the body
        /// is drawn a few per cent smaller than it could be and everything downstream is honest.
        /// The view sizes a raider in <em>cells</em> (<c>SiegeTuning.TallOf</c>), so this decides
        /// crispness and nothing else.
        /// </para>
        /// </summary>
        static Texture2D[][] Armed(Camera cam, Light key, Skin skin, Body body,
                                   AnimationClip walk, AnimationClip swing, Bounds home, float pitch)
        {
            var reach = Extent(skin, swing);

            // Symmetric about the walk's own centre, so the body's middle is the middle of the
            // square in both reels and the crop below has one point to mirror about.
            var arm = home.extents;
            arm = Vector3.Max(arm, home.center - reach.min);
            arm = Vector3.Max(arm, reach.max - home.center);

            var padded = new Bounds(home.center, arm * 2f);

            float grow = Mathf.Max(padded.size.x, padded.size.y, padded.size.z)
                       / Mathf.Max(1e-5f, Mathf.Max(home.size.x, home.size.y, home.size.z));

            int side = Mathf.Min(MostPixels, Mathf.RoundToInt(Tall * grow));

            var reels = new Texture2D[Hues.Length * 2][];
            for (int c = 0; c < Hues.Length; c++)
            {
                reels[c] = new Texture2D[Frames];
                reels[Hues.Length + c] = new Texture2D[SwingFrames];
            }

            for (int f = 0; f < Frames; f++)
            {
                skin.Pose(walk, walk.length * f / Frames);
                var raw = Render(cam, key, padded, pitch, side);
                for (int c = 0; c < Hues.Length; c++) reels[c][f] = Finish(raw, Hues[c], side, true);
                Object.DestroyImmediate(raw);
            }

            for (int f = 0; f < SwingFrames; f++)
            {
                skin.Pose(swing, swing.length * f / SwingFrames);
                var raw = Render(cam, key, padded, pitch, side);
                for (int c = 0; c < Hues.Length; c++)
                    reels[Hues.Length + c][f] = Finish(raw, Hues[c], side, true);
                Object.DestroyImmediate(raw);
            }

            var walks = new Texture2D[Hues.Length][];
            var swings = new Texture2D[Hues.Length][];
            for (int c = 0; c < Hues.Length; c++)
            {
                walks[c] = reels[c];
                swings[c] = reels[Hues.Length + c];
            }

            Pare(walks, swings);

            Moves(body, walks[0]);
            Moves(body, swings[0], body.Swing);

            return reels;
        }

        /// <summary>
        /// Cuts a walk tight and a swing to a canvas mirrored about the walk body's own middle.
        ///
        /// <b>Mirrored rather than merely containing, because the view centres both.</b>
        /// <c>SiegeView.Wear</c> swaps the sprite and resizes the widget about its own centre, so
        /// the body only stays put between the two reels if it sits at the same fraction of each
        /// frame. A box that merely contained the swing would put the body off-centre by however
        /// far the weapon reached on one side, and a raider would step sideways every time it hit
        /// something. See <see cref="Armed"/> for why they are cut out of one render at all.
        /// </summary>
        static void Pare(Texture2D[][] walks, Texture2D[][] swings)
        {
            var home = Box(walks);
            var whole = Box(walks, swings);
            if (home.width <= 0 || whole.width <= 0) return;

            float across = home.x + home.width * .5f, down = home.y + home.height * .5f;

            int reach = Mathf.CeilToInt(Mathf.Max(across - whole.x, whole.xMax - across));
            int fall = Mathf.CeilToInt(Mathf.Max(down - whole.y, whole.yMax - down));

            var wide = new RectInt(Mathf.FloorToInt(across) - reach, Mathf.FloorToInt(down) - fall,
                                   reach * 2, fall * 2);

            // `one_canvas`'s promise, asserted rather than trusted for its own reason: a clipped
            // weapon imports, addresses, audits and draws, and the only symptom is an axe losing
            // its head for two frames of a swing nobody is looking at closely.
            if (wide.x > whole.x || wide.y > whole.y
                || wide.xMax < whole.xMax || wide.yMax < whole.yMax)
            {
                Debug.LogError("SiegeCastBake: a swing canvas does not contain every frame of it.");
                return;
            }

            Cut(walks, home);
            Cut(swings, wide);
        }

        /// <summary>The alpha box over every frame of every reel handed in.</summary>
        static RectInt Box(params Texture2D[][][] sets)
        {
            int left = int.MaxValue, right = int.MinValue, low = int.MaxValue, high = int.MinValue;

            foreach (var set in sets)
                foreach (var reel in set)
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

            if (left > right || low > high) return new RectInt(0, 0, 0, 0);
            return new RectInt(left, low, right - left + 1, high - low + 1);
        }

        /// <summary>
        /// Crops every frame of every reel to one box.
        ///
        /// <b>A box that runs off the render is padded with nothing rather than clamped into
        /// range</b>, because the box's whole job is to be centred on the body: clamping it would
        /// silently move the body inside its own frame, which is the one thing
        /// <see cref="Pare"/> exists to prevent. Transparent margin costs a few rows and keeps the
        /// promise.
        /// </summary>
        static void Cut(Texture2D[][] reels, RectInt box)
        {
            int w = Mathf.Max(1, box.width), h = Mathf.Max(1, box.height);

            foreach (var reel in reels)
                for (int f = 0; f < reel.Length; f++)
                {
                    var src = reel[f];
                    var from = src.GetPixels32();
                    var to = new Color32[w * h];

                    for (int y = 0; y < h; y++)
                    {
                        int sy = box.y + y;
                        if (sy < 0 || sy >= src.height) continue;

                        for (int x = 0; x < w; x++)
                        {
                            int sx = box.x + x;
                            if (sx < 0 || sx >= src.width) continue;
                            to[y * w + x] = from[sy * src.width + sx];
                        }
                    }

                    var cut = new Texture2D(w, h, TextureFormat.RGBA32, false);
                    cut.SetPixels32(to);
                    cut.Apply();
                    Object.DestroyImmediate(src);
                    reel[f] = cut;
                }
        }

        /// <summary>
        /// A boss's three reels: the one it walks on in, the one it stands in, and the one it
        /// throws in.
        ///
        /// <para>
        /// <b>All measured and trimmed together, which is the whole reason this is not three calls
        /// to <see cref="Reels"/>.</b> Framing each animation to its own box draws the body at
        /// three different sizes, so on screen the boss shrinks by a quarter every time it casts
        /// and grows back — the fault <c>make_siege_art.one_canvas</c> exists to stop, met here
        /// from the 3D side. <see cref="Extent"/> is taken over the union of the clips and
        /// <see cref="Trim"/> is handed every reel at once, so the frame is shared by
        /// construction. <b>Sharing it is what makes the arrival invisible</b>: the body has to be
        /// the same size and standing in the same place in the frame on the last step of the walk
        /// and the first frame of the stand, or a boss that has reached its ground jumps.
        /// <c>SiegeView.Wear</c>'s <c>Grown</c> can rescue the <em>size</em> of a reel cut on its
        /// own canvas and cannot rescue where the body sits inside it, which is why a raider's
        /// swing is centred by hand (<c>make_siege_art.walk_and_swing</c>) and a boss simply
        /// shares.
        /// </para>
        /// <para>
        /// <b>No ward colour, because a boss has none.</b> A raider's colour is a rule — it decides
        /// which ward answers it (37f) — and a boss's is not, so it keeps what the pack painted.
        /// That is the same call every 2D boss in this mode gets (<c>make_siege_art.BOSS_SET</c>),
        /// and it is why <see cref="Finish"/> had to learn to skip the tint rather than be handed
        /// some hue that means "leave it alone", which no hue does.
        /// </para>
        /// <para>
        /// <b>And <see cref="Moves"/> is asked of the stand and of the walk, not of the throw.</b>
        /// An idle is the quietest clip in the library and is exactly the one a mis-bound rig would
        /// leave looking plausible — see that method for the session this cost. The walk is asked
        /// for a second reason on top of that one: it is the only clip here that comes out of a
        /// <em>different animation file</em> from the rest of its body's, which is precisely the
        /// binding this pack loses silently.
        /// </para>
        /// <para>
        /// <b>A boss with no <see cref="Body.Walk"/> still bakes, and gets two reels.</b> Nothing
        /// downstream is obliged to have one — <c>SiegeView</c> falls back to the stand for a boss
        /// whose walk reel is absent, exactly as it falls back to the walk for a cast that drew no
        /// swing — so a future boss taken from a pack with no walk cycle costs a null here and no
        /// branch anywhere else.
        /// </para>
        /// </summary>
        static Texture2D[][] BossReels(Transform stage, Camera cam, Light key, Body body)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Source + "/" + body.Model + ".fbx");
            var stand = ClipNamed(body.Rig, body.Clip);
            var cast = ClipNamed(body.Rig, body.Cast);
            if (model == null || stand == null || cast == null) return null;

            // Null when this body authored no walk; refused when it authored one this rig has
            // never heard of, which is a typo rather than a decision and must not silently ship
            // the boss that slid.
            AnimationClip walk = null;
            if (!string.IsNullOrEmpty(body.Walk))
            {
                walk = ClipNamed(body.WalkRig ?? Clips, body.Walk);
                if (walk == null)
                {
                    Debug.LogError(string.Format(
                        "SiegeCastBake: '{0}' asks for the walk clip '{1}', which is not in {2}.",
                        body.Key, body.Walk, body.WalkRig ?? Clips));
                    return null;
                }
            }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.transform.SetParent(stage, false);

            // **Before the skin, and that ordering is load-bearing** - see `Fit`.
            if (!Fit(inst, body)) { Object.DestroyImmediate(inst); return null; }

            var skin = new Skin(inst);

            try
            {
                var box = Extent(skin, stand);
                box.Encapsulate(Extent(skin, cast));
                if (walk != null) box.Encapsulate(Extent(skin, walk));

                float pitch = body.Lean > 0f ? body.Lean : Pitch;
                int tall = body.Height > 0 ? body.Height : Tall;

                var clips = walk == null
                          ? new[] { stand, cast }
                          : new[] { stand, cast, walk };

                var counts = walk == null
                           ? new[] { BossFrames, BossCastFrames }
                           : new[] { BossFrames, BossCastFrames, BossWalkFrames };

                var reels = new Texture2D[clips.Length][];

                for (int r = 0; r < clips.Length; r++)
                {
                    var clip = clips[r];
                    int frames = counts[r];

                    reels[r] = new Texture2D[frames];

                    for (int f = 0; f < frames; f++)
                    {
                        skin.Pose(clip, clip.length * f / frames);

                        var raw = Render(cam, key, box, pitch);
                        reels[r][f] = Finish(raw, default, tall, tint: false);
                        Object.DestroyImmediate(raw);
                    }
                }

                Trim(reels);
                Moves(body, reels[0]);

                if (walk != null)
                {
                    Moves(body, reels[2], body.Walk);
                    Strides(body, reels[2]);
                }

                return reels;
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
        static void Moves(Body body, Texture2D[] reel, string clip = null)
        {
            clip = clip ?? body.Clip;

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
                    + "animation file (see Body.Rig).", body.Key, clip, body.Model));
        }

        /// <summary>
        /// Refuses a walk reel whose body never rises and falls — a sway rather than a gait.
        ///
        /// <para>
        /// <b><see cref="Moves"/> asks whether a reel differs from a photograph; this asks whether
        /// it reads as walking, and the gap between those two questions shipped a sliding
        /// boss.</b> A clip that rocks a robed body from side to side lights up almost every pixel
        /// it owns — the reel that shipped changed 78% of its body across the cycle and cleared
        /// <see cref="Moves"/> in its lower half without trouble — while the feet stayed where
        /// they were and the hips never moved. What a player sees then is a picture being
        /// translated down a slope, which is the one thing the second reel exists to prevent.
        /// </para>
        /// <para>
        /// <b>Vertical, because that is what survives this camera.</b> A stride's horizontal
        /// travel is along the body's own axis and is therefore mostly occluded (see
        /// <see cref="Clips"/>), but a foot-locked cycle has to raise and drop the pelvis to take
        /// a step, and that rise projects at any pitch. Measured as the silhouette's centroid
        /// against the body's own drawn height, so it is a fact about the animation rather than
        /// about the canvas or the cut size.
        /// </para>
        /// <para>
        /// <b>The bar is set from the shipped cast rather than chosen.</b> Every running body on
        /// this hill reads 3.9-7.7%; the walk that was reported reads 0.80%; a stand reads 0.16%.
        /// Two per cent sits in the middle of that gap with roughly a factor of two either side,
        /// which is the most a measurement of six reels can honestly claim — this is the
        /// difference between a gait and a sway, not a judgement about how good a gait is. That
        /// judgement needs <c>render_siege.py --warlord walk</c> and somebody looking at it.
        /// </para>
        /// </summary>
        static void Strides(Body body, Texture2D[] reel)
        {
            if (reel == null || reel.Length < 2) return;

            int w = reel[0].width, h = reel[0].height;

            float low = float.MaxValue, high = float.MinValue;
            int top = int.MaxValue, foot = int.MinValue;

            for (int f = 0; f < reel.Length; f++)
            {
                var px = reel[f].GetPixels32();

                double sum = 0d;
                int seen = 0;

                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        if (px[y * w + x].a < 8) continue;

                        sum += y;
                        seen++;

                        if (y < top) top = y;
                        if (y > foot) foot = y;
                    }

                if (seen == 0) continue;

                float mid = (float)(sum / seen);
                if (mid < low) low = mid;
                if (mid > high) high = mid;
            }

            int tall = foot - top + 1;
            if (tall <= 1 || low > high) return;

            float bob = (high - low) / tall;
            if (bob >= WalkBob) return;

            Debug.LogError(string.Format(
                "SiegeCastBake: '{0}' walks on '{1}', whose body rises and falls {2:0.00}% of its "
                + "own height across the cycle — under the {3:0.0}% a gait reads at, so this is a "
                + "sway and what ships is a figure sliding down the hill. Every running body in "
                + "this cast reads 3.9-7.7%. See Clips: a humanoid's walk does not survive this "
                + "camera and a run is the most one gives you from it.",
                body.Key, body.Walk, bob * 100f, WalkBob * 100f));
        }

        /// <summary>How far a walking body must rise and fall to read as one. See <see cref="Strides"/>.</summary>
        const float WalkBob = .02f;

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

        /// <summary>
        /// One frame, rendered.
        ///
        /// <b><c>tall</c> is the square this is cut into, not the height of the body in it</b> -
        /// the body fills <see cref="Fill"/> of whatever square it is given. A reel that has to
        /// share a pixel scale with another passes its own (see <see cref="Armed"/>); everything
        /// else takes <see cref="Tall"/>.
        /// </summary>
        static Texture2D Render(Camera cam, Light key, Bounds box, float pitch, int tall = 0)
        {
            float reach = Mathf.Max(box.size.x, box.size.y, box.size.z);

            var rot = Quaternion.Euler(pitch, Yaw, 0f);
            cam.transform.position = box.center - (rot * Vector3.forward) * (reach * 4f + 10f);
            cam.transform.rotation = rot;
            cam.orthographicSize = reach * .5f / Fill;

            // The key rides with the camera, so a body is lit the same whatever pitch is chosen.
            key.transform.rotation = Quaternion.Euler(pitch - 16f, Yaw + 32f, 0f);

            int side = (tall > 0 ? tall : Tall) * Super;
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
        static Texture2D Finish(Texture2D raw, Color hue) => Finish(raw, hue, Tall, tint: true);

        /// <summary>
        /// The same, for a body that is cut at its own height and may keep its own colours.
        ///
        /// <b><c>tint: false</c> rather than a hue that means "leave it alone"</b>, because no hue
        /// does: <see cref="Tint"/> pushes saturation up from a floor whatever it is handed, so the
        /// nearest thing to an identity still repaints every grey pixel.
        /// </summary>
        static Texture2D Finish(Texture2D raw, Color hue, int tall, bool tint)
        {
            var painted = tint ? Tint(raw, hue) : Copy(raw);
            var lined = Outline(painted);
            Object.DestroyImmediate(painted);

            var small = Shrink(lined, tall);
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

            // **Before the skin, and that ordering is load-bearing** - see `Fit`.
            if (!Fit(inst, body)) { Object.DestroyImmediate(inst); return null; }

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
        /// <summary>
        /// The widest a pane grid may be before it stops being readable, in panes.
        ///
        /// <b>A wrap rather than a row, and the row was a limit nobody had met.</b> This sheet laid
        /// every pane out in a single line, which was fine while a bake made nineteen of them; the
        /// fourth chapter took it to fifty-seven — a fifth cast, and a swing reel for every body in
        /// two of them — and 57 panes at 300 is <b>17,100 pixels</b>, past the 16,384 a
        /// <c>Texture2D</c> may be. Unity refuses the allocation and the whole run throws at the
        /// very end, after every reel has been rendered: the most expensive possible moment to
        /// find out.
        ///
        /// <b>So the grid is derived rather than trusted</b>, and twelve is about readability
        /// rather than about the ceiling — a strip fifty-four panes wide would fit a texture and
        /// answer no question anybody has, which is what a contact sheet is for (32b).
        /// </summary>
        const int WidestSheet = 12;

        static void Sheet(List<Texture2D> shots, string file, int rows = 1)
        {
            shots = shots.Where(s => s != null).ToList();
            if (shots.Count == 0) return;

            const int Pane = 300;

            // `rows` is a hint - what `Angles` wants is its three pitches on three rows - and it
            // gives way to the wrap when there are more panes than a row may hold.
            int cols = Mathf.CeilToInt(shots.Count / (float)rows);
            if (cols > WidestSheet)
            {
                cols = WidestSheet;
                rows = Mathf.CeilToInt(shots.Count / (float)cols);
            }

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
