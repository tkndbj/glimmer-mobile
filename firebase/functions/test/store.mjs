/**
 * The two pieces of the shop that decide money, exercised offline.
 *
 * `readProduct` is the only thing that says what a validated receipt is worth, and
 * `transactionIdsIn` is the only thing standing between an unauthenticated HTTP endpoint
 * and a wallet being reduced. Neither needs a store, a device or a real card to check —
 * and a routine nobody can run without one is a routine that gets weakened during an
 * outage and never restored, which is the same argument `ad-callback.mjs` makes.
 *
 *   npm --prefix firebase/functions test
 */

import { grantEntries, readProduct, MAX_GRANT } from "../lib/products.js";
import { transactionIdsIn } from "../lib/refunds.js";

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

const TABLE = {
  gg_gems_1: { credits: 0, gems: 100, kind: "consumable" },
  gg_coins_1: { credits: 2500, gems: 0, kind: "consumable" },
  gg_bundle_starter: { credits: 7500, gems: 500, kind: "nonconsumable" },
};

console.log("== the product catalog");

check("a listed product is read as published",
      readProduct(TABLE, "gg_gems_1").gems === 100);

check("a bundle carries both currencies",
      readProduct(TABLE, "gg_bundle_starter").credits === 7500 &&
      readProduct(TABLE, "gg_bundle_starter").gems === 500);

// The important refusal. A valid receipt naming a product this server does not sell is a
// real payment the game cannot honour, and it has to be loud enough that somebody notices
// before Google's three-day refund window closes — which is why it throws rather than
// returning a zero grant that would quietly confirm the transaction.
refuses("a product that is not in the table is refused", TABLE, "gg_gems_99");
refuses("a missing catalog is refused rather than treated as empty", undefined, "gg_gems_1");
refuses("a null catalog is refused", null, "gg_gems_1");

refuses("a product granting nothing is refused",
        { nothing: { credits: 0, gems: 0, kind: "consumable" } }, "nothing");

// Refusing rather than clamping. A clamped grant is a player charged one amount and given
// another, and unlike every other number in this project that mistake cannot be taken
// back — the currency was granted against a real receipt.
refuses("a grant above the ceiling is refused rather than clamped",
        { huge: { credits: MAX_GRANT + 1, gems: 0, kind: "consumable" } }, "huge");

check("a negative amount reads as zero rather than as a debit",
      (() => {
        try {
          return readProduct({ odd: { credits: -500, gems: 10, kind: "consumable" } }, "odd").credits === 0;
        } catch { return false; }
      })());

check("a fractional amount is floored rather than granted as a fraction",
      readProduct({ f: { credits: 100.9, gems: 0, kind: "consumable" } }, "f").credits === 100);

console.log("== what a grant becomes");

check("only the non-zero currencies are granted",
      JSON.stringify(grantEntries(readProduct(TABLE, "gg_gems_1"))) === '[["gems",100]]');

check("a bundle grants both, credits first",
      JSON.stringify(grantEntries(readProduct(TABLE, "gg_bundle_starter"))) ===
      '[["credits",7500],["gems",500]]');

console.log("== the Apple notification scan");

// The property the whole handler rests on: it reads ids out of an unverified body, and
// every one of them is then checked with Apple over an authenticated channel. So the scan
// only has to be *complete enough* and bounded — it never has to be trusted.
const payload = Buffer.from(
  JSON.stringify({ transactionId: "2000000123456789", productId: "gg_gems_1" })
).toString("base64url");

check("a transaction id is found inside a base64url segment",
      transactionIdsIn(`{"signedPayload":"header.${payload}.signature"}`)
        .includes("2000000123456789"));

check("an originalTransactionId is found too",
      transactionIdsIn(
        "x." + Buffer.from(JSON.stringify({ originalTransactionId: "9000000000000001" }))
          .toString("base64url") + ".y"
      ).includes("9000000000000001"));

check("a body with nothing in it yields nothing",
      transactionIdsIn("{}").length === 0);

check("an empty body yields nothing", transactionIdsIn("").length === 0);

check("garbage yields nothing rather than throwing",
      transactionIdsIn("!!!! not base64 !!!!").length === 0);

// Bounded, because the endpoint is unauthenticated and each id costs a Firestore read.
check("the scan is bounded", (() => {
  const many = Array.from({ length: 200 }, (_, i) =>
    Buffer.from(JSON.stringify({ transactionId: String(1000000000000000 + i) }))
      .toString("base64url")).join(".");

  return transactionIdsIn(many, 8).length <= 8;
})());

check("a non-numeric transaction id is not picked up",
      transactionIdsIn(
        "x." + Buffer.from('{"transactionId":"../../etc/passwd"}').toString("base64url") + ".y"
      ).length === 0);

console.log("\n" + `${pass} passed, ${fail} failed`);
process.exit(fail === 0 ? 0 : 1);
