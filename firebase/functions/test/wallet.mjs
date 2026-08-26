// The wallet document's read path, and the two things about it that fail silently.
//
// `readWallet` is the only door onto `players/{uid}/private/wallet`, and every writer of that
// document writes it *whole* — `transaction.set(walletRef, { ...state, updatedAt })`, with no
// merge, in six different functions. So a field this function does not copy is a field the next
// sync deletes, and the deletion is invisible: nothing errors, nothing logs, and the value simply
// is not there any more. That is invariant 12a's lesson one document over from the save file, and
// it is exactly how the keeper name would have vanished from a board on the player's next daily
// chest.
//
// Run by `npm test`. No emulator: `readWallet` takes a snapshot, so a two-field fake is enough,
// and a fake is *better* here — it lets the whole-document write be replayed in three lines.

import { readFileSync } from "node:fs";
import { existsSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const REPO = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "..");
const compiled = join(REPO, "firebase", "functions", "lib", "wallet.js");

if (!existsSync(compiled)) {
  console.error("build the functions first: npm --prefix firebase/functions run build");
  process.exit(1);
}

const { readWallet, toReply } = await import(pathToFileURL(compiled).href);
const { CURRENCIES } = await import(pathToFileURL(join(REPO, "firebase", "functions", "lib", "config.js")).href);

let pass = 0, fail = 0;
const check = (ok, what, detail = "") => {
  if (ok) { pass++; console.log("  ok  ", what); }
  else { fail++; console.log("  FAIL", what, detail ? " — " + detail : ""); }
};
const equal = (what, got, want) =>
  check(Object.is(got, want) || JSON.stringify(got) === JSON.stringify(want), what,
        `expected ${JSON.stringify(want)}, got ${JSON.stringify(got)}`);

/** A document snapshot, as `readWallet` sees one. */
const snapshot = (data) => ({ exists: data !== undefined, data: () => data });

/** The seeds the real config carries. Only the shape matters here. */
const config = { seeds: Object.fromEntries(CURRENCIES.map((c) => [c, c === "credits" ? 1250 : 0])) };

/**
 * What every writer of this document actually does: take `readWallet`'s output and set the
 * document to it, whole. Replaying it is the only way to catch a field that is read but not
 * carried, because reading alone always looks correct.
 */
const wholeDocumentWrite = (doc) => {
  const state = readWallet(snapshot(doc), config);
  return JSON.parse(JSON.stringify({ ...state, updatedAt: "server-timestamp" }));
};

// ------------------------------------------------------------------ the name survives a write
console.log("\nthe keeper name survives a whole-document write");
{
  // `deniedUnix` is part of the holding, so the round trip has to carry it. It is written
  // explicitly as a zero rather than left absent for the reason `claimName` writes it that
  // way: the wallet is written with `{ merge: true }` from more than one place, and a field
  // that is sometimes present and sometimes not is a field whose absence means two things.
  const held = { key: "fernwillow", public: "Fern Willow", atUnix: 1700000000, deniedUnix: 0 };

  const once = wholeDocumentWrite({ credits: { granted: 1250, spent: 0 }, name: held });
  equal("a held name is carried through the read", once.name, held);

  // The failure this file exists for: one sync deletes it, the next publish silently re-claims
  // it, and all anybody ever sees is their name occasionally missing from a board.
  const twice = wholeDocumentWrite(once);
  equal("and through a second write", twice.name, held);

  const thrice = wholeDocumentWrite(twice);
  equal("and a third, because a player syncs all day", thrice.name, held);

  // The same failure, one field further in, and strictly worse: a takedown that a routine
  // wallet write erases. Nothing would report it — the name simply reappears on the boards the
  // next time the player opens a chest, and the moderator who hid it has no reason to look
  // again. This is invariant 12a in the document that is not the save.
  const denied = { ...held, deniedUnix: 1700009999 };
  const hidden = wholeDocumentWrite({ credits: { granted: 1250, spent: 0 }, name: denied });
  equal("a takedown survives a wallet write", hidden.name, denied);
  equal("and the one after it", wholeDocumentWrite(hidden).name, denied);

  // An older wallet, written before the field existed, must read as "not denied" rather than
  // as anything else. Zero is unreachable for a real takedown, which is what makes that safe
  // and is why no migration is needed.
  const legacy = wholeDocumentWrite({
    credits: { granted: 1250, spent: 0 },
    name: { key: "fernwillow", public: "Fern Willow", atUnix: 1700000000 },
  });
  equal("a wallet written before takedowns existed reads as not denied", legacy.name.deniedUnix, 0);
}

