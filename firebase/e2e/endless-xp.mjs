#!/usr/bin/env node
/**
 * Proves the *deployed* `publishGrove` pays for endless waves — run after deploying it.
 *
 *     node firebase/e2e/endless-xp.mjs
 *
 * **Why this exists as its own probe.** "The deploy said Successful" and "the running bundle
 * has the fix in it" are two different facts, and for this particular fix the difference is
 * invisible: a `publishGrove` that has never heard of `endlessXp` returns 200, writes a
 * perfectly valid card, and simply computes a *lower* keeper level — whereupon `buildCard`
 * drops rather than clamps whatever that level gated (invariant 19a). Nothing throws, nothing
 * logs, and the only people affected are the ones who play the Infinite lane most.
 *
 * So the test is differential rather than absolute. The same account publishes twice, once
 * with an `endlessBest` row carrying a tally and once without, and the two keeper levels are
 * compared against a figure computed here from the *published* config. Reading the config
 * rather than hard-coding 15 is this suite's oldest lesson (see `glades` in smoke-test.mjs):
 * a retune must not turn this red with an answer that is entirely correct.
 *
 * It leaves behind one anonymous account, its save and its published card, exactly as
 * smoke-test.mjs does.
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
const config = await (await fetch(`${FS}/config/progression`, { headers: bearer })).json();
const fields = config?.fields ?? {};

const endless = fields.endless?.mapValue?.fields;
check(!!endless, "config/progression carries the endless block — the seed landed",
      JSON.stringify(Object.keys(fields)));
if (!endless) { console.log(`\n${pass} passed, ${fail} failed`); process.exit(1); }

const RATE = Number(endless.xpPerWave?.integerValue ?? 0);
const CEILING = Number(endless.maxWaves?.integerValue ?? 0);
check(RATE > 0 && CEILING > 0, "and it pays", `${RATE} xp/wave capped at ${CEILING}`);

const rewards = fields.rewards?.mapValue?.fields ?? {};
const XP_FIRST = Number(rewards.xpFirstClear?.integerValue ?? 0);
const XP_STAR = Number(rewards.xpPerStar?.integerValue ?? 0);

const keeper = fields.keeper?.mapValue?.fields ?? {};
const BAND = (keeper.xpToNext?.arrayValue?.values ?? []).map((v) => Number(v.integerValue));
const TAIL = Number(keeper.tailXpToNext?.integerValue ?? 0);
const STEP = Number(keeper.tailXpIncrement?.integerValue ?? 0);
const CAP = Number(keeper.maxLevel?.integerValue ?? 0);

/** The keeper curve, walked here so the expectation is not read off the thing under test. */
function keeperLevelFor(xp) {
  let level = 1, spent = 0;
  while (level < CAP) {
    const cost = level - 1 < BAND.length ? BAND[level - 1] : TAIL + STEP * (level - 1 - BAND.length);
    if (spent + cost > xp) break;
    spent += cost;
    level += 1;
  }
  return level;
}

// A glade the published catalog actually names, and the chapter holding it. Derived, never
// written down — a hidden chapter would otherwise turn this red for a content change.
const levelMap = fields.levelChapters?.mapValue?.fields ?? {};
const glade = Object.keys(levelMap).sort()[0];
check(!!glade, "the published catalog names a glade to score against", glade ?? "(none)");

const chapterOverride = fields.chapterRewards?.mapValue?.fields?.[levelMap[glade]?.stringValue]
                          ?.mapValue?.fields;
const xpFirst = Number(chapterOverride?.xpFirstClear?.integerValue ?? XP_FIRST);
const xpStar = Number(chapterOverride?.xpPerStar?.integerValue ?? XP_STAR);
const STAR_XP = xpFirst + 3 * xpStar;

// A tally small enough to sit well under the ceiling, and large enough that the keeper level
// it buys is unambiguously different from the star-only one.
const WAVES = 400;

console.log(`glade ${glade} pays ${STAR_XP} xp at three stars; ${WAVES} waves pay ` +
            `${WAVES * RATE}\n`);

