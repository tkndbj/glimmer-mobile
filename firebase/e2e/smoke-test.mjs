#!/usr/bin/env node
/**
 * Deployment smoke test — run after every deploy.
 *
 *     node firebase/e2e/smoke-test.mjs
 *
 * It signs in as a real anonymous client and attacks its own account, because the
 * security rules are the one part of this system that no unit test can check. Rules
 * are evaluated by Firestore, not by the app: a mistake in them cannot fail a compile,
 * cannot fail the Unity test suite, and does not misbehave in the Editor — it misbehaves
 * in production, silently, as either "cloud save stopped working" or "currency is free".
 *
 * The checks are written from the attacker's side on purpose. Confirming that a legal
 * write succeeds proves very little; confirming that granting yourself a million
 * credits is refused proves the thing that matters.
 *
 * It leaves behind one anonymous auth account and its save document. Delete them from
 * the console if you care, or leave them — anonymous accounts are cheap and Firebase
 * can be configured to expire unused ones automatically.
 */

import { execSync } from "node:child_process";

const PROJECT = process.env.GLIMMER_PROJECT ?? "glimmer-groove-1cd60";
const REGION = process.env.GLIMMER_REGION ?? "europe-west1";
const FN = `https://${REGION}-${PROJECT}.cloudfunctions.net`;
const FS = `https://firestore.googleapis.com/v1/projects/${PROJECT}/databases/(default)/documents`;

const SEED_CREDITS = 1250;

/** Matches `DailyRules.SecondsPerDay` and `daily.ts`. A day key is days since the epoch. */
const SECONDS_PER_DAY = 86400;

/**
 * The Android app's web API key, used to mint an anonymous token to test the rules with.
 *
 * The app id is looked up rather than assumed. Once both an Android and an iOS app are
 * registered, `apps:sdkconfig ANDROID` refuses to guess which one is meant and exits
 * non-zero — which is a confusing way for this script to die, since nothing about the
 * project is actually wrong.
 */
function apiKey() {
  if (process.argv[2]) return process.argv[2];

  const listed = execSync(`firebase apps:list ANDROID --project ${PROJECT}`, {
    encoding: "utf8", stdio: ["ignore", "pipe", "pipe"],
  });

  const appId = listed.match(/(1:\d+:android:[0-9a-f]+)/);
  if (!appId) {
    throw new Error(
      `no Android app found on ${PROJECT}. Pass the web API key as the first argument instead.`
    );
  }

  const config = execSync(
    `firebase apps:sdkconfig ANDROID ${appId[1]} --project ${PROJECT}`,
    { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }
  );

  const match = config.match(/"current_key"\s*:\s*"([^"]+)"/);
  if (!match) throw new Error("could not read the app's API key; pass it as the first argument");
  return match[1];
}

let pass = 0, fail = 0;
const check = (ok, what, detail = "") => {
  if (ok) { pass++; console.log(`  ok   ${what}`); }
  else { fail++; console.log(`  FAIL ${what} ${detail}`); }
};

const KEY = apiKey();

const signUp = await fetch(`https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=${KEY}`, {
  method: "POST", headers: { "Content-Type": "application/json" },
  body: JSON.stringify({ returnSecureToken: true }),
});

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

// ------------------------------------------------------------------ the save
console.log("save document");

