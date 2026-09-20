#!/usr/bin/env node
/**
 * Proves the *deployed* `publishGrove` counts copies of a colourless turret — run after
 * deploying it and after re-seeding `config/grove`.
 *
 *     node firebase/e2e/ward-copies.mjs
 *
 * **Why this exists as its own probe**, which is `endless-xp.mjs`'s argument arriving at a
 * second fix with the same shape. A `publishGrove` that has never heard of copies answers 200
 * and writes a perfectly valid card — with four Eclipses on it, off one purchase. Nothing
 * throws and nothing logs, so an absolute check cannot tell the fix from a card that happens
 * to have four seats. The test is **differential**: one account, one loadout, published twice
 * with a different number of copy rows, and the seat counts compared.
 *
 * **It is also differential about the half that must not have moved.** A bare row on a
 * *per-colour* turret is what a build from before colours existed wrote; it means all four
 * seats, somebody paid for them, and a copy rule that read it as a single copy would take
 * three seats off their card with nothing saying so. That case is here beside the other one,
 * because the two are the same line of code read in opposite directions.
 *
 * **Two things are read off the published config rather than written down here** — which is
 * this suite's oldest lesson: the legendary is whichever turret `config/grove` flags, and the
 * wave tally is whatever the published curve says reaches its keeper gate. A retune must not
 * turn this red with an answer that is entirely correct.
 *
 * <b>It cleans up after itself, and that is a counted check rather than a best-effort
 * tidy.</b> Reaching a legendary's gate means forging an endless tally, and a published card
 * carrying one is a row on the Endless Watch board from the next 04:00 rebuild onwards. One
 * survived the 2026-09-17 run of the sibling probe and had to be found by hand; a cleanup that
 * failed quietly is how it survived, so this one is asserted.
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

// ------------------------------------------------- what the published tables say
console.log("what the published roster says");

const grove = await (await fetch(`${FS}/config/grove`, { headers: bearer })).json();
const roster = grove?.fields?.wards?.mapValue?.fields ?? {};
const wardIds = Object.keys(roster);

check(wardIds.length > 0, "config/grove carries the turret roster", `${wardIds.length} turret(s)`);

const entry = (id) => roster[id]?.mapValue?.fields ?? {};
const gateOf = (id) => Number(entry(id).level?.integerValue ?? 0);
const isFree = (id) => entry(id).free?.booleanValue === true;
const isLegend = (id) => entry(id).legendary?.booleanValue === true;

// **The whole point of the re-seed, asserted first.** `copiesOf` reads an absent flag as
// false, which caps nothing — so a stale `config/grove` leaves this probe green on a server
// that is still publishing four Eclipses for one purchase. The flag has to be there.
const LEGEND = wardIds.filter(isLegend).sort()[0];
check(!!LEGEND, "and it flags the colourless band (re-seed if this is red)",
      `${wardIds.filter(isLegend).length} of ${wardIds.length} legendary`);

// The other direction: an ordinary priced turret, whose bare row must keep meaning all four.
const ORDINARY = wardIds.filter((id) => !isLegend(id) && !isFree(id)).sort()[0];
check(!!ORDINARY, "and an ordinary priced turret to read the bare row against", ORDINARY ?? "");

if (!LEGEND || !ORDINARY) { console.log(`\n${pass} passed, ${fail} failed`); process.exit(1); }

const progression = await (await fetch(`${FS}/config/progression`, { headers: bearer })).json();
const fields = progression?.fields ?? {};

const rewards = fields.rewards?.mapValue?.fields ?? {};
const XP_FIRST = Number(rewards.xpFirstClear?.integerValue ?? 0);
const XP_STAR = Number(rewards.xpPerStar?.integerValue ?? 0);

const keeper = fields.keeper?.mapValue?.fields ?? {};
const BAND = (keeper.xpToNext?.arrayValue?.values ?? []).map((v) => Number(v.integerValue));
const TAIL = Number(keeper.tailXpToNext?.integerValue ?? 0);
const STEP = Number(keeper.tailXpIncrement?.integerValue ?? 0);
const CAP = Number(keeper.maxLevel?.integerValue ?? 0);

const endless = fields.endless?.mapValue?.fields ?? {};
const RATE = Number(endless.xpPerWave?.integerValue ?? 0);
const CEILING = Number(endless.maxWaves?.integerValue ?? 0);

/** What one keeper level costs to leave. Mirrors the curve, walked here rather than asked. */
const costOf = (level) =>
  level - 1 < BAND.length ? BAND[level - 1] : TAIL + STEP * (level - 1 - BAND.length);

