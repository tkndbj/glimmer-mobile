/**
 * The seven-day passes, exercised offline: what a purchase opens, what a claim pays, what a
 * refund takes back, and what the catalog may publish. Every path here mints or reverses real
 * money, and none of it needs a store or a device to prove.
 *
 *   npm --prefix firebase/functions test
 */

import {
  MAX_PASS_DAYS, claimPiece, openRun, passReply, readDailyPasses, revokeRun, runKey,
} from "../lib/daily-pass.js";
import { readProduct } from "../lib/products.js";
import { revocationUpdate } from "../lib/refunds.js";

let pass = 0;
let fail = 0;

function check(name, condition) {
  if (condition) { console.log("  ok   " + name); pass++; }
  else { console.log("  FAIL " + name); fail++; }
}

function refuses(name, table, productId) {
  try {
    readProduct(table, productId);
    check(name, false);
  } catch {
    check(name, true);
  }
}

const GEMS = { credits: 0, gems: 500 };
const DAY = 20000;

console.log("== a purchase opens a run and pays its first piece");
{
  const passes = {};
  const paid = openRun(passes, "gg_gem_pass_7d", "google:GPA.1", GEMS, 7, DAY);
  check("the first piece is paid at once", paid.gems === 500 && paid.credits === 0);
  check("six pieces are left", passReply(passes)[0].remaining === 6);
  check("the day is today", passes.gg_gem_pass_7d.day === DAY);

  const again = claimPiece(passes, "gg_gem_pass_7d", DAY);
  check("a claim the same day pays nothing", again.outcome === "not_yet" && again.gems === 0);
  check("and leaves the run as it was", passReply(passes)[0].remaining === 6);
}

console.log("== one piece a day, oldest run first, nothing lost");
{
  const passes = {};
  openRun(passes, "gg_gem_pass_7d", "a", GEMS, 7, DAY);

  let total = 500;
  for (let d = 1; d <= 6; d++) {
    const claim = claimPiece(passes, "gg_gem_pass_7d", DAY + d * 2);   // every other day: no expiry
    check(`piece ${d + 1} is paid on a later day`, claim.outcome === "paid" && claim.gems === 500);
    total += claim.gems;
  }
  check("seven pieces in all", total === 3500);
  check("the run is gone once paid in full", passes.gg_gem_pass_7d === undefined);
  check("a claim after that is none", claimPiece(passes, "gg_gem_pass_7d", DAY + 99).outcome === "none");
  check("and the reply lists nothing", passReply(passes).length === 0);
}

console.log("== a second purchase while a run is open is paid in full too");
{
  const passes = {};
  openRun(passes, "gg_gem_pass_7d", "a", GEMS, 7, DAY);
  claimPiece(passes, "gg_gem_pass_7d", DAY + 1);
  openRun(passes, "gg_gem_pass_7d", "b", GEMS, 7, DAY + 1);

  check("both runs are owed: 5 + 6", passReply(passes)[0].remaining === 11);
  check("the purchase took today's slot", claimPiece(passes, "gg_gem_pass_7d", DAY + 1).outcome === "not_yet");

  let pieces = 0;
  for (let d = 2; d < 40; d++) if (claimPiece(passes, "gg_gem_pass_7d", DAY + d).outcome === "paid") pieces++;
  check("every owed piece is paid, one a day", pieces === 11);
}

console.log("== products are separate");
{
  const passes = {};
  openRun(passes, "gg_gem_pass_7d", "a", GEMS, 7, DAY);
  openRun(passes, "gg_coin_pass_7d", "b", { credits: 20000, gems: 0 }, 7, DAY);
  const coins = claimPiece(passes, "gg_coin_pass_7d", DAY + 1);
  check("a coin claim pays coins", coins.outcome === "paid" && coins.credits === 20000 && coins.gems === 0);
  check("and does not touch the gem pass", claimPiece(passes, "gg_gem_pass_7d", DAY + 1).outcome === "paid");
}

console.log("== the stored amounts are the ones bought");
{
  const passes = {};
  openRun(passes, "gg_gem_pass_7d", "a", { credits: 0, gems: 500 }, 7, DAY);
  const stored = readDailyPasses(JSON.parse(JSON.stringify(passes)));
  check("a run survives a round trip through the document", passReply(stored)[0].remaining === 6);
  check("and pays what it was bought at", claimPiece(stored, "gg_gem_pass_7d", DAY + 1).gems === 500);
}

