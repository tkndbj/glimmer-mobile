/**
 * Keeper levels bought outright — the server half of invariant 57.
 *
 * A keeper level is derived from XP and XP from play (invariant 9). A *bought* level is the one
 * addend on top of that which is neither: an entitlement, a count of purchases, held on the
 * wallet document no client can write (`WalletDoc.keeperBought`) and mirrored into the save as
 * a monotonic integer the device draws from. The level a card carries is the earned level plus
 * that count; the rank ladder (`rungOf`) reads the earned level alone, because a badge is a
 * reading of play and a badge for sale is worth nothing to the people who earned theirs.
 *
 * Three things live here:
 *
 *  - `usableKeeperLadder` reads the published `keeperLevels` block, refusing the whole of it on
 *    any fault — mirrors `KeeperLadder.Resolve` on the client and the seeder's own reader, so a
 *    block one side accepts the others do too. **Absent sells nothing**, on every side: a ladder
 *    the server was never told about must not be sold on a device, or the debit is refused and
 *    the level taken back on the next sync, which is money shown and taken away.
 *  - `keeperPrice` is the arithmetic, and it exists three times (here, `KeeperLadder.PriceFor`,
 *    `Tools/make_keeper_vectors.py`), held together by `keeperCases` in the shared vectors.
 *    Integer throughout, rounding half up — nothing that decides a payment may be a float.
 *  - `parseKeeperSpendId` reads `keeper:{ordinal}:{level}` back. The ordinal is what is bought
 *    (the first bought level is 1); the level is the rung the device priced, in the id so this
 *    side prices the same rung, and refused when it is below what the save already proves.
 */

import type { ProgressionConfig } from "./progression";
import {
  DEFAULT_KEEPER_CURVE, KeeperCurve, derivedXp, endlessXp, keeperLevel, xpBoostXp,
} from "./grove";
import { challengeXp } from "./challenges";

// ------------------------------------------------------------------ the ladder
/** The two currencies a level may be priced in — the client's `Currency.Credits` / `.Gems`. */
export type KeeperCurrency = "credits" | "gems";

export interface KeeperAnchorConfig {
  /** The keeper level this anchor prices — the level a purchase *reaches*. */
  level: number;
  currency: KeeperCurrency;
  price: number;
}

/** The published block, mirrored from `KeeperLadderDto`. */
export interface KeeperLadderConfig {
  /** The highest level sold, and the level the last anchor names. */
  top: number;
  /** The corners of the ladder, lowest level first. */
  anchors: KeeperAnchorConfig[];
}

/** `KeeperLadderLimits` on the client, and the seeder's own bounds. */
export const KEEPER_LOWEST_LEVEL = 2;
export const KEEPER_MAX_TOP = 200;
export const KEEPER_MAX_ANCHORS = 16;
export const KEEPER_MAX_PRICE = 10_000_000;

function whole(raw: unknown, max: number): number | null {
  if (typeof raw !== "number" || !Number.isFinite(raw)) return null;
  const value = Math.floor(raw);
  if (value < 0 || value > max) return null;
  return value;
}

/**
 * The published ladder, or null when nothing is for sale: absent, or any fault at all. The
 * rules are `KeeperLadder.Resolve`'s to the letter — levels climb strictly from 2 to `top`,
 * the last anchor *is* the top, a currency is one of two, a price is 1..MAX, and a band never
 * gets cheaper as it climbs.
 */
export function usableKeeperLadder(raw: unknown): KeeperLadderConfig | null {
  if (!raw || typeof raw !== "object" || Array.isArray(raw)) return null;
  const block = raw as Partial<KeeperLadderConfig>;

  const top = whole(block.top, KEEPER_MAX_TOP);
  if (top === null || top < KEEPER_LOWEST_LEVEL) return null;

  if (!Array.isArray(block.anchors) || block.anchors.length === 0) return null;
  if (block.anchors.length > KEEPER_MAX_ANCHORS) return null;

  const anchors: KeeperAnchorConfig[] = [];
  let last: KeeperAnchorConfig | null = null;

  for (const row of block.anchors as Partial<KeeperAnchorConfig>[]) {
    if (!row || typeof row !== "object") return null;

    const level = whole(row.level, top);
    const price = whole(row.price, KEEPER_MAX_PRICE);
    const currency = row.currency === "credits" || row.currency === "gems" ? row.currency : null;

    if (level === null || price === null || currency === null) return null;
    if (level < KEEPER_LOWEST_LEVEL || price < 1) return null;
    if (last && level <= last.level) return null;
    if (last && last.currency === currency && price < last.price) return null;

    last = { level, currency, price };
    anchors.push(last);
  }

  if (!last || last.level !== top) return null;
  return { top, anchors };
}

/**
 * The point `step` of `steps` along the line from `a` to `b`, rounded half up, in integers —
 * `KeeperLadder.Between` line for line. `b` is never below `a` (the reader refuses a falling
 * band), so the numerator is never negative and the division truncates toward the answer.
 */
export function between(a: number, b: number, step: number, steps: number): number {
  if (steps <= 0) return a;
  const num = (b - a) * step;
  return a + Math.floor((2 * num + steps) / (2 * steps));
}

/**
 * What reaching `level` costs, or null when that level is not sold: below the first anchor,
 * above the top, or on no ladder at all. On the straight line between two anchors of one
 * currency; flat at the lower anchor between two of different currencies.
 */
