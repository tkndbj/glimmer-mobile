#!/usr/bin/env node
/**
 * Showcase groves — ten designed villages, so the boards are not empty on launch day.
 *
 *     node firebase/seed/seed-showcase.mjs --dry-run             # print them, write nothing
 *     node firebase/seed/seed-showcase.mjs --dry-run --dump out/ # also write render layouts
 *     node firebase/seed/seed-showcase.mjs                       # write them
 *     node firebase/seed/seed-showcase.mjs --remove              # take them all down again
 *
 * ## What this is, and what it is not
 *
 * A leaderboard with nobody on it teaches a player, in one glance, that the feature is
 * dead — and the Grovement's whole argument is that a grove is worth building because
 * somebody else might see it. These ten exist so the first real player has something to
 * be inspired by. They are **not** a growth trick and they are not permanent: every
 * account is written under a `showcase-` id, every card carries `synthetic: true`, and
 * `--remove` deletes the lot in one command. When there are real groves worth visiting,
 * take them down.
 *
 * ## The villages are authored, not generated
 *
 * `showcase-villages.json` holds each village as a fourteen-line plan — one character per
 * tile — and a legend saying which piece each character stands for. That file is the
 * design; this script only compiles, proves and writes it.
 *
 * It replaced two generations of procedural composer, and the reason is worth keeping.
 * A composer rolls dice inside rules — "trees at the back", "flowers by the path" — and
 * every rule was right, and the villages still read as objects dropped at random, because
 * design is not a set of constraints on where things may go, it is a decision about where
 * each thing *does* go. A fence with a gate in it, a path that arrives somewhere, an
 * orchard in rows, a campfire with the logs around it: none of that is a probability. A
 * plan is also the only representation a person can look at and correct one tile at a
 * time, which is how these were made — drawn, rendered with `Tools/render_grove.py`,
 * looked at, and redrawn, twenty-odd times.
 *
 * Four facts about the art decide the vocabulary, all measured by rendering rather than
 * read off the catalog, and a plan that ignores them looks wrong however carefully it is
 * composed:
 *
 *   * **Only four fences join into a line** — `fence_long`, `fence_wide`, `fence_low` and
 *     `rope_fence` — and each joins along one diagonal as drawn and along the other when
 *     mirrored (`fence_long` and `rope_fence` run along +col flipped and +row unflipped;
 *     `fence_wide` and `fence_low` the other way round). `fence_picket` and `fence_timber`
 *     are too short for the tile pitch and read as a row of loose posts whichever way they
 *     are laid, so no plan uses them for a line.
 *   * **`plank_bridge` is the only paving that tiles**, as a raised boardwalk with posts,
 *     and `stepping_stones` the only ground path that survives repetition. Everything else
 *     on the path shelf is an object.
 *   * **Depth is a constraint.** The field draws back to front by a piece's front tile, so
 *     anything tall in front of the hall covers it. Woods go behind and to the sides; the
 *     ground in front of the door holds paths, beds, lamps at the edges and friends.
 *   * **Only the five companions with board flipbooks may stand in a grove** — the other
 *     twenty-six draw their UI portrait, a head the size of the house. See `CRITTERS`.
 *
 * ## What is written, and why the card is not written by hand
 *
 *   players/{uid}                  the save: ledger, wallet, stock, arrangement, land
 *   players/{uid}/private/wallet   granted currency and the name holding — server-owned
 *   names/{key}                    the name's reservation, so a real player cannot take it
 *   groves/{uid}                   the public card
 *
 * The card is built by `buildCard`/`groveWorth` out of the compiled functions, so it is
 * **exactly** what `publishGrove` would have written from the same save — including the
 * keeper level, which is derived from the star ledger here the way the server derives it,
 * because a card claiming companions above the level its own ledger reaches is the one
 * shape `groveWorth` refuses to score. Writing a card by hand would put a number on the
 * board that the server's own derivation disagrees with, and it would drift the first time
 * the scoring rule changed.
 *
 * Everything that stands in a village is proved held before anything is written: bought
 * by the copy in `homesteadStock`, free to everybody, earned by a level the ledger clears,
 * or a resident on the companion roster. A visitor cannot tell, and neither can any check
 * the game runs — the picker is what normally guarantees it — so a village assembled by a
 * script has to prove it for itself.
 */

