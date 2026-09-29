#!/usr/bin/env node
/**
 * The daily challenges' server half: the two ids, the config reader, the wallet's deals and the
 * pricing of a claim.
 *
 *     npm --prefix firebase/functions test
 *
 * Everything here fails silently on a device — a loose parse pays the wrong day, a loose
 * allowance pays for a deal nobody bought, and a claim refused instead of left unconfirmed is
 * money shown and taken back (45d) — so each is pinned by name.
 */

import { existsSync } from "node:fs";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(HERE, "..", "..", "..");
const compiled = join(REPO, "firebase", "functions", "lib", "challenges.js");

if (!existsSync(compiled)) {
  console.error("build the functions first: npm --prefix firebase/functions run build");
  process.exit(1);
}

const {
  DEFAULT_CHALLENGES, HARD_MAX_CLEARS, MAX_AD_PLAYS_PER_DAY, MAX_CHALLENGE_DAYS_BEHIND,
  adPlaysDrawn, adPlaysOn, allowanceOn, challengeClears, challengeGrant, challengeXp,
  dealPrice, drawAdPlay, findTier, grantAdPlays, holdTier, isChallengeGrantId, isChallengeTierSpendId,
  parseChallengeClaim, parseChallengeTierSpendId, readChallengeAds, readChallengeTiers, usableChallengesConfig,
} = await import(pathToFileURL(compiled).href);

let pass = 0, fail = 0;
const check = (ok, what, detail = "") => {
  if (ok) { pass++; console.log("  ok  ", what); }
  else { fail++; console.log("  FAIL", what, detail ? " — " + detail : ""); }
};
const equal = (what, got, want) =>
  check(JSON.stringify(got) === JSON.stringify(want), what,
        `expected ${JSON.stringify(want)}, got ${JSON.stringify(got)}`);

const CONFIG = {
  genres: ["glade", "merge", "pairs", "sokoban"],
  freePlays: 2,
  tiers: [
    { id: "bronze", gems: 120, plays: 5, days: 7 },
    { id: "silver", gems: 200, plays: 10, days: 7 },
    { id: "gold", gems: 500, plays: 25, days: 7 },
  ],
  coins: 40,
  xp: 20,
  maxClears: 25000,
};
const DAY = 20500;

// ------------------------------------------------------------------ the ids
console.log("\nthe claim id");
{
  check(isChallengeGrantId("chal:20500:pairs:1:credits"), "a challenge id is recognised");
  check(!isChallengeGrantId("chaltier:bronze:20500"), "a deal debit is not a claim");
  check(!isChallengeGrantId("endless:20500:0:credits"), "an endless id is not");

  equal("a well-formed id parses", parseChallengeClaim("chal:20500:pairs:3:credits"),
        { dayKey: 20500, genre: "pairs", win: 3, currency: "credits" });
  equal("a win of nought is refused", parseChallengeClaim("chal:20500:pairs:0:credits"), null);
  equal("a genre with a capital is refused", parseChallengeClaim("chal:20500:Pairs:1:credits"), null);
  equal("a leading plus is refused", parseChallengeClaim("chal:+20500:pairs:1:credits"), null);
  equal("too few parts is refused", parseChallengeClaim("chal:20500:pairs:1"), null);
  equal("too many parts is refused", parseChallengeClaim("chal:20500:pairs:1:credits:x"), null);
  equal("an empty currency is refused", parseChallengeClaim("chal:20500:pairs:1:"), null);
}

console.log("\nthe deal debit id");
{
  check(isChallengeTierSpendId("chaltier:bronze:20500:20500"), "a deal debit is recognised");
  check(!isChallengeTierSpendId("pass:watch_0001"), "a pass is not");
  equal("a fresh debit parses", parseChallengeTierSpendId("chaltier:bronze:20500:20500"),
        { tierId: "bronze", fromDay: 20500, boughtDay: 20500 });
  equal("an upgrade debit parses", parseChallengeTierSpendId("chaltier:silver:20500:20503"),
        { tierId: "silver", fromDay: 20500, boughtDay: 20503 });
  equal("the old three-part shape is refused", parseChallengeTierSpendId("chaltier:bronze:20500"), null);
  equal("a day of nought is refused", parseChallengeTierSpendId("chaltier:bronze:0:20500"), null);
  equal("a window beginning after its purchase is refused", parseChallengeTierSpendId("chaltier:bronze:20503:20500"), null);
  equal("a tier with a dash is refused", parseChallengeTierSpendId("chaltier:bron-ze:20500:20500"), null);
  equal("a fifth part is refused", parseChallengeTierSpendId("chaltier:bronze:20500:20500:x"), null);
}

