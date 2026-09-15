#!/usr/bin/env node
/**
 * The server half of the public boards' shared contract.
 *
 *     npm --prefix firebase/functions test
 *
 * A grove's worth, the keeper level behind it, the public name and the endless best are all
 * derived twice — in C# so the game can draw them offline, and here so a forged save
 * cannot rank. Two implementations of one rule drift, so both run
 * firebase/shared/grove-vectors.json. Assets/Game/Tests/GroveBoardTests.cs is the other
 * half.
 *
 * If this goes red, the board would show a player a different number than their own grove
 * screen does — which is not a crash, is not caught by anything else, and is very hard to
 * explain to somebody who is looking at both.
 *
 * `summarise` is exercised here too, and it is server-only by nature: no client ever ranks
 * anybody. It is in this file rather than a separate one because its inputs are the same
 * cards this file already builds.
 */

import { readFileSync, existsSync } from "node:fs";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(HERE, "..", "..", "..");

const compiled = join(REPO, "firebase", "functions", "lib", "grove.js");
if (!existsSync(compiled)) {
  console.error("build the functions first: npm --prefix firebase/functions run build");
  process.exit(1);
}

const {
  groveWorth, keeperLevel, starsFor, bestWave, MAX_WAVE,
  sanitiseName, isNameAllowed, publicName, boardName, fallbackName,
  BOARD_IDS, deciles, optedIn, saveRevision,
} = await import(pathToFileURL(compiled).href);

const namesModule = join(REPO, "firebase", "functions", "lib", "names.js");
const { nameKey, isNameClaimable, MAX_KEY_LENGTH } =
  await import(pathToFileURL(namesModule).href);

const shared = (name) =>
  JSON.parse(readFileSync(join(REPO, "firebase", "shared", name), "utf8"));

const vectors = shared("grove-vectors.json");

// The catalog is a real homestead.json so the C# half can feed it through the mapper that
// ships. Read here the way the seeder reads the shipped one, so the two derivations of
// config/grove stay the same derivation.
const catalog = shared("grove-catalog.json");

let pass = 0;
let fail = 0;

function check(name, condition, detail = "") {
  if (condition) { console.log("  ok   " + name); pass++; }
  else { console.log("  FAIL " + name + (detail ? "  — " + detail : "")); fail++; }
}

function equal(name, actual, expected) {
  check(name, actual === expected, `expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`);
}

// The grove catalog, in the shape the seeder publishes. Built from the same vector the C#
// half feeds through HomesteadMapper, so a change to one is a change to both.
function groveConfig() {

  const pieces = {};
  const bundles = {};
  const dwellings = {};
  const dwellingLevels = {};
  for (const piece of catalog.pieces) {
    if ((piece.cost ?? 0) > 0) pieces[piece.id] = piece.cost;
    if ((piece.cost ?? 0) > 0 && (piece.bundle ?? 1) > 1) bundles[piece.id] = piece.bundle;
    if (piece.kind === "dwelling") {
      dwellings[piece.id] = piece.tier ?? 0;
      // Written only when there is a gate, which is what `seed-config.mjs` publishes and what
      // keeps the field additive — an absent entry means ungated in both directions.
      const gate = Math.floor(piece.requiresKeeperLevel ?? 0);
      if (gate > 0) dwellingLevels[piece.id] = gate;
    }
  }

  const regions = {};
  for (const region of catalog.floor.regions) {
    if ((region.cost ?? 0) > 0) regions[region.id] = region.cost;
  }

  const companions = {};
  for (const companion of vectors.companions) {
    if ((companion.unlockCost ?? 0) > 0) {
      companions[companion.id] = { cost: companion.unlockCost, level: companion.unlockLevel ?? 0 };
    }
  }

  return {
    version: 1, pieces, bundles, regions, companions, dwellings, dwellingLevels,
    stars: catalog.score.stars,
  };
}

