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
   * The event calendar, if the seeder has published it. Past events included: a closed
   * event still pays what it paid, and dropping it would take currency away from every
   * player who finished it.
   */
  events?: EventConfig[];
}

/** One rung of an event's reward track. */
export interface EventMilestone {
  goal: number;
  credits: number;
}

/** A time-boxed run at a set of glades, exactly as the manifest authors it. */
export interface EventConfig {
  id: string;
  startUnix: number;
  endUnix: number;
  levels: string[];
  milestones: EventMilestone[];
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
 * What the event calendar has paid this player.
 *
 * The server's copy of `EventLedger`. An event's progress is a count of its glades whose
 * *first* clear falls inside its window, and the reward is the milestones that count has
 * passed — so, like the golden multiplier, it is derived rather than granted and there is
 * nothing to claim, confirm or store. It rides inside `earnedCredits` for that reason: it
 * is part of what the save is worth, not a payment on top of it.
 *
 * Only glades the catalog vouches for count, exactly as they do for stars. An event naming
 * a level this server has not been seeded with contributes nothing rather than being
 * guessed at, which is the same understate-rather-than-invent bargain the rest of this
 * file makes — an understatement is recoverable through the wallet's earned floor, and a
 * giveaway is not.
 */
export function eventCredits(
  records: Record<string, { stars: number; firstClearedUnix: number }>,
  config: ProgressionConfig
): number {
  const events = config.events;
  if (!Array.isArray(events) || events.length === 0) return 0;

  let credits = 0;

  for (const groveEvent of events) {
    if (!groveEvent || !Array.isArray(groveEvent.levels) ||
        !Array.isArray(groveEvent.milestones)) {
      continue;
    }
    if (!(groveEvent.endUnix > groveEvent.startUnix)) continue;

    let finished = 0;

    for (const levelId of groveEvent.levels) {
      const record = records[levelId];
      if (!record || record.stars <= 0) continue;

      const at = record.firstClearedUnix;
      if (at < groveEvent.startUnix || at >= groveEvent.endUnix) continue;

      finished++;
    }

    // Milestones are authored lowest goal first and the reader on both sides refuses a
    // track that is not — sorting one here would pay rewards nobody authored.
    for (const milestone of groveEvent.milestones) {
      if (!milestone || finished < milestone.goal) break;
      credits += Math.max(0, Math.floor(milestone.credits));
    }
  }

  return credits;
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

  // Gathered as they are believed, so the event track sees exactly the records the star
  // arithmetic did — a level the catalog cannot vouch for must not advance an event any
  // more than it can earn a star.
  const believed: Record<string, { stars: number; firstClearedUnix: number }> = {};

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

    const clearedAt = entry && typeof entry === "object" &&
                      typeof (entry as { firstClearedUnix?: unknown }).firstClearedUnix === "number"
      ? Math.floor((entry as { firstClearedUnix: number }).firstClearedUnix)
      : 0;

    believed[levelId] = { stars, firstClearedUnix: clearedAt };
  }

  credits += eventCredits(believed, config);

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
