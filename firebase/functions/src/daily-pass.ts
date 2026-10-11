/**
 * The seven-day passes: a real-money product that pays its currency in daily instalments.
 *
 * <p><b>Why it is safe to sell for money.</b> Invariant 18 says a real-money product grants
 * currency and nothing else, and this one does exactly that - it just grants it in `days`
 * pieces instead of one. Every piece is currency this server mints into `granted`, so the
 * client applies nothing and stores nothing (10, 15): the whole entitlement lives on the wallet
 * document, which no client can write. The receipt is still granted once per transaction
 * (`redeemPurchase`), and the first piece is paid in that same transaction, so a purchase
 * always lands with something in it.</p>
 *
 * <p><b>The shape: runs, paid one piece per UTC day, oldest first.</b> Each purchase opens a
 * run of `days` pieces whose per-day amounts are copied from the catalog at the moment of
 * purchase, so a later re-seed cannot change what somebody has already paid for. A claim pays
 * the next piece of the oldest open run, at most once per UTC day per product, and a run is
 * dropped the moment its last piece is paid. Nothing ever expires: a player who misses a day
 * collects the piece on a later day, so a pass bought is always paid in full - there is no
 * clock that can cost anybody a reward they paid for.</p>
 *
 * <p><b>Idempotent by the day, not by a request id.</b> The second claim of a UTC day is
 * answered `not_yet` and pays nothing, so a reply lost on the way back costs a retry that
 * reports the balance the first call already raised. A second purchase while a run is still
 * open (two phones, or a store re-delivery) opens a second run behind the first: every piece of
 * both is paid, so nothing a player paid for is ever folded away.</p>
 *
 * <p>Pure functions over plain objects, for `revocationUpdate`'s reason: the paths that mint
 * and take back money are proved offline by `test/daily-pass.mjs`.</p>
 */

/** The most pieces one pass may be split into. Mirrors `StoreLimits.MaxPassDays`. */
export const MAX_PASS_DAYS = 31;

/**
 * How many open runs one product is expected to carry at most. Never reached by a player - the
 * shop shows CLAIM instead of a price while a run is open - so crossing it is logged as
 * something to look at. A purchase beyond it is still paid in full: see `openRun`.
 */
export const MAX_OPEN_RUNS = 16;

/** One purchase's instalments: what each piece pays, how many there are, how many are paid. */
export interface PassRun {
  /** `{store}:{transactionId}` - the receipt this run came out of, so a refund can find it. */
  tx: string;
  days: number;
  paid: number;
  credits: number;
  gems: number;
}

/** One product's open runs, and the last UTC day a piece of it was paid. */
export interface PassHolding {
  day: number;
  runs: PassRun[];
}

/** Product id to holding. Only products with an open run are kept. */
export type DailyPasses = Record<string, PassHolding>;

/** What one claim did. */
export type PassClaimOutcome = "paid" | "not_yet" | "none";

export interface PassClaim {
  outcome: PassClaimOutcome;
  credits: number;
  gems: number;
}

/** What a wallet reply says about one product: pieces still owed, and the day one was last paid. */
export interface PassReplyRow {
  id: string;
  remaining: number;
  day: number;
}

const PRODUCT_ID = /^[a-z0-9_]{1,64}$/;

const whole = (value: unknown): number =>
  typeof value === "number" && Number.isFinite(value) ? Math.max(0, Math.floor(value)) : 0;

/** True for a string that could be a product id (the catalog's own alphabet). */
export function isPassProductId(id: unknown): id is string {
  return typeof id === "string" && PRODUCT_ID.test(id);
}

/** The run key for a receipt. */
export function runKey(store: string, transactionId: string): string {
  return `${store}:${transactionId}`;
}

/**
 * Reads the wallet's `dailyPasses` field, dropping anything malformed and any run already paid
 * in full. Never throws; absent reads as empty.
 */
export function readDailyPasses(raw: unknown): DailyPasses {
  const passes: DailyPasses = {};
  if (!raw || typeof raw !== "object" || Array.isArray(raw)) return passes;

  for (const [id, value] of Object.entries(raw as Record<string, unknown>)) {
    if (!isPassProductId(id) || !value || typeof value !== "object") continue;

    const holding = value as { day?: unknown; runs?: unknown };
    const runs: PassRun[] = [];

    if (Array.isArray(holding.runs)) {
      for (const entry of holding.runs) {
        if (!entry || typeof entry !== "object") continue;
        const r = entry as Record<string, unknown>;

        const run: PassRun = {
          tx: typeof r.tx === "string" ? r.tx : "",
          days: Math.min(whole(r.days), MAX_PASS_DAYS),
          paid: whole(r.paid),
          credits: whole(r.credits),
          gems: whole(r.gems),
        };

        if (run.days < 1 || run.paid >= run.days) continue;
        if (run.credits === 0 && run.gems === 0) continue;
        runs.push(run);
      }
    }

    if (runs.length > 0) passes[id] = { day: whole(holding.day), runs };
  }

  return passes;
}

