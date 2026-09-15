/**
 * The server's copy of the season rules: what a rung's chest pays, and who may claim one.
 *
 * A rung's chest is a `daily.ts` chest seeded from a *subject* rather than a day: the
 * account, the tag `mark`, and `{seasonId}:{track}:{goal}`. The client rolls it to show
 * and spend the reward offline; this re-rolls it from the claim id to decide what to
 * grant, and the client's number is a prediction (invariant 10a). Both halves are pinned
 * by `firebase/shared/reward-vectors.json`.
 *
 * **What bounds a claim is the ladder itself.** The server cannot recompute whether the
 * marks were grown — they come from chests claimed against counters that live in the
 * player's own save, which is forgeable — so a season claim is *bounded* rather than
 * proved, the way a streak night and a task chest are. The bound here is tighter than
 * either, and needs no allowance map to enforce it: a rung is one document in the grant
 * log, keyed by `mark:{season}:{track}:{goal}:{currency}`, so the most a forged save can
 * ever extract is **one season's authored ladder, once**. That is exactly what an honest
 * player who finishes the track gets.
 *
 * **The pass track needs a receipt on top of that.** Its entitlement is written by
 * `redeemPurchase` when a receipt has been verified with the store, never by a client, and
 * it is read here under this server's own credentials — so the paid column is not merely
 * bounded, it is gated on money that actually moved (invariant 18a).
 */

import { logger } from "firebase-functions";
import { subjectSeed, Rolls } from "./random";
import { ChestConfig, RolledDrop, rollChestWith } from "./daily";
import { EventConfig, EventMilestone } from "./progression";
import { findTier, TaskConfig } from "./tasks";

/** Matches `SeasonLedger.SeedTag`. Contract. */
export const MARK_SEED_TAG = "mark";

/** Matches `SeasonTracks.Id`. Contract: these strings are in the seed and in the claim id. */
export const SEASON_TRACKS = ["free", "pass"] as const;
export type SeasonTrack = (typeof SEASON_TRACKS)[number];

/** Matches `CatalogIndexBuilder.IsCleanId` on the client. */
export const SEASON_ID = /^[a-z0-9_]{1,64}$/;

/** Matches `EventRules.MaxMilestones` and `EventRules.MaxGoal`. */
export const MAX_SEASON_RUNGS = 40;
export const MAX_SEASON_GOAL = 100000;

/**
 * Matches `SeasonCycle.IndexDigits` and `SeasonCycle.MaxIndex`. **Contract.**
 *
 * A repeating season mints its ids from the clock rather than from a file, so these two
 * numbers are the only thing that makes an id written by a phone parseable by this server.
 * Widening them later would orphan every id already in a save row and every key already in
 * a grant log (invariant 1), which is why they are stated here rather than inferred from
 * whatever the id happens to look like.
 */
export const SEASON_INDEX_DIGITS = 4;
export const MAX_SEASON_INDEX = 9999;

/**
 * The cycle number an id names against a repeating season, or -1.
 *
 * Deliberately strict, and identical to `SeasonCycle.IndexOf`: exact stem, exactly
 * `SEASON_INDEX_DIGITS` digits, no other spelling accepted. A looser parse would read
 * `watch_7` and `watch_0007` as one season on one side and two on the other, which is two
 * sets of grant-log keys for one ladder — the bound in this file's header would then be
 * "one season's authored ladder, once, per spelling".
 */
export function seasonCycleIndex(baseId: string, seasonId: string): number {
  if (!baseId || !seasonId) return -1;
  if (seasonId.length !== baseId.length + 1 + SEASON_INDEX_DIGITS) return -1;
  if (!seasonId.startsWith(baseId)) return -1;
  if (seasonId[baseId.length] !== "_") return -1;

  const digits = seasonId.slice(baseId.length + 1);
  if (!/^[0-9]+$/.test(digits)) return -1;

  const index = Number(digits);
  return Number.isSafeInteger(index) && index >= 0 && index <= MAX_SEASON_INDEX ? index : -1;
}

