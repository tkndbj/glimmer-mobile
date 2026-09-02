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
import { grantEntries, readProduct } from "./products";
import { ReceiptRejected, lookupAppleTransaction, validateReceipt } from "./receipts";
import {
  listVoidedPurchases, recordSweep, revokeReceipt, sweepWindow, transactionIdsIn,
} from "./refunds";
import { rebuildStats } from "./stats";
import {
  DEFAULT_KEEPER_CURVE, GROVE_PATHS, GroveCardDoc, KeeperCurve,
  assertUsableGroveConfig, buildCard, derivedXp, groveWorth, keeperLevel,
  optedIn, rebuildGroveRanks, saveRevision, withdrawCard,
} from "./grove";
import {
  ClaimOutcome, NameHolding, RENAME_COOLDOWN_SECONDS, claimName as claimName_, heldName, nameKey, publishableName,
} from "./names";
import { loadNameConfig } from "./blocklist";
import {
  AppleRevocationKeys, accountExists, deleteAccount as deleteAccount_, usableAppleKeys,
} from "./account";
import { reportName, ReportOutcome } from "./reports";
import {
  WHEEL_MIN_PERCENT, applyWheelPercent, readWheelPosition, usableWheelConfig, wheelPercent,
} from "./wheel";
import { earnedCredits } from "./progression";
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

/**
 * Sign in with Apple, for revoking a grant when an account is deleted.
 *
 * <b>Four more secrets rather than reusing the three above, and that is not duplication.</b>
 * `APPLE_KEY_ID` / `APPLE_ISSUER_ID` / `APPLE_PRIVATE_KEY` are the **App Store Server API**
 * credentials — they sign questions about purchases and refunds. Sign in with Apple is a
 * separate key, created under a separate heading in the developer portal, with a team id
 * rather than an issuer id and a client id rather than nothing. Using one set for the other
 * fails with `invalid_client`, which names neither and reads like a malformed request.
 *
 * All four absent is a supported state: deletion still happens, and the report says the
 * revocation was skipped. That is deliberate — a deployment must never be unable to delete an
 * account because a credential is missing.
 */
const APPLE_SIWA_KEY_ID = defineSecret("APPLE_SIWA_KEY_ID");
const APPLE_SIWA_TEAM_ID = defineSecret("APPLE_SIWA_TEAM_ID");
const APPLE_SIWA_CLIENT_ID = defineSecret("APPLE_SIWA_CLIENT_ID");
const APPLE_SIWA_PRIVATE_KEY = defineSecret("APPLE_SIWA_PRIVATE_KEY");

