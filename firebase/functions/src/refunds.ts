/**
 * Taking back what a store took back.
 *
 * A purchase can be undone after it has been granted: Apple refunds through support,
 * Google refunds through the Play Console and through chargebacks, and both can revoke a
 * transaction weeks later. Without this, a refund is free currency — buy, spend, refund,
 * repeat — and it is the single most common way a mobile economy leaks money, because it
 * needs no exploit and no tooling. Somebody works it out and posts it.
 *
 * The architecture already anticipated this, which is why so little of it is new.
 * `CurrencyLedger.ApplyServerState` on the client adopts the server's baselines rather
 * than taking the larger of the two, with a comment saying in as many words that a refund
 * legitimately lowers what was granted. All that was missing was something to lower it.
 *
 * Two different mechanisms, because the two stores are genuinely different:
 *
 * - **Apple pushes.** App Store Server Notifications V2 arrive at an HTTP endpoint as a
 *   signed JWS. See `appleNotification` for why the signature is deliberately *not* what
 *   this trusts.
 * - **Google is polled.** The Voided Purchases API is a list of everything voided in a
 *   window, fetched by us over an authenticated channel. There is a real-time
 *   notification channel too, but it needs a Pub/Sub topic and a subscription to keep
 *   alive, and refunds are not urgent — an hour's delay costs nothing, and a poll cannot
 *   silently stop working the way a subscription can.
 */

import { getFirestore, FieldValue } from "firebase-admin/firestore";
import { JWT } from "google-auth-library";
import { logger } from "firebase-functions";

import { CURRENCIES, CurrencyId, PATHS } from "./config";

/** Where the sweep records how far it has read. Not under `config`: no client may see it. */
export const SWEEP_PATH = "ops/refundSweep";

const DAY_MILLIS = 24 * 60 * 60 * 1000;

/**
 * How far back a first sweep looks, and the furthest a late one will reach.
 *
 * Google keeps thirty days of voided purchases and **refuses a `startTime` older than
 * that** ("Start time must be within [30] days of data") - so a lookback of exactly thirty
 * days is refused by the few hundred milliseconds between computing it and Google reading
 * it. That is how every sweep from launch until 2026-10-08 failed: 189 hourly runs in the
 * last week alone, none succeeding, and no Play refund ever reversed. Twenty-nine days
 * leaves a day of margin for clock skew and still catches up any outage this job could
 * survive in one pass. Held by `test/store.mjs`.
 */
export const MAX_LOOKBACK_MILLIS = 29 * DAY_MILLIS;

/**
 * The widest slice of time one list request asks Google for.
 *
 * The sweep reads its window in slices and records the cursor after each one, so a long
 * catch-up is a handful of bounded requests rather than one unbounded one, a run cut short
 * keeps what it finished, and the API's own rate limit (30 requests per 30 seconds) is never
 * approached: twenty-nine days is five slices.
 */
export const SWEEP_SLICE_MILLIS = 7 * DAY_MILLIS;

/**
 * Re-read on every sweep, behind the stored cursor. Voided purchases are listed by the time
 * they were voided, and a boundary read exactly at the last cursor will eventually drop one
 * to clock skew or to a record landing a moment late. Re-reading an hour costs a handful of
 * no-op revocations - `revokeReceipt` is idempotent - and losing one costs real money.
 */
export const SWEEP_OVERLAP_MILLIS = 60 * 60 * 1000;

/** What a receipt records about the grant it is reversing. */
export interface RevocableReceipt {
  uid?: string;
  productId?: string;
  capacity?: number;
  granted?: Record<string, number>;
}

/** A wallet, as much of one as a reversal needs to read. */
export type RevocableWallet = Record<string, { granted?: number } | undefined>;

