/**
 * The free bonus (invariant 67): gems for linking a sign-in and for choosing a nickname, offered
 * to an account that had done neither when it was first asked, and paid once per task per account.
 *
 * <p>
 * <b>Server-owned end to end, the seven-day pass's shape</b> (`daily-pass.ts`). The offer is a
 * record on `players/{uid}/private/wallet` - the document no client can write - and so is what it
 * paid; the client predicts nothing and adopts the balances a claim answers with. Nothing is in the
 * save, so nothing merges (11e) and a second device reads the same answer as the first.
 * </p>
 * <p>
 * <b>"Only for players who did not do them before" is an enrolment, not a date.</b> The first time a
 * build that draws the bonus asks (`getFreeBonus`), each task is written down as <em>offered</em> if
 * it is still undone and <em>exempt</em> if it is already done - judged on the server's own facts: the
 * Firebase Auth record's linked providers and the wallet's name reservation. Once written the record
 * never re-enrols, so a task done afterwards is the only way to be paid, and a task done before is
 * never offered. An old build never asks, so linking on a build that cannot show the offer does not
 * earn it later. A third task added in a later build enrols on that build's first ask
 * ({@link enrol} fills only what is missing).
 * </p>
 * <p>
 * <b>Every payment is adjudicated</b> (13): a link is read off the auth record at claim time, a name
 * off the wallet's reservation (and only a publishable one, so a name taken down pays nothing). The
 * amount was copied onto the offer when it was made (a retune never changes a promise already shown),
 * and the paid state is written in the transaction that raises `granted` (10a's spirit: the amount is
 * the server's, never the client's). A grant record `freebonus:{task}` is written beside it for support.
 * </p>
 * <p>
 * <b>A sign-in earns the link bonus once, ever</b>, not once per account: each linked identity is
 * recorded under a hash of `provider:providerUid` in `bonusIdentities` (server-only), kept when an
 * account is deleted - `receipts`' reasoning (`account.ts`) - so delete, start over and link the same
 * Google or Apple account again pays nothing. The hash keeps the provider's own id out of the store.
 * </p>
 */

import { createHash } from "node:crypto";

// ------------------------------------------------------------------ the tasks
/** The tasks, in the order the panel lists them. Code (`FreeBonusTasks`); amounts are content. */
export const FREE_BONUS_TASKS = ["link", "name"] as const;
export type FreeBonusTask = (typeof FREE_BONUS_TASKS)[number];

/** A sanity ceiling on one task's gems, `GIFT_MAX_GEMS`'s role. The seeder refuses above it too. */
export const FREE_BONUS_MAX_GEMS = 10_000;

/** What every free-bonus grant records as its cause. */
export const FREE_BONUS_REASON = "free_bonus";

/** Where a sign-in that has earned the link bonus is recorded. Server-only: the rules' catch-all denies it. */
export const FREE_BONUS_PATHS = {
  identity: (key: string) => `bonusIdentities/${key}`,
};

export function isFreeBonusTask(value: unknown): value is FreeBonusTask {
  return typeof value === "string" && (FREE_BONUS_TASKS as readonly string[]).includes(value);
}

/** The grant record's id, for support. Nothing reads it back; the wallet's record is the entitlement. */
export function freeBonusGrantId(task: FreeBonusTask): string {
  return `freebonus:${task}`;
}

// ------------------------------------------------------------------ the config
/** `config/progression`'s `freeBonus` block, as `seed-config.mjs` publishes it. */
export interface FreeBonusConfig {
  /** Task to the gems it pays. A task absent here is not offered. */
  gems: Partial<Record<FreeBonusTask, number>>;
}

function wholeGems(value: unknown): number | null {
  if (typeof value !== "number" || !Number.isInteger(value)) return null;
  return value > 0 && value <= FREE_BONUS_MAX_GEMS ? value : null;
}

/**
 * The published block, or null when there is none or nothing in it is usable: the feature off.
 * A malformed task is dropped rather than failing the block, the seeder having refused it already.
 */
