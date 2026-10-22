/**
 * The server half of the first steps (`FirstStepTable` / `FirstStepsLedger` on the client): the
 * chests a new player is handed the first time they clear a named level.
 *
 * A chest step's chest is a `daily.ts` chest seeded from a *subject*: the account, the tag
 * `steps` and `{levelId}:{chest}`. The client rolls it to show and bank the reward offline and
 * claims the currency under `steps:{levelId}:{chest}:{currency}`; this re-rolls the same chest
 * from the same facts (10a), so the client's number is only a prediction.
 *
 * **What bounds a claim is the published table and the save.** A step pays only on a level the
 * seeded table names, only as many chests as it names, and only once the save this server holds
 * shows that level cleared - the same `levels` map every derived reward reads. The grant log makes
 * each chest payable once per account, so the most a forged save can ever extract is the table's
 * chests, once: exactly what an honest new player gets. Whether the account earned the step on a
 * build that knew it (the client's "new players only" gate) is pacing, not money, and is not
 * re-checked here.
 */

import { subjectSeed, Rolls } from "./random";
import { ChestConfig, RolledDrop, rollChestWith } from "./daily";
import { TaskConfig, TaskTier, findTier } from "./tasks";

/** Matches `FirstStepsLedger.SeedTag`. Contract (invariant 9c). */
export const STEPS_SEED_TAG = "steps";

/** Matches `FirstStepTable.MaxSteps` and `FirstStepTable.MaxChests`. */
export const STEPS_MAX = 16;
export const STEPS_MAX_CHESTS = 4;

/** Matches `LevelId.TryParse`: a-z, 0-9 and underscore, at most 48, no underscore at either end. */
const LEVEL_ID = /^[a-z0-9](?:[a-z0-9_]{0,46}[a-z0-9])?$/;

export interface FirstStepRow {
  level: string;
  tier: string;
  count: number;
}

export interface FirstStepsConfig {
  steps: FirstStepRow[];
}

/**
 * The published chest steps, or null when nothing is paid: absent, or any fault at all. Refused
 * whole for `usableKeeperMilestones`' reason - a row this side would not price is a claim left
 * unconfirmed for ever, and a client that refused the same block shows nothing to claim.
 */
export function usableFirstSteps(raw: unknown, tasks: TaskConfig | null): FirstStepsConfig | null {
  if (!raw || typeof raw !== "object" || Array.isArray(raw)) return null;
  const block = raw as Partial<FirstStepsConfig>;

  if (!Array.isArray(block.steps) || block.steps.length === 0) return null;
  if (block.steps.length > STEPS_MAX) return null;

  const steps: FirstStepRow[] = [];
  const seen = new Set<string>();

  for (const row of block.steps as Partial<FirstStepRow>[]) {
    if (!row || typeof row !== "object") return null;
    if (typeof row.level !== "string" || !LEVEL_ID.test(row.level) || seen.has(row.level)) return null;
    if (typeof row.tier !== "string" || !row.tier || !tasks || !findTier(tasks, row.tier)) return null;
    if (typeof row.count !== "number" || !Number.isInteger(row.count) ||
        row.count < 1 || row.count > STEPS_MAX_CHESTS) return null;

    seen.add(row.level);
    steps.push({ level: row.level, tier: row.tier, count: row.count });
  }

  return { steps };
}

/** The chest step standing on `level`, or null. */
export function stepAt(config: FirstStepsConfig | null, level: string): FirstStepRow | null {
  if (!config) return null;
  return config.steps.find((row) => row.level === level) ?? null;
}

/** `FirstStepsLedger.Subject`: the level and the chest's index. Contract. */
export function stepSubject(level: string, chest: number): string {
  return `${level}:${chest}`;
}

class StepRandom extends Rolls {
  constructor(playerKey: string, subject: string, stream: number) {
    super(subjectSeed(playerKey, STEPS_SEED_TAG, subject, stream));
  }
}

/** Everything in one step chest, for one account. */
export function rollStepChest(chest: ChestConfig, playerKey: string, level: string, index: number): RolledDrop[] {
  const subject = stepSubject(level, index);
  return rollChestWith(chest, (stream) => new StepRandom(playerKey, subject, stream));
}