/**
 * What reversing one receipt does to a wallet, as plain arithmetic over plain objects.
 *
 * <p>Lifted out of `revokeReceipt` so it can be proved without a Firestore transaction, a
 * network, or a real payment. This is the one path in the whole backend that takes money
 * *back*, and until it was split out the only way to exercise it was to buy something and
 * refund it — which is to say it was never exercised at all. The same argument the client
 * makes for splitting a run's economy out of its board.</p>
 *
 * <p>Two rules and both are load-bearing. <b>Balances clamp at zero</b> rather than going
 * negative: a player who already spent refunded currency ends at zero and keeps what they
 * bought, because the alternative is a balance that silently eats everything they earn for
 * a month, and a player who cannot see why their credits will not rise uninstalls. And a
 * <b>heart container is revoked by id rather than by amount</b>, because it is not an
 * amount — it is an entitlement the client holds, so the only thing this server can do
 * about it is say, on every wallet reply, that it was taken back. `arrayUnion` rather than
 * a read-modify-write, so a second notification for the same transaction — Apple sends
 * several — costs nothing and cannot race.</p>
 *
 * @param union what to wrap an id in; `FieldValue.arrayUnion` in production and the
 *   identity in a test, because a sentinel cannot be compared to anything.
 */
export function revocationUpdate(
  receipt: RevocableReceipt,
  wallet: RevocableWallet,
  union: (id: string) => unknown
): Record<string, unknown> {
  const update: Record<string, unknown> = {};

  for (const currency of CURRENCIES) {
    const amount = Math.floor(receipt.granted?.[currency] ?? 0);
    if (amount <= 0) continue;

    const held = Math.floor(wallet[currency]?.granted ?? 0);
    update[`${currency}.granted`] = Math.max(0, held - amount);
  }

  const capacity = Math.floor(receipt.capacity ?? 0);
  if (capacity > 0 && receipt.productId) {
    update.containersRevoked = union(receipt.productId);
  }

  return update;
}

/**
 * Reverses one granted receipt, exactly once.
 *
 * <p>The amounts come from the receipt document rather than from the product table, and
 * that is deliberate: the table can be retuned between the purchase and the refund, and
 * what has to be taken back is what was actually given. Storing the grant on the receipt
 * at redemption time is what makes that possible — see `redeemPurchase`.</p>
 *
 * <p>Balances clamp at zero rather than going negative. A player who has already spent
 * refunded currency ends at zero and keeps whatever they bought with it, which is the
 * right trade: the alternative is a negative balance that silently eats everything they
 * earn for the next month, and a player who cannot understand why their credits do not
 * rise is a player who uninstalls. Repeat abuse is a job for the stores' own account
 * bans, not for arithmetic.</p>
 *
 * @returns true when this call is what reversed it; false when it was already reversed.
 */
export async function revokeReceipt(
  store: string,
  transactionId: string,
  reason: string,
  db: FirebaseFirestore.Firestore = getFirestore()
): Promise<boolean> {
  const receiptRef = db.doc(PATHS.receipt(store, transactionId));

  return db.runTransaction(async (transaction) => {
    const snapshot = await transaction.get(receiptRef);

    // Never granted here. Common and not an error: a notification arrives for a purchase
    // made in another environment, or for one the client never managed to redeem.
    if (!snapshot.exists) return false;

    const receipt = snapshot.data() as RevocableReceipt & { revokedAt?: unknown };

    if (receipt.revokedAt) return false;                   // already reversed
    if (!receipt.uid) return false;

    const walletRef = db.doc(PATHS.wallet(receipt.uid));
    const walletSnapshot = await transaction.get(walletRef);

    // Nothing to revoke beyond the currency. A season's pass is bought with gems now, and a
    // gem purchase has no refund of its own: a store refund reverses the *gems*, through
    // `revocationUpdate` below, not whatever they were later spent on. That is the answer
    // this file already gives for a turret or a companion bought with them.

    // No wallet means nothing was ever granted into one. Stamp the receipt anyway so a
    // repeated notification stops asking.
    if (walletSnapshot.exists) {
      // The arithmetic is `revocationUpdate` and lives outside the transaction so it can be
      // proved offline — see its docs. All that happens here is reading, wrapping and
      // writing.
      const update = revocationUpdate(
        receipt,
        walletSnapshot.data() as RevocableWallet,
        (id) => FieldValue.arrayUnion(id)
      );

      update.updatedAt = FieldValue.serverTimestamp();
      transaction.update(walletRef, update);
    }

    transaction.update(receiptRef, {
      revokedAt: FieldValue.serverTimestamp(),
      revokedReason: reason,
    });

    logger.warn("purchase revoked", {
      uid: receipt.uid, store, transactionId, reason,
      granted: receipt.granted, productId: receipt.productId, capacity: receipt.capacity ?? 0,
    });

    return true;
  });
}

