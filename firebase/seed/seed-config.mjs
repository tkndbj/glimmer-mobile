#!/usr/bin/env node
/**
 * Publishes the reward table and product catalog into Firestore.
 *
 * The server has to derive earned currency independently of the client — that is what
 * makes a forged save unable to mint money — and to do that it needs the same reward
 * table and the same level-to-chapter mapping the client uses. Rather than maintaining
 * a second copy by hand, this generates them from the shipped content, so the two can
 * only disagree if somebody forgets to run it.
 *
 * Run it after any change to progression.json, to a chapter's levels, or to the seed
 * balances:
 *
 *     node firebase/seed/seed-config.mjs
 *
 * Authentication reuses the gcloud login rather than a service-account key file, so
 * there is no long-lived credential sitting in the repository to leak.
 */

import { readFileSync, existsSync, readdirSync } from "node:fs";
import { execSync } from "node:child_process";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { pathToFileURL } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(HERE, "..", "..");
const CONTENT = join(REPO, "Assets", "StreamingAssets", "Content");
const CURRENCY_CS = join(REPO, "Assets", "Game", "Scripts", "Domain", "Persistence", "CurrencyLedger.cs");

const PROJECT = "glimmer-groove-1cd60";

// ---------------------------------------------------------------- reading content
function readJson(path) {
  if (!existsSync(path)) throw new Error(`missing ${path}`);
  return JSON.parse(readFileSync(path, "utf8"));
}

/**
 * The starting balances, read out of the C# rather than restated here.
 *
 * A second copy of these numbers would drift, and the symptom would be a player's
 * balance changing the first time they sync — the client granting one seed and the
 * server another. Failing loudly if the constants cannot be found is the point: a
 * rename should break this script, not quietly desynchronise the economy.
 */
function readSeeds() {
  const source = readFileSync(CURRENCY_CS, "utf8");

  const grab = (name) => {
    const match = source.match(new RegExp(`public\\s+const\\s+long\\s+${name}\\s*=\\s*(\\d+)`));
    if (!match) {
      throw new Error(
        `could not find Currency.${name} in CurrencyLedger.cs — if it was renamed, update this script ` +
        `rather than hardcoding the value, or the client and server will seed different balances`
      );
    }
    return Number(match[1]);
  };

  return { credits: grab("SeedCredits"), gems: grab("SeedGems") };
}

/**
 * Rule resolution is imported from the built functions rather than reimplemented.
 *
 * The seeder decides what the server will believe, so if it resolved overrides even
 * slightly differently from the code that reads them, the server would enforce numbers
 * nobody authored. Requiring a build first is a small price for there being exactly one
 * implementation of the rule.
 */
const compiled = join(REPO, "firebase", "functions", "lib", "progression.js");
if (!existsSync(compiled)) {
  throw new Error(
    "firebase/functions/lib/progression.js is missing — run 'npm --prefix firebase/functions run build' first.\n" +
    "The seeder shares its reward-resolution logic with the server rather than keeping a second copy."
  );
}
const { resolveRule, buildChapterRules, DEFAULT_RULE } = await import(pathToFileURL(compiled).href);

function buildProgressionConfig() {
  const progression = readJson(join(CONTENT, "progression.json"));
  const manifest = readJson(join(CONTENT, "manifest.json"));

  const defaults = resolveRule(progression.rewards, DEFAULT_RULE);
  const chapterRewards = buildChapterRules(progression.chapterRewards, defaults);

  // levelId → chapterId, read from the chapters the manifest actually lists. A chapter
  // file left on disk but removed from the manifest is not shipped, so its levels must
  // not earn anything either.
  const levelChapters = {};
  let levelCount = 0;

  for (const listed of manifest.chapters ?? []) {
    if (!listed?.id || listed.disabled) continue;

    const chapter = readJson(join(CONTENT, "chapters", `${listed.id}.json`));
    for (const level of chapter.levels ?? []) {
      if (!level?.id) continue;
      if (levelChapters[level.id]) {
        throw new Error(`level id '${level.id}' appears in more than one chapter`);
      }
      levelChapters[level.id] = listed.id;
      levelCount++;
    }
  }

  if (levelCount === 0) throw new Error("no levels found; refusing to seed an empty catalog");

  return {
    config: {
      version: manifest.progressionVersion ?? 1,
      rewards: defaults,
      chapterRewards,
      levelChapters,
      seeds: readSeeds(),
      daily: readDaily(progression),
    },
    levelCount,
  };
}

/**
 * The daily chest table, published verbatim so the server can re-roll a chest for
 * itself.
 *
 * This is not optional tuning. `claimAwards` recomputes what a chest was worth rather
 * than believing the client, and it cannot do that without the same weights and bands
 * the client rolled against. A config document missing this block makes the server
 * refuse every award — deliberately, since granting a guess would be inventing money —
 * so it is validated here rather than discovered in production.
 */
