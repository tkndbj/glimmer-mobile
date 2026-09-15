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
import { Rolls, subjectSeed } from "./random";

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

  /**
   * The golden bands, if the seeder has published them. Absent means every glade pays
   * exactly what its reward rule says — which is a working economy, and the right thing
   * for a server that has not been seeded with the block yet. Understating is recoverable
   * because the wallet's earned floor never falls; overstating is not.
   */
  golden?: GoldenBand[];

  /**
   * The season calendar, if the seeder has published it. Past seasons included, because a
   * rung reached before a window closed stays claimable for ever — a device that was
   * offline over the deadline must not lose what it earned. Nothing here is part of
   * `earnedCredits` any more: a season pays chests, which are claims (see `season.ts`).
   */
  events?: EventConfig[];
}

/**
 * One rung of a season's ladder: how many marks it asks for, and the chest tier each
 * track pays. Tier ids name entries in the published `tasks` block — see `bloom.ts`.
 */
export interface EventMilestone {
  goal: number;
  tier: string;
  premiumTier?: string;
}

/** A time-boxed season with a two-track ladder, exactly as the manifest authors it. */
export interface EventConfig {
  /** What the pass track costs in gems, or absent for a season with only a free one. */
  passGems?: number;
  id: string;
  startUnix: number;
  endUnix: number;
  milestones: EventMilestone[];

  /**
   * True when the season runs again for ever instead of ending — see `SeasonCycle` on the
   * client, which this mirrors.
   *
   * `id` is then a *stem* and `startUnix`/`endUnix` describe cycle nought; cycle `n` runs
   * `[start + n·period, …)` and wears the id `{stem}_{n padded to 4}`. Absent is a one-off
   * season, which is the only kind that existed before this field and reads exactly as it
   * always did.
   */
  repeats?: boolean;
}

/** One golden outcome: a percentage of the ordinary credit reward, and its weight. */
export interface GoldenBand {
  percent: number;
  weight: number;
}

export const MAX_STARS = 3;
export const MAX_LEVEL_ID_LENGTH = 48;

/**
 * The golden's place in the seed and its floor. Contract with `GoldenRules` on the
 * client — see invariant 9c — and never renumbered or renamed.
 */
export const GOLDEN_TAG = "golden";
export const GOLDEN_STREAM = 0;
export const GOLDEN_MIN_PERCENT = 100;
export const GOLDEN_MAX_PERCENT = 1000;

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
 * A glade's golden multiplier for this account, as a percentage.
 *
 * The server's copy of `GoldenTable.PercentFor`. Both sides derive it from (account id,
 * level id) and neither tells the other, which is what lets a variable reward exist at all
 * in an economy where the client may never name its own payout: there is nothing to claim,
 * because the bonus is part of the credits the server already recomputes from the star
 * ledger on every sync.
 *
 * A band below 100 is refused rather than honoured. The bonus may only ever add — a
 * multiplier that bit would quietly pay a player less for a glade than the published
 * reward rule promises, and the published rule is what a store listing and a support reply
 * both quote.
 */
export function goldenPercent(uid: string, levelId: string,
                               bands: GoldenBand[] | undefined): number {
  if (!uid || !levelId || !Array.isArray(bands) || bands.length === 0) {
    return GOLDEN_MIN_PERCENT;
  }

  const usable = bands.filter((band) =>
    band &&
    Number.isFinite(band.percent) && band.percent >= GOLDEN_MIN_PERCENT &&
    Number.isFinite(band.weight) && band.weight >= 1);

  if (usable.length !== bands.length) {
    logger.error("config/progression has unusable golden bands; paying the base", {
      authored: bands.length, usable: usable.length,
    });
    return GOLDEN_MIN_PERCENT;
  }

  let total = 0;
  for (const band of usable) total += Math.floor(band.weight);
  if (total <= 0) return GOLDEN_MIN_PERCENT;

  const rolls = new Rolls(subjectSeed(uid, GOLDEN_TAG, levelId, GOLDEN_STREAM));
  const target = rolls.below(total);

  let accumulated = 0;
  for (const band of usable) {
    accumulated += Math.floor(band.weight);
    if (target < accumulated) {
      return Math.min(Math.floor(band.percent), GOLDEN_MAX_PERCENT);
    }
  }

  return GOLDEN_MIN_PERCENT;
}

/**
 * Applies a percentage to an amount, the one way, in one place.
 *
 * Multiply before divide, and `Math.floor` rather than any rounding, because the client
 * does integer arithmetic and the two have to land on the same number every time.
 */
export function applyGolden(credits: number, percent: number): number {
  if (credits <= 0) return 0;
  if (percent <= GOLDEN_MIN_PERCENT) return credits;

  return Math.floor((credits * percent) / 100);
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
  config: ProgressionConfig,
  uid = ""
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

    // The golden multiplier is part of what a glade is worth, not a bonus paid on top,
    // so it belongs inside this derivation rather than in a grant. An empty uid pays the
    // base — the same refusal the client makes before its first sign-in.
    credits += applyGolden(creditsForStars(rule, stars),
                            goldenPercent(uid, levelId, config.golden));
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
