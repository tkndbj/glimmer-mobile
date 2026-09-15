#!/usr/bin/env node
/**
 * Reporting a keeper's name, and taking it down.
 *
 *     npm --prefix firebase/functions test
 *
 * <b>This is the one part of name moderation with no offline analogue on the client</b> — the
 * threshold, the idempotency and the daily quota all live in one Firestore transaction, and the
 * live smoke test can only prove the happy path against a real deployment. So the transaction
 * is driven here against a fake database that behaves the way the real one does in the two ways
 * that matter: reads happen before writes, and a document that was never written reads as absent.
 *
 * The cases that earn this file are the ones where a wrong answer is *silent*:
 *
 *  - a takedown that a later wallet write erases (proved in wallet.mjs, from the other side);
 *  - a restore that the next single tap undoes, which would make moderation look ignored;
 *  - a quota that a duplicate report spends, which would punish somebody for double-tapping;
 *  - a threshold measured from zero after a review, which is the same bug as the second one
 *    wearing different clothes.
 */

import { existsSync } from "node:fs";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(HERE, "..", "..", "..");
const LIB = join(REPO, "firebase", "functions", "lib");

if (!existsSync(join(LIB, "reports.js"))) {
  console.error("build the functions first: npm --prefix firebase/functions run build");
  process.exit(1);
}

const load = async (name) => import(pathToFileURL(join(LIB, name)).href);

const { reportName, reportKeeper, parseSubject, REPORT_ROOTS, REPORT_PATHS, MAX_REPORTS_PER_DAY } = await load("reports.js");
const { fallbackName } = await load("grove.js");

let pass = 0;
let fail = 0;

function check(name, condition, detail = "") {
  if (condition) { console.log("  ok   " + name); pass++; }
  else { console.log("  FAIL " + name + (detail ? "  — " + detail : "")); fail++; }
}

function equal(name, actual, expected) {
  check(name, actual === expected,
        `expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`);
}

// ------------------------------------------------------------------- a fake Firestore
//
// Deliberately strict about the one thing the real one is strict about: a transaction may not
// read after it has written. Getting that wrong is the commonest way a Firestore transaction
// fails in production and never in a test that does not enforce it.

function fakeDb(seed = {}) {
  const docs = new Map(Object.entries(seed));

  const merge = (into, patch) => {
    const out = { ...into };
    for (const [k, v] of Object.entries(patch)) {
      out[k] = v && typeof v === "object" && !Array.isArray(v)
        ? merge(out[k] ?? {}, v)
        : v;
    }
    return out;
  };

  const snapshot = (path) => ({
    exists: docs.has(path),
    data: () => docs.get(path),
  });

  const db = {
    docs,
    doc: (path) => ({
      path,
      get: async () => snapshot(path),
      set: async (data, options) => {
        docs.set(path, options?.merge ? merge(docs.get(path) ?? {}, data) : data);
      },
    }),

    runTransaction: async (fn) => {
      let written = false;

      const tx = {
        getAll: async (...refs) => {
          if (written) throw new Error("Firestore: read after write in a transaction");
          return refs.map((r) => snapshot(r.path));
        },
        set: (ref, data, options) => {
          written = true;
          docs.set(ref.path, options?.merge ? merge(docs.get(ref.path) ?? {}, data) : data);
        },
        // `update` **replaces** each named field, where `set(..., {merge:true})` merges into
        // it. That is the real difference and it is load-bearing here: a grove takedown writes
        // `{ placed: {} }`, which has to empty the map rather than leave every row standing —
        // and a fake that deep-merged both would have reported the takedown as working while
        // the live card kept the arrangement three people reported.
        update: (ref, data) => {
          written = true;
          if (!docs.has(ref.path)) throw new Error("Firestore: update of a missing document");
          docs.set(ref.path, { ...docs.get(ref.path), ...data });
        },
      };

      return fn(tx);
    },
  };

  return db;
}

const TARGET = "target-uid";
const CARD = `groves/${TARGET}`;
const WALLET = `players/${TARGET}/private/wallet`;

/** A deployment in which the target has published a card under a chosen name. */
function published(extra = {}) {
  return fakeDb({
    [CARD]: { name: "Fern Willow", score: 4200, placed: { "4,4": "bench", "4,5": "wall" } },
    [WALLET]: {
      credits: { granted: 1250, spent: 0 },
      name: { key: "fernwillow", public: "Fern Willow", atUnix: 1000, deniedUnix: 0 },
    },
    ...extra,
  });
}

