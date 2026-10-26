import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { normalPoints, normalScore, usableNormalScoring } from "../lib/normal-scores.js";
import { normalInputsChanged, sameNormalCard } from "../lib/normal-board.js";
import { buildCard, rowOf, readRows, figureOf, compareRows, LIVE_BOARD_IDS } from "../lib/grove.js";
import { readNormalScoring } from "../../seed/normal-config.mjs";

const vectors = JSON.parse(readFileSync(new URL("../../shared/normal-score-vectors.json", import.meta.url)));
for (const row of vectors.points)
  assert.equal(normalPoints(row.stars, row.moves, row.target), row.score, row.name);
const config = { version: 1, levels: [{ level: "s01_one", target: 20 }, { level: "s01_two", target: 10 }] };
const levels = { s01_one: { stars: 3, bestMoves: 15, clears: 99 },
                 s01_two: { stars: 1, bestMoves: 10 },
                 infinite: { stars: 3, bestMoves: 1 }, unknown: { stars: 999, bestMoves: 1 } };
assert.equal(normalScore(levels, config), 2500);
assert.equal(normalScore({ ...levels, s01_one: { ...levels.s01_one, clears: 1000 } }, config), 2500);
assert.equal(normalScore({}, config), 0);
assert.equal(normalScore(levels, undefined), 0);
assert.equal(normalScore(levels, { ...config, version: 2 }), 0);
assert.equal(usableNormalScoring({ ...config, levels: [...config.levels, config.levels[0]] }), false);
assert.equal(normalScore({ s01_one: { stars: 3, bestMoves: 0 } }, config), 1300);
assert.equal(normalScore({ s01_one: { stars: NaN, bestMoves: 1 } }, config), 0);
assert.equal(normalScore({ s01_one: { stars: 3, bestMoves: Infinity } }, config), 1300);
const progression = { version: 1, rewards: {}, chapterRewards: {}, levelChapters: {}, normalScoring: config };
const card = buildCard("uid", { levels }, { companions: {} }, progression, 1, 10, null);
assert.equal(card.normalScore, 2500);
assert.equal(figureOf(readRows([rowOf("uid", card)])[0], "normalScore"), 2500);
assert.equal(compareRows("normalScore", rowOf("a", card), rowOf("z", card)), 1);
assert.deepEqual(LIVE_BOARD_IDS, ["endless", "shuffle"], "Normal never enters the existing live-write path");
assert.equal(normalInputsChanged({ levels }, { levels, settings: { sfx: false } }), false);
assert.equal(normalInputsChanged({ levels }, { levels: { ...levels,
  s01_one: { ...levels.s01_one, clears: 1000, lastPlayedUnix: 99999 } } }), false);
assert.equal(normalInputsChanged({ levels }, { levels, settings: { board: 2 } }), true);
assert.equal(normalInputsChanged({ levels, wallet: { displayName: "Fern" } },
  { levels, wallet: { displayName: "Ash" } }), true);
assert.equal(normalInputsChanged({ levels }, { levels: { s01_one: { stars: 3, bestMoves: 14 } } }), true);
assert.equal(sameNormalCard(card, { ...card, builtUnix: 20 }), true);
assert.equal(sameNormalCard(card, { ...card, normalScore: 2501 }), false);
assert.equal(sameNormalCard(undefined, card), false);
const manifest = { chapters: [{ levels: ["s01_one", "s01_two"] },
  { track: "infinite", levels: ["infinite"] }, { disabled: true, levels: ["retired"] }] };
assert.deepEqual(readNormalScoring({ normalScoring: config }, manifest), config);
assert.throws(() => readNormalScoring({ normalScoring: config }, { chapters: [] }));
assert.throws(() => readNormalScoring({ normalScoring: config }, { chapters: [{ levels: ["s01_one"] }] }));
console.log(`Normal scores: ${vectors.points.length} shared vectors and projection/eligibility/retry checks passed`);