export function isSeasonTrack(value: unknown): value is SeasonTrack {
  return typeof value === "string" && (SEASON_TRACKS as readonly string[]).includes(value);
}

// --------------------------------------------------------------------------- the config
/**
 * The cycle a repeating season's published entry would have minted under this id, or null.
 *
 * **This is the whole of what keeps invariant 47c true once the calendar stops ending.** The
 * bound was "the most a forged save can extract is one season's authored ladder, once",
 * which rested on the ladder being a finite list; a recurrence has no list, so a save that
 * simply invented `watch_9999` could otherwise claim ten thousand ladders. The replacement
 * is a clock: **a cycle that has not opened yet does not exist**, so the most any save can
 * extract is one ladder per elapsed period — which is exactly what an honest player who
 * finishes every season gets, and is the same sentence as before with the list swapped for
 * the calendar.
 *
 * A *future* cycle answers null, which `judgeMarkClaim` turns into `unknown` rather than
 * `refuse`. That is deliberate and it is invariant 13a: "this season has not started" stops
 * being true the moment it does, so refusing it would throw away a claim that a clock skew
 * of a few seconds either side of a rollover makes perfectly honest. Left unconfirmed, it
 * pays itself the next time the client asks.
 *
 * A *past* cycle is resolved without complaint, because a season's chests never expire.
 */
function cycleSeason(config: { events?: EventConfig[] } | undefined,
                     seasonId: string,
                     nowUnix: number): EventConfig | null {
  const cycles = config?.events?.filter((entry) => entry?.repeats) ?? [];

  for (const cycle of cycles) {
    if (!cycle.id || !Number.isSafeInteger(cycle.startUnix) || !Number.isSafeInteger(cycle.endUnix)) {
      continue;
    }

    const period = cycle.endUnix - cycle.startUnix;
    if (period <= 0) continue;

    const index = seasonCycleIndex(cycle.id, seasonId);
    if (index < 0) continue;

    const startUnix = cycle.startUnix + index * period;

    // Not yet open. Null rather than a refusal — see this function's note.
    if (!Number.isSafeInteger(startUnix) || !Number.isSafeInteger(nowUnix) || nowUnix < startUnix) {
      return null;
    }

    return { ...cycle, id: seasonId, startUnix, endUnix: startUnix + period };
  }

  return null;
}

/**
 * Guards a season the seeder published, or one it published badly.
 *
 * Returns null rather than throwing: a claim against a season this server cannot price is
 * left *unconfirmed* rather than refused, because it is either a content pack the client
 * fetched before the seeder ran or a forged id — and the first must not be thrown away for
 * the sake of the second, which pays nothing either way (invariant 13a).
 */
export function usableSeason(config: { events?: EventConfig[] } | undefined,
                             seasonId: string,
                             nowUnix: number): EventConfig | null {
  const authored = config?.events?.find((entry) => entry?.id === seasonId && !entry.repeats);
  const season = authored ?? cycleSeason(config, seasonId, nowUnix);
  if (!season) return null;

  if (!SEASON_ID.test(season.id)) return null;
  if (!Number.isSafeInteger(season.startUnix) || !Number.isSafeInteger(season.endUnix)) return null;
  if (!(season.endUnix > season.startUnix)) return null;
  if (!Array.isArray(season.milestones)) return null;
  if (season.milestones.length < 1 || season.milestones.length > MAX_SEASON_RUNGS) return null;

  let previous = 0;
  for (const rung of season.milestones) {
    if (!rung || !Number.isSafeInteger(rung.goal)) return null;
    if (rung.goal <= previous || rung.goal > MAX_SEASON_GOAL) return null;
    if (typeof rung.tier !== "string" || !SEASON_ID.test(rung.tier)) return null;
    if (rung.premiumTier !== undefined && rung.premiumTier !== "" &&
        (typeof rung.premiumTier !== "string" || !SEASON_ID.test(rung.premiumTier))) return null;
    previous = rung.goal;
  }

  return season;
}

