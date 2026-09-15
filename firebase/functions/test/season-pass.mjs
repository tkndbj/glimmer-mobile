/**
 * The season pass: what it costs, how a debit becomes an entitlement, and what the ladder
 * has to say for the server to price any of it.
 *
 * <p>What used to be here was a <em>purchase</em> — a $4.99 non-consumable, a verified
 * receipt, `redeemPurchase` writing the entitlement and `revokeReceipt` reversing exactly
 * what the paid column had paid. A pass is bought with gems now, which is an ordinary
 * spend (invariant 18), and the whole of that apparatus went with the product: no receipt,
 * no store registration, no refund reversal, no paid-so-far tally.</p>
 *
 * <p>What replaced it is one rule with teeth: a spend is an amount the <em>client</em>
 * chooses, so `submitSpends` refuses `pass:{season}` unless it is at least the published
 * price in gems — and writes the entitlement in the same transaction that takes them, so
 * the purchase and the permission cannot come apart.</p>
 */

import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { initializeApp } from "firebase-admin/app";
import { holdsPass } from "../lib/event-pass.js";
import {
  MAX_SEASON_INDEX, SEASON_INDEX_DIGITS, SEASON_TRACKS, markSubject, findRung, isMarkGrantId,
  isPassSpendId, judgeMarkClaim, parseMarkClaim, parsePassSpendId, passPrice, seasonCycleIndex,
  tierIdOn, usableSeason,
} from "../lib/season.js";
import { usableTaskConfig } from "../lib/tasks.js";
import { readProduct } from "../lib/products.js";

initializeApp({ projectId: "season-pass-unit-tests" });

const manifest = JSON.parse(readFileSync(
  new URL("../../../Assets/StreamingAssets/Content/manifest.json", import.meta.url)));
const progression = JSON.parse(readFileSync(
  new URL("../../../Assets/StreamingAssets/Content/progression.json", import.meta.url)));

// The shipped season *repeats* (`SeasonCycle`), so `cycle` is the authored entry — a stem,
// a window describing cycle nought and a ladder — and `season` is one concrete cycle of it,
// which is what every id in this file names and what the server ever actually prices.
const cycle = manifest.events.find(e => e.id === "watch");
const PERIOD = cycle.endUnix - cycle.startUnix;

const SEASON = "watch_0000";
const NOW = cycle.startUnix;                       // inside cycle nought
const config = { events: [cycle] };

const season = usableSeason(config, SEASON, NOW);
assert.ok(season, "cycle nought of the shipped season resolves");

// ------------------------------------------------------------------ the price
assert.equal(season.milestones.length, 40, "forty rungs");
assert.ok(season.passGems > 0, "and a gem price to sell the paid column at");
assert.equal(passPrice(season), season.passGems);
assert.equal(passPrice({ ...season, passGems: 0 }), 0, "nought is a free-track-only season");
assert.equal(passPrice({ ...season, passGems: -1 }), 0);
assert.equal(passPrice({ ...season, passGems: 1.5 }), 0);
assert.equal(passPrice({ ...season, passGems: 1e9 }), 0, "and an absurd one sells nothing");

// No real-money product may carry the entitlement any more. Refused by name rather than
// merely unread: such a product would take money and unlock nothing.
assert.throws(() => readProduct(
  { gg_pass: { credits: 0, gems: 0, kind: "nonconsumable", capacity: 0, eventPassId: SEASON } },
  "gg_pass"));

assert.equal(progression.store.products.some(p => p.eventPassId || p.shelf === "event_pass"),
             false, "and none ships");

// ------------------------------------------------------------- the debit's id
assert.equal(isPassSpendId(`pass:${SEASON}`), true);
assert.equal(isPassSpendId(`mark:${SEASON}:pass:200:credits`), false);
assert.equal(parsePassSpendId(`pass:${SEASON}`), SEASON);

// Malformed or non-canonical names no purchase. The round-trip check is the one that
// matters: two ids naming one pass would be two debits for one entitlement.
for (const bad of ["pass:", "pass:Watch_0000", "pass:watch 0000", `pass:${SEASON}:extra`,
                   "pass:" + "x".repeat(70), "spend:watch_0000"])
  assert.equal(parsePassSpendId(bad), null, bad);