import { readFileSync, existsSync, writeFileSync, mkdirSync } from "node:fs";
import { execSync } from "node:child_process";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(HERE, "..", "..");
const CONTENT = join(REPO, "Assets", "StreamingAssets", "Content");

const PROJECT = "glimmer-groove-1cd60";
const FS = `https://firestore.googleapis.com/v1/projects/${PROJECT}/databases/(default)/documents`;

const DRY = process.argv.includes("--dry-run");
const REMOVE = process.argv.includes("--remove");

/**
 * Where to write each village as a layout file, if anywhere.
 *
 * These are read by `Tools/render_grove.py`, which draws a grove exactly as the game does
 * without opening Unity. A composition can only be judged as a picture, and the loop that
 * goes through the Editor is far too slow to design against — so this exists to make the
 * loop `--dump` then render then look.
 */
const dumpAt = process.argv.indexOf("--dump");
const DUMP = dumpAt >= 0 ? process.argv[dumpAt + 1] : null;

const readJson = (p) => JSON.parse(readFileSync(p, "utf8"));

const LIB = join(REPO, "firebase", "functions", "lib");
if (!existsSync(join(LIB, "grove.js"))) {
  throw new Error("build the functions first: npm --prefix firebase/functions run build");
}
const { buildCard, groveWorth, leagueOf, derivedXp, keeperLevel, sanitiseName } =
  await import(pathToFileURL(join(LIB, "grove.js")).href);
const { resolveRule, buildChapterRules, DEFAULT_RULE } =
  await import(pathToFileURL(join(LIB, "progression.js")).href);
const { nameKey } = await import(pathToFileURL(join(LIB, "names.js")).href);

const homestead = readJson(join(CONTENT, "homestead.json"));
const manifest = readJson(join(CONTENT, "manifest.json"));
const progression = readJson(join(CONTENT, "progression.json"));

// ---------------------------------------------------------------- the catalog
const FLOOR = homestead.floor;
const COLS = FLOOR.cols;
const ROWS = FLOOR.rows;
const HALL_COL = Number(FLOOR.hallTile.slice(2, 5));
const HALL_ROW = Number(FLOOR.hallTile.slice(6, 9));
const HALL_COLS = FLOOR.hallCols ?? 1;
const HALL_ROWS = FLOOR.hallRows ?? 1;

const tileId = (col, row) =>
  `t_${String(col).padStart(3, "0")}_${String(row).padStart(3, "0")}`;

const PIECES = {};
for (const piece of homestead.pieces) PIECES[piece.id] = piece;

// Residents are projected in exactly as `GroveResidents.From` does: `friend_` + companion.
const COMPANIONS = {};
for (const c of manifest.companions ?? []) {
  if (c.disabled) continue;
  COMPANIONS[c.id] = { cost: c.unlockCost ?? 0, level: c.unlockLevel ?? 0 };
}

const REGIONS = {};
for (const region of FLOOR.regions) REGIONS[region.id] = region;

/** Starter land is never written down (invariant 16e): "absent" and "bought nothing" are one fact. */
const isStarter = (region) => (region.cost ?? 0) <= 0 && (region.gems ?? 0) <= 0;

/**
 * The grove catalog as the server holds it — the same derivation as `seed-config.mjs`'s
 * `buildGroveConfig`, so a card built here scores as `publishGrove` would score it.
 *
 * Two entries are easy to leave out and each is a visible bug. `bundles` is what makes a
 * copy worth `cost / bundle`; without it every fence panel scores as a whole bundle. And a
 * gem-priced region has to be present *at zero*: `buildCard` filters the published `land[]`
 * through this table, so a gem region left out would delete its ground from every
 * visitor's view with everything on it floating over nothing.
 */
