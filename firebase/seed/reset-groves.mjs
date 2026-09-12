#!/usr/bin/env node
/**
 * Clears every stored grove, and every published card built from one.
 *
 *     node firebase/seed/reset-groves.mjs --dry-run     # say what it would touch
 *     node firebase/seed/reset-groves.mjs --confirm     # do it
 *
 * ## Why this exists, and why it is not the whole answer
 *
 * On 2026-09-11 the grove's entire catalogue was replaced — the isometric sheets went and
 * a village rendered from models took their place under new ids — so every stored grove
 * names pieces that no longer exist. What those saves describe is a floor of tiles that
 * read as occupied and draw nothing.
 *
 * **The reset that actually works is `groveEpoch`, in the client** (see
 * `Homestead/GroveEpoch.cs`). It has to be, because the grove's three sections are merged
 * so that nothing is ever lost: clearing them here and nowhere else is undone by the first
 * device that has not synced, which pushes its copy straight back up. An epoch is the one
 * shape a monotonic join can express "this is gone" in.
 *
 * **So what is this for?** Three things the epoch cannot reach on its own:
 *
 *   * the ~210 synthetic saves the live suite has left behind, which no device will ever
 *     open and which would therefore keep their old groves for ever;
 *   * the published cards in `groves/*`, which strangers can read — a card is rebuilt on
 *     its owner's next sync, and an account that never syncs again would leave a village
 *     of dead ids on a public board indefinitely;
 *   * the leaderboard rows built from those cards.
 *
 * It writes `groveEpoch` alongside the blanked fields, so a device that pulls afterwards
 * agrees with the server rather than treating it as the older generation and pushing an
 * old grove back.
 *
 * ## What it deliberately does not touch
 *
 * Nothing outside the grove. Not the wallet, not the star ledger, not companions, not
 * turrets, not receipts. A grove's worth is derived from what is held in it, so clearing
 * one lowers a score that reaches a public board — which is exactly why this is the only
 * thing here that takes anything away, and why it is scoped as narrowly as it can be.
 *
 * Authentication reuses the gcloud login, as `seed-config.mjs` does, so there is no
 * long-lived credential in the repository.
 */

import { execSync } from "node:child_process";

const PROJECT = "glimmer-groove-1cd60";
const FS = `https://firestore.googleapis.com/v1/projects/${PROJECT}/databases/(default)/documents`;

/** `GroveEpoch.Current`. Raised together or the server and the client disagree about which
 *  generation a grove belongs to, and the older one loses every merge. */
const GROVE_EPOCH = 1;

/** The save fields a grove lives in. `homesteadOwned` is the retired v19 mirror and is
 *  blanked with the rest — `GroveStock.In` falls back to it for a document written before
 *  v20, so leaving it would re-grant the whole of an old grove's purchases. */
const GROVE_FIELDS = [
  "homesteadStock",
  "homesteadOwned",
  "homesteadPlaced",
  "groveLandOwned",
];

const dry = !process.argv.includes("--confirm");

function accessToken() {
  try {
    // execSync rather than execFileSync-with-shell: gcloud is a .cmd on Windows and needs
    // a shell, but passing an argument array through one is a quoting hazard Node warns
    // about. The command is a constant, so a single string is safe.
    return execSync("gcloud auth print-access-token", {
      encoding: "utf8",
      stdio: ["ignore", "pipe", "pipe"],
    }).trim();
  } catch (e) {
    throw new Error(
      "could not get a token from gcloud. Run 'gcloud auth login' first.\n" +
        (e.stderr ?? e.message)
    );
  }
}

async function call(token, url, init = {}) {
  const response = await fetch(url, {
    ...init,
    headers: {
      Authorization: `Bearer ${token}`,
      "Content-Type": "application/json",
      ...(init.headers ?? {}),
    },
  });
  if (!response.ok) {
    throw new Error(`${init.method ?? "GET"} ${url} failed: ${response.status} ${await response.text()}`);
  }
  return response.status === 204 ? null : response.json();
}

