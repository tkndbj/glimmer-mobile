// The save rule's evaluation budget, measured against Firestore's own engine.
//
// Firestore refuses any request whose rules evaluate more than 1,000 expressions, and it refuses
// it as PERMISSION_DENIED - indistinguishable on the device from a malformed save, and fatal in
// the same way: every write of that account is lost until the rules change (invariant 12c). The
// cost of `players/{uid}` grows with every clause `wellFormed()` evaluates, and a save carrying
// more optional keys evaluates more of them, so the account that hits the cap first is the
// longest-played one. On 2026-10-25 it did: the v47 build-stamp clauses tipped a veteran save over
// the cap with two expressions to spare before them.
//
// So the budget is measured, never estimated, and measured on the heaviest save the rules can
// accept - **derived from the rules text itself** (every allow-listed key, every optional sub-key
// a clause asks about, each at the type its clause demands), so a new clause is priced the moment
// it is written and this file never needs editing to keep up.
//
// How: the Rules test API (`projects.test`, which evaluates source without releasing it) runs the
// create and the update of that save under a copy of the rules padded with N no-op terms, and
// bisects the largest N still allowed. A separate run of the padding alone prices one term, so
// headroom is reported in expressions. It fails below `REQUIRED_HEADROOM`.
//
// Read-only: nothing is released and no document is touched. Needs the owner's gcloud token.
//
// Usage: node e2e/rules-budget.mjs [path/to/firestore.rules]

import { readFileSync } from "node:fs";
import { execSync } from "node:child_process";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const PROJECT = process.env.GLIMMER_PROJECT ?? "glimmer-groove-1cd60";

/** Firestore's per-request ceiling on evaluated expressions. Not configurable. */
const CAP = 1000;

/**
 * The least headroom a ruleset may ship with, in expressions, for both the create and the update
 * of the heaviest save. A quarter of the cap: a new top-level key with a bounded list costs about
 * a dozen, so this is room for twenty more before anybody has to think again - and the day this
 * fails is a day the cost is visible in a terminal rather than on a player's phone.
 */
const REQUIRED_HEADROOM = 250;

const here = dirname(fileURLToPath(import.meta.url));
const rulesPath = process.argv[2] ?? join(here, "..", "firestore.rules");
const rules = readFileSync(rulesPath, "utf8");

const token = execSync("gcloud auth print-access-token", { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }).trim();

// ------------------------------------------------------------------------- the heaviest save

/** The body of `function name(...) { ... }`, braces balanced. */
function functionBody(source, name) {
  const start = source.search(new RegExp(`function\\s+${name}\\s*\\(`));
  if (start < 0) throw new Error(`firestore.rules has no function ${name}()`);

  const open = source.indexOf("{", start);
  let depth = 0;
  for (let i = open; i < source.length; i++) {
    if (source[i] === "{") depth++;
    else if (source[i] === "}" && --depth === 0) return source.slice(open + 1, i);
  }
  throw new Error(`function ${name}() is not closed`);
}

/** Line comments out, so a key named in prose is never mistaken for one a clause asks about. */
const code = (text) => text.replace(/\/\/[^\n]*/g, "");

/** A present value of each type: the smallest that passes (`1` meets every bound the rules write). */
const VALUE = { list: () => [], map: () => ({}), int: () => 1, string: () => "x", bool: () => true };

/** The type a `get` default stands for. The default always passes its own clause. */
const DEFAULT_TYPE = { "[]": "list", "{}": "map", "0": "int", "''": "string", "false": "bool", "true": "bool" };

function put(doc, path, type) {
  let at = doc;
  for (const key of path.slice(0, -1)) {
    if (typeof at[key] !== "object" || at[key] === null || Array.isArray(at[key])) at[key] = {};
    at = at[key];
  }
  const leaf = path[path.length - 1];
  const existing = at[leaf];
  // A map already holding sub-keys is never flattened by a later, shallower mention of it.
  if (type === "map" && existing && typeof existing === "object" && !Array.isArray(existing)) return;
  at[leaf] = VALUE[type]();
}

