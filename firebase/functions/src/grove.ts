/**
 * Glimmer Grove — the public boards.
 *
 * This is the file that makes a leaderboard safe in a game whose grove contents are
 * written by the client.
 *
 * ## Why anything here has to be recomputed
 *
 * `homesteadOwned`, `groveLandOwned` and `companionsOwned` are three id sets the player's
 * own device writes, and `firestore.rules` justifies letting it write them with the
 * sentence "a forged entry buys a picture on a screen nobody else sees". A leaderboard
 * makes that sentence false: the same forged entry now buys a position on a public list.
 *
 * So the client's figure is a prediction and this is the authority. `publishGrove` takes an
 * empty request, opens `players/{uid}` with admin credentials, and recomputes everything it
 * writes. There is nothing in the call for a modified client to put its thumb on.
 *
 * ## The bound, and why the score splits in two
 *
 * A grove's worth is what the player *holds*, and things are held two ways. Something
 * **earned** — a companion whose keeper gate the star ledger has passed — is derived from
 * records this server already validates for currency, so it is unforgeable by construction.
 * Something **bought** was paid for in credits, and credits are server-derived, so the
 * bought half has a ceiling nobody can lie past:
 *
 *     boughtValue <= earnedCredits + grantedBaseline
 *
 * That is invariant 13's fourth clause — a claim bounded so tightly that forging it buys
 * nothing — and it needs no new state on either side. A save awarding itself the whole
 * catalog scores exactly what its owner could have afforded.
 *
 * The clamp is deliberately generous rather than exact: it counts currency ever received
 * rather than currency actually spent on the grove, because a player who bought companions
 * and land legitimately must never be marked down for it. Understating a leaderboard
 * position is a bug; overstating one is an exploit.
 *
 * ## What is not defended, and why that is right
 *
 * A player can keep their card off the board by never asking for a publish. That is the
 * whole exploit available on the trigger side and it is self-punishing. A player can also
 * arrange their grove however they like — every piece is an id from a catalog we ship, so
 * there is no arrangement that produces anything a moderator would care about. The one
 * piece of free text is the name, and `publicName` is what stands in front of it.
 */

import { getFirestore, FieldValue, FieldPath } from "firebase-admin/firestore";
import { logger } from "firebase-functions";

import {
  ProgressionConfig, RewardRule, MAX_STARS, MAX_LEVEL_ID_LENGTH, earnedCredits,
} from "./progression";
import { PreparedBlocklist, judgeName } from "./profanity";
import { builtInBlocklist } from "./blocklist";

// --------------------------------------------------------------------------- config

/** Where the boards and the cards live. Named once so a typo cannot become two collections. */
export const GROVE_PATHS = {
  card: (uid: string) => `groves/${uid}`,
  board: (boardId: string) => `leaderboards/${boardId}`,
  groveConfig: "config/grove",
  ranksConfig: "config/groveRanks",
};

/**
 * One turret's row of the published roster.
 *
 * <b>`free` is carried rather than derived, and that is invariant 16j's trap said about a
 * shelf.</b> A free turret is held by everybody and never appears in `wardsOwned` at all, so a
 * line standing one would be silently dropped by an ownership test — the whole roster reading as
 * unowned for the one turret every account has. The predicate that means "free" has to be
 * written down where every caller sees it, not re-derived from a price that a second currency
 * will one day make ambiguous.
 */
export interface GroveWardEntry {
  /** The keeper level that opens this rung. Nought is ungated. */
  level: number;

  /** True when nothing has to be paid for it, in any currency. Mirrors `WardModel.IsStarter`. */
  free: boolean;
}

/** The grove catalog, published by the seeder from `homestead.json` and the manifest. */
export interface GroveConfig {
  /** `groveVersion` from the manifest, so a stale seed is visible in the document. */
  version: number;

  /**
   * Piece id → credits for one purchase. Free pieces are absent rather than zero; they
   * are worth nothing.
   *
   * Since save v20 a purchase of priced decor grants `bundles[id]` copies, so this is the
   * price of a *bundle* and a single copy is worth `cost / bundle`. Dwellings are in here
   * too and are never bundled — see `dwellings`.
   */
  pieces: Record<string, number>;

  /**
   * Piece id → how many copies one purchase grants. Absent means one.
   *
   * Published as a second map rather than by widening `pieces` into an object, because that
   * keeps this field additive: a config seeded before bundles existed reads as "everything
   * sells singly", which is exactly what it meant. What it buys is the ability to score a
   * grove by what was *paid* for it — ten fences bought in one bundle are worth the bundle,
   * not ten of them — so a bundle retune cannot inflate every existing grove on the boards.
   */
  bundles: Record<string, number>;

  /** Region id → credits. Starter land is absent for the same reason. */
  regions: Record<string, number>;

  /** Companion id → what it costs and what keeper level reaches it. */
  companions: Record<string, { cost: number; level: number }>;

  /**
   * Dwelling id → its rung on the home ladder.
   *
   * Published separately from `pieces` because the home is *derived* rather than placed —
   * the hall draws the best rung owned — so the server has to know which pieces are homes
   * and how they order. A rung absent from `pieces` is free, which is how the first one is
   * held by everybody without appearing in anyone's save.
   */
  dwellings: Record<string, number>;

  /**
   * Dwelling id → the keeper level that opens that rung. Absent means ungated.
   *
   * <b>A second map rather than widening `dwellings` into an object</b>, which is `bundles`'
   * bargain for its reason: it keeps the field additive in both directions. A function
   * deployed before the ladder was gated ignores it and scores exactly as it did; a function
   * reading a config seeded before it sees no gates and does the same. Widening the existing
   * map would make either of those a crash, and neither side can be redeployed first.
   *
   * What it buys is the home half of invariant 19a. A home rung stopped being something
   * credits alone could reach, so a save naming one whose gate its own star ledger has not
   * passed cannot have come about honestly — exactly as a save naming an unreachable
   * companion cannot — and without this the citadel is the one 30,000-credit entry on a
   * public score that a forged `homesteadStock` row buys for nothing.
   */
  dwellingLevels?: Record<string, number>;

  /**
   * Turret id -> the keeper level that opens its rung on the shelf. Absent means ungated.
   *
   * **A third additive map rather than a widening**, which is `bundles`' bargain for
   * `dwellingLevels`' reason: a function deployed before the turret line was published ignores
   * it and writes exactly the card it wrote yesterday, and a function reading a config seeded
   * before this field existed sees no roster and publishes no line. Neither side has to be
   * deployed first.
   *
   * <b>It is the whole of what the server knows about turrets, and that is deliberate.</b> A
   * line is a *picture* — it orders no board, pays nothing and is worth nothing to forge, which
   * is the same reading `placed` has carried since the card shipped. What the roster buys is the
   * one forgery a visitor could actually catch as a lie: a level-two keeper standing the
   * forty-gate turret. So the gate is asked (and asked **before** anything else, which is
   * `groveWorth`'s companion clause) and ownership is not, because nothing this server holds
   * implies a turret was bought — `wardsOwned` is client-written exactly as `companionsOwned`
   * is, and unlike a companion a turret has no price in the grove catalog to clamp against.
   *
   * The day a line reaches a board, a match-up or anything that pays, this stops being
   * defensible — the sentence `bestWave` carries, for the same reason.
   *
   * An entry per turret rather than a bare level, because `free` has to travel with it — see
   * {@link GroveWardEntry}.
   */
  wards?: Record<string, GroveWardEntry>;

  /** The star ladder, ascending. What a grove's worth is banded into on a card. */
  stars: number[];
}

/** The keeper-level curve, published into `config/progression` alongside the reward rules. */
export interface KeeperCurve {
  maxLevel: number;
  xpToNext: number[];
  tailXpToNext: number;
  tailXpIncrement: number;
}

/**
 * The curve used when the seeder has not published one.
 *
 * Mirrors `ProgressionTable.Default`. An absent curve must not mean "everybody is level 1",
 * because that would silently zero the earned half of every score in the world — a
 * publishing job that looks like it worked and quietly halves the leaderboard.
 */
export const DEFAULT_KEEPER_CURVE: KeeperCurve = {
  maxLevel: 60,
  xpToNext: [120, 180, 240, 320, 400, 500, 620, 760, 920, 1100],
  tailXpToNext: 1250,
  tailXpIncrement: 150,
};