/** The rung asking for exactly this many marks, or null. Keyed on the goal — see the client. */
/**
 * What a season's pass costs in gems, or 0 when it sells none.
 *
 * <p>The price is <b>published content</b> and the server reads it for one reason: a spend
 * is an amount the client chooses, so without a price to check against, `pass:{season}`
 * would buy the paid column for one gem. See `submitSpends`.</p>
 */
export function passPrice(season: EventConfig): number {
  const gems = season.passGems;
  if (typeof gems !== "number" || !Number.isSafeInteger(gems) || gems <= 0) return 0;
  return gems > MAX_PASS_GEMS ? 0 : gems;
}

/** Matches `EventRules.MaxPassGems`. */
export const MAX_PASS_GEMS = 100000;

/** A season pass debit, as `SpendEntry.SeasonPassId` writes it: `pass:{seasonId}`. */
export function isPassSpendId(id: string): boolean {
  return typeof id === "string" && id.startsWith("pass:");
}

/**
 * The season a pass debit names, or null.
 *
 * Parsed rather than trusted from a payload, for `parseMarkClaim`'s reason: the id is what
 * the database keys on, and the canonical form has to round-trip or two ids could name one
 * purchase.
 */
export function parsePassSpendId(id: string): string | null {
  if (!isPassSpendId(id) || id.length > 64) return null;

  const seasonId = id.slice("pass:".length);
  if (!SEASON_ID.test(seasonId)) return null;
  if (`pass:${seasonId}` !== id) return null;

  return seasonId;
}

export function findRung(season: EventConfig, goal: number): EventMilestone | null {
  return season.milestones.find((rung) => rung?.goal === goal) ?? null;
}

/** Which tier a rung pays on a track, or "" when that track pays nothing there. */
export function tierIdOn(rung: EventMilestone, track: SeasonTrack): string {
  const id = track === "pass" ? rung.premiumTier : rung.tier;
  return typeof id === "string" ? id : "";
}

// --------------------------------------------------------------------------- the roll
/** The subject half of the seed and of the claim id. Mirrors `SeasonLedger.Subject`. */
export function markSubject(seasonId: string, track: SeasonTrack, goal: number): string {
  return `${seasonId}:${track}:${goal}`;
}

class BloomRandom extends Rolls {
  constructor(playerKey: string, subject: string, stream: number) {
    super(subjectSeed(playerKey, MARK_SEED_TAG, subject, stream));
  }
}

/** Everything in one rung's chest, for one account. */
export function rollMarkChest(
  chest: ChestConfig,
  playerKey: string,
  seasonId: string,
  track: SeasonTrack,
  goal: number
): RolledDrop[] {
  const subject = markSubject(seasonId, track, goal);
  return rollChestWith(chest, (stream) => new BloomRandom(playerKey, subject, stream));
}

/** What one rung's chest is worth in one currency. Zero when it holds none of it. */
export function markCurrencyValue(
  chest: ChestConfig,
  playerKey: string,
  seasonId: string,
  track: SeasonTrack,
  goal: number,
  currency: string
): number {
  let total = 0;
  for (const drop of rollMarkChest(chest, playerKey, seasonId, track, goal)) {
    if (drop.kind === currency) total += drop.amount;
  }
  return total;
}

// -------------------------------------------------------------------- claim parsing
export interface MarkClaim {
  seasonId: string;
  track: SeasonTrack;
  goal: number;
  currency: string;

  /**
   * The claim's day, for the shared oldest-first sort in `claimAwards`.
   *
   * Zero, deliberately: a season rung is order-independent — nothing about paying one
   * changes what the next is worth — so it has no day of its own to sort by and sorting it
   * to the front of a mixed batch costs nothing. Streak nights, which *are* order
   * dependent, carry real day keys and still sort correctly among themselves.
   */
  dayKey: number;
}

export function isMarkGrantId(id: string): boolean {
  return typeof id === "string" && id.startsWith("mark:");
}

/**
 * Reads a grant id back into what it claims:
 * `mark:{seasonId}:{track}:{goal}:{currency}`, produced by `GrantEntry.MarkChestId` on
 * the client. Parsed rather than trusted from a payload, for `parseDailyClaim`'s reason:
 * the id is what the database keys on.
 */
