#!/usr/bin/env node
/**
 * The offer engine's server half, live (invariant 64): every wallet reply carries the payer fields
 * the client waits for before dealing any personal offer, as noughts for an account that never paid,
 * and the published catalog prices the seven offer products with their reference cents.
 *
 *     node firebase/e2e/payer-reply.mjs
 *
 * Differential on purpose, as the rest of this folder is: a stale `getWallet` answers 200 with a valid
 * wallet that simply lacks the fields, which is the one failure that would leave offers silently off.
 *
 * **It leaves nothing behind**: it signs in as a fresh anonymous account, reads, and deletes that
 * account through `deleteAccount` (invariant 27) before it exits.
 */

import { execSync } from "node:child_process";

const PROJECT = process.env.GLIMMER_PROJECT ?? "glimmer-groove-1cd60";
const REGION = process.env.GLIMMER_REGION ?? "europe-west1";
const FN = `https://${REGION}-${PROJECT}.cloudfunctions.net`;
const FS = `https://firestore.googleapis.com/v1/projects/${PROJECT}/databases/(default)/documents`;
const OFFERS = ["gg_offer_1", "gg_offer_2", "gg_offer_3", "gg_offer_4", "gg_offer_5", "gg_offer_6", "gg_offer_7"];

function apiKey() {
  if (process.argv[2]) return process.argv[2];
  const listed = execSync(`firebase apps:list ANDROID --project ${PROJECT}`, {
    encoding: "utf8", stdio: ["ignore", "pipe", "pipe"],
  });
  const appId = listed.match(/(1:\d+:android:[0-9a-f]+)/);
  if (!appId) throw new Error("could not find an Android app; pass the API key as the first argument");
  const config = execSync(`firebase apps:sdkconfig ANDROID ${appId[1]} --project ${PROJECT}`, {
    encoding: "utf8", stdio: ["ignore", "pipe", "pipe"],
  });
  const match = config.match(/"current_key"\s*:\s*"([^"]+)"/);
  if (!match) throw new Error("could not read the app's API key");
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
const account = await signUp.json();
if (!account.idToken) throw new Error("anonymous sign-in failed: " + JSON.stringify(account));

const call = async (name, data) => {
  const r = await fetch(`${FN}/${name}`, {
    method: "POST",
    headers: { Authorization: `Bearer ${account.idToken}`, "Content-Type": "application/json" },
    body: JSON.stringify({ data }),
  });
  return { status: r.status, body: await r.json().catch(() => ({})) };
};

try {
  console.log("payer reply");
  const wallet = await call("getWallet", {});
  const rows = wallet.body?.result?.wallets ?? [];
  check(wallet.status === 200 && rows.length > 0, "getWallet answers", JSON.stringify(wallet.body).slice(0, 200));
  check(rows.every((r) => "payerPurchases" in r && "payerCents" in r && "payerTopCents" in r && "payerLastUnix" in r),
        "every row carries the four payer fields (a stale bundle omits them)");
  check(rows.every((r) => r.payerPurchases === 0 && r.payerCents === 0 && r.payerTopCents === 0 && r.payerLastUnix === 0),
        "an account that never paid answers noughts");

  console.log("published catalog");
  const products = await fetch(`${FS}/config/products`, { headers: { Authorization: `Bearer ${account.idToken}` } });
  const fields = (await products.json()).fields ?? {};
  for (const id of OFFERS) {
    const f = fields[id]?.mapValue?.fields;
    check(!!f && Number(f.cents?.integerValue) > 0 && Number(f.gems?.integerValue) > 0 && Number(f.credits?.integerValue) > 0,
          `${id} is sold, with its reference cents`);
  }
} finally {
  const gone = await call("deleteAccount", {});
  check(gone.status === 200, "the test account deleted itself", JSON.stringify(gone.body).slice(0, 200));
}

console.log(`\n${pass} passed, ${fail} failed`);
if (fail > 0) process.exitCode = 1;
