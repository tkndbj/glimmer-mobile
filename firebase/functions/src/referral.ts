/**
 * Refer-a-friend: codes, bindings, the milestone, and the chests both sides are paid.
 *
 * <h3>Why every number lives here and none in the save</h3>
 *
 * A referral is a fact about *two* accounts — how many strangers typed this code, whether
 * one of them cleared a chapter — and no device can know it, no merge could join it, and a
 * count of it is exactly the stored count invariant 11b refuses. So the whole state is
 * server-owned: one document per account (`referrals/{uid}`) carrying both halves, the code
 * it holds and the code it typed; one document per code (`referralCodes/{code}`) so that
 * uniqueness is held by a document id and never by a query (invariant 19d); and one row per
 * invitee under the referrer, for support and for the deletion scrub. Nothing about it is
 * client-writable in either direction — see `firestore.rules`.
 *
 * <h3>Why a chest is paid on request rather than claimed</h3>
 *
 * Every other chest in this game is rolled on the device and submitted as a claim with a
 * derived id, because a chest opened on a plane has to be spendable on the plane. A referral
 * chest cannot be: the count it pays on lives nowhere but here. So `claimReferral` rolls the
 * chest, records the grant against the same shape of derived id every other award uses
 * (`referral:{subject}:{currency}`, in `grantLog`), moves the money in the same transaction
 * and answers with the drops. The client banks what is not currency and predicts nothing.
 * Idempotent by the grant log and by the paid list on the referral document, so a retry
 * after a lost reply answers `already_paid` with the same drops.
 *
 * <h3>What bounds a forged claim</h3>
 *
 * Nothing the client sends is trusted: a rung's goal is looked up in the published ladder,
 * the finished count is this document's, and the milestone is judged off the save the
 * invitee's own client pushed — which the client can forge, and that is the one bound that
 * costs a forger real work: the referrer's chest pays once per *account* that cleared ten
 * rungs of the first chapter, and an account is anonymous only until somebody wants a
 * second one paid. The cap on bound invitees is what keeps the document small at any
 * player count; the ladder's top is what keeps the payout finite.
 *
 * <h3>The season</h3>
 *
 * A referral chest grows no season, by the owner's decision on 2026-09-17. Nothing here
 * writes a mark and the client's ledger deliberately does not note one (invariant 51).
 */

import { FieldValue, Firestore, Timestamp, Transaction } from "firebase-admin/firestore";
import { randomBytes } from "node:crypto";
import { logger } from "firebase-functions";

import { CURRENCIES, CurrencyId, PATHS } from "./config";
import { ChestConfig, RolledDrop, rollChestWith } from "./daily";
import { subjectSeed, Rolls } from "./random";
import { ProgressionConfig } from "./progression";
import { TaskConfig, findTier } from "./tasks";
import { WalletDoc, deriveEarned, readWallet } from "./wallet";

// ------------------------------------------------------------------------- the paths
export const REFERRAL_PATHS = {
  /** Both halves of one account's referral state. */
  account: (uid: string) => `referrals/${uid}`,

  /** One row per invitee under the referrer. Support and the deletion scrub; the counts are the truth. */
  invitee: (referrerUid: string, inviteeUid: string) => `referrals/${referrerUid}/invitees/${inviteeUid}`,

  /** The code, keyed on itself so a duplicate is unrepresentable. */
  code: (code: string) => `referralCodes/${code}`,
};

// ------------------------------------------------------------------------- the code
/** Mirrors `ReferralCode.Alphabet`. Contract: no 0/O, no 1/I/L. */
export const CODE_ALPHABET = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

/** Mirrors `ReferralCode.Length`. */
export const CODE_LENGTH = 8;

/** Mirrors `ReferralCode.MaxTyped`: the most a typed code may be before it is folded. */
export const MAX_TYPED = 32;

/**
 * Folds a typed code exactly as the client does (`ReferralCode.Normalise`): upper case,
 * whitespace and separators gone, bounded. Never validates.
 */