/**
 * The heaviest save `wellFormed()` accepts: every key `hasOnly` names and every optional key or
 * sub-key a clause reads, each holding the type its clause demands.
 *
 * Read off the rules' one idiom for an optional value, `owner.get('key', default)` - the default
 * names the type - where an owner is `d`, a `let` bound to another such read, or a helper's
 * parameter at each call site. A clause on an absent key is evaluated against its default, so a
 * save that holds every key pays for every clause; values are the smallest that pass.
 *
 * **Anything else is refused rather than guessed.** An `in` test, or a `get` this parser cannot
 * place, would leave a clause out of the heaviest save and the budget would read lighter than the
 * truth - so it stops here instead, which is the one failure this tool must never have.
 */
function heaviestSave(source) {
  const all = code(source);
  const body = code(functionBody(source, "wellFormed"));
  const doc = {};

  const allowed = /hasOnly\(\[([\s\S]*?)\]\)/.exec(body);
  if (!allowed) throw new Error("wellFormed() has no hasOnly([...]) allow-list");
  const keys = [...allowed[1].matchAll(/'([A-Za-z0-9_]+)'/g)].map((m) => m[1]);

  const helpers = [...all.matchAll(/function\s+(\w+)\s*\(([^)]*)\)/g)]
    .filter(([, name]) => name !== "wellFormed" && name !== "isOwner" && name !== "signedIn")
    .map(([, name, params]) => ({ name, params: params.split(",").map((p) => p.trim()), body: code(functionBody(source, name)) }));

  const GET = String.raw`(\w+)\.get\(\s*'(\w+)'\s*,\s*(\[\]|\{\}|0|''|false|true)\s*\)`;

  // Owner name -> the path it stands for, scope by scope.
  const scopes = [{ owners: { d: [] }, text: body }];

  // `let name = owner.get('key', default);`
  for (const m of body.matchAll(new RegExp(String.raw`let\s+(\w+)\s*=\s*${GET}\s*;`, "g"))) {
    const [, name, owner, key, fallback] = m;
    const base = scopes[0].owners[owner];
    if (!base) throw new Error(`rules-budget cannot place 'let ${name}': '${owner}' is not d or an earlier let`);
    scopes[0].owners[name] = [...base, key];
    put(doc, [...base, key], DEFAULT_TYPE[fallback]);
  }

  // `helper(owner.get('key', default))`: the helper's first parameter stands for that path.
  for (const helper of helpers) {
    for (const m of body.matchAll(new RegExp(String.raw`\b${helper.name}\(\s*${GET}`, "g"))) {
      const [, owner, key, fallback] = m;
      const base = scopes[0].owners[owner];
      if (!base) continue;
      put(doc, [...base, key], DEFAULT_TYPE[fallback]);
      scopes.push({ owners: { [helper.params[0]]: [...base, key] }, text: helper.body });
    }
  }

  for (const { owners, text } of scopes) {
    if (/'\w+'\s+in\s+\w/.test(text)) {
      throw new Error("an optional key is tested with `in`; read it with `get(key, default)` " +
                      "(firestore.rules, the evaluation budget note) so it is priced here");
    }

    // Every `get` must resolve, or a clause would be missing from the heaviest save.
    for (const m of text.matchAll(/(\S)\.get\(/g)) {
      const before = text.slice(0, m.index + 1).match(/(\w+)$/);
      if (!before || !(before[1] in owners) && !scopes[0].owners[before[1]]) {
        throw new Error(`rules-budget cannot place a get() near '${text.slice(Math.max(0, m.index - 30), m.index + 30).trim()}'`);
      }
    }

    for (const m of text.matchAll(new RegExp(GET, "g"))) {
      const [, owner, key, fallback] = m;
      const base = owners[owner] ?? scopes[0].owners[owner];
      put(doc, [...base, key], DEFAULT_TYPE[fallback]);
    }

    // Required values, read directly: `d.schemaVersion is int`.
    for (const m of text.matchAll(/\b(\w+)((?:\.\w+)+)\s+is\s+(list|map|int|string|bool)\b/g)) {
      const base = owners[m[1]];
      if (base) put(doc, [...base, ...m[2].slice(1).split(".")], m[3]);
    }
  }

  // A key no clause types is held to nothing but the allow-list, so any value will do.
  for (const key of keys) if (!(key in doc)) doc[key] = "x";

  // `revisionAdvances()` reads `cloud.revision` on both sides of an update.
  doc.cloud = { revision: 1 };
  return doc;
}

// ------------------------------------------------------------------------- padding and the API

