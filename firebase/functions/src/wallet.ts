/**
 * The server's view of a player's currency, and the only code allowed to change it.
 *
 * Security rules make `players/{uid}/private/wallet` unwritable by any client, so every
 * path into this document goes through the Admin SDK here. That is the whole point of
 * splitting it out of the save: the rule is one line, and no amount of cleverness in the
 * client can route around it.
 */

import { getFirestore, Timestamp, Transaction } from "firebase-admin/firestore";
import { CURRENCIES, CurrencyId, PATHS } from "./config";
import { assertUsableConfig, earnedCredits, ProgressionConfig } from "./progression";

export interface CurrencyState {
  granted: number;
  spent: number;
  confirmedThroughUnix: number;

  /**
   * The highest derived earnings this account has ever shown.
   *
   * Mirrors the client's `earnedHighWater` and exists for the same reason, plus one
   * that is server-specific: derivation depends on `config/progression`, so a content
   * drop whose seed script has not been run yet would recompute a *smaller* earned
   * figure for every player who has cleared the new glades. Without this floor that
   * would briefly take spendable currency off them.
   */
  earnedFloor: number;
}

export type WalletDoc = Record<CurrencyId, CurrencyState> & { updatedAt?: Timestamp };

/** What the client's `CloudWalletState` expects back. */
export interface WalletReply {
  currency: string;
  grantedBaseline: number;
  spentBaseline: number;
  confirmedThroughUnix: number;
  earnedFloor: number;
  confirmedSpendIds: string[];
}

export function emptyCurrency(): CurrencyState {
  return { granted: 0, spent: 0, confirmedThroughUnix: 0, earnedFloor: 0 };
}

const nonNegative = (value: unknown): number =>
  typeof value === "number" && Number.isFinite(value) ? Math.max(0, Math.floor(value)) : 0;

/**
 * A wallet as it exists, or a seeded one if the player is new.
 *
 * The seed is granted here rather than by the client. The client seeds itself too, so a
 * first session works offline, but the moment the server answers its number is the one
 * that stands — and if the two disagreed the player would watch their starting balance
 * change. They agree because the seeder reads both from the same C# constants.
 */
export function readWallet(
  snapshot: FirebaseFirestore.DocumentSnapshot,
  config: ProgressionConfig
): WalletDoc {
  const raw = (snapshot.exists ? snapshot.data() : undefined) as Partial<WalletDoc> | undefined;
  const wallet = {} as WalletDoc;

  for (const currency of CURRENCIES) {
    const existing = raw?.[currency];

    if (existing && typeof existing.granted === "number") {
      wallet[currency] = {
        granted: nonNegative(existing.granted),
        spent: nonNegative(existing.spent),
        confirmedThroughUnix: nonNegative(existing.confirmedThroughUnix),
        earnedFloor: nonNegative(existing.earnedFloor),
      };
      continue;
    }

    // Never seen before: grant the starting balance exactly once, which is what
    // "the document did not exist" means.
    wallet[currency] = {
      ...emptyCurrency(),
      granted: nonNegative(config.seeds?.[currency]),
    };
  }

  return wallet;
}

export function toReply(wallet: WalletDoc, confirmed: Record<string, string[]>): WalletReply[] {
  return CURRENCIES.map((currency) => ({
    currency,
    grantedBaseline: wallet[currency].granted,
    spentBaseline: wallet[currency].spent,
    confirmedThroughUnix: wallet[currency].confirmedThroughUnix,
    earnedFloor: wallet[currency].earnedFloor,
    confirmedSpendIds: confirmed[currency] ?? [],
  }));
}

/**
 * Re-derives earned currency from the player's own save, and ratchets the floor.
 *
 * Mutates `wallet` so the caller writes the raised floor back in the same transaction.
 * Only credits are earned by playing; gems are granted, so their earned component is
 * always zero and their balance is entirely grants minus spends.
 */
export async function deriveEarned(
  transaction: Transaction,
  uid: string,
  wallet: WalletDoc,
  config: ProgressionConfig
): Promise<Record<CurrencyId, number>> {
  const save = await transaction.get(getFirestore().doc(PATHS.player(uid)));
  const levels = save.exists ? (save.data() as { levels?: unknown }).levels : {};

  const derived = earnedCredits(levels, config).credits;

  if (derived > wallet.credits.earnedFloor) wallet.credits.earnedFloor = derived;

  return {
    credits: Math.max(derived, wallet.credits.earnedFloor),
    gems: wallet.gems.earnedFloor,
  };
}

/**
 * What a player can spend, computed rather than believed.
 *
 * `earned` is re-derived from the save's level records using the same rule the client
 * uses, ignoring anything the catalog cannot vouch for. `granted` and `spent` come from
 * this document, which the client cannot write. So the only lever a forged save has on
 * this number is a ledger claiming glades that do not exist, and those are dropped in
 * `earnedCredits`.
 */
export function spendableBalance(
  currency: CurrencyId,
  wallet: WalletDoc,
  earned: Record<CurrencyId, number>
): number {
  const state = wallet[currency];
  return Math.max(0, (earned[currency] ?? 0) + state.granted - state.spent);
}

export async function loadProgressionConfig(transaction?: Transaction): Promise<ProgressionConfig> {
  const ref = getFirestore().doc(PATHS.progressionConfig);
  const snapshot = transaction ? await transaction.get(ref) : await ref.get();

  const data = snapshot.exists ? snapshot.data() : undefined;
  assertUsableConfig(data);

  return data as ProgressionConfig;
}
