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

            // The ward line: four turret models, each with its own recoil, and none of them
            // carrying a colour - which one a ward burns is put on at run time, from the same
            // `Pal` entry the gems and the raiders take theirs from (`SiegeView.Coat`).
            AssetRequest.Sprite(AssetManifest.SiegeArt("ward1")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("ward2")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("ward3")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("ward4")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("ward_dead")),

            AssetRequest.SpriteSet(AssetManifest.SiegeArt("fire1")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("fire2")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("fire3")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("fire4")),

            AssetRequest.Sprite(AssetManifest.SiegeArt("bullet")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("socket")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("rampart")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("hill")),
            AssetRequest.Sprite(AssetManifest.SiegeArt("plate")),

            AssetRequest.SpriteSet(AssetManifest.SiegeArt("mon1")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("mon2")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("mon3")),
            AssetRequest.SpriteSet(AssetManifest.SiegeArt("brute")),

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

        public override IReadOnlyList<AssetRequest> Art => Cast;

        public override bool TryRead(LevelDto dto, LevelId id, ICollection<string> problems,
                                     out ILevelRules rules)
        {
            rules = null;

            var block = dto.siege;

            if (!ProtoGrid.TryRead(block.rows, block.width, block.height, SiegeLayout.Letters,
                                   out var grid, out string error))
            {
                problems.Add($"{id}: {error}");
                return false;
            }

            var layout = new SiegeLayout(grid, block.gems, block.wards, block.waves);

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