const NOW = 1_700_000_000;
const THRESHOLD = 3;

// ================================================================== 1. the ordinary path

console.log("\nreporting");
{
  const db = published();

  const first = await reportName(db, "a", TARGET, NOW, THRESHOLD);
  equal("the first report is counted", first.outcome, "recorded");
  equal("and says so", first.reports, 1);
  equal("the name is untouched", db.docs.get(CARD).name, "Fern Willow");

  const second = await reportName(db, "b", TARGET, NOW, THRESHOLD);
  equal("a second reporter counts too", second.reports, 2);
  equal("still untouched", db.docs.get(CARD).name, "Fern Willow");

  const third = await reportName(db, "c", TARGET, NOW, THRESHOLD);
  equal("the third crosses the threshold", third.outcome, "hidden");

  equal("and the live card is corrected in the same transaction",
        db.docs.get(CARD).name, fallbackName(TARGET));
  check("the wallet records when it happened",
        db.docs.get(WALLET).name.deniedUnix === NOW,
        JSON.stringify(db.docs.get(WALLET).name));

  // The wallet is where the account's granted and spent baselines live. A whole-document write
  // here would be the single most expensive mistake available in this codebase.
  equal("and nothing else on the wallet moved",
        db.docs.get(WALLET).credits.granted, 1250);

  // The reservation is not released, which is what stops the reported name being handed to the
  // next person who asks for it.
  equal("the reservation is kept, not released", db.docs.get(WALLET).name.key, "fernwillow");

  const after = await reportName(db, "d", TARGET, NOW, THRESHOLD);
  equal("reporting an already-hidden name is not a failure", after.outcome, "recorded");
}

// ============================================================ 2. one report per person

console.log("\nidempotency");
{
  const db = published();

  await reportName(db, "a", TARGET, NOW, THRESHOLD);
  const again = await reportName(db, "a", TARGET, NOW, THRESHOLD);

  equal("the same player reporting twice is one report", again.outcome, "duplicate");
  equal("and the count does not move", again.reports, 1);

  // The whole reason the threshold counts distinct reporters: otherwise one person holding a
  // button down takes any name in the game off the boards.
  for (let i = 0; i < 10; i++) await reportName(db, "a", TARGET, NOW, THRESHOLD);
  equal("so one account can never reach the threshold alone",
        db.docs.get(CARD).name, "Fern Willow");

  // The first report's timestamp is the useful one, so a repeat must not overwrite it.
  const summary = db.docs.get(REPORT_PATHS.summary("name", TARGET));
  equal("and the first report's date is kept", summary.firstUnix, NOW);
}

// ========================================================== 3. the per-reporter quota

console.log("\nthe daily quota");
{
  const db = fakeDb();

  for (let i = 0; i < MAX_REPORTS_PER_DAY + 5; i++) {
    const uid = `victim-${i}`;
    db.docs.set(`groves/${uid}`, { name: "Somebody" });
    db.docs.set(`players/${uid}/private/wallet`, {
      name: { key: `k${i}`, public: "Somebody", atUnix: 1, deniedUnix: 0 },
    });
  }

  let throttledAt = -1;
  for (let i = 0; i < MAX_REPORTS_PER_DAY + 5; i++) {
    const r = await reportName(db, "spammer", `victim-${i}`, NOW, THRESHOLD);
    if (r.outcome === "throttled" && throttledAt < 0) throttledAt = i;
  }

  equal("one account may file exactly a day's worth", throttledAt, MAX_REPORTS_PER_DAY);

  // Tomorrow is a fresh allowance, which is what makes this a bound on a script rather than a
  // permanent cap on somebody who plays a lot.
  const tomorrow = await reportName(db, "spammer", "victim-0", NOW + 86_400, THRESHOLD);
  check("and the allowance returns the next day", tomorrow.outcome !== "throttled",
        JSON.stringify(tomorrow));

  // A duplicate must not spend a slot: a player who double-taps has not used anything up, and
  // telling them they have would be a refusal they cannot understand or act on.
  const db2 = published();
  await reportName(db2, "a", TARGET, NOW, THRESHOLD);
  for (let i = 0; i < MAX_REPORTS_PER_DAY + 3; i++) {
    await reportName(db2, "a", TARGET, NOW, THRESHOLD);
  }
  const quota = db2.docs.get(REPORT_PATHS.quota("a"));
  equal("a duplicate report spends no quota", quota.filed, 1);
}

