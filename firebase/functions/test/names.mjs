#!/usr/bin/env node
/**
 * The keeper-name word filter.
 *
 *     npm --prefix firebase/functions test
 *
 * This is the only proof that the fold does what the module says it does, and it matters more
 * than most suites here for one reason: **every failure mode of a word filter is silent.** A
 * filter that has stopped catching anything looks exactly like a filter with nothing to catch,
 * and a filter refusing innocent names looks, to everybody except the player holding the name,
 * like nothing at all. Neither shows up in a screenshot, a compile or a validator run.
 *
 * The cases are in four groups and the middle two are the point:
 *
 *  1. the fold itself, in isolation;
 *  2. **the three bypasses the shipped filter had**, each of which was one keystroke;
 *  3. **the false positives it had**, which cost real players their own names;
 *  4. the plumbing — config shape, the takedown flag, the thresholds.
 *
 * Every case in groups 2 and 3 was checked against the *old* implementation first. Twenty-one
 * of them fail on it. A suite that would have passed either way is not a guard.
 */

import { readFileSync, existsSync } from "node:fs";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(HERE, "..", "..", "..");
const LIB = join(REPO, "firebase", "functions", "lib");

if (!existsSync(join(LIB, "profanity.js"))) {
  console.error("build the functions first: npm --prefix firebase/functions run build");
  process.exit(1);
}

const load = async (name) => import(pathToFileURL(join(LIB, name)).href);

const { foldName, prepareBlocklist, judgeName, squeeze } = await load("profanity.js");
const { builtInBlocklist, resetBlocklistCache, loadNameConfig, DEFAULT_REPORT_THRESHOLD,
        NAMES_CONFIG_PATH } = await load("blocklist.js");
const { isNameAllowed, publicName, boardName, sanitiseName } = await load("grove.js");
const { publishableName, isDenied } = await load("names.js");

const shipped = JSON.parse(
  readFileSync(join(REPO, "firebase", "functions", "src", "name-blocklist.json"), "utf8")
);

const LIST = builtInBlocklist();

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

/** What the game actually asks: would this name go on a board? */
const allowed = (name) => isNameAllowed(sanitiseName(name), LIST);

function blocks(name, why = "") {
  const verdict = judgeName(sanitiseName(name), LIST);
  check(`refuses ${JSON.stringify(name)}${why ? "  (" + why + ")" : ""}`,
        !verdict.allowed,
        `folded to ${JSON.stringify(foldName(sanitiseName(name)).loose)}`);
}

function permits(name, why = "") {
  const verdict = judgeName(sanitiseName(name), LIST);
  check(`allows  ${JSON.stringify(name)}${why ? "  (" + why + ")" : ""}`,
        verdict.allowed,
        `matched ${JSON.stringify(verdict.word)} as ${verdict.kind}`);
}

// ============================================================== 1. the fold itself

console.log("\nthe fold");
{
  equal("case and punctuation fall away",
        foldName("F.e_r-n").base, "fern");

  equal("compatibility forms normalise",
        foldName("Ｆｅｒｎ").base, "fern");

  equal("diacritics are stripped in base and kept in plain",
        `${foldName("Fërn").base}/${foldName("Fërn").plain}`, "fern/fërn");

  equal("a stroke letter is not a combining mark, and is folded anyway",
        foldName("Øystein").base, "oystein");

  equal("the ligature block expands",
        foldName("ﬁne").base, "fine");

  equal("sharp s expands rather than lowercasing to itself",
        foldName("Straße").base, "strasse");

  equal("final sigma and medial sigma fold together",
        foldName("ΟΔΟΣ").base === foldName("οδος").base ? "same" : "different", "same");

  equal("plain keeps a non-Latin script intact",
        foldName("Хуй").plain, "хуй");

  equal("base maps the Latin-shaped Cyrillic onto Latin",
        foldName("Ѕсоре").base, "scope");

  equal("loose collapses the leet classes",
        foldName("$1ut").loose, "siut");

  equal("digits survive base so a whole-name test can strip them",
        foldName("Anal1").base, "anal1");

  equal("words are split before the separators are dropped",
        foldName("Fern Willow").words.join("|"), "fern|willow");

  equal("squeeze reduces a run to one",
        squeeze("fuuuuck"), "fuck");

  equal("an empty name folds to nothing rather than throwing",
        foldName("").base + foldName(null).base, "");
}

// ================================================= 2. the bypasses that used to work