// ------------------------------------------------------------------ the config
console.log("\nthe published block");
{
  equal("a good block reads back whole", usableChallengesConfig(CONFIG), CONFIG);
  equal("no block is null", usableChallengesConfig(undefined), null);
  equal("a ladder that does not climb is refused", usableChallengesConfig({
    ...CONFIG, tiers: [{ id: "a", gems: 10, plays: 5, days: 7 }, { id: "b", gems: 20, plays: 5, days: 7 }],
  }), null);
  equal("a deal under the free figure is refused", usableChallengesConfig({
    ...CONFIG, tiers: [{ id: "a", gems: 10, plays: 2, days: 7 }],
  }), null);
  equal("a duplicated deal id is refused", usableChallengesConfig({
    ...CONFIG, tiers: [{ id: "a", gems: 10, plays: 5, days: 7 }, { id: "a", gems: 20, plays: 9, days: 7 }],
  }), null);
  equal("a coin rate past the typo guard is refused", usableChallengesConfig({ ...CONFIG, coins: 5000 }), null);
  equal("a genre that is not key-shaped is dropped, not fatal",
        usableChallengesConfig({ ...CONFIG, genres: ["pairs", "Bad!"] }).genres, ["pairs"]);
  equal("a block with no deals sells none", usableChallengesConfig({ ...CONFIG, tiers: [] }).tiers, []);
  equal("the deal a debit names is found", findTier(CONFIG, "silver").plays, 10);
  equal("a deal the block does not sell is null", findTier(CONFIG, "platinum"), null);
}

// ------------------------------------------------------------------ the wallet's deals
console.log("\nthe held deals");
{
  equal("a clean map reads back", readChallengeTiers({ bronze: 20500, gold: 20490 }), { bronze: 20500, gold: 20490 });
  equal("a nought date is dropped", readChallengeTiers({ bronze: 0 }), {});
  equal("a string date is dropped", readChallengeTiers({ bronze: "20500" }), {});
  equal("a key that is not key-shaped is dropped", readChallengeTiers({ "Bron ze": 20500 }), {});
  equal("nothing is an empty map", readChallengeTiers(undefined), {});

  equal("holding a deal writes its day", holdTier({}, "bronze", DAY), { bronze: DAY });
  equal("a later purchase replaces an earlier", holdTier({ bronze: DAY - 10 }, "bronze", DAY), { bronze: DAY });
  equal("an earlier purchase does not replace a later", holdTier({ bronze: DAY }, "bronze", DAY - 10), { bronze: DAY });
  equal("holding does not touch another deal", holdTier({ gold: DAY }, "bronze", DAY), { gold: DAY, bronze: DAY });
}

// ------------------------------------------------------------------ the allowance
console.log("\nthe allowance");
{
  equal("nothing held is the free figure", allowanceOn(CONFIG.tiers, {}, DAY, 2), 2);
  equal("a deal bought today covers today", allowanceOn(CONFIG.tiers, { bronze: DAY }, DAY, 2), 5);
  equal("the seventh day after a seven-day purchase is still covered, because the client's window is an instant inside it",
        allowanceOn(CONFIG.tiers, { bronze: DAY }, DAY + 7, 2), 5);
  equal("the eighth is free again", allowanceOn(CONFIG.tiers, { bronze: DAY }, DAY + 8, 2), 2);
  equal("the larger of two overlapping deals governs", allowanceOn(CONFIG.tiers, { bronze: DAY, gold: DAY + 1 }, DAY + 2, 2), 25);
  equal("a deal bought tomorrow does not cover today", allowanceOn(CONFIG.tiers, { bronze: DAY + 1 }, DAY, 2), 2);
}