const save = {
  fields: {
    schemaVersion: { integerValue: "2" },
    updatedUnix: { integerValue: "1700000000" },
    legacyImportDone: { booleanValue: true },
    lastPlayedLevelId: { stringValue: "c01_first_light" },
    checksum: { stringValue: "smoketest" },
    // A map keyed by level id, matching FirestoreSaveMapper. An array here would be
    // refused by the rules and would derive no credits on the server.
    levels: { mapValue: { fields: {
      c01_first_light: { mapValue: { fields: {
        stars: { integerValue: "3" },
        bestMoves: { integerValue: "12" },
        clears: { integerValue: "1" },
        firstClearedUnix: { integerValue: "1600000000" },
        lastPlayedUnix: { integerValue: "1700000000" },
      } } },
    } } },
    settings: { mapValue: { fields: { music: { integerValue: "1" }, sfx: { integerValue: "1" },
                                      haptics: { integerValue: "1" }, language: { stringValue: "en" } } } },
    // The heart ledger, plus the derived count beside it. Sub-fields of `wallet` are not
    // named by the rules, but this fixture is the only thing that writes the shape the
    // client actually sends, so it sends the whole of it.
    wallet: { mapValue: { fields: { heartsProduced: { integerValue: "9" },
                                    heartsSpent: { integerValue: "5" },
                                    heartsDueUnix: { integerValue: "1700028800" },
                                    hearts: { integerValue: "4" },
                                    heartsNextRefillUnix: { integerValue: "1700028800" },
                                    heartBoostUntilUnix: { integerValue: "0" },
                                    displayName: { stringValue: "Grovekeeper" } } } },
    // Today's chest counters. Present here for one reason: this fixture is the only
    // thing in the repo that tests a real write against the *deployed* rules, so a
    // field the client sends and this does not is a field nothing checks. That gap is
    // how a `daily` key that was added locally and never released reached a handset
    // and turned every sync into PERMISSION_DENIED.
    //
    // The rule to keep: when FirestoreSaveMapper gains a top-level field, add it here
    // in the same commit. CloudWireTests already compares the mapper against the rules
    // *file*; only this compares it against the rules that are actually live.
    daily: { mapValue: { fields: { dayKey: { integerValue: "20315" },
                                   runs: { integerValue: "2" },
                                   claimed: { integerValue: "0" } } } },
    // Today's rewarded-ad allowance, here for exactly the reason `daily` is above: the
    // mapper sends it, so this has to send it, or nothing checks that the live rules
    // accept it. `watched` is a list of maps rather than a map keyed by placement id,
    // because a placement id is content and a Firestore field name is not.
    ads: { mapValue: { fields: { dayKey: { integerValue: "20315" },
                                 lastWatchedUnix: { integerValue: "1700000000" },
                                 watched: { arrayValue: { values: [
                                   { mapValue: { fields: { placement: { stringValue: "heart_refill" },
                                                           count: { integerValue: "1" } } } },
                                 ] } } } } },
    // The streak's three dates, here for the same reason `daily` and `ads` are: the
    // mapper sends them, so this has to, or nothing checks that the live rules accept
    // them. Newer than either, and the one whose absence would be most expensive — the
    // ladder pays currency now, so a PERMISSION_DENIED here would stop the save pushing
    // and the nights would pile up uncollected behind it.
    streak: { mapValue: { fields: { startDay: { integerValue: "20310" },
                                    lastPlayedDay: { integerValue: "20315" },
                                    collectedThroughDay: { integerValue: "20314" } } } },
    // How much of each event's reward track has been taken, here for the reason the three
    // blocks above are and one sharper than any of them: `eventCredits` *pays* on this, so
    // a PERMISSION_DENIED here would stop the save pushing and the server would go on
    // deriving a balance without the milestones the game has already shown as collected.
    // A list of maps rather than a map keyed by event id, because an event id is content
    // and a Firestore field name is not.
    eventsSeeded: { booleanValue: true },
    events: { arrayValue: { values: [
      { mapValue: { fields: { id: { stringValue: "first_bloom" },
                              collectedGoal: { integerValue: "2" } } } },
    ] } },
    progression: { mapValue: { fields: { xpHighWater: { integerValue: "100" },
                                         levelHighWater: { integerValue: "2" } } } },
    cloud: { mapValue: { fields: { userId: { stringValue: uid }, revision: { integerValue: "1" },
                                   lastSyncedUnix: { integerValue: "0" },
                                   deviceId: { stringValue: "smoke" } } } },
  },
};

const write = await fetch(`${FS}/players/${uid}`, { method: "PATCH", headers: json, body: JSON.stringify(save) });
check(write.ok, "a well-formed save is accepted", write.ok ? "" : (await write.text()).slice(0, 300));
check((await fetch(`${FS}/players/${uid}`, { headers: bearer })).ok, "own save is readable");

