/**
 * The Infinite lane's credits: the one payment in this game the server cannot recompute.
 *
 * Every other claim `claimAwards` honours names something this server can re-price for
 * itself — a daily chest is a pure function of (account, day, index), a streak night reads a
 * published ladder, a task chest re-rolls from the slate. A wave count is none of those. It
 * comes out of a run that happened entirely on a phone, and invariant 9d says plainly why the
 * lane has paid no currency until now: a forged tally moves a keeper level and never a
 * balance, and invariant 19l warns that the day it pays anything the argument stops working.
 *
 * So this payment falls to invariant 13's **fourth** clause — bound it so tightly that forging
 * buys nothing worth having — and everything in this file is that bound:
 *
 *  - The amount is the client's own figure, because nothing here can derive a better one.
 *  - It is clamped to what is left of a **daily ceiling** held against the wallet document,
 *    which is server-owned and which no client can write.
 *  - The ceiling is a figure about *money*, not about waves. A cap on waves is minted again by
 *    every replayed run; a cap on the day pays a cheater exactly what it pays somebody who
 *    played all evening.
 *  - The grant log makes each claim payable once, so a resubmission confirms instead of paying.
 *
 * The client keeps its own copy of the day's tally so the hub can stop offering money that
 * would be refused here (`EndlessCoins`), but that copy is a hint. This one is the ceiling.
 */

/** `endless:{dayKey}:{paidBefore}:{currency}` — minted by `GrantEntry.EndlessWavesId`. */
export interface EndlessClaim {
  dayKey: number;
  /** What the day had already paid when the run ended, as the client understood it. */
  paidBefore: number;
  currency: string;
}

/** Whether this id is a claim from the Infinite lane. */
export function isEndlessGrantId(id: string): boolean {
  return id.startsWith("endless:");
}

/**
 * Reads one back, or null.
 *
 * **Strict on both numbers**, for the reason every parse in this codebase is: a claim whose id
 * this server half-understands is a claim it prices against the wrong day. Exactly four parts,
 * both numbers non-negative integers, and no leading `+`, spaces or exponents — `Number` alone
 * accepts all three and would let two spellings of one day key past, which doubles the bound.
 */
export function parseEndlessClaim(id: string): EndlessClaim | null {
  const parts = id.split(":");
  if (parts.length !== 4 || parts[0] !== "endless") return null;

  const dayKey = strictInt(parts[1]);
  const paidBefore = strictInt(parts[2]);
  const currency = parts[3];

  if (dayKey === null || paidBefore === null || !currency) return null;

  return { dayKey, paidBefore, currency };
}

function strictInt(raw: string): number | null {
  if (!/^\d{1,9}$/.test(raw)) return null;
  return Number(raw);
}

/**
 * How many days either side of today a claim from this lane may be dated.
 *
 * **Much tighter than the task window's forty-five days**, and deliberately so. A task chest
 * sits unclaimed because a player did not open the game; an endless claim is raised the instant
 * a run ends, so anything old is a device that has been offline for days with money owed — rare
 * — or a backlog somebody assembled. Two days each way covers a phone with a wrong clock and a
 * sync that could not reach the network overnight.
 *
 * A claim outside it is **refused** rather than left pending, for invariant 13a's reason read
 * the other way: the window will not become true again tomorrow, so leaving it pending is a
 * claim resubmitted for the life of the account.
 */
export const MAX_ENDLESS_DAYS_AHEAD = 2;
export const MAX_ENDLESS_DAYS_BEHIND = 2;

/** The day's tally, as it is held on the wallet document. */
export interface EndlessDay {
  day: number;
  paid: number;
}

/**
 * Reads the tally off a wallet, defaulting to an unspent day.
 *
 * Carried through `readWallet` like every other field on that document, because every writer
 * of it writes it **whole** — a field the reader does not copy is one the next sync deletes,
 * and deleting this one hands the day's ceiling back to somebody who has already spent it.
 */
export function readEndlessDay(raw: unknown): EndlessDay | null {
  if (!raw || typeof raw !== "object") return null;

  const day = (raw as EndlessDay).day;
  const paid = (raw as EndlessDay).paid;

  if (typeof day !== "number" || typeof paid !== "number") return null;

  return { day: Math.max(0, Math.floor(day)), paid: Math.max(0, Math.floor(paid)) };
}

/**
 * What this claim may be paid, given the day it names and what that day has already cost.
 *
 * **The client's amount is an upper bound and never a figure to trust**, which is the whole
 * difference between this and every other pricing function in `claimAwards`. Three things cut
 * it down, in order: a day that is not the one the wallet is counting resets the tally, the
 * ceiling caps what is left, and a claim asking for nothing is refused rather than granted.
 *
 * `paidBefore` off the id is deliberately **not** consulted. It is the client's account of the
 * same number this function already holds a server-owned copy of, and preferring it would move
 * the bound onto the wire. It is in the id so that two devices banking the same run mint the
 * same string and are paid once — an identity, not a permission.
 */
export function endlessGrant(
  claimed: number,
  today: number,
  cap: number,
  held: EndlessDay | null
): { amount: number; day: EndlessDay } {
  const spent = held && held.day === today ? held.paid : 0;
  const ceiling = Math.max(0, Math.floor(cap));

  if (ceiling <= 0) return { amount: 0, day: { day: today, paid: spent } };

  const room = Math.max(0, ceiling - spent);
  const wanted = Math.max(0, Math.floor(claimed));
  const amount = Math.min(room, wanted);

  return { amount, day: { day: today, paid: spent + amount } };
}
