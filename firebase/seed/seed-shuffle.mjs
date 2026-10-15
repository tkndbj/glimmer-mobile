#!/usr/bin/env node
/**
 * Publishes the Shuffle lane's tuning (MODES.md 59l) into Firestore.
 *
 *     node firebase/seed/seed-shuffle.mjs --check     # prove it, write nothing
 *     node firebase/seed/seed-shuffle.mjs             # publish
 *
 * What it publishes is `firebase/seed/shuffle.json`, verbatim, as the `schema1` field of
 * `config/shuffle`. Every device reads that document beside play (at launch and on the Shuffle
 * hub, at most every fifteen minutes) and opens its *next* run on it; a run in progress is never
 * touched, and a device that cannot reach the server keeps the last one it read.
 *
 * Deliberately its own tool rather than a block in `seed-config.mjs`, for `seed-release.mjs`'
 * reason: a retune is a decision of its own, and a re-seed done for prices or levels must never
 * publish somebody's half-finished tuning as a side effect (the seeder publishes the working tree).
 *
 * **The game's own reader decides, not a copy of it.** Before anything is written this runs
 * `ShuffleTuningTests.ThePublishedFileIsAcceptedAndRevisionOneIsTheBuildsOwnFigures` through the
 * offline runner, which reads the file with `ShuffleTuning.TryRead` in its strict form - every
 * figure present and in range, no unknown key, no unknown or retired card, no card left out, no
 * figure a card's text states in words moved. A second, hand-written validator here would be a
 * second rule that could disagree with the phone; there is one. The client refuses a bad document
 * too (and keeps what it had), so this is the guard that stops one ever reaching the database.
 *
 * Revisions only climb: the published file must carry a `revision` above the live one, so every
 * run's analytics (`shuffle_ended.tuning`) names exactly one set of figures. To roll back, publish
 * the old figures under a new revision. To return every device to its build's own figures, delete
 * the document (`config/shuffle`) - a device that reads "nothing published" drops its cache.
 *
 * Authentication reuses the gcloud login rather than a key file, as the other seeders do.
 */

import { readFileSync, existsSync } from "node:fs";
import { execSync, spawnSync } from "node:child_process";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(HERE, "..", "..");
const SOURCE = join(HERE, "shuffle.json");

const PROJECT = "glimmer-groove-1cd60";
const DOCUMENT = "config/shuffle";
const SCHEMA = 1;
const FIELD = `schema${SCHEMA}`;
const GATE = "ShuffleTuningTests.ThePublishedFileIsAcceptedAndRevisionOneIsTheBuildsOwnFigures";
const URL = `https://firestore.googleapis.com/v1/projects/${PROJECT}/databases/(default)/documents/${DOCUMENT}`;

// --------------------------------------------------------------------- the file
if (!existsSync(SOURCE)) throw new Error(`missing ${SOURCE}`);

const body = readFileSync(SOURCE, "utf8");
let authored;

try {
  authored = JSON.parse(body);
} catch (e) {
  throw new Error(`shuffle.json is not JSON: ${e.message}`);
}

if (authored.schema !== SCHEMA) {
  throw new Error(`shuffle.json says schema ${authored.schema}; this tool publishes schema ${SCHEMA}`);
}

if (!Number.isInteger(authored.revision) || authored.revision < 1) {
  throw new Error(`shuffle.json's revision is ${authored.revision}; a published revision is a whole number from 1`);
}

// --------------------------------------------------------------- the game's verdict
console.log(`checking shuffle.json (revision ${authored.revision}) with the game's own reader...`);

const verdict = spawnSync("python", [join("Tools", "verify", "tests.py"), GATE], {
  cwd: REPO, encoding: "utf8",
});

const said = `${verdict.stdout ?? ""}${verdict.stderr ?? ""}`;
const passed = verdict.status === 0 && /\b1 passed, 0 failed, 0 need the Editor\b/.test(said);

if (!passed) {
  console.log(said.split("\n").filter((l) => !/^\s+\S+\s+ok\s/.test(l)).join("\n"));
  throw new Error(
    "the game refuses this file, or the check could not run. Nothing was published. The fault " +
    "above names the figure and its range; fix it and run again."
  );
}

// The fixture prints what the file moves away from this build's own figures.
const report = said.split("\n").filter((l) => /^firebase\/seed\/shuffle\.json|^\s{2}(ramp|deck|arsenal|refresh|cards)\./.test(l));
for (const line of report) console.log(line);

// ------------------------------------------------------------------- the live one
function accessToken() {
  try {
    // execSync with a shell, for `seed-config.mjs`' reason: gcloud is a .cmd on Windows.
    return execSync("gcloud auth print-access-token", {
      encoding: "utf8", stdio: ["ignore", "pipe", "pipe"],
    }).trim();
  } catch (e) {
    throw new Error("could not get a token from gcloud. Run 'gcloud auth login' first.\n" +
                    (e.stderr ?? e.message));
  }
}

const token = accessToken();
const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };

async function liveBody() {
  const response = await fetch(URL, { headers });
  if (response.status === 404) return null;
  if (!response.ok) throw new Error(`reading ${DOCUMENT} failed: ${response.status} ${await response.text()}`);

  const doc = await response.json();
  return doc.fields?.[FIELD]?.stringValue ?? null;
}

// Everything from here returns rather than calling process.exit: exiting while fetch's sockets
// are still closing trips a libuv assertion on Windows.
async function main() {
  const live = await liveBody();
  let liveRevision = 0;

  if (live !== null) {
    try {
      liveRevision = JSON.parse(live).revision ?? 0;
    } catch {
      console.log(`  note: the live ${FIELD} is not readable JSON; any revision replaces it`);
    }
  }

  console.log(`${DOCUMENT}: live revision ${live === null ? "none (devices play their build's own figures)" : liveRevision}`);

  if (live === body) {
    console.log("The live document is already this file. Nothing to publish.");
    return;
  }

  if (authored.revision <= liveRevision) {
    throw new Error(
      `shuffle.json is revision ${authored.revision}, but revision ${liveRevision} is live. Raise ` +
      `"revision" to ${liveRevision + 1}: every run's analytics names its revision, so one number ` +
      `may never mean two sets of figures. To roll back, publish the old figures as a new revision.`
    );
  }

  if (process.argv.includes("--check")) {
    console.log(`Validated: revision ${authored.revision} would replace ${liveRevision || "nothing"}. No remote writes.`);
    return;
  }

  // -------------------------------------------------------------------- publishing
  // Only this schema's field and the stamp are written (an update mask), so a later build's
  // `schema2` published beside it is never touched by a schema 1 publish.
  const fields = {
    [FIELD]: { stringValue: body },
    publishedAt: { timestampValue: new Date().toISOString() },
  };

  const mask = Object.keys(fields).map((f) => `updateMask.fieldPaths=${encodeURIComponent(f)}`).join("&");
  const response = await fetch(`${URL}?${mask}`, {
    method: "PATCH",
    headers,
    body: JSON.stringify({ fields }),
  });

  if (!response.ok) {
    throw new Error(`writing ${DOCUMENT} failed: ${response.status} ${await response.text()}`);
  }

  // Read back: "the write returned 200" is not "the document holds the file".
  const after = await liveBody();
  if (after !== body) {
    throw new Error(`${DOCUMENT} was written but reads back differently. Check the console before trusting it.`);
  }

  console.log(`published ${DOCUMENT} revision ${authored.revision}, read back identical.`);
  console.log("Devices pick it up at their next launch or Shuffle hub visit (at most every fifteen " +
              "minutes) and open their next run on it.");
}

await main();