// hasOnly in the rules is what keeps the document to a known shape.
const smuggled = await fetch(`${FS}/players/${uid}?updateMask.fieldPaths=smuggled`, {
  method: "PATCH", headers: json, body: JSON.stringify({ fields: { smuggled: { stringValue: "x" } } }) });
check(smuggled.status === 403, "a field the rules do not list is refused", `got ${smuggled.status}`);

// The point of keying the ledger by level id: one glade can be written on its own
// rather than re-uploading a ledger that may run to thousands of entries.
const partial = await fetch(
  `${FS}/players/${uid}?updateMask.fieldPaths=${encodeURIComponent("levels.c01_twin_streams")}`, {
    method: "PATCH", headers: json,
    body: JSON.stringify({ fields: { levels: { mapValue: { fields: {
      c01_twin_streams: { mapValue: { fields: {
        stars: { integerValue: "2" }, bestMoves: { integerValue: "30" },
        clears: { integerValue: "1" }, firstClearedUnix: { integerValue: "1700000100" },
        lastPlayedUnix: { integerValue: "1700000100" },
      } } },
    } } } } }) });
check(partial.ok, "one glade can be written on its own", partial.ok ? "" : (await partial.text()).slice(0, 200));

const afterPartial = await (await fetch(`${FS}/players/${uid}`, { headers: bearer })).json();
const ledger = afterPartial?.fields?.levels?.mapValue?.fields ?? {};
check(!!ledger.c01_first_light && !!ledger.c01_twin_streams,
      "the partial write did not clobber the rest of the ledger",
      `keys: ${Object.keys(ledger).join(",")}`);

// ------------------------------------------------------- money the client wants
console.log("\nserver-owned money");

const forgeGrant = await fetch(`${FS}/players/${uid}/private/wallet`, {
  method: "PATCH", headers: json,
  body: JSON.stringify({ fields: { credits: { mapValue: { fields: {
    granted: { integerValue: "999999" }, spent: { integerValue: "0" },
    confirmedThroughUnix: { integerValue: "0" } } } } } }) });
check(forgeGrant.status === 403, "a client CANNOT grant itself currency", `got ${forgeGrant.status}`);

const forgeSpendLog = await fetch(`${FS}/players/${uid}/spendLog/forged`, {
  method: "PATCH", headers: json, body: JSON.stringify({ fields: { amount: { integerValue: "1" } } }) });
check(forgeSpendLog.status === 403, "a client cannot forge a spend record", `got ${forgeSpendLog.status}`);

const peekReceipts = await fetch(`${FS}/receipts/apple__anything`, { headers: bearer });
check(peekReceipts.status === 403, "receipt claims are invisible to clients", `got ${peekReceipts.status}`);

const otherPlayer = await fetch(`${FS}/players/not-me`, { headers: bearer });
check(otherPlayer.status === 403, "another player's save is unreadable", `got ${otherPlayer.status}`);

const publishedConfig = await fetch(`${FS}/config/progression`, { headers: bearer });
check(publishedConfig.ok, "the reward table is readable by a signed-in client");

/**
 * The golden percentages the server may legitimately apply, read from the live config.
 *
 * Needed because a glade's credit reward is not a fixed number: it carries a golden
 * multiplier that is a pure function of (account, level), and this script signs in as a
 * *new* anonymous account every run. Hard-coding the base made the earned-credits check
 * below fail on roughly a third of runs — whenever either glade happened to roll above
 * 100 for that uid — which is the worst kind of failure in a suite about money, because
 * it teaches everyone reading it that a red line here means nothing.
 */
const goldenBands = (async () => {
  const body = await publishedConfig.clone().json().catch(() => null);
  const golden = body?.fields?.golden;

  // Published as a bare array — `progression.ts` reads `config.golden` as `GoldenBand[]`.
  // The `{ bands: [...] }` shape is only how the vector file carries them, so both are
  // accepted here rather than assumed: reading the wrong one silently degrades to [100],
  // which looks exactly like a passing test until a uid rolls above the base.
  const values = golden?.arrayValue?.values
              ?? golden?.mapValue?.fields?.bands?.arrayValue?.values
              ?? [];

  const percents = values
    .map((band) => Number(band?.mapValue?.fields?.percent?.integerValue ?? 0))
    .filter((percent) => percent >= 100);

  if (percents.length === 0) {
    // Never silently: a config with no readable bands means this script cannot know what
    // the server is entitled to answer, and saying so is better than passing by luck.
    check(false, "the golden bands are readable from the live config",
          "none parsed — the earned-credits case below is now only checking the base");
    return [100];
  }

  return percents;
})();

