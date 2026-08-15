/**
 * Glimmer Grove — the server half of the economy.
 *
 * Three callable endpoints, and between them they hold the two invariants that cannot
 * be enforced anywhere else:
 *
 *   1. A debit is charged exactly once, however many times it is submitted.
 *   2. A store transaction is granted exactly once, to exactly one account, ever.
 *
 * Both are enforced by writing an identity document inside the same transaction that
 * moves the money. Not by remembering what was seen, not by a lock, not by the client
 * being well behaved — by the database refusing to let the second attempt through.
 */

import { initializeApp } from "firebase-admin/app";
import { getFirestore, FieldValue, Timestamp } from "firebase-admin/firestore";
import { onCall, onRequest, HttpsError, CallableRequest } from "firebase-functions/v2/https";
import { onSchedule } from "firebase-functions/v2/scheduler";
import { defineSecret } from "firebase-functions/params";
import { logger } from "firebase-functions";

import {
  BUNDLE_ID, CURRENCIES, CurrencyId,
  MAX_AWARDS_PER_CALL, MAX_SPENDS_PER_CALL, PATHS, REGION,
} from "./config";
import {
  ackBody, adCurrencyOf, adCurrencyValue, adGrantId,
  isAdGrantId, usableAdConfig, verifyAdCallback,
} from "./ads";
import {
  chestCurrencyValue, DailyClaim, MAX_DAYS_AHEAD, MAX_DAYS_BEHIND,
  parseDailyClaim, todayKey, usableDailyConfig,
} from "./daily";
import {
  advances, isStreakGrantId, MAX_STREAK_DAYS_AHEAD, MAX_STREAK_DAYS_BEHIND,
  parseStreakClaim, raise, readSavedStreak, saveSupports, StreakClaim,
  streakCurrencyValue, usableStreakConfig,
} from "./streak";
import { ReceiptRejected, validateReceipt } from "./receipts";
import { rebuildStats } from "./stats";
import {
  deriveEarned,
  loadProgressionConfig,
  readWallet,
  spendableBalance,
  toReply,
  WalletDoc,
  WalletReply,
} from "./wallet";

initializeApp();

// Secrets live in Secret Manager, never in source and never in environment config that
// ends up in a repository. Absent secrets make validation fail closed — see receipts.ts.
const APPLE_KEY_ID = defineSecret("APPLE_KEY_ID");
const APPLE_ISSUER_ID = defineSecret("APPLE_ISSUER_ID");
const APPLE_PRIVATE_KEY = defineSecret("APPLE_PRIVATE_KEY");
const GOOGLE_PLAY_SERVICE_ACCOUNT = defineSecret("GOOGLE_PLAY_SERVICE_ACCOUNT");

/**
 * The private key LevelPlay signs its rewarded callbacks with.
 *
 * Set on the LevelPlay dashboard and here, and nowhere else. Absent, every callback is
 * refused — see `verifyAdCallback`, which fails closed rather than treating an empty key
 * as an empty signature that matches everything.
 */
const LEVELPLAY_SECRET = defineSecret("LEVELPLAY_SECRET");

const callOptions = { region: REGION, cors: false, enforceAppCheck: false } as const;

/**
 * The value a secret holds before real store credentials exist.
 *
 * A declared secret has to exist for the deployment to go through, so the sync and
 * spend endpoints would otherwise be blocked on App Store Connect paperwork. A
 * placeholder unblocks them and is read back as "not configured", which
 * <c>validateReceipt</c> turns into a refusal — so purchases fail closed rather than
 * being granted against a key that cannot validate anything.
 */
const UNSET = "UNSET";

function configured(secret: { value: () => string }): string | undefined {
  const value = secret.value();
  return !value || value === UNSET ? undefined : value;
}

function requireUid(request: CallableRequest): string {
  const uid = request.auth?.uid;
  if (!uid) throw new HttpsError("unauthenticated", "sign in before touching the wallet");
  return uid;
}

// ---------------------------------------------------------------- read wallet
/**
 * The player's authoritative balances. Called on sync so the client can adopt them.
 */
