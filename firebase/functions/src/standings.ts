/**
 * Where a finished chapter stands against everybody else who finished it (invariant 62).
 *
 * ## The question, and why it is answered with a sample
 *
 * The map's left column says "you are in the top 12%" of the keepers who cleared every level of
 * the chapter on screen, graded on stars first and moves second. That is a *population*
 * question, and the obvious ways of answering one exactly are the two this codebase has already
 * refused for the boards (19c) and for the retired per-glade move table (`stats.ts`, removed
 * 2026-10-07): a maintained global ordering is a write on every clear for ever and a read that
 * walks a list which grows with the game, and a per-chapter histogram is one hot document that
 * every clear in the world contends on. Neither buys a player anything they could notice over
 * a bounded sample: the sampling error on a few thousand saves is well under the whole
 * percentage point the client prints.
 *
 * So this is one scheduled job that reads a bounded window of saves, projected to the one
 * field it needs, and writes one document. **Nothing here grows with the player count.** At a
 * million players or a hundred million it costs `SAMPLE_SIZE` reads and one write per run, and
 * the document grows only with the catalog (a hundred numbers a chapter).
 *
 * ## What "fresh" means here
 *
 * The number a player sees is computed on the handset from their *own* save, against this
 * table, every time the map is drawn (`ChapterStanding`), so an improved clear moves it the
 * moment the run ends. What this job refreshes is the shape of everybody else's results, which
 * drifts over hours, not seconds - a few runs a day is more than that shape ever needs.
 *
 * ## The rule, in two places
 *
 * `chapterScore` exists in C# too (`ChapterStanding.ScoreOf`), because the client folds its own
 * save with it and compares the result against cut points this file produced from other
 * people's saves. A disagreement would not crash anything: it would tell a player a number that
 * is quietly about a different population. So both halves run `standingCases` in
 * `firebase/shared/grove-vectors.json` (`Tools/make_standing_vectors.py`), for invariant 9a's
 * reason.
 *
 * ## No backfill, and no writes to anybody's save
 *
 * Every save already carries `levels[id].stars` and `levels[id].bestMoves` - they are what the
 * map's nodes draw - so the first run of this job reads the real population as it stands. It
 * never writes a player document and never reads one whole.
 */

import { FieldPath, getFirestore } from "firebase-admin/firestore";
import { logger } from "firebase-functions";
import { loadProgressionConfig } from "./wallet";

/** Where the published table lives. One document: `config/*` is readable by any signed-in client. */
export const STANDINGS_PATH = "config/chapterStandings";

/**
 * How many saves one run reads. Bounded so the cost never grows with the player count, and
 * wide enough that the chapters most of the game has finished are measured over thousands.
 */
export const STANDING_SAMPLE_SIZE = 5000;

/**
 * Below this many finishers a chapter's cut points are noise, and the client says "top 1%"
 * instead (the owner's rule, 62). Published with the table so the client never has to agree
 * with this file about the threshold - it reads the number it is given.
 */
export const STANDING_MINIMUM_SAMPLES = 50;

/** How many cut points a chapter publishes: p1 to p99, ascending. */
export const PERCENTILE_COUNT = 99;

/**
 * What one star is worth against moves. Stars are the primary grade and moves the tiebreak,
 * so a star has to outweigh every move a chapter could possibly take: with each level's moves
 * clamped to `MOVES_CAP`, a thousand-level chapter still cannot bleed into the next star.
 * Shared with the client (`ChapterStanding.StarWeight`); changing it is a vector change.
 */
export const STAR_WEIGHT = 10_000_000;

/**
 * The most moves a level may count. A forgery guard rather than a difficulty opinion: no real
 * board takes ten thousand turns, and an absurd value would otherwise drag a cut point with it.
 * A cleared level recorded with no move count at all (a very old import) counts as the cap,
 * which is the worst honest reading rather than a free best.
 */
export const MOVES_CAP = 9_999;

/** Three stars is the ceiling everywhere (`LevelRecord.MaxStars`, `MAX_STARS`). */
const MAX_STARS = 3;

/** The chapter table as it is published, one row per chapter. */
export interface ChapterStandingDoc {
  /** How many sampled keepers had cleared every level of the chapter. */
  samples: number;

