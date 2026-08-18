/**
 * Rewarded ads — the half the client is not allowed to be trusted with.
 *
 * <p>
 * Every other award in this game is adjudicable because the server can recompute it. A
 * daily chest is a pure function of (account, day, index), so `claimAwards` re-rolls it
 * and grants its own figure; the client's number is only ever a prediction. That property
 * is what lets a chest be opened on a plane.
 * </p>
 * <p>
 * An ad has no such property. Nothing about "this player watched a video" can be derived
 * from an id, a timestamp or a save file — the only party that knows is the ad network.
 * So the authority moves outside: the network's own servers call
 * {@link handleAdRewardCallback} with a signed payload, that call is the sole evidence a
 * view happened, and `claimAwards` grants nothing without a matching record. The client
 * shows the reward immediately and is right almost always; when it is wrong, the server's
 * silence corrects it.
 * </p>
 * <p>
 * The contract below is LevelPlay's, and it is exact:
 * `signature = md5(TIMESTAMP + EVENT_ID + USER_ID + REWARDS + PRIVATE_KEY)`, with a
 * response containing `EVENT_ID:OK` expected inside 400ms or the callback is retried.
 * That budget is the reason this endpoint writes one document and grants nothing — the
 * grant is a separate, unhurried transaction that happens when the client next syncs.
 * </p>
 */

import { createHash, timingSafeEqual } from "node:crypto";

/** The permanent placement ids, mirroring `AdPlacement` on the client. */
export const AD_PLACEMENTS = [
  "heart_refill",
  "coin_bonus",
  "run_continue",
  "win_bonus",
] as const;
export type AdPlacementId = (typeof AD_PLACEMENTS)[number];

/**
 * Ceiling on a single ad grant, whatever the config says.
 *
 * Mirrors `AdRules.MaxRewardAmount`. A backstop against a mis-seeded config document,
 * which is the one input here that is trusted completely.
 */
export const MAX_AD_REWARD = 5000;

export interface AdPlacementConfig {
  kind: string;
  amount: number;
}

export interface AdsConfig {
  placements: Record<string, AdPlacementConfig>;
}

/**
 * The grant id for one confirmed ad view.
 *
 * <p>
 * Derived from the network's own event id, which is the only identifier both the view and
 * this server agree on. That makes the grant idempotent in exactly the way every other
 * award here is: LevelPlay retries a callback until it is acknowledged, and each retry
 * collides with a document that already exists rather than paying again.
 * </p>
 * <p>
 * Note the client never constructs this and never submits it. Unlike a daily chest, an ad
 * reward is not claimed — it is granted outright when the callback lands, because the
 * client holds no token the callback echoes back. See `AdImpression` on the client for the
 * design that was tried first and why it does not survive LevelPlay 9.
 * </p>
 */
export function adGrantId(eventId: string): string {
  return `ad:${eventId}`;
}

/** True for any id in the ad namespace, submitted or forged. */
export function isAdGrantId(id: string): boolean {
  return typeof id === "string" && id.startsWith("ad:");
}

/**
 * Whether an event id is safe to use as a Firestore document key.
 *
 * Bounded and free of path separators. The value comes from the ad network rather than
 * from a player, but it still lands in a document path, and "it came from a trusted party"
 * is the reasoning behind most path-traversal bugs.
 */
export function usableEventId(eventId: string): boolean {
  return typeof eventId === "string"
    && eventId.length > 0
    && eventId.length <= 128
    && /^[A-Za-z0-9_.:-]+$/.test(eventId);
}

/** Guards a config document that predates the ads block, or was seeded badly. */
export function usableAdConfig(config: unknown): AdsConfig | null {
  if (!config || typeof config !== "object") return null;

  const placements = (config as { placements?: unknown }).placements;
  if (!placements || typeof placements !== "object") return null;

  const clean: Record<string, AdPlacementConfig> = {};

  for (const placement of AD_PLACEMENTS) {
    const entry = (placements as Record<string, unknown>)[placement];
    if (!entry || typeof entry !== "object") continue;

    const kind = (entry as { kind?: unknown }).kind;
    const amount = (entry as { amount?: unknown }).amount;

    if (typeof kind !== "string") continue;
    if (typeof amount !== "number" || !Number.isFinite(amount) || amount <= 0) continue;

    clean[placement] = { kind, amount: Math.min(Math.floor(amount), MAX_AD_REWARD) };
  }

  return Object.keys(clean).length > 0 ? { placements: clean } : null;
}

/**
 * What one finished view of a placement is worth, in the named currency.
 *
 * Returns 0 when the placement pays something that is not this currency — a heart, a
 * boost — which is not an error: those are applied by the client and never adjudicated,
 * so there is genuinely nothing to grant.
 */
export function adCurrencyValue(
  ads: AdsConfig,
  placement: string,
  currency: string
): number {
  const entry = ads.placements[placement];
  if (!entry) return 0;
  return entry.kind === currency ? entry.amount : 0;
}

/**
 * The currency a placement pays, or null when it pays something client-side.
 *
 * Read from the config rather than from the claim, because the claim only carries a
 * placement id — the client does not get to name which ledger it is paid out of.
 *
 * <p>
 * `run_continue` always answers null here, and is listed in {@link AD_PLACEMENTS} anyway.
 * It pays `run_time` — seconds on the run in progress — which is spent before the callback
 * for it has finished arriving, cannot be banked and cannot be moved anywhere else. Listing
 * it keeps the published config a complete description of what the client offers, so a
 * placement missing from this list stays a real signal rather than a question.
 * </p>
 */