/** The four read together, or null when the deployment has not been given them. */
function appleSignInKeys(): AppleRevocationKeys | null {
  return usableAppleKeys({
    keyId: configured(APPLE_SIWA_KEY_ID),
    teamId: configured(APPLE_SIWA_TEAM_ID),
    clientId: configured(APPLE_SIWA_CLIENT_ID),
    privateKey: configured(APPLE_SIWA_PRIVATE_KEY),
  });
}

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
  // Trimmed, and the byte-order mark is stripped by name. Secret Manager stores exactly
  // the bytes it is given, and the two ordinary ways of setting one — a file written by a
  // Windows editor, or a shell that appends a newline — both produce a value that is not
  // equal to the sentinel. That turns "not configured" into "configured with garbage":
  // the placeholder would be handed to JSON.parse as if it were a service account, and the
  // clear "validation is not configured on this deployment" refusal would be replaced by an
  // unexplained parse failure. Observed live on GOOGLE_PLAY_SERVICE_ACCOUNT, which was
  // holding a BOM followed by UNSET.
  //
  // Trimming is safe for all four: two are opaque identifiers, one is a JSON document, and
  // a PEM is unaffected by surrounding whitespace.
  const value = secret.value()?.replace(/^﻿/, "").trim();
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

    // Nobody to pay. A verified callback for a deleted account is not an error and not a
    // fault the network can fix by repeating it — the player asked for the account to be
    // erased between starting the video and the callback arriving, which is a real if narrow
    // window. Acknowledged rather than retried, because everything below writes with `set`
    // and would otherwise recreate `grantLog` and the wallet under a uid that can never
    // authenticate again: an orphan nothing reads, nothing cleans up and no query can find.
    //
    // One Auth lookup per confirmed view. Affordable exactly here and nowhere on the sync
    // path — see `accountExists` for that distinction.
    if (!(await accountExists(verdict.uid))) {
      logger.warn("rewarded ad callback for a deleted account; nothing granted", {
        uid: verdict.uid, eventId: verdict.eventId, placement: verdict.placement,
      });
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

        const base = adCurrencyValue(ads, verdict.placement, currency);
        if (base <= 0) return 0;

        // The bonus wheel, and it applies to exactly one placement. A view of `win_bonus` is
        // spun for on the phone before the video plays; the slice is a pure function of
        // (account, day, spin index), so this arrives at the same figure without being told
        // it — see `wheel.ts`. Every other placement pays flat and takes no position.
        //
        // The index comes from this document rather than from the client, because it decides
        // money. It advances only here, inside the transaction guarded by the grant record
        // above, so a retried callback collides with a document that already exists and the
        // wheel does not turn twice for one video.
        const spinning = verdict.placement === "win_bonus";
        const wheel = spinning
          ? usableWheelConfig((config as { ads?: { wheel?: unknown } }).ads?.wheel)
          : null;

        const today = todayKey(Date.now());
        const position = readWheelPosition(state.wheel, today);

        const percent = spinning
          ? wheelPercent(verdict.uid, position.day, position.spins, wheel)
          : WHEEL_MIN_PERCENT;

        const amount = applyWheelPercent(base, percent);

        transaction.set(grantRef, {
          currency,
          amount,
          reason: "rewarded_ad",
          placement: verdict.placement,
          eventId: verdict.eventId,
          network: typeof query.adNetwork === "string" ? query.adNetwork.slice(0, 64) : "",

          // Kept so a support reply can say what a view was worth and why, without re-deriving
          // it from a seed and a day that have both moved on since.
          basis: base,
          percent,
          spinIndex: spinning ? position.spins : -1,

          grantedAt: FieldValue.serverTimestamp(),
        });

        state[currency as CurrencyId].granted += amount;

        // Written whether or not this view spun, so the rollover lands: a day whose only
        // views were heart refills still has to leave today's key behind, or the next win
        // bonus would seed its first spin from a day the client has long since left.
        state.wheel = spinning
          ? { day: position.day, spins: position.spins + 1 }
          : position;

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
 * Three properties are doing the work here, and each closes a different attack.
 *
 * **The amount comes from `config/products` on the server, never from the request.** A
 * client that names its own reward names any number it likes, and this is the one place
 * where the number is backed by a real payment and therefore impossible to argue with
 * after the fact.
 *
 * **The grant is keyed on a global receipt document, not a per-player one.** Receipt
 * replay across accounts is an automated, industrialised attack: one genuine purchase
 * funding thousands of accounts. Keying per player would validate every one of them.
 *
 * **A retry reports success and grants nothing.** The client cannot finish a transaction
 * with the store until this returns, so a lost reply is redeemed again on the next
 * launch — every time, for as long as it takes. That is only safe because the second
 * attempt collides with a document that already exists.
 *
 * A product may grant more than one currency, because a bundle does. What it may never
 * grant is a stored *amount*: hearts and boosts live in the player's save file and are
 * applied by the phone, so a product that promised them would need the client to apply
 * half a purchase after the server applied the other half — and a record of "did I already
 * apply this transaction's hearts" is a new field in the save whose failure mode is
 * somebody paying and receiving nothing. Hearts are bought with gems instead.
 *
 * The one non-currency thing a product may grant is a **heart capacity**, and it is the
 * exception the paragraph above defines rather than one it forgot. A capacity arrives as
 * the union of a permanent product id, so applying it twice is applying it once and the
 * record has nothing to answer — which is why the entitlement can live entirely on the
 * client and be recovered by a Restore, with nothing here to grant. What this server owns
 * is the reversal: a refunded receipt is revoked, the container id is recorded on the
 * account's wallet, and every wallet reply carries it back so the phone stops honouring
 * something nobody paid for. See `StoreProduct.HeartCapacity` on the client for the
 * argument in full.
 */
export const redeemPurchase = onCall(
  {
    ...callOptions,
    secrets: [APPLE_KEY_ID, APPLE_ISSUER_ID, APPLE_PRIVATE_KEY, GOOGLE_PLAY_SERVICE_ACCOUNT],
  },
  async (request): Promise<{
    wallets: WalletReply[];
    granted: Record<string, number>;
    alreadyGranted: boolean;
  }> => {
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

    const products = (await db.doc(PATHS.productsConfig).get()).data();

    let grant;
    try {
      grant = readProduct(products, purchase.productId);
    } catch (error) {
      // Loud, and deliberately so. This is a real payment the game cannot honour, and
      // the client will not finish the transaction — so on Google it is refunded
      // automatically in three days. Adding the product to config/products and re-seeding
      // before then turns it back into an ordinary purchase with no support case at all.
      logger.error("a valid purchase names a product this server does not sell", {
        uid, productId: purchase.productId, store: purchase.store,
        message: error instanceof Error ? error.message : String(error),
      });
      throw new HttpsError(
        "failed-precondition",
        `product ${purchase.productId} is not configured`
      );
    }

    const entries = grantEntries(grant);
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
          //
          // `already-exists` rather than `permission-denied`, and the distinction is the one
          // refusal in this file that is **permanently** true. Invariant 13a's rule is that a
          // claim must never be refused for a reason that will still hold tomorrow, because
          // the client resubmits for ever — and everything else `redeemPurchase` refuses is
          // temporary by design: a product missing from `config/products` is fixed by a seed, a
          // bad signature by a retry against a redeployed function. This one can never change.
          // The receipt document is never deleted (account deletion keeps it deliberately — see
          // invariant 27) and `uid` is never rewritten, so no future state makes this caller the
          // owner. Saying so in the error code is what lets the client stop asking.
          //
          // It matters more than a wasted call. On Google an unacknowledged purchase is
          // auto-refunded after three days, and `sweepVoidedPurchases` then reverses the grant
          // against `receipt.uid` — the account that *legitimately* paid. So a client that can
          // never finish this transaction eventually costs the original buyer the currency they
          // bought, on an account that did nothing wrong. Reachable by switching accounts, and
          // now also by deleting one.
          logger.error("receipt replay across accounts refused", {
            uid, claimedBy: claim.uid, transactionId: purchase.transactionId,
          });
          throw new HttpsError("already-exists", "this purchase belongs to another account");
        }

        // Same player, same transaction: a retry. Report success without granting again,
        // so a lost response cannot cost the player their purchase or hand them a second
        // one. The client reads `alreadyGranted` and skips the celebration rather than
        // congratulating somebody for reopening the app.
        //
        // The wallet goes back with it, and for a container that is the whole of what the
        // reply has to carry: the entitlement lives in the client's save and is re-applied
        // from the product id on every successful redemption, so a re-delivery after a
        // reinstall restores it without this server granting anything twice.
        return { state, granted: {} as Record<string, number>, already: true };
      }

      const granted: Record<string, number> = {};

      for (const [currency, amount] of entries) {
        state[currency].granted += amount;
        granted[currency] = amount;
      }

      // Buying a container again after a refund lifts the revocation, in the transaction
      // that grants it. Doing it anywhere else would leave a window in which the server was
      // paid for a container it was still telling the client to take away.
      if (grant.capacity > 0 && (state.containersRevoked ?? []).includes(purchase.productId)) {
        state.containersRevoked =
          (state.containersRevoked ?? []).filter((id) => id !== purchase.productId);
      }

      transaction.set(receiptRef, {
        uid,
        store: purchase.store,
        productId: purchase.productId,
        kind: grant.kind,

        // Recorded so a refund can be reversed without re-reading the product catalog,
        // which may have been re-seeded by then. `revokeReceipt` reads exactly this.
        capacity: grant.capacity,
        granted,
        sandbox: purchase.sandbox,
        purchasedAt: Timestamp.fromMillis(purchase.purchasedAtMillis),
        grantedAt: FieldValue.serverTimestamp(),

        // Written now so a later revocation can find the wallet to reverse without a
        // second lookup, and so a refunded receipt is distinguishable from one that was
        // never granted. See `revokeReceipt`.
        revokedAt: null,
      });

      transaction.set(walletRef, { ...state, updatedAt: FieldValue.serverTimestamp() });

      return { state, granted, already: false };
    });

    logger.info("purchase redeemed", {
      uid, store: purchase.store, productId: purchase.productId,
      granted: result.granted, capacity: grant.capacity,
      already: result.already, sandbox: purchase.sandbox,
    });

    return {
      wallets: toReply(result.state, {}),
      granted: result.granted,
      alreadyGranted: result.already,
    };
  }
);

