#!/usr/bin/env node
/**
 * Showcase groves — ten built villages, so the boards are not empty on launch day.
 *
 *     node firebase/seed/seed-showcase.mjs --dry-run     # print them, write nothing
 *     node firebase/seed/seed-showcase.mjs               # write them
 *     node firebase/seed/seed-showcase.mjs --remove      # take them all down again
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
 * ## Why they are built the way they are
 *
 * Each village is *composed* rather than sprinkled: a spine of path from the hall, a
 * fence following the rim of the owned land, landmarks at the heart of each region,
 * canopy massed at the back, flowers where a path passes, friends near the door. A
 * random scatter of two hundred pieces reads as noise from the first second — which is
 * precisely the failure the tile floor was built to avoid (invariant 16b), so a village
 * that looked accidental would be arguing against the feature it is advertising.
 *
 * The ten differ by *plan* as well as by palette, because five colour schemes over one
 * layout is one village painted five ways. Formal and axial, organic and winding, a ring
 * around a plaza, a spiral, a waterfront strip.
 *
 * ## What is written, and why the card is not written by hand
 *
 *   players/{uid}                  the save: ledger, wallet, grove sets, arrangement
 *   players/{uid}/private/wallet   granted currency — these are players who bought coins
 *   groves/{uid}                   the public card
 *
 * The card is built by `buildCard`/`groveWorth` out of the compiled functions, so it is
 * **exactly** what `publishGrove` would have written from the same save. Writing one by
 * hand would put a number on the board that the server's own derivation disagrees with,
 * which is the one thing a leaderboard cannot survive — and it would drift the first time
 * the scoring rule changed.
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

/**
 * The keeper level every showcase account stands at.
 *
 * It decides which companions count as *earned* rather than bought, which is the only
 * thing about these accounts the score treats differently from a real one. Kept modest on
 * purpose: a showcase grove should be a monument to what somebody bought and arranged,
 * not to a level nobody can reach yet.
 */
const KEEPER_LEVEL = 8;

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

const compiled = join(REPO, "firebase", "functions", "lib", "grove.js");
if (!existsSync(compiled)) {
  throw new Error("build the functions first: npm --prefix firebase/functions run build");
}
const { buildCard, groveWorth, leagueOf } = await import(pathToFileURL(compiled).href);

const homestead = readJson(join(CONTENT, "homestead.json"));
const manifest = readJson(join(CONTENT, "manifest.json"));

// ---------------------------------------------------------------- the catalog
const FLOOR = homestead.floor;
const COLS = FLOOR.cols;
const ROWS = FLOOR.rows;
const HALL = FLOOR.hallTile;

const tileId = (col, row) =>
  `t_${String(col).padStart(3, "0")}_${String(row).padStart(3, "0")}`;

const PRICE = {};
const SLOT = {};
const DWELLINGS = {};
for (const piece of homestead.pieces) {
  if ((piece.cost ?? 0) > 0) PRICE[piece.id] = piece.cost;
  SLOT[piece.id] = piece.kind === "dwelling" ? "dwelling" : (piece.slot ?? "ground");
  if (piece.kind === "dwelling") DWELLINGS[piece.id] = piece.tier ?? 0;
}

const REGION_PRICE = {};
const REGIONS = {};
for (const region of FLOOR.regions) {
  REGIONS[region.id] = region;
  if ((region.cost ?? 0) > 0) REGION_PRICE[region.id] = region.cost;
}

const COMPANIONS = {};
for (const c of manifest.companions ?? []) {
  if (c.disabled) continue;
  COMPANIONS[c.id] = { cost: c.unlockCost ?? 0, level: c.unlockLevel ?? 0 };
}

const GROVE_CONFIG = {
  version: manifest.groveVersion ?? 1,
  pieces: PRICE,
  regions: REGION_PRICE,
  companions: Object.fromEntries(
    Object.entries(COMPANIONS).filter(([, v]) => v.cost > 0)
  ),
  dwellings: DWELLINGS,
  stars: (homestead.score?.stars ?? []).slice().sort((a, b) => a - b),
};

const LEVEL_IDS = [];
for (const chapter of manifest.chapters ?? []) {
  const body = readJson(join(CONTENT, "chapters", `${chapter.id}.json`));
  for (const level of body.levels ?? []) LEVEL_IDS.push(level.id);
}

const ALL_REGIONS = Object.keys(REGIONS);

/**
 * Pieces held without being bought, and the two ways that happens.
 *
 * `FREE` is what nothing gates and nothing charges for — held by everybody. `REQUIRED` is
 * what play unlocks, keyed by what it asks for. Neither ever appears in `homesteadOwned`:
 * that set is a record of *purchases*, so listing an earned piece would be claiming a
 * purchase that never happened, and it is worth nothing so it would not move the score
 * either. It matters here because the villages decorate with both — `lantern` is free to
 * anybody who cleared Lantern Ring, and it is exactly the sort of thing somebody lines a
 * path with.
 */
const FREE = new Set();
const REQUIRED = new Map();

for (const piece of homestead.pieces) {
  if ((piece.cost ?? 0) > 0 || piece.kind === "dwelling") continue;

  if (piece.requiresLevel) REQUIRED.set(piece.id, { level: piece.requiresLevel });
  else if (piece.requiresChapter) REQUIRED.set(piece.id, { chapter: piece.requiresChapter });
  else FREE.add(piece.id);
}

/** levelId -> chapterId, for the chapter half of a requirement. */
const CHAPTER_OF = {};
for (const chapter of manifest.chapters ?? []) {
  const body = readJson(join(CONTENT, "chapters", `${chapter.id}.json`));
  for (const level of body.levels ?? []) CHAPTER_OF[level.id] = chapter.id;
}

/** Whether a save's ledger has earned a gated piece. */
function earnedBy(save, id) {
  const rule = REQUIRED.get(id);
  if (!rule) return false;

  const cleared = (levelId) => (save.levels[levelId]?.stars ?? 0) > 0;

  if (rule.level) return cleared(rule.level);

  return Object.keys(CHAPTER_OF)
    .filter((levelId) => CHAPTER_OF[levelId] === rule.chapter)
    .every(cleared);
}

/** The companion roster in the order it unlocks, which is the order it is bought in. */
const LADDER = Object.entries(COMPANIONS)
  .sort((a, b) => a[1].level - b[1].level)
  .map(([id]) => id);

// ------------------------------------------------------------------ a die
/**
 * A seeded generator, so a village is a function of its name.
 *
 * Deterministic on purpose: re-running this script has to rebuild the *same* ten villages
 * rather than ten new ones, or every run would reshuffle groves people had already
 * visited. It is xorshift32 for the reason `ChestRandom` is — one line, no dependency,
 * and identical on every machine.
 */
function die(seed) {
  let state = 2166136261 >>> 0;
  for (const ch of seed) {
    state = Math.imul(state ^ ch.charCodeAt(0), 16777619) >>> 0;
  }
  if (state === 0) state = 2166136261;

  return {
    next() {
      state ^= state << 13; state >>>= 0;
      state ^= state >>> 17;
      state ^= state << 5;  state >>>= 0;
      return state / 4294967296;
    },
    chance(p) { return this.next() < p; },
    pick(list) { return list[Math.floor(this.next() * list.length)]; },
    among(list, n) {
      const copy = list.slice();
      const out = [];
      while (out.length < n && copy.length) {
        out.push(copy.splice(Math.floor(this.next() * copy.length), 1)[0]);
      }
      return out;
    },
  };
}