const GROVE_CONFIG = (() => {
  const pieces = {};
  const bundles = {};
  const dwellings = {};
  for (const piece of homestead.pieces) {
    const cost = Math.floor(piece.cost ?? 0);
    if (cost > 0) pieces[piece.id] = cost;
    const bundle = Math.floor(piece.bundle ?? 1);
    if (cost > 0 && bundle > 1) bundles[piece.id] = bundle;
    if (piece.kind === "dwelling") dwellings[piece.id] = Math.floor(piece.tier ?? 0);
  }
  const regions = {};
  for (const region of FLOOR.regions) {
    if (!isStarter(region)) regions[region.id] = Math.floor(region.cost ?? 0);
  }
  const companions = {};
  for (const [id, c] of Object.entries(COMPANIONS)) {
    if (c.cost > 0) companions[id] = { cost: c.cost, level: c.level };
  }
  return {
    version: Math.floor(manifest.groveVersion ?? 1),
    pieces, bundles, regions, companions, dwellings,
    stars: (homestead.score?.stars ?? []).slice().sort((a, b) => a - b),
  };
})();

/**
 * The reward rules and keeper curve, as `seed-config.mjs` publishes them, so the keeper
 * level on a card is the one `publishGrove` would derive from the same ledger.
 */
const LEVEL_CHAPTERS = {};
for (const chapter of manifest.chapters ?? []) {
  if (!chapter?.id || chapter.disabled) continue;
  const body = readJson(join(CONTENT, "chapters", `${chapter.id}.json`));
  for (const level of body.levels ?? []) LEVEL_CHAPTERS[level.id] = chapter.id;
}
const LEVEL_IDS = Object.keys(LEVEL_CHAPTERS);

const REWARDS = resolveRule(progression.rewards, DEFAULT_RULE);
const PROGRESSION_CONFIG = {
  rewards: REWARDS,
  chapterRewards: buildChapterRules(progression.chapterRewards, REWARDS),
  levelChapters: LEVEL_CHAPTERS,
};
const KEEPER_CURVE = {
  maxLevel: Math.floor(progression.maxLevel ?? 60),
  xpToNext: (progression.xpToNext ?? []).map(Math.floor),
  tailXpToNext: Math.floor(progression.tailXpToNext ?? 0),
  tailXpIncrement: Math.floor(progression.tailXpIncrement ?? 0),
};

/**
 * Pieces held without being bought, and the two ways that happens.
 *
 * `FREE` is what nothing gates and nothing charges for — held by everybody. `REQUIRED` is
 * what play unlocks, keyed by what it asks for. Neither ever appears in the stock: that is
 * a record of *purchases*, so listing an earned piece would be claiming a purchase that
 * never happened, and it is worth nothing so it would not move the score either.
 */
const FREE = new Set();
const REQUIRED = new Map();
for (const piece of homestead.pieces) {
  if ((piece.cost ?? 0) > 0 || piece.kind === "dwelling") continue;
  if (piece.requiresLevel) REQUIRED.set(piece.id, { level: piece.requiresLevel });
  else if (piece.requiresChapter) REQUIRED.set(piece.id, { chapter: piece.requiresChapter });
  else FREE.add(piece.id);
}

function earnedBy(save, id) {
  const rule = REQUIRED.get(id);
  if (!rule) return false;
  const cleared = (levelId) => (save.levels[levelId]?.stars ?? 0) > 0;
  if (rule.level) return cleared(rule.level);
  return LEVEL_IDS.filter((levelId) => LEVEL_CHAPTERS[levelId] === rule.chapter).every(cleared);
}