// ------------------------------------------------------------------- refunds
/**
 * App Store Server Notifications V2. Apple tells us a transaction changed.
 *
 * <p><b>Nothing in the request is trusted.</b> The body is scraped for transaction ids,
 * each one is matched against a receipt this server actually granted, and only then is
 * Apple asked — over TLS, with a key only we hold — whether that transaction has been
 * revoked. Apple's own answer is what moves the money. So the worst a forged POST can do
 * is make this server look up a handful of transactions and be told they are fine.</p>
 *
 * <p>That is why there is no JWS chain verification here, and it is a deliberate position
 * rather than a shortcut: verifying the x5c chain would mean shipping and rotating
 * Apple's root certificate, or taking a dependency that does, inside the one code path
 * that reverses money — for a guarantee already obtained from the lookup. See
 * `transactionIdsIn`, which states the condition this rests on.</p>
 *
 * <p>Always answers 200. Apple retries a non-200 for hours and then gives up; since this
 * function decides for itself what is true, a body it could not read is nothing to retry.
 * A lookup that genuinely failed is caught by the next notification for the same
 * transaction, and by nothing else — which is acceptable because Apple sends several over
 * the life of a refund.</p>
 *
 * <p>Set the URL in App Store Connect ▸ App Information ▸ App Store Server Notifications,
 * for both the production and the sandbox environment.</p>
 */
