#!/usr/bin/env node
/**
 * Publishes the forced-update requirement into Firestore.
 *
 *     node firebase/seed/seed-release.mjs --check     # prove it, write nothing
 *     node firebase/seed/seed-release.mjs             # publish
 *
 * Deliberately its own tool rather than another block in `seed-config.mjs`, and the reason
 * is the same one that keeps the release requirement out of the content pipeline: forcing
 * an update is a decision taken at a moment that has nothing to do with rewards, prices or
 * levels, and it is the one document in this deployment whose mistakes are measured in
 * players who cannot open the game. A re-seed done for an unrelated reason must not be able
 * to raise or lower a wall as a side effect — and this repository has already learned that a
 * re-seed publishes whatever is in the working tree, which for a wall would be somebody
 * else's in-flight edit deciding who may play.
 *
 * What the client does with what this writes is in `Assets/Game/Scripts/Domain/Release/`.
 * The two rules worth knowing here:
 *
 *   - A device caches what it was last told, so a wall survives a restart with no network.
 *     Publishing is therefore not reversible by *stopping* publishing; it is reversed by
 *     publishing a lower minimum, or by deleting the document.
 *   - A minimum with no store link is refused by the client outright. The guard below exists
 *     so the mistake never reaches the database at all, exactly as the word-list guard in
 *     `seed-config.mjs` does.
 *
 * Authentication reuses the gcloud login rather than a service-account key file, so there is
 * no long-lived credential sitting in the repository to leak.
 */

import { readFileSync, existsSync } from "node:fs";
import { execSync } from "node:child_process";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(HERE, "..", "..");
const SOURCE = join(HERE, "release.json");
const PROJECT_SETTINGS = join(REPO, "ProjectSettings", "ProjectSettings.asset");

const PROJECT = "glimmer-groove-1cd60";
const DOCUMENT = "config/release";

/** The platform keys the client looks itself up under. Wire spellings — see ReleasePlatform. */
const PLATFORMS = ["android", "ios"];

// ------------------------------------------------------------------- the build number
/**
 * The second copy of `AppVersion.TryParse`, and the only other one that will ever exist.
 *
 * It has to match the client's exactly: this tool decides the integer a phone compares
 * itself against, so a disagreement here is not a rounding error, it is the wrong people
 * being walled out. `VECTORS` below is what keeps the two honest — the same shape invariant
 * 9a uses for the reward rule, at a scale that does not justify a shared vector file.
 */
function parseVersion(version) {
  if (typeof version !== "string" || version.length === 0) return null;

  const parts = version.split(".");
  const major = segment(parts[0]);
  const minor = parts.length > 1 ? segment(parts[1]) : 0;
  const patch = parts.length > 2 ? segment(parts[2]) : 0;

  if (major === null || minor === null || patch === null) return null;
  if (minor > 99 || patch > 99) return null;   // 1.100.0 and 2.0.0 are the same integer

  return major * 10000 + minor * 100 + patch;
}

function segment(text) {
  if (!/^\d+$/.test(text ?? "")) return null;
  return Number(text);
}

/** Pinned against `ReleaseRuleTests`, case for case. A change to one is a change to both. */
const VECTORS = [
  ["1.4.2", 10402],
  ["1", 10000],
  ["1.0.0", 10000],
  ["0.0.0", 0],
  ["1.99.99", 19999],
  ["1.100.0", null],
  ["1.0.100", null],
  ["1.4.2-beta", null],
  ["beta", null],
  ["", null],
];

for (const [input, expected] of VECTORS) {
  const got = parseVersion(input);
  if (got !== expected) {
    throw new Error(
      `the version parser here disagrees with the client's: '${input}' gave ${got}, ` +
      `expected ${expected}. AppVersion.TryParse and parseVersion must answer identically — ` +
      `this tool decides the number every phone compares itself against.`
    );
  }
}

// ------------------------------------------------------------------------ what ships
/** `LegalLinks.Usable`, which is what the client asks before opening one. */
function usableUrl(url) {
  return typeof url === "string"
      && url.startsWith("https://")
      && url.length > "https://".length
      && !url.includes(" ");
}

/** The version in the working tree — the build this repository currently produces. */
function treeVersion() {
  if (!existsSync(PROJECT_SETTINGS)) return null;

  const match = readFileSync(PROJECT_SETTINGS, "utf8").match(/^\s*bundleVersion:\s*(\S+)\s*$/m);
  return match ? match[1] : null;
}

if (!existsSync(SOURCE)) throw new Error(`missing ${SOURCE}`);
const authored = JSON.parse(readFileSync(SOURCE, "utf8"));

const bundleVersion = treeVersion();
const treeBuild = bundleVersion === null ? null : parseVersion(bundleVersion);