export function parseMarkClaim(id: string): MarkClaim | null {
  if (typeof id !== "string" || id.length > 64) return null;

  const parts = id.split(":");
  if (parts.length !== 5 || parts[0] !== "mark") return null;

  const seasonId = parts[1];
  const track = parts[2];
  const goal = Number(parts[3]);
  const currency = parts[4];

  if (!SEASON_ID.test(seasonId)) return null;
  if (!isSeasonTrack(track)) return null;
  if (!Number.isInteger(goal) || goal < 1 || goal > MAX_SEASON_GOAL) return null;
  if (!currency || currency.length > 24) return null;

  // Canonical form has to round-trip, or two ids could name one chest and pay twice.
  if (`mark:${seasonId}:${track}:${goal}:${currency}` !== id) return null;

  return { seasonId, track, goal, currency, dayKey: 0 };
}

// ---------------------------------------------------------------------- the decision
export type MarkVerdict =
  | { kind: "pay"; chest: ChestConfig; tierId: string }

  /** Refused for good: nothing about this claim can become payable later. */
  | { kind: "refuse"; why: string }

  /** Left alone: this server cannot price it *yet*. The client keeps trying. */
  | { kind: "unknown"; why: string };

/**
 * Whether a season claim may be paid, and what it pays.
 *
 * <p>The split between `refuse` and `unknown` is the whole of invariant 13a applied here.
 * A rung the published ladder does not hold, or a tier the published tasks block does not
 * define, is `unknown` — a content push may still be on its way, and the client drops a
 * refused claim permanently (45d). A claim on the *paid* track without the entitlement is
 * `refuse`, because no content push will ever make an unpaid account paid.</p>
 */
export function judgeMarkClaim(
  claim: MarkClaim,
  config: { events?: EventConfig[] } | undefined,
  tasks: TaskConfig | null,
  ownsPass: boolean,
  nowUnix: number
): MarkVerdict {
  const season = usableSeason(config, claim.seasonId, nowUnix);
  if (!season) {
    // Covers three cases that all deserve the same answer: a season this deployment has not
    // been seeded with, a cycle of a repeating season that has not opened yet, and an id
    // nobody could have earned. `unknown` rather than `refuse` because the first two stop
    // being true on their own (invariant 13a) and the third pays nothing either way.
    return { kind: "unknown", why: "config/progression holds no usable season by that id" };
  }

  const rung = findRung(season, claim.goal);
  if (!rung) return { kind: "unknown", why: "the published ladder has no rung at that goal" };

  const tierId = tierIdOn(rung, claim.track);
  if (!tierId) return { kind: "refuse", why: "that rung pays nothing on that track" };

  if (claim.track === "pass") {
    if (!passPrice(season)) return { kind: "refuse", why: "the season sells no pass" };
    if (!ownsPass) return { kind: "refuse", why: "the pass has not been bought" };
  }

  if (!tasks) return { kind: "unknown", why: "config/progression holds no usable tier table" };

  const tier = findTier(tasks, tierId);
  if (!tier) return { kind: "unknown", why: "the published tier table does not hold that tier" };

  return { kind: "pay", chest: tier.chest, tierId };
}

/**
 * A claim whose rung falls outside the season's own window.
 *
 * Logged and paid, never refused. A season's chests do not expire — the marks stop
 * growing at the deadline but a rung already reached stays claimable, which is what keeps
 * a closed season's box on the hub — and a device that was offline over the deadline would
 * otherwise lose what it earned. The bound is the ladder, not the calendar.
 */
export function noteLateClaim(uid: string, season: EventConfig, claim: MarkClaim,
                              nowMillis: number): void {
  if (Math.floor(nowMillis / 1000) < season.endUnix) return;

  logger.info("a season chest was claimed after its window closed", {
    uid, season: claim.seasonId, track: claim.track, goal: claim.goal, endUnix: season.endUnix,
  });
}
