/**
 * The population's move counts, published for the one line on the victory panel that
 * compares a player to everybody else.
 *
 * ## Why a sample rather than a count
 *
 * The obvious implementation keeps a running histogram per glade, incremented whenever a
 * player's best improves. It is exact, and it is the wrong trade twice over: it is a
 * write per improved clear, forever, on the busiest path in the game — and the increment
 * would have to come from the client, which is precisely the thing this codebase never
 * lets decide a number.
 *
 * A scheduled job that reads a bounded sample of saves costs one run a day whatever the
 * player count, writes one document, and is accurate to well within the single percentage
 * point the client ever displays. Sampling error on 5,000 saves is under one point; the
 * line rounds to five. There is no scale at which the exact version buys anything a player
 * could notice.
 *
 * ## What it publishes
 *
 * Per glade, the nine deciles of every sampled keeper's *best* move count, ascending, plus
 * how many keepers that was measured over. Bests rather than runs, because the client
 * compares a player's own record against it and comparing a fresh run against a population
 * of records would tell almost everybody they are below average — which is untrue and the
 * most demoralising thing a victory screen could say.
 *
 * Glades with too few keepers are published anyway, with their real sample count, and the
 * client refuses to draw them. Filtering here instead would make "absent" mean both "not
 * enough data" and "the job has not run", which are different problems.
 */

import { getFirestore, FieldPath } from "firebase-admin/firestore";
import { logger } from "firebase-functions";

/** How many saves one run reads. Bounded so the cost never grows with the player count. */
export const SAMPLE_SIZE = 5000;

/** Below this many keepers a glade's deciles are noise; the client draws nothing. */
export const MINIMUM_SAMPLES = 200;

export interface LevelStatsDoc {
  samples: number;
  deciles: number[];
}

/**
 * The nine deciles of a set of move counts, ascending.
 *
 * Nearest-rank rather than interpolated, which is the definition that always returns a
 * move count somebody actually achieved. An interpolated decile of 17.4 moves is not a
 * result any player has ever had, and the client turns these back into a percentage by
 * interpolating between them anyway — doing it twice would smear a small sample rather
 * than sharpen it.
 */
export function deciles(sorted: number[]): number[] {
  const out: number[] = [];

  for (let d = 1; d <= 9; d++) {
    const rank = Math.ceil((d / 10) * sorted.length) - 1;
    out.push(sorted[Math.min(Math.max(rank, 0), sorted.length - 1)]);
  }

  return out;
}

/**
 * Folds a batch of save documents into per-glade move counts.
 *
 * Exported so the shape can be exercised without Firestore. Every entry is validated the
 * way `earnedCredits` validates them — a best of zero means "never cleared", and a
 * negative or absurd one is a forged save rather than a fast player.
 */
export function collect(
  saves: Iterable<{ levels?: unknown }>,
  into: Map<string, number[]> = new Map()
): Map<string, number[]> {
  for (const save of saves) {
    const levels = save?.levels;
    if (!levels || typeof levels !== "object" || Array.isArray(levels)) continue;

    for (const [levelId, raw] of Object.entries(levels as Record<string, unknown>)) {
      const entry = raw as { bestMoves?: unknown; stars?: unknown } | null;
      if (!entry || typeof entry !== "object") continue;

      const stars = typeof entry.stars === "number" ? Math.floor(entry.stars) : 0;
      const best = typeof entry.bestMoves === "number" ? Math.floor(entry.bestMoves) : 0;

      // Zero best means never cleared, which is the same convention the save file and the
      // client both use. The upper bound is a forgery guard, not a difficulty opinion: no
      // real board is solvable in ten thousand turns and a value like that would drag a
      // decile with it.
      if (stars <= 0 || best <= 0 || best > 10_000) continue;

      const bucket = into.get(levelId);
      if (bucket) bucket.push(best);
      else into.set(levelId, [best]);
    }
  }

  return into;
}

