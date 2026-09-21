#!/usr/bin/env node
/**
 * The Infinite lane's credit bound.
 *
 *     npm --prefix firebase/functions test
 *
 * This is the only payment `claimAwards` honours that the server cannot re-price for itself:
 * a wave count comes out of a run no server saw. So the amount is the client's, and what
 * stands between it and the turret shelf is the day's ceiling in `endlessGrant` and the parse
 * that decides which day a claim names. Both are tested here, because both fail silently —
 * a loose parse pays the wrong day's ceiling and a loose clamp pays whatever was asked for,
 * and in each case the reply looks perfectly ordinary.
 */

import { existsSync } from "node:fs";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(HERE, "..", "..", "..");
const compiled = join(REPO, "firebase", "functions", "lib", "endless.js");

if (!existsSync(compiled)) {
  console.error("build the functions first: npm --prefix firebase/functions run build");
  process.exit(1);
}

const {
  endlessGrant, isEndlessGrantId, parseEndlessClaim, readEndlessDay,
  MAX_ENDLESS_DAYS_AHEAD, MAX_ENDLESS_DAYS_BEHIND,
} = await import(pathToFileURL(compiled).href);

let pass = 0, fail = 0;
const check = (ok, what, detail = "") => {
  if (ok) { pass++; console.log("  ok  ", what); }
  else { fail++; console.log("  FAIL", what, detail ? " — " + detail : ""); }
};
const equal = (what, got, want) =>
  check(JSON.stringify(got) === JSON.stringify(want), what,
        `expected ${JSON.stringify(want)}, got ${JSON.stringify(got)}`);

const CAP = 10000;
const TODAY = 20420;

// ------------------------------------------------------------------ telling the id apart
console.log("\nthe claim id");
{
  check(isEndlessGrantId("endless:20420:600:credits"), "an endless id is recognised");
  check(!isEndlessGrantId("daily:20420:0:credits"), "a daily id is not");
  check(!isEndlessGrantId("streak:20420:3:credits"), "a streak night is not");
  check(!isEndlessGrantId("endlessly:1:2:credits"), "a prefix that merely starts the same is not");

  equal("a well-formed id parses",
        parseEndlessClaim("endless:20420:600:credits"),
        { dayKey: 20420, paidBefore: 600, currency: "credits" });

  equal("a first claim of the day parses",
        parseEndlessClaim("endless:20420:0:credits"),
        { dayKey: 20420, paidBefore: 0, currency: "credits" });

  // **Two spellings of one day would double the bound**, which is the whole reason the parse
  // is strict rather than `Number`. Each of these is accepted by `Number` and refused here.
  for (const id of ["endless:+20420:0:credits", "endless: 20420:0:credits",
                    "endless:2.042e4:0:credits", "endless:-1:0:credits",
                    "endless:20420:-5:credits", "endless:20420:0.5:credits"]) {
    check(parseEndlessClaim(id) === null, `refused a loose spelling: ${id}`);
  }

  check(parseEndlessClaim("endless:20420:0") === null, "refused an id missing its currency");
  check(parseEndlessClaim("endless:20420:0:credits:extra") === null, "refused an id with a fifth part");
  check(parseEndlessClaim("endless:20420:0:") === null, "refused an empty currency");
}

