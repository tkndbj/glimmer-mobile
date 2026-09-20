/**
 * The rank a keeper holds, derived here rather than believed.
 *
 * **Why this file exists at all.** A rank used to be a private badge on the player's own map:
 * derived on the device, stored nowhere, and worth nothing to forge because nobody else could
 * see it (invariant 52). It is on a public board now, which changes what it is — invariant 19a
 * says a number that goes public stops being derived-and-trusted and becomes adjudicated. The
 * client publishes its own reading too, but only so that reaching a rung marks the card as owing
 * a publish (`GroveCard.Fingerprint`); what a stranger actually sees is what this computes from
 * the save document the server reads for itself.
 *
 * **It pays nothing, and that bound is what makes this affordable.** A rung buys no currency, no
 * XP and no position — the endless board is still ordered on waves and the finest-groves board on
 * worth. So the worst a bug here can do is draw the wrong picture beside somebody's name, which
 * is why the ladder can be content (retunable with no deploy) rather than frozen into this code.
 * The day a rung pays anything, invariant 13 starts applying to it and this file needs a second
 * look.
 *
 * **The rule exists twice and the halves must stay identical** — invariant 9a. The C# side is
 * `RankLadder.Held` over a `SaveRankSource`; this is the same walk over the same document, and
 * the pair is held by `rankCases` in `grove-vectors.json`, written by
 * `Tools/make_rank_vectors.py` and run as a test on both sides. A silent disagreement here is a
 * keeper whose own map says Auroracrest and whose board row says Frostheart, with every gate
 * green on both sides.
 *
 * **Absent config answers nothing rather than guessing.** A server that has not been seeded with
 * a `ranks` block publishes no rung, every card reads as unranked and every row draws no badge —
 * which is exactly what a card written before this deployment says, and is the only honest answer
 * to a ladder nobody has told this server about. That is `referral`'s stance rather than
 * `endless`'s, and the difference is that a missing rung costs a picture where a missing endless
 * block would cost a keeper level (see `ProgressionConfig.endless`).
 */

import { MAX_LEVEL_ID_LENGTH } from "./progression";
import type { ProgressionConfig } from "./progression";

/** How many lines one rung may ask for. Mirrors `RankLadder.MaxRequirements`. */
export const MAX_REQUIREMENTS = 8;

/** How many rungs a ladder may hold. Mirrors `RankLadder.MaxRungs`. */
export const MAX_RUNGS = 24;

/** The most any lifetime counter may reach. Mirrors `LifetimeTally.Ceiling`. */
export const TALLY_CEILING = 999_999_999;

/** The per-level star clamp. Mirrors `MAX_STARS` in `grove.ts` and `SaveRankSource.MaxStars`. */
const MAX_STARS = 3;

/** The rows `firestore.rules` bounds `endlessBest` to. Mirrors `EndlessLedger.MaxRows`. */
const MAX_ENDLESS_ROWS = 64;

/** The wave ceiling. Mirrors `EndlessLedger.MaxWave` and `MAX_WAVE` in `grove.ts`. */
const MAX_WAVE = 9999;

/**
 * The five readings that are *not* counted verbs, as content names them. Mirrors
 * `RankMeasures`' own constants; anything else a rung names is a lifetime tally read by its own
 * id, which is what makes a future mode's verb a rank requirement with no code on either side.
 */
const LEVELS_CLEARED = "levels_cleared";
const STARS = "stars";
const THREE_STARS = "three_stars";
const KEEPER_LEVEL = "keeper_level";
const BEST_WAVE = "best_wave";

/**
 * The three counted verbs the rest of the save already proves, and what proves them. Mirrors
 * `LifetimeTally.FloorFor` exactly.
 *
 * **Without this a published rank sits below the one the player's own map draws.** The tally
 * ships as `max(counted, proved)` precisely so an account older than the feature reads correctly
 * instead of starting again from nought; a server that read only the counted half would publish
 * Cinderling for somebody the game itself calls Goldbrand, and nothing would say so.
 */
const RUNS = "runs";
const WINS = "wins";
const WAVES = "waves";

/** One line of a rung, as `progression.json` authors it and the seeder publishes it. */
export interface RankRequirementConfig {
  measure: string;
  scope?: string;
  target: number;
}

/** One rung. The id names a badge on disk and a loc key on the client, and nothing here. */
export interface RankRungConfig {
  id: string;
  requires: RankRequirementConfig[];
}

/**
 * Everything this side needs to read a save's rows without knowing what a save is.
 *
 * Narrow on purpose: the three walks below are the only things that touch the document, so a
 * change to the save's shape lands in one place rather than in six readings.
 */
interface SaveRows {
  levels: unknown;
  endlessBest: unknown;
  lifetime: unknown;
  lifetimeWaves: number;
}

