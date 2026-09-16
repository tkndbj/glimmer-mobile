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

// --------------------------------------------------------------- which glades to use
/**
 * Two glades the *published catalog* actually names, and the chapter each belongs to.
 *
 * <p><b>Derived rather than written down, which is this suite's oldest lesson one field
 * over.</b> The per-star figures below are already read from the published table because
 * hard-coding them meant a retune turned this case red with an answer that was entirely
 * correct. The level ids were still hard-coded — `c01_first_light` and `c01_twin_streams` —
 * and they went the same way the moment their chapter was hidden: `seed-config.mjs` skips a
 * disabled chapter, so those levels stopped being in `levelChapters`, the server correctly
 * valued a save holding them at nothing, and a green suite went red for a content change
 * rather than for a disagreement.</p>
 *
 * <p>Note which side was wrong. The client's `CatalogIndex` has always skipped a disabled
 * chapter (`CatalogIndexBuilder.Add`) and `ProgressionLedger` values a record the catalog
 * has never heard of at nothing, so a *stale* published table was the server over-valuing
 * hidden levels — the seed brought the two into agreement. What this case is for is proving
 * they agree, and it can only do that about glades that ship.</p>
 *
 * <p>Each carries its own chapter, so the two need not share one: a catalog with one level
 * per chapter is a legal catalog and this suite should not be the thing that notices.</p>
 */
const glades = await (async () => {
  const doc = await (await fetch(`${FS}/config/progression`, { headers: bearer })).json();
  const map = doc?.fields?.levelChapters?.mapValue?.fields ?? {};

  const ids = Object.keys(map).sort();
  if (ids.length < 2) {
    throw new Error(
      `config/progression names ${ids.length} level(s); this suite needs two to prove the ` +
      "server derives credits. Run: node firebase/seed/seed-config.mjs");
  }

  return ids.slice(0, 2).map((id) => ({ id, chapter: map[id]?.stringValue ?? "" }));
})();

console.log(`deriving credits against ${glades.map((g) => g.id).join(" and ")}\n`);

// ------------------------------------------------------------------ the save
console.log("save document");