  /** The p1 to p99 cut points of their scores, ascending; empty when nobody had. */
  percentiles: number[];
}

/**
 * One finished chapter's score, or `null` for a chapter not yet finished.
 *
 * Stars first (a star outweighs any number of moves), moves second (fewer is better), as one
 * integer that is larger for a better result, so a sorted list of them is a ranking and a cut
 * point through it is a percentile. A level cleared with no move count counts as `MOVES_CAP`.
 *
 * `null` rather than nought for an unfinished chapter because the two mean different things to
 * the sampler: a finished chapter with a terrible score is in the population and an unfinished
 * one is not. Every level has to be cleared - a chapter half done says nothing about where its
 * finisher stands.
 */
export function chapterScore(levels: unknown, levelIds: readonly string[]): number | null {
  if (levelIds.length === 0) return null;
  if (!levels || typeof levels !== "object" || Array.isArray(levels)) return null;

  const map = levels as Record<string, unknown>;
  let stars = 0;
  let moves = 0;

  for (const id of levelIds) {
    const entry = map[id] as { stars?: unknown; bestMoves?: unknown } | null | undefined;
    if (!entry || typeof entry !== "object") return null;

    const rawStars = typeof entry.stars === "number" && Number.isFinite(entry.stars)
      ? Math.floor(entry.stars) : 0;
    if (rawStars <= 0) return null;

    const rawMoves = typeof entry.bestMoves === "number" && Number.isFinite(entry.bestMoves)
      ? Math.floor(entry.bestMoves) : 0;

    stars += Math.min(rawStars, MAX_STARS);
    moves += rawMoves <= 0 ? MOVES_CAP : Math.min(rawMoves, MOVES_CAP);
  }

  return stars * STAR_WEIGHT - moves;
}

/**
 * The catalog's chapters, each with its level ids in authored order, inverted from the
 * `levelChapters` map the seeder publishes (levelId → chapterId).
 *
 * Order within a chapter does not change the score - it is a sum - but the ids are sorted so
 * the walk is deterministic and a log line is readable. Only enabled chapters reach
 * `levelChapters` at all, so a hidden chapter is never ranked - and the client only asks about
 * laddered lanes, so a lane whose records climb (the Infinite lane) is ranked here and read by
 * nobody, which costs a hundred numbers and nothing else.
 */
export function chaptersOf(levelChapters: Record<string, string>): Map<string, string[]> {
  const out = new Map<string, string[]>();

  for (const [levelId, chapterId] of Object.entries(levelChapters)) {
    if (typeof chapterId !== "string" || !chapterId || typeof levelId !== "string" || !levelId) continue;

    const list = out.get(chapterId);
    if (list) list.push(levelId);
    else out.set(chapterId, [levelId]);
  }

  for (const list of out.values()) list.sort();
  return out;
}

/**
 * Folds a batch of save documents into per-chapter score lists. Exported so the shape can be
 * exercised without Firestore. A save contributes to every chapter it has finished and to no
 * other, which is exactly the population a percentile means anything against.
 */
export function collectStandings(
  saves: Iterable<{ levels?: unknown }>,
  chapters: Map<string, string[]>,
  into: Map<string, number[]> = new Map()
): Map<string, number[]> {
  for (const save of saves) {
    const levels = save?.levels;
    if (!levels || typeof levels !== "object") continue;

    for (const [chapterId, levelIds] of chapters) {
      const score = chapterScore(levels, levelIds);
      if (score === null) continue;

      const bucket = into.get(chapterId);
      if (bucket) bucket.push(score);
      else into.set(chapterId, [score]);
    }
  }

  return into;
}

/**
 * The p1 to p99 cut points of a sorted list, nearest-rank.
 *
 * Nearest-rank rather than interpolated so every cut point is a score somebody actually had,
 * and so the client can read a standing with integer arithmetic: the share of keepers at or
 * below a score is the number of cut points at or below it.
 *
 * **An empty list has no cut points**, for `deciles`' reason: the obvious loop indexes
 * `sorted[-1]` and produces ninety-nine `undefined`s, which Firestore refuses as a value, and
 * the whole job throws after doing its reads. An empty array is what the client reads as
 * "nothing to say".
 */
