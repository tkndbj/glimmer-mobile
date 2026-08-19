/**
 * The product catalog, as the server sees it.
 *
 * This is the only thing that decides what a purchase is worth. The client sends a
 * product id and a receipt and nothing else — never an amount — because a client that
 * names its own reward names any number it likes, and unlike every other forgeable
 * number in this game that one would be backed by a real payment and impossible to
 * argue with afterwards.
 *
 * The document is published by `firebase/seed/seed-config.mjs` from the `store` block
 * of `progression.json`, which is the same block the game reads to draw its shop. One
 * authored list, two consumers: the amount on the card and the amount in the wallet
 * cannot disagree unless somebody edits the content and does not re-seed, which is the
 * one failure the seeder shouts about. Invariant 9a, applied to money.
 */

import { CURRENCIES, CurrencyId } from "./config";

/** What one product grants. Absent currencies are zero rather than missing. */
export interface ProductGrant {
  credits: number;
  gems: number;

  /**
   * `consumable` or `nonconsumable`, carried through purely so a refusal can explain
   * itself. Nothing on this side enforces it: the stores themselves refuse to sell a
   * non-consumable twice, long before a receipt reaches us, which is a far stronger
   * guarantee than anything expressible here.
   */
  kind: string;
}

export type ProductTable = Record<string, ProductGrant>;

export class ProductRejected extends Error {
  constructor(message: string) {
    super(message);
    this.name = "ProductRejected";
  }
}

const nonNegative = (value: unknown): number =>
  typeof value === "number" && Number.isFinite(value) ? Math.max(0, Math.floor(value)) : 0;

/**
 * The most one product may grant, restated here rather than trusted from the document.
 *
 * The seeder checks this too, so reaching it means the config document was written by
 * something other than the seeder — which is either a mistake worth catching or an
 * attack worth refusing. Mirrors `StoreLimits.MaxGrant` on the client; the two are
 * pinned together by `StoreTests`.
 */
export const MAX_GRANT = 5_000_000;

/**
 * Reads one product out of the published catalog.
 *
 * Throws rather than returning null, and the distinction matters at the call site: a
 * validated receipt naming a product this server does not sell is not a "not found", it
 * is a real payment the game cannot honour, and it has to be loud enough that somebody
 * notices before three days of Play's refund window run out.
 */
export function readProduct(table: unknown, productId: string): ProductGrant {
  if (!table || typeof table !== "object") {
    throw new ProductRejected(
      "config/products has not been published; run firebase/seed/seed-config.mjs. Until " +
      "it exists no purchase can be honoured, which is correct — granting a guess against " +
      "a real payment is worse than refusing it."
    );
  }

  const entry = (table as Record<string, unknown>)[productId];
  if (!entry || typeof entry !== "object") {
    throw new ProductRejected(`product '${productId}' is not in config/products`);
  }

  const raw = entry as Record<string, unknown>;
  const grant: ProductGrant = {
    credits: nonNegative(raw.credits),
    gems: nonNegative(raw.gems),
    kind: typeof raw.kind === "string" ? raw.kind : "consumable",
  };

  if (grant.credits === 0 && grant.gems === 0) {
    throw new ProductRejected(`product '${productId}' grants nothing`);
  }

  if (grant.credits > MAX_GRANT || grant.gems > MAX_GRANT) {
    throw new ProductRejected(
      `product '${productId}' grants more than ${MAX_GRANT}; refusing rather than clamping, ` +
      "because a clamped grant is a player charged for one amount and given another"
    );
  }

  return grant;
}

/** The grant as a list of (currency, amount) pairs, skipping the zeroes. */
export function grantEntries(grant: ProductGrant): Array<[CurrencyId, number]> {
  const entries: Array<[CurrencyId, number]> = [];

  for (const currency of CURRENCIES) {
    const amount = currency === "credits" ? grant.credits : grant.gems;
    if (amount > 0) entries.push([currency, amount]);
  }

  return entries;
}
