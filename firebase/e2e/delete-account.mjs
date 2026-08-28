#!/usr/bin/env node
/**
 * Account deletion, against the deployed function and the real database.
 *
 *     node firebase/e2e/delete-account.mjs
 *
 * <b>Why this is not a unit test.</b> Everything provable offline about deletion already is —
 * the ordering, the idempotency, the local erasure, the client's refusal branches. What no
 * offline test can reach is the half that only exists in Firestore: whether `recursiveDelete`
 * actually takes the subcollections with it, whether a name reservation is genuinely released
 * back to the pool, whether the security rules let a fresh account claim it afterwards, and
 * whether the auth user is really gone. Those are the same class of gap `smoke-test.mjs`
 * exists for — a mistake in any of them cannot fail a compile, cannot fail the Unity suite,
 * and shows up only in production.
 *
 * <b>It deletes only accounts it created.</b> Two throwaway anonymous accounts are minted, one
 * is filled with a save, a published card and a reserved name, and then erased. Unlike the
 * smoke test, this one leaves *less* behind than it makes — the account under test is gone by
 * the end, which is the whole point of it.
 */

import { execSync } from "node:child_process";

const PROJECT = process.env.GLIMMER_PROJECT ?? "glimmer-groove-1cd60";
const REGION = process.env.GLIMMER_REGION ?? "europe-west1";
const FN = `https://${REGION}-${PROJECT}.cloudfunctions.net`;
const FS = `https://firestore.googleapis.com/v1/projects/${PROJECT}/databases/(default)/documents`;

/** Unique per run, so two runs never contend for one reservation. */
const TAG = Math.random().toString(36).slice(2, 8);

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

async function anonymous() {
  const r = await fetch(`https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=${KEY}`, {
    method: "POST", headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ returnSecureToken: true }),
  });

  const body = await r.json();
  if (!body.idToken) throw new Error("anonymous sign-in failed: " + JSON.stringify(body));
  return body;
}

const callAs = async (name, data, token) => {
  const r = await fetch(`${FN}/${name}`, {
    method: "POST",
    headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
    body: JSON.stringify({ data }),
  });
  return { status: r.status, body: await r.json().catch(() => ({})) };
};

const readAs = async (path, token) => {
  const r = await fetch(`${FS}/${path}`, { headers: { Authorization: `Bearer ${token}` } });
  return { status: r.status, body: await r.json().catch(() => ({})) };
};

/** A save the deployed rules will accept, with one glade cleared so the account is worth something. */
const saveDocument = (name) => ({
  fields: {
    schemaVersion: { integerValue: "2" },
    updatedUnix: { integerValue: "1700000000" },
    legacyImportDone: { booleanValue: true },
    lastPlayedLevelId: { stringValue: "c01_first_light" },
    checksum: { stringValue: "deletetest" },
    levels: { mapValue: { fields: {
      c01_first_light: { mapValue: { fields: {
        stars: { integerValue: "3" },
        bestMoves: { integerValue: "12" },
        clears: { integerValue: "1" },
        firstClearedUnix: { integerValue: "1600000000" },
        lastPlayedUnix: { integerValue: "1700000000" },
      } } },
    } } },
    settings: { mapValue: { fields: {
      music: { integerValue: "1" }, sfx: { integerValue: "1" },
      haptics: { integerValue: "1" }, language: { stringValue: "en" },
      board: { integerValue: "1" },
    } } },
    wallet: { mapValue: { fields: {
      heartsProduced: { integerValue: "9" }, heartsSpent: { integerValue: "5" },
      hearts: { integerValue: "4" }, displayName: { stringValue: name },
      displayNameSetUnix: { integerValue: "1700000000" },
    } } },
  },
});

// ================================================================== set the account up
console.log("\nbuilding an account worth deleting");

const victim = await anonymous();
const uid = victim.localId;
const token = victim.idToken;
const jsonHeaders = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };

const NAME = `Deletee${TAG}`;

