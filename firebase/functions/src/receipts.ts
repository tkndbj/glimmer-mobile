/**
 * Store receipt validation.
 *
 * Everything here fails closed. A receipt that cannot be checked — because a secret is
 * missing, a store is unreachable, or a response is shaped unexpectedly — is not
 * granted. Granting on a failed check is how a store outage turns into free currency
 * for anyone watching, and there is no way to take it back afterwards.
 *
 * Validation answers one question: did *this* transaction id really happen, for *this*
 * product, in *our* app. It deliberately does not decide what the purchase is worth —
 * that comes from the server's own product catalog, because a client that names its
 * own reward is a client that names any number it likes.
 */

import { JWT } from "google-auth-library";
import * as jwt from "jsonwebtoken";
import { logger } from "firebase-functions";

export interface ValidatedPurchase {
  store: "apple" | "google";
  transactionId: string;
  productId: string;
  purchasedAtMillis: number;
  /** True when the store says this came from a sandbox or test account. */
  sandbox: boolean;
}

export class ReceiptRejected extends Error {
  constructor(message: string) {
    super(message);
    this.name = "ReceiptRejected";
  }
}

// ---------------------------------------------------------------------- Apple

export interface AppleSecrets {
  keyId: string;
  issuerId: string;
  privateKey: string;
  bundleId: string;
}

/**
 * Asks Apple about a transaction, rather than believing a payload the device supplied.
 *
 * This is the important structural choice. The App Store Server API is queried by us,
 * over TLS, using a key only we hold, and the answer describes a transaction Apple
 * recognises. A receipt blob handed over by the client is not evidence of anything —
 * it is a string the client controls — so it is used only as a lookup key.
 *
 * The response body is a JWS. Because we fetched it ourselves from an authenticated
 * Apple endpoint, the transport is what establishes authenticity and the payload is
 * decoded rather than signature-checked.
 *
 * That reasoning does NOT extend to App Store Server Notifications, which arrive
 * unsolicited — so `appleNotification` does not extend it. It believes nothing in the
 * pushed payload; it scrapes transaction ids out of it and asks
 * `lookupAppleTransaction` about each one, which comes back here, over this same
 * authenticated channel. Anything that ever acts on a notification's own word instead
 * must verify its x5c chain against Apple's root first.
 */
async function validateApple(
  transactionId: string,
  secrets: AppleSecrets
): Promise<ValidatedPurchase> {
  const decoded = await lookupAppleTransaction(transactionId, secrets);

  if (decoded.bundleId !== secrets.bundleId) {
    // A real transaction, from somebody else's app. Refusing this is what stops a
    // receipt bought in another product being spent here.
    throw new ReceiptRejected(
      `transaction belongs to bundle ${decoded.bundleId}, not ${secrets.bundleId}`
    );
  }

  if (decoded.revocationDate) {
    throw new ReceiptRejected("transaction has been revoked or refunded");
  }

  return {
    store: "apple",
    transactionId: decoded.transactionId!,
    productId: decoded.productId!,
    purchasedAtMillis: decoded.purchaseDate ?? Date.now(),
    sandbox: (decoded.environment ?? "").toLowerCase() === "sandbox",
  };
}

export interface AppleTransaction {
  transactionId?: string;
  productId?: string;
  bundleId?: string;
  purchaseDate?: number;
  environment?: string;
  revocationDate?: number;
  revocationReason?: number;
}

/**
 * Asks Apple what it holds for one transaction id.
 *
 * <p>Split out of validation because two callers need it and they want different halves.
 * A purchase being redeemed wants "is this real, is it ours, is it still good"; a refund
 * notification wants only the last of those. Both get the answer from the same
 * authenticated round trip, which is the property the notification handler leans on
 * entirely — see `appleNotification`, where nothing in the pushed payload is believed and
 * this call is what decides.</p>
 */
export async function lookupAppleTransaction(
  transactionId: string,
  secrets: AppleSecrets
): Promise<AppleTransaction> {
  const now = Math.floor(Date.now() / 1000);

  const token = jwt.sign(
    {
      iss: secrets.issuerId,
      iat: now,
      exp: now + 15 * 60,
      aud: "appstoreconnect-v1",
      bid: secrets.bundleId,
    },
    secrets.privateKey,
    { algorithm: "ES256", header: { alg: "ES256", kid: secrets.keyId, typ: "JWT" } }
  );

  // Production first. A live build's transaction is never in the sandbox, but a
  // TestFlight or review build's always is, and app review failing to buy anything is
  // a rejection — so the sandbox is tried second rather than not at all.
  const hosts = [
    "https://api.storekit.itunes.apple.com",
    "https://api.storekit-sandbox.itunes.apple.com",
  ];

  let lastStatus = 0;

  for (const host of hosts) {
    const response = await fetch(`${host}/inApps/v1/transactions/${encodeURIComponent(transactionId)}`, {
      headers: { Authorization: `Bearer ${token}` },
    });

    if (response.status === 404) {
      lastStatus = 404;
      continue;                              // not in this environment; try the other
    }

    if (!response.ok) {
      lastStatus = response.status;
      logger.warn("App Store Server API refused the lookup", { status: response.status, host });
      continue;
    }

    const body = (await response.json()) as { signedTransactionInfo?: string };
    if (!body.signedTransactionInfo) {
      throw new ReceiptRejected("Apple returned no signedTransactionInfo");
    }

    const decoded = jwt.decode(body.signedTransactionInfo) as AppleTransaction | null;

    if (!decoded || !decoded.transactionId || !decoded.productId) {
      throw new ReceiptRejected("Apple transaction payload was not readable");
    }

    if (decoded.transactionId !== transactionId) {
      throw new ReceiptRejected("Apple returned a different transaction id than was asked for");
    }

    return decoded;
  }

  throw new ReceiptRejected(`Apple does not recognise transaction ${transactionId} (last status ${lastStatus})`);
}