export function usableFreeBonusConfig(raw: unknown): FreeBonusConfig | null {
  if (!raw || typeof raw !== "object") return null;
  const rows = (raw as { tasks?: unknown }).tasks;
  if (!Array.isArray(rows)) return null;

  const gems: Partial<Record<FreeBonusTask, number>> = {};
  for (const row of rows) {
    const id = (row as { id?: unknown } | null)?.id;
    const amount = wholeGems((row as { gems?: unknown } | null)?.gems);
    if (!isFreeBonusTask(id) || amount === null || gems[id] !== undefined) continue;
    gems[id] = amount;
  }
  return Object.keys(gems).length > 0 ? { gems } : null;
}

// ------------------------------------------------------------------ the record
/**
 * One task's standing on the wallet. `offered` is waiting to be earned and paid; `exempt` was already
 * done when the account was enrolled (or its sign-in already earned the bonus elsewhere) and is never
 * offered; `paid` was paid `gems` at `paidUnix`.
 */
export type TaskState = "offered" | "exempt" | "paid";

export interface TaskRecord {
  state: TaskState;
  /** The gems promised when the offer was made; nought for an exempt task. */
  gems: number;
  /** When the account was enrolled for this task. */
  atUnix: number;
  /** When it was paid; nought until then. */
  paidUnix: number;
}

export type FreeBonusRecord = Partial<Record<FreeBonusTask, TaskRecord>>;

const STATES: readonly TaskState[] = ["offered", "exempt", "paid"];

function unix(value: unknown): number {
  return typeof value === "number" && Number.isFinite(value) && value > 0 ? Math.floor(value) : 0;
}

/**
 * The wallet's record, read back. A malformed row is dropped, which re-enrols that one task on the
 * next ask - as it was then, so a task done since is exempt rather than paid. Carried through
 * `readWallet`, because every writer writes the wallet whole.
 */
export function readFreeBonus(raw: unknown): FreeBonusRecord {
  const out: FreeBonusRecord = {};
  if (!raw || typeof raw !== "object" || Array.isArray(raw)) return out;

  for (const task of FREE_BONUS_TASKS) {
    const row = (raw as Record<string, unknown>)[task] as Record<string, unknown> | undefined;
    if (!row || typeof row !== "object") continue;

    const state = row.state as TaskState;
    if (!STATES.includes(state)) continue;

    const gems = state === "exempt" ? 0 : wholeGems(row.gems);
    if (gems === null) continue;

    out[task] = { state, gems, atUnix: unix(row.atUnix), paidUnix: state === "paid" ? unix(row.paidUnix) : 0 };
  }
  return out;
}

/** What the server knows about the account when it judges: a sign-in linked, a publishable name held. */
export interface FreeBonusFacts {
  linked: boolean;
  named: boolean;
}

export function isDone(task: FreeBonusTask, facts: FreeBonusFacts): boolean {
  return task === "link" ? facts.linked : facts.named;
}

/**
 * The record after enrolment: every task the config offers and the record does not yet hold is
 * written down, offered if undone and exempt if done. Tasks already held are never touched - that is
 * the whole of "only for players who did not do them before". Pure; `changed` says whether to write.
 */
export function enrol(
  record: FreeBonusRecord, config: FreeBonusConfig, facts: FreeBonusFacts, nowUnix: number
): { record: FreeBonusRecord; changed: boolean } {
  const next: FreeBonusRecord = { ...record };
  let changed = false;

  for (const task of FREE_BONUS_TASKS) {
    const gems = config.gems[task];
    if (next[task] || gems === undefined) continue;

    next[task] = isDone(task, facts)
      ? { state: "exempt", gems: 0, atUnix: nowUnix, paidUnix: 0 }
      : { state: "offered", gems, atUnix: nowUnix, paidUnix: 0 };
    changed = true;
  }
  return { record: next, changed };
}