/**
 * The five companions drawn as in-world critters, and the reason the other twenty-six may
 * not stand in a showcase village.
 *
 * A companion's grove art is its *portrait* unless it has a board flipbook
 * (`GroveResidents.From`), and a portrait is UI art: measured off the shipped files they
 * draw between 200 and 436 pixels wide against a 220 pixel tile and a 459 pixel cottage,
 * facing the camera with no ground contact. Standing one in a village puts a head the size
 * of the house on the lawn. The five with flipbooks draw at 133 to 192 and are the
 * creatures the board itself uses, so they read as livestock in a field.
 *
 * **This is a workaround for something real, not a preference.** A player who buys a
 * portrait-only companion and stands them in their own grove gets the same giant head, and
 * no validator would ever mention it. The fix is a per-companion draw scale in the roster
 * rather than `GroveResidents.Scale` being one constant for art cut two different ways —
 * out of scope here, and worth doing before the roster grows again.
 */
const CRITTERS = new Set(
  (manifest.companions ?? []).filter((c) => c.animated && !c.disabled).map((c) => c.id)
);

// ------------------------------------------------------------------ a die
/**
 * A seeded generator, so a village's purchase history is a function of its name.
 *
 * Deterministic on purpose: re-running this script has to rebuild the *same* ten villages
 * rather than ten new ones. Only the breadth of a keeper's collection and their star
 * ledger are rolled; nothing that stands on the floor ever is.
 */
function die(seed) {
  let state = 2166136261 >>> 0;
  for (const ch of seed) state = Math.imul(state ^ ch.charCodeAt(0), 16777619) >>> 0;
  if (state === 0) state = 2166136261;
  return {
    next() {
      state ^= state << 13; state >>>= 0;
      state ^= state >>> 17;
      state ^= state << 5;  state >>>= 0;
      return state / 4294967296;
    },
    chance(p) { return this.next() < p; },
  };
}

// ------------------------------------------------------------------ the plan
/**
 * Compiles a village's plan into placements, refusing anything the game could not hold.
 *
 * A plan is `ROWS` strings of `COLS` characters: `.` is empty ground, `#` is the hall's
 * footprint, `+` is ground covered by a multi-tile piece anchored at its top-left, and any
 * other character is looked up in the village's legend — a piece id, or `[id, "flip"]`
 * for a mirrored one. Footprints follow `HomesteadPiece.Footprint`: `cols` x `rows`,
 * swapped when flipped, exactly as `GroveOccupancy` reads them.
 *
 * Every check here is something the picker enforces for a real player: on owned ground,
 * off the hall, one piece per tile, a footprint that fits. A script that skipped them
 * would write a grove the merge rule tolerates (invariant 16i keeps both of two
 * overlapping footprints) and the visit screen draws — just wrongly.
 */
