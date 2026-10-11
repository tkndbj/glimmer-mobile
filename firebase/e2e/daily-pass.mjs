/**
 * The seven-day passes (invariant 61), against the live deployment.
 *
 *   node firebase/e2e/daily-pass.mjs [webApiKey]
 *
 * Differential on purpose, like the rest of this folder: a stale function answers 200 with a valid
 * but wrong document, so each check names what only the new code says. It signs in as a fresh
 * anonymous account, which owns no pass - so it proves the wire (the reply field, the callable, its
 * refusals) and that nothing is paid to an account that bought nothing. What a purchase opens and
 * what a claim pays is held offline by `functions/test/daily-pass.mjs`; a real purchase is the
 * owner's device test.
 */

import { execSync } from "node:child_process";

const PROJECT = process.env.GLIMMER_PROJECT ?? "glimmer-groove-1cd60";
const REGION = process.env.GLIMMER_REGION ?? "europe-west1";
const FN = `https://${REGION}-${PROJECT}.cloudfunctions.net`;
const FS = `https://firestore.googleapis.com/v1/projects/${PROJECT}/databases/(default)/documents`;

function apiKey() {
  if (process.argv[2]) return process.argv[2];
  const listed = execSync(`firebase apps:list ANDROID --project ${PROJECT}`,
                          { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] });
  const appId = listed.match(/(1:\d+:android:[0-9a-f]+)/);
  if (!appId) throw new Error("no Android app found; pass the web API key as the first argument");
  const config = execSync(`firebase apps:sdkconfig ANDROID ${appId[1]} --project ${PROJECT}`,
                          { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] });
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
  method: "POST", headers: { "Content-Type": "application/json" },
  body: JSON.stringify({ returnSecureToken: true }),
});
const auth = await signUp.json();
if (!auth.idToken) { console.error("anonymous sign-in failed", auth); process.exit(1); }

const bearer = { Authorization: `Bearer ${auth.idToken}` };
const json = { ...bearer, "Content-Type": "application/json" };
console.log(`signed in anonymously as ${auth.localId}\n`);

async function call(name, data, headers = json) {
  const res = await fetch(`${FN}/${name}`, { method: "POST", headers, body: JSON.stringify({ data }) });
  let body = null;
  try { body = await res.json(); } catch { /* not JSON */ }
  return { status: res.status, body };
}

console.log("== the catalog");
const products = await (await fetch(`${FS}/config/products`, { headers: bearer })).json();
const field = (id, key) => Number(products?.fields?.[id]?.mapValue?.fields?.[key]?.integerValue ?? 0);
check(field("gg_gem_pass_7d", "passDays") === 7 && field("gg_gem_pass_7d", "gems") === 500,
      "the gem pass is published: 500 gems a day for 7 days");
check(field("gg_coin_pass_7d", "passDays") === 7 && field("gg_coin_pass_7d", "credits") === 20000,
      "the coin pass is published: 20,000 coins a day for 7 days");

console.log("\n== the wallet reply");
const wallet = await call("getWallet", {});
const rows = wallet.body?.result?.wallets ?? [];
check(wallet.status === 200 && rows.length > 0, "getWallet answers", `status ${wallet.status}`);
check(rows.length > 0 && rows.every((r) => Array.isArray(r.dailyPasses)),
      "every row carries dailyPasses (only the new getWallet sends it)");
check(rows.every((r) => (r.dailyPasses ?? []).length === 0), "a fresh account owes no pass");

console.log("\n== claiming");
const claim = await call("claimDailyPass", { product: "gg_gem_pass_7d" });
check(claim.status === 200, "claimDailyPass is callable (deployed, and the invoker is granted)",
      `status ${claim.status} ${JSON.stringify(claim.body)}`);
check(claim.body?.result?.outcome === "none", "an account that bought nothing is paid nothing",
      JSON.stringify(claim.body?.result));
check(Object.keys(claim.body?.result?.granted ?? { x: 1 }).length === 0, "and grants nothing");
check((claim.body?.result?.wallets ?? []).every((r) => Array.isArray(r.dailyPasses)),
      "and answers with the wallet, passes included");

const gemsBefore = rows.find((r) => r.currency === "gems")?.grantedBaseline;
const gemsAfter = (claim.body?.result?.wallets ?? []).find((r) => r.currency === "gems")?.grantedBaseline;
check(gemsBefore === gemsAfter, "the gem balance did not move", `${gemsBefore} -> ${gemsAfter}`);

const bad = await call("claimDailyPass", { product: "Not An Id!" });
check(bad.status === 400 && bad.body?.error?.status === "INVALID_ARGUMENT",
      "a malformed product id is refused", `status ${bad.status}`);

const anon = await call("claimDailyPass", { product: "gg_gem_pass_7d" }, { "Content-Type": "application/json" });
check(anon.status === 401 || anon.body?.error?.status === "UNAUTHENTICATED",
      "a signed-out caller is refused", `status ${anon.status}`);

console.log("\n== forging");
const forged = await call("claimAwards", {
  awards: [{ id: `pass:gg_gem_pass_7d:${Math.floor(Date.now() / 86400000)}`, claimedAmount: 500, unix: 0, reason: "" }],
});
const refused = forged.body?.result?.rejected ?? [];
check(forged.status === 200 && refused.length === 1, "a pass id submitted as an award is refused",
      JSON.stringify(forged.body?.result?.rejected));

const receipt = await call("redeemPurchase", {
  receipt: { store: "google", transactionId: "GPA.0000-0000-0000-00000", productId: "gg_gem_pass_7d", payload: "forged" },
});
check(receipt.status !== 200, "a forged pass receipt is not honoured", `status ${receipt.status}`);

const write = await fetch(`${FS}/players/${auth.localId}/private/wallet?updateMask.fieldPaths=dailyPasses`, {
  method: "PATCH", headers: json,
  body: JSON.stringify({ fields: { dailyPasses: { mapValue: { fields: {} } } } }),
});
check(write.status === 403, "a client cannot write its own passes", `status ${write.status}`);

console.log(`\n${pass} passed, ${fail} failed`);
if (fail > 0) process.exit(1);
