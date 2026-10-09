/**
 * Limited-time shop deals (invariant 60): a bundle of coins sold for gems for a window, made on
 * the admin page and published in one public document, `config/deals`.
 *
 * <p>
 * <b>A deal is the season pass's shape, read across to currency.</b> The client debits the gems
 * under a derived id, `deal:{dealId}` (`SpendEntry.ShopDealId`), and queues the coins as a claim
 * under `deal:{dealId}:credits` (`GrantEntry.ShopDealId`) so they are spendable at once. Neither
 * amount is believed: `submitSpends` prices the debit off this document and, <em>in the same
 * transaction that takes the gems</em>, writes the coins' grant record and raises
 * `credits.granted` - so the purchase and the coins cannot come apart. `claimAwards` never pays
 * a deal claim; it confirms one whose grant record exists (the generic path), leaves one
 * unconfirmed while its debit can still arrive (a sync sends awards before debits), and refuses
 * one whose deal is closed with nothing paid (13a: that will still be true tomorrow).
 * </p>
 * <p>
 * <b>Once per account.</b> The id is derived from the deal, so a second purchase on another
 * device is the same spend document and charges nothing; the wallet keeps `deals` (deal id to
 * its end) so every device can draw a bought deal as bought.
 * </p>
 * <p>
 * <b>A deal is immutable once made</b> - only its end can be brought forward. A price or an
 * amount edited under a player who had already bought would be a payment for one thing and a
 * delivery of another.
 * </p>
 */

import { randomBytes } from "node:crypto";

// ------------------------------------------------------------------ the limits
/** Where the deals live. Readable by any signed-in client (`config/{document}`), writable by nobody. */
export const DEALS_PATH = "config/deals";

/** The document's shape version. A reader refuses a version it was not written for. */
export const DEALS_SCHEMA = 1;

/** The shortest window a deal may run. Under five minutes nobody sees it. */
export const DEAL_MIN_SECONDS = 5 * 60;

/** The longest window a deal may run: a quarter. Anything longer is a shelf price, not a deal. */
export const DEAL_MAX_SECONDS = 90 * 86400;

/** Sanity ceilings, far above anything the shop sells. Mirrored by `ShopDeals` on the client. */
export const DEAL_MAX_CREDITS = 10_000_000;
export const DEAL_MAX_GEMS = 100_000;

/**
 * How long after its end a deal's debit is still honoured: a purchase tapped in the last second
 * whose sync lands a moment later. Deliberately short, because ending a deal early is how a
 * mistake is taken off sale, and a long grace would keep selling it.
 */
export const DEAL_GRACE_SECONDS = 15 * 60;

/**
 * How long an ended deal stays in the public document and in a wallet's `deals`, so a claim or a
 * debit arriving late is judged against the deal it names rather than against nothing.
 */
export const DEAL_KEEP_SECONDS = 30 * 86400;

/** How many deals the document may list, ended ones included. Bounds the one read every client makes. */
export const DEAL_MAX_LISTED = 40;

/**
 * How many deals may be open at once. One, because the shop draws one band and the owner should
 * never wonder why a deal they made is not showing.
 */
export const DEAL_MAX_LIVE = 1;

/** What every deal debit and grant records as its cause. Mirrors `SpendEntry.ShopDealReason`. */
export const DEAL_REASON = "shop_deal";

/**
 * The accounts allowed to make and end deals: the owner's three Google addresses. Checked against
 * a verified token from Google sign-in only, so an address typed into any other provider is not
 * enough.
 */
export const DEAL_ADMINS: readonly string[] = [
  "tekin.dabaj@outlook.com",
  "tekin_dabaj@hotmail.com",
  "arcadetkn@gmail.com",
];

/** `d` + UTC yyyymmddhhmm + four random characters, e.g. `d202610091530k3x9`. */
const DEAL_ID = /^d[0-9]{12}[a-z0-9]{4}$/;

// ------------------------------------------------------------------ the shape
export interface ShopDeal {
  id: string;
  /** Coins granted. */
  credits: number;
  /** Gems charged. */
  gems: number;
  startUnix: number;
  endUnix: number;
}

export interface DealsDoc {
  schema: number;
  deals: ShopDeal[];
}

