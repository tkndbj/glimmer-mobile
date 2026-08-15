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

import { getFirestore } from "firebase-admin/firestore";
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

/**
 * Reads a bounded sample of saves and republishes `config/stats`.
 *
 * The sample is whatever Firestore returns first, which is document-id order — and a
 * Firebase uid is random, so id order is as good as random for this purpose. It is not a
 * uniform sample of *play*: a player who has cleared more glades contributes to more of
 * them, which is exactly the population the line is about ("keepers who have finished this
 * glade"), so the bias is the one that was wanted.
 */
export async function rebuildStats(): Promise<{ levels: number; saves: number }> {
  const db = getFirestore();

  const snapshot = await db.collection("players").limit(SAMPLE_SIZE).get();
  const collected = collect(snapshot.docs.map((doc) => doc.data() as { levels?: unknown }));
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
