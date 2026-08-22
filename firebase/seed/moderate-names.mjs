#!/usr/bin/env node
/**
 * The moderation desk for keeper names.
 *
 *     node firebase/seed/moderate-names.mjs queue           # what is waiting to be looked at
 *     node firebase/seed/moderate-names.mjs show <uid>      # one account's reports
 *     node firebase/seed/moderate-names.mjs hide <uid>      # take a name off the boards now
 *     node firebase/seed/moderate-names.mjs restore <uid>   # put it back, and record the review
 *
 * ## Why this exists as a script and not as an endpoint
 *
 * Every other thing this deployment does is reachable by a player. This is not, and it must not
 * be: hiding and restoring a name are decisions somebody makes with the reports in front of
 * them, so they belong to whoever holds admin credentials. A callable would need an
 * authorisation model this deployment does not have and would be, by some distance, the most
 * interesting endpoint here to attack.
 *
 * Authentication reuses the gcloud login rather than a service-account key file, exactly as
 * `seed-config.mjs` does and for the same reason: a key file in a repository is a key file in a
 * repository.
 *
 * ## What the auto-hide leaves for a person to do
 *
 * `reportKeeperName` hides a name once enough distinct players have reported it, so the urgent
 * half needs nobody. What is left is the half a threshold cannot judge: whether the name was
 * actually offensive. Both mistakes show up here — a name hidden by a brigade (`restore`), and
 * one reported once by somebody who was right (`hide`).
 *
 * `restore` deliberately does **not** delete the reports. They are the record of why the name
 * was hidden, and clearing them would let the same reporters hide it again immediately with
 * nothing on file to show that it had been reviewed. Instead it stamps `reviewedAt` with the
 * count as it stands, and `reportName` measures the threshold from there — so re-hiding a
 * reviewed name takes a fresh threshold of *new* reporters rather than one tap.
 */

import { execSync } from "node:child_process";

const PROJECT = process.env.GLIMMER_PROJECT ?? "glimmer-groove-1cd60";
const FS = `https://firestore.googleapis.com/v1/projects/${PROJECT}/databases/(default)/documents`;

/**
 * How many reports a name needs before it hides itself. Read from `config/names` so this script
 * reports the same number the server is actually using — a moderator reading "2 of 3" against a
 * server running a threshold of five would draw exactly the wrong conclusion.
 */
const DEFAULT_THRESHOLD = 3;

function accessToken() {
  try {
    // execSync rather than execFileSync-with-shell: gcloud is a .cmd on Windows.
    return execSync("gcloud auth print-access-token", { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }).trim();
  } catch (e) {
    throw new Error("could not get a token from gcloud. Run 'gcloud auth login' first.\n" +
                    (e.stderr ?? e.message));
  }
}

const token = accessToken();
const auth = { Authorization: `Bearer ${token}` };
const json = { ...auth, "Content-Type": "application/json" };

/** Firestore's wire format, unwrapped far enough to read. */
function plain(fields) {
  const out = {};
  for (const [k, v] of Object.entries(fields ?? {})) {
    if ("integerValue" in v) out[k] = Number(v.integerValue);
    else if ("stringValue" in v) out[k] = v.stringValue;
    else if ("booleanValue" in v) out[k] = v.booleanValue;
    else if ("doubleValue" in v) out[k] = Number(v.doubleValue);
    else if ("mapValue" in v) out[k] = plain(v.mapValue.fields);
    else out[k] = null;
  }
  return out;
}

async function readDoc(path) {
  const r = await fetch(`${FS}/${path}`, { headers: auth });
  if (r.status === 404) return null;
  if (!r.ok) throw new Error(`reading ${path} failed: ${r.status} ${await r.text()}`);

  return plain((await r.json()).fields);
}

async function patch(path, fields) {
  const mask = Object.keys(fields).map((k) => `updateMask.fieldPaths=${encodeURIComponent(k)}`).join("&");
  const r = await fetch(`${FS}/${path}?${mask}`, {
    method: "PATCH", headers: json, body: JSON.stringify({ fields }),
  });

  if (!r.ok) throw new Error(`writing ${path} failed: ${r.status} ${await r.text()}`);
}