function positiveInt(value: unknown, max: number): number | null {
  if (typeof value !== "number" || !Number.isFinite(value) || !Number.isInteger(value)) return null;
  return value > 0 && value <= max ? value : null;
}

function unix(value: unknown): number | null {
  if (typeof value !== "number" || !Number.isFinite(value) || !Number.isInteger(value)) return null;
  return value > 0 ? value : null;
}

/** One row read back, or null when any field is wrong. A deal is all or nothing. */
export function readDeal(raw: unknown): ShopDeal | null {
  if (!raw || typeof raw !== "object") return null;
  const row = raw as Record<string, unknown>;

  const id = typeof row.id === "string" && DEAL_ID.test(row.id) ? row.id : null;
  const credits = positiveInt(row.credits, DEAL_MAX_CREDITS);
  const gems = positiveInt(row.gems, DEAL_MAX_GEMS);
  const startUnix = unix(row.startUnix);
  const endUnix = unix(row.endUnix);

  if (!id || credits === null || gems === null || startUnix === null || endUnix === null) return null;
  if (endUnix <= startUnix) return null;

  return { id, credits, gems, startUnix, endUnix };
}

/**
 * The deals a document lists. A malformed row is dropped rather than failing the document, so
 * one bad row written by hand cannot take every deal off sale; a document of another schema
 * lists nothing.
 */
export function usableDeals(raw: unknown): ShopDeal[] {
  if (!raw || typeof raw !== "object") return [];
  const doc = raw as { schema?: unknown; deals?: unknown };
  if (doc.schema !== DEALS_SCHEMA || !Array.isArray(doc.deals)) return [];

  const out: ShopDeal[] = [];
  const seen = new Set<string>();
  for (const row of doc.deals) {
    const deal = readDeal(row);
    if (!deal || seen.has(deal.id)) continue;
    seen.add(deal.id);
    out.push(deal);
  }
  return out;
}

export function findDeal(deals: readonly ShopDeal[], id: string): ShopDeal | null {
  return deals.find((deal) => deal.id === id) ?? null;
}

/** On sale: the window has opened and not closed. */
export function isLive(deal: ShopDeal, nowUnix: number): boolean {
  return nowUnix >= deal.startUnix && nowUnix < deal.endUnix;
}

/** A debit for it is still honoured: on sale, or closed less than the grace ago. */
export function isPayable(deal: ShopDeal, nowUnix: number): boolean {
  return nowUnix >= deal.startUnix && nowUnix < deal.endUnix + DEAL_GRACE_SECONDS;
}

// ------------------------------------------------------------------ the ids
/** `deal:{dealId}` - minted by `SpendEntry.ShopDealId`. */
export function isDealSpendId(id: string): boolean {
  return typeof id === "string" && id.startsWith("deal:") && id.split(":").length === 2;
}

/** The deal a debit names, or null. Strict, because the id is what the spend log keys on. */
export function parseDealSpendId(id: string): string | null {
  if (!isDealSpendId(id)) return null;
  const dealId = id.slice(5);
  return DEAL_ID.test(dealId) ? dealId : null;
}

/** `deal:{dealId}:{currency}` - minted by `GrantEntry.ShopDealId`. */
export interface DealClaim {
  dealId: string;
  currency: string;
  /** For the shared oldest-first sort: a deal has no calendar day, so nought orders it first. */
  dayKey: number;
}

export function isDealGrantId(id: string): boolean {
  return typeof id === "string" && id.startsWith("deal:") && id.split(":").length === 3;
}

export function parseDealClaim(id: string): DealClaim | null {
  if (!isDealGrantId(id) || id.length > 64) return null;
  const [, dealId, currency] = id.split(":");
  if (!DEAL_ID.test(dealId) || !currency || currency.length > 24) return null;
  return { dealId, currency, dayKey: 0 };
}

export function dealGrantId(dealId: string): string {
  return `deal:${dealId}:credits`;
}

// ------------------------------------------------------------------ judging
export type DealSpendVerdict =
  | { ok: true; deal: ShopDeal }
  | { ok: false; reason: string };

/**
 * Whether a debit buys the deal it names. Every refusal is permanent - an unknown deal, a closed
 * window, the wrong currency, too little - and the client answers it by dropping the debit and
 * the coins together (47o), so none is a loop (13a).
 */
