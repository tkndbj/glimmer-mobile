import { FieldPath, FieldValue, Firestore, Timestamp } from "firebase-admin/firestore";
import { isDeepStrictEqual } from "node:util";
import { NORMAL_BOARD, normalScore, usableNormalScoring } from "./normal-scores";
import { BOARD_ROWS, GROVE_PATHS, compareRows, optedIn, populationOf, rowOf } from "./grove";

/** A single periodic writer. Reading the index inside the transaction prevents a stale
 * rebuild from resurrecting a row concurrently removed by a withdrawal or account deletion.
 * Only Normal uses this cadence; no global write on each player's improvement.
 */
export async function publishNormalBoard(db: Firestore): Promise<number> {
  return db.runTransaction(async (tx) => {
    const ref = db.doc(GROVE_PATHS.board(NORMAL_BOARD));
    const [top, held] = await Promise.all([
      tx.get(db.collection(GROVE_PATHS.normalCards).orderBy("normalScore", "desc").limit(BOARD_ROWS)),
      tx.get(ref),
    ]);
    const rows = top.docs.map((doc) => rowOf(doc.id, doc.data() as Parameters<typeof rowOf>[1]))
      .filter((row) => (row.normalScore ?? 0) > 0);
    rows.sort((a, b) => compareRows("normalScore", a, b));
    tx.set(ref, {
      entries: rows,
      ...populationOf(rows),
      builtUnix: Math.floor(Date.now() / 1000),
      scoringVersion: 1,
      // Keep the nightly population count once the list is full.
      ...(!held.exists && rows.length >= BOARD_ROWS ? { population: rows.length } : {}),
    }, { merge: true });
    return rows.length;
  });
}

export const NORMAL_BACKFILL_PATH = "maintenance/normalLeaderboardV1";
export const NORMAL_BACKFILL_PAGE = 200;

/** Publication stamps do not make an otherwise identical public card a new write. */
export function sameNormalCard(before: Record<string, unknown> | undefined,
                               after: Record<string, unknown>): boolean {
  if (!before) return false;
  const a = { ...before }, b = { ...after };
  delete a.builtUnix;
  delete b.builtUnix;
  return isDeepStrictEqual(a, b);
}

/** Durable, bounded backfill. Checkpoint only a fully successful page: a retry repeats
 * deterministic projections rather than adding points. A lease prevents overlapping jobs.
 * The scan includes saves whose owners never played Infinite or Shuffle and have no card.
 */
export async function backfillNormalScores(
  db: Firestore, config: unknown, publish: (uid: string) => Promise<unknown>,
): Promise<{ processed: number; done: boolean }> {
  if (!usableNormalScoring(config)) throw new Error("Normal scoring config is not seeded");
  const ref = db.doc(NORMAL_BACKFILL_PATH);
  const now = Timestamp.now();
  const lease = await db.runTransaction(async (tx) => {
    const snapshot = await tx.get(ref);
    const state = snapshot.data();
    if (state?.done === true || (state?.leaseUntil instanceof Timestamp && state.leaseUntil.toMillis() > now.toMillis()))
      return null;
    tx.set(ref, { leaseUntil: Timestamp.fromMillis(now.toMillis() + 540_000),
                  leaseToken: now, version: 1 }, { merge: true });
    return { cursor: typeof state?.cursor === "string" ? state.cursor : "", token: now };
  });
  if (!lease) return { processed: 0, done: false };

  try {
    let query = db.collection("players").orderBy(FieldPath.documentId()).limit(NORMAL_BACKFILL_PAGE)
      .select("levels", "settings");
    if (lease.cursor) query = query.startAfter(lease.cursor);
    const page = await query.get();
    // Four concurrent players, bounded regardless of population. Failed pages stay owed.
    for (let start = 0; start < page.docs.length; start += 4) {
      const results = await Promise.allSettled(page.docs.slice(start, start + 4).map(async (doc) => {
        const save = doc.data();
        if (optedIn(save) && normalScore(save.levels, config) > 0) await publish(doc.id);
      }));
      const failed = results.find((result) => result.status === "rejected");
      if (failed?.status === "rejected") throw failed.reason;
    }
    const done = page.size < NORMAL_BACKFILL_PAGE;
    await db.runTransaction(async (tx) => {
      const current = (await tx.get(ref)).data();
      if (!(current?.leaseToken instanceof Timestamp) || !current.leaseToken.isEqual(lease.token))
        throw new Error("Normal backfill lease changed; checkpoint refused");
      tx.set(ref, { cursor: page.docs.at(-1)?.id ?? lease.cursor, done,
                    processed: FieldValue.increment(page.size), updatedAt: Timestamp.now(),
                    leaseUntil: FieldValue.delete(), leaseToken: FieldValue.delete() }, { merge: true });
    });
    return { processed: page.size, done };
  } catch (error) {
    // Leave the lease to expire if we cannot release it. The cursor is still unchanged.
    await db.runTransaction(async (tx) => {
      const current = (await tx.get(ref)).data();
      if (current?.leaseToken instanceof Timestamp && current.leaseToken.isEqual(lease.token))
        tx.update(ref, { leaseUntil: FieldValue.delete(), leaseToken: FieldValue.delete() });
    }).catch(() => undefined);
    throw error;
  }
}

/** Used by the save trigger before doing any database reads. */
export function normalInputsChanged(before: Record<string, unknown> | undefined,
                                    after: Record<string, unknown>): boolean {
  if (optedIn(before ?? {}) !== optedIn(after) || normalProfileChanged(before, after)) return true;
  type Records = Record<string, { stars?: unknown; bestMoves?: unknown } | null>;
  const records = (raw: unknown): Records =>
    raw && typeof raw === "object" && !Array.isArray(raw) ? raw as Records : {};
  const a = records(before?.levels), b = records(after.levels);
  // Clear counts and played timestamps do not alter any best. Replays which improve
  // neither return before a config read, and map iteration order is irrelevant.
  for (const id of new Set([...Object.keys(a), ...Object.keys(b)]))
    if ((a[id]?.stars ?? 0) !== (b[id]?.stars ?? 0)
        || (a[id]?.bestMoves ?? 0) !== (b[id]?.bestMoves ?? 0)) return true;
  return false;
}

/** Legacy Normal-only clients do not ask for a public card themselves. Keep their
 * name, avatar and loadout current too, through the same moderated projection.
 */
export function normalProfileChanged(before: Record<string, unknown> | undefined,
                                     after: Record<string, unknown>): boolean {
  const wallet = (save: Record<string, unknown> | undefined) =>
    (save?.wallet ?? {}) as { displayName?: unknown; avatarId?: unknown };
  return wallet(before).displayName !== wallet(after).displayName
    || wallet(before).avatarId !== wallet(after).avatarId
    || !isDeepStrictEqual(before?.wardLoadout ?? [], after.wardLoadout ?? [])
    || !isDeepStrictEqual(before?.wardStars ?? [], after.wardStars ?? []);
}