export const getWallet = onCall(callOptions, async (request): Promise<{ wallets: WalletReply[] }> => {
  const uid = requireUid(request);
  const db = getFirestore();

  const wallet = await db.runTransaction(async (transaction) => {
    const config = await loadProgressionConfig(transaction);

    const walletRef = db.doc(PATHS.wallet(uid));
    const snapshot = await transaction.get(walletRef);
    const state = readWallet(snapshot, config);

    const before = state.credits.earnedFloor;
    await deriveEarned(transaction, uid, state, config);   // ratchets the floor

    // Written when the document is new — so the starting balance is granted once and
    // recorded rather than re-granted on every call — or when the floor has moved.
    if (!snapshot.exists || state.credits.earnedFloor !== before) {
      transaction.set(walletRef, { ...state, updatedAt: FieldValue.serverTimestamp() });
      if (!snapshot.exists) logger.info("seeded a new wallet", { uid });
    }

    return state;
  });

  return { wallets: toReply(wallet, {}) };
});

// -------------------------------------------------------------- submit spends
interface SubmittedSpend {
  id?: unknown;
  currency?: unknown;
  amount?: unknown;
  unix?: unknown;
  reason?: unknown;
}

/**
 * Confirms debits the client has already applied locally.
 *
 * Idempotent by construction: each debit carries a client-generated id, and the first
 * thing the transaction does is check whether a document with that id already exists.
 * Resubmitting is therefore not merely tolerated, it is the expected behaviour after a
 * dropped response — which is exactly the case a bare "spent" counter cannot survive.
 *
 * A debit the player cannot afford is rejected rather than clamped, and reported back
 * so the client can drop it. Clamping would silently give away whatever the shortfall
 * was.
 */
export const submitSpends = onCall(callOptions, async (request): Promise<{
  wallets: WalletReply[];
  rejected: string[];
}> => {
  const uid = requireUid(request);
  const db = getFirestore();

  const submitted = (request.data?.spends ?? []) as SubmittedSpend[];
  if (!Array.isArray(submitted)) {
    throw new HttpsError("invalid-argument", "spends must be a list");
  }
  if (submitted.length > MAX_SPENDS_PER_CALL) {
    throw new HttpsError("invalid-argument", `at most ${MAX_SPENDS_PER_CALL} spends per call`);
  }

  const clean = submitted.flatMap((spend) => {
    const id = typeof spend.id === "string" ? spend.id : "";
    const currency = typeof spend.currency === "string" ? spend.currency : "credits";
    const amount = typeof spend.amount === "number" ? Math.floor(spend.amount) : 0;

    if (!id || id.length > 64) return [];
    if (!CURRENCIES.includes(currency as CurrencyId)) return [];
    if (amount <= 0) return [];

    return [{
      id,
      currency: currency as CurrencyId,
      amount,
      unix: typeof spend.unix === "number" ? Math.floor(spend.unix) : 0,
      reason: typeof spend.reason === "string" ? spend.reason.slice(0, 64) : "",
    }];
  });

  const confirmed: Record<string, string[]> = {};
  const rejected: string[] = [];

  const wallet = await db.runTransaction(async (transaction) => {
    const config = await loadProgressionConfig(transaction);

    const walletRef = db.doc(PATHS.wallet(uid));
    const walletSnapshot = await transaction.get(walletRef);
    const state = readWallet(walletSnapshot, config);

    // Every read has to happen before the first write, so the existing spend records,
    // the save, and the balances are all gathered up front. The save is read once
    // rather than once per currency.
    const existing = await Promise.all(
      clean.map((spend) => transaction.get(db.doc(PATHS.spend(uid, spend.id))))
    );

    const earned = await deriveEarned(transaction, uid, state, config);

    const balances: Partial<Record<CurrencyId, number>> = {};
    for (const currency of CURRENCIES) {
      balances[currency] = spendableBalance(currency, state, earned);
    }

    for (let i = 0; i < clean.length; i++) {
      const spend = clean[i];
      const already = existing[i];

      (confirmed[spend.currency] ??= []);

      if (already.exists) {
        // Seen before. The debit is already inside `spent`; confirming it again tells
        // the client to stop sending it, and changes no balance.
        confirmed[spend.currency].push(spend.id);
        continue;
      }

      const available = balances[spend.currency] ?? 0;
      if (available < spend.amount) {
        rejected.push(spend.id);
        logger.warn("refused an unaffordable debit", {
          uid, spendId: spend.id, amount: spend.amount, available,
        });
        continue;
      }

      transaction.set(db.doc(PATHS.spend(uid, spend.id)), {
        currency: spend.currency,
        amount: spend.amount,
        reason: spend.reason,
        clientUnix: spend.unix,
        appliedAt: FieldValue.serverTimestamp(),
      });

      state[spend.currency].spent += spend.amount;
      balances[spend.currency] = available - spend.amount;

      if (spend.unix > state[spend.currency].confirmedThroughUnix) {
        state[spend.currency].confirmedThroughUnix = spend.unix;
      }

      confirmed[spend.currency].push(spend.id);
    }

    transaction.set(walletRef, { ...state, updatedAt: FieldValue.serverTimestamp() });
    return state;
  });

  return { wallets: toReply(wallet, confirmed), rejected };
});