// ------------------------------------------------------------------- grove worth
console.log("\ngrove worth");
{
  const config = groveConfig();

  for (const c of vectors.worthCases) {
    // A case carrying `stock` is a v20 save; one carrying only `pieces` is a v19 save,
    // and it stays that way on purpose — it is the coverage for the fallback a device
    // that has not updated still goes through. Both must reach the same worth for the
    // same holdings, which is what the "reads as one bundle of each" case pins.
    const save = c.stock
      ? {
          homesteadStock: c.stock,
          groveLandOwned: c.land,
          companionsOwned: c.companions,
        }
      : {
          homesteadOwned: c.pieces,
          groveLandOwned: c.land,
          companionsOwned: c.companions,
        };

    const worth = groveWorth(save, config, c.keeperLevel, c.affordable);

    check(
      `${c.name}: ${worth.earned} earned + ${worth.bought} bought -> ${worth.score}`,
      worth.earned === c.earned && worth.bought === c.bought &&
      worth.score === c.score && worth.stars === c.stars && worth.clamped === c.clamped,
      `expected ${c.earned}/${c.bought}/${c.score}/${c.stars}/${c.clamped}, ` +
      `got ${worth.earned}/${worth.bought}/${worth.score}/${worth.stars}/${worth.clamped}`
    );
  }

  // An id nobody has ever heard of is worth nothing rather than a crash. It is the
  // ordinary case for a server one content drop behind a client, not an attack.
  const unknown = groveWorth(
    { homesteadOwned: ["no_such_piece"], groveLandOwned: ["nowhere"], companionsOwned: ["nobody"] },
    config, 1, 100000
  );
  equal("an unknown id is worth nothing", unknown.score, 0);

  // Every axis a client controls on the stock array. None of these can be reached by the
  // shipped writer; all of them can be reached by a modified one, and the failure this
  // guards against is arithmetic that overflows rather than a grove that scores high —
  // the affordability ceiling already handles scoring high.
  const junkStock = groveWorth(
    {
      homesteadStock: [
        { id: "fence", copies: 1e308 },       // beyond the copy ceiling
        { id: "fence", copies: 5 },           // a duplicate row, which the file forbids
        { id: "bench", copies: -4 },          // negative
        { id: "bench", copies: "12" },        // not a number
        { id: "", copies: 3 },                // no id
        { id: "x".repeat(200), copies: 3 },   // an id no catalog could hold
        null,
        "bench",
      ],
      groveLandOwned: [],
      companionsOwned: [],
    },
    config, 1, 100000
  );
  // fence resolves to the copy ceiling (the larger of the two rows, clamped), which is
  // 9,999 x 90 = 899,910 — and every other row in that array is dropped.
  equal("a malformed stock row is dropped rather than trusted", junkStock.bought, 899910);
  equal("and the copy ceiling is what stops it overflowing", junkStock.score, 100000);

  // An empty stock array is not "owns nothing"; it falls through to the v19 field, because
  // that is what GroveStock.In does on the client and the two must agree. Reachable from an
  // ordinary partial update, and it scores a real grove at zero if it is got wrong.
  const emptyStock = groveWorth(
    { homesteadStock: [], homesteadOwned: ["bench"], groveLandOwned: [], companionsOwned: [] },
    config, 1, 100000
  );
  equal("an empty stock array falls back to the v19 field", emptyStock.bought, 500);

  const legacyBundle = groveWorth(
    { homesteadOwned: ["fence"], groveLandOwned: [], companionsOwned: [] },
    config, 1, 100000
  );
  equal("a v19 fence is worth one whole bundle", legacyBundle.bought, 900);

  // Malformed inputs are the shape a forged save actually arrives in.
  const junk = groveWorth(
    { homesteadOwned: "bench", groveLandOwned: null, companionsOwned: [1, 2, {}] },
    config, 1, 100000
  );
  equal("a save with junk in it scores zero", junk.score, 0);

  const repeated = groveWorth(
    { homesteadOwned: ["bench", "bench", "bench"], groveLandOwned: [], companionsOwned: [] },
    config, 1, 100000
  );
  equal("a piece listed three times is counted once", repeated.score, 500);

  const negative = groveWorth({}, config, 1, -5000);
  equal("a negative ceiling clamps to nothing rather than inverting", negative.score, 0);
}

// ------------------------------------------------------------------ keeper level
console.log("\nkeeper level");
{
  for (const c of vectors.keeperCases) {
    equal(`${c.xp} xp is level ${c.level}`, keeperLevel(c.xp, vectors.keeperCurve), c.level);
  }

  equal("negative xp is level 1", keeperLevel(-100, vectors.keeperCurve), 1);
}

// ------------------------------------------------------------------------ stars
console.log("\nstars");
{
  const ladder = catalog.score.stars;

  for (const c of vectors.starCases) {
    equal(`${c.score} earns ${c.stars} star(s)`, starsFor(c.score, ladder), c.stars);
  }
}

