/**
 * The server's copy of the task rules: what a task's chest pays, and how many chests a
 * period may pay at all.
 *
 * A task's chest is a `daily.ts` chest seeded from a *subject* rather than a day: the
 * account, the tag `task`, and `{period}:{key}:{taskId}`. The client rolls it to show and
 * spend the reward offline; this re-rolls it from the claim id to decide what to grant, and
 * the client's number is a prediction (invariant 10a). Both halves are pinned by
 * `firebase/shared/reward-vectors.json`.
 *
 * What the server cannot recompute is whether the task was *done* — the counters live in
 * the player's own save, which is forgeable — so a task claim is *bounded* instead of
 * proved, the way a streak night is (`streak.ts`): the wallet this server owns remembers
 * which task ids it has paid in each period, and pays no more than the slate deals. A
 * forged save therefore buys what an honest day buys, and no more.
 *
 * The rotation — which three tasks a day actually deals — is deliberately *not* refused
 * on. It is a pure function of the key and the published slate, but a content push that
 * adds a task re-deals every period after it, and a claim in flight across that push
 * would then be refused for ever (invariant 13a). So a claim for a task the current slate
 * would not have dealt is logged and paid, inside the allowance.
 */

import { logger } from "firebase-functions";
import { subjectSeed, Rolls } from "./random";
import { ChestConfig, RolledDrop, rollChestWith, SECONDS_PER_DAY } from "./daily";

// ------------------------------------------------------------------------ the config
export const TASK_PERIODS = ["daily", "weekly"] as const;
export type TaskPeriod = (typeof TASK_PERIODS)[number];

export interface TaskTier {
  id: string;
  chest: ChestConfig;
}

export interface TaskEntry {
  id: string;
  goal: string;
  target: number;
  tier: string;
  retired?: boolean;
}

export interface TaskConfig {
  activePerPeriod: number;
  tiers: TaskTier[];
  daily: TaskEntry[];
  weekly: TaskEntry[];
}

/** Matches `TaskLedger.SeedTag`. Contract. */
export const TASK_SEED_TAG = "task";

/** Matches `TaskDefinition.MaxIdLength`. */
export const MAX_TASK_ID = 32;

/**
 * How far behind the calendar a claim may be dated, per period.
 *
 * A task chest is claimed while its period is live, so a claim only ages when the device
 * stays offline afterwards; forty-five days and ten weeks is longer than any offline
 * stretch a player comes back from, and the window is what bounds a forged backlog. A
 * claim outside it is refused, and the client drops a refused claim (CloudWalletState.
 * RejectedGrantIds), so it is not a loop.
 */
export const MAX_TASK_DAYS_BEHIND = 45;
export const MAX_TASK_WEEKS_BEHIND = 10;
export const MAX_TASK_PERIODS_AHEAD = 1;

/** Matches `WeeklyRules`: Monday-aligned weeks, three days' offset from the epoch's Thursday. */
export const DAYS_PER_WEEK = 7;
export const MONDAY_OFFSET = 3;

export function weekOfDay(dayKey: number): number {
  return dayKey <= 0 ? 0 : Math.floor((dayKey + MONDAY_OFFSET) / DAYS_PER_WEEK);
}

export function weekKey(nowMillis: number): number {
  return weekOfDay(Math.floor(nowMillis / 1000 / SECONDS_PER_DAY));
}

/** The current key for a period, off the server's own clock. */
export function periodKeyNow(period: TaskPeriod, nowMillis: number): number {
  const day = Math.floor(nowMillis / 1000 / SECONDS_PER_DAY);
  return period === "weekly" ? weekOfDay(day) : day;
}

export function isValidTaskId(id: unknown): id is string {
  return typeof id === "string" && id.length > 0 && id.length <= MAX_TASK_ID && /^[a-z0-9_]+$/.test(id);
}

