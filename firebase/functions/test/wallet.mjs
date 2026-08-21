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

const { readWallet } = await import(pathToFileURL(compiled).href);
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
  const held = { key: "fernwillow", public: "Fern Willow", atUnix: 1700000000 };

  const once = wholeDocumentWrite({ credits: { granted: 1250, spent: 0 }, name: held });
  equal("a held name is carried through the read", once.name, held);

  // The failure this file exists for: one sync deletes it, the next publish silently re-claims
  // it, and all anybody ever sees is their name occasionally missing from a board.
  const twice = wholeDocumentWrite(once);
  equal("and through a second write", twice.name, held);

  const thrice = wholeDocumentWrite(twice);
  equal("and a third, because a player syncs all day", thrice.name, held);
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

console.log(`\n${pass} passed, ${fail} failed`);
process.exit(fail === 0 ? 0 : 1);