// --------------------------------------------------------------- claim awards
interface SubmittedAward {
  id?: unknown;
  claimedAmount?: unknown;
  unix?: unknown;
  reason?: unknown;
}

/**
 * A submitted award that has survived parsing, tagged with how it will be adjudicated.
 *
 * The currency rides on the claim, and only because both claimable award kinds name the
 * ledger they pay into. An ad id does not — the placement decides, and the placement's
 * payout lives in server config — which is why an ad is never claimed here at all. That
 * asymmetry is the point: a client cannot watch the heart video and ask to be paid in gems,
 * because at no point does it get to say "gems".
 *
 * Two shapes rather than one because the two are adjudicated by different evidence. A chest
 * is *recomputed* — the server re-rolls it and the answer is not a matter of opinion. A
 * streak night cannot be recomputed by anybody, so it is *bounded* instead, against a floor
 * this server owns. See `streak.ts`.
 */
type CleanCommon = {
  id: string;
  claimedAmount: number;
  unix: number;
  reason: string;
  currency: CurrencyId;
};

type CleanAward =
  | (CleanCommon & { kind: "daily"; claim: DailyClaim })
  | (CleanCommon & { kind: "streak"; claim: StreakClaim });

export const claimAwards = onCall(callOptions, async (request): Promise<{
  wallets: WalletReply[];
  rejected: string[];
}> => {
  const uid = requireUid(request);
  const db = getFirestore();

  const submitted = (request.data?.awards ?? []) as SubmittedAward[];
  if (!Array.isArray(submitted)) {
    throw new HttpsError("invalid-argument", "awards must be a list");
  }
  if (submitted.length > MAX_AWARDS_PER_CALL) {
    throw new HttpsError("invalid-argument", `at most ${MAX_AWARDS_PER_CALL} awards per call`);
  }

  const today = todayKey(Date.now());
  const rejected: string[] = [];

  const clean = submitted.flatMap((award): CleanAward[] => {
    const id = typeof award.id === "string" ? award.id : "";
    if (!id) return [];

    const common = {
      id,
      claimedAmount: typeof award.claimedAmount === "number" ? Math.floor(award.claimedAmount) : 0,
      unix: typeof award.unix === "number" ? Math.floor(award.unix) : 0,
      reason: typeof award.reason === "string" ? award.reason.slice(0, 64) : "",
    };

    // Ad grants are never claimed. They are paid outright by `adReward` when the network
    // vouches for the view, because the client holds no token the callback echoes back —
    // see `adGrantId`. So an `ad:` id arriving here is either a client from a build that
    // predates that decision or somebody inventing one, and both are refused rather than
    // left pending: a claim that will never confirm is a claim resubmitted forever.
    if (isAdGrantId(id)) {
      logger.warn("refused an ad award submitted as a claim", { uid, id });
      rejected.push(id);
      return [];
    }

    if (isStreakGrantId(id)) {
      const streak = parseStreakClaim(id);
      if (!streak) { rejected.push(id); return []; }

      if (!CURRENCIES.includes(streak.currency as CurrencyId)) { rejected.push(id); return []; }

      if (streak.dayKey > today + MAX_STREAK_DAYS_AHEAD ||
          streak.dayKey < today - MAX_STREAK_DAYS_BEHIND) {
        logger.warn("refused a streak night dated outside the window", {
          uid, id, claimedDay: streak.dayKey, today,
        });
        rejected.push(id);
        return [];
      }

      return [{ ...common, kind: "streak", claim: streak, currency: streak.currency as CurrencyId }];
    }

    const claim = parseDailyClaim(id);
    if (!claim) { rejected.push(id); return []; }

    if (!CURRENCIES.includes(claim.currency as CurrencyId)) { rejected.push(id); return []; }

    if (claim.dayKey > today + MAX_DAYS_AHEAD || claim.dayKey < today - MAX_DAYS_BEHIND) {
      logger.warn("refused an award for a day outside the window", {
        uid, id, claimedDay: claim.dayKey, today,
      });
      rejected.push(id);
      return [];
    }

    return [{ ...common, kind: "daily", claim, currency: claim.currency as CurrencyId }];
  });

  // Oldest first. Streak nights are judged against a floor that moves as each one is paid,
  // so a batch that arrived out of order — a device sending a backlog, two devices merging
  // — would otherwise have night six judged against a floor night seven had already
  // raised. Daily chests are order-independent and ride along harmlessly.
  clean.sort((a, b) => a.claim.dayKey - b.claim.dayKey);

  const confirmed: Record<string, string[]> = {};

  const wallet = await db.runTransaction(async (transaction) => {
    const config = await loadProgressionConfig(transaction);

    const walletRef = db.doc(PATHS.wallet(uid));
    const walletSnapshot = await transaction.get(walletRef);
    const state = readWallet(walletSnapshot, config);

    // Every read before the first write, as Firestore requires. The ad proofs are
    // gathered in the same pass — an ad claim is only ever granted when the network's own
    // callback has already written one of these, and that read cannot happen later.
    const existing = await Promise.all(
      clean.map((award) => transaction.get(db.doc(PATHS.grant(uid, award.id))))
    );

    // The player's own account of their streak, fetched only when a streak night is being
    // claimed and used only to log a disagreement — see `saveSupports`. Nothing is refused
    // on it, so this is an extra read in the name of being able to explain a support
    // ticket, not part of the decision.
    const wantsStreak = clean.some((award) => award.kind === "streak");
    const saved = wantsStreak
      ? readSavedStreak((await transaction.get(db.doc(PATHS.player(uid)))).data())
      : null;

    await deriveEarned(transaction, uid, state, config);   // ratchets the floor

    const daily = usableDailyConfig((config as { daily?: unknown }).daily);
    const ladder = usableStreakConfig((config as { streak?: unknown }).streak);

    // Where this server last paid a streak night to. Threaded through the loop rather than
    // re-read, because a batch pays several nights and each one moves it.
    let floor = state.streak ?? { paidThroughDay: 0, paidNight: 0 };

    for (let i = 0; i < clean.length; i++) {
      const award = clean[i];
      (confirmed[award.currency] ??= []);

      if (existing[i].exists) {
        // Seen before. It is already inside `granted`; saying so again tells the client
        // to stop sending it and moves no balance.
        confirmed[award.currency].push(award.id);
        continue;
      }

      // A config that predates the block this award needs, or one seeded badly. Granting a
      // guess would be inventing money, so the award is neither granted nor rejected: the
      // client keeps its local copy and tries again after the seeder has run. Rejecting
      // would be worse than doing nothing — it throws away a reward the player earned.
      const table = award.kind === "streak" ? ladder : daily;

      if (!table) {
        logger.error(`config/progression has no usable ${award.kind} table; leaving the ` +
                     "award unconfirmed rather than granting or discarding it", { uid, id: award.id });
        continue;
      }

      let amount = 0;
      let detail: Record<string, unknown>;

      if (award.kind === "streak") {
        // The whole of the security, in one call. A night may only climb as fast as the
        // calendar climbs; see `advances`. Refused rather than left pending, because a
        // claim that fails this will fail it for ever and a pending claim is resubmitted
        // for the life of the account.
        if (!advances(floor, award.claim.dayKey, award.claim.night)) {
          logger.warn("refused a streak night that outruns the calendar", {
            uid, id: award.id, night: award.claim.night, day: award.claim.dayKey,
            paidNight: floor.paidNight, paidThroughDay: floor.paidThroughDay,
          });
          rejected.push(award.id);
          continue;
        }

        if (saved && !saveSupports(saved, award.claim.dayKey, award.claim.night)) {
          // Worth seeing, never worth acting on. Usually a device that has not pushed its
          // save yet, or a night collected before a streak lapsed and restarted.
          logger.info("a streak night does not match the save's own dates", {
            uid, id: award.id, night: award.claim.night, day: award.claim.dayKey,
            startDay: saved.startDay, lastPlayedDay: saved.lastPlayedDay,
          });
        }

        amount = streakCurrencyValue(ladder!, award.claim.night, award.currency);
        detail = { night: award.claim.night };
      } else {
        if (award.claim.chestIndex >= daily!.chests.length) {
          rejected.push(award.id);
          continue;
        }

        amount = chestCurrencyValue(
          daily!, uid, award.claim.dayKey, award.claim.chestIndex, award.currency);
        detail = { chestIndex: award.claim.chestIndex };
      }

      if (amount <= 0) {
        // The rung, or the chest, holds none of this currency. Either the client is on a
        // table this server has not been seeded with — a content push it fetched first —
        // or the claim is invented. Either way there is nothing to grant, and the client
        // will adopt the server's balance on this same reply.
        logger.warn("award names a reward that pays nothing in that currency", {
          uid, id: award.id, currency: award.currency,
        });
        rejected.push(award.id);
        continue;
      }

      if (amount !== award.claimedAmount) {
        // Worth seeing, never worth acting on. A mismatch is usually a client on an
        // older table; the server's figure stands either way.
        logger.info("award amount differs from the client's claim", {
          uid, id: award.id, server: amount, client: award.claimedAmount,
        });
      }

      transaction.set(db.doc(PATHS.grant(uid, award.id)), {
        currency: award.currency,
        amount,
        claimedAmount: award.claimedAmount,
        reason: award.reason,
        dayKey: award.claim.dayKey,
        clientUnix: award.unix,
        grantedAt: FieldValue.serverTimestamp(),
        ...detail,
      });

      state[award.currency].granted += amount;
      confirmed[award.currency].push(award.id);

      // Raised inside the same transaction that moves the money, so a night cannot be
      // paid without the floor moving with it.
      if (award.kind === "streak") floor = raise(floor, award.claim.dayKey, award.claim.night);
    }

    state.streak = floor;

    transaction.set(walletRef, { ...state, updatedAt: FieldValue.serverTimestamp() });
    return state;
  });

  return { wallets: toReply(wallet, {}, confirmed), rejected };
});