export function normaliseCode(typed: unknown): string {
  if (typeof typed !== "string" || typed.length === 0) return "";
  const bounded = typed.length > MAX_TYPED ? typed.slice(0, MAX_TYPED) : typed;
  let out = "";
  for (const c of bounded) {
    if (/\s/.test(c) || c === "-" || c === "_" || c === ".") continue;
    out += c.toUpperCase();
  }
  return out;
}

/** Whether a *folded* string is a code this deployment could have minted. */
export function isValidCode(code: string): boolean {
  if (typeof code !== "string" || code.length !== CODE_LENGTH) return false;
  for (const c of code) if (!CODE_ALPHABET.includes(c)) return false;
  return true;
}

/**
 * Mints a code from the alphabet. Rejection-sampled so every symbol is equally likely —
 * not because a biased code is exploitable, but because a mint that is not uniform is a
 * mint somebody will one day have to explain.
 */
export function mintCode(random: (n: number) => Buffer = randomBytes): string {
  const n = CODE_ALPHABET.length;
  const limit = 256 - (256 % n);
  let out = "";
  while (out.length < CODE_LENGTH) {
    const bytes = random(CODE_LENGTH);
    for (const b of bytes) {
      if (b >= limit) continue;
      out += CODE_ALPHABET[b % n];
      if (out.length === CODE_LENGTH) break;
    }
  }
  return out;
}

// ----------------------------------------------------------------------- the config
/** What one side is paid for one finished invitee: a tier, this many times. */
export interface ReferralPayment {
  tier: string;
  count: number;
}

/**
 * The published block. Mirrors `ReferralTable`; published by `seed-config.mjs`, which has
 * already proved every tier exists. Checked again here for shape only, because a document
 * nothing seeded is a document this function must not guess from.
 *
 * Flat, by the owner's decision on 2026-09-17: every finished invitee pays the referrer
 * `perInvitee`, up to `maxBound` invitees, and the invitee is paid `invitee` on finishing.
 */
export interface ReferralConfig {
  milestoneChapter: string;
  maxBound: number;
  invitee: ReferralPayment;
  perInvitee: ReferralPayment;
}

export const MAX_COUNT = 4;
export const MAX_BOUND_CEILING = 500;

function usablePayment(p: unknown): ReferralPayment | null {
  const c = p as ReferralPayment | undefined;
  if (!c || typeof c !== "object") return null;
  if (typeof c.tier !== "string" || c.tier.length === 0) return null;
  const count = c.count === undefined ? 1 : c.count;
  if (typeof count !== "number" || !Number.isInteger(count) || count < 1 || count > MAX_COUNT) return null;
  return { tier: c.tier, count };
}

export function usableReferralConfig(config: unknown): ReferralConfig | null {
  const c = config as ReferralConfig | undefined;
  if (!c || typeof c !== "object") return null;
  if (typeof c.milestoneChapter !== "string" || c.milestoneChapter.length === 0) return null;
  if (typeof c.maxBound !== "number" || !Number.isInteger(c.maxBound) || c.maxBound < 1) return null;
  if (c.maxBound > MAX_BOUND_CEILING) return null;

  const invitee = usablePayment(c.invitee);
  const perInvitee = usablePayment(c.perInvitee);
  if (!invitee || !perInvitee) return null;

  return { milestoneChapter: c.milestoneChapter, maxBound: c.maxBound, invitee, perInvitee };
}

export function paymentFor(config: ReferralConfig, kind: ReferralClaimKind): ReferralPayment {
  return kind === "invitee" ? config.invitee : config.perInvitee;
}

// -------------------------------------------------------------------- the milestone
/** The level ids of the milestone chapter, read off the published levelId → chapterId map. */
export function milestoneLevels(config: ProgressionConfig, chapterId: string): string[] {
  const ids: string[] = [];
  for (const [levelId, chapter] of Object.entries(config.levelChapters ?? {})) {
    if (chapter === chapterId) ids.push(levelId);
  }
  return ids;
}