export function keeperPrice(
  ladder: KeeperLadderConfig | null,
  level: number
): { currency: KeeperCurrency; price: number } | null {
  if (!ladder || ladder.anchors.length === 0) return null;
  if (!Number.isInteger(level)) return null;
  if (level < ladder.anchors[0].level || level > ladder.top) return null;

  let i = 0;
  while (i + 1 < ladder.anchors.length && ladder.anchors[i + 1].level <= level) i++;

  const lower = ladder.anchors[i];
  if (i + 1 >= ladder.anchors.length || lower.level === level) {
    return { currency: lower.currency, price: lower.price };
  }

  const upper = ladder.anchors[i + 1];
  if (lower.currency !== upper.currency) {
    return { currency: lower.currency, price: lower.price };
  }

  return {
    currency: lower.currency,
    price: between(lower.price, upper.price, level - lower.level, upper.level - lower.level),
  };
}

// ------------------------------------------------------------------ the debit
/** `keeper:{ordinal}:{level}` — minted by `SpendEntry.KeeperLevelId`. */
export interface KeeperSpend {
  /** Which bought level this is: the first is 1. What the wallet counts. */
  ordinal: number;
  /** The keeper level the purchase reached as the device saw it: earned + ordinal. */
  level: number;
}

export function isKeeperSpendId(id: string): boolean {
  return typeof id === "string" && id.startsWith("keeper:");
}

/**
 * Reads a keeper debit back, or null. Strict on both numbers for `parseChallengeTierSpendId`'s
 * reason: two spellings of one purchase would be two purchases. Exactly three parts, two plain
 * positive integers, and a level at least one above the ordinal — nobody stands below level 1.
 */
export function parseKeeperSpendId(id: string): KeeperSpend | null {
  if (!isKeeperSpendId(id) || id.length > 32) return null;

  const parts = id.split(":");
  if (parts.length !== 3) return null;

  const ordinal = strictInt(parts[1]);
  const level = strictInt(parts[2]);
  if (ordinal === null || level === null || ordinal <= 0 || level <= ordinal) return null;

  return { ordinal, level };
}

function strictInt(raw: string): number | null {
  if (!/^[1-9]\d{0,8}$/.test(raw)) return null;
  return Number(raw);
}

/**
 * Why a keeper debit is refused, or `ok` with the price it is held to. A pure function so the
 * transaction in `submitSpends` is one call and the rule is testable without a Firestore.
 *
 *  - `not_sold`: no usable ladder, or the level is off it.
 *  - `out_of_order`: the ordinal is not the next one this wallet has not bought. The next is
 *    `bought + 1`; anything above skips a purchase, anything at or below names a purchase the
 *    wallet already holds under a different level suffix (another device's), and the client
 *    takes its own copy back on the refusal while the merged save already carries the level.
 *  - `under_earned`: the level named is below what the save this server holds already proves
 *    plus the ordinal — a client naming a cheaper rung than the one it stands on. A level
 *    *above* that is never refused: a save one sync behind can only understate what was earned.
 *  - `underpaid`: the wrong currency, or too little of the right one.
 */
export type KeeperVerdict =
  | { ok: true; currency: KeeperCurrency; price: number }
  | { ok: false; reason: "not_sold" | "out_of_order" | "under_earned" | "underpaid"; price?: number };

export function judgeKeeperSpend(
  ladder: KeeperLadderConfig | null,
  spend: KeeperSpend,
  bought: number,
  earnedLevel: number,
  paidCurrency: string,
  paidAmount: number
): KeeperVerdict {
  if (spend.ordinal !== Math.max(0, Math.floor(bought)) + 1) return { ok: false, reason: "out_of_order" };
  if (spend.level < Math.max(1, Math.floor(earnedLevel)) + spend.ordinal) {
    return { ok: false, reason: "under_earned" };
  }

  const owed = keeperPrice(ladder, spend.level);
  if (!owed) return { ok: false, reason: "not_sold" };

  if (paidCurrency !== owed.currency || paidAmount < owed.price) {
    return { ok: false, reason: "underpaid", price: owed.price };
  }

  return { ok: true, currency: owed.currency, price: owed.price };
}

// ------------------------------------------------------------------ the level
/**
 * The keeper level a save's XP alone pays for — the star ledger, the Infinite lane and the
 * daily challenges, with the boost clamped against their sum, run through the published curve.
 * `publishGrove`'s own derivation, moved here so a debit and a card cannot disagree about it.
 */
export function earnedKeeperLevel(save: Record<string, unknown>, config: ProgressionConfig): number {
  const curve = (config as { keeper?: KeeperCurve }).keeper ?? DEFAULT_KEEPER_CURVE;
  const provable = derivedXp(save.levels, config) + endlessXp(save, config) + challengeXp(save, config);
  return keeperLevel(provable + xpBoostXp(save, config, provable), curve);
}

/** The bought count a wallet document holds. Nought for anything that is not a count. */
export function keeperBoughtOf(raw: unknown): number {
  if (!raw || typeof raw !== "object") return 0;
  const value = (raw as { keeperBought?: unknown }).keeperBought;
  if (typeof value !== "number" || !Number.isFinite(value)) return 0;
  return Math.max(0, Math.floor(value));
}
