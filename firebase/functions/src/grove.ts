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

import { getFirestore, FieldValue } from "firebase-admin/firestore";
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

  /** The star ladder, ascending. Doubles as the league boundaries — see `leagueOf`. */
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
 * The league id for a star count. Mirrors `GroveLeague.IdFor`.
 *
 * A league *is* the star rating the player already wears, so there is no second ladder to
 * tune and no second thing to explain — see `GroveLeague` for the argument.
 */
export function leagueOf(stars: number): string {
  const clamped = stars < 0 ? 0 : stars > 8 ? 8 : Math.floor(stars);
  return `l${clamped}`;
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
      bought += cost;
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
  for (const [id, entry] of Object.entries(grove.companions)) {
    if (!entry || typeof entry.cost !== "number" || entry.cost <= 0) continue;
    if (!companions.has(id)) continue;

    if (level >= Math.floor(entry.level ?? 0)) bought += entry.cost;
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

/** A placement as it appears on a card: a bare id, or a map when the piece is mirrored. */
type CardPlacement = string | { piece: string; flip: number };

export interface GroveCardDoc {
  name: string;
  avatar: string;
  level: number;
  score: number;
  stars: number;
  league: string;
  dwelling: string;
  land: string[];
  placed: Record<string, CardPlacement>;
  builtUnix: number;

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
  list?: PreparedBlocklist
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

    const tier = Math.floor(rawTier ?? 0);
    if (tier > dwellingTier) {
      dwelling = id;
      dwellingTier = tier;
    }
  }

  const placed: Record<string, CardPlacement> = {};
  const rows = Array.isArray(save.homesteadPlaced) ? save.homesteadPlaced : [];

  for (const raw of rows) {
    const row = raw as { slot?: unknown; piece?: unknown; flipped?: unknown } | null;
    if (!row || typeof row !== "object") continue;

    const slot = typeof row.slot === "string" ? row.slot : "";
    const piece = typeof row.piece === "string" ? row.piece : "";

    // An emptied slot is a real instruction in the save (invariant 16) and nothing at all
    // on a card: a visitor cannot tell "never touched" from "cleared", and does not need to.
    if (slot.length === 0 || slot.length > 32 || piece.length === 0 || piece.length > 64) continue;

    placed[slot] = row.flipped === true ? { piece, flip: 1 } : piece;

    if (Object.keys(placed).length >= 1024) break;
  }

  return {
    name: boardName(confirmedName, uid, list),
    avatar: typeof wallet.avatarId === "string" ? wallet.avatarId.slice(0, 64) : "",
    level,
    score: worth.score,
    stars: worth.stars,
    league: leagueOf(worth.stars),
    dwelling,
    land,
    placed,
    builtUnix: nowUnix,
    catalogVersion: Math.floor(grove.version ?? 0),
  };
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
  league: string;
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
 * Turns a sample of published cards into the boards and the distribution.
 *
 * <b>Sampled rather than exhaustive, and read from the cards rather than from the saves.</b>
 * Cards are the small documents — a couple of kilobytes against a full save ledger — and
 * only players who asked to be ranked have one, so the sample is already the population the
 * boards are about. Reading saves instead would be the same job over documents ten times
 * the size, most of which are not on the board at all.
 *
 * The global board is exact for the top of the sample and the sample is bounded, so with
 * more than `RANK_SAMPLE_SIZE` participants the global hundred becomes the best hundred
 * *seen*, not the best hundred alive. That is a deliberate trade and the reason the screen
 * leads with a percentile: a percentile from a bounded sample is accurate to well under the
 * point it is rounded to, whereas an exact global top hundred needs an ordering nothing
 * here maintains. When that day comes, the fix is a scored index and a query — and it is a
 * change to this function alone.
 */
export function summarise(sample: RankedGrove[]): {
  boards: Record<string, RankedGrove[]>;
  distribution: { samples: number; deciles: number[] };
  population: Record<string, number>;
} {
  const scored = sample.filter((g) => g.score > 0);
  scored.sort((a, b) => (b.score - a.score) || a.uid.localeCompare(b.uid));

  const boards: Record<string, RankedGrove[]> = { global: scored.slice(0, BOARD_ROWS) };
  const population: Record<string, number> = {};

  for (const grove of scored) {
    population[grove.league] = (population[grove.league] ?? 0) + 1;

    const board = boards[grove.league] ?? (boards[grove.league] = []);
    if (board.length < BOARD_ROWS) board.push(grove);
  }

  const ascending = scored.map((g) => g.score).sort((a, b) => a - b);

  return {
    boards,
    distribution: { samples: ascending.length, deciles: deciles(ascending) },
    population,
  };
}

/**
 * Reads a bounded sample of published cards and rewrites the boards.
 *
 * Every board is written whether or not anything is on it, so a league that emptied stops
 * showing yesterday's rows rather than keeping them for ever. `config/groveRanks` is
 * written last: it is what the client reads to decide whether to draw a percentile at all,
 * so publishing it before the boards it describes would open a window where the two
 * disagree.
 */
export async function rebuildGroveRanks(): Promise<{ ranked: number; boards: number }> {
  const db = getFirestore();

  const snapshot = await db.collection("groves").limit(RANK_SAMPLE_SIZE).get();

  const sample: RankedGrove[] = [];
  for (const doc of snapshot.docs) {
    const data = doc.data() as Partial<GroveCardDoc>;
    const score = typeof data.score === "number" ? Math.floor(data.score) : 0;
    if (score <= 0) continue;

    const stars = typeof data.stars === "number" ? Math.floor(data.stars) : 0;

    sample.push({
      uid: doc.id,
      name: typeof data.name === "string" ? data.name : "",
      avatar: typeof data.avatar === "string" ? data.avatar : "",
      level: typeof data.level === "number" ? Math.floor(data.level) : 1,
      score,
      stars,
      league: leagueOf(stars),
    });
  }

  const { boards, distribution, population } = summarise(sample);
  const builtUnix = Math.floor(Date.now() / 1000);

  // Every league gets a document, including the empty ones — see the remarks above.
  const ids = ["global", ...Array.from({ length: 9 }, (_, i) => `l${i}`)];
  const batch = db.batch();

  for (const boardId of ids) {
    const entries = boards[boardId] ?? [];
    batch.set(db.doc(GROVE_PATHS.board(boardId)), {
      entries,
      population: boardId === "global" ? distribution.samples : (population[boardId] ?? 0),
      builtUnix,
    });
  }

  await batch.commit();

  await db.doc(GROVE_PATHS.ranksConfig).set({
    samples: distribution.samples,
    deciles: distribution.deciles,
    population,
    builtUnix,
    builtAt: new Date().toISOString(),
  });

  logger.info("published grove ranks", {
    ranked: distribution.samples, read: snapshot.size, boards: ids.length,
  });

  return { ranked: distribution.samples, boards: ids.length };
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
