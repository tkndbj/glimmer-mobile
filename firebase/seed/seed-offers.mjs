#!/usr/bin/env node
/**
 * Publishes the offer engine's tuning (invariant 64) into Firestore.
 *
 *     node firebase/seed/seed-offers.mjs --check     # prove it, write nothing
 *     node firebase/seed/seed-offers.mjs             # publish
 *
 * What it publishes is `firebase/seed/offers.json`, verbatim, as the `schema1` field of
 * `config/offers`. Every device reads that document beside play (after the launch's first settled
 * sync, then at most every fifteen minutes while a calm screen asks) and deals offers by it from
 * then on; a device that cannot reach the server keeps the last one it read, and one that never
 * read one deals by the build's own tuning (`OfferTuning.ShippedJson`).
 *
 * Deliberately its own tool rather than a block in `seed-config.mjs`, for `seed-shuffle.mjs`' reason:
 * a retune is a decision of its own, and a re-seed done for prices or levels must never publish
 * somebody's half-finished offer tuning as a side effect (the seeder publishes the working tree).
 *
 * **The game's own reader decides, not a copy of it.** Before anything is written this runs
 * `OfferTuningTests.TheSeedFileReadsAndNamesOnlyOfferProducts` through the offline runner, which
 * reads the file with `OfferTuning.TryRead` in its strict form - every figure present and in range,
 * no unknown key, measure, moment or look, no segment named that does not exist, and every product
 * on the offers shelf of the shipped catalog. The client refuses a bad document too (and keeps what
 * it had), so this is the guard that stops one ever reaching the database.
 *
 * **What the tuning can never do**: change what a product grants or costs. Those are the store's
 * and `config/products`' (re-seeded by `seed-config.mjs`), so the worst a mistaken tuning can do is
 * deal the wrong published offer to the wrong player at the wrong time. **The stop** is publishing
 * `"enabled": false` under a new revision; deleting the document returns devices to the build's own
 * tuning, which deals offers.
 *
 * Revisions only climb, so every `offer_shown` event names exactly one set of rules.
 * Authentication reuses the gcloud login, as the other seeders do.
 */

import { readFileSync, existsSync } from "node:fs";
import { execSync, spawnSync } from "node:child_process";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(HERE, "..", "..");
const SOURCE = join(HERE, "offers.json");

const PROJECT = "glimmer-groove-1cd60";
const DOCUMENT = "config/offers";
const SCHEMA = 1;
const FIELD = `schema${SCHEMA}`;
const GATE = "OfferTuningTests.TheSeedFileReadsAndNamesOnlyOfferProducts";
const URL = `https://firestore.googleapis.com/v1/projects/${PROJECT}/databases/(default)/documents/${DOCUMENT}`;

// --------------------------------------------------------------------- the file
if (!existsSync(SOURCE)) throw new Error(`missing ${SOURCE}`);

const body = readFileSync(SOURCE, "utf8");
let authored;

try {
  authored = JSON.parse(body);
} catch (e) {
  throw new Error(`offers.json is not JSON: ${e.message}`);
}

if (authored.schema !== SCHEMA) {
  throw new Error(`offers.json says schema ${authored.schema}; this tool publishes schema ${SCHEMA}`);
}

if (!Number.isInteger(authored.revision) || authored.revision < 1) {
  throw new Error(`offers.json's revision is ${authored.revision}; a published revision is a whole number from 1`);
}

// --------------------------------------------------------------- the game's verdict
console.log(`checking offers.json (revision ${authored.revision}) with the game's own reader...`);

const verdict = spawnSync("python", [join("Tools", "verify", "tests.py"), GATE], {
  cwd: REPO, encoding: "utf8",
});

const said = `${verdict.stdout ?? ""}${verdict.stderr ?? ""}`;
const passed = verdict.status === 0 && /\b1 passed, 0 failed, 0 need the Editor\b/.test(said);

if (!passed) {
  console.log(said.split("\n").filter((l) => !/^\s+\S+\s+ok\s/.test(l)).join("\n"));
  throw new Error(
    "the game refuses this file, or the check could not run. Nothing was published. The fault " +
    "above names the figure and where it is; fix it and run again."
  );
}

console.log(`  ${authored.enabled ? "enabled" : "DISABLED"}: ${authored.offers.length} offer(s), ` +
            `${authored.segments.length} segment(s), holdout ${authored.holdoutPercent}%`);

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

  console.log(`${DOCUMENT}: live revision ${live === null ? "none (devices deal by their build's own tuning)" : liveRevision}`);

  if (live === body) {
    console.log("The live document is already this file. Nothing to publish.");
    return;
  }

  if (authored.revision <= liveRevision) {
    throw new Error(
      `offers.json is revision ${authored.revision}, but revision ${liveRevision} is live. Raise ` +
      `"revision" to ${liveRevision + 1}: every offer_shown event names its revision, so one number ` +
      `may never mean two sets of rules. To roll back, publish the old rules as a new revision.`
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
  console.log("Devices pick it up at their next launch or calm screen (at most every fifteen minutes).");
}

await main();
