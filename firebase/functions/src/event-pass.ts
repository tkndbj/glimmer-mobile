import { FieldValue, getFirestore } from "firebase-admin/firestore";
import { HttpsError, onCall } from "firebase-functions/v2/https";
import { PATHS, REGION } from "./config";
import { EventConfig, ProgressionConfig } from "./progression";
import { loadProgressionConfig, readWallet, toReply } from "./wallet";

/** A bounded document per account/event. Only receipt redemption can set owned. */
export interface PassState {
  definition?: EventConfig;
  owned?: boolean;
  productId?: string;
  receiptPath?: string;
  collectedGoal?: number;
  paidCredits?: number;
  paidGems?: number;
}

export function passProgress(event: EventConfig, levels: unknown, config: ProgressionConfig): number {
  if (!levels || typeof levels !== "object" || Array.isArray(levels)) return 0;
  const records = levels as Record<string, { stars?: unknown; firstClearedUnix?: unknown }>;
  let finished = 0;
  for (const id of new Set(event.levels)) {
    if (!Object.prototype.hasOwnProperty.call(config.levelChapters, id)) continue;
    const record = records[id];
    if (!record || typeof record.stars !== "number" || !Number.isFinite(record.stars) || record.stars < 1) continue;
    const at = record.firstClearedUnix;
    if (typeof at === "number" && Number.isSafeInteger(at) && at >= event.startUnix && at < event.endUnix) finished++;
  }
  return finished;
}

/** Pure claim arithmetic. A retry, stale device or forged goal cannot pay twice. */
export function passClaim(event: EventConfig, state: PassState, finished: number, goal: number) {
  const floor = Math.max(0, Math.floor(state.collectedGoal ?? 0));
  let through = floor, credits = 0, gems = 0;
  if (!state.owned || state.productId !== event.premiumProductId || !Number.isSafeInteger(goal) || goal < 1)
    return { through, credits, gems };
  for (const tier of event.milestones) {
    if (tier.goal <= floor || tier.goal > Math.min(goal, finished)) continue;
    credits += tier.premiumCredits ?? 0;
    gems += tier.premiumGems ?? 0;
    through = tier.goal;
  }
  return { through, credits, gems };
}

export function validatePass(event: EventConfig | undefined): asserts event is EventConfig {
  if (!event || !event.premiumProductId || !Array.isArray(event.levels) ||
      !Array.isArray(event.milestones) || event.milestones.length < 1 || event.milestones.length > 40 ||
      !(event.endUnix > event.startUnix) || new Set(event.levels).size !== event.levels.length)
    throw new HttpsError("failed-precondition", "event pass is not configured");
  let previous = 0;
  for (const tier of event.milestones) {
    if (!Number.isSafeInteger(tier.goal) || tier.goal <= previous || tier.goal > event.levels.length ||
        !Number.isSafeInteger(tier.premiumCredits ?? 0) || (tier.premiumCredits ?? 0) < 0 || (tier.premiumCredits ?? 0) > 5000 ||
        !Number.isSafeInteger(tier.premiumGems ?? 0) || (tier.premiumGems ?? 0) < 0 || (tier.premiumGems ?? 0) > 1000)
      throw new HttpsError("failed-precondition", "invalid event pass rewards");
    previous = tier.goal;
  }
}

/** goal=0 reads; positive goals collect earned premium tiers. Earned claims never expire. */
export const eventPass = onCall({ region: REGION, cors: false, enforceAppCheck: false }, async request => {
  const uid = request.auth?.uid;
  if (!uid) throw new HttpsError("unauthenticated", "sign in to read the pass");
  const id = request.data?.eventId, goal = request.data?.goal ?? 0;
  if (typeof id !== "string" || !/^[a-z0-9_]{1,64}$/.test(id) || !Number.isSafeInteger(goal) || goal < 0 || goal > 10000)
    throw new HttpsError("invalid-argument", "invalid event pass request");
  return resolveEventPass(getFirestore(), uid, id, goal);
});

export async function resolveEventPass(db: FirebaseFirestore.Firestore, uid: string, id: string, goal: number) {
  return db.runTransaction(async tx => {
    const config = await loadProgressionConfig(tx);
    const passRef = db.doc(PATHS.eventPass(uid, id)), walletRef = db.doc(PATHS.wallet(uid));
    const [passDoc, saveDoc, walletDoc] = await tx.getAll(passRef, db.doc(PATHS.player(uid)), walletRef);
    const state = (passDoc.data() ?? {}) as PassState;
    // Preserve the contract sold to this account, even after a content deployment.
    const event = state.definition ?? config.events?.find(e => e.id === id);
    validatePass(event);
    const finished = passProgress(event, saveDoc.data()?.levels, config);
    if (goal > 0 && (!state.owned || state.productId !== event.premiumProductId))
      throw new HttpsError("permission-denied", "a verified pass purchase is required");
    const claim = passClaim(event, state, finished, goal);
    const wallet = readWallet(walletDoc, config);
    if (claim.through > (state.collectedGoal ?? 0)) {
      wallet.credits.granted += claim.credits;
      wallet.gems.granted += claim.gems;
      tx.set(walletRef, { ...wallet, updatedAt: FieldValue.serverTimestamp() });
      tx.set(passRef, { collectedGoal: claim.through,
        paidCredits: (state.paidCredits ?? 0) + claim.credits,
        paidGems: (state.paidGems ?? 0) + claim.gems,
        updatedAt: FieldValue.serverTimestamp() }, { merge: true });
    }
    return { eventId: id, owned: state.owned === true && state.productId === event.premiumProductId,
      collectedGoal: claim.through, finished, wallets: toReply(wallet, {}),
      credits: claim.credits, gems: claim.gems };
  });
}
