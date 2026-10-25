#!/usr/bin/env node
/**
 * Removes the Grovement's eight save keys from every `players/{uid}` document.
 *
 *     node firebase/seed/retire-grove-keys.mjs                      # dry run: count, write nothing
 *     node firebase/seed/retire-grove-keys.mjs --apply --backup F   # back up to F, then remove
 *     node firebase/seed/retire-grove-keys.mjs --check              # fail while any document holds one
 *
 * ## Why
 *
 * The Grovement was removed on 2026-09-21 (save v33) and no build since writes
 * `homesteadStock`, `homesteadOwned`, `homesteadPlaced`, `groveLandOwned`, `groveEpoch`,
 * `groveHall`, `groveHallFacing` or `groveHallSetUnix`. But a sync is a field-masked update, which
 * cannot delete a key it does not name, so every long-standing account still carries them on the
 * server - and `firestore.rules` judges the *merged* document, so the rules had to keep paying for
 * eight dead clauses on every save write. On 2026-10-25 that cost tipped the heaviest saves over
 * Firestore's 1,000-expression cap (12c) and every write from those accounts was refused.
 *
 * The keys can leave the rules only once no document holds them and no client the update wall
 * admits writes them (16z). This is the first half.
 *
 * ## What it touches, and what it cannot
 *
 * - **Only those eight keys.** Each write's field mask names exactly the keys that document holds
 *   and nothing else, so no other field of a save can be written, cleared or reordered.
 * - **Never a document that is not there.** Every write carries `currentDocument.exists = true`,
 *   so an account deleted between the read and the write stays deleted rather than coming back
 *   as an empty document.
 * - **Nothing the game reads.** No build the update wall admits reads these keys (the client
 *   dropped them at v33, the server with this change), and a client never writes them, so a
 *   removal and a concurrent sync commute: neither can undo the other.
 * - **Nothing without a backup.** `--apply` refuses to start without `--backup`, writes every
 *   removed value there (one JSON line per document) and fsyncs it before the first delete. The
 *   file holds player data: it is refused inside the repository.
 *
 * Idempotent: a second run finds nothing and writes nothing. Authentication reuses the gcloud
 * login, as `seed-config.mjs` does; an owner's OAuth token is privileged, so security rules do not
 * apply to these writes (which is what lets it delete keys the new rules no longer name).
 */

import { execSync } from "node:child_process";
import { openSync, writeSync, fsyncSync, closeSync, existsSync } from "node:fs";
import { resolve, relative, dirname, isAbsolute } from "node:path";
import { fileURLToPath } from "node:url";

const PROJECT = process.env.GLIMMER_PROJECT ?? "glimmer-groove-1cd60";
const DOCS = `projects/${PROJECT}/databases/(default)/documents`;
const API = `https://firestore.googleapis.com/v1/${DOCS}`;

/** The Grovement's save keys, removed from the client at save v33. */
export const RETIRED_KEYS = [
  "homesteadStock", "homesteadOwned", "homesteadPlaced", "groveLandOwned",
  "groveEpoch", "groveHall", "groveHallFacing", "groveHallSetUnix",
];

/** Documents read per page, and writes per `batchWrite` (Firestore allows 500). */
const PAGE = 300;
const BATCH = 300;

const REPO = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");

// --------------------------------------------------------------------------- arguments

const args = process.argv.slice(2);
const KNOWN = new Set(["--apply", "--check", "--backup"]);
for (let i = 0; i < args.length; i++) {
  if (args[i] === "--backup") { i++; continue; }
  if (!KNOWN.has(args[i])) {
    console.error(`unknown argument '${args[i]}'. Flags: --apply --backup <file>, --check, or none for a dry run.`);
    process.exit(2);
  }
}

const apply = args.includes("--apply");
const checkOnly = args.includes("--check");
const backupAt = args.includes("--backup") ? args[args.indexOf("--backup") + 1] : undefined;

if (apply && checkOnly) { console.error("--apply and --check are separate runs."); process.exit(2); }
if (apply) {
  if (!backupAt) { console.error("--apply needs --backup <file>: nothing is removed without a copy."); process.exit(2); }
  const path = resolve(backupAt);
  const inside = relative(REPO, path);
  if (!inside.startsWith("..") && !isAbsolute(inside)) {
    console.error(`the backup holds player data and must live outside the repository (${path}).`);
    process.exit(2);
  }
  if (existsSync(path)) { console.error(`refusing to overwrite an existing backup: ${path}`); process.exit(2); }
}

// --------------------------------------------------------------------------- the API

let token = "";
let tokenAt = 0;

/** The owner's token, refreshed well inside its hour so a long run never presents a dead one. */
function auth() {
  if (!token || Date.now() - tokenAt > 30 * 60 * 1000) {
    token = execSync("gcloud auth print-access-token", { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }).trim();
    tokenAt = Date.now();
  }
  return { Authorization: `Bearer ${token}`, "x-goog-user-project": PROJECT, "Content-Type": "application/json" };
}