// ------------------------------------------------------------------- endless best
//
// The one figure on a card that cannot be recomputed, so what is tested is the two things
// standing in for that: it is read exactly as the client reads it (`EndlessLedger.BestIn`),
// and it is bounded. A save that has never played the lane has to come back as a plain nought,
// because `buildCard` omits the field on a nought and that is what keeps the endless board's
// index to the players who are on it.
console.log("\nendless best");
{
  equal("a save with no rows has no wave", bestWave({}), 0);
  equal("an empty array has no wave", bestWave({ endlessBest: [] }), 0);
  equal("a non-array is not a wave", bestWave({ endlessBest: 37 }), 0);

  equal("the best of every row wins",
        bestWave({ endlessBest: [
          { level: "s02_endlesswatch", wave: 12 },
          { level: "s09_elsewhere", wave: 31 },
          { level: "s10_lower", wave: 4 },
        ] }), 31);

  equal("a row naming nothing is not a run",
        bestWave({ endlessBest: [{ level: "", wave: 900 }, { level: "a", wave: 3 }] }), 3);
  equal("a level id no catalog could have shipped is refused",
        bestWave({ endlessBest: [{ level: "x".repeat(49), wave: 900 }] }), 0);
  equal("a null row is skipped",
        bestWave({ endlessBest: [null, { level: "a", wave: 5 }] }), 5);
  equal("a wave that is not a number is nought",
        bestWave({ endlessBest: [{ level: "a", wave: "many" }] }), 0);
  equal("a negative wave is nought",
        bestWave({ endlessBest: [{ level: "a", wave: -9 }] }), 0);

  // The bound. It has to be the client's, or the prediction a device draws and the card the
  // server writes disagree for the one account that reaches it.
  equal("the ceiling holds", bestWave({ endlessBest: [{ level: "a", wave: 10 ** 9 }] }), MAX_WAVE);
  equal("the ceiling is the one the client publishes", MAX_WAVE, 9999);

  // Past `EndlessLedger.MaxRows`, which is also the rules' own cap. A document written before
  // that cap existed must not be able to cost this an unbounded walk.
  const long = Array.from({ length: 200 }, (_, i) => ({ level: `a${i}`, wave: i }));
  equal("no more rows are read than the rules allow", bestWave({ endlessBest: long }), 63);
}

// ------------------------------------------------------------------------- names
console.log("\npublic names");
{
  // The C# half reads the code points rather than the strings, because Unity's JsonUtility
  // truncates the bidi and zero-width cases silently. This asserts the two encodings say the
  // same thing, so a vector file cannot come to disagree with itself — which would be worse
  // than either encoding alone, since each half would pass against its own half of the file.
  const codesOf = (text) => Array.from(text, (ch) => ch.codePointAt(0));

  for (const c of vectors.nameCases) {
    equal(`${JSON.stringify(c.stored)} codes match its string`,
          JSON.stringify(c.storedCodes), JSON.stringify(codesOf(c.stored)));
    equal(`${JSON.stringify(c.public)} codes match its string`,
          JSON.stringify(c.publicCodes), JSON.stringify(codesOf(c.public)));

    equal(`${JSON.stringify(c.key)} codes match its string`,
          JSON.stringify(c.keyCodes), JSON.stringify(codesOf(c.key)));

    equal(`sanitise ${JSON.stringify(c.stored)}`, sanitiseName(c.stored), c.public);
    equal(`allow ${JSON.stringify(c.stored)}`, isNameAllowed(sanitiseName(c.stored)), c.allowed);
    equal(`key ${JSON.stringify(c.stored)}`, nameKey(c.stored), c.key);
    equal(`claimable ${JSON.stringify(c.stored)}`, isNameClaimable(c.stored), c.claimable);
  }

  equal("a non-string name sanitises to nothing", sanitiseName(42), "");
  equal("an undefined name sanitises to nothing", sanitiseName(undefined), "");

  // The fallback is the reason two unnamed keepers do not share a row, so it has to be
  // stable for one account and different between two.
  equal("the fallback is stable for an account", fallbackName("abc123"), fallbackName("abc123"));
  check("the fallback differs between accounts", fallbackName("abc123") !== fallbackName("abc124"));
  check("the fallback is itself publishable", isNameAllowed(fallbackName("abc123")));

  equal("a refused name is published under a handle",
        publicName("ADMIN", "abc123"), fallbackName("abc123"));
  equal("an empty name is published under a handle",
        publicName("", "abc123"), fallbackName("abc123"));
  equal("a good name is published as itself", publicName("Fern", "abc123"), "Fern");

  // An emoji name would draw as a row nobody can report or search for, and half a
  // surrogate pair in a database is worse than no character at all.
  equal("astral characters are dropped", sanitiseName("Fern\u{1F600}Willow"), "FernWillow");
}

