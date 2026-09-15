/**
 * The server's copy of the streak ladder, and the rule that decides what a night is worth.
 *
 * A deliberate second implementation of the client's `StreakTable` and part of
 * `DailyStreak`, for the reason `daily.ts` and `progression.ts` give: currency that was
 * *given* rather than earned is the one thing a client must never decide for itself.
 *
 * The streak is the hardest of the three to adjudicate and it is worth being precise about
 * why. A chest is recomputable from (account, day, index) and an ad is vouched for by the
 * network's own callback, but nothing about "this player has finished a glade seven days
 * running" is derivable from anything the server observes — it happens on the phone, often
 * offline, and the only record is a save file the player can edit. For a long time that was
 * taken as proof that a streak could not pay currency at all.
 *
 * What that argument missed is that the server does not need to know the streak. It needs
 * to know that a claim is not *better* than an honest one, and that is a fact about
 * arithmetic rather than about gameplay. Three things establish it:
 *
 *   1. A night is claimed once. The id carries the calendar day, and `grantLog/{id}`
 *      refuses a second grant for it, on any device, after any reinstall.
 *   2. There is one night per calendar day. So the *rate* is bounded by the calendar,
 *      which no save file can edit.
 *   3. A night number may only climb as fast as the calendar climbs — see `advances`.
 *      That is the whole trick, and it lives in this file. The server remembers the day
 *      and the night it last paid; a continuing streak must advance the night by exactly
 *      the days elapsed, and a restarted one may claim no more nights than have elapsed.
 *      A save edited to say "night seven" every morning satisfies neither.
 *
 * Together those reduce a forged streak to an honest one, and note what is *not* in the
 * list: the player's save. `startDay` and `lastPlayedDay` are read, compared and logged —
 * see `saveSupports` — but nothing is ever refused on them, because a claim they support
 * has proved nothing and a claim they contradict is usually a device that has not finished
 * syncing. The rule stands on its own.
 *
 * Everything here is integer arithmetic over day keys (days since the epoch, UTC), which
 * matches `DailyRules.DayKeyFor` on the client.
 */

import { subjectSeed, Rolls } from "./random";
import { ChestConfig, RolledDrop, rollChestWith } from "./daily";
import { findTier, TaskConfig } from "./tasks";

/**
 * Mirrors `ChestDropKinds`. Never renamed or reused — a published ladder names these.
 *
 * `hearts` and `heart_boost` are **retired on the client** (`StreakRules.IsRetiredKind`):
 * a night pays credits, gems or a chest now, and everything a chest can hold reaches this
 * ladder through the tier. They stay in this list because it is a *wire* vocabulary — a
 * rolled-back client can still publish a ladder naming one, and `usableStreakConfig`
 * answering null for that ladder would leave every night of every account **unconfirmed**
 * rather than degrading. Neither ever paid currency, so neither has ever moved a balance
 * here.
 */
export const STREAK_KINDS = ["credits", "gems", "hearts", "heart_boost"] as const;
export type StreakKind = (typeof STREAK_KINDS)[number];

/**
 * One night of the ladder: a figure, or a chest.
 *
 * A rung carries `kind`+`amount` **or** `tier`, never both — mirrors `StreakRung` on the
 * client. A chest night names a tier out of the `tasks` block's chest ladder, so it is
 * *recomputed* (like a task's chest) on top of being *bounded* (like a night), and the
 * claim id never had to learn anything: `streak:{day}:{night}:{currency}` already carries
 * everything either shape needs, because the rung decides which shape it is.
 */
export interface StreakRung {
  kind: StreakKind | "";
  amount: number;
  tier?: string;
}

export interface StreakConfig {
  rungs: StreakRung[];
}

/**
 * Mirrors `StreakRules`. Both halves clamp to the same figures, because a client that
 * showed one number and a server that paid another would be a support case per player.
 */