/**
 * Whether every level of the milestone is cleared in a save's `levels` map. Mirrors
 * `ReferralMilestone.IsComplete`: a chapter with no levels is never complete, and a level
 * with no stars is not cleared.
 */
export function milestoneComplete(levels: unknown, ids: string[]): boolean {
  if (ids.length === 0) return false;
  if (!levels || typeof levels !== "object" || Array.isArray(levels)) return false;

  const map = levels as Record<string, { stars?: unknown } | null>;
  for (const id of ids) {
    const entry = map[id];
    const stars = entry && typeof entry === "object" && typeof entry.stars === "number"
      ? Math.floor(entry.stars) : 0;
    if (stars <= 0) return false;
  }
  return true;
}

// ---------------------------------------------------------------------- the document
export interface ReferralDoc {
  code: string;
  bound: number;
  finished: number;
  /** The chests paid, by subject (`referralSubject`). Bounded by `maxBound * count + count`. */
  paid: string[];
  referrer: string;
  finished_: boolean;        // the invitee's own milestone, settled
}

/** Reads a referral document defensively. Absent fields read as their empty value. */
export function readReferralDoc(raw: unknown): ReferralDoc {
  const r = (raw && typeof raw === "object" ? raw : {}) as Record<string, unknown>;

  const paid = Array.isArray(r.paid)
    ? (r.paid as unknown[]).filter((p): p is string => typeof p === "string" && p.length > 0 && p.length <= 32)
    : [];

  return {
    code: typeof r.code === "string" && isValidCode(r.code) ? r.code : "",
    bound: typeof r.bound === "number" ? Math.max(0, Math.floor(r.bound)) : 0,
    finished: typeof r.finished === "number" ? Math.max(0, Math.floor(r.finished)) : 0,
    paid,
    referrer: typeof r.referrer === "string" ? r.referrer : "",
    finished_: r.finishedAt instanceof Timestamp || r.finishedAt === true,
  };
}

/** What the client is told. Mirrors `ReferralState`. */
export interface ReferralStateReply {
  code: string;
  bound: number;
  finished: number;
  paid: string[];
  referred: boolean;
  milestoneReached: boolean;
  canRedeem: boolean;
}

export function stateOf(doc: ReferralDoc, milestoneDone: boolean): ReferralStateReply {
  return {
    code: doc.code,
    bound: doc.bound,
    finished: doc.finished,
    paid: doc.paid,
    referred: doc.referrer.length > 0,
    milestoneReached: doc.finished_,
    canRedeem: doc.referrer.length === 0 && !milestoneDone,
  };
}

// ------------------------------------------------------------------------ the roll
/** Mirrors `ReferralLanding.Subject` and `ReferralLedger.SeedTag`. Contract. */
export const REFERRAL_SEED_TAG = "referral";

export type ReferralClaimKind = "rung" | "invitee";

/**
 * `rung:{friend}:{n}` for the referrer's n-th chest for their {friend}-th finished invitee,
 * `invitee:{n}` for the invitee's n-th chest. Mirrors `ReferralLanding.Subject`.
 */
export function referralSubject(kind: ReferralClaimKind, goal: number, index: number): string {
  return kind === "invitee" ? `invitee:${index}` : `rung:${goal}:${index}`;
}

/** `referral:{subject}:{currency}` — the grant log key. Derived from what earned it (10a). */
export function referralGrantId(kind: ReferralClaimKind, goal: number, index: number, currency: string): string {
  return `referral:${referralSubject(kind, goal, index)}:${currency}`;
}

export function isReferralGrantId(id: string): boolean {
  return typeof id === "string" && id.startsWith("referral:");
}

class ReferralRandom extends Rolls {
  constructor(playerKey: string, subject: string, stream: number) {
    super(subjectSeed(playerKey, REFERRAL_SEED_TAG, subject, stream));
  }
}

