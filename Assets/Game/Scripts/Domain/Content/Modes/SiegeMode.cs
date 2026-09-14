using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Modes;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// The rules a Thornwatch level is played by: the field, the line and what is coming.
    ///
    /// <para>
    /// <b>It is a <see cref="ProtoLevelRules"/> so that the whole run comes for free</b> — the
    /// heart, the stake, the record, the chests, the streak, what a restart costs, which latch
    /// holds the board while a lesson is up — which is exactly invariant 20b's bargain: bring
    /// your own board, share the run. What it does <em>not</em> share is the search:
    /// <see cref="Opening"/> answers null, because a hill with raiders walking down it while
    /// nobody is touching the board has no state graph to walk. Par is arithmetic
    /// (<see cref="SiegeTuning.Par"/>) and is the honest cost of a mode that runs on a clock.
    /// </para>
    /// </summary>
    public sealed class SiegeRules : ProtoLevelRules
    {
        public readonly SiegeLayout Layout;

        public SiegeRules(SiegeLayout layout) : base(0) => Layout = layout;

        public override GameMode Mode => GameMode.Siege;
        public override ProtoGrid Grid => Layout.Grid;
        /// <summary>
        /// A board for a run, with the turrets the player has stood on the line.
        ///
        /// <para>
        /// <b>The loadout is read here and nowhere else.</b> Every content gate, every offline
        /// mirror and <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c> build a board directly
        /// (<c>SiegeBoard.Build(layout)</c>), which stands the <em>starter</em> line — so a level
        /// is proved holdable with the weakest line a player could bring, and a validator can
        /// never come to depend on whatever loadout the developer running it happens to have.
        /// </para>
        /// </summary>
        public override IProtoBoard Fresh() => SiegeBoard.Build(Layout, Wards.WardLoadout.Line);

        /// <summary>
        /// Nothing to search.
        ///
        /// <para>
        /// <b>Null rather than a position, and <c>ProtoSearch</c> already reads it that way</b>
        /// (par nought, proved). Every other mode on this shape is a DAG whose depth is moves
        /// spent, so par is the first layer that wins; a siege advances on a clock whether or not
        /// a gem is touched, and its field refills, so there is no fixed future and no graph.
        /// Saying so out loud here is the point — the alternative was a position that pretended to
        /// be searchable and answered a par nobody could have earned.
        /// </para>
        /// </summary>
        public override ProtoPosition Opening() => null;

        /// <summary>How much there is to work with: the field, in cells.</summary>
        public override int Room => Layout.Grid.Count;
    }

    /// <summary>
    /// <b>Thornwatch.</b> The raiders are coming down the hill at the grove's ward line. Match
    /// gems and the colour you matched fuels the ward of that colour, which looses bolts until its
    /// fuel fades. A bolt is worth double against a raider of its own colour. Let them through and
    /// they break the wards; break every ward and the grove is taken.
    ///
    /// <para>
    /// <b>It is the genre's own board bolted to the genre's other loop, and the twist is what a
    /// match is <em>for</em>.</b> Nothing on the field is a goal — the goals are on the hill — so
    /// a match is never worth anything by itself and is only ever worth the colour it was. That is
    /// what makes the decision continuous rather than per-move: fuel fades, so a colour banked is
    /// a colour wasted, and the question is always which ward wants feeding <em>now</em> rather
    /// than which match is biggest.
    /// </para>
    /// <para>
    /// <b>What it cost the save file, the wire and the server: nothing</b> (invariant 20a). A
    /// Thornwatch level is an ordinary level with its own permanent id, so its record, its stars,
    /// its rewards and its merge are the ones every glade already has — which is also what makes
    /// it cheap to take back out, exactly as seven modes before it were.
    /// </para>
    /// <para>
    /// <b>And what it costs that no other mode does: par is not a proof.</b> Every graded number
    /// here still derives from par and both star lines are still the same multiples every mode
    /// uses, but par is arithmetic over what the level sends rather than the depth of a search. It
    /// is a genuine floor — no run of fewer matches could have destroyed what is coming — so it
    /// errs toward three stars being reachable, which is the direction invariant 22 says to err
    /// in. It is the one thing about this mode to judge by playing before a second level is
    /// authored.
    /// </para>
    /// </summary>
    public sealed class SiegeMode : LevelMode
    {
        public override GameMode Mode => GameMode.Siege;

        public override bool Claims(LevelDto dto) => dto.siege != null && dto.siege.IsAuthored;

        /// <summary>
        /// The field, the line, the hill and the cast.
        ///
        /// <para>
        /// <b>Its own folder rather than Emberforge's</b>, though the jewels are cut from the same
        /// licensed pack. An address two chapters ask for belongs to neither
        /// (<c>AddressableAddresses.ChapterOwnership</c>), so sharing would move Emberforge's wall
        /// out of its chapter scope and into the global group, resident for the whole session on
        /// every device (invariant 7b) — and it would weld two modes together in a project that
        /// withdraws them often enough for that to matter.
        /// </para>
        /// </summary>
        static readonly AssetRequest[] Cast =
        {
            AssetRequest.Sprite(AssetManifest.SiegeArt("gem_r")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("gem_g")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("gem_b")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("gem_y")),

            // The cog: the one cell of the field that is not a jewel, and the only thing here a
            // ward can be upgraded with (`SiegeLayout.Cog`).
            AssetRequest.Sprite(AssetManifest.SiegeArt("gem_cog")),

            // **The charms** (`SiegeCharm`), and every one of the three is a **gem of its own**.
            //
            // **That is a correction and the reason is worth keeping.** A lance and a stormglass
            // used to be *marks* - a white glyph drawn over one of the four jewels - on the
            // argument that the colour underneath has to keep reading, which is true and is
            // answered by cutting a *different jewel in the same colour* rather than by leaving
            // the jewel alone and printing on it. The owner's verdict was one sentence: the charms
            // were the existing gems with an icon put on them. It is also the weaker reading on
            // its own terms - a mark is forty pixels of drawing over a saturated stone, where a
            // silhouette is what the eye separates at a glance on a board that also has a hill
            // walking down it (invariant 34f). So a lance is a stellated star and a stormglass a
            // vortex orb, each hue-rotated into all four gem colours, and a prism is what it
            // always was: the one gem here that is not a colour at all.
            //
            // The halo behind them and the beam a lance draws are white and tinted where they are
            // used, for the muzzle flash's reason: one reel serves four colours and four painted
            // copies are four chances for one of them to stop matching `Pal`.
            //
            // **Loaded with the rest of the field rather than scoped to the chapters that deal
            // them**, which is the same call the cog's art gets: the set a level deals is a
            // per-level fact rather than a per-chapter one, and a scope that changed mid-chapter
            // would be a screen repainting itself for a handful of sprites (invariant 7b's rule is
            // about what is *big*).
            AssetRequest.Sprite(AssetManifest.SiegeArt("gem_prism")),

            AssetRequest.Sprite(AssetManifest.SiegeArt("gem_lance_r")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("gem_lance_g")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("gem_lance_b")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("gem_lance_y")),

            AssetRequest.Sprite(AssetManifest.SiegeArt("gem_storm_r")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("gem_storm_g")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("gem_storm_b")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("gem_storm_y")),

            AssetRequest.Sprite(AssetManifest.SiegeArt("charm_ring")),

            // **The beam is a reel now and not a bar.** A still gradient stretched across the row
            // has no event in it, so what a lance read as was a highlighter line appearing over
            // gems that were going anyway; ten frames of a crawling filament is light under
            // pressure. See `make_siege_art.beam`.
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("beam")),

            // **And a second beam, because a lance's thread and a stormglass's laser are
            // different materials.** `beam` is a hot wandering filament with a long soft tail,
            // which is what reads when it is stretched across a whole row at two thirds of a
            // cell; a laser is a *bar* - flat at full alpha across its middle third with a short
            // shoulder - so that stretched to any thickness it stays a solid band. Scaling one to
            // do the other's job was measured and does not work: the thread's brightness sits in
            // about a sixth of its height whatever that height is, so at two and a half cells it
            // was still a coloured hair. See `make_siege_art.laser`.
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("laser")),

            // **What a charm goes off in, one reel per gem colour.** Its own bake rather than the
            // ward impact it used to borrow: that one is framed small because a lit line lands
            // eighteen a second, and a charm drew it at four and a half cells - the biggest moment
            // on the field as a small reel blown up two and a half times. See
            // `SiegeShotBake.Charms`.
            AssetRequest.SpriteSet(AssetManifest.SiegeFx("charm_blast_r")),
            AssetRequest.SpriteSet(AssetManifest.SiegeFx("charm_blast_g")),
            AssetRequest.SpriteSet(AssetManifest.SiegeFx("charm_blast_b")),
            AssetRequest.SpriteSet(AssetManifest.SiegeFx("charm_blast_y")),

            // The overcharge glyph on a turret's chassis. Resident with the rest of the line's
            // furniture rather than scoped, because every siege can bank one.
            AssetRequest.Sprite(AssetManifest.SiegeArt("charge")),

            // **The ward line: five tiers times four colours, and the rank badge.** A ward carries
            // its rank in its silhouette and its colour in its hue, so both are baked (see
            // `Tools/make_siege_art.py`) and nothing here is tinted - which is the correction
            // invariant 37l records: `Image.color` is a multiply, so it can only ever darken, and a
            // turret is the one thing on this board that has to read as *lit*.
            //
            // Written out one literal at a time rather than built from a loop, which is invariant
            // 6's rule for loc keys read across to art: `Tools/verify/artnames.py` reads the name
            // that is actually passed, so a name assembled a call away from the lookup is a name
            // nothing checks - and this mode has already paid for that once.
            // **The line is no longer a ladder of five turrets, it is whichever four the player
            // chose** - so what is resident here is the wreck and the badge, and nothing else.
            // The turrets themselves arrive in a hold of four (`SiegeScreen`'s own,
            // invariant 7b), because the roster is twenty models in four colours and a run draws
            // four of them; the *rank* is the badge's own colour (`SiegeView.RankTint`), which
            // costs no art at all and is the one corner of a ward the field's plate does not
            // cover (invariant 37y).
            AssetRequest.Sprite(AssetManifest.SiegeArt("ward_dead")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("crest")),

            AssetRequest.Sprite(AssetManifest.SiegeArt("bullet")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("socket")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("rampart")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("plate")),

            // The first rung's ground. The other nine are added per chapter by <see cref="ArtFor"/>
            // and this one is here so a siege asked for its art without a chapter still has a
            // floor - an Image with a null sprite is a white rectangle rather than a blank (7b).
            AssetRequest.Sprite(AssetManifest.SiegeArt("hill1")),

            // **Twelve bodies rather than four, and none of them is tinted here.** A raider used
            // to be one of three creeper models or the brute, multiplied at run time by 62% toward
            // its colour — which `Image.color` can only do by *darkening*, so what four packs had
            // drawn came out as four silhouettes of one value. The colour is baked now
            // (`make_siege_art.RAIDER_SET`), which lets the body say it too: one model per colour
            // per kind, so a player who cannot separate two hues can still separate a fly from a
            // horned beetle.
            //
            // **The two that work on the field, and they are one set whatever the chapter.** A
            // creeper is scenery and is re-cast per chapter; a weaver and a thief are rules, and a
            // player who has learned that the crawling beetle locks cells must not have to learn
            // it again next chapter because the beetle has become something else.
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("weaver_r")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("weaver_g")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("weaver_b")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("weaver_y")),

            AssetRequest.SpriteSet(AssetManifest.SiegeArt("thief_r")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("thief_g")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("thief_b")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("thief_y")),

            // What a weaver leaves on the field, and what a thief leaves in place of a gem.
            AssetRequest.Sprite(AssetManifest.SiegeArt("web")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("sack")),

            // **The storm, which every siege loads whether or not the player holds one.** A
            // utility is account-wide and can be used on any rung, so it is not a fact about a
            // chapter the way a boss is - there is no level it could be scoped to. **One reel
            // rather than three**: the bought pack draws the whole strike - bolt, flash, ground
            // crack - as a single effect, so there is nothing to fire and nothing to fly.
            AssetRequest.SpriteSet(AssetManifest.SiegeFx("storm")),

            AssetRequest.SpriteSet(AssetManifest.SiegeFx("boom_fire")),
            AssetRequest.SpriteSet(AssetManifest.SiegeFx("boom_smoke")),

            // What each ward fires, as three parts: the flash it lets go with, the thing that
            // crosses the hill, and what that does when it arrives. Baked out of the bought
            // projectile pack by `SiegeShotBake`, one element per colour — a fireball, a venom
            // dart, an icicle and a lightning bolt — so a bolt is told apart by silhouette rather
            // than by hue alone, which is the argument the four turret models already carry.
            //
            // **Their colour is baked and none of them is tinted here**, unlike the two white
            // reels below: the bake reads `Pal` itself, so a retune moves the gems, the turrets
            // and these together and cannot leave them disagreeing (invariant 37f).
            AssetRequest.SpriteSet(AssetManifest.SiegeFx("shot_r")),
            AssetRequest.SpriteSet(AssetManifest.SiegeFx("shot_g")),
            AssetRequest.SpriteSet(AssetManifest.SiegeFx("shot_b")),
            AssetRequest.SpriteSet(AssetManifest.SiegeFx("shot_y")),

            AssetRequest.SpriteSet(AssetManifest.SiegeFx("muzzle_r")),
            AssetRequest.SpriteSet(AssetManifest.SiegeFx("muzzle_g")),
            AssetRequest.SpriteSet(AssetManifest.SiegeFx("muzzle_b")),
            AssetRequest.SpriteSet(AssetManifest.SiegeFx("muzzle_y")),

            AssetRequest.SpriteSet(AssetManifest.SiegeFx("hit_r")),
            AssetRequest.SpriteSet(AssetManifest.SiegeFx("hit_g")),
            AssetRequest.SpriteSet(AssetManifest.SiegeFx("hit_b")),
            AssetRequest.SpriteSet(AssetManifest.SiegeFx("hit_y")),

            // Both white, both tinted where they are drawn: a muzzle flash takes the ward's
            // colour and a gem's burst takes the gem's, so one reel serves four.
            AssetRequest.SpriteSet(AssetManifest.SiegeFx("flash")),
            AssetRequest.SpriteSet(AssetManifest.SiegeFx("pop")),
        };

        /// <summary>
        /// One boss, as the three reels its body wears and the three its spell is drawn with.
        ///
        /// <para>
        /// <b>Six flipbooks each, and this mode has four of them</b> — which is the whole reason
        /// this is asked per chapter rather than listed with the rest of the cast. Every siege
        /// level used to load every boss, so a chapter sending one paid for four; twelve body
        /// reels at three hundred pixels and twelve effect reels is most of what this mode weighs.
        /// Invariant 7b's rule is that memory is bounded by what is on the screen rather than by
        /// how much content exists, and a boss is the first thing here where those two differ.
        /// </para>
        /// <para>
        /// <b>Every name is a literal</b>, for invariant 7's reason and for the gate that enforces
        /// it: <c>Tools/verify/artnames.py</c> reads literals off the call site, so a key built
        /// from a kind would be twenty-four names nothing checks.
        /// </para>
        /// </summary>
        static void Bosses(SiegeKind kind, List<AssetRequest> into)
        {
            switch (kind)
            {
                case SiegeKind.Blightcaller:
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeArt("blight")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeArt("blight_cast")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeFx("hex")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeFx("hex_muzzle")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeFx("hex_hit")));
                    break;

                case SiegeKind.Boss:
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeArt("boss")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeArt("boss_cast")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeFx("spell")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeFx("spell_muzzle")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeFx("spell_hit")));
                    break;

                case SiegeKind.Warbringer:
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeArt("bringer")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeArt("bringer_cast")));
                    // **Two of its three reels, because a roar is thrown at nothing.** It has no
                    // flight, so `roar` is baked and never scoped in - see `SiegeView.Roar`, which
                    // draws the impact upright at the boss and the muzzle flat over the ground.
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeFx("roar_muzzle")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeFx("roar_hit")));
                    break;

                case SiegeKind.Overlord:
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeArt("over")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeArt("over_cast")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeFx("omen")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeFx("omen_muzzle")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeFx("omen_hit")));
                    break;

                // **Two of its three reels, for the warbringer's reason**: a devour is thrown at
                // the ground the player has been killing over rather than at a ward, so it has no
                // flight and nothing crosses the hill.
                case SiegeKind.Gravemaw:
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeArt("maw")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeArt("maw_cast")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeFx("roar_muzzle")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeFx("roar_hit")));
                    break;

                // The same two, for the same reason: a raise puts bodies at the top of the hill.
                //
                // **And a third body reel, which this is the one boss in the mode to have.** It
                // is the only one rendered out of 3D (invariant 37bx), so it is the only one that
                // really stands still when it reaches its ground rather than cycling in place —
                // which makes it the only one for which walking on and holding the hill are two
                // pictures. See `SiegeView.WalkReel`. Named here as well as there because a reel
                // `SiegeMode.Art` never asks for is one that ships addressed, grouped, built into
                // a bundle and impossible to load (`SiegeCastTests`, invariant 37at).
                case SiegeKind.Bonecaller:
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeArt("caller")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeArt("caller_cast")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeArt("caller_walk")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeFx("roar_muzzle")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeFx("roar_hit")));
                    break;
            }
        }

        /// <summary>
        /// How many grounds this mode ships, and therefore how far the ladder goes before it
        /// starts again. Ten, because a chapter is ten rungs.
        /// </summary>
        public const int Grounds = 10;

        /// <summary>
        /// The ground a siege is fought over, by the level's <b>place in its chapter</b>.
        ///
        /// <para>
        /// <b>An ordinal rather than a choice</b>, which is invariant 7c's rule and the backdrop's
        /// shape exactly: ten grounds serve every siege chapter that ever ships, so a second one
        /// costs no art at all, and no chapter can be published drawing a floor nobody decided on.
        /// A chapter longer than ten rungs wraps rather than drawing nothing.
        /// </para>
        /// <para>
        /// <b>Ten literal cases rather than a name built from the number</b>, for
        /// <c>Tools/verify/artnames.py</c>: it reads the literals at a lookup's call site, so a key
        /// assembled from an index is ten names nothing checks. It is the shape <see cref="Bosses"/>
        /// already uses for the same reason.
        /// </para>
        /// </summary>
        public static AssetRequest Ground(int place)
        {
            switch (((place % Grounds) + Grounds) % Grounds)
            {
                case 1:  return AssetRequest.Sprite(AssetManifest.SiegeArt("hill2"));
                case 2:  return AssetRequest.Sprite(AssetManifest.SiegeArt("hill3"));
                case 3:  return AssetRequest.Sprite(AssetManifest.SiegeArt("hill4"));
                case 4:  return AssetRequest.Sprite(AssetManifest.SiegeArt("hill5"));
                case 5:  return AssetRequest.Sprite(AssetManifest.SiegeArt("hill6"));
                case 6:  return AssetRequest.Sprite(AssetManifest.SiegeArt("hill7"));
                case 7:  return AssetRequest.Sprite(AssetManifest.SiegeArt("hill8"));
                case 8:  return AssetRequest.Sprite(AssetManifest.SiegeArt("hill9"));
                case 9:  return AssetRequest.Sprite(AssetManifest.SiegeArt("hill10"));
                default: return AssetRequest.Sprite(AssetManifest.SiegeArt("hill1"));
            }
        }

        /// <summary>
        /// The hill, the line, the field and the creepers — everything every siege draws.
        ///
        /// A chapter's bosses and its grounds are added on top by <see cref="ArtFor"/>, because
        /// which of the four bosses a chapter sends, and how many rungs it has, are both facts
        /// about the chapter.
        /// </summary>
        /// <summary>
        /// The insects: the twelve bodies the <b>first</b> chapter draws.
        ///
        /// <para>
        /// <b>Twelve bodies rather than four, and none of them is tinted here.</b> A raider used to
        /// be one of three creeper models or the brute, multiplied at run time by 62% toward its
        /// colour — which <c>Image.color</c> can only do by <em>darkening</em>, so what four packs
        /// had drawn came out as four silhouettes of one value. The colour is baked now
        /// (<c>make_siege_art.RAIDER_SET</c>), which lets the body say it too: one model per colour
        /// per kind, so a player who cannot separate two hues can still separate a fly from a
        /// horned beetle.
        /// </para>
        /// <para>
        /// <b>All twelve are insects, drawn top-down, out of one pack</b> — which is the owner's
        /// call and also the one view this board has: the hill is looked down on and its floor is a
        /// top-down tileset, so the side-view cast that stood here was a mismatch nobody had named.
        /// Light fliers and smooth shells creep, horned beetles are the brutes, and the hard domed
        /// hybrids are the bulwarks, because for an insect plating is a shell rather than a held
        /// shield.
        /// </para>
        /// <para>
        /// <b>Lifted out of <see cref="Cast"/> when the second chapter arrived</b>, and that was a
        /// real fix rather than tidying: while the insects were part of the always-resident set,
        /// the Infinite lane loaded twenty-four bodies to draw twelve. A chapter pays for one cast
        /// now (invariant 7b).
        /// </para>
        /// </summary>
        static readonly AssetRequest[] InsectCast =
        {
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("mon_r")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("mon_g")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("mon_b")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("mon_y")),

            AssetRequest.SpriteSet(AssetManifest.SiegeArt("brute_r")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("brute_g")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("brute_b")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("brute_y")),

            AssetRequest.SpriteSet(AssetManifest.SiegeArt("bulwark_r")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("bulwark_g")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("bulwark_b")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("bulwark_y")),
        };

        /// <summary>
        /// The brood: the twelve bodies the <b>second</b> chapter draws.
        ///
        /// <para>
        /// <b>This is the second cast set invariant 37ar said a second pack would buy.</b> That
        /// entry recorded the price in advance — "one table in <c>make_siege_art.RAIDER_SET</c>,
        /// twelve rows in <c>SiegeMode</c>, and no code" — and the prediction held to the letter.
        /// The owner supplied two monster packs that between them draw fifteen small bodies, so the
        /// set needs no body worn twice.
        /// </para>
        /// <para>
        /// <b>The kind is said by the silhouette, because the colour is already spoken for.</b>
        /// Every raider is hue-rotated onto the colour it answers to (37f), so the pack's own paint
        /// is overwritten and cannot carry the kind. What survives a rotation is shape: plain
        /// smooth bodies creep, the four with <em>arms</em> are the brutes, and a hard crest,
        /// stalks or a banded shell make a bulwark.
        /// </para>
        /// </summary>
        static readonly AssetRequest[] BroodCast =
        {
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("broodMon_r")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("broodMon_g")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("broodMon_b")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("broodMon_y")),

            AssetRequest.SpriteSet(AssetManifest.SiegeArt("broodBrute_r")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("broodBrute_g")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("broodBrute_b")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("broodBrute_y")),

            AssetRequest.SpriteSet(AssetManifest.SiegeArt("broodBulwark_r")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("broodBulwark_g")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("broodBulwark_b")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("broodBulwark_y")),
        };

        /// <summary>
        /// The baked cast: the twelve bodies the <b>Infinite</b> lane draws instead of the
        /// insects.
        ///
        /// <para>
        /// <b>Baked out of 3D rather than bought</b> (<c>SiegeCastBake</c>), which is what this
        /// seam was re-opened for. The insect roster is one pack of fifteen and there is no second
        /// one on this machine — "top-down" in the sprite market nearly always means a
        /// three-quarter RPG view, and true overhead only reads for creatures whose silhouette
        /// <em>is</em> their back (37ar). Rendering a rigged model makes the angle a decision
        /// rather than a purchase, which is the same technique <c>SiegeShotBake</c> already uses
        /// for the bought VFX pack and the one Clash Royale's whole cast is made with.
        /// </para>
        /// <para>
        /// <b>By track rather than by chapter ordinal, and that is a trial rather than a rule.</b>
        /// Invariant 7c's arithmetic is what a cast should hang on once one is settled; hanging it
        /// on the track puts the two one tap apart, so the authored chapter's insects and the baked
        /// cast can be compared without leaving the mode. If the bake is kept this goes back to an
        /// ordinal; if it is not, this row and twelve reels are the whole of what has to be undone.
        /// </para>
        /// </summary>
        static readonly AssetRequest[] SecondCast =
        {
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("kayMon_r")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("kayMon_g")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("kayMon_b")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("kayMon_y")),

            AssetRequest.SpriteSet(AssetManifest.SiegeArt("kayBrute_r")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("kayBrute_g")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("kayBrute_b")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("kayBrute_y")),

            AssetRequest.SpriteSet(AssetManifest.SiegeArt("kayBulwark_r")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("kayBulwark_g")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("kayBulwark_b")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("kayBulwark_y")),
        };

        /// <summary>
        /// The bone cast: the twelve bodies the <b>third</b> chapter draws.
        ///
        /// <para>
        /// <b>Five bodies over twelve slots, which is a smaller budget than either cast before it
        /// and changes what the cast says.</b> The insects and the brood each had fifteen to draw
        /// from, so every kind could have four distinct silhouettes and the shape carried the
        /// colour as well as the kind. This pack draws five, so the kind is carried plainly and
        /// nothing else is pretended: a bare rib cage creeps, a helm over a long weapon is a
        /// brute, and a shield or a closed visor is a bulwark. The colour is still said three
        /// times — the body is hue-rotated, the view tints it and the view rings it (invariant
        /// 37f) — and none of those was ever the silhouette's job.
        /// </para>
        /// <para>
        /// <b>It is the first cast with a second reel per body</b>: see <see cref="BoneSwings"/>.
        /// </para>
        /// </summary>
        static readonly AssetRequest[] BoneCast =
        {
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("boneMon_r")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("boneMon_g")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("boneMon_b")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("boneMon_y")),

            AssetRequest.SpriteSet(AssetManifest.SiegeArt("boneBrute_r")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("boneBrute_g")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("boneBrute_b")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("boneBrute_y")),

            AssetRequest.SpriteSet(AssetManifest.SiegeArt("boneBulwark_r")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("boneBulwark_g")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("boneBulwark_b")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("boneBulwark_y")),
        };

        /// <summary>
        /// What the bone cast swings at the ward line, in <see cref="CastArt"/>'s own order.
        ///
        /// <para>
        /// <b>A raider that reaches the line stands there hitting it</b> every
        /// <c>SiegeTuning.BlowEvery</c> until something kills it, and for two chapters what that
        /// looked like was a walk cycle looping in place against a turret — invariant 37u's
        /// complaint (a body doing the wrong thing where it stands) arriving through the art
        /// rather than through the framing. This pack is the first one bought here that drew an
        /// attack, so this is the first cast that can answer it.
        /// </para>
        /// <para>
        /// <b>A cast with no swing answers null and the view keeps walking</b>, which is exactly
        /// what the insects and the brood do today. Nothing about either of them moves.
        /// </para>
        /// <para>
        /// <b>Cut on a bigger canvas at the walk's own scale</b>, because a shared one fitted to
        /// the walk's height would draw every skeleton at 59–73% of its size for the whole run for
        /// the sake of six frames at the line — see <c>make_siege_art.walk_and_swing</c>, and
        /// <c>SiegeView.Wear</c> for the half that reads the ratio back.
        /// </para>
        /// </summary>
        static readonly AssetRequest[] BoneSwings =
        {
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("boneMon_r_swing")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("boneMon_g_swing")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("boneMon_b_swing")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("boneMon_y_swing")),

            AssetRequest.SpriteSet(AssetManifest.SiegeArt("boneBrute_r_swing")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("boneBrute_g_swing")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("boneBrute_b_swing")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("boneBrute_y_swing")),

            AssetRequest.SpriteSet(AssetManifest.SiegeArt("boneBulwark_r_swing")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("boneBulwark_g_swing")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("boneBulwark_b_swing")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("boneBulwark_y_swing")),
        };

        /// <summary>The insects, which are what a siege draws unless something says otherwise.</summary>
        public const int Insects = 0;

        /// <summary>The cast rendered out of 3D. See <see cref="SecondCast"/>.</summary>
        public const int Baked = 1;

        /// <summary>The blob brood. See <see cref="BroodCast"/>.</summary>
        public const int Brood = 2;

        /// <summary>The skeletons. See <see cref="BoneCast"/>.</summary>
        public const int Bones = 3;

        /// <summary>How many casts this mode ships.</summary>
        public const int CastSets = 4;

        /// <summary>
        /// The casts the <b>main ladder</b> draws from, in the order its chapters meet them.
        ///
        /// <b>A chapter's cast is arithmetic on its ordinal, exactly as its map and its skies are</b>
        /// (invariant 7c). Two entries, so the third siege chapter draws the insects again and the
        /// fourth the brood — which is the point rather than a shortage: <b>a chapter published next
        /// year costs no cast at all</b>, and no chapter can ship drawing bodies nobody chose. A
        /// third pack lengthens this array and changes nothing else.
        /// </summary>
        static readonly int[] MainCasts = { Insects, Brood, Bones };

        /// <summary>
        /// How many casts the main ladder draws from before it starts again.
        ///
        /// <b>Exposed so a fixture can check the wrap without writing the number down</b> - the
        /// first one said "the third chapter wraps onto the first", which was true of two casts and
        /// stopped being true the day there were three: green, and about arithmetic this mode no
        /// longer does.
        /// </summary>
        public static int MainCastCount => MainCasts.Length;

        /// <summary>
        /// Which cast a chapter draws.
        ///
        /// <para>
        /// <b>One function, asked by both ends</b>, which is the rule <see cref="Ground"/> and
        /// <c>SiegeView.GroundAddress</c> are held to by <c>SiegeGroundTests</c> and for the same
        /// reason: two switches disagreeing about a chapter would <em>load</em> one cast and
        /// <em>draw</em> another, and an <c>Image</c> with a null sprite is a white rectangle over
        /// every raider on the hill rather than a blank (invariant 7b). <see cref="ArtFor"/> asks it
        /// to decide what to preload and <c>SiegeScreen</c> asks it to decide what to draw.
        /// </para>
        /// <para>
        /// <b>The Infinite lane is the one answer that is not arithmetic</b>, and it is a trial
        /// rather than a rule (see <see cref="SecondCast"/>): hanging the baked cast on the track
        /// puts it one tap from the authored chapters so the two can be judged against each other
        /// without leaving the mode. If the bake is kept, this line goes and the bake takes a place
        /// in <see cref="MainCasts"/>.
        /// </para>
        /// <para>
        /// <b>An unknown chapter answers with the insects</b>, because <c>ChapterOrderOf</c> reports
        /// -1 for one this catalog has never heard of and the fallback has to be a cast that is
        /// certainly on disk.
        /// </para>
        /// </summary>
        public static int CastFor(GameTrack track, int ordinal)
        {
            if (track == GameTrack.Infinite) return Baked;
            if (ordinal < 0) return Insects;

            return MainCasts[ordinal % MainCasts.Length];
        }

        /// <summary>
        /// The reels one cast is made of, in a <b>fixed order</b>: four creepers, then four brutes,
        /// then four bulwarks, each in <c>WardLine.Colours</c> order.
        ///
        /// <b>The order is the contract</b>, because <see cref="CastAddress"/> indexes into it —
        /// see that method for why the names live here and only here. <c>SiegeCastTests</c> pins it.
        /// </summary>
        public static IReadOnlyList<AssetRequest> CastArt(int set)
        {
            switch (set)
            {
                case Baked: return SecondCast;
                case Brood: return BroodCast;
                case Bones: return BoneCast;
                default: return InsectCast;
            }
        }

        /// <summary>
        /// The reels one cast swings at the line, in <see cref="CastArt"/>'s own order, or
        /// <b>null</b> for a cast whose pack drew none.
        ///
        /// <b>Null rather than a fallback to the walk</b>, because the view has to be able to tell
        /// "this cast has no swing" from "this cast has a swing and it failed to load": the first
        /// keeps walking and the second would be an <c>Image</c> with no sprite, which is a white
        /// rectangle over every raider at the line (invariant 7b).
        /// </summary>
        public static IReadOnlyList<AssetRequest> CastSwingArt(int set)
            => set == Bones ? BoneSwings : null;

        /// <summary>
        /// The address one raider's swing reel is at, or <b>empty</b> when this cast has none.
        ///
        /// Indexed into <see cref="CastSwingArt"/> exactly as <see cref="CastAddress"/> is indexed
        /// into <see cref="CastArt"/>, and for the same reason: one copy of the names.
        /// </summary>
        public static string CastSwing(int set, SiegeKind kind, int colour)
        {
            var art = CastSwingArt(set);
            if (art == null || art.Count < CastBodies) return string.Empty;

            int colours = Wards.WardLine.Colours.Length;
            int row = kind == SiegeKind.Bulwark ? 2 : kind == SiegeKind.Brute ? 1 : 0;
            int at = colour < 0 || colour >= colours ? 0 : colour;

            return art[row * colours + at].Address;
        }

        /// <summary>How many bodies a cast holds: three kinds in four colours.</summary>
        public const int CastBodies = 12;

        /// <summary>
        /// The address one raider's reel is at, in one cast.
        ///
        /// <para>
        /// <b>Indexed into <see cref="CastArt"/> rather than switched over a second copy of the
        /// names</b>, and that is the whole point of this method. The view used to carry its own
        /// switch of twelve literals per cast — thirty-six names written down twice, once to
        /// <em>load</em> and once to <em>draw</em> — which is exactly the fault <c>SiegeGroundTests</c>
        /// exists to police for the ten grounds: two switches disagreeing load one thing and draw
        /// another, and an <c>Image</c> with a null sprite is a <b>white rectangle</b> rather than a
        /// blank (invariant 7b), over every raider on the hill, with every gate green.
        /// </para>
        /// <para>
        /// So there is one copy. The arrays keep their literals, so
        /// <c>Tools/verify/artnames.py</c> still reads every name at its own call site and holds it
        /// to disk, and the view now has none of its own to get wrong. Adding a fourth cast is one
        /// array and one row of <see cref="MainCasts"/>.
        /// </para>
        /// <para>
        /// <b>A kind this cast has no body for draws the creeper</b>, which is what a bomber has
        /// always done: it is a creeper carrying something, and giving it a body of its own is a
        /// decision nobody has made.
        /// </para>
        /// </summary>
        public static string CastAddress(int set, SiegeKind kind, int colour)
        {
            var art = CastArt(set);
            if (art == null || art.Count < CastBodies) return string.Empty;

            int colours = Wards.WardLine.Colours.Length;
            int row = kind == SiegeKind.Bulwark ? 2 : kind == SiegeKind.Brute ? 1 : 0;
            int at = colour < 0 || colour >= colours ? 0 : colour;

            return art[row * colours + at].Address;
        }

        /// <summary>
        /// The turrets a run is guaranteed to be able to draw: the roster's starter, in four
        /// colours.
        ///
        /// <b>Resident rather than scoped, and it is the safety net rather than the feature.</b>
        /// The player's own four arrive in the screen's own hold, which is asynchronous —
        /// and an <c>Image</c> with a null sprite is a white rectangle rather than a blank
        /// (invariant 7b). So the fallback the line resolves to when anything at all is wrong is
        /// the one thing that can never be missing.
        /// </summary>
        static IEnumerable<AssetRequest> StarterLine()
        {
            var starter = ProgressionRules.Table.Wards.Starter;
            if (starter == null) yield break;

            for (int i = 0; i < Wards.WardLine.Colours.Length; i++)
            {
                char colour = Wards.WardLine.Colours[i];
                yield return AssetRequest.Sprite(AssetManifest.SiegeArt(starter.ArtFor(colour)));
                yield return AssetRequest.SpriteSet(AssetManifest.SiegeArt(starter.FireFor(colour)));

                // **And what it throws**, which used to be resident anyway under another name: the
                // starter fired the four elemental reels the mode's cast already carries, so a
                // line falling back to it could always draw. It has an effect of its own now
                // (`WardModel.Elemental` moved), so the safety net has to name that instead - or
                // the one turret a broken line resolves to is the one with no bolt.
                if (!starter.OwnShot) continue;

                yield return AssetRequest.SpriteSet(AssetManifest.SiegeFx(starter.ShotFor(colour)));
                yield return AssetRequest.SpriteSet(AssetManifest.SiegeFx(starter.MuzzleFor(colour)));
                yield return AssetRequest.SpriteSet(AssetManifest.SiegeFx(starter.HitFor(colour)));
            }
        }

        public override IReadOnlyList<AssetRequest> Art
        {
            get
            {
                var list = new List<AssetRequest>(Cast);
                list.AddRange(StarterLine());

                // **Every cast, because this is the question about what *exists*.** It is what
                // `AddressableAddresses.FrameFolders` walks to label frames, and a reel that is
                // never named here ships addressed, grouped, built into a bundle and impossible to
                // load (invariant 37at's `No Location found for Key=...`). Only `ArtFor` narrows.
                for (int set = 0; set < CastSets; set++)
                {
                    list.AddRange(CastArt(set));

                    // **And every swing reel**, for the same reason: this is the question about
                    // what *exists*, and a reel never named here ships addressed, grouped, built
                    // into a bundle and impossible to load (invariant 37at).
                    var swings = CastSwingArt(set);
                    if (swings != null) list.AddRange(swings);
                }

                return list;
            }
        }

        public override IReadOnlyList<AssetRequest> ArtFor(ChapterBody chapter)
        {
            var list = new List<AssetRequest>(Cast);
            list.AddRange(StarterLine());

            if (chapter == null)
            {
                list.AddRange(CastArt(Insects));
                return list;
            }

            // One cast, chosen by where this chapter sits and which lane it is in - and by the same
            // function the view asks when it draws a body, so the two cannot disagree.
            int cast = CastFor(GameContent.Index.TrackOf(chapter.Id),
                               GameContent.Index.ChapterOrderOf(chapter.Id));

            list.AddRange(CastArt(cast));

            var swing = CastSwingArt(cast);
            if (swing != null) list.AddRange(swing);

            var seen = new HashSet<SiegeKind>();

            for (int i = 0; i < chapter.Levels.Count; i++)
            {
                // One ground per rung (invariant 7c). Asked of every level rather than of the
                // siege ones alone, because the place a level sits in its chapter is what decides
                // this and `ChapterModeValidator` already proves a chapter is one mode.
                list.Add(Ground(i));

                if (!(chapter.Levels[i].Rules is SiegeRules siege)) continue;

                var sends = siege.Layout;
                if (sends == null || !sends.HasBoss || !seen.Add(sends.BossKind)) continue;

                Bosses(sends.BossKind, list);
            }

            return list;
        }

        /// <summary>
        /// Whether any authored row stands the retired cog cell, and which one.
        ///
        /// Its own method rather than a clause, because it is asked before the grid exists — a
        /// refusal that has to run before the thing it is refusing can be parsed has nowhere else
        /// to live.
        /// </summary>
        static bool Retired(string[] rows, out int row)
        {
            for (int i = 0; rows != null && i < rows.Length; i++)
            {
                if (rows[i] == null || rows[i].IndexOf(SiegeLayout.RetiredCog) < 0) continue;

                row = i;
                return true;
            }

            row = -1;
            return false;
        }

        public override bool TryRead(LevelDto dto, LevelId id, ICollection<string> problems,
                                     out ILevelRules rules)
        {
            rules = null;

            var block = dto.siege;

            // **The retired cog cell is refused by name rather than by falling through.** A `*`
            // was a cog standing on the field; cogs are dropped by felled raiders now, so a body
            // carrying one was authored for a build that is gone. `ProtoGrid.TryRead` would
            // refuse it anyway as an unknown cell — what a named refusal buys is that whoever
            // meets it is told *why* rather than left to guess which of five letters is wrong
            // (invariant 5f, the duskcap's rule).
            if (Retired(block.rows, out int row))
            {
                problems.Add($"{id}: row {row + 1} stands a '{SiegeLayout.RetiredCog}' on the "
                           + "field. A cog is no longer a cell - it is dropped by a raider the "
                           + "line kills and lies on the hill until it is tapped, so a field "
                           + "authors gems and nothing else. Drop the character and set 'cogs' "
                           + "to a drop rate per hundred kills");
                return false;
            }

            if (!ProtoGrid.TryRead(block.rows, block.width, block.height, SiegeLayout.Cells,
                                   out var grid, out string error))
            {
                problems.Add($"{id}: {error}");
                return false;
            }

            // **The ramp, for a lane whose waves never stop.** A level authors one or the other:
            // an endless lane's muster is a rule (`SiegeEndless`) rather than a list, so a file
            // carrying both would have two answers to what its second wave is.
            SiegeEndless endless = null;

            if (block.endless != null && block.endless.IsAuthored)
            {
                bool authored = (block.waves != null && block.waves.Length > 0)
                             || !string.IsNullOrEmpty(block.boss);

                if (authored)
                {
                    problems.Add($"{id}: this siege authors both an endless ramp and its own "
                               + "waves. A lane whose waves never stop has no list of them - "
                               + "drop the 'waves' and 'boss' fields, or drop 'endless'");
                    return false;
                }

                endless = new SiegeEndless(block.gems, block.cogs,
                                           block.endless.goldWave, block.endless.silverFactor);
            }

            // **Refused here rather than clamped**, for the retired cog cell's reason: a body
            // carrying a figure this mode cannot mean was written against different rules, and
            // silently reading it as something else ships a hill nobody composed. Nought is the
            // plain figure and is what every body before this field existed says.
            if (block.tough != 0 && (block.tough < 10 || block.tough > SiegeTuning.MostTough))
            {
                problems.Add($"{id}: this siege deals raiders at {block.tough} tenths of their "
                           + $"ordinary health. A surge is written in tenths, is never below 10 "
                           + $"(the plain figure) and is capped at {SiegeTuning.MostTough} - and it "
                           + "is derived from the chapter's ordinal by the chapter tool rather than "
                           + "typed (SiegeTuning.ToughnessFor)");
                return false;
            }

            var layout = new SiegeLayout(grid, block.gems, block.wards, block.waves, block.boss,
                                         block.cogs, endless, block.tough, block.charms);

            if (layout.Fault != null)
            {
                problems.Add($"{id}: {layout.Fault}");
                return false;
            }

            rules = new SiegeRules(layout);
            return true;
        }

        /// <summary>
        /// Par is what the level sends, divided by the most one match could ever be worth.
        ///
        /// <para>
        /// <b>Worked out here rather than handed over as a function</b>, unlike every other mode
        /// on this shape: theirs is a breadth-first search worth deferring off the map screen
        /// (invariant 26d) and this is a division, so paying for it while a level is read costs
        /// nothing measurable.
        /// </para>
        /// <para>
        /// <b>The budget is turned off and must stay off.</b> A siege is lost when the last ward
        /// falls, so a move allowance would be a second fail state — and a meter counting down to
        /// an ending that never happens is the fault invariant 22 names from the other side. The
        /// authored <c>budgetFactor</c> is ignored rather than honoured for the same reason
        /// <c>ProtoValidator</c> refuses one: two ways to say one thing is how they come to
        /// disagree.
        /// </para>
        /// </summary>
        public override LevelTuning Tune(LevelDto dto, ILevelRules rules)
        {
            var siege = (SiegeRules)rules;

            // **An endless lane is graded on a count that climbs**, which is the one place in this
            // game where a bigger number is a better run. Par is the wave a three-star run
            // reaches - authored, because nothing can derive it: par everywhere else is a search
            // or an arithmetic floor over what a level *sends*, and an endless lane sends
            // everything.
            // Bound to a local rather than reached through twice, which `compile.py`'s coarse
            // `.Layout.` guard also happens to want: it cannot tell a siege's own layout from a
            // glade's board, and the guard is deliberately coarse (a false positive costs one
            // line, a missing null check costs a crash).
            var sends = siege.Layout;

            if (sends.IsEndless)
                return LevelTuning.Climbing(sends.Endless.GoldWave, sends.Endless.SilverFactor);

            return new LevelTuning(SiegeTuning.Par(sends),
                                   dto.goldFactor, dto.silverFactor,
                                   LevelTuning.Unlimited);
        }

        /// <summary>A siege counts one thing the player spends, and it is a match.</summary>
        public override string RecordStem => "ui.rank.moves";
    }
}
