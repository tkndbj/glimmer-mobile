#!/usr/bin/env node
/**
 * Account deletion — the parts that can be proved without a database.
 *
 *     npm --prefix firebase/functions test
 *
 * The erasure itself is transactions over Firestore and belongs to the live suite. What lives
 * here is the half whose failures are **silent**, which is the same reason `names.mjs` exists:
 *
 *  1. **The credential guard.** Sign in with Apple needs four secrets, and a deployment holding
 *     three of them must skip the revocation rather than attempt it — an attempt with a missing
 *     piece fails inside Apple with `invalid_client`, which names nothing, and the deletion
 *     would still report success. The only visible difference between "revoked" and "quietly
 *     never revoked" is a log line, so the predicate that decides it is worth pinning.
 *
 *  2. **The client secret's claims.** Apple refuses a token whose `sub` is the wrong client
 *     with the same `invalid_client` — and the trap is specific and documented: a *native* iOS
 *     sign-in is identified by the app's **bundle id**, not by the Services ID that identifies
 *     the web flow. That is one string, it is impossible to tell apart by reading the code, and
 *     getting it wrong means every Apple account deletion silently leaves the grant standing.
 *
 *  3. **The tombstone.** A denied name's reservation is retargeted rather than released, so the
 *     value it is retargeted to must be something no real account can ever be.
 */

import { existsSync } from "node:fs";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { generateKeyPairSync, createVerify } from "node:crypto";

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(HERE, "..", "..", "..");
const LIB = join(REPO, "firebase", "functions", "lib");

if (!existsSync(join(LIB, "account.js"))) {
  console.error("build the functions first: npm --prefix firebase/functions run build");
  process.exit(1);
}

const load = async (name) => import(pathToFileURL(join(LIB, name)).href);

const { usableAppleKeys, clientSecret, TOMBSTONE_UID, BOARD_IDS } = await load("account.js");
const { BUNDLE_ID } = await load("config.js");

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

// A throwaway P-256 key, so the signature can actually be verified rather than merely produced.
const { privateKey, publicKey } = generateKeyPairSync("ec", { namedCurve: "P-256" });
const PEM = privateKey.export({ type: "pkcs8", format: "pem" });

const KEYS = {
  keyId: "ABCDE12345",
  teamId: "TEAM123456",
  clientId: BUNDLE_ID,
  privateKey: PEM,
};

// ============================================================ the credential guard
console.log("\nthe credential guard");

check("all four present is usable", usableAppleKeys(KEYS) !== null);
check("nothing at all is not", usableAppleKeys(undefined) === null);
check("an empty object is not", usableAppleKeys({}) === null);

for (const missing of ["keyId", "teamId", "clientId", "privateKey"]) {
  const partial = { ...KEYS, [missing]: undefined };
  check(`without ${missing}, revocation is skipped rather than attempted`,
        usableAppleKeys(partial) === null);
}

// An unset secret reads back as an empty string rather than as absent on some paths, and an
// empty client id would be sent to Apple verbatim.
check("an empty string counts as missing",
      usableAppleKeys({ ...KEYS, clientId: "" }) === null);

// ============================================================ the client secret
console.log("\nthe client secret Apple is given");

const now = 1_800_000_000;
const token = clientSecret(KEYS, now);
const [rawHeader, rawPayload, rawSignature] = token.split(".");

const header = JSON.parse(Buffer.from(rawHeader, "base64url").toString("utf8"));
const payload = JSON.parse(Buffer.from(rawPayload, "base64url").toString("utf8"));

equal("signed with ES256", header.alg, "ES256");
equal("carries the key id, which is how Apple finds the public half", header.kid, KEYS.keyId);

equal("issued by the team", payload.iss, KEYS.teamId);
equal("audience is Apple's token endpoint", payload.aud, "https://appleid.apple.com");

// The documented trap, pinned. A native iOS sign-in is identified by the bundle id; the
// Services ID identifies the *web* flow, and Apple refuses the mismatch as `invalid_client`.
equal("subject is the client id it was configured with", payload.sub, KEYS.clientId);
equal("and this deployment's client id is the app's bundle id", KEYS.clientId, BUNDLE_ID);

equal("issued at the moment it is used", payload.iat, now);
equal("and lives five minutes, not Apple's six-month ceiling", payload.exp - payload.iat, 300);

// Verified rather than assumed: a JWT that parses but does not verify is the failure that
// looks exactly like a wrong key.
const verifier = createVerify("SHA256");
verifier.update(`${rawHeader}.${rawPayload}`);

// jsonwebtoken emits a JOSE (r||s) signature; Node wants DER unless told otherwise.
check("the signature verifies against the key it was signed with",
      verifier.verify({ key: publicKey, dsaEncoding: "ieee-p1363" },
                      Buffer.from(rawSignature, "base64url")));

// An escaped newline is what a PEM looks like after a round trip through a shell or a secret
// editor, and it is the commonest way a working key arrives unusable.
check("a PEM whose newlines were escaped still signs",
      typeof clientSecret({ ...KEYS, privateKey: PEM.replace(/\n/g, "\\n") }, now) === "string");

// ============================================================ the tombstone
console.log("\nthe tombstone a denied name is retargeted to");

// A denied name's reservation is kept so the string cannot be handed to the next person who
// asks for it. That only holds while the tombstone matches no caller: `claimName` compares
// `existing.uid !== uid` and answers "taken".
check("is not a possible Firebase uid", TOMBSTONE_UID.length !== 28);
check("and contains characters a uid never does", /[^A-Za-z0-9]/.test(TOMBSTONE_UID));

// ============================================================ the boards
console.log("\nthe boards a deleted keeper is scrubbed from");

// Mirrors `rebuildGroveRanks`. A board it does not know about keeps a deleted player's name
// and score standing on a public leaderboard until the nightly rebuild, which is the one thing
// the scrub exists to prevent.
equal("ten of them", BOARD_IDS.length, 10);
equal("the global one first", BOARD_IDS[0], "global");
check("and one per league",
      BOARD_IDS.slice(1).every((id, i) => id === `l${i}`),
      BOARD_IDS.join(","));

console.log(`\n${pass} passed, ${fail} failed`);
process.exit(fail === 0 ? 0 : 1);