// ------------------------------------------------------------------ absent means absent
console.log("\nan account with no name writes no name field");
{
  const written = wholeDocumentWrite({ credits: { granted: 1250, spent: 0 } });

  // Firestore rejects `undefined` as a document value, so an unconditional copy would fail
  // *every* wallet write for every account that has never renamed — which is all of them on
  // the day this ships.
  check(!("name" in written), "no `name` key at all", JSON.stringify(written.name));

  const malformed = wholeDocumentWrite({ credits: { granted: 0, spent: 0 }, name: { public: "x" } });
  check(!("name" in malformed), "a name with no key is not carried");

  const empty = wholeDocumentWrite({ credits: { granted: 0, spent: 0 }, name: { key: "", public: "" } });
  check(!("name" in empty), "an empty key is not a held name");
}

// ------------------------------------------------------------------ the streak seed
console.log("\n'brand new' is decided by currency, not by the document existing");
{
  // The seeded floor is what stops a fresh account claiming a backlog of streak nights it never
  // played. It used to be chosen by `snapshot.exists`, which stopped being the same question the
  // moment a second feature wrote to this document: claiming a name before the first sync creates
  // the wallet, and the account would then have been read as one migrating in — which deliberately
  // has *no* floor, and so gets one unbounded first claim.
  const nameOnly = readWallet(snapshot({ name: { key: "fern", public: "Fern", atUnix: 1 } }), config);
  check(nameOnly.streak && nameOnly.streak.paidThroughDay > 0,
        "a wallet created by a name claim is still a brand-new account",
        JSON.stringify(nameOnly.streak));
  equal("and it is seeded to yesterday, not to nothing", nameOnly.streak.paidNight, 0);

  const absent = readWallet(snapshot(undefined), config);
  equal("a document that does not exist is seeded the same way",
        absent.streak.paidThroughDay, nameOnly.streak.paidThroughDay);

  // The real migration case, unchanged: an account this server has paid before, with no floor
  // recorded, keeps its one unbounded claim. Refusing it would take nights the game already
  // showed people.
  const migrating = readWallet(snapshot({ credits: { granted: 1250, spent: 100 } }), config);
  equal("an account with currency and no floor keeps the migration allowance",
        migrating.streak.paidThroughDay, 0);

  // And a recorded floor is always carried, whatever else is on the document.
  const recorded = readWallet(
    snapshot({ credits: { granted: 1250, spent: 0 }, streak: { paidThroughDay: 19000, paidNight: 4 } }),
    config);
  equal("a recorded floor survives", recorded.streak, { paidThroughDay: 19000, paidNight: 4 });
}

// ------------------------------------------------------------------ the seed itself
console.log("\nthe account seed is still granted exactly once");
{
  const fresh = readWallet(snapshot(undefined), config);
  equal("a new account is seeded", fresh.credits.granted, 1250);

  // A wallet created by a name claim has no currency recorded, so the seed still applies — the
  // grant must not be lost to a document that exists for an unrelated reason.
  const nameOnly = readWallet(snapshot({ name: { key: "fern", public: "Fern", atUnix: 1 } }), config);
  equal("and so is one created by a name claim", nameOnly.credits.granted, 1250);

  const spent = readWallet(snapshot({ credits: { granted: 1250, spent: 900 } }), config);
  equal("an account that has spent is not re-seeded", spent.credits.granted, 1250);
  equal("and keeps what it spent", spent.credits.spent, 900);
}

// ------------------------------------------------- refunded containers survive a write
console.log("\na refunded heart container survives a whole-document write");
{
  // The same failure as the keeper name, and the one with a price on it. A heart container
  // is an entitlement the *client* holds, so this list is the only thing that can take a
  // refunded one back — the phone reads it off every wallet reply and stops honouring the
  // container. Drop it on the next write and the refund silently un-happens: the player
  // keeps a heart cap they were paid back for, on every device, for ever. Buy, refund, keep
  // is the commonest way a mobile economy leaks (invariant 18c), and this list is the whole
  // of what stands in its way.
  const revoked = ["gg_heart_vessel_2"];

  const once = wholeDocumentWrite({ credits: { granted: 1250, spent: 0 }, containersRevoked: revoked });
  equal("a revoked container is carried through the read", once.containersRevoked, revoked);

  const twice = wholeDocumentWrite(once);
  equal("and through a second write", twice.containersRevoked, revoked);

  equal("and a third, because a player syncs all day",
        wholeDocumentWrite(twice).containersRevoked, revoked);

  // Two refunds, in the order `arrayUnion` would leave them.
  const both = ["gg_heart_vessel_1", "gg_heart_vessel_3"];
  equal("more than one survives together",
        wholeDocumentWrite({ credits: { granted: 1250, spent: 0 }, containersRevoked: both })
          .containersRevoked, both);

  // Absent is the overwhelmingly common case — nobody has been refunded — and it must stay
  // absent rather than becoming an empty array, because Firestore rejects `undefined` and a
  // field written onto every wallet in the world to carry nothing is a cost paid for ever.
  const none = wholeDocumentWrite({ credits: { granted: 1250, spent: 0 } });
  check(!("containersRevoked" in none),
        "an account with no refunds writes no list at all",
        JSON.stringify(none.containersRevoked));

  // A malformed list must not travel. It can only get here by hand or by a bug, and an entry
  // that is not a product id would be compared against one on every device for ever.
  const dirty = wholeDocumentWrite({
    credits: { granted: 1250, spent: 0 },
    containersRevoked: ["gg_heart_vessel_1", "", null, 7, { id: "x" }],
  });
  equal("only real ids survive a malformed list", dirty.containersRevoked, ["gg_heart_vessel_1"]);
}

