/**
 * The rewarded-ad callback is the only thing standing between a signed HTTP request and
 * the granted baseline, so it is exercised offline rather than against a live network.
 *
 * A verification routine nobody can run without an ad account is a verification routine
 * that gets weakened during an outage and never restored. Everything below builds its own
 * signatures from the documented formula — md5(TIMESTAMP + EVENT_ID + USER_ID + REWARDS +
 * PRIVATE_KEY) — so a change to that formula fails here rather than in production, where
 * the symptom would be every player quietly not being paid.
 */

import { createHash } from "node:crypto";
import {
  adCurrencyOf, adCurrencyValue, adGrantId, ackBody,
  isAdGrantId, usableAdConfig, usableEventId, verifyAdCallback,
} from "../lib/ads.js";

const KEY = "a-private-key-known-only-to-us";

let pass = 0;
let fail = 0;

function check(name, condition) {
  if (condition) { console.log("  ok   " + name); pass++; }
  else { console.log("  FAIL " + name); fail++; }
}

function sign(query, key = KEY) {
  return createHash("md5")
    .update(`${query.timestamp}${query.eventId}${query.userId}${query.rewards}${key}`)
    .digest("hex");
}

function callback(overrides = {}) {
  const query = {
    eventId: "evt-000123",
    userId: "uid-abcdef",
    rewards: "1",
    timestamp: "1786700000",
    placement: "coin_bonus",
    ...overrides,
  };
  return { ...query, signature: overrides.signature ?? sign(query) };
}

console.log("== signature");

check("a correctly signed callback is accepted",
      verifyAdCallback(callback(), KEY).ok);

check("a wrong signature is refused and never retried",
      (() => {
        const v = verifyAdCallback(callback({ signature: "0".repeat(32) }), KEY);
        return !v.ok && v.retryable === false;
      })());

check("a signature signed with a different key is refused",
      (() => {
        const query = callback();
        query.signature = sign(query, "somebody-elses-key");
        return !verifyAdCallback(query, KEY).ok;
      })());

check("tampering with the user id invalidates the signature",
      !verifyAdCallback({ ...callback(), userId: "uid-someone-else" }, KEY).ok);

check("tampering with the reward count invalidates the signature",
      !verifyAdCallback({ ...callback(), rewards: "9999" }, KEY).ok);

check("an uppercase signature still matches",
      verifyAdCallback((() => {
        const q = callback();
        return { ...q, signature: q.signature.toUpperCase() };
      })(), KEY).ok);

console.log("== failing closed");

check("no configured key refuses everything, and asks to be retried",
      (() => {
        const v = verifyAdCallback(callback(), undefined);
        return !v.ok && v.retryable === true;
      })());

check("an empty key is not treated as a key that matches an empty signature",
      !verifyAdCallback(callback({ signature: "" }), "").ok);

console.log("== parameters");

for (const missing of ["eventId", "userId", "rewards", "timestamp"]) {
  check(`a callback missing ${missing} is refused`,
        !verifyAdCallback({ ...callback(), [missing]: undefined }, KEY).ok);
}

check("an unknown placement is refused rather than paid at a default",
      !verifyAdCallback(callback({ placement: "not_a_placement" }), KEY).ok);

check("a placement is required at all",
      !verifyAdCallback(callback({ placement: undefined }), KEY).ok);

check("itemName names the offer when placement does not",
      (() => {
        const q = callback({ placement: "Rewarded_Android" });
        return verifyAdCallback({ ...q, itemName: "coin_bonus" }, KEY).ok;
      })());

check("placement wins when both name a known offer",
      (() => {
        const q = callback({ placement: "heart_refill" });
        const v = verifyAdCallback({ ...q, itemName: "coin_bonus" }, KEY);
        return v.ok && v.placement === "heart_refill";
      })());

check("neither field naming a known offer is refused, not defaulted",
      !verifyAdCallback({ ...callback({ placement: "Rewarded_Android" }), itemName: "Coins" }, KEY).ok);

check("an event id carrying a path separator is refused",
      (() => {
        const q = callback({ eventId: "../../etc/passwd" });
        q.signature = sign(q);
        return !verifyAdCallback(q, KEY).ok;
      })());

check("a user id carrying a path separator is refused",
      (() => {
        const q = callback({ userId: "uid/../../other" });
        q.signature = sign(q);
        return !verifyAdCallback(q, KEY).ok;
      })());

check("usableEventId accepts what LevelPlay sends and refuses what it does not",
      usableEventId("dae8e6cf42b1357f8652ad6ecb5b24f1")
      && usableEventId("evt-1.2:3_4")
      && !usableEventId("")
      && !usableEventId("a/b")
      && !usableEventId("x".repeat(129)));

console.log("== grant identity");

check("a grant id is derived from the network's event id",
      adGrantId("evt-000123") === "ad:evt-000123");

check("the same event id always produces the same grant id",
      adGrantId("evt-1") === adGrantId("evt-1"));

check("two different views never share a grant id",
      adGrantId("evt-1") !== adGrantId("evt-2"));

check("an ad grant id is recognised in the award namespace",
      isAdGrantId("ad:evt-1") && !isAdGrantId("daily:20315:0:credits"));

console.log("== payouts");

const config = usableAdConfig({
  placements: {
    heart_refill: { kind: "hearts", amount: 2 },
    coin_bonus: { kind: "credits", amount: 150 },
  },
});

check("a usable config is read", config !== null);

check("the coin placement pays credits",
      adCurrencyOf(config, "coin_bonus") === "credits"
      && adCurrencyValue(config, "coin_bonus", "credits") === 150);

check("the heart placement has no currency to grant",
      adCurrencyOf(config, "heart_refill") === null
      && adCurrencyValue(config, "heart_refill", "credits") === 0);

check("a placement pays nothing in a currency it does not hold",
      adCurrencyValue(config, "coin_bonus", "gems") === 0);

check("an amount above the ceiling is clamped rather than trusted",
      usableAdConfig({ placements: { coin_bonus: { kind: "credits", amount: 999999 } } })
        .placements.coin_bonus.amount === 5000);

check("a config with no usable placements reads as absent",
      usableAdConfig({ placements: { coin_bonus: { kind: "credits", amount: 0 } } }) === null
      && usableAdConfig(null) === null
      && usableAdConfig({}) === null);

check("an unknown placement in config is ignored rather than adopted",
      usableAdConfig({ placements: { made_up: { kind: "credits", amount: 50 } } }) === null);

console.log("== acknowledgement");

check("the acknowledgement is exactly what LevelPlay looks for",
      ackBody("dae8e6cf42b1357f8652ad6ecb5b24f1") === "dae8e6cf42b1357f8652ad6ecb5b24f1:OK");

console.log(`\n${pass} ad callback check(s), ${fail} failure(s)`);
process.exit(fail === 0 ? 0 : 1);
