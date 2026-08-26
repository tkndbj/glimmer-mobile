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

const { earnedCredits, resolveRule, buildChapterRules, DEFAULT_RULE, goldenPercent } =
  await import(pathToFileURL(compiled).href);

const { usableWheelConfig, wheelLanding, applyWheelPercent, WHEEL_MIN_PERCENT } =
  await import(pathToFileURL(join(REPO, "firebase", "functions", "lib", "wheel.js")).href);

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

// The golden bands ride inside the vector file's progression block rather than beside
// it, because unlike a chest's drop table the multiplier is part of the credit derivation
// itself — the same table has to be in force for the end-to-end cases to mean anything.
const config = {
  version: 1, rewards: defaults, chapterRewards, levelChapters,
  golden: vectors.progression.golden?.bands,
  events: vectors.events,
};

let failures = 0;

for (const testCase of vectors.cases) {
  // The wire format is a map keyed by level id, so the vector's array is collapsed the
  // way a real document would be — which is also what makes the "duplicated level id"
  // case come out right without any special handling.
  const levels = {};
  for (const level of testCase.levels ?? []) {
    levels[level.levelId] = { stars: level.stars, firstClearedUnix: level.firstClearedUnix ?? 0 };
  }

  // The floors go in as the wire shape a save document carries, so the harness exercises
  // `eventFloors` too rather than handing the derivation a tidy map it would never see.
  const { credits } = earnedCredits(levels, config, testCase.playerKey ?? "",
                                    testCase.collected ?? []);

  if (credits !== testCase.credits) {
    failures++;
    console.log(`  FAIL '${testCase.name}': credits expected ${testCase.credits}, got ${credits}`);
  } else {
    console.log(`  ok   ${testCase.name}`);
  }
}

// The golden picker on its own. A glade's multiplier is a pure function of (account,
// level) and the client derives the same number without being told — a disagreement is a
// balance that moves after a sync, in front of a player, for no reason they can see.
for (const testCase of vectors.goldenCases ?? []) {
  const got = goldenPercent(testCase.playerKey, testCase.levelId,
                             vectors.progression.golden?.bands);

  if (got !== testCase.percent) {
    failures++;
    console.log(`  FAIL golden '${testCase.name}': expected ${testCase.percent}%, got ${got}%`);
  } else {
    console.log(`  ok   golden ${testCase.name}`);
  }
}

if (!(vectors.goldenCases ?? []).length) {
  failures++;
  console.log("  FAIL the vector file has no golden cases");
} else if (new Set(vectors.goldenCases.map((c) => c.percent)).size < 3) {
  failures++;
  console.log("  FAIL the golden vectors reach fewer than three bands, so they would not " +
              "notice a picker that had stopped picking");
}