// ------------------------------------------------------------------ functions
console.log("\nfunctions");

const call = async (name, data) => {
  const r = await fetch(`${FN}/${name}`, { method: "POST", headers: json, body: JSON.stringify({ data }) });
  return { status: r.status, body: await r.json().catch(() => ({})) };
};

const creditsOf = (reply) => reply?.result?.wallets?.find((w) => w.currency === "credits");

const wallet = await call("getWallet", {});
check(wallet.status === 200, "getWallet responds", JSON.stringify(wallet.body).slice(0, 200));
check(creditsOf(wallet.body)?.grantedBaseline === SEED_CREDITS,
      `a new account is seeded with ${SEED_CREDITS} credits`,
      `got ${creditsOf(wallet.body)?.grantedBaseline}`);

// The save above holds two cleared glades in c01_shallows, whose override pays
// 20 + 10 per star: three stars is 50, two stars is 40. The server derives this from
// the ledger itself — nothing in the request said anything about currency.
//
// Each glade then carries its own golden multiplier, so the answer is one of a small set
// rather than a single number. Every member of that set is still proof of the thing this
// case is really about: the figure came from the ledger and the published bands, not from
// anything the client said.
const BASE_CREDITS = [50, 40];
const percents = await goldenBands;

const achievable = new Set();
for (const first of percents) {
  for (const second of percents) {
    achievable.add(Math.floor((BASE_CREDITS[0] * first) / 100) +
                   Math.floor((BASE_CREDITS[1] * second) / 100));
  }
}

const earned = creditsOf(wallet.body)?.earnedFloor;
check(achievable.has(earned),
      "the server derives earned credits from the ledger it was sent",
      `got ${earned}, expected one of ${[...achievable].sort((a, b) => a - b).join(", ")}`);

const spendId = "smoke-" + Math.random().toString(36).slice(2, 10);
const debit = { id: spendId, currency: "credits", amount: 100, unix: 1700000001, reason: "smoke" };

const spend = await call("submitSpends", { spends: [debit] });
check(creditsOf(spend.body)?.spentBaseline === 100, "a debit is charged",
      `spent=${creditsOf(spend.body)?.spentBaseline}`);
check((creditsOf(spend.body)?.confirmedSpendIds ?? []).includes(spendId), "the debit comes back confirmed");

// The reason debits carry an id rather than being counted.
const retry = await call("submitSpends", { spends: [debit] });
check(creditsOf(retry.body)?.spentBaseline === 100, "resubmitting the SAME debit does not charge twice",
      `spent=${creditsOf(retry.body)?.spentBaseline}`);

const greedy = await call("submitSpends", {
  spends: [{ id: "smoke-greedy-" + Math.random().toString(36).slice(2, 8),
             currency: "credits", amount: 99999999, unix: 1700000002, reason: "smoke" }] });
check((greedy.body?.result?.rejected ?? []).length === 1,
      "an unaffordable debit is refused rather than clamped");

const fakePurchase = await call("redeemPurchase", {
  receipt: { store: "apple", transactionId: "smoke-fake", productId: "nope", payload: "x" } });
check(fakePurchase.status >= 400, "a receipt that cannot be validated is refused",
      `got ${fakePurchase.status}`);
check(creditsOf((await call("getWallet", {})).body)?.grantedBaseline === SEED_CREDITS,
      "the refused purchase granted nothing");

// ------------------------------------------------------------------ streak nights
//
// The streak is the one reward the server cannot recompute — nothing about "seven days
// running" is derivable from anything it observes — so instead of checking a number it
// checks a *rate*: one night per calendar day, climbing no faster than the calendar. All
// of that lives in `advances`, and none of it can be proved by a unit test alone, because
// the floor it compares against is written by the server into a document no client can
// read. This is the only place the whole path is exercised end to end.
console.log("\nstreak nights");

