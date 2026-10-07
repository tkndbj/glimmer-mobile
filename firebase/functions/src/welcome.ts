/**
 * The welcome bonus's server half (invariant 58): a finished quest taken as its turret's price
 * rather than as the turret.
 *
 * The turret itself never reaches this server - it is a client-held entitlement that buys no
 * currency (invariant 15), so there is nothing to adjudicate. The *price* is currency, so it is
 * a claim (10a): the client awards itself a pending grant under `welcome:{questId}:{currency}`
 * and this side re-prices it off the published block, pays it once (the grant log keys on the
 * id), and never trusts the client's figure.
 *
 * What is published, by `seed-config.mjs` out of `progression.json`: each quest's id, the
 * turret it pays, and that turret's shelf price in its currency - the same roster the device
 * reads, so a retune of the shelf retunes both sides on the next seed. Absent leaves every
 * `welcome:` claim unconfirmed rather than refused (13a): a client that fetched the block before
 * the seeder ran is a player who earned the money, not a forger.
 */

import type { WalletDoc } from "./wallet";

export type WelcomeCurrency = "credits" | "gems";

export interface WelcomeQuestConfig {
  id: string;
  ward: string;
  currency: WelcomeCurrency;
  amount: number;
}

export interface WelcomeConfig {
  /** By quest id. */
  quests: Record<string, WelcomeQuestConfig>;
}

/** `WelcomeTable.MaxQuests`: the page's capacity, which bounds the block. */
export const WELCOME_MAX_QUESTS = 5;

/** A sanity ceiling on a price, far above any turret on the shelf. Mirrors nothing authored. */
export const WELCOME_MAX_AMOUNT = 1_000_000;

const QUEST_ID = /^[a-z][a-z0-9_]{0,31}$/;

/**
 * Reads the published block, or null when it is absent or malformed. Shape only - the seeder
 * refused everything else before it was written.
 */
export function usableWelcomeConfig(raw: unknown): WelcomeConfig | null {
  if (!raw || typeof raw !== "object") return null;
  const quests = (raw as { quests?: unknown }).quests;
  if (!quests || typeof quests !== "object") return null;

  const out: Record<string, WelcomeQuestConfig> = {};
  for (const [id, value] of Object.entries(quests as Record<string, unknown>)) {
    if (!QUEST_ID.test(id) || !value || typeof value !== "object") return null;
    const row = value as { ward?: unknown; currency?: unknown; amount?: unknown };
    if (typeof row.ward !== "string" || !row.ward) return null;
    if (row.currency !== "credits" && row.currency !== "gems") return null;
    if (typeof row.amount !== "number" || !Number.isFinite(row.amount)) return null;
    const amount = Math.floor(row.amount);
    if (amount <= 0 || amount > WELCOME_MAX_AMOUNT) return null;
    out[id] = { id, ward: row.ward, currency: row.currency, amount };
  }

  if (Object.keys(out).length === 0 || Object.keys(out).length > WELCOME_MAX_QUESTS) return null;
  return { quests: out };
}

// ------------------------------------------------------------------ the claim
/** `welcome:{questId}:{currency}` - minted by `GrantEntry.WelcomeId`. */
export interface WelcomeClaim {
  questId: string;
  currency: string;
  /** For the shared oldest-first sort: a quest has no calendar, so nought orders it first. */
  dayKey: number;
}

export function isWelcomeGrantId(id: string): boolean {
  return typeof id === "string" && id.startsWith("welcome:");
}

/**
 * Reads a welcome grant id back, or null. Strict for `parseStreakClaim`'s reason: the id is what
 * the database keys on, so two spellings of one quest would pay twice. Exactly three parts, a
 * quest id in the content alphabet, and a currency.
 */
export function parseWelcomeClaim(id: string): WelcomeClaim | null {
  if (!isWelcomeGrantId(id) || id.length > 72) return null;

  const parts = id.split(":");
  if (parts.length !== 3) return null;

  const questId = parts[1];
  const currency = parts[2];
  if (!QUEST_ID.test(questId)) return null;
  if (!currency || currency.length > 24) return null;

  if (`welcome:${questId}:${currency}` !== id) return null;

  return { questId, currency, dayKey: 0 };
}

/** The quest ids the player's own save says were taken as their price. Read for shape, never believed. */
export function welcomeCoinedOf(save: unknown): Set<string> {
  const out = new Set<string>();
  const tasks = (save as { tasks?: { welcome?: { coined?: unknown } } } | undefined)?.tasks;
  const coined = tasks?.welcome?.coined;
  if (!Array.isArray(coined)) return out;
  for (const id of coined) if (typeof id === "string" && QUEST_ID.test(id)) out.add(id);
  return out;
}

/**
 * How a welcome claim is judged, as a pure function so `claimAwards` is one call.
 *
 *  - `ok`: the quest is published, the currency is the one its turret is priced in, and the
 *    save says the quest was taken as its price. Carries the amount.
 *  - `unknown`: nothing this server can pay *yet* - no usable block, a quest the block does not
 *    name, or a save that does not (yet) record the choice. Each can become true (a seed, a
 *    content push, the save landing on the next sync), so the claim is left unconfirmed (13a).
 *  - `refuse`: the currency is not the turret's. That will still be wrong tomorrow, and a claim
 *    that will never confirm is one the client resubmits for ever.
 */
export type WelcomeVerdict =
  | { kind: "ok"; amount: number; ward: string }
  | { kind: "unknown"; why: string }
  | { kind: "refuse"; why: string };

export function judgeWelcomeClaim(
  config: WelcomeConfig | null,
  claim: WelcomeClaim,
  coined: Set<string> | null
): WelcomeVerdict {
  if (!config) return { kind: "unknown", why: "no usable welcome block" };

  const quest = config.quests[claim.questId];
  if (!quest) return { kind: "unknown", why: `no published quest '${claim.questId}'` };

  if (quest.currency !== claim.currency) {
    return { kind: "refuse", why: `quest '${claim.questId}' is priced in ${quest.currency}, not ${claim.currency}` };
  }

  if (!coined || !coined.has(claim.questId)) {
    return { kind: "unknown", why: "the save does not record this quest as taken for its price" };
  }

  return { kind: "ok", amount: quest.amount, ward: quest.ward };
}

/** Kept for symmetry with the other modules' wallet helpers; a welcome claim writes no wallet field of its own. */
export function touchesWallet(_state: WalletDoc): boolean {
  return false;
}
