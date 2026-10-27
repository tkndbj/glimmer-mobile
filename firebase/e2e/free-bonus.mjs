// The free bonus (`free-bonus.ts`, invariant 67), against the live functions.
//
//     node firebase/e2e/free-bonus.mjs [web-api-key]
//
// **Differential for `endless-xp.mjs`'s reason**: a deployment without the bonus answers 404 on both
// callables, and a stale one would pay without checking. A working server answers, in order:
//
//   - a fresh account is enrolled with both tasks offered, at the gems the *published* block says, and
//     neither done;
//   - a claim for a task not yet done is `not_done`, pays nothing, and is never permanent;
//   - a task the client cannot name is refused (`invalid-argument`);
//   - once a nickname is reserved (`claimName`), the name task is reported done and pays exactly the
//     promised gems, once - a second claim is `spent` and moves nothing;
//   - once a Google identity is linked (attached with the gcloud login, as a real link leaves it on the
//     Auth record), the link task pays the same way; a second account linking that same sign-in would be
//     `used` (held by the unit tests - a provider id can belong to only one live account);
//   - re-asking never re-enrols: the paid tasks stay paid.
//
// A new anonymous account every run, removed at the end through `deleteAccount`. The one record a
// deletion deliberately keeps - the sign-in's `bonusIdentities` hash - is deleted here with the gcloud
// login, so the probe leaves nothing behind.

import { execSync } from "node:child_process";
import { createHash, randomBytes } from "node:crypto";

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
let headers = { Authorization: `Bearer ${auth.idToken}`, "Content-Type": "application/json" };
console.log(`signed in anonymously as ${uid}\n`);

const call = async (name, data) => {
  const r = await fetch(`${FN}/${name}`, { method: "POST", headers, body: JSON.stringify({ data }) });
  return { status: r.status, body: await r.json().catch(() => ({})) };
};
const gemsOf = (reply) => (reply?.result?.wallets ?? []).find((w) => w.currency === "gems")?.grantedBaseline;
const task = (reply, id) => (reply?.result?.tasks ?? []).find((t) => t.id === id);