// ================================================================ 4. review and restore

console.log("\nrestoring a name a moderator cleared");
{
  const db = published();

  for (const uid of ["a", "b", "c"]) await reportName(db, uid, TARGET, NOW, THRESHOLD);
  equal("hidden to begin with", db.docs.get(CARD).name, fallbackName(TARGET));

  // What `moderate-names.mjs restore` writes. Replayed here rather than called, because the
  // desk holds admin credentials and the deployment has no restore path of its own — so what
  // has to be proved is that *this* half honours what the desk wrote.
  const onFile = db.docs.get(REPORT_PATHS.summary("name", TARGET)).reports;
  await db.doc(WALLET).set({ name: { deniedUnix: 0 } }, { merge: true });
  await db.doc(REPORT_PATHS.summary("name", TARGET)).set(
    { deniedUnix: 0, reviewedUnix: NOW + 60, reviewedAt: onFile }, { merge: true });

  equal("the flag is cleared", db.docs.get(WALLET).name.deniedUnix, 0);

  // The point of `reviewedAt`. Without it the count is still at the threshold, so the next
  // single report re-hides the name and the review is silently undone.
  const nextTap = await reportName(db, "d", TARGET, NOW + 120, THRESHOLD);
  check("the next single report does not undo the review",
        nextTap.outcome !== "hidden", JSON.stringify(nextTap));

  await reportName(db, "e", TARGET, NOW + 130, THRESHOLD);
  const enough = await reportName(db, "f", TARGET, NOW + 140, THRESHOLD);
  equal("but a fresh threshold of new reporters does", enough.outcome, "hidden");

  // The reports are never deleted: they are the record of why the name was hidden.
  const summary = db.docs.get(REPORT_PATHS.summary("name", TARGET));
  equal("and every report is still on file", summary.reports, 6);
}

// ================================================================= 5. nothing to report

console.log("\nthe cases with nothing to hide");
{
  equal("reporting yourself is refused before anything is read",
        (await reportName(published(), TARGET, TARGET, NOW, THRESHOLD)).outcome, "self");

  equal("an account with no published card",
        (await reportName(fakeDb(), "a", TARGET, NOW, THRESHOLD)).outcome, "nothing");

  // A keeper who never renamed is published under a handle the server invented. Reporting it
  // would be reporting us, and it must not consume the reporter's allowance either.
  const handleOnly = fakeDb({
    [CARD]: { name: fallbackName(TARGET) },
    [WALLET]: { credits: { granted: 0, spent: 0 } },
  });

  equal("a keeper published under a generated handle",
        (await reportName(handleOnly, "a", TARGET, NOW, THRESHOLD)).outcome, "nothing");
  check("and that costs the reporter nothing",
        !handleOnly.docs.has(REPORT_PATHS.quota("a")));
}

// =========================================================== 6. reporting an arrangement

console.log("\nreporting a grovement");
{
  const db = published();

  const first = await reportKeeper(db, "a", TARGET, "grove", NOW, THRESHOLD);
  equal("the first report is counted", first.outcome, "recorded");
  check("the arrangement is untouched",
        Object.keys(db.docs.get(CARD).placed).length === 2);

  await reportKeeper(db, "b", TARGET, "grove", NOW, THRESHOLD);
  const third = await reportKeeper(db, "c", TARGET, "grove", NOW, THRESHOLD);
  equal("the third crosses the threshold", third.outcome, "hidden");

  // The whole of what a grove takedown does, and deliberately all of it.
  equal("and the live card's arrangement is emptied in the same transaction",
        Object.keys(db.docs.get(CARD).placed).length, 0);
  equal("the wallet records when it happened", db.docs.get(WALLET).grove.deniedUnix, NOW);
  equal("and nothing else on the wallet moved", db.docs.get(WALLET).credits.granted, 1250);

  // The keeper keeps their name, their score and their row. A takedown is not a punishment
  // pipeline, which is the argument in reports.ts's header.
  equal("the name is untouched", db.docs.get(CARD).name, "Fern Willow");
  equal("and the score with it", db.docs.get(CARD).score, 4200);
}

// ================================================= 7. the two subjects cannot reach each other