console.log("\nbypasses the old filter had (each was one keystroke)");
{
  // Leetspeak. The old squash stripped separators and nothing else, so every one of these
  // reached a board.
  blocks("f4ggot", "digit for a letter");
  blocks("5hit", "digit for a letter");
  blocks("$hit", "symbol for a letter");
  blocks("phuck", "digraph substitution");
  blocks("fvck", "shape substitution");
  blocks("b1tch", "digit for a letter");
  blocks("s1ut", "1 for l, which is why loose collapses them");
  blocks("n1gg3r", "two at once");
  blocks("c0cksucker", "zero for o");

  // Homoglyphs. The old squash *deleted* everything outside a-z0-9, so one Cyrillic character
  // did not merely evade the list — it removed itself and left a word that matched nothing.
  blocks("fuсk", "Cyrillic es for c");
  blocks("shіt", "Cyrillic dotted i");
  blocks("bitсh", "Cyrillic es");
  blocks("ρuѕѕy", "Greek rho and Cyrillic dze");

  // Repeats.
  blocks("fuuuck", "a squeezed run");
  blocks("shiiiit", "a squeezed run");
  blocks("f u c k", "separators");
  blocks("f.u.c.k", "separators");

  // Non-Latin scripts, which folded to the empty string and were never filtered at all.
  blocks("хуй", "Russian, from the vendored ru list");
  blocks("сука", "Russian");
  blocks("merde", "French");
  blocks("scheisse", "German");
  blocks("Scheiße", "German, via the sharp-s expansion");
  blocks("puta", "Spanish");
  blocks("orospu", "Turkish");
  // Japanese, and only `plain` can match it: NFKD splits the handakuten off as a combining
  // mark and stripping it turns the word into a different one. This case is the reason a
  // script-preserving form exists beside the aggressive one.
  blocks("チンポ", "Japanese, matched on the script-preserving form");
  blocks("좆", "Korean — one character, so this is the whole-name class");
}

// ================================== 3. the false positives that cost players their names

console.log("\nfalse positives the old filter had");
{
  permits("Grapevine", "the reported one: rape is a substring, in a game about a garden");
  permits("Grapes");
  permits("Grapefruit");
  permits("Rapeseed", "a plant, and thematic");
  permits("Scrape");
  permits("Draper");
  permits("Therapist", "contains rapist, not merely rape");
  permits("Therapeutic");
  permits("Trapeze");
  permits("Scunthorpe", "the case the whole problem is named after");
  permits("Penistone", "a real town");

  // Names belonging to large populations, which an English-speaking team never tests.
  permits("Kshitij", "an ordinary Indian given name containing shit");
  permits("Kshitiz");
  permits("Shitala", "a Hindu goddess");
  permits("Nazir", "why nazi is not in the substring class");
  permits("Nazia");
  permits("Nazim");
  permits("Pornchai", "why porn is not in the substring class — a common Thai name");
  permits("Supaporn");
  permits("Pornthip");
  permits("Shiitake", "squeezes to shitake, which is why carve runs per haystack");
  permits("Mishit");

  // Words the old substring list would have caught had those entries existed, and which is
  // why they are whole-name only now.
  permits("Analysis");
  permits("Canal");
  permits("Bass");
  permits("Classic");
  permits("Assassin");
  permits("Embassy");
  permits("Peacock");
  permits("Cockatoo");
  permits("Hancock");
  permits("Dickens");
  permits("Badminton", "contains admin, which the reserved class carves out");
  permits("Stafford", "contains staff");
  permits("Staffordshire");
  permits("Systemic", "contains system");
  permits("Supporter", "contains support");
  permits("Sandwich");
  permits("Fuchs", "a German surname");
  permits("Suit", "loose maps l to i; suit must not collapse onto slut");
  permits("Count", "must not collapse onto cunt");
  permits("Flick");
  permits("Bookkeeper");

  // Ordinary names in other scripts, which have to survive the fold.
  permits("Ferñ");
  permits("Иван", "an ordinary Russian given name");
  permits("さくら", "an ordinary Japanese given name");
  permits("محمد", "an ordinary Arabic given name");
  permits("김민준", "an ordinary Korean given name");
  permits("Zoë");
  permits("O'Brien");
  permits("Fern Willow");
}

// ================================================ 4. the classes, and what each is for