/** Turns collected move counts into the document the client reads. */
export function summarise(collected: Map<string, number[]>): Record<string, LevelStatsDoc> {
  const levels: Record<string, LevelStatsDoc> = {};

  for (const [levelId, moves] of collected) {
    moves.sort((a, b) => a - b);
    levels[levelId] = { samples: moves.length, deciles: deciles(moves) };
  }

  return levels;
}

/** The alphabet a Firebase uid is drawn from. See `rebuildStats`. */
const UID_ALPHABET = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

/**
 * A random point in document-id space, so the sample is a fresh window every run.
 *
 * Deliberately duplicated from `grove.ts` rather than shared: the two jobs sample different
 * collections for different reasons and neither should acquire a dependency on the other to
 * say so. Three characters is about a quarter of a million starting points, far finer than
 * either sample is wide.
 */
function randomCursor(): string {
  let out = "";
  for (let i = 0; i < 3; i++) {
    out += UID_ALPHABET[Math.floor(Math.random() * UID_ALPHABET.length)];
  }
  return out;
}

/**
 * Reads a bounded random sample of saves and republishes `config/stats`.
 *
 * **The sample is a random window, not the first N.** A Firebase uid is random, so id order
 * is as good as random for picking *who* — but `limit(n)` with no cursor returns the n
 * lowest ids, which is the same players every day for ever. A keeper whose uid sorts late
 * could never contribute to a decile and nobody could tell, because the numbers would look
 * entirely reasonable. `rebuildGroveRanks` has the same cursor for the same reason.
 *
 * It is deliberately not a uniform sample of *play*: a player who has cleared more glades
 * contributes to more of them, which is exactly the population the line is about ("keepers
 * who have finished this glade"), so that bias is the one that was wanted.
 *
 * **Projected to one field.** A save is the largest document this game writes — every level
 * record, every ledger, the whole event history — and all this job wants is `levels`.
 * `select` costs the same reads (Firestore bills the index entry, not the payload) and is
 * the difference between pulling five thousand whole saves into a function's memory and
 * pulling five thousand small maps. At a million players that is the ceiling this job would
 * have hit first.
 */
export async function rebuildStats(): Promise<{ levels: number; saves: number }> {
  const db = getFirestore();

  const players = db.collection("players");
  const byId = FieldPath.documentId();
  const cursor = randomCursor();

  const first = await players.orderBy(byId).startAt(cursor)
                             .limit(SAMPLE_SIZE).select("levels").get();

  const docs = [...first.docs];

  // The window wraps, and it wraps with `endBefore`. A cursor landing near the end of the id
  // space would otherwise return a short sample, and a short sample taken from the end of the
  // alphabet is exactly the bias the cursor exists to remove. The remainder must come from
  // *before* the cursor: with fewer players than the sample size, starting again at the
  // beginning returns saves the first query already returned, and every one from the cursor
  // onwards is counted twice. `rebuildGroveRanks` shipped that mistake and a live run caught
  // it — 29 samples against a population of 15.
  if (first.size < SAMPLE_SIZE) {
    const rest = await players.orderBy(byId).endBefore(cursor)
                              .limit(SAMPLE_SIZE - first.size).select("levels").get();
    docs.push(...rest.docs);
  }

  const snapshot = { size: docs.length };
  const collected = collect(docs.map((doc) => doc.data() as { levels?: unknown }));
  const levels = summarise(collected);

  await db.doc("config/stats").set({
    levels,
    sampledSaves: snapshot.size,
    minimumSamples: MINIMUM_SAMPLES,
    builtAt: new Date().toISOString(),
  });

  const usable = Object.values(levels).filter((l) => l.samples >= MINIMUM_SAMPLES).length;
  logger.info("published grove stats", {
    saves: snapshot.size, glades: Object.keys(levels).length, usable,
  });

  return { levels: Object.keys(levels).length, saves: snapshot.size };
}
