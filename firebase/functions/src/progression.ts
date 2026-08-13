/**
 * Server-side derivation of earned currency.
 *
 * This is a deliberate second implementation of the client's `ProgressionLedger`, and
 * it exists because "XP is derived" is not only an architecture choice — it is the
 * security property that makes the economy defensible. A client that accumulated
 * currency into a counter could only ever be believed or disbelieved. A client that
 * derives it from a ledger can be *checked*, because the server can run the same
 * arithmetic over the same records and compare.
 *
 * The two are held together by `firebase/shared/reward-vectors.json`, which both sides
 * run as a test. Change the arithmetic here and the C# vector test fails; change it
 * there and this one does. That file is the contract — not the comments.
 *
 * Three rules make them agree exactly, and each has a reason: a glade the catalog
 * cannot vouch for earns nothing (or an invented level id would mint currency), stars
 * are clamped to three (or a forged record would), and a level id counts once (which
 * the map-keyed wire format now also enforces structurally).
 */

import { logger } from "firebase-functions";

export interface RewardRule {
  xpFirstClear: number;
  xpPerStar: number;
  creditsFirstClear: number;
  creditsPerStar: number;
}

/** A rule as authored, where -1 means "inherit" rather than zero. */
export interface RewardRuleInput {
  xpFirstClear?: number;
  xpPerStar?: number;
  creditsFirstClear?: number;
  creditsPerStar?: number;
}

export interface ProgressionConfig {
  /** Bumped by the seed script whenever the table changes. */
  version: number;
  rewards: RewardRule;
  /** Per-chapter overrides, already resolved against the defaults by the seeder. */
  chapterRewards: Record<string, RewardRule>;
  /** levelId → chapterId, derived from the shipped catalog. */
  levelChapters: Record<string, string>;
  /** Starting balances, read out of the C# constants by the seeder. */
  seeds?: Record<string, number>;
}

export const MAX_STARS = 3;
export const MAX_LEVEL_ID_LENGTH = 48;

export const DEFAULT_RULE: RewardRule = {
  xpFirstClear: 40,
  xpPerStar: 20,
  creditsFirstClear: 30,
  creditsPerStar: 15,
};

/**
 * Fills unwritten fields from a fallback.
 *
 * -1 means "not written, inherit"; zero is a legitimate payout for a tutorial chapter
 * and the two have to stay distinguishable. Exported because the seed script resolves
 * overrides with it too — a second copy of this rule would be a second thing to drift.
 */
export function resolveRule(override: RewardRuleInput | undefined | null,
                            fallback: RewardRule): RewardRule {
  const pick = (field: keyof RewardRule): number => {
    const value = override?.[field];
    return typeof value === "number" && value >= 0 ? value : fallback[field];
  };

  return {
    xpFirstClear: pick("xpFirstClear"),
    xpPerStar: pick("xpPerStar"),
    creditsFirstClear: pick("creditsFirstClear"),
    creditsPerStar: pick("creditsPerStar"),
  };
}

/** Resolves an authored list of chapter overrides into a lookup. */
export function buildChapterRules(
  overrides: Array<RewardRuleInput & { chapterId?: string }> | undefined | null,
  defaults: RewardRule
): Record<string, RewardRule> {
  const rules: Record<string, RewardRule> = {};

  for (const entry of overrides ?? []) {
    if (!entry?.chapterId) continue;
    if (rules[entry.chapterId]) continue;          // the first one wins, as on the client
    rules[entry.chapterId] = resolveRule(entry, defaults);
  }

  return rules;
}

function creditsForStars(rule: RewardRule, stars: number): number {
  return stars <= 0 ? 0 : rule.creditsFirstClear + rule.creditsPerStar * stars;
}

/**
 * Earned credits, computed from records the server is willing to believe.
 *
 * `levels` is the wire shape: a map keyed by level id. Keying by id rather than using
 * an array is what makes a duplicated record structurally impossible rather than
 * something this function has to remember to guard against — and it is what lets the
 * client write one changed glade instead of the whole ledger.
 *
 * Every entry is still validated: a level id the catalog has never heard of is ignored,
 * and a star count outside 0..3 is clamped. That is what stops a forged save minting
 * currency. A player can write anything they like into their own save document, and
 * none of it reaches this number unless it describes a glade that actually exists.
 */
export function earnedCredits(
  levels: unknown,
  config: ProgressionConfig
): { credits: number; counted: number; rejected: number } {
  let credits = 0;
  let counted = 0;
  let rejected = 0;

  if (!levels || typeof levels !== "object" || Array.isArray(levels)) {
    return { credits: 0, counted: 0, rejected: 0 };
  }

  for (const [levelId, raw] of Object.entries(levels as Record<string, unknown>)) {
    if (!levelId || levelId.length > MAX_LEVEL_ID_LENGTH) {
      rejected++;
      continue;
    }

    const chapterId = config.levelChapters[levelId];
    if (chapterId === undefined) {
      // Either a forged id, or a glade from content this server has not been seeded
      // with yet. Both are handled the same way: it earns nothing until the seeder has
      // run. An understatement is recoverable — the earned floor on the wallet means it
      // cannot take spendable currency away from anyone — while a giveaway is not.
      rejected++;
      continue;
    }

    const entry = raw as { stars?: unknown } | null;
    const rawStars = entry && typeof entry === "object" && typeof entry.stars === "number"
      ? Math.floor(entry.stars)
      : 0;

    if (rawStars <= 0) continue;                    // played, never cleared

    const stars = Math.min(rawStars, MAX_STARS);
    const rule = config.chapterRewards[chapterId] ?? config.rewards;

    credits += creditsForStars(rule, stars);
    counted++;
  }

  if (rejected > 0) {
    logger.info("ledger entries ignored while deriving credits", { rejected, counted });
  }

  return { credits, counted, rejected };
}

/** Guards against a config document that was never seeded or was seeded badly. */
export function assertUsableConfig(config: unknown): asserts config is ProgressionConfig {
  const c = config as ProgressionConfig | undefined;

  if (
    !c ||
    typeof c !== "object" ||
    !c.rewards ||
    typeof c.rewards.creditsPerStar !== "number" ||
    typeof c.levelChapters !== "object" ||
    c.levelChapters === null
  ) {
    // Failing closed matters here: an empty config would derive zero earned credits for
    // everybody, which reads to a player as their balance vanishing.
    throw new Error(
      "config/progression is missing or malformed — run the seed script before serving traffic"
    );
  }
}