export const MAX_RUNGS = 30;
export const MAX_RUNG_AMOUNT = 72;
export const MAX_CREDITS_PER_RUNG = 2000;
export const MAX_GEMS_PER_RUNG = 100;

export function maxFor(kind: string): number {
  if (kind === "credits") return MAX_CREDITS_PER_RUNG;
  if (kind === "gems") return MAX_GEMS_PER_RUNG;
  return MAX_RUNG_AMOUNT;
}

/**
 * How far ahead of the server's own day a streak claim may be dated, and how far behind.
 *
 * The near bound tolerates a device whose clock is a little fast crossing midnight. The
 * far bound is generous on purpose — a player can be offline for months while still
 * finishing a glade a day, and every one of those nights is genuinely theirs — because the
 * bound that actually matters is not this one. `advances` already stops an old day being
 * used to inflate a night, so this exists only to keep the grant log from being usable as
 * unbounded storage.
 */
export const MAX_STREAK_DAYS_AHEAD = 1;
export const MAX_STREAK_DAYS_BEHIND = 400;

/** Keeps a claimed night's id bounded without capping how long a streak may run. */
const MAX_NIGHT = 100000;

/** Matches `TaskDefinition.MaxIdLength`, which is what a chest tier id is. */
const MAX_TIER_ID = 32;

/** Matches `DailyStreak.SeedTag`. Contract (invariant 9c). */
export const STREAK_SEED_TAG = "streak";

// ------------------------------------------------------------------------ the ladder
/**
 * What the `night`th night pays, counting from 1.
 *
 * Wraps: night eight of a seven-night ladder pays night one's rung. Mirrors
 * `StreakTable.Rung`, and the wrap is the contract — a server that repeated the last rung
 * instead would pay a different reward from the one the board drew, every lap, for ever.
 */
export function rungFor(config: StreakConfig, night: number): StreakRung {
  const nothing: StreakRung = { kind: "", amount: 0 };

  if (!Number.isInteger(night) || night < 1) return nothing;

  const rungs = config.rungs;
  if (!Array.isArray(rungs) || rungs.length === 0) return nothing;

  const rung = rungs[(night - 1) % rungs.length];
  if (!rung) return nothing;

  // A chest night. The tier is resolved against the published task tiers by the caller,
  // because the two blocks are seeded together and a tier this ladder names but that block
  // does not hold is a claim to leave *unconfirmed* rather than refuse (invariant 13a).
  if (typeof rung.tier === "string" && rung.tier.length > 0) {
    return { kind: "", amount: 0, tier: rung.tier };
  }

  if (!rung.kind) return nothing;

  const amount = Math.floor(rung.amount);
  if (!Number.isFinite(amount) || amount < 1) return nothing;

  return { kind: rung.kind, amount: Math.min(amount, maxFor(rung.kind)) };
}

/** What one *currency* night is worth in one currency. Zero when it pays something else. */
export function streakCurrencyValue(
  config: StreakConfig,
  night: number,
  currency: string
): number {
  const rung = rungFor(config, night);
  return rung.kind === currency ? rung.amount : 0;
}

// ---------------------------------------------------------------------- the chest night
/**
 * The subject half of the seed. Mirrors `DailyStreak.Subject`: `{dayKey}:{night}`.
 *
 * The night's own calendar day, not today's — the client rolls it the same way, so a night
 * collected a week late rolls what it always would have. Contract.
 */
export function streakSubject(dayKey: number, night: number): string {
  return `${dayKey}:${night}`;
}

class StreakRandom extends Rolls {
  constructor(playerKey: string, subject: string, stream: number) {
    super(subjectSeed(playerKey, STREAK_SEED_TAG, subject, stream));
  }
}

/** Everything in one streak night's chest, for one account. */
export function rollStreakChest(
  chest: ChestConfig,
  playerKey: string,
  dayKey: number,
  night: number
): RolledDrop[] {
  const subject = streakSubject(dayKey, night);
  return rollChestWith(chest, (stream) => new StreakRandom(playerKey, subject, stream));
}

