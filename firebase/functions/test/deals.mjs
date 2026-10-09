// Limited-time shop deals (invariant 60): coins sold for gems for a window, made on the admin page
// and published in `config/deals`. What is under contract: the two id spellings
// (`SpendEntry.ShopDealId`, `GrantEntry.ShopDealId`), that a debit is priced off the published
// deal and never the client's figure, that a claim is never paid and only waits while its debit
// can still arrive, that only the owner's three Google addresses may make or end a deal, that one
// deal is on sale at a time, and that the wallet carries what was bought.
//
// Run through `lib/`, the compiled output, so what is tested is what deploys.

import { existsSync } from "node:fs";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(HERE, "..", "..", "..");
const lib = (name) => join(REPO, "firebase", "functions", "lib", name);

if (!existsSync(lib("deals.js"))) {
  console.error("build first: npm run build (lib/deals.js is missing)");
  process.exit(1);
}

const D = await import(pathToFileURL(lib("deals.js")).href);
const { readWallet, toReply } = await import(pathToFileURL(lib("wallet.js")).href);

let failed = 0;
const check = (ok, what, detail = "") => {
  if (ok) console.log("  ok   " + what);
  else { failed++; console.log("  FAIL " + what + (detail ? "  " + detail : "")); }
};
const equal = (what, got, want) =>
  check(JSON.stringify(got) === JSON.stringify(want), what, `got ${JSON.stringify(got)}, want ${JSON.stringify(want)}`);

const NOW = 1_791_000_000;                     // 2026-10-03, any fixed instant
const ID = "d202610091530k3x9";
const deal = { id: ID, credits: 26000, gems: 900, startUnix: NOW - 60, endUnix: NOW + 3600 };

// ------------------------------------------------------------------ the ids
console.log("deals: the two ids");
check(D.isDealSpendId(`deal:${ID}`), "a debit is deal:{id}");
check(!D.isDealSpendId(`deal:${ID}:credits`), "a grant id is not a debit");
equal("the debit names its deal", D.parseDealSpendId(`deal:${ID}`), ID);
equal("an unreadable deal id names nothing", D.parseDealSpendId("deal:Bad-Id"), null);
equal("nor does an empty one", D.parseDealSpendId("deal:"), null);
check(D.isDealGrantId(`deal:${ID}:credits`), "a grant is deal:{id}:{currency}");
equal("the grant reads back", D.parseDealClaim(`deal:${ID}:credits`), { dealId: ID, currency: "credits", dayKey: 0 });
equal("an unreadable grant reads as nothing", D.parseDealClaim("deal:x:credits"), null);
equal("the grant id is minted in one place", D.dealGrantId(ID), `deal:${ID}:credits`);
check(`deal:${ID}`.length <= 64 && D.dealGrantId(ID).length <= 64, "both fit the 64-character id bound");

const minted = D.mintDealId(Date.UTC(2026, 9, 9, 15, 30, 12) / 1000, Buffer.from([0, 1, 2, 35]));
equal("a minted id is d + yyyymmddhhmm + four", minted, "d202610091530abc9");
check(D.parseDealSpendId(`deal:${D.mintDealId(NOW)}`) !== null, "and a random one parses");

// ------------------------------------------------------------------ the document
console.log("deals: the published document");
equal("a sound document lists its deal", D.usableDeals({ schema: 1, deals: [deal] }), [deal]);
equal("another schema lists nothing", D.usableDeals({ schema: 2, deals: [deal] }), []);
equal("an absent document lists nothing", D.usableDeals(undefined), []);
equal("a malformed row is dropped, the rest kept",
      D.usableDeals({ schema: 1, deals: [{ ...deal, gems: 0 }, deal] }), [deal]);
equal("a fractional amount is malformed", D.readDeal({ ...deal, credits: 1.5 }), null);
equal("an amount past the ceiling is malformed", D.readDeal({ ...deal, credits: D.DEAL_MAX_CREDITS + 1 }), null);
equal("a window of nought is malformed", D.readDeal({ ...deal, endUnix: deal.startUnix }), null);
equal("a duplicated id is listed once", D.usableDeals({ schema: 1, deals: [deal, deal] }).length, 1);

check(D.isLive(deal, NOW), "on sale inside its window");
check(!D.isLive(deal, deal.endUnix), "and not at its end");
check(D.isPayable(deal, deal.endUnix + D.DEAL_GRACE_SECONDS - 1), "a debit just after the end is still honoured");
check(!D.isPayable(deal, deal.endUnix + D.DEAL_GRACE_SECONDS), "and not after the grace");
check(!D.isPayable(deal, deal.startUnix - 1), "nor before it opens");

