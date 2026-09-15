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
  SEASON_TRACKS, markSubject, findRung, isMarkGrantId, isPassSpendId, judgeMarkClaim,
  parseMarkClaim, parsePassSpendId, passPrice, tierIdOn, usableSeason,
} from "../lib/season.js";
import { usableTaskConfig } from "../lib/tasks.js";
import { readProduct } from "../lib/products.js";

initializeApp({ projectId: "season-pass-unit-tests" });

const manifest = JSON.parse(readFileSync(
  new URL("../../../Assets/StreamingAssets/Content/manifest.json", import.meta.url)));
const progression = JSON.parse(readFileSync(
  new URL("../../../Assets/StreamingAssets/Content/progression.json", import.meta.url)));

const season = manifest.events.find(e => e.id === "first_watch");

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
  { gg_pass: { credits: 0, gems: 0, kind: "nonconsumable", capacity: 0, eventPassId: "first_watch" } },
  "gg_pass"));

assert.equal(progression.store.products.some(p => p.eventPassId || p.shelf === "event_pass"),
             false, "and none ships");

// ------------------------------------------------------------- the debit's id
assert.equal(isPassSpendId("pass:first_watch"), true);
assert.equal(isPassSpendId("mark:first_watch:pass:200:credits"), false);
assert.equal(parsePassSpendId("pass:first_watch"), "first_watch");

// Malformed or non-canonical names no purchase. The round-trip check is the one that
// matters: two ids naming one pass would be two debits for one entitlement.
for (const bad of ["pass:", "pass:First_Watch", "pass:first watch", "pass:first_watch:extra",
                   "pass:" + "x".repeat(70), "spend:first_watch"])
  assert.equal(parsePassSpendId(bad), null, bad);

// -------------------------------------------------------------- the entitlement
assert.equal(holdsPass({ owned: true, seasonId: "first_watch" }, season), true);
assert.equal(holdsPass({ owned: true, seasonId: "other" }, season), false,
             "an entitlement written against another season is not this one's");
assert.equal(holdsPass({ owned: false, seasonId: "first_watch" }, season), false);
assert.equal(holdsPass(undefined, season), false);
assert.equal(holdsPass({ owned: true, seasonId: "first_watch" }, undefined), false);

// ------------------------------------------------------------------ the ladder
const config = { events: [season] };
const tasks = usableTaskConfig(progression.tasks);

assert.ok(tasks, "the shipped tasks block is usable, or no rung could be priced");
assert.ok(usableSeason(config, "first_watch"), "the shipped season is usable");
assert.equal(usableSeason(config, "no_such_season"), null);

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
  assert.equal(usableSeason({ events: [bad] }, season.id), null);

const top = season.milestones[season.milestones.length - 1];
assert.equal(findRung(season, top.goal).tier, top.tier);
assert.equal(findRung(season, top.goal + 1), null, "a rung is found by its goal, not its position");
assert.equal(tierIdOn(top, "free"), top.tier);
assert.equal(tierIdOn(top, "pass"), top.premiumTier);

// ------------------------------------------------------------------- the claim
assert.equal(isMarkGrantId("mark:first_watch:pass:200:credits"), true);
assert.equal(isMarkGrantId("task:daily:0:d_play:credits"), false);

const claim = parseMarkClaim("mark:first_watch:pass:200:credits");
assert.deepEqual(claim, { seasonId: "first_watch", track: "pass", goal: 200,
                          currency: "credits", dayKey: 0 });
assert.equal(markSubject(claim.seasonId, claim.track, claim.goal), "first_watch:pass:200");

for (const bad of ["mark:first_watch:pass:200", "mark:first_watch:premium:200:credits",
                   "mark:first_watch:pass:0200:credits", "mark:first_watch:pass:0:credits",
                   "mark:First_Watch:pass:200:credits", "mark::pass:200:credits",
                   "mark:first_watch:pass:200:" + "c".repeat(40)])
  assert.equal(parseMarkClaim(bad), null, bad);

// The verdict. `refuse` is permanent and `unknown` is "not yet" — a claim the client keeps
// resending until a content push arrives (invariant 13a).
//
// The goal is read off the shipped ladder rather than typed: the step between rungs is
// content (`Tools/author_season.py`), and a number written here goes stale the first time
// it moves.
const firstGoal = season.milestones[0].goal;
const free = { seasonId: "first_watch", track: "free", goal: firstGoal,
               currency: "credits", dayKey: 0 };
const paid = { ...free, track: "pass" };

assert.equal(judgeMarkClaim(free, config, tasks, false).kind, "pay");
assert.equal(judgeMarkClaim(paid, config, tasks, true).kind, "pay");
assert.equal(judgeMarkClaim(paid, config, tasks, false).kind, "refuse",
             "the paid column without the purchase is refused for good");
assert.equal(judgeMarkClaim(paid, { events: [{ ...season, passGems: 0 }] }, tasks, true).kind,
             "refuse", "and so is a paid claim on a season that sells no pass");
assert.equal(judgeMarkClaim({ ...free, goal: firstGoal + 1 }, config, tasks, false).kind, "unknown",
             "a goal no rung asks for is left alone rather than refused");
assert.equal(judgeMarkClaim(free, { events: [] }, tasks, false).kind, "unknown");
assert.equal(judgeMarkClaim(free, config, null, false).kind, "unknown");

assert.equal(judgeMarkClaim(free, config, tasks, false).tierId,
             findRung(season, firstGoal).tier, "and it pays the tier the ladder names");

assert.deepEqual([...SEASON_TRACKS], ["free", "pass"], "the track ids are contract");

console.log("Season pass: price, debit id, entitlement, ladder, claim parsing and verdicts passed.");
