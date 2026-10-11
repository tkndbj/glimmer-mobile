#!/usr/bin/env node
/**
 * The server half of the chapter standing's shared contract (invariant 62).
 *
 *     npm --prefix firebase/functions test
 *
 * A finished chapter's score is derived twice - in C# to fold the player's own save on the
 * handset, and here to fold everybody else's into the published cut points. Two copies of one
 * rule drift, and this one drifts silently: a player is told a number that is quietly about a
 * different population. So both run firebase/shared/grove-vectors.json;
 * Assets/Game/Tests/ChapterStandingTests.cs is the other half.
 *
 * The sampler's fold and the percentile cut are server-only by nature and are exercised here
 * too, because their inputs are the same saves this file already builds.
 */

import { readFileSync, existsSync } from "node:fs";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(HERE, "..", "..", "..");

const compiled = join(REPO, "firebase", "functions", "lib", "standings.js");
if (!existsSync(compiled)) {
  console.error("build the functions first: npm --prefix firebase/functions run build");
  process.exit(1);
}

const {
  chapterScore, chaptersOf, collectStandings, percentiles, summariseStandings,
  STAR_WEIGHT, MOVES_CAP, PERCENTILE_COUNT, STANDING_MINIMUM_SAMPLES,
} = await import(pathToFileURL(compiled).href);

const vectors = JSON.parse(readFileSync(join(REPO, "firebase", "shared", "grove-vectors.json"), "utf8"));

let pass = 0, fail = 0;
const check = (ok, what, detail = "") => {
  if (ok) { pass++; console.log("  ok  ", what); }
  else { fail++; console.log("  FAIL", what, detail ? " — " + detail : ""); }
};
const equal = (what, got, want) =>
  check(JSON.stringify(got) === JSON.stringify(want), what,
        `expected ${JSON.stringify(want)}, got ${JSON.stringify(got)}`);

// ------------------------------------------------------------------ the shared rule
console.log("\nthe score (shared vectors)");
{
  equal("the star weight is the one the client ships", STAR_WEIGHT, vectors.standingConstants.starWeight);
  equal("the move cap is the one the client ships", MOVES_CAP, vectors.standingConstants.movesCap);

  check((vectors.standingCases ?? []).length > 0, "the vector file has standing cases");
  for (const c of vectors.standingCases ?? []) {
    equal(c.name, chapterScore(c.levels, c.chapter), c.score);
  }
}

// ------------------------------------------------------------------ malformed saves
console.log("\nmalformed saves");
{
  const chapter = ["a", "b"];
  equal("no levels at all", chapterScore(undefined, chapter), null);
  equal("levels as an array (the pre-11a shape)", chapterScore([{ stars: 3 }], chapter), null);
  equal("a level entry that is not an object", chapterScore({ a: 3, b: { stars: 3, bestMoves: 1 } }, chapter), null);
  equal("stars as a string", chapterScore({ a: { stars: "3", bestMoves: 1 }, b: { stars: 3, bestMoves: 1 } }, chapter), null);
  equal("moves as a string count as the cap",
        chapterScore({ a: { stars: 3, bestMoves: "1" }, b: { stars: 3, bestMoves: 1 } }, chapter),
        6 * STAR_WEIGHT - MOVES_CAP - 1);
  equal("fractional stars floor", chapterScore({ a: { stars: 2.9, bestMoves: 1 }, b: { stars: 3, bestMoves: 1 } }, chapter),
        5 * STAR_WEIGHT - 2);
  equal("a NaN star count is not a clear",
        chapterScore({ a: { stars: NaN, bestMoves: 1 }, b: { stars: 3, bestMoves: 1 } }, chapter), null);
}

// ------------------------------------------------------------------ the catalog
console.log("\nchapters from levelChapters");
{
  const chapters = chaptersOf({ s01_b: "s01", s01_a: "s01", s02_x: "s02", "": "s03", s04_a: "" });
  equal("two chapters, the two malformed rows dropped", [...chapters.keys()].sort(), ["s01", "s02"]);
  equal("a chapter's levels are sorted", chapters.get("s01"), ["s01_a", "s01_b"]);
  equal("an empty map is no chapters", chaptersOf({}).size, 0);
}

// ------------------------------------------------------------------ the fold
console.log("\ncollecting a sample");
{
  const chapters = chaptersOf({ a1: "A", a2: "A", b1: "B" });
  const full = { levels: { a1: { stars: 3, bestMoves: 5 }, a2: { stars: 3, bestMoves: 5 }, b1: { stars: 1, bestMoves: 9 } } };
  const half = { levels: { a1: { stars: 3, bestMoves: 5 } } };
  const none = {};
  const odd = { levels: "nope" };

  const collected = collectStandings([full, half, none, odd, full], chapters);
  equal("chapter A has the two finished saves", collected.get("A"), [6 * STAR_WEIGHT - 10, 6 * STAR_WEIGHT - 10]);
  equal("chapter B has the two finished saves", collected.get("B"), [STAR_WEIGHT - 9, STAR_WEIGHT - 9]);

  const table = summariseStandings(collected, chapters);
  equal("every catalog chapter is published", Object.keys(table).sort(), ["A", "B"]);
  equal("a chapter's sample is its finisher count", table.A.samples, 2);

  const empty = summariseStandings(new Map(), chapters);
  equal("a chapter nobody finished is published with no cut points", empty.A, { samples: 0, percentiles: [] });
}

// ------------------------------------------------------------------ the cut
console.log("\npercentiles");
{
  const hundred = Array.from({ length: 100 }, (_, i) => i + 1);
  const cuts = percentiles(hundred);
  equal("ninety-nine cut points", cuts.length, PERCENTILE_COUNT);
  equal("p1 of 1..100 is 1", cuts[0], 1);
  equal("p50 of 1..100 is 50", cuts[49], 50);
  equal("p99 of 1..100 is 99", cuts[98], 99);
  equal("an empty list has no cut points", percentiles([]), []);
  equal("one value is every cut point", new Set(percentiles([7])).size, 1);

  const ascending = cuts.every((v, i) => i === 0 || v >= cuts[i - 1]);
  check(ascending, "cut points ascend");

  // Ties: half the population on one score. The share at or below that score is what the
  // client reads off the cut points, so the tied score has to occupy exactly its share.
  const tied = [...Array(50).fill(10), ...Array(50).fill(20)];
  const t = percentiles(tied);
  equal("the tied low score holds p1..p50", t.filter((v) => v === 10).length, 50);
  equal("and the high score holds the rest", t.filter((v) => v === 20).length, 49);

  check(STANDING_MINIMUM_SAMPLES > 0, "a minimum sample is published");
}

console.log(`\n${pass} passed, ${fail} failed`);
process.exit(fail ? 1 : 0);