// ---------------------------------------------------------------------- keeper level

/**
 * Total XP needed to stand at the start of each level, ascending.
 *
 * A byte-for-byte mirror of `ProgressionTable.Build`, and it has to be: the earned half of
 * a grove's worth is decided by which companion gates the player has passed, so a curve
 * that disagreed with the client's would put a different number on the board than the one
 * the player's own grove screen shows. Invariant 9a, for a leaderboard.
 */
export function cumulativeXp(curve: KeeperCurve): number[] {
  const maxLevel = Math.max(1, Math.floor(curve.maxLevel) || 1);
  const bands = Array.isArray(curve.xpToNext) ? curve.xpToNext : [];

  const cumulative = new Array<number>(maxLevel);
  cumulative[0] = 0;

  for (let level = 1; level < maxLevel; level++) {
    const step = level - 1 < bands.length
      ? bands[level - 1]
      : curve.tailXpToNext + curve.tailXpIncrement * (level - 1 - bands.length);

    cumulative[level] = cumulative[level - 1] + Math.max(1, Math.floor(step));
  }

  return cumulative;
}

/** The level an XP total stands at. Mirrors `ProgressionTable.LevelFor`. */
export function keeperLevel(xp: number, curve: KeeperCurve): number {
  const cumulative = cumulativeXp(curve);
  const total = xp > 0 ? Math.floor(xp) : 0;

  let lo = 1;
  let hi = cumulative.length;

  while (lo < hi) {
    const mid = lo + Math.floor((hi - lo + 1) / 2);
    if (cumulative[mid - 1] <= total) lo = mid;
    else hi = mid - 1;
  }

  return lo;
}

/**
 * XP derived from the star ledger.
 *
 * The same walk `earnedCredits` makes and validated the same way — a level id the catalog
 * has never heard of earns nothing, and stars are clamped to three — because the two
 * numbers have to describe the same set of believed records. The high-water floors in
 * `progression.json` are deliberately *not* read: they are client-written, and reading them
 * would hand back the forgeability this whole file exists to remove. Deriving alone can
 * only understate a player's level, which can only lower a score, which is the safe
 * direction.
 */
export function derivedXp(levels: unknown, config: ProgressionConfig): number {
  if (!levels || typeof levels !== "object" || Array.isArray(levels)) return 0;

  let xp = 0;

  for (const [levelId, raw] of Object.entries(levels as Record<string, unknown>)) {
    if (!levelId || levelId.length > MAX_LEVEL_ID_LENGTH) continue;

    const chapterId = config.levelChapters[levelId];
    if (chapterId === undefined) continue;

    const entry = raw as { stars?: unknown } | null;
    const rawStars = entry && typeof entry === "object" && typeof entry.stars === "number"
      ? Math.floor(entry.stars)
      : 0;

    if (rawStars <= 0) continue;

    const stars = Math.min(rawStars, MAX_STARS);
    const rule: RewardRule = config.chapterRewards[chapterId] ?? config.rewards;

    xp += rule.xpFirstClear + rule.xpPerStar * stars;
  }

  return xp;
}

// ---------------------------------------------------------------------------- score

/** What a grove is worth, split so the clamp can be applied to the half that needs it. */
export interface GroveWorth {
  /**
   * Value held without paying anything.
   *
   * Structurally **zero** since the companion rule became keeper level AND purchase: the
   * keeper ladder was the only thing in a grove that was ever handed over, and it no longer
   * is. Kept in the shape rather than deleted because the clamp is expressed in terms of the
   * split, the published card and the shared vectors are both built around it, and a rule
   * that puts something back here later — a companion granted by an event, say — should find
   * the half it belongs in already present rather than have to reintroduce it.
   */
  earned: number;

  /** Value paid for, before clamping. */
  bought: number;

  /** The ceiling the bought half is held to. */
  affordable: number;

  /** `earned + min(bought, affordable)`. What goes on the board. */
  score: number;

  /** Stars the score earns against the published ladder. */
  stars: number;

  /** True when the clamp actually bit, which is worth logging. */
  clamped: boolean;
}

/**
 * The `homesteadStock` rows of a save, as id → copies.
 *
 * Bounded on every axis a client controls, because this walks a client-written array: the
 * number of rows, the length of an id and the count on each row. `MAX_COPIES` mirrors
 * `GroveStock.MaxCopies` and exists so no arithmetic downstream can be made to overflow;
 * the *economic* bound is the affordability clamp in `groveWorth`, which is the one that
 * actually decides what a forged save scores.
 *
 * A v19 save carries `homesteadOwned` — a set of ids, from when owning a piece was
 * permission to draw it rather than possession of a copy — and it is read as one bundle of
 * each, which is exactly what that save used to score. A device that has not updated
 * therefore keeps its position on the boards instead of dropping to nothing, and its first
 * v20 push replaces the reading with the real one.
 */
const MAX_STOCK_ROWS = 512;
const MAX_COPIES = 9999;

function stockOf(save: Record<string, unknown>, grove: GroveConfig): Map<string, number> {
  const out = new Map<string, number>();
  const rows = save.homesteadStock;

  // An **empty** array falls through to the v19 field rather than meaning "owns nothing",
  // which is what `GroveStock.In` does on the client — and the two halves have to agree or
  // this is invariant 9a again. It is reachable: a document written by a v20 client that has
  // bought no decor carries `homesteadStock: []` beside a mirror, and a partial update that
  // rewrites only the legacy field leaves the empty array standing. Reading that as an empty
  // v20 save scores such a grove at zero, on a public board, with nothing to show why.
  if (Array.isArray(rows) && rows.length > 0) {
    for (const row of rows) {
      if (!row || typeof row !== "object") continue;

      const id = (row as { id?: unknown }).id;
      const copies = (row as { copies?: unknown }).copies;
      if (typeof id !== "string" || id.length === 0 || id.length > 64) continue;
      if (typeof copies !== "number" || !Number.isFinite(copies) || copies <= 0) continue;

      // The larger of two rows for one id, never the last one. The file forbids duplicates
      // (invariant 11a), so this is only reachable from a modified client — but `GroveStock`
      // resolves it by taking the larger, and two implementations of one rule that disagree
      // about a malformed input is exactly the drift invariant 9a is about. Cheaper to agree
      // than to find out later which half was right.
      const clamped = Math.min(Math.floor(copies), MAX_COPIES);
      const had = out.get(id) ?? 0;
      if (clamped > had) out.set(id, clamped);

      if (out.size >= MAX_STOCK_ROWS) break;
    }

    return out;
  }

  for (const id of idSet(save.homesteadOwned, MAX_STOCK_ROWS)) {
    const bundle = grove.bundles?.[id];
    out.set(id, typeof bundle === "number" && bundle > 1 ? Math.min(bundle, MAX_COPIES) : 1);
  }

  return out;
}

function idSet(raw: unknown, limit: number): Set<string> {
  const out = new Set<string>();
  if (!Array.isArray(raw)) return out;

  for (const value of raw) {
    if (typeof value !== "string" || value.length === 0 || value.length > 64) continue;
    out.add(value);
    if (out.size >= limit) break;
  }

  return out;
}

/** How many stars a score earns. Mirrors `GroveScoreTable.StarsFor`. */
export function starsFor(score: number, ladder: number[]): number {
  let stars = 0;
  for (let i = 0; i < ladder.length; i++) if (score >= ladder[i]) stars = i + 1;
  return stars;
}

/**
 * The furthest wave this save has ever reached on the Infinite lane. Mirrors
 * `EndlessLedger.BestIn`.
 *
 * ## The one figure on a card that cannot be recomputed
 *
 * Everything else `buildCard` writes is derived from records this server validates for
 * currency, or clamped to currency it derived itself (see this file's header). A wave count
 * is neither: nothing the server holds implies how far a run got, and there is no third
 * party to ask — which is invariant 10d's shape, arriving on a *reading* instead of on a
 * grant. So the two defences it has are the ones invariant 13 leaves when a claim cannot be
 * adjudicated:
 *
 *   * it is **bounded** to `MAX_WAVE`, which is far past anything the mode can produce, so a
 *     tampered save takes a row on a board rather than making every honest row unreadable
 *     beside a nineteen-digit one; and
 *   * **it buys nothing.** Credits and XP derive from the star ledger and from nothing else
 *     (invariant 9), so a forged wave moves a position on a list and not a balance. The day
 *     the endless board pays anything, this stops being defensible.
 *
 * Read the same way the client reads it and bounded the same way, or the client's prediction
 * and the card disagree for the one account that reaches the ceiling.
 */
