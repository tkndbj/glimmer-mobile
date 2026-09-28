// The server half of keeper levels bought outright (invariant 57), against the shared vectors.
//
// What is under contract, and why it is pinned rather than argued: the price of a level is
// decided on the device and here, and a drift is a purchase refused as underpaid - the client
// drops the debit and takes the level back, so a player who was shown a price loses the level
// and keeps the money. `Tools/make_keeper_vectors.py` writes the cases from the prose rule; this
// file and `KeeperLevelTests` are the two implementations held to them.
//
// Run through `lib/`, the compiled output, so what is tested is what deploys.

import { readFileSync, existsSync } from "node:fs";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(HERE, "..", "..", "..");
const compiled = join(REPO, "firebase", "functions", "lib", "keeper.js");

if (!existsSync(compiled)) {
  console.error("build first: npm run build (lib/keeper.js is missing)");
  process.exit(1);
}

const {
  between, earnedKeeperLevel, isKeeperSpendId, judgeKeeperSpend, keeperBoughtOf, keeperPrice,
  parseKeeperSpendId, usableKeeperLadder, KEEPER_MAX_TOP, KEEPER_MAX_ANCHORS, KEEPER_MAX_PRICE,
} = await import(pathToFileURL(compiled).href);

const vectors = JSON.parse(readFileSync(join(REPO, "firebase", "shared", "grove-vectors.json"), "utf8"));

let failed = 0;
const check = (ok, what, detail = "") => {
  if (ok) console.log("  ok   " + what);
  else { failed++; console.log("  FAIL " + what + (detail ? "  " + detail : "")); }
};
const equal = (what, got, want) =>
  check(JSON.stringify(got) === JSON.stringify(want), what, `got ${JSON.stringify(got)}, want ${JSON.stringify(want)}`);

console.log("keeper: the limits are the client's");
equal("top", KEEPER_MAX_TOP, 200);
equal("anchors", KEEPER_MAX_ANCHORS, 16);
equal("price", KEEPER_MAX_PRICE, 10_000_000);

console.log("\nkeeper: the ladders resolve");
const ladders = {};
for (const [name, block] of Object.entries(vectors.keeperPriceLadders)) {
  ladders[name] = usableKeeperLadder(block);
  check(ladders[name] !== null, `the '${name}' ladder is usable`);
  equal(`and keeps its top`, ladders[name]?.top, block.top);
  equal(`and its anchors`, ladders[name]?.anchors, block.anchors);
}

console.log("\nkeeper: every price case");
for (const c of vectors.keeperPriceCases) {
  const got = keeperPrice(ladders[c.ladder], c.level);
  if (!c.sold) equal(c.name, got, null);
  else equal(c.name, got, { currency: c.currency, price: c.price });
}

console.log("\nkeeper: every refused block resolves to nothing");
for (const r of vectors.keeperPriceRejected) {
  equal(r.name, usableKeeperLadder(r.block), null);
}
equal("a null ladder prices nothing", keeperPrice(null, 5), null);
equal("a fractional level prices nothing", keeperPrice(ladders.shipped, 5.5), null);

console.log("\nkeeper: the arithmetic");
equal("a whole step", between(100, 250, 1, 3), 150);
equal("a half rounds up", between(10, 25, 3, 6), 18);
equal("nought steps is the foot", between(10, 25, 0, 6), 10);
equal("all the steps is the top", between(10, 25, 6, 6), 25);
equal("no steps at all is the foot", between(10, 25, 0, 0), 10);
equal("a flat band is flat", between(500, 500, 1, 2), 500);

console.log("\nkeeper: every spend id");
for (const s of vectors.keeperSpendIds) {
  const got = parseKeeperSpendId(s.id);
  if (s.invalid) equal(`'${s.id}' is refused`, got, null);
  else equal(`'${s.id}' reads back`, got, { ordinal: s.ordinal, level: s.level });
}
equal("is-keeper reads the prefix", isKeeperSpendId("keeper:1:2"), true);
equal("and refuses another prefix", isKeeperSpendId("chaltier:gold:1:1"), false);
equal("and a non-string", isKeeperSpendId(7), false);

