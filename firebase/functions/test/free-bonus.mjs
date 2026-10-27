// The free bonus (invariant 67): gems for linking a sign-in and for choosing a nickname, offered to an
// account that had done neither when it was first asked, paid once per task per account. What is under
// contract: enrolment writes an undone task as offered and a done one as exempt, and never revisits a
// task it has written (the whole of "only for players who did not do them before"); a claim pays the
// gems the offer promised, once, and only for a deed the server can see; a refusal is only ever for
// what stays true tomorrow; a sign-in earns the link bonus for one account ever; the record survives a
// whole-document wallet write; and the config reader drops what the seeder would have refused.
//
// Run through `lib/`, the compiled output, so what is tested is what deploys.

import { existsSync } from "node:fs";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(HERE, "..", "..", "..");
const lib = (name) => join(REPO, "firebase", "functions", "lib", name);

if (!existsSync(lib("free-bonus.js"))) {
  console.error("build first: npm run build (lib/free-bonus.js is missing)");
  process.exit(1);
}

const B = await import(pathToFileURL(lib("free-bonus.js")).href);
const { readWallet } = await import(pathToFileURL(lib("wallet.js")).href);

let failed = 0;
const check = (ok, what, detail = "") => {
  if (ok) console.log("  ok   " + what);
  else { failed++; console.log("  FAIL " + what + (detail ? "  " + detail : "")); }
};
const equal = (what, got, want) =>
  check(JSON.stringify(got) === JSON.stringify(want), what, `got ${JSON.stringify(got)}, want ${JSON.stringify(want)}`);

const NOW = 1_793_000_000;
const CONFIG = { gems: { link: 60, name: 60 } };
const NOTHING = { linked: false, named: false };
const BOTH = { linked: true, named: true };

// ------------------------------------------------------------------ the config
console.log("free bonus: the config");
equal("the shipped shape reads", B.usableFreeBonusConfig({ tasks: [{ id: "link", gems: 60 }, { id: "name", gems: 60 }] }), CONFIG);
equal("an absent block is the feature off", B.usableFreeBonusConfig(undefined), null);
equal("an empty block is the feature off", B.usableFreeBonusConfig({ tasks: [] }), null);
equal("an unknown task, a nought, a fraction and a repeat are dropped",
  B.usableFreeBonusConfig({ tasks: [{ id: "email", gems: 5 }, { id: "link", gems: 0 }, { id: "name", gems: 1.5 },
                                     { id: "link", gems: 40 }, { id: "link", gems: 50 }] }),
  { gems: { link: 40 } });
equal("a figure past the ceiling is dropped", B.usableFreeBonusConfig({ tasks: [{ id: "name", gems: B.FREE_BONUS_MAX_GEMS + 1 }] }), null);

// ------------------------------------------------------------------ enrolment
console.log("free bonus: enrolment");
{
  const fresh = B.enrol({}, CONFIG, NOTHING, NOW);
  check(fresh.changed, "a fresh account is written down");
  equal("both tasks offered at the promised gems", fresh.record, {
    link: { state: "offered", gems: 60, atUnix: NOW, paidUnix: 0 },
    name: { state: "offered", gems: 60, atUnix: NOW, paidUnix: 0 },
  });

  const veteran = B.enrol({}, CONFIG, BOTH, NOW);
  equal("both done before: both exempt, and nothing to list", B.bonusRows(veteran.record, BOTH), []);

  const linkedOnly = B.enrol({}, CONFIG, { linked: true, named: false }, NOW);
  equal("linked, no name: only the name is offered", B.bonusRows(linkedOnly.record, { linked: true, named: false }),
        [{ id: "name", gems: 60, paid: false, done: false }]);

  const namedOnly = B.enrol({}, CONFIG, { linked: false, named: true }, NOW);
  equal("named, not linked: only the link is offered", B.bonusRows(namedOnly.record, { linked: false, named: true }),
        [{ id: "link", gems: 60, paid: false, done: false }]);

  const again = B.enrol(fresh.record, { gems: { link: 999, name: 999 } }, BOTH, NOW + 100);
  check(!again.changed, "a second ask never re-enrols, whatever is done since");
  equal("and never re-prices a promise", again.record, fresh.record);

  const later = B.enrol({ link: fresh.record.link }, CONFIG, { linked: false, named: true }, NOW + 5);
  check(later.changed && later.record.name.state === "exempt" && later.record.link === fresh.record.link,
        "a task missing from the record enrols alone, on the facts of the day");

  equal("a task the config does not offer is not enrolled", B.enrol({}, { gems: { name: 60 } }, NOTHING, NOW).record,
        { name: { state: "offered", gems: 60, atUnix: NOW, paidUnix: 0 } });
}

