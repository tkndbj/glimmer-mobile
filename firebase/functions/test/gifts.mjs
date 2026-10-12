// Admin gifts (invariant 63): currency the owner sends one account from the admin page, held on
// the wallet until claimed and paid by `claimAwards` under `gift:{giftId}:{currency}`. What is
// under contract: the id spelling (`GrantEntry.GiftId`), that a claim is paid the amount the
// wallet holds and never the client's figure, that paying one currency leaves the other waiting
// and the row goes once both are paid, that a refusal is only ever for what stays true tomorrow,
// that the pending list is bounded, that it survives a whole-document wallet write, and that
// every reply carries `gifts` (the wheel's trick).
//
// Run through `lib/`, the compiled output, so what is tested is what deploys.

import { existsSync } from "node:fs";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(HERE, "..", "..", "..");
const lib = (name) => join(REPO, "firebase", "functions", "lib", name);

if (!existsSync(lib("gifts.js"))) {
  console.error("build first: npm run build (lib/gifts.js is missing)");
  process.exit(1);
}

const G = await import(pathToFileURL(lib("gifts.js")).href);
const { readWallet, toReply } = await import(pathToFileURL(lib("wallet.js")).href);
const { CURRENCIES } = await import(pathToFileURL(lib("config.js")).href);

let failed = 0;
const check = (ok, what, detail = "") => {
  if (ok) console.log("  ok   " + what);
  else { failed++; console.log("  FAIL " + what + (detail ? "  " + detail : "")); }
};
const equal = (what, got, want) =>
  check(JSON.stringify(got) === JSON.stringify(want), what, `got ${JSON.stringify(got)}, want ${JSON.stringify(want)}`);

const NOW = 1_791_700_000;                     // 2026-10-11, any fixed instant
const ID = "g202610111530k3x9";
const OTHER = "g202610111600aaaa";

// ------------------------------------------------------------------ the ids
console.log("gifts: the id");
check(G.isGiftGrantId(`gift:${ID}:credits`), "a grant is gift:{id}:{currency}");
check(!G.isGiftGrantId(`gift:${ID}`), "two parts is not a gift grant");
check(!G.isGiftGrantId(`deal:${ID}:credits`), "nor is a deal's");
equal("the grant reads back", G.parseGiftClaim(`gift:${ID}:gems`), { giftId: ID, currency: "gems", dayKey: 0 });
equal("an unreadable gift id reads as nothing", G.parseGiftClaim("gift:x:credits"), null);
equal("a deal's id is not a gift", G.parseGiftClaim("gift:d202610111530k3x9:credits"), null);
equal("the grant id is minted in one place", G.giftGrantId(ID, "credits"), `gift:${ID}:credits`);
check(G.giftGrantId(ID, "credits").length <= 64, "it fits the 64-character id bound");

const minted = G.mintGiftId(Date.UTC(2026, 9, 11, 15, 30, 12) / 1000, Buffer.from([0, 1, 2, 35]));
equal("a minted id is g + yyyymmddhhmm + four", minted, "g202610111530abc9");
check(G.GIFT_ID.test(G.mintGiftId(NOW)), "and a random one parses");

// ------------------------------------------------------------------ the pending map
console.log("gifts: the wallet's pending map");
const pending = { [ID]: { credits: 500, gems: 20, atUnix: NOW - 60 } };
equal("a sound row reads back", G.readGiftsPending(pending), pending);
equal("a row with nothing left is dropped", G.readGiftsPending({ [ID]: { credits: 0, gems: 0, atUnix: NOW } }), {});
equal("a malformed row is dropped, the rest kept",
      G.readGiftsPending({ bad: { credits: 1, gems: 0, atUnix: NOW }, [ID]: { credits: 1.5, gems: 0, atUnix: NOW },
                           [OTHER]: { credits: 1, gems: 0, atUnix: NOW } }),
      { [OTHER]: { credits: 1, gems: 0, atUnix: NOW } });
equal("a negative amount is malformed", G.readGiftsPending({ [ID]: { credits: -1, gems: 1, atUnix: NOW } }), {});
equal("an amount past the ceiling is malformed", G.readGiftsPending({ [ID]: { credits: G.GIFT_MAX_CREDITS + 1, gems: 0, atUnix: NOW } }), {});
equal("an absent map is empty", G.readGiftsPending(undefined), {});
equal("so is a list", G.readGiftsPending([]), {});

equal("the reply lists oldest first",
      G.giftRows({ [OTHER]: { credits: 1, gems: 0, atUnix: NOW }, [ID]: { credits: 500, gems: 20, atUnix: NOW - 60 } }),
      [{ id: ID, credits: 500, gems: 20, atUnix: NOW - 60 }, { id: OTHER, credits: 1, gems: 0, atUnix: NOW }]);
equal("an empty map lists nothing", G.giftRows(undefined), []);

