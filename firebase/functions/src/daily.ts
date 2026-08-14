/**
 * The server's copy of the daily chest rules.
 *
 * This is a deliberate second implementation of the client's `DailyChests`, and it
 * exists for the same reason `progression.ts` duplicates `ProgressionLedger`: currency
 * that was *given* rather than earned is the one thing a client must never be able to
 * decide for itself. A client that names its own reward names any number it likes.
 *
 * So the client's claim carries no authority beyond "I opened day D's chest N". The
 * server re-rolls that chest here, from the account id, the day and the index, and
 * grants its own answer. The client's amount is recorded alongside for support — a
 * disagreement is worth seeing — and never used.
 *
 * The two halves are held together by `firebase/shared/reward-vectors.json`, which both
 * sides run as a test. Every constant below — the stream numbers, the hash, the shift
 * amounts, the modulo — is part of that contract. Changing any of them re-rolls every
 * unopened chest in the world and desynchronises the two implementations; do it only
 * with a vector proving what changed.
 *
 * Everything is 32-bit integer arithmetic, which JavaScript does exactly. Anything
 * wider would land inside the 53-bit mantissa and the two sides would agree on most
 * inputs and not all — the worst possible failure mode for money.
 */

export const DROP_KINDS = ["credits", "gems", "hearts", "heart_boost"] as const;
export type DropKind = (typeof DROP_KINDS)[number];

export interface DropBand {
  kind: DropKind;
  min: number;
  max: number;
}

export interface DropOption extends DropBand {
  weight: number;
}

export interface ChestConfig {
  guaranteed: DropBand[];
  options: DropOption[];
}

export interface DailyConfig {
  runsPerChest: number;
  chests: ChestConfig[];
}

/** Matches `DailyRules.SecondsPerDay`. */
export const SECONDS_PER_DAY = 86400;

/**
 * How far ahead of the server's own day a claim may be dated.
 *
 * One day, to tolerate a device whose clock is a little fast crossing midnight. Beyond
 * that a client would be pre-farming days it has not lived through. There is no lower
 * bound worth having — an id can only ever pay once, so an old claim is simply a player
 * who has been offline — but the far bound keeps the grant log from being usable as
 * unbounded storage.
 */
export const MAX_DAYS_AHEAD = 1;
export const MAX_DAYS_BEHIND = 400;

/** Matches `ChestDefinition`'s stream numbering. Never renumber these. */
const STREAM_PICK = 0;
const STREAM_AMOUNT = 1;
const streamForGuaranteed = (index: number): number => 100 + index;

const FNV_OFFSET_BASIS = 2166136261;
const FNV_PRIME = 16777619;

// -------------------------------------------------------------------- the generator
function absorbByte(hash: number, byte: number): number {
  return Math.imul(hash ^ (byte & 0xff), FNV_PRIME) >>> 0;
}

function absorbChar(hash: number, code: number): number {
  return absorbByte(absorbByte(hash, code & 0xff), (code >> 8) & 0xff);
}

function absorbString(hash: number, text: string): number {
  let h = hash;
  for (let i = 0; i < text.length; i++) h = absorbChar(h, text.charCodeAt(i));
  return h;
}

/** Decimal digits, most significant first — the form a human would write. */
function absorbInt(hash: number, value: number): number {
  let h = hash;
  let v = Math.trunc(value);

  if (v < 0) {
    h = absorbChar(h, 45);                        // '-'
    v = -v;
  }

  let divisor = 1;
  while (Math.floor(v / divisor) >= 10) divisor *= 10;

  while (divisor > 0) {
    h = absorbChar(h, 48 + (Math.floor(v / divisor) % 10));
    divisor = Math.floor(divisor / 10);
  }

  return h;
}

function seed(playerKey: string, dayKey: number, chestIndex: number, stream: number): number {
  let hash = FNV_OFFSET_BASIS >>> 0;

  hash = absorbString(hash, playerKey ?? "");
  hash = absorbChar(hash, 124);                   // '|'
  hash = absorbInt(hash, dayKey);
  hash = absorbChar(hash, 124);
  hash = absorbInt(hash, chestIndex);
  hash = absorbChar(hash, 124);
  hash = absorbInt(hash, stream);

  // xorshift32's one fixed point is zero, and it would return zero forever.
  return hash === 0 ? FNV_OFFSET_BASIS >>> 0 : hash;
}

class ChestRandom {
  private state: number;

  constructor(playerKey: string, dayKey: number, chestIndex: number, stream: number) {
    this.state = seed(playerKey, dayKey, chestIndex, stream);
  }

  /** xorshift32, exactly as Marsaglia gave it. */
  next(): number {
    let x = this.state;
    x = (x ^ (x << 13)) >>> 0;
    x = (x ^ (x >>> 17)) >>> 0;
    x = (x ^ (x << 5)) >>> 0;
    this.state = x;
    return x;
  }

  below(bound: number): number {
    return bound <= 1 ? 0 : this.next() % bound;
  }

  between(min: number, max: number): number {
    return max <= min ? min : min + this.below(max - min + 1);
  }
}