export function adCurrencyOf(ads: AdsConfig, placement: string): string | null {
  const entry = ads.placements[placement];
  if (!entry) return null;
  return entry.kind === "credits" || entry.kind === "gems" ? entry.kind : null;
}

// ------------------------------------------------------------ the SSV callback
export interface AdCallbackQuery {
  eventId?: string;
  userId?: string;
  rewards?: string;
  timestamp?: string;
  signature?: string;

  /**
   * The two parameters that can name which offer was watched.
   *
   * Both are read, and the first that names a known placement wins. LevelPlay exposes a
   * placement name and a per-ad-unit rewarded item name, and which of them actually
   * carries our id depends on how the ad units were filled in on the dashboard — a
   * question that cannot be answered from outside the account. Accepting either costs one
   * line and removes the failure where the callback arrives, names the offer in the field
   * we did not read, and the player is silently not paid.
   */
  placement?: string;
  placementName?: string;
  itemName?: string;
}

export type AdCallbackVerdict =
  | { ok: true; uid: string; placement: string; eventId: string }
  | { ok: false; reason: string; retryable: boolean };

/**
 * Checks a callback and says what it authorises.
 *
 * <p>
 * Pure, and separated from the HTTP handler and from Firestore, so the signature rule can
 * be exercised against known vectors in a test rather than against a live ad network. A
 * verification routine nobody can run offline is a verification routine that gets weakened
 * during an outage and never restored.
 * </p>
 * <p>
 * `retryable` is the difference between "we could not process this" and "this is not
 * something we will ever accept". LevelPlay retries until it sees `EVENT_ID:OK`, so a
 * non-retryable verdict must still be answered 200 — otherwise a single malformed request,
 * or one probe, is retried against this endpoint indefinitely.
 * </p>
 */
export function verifyAdCallback(
  query: AdCallbackQuery,
  privateKey: string | undefined
): AdCallbackVerdict {
  // Fail closed. An unconfigured key must never mean "accept everything", which is the
  // shape this bug always takes: a placeholder secret that reads as empty, an equality
  // test against another empty string, and every forged callback authorised.
  if (!privateKey) {
    return { ok: false, reason: "no LevelPlay secret configured", retryable: true };
  }

  const { eventId, userId, rewards, timestamp, signature } = query;
  const placement = namedPlacement(query);

  if (!eventId || !userId || !rewards || !timestamp || !signature) {
    return { ok: false, reason: "callback is missing a mandatory parameter", retryable: false };
  }

  const expected = createHash("md5")
    .update(`${timestamp}${eventId}${userId}${rewards}${privateKey}`)
    .digest("hex");

  if (!equalsConstantTime(expected, signature.toLowerCase())) {
    return { ok: false, reason: "signature does not match", retryable: false };
  }

  // Everything below is checked only once the signature has passed. Complaining about a
  // malformed parameter on an unsigned request tells an attacker what the parameters are.
  if (!usableEventId(eventId)) {
    return { ok: false, reason: "eventId is not a usable document key", retryable: false };
  }

  // Neither field named an offer we ship. Refused rather than paid at a default: guessing
  // which placement a view belongs to is guessing what it was worth.
  if (!placement) {
    return {
      ok: false,
      reason: `callback names no known placement (placement='${query.placement}', itemName='${query.itemName}')`,
      retryable: false,
    };
  }

  // Firestore document ids: bounded, and never a path.
  if (userId.length > 128 || userId.includes("/")) {
    return { ok: false, reason: "userId is not a usable account id", retryable: false };
  }

  return { ok: true, uid: userId, placement, eventId };
}

/**
 * Which offer the callback names, from whichever field carries it.
 *
 * Returns null when neither is one of ours — including when the dashboard sends its own
 * default placement name, which is exactly the case worth refusing rather than guessing at.
 */
function namedPlacement(query: AdCallbackQuery): AdPlacementId | null {
  for (const candidate of [query.placement, query.placementName, query.itemName]) {
    if (candidate && AD_PLACEMENTS.includes(candidate as AdPlacementId)) {
      return candidate as AdPlacementId;
    }
  }
  return null;
}

/**
 * Compares two hex digests without leaking where they first differ.
 *
 * `timingSafeEqual` throws on a length mismatch, so the lengths are checked first — and
 * that check is safe to short-circuit, because the length of an MD5 digest is not a secret.
 */
function equalsConstantTime(a: string, b: string): boolean {
  if (a.length !== b.length) return false;

  try {
    return timingSafeEqual(Buffer.from(a, "utf8"), Buffer.from(b, "utf8"));
  } catch {
    return false;
  }
}

/**
 * The body LevelPlay needs to see before it stops retrying.
 *
 * It looks for `EVENT_ID:OK` *somewhere* in the response, so this is deliberately the
 * whole body and nothing else — no wrapper, no JSON, nothing that could be reformatted by
 * a proxy into something the substring search misses.
 */
export function ackBody(eventId: string): string {
  return `${eventId}:OK`;
}