// -------------------------------------------------------------- the composer
/**
 * A village is a set of *districts*, not a field of dice.
 *
 * ## What was wrong with rolling for every tile
 *
 * The first version walked the whole floor and gave each tile a chance of a tree, then a
 * chance of a flower, then a chance of a rock — and the result was a uniform texture with
 * something on roughly four tiles in five. Uniform texture is the one thing that cannot
 * read as designed, because *design is where the variation is*: a wood only looks like a
 * wood next to a clearing, and a lawn only exists if something stops. Three things follow
 * from that, and they are the whole of this rewrite.
 *
 * **Massing, not sprinkling.** Rendered side by side, twenty trees in a solid block read
 * as a wood, twenty on alternating tiles read as a planted orchard, and twenty scattered
 * read as nothing at all — lonely dots on a lawn. The old composer only ever produced the
 * third. So canopy is laid as blocks and grids, never per tile.
 *
 * **Emptiness is a material.** The floor tile is a good-looking piece of art and the eye
 * needs somewhere to rest; the target here is a little over a third of the tiles occupied,
 * where it used to be four fifths. The clearing around the hall is authored first and
 * defended from every later pass, because a hall with things pressed against its walls is
 * a hall nobody can see.
 *
 * **Depth is a constraint, not an afterthought.** The field draws back to front by
 * `col + row` (`GroveFloor.DrawOrder`), so anything tall standing in front of the hall
 * covers it — and `oak` is 539px against a 124px tile step. Tall pieces are therefore
 * confined to the back of the floor, which is also simply how a landscape works: canopy
 * behind, lawn in front.
 *
 * ## Two facts about the art that decide the vocabulary
 *
 * **There is no paving tile.** Every piece on the `path` shelf is an *object* — bridges
 * and dirt curves 184 to 526 pixels wide against a 220 pixel tile. Laid along a run they
 * overlap into a smear, which is what the brown mess in the old villages was. Only
 * `stepping_stones` (94x47) and `plank_bridge` (a boardwalk that genuinely tiles) survive
 * being repeated, so those two are the only paving here.
 *
 * **A fence sprite faces one diagonal.** Run along +col a picket fence joins into a
 * continuous line; run along +row the same sprite reads as a row of loose posts, flipped
 * or not. So fences run along +col only, and never around the whole property — the floor
 * already draws its own edge wall, so a rim fence is noise on top of a boundary that is
 * already there. A fence here encloses a garden, which is what fences are for.
 */

// A tile's depth on screen: bigger is nearer the viewer. GroveFloor.DrawOrder without the
// tie-break, which is all a "is this in front of that" test needs.
const depth = (col, row) => col + row;