export const MAX_WAVE = 9999;

export function bestWave(save: Record<string, unknown>): number {
  const rows = save.endlessBest;
  if (!Array.isArray(rows)) return 0;

  let best = 0;

  // The rules cap the array at 64 (`EndlessLedger.MaxRows`); walking no further is belt and
  // braces against a document written before that cap existed.
  for (const raw of rows.slice(0, 64)) {
    const row = raw as { level?: unknown; wave?: unknown } | null;
    if (!row || typeof row !== "object") continue;

    // A row naming nothing is not a run. The length cap is the level ledger's, for its
    // reason: an id this long cannot have come from a catalog we shipped.
    const level = typeof row.level === "string" ? row.level : "";
    if (level.length === 0 || level.length > MAX_LEVEL_ID_LENGTH) continue;

    const wave = Math.floor(Number(row.wave ?? 0));
    if (Number.isFinite(wave) && wave > best) best = wave;
  }

  return best <= 0 ? 0 : Math.min(best, MAX_WAVE);
}

/**
 * What this save's grove is worth, recomputed from scratch.
 *
 * `affordable` is passed in rather than derived here so that the caller can compose it
 * from the two things only it can see — the derived earnings and the wallet's granted
 * baseline — and so this function stays a pure one that the test vectors can drive.
 */
export function groveWorth(
  save: Record<string, unknown>,
  grove: GroveConfig,
  level: number,
  affordable: number
): GroveWorth {
  const stock = stockOf(save, grove);
  const land = idSet(save.groveLandOwned, 128);
  const companions = idSet(save.companionsOwned, 256);

  let bought = 0;
  let earned = 0;

  // A copy is worth `cost / bundle`, so a bundle comes back to the price paid for it. That
  // is the same reading this file has always taken — market value of what is held — and it
  // is what keeps a bundle retune from moving every grove already on the boards.
  //
  // A home rung is clamped to one copy. It is in `pieces` and it is not stock: the ladder
  // is a set of ids and the hall draws the best one owned, so a save claiming five sanctums
  // is claiming something the client cannot produce. Clamping is strictly tighter than
  // leaving it to the affordability ceiling, and the server knows which ids are rungs
  // because it publishes them.
  for (const [id, copies] of stock) {
    const cost = grove.pieces[id];
    if (typeof cost !== "number" || cost <= 0) continue;

    if (id in (grove.dwellings ?? {})) {
      // The gate is asked before the clamp, which is the companion clause below said about
      // the home ladder and is strictly tighter than clamping: a level-3 save claiming the
      // 30,000-credit citadel scores nothing for it rather than scoring what it could
      // afford. An ungated rung, and a config seeded before the ladder was gated, both read
      // as level 0 and are unaffected.
      if (level >= Math.floor(grove.dwellingLevels?.[id] ?? 0)) bought += cost;
      continue;
    }

    const bundle = grove.bundles?.[id];
    const unit = typeof bundle === "number" && bundle > 1
      ? Math.floor(cost / bundle)
      : cost;

    bought += unit * copies;
  }

  for (const id of land) {
    const cost = grove.regions[id];
    if (typeof cost === "number" && cost > 0) bought += cost;
  }

  // A companion is worth its price, and only a companion the save actually **owns** —
  // which is the whole of what changed when the unlock rule became keeper level AND
  // purchase. It used to be that passing a gate handed the companion over, so a gate the
  // star ledger had provably passed was value this server could vouch for on its own, and
  // it went into the unforgeable `earned` half. Nothing is handed over now: every companion
  // in a grove was paid for in credits, so every companion belongs in the clamped half,
  // beside the benches and the land that were always bought.
  //
  // The gate still does work, and it does it *before* the clamp rather than inside it: a
  // save naming a companion whose gate its own keeper level has not reached cannot have
  // come about honestly, so that entry is dropped outright rather than clamped down. That
  // is strictly tighter than clamping — a level-1 save claiming the 30,000-credit companion
  // now scores nothing for it instead of scoring whatever it could afford.
  //
  // **Walked through `heldCompanions` rather than inline**, because the card draws this set and
  // the score counts it, and two walks over one rule is how a visitor comes to see a portrait
  // the number beside it was never told about.
  for (const id of heldCompanions(companions, grove, level)) {
    bought += Math.floor(grove.companions[id].cost);
  }

  const ceiling = affordable > 0 ? Math.floor(affordable) : 0;
  const allowed = Math.min(bought, ceiling);
  const score = earned + allowed;

  return {
    earned,
    bought,
    affordable: ceiling,
    score,
    stars: starsFor(score, grove.stars ?? []),
    clamped: allowed < bought,
  };
}

/**
 * The priced companions a save may honestly be said to own, in catalog order.
 *
 * <b>One walk, read twice.</b> `groveWorth` prices it and `buildCard` publishes it, which is
 * what makes the portraits on a public profile and the number under them the same fact — the
 * alternative is two filters that agree until one of them is edited, and the symptom is a
 * stranger's screen showing a companion the score never counted.
 *
 * Three clauses and all three belong to the score:
 *
 *   * **priced only**, because a free companion is worth nothing and is held by *everybody* who
 *     has reached its gate — so it is not in `config/grove` at all, and a visitor's own client
 *     resolves it from its own roster (`CompanionLedger.IsHeld`) rather than being told;
 *   * **owned**, from the client-written set, which is what a purchase is;
 *   * **gated**, asked before anything else, because a save naming a companion whose gate its
 *     own keeper level has not reached cannot have come about honestly.
 *
 * Sorted rather than left in the save's order, so two devices that bought the same companions
 * in different orders publish byte-identical cards and neither churns a write.
 */
export function heldCompanions(
  owned: Set<string>, grove: GroveConfig, level: number
): string[] {
  const out: string[] = [];

  for (const [id, entry] of Object.entries(grove.companions)) {
    if (!entry || typeof entry.cost !== "number" || entry.cost <= 0) continue;
    if (!owned.has(id)) continue;
    if (level < Math.floor(entry.level ?? 0)) continue;

    out.push(id);
  }

  out.sort();
  return out;
}

// ------------------------------------------------------------------------- the line

/**
 * The colours a turret line may be drawn on, in the order the save writes them.
 * Mirrors `WardLine.Colours`.
 */
export const WARD_COLOURS = "rgby";

/** The rungs a turret may be upgraded to. Mirrors `WardStars.Least` and `WardStars.Most`. */
export const WARD_STARS_LEAST = 1;
export const WARD_STARS_MOST = 5;

/** What separates a turret's id from the colour it was bought for. Mirrors `WardHolding.Mark`. */
const WARD_HOLDING_MARK = ":";

/** One seat of a published line: the colour, the turret standing on it, and its rung. */
export interface CardSeat {
  /** One of {@link WARD_COLOURS}. */
  c: string;

  /** The turret's permanent id. */
  w: string;

  /** 1..5. Written out even at one, because a seat costs the same either way and absent is a branch. */
  s: number;
}

/**
 * Whether a save's `wardsOwned` covers a turret on a colour. Mirrors `WardHolding.Covers`.
 *
 * <b>The bare-id clause is the half that matters.</b> A row with no colour on it is what a build
 * that owned turrets outright wrote, and under a per-colour rule the honest reading is "on all
 * four" — a reader that checked only the exact key would quietly confiscate every holding
 * written before colours existed, which on a card is a line that silently loses seats.
 */
function ownsWard(
  owned: Set<string>, entry: GroveWardEntry, id: string, colour: string
): boolean {
  // A turret nobody has to pay for is held by everybody and is never written into `wardsOwned`,
  // so asking the set about one answers "no" for the turret every account in the game stands.
  if (entry.free) return true;

  return owned.has(id) || owned.has(id + WARD_HOLDING_MARK + colour);
}