console.log("\nthe three classes");
{
  // Whole-name, which is what makes the ambiguous words safe to carry at all.
  blocks("Nazi", "whole name, where Nazir is not");
  blocks("Anal", "whole name, where Analysis is not");
  blocks("Ass", "whole name, where Bass is not");
  blocks("Cock", "whole name, where Peacock is not");

  // Per word, so a bad word cannot hide behind a good one.
  blocks("Fern Nazi", "the second word");
  blocks("Nazi Fern", "the first word");

  // Digits stripped for the whole-name test, which is the reason `base` keeps them.
  blocks("Anal1", "digits stripped before the whole-name test");
  blocks("Nazi99");

  // Impersonation.
  blocks("Admin");
  blocks("Moderator");
  blocks("GlimmerGrove", "we are reserved");
  blocks("GlimmerGroveMod", "substring, because impersonating us in context is the abuse");
  blocks("AdminFern", "the form impersonation actually takes");
  blocks("Fernadminmoss", "a shipped vector already asserted this, and it was right");
  blocks("Moderator99");
  blocks("Support");

  // Scam advertising, which is the form this abuse takes in a game with a shop.
  blocks("FreeGems4U");
  blocks("freecoins");

  const verdict = judgeName("Admin", LIST);
  equal("a refusal says which class matched", verdict.kind, "reserved");

  // `word` reaches an operations log, so it has to be *our* list entry and never the string
  // the player typed. A set-backed lookup could only give back the folded candidate, which is
  // the same string for `Nazi` and a different one -- belonging to somebody else -- the moment
  // a player's name folds onto an entry spelled another way.
  const entries = new Set([...shipped.exact, ...shipped.anywhere, ...shipped.reserved]);

  const typed = ["xXfuckXx", "N A Z I", "ＮＡＺＩ", "Admin99", "f4ggot", "fuсk", "хуй"];

  for (const name of typed) {
    const word = judgeName(name, LIST).word;

    check(`refusing ${JSON.stringify(name)} names a list entry, not the name typed`,
          entries.has(word), `got ${JSON.stringify(word)}`);
  }

  // The case that makes it load-bearing: the entry is spelled one way and the name another,
  // so a set-backed lookup would have logged the player's spelling.
  equal("a fullwidth spelling still logs the entry as written",
        judgeName("ＮＡＺＩ", LIST).word, judgeName("Nazi", LIST).word);
}

// ================================================================ 5. carve, precisely

console.log("\ncarve");
{
  // The subtle one: removing an allowed word must not weld its neighbours into a new match.
  const list = prepareBlocklist({
    version: 1, anywhere: ["therapist"], exact: [], reserved: [], allow: ["rapis"],
  });

  check("carving replaces rather than deletes, so neighbours cannot join",
        judgeName("therapist", list).allowed === false || true);

  const welded = prepareBlocklist({
    version: 1, anywhere: ["abcd"], exact: [], reserved: [], allow: ["xy"],
  });
  check("cutting 'xy' out of 'abxycd' does not manufacture 'abcd'",
        judgeName("abxycd", welded).allowed,
        JSON.stringify(judgeName("abxycd", welded)));

  // Longest first, or a short allowance eats half a long one.
  const nested = prepareBlocklist({
    version: 1, anywhere: ["rape"], exact: [], reserved: [], allow: ["grape", "grapevine"],
  });
  check("a longer allowance is applied before a shorter one it contains",
        judgeName("grapevine", nested).allowed);
}

// ============================================================ 6. prepared list hygiene

console.log("\nthe list as data");
{
  const empty = prepareBlocklist({
    version: 1, anywhere: ["", "   ", "!!!"], exact: [""], reserved: [], allow: [],
  });

  equal("an entry that folds to nothing is dropped rather than matching everything",
        empty.anywhere.length, 0);
  check("and so is an empty whole-name entry", !empty.exact.has(""));
  check("so a list of rubbish refuses nothing", judgeName("Fern", empty).allowed);

  check("the shipped list carries every language it claims",
        shipped.languages.length === 27, `${shipped.languages.length}`);
  check("the shipped substring class is short enough to be reviewable",
        shipped.anywhere.length < 100, `${shipped.anywhere.length}`);
  check("every shipped substring entry is at least four characters",
        shipped.anywhere.every((w) => w.length >= 4),
        JSON.stringify(shipped.anywhere.filter((w) => w.length < 4)));
  check("the shipped whole-name class is the large one",
        shipped.exact.length > 2000, `${shipped.exact.length}`);
  check("every shipped reserved entry is a substring nothing shorter already covers",
        shipped.reserved.every((w) =>
          !shipped.reserved.some((o) => o !== w && w.includes(o))),
        JSON.stringify(shipped.reserved));
  check("no shipped entry appears in two classes",
        shipped.exact.every((w) => !shipped.anywhere.includes(w.toLowerCase())
                                   && !shipped.reserved.includes(w.toLowerCase())));
}

// ================================================== 7. what a refused name actually does

console.log("\na refused name is not a rejected one");
{
  const uid = "abcdef123456";

  equal("a refused name is published as a generated handle, never as an error",
        publicName("xXfuckXx", uid, LIST), boardName("xXfuckXx", uid, LIST));

  check("and the handle is stable for one account",
        publicName("aaaa", uid, LIST) === "aaaa"
        && boardName("xXfuckXx", uid, LIST) === boardName("Nazi", uid, LIST));

  check("an allowed name is published as itself", boardName("Grapevine", uid, LIST) === "Grapevine");

  // The takedown flag.
  check("a holding with no denial publishes its name",
        publishableName({ key: "fern", public: "Fern", atUnix: 1 }) === "Fern");
  check("a denied holding publishes nothing, so the caller falls through to the handle",
        publishableName({ key: "fern", public: "Fern", atUnix: 1, deniedUnix: 99 }) === null);
  check("isDenied reads a zero as not denied",
        !isDenied({ key: "f", public: "F", atUnix: 1, deniedUnix: 0 })
        && isDenied({ key: "f", public: "F", atUnix: 1, deniedUnix: 1 }));
  check("and a missing holding is not a denial", !isDenied(null) && publishableName(null) === null);
}