/** What one streak night's chest is worth in one currency. Zero when it holds none. */
export function streakChestValue(
  chest: ChestConfig,
  playerKey: string,
  dayKey: number,
  night: number,
  currency: string
): number {
  let total = 0;
  for (const drop of rollStreakChest(chest, playerKey, dayKey, night)) {
    if (drop.kind === currency) total += drop.amount;
  }
  return total;
}

/**
 * What a night is worth in one currency, whichever shape it is.
 *
 * <p>The one place the two shapes meet, so no caller has to know that a streak rung can be
 * a chest. Returns 0 — never a guess — when the rung names a tier the published task block
 * does not hold; the caller leaves such a claim <b>unconfirmed</b> rather than refusing it,
 * because that is a client on a content pack this server has not been seeded with, and
 * throwing away a reward the player earned is worse than paying it late (invariant 13a).</p>
 */
export function streakNightValue(
  ladder: StreakConfig,
  tasks: TaskConfig | null,
  playerKey: string,
  dayKey: number,
  night: number,
  currency: string
): { amount: number; tierId: string; priceable: boolean } {
  const rung = rungFor(ladder, night);

  if (!rung.tier) {
    return { amount: rung.kind === currency ? rung.amount : 0, tierId: "", priceable: true };
  }

  const tier = tasks ? findTier(tasks, rung.tier) : null;
  if (!tier) return { amount: 0, tierId: rung.tier, priceable: false };

  return {
    amount: streakChestValue(tier.chest, playerKey, dayKey, night, currency),
    tierId: rung.tier,
    priceable: true,
  };
}

/** Guards a config document that predates the streak block, or was seeded badly. */
export function usableStreakConfig(config: unknown): StreakConfig | null {
  const c = config as StreakConfig | undefined;

  if (!c || typeof c !== "object") return null;
  if (!Array.isArray(c.rungs) || c.rungs.length === 0) return null;
  if (c.rungs.length > MAX_RUNGS) return null;

  // At least one rung has to pay something, or the ladder is a published table that
  // grants nothing and every claim against it would be refused as invented.
  let pays = false;

  for (const rung of c.rungs) {
    if (!rung || typeof rung !== "object") return null;

    const tier = rung.tier;
    if (tier !== undefined && (typeof tier !== "string" || tier.length > MAX_TIER_ID)) return null;
    if (typeof tier === "string" && tier.length > 0) {
      // A chest night pays whatever its tier rolls, which is never nothing — every tier
      // guarantees at least one band (`usableTaskConfig`).
      if (rung.kind) return null;                     // one shape or the other, never both
      pays = true;
      continue;
    }

    if (rung.kind && !STREAK_KINDS.includes(rung.kind as StreakKind)) return null;
    if (rung.kind && rungFor({ rungs: [rung] }, 1).amount > 0) pays = true;
  }

  return pays ? c : null;
}

// -------------------------------------------------------------------- claim parsing
export interface StreakClaim {
  dayKey: number;
  night: number;
  currency: string;
}

export function isStreakGrantId(id: string): boolean {
  return typeof id === "string" && id.startsWith("streak:");
}

/**
 * Reads a grant id back into what it claims.
 *
 * The format is `streak:{day}:{night}:{currency}` and it is produced by
 * `GrantEntry.StreakNightId` on the client. Parsing rather than trusting a structured
 * payload is deliberate and is the same decision `parseDailyClaim` documents: the id is
 * what the database keys on, so the id is what has to be validated. Anything else leaves
 * room for a request whose id and whose fields disagree.
 */