if (bundleVersion === null) {
  console.log("warning: could not read bundleVersion from ProjectSettings — the check that a " +
              "minimum names a build that actually exists is not running");
} else if (treeBuild === null) {
  throw new Error(
    `bundleVersion in Player Settings is '${bundleVersion}', which is not a major.minor.patch ` +
    `number. The client cannot compare itself against anything in that state (AppVersion.Running ` +
    `answers 0 and the gate is inert), so publishing a wall would do nothing at all.`
  );
}

const document = {};
const lines = [];

for (const platform of PLATFORMS) {
  const block = authored[platform];

  if (block === undefined) {
    throw new Error(
      `release.json names no '${platform}' block. Every platform is listed explicitly, with ` +
      `"0.0.0" meaning "nothing forced" — an absent block and an unforced one look identical ` +
      `to the client, and only one of them is a decision somebody made.`
    );
  }

  const minimum = parseVersion(block.minimum);
  if (minimum === null) {
    throw new Error(`${platform}.minimum is '${block.minimum}', which is not a ` +
                    `major.minor.patch version with segments in 0..99`);
  }

  const store = block.store ?? "";

  // The brick guard, and the reason this tool exists rather than a console edit. A minimum
  // with no door is one no build can satisfy from the player's side. The client refuses it
  // too — this is here so it never reaches the database, exactly as the word-list floor is.
  if (minimum > 0 && !usableUrl(store)) {
    throw new Error(
      `${platform} asks for ${block.minimum} but names no usable https store link, so every ` +
      `${platform} player would meet a wall with no door. Fill in ${platform}.store, or set ` +
      `${platform}.minimum back to "0.0.0".`
    );
  }

  if (store !== "" && !usableUrl(store)) {
    throw new Error(`${platform}.store is '${store}', which is not an https link the device ` +
                    `will open. See LegalLinks.Usable.`);
  }

  // Forcing an update to a build that does not exist yet. Always a mistake and always the
  // same one: the number was raised before the store was serving it, or ahead of the tree.
  if (treeBuild !== null && minimum > treeBuild) {
    throw new Error(
      `${platform} asks for ${block.minimum} but this repository builds ${bundleVersion}, so ` +
      `no such release exists to update to. Ship the build first, wait for the store to serve ` +
      `it, then raise the minimum.`
    );
  }

  document[platform] = { minimum, store };

  lines.push(minimum === 0
    ? `  ${platform.padEnd(8)} nothing forced`
    : `  ${platform.padEnd(8)} ${block.minimum} and newer (build ${minimum})`);
}

console.log(`${DOCUMENT} — this repository builds ${bundleVersion ?? "unknown"}`);
for (const line of lines) console.log(line);

const forcing = PLATFORMS.filter((p) => document[p].minimum > 0);
if (forcing.length === 0) {
  console.log("  no wall on any platform: publishing this lifts any wall already out there");
} else if (forcing.length < PLATFORMS.length) {
  console.log(`  note: ${forcing.join(", ")} only — the others are left open, which is the ` +
              `normal state while a store is still reviewing`);
}

if (process.argv.includes("--check")) {
  console.log("Validated. No remote writes.");
  process.exit(0);
}

// ---------------------------------------------------------------------- publishing
function encode(value) {
  if (typeof value === "boolean") return { booleanValue: value };
  if (typeof value === "number") {
    return Number.isInteger(value) ? { integerValue: String(value) } : { doubleValue: value };
  }
  if (typeof value === "string") return { stringValue: value };
  if (Array.isArray(value)) return { arrayValue: { values: value.map(encode) } };

  const fields = {};
  for (const [k, v] of Object.entries(value ?? {})) fields[k] = encode(v);
  return { mapValue: { fields } };
}

function accessToken() {
  try {
    // execSync rather than execFileSync-with-shell, for `seed-config.mjs`' reason: gcloud is
    // a .cmd on Windows and needs a shell, and the command is a constant.
    return execSync("gcloud auth print-access-token", {
      encoding: "utf8", stdio: ["ignore", "pipe", "pipe"],
    }).trim();
  } catch (e) {
    throw new Error("could not get a token from gcloud. Run 'gcloud auth login' first.\n" +
                    (e.stderr ?? e.message));
  }
}

const fields = {};
for (const [k, v] of Object.entries(document)) fields[k] = encode(v);

// Replaced rather than merged. A platform removed from release.json has to stop being
// enforced, and a merge would leave the server asking for a build nobody publishes any more
// — which on this document is a wall nobody remembers putting up. `config/products` is
// written the same way for the same reason.
const response = await fetch(
  `https://firestore.googleapis.com/v1/projects/${PROJECT}/databases/(default)/documents/${DOCUMENT}`,
  {
    method: "PATCH",
    headers: { Authorization: `Bearer ${accessToken()}`, "Content-Type": "application/json" },
    body: JSON.stringify({ fields }),
  }
);

if (!response.ok) {
  throw new Error(`writing ${DOCUMENT} failed: ${response.status} ${await response.text()}`);
}

console.log(`published ${DOCUMENT}`);
console.log("Devices pick it up on their next launch, on coming back to the app, or within " +
            "fifteen minutes of a session already running.");
