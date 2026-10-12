/**
 * Admin gifts (invariant 63): currency the owner hands to one account from the admin page, held
 * on the account's wallet until the player claims it, paid by `claimAwards` under a derived id.
 *
 * <p>
 * <b>A gift is a claim, and the server recomputes the amount</b> (10a). The admin page writes the
 * gift onto `players/{uid}/private/wallet` - the document no client can write, already read by
 * every path that moves money - as `gifts[giftId] = { credits, gems, atUnix }`, holding what is
 * <em>still unpaid</em>. The game sees it on its next wallet reply (`gifts` on every row, the
 * wheel's trick: presence means this deployment understands gifts), shows the panel, and on
 * Claim queues `gift:{giftId}:{currency}` through the ordinary award queue - so the coins are
 * spendable at once, offline included, and the claim is resubmitted until this server confirms
 * it. `claimAwards` pays the amount <em>the wallet holds</em>, never the client's figure, writes
 * the grant record and zeroes that currency on the pending row in one transaction; a row with
 * nothing left is removed. A second device claiming the same gift collides with the grant record
 * and is confirmed without being paid again.
 * </p>
 * <p>
 * <b>Why the pending list rides on the wallet rather than in its own collection.</b> Every reply
 * already reads this document, so listing what is owed costs nothing per player per sync; a
 * query over a subcollection on every reply would be a read per player per sync for ever, for a
 * feature most accounts never meet. The permanent record of every gift ever sent - who, when,
 * when it was claimed - is `players/{uid}/gifts/{giftId}`, server-only, written beside the wallet
 * in the same transactions and paged by the admin page; it is never read on a player's path.
 * </p>
 * <p>
 * <b>A gift is immutable once sent.</b> There is no revocation: a client may already have claimed
 * it locally, and a refusal would take back a balance the player has watched arrive. The admin
 * page confirms the account and the amounts before sending; a mistake is a second gift, never a
 * subtraction.
 * </p>
 */

import { randomBytes } from "node:crypto";
import { CURRENCIES, CurrencyId } from "./config";

// ------------------------------------------------------------------ the limits
/** Sanity ceilings, the deals' (`DEAL_MAX_CREDITS`, `DEAL_MAX_GEMS`). */
export const GIFT_MAX_CREDITS = 10_000_000;
export const GIFT_MAX_GEMS = 100_000;

/**
 * How many gifts one account may have waiting at once. Bounds the one document every reply
 * reads and the list every panel draws; a player who has not opened the game for a month does
 * not need a twenty-first.
 */
export const GIFT_MAX_PENDING = 20;

/** What every gift grant records as its cause. Mirrors `GrantEntry.GiftReason`. */
export const GIFT_REASON = "admin_gift";

/** `g` + UTC yyyymmddhhmm + four random characters, e.g. `g202610111530k3x9`. Mirrors `AdminGifts.IsId`. */
export const GIFT_ID = /^g[0-9]{12}[a-z0-9]{4}$/;

/** The permanent record of one gift, under the player. Server-only: the rules' catch-all denies it. */
export const GIFT_PATHS = {
  history: (uid: string, giftId: string) => `players/${uid}/gifts/${giftId}`,
  historyCollection: (uid: string) => `players/${uid}/gifts`,
};

/** Audit trail of every gift sent, and by whom. Server-only, as `dealLog` is. */
export const GIFT_LOG = "giftLog";

// ------------------------------------------------------------------ the shape
/** One pending gift as the wallet holds it: what is still unpaid, per currency. */
export interface PendingGift {
  credits: number;
  gems: number;
  atUnix: number;
}

/** Gift id to what is still owed. */
export type GiftsPending = Record<string, PendingGift>;

/** One row of the wallet reply: a gift with something still unpaid. */
export interface GiftReplyRow {
  id: string;
  credits: number;
  gems: number;
  atUnix: number;
}

function wholeAmount(value: unknown, max: number): number | null {
  if (typeof value !== "number" || !Number.isFinite(value) || !Number.isInteger(value)) return null;
  return value >= 0 && value <= max ? value : null;
}

function unix(value: unknown): number | null {
  if (typeof value !== "number" || !Number.isFinite(value) || !Number.isInteger(value)) return null;
  return value > 0 ? value : null;
}

/**
 * The wallet's pending gifts, read back. A malformed row is dropped rather than failing the
 * document, and a row with nothing left to pay is dropped too, so a reader never lists a gift
 * that owes nothing. Carried through `readWallet`, because every writer writes the wallet whole.
 */
export function readGiftsPending(raw: unknown): GiftsPending {
  const out: GiftsPending = {};
  if (!raw || typeof raw !== "object" || Array.isArray(raw)) return out;

  for (const [id, value] of Object.entries(raw as Record<string, unknown>)) {
    if (!GIFT_ID.test(id) || !value || typeof value !== "object") continue;
    const row = value as Record<string, unknown>;

    const credits = wholeAmount(row.credits, GIFT_MAX_CREDITS);
    const gems = wholeAmount(row.gems, GIFT_MAX_GEMS);
    const atUnix = unix(row.atUnix);
    if (credits === null || gems === null || atUnix === null) continue;
    if (credits === 0 && gems === 0) continue;

    out[id] = { credits, gems, atUnix };
  }
  return out;
}

/** The pending gifts as the reply lists them: oldest first, the id breaking a tie. */
export function giftRows(pending: GiftsPending | undefined): GiftReplyRow[] {
  const rows: GiftReplyRow[] = [];
  for (const [id, gift] of Object.entries(pending ?? {})) {
    if (gift.credits <= 0 && gift.gems <= 0) continue;
    rows.push({ id, credits: gift.credits, gems: gift.gems, atUnix: gift.atUnix });
  }
  rows.sort((a, b) => a.atUnix - b.atUnix || a.id.localeCompare(b.id));
  return rows;
}