// ------------------------------------------------------- rewarded ad callback
/**
 * The ad network telling us, from its own servers, that somebody watched a video.
 *
 * <p>
 * This is the only evidence in the system that a rewarded ad happened, and it is the
 * reason invariant 10 survives a feature that hands out currency for watching something.
 * The client's claim is a prediction; this is the fact.
 * </p>
 * <p>
 * It is an `onRequest` rather than an `onCall` because the caller is LevelPlay, not a
 * Firebase client — there is no auth context, and the request is authenticated entirely by
 * the MD5 signature over the query string. That is a weaker primitive than we would
 * choose, but it is the one the network offers, and the blast radius is bounded: the worst
 * a forged callback achieves is granting one placement's configured amount to an account
 * the attacker names, and forging it requires the shared secret.
 * </p>
 * <p>
 * <b>It grants nothing.</b> It writes one document and returns. LevelPlay wants an answer
 * inside 400ms before it starts retrying, and a wallet transaction is not a 400ms
 * operation — but more than that, splitting the two means the write here is a single
 * idempotent `set` that is safe to repeat as many times as the network chooses to retry.
 * The money moves later, in `claimAwards`, under the same transaction discipline as
 * everything else.
 * </p>
 * <p>
 * Every non-retryable outcome still answers 200 with the acknowledgement. LevelPlay retries
 * until it sees `EVENT_ID:OK`, so answering anything else to a request we will never accept
 * — a bad signature, a probe — buys an indefinite retry loop against this endpoint.
 * </p>
 */