export function percentiles(sorted: readonly number[]): number[] {
  if (sorted.length === 0) return [];

  const out: number[] = [];
  for (let p = 1; p <= PERCENTILE_COUNT; p++) {
    const rank = Math.ceil((p / 100) * sorted.length) - 1;
    out.push(sorted[Math.min(Math.max(rank, 0), sorted.length - 1)]);
  }
  return out;
}

/**
 * Turns collected scores into the table the client reads: every chapter the catalog names,
 * including ones nobody has finished, so "absent" can only ever mean "the job has not run"
 * and never "not enough data" - those are different problems and the client treats them
 * differently (nothing to say, versus top 1%).
 */
export function summariseStandings(
  collected: Map<string, number[]>,
  chapters: Map<string, string[]>
): Record<string, ChapterStandingDoc> {
  const out: Record<string, ChapterStandingDoc> = {};

  for (const chapterId of chapters.keys()) {
    const scores = (collected.get(chapterId) ?? []).slice().sort((a, b) => a - b);
    out[chapterId] = { samples: scores.length, percentiles: percentiles(scores) };
  }

  return out;
}

/** The alphabet a Firebase uid is drawn from, for the sampling cursor. See `randomCursor`. */
const UID_ALPHABET = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

/**
 * A random point in document-id space, so the sample is a fresh window every run.
 *
 * Deliberately duplicated from `grove.ts` rather than shared: the two jobs sample different
 * collections for different reasons and neither should acquire a dependency on the other to
 * say so. `limit(n)` with no cursor would be the same players every run for ever.
 */
function randomCursor(): string {
  let out = "";
  for (let i = 0; i < 3; i++) {
    out += UID_ALPHABET[Math.floor(Math.random() * UID_ALPHABET.length)];
  }
  return out;
}

/**
 * Reads a bounded random sample of saves and republishes `config/chapterStandings`.
 *
 * **Projected to one field.** A save is the largest document this game writes, and all this
 * wants is `levels`. `select` costs the same reads (Firestore bills the index entry, not the
 * payload) and is the difference between five thousand whole saves in memory and five thousand
 * small maps.
 *
 * **The window wraps with `endBefore`**, for `sampleRanks`' reason: a cursor near the end of
 * the id space would otherwise return a short sample biased to the end of the alphabet, and
 * wrapping to the *start* would count every document from the cursor onwards twice on a
 * population smaller than the sample.
 *
 * Written whole with `set` rather than merged, so a chapter withdrawn from the catalog leaves
 * the table on the next run rather than standing for ever.
 */
export async function rebuildChapterStandings(): Promise<{ chapters: number; saves: number; usable: number }> {
  const db = getFirestore();
  const progression = await loadProgressionConfig();
  const chapters = chaptersOf(progression.levelChapters ?? {});

  const players = db.collection("players");
  const byId = FieldPath.documentId();
  const cursor = randomCursor();

  const first = await players.orderBy(byId).startAt(cursor)
                             .limit(STANDING_SAMPLE_SIZE).select("levels").get();
  const docs = [...first.docs];

  if (first.size < STANDING_SAMPLE_SIZE) {
    const rest = await players.orderBy(byId).endBefore(cursor)
                              .limit(STANDING_SAMPLE_SIZE - first.size).select("levels").get();
    docs.push(...rest.docs);
  }

  const collected = collectStandings(docs.map((doc) => doc.data() as { levels?: unknown }), chapters);
  const table = summariseStandings(collected, chapters);

  await db.doc(STANDINGS_PATH).set({
    chapters: table,
    sampledSaves: docs.length,
    minimumSamples: STANDING_MINIMUM_SAMPLES,
    builtUnix: Math.floor(Date.now() / 1000),
    builtAt: new Date().toISOString(),
  });

  const usable = Object.values(table).filter((c) => c.samples >= STANDING_MINIMUM_SAMPLES).length;
  logger.info("published chapter standings", {
    saves: docs.length, chapters: chapters.size, usable,
  });

  return { chapters: chapters.size, saves: docs.length, usable };
}
