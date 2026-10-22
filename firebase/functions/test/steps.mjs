// The server half of the first steps (`steps.ts`): a new player's chests for clearing a named
// level. What is under contract: the id's spelling (`GrantEntry.FirstStepId`), the seed's tag and
// subject (`FirstStepsLedger.SeedTag` / `Subject`), that the amount is re-rolled and never the
// client's, and that a claim the save does not yet vouch for is left unconfirmed, never refused.
//
// Run through `lib/`, the compiled output, so what is tested is what deploys.

import { existsSync, readFileSync } from "node:fs";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(HERE, "..", "..", "..");
const lib = (name) => join(REPO, "firebase", "functions", "lib", name);

if (!existsSync(lib("steps.js"))) {
  console.error("build first: npm run build (lib/steps.js is missing)");
  process.exit(1);
}

const {
  STEPS_SEED_TAG, STEPS_MAX_CHESTS, isStepGrantId, judgeStepClaim, parseStepClaim, rollStepChest,
  savedClear, stepChestValue, stepSubject, usableFirstSteps,
} = await import(pathToFileURL(lib("steps.js")).href);
const { subjectSeed, Rolls } = await import(pathToFileURL(lib("random.js")).href);
const { rollChestWith } = await import(pathToFileURL(lib("daily.js")).href);

let failed = 0;
const check = (ok, what, detail = "") => {
  if (ok) console.log("  ok   " + what);
  else { failed++; console.log("  FAIL " + what + (detail ? "  " + detail : "")); }
};
const equal = (what, got, want) =>
  check(JSON.stringify(got) === JSON.stringify(want), what, `got ${JSON.stringify(got)}, want ${JSON.stringify(want)}`);

// The task vectors' synthetic tiers: what is pinned is the seeding, never the shipped odds.
const vectors = JSON.parse(readFileSync(join(REPO, "firebase", "shared", "reward-vectors.json"), "utf8"));
const tasks = { tiers: vectors.markChestTiers };
const royal = vectors.markChestTiers[vectors.markChestTiers.length - 1];

console.log("steps: the contract strings");
equal("the seed tag is the client's", STEPS_SEED_TAG, "steps");
equal("the subject is level and chest", stepSubject("s03_blightfen", 1), "s03_blightfen:1");
equal("a step pays at most four chests", STEPS_MAX_CHESTS, 4);

console.log("steps: the id parses strictly");
equal("a well-formed id", parseStepClaim("steps:s03_blightfen:1:credits"),
      { level: "s03_blightfen", chest: 1, currency: "credits", dayKey: 0 });
check(isStepGrantId("steps:a:0:gems"), "the prefix is recognised");
check(!isStepGrantId("milestone:4:credits"), "another prefix is not");
for (const bad of [
  "steps:s03_blightfen:01:credits",        // a second spelling of chest 1
  "steps:s03_blightfen:4:credits",         // past the most a step pays
  "steps:s03_blightfen:-1:credits",
  "steps:S03_blightfen:0:credits",         // not a level id the client can spell
  "steps:_s03:0:credits",
  "steps:s03_blightfen:0",                 // no currency
  "steps:s03_blightfen:0:credits:extra",
  "steps::0:credits",
  "steps:" + "a".repeat(49) + ":0:credits",
]) check(parseStepClaim(bad) === null, `refused at parse: ${bad}`);

console.log("steps: the published block resolves whole or not at all");
const block = { steps: [{ level: "s03_blightfen", tier: royal.id, count: 2 }] };
equal("a good block", usableFirstSteps(block, tasks), block);
check(usableFirstSteps(undefined, tasks) === null, "absent pays nothing");
check(usableFirstSteps({ steps: [] }, tasks) === null, "empty pays nothing");
check(usableFirstSteps(block, null) === null, "no tasks block prices nothing");
check(usableFirstSteps({ steps: [{ level: "s03_blightfen", tier: "nope", count: 2 }] }, tasks) === null,
      "an unknown tier refuses the block");
check(usableFirstSteps({ steps: [{ level: "s03_blightfen", tier: royal.id, count: 5 }] }, tasks) === null,
      "too many chests refuses the block");
check(usableFirstSteps({ steps: [block.steps[0], block.steps[0]] }, tasks) === null,
      "a level named twice refuses the block");

console.log("steps: the chest is re-rolled from the account, the level and the index");
const direct = rollChestWith(royal.chest, (stream) => new Rolls(subjectSeed("player-1", "steps", "s03_blightfen:0", stream)));
equal("the roll is subjectSeed's under the tag and subject", rollStepChest(royal.chest, "player-1", "s03_blightfen", 0), direct);
check(JSON.stringify(rollStepChest(royal.chest, "player-1", "s03_blightfen", 0)) !==
      JSON.stringify(rollStepChest(royal.chest, "player-1", "s03_blightfen", 1)) ||
      JSON.stringify(rollStepChest(royal.chest, "player-2", "s03_blightfen", 0)) !==
      JSON.stringify(rollStepChest(royal.chest, "player-1", "s03_blightfen", 0)),
      "chests and accounts roll independently");
const credits = direct.filter((d) => d.kind === "credits").reduce((a, d) => a + d.amount, 0);
equal("the value is the roll's, in one currency", stepChestValue(royal.chest, "player-1", "s03_blightfen", 0, "credits"), credits);

console.log("steps: a claim is judged against the table and the save");
const config = usableFirstSteps(block, tasks);
const cleared = { levels: { s03_blightfen: { stars: 2 } } };
const claim = (chest) => parseStepClaim(`steps:s03_blightfen:${chest}:credits`);

equal("cleared and published pays", judgeStepClaim(config, tasks, claim(1), cleared).kind, "ok");
equal("the save not yet showing the clear waits", judgeStepClaim(config, tasks, claim(0), { levels: {} }).kind, "unknown");
equal("a starless row is no clear", judgeStepClaim(config, tasks, claim(0), { levels: { s03_blightfen: { stars: 0 } } }).kind, "unknown");
equal("no save waits", judgeStepClaim(config, tasks, claim(0), undefined).kind, "unknown");
equal("a chest past the count waits for a seed", judgeStepClaim(config, tasks, claim(2), cleared).kind, "unknown");
equal("a level the table does not name waits", judgeStepClaim(config, tasks,
      parseStepClaim("steps:s01_ironward:0:credits"), { levels: { s01_ironward: { stars: 3 } } }).kind, "unknown");
equal("no table waits", judgeStepClaim(null, tasks, claim(0), cleared).kind, "unknown");

check(savedClear({ levels: { a: { stars: 1 } } }, "a"), "one star is a clear");
check(!savedClear({ levels: [] }, "a"), "a list is not the levels map");

if (failed > 0) {
  console.log(`steps: ${failed} check(s) failed`);
  process.exit(1);
}
console.log("steps: all checks passed");