// ------------------------------------------------------------------- name keys
console.log("\nname keys");
{
  // The fold's whole job, stated as the thing a player would notice: these are one name, so
  // exactly one of them can be reserved. The fullwidth and ligature spellings are the ones no
  // amount of reading the code catches.
  const oneName = ["Fern", "fern", "FERN", "F e r n", "Ｆｅｒｎ", "F.e.r.n", " Fern "];
  for (const spelling of oneName) {
    equal(`${JSON.stringify(spelling)} is the same name as Fern`, nameKey(spelling), "fern");
  }

  check("a digit makes a different name", nameKey("Fern") !== nameKey("Fern2"));

  // Folding to ASCII would be shorter and would leave every player writing in these scripts
  // with no reservable name at all, which in a game shipped globally is not a corner case.
  check("cyrillic survives the fold", nameKey("Фёдор").length >= 2);
  check("kana survives the fold", nameKey("こけもも").length >= 2);
  check("arabic survives the fold", nameKey("فرن").length >= 2);

  // A document id may not be unbounded. Compatibility normalisation expands, so a name at the
  // length limit can legitimately fold to something longer than itself.
  check("the key is bounded", nameKey("㎐".repeat(16)).length <= MAX_KEY_LENGTH);
  check("an expanding name still folds to something", nameKey("㎐㎐").length >= 2);

  // The pair of measurements. Two visible characters and an empty fold is the case that would
  // have put two keepers on one board under one name.
  equal("punctuation is not claimable", isNameClaimable("!!"), false);
  equal("punctuation still sanitises to itself", sanitiseName("!!"), "!!");
  equal("a real name is claimable", isNameClaimable("Fern"), true);
  equal("a filtered name is not claimable", isNameClaimable("ADMIN"), false);

  // A Firestore document id may not be empty, contain a slash, or be dot-shaped. The fold
  // yields letters and digits only, so none of those are expressible — asserted rather than
  // reasoned about, because a key that broke this would fail at the write and not before.
  for (const c of vectors.nameCases) {
    check(`${JSON.stringify(c.key)} is a legal document id`,
          c.key === "" || (!c.key.includes("/") && c.key !== "." && c.key !== ".." &&
                           !/^__.*__$/.test(c.key) && Buffer.byteLength(c.key, "utf8") <= 1500));
  }
}

// --------------------------------------------------------------- the board name
console.log("\nboard names");
{
  // The card's name comes from the reservation and never from the save, which is what makes it
  // unforgeable rather than merely sanitised.
  equal("a confirmed name is published", boardName("Fern", "abc123"), "Fern");
  equal("no confirmed name is a handle", boardName(null, "abc123"), fallbackName("abc123"));
  equal("an empty confirmation is a handle", boardName("", "abc123"), fallbackName("abc123"));

  // Re-tested at publish time on purpose: the word list grows, and a name claimed before a word
  // was added must leave the boards on the next rebuild rather than needing a sweep.
  equal("a name the filter now refuses drops to a handle",
        boardName("ADMIN", "abc123"), fallbackName("abc123"));

  // The bidi guard still applies to a name that reached the reservation through an older build.
  equal("a bidi override never reaches a card",
        boardName("Fern‮Willow", "abc123"), "FernWillow");
}

// --------------------------------------------------------------------- opting out
console.log("\nopting out");
{
  check("a save with no settings is in", optedIn({}));
  check("an unset flag is in", optedIn({ settings: { board: 0 } }));
  check("an explicit on is in", optedIn({ settings: { board: 1 } }));
  check("an explicit off is out", !optedIn({ settings: { board: 2 } }));
  check("a junk flag is in rather than out", optedIn({ settings: { board: "yes" } }));
}