/** One task as a reply lists it. Exempt tasks are never listed: the player never sees them. */
export interface FreeBonusRow {
  id: FreeBonusTask;
  gems: number;
  /** Paid already - on this device or another. */
  paid: boolean;
  /** Done by the server's facts, so the panel offers CLAIM rather than the way to do it. */
  done: boolean;
}

export function bonusRows(record: FreeBonusRecord, facts: FreeBonusFacts): FreeBonusRow[] {
  const rows: FreeBonusRow[] = [];
  for (const task of FREE_BONUS_TASKS) {
    const row = record[task];
    if (!row || row.state === "exempt") continue;
    rows.push({ id: task, gems: row.gems, paid: row.state === "paid", done: isDone(task, facts) });
  }
  return rows;
}

// ------------------------------------------------------------------ judging
export type FreeBonusVerdict =
  | { kind: "pay"; gems: number }
  | { kind: "spent" }
  | { kind: "not_done" }
  | { kind: "none" };

/**
 * What a claim is worth. `none` - never offered to this account (exempt, or not enrolled); `spent` -
 * already paid; `not_done` - offered, and the server cannot see it done yet (a claim the client may
 * make again tomorrow: 13a); `pay` - the gems the offer promised.
 */
export function judgeClaim(record: FreeBonusRecord, task: FreeBonusTask, facts: FreeBonusFacts): FreeBonusVerdict {
  const row = record[task];
  if (!row || row.state === "exempt") return { kind: "none" };
  if (row.state === "paid") return { kind: "spent" };
  if (!isDone(task, facts)) return { kind: "not_done" };
  return { kind: "pay", gems: row.gems };
}

/** The record after a task is paid. Pure, so the paying transaction writes exactly this back. */
export function payTask(record: FreeBonusRecord, task: FreeBonusTask, nowUnix: number): FreeBonusRecord {
  const row = record[task];
  if (!row || row.state !== "offered") return record;
  return { ...record, [task]: { ...row, state: "paid", paidUnix: nowUnix } };
}

/**
 * The record after a link claim finds its sign-in already paid to another account: the task becomes
 * exempt, so the panel stops offering what can never pay and the client stops asking.
 */
export function voidTask(record: FreeBonusRecord, task: FreeBonusTask): FreeBonusRecord {
  const row = record[task];
  if (!row || row.state !== "offered") return record;
  return { ...record, [task]: { ...row, state: "exempt", gems: 0 } };
}

// ------------------------------------------------------------------ the sign-ins
/** The slice of a Firebase Auth `UserInfo` this reads. */
export interface ProviderInfo {
  providerId: string;
  uid: string;
}

/**
 * The account's linked sign-ins, as `provider:providerUid`. The client's `IsLinked` rule: any provider
 * that is not Firebase's own (an anonymous account lists none). Sorted, so a record is stable.
 */
export function linkedIdentities(providers: readonly ProviderInfo[] | undefined): string[] {
  const out = new Set<string>();
  for (const p of providers ?? []) {
    if (!p || typeof p.providerId !== "string" || typeof p.uid !== "string") continue;
    if (p.providerId.length === 0 || p.uid.length === 0 || p.providerId === "firebase") continue;
    out.add(`${p.providerId}:${p.uid}`);
  }
  return [...out].sort();
}

/** The document id a sign-in is recorded under: a SHA-256 of the identity, so no provider id is stored. */
export function identityKey(identity: string): string {
  return createHash("sha256").update(identity, "utf8").digest("hex");
}

/** What `bonusIdentities/{key}` holds: which account the sign-in's bonus was paid to, and when. */
export interface IdentityDoc {
  uid: string;
  atUnix: number;
}

/** True when any of the account's sign-ins already earned the link bonus for a different account. */
export function earnedElsewhere(uid: string, held: readonly (IdentityDoc | null | undefined)[]): boolean {
  return held.some((doc) => !!doc && typeof doc.uid === "string" && doc.uid !== uid);
}