// ------------------------------------------------------------------------ Apple

/**
 * Every transaction id that appears anywhere in an App Store notification body.
 *
 * <p><b>The signature is deliberately not verified, and that is a stronger position
 * rather than a weaker one.</b> Nothing in this payload is believed. The ids scraped out
 * of it are used only to look up receipts this server already granted, and each of those
 * is then re-checked against the App Store Server API over TLS with a key only we hold —
 * the same authenticated channel `receipts.ts` validates purchases on. Apple's own answer
 * is what decides, so a forged notification can at most make this server ask Apple about
 * a transaction and be told it is fine.</p>
 *
 * <p>The alternative is verifying the JWS x5c chain against Apple's root, which means
 * shipping and rotating a root certificate, or taking a dependency that does. That is a
 * moving part in the path that reverses money, maintained for a guarantee already
 * obtained for free. Note that this reasoning holds <em>only</em> because every id is
 * re-checked with Apple; the moment anything here acts on the payload's own word, the
 * chain verification becomes mandatory.</p>
 */
export function transactionIdsIn(body: string, limit = 32): string[] {
  const ids = new Set<string>();
  if (!body) return [];

  // The JWS payload is base64url in the middle segment, and its own fields are further
  // signed payloads. Rather than unwrapping levels, decode anything that looks like a
  // segment and scan the lot for the one field shape that matters.
  const segments = body.split(/[."'\s]+/);

  for (const segment of segments) {
    if (segment.length < 24) continue;

    let decoded: string;
    try {
      decoded = Buffer.from(segment, "base64url").toString("utf8");
    } catch {
      continue;
    }

    for (const match of decoded.matchAll(/"(?:originalT|t)ransactionId"\s*:\s*"(\d{4,32})"/g)) {
      ids.add(match[1]);
      if (ids.size >= limit) return [...ids];
    }
  }

  return [...ids];
}

// ----------------------------------------------------------------------- Google

export interface VoidedPurchase {
  orderId: string;
  purchaseToken: string;
  voidedTimeMillis: number;
  reason: number;
}

/**
 * Everything Google voided in `[startMillis, endMillis]`, every page of it.
 *
 * `type=1` asks for voided subscriptions as well as one-off purchases. This game sells no
 * subscriptions, so it costs nothing today and means the sweep keeps working on the day
 * one is added — the failure it prevents is silent, which is the worst kind here.
 *
 * The end is explicit and the pages are read to the last one. An earlier cut stopped at
 * 5,000 entries and then recorded the cursor as *now*, which would have skipped everything
 * past the cap for ever without a word; bounding the slice in time (`SWEEP_SLICE_MILLIS`)
 * is what bounds the work instead.
 */
export async function listVoidedPurchases(
  serviceAccountJson: string,
  packageName: string,
  startMillis: number,
  endMillis: number
): Promise<VoidedPurchase[]> {
  const account = JSON.parse(serviceAccountJson) as { client_email: string; private_key: string };

  const client = new JWT({
    email: account.client_email,
    key: account.private_key,
    scopes: ["https://www.googleapis.com/auth/androidpublisher"],
  });

  const voided: VoidedPurchase[] = [];
  let token: string | undefined;

  do {
    const url = new URL(
      `https://androidpublisher.googleapis.com/androidpublisher/v3/applications/` +
      `${encodeURIComponent(packageName)}/purchases/voidedpurchases`
    );
    url.searchParams.set("startTime", String(startMillis));
    url.searchParams.set("endTime", String(endMillis));
    url.searchParams.set("type", "1");
    url.searchParams.set("maxResults", "1000");
    if (token) url.searchParams.set("token", token);

    const response = await client.request<{
      voidedPurchases?: Array<{
        purchaseToken?: string;
        orderId?: string;
        voidedTimeMillis?: string;
        voidedReason?: number;
      }>;
      tokenPagination?: { nextPageToken?: string };
    }>({ url: url.toString() });

    for (const entry of response.data.voidedPurchases ?? []) {
      if (!entry.orderId) continue;

      voided.push({
        orderId: entry.orderId,
        purchaseToken: entry.purchaseToken ?? "",
        voidedTimeMillis: Number(entry.voidedTimeMillis ?? 0),
        reason: entry.voidedReason ?? 0,
      });
    }

    token = response.data.tokenPagination?.nextPageToken;
  } while (token);

  return voided;
}

/**
 * Where the next sweep starts reading, from the stored cursor: an hour behind it
 * (`SWEEP_OVERLAP_MILLIS`), and never further back than Google will answer
 * (`MAX_LOOKBACK_MILLIS`). A missing, zero or unreadable cursor is a first sweep, which
 * reads the whole lookback. Pure, so the one rule that kept this job from ever succeeding
 * is held by a test rather than by a comment.
 */
export function sweepStart(nowMillis: number, cursorMillis: number): number {
  const floor = nowMillis - MAX_LOOKBACK_MILLIS;
  if (!Number.isFinite(cursorMillis) || cursorMillis <= 0) return floor;
  return Math.min(nowMillis, Math.max(floor, cursorMillis - SWEEP_OVERLAP_MILLIS));
}

/**
 * The window `[startMillis, nowMillis]` cut into consecutive slices of at most
 * `SWEEP_SLICE_MILLIS`, oldest first, each one's end the next one's start. Empty when
 * there is nothing to read. Pure for `sweepStart`'s reason.
 */
export function sweepSlices(startMillis: number, nowMillis: number): Array<[number, number]> {
  const slices: Array<[number, number]> = [];
  for (let from = startMillis; from < nowMillis; ) {
    const to = Math.min(nowMillis, from + SWEEP_SLICE_MILLIS);
    slices.push([from, to]);
    from = to;
  }
  return slices;
}

/** The stored cursor; nought when the sweep has never finished a slice. */
export async function readSweepCursor(): Promise<number> {
  const snapshot = await getFirestore().doc(SWEEP_PATH).get();
  if (!snapshot.exists) return 0;
  return Number((snapshot.data() as { lastVoidedMillis?: number })?.lastVoidedMillis ?? 0);
}

/**
 * A finished slice: the cursor moves to its end. Written after every slice rather than
 * once a run, so a run stopped part-way keeps what it finished and the next one resumes
 * from there. Clears the failure fields, so the document always says whether the job is
 * healthy.
 */
export async function recordSweep(cursorMillis: number, revoked: number, seen: number): Promise<void> {
  await getFirestore().doc(SWEEP_PATH).set(
    {
      lastVoidedMillis: cursorMillis,
      lastRunAt: FieldValue.serverTimestamp(),
      lastRevoked: revoked,
      lastSeen: seen,
      lastError: FieldValue.delete(),
      lastFailedAt: FieldValue.delete(),
    },
    { merge: true }
  );
}

/**
 * A failed slice: the cursor does not move, so the next run re-reads the same window, and
 * the failure is written beside it. A job that fails every hour was invisible for weeks
 * because the only record was a log line nobody was reading; `ops/refundSweep` now says
 * so itself, and `consecutiveFailures` says for how long.
 */
export async function recordSweepFailure(error: string): Promise<void> {
  await getFirestore().doc(SWEEP_PATH).set(
    {
      lastError: error.slice(0, 500),
      lastFailedAt: FieldValue.serverTimestamp(),
      consecutiveFailures: FieldValue.increment(1),
    },
    { merge: true }
  );
}

/**
 * A voided purchase this sweep could not reverse, kept by order id for a person to look at.
 * Written as a nested object rather than a dotted update path, because a Play order id is
 * `GPA.1234-...` and a dot in an update path would be read as nesting.
 */
export async function recordSweepSetAside(orderId: string, error: string): Promise<void> {
  await getFirestore().doc(SWEEP_PATH).set(
    { setAside: { [orderId]: { error: error.slice(0, 300), at: FieldValue.serverTimestamp() } } },
    { merge: true }
  );
}

/** Resets the failure count once a run has read every slice it owed. */
export async function recordSweepHealthy(): Promise<void> {
  await getFirestore().doc(SWEEP_PATH).set({ consecutiveFailures: 0 }, { merge: true });
}

/** Restated so a caller need not import the currency list to log a reversal. */
export const REVOCABLE_CURRENCIES: readonly CurrencyId[] = CURRENCIES;