/**
 * The two figures the caller has already worked out, handed in rather than worked out again.
 *
 * **Both are here to stop a third copy of a rule that already exists twice.** The keeper level
 * comes from XP this server recomputed itself, having refused the save's own claim; the lifetime
 * waves come from `endlessWaves`, whose two different clamps (a best at `MAX_WAVE`, a tally an
 * order of magnitude higher) are a trap worth owning in exactly one place.
 *
 * The client reaches the same two figures by a different route — `SaveRankSource` is handed the
 * keeper level and calls `EndlessLedger.LifetimeWavesIn` itself — and the answers agree because
 * both routes end at the pair `endlessCases` already holds together. The asymmetry is real and
 * is worth naming: a rank case may therefore never *state* a wave figure its own rows do not
 * support, and the first run of `rankCases` failed on exactly that.
 */
export interface RankReadings {
  keeperLevel: number;
  lifetimeWaves: number;
}

/**
 * The rung this save holds, or `""` for an account below the first one.
 */
export function rungOf(save: Record<string, unknown>, config: ProgressionConfig,
                       readings: RankReadings): string {
  const ladder = readLadder(config);
  if (ladder.length === 0) return "";

  const rows: SaveRows = {
    levels: save?.levels,
    endlessBest: save?.endlessBest,
    lifetime: lifetimeRows(save),
    lifetimeWaves: Math.max(0, Math.floor(Number(readings?.lifetimeWaves ?? 0)) || 0),
  };

  const raw = Number(readings?.keeperLevel ?? 1);
  const level = Number.isFinite(raw) ? Math.max(1, Math.floor(raw)) : 1;

  // **Up from the bottom, never "the highest met."** A ladder is authored by a person, and one
  // rung asking for something the rung above it does not is an ordinary slip — with "highest
  // met" that slip hands out rank six to somebody who never met rank five. Walking makes the
  // answer monotone by construction whatever content says. `RankLadder.Held` carries the same
  // argument and this is the half of it that strangers see.
  let held = "";

  for (const rung of ladder) {
    if (!isMet(rung, rows, config, level)) break;
    held = rung.id;
  }

  return held;
}

/**
 * The ladder as published, **truncated** at the first rung this cannot read.
 *
 * Three behaviours were available and only one of them is safe. Refusing the whole ladder takes
 * every badge off every board at once, over one bad row. Dropping the bad rung and carrying on
 * is worse than either: the rung *above* it then goes on being handed out, over a line nobody
 * checked — a badge given away for less than it asks for, which is the one failure in this
 * feature nobody would ever notice. Truncating keeps every keeper below the fault exactly where
 * they stand and gives nobody anything above it.
 *
 * It is also the same shape as the walk it feeds: `rungOf` stops at the first rung whose lines
 * are unmet, and this stops at the first rung whose lines are unreadable.
 *
 * Nothing here throws. A publish that failed over a badge would cost a keeper their whole card
 * — the name, the score, the board row — and `seed-config.mjs` has already refused this ladder
 * at deploy time, so anything reaching here is a shape no seeder wrote.
 */
function readLadder(config: ProgressionConfig): RankRungConfig[] {
  const raw = config?.ranks;
  if (!Array.isArray(raw)) return [];

  const rungs: RankRungConfig[] = [];
  const seen = new Set<string>();

  for (const entry of raw.slice(0, MAX_RUNGS)) {
    const rung = entry as Partial<RankRungConfig> | null;
    if (!rung || typeof rung !== "object") break;

    const id = typeof rung.id === "string" ? rung.id : "";
    if (id.length === 0 || id.length > 32 || seen.has(id)) break;
    if (!Array.isArray(rung.requires) || rung.requires.length === 0) break;

    const requires: RankRequirementConfig[] = [];
    for (const line of rung.requires.slice(0, MAX_REQUIREMENTS)) {
      const row = line as Partial<RankRequirementConfig> | null;
      if (!row || typeof row !== "object") continue;

      const measure = typeof row.measure === "string" ? row.measure : "";
      const target = Math.floor(Number(row.target ?? 0));
      if (measure.length === 0 || !Number.isFinite(target) || target < 1) continue;

      requires.push({
        measure,
        scope: typeof row.scope === "string" ? row.scope : "",
        target,
      });
    }

    // A rung whose lines could not all be read would be a rung asking for *less* than it was
    // authored to ask for, which is a badge handed out cheap. The ladder stops here instead, so
    // nothing above it is handed out either. See the function's own remarks.
    if (requires.length !== rung.requires.length) break;

    seen.add(id);
    rungs.push({ id, requires });
  }

  return rungs;
}

function isMet(rung: RankRungConfig, rows: SaveRows, config: ProgressionConfig,
               keeperLevel: number): boolean {
  for (const line of rung.requires) {
    if (read(line, rows, config, keeperLevel) < line.target) return false;
  }
  return rung.requires.length > 0;
}