export function judgeDealSpend(
  deal: ShopDeal | null, currency: string, amount: number, nowUnix: number
): DealSpendVerdict {
  if (!deal) return { ok: false, reason: "no such deal" };
  if (!isPayable(deal, nowUnix)) return { ok: false, reason: "the deal is not on sale" };
  if (currency !== "gems") return { ok: false, reason: `a deal is bought with gems, not ${currency}` };
  if (amount < deal.gems) return { ok: false, reason: `paid ${amount} against ${deal.gems}` };
  return { ok: true, deal };
}

/**
 * A deal claim with no grant record yet. Never paid here - the coins are paid with the debit -
 * so the answer is only whether to keep waiting for it:
 *
 *  - `wait`: the deal still takes debits, so the one that pays this claim may be later in the
 *    same sync (awards are sent before debits).
 *  - `refuse`: the wrong currency, or a deal that no longer takes debits with nothing paid. Both
 *    will still be true tomorrow.
 */
export function judgeDealClaim(
  deal: ShopDeal | null, claim: DealClaim, nowUnix: number
): { kind: "wait" } | { kind: "refuse"; why: string } {
  if (claim.currency !== "credits") return { kind: "refuse", why: `a deal pays credits, not ${claim.currency}` };
  if (deal && isPayable(deal, nowUnix)) return { kind: "wait" };
  return { kind: "refuse", why: deal ? "the deal closed with no debit recorded" : "no such deal" };
}

// ------------------------------------------------------------------ the wallet
/** The wallet's record of bought deals: deal id to the deal's end, pruned once nothing can name it. */
export type DealsBought = Record<string, number>;

export function readDealsBought(raw: unknown, nowUnix: number): DealsBought {
  const out: DealsBought = {};
  if (!raw || typeof raw !== "object") return out;
  for (const [id, end] of Object.entries(raw as Record<string, unknown>)) {
    if (!DEAL_ID.test(id)) continue;
    const endUnix = unix(end);
    if (endUnix === null || endUnix + DEAL_KEEP_SECONDS < nowUnix) continue;
    out[id] = endUnix;
  }
  return out;
}

// ------------------------------------------------------------------ the admin
/**
 * Whether a verified token may make deals: one of {@link DEAL_ADMINS}, verified, from Google
 * sign-in. The provider is checked because a password account can be created with any address
 * and Google is the one place these addresses are proved.
 */
export function isDealAdmin(token: Record<string, unknown> | undefined): boolean {
  if (!token) return false;
  const email = typeof token.email === "string" ? token.email.trim().toLowerCase() : "";
  const verified = token.email_verified === true;
  const provider = (token.firebase as { sign_in_provider?: unknown } | undefined)?.sign_in_provider;
  return verified && provider === "google.com" && DEAL_ADMINS.includes(email);
}

/** Ended deals kept no longer than {@link DEAL_KEEP_SECONDS}, newest last, at most {@link DEAL_MAX_LISTED}. */
export function pruneDeals(deals: readonly ShopDeal[], nowUnix: number): ShopDeal[] {
  const kept = deals
    .filter((deal) => deal.endUnix + DEAL_KEEP_SECONDS > nowUnix)
    .sort((a, b) => a.startUnix - b.startUnix || a.id.localeCompare(b.id));
  return kept.slice(Math.max(0, kept.length - DEAL_MAX_LISTED));
}

export function liveDeals(deals: readonly ShopDeal[], nowUnix: number): ShopDeal[] {
  return deals.filter((deal) => isLive(deal, nowUnix));
}

export interface DealRequest {
  credits: number;
  gems: number;
  durationSeconds: number;
}

/** Reads the admin page's request, or says which field is wrong. */
export function readDealRequest(raw: unknown): { ok: true; request: DealRequest } | { ok: false; why: string } {
  const data = (raw && typeof raw === "object" ? raw : {}) as Record<string, unknown>;

  const credits = positiveInt(data.credits, DEAL_MAX_CREDITS);
  if (credits === null) return { ok: false, why: `coins must be a whole number from 1 to ${DEAL_MAX_CREDITS}` };

  const gems = positiveInt(data.gems, DEAL_MAX_GEMS);
  if (gems === null) return { ok: false, why: `gems must be a whole number from 1 to ${DEAL_MAX_GEMS}` };

  const duration = positiveInt(data.durationSeconds, DEAL_MAX_SECONDS);
  if (duration === null || duration < DEAL_MIN_SECONDS) {
    return { ok: false, why: `the duration must be between ${DEAL_MIN_SECONDS / 60} minutes and ${DEAL_MAX_SECONDS / 86400} days` };
  }

  return { ok: true, request: { credits, gems, durationSeconds: duration } };
}