/** One referral chest, rolled for one account and one subject. Deterministic, so a retry answers the same drops. */
export function rollReferralChest(chest: ChestConfig, playerKey: string, subject: string): RolledDrop[] {
  return rollChestWith(chest, (stream) => new ReferralRandom(playerKey, subject, stream));
}

// -------------------------------------------------------------------- the transactions
export type RedeemOutcome =
  | "bound" | "unknown_code" | "own_code" | "already_referred" | "full" | "too_late" | "no_save";

export type ClaimOutcome = "paid" | "already_paid" | "not_yet" | "unknown";

const MINT_ATTEMPTS = 6;

async function readLevels(transaction: Transaction, db: Firestore, uid: string):
    Promise<{ exists: boolean; levels: unknown }> {
  const save = await transaction.get(db.doc(PATHS.player(uid)));
  const data = save.exists ? (save.data() as { levels?: unknown }) : undefined;
  return { exists: save.exists, levels: data?.levels ?? {} };
}

/**
 * Reads the account's state, minting a code on the first ask and settling the milestone.
 *
 * Settling happens here rather than on a save trigger, deliberately: a trigger on every
 * save write of every player is a function invocation per sync at any player count, for a
 * question that has an answer only once per account. The client asks after a settled sync
 * (invariant 19j) and once when it has never asked; both are one call.
 */
export async function getReferral(
  db: Firestore, uid: string, config: ProgressionConfig, referral: ReferralConfig
): Promise<ReferralStateReply> {
  const ids = milestoneLevels(config, referral.milestoneChapter);

  return db.runTransaction(async (transaction) => {
    const ref = db.doc(REFERRAL_PATHS.account(uid));
    const snapshot = await transaction.get(ref);
    const doc = readReferralDoc(snapshot.data());

    const { levels } = await readLevels(transaction, db, uid);
    const done = milestoneComplete(levels, ids);

    // Every read before the first write, as Firestore requires. The referrer's document is
    // read only when there is something to settle on it.
    const settle = doc.referrer.length > 0 && !doc.finished_ && done;
    const referrerRef = settle ? db.doc(REFERRAL_PATHS.account(doc.referrer)) : null;
    const referrerDoc = referrerRef ? readReferralDoc((await transaction.get(referrerRef)).data()) : null;

    // A code, minted once. The collision check is a read of the code's own document, so it
    // has to happen before any write too — hence the loop reads first and writes after.
    let code = doc.code;
    let minted = false;
    if (code.length === 0) {
      for (let attempt = 0; attempt < MINT_ATTEMPTS; attempt++) {
        const candidate = mintCode();
        const taken = await transaction.get(db.doc(REFERRAL_PATHS.code(candidate)));
        if (!taken.exists) { code = candidate; minted = true; break; }
      }
      if (code.length === 0) {
        throw new Error("could not mint a referral code after several attempts");
      }
    }

    if (minted) {
      transaction.set(db.doc(REFERRAL_PATHS.code(code)), {
        uid, createdAt: FieldValue.serverTimestamp(),
      });
      transaction.set(ref, {
        code,
        bound: doc.bound,
        finished: doc.finished,
        paid: doc.paid,
        createdAt: snapshot.exists ? (snapshot.get("createdAt") ?? FieldValue.serverTimestamp())
                                   : FieldValue.serverTimestamp(),
        updatedAt: FieldValue.serverTimestamp(),
      }, { merge: true });
      logger.info("referral code minted", { uid });
    }

    if (settle && referrerRef && referrerDoc) {
      transaction.set(ref, { finishedAt: FieldValue.serverTimestamp(), updatedAt: FieldValue.serverTimestamp() },
                      { merge: true });
      transaction.set(referrerRef, {
        finished: referrerDoc.finished + 1, updatedAt: FieldValue.serverTimestamp(),
      }, { merge: true });
      transaction.set(db.doc(REFERRAL_PATHS.invitee(doc.referrer, uid)),
                      { finishedAt: FieldValue.serverTimestamp() }, { merge: true });
      logger.info("referral milestone settled", { uid, referrer: doc.referrer, finished: referrerDoc.finished + 1 });
    }

    return stateOf({ ...doc, code, finished_: doc.finished_ || settle }, done);
  });
}