export const appleNotification = onRequest(
  {
    region: REGION,
    secrets: [APPLE_KEY_ID, APPLE_ISSUER_ID, APPLE_PRIVATE_KEY],
    // Apple does not need one and this endpoint is not a browser destination.
    cors: false,
  },
  async (request, response) => {
    const keyId = configured(APPLE_KEY_ID);
    const issuerId = configured(APPLE_ISSUER_ID);
    const privateKey = configured(APPLE_PRIVATE_KEY);

    if (!keyId || !issuerId || !privateKey) {
      logger.warn("an App Store notification arrived before Apple validation was configured");
      response.status(200).send("ok");
      return;
    }

    const body = typeof request.rawBody?.toString === "function"
      ? request.rawBody.toString("utf8")
      : JSON.stringify(request.body ?? {});

    const ids = transactionIdsIn(body);

    if (ids.length === 0) {
      logger.info("an App Store notification named no transaction");
      response.status(200).send("ok");
      return;
    }

    const db = getFirestore();
    const secrets = { keyId, issuerId, privateKey, bundleId: BUNDLE_ID };
    let revoked = 0;

    for (const transactionId of ids) {
      // Only ids this server has actually granted are worth a round trip. That bound is
      // what stops an unauthenticated endpoint being a way to make us call Apple all day.
      const receipt = await db.doc(PATHS.receipt("apple", transactionId)).get();
      if (!receipt.exists) continue;
      if ((receipt.data() as { revokedAt?: unknown })?.revokedAt) continue;

      try {
        const transaction = await lookupAppleTransaction(transactionId, secrets);
        if (!transaction.revocationDate) continue;

        if (await revokeReceipt("apple", transactionId, `apple_revocation_${transaction.revocationReason ?? 0}`)) {
          revoked++;
        }
      } catch (error) {
        logger.error("could not check an Apple transaction named by a notification", {
          transactionId, error: String(error),
        });
      }
    }

    if (revoked > 0) logger.warn("App Store notification reversed purchases", { revoked });

    response.status(200).send("ok");
  }
);