function compile(village) {
  const owned = new Set();
  for (const region of FLOOR.regions) {
    if (!isStarter(region) && !village.land.includes(region.id)) continue;
    for (let c = region.col; c < region.col + region.cols; c++) {
      for (let w = region.row; w < region.row + region.rows; w++) owned.add(`${c},${w}`);
    }
  }

  const onHall = (c, w) =>
    c >= HALL_COL && c < HALL_COL + HALL_COLS && w >= HALL_ROW && w < HALL_ROW + HALL_ROWS;

  const plan = village.plan;
  if (plan.length !== ROWS) throw new Error(`${village.id}: plan has ${plan.length} rows, floor has ${ROWS}`);

  const taken = new Set();
  const placements = [];

  for (let w = 0; w < ROWS; w++) {
    const line = plan[w];
    if (line.length !== COLS) {
      throw new Error(`${village.id}: row ${w} has ${line.length} columns, floor has ${COLS}: ${line}`);
    }
    for (let c = 0; c < COLS; c++) {
      const ch = line[c];
      if (ch === "." || ch === "+") continue;
      if (ch === "#") {
        if (!onHall(c, w)) throw new Error(`${village.id}: '#' at ${c},${w} is not the hall`);
        continue;
      }

      const entry = village.legend[ch];
      if (!entry) throw new Error(`${village.id}: no legend entry for '${ch}' at ${c},${w}`);
      const [piece, flipped] = Array.isArray(entry) ? [entry[0], entry[1] === "flip"] : [entry, false];

      const def = PIECES[piece];
      const isResident = piece.startsWith("friend_");
      if (!def && !(isResident && COMPANIONS[piece.slice(7)])) {
        throw new Error(`${village.id}: unknown piece '${piece}' at ${c},${w}`);
      }
      if (isResident && !CRITTERS.has(piece.slice(7))) {
        throw new Error(`${village.id}: ${piece} draws a portrait, not a critter — see CRITTERS`);
      }

      let fc = def?.cols ?? 1, fr = def?.rows ?? 1;
      if (flipped) [fc, fr] = [fr, fc];

      for (let dc = 0; dc < fc; dc++) {
        for (let dw = 0; dw < fr; dw++) {
          const cc = c + dc, ww = w + dw;
          const key = `${cc},${ww}`;
          if (!owned.has(key)) throw new Error(`${village.id}: ${piece} at ${c},${w} covers unowned ${key}`);
          if (onHall(cc, ww)) throw new Error(`${village.id}: ${piece} at ${c},${w} covers the hall`);
          if (taken.has(key)) throw new Error(`${village.id}: ${piece} at ${c},${w} overlaps at ${key}`);
          if ((dc || dw) && plan[ww][cc] !== "+") {
            throw new Error(`${village.id}: ${piece} at ${c},${w} needs '+' at ${key}`);
          }
          taken.add(key);
        }
      }

      placements.push({ slot: tileId(c, w), piece, flipped });
    }
  }

  // Sorted by slot, the way the client's ledger walks them (`SaveDelta` walks ids in order).
  placements.sort((a, b) => (a.slot < b.slot ? -1 : a.slot > b.slot ? 1 : 0));
  return { placements, tiles: owned.size };
}

// --------------------------------------------------------------- what they own
/**
 * A shopping list, not a switch that turns everything on.
 *
 * Priced decor is bought by the copy (invariant 16h), so the stock holds, for every priced
 * piece standing in the village, at least as many copies as stand there, rounded up to
 * whole bundles — which is what buying enough to build it would have left. On top of that
 * comes the home ladder up to this keeper's rung (rungs are bought one after another, so
 * owning the manor and not the lodge is not a thing that can have happened), a run of
 * companions as far up the ladder as the keeper's level reaches, and a wider collection of
 * things bought and not used — weighted to the cheap end, the way a real list is, and
 * scaled by `breadth` so the ten are worth different amounts.
 *
 * Nothing here is aimed at a score: the worth follows from what is held, as it does for
 * a real player.
 */
function holdings(village, placements, level) {
  const rng = die(village.id + ":own");

  const standing = new Map();
  for (const { piece } of placements) standing.set(piece, (standing.get(piece) ?? 0) + 1);

  const stock = new Map();
  const bundleOf = (id) => GROVE_CONFIG.bundles[id] ?? 1;

  for (const [id, count] of standing) {
    if (!(id in GROVE_CONFIG.pieces)) continue;
    const bundle = bundleOf(id);
    stock.set(id, Math.ceil(count / bundle) * bundle);
  }

  const tier = GROVE_CONFIG.dwellings[village.home];
  if (tier === undefined) throw new Error(`${village.id}: '${village.home}' is not a dwelling`);
  for (const [id, t] of Object.entries(GROVE_CONFIG.dwellings)) {
    if (t <= tier && id in GROVE_CONFIG.pieces) stock.set(id, 1);
  }

  const rest = Object.keys(GROVE_CONFIG.pieces)
    .filter((id) => !stock.has(id) && !(id in GROVE_CONFIG.dwellings))
    .sort((a, b) => GROVE_CONFIG.pieces[a] - GROVE_CONFIG.pieces[b]);
  const depth = Math.floor(rest.length * (village.breadth ?? 0.75));
  for (const id of rest.slice(0, depth)) {
    if (rng.chance(0.86)) stock.set(id, bundleOf(id));
  }

  // Companions are bought in ladder order and only as far as the keeper's level reaches:
  // `groveWorth` drops a companion above the level outright, so holding one would be a
  // purchase the server refuses to score and the client refuses to make.
  const companions = Object.entries(COMPANIONS)
    .filter(([, c]) => c.cost > 0 && c.level <= level)
    .sort((a, b) => a[1].level - b[1].level)
    .map(([id]) => id);

  const land = village.land.filter((id) => {
    const region = REGIONS[id];
    if (!region) throw new Error(`${village.id}: '${id}' is not a region`);
    return !isStarter(region);
  });

  return {
    stock: [...stock].map(([id, copies]) => ({ id, copies })).sort((a, b) => (a.id < b.id ? -1 : 1)),
    land: land.slice().sort(),
    companions,
    gemsSpent: land.reduce((n, id) => n + Math.floor(REGIONS[id].gems ?? 0), 0),
  };
}