function compose(persona, owned, held) {
  const rng = die(persona.id);
  const T = persona.theme;

  const [hallCol, hallRow] = [Number(HALL.slice(2, 5)), Number(HALL.slice(6, 9))];
  const hallDepth = depth(hallCol, hallRow);

  const ownedTiles = new Set();
  for (const id of owned.land) {
    const r = REGIONS[id];
    if (!r) continue;
    for (let c = r.col; c < r.col + r.cols; c++) {
      for (let w = r.row; w < r.row + r.rows; w++) ownedTiles.add(`${c},${w}`);
    }
  }

  const placed = new Map();
  const kept = new Set();          // tiles authored to stay empty, defended from later passes

  const has = (c, w) => ownedTiles.has(`${c},${w}`);
  const free = (c, w) => has(c, w) && tileId(c, w) !== HALL
                      && !placed.has(`${c},${w}`) && !kept.has(`${c},${w}`);

  const keep = (c, w) => { if (has(c, w)) kept.add(`${c},${w}`); };
  const release = (c, w) => kept.delete(`${c},${w}`);
  const put = (c, w, piece, flip = false) => {
    if (piece && free(c, w)) placed.set(`${c},${w}`, { piece, flip });
  };

  /**
   * Whether something this tall may stand here.
   *
   * A piece taller than about two tile steps hides whatever is behind it, so it is allowed
   * only behind the hall. Without this the finale of every village was an oak planted in
   * the front garden with the house behind it.
   */
  const roomAbove = (c, w) => depth(c, w) < hallDepth - 1;

  const tree = (c, w) => {
    if (!free(c, w)) return;
    const list = roomAbove(c, w) && T.big.length && rng.chance(.45) ? T.big : T.tree;
    // Mirrored on alternate tiles so a block of one species does not read as printed.
    put(c, w, rng.pick(list), (c + w) % 2 === 0 && rng.chance(.5));
  };

  // ------------------------------------------------------------------ the path
  /**
   * Laid before anything else, and its shoulders reserved.
   *
   * <b>Order is the whole of why this works.</b> Built last, a path is a line of stones
   * threaded between things already standing, and it disappears — which is exactly what the
   * first pass of this rewrite produced. Built first, with the tile either side of it kept
   * empty, it is a corridor the districts have to grow around, and a corridor is what turns
   * a field of objects into somewhere you could walk.
   *
   * The paving is `stepping_stones` for every keeper. It is the only piece on the shelf that
   * survives repetition: the rest of that shelf is bridges and dirt patches 184 to 526 pixels
   * wide against a 220 pixel tile, and a run of them overlaps into a smear. `plank_bridge`
   * genuinely tiles, but it is *drawn as a raised bridge with posts* — two runs of it in one
   * village read as a wooden lattice rather than as walkways, so it is kept for the one place
   * it is telling the truth, which is a quay over water.
   */
  function layPath() {
    const ARMS = { south: [0, 1], east: [1, 0], north: [0, -1], west: [-1, 0] };
    const walked = [];

    for (const name of persona.gate) {
      const [dc, dw] = ARMS[name];
      for (let i = 2; i < 14; i++) {
        const c = hallCol + dc * i, w = hallRow + dw * i;
        if (!has(c, w)) break;
        release(c, w);
        placed.set(`${c},${w}`, { piece: T.path, flip: false });
        walked.push([c, w]);
      }
    }

    // A stone path is a quiet thing, so it only reads if nothing is pressed against it.
    for (const [c, w] of walked) {
      for (const [dc, dw] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) keep(c + dc, w + dw);
    }

    return walked;
  }

  // ------------------------------------------------------------------ districts
  /**
   * Each fill takes a region and does one thing to the whole of it, because a district is
   * the unit a visitor actually reads — "the wood", "the orchard", "the water" — and a
   * region that did two things would read as neither.
   */
  let deckLaid = false;

  /**
   * A fence along one row, drawn only where it can make a *run*.
   *
   * Fragments are the whole reason this is a function. A fence is laid along a district's
   * edge, but the path was laid first and reserves the tiles either side of it, so a run
   * crossing the approach comes back as two lengths and a couple of orphans — and one
   * fence panel on its own does not read as "a fence with a gap in it", it reads as debris,
   * which is exactly what the villages this replaces were covered in. So spans shorter
   * than three are simply not drawn, and a long one is given a gate.
   */
  function fenceRun(row, from, to) {
    let span = [];

    const flush = () => {
      if (span.length >= 3) {
        const gate = span.length >= 6
          ? span[1 + Math.floor(rng.next() * (span.length - 2))]
          : -1;
        for (const c of span) {
          if (c === gate) { keep(c, row); continue; }
          put(c, row, c === span[span.length - 1] ? T.fenceEnd : T.fence);
        }
      }
      span = [];
    };

    for (let c = from; c <= to; c++) {
      if (free(c, row)) span.push(c); else flush();
    }
    flush();
  }

  const DISTRICTS = {
    /** A solid mass of canopy with a ragged edge and one clearing bitten out of it. */
    wood(r) {
      const cx = r.col + (r.cols - 1) / 2, cy = r.row + (r.rows - 1) / 2;
      const reach = Math.max(r.cols, r.rows) / 2 + .35;

      // A clearing, so the wood has an inside. Placed off centre or it reads as a donut.
      const gx = r.col + 1 + Math.floor(rng.next() * Math.max(1, r.cols - 2));
      const gy = r.row + 1 + Math.floor(rng.next() * Math.max(1, r.rows - 2));
      for (const [dc, dw] of [[0, 0], [1, 0], [0, 1], [1, 1]]) keep(gx + dc, gy + dw);

      for (let c = r.col; c < r.col + r.cols; c++) {
        for (let w = r.row; w < r.row + r.rows; w++) {
          const d = Math.hypot(c - cx, w - cy) / reach;      // 0 at the heart, 1 at the rim
          if (rng.chance(.98 - d * .78)) tree(c, w);
        }
      }

      // Undergrowth at the clearing's mouth, which is what stops a clearing looking mown.
      for (const [dc, dw] of [[-1, 0], [0, -1], [2, 1], [1, 2]]) {
        if (rng.chance(.5)) put(gx + dc, gy + dw, rng.pick(T.hedge));
      }
    },

    /** Planted rows: one species on alternate tiles, with an aisle down the middle. */
    orchard(r) {
      const species = T.orchard;
      const aisle = r.row + (r.rows >> 1);

      for (let c = r.col; c < r.col + r.cols; c++) {
        for (let w = r.row; w < r.row + r.rows; w++) {
          if (w === aisle) { keep(c, w); continue; }
          if ((c + w) % 2 !== 0) continue;
          put(c, w, species, (c + w) % 4 === 0);
        }
      }

      // A basket or two by the aisle. Two, not twelve — this is punctuation.
      for (let i = 0; i < 2; i++) {
        const c = r.col + Math.floor(rng.next() * r.cols);
        put(c, aisle, rng.pick(T.yard));
      }
    },

    /**
     * Beds in a solid block: hedged along the back, two dense rows of one flower, fenced in
     * front.
     *
     * A *block* rather than stripes across the whole region. One flower every other tile
     * over four rows is a lawn somebody dropped flowers on, and the first pass of this
     * proved it — the bed only exists because it has an edge on both sides.
     */
    garden(r) {
      const top = r.row + Math.max(1, (r.rows >> 1) - 1);
      const flower = rng.pick(T.flower);
      const second = rng.pick(T.flower);

      for (let c = r.col; c < r.col + r.cols; c++) {
        put(c, top, flower);
        put(c, top + 1, rng.chance(.72) ? flower : second);
      }

      // Hedged along the back, so the bed has a wall to sit against.
      for (let c = r.col; c < r.col + r.cols; c++) {
        if (rng.chance(.72)) put(c, top - 1, rng.pick(T.hedge));
      }

      fenceRun(top + 2, r.col, r.col + r.cols - 1);
    },

    /**
     * Open ground: a landmark with its own gathering, a stand of trees at one corner, a
     * hedgerow along one edge. Nearly empty, but never *nothing* — an empty rectangle with
     * bare edges reads as land nobody has got to yet rather than as a lawn.
     */
    meadow(r) {
      const c = r.col + 1 + Math.floor(rng.next() * Math.max(1, r.cols - 2));
      const w = r.row + 1 + Math.floor(rng.next() * Math.max(1, r.rows - 2));

      put(c, w, rng.pick(T.mark));
      for (const [dc, dw] of [[1, 0], [-1, 0], [0, 1], [0, -1], [1, 1], [-1, -1]]) {
        if (rng.chance(.5)) put(c + dc, w + dw, rng.pick(T.low));
      }

      // A stand of four at the back corner, so the emptiness has an edge to be measured
      // against.
      const bc = r.col + (rng.chance(.5) ? 0 : r.cols - 2);
      for (const [dc, dw] of [[0, 0], [1, 0], [0, 1], [1, 1], [-1, 0], [2, 1]]) {
        if (rng.chance(.85)) tree(bc + dc, r.row + dw);
      }

      // A hedgerow down one flank, which is what a field boundary looks like when it is not
      // a fence — and it is the piece that stops the region reading as a bare rectangle.
      const flank = rng.chance(.5) ? r.col : r.col + r.cols - 1;
      for (let y = r.row + 1; y < r.row + r.rows; y++) {
        if (rng.chance(.62)) put(flank, y, rng.pick(T.hedge));
      }

      // A second gathering, so an open district has two things in it rather than one — a
      // rectangle with a single object in the middle reads as a placeholder.
      const c2 = r.col + Math.floor(rng.next() * r.cols);
      const w2 = r.row + r.rows - 2;
      for (const [dc, dw] of [[0, 0], [1, 0], [0, 1], [1, 1], [2, 0]]) {
        if (rng.chance(.62)) put(c2 + dc, w2 + dw, rng.pick(T.hedge.concat(T.low)));
      }

      for (let i = 0; i < 4; i++) {
        put(r.col + Math.floor(rng.next() * r.cols),
            r.row + Math.floor(rng.next() * r.rows), rng.pick(T.dust));
      }
    },

    /** Reeds massed at the far edge, one quay across them, posts along it. */
    water(r) {
      for (let c = r.col; c < r.col + r.cols; c++) {
        for (let w = r.row; w < r.row + r.rows; w++) {
          // Massed towards the region's far edge and thinning inward, the way a bank does.
          const t = (c - r.col) / Math.max(1, r.cols - 1);
          if (rng.chance(.92 - t * .60)) put(c, w, rng.pick(T.reed));
        }
      }

      // At most one quay in a village. `plank_bridge` is a raised bridge with posts, so a
      // second run of it stops reading as a walkway and starts reading as fencing.
      if (!deckLaid) {
        deckLaid = true;
        const walk = r.row + (r.rows >> 1);
        for (let c = r.col; c < r.col + r.cols; c++) {
          if (kept.has(`${c},${walk}`)) continue;
          placed.delete(`${c},${walk}`);
          put(c, walk, T.deck);
        }
        for (let c = r.col + 1; c < r.col + r.cols; c += 3) {
          placed.delete(`${c},${walk - 1}`);
          put(c, walk - 1, T.post);
        }
      }

      put(r.col + r.cols - 2, r.row + r.rows - 1, rng.pick(T.mark));
    },

    /** A fallen wall, stones along its line, one monument standing. */
    ruin(r) {
      const w = r.row + (r.rows >> 1);
      for (let c = r.col; c < r.col + r.cols; c++) {
        if (rng.chance(.78)) put(c, w, rng.pick(T.rubble));
      }
      for (let c = r.col; c < r.col + r.cols; c++) {
        if (rng.chance(.42)) put(c, w + 1, rng.pick(T.low));
      }

      put(r.col + 1, r.row, rng.pick(T.mark));
      for (let c = r.col; c < r.col + r.cols; c++) {
        if (rng.chance(.5)) tree(c, r.row + r.rows - 1);
      }
    },

    /** Things gathered round an open middle, which is what a camp is. */
    camp(r) {
      const cx = r.col + (r.cols >> 1), cy = r.row + (r.rows >> 1);
      for (const [dc, dw] of [[0, 0], [1, 0], [0, 1], [1, 1]]) keep(cx + dc - 1, cy + dw - 1);

      put(cx - 1, cy - 2, rng.pick(T.mark));
      const ring = [[-2, -1], [-2, 1], [1, -2], [2, 0], [1, 2], [-1, 2], [2, -1], [0, -2],
                    [-2, 0], [2, 1], [0, 3], [3, 0]];
      for (const [dc, dw] of ring) {
        if (rng.chance(.78)) put(cx + dc, cy + dw, rng.pick(T.yard.concat(T.low)));
      }
      put(cx - 2, cy - 2, T.lamp);

      // Trees at the back edge, or a camp sits in a void.
      for (let c = r.col; c < r.col + r.cols; c++) if (rng.chance(.45)) tree(c, r.row);
    },

    /**
     * A built square: paving, a well in the middle of it, lamps at the corners, benches and
     * crates round the edge, a fence along the front.
     *
     * <b>This is the district that says a person made the place.</b> Every other role here
     * is a kind of landscape, and ten villages of woods, orchards and meadows read as ten
     * pieces of countryside that happen to have a house on them. What a grove is advertising
     * is that somebody *built* something, so at least one district in each village is
     * masonry rather than planting — and paving laid as a square reads as deliberate in a
     * way no amount of scattered decor does.
     */
    commons(r) {
      const cx = r.col + (r.cols >> 1) - 1, cy = r.row + (r.rows >> 1) - 1;

      // <b>No paving, and that is a finding rather than a preference.</b> The obvious build
      // is a paved square, and there is nothing to pave it with: `stepping_stones` is 94
      // pixels on a 220 pixel tile, so a block of them comes out as scattered stones with
      // grass between — it reads as a path only because a *line* of gaps still reads as a
      // line. What makes a yard instead is the enclosure and what stands in it.
      for (const [dc, dw] of [[0, 0], [1, 0], [2, 0], [0, 1], [1, 1], [2, 1]]) {
        keep(cx + dc, cy + dw);
      }

      placed.delete(`${cx + 1},${cy}`);
      release(cx + 1, cy);
      put(cx + 1, cy, rng.pick(T.mark));
      keep(cx + 1, cy);

      // Working things round the open middle, which is what a yard is.
      for (const [dc, dw] of [[-1, 0], [3, 0], [-1, 2], [1, 2], [3, 1], [0, -1], [2, -1],
                              [-1, -1], [3, -1], [0, 2], [2, 2], [-1, 1]]) {
        if (rng.chance(.74)) put(cx + dc, cy + dw, rng.pick(T.yard.concat(T.low)));
      }

      // Hedged along one flank so the yard has a back to it.
      for (let y = r.row; y < r.row + r.rows - 1; y++) {
        if (rng.chance(.6)) put(r.col, y, rng.pick(T.hedge));
      }

      put(cx - 1, cy - 1, T.lamp);
      put(cx + 3, cy + 2, T.lamp);

      fenceRun(r.row + r.rows - 1, r.col, r.col + r.cols - 1);

      for (let c = r.col; c < r.col + r.cols; c++) if (rng.chance(.5)) tree(c, r.row);
    },
  };

  // ---------------------------------------------------------------------- yard
  /**
   * The hall's own ground, authored rather than rolled — it is the one part of the picture
   * every visitor looks at, and it is where the composition either has a subject or does not.
   *
   * Two rules carry it. The clearing is reserved *first* and never given back, because a
   * hall with things pressed against its walls is a hall nobody can see. And the door's own
   * axis stays empty for two tiles: the first pass stood a resident dead in front of the
   * door, and one creature on the centre line is enough to make the house look like its
   * backdrop.
   */
  function yardDetails(walked) {
    // Lamps down one side of the path at an even rhythm. A lantern every third step reads as
    // lighting; one wherever the dice said reads as litter.
    // A lantern is 280 pixels tall against a 124 pixel tile step, so it is one of the
    // loudest things a village can hold — seven of them dotted about read as lamp-post spam
    // and drown the house. Two down the approach is lighting; the rest is noise.
    let lit = 0;
    walked.forEach(([c, w], i) => {
      if (i % 4 !== 1 || lit >= 2) return;
      const [sc, sw] = c === hallCol ? [1, 0] : [0, 1];
      release(c + sc, w + sw);
      if (!free(c + sc, w + sw)) return;
      put(c + sc, w + sw, T.lamp);
      lit++;
    });

    // A matching pair either side of the door. Symmetry is the cheapest way to say that a
    // building matters, and it is the only place in a village where symmetry belongs.
    // Deliberately the oversized piece: a mass either side of the door is what gives the
    // house a front, and it is the one place in a village where 369 pixels of shrub is the
    // right answer rather than a thing that swallows its neighbours.
    const flank = T.flank ?? rng.pick(T.hedge);
    for (const [dc, dw] of [[-1, 1], [1, -1]]) {
      release(hallCol + dc, hallRow + dw);
      placed.delete(`${hallCol + dc},${hallRow + dw}`);
      put(hallCol + dc, hallRow + dw, flank, dc < 0);
    }

    // The yard's working things, against the hall's back and side, where a house's clutter
    // actually collects — and behind it, so nothing stands in front of the door.
    for (const [dc, dw] of [[-1, -2], [0, -2], [-2, 0], [-2, 1], [2, -1], [-2, -1]]) {
      if (!rng.chance(.6)) continue;
      release(hallCol + dc, hallRow + dw);
      put(hallCol + dc, hallRow + dw, rng.pick(T.yard));
    }

    // Friends on the lawn *beside* the approach, never on the door's axis. Only the
    // companions drawn as in-world critters stand here — see `CRITTERS` for why the rest
    // may not.
    // Rotated per keeper, so ten villages do not all have the same friend standing in the
    // same corner. On a board where every row is one tap from every other, that repetition
    // is the tell that the ten were made by one hand.
    const roster = held.companions.filter((id) => CRITTERS.has(id));
    const turn = persona.id.charCodeAt(persona.id.length - 1) % Math.max(1, roster.length);
    const residents = roster.slice(turn).concat(roster.slice(0, turn))
      .map((id) => `friend_${id}`);

    const perches = rng.chance(.5)
      ? [[-1, 2], [2, -1], [-2, 2], [2, 1], [1, 3]]
      : [[2, -1], [-1, 2], [2, 2], [-2, 1], [3, 0]];
    let standing = 0;
    for (const [dc, dw] of perches) {
      if (standing >= Math.min(persona.friends, residents.length)) break;
      const c = hallCol + dc, w = hallRow + dw;
      release(c, w);
      if (!free(c, w)) continue;
      put(c, w, residents[standing], dc > 0);
      standing++;
    }
  }

  // --------------------------------------------------------------------- build
  // The clearing round the hall, then the path, then the districts around both, then the
  // detail that belongs to the house. Nothing here is a per-tile roll over the whole floor.
  for (let dc = -1; dc <= 2; dc++) {
    for (let dw = -1; dw <= 2; dw++) keep(hallCol + dc, hallRow + dw);
  }

  const walked = layPath();

  for (const [id, role] of Object.entries(persona.districts)) {
    const r = REGIONS[id];
    if (!r || !owned.land.includes(id) || id === "hearthstead") continue;
    (DISTRICTS[role] ?? DISTRICTS.meadow)(r);
  }

  /**
   * Nothing is left bare, and the filler is clustered and low.
   *
   * The roles that read as open ground — meadow, water, camp — can come back with four
   * things in a twenty-four tile rectangle, and a region that empty does not read as a lawn,
   * it reads as land the player has bought and not got to yet. Which is the opposite of what
   * a showcase is for. So a district under a quarter full is topped up, and the two rules
   * that keep the top-up from undoing the rest of this are that it places in *clumps* rather
   * than one tile at a time (a scatter is what the whole rewrite exists to avoid) and that it
   * only ever uses low pieces, so it can never break the depth rule near the front.
   */
  for (const id of owned.land) {
    const r = REGIONS[id];
    if (!r || id === "hearthstead") continue;

    let held = 0, room = 0;
    for (let c = r.col; c < r.col + r.cols; c++) {
      for (let w = r.row; w < r.row + r.rows; w++) {
        if (placed.has(`${c},${w}`)) held++;
        room++;
      }
    }
    if (held >= room * .40) continue;

    const clumps = Math.ceil((room * .50 - held) / 4);
    for (let i = 0; i < clumps; i++) {
      const c = r.col + Math.floor(rng.next() * r.cols);
      const w = r.row + Math.floor(rng.next() * r.rows);

      // A clump of canopy where there is room above, and of low cover where there is not.
      // Behind the hall a stand of trees is the strongest thing available for filling
      // ground; in front of it, it would be the strongest thing for hiding the house.
      const wood = roomAbove(c, w) && rng.chance(.5);
      const of = rng.chance(.5) ? T.hedge : T.low;

      for (const [dc, dw] of [[0, 0], [1, 0], [0, 1], [1, 1], [2, 0], [1, 2]]) {
        if (!rng.chance(.72)) continue;
        if (wood) tree(c + dc, w + dw); else put(c + dc, w + dw, rng.pick(of));
      }
    }
  }

  yardDetails(walked);

  return [...placed].map(([key, { piece, flip }]) => {
    const [c, w] = key.split(",").map(Number);
    return { slot: tileId(c, w), piece, flipped: flip };
  });
}

