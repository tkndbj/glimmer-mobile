#!/usr/bin/env node
/**
 * Proves the *deployed* `publishGrove` derives a rank and writes it onto the card and the board.
 *
 *     node firebase/e2e/rank-badge.mjs
 *
 * **Why this exists as its own probe.** "The deploy said Successful" and "a badge reaches a
 * board row" are two different facts, and the gap between them is silent in both directions. A
 * `publishGrove` that has never heard of `rungOf` answers 200 and writes a perfectly valid card
 * with no `rung` on it. A server whose `config/progression` carries no `ranks` block does
 * exactly the same thing — deliberately, because a ladder nobody told this server about is not
 * one it may guess at. Neither logs anything, and both look identical on the device: every
 * board row simply draws no badge, which `RankArt` is right to do and which reads to a player
 * as a broken screen. That is the state this feature shipped in, and it is what this probe is
 * for.
 *
 * So the test is **differential**: the same account publishes twice, once with a save that
 * meets no rung and once with a save that meets the first, and the two published rungs are
 * compared. An absolute check could not tell a working server from one whose ladder happens to
 * be unreachable.
 *
 * **Everything is read off the published ladder rather than written down here.** Which rung is
 * first, what it asks for, which chapter it names and how many glades that is are all taken
 * from `config/progression` — a retune must not turn this red with an answer that is entirely
 * correct. That is `glades` in `smoke-test.mjs`, and this suite's oldest lesson.
 *
 * **It also proves the lifetime floor**, which is the half most likely to be got wrong: the
 * shipped first rung asks for runs as well as clears, and the save this probe writes carries no
 * tally at all. If the server read only the counted half it would publish nothing here, and the
 * player's own device — which applies the floor — would draw a badge the board does not.
 *
 * <b>It cleans up after itself, and that is a counted check rather than a best-effort tidy.</b>
 * A published card is a row on a live board the moment it is written. One probe's litter
 * survived the 2026-09-17 run and had to be found by hand, because the cleanup failed quietly.
 */

import { execSync } from "node:child_process";

const PROJECT = process.env.GLIMMER_PROJECT ?? "glimmer-groove-1cd60";
const REGION = process.env.GLIMMER_REGION ?? "europe-west1";
const FN = `https://${REGION}-${PROJECT}.cloudfunctions.net`;
const FS = `https://firestore.googleapis.com/v1/projects/${PROJECT}/databases/(default)/documents`;

let pass = 0, fail = 0;
const check = (ok, what, detail = "") => {
  if (ok) { pass++; console.log(`  ok   ${what}`); }
  else { fail++; console.log(`  FAIL ${what} ${detail}`); }
};

function apiKey() {
  if (process.argv[2]) return process.argv[2];

  const listed = execSync(`firebase apps:list ANDROID --project ${PROJECT}`,
                          { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] });
  const appId = listed.match(/(1:\d+:android:[0-9a-f]+)/);
  if (!appId) throw new Error(`no Android app on ${PROJECT}; pass the web API key as argv[2]`);

  const config = execSync(`firebase apps:sdkconfig ANDROID ${appId[1]} --project ${PROJECT}`,
                          { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] });
  const match = config.match(/"current_key"\s*:\s*"([^"]+)"/);
  if (!match) throw new Error("could not read the app's API key; pass it as argv[2]");
  return match[1];
}

// ------------------------------------------------------------------ signing in
const KEY = apiKey();
const signUp = await fetch(
  `https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=${KEY}`,
  { method: "POST", headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ returnSecureToken: true }) });

const auth = await signUp.json();
if (!auth.idToken) {
  console.error("anonymous sign-in failed — is Anonymous enabled in Authentication?");
  console.error(JSON.stringify(auth, null, 2));
  process.exit(1);
}

const uid = auth.localId;
const bearer = { Authorization: `Bearer ${auth.idToken}` };
const json = { ...bearer, "Content-Type": "application/json" };
console.log(`signed in anonymously as ${uid}\n`);

const call = async (name, data) => {
  const r = await fetch(`${FN}/${name}`, { method: "POST", headers: json,
                                           body: JSON.stringify({ data }) });
  return { status: r.status, body: await r.json().catch(() => ({})) };
};

// ------------------------------------------------- what the published table says
console.log("the published ladder");

const config = await (await fetch(`${FS}/config/progression`, { headers: bearer })).json();
const fields = config?.fields ?? {};