// ------------------------------------------------------------------- the save
/**
 * A believable ledger: every glade cleared, most of them three-starred, a few not — and
 * the keeper level *derived* from it, exactly as the server derives it, so the card is
 * honest about what this account has reached.
 */
function ledger(village) {
  const rng = die(village.id + ":play");
  const levels = {};
  let firstClear = 1750000000;
  for (const id of LEVEL_IDS) {
    const stars = rng.chance(0.8) ? 3 : rng.chance(0.6) ? 2 : 1;
    firstClear += Math.floor(rng.next() * 90000) + 20000;
    levels[id] = {
      stars,
      bestMoves: 30 + Math.floor(rng.next() * 40),
      bestMillis: 0,
      clears: 1 + Math.floor(rng.next() * 5),
      firstClearedUnix: firstClear,
      lastPlayedUnix: firstClear + 100000,
      bestRank: 0,
    };
  }
  const xp = derivedXp(levels, PROGRESSION_CONFIG);
  return { levels, xp, level: keeperLevel(xp, KEEPER_CURVE), lastUnix: firstClear + 200000 };
}

function buildSave(village, play, held, placements, nowUnix) {
  if (!held.companions.includes(village.avatar)) {
    throw new Error(
      `${village.id}: wears '${village.avatar}', which a level ${play.level} keeper cannot hold`
    );
  }

  // The starter is held by everybody and never bought, so it is not in the roster's
  // purchased set — exactly as `CompanionLedger` keeps it.
  return {
    schemaVersion: 21,
    updatedUnix: play.lastUnix,
    legacyImportDone: true,
    lastPlayedLevelId: LEVEL_IDS[LEVEL_IDS.length - 1],
    levels: play.levels,
    settings: { music: 1, sfx: 1, haptics: 1, board: 1, language: "en" },
    wallet: {
      displayName: village.name, displayNameSetUnix: play.lastUnix,
      avatarId: village.avatar, avatarSetUnix: play.lastUnix,
      heartsProduced: 120, heartsSpent: 96, heartsDueUnix: 0,
      hearts: 5, heartsNextRefillUnix: 0, heartBoostUntilUnix: 0,
      hintsProduced: 30, hintsSpent: 27, hintsDueUnix: 0,
    },
    progression: { xpHighWater: play.xp, levelHighWater: play.level },
    cloud: { userId: village.id, revision: 1, lastSyncedUnix: nowUnix, deviceId: "showcase" },
    companionsOwned: held.companions,
    homesteadStock: held.stock,
    // The v19 mirror, read only when the stock section is empty (invariant 16h).
    homesteadOwned: held.stock.map((row) => row.id),
    homesteadPlaced: placements.map((p) => ({ ...p, setUnix: play.lastUnix })),
    groveLandOwned: held.land,
    heartContainersOwned: [],
    heartContainersRevoked: [],
  };
}

// ------------------------------------------------------------------ encoding
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