// ------------------------------------------------------------------ palettes
/**
 * A palette is by *role in a picture*, not by the shop shelf a piece sells on.
 *
 * `big` is anything over about two tile steps tall, which may only stand behind the hall;
 * `tree` is ordinary canopy; `hedge` and `flower` are the two things beds are for — massing
 * and colour; `low` is ground detail that never interrupts a silhouette; `yard` is the
 * working clutter a house collects. `fence`/`fenceEnd` are the one fence that holds a line
 * along +col, `path` is one of the two pieces that survive being repeated, and `mark` is
 * what a district is built around.
 */
const WOODLAND = {
  orchard: "tree_apple",
  big: ["oak", "tree_slim", "pine_bent", "pine_round"],
  tree: ["oak_broad", "tree_leafy", "tree_round", "tree_apple", "pine", "pine_tall"],
  hedge: ["shade_bush", "berry_bush", "shrub_green"],
  flower: ["blossom", "sprout", "berry_bush", "daisies"],
  flank: "bush",
  low: ["mossy_rock", "forest_rock", "stone_stumps", "cairn_stone", "little_stump"],
  dust: ["grass_tuft", "stone", "field_stone", "pebble"],
  yard: ["log", "crate", "hay_bale", "stump"],
  rubble: ["field_stone", "mossy_rock", "stone_pair"],
  reed: ["grass_blades", "sprout", "shrub_green"],
  mark: ["well", "great_stump", "mushroom_log", "chest"],
  fence: "fence_picket", fenceEnd: "fence_picket_end",
  lamp: "lantern", path: "stepping_stones", deck: "plank_bridge", post: "wood_post",
};