// ------------------------------------------------------------------ the debit
console.log("deals: a debit is priced off the deal");
equal("the price in gems buys it", D.judgeDealSpend(deal, "gems", 900, NOW), { ok: true, deal });
check(D.judgeDealSpend(deal, "gems", 1000, NOW).ok, "overpaying is still buying");
check(!D.judgeDealSpend(deal, "gems", 899, NOW).ok, "underpaying is refused");
check(!D.judgeDealSpend(deal, "credits", 900, NOW).ok, "credits are refused");
check(!D.judgeDealSpend(null, "gems", 900, NOW).ok, "an unknown deal is refused");
check(!D.judgeDealSpend(deal, "gems", 900, deal.endUnix + D.DEAL_GRACE_SECONDS).ok, "a closed deal is refused");

// ------------------------------------------------------------------ the claim
console.log("deals: a claim waits or is refused, never paid");
const claim = D.parseDealClaim(`deal:${ID}:credits`);
equal("waits while the debit can still arrive", D.judgeDealClaim(deal, claim, NOW).kind, "wait");
equal("refused once the deal takes no debits",
      D.judgeDealClaim(deal, claim, deal.endUnix + D.DEAL_GRACE_SECONDS).kind, "refuse");
equal("refused for a deal nobody published", D.judgeDealClaim(null, claim, NOW).kind, "refuse");
equal("refused in gems", D.judgeDealClaim(deal, { ...claim, currency: "gems" }, NOW).kind, "refuse");

// ------------------------------------------------------------------ the admin
console.log("deals: only the owner's Google accounts manage deals");
const token = (email, verified = true, provider = "google.com") =>
  ({ email, email_verified: verified, firebase: { sign_in_provider: provider } });
for (const email of ["tekin.dabaj@outlook.com", "tekin_dabaj@hotmail.com", "arcadetkn@gmail.com"]) {
  check(D.isDealAdmin(token(email)), `${email} may`);
}
check(D.isDealAdmin(token("ArcadeTKN@Gmail.com")), "the address is compared without case");
check(!D.isDealAdmin(token("tekin.dabaj@gmail.com")), "a near spelling may not");
check(!D.isDealAdmin(token("arcadetkn@gmail.com", false)), "an unverified address may not");
check(!D.isDealAdmin(token("arcadetkn@gmail.com", true, "password")), "nor one from another provider");
check(!D.isDealAdmin(token("arcadetkn@gmail.com", true, "anonymous")), "nor an anonymous player");
check(!D.isDealAdmin(undefined), "nor nobody");

console.log("deals: the request");
equal("a sound request reads", D.readDealRequest({ credits: 26000, gems: 900, durationSeconds: 86400 }),
      { ok: true, request: { credits: 26000, gems: 900, durationSeconds: 86400 } });
check(!D.readDealRequest({ credits: 0, gems: 900, durationSeconds: 86400 }).ok, "no coins is refused");
check(!D.readDealRequest({ credits: 26000, gems: -1, durationSeconds: 86400 }).ok, "negative gems is refused");
check(!D.readDealRequest({ credits: 26000, gems: 900, durationSeconds: 60 }).ok, "a minute is too short");
check(!D.readDealRequest({ credits: 26000, gems: 900, durationSeconds: D.DEAL_MAX_SECONDS + 1 }).ok, "past ninety days is too long");
check(!D.readDealRequest({ credits: "26000", gems: 900, durationSeconds: 86400 }).ok, "a string is refused");
check(!D.readDealRequest(null).ok, "and so is nothing");

console.log("deals: making and ending");
const request = { credits: 26000, gems: 900, durationSeconds: 7200 };
const made = D.createDeal([], request, NOW, ID);
check(made.ok, "a deal is made");
equal("it opens now and runs the duration", made.deal, { id: ID, credits: 26000, gems: 900, startUnix: NOW, endUnix: NOW + 7200 });
let several = made.deals;
for (let i = 1; i < D.DEAL_MAX_LIVE; i++) {
  const next = D.createDeal(several, request, NOW + i, `d20261009153${i}aaaa`);
  check(next.ok, `deal ${i + 1} of ${D.DEAL_MAX_LIVE} may be on sale beside the others`);
  several = next.ok ? next.deals : several;
}
equal("all of them are live", D.liveDeals(several, NOW + D.DEAL_MAX_LIVE).length, D.DEAL_MAX_LIVE);
check(!D.createDeal(several, request, NOW + 10, "d202610091540aaaa").ok, `a ${D.DEAL_MAX_LIVE + 1}th is refused while ${D.DEAL_MAX_LIVE} are on sale`);
const freed = D.endDeal(several, ID, NOW + 20);
check(freed.ok && D.createDeal(freed.deals, request, NOW + 21, "d202610091541aaaa").ok, "ending one makes room for another");
check(D.createDeal(several, request, NOW + 7200, "d202610091731aaaa").ok, "and all may be replaced once they have ended");

