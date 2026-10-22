// The first steps' chests (`steps.ts`), against the live functions.
//
//     node firebase/e2e/first-steps.mjs [web-api-key]
//
// **Differential for `endless-xp.mjs`'s reason.** A `claimAwards` that has never heard of a
// `steps:` id reads it as a daily-chest id and *rejects* it - and a client drops a rejected claim
// with the balance it inflated (45d), so a new player's royal chests would pay a flash of coins and
// take them straight back. A working server answers differently in every case below:
//
//   - a step chest claimed before the save shows its level cleared is left UNCONFIRMED - neither
//     paid nor rejected - because the save may simply be one sync behind (13a);
//   - once the save shows the level cleared, the same claim is PAID, at the amount this script
//     re-rolls from the published tier with the server's own compiled code - never the client's;
//   - resubmitting it pays nothing more;
//   - a malformed step id is refused at parse.
//
// The tier, the level and the count are read off the *published* config, never typed. A new
// anonymous account every run, removed at the end through `deleteAccount`.

import { execSync } from "node:child_process";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const PROJECT = process.env.GLIMMER_PROJECT ?? "glimmer-groove-1cd60";
const REGION = process.env.GLIMMER_REGION ?? "europe-west1";
const FN = `https://${REGION}-${PROJECT}.cloudfunctions.net`;
const FS = `https://firestore.googleapis.com/v1/projects/${PROJECT}/databases/(default)/documents`;

const HERE = dirname(fileURLToPath(import.meta.url));
const LIB = resolve(HERE, "..", "functions", "lib");
const { rollStepChest } = await import(pathToFileURL(join(LIB, "steps.js")).href);

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

/** A Firestore REST value as plain JSON. */
const plain = (v) => {
  if (!v || typeof v !== "object") return v;
  if ("mapValue" in v) return Object.fromEntries(Object.entries(v.mapValue.fields ?? {}).map(([k, x]) => [k, plain(x)]));
  if ("arrayValue" in v) return (v.arrayValue.values ?? []).map(plain);
  if ("integerValue" in v) return Number(v.integerValue);
  if ("doubleValue" in v) return v.doubleValue;
  if ("stringValue" in v) return v.stringValue;
  if ("booleanValue" in v) return v.booleanValue;
  if ("nullValue" in v) return null;
  return v;
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

// ------------------------------------------------------------------ the published step
const published = plain({ mapValue: (await (await fetch(`${FS}/config/progression`, { headers })).json()) });
const step = published?.firstSteps?.steps?.[0];
check(!!step, "config/progression carries a published chest step (re-seed if not)", JSON.stringify(published?.firstSteps));
const tier = (published?.tasks?.tiers ?? []).find((t) => t.id === step?.tier);
check(!!tier, `the step's tier '${step?.tier}' is in the published tasks block`);
if (!step || !tier) process.exit(1);

// What the first chest is worth, re-rolled here with the server's own code.
const drops = rollStepChest(tier.chest, uid, step.level, 0);
const worth = (currency) => drops.filter((d) => d.kind === currency).reduce((a, d) => a + d.amount, 0);
const currency = worth("credits") > 0 ? "credits" : "gems";
const expected = worth(currency);
check(expected > 0, `chest 0 of ${step.level} pays ${expected} ${currency}`, JSON.stringify(drops));

const id = `steps:${step.level}:0:${currency}`;
const award = { id, claimedAmount: expected, unix: 1700000000, reason: "first_steps" };

const before = await call("getWallet", {});
const base = rowOf(before.body, currency)?.grantedBaseline ?? 0;

// ------------------------------------------------------------------ 1. not yet
const early = await call("claimAwards", { awards: [award] });
check(early.status === 200, "claimAwards responds", JSON.stringify(early.body).slice(0, 200));
check(!rejectedOf(early.body).includes(id), "a claim the save cannot vouch for yet is NOT rejected");
check(!(rowOf(early.body, currency)?.confirmedGrantIds ?? []).includes(id), "and not paid");
check((rowOf(early.body, currency)?.grantedBaseline ?? 0) === base, "the balance did not move");

// ------------------------------------------------------------------ 2. malformed
const bad = await call("claimAwards", { awards: [{ ...award, id: `steps:${step.level}:01:${currency}` }] });
check(rejectedOf(bad.body).includes(`steps:${step.level}:01:${currency}`), "a malformed step id is refused at parse");

// ------------------------------------------------------------------ 3. the save shows the clear
const save = {
  schemaVersion: { integerValue: "46" },
  updatedUnix: { integerValue: "1700000000" },
  legacyImportDone: { booleanValue: true },
  lastPlayedLevelId: { stringValue: step.level },
  checksum: { stringValue: "first-steps-probe" },
  levels: { mapValue: { fields: {
    [step.level]: { mapValue: { fields: {
      stars: { integerValue: "2" }, bestMoves: { integerValue: "40" }, clears: { integerValue: "1" },
      firstClearedUnix: { integerValue: "1700000000" }, lastPlayedUnix: { integerValue: "1700000000" },
    } } },
  } } },
  tasks: { mapValue: { fields: {
    stepsEarned: { arrayValue: { values: [{ stringValue: step.level }] } },
    stepsTaken: { arrayValue: { values: [{ stringValue: `${step.level}:0` }] } },
  } } },
};
const written = await fetch(`${FS}/players/${uid}`, { method: "PATCH", headers, body: JSON.stringify({ fields: save }) });
check(written.ok, "the rules accept a v46 save carrying the steps' lists", written.ok ? "" : (await written.text()).slice(0, 300));

const paid = await call("claimAwards", { awards: [award] });
const row = rowOf(paid.body, currency);
check((row?.confirmedGrantIds ?? []).includes(id), "once the save shows the clear, the chest is paid", JSON.stringify(paid.body).slice(0, 300));
check((row?.grantedBaseline ?? 0) === base + expected, `at the server's re-rolled ${expected}`, `granted ${row?.grantedBaseline} from ${base}`);

const again = await call("claimAwards", { awards: [award] });
check((rowOf(again.body, currency)?.grantedBaseline ?? 0) === base + expected, "resubmitting pays nothing more");

const past = await call("claimAwards", { awards: [{ ...award, id: `steps:${step.level}:${step.count}:${currency}` }] });
check(!rejectedOf(past.body).includes(`steps:${step.level}:${step.count}:${currency}`) &&
      (rowOf(past.body, currency)?.grantedBaseline ?? 0) === base + expected,
      "a chest past the published count is left unconfirmed, never paid");

// ------------------------------------------------------------------ cleaning up
console.log("\ncleaning up after itself");
const removed = await call("deleteAccount", {});
check(removed.status === 200 && removed.body?.result?.deleted === true, "the probe's own account deletes itself",
      `status ${removed.status} ${JSON.stringify(removed.body).slice(0, 200)}`);

console.log(`\n${pass} passed, ${fail} failed`);
process.exit(fail ? 1 : 0);
