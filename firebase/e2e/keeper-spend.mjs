// Keeper levels bought outright (invariant 57), against the live functions.
//
//     node firebase/e2e/keeper-spend.mjs [web-api-key]
//
// **Differential for `endless-xp.mjs`'s reason.** A `submitSpends` that has never heard of a
// `keeper:` id treats it as any other debit: it charges whatever amount is named and confirms
// it, which from a client is indistinguishable from a level bought. A `getWallet` that has never
// heard of the count answers a valid reply with no `keeperBought` on it, which the client reads
// as "not carried" and moves nothing - a working feature and a missing one look the same on a
// device. So this asks the live server the questions only the keeper code answers:
//
//   - every wallet reply carries `keeperBought`, and a fresh account's is nought;
//   - a keeper debit priced under the published ladder is REFUSED rather than charged;
//   - a keeper debit that skips an ordinal is refused;
//   - a keeper debit in the wrong currency is refused;
//   - a malformed keeper id is refused;
//   - and, when the seed leaves the account able to afford the first level, the honest debit is
//     confirmed and the count comes back as one on the very same reply.
//
// The prices are read off the *published* config, never typed, so a retune cannot turn this
// red with a correct answer. A new anonymous account every run, as the whole live suite does.

import { execSync } from "node:child_process";

const PROJECT = process.env.GLIMMER_PROJECT ?? "glimmer-groove-1cd60";
const REGION = process.env.GLIMMER_REGION ?? "europe-west1";
const FN = `https://${REGION}-${PROJECT}.cloudfunctions.net`;
const FS = `https://firestore.googleapis.com/v1/projects/${PROJECT}/databases/(default)/documents`;

function apiKey() {
  if (process.argv[2]) return process.argv[2];
  const listed = execSync(`firebase apps:list ANDROID --project ${PROJECT}`, { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] });
  const appId = listed.match(/(1:\d+:android:[0-9a-f]+)/);
  if (!appId) throw new Error(`no Android app found on ${PROJECT}; pass the web API key as the first argument`);
  const config = execSync(`firebase apps:sdkconfig ANDROID ${appId[1]} --project ${PROJECT}`, { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] });
  const match = config.match(/"current_key"\s*:\s*"([^"]+)"/);
  if (!match) throw new Error("could not read the app's API key; pass it as the first argument");
  return match[1];
}

let pass = 0, fail = 0;
const check = (ok, what, detail = "") => {
  if (ok) { pass++; console.log(`  ok   ${what}`); }
  else { fail++; console.log(`  FAIL ${what} ${detail}`); }
};

const KEY = apiKey();
const signUp = await fetch(`https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=${KEY}`, {
  method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ returnSecureToken: true }),
});
const auth = await signUp.json();
if (!auth.idToken) { console.error("anonymous sign-in failed", JSON.stringify(auth)); process.exit(1); }
const uid = auth.localId;
const headers = { Authorization: `Bearer ${auth.idToken}`, "Content-Type": "application/json" };
console.log(`signed in anonymously as ${uid}\n`);

const call = async (name, data) => {
  const r = await fetch(`${FN}/${name}`, { method: "POST", headers, body: JSON.stringify({ data }) });
  return { status: r.status, body: await r.json().catch(() => ({})) };
};
const walletsOf = (reply) => reply?.result?.wallets ?? [];
const rowOf = (reply, currency) => walletsOf(reply).find((w) => w.currency === currency);
const rejectedOf = (reply) => reply?.result?.rejected ?? [];

// The published ladder, read as a client may (config/* is client-readable).
const published = await (await fetch(`${FS}/config/progression`, { headers })).json();
const ladderField = published?.fields?.keeperLevels?.mapValue?.fields;
check(!!ladderField, "config/progression carries a keeperLevels block (re-seed if not)");
const anchors = (ladderField?.anchors?.arrayValue?.values ?? []).map((v) => {
  const f = v.mapValue.fields;
  return { level: Number(f.level.integerValue), currency: f.currency.stringValue, price: Number(f.price.integerValue) };
});
const first = anchors[0];
check(!!first && first.level === 2, "the first anchor prices level 2", JSON.stringify(first));

