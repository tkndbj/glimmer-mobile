using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Social
{
    /// <summary>
    /// Normal's best-per-level score. Targets are frozen content, never today's difficulty.
    /// No save fields or currency: a merged set of bests always derives the same total.
    /// Mirrored by functions/src/normal-scores.ts and the shared normal-score vectors.
    /// </summary>
    public sealed class NormalScores
    {
        public const int CompletionPoints = 1000, StarPoints = 100, EfficiencyPoints = 200;
        public const int MaxLevels = 5000, MaxTarget = 1000000;
        readonly Dictionary<string, int> _targets = new Dictionary<string, int>(StringComparer.Ordinal);
        public int Version { get; private set; }
        public static readonly NormalScores Empty = new NormalScores();

        public static NormalScores Resolve(NormalScoringDto dto, List<string> problems)
        {
            if (dto == null) return Empty;
            var result = new NormalScores();
            if (dto.version != 1 || dto.levels == null || dto.levels.Length == 0 || dto.levels.Length > MaxLevels)
            {
                problems?.Add("normalScoring: unsupported version or level count");
                return Empty;
            }
            foreach (var row in dto.levels)
            {
                if (row == null || !LevelId.TryParse(row.level, out _, out _)
                    || row.target < 1 || row.target > MaxTarget || result._targets.ContainsKey(row.level))
                {
                    problems?.Add("normalScoring: invalid or duplicate level target");
                    return Empty;
                }
                result._targets.Add(row.level, row.target);
            }
            result.Version = dto.version;
            return result;
        }

        public static int Points(int stars, int moves, int target)
        {
            if (stars <= 0 || target < 1 || target > MaxTarget) return 0;
            stars = Math.Min(LevelRecord.MaxStars, stars);
            // Zero means missing moves, never an instant clear. A run at half the frozen
            // three-star target earns the capped bonus; each better move can only add points.
            long bonus = moves > 0
                ? Math.Min(EfficiencyPoints, Math.Max(0L, 2L * EfficiencyPoints * (target - (long)moves) / target))
                : 0L;
            return CompletionPoints + StarPoints * stars + (int)bonus;
        }

        public long In(SaveFileDto save, CatalogIndex index)
        {
            if (save?.levels == null || index == null) return 0L;
            // Files from two devices can contain the same id before their join. Count once,
            // taking independent bests exactly as SaveMerge does.
            var bests = new Dictionary<string, (int stars, int moves)>(StringComparer.Ordinal);
            foreach (var row in save.levels)
            {
                if (row == null || row.levelId == null || !_targets.ContainsKey(row.levelId)) continue;
                if (!LevelId.TryParse(row.levelId, out var id, out _) || !index.Contains(id)
                    || !index.TrackOf(id).IsMain) continue;
                bests.TryGetValue(row.levelId, out var held);
                int moves = row.bestMoves > 0 && (held.moves <= 0 || row.bestMoves < held.moves)
                    ? row.bestMoves : held.moves;
                bests[row.levelId] = (Math.Max(held.stars, row.stars), moves);
            }
            long total = 0L;
            foreach (var row in bests)
                total += Points(row.Value.stars, row.Value.moves, _targets[row.Key]);
            return total;
        }

        public long ForPlayer(CatalogIndex index)
        {
            if (index == null) return 0L;
            long total = 0L;
            foreach (var record in PlayerProgress.Records)
                if (_targets.TryGetValue(record.Id.Value, out int target)
                    && index.Contains(record.Id) && index.TrackOf(record.Id).IsMain)
                    total += Points(record.Stars, record.BestMoves, target);
            return total;
        }
    }

    [Serializable]
    public sealed class NormalScoringDto
    {
        public int version;
        public NormalTargetDto[] levels;
    }

    [Serializable]
    public sealed class NormalTargetDto
    {
        public string level;
        public int target;
    }
}
