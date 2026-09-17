#!/usr/bin/env node
/**
 * Refer-a-friend: the code, the config, the milestone and the claim's arithmetic.
 *
 *     npm --prefix firebase/functions test
 *
 * No emulator. Everything the transactions decide is a pure function over a document and a
 * config — that is why `referral.ts` is shaped the way it is — so the cases here are the
 * refusals a forged request has to meet, the fold both runtimes have to agree on (the
 * shared vectors, invariant 9a), and the one number the client draws the ladder from.
 */

import { readFileSync, existsSync } from "node:fs";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(HERE, "..", "..", "..");
const LIB = join(REPO, "firebase", "functions", "lib");

if (!existsSync(join(LIB, "referral.js"))) {
  console.error("build the functions first: npm --prefix firebase/functions run build");
  process.exit(1);
}

const load = async (name) => import(pathToFileURL(join(LIB, name)).href);

const {
  CODE_ALPHABET, CODE_LENGTH, normaliseCode, isValidCode, mintCode, usableReferralConfig,
  paymentFor, milestoneLevels, milestoneComplete, readReferralDoc, stateOf, referralGrantId,
  referralSubject, isReferralGrantId, rollReferralChest,
} = await load("referral.js");

let pass = 0, fail = 0;
const check = (ok, what, detail = "") => {
  if (ok) { pass++; console.log("  ok  ", what); }
  else { fail++; console.log("  FAIL", what, detail ? " — " + detail : ""); }
};
const equal = (what, got, want) =>
  check(Object.is(got, want) || JSON.stringify(got) === JSON.stringify(want), what,
        `expected ${JSON.stringify(want)}, got ${JSON.stringify(got)}`);

// ------------------------------------------------------------------ the fold, shared
console.log("\nthe code fold agrees with the shared vectors");
{
  const vectors = JSON.parse(readFileSync(join(REPO, "firebase", "shared", "referral-vectors.json"), "utf8"));
  equal("the alphabet is the vectors' alphabet", CODE_ALPHABET, vectors.alphabet);
  equal("the length is the vectors' length", CODE_LENGTH, vectors.length);
  for (const c of vectors.cases) {
    equal(`fold ${JSON.stringify(c.typed)}`, normaliseCode(c.typed), c.folded);
    equal(`valid ${JSON.stringify(c.folded)}`, isValidCode(c.folded), c.valid);
  }
}

// ------------------------------------------------------------------ the mint
console.log("\nminting");
{
  for (let i = 0; i < 200; i++) {
    const code = mintCode();
    if (!isValidCode(code)) { check(false, "every minted code is valid", code); break; }
    if (i === 199) check(true, "two hundred minted codes are all valid");
  }

  // Rejection sampling: a byte at or above the limit is skipped, never folded onto a symbol.
  let calls = 0;
  const scripted = (n) => {
    calls++;
    return calls === 1 ? Buffer.from([255, 250, 248, 0, 1, 2, 3, 4]) : Buffer.from([5, 6, 7, 8, 9, 10, 11, 12]);
  };
  const code = mintCode(scripted);
  equal("bytes past the limit are skipped rather than biased", code,
        CODE_ALPHABET[0] + CODE_ALPHABET[1] + CODE_ALPHABET[2] + CODE_ALPHABET[3] + CODE_ALPHABET[4] +
        CODE_ALPHABET[5] + CODE_ALPHABET[6] + CODE_ALPHABET[7]);
  check(!CODE_ALPHABET.includes("0") && !CODE_ALPHABET.includes("O") && !CODE_ALPHABET.includes("1") &&
        !CODE_ALPHABET.includes("I") && !CODE_ALPHABET.includes("L"),
        "the alphabet has no look-alikes");
}

// ------------------------------------------------------------------ the config
console.log("\nthe published block");
{
  const good = {
    milestoneChapter: "s01_thornwatch", maxBound: 50,
    invitee: { tier: "royal", count: 2 }, perInvitee: { tier: "royal", count: 2 },
  };
  check(usableReferralConfig(good) !== null, "the shipped shape is usable");
  check(usableReferralConfig(undefined) === null, "an absent block is not");
  equal("an absent count reads as one", usableReferralConfig({ ...good, invitee: { tier: "gold" } })?.invitee,
        { tier: "gold", count: 1 });
  check(usableReferralConfig({ ...good, perInvitee: { tier: "royal", count: 5 } }) === null,
        "a count over the ceiling is refused");
  check(usableReferralConfig({ ...good, perInvitee: { tier: "royal", count: 0 } }) === null,
        "a count of nought is refused");
  check(usableReferralConfig({ ...good, maxBound: 0 }) === null, "a cap of nought is refused");
  check(usableReferralConfig({ ...good, maxBound: 501 }) === null, "a cap over the ceiling is refused");
  check(usableReferralConfig({ ...good, milestoneChapter: "" }) === null, "no milestone is refused");
  check(usableReferralConfig({ ...good, invitee: {} }) === null, "no invitee tier is refused");

  const cfg = usableReferralConfig(good);
  equal("the referrer's payment", paymentFor(cfg, "rung"), { tier: "royal", count: 2 });
  equal("the invitee's payment", paymentFor(cfg, "invitee"), { tier: "royal", count: 2 });
}