// -------------------------------------------------------------- the entitlement
assert.equal(holdsPass({ owned: true, seasonId: SEASON }, season), true);
assert.equal(holdsPass({ owned: true, seasonId: "other" }, season), false,
             "an entitlement written against another season is not this one's");
assert.equal(holdsPass({ owned: false, seasonId: SEASON }, season), false);
assert.equal(holdsPass(undefined, season), false);
assert.equal(holdsPass({ owned: true, seasonId: SEASON }, undefined), false);

// ------------------------------------------------------------------ the ladder
const tasks = usableTaskConfig(progression.tasks);

assert.ok(tasks, "the shipped tasks block is usable, or no rung could be priced");
assert.equal(usableSeason(config, "no_such_season", NOW), null);
assert.equal(usableSeason(config, "watch", NOW), null,
             "the stem alone names no season; every cycle carries its number");

// Everything the reader refuses. Each one is a ladder the client would also have refused,
// and a config the client refuses is a config this server would quietly disagree with.
for (const bad of [
  { ...season, milestones: [] },
  { ...season, milestones: [...season.milestones, ...season.milestones] },     // over forty
  { ...season, milestones: [{ goal: 10, tier: "wood" }, { goal: 10, tier: "wood" }] },
  { ...season, milestones: [{ goal: 0, tier: "wood" }] },
  { ...season, milestones: [{ goal: 10, tier: "Wood" }] },
  { ...season, milestones: [{ goal: 10, tier: "wood", premiumTier: "../x" }] },
  { ...season, endUnix: season.startUnix },
])
  assert.equal(usableSeason({ events: [{ ...bad, id: "watch", repeats: true }] }, SEASON, NOW), null);

const top = season.milestones[season.milestones.length - 1];
assert.equal(findRung(season, top.goal).tier, top.tier);
assert.equal(findRung(season, top.goal + 1), null, "a rung is found by its goal, not its position");
assert.equal(tierIdOn(top, "free"), top.tier);
assert.equal(tierIdOn(top, "pass"), top.premiumTier);

// ------------------------------------------------------------------- the claim
assert.equal(isMarkGrantId(`mark:${SEASON}:pass:200:credits`), true);
assert.equal(isMarkGrantId("task:daily:0:d_play:credits"), false);

const claim = parseMarkClaim(`mark:${SEASON}:pass:200:credits`);
assert.deepEqual(claim, { seasonId: SEASON, track: "pass", goal: 200,
                          currency: "credits", dayKey: 0 });
assert.equal(markSubject(claim.seasonId, claim.track, claim.goal), `${SEASON}:pass:200`);

for (const bad of [`mark:${SEASON}:pass:200`, `mark:${SEASON}:premium:200:credits`,
                   `mark:${SEASON}:pass:0200:credits`, `mark:${SEASON}:pass:0:credits`,
                   "mark:Watch_0000:pass:200:credits", "mark::pass:200:credits",
                   `mark:${SEASON}:pass:200:` + "c".repeat(40)])
  assert.equal(parseMarkClaim(bad), null, bad);

// The verdict. `refuse` is permanent and `unknown` is "not yet" — a claim the client keeps
// resending until a content push arrives (invariant 13a).
//
// The goal is read off the shipped ladder rather than typed: the step between rungs is
// content (`Tools/author_season.py`), and a number written here goes stale the first time
// it moves.
const firstGoal = season.milestones[0].goal;
const free = { seasonId: SEASON, track: "free", goal: firstGoal,
               currency: "credits", dayKey: 0 };
const paid = { ...free, track: "pass" };

assert.equal(judgeMarkClaim(free, config, tasks, false, NOW).kind, "pay");
assert.equal(judgeMarkClaim(paid, config, tasks, true, NOW).kind, "pay");
assert.equal(judgeMarkClaim(paid, config, tasks, false, NOW).kind, "refuse",
             "the paid column without the purchase is refused for good");
assert.equal(judgeMarkClaim(paid, { events: [{ ...cycle, passGems: 0 }] }, tasks, true, NOW).kind,
             "refuse", "and so is a paid claim on a season that sells no pass");
