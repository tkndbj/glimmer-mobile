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
import { NameHolding, heldName } from "./names";
import { todayKey } from "./daily";
import { assertUsableConfig, earnedCredits, ProgressionConfig } from "./progression";
import { readFloor, StreakFloor } from "./streak";
import { readWheelPosition } from "./wheel";

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

export type WalletDoc = Record<CurrencyId, CurrencyState> & {
  updatedAt?: Timestamp;

  /**
   * The last streak night this server paid for, and the day it fell on.
   *
   * It rides here rather than in its own document for one reason: this is the document no
   * client can write, and the value is worthless anywhere else. It is read and raised in
   * the same transaction that moves the money, so a night cannot be paid without the floor
   * moving with it. See `streak.ts`.
   */
  streak?: StreakFloor;

  /**
   * The keeper name this account holds, and the key it is reserved under.
   *
   * It rides here for `streak`'s reason: this is the document no client can write. That is
   * what makes the name on a public card unforgeable rather than merely sanitised — a card is
   * built from this and never from the save's own string. Written only by `names.ts`, in the
   * same transaction that takes the reservation, so a name cannot be held without the
   * reservation existing or the reverse.
   */
  name?: NameHolding;

  /**
   * Heart containers this account bought and this server has since <b>revoked</b>, because
   * the store refunded or charged back the payment.
   *
   * It rides here for `streak` and `name`'s reason: this is the document no client can
   * write. That is the whole of what makes a refund stick — a container is otherwise a
   * client-held entitlement (the save's `heartContainersOwned`), which is safe because a
   * forged one buys faster hearts and no currency, but a *refunded* one would be money
   * leaving with the goods still delivered. Buy, spend, refund, repeat is the commonest way
   * a mobile economy leaks; see invariant 18c.
   *
   * Written by `revokeReceipt` and cleared, per product, by `redeemPurchase` when the same
   * container is bought again. Reported to the client on every wallet reply as a list of
   * ids that were revoked — never as a list of ids the account owns, because an answer read
   * as a whitelist would confiscate a purchase on any reply that was short or from an
   * account this server had not caught up with.
   */
  containersRevoked?: string[];

  /**
   * How many `win_bonus` views this server has granted the account, and on which UTC day.
   *
   * <p>
   * It rides here for `streak`, `name` and `containersRevoked`'s reason: this is the
   * document no client can write. That is the whole of what makes the bonus wheel safe — a
   * slice is a pure function of (account, day, spin index), so the index decides money, and
   * an index the client kept for itself would be both forgeable and, more prosaically,
   * wrong the first time a verification callback was delayed past the next win.
   * </p>
   * <p>
   * Advanced only inside `adReward`'s granting transaction, which is guarded by the grant
   * document — so a retried callback collides with a record that already exists and the
   * counter does not move. Reported back on every wallet reply as `wheelDay`/`wheelSpins`,
   * where its <em>presence</em> is also what tells a client this deployment understands the
   * wheel at all. See `wheel.ts` and `WheelStand` on the client.
   * </p>
   */
  wheel?: { day: number; spins: number };
};

/** What the client's `CloudWalletState` expects back. */
export interface WalletReply {
  currency: string;
  grantedBaseline: number;
  spentBaseline: number;
  confirmedThroughUnix: number;
  earnedFloor: number;
  confirmedSpendIds: string[];

  /**
   * Heart container product ids this server has revoked for the account.
   *
   * An account fact rather than a currency one, repeated on every row because the reply is
   * a list of currency rows — cheaper than giving four callables a second shape, and the
   * client's own union makes reading it from several rows the same as reading it once.
   */
  containersRevoked: string[];

  /**
   * Award ids this server holds a record for, and has therefore already folded into
   * `grantedBaseline`. The client drops them from its own queue on seeing them; until
   * it does it keeps counting them locally, so a lost reply costs a resubmission rather
   * than a player's daily chest.
   */
  confirmedGrantIds: string[];

  /**
   * The bonus wheel's position: the UTC day it is counted for, and how many `win_bonus`
   * views have been granted within it — which is therefore the index of the next spin.
   *
   * <p>
   * Always present, including as (today, 0) for an account that has never spun, and that is
   * load-bearing rather than tidy. A client reads the <em>presence</em> of the field as
   * "this deployment understands the wheel" and draws no wheel without it, which is what
   * makes shipping the app ahead of the functions cost a feature nobody has seen rather
   * than a payout nobody honours — invariant 12a's deploy-ordering hazard, removed instead
   * of written down. Omitting it for a fresh account would make that signal a lie.
   * </p>
   * <p>
   * An account fact rather than a currency one, repeated on every row for
   * {@link WalletReply.containersRevoked}'s reason; the client's stand only ever moves
   * forward, so reading it from several rows is the same as reading it once.
   * </p>
   */
  wheelDay: number;
  wheelSpins: number;
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

