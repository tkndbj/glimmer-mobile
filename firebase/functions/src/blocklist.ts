import { Firestore } from "firebase-admin/firestore";
import { logger } from "firebase-functions";

import { NameBlocklist, PreparedBlocklist, prepareBlocklist } from "./profanity";
import builtIn from "./name-blocklist.json";

/**
 * Where the word list comes from, and how fast a change to it reaches players.
 *
 * ## Why the list is a document rather than a constant
 *
 * A blocklist in source is a blocklist that needs a deploy, and the moment it needs a deploy
 * is the moment somebody has already put the word on a leaderboard. `config/names` makes
 * adding a word an edit in a console that reaches every instance inside
 * {@link CACHE_TTL_SECONDS}. That is the same trade every other tunable in this game makes
 * (`config/progression` for the heart gate, the chest odds and the ad payouts), for the same
 * reason and with one difference worth stating: those move a *deal*, and this moves a
 * *refusal*, so the failure directions are opposite. A bad push to the reward table costs
 * money; a bad push here costs somebody their name. Hence the floor below.
 *
 * ## The floor, and why the shipped file is not merely a fallback
 *
 * `name-blocklist.json` is compiled into the deployment and is what runs when `config/names`
 * is absent — a fresh project, a failed seed, a Firestore outage. That is not a nicety: a
 * filter that fails *open* looks exactly like a filter that is working, and nobody discovers
 * it until a screenshot is on social media. Reading the list is on the claim path, so an
 * unavailable Firestore has to answer something, and the only safe something is the list we
 * shipped.
 *
 * The published document **overrides** rather than merges, deliberately. A union would make
 * the config doc add-only, which is the safe direction for adding a slur and the wrong
 * direction for the thing that actually happens more often — discovering that an entry
 * refuses an innocent name and needing it gone today. Both directions matter, so the
 * document wins outright and the file is what runs when there is no document.
 *
 * ## What the cache costs
 *
 * One document read per instance per {@link CACHE_TTL_SECONDS}. At a handful of warm
 * instances that is a few hundred reads a day and rounds to nothing on the bill; the
 * alternative — reading it per claim — would be one read per rename, which is also nearly
 * nothing, but it would be a read on the latency path of the one call a player waits on.
 *
 * Ten minutes rather than ten seconds because **the list is not the takedown path**. A
 * specific offensive name is removed by the `denied` flag on its own reservation, which is
 * read inside the claim transaction and is therefore instant. The list only decides how fast
 * a newly-banned *word* starts refusing *future* names, and ten minutes is not a meaningful
 * delay for that.
 */

/** How long a loaded list is reused before the document is read again. */
export const CACHE_TTL_SECONDS = 600;

/** Where the published list lives. */
export const NAMES_CONFIG_PATH = "config/names";

/**
 * How many distinct players must report one name before it comes off the boards on its own.
 *
 * **Published beside the list, because the right number is not knowable before launch.** Too
 * low and a small group can take an innocent name down; too high and an offensive one stands
 * for as long as it takes a human to look. It rides `config/names` rather than
 * `progression.json` for the reason the cooldown in `names.ts` does not ride either — it is
 * an abuse bound rather than a product knob — but unlike that one it has no attacker-facing
 * downside to being moved, because raising it cannot unblock anything and lowering it cannot
 * pay anybody. What it can do is need changing at three in the morning.
 *
 * The auto-hide is deliberately *soft*: the name reverts to a generated handle and nothing
 * else about the account is touched. So the worst a brigade achieves is a duller row, which
 * is why three is a defensible starting point rather than a number that needs a moderator
 * behind it.
 */
export const DEFAULT_REPORT_THRESHOLD = 3;

/** The list, plus the one number that is published with it. */
export interface NameConfig {
  list: PreparedBlocklist;
  reportThreshold: number;
}

/**
 * The list compiled into this deployment.
 *
 * Prepared once at module load rather than per call: folding a few thousand entries is a few
 * milliseconds, which is nothing once and would be silly on every rename.
 */
const SHIPPED = builtIn as NameBlocklist;
const FLOOR: PreparedBlocklist = prepareBlocklist(SHIPPED);

let cached: NameConfig = { list: FLOOR, reportThreshold: DEFAULT_REPORT_THRESHOLD };
let cachedAtUnix = 0;
let cachedVersion = -1;