/** The rung a save claims for one holding. Mirrors `WardStarLedger.StarsOf` and its clamp. */
function starsOf(save: Record<string, unknown>, id: string, colour: string): number {
  const rows = save.wardStars;
  if (!Array.isArray(rows)) return WARD_STARS_LEAST;

  // Keyed on the *holding* rather than on the turret, because a turret is bought per colour and
  // upgraded per seat — `WardStarDto.ward` is `{id}:{colour}`. The rules cap the array at 128;
  // walking no further is belt and braces against a document written before that cap existed.
  const key = id + WARD_HOLDING_MARK + colour;

  for (const raw of rows.slice(0, 128)) {
    const row = raw as { ward?: unknown; stars?: unknown } | null;
    if (!row || typeof row !== "object") continue;
    if (row.ward !== key) continue;

    const stars = Math.floor(Number(row.stars ?? 0));
    if (!Number.isFinite(stars)) return WARD_STARS_LEAST;

    return Math.min(Math.max(stars, WARD_STARS_LEAST), WARD_STARS_MOST);
  }

  // Absent means one star — what a turret bought before the ladder shipped means, and what a
  // rolled-back client writes. There is no sentinel and there never was one.
  return WARD_STARS_LEAST;
}

/**
 * The turret line a card carries: the seats this save may honestly be said to have arranged.
 *
 * <b>A seat the server cannot vouch for is omitted rather than corrected</b>, and that is what
 * lets this function know nothing about which turret is the starter. A visiting client resolves
 * a missing seat through `WardLine.Resolve`, which is the path every board in the game already
 * takes for a turret that was renamed, retired or never held — so an omitted seat draws the
 * starter, which is exactly what its owner's own game draws.
 *
 * Three refusals, in the order they are asked:
 *
 *   * the roster has never heard of the id (a retired turret, or a save from a newer drop);
 *   * the save does not hold it **on that colour** — a turret is bought per colour
 *     (`WardHolding`), and this is the one place a stored choice could otherwise put one on a
 *     seat nobody paid for;
 *   * the keeper level has not reached its rung, which is `groveWorth`'s companion clause and
 *     the one forgery about a line a visitor could catch as a lie.
 *
 * Emitted in colour order rather than in the save's row order, so two devices that arranged the
 * same line publish byte-identical cards.
 */
export function publishedLine(
  save: Record<string, unknown>, grove: GroveConfig, level: number
): CardSeat[] {
  const roster = grove.wards;
  if (!roster || typeof roster !== "object") return [];

  const rows = save.wardLoadout;
  if (!Array.isArray(rows)) return [];

  const owned = idSet(save.wardsOwned, 256);

  // Colour → chosen turret. A duplicated colour resolves the way the client's own reader does
  // (`WardLoadout.LoadFrom`): the last row wins.
  const chosen = new Map<string, string>();

  // The rules cap the array at 8.
  for (const raw of rows.slice(0, 8)) {
    const row = raw as { colour?: unknown; ward?: unknown } | null;
    if (!row || typeof row !== "object") continue;

    const colour = typeof row.colour === "string" ? row.colour : "";
    const ward = typeof row.ward === "string" ? row.ward : "";

    if (colour.length !== 1 || !WARD_COLOURS.includes(colour)) continue;
    if (ward.length === 0 || ward.length > 64) continue;

    chosen.set(colour, ward);
  }

  const out: CardSeat[] = [];

  for (const colour of WARD_COLOURS) {
    const ward = chosen.get(colour);
    if (!ward) continue;

    const entry = roster[ward];
    if (!entry || typeof entry !== "object") continue;      // not on the roster we published
    if (!ownsWard(owned, entry, ward, colour)) continue;    // not held on this seat
    if (level < Math.floor(entry.level ?? 0)) continue;     // not reached

    out.push({ c: colour, w: ward, s: starsOf(save, ward, colour) });
  }

  return out;
}

// ------------------------------------------------------- the arrangement's own takedown

/**
 * What the wallet records about this account's published *arrangement*.
 *
 * <b>Why a grove can be reported at all, when this file's own header says it cannot be.</b>
 * That header argued a grove "cannot be arranged into something offensive", because every piece
 * is an id from a catalog we ship. That was true of the handful of pre-placed dots the grove
 * shipped with and is false of a 28×28 floor sold with walls, gates and fences (invariants 16b
 * and 16e): walls tile, so a player with enough of them can write whatever they like on the
 * ground — and every gate in this project stays green while they do, because nothing here ever
 * opens a picture (invariant 32b, on somebody else's screen).
 *
 * It lives on the wallet beside the name's holding for that holding's reason: `publishGrove`
 * already opens the wallet, so honouring a takedown on the one path that has to costs no read
 * at all, where a flag anywhere else would be a document read per player per publish for ever
 * to carry one bit that is almost always nought.
 */
export interface GroveHolding {
  /** When the arrangement was taken off the boards, or 0 if it never was. */
  deniedUnix: number;
}

/** Reads what the wallet says about this account's arrangement. Adds no read of its own. */
export function heldGrove(walletData: Record<string, unknown> | undefined): GroveHolding | null {
  const raw = walletData?.grove as Partial<GroveHolding> | undefined;
  if (!raw || typeof raw !== "object") return null;

  return { deniedUnix: Math.floor(Number(raw.deniedUnix ?? 0)) };
}

/**
 * Whether this account's arrangement has been taken off the boards.
 *
 * Written as a predicate so a caller cannot get the sense of it backwards, which is `isDenied`'s
 * rule and for its reason: the obvious `holding.deniedUnix > 0` is exactly the expression
 * somebody writes as `!holding.deniedUnix` at the fourth call site.
 */
export function isGroveDenied(holding: GroveHolding | null | undefined): boolean {
  return !!holding && Math.floor(Number(holding.deniedUnix ?? 0)) > 0;
}

// ----------------------------------------------------------------------------- names

/** The longest public name. Mirrors `GroveNames.MaxLength`. */
export const MAX_NAME_LENGTH = 16;

/** The fewest visible characters a published name may have. Mirrors `GroveNames.MinLength`. */
export const MIN_NAME_LENGTH = 2;

/**
 * Everything a published name may not contain.
 *
 * The bidirectional controls are the important half and the reason this is not a length
 * check: U+202A–U+202E and U+2066–U+2069 re-order the text that *follows* them, so a name
 * carrying one misdraws the rest of the row rather than itself. The zero-width family is
 * here for the quieter version — a name that measures as fifteen characters and draws as
 * none, and a name that looks identical to somebody else's.
 *
 * Written as an explicit class rather than as a `\p{C}` match so the ranges are auditable,
 * and applied per code point so a surrogate pair is dropped whole.
 */
const FORBIDDEN = new RegExp(
  "[" +
  "\\u0000-\\u001F\\u007F-\\u009F" +   // C0 and C1 controls
  "\\u00AD" +                     // soft hyphen
  "\\u061C" +                     // arabic letter mark
  "\\u180E" +                     // mongolian vowel separator
  "\\u200B-\\u200F" +               // zero-width family, LRM, RLM
  "\\u2028\\u2029" +                // line and paragraph separators
  "\\u202A-\\u202E" +               // bidi embeddings and overrides
  "\\u2060-\\u2064" +               // word joiner and invisible operators
  "\\u2066-\\u206F" +               // bidi isolates and deprecated formats
  "\\uFEFF" +                     // zero-width no-break space
  "\\uFFF9-\\uFFFB" +               // interlinear annotation
  "]",
  "u"
);

/** Anything outside the Basic Multilingual Plane, which is where the emoji are. */
const ASTRAL = /[\u{10000}-\u{10FFFF}]/u;

/**
 * What counts as whitespace, spelled out rather than left to `\s`.
 *
 * This is exactly the set .NET's `char.IsWhiteSpace` returns true for, and it is written
 * out because the two languages disagree about two characters: JavaScript's `\s` matches
 * U+FEFF and does not match U+0085, and .NET is the other way round. Either disagreement
 * would put a different name on the board than the rename panel previewed — quietly, for
 * one player in a million, which is the worst kind of difference to have.
 */
const WHITESPACE = new RegExp(
  "[" +
  "\u0009-\u000D\u0020\u0085\u00A0" +
  "\u1680\u2000-\u200A" +
  "\u2028\u2029\u202F\u205F\u3000" +
  "]",
  "u"
);

/**
 * The public form of a stored name.
 *
 * A mirror of `GroveNames.Public`, and the authoritative one. It runs on the server because
 * a client's opinion about its own name is exactly the kind of claim that stops being
 * trustworthy the moment a stranger reads it.
 */