console.log("\ntwo subjects, two judgements");
{
  const db = published();

  // Three reporters is the threshold, so this would hide a name. It must hide nothing.
  for (const uid of ["a", "b", "c"]) await reportKeeper(db, uid, TARGET, "grove", NOW, THRESHOLD);

  equal("hiding a grove does not hide the name", db.docs.get(CARD).name, "Fern Willow");
  check("and leaves the name's own flag alone",
        Math.floor(Number(db.docs.get(WALLET).name.deniedUnix ?? 0)) === 0,
        JSON.stringify(db.docs.get(WALLET).name));

  // And the counts are separate: a grove three people reported says nothing about the name.
  check("the two counts are separate collections",
        db.docs.has(REPORT_PATHS.summary("grove", TARGET))
        && !db.docs.has(REPORT_PATHS.summary("name", TARGET)));

  equal("so a name report starts from nothing",
        (await reportKeeper(db, "a", TARGET, "name", NOW, THRESHOLD)).reports, 1);

  // A player who reported the grove has not reported the name, so the pair key must differ —
  // otherwise one tap would be counted against whichever subject was asked about first.
  check("and one player may report both",
        db.docs.has(REPORT_PATHS.reporter("grove", TARGET, "a"))
        && db.docs.has(REPORT_PATHS.reporter("name", TARGET, "a")));
}

// ===================================================== 8. the quota is the account's, not the subject's

console.log("\none allowance across both subjects");
{
  const db = published();

  await reportKeeper(db, "a", TARGET, "name", NOW, THRESHOLD);
  await reportKeeper(db, "a", TARGET, "grove", NOW, THRESHOLD);

  // Two reports is two of the day's twenty, however they were split. A per-subject allowance
  // would be forty, which is the bound doubling itself every time a subject is added.
  equal("both reports spend the same day's allowance",
        db.docs.get(REPORT_PATHS.quota("a")).filed, 2);
}

// ==================================================== 9. nothing to report about an empty floor

console.log("\nan arrangement with nothing on it");
{
  const bare = fakeDb({
    [CARD]: { name: "Fern Willow", score: 0, placed: {} },
    [WALLET]: { name: { key: "fernwillow", public: "Fern Willow", atUnix: 1, deniedUnix: 0 } },
  });

  equal("an empty floor has nothing anybody could have taken offence at",
        (await reportKeeper(bare, "a", TARGET, "grove", NOW, THRESHOLD)).outcome, "nothing");
  check("and that costs the reporter nothing",
        !bare.docs.has(REPORT_PATHS.quota("a")));

  // A card written before groves could be reported carries no `placed` at all. It must read as
  // "nothing here" rather than throwing, which is the only way an older card could break this.
  const old = fakeDb({
    [CARD]: { name: "Fern Willow" },
    [WALLET]: { name: { key: "fernwillow", public: "Fern Willow", atUnix: 1, deniedUnix: 0 } },
  });
  equal("and a card with no arrangement field at all",
        (await reportKeeper(old, "a", TARGET, "grove", NOW, THRESHOLD)).outcome, "nothing");

  // A keeper with no name can still have their grove reported: the two judgements are separate,
  // and an unnamed keeper's benches are as visible as anybody's.
  const nameless = fakeDb({
    [CARD]: { name: fallbackName(TARGET), placed: { "4,4": "wall" } },
    [WALLET]: { credits: { granted: 0, spent: 0 } },
  });
  equal("a keeper published under a generated handle may still have their grove reported",
        (await reportKeeper(nameless, "a", TARGET, "grove", NOW, THRESHOLD)).outcome, "recorded");
}

// ============================================================= 10. what a subject may be

console.log("\nthe subject on the wire");
{
  equal("an absent subject is the name, which is what every older client means",
        parseSubject(undefined), "name");
  equal("and an empty one", parseSubject(""), "name");
  equal("name", parseSubject("name"), "name");
  equal("grove", parseSubject("grove"), "grove");

  // Refused rather than defaulted. A client one drop ahead asking for something this build
  // cannot take down must be told nothing happened, not told its report was counted.
  equal("a subject this build has never heard of is refused", parseSubject("avatar"), null);
  equal("and so is a non-string", parseSubject(7), null);

  // The collection names are permanent ids: renaming one orphans every report filed under the
  // old spelling and resets a threshold somebody had already reached.
  equal("the name subject keeps the collection that shipped", REPORT_ROOTS.name, "nameReports");
  equal("and the grove has its own", REPORT_ROOTS.grove, "groveReports");
}

console.log(`\n${pass} passed, ${fail} failed`);
process.exit(fail === 0 ? 0 : 1);