let identityDoc = null, rawId = null;
try {
  // ---------------------------------------------------------------- the published block
  const published = plain({ mapValue: (await (await fetch(`${FS}/config/progression`, { headers })).json()) });
  const promised = Object.fromEntries((published?.freeBonus?.tasks ?? []).map((t) => [t.id, t.gems]));
  check(promised.link > 0 && promised.name > 0, "config/progression publishes the freeBonus block (re-seed if not)",
        JSON.stringify(published?.freeBonus));

  // ---------------------------------------------------------------- enrolment
  const wallet = await call("getWallet", {});
  check(wallet.status === 200, "getWallet banks the new account");
  const base = gemsOf(wallet.body) ?? 0;

  const first = await call("getFreeBonus", {});
  check(first.status === 200, "getFreeBonus is deployed and invokable", `status ${first.status} ${JSON.stringify(first.body).slice(0, 200)}`);
  check(first.body?.result?.live === true && first.body?.result?.enrolled === true, "a fresh account is live and enrolled",
        JSON.stringify(first.body?.result));
  for (const id of ["link", "name"]) {
    const row = task(first.body, id);
    check(row && row.gems === promised[id] && !row.paid && !row.done, `the ${id} task is offered at the published ${promised[id]} gems, undone`,
          JSON.stringify(row));
  }

  // ---------------------------------------------------------------- not yet done
  for (const id of ["link", "name"]) {
    const early = await call("claimFreeBonus", { task: id });
    check(early.body?.result?.outcome === "not_done" && gemsOf(early.body) === base,
          `claiming ${id} before it is done is not_done and pays nothing`, JSON.stringify(early.body).slice(0, 200));
  }
  const wrong = await call("claimFreeBonus", { task: "email" });
  check(wrong.status === 400, "an unknown task is refused", `status ${wrong.status}`);

  // ---------------------------------------------------------------- the nickname
  // `first-steps.mjs`'s save, the shape the released rules are proved to accept, with no levels.
  const save = {
    schemaVersion: { integerValue: "46" },
    updatedUnix: { integerValue: "1700000000" },
    legacyImportDone: { booleanValue: true },
    lastPlayedLevelId: { stringValue: "" },
    checksum: { stringValue: "free-bonus-probe" },
    levels: { mapValue: { fields: {} } },
  };
  const written = await fetch(`${FS}/players/${uid}`, { method: "PATCH", headers, body: JSON.stringify({ fields: save }) });
  check(written.ok, "a minimal save is accepted (claimName needs one)", written.ok ? "" : (await written.text()).slice(0, 300));

  const name = "Probe" + randomBytes(4).toString("hex");
  const claimed = await call("claimName", { name });
  check(claimed.body?.result?.outcome === "claimed", `a nickname is reserved (${name})`, JSON.stringify(claimed.body).slice(0, 200));

  const named = await call("getFreeBonus", {});
  check(task(named.body, "name")?.done === true && !task(named.body, "name")?.paid, "the name task now reads done, unpaid");

  const paid = await call("claimFreeBonus", { task: "name" });
  check(paid.body?.result?.outcome === "paid" && paid.body?.result?.gems === promised.name,
        `the name task pays ${promised.name}`, JSON.stringify(paid.body).slice(0, 300));
  check(gemsOf(paid.body) === base + promised.name, "and the wallet reply carries the raised balance",
        `${gemsOf(paid.body)} from ${base}`);

  const again = await call("claimFreeBonus", { task: "name" });
  check(again.body?.result?.outcome === "spent" && gemsOf(again.body) === base + promised.name,
        "a second claim is spent and moves nothing");

  // ---------------------------------------------------------------- the sign-in
  // A Google identity attached by the Identity Toolkit admin API (the gcloud login) - what a real Google
  // link leaves on the Auth record, which is all `claimFreeBonus` reads.
  rawId = `free-bonus-probe-${randomBytes(8).toString("hex")}`;
  const admin = execSync("gcloud auth print-access-token", { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }).trim();
  const linked = await fetch(`https://identitytoolkit.googleapis.com/v1/projects/${PROJECT}/accounts:update`, {
    method: "POST",
    headers: { Authorization: `Bearer ${admin}`, "x-goog-user-project": PROJECT, "Content-Type": "application/json" },
    body: JSON.stringify({ localId: uid, linkProviderUserInfo: { providerId: "google.com", rawId, displayName: "Probe" } }),
  });
  const link = await linked.json().catch(() => ({}));
  check(linked.ok, "a Google identity is linked to the probe", JSON.stringify(link).slice(0, 200));
  if (linked.ok) {
    identityDoc = createHash("sha256").update(`google.com:${rawId}`, "utf8").digest("hex");

    const linkPaid = await call("claimFreeBonus", { task: "link" });
    check(linkPaid.body?.result?.outcome === "paid" && gemsOf(linkPaid.body) === base + promised.name + promised.link,
          `once linked, the link task pays ${promised.link}`, JSON.stringify(linkPaid.body).slice(0, 300));
    const linkAgain = await call("claimFreeBonus", { task: "link" });
    check(linkAgain.body?.result?.outcome === "spent", "and only once");
  }

  // ---------------------------------------------------------------- never re-enrolled
  const last = await call("getFreeBonus", {});
  check(task(last.body, "name")?.paid === true, "re-asking keeps the paid task paid (no re-enrolment)");
  check(!identityDoc || task(last.body, "link")?.paid === true, "and the link task too");
} finally {
  // ---------------------------------------------------------------- cleaning up
  console.log("\ncleaning up after itself");
  const removed = await call("deleteAccount", {});
  check(removed.status === 200 && removed.body?.result?.deleted === true, "the probe's own account deletes itself",
        `status ${removed.status} ${JSON.stringify(removed.body).slice(0, 200)}`);

  // ---------------------------------------------------------------- start over, link again
  // The faucet the identity record closes: delete the account, start a new one, link the same Google
  // identity (free again now its account is gone) - the link task must answer `used` and pay nothing.
  if (identityDoc) {
    console.log("\nstarting over with the same sign-in");
    const again = await (await fetch(`https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=${KEY}`, {
      method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ returnSecureToken: true }),
    })).json();
    headers = { Authorization: `Bearer ${again.idToken}`, "Content-Type": "application/json" };
    try {
      const base = gemsOf((await call("getWallet", {})).body) ?? 0;
      const offered = await call("getFreeBonus", {});
      check(task(offered.body, "link") && !task(offered.body, "link").paid, "the new account is offered the link task");

      const admin = execSync("gcloud auth print-access-token", { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }).trim();
      const relinked = await fetch(`https://identitytoolkit.googleapis.com/v1/projects/${PROJECT}/accounts:update`, {
        method: "POST",
        headers: { Authorization: `Bearer ${admin}`, "x-goog-user-project": PROJECT, "Content-Type": "application/json" },
        body: JSON.stringify({ localId: again.localId, linkProviderUserInfo: { providerId: "google.com", rawId, displayName: "Probe" } }),
      });
      check(relinked.ok, "the same Google identity links to the new account", relinked.ok ? "" : (await relinked.text()).slice(0, 200));

      const used = await call("claimFreeBonus", { task: "link" });
      check(used.body?.result?.outcome === "used" && gemsOf(used.body) === base,
            "its link claim is `used` and pays nothing", JSON.stringify(used.body?.result ?? used.body).slice(0, 200));
      check(!task(used.body, "link"), "and the task is withdrawn from the panel");
    } finally {
      const gone = await call("deleteAccount", {});
      check(gone.body?.result?.deleted === true, "the second probe deletes itself");
    }
  }

  if (identityDoc) {
    // The one record a deletion keeps on purpose (account.ts); a probe's own is removed with the gcloud login.
    const token = execSync("gcloud auth print-access-token", { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }).trim();
    const gone = await fetch(`${FS}/bonusIdentities/${identityDoc}`, {
      method: "DELETE", headers: { Authorization: `Bearer ${token}`, "x-goog-user-project": PROJECT },
    });
    check(gone.ok, "the probe's sign-in record is removed", gone.ok ? "" : (await gone.text()).slice(0, 200));
  }
}

console.log(`\n${pass} passed, ${fail} failed`);
process.exit(fail ? 1 : 0);
