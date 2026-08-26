/**
 * The bonus wheel — the server half of the client's `BonusWheel`.
 *
 * <p>
 * This is a contract, not a utility. The wheel decides what one `win_bonus` view is worth,
 * the phone draws the answer before the video plays, and this file grants the figure when
 * the network's callback lands. If the two ever disagree, a player watches a wheel stop on
 * five hundred and then watches their balance rise by two hundred — which is the worst
 * thing an economy can do in front of somebody and the hardest to explain afterwards.
 * </p>
 * <p>
 * <b>What makes that agreement possible at all.</b> Nothing about a rewarded ad is derivable
 * (invariant 10d): LevelPlay 9 carries no per-impression token from the phone to the
 * verification callback, so "the client says it won a thousand" is evidence of nothing. But
 * a <em>multiplier</em> over an amount this server already publishes is derivable, in exactly
 * the sense a daily chest's contents are — a pure function of (account, day, spin index),
 * seeded through the same `subjectSeed` the golden bonus uses. So neither side tells the
 * other anything; both compute it.
 * </p>
 * <p>
 * <b>The spin index is the one thing only this server can know</b>, and that is why it lives
 * on the wallet document rather than in the save. A counter the client kept would drift the
 * first time a callback was delayed past the next win. Here it advances only inside the
 * transaction that grants a view, so it is exactly "how many win-bonus videos this account
 * has been paid for today", and it rides back to the phone on every wallet reply.
 * </p>
 * <p>
 * Every constant here is contract: the tag, the stream, the separator inside the subject and
 * the plain modulo. Changing any of them re-rolls every unspun wheel in the world. See
 * invariant 9c and `firebase/shared/reward-vectors.json`, which both sides run as a test.
 * </p>
 */

import { Rolls, subjectSeed } from "./random";

/**
 * The wheel's place in the seed. Contract with `WheelRules` on the client — invariant 9c —
 * and never renumbered or renamed.
 */
export const WHEEL_TAG = "wheel";
export const WHEEL_STREAM = 0;

/** A slice may never pay less than the placement's own amount. Mirrors `WheelRules`. */
export const WHEEL_MIN_PERCENT = 100;
export const WHEEL_MAX_PERCENT = 1000;

export const WHEEL_MIN_SLICES = 4;
export const WHEEL_MAX_SLICES = 12;

/** One wedge: a multiplier on the placement's payout, as a percentage. */
export interface WheelSlice {
  percent: number;
}

export interface WheelConfig {
  slices: WheelSlice[];
}

/** Where a spin sits in the seed: the day, and which of that day's spins it is. */
export function wheelSubject(dayKey: number, spinIndex: number): string {
  return `${dayKey}:${spinIndex}`;
}

/**
 * Guards a published wheel, or answers null for a config that has none.
 *
 * <p>
 * Null is the ordinary answer, not an error: a deployment with no wheel pays the flat
 * amount, which is what `win_bonus` paid before the wheel existed and what a client that
 * has heard nothing back will draw. Refusing to grant at all would punish the player for a
 * seeding gap.
 * </p>
 * <p>
 * A slice below the floor is refused rather than clamped, and the whole wheel goes with it.
 * The client refuses the same file for the same reason, so the two agree about what a bad
 * table means — and "the wheel is off" is a state both sides can be in together, where
 * "this server silently repaired slice four" is not.
 * </p>
 */
export function usableWheelConfig(config: unknown): WheelConfig | null {
  if (!config || typeof config !== "object") return null;

  const slices = (config as { slices?: unknown }).slices;
  if (!Array.isArray(slices)) return null;
  if (slices.length < WHEEL_MIN_SLICES || slices.length > WHEEL_MAX_SLICES) return null;

  const clean: WheelSlice[] = [];
  let anyBonus = false;

  for (const slice of slices) {
    if (!slice || typeof slice !== "object") return null;

    const percent = (slice as { percent?: unknown }).percent;
    if (typeof percent !== "number" || !Number.isFinite(percent)) return null;

    const whole = Math.floor(percent);
    if (whole < WHEEL_MIN_PERCENT) return null;

    const capped = Math.min(whole, WHEEL_MAX_PERCENT);
    clean.push({ percent: capped });
    if (capped > WHEEL_MIN_PERCENT) anyBonus = true;
  }

  // A wheel every slice of which pays the same is a spin animation in front of a fixed
  // number. The client refuses it too, so refusing here keeps the pair honest rather than
  // leaving one side drawing a wheel the other has quietly flattened.
  return anyBonus ? { slices: clean } : null;
}

/**
 * Which slice this account's `spinIndex`'th spin of the day lands on.
 *
 * The client's `BonusWheel.Landing`, arrived at independently from the same three inputs.
 */
export function wheelLanding(uid: string, dayKey: number, spinIndex: number,
                             wheel: WheelConfig): number {
  if (!uid || !wheel || wheel.slices.length === 0) return -1;
  if (!Number.isFinite(dayKey) || dayKey < 0) return -1;
  if (!Number.isFinite(spinIndex) || spinIndex < 0) return -1;

  const rolls = new Rolls(subjectSeed(uid, WHEEL_TAG, wheelSubject(dayKey, spinIndex),
                                      WHEEL_STREAM));

  return rolls.below(wheel.slices.length);
}

/**
 * What that spin multiplies the placement's amount by, as a percentage.
 *
 * Falls back to the floor — the flat offer — for anything it cannot compute, which is the
 * only direction that can never cost a player something they were shown.
 */
export function wheelPercent(uid: string, dayKey: number, spinIndex: number,
                             wheel: WheelConfig | null): number {
  if (!wheel) return WHEEL_MIN_PERCENT;

  const landing = wheelLanding(uid, dayKey, spinIndex, wheel);
  if (landing < 0) return WHEEL_MIN_PERCENT;

  return wheel.slices[landing].percent;
}

/**
 * Applies a percentage to an amount, the one way, in one place.
 *
 * Integer arithmetic with the multiply before the divide, matching `BonusWheel.Apply`
 * exactly. `Math.floor` after the divide rather than before it, because the client's
 * `long` division truncates towards zero and both operands here are positive.
 */
export function applyWheelPercent(amount: number, percent: number): number {
  if (amount <= 0) return 0;
  if (percent <= WHEEL_MIN_PERCENT) return amount;

  return Math.floor((amount * percent) / 100);
}

// ------------------------------------------------------------------- the position
/** How many `win_bonus` views this account has been granted, and on which day. */
export interface WheelPosition {
  day: number;
  spins: number;
}

/**
 * Reads the stored position, rolling it over when the day has moved on.
 *
 * <p>
 * Lazy rather than on a timer, for `RewardedAds.Sync`'s reason on the client: a midnight
 * job would have to run for every account in the world to move a counter almost none of
 * them will use tomorrow. Only ever forward — a stored day in the future (a clock that ran
 * ahead once) is left alone rather than reset into, which would hand that day's wheel out a
 * second time.
 * </p>
 */
export function readWheelPosition(raw: unknown, today: number): WheelPosition {
  const stored = raw as Partial<WheelPosition> | undefined;

  const day = typeof stored?.day === "number" && Number.isFinite(stored.day)
    ? Math.max(0, Math.floor(stored.day))
    : -1;

  const spins = typeof stored?.spins === "number" && Number.isFinite(stored.spins)
    ? Math.max(0, Math.floor(stored.spins))
    : 0;

  if (day < 0 || day < today) return { day: today, spins: 0 };

  return { day, spins };
}