/**
 * Everything Google has voided since the last sweep, reversed.
 *
 * <p>A poll rather than a Pub/Sub subscription, and hourly rather than instant. Refunds
 * are not urgent — an hour of a refunded balance is not an exploit anybody can run at
 * scale — and the trade is worth naming: a subscription is a second piece of
 * infrastructure that can silently stop delivering, and nothing would notice until a
 * month of refunds had gone unreversed. A poll that stops running shows up in this
 * function's own logs, and its cursor means the first run after an outage catches up
 * rather than losing the gap.</p>
 *
 * <p>The Play service account needs the "View financial data" permission for this API. It
 * is a different permission from the one receipt validation uses, and a sweep returning
 * nothing for ever is exactly what a missing permission looks like — which is why the
 * count is logged on every run, including zero.</p>
 */
export const sweepVoidedPurchases = onSchedule(
  {
    region: REGION,
    schedule: "17 * * * *",
    timeZone: "Etc/UTC",
    timeoutSeconds: 300,
    secrets: [GOOGLE_PLAY_SERVICE_ACCOUNT],
  },
  async () => {
    const serviceAccount = configured(GOOGLE_PLAY_SERVICE_ACCOUNT);
    if (!serviceAccount) {
      logger.info("voided purchase sweep skipped: Google Play validation is not configured");
      return;
    }

    const now = Date.now();
    const since = await sweepWindow(now);

    let voided;
    try {
      voided = await listVoidedPurchases(serviceAccount, BUNDLE_ID, since);
    } catch (error) {
      // Not recorded as progress. The cursor stays where it was, so the next run reads
      // the same window again rather than skipping over whatever was voided during it.
      logger.error("could not list voided purchases", { error: String(error) });
      return;
    }

    let revoked = 0;
    for (const entry of voided) {
      if (await revokeReceipt("google", entry.orderId, `play_voided_${entry.reason}`)) revoked++;
    }

    await recordSweep(now, revoked, voided.length);

    logger.info("voided purchase sweep", { seen: voided.length, revoked, sinceMillis: since });
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

// ------------------------------------------------------------- the grove board
/**
 * Rebuilds this account's public grove card.
 *
 * <b>The request is empty and must stay empty.</b> Everything written here is recomputed
 * from the save this function opens with its own credentials — the worth, the stars, the
 * league, the keeper level and the name. A client that could hand any of those in would be
 * a client that could put any number it liked on a public leaderboard, which is the whole
 * of what `grove.ts` exists to prevent.
 *
 * Called by the client when the part of the grove a visitor can see has actually changed —
 * see `GrovePublishPolicy` for why that is a client decision and why it is safe to be one.
 * A player who never calls it is simply not on the board.
 */
export const publishGrove = onCall(callOptions, async (request): Promise<{
  card: GroveCardDoc | null;
  withdrawn?: boolean;
  /** The revision of the save the card was built from. See `saveRevision`. */
  revision: number;
}> => {
  const uid = requireUid(request);
  const db = getFirestore();

  const [saveSnapshot, groveSnapshot, walletSnapshot] = await Promise.all([
    db.doc(PATHS.player(uid)).get(),
    db.doc(GROVE_PATHS.groveConfig).get(),
    db.doc(PATHS.wallet(uid)).get(),
  ]);

  if (!saveSnapshot.exists) {
    // Nothing to publish and nothing to fix by trying again. Refused rather than failed, so
    // the client drops it instead of retrying for the life of the account (invariant 13a).
    throw new HttpsError("failed-precondition", "no save to publish");
  }

  const save = saveSnapshot.data() as Record<string, unknown>;

  // The opt-out is read off the save rather than taken from the request, so it is enforced
  // where a modified client cannot talk its way past it. Turning it off here also takes down
  // whatever is already published rather than merely declining to refresh it.
  if (!optedIn(save)) {
    await withdrawCard(uid);
    return { card: null, withdrawn: true, revision: saveRevision(save) };
  }

  const groveConfig = groveSnapshot.data();
  assertUsableGroveConfig(groveConfig);

  const config = await loadProgressionConfig();
  const curve = (config as { keeper?: KeeperCurve }).keeper ?? DEFAULT_KEEPER_CURVE;

  const level = keeperLevel(derivedXp(save.levels, config), curve);

  // The ceiling on the bought half: everything this account has ever legitimately had to
  // spend. Derived earnings plus whatever the server itself granted — never a number out of
  // the save. See the header of grove.ts for why it is generous rather than exact.
  //
  // `credits.granted` is the *stored* field. `grantedBaseline` is the name it is given on
  // the way out to a client (`toReply`), and reading that name here silently yielded zero —
  // so the ceiling was derived earnings alone, and every account seed, daily chest, streak
  // night, rewarded video and **real-money coin purchase** was left out of what a player
  // could be said to afford. Somebody who bought forty dollars of coins and spent them on
  // their grove would have been clamped to near nothing and ranked at the bottom. Nothing
  // could catch it but a live run: the unit vectors take `affordable` as a parameter, and a
  // clamp that is too tight looks exactly like a clamp that is working.
  const derived = earnedCredits(save.levels, config, uid, save.events);
  //
  // Typed rather than read off `DocumentData`, so the next person to reach for the reply's
  // name gets a compile error instead of a zero.
  const walletDoc = walletSnapshot.data() as Partial<WalletDoc> | undefined;
  const granted = Math.floor(Number(walletDoc?.credits?.granted ?? 0));
  const affordable = derived.credits + Math.max(0, granted);

  const nowUnix = Math.floor(Date.now() / 1000);
  const { list } = await loadNameConfig(db, nowUnix);

  // ------------------------------------------------------------------ the name
  //
  // The card's name comes from the reservation on the wallet, never from the save — see
  // `boardName` in grove.ts for why that is a security improvement and not merely plumbing.
  //
  // This is also where a rename made offline finally lands, and doing it here is what let the
  // whole feature ship with no new client-side retry state. `claimName` is called with the
  // interactive path's exact arguments; re-claiming the name already held is free and writes
  // nothing, so the common case — a settled name — costs one comparison of two strings and no
  // database work at all. A claim that fails leaves the previous name standing, which is the
  // right outcome for something nobody is watching.
  let holding: NameHolding | null = heldName(walletDoc as Record<string, unknown> | undefined);
  const stored = (save.wallet as Record<string, unknown> | undefined)?.displayName;
  const wanted = nameKey(stored);

  if (wanted.length > 0 && holding?.key !== wanted) {
    try {
      const claim = await claimName_(db, uid, stored, nowUnix, list);
      holding = claim.holding;

      if (claim.outcome !== "claimed" && claim.outcome !== "unchanged") {
        // Not an error and not worth failing the publish over: the player keeps their name on
        // their own screens and the board shows whatever they last confirmed.
        logger.info("name not claimed on publish", { uid, outcome: claim.outcome });
      }
    } catch (e) {
      // A contended transaction or a transient fault. The card is still worth writing, and
      // the next publish tries again.
      logger.warn("name claim failed on publish", { uid, error: String(e) });
    }
  }

  const worth = groveWorth(save, groveConfig, level, affordable);

  // `publishableName` rather than `holding.public`, and that one word is the whole takedown
  // path: a name that has been reported past the threshold resolves to null here and falls
  // through to the generated handle, exactly as an unnamed keeper's does. Reading the field
  // directly would republish a name a moment after it was taken down, on this player's very
  // next sync, and nothing else would ever notice.
  const card = buildCard(
    uid, save, groveConfig, worth, level, nowUnix, publishableName(holding), list
  );

  await db.doc(GROVE_PATHS.card(uid)).set(card);

  if (worth.clamped) {
    // Not an accusation and not an error: a stale seed produces it too, and so does a
    // player mid-refund. It is logged because a sudden run of them is the signal that
    // either the catalog or the economy has moved and nobody re-seeded.
    logger.warn("grove worth clamped to what the account could afford", {
      uid, bought: worth.bought, affordable: worth.affordable, earned: worth.earned,
    });
  }

  // Which save this card was built from, so the client can prove it was the one it pushed.
  // Always present: the client reads a *missing* field as "cannot be checked".
  return { card, revision: saveRevision(save) };
});

/**
 * Reserves a keeper name, so that no two groves stand on a board under the same one.
 *
 * <b>Uniqueness is held by a document id, not by a query.</b> `names/{nameKey}` is created
 * inside a transaction, so the check and the take are one indivisible act at any concurrency
 * — where reading a query and then writing lets two players a second apart both be told the
 * name is free. See `names.ts` for the full argument and for why the client's "is this taken"
 * hint is a direct document read rather than a call to this.
 *
 * <b>The save has to exist.</b> Not a data dependency — nothing here reads it — but the only
 * cheap bound on the one abuse this design leaves open. A reservation is one per account and
 * accounts are free, so squatting means minting accounts; requiring a synced save makes each
 * squatted name cost a real session rather than an anonymous token.
 *
 * <b>Every outcome is reported plainly, and only two of the five are failures.</b> "This name
 * is taken" and "wait a moment" are answers a player acts on; collapsing them into an error
 * is how somebody concludes renaming is broken. `refused` is permanent by design and the
 * client stops asking (invariant 13a); `taken` is not, because a name can be released.
 */
export const claimName = onCall(callOptions, async (request): Promise<{
  outcome: ClaimOutcome;
  name: string;
  key: string;
  cooldownSeconds: number;
}> => {
  const uid = requireUid(request);
  const db = getFirestore();

  const requested = (request.data ?? {}) as { name?: unknown };

  // Bounded before anything is read, so an oversized body cannot be used to make the
  // function do work. `sanitiseName` caps it again; this is only about what crosses the wire.
  const raw = typeof requested.name === "string" ? requested.name.slice(0, 256) : "";

  const saveSnapshot = await db.doc(PATHS.player(uid)).get();
  if (!saveSnapshot.exists) {
    throw new HttpsError("failed-precondition", "sync a save before claiming a name");
  }

  const nowUnix = Math.floor(Date.now() / 1000);

  // The word list, from `config/names` if it has been published and from the list compiled
  // into this deployment otherwise. Cached per instance, so this is a document read a few
  // times an hour rather than one per claim — and it never throws, because a name claim that
  // failed when Firestore hiccuped would tell a player their name was unacceptable.
  const { list } = await loadNameConfig(db, nowUnix);

  const claim = await claimName_(db, uid, raw, nowUnix, list);

  const cooldownSeconds = claim.outcome === "cooldown" && claim.holding
    ? Math.max(0, RENAME_COOLDOWN_SECONDS - (nowUnix - claim.holding.atUnix))
    : 0;

  if (claim.outcome === "claimed") {
    logger.info("keeper name claimed", { uid, key: claim.holding?.key });
  }

  return {
    outcome: claim.outcome,

    // What the account holds *after* the call, whatever happened — so a client refused a name
    // can show what it is still called rather than guessing.
    name: claim.holding?.public ?? "",
    key: claim.holding?.key ?? "",
    cooldownSeconds,
  };
});

/**
 * Reports another keeper's published name.
 *
 * <b>The request carries one id and nothing else</b> — no reason, no category, no free text.
 * That is deliberate and it is what keeps this endpoint uninteresting to attack: there is
 * nothing in the body to forge, nothing to store that a stranger wrote, and no way to report a
 * name that is not actually on a board, because the server reads the card itself.
 *
 * <b>Every outcome is a success and the client is told almost nothing.</b> A report that was a
 * duplicate, a report that hit the threshold and a report of a keeper with no chosen name all
 * come back as `reported`, because the alternative leaks moderation state to the person best
 * placed to game it: a caller who can tell "counted" from "already hidden" can binary-search
 * the threshold, and one who can tell "counted" from "nothing to report" learns which accounts
 * are worth brigading. The two answers a player can act on are kept — they reported this name
 * before, and they have filed a day's worth — because both change what the button should say.
 *
 * See `reports.ts` for why the auto-hide is safe to run without a human in front of it.
 */
export const reportKeeperName = onCall(callOptions, async (request): Promise<{
  outcome: "reported" | "duplicate" | "throttled";
}> => {
  const uid = requireUid(request);
  const db = getFirestore();

  const body = (request.data ?? {}) as { keeperId?: unknown };

  // Bounded before anything is read, so an oversized body cannot be used to make the function
  // do work. A uid is an opaque provider token, so the only thing worth asserting about it is
  // that it is a plausible document id.
  const targetUid = typeof body.keeperId === "string" ? body.keeperId.slice(0, 128) : "";
  if (targetUid.length === 0 || targetUid.includes("/")) {
    throw new HttpsError("invalid-argument", "a keeper id is required");
  }

  const nowUnix = Math.floor(Date.now() / 1000);
  const { reportThreshold } = await loadNameConfig(db, nowUnix);

  const result = await reportName(db, uid, targetUid, nowUnix, reportThreshold);

  if (result.outcome === "hidden") {
    logger.warn("keeper name hidden by reports", {
      uid: targetUid, reports: result.reports, threshold: reportThreshold,
    });
  }

  return { outcome: reply(result.outcome) };
});

/** Collapses the six internal outcomes into the three a client is allowed to tell apart. */
function reply(outcome: ReportOutcome): "reported" | "duplicate" | "throttled" {
  if (outcome === "duplicate" || outcome === "already") return "duplicate";
  if (outcome === "throttled") return "throttled";

  return "reported";
}

/**
 * Takes this account's card down.
 *
 * Separate from publishing an empty one, because they are different acts and only one of
 * them is what somebody asked for when they turned the boards off. Deleting a card that is
 * already absent is a success — a withdrawal that could fail permanently is a device
 * retrying it for ever.
 */
export const withdrawGrove = onCall(callOptions, async (request): Promise<{ withdrawn: boolean }> => {
  const uid = requireUid(request);
  await withdrawCard(uid);
  return { withdrawn: true };
});

/**
 * Erases the calling account — every document this deployment holds for it, the Sign in with
 * Apple grant, and the Firebase Auth user itself.
 *
 * <b>The caller can only ever delete itself.</b> There is no uid parameter: the account comes
 * from the verified token and nothing in the request body can move it. That is what makes this
 * safe to expose to every client in the world without a second authorisation of its own.
 *
 * <b>Idempotent, and the client depends on it.</b> A dropped reply is retried, and a retry
 * runs the whole thing again against a mostly-empty account and reports success. See
 * `account.ts` for why the auth user is the last thing removed and why that is what makes the
 * retry safe rather than merely tolerable.
 *
 * <b>The reply is one boolean.</b> The full {@link DeletionReport} goes to the log, where
 * support can read it; the client is told only that it worked, for `reportKeeperName`'s
 * reason — `nameRetained` would tell a player their name had been reported, which is not
 * something a deletion is entitled to disclose.
 *
 * <b>Secrets are declared even though a missing one is not an error</b>, because a function
 * that names a secret gets it injected and one that does not cannot read it at all. All four
 * absent means the revocation is skipped and the deletion still happens.
 */
export const deleteAccount = onCall(
  {
    ...callOptions,
    secrets: [
      APPLE_SIWA_KEY_ID, APPLE_SIWA_TEAM_ID, APPLE_SIWA_CLIENT_ID, APPLE_SIWA_PRIVATE_KEY,
    ],
  },
  async (request): Promise<{ deleted: boolean }> => {
    const uid = requireUid(request);

    // Bounded before it is used. An authorization code is a short opaque string; anything
    // longer is not one, and forwarding it to Apple would make this a way to post arbitrary
    // bodies to a third party through our credentials.
    const raw = (request.data as { appleAuthorizationCode?: unknown })?.appleAuthorizationCode;
    const code = typeof raw === "string" && raw.length > 0 && raw.length <= 512 ? raw : undefined;

    await deleteAccount_({
      uid,
      appleAuthorizationCode: code,
      apple: appleSignInKeys() ?? undefined,
    });

    return { deleted: true };
  }
);

/**
 * Rebuilds the boards and the published distribution of grove worth.
 *
 * Four in the morning UTC, an hour after `publishGroveStats`, so the two heaviest reads in
 * the deployment never overlap — and late enough that a card published anywhere in the
 * world during the previous day is in the sample. Like the stats job it has no retry policy
 * and no alerting: a day with no run leaves yesterday's boards standing, every client reads
 * them exactly as it read them yesterday, and nothing in the game behaves differently.
 */
export const publishGroveRanks = onSchedule(
  { region: REGION, schedule: "0 4 * * *", timeZone: "Etc/UTC", timeoutSeconds: 540 },
  async () => {
    const { ranked, boards } = await rebuildGroveRanks();
    logger.info("grove ranks rebuilt", { ranked, boards });
  }
);