console.log("== reading a hostile document");
{
  const read = readDailyPasses({
    gg_gem_pass_7d: { day: 5, runs: [{ tx: "a", days: 7, paid: 7, credits: 0, gems: 500 }] },
    "Bad Id": { day: 1, runs: [{ tx: "a", days: 7, paid: 1, credits: 0, gems: 500 }] },
    gg_x: { day: 1, runs: [{ tx: "a", days: 9999, paid: 1, credits: 0, gems: 500 }] },
    gg_y: { day: 1, runs: [{ tx: "a", days: 7, paid: 1, credits: 0, gems: 0 }] },
    gg_z: "nonsense",
  });
  check("a run paid in full is dropped", read.gg_gem_pass_7d === undefined);
  check("an id outside the alphabet is dropped", read["Bad Id"] === undefined);
  check("days are clamped", read.gg_x.runs[0].days === MAX_PASS_DAYS);
  check("a run paying nothing is dropped", read.gg_y === undefined);
  check("garbage is dropped", read.gg_z === undefined);
  check("absent is empty", Object.keys(readDailyPasses(undefined)).length === 0);
}

console.log("== a refund takes back exactly what the purchase paid");
{
  const passes = {};
  openRun(passes, "gg_gem_pass_7d", runKey("apple", "1"), GEMS, 7, DAY);
  claimPiece(passes, "gg_gem_pass_7d", DAY + 1);
  claimPiece(passes, "gg_gem_pass_7d", DAY + 2);       // three pieces paid in all

  const receipt = {
    uid: "u", productId: "gg_gem_pass_7d", capacity: 0, granted: { gems: 500 },
    pass: { days: 7, credits: 0, gems: 500 },
  };
  const wallet = { gems: { granted: 10000 }, credits: { granted: 0 }, dailyPasses: passes };
  const update = revocationUpdate(receipt, wallet, (id) => id, runKey("apple", "1"));

  check("three pieces are reversed (1,500)", update["gems.granted"] === 8500);
  check("the run is removed, so nothing more is paid", Object.keys(update.dailyPasses).length === 0);
  check("credits are untouched", update["credits.granted"] === undefined);
}
{
  const receipt = {
    uid: "u", productId: "gg_gem_pass_7d", capacity: 0, granted: { gems: 500 },
    pass: { days: 7, credits: 0, gems: 500 },
  };
  const wallet = { gems: { granted: 10000 }, credits: { granted: 0 } };
  const update = revocationUpdate(receipt, wallet, (id) => id, runKey("apple", "1"));
  check("a run paid in full and pruned is reversed in full (3,500)", update["gems.granted"] === 6500);
}
{
  const passes = {};
  openRun(passes, "gg_gem_pass_7d", "apple:1", GEMS, 7, DAY);
  openRun(passes, "gg_gem_pass_7d", "apple:2", GEMS, 7, DAY + 1);
  const left = revokeRun(passes, "gg_gem_pass_7d", "apple:2", { days: 7, credits: 0, gems: 500 });
  check("revoking one run leaves the other", passes.gg_gem_pass_7d.runs.length === 1 &&
        passes.gg_gem_pass_7d.runs[0].tx === "apple:1" && left.gems === 0);
}
{
  const receipt = { uid: "u", productId: "gg_gems_1", capacity: 0, granted: { gems: 100 } };
  const update = revocationUpdate(receipt, { gems: { granted: 300 } }, (id) => id, "apple:9");
  check("an ordinary product's refund is unchanged", update["gems.granted"] === 200 && update.dailyPasses === undefined);
}

console.log("== the catalog");
{
  const TABLE = {
    gg_gem_pass_7d: { credits: 0, gems: 500, kind: "consumable", passDays: 7 },
    gg_one_day: { credits: 0, gems: 500, kind: "consumable", passDays: 1 },
    gg_long: { credits: 0, gems: 500, kind: "consumable", passDays: 32 },
    gg_once: { credits: 0, gems: 500, kind: "nonconsumable", passDays: 7 },
    gg_huge: { credits: 0, gems: 1_000_000, kind: "consumable", passDays: 7 },
    gg_gems_1: { credits: 0, gems: 100, kind: "consumable" },
  };
  const grant = readProduct(TABLE, "gg_gem_pass_7d");
  check("a pass is read with its days", grant.passDays === 7 && grant.gems === 500);
  check("an ordinary product has no days", readProduct(TABLE, "gg_gems_1").passDays === 0);
  refuses("a pass of one day is refused", TABLE, "gg_one_day");
  refuses("a pass past the bound is refused", TABLE, "gg_long");
  refuses("a non-consumable pass is refused", TABLE, "gg_once");
  refuses("a pass paying past the grant bound in all is refused", TABLE, "gg_huge");
}

console.log(`\n${pass} passed, ${fail} failed`);
if (fail > 0) process.exit(1);