const rungs = fields.ranks?.arrayValue?.values ?? [];
check(rungs.length > 0,
      "config/progression carries the ranks block — the seed landed",
      `top-level keys: ${Object.keys(fields).join(", ")}`);

if (rungs.length === 0) {
  console.log("\nthe server has no ladder, so no card can ever carry a rung. Re-seed first.");
  console.log(`\n${pass} passed, ${fail} failed`);
  process.exit(1);
}

const rungOf = (v) => {
  const f = v.mapValue.fields;
  return {
    id: f.id.stringValue,
    requires: (f.requires?.arrayValue?.values ?? []).map((l) => {
      const r = l.mapValue.fields;
      return {
        measure: r.measure.stringValue,
        scope: r.scope?.stringValue ?? "",
        target: Number(r.target.integerValue),
      };
    }),
  };
};

const first = rungOf(rungs[0]);
console.log(`  the first rung is '${first.id}', asking for ` +
            first.requires.map((r) => `${r.measure}${r.scope ? "@" + r.scope : ""} x${r.target}`)
                 .join(", ") + "\n");

// Every level the published catalog ships, grouped by chapter. Derived, never written down.
const levelMap = fields.levelChapters?.mapValue?.fields ?? {};
const byChapter = {};
for (const [level, v] of Object.entries(levelMap)) {
  (byChapter[v.stringValue] ??= []).push(level);
}
for (const list of Object.values(byChapter)) list.sort();

/**
 * Glades to clear so that the first rung is met — worked out from what it asks for, so a
 * retune of the ladder changes the save rather than this file.
 *
 * `runs` is deliberately satisfied by the *floor* (a cleared glade is a run that happened)
 * rather than by writing a tally, because the floor is the half a server can most easily be
 * missing, and missing it is invisible from every other angle.
 */
function gladesMeeting(rung) {
  const wanted = new Set();

  for (const line of rung.requires) {
    if (line.measure === "levels_cleared" && line.scope) {
      const list = byChapter[line.scope] ?? [];
      if (list.length < line.target) return null;
      for (const l of list.slice(0, line.target)) wanted.add(l);
    } else if (line.measure === "levels_cleared" || line.measure === "runs" ||
               line.measure === "wins") {
      // Topped up below, once the scoped clauses have had their say.
    } else {
      return null;                                  // a shape this probe cannot satisfy
    }
  }

  const floorTarget = Math.max(
    0,
    ...rung.requires.filter((r) => ["runs", "wins", "levels_cleared"].includes(r.measure) && !r.scope)
                    .map((r) => r.target));

  for (const level of Object.keys(levelMap).sort()) {
    if (wanted.size >= floorTarget) break;
    wanted.add(level);
  }

  return wanted.size >= floorTarget ? [...wanted] : null;
}

const glades = gladesMeeting(first);
check(glades !== null,
      "the probe can build a save that meets the first rung off the published catalog",
      glades === null ? "the ladder asks for something this probe cannot forge honestly" : "");

if (glades === null) {
  console.log("\nthe first rung's shape changed; teach this probe the new measure.");
  console.log(`\n${pass} passed, ${fail} failed`);
  process.exit(1);
}

console.log(`  a save clearing ${glades.length} glade(s) should reach '${first.id}'\n`);

// ------------------------------------------------------------------ the save
const saveFields = (levels) => ({
  schemaVersion: { integerValue: "2" },
  updatedUnix: { integerValue: "1700000000" },
  legacyImportDone: { booleanValue: true },
  lastPlayedLevelId: { stringValue: glades[0] },
  checksum: { stringValue: "rank-badge-probe" },
  levels: { mapValue: { fields: Object.fromEntries(levels.map((id) => [id, { mapValue: { fields: {
    stars: { integerValue: "3" },
    bestMoves: { integerValue: "12" },
    clears: { integerValue: "1" },
    firstClearedUnix: { integerValue: "1600000000" },
    lastPlayedUnix: { integerValue: "1700000000" },
  } } }])) } },
  settings: { mapValue: { fields: { music: { integerValue: "1" }, sfx: { integerValue: "1" },
                                    haptics: { integerValue: "1" },
                                    language: { stringValue: "en" } } } },
  wallet: { mapValue: { fields: {
    heartsProduced: { integerValue: "9" }, heartsSpent: { integerValue: "5" },
    heartsDueUnix: { integerValue: "1700028800" }, hearts: { integerValue: "4" },
    heartsNextRefillUnix: { integerValue: "1700028800" },
    heartBoostUntilUnix: { integerValue: "0" },
  } } },
});