console.log("\nthe bonus wheel's position survives a whole-document write");
{
  // The third field with this failure mode, and the one that decides a payout on the spot.
  // The wheel's slice is a pure function of (account, day, spin index), so the index *is* the
  // prize: drop it on the next write and every win-bonus video of the day is seeded from spin
  // zero, so a player is paid the same slice all day while the phone — which reads this index
  // back off the reply — draws a different one each time. Nothing errors and nothing logs.
  const held = { credits: { granted: 1250, spent: 0 }, wheel: { day: 20330, spins: 3 } };

  const once = wholeDocumentWrite(held);
  equal("the position is carried through the read", once.wheel, { day: 20330, spins: 3 });

  const twice = wholeDocumentWrite(once);
  equal("and through a second write", twice.wheel, { day: 20330, spins: 3 });

  equal("and a third, because a player syncs all day",
        wholeDocumentWrite(twice).wheel, { day: 20330, spins: 3 });

  // Absent stays absent for `containersRevoked`'s reason: Firestore rejects `undefined`, and a
  // field written onto every wallet in the world to carry a zero is a cost paid for ever. The
  // *reply* still answers today and zero — see below.
  const none = wholeDocumentWrite({ credits: { granted: 1250, spent: 0 } });
  check(!("wheel" in none), "an account that has never spun writes no position",
        JSON.stringify(none.wheel));

  const dirty = wholeDocumentWrite({
    credits: { granted: 1250, spent: 0 },
    wheel: { day: "yesterday", spins: -4 },
  });
  check(!("wheel" in dirty), "a malformed position does not travel", JSON.stringify(dirty.wheel));
}

console.log("\nthe reply always names the wheel, and rolls it over");
{
  // The presence of the field is the client's signal that this deployment understands the
  // wheel at all — it draws none without it and falls back to the flat offer the deployment
  // does grant. That is what removes invariant 12a's deploy-ordering hazard from the feature,
  // and it only works if a brand-new account answers (today, 0) rather than nothing.
  const today = 20331;

  const fresh = toReply(readWallet(snapshot(undefined), config), {}, {}, today);
  check(fresh.every((row) => row.wheelSpins === 0 && row.wheelDay === today),
        "a brand-new account answers today and no spins",
        JSON.stringify(fresh[0]));

  const held = toReply(
    readWallet(snapshot({ credits: { granted: 1250, spent: 0 }, wheel: { day: today, spins: 2 } }), config),
    {}, {}, today);
  check(held.every((row) => row.wheelSpins === 2 && row.wheelDay === today),
        "today's tally is reported as it stands", JSON.stringify(held[0]));

  // Rolled over in the reply as well as in the granting transaction, so a reply taken on a day
  // with no views yet answers zero rather than yesterday's tally. Without it the phone would
  // seed its first spin of the day from yesterday's index and disagree with the very grant
  // that is about to be computed from today's.
  const stale = toReply(
    readWallet(snapshot({ credits: { granted: 1250, spent: 0 }, wheel: { day: today - 1, spins: 5 } }), config),
    {}, {}, today);
  check(stale.every((row) => row.wheelSpins === 0 && row.wheelDay === today),
        "yesterday's tally reads as no spins today", JSON.stringify(stale[0]));

  // A stored day in the future is a clock that ran ahead once. Left alone rather than reset
  // into, which would hand that day's wheel out a second time.
  const ahead = toReply(
    readWallet(snapshot({ credits: { granted: 1250, spent: 0 }, wheel: { day: today + 1, spins: 4 } }), config),
    {}, {}, today);
  check(ahead.every((row) => row.wheelSpins === 4 && row.wheelDay === today + 1),
        "a day in the future is left where it is", JSON.stringify(ahead[0]));

  // Repeated on every currency row, so a reader may take it from any of them.
  check(new Set(held.map((row) => `${row.wheelDay}:${row.wheelSpins}`)).size === 1,
        "every currency row carries the same position");
}

console.log(`\n${pass} passed, ${fail} failed`);
process.exit(fail === 0 ? 0 : 1);