/** Binds the caller to a code's owner. Every refusal is a word the client has a sentence for. */
export async function redeemReferral(
  db: Firestore, uid: string, typed: unknown, config: ProgressionConfig, referral: ReferralConfig
): Promise<{ outcome: RedeemOutcome; state: ReferralStateReply }> {
  const code = normaliseCode(typed);
  const ids = milestoneLevels(config, referral.milestoneChapter);

  return db.runTransaction(async (transaction) => {
    const ref = db.doc(REFERRAL_PATHS.account(uid));
    const doc = readReferralDoc((await transaction.get(ref)).data());
    const { exists, levels } = await readLevels(transaction, db, uid);
    const done = milestoneComplete(levels, ids);

    const refuse = (outcome: RedeemOutcome) => ({ outcome, state: stateOf(doc, done) });

    if (doc.referrer.length > 0) return refuse("already_referred");
    if (!isValidCode(code)) return refuse("unknown_code");

    const codeDoc = await transaction.get(db.doc(REFERRAL_PATHS.code(code)));
    const owner = codeDoc.exists ? String(codeDoc.get("uid") ?? "") : "";
    if (!owner) return refuse("unknown_code");
    if (owner === uid) return refuse("own_code");

    // A reservation costs a real session, for the name claim's reason: an anonymous token
    // with no save is the cheapest thing in the world to make.
    if (!exists) return refuse("no_save");
    if (done) return refuse("too_late");

    const ownerRef = db.doc(REFERRAL_PATHS.account(owner));
    const ownerDoc = readReferralDoc((await transaction.get(ownerRef)).data());
    if (ownerDoc.bound >= referral.maxBound) return refuse("full");

    transaction.set(ref, {
      referrer: owner, boundAt: FieldValue.serverTimestamp(), updatedAt: FieldValue.serverTimestamp(),
    }, { merge: true });
    transaction.set(ownerRef, {
      bound: ownerDoc.bound + 1, updatedAt: FieldValue.serverTimestamp(),
    }, { merge: true });
    transaction.set(db.doc(REFERRAL_PATHS.invitee(owner, uid)), {
      boundAt: FieldValue.serverTimestamp(),
    }, { merge: true });

    logger.info("referral bound", { uid, referrer: owner, bound: ownerDoc.bound + 1 });
    return { outcome: "bound", state: stateOf({ ...doc, referrer: owner }, done) };
  });
}

export interface ClaimReply {
  claim: ClaimOutcome;
  state: ReferralStateReply;
  drops: RolledDrop[];
  wallet: WalletDoc | null;
}

/**
 * Pays one chest, once. `goal` is which finished invitee (from one) for the referrer's
 * side and ignored for the invitee's; `index` is which of the payment's chests (from one).
 * The grant log and the paid list are two idempotency guards on one transaction: a claim
 * that reaches the log finds its own document and adds nothing.
 */
