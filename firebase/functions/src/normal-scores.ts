/** Normal score v1. Frozen three-star targets, integer arithmetic, bests counted once.
 * Client-reported achievements, not proof of gameplay. Never used to award currency.
 */
export interface NormalScoring {
  version: 1;
  levels: { level: string; target: number }[];
}

export const NORMAL_BOARD = "normal";
export const NORMAL_REBUILD_MINUTES = 1;
export const NORMAL_MAX_LEVELS = 5000;
export const NORMAL_MAX_TARGET = 1_000_000;

export function usableNormalScoring(raw: unknown): raw is NormalScoring {
  if (!raw || typeof raw !== "object") return false;
  const block = raw as Partial<NormalScoring>;
  if (block.version !== 1 || !Array.isArray(block.levels)
      || block.levels.length === 0 || block.levels.length > NORMAL_MAX_LEVELS) return false;
  const ids = new Set<string>();
  for (const row of block.levels) {
    if (!row || typeof row.level !== "string" || !/^[a-z0-9](?:[a-z0-9_]{0,46}[a-z0-9])?$/.test(row.level)
        || !Number.isSafeInteger(row.target) || row.target < 1 || row.target > NORMAL_MAX_TARGET
        || ids.has(row.level)) return false;
    ids.add(row.level);
  }
  return true;
}

export function normalPoints(stars: number, moves: number, target: number): number {
  if (!Number.isSafeInteger(stars) || stars <= 0 || !Number.isSafeInteger(target)
      || target < 1 || target > NORMAL_MAX_TARGET) return 0;
  const bonus = Number.isSafeInteger(moves) && moves > 0
    ? Math.min(200, Math.max(0, Math.trunc(400 * (target - moves) / target))) : 0;
  return 1000 + 100 * Math.min(3, stars) + bonus;
}

export function normalScore(levels: unknown, rawConfig: unknown): number {
  if (!usableNormalScoring(rawConfig) || !levels || typeof levels !== "object" || Array.isArray(levels)) return 0;
  const records = levels as Record<string, { stars?: unknown; bestMoves?: unknown } | null>;
  let total = 0;
  // Walk the trusted, bounded catalog, not arbitrary client keys.
  for (const row of rawConfig.levels) {
    const record = Object.hasOwn(records, row.level) ? records[row.level] : null;
    if (!record || typeof record !== "object") continue;
    total += normalPoints(typeof record.stars === "number" ? record.stars : 0,
                          typeof record.bestMoves === "number" ? record.bestMoves : 0, row.target);
  }
  return total;
}