async function request(url, init = {}, attempt = 0) {
  const r = await fetch(url, { ...init, headers: auth() });
  if ((r.status === 429 || r.status >= 500) && attempt < 5) {
    await new Promise((done) => setTimeout(done, 500 * 2 ** attempt));
    return request(url, init, attempt + 1);
  }
  const body = await r.json().catch(() => ({}));
  if (!r.ok) throw new Error(`${init.method ?? "GET"} ${url.slice(0, 120)} -> ${r.status} ${JSON.stringify(body).slice(0, 300)}`);
  return body;
}

/** Every save, a page at a time, carrying only the retired keys (one read per document). */
async function* saves() {
  const mask = RETIRED_KEYS.map((k) => `mask.fieldPaths=${k}`).join("&");
  let pageToken = "";
  do {
    const page = await request(`${API}/players?pageSize=${PAGE}&${mask}${pageToken ? `&pageToken=${encodeURIComponent(pageToken)}` : ""}`);
    for (const doc of page.documents ?? []) yield doc;
    pageToken = page.nextPageToken ?? "";
  } while (pageToken);
}

/** The retired keys a document still holds. */
const heldBy = (doc) => RETIRED_KEYS.filter((k) => doc.fields && k in doc.fields);

/**
 * Removes the named keys from each document: a field in the mask and absent from the body is
 * deleted, and nothing outside the mask is touched. Non-atomic per document, each write
 * reported on its own, which is what lets one deleted account fail alone.
 */
async function remove(batch) {
  const reply = await request(`https://firestore.googleapis.com/v1/projects/${PROJECT}/databases/(default)/documents:batchWrite`, {
    method: "POST",
    body: JSON.stringify({
      writes: batch.map(({ name, keys }) => ({
        update: { name, fields: {} },
        updateMask: { fieldPaths: keys },
        currentDocument: { exists: true },
      })),
    }),
  });
  return (reply.status ?? []).map((s) => s?.code ?? 0);
}

// --------------------------------------------------------------------------- the run

const started = Date.now();
let scanned = 0;
const holding = [];
const perKey = Object.fromEntries(RETIRED_KEYS.map((k) => [k, 0]));

for await (const doc of saves()) {
  scanned++;
  const keys = heldBy(doc);
  if (keys.length === 0) continue;
  for (const k of keys) perKey[k]++;
  holding.push({ name: doc.name, keys, fields: doc.fields });
}

console.log(`scanned ${scanned} save(s); ${holding.length} still hold a retired grove key`);
for (const k of RETIRED_KEYS) if (perKey[k] > 0) console.log(`  ${k.padEnd(18)} ${perKey[k]}`);

if (checkOnly) {
  console.log(holding.length === 0 ? "ok: no save holds a retired grove key" : "FAIL: run with --apply before releasing the rules that refuse them");
  process.exit(holding.length === 0 ? 0 : 1);
}

if (!apply) {
  console.log("dry run: nothing written. --apply --backup <file> removes them.");
  process.exit(0);
}

if (holding.length === 0) {
  console.log("nothing to remove.");
  process.exit(0);
}

// The backup, whole and on disk, before the first delete.
const backupPath = resolve(backupAt);
const fd = openSync(backupPath, "wx");
for (const { name, keys, fields } of holding) {
  const uid = name.slice(name.lastIndexOf("/") + 1);
  writeSync(fd, JSON.stringify({ uid, removed: Object.fromEntries(keys.map((k) => [k, fields[k]])) }) + "\n");
}
fsyncSync(fd);
closeSync(fd);
console.log(`backed up ${holding.length} document(s) to ${backupPath}`);

let removed = 0;
let gone = 0;
const failed = [];

for (let i = 0; i < holding.length; i += BATCH) {
  const batch = holding.slice(i, i + BATCH);
  const codes = await remove(batch);
  batch.forEach((doc, j) => {
    const code = codes[j] ?? 0;
    if (code === 0) removed++;
    else if (code === 5 || code === 9) gone++;   // NOT_FOUND / FAILED_PRECONDITION: deleted since the read
    else failed.push(`${doc.name.slice(doc.name.lastIndexOf("/") + 1)}: code ${code}`);
  });
  console.log(`  ${Math.min(i + BATCH, holding.length)} / ${holding.length}`);
}

console.log(`removed the retired keys from ${removed} save(s)` +
            (gone > 0 ? `; ${gone} account(s) were deleted meanwhile and left alone` : "") +
            ` in ${((Date.now() - started) / 1000).toFixed(1)}s`);

if (failed.length > 0) {
  console.error(`FAILED on ${failed.length} document(s); re-run to retry them:`);
  for (const f of failed.slice(0, 20)) console.error("  " + f);
  process.exit(1);
}

console.log("re-run with --check to confirm none remain.");
