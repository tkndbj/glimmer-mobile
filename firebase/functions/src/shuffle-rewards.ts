/**
 * Shuffle's fixed payout contract, mirrored by ShuffleRewards.cs and shared vectors.
 * Best wave and grade never pay. Only post-v44 cleared-wave counters contribute.
 * Per-installation max, then sum, preserves concurrent offline play and is idempotent.
 * These are client-reported gameplay counts; arithmetic validation is not proof of play.
 */
export const SHUFFLE_REWARDS = {
  xpPerWave: 10,
  creditsPerWave: 10,
  maxRows: 64,
  maxDevices: 64,
  maxDeviceIdLength: 64,
  maxWavesPerDevice: 1_000_000_000,
} as const;

function counters(raw: unknown): Map<string, number> {
  const counts = new Map<string, number>();
  if (!Array.isArray(raw)) return counts;
  for (const row of raw.slice(0, SHUFFLE_REWARDS.maxDevices)) {
    if (!row || typeof row !== "object") continue;
    const { device, waves } = row;
    if (typeof device !== "string" || device.length === 0 ||
        device.length > SHUFFLE_REWARDS.maxDeviceIdLength || !/^[a-z0-9_-]+$/.test(device)) continue;
    if (typeof waves !== "number" || !Number.isFinite(waves) || waves <= 0) continue;
    const value = Math.min(Math.floor(waves), SHUFFLE_REWARDS.maxWavesPerDevice);
    if (value > (counts.get(device) ?? 0)) counts.set(device, value);
  }
  return counts;
}

/** Reads the same canonical rows as ShuffleLedger.Join + ShuffleRewards.WavesIn. */
export function shuffleWaves(save: Record<string, unknown>, levelChapters?: Record<string, string>): number {
  if (!Array.isArray(save.shuffleBest)) return 0;
  const levels = new Map<string, Map<string, number>>();
  for (const row of save.shuffleBest.slice(0, SHUFFLE_REWARDS.maxRows)) {
    if (!row || typeof row !== "object" || typeof row.level !== "string" ||
        row.level.length === 0 || row.level.length > 48) continue;
    if (levelChapters && !Object.hasOwn(levelChapters, row.level)) continue;
    const next = counters(row.rewards);
    const held = levels.get(row.level) ?? new Map<string, number>();
    for (const [device, waves] of next) held.set(device, Math.max(held.get(device) ?? 0, waves));
    levels.set(row.level, new Map([...held].sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0)
      .slice(0, SHUFFLE_REWARDS.maxDevices)));
  }
  let total = 0;
  for (const counts of levels.values()) for (const waves of counts.values()) total += waves;
  return total;
}

export function shuffleXp(save: Record<string, unknown>): number {
  return shuffleWaves(save) * SHUFFLE_REWARDS.xpPerWave;
}

export function shuffleCredits(save: Record<string, unknown>, levelChapters?: Record<string, string>): number {
  return shuffleWaves(save, levelChapters) * SHUFFLE_REWARDS.creditsPerWave;
}