console.log("\nkeeper: the verdict on a debit");
{
  const ladder = ladders.shipped;
  const first = keeperPrice(ladder, 2);          // credits
  const gem = keeperPrice(ladder, 11);           // the first gem level

  equal("the first level, paid in full, is bought",
        judgeKeeperSpend(ladder, { ordinal: 1, level: 2 }, 0, 1, first.currency, first.price),
        { ok: true, currency: first.currency, price: first.price });
  equal("and overpaying is fine",
        judgeKeeperSpend(ladder, { ordinal: 1, level: 2 }, 0, 1, first.currency, first.price + 1).ok, true);
  equal("underpaid by one is refused",
        judgeKeeperSpend(ladder, { ordinal: 1, level: 2 }, 0, 1, first.currency, first.price - 1),
        { ok: false, reason: "underpaid", price: first.price });
  equal("the wrong currency is refused",
        judgeKeeperSpend(ladder, { ordinal: 1, level: 2 }, 0, 1, "gems", 1_000_000).reason, "underpaid");
  equal("skipping an ordinal is refused",
        judgeKeeperSpend(ladder, { ordinal: 2, level: 3 }, 0, 1, first.currency, first.price).reason, "out_of_order");
  equal("an ordinal already held is refused",
        judgeKeeperSpend(ladder, { ordinal: 1, level: 2 }, 1, 1, first.currency, first.price).reason, "out_of_order");
  equal("a level below what the save proves is refused",
        judgeKeeperSpend(ladder, { ordinal: 1, level: 5 }, 0, 9, first.currency, first.price).reason, "under_earned");
  equal("a level above what the save proves is never refused for it",
        judgeKeeperSpend(ladder, { ordinal: 1, level: 11 }, 0, 4, gem.currency, gem.price).ok, true);
  equal("a level the ladder does not sell is refused",
        judgeKeeperSpend(ladder, { ordinal: 1, level: ladder.top + 1 }, 0, ladder.top, "gems", 1_000_000).reason, "not_sold");
  equal("no ladder at all sells nothing",
        judgeKeeperSpend(null, { ordinal: 1, level: 2 }, 0, 1, first.currency, first.price).reason, "not_sold");
  equal("the ordinal is judged before the price, so a stale device learns the right thing",
        judgeKeeperSpend(null, { ordinal: 3, level: 4 }, 0, 1, "gems", 1).reason, "out_of_order");
}

console.log("\nkeeper: the wallet's count");
equal("a count reads back", keeperBoughtOf({ keeperBought: 4 }), 4);
equal("a fraction floors", keeperBoughtOf({ keeperBought: 4.9 }), 4);
equal("a negative is nought", keeperBoughtOf({ keeperBought: -3 }), 0);
equal("a string is nought", keeperBoughtOf({ keeperBought: "4" }), 0);
equal("absent is nought", keeperBoughtOf({}), 0);
equal("no document is nought", keeperBoughtOf(undefined), 0);

console.log("\nkeeper: the earned level is XP alone");
{
  const config = { version: 1, rewards: { xpFirstClear: 60, xpPerStar: 30, creditsFirstClear: 0, creditsPerStar: 0 },
                   chapterRewards: {}, levelChapters: { g1: "c1" } };
  equal("an empty save stands at level 1", earnedKeeperLevel({}, config), 1);
  const played = { levels: { g1: { stars: 3, cleared: true } } };
  check("a cleared glade lifts it", earnedKeeperLevel(played, config) >= 1);
  equal("and a bought count on the wallet moves nothing here",
        earnedKeeperLevel({ ...played, wallet: { keeperLevelsBought: 40 } }, config),
        earnedKeeperLevel(played, config));
}

console.log(failed === 0 ? "\nkeeper: all green" : `\nkeeper: ${failed} FAILED`);
process.exit(failed === 0 ? 0 : 1);
