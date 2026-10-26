/** Real Firestore transactions and rules, strictly against a demo emulator. */
import assert from "node:assert/strict";
import { initializeApp } from "firebase-admin/app";
import { getFirestore } from "firebase-admin/firestore";
import { publishNormalBoard, backfillNormalScores, NORMAL_BACKFILL_PATH } from "../lib/normal-board.js";
import { normalScore } from "../lib/normal-scores.js";
import { reportName } from "../lib/reports.js";
import { fallbackName } from "../lib/grove.js";

const projectId = process.env.GCLOUD_PROJECT ?? "demo-normal-leaderboard";
assert.ok(/^(127\.0\.0\.1|localhost):\d+$/.test(process.env.FIRESTORE_EMULATOR_HOST ?? "")
  && projectId.startsWith("demo-"), "Local emulator/demo project required");
const reset = await fetch(`http://${process.env.FIRESTORE_EMULATOR_HOST}/emulator/v1/projects/${projectId}/databases/(default)/documents`,
  { method: "DELETE" });
assert.equal(reset.status, 200, "Reset only this isolated demo database before the test");
initializeApp({ projectId });
const db = getFirestore();
const config = { version: 1, levels: [{ level: "s01_one", target: 20 }] };
const batch = db.batch();
for (let i = 0; i < 105; i++)
  batch.set(db.doc(`normalCards/top${String(i).padStart(3, "0")}`),
    { name: "Fern", avatar: "", level: 1, normalScore: 2000 + i, builtUnix: 1 });
batch.set(db.doc("groves/untouched"), { name: "Wave player", wave: 44, level: 1 });
batch.set(db.doc("leaderboards/endless"), { entries: [{ uid: "untouched", wave: 44 }] });
batch.set(db.doc("leaderboards/shuffle"), { entries: [{ uid: "untouched", shuffle: 22 }] });
await batch.commit();
assert.equal(await publishNormalBoard(db), 100);
let board = (await db.doc("leaderboards/normal").get()).data();
assert.equal(board.entries[0].uid, "top104");
assert.equal(board.entries[99].uid, "top005");
assert.equal(board.scoringVersion, 1);
await db.doc("normalCards/top104").delete();
await publishNormalBoard(db);
board = (await db.doc("leaderboards/normal").get()).data();
assert.equal(board.entries[0].uid, "top103");
assert.equal(board.entries.some((row) => row.uid === "top104"), false, "Withdrawn rows leave the snapshot");

// Two pages, including opted-out and non-Normal-only players.
const saves = db.batch();
for (let i = 0; i < 205; i++)
  saves.set(db.doc(`players/p${String(i).padStart(3, "0")}`), {
    levels: i === 1 ? { infinite: { stars: 3, bestMoves: 1 } }
      : { s01_one: { stars: 3, bestMoves: 15 } }, settings: { board: i === 0 ? 2 : 1 },
  });
await saves.commit();
const published = new Map();
const publish = async (uid) => {
  const saved = (await db.doc(`players/${uid}`).get()).data();
  const score = normalScore(saved.levels, config);
  published.set(uid, score); // Projection, not an increment.
  await db.doc(`normalCards/${uid}`).set({ name: "Fern", level: 1, normalScore: score });
};
let fail = true;
await assert.rejects(backfillNormalScores(db, config, async (uid) => {
  if (uid === "p010" && fail) { fail = false; throw new Error("interrupted page"); }
  return publish(uid);
}));
let checkpoint = (await db.doc(NORMAL_BACKFILL_PATH).get()).data();
assert.equal(checkpoint.cursor, undefined, "A failed page never advances its checkpoint");
assert.equal(checkpoint.leaseUntil, undefined, "A failed page releases its lease");

let started, release;
const began = new Promise((resolve) => { started = resolve; });
const blocked = new Promise((resolve) => { release = resolve; });
const first = backfillNormalScores(db, config, async (uid) => { started(); await blocked; return publish(uid); });
await began;
assert.deepEqual(await backfillNormalScores(db, config, publish), { processed: 0, done: false }, "Lease prevents overlap");
release();
assert.deepEqual(await first, { processed: 200, done: false });
assert.deepEqual(await backfillNormalScores(db, config, publish), { processed: 5, done: true });
assert.equal(published.size, 203);
assert.equal(published.has("p000"), false, "Opt-out honored");
assert.equal(published.has("p001"), false, "Infinite-only saves excluded");
assert.ok([...published.values()].every((score) => score === 1400), "Retries never duplicate points");
checkpoint = (await db.doc(NORMAL_BACKFILL_PATH).get()).data();
assert.equal(checkpoint.cursor, "p204");
assert.equal(checkpoint.processed, 205);
assert.equal(checkpoint.done, true);
assert.equal((await backfillNormalScores(db, config, publish)).processed, 0, "Completed backfill stays complete");
assert.deepEqual((await db.doc("groves/untouched").get()).data(), { name: "Wave player", wave: 44, level: 1 });
assert.equal((await db.doc("leaderboards/endless").get()).data().entries[0].wave, 44);
assert.equal((await db.doc("leaderboards/shuffle").get()).data().entries[0].shuffle, 22);

// Normal-only profiles participate in the existing name moderation mechanism.
await db.doc("normalCards/moderated").set({ name: "Chosen Name", normalScore: 1400, level: 1 });
await db.doc("players/moderated/private/wallet").set({ name: { key: "chosenname", public: "Chosen Name", atUnix: 1 } });
const report = await reportName(db, "reporter", "moderated", 1700000000, 1);
assert.equal(report.outcome, "hidden");
assert.equal((await db.doc("normalCards/moderated").get()).data().name, fallbackName("moderated"));

// Exercise the actual client rules with an emulator-only unsigned Auth token.
const part = (value) => Buffer.from(JSON.stringify(value)).toString("base64url");
const token = part({ alg: "none", typ: "JWT" }) + "." + part({
  aud: projectId, iss: `https://securetoken.google.com/${projectId}`, sub: "reader", user_id: "reader",
  iat: Math.floor(Date.now() / 1000), exp: Math.floor(Date.now() / 1000) + 3600,
  firebase: { sign_in_provider: "anonymous", identities: {} },
}) + ".";
const api = `http://${process.env.FIRESTORE_EMULATOR_HOST}/v1/projects/${projectId}/databases/(default)/documents`;
const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
assert.equal((await fetch(api + "/normalCards/p002", { headers })).status, 200);
assert.equal((await fetch(api + "/normalCards/p002")).status, 403);
assert.equal((await fetch(api + "/normalCards", { headers })).status, 403);
assert.equal((await fetch(api + "/normalCards/p002", { method: "PATCH", headers,
  body: JSON.stringify({ fields: { normalScore: { integerValue: "9999999" } } }) })).status, 403);
console.log("Normal emulator: top 100, withdrawals, backfill restart/lease/two pages, isolation, moderation and rules passed");
await db.terminate();