// ------------------------------------------------------------------ the milestone
console.log("\nthe milestone");
{
  const config = {
    levelChapters: { a1: "ch_a", a2: "ch_a", b1: "ch_b" },
  };
  equal("the milestone's levels come off the published map", milestoneLevels(config, "ch_a").sort(), ["a1", "a2"]);
  equal("a chapter nothing lists has no levels", milestoneLevels(config, "ch_z"), []);

  const ids = ["a1", "a2"];
  check(milestoneComplete({ a1: { stars: 1 }, a2: { stars: 3 } }, ids), "every level starred is complete");
  check(!milestoneComplete({ a1: { stars: 1 } }, ids), "a level missing is not");
  check(!milestoneComplete({ a1: { stars: 1 }, a2: { stars: 0 } }, ids), "a level played and never cleared is not");
  check(!milestoneComplete({ a1: { stars: 1 }, a2: { stars: 3 } }, []), "an empty chapter is never complete");
  check(!milestoneComplete([{ stars: 3 }], ids), "an array where a map should be is not");
  check(!milestoneComplete(undefined, ids), "no ledger is not");
}

// ------------------------------------------------------------------ the document
console.log("\nthe document and the state");
{
  const empty = readReferralDoc(undefined);
  equal("an absent document reads as empty", empty,
        { code: "", bound: 0, finished: 0, paid: [], referrer: "", finished_: false });

  const doc = readReferralDoc({
    code: "ABCDEFGH", bound: 3, finished: 2, paid: ["rung:1:1", 7, "", "rung:1:2"], referrer: "u2",
    finishedAt: true,
  });
  equal("paid keeps only subject strings", doc.paid, ["rung:1:1", "rung:1:2"]);
  equal("a malformed code reads as none", readReferralDoc({ code: "abc" }).code, "");

  const state = stateOf(doc, true);
  equal("the state is what the client draws", state, {
    code: "ABCDEFGH", bound: 3, finished: 2, paid: ["rung:1:1", "rung:1:2"], referred: true,
    milestoneReached: true, canRedeem: false,
  });
  equal("a fresh account with the milestone uncleared may redeem", stateOf(empty, false).canRedeem, true);
  equal("a veteran may not", stateOf(empty, true).canRedeem, false);
  equal("nor may somebody already referred", stateOf(doc, false).canRedeem, false);
}

// ------------------------------------------------------------------ the ids and the roll
console.log("\nthe grant id and the roll");
{
  equal("a friend's first chest", referralSubject("rung", 5, 1), "rung:5:1");
  equal("a friend's second chest", referralSubject("rung", 5, 2), "rung:5:2");
  equal("the invitee's subject", referralSubject("invitee", 0, 2), "invitee:2");
  equal("a friend's grant id", referralGrantId("rung", 5, 1, "credits"), "referral:rung:5:1:credits");
  equal("the invitee's grant id", referralGrantId("invitee", 0, 2, "gems"), "referral:invitee:2:gems");
  check(isReferralGrantId("referral:rung:1:1:credits") && !isReferralGrantId("task:daily:1:x:credits"),
        "a referral id is told from every other");

  const chest = {
    guaranteed: [{ kind: "credits", min: 100, max: 200 }, { kind: "gems", min: 3, max: 5 }],
    options: [{ kind: "hearts", min: 1, max: 1, weight: 50 }, { kind: "credits", min: 10, max: 20, weight: 50 }],
  };
  const a = rollReferralChest(chest, "uid-a", "rung:1:1");
  const b = rollReferralChest(chest, "uid-a", "rung:1:1");
  equal("a retry rolls the same chest", a, b);
  const credits = a.filter((d) => d.kind === "credits").reduce((s, d) => s + d.amount, 0);
  check(credits >= 100 && credits <= 220, "the roll stays inside its bands", String(credits));
  check(JSON.stringify(rollReferralChest(chest, "uid-a", "rung:1:2")) !== JSON.stringify(a) ||
        JSON.stringify(rollReferralChest(chest, "uid-b", "rung:1:1")) !== JSON.stringify(a),
        "the second chest of a pair, or another account, rolls a different chest");
}

console.log(`\n${pass} passed, ${fail} failed`);
process.exit(fail === 0 ? 0 : 1);