const MARSH = {
  orchard: "willow",
  big: ["willow_old", "pine_bent", "vines"],
  tree: ["willow", "tree_leafy", "tree_round", "tree_slim"],
  hedge: ["shade_bush", "grass_blades", "shrub_green"],
  flower: ["bamboo", "teal_plant", "sprout", "sea_grass"],
  flank: "bush",
  low: ["shore_rock", "mossy_rock", "log", "stone_stumps", "cairn_stone"],
  dust: ["gravel", "stone", "little_stump", "pebble"],
  yard: ["crate", "wood_post", "log", "fish_trap"],
  rubble: ["shore_rock", "stone_pair", "gravel"],
  reed: ["sea_grass", "lily_pads", "bamboo", "grass_blades"],
  mark: ["well", "fish_trap", "great_stump", "chest"],
  fence: "fence_timber", fenceEnd: "fence_timber_end",
  lamp: "lantern", path: "stepping_stones", deck: "plank_bridge", post: "mooring_post",
};

const ESTATE = {
  orchard: "tree_apple",
  big: ["tree_gold", "pine_tall", "spire"],
  tree: ["tree_apple", "oak_broad", "tree_round", "tree_leafy"],
  hedge: ["shade_bush", "shrub_green", "berry_bush"],
  flower: ["blossom", "sprout", "daisies"],
  flank: "bush",
  low: ["pedestal", "cairn_stone", "broken_pillars", "stone_stumps", "crate"],
  dust: ["flat_stone", "field_stone", "stone", "stone_pair"],
  yard: ["crate", "pedestal", "cairn_stone"],
  rubble: ["flat_stone", "broken_pillars", "stone_pair"],
  reed: ["shrub_green", "grass_blades", "sprout"],
  mark: ["obelisk", "well", "stone_pillars", "chest"],
  fence: "fence_picket", fenceEnd: "fence_picket_end",
  lamp: "torch_tall", path: "stepping_stones", deck: "plank_bridge", post: "wood_post",
};

const FROST = {
  orchard: "pine_round",
  big: ["pine_frost", "pine_leaning", "pine_snow_tall"],
  tree: ["pine_snow", "pine_round", "pine_bare", "pine"],
  hedge: ["dry_shrub", "shrub_green", "grass_blades"],
  flower: ["sprout", "daisies", "dry_shrub"],
  flank: "bush",
  low: ["snow_rock", "snow_boulder", "cairn_snow", "icicles", "ice_stump"],
  dust: ["flat_stone", "stone", "gravel"],
  yard: ["crate", "log", "wood_post"],
  rubble: ["snow_rock", "ice_stump", "flat_stone"],
  reed: ["dry_tuft", "grass_blades", "dry_shrub"],
  mark: ["well", "stone_pillars", "chest", "cairn_snow"],
  fence: "fence_timber", fenceEnd: "fence_timber_end",
  lamp: "torch", path: "stepping_stones", deck: "plank_bridge", post: "wood_post",
};

const DUNE = {
  orchard: "palm_pair",
  big: ["palm_curved", "desert_snag", "dead_tree_bare"],
  tree: ["palm", "palm_pair", "dead_trunk"],
  hedge: ["dry_shrub", "bamboo", "grass_blades"],
  flower: ["bamboo", "sea_grass", "sprout"],
  flank: "bush",
  low: ["sand_block", "stone_teeth", "sand_rock", "cairn_stone", "crate"],
  dust: ["gravel", "small_stone", "skull"],
  yard: ["crate", "wood_post", "sand_rock"],
  rubble: ["sand_rock", "stone_teeth", "gravel"],
  reed: ["dry_tuft", "sea_grass", "dry_shrub"],
  mark: ["tent", "sand_pillar", "obelisk", "chest"],
  fence: "fence_timber", fenceEnd: "fence_timber_end",
  lamp: "torch", path: "stepping_stones", deck: "plank_bridge", post: "wood_post",
};