export async function claimReferral(
  db: Firestore, uid: string, kind: ReferralClaimKind, goal: number, index: number,
  config: ProgressionConfig, referral: ReferralConfig, tasks: TaskConfig
): Promise<ClaimReply> {
  const ids = milestoneLevels(config, referral.milestoneChapter);

  return db.runTransaction(async (transaction) => {
    const ref = db.doc(REFERRAL_PATHS.account(uid));
    const doc = readReferralDoc((await transaction.get(ref)).data());

    const walletRef = db.doc(PATHS.wallet(uid));
    const walletSnapshot = await transaction.get(walletRef);
    const wallet = readWallet(walletSnapshot, config);
    await deriveEarned(transaction, uid, wallet, config);   // ratchets the floor

    const { levels } = await readLevels(transaction, db, uid);
    const done = milestoneComplete(levels, ids);

    const answer = (claim: ClaimOutcome, drops: RolledDrop[] = []): ClaimReply =>
      ({ claim, state: stateOf(doc, done), drops, wallet });

    const payment = paymentFor(referral, kind);
    if (!Number.isInteger(index) || index < 1 || index > payment.count) return answer("unknown");

    if (kind === "rung") {
      if (!Number.isInteger(goal) || goal < 1 || goal > referral.maxBound) return answer("unknown");
      if (doc.finished < goal) return answer("not_yet");
    } else {
      if (doc.referrer.length === 0) return answer("unknown");
      if (!doc.finished_) return answer("not_yet");
    }

    const tier = findTier(tasks, payment.tier);
    if (!tier) {
      // A tier the published tasks block does not hold: a content pack ahead of the seeder.
      // Unknown rather than paid-with-a-guess; the client tries again after the seed.
      logger.error("a referral chest names a tier config/progression does not hold", { uid, kind, goal, index, tier: payment.tier });
      return answer("unknown");
    }

    const subject = referralSubject(kind, goal, index);
    const drops = rollReferralChest(tier.chest, uid, subject);
    if (doc.paid.includes(subject)) return answer("already_paid", drops);

    // The grant documents, read before any write.
    const grants: { currency: CurrencyId; amount: number; ref: FirebaseFirestore.DocumentReference; exists: boolean }[] = [];
    for (const currency of CURRENCIES) {
      let amount = 0;
      for (const drop of drops) if (drop.kind === currency) amount += drop.amount;
      if (amount <= 0) continue;

      const grantRef = db.doc(PATHS.grant(uid, referralGrantId(kind, goal, index, currency)));
      const existing = await transaction.get(grantRef);
      grants.push({ currency, amount, ref: grantRef, exists: existing.exists });
    }

    for (const grant of grants) {
      if (grant.exists) continue;                   // a lost reply's retry: already inside `granted`

      transaction.set(grant.ref, {
        currency: grant.currency,
        amount: grant.amount,
        claimedAmount: 0,
        reason: "referral_chest",
        dayKey: 0,
        clientUnix: 0,
        grantedAt: FieldValue.serverTimestamp(),
        kind, goal, index, tier: tier.id,
      });
      wallet[grant.currency].granted += grant.amount;
    }

    transaction.set(walletRef, { ...wallet, updatedAt: FieldValue.serverTimestamp() });
    transaction.set(ref, { paid: FieldValue.arrayUnion(subject), updatedAt: FieldValue.serverTimestamp() },
                    { merge: true });

    logger.info("referral chest paid", { uid, kind, goal, index, tier: tier.id });

    return { claim: "paid", state: stateOf({ ...doc, paid: [...doc.paid, subject] }, done), drops, wallet };
  });
}

// ---------------------------------------------------------------------- deletion
/**
 * Removes an account's referral state: its code, its document and the row it holds under
 * its referrer. The referrer's *counts* stay, deliberately — a chest already paid on this
 * invitee is not un-paid by the invitee leaving, and the slot the binding consumed does
 * not reopen for a second account to take.
 */
export async function deleteReferral(db: Firestore, uid: string): Promise<boolean> {
  const ref = db.doc(REFERRAL_PATHS.account(uid));
  const snapshot = await ref.get();
  if (!snapshot.exists) return false;

  const doc = readReferralDoc(snapshot.data());

  if (doc.code.length > 0) {
    const codeRef = db.doc(REFERRAL_PATHS.code(doc.code));
    const codeDoc = await codeRef.get();
    // Only a reservation this account holds. A stale document naming another account's
    // code must not take theirs down — the name release's own rule.
    if (codeDoc.exists && codeDoc.get("uid") === uid) await codeRef.delete();
  }

  if (doc.referrer.length > 0) {
    await db.doc(REFERRAL_PATHS.invitee(doc.referrer, uid)).delete();
  }

  const bulk = db.bulkWriter();
  bulk.onWriteError((error) => error.failedAttempts < 5);
  await db.recursiveDelete(ref, bulk);

  return true;
}