// ------------------------------------------------------------------ the ids
/** `gift:{giftId}:{currency}` - minted by `GrantEntry.GiftId`. */
export interface GiftClaim {
  giftId: string;
  currency: string;
  /** For the shared oldest-first sort: a gift has no calendar day, so nought orders it first. */
  dayKey: number;
}

export function isGiftGrantId(id: string): boolean {
  return typeof id === "string" && id.startsWith("gift:") && id.split(":").length === 3;
}

export function parseGiftClaim(id: string): GiftClaim | null {
  if (!isGiftGrantId(id) || id.length > 64) return null;
  const [, giftId, currency] = id.split(":");
  if (!GIFT_ID.test(giftId) || !currency || currency.length > 24) return null;
  return { giftId, currency, dayKey: 0 };
}

export function giftGrantId(giftId: string, currency: CurrencyId): string {
  return `gift:${giftId}:${currency}`;
}

// ------------------------------------------------------------------ judging
export type GiftVerdict =
  | { kind: "pay"; amount: number }
  | { kind: "refuse"; why: string };

/**
 * What a gift claim is worth: the amount the wallet still holds for that currency. Every refusal
 * is permanent (13a): a gift this wallet has never held, or one that carries none of the
 * currency named. A gift already paid in that currency is not seen here - its grant record
 * exists, and `claimAwards` confirms a recorded claim before judging anything.
 */
export function judgeGiftClaim(pending: GiftsPending, claim: GiftClaim): GiftVerdict {
  if (!CURRENCIES.includes(claim.currency as CurrencyId)) {
    return { kind: "refuse", why: `a gift pays credits or gems, not ${claim.currency}` };
  }
  const gift = pending[claim.giftId];
  if (!gift) return { kind: "refuse", why: "no such gift is waiting on this account" };

  const amount = gift[claim.currency as CurrencyId];
  if (!(amount > 0)) return { kind: "refuse", why: `the gift carries no ${claim.currency}` };

  return { kind: "pay", amount };
}

/**
 * The pending map after one currency of a gift is paid: that currency zeroed, and the row gone
 * once nothing is left. Pure, so the transaction that pays can write exactly this back.
 */
export function payGift(pending: GiftsPending, giftId: string, currency: CurrencyId): GiftsPending {
  const gift = pending[giftId];
  if (!gift) return pending;

  const paid: PendingGift = { ...gift, [currency]: 0 };
  const next: GiftsPending = { ...pending };
  if (paid.credits <= 0 && paid.gems <= 0) delete next[giftId];
  else next[giftId] = paid;
  return next;
}

/** Whether a gift is fully paid after `currency` is: nothing of the other currency left. */
export function settlesGift(pending: GiftsPending, giftId: string, currency: CurrencyId): boolean {
  return payGift(pending, giftId, currency)[giftId] === undefined;
}

// ------------------------------------------------------------------ the admin
export interface GiftRequest {
  credits: number;
  gems: number;
}

/** Reads the admin page's request, or says which field is wrong. At least one currency must be positive. */
export function readGiftRequest(raw: unknown): { ok: true; request: GiftRequest } | { ok: false; why: string } {
  const data = (raw && typeof raw === "object" ? raw : {}) as Record<string, unknown>;

  const credits = wholeAmount(data.credits ?? 0, GIFT_MAX_CREDITS);
  if (credits === null) return { ok: false, why: `coins must be a whole number from 0 to ${GIFT_MAX_CREDITS}` };

  const gems = wholeAmount(data.gems ?? 0, GIFT_MAX_GEMS);
  if (gems === null) return { ok: false, why: `gems must be a whole number from 0 to ${GIFT_MAX_GEMS}` };

  if (credits === 0 && gems === 0) return { ok: false, why: "a gift must carry some coins or some gems" };

  return { ok: true, request: { credits, gems } };
}

/** A fresh gift id for a gift sent at `nowUnix`. The deals' minting, with the gift's letter. */
export function mintGiftId(nowUnix: number, random: Buffer = randomBytes(4)): string {
  const at = new Date(nowUnix * 1000).toISOString();           // 2026-10-11T15:30:12.000Z
  const stamp = at.slice(0, 16).replace(/[-T:]/g, "");          // 202610111530
  const alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
  let tail = "";
  for (let i = 0; i < 4; i++) tail += alphabet[random[i] % alphabet.length];
  return `g${stamp}${tail}`;
}

/**
 * The pending map after a gift is added, or why it cannot be: the account already has
 * {@link GIFT_MAX_PENDING} waiting, or the minted id is somehow taken.
 */
export function createGift(
  pending: GiftsPending, request: GiftRequest, nowUnix: number, id: string = mintGiftId(nowUnix)
): { ok: true; id: string; gift: PendingGift; pending: GiftsPending } | { ok: false; why: string } {
  if (Object.keys(pending).length >= GIFT_MAX_PENDING) {
    return { ok: false, why: `this player already has ${GIFT_MAX_PENDING} unclaimed gifts; they must claim some first` };
  }
  if (pending[id]) return { ok: false, why: "that gift id is taken; try again" };

  const gift: PendingGift = { credits: request.credits, gems: request.gems, atUnix: nowUnix };
  return { ok: true, id, gift, pending: { ...pending, [id]: gift } };
}

/** Rows per page of a player's gift history on the admin page. */
export const GIFT_HISTORY_PAGE = 50;