/** A fresh deal id for a deal opening at `nowUnix`. Random enough that two in one minute never meet. */
export function mintDealId(nowUnix: number, random: Buffer = randomBytes(4)): string {
  const at = new Date(nowUnix * 1000).toISOString();           // 2026-10-09T15:30:12.000Z
  const stamp = at.slice(0, 16).replace(/[-T:]/g, "");          // 202610091530
  const alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
  let tail = "";
  for (let i = 0; i < 4; i++) tail += alphabet[random[i] % alphabet.length];
  return `d${stamp}${tail}`;
}

/**
 * The document after a deal is made, or why it cannot be. A deal opens now; a second is refused
 * while one is live ({@link DEAL_MAX_LIVE}).
 */
export function createDeal(
  deals: readonly ShopDeal[], request: DealRequest, nowUnix: number, id: string = mintDealId(nowUnix)
): { ok: true; deal: ShopDeal; deals: ShopDeal[] } | { ok: false; why: string } {
  const kept = pruneDeals(deals, nowUnix);
  if (liveDeals(kept, nowUnix).length >= DEAL_MAX_LIVE) {
    return { ok: false, why: "a deal is already on sale; end it before making another" };
  }
  if (findDeal(kept, id)) return { ok: false, why: "that deal id is taken; try again" };

  const deal: ShopDeal = {
    id,
    credits: request.credits,
    gems: request.gems,
    startUnix: nowUnix,
    endUnix: nowUnix + request.durationSeconds,
  };

  return { ok: true, deal, deals: pruneDeals([...kept, deal], nowUnix) };
}

/** The document after a live deal is ended now. Only the end moves. */
export function endDeal(
  deals: readonly ShopDeal[], id: string, nowUnix: number
): { ok: true; deal: ShopDeal; deals: ShopDeal[] } | { ok: false; why: string } {
  const deal = findDeal(deals, id);
  if (!deal) return { ok: false, why: "no such deal" };
  if (!isLive(deal, nowUnix)) return { ok: false, why: "that deal is not on sale" };

  // A second past its start at the least: a deal ended in the second it opened would otherwise
  // read back as a window of nought, which `readDeal` refuses as malformed.
  const ended: ShopDeal = { ...deal, endUnix: Math.max(nowUnix, deal.startUnix + 1) };
  return {
    ok: true,
    deal: ended,
    deals: pruneDeals(deals.map((row) => (row.id === id ? ended : row)), nowUnix),
  };
}

// ------------------------------------------------------------------ the history
/**
 * Every deal ever made, one small server-only document each (`dealHistory/{dealId}`), written in
 * the same transaction that makes or ends it. The public document keeps only the last
 * {@link DEAL_KEEP_SECONDS}; this is what the admin page pages through. Denied to every client by
 * the rules' catch-all.
 */
export const DEAL_HISTORY = "dealHistory";

/** Rows per history page. One query of this many documents, plus one count per row not yet settled. */
export const DEAL_HISTORY_PAGE = 20;

/**
 * Whether a deal's buyer count can no longer move: past its end and the grace, so no debit for
 * it will ever be honoured again. A settled count is written onto the history document once and
 * never counted again, so an old page costs only its own reads.
 */
export function isSettled(deal: ShopDeal, nowUnix: number): boolean {
  return nowUnix >= deal.endUnix + DEAL_GRACE_SECONDS;
}

/** Where the next history page starts: after the last row of this one, newest first. */
export interface HistoryCursor {
  endUnix: number;
  id: string;
}

/** Reads a cursor from the admin page, or null for the first page. Throws nothing: junk is the first page. */
export function readHistoryCursor(raw: unknown): HistoryCursor | null {
  if (!raw || typeof raw !== "object") return null;
  const row = raw as Record<string, unknown>;
  const endUnix = unix(row.endUnix);
  const id = typeof row.id === "string" && DEAL_ID.test(row.id) ? row.id : null;
  return endUnix !== null && id ? { endUnix, id } : null;
}