// A guard against the vectors being quietly hollowed out. These are the cases where a
// naive implementation on either side would differ.
const names = vectors.cases.map((c) => (c.name ?? "").toLowerCase()).join(" | ");
for (const required of ["does not know", "duplicated", "clamped", "negative", "inherits", "pay nothing", "golden", "outside its window", "whole track"]) {
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

/*
 * The streak ladder.
 *
 * Same contract as the chests and the same reason for it: the board shows a night's reward
 * and this server grants it, so the two lookups have to be one lookup. What these pin
 * beyond the arithmetic is the *lap* — night eight pays night one — and the shared per-kind
 * ceilings, which is why the vector ladder deliberately overreaches on two nights.
 */
const streakCompiled = join(REPO, "firebase", "functions", "lib", "streak.js");
const { rungFor, usableStreakConfig, advances } = await import(pathToFileURL(streakCompiled).href);

const streakLadder = vectors.streakLadder;
const streakCases = vectors.streakCases ?? [];

if (!streakLadder || streakCases.length === 0) {
  failures++;
  console.log("  FAIL the streak vectors are missing");
}

if (streakLadder && !usableStreakConfig(streakLadder)) {
  failures++;
  console.log("  FAIL the server refuses the vector streak ladder outright");
}

let streakFailures = 0;

for (const testCase of streakCases) {
  const rung = rungFor(streakLadder, testCase.night);
  const got = rung.kind ? `${rung.kind}=${rung.amount}` : "(nothing)";
  const want = testCase.kind ? `${testCase.kind}=${testCase.amount}` : "(nothing)";

  if (got !== want) {
    streakFailures++;
    console.log(`  FAIL streak '${testCase.name}' (night ${testCase.night}): expected ${want}, got ${got}`);
  }
}

if (streakCases.filter((c) => c.night > (streakLadder?.rungs?.length ?? 0)).length < 3) {
  streakFailures++;
  console.log("  FAIL fewer than three streak vectors fall past the end of the ladder, so they " +
              "would not notice the lap being lost");
}

failures += streakFailures;
console.log(`  ${streakCases.length - streakFailures}/${streakCases.length} streak vector(s) ok`);

/*
 * The rule that bounds a streak claim. Server-only — the client never judges its own claim
 * — so it has no shared vectors and is pinned here instead.
 *
 * What it has to get right is one sentence: a night may climb only as fast as the calendar.
 * The cases below are the ones that were reasoned about when it was written, and the two
 * that matter most are the last two — an honest player collecting a backlog out of order,
 * and a save edited to claim the top rung every morning.
 */
const NEVER_PAID = { paidThroughDay: 0, paidNight: 0 };
const PAID_5_ON_100 = { paidThroughDay: 100, paidNight: 5 };

const advanceCases = [
  ["an account never paid accepts anything, once", NEVER_PAID, 20000, 7, true],
  ["a fresh floor accepts night one today", { paidThroughDay: 99, paidNight: 0 }, 100, 1, true],
  ["a fresh floor refuses night five today", { paidThroughDay: 99, paidNight: 0 }, 100, 5, false],
  ["the next night, the next day", PAID_5_ON_100, 101, 6, true],
  ["three days on, three nights on", PAID_5_ON_100, 103, 8, true],
  ["a streak that broke and restarted", PAID_5_ON_100, 103, 1, true],
  ["a restart may be as long as the gap", PAID_5_ON_100, 103, 3, true],
  ["a restart may not outrun the gap", PAID_5_ON_100, 103, 4, false],
  ["a backlog submitted out of order still adds up", PAID_5_ON_100, 98, 3, true],
  ["the top rung, every single morning", PAID_5_ON_100, 101, 5, false],
  ["night zero is not a night", PAID_5_ON_100, 101, 0, false],

  // Permitted here on purpose, and worth pinning so nobody "fixes" it. Re-submitting the
  // night we just paid satisfies the calendar (zero days on, zero nights on) and is stopped
  // one layer up, by grantLog/{id} — which is the right layer, because that is the one that
  // also stops it across devices, across reinstalls and after a dropped reply. Tightening
  // this to refuse it would buy nothing and would break the out-of-order backlog above,
  // which relies on the same arithmetic running backwards.
  ["the night we just paid is the grant log's problem, not this one", PAID_5_ON_100, 100, 5, true],
];

let advanceFailures = 0;

for (const [name, floor, day, night, want] of advanceCases) {
  const got = advances(floor, day, night);
  if (got !== want) {
    advanceFailures++;
    console.log(`  FAIL advance '${name}': expected ${want}, got ${got}`);
  }
}

failures += advanceFailures;
console.log(`  ${advanceCases.length - advanceFailures}/${advanceCases.length} streak advance case(s) ok`);

// --------------------------------------------------------------- bonus wheel
/**
 * The third contract in this file: the wheel a won glade spins for its video bonus.
 *
 * The wheel is `win_bonus`'s payout made variable, and neither side is told what the other
 * decided. The phone draws where the wheel stopped before the video plays; this server
 * recomputes the same slice when the ad network's callback lands, and grants its own
 * figure. A disagreement is a player watching a wheel stop on nine hundred and then
 * watching their balance rise by two hundred, which is the worst thing an economy can do
 * in front of somebody. See invariant 9c.
 */
const wheelCases = vectors.wheelCases ?? [];
const wheel = usableWheelConfig(vectors.wheelConfig);

let wheelFailures = 0;

if (!wheel) {
  wheelFailures++;
  console.log("  FAIL the vector wheel is not one this server accepts");
} else if (!wheelCases.length) {
  wheelFailures++;
  console.log("  FAIL the vector file has no wheel cases");
} else if (!(vectors.wheelBasis > 0)) {
  wheelFailures++;
  console.log("  FAIL the vector file has no flat amount for the wheel to multiply");
} else {
  for (const testCase of wheelCases) {
    const landing = wheelLanding(testCase.playerKey, testCase.dayKey, testCase.spinIndex, wheel);

    if (landing !== testCase.landing) {
      wheelFailures++;
      console.log(`  FAIL wheel '${testCase.name}': slice expected ${testCase.landing}, ` +
                  `got ${landing}`);
      continue;
    }

    const percent = landing < 0 ? WHEEL_MIN_PERCENT : wheel.slices[landing].percent;
    if (percent !== testCase.percent) {
      wheelFailures++;
      console.log(`  FAIL wheel '${testCase.name}': expected ${testCase.percent}%, got ${percent}%`);
      continue;
    }

    const pays = applyWheelPercent(vectors.wheelBasis, percent);
    if (pays !== testCase.pays) {
      wheelFailures++;
      console.log(`  FAIL wheel '${testCase.name}': pays expected ${testCase.pays}, got ${pays}`);
    }
  }

  // The pre-sign-in row is the one that matters most and the easiest to lose: a client
  // rolling against a device id while this server rolls against a uid is the only way the
  // feature could pay two different numbers for one video.
  if (!wheelCases.some((c) => !c.playerKey && c.landing === -1)) {
    wheelFailures++;
    console.log("  FAIL the wheel vectors no longer cover the pre-sign-in case");
  }

  // And a set that never leaves one slice would not notice a picker that had stopped
  // picking - the same guard the golden bands carry above.
  if (new Set(wheelCases.map((c) => c.landing)).size < 4) {
    wheelFailures++;
    console.log("  FAIL the wheel vectors reach fewer than four slices");
  }
}

failures += wheelFailures;
console.log(`  ${wheelCases.length - wheelFailures}/${wheelCases.length} wheel vector(s) ok`);

console.log(`\n${vectors.cases.length} reward vector(s), ${dailyCases.length} chest vector(s), ` +
            `${streakCases.length} streak vector(s), ${wheelCases.length} wheel vector(s), ` +
            `${failures} failure(s)`);

if (failures > 0) {
  console.log(
    "\nThe server no longer matches the shared vectors. If the change was intended, " +
    "update firebase/shared/reward-vectors.json and make the same change in " +
    "Assets/Game/Scripts/Domain/Progression/ProgressionLedger.cs, " +
    "Assets/Game/Scripts/Domain/Daily/DailyChestTable.cs, " +
    "Assets/Game/Scripts/Domain/Daily/StreakTable.cs or " +
    "Assets/Game/Scripts/Domain/Ads/BonusWheel.cs."
  );
}

process.exit(failures === 0 ? 0 : 1);