const MILL = {
  orchard: "oak_broad",
  big: ["oak", "pine_bent", "tree_slim"],
  tree: ["oak_broad", "tree_leafy", "tree_round", "willow", "pine"],
  hedge: ["berry_bush", "shade_bush", "shrub_green"],
  flower: ["sprout", "berry_bush", "daisies"],
  flank: "bush",
  low: ["hay_bale", "hay_stack", "crate", "mossy_rock", "stone_stumps"],
  dust: ["flat_stone", "gravel", "wood_post", "little_stump"],
  yard: ["hay_bale", "crate", "log", "hay_stack"],
  rubble: ["flat_stone", "field_stone", "gravel"],
  reed: ["grass_blades", "sea_grass", "sprout"],
  mark: ["well", "great_stump", "mushroom_log", "fish_trap"],
  fence: "fence_timber", fenceEnd: "fence_timber_end",
  lamp: "lantern", path: "stepping_stones", deck: "plank_bridge", post: "wood_post",
};

const EMBER = {
  orchard: "tree_amber",
  big: ["pine_crimson", "tree_gold", "pine_bent"],
  tree: ["tree_amber", "tree_orange", "tree_round", "tree_apple"],
  hedge: ["berry_bush", "shade_bush", "shrub_green"],
  flower: ["pumpkins", "bud_amber", "berry_bush"],
  flank: "bush",
  low: ["hay_bale", "hay_stack", "crate", "log", "stone_stumps"],
  dust: ["little_stump", "field_stone", "stone", "flat_stone"],
  yard: ["hay_stack", "hay_bale", "crate", "log"],
  rubble: ["field_stone", "flat_stone", "stone_pair"],
  reed: ["dry_tuft", "grass_blades", "dry_shrub"],
  mark: ["great_stump", "well", "mushroom_log", "chest"],
  fence: "fence_picket", fenceEnd: "fence_picket_end",
  lamp: "torch", path: "stepping_stones", deck: "plank_bridge", post: "wood_post",
};

const RUIN = {
  orchard: "tree_round",
  big: ["dead_tree_bare", "pine_bare", "dead_tree"],
  tree: ["dead_trunk", "vines", "stump", "tree_round"],
  hedge: ["shade_bush", "dry_shrub", "shrub_green"],
  flower: ["dusk_plant", "sprout", "dry_shrub"],
  flank: "brambles",
  low: ["broken_pillars", "ruin_block", "rune_stone", "cairn_stone", "stone_stumps"],
  dust: ["skull", "rock_shard", "flat_stone"],
  yard: ["crate", "log", "cairn_stone"],
  rubble: ["broken_pillars", "ruin_block", "rune_stone", "stone_stumps"],
  reed: ["dry_tuft", "brambles", "dry_shrub"],
  mark: ["gravestone", "obelisk", "idol", "cave"],
  fence: "fence_timber", fenceEnd: "fence_timber_end",
  lamp: "candle", path: "stepping_stones", deck: "plank_bridge", post: "wood_post",
};

const NIGHTBLOOM = {
  orchard: "tree_night",
  big: ["pine_night", "willow_old", "spire"],
  tree: ["tree_night", "tree_slim", "vines", "tree_round"],
  hedge: ["shade_bush", "dusk_plant", "shrub_green"],
  flower: ["blossom", "dusk_plant", "teal_plant"],
  flank: "bush",
  low: ["crystal_shards", "rune_stone", "mossy_rock", "cairn_stone", "stone_stumps"],
  dust: ["stone", "rock_shard", "pebble"],
  yard: ["crate", "cairn_stone", "log"],
  rubble: ["rune_stone", "crystal_shards", "mossy_rock"],
  reed: ["teal_plant", "sprout_teal", "grass_blades"],
  mark: ["crystal", "idol", "obelisk", "stone_pillars"],
  fence: "fence_timber", fenceEnd: "fence_timber_end",
  lamp: "candle", path: "stepping_stones", deck: "plank_bridge", post: "wood_post",
};

const HARBOUR = {
  orchard: "palm",
  big: ["palm_curved", "willow_old", "dead_tree_bare"],
  tree: ["palm", "palm_pair", "willow", "tree_slim"],
  hedge: ["shade_bush", "bamboo", "shrub_green"],
  flower: ["bamboo", "sea_grass", "teal_plant"],
  flank: "bush",
  low: ["shore_rock", "crate", "hay_bale", "mossy_rock", "stone_stumps"],
  dust: ["gravel", "small_stone", "stone", "wood_post"],
  yard: ["crate", "wood_post", "fish_trap", "log"],
  rubble: ["shore_rock", "gravel", "stone_pair"],
  reed: ["sea_grass", "lily_pads", "grass_blades", "bamboo"],
  mark: ["well", "fish_trap", "chest", "great_stump"],
  fence: "fence_timber", fenceEnd: "fence_timber_end",
  lamp: "lantern", path: "stepping_stones", deck: "pier", post: "mooring_post",
};

/**
 * The five companions drawn as in-world critters, and the reason the other twenty-six may
 * not stand in a showcase village.
 *
 * A companion's grove art is its *portrait* unless it has a board flipbook
 * (`GroveResidents.From`), and a portrait is UI art: measured off the shipped files they
 * draw between 200 and 436 pixels wide against a 220 pixel tile and a 459 pixel cottage,
 * facing the camera with no ground contact. Standing one in a village puts a head the size
 * of the house on the lawn — which is most of what made the first ten look wrong, and it is
 * plainly visible in the render. The five with flipbooks draw at 133 to 192 and are the
 * creatures the board itself uses, so they read as livestock in a field.
 *
 * **This is a workaround for something real, not a preference.** A player who buys a
 * portrait-only companion and stands them in their own grove gets the same giant head, and
 * no validator would ever mention it. The fix is a per-companion draw scale in the roster
 * rather than `GroveResidents.Scale` being one constant for art cut two different ways —
 * out of scope here, and worth doing before the roster grows again.
 */
const CRITTERS = new Set(["monarch", "timber", "sprocket", "thistle", "puff"]);

// ------------------------------------------------------------------ the ten
/**
 * Ten keepers, each a *place* rather than a palette.
 *
 * `districts` is the plan: one role per region, so a visitor reads a village as a handful
 * of areas — the wood, the orchard, the water — instead of as one texture. That is the
 * level the variety lives at, because two villages built of the same pieces in different
 * districts look far less alike than two built of different pieces in the same arrangement.
 *
 * `gate` is which way the path leaves the door, and it is doing more work than it looks:
 * it decides where the eye enters the picture and which district the hall belongs to.
 *
 * `target` is what each grove should be worth. Spread from 205,000 to 430,000 rather than
 * bunched at the floor, because a board where every row reads within a few thousand of
 * every other says the number is decorative. `holdings` reaches the target by buying
 * further up the companion ladder, so a keeper with less land has more friends — which is
 * what somebody with the same amount to spend would actually have done.
 */