// ------------------------------------------------------------------ the deal's price
console.log("\nthe deal's price");
{
  equal("a fresh deal costs its full price", dealPrice(CONFIG, {}, "bronze", DAY, DAY), { price: 120, upgrades: "" });
  equal("a fresh deal is full price whatever runs", dealPrice(CONFIG, { gold: DAY }, "bronze", DAY + 1, DAY + 1), { price: 120, upgrades: "" });
  equal("an upgrade over a held smaller deal costs the difference",
        dealPrice(CONFIG, { bronze: DAY }, "silver", DAY, DAY + 3), { price: 80, upgrades: "bronze" });
  equal("an upgrade over silver to gold costs what is left",
        dealPrice(CONFIG, { bronze: DAY, silver: DAY }, "gold", DAY, DAY + 5), { price: 300, upgrades: "silver" });
  equal("an upgrade naming a window this wallet does not hold is refused",
        dealPrice(CONFIG, { bronze: DAY }, "silver", DAY - 1, DAY + 3), null);
  equal("an upgrade over a window that has ended is refused",
        dealPrice(CONFIG, { bronze: DAY }, "silver", DAY, DAY + 8), null);
  equal("an upgrade to a smaller deal is refused", dealPrice(CONFIG, { silver: DAY }, "bronze", DAY, DAY + 3), null);
  equal("a deal the block does not sell is refused", dealPrice(CONFIG, {}, "platinum", DAY, DAY), null);
  equal("a purchase before its window is refused", dealPrice(CONFIG, {}, "bronze", DAY + 1, DAY), null);
}

// ------------------------------------------------------------------ the pricing
console.log("\nthe claim's price");
{
  const claim = (win, genre = "pairs", day = DAY) => ({ dayKey: day, genre, win, currency: "credits" });

  equal("a free play pays the rate", challengeGrant(CONFIG, claim(2), {}), { amount: 40, allowance: 2, known: true });
  equal("a third play with no deal pays nothing", challengeGrant(CONFIG, claim(3), {}), { amount: 0, allowance: 2, known: true });
  equal("a third play under bronze pays", challengeGrant(CONFIG, claim(3), { bronze: DAY }), { amount: 40, allowance: 5, known: true });
  equal("a sixth play under bronze pays nothing", challengeGrant(CONFIG, claim(6), { bronze: DAY }), { amount: 0, allowance: 5, known: true });
  equal("a sixth play under silver pays", challengeGrant(CONFIG, claim(6), { silver: DAY - 3 }), { amount: 40, allowance: 10, known: true });
  equal("a deal that ran out does not cover the day", challengeGrant(CONFIG, claim(3), { bronze: DAY - 8 }), { amount: 0, allowance: 2, known: true });
  equal("a genre the file does not ship is not known", challengeGrant(CONFIG, claim(1, "sudoku"), {}), { amount: 0, allowance: 2, known: false });
  equal("a rate of nought pays nothing", challengeGrant({ ...CONFIG, coins: 0 }, claim(1), {}), { amount: 0, allowance: 2, known: true });
}

// ------------------------------------------------------------------ the XP
console.log("\nthe xp");
{
  equal("the built-in figures are the client's", DEFAULT_CHALLENGES, { freePlays: 2, coins: 40, xp: 20, maxClears: 25000 });
  equal("the structural ceiling is the client's", HARD_MAX_CLEARS, 1000000);

  const save = { challenges: { clears: [{ genre: "pairs", count: 3 }, { genre: "merge", count: 4 }] } };
  equal("clears are summed across genres", challengeClears(save), 7);
  equal("xp is the rate over the sum", challengeXp(save, { challenges: CONFIG }), 140);
  equal("an unseeded config pays the built-in rate", challengeXp(save, {}), 7 * DEFAULT_CHALLENGES.xp);
  equal("a written nought ceiling pays nothing", challengeXp(save, { challenges: { ...CONFIG, maxClears: 0 } }), 0);
  equal("a save with no block pays nothing", challengeXp({}, { challenges: CONFIG }), 0);
  equal("a non-object block pays nothing", challengeClears({ challenges: 7 }), 0);
  equal("a null row is skipped", challengeClears({ challenges: { clears: [null, { genre: "pairs", count: 2 }] } }), 2);
}