export function parseStreakClaim(id: string): StreakClaim | null {
  if (typeof id !== "string" || id.length > 64) return null;

  const parts = id.split(":");
  if (parts.length !== 4 || parts[0] !== "streak") return null;

  const dayKey = Number(parts[1]);
  const night = Number(parts[2]);
  const currency = parts[3];

  if (!Number.isInteger(dayKey) || dayKey < 0) return null;
  if (!Number.isInteger(night) || night < 1 || night > MAX_NIGHT) return null;
  if (!currency || currency.length > 24) return null;

  // The canonical form has to round-trip, or two ids could name one night — "streak:07:3"
  // and "streak:7:3" would key two documents and pay twice.
  if (`streak:${dayKey}:${night}:${currency}` !== id) return null;

  return { dayKey, night, currency };
}

// ---------------------------------------------------------------------- the floor
/**
 * The last night this server paid for, and the day it fell on.
 *
 * Lives on `players/{uid}/private/wallet`, which no client can write. That is the entire
 * reason it is trustworthy and the reason it is not simply read off the save.
 */
export interface StreakFloor {
  paidThroughDay: number;
  paidNight: number;
}

export const NO_STREAK_FLOOR: StreakFloor = { paidThroughDay: 0, paidNight: 0 };

/**
 * Whether a claim is one an honest player could have arrived at, given what we last paid.
 *
 * <p>Three ways to satisfy it, and between them they describe every legitimate streak:</p>
 *
 * <ul>
 *   <li><b>Continuing.</b> The night <em>climbs</em>, and no faster than the calendar: we
 *   paid night 5 on Monday, so Thursday may be night 6, 7 or 8, and may not be night 9.</li>
 *   <li><b>Backfilled.</b> A claim dated <em>before</em> the floor — two devices submitting
 *   the same backlog in different orders — has to add up exactly, because there is no slack
 *   to spend running backwards.</li>
 *   <li><b>Restarted.</b> The streak broke, so the night is low again. It may be no
 *   longer than the days that have elapsed since we last paid: a streak that began after
 *   Monday cannot be six nights old on Wednesday.</li>
 * </ul>
 *
 * <p><b>Why the first one is a band rather than an equality, and why that costs nothing.</b>
 * It used to demand `night === paidNight + elapsed`, which is right for a streak that is fed
 * every single day and wrong for one that was <em>protected</em>: a shield keeps a streak
 * alive across days nobody played and deliberately buys no nights (`DailyStreak.Advance`),
 * so a player back after five protected days claims night 21 on the seventh day — one night
 * on, seven days on. The old rule refused that permanently, and the client drops a refused
 * claim (`CloudWalletState.RejectedGrantIds`), so it would have been a reward somebody paid
 * a hundred and twenty gems to keep and then silently lost.</p>
 *
 * <p>The security is unchanged, and it is worth being exact about why, because the obvious
 * worry is that a band lets a save editor <em>stall</em> on the ladder's best rung. It does
 * not: the night must <b>strictly increase</b>. Together the two halves say that over any
 * window of D days an account gets at most D claims and the night advances by at least one
 * per claim and by at most D in total — so reaching a given rung still costs exactly as many
 * days as it costs an honest player, and skipping the cheap rungs in between costs the days
 * you skipped. Which is the whole sentence this file exists to enforce: a forged streak buys
 * nothing an honest one does not.</p>
 *
 * <p>Note the shield itself is nowhere in this rule, and needs to be nowhere. It is an
 * ordinary gem spend on the client (invariant 18) that stores one date in the save; nothing
 * about it reaches this server as a permission, because there is nothing for a permission to
 * gate — the rate bound above holds whether or not anybody paid for anything.</p>
 *
 * <p>A zero floor — an account this server has never paid a streak night for — accepts
 * anything, once. That is deliberate. Every player who upgrades into this build arrives
 * holding a streak nobody recorded, often with several nights uncollected, and refusing
 * their backlog would take a reward the game had already shown them. From their first
 * claim onward the floor is real and the rule bites. New accounts do not get that
 * allowance at all: `readWallet` seeds the floor to yesterday when it creates a wallet,
 * so a fresh account's first claim must be night one, today.</p>
 */