export const adReward = onRequest(
  { region: REGION, secrets: [LEVELPLAY_SECRET], cors: false },
  async (request, response) => {
    const query = request.query as Record<string, string | undefined>;

    const eventId = typeof query.eventId === "string" ? query.eventId : "";

    const verdict = verifyAdCallback(
      {
        eventId,
        userId: query.userId,
        rewards: query.rewards,
        timestamp: query.timestamp,
        signature: query.signature,
        placement: query.placement,
        placementName: query.placementName,
        itemName: query.itemName,
      },
      configured(LEVELPLAY_SECRET)
    );

    if (!verdict.ok) {
      logger.error("rewarded ad callback refused", {
        reason: verdict.reason, retryable: verdict.retryable, eventId,
      });

      // Retryable means the fault is ours — an unconfigured secret — and a retry might
      // genuinely succeed once it is fixed, so the reward is not thrown away.
      if (verdict.retryable) { response.status(503).send("retry"); return; }

      response.status(200).send(ackBody(eventId));
      return;
    }

    const db = getFirestore();
    const grantRef = db.doc(PATHS.grant(verdict.uid, adGrantId(verdict.eventId)));

    try {
      const granted = await db.runTransaction(async (transaction) => {
        const config = await loadProgressionConfig(transaction);

        const already = await transaction.get(grantRef);
        const walletRef = db.doc(PATHS.wallet(verdict.uid));
        const walletSnapshot = await transaction.get(walletRef);
        const state = readWallet(walletSnapshot, config);

        // A retry of a callback we have already paid. LevelPlay repeats until it is
        // acknowledged, so this is the ordinary path, not an anomaly.
        if (already.exists) return 0;

        const ads = usableAdConfig((config as { ads?: unknown }).ads);
        if (!ads) {
          logger.error("config/progression has no usable ads table; cannot pay a confirmed view", {
            uid: verdict.uid, placement: verdict.placement,
          });
          throw new Error("no ads config");     // 503, so the network retries after seeding
        }

        const currency = adCurrencyOf(ads, verdict.placement);

        // A placement that pays hearts or a boost is applied by the client and has no
        // server side. The callback still arrives and is still acknowledged — there is
        // simply nothing to grant, and saying so is not an error.
        if (!currency || !CURRENCIES.includes(currency as CurrencyId)) return 0;

        const amount = adCurrencyValue(ads, verdict.placement, currency);
        if (amount <= 0) return 0;

        transaction.set(grantRef, {
          currency,
          amount,
          reason: "rewarded_ad",
          placement: verdict.placement,
          eventId: verdict.eventId,
          network: typeof query.adNetwork === "string" ? query.adNetwork.slice(0, 64) : "",
          grantedAt: FieldValue.serverTimestamp(),
        });

        state[currency as CurrencyId].granted += amount;
        transaction.set(walletRef, { ...state, updatedAt: FieldValue.serverTimestamp() });

        return amount;
      });

      logger.info("rewarded ad confirmed", {
        uid: verdict.uid, placement: verdict.placement, eventId: verdict.eventId, granted,
      });
    } catch (error) {
      // A write that failed must be retried, or the player watched an ad for nothing.
      logger.error("could not pay a rewarded ad callback", {
        uid: verdict.uid, eventId: verdict.eventId, error: String(error),
      });
      response.status(503).send("retry");
      return;
    }

    response.status(200).send(ackBody(verdict.eventId));
  }
);