export function sanitiseName(stored: unknown): string {
  if (typeof stored !== "string" || stored.length === 0) return "";

  let out = "";
  let pendingSpace = false;

  for (const ch of stored) {
    // Whitespace is asked about first, and the order is the rule rather than a detail. A
    // tab is a control character *and* a word break; dropping it as the former turns
    // "Fern<tab>Willow" into one word, which is a different name from the one the player
    // typed. Anything that separates words separates them; only what draws as nothing is
    // deleted. The C# half tests in the same order for the same reason.
    if (WHITESPACE.test(ch)) {
      if (out.length > 0) pendingSpace = true;
      continue;
    }

    if (ASTRAL.test(ch) || FORBIDDEN.test(ch)) continue;

    if (pendingSpace) {
      if (out.length >= MAX_NAME_LENGTH) break;
      out += " ";
      pendingSpace = false;
    }

    if (out.length >= MAX_NAME_LENGTH) break;
    out += ch;
  }

  return out.replace(/ +$/u, "");
}

/**
 * Whether a sanitised name may go on a board.
 *
 * <b>The word matching moved out of this file and the fold is the reason.</b> What stood here
 * was thirteen English words and `flat.includes(word)` over a string with everything outside
 * `a-z0-9` deleted, and it was weaker than it read in three ways that are each one keystroke:
 * leetspeak walked past it (`5hit`, `f4ggot`, `phuck`), a single Cyrillic character defeated
 * it entirely (the deletion removed the `с` from `fuсk` and left `fuk`, which matched
 * nothing), and any name written in a non-Latin script squashed to the empty string and was
 * never filtered at all — which in a game that ships globally is most of the world. It also
 * refused **Grapevine**, in a game about a garden, because `rape` is a substring of it.
 *
 * `profanity.ts` holds the fold and the three matching classes; `blocklist.ts` holds where the
 * list comes from and how fast a change to it lands. This is the seam they meet at, and it is
 * kept synchronous — with the shipped list as the default — because it has four call sites
 * that have no database in hand and no reason to grow one.
 *
 * The length test stays here rather than moving with the rest: it is a fact about what a row
 * can draw, not about what a word means, and `MIN_NAME_LENGTH` is this file's constant.
 */
export function isNameAllowed(name: string, list: PreparedBlocklist = builtInBlocklist()): boolean {
  if (name.length < MIN_NAME_LENGTH) return false;

  return judgeName(name, list).allowed;
}

/**
 * A generated handle for a keeper who has no usable name of their own.
 *
 * <b>The server does this and the client never could.</b> Two unnamed keepers still need
 * rows that differ, and the discriminator has to be stable — a name that changed on every
 * publish would make one player look like several across a day's boards. It is derived from
 * the uid, which the client cannot reproduce for anybody else and has no reason to.
 *
 * It is also the answer for a name the filter refused: the player keeps their name on their
 * own screens and simply appears under a generated one publicly, which is a quieter and
 * more proportionate response than refusing to publish them at all.
 */
export function fallbackName(uid: string): string {
  let hash = 2166136261;

  for (let i = 0; i < uid.length; i++) {
    hash ^= uid.charCodeAt(i) & 0xff;
    hash = Math.imul(hash, 16777619) >>> 0;
    hash ^= (uid.charCodeAt(i) >> 8) & 0xff;
    hash = Math.imul(hash, 16777619) >>> 0;
  }

  return `Keeper ${String(hash % 10000).padStart(4, "0")}`;
}

/**
 * What a stored name resolves to, ignoring uniqueness.
 *
 * Kept as the definition of "what this string would be called publicly", and used by the
 * claim path to work out what to reserve. It is deliberately no longer what a card is built
 * from — see `boardName`.
 */
export function publicName(
  stored: unknown, uid: string, list?: PreparedBlocklist
): string {
  const cleaned = sanitiseName(stored);
  return isNameAllowed(cleaned, list) ? cleaned : fallbackName(uid);
}

/**
 * The name that actually goes on the board.
 *
 * **Read from the reservation, never from the save**, which is the change uniqueness forced
 * and a security improvement on its own. `players/{uid}` is client-written, so building a
 * card from `wallet.displayName` meant the one string on a public list came from the one
 * document an attacker fully controls — sanitised, but theirs. It now comes from
 * `players/{uid}/private/wallet`, which only `names.ts` writes and no client may, so a
 * modified save changes its owner's screens and leaves the board untouched.
 *
 * **The word filter runs again here, on a name that already passed it.** That is not
 * redundancy: the list grows, and re-testing at publish time means adding a word takes a name
 * off every board on its next rebuild rather than needing a sweep over the reservations. It is
 * also what makes `config/names` a takedown lever rather than merely a rule for future renames
 * -- a word added to the published list at noon is off every card by that account's next publish.
 *
 * A keeper with no confirmed name — never renamed, or renamed while offline and not yet
 * claimed — is published under a generated handle, exactly as an unnamed one always was.
 */
export function boardName(
  confirmed: string | null | undefined, uid: string, list?: PreparedBlocklist
): string {
  const cleaned = sanitiseName(confirmed);
  return isNameAllowed(cleaned, list) ? cleaned : fallbackName(uid);
}

// ------------------------------------------------------------------------ the card

/**
 * A placement as it appears on a card: a bare id, or a map when the piece has been turned.
 *
 * Two shapes because a turned piece is the exception, and a full floor is a thousand rows:
 * a bare string keeps the common case to one value and the document to about a third of
 * what a uniform map would cost. `facing` is a quarter turn, 1..3 — a piece facing 0 is
 * written as the bare string, because that is what it means.
 */
type CardPlacement = string | { piece: string; facing: number };

export interface GroveCardDoc {
  name: string;
  avatar: string;
  level: number;
  score: number;
  stars: number;
  dwelling: string;
  land: string[];
  placed: Record<string, CardPlacement>;
  builtUnix: number;

  /**
   * Where the keeper moved their hall, and which way they turned it. Absent means wherever
   * the floor says, which is what every grove was before a seat could be stored at all.
   *
   * Sanitised rather than trusted, exactly as `placed` is: it is a picture, it is worth
   * nothing to forge, and the only thing a bad value could do is put somebody's own house
   * somewhere odd on their own card.
   */
  hall?: string;
  hallFacing?: number;

  /**
   * The furthest wave this keeper has held out to on the Infinite lane — what the `endless`
   * board is ordered on. See `bestWave` for why it is bounded rather than recomputed.
   *
   * **Absent rather than nought for a keeper who has never played the lane**, and that is the
   * whole cost story of the second board. Firestore indexes a field only on the documents
   * that carry it, so `orderBy("wave","desc")` and `where("wave",">",0).count()` walk an
   * index holding the endless players alone rather than every card in the game — a board that
   * costs a hundred reads a night at any population, and a count billed against the people on
   * it rather than against everybody.
   */
  wave?: number;

  /**
   * The priced companions this keeper owns, as permanent ids — what a public profile draws.
   *
   * <b>Exactly the set `groveWorth` counted</b> (`heldCompanions`), so the portraits and the
   * number under them are one fact rather than two filters that agree until one is edited.
   *
   * <b>The free companion is deliberately absent.</b> It has no price, so it is not in
   * `config/grove` at all, and it is held by everybody who has reached its gate — a visitor's
   * own client resolves it through `CompanionLedger.IsHeld` over its own roster, which is the
   * same rule it applies to the player in front of it. Telling a visitor something every
   * account already knows would be a field that can go stale.
   *
   * Absent rather than empty for a keeper who has bought none, which is what every card written
   * before this deployment says and is the same answer.
   */
  companions?: string[];

  /**
   * The turret line this keeper carries into a siege: colour, turret and rung, in colour order.
   *
   * Only the seats the server can vouch for (`publishedLine`); a seat it cannot is **omitted**,
   * and a visiting client fills it with its own roster's starter exactly as its owner's game
   * does. Absent for a keeper who has never arranged one, and absent on a deployment whose
   * `config/grove` carries no turret roster — a stale seed publishes no line rather than an
   * unvouched one.
   */
  line?: CardSeat[];

  /** The grove catalog this was scored against, so a stale seed is diagnosable. */
  catalogVersion: number;
}