const ended = D.endDeal(made.deals, ID, NOW + 100);
check(ended.ok, "a live deal ends");
equal("only its end moved, and the mark", ended.deal, { ...made.deal, endUnix: NOW + 100, endedEarly: true });
check(!D.endDeal(ended.deals, ID, NOW + 101).ok, "an ended deal cannot end again");
equal("it is marked as ended early", ended.deal.endedEarly, true);
equal("and reads back so", D.readDeal(ended.deal)?.endedEarly, true);
check(!D.isPayable(ended.deal, NOW + 100), "a deal ended early takes no debit from that second - no grace");
check(!D.judgeDealSpend(ended.deal, "gems", 900, NOW + 101).ok, "so a debit right after End now is refused");
equal("and its waiting claim is refused", D.judgeDealClaim(ended.deal, D.parseDealClaim(`deal:${ID}:credits`), NOW + 101).kind, "refuse");
check(D.isSettled(ended.deal, NOW + 100), "and its buyer count is final at once");
check(D.isPayable(deal, deal.endUnix + 1), "a deal that ran out keeps its grace");
check(!("endedEarly" in made.deal), "a new deal carries no endedEarly key (Firestore refuses undefined)");
check(!("endedEarly" in D.readDeal({ ...made.deal, endedEarly: false })), "and false is never written back");
check(!D.endDeal([], ID, NOW).ok, "an unknown deal cannot end");
const instant = D.endDeal(made.deals, ID, NOW);
check(instant.ok && D.readDeal(instant.deal) !== null, "ending in the second it opened still reads back");

const old = { ...deal, id: "d202601010000aaaa", startUnix: NOW - 90 * 86400, endUnix: NOW - D.DEAL_KEEP_SECONDS - 1 };
equal("a deal ended past the keep is pruned", D.pruneDeals([old, deal], NOW), [deal]);
const many = Array.from({ length: D.DEAL_MAX_LISTED + 5 }, (_, i) =>
  ({ ...deal, id: `d2026100915${String(i).padStart(2, "0")}aaaa`, startUnix: NOW - 1000 + i, endUnix: NOW - 500 + i }));
const pruned = D.pruneDeals(many, NOW);
equal("the list is bounded", pruned.length, D.DEAL_MAX_LISTED);
equal("keeping the newest", pruned[pruned.length - 1].id, many[many.length - 1].id);

// ------------------------------------------------------------------ the history
console.log("deals: the history");
check(!D.isSettled(deal, deal.endUnix), "a deal is not settled at its end - a debit can still land");
check(!D.isSettled(deal, deal.endUnix + D.DEAL_GRACE_SECONDS - 1), "nor inside the grace");
check(D.isSettled(deal, deal.endUnix + D.DEAL_GRACE_SECONDS), "and is once no debit can be honoured");
equal("a cursor reads back", D.readHistoryCursor({ endUnix: NOW, id: ID }), { endUnix: NOW, id: ID });
equal("no cursor is the first page", D.readHistoryCursor(undefined), null);
equal("a junk id is the first page", D.readHistoryCursor({ endUnix: NOW, id: "x" }), null);
equal("a junk time is the first page", D.readHistoryCursor({ endUnix: "1", id: ID }), null);
equal("a page is twenty rows", D.DEAL_HISTORY_PAGE, 20);

// ------------------------------------------------------------------ the wallet
console.log("deals: the wallet carries what was bought");
const snapshot = (data) => ({ exists: data !== undefined, data: () => data });
const config = { seeds: { credits: 1250, gems: 12 } };
const nowUnix = Math.floor(Date.now() / 1000);
const bought = {
  credits: { granted: 1250, spent: 0 }, gems: { granted: 12, spent: 0 },
  deals: { [ID]: nowUnix + 3600, d202601010000aaaa: nowUnix - D.DEAL_KEEP_SECONDS - 10, "not an id": nowUnix },
};
const wallet = readWallet(snapshot(bought), config);
equal("a bought deal survives a read; a pruned one and garbage do not", wallet.deals, { [ID]: nowUnix + 3600 });
equal("every reply row names it", toReply(wallet, {}).map((row) => row.dealsBought), [[ID], [ID]]);
const none = readWallet(snapshot({ credits: { granted: 1, spent: 0 }, gems: { granted: 1, spent: 0 } }), config);
check(!("deals" in none), "an account that never bought writes no field (Firestore refuses undefined)");
equal("and answers an empty list, present", toReply(none, {})[0].dealsBought, []);

if (failed > 0) {
  console.error(`\ndeals: ${failed} check(s) failed`);
  process.exit(1);
}
console.log("\ndeals: all checks passed");