const save = {
  fields: {
    schemaVersion: { integerValue: "2" },
    updatedUnix: { integerValue: "1700000000" },
    legacyImportDone: { booleanValue: true },
    lastPlayedLevelId: { stringValue: glades[0].id },
    checksum: { stringValue: "smoketest" },
    // A map keyed by level id, matching FirestoreSaveMapper. An array here would be
    // refused by the rules and would derive no credits on the server.
    levels: { mapValue: { fields: {
      [glades[0].id]: { mapValue: { fields: {
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
    // The tasks: both periods' counters and claims, here for the reason every block above
    // is. Nothing on the server pays on these — a task chest is bounded by the wallet's own
    // allowance — but the mapper sends them, so a PERMISSION_DENIED here would stop the save
    // pushing and a task done on one phone would read half done on the other.
    tasks: { mapValue: { fields: {
      daily: { mapValue: { fields: { key: { integerValue: "20315" },
                                     counts: { arrayValue: { values: [
                                       { mapValue: { fields: { goal: { stringValue: "runs" },
                                                               count: { integerValue: "2" } } } },
                                     ] } },
                                     claimed: { arrayValue: { values: [ { stringValue: "d_play" } ] } } } } },
      weekly: { mapValue: { fields: { key: { integerValue: "2902" },
                                      counts: { arrayValue: { values: [] } },
                                      claimed: { arrayValue: { values: [] } } } } },
    } } },
    // Each season's marks and its two claim floors, here for the reason the three blocks
    // above are: an unlisted field is refused outright by `hasOnly`, so a rules release
    // that forgot these would not degrade the season, it would turn every sync in the game
    // into PERMISSION_DENIED (invariant 12a). Nothing on the server *derives* from them —
    // a season pays chests, which are claims — which is exactly why they need a live push
    // to prove anything at all. A list of maps rather than a map keyed by season id,
    // because a season id is content and a Firestore field name is not.
    eventsSeeded: { booleanValue: true },
    events: { arrayValue: { values: [
      // A *cycle* id rather than the manifest's stem: the shipped season repeats, so
      // `watch` names no season and `watch_0000` is the first one (`SeasonCycle`).
      { mapValue: { fields: { id: { stringValue: "watch_0000" },
                              marks: { integerValue: "62" },
                              collectedGoal: { integerValue: "55" },
                              premiumGoal: { integerValue: "40" } } } },
    ] } },
    // The lessons already shown and the companions already bought. Here for the reason
    // every block above is: the mapper sends them, so this has to, or nothing checks that
    // the live rules accept them.
    // Two hundred of them, deliberately. The ledger keeps every lesson ever seen, withdrawn
    // modes included, and the rules bounded the list at 64 — the owner's own account crossed
    // that on 2026-09-16 and lost every save write from then on (invariant 12a). The bound
    // is `TipLedger.MaxIds` now, and this is what proves the *released* rules honour it.
    tipsSeen: { arrayValue: { values: Array.from({ length: 200 }, (_, i) => ({ stringValue: `lesson_${i}` })) } },
    companionsOwned: { arrayValue: { values: [{ stringValue: "coral" }] } },
    // The grove: what was bought for it, and where it stands. Same reason again, and it
    // is the newest pair — an unlisted field is refused outright by `hasOnly`, so a rules
    // release that forgot these would not degrade the grove, it would turn every sync in
    // the game into PERMISSION_DENIED. A list of maps rather than a map keyed by slot id,
    // because a slot id is content and a Firestore field name is not.
    homesteadOwned: { arrayValue: { values: [{ stringValue: "fence_low" }] } },
    homesteadPlaced: { arrayValue: { values: [
      { mapValue: { fields: { slot: { stringValue: "meadow_a" },
                              piece: { stringValue: "oak" },
                              setUnix: { integerValue: "1700000000" } } } },
      // A slot the player deliberately emptied: a row with no piece. It is a choice and
      // carries a stamp, which is what stops a stale device putting the tree back.
      { mapValue: { fields: { slot: { stringValue: "meadow_b" },
                              piece: { stringValue: "" },
                              setUnix: { integerValue: "1700000001" } } } },
      // Facing the other way. Part of the arrangement, so it has to survive the trip —
      // a piece that comes back mirrored is the same loss as one that comes back missing.
      { mapValue: { fields: { slot: { stringValue: "t_007_006" },
                              piece: { stringValue: "lantern_post" },
                              setUnix: { integerValue: "1700000002" },
                              flipped: { booleanValue: true } } } },
    ] } },

    // The ground the grove stands on. It shipped in save schema v17 and reached neither
    // this document nor the rules for a whole version, so land bought with credits stayed
    // on the phone that bought it — invisible until account switching became the first
    // feature that ever reads the cloud copy back over a working local save, at which
    // point a player's grove came back as the free starter square. It is here now so that
    // "the rules accept what the client actually writes" is checked rather than assumed.
    groveLandOwned: { arrayValue: { values: [
      { stringValue: "r_north" }, { stringValue: "r_east" },
    ] } },

    // How far the Infinite lane has ever been held, per level. It is here for the reason
    // every block above is — the mapper sends it, so an unlisted field would refuse the
    // whole write (invariant 12a) — and for one more: it is the only figure on a public
    // card the server cannot recompute, so the *shape* it arrives in is the only thing
    // standing between an honest board and a nonsense one. The high row is what the card's
    // `wave` has to come back as.
    endlessBest: { arrayValue: { values: [
      { mapValue: { fields: { level: { stringValue: "s02_endlesswatch" },
                              wave: { integerValue: "23" } } } },
      { mapValue: { fields: { level: { stringValue: "s02_lower" },
                              wave: { integerValue: "4" } } } },
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

// The grove's arrangement is the longest client-controlled list in the document, and the
// only defence against a device making its own save expensive to read is the bound in the
// rules. Checked rather than assumed, because a `size()` clause that was written but never
// released looks exactly like one that works.
const bloated = await fetch(`${FS}/players/${uid}?updateMask.fieldPaths=homesteadPlaced`, {
  method: "PATCH", headers: json, body: JSON.stringify({ fields: { homesteadPlaced: {
    arrayValue: { values: Array.from({ length: 1200 }, (_, i) => (
      { mapValue: { fields: { slot: { stringValue: "s" + i }, piece: { stringValue: "oak" },
                              setUnix: { integerValue: "1700000000" } } } })) } } } }) });
check(bloated.status === 403, "an oversized grove arrangement is refused", `got ${bloated.status}`);

// Land is a set of regions rather than of tiles precisely so it stays small (invariant
// 16e), and the cap is what keeps it that way. Same reasoning as the arrangement above: a
// size() clause that was written but never released looks exactly like one that works.
const tooMuchLand = await fetch(`${FS}/players/${uid}?updateMask.fieldPaths=groveLandOwned`, {
  method: "PATCH", headers: json, body: JSON.stringify({ fields: { groveLandOwned: {
    arrayValue: { values: Array.from({ length: 200 }, (_, i) => ({ stringValue: "r" + i })) } } } }) });
check(tooMuchLand.status === 403, "an oversized land set is refused", `got ${tooMuchLand.status}`);

// The point of keying the ledger by level id: one glade can be written on its own
// rather than re-uploading a ledger that may run to thousands of entries.
const partial = await fetch(
  `${FS}/players/${uid}?updateMask.fieldPaths=${encodeURIComponent(`levels.${glades[1].id}`)}`, {
    method: "PATCH", headers: json,
    body: JSON.stringify({ fields: { levels: { mapValue: { fields: {
      [glades[1].id]: { mapValue: { fields: {
        stars: { integerValue: "2" }, bestMoves: { integerValue: "30" },
        clears: { integerValue: "1" }, firstClearedUnix: { integerValue: "1700000100" },
        lastPlayedUnix: { integerValue: "1700000100" },
      } } },
    } } } } }) });
check(partial.ok, "one glade can be written on its own", partial.ok ? "" : (await partial.text()).slice(0, 200));

const afterPartial = await (await fetch(`${FS}/players/${uid}`, { headers: bearer })).json();
const ledger = afterPartial?.fields?.levels?.mapValue?.fields ?? {};
check(!!ledger[glades[0].id] && !!ledger[glades[1].id],
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

/**
 * What night one of the streak ladder pays, read from the live config.
 *
 * The same lesson as the golden bands above, learned a second time on a second number: the
 * three cases below hard-coded 150 and went red the day the ladder was retuned to 500. A
 * rung is content — it is published to `config/progression` by the seeder and the server
 * grants from its own copy — so the only figure this suite is entitled to assert is
 * whatever it can read back. Hard-coding the new number would simply set the same trap for
 * whoever retunes it next.
 */
const firstNightCredits = (async () => {
  const body = await publishedConfig.clone().json().catch(() => null);
  const rungs = body?.fields?.streak?.mapValue?.fields?.rungs?.arrayValue?.values ?? [];
  const first = rungs[0]?.mapValue?.fields;

  const kind = first?.kind?.stringValue;
  const amount = Number(first?.amount?.integerValue ?? first?.amount?.doubleValue ?? 0);

  if (kind !== "credits" || !(amount > 0)) {
    // Never silently, for the golden bands' reason. If night one stops paying credits the
    // three cases below are no longer about anything and should be rewritten, not skipped.
    check(false, "night one of the published ladder pays credits",
          `read kind=${kind} amount=${amount}`);
    return 0;
  }

  return amount;
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

// The heart containers' refund path, and the only part of it a client ever sees. A
// container is an entitlement the *client* holds (invariant 18d), so the one thing this
// server has to say about one is that it was refunded — and it says it on every wallet
// reply. The field is checked for its shape rather than its contents: a brand-new account
// has refunded nothing, and an absent array would read the same way while quietly meaning
// a deployment that cannot revoke anything at all. Invariant 12a's lesson, on the wire that
// carries money back out.
check(Array.isArray(creditsOf(wallet.body)?.containersRevoked),
      "the wallet reply carries the refunded heart containers",
      JSON.stringify(creditsOf(wallet.body)));

check(creditsOf(wallet.body)?.containersRevoked?.length === 0,
      "and a new account has had none taken back");

// The bonus wheel's position, and this is the one part of that feature only a live run can
// prove. The client reads the *presence* of these two fields as "this deployment understands
// the wheel" and draws no wheel without them — which is what makes shipping the app ahead of
// the functions cost a feature nobody has seen rather than a payout nobody honours (invariant
// 25). A deployment that had lost them would look identical from every offline check and from
// a console: players would simply never see a wheel, for ever, and nothing would say why.
const wheelRow = creditsOf(wallet.body);
check(typeof wheelRow?.wheelSpins === "number" && typeof wheelRow?.wheelDay === "number",
      "the wallet reply carries the bonus wheel's position",
      JSON.stringify(wheelRow));

// Today and zero, not absent. A brand-new account has spun nothing, and answering with
// nothing would make the signal above a lie in exactly the case it matters most.
check(wheelRow?.wheelSpins === 0, "and a new account is on its first spin",
      `got ${wheelRow?.wheelSpins}`);
check(wheelRow?.wheelDay === Math.floor(Date.now() / 1000 / 86400),
      "stamped with today, so the client seeds from the same day the server will grant on",
      `got ${wheelRow?.wheelDay}`);

// Repeated on every currency row, so a client may read it off whichever one it happens to
// walk first — the same shape `containersRevoked` rides on.
check(new Set((wallet.body?.result?.wallets ?? []).map((w) => `${w.wheelDay}:${w.wheelSpins}`)).size === 1,
      "and every currency row agrees about it");

// The save above holds the two glades picked off the published catalog, one cleared at
// three stars and one at two. The server derives what they are worth from the ledger
// itself — nothing in the request said anything about currency.
//
// The per-star figures are read from the published table rather than written here. They
// were hard-coded as 20 + 10 per star, the chapter was later retuned to 50 + 25, and this
// case then failed with a number that was entirely correct — which is the same trap the
// golden bands above already document, one field over: a money suite that goes red for a
// tuning change is a money suite people learn to ignore. Deriving them means a retune
// moves the expectation with it and only a real disagreement fails.
//
// Read per glade rather than once, because the two need not share a chapter: `glades` takes
// whatever the published catalog names, and a chapter with one level in it is legal.
const rewardsFor = await (async () => {
  const body = await publishedConfig.clone().json().catch(() => null);

  // Published by buildChapterRules as a map keyed by chapter id, not as the array the
  // content file authors. resolveRule has already folded the defaults in, so an override
  // that names only some fields still arrives complete.
  const overrides = body?.fields?.chapterRewards?.mapValue?.fields ?? {};
  const base = body?.fields?.rewards?.mapValue?.fields;

  return (chapter) => {
    // No override published for this chapter: the base table governs.
    const source = overrides?.[chapter]?.mapValue?.fields ?? base;

    return {
      firstClear: Number(source?.creditsFirstClear?.integerValue ?? NaN),
      perStar: Number(source?.creditsPerStar?.integerValue ?? NaN),
    };
  };
})();

const gladeRewards = glades.map((g) => rewardsFor(g.chapter));

check(gladeRewards.every((r) => Number.isFinite(r.firstClear) && Number.isFinite(r.perStar)),
      "the published table names what each of those glades pays",
      `got ${JSON.stringify(gladeRewards)}`);

// Three stars and two stars, which is what the save above holds.
const BASE_CREDITS = [
  gladeRewards[0].firstClear + gladeRewards[0].perStar * 3,
  gladeRewards[1].firstClear + gladeRewards[1].perStar * 2,
];
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
const rungOne = await firstNightCredits;
const firstNight = await call("claimAwards", { awards: [night(today, 1, "credits")] });
check(creditsOf(firstNight.body)?.grantedBaseline === creditsBefore + rungOne,
      "the first night pays the ladder's figure, not the client's",
      `credits ${creditsBefore} -> ${creditsOf(firstNight.body)?.grantedBaseline}`);

// The derived id is what makes this safe to resubmit, which the client does on every sync
// until the server confirms it.
const sameNight = await call("claimAwards", { awards: [night(today, 1, "credits")] });
check(creditsOf(sameNight.body)?.grantedBaseline === creditsBefore + rungOne,
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
check(creditsOf(preFarmed.body)?.grantedBaseline === creditsBefore + rungOne,
      "a night dated a week ahead pays nothing",
      `credits -> ${creditsOf(preFarmed.body)?.grantedBaseline}`);
check(rejectedBy(preFarmed.body).includes(`streak:${today + 7}:8:credits`),
      "and is refused rather than left pending");

// ------------------------------------------------------- the chest nights
//
// A streak night can pay a chest, which is the one rung shape the unit tests cannot see
// end to end: the ladder lives in `config/progression` and the tier it names lives in the
// *tasks* block of the same document, published by the same run of the seeder. A rung
// naming a tier that block does not define leaves every claim against it **unconfirmed**
// for ever (invariant 13a) — the safe half, and it pays nobody.
//
// What cannot be exercised here is a chest night actually being *paid*. A fresh account's
// floor is seeded to yesterday, so the only night it may claim today is night one, and no
// ladder worth shipping puts a chest there. That the roll matches is pinned by the shared
// vectors on both implementations instead.
const publishedStreak = (async () => {
  const body = await publishedConfig.clone().json().catch(() => null);
  const rungs = body?.fields?.streak?.mapValue?.fields?.rungs?.arrayValue?.values ?? [];
  const tiers = body?.fields?.tasks?.mapValue?.fields?.tiers?.arrayValue?.values ?? [];

  return {
    rungs: rungs.map((r) => r?.mapValue?.fields ?? {}),
    tierIds: tiers.map((t) => t?.mapValue?.fields?.id?.stringValue).filter(Boolean),
    shieldGems: Number(body?.fields?.streak?.mapValue?.fields?.shieldGems?.integerValue ?? 0),
    shieldDays: Number(body?.fields?.streak?.mapValue?.fields?.shieldDays?.integerValue ?? 0),
  };
})();

const ladder = await publishedStreak;
const chestNights = ladder.rungs
  .map((r, i) => ({ night: i + 1, tier: r?.tier?.stringValue }))
  .filter((r) => r.tier);

check(chestNights.length > 0,
      "the published ladder pays a chest on at least one night",
      `${ladder.rungs.length} rung(s), none naming a tier`);

check(chestNights.every((r) => ladder.tierIds.includes(r.tier)),
      "and every tier it names is one the published tasks block defines",
      `named ${chestNights.map((r) => r.tier).join(",")} against ${ladder.tierIds.join(",")}`);

// Hearts and boosts reach this ladder through a chest now, and are refused by name at
// authoring. A published rung still naming one is a seeder run from an older tree.
check(!ladder.rungs.some((r) => ["hearts", "heart_boost"].includes(r?.kind?.stringValue)),
      "and no rung names a kind the streak retired");

if (chestNights.length > 0) {
  const chest = chestNights[0];
  const early = await call("claimAwards", { awards: [night(today, chest.night, "credits")] });

  check(rejectedBy(early.body).includes(`streak:${today}:${chest.night}:credits`),
        `a chest night (${chest.night}) is refused on a fresh account's first day`,
        "the floor is seeded to yesterday, so only night one is reachable today");
}

// The shield is an ordinary gem spend that stores one date in the save: it grants no
// currency and gates no payout, so nothing about it is adjudicated. Its two numbers are
// published for completeness, and the only thing worth checking is that they arrived —
// a support question about "why did my streak break" is answerable from them.
check(ladder.shieldGems > 0 && ladder.shieldDays > 0,
      "the shield's price and window reached config/progression",
      `${ladder.shieldGems} gems for ${ladder.shieldDays} day(s)`);

// ---------------------------------------------------------------- task chests
console.log("task chests");

// The published slate, read live for the reason the streak's ladder is: the suite signs in
// as a fresh account and hard-codes nothing the catalog decides — and a task chest cannot
// be read as a flat amount, so it is re-rolled with the server's own compiled roller.
//: One decoder for the published document, used by the tasks block and by the season's.
//: Lifted out of the tasks IIFE when the season came to need it: two copies of a Firestore
//: value decoder is two places one of them stops understanding a type.
const decodeFirestore = (v) => {
  if (v.integerValue !== undefined) return Number(v.integerValue);
  if (v.doubleValue !== undefined) return Number(v.doubleValue);
  if (v.stringValue !== undefined) return v.stringValue;
  if (v.booleanValue !== undefined) return v.booleanValue;
  if (v.arrayValue) return (v.arrayValue.values ?? []).map(decodeFirestore);
  if (v.mapValue) {
    return Object.fromEntries(
      Object.entries(v.mapValue.fields ?? {}).map(([k, x]) => [k, decodeFirestore(x)]));
  }
  return null;
};

//: The published seasons, read live for the reason everything else here is: the suite hard-
//: codes nothing the catalog decides, and a repeating season's ids are not in the catalog at
//: all - they are derived from its window.
const publishedSeasons = await (async () => {
  const body = await publishedConfig.clone().json().catch(() => null);
  const values = body?.fields?.events?.arrayValue?.values;
  return Array.isArray(values) ? values.map(decodeFirestore) : [];
})();

const taskConfig = await (async () => {
  const body = await publishedConfig.clone().json().catch(() => null);
  const fields = body?.fields?.tasks?.mapValue?.fields;
  if (!fields) { check(false, "config/progression carries a tasks block"); return null; }

  const decode = (v) => {
    if (v.integerValue !== undefined) return Number(v.integerValue);
    if (v.doubleValue !== undefined) return Number(v.doubleValue);
    if (v.stringValue !== undefined) return v.stringValue;
    if (v.booleanValue !== undefined) return v.booleanValue;
    if (v.arrayValue) return (v.arrayValue.values ?? []).map(decode);
    if (v.mapValue) return Object.fromEntries(Object.entries(v.mapValue.fields ?? {}).map(([k, x]) => [k, decode(x)]));
    return null;
  };
  return decode({ mapValue: { fields } });
})();

if (taskConfig) {
  const { pathToFileURL } = await import("node:url");
  const { dirname, join } = await import("node:path");
  const { fileURLToPath } = await import("node:url");
  const here = dirname(fileURLToPath(import.meta.url));
  const { rollTaskChest, dealt, weekOfDay } = await import(
    pathToFileURL(join(here, "..", "functions", "lib", "tasks.js")).href);

  const dailyDealt = dealt(taskConfig, "daily", today);
  const first = dailyDealt[0];
  const tier = taskConfig.tiers.find((t) => t.id === first.tier);
  const expected = rollTaskChest(tier.chest, uid, "daily", today, first.id)
    .filter((d) => d.kind === "credits").reduce((sum, d) => sum + d.amount, 0);

  const taskClaim = (period, key, id, currency) => ({
    id: `task:${period}:${key}:${id}:${currency}`, claimedAmount: 1, unix: 1700000004, reason: "task_chest",
  });

  const before = creditsOf(preFarmed.body)?.grantedBaseline ?? 0;
  const paidTask = await call("claimAwards", { awards: [taskClaim("daily", today, first.id, "credits")] });
  check(creditsOf(paidTask.body)?.grantedBaseline === before + expected,
        "a task chest pays the server's own roll of it, not the client's figure",
        `credits ${before} -> ${creditsOf(paidTask.body)?.grantedBaseline}, expected +${expected}`);

  const again = await call("claimAwards", { awards: [taskClaim("daily", today, first.id, "credits")] });
  check(creditsOf(again.body)?.grantedBaseline === before + expected,
        "resubmitting that chest does not pay twice");

  const bogus = await call("claimAwards", { awards: [taskClaim("daily", today, "no_such_task", "credits")] });
  check(!rejectedBy(bogus.body).includes(`task:daily:${today}:no_such_task:credits`) &&
        creditsOf(bogus.body)?.grantedBaseline === before + expected,
        "a task the slate has never held is left unconfirmed and pays nothing");

  // Three is the allowance; a fourth distinct task in one day is refused.
  const slate = taskConfig.daily.map((t) => t.id).filter((id) => id !== first.id);
  const extra = slate.slice(0, taskConfig.activePerPeriod);
  const flood = await call("claimAwards", {
    awards: extra.map((id) => taskClaim("daily", today, id, "credits")),
  });
  const refused = rejectedBy(flood.body);
  check(refused.length >= 1 && refused.includes(`task:daily:${today}:${extra[extra.length - 1]}:credits`),
        "a day pays no more chests than the slate deals",
        `rejected ${JSON.stringify(refused)}`);

  const stale = await call("claimAwards", { awards: [taskClaim("daily", today - 400, first.id, "credits")] });
  check(rejectedBy(stale.body).includes(`task:daily:${today - 400}:${first.id}:credits`),
        "a chest dated outside the window is refused rather than left pending");

  const future = await call("claimAwards", {
    awards: [taskClaim("weekly", weekOfDay(today) + 5, taskConfig.weekly[0].id, "gems")],
  });
  check(rejectedBy(future.body).includes(`task:weekly:${weekOfDay(today) + 5}:${taskConfig.weekly[0].id}:gems`),
        "a chest dated weeks ahead is refused");

  // ---------------------------------------------------------------- the season
  // **The one thing that proves the deployment understands a repeating season.** The ladder
  // is derived from the clock on both sides now (`SeasonCycle`), so the server is asked for a
  // season id that appears in no published list — and every gate that can run offline reads a
  // mirror rather than the deployed function. A deployment that has not been redeployed
  // answers `unknown` here, pays nothing, and looks exactly like a network hiccup.
  const { usableSeason, seasonCycleIndex, rollMarkChest } = await import(
    pathToFileURL(join(here, "..", "functions", "lib", "season.js")).href);

  const cycleEntry = publishedSeasons.find((e) => e && e.repeats);

  if (!cycleEntry) {
    check(false, "the published season repeats",
          "config/progression holds no repeating season; re-seed before trusting this");
  } else {
    const nowUnix = Math.floor(Date.now() / 1000);
    const period = cycleEntry.endUnix - cycleEntry.startUnix;
    const index = Math.max(0, Math.floor((nowUnix - cycleEntry.startUnix) / period));
    const seasonId = `${cycleEntry.id}_${String(index).padStart(4, "0")}`;

    check(seasonCycleIndex(cycleEntry.id, seasonId) === index,
          "the live cycle's id round-trips", seasonId);

    const live = usableSeason({ events: publishedSeasons }, seasonId, nowUnix);
    check(!!live && live.milestones.length > 0,
          "the deployment can price a cycle that appears in no published list", seasonId);

    const rung = live.milestones[0];
    const markClaim = (id, track, goal, currency) => ({
      id: `mark:${id}:${track}:${goal}:${currency}`,
      claimedAmount: 1, unix: 1700000005, reason: "season_rung",
    });

    const tier = taskConfig.tiers.find((t) => t.id === rung.tier);
    const owed = rollMarkChest(tier.chest, uid, seasonId, "free", rung.goal)
      .filter((d) => d.kind === "credits").reduce((sum, d) => sum + d.amount, 0);

    const was = creditsOf(future.body)?.grantedBaseline ?? 0;
    const paidRung = await call("claimAwards",
                                { awards: [markClaim(seasonId, "free", rung.goal, "credits")] });

    check(creditsOf(paidRung.body)?.grantedBaseline === was + owed,
          "a season rung pays the server's own roll of its chest",
          `credits ${was} -> ${creditsOf(paidRung.body)?.grantedBaseline}, expected +${owed}`);

    const twice = await call("claimAwards",
                             { awards: [markClaim(seasonId, "free", rung.goal, "credits")] });
    check(creditsOf(twice.body)?.grantedBaseline === was + owed,
          "and the grant log pays it exactly once, which is the whole bound");

    // The paid column without the purchase is refused for good: no content push will ever
    // make an unpaid account paid (invariant 13a, the other direction).
    const unpaid = await call("claimAwards",
                              { awards: [markClaim(seasonId, "pass", rung.goal, "credits")] });
    check(rejectedBy(unpaid.body).includes(`mark:${seasonId}:pass:${rung.goal}:credits`),
          "the pass column without the pass is refused rather than left pending");

    // **The bound.** A cycle that has not opened does not exist, so a forged far-future id
    // pays nothing — and is left *unconfirmed* rather than refused, because it stops being
    // early on its own.
    const forged = `${cycleEntry.id}_9999`;
    const ahead = await call("claimAwards",
                             { awards: [markClaim(forged, "free", rung.goal, "credits")] });
    check(creditsOf(ahead.body)?.grantedBaseline === was + owed &&
          !rejectedBy(ahead.body).includes(`mark:${forged}:free:${rung.goal}:credits`),
          "a cycle that has not opened pays nothing and is left unconfirmed", forged);

    // And the stem alone names no season at all.
    const stem = await call("claimAwards",
                            { awards: [markClaim(cycleEntry.id, "free", rung.goal, "credits")] });
    check(creditsOf(stem.body)?.grantedBaseline === was + owed,
          "the stem alone names no season, so it pays nothing");
  }

}


// ------------------------------------------------------------------ the boards
// The one part of this system where a forged save buys something a *stranger* sees, so it
// is the part worth attacking from the client's side. Everything below runs against the
// deployed rules and the deployed functions, because neither can be checked anywhere else.

// A grove nobody could possibly have afforded: every priced piece, every region, and a
// companion the keeper ladder has not reached. This account has cleared one glade.
const groveConfig = await (await fetch(`${FS}/config/grove`, { headers: bearer })).json();
check(!groveConfig.error, "config/grove is readable by a signed-in player",
      groveConfig.error?.status ?? "");

const pieceIds = Object.keys(groveConfig?.fields?.pieces?.mapValue?.fields ?? {});
const regionIds = Object.keys(groveConfig?.fields?.regions?.mapValue?.fields ?? {});
const companionIds = Object.keys(groveConfig?.fields?.companions?.mapValue?.fields ?? {});

check(pieceIds.length > 0 && regionIds.length > 0 && companionIds.length > 0,
      "the grove catalog has been seeded",
      `${pieceIds.length} piece(s), ${regionIds.length} region(s), ${companionIds.length} companion(s)`);

const priceTotal =
  Object.values(groveConfig?.fields?.pieces?.mapValue?.fields ?? {})
        .reduce((sum, v) => sum + Number(v.integerValue ?? 0), 0) +
  Object.values(groveConfig?.fields?.regions?.mapValue?.fields ?? {})
        .reduce((sum, v) => sum + Number(v.integerValue ?? 0), 0);

const list = (ids) => ({ arrayValue: { values: ids.map((id) => ({ stringValue: id })) } });

// The name is written with a right-to-left override in it. Left alone it would re-order
// every row drawn after it on the board — the defect a length cap and a word filter both
// miss, and the reason the server sanitises rather than trusting what it is sent.
// The keeper name this run uses.
//
// Unique per run, and it has to be: a reservation is permanent and global, so the first run of
// a hard-coded name claims it for ever and every run afterwards is told — correctly — that it
// is taken. That is the same trap the earned-credits case fell into, one collection over. It
// still carries the bidi override, because stripping that is the assertion this name exists for.
const RUN_TAG = Math.random().toString(36).slice(2, 7);
const STORED_NAME = `Fern‮${RUN_TAG}`;
const PUBLIC_NAME = `Fern${RUN_TAG}`;
const NAME_KEY = PUBLIC_NAME.toLowerCase();

const forgedGrove = await fetch(
  `${FS}/players/${uid}?updateMask.fieldPaths=homesteadOwned` +
  `&updateMask.fieldPaths=groveLandOwned&updateMask.fieldPaths=companionsOwned` +
  `&updateMask.fieldPaths=wallet`,
  {
    method: "PATCH", headers: json,
    body: JSON.stringify({ fields: {
      homesteadOwned: list(pieceIds),
      groveLandOwned: list(regionIds),
      companionsOwned: list(companionIds),
      wallet: { mapValue: { fields: {
        heartsProduced: { integerValue: "9" }, heartsSpent: { integerValue: "5" },
        heartsDueUnix: { integerValue: "1700028800" }, hearts: { integerValue: "4" },
        heartsNextRefillUnix: { integerValue: "1700028800" },
        heartBoostUntilUnix: { integerValue: "0" },
        displayName: { stringValue: STORED_NAME },
        avatarId: { stringValue: "monarch" },
      } } },
    } }),
  }
);
check(forgedGrove.ok, "a client may write its own grove sets", String(forgedGrove.status));

const published = await call("publishGrove", {});
check(published.status === 200, "publishGrove accepts an honest call", String(published.status));

// The proof the client holds the reply to: which save the card was built from. This save
// was written with revision 1 above, so anything less says the server read a document that
// is not the one this client pushed — the exact failure the field exists to expose.
check(Number(published.body?.result?.revision) >= 1,
      "publishGrove reports the save revision it built the card from",
      JSON.stringify(published.body?.result?.revision));

const card = await (await fetch(`${FS}/groves/${uid}`, { headers: bearer })).json();
const cardScore = Number(card?.fields?.score?.integerValue ?? -1);

check(cardScore >= 0, "a card was published", `score ${cardScore}`);

// The whole point. The save claims the entire catalog; the server pays out only what this
// account could ever have afforded, which after one cleared glade is the seed plus a few
// dozen credits. See invariant 19a.
check(cardScore < priceTotal,
      "a forged grove is clamped to what the account could afford",
      `${cardScore} against a catalog worth ${priceTotal}`);

// And the ceiling includes currency the server *granted*, not only currency the ledger
// derives. This is the assertion that catches the clamp being too tight, which is the
// failure mode that looks exactly like the clamp working: the first live run read the
// wallet's reply field name instead of its stored one, got zero, and would have ranked
// every player who bought coins with real money at the bottom of the board. A unit test
// cannot see it — the vectors take the ceiling as a parameter.
const grantedCredits = Number(creditsOf(wallet.body)?.grantedBaseline ?? 0);
check(cardScore >= grantedCredits,
      "the ceiling counts currency the server granted, not only what play derived",
      `score ${cardScore} against ${grantedCredits} granted`);

check(card?.fields?.name?.stringValue === PUBLIC_NAME,
      "the published name has its bidi override stripped",
      JSON.stringify(card?.fields?.name?.stringValue));

// The endless board's whole input. `bestWave` takes the best of every row, so this is the 23
// rather than the 4 — and a unit test cannot prove the field survived Firestore's own typing,
// which is what this run is for: an `integerValue` comes back as a *string* over REST, and a
// reader that compared it as a number would have written a card with no wave on it at all.
check(Number(card?.fields?.wave?.integerValue ?? 0) === 23,
      "the card carries the best wave of every endless row",
      JSON.stringify(card?.fields?.wave));

check(card?.fields?.league === undefined,
      "and no longer carries a league", JSON.stringify(card?.fields?.league));

// ------------------------------------------------------- what a public profile reads
//
// Two fields the card grew for `PublicProfileScreen`, and the whole reason they are checked
// live rather than only in the shared vectors: both are built from save keys nothing else on
// the card reads (`wardLoadout`, `wardsOwned`, `wardStars`), against a `config/grove` the
// seeder has to have published a turret roster into. A vector builds its own input; only a
// real run sees the shape Firestore actually wrote.

const cardCompanions = (card?.fields?.companions?.arrayValue?.values ?? [])
  .map((v) => v.stringValue);

// The save above claimed the entire companion catalog and this account has cleared one glade,
// so the gated ones are dropped outright — `heldCompanions` asks the gate before anything
// else, exactly as the score does. The set drawn and the set counted are one walk, which is
// what stops a visitor seeing a portrait the number beside it was never told about.
check(cardCompanions.length < companionIds.length,
      "a forged companion set is published gated, not whole",
      `${cardCompanions.length} of ${companionIds.length}`);

const wardRoster = groveConfig?.fields?.wards?.mapValue?.fields ?? {};
const wardIds = Object.keys(wardRoster);

check(wardIds.length > 0,
      "config/grove carries the turret roster (re-seed if this is red)",
      `${wardIds.length} turret(s)`);

if (wardIds.length > 0) {
  // The free turret — level 0 and no price — which every account stands and which appears in
  // nobody's `wardsOwned`. Publishing a line that dropped it would drop the one seat every
  // player in the game has.
  const freeWard = wardIds.find(
    (id) => wardRoster[id]?.mapValue?.fields?.free?.booleanValue === true);

  // And one behind a gate this account cannot have reached, held on a seat it did not buy.
  const gatedWard = wardIds.find(
    (id) => Number(wardRoster[id]?.mapValue?.fields?.level?.integerValue ?? 0) >= 20);

  check(!!freeWard && !!gatedWard,
        "the roster has a free turret and a gated one to test against",
        `${freeWard} / ${gatedWard}`);

  const lineWrite = await fetch(
    `${FS}/players/${uid}?updateMask.fieldPaths=wardLoadout` +
    `&updateMask.fieldPaths=wardsOwned&updateMask.fieldPaths=wardStars`,
    {
      method: "PATCH", headers: json,
      body: JSON.stringify({ fields: {
        wardLoadout: { arrayValue: { values: [
          { mapValue: { fields: { colour: { stringValue: "r" },
                                  ward: { stringValue: freeWard } } } },
          { mapValue: { fields: { colour: { stringValue: "g" },
                                  ward: { stringValue: gatedWard } } } },
        ] } },
        // Held on green, so the only thing that can drop it is the keeper gate.
        wardsOwned: { arrayValue: { values: [{ stringValue: `${gatedWard}:g` }] } },
        wardStars: { arrayValue: { values: [
          { mapValue: { fields: { ward: { stringValue: `${gatedWard}:g` },
                                  stars: { integerValue: "4" } } } },
        ] } },
      } }),
    }
  );
  check(lineWrite.ok, "a client may write its own loadout", String(lineWrite.status));

  const relined = await call("publishGrove", {});
  check(relined.status === 200, "and publishGrove takes it", String(relined.status));

  const lined = await (await fetch(`${FS}/groves/${uid}`, { headers: bearer })).json();
  const seats = (lined?.fields?.line?.arrayValue?.values ?? [])
    .map((v) => v.mapValue?.fields ?? {});

  check(seats.length === 1, "a line is published with only the seats the server can vouch for",
        JSON.stringify(seats));

  check(seats[0]?.c?.stringValue === "r" && seats[0]?.w?.stringValue === freeWard,
        "the free turret needs no holding, so its seat stands", JSON.stringify(seats[0]));

  // The one forgery about a line a visitor could catch as a lie: a level-1 keeper standing a
  // turret whose rung opens at twenty. Dropped outright rather than drawn, and the visiting
  // client fills the gap with its own roster's starter.
  check(!seats.some((s) => s?.w?.stringValue === gatedWard),
        "a turret above this keeper's level is not published", JSON.stringify(seats));
}

// Placed after the forged-grove assertions rather than before them, and that is not
// housekeeping: these cases rewrite the player's grove sets to isolate the arithmetic, so
// running them first left every assertion above reading a grove this section had emptied.
// ---------------------------------------------------------------- stock, live
//
// WHY THIS IS HERE AND NOT ONLY IN THE VECTORS. Save v20 made priced decor a count of
// copies, and two things about that can only be seen against the deployed project. The
// first has teeth: `hasOnly` in firestore.rules is an allow-list over the whole document,
// so a client writing a key the released rules do not name loses *every* save write rather
// than that one field — the failure invariant 12a is about, which is invisible until
// something replaces the local save. The second is that `groveWorth` has to divide by the
// bundle it reads out of `config/grove`, and a seeder that published no `bundles` block
// would score every bundled piece at ten times what was paid for it, silently.
//
// The account is freshly seeded, so the cheapest bundled piece is chosen deliberately:
// everything below has to stay under the affordability ceiling or the clamp would hide
// exactly the arithmetic being proved.
const bundleFields = groveConfig?.fields?.bundles?.mapValue?.fields ?? {};
const bundledIds = Object.keys(bundleFields);

check(bundledIds.length > 0,
      "config/grove carries the bundle sizes",
      `${bundledIds.length} bundled piece(s)`);

const priceOf = (id) =>
  Number(groveConfig?.fields?.pieces?.mapValue?.fields?.[id]?.integerValue ?? 0);
const bundleOf = (id) => Number(bundleFields[id]?.integerValue ?? 1);

const cheapest = bundledIds
  .filter((id) => priceOf(id) > 0)
  .sort((a, b) => priceOf(a) - priceOf(b))[0];

const stockRows = (id, copies) => ({
  arrayValue: { values: [{ mapValue: { fields: {
    id: { stringValue: id },
    copies: { integerValue: String(copies) },
  } } }] },
});

const writeStock = (body) => fetch(
  `${FS}/players/${uid}?updateMask.fieldPaths=homesteadStock` +
  `&updateMask.fieldPaths=homesteadOwned&updateMask.fieldPaths=groveLandOwned` +
  `&updateMask.fieldPaths=companionsOwned`,
  { method: "PATCH", headers: json, body: JSON.stringify({ fields: body }) }
);

const scoreNow = async () => {
  const r = await call("publishGrove", {});
  if (r.status !== 200) return -1;
  const c = await (await fetch(`${FS}/groves/${uid}`, { headers: bearer })).json();
  return Number(c?.fields?.score?.integerValue ?? -1);
};

if (cheapest) {
  const bundle = bundleOf(cheapest);
  const price = priceOf(cheapest);

  // The write itself is the assertion. A rules deploy that forgot this key answers 403 and
  // the client silently stops syncing anything at all.
  const oneBundle = await writeStock({
    homesteadStock: stockRows(cheapest, bundle),
    homesteadOwned: { arrayValue: { values: [] } },
    groveLandOwned: { arrayValue: { values: [] } },
    companionsOwned: { arrayValue: { values: [] } },
  });
  check(oneBundle.ok, "the released rules accept a homesteadStock write", String(oneBundle.status));

  const stockScore = await scoreNow();
  check(stockScore === price,
        "one bundle of copies is worth the bundle, not ten of it",
        `${stockScore} for ${bundle} copies of ${cheapest} at ${price}`);

  // Ten bundles, still inside what a seeded account can afford, so the multiplication is
  // read rather than clamped away.
  const tenBundles = await writeStock({
    homesteadStock: stockRows(cheapest, bundle * 10),
    homesteadOwned: { arrayValue: { values: [] } },
    groveLandOwned: { arrayValue: { values: [] } },
    companionsOwned: { arrayValue: { values: [] } },
  });
  check(tenBundles.ok, "and a larger stock write too", String(tenBundles.status));

  const tenScore = await scoreNow();
  check(tenScore === price * 10,
        "copies multiply, so buying more is worth more",
        `${tenScore} against ${price * 10}`);

  // The v19 shape has to keep scoring the same thing, because a device that has not updated
  // still writes it — that is what the derived mirror on the client is for, and it is the
  // whole reason this deploy could go out before the client.
  const legacy = await writeStock({
    homesteadStock: { arrayValue: { values: [] } },
    homesteadOwned: { arrayValue: { values: [{ stringValue: cheapest }] } },
    groveLandOwned: { arrayValue: { values: [] } },
    companionsOwned: { arrayValue: { values: [] } },
  });
  check(legacy.ok, "a v19 client's owned set is still accepted", String(legacy.status));

  const legacyScore = await scoreNow();
  check(legacyScore === price,
        "a v19 save reads as one bundle, so it scores exactly what it used to",
        `${legacyScore} against ${price}`);

  // **A card always names a home, and it is one this deployment's own catalog knows.**
  //
  // What this is for is a seed the client has outgrown. The home a card draws is derived
  // server-side from `config/grove`'s `dwellings` map, and the visiting client then looks that
  // id up in the catalog it shipped with: a rung renamed in `homestead.json` and not re-seeded
  // publishes an id nothing can resolve, and a `dwellings` map that never made it into the seed
  // at all publishes the empty string. Both draw *no house* on every visitor's screen, and both
  // are invisible to every other gate here — the score is right, the arrangement is right, the
  // write succeeds and the document is well formed.
  //
  // It is asked over the three writes above rather than once, because those are the three
  // shapes a real save arrives in (stock, larger stock, v19 mirror) and the home is derived
  // from the same rows the score is. That is the half worth checking live; whether the *best*
  // rung wins is arithmetic over a fixed catalog, which `functions/test/grove.mjs` pins on both
  // sides of the keeper gate and which this throwaway account cannot reach in any case.
  const homed = await (await fetch(`${FS}/groves/${uid}`, { headers: bearer })).json();
  const drawn = homed?.fields?.dwelling?.stringValue ?? "";
  const rungs = Object.keys(groveConfig?.fields?.dwellings?.mapValue?.fields ?? {});

  check(rungs.length > 0, "the published catalog names a home ladder", String(rungs.length));
  check(drawn.length > 0 && rungs.includes(drawn),
        "a published card names a home the published catalog knows",
        `drew ${JSON.stringify(drawn)} against ${rungs.join(", ")}`);
}


// Server-written, and that is the rule with teeth: everything on a card is derived from a
// document the client controls, so a client that could write here would own the board.
const forgeCard = await fetch(`${FS}/groves/${uid}?updateMask.fieldPaths=score`, {
  method: "PATCH", headers: json,
  body: JSON.stringify({ fields: { score: { integerValue: "999999999" } } }),
});
check(forgeCard.status === 403, "a client cannot write its own card", String(forgeCard.status));

const forgeBoard = await fetch(`${FS}/leaderboards/global?updateMask.fieldPaths=population`, {
  method: "PATCH", headers: json,
  body: JSON.stringify({ fields: { population: { integerValue: "1" } } }),
});
check(forgeBoard.status === 403, "a client cannot write a board", String(forgeBoard.status));

const boardRead = await fetch(`${FS}/leaderboards/global`, { headers: bearer });
check(boardRead.ok, "a signed-in player may read a board", String(boardRead.status));

// Both boards, and read the same way — one document, whole. A board that has never been built
// is a 404 and that is an ordinary first-day state rather than a failure, so what is asserted
// is that the read is *permitted*, which is the half the rules decide.
const endlessRead = await fetch(`${FS}/leaderboards/endless`, { headers: bearer });
check(endlessRead.ok || endlessRead.status === 404,
      "and the endless board is readable on the same rule", String(endlessRead.status));

const forgeEndless = await fetch(`${FS}/leaderboards/endless?updateMask.fieldPaths=population`, {
  method: "PATCH", headers: json, body: JSON.stringify({ fields: { population: { integerValue: "1" } } }) });
check(forgeEndless.status === 403,
      "and no client may write it either", String(forgeEndless.status));

const ranksRead = await fetch(`${FS}/config/groveRanks`, { headers: bearer });
check(ranksRead.ok, "and the published distribution", String(ranksRead.status));

// A second keeper, because visiting is the feature and one account cannot prove it.
const second = await (await fetch(
  `https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=${KEY}`,
  { method: "POST", headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ returnSecureToken: true }) }
)).json();

const visitor = { Authorization: `Bearer ${second.idToken}` };

const visited = await fetch(`${FS}/groves/${uid}`, { headers: visitor });
check(visited.ok, "another keeper may read this grove's card", String(visited.status));

// And still cannot read the save it was built from. This is the whole reason a card is a
// separate document — see invariant 19.
const peekSave = await fetch(`${FS}/players/${uid}`, { headers: visitor });
check(peekSave.status === 403, "and cannot read the save behind it", String(peekSave.status));

// ------------------------------------------------------------- the release gate
console.log("\nrelease");

// Read with no Authorization header at all, which is the point of the case rather than a
// shortcut. A forced update exists because something about an old client no longer works
// against this deployment, and if that something is ever authentication then a gate behind
// `signedIn()` is a gate that cannot close on the builds it was written for.
const releaseAnon = await fetch(`${FS}/config/release`);
check(releaseAnon.ok || releaseAnon.status === 404,
      "config/release is readable with no session at all", String(releaseAnon.status));

// A client that could write this could wall every other player out of the game, which makes
// it the one config document whose write rule protects other people rather than the economy.
const forgeRelease = await fetch(`${FS}/config/release?updateMask.fieldPaths=android`, {
  method: "PATCH", headers: json,
  body: JSON.stringify({ fields: { android: { mapValue: { fields: {
    minimum: { integerValue: "999999" },
    store: { stringValue: "https://example.invalid" },
  } } } } }) });
check(forgeRelease.status === 403, "and no client may write it", String(forgeRelease.status));

// If it has been seeded, the shape has to be one the client can act on. A minimum with no
// store link is a wall with no door: the client refuses it outright (so the wall silently
// never appears) and `seed-release.mjs` refuses to publish it — this is the third place, and
// the only one that sees what is actually in the database.
if (releaseAnon.ok) {
  const published = (await releaseAnon.json())?.fields ?? {};

  for (const platform of ["android", "ios"]) {
    const block = published[platform]?.mapValue?.fields;
    if (!block) {
      check(true, `config/release names no ${platform} block, so nothing is forced there`);
      continue;
    }

    const minimum = Number(block.minimum?.integerValue ?? 0);
    const store = block.store?.stringValue ?? "";

    check(minimum === 0 || store.startsWith("https://"),
          `${platform}'s wall names a store link a device will open`,
          `minimum ${minimum}, store '${store}'`);
  }
}

// -------------------------------------------------------------- keeper names
console.log("\nnames");

// Publishing above should have reserved the name, because that is the path a rename made
// offline takes: `publishGrove` claims whatever the save asks for and nothing else has to
// remember to. The document id is the fold, so the bidi override is not in it.
const reservation = await (await fetch(`${FS}/names/${NAME_KEY}`, { headers: bearer })).json();
check(reservation?.fields?.uid?.stringValue === uid,
      "publishing reserved the keeper's name",
      JSON.stringify(reservation?.fields?.uid?.stringValue));

// A `get` is granted and a `list` is not. Without that split the reservations are a directory
// of every name in the game, downloadable by anybody with an account.
const walkNames = await fetch(`${FS}/names?pageSize=1`, { headers: bearer });
check(walkNames.status === 403, "a client cannot walk the reservations", String(walkNames.status));

// The whole security position in one request: uniqueness is server-held, so a client that
// could write here could take any name it liked, including one somebody else is using.
const forgeName = await fetch(`${FS}/names/somebodyelse`, {
  method: "PATCH", headers: json,
  body: JSON.stringify({ fields: { uid: { stringValue: uid } } }),
});
check(forgeName.status === 403, "a client cannot write a reservation", String(forgeName.status));

// Re-claiming the name already held writes nothing and is a success, which is what makes a
// retry after a dropped reply free rather than a second write.
const reclaim = await call("claimName", { name: STORED_NAME });
check(reclaim.body?.result?.outcome === "unchanged",
      "re-claiming the name already held is a no-op",
      JSON.stringify(reclaim.body?.result));

// The point of the feature. A second keeper asks for the same name and is refused — and the
// refusal comes from the document id, not from a query that two callers could both pass.
const callAs = async (name, data, token) => {
  const r = await fetch(`${FN}/${name}`, {
    method: "POST",
    headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
    body: JSON.stringify({ data }),
  });
  return { status: r.status, body: await r.json().catch(() => ({})) };
};

// The save has to exist first: a reservation costs a real session rather than an anonymous
// token, which is the only cheap bound on squatting.
const noSave = await callAs("claimName", { name: PUBLIC_NAME }, second.idToken);
check(noSave.status === 400,
      "a name cannot be claimed before a save exists", String(noSave.status));

const secondSave = await fetch(`${FS}/players/${second.localId}`, {
  method: "PATCH",
  headers: { Authorization: `Bearer ${second.idToken}`, "Content-Type": "application/json" },
  body: JSON.stringify({ fields: {
    schemaVersion: { integerValue: "17" }, updatedUnix: { integerValue: "1700000000" },
    levels: { mapValue: { fields: {} } },
  } }),
});
check(secondSave.ok, "the second keeper has a save", String(secondSave.status));

const stolen = await callAs("claimName", { name: `fern ${RUN_TAG}` }, second.idToken);
check(stolen.body?.result?.outcome === "taken",
      "a name another keeper holds is refused",
      JSON.stringify(stolen.body?.result));

// And the fold is what refused it: `fern willow` is not the string the first keeper stored.
// Case, spacing and width are the ways a duplicate actually gets in.
const stolenWide = await callAs(
  "claimName", { name: `FERN.${RUN_TAG.toUpperCase()}` }, second.idToken);
check(stolenWide.body?.result?.outcome === "taken",
      "and so is the same name spelled differently",
      JSON.stringify(stolenWide.body?.result));

// A free name is taken, once, by whoever asks first.
const freeName = "Moss" + Math.random().toString(36).slice(2, 8);
const claimed = await callAs("claimName", { name: freeName }, second.idToken);
check(claimed.body?.result?.outcome === "claimed",
      "a free name is claimed", JSON.stringify(claimed.body?.result));
check(claimed.body?.result?.name === freeName,
      "and the reply says what the account is now called",
      JSON.stringify(claimed.body?.result?.name));

// Renaming again inside the cooldown is refused with a number rather than an error, because
// it is the one refusal a player acts on by waiting.
const tooSoon = await callAs("claimName", { name: freeName + "x" }, second.idToken);
check(tooSoon.body?.result?.outcome === "cooldown",
      "renaming twice inside the cooldown is refused",
      JSON.stringify(tooSoon.body?.result));
check(Number(tooSoon.body?.result?.cooldownSeconds ?? 0) > 0,
      "and says how long is left",
      String(tooSoon.body?.result?.cooldownSeconds));

// Refused, not rejected: the player keeps the name on their own screens. The word list lives
// only on the server, which is why this is the only place the answer can come from.
const filtered = await call("claimName", { name: "ADMIN" });
check(filtered.body?.result?.outcome === "refused",
      "a filtered name is refused", JSON.stringify(filtered.body?.result));
check(filtered.body?.result?.name === PUBLIC_NAME,
      "and the account keeps the name it had",
      JSON.stringify(filtered.body?.result?.name));

// Two visible characters and an empty fold. It used to publish and could never be reserved,
// so two keepers would have stood on one board under one name.
const punctuation = await call("claimName", { name: "!!" });
check(punctuation.body?.result?.outcome === "refused",
      "a name that folds to nothing is refused",
      JSON.stringify(punctuation.body?.result));

// ----------------------------------------------------------------- the word filter, live
//
// The filter's arithmetic is proved offline by functions/test/names.mjs, which runs the
// shipped list against the shipped fold. What only a live run can prove is that the *deployed*
// build is running that list — that `config/names` was seeded, that the loader adopted it
// rather than quietly falling back, and that a claim actually consults it. All three failures
// look identical from a console: a filter that refuses nothing.

const bypasses = [
  ["f4ggot", "leetspeak, which the old filter let straight through"],
  ["fu\u0441k", "a Cyrillic es, which the old filter *deleted* rather than folded"],
  ["fuuuck", "a repeated run"],
  ["\u0445\u0443\u0439", "Russian, which used to fold to the empty string and never be filtered"],
];

for (const [attempt, why] of bypasses) {
  const tried = await callAs("claimName", { name: attempt }, second.idToken);
  check(tried.body?.result?.outcome === "refused", `the deployed filter refuses ${why}`,
        JSON.stringify(tried.body?.result));
}

// The other half, and the one a word list gets wrong far more often. `rape` is a substring of
// `Grapevine` and the shipped filter refused it for a year, in a game about a garden.
const innocent = await callAs("claimName", { name: `Grapevine${RUN_TAG}` }, second.idToken);
check(innocent.body?.result?.outcome !== "refused",
      "and does not refuse an innocent name that merely contains one",
      JSON.stringify(innocent.body?.result));

// ---------------------------------------------------------------------- reporting a name
//
// `nameReports` is server-only in both directions. Not writable, for the obvious reason; not
// *readable*, because a caller who can see the count can binary-search the takedown threshold,
// and one who can read the reporter documents learns who reported them.

const reportRead = await fetch(`${FS}/nameReports/${uid}`, { headers: bearer });
check(reportRead.status === 403 || reportRead.status === 404,
      "a client cannot read a report summary", String(reportRead.status));

const reportWrite = await fetch(`${FS}/nameReports/${uid}`, {
  method: "PATCH", headers: json,
  body: JSON.stringify({ fields: { reports: { integerValue: "99" } } }),
});
check(reportWrite.status === 403, "and cannot write one", String(reportWrite.status));

// The first keeper republishes so there is a card to report. (The opt-out below takes it down
// again, which is why this runs first.)
await call("publishGrove", {});

const reported = await callAs("reportKeeper", { keeperId: uid }, second.idToken);
check(reported.body?.result?.outcome === "reported", "a keeper can report another's name",
      JSON.stringify(reported.body?.result));

// Idempotent on the pair, which is what makes the control safe to tap twice and what makes the
// threshold count *people* rather than taps.
const again = await callAs("reportKeeper", { keeperId: uid }, second.idToken);
check(again.body?.result?.outcome === "duplicate", "and reporting the same name twice is one",
      JSON.stringify(again.body?.result));

// The other subject. A keeper puts a name *and* an arrangement in front of strangers, and the
// two are separate judgements in separate collections — so reporting one must leave the other
// untouched and must not read as a duplicate of it.
const groveReported = await callAs(
  "reportKeeper", { keeperId: uid, subject: "grove" }, second.idToken);
check(groveReported.body?.result?.outcome === "reported",
      "and can report their grovement, which is a separate judgement",
      JSON.stringify(groveReported.body?.result));

const groveAgain = await callAs(
  "reportKeeper", { keeperId: uid, subject: "grove" }, second.idToken);
check(groveAgain.body?.result?.outcome === "duplicate",
      "idempotent on the pair and the subject together",
      JSON.stringify(groveAgain.body?.result));

// `groveReports` is server-only in both directions for `nameReports`' reasons, and the rules
// name it explicitly rather than leaning on the catch-all — a collection nobody wrote a rule
// for is a collection nobody thought about.
const groveReportRead = await fetch(`${FS}/groveReports/${uid}`, { headers: bearer });
check(groveReportRead.status === 403 || groveReportRead.status === 404,
      "a client cannot read a grovement report summary", String(groveReportRead.status));

const groveReportWrite = await fetch(`${FS}/groveReports/${uid}`, {
  method: "PATCH", headers: json,
  body: JSON.stringify({ fields: { reports: { integerValue: "99" } } }),
});
check(groveReportWrite.status === 403, "and cannot write one", String(groveReportWrite.status));

// A subject this deployment has never heard of is refused rather than defaulted: a client one
// drop ahead asking for something the server cannot take down must be told nothing happened,
// not told its report was counted.
const unknownSubject = await callAs(
  "reportKeeper", { keeperId: uid, subject: "avatar" }, second.idToken);
check(unknownSubject.status === 400, "an unknown report subject is refused",
      `${unknownSubject.status} ${JSON.stringify(unknownSubject.body?.error?.status ?? "")}`);

// And an absent subject is the name, which is what every client written before grovements
// could be reported means — the reason this callable kept its id (invariant 1).
const legacySubject = await callAs("reportKeeper", { keeperId: uid }, second.idToken);
check(legacySubject.body?.result?.outcome === "duplicate",
      "an absent subject is still the name, so an older client is answered as before",
      JSON.stringify(legacySubject.body?.result));

// Reporting yourself is answered exactly as a real report is. The client is told nothing it
// could use to probe the moderation state — see NameReportOutcome.
const self = await call("reportKeeper", { keeperId: uid });
check(self.status === 200, "reporting yourself is answered rather than refused",
      String(self.status));

const noKeeper = await call("reportKeeper", {});
check(noKeeper.status === 400, "a report with no keeper is rejected", String(noKeeper.status));

const unpublished = await call("reportKeeper", { keeperId: `nobody-${RUN_TAG}` });
check(unpublished.body?.result?.outcome === "reported",
      "reporting an account with no card says nothing about whether it exists",
      JSON.stringify(unpublished.body?.result));

// Opting out is a withdrawal rather than a preference that takes effect later: a card left
// standing after somebody asked to be hidden is a data-protection failure.
const optOut = await fetch(`${FS}/players/${uid}?updateMask.fieldPaths=settings`, {
  method: "PATCH", headers: json,
  body: JSON.stringify({ fields: { settings: { mapValue: { fields: {
    music: { integerValue: "1" }, sfx: { integerValue: "1" }, haptics: { integerValue: "1" },
    board: { integerValue: "2" }, language: { stringValue: "en" },
  } } } } }),
});
check(optOut.ok, "a client may opt out of the boards", String(optOut.status));

const republish = await call("publishGrove", {});
check(republish.body?.result?.withdrawn === true,
      "publishing while opted out takes the card down instead");

const afterOptOut = await fetch(`${FS}/groves/${uid}`, { headers: bearer });
check(afterOptOut.status === 404, "and the card is gone", String(afterOptOut.status));

// Twice is a success, not an error. A withdrawal that could fail permanently is a device
// retrying it for the life of the account — invariant 13a.
const withdrawAgain = await call("withdrawGrove", {});
check(withdrawAgain.status === 200, "withdrawing a card that is already gone succeeds",
      String(withdrawAgain.status));

console.log(`\n${pass} passed, ${fail} failed`);
process.exit(fail === 0 ? 0 : 1);