/**
 * Reads the published list, or falls back to the compiled one.
 *
 * <b>Never throws and never rejects a name because the database was unreachable.</b> A read
 * that fails leaves whatever is cached in place — the compiled floor on the first call, the
 * last good document afterwards — and logs once. The alternative is a claim path whose
 * outcome depends on Firestore's availability, which would turn a transient outage into
 * players being told their names are unacceptable.
 */
export async function loadNameConfig(db: Firestore, nowUnix: number): Promise<NameConfig> {
  if (nowUnix - cachedAtUnix < CACHE_TTL_SECONDS && cachedVersion >= 0) return cached;

  try {
    const snapshot = await db.doc(NAMES_CONFIG_PATH).get();
    const data = snapshot.exists ? (snapshot.data() as NameBlocklist | undefined) : undefined;

    if (usable(data)) {
      const list = prepareBlocklist(data);
      cached = { list, reportThreshold: threshold(data) };
      cachedVersion = list.version;
    } else {
      // Absent is the ordinary state of a project that has not been seeded yet, so it is
      // logged at info rather than as an error — but it is logged, because a deployment
      // running on the floor for months is a thing somebody should be able to notice.
      cached = { list: FLOOR, reportThreshold: DEFAULT_REPORT_THRESHOLD };
      cachedVersion = FLOOR.version;
      logger.info("config/names is absent or unusable; running the compiled word list", {
        version: FLOOR.version,
      });
    }

    cachedAtUnix = nowUnix;
  } catch (error) {
    // Keep serving whatever is in hand and try again on the next expiry. Deliberately not
    // rethrown: a name claim must not fail because a config read did.
    logger.error("could not read config/names; keeping the list already loaded", {
      version: cachedVersion,
      error: error instanceof Error ? error.message : String(error),
    });
  }

  return cached;
}

/**
 * Whether a published document is fit to replace the floor.
 *
 * The shape is checked rather than trusted, because the one failure this has to prevent is a
 * malformed push emptying the list — which reads, from every side, as the filter simply
 * letting everything through. An `anywhere` array that arrives as `undefined` would prepare
 * cleanly into an empty list and refuse nothing at all, silently, for as long as it took
 * somebody to notice.
 */
function usable(data: NameBlocklist | undefined): data is NameBlocklist {
  if (!data || typeof data !== "object") return false;

  if (!Array.isArray(data.anywhere) || !Array.isArray(data.exact)) return false;
  if (!Array.isArray(data.reserved) || !Array.isArray(data.allow)) return false;

  // A published list *dramatically* smaller than the one we shipped is the shape of a
  // truncated or partial write, and adopting it would quietly weaken the filter to nothing.
  //
  // **A proportion rather than "at least as big", and the difference is the whole point of
  // this document existing.** The commonest real edit here is not adding a slur — that can
  // wait for the next seed — it is *removing* an entry that turned out to refuse an innocent
  // name, today, because somebody is currently unable to be called what they are called. A
  // floor of "no smaller than what shipped" would refuse exactly that edit, which is the one
  // this whole mechanism is for. Half is far below any honest edit and far above a truncation.
  //
  // Compared against the raw arrays rather than the prepared sets, which hold several folded
  // forms per word and would make this a comparison between two different units.
  const enough = (published: number, shipped: number) => published * 2 >= shipped;

  return enough(data.anywhere.length, SHIPPED.anywhere.length)
    && enough(data.reserved.length, SHIPPED.reserved.length)
    && enough(data.exact.length, SHIPPED.exact.length);
}

/**
 * The threshold as published, bounded.
 *
 * Clamped rather than trusted, and the lower bound is the one that matters: a published zero
 * or one would hide a name on the first tap of a button any player can reach, which turns
 * reporting from a safety feature into a weapon. The upper bound stops a typo — a stray extra
 * digit — from switching the whole mechanism off silently.
 */
function threshold(data: NameBlocklist & { reportThreshold?: unknown }): number {
  const raw = Math.floor(Number(data.reportThreshold ?? DEFAULT_REPORT_THRESHOLD));
  if (!Number.isFinite(raw)) return DEFAULT_REPORT_THRESHOLD;

  return Math.min(100, Math.max(2, raw));
}

/** The compiled list, for callers with no database in hand — the tests, mostly. */
export function builtInBlocklist(): PreparedBlocklist {
  return FLOOR;
}

/** Drops the cache. Tests only; nothing in the deployment needs it. */
export function resetBlocklistCache(): void {
  cached = { list: FLOOR, reportThreshold: DEFAULT_REPORT_THRESHOLD };
  cachedAtUnix = 0;
  cachedVersion = -1;
}