/** What the save holds against one line. Never negative. */
function read(line: RankRequirementConfig, rows: SaveRows, config: ProgressionConfig,
              keeperLevel: number): number {
  const scope = line.scope ?? "";

  switch (line.measure) {
    case LEVELS_CLEARED: return walkLevels(rows.levels, config, scope, () => 1);
    case STARS: return walkLevels(rows.levels, config, scope, (stars) => stars);
    case THREE_STARS: return walkLevels(rows.levels, config, scope, (stars) => (stars >= 3 ? 1 : 0));
    case KEEPER_LEVEL: return keeperLevel;
    case BEST_WAVE: return bestWaveIn(rows.endlessBest, scope);
    default: return lifetime(line.measure, rows, config);
  }
}

/**
 * One walk over the save's level rows, with the per-row worth handed in.
 *
 * **Bounded by the shipped catalog**, which is `derivedXp`'s rule and is here for its reason: a
 * record naming a level no longer in the manifest must not count toward anything. `SaveRankSource`
 * applies the same bound against the catalog index, so the two sides agree on a real save and
 * this side can only ever read *lower* on a forged one — invariant 19a's safe direction.
 */
function walkLevels(levels: unknown, config: ProgressionConfig, scope: string,
                    worth: (stars: number) => number): number {
  if (!levels || typeof levels !== "object" || Array.isArray(levels)) return 0;

  const chapters = config?.levelChapters ?? {};
  let total = 0;

  for (const [levelId, raw] of Object.entries(levels as Record<string, unknown>)) {
    if (!levelId || levelId.length > MAX_LEVEL_ID_LENGTH) continue;

    const chapterId = chapters[levelId];
    if (chapterId === undefined) continue;
    if (scope.length > 0 && chapterId !== scope) continue;

    const entry = raw as { stars?: unknown } | null;
    const rawStars = entry && typeof entry === "object" && typeof entry.stars === "number"
      ? Math.floor(entry.stars)
      : 0;

    // `IsCleared` is `Stars > 0` on the client, which is the same test `derivedXp` already
    // makes here — a row with no stars is a glade that was opened and not finished.
    if (rawStars <= 0) continue;

    total += worth(Math.min(rawStars, MAX_STARS));
  }

  return total;
}

/**
 * The furthest wave one endless level reached, or the best of all of them when no level is
 * named. Mirrors `bestWave` in `grove.ts` and `SaveRankSource.BestWave`.
 *
 * The walk is bounded by the rules' own row cap rather than by the array's length, which is the
 * reading both other copies take — a refused row near the top would otherwise let one side reach
 * a sixty-fifth the other never sees.
 */
function bestWaveIn(rows: unknown, scope: string): number {
  if (!Array.isArray(rows)) return 0;

  let best = 0;

  for (const raw of rows.slice(0, MAX_ENDLESS_ROWS)) {
    const row = raw as { level?: unknown; wave?: unknown } | null;
    if (!row || typeof row !== "object") continue;

    const level = typeof row.level === "string" ? row.level : "";
    if (level.length === 0 || level.length > MAX_LEVEL_ID_LENGTH) continue;
    if (scope.length > 0 && level !== scope) continue;

    const wave = Math.floor(Number(row.wave ?? 0));
    if (Number.isFinite(wave) && wave > best) best = wave;
  }

  return best <= 0 ? 0 : Math.min(best, MAX_WAVE);
}

/**
 * A counted verb, for ever: the larger of what the tally counted and what the rest of the save
 * already proves. See the `RUNS`/`WINS`/`WAVES` note above for why the floor is not optional.
 */
function lifetime(goal: string, rows: SaveRows, config: ProgressionConfig): number {
  let counted = 0;

  if (Array.isArray(rows.lifetime)) {
    for (const raw of rows.lifetime) {
      const row = raw as { goal?: unknown; count?: unknown } | null;
      if (!row || typeof row !== "object") continue;
      if (typeof row.goal !== "string" || row.goal !== goal) continue;

      const count = Math.floor(Number(row.count ?? 0));
      if (!Number.isFinite(count) || count <= 0) continue;

      const value = Math.min(count, TALLY_CEILING);
      if (value > counted) counted = value;
    }
  }

  let proved = 0;
  switch (goal) {
    case RUNS:
    case WINS:
      proved = walkLevels(rows.levels, config, "", () => 1);
      break;

    case WAVES:
      proved = rows.lifetimeWaves;
      break;

    default:
      break;
  }

  return counted > proved ? counted : proved;
}

/** The lifetime rows, which ride inside the `tasks` map (invariant 12a). */
function lifetimeRows(save: Record<string, unknown>): unknown {
  const tasks = save?.tasks;
  if (!tasks || typeof tasks !== "object" || Array.isArray(tasks)) return null;

  return (tasks as Record<string, unknown>).lifetime;
}
