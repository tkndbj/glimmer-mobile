/**
 * The season pass entitlement: one bounded document per account and season.
 *
 * <p><b>Only `submitSpends` writes it</b>, in the same transaction that takes the gems —
 * so the purchase and the permission cannot come apart, and nothing a client asserts on
 * its own can set it. The client keeps its own copy in the save for drawing the page; this
 * is the one that gates the money.</p>
 *
 * <p>It was a real-money entitlement written by `redeemPurchase` against a verified
 * receipt. A pass is priced in gems now, which is an ordinary spend (invariant 18) — and
 * that removed the whole apparatus a non-consumable needs: the receipt, the store
 * registration, the refund reversal and the paid-so-far tally a refund had to subtract.</p>
 */

import { PATHS } from "./config";
import { EventConfig } from "./progression";

/** A bounded document per account/season. Only receipt redemption may set `owned`. */
export interface PassState {
  /**
   * The season as it was when the pass was sold.
   *
   * Kept so a content deployment cannot change what somebody has already bought — the
   * ladder they were shown at checkout is the ladder they own. Written by
   * `redeemPurchase`; never read for the entitlement itself, which is `owned` alone.
   */
  definition?: EventConfig;
  owned?: boolean;

  /**
   * Which season this entitlement is for, checked as well as `owned`.
   *
   * Belt and braces on a document whose path already says it: a bug that wrote the wrong
   * path would otherwise hand somebody a season they never bought, and this is the field
   * that makes that a refusal rather than a gift.
   */
  seasonId?: string;

  /** What was paid for it, in gems. For support, never for a decision. */
  gems?: number;

  /** Written by earlier builds, kept so a rollback reads a document it understands. */
  productId?: string;
  receiptPath?: string;
  collectedGoal?: number;
  paidCredits?: number;
  paidGems?: number;
}

/**
 * Whether this account holds a season's pass, read under this server's own credentials.
 *
 * <p>Shared by the callable below and by `claimAwards`, so the paid column is gated by one
 * rule rather than two that can disagree. `productId` is compared as well as `owned`,
 * because a season may change which product sells it and an entitlement bought against the
 * old one is not an entitlement to the new ladder.</p>
 */
export function holdsPass(state: PassState | undefined, season: EventConfig | undefined): boolean {
  if (!state || !season) return false;
  return state.owned === true && state.seasonId === season.id;
}

/** Reads the entitlement document for one account and season. */
export async function readPass(
  reader: { get(ref: FirebaseFirestore.DocumentReference): Promise<FirebaseFirestore.DocumentSnapshot> },
  db: FirebaseFirestore.Firestore,
  uid: string,
  seasonId: string
): Promise<PassState> {
  const snapshot = await reader.get(db.doc(PATHS.eventPass(uid, seasonId)));
  return (snapshot.data() ?? {}) as PassState;
}
