/**
 * The daily challenges' server half: what a cleared level is worth, what a deal allows, and
 * how a claim and a deal debit are read back.
 *
 * Three rules live here and every one has a twin on the client (invariant 56g):
 *
 *  - `challengeXp` turns a save's `challenges.clears` rows into XP — the third source of XP in
 *    this game that is not a star, built exactly as the Infinite lane's was (`endlessXp`): a
 *    monotonic lifetime tally, a rate and a ceiling. Mirrors `ChallengeRewardRule.XpFor`, pinned
 *    by `challengeCases` in the shared vectors. A drift is silent — `buildCard` drops whatever
 *    the lower keeper level gated (19a).
 *  - `allowanceOn` turns the deals the *wallet* holds into the plays a day allows. Mirrors
 *    `ChallengeAllowance.On`, pinned by `challengeAllowanceCases`. A drift is a coin claim
 *    refused for a play the page offered, which is money shown and taken back (45d).
 *  - `challengeGrant` prices a coin claim: the published rate if the win's ordinal is inside
 *    that day's allowance, nought otherwise. **The client's figure is never consulted.** Every
 *    number here is either published or server-held, which is what makes a cleared puzzle
 *    *adjudicated* rather than merely bounded (invariant 13's second clause rather than its
 *    fourth) — the difference between this and the Infinite lane, whose wave count nothing
 *    can re-price.
 *
 * What the server never sees is a board. A level is content the device holds and a challenge
 * id reaches no document; the count of wins is the whole of what is adjudicated.
 */

/** Mirrors `ChallengeLimits.HardMaxClears`. */
export const HARD_MAX_CLEARS = 1_000_000;

/** Mirrors `ChallengeLimits.MaxClearRows`, which is also the rules' bound on the list. */
export const MAX_CLEAR_ROWS = 64;

/** A genre spelling or a tier id: 1–32 lower-case letters, digits or underscores. Mirrors `ChallengeTable.IsValidTierId`. */
const KEY = /^[a-z0-9_]{1,32}$/;

/** Mirrors `ChallengeLimits`' published bounds. */
export const MAX_FREE_PLAYS = 100;
export const MAX_TIER_PLAYS = 1000;
export const MAX_TIER_GEMS = 100000;
export const MAX_TIER_DAYS = 365;
export const MAX_TIERS = 16;
export const MAX_COINS_PER_CLEAR = 200;
export const MAX_XP_PER_CLEAR = 1000;

/**
 * What ships in the build when no `challenges` block has been published. Mirrors
 * `ChallengeLimits.Default*`; both sides assert these through the shared vectors, so a stale
 * deploy agrees with the client instead of quietly publishing a smaller player.
 */
export const DEFAULT_CHALLENGES = {
  freePlays: 2,
  coins: 40,
  xp: 20,
  maxClears: 25000,
} as const;

/** One deal, as the seeder publishes it out of `challenges.json`. */
export interface ChallengeTierConfig {
  id: string;
  gems: number;
  plays: number;
  days: number;
}

/**
 * The `challenges` block of `config/progression`: the genre spellings the file ships, the free
 * allowance, the deal rows and the two reward rates. Published by `seed-config.mjs` out of
 * `challenges.json`; the boards never leave the device.
 */
export interface ChallengesConfig {
  genres: string[];
  freePlays: number;
  tiers: ChallengeTierConfig[];
  coins: number;
  xp: number;
  maxClears: number;
}

/**
 * The block as claims are priced against it, or null when it is not usable.
 *
 * **A block the seeder never published is null and a claim against it is left unconfirmed**
 * (13a); a block with a rate and no ceiling, or a ladder that does not climb, is refused by the
 * seeder before it can be published, so this reader only has to say no to a document somebody
 * edited by hand.
 */