/** N no-op terms as a balanced tree, so depth stays logarithmic and the parser never balks. */
function padding(n) {
  if (n <= 0) return "true";
  if (n === 1) return "1 == 1";
  const half = Math.floor(n / 2);
  return `(${padding(half)} && ${padding(n - half)})`;
}

const SAVE_ALLOW = /(allow\s+(create|update)\s*:\s*if\s+)(isOwner\(uid\))/g;

function padded(source, n) {
  const hits = source.match(SAVE_ALLOW)?.length ?? 0;
  if (hits !== 2) throw new Error(`expected the save's create and update rules, found ${hits}`);
  return source.replace(SAVE_ALLOW, `$1${padding(n)} && $3`);
}

async function evaluate(source, testCase) {
  const r = await fetch(`https://firebaserules.googleapis.com/v1/projects/${PROJECT}:test`, {
    method: "POST",
    headers: { Authorization: `Bearer ${token}`, "x-goog-user-project": PROJECT, "Content-Type": "application/json" },
    body: JSON.stringify({ source: { files: [{ name: "firestore.rules", content: source }] },
                           testSuite: { testCases: [{ expectation: "ALLOW", ...testCase }] } }),
  });
  const json = await r.json();
  if (!r.ok) throw new Error(`Rules test API ${r.status}: ${JSON.stringify(json).slice(0, 400)}`);
  if (!json.testResults) {
    const issue = (json.issues ?? [])[0];
    throw new Error(`the ruleset does not compile: ${issue ? `line ${issue.sourcePosition?.line}: ${issue.description}` : JSON.stringify(json).slice(0, 400)}`);
  }
  const result = json.testResults[0];
  return { allowed: result.state === "SUCCESS", why: (result.debugMessages ?? []).join(" ") };
}

/** The largest N for which `allowed(N)` holds, given `allowed(0)`; bisected below `ceiling`. */
async function largest(allowed, ceiling) {
  let lo = 0, hi = ceiling;
  while (hi - lo > 1) {
    const mid = (lo + hi) >> 1;
    if (await allowed(mid)) lo = mid; else hi = mid;
  }
  return lo;
}

// ------------------------------------------------------------------------- the measurement

const UID = "budget-probe";
const PATH = `/databases/(default)/documents/players/${UID}`;
const save = heaviestSave(rules);
const before = structuredClone(save);
const after = structuredClone(save);
after.cloud.revision = 2;

const cases = {
  create: { request: { auth: { uid: UID }, path: PATH, method: "create", resource: { data: save } } },
  update: { resource: { data: before },
            request: { auth: { uid: UID }, path: PATH, method: "update", resource: { data: after } } },
};

// One term's price: the most terms a rule of nothing else can evaluate.
const CALIBRATION = `rules_version = '2';
service cloud.firestore { match /databases/{database}/documents { match /c/{id} {
  allow create: if PAD;
} } }`;
const calibrate = (n) => evaluate(CALIBRATION.replace("PAD", padding(n)),
  { request: { auth: { uid: UID }, path: "/databases/(default)/documents/c/x", method: "create", resource: { data: {} } } })
  .then((r) => r.allowed);

const termsInCap = await largest(calibrate, 2 * CAP);
const perTerm = CAP / (termsInCap + 1);

console.log(`rules:   ${rulesPath}`);
console.log(`save:    ${Object.keys(save).length} top-level keys, every optional clause paid for`);
console.log(`padding: one term costs ~${perTerm.toFixed(2)} expressions (${termsInCap} fit under the cap)\n`);

let failed = false;
for (const [name, testCase] of Object.entries(cases)) {
  const plain = await evaluate(padded(rules, 0), testCase);
  if (!plain.allowed) {
    console.log(`FAIL  ${name}: the heaviest save is refused outright ${plain.why ? `(${plain.why})` : ""}`);
    failed = true;
    continue;
  }

  const terms = await largest((n) => evaluate(padded(rules, n), testCase).then((r) => r.allowed), termsInCap + 1);
  const headroom = Math.floor(terms * perTerm);
  const ok = headroom >= REQUIRED_HEADROOM;
  if (!ok) failed = true;

  console.log(`${ok ? "ok  " : "FAIL"}  ${name}: ~${CAP - headroom} of ${CAP} expressions used, ` +
              `headroom ~${headroom} (required ${REQUIRED_HEADROOM})`);
}

process.exit(failed ? 1 : 0);