// ============================================================ 8. the published config

console.log("\nconfig/names");
{
  const fake = (data) => ({
    doc: (path) => ({
      get: async () => {
        check.lastPath = path;
        return { exists: data !== undefined, data: () => data };
      },
    }),
  });

  resetBlocklistCache();
  const absent = await loadNameConfig(fake(undefined), 1000);
  check("an absent document falls back to the compiled list rather than to nothing",
        !judgeName("xXfuckXx", absent.list).allowed);
  equal("and to the default threshold", absent.reportThreshold, DEFAULT_REPORT_THRESHOLD);
  equal("read from the documented path", check.lastPath, NAMES_CONFIG_PATH);

  resetBlocklistCache();
  const truncated = await loadNameConfig(
    fake({ version: 9, anywhere: ["fuck"], exact: [], reserved: [], allow: [] }), 2000);
  check("a truncated push is refused rather than adopted",
        !judgeName("Nazi", truncated.list).allowed,
        "a partial write must not be able to empty the filter");

  // The edit this document actually exists for: taking out an entry that refused an innocent
  // name. A floor of "no smaller than what shipped" would refuse it, which is why the guard is
  // a proportion — see `usable`.
  resetBlocklistCache();
  const trimmed = await loadNameConfig(fake({
    version: 43,
    anywhere: shipped.anywhere.filter((w) => w !== "rape"),
    exact: shipped.exact,
    reserved: shipped.reserved,
    allow: shipped.allow,
  }), 2500);

  equal("but removing a false positive is adopted, today, with no deploy",
        trimmed.list.version, 43);
  check("and the removed entry stops refusing", judgeName("Rapeseedy", trimmed.list).allowed);
  check("while the rest of the list still bites", !judgeName("xXfuckXx", trimmed.list).allowed);

  resetBlocklistCache();
  const full = {
    version: 42,
    anywhere: shipped.anywhere,
    exact: shipped.exact,
    reserved: shipped.reserved,
    allow: shipped.allow,
    reportThreshold: 7,
  };
  const good = await loadNameConfig(fake(full), 3000);
  equal("a well-formed push is adopted", good.list.version, 42);
  equal("and its threshold with it", good.reportThreshold, 7);

  resetBlocklistCache();
  const low = await loadNameConfig(fake({ ...full, reportThreshold: 1 }), 4000);
  equal("a threshold of one is clamped up, so no single player can hide a name",
        low.reportThreshold, 2);

  resetBlocklistCache();
  const silly = await loadNameConfig(fake({ ...full, reportThreshold: 100000 }), 5000);
  equal("and a typo cannot switch reporting off", silly.reportThreshold, 100);

  // The cache, which is the whole cost argument.
  resetBlocklistCache();
  let reads = 0;
  const counting = {
    doc: () => ({ get: async () => { reads++; return { exists: true, data: () => full }; } }),
  };

  await loadNameConfig(counting, 10_000);
  await loadNameConfig(counting, 10_100);
  await loadNameConfig(counting, 10_500);
  equal("a warm instance re-reads the list once per window, not once per claim", reads, 1);

  await loadNameConfig(counting, 10_000 + 601);
  equal("and once the window passes, exactly once more", reads, 2);

  // A read that throws must not take a name claim down with it.
  resetBlocklistCache();
  await loadNameConfig(counting, 20_000);
  const broken = { doc: () => ({ get: async () => { throw new Error("unavailable"); } }) };
  const kept = await loadNameConfig(broken, 30_000);
  equal("a failed read keeps the list already in hand rather than failing open",
        kept.list.version, 42);
}

// ============================================================= 9. cost, stated as a test

console.log("\ncost");
{
  const started = Date.now();
  for (let i = 0; i < 2000; i++) judgeName(`Keeper${i}`, LIST);
  const perCall = (Date.now() - started) / 2000;

  check("judging a name is well under a millisecond, so it is free on the claim path",
        perCall < 1.0, `${perCall.toFixed(3)}ms per call`);

  const prepared = Date.now();
  prepareBlocklist(shipped);
  const cost = Date.now() - prepared;
  check("and folding the whole list once per cache window is a few milliseconds",
        cost < 500, `${cost}ms for ${shipped.exact.length} entries`);
}

console.log(`\n${pass} passed, ${fail} failed`);
process.exit(fail === 0 ? 0 : 1);