// ---------------------------------------------------------------------- the boards
console.log("\nranking");
{
  // `summarise` is gone, and with it the test that drove it. Boards are a query now
  // (`orderBy("score","desc").limit(100)`) and populations are a `count()`, because the top
  // hundred of a bounded sample stopped being the top hundred the day this game had more
  // than RANK_SAMPLE_SIZE published groves. Neither can be exercised without Firestore, so
  // what is pinned here is everything around them that still can be — and the writability
  // check below is the one that has already cost a live run.

  // Deciles of a known list, so the shape of the distribution is pinned rather than assumed.
  // Nearest-rank, the definition stats.ts already uses.
  const ten = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100];
  equal("nine deciles", deciles(ten).length, 9);
  equal("the first decile is the tenth value", deciles(ten)[0], 10);
  equal("the ninth decile is the ninetieth value", deciles(ten)[8], 90);

  equal("an empty list has no deciles", JSON.stringify(deciles([])), "[]");

  // The assertion that was missing, and it cost a live run: the old test checked the sample
  // count and never that the result could be *written*. `deciles([])` returned nine
  // `undefined`s, Firestore refuses those as document values, and the job threw after it had
  // already published ten board documents — leaving the boards up and the distribution
  // absent, which is precisely the state the feature's first day is in. Anything a scheduled
  // job writes has to be checked for writability, not only for arithmetic.
  const empty = { samples: 0, deciles: deciles([]) };
  check("every value the ranks document carries is writable", writable(empty),
        JSON.stringify(empty));
  check("every value a board document carries is writable",
        writable({ entries: [], population: 0, builtUnix: 0 }));

  const one = { samples: 1, deciles: deciles([4200]) };
  equal("a population of one still produces nine deciles", one.deciles.length, 9);
  check("and they are writable", writable(one));

  // The wave distribution is the same function over a second array, and it is what answers
  // "where do I stand" for everybody the hundred-row board cannot reach. An empty one has to
  // come back as an empty *array* rather than nine undefineds — Firestore refuses undefined,
  // and this is the state on the day the board ships, when no watcher exists yet.
  const noWatchers = { waveSamples: 0, waveDeciles: deciles([]) };
  equal("no watchers yet publishes no wave deciles", noWatchers.waveDeciles.length, 0);
  check("and the document is still writable", writable(noWatchers));

  const watched = deciles([3, 5, 5, 8, 12, 14, 19, 23, 31, 44].sort((a, b) => a - b));
  equal("a wave sample produces nine deciles", watched.length, 9);
  check("ascending, which is what the client refuses a table for not being",
        watched.every((v, i) => i === 0 || v >= watched[i - 1]), watched.join(","));
  check("and every one of them is a real number", watched.every(Number.isFinite));

  // Every board the job publishes, and there is exactly one list of them. The nine league
  // boards are gone; `l0`..`l8` are spent ids and `pruneRetiredBoards` is what takes the
  // documents away. The deletion scrub deliberately no longer reads this list — it walks the
  // collection instead, because a board that has been retired but not yet pruned is exactly
  // where a deleted keeper's name would otherwise survive (invariant 27).
  equal("two boards, and no more", BOARD_IDS.length, 2);
  equal("the global board is named first", BOARD_IDS[0], "global");
  equal("the endless board is named second", BOARD_IDS[1], "endless");

  check("no retired league id has come back",
        BOARD_IDS.every((id) => !/^l[0-8]$/.test(id)));

  // Mirrored by `LeaderboardBoard.All`, and the client refuses a board id outside its own
  // list before it spends a read on it — so an id here that the client does not know is a
  // board nobody can ever open.
  equal("the list the client mirrors", BOARD_IDS.join(","), "global,endless");
}

/** True when nothing in this value is undefined — what Firestore actually demands. */
function writable(value) {
  if (value === undefined) return false;
  if (value === null || typeof value !== "object") return true;

  if (Array.isArray(value)) return value.every(writable);

  return Object.values(value).every(writable);
}

console.log(`\n${pass} passed, ${fail} failed`);
// ------------------------------------------------------------------ the revision
//
// What `publishGrove` reports beside the card, so the client can prove the card was built
// from the save it pushed. The client reads a *missing* field as "cannot be checked" and a
// nought as a real answer, so the shape of the bad cases matters as much as the good one.
equal("a save's revision is reported as written", saveRevision({ cloud: { revision: 41 } }), 41);
equal("a revision written as a string is read", saveRevision({ cloud: { revision: "17" } }), 17);
equal("a fractional revision is floored", saveRevision({ cloud: { revision: 17.9 } }), 17);
equal("a save with no cloud block reports nought", saveRevision({}), 0);
equal("a cloud block with no revision reports nought", saveRevision({ cloud: {} }), 0);
equal("a negative revision reports nought", saveRevision({ cloud: { revision: -3 } }), 0);
equal("an unreadable revision reports nought", saveRevision({ cloud: { revision: "later" } }), 0);
equal("a cloud block that is not an object reports nought", saveRevision({ cloud: 7 }), 0);

process.exit(fail === 0 ? 0 : 1);