// ------------------------------------------------------------------------ the roll
export interface RolledDrop {
  kind: DropKind;
  amount: number;
}

function clampBand(band: DropBand): DropBand {
  const min = band.min < 1 ? 1 : Math.floor(band.min);
  const max = band.max < min ? min : Math.floor(band.max);
  return { kind: band.kind, min, max };
}

/**
 * Everything in one chest, for one account, on one day.
 *
 * Mirrors `ChestDefinition.Roll`: each guaranteed band on its own stream, then exactly
 * one weighted pick. The single pick is what makes the odds a list of percentages that
 * sums to a hundred, which is a thing that can be published.
 */
export function rollChest(
  config: DailyConfig,
  playerKey: string,
  dayKey: number,
  chestIndex: number
): RolledDrop[] {
  const chest = config.chests[chestIndex];
  if (!chest) return [];

  const drops: RolledDrop[] = [];

  /**
   * Folds a drop in, adding to an existing entry of the same kind.
   *
   * Mirrors `DailyChestTable.Merge`, and it is load-bearing rather than tidy: a chest
   * whose floor and whose bonus are both credits awards one id, so the two halves have
   * to be one number on both sides or the client and the server disagree about a wallet.
   */
  const merge = (kind: DropKind, amount: number): void => {
    if (amount <= 0) return;

    const existing = drops.find((drop) => drop.kind === kind);
    if (existing) existing.amount += amount;
    else drops.push({ kind, amount });
  };

  const guaranteed = chest.guaranteed ?? [];
  for (let i = 0; i < guaranteed.length; i++) {
    const band = clampBand(guaranteed[i]);
    const random = new ChestRandom(playerKey, dayKey, chestIndex, streamForGuaranteed(i));
    merge(band.kind, random.between(band.min, band.max));
  }

  const options = chest.options ?? [];
  let totalWeight = 0;
  for (const option of options) totalWeight += option.weight < 1 ? 1 : Math.floor(option.weight);

  if (options.length > 0 && totalWeight > 0) {
    const chooser = new ChestRandom(playerKey, dayKey, chestIndex, STREAM_PICK);
    let target = chooser.below(totalWeight);

    let chosen = options[options.length - 1];
    for (const option of options) {
      target -= option.weight < 1 ? 1 : Math.floor(option.weight);
      if (target < 0) { chosen = option; break; }
    }

    const band = clampBand(chosen);
    const amountRandom = new ChestRandom(playerKey, dayKey, chestIndex, STREAM_AMOUNT);
    merge(band.kind, amountRandom.between(band.min, band.max));
  }

  return drops;
}

/** What one chest is worth in one currency. Zero when it holds none of it. */
export function chestCurrencyValue(
  config: DailyConfig,
  playerKey: string,
  dayKey: number,
  chestIndex: number,
  currency: string
): number {
  let total = 0;
  for (const drop of rollChest(config, playerKey, dayKey, chestIndex)) {
    if (drop.kind === currency) total += drop.amount;
  }
  return total;
}

// -------------------------------------------------------------------- claim parsing
export interface DailyClaim {
  dayKey: number;
  chestIndex: number;
  currency: string;
}

/**
 * Reads a grant id back into what it claims.
 *
 * The format is `daily:{day}:{chest}:{currency}` and it is produced by
 * `GrantEntry.DailyChestId` on the client. Parsing rather than trusting a structured
 * payload is deliberate: the id is what the database keys on, so the id has to be the
 * thing that is validated. Anything else leaves room for a request whose id and whose
 * fields disagree.
 */
export function parseDailyClaim(id: string): DailyClaim | null {
  if (typeof id !== "string" || id.length > 64) return null;

  const parts = id.split(":");
  if (parts.length !== 4 || parts[0] !== "daily") return null;

  const dayKey = Number(parts[1]);
  const chestIndex = Number(parts[2]);
  const currency = parts[3];

  if (!Number.isInteger(dayKey) || dayKey < 0) return null;
  if (!Number.isInteger(chestIndex) || chestIndex < 0 || chestIndex > 32) return null;
  if (!currency || currency.length > 24) return null;

  // The canonical form has to round-trip, or two ids could name one chest — "daily:07:1"
  // and "daily:7:1" would key two documents and pay twice.
  if (`daily:${dayKey}:${chestIndex}:${currency}` !== id) return null;

  return { dayKey, chestIndex, currency };
}

export function todayKey(nowMillis: number): number {
  return Math.floor(nowMillis / 1000 / SECONDS_PER_DAY);
}

/** Guards a config document that predates the daily block, or was seeded badly. */
export function usableDailyConfig(config: unknown): DailyConfig | null {
  const c = config as DailyConfig | undefined;

  if (!c || typeof c !== "object") return null;
  if (!Array.isArray(c.chests) || c.chests.length === 0) return null;
  if (typeof c.runsPerChest !== "number" || c.runsPerChest < 1) return null;

  for (const chest of c.chests) {
    if (!chest || !Array.isArray(chest.guaranteed) || chest.guaranteed.length === 0) return null;
    if (chest.options !== undefined && !Array.isArray(chest.options)) return null;
  }

  return c;
}