const gemsOf = (reply) => reply?.result?.wallets?.find((w) => w.currency === "gems");
const rejectedBy = (reply) => reply?.result?.rejected ?? [];

const today = Math.floor(Date.now() / 1000 / SECONDS_PER_DAY);
const night = (day, n, currency) => ({
  id: `streak:${day}:${n}:${currency}`, claimedAmount: 1, unix: 1700000003, reason: "streak_night",
});

const creditsBefore = creditsOf((await call("getWallet", {})).body)?.grantedBaseline;
const gemsBefore = gemsOf((await call("getWallet", {})).body)?.grantedBaseline;

// The floor `readWallet` seeded at yesterday. A brand-new account has no streak to speak
// of, so the top of the ladder is exactly what it must not be able to ask for.
const topRung = await call("claimAwards", { awards: [night(today, 7, "gems")] });
check(gemsOf(topRung.body)?.grantedBaseline === gemsBefore,
      "a fresh account cannot claim the seventh night today",
      `gems ${gemsBefore} -> ${gemsOf(topRung.body)?.grantedBaseline}`);
check(rejectedBy(topRung.body).includes(`streak:${today}:7:gems`),
      "and it is refused rather than left pending for ever");

// The one night it may claim, and the amount comes from config/progression rather than
// from the request — `claimedAmount` above says 1.
const firstNight = await call("claimAwards", { awards: [night(today, 1, "credits")] });
check(creditsOf(firstNight.body)?.grantedBaseline === creditsBefore + 150,
      "the first night pays the ladder's figure, not the client's",
      `credits ${creditsBefore} -> ${creditsOf(firstNight.body)?.grantedBaseline}`);

// The derived id is what makes this safe to resubmit, which the client does on every sync
// until the server confirms it.
const sameNight = await call("claimAwards", { awards: [night(today, 1, "credits")] });
check(creditsOf(sameNight.body)?.grantedBaseline === creditsBefore + 150,
      "resubmitting that night does not pay twice",
      `credits -> ${creditsOf(sameNight.body)?.grantedBaseline}`);

// One day on, seven nights on: the shape a forged save produces every morning.
const outrun = await call("claimAwards", { awards: [night(today + 1, 7, "gems")] });
check(gemsOf(outrun.body)?.grantedBaseline === gemsBefore,
      "a night that outruns the calendar is refused",
      `gems -> ${gemsOf(outrun.body)?.grantedBaseline}`);

// The rung decides the currency, so naming a different one earns nothing. This is what
// stops a client collecting the credit night and asking to be paid in gems.
const wrongCurrency = await call("claimAwards", { awards: [night(today, 1, "gems")] });
check(gemsOf(wrongCurrency.body)?.grantedBaseline === gemsBefore,
      "a credit night cannot be claimed as gems",
      `gems -> ${gemsOf(wrongCurrency.body)?.grantedBaseline}`);

// Hearts are applied by the client and never claimed, so `hearts` is not a currency here
// and the id does not survive parsing.
const notCurrency = await call("claimAwards", { awards: [night(today + 1, 2, "hearts")] });
check(rejectedBy(notCurrency.body).includes(`streak:${today + 1}:2:hearts`),
      "an id naming a non-currency is refused outright");

// The far bound. The lap itself — night eight paying night one's rung — cannot be reached
// from a fresh account without waiting eight days, which is precisely what the floor is
// for; it is pinned instead by the shared vectors, on both implementations. What belongs
// here is the check that a client cannot skip the wait by dating its claim forward.
const preFarmed = await call("claimAwards", { awards: [night(today + 7, 8, "credits")] });
check(creditsOf(preFarmed.body)?.grantedBaseline === creditsBefore + 150,
      "a night dated a week ahead pays nothing",
      `credits -> ${creditsOf(preFarmed.body)?.grantedBaseline}`);
check(rejectedBy(preFarmed.body).includes(`streak:${today + 7}:8:credits`),
      "and is refused rather than left pending");

console.log(`\n${pass} passed, ${fail} failed`);
process.exit(fail === 0 ? 0 : 1);