// ------------------------------------------------------------------ the day's ceiling
console.log("\nthe day's ceiling");
{
  equal("a first run of the day is paid in full",
        endlessGrant(600, TODAY, CAP, null),
        { amount: 600, day: { day: TODAY, paid: 600 } });

  equal("a second run adds to the day",
        endlessGrant(600, TODAY, CAP, { day: TODAY, paid: 600 }),
        { amount: 600, day: { day: TODAY, paid: 1200 } });

  equal("a run that would cross the ceiling is cut to what is left",
        endlessGrant(600, TODAY, CAP, { day: TODAY, paid: 9700 }),
        { amount: 300, day: { day: TODAY, paid: CAP } });

  equal("a spent day pays nothing",
        endlessGrant(600, TODAY, CAP, { day: TODAY, paid: CAP }),
        { amount: 0, day: { day: TODAY, paid: CAP } });

  equal("yesterday's tally does not spend today",
        endlessGrant(600, TODAY, CAP, { day: TODAY - 1, paid: CAP }),
        { amount: 600, day: { day: TODAY, paid: 600 } });

  // **The forged case, and the one this whole file exists for.** A save claiming every wave
  // the XP ceiling allows is worth three million credits at thirty a wave; the bound pays it
  // exactly what an honest evening pays.
  equal("a forged amount is paid the day's ceiling and no more",
        endlessGrant(2999700, TODAY, CAP, null),
        { amount: CAP, day: { day: TODAY, paid: CAP } });

  equal("a negative amount pays nothing",
        endlessGrant(-500, TODAY, CAP, null),
        { amount: 0, day: { day: TODAY, paid: 0 } });

  equal("a fractional amount is floored rather than rounded up",
        endlessGrant(600.9, TODAY, CAP, null),
        { amount: 600, day: { day: TODAY, paid: 600 } });

  // A server that has not been seeded with the block has no ceiling to enforce. It must pay
  // nothing rather than pay unbounded - `claimAwards` leaves the claim unconfirmed on this.
  equal("no published cap pays nothing",
        endlessGrant(600, TODAY, 0, null),
        { amount: 0, day: { day: TODAY, paid: 0 } });
}

// ------------------------------------------------------------------ the stored tally
console.log("\nthe tally on the wallet");
{
  equal("a stored day reads back", readEndlessDay({ day: TODAY, paid: 600 }),
        { day: TODAY, paid: 600 });

  check(readEndlessDay(undefined) === null, "an absent tally reads as none");
  check(readEndlessDay({ day: "x", paid: 1 }) === null, "a malformed day reads as none");
  check(readEndlessDay({ day: 1 }) === null, "a tally with no figure reads as none");

  equal("a negative stored figure reads as nought rather than as credit",
        readEndlessDay({ day: TODAY, paid: -50 }), { day: TODAY, paid: 0 });
}

// ------------------------------------------------------------------ the window
console.log("\nthe window");
{
  check(MAX_ENDLESS_DAYS_AHEAD >= 1 && MAX_ENDLESS_DAYS_AHEAD <= 7,
        "the forward window is a day or two, not a month");
  check(MAX_ENDLESS_DAYS_BEHIND >= 1 && MAX_ENDLESS_DAYS_BEHIND <= 7,
        "the backward window is a day or two, not a month");
}

// ------------------------------------------------------------------ the reply
console.log("\nthe day the reply reports");
{
  const { toReply } = await import(
    pathToFileURL(join(REPO, "firebase", "functions", "lib", "wallet.js")).href);

  const empty = { granted: 0, spent: 0, confirmedThroughUnix: 0, earnedFloor: 0 };
  const row = (endless) => toReply(
    endless ? { credits: empty, gems: empty, endless } : { credits: empty, gems: empty },
    {}, {}, TODAY)[0];

  const spent = row({ day: TODAY, paid: 4200 });
  equal("today's spend is reported", [spent.endlessDay, spent.endlessPaid], [TODAY, 4200]);

  // **Rolled over in the reply as well as in the granting transaction.** Without it every
  // device would believe the day was already gone until the first claim of it landed, which
  // is the lane refusing to pay anybody on the morning after a full evening.
  const stale = row({ day: TODAY - 1, paid: CAP });
  equal("yesterday's spend is reported as today unspent",
        [stale.endlessDay, stale.endlessPaid], [TODAY, 0]);

  const fresh = row(null);
  equal("an account that has never played the lane reports an unspent day",
        [fresh.endlessDay, fresh.endlessPaid], [TODAY, 0]);
}

console.log(`\n${pass} passed, ${fail} failed`);
process.exit(fail === 0 ? 0 : 1);