// ------------------------------------------------------------------ judging
console.log("gifts: a claim is paid what the wallet holds");
equal("the credits half", G.judgeGiftClaim(pending, G.parseGiftClaim(`gift:${ID}:credits`)), { kind: "pay", amount: 500 });
equal("the gems half", G.judgeGiftClaim(pending, G.parseGiftClaim(`gift:${ID}:gems`)), { kind: "pay", amount: 20 });
check(G.judgeGiftClaim(pending, G.parseGiftClaim(`gift:${OTHER}:credits`)).kind === "refuse", "a gift the wallet never held is refused");
check(G.judgeGiftClaim({ [ID]: { credits: 500, gems: 0, atUnix: NOW } }, G.parseGiftClaim(`gift:${ID}:gems`)).kind === "refuse",
      "a currency the gift does not carry is refused");
check(G.judgeGiftClaim(pending, { giftId: ID, currency: "hearts", dayKey: 0 }).kind === "refuse", "an unknown currency is refused");

const afterCredits = G.payGift(pending, ID, "credits");
equal("paying the credits leaves the gems waiting", afterCredits, { [ID]: { credits: 0, gems: 20, atUnix: NOW - 60 } });
check(!G.settlesGift(pending, ID, "credits"), "and does not settle the gift");
check(G.settlesGift(afterCredits, ID, "gems"), "paying the gems then settles it");
equal("and the row is gone", G.payGift(afterCredits, ID, "gems"), {});
equal("paying a gift that is not there changes nothing", G.payGift(pending, OTHER, "credits"), pending);
check(G.judgeGiftClaim(afterCredits, G.parseGiftClaim(`gift:${ID}:credits`)).kind === "refuse",
      "a half already paid is refused if it somehow comes back without its grant record");
equal("the reply after one half lists what is left", G.giftRows(afterCredits), [{ id: ID, credits: 0, gems: 20, atUnix: NOW - 60 }]);

// ------------------------------------------------------------------ the admin
console.log("gifts: the admin's request");
equal("coins alone", G.readGiftRequest({ credits: 500 }), { ok: true, request: { credits: 500, gems: 0 } });
equal("gems alone", G.readGiftRequest({ gems: 20 }), { ok: true, request: { credits: 0, gems: 20 } });
equal("both", G.readGiftRequest({ credits: 500, gems: 20 }), { ok: true, request: { credits: 500, gems: 20 } });
check(!G.readGiftRequest({}).ok, "nothing is refused");
check(!G.readGiftRequest({ credits: 0, gems: 0 }).ok, "two noughts are refused");
check(!G.readGiftRequest({ credits: -5 }).ok, "a negative amount is refused");
check(!G.readGiftRequest({ credits: 1.5 }).ok, "a fraction is refused");
check(!G.readGiftRequest({ credits: "500" }).ok, "a string is refused");
check(!G.readGiftRequest({ gems: G.GIFT_MAX_GEMS + 1 }).ok, "past the ceiling is refused");

const made = G.createGift({}, { credits: 500, gems: 20 }, NOW, ID);
equal("a gift is added to the pending map", made, {
  ok: true, id: ID, gift: { credits: 500, gems: 20, atUnix: NOW }, pending: { [ID]: { credits: 500, gems: 20, atUnix: NOW } },
});
check(!G.createGift(made.pending, { credits: 1, gems: 0 }, NOW, ID).ok, "a taken id is refused");
const full = {};
for (let i = 0; i < G.GIFT_MAX_PENDING; i++) full[`g202610111530a${String(i).padStart(3, "0")}`] = { credits: 1, gems: 0, atUnix: NOW };
check(Object.keys(full).length === G.GIFT_MAX_PENDING, "a full map for the bound");
check(!G.createGift(full, { credits: 1, gems: 0 }, NOW, OTHER).ok, `the ${G.GIFT_MAX_PENDING}th waiting gift refuses another`);

// ------------------------------------------------------------------ the wallet
console.log("gifts: the wallet carries the pending map");
const snapshot = (data) => ({ exists: data !== undefined, data: () => data });
const config = { seeds: Object.fromEntries(CURRENCIES.map((c) => [c, c === "credits" ? 1250 : 0])) };
const doc = {
  credits: { granted: 10, spent: 0, confirmedThroughUnix: 0, earnedFloor: 0 },
  gems: { granted: 0, spent: 0, confirmedThroughUnix: 0, earnedFloor: 0 },
  gifts: pending,
};
const written = JSON.parse(JSON.stringify({ ...readWallet(snapshot(doc), config), updatedAt: "stamp" }));
equal("a whole-document write keeps the gifts", written.gifts, pending);
const twice = JSON.parse(JSON.stringify({ ...readWallet(snapshot(written), config), updatedAt: "stamp" }));
equal("and a second one", twice.gifts, pending);
check(readWallet(snapshot({ ...doc, gifts: {} }), config).gifts === undefined, "an empty map is not written (Firestore refuses undefined)");

const reply = toReply(readWallet(snapshot(doc), config), {});
check(reply.every((row) => Array.isArray(row.gifts)), "every reply row carries gifts");
equal("and lists the pending gift", reply[0].gifts, [{ id: ID, credits: 500, gems: 20, atUnix: NOW - 60 }]);
equal("a fresh wallet carries an empty list, never nothing", toReply(readWallet(snapshot(undefined), config), {})[0].gifts, []);

console.log(failed === 0 ? "\ngifts: all green" : `\ngifts: ${failed} FAILED`);
process.exit(failed === 0 ? 0 : 1);