function decode(value) {
  if (!value || typeof value !== "object") return undefined;
  if ("stringValue" in value) return value.stringValue;
  if ("integerValue" in value) return Number(value.integerValue);
  if ("doubleValue" in value) return value.doubleValue;
  if ("booleanValue" in value) return value.booleanValue;
  if ("nullValue" in value) return null;
  if ("arrayValue" in value) return (value.arrayValue.values ?? []).map(decode);
  if ("mapValue" in value) {
    const out = {};
    for (const [k, v] of Object.entries(value.mapValue.fields ?? {})) out[k] = decode(v);
    return out;
  }
  return undefined;
}

function accessToken() {
  return execSync("gcloud auth print-access-token", { encoding: "utf8" }).trim();
}

async function write(token, path, data) {
  const response = await fetch(`${FS}/${path}`, {
    method: "PATCH",
    headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
    body: JSON.stringify({ fields: Object.fromEntries(
      Object.entries(data).map(([k, v]) => [k, encode(v)])
    ) }),
  });
  if (!response.ok) throw new Error(`${path}: ${response.status} ${await response.text()}`);
}

async function read(token, path) {
  const response = await fetch(`${FS}/${path}`, { headers: { Authorization: `Bearer ${token}` } });
  if (response.status === 404) return null;
  if (!response.ok) throw new Error(`${path}: ${response.status} ${await response.text()}`);
  const doc = await response.json();
  const out = {};
  for (const [k, v] of Object.entries(doc.fields ?? {})) out[k] = decode(v);
  return out;
}

async function remove(token, path) {
  const response = await fetch(`${FS}/${path}`, {
    method: "DELETE", headers: { Authorization: `Bearer ${token}` },
  });
  if (!response.ok && response.status !== 404) {
    throw new Error(`${path}: ${response.status} ${await response.text()}`);
  }
}

// --------------------------------------------------------------------- names
/**
 * Reserves the keeper's name the way `claimName` does — `names/{key}` owned by the uid,
 * and the holding on the private wallet that `publishGrove` reads the board name from.
 *
 * Without this the card would carry the name while the reservation stayed free, and the
 * first real player to claim "Nyx" would stand on the boards beside a synthetic "Nyx" —
 * which is exactly the duplicate invariant 19d exists to make impossible. A reservation
 * already held by somebody else is a hard stop, never a silent fallback.
 */
async function reserveName(token, village, nowUnix) {
  const key = nameKey(village.name);
  const existing = await read(token, `names/${key}`);
  if (existing && existing.uid !== village.id) {
    throw new Error(`${village.id}: the name '${village.name}' (${key}) is held by ${existing.uid}`);
  }
  await write(token, `names/${key}`, { uid: village.id, atUnix: nowUnix });
  return { key, public: sanitiseName(village.name), atUnix: nowUnix, deniedUnix: 0 };
}

async function releaseName(token, village) {
  const key = nameKey(village.name);
  const existing = await read(token, `names/${key}`);
  if (existing && existing.uid === village.id) await remove(token, `names/${key}`);
}

// --------------------------------------------------------------------- main
const VILLAGES = readJson(join(HERE, "showcase-villages.json"));
if (VILLAGES.length === 0) throw new Error("showcase-villages.json holds no villages");

const token = DRY ? null : accessToken();

if (REMOVE) {
  for (const village of VILLAGES) {
    await releaseName(token, village);
    await remove(token, `groves/${village.id}`);
    await remove(token, `players/${village.id}/private/wallet`);
    await remove(token, `players/${village.id}`);
    console.log(`removed ${village.id}`);
  }
  console.log(`\n${VILLAGES.length} showcase grove(s) taken down`);
  process.exit(0);
}

const now = Math.floor(Date.now() / 1000);
let lowest = Infinity;
const seen = new Set();