assert.equal(judgeMarkClaim({ ...free, goal: firstGoal + 1 }, config, tasks, false, NOW).kind, "unknown",
             "a goal no rung asks for is left alone rather than refused");
assert.equal(judgeMarkClaim(free, { events: [] }, tasks, false, NOW).kind, "unknown");
assert.equal(judgeMarkClaim(free, config, null, false, NOW).kind, "unknown");

assert.equal(judgeMarkClaim(free, config, tasks, false, NOW).tierId,
             findRung(season, firstGoal).tier, "and it pays the tier the ladder names");

assert.deepEqual([...SEASON_TRACKS], ["free", "pass"], "the track ids are contract");

// ------------------------------------------------------------- the recurrence
// A season that runs for ever needs a bound that is not "the ladder is a finite list",
// because it no longer is. The replacement is the clock: a cycle that has not opened does
// not exist, so the most any save can extract is one ladder per elapsed period.
assert.equal(cycle.repeats, true, "the shipped season repeats");
assert.ok(PERIOD > 0);

// The id arithmetic, which is contract with `SeasonCycle` on the client.
assert.equal(SEASON_INDEX_DIGITS, 4);
assert.equal(MAX_SEASON_INDEX, 9999);
assert.equal(seasonCycleIndex("watch", "watch_0000"), 0);
assert.equal(seasonCycleIndex("watch", "watch_0037"), 37);
assert.equal(seasonCycleIndex("watch", "watch_9999"), 9999);

// Every other spelling names nothing. Two spellings of one cycle would be two sets of
// grant-log keys for one ladder, which is the bound quietly doubling.
for (const bad of ["watch_37", "watch_00037", "watch_-001", "watch_", "watch",
                   "watch_abcd", "Watch_0000", "watch_0000 ", "other_0000"])
  assert.equal(seasonCycleIndex("watch", bad), -1, bad);

// A cycle that has opened resolves, and its window is its own rather than cycle nought's.
const later = usableSeason(config, "watch_0003", cycle.startUnix + 3 * PERIOD);
assert.ok(later, "an open cycle resolves");
assert.equal(later.id, "watch_0003");
assert.equal(later.startUnix, cycle.startUnix + 3 * PERIOD);
assert.equal(later.endUnix, cycle.startUnix + 4 * PERIOD);
assert.deepEqual(later.milestones, cycle.milestones, "and pays the same ladder");
assert.equal(later.passGems, cycle.passGems, "at the same price");

// A cycle that has closed still resolves — a season's chests never expire (invariant 47c).
assert.ok(usableSeason(config, "watch_0000", cycle.startUnix + 9 * PERIOD),
          "a closed cycle is still priceable, because its chests do not expire");

// A cycle that has not opened does not. This is the bound.
assert.equal(usableSeason(config, "watch_0001", NOW), null, "the next cycle is not open yet");
assert.equal(usableSeason(config, "watch_9999", NOW), null, "nor is a forged far-future one");
assert.equal(usableSeason(config, "watch_0001", cycle.startUnix + PERIOD - 1), null,
             "not one second early");
assert.ok(usableSeason(config, "watch_0001", cycle.startUnix + PERIOD),
          "and exactly on the boundary it is");

// A claim against an unopened cycle is left *unconfirmed*, never refused: "it has not
// started" stops being true on its own, and a clock skew either side of a rollover makes an
// honest claim look early (invariant 13a).
const future = { seasonId: "watch_0001", track: "free", goal: firstGoal,
                 currency: "credits", dayKey: 0 };
assert.equal(judgeMarkClaim(future, config, tasks, false, NOW).kind, "unknown");
assert.equal(judgeMarkClaim(future, config, tasks, false, cycle.startUnix + PERIOD).kind, "pay",
             "and it pays itself the moment that cycle opens");

// An authored one-off season alongside the recurrence is still found, and is not parsed as
// a cycle of it.
const oneOff = { ...cycle, id: "yule_feast", repeats: false };
assert.ok(usableSeason({ events: [cycle, oneOff] }, "yule_feast", NOW));
assert.equal(seasonCycleIndex("watch", "yule_feast"), -1);

console.log("Season pass: price, debit id, entitlement, ladder, claim parsing and verdicts passed.");