// --------------------------------------------------------------------- Google

interface GoogleSecrets {
  serviceAccount: { client_email: string; private_key: string };
  packageName: string;
}

/**
 * Asks Google Play about a purchase token.
 *
 * `purchaseState` is the field that matters: 0 is purchased, 1 is cancelled, 2 is
 * pending. Granting on a pending purchase is a classic mistake — the money has not
 * moved, and the player can simply walk away from the payment.
 */
async function validateGoogle(
  productId: string,
  purchaseToken: string,
  secrets: GoogleSecrets
): Promise<ValidatedPurchase> {
  const client = new JWT({
    email: secrets.serviceAccount.client_email,
    key: secrets.serviceAccount.private_key,
    scopes: ["https://www.googleapis.com/auth/androidpublisher"],
  });

  const url =
    `https://androidpublisher.googleapis.com/androidpublisher/v3/applications/` +
    `${encodeURIComponent(secrets.packageName)}/purchases/products/` +
    `${encodeURIComponent(productId)}/tokens/${encodeURIComponent(purchaseToken)}`;

  const response = await client.request<{
    purchaseState?: number;
    purchaseTimeMillis?: string;
    orderId?: string;
    purchaseType?: number;
  }>({ url });

  const data = response.data;

  if (data.purchaseState !== 0) {
    throw new ReceiptRejected(
      `Google Play reports purchaseState ${data.purchaseState} (0 means purchased)`
    );
  }

  if (!data.orderId) {
    throw new ReceiptRejected("Google Play returned no orderId to key the grant on");
  }

  return {
    store: "google",
    // The order id, not the purchase token: the token is a client-held string, while
    // the order id is Google's own identifier for the transaction.
    transactionId: data.orderId,
    productId,
    purchasedAtMillis: Number(data.purchaseTimeMillis ?? Date.now()),
    // purchaseType 0 is a test purchase, 1 is a promo. Both are real transactions but
    // neither is revenue, and a live economy should know the difference.
    sandbox: data.purchaseType === 0,
  };
}

// ---------------------------------------------------------------------- entry

export interface RawReceipt {
  store?: unknown;
  transactionId?: unknown;
  productId?: unknown;
  payload?: unknown;
}

export async function validateReceipt(
  receipt: RawReceipt,
  secrets: {
    appleKeyId?: string;
    appleIssuerId?: string;
    applePrivateKey?: string;
    googleServiceAccount?: string;
    bundleId: string;
  }
): Promise<ValidatedPurchase> {
  const store = typeof receipt.store === "string" ? receipt.store.toLowerCase() : "";
  const transactionId = typeof receipt.transactionId === "string" ? receipt.transactionId : "";
  const productId = typeof receipt.productId === "string" ? receipt.productId : "";

  if (!transactionId) throw new ReceiptRejected("receipt has no transaction id");
  if (!productId) throw new ReceiptRejected("receipt has no product id");

  if (store === "apple") {
    if (!secrets.appleKeyId || !secrets.appleIssuerId || !secrets.applePrivateKey) {
      throw new ReceiptRejected("Apple validation is not configured on this deployment");
    }
    return validateApple(transactionId, {
      keyId: secrets.appleKeyId,
      issuerId: secrets.appleIssuerId,
      // Secret Manager stores the .p8 with literal \n, which is not a PEM.
      privateKey: secrets.applePrivateKey.replace(/\\n/g, "\n"),
      bundleId: secrets.bundleId,
    });
  }

  if (store === "google") {
    if (!secrets.googleServiceAccount) {
      throw new ReceiptRejected("Google Play validation is not configured on this deployment");
    }

    const payload = typeof receipt.payload === "string" ? receipt.payload : "";
    if (!payload) throw new ReceiptRejected("Google receipt has no purchase token");

    return validateGoogle(productId, payload, {
      serviceAccount: JSON.parse(secrets.googleServiceAccount),
      packageName: secrets.bundleId,
    });
  }

  throw new ReceiptRejected(`unknown store '${store}'`);
}