const PEOPLE = [
  {
    id: "showcase-01", target: 215000, name: "Mirelle", avatar: "coral", theme: MARSH,
    home: "manor", friends: 3, gate: ["south", "west"],
    land: ["hearthstead", "west_hollow", "still_shore", "south_bank", "east_meadow"],
    districts: {
      west_hollow: "wood", still_shore: "water", south_bank: "garden", east_meadow: "commons",
    },
    bio: "reedbeds and rope walks",
  },
  {
    id: "showcase-02", target: 240000, name: "Bramblewick", avatar: "timber", theme: WOODLAND,
    home: "hall", friends: 4, gate: ["south"],
    land: ALL_REGIONS.filter((r) => r !== "far_terrace" && r !== "still_shore"),
    districts: {
      dusk_field: "wood", north_reach: "wood", west_hollow: "wood",
      east_meadow: "orchard", south_bank: "commons", sunrise_field: "garden",
    },
    bio: "a clearing deep in the pines",
  },
  {
    id: "showcase-03", target: 300000, name: "Aurelian", avatar: "saffron", theme: ESTATE,
    home: "sanctum", friends: 3, gate: ["south", "east"],
    land: ALL_REGIONS,
    districts: {
      dusk_field: "wood", north_reach: "orchard", far_terrace: "wood",
      west_hollow: "garden", east_meadow: "commons",
      still_shore: "meadow", south_bank: "orchard", sunrise_field: "garden",
    },
    bio: "a formal estate on the terrace",
  },
  {
    id: "showcase-04", target: 265000, name: "Frost", avatar: "dewdrop", theme: FROST,
    home: "manor", friends: 3, gate: ["south"],
    land: ["hearthstead", "north_reach", "dusk_field", "west_hollow", "south_bank"],
    districts: {
      dusk_field: "wood", north_reach: "wood", west_hollow: "commons", south_bank: "garden",
    },
    bio: "a winter holding above the vale",
  },
  {
    id: "showcase-05", target: 205000, name: "Sable Dunn", avatar: "quill", theme: DUNE,
    home: "hall", friends: 2, gate: ["east"],
    land: ["hearthstead", "east_meadow", "sunrise_field", "south_bank", "far_terrace"],
    districts: {
      far_terrace: "orchard", east_meadow: "commons", sunrise_field: "garden", south_bank: "camp",
    },
    bio: "a waystation on the dry road",
  },
  {
    id: "showcase-06", target: 285000, name: "Old Weir", avatar: "pebble", theme: MILL,
    home: "manor", friends: 4, gate: ["south", "north"],
    land: ALL_REGIONS.filter((r) => r !== "dusk_field"),
    districts: {
      north_reach: "wood", far_terrace: "wood", west_hollow: "orchard",
      east_meadow: "commons", still_shore: "water", south_bank: "meadow",
      sunrise_field: "garden",
    },
    bio: "the mill and its waters",
  },
  {
    id: "showcase-07", target: 225000, name: "Emberly", avatar: "clementine", theme: EMBER,
    home: "hall", friends: 3, gate: ["south", "east"],
    land: ["hearthstead", "south_bank", "sunrise_field", "east_meadow", "north_reach"],
    districts: {
      north_reach: "wood", east_meadow: "orchard", south_bank: "commons",
      sunrise_field: "garden",
    },
    bio: "an autumn commons",
  },
  {
    id: "showcase-08", target: 430000, name: "Thornrest", avatar: "thorn", theme: RUIN,
    home: "sanctum", friends: 2, gate: ["west", "south"],
    land: ALL_REGIONS,
    districts: {
      dusk_field: "wood", north_reach: "ruin", far_terrace: "ruin",
      west_hollow: "wood", east_meadow: "commons",
      still_shore: "ruin", south_bank: "meadow", sunrise_field: "garden",
    },
    bio: "what grew over the old stones",
  },
  {
    id: "showcase-09", target: 335000, name: "Nyx", avatar: "indigo", theme: NIGHTBLOOM,
    home: "sanctum", friends: 3, gate: ["south", "west"],
    land: ALL_REGIONS.filter((r) => r !== "sunrise_field"),
    districts: {
      dusk_field: "wood", north_reach: "garden", far_terrace: "wood",
      west_hollow: "commons", east_meadow: "garden",
      still_shore: "water", south_bank: "orchard",
    },
    bio: "a garden that opens after dark",
  },
  {
    id: "showcase-10", target: 250000, name: "Halcyon", avatar: "shell", theme: HARBOUR,
    home: "manor", friends: 3, gate: ["south", "east"],
    land: ["hearthstead", "east_meadow", "south_bank", "still_shore", "sunrise_field",
           "west_hollow"],
    districts: {
      west_hollow: "orchard", east_meadow: "commons", still_shore: "water",
      south_bank: "garden", sunrise_field: "camp",
    },
    bio: "a harbour worth coming back to",
  },
];

// --------------------------------------------------------------- what they own
/**
 * A shopping list, not a switch that turns everything on.
 *
 * Each keeper owns the palette they actually built with, every home rung up to theirs,
 * and a run of companions — so their `homesteadOwned` reads like somebody's purchase
 * history rather than like a debug flag. The score follows from that rather than being
 * aimed at.
 */
function holdings(persona) {
  const rng = die(persona.id + ":own");

  const pieces = new Set();

  // Everything in their own palette, which is what they decorated with. A palette holds
  // lists for the roles that offer a choice and a bare id for the ones that must not vary
  // (the fence that holds a line, the paving, the lamp), so both shapes are taken.
  for (const entry of Object.values(persona.theme)) {
    for (const id of Array.isArray(entry) ? entry : [entry]) pieces.add(id);
  }

  // The home ladder up to theirs — the rungs are bought one after another, so owning the
  // manor and not the lodge is not a thing that can have happened.
  const tier = DWELLINGS[persona.home] ?? 1;
  for (const [id, t] of Object.entries(DWELLINGS)) if (t <= tier) pieces.add(id);

  // And a wider collection on top: somebody with a village this size has bought plenty
  // they did not end up using. Weighted to the cheap end, the way a real list is.
  const rest = Object.keys(PRICE).filter((id) => !pieces.has(id));
  rest.sort((a, b) => PRICE[a] - PRICE[b]);
  const depth = Math.floor(rest.length * (persona.breadth ?? .78));
  for (const id of rest.slice(0, depth)) if (rng.chance(.86)) pieces.add(id);

  const held = {
    pieces: [...pieces].filter((id) => PRICE[id] !== undefined),
    land: persona.land.filter((id) => REGION_PRICE[id] !== undefined),
    companions: [],
  };

  // The companion ladder is bought in order, so how far up it somebody has got is one
  // number. It is grown until the village is worth what this keeper is meant to be worth,
  // rather than authored beside the target and kept in step by hand — which means a
  // keeper who bought less land has bought more friends instead, exactly as a real one
  // with the same amount to spend would have.
  //
  // It is emphatically *not* tied to the companion they are wearing. That was the first
  // version and it is a mistake with a real-world shape: wearing an early favourite is
  // the commonest thing a long-standing player does, and it left a keeper who had bought
  // half the roster looking like somebody who had just started.
  const ladder = LADDER.filter((id) => COMPANIONS[id].cost > 0);

  for (let depth = 8; depth <= ladder.length; depth++) {
    held.companions = ladder.slice(0, depth);

    const worth = groveWorth(
      { homesteadOwned: held.pieces, groveLandOwned: held.land, companionsOwned: held.companions },
      GROVE_CONFIG, KEEPER_LEVEL, Number.MAX_SAFE_INTEGER
    );

    if (worth.score >= persona.target) break;
  }

  return held;
}

