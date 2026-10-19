/**
 * What an account has paid, as the offer engine reads it (invariant 64).
 *
 * Four counters on the wallet document, raised by `redeemPurchase` in the transaction that grants
 * a validated receipt, and nowhere else: how many purchases, what they cost in reference US cents
 * in all and at most, and when the last one was granted. They are the money half of the client's
 * offer segments ("never paid", "bought up to $3.99", "lapsed for three weeks"), and they are the
 * server's because only the server sees a receipt it has validated.
 *
 * **They only climb.** A refund reverses the currency a purchase granted (`revokeReceipt`), never the
 * fact that somebody chose to pay, so a late sync can never walk the ladder of offers back down.
 * That also makes the field safe to carry through every whole-document wallet write untouched.
 *
 * Reference cents, not storefront money: the catalog's `referenceUsdCents`, published beside each
 * product by the seeder. A price tier in another currency moves with exchange rates and tax, and a
 * segment that read it would put the same player in different groups from one week to the next.
 */

/** The stored shape. Absent on an account that never paid, and on every wallet before this field. */
export interface PayerRecord {
  purchases: number;
  cents: number;
  topCents: number;
  lastUnix: number;
}

const whole = (value: unknown): number =>
  typeof value === "number" && Number.isFinite(value) ? Math.max(0, Math.floor(value)) : 0;

/** Reads the stored record, or null when there is none (an account that never paid). */
export function readPayer(raw: unknown): PayerRecord | null {
  if (!raw || typeof raw !== "object") return null;
  const r = raw as Record<string, unknown>;
  const record = {
    purchases: whole(r.purchases),
    cents: whole(r.cents),
    topCents: whole(r.topCents),
    lastUnix: whole(r.lastUnix),
  };
  return record.purchases > 0 || record.cents > 0 ? record : null;
}

/**
 * The record after one more purchase of `cents`, granted at `nowUnix`. Pure; the caller writes it in
 * the granting transaction, so a retried receipt (which grants nothing) never counts twice.
 */
export function addPurchase(record: PayerRecord | null, cents: number, nowUnix: number): PayerRecord {
  const c = whole(cents);
  const before = record ?? { purchases: 0, cents: 0, topCents: 0, lastUnix: 0 };
  return {
    purchases: before.purchases + 1,
    cents: before.cents + c,
    topCents: Math.max(before.topCents, c),
    lastUnix: Math.max(before.lastUnix, whole(nowUnix)),
  };
}

/** The four reply fields, always present - their presence is how a client knows this deployment counts. */
export function payerReply(record: PayerRecord | null | undefined): {
  payerPurchases: number; payerCents: number; payerTopCents: number; payerLastUnix: number;
} {
  return {
    payerPurchases: record?.purchases ?? 0,
    payerCents: record?.cents ?? 0,
    payerTopCents: record?.topCents ?? 0,
    payerLastUnix: record?.lastUnix ?? 0,
  };
}