// ------------------------------------------------------------------ judging
console.log("free bonus: judging a claim");
{
  const { record } = B.enrol({}, CONFIG, NOTHING, NOW);
  equal("undone: not_done, which is never permanent", B.judgeClaim(record, "link", NOTHING), { kind: "not_done" });
  equal("done: pays the promised gems", B.judgeClaim(record, "link", BOTH), { kind: "pay", gems: 60 });

  const paid = B.payTask(record, "link", NOW + 60);
  equal("paid once: the second ask is spent", B.judgeClaim(paid, "link", BOTH), { kind: "spent" });
  equal("paying one task leaves the other offered", B.judgeClaim(paid, "name", BOTH), { kind: "pay", gems: 60 });
  equal("the paid row is dated", paid.link, { state: "paid", gems: 60, atUnix: NOW, paidUnix: NOW + 60 });
  equal("a paid task is listed as paid until the panel closes", B.bonusRows(paid, BOTH)[0], { id: "link", gems: 60, paid: true, done: true });
  equal("paying a paid task changes nothing", B.payTask(paid, "link", NOW + 99), paid);

  const exempt = B.enrol({}, CONFIG, BOTH, NOW).record;
  equal("an exempt task is never paid", B.judgeClaim(exempt, "name", BOTH), { kind: "none" });
  equal("nor one never enrolled", B.judgeClaim({}, "name", BOTH), { kind: "none" });

  const voided = B.voidTask(record, "link");
  equal("a sign-in used elsewhere withdraws the task", B.judgeClaim(voided, "link", BOTH), { kind: "none" });
  equal("and voiding a paid task does nothing", B.voidTask(paid, "link"), paid);
}

// ------------------------------------------------------------------ the sign-ins
console.log("free bonus: sign-ins");
equal("an anonymous account holds none", B.linkedIdentities([]), []);
equal("Firebase's own provider is not a sign-in", B.linkedIdentities([{ providerId: "firebase", uid: "x" }]), []);
equal("two providers, sorted and de-duplicated",
      B.linkedIdentities([{ providerId: "google.com", uid: "g1" }, { providerId: "apple.com", uid: "a1" },
                          { providerId: "google.com", uid: "g1" }]),
      ["apple.com:a1", "google.com:g1"]);
check(/^[0-9a-f]{64}$/.test(B.identityKey("google.com:g1")), "a sign-in is stored as a SHA-256, never its raw id");
check(B.identityKey("google.com:g1") === B.identityKey("google.com:g1") &&
      B.identityKey("google.com:g1") !== B.identityKey("apple.com:g1"), "the key is stable and per provider");
check(!B.earnedElsewhere("me", [null, { uid: "me", atUnix: NOW }]), "earned by this account is not elsewhere");
check(B.earnedElsewhere("me", [null, { uid: "them", atUnix: NOW }]), "earned by another account is");
equal("the grant record's id", B.freeBonusGrantId("name"), "freebonus:name");
check(B.isFreeBonusTask("link") && !B.isFreeBonusTask("email") && !B.isFreeBonusTask(undefined), "only the two tasks parse");

// ------------------------------------------------------------------ the record on the wallet
console.log("free bonus: the record on the wallet");
{
  const { record } = B.enrol({}, CONFIG, NOTHING, NOW);
  const paid = B.payTask(record, "name", NOW + 1);
  equal("a sound record reads back", B.readFreeBonus(paid), paid);
  equal("a malformed row is dropped (and re-enrols on the facts of the day), the rest kept",
        B.readFreeBonus({ link: { state: "won", gems: 60 }, name: paid.name, email: paid.name }), { name: paid.name });
  equal("an exempt row reads as nought gems", B.readFreeBonus({ link: { state: "exempt", gems: 60, atUnix: NOW } }),
        { link: { state: "exempt", gems: 0, atUnix: NOW, paidUnix: 0 } });
  equal("garbage reads as nothing", B.readFreeBonus("yes"), {});

  const raw = { credits: { granted: 10, spent: 0 }, gems: { granted: 5, spent: 0 }, freeBonus: paid };
  const wallet = readWallet({ exists: true, data: () => raw }, { seeds: {} });
  equal("it survives readWallet, which every writer writes whole", wallet.freeBonus, paid);
  const bare = readWallet({ exists: true, data: () => ({ credits: { granted: 1 }, gems: { granted: 1 } }) }, { seeds: {} });
  check(!("freeBonus" in bare), "and a wallet without one gains no field (Firestore refuses undefined)");
}

if (failed > 0) {
  console.log(`\n${failed} free bonus check(s) FAILED`);
  process.exit(1);
}
console.log("\nfree bonus: all checks passed");
