#!/usr/bin/env node
/**
 * Differential checks of deployed Shuffle rewards and the v44 save contract.
 * Only the fresh account created here is written. Cleanup runs even after a failed check.
 * Usage: node firebase/e2e/shuffle-rewards.mjs [web-api-key]
 */
import assert from "node:assert/strict";
import { execSync } from "node:child_process";
import { randomUUID } from "node:crypto";
import { earnedCredits } from "../functions/lib/progression.js";

const PROJECT = process.env.GLIMMER_PROJECT ?? "glimmer-groove-1cd60";
const REGION = process.env.GLIMMER_REGION ?? "europe-west1";
const FN = `https://${REGION}-${PROJECT}.cloudfunctions.net`;
const FS = `https://firestore.googleapis.com/v1/projects/${PROJECT}/databases/(default)/documents`;
const command = process.platform === "win32" ? "firebase.cmd" : "firebase";
const cloud = process.platform === "win32" ? "gcloud.cmd" : "gcloud";
const cli = (text) => execSync(text, { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"], timeout: 90_000 });
function apiKey() {
  if (process.argv[2]) return process.argv[2];
  if (process.env.GLIMMER_WEB_KEY) return process.env.GLIMMER_WEB_KEY;
  // Project and app id are fixed-format identifiers, never arbitrary shell input.
  assert.match(PROJECT, /^[a-z0-9-]+$/);
  const listed = cli(`${command} apps:list ANDROID --project ${PROJECT}`);
  const app = listed.match(/1:\d+:android:[0-9a-f]+/);
  assert.ok(app, "The project must have an Android app");
  const config = cli(`${command} apps:sdkconfig ANDROID ${app[0]} --project ${PROJECT}`);
  const key = config.match(/"current_key"\s*:\s*"([^"]+)"/);
  assert.ok(key, "The Android SDK config must carry its public API key");
  return key[1];
}
const key = apiKey();
const owner = cli(`${cloud} auth print-access-token`).trim();
const ownerHeaders = { Authorization: `Bearer ${owner}` };
let auth, headers;
let passed = 0;
async function request(url, options = {}) {
  const r = await fetch(url, { ...options, signal: AbortSignal.timeout(60_000) });
  return { status: r.status, body: await r.json().catch(() => ({})) };
}
const call = (name, data) => request(`${FN}/${name}`, {
  method: "POST", headers, body: JSON.stringify({ data }),
});
function check(yes, message) {
  assert.ok(yes, message);
  passed++;
  console.log(`  ok   ${message}`);
}
function wire(value) {
  if (typeof value === "number") return { integerValue: String(value) };
  if (typeof value === "string") return { stringValue: value };
  if (typeof value === "boolean") return { booleanValue: value };
  if (Array.isArray(value)) return { arrayValue: { values: value.map(wire) } };
  return { mapValue: { fields: Object.fromEntries(Object.entries(value).map(([k, v]) => [k, wire(v)])) } };
}
function decode(value) {
  if (value.integerValue !== undefined) return Number(value.integerValue);
  if (value.doubleValue !== undefined) return value.doubleValue;
  if (value.stringValue !== undefined) return value.stringValue;
  if (value.booleanValue !== undefined) return value.booleanValue;
  if (value.arrayValue) return (value.arrayValue.values ?? []).map(decode);
  if (value.mapValue) return Object.fromEntries(Object.entries(value.mapValue.fields ?? {}).map(([k, v]) => [k, decode(v)]));
  return null;
}
function wallet(reply, currency = "credits") {
  assert.equal(reply.status, 200, "Wallet callable must answer successfully");
  const row = reply.body.result?.wallets?.find((w) => w.currency === currency);
  assert.ok(row, `Wallet reply must include ${currency}`);
  return row;
}

try {
  const signed = await request(`https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=${key}`, {
    method: "POST", headers: { "Content-Type": "application/json", Referer: "https://www.tekoworld.com/" },
    body: JSON.stringify({ returnSecureToken: true }),
  });
  assert.equal(signed.status, 200, "Anonymous test sign-in must succeed");
  auth = signed.body;
  assert.ok(auth.idToken && auth.localId, "Sign-in must return an account and token");
  headers = { Authorization: `Bearer ${auth.idToken}`, "Content-Type": "application/json" };
  console.log(`Shuffle production probe: own temporary account ${auth.localId}`);

  const table = await request(`${FS}/config/progression`, { headers });
  assert.equal(table.status, 200);
  const config = decode({ mapValue: { fields: table.body.fields } });
  const level = "s13_shuffle";
  check(Object.hasOwn(config.levelChapters, level), "the live reward catalog knows Shuffle");
  const now = Math.floor(Date.now() / 1000);
  const levels = { [level]: { stars: 2, bestMoves: 12, clears: 1,
    firstClearedUnix: now, lastPlayedUnix: now } };
  const baseCredits = earnedCredits(levels, config, auth.localId).credits;
  const rule = config.chapterRewards[config.levelChapters[level]] ?? config.rewards;
  const starXp = rule.xpFirstClear + 2 * rule.xpPerStar;
  const writeSave = async (rows, schemaVersion = 44, boost = 0) => {
    const save = { schemaVersion, updatedUnix: now, levels, legacyImportDone: true,
      settings: { language: "en" }, wallet: { xpBoostEarned: boost },
      shuffleBest: rows, checksum: "shuffle-production-probe" };
    const r = await request(`${FS}/players/${auth.localId}`, {
      method: "PATCH", headers, body: JSON.stringify({ fields: wire(save).mapValue.fields }),
    });
    assert.equal(r.status, 200, `The deployed rules must accept a v${schemaVersion} save`);
  };
  const row = (rewards, wave = 80) => ({ level, wave, ...(rewards ? { rewards } : {}) });
  const count = (device, waves) => ({ device, waves });

  await writeSave([row(null)], 43);
  const first = wallet(await call("getWallet", {}));
  check(first.earnedFloor === baseCredits, "an older save keeps star coins and receives no retroactive wave coins");
  const grants = first.grantedBaseline;

  await writeSave([row([count("a", 7)])]);
  const seven = wallet(await call("getWallet", {}));
  check(seven.earnedFloor === baseCredits + 70, "seven cleared waves add exactly 70 coins");
  check(seven.grantedBaseline === grants && seven.endlessPaid === 0, "Shuffle leaves grants and Infinite's daily allowance unchanged");
  check(wallet(await call("getWallet", {})).earnedFloor === baseCredits + 70, "retrying a wallet read pays nothing twice");

  await writeSave([row([count("a", 12)])]);
  const spendId = randomUUID().replaceAll("-", "");
  const spent = await call("submitSpends", { spends: [{ id: spendId, currency: "credits", amount: 1,
    unix: now, reason: "shuffle_e2e" }] });
  const afterSpend = wallet(spent);
  check(afterSpend.earnedFloor === baseCredits + 120, "submitSpends reads the replay's new earned total");
  check(afterSpend.spentBaseline === 1 && afterSpend.confirmedSpendIds.includes(spendId), "the new coins are spendable");
  check(wallet(await call("submitSpends", { spends: [{ id: spendId, currency: "credits", amount: 1,
    unix: now, reason: "shuffle_e2e" }] })).spentBaseline === 1, "retrying the debit charges once");

  await writeSave([row([count("a", 12), count("b", 3)])]);
  const claimed = wallet(await call("claimAwards", { awards: [] }));
  check(claimed.earnedFloor === baseCredits + 150, "claimAwards includes both devices' cleared waves");
  check(claimed.grantedBaseline === grants && claimed.spentBaseline === 1, "reward derivation preserves grants and debits");
  await writeSave([row([count("a", 12), count("a", 7), count("b", 3)])]);
  check(wallet(await call("getWallet", {})).earnedFloor === baseCredits + 150, "duplicate device counters take max and never double-pay");

  // A reward-only row exercises the forfeit contract without putting a fake best on the
  // Shuffle board. The fresh profile is removed in the finally block below.
  await writeSave([row([count("a", 400), count("b", 5)], 0)], 44, 200);
  check(wallet(await call("getWallet", {})).earnedFloor === baseCredits + 4050, "reward-only rows pay without a best-wave record");
  const published = await call("publishGrove", {});
  assert.equal(published.status, 200, "publishGrove must answer successfully");
  const cardDoc = await request(`${FS}/groves/${auth.localId}`, { headers });
  assert.equal(cardDoc.status, 200, "The profile must be readable");
  const card = decode({ mapValue: { fields: cardDoc.body.fields } });
  const baseXp = starXp + 4050;
  const boost = Math.min(200, Math.floor(baseXp * (config.xpBoost?.maxPercent ?? 150) / 100), 1_000_000_000);
  const curve = config.keeper;
  assert.ok(curve, "The live config must carry the keeper curve");
  let expectedLevel = 1, spentXp = 0;
  while (expectedLevel < curve.maxLevel) {
    const cost = expectedLevel - 1 < curve.xpToNext.length ? curve.xpToNext[expectedLevel - 1]
      : curve.tailXpToNext + curve.tailXpIncrement * (expectedLevel - 1 - curve.xpToNext.length);
    if (spentXp + cost > baseXp + boost) break;
    spentXp += cost;
    expectedLevel++;
  }
  check(card.level === expectedLevel, "the live public profile includes exactly 10 XP per cleared wave and the existing boost");
  check(!card.shuffle, "reward counters never create a best-wave leaderboard score");

  for (const name of ["adminListPlayers", "adminFindPlayers", "adminGetPlayer"]) {
    const r = await call(name, { uid: auth.localId, query: auth.localId });
    check(r.body.error?.status === "PERMISSION_DENIED", `${name} remains restricted to admins`);
  }
  console.log(`Shuffle production probe: ${passed} checks passed`);
} finally {
  if (auth?.localId && headers) {
    const removed = await call("deleteAccount", {});
    assert.equal(removed.status, 200, "The temporary account must delete itself");
    assert.equal(removed.body.result?.deleted, true, "Account deletion must be confirmed");
    for (const path of [`players/${auth.localId}`, `players/${auth.localId}/private/wallet`, `groves/${auth.localId}`]) {
      const r = await request(`${FS}/${path}`, { headers: ownerHeaders });
      assert.equal(r.status, 404, `Cleanup must remove ${path}`);
    }
    console.log("Cleanup verified: temporary save, wallet, profile and account removed");
  }
}
