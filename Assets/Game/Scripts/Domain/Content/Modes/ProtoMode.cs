using System;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Modes;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// What a level of a prototype mode carries: its board, and the room it forgives.
    ///
    /// <para>
    /// <b>A base class rather than a bare one, because a run is shared and a board is not.</b>
    /// <c>ProtoScreen</c> drives a mode through <see cref="Fresh"/> and <see cref="Opening"/> and
    /// never learns which one it is holding; each mode's own screen casts back down to reach its
    /// layout, which is the one thing that genuinely differs. That split is what let five modes
    /// ship for the price of a little over one — and then what let four of them be taken out
    /// again without Toppleglen noticing, and Toppleglen and Nova Raid after them without the
    /// Iron Quarry noticing. The same seam earning its keep four times.
    /// </para>
    /// </summary>
    public abstract class ProtoLevelRules : ILevelRules
    {
        /// <summary>
        /// Wasted moves these boards forgive, above par.
        ///
        /// <para>
        /// <b>Five, and the fifth is the two-star line rather than generosity.</b> A budget of
        /// <c>par + spare</c> has to clear <c>ceil(par × 1.40)</c> or the bottom band is stranded
        /// and every clear is worth two stars or three — invariant 22's fault arrived at from the
        /// budget's side, which is how Groovekeeper found it. Five holds to par twelve, which is
        /// twice as deep as any of these boards goes.
        /// </para>
        /// <para>
        /// <b>A count rather than a multiple of par</b>, for invariant 26e's reason: a wrong move
        /// in every one of these modes is permanent <em>and</em> makes the board worse — a pulled
        /// stone is gone, a stopper cannot go back, three blooms have left the grove, a pod is
        /// burst, a bramble is cut, and a charge cut loose in the wrong direction is a charge
        /// that has already gone off. So the room a board needs is a count, and it is the same on
        /// the first board and the last, because the budget is a fail line and difficulty is the
        /// boards' job (invariant 5d).
        /// </para>
        /// </summary>
        public const int DefaultSpare = 5;

        public readonly int Spare;

        protected ProtoLevelRules(int spare) => Spare = spare > 0 ? spare : DefaultSpare;

        public abstract GameMode Mode { get; }

        /// <summary>
        /// The board as authored, for anything that needs its shape rather than its rules.
        ///
        /// <b>Here rather than reached through each mode's own layout</b>, and that is not only
        /// tidiness: <c>compile.py</c> refuses a file that writes <c>.Layout.</c> without saying
        /// it knows a level's board can be absent (a glade's <c>LevelDefinition.Layout</c> is null
        /// on every level of every other mode). The guard is coarse on purpose and this is the
        /// idiom that satisfies it — ask the thing that knows.
        /// </summary>
        public abstract ProtoGrid Grid { get; }

        /// <summary>A board as authored, for a run. Never shared — a run mutates what it is given.</summary>
        public abstract IProtoBoard Fresh();

        /// <summary>The same board as the solver sees it.</summary>
        public abstract ProtoPosition Opening();

        /// <summary>How much board there is, which is the fallback par when a search cannot prove one.</summary>
        public abstract int Room { get; }
    }

    /// <summary>Hollowmarch: a haul-road, and the line walking along it.</summary>
    public sealed class MarchRules : ProtoLevelRules
    {
        public readonly MarchLayout Layout;

        public MarchRules(MarchLayout layout, int spare) : base(spare) => Layout = layout;

        public override GameMode Mode => GameMode.March;
        public override ProtoGrid Grid => Layout.Grid;
        public override IProtoBoard Fresh() => MarchBoard.Build(Layout);
        public override ProtoPosition Opening() => new MarchFuture(MarchBoard.Build(Layout));

        /// <summary>
        /// The fallback par, used only when a board could not be proved on the device that
        /// opened it — which means broken content shipped, so it is chosen to be impossible to
        /// lose to rather than to be accurate (see <see cref="ProtoSetup.Par"/>). One core per
        /// pod on the road, because clearing the whole line one match at a time is more than any
        /// run could ever be asked for.
        /// </summary>
        public override int Room => Layout.Line.Length;
    }

    /// <summary>
    /// The half of a prototype mode that the content pipeline sees: how to read one of its levels,
    /// and what tuning that level gets.
    ///
    /// <para>
    /// <b>One reader, however many modes.</b> Each of them authors the same block — a grid, a
    /// deal, a slack — so what a subclass supplies is three things: which field on the level
    /// claims it, which letters its grid may hold, and how to turn a parsed grid into its own
    /// rules. Nothing about parsing rows, counting them, reporting a bad character or resolving
    /// par is written per mode, which is one place per mode it cannot come to differ. It has
    /// carried nine and carries one.
    /// </para>
    /// </summary>
    public abstract class ProtoMode : LevelMode
    {
        /// <summary>This mode's block on a level, or null when the level has none.</summary>
        protected abstract ProtoDto Block(LevelDto dto);

        /// <summary>Every character this mode's boards may be written in.</summary>
        protected abstract string Letters { get; }

        /// <summary>
        /// Turns a parsed grid into this mode's rules, or reports what is wrong with it.
        ///
        /// Everything refused here is a fact about the mode rather than about the file format —
        /// a quarry with nothing to cut loose, a floor where no flick strikes anything — so the
        /// message says what the board is missing rather than where a character is.
        /// </summary>
        protected abstract bool Compose(ProtoGrid grid, ProtoDto block, LevelId id,
                                        ICollection<string> problems, out ProtoLevelRules rules);

        public override bool Claims(LevelDto dto)
        {
            var block = Block(dto);
            return block != null && block.IsAuthored;
        }

        public override bool TryRead(LevelDto dto, LevelId id, ICollection<string> problems,
                                     out ILevelRules rules)
        {
            rules = null;

            var block = Block(dto);

            if (!ProtoGrid.TryRead(block.rows, block.width, block.height, Letters,
                                   out var grid, out string error))
            {
                problems.Add($"{id}: {error}");
                return false;
            }

            if (!Compose(grid, block, id, problems, out var composed)) return false;

            rules = composed;
            return true;
        }

        /// <summary>
        /// Par is searched and everything else derives from it.
        ///
        /// <para>
        /// Handed over as a <see cref="Func{T}"/> rather than run here, for <c>FallMode</c>'s
        /// reason: a chapter body may hold ten boards and this is a search rather than a walk, so
        /// running all ten while the map is opening would be a hitch on a screen that never asks
        /// the question (invariant 26d).
        /// </para>
        /// </summary>
        public override LevelTuning Tune(LevelDto dto, ILevelRules rules)
        {
            var board = (ProtoLevelRules)rules;
            string id = dto.id;

            return new LevelTuning(() => ProtoSetup.Par(id, board),
                                   dto.goldFactor, dto.silverFactor, dto.budgetFactor,
                                   board.Spare);
        }

        /// <summary>A prototype board counts one thing, and it is an input.</summary>
        public override string RecordStem => "ui.rank.moves";
    }

    /// <summary>
    /// Hollowmarch. Fire a core into the line; three alike go off and the gap closes behind
    /// them.
    ///
    /// <para>
    /// <b>It authors the shared prototype block</b> — a grid, a deal, a slack — so parsing,
    /// counting rows, naming a bad character and resolving par by search are all inherited,
    /// which is the seam that has now carried ten modes. What it adds is a road, a set of pods
    /// and a cast, all scoped to the chapter rather than to the global set (invariant 7b).
    /// </para>
    /// <para>
    /// <b>It is the first of them to use the block's third field.</b> <c>ProtoDto</c> has always
    /// described itself as a grid, a deal and a slack, and until now no mode built on it had a
    /// deal — Lightfall and Budburst both have one and neither is a prototype mode. That is the
    /// shape paying off rather than being stretched: the field was described before it was
    /// needed and did not have to be invented under a mode.
    /// </para>
    /// </summary>
    public sealed class MarchMode : ProtoMode
    {
        public override GameMode Mode => GameMode.March;

        protected override ProtoDto Block(LevelDto dto) => dto.march;
        protected override string Letters => MarchLayout.Letters;

        /// <summary>
        /// The road, the pods, the cast and the explosions, addressed as single sprites and as
        /// folders of frames.
        ///
        /// <para>
        /// Listed here rather than derived from a board, for the reason Nova Raid's list gave:
        /// which of three critters runs out of a broken cage is a drawing decision the view
        /// makes from the pod, so a board holding one cage still needs all three loadable, and a
        /// set that varied per level would be a scope changing under the player mid-chapter.
        /// </para>
        /// <para>
        /// <b>Twelve sprites draw the whole board.</b> A road tile, a ground tile, some rubble,
        /// a gate, four pods, a cage to hang on one, a plate for a raider and the Spark — every
        /// cell is one of them drawn again, which is what leaves the budget for a cast that
        /// moves.
        /// </para>
        /// </summary>
        static readonly AssetRequest[] Cast =
        {
            AssetRequest.Sprite(AssetManifest.MarchArt("road")),
            AssetRequest.Sprite(AssetManifest.MarchArt("road_lit")),
            AssetRequest.Sprite(AssetManifest.MarchArt("ground")),
            AssetRequest.Sprite(AssetManifest.MarchArt("rubble")),
            AssetRequest.Sprite(AssetManifest.MarchArt("portal")),
            AssetRequest.Sprite(AssetManifest.MarchArt("pod_r")),
            AssetRequest.Sprite(AssetManifest.MarchArt("pod_g")),
            AssetRequest.Sprite(AssetManifest.MarchArt("pod_b")),
            AssetRequest.Sprite(AssetManifest.MarchArt("pod_y")),
            AssetRequest.Sprite(AssetManifest.MarchArt("pod_spark")),
            AssetRequest.Sprite(AssetManifest.MarchArt("cage")),
            AssetRequest.Sprite(AssetManifest.MarchArt("plate")),

            AssetRequest.SpriteSet(AssetManifest.MarchArt("mon1")),
            AssetRequest.SpriteSet(AssetManifest.MarchArt("mon1_jump")),
            AssetRequest.SpriteSet(AssetManifest.MarchArt("mon2")),
            AssetRequest.SpriteSet(AssetManifest.MarchArt("mon2_jump")),
            AssetRequest.SpriteSet(AssetManifest.MarchArt("mon3")),
            AssetRequest.SpriteSet(AssetManifest.MarchArt("mon3_jump")),
            AssetRequest.SpriteSet(AssetManifest.MarchArt("drone")),
            AssetRequest.SpriteSet(AssetManifest.MarchArt("brute")),
            AssetRequest.SpriteSet(AssetManifest.MarchArt("brute_hit")),
            AssetRequest.SpriteSet(AssetManifest.MarchArt("brute_dead")),
            // Bolt himself, who stands at the launcher rather than being a sprite of a gun: a
            // robot with his hands up says "this is where the cores come from" where a barrel
            // would only be scenery, and he is already a flipbook because he does the talking.
            AssetRequest.SpriteSet(AssetManifest.MarchArt("bolt")),
            AssetRequest.SpriteSet(AssetManifest.MarchArt("collector")),

            AssetRequest.SpriteSet(AssetManifest.MarchFx("boom_fire")),
            AssetRequest.SpriteSet(AssetManifest.MarchFx("boom_gold")),
            AssetRequest.SpriteSet(AssetManifest.MarchFx("boom_spark")),
            AssetRequest.SpriteSet(AssetManifest.MarchFx("boom_red")),
            AssetRequest.SpriteSet(AssetManifest.MarchFx("boom_smoke")),
        };

        public override IReadOnlyList<AssetRequest> Art => Cast;

        protected override bool Compose(ProtoGrid grid, ProtoDto block, LevelId id,
                                        ICollection<string> problems, out ProtoLevelRules rules)
        {
            rules = null;
            var layout = new MarchLayout(grid, block.spare, block.cores);

            // The road is read before anything else is asked, because everything else asked
            // about this board is a question about a line walking along it.
            if (layout.Fault != null)
            {
                problems.Add($"{id}: {layout.Fault}");
                return false;
            }

            var board = MarchBoard.Build(layout);

            if (board.Goals == 0)
            {
                problems.Add($"{id}: this road carries no cage and no raider, so there is " +
                             "nothing to do and the level opens finished");
                return false;
            }

            if (board.IsFinished)
            {
                problems.Add($"{id}: this road opens finished");
                return false;
            }

            if (board.Line.Count == 0)
            {
                problems.Add($"{id}: this road is empty, so there is no line to fire into");
                return false;
            }

            // A board authored with three alike already touching plays its own first move before
            // anybody has looked at it. Budburst's "authored settled" rule, and it matters more
            // here because the chain would run on from it - so the board the player meets would
            // not be the board that was authored, proved or graded.
            var runs = new List<int>(16);
            board.Runs(runs);
            for (int i = 0; i < runs.Count; i += 2)
            {
                if (runs[i + 1] < MarchLayout.BurstAt) continue;

                problems.Add($"{id}: the line holds {runs[i + 1]} alike already touching at " +
                             $"position {runs[i]}, so it would go off before anybody had fired " +
                             "a core - a line is authored settled");
                return false;
            }

            if (!board.AnyMove)
            {
                problems.Add($"{id}: no core can be fired into this line at all - it matches " +
                             "nothing and the road is packed solid, so there is no move to make");
                return false;
            }

            rules = new MarchRules(layout, block.spare);
            return true;
        }
    }

    /// <summary>Emberforge: a wall of shards, and the fittings bolted into it.</summary>
    public sealed class EmberRules : ProtoLevelRules
    {
        public readonly EmberLayout Layout;

        public EmberRules(EmberLayout layout, int spare) : base(spare) => Layout = layout;

        public override GameMode Mode => GameMode.Ember;
        public override ProtoGrid Grid => Layout.Grid;
        public override IProtoBoard Fresh() => EmberBoard.Build(Layout);
        public override ProtoPosition Opening() => new EmberFuture(EmberBoard.Build(Layout));

        /// <summary>
        /// The fallback par, used only when a board could not be proved on the device that
        /// opened it - which means broken content shipped, so it is chosen to be impossible to
        /// lose to rather than to be accurate (see <see cref="ProtoSetup.Par"/>). One move for
        /// every cell that is not a plain shard, which is more than any wall could ever ask for.
        /// </summary>
        public override int Room
        {
            get
            {
                int room = 0;
                for (int i = 0; i < Layout.Grid.Count; i++)
                    if (!EmberLayout.IsShard(Layout.Grid.At(i))) room++;
                return room;
            }
        }
    }

    /// <summary>
    /// Emberforge. Fuse three alike into an ember; tap it and a cross of light goes down its
    /// row and its column.
    ///
    /// <para>
    /// <b>It authors the shared prototype block and leaves the deal empty.</b> A wall is
    /// everything the level hands over - nothing falls in from above, which is what keeps the
    /// board monotone and therefore searchable at all (invariant 26). Hollowmarch is the only
    /// mode on this block that wanted <c>cores</c>, and the two sitting side by side on one
    /// reader is the seam doing what it was built for.
    /// </para>
    /// </summary>
    public sealed class EmberMode : ProtoMode
    {
        public override GameMode Mode => GameMode.Ember;

        protected override ProtoDto Block(LevelDto dto) => dto.ember;
        protected override string Letters => EmberLayout.Letters;

        /// <summary>
        /// The wall, the fittings, the cast and the explosions.
        ///
        /// <para>
        /// Listed here rather than derived from a board, for the reason Hollowmarch's list
        /// gives: which of three critters climbs out of a broken cage is a drawing decision the
        /// view makes from the cell, so a wall holding one cage still needs all three loadable,
        /// and a set that varied per level would be a scope changing under the player mid-chapter.
        /// </para>
        /// <para>
        /// <b>Eleven sprites draw the whole wall.</b> A backing plate and its lit twin, four
        /// shards, an ember, stone, frost, a cage and a warden plate - every cell is one of them
        /// drawn again, which is what leaves the budget for a cast that moves and for explosions
        /// that are frames rather than particles.
        /// </para>
        /// <para>
        /// <b>The cast is cut into this mode own folder</b> rather than reached across into
        /// <c>Art/March/</c>. Two chapters wanting one address puts it in the global group
        /// (<c>AddressableAddresses.ChapterOwnership</c>), which is right for art that genuinely
        /// is one thing - and these are not: the smelter critters are a different three, and the
        /// same robot reads differently lit by a furnace. What the two modes share is the
        /// <em>cast list</em> (<c>StoryCast</c>), which is vocabulary rather than pixels.
        /// </para>
        /// </summary>
        static readonly AssetRequest[] Cast =
        {
            AssetRequest.Sprite(AssetManifest.EmberArt("wall")),
            AssetRequest.Sprite(AssetManifest.EmberArt("wall_lit")),
            AssetRequest.Sprite(AssetManifest.EmberArt("stone")),
            AssetRequest.Sprite(AssetManifest.EmberArt("frost")),
            AssetRequest.Sprite(AssetManifest.EmberArt("cage")),
            AssetRequest.Sprite(AssetManifest.EmberArt("plate")),
            AssetRequest.Sprite(AssetManifest.EmberArt("ember")),
            AssetRequest.Sprite(AssetManifest.EmberArt("shard_r")),
            AssetRequest.Sprite(AssetManifest.EmberArt("shard_g")),
            AssetRequest.Sprite(AssetManifest.EmberArt("shard_b")),
            AssetRequest.Sprite(AssetManifest.EmberArt("shard_y")),

            AssetRequest.SpriteSet(AssetManifest.EmberArt("warden")),
            AssetRequest.SpriteSet(AssetManifest.EmberArt("warden_hit")),
            AssetRequest.SpriteSet(AssetManifest.EmberArt("warden_dead")),

            AssetRequest.SpriteSet(AssetManifest.EmberArt("mon1")),
            AssetRequest.SpriteSet(AssetManifest.EmberArt("mon1_jump")),
            AssetRequest.SpriteSet(AssetManifest.EmberArt("mon2")),
            AssetRequest.SpriteSet(AssetManifest.EmberArt("mon2_jump")),
            AssetRequest.SpriteSet(AssetManifest.EmberArt("mon3")),
            AssetRequest.SpriteSet(AssetManifest.EmberArt("mon3_jump")),

            AssetRequest.SpriteSet(AssetManifest.EmberArt("bolt")),
            AssetRequest.SpriteSet(AssetManifest.EmberArt("collector")),

            AssetRequest.SpriteSet(AssetManifest.EmberFx("boom_fire")),
            AssetRequest.SpriteSet(AssetManifest.EmberFx("boom_gold")),
            AssetRequest.SpriteSet(AssetManifest.EmberFx("boom_blue")),
            AssetRequest.SpriteSet(AssetManifest.EmberFx("boom_violet")),
            AssetRequest.SpriteSet(AssetManifest.EmberFx("boom_smoke")),
        };

        public override IReadOnlyList<AssetRequest> Art => Cast;

        protected override bool Compose(ProtoGrid grid, ProtoDto block, LevelId id,
                                        ICollection<string> problems, out ProtoLevelRules rules)
        {
            rules = null;

            if (!string.IsNullOrEmpty(block.cores))
            {
                problems.Add($"{id}: this wall deals '{block.cores}', and nothing is ever dealt " +
                             "into an Emberforge wall - what is authored is all there is, which " +
                             "is what makes par searchable at all");
                return false;
            }

            var layout = new EmberLayout(grid, block.spare);

            if (layout.Fault != null)
            {
                problems.Add($"{id}: {layout.Fault}");
                return false;
            }

            var board = EmberBoard.Build(layout);

            if (board.IsFinished)
            {
                problems.Add($"{id}: this wall opens finished");
                return false;
            }

            // A wall authored with three alike already in a line plays its own first move
            // before anybody has looked at it - Budburst "authored settled" rule, and it
            // matters more here because the cascade would run on from it, so the wall the
            // player meets would not be the wall that was authored, proved or graded.
            if (board.Stirred)
            {
                problems.Add($"{id}: this wall holds {EmberLayout.FuseAt} alike already in a " +
                             "line, so it would fuse before anybody had touched it - a wall is " +
                             "authored settled");
                return false;
            }

            if (!board.AnyMove)
            {
                problems.Add($"{id}: no swap on this wall lines anything up and it carries no " +
                             "ember, so there is no move to make");
                return false;
            }

            rules = new EmberRules(layout, block.spare);
            return true;
        }
    }

    /// <summary>Prismvale: a field of gems, the lanterns standing in it and the critters asleep among them.</summary>
    public sealed class PrismRules : ProtoLevelRules
    {
        public readonly PrismLayout Layout;

        public PrismRules(PrismLayout layout, int spare) : base(spare) => Layout = layout;

        public override GameMode Mode => GameMode.Prism;
        public override ProtoGrid Grid => Layout.Grid;
        public override IProtoBoard Fresh() => PrismBoard.Build(Layout);
        public override ProtoPosition Opening() => new PrismFuture(PrismBoard.Build(Layout));

        /// <summary>
        /// The fallback par, used only when a board could not be proved on the device that
        /// opened it - which means broken content shipped, so it is chosen to be impossible to
        /// lose to rather than to be accurate (see <see cref="ProtoSetup.Par"/>). One swap for
        /// every gem on the board, which is more moves than any arrangement of them could ever
        /// ask for.
        /// </summary>
        public override int Room
        {
            get
            {
                int room = 0;
                for (int i = 0; i < Layout.Count; i++)
                    if (PrismLayout.IsGem(Layout.At(i))) room++;

                return room > 0 ? room : 1;
            }
        }
    }

    /// <summary>
    /// Prismvale. Drag a gem onto its neighbour; line the lantern's own colour up all the way to
    /// a sleeping critter, and the vein between them lights.
    ///
    /// <para>
    /// <b>It authors the shared prototype block and leaves the deal empty</b>, exactly as
    /// Emberforge does and for the same reason: the field of gems is everything the level hands
    /// over, so its future is fixed and <see cref="ProtoSearch"/> can prove it. The three
    /// sitting side by side on one reader is the seam doing what it was built for - this is the
    /// twelfth mode it has carried and the parsing, the row counting, the bad-character message
    /// and the lazy par are all inherited untouched.
    /// </para>
    /// </summary>
    public sealed class PrismMode : ProtoMode
    {
        public override GameMode Mode => GameMode.Prism;

        protected override ProtoDto Block(LevelDto dto) => dto.prism;
        protected override string Letters => PrismLayout.Letters;

        /// <summary>
        /// The ground, the gems, the lanterns, the sleeping critters and the cast.
        ///
        /// <para>
        /// Listed here rather than derived from a board, for the reason Emberforge's list gives:
        /// which of three critters runs out of a husk is a drawing decision the view makes from
        /// the cell, so a board holding one sleeper still needs all three loadable, and a set
        /// that varied per level would be a scope changing under the player mid-chapter.
        /// </para>
        /// <para>
        /// <b>Ten sprites draw the whole board.</b> A ground tile and its lit twin, four gems, a
        /// lantern and its dark twin, and a husk - every cell is one of them drawn again, which
        /// is what leaves the budget for a cast that moves and for flares that are frames rather
        /// than particles.
        /// </para>
        /// </summary>
        static readonly AssetRequest[] Cast =
        {
            AssetRequest.Sprite(AssetManifest.PrismArt("moss")),
            AssetRequest.Sprite(AssetManifest.PrismArt("moss_lit")),
            AssetRequest.Sprite(AssetManifest.PrismArt("lamp")),
            AssetRequest.Sprite(AssetManifest.PrismArt("lamp_dark")),
            AssetRequest.Sprite(AssetManifest.PrismArt("husk")),
            AssetRequest.Sprite(AssetManifest.PrismArt("gem_r")),
            AssetRequest.Sprite(AssetManifest.PrismArt("gem_g")),
            AssetRequest.Sprite(AssetManifest.PrismArt("gem_b")),
            AssetRequest.Sprite(AssetManifest.PrismArt("gem_y")),

            AssetRequest.SpriteSet(AssetManifest.PrismArt("mon1")),
            AssetRequest.SpriteSet(AssetManifest.PrismArt("mon1_jump")),
            AssetRequest.SpriteSet(AssetManifest.PrismArt("mon2")),
            AssetRequest.SpriteSet(AssetManifest.PrismArt("mon2_jump")),
            AssetRequest.SpriteSet(AssetManifest.PrismArt("mon3")),
            AssetRequest.SpriteSet(AssetManifest.PrismArt("mon3_jump")),

            AssetRequest.SpriteSet(AssetManifest.PrismFx("flare_warm")),
            AssetRequest.SpriteSet(AssetManifest.PrismFx("flare_bloom")),
        };

        public override IReadOnlyList<AssetRequest> Art => Cast;

        protected override bool Compose(ProtoGrid grid, ProtoDto block, LevelId id,
                                        ICollection<string> problems, out ProtoLevelRules rules)
        {
            rules = null;

            if (!string.IsNullOrEmpty(block.cores))
            {
                problems.Add($"{id}: this board deals '{block.cores}', and nothing is ever dealt " +
                             "into a Prismvale board - what is authored is all there is, which " +
                             "is what makes par searchable at all");
                return false;
            }

            var layout = new PrismLayout(grid, block.spare);

            if (layout.Fault != null)
            {
                problems.Add($"{id}: {layout.Fault}");
                return false;
            }

            var board = PrismBoard.Build(layout);

            if (board.IsFinished)
            {
                problems.Add($"{id}: this board opens with every critter awake");
                return false;
            }

            // A board dealt with a vein already standing against a sleeper is a board whose
            // first move its author played - Budburst's "authored settled" rule. It matters
            // here because the goal count the player is graded against would already have
            // moved, so the board proved is not the board that opens.
            if (board.Stirred)
            {
                problems.Add($"{id}: a vein on this board is already touching a sleeping " +
                             "critter, so it would wake before anybody had moved a gem - a " +
                             "board is authored dark");
                return false;
            }

            if (!board.AnyMove)
            {
                problems.Add($"{id}: no two touching gems on this board are different colours, " +
                             "so there is no swap to make and the run is over before it begins");
                return false;
            }

            rules = new PrismRules(layout, block.spare);
            return true;
        }
    }

    /// <summary>
    /// Searches a prototype board for its par, once, and remembers the answer.
    ///
    /// <para>
    /// <b>Why the search runs on the phone at all.</b> Par decides both star lines and the
    /// allowance a run is dealt, so it has to be known before the first move — and it may not be
    /// authored, because a typed par drifts from the board it claims to describe and the drift has
    /// no symptom. Writing the number into the chapter body at authoring time is the same typed
    /// par with an extra step in front of it (invariant 5).
    /// </para>
    /// <para>
    /// <b>Cached on the level id rather than the layout</b>, for <c>KeeperSetup</c>'s reason: a
    /// chapter body is evicted on leaving the chapter and re-read on coming back (invariant 4a),
    /// so without this every trip in and out would re-search every board in it.
    /// </para>
    /// <para>
    /// <b>What happens when a search fails is the interesting half.</b> Nothing here can prove a
    /// board on a device that the build gate did not already prove on a build machine, so reaching
    /// the fallback means broken content shipped. The fallback is chosen to be impossible to lose
    /// to rather than to be accurate: par becomes the room the board has, which puts three stars,
    /// two stars and the allowance all above it. A player meets a generously graded level instead
    /// of an unwinnable one, and the log names the level.
    /// </para>
    /// </summary>
    public static class ProtoSetup
    {
        static readonly Dictionary<string, int> _par = new Dictionary<string, int>();

        public static int Par(string levelId, ProtoLevelRules rules)
        {
            if (rules == null) return 1;

            string key = levelId ?? string.Empty;
            if (key.Length > 0 && _par.TryGetValue(key, out int cached)) return cached;

            var answer = ProtoSearch.Solve(rules.Opening());
            int par = answer.Par;

            if (par < 1)
            {
                par = rules.Room > 0 ? rules.Room : 1;
                UnityEngine.Debug.LogError(
                    $"[{rules.Mode}] '{key}' could not be proved solvable inside " +
                    $"{ProtoSearch.NodeBudget} positions, which the build gate should have " +
                    $"refused. Grading it against the room it has ({par}) so it stays winnable; " +
                    "run Validate Content.");
            }

            if (key.Length > 0) _par[key] = par;
            return par;
        }

        /// <summary>
        /// Forgets everything. For the test suite and for the Editor's content refresh, which
        /// rebuilds the catalog inside one process — the only two places a level id can come to
        /// name a different board.
        /// </summary>
        public static void Forget() => _par.Clear();
    }
}