/**
 * Whether the keeper has asked to be on the boards.
 *
 * Read off the save the server already has open, so the refusal is enforced where it cannot
 * be talked out of by a modified client. Absent means yes — the flag is tri-state precisely
 * so "never chosen" is distinguishable, and a keeper who has never renamed is published
 * under a generated handle that names nobody.
 */
export function optedIn(save: Record<string, unknown>): boolean {
  const settings = save.settings as Record<string, unknown> | undefined;
  if (!settings || typeof settings !== "object") return true;

  // 0 unset, 1 on, 2 off — `StoredFlag` in SaveSchema.cs.
  return Math.floor(Number(settings.board ?? 0)) !== 2;
}

/**
 * Builds the card document for a save.
 *
 * Everything on it is recomputed or sanitised. The only fields taken from the save as
 * written are the arrangement and the ids in it — a picture, bounded by the rules' own size
 * caps, and worth nothing to forge.
 */
export function buildCard(
  uid: string,
  save: Record<string, unknown>,
  grove: GroveConfig,
  worth: GroveWorth,
  level: number,
  nowUnix: number,
  confirmedName: string | null,
  list?: PreparedBlocklist,
  groveDenied = false
): GroveCardDoc {
  const wallet = (save.wallet ?? {}) as Record<string, unknown>;

  const land: string[] = [];
  for (const id of idSet(save.groveLandOwned, 128)) {
    if (grove.regions[id] !== undefined) land.push(id);
  }
  land.sort();

  // The best dwelling held, which is derived rather than placed — the hearth's rule, and
  // the reason a home cannot be bought and then not seen. "Held" is the same composite the
  // client uses: a rung with no price is free to everybody, and a priced one has to be in
  // the purchased set. Ties break on catalog order, which is arbitrary and stable.
  const owned = idSet(save.homesteadOwned, 512);
  let dwelling = "";
  let dwellingTier = -1;

  for (const [id, rawTier] of Object.entries(grove.dwellings ?? {})) {
    const priced = typeof grove.pieces[id] === "number" && grove.pieces[id] > 0;
    if (priced && !owned.has(id)) continue;

    // And the gate, for the same reason the worth drops such a rung: a card drawing a
    // citadel over a ledger that cannot reach one is the inconsistency a visitor could
    // actually catch, and it would draw it on every board this grove appears on.
    if (level < Math.floor(grove.dwellingLevels?.[id] ?? 0)) continue;

    const tier = Math.floor(rawTier ?? 0);
    if (tier > dwellingTier) {
      dwelling = id;
      dwellingTier = tier;
    }
  }

  // **An arrangement taken off the boards publishes no arrangement**, which is
  // `publishableName`'s fall-through wearing different clothes and is the whole mechanism of a
  // grove takedown. It is deliberately the *only* thing a takedown costs: the keeper keeps their
  // grove on their own screens, their name, their score, their stars and their row, exactly as a
  // reported name keeps everything but its place on a board. A withdrawal would be a punishment
  // pipeline, and this file's own header says why we do not have one.
  //
  // Read here rather than at the call site so that the report path and `publishGrove` cannot
  // come to disagree about what a denial does — a rule with two readers and no home is a rule
  // with two answers.
  const placed: Record<string, CardPlacement> = {};
  const rows = groveDenied || !Array.isArray(save.homesteadPlaced) ? [] : save.homesteadPlaced;

  for (const raw of rows) {
    const row = raw as { slot?: unknown; piece?: unknown; facing?: unknown } | null;
    if (!row || typeof row !== "object") continue;

    const slot = typeof row.slot === "string" ? row.slot : "";
    const piece = typeof row.piece === "string" ? row.piece : "";

    // An emptied slot is a real instruction in the save (invariant 16) and nothing at all
    // on a card: a visitor cannot tell "never touched" from "cleared", and does not need to.
    if (slot.length === 0 || slot.length > 32 || piece.length === 0 || piece.length > 64) continue;

    // Clamped rather than trusted: `facing` comes out of a client-written save, and a card
    // is what strangers see. Anything outside 0..3 is read as 0, which is the facing every
    // piece had before they could be turned and the only safe reading of a value that
    // cannot have come from this build.
    const facing = typeof row.facing === "number" && Number.isInteger(row.facing)
      ? ((row.facing % 4) + 4) % 4
      : 0;

    placed[slot] = facing === 0 ? piece : { piece, facing };

    if (Object.keys(placed).length >= 1024) break;
  }

  // Spread rather than written as `wave: bestWave(save)`, because Firestore refuses
  // `undefined` and a nought written out would put every card in the game into the endless
  // board's index — see `GroveCardDoc.wave`.
  const wave = bestWave(save);

  // Both spread for Firestore's reason and omitted when empty for the document's: a card with
  // no companions and no line is what every card written before this deployment is, and absent
  // has to keep meaning exactly that.
  const companions = heldCompanions(idSet(save.companionsOwned, 256), grove, level);
  const line = publishedLine(save, grove, level);

  return {
    name: boardName(confirmedName, uid, list),
    avatar: typeof wallet.avatarId === "string" ? wallet.avatarId.slice(0, 64) : "",
    level,
    score: worth.score,
    stars: worth.stars,
    dwelling,
    land,
    placed,
    ...hallSeat(save),
    ...(wave > 0 ? { wave } : {}),
    ...(companions.length > 0 ? { companions } : {}),
    ...(line.length > 0 ? { line } : {}),
    builtUnix: nowUnix,
    catalogVersion: Math.floor(grove.version ?? 0),
  };
}

/**
 * The hall's seat, read off the save the way a placement is.
 *
 * Omitted entirely when the keeper has never moved their home, so a card carries the field
 * only when it says something — which keeps every card written before this deployment
 * correct rather than merely old: absent has always meant "where the floor says".
 */
function hallSeat(save: Record<string, unknown>): { hall?: string; hallFacing?: number } {
  const slot = typeof save.groveHall === "string" ? save.groveHall : "";
  if (slot.length === 0 || slot.length > 32) return {};

  const raw = save.groveHallFacing;
  const facing = typeof raw === "number" && Number.isInteger(raw) ? ((raw % 4) + 4) % 4 : 0;

  return facing === 0 ? { hall: slot } : { hall: slot, hallFacing: facing };
}

/** Guards against a grove config that was never seeded or was seeded badly. */
export function assertUsableGroveConfig(config: unknown): asserts config is GroveConfig {
  const c = config as GroveConfig | undefined;

  if (
    !c ||
    typeof c !== "object" ||
    typeof c.pieces !== "object" || c.pieces === null ||
    typeof c.regions !== "object" || c.regions === null ||
    typeof c.companions !== "object" || c.companions === null ||
    typeof c.dwellings !== "object" || c.dwellings === null ||
    !Array.isArray(c.stars)
  ) {
    throw new Error("config/grove is missing or malformed; run the seed script");
  }
}

// ----------------------------------------------------------------------- the ranks

/** How many saves one ranking run reads. Bounded, so the cost never grows with the game. */
export const RANK_SAMPLE_SIZE = 5000;

/** How many rows a published board carries. Mirrors `LeaderboardBoard.MaxRows`. */
export const BOARD_ROWS = 100;

export interface RankedGrove {
  uid: string;
  name: string;
  avatar: string;
  level: number;
  score: number;
  stars: number;

  /**
   * The furthest wave, carried on every row of every board rather than only on the endless
   * one.
   *
   * One row shape for both boards, because a row *is* the same row — the person, their
   * companion and their keeper level are the same facts whichever list they are read off, and
   * which figure gets drawn is the board's decision (`LeaderboardBoard.IsEndless`). Two row
   * shapes would be two readers on the client for one document format.
   *
   * Nought for a keeper who has never played the lane, which is what a global row usually is.
   * Written out here rather than omitted: inside an array it costs a byte and buys nothing,
   * and unlike the card's own field it is not in an index.
   */
  wave: number;
}

/**
 * Nine deciles of a sorted list, nearest-rank. The same definition `stats.ts` uses.
 *
 * <b>An empty list has no deciles, and saying so is load-bearing.</b> The obvious loop
 * indexes `sorted[-1]` nine times and produces nine `undefined`s, which Firestore refuses as
 * a document value — so the whole ranking job threw *after* it had written ten board
 * documents, leaving the boards published and `config/groveRanks` absent. That is the state
 * on the first day of the feature, when nobody has a card yet, so it is the state it would
 * have shipped in. `stats.ts` never hits it because its buckets exist only once something has
 * been pushed into them; this one derives its list from a filter and can legitimately get
 * nothing.
 *
 * An empty array is also exactly what the client reads as "nothing to say": it refuses any
 * table that is not nine ascending values and draws no percentile, which is the right
 * behaviour for a population of nobody.
 */
