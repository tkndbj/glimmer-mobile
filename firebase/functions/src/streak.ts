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

/** Mirrors `ChestDropKinds`. Never renamed or reused — a published ladder names these. */
export const STREAK_KINDS = ["credits", "gems", "hearts", "heart_boost"] as const;
export type StreakKind = (typeof STREAK_KINDS)[number];

export interface StreakRung {
  kind: StreakKind | "";
  amount: number;
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

// ------------------------------------------------------------------------ the ladder
/**
 * What the `night`th night pays, counting from 1.
 *
 * Wraps: night eight of a seven-night ladder pays night one's rung. Mirrors
 * `StreakTable.Rung`, and the wrap is the contract — a server that repeated the last rung
 * instead would pay a different reward from the one the board drew, every lap, for ever.
 */
export function rungFor(config: StreakConfig, night: number): StreakRung {
  if (!Number.isInteger(night) || night < 1) return { kind: "", amount: 0 };

  const rungs = config.rungs;
  if (!Array.isArray(rungs) || rungs.length === 0) return { kind: "", amount: 0 };

  const rung = rungs[(night - 1) % rungs.length];
  if (!rung || !rung.kind) return { kind: "", amount: 0 };

  const amount = Math.floor(rung.amount);
  if (!Number.isFinite(amount) || amount < 1) return { kind: "", amount: 0 };

  return { kind: rung.kind, amount: Math.min(amount, maxFor(rung.kind)) };
}

/** What one night is worth in one currency. Zero when it pays something else. */
export function streakCurrencyValue(
  config: StreakConfig,
  night: number,
  currency: string
): number {
  const rung = rungFor(config, night);
  return rung.kind === currency ? rung.amount : 0;
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
 * <p>Two ways to satisfy it, and between them they describe every legitimate streak:</p>
 *
 * <ul>
 *   <li><b>Continuing.</b> The night advances by exactly as many days as have passed:
 *   we paid night 5 on Monday, so Tuesday is night 6 and Thursday is night 8. This also
 *   accepts a claim dated *before* the floor — two devices submitting the same backlog in
 *   different orders — because the arithmetic works in both directions.</li>
 *   <li><b>Restarted.</b> The streak broke, so the night is low again. It may be no
 *   longer than the days that have elapsed since we last paid: a streak that began after
 *   Monday cannot be six nights old on Wednesday.</li>
 * </ul>
 *
 * <p>What it refuses is the only thing worth refusing: a night that is higher than the
 * calendar allows. Claiming night seven every morning fails the first test (seven is not
 * six plus one) and the second (seven nights have not elapsed since yesterday).</p>
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

  if (night === floor.paidNight + elapsed) return true;   // an unbroken run
  return elapsed > 0 && night <= elapsed;                 // a run that restarted
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