const SAVE_PATH = process.env.GLIMMER_SAVE_PATH ?? "players";

const writeSave = async (levels) => {
  const r = await fetch(`${FS}/${SAVE_PATH}/${uid}`, {
    method: "PATCH", headers: json,
    body: JSON.stringify({ fields: saveFields(levels) }),
  });
  if (!r.ok) console.log(`    (save write ${r.status}: ${(await r.text()).slice(0, 300)})`);
  return r.ok;
};

const publishedRung = async () => {
  const published = await call("publishGrove", {});
  if (published.status !== 200) {
    console.log(`    (publishGrove ${published.status}: ` +
                `${JSON.stringify(published.body).slice(0, 300)})`);
    return null;
  }
  const card = await (await fetch(`${FS}/groves/${uid}`, { headers: bearer })).json();
  return card?.fields?.rung?.stringValue ?? "";
};

// -------------------------------------------------- below the first rung, then on it
console.log("the card's badge");

check(await writeSave([glades[0]]), "a save one glade deep is accepted");
const without = await publishedRung();

check(without === "",
      "a keeper below the first rung publishes no rung at all",
      `got '${without}'`);

check(await writeSave(glades), `a save clearing ${glades.length} glade(s) is accepted`);
const with_ = await publishedRung();

check(with_ === first.id,
      "**the deployed publishGrove derives the rank and writes it onto the card**",
      `got '${with_}', expected '${first.id}'`);

// The differential. If the running bundle predates the fix — or the ladder never reached the
// server — both publishes answer "", which is exactly the silent failure this probe is for and
// which an absolute check could not tell from a keeper who has genuinely earned nothing.
check(with_ !== without && with_ !== "",
      "and it is the play that moved it, not the deploy answering blank twice",
      `'${without}' without, '${with_}' with`);

// The floor, on the live server. The shipped first rung asks for runs, and this save carries
// no tally — so a server reading only the counted half publishes nothing here while the
// player's own device draws a badge. Asserted separately because it is the clause most
// likely to be missing and the least likely to be noticed.
const wantsRuns = first.requires.some((r) => ["runs", "wins"].includes(r.measure));
if (wantsRuns) {
  check(with_ === first.id,
        "and the lifetime floor is applied — cleared glades count as runs with no tally",
        `'${first.id}' needs runs and the save carries none`);
}

// ------------------------------------------------------------------ onto the board
// A card is a board row the moment it is written (the boards are live), so the rung has to
// travel with it. Read with an admin credential: a board is world-readable, but this is the
// one read where being sure beats being convenient.
const adminToken = execSync("gcloud auth print-access-token", { encoding: "utf8" }).trim();
const admin = { Authorization: `Bearer ${adminToken}` };

const boardDoc = await (await fetch(`${FS}/leaderboards/global`, { headers: admin })).json();
const rows = boardDoc?.fields?.entries?.arrayValue?.values ?? [];
const mine = rows.map((r) => r.mapValue.fields).find((f) => f.uid?.stringValue === uid);

if (mine) {
  check(mine.rung?.stringValue === first.id,
        "and the row it placed on the board carries the badge too",
        `row has '${mine.rung?.stringValue ?? "(absent)"}'`);
} else {
  // A worth of nought never reaches the global board, which is correct and not a fault: this
  // probe builds no grove. Said out loud rather than skipped silently.
  console.log("  --   the probe's card carries no grove worth, so it placed on no board " +
              "(correct; the card above is the contract)");
}

// ------------------------------------------------------------------ cleaning up
console.log("\ncleaning up after itself");

const removed = await call("deleteAccount", {});
check(removed.status === 200 && removed.body?.result?.deleted === true,
      "the probe's own account deletes itself",
      `status ${removed.status} ${JSON.stringify(removed.body).slice(0, 200)}`);

const leftover = await fetch(`${FS}/groves/${uid}`, { headers: admin });
check(leftover.status === 404,
      "and its card is gone, so no rebuild can put it back on the board",
      `groves/${uid} answered ${leftover.status}`);

console.log(`\n${pass} passed, ${fail} failed`);
process.exit(fail === 0 ? 0 : 1);