export function deciles(sorted: number[]): number[] {
  if (sorted.length === 0) return [];

  const out: number[] = [];

  for (let d = 1; d <= 9; d++) {
    const rank = Math.ceil((d / 10) * sorted.length) - 1;
    out.push(sorted[Math.min(Math.max(rank, 0), sorted.length - 1)]);
  }

  return out;
}

/**
 * Every board this job writes, and the whole of what `leaderboards` is allowed to hold.
 *
 * Two, and they are the game's two permanent numbers: what a keeper has **built** (the grove's
 * worth) and how far they have **held out** (the Infinite lane's wave count). Mirrored by
 * `LeaderboardBoard.All`, which is what the client asks for, and a board id is permanent for
 * invariant 1's reason — it names a document, so renaming one orphans whatever the last run
 * wrote and empties the screen until the next.
 *
 * **What used to be here was nine league boards** (l0 to l8) cutting the global board's own
 * number into bands. They cost nine queries and nine counts a night to answer a question the
 * published distribution already answers exactly and at O(1) (`config/groveRanks`, invariant
 * 19c), and no screen in the game ever named one. Those ids are spent and must never be
 * reused; `pruneRetiredBoards` is what takes the documents away.
 */
export const BOARD_IDS = ["global", "endless"];

/** The alphabet a Firebase uid is drawn from, for the sampling cursor. See `randomCursor`. */
const UID_ALPHABET = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

/**
 * A random point in document-id space, so the decile sample is a fresh window every run.
 *
 * **Without it the sample is a fixed panel.** `limit(n)` with no cursor returns the *n lowest
 * document ids*, which is an unbiased sample of score - a uid is random and knows nothing
 * about how good somebody's grove is - but it is the same players every day for ever. That is
 * fine for the arithmetic and wrong for the game: a keeper whose uid sorts late could never
 * contribute to a decile, and nobody could ever tell, because the numbers would look
 * completely reasonable. Three characters is about a quarter of a million starting points,
 * far finer than the sample is wide.
 */
function randomCursor(): string {
  let out = "";
  for (let i = 0; i < 3; i++) {
    out += UID_ALPHABET[Math.floor(Math.random() * UID_ALPHABET.length)];
  }
  return out;
}

/**
 * A bounded random sample of cards, for the deciles and nothing else.
 *
 * **Projected, and it now projects two fields for the price of one.** `select(...)` still costs
 * one read per document - Firestore bills the index entry rather than the payload - but it is
 * the difference between pulling five thousand whole cards into a function's memory and pulling
 * five thousand pairs of numbers. A card carries every placement in somebody's grove.
 *
 * **Both distributions come out of one walk, and that is why the second board's percentile was
 * nearly free.** A board is a hundred rows; a *percentile* is what answers "where do I stand" for
 * everybody the hundred cannot reach, and without one the Endless Watch would be a feature for a
 * hundred people at any population. Sampling waves separately would have cost another five
 * thousand reads a night; asking the same documents for a second field costs nothing at all.
 *
 * The two arrays are deliberately **not** the same length. A card with a wave and an empty grove
 * belongs in the wave distribution and not in the worth one, and vice versa - each population is
 * "keepers who have actually done this thing", which is the only population a percentile means
 * anything against (see `GroveRankTable`). That is also why each ships its own sample count.
 *
 * **The window wraps, and it wraps with `endBefore`.** A cursor landing near the end of the
 * id space would otherwise return a short sample, and a short sample taken from the end of
 * the alphabet is exactly the bias the cursor exists to remove. The remainder has to be taken
 * from *before* the cursor rather than from the start of the collection: with a population
 * smaller than the sample size the second query would otherwise return documents the first
 * one already returned, and every card from the cursor to the end would be counted twice.
 * That is not a rounding error — it double-weights the tail of the id space in a distribution
 * players are shown as a percentile. It was caught by a live run reporting 29 samples against
 * a population of 15.
 *
 * **A card worth nothing is read and then discarded, and that is now a real dilution.** Since
 * the endless board shipped, a keeper who has played the Infinite lane publishes a card even
 * with an empty grove (`GrovePublishPolicy.WorthPublishing`), so the window can hand back
 * documents whose score is nought — which is exactly right for the deciles, whose population
 * is deliberately "keepers who have built something", and which means the *usable* sample is
 * smaller than `RANK_SAMPLE_SIZE`. It degrades the honest way: below
 * `GroveRankTable.MinimumSamples` the client draws no percentile at all rather than a wrong
 * one. Raising the sample size is the lever if that ever binds — it cannot be filtered in the
 * query, because Firestore wants the inequality field first in the ordering and this one is
 * ordered by document id.
 */
export interface RankSample {
  /** Grove worth, for every sampled card worth more than nothing. */
  scores: number[];

  /** Furthest wave, for every sampled card that has ever run the Infinite lane. */
  waves: number[];
}

async function sampleRanks(db: FirebaseFirestore.Firestore): Promise<RankSample> {
  const groves = db.collection("groves");
  const byId = FieldPath.documentId();
  const cursor = randomCursor();

  const first = await groves.orderBy(byId).startAt(cursor)
                            .limit(RANK_SAMPLE_SIZE).select("score", "wave").get();

  const sample: RankSample = { scores: [], waves: [] };

  const take = (snap: FirebaseFirestore.QuerySnapshot) => {
    for (const doc of snap.docs) {
      const score = Math.floor(Number(doc.get("score") ?? 0));
      if (Number.isFinite(score) && score > 0) sample.scores.push(score);

      // Absent on every card whose owner has never played the lane, which is most of them -
      // and absent has to read as "not in this population" rather than as a nought, or the
      // median wave would be nought and the first person to finish a run would be told they
      // are ahead of ninety per cent of the world.
      const wave = Math.floor(Number(doc.get("wave") ?? 0));
      if (Number.isFinite(wave) && wave > 0) sample.waves.push(wave);
    }
  };

  take(first);

  if (first.size < RANK_SAMPLE_SIZE) {
    const rest = await groves.orderBy(byId).endBefore(cursor)
                             .limit(RANK_SAMPLE_SIZE - first.size).select("score", "wave").get();
    take(rest);
  }

  return sample;
}

/**
 * The field a board is ordered on, which is the whole of what tells two boards apart here.
 *
 * A board is "rank the cards by one number, best first", so the only thing that varies is
 * which number — which keeps `topOf` and `countOf` one query each rather than a branch per
 * board, and makes a third board a row in a table instead of a third code path. A field named
 * here **must** be one `buildCard` writes, or the board is silently empty for ever: nothing
 * fails, the query simply matches no document.
 *
 * Both are single fields, so Firestore indexes them automatically and neither board needs a
 * composite index. That is not an accident — it is why a board is ordered on a figure the card
 * already carries rather than on one derived at query time.
 */
const BOARD_FIELD: Record<string, "score" | "wave"> = {
  global: "score",
  endless: "wave",
};

/** The top rows of one board, straight out of the index. */
async function topOf(db: FirebaseFirestore.Firestore, boardId: string): Promise<RankedGrove[]> {
  const field = BOARD_FIELD[boardId];
  if (!field) return [];

  const snapshot = await db.collection("groves")
                           .orderBy(field, "desc").limit(BOARD_ROWS).get();

  const rows: RankedGrove[] = [];

  for (const doc of snapshot.docs) {
    const data = doc.data() as Partial<GroveCardDoc>;

    const score = typeof data.score === "number" ? Math.floor(data.score) : 0;
    const wave = typeof data.wave === "number" ? Math.floor(data.wave) : 0;

    // Nothing worth ranking. A card carrying a nought sorts to the bottom of its own board
    // rather than being absent from it, which is the one thing an `orderBy` alone cannot say
    // — and it is unreachable on `endless`, where a nought is never written at all.
    if ((field === "score" ? score : wave) <= 0) continue;

    rows.push({
      uid: doc.id,
      name: typeof data.name === "string" ? data.name : "",
      avatar: typeof data.avatar === "string" ? data.avatar : "",
      level: typeof data.level === "number" ? Math.floor(data.level) : 1,
      score,
      stars: typeof data.stars === "number" ? Math.floor(data.stars) : 0,
      wave,
    });
  }

  return rows;
}

