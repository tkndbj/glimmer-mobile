using System.Collections.Generic;
using System.IO;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Dev;
using GlimmerGrove.Modes;
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
    /// <c>SiegeTuning.FireEvery</c>, which is a bit over two bolts a second each and nine across a
    /// lit line — and was four times that when this was written; a board doing either cannot
    /// afford anything else.
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
    public static partial class SiegeShotBake
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

            /// <summary>
            /// A full project path, for an effect that is not in the projectiles pack.
            ///
            /// <b>A second field rather than a second root</b>, because the packs are not laid out
            /// alike: one files its prefabs under <c>Projectiles/</c> and the next one does not, so
            /// a shared root would only move the guesswork. When this is set it is used verbatim
            /// and <see cref="Prefab"/> is ignored.
            /// </summary>
            public string Path;

            /// <summary>
            /// A second effect layered into this one, or null.
            ///
            /// <para>
            /// <b>Because the pack holds twenty silhouettes and this roster wants nineteen good
            /// ones.</b> Four of the twenty are weak on their own - a plain pill, a puff of wind, a
            /// disc, a stave of music - and three of those were the only shapes left once the
            /// starter and the bosses had taken theirs. Layering is what a VFX artist does with a
            /// library like this: the base decides the silhouette and the second one gives it a
            /// head, a shell or a wake. It is composition rather than approximation, which is the
            /// difference invariant 32b keeps drawing.
            /// </para>
            /// <para>
            /// The two are flown as one object at one speed, so a trail smears the same way for
            /// both, and the muzzle and impact are still the base's unless overridden.
            /// </para>
            /// </summary>
            public string With;

            /// <summary>
            /// The flash and the impact, when the projectile's own pair are not the ones to use.
            ///
            /// <b>A projectile can be right while its companions are wrong, and that is a real
            /// case rather than a hypothetical.</b> The crescent blade came back from play as "a
            /// weird circular animation on initial fire" and "no impact effect" - its flight was
            /// exactly right, its muzzle was an expanding ring and its impact was a two-pixel
            /// vertical line the bake framed as a hairline. Giving up the flight to fix those would
            /// have been throwing away the part that worked.
            /// </summary>
            public string Muzzle, Hit;

            /// <summary>
            /// Whether this spell is thrown at <b>nothing</b>, so it has no flight to bake.
            ///
            /// <para>
            /// <b>Two of the eight bosses aim at the hill rather than at a ward</b> — a roar goes
            /// out over the whole line and a devour takes what is lying on the ground — so what
            /// the view draws is a ring opening and a flat wash over the floor, and nothing ever
            /// crosses the board (<c>SiegeView.Roar</c>). Their muzzle and impact are the whole
            /// drawing; the projectile in between is a reel nobody can ask for.
            /// </para>
            /// <para>
            /// <b>It is a field rather than an omission, because the bake had no way to say
            /// it.</b> `BakeSpell` writes a flight for every row, so the unused one shipped:
            /// addressed frame by frame, carrying no label (it is not in
            /// <c>AddressableAddresses.FrameFolders</c>, so nothing can load it as a reel), and
            /// built into a bundle for the life of the game. The audit had been reporting it as
            /// dead weight in as many words.
            /// </para>
            /// </summary>
            public bool Grounded;

            /// <summary>
            /// Whether this spell's source is a <b>comet</b> — a head with a tail behind it —
            /// rather than an orb, and therefore framed round the head instead of square.
            ///
            /// <para>
            /// <b>The fifth telling of invariant 37k's sliver, and the first one that is not a
            /// hand-fix.</b> <see cref="BakeSpell"/> framed every spell square because the thing
            /// it was written for is a sun, and the note on it says so in as many words. Two rows
            /// added later are not suns: a shackler looses an <em>arrow</em> and an ironclad
            /// brings a <em>blade</em> down, and both came out of a 384-square frame as a thread
            /// down the middle of nothing —
            /// <c>snare</c> at <b>2.1 %</b> of its own frame across and <c>quake</c> at
            /// <b>5.2 %</b>, against a median of 17 % for the 266 reels on disk. On the board
            /// that is an arrow two hundredths of a cell wide. Both shipped.
            /// </para>
            /// <para>
            /// <b>So it is a field, for the reason <see cref="Toward"/> is one</b>: how a source
            /// has to be framed is a fact about <em>that source</em>, not about which table it
            /// sits in. The same square framing that is right for <c>Sun01</c> is wrong for
            /// <c>MagicArrow02</c>, and there is nothing a bake can read off a prefab that
            /// answers it — the survey sheet renders both perfectly well as comets, which is
            /// exactly why looking at the pack never caught this.
            /// </para>
            /// <para>
            /// <b>The view needs no matching flag, and deliberately.</b> <c>SiegeView.Hurl</c>
            /// anchors a square reel at its middle and a comet at <c>HeadAt</c>, and it decides
            /// which by reading the <em>aspect off the sprite</em> — so this table stays the one
            /// place the decision is made and a re-bake cannot leave the drawing holding the old
            /// answer. <c>Tools/verify/fxreels.py</c> is what proves the result is a picture.
            /// </para>
            /// </summary>
            public bool Comet;

            /// <summary>
            /// Whether this reel is baked <b>white in all four ward colours</b> rather than graded
            /// onto each one.
            ///
            /// <para>
            /// <b>The one exception to invariant 37f, and it is the owner's decision about what
            /// frost looks like.</b> Every other bolt wears the colour of the ward that threw it,
            /// which is what keeps a turret, its bullets and the gems that feed it from ever
            /// disagreeing about what red is. Frost is white — a snowball thrown by a red turret
            /// is still a snowball — so both rungs of that ability are baked once and worn by all
            /// four.
            /// </para>
            /// <para>
            /// <b>It costs the mode nothing, because the double is not said in the bolt.</b> What
            /// tells a player a hit was worth double is the gold figure, the ring and the sparks
            /// at the impact (<c>SiegeView.Land</c>), never the colour of the thing in the air —
            /// so a colourless bolt says "this is the frost turret" and takes nothing away.
            /// </para>
            /// <para>
            /// The four reels are still written, because <c>WardModel.ShotFor</c> builds a name
            /// per colour; they simply hold the same picture.
            /// </para>
            /// </summary>
            public bool White;

            /// <summary>
            /// How far this reel is pulled onto its <see cref="Hue"/>, how much of a near-white
            /// pixel it keeps, and how saturated its palest pixel comes out.
            ///
            /// <para>
            /// <b>On the row rather than on the bake, because how far a source has to be carried
            /// is a fact about <em>that</em> source.</b> <see cref="Toward"/> is a third of the
            /// way and <see cref="RosterToward"/> nearly all of it, and the difference has never
            /// been about which table an effect sits in: it is that the elemental four were
            /// <em>chosen</em> for already wearing roughly the hue they are graded to, where a
            /// roster effect arrives teal or magenta and has to be carried the whole way. The
            /// moment those four stopped being four differently-coloured sources they stopped
            /// qualifying for the lean — see <see cref="Shots"/> — so the pair moved to where the
            /// source is named.
            /// </para>
            /// <para>
            /// Only the elemental rows set these; a roster row is graded with the roster's own
            /// constants, which is <see cref="BakeTurret"/>'s business and not this table's.
            /// </para>
            /// </summary>
            public float Toward, Keep, Floor;

            /// <summary>
            /// How wide this projectile is drawn against how wide the pack draws it, or nought for
            /// the pack's own.
            ///
            /// <para>
            /// <b>The bolt only, and about its own flight axis.</b> A muzzle flash and an impact
            /// are radial and drawn where something happened, so squeezing either would make an
            /// ellipse out of a burst; a comet is symmetric about the line it travels, so the same
            /// operation makes it leaner and nothing else.
            /// </para>
            /// <para>
            /// <b>It exists because the frame is as wide as the head, and one head is much fatter
            /// than the rest.</b> <see cref="LeanestShot"/> and its neighbours frame a comet round
            /// its own proportions, which is right and is why the bake was stopped from forcing a
            /// shape (see that entry) - so a prefab whose head is a broad capsule comes out a
            /// broad capsule. Measured on the shelf, the leech's bolt covered 0.42 of a cell on
            /// average against a fireball's 0.38 and a lance's 0.21: the fattest thing anybody can
            /// buy, and reported as exactly that.
            /// </para>
            /// <para>
            /// <b>Applied to the pixels rather than to the prefab or the camera</b>, after the
            /// framing has run. Scaling the object or the frustum feeds a narrower measurement
            /// back into <see cref="Shape"/>, which asks for a narrower frame - and since the view
            /// sizes a bolt by its frame's <em>width</em>, that draws the same bolt at the same
            /// width and simply makes it longer. Squeezing the finished frames keeps the frame,
            /// so what changes is the one thing that was asked to change.
            /// </para>
            /// </summary>
            public float Slim;
        }

        /// <summary>
        /// <b>One silhouette in four colours, and it was four silhouettes until the owner played
        /// it.</b> The set began as fire, venom, ice and lightning - one element per ward colour -
        /// on the argument above, and that argument was written while the <em>starter</em> threw
        /// them: a line of four starters put four different shapes in the air at once, which is
        /// how a player who cannot separate red from green separates one bolt from another.
        ///
        /// <para>
        /// <b>That stopped being true when the set moved onto a bought turret.</b> Only
        /// <c>WardModel.Elemental</c> draws these now, and a line stands one turret per seat - so
        /// the four elements never appear together on a board unless somebody has bought the
        /// <em>same</em> turret for all four colours, at which point they read as four unrelated
        /// weapons wearing one name. Reported from the preview panel in one sentence: the red
        /// Breaker does a different animation from the orange one. Every other turret in the
        /// roster is one silhouette worn in four colours, and this is now the same.
        /// </para>
        /// <para>
        /// <b>The fireball, because that is the one the owner picked.</b> Red is untouched - the
        /// same prefab, graded the same way, so the picture that was liked is the picture that
        /// ships.
        /// </para>
        /// <para>
        /// <b>And the other three had to change how they are graded, which is the half that is
        /// not obvious.</b> <see cref="Toward"/>'s 38% lean is only enough because a source
        /// already wears roughly its target hue; a warm fireball leaned a third of the way toward
        /// <c>Pal.Mint</c> comes out orange, which is invariant 37f's whole subject. So the three
        /// that are now carrying a warm source onto a hue it does not have are graded with the
        /// roster's constants - the same ones nineteen turrets already use for exactly this - and
        /// red keeps the elemental pair because red is the case those were tuned for. Invariant
        /// 37ae, said the other way round: before reusing a grading constant on art chosen a
        /// different way, ask what the old art was chosen for.
        /// </para>
        /// </summary>
        static readonly Shot[] Shots =
        {
            new Shot { Key = "r", Prefab = "vfx_Projectile_Fireball01", Hue = Pal.Poppy,
                       Toward = Toward,      Keep = MostWhite,   Floor = Muted      },
            new Shot { Key = "g", Prefab = "vfx_Projectile_Fireball01", Hue = Pal.Mint,
                       Toward = RosterToward, Keep = RosterWhite, Floor = RosterMuted },
            new Shot { Key = "b", Prefab = "vfx_Projectile_Fireball01", Hue = Pal.Azure,
                       Toward = RosterToward, Keep = RosterWhite, Floor = RosterMuted },
            new Shot { Key = "y", Prefab = "vfx_Projectile_Fireball01", Hue = Pal.Amber,
                       Toward = RosterToward, Keep = RosterWhite, Floor = RosterMuted },
        };

        /// <summary>
        /// What each turret in the roster throws — one effect per model, chosen to say what that
        /// model's <em>ability</em> does.
        ///
        /// <para>
        /// <b>Nineteen distinct silhouettes, because a turret somebody paid credits or gems for
        /// has to be visibly the thing they bought.</b> The shared elemental reels are
        /// <see cref="Shots"/>' business and are untouched here; every model with an ability throws something of its own,
        /// worn in whichever colour its ward burns. The pack holds exactly twenty families and the
        /// three files in each are the same shape in three colours — so the shapes are the scarce
        /// thing, and every one of these is a different one.
        /// </para>
        /// <para>
        /// <b>Chosen by looking, which is the only way these can be chosen.</b> Their names say a
        /// family and their thumbnails are grey cubes; <c>Survey Projectile Pack</c> rasterises all
        /// sixty onto one sheet at the size a phone draws them, and that sheet is what picked
        /// these. Invariant 37k records the same method choosing one boss spell out of six
        /// candidates, and every time this project has skipped it, it has shipped a picture
        /// somebody had to do again.
        /// </para>
        /// <para>
        /// <b>The pairing rule is that the silhouette says the ability, not the tier.</b> Two
        /// models sharing an ability are the same answer at two strengths, so making them two
        /// tints of one shape would be invariant 37z's fault exactly — two things told apart by a
        /// hue are not told apart. They are instead two different readings of the same idea: a
        /// crescent and a shockwave both cut through armour, an arrow and a lance both run a lane.
        /// </para>
        /// </summary>
        static readonly Shot[] Roster =
        {
            // ---- the three that arc on to the raiders behind the one they hit ----------------
            // **Grouped by what each throws rather than by what each does**, because after three
            // rounds of the owner moving effects and abilities between cards these two no longer
            // line up - an effect is chosen by looking at it beside the turret that wears it, and
            // an ability is chosen by where the rung sits. See `WardCatalog.Default` for which is
            // which today.
            //
            // A crackle, a forked bolt and an orb. `spark` keeps its fireworks and now banks fuel
            // rather than arcing (invariant 37ay), which is why it is still listed here: the reel
            // belongs to the id, not to the ability.
            new Shot { Key = "spark",      Prefab = "vfx_Projectile_Fireworks02" },
            // **The lamp and the forked bolt changed places on the owner's call.** A chain reads
            // as lightning and `lighthouse` is the chain turret now, so it takes the fork and
            // `arcstorm` takes the lamp.
            new Shot { Key = "lighthouse", Prefab = "vfx_Projectile_Lightning02" },
            new Shot { Key = "arcstorm",   Prefab = "vfx_Projectile_Orb18_red" },
            // **A sun, and the biggest thing on the board.** It stood on `prism` and was moved to
            // the top of the shelf on the owner's call: the dearest turret in the game is the one
            // that should be throwing it. It is drawn half again the size of every other bolt
            // (`SiegeView.BoltScale`), which is the only place a turret's projectile is allowed to
            // differ in size, and that scale travelled with the picture rather than staying on the
            // rung - because what wants the room is the sun.
            new Shot
            {
                Key = "apex", Prefab = "vfx_Projectile_Sun02",
                Hit = "vfx_Hit_Orb18_purple",
            },

            // ---- Splash: it strikes everything within a band of what it hit -------------------
            // Ordnance. A finned shell that is unmistakably fired, and — since the owner exchanged
            // it with the beacon's — a heavy orb of light. Both are read as "this one goes off"
            // before they land, which is what a splash owes the player.
            new Shot { Key = "mortar",     Prefab = "vfx_Projectile_Rocket01" },
            new Shot { Key = "howitzer",   Prefab = "vfx_Projectile_Orb17_blue" },

            // ---- Frost: what it hits walks slower --------------------------------------------
            // **A snowball, white, and the same one on both rungs** - the owner's call, and the
            // only place in this mode a bolt does not wear the colour of the ward that threw it
            // (see `Shot.White`). What it replaced was a block of ice on the bought rung and the
            // four elemental bolts on the earned one, which meant the ability that reads as cold
            // was drawn as a cube on one turret and as a fireball on the other.
            //
            // **A ball wrapped in a gust, because the pack holds no snowball.** Its twenty
            // families are all spoken for, so this is `Orb17` - the only round thing in it - with
            // `Wind01` layered in for spindrift. Both are shapes that were weak on their own:
            // invariant 37ag records the same repair turning a plain pill into a lance and a grey
            // puff into a wrecking slug, and it is composition rather than approximation (32b).
            // The orb is `howitzer`'s family, which is the one sharing left on the shelf and is
            // survivable for exactly one reason: that one wears its ward's colour and this is
            // white, which on a board where every other bolt is coloured is the loudest
            // difference available.
            // **Its own muzzle and impact, because the orb's are wrong here twice over.** The
            // orb lands as a plain disc, which says nothing about ice - and that disc is built on
            // a flat shockwave *card*, which this rig looks straight at and so bakes as a
            // translucent square the size of the frame (invariant 37aj, where the same quads
            // collapsed to hairlines from the other direction). Coloured it passes; white it is a
            // pane of glass over a quarter of the hill. `Icicle03`'s pair are a frosty burst at
            // the barrel and a shatter where it lands, and they are the one set in the pack that
            // nothing else uses - `harpoon` borrows that projectile but overrides both.
            new Shot
            {
                Key = "rime", Prefab = "vfx_Projectile_Orb17_blue",
                With = "vfx_Projectile_Wind01", White = true,
                Muzzle = "vfx_Muzzle_Icicle03", Hit = "vfx_Hit_Icicle03",
            },
            new Shot
            {
                Key = "glacier", Prefab = "vfx_Projectile_Orb17_blue",
                With = "vfx_Projectile_Wind01", White = true,
                Muzzle = "vfx_Muzzle_Icicle03", Hit = "vfx_Hit_Icicle03",
            },

            // ---- the starter, which throws one effect rather than four ------------------------
            // **The free turret used to fire the elemental set and the frost turret an ice shard;
            // the owner swapped them, and the elemental set has since moved on again to
            // `breaker`** (`WardModel.Elemental`). So `bolt` is an ordinary roster entry with a
            // shard of its own. It is graded here rather than on the elemental path because that
            // path leans a reel only a third of the way onto its colour - which is right for four
            // effects chosen for already wearing roughly the right hue, and leaves a pale shard
            // whitish in four colours.
            new Shot { Key = "bolt",       Prefab = "vfx_Projectile_Icicle02" },

            // ---- Pierce: every so often it runs the whole lane --------------------------------
            // Both are *linear* — an arrow with a shaft behind it and a straight lance of light —
            // because what this ability does is a line, and a round thing cannot say that.
            new Shot { Key = "lance",      Prefab = "vfx_Projectile_MagicArrow01" },
            // A lance of light with a crystal head on it. `Bullet` alone came back from play as
            // "so primitive, I do not even understand it" - a plain pill on a soft trail - and what
            // it was missing is a *point*.
            new Shot
            {
                Key = "harpoon", Prefab = "vfx_Projectile_Bullet02",
                With = "vfx_Projectile_Icicle03",
                Muzzle = "vfx_Muzzle_Fireworks03", Hit = "vfx_Hit_Rocket03",
            },

            // ---- Rend: it is not blunted by a shield ------------------------------------------
            // A crescent that cuts and a wave that shatters. Two ways of getting through armour
            // rather than one drawn twice, which is what keeps a five-thousand-credit turret and a
            // thousand-gem one from reading as the same purchase.
            // The crescent is right and both its companions were wrong: its own muzzle opens an
            // expanding ring (the "weird circular animation on initial fire") and its own impact is
            // a two-pixel vertical line. A sharp burst leaving the barrel and a bright sparking
            // ring where it lands say what a cut does.
            new Shot
            {
                Key = "cleaver", Prefab = "vfx_Projectile_Slash01",
                Muzzle = "vfx_Muzzle_Icicle03", Hit = "vfx_Hit_Fireworks03",
            },
            // **`breaker` is not here: it draws the shared elemental reels** - the fireball, in
            // whichever colour its ward burns (see `Shots`)
            // (`WardModel.Elemental`, and `Shots` above). The owner moved that set onto it from
            // `rime`, which took the snowball; what it gave up was a wrecking slug wrapped in
            // shattering crystal, and with the glacier's block of ice gone with it there is no
            // cube left anywhere in this mode.

            // ---- Siphon: a kill hands some of its fuel back -----------------------------------
            // The two things in the pack that read as *taking* something: a spectral wisp trailing
            // smoke, and a dart that leaves venom behind it.
            new Shot { Key = "siphon",     Prefab = "vfx_Projectile_Ghostly01" },
            // **Slimmed, and the number was chosen by measuring rather than by taste.** At the
            // pack's own width this bolt is the fattest on the shelf; at .80 its widest point is
            // 0.56 of a cell against the fireball's 0.57 and its own earned rung's 0.62, so it
            // lands in the middle of the shelf instead of at the end of it. See `Shot.Slim`.
            new Shot { Key = "leech",      Prefab = "vfx_Projectile_PoisonDart02", Slim = .80f },

            // ---- Ember: what it hits goes on burning ------------------------------------------
            // Fire, twice, and the second one is a firestorm rather than a bigger flame: twin
            // spirals that keep turning after the head has gone, which is the ability drawn.
            new Shot { Key = "ember",      Prefab = "vfx_Projectile_Fireball02" },
            new Shot { Key = "pyre",       Prefab = "vfx_Projectile_Spiral01" },

            // ---- the slug --------------------------------------------------------------------
            // **The orb that stood here went to `howitzer` on the owner's call and this took its
            // slug**, so the one ability that does nothing to a raider now throws the heaviest
            // looking thing on the shelf. That is a decision about what a turret looks like rather
            // than about what it does, which is the whole reason these are chosen by eye (37ay).
            // Its partner lamp is on `arcstorm` and the turret that banks alongside it is `spark`,
            // which kept its crackle - see the notes there.
            new Shot { Key = "beacon",     Prefab = "vfx_Projectile_Capsule02" },

            // ---- Prism: its bolts are worth double against two colours -------------------------
            // The one ability that widens the mode's central rule, so both of these are about
            // *two-ness*: a star that splits as it flies, and a sun thrown as a spray.
            // **The sun that used to be here went to `apex` on the owner's call** and this took
            // the star it was throwing. What stayed is the pairing rule: these two are told apart
            // by shape and by size, never by a hue (invariant 37z).
            new Shot { Key = "prism",      Prefab = "vfx_Projectile_ShootingStar01" },

            // The same light thrown as a spray of stars, which is what splitting it looks like.
            // Its pair share a family because the pack holds one sun; what tells them apart is the
            // spray, the sparkling impact and half a cell of size. It carried a crystal shell for
            // one bake and that was wrong in the way this file keeps recording - a shatter beside
            // a shatter is two turrets told apart by nothing (invariant 37z), because the wrecking
            // slug two rows up already lands as one.
            new Shot
            {
                Key = "spectrum", Prefab = "vfx_Projectile_Sun02",
                With = "vfx_Projectile_Fireworks03",
                Muzzle = "vfx_Muzzle_ShootingStar02", Hit = "vfx_Hit_ShootingStar02",
            },
        };

        /// <summary>
        /// What the warlord throws.
        ///
        /// <para>
        /// <b>One set rather than four, and graded to a colour no ward and no gem wears.</b> The
        /// elemental double is a rule about bolts landing <em>on</em> a raider; a spell coming out
        /// of one that wore one of the board's four colours would be saying something the rules do
        /// not mean, and a player would reasonably read it as "this is the blue one, so it hurts
        /// the blue ward". Violet is the one entry in <c>Pal</c>'s board set that is none of the
        /// four, so it cannot be read as a colour rule at all.
        /// </para>
        /// <para>
        /// <b>And it is a different <em>kind</em> of thing rather than a bigger bolt</b>, which is
        /// invariant 33e's test asked of the boss: a sun rather than a comet, a dart, a shard or a
        /// bolt of lightning. At the size it is drawn — half again a ward's bolt — silhouette is
        /// what tells a player this one is not a turret shooting.
        /// </para>
        /// </summary>
        static readonly Shot Spell =
            new Shot { Key = "spell", Prefab = "vfx_Projectile_Sun01", Hue = Pal.Foxglove };

        /// <summary>
        /// What a <b>stormcall</b> drops on the hill.
        ///
        /// <para>
        /// <b>The pack's third lightning rather than the one the yellow ward fires</b>, and that
        /// is the whole reason it is its own entry. A ward's bolt goes off four or five times a
        /// second, so it is cut small and short; a storm happens once a run and costs forty gems,
        /// so it is baked on the <em>spell</em> path — bigger, longer, framed square with its own
        /// tail. Sharing <c>shot_y</c> would have meant tuning the biggest moment in the mode by
        /// the smallest, which is the mistake the firepot's sound already made once.
        /// </para>
        /// <para>
        /// <b>Graded to <c>Pal.Sun</c>, which is the one effect here that is none of the four ward
        /// colours - and that is the point rather than an oversight.</b> It read as the fourth
        /// ward's colour while that ward was yellow; the ward is <c>Pal.Amber</c> now, and this
        /// stayed where it was because a storm is <em>meant</em> to read as lightning rather than
        /// as a fifth element, and the storm is not a colour rule - it hits every raider whatever
        /// it wears. A bolt the colour of a ward would say the opposite.
        /// </para>
        /// </summary>
        static readonly Shot Storm = new Shot
        {
            Key = "storm",
            Path = "Assets/Mirza Beig/Lightning VFX/Prefabs/Lightning.prefab",
            Hue = Pal.Sun,
        };

        /// <summary>
        /// What the <em>overlord</em> throws: the same class of magic, hotter and in a colour
        /// nothing else on the board wears.
        ///
        /// <para>
        /// <b>Magenta rather than violet</b>, which is the other <c>Pal</c> entry that is none of
        /// the board's four colours — so it can no more be read as a colour rule than the
        /// warlord's violet can, and the two cannot be confused with each other either. The
        /// overlord is the last thing in the chapter and it stands on the same hill a warlord did
        /// five levels earlier, so its spell has to be told apart at a glance.
        /// </para>
        /// <para>
        /// <b>And it is a sun rather than the two things tried first, which is a framing fact
        /// rather than a taste.</b> A spiral and a spinning disc were both baked and both looked
        /// at: the spiral came out as invariant 37k's sliver again — a thin ribbon framed square,
        /// eighty per cent empty, drawn on the board as a magenta thread — and the disc is drawn
        /// nearly black by the pack, so a hue rotation toward magenta had nothing to work on and it
        /// arrived as a dot. What frames well here is what already framed well for the warlord: a
        /// round, bright, self-lit thing. The difference between the two is carried by the colour,
        /// by the size the view draws it at, and by the impact — which is where a player is looking
        /// when it lands.
        /// </para>
        /// </summary>
        static readonly Shot Omen =
            new Shot { Key = "omen", Prefab = "vfx_Projectile_Sun03", Hue = Pal.Bloom };

        /// <summary>
        /// What the <em>blightcaller</em> throws: a spectral wisp that puts a ward out.
        ///
        /// <para>
        /// <b>Teal, which is the third <c>Pal</c> entry that is none of the board's four</b> — and
        /// it is the closest of the four spell colours to a gem (the blue one), which is exactly
        /// why this is the one drawn as something <em>trailing</em> rather than as anything round.
        /// A player never has to tell a teal orb from a blue one, because there is no teal orb:
        /// there is a wisp that drifts across the hill and a bolt that goes straight up it.
        /// </para>
        /// <para>
        /// <b>An electric orb rather than the wisp it was first baked as</b>, and that is invariant
        /// 37k's sliver for the third time in this file. A hex <em>ought</em> to trail — it
        /// smothers rather than detonating, and a drifting shape says that — so the first cut was
        /// a ghostly comet, and it came out of the bake as a hairline: eight pixels of thread in a
        /// 384-pixel square, drawn on the hill as a scratch. What frames well here is what framed
        /// well for the warlord and the overlord, and what carries the "not an impact" reading
        /// instead is the <em>path</em> — <c>SiegeView.Hurl</c> wafts a douse across the hill where
        /// a smite and an omen go straight. The drawing says it, and the framing does not have to.
        /// </para>
        /// <para>
        /// Six candidates were baked and looked at side by side, which is the only way to choose
        /// one of these (<c>Siege Projectile Contact Sheet</c>): two ghostly wisps and a firework
        /// framed as slivers, one orb was a dot, and this one is a compact cyan sphere with a white
        /// crackling core that holds its size for every frame of its flight.
        /// </para>
        /// </summary>
        static readonly Shot Hex =
            new Shot { Key = "hex", Prefab = "vfx_Projectile_Orb18_blue", Hue = Pal.Aqua };

        /// <summary>
        /// What the <em>warbringer</em> roars with — and it is the one of the four that is not
        /// thrown at anything.
        ///
        /// <para>
        /// <b>Near-white, which is the fourth and last <c>Pal</c> entry that is none of the
        /// board's four.</b> A roar is pressure rather than magic, so the one colour that says "no
        /// colour" is the right one for it: nothing about a warbringer is a rule about which ward
        /// is which, and a coloured shout would imply one.
        /// </para>
        /// <para>
        /// <b>Wind rather than a projectile, and only two of its three reels are ever drawn.</b>
        /// Its <em>impact</em> is a white ring that opens outward with shards in it, which is
        /// exactly what a roar looks like, and its <em>muzzle</em> is a flat horizontal ellipse
        /// spreading — which over a hill drawn in perspective reads as the same pressure crossing
        /// the ground. <c>SiegeView.Roar</c> draws both, one upright and one flat, and the flight
        /// reel is baked because <see cref="BakeSpell"/> bakes a set and is never asked for
        /// (<c>SiegeMode.Bosses</c> does not load it). A roar is thrown at nothing, so it has no
        /// flight.
        /// </para>
        /// </summary>
        static readonly Shot Roar =
            new Shot { Key = "roar", Prefab = "vfx_Projectile_Wind01", Hue = Pal.Radiance,
                       Grounded = true };

        /// <summary>
        /// What a <b>shackler</b> looses, and it is the first thing a boss in this mode throws
        /// that is not magic.
        ///
        /// <para>
        /// <b>Iron rather than a hue</b>, and that is a decision the four before it could not
        /// make. Every boss colour here has to be one no ward and no gem wears, or the drawing
        /// says a colour rule the game does not have — and between them the first four take every
        /// entry in <c>Pal</c>'s board set that qualifies. <see cref="Pal.Dormant"/> is the
        /// unpowered slate, which is the one thing in this palette that reads as <em>metal</em>:
        /// it is the colour of a chain, it cannot be mistaken for any of the four, and it is the
        /// right answer rather than the last one left.
        /// </para>
        /// <para>
        /// <b>A bolt rather than a sun or an orb, because what it throws is a chain</b> — a
        /// shackler takes no health at all and simply stops a ward firing, so the one thing the
        /// flight must not read as is magic landing on a turret (invariant 33e asked of the boss
        /// and its spell together rather than of the effect alone).
        /// </para>
        /// <para>
        /// <b>It was picked when the body loosing it was a drawn bow, and that body is gone.</b>
        /// A shackler was rendered out of rigged 3D as an archer; the bake was withdrawn and it
        /// is a flat cut now (<c>make_siege_art.BOSS_SET</c>) whose attack lashes rather than
        /// looses. Iron still reads — a bolt on a chain is what the mechanic is — but <b>this is
        /// the one prefab in this file whose body changed underneath it</b>, so it is owed a look
        /// on <c>Siege Projectile Contact Sheet</c> beside the new stand. No gate here opens a
        /// PNG (32b).
        /// </para>
        /// </summary>
        /// <para>
        /// <b>A comet, and it took a gate to notice.</b> An arrow is a head with a shaft behind
        /// it, and framed square by <see cref="BakeSpell"/> it baked at <b>2.1 %</b> of its own
        /// frame across — the thinnest reel in the game by a factor of fifteen, drawn on the hill
        /// as a thread two hundredths of a cell wide, shipped, and green on every check this
        /// project had. See <see cref="Shot.Comet"/> and <c>Tools/verify/fxreels.py</c>.
        /// </para>
        static readonly Shot Snare =
            new Shot { Key = "snare", Prefab = "vfx_Projectile_MagicArrow02", Hue = Pal.Dormant,
                       Comet = true };

        /// <summary>
        /// What an <b>ironclad</b> lands when it brings its axe down.
        ///
        /// <para>
        /// <b>Near-white, sharing the roar's hue, and that is allowed where sharing a
        /// <em>shape</em> would not be.</b> The constraint on a boss colour is that it must not
        /// read as one of the board's four (see <see cref="Spell"/>); pressure and dust are both
        /// colourless, and the two bosses are four rungs and two chapters apart. What must differ
        /// is the thing invariant 37z is actually about — a roar opens a ring over the whole hill
        /// and a slam is one heavy arc coming down on one ward — and that is the prefab, not the
        /// grade.
        /// </para>
        /// <para>
        /// <b>Also a candidate until it has been looked at</b>, for <see cref="Snare"/>'s reason.
        /// </para>
        /// </summary>
        /// <para>
        /// <b>A comet for the snare's reason, and the one row here that also had to replace its
        /// impact.</b> <c>Slash03</c>'s flight is a crescent blade with the streaks of the swing
        /// behind it — right, and 8 : 1, so framed square it baked at <b>5.2 %</b> across. Its
        /// impact is worse and cannot be framed out of it: all three of the pack's slash impacts
        /// are a <em>vertical line</em>, which baked to <b>3.1 %</b> across and drew on the board
        /// as a hairline down the turret. <b>That is the cleaver's fault a second time</b>, in the
        /// same file, under the same comment that records fixing it — and the cleaver's fix was
        /// exactly this: keep the flight, name a different impact.
        /// </para>
        /// <para>
        /// <b>Dust and shards, because that is what mass landing on something throws.</b>
        /// <c>Hit_Capsule01</c> is a smoke ring with embers in it and hard chips flung outward;
        /// graded to <see cref="Pal.Radiance"/> it is stone and dust, and it is the only impact in
        /// this pack that has <em>weight</em> rather than light. Nothing else in the mode uses it,
        /// so an ironclad cannot be confused with anything — which is invariant 37z's requirement
        /// that the difference be a shape and not a tint, and the reason it can go on sharing a
        /// hue with the roar two chapters away.
        /// </para>
        /// <para>
        /// <b><c>Hit_Cube02</c> was baked first and looked at, which is the only reason it is not
        /// in this game.</b> Its name says rubble and its picture is a stack of white boxes — the
        /// pack's low-poly debris mesh, drawn large, reading as geometry rather than as stone.
        /// Every number was healthy: 26 % ink, two thirds of its frame, straight through the gate
        /// that had just caught the hairline it replaced. <b>A gate proves a reel is a picture and
        /// can never prove it is the right one</b> (invariant 32b), and the contact sheet took one
        /// look.
        /// </para>
        static readonly Shot Quake =
            new Shot { Key = "quake", Prefab = "vfx_Projectile_Slash03", Hue = Pal.Radiance,
                       Comet = true, Hit = "vfx_Hit_Capsule01" };

        /// <summary>
        /// What a <b>gravemaw</b> opens when it eats the hill, and it is the first of two rows
        /// that exist to end three bosses sharing one drawing.
        ///
        /// <para>
        /// <b>Three of the eight wore the warbringer's reels, separated by a run-time hue.</b>
        /// That is invariant 37z's fault in its purest form — the rule is written about bosses
        /// and this is the drawing half of it — and the code that did it said so out loud: the
        /// gravemaw and the bonecaller were scoped to <c>roar_muzzle</c> and <c>roar_hit</c>
        /// because both are <see cref="Shot.Grounded"/> and the roar was the grounded row that
        /// already existed. Grounded is what they have in common with a roar. It is not what they
        /// <em>are</em>.
        /// </para>
        /// <para>
        /// <b>A ring that closes, because a devour is a pull.</b> The whole difference between
        /// this and a roar is direction — a roar pushes outward off the thing casting it and this
        /// draws inward onto it (<c>SiegeView.Feed</c>) — so what it wants is a ring with a
        /// current in it rather than a shockwave with shards. <c>Spiral02</c>'s impact is that,
        /// and <see cref="Pal.Verdant"/> is the sicklier green on the wheel, which is the colour
        /// a mouth should be.
        /// </para>
        /// <para>
        /// <b>Grounded, so only two of its three reels are baked</b> — nothing crosses the hill,
        /// and a flight reel nothing can ask for is a bundle entry for the life of the game.
        /// </para>
        /// </summary>
        static readonly Shot Maw =
            new Shot { Key = "maw", Prefab = "vfx_Projectile_Spiral02", Hue = Pal.Verdant,
                       Grounded = true };

        /// <summary>
        /// What a <b>bonecaller</b> opens when it raises a group, and the second row ending the
        /// shared drawing <see cref="Maw"/> describes.
        ///
        /// <para>
        /// <b>Spectral rather than pressure, which is the whole point of not being a roar.</b>
        /// <c>Ghostly02</c>'s impact is a soft burst that blooms and hangs rather than snapping
        /// outward, and it is the only thing in this pack that looks like something arriving from
        /// somewhere else. Graded to <see cref="Pal.Glass"/> — the field's own ice white against
        /// the roar's warm <see cref="Pal.Radiance"/> — it is the light the dead come up in.
        /// </para>
        /// <para>
        /// <b>`crypt` rather than `bone`, because the cast is already called that.</b> The
        /// bonecaller's body reels are <c>caller</c> and its raised bodies are the chapter's
        /// <c>bone*</c> roster; a third thing spelt <c>bone</c> would collide with a name in the
        /// same folder. An art address is permanent (invariant 1's reach), so it is worth one
        /// minute of thought.
        /// </para>
        /// </summary>
        static readonly Shot Crypt =
            new Shot { Key = "crypt", Prefab = "vfx_Projectile_Ghostly02", Hue = Pal.Glass,
                       Grounded = true };

        /// <summary>
        /// What a <b>charm</b> goes off in: one big radial detonation, in each of the four gem
        /// colours.
        ///
        /// <para>
        /// <b>Its own reel because the one it was borrowing is cut for something a hundred times
        /// more frequent, and that is the whole fault.</b> A charm used to detonate in
        /// <c>hit_{c}</c> — a ward's *impact*, framed at <see cref="BurstSide"/> because a lit line
        /// lands about eighteen of them a second — drawn by the view at four and a half cells. So
        /// the biggest moment on the field was a small reel blown up two and a half times, which is
        /// exactly what "the animations are horrendous" is when it is measured rather than argued
        /// about. It is the <see cref="Storm"/> argument arriving a second time: <em>sharing a reel
        /// with something that happens constantly means tuning the biggest moment in the mode by
        /// the smallest</em>.
        /// </para>
        /// <para>
        /// <b>A sun rather than a firework</b>, which is what the survey said
        /// (<c>Tools/charm_candidates.png</c>): the pack's three fireworks are small sparse rings
        /// that read at a cell and vanish at four, and the only things in it that hold a *body* at
        /// that size are the three suns. <c>Sun02</c> is the one none of the bosses took — the
        /// warlord throws <c>Sun01</c> and the overlord <c>Sun03</c> — and what keeps the three
        /// apart on a board is the colour rather than the mesh: a boss's spell is graded to one of
        /// the four <c>Pal</c> entries that is <em>not</em> a gem colour, and a charm is graded to
        /// the gem colour it was paid. Nothing else on this field wears a board colour at that
        /// size, so a charm cannot be read as a boss landing.
        /// </para>
        /// <para>
        /// <b>Graded with the roster's constants and not the elemental pair.</b> <c>Sun02</c> is
        /// painted violet, so it is a source being carried the whole way onto a hue it does not
        /// have — which is the case <see cref="RosterToward"/> exists for, and the case
        /// <see cref="Toward"/>'s third-of-the-way lean is explicitly not (see <see cref="Shots"/>).
        /// </para>
        /// </summary>
        static readonly Shot[] Charms =
        {
            new Shot { Key = "r", Prefab = "vfx_Hit_Sun02", Hue = Pal.Poppy },
            new Shot { Key = "g", Prefab = "vfx_Hit_Sun02", Hue = Pal.Mint  },
            new Shot { Key = "b", Prefab = "vfx_Hit_Sun02", Hue = Pal.Azure },
            new Shot { Key = "y", Prefab = "vfx_Hit_Sun02", Hue = Pal.Amber },
        };

        /// <summary>
        /// How a charm's detonation is cut: half again as wide as an impact and two frames longer.
        ///
        /// <para>
        /// <b>320 because that is the size it is drawn at, and 512 because that is the cap.</b>
        /// <c>SiegeView.Sprung</c> draws this at four to five cells; a cell is about 124 units on a
        /// phone, so the reel is drawn around 560 units across and every pixel over about 320 is
        /// paid for and thrown away by <c>ArtImportRules</c> anyway. Under the cap and over the
        /// draw size is the band, and this sits in it.
        /// </para>
        /// <para>
        /// <b>Fourteen frames, which is two more than an impact and six fewer than a storm.</b> A
        /// charm holds the fall and slows the run's own clock while it plays
        /// (<c>SiegeView.Dilate</c>) — long enough to be watched, which is the whole of what was
        /// asked for — so a twelve-frame reel at 30fps would be over in four tenths of a second
        /// with two thirds of the window still to run.
        /// </para>
        /// </summary>
        const int CharmSide = 320, CharmFrames = 14;


        /// <summary>
        /// How much bigger the warlord's three reels are than a ward's.
        ///
        /// <b>A boss's spell is on screen about twice a minute against a bolt's twenty-eight a
        /// second</b>, so it can afford the frames — and it has to be worth stopping for, which is
        /// the whole of invariant 26f's rule about price and spectacle read from the other end:
        /// this one is rare, so it is allowed to be loud.
        /// </summary>
        const int SpellTall = 384, SpellBurst = 320;
        const int SpellFrames = 18, SpellBurstFrames = 16;

        /// <summary>
        /// Where a named prefab lives in the pack.
        ///
        /// <b>Three folders tried in turn, because a muzzle and an impact are prefabs of their own
        /// here.</b> Every projectile names its own pair, which is what the bake normally follows —
        /// but a projectile whose flight is right and whose flash is wrong is a real case (a
        /// crescent blade that opens with an expanding ring), and the answer to it is to take the
        /// two from somewhere else rather than to give up the flight.
        /// </summary>
        static string PrefabPath(string name)
        {
            foreach (var folder in new[] { "/Projectiles/", "/Muzzles/", "/Hits/" })
            {
                string at = VfxBench.PackRoot + folder + name + ".prefab";
                if (System.IO.File.Exists(at)) return at;
            }

            return VfxBench.PackRoot + "/Projectiles/" + name + ".prefab";
        }

        static string PathOf(Shot shot)
            => string.IsNullOrEmpty(shot.Path) ? PrefabPath(shot.Prefab) : shot.Path;

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
        /// <summary>
        /// Which half of the mode's projectile art a run touches.
        ///
        /// <b>Separable because the two halves are baked differently and change for different
        /// reasons.</b> The elemental four and the bosses' eight are graded into a colour and have
        /// been stable for chapters; the roster's nineteen are bleached and are the ones being
        /// tuned. Re-baking everything to iterate on one of them would rewrite thirty reels nobody
        /// asked to change, and <see cref="Verify"/> would then be comparing a fresh bake against
        /// a fresh bake.
        /// </summary>
        [System.Flags]
        enum Parts
        {
            Elemental = 1,      // the starter's four, and the eight spells the bosses throw
            Roster = 2,         // one effect per bought turret
            Strike = 4,         // the stormcall, out of a different pack and graded its own way
            Charm = 8,          // the one detonation a charm goes off in, in four gem colours
            All = Elemental | Roster | Strike | Charm,
        }

        [MenuItem("Glimmer Grove/Art/Bake Siege Projectiles", false, 30)]
        public static void Bake() => Run(write: true, contact: false, parts: Parts.All);

        [MenuItem("Glimmer Grove/Art/Verify Siege Projectiles", false, 31)]
        public static void Verify() => Run(write: false, contact: false, parts: Parts.All);

        [MenuItem("Glimmer Grove/Art/Siege Projectile Contact Sheet", false, 32)]
        public static void Contact() => Run(write: false, contact: true, parts: Parts.Elemental);

        /// <summary>
        /// Bakes only the four elemental bolts and the eight boss spells — see <see cref="Parts"/>.
        ///
        /// <b>Narrow because the roster is 228 reels and these are twelve.</b> The other two parts
        /// already had an entry of their own and this did not, so the only way to re-bake a
        /// <see cref="Shots"/> row was to re-bake everything — twenty minutes to rewrite 240 files
        /// that had not changed, which is how a bake comes to be avoided and a table comes to
        /// disagree with the pictures on disk.
        /// </summary>
        [MenuItem("Glimmer Grove/Art/Bake Elemental Projectiles", false, 33)]
        public static void BakeElemental() => Run(write: true, contact: false, parts: Parts.Elemental);

        /// <summary>Bakes only the turret roster — see <see cref="Parts"/>.</summary>
        [MenuItem("Glimmer Grove/Art/Bake Turret Projectiles", false, 34)]
        public static void BakeRoster() => Run(write: true, contact: false, parts: Parts.Roster);

        /// <summary>
        /// Bakes only the stormcall's strike — see <see cref="Parts"/>.
        ///
        /// <b>Its own item for the same reason the roster has one.</b> It comes out of a different
        /// bought pack, it is framed round a ground burst rather than round a flying head, and it
        /// is graded and lit by numbers nothing else uses — so it is the one reel that gets tuned
        /// on its own, and re-baking thirty others to look at it would rewrite art nobody asked to
        /// change and leave <see cref="Verify"/> comparing a fresh bake against a fresh bake.
        /// </summary>
        [MenuItem("Glimmer Grove/Art/Bake Storm Strike", false, 35)]
        public static void BakeStrikeOnly() => Run(write: true, contact: false, parts: Parts.Strike);

        /// <summary>
        /// Bakes only the four charm detonations — see <see cref="Charms"/>.
        ///
        /// <b>Its own item for <see cref="BakeStrikeOnly"/>'s reason.</b> These are four reels cut
        /// bigger and longer than anything else on the field and they are the ones being looked at;
        /// re-baking the roster's 228 to see one of them rewrites art nobody asked to change and
        /// leaves <see cref="Verify"/> comparing a fresh bake against a fresh bake.
        /// </summary>
        [MenuItem("Glimmer Grove/Art/Bake Charm Blasts", false, 36)]
        public static void BakeCharms() => Run(write: true, contact: false, parts: Parts.Charm);

        /// <summary>
        /// Bakes one turret's three reels and nothing else.
        ///
        /// <para>
        /// <b>Its own entry for <see cref="BakeStrikeOnly"/>'s reason, one step finer.</b> A
        /// turret's effect is tuned on its own — a prefab swapped, a companion overridden, a slim
        /// applied — and re-baking the other eighteen to look at one rewrites 216 reels nobody
        /// asked to change. It also leaves <see cref="Verify"/> comparing a fresh bake against a
        /// fresh bake, which is the one thing that check must not be reduced to.
        /// </para>
        /// <para>
        /// <b>No menu item, because a menu item cannot name a turret.</b> It is called from a
        /// one-line script or from the editor bridge; a picker window is owed if this ever gets
        /// used often enough to be worth one.
        /// </para>
        /// </summary>
        public static void BakeOne(string id)
            => Run(write: true, contact: false, parts: Parts.Roster, only: id);

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
        static void Run(bool write, bool contact, Parts parts, string only = null)
        {
            var made = new Dictionary<string, Book>();
            GameObject stage = null;
            Camera cam = null;

            try
            {
                stage = BuildStage(out cam);

                if ((parts & Parts.Roster) != 0) RunRoster(stage.transform, cam, made, only);
                if ((parts & Parts.Elemental) != 0) RunElemental(stage.transform, cam, made);
                if ((parts & Parts.Strike) != 0) RunStrike(stage.transform, cam, made);
                if ((parts & Parts.Charm) != 0) RunCharms(stage.transform, cam, made);

                Finish(made, write, contact);
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

        /// <summary>The starter's four bolts, and the four spells the bosses throw.</summary>
        static void RunElemental(Transform stage, Camera cam, Dictionary<string, Book> made)
        {
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

                BakeOne(stage, cam, prefab, shot, made);
            }

            // **One row per boss, and eight rather than four.** A chapter shipped two bosses
            // sharing a body reel and separated by a run-time hue; this is the same fault's
            // other half — two spells that were one prefab at two colours. Each of the eight
            // now throws a different *kind* of object, which is invariant 33e's test asked of
            // the thing the author places rather than of the thing the player makes.
            //
            // **Eight because the last two were added the day the count was checked.** Six rows
            // served eight bosses: `Maw` and `Crypt` are the gravemaw's and the bonecaller's,
            // which had been wearing the warbringer's two reels under their own colours for two
            // chapters. `SiegeArtTests.EveryBossSpellIsItsOwnDrawing` is what keeps the count
            // honest now, because a ninth boss sharing an eighth's reels is green everywhere
            // else — it loads, it draws, and it is the wrong picture.
            foreach (var thrown in new[] { Hex, Spell, Roar, Omen, Snare, Quake, Maw, Crypt })
            {
                var warlord = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(thrown.Prefab));

                if (warlord == null)
                    Debug.LogWarning($"[siege shots] {thrown.Prefab} is not in this project — skipped.");
                else
                    BakeSpell(stage, cam, warlord, thrown, made);
            }
        }

        /// <summary>
        /// The stormcall's strike, which is the only thing here out of the lightning pack.
        ///
        /// <para>
        /// <b>It is baked on the *comet* path rather than the spell one, and the first cut proved
        /// why.</b> <see cref="BakeSpell"/> frames square because the thing it was written for is
        /// an orb; a bolt of lightning is eight to one, so framed square it came out as a thread
        /// down the middle of a 384-square texture with ninety per cent of the frame empty —
        /// invariant 37k's sliver exactly, and the second time this pack has produced it. What a
        /// bolt wants is a tall narrow frame measured off its own picture.
        /// </para>
        /// <para>
        /// <b>Its own step rather than a line in <see cref="RunElemental"/></b>, so that
        /// <see cref="Parts.Strike"/> can be baked on its own: it is graded, lit, framed and
        /// angled by numbers nothing else in this file uses, so it is the one reel that gets
        /// tuned alone.
        /// </para>
        /// </summary>
        static void RunStrike(Transform stage, Camera cam, Dictionary<string, Book> made)
        {
            var storm = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(Storm));

            if (storm == null)
                Debug.LogWarning($"[siege shots] {PathOf(Storm)} is not in this project — skipped.");
            else
                BakeStorm(stage, cam, storm, made);
        }

        /// <summary>
        /// The four charm detonations — see <see cref="Charms"/>.
        ///
        /// <b>Captured standing still, like a strike and unlike a bolt.</b> There is nothing to
        /// fly: a charm goes off in the cell it stood in, so what is wanted is the burst fully
        /// drawn and framed square about the point it happened at. It is the impact half of
        /// <see cref="BakeOne"/> with its own size and its own frame count, which is the whole of
        /// what a bespoke reel buys.
        /// </summary>
        static void RunCharms(Transform stage, Camera cam, Dictionary<string, Book> made)
        {
            for (int i = 0; i < Charms.Length; i++)
            {
                var shot = Charms[i];
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(shot.Prefab));

                if (prefab == null)
                {
                    // The pack is a bought asset and a checkout may not have it: say which one is
                    // missing and carry on, exactly as every other part of this bake does.
                    Debug.LogWarning($"[siege shots] {shot.Prefab} is not in this project — " +
                                     $"charm_blast_{shot.Key} skipped.");
                    continue;
                }

                var recipes = new[] { Ward(shot.Hue, RosterToward, RosterWhite, RosterMuted) };

                made["charm_blast_" + shot.Key] =
                    CaptureAll(stage, cam, prefab, recipes, CharmFrames, Burst(prefab), 0f, 0f,
                               .5f, CharmSide, CharmSide, CharmSide, 1f, 1f, comet: false)[0];
            }
        }

        /// <summary>Writes, compares or lays out whatever was baked. Reports rather than throws.</summary>
        static void Finish(Dictionary<string, Book> made, bool write, bool contact)
        {
            if (made.Count == 0)
            {
                Debug.LogWarning("[siege shots] nothing baked — is the projectile pack imported?");
                return;
            }

            if (contact) WriteContact(made);
            else if (write) WriteAll(made);
            else CompareAll(made);
        }

        /// <summary>
        /// Bakes one effect per bought turret — see <see cref="Roster"/>.
        ///
        /// <b>A missing prefab is a warning and never a throw</b>, exactly as the elemental four
        /// are: the pack is a bought asset and a checkout may not have it, and the whole of the
        /// rest of the bake is still worth having.
        /// </summary>
        static void RunRoster(Transform stage, Camera cam, Dictionary<string, Book> made,
                              string only = null)
        {
            for (int i = 0; i < Roster.Length; i++)
            {
                var shot = Roster[i];

                // Named rather than indexed, so a re-rung shelf cannot point this at the wrong
                // turret — the shelf has already been re-rung twice (invariants 37ax, 37ay).
                if (!string.IsNullOrEmpty(only) && shot.Key != only) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(shot));

                if (prefab == null)
                {
                    Debug.LogWarning($"[siege shots] {shot.Prefab} is not in this project — " +
                                     $"{shot.Key} skipped.");
                    continue;
                }

                GameObject composite = null;

                try
                {
                    var with = Named(shot.With);
                    if (with != null) prefab = composite = Composite(stage, prefab, with);

                    BakeTurret(stage, cam, prefab, shot, made);
                }
                finally { if (composite != null) Object.DestroyImmediate(composite); }
            }
        }

        /// <summary>
        /// The four ward colours a roster effect is baked in, in the order
        /// <c>WardLine.Colours</c> names them.
        ///
        /// <b>Read off <c>Pal</c> rather than written out</b>, which is what makes invariant 37f
        /// hold by construction: a bolt, the turret that threw it, the raider it is worth double
        /// against and the gems that paid for it all take one entry, so a palette retune moves
        /// every one of them and none of them can drift.
        /// </summary>
        static readonly Color[] WardHues = { Pal.Poppy, Pal.Mint, Pal.Azure, Pal.Amber };

        /// <summary>
        /// How far a roster effect is pulled onto its ward's colour.
        ///
        /// <para>
        /// <b>Nearly all the way, where the elemental four are pulled about a third.</b> Those
        /// four were <em>chosen</em> in invariant 37k because the pack already paints them roughly
        /// the colours the wards burn, so a lean is enough and more would flatten a fireball's
        /// yellow-hot head into one red. Nothing in the roster was chosen that way — a turret's
        /// effect is picked for its silhouette, so it arrives teal, magenta or gold and has to be
        /// carried the whole way. Left at a lean, a red ward fires a teal arrow.
        /// </para>
        /// <para>
        /// <b>Not the whole way, and the remainder is the point.</b> <see cref="Grade"/> keeps a
        /// pixel's own saturation and value — a near-white glint stays near-white, a deep body
        /// stays deep — so a full pull still leaves the effect's internal structure intact, and
        /// the last sixth keeps a little of whatever the pack drew so the four do not come out as
        /// four flat stencils.
        /// </para>
        /// </summary>
        const float RosterToward = .84f;

        /// <summary>
        /// How much of a near-white pixel a roster effect is allowed to keep.
        ///
        /// <para>
        /// <b>Much less than the elemental four keep, and the first roster bake is why.</b>
        /// <see cref="MostWhite"/> exists so a fireball's glint and the flash inside a lightning
        /// bolt stay light rather than being painted a flat colour, and at 0.62 it is right for
        /// four effects the pack already draws in roughly the right hue. Half this roster is drawn
        /// pale — an icicle, a crystal, a plasma core, a spark — so under the same rule the grade
        /// had almost nothing to bite on and they came out of the bake white with a tinge: a blue
        /// ward firing a white shard, which is the one thing invariant 37f is about. It is the same
        /// measurement 37k already records (0.18 median saturation on a bolt fired by a blue
        /// turret), arriving through a different door.
        /// </para>
        /// <para>
        /// <b>Not nought, because a specular highlight is not a colour.</b> A little protection
        /// keeps the hottest point of a flash and the glint off a facet light, which is what stops
        /// a graded effect reading as a coloured cut-out.
        /// </para>
        /// </summary>
        const float RosterWhite = .22f;

        /// <summary>How saturated the palest roster pixel still comes out. See <c>Recipe.Floor</c>.</summary>
        const float RosterMuted = .84f;

        /// <summary>
        /// How solid and how lit a ward's projectile comes out - see <c>Recipe.Lift</c> and
        /// <c>Recipe.Bloom</c>.
        ///
        /// <para>
        /// <b>Both moved together after play, and the complaint they answer was one sentence:</b>
        /// "they all look too shallow and transparent, I want them thick and bright and alive".
        /// Those are two different faults with two different causes. <em>Thin</em> was the alpha
        /// exponent, which at 1.25 took a trail's half-coverage down to 0.42 and its wisps to
        /// nothing. <em>Not alive</em> was the bloom this bake never had: these effects are drawn
        /// to be seen through a post-processing stack, and a bare camera renders the geometry
        /// without the light it throws.
        /// </para>
        /// <para>
        /// Applied to every ward projectile, the starter's four included, because the complaint
        /// was about all of them. The bosses' spells are deliberately left alone - they are not
        /// what was being judged, and a boss is loud enough already.
        /// </para>
        /// </summary>
        const float WardLift = .62f, WardBloom = .85f;

        /// <summary>
        /// One roster turret's three reels, in all four ward colours.
        ///
        /// <para>
        /// <b>Baked in four colours rather than bleached and tinted at run time, and that was
        /// settled by looking.</b> A bleached reel — white, with all its brightness in coverage —
        /// costs a quarter as much and can be worn in any colour by one multiply, which is what
        /// this shipped as first. Held up beside the elemental fireball it was a flat pink smear:
        /// a multiply can only vary <em>value</em>, and what makes these effects read is variation
        /// in <em>hue</em> — a yellow-hot head inside an orange body inside a red trail. Neither a
        /// white-core overlay nor a saturation ramp recovered it, because this pack's hot cores are
        /// saturated yellow rather than white and there is nothing for a white-core rule to catch.
        /// That is invariant 37l met from a third direction, and the cost of ignoring it is the one
        /// thing a turret somebody paid nine thousand credits for may not look: cheaper than the
        /// free one.
        /// </para>
        /// </summary>
        static void BakeTurret(Transform stage, Camera cam, GameObject prefab, Shot shot,
                               Dictionary<string, Book> made)
        {
            string key = shot.Key;

            var recipes = new Recipe[WardHues.Length];
            for (int i = 0; i < WardHues.Length; i++)
                recipes[i] = shot.White
                           ? Frost()
                           : Ward(WardHues[i], RosterToward, RosterWhite, RosterMuted);

            float authored = Mathf.Max(1f, Reflected(prefab, "speed", 30f));
            float warm = WarmFor(TrailOf(prefab));

            float speed = Trailing(stage, prefab, authored, warm, ShotSeconds);

            // The slim is the bolt's alone: a flash and an impact are radial, and squeezing a
            // radial burst makes an ellipse out of it (see <see cref="Shot.Slim"/>).
            Keep(made, "shot_" + key,
                 CaptureAll(stage, cam, prefab, recipes, ShotFrames, ShotSeconds, speed, warm,
                            SiegeView.HeadAt, ShotTall, NarrowestShot, WidestShot,
                            LeanestShot, LongestShot, comet: true, slim: shot.Slim));

            var muzzle = Named(shot.Muzzle) ?? Companion(prefab, "muzzlePrefab");
            if (muzzle != null)
                Keep(made, "muzzle_" + key,
                     CaptureAll(stage, cam, muzzle, recipes, MuzzleFrames, Burst(muzzle), 0f, 0f,
                                SiegeView.MuzzleAt, BurstSide, NarrowestMuzzle, BurstSide,
                                LeanestMuzzle, LongestMuzzle, comet: false));

            var hit = Named(shot.Hit) ?? Companion(prefab, "hitPrefab");
            if (hit != null)
                Keep(made, "hit_" + key,
                     CaptureAll(stage, cam, hit, recipes, HitFrames, Burst(hit), 0f, 0f,
                                .5f, BurstSide, BurstSide, BurstSide, 1f, 1f, comet: false));
        }

        /// <summary>A pack prefab by name, or null when the name is blank or nothing answers it.</summary>
        static GameObject Named(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            var found = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(name));
            if (found == null) Debug.LogWarning("[siege shots] " + name + " is not in this project.");

            return found;
        }

        /// <summary>
        /// Two pack prefabs under one root, so the bake can fly them as a single effect.
        ///
        /// <b>A scene object rather than an asset, and everything downstream is fine with that.</b>
        /// <see cref="Spawn"/> instantiates whatever it is handed and the rig walks the copy's own
        /// hierarchy - <see cref="Roots"/> finds the top system of each half, <see cref="Burst"/>
        /// and <see cref="TrailOf"/> measure across both, and <see cref="Advance"/> moves the pair
        /// together at one speed, which is what makes their trails agree.
        /// </summary>
        static GameObject Composite(Transform stage, GameObject baseFx, GameObject withFx)
        {
            var root = new GameObject("~SiegeComposite") { hideFlags = HideFlags.HideAndDontSave };
            root.transform.SetParent(stage, false);
            root.transform.localPosition = Vector3.zero;

            foreach (var part in new[] { baseFx, withFx })
            {
                var copy = (GameObject)PrefabUtility.InstantiatePrefab(part, root.transform);
                copy.transform.localPosition = Vector3.zero;
                copy.transform.localRotation = Quaternion.identity;
            }

            return root;
        }

        /// <summary>Files one effect's four colours under <c>&lt;key&gt;_r</c> … <c>_y</c>.</summary>
        static void Keep(Dictionary<string, Book> made, string key, Book[] books)
        {
            for (int i = 0; i < books.Length && i < Wards.WardLine.Colours.Length; i++)
                made[key + "_" + Wards.WardLine.Colours[i]] = books[i];
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
            // it at whatever makes that tail fit a comet-shaped frame. See `Trailing`, which is
            // this and is shared with the two spells that are comets.
            float speed = Trailing(stage, prefab, authored, warm, ShotSeconds);

            // The row's own, never this file's elemental pair: see <see cref="Shots"/> for why
            // three of the four are carried the whole way and one is not.
            var recipes = new[] { Ward(shot.Hue, shot.Toward, shot.Keep, shot.Floor) };

            made["shot_" + shot.Key] =
                CaptureAll(stage, cam, prefab, recipes, ShotFrames, ShotSeconds, speed, warm,
                           SiegeView.HeadAt, ShotTall, NarrowestShot, WidestShot,
                           LeanestShot, LongestShot, comet: true)[0];

            // The pack fires as three parts and the vendor's own demo plays all three. Showing the
            // middle one alone was judging a sentence by its verb.
            //
            // The impact stays square — it is radial and it is drawn where something happened —
            // while the flash is anchored at the barrel and free to be taller than it is wide.
            var muzzle = Companion(prefab, "muzzlePrefab");
            if (muzzle != null)
                made["muzzle_" + shot.Key] =
                    CaptureAll(stage, cam, muzzle, recipes, MuzzleFrames, Burst(muzzle), 0f, 0f,
                               SiegeView.MuzzleAt, BurstSide, NarrowestMuzzle, BurstSide,
                               LeanestMuzzle, LongestMuzzle, comet: false)[0];

            var hit = Companion(prefab, "hitPrefab");
            if (hit != null)
                made["hit_" + shot.Key] =
                    CaptureAll(stage, cam, hit, recipes, HitFrames, Burst(hit), 0f, 0f,
                               .5f, BurstSide, BurstSide, BurstSide, 1f, 1f, comet: false)[0];
        }

        /// <summary>One ward-projectile recipe: graded to a hue, and lit the way a ward's bolt is.</summary>
        static Recipe Ward(Color hue, float toward, float white, float floor)
            => new Recipe
            {
                Hue = hue, Toward = toward, White = white, Floor = floor,
                Lift = WardLift, Bloom = WardBloom,
            };

        /// <summary>
        /// How cold the white a frost turret throws is, and how much of the colour is taken out.
        ///
        /// <b>Not a full bleach.</b> At <c>1</c> a reel is grey and grey over a lit hill reads as
        /// smoke; the tenth that is left leans the shadows of a snowball toward ice rather than
        /// toward nothing. The floor is nought for the same reason the roster's is high: that one
        /// exists to stop a pale source washing out, and washing out is the point here.
        /// </summary>
        const float FrostBleach = .90f;

        /// <summary>
        /// The recipe a <see cref="Shot.White"/> reel is baked with, in every ward colour.
        ///
        /// See <see cref="Shot.White"/> for why one ability is allowed to ignore the ward's
        /// colour. Everything else about it is a ward's - the same alpha curve and the same bloom,
        /// so a snowball sits in the line at the weight every other bolt does.
        /// </summary>
        static Recipe Frost()
            => new Recipe
            {
                Hue = Pal.Azure, Toward = 1f, White = RosterWhite, Floor = 0f,
                Bleach = FrostBleach, Lift = WardLift, Bloom = WardBloom,
            };

        /// <summary>
        /// A stormcall's bolt and the burst it leaves, cut bigger than a ward's.
        ///
        /// <para>
        /// <b>The comet path with a bigger frame</b> — a ward's bolt and this are the same shape
        /// and differ only in how often they happen, so they want the same framing and a different
        /// size. A ward fires four or five a second and is cut to read at that rate; this goes off
        /// once a run and is what forty gems bought, so it is taller, kept longer and framed with
        /// more of its own trail.
        /// </para>
        /// <para>
        /// <b>It is not flown.</b> A ward's bolt crosses the hill, so its speed is bent until its
        /// tail fits the frame; a storm falls straight down onto one raider and the view draws the
        /// sprite stretched from the top of the board to whatever it hit, so what is wanted here is
        /// the bolt standing still and fully drawn.
        /// </para>
        /// </summary>
        static void BakeStorm(Transform stage, Camera cam, GameObject prefab,
                              Dictionary<string, Book> made)
        {
            // **One reel, not three, because this pack draws the whole event.** A ward's bolt is
            // three prefabs - a flash at the barrel, a thing that flies, a burst where it lands -
            // because it is a projectile crossing the hill. This is a *strike*: the bolt, the
            // ground crack and the shockwave rings are one effect that happens in one place, so
            // there is nothing to fly and nothing to fire it. It is captured standing still.
            var book = CaptureAll(stage, cam, prefab, new[] { Strike(Storm.Hue) },
                                  StormFrames, StormSeconds, 0f, 0f,
                                  1f - SiegeView.StrikeAt, StormTall, NarrowestStorm, WidestStorm,
                                  LeanestStorm, LongestStorm, comet: false, tilt: StormTilt)[0];

            Tighten(book, 1f - SiegeView.StrikeAt);
            made["storm"] = book;
        }

        /// <summary>
        /// Trims a reel to its own picture, keeping the point it is anchored by exactly where it
        /// is.
        ///
        /// <para>
        /// <b>Because a sprite whose frame is bigger than its content cannot be reasoned about
        /// from outside.</b> <see cref="Frame"/> sizes the world frame from the widest thing in
        /// shot, so an effect that is wide and short — which a strike is, once its ground burst is
        /// rendered at all — comes out in a frame a third taller than anything drawn in it. That
        /// is invisible while a reel is only ever *drawn*: transparent padding costs nothing to
        /// look at. It stops being invisible the moment the board has to know **where the bolt
        /// ends**, which is what keeps a strike on the plate rather than over the status bar
        /// (invariant 37ac). With this, a strike's picture runs from its own frame's foot to its
        /// own frame's top and the view can size it against the room it has.
        /// </para>
        /// <para>
        /// <b>It keeps the anchor rather than centring the content</b>, which is the whole
        /// difficulty: the flash has to stay at <paramref name="head"/> of the way up, so the
        /// frame is re-cut around *that row* and the two sides are padded to whatever the fraction
        /// demands. Cropping to the bounding box alone would move the flash and put the strike
        /// back where it was.
        /// </para>
        /// </summary>
        static void Tighten(Book book, float head)
        {
            var all = book.Sheet.GetPixels32();
            int w = book.Wide, t = book.Tall, n = book.Frames;

            int minX = w, maxX = -1, minY = t, maxY = -1;

            for (int f = 0; f < n; f++)
                for (int y = 0; y < t; y++)
                    for (int x = 0; x < w; x++)
                    {
                        if (all[(f * t + y) * w + x].a <= AlphaFloor) continue;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }

            if (maxX < minX || maxY < minY) return;

            // A margin, because the alpha floor cuts the very last of a bloom and a hard edge on a
            // halo is a line drawn across the sky.
            minX = Mathf.Max(0, minX - Margin); maxX = Mathf.Min(w - 1, maxX + Margin);
            minY = Mathf.Max(0, minY - Margin); maxY = Mathf.Min(t - 1, maxY + Margin);

            // The row the effect is anchored by. `Roll` puts the prefab's own origin `head` of the
            // way up whatever it renders into, so this is exact rather than measured.
            int origin = Mathf.Clamp(Mathf.RoundToInt(head * t), 0, t - 1);

            float over = Mathf.Max(1, maxY - origin) / Mathf.Max(.02f, 1f - head);
            float under = Mathf.Max(1, origin - minY) / Mathf.Max(.02f, head);

            int tall = Mathf.Max(32, Mathf.CeilToInt(Mathf.Max(over, under) / 4f) * 4);
            int mark = Mathf.RoundToInt(head * tall);

            // The rig is centred in x, so the picture is too: the frame is cut round the middle
            // rather than round the bounding box, or a strike leans to whichever side its sparks
            // happened to fly.
            int reach = Mathf.Max(maxX - w / 2, w / 2 - minX) + 1;
            int wide = Mathf.Clamp(Mathf.CeilToInt(reach / 8f) * 16, 16, w);

            if (wide >= w && tall >= t) return;

            var cut = new Color32[wide * tall * n];
            var clear = new Color32(255, 255, 255, 0);
            for (int i = 0; i < cut.Length; i++) cut[i] = clear;

            for (int f = 0; f < n; f++)
                for (int y = 0; y < tall; y++)
                {
                    int sy = y - mark + origin;
                    if (sy < 0 || sy >= t) continue;

                    for (int x = 0; x < wide; x++)
                    {
                        int sx = x - wide / 2 + w / 2;
                        if (sx < 0 || sx >= w) continue;
                        cut[(f * tall + y) * wide + x] = all[(f * t + sy) * w + sx];
                    }
                }

            var sheet = new Texture2D(wide, tall * n, TextureFormat.RGBA32, false, false);
            sheet.SetPixels32(cut);
            sheet.Apply(false);

            Object.DestroyImmediate(book.Sheet);
            book.Sheet = sheet;
            book.Wide = wide;
            book.Tall = tall;
        }

        /// <summary>The alpha a pixel has to carry before <see cref="Tighten"/> counts it drawn.</summary>
        const byte AlphaFloor = 4;

        /// <summary>Pixels of air <see cref="Tighten"/> leaves round what it found.</summary>
        const int Margin = 3;

        /// <summary>How a storm's bolt is cut: taller and wider than a ward's, and held longer.</summary>
        const int StormFrames = 20, StormTall = 512;
        const int NarrowestStorm = 160, WidestStorm = 448;
        const float StormSeconds = 1.0f;

        /// <summary>How far from square a storm's frame may go. Wider than a bolt: it has ground.</summary>
        const float LeanestStorm = 0.55f, LongestStorm = 3.0f;

        /// <summary>
        /// How far the rig is pitched down to bake a strike — see <see cref="Roll"/>'s tilt.
        ///
        /// <para>
        /// <b>Enough that a ring on the floor is an ellipse, and not so much that the bolt is
        /// foreshortened.</b> At nought the pack's ground crack, splat, shockwave and rings are
        /// all edge-on hairlines and the reel is a bolt with a small star at the end of it; every
        /// one of them is a flat quad, so what shows them is the only thing that ever could. A
        /// strike is also the one effect here whose *ground* is part of the picture — the board
        /// draws raiders standing on a hill, so a burst spreading around their feet is what says
        /// the lightning arrived somewhere rather than merely existed.
        /// </para>
        /// </summary>
        const float StormTilt = 26f;

        /// <summary>
        /// How a strike is graded and lit — see <see cref="Strike"/>.
        ///
        /// <para>
        /// <b><c>Toward</c> is high where a ward's is a third, and the pack is why.</b>
        /// <see cref="Toward"/> is .38 because the four elemental effects were *chosen* for
        /// already wearing roughly the colour the ward burns, so agreeing with <c>Pal</c> is a
        /// lean. Nothing about this pack was chosen that way: its bolt is white with cyan through
        /// the core and a gold flare at the end, so a .38 lean left the cyan exactly where it was
        /// — which on a device read as lightning with a green tinge in it, in an item whose own
        /// icon draws its bolts in <c>Pal.Sun</c>.
        /// </para>
        /// <para>
        /// <b>And it stops short of all the way</b>, for invariant 37p's reason: a full re-hue
        /// takes every warm and cool note the artist drew and collapses them into one flat
        /// colour. A fifth of the original spread is what makes a thing look *made of* a colour
        /// rather than painted it.
        /// </para>
        /// <para>
        /// <b><c>White</c> is the highest here of anywhere</b>, because a white-hot core inside a
        /// coloured halo is not a nicety of lightning, it is what lightning <em>is</em> — and it
        /// is the difference between the vendor's picture and a gold streak. It is safe here in a
        /// way invariant 37f says it would not be on a ward: a strike answers to no colour rule,
        /// so a white core cannot be read as the wrong element.
        /// </para>
        /// </summary>
        const float StrikeToward = .80f, StrikeWhite = .76f, StrikeFloor = .72f;

        /// <summary>
        /// How thick a strike is drawn and how much light it spills — invariant 37af's two
        /// numbers, which this reel was never given.
        ///
        /// <b>Both further than a ward's</b> (.62 and .85): a bolt is thin geometry, so the curve
        /// has to work harder to make a body out of it, and a strike is the one effect in this
        /// game that is allowed to light the hill around it.
        /// </summary>
        const float StrikeLift = .50f, StrikeBloom = 1.85f;

        /// <summary>
        /// The colour the wide half of a strike's halo is lit in — see <see cref="Recipe.Halo"/>.
        ///
        /// <para>
        /// <b><c>Pal.Ember</c>, so the light goes gold at the bolt and warm in the air.</b> The
        /// tight scale keeps <c>Pal.Sun</c> on its way to white, which is what the item's own icon
        /// draws; the wash around it is the next colour along the board's own warm run. That is
        /// what an incandescent thing looks like, and it is what a single-coloured halo could only
        /// ever have made bigger.
        /// </para>
        /// <para>
        /// <b>A colour the board already has</b>, rather than a red picked to match a vendor's
        /// demo. Ember is the fire the firepot throws, so a strike and a blast agree about what
        /// hot looks like on this hill.
        /// </para>
        /// </summary>
        static Color StrikeHalo => Pal.Ember;

        /// <summary>
        /// How hard a strike's core is blown out, and from what coverage — see
        /// <see cref="Recipe.Hot"/>.
        ///
        /// <para>
        /// <b>Nearly all the way, and only at the very top.</b> The three-rung ladder this
        /// completes is white core, gold body, ember haze, and it only reads if the rungs are far
        /// apart: blowing out from half coverage would take the body with it and leave a pale
        /// bolt with a gold edge, which is the mirror of the fault it fixes. Above four fifths is
        /// the bolt's own filament and the middle of the ground burst, and nothing else.
        /// </para>
        /// </summary>
        const float StrikeHot = .90f, StrikeHotFrom = .80f;

        /// <summary>
        /// The warlord's spell, as the same three parts a ward's bolt is.
        ///
        /// <b>Its own method rather than a flag on <see cref="BakeOne"/></b>, because almost every
        /// number differs — it is baked bigger, kept longer, and framed with more of its own tail,
        /// and a <c>BakeOne</c> with six extra parameters would be the same method twice with the
        /// two versions interleaved.
        /// </summary>
        static void BakeSpell(Transform stage, Camera cam, GameObject prefab, Shot thrown,
                              Dictionary<string, Book> made)
        {
            float warm = WarmFor(TrailOf(prefab));
            float seconds = SiegeTuning.BossFlight * 2f;

            // **Framed square and flown at the speed it was authored at, unlike every bolt.**
            // The wards' four are comets — nearly all trail — so their frames are tall and their
            // speed is bent until the tail fits one. This is an orb: baked as a comet it came out
            // 112 x 512 with the whole effect inside the top ninety rows and *eighty per cent of
            // the frame empty*, which the view then draws as a violet sliver seven cells long
            // crossing a hill four cells deep (invariant 37k's sliver, from the other direction).
            // Nothing about the framing was wrong for a comet; the thing being framed was not one.
            //
            // It also does the job invariant 33e asks of anything the boss brings: a slow round
            // orb is a different *kind* of object from four streaking comets, not a bigger one.
            // **A spell aimed at the hill has no flight**, and baking one anyway is how an
            // unloadable reel came to ship in a bundle. See `Shot.Grounded`.
            float authored = Mathf.Max(1f, Reflected(prefab, "speed", 30f));

            // The row's grade where it states one, this file's lean where it does not. All three
            // reels of a spell take the same recipe: a boss's flight, its flash and its impact are
            // one event and the first thing a player would notice about three different grades is
            // that they were three.
            var recipe = Spellwork(thrown.Hue, thrown.Toward, thrown.Keep, thrown.Floor);

            if (!thrown.Grounded)
                made[thrown.Key] = thrown.Comet

                    // **Framed round its head, and flown at whatever makes its tail fit** — the
                    // two decide each other, so the speed is found the way `BakeOne` finds it
                    // rather than taken as authored. A comet flown at its authored speed into a
                    // frame sized for an orb is how both of these came out as threads.
                    ? Capture(stage, cam, prefab, thrown.Hue, SpellFrames, seconds,
                              Trailing(stage, prefab, authored, warm, seconds), warm,
                              SiegeView.HeadAt, SpellTall, NarrowestShot, WidestShot,
                              LeanestShot, LongestShot, comet: true, recipe: recipe)

                    : Capture(stage, cam, prefab, thrown.Hue, SpellFrames, seconds,
                              authored, warm,
                              .5f, SpellTall, SpellTall, SpellTall, 1f, 1f, comet: false,
                              recipe: recipe);

            var muzzle = Companion(prefab, thrown.Muzzle, "muzzlePrefab");
            if (muzzle != null)
                made[thrown.Key + "_muzzle"] =
                    Capture(stage, cam, muzzle, thrown.Hue, SpellBurstFrames, Burst(muzzle), 0f, 0f,
                            SiegeView.MuzzleAt, SpellBurst, NarrowestMuzzle, SpellBurst,
                            LeanestMuzzle, LongestMuzzle, comet: false, recipe: recipe);

            var hit = Companion(prefab, thrown.Hit, "hitPrefab");
            if (hit != null)
                made[thrown.Key + "_hit"] =
                    Capture(stage, cam, hit, thrown.Hue, SpellBurstFrames, Burst(hit), 0f, 0f,
                            .5f, SpellBurst, SpellBurst, SpellBurst, 1f, 1f, comet: false,
                            recipe: recipe);
        }

        /// <summary>
        /// A companion effect: the row's own override where it names one, and the prefab's own
        /// otherwise.
        ///
        /// <b>The boss rows could not say this and the roster rows always could</b>, which is the
        /// whole of why an ironclad's axe landed as a two-pixel line. <see cref="Shot.Muzzle"/>
        /// and <see cref="Shot.Hit"/> exist precisely for "the flight is right and its companions
        /// are not" — the cleaver's note records the case being met and fixed — and
        /// <see cref="BakeSpell"/> simply never read them, so the one table that most needed the
        /// escape hatch was the one table without it.
        /// </summary>
        static GameObject Companion(GameObject prefab, string named, string field)
        {
            if (!string.IsNullOrEmpty(named))
            {
                var own = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(named));

                if (own != null) return own;

                // Named and absent is a typo, never a checkout without the pack: the pack being
                // absent is reported once by the caller and every prefab in it goes missing
                // together. Falling back silently would bake the wrong picture under the right
                // name, which is the one outcome no gate here can see.
                Debug.LogWarning($"[siege shots] {named} is not in this project — " +
                                 $"falling back to the prefab's own {field}.");
            }

            return Companion(prefab, field);
        }

        /// <summary>
        /// How fast a comet has to fly for its tail to fit the frame it is about to be given.
        ///
        /// <b>Lifted out of <see cref="BakeOne"/> the day a second path needed it.</b> Every trail
        /// in this pack is a world-space system emitting per second, so the length one smears over
        /// is speed times particle lifetime — which makes framing and flight two halves of one
        /// decision, and makes a comet flown at its authored speed into a frame chosen for
        /// something else the bug the bench shipped once already. The band is
        /// <see cref="SlowestBake"/>..<see cref="FastestBake"/> for that reason: outside it the
        /// flames pile onto the head and the comet becomes an oval with debris round it.
        /// </summary>
        static float Trailing(Transform stage, GameObject prefab, float authored, float warm,
                              float seconds)
        {
            var seen = Sample(stage, prefab, authored, warm, seconds, SiegeView.HeadAt);

            if (seen.Behind <= .05f) return authored;

            float want = seen.Across * 2f * WantedShot * SiegeView.HeadAt;

            // **Rounded to hundredths, because this number multiplies every frame's position and
            // the measurement behind it is not bit-stable.** `Sample` reads `Renderer.bounds`
            // after simulating, and a GPU is not obliged to land a bounds query on the same last
            // bit twice — normally invisible, because <see cref="CompareAll"/> allows six levels
            // of drift. Here it is not invisible: the factor scales *distance travelled*, so a
            // difference in its last digit puts the head a fraction further along on frame one
            // and a multiple of that fraction further along on frame seventeen. Measured on the
            // ironclad's blade, which drifted 6.2 levels at frame 3 and 8.5 by frame 17 —
            // monotonically, which is the signature of a speed and never of a rasteriser.
            //
            // **Thousandths, which is the finest grid that still absorbs the wobble** — and the
            // fineness is the point rather than a detail. The noise being removed is a last-bit
            // difference, parts per million; a grid a thousand times coarser than that is ample.
            // The first cut used *hundredths*, which is also ample and perturbs the value ten
            // times as far — far enough to push a reel sitting near a quantisation boundary in
            // `Pixels` across it. That is not theoretical: it re-framed eight shipped frost
            // turret reels from 128 to 112 pixels wide, drawing every one of them 14 % longer,
            // as a side effect of a change whose whole purpose was to stop framing moving.
            // **Quantise as finely as the noise allows, never as coarsely as it tolerates.**
            float factor = Mathf.Clamp(want / seen.Behind, SlowestBake, FastestBake);

            return authored * (Mathf.Round(factor * 1000f) / 1000f);
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
                            bool comet, Recipe? recipe = null)
            => CaptureAll(stage, cam, prefab,
                          new[] { recipe ?? Spellwork(hue, 0f, 0f, 0f) },
                          frames, seconds, speed, warm, head, tallPx, narrowest, widest,
                          leanest, longest, comet)[0];

        /// <summary>
        /// How a <b>spell</b> is graded: this file's own lean, with the row allowed to overrule
        /// any of the three numbers.
        ///
        /// <para>
        /// <b><see cref="Shot"/> has carried <see cref="Shot.Toward"/>, <see cref="Shot.Keep"/>
        /// and <see cref="Shot.Floor"/> since the day their note was written, and
        /// <see cref="BakeSpell"/> has never read one of them.</b> That note says, in as many
        /// words, that how far a source must be carried is a fact about <em>that source</em> — and
        /// then the boss table, which is the one table whose eight rows come from eight unrelated
        /// families, was wired to a single constant. It is the third field on this row to turn out
        /// to be decorative (see <see cref="Companion"/> for the muzzle and the impact), and all
        /// three are the same omission: the spell path was written when there was one spell.
        /// </para>
        /// <para>
        /// <b>Nought means "this file's own", so no row's picture moves.</b> <see cref="Toward"/>
        /// is a 38 % lean, which is right only where the pack already draws roughly the hue being
        /// graded to; <see cref="RosterToward"/> is 84 % and exists for a source that arrives in
        /// the wrong colour entirely. Today every boss row wants the lean and every boss row gets
        /// it — <see cref="Verify"/> proves that, because a changed number would rewrite every
        /// reel it touches. What has changed is that the next one can say otherwise.
        /// </para>
        /// </summary>
        static Recipe Spellwork(Color hue, float toward, float keep, float floor) => new Recipe
        {
            Hue = hue,
            Toward = toward > 0f ? toward : Toward,
            White = keep > 0f ? keep : MostWhite,
            Floor = floor > 0f ? floor : Muted,
            Lift = Lift,
            Bloom = 0f,
        };

        /// <summary>
        /// One reel graded the way a <em>strike</em> is: the loudest thing this mode ever draws.
        ///
        /// <para>
        /// <b>Its own recipe because it was on nobody's.</b> Invariant 37af gave every ward
        /// projectile a bloom and an alpha curve that thickens rather than thins, on the finding
        /// that these packs are authored to be seen through a post-processing stack and a bare
        /// camera bakes the geometry with the light left out. <see cref="Capture"/> — which is
        /// what the storm and the four boss spells are baked through — was never moved, so this
        /// reel shipped with <c>Bloom = 0</c> and an exponent of <see cref="Lift"/> thinning it on
        /// top. That is the whole of "it looks nothing like the store page": the vendor's own
        /// demo scene carries a bloom profile, and ours rendered without one.
        /// </para>
        /// <para>
        /// <b>Louder than a ward's, and that is a decision rather than a slip.</b> A ward fires
        /// four or five bolts a second for a whole run; this goes off once, costs forty gems and
        /// is the biggest thing on the board when it does. It is the one reel allowed to spill
        /// more light than it draws.
        /// </para>
        /// </summary>
        static Recipe Strike(Color hue)
            => new Recipe
            {
                Hue = hue, Toward = StrikeToward, White = StrikeWhite, Floor = StrikeFloor,
                Lift = StrikeLift, Bloom = StrikeBloom, Halo = StrikeHalo,
                Hot = StrikeHot, HotFrom = StrikeHotFrom,
            };

        /// <summary>
        /// How one reel is coloured: which hue it is pulled onto, and how far.
        ///
        /// <para>
        /// <b>How far is a per-effect decision, and treating it as a constant is what made the
        /// first roster bake unusable.</b> The elemental four were chosen in invariant 37k
        /// <em>because</em> the pack already paints them roughly the colours the wards burn, so
        /// agreeing with <c>Pal</c> is a lean of about a third and anything more flattens a
        /// fireball's yellow-hot head into one red. Nothing else in the pack was chosen that way —
        /// a turret's effect is picked for its silhouette, so it arrives teal or magenta or gold
        /// and has to be carried the whole way, or a red ward fires a teal arrow.
        /// </para>
        /// </summary>
        struct Recipe
        {
            public Color Hue;
            public float Toward;
            public float White;

            /// <summary>
            /// How far a pixel's saturation is pulled out before <see cref="Hue"/> is applied at
            /// all, as a fraction: nought leaves the recipe's own colour and one bleaches to grey.
            ///
            /// <para>
            /// <b>A separate dial from <see cref="White"/>, which only protects pixels that are
            /// already bright and pale.</b> That is right for keeping a flame's hot core from
            /// turning red; it cannot make a whole effect white, because a trail at half
            /// brightness never reaches its threshold. This is applied to every pixel, so what
            /// comes out is the effect's own light with the colour taken off it.
            /// </para>
            /// <para>
            /// <b>Short of one on purpose.</b> At a full bleach a reel is grey, and grey over a
            /// lit hill reads as smoke; a little of the hue left in gives snow its cold edge.
            /// </para>
            /// </summary>
            public float Bleach;

            /// <summary>
            /// The least saturated a graded pixel may come out, as a fraction of full.
            ///
            /// <para>
            /// <b>A floor rather than a scale, because the source's own saturation is the thing
            /// that cannot be trusted here.</b> <see cref="Grade"/> keeps a pixel's saturation so
            /// that grey smoke stays grey and a strongly coloured pixel becomes strongly the
            /// ward's colour — which is right for four effects the pack draws saturated already.
            /// Half the turret roster is drawn pale, so under the same rule every one of them
            /// graded to a wash: a pink icicle, a pink crystal, a pink plasma core. Raising the
            /// floor makes the colour the bake's decision instead of the pack's, which is what it
            /// has to be for an effect chosen by silhouette.
            /// </para>
            /// </summary>
            public float Floor;

            /// <summary>
            /// The exponent the coverage is raised to before it becomes alpha.
            ///
            /// <para>
            /// <b>Above one thins a reel and below one thickens it, and the shipped 1.25 was the
            /// wrong side of that.</b> Coverage is nearly all mid-tones on a trail — a wisp is a
            /// tenth of an alpha and a flame's body about a half — so an exponent of 1.25 takes a
            /// half to 0.42 and a tenth to 0.06, which is what "they all look shallow and
            /// transparent" was. It is not the haze problem that number was chosen for:
            /// <see cref="Haze"/> now drops the near-black wash outright, so what an exponent
            /// below one promotes is the effect rather than the fog it sits in.
            /// </para>
            /// </summary>
            public float Lift;

            /// <summary>
            /// How much light a reel spills around itself.
            ///
            /// <para>
            /// <b>These effects are authored to be seen through bloom, and this bake had none.</b>
            /// The pack's own demos run a post-processing stack — the lightning pack in this
            /// project ships one — so what a plain camera renders is the raw geometry of an effect
            /// with the glow that makes it read as *light* missing. Adding it here is restoring the
            /// intended picture rather than decorating one: a bright-pass, blurred at two scales
            /// and added back, which is what a renderer's bloom is.
            /// </para>
            /// <para>
            /// <b>It raises alpha as well as colour</b>, because a halo nothing can see through is
            /// not a halo — the glow has to be *drawn* over the hill, and a reel is composited with
            /// ordinary alpha blending.
            /// </para>
            /// </summary>
            public float Bloom;

            /// <summary>
            /// The colour the <em>wide</em> half of the bloom is lit in, when it differs from the
            /// tight half. Left unset (alpha nought) the whole halo is one colour, which is what
            /// every reel but the strike wants.
            ///
            /// <para>
            /// <b>Light reddens as it spreads, and a bloom that does not is the difference between
            /// a shape painted a colour and a thing that is burning.</b> Every photograph of
            /// something incandescent is a white middle inside a warm haze — it is what the eye
            /// reads as heat — and it is most of why the vendor's own picture of this pack looks
            /// the way it does. A single-colour halo can only make the gold larger.
            /// </para>
            /// </summary>
            public Color Halo;

            /// <summary>
            /// How far the densest coverage is blown out toward white, and where that starts.
            ///
            /// <para>
            /// <b>The exposure a bare camera never applied.</b> <see cref="Reel"/> divides every
            /// pixel by its own largest channel, which is what puts all of an effect's brightness
            /// into the alpha and lets one render be graded four ways — and the cost of it is that
            /// the *colour* left behind carries no brightness at all. A pixel with a tenth of the
            /// light and a pixel with all of it come out the same gold, so a thick bolt is not a
            /// hot bolt, it is a wide one.
            /// </para>
            /// <para>
            /// <b>Every real renderer does this and ours had no way to.</b> An emissive effect at
            /// full coverage is over one in linear light, and a tonemap brings it back as *white*;
            /// the pack is authored against a stack that does exactly that, and the camera here
            /// runs with <c>allowHDR</c> off and nothing after it. So the ladder a hot thing is
            /// read by — white core, saturated body, warm haze — had its top rung missing, and
            /// what shipped was the middle rung painted over all three.
            /// </para>
            /// <para>
            /// <b>Driven by coverage rather than by the source's own whiteness</b>, which is what
            /// separates it from <see cref="Recipe.White"/>. That one asks whether the artist drew
            /// this pixel pale and protects it; this one asks how much light is standing here. A
            /// flame's core is saturated yellow and still blows out, which is precisely the case
            /// the whiteness rule is documented as unable to see.
            /// </para>
            /// </summary>
            public float Hot, HotFrom;
        }

        /// <summary>
        /// Renders an effect once and grades it into several reels.
        ///
        /// <para>
        /// <b>One render, many colours, and that is not merely an optimisation.</b> The four
        /// colours of a turret's bolt have to be the same *picture* — the same trail, the same
        /// sparks, the same frame — or a player who re-stands a turret on another slot is looking
        /// at a different effect. Rendering four times would give four, because a particle system
        /// reseeded is a different comet however carefully it is seeded, and the framing is
        /// measured off what was drawn. It also happens to make the bake four times cheaper.
        /// </para>
        /// </summary>
        static Book[] CaptureAll(Transform stage, Camera cam, GameObject prefab, Recipe[] recipes,
                                 int frames, float seconds, float speed, float warm, float head,
                                 int tallPx, int narrowest, int widest, float leanest,
                                 float longest, bool comet, float tilt = 0f, float slim = 0f)
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
                           height, tilt);

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
                               warm + live.Skip, head, take, tilt);
                }
            }
            finally { Object.DestroyImmediate(pixels); }

            // **Before the gradings and not after**, so the four colours are one squeeze rather
            // than four - which is the same reason one render is graded four ways.
            //
            // **At the supersampled size, which is the size `raw` really is.** Handed the final
            // frame's dimensions it read a quarter of the buffer with the wrong stride, corrupted
            // the faintest end of the tail, and changed nothing an eye or a width measurement
            // could see - a fix that reported success and did nothing.
            if (slim > 0f && slim < .999f)
                Slimmer(raw, wide * Super, tallPx * Super, slim);

            var books = new Book[recipes.Length];

            for (int i = 0; i < recipes.Length; i++)
                books[i] = new Book
                {
                    Sheet = Reel(raw, recipes[i], wide, tallPx, comet),
                    Wide = wide,
                    Tall = tallPx,
                    Frames = frames,
                };

            return books;
        }

        /// <summary>
        /// Squeezes every frame toward its own middle column, in place.
        ///
        /// <para>
        /// <b>About the frame's centre line, which is where a comet's head already is</b> - the
        /// rig flies the effect straight up the middle, so the squeeze narrows the bolt without
        /// moving it off the axis the view rotates it about.
        /// </para>
        /// <para>
        /// Sampled linearly and left transparent past the edges: at a slim below one the read runs
        /// <em>wider</em> than the frame, so the outermost columns of the result ask for pixels
        /// that were never drawn, and those are empty rather than clamped - a clamp would smear
        /// the effect's last column out to the frame's edge, which on a soft halo is a visible
        /// bar.
        /// </para>
        /// </summary>
        static void Slimmer(Color[][] raw, int wide, int tall, float slim)
        {
            if (raw == null || wide <= 0 || slim <= 0f) return;

            float centre = (wide - 1) * .5f;
            var row = new Color[wide];

            for (int f = 0; f < raw.Length; f++)
            {
                var pixels = raw[f];

                // **Exactly, and it says so out loud.** A tolerant `<` here is what let the first
                // cut read a supersampled buffer at the final frame's stride and report nothing
                // wrong; a squeeze that silently does not happen is worse than one that throws.
                if (pixels == null || pixels.Length != wide * tall)
                {
                    Debug.LogError($"[siege shots] a frame is {pixels?.Length ?? 0} pixels where " +
                                   $"{wide}x{tall} was expected - not slimmed.");
                    return;
                }

                for (int y = 0; y < tall; y++)
                {
                    int at = y * wide;

                    for (int x = 0; x < wide; x++)
                    {
                        float from = centre + (x - centre) / slim;
                        int left = Mathf.FloorToInt(from);
                        float part = from - left;

                        row[x] = left < 0 || left + 1 >= wide
                               ? new Color(0f, 0f, 0f, 0f)
                               : Color.Lerp(pixels[at + left], pixels[at + left + 1], part);
                    }

                    for (int x = 0; x < wide; x++) pixels[at + x] = row[x];
                }
            }
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

            // **Rounded for <see cref="Trailing"/>'s reason, one step further downstream.** This
            // aspect is handed to <see cref="Pixels"/>, which quantises it to a multiple of
            // sixteen — so almost every value maps to the same width and a few sit exactly on a
            // boundary. For those, a last-bit difference in a bounds query is not a fraction of a
            // level of drift: it is a **different texture size**, which `CompareAll` can only
            // report as 255/255 and which reads as the art having been replaced. Measured on the
            // gravemaw's ground wash, which flipped width between two bakes in the same call.
            // **Thousandths for <see cref="Trailing"/>'s reason, and this is where that reason was
            // learned**: rounding here to hundredths moved eight frost reels across a boundary in
            // <see cref="Pixels"/> and made them 14 % longer on the board.
            return Mathf.Round(Mathf.Clamp(tall / wide, leanest, longest) * 1000f) / 1000f;
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
        /// <param name="tilt">
        /// Degrees the camera is pitched down by, orbiting the point it is aimed at.
        ///
        /// <para>
        /// <b>Nought for everything that flies, and the one thing that does not needs it.</b> A
        /// projectile is billboards and trails: it looks the same from any angle, so the rig has
        /// always looked straight along Z and there was never a reason to ask. A *strike* is not —
        /// this pack draws its ground crack, its splat, its shockwave and its rings as horizontal
        /// quads lying on the floor, so a camera level with them sees every one of them edge-on
        /// and each collapses to a hairline. That is most of what the vendor's own picture is, and
        /// this bake was rendering none of it: the reel came out as a bolt and a small star, which
        /// is the geometry of a lightning strike with the *strike* left out.
        /// </para>
        /// </summary>
        static Color[][] Roll(Transform stage, Camera cam, RenderTexture rt, Texture2D pixels,
                              GameObject prefab, int frames, float seconds, float speed, float warm,
                              float head, float height, float tilt)
        {
            float back = height * .5f / Mathf.Tan(FieldOfView * .5f * Mathf.Deg2Rad);
            var look = Quaternion.Euler(tilt, 0f, 0f);

            var shot = Spawn(stage, prefab);
            var roots = Roots(shot);
            var raw = new Color[frames][];

            // The effect's own clock, which every frame of this loop advances and every render
            // reads. See <see cref="Clock"/> for why it exists at all.
            float clock = 0f;

            HoldClock();

            try
            {
                Start(roots);
                Advance(shot, roots, warm, speed);
                clock += warm;

                for (int f = 0; f < frames; f++)
                {
                    // The camera rides with the head, and the head sits `head` of the way up the
                    // frame — so the trail has the whole rest of the frame to lie in.
                    //
                    // **It orbits what it is aimed at rather than being rotated where it stands**,
                    // so a tilt changes the angle the effect is seen from and never what is in
                    // shot. At nought this is the straight-on rig to the pixel.
                    Vector3 at = shot.transform.position;
                    var aim = new Vector3(at.x, at.y - (head - .5f) * height, at.z);

                    cam.transform.position = aim - look * Vector3.forward * back;
                    cam.transform.rotation = look;

                    Clock(clock);
                    cam.Render();
                    raw[f] = ReadBack(rt, pixels);

                    Advance(shot, roots, seconds / frames, speed);
                    clock += seconds / frames;
                }
            }
            finally { Object.DestroyImmediate(shot); ReleaseClock(); }

            return raw;
        }

        /// <summary>
        /// Pins the shader clock to the effect's <b>own</b> elapsed time for one render, and hands
        /// it back to the engine when the bake is done.
        ///
        /// <para>
        /// <b>Fixing every particle seed was necessary and was not sufficient</b>, and the gap
        /// between those two is why <see cref="Verify"/> had quietly stopped meaning anything.
        /// <see cref="Spawn"/> sets <c>useAutoRandomSeed = false</c> on every system, so the
        /// particles are identical run to run — but four of this pack's seven shader graphs
        /// (<c>MasterScroll01/02/03</c> and <c>MasterScrollManual</c>) scroll their textures off a
        /// <b>Time</b> node, and in the Editor that is the wall clock since the Editor started.
        /// So every bake rendered the same particles through a texture at a different scroll
        /// phase, and a scroll moves *everywhere at once*: two bakes four minutes apart differed
        /// by a mean of <b>12.8 levels</b> on a ward's bolt, against <see cref="CompareAll"/>'s
        /// tolerance of six. The check was reporting correct art as drifted, and there is no
        /// tolerance that separates "the scroll is at a different phase" from "somebody changed
        /// the prefab" — they are the same picture-wide difference.
        /// </para>
        /// <para>
        /// <b>Driven by the simulated clock rather than frozen at nought</b>, which is the whole
        /// point: a frozen scroll would be reproducible and would also stop the texture moving
        /// across the reel, which is half of what makes these effects read as flowing. Advancing
        /// it by the same step the particles advance by makes the scroll part of the bake, and
        /// makes it identical on every machine on every run.
        /// </para>
        /// <para>
        /// <b>A negative time hands the clock back</b>, and the <c>finally</c> is not tidiness:
        /// <c>_Time</c> is a global, so a bake that threw with it pinned would leave every
        /// scrolling material in the Editor frozen at whatever instant it died on — a Scene view
        /// that has stopped animating with no error to explain it.
        /// </para>
        /// </summary>
        static void Clock(float t)
        {
            // Unity's own layout, which these graphs' Time node reads through: (t/20, t, t*2, t*3)
            // for `_Time`, and the sine and cosine of t in the two companions. All four of this
            // pack's scrolling graphs read the plain **Time** slot, which is `_Time.y`; the other
            // two are set so a graph that ever reaches for sine or cosine time is pinned as well.
            Shader.SetGlobalVector(TimeId, new Vector4(t / 20f, t, t * 2f, t * 3f));
            Shader.SetGlobalVector(SinId, new Vector4(Mathf.Sin(t / 8f), Mathf.Sin(t / 4f),
                                                      Mathf.Sin(t / 2f), Mathf.Sin(t)));
            Shader.SetGlobalVector(CosId, new Vector4(Mathf.Cos(t / 8f), Mathf.Cos(t / 4f),
                                                      Mathf.Cos(t / 2f), Mathf.Cos(t)));
        }

        static Vector4 _wasTime, _wasSin, _wasCos;
        static bool _clockHeld;

        /// <summary>
        /// Remembers the shader clock so <see cref="ReleaseClock"/> can put it back.
        ///
        /// <para>
        /// <b>This exists because the first version asserted something false and the assertion was
        /// the exact hazard it was written to warn about.</b> <see cref="Clock"/> used to "release"
        /// by writing nought, under a comment claiming the engine rewrites `_Time` before every
        /// frame it renders so there was nothing worth restoring. Measured instead of argued:
        /// setting `_Time` to a sentinel and reading it back <b>on a later frame</b> returns the
        /// sentinel. Unity does not put that global back. So the bake was not releasing the clock,
        /// it was pinning every scrolling material in the project at time nought and walking away
        /// — which is a Scene view that has quietly stopped animating, with no error, exactly the
        /// failure the old comment described in the course of dismissing it.
        /// </para>
        /// <para>
        /// <b>Save and restore, rather than a value this file invents.</b> Whatever was there
        /// before the bake is the only honest answer, because nothing here knows what else has
        /// written to it or what reads it next. Held across nested <see cref="Roll"/> calls by the
        /// latch, so the two passes of one capture save once and restore once.
        /// </para>
        /// </summary>
        static void HoldClock()
        {
            if (_clockHeld) return;

            _wasTime = Shader.GetGlobalVector(TimeId);
            _wasSin = Shader.GetGlobalVector(SinId);
            _wasCos = Shader.GetGlobalVector(CosId);
            _clockHeld = true;
        }

        /// <summary>Puts the shader clock back. See <see cref="HoldClock"/>.</summary>
        static void ReleaseClock()
        {
            if (!_clockHeld) return;

            Shader.SetGlobalVector(TimeId, _wasTime);
            Shader.SetGlobalVector(SinId, _wasSin);
            Shader.SetGlobalVector(CosId, _wasCos);
            _clockHeld = false;
        }

        static readonly int TimeId = Shader.PropertyToID("_Time");
        static readonly int SinId = Shader.PropertyToID("_SinTime");
        static readonly int CosId = Shader.PropertyToID("_CosTime");

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
        static Texture2D Reel(Color[][] raw, Recipe recipe, int wide, int tall, bool comet)
        {
            int bigW = wide * Super;
            int frames = raw.Length;

            // Hoisted out of the pixel loop: this runs tens of millions of times per bake.
            Color tint = recipe.Hue;
            float toward = recipe.Toward;
            float mostWhite = recipe.White;
            float floor = recipe.Floor;
            float bleach = recipe.Bleach;
            float lift = recipe.Lift > 0f ? recipe.Lift : Lift;

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

                    // `cover` is the largest channel, so dividing by it leaves a pure
                    // chromaticity and puts every scrap of the effect's brightness in the alpha.
                    // What the grade then decides is the *hue* of that chromaticity, which is the
                    // one thing a run-time tint could never have supplied (see `BakeTurret`).
                    dst[i] = Grade(new Color(c.r / cover, c.g / cover, c.b / cover, 1f),
                                   tint, toward, mostWhite, floor, bleach);

                    dst[i].a = cover;

                    if (cover > peak) peak = cover;
                }
                keyed[f] = dst;
            }

            float gain = peak > .02f ? 1f / peak : 1f;

            var sheet = new Texture2D(wide, tall * frames, TextureFormat.RGBA32, false, false);
            var outPix = new Color32[wide * tall * frames];

            int cells = wide * tall;
            var flatR = new float[cells];
            var flatG = new float[cells];
            var flatB = new float[cells];
            var flatA = new float[cells];

            // What a bloom is lit in. Its own colour rather than the pixels it came from, which is
            // both far cheaper - one channel to blur instead of three - and right: everything in
            // the frame has already been graded onto this hue, so the light it spills is that hue,
            // and a hot glow is that hue on its way to white.
            var glowTint = Color.Lerp(tint, Color.white, .34f);

            // The wide half of the halo, when the recipe asks for one of its own. Unset it is the
            // same colour as the tight half, which is the single-coloured bloom every other reel
            // here carries.
            bool twoTone = recipe.Halo.a > 0f;
            var hazeTint = twoTone ? recipe.Halo : glowTint;

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

                        int at = y * wide + x;
                        flatA[at] = Mathf.Clamp01(a / (Super * Super) * gain);
                        flatR[at] = a > 1e-5f ? Mathf.Clamp01(r / a) : 0f;
                        flatG[at] = a > 1e-5f ? Mathf.Clamp01(g / a) : 0f;
                        flatB[at] = a > 1e-5f ? Mathf.Clamp01(b / a) : 0f;
                    }

                float[] glowNear = null, glowFar = null;
                if (recipe.Bloom > 0f) Glow(flatA, wide, tall, out glowNear, out glowFar);

                for (int y = 0; y < tall; y++)
                    for (int x = 0; x < wide; x++)
                    {
                        int at = y * wide + x;

                        // The tail runs off the bottom of a comet's frame, so it is thinned out
                        // over the last of it: a hard edge there is a line drawn across the sky.
                        float ramp = comet
                            ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, tall * TailFade, y))
                            : 1f;

                        float alpha = Mathf.Pow(flatA[at], lift);
                        float cr = flatR[at], cg = flatG[at], cb = flatB[at];

                        // **Blown out where the light is densest** — the exposure the bare camera
                        // never applied. Before the halo, so the glow is added on top of a core
                        // that is already white rather than mixed into one that never gets there.
                        if (recipe.Hot > 0f)
                        {
                            float blown = Mathf.SmoothStep(
                                0f, 1f, Mathf.InverseLerp(recipe.HotFrom, 1f, flatA[at]))
                                * recipe.Hot;

                            cr = Mathf.Lerp(cr, 1f, blown);
                            cg = Mathf.Lerp(cg, 1f, blown);
                            cb = Mathf.Lerp(cb, 1f, blown);
                        }

                        if (glowNear != null && glowNear[at] + glowFar[at] > .002f)
                        {
                            // **Added as light, which means in emission and not in colour.** What
                            // an effect contributes to the screen is rgb times alpha, so a halo is
                            // added there and the pair separated again afterwards - adding it to
                            // the colour instead would brighten a transparent pixel, which draws
                            // nothing, and adding it to the alpha alone would make a *darker* halo
                            // rather than a lit one.
                            float near = glowNear[at] * recipe.Bloom;
                            float airy = glowFar[at] * recipe.Bloom;
                            float lit = near + airy;

                            // Each scale carries its own colour into the emission, so the wash in
                            // the air is warm where the rim on the object's own edge is hot. Summed
                            // with one tint this is the single-coloured halo exactly.
                            float er = cr * alpha + glowTint.r * near + hazeTint.r * airy;
                            float eg = cg * alpha + glowTint.g * near + hazeTint.g * airy;
                            float eb = cb * alpha + glowTint.b * near + hazeTint.b * airy;

                            alpha = Mathf.Clamp01(alpha + lit * GlowOpacity);

                            if (alpha > 1e-4f)
                            {
                                cr = Mathf.Clamp01(er / alpha);
                                cg = Mathf.Clamp01(eg / alpha);
                                cb = Mathf.Clamp01(eb / alpha);
                            }
                        }

                        alpha *= ramp;

                        outPix[(f * tall + y) * wide + x] = alpha <= 1e-4f
                            ? new Color32(255, 255, 255, 0)
                            : new Color32((byte)(cr * 255f), (byte)(cg * 255f), (byte)(cb * 255f),
                                          (byte)(Mathf.Clamp01(alpha) * 255f));
                    }
            }

            sheet.SetPixels32(outPix);
            sheet.Apply(false);
            return sheet;
        }

        /// <summary>
        /// How much of a bloom's brightness becomes opacity.
        ///
        /// <b>A halo has to be drawn to be seen.</b> These reels are composited with ordinary alpha
        /// blending, so light that only changes a transparent pixel's colour changes nothing; the
        /// glow earns its own alpha.
        /// </summary>
        const float GlowOpacity = .82f;

        /// <summary>Where a pixel starts counting as bright enough to spill light.</summary>
        const float GlowFloor = .38f;

        /// <summary>
        /// The faintest glow that is kept at all.
        ///
        /// <b>A halo has to end somewhere.</b> A blur has no zero — it puts a thousandth of an
        /// alpha over the whole frame — and a thousandth of an alpha across a rectangle is still a
        /// rectangle. Subtracting a floor is what gives the light an edge to fade to.
        /// </summary>
        const float GlowCut = .035f;

        /// <summary>
        /// The bright parts of a frame, blurred at two scales - a renderer's bloom, done here.
        ///
        /// <para>
        /// <b>One channel and two scales, because that is what makes it affordable.</b> The colour
        /// is the recipe's own hue, so only the *shape* of the light has to be blurred; and a bloom
        /// is low-frequency, so the wide scale is computed on a buffer an eighth the size and
        /// stretched back. Blurring three channels at full resolution across two hundred and
        /// twenty-eight reels would be minutes of bake for a picture nobody could tell apart.
        /// </para>
        /// <para>
        /// <b>Two scales rather than one</b>, which is what separates a bloom from a smudge: a
        /// tight one puts a rim of light on the object's own edge and a wide one puts a wash in the
        /// air around it, and a single radius can only ever be one of those.
        /// </para>
        /// <para>
        /// <b>And they are handed back separately, because they are allowed to be different
        /// colours.</b> Light spreading through air reddens as it goes — which is why every
        /// photograph of something incandescent has a white middle inside a warm haze, and why a
        /// bloom drawn in one colour reads as a coloured shape rather than as something burning.
        /// A caller that does not care adds them and gets exactly what a single array gave it;
        /// <see cref="Recipe.Halo"/> is the one that does.
        /// </para>
        /// </summary>
        static void Glow(float[] alpha, int wide, int tall, out float[] tight, out float[] airy)
        {
            int cells = wide * tall;
            var bright = new float[cells];

            for (int i = 0; i < cells; i++)
                bright[i] = Mathf.Max(0f, alpha[i] - GlowFloor) / (1f - GlowFloor);

            int nw, nh, fw, fh;

            // **The wide scale is shrunk by a factor the frame can afford.** A muzzle flash is 128
            // by 192, so an eighth of it is sixteen across — and three box passes of radius three
            // on a buffer that size reach right across the picture, which is a wash rather than a
            // halo. The divisor is held so the small buffer never falls under about forty across.
            int far8 = Mathf.Clamp(Mathf.Min(wide, tall) / 40, 2, 8);

            var near = Blur(Shrink(bright, wide, tall, 2, out nw, out nh), nw, nh, 2);
            var far = Blur(Shrink(bright, wide, tall, far8, out fw, out fh), fw, fh, 3);

            tight = new float[cells];
            airy = new float[cells];

            // The cut is taken here rather than by the caller so that a caller adding the two back
            // together gets the single array this used to return, to the last bit. It is shared
            // out in proportion, because it is one floor under one halo rather than one under each.
            for (int y = 0; y < tall; y++)
                for (int x = 0; x < wide; x++)
                {
                    int at = y * wide + x;

                    float n = Bilinear(near, nw, nh, x / 2f, y / 2f) * .58f;
                    float f = Bilinear(far, fw, fh, (float)x / far8, (float)y / far8) * .52f;
                    float sum = n + f;

                    if (sum <= GlowCut) { tight[at] = 0f; airy[at] = 0f; continue; }

                    float keep = (sum - GlowCut) / sum;
                    tight[at] = n * keep;
                    airy[at] = f * keep;
                }
        }

        /// <summary>Averages a buffer down by a whole factor.</summary>
        static float[] Shrink(float[] src, int wide, int tall, int by, out int outW, out int outH)
        {
            outW = Mathf.Max(1, wide / by);
            outH = Mathf.Max(1, tall / by);

            var dst = new float[outW * outH];

            for (int y = 0; y < outH; y++)
                for (int x = 0; x < outW; x++)
                {
                    float sum = 0f;
                    int n = 0;

                    for (int sy = 0; sy < by; sy++)
                        for (int sx = 0; sx < by; sx++)
                        {
                            int px = x * by + sx, py = y * by + sy;
                            if (px >= wide || py >= tall) continue;
                            sum += src[py * wide + px];
                            n++;
                        }

                    dst[y * outW + x] = n > 0 ? sum / n : 0f;
                }

            return dst;
        }

        /// <summary>
        /// Three separable box passes, which is a Gaussian to within what anybody can see and is
        /// linear in the radius rather than square.
        ///
        /// <para>
        /// <b>Divided by the whole kernel and never by how much of it was in bounds, which is the
        /// difference between a bloom and a box.</b> Averaging only the samples that exist treats
        /// the edge of the frame as a mirror: a pixel one step in is divided by half the kernel, so
        /// its light is doubled, and three passes of that pile a bright rim against all four
        /// edges. Baked into a reel that reads as a faintly glowing rectangle around the effect —
        /// which is exactly what it did, most visibly on the small frames where the kernel is a
        /// large part of the picture. Treating everything outside as dark is both the physically
        /// honest answer and the one that lets a halo fade out.
        /// </para>
        /// </summary>
        static float[] Blur(float[] src, int wide, int tall, int radius)
        {
            var a = src;
            var b = new float[src.Length];
            float span = radius * 2 + 1;

            for (int pass = 0; pass < 3; pass++)
            {
                for (int y = 0; y < tall; y++)
                    for (int x = 0; x < wide; x++)
                    {
                        float sum = 0f;

                        for (int k = -radius; k <= radius; k++)
                        {
                            int at = x + k;
                            if (at < 0 || at >= wide) continue;
                            sum += a[y * wide + at];
                        }
                        b[y * wide + x] = sum / span;
                    }

                for (int y = 0; y < tall; y++)
                    for (int x = 0; x < wide; x++)
                    {
                        float sum = 0f;

                        for (int k = -radius; k <= radius; k++)
                        {
                            int at = y + k;
                            if (at < 0 || at >= tall) continue;
                            sum += b[at * wide + x];
                        }
                        a[y * wide + x] = sum / span;
                    }
            }

            return a;
        }

        /// <summary>Bilinear read, so a bloom computed small comes back smooth rather than blocky.</summary>
        static float Bilinear(float[] src, int wide, int tall, float x, float y)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, wide - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(y), 0, tall - 1);
            int x1 = Mathf.Min(x0 + 1, wide - 1);
            int y1 = Mathf.Min(y0 + 1, tall - 1);

            float fx = Mathf.Clamp01(x - x0), fy = Mathf.Clamp01(y - y0);

            float top = Mathf.Lerp(src[y0 * wide + x0], src[y0 * wide + x1], fx);
            float bot = Mathf.Lerp(src[y1 * wide + x0], src[y1 * wide + x1], fx);
            return Mathf.Lerp(top, bot, fy);
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

        /// <summary>
        /// The saturation floor the elemental four are graded with — see <c>Recipe.Floor</c>.
        ///
        /// <b>Named and kept exactly where it was</b>, because these four have shipped for
        /// chapters and <see cref="Verify"/> holds what is on disk to what this tool bakes: the
        /// number moving would read as the art having drifted.
        /// </summary>
        const float Muted = .55f;

        static Color Grade(Color lit, Color hue, float toward, float mostWhite, float floor,
                           float bleach)
        {
            Color.RGBToHSV(lit, out _, out float s, out float v);
            Color.RGBToHSV(hue, out float h, out _, out _);

            float white = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.86f, 1f, v * (1f - s)))
                          * mostWhite;

            float sat = Mathf.Clamp01(s * (1f - floor) + floor) * (1f - white)
                      * (1f - Mathf.Clamp01(bleach));
            var wanted = Color.HSVToRGB(h, sat, Mathf.Clamp01(v * 1.06f + .04f));

            return Color.Lerp(lit, wanted, toward * (1f - white));
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