for (const village of VILLAGES) {
  for (const field of ["id", "name", "avatar", "home", "land", "legend", "plan"]) {
    if (village[field] === undefined) throw new Error(`a village is missing '${field}'`);
  }
  if (!village.id.startsWith("showcase-")) throw new Error(`${village.id}: ids must start with 'showcase-'`);
  if (seen.has(village.id) || seen.has(village.name)) throw new Error(`${village.id}: duplicate id or name`);
  seen.add(village.id); seen.add(village.name);

  const { placements, tiles } = compile(village);
  const play = ledger(village);
  const held = holdings(village, placements, play.level);
  const save = buildSave(village, play, held, placements, now);

  // Nothing may stand in a grove its keeper does not hold: bought by the copy, free to
  // everybody, earned by the ledger, or on the roster.
  const copies = new Map(held.stock.map((row) => [row.id, row.copies]));
  // The starter is held by everybody and bought by nobody (`CompanionLedger.IsHeld`).
  const roster = new Set(
    held.companions.concat(Object.keys(COMPANIONS).filter((id) => COMPANIONS[id].cost <= 0))
      .map((id) => `friend_${id}`)
  );
  const standing = new Map();
  for (const { slot, piece } of placements) {
    standing.set(piece, (standing.get(piece) ?? 0) + 1);
    const ok = FREE.has(piece) || roster.has(piece) || earnedBy(save, piece)
            || (copies.get(piece) ?? 0) >= standing.get(piece);
    if (!ok) throw new Error(`${village.id}: ${piece} stands on ${slot} and is not held`);
  }

  // Enough granted currency to cover what they built, with change. These are keepers who
  // bought coins; the clamp then never bites, which is what makes the card's score the
  // honest sum of what they hold rather than a number the ceiling chose.
  const worth = groveWorth(save, GROVE_CONFIG, play.level, Number.MAX_SAFE_INTEGER);
  const granted = Math.ceil((worth.bought + 25000) / 1000) * 1000;
  const card = buildCard(village.id, save, GROVE_CONFIG,
                         groveWorth(save, GROVE_CONFIG, play.level, granted),
                         play.level, now, village.name);
  card.synthetic = true;
  card.bio = village.bio ?? "";

  if (card.name !== sanitiseName(village.name)) {
    throw new Error(`${village.id}: '${village.name}' would publish as '${card.name}'`);
  }

  lowest = Math.min(lowest, card.score);

  console.log(
    `\n${village.name}  (${village.id})  ${village.bio ?? ""}\n` +
    `  ${card.score.toLocaleString()} worth · ${card.stars}★ ${leagueOf(card.stars)} · level ${play.level} · ` +
    `${placements.length} piece(s) on ${tiles} tile(s) (${Math.round(placements.length / tiles * 100)}%) · ` +
    `${held.land.length + 1} region(s) · ${held.stock.length} stock row(s) · ` +
    `${held.companions.length} companion(s) · home ${card.dwelling}`
  );

  if (DUMP) {
    mkdirSync(DUMP, { recursive: true });
    writeFileSync(join(DUMP, `${village.id}.json`), JSON.stringify({
      name: village.name, land: held.land, dwelling: card.dwelling, placements,
    }, null, 1));
  }

  if (DRY) { console.log(village.plan.map((line) => "  " + line.split("").join(" ")).join("\n")); continue; }

  const holding = await reserveName(token, village, now);
  await write(token, `players/${village.id}`, save);
  await write(token, `players/${village.id}/private/wallet`, {
    credits: { granted, spent: worth.bought, confirmedThroughUnix: now, earnedFloor: 0 },
    gems: { granted: held.gemsSpent + 400, spent: held.gemsSpent, confirmedThroughUnix: now, earnedFloor: 0 },
    name: holding,
  });
  await write(token, `groves/${village.id}`, card);
}

console.log(
  `\n${VILLAGES.length} showcase grove(s) ${DRY ? "previewed" : "written"}` +
  `, lowest worth ${lowest.toLocaleString()}`
);

if (!DRY) {
  console.log(
    "\nRun the ranking job to put them on the boards:\n" +
    "  gcloud scheduler jobs run firebase-schedule-publishGroveRanks-europe-west1 " +
    `--project ${PROJECT} --location europe-west1\n` +
    "\nTake them down again with:\n  node firebase/seed/seed-showcase.mjs --remove"
  );
}