const int = (n) => ({ integerValue: String(Math.floor(n)) });

async function threshold() {
  const config = await readDoc("config/names");
  const value = Math.floor(Number(config?.reportThreshold ?? DEFAULT_THRESHOLD));

  // The same clamp the server applies, so this never prints a number the server would not use.
  return Number.isFinite(value) ? Math.min(100, Math.max(2, value)) : DEFAULT_THRESHOLD;
}

// ------------------------------------------------------------------------- commands

async function queue() {
  const limit = 200;
  const r = await fetch(`${FS}/nameReports?pageSize=${limit}`, { headers: auth });
  if (!r.ok) throw new Error(`listing reports failed: ${r.status} ${await r.text()}`);

  const documents = (await r.json()).documents ?? [];
  const bar = await threshold();

  const rows = documents.map((d) => ({
    uid: d.name.split("/").pop(),
    ...plain(d.fields),
  }));

  // Hidden names first — those are the ones a person has to confirm or reverse — then the ones
  // climbing towards the threshold, most-reported first.
  rows.sort((a, b) =>
    (b.deniedUnix ? 1 : 0) - (a.deniedUnix ? 1 : 0) || (b.reports ?? 0) - (a.reports ?? 0));

  if (rows.length === 0) {
    console.log("nothing reported.");
    return;
  }

  console.log(`threshold ${bar}; ${rows.length} account(s) with reports\n`);
  console.log("  state     reports  name              account");
  console.log("  " + "-".repeat(62));

  for (const row of rows) {
    const reviewed = Math.floor(Number(row.reviewedAt ?? 0));
    const state = row.deniedUnix ? "HIDDEN " : reviewed ? "reviewed" : "open   ";
    const count = `${row.reports ?? 0}${reviewed ? ` (+${(row.reports ?? 0) - reviewed})` : ""}`;

    console.log(`  ${state}  ${String(count).padStart(7)}  ${String(row.name ?? "").padEnd(16)}  ${row.uid}`);
  }

  console.log("\n  show <account>  for the reporters; hide / restore <account> to act");
}

async function show(uid) {
  const summary = await readDoc(`nameReports/${uid}`);
  if (!summary) return console.log(`${uid} has never been reported.`);

  const wallet = await readDoc(`players/${uid}/private/wallet`);
  const card = await readDoc(`groves/${uid}`);
  const bar = await threshold();

  const held = wallet?.name ?? {};
  const denied = Math.floor(Number(held.deniedUnix ?? 0));

  console.log(`account      ${uid}`);
  console.log(`name         ${summary.name ?? "(unknown)"}`);
  console.log(`reservation  ${held.key ?? "(none)"}`);
  console.log(`on the board ${card?.name ?? "(no card)"}`);
  console.log(`state        ${denied ? `HIDDEN since ${new Date(denied * 1000).toISOString()}` : "visible"}`);
  console.log(`reports      ${summary.reports ?? 0} (threshold ${bar}` +
              `${summary.reviewedAt ? `, ${summary.reviewedAt} at the last review` : ""})`);
  console.log(`first        ${new Date((summary.firstUnix ?? 0) * 1000).toISOString()}`);
  console.log(`latest       ${new Date((summary.lastUnix ?? 0) * 1000).toISOString()}`);

  const r = await fetch(`${FS}/nameReports/${uid}/reporters?pageSize=100`, { headers: auth });
  const reporters = r.ok ? ((await r.json()).documents ?? []) : [];

  console.log(`\n${reporters.length} reporter(s):`);
  for (const d of reporters) {
    const at = Math.floor(Number(plain(d.fields).atUnix ?? 0));
    console.log(`  ${d.name.split("/").pop()}  ${new Date(at * 1000).toISOString()}`);
  }
}