/** What one step chest is worth in one currency. Zero when it holds none. */
export function stepChestValue(chest: ChestConfig, playerKey: string, level: string, index: number,
                               currency: string): number {
  let total = 0;
  for (const drop of rollStepChest(chest, playerKey, level, index)) {
    if (drop.kind === currency) total += drop.amount;
  }
  return total;
}

// ------------------------------------------------------------------ the claim
/** `steps:{levelId}:{chest}:{currency}` - minted by `GrantEntry.FirstStepId`. */
export interface StepClaim {
  level: string;
  chest: number;
  currency: string;
  /** For the shared oldest-first sort: a step has no calendar, so it sorts first. */
  dayKey: number;
}

export function isStepGrantId(id: string): boolean {
  return typeof id === "string" && id.startsWith("steps:");
}

/**
 * Reads a step grant id back, or null. Strict for `parseMilestoneClaim`'s reason: the id is what
 * the database keys on, so two spellings of one chest would pay twice. Exactly four parts, a
 * level id as the client spells one, a plain chest index under the most a step may pay, and a
 * currency.
 */
export function parseStepClaim(id: string): StepClaim | null {
  if (!isStepGrantId(id) || id.length > 96) return null;

  const parts = id.split(":");
  if (parts.length !== 4) return null;

  const [, level, rawChest, currency] = parts;
  if (!LEVEL_ID.test(level)) return null;
  if (!/^\d$/.test(rawChest)) return null;

  const chest = Number(rawChest);
  if (chest >= STEPS_MAX_CHESTS) return null;
  if (!currency || currency.length > 24) return null;

  if (`steps:${level}:${chest}:${currency}` !== id) return null;

  return { level, chest, currency, dayKey: 0 };
}

/**
 * Whether the save this server holds shows `level` cleared: a row in the `levels` map with at
 * least one star - the reading `derivedXp` puts on the same map.
 */
export function savedClear(save: Record<string, unknown> | undefined, level: string): boolean {
  const levels = save?.levels;
  if (!levels || typeof levels !== "object" || Array.isArray(levels)) return false;

  const entry = (levels as Record<string, unknown>)[level] as { stars?: unknown } | undefined;
  return !!entry && typeof entry === "object" && typeof entry.stars === "number" && Math.floor(entry.stars) > 0;
}

/**
 * How a step claim is judged, as a pure function so `claimAwards` is one call.
 *
 *  - `ok`: the level is a published chest step, the chest index is one it pays, its tier is
 *    priceable, and the save shows the level cleared. Carries the tier.
 *  - `unknown`: nothing this server can price *yet* - no usable table, a level it does not name,
 *    a chest index past the step's count, a tier the tasks block does not hold, or a level the
 *    save does not yet show cleared (the save may be one sync behind the claim). Every one of
 *    those can become true - a client may hold a content push this server has not been seeded
 *    with - so the claim is left unconfirmed rather than refused (13a). Nothing past the parse is
 *    refused.
 */
export type StepVerdict =
  | { kind: "ok"; tier: TaskTier }
  | { kind: "unknown"; why: string };

export function judgeStepClaim(
  steps: FirstStepsConfig | null,
  tasks: TaskConfig | null,
  claim: StepClaim,
  save: Record<string, unknown> | undefined
): StepVerdict {
  const row = stepAt(steps, claim.level);
  if (!row) return { kind: "unknown", why: "no published chest step on this level" };

  if (claim.chest >= row.count) {
    return { kind: "unknown", why: `the step pays ${row.count} chest(s); chest ${claim.chest} is not one of them` };
  }

  const tier = tasks ? findTier(tasks, row.tier) : null;
  if (!tier) return { kind: "unknown", why: `tier '${row.tier}' is not in the tasks block` };

  if (!savedClear(save, claim.level)) {
    return { kind: "unknown", why: "the save this server holds does not yet show the level cleared" };
  }

  return { kind: "ok", tier };
}