// 1. The reply carries the count.
const wallet = await call("getWallet", {});
check(wallet.status === 200, "getWallet responds", JSON.stringify(wallet.body).slice(0, 160));
check(walletsOf(wallet.body).length > 0 && walletsOf(wallet.body).every((w) => typeof w.keeperBought === "number"),
      "every wallet row carries keeperBought", JSON.stringify(walletsOf(wallet.body).map((w) => w.keeperBought)));
check(walletsOf(wallet.body).every((w) => w.keeperBought === 0), "and a fresh account's count is nought");

const seed = rowOf(wallet.body, "credits")?.grantedBaseline ?? 0;

// 2. The refusals. Every one of these would have been *charged* by a server that had never
// heard of the id, so each is a differential in its own right.
if (first) {
  const under = { id: "keeper:1:2", currency: first.currency, amount: Math.max(1, first.price - 1), unix: 1700000001, reason: "keeper_level" };
  const r1 = await call("submitSpends", { spends: [under] });
  check(rejectedOf(r1.body).includes(under.id), "a keeper debit one under the published price is refused", JSON.stringify(r1.body).slice(0, 200));
  check((rowOf(r1.body, first.currency)?.spentBaseline ?? 0) === 0, "and nothing was charged for it");

  const skip = { id: "keeper:2:3", currency: first.currency, amount: first.price * 10, unix: 1700000002, reason: "keeper_level" };
  const r2 = await call("submitSpends", { spends: [skip] });
  check(rejectedOf(r2.body).includes(skip.id), "a keeper debit that skips the first ordinal is refused");

  const other = first.currency === "gems" ? "credits" : "gems";
  const wrong = { id: "keeper:1:2", currency: other, amount: 1, unix: 1700000003, reason: "keeper_level" };
  const r3 = await call("submitSpends", { spends: [wrong] });
  check(rejectedOf(r3.body).includes(wrong.id), "a keeper debit in the wrong currency is refused");

  const bad = { id: "keeper:01:2", currency: first.currency, amount: first.price, unix: 1700000004, reason: "keeper_level" };
  const r4 = await call("submitSpends", { spends: [bad] });
  check(rejectedOf(r4.body).includes(bad.id), "a malformed keeper id is refused rather than charged");

  // 3. The honest purchase, when the account seed can afford it.
  if (first.currency === "credits" && seed >= first.price) {
    const honest = { id: "keeper:1:2", currency: "credits", amount: first.price, unix: 1700000005, reason: "keeper_level" };
    const r5 = await call("submitSpends", { spends: [honest] });
    const row = rowOf(r5.body, "credits");
    check((row?.confirmedSpendIds ?? []).includes(honest.id), "the first level, paid in full, is confirmed", JSON.stringify(r5.body).slice(0, 200));
    check(row?.spentBaseline === first.price, "and charged exactly the published price", `spent=${row?.spentBaseline}`);
    check(walletsOf(r5.body).every((w) => w.keeperBought === 1), "and the count comes back as one on the same reply");

    const again = await call("submitSpends", { spends: [honest] });
    check(rowOf(again.body, "credits")?.spentBaseline === first.price, "resubmitting the same purchase charges nothing more");
    check(walletsOf(again.body).every((w) => w.keeperBought === 1), "and the count stays one");

    const dup = { id: "keeper:1:5", currency: "credits", amount: first.price * 5, unix: 1700000006, reason: "keeper_level" };
    const r6 = await call("submitSpends", { spends: [dup] });
    check(rejectedOf(r6.body).includes(dup.id), "the first ordinal under a different level is refused as already bought");

    const later = await call("getWallet", {});
    check(walletsOf(later.body).every((w) => w.keeperBought === 1), "a later wallet read still carries the count");
  } else {
    console.log(`  note the account seed (${seed} credits) cannot afford level 2 at ${first.price} ${first.currency}; the honest purchase is not exercised`);
  }
}

console.log(`\n${pass} passed, ${fail} failed`);
process.exit(fail === 0 ? 0 : 1);