async function hide(uid) {
  const wallet = await readDoc(`players/${uid}/private/wallet`);
  const held = wallet?.name;

  if (!held?.key) return console.log(`${uid} holds no reserved name; there is nothing to hide.`);
  if (Math.floor(Number(held.deniedUnix ?? 0)) > 0) return console.log("already hidden.");

  const now = Math.floor(Date.now() / 1000);

  // The wallet is the account's currency document, so this writes the one field and never the
  // document. A whole-document write here would be the most expensive mistake in this codebase.
  await patch(`players/${uid}/private/wallet`, {
    name: { mapValue: { fields: {
      key: { stringValue: held.key },
      public: { stringValue: held.public ?? "" },
      atUnix: int(held.atUnix ?? 0),
      deniedUnix: int(now),
    } } },
  });

  // The live card comes **down** rather than being rewritten, and that is the one place this
  // deliberately differs from the automatic path. `reportName` rewrites the card's name to the
  // generated handle because it already holds that function legitimately, inside the same
  // transaction; computing the handle out here would be a second copy of a rule that lives in
  // functions/src/grove.ts -- invariant 9a, for a string nobody would ever think to check.
  // Deleting is one request, needs no mirrored arithmetic, and is if anything the stricter
  // answer: the account is off the boards entirely until its next publish, which then rebuilds
  // the card under the handle because `deniedUnix` is set by then.
  const dropped = await fetch(`${FS}/groves/${uid}`, { method: "DELETE", headers: auth });
  if (!dropped.ok && dropped.status !== 404) {
    throw new Error(`taking the card down failed: ${dropped.status} ${await dropped.text()}`);
  }

  await patch(`nameReports/${uid}`, { deniedUnix: int(now) });

  console.log(`hidden. "${held.public}" is off the boards now, and comes back as a generated`);
  console.log("handle on this account's next publish. the reservation is kept, so nobody else");
  console.log("can claim that name.");
}

async function restore(uid) {
  const wallet = await readDoc(`players/${uid}/private/wallet`);
  const held = wallet?.name;

  if (!held?.key) return console.log(`${uid} holds no reserved name.`);
  if (Math.floor(Number(held.deniedUnix ?? 0)) <= 0) return console.log("not hidden.");

  const summary = await readDoc(`nameReports/${uid}`);
  const reports = Math.floor(Number(summary?.reports ?? 0));
  const now = Math.floor(Date.now() / 1000);

  await patch(`players/${uid}/private/wallet`, {
    name: { mapValue: { fields: {
      key: { stringValue: held.key },
      public: { stringValue: held.public ?? "" },
      atUnix: int(held.atUnix ?? 0),
      deniedUnix: int(0),
    } } },
  });

  // `reviewedAt` is what stops the next single report undoing this. The reports themselves are
  // kept — they are the record of why the name was hidden.
  await patch(`nameReports/${uid}`, {
    deniedUnix: int(0), reviewedUnix: int(now), reviewedAt: int(reports),
  });

  console.log(`restored. "${held.public}" returns to the boards on this account's next publish.`);
  console.log(`re-hiding it now needs a fresh threshold of new reporters (${reports} on file).`);
}

// ---------------------------------------------------------------------------- main

const [command, uid] = process.argv.slice(2);

const needsUid = (name) => {
  if (!uid) {
    console.error(`${name} needs an account id. Run 'queue' to see them.`);
    process.exit(1);
  }
};

switch (command) {
  case "queue":   await queue(); break;
  case "show":    needsUid("show"); await show(uid); break;
  case "hide":    needsUid("hide"); await hide(uid); break;
  case "restore": needsUid("restore"); await restore(uid); break;

  default:
    console.log(`usage: node firebase/seed/moderate-names.mjs <command> [account]

  queue             what is waiting to be looked at
  show <account>    one account's reports, and who filed them
  hide <account>    take a name off the boards now
  restore <account> put it back, and record that it was reviewed

project: ${PROJECT}  (override with GLIMMER_PROJECT)`);
    process.exit(command ? 1 : 0);
}