/**
 * How many keepers are on one board, counted rather than sampled.
 *
 * An aggregation, which Firestore bills at one read per thousand index entries — so this is
 * the one figure here that grows with the game, and it grows at a thousandth of it. The
 * inequality is what keeps it honest on `global`, where every published card carries a `score`
 * and a good many of them are worth nothing; on `endless` it is nearly free, because a card
 * with no wave is not in that index at all.
 */
async function countOf(db: FirebaseFirestore.Firestore, boardId: string): Promise<number> {
  const field = BOARD_FIELD[boardId];
  if (!field) return 0;

  const snapshot = await db.collection("groves").where(field, ">", 0).count().get();
  return snapshot.data().count;
}

/**
 * Deletes any board document this build no longer publishes.
 *
 * **A retired board is worse than a missing one.** It keeps whatever rows the last run that
 * knew about it wrote, for ever, readable by any signed-in player — a picture of a ladder that
 * no longer exists, which nothing will ever correct because nothing writes it. That is what
 * the nine league boards became the moment `BOARD_IDS` stopped naming them.
 *
 * It is a standing rule in the job rather than a one-off script, for invariant 7a's reason: a
 * cleanup somebody has to remember on the day they retire a board will be forgotten, and its
 * failure is invisible. `listDocuments` bills one read per document name and this collection is
 * bounded by `BOARD_IDS` from the next run onwards, so the steady-state cost is two reads a
 * night for ever and the collection can never quietly grow a stale member again.
 */
async function pruneRetiredBoards(db: FirebaseFirestore.Firestore): Promise<string[]> {
  const live = new Set(BOARD_IDS);
  const stale = (await db.collection("leaderboards").listDocuments())
                  .filter((ref) => !live.has(ref.id));

  await Promise.all(stale.map((ref) => ref.delete()));

  return stale.map((ref) => ref.id);
}

/**
 * Rewrites every board, the population counts and the distribution.
 *
 * **Three kinds of question, three kinds of query, and that split is the whole design.** It
 * used to be one: read a bounded slab of cards and derive all three from it. That is exactly
 * right for a decile and exactly wrong for a leaderboard - past `RANK_SAMPLE_SIZE` published
 * groves the "finest groves" board stopped being the finest and became the best of an
 * arbitrary five thousand, with no symptom anybody could see. A top hundred has to be a
 * *query*, because it is the one number here where approximately right is wrong.
 *
 * - **Boards** come from `orderBy(field,"desc").limit(100)`: exact at any population, and a
 *   hundred reads each whether the game has a thousand players or ten million.
 * - **Populations** come from a `count()` aggregation, which Firestore bills at one read per
 *   thousand matches rather than one per document. "The finest of N keepers" is now N rather
 *   than the size of a sample.
 * - **Deciles** stay sampled, because a percentile from a few thousand draws is accurate to
 *   far under the point it is rounded to, and an exact one would mean reading every card in
 *   the game every day to tell somebody they are in the top 12%. **Both** distributions come
 *   out of the one sample, so the second board's percentile costs no reads at all.
 *
 * **What it costs at ten million published cards:** two board queries at a hundred rows (200),
 * two counts (Firestore bills an aggregation at one read per thousand index entries, so the
 * global count is at most 10,000 and the endless one is a thousandth of however many people
 * have actually played the lane), the prune (one read per board document, so two), and the
 * sample (5,000). Call it fifteen thousand reads a day for the entire game, once, at four in
 * the morning - a fraction of a penny. **Nothing here grows with the player count except the
 * counts, and those grow at a thousandth of it**; adding a board adds a hundred reads a night
 * and one aggregation, which is the whole reason a board is a document rather than a query.
 *
 * Every board is written whether or not anything is on it, so a board that emptied stops
 * showing yesterday's rows rather than keeping them for ever, and one that has been *retired*
 * is deleted outright (`pruneRetiredBoards`). `config/groveRanks` is written last: it is what
 * the client reads to decide whether to draw a percentile at all, so publishing it before the
 * boards it describes would open a window where the two disagree.
 */
export async function rebuildGroveRanks(): Promise<{ ranked: number; boards: number }> {
  const db = getFirestore();

  // Asked for together rather than one after another: they are independent reads against one
  // collection, and a scheduled job should not spend a round trip in series for each of them.
  //
  // The prune rides along here rather than after the writes because it touches a different
  // collection and can only ever remove ids `BOARD_IDS` does not name - so it cannot race the
  // batch below, whatever order the two finish in.
  const [tops, counts, sample, pruned] = await Promise.all([
    Promise.all(BOARD_IDS.map((id) => topOf(db, id))),
    Promise.all(BOARD_IDS.map((id) => countOf(db, id))),
    sampleRanks(db),
    pruneRetiredBoards(db),
  ]);

  if (pruned.length > 0) logger.info("removed retired boards", { boards: pruned });

  const population: Record<string, number> = {};
  BOARD_IDS.forEach((id, i) => { population[id] = counts[i]; });

  // Ascending in both cases, which is what `deciles` wants and what the client asserts before
  // it will believe a table at all.
  const scores = sample.scores.slice().sort((a, b) => a - b);
  const waves = sample.waves.slice().sort((a, b) => a - b);

  const builtUnix = Math.floor(Date.now() / 1000);

  const batch = db.batch();

  BOARD_IDS.forEach((boardId, i) => {
    batch.set(db.doc(GROVE_PATHS.board(boardId)), {
      entries: tops[i],
      population: population[boardId] ?? 0,
      builtUnix,
    });
  });

  await batch.commit();

  // `waveSamples` and `waveDeciles` are **additive**, and that is what makes this deployable
  // in either order: a client that has never heard of them reads the document exactly as it
  // did, and a client that has reads an absent pair as "nothing to say" and draws no standing
  // — which is also the honest answer on the day this ships, when no population of watchers
  // exists yet.
  await db.doc(GROVE_PATHS.ranksConfig).set({
    samples: scores.length,
    deciles: deciles(scores),
    waveSamples: waves.length,
    waveDeciles: deciles(waves),
    population,
    builtUnix,
    builtAt: new Date().toISOString(),
  });

  logger.info("published grove ranks", {
    ranked: population.global ?? 0,
    sampled: scores.length,
    watchers: population.endless ?? 0,
    waveSampled: waves.length,
    boards: BOARD_IDS.length,
  });

  return { ranked: population.global ?? 0, boards: BOARD_IDS.length };
}

/**
 * Removes a card, and does not mind if there was none.
 *
 * `delete` on a missing document is a success in Firestore, which is exactly the behaviour
 * wanted: a withdrawal that could fail permanently is a device retrying it for the life of
 * the account (invariant 13a). The row survives on whatever board was last built until the
 * next run, which is the one visible consequence and is worth stating — a player who opts
 * out sees themselves gone from their own profile immediately and off the lists within a
 * day.
 */
/**
 * The revision of the save a card was built from, reported back to the client.
 *
 * Every save carries `cloud.revision`, which only ever rises (`revisionAdvances` in the
 * rules). The client asks for a publish after a sync it knows the revision of, so handing
 * this back lets it prove the card was built from the save it pushed rather than the one
 * before — the failure this closes was a card one session behind its grove for the life of
 * the account, with a successful call and a well-formed card on every publish.
 *
 * Absent or malformed reads as 0. The client treats a *missing field* as "cannot be
 * checked" and a 0 as a real answer, so this must always be present on the reply.
 */
export function saveRevision(save: Record<string, unknown>): number {
  const cloud = save.cloud as Record<string, unknown> | undefined;
  if (!cloud || typeof cloud !== "object") return 0;

  const revision = Math.floor(Number(cloud.revision ?? 0));
  return Number.isFinite(revision) && revision > 0 ? revision : 0;
}

export async function withdrawCard(uid: string): Promise<void> {
  await getFirestore().doc(GROVE_PATHS.card(uid)).delete();
}

/** Marks a save as having been published, for support. Never read by any rule. */
export function publishStamp(): Record<string, unknown> {
  return { lastPublishedAt: FieldValue.serverTimestamp() };
}

/** Re-exported so `index.ts` composes the ceiling from one place. */
export { earnedCredits };