function readDaily(progression) {
  const daily = progression.daily;

  if (!daily || !Array.isArray(daily.chests) || daily.chests.length === 0) {
    throw new Error(
      "progression.json has no 'daily' block. The server re-rolls each chest to decide " +
      "what it pays, so seeding without one would make every daily chest fail to grant."
    );
  }

  const chests = daily.chests.map((chest, index) => {
    const guaranteed = (chest.guaranteed ?? []).map((band) => band8(band, index, "guaranteed"));
    if (guaranteed.length === 0) {
      throw new Error(`daily chest ${index} guarantees nothing; every chest must pay something`);
    }

    const options = (chest.options ?? []).map((option) => ({
      ...band8(option, index, "option"),
      weight: Math.max(1, Math.floor(option.weight ?? 1)),
    }));

    return { guaranteed, options };
  });

  return { runsPerChest: Math.max(1, Math.floor(daily.runsPerChest ?? 3)), chests };
}

const DROP_KINDS = new Set(["credits", "gems", "hearts", "heart_boost"]);

function band8(band, chestIndex, role) {
  if (!band || !DROP_KINDS.has(band.kind)) {
    throw new Error(`daily chest ${chestIndex} ${role} names unknown reward kind '${band?.kind}'`);
  }

  const min = Math.floor(band.min ?? 0);
  const max = Math.floor(band.max ?? 0);

  if (min < 1 || max < min) {
    throw new Error(`daily chest ${chestIndex} ${role} '${band.kind}' has band ${min}..${max}`);
  }

  return { kind: band.kind, min, max };
}

// -------------------------------------------------------- Firestore REST encoding
function encode(value) {
  if (value === null || value === undefined) return { nullValue: null };
  if (typeof value === "boolean") return { booleanValue: value };
  if (typeof value === "number") {
    return Number.isInteger(value) ? { integerValue: String(value) } : { doubleValue: value };
  }
  if (typeof value === "string") return { stringValue: value };
  if (Array.isArray(value)) return { arrayValue: { values: value.map(encode) } };

  const fields = {};
  for (const [k, v] of Object.entries(value)) fields[k] = encode(v);
  return { mapValue: { fields } };
}

function accessToken() {
  try {
    // execSync rather than execFileSync-with-shell: gcloud is a .cmd on Windows and
    // needs a shell, but passing an argument array through one is a quoting hazard
    // Node now warns about. The command is a constant, so a single string is safe.
    return execSync("gcloud auth print-access-token", {
      encoding: "utf8", stdio: ["ignore", "pipe", "pipe"],
    }).trim();
  } catch (e) {
    throw new Error(
      "could not get a token from gcloud. Run 'gcloud auth login' first.\n" + (e.stderr ?? e.message)
    );
  }
}

async function writeDoc(token, path, data) {
  const fields = {};
  for (const [k, v] of Object.entries(data)) fields[k] = encode(v);

  const url =
    `https://firestore.googleapis.com/v1/projects/${PROJECT}/databases/(default)/documents/${path}` +
    `?${Object.keys(data).map((k) => `updateMask.fieldPaths=${encodeURIComponent(k)}`).join("&")}`;

  const response = await fetch(url, {
    method: "PATCH",
    headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
    body: JSON.stringify({ fields }),
  });

  if (!response.ok) {
    throw new Error(`writing ${path} failed: ${response.status} ${await response.text()}`);
  }
}

// ------------------------------------------------------------------------- main
const { config, levelCount } = buildProgressionConfig();
const token = accessToken();

await writeDoc(token, "config/progression", config);
console.log(
  `config/progression: ${levelCount} level(s), ` +
  `${Object.keys(config.chapterRewards).length} chapter override(s), ` +
  `${config.daily.chests.length} daily chest(s) every ${config.daily.runsPerChest} run(s), ` +
  `seeds ${config.seeds.credits} credits / ${config.seeds.gems} gems`
);

const productsPath = join(HERE, "products.json");
if (existsSync(productsPath)) {
  const products = readJson(productsPath);
  await writeDoc(token, "config/products", products);
  console.log(`config/products: ${Object.keys(products).length} product(s)`);
} else {
  console.log("config/products: skipped, no firebase/seed/products.json — in-app purchases stay inert");
}

// Sanity: a chapter file on disk that nobody lists is usually a mistake worth naming.
const onDisk = readdirSync(join(CONTENT, "chapters")).filter((f) => f.endsWith(".json")).length;
const listed = (readJson(join(CONTENT, "manifest.json")).chapters ?? []).length;
if (onDisk !== listed) {
  console.log(`note: ${onDisk} chapter file(s) on disk, ${listed} listed in the manifest`);
}