/** The XP a keeper level demands. Walked here so the expectation is not read off the thing
 *  under test — `endless-xp.mjs`'s rule, inverted. */
function xpForLevel(level) {
  let spent = 0;
  for (let at = 1; at < Math.min(level, CAP); at++) spent += costOf(at);
  return spent;
}

const levelMap = fields.levelChapters?.mapValue?.fields ?? {};
const glade = Object.keys(levelMap).sort()[0];
const override = fields.chapterRewards?.mapValue?.fields?.[levelMap[glade]?.stringValue]
                   ?.mapValue?.fields;
const STAR_XP = Number(override?.xpFirstClear?.integerValue ?? XP_FIRST)
              + 3 * Number(override?.xpPerStar?.integerValue ?? XP_STAR);

// The smallest forgery that clears the gate, rather than the ceiling the sibling probe uses:
// this card becomes a row on the Endless Watch board until the cleanup takes it down, and a
// smaller number is a smaller lie sitting there if the cleanup ever fails.
const GATE = gateOf(LEGEND);
const WAVES = Math.min(CEILING, Math.max(0, Math.ceil((xpForLevel(GATE) - STAR_XP) / RATE)));

check(RATE > 0 && WAVES > 0 && WAVES <= CEILING,
      `a tally that reaches keeper ${GATE}, which is ${LEGEND}'s gate`,
      `${WAVES} wave(s) at ${RATE} xp`);

console.log(`  ${LEGEND} opens at keeper ${GATE}; ${ORDINARY} at ${gateOf(ORDINARY)}\n`);

// ------------------------------------------------------------------ the save
const SAVE_PATH = process.env.GLIMMER_SAVE_PATH ?? "players";

const base = {
  schemaVersion: { integerValue: "2" },
  updatedUnix: { integerValue: "1700000000" },
  legacyImportDone: { booleanValue: true },
  lastPlayedLevelId: { stringValue: glade },
  checksum: { stringValue: "ward-copies-probe" },
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
  endlessBest: { arrayValue: { values: [{ mapValue: { fields: {
    level: { stringValue: "s02_endlesswatch" },
    wave: { integerValue: "20" },
    waves: { integerValue: String(WAVES) },
  } } }] } },
};

const wrote = await fetch(`${FS}/${SAVE_PATH}/${uid}`, {
  method: "PATCH", headers: json, body: JSON.stringify({ fields: base }),
});
check(wrote.ok, "a save that reaches the gate is accepted by the live rules", String(wrote.status));

/** Stands one turret on every colour and claims these rows, then publishes and reads the card. */
async function seatsFor(ward, owned) {
  const patch = await fetch(
    `${FS}/${SAVE_PATH}/${uid}?updateMask.fieldPaths=wardLoadout` +
    `&updateMask.fieldPaths=wardsOwned`,
    { method: "PATCH", headers: json, body: JSON.stringify({ fields: {
        wardLoadout: { arrayValue: { values: ["r", "g", "b", "y"].map((c) => (
          { mapValue: { fields: { colour: { stringValue: c },
                                  ward: { stringValue: ward } } } })) } },
        wardsOwned: { arrayValue: { values: owned.map((v) => ({ stringValue: v })) } },
      } }) });

  if (!patch.ok) {
    console.log(`    (loadout write ${patch.status}: ${(await patch.text()).slice(0, 200)})`);
    return null;
  }

  const published = await call("publishGrove", {});
  if (published.status !== 200) {
    console.log(`    (publishGrove ${published.status}: ` +
                `${JSON.stringify(published.body).slice(0, 200)})`);
    return null;
  }

  const card = await (await fetch(`${FS}/groves/${uid}`, { headers: bearer })).json();
  return (card?.fields?.line?.arrayValue?.values ?? [])
    .map((v) => v.mapValue?.fields ?? {})
    .filter((s) => s?.w?.stringValue === ward)
    .map((s) => s?.c?.stringValue);
}

// ------------------------------------------------- one purchase, one seat
console.log("a colourless turret stands once per copy bought");

const one = await seatsFor(LEGEND, [LEGEND]);
check(one?.length === 1, "**one copy stands on one seat, not four**", JSON.stringify(one));
check(one?.[0] === "r", "and it is the first colour, which is the order the client drops in",
      JSON.stringify(one));