export function usableChallengesConfig(raw: unknown): ChallengesConfig | null {
  if (!raw || typeof raw !== "object" || Array.isArray(raw)) return null;
  const block = raw as Partial<ChallengesConfig>;

  const genres = Array.isArray(block.genres)
    ? block.genres.filter((g): g is string => typeof g === "string" && KEY.test(g))
    : [];

  const freePlays = whole(block.freePlays, MAX_FREE_PLAYS);
  const coins = whole(block.coins, MAX_COINS_PER_CLEAR);
  const xp = whole(block.xp, MAX_XP_PER_CLEAR);
  const maxClears = whole(block.maxClears, HARD_MAX_CLEARS);
  if (freePlays === null || coins === null || xp === null || maxClears === null) return null;

  const tiers: ChallengeTierConfig[] = [];
  const seen = new Set<string>();
  if (Array.isArray(block.tiers)) {
    if (block.tiers.length > MAX_TIERS) return null;
    let lastPlays = freePlays, lastGems = 0;
    for (const row of block.tiers as Partial<ChallengeTierConfig>[]) {
      if (!row || typeof row !== "object") return null;
      const id = typeof row.id === "string" && KEY.test(row.id) ? row.id : null;
      const gems = whole(row.gems, MAX_TIER_GEMS);
      const plays = whole(row.plays, MAX_TIER_PLAYS);
      const days = whole(row.days, MAX_TIER_DAYS);
      if (id === null || gems === null || plays === null || days === null) return null;
      if (gems <= 0 || plays <= 0 || days <= 0) return null;
      if (seen.has(id) || plays <= lastPlays || gems <= lastGems) return null;
      seen.add(id);
      lastPlays = plays;
      lastGems = gems;
      tiers.push({ id, gems, plays, days });
    }
  }

  return { genres, freePlays, tiers, coins, xp, maxClears };
}

/** A non-negative whole number at or under `max`, or null. `Number` alone accepts too much. */
function whole(raw: unknown, max: number): number | null {
  if (typeof raw !== "number" || !Number.isFinite(raw)) return null;
  const value = Math.floor(raw);
  if (value < 0 || value > max) return null;
  return value;
}

// --------------------------------------------------------------------------- the XP
/**
 * Lifetime clears off a save's rows: every genre summed, a withdrawn one included, each row
 * clamped at the structural ceiling and the walk bounded at the rules' own cap.
 *
 * Mirrors `ChallengeLedger.LifetimeClearsIn`. The cap bounds the *walk*, malformed rows
 * included — the endless rule's reading, pinned by the vectors.
 */
export function challengeClears(save: Record<string, unknown>): number {
  const block = save.challenges;
  if (!block || typeof block !== "object" || Array.isArray(block)) return 0;

  const rows = (block as { clears?: unknown }).clears;
  if (!Array.isArray(rows)) return 0;

  let total = 0;
  for (const raw of rows.slice(0, MAX_CLEAR_ROWS)) {
    const row = raw as { genre?: unknown; count?: unknown } | null;
    if (!row || typeof row !== "object") continue;

    const genre = typeof row.genre === "string" ? row.genre : "";
    if (!KEY.test(genre)) continue;

    const count = Math.floor(Number(row.count ?? 0));
    if (!Number.isFinite(count) || count <= 0) continue;

    total += Math.min(count, HARD_MAX_CLEARS);
  }

  return total;
}

/**
 * What a save's clears are worth in XP, under the published block or the built-in figures.
 *
 * Absent falls back to the built-in rate rather than to nothing, for `endlessXp`'s reason: a
 * server one deploy behind would otherwise publish every challenge player short. A written
 * nought in either field pays nothing, as authored — the client reads the same block the same
 * way (`ChallengeRewardRule.Resolve`), and the two must agree byte for byte.
 */
export function challengeXp(save: Record<string, unknown>, config: { challenges?: unknown }): number {
  const rule = (config.challenges && typeof config.challenges === "object")
    ? config.challenges as { xp?: unknown; maxClears?: unknown }
    : DEFAULT_CHALLENGES;

  const rate = Math.floor(Number(rule.xp ?? 0));
  const ceiling = Math.floor(Number(rule.maxClears ?? 0));
  if (!Number.isFinite(rate) || rate <= 0) return 0;
  if (!Number.isFinite(ceiling) || ceiling <= 0) return 0;

  const clears = Math.min(challengeClears(save), ceiling);
  return clears * rate;
}

// --------------------------------------------------------------------------- the deals
/** Tier id → the day it was last bought, as the wallet document holds it. */
export type ChallengeTiersHeld = Record<string, number>;

/**
 * Reads the held deals off a wallet, dropping anything that is not one.
 *
 * Carried through `readWallet` like every other field on that document, because every writer
 * writes it **whole** — a field the reader does not copy is one the next sync deletes, and
 * deleting this one refuses every paid play the account bought.
 */
