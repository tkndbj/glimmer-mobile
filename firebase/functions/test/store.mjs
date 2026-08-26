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

import { grantEntries, readProduct, MAX_GRANT, MAX_CAPACITY } from "../lib/products.js";
import { revocationUpdate, transactionIdsIn } from "../lib/refunds.js";

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
  gg_heart_vessel_2: { credits: 0, gems: 0, kind: "nonconsumable", capacity: 20 },
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

console.log("== heart containers");

// The one non-currency thing a product may grant. It is permitted because a capacity is an
// *idempotent permanent entitlement* rather than an amount: applying it twice is applying
// it once, so the client needs no record of "have I already applied this transaction" — and
// that record is the whole of what invariant 18 protects against.
check("a container is read as published even though it grants no currency",
      readProduct(TABLE, "gg_heart_vessel_2").capacity === 20);

check("a container grants no currency entries at all",
      grantEntries(readProduct(TABLE, "gg_heart_vessel_2")).length === 0);

check("a currency product carries no capacity",
      readProduct(TABLE, "gg_gems_1").capacity === 0);

// The "never both" half, and it is load-bearing rather than tidy: a container that also
// paid gems would put a stored amount straight back onto the client's side of a purchase.
refuses("a product granting a capacity and currency is refused",
        { mixed: { credits: 0, gems: 100, kind: "nonconsumable", capacity: 20 } }, "mixed");

// Refusing rather than clamping, for the grant ceiling's reason: the client's ledger holds
// the cap to what it will honour, so a clamped capacity is a card promising more than it
// gives against a payment that already went through.
refuses("a capacity above the ceiling is refused rather than clamped",
        { huge: { credits: 0, gems: 0, kind: "nonconsumable", capacity: MAX_CAPACITY + 1 } },
        "huge");

refuses("a product with neither currency nor a capacity is still refused",
        { nothing: { credits: 0, gems: 0, kind: "nonconsumable", capacity: 0 } }, "nothing");

console.log("== what a grant becomes");

check("only the non-zero currencies are granted",
      JSON.stringify(grantEntries(readProduct(TABLE, "gg_gems_1"))) === '[["gems",100]]');

check("a bundle grants both, credits first",
      JSON.stringify(grantEntries(readProduct(TABLE, "gg_bundle_starter"))) ===
      '[["credits",7500],["gems",500]]');

console.log("== reversing a refund");

// The one path in this backend that takes money *back*, and until `revocationUpdate` was
// split out of the Firestore transaction the only way to exercise it was to make a real
// purchase and refund it — which is to say it was never exercised at all.
const union = (id) => ({ arrayUnion: id });

check("a refunded currency grant is taken off the baseline",
      revocationUpdate({ granted: { credits: 2500 } },
                       { credits: { granted: 4000 } }, union)["credits.granted"] === 1500);

// Clamped rather than negative: a player who already spent it ends at zero and keeps what
// they bought, because a balance that silently eats a month of earnings is how somebody
// stops playing rather than how they are held to account.
check("a balance already spent down clamps at zero rather than going negative",
      revocationUpdate({ granted: { credits: 2500 } },
                       { credits: { granted: 900 } }, union)["credits.granted"] === 0);

check("a wallet that never recorded the currency clamps at zero too",
      revocationUpdate({ granted: { gems: 100 } }, {}, union)["gems.granted"] === 0);

check("nothing is written for a currency the receipt never granted",
      !("gems.granted" in revocationUpdate({ granted: { credits: 100 } },
                                           { credits: { granted: 100 } }, union)));

// A container is not an amount, so it is not subtracted from anything — the entitlement is
// held by the client, and the only thing this server can do is say it was taken back, on
// every wallet reply, for ever. See invariant 18d.
check("a refunded heart container is revoked by id, not by amount",
      JSON.stringify(revocationUpdate(
        { productId: "gg_heart_vessel_2", capacity: 20, granted: {} }, {}, union
      )) === JSON.stringify({ containersRevoked: { arrayUnion: "gg_heart_vessel_2" } }));

check("a currency refund revokes no container",
      !("containersRevoked" in revocationUpdate({ granted: { credits: 100 } },
                                                { credits: { granted: 100 } }, union)));

// The two guards that stop a malformed receipt writing a container revocation nobody can
// undo: a capacity with no id, and an id with no capacity.
check("a receipt with a capacity and no product id revokes nothing",
      !("containersRevoked" in revocationUpdate({ capacity: 20, granted: {} }, {}, union)));

check("a receipt with a product id and no capacity revokes nothing",
      !("containersRevoked" in revocationUpdate(
        { productId: "gg_gems_1", granted: {} }, {}, union)));

// Older receipts, written before containers existed, carry neither field. They must read as
// an ordinary currency reversal rather than as anything at all to do with a container.
check("a receipt written before containers existed reverses only its currency",
      JSON.stringify(revocationUpdate({ granted: { gems: 100 } },
                                      { gems: { granted: 340 } }, union)) ===
      JSON.stringify({ "gems.granted": 240 }));

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