const written = await fetch(`${FS}/players/${uid}`, {
  method: "PATCH", headers: jsonHeaders, body: JSON.stringify(saveDocument(NAME)),
});
check(written.status === 200, "a save is written", `status ${written.status}`);

const named = await callAs("claimName", { name: NAME }, token);
check(named.body?.result?.outcome === "claimed",
      "a keeper name is reserved", JSON.stringify(named.body).slice(0, 200));

const nameKey = named.body?.result?.key ?? NAME.toLowerCase();

const published = await callAs("publishGrove", {}, token);
check(published.status === 200, "a grove card is published", `status ${published.status}`);

const cardBefore = await readAs(`groves/${uid}`, token);
check(cardBefore.status === 200, "and the card is really there");

// The wallet is created by the server, so touching it proves there is a subcollection to
// take with the parent — `recursiveDelete`'s whole job.
const walletCall = await callAs("getWallet", {}, token);
check(walletCall.status === 200, "and a server-owned wallet exists under the save");

// ============================================================== delete it
console.log("\ndeleting");

const deleted = await callAs("deleteAccount", {}, token);
check(deleted.status === 200, "deleteAccount responds",
      `status ${deleted.status} ${JSON.stringify(deleted.body).slice(0, 300)}`);
check(deleted.body?.result?.deleted === true, "and reports the account deleted");

// ============================================================== prove it is gone
console.log("\nwhat is left");

// Read with an admin credential rather than the deleted account's token: the token is still
// valid for up to an hour, but a rules-based read would answer 403 either way and prove
// nothing about whether the document survived.
const adminToken = execSync("gcloud auth print-access-token", { encoding: "utf8" }).trim();
const admin = { Authorization: `Bearer ${adminToken}` };

const readAdmin = async (path) => {
  const r = await fetch(`${FS}/${path}`, { headers: admin });
  return r.status;
};

check(await readAdmin(`players/${uid}`) === 404, "the save document is gone");
check(await readAdmin(`players/${uid}/private/wallet`) === 404,
      "the server-owned wallet went with it, so recursiveDelete really walked the subcollections");
check(await readAdmin(`groves/${uid}`) === 404, "the public card is gone");
check(await readAdmin(`names/${nameKey}`) === 404, "the keeper name is released back to the pool");

// The name being free is the half that matters to another player, so it is checked the way
// they would meet it — by claiming it.
const heir = await anonymous();
await fetch(`${FS}/players/${heir.localId}`, {
  method: "PATCH",
  headers: { Authorization: `Bearer ${heir.idToken}`, "Content-Type": "application/json" },
  body: JSON.stringify(saveDocument("Grovekeeper")),
});

const reclaimed = await callAs("claimName", { name: NAME }, heir.idToken);
check(reclaimed.body?.result?.outcome === "claimed",
      "and somebody else can now take it", JSON.stringify(reclaimed.body).slice(0, 200));

// The auth user itself. `lookup` answers with an empty user list for an account that no
// longer exists, which is the only way to ask from outside the Admin SDK.
const lookup = await fetch(
  `https://identitytoolkit.googleapis.com/v1/accounts:lookup?key=${KEY}`,
  { method: "POST", headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ idToken: token }) }
);
check(lookup.status !== 200 || !(await lookup.json()).users?.length,
      "the authentication user is gone");

// ============================================================== idempotency
console.log("\ncalling it again");

// The ID token outlives the account by up to an hour, which is exactly the window a retry
// after a dropped reply lands in. It must not throw, and it must not report failure.
const again = await callAs("deleteAccount", {}, token);
check(again.status === 200 || again.status === 401,
      "a second deletion is answered rather than blowing up",
      `status ${again.status} ${JSON.stringify(again.body).slice(0, 200)}`);

// ============================================================== tidy up
await callAs("deleteAccount", {}, heir.idToken);

console.log(`\n${pass} passed, ${fail} failed`);
process.exit(fail === 0 ? 0 : 1);