// ------------------------------------------------------------------ the advert pool
console.log("\nthe advert pool");
{
  const DAY = 20700;
  const claim = (win, genre = "pairs", day = DAY) => ({ dayKey: day, genre, win, currency: "credits" });

  // The count: one play a confirmed view, never past the cap, and a full day adds nothing.
  let days = {};
  for (let i = 0; i < 12; i++) days = grantAdPlays(days, DAY, 1, 10).days;
  equal("ten views fill a cap of ten and the rest count nothing", adPlaysOn(days, DAY), 10);
  equal("a view past the cap adds nought", grantAdPlays(days, DAY, 1, 10).added, 0);
  equal("a cap above the ceiling is held to it",
        adPlaysOn(grantAdPlays({}, DAY, 99, 500).days, DAY), MAX_AD_PLAYS_PER_DAY);
  equal("an old day is pruned when a new one is counted",
        Object.keys(grantAdPlays({ [DAY - MAX_CHALLENGE_DAYS_BEHIND - 1]: { granted: 3, drawn: {} } }, DAY, 1, 10).days),
        [String(DAY)]);

  // The draw: a win inside the allowance never touches the pool; one past it spends a play.
  const two = { [DAY]: { granted: 2, drawn: {} } };
  equal("a win inside the allowance is covered without a draw", drawAdPlay(two, claim(2), 2), two);
  const once = drawAdPlay(two, claim(3), 2);
  equal("a win past the allowance draws a play", once?.[DAY]?.drawn, { pairs: [3] });
  const twice = drawAdPlay(once, claim(3, "merge"), 2);
  equal("the pool is shared by every genre", twice?.[DAY]?.drawn, { pairs: [3], merge: [3] });
  equal("a third win past the allowance is refused once the pool is spent", drawAdPlay(twice, claim(4), 2), null);
  equal("an ordinal already drawn is covered again, not drawn twice", drawAdPlay(twice, claim(3), 2), twice);
  equal("a day with no advert plays covers nothing past the allowance", drawAdPlay({}, claim(3), 2), null);
  equal("another day's plays do not cover this one", drawAdPlay(two, claim(3, "pairs", DAY + 1), 2), null);

  // The recount: a deal landing after a win was drawn gives that play back.
  equal("drawn plays past the allowance are counted", adPlaysDrawn(twice[DAY], 2), 2);
  equal("a deal that now covers them gives both back", adPlaysDrawn(twice[DAY], 5), 0);
  equal("so a later win can draw again", drawAdPlay(twice, claim(7), 5)?.[DAY]?.drawn?.pairs, [3, 7]);

  // The reader: shape and the window.
  equal("the reader keeps a well-formed day", readChallengeAds({ [DAY]: { granted: 3, drawn: { pairs: [5, 3, 3] } } }, DAY),
        { [DAY]: { granted: 3, drawn: { pairs: [3, 5] } } });
  equal("the reader drops a day no claim can name any more",
        readChallengeAds({ [DAY - MAX_CHALLENGE_DAYS_BEHIND - 1]: { granted: 3, drawn: {} } }, DAY), {});
  equal("the reader drops a count past the ceiling and junk",
        readChallengeAds({ [DAY]: { granted: 999 }, x: { granted: 1 }, [DAY - 1]: 7 }, DAY), {});
  equal("the reader drops a genre that is not key-shaped and ordinals that are not whole",
        readChallengeAds({ [DAY]: { granted: 2, drawn: { "Bad Genre": [3], pairs: [0, -1, 2.5, "4", 4] } } }, DAY),
        { [DAY]: { granted: 2, drawn: { pairs: [4] } } });
}

console.log(`\n${pass} passed, ${fail} failed`);
process.exit(fail === 0 ? 0 : 1);