/**
 * Who this keeper wears, chosen from the companions they actually hold.
 *
 * A grove drawing a portrait its owner does not own would be the one detail in the whole
 * picture that cannot be explained — and it is the first thing anybody would notice,
 * because the nameplate is the top line of the card.
 */
function worn(persona, held) {
  if (held.companions.includes(persona.avatar)) return persona.avatar;

  // Reached only if a persona names a companion further up the ladder than their roster
  // goes, which is an authoring slip rather than a state. It is worth keeping as a
  // fallback and worth not relying on: the first run put two keepers in the same portrait,
  // and on a board where all ten rows are visible at once that reads as a bug rather than
  // as a coincidence.
  console.warn(`  note: ${persona.name} does not own ${persona.avatar}; wearing the last owned`);

  return held.companions[held.companions.length - 1] ?? "monarch";
}

// ------------------------------------------------------------------- the save
function buildSave(persona, held, placements) {
  const rng = die(persona.id + ":play");

  // A believable ledger: every glade cleared, most of them three-starred, a few not.
  const levels = {};
  let firstClear = 1750000000;
  for (const id of LEVEL_IDS) {
    const stars = rng.chance(.72) ? 3 : rng.chance(.6) ? 2 : 1;
    firstClear += Math.floor(rng.next() * 90000) + 20000;
    levels[id] = {
      stars,
      bestMoves: 30 + Math.floor(rng.next() * 40),
      bestMillis: 45000 + Math.floor(rng.next() * 70000),
      clears: 1 + Math.floor(rng.next() * 5),
      firstClearedUnix: firstClear,
      lastPlayedUnix: firstClear + 100000,
      bestRank: 0,
    };
  }

  return {
    schemaVersion: 17,
    updatedUnix: firstClear + 200000,
    legacyImportDone: true,
    lastPlayedLevelId: LEVEL_IDS[LEVEL_IDS.length - 1],
    levels,
    settings: { music: 1, sfx: 1, haptics: 1, board: 1, language: "en" },
    wallet: {
      displayName: persona.name,
      avatarId: worn(persona, held),
      heartsProduced: 120, heartsSpent: 96, heartsDueUnix: 0,
      hearts: 5, heartsNextRefillUnix: 0, heartBoostUntilUnix: 0,
    },
    companionsOwned: held.companions,
    homesteadOwned: held.pieces,
    groveLandOwned: held.land,
    homesteadPlaced: placements,
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

async function remove(token, path) {
  const response = await fetch(`${FS}/${path}`, {
    method: "DELETE", headers: { Authorization: `Bearer ${token}` },
  });

  if (!response.ok && response.status !== 404) {
    throw new Error(`${path}: ${response.status} ${await response.text()}`);
  }
}

// ----------------------------------------------------------------- a preview
/** The village as a picture, so a layout can be judged before it is written. */
function preview(persona, placements) {
  const at = new Map(placements.map((p) => [p.slot, p.piece]));
  const glyph = (piece) => {
    if (!piece) return " ·";
    if (piece.startsWith("friend_")) return " @";
    switch (SLOT[piece]) {
      case "canopy": return " ♣";
      case "path": return " ▪";
      case "edge": return " |";
      case "bed": return " ,";
      case "structure": return " ▲";
      default: return " o";
    }
  };

  const lines = [];
  for (let w = 0; w < ROWS; w++) {
    let line = "";
    for (let c = 0; c < COLS; c++) {
      const id = tileId(c, w);
      line += id === HALL ? " ■" : glyph(at.get(id));
    }
    lines.push(line);
  }
  return lines.join("\n");
}

// --------------------------------------------------------------------- main
const token = DRY ? null : accessToken();

if (REMOVE) {
  for (const persona of PEOPLE) {
    await remove(token, `groves/${persona.id}`);
    await remove(token, `players/${persona.id}/private/wallet`);
    await remove(token, `players/${persona.id}`);
    console.log(`removed ${persona.id}`);
  }
  console.log(`\n${PEOPLE.length} showcase grove(s) taken down`);
  process.exit(0);
}

const now = Math.floor(Date.parse("2026-08-20T12:00:00Z") / 1000);
let lowest = Infinity;

for (const persona of PEOPLE) {
  const held = holdings(persona);
  const placements = compose(persona, { land: [...persona.land] }, held);

  const save = buildSave(persona, held, placements);

  // Enough granted currency to cover what they built, with change. These are keepers who
  // bought coins; the clamp then never bites, which is what makes the card's score the
  // honest sum of what they hold rather than a number the ceiling chose.
  const worth = groveWorth(save, GROVE_CONFIG, KEEPER_LEVEL, Number.MAX_SAFE_INTEGER);
  const granted = Math.ceil((worth.bought + 25000) / 1000) * 1000;

  // Nothing may stand in a grove its keeper does not hold. A visitor cannot tell, and
  // neither can any check the game runs — the picker is what normally guarantees it — so a
  // village assembled by a script has to prove it for itself. A free piece is held by
  // everybody; anything else is either bought or a resident on the roster.
  const owns = new Set(held.pieces);
  const roster = new Set(held.companions.map((id) => `friend_${id}`));

  for (const { slot, piece } of placements) {
    const ok = FREE.has(piece) || owns.has(piece) || roster.has(piece)
            || earnedBy(save, piece);
    if (!ok) throw new Error(`${persona.id}: ${piece} stands on ${slot} and is not held`);
  }

  // How full the place is. The old villages ran at about four fifths, which is the single
  // number behind "it reads as noise" — see the composer.
  const tiles = [...persona.land, "hearthstead"]
    .filter((id, i, a) => REGIONS[id] && a.indexOf(id) === i)
    .reduce((n, id) => n + REGIONS[id].cols * REGIONS[id].rows, 0);

  const card = buildCard(persona.id, save, GROVE_CONFIG,
                         groveWorth(save, GROVE_CONFIG, KEEPER_LEVEL, granted),
                         KEEPER_LEVEL, now);

  card.synthetic = true;
  card.bio = persona.bio;

  lowest = Math.min(lowest, card.score);

  console.log(
    `\n${persona.name}  (${persona.id})  ${persona.bio}\n` +
    `  ${card.score.toLocaleString()} worth · ${card.stars}★ ${leagueOf(card.stars)} · ` +
    `${placements.length} piece(s) on ${tiles} tile(s) (${Math.round(placements.length / tiles * 100)}%) · ` +
    `${held.land.length + 1} region(s) · ` +
    `${held.companions.length} companion(s) · home ${card.dwelling}`
  );

  if (DUMP) {
    mkdirSync(DUMP, { recursive: true });
    writeFileSync(join(DUMP, `${persona.id}.json`), JSON.stringify({
      name: persona.name, land: held.land, dwelling: card.dwelling, placements,
    }, null, 1));
  }

  if (DRY) { console.log(preview(persona, placements)); continue; }

  await write(token, `players/${persona.id}`, save);
  await write(token, `players/${persona.id}/private/wallet`, {
    credits: { granted, spent: worth.bought, confirmedThroughUnix: now, earnedFloor: 0 },
    gems: { granted: 400, spent: 0, confirmedThroughUnix: now, earnedFloor: 0 },
  });
  await write(token, `groves/${persona.id}`, card);
}

console.log(
  `\n${PEOPLE.length} showcase grove(s) ${DRY ? "previewed" : "written"}` +
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
