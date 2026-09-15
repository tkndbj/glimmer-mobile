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
        /// stone is gone, a stopper cannot go back, three marks have left the grove, a pod is
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
    /// carried twelve and carries one.
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
    /// <b>It authors the shared prototype block and leaves the deal empty</b>, and the reason
    /// is the one every mode on this block has had: the field of gems is everything the level
    /// hands over, so its future is fixed and <see cref="ProtoSearch"/> can prove it. It is the
    /// twelfth mode this reader has carried and the only one still on it - the parsing, the row
    /// counting, the bad-character message and the lazy par are all inherited untouched, which
    /// is the seam doing what it was built for.
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
        /// Listed here rather than derived from a board, and the reason is the one every mode
        /// here has had:
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