/**
 * Every document id in a collection, a page at a time.
 *
 * The field mask names a field nothing has, which is how the REST API is asked for ids
 * and no content — and it must not *look* reserved: `__none__` is refused outright with
 * "Invalid reserved name in field path", because a leading double underscore is Firestore's
 * own namespace.
 */
async function ids(token, collection) {
  const out = [];
  let page = "";
  for (;;) {
    const url =
      `${FS}/${collection}?pageSize=300&mask.fieldPaths=idsOnlyNoSuchField` +
      (page ? `&pageToken=${encodeURIComponent(page)}` : "");
    const body = await call(token, url);
    for (const doc of body.documents ?? []) out.push(doc.name.split("/").pop());
    page = body.nextPageToken ?? "";
    if (!page) break;
  }
  return out;
}

/**
 * Blanks one save's grove.
 *
 * An update mask naming only the grove's own fields, so everything else in the document is
 * untouched — a whole-document write here would need to read and re-send a save, which is
 * a race against the owner's next sync and would lose whatever they did in between.
 *
 * The fields are written as *empty arrays* rather than deleted. Absent and empty are the
 * same fact to every reader here (invariant 16e's rule about starter land), and an empty
 * array is the one of the two that survives `hasOnly` unambiguously.
 */
async function clearSave(token, uid) {
  const mask = [...GROVE_FIELDS, "groveEpoch"]
    .map((f) => `updateMask.fieldPaths=${encodeURIComponent(f)}`)
    .join("&");

  const fields = { groveEpoch: { integerValue: String(GROVE_EPOCH) } };
  for (const f of GROVE_FIELDS) fields[f] = { arrayValue: { values: [] } };

  await call(token, `${FS}/players/${uid}?${mask}`, {
    method: "PATCH",
    body: JSON.stringify({ fields }),
  });
}

async function main() {
  const token = accessToken();

  const players = await ids(token, "players");
  const cards = await ids(token, "groves");
  const boards = await ids(token, "leaderboards");

  console.log(
    `${dry ? "DRY RUN — " : ""}${players.length} save(s), ${cards.length} published card(s), ` +
      `${boards.length} leaderboard(s)`
  );

  if (dry) {
    console.log("\nIt would:");
    console.log(`  * blank ${GROVE_FIELDS.join(", ")} on every save and stamp groveEpoch ${GROVE_EPOCH}`);
    console.log(`  * delete every document in groves/ (${cards.length})`);
    console.log(`  * delete every document in leaderboards/ (${boards.length}) — publishGroveRanks rebuilds them`);
    console.log("\nNothing outside the grove is touched. Re-run with --confirm.");
    return;
  }

  let done = 0;
  for (const uid of players) {
    await clearSave(token, uid);
    if (++done % 25 === 0) console.log(`  ${done}/${players.length} save(s)`);
  }
  console.log(`  ${done} save(s) cleared`);

  // The cards go rather than being blanked: a card is *derived*, `publishGrove` rebuilds one
  // from the save on its owner's next sync, and an empty card standing on a board is worse
  // than no card at all. The same argument the account deletion makes about visibility
  // (invariant 27) — a row a stranger can read is the half that cannot wait.
  for (const id of cards) await call(token, `${FS}/groves/${id}`, { method: "DELETE" });
  console.log(`  ${cards.length} card(s) deleted`);

  // And the boards built from them, which `publishGroveRanks` rebuilds nightly.
  for (const id of boards) await call(token, `${FS}/leaderboards/${id}`, { method: "DELETE" });
  console.log(`  ${boards.length} leaderboard(s) deleted`);

  console.log("\nDone. Deploy firestore.rules before any client writes groveEpoch, and note");
  console.log("that a device still holding an old grove clears it on load (GroveEpoch), so");
  console.log("this sweep is the tidy-up rather than the mechanism.");
}

main().catch((e) => {
  console.error(e.message);
  process.exit(1);
});