export function readChallengeTiers(raw: unknown): ChallengeTiersHeld {
  const held: ChallengeTiersHeld = {};
  if (!raw || typeof raw !== "object" || Array.isArray(raw)) return held;

  for (const [id, day] of Object.entries(raw as Record<string, unknown>)) {
    if (!KEY.test(id)) continue;
    if (typeof day !== "number" || !Number.isFinite(day)) continue;
    const from = Math.floor(day);
    if (from <= 0) continue;
    held[id] = from;
  }

  return held;
}

/**
 * The running deal with the most plays on `day`, or null.
 *
 * **A window is a day here and an instant on the client, on purpose** (56h). The save holds
 * the instant a window began and the device covers exactly `days` of the clock from it; this
 * side holds only the day off the spend id, so it covers day keys `from .. from + days`
 * **inclusive** — one key wider than the instant window could ever reach, never narrower —
 * so a play dealt on the window's last partial day is paid rather than refused. Pinned by
 * `challengeAllowanceCases`, which carries both readings.
 */
export function governingOn(
  tiers: ChallengeTierConfig[],
  held: ChallengeTiersHeld,
  day: number
): ChallengeTierConfig | null {
  let best: ChallengeTierConfig | null = null;

  for (const tier of tiers) {
    const from = held[tier.id];
    if (typeof from !== "number" || from <= 0) continue;
    if (!(day >= from && day <= from + tier.days)) continue;
    if (best === null || tier.plays > best.plays) best = tier;
  }

  return best;
}

/**
 * Plays of each genre a day on `day`: the governing deal's figure, else the free figure.
 * Mirrors `ChallengeAllowance.On` and is pinned by `challengeAllowanceCases`.
 */
export function allowanceOn(
  tiers: ChallengeTierConfig[],
  held: ChallengeTiersHeld,
  day: number,
  freePlays: number
): number {
  const best = governingOn(tiers, held, day);
  return best ? best.plays : freePlays;
}

/**
 * What a deal debit is owed, or null when it is refused.
 *
 * Two shapes, told apart by the id (`SpendEntry.ChallengeTierId`). A **fresh** purchase names
 * its own day twice and costs the full price — accepted whatever else is running, because the
 * full price is the most any purchase could cost and the device is the one refusing a pointless
 * buy for the player's sake. An **upgrade** names an earlier window it inherits, and is owed the
 * difference over the largest smaller deal this wallet holds *with that very start* and still
 * running on the purchase day; anything else is refused, because an upgrade over a window this
 * server never sold is a discount on nothing. Mirrors `ChallengeAllowance.Price`; pinned by
 * `challengeUpgradeCases`.
 */
export function dealPrice(
  config: ChallengesConfig,
  held: ChallengeTiersHeld,
  tierId: string,
  fromDay: number,
  boughtDay: number
): { price: number; upgrades: string } | null {
  const tier = findTier(config, tierId);
  if (!tier || fromDay <= 0 || boughtDay < fromDay) return null;

  if (fromDay === boughtDay) return { price: tier.gems, upgrades: "" };

  let running: ChallengeTierConfig | null = null;
  for (const other of config.tiers) {
    if (held[other.id] !== fromDay) continue;
    if (other.plays >= tier.plays) continue;
    if (!(boughtDay <= fromDay + other.days)) continue;
    if (running === null || other.plays > running.plays) running = other;
  }

  if (!running) return null;
  return { price: Math.max(0, tier.gems - running.gems), upgrades: running.id };
}

/**
 * Records a deal purchase on the wallet's copy: the later of the held date and the new one,
 * per tier id, which is the client's join (`ChallengeLedger.ReadTiers`).
 */
export function holdTier(held: ChallengeTiersHeld, tierId: string, fromDay: number): ChallengeTiersHeld {
  const next = { ...held };
  const current = next[tierId] ?? 0;
  if (fromDay > current) next[tierId] = fromDay;
  return next;
}

/** `chaltier:{tierId}:{fromDay}:{boughtDay}` — minted by `SpendEntry.ChallengeTierId`. */
export interface ChallengeTierSpend {
  tierId: string;
  /** The day the window began: the purchase day for a fresh deal, the running deal's start for an upgrade. */
  fromDay: number;
  /** The day the gems left, which the purchase is windowed on. */
  boughtDay: number;
}

export function isChallengeTierSpendId(id: string): boolean {
  return typeof id === "string" && id.startsWith("chaltier:");
}

