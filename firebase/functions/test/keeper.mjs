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
  KEEPER_MILESTONE_SEED_TAG, KEEPER_MILESTONE_MAX_ROWS, isMilestoneGrantId, judgeMilestoneClaim,
  milestoneAt, milestoneChestValue, milestoneSubject, parseMilestoneClaim, rollMilestoneChest,
  usableKeeperMilestones,
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

// ------------------------------------------------------------------ the milestones (57d)
console.log("\nmilestone: the contract is the client's");
equal("seed tag", KEEPER_MILESTONE_SEED_TAG, "milestone");
equal("row ceiling", KEEPER_MILESTONE_MAX_ROWS, 64);
equal("the subject is the level alone", milestoneSubject(4), "4");

console.log("\nmilestone: every chest vector");
const milestoneTiers = { tiers: (vectors.keeperMilestoneChestTiers ?? []).map((t) => ({ id: t.id, chest: t.chest })),
                         activePerPeriod: 3, daily: [], weekly: [] };
check(milestoneTiers.tiers.length > 0, "the vector file carries milestone chest tiers");
const milestoneChests = new Map(milestoneTiers.tiers.map((t) => [t.id, t.chest]));
let milestoneChestFailures = 0;
for (const c of vectors.keeperMilestoneChestCases ?? []) {
  const chest = milestoneChests.get(c.tier);
  const rolled = chest ? rollMilestoneChest(chest, c.playerKey, c.level) : [];
  const got = rolled.map((d) => `${d.kind}${d.item ? ":" + d.item : ""}=${d.amount}`).join(",");
  const want = (c.drops ?? []).map((d) => `${d.kind}${d.item ? ":" + d.item : ""}=${d.amount}`).join(",");
  if (got !== want) { milestoneChestFailures++; failed++; console.log(`  FAIL ${c.name}: expected ${want}, got ${got}`); }
}
console.log(`  ${(vectors.keeperMilestoneChestCases ?? []).length - milestoneChestFailures}/${(vectors.keeperMilestoneChestCases ?? []).length} milestone chest vector(s) ok`);
check(milestoneSubject(4) !== milestoneSubject(5), "neighbouring levels roll apart");

console.log("\nmilestone: every claim id");
for (const s of vectors.keeperMilestoneClaimIds ?? []) {
  const got = parseMilestoneClaim(s.id);
  if (s.invalid) equal(`'${s.id}' is refused`, got, null);
  else equal(`'${s.id}' reads back`, got, { level: s.level, currency: s.currency, dayKey: s.level });
}
check(!isMilestoneGrantId("keeper:1:4"), "a keeper debit is not a milestone grant");

console.log("\nmilestone: every refused block resolves to nothing");
for (const r of vectors.keeperMilestoneRejected ?? []) {
  equal(r.name, usableKeeperMilestones(r.block, milestoneTiers), null);
}
equal("a block against no tasks table resolves to nothing", usableKeeperMilestones({ rows: [{ level: 4, tier: "wood" }] }, null), null);

console.log("\nmilestone: the shipped block resolves against the shipped tiers");
const shippedProgression = JSON.parse(readFileSync(join(REPO, "Assets", "StreamingAssets", "Content", "progression.json"), "utf8"));
const shippedTasks = { tiers: shippedProgression.tasks.tiers, activePerPeriod: 3, daily: [], weekly: [] };
const shippedMilestones = usableKeeperMilestones(shippedProgression.keeperMilestones, shippedTasks);
check(shippedMilestones !== null, "progression.json's keeperMilestones block is usable");
equal("and is the one the vectors pin", shippedMilestones?.rows, vectors.keeperMilestoneShipped?.rows);
equal("and level 4 is a milestone", milestoneAt(shippedMilestones, 4)?.level, 4);
equal("and level 5 is not", milestoneAt(shippedMilestones, 5), null);

console.log("\nmilestone: the judgement");
const ms = usableKeeperMilestones({ rows: [{ level: 4, tier: "wood" }, { level: 8, tier: "gold" }] }, milestoneTiers);
const claim4 = parseMilestoneClaim("milestone:4:credits");
const claim8 = parseMilestoneClaim("milestone:8:credits");
const claim6 = parseMilestoneClaim("milestone:6:credits");
equal("reached by play", judgeMilestoneClaim(ms, milestoneTiers, claim4, 4, 0).kind, "ok");
equal("reached by purchase", judgeMilestoneClaim(ms, milestoneTiers, claim4, 1, 3).kind, "ok");
equal("reached by both", judgeMilestoneClaim(ms, milestoneTiers, claim8, 5, 3).kind, "ok");
equal("carries the tier", judgeMilestoneClaim(ms, milestoneTiers, claim8, 8, 0).tier?.id, "gold");
equal("one level short is unknown, never refused", judgeMilestoneClaim(ms, milestoneTiers, claim4, 1, 2).kind, "unknown");
equal("no milestone at that level is unknown", judgeMilestoneClaim(ms, milestoneTiers, claim6, 50, 0).kind, "unknown");
equal("no block at all is unknown", judgeMilestoneClaim(null, milestoneTiers, claim4, 50, 0).kind, "unknown");
equal("a tier the tasks block lost is unknown",
      judgeMilestoneClaim(ms, { ...milestoneTiers, tiers: milestoneTiers.tiers.filter((t) => t.id !== "gold") }, claim8, 50, 0).kind, "unknown");
equal("a negative bought count is nought", judgeMilestoneClaim(ms, milestoneTiers, claim4, 4, -9).kind, "ok");
equal("a fractional earned level floors", judgeMilestoneClaim(ms, milestoneTiers, claim4, 3.9, 0).kind, "unknown");

console.log("\nmilestone: the value is the vector's roll in one currency");
const royalCase = (vectors.keeperMilestoneChestCases ?? []).find((c) => c.tier === "royal" && c.playerKey === "uid_abc123" && c.level === 4);
check(royalCase !== undefined, "a royal case for uid_abc123 at level 4 exists");
if (royalCase) {
  const credits = (royalCase.drops.find((d) => d.kind === "credits") ?? { amount: 0 }).amount;
  const gems = (royalCase.drops.find((d) => d.kind === "gems") ?? { amount: 0 }).amount;
  equal("credits", milestoneChestValue(milestoneChests.get("royal"), "uid_abc123", 4, "credits"), credits);
  equal("gems", milestoneChestValue(milestoneChests.get("royal"), "uid_abc123", 4, "gems"), gems);
  equal("hearts, which it holds none of", milestoneChestValue(milestoneChests.get("royal"), "uid_abc123", 4, "hearts"), 0);
}

console.log(failed === 0 ? "\nkeeper: all green" : `\nkeeper: ${failed} FAILED`);
process.exit(failed === 0 ? 0 : 1);