// ------------------------------------------------------------ redeem purchase
/**
 * Validates a store receipt and grants what the product catalog says it is worth.
 *
 * Two properties are doing the work here.
 *
 * The amount comes from `config/products` on the server, never from the request. A
 * client that names its own reward names any number it likes.
 *
 * The grant is keyed on a global receipt document, not a per-player one. Receipt
 * replay across accounts is an automated, industrialised attack: one genuine purchase
 * funding thousands of accounts. Keying per player would validate every one of them.
 */
export const redeemPurchase = onCall(
  {
    ...callOptions,
    secrets: [APPLE_KEY_ID, APPLE_ISSUER_ID, APPLE_PRIVATE_KEY, GOOGLE_PLAY_SERVICE_ACCOUNT],
  },
  async (request): Promise<{ wallets: WalletReply[]; granted: number; currency: string }> => {
    const uid = requireUid(request);
    const db = getFirestore();

    // Validated before the transaction: it is a network call to a store, and holding a
    // Firestore transaction open across one would be a fine way to melt the database.
    let purchase;
    try {
      purchase = await validateReceipt(request.data?.receipt ?? {}, {
        appleKeyId: configured(APPLE_KEY_ID),
        appleIssuerId: configured(APPLE_ISSUER_ID),
        applePrivateKey: configured(APPLE_PRIVATE_KEY),
        googleServiceAccount: configured(GOOGLE_PLAY_SERVICE_ACCOUNT),
        bundleId: BUNDLE_ID,
      });
    } catch (error) {
      if (error instanceof ReceiptRejected) {
        logger.warn("receipt rejected", { uid, message: error.message });
        throw new HttpsError("permission-denied", error.message);
      }
      logger.error("receipt validation failed", { uid, error: String(error) });
      throw new HttpsError("unavailable", "could not reach the store to validate this purchase");
    }

    const products = (await db.doc(PATHS.productsConfig).get()).data() as
      | Record<string, { currency?: string; amount?: number }>
      | undefined;

    const product = products?.[purchase.productId];
    if (!product || typeof product.amount !== "number" || product.amount <= 0) {
      logger.error("a valid purchase names a product this server does not sell", {
        uid, productId: purchase.productId,
      });
      throw new HttpsError("failed-precondition", `product ${purchase.productId} is not configured`);
    }

    const currency = (product.currency ?? "credits") as CurrencyId;
    if (!CURRENCIES.includes(currency)) {
      throw new HttpsError("failed-precondition", `product ${purchase.productId} names an unknown currency`);
    }

    const amount = Math.floor(product.amount);
    const receiptRef = db.doc(PATHS.receipt(purchase.store, purchase.transactionId));

    const result = await db.runTransaction(async (transaction) => {
      const config = await loadProgressionConfig(transaction);

      const receiptSnapshot = await transaction.get(receiptRef);
      const walletRef = db.doc(PATHS.wallet(uid));
      const walletSnapshot = await transaction.get(walletRef);
      const state: WalletDoc = readWallet(walletSnapshot, config);

      if (receiptSnapshot.exists) {
        const claim = receiptSnapshot.data() as { uid?: string };

        if (claim.uid !== uid) {
          // A real transaction, already spent on another account. This is the replay
          // attack, and it is the reason the key is global.
          logger.error("receipt replay across accounts refused", {
            uid, claimedBy: claim.uid, transactionId: purchase.transactionId,
          });
          throw new HttpsError("permission-denied", "this purchase has already been redeemed");
        }

        // Same player, same transaction: a retry. Report success without granting
        // again, so a lost response cannot cost the player their purchase or hand
        // them a second one.
        return { state, granted: 0 };
      }

      transaction.set(receiptRef, {
        uid,
        store: purchase.store,
        productId: purchase.productId,
        currency,
        amount,
        sandbox: purchase.sandbox,
        purchasedAt: Timestamp.fromMillis(purchase.purchasedAtMillis),
        grantedAt: FieldValue.serverTimestamp(),
      });

      state[currency].granted += amount;
      transaction.set(walletRef, { ...state, updatedAt: FieldValue.serverTimestamp() });

      return { state, granted: amount };
    });

    logger.info("purchase redeemed", {
      uid, store: purchase.store, productId: purchase.productId,
      granted: result.granted, sandbox: purchase.sandbox,
    });

    return { wallets: toReply(result.state, {}), granted: result.granted, currency };
  }
);

/**
 * Republishes the population's move counts, once a day.
 *
 * The only scheduled function in the project, and the only one that reads other people's
 * saves. It writes one public document — nine numbers per glade, aggregated over thousands
 * of players, with no identifier of any kind in it — which the client fetches from the
 * splash to draw a single line on the victory panel.
 *
 * Nothing depends on it. If it never runs, the line is never drawn and every other part of
 * the game behaves identically, which is why it can afford to be a plain daily job with no
 * retry policy and no alerting.
 *
 * Three in the morning UTC is deliberately the quietest hour for a globally distributed
 * player base, and the read is bounded to a sample — see `stats.ts` for why an exact
 * running count would be the wrong trade.
 */
export const publishGroveStats = onSchedule(
  { region: REGION, schedule: "0 3 * * *", timeZone: "Etc/UTC", timeoutSeconds: 540 },
  async () => {
    const { levels, saves } = await rebuildStats();
    logger.info("grove stats rebuilt", { levels, saves });
  }
);