/**
 * Reads a deal debit back, or null.
 *
 * Strict on both days, for `parseEndlessClaim`'s reason: two spellings of one day would be two
 * windows for one purchase. Exactly four parts, a key-shaped tier and two plain positive
 * integers with the window never beginning after its purchase.
 */
export function parseChallengeTierSpendId(id: string): ChallengeTierSpend | null {
  if (!isChallengeTierSpendId(id) || id.length > 64) return null;

  const parts = id.split(":");
  if (parts.length !== 4) return null;

  const tierId = parts[1];
  if (!KEY.test(tierId)) return null;

  const fromDay = strictInt(parts[2]);
  const boughtDay = strictInt(parts[3]);
  if (fromDay === null || boughtDay === null || fromDay <= 0 || boughtDay < fromDay) return null;

  return { tierId, fromDay, boughtDay };
}

/**
 * How many days either side of today a deal's *purchase* day may be dated. A purchase is
 * written the moment it is made, so anything further off is a wrong clock or a backlog somebody
 * assembled; refused rather than left pending (13a read the other way), and the client takes
 * the deal back. The window's start day is not windowed — an upgrade names a start weeks old.
 */
export const MAX_TIER_DAYS_AHEAD = 2;
export const MAX_TIER_DAYS_BEHIND = 2;

/** The deal a debit names, or null when the block does not sell it. */
export function findTier(config: ChallengesConfig, tierId: string): ChallengeTierConfig | null {
  return config.tiers.find((tier) => tier.id === tierId) ?? null;
}

// --------------------------------------------------------------------------- the claim
/** `chal:{dayKey}:{genre}:{win}:{currency}` — minted by `GrantEntry.ChallengeClearId`. */
export interface ChallengeClaim {
  dayKey: number;
  genre: string;
  /** The ordinal of the win within the day for that genre, one-based. */
  win: number;
  currency: string;
}

export function isChallengeGrantId(id: string): boolean {
  return typeof id === "string" && id.startsWith("chal:");
}

/**
 * Reads a claim back, or null. Strict on both numbers and on the genre's shape, for the reason
 * every parse here is: a claim this server half-understands is one it prices against the wrong
 * day, and a loose genre would let two spellings of one win past.
 */
export function parseChallengeClaim(id: string): ChallengeClaim | null {
  if (!isChallengeGrantId(id) || id.length > 96) return null;

  const parts = id.split(":");
  if (parts.length !== 5) return null;

  const dayKey = strictInt(parts[1]);
  const genre = parts[2];
  const win = strictInt(parts[3]);
  const currency = parts[4];

  if (dayKey === null || win === null || win <= 0 || !KEY.test(genre) || !currency) return null;

  return { dayKey, genre, win, currency };
}

function strictInt(raw: string): number | null {
  if (!/^\d{1,9}$/.test(raw)) return null;
  return Number(raw);
}

/**
 * How many days either side of today a claim may be dated. A win is claimed the instant a run
 * ends, so two days each way covers a wrong clock and an overnight sync that could not reach
 * the network; the endless window, for the endless reason. Outside it a claim is **refused**,
 * because the window will not become true again tomorrow.
 */
export const MAX_CHALLENGE_DAYS_AHEAD = 2;
export const MAX_CHALLENGE_DAYS_BEHIND = 2;

/**
 * What a claim is worth: the published rate when the genre is one the file ships and the win's
 * ordinal is inside the allowance the wallet's deals give that day, nought otherwise.
 *
 * **Nought here means "not yet" as often as it means "never".** A sync sends awards before
 * debits, so a deal bought offline and the wins it allowed arrive as claims *ahead* of the
 * debit that would cover them. The caller therefore leaves an over-allowance claim unconfirmed
 * while its day is still inside the window, and refuses it only once the window has closed —
 * by then the debit has landed or been refused, and in the second case the client has already
 * taken the deal back (`ChallengeLedger.OnSpendRejected`).
 */
export function challengeGrant(
  config: ChallengesConfig,
  claim: ChallengeClaim,
  held: ChallengeTiersHeld
): { amount: number; allowance: number; known: boolean } {
  const known = config.genres.includes(claim.genre);
  const allowance = allowanceOn(config.tiers, held, claim.dayKey, config.freePlays);

  if (!known || claim.win > allowance || config.coins <= 0) return { amount: 0, allowance, known };
  return { amount: config.coins, allowance, known };
}
