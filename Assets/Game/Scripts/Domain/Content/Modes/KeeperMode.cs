using System.Collections.Generic;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// Grovekeeper: tiles laid out so that unlike edges bloom.
    ///
    /// The one authored number is how many tiles the run hands out, which is the level — the
    /// same shape every level-based puzzle game uses, and the only figure a retune would reach
    /// for.
    /// </summary>
    public sealed class KeeperMode : LevelMode
    {
        public override GameMode Mode => GameMode.Keeper;

        public override bool Claims(LevelDto dto) => dto.keeper != null && dto.keeper.IsAuthored;

        public override bool TryRead(LevelDto dto, LevelId id, ICollection<string> problems,
                                     out ILevelRules rules)
        {
            var grove = dto.keeper;
            rules = new KeeperRules(
                grove.width > 0 ? grove.width : 9,
                grove.height > 0 ? grove.height : 9,
                grove.tiles > 0 ? grove.tiles : 30,
                grove.seed);
            return true;
        }

        public override LevelTuning Tune(LevelDto dto, ILevelRules rules) => LevelTuning.Default(1);

        public override string RecordStem => "ui.rank.points";
    }

    /// <summary>A grove: how big the ground is, and how many tiles the run gets.</summary>
    public sealed class KeeperRules : ILevelRules
    {
        public readonly int Width, Height, Tiles, Seed;

        public KeeperRules(int width, int height, int tiles, int seed)
        {
            Width = width;
            Height = height;
            Tiles = tiles;
            Seed = seed;
        }

        public GameMode Mode => GameMode.Keeper;

        public uint SeedFor(LevelId id) => ContentSeed.For(Seed, id);
    }
}