// ------------------------------------------------------------------ the save
const saveFields = (endlessRow) => ({
  schemaVersion: { integerValue: "2" },
  updatedUnix: { integerValue: "1700000000" },
  legacyImportDone: { booleanValue: true },
  lastPlayedLevelId: { stringValue: glade },
  checksum: { stringValue: "endless-xp-probe" },
  levels: { mapValue: { fields: {
    [glade]: { mapValue: { fields: {
      stars: { integerValue: "3" },
      bestMoves: { integerValue: "12" },
      clears: { integerValue: "1" },
      firstClearedUnix: { integerValue: "1600000000" },
      lastPlayedUnix: { integerValue: "1700000000" },
    } } },
  } } },
  settings: { mapValue: { fields: { music: { integerValue: "1" }, sfx: { integerValue: "1" },
                                    haptics: { integerValue: "1" },
                                    language: { stringValue: "en" } } } },
  wallet: { mapValue: { fields: {
    heartsProduced: { integerValue: "9" }, heartsSpent: { integerValue: "5" },
    heartsDueUnix: { integerValue: "1700028800" }, hearts: { integerValue: "4" },
    heartsNextRefillUnix: { integerValue: "1700028800" },
    heartBoostUntilUnix: { integerValue: "0" },
  } } },
  ...(endlessRow ? { endlessBest: { arrayValue: { values: [endlessRow] } } } : {}),
});

// The collection a save lives in (`firestore.rules`: match /players/{uid}), named once.
const SAVE_PATH = process.env.GLIMMER_SAVE_PATH ?? "players";

const writeSave = async (endlessRow) => {
  const r = await fetch(`${FS}/${SAVE_PATH}/${uid}`, {
    method: "PATCH", headers: json,
    body: JSON.stringify({ fields: saveFields(endlessRow) }),
  });
  if (!r.ok) console.log(`    (save write ${r.status}: ${(await r.text()).slice(0, 300)})`);
  return r.ok;
};

const publishedLevel = async () => {
  const published = await call("publishGrove", {});
  if (published.status !== 200) {
    console.log(`    (publishGrove ${published.status}: ` +
                `${JSON.stringify(published.body).slice(0, 300)})`);
    return null;
  }
  const card = await (await fetch(`${FS}/groves/${uid}`, { headers: bearer })).json();
  return Number(card?.fields?.level?.integerValue ?? -1);
};

// ------------------------------------------------- star ledger alone, then with waves
console.log("the card's keeper level");

check(await writeSave(null), "a save with no endless rows is accepted");
const without = await publishedLevel();
check(without === keeperLevelFor(STAR_XP),
      "star XP alone gives the level the published curve says",
      `got ${without}, expected ${keeperLevelFor(STAR_XP)}`);

check(await writeSave({ mapValue: { fields: {
        level: { stringValue: "s02_endlesswatch" },
        wave: { integerValue: "40" },
        waves: { integerValue: String(WAVES) },
      } } }),
      "a save carrying a lifetime tally is accepted by the live rules");

const withWaves = await publishedLevel();
const expected = keeperLevelFor(STAR_XP + Math.min(WAVES, CEILING) * RATE);

check(withWaves === expected,
      "**the deployed publishGrove pays for endless waves**",
      `got ${withWaves}, expected ${expected}`);

// The differential. If the running bundle predates the fix, the two publishes agree — which
// is exactly the silent failure this probe exists for, and an absolute check could not tell
// it from a curve that simply has no band between the two figures.
check(withWaves > without,
      "and the tally is what moved it, not the stars",
      `${without} without, ${withWaves} with`);

// The ceiling, on the live server. A forged tally must come back inside an honest range.
check(await writeSave({ mapValue: { fields: {
        level: { stringValue: "s02_endlesswatch" },
        wave: { integerValue: "40" },
        waves: { integerValue: "999999999" },
      } } }),
      "a save carrying an absurd tally is accepted (the rules do not judge it)");

const forged = await publishedLevel();
check(forged === keeperLevelFor(STAR_XP + CEILING * RATE),
      "a forged tally is bounded by the published ceiling",
      `got ${forged}, expected ${keeperLevelFor(STAR_XP + CEILING * RATE)}`);

console.log(`\n${pass} passed, ${fail} failed`);
process.exit(fail === 0 ? 0 : 1);
