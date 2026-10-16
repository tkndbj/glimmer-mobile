import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { SHUFFLE_REWARDS, shuffleWaves, shuffleXp, shuffleCredits } from "../lib/shuffle-rewards.js";
import { earnedCredits, earnedCreditsForSave } from "../lib/progression.js";
import { earnedKeeperLevel } from "../lib/keeper.js";
import { DEFAULT_KEEPER_CURVE, keeperLevel, xpBoostXp } from "../lib/grove.js";

const vectors = JSON.parse(readFileSync(new URL("../../shared/shuffle-reward-vectors.json", import.meta.url), "utf8"));
assert.deepEqual(SHUFFLE_REWARDS, vectors.constants);
for (const test of vectors.cases) {
  const save = { shuffleBest: test.rows };
  assert.equal(shuffleWaves(save), test.waves, test.name);
  assert.equal(shuffleXp(save), test.xp, test.name);
  assert.equal(shuffleCredits(save), test.credits, test.name);
  assert.ok(Number.isSafeInteger(shuffleCredits(save)), test.name);
}

// Gameplay tallies are untrusted wire input: unreadable numeric shapes must not mint money.
for (const waves of [NaN, Infinity, -Infinity, "10", {}, null, -1]) {
  assert.equal(shuffleCredits({ shuffleBest: [{ level: "s13_shuffle", rewards: [{ device: "a", waves }] }] }), 0);
}
for (const shuffleBest of [undefined, null, {}, "bad", [null, false, "bad"]]) {
  assert.equal(shuffleCredits({ shuffleBest }), 0);
}

const config = {
  version: 1,
  rewards: { xpFirstClear: 60, xpPerStar: 30, creditsFirstClear: 80, creditsPerStar: 40 },
  chapterRewards: {}, levelChapters: { s13_shuffle: "s13_shufflewatch" },
};
const save = {
  levels: { s13_shuffle: { stars: 2 } },
  shuffleBest: [{ level: "s13_shuffle", wave: 80, rewards: [{ device: "a", waves: 12 }, { device: "b", waves: 3 }] }],
  wallet: { xpBoostEarned: 10_000 },
};
assert.equal(earnedCreditsForSave(save, config).credits, earnedCredits(save.levels, config).credits + 150);
assert.equal(earnedCreditsForSave({ levels: save.levels }, config).credits, earnedCredits(save.levels, config).credits);
assert.equal(earnedKeeperLevel(save, config), keeperLevel(120 + 150 + xpBoostXp(save, config, 270), DEFAULT_KEEPER_CURVE));
assert.equal(earnedKeeperLevel({ shuffleBest: save.shuffleBest }, config), keeperLevel(150, DEFAULT_KEEPER_CURVE));
assert.equal(earnedCreditsForSave({}, config).credits, 0);
assert.equal(earnedCreditsForSave({ shuffleBest: [{ level: "invented", rewards: [{ device: "a", waves: 100 }] }] }, config).credits, 0);
console.log(`Shuffle rewards: ${vectors.cases.length} shared vectors and payout/keeper/boost regressions passed`);
