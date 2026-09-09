using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Modes;

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
        public override IProtoBoard Fresh() => SiegeBoard.Build(Layout);

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
            AssetRequest.Sprite(AssetManifest.SiegeArt("ward1_r")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("ward1_g")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("ward1_b")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("ward1_y")),

            AssetRequest.Sprite(AssetManifest.SiegeArt("ward2_r")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("ward2_g")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("ward2_b")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("ward2_y")),

            AssetRequest.Sprite(AssetManifest.SiegeArt("ward3_r")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("ward3_g")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("ward3_b")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("ward3_y")),

            AssetRequest.Sprite(AssetManifest.SiegeArt("ward4_r")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("ward4_g")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("ward4_b")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("ward4_y")),

            AssetRequest.Sprite(AssetManifest.SiegeArt("ward5_r")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("ward5_g")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("ward5_b")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("ward5_y")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("ward_dead")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("crest")),

            AssetRequest.SpriteSet(AssetManifest.SiegeArt("fire1_r")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("fire1_g")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("fire1_b")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("fire1_y")),

            AssetRequest.SpriteSet(AssetManifest.SiegeArt("fire2_r")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("fire2_g")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("fire2_b")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("fire2_y")),

            AssetRequest.SpriteSet(AssetManifest.SiegeArt("fire3_r")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("fire3_g")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("fire3_b")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("fire3_y")),

            AssetRequest.SpriteSet(AssetManifest.SiegeArt("fire4_r")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("fire4_g")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("fire4_b")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("fire4_y")),

            AssetRequest.SpriteSet(AssetManifest.SiegeArt("fire5_r")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("fire5_g")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("fire5_b")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("fire5_y")),

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
            // per kind, so a player who cannot separate two hues can still separate a mushroom
            // from a skull.
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
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeArt("blight_walk")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeArt("blight_cast")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeFx("hex")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeFx("hex_muzzle")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeFx("hex_hit")));
                    break;

                case SiegeKind.Boss:
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeArt("boss")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeArt("boss_walk")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeArt("boss_cast")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeFx("spell")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeFx("spell_muzzle")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeFx("spell_hit")));
                    break;

                case SiegeKind.Warbringer:
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeArt("bringer")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeArt("bringer_walk")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeArt("bringer_cast")));
                    // **Two of its three reels, because a roar is thrown at nothing.** It has no
                    // flight, so `roar` is baked and never scoped in - see `SiegeView.Roar`, which
                    // draws the impact upright at the boss and the muzzle flat over the ground.
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeFx("roar_muzzle")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeFx("roar_hit")));
                    break;

                case SiegeKind.Overlord:
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeArt("over")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeArt("over_walk")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeArt("over_cast")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeFx("omen")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeFx("omen_muzzle")));
                    into.Add(AssetRequest.SpriteSet(AssetManifest.SiegeFx("omen_hit")));
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
        public override IReadOnlyList<AssetRequest> Art => Cast;

        public override IReadOnlyList<AssetRequest> ArtFor(ChapterBody chapter)
        {
            var list = new List<AssetRequest>(Cast);
            if (chapter == null) return list;

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

        public override bool TryRead(LevelDto dto, LevelId id, ICollection<string> problems,
                                     out ILevelRules rules)
        {
            rules = null;

            var block = dto.siege;

            // `Cells` rather than `Letters`: a field may stand a cog on it, and a cog is not a
            // colour (see `SiegeLayout.Cells`).
            if (!ProtoGrid.TryRead(block.rows, block.width, block.height, SiegeLayout.Cells,
                                   out var grid, out string error))
            {
                problems.Add($"{id}: {error}");
                return false;
            }

            var layout = new SiegeLayout(grid, block.gems, block.wards, block.waves, block.boss,
                                         block.cogs);

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

            return new LevelTuning(SiegeTuning.Par(siege.Layout),
                                   dto.goldFactor, dto.silverFactor,
                                   LevelTuning.Unlimited);
        }

        /// <summary>A siege counts one thing the player spends, and it is a match.</summary>
        public override string RecordStem => "ui.rank.moves";
    }
}