const two = await seatsFor(LEGEND, [LEGEND, `${LEGEND}#2`]);
check(two?.length === 2, "two copies stand on two", JSON.stringify(two));
check(two?.join("") === "rg", "in colour order, so a card and the board agree",
      JSON.stringify(two));

const four = await seatsFor(LEGEND, [LEGEND, `${LEGEND}#2`, `${LEGEND}#3`, `${LEGEND}#4`]);
check(four?.length === 4, "and four fill the line", JSON.stringify(four));

// The differential. A bundle that predates the fix answers four to all three, which is
// exactly the silent failure this probe exists for.
check(one?.length < four?.length,
      "and the copies are what moved it, not the loadout",
      `${one?.length} with one copy, ${four?.length} with four`);

const five = await seatsFor(LEGEND, [LEGEND, `${LEGEND}#2`, `${LEGEND}#3`, `${LEGEND}#4`,
                                     `${LEGEND}#5`]);
check(five?.length === 4, "a fifth copy buys no fifth seat", JSON.stringify(five));

// ------------------------------------------------- the half that must not have moved
console.log("\na bare row on an ordinary turret still means every colour");

const bare = await seatsFor(ORDINARY, [ORDINARY]);
check(bare?.length === 4,
      "**a file from before colours keeps all four seats**",
      `${ORDINARY}: ${JSON.stringify(bare)}`);

const oneColour = await seatsFor(ORDINARY, [`${ORDINARY}:g`]);
check(oneColour?.length === 1 && oneColour?.[0] === "g",
      "and a per-colour row is still exactly its own seat", JSON.stringify(oneColour));

// ------------------------------------- the gate this walk must not ask
console.log("\na turret bought stays bought, whatever the keeper level is now");

// **The case every check above was blind to, and the one a player reported.** Everything so
// far runs on an account whose endless tally was forged *precisely* to clear the legendary's
// gate - so a `publishGrove` that re-asked that gate on a turret already bought passed all of
// it, and the fault only ever showed on a real account, which is at keeper 16 and nowhere near
// 45. A probe that arranges to satisfy a condition cannot see a bug in that condition.
//
// So the tally comes off and the same line goes up again. A gate is permission to pay
// (invariant 15a); re-asking it on a holding is confiscation, and the seat it drops draws as
// the *starter* - which is how a five-star Pyroclast came to be published as `bolt`.
//
// Differential on purpose, for this suite's own reason: a bundle that predates the fix
// publishes no seat here and a perfectly valid card beside it, so only the two readings
// together say which bundle is running.
const dropped = await fetch(
  `${FS}/${SAVE_PATH}/${uid}?updateMask.fieldPaths=endlessBest`,
  { method: "PATCH", headers: json,
    body: JSON.stringify({ fields: { endlessBest: { arrayValue: {} } } }) });

check(dropped.ok, "the forged tally comes off the save", String(dropped.status));

const junior = await seatsFor(LEGEND, [LEGEND]);
check(junior?.length === 1,
      `**a keeper below ${LEGEND}'s gate still stands the one they bought**`,
      JSON.stringify(junior));

// And the level really did fall - otherwise the line above proves nothing, because the account
// would still be clearing the gate it is meant to be under.
const under = await (await fetch(`${FS}/groves/${uid}`, { headers: bearer })).json();
const level = Number(under?.fields?.level?.integerValue ?? 0);

check(level > 0 && level < GATE,
      "on a card whose own keeper level is below that gate",
      `keeper ${level} against a gate of ${GATE}`);

// ------------------------------------------------------------------ cleaning up
console.log("\ncleaning up after itself");

const removed = await call("deleteAccount", {});
check(removed.status === 200 && removed.body?.result?.deleted === true,
      "the probe's own account deletes itself",
      `status ${removed.status} ${JSON.stringify(removed.body).slice(0, 200)}`);

// An admin credential rather than this account's own token: the ID token outlives the account
// by up to an hour, so a rules-based read would answer the same way whether or not the
// document survived, and would prove nothing.
const adminToken = execSync("gcloud auth print-access-token", { encoding: "utf8" }).trim();
const leftover = await fetch(`${FS}/groves/${uid}`,
                             { headers: { Authorization: `Bearer ${adminToken}` } });

check(leftover.status === 404,
      "and its card is gone, so no rebuild can put it on the board",
      `groves/${uid} answered ${leftover.status}`);

console.log(`\n${pass} passed, ${fail} failed`);
process.exit(fail === 0 ? 0 : 1);
