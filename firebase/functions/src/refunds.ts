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

/**
 * How far back a first sweep looks, and the furthest a late one will reach.
 *
 * Google keeps thirty days of voided purchases, so anything longer is asking for
 * something that does not exist. Thirty days is also comfortably longer than any outage
 * this job could survive, which is the point: a sweep that has not run for a week
 * catches up in one pass rather than losing the week.
 */
const MAX_LOOKBACK_MILLIS = 30 * 24 * 60 * 60 * 1000;

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
  reason: string
): Promise<boolean> {
  const db = getFirestore();
  const receiptRef = db.doc(PATHS.receipt(store, transactionId));

  return db.runTransaction(async (transaction) => {
    const snapshot = await transaction.get(receiptRef);

    // Never granted here. Common and not an error: a notification arrives for a purchase
    // made in another environment, or for one the client never managed to redeem.
    if (!snapshot.exists) return false;

    const receipt = snapshot.data() as {
      uid?: string;
      granted?: Record<string, number>;
      revokedAt?: unknown;
    };

    if (receipt.revokedAt) return false;                   // already reversed
    if (!receipt.uid) return false;

    const walletRef = db.doc(PATHS.wallet(receipt.uid));
    const walletSnapshot = await transaction.get(walletRef);

    // No wallet means nothing was ever granted into one. Stamp the receipt anyway so a
    // repeated notification stops asking.
    if (walletSnapshot.exists) {
      const wallet = walletSnapshot.data() as Record<string, { granted?: number }>;
      const update: Record<string, unknown> = { updatedAt: FieldValue.serverTimestamp() };

      for (const currency of CURRENCIES) {
        const amount = Math.floor(receipt.granted?.[currency] ?? 0);
        if (amount <= 0) continue;

        const held = Math.floor(wallet[currency]?.granted ?? 0);
        update[`${currency}.granted`] = Math.max(0, held - amount);
      }

      transaction.update(walletRef, update);
    }

    transaction.update(receiptRef, {
      revokedAt: FieldValue.serverTimestamp(),
      revokedReason: reason,
    });

    logger.warn("purchase revoked", {
      uid: receipt.uid, store, transactionId, reason, granted: receipt.granted,
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
 * Everything Google has voided since `sinceMillis`.
 *
 * `type=1` asks for voided subscriptions as well as one-off purchases. This game sells no
 * subscriptions, so it costs nothing today and means the sweep keeps working on the day
 * one is added — the failure it prevents is silent, which is the worst kind here.
 */
export async function listVoidedPurchases(
  serviceAccountJson: string,
  packageName: string,
  sinceMillis: number
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
    url.searchParams.set("startTime", String(sinceMillis));
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
  } while (token && voided.length < 5000);

  return voided;
}

/**
 * How far back the next sweep should read.
 *
 * The stored cursor is deliberately rewound by an hour on every read. Voided purchases
 * are listed by the time they were voided, and a boundary read exactly at the last
 * cursor will eventually drop one to clock skew or to a record landing a moment late.
 * Re-reading an hour costs a handful of no-op revocations — `revokeReceipt` is idempotent
 * — and losing one costs real money.
 */
export async function sweepWindow(nowMillis: number): Promise<number> {
  const snapshot = await getFirestore().doc(SWEEP_PATH).get();
  const last = snapshot.exists ? Number((snapshot.data() as { lastVoidedMillis?: number })?.lastVoidedMillis ?? 0) : 0;

  const overlap = 60 * 60 * 1000;
  const floor = nowMillis - MAX_LOOKBACK_MILLIS;

  if (!Number.isFinite(last) || last <= 0) return floor;
  return Math.max(floor, last - overlap);
}

export async function recordSweep(nowMillis: number, revoked: number, seen: number): Promise<void> {
  await getFirestore().doc(SWEEP_PATH).set(
    {
      lastVoidedMillis: nowMillis,
      lastRunAt: FieldValue.serverTimestamp(),
      lastRevoked: revoked,
      lastSeen: seen,
    },
    { merge: true }
  );
}

/** Restated so a caller need not import the currency list to log a reversal. */
export const REVOCABLE_CURRENCIES: readonly CurrencyId[] = CURRENCIES;
