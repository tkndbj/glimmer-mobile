/**
 * The payer counters (invariant 64), exercised offline: what a purchase adds, that the counters only
 * climb, what a wallet reply always carries, and that a published product's reference cents reach the
 * grant the counters are raised from.
 *
 *   npm --prefix firebase/functions test
 */

import { addPurchase, payerReply, readPayer } from "../lib/payer.js";
import { readProduct } from "../lib/products.js";
import { toReply } from "../lib/wallet.js";

let pass = 0;
let fail = 0;

function check(name, condition) {
  if (condition) { console.log("  ok   " + name); pass++; }
  else { console.log("  FAIL " + name); fail++; }
}

console.log("payer counters");

const first = addPurchase(null, 99, 1000);
check("a first purchase counts one, its cents in all and at most", first.purchases === 1 && first.cents === 99 &&
      first.topCents === 99 && first.lastUnix === 1000);

const second = addPurchase(first, 799, 900);
check("a second adds to the total and raises the largest", second.purchases === 2 && second.cents === 898 &&
      second.topCents === 799);
check("the last purchase never moves backwards", second.lastUnix === 1000);

const third = addPurchase(second, 149, 2000);
check("a smaller purchase never lowers the largest", third.topCents === 799 && third.lastUnix === 2000);

check("junk cents count as nought, never as a negative", addPurchase(null, -50, 10).cents === 0 &&
      addPurchase(null, Number.NaN, 10).cents === 0);

check("an absent record reads as none", readPayer(undefined) === null && readPayer({}) === null &&
      readPayer("x") === null);
check("a stored record reads back", JSON.stringify(readPayer(third)) === JSON.stringify(third));
check("a damaged record reads whole numbers only", readPayer({ purchases: 2.7, cents: "9", topCents: -4 })?.purchases === 2 &&
      readPayer({ purchases: 2.7, cents: "9", topCents: -4 })?.cents === 0 &&
      readPayer({ purchases: 2.7, cents: "9", topCents: -4 })?.topCents === 0);

const none = payerReply(undefined);
check("a wallet that never paid still answers the four fields", none.payerPurchases === 0 && none.payerCents === 0 &&
      none.payerTopCents === 0 && none.payerLastUnix === 0);

const wallet = {
  credits: { granted: 10, spent: 0, confirmedThroughUnix: 0, earnedFloor: 0 },
  gems: { granted: 5, spent: 0, confirmedThroughUnix: 0, earnedFloor: 0 },
  streak: { paidThroughDay: 0, paidNight: 0 },
  payer: third,
};
const rows = toReply(wallet, {});
check("every reply row carries the payer fields", rows.length > 0 && rows.every((r) =>
  r.payerPurchases === 3 && r.payerCents === 1047 && r.payerTopCents === 799 && r.payerLastUnix === 2000));
check("a reply for a wallet with no record carries noughts, never nothing",
      toReply({ ...wallet, payer: undefined }, {}).every((r) => r.payerPurchases === 0 && "payerCents" in r));

const table = {
  gg_offer_1: { credits: 5000, gems: 300, kind: "consumable", capacity: 0, cents: 99 },
  gg_gems_1: { credits: 0, gems: 100, kind: "consumable", capacity: 0 },
};
check("a published offer grants its fixed contents and carries its cents", (() => {
  const g = readProduct(table, "gg_offer_1");
  return g.credits === 5000 && g.gems === 300 && g.cents === 99;
})());
check("a catalog published before cents reads them as nought", readProduct(table, "gg_gems_1").cents === 0);

console.log(`\n${pass} passed, ${fail} failed`);
if (fail > 0) process.exitCode = 1;