/**
 * Opens a run for a purchase that has just been validated, and pays its first piece - which the
 * caller adds to `granted` in the same transaction that records the receipt.
 *
 * <p>The first piece is paid whatever day it is and whatever was claimed today: the player has
 * just paid, so something must arrive now. It moves the product's day to today, so the next
 * piece of any run is tomorrow's.</p>
 *
 * <p>A run of one day is paid in full here and never stored. A purchase past
 * `MAX_OPEN_RUNS` is still opened - refusing it would be a paid receipt with nothing behind it -
 * and the caller only logs it (`overBound`).</p>
 */
export function openRun(
  passes: DailyPasses,
  productId: string,
  tx: string,
  perDay: { credits: number; gems: number },
  days: number,
  today: number
): { credits: number; gems: number } {
  const span = Math.min(Math.max(1, whole(days)), MAX_PASS_DAYS);
  const credits = whole(perDay.credits);
  const gems = whole(perDay.gems);

  const holding = passes[productId] ?? { day: 0, runs: [] };
  holding.day = Math.max(holding.day, today);

  if (span > 1) holding.runs.push({ tx, days: span, paid: 1, credits, gems });

  if (holding.runs.length > 0) passes[productId] = holding;
  else delete passes[productId];

  return { credits, gems };
}

/**
 * Pays today's piece of a product's oldest open run, or says why not.
 *
 * <p>`none` when nothing is owed, `not_yet` when a piece was already paid today. Neither is a
 * refusal anything has to drop: the first means the shop shows the price again, the second
 * that the button comes back tomorrow (13a - nothing here is refused for a reason that is
 * still true tomorrow).</p>
 */
export function claimPiece(passes: DailyPasses, productId: string, today: number): PassClaim {
  const holding = passes[productId];
  if (!holding || holding.runs.length === 0) return { outcome: "none", credits: 0, gems: 0 };
  if (holding.day >= today) return { outcome: "not_yet", credits: 0, gems: 0 };

  const run = holding.runs[0];
  run.paid += 1;
  holding.day = today;

  if (run.paid >= run.days) holding.runs.shift();
  if (holding.runs.length === 0) delete passes[productId];

  return { outcome: "paid", credits: run.credits, gems: run.gems };
}

/**
 * Takes back a refunded purchase's run: what to reverse beyond the first piece (which the
 * receipt's own `granted` already reverses), and the passes without that run.
 *
 * <p>A run still open is reversed for the pieces paid after the first, and its unpaid pieces
 * are simply never paid. A run no longer present was paid in full, so all its later pieces are
 * reversed. Either way the refund takes back exactly what the purchase paid out.</p>
 */
export function revokeRun(
  passes: DailyPasses,
  productId: string,
  tx: string,
  recorded: { days: number; credits: number; gems: number }
): { credits: number; gems: number } {
  const holding = passes[productId];
  const index = holding ? holding.runs.findIndex((run) => run.tx === tx) : -1;

  let later: number;
  if (holding && index >= 0) {
    later = Math.max(0, holding.runs[index].paid - 1);
    holding.runs.splice(index, 1);
    if (holding.runs.length === 0) delete passes[productId];
  } else {
    later = Math.max(0, Math.min(whole(recorded.days), MAX_PASS_DAYS) - 1);
  }

  return { credits: later * whole(recorded.credits), gems: later * whole(recorded.gems) };
}

/** The wallet reply's rows: one per product still owed something, sorted by id. */
export function passReply(passes: DailyPasses | undefined): PassReplyRow[] {
  if (!passes) return [];

  return Object.keys(passes).sort().map((id) => {
    const holding = passes[id];
    const remaining = holding.runs.reduce((sum, run) => sum + Math.max(0, run.days - run.paid), 0);
    return { id, remaining, day: holding.day };
  }).filter((row) => row.remaining > 0);
}

/**
 * Whether the wallet's open runs are within the read bound. Logged, never enforced on a
 * purchase - see `MAX_OPEN_RUNS`.
 */
export function overBound(passes: DailyPasses, productId: string): boolean {
  return (passes[productId]?.runs.length ?? 0) > MAX_OPEN_RUNS;
}