/** Guards a config document that predates the tasks block, or was seeded badly. */
export function usableTaskConfig(config: unknown): TaskConfig | null {
  const c = config as TaskConfig | undefined;

  if (!c || typeof c !== "object") return null;
  if (typeof c.activePerPeriod !== "number" || c.activePerPeriod < 1) return null;
  if (!Array.isArray(c.tiers) || c.tiers.length === 0) return null;
  if (!Array.isArray(c.daily) || !Array.isArray(c.weekly)) return null;

  for (const tier of c.tiers) {
    if (!tier || !isValidTaskId(tier.id)) return null;
    if (!tier.chest || !Array.isArray(tier.chest.guaranteed) || tier.chest.guaranteed.length === 0) return null;
    if (tier.chest.options !== undefined && !Array.isArray(tier.chest.options)) return null;
  }

  for (const entry of [...c.daily, ...c.weekly]) {
    if (!entry || !isValidTaskId(entry.id) || !isValidTaskId(entry.tier)) return null;
  }

  return c;
}

export function findTask(config: TaskConfig, period: TaskPeriod, taskId: string): TaskEntry | null {
  const slate = period === "weekly" ? config.weekly : config.daily;
  return slate.find((entry) => entry.id === taskId) ?? null;
}

export function findTier(config: TaskConfig, tierId: string): TaskTier | null {
  return config.tiers.find((tier) => tier.id === tierId) ?? null;
}

// ----------------------------------------------------------------------- the rotation
/**
 * Which slate indices a period deals. Mirrors `TaskRotation.Indices`: key `k` deals
 * `k·n … k·n+n-1` modulo the live slate. Advisory here — see the module comment.
 */
export function rotationIndices(slateCount: number, key: number, perPeriod: number): number[] {
  if (slateCount <= 0 || perPeriod <= 0) return [];

  const n = Math.min(perPeriod, slateCount);
  const start = Math.max(0, key) * perPeriod;
  const picked: number[] = [];
  for (let i = 0; i < n; i++) picked.push((start + i) % slateCount);
  return picked;
}

export function dealt(config: TaskConfig, period: TaskPeriod, key: number): TaskEntry[] {
  const slate = (period === "weekly" ? config.weekly : config.daily).filter((t) => !t.retired);
  return rotationIndices(slate.length, key, config.activePerPeriod).map((i) => slate[i]);
}

// --------------------------------------------------------------------------- the roll
/** The subject half of the seed and of the claim id. Mirrors `TaskLedger.Subject`. */
export function taskSubject(period: TaskPeriod, key: number, taskId: string): string {
  return `${period}:${key}:${taskId}`;
}

class TaskRandom extends Rolls {
  constructor(playerKey: string, subject: string, stream: number) {
    super(subjectSeed(playerKey, TASK_SEED_TAG, subject, stream));
  }
}

/** Everything in one task's chest, for one account, in one period. */
export function rollTaskChest(
  chest: ChestConfig,
  playerKey: string,
  period: TaskPeriod,
  key: number,
  taskId: string
): RolledDrop[] {
  const subject = taskSubject(period, key, taskId);
  return rollChestWith(chest, (stream) => new TaskRandom(playerKey, subject, stream));
}

/** What one task's chest is worth in one currency. Zero when it holds none of it. */
export function taskCurrencyValue(
  chest: ChestConfig,
  playerKey: string,
  period: TaskPeriod,
  key: number,
  taskId: string,
  currency: string
): number {
  let total = 0;
  for (const drop of rollTaskChest(chest, playerKey, period, key, taskId)) {
    if (drop.kind === currency) total += drop.amount;
  }
  return total;
}

// -------------------------------------------------------------------- claim parsing
export interface TaskClaim {
  period: TaskPeriod;
  key: number;
  taskId: string;
  currency: string;

  /** The claim's day, for the shared oldest-first sort; a week is its Monday. */
  dayKey: number;
}

export function isTaskGrantId(id: string): boolean {
  return typeof id === "string" && id.startsWith("task:");
}

/**
 * Reads a grant id back into what it claims: `task:{period}:{key}:{taskId}:{currency}`,
 * produced by `GrantEntry.TaskChestId` on the client. Parsed rather than trusted from a
 * payload, for `parseDailyClaim`'s reason: the id is what the database keys on.
 */