export function advances(floor: StreakFloor, dayKey: number, night: number): boolean {
  if (night < 1) return false;
  if (floor.paidThroughDay <= 0) return true;      // never paid: see above

  const elapsed = dayKey - floor.paidThroughDay;

  // Running backwards, or the same day twice: exact, with no band to spend. Re-submitting
  // the night we just paid lands here and is permitted on purpose — it is stopped one layer
  // up by `grantLog/{id}`, which is the layer that also stops it across devices, across
  // reinstalls and after a dropped reply.
  if (elapsed <= 0) return night === floor.paidNight + elapsed;

  // Continuing: climbing, and no faster than the calendar.
  if (night > floor.paidNight && night <= floor.paidNight + elapsed) return true;

  return night <= elapsed;                                // a run that restarted
}

/** The floor after paying a night, which only ever moves forward. */
export function raise(floor: StreakFloor, dayKey: number, night: number): StreakFloor {
  return dayKey > floor.paidThroughDay ? { paidThroughDay: dayKey, paidNight: night } : floor;
}

/**
 * Reads a floor off a wallet document, treating anything malformed as absent.
 *
 * Absent has to mean "never paid" rather than "day zero", because the two lead to opposite
 * decisions — see `advances` — and a document written by an older build has no field here
 * at all.
 */
export function readFloor(raw: unknown): StreakFloor {
  const floor = raw as Partial<StreakFloor> | undefined;

  if (!floor || typeof floor !== "object") return NO_STREAK_FLOOR;

  const day = typeof floor.paidThroughDay === "number" ? Math.floor(floor.paidThroughDay) : 0;
  const night = typeof floor.paidNight === "number" ? Math.floor(floor.paidNight) : 0;

  if (!Number.isFinite(day) || day < 0) return NO_STREAK_FLOOR;

  return { paidThroughDay: day, paidNight: Number.isFinite(night) && night > 0 ? night : 0 };
}

// ------------------------------------------------------------------ the save's view
/** What the player's own save says about their streak. Read for shape, never believed. */
export interface SavedStreak {
  startDay: number;
  lastPlayedDay: number;
}

export function readSavedStreak(save: unknown): SavedStreak {
  const streak = (save as { streak?: Partial<SavedStreak> } | undefined)?.streak;

  const number = (value: unknown): number =>
    typeof value === "number" && Number.isFinite(value) && value > 0 ? Math.floor(value) : 0;

  return {
    startDay: number(streak?.startDay),
    lastPlayedDay: number(streak?.lastPlayedDay),
  };
}

/**
 * Whether the account's own save agrees that it reached this night on this day.
 *
 * <p><b>Advisory. Nothing is refused on it.</b> A claim that passes proves nothing — the
 * save is written by the client — so it could only ever be used to refuse, and refusing on
 * it would cost honest players real rewards. The case is ordinary rather than exotic:
 * collect a night, go offline, let the flame go out, play again three days later.
 * `DailyStreak.Record` moves `startDay` to today for the new run, and the still-unsent
 * claim now names a day the save no longer covers. It is a night the player genuinely
 * earned and genuinely collected, and a gate here would reject it — for ever, because a
 * rejected claim is one the client resubmits until it is confirmed.</p>
 *
 * <p>So this exists for the log. A disagreement is worth seeing, in exactly the way
 * `claimAwards` already records a client's amount differing from the server's, and for the
 * same reason: it is usually a device that has not pushed its save yet, and occasionally
 * it is the first sign of something being tried. The security is entirely in `advances`,
 * which needs no save at all.</p>
 */
export function saveSupports(saved: SavedStreak, dayKey: number, night: number): boolean {
  if (saved.startDay <= 0 || saved.lastPlayedDay <= 0) return false;
  if (dayKey < saved.startDay || dayKey > saved.lastPlayedDay) return false;

  return night === dayKey - saved.startDay + 1;
}