  // The reserved keeper name, carried through for `streak`'s reason and with more urgency:
  // every writer of this document writes it *whole* (`transaction.set` with no merge), so a
  // field this function does not copy is a field the next sync deletes. That is invariant 12a
  // one document over — and it would have been near-invisible, because the reservation
  // survives, the next publish silently re-claims it, and all anybody sees is their name
  // occasionally missing from a board.
  const name = heldName(raw as Record<string, unknown> | undefined);

  // Assigned only when there is one. Firestore rejects `undefined` as a document value, so an
  // unconditional assignment would fail every wallet write for an account that has no name.
  if (name) wallet.name = name;

  // Carried through for exactly the name's reason, and it is the more expensive one to get
  // wrong: every writer of this document writes it *whole*, so a field this function does not
  // copy is a field the next spend or claim silently deletes — and deleting a revocation hands
  // a refunded container back to the player who was refunded for it. Invariant 12a, one
  // document over. Assigned only when there is something, for the `undefined` reason above.
  const revoked = Array.isArray(raw?.containersRevoked)
    ? (raw.containersRevoked as unknown[])
        .filter((id): id is string => typeof id === "string" && id.length > 0)
    : [];

  if (revoked.length > 0) wallet.containersRevoked = revoked;

  // The bonus wheel's position, carried through for exactly the two fields above's reason:
  // every writer of this document writes it *whole*, so a field this function does not copy
  // is a field the next spend, claim or grant silently deletes. Invariant 12a, one document
  // over — and dropping this one would reset the wheel to its first spin on every sync,
  // which is a player being paid the same slice all day and nothing at all showing it.
  const wheel = raw?.wheel;
  if (wheel && typeof wheel === "object" &&
      typeof wheel.day === "number" && typeof wheel.spins === "number") {
    wallet.wheel = {
      day: Math.max(0, Math.floor(wheel.day)),
      spins: Math.max(0, Math.floor(wheel.spins)),
    };
  }

  // Whether this server has ever recorded currency for the account, which is what "brand new"
  // has always meant here. It used to be read off `snapshot.exists`, and that stopped being
  // the same question the moment a second feature wrote to this document: a name claimed
  // before the first sync would create it, and the account would then be treated as one
  // migrating in — which hands it the unbounded first streak claim that the seeded floor below
  // exists to prevent.
  const everBanked = CURRENCIES.some((currency) => {
    const state = raw?.[currency];
    return !!state && typeof state.granted === "number";
  });

  // The streak floor, carried through so that writing the document back cannot drop it.
  //
  // A brand-new wallet is seeded to *yesterday*, and that one line is what stops a fresh
  // account claiming a long backlog of nights it never played: with a real floor in place,
  // its first claim has to be night one, today. Existing wallets are deliberately left
  // without one — see `advances`. A player upgrading into this build is holding a streak
  // this server has never recorded, and an invented floor would refuse the nights the game
  // has already shown them.
  wallet.streak = everBanked
    ? readFloor(raw?.streak)
    : { paidThroughDay: todayKey(Date.now()) - 1, paidNight: 0 };

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

export function toReply(
  wallet: WalletDoc,
  confirmed: Record<string, string[]>,
  confirmedGrants: Record<string, string[]> = {},
  today: number = todayKey(Date.now())
): WalletReply[] {
  // Rolled over here as well as in the granting transaction, so a reply taken on a day with
  // no views yet answers (today, 0) rather than yesterday's tally. Without it the client
  // would seed its first spin of the day from yesterday's index and disagree with the very
  // grant that is about to be computed from today's.
  const wheel = readWheelPosition(wallet.wheel, today);

  return CURRENCIES.map((currency) => ({
    currency,
    grantedBaseline: wallet[currency].granted,
    spentBaseline: wallet[currency].spent,
    confirmedThroughUnix: wallet[currency].confirmedThroughUnix,
    earnedFloor: wallet[currency].earnedFloor,
    confirmedSpendIds: confirmed[currency] ?? [],
    confirmedGrantIds: confirmedGrants[currency] ?? [],
    containersRevoked: wallet.containersRevoked ?? [],
    wheelDay: wheel.day,
    wheelSpins: wheel.spins,
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
  const data = save.exists ? (save.data() as { levels?: unknown; events?: unknown }) : undefined;
  const levels = data?.levels ?? {};

  // The uid is passed because it seeds the golden multiplier — a glade's credits are a
  // function of (account, level), not of the level alone. See `goldenPercent`. Omitting
  // it would pay every player the base and quietly disagree with what the game showed them.
  //
  // `events` carries how much of each event track the player has collected. It has to be
  // read: since save schema v11 a milestone pays only once it has been taken, so a wallet
  // deriving without it would hold back credits the game has already shown as banked.
  // Forging it buys nothing — `eventCredits` clamps the floor to the glades this same
  // derivation counted.
  const derived = earnedCredits(levels, config, uid, data?.events).credits;

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