export function parseTaskClaim(id: string): TaskClaim | null {
  if (typeof id !== "string" || id.length > 64) return null;

  const parts = id.split(":");
  if (parts.length !== 5 || parts[0] !== "task") return null;

  const period = parts[1] as TaskPeriod;
  if (!TASK_PERIODS.includes(period)) return null;

  const key = Number(parts[2]);
  const taskId = parts[3];
  const currency = parts[4];

  if (!Number.isInteger(key) || key < 0) return null;
  if (!isValidTaskId(taskId)) return null;
  if (!currency || currency.length > 24) return null;

  // Canonical form has to round-trip, or two ids could name one chest and pay twice.
  if (`task:${period}:${key}:${taskId}:${currency}` !== id) return null;

  const dayKey = period === "weekly" ? key * DAYS_PER_WEEK - MONDAY_OFFSET : key;

  return { period, key, taskId, currency, dayKey };
}

/** Whether a claim's period is inside the window this server will price. */
export function insideWindow(claim: TaskClaim, nowMillis: number): boolean {
  const now = periodKeyNow(claim.period, nowMillis);
  const behind = claim.period === "weekly" ? MAX_TASK_WEEKS_BEHIND : MAX_TASK_DAYS_BEHIND;
  return claim.key <= now + MAX_TASK_PERIODS_AHEAD && claim.key >= now - behind;
}

// ---------------------------------------------------------------------- the allowance
/**
 * Which task ids this server has paid in each period, keyed `{period}:{key}`.
 *
 * Lives on `players/{uid}/private/wallet`, which no client can write — the whole reason it
 * is trustworthy. Pruned to the claim window on every write, so it is bounded by the
 * window times the allowance rather than by the account's age.
 */
export type TaskPaid = Record<string, string[]>;

export function readTaskPaid(raw: unknown): TaskPaid {
  const out: TaskPaid = {};
  if (!raw || typeof raw !== "object") return out;

  for (const [slot, ids] of Object.entries(raw as Record<string, unknown>)) {
    if (!Array.isArray(ids)) continue;
    const clean = ids.filter(isValidTaskId);
    if (clean.length > 0) out[slot] = clean;
  }

  return out;
}

export function paidSlot(period: TaskPeriod, key: number): string {
  return `${period}:${key}`;
}

/**
 * Whether the allowance lets this task be paid: already paid (the other currency of the
 * same chest, or a resubmission) or room left in the period.
 */
export function allowsTask(paid: TaskPaid, claim: TaskClaim, activePerPeriod: number): boolean {
  const ids = paid[paidSlot(claim.period, claim.key)] ?? [];
  if (ids.includes(claim.taskId)) return true;
  return ids.length < activePerPeriod;
}

/** The allowance after paying a task. Returns a new map; the old one is untouched. */
export function recordTask(paid: TaskPaid, claim: TaskClaim): TaskPaid {
  const slot = paidSlot(claim.period, claim.key);
  const ids = paid[slot] ?? [];
  if (ids.includes(claim.taskId)) return paid;
  return { ...paid, [slot]: [...ids, claim.taskId] };
}

/** Drops periods that have fallen out of the claim window, so the map stays small. */
export function pruneTaskPaid(paid: TaskPaid, nowMillis: number): TaskPaid {
  const out: TaskPaid = {};

  for (const [slot, ids] of Object.entries(paid)) {
    const [period, keyText] = slot.split(":");
    const key = Number(keyText);
    if (!TASK_PERIODS.includes(period as TaskPeriod) || !Number.isInteger(key)) continue;

    const claim: TaskClaim = { period: period as TaskPeriod, key, taskId: "x", currency: "", dayKey: 0 };
    if (insideWindow(claim, nowMillis)) out[slot] = ids;
  }

  return out;
}

/**
 * A claim for a task the current slate would not have dealt in that period. Logged and
 * paid — see the module comment — so this exists for support and for noticing a rotation
 * that has drifted, never for refusing.
 */
export function noteUndealt(uid: string, config: TaskConfig, claim: TaskClaim): void {
  const ids = dealt(config, claim.period, claim.key).map((t) => t.id);
  if (ids.includes(claim.taskId)) return;

  logger.info("a task claim names a task the current slate does not deal in that period", {
    uid, period: claim.period, key: claim.key, task: claim.taskId, dealt: ids,
  });
}
