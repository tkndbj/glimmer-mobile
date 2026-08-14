#!/usr/bin/env node
/**
 * The server half of the shared reward contract.
 *
 *     npm --prefix firebase/functions run test
 *
 * Earned currency is derived twice — in C# so the game works offline, and here so a
 * forged save can be caught rather than merely disbelieved. Two implementations of one
 * rule drift, so both run firebase/shared/reward-vectors.json. Assets/Game/Tests/
 * RewardVectorTests.cs is the other half.
 *
 * If this goes red, the server would enforce different numbers than the game shows the
 * player — which surfaces as a balance that cannot be spent, and a support case that is
 * very hard to explain.
 */

import { readFileSync, existsSync } from "node:fs";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(HERE, "..", "..", "..");

const compiled = join(REPO, "firebase", "functions", "lib", "progression.js");
if (!existsSync(compiled)) {
  console.error("build the functions first: npm --prefix firebase/functions run build");
  process.exit(1);
}

const { earnedCredits, resolveRule, buildChapterRules, DEFAULT_RULE } =
  await import(pathToFileURL(compiled).href);

const vectors = JSON.parse(
  readFileSync(join(REPO, "firebase", "shared", "reward-vectors.json"), "utf8")
);

// Built exactly the way the seed script builds the live config, so the vectors test the
// deployed arrangement rather than a convenient stand-in.
const defaults = resolveRule(vectors.progression.rewards, DEFAULT_RULE);
const chapterRewards = buildChapterRules(vectors.progression.chapterRewards, defaults);

const levelChapters = {};
for (const entry of vectors.levelChapters ?? []) {
  levelChapters[entry.levelId] = entry.chapterId;
}

const config = { version: 1, rewards: defaults, chapterRewards, levelChapters };

let failures = 0;

for (const testCase of vectors.cases) {
  // The wire format is a map keyed by level id, so the vector's array is collapsed the
  // way a real document would be — which is also what makes the "duplicated level id"
  // case come out right without any special handling.
  const levels = {};
  for (const level of testCase.levels ?? []) {
    levels[level.levelId] = { stars: level.stars };
  }

  const { credits } = earnedCredits(levels, config);

  if (credits !== testCase.credits) {
    failures++;
    console.log(`  FAIL '${testCase.name}': credits expected ${testCase.credits}, got ${credits}`);
  } else {
    console.log(`  ok   ${testCase.name}`);
  }
}

// A guard against the vectors being quietly hollowed out. These are the cases where a
// naive implementation on either side would differ.
const names = vectors.cases.map((c) => (c.name ?? "").toLowerCase()).join(" | ");
for (const required of ["does not know", "duplicated", "clamped", "negative", "inherits", "pay nothing"]) {
  if (!names.includes(required)) {
    failures++;
    console.log(`  FAIL the vectors no longer cover '${required}'`);
  }
}

// ------------------------------------------------------------- daily chests
/**
 * The second contract in this file: the chest generator.
 *
 * A chest is rolled twice — by the client so the reward can be shown and spent while
 * offline, and here so the grant can be adjudicated without believing the client. If
 * the two ever disagree, a player watches a number change after a sync, which is the
 * single worst thing an economy can do in front of somebody.
 */
const dailyCompiled = join(REPO, "firebase", "functions", "lib", "daily.js");
const { rollChest } = await import(pathToFileURL(dailyCompiled).href);

const dailyConfig = vectors.dailyChestConfig;
const dailyCases = vectors.dailyChestCases ?? [];

if (!dailyConfig || dailyCases.length === 0) {
  failures++;
  console.log("  FAIL the daily chest vectors are missing");
}

let dailyFailures = 0;

for (const testCase of dailyCases) {
  const rolled = rollChest(dailyConfig, testCase.playerKey, testCase.dayKey, testCase.chestIndex);
  const got = rolled.map((d) => `${d.kind}=${d.amount}`).join(",");
  const want = (testCase.drops ?? []).map((d) => `${d.kind}=${d.amount}`).join(",");

  if (got !== want) {
    dailyFailures++;
    console.log(`  FAIL daily '${testCase.name}': expected ${want || "(nothing)"}, got ${got || "(nothing)"}`);
  }
}

failures += dailyFailures;
console.log(`  ${dailyCases.length - dailyFailures}/${dailyCases.length} daily chest vector(s) ok`);

console.log(`\n${vectors.cases.length} reward vector(s), ${dailyCases.length} chest vector(s), ` +
            `${failures} failure(s)`);

if (failures > 0) {
  console.log(
    "\nThe server no longer matches the shared vectors. If the change was intended, " +
    "update firebase/shared/reward-vectors.json and make the same change in " +
    "Assets/Game/Scripts/Domain/Progression/ProgressionLedger.cs or " +
    "Assets/Game/Scripts/Domain/Daily/DailyChestTable.cs."
  );
}

process.exit(failures === 0 ? 0 : 1);
