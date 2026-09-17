#!/usr/bin/env node
/**
 * Publishes the reward table and product catalog into Firestore.
 *
 * The server has to derive earned currency independently of the client — that is what
 * makes a forged save unable to mint money — and to do that it needs the same reward
 * table and the same level-to-chapter mapping the client uses. Rather than maintaining
 * a second copy by hand, this generates them from the shipped content, so the two can
 * only disagree if somebody forgets to run it.
 *
 * Run it after any change to progression.json, to a chapter's levels, or to the seed
 * balances:
 *
 *     node firebase/seed/seed-config.mjs
 *
 * Authentication reuses the gcloud login rather than a service-account key file, so
 * there is no long-lived credential sitting in the repository to leak.
 */

import { readFileSync, existsSync, readdirSync } from "node:fs";
import { execSync } from "node:child_process";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { pathToFileURL } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(HERE, "..", "..");
const CONTENT = join(REPO, "Assets", "StreamingAssets", "Content");
const CURRENCY_CS = join(REPO, "Assets", "Game", "Scripts", "Domain", "Persistence", "CurrencyLedger.cs");

const PROJECT = "glimmer-groove-1cd60";

// ---------------------------------------------------------------- reading content
function readJson(path) {
  if (!existsSync(path)) throw new Error(`missing ${path}`);
  return JSON.parse(readFileSync(path, "utf8"));
}

/**
 * The starting balances, read out of the C# rather than restated here.
 *
 * A second copy of these numbers would drift, and the symptom would be a player's
 * balance changing the first time they sync — the client granting one seed and the
 * server another. Failing loudly if the constants cannot be found is the point: a
 * rename should break this script, not quietly desynchronise the economy.
 */
function readSeeds() {
  const source = readFileSync(CURRENCY_CS, "utf8");

  const grab = (name) => {
    const match = source.match(new RegExp(`public\\s+const\\s+long\\s+${name}\\s*=\\s*(\\d+)`));
    if (!match) {
      throw new Error(
        `could not find Currency.${name} in CurrencyLedger.cs — if it was renamed, update this script ` +
        `rather than hardcoding the value, or the client and server will seed different balances`
      );
    }
    return Number(match[1]);
  };

  return { credits: grab("SeedCredits"), gems: grab("SeedGems") };
}

/**
 * Rule resolution is imported from the built functions rather than reimplemented.
 *
 * The seeder decides what the server will believe, so if it resolved overrides even
 * slightly differently from the code that reads them, the server would enforce numbers
 * nobody authored. Requiring a build first is a small price for there being exactly one
 * implementation of the rule.
 */
const compiled = join(REPO, "firebase", "functions", "lib", "progression.js");
if (!existsSync(compiled)) {
  throw new Error(
    "firebase/functions/lib/progression.js is missing — run 'npm --prefix firebase/functions run build' first.\n" +
    "The seeder shares its reward-resolution logic with the server rather than keeping a second copy."
  );
}
const { resolveRule, buildChapterRules, DEFAULT_RULE } = await import(pathToFileURL(compiled).href);

/**
 * The keeper-level curve, exactly as the client reads it.
 *
 * Published because the boards need it: the earned half of a grove's worth is decided by
 * which companion gates the star ledger has passed, so the server has to be able to derive
 * a keeper level. Nothing else on the server has ever needed one, which is why this block
 * did not exist until the boards did.
 *
 * It rides `config/progression` rather than `config/grove` because it is a fact about
 * progression, and because `ProgressionSchema` versions on its own cadence (invariant 9b) —
 * a catalog bump must not invalidate the curve.
 */
function readKeeperCurve(progression) {
  const bands = Array.isArray(progression.xpToNext) ? progression.xpToNext.map(Math.floor) : [];

  if (bands.length === 0 || bands.some((step) => !(step > 0))) {
    throw new Error("progression.json has no usable xpToNext band; the keeper curve would be undefined");
  }

  const tailXpToNext = Math.floor(progression.tailXpToNext ?? 0);
  const tailXpIncrement = Math.floor(progression.tailXpIncrement ?? 0);

  if (!(tailXpToNext > 0)) throw new Error("progression.json tailXpToNext must be positive");
  if (tailXpIncrement < 0) throw new Error("progression.json tailXpIncrement must not be negative");

  return {
    maxLevel: Math.floor(progression.maxLevel ?? 60),
    xpToNext: bands,
    tailXpToNext,
    tailXpIncrement,
  };
}

/**
 * The grove catalog, derived from `homestead.json` and the manifest's roster.
 *
 * This is `readStore`'s argument for a second feature. The server has to be able to answer
 * "what is this grove worth" without believing the client, which means it needs every
 * price the client uses — and a price list maintained beside the content file is two files
 * edited on different days, which is how a leaderboard ends up ranking people against a
 * catalog that no longer exists. Invariant 9a: derived into the second place, never typed
 * there.
 *
 * Only *priced* things are published. A free piece is worth nothing (invariant 16g), so it
 * has no entry and needs no exclusion rule on the far side — the same reason starter land
 * is absent rather than zero.
 */
function buildGroveConfig() {
  const homestead = readJson(join(CONTENT, "homestead.json"));
  const manifest = readJson(join(CONTENT, "manifest.json"));

  // The shelf is authored in `progression.json`, so the card's turret roster is read from there
  // even though everything else on this document comes out of the grove's own files.
  const progression = readJson(join(CONTENT, "progression.json"));

  const pieces = {};
  const bundles = {};
  const dwellings = {};
  const dwellingLevels = {};

  for (const piece of homestead.pieces ?? []) {
    if (!piece?.id) continue;

    const cost = Math.floor(piece.cost ?? 0);
    if (cost > 0) pieces[piece.id] = cost;

    // How many copies one purchase grants, so the boards can score a grove by what was
    // paid for it rather than by how the shop happened to package it. Only written when
    // it is not one: an absent entry means "sells singly", which is what a config seeded
    // before bundles existed already meant, so this stayed additive.
    const bundle = Math.floor(piece.bundle ?? 1);
    if (cost > 0 && bundle > 1) {
      if (cost % bundle !== 0) {
        throw new Error(
          `grove piece '${piece.id}' costs ${cost} in bundles of ${bundle}, which does not ` +
          "divide it — a copy would be worth less than a tenth of the bundle and every " +
          "grove holding one would score short on the boards"
        );
      }
      bundles[piece.id] = bundle;
    }

    // Every rung, priced or not: the first is free and still has to be findable, because
    // the hall draws the best rung *held* and a free one is held by everybody.
    if (piece.kind === "dwelling") {
      dwellings[piece.id] = Math.floor(piece.tier ?? 0);

      // And the keeper level that opens it, written only when there is one — `bundles`'
      // rule, for its reason: an absent entry means "ungated", which is exactly what a
      // config seeded before the ladder was gated already meant, so this stays additive in
      // both directions and neither the server nor the seeder has to go first.
      //
      // A gated rung must also be *priced*, or reaching the level would be the only thing
      // between a player and the home and nothing would ever grant it. `ContentValidation`
      // and `content.py` both refuse that; this refuses it again, because the seeder sees
      // the file first and a published table that disagreed with the game would be a home
      // the server scores and the client cannot sell.
      const level = Math.floor(piece.requiresKeeperLevel ?? 0);
      if (level > 0) {
        if (cost <= 0) {
          throw new Error(
            `grove home '${piece.id}' opens at keeper level ${level} and has no price — the ` +
            "gate is permission to pay rather than a way of paying, so nothing would ever " +
            "grant it and the ladder would end there"
          );
        }
        dwellingLevels[piece.id] = level;
      }
    }
  }

  // Every region that is *sold*, at its worth in credits — which is nought for the ones
  // priced in gems.
  //
  // Two jobs, and only the first is obvious. `groveWorth` sums this to score the bought half
  // of a grove, and a zero adds nothing, which is the whole of invariant 16g's answer for gem
  // land: the score is the credits' worth of what is held and the server's clamp is
  // denominated in credits, so a gem cannot be priced into it. But `buildCard` also filters
  // the published `land[]` through this table — as a sanity check that the catalog vouches for
  // the id — and `GroveVisitScreen` draws only the ground the card says is owned. So a gem
  // region left *out* would score correctly and quietly delete eight of the floor's fourteen
  // columns from every visitor's view, with everything standing on them floating over nothing.
  // Present at zero says both things at once; absent says one of them wrong.
  //
  // Starter land stays absent, and that is not the same case: it is never written into
  // `groveLandOwned` at all (invariant 16e), so nothing ever looks it up here.
  const regions = {};
  for (const region of homestead.floor?.regions ?? []) {
    if (!region?.id) continue;

    const cost = Math.floor(region.cost ?? 0);
    const gems = Math.floor(region.gems ?? 0);

    if (cost > 0 && gems > 0) {
      throw new Error(
        `grove region '${region.id}' is priced in both credits (${cost}) and gems (${gems}); ` +
        "a region is sold in one currency or the other, and a card built from two prices " +
        "would score whichever this file happened to read first"
      );
    }

    if (cost > 0 || gems > 0) regions[region.id] = cost;
  }

  const companions = {};
  for (const companion of manifest.companions ?? []) {
    if (!companion?.id || companion.disabled) continue;

    const cost = Math.floor(companion.unlockCost ?? 0);
    if (cost <= 0) continue;                      // the starter, and anything else given away

    companions[companion.id] = { cost, level: Math.floor(companion.unlockLevel ?? 0) };
  }

  // The turret roster, as id -> the keeper level that opens its rung, read out of
  // `progression.json` rather than out of `homestead.json` because that is where the shelf is
  // authored. It rides `config/grove` and not `config/progression` for one reason: it is read
  // by exactly one thing, `buildCard`, which already has the grove config open — and putting it
  // beside the reward table would be a second document read on every publish to carry twenty
  // numbers that never change between drops.
  //
  // <b>The gate and nothing else.</b> The server does not price a turret, does not know which
  // one is the starter and does not check that one was bought, because nothing it holds implies
  // a purchase (`publishedLine`). A level is the one claim about a line that can be checked
  // against a ledger this server derives itself, so it is the one that is published.
  //
  // The starter is in here at level nought, deliberately. Omitting it would make an absent entry
  // mean two things at once — "ungated" and "not a turret" — and `publishedLine` refuses an id
  // the roster has never heard of, so the free turret would be the only one a card could not
  // carry.
  //
  // `free` is published rather than left to be derived, which is invariant 16j's trap said about
  // a shelf: a free turret is held by everybody and is never written into `wardsOwned`, so a
  // server testing ownership without it would drop the one turret every account in the game
  // stands. Read here as "no price in any currency", which is `WardModel.IsStarter` — and the
  // reason it is read *here* rather than over there is that a second currency makes the
  // predicate ambiguous exactly once, and this is where the file is in hand.
  const wards = {};
  for (const model of progression.wards?.models ?? []) {
    if (!model?.id) continue;
    if (typeof model.id !== "string" || model.id.length === 0 || model.id.length > 64) continue;

    const gems = Math.floor(model.gemPrice ?? 0);
    const coins = Math.floor(model.coinPrice ?? 0);

    wards[model.id] = {
      level: Math.max(0, Math.floor(model.minLevel ?? 0)),
      free: gems <= 0 && coins <= 0,
    };
  }

  if (Object.keys(wards).length === 0) {
    throw new Error("progression.json has no turret roster; every published card would carry " +
                    "an empty line and every public profile would draw four starters");
  }

  const stars = (homestead.score?.stars ?? [])
    .map(Math.floor)
    .filter((at) => at > 0)
    .sort((a, b) => a - b);

  if (stars.length === 0) {
    throw new Error("homestead.json has no score ladder; every grove would be published wearing no stars");
  }

  if (Object.keys(dwellings).length === 0) {
    throw new Error("homestead.json has no dwelling; a published card could name no home");
  }

  return {
    version: Math.floor(manifest.groveVersion ?? 1),
    pieces,
    bundles,
    regions,
    companions,
    dwellings,
    dwellingLevels,
    wards,
    stars,
  };
}

function buildProgressionConfig() {
  const progression = readJson(join(CONTENT, "progression.json"));
  const manifest = readJson(join(CONTENT, "manifest.json"));

  const defaults = resolveRule(progression.rewards, DEFAULT_RULE);
  const chapterRewards = buildChapterRules(progression.chapterRewards, defaults);

  // levelId → chapterId, read from the chapters the manifest actually lists. A chapter
  // file left on disk but removed from the manifest is not shipped, so its levels must
  // not earn anything either.
  const levelChapters = {};
  let levelCount = 0;

  for (const listed of manifest.chapters ?? []) {
    if (!listed?.id || listed.disabled) continue;

    const chapter = readJson(join(CONTENT, "chapters", `${listed.id}.json`));
    for (const level of chapter.levels ?? []) {
      if (!level?.id) continue;
      if (levelChapters[level.id]) {
        throw new Error(`level id '${level.id}' appears in more than one chapter`);
      }
      levelChapters[level.id] = listed.id;
      levelCount++;
    }
  }

  if (levelCount === 0) throw new Error("no levels found; refusing to seed an empty catalog");

  return {
    config: {
      version: manifest.progressionVersion ?? 1,
      rewards: defaults,
      chapterRewards,
      levelChapters,
      seeds: readSeeds(),
      daily: readDaily(progression),
      ads: readAds(progression),
      golden: readGolden(progression),
      endless: readEndless(progression),
      // Read before the calendar and before the streak, because both name chest tiers and
      // the seeder is the one place that can prove a named tier actually exists — a
      // season's ladder lives in the manifest, the streak's in progression.json, the tiers
      // in progression.json's tasks block, and no one of those files can check another.
      ...(() => {
        const tasks = readTasks(progression);
        const tierIds = new Set(tasks.tiers.map((t) => t.id));
        return {
          events: readEvents(manifest, tierIds),
          tasks,
          streak: readStreak(progression, tierIds),
          referral: readReferral(progression, tasks.tiers.map((t) => t.id), levelChapters),
        };
      })(),
      keeper: readKeeperCurve(progression),
    },
    products: readStore(progression),
    levelCount,
  };
}

/**
 * The product catalog, derived from the same block the game draws its shop from.
 *
 * This is the whole reason `config/products` is no longer hand-maintained. A shop card
 * promising 750 gems and a server granting 700 is not a bug anybody would find by looking
 * at either file — it is two files edited on different days — and the difference is
 * charged to a real card. Invariant 9a says a rule that must exist twice is generated into
 * the second place rather than typed there; this is that, for money.
 *
 * Everything the client's reader enforces is enforced again here, because a catalog the
 * client would have dropped is a card that is not drawn against a receipt the server would
 * still honour. A refusal at seed time is a message on a terminal; the same disagreement in
 * production is a chargeback.
 */
function readStore(progression) {
  const store = progression.store;

  // Absent is legitimate and means exactly one thing: no shop. `redeemPurchase` then
  // refuses every receipt with "product is not configured", which is correct — a purchase
  // that cannot be priced must not be granted a guess.
  if (!store || !Array.isArray(store.products) || store.products.length === 0) {
    console.log("  note: progression.json has no store block, so no product can be redeemed");
    return null;
  }

  const MAX_GRANT = 5000000;                  // mirrors StoreLimits.MaxGrant and products.ts
  const MIN_CAPACITY = 6, MAX_CAPACITY = 50;  // mirrors StoreLimits and products.ts
  const SHELVES = new Set(["gems", "coins", "bundles", "supplies", "event_pass"]);
  const KINDS = new Set(["consumable", "nonconsumable"]);

  const products = {};
  const shelves = new Map();

  for (const entry of store.products) {
    const id = String(entry?.id ?? "");

    if (!/^[a-z0-9_]{1,64}$/.test(id)) {
      throw new Error(
        `store product id '${entry?.id}' is unusable; ids are lower case letters, digits and ` +
        "underscores, because a receipt is looked up by this string for the life of the account"
      );
    }

    if (products[id]) throw new Error(`store lists product '${id}' twice`);

    if (!KINDS.has(entry.kind)) {
      throw new Error(
        `store product '${id}' has kind '${entry.kind}'; it must be consumable or nonconsumable, ` +
        "and the two are not interchangeable — the store itself enforces that a nonconsumable " +
        "is sold once per account"
      );
    }

    if (!SHELVES.has(entry.shelf)) {
      throw new Error(`store product '${id}' names unknown shelf '${entry.shelf}'`);
    }

    const credits = Math.floor(entry.credits ?? 0);
    const gems = Math.floor(entry.gems ?? 0);
    const capacity = Math.floor(entry.heartCapacity ?? 0);
    // A season's pass is bought with gems, so no real-money product grants one. `products.ts`
    // refuses such a product on the way in; refusing here is what stops one being published
    // in the first place, which is the difference between a seed that fails on a terminal
    // and a card that takes money and unlocks nothing.
    if (entry.eventPassId || entry.shelf === "event_pass") {
      throw new Error(`store product '${id}' carries a season pass entitlement, which nothing ` +
                      "grants any more; a pass is bought with gems");
    }

    if (!Number.isFinite(credits) || !Number.isFinite(gems) || credits < 0 || gems < 0) {
      throw new Error(`store product '${id}' grants ${entry.credits} credits and ${entry.gems} gems`);
    }

    // A heart container: the one non-currency thing a real-money product may grant, because
    // a capacity is an idempotent permanent entitlement rather than an amount. Mixing the
    // two is refused rather than resolved — see products.ts and StoreProduct.HeartCapacity.
    if (capacity > 0 && (credits > 0 || gems > 0)) {
      throw new Error(
        `store product '${id}' sells a heart capacity and also grants currency; a real-money ` +
        "product may grant one or the other, never both"
      );
    }

    if (capacity > 0) {
      if (capacity < MIN_CAPACITY || capacity > MAX_CAPACITY) {
        throw new Error(
          `store product '${id}' sells a heart capacity of ${capacity}, outside ` +
          `${MIN_CAPACITY}..${MAX_CAPACITY}`
        );
      }
      if (entry.kind !== "nonconsumable") {
        throw new Error(
          `store product '${id}' sells a heart capacity as a consumable; a permanent upgrade ` +
          "must be nonconsumable so the store itself refuses to sell it twice"
        );
      }
      if (entry.shelf !== "supplies") {
        throw new Error(
          `store product '${id}' sells a heart capacity but sits on the '${entry.shelf}' shelf; ` +
          "capacities belong on 'supplies', which is where everything about hearts is"
        );
      }
    } else if (entry.shelf === "supplies") {
      throw new Error(
        `store product '${id}' sits on the supplies shelf without selling a heart capacity; ` +
        "that shelf is otherwise for goods bought with gems"
      );
    }

    if (credits === 0 && gems === 0 && capacity === 0) {
      throw new Error(`store product '${id}' grants nothing`);
    }

    if (credits > MAX_GRANT || gems > MAX_GRANT) {
      throw new Error(
        `store product '${id}' grants more than the supported ${MAX_GRANT}. The server refuses ` +
        "rather than clamping, so publishing this would make every purchase of it fail"
      );
    }

    const cents = Math.floor(entry.referenceUsdCents ?? 0);
    if (!Number.isFinite(cents) || cents < 49 || cents > 100000) {
      throw new Error(
        `store product '${id}' has referenceUsdCents ${entry.referenceUsdCents}, outside ` +
        "49..100000. It is never shown to a player, but the value ladder is proved against it"
      );
    }

    // One-time offers are left out of the ladder check below, and that is the whole
    // point of them rather than a loophole: a starter pack is deliberately better value
    // than anything else on its shelf, and it cannot cannibalise the ladder because the
    // store will not sell it twice. Ranking it alongside the repeatable rungs would either
    // fail the build or force it to be a worse offer than it should be.
    //
    // Heart containers are excluded by the same clause and need to be: they grant no
    // currency at all, so their value per unit of money is zero and ranking them would
    // fail every ladder they were part of. What proves a container ladder is right is
    // `Validate Content`, which compares capacity against price instead.
    if (entry.kind !== "nonconsumable") {
      if (!shelves.has(entry.shelf)) shelves.set(entry.shelf, []);
      shelves.get(entry.shelf).push({ id, credits, gems, cents });
    }

    // Only what the server needs in order to honour a receipt. The shelf, the badge and
    // the reference price are display and validation; publishing them would invite
    // somebody to think the server had an opinion about them.
    products[id] = { credits, gems, kind: entry.kind, capacity };
  }

  // The ladder has to get better as it gets bigger. A middle rung worth less per unit of
  // money than the one below it is invisible in the file and obvious to the first player
  // who does the arithmetic — and it makes the derived "+40% extra" badge print a smaller
  // number on a dearer product.
  const perGem = creditsPerGem(shelves);

  for (const [shelf, entries] of shelves) {
    const ranked = [...entries].sort((a, b) => a.cents - b.cents);

    for (let i = 1; i < ranked.length; i++) {
      if (shelfValue(ranked[i], perGem) < shelfValue(ranked[i - 1], perGem)) {
        throw new Error(
          `store shelf '${shelf}': '${ranked[i].id}' costs more than '${ranked[i - 1].id}' and ` +
          "gives less per unit of money. A ladder that gets worse as it gets bigger is a shop " +
          "nobody buys the large size in"
        );
      }
    }
  }

  // Goods are bought with gems and applied on the phone, so the server has no opinion
  // about them and they are deliberately not published. They are checked here anyway,
  // because this is the one place both halves of the shop are read together.
  for (const good of Array.isArray(store.goods) ? store.goods : []) {
    if (!/^[a-z0-9_]{1,64}$/.test(String(good?.id ?? ""))) {
      throw new Error(`store good id '${good?.id}' is unusable`);
    }
    if (good.kind !== "hearts" && good.kind !== "heart_boost") {
      throw new Error(
        `store good '${good.id}' names kind '${good.kind}'. Only hearts and heart_boost can be ` +
        "bought with gems — currency cannot, because only the server may grant it"
      );
    }
    if (!(good.amount > 0) || !(good.gems > 0)) {
      throw new Error(`store good '${good.id}' hands over ${good.amount} for ${good.gems} gems`);
    }
  }

  return products;
}

/** Credits per gem, from the cheapest rung of each money shelf. Mirrors `StoreCatalog`. */
function creditsPerGem(shelves) {
  const cheapest = (shelf, pick) => {
    const entries = (shelves.get(shelf) ?? []).filter(pick);
    if (entries.length === 0) return null;
    return entries.reduce((a, b) => (b.cents < a.cents ? b : a));
  };

  const gemBase = cheapest("gems", (e) => e.gems > 0);
  const coinBase = cheapest("coins", (e) => e.credits > 0);

  if (!gemBase || !coinBase) return 1;

  const rate = Math.floor((coinBase.credits * gemBase.cents) / (gemBase.gems * coinBase.cents));
  return rate < 1 ? 1 : rate;
}

function shelfValue(entry, perGem) {
  return Math.floor(((entry.credits + entry.gems * perGem) * 10000) / entry.cents);
}

/**
 * The season calendar, published so the server can price what a rung's chest pays.
 *
 * Past seasons are published too, and that is not an oversight: a rung reached before a
 * window closed stays claimable for ever, so dropping one would refuse a chest somebody
 * earned. Nothing here expires.
 *
 * Every rule the client's reader enforces is enforced again here, because a config the
 * client would have refused is a config the server would quietly disagree with. A refusal
 * at seed time is a message on a terminal; the same refusal in production is a chest
 * nobody can claim.
 *
 * `tierIds` is the published tasks block's own ladder, and checking against it here is the
 * whole reason this takes an argument: a rung naming a tier nothing defines is a claim the
 * server can never price, and it is invisible in both files on their own.
 *
 * The pass is priced in **gems**, and that price is published because `submitSpends` has to
 * compare a pass debit against it — a spend is an amount the client chooses, so without it
 * the paid column would cost whatever a client says.
 */
function readEvents(manifest, tierIds) {
  const events = manifest.events;
  if (!Array.isArray(events) || events.length === 0) return null;

  const seen = new Set();
  const published = [];

  for (const entry of events) {
    if (!entry || entry.disabled) continue;

    const id = String(entry.id ?? "");
    if (!/^[a-z0-9_]+$/.test(id)) {
      throw new Error(`season id '${entry.id}' is unusable; ids are lower case letters, ` +
                      "digits and underscores, because one names a save row and every claim id");
    }
    if (seen.has(id)) throw new Error(`manifest lists season '${id}' twice`);
    seen.add(id);

    const startUnix = Math.floor(Number(entry.startUnix));
    const endUnix = Math.floor(Number(entry.endUnix));
    if (!Number.isFinite(startUnix) || !Number.isFinite(endUnix) || endUnix <= startUnix) {
      throw new Error(`season '${id}' ends at or before it starts`);
    }

    const passGems = Math.floor(Number(entry.passGems ?? 0));
    if (!Number.isFinite(passGems) || passGems < 0 || passGems > 100000) {
      throw new Error(`season '${id}' prices its pass at ${entry.passGems} gems, outside 0..100000`);
    }
    const milestones = [];
    let previousGoal = 0;

    for (const rung of entry.milestones ?? []) {
      const goal = Math.floor(Number(rung?.goal));

      if (!Number.isFinite(goal) || goal <= previousGoal) {
        throw new Error(`season '${id}' rung goals must rise: ${rung?.goal} follows ${previousGoal}`);
      }
      if (goal > 100000) throw new Error(`season '${id}' has a rung at ${goal} marks, above 100000`);

      const tier = String(rung?.tier ?? "");
      const premiumTier = String(rung?.premiumTier ?? "");

      if (!tierIds.has(tier)) {
        throw new Error(`season '${id}' rung at ${goal} pays free tier '${tier}', which the ` +
                        "published tasks block does not define; a rung paying a chest nobody " +
                        "can price is a claim the server can never confirm");
      }

      if (passGems > 0 && !tierIds.has(premiumTier)) {
        throw new Error(`season '${id}' sells a pass but its rung at ${goal} pays pass tier ` +
                        `'${premiumTier}', which the published tasks block does not define`);
      }

      if (passGems <= 0 && premiumTier) {
        throw new Error(`season '${id}' pays pass tier '${premiumTier}' at ${goal} but sells ` +
                        "no pass, so nobody could ever claim it");
      }

      milestones.push(premiumTier ? { goal, tier, premiumTier } : { goal, tier });
      previousGoal = goal;
    }

    if (milestones.length === 0) throw new Error(`season '${id}' has no rungs, so it pays nothing`);
    if (milestones.length > 40) throw new Error(`season '${id}' exceeds 40 rungs`);

    // A repeating season mints its ids from the clock (`SeasonCycle`), so what is published
    // is the *stem* and the window of cycle nought — and the flag is what tells this server
    // to derive the rest rather than look the id up in this list.
    //
    // **Publishing it is not optional and its absence is silent.** Without the flag the
    // server finds no season by `watch_0003`, answers `unknown`, and the client — which is
    // quite happy, because it derived the season itself — resubmits that claim for the life
    // of the account without ever being paid. Every file reads as authored.
    const repeats = entry.repeats === true;

    if (repeats) {
      // The suffix has to fit inside the id ceiling `season.ts` parses against, or the ids
      // this stem mints are ids that deployment refuses. `EventRules.MaxSeasonIdLength`.
      const room = 64 - (4 + 1);
      if (id.length > room) {
        throw new Error(`season '${id}' repeats, so its id is a stem with a 4-digit cycle ` +
                        `number on the end; that leaves ${room} characters and this one is ` +
                        `${id.length}`);
      }

      if (published.some((e) => e.repeats)) {
        throw new Error(`manifest asks season '${id}' to repeat, but another already does; ` +
                        "only one season may repeat, because two would be two answers to " +
                        "which season is running");
      }
    }

    published.push(repeats
      ? { id, startUnix, endUnix, milestones, passGems, repeats: true }
      : { id, startUnix, endUnix, milestones, passGems });
  }

  return published.length > 0 ? published : null;
}

/**
 * The golden bands, published so the server can re-derive what a glade was worth.
 *
 * Not optional tuning either, though it fails softly rather than loudly: a glade's credits
 * are a function of (account, level), and a server without these bands would derive the
 * base for every glade while the game showed the multiplied figure. That surfaces as a
 * balance the player cannot spend — the earned floor keeps what they were shown, but the
 * server would stop agreeing with it on the next content push.
 *
 * The floor of 100 is enforced here as well as in the two readers. The bonus may only ever
 * add, and a seeder that quietly published a band under 100 would pay every player holding
 * that glade less than the published reward rule promises.
 */
/**
 * What the Infinite lane pays per wave cleared, and the ceiling on it.
 *
 * **The only block here that decides XP outside the star ledger**, and the only one whose
 * absence is silently wrong rather than merely inert: a keeper level this server derives below
 * the one the device holds is a published card that *drops* whatever that level gated
 * (invariant 19a), with nothing said anywhere. So this is published whenever the content file
 * carries it, and the function refuses rather than clamps — a bad number here reaches every
 * player's keeper level at once and XP is floored, so it cannot be taken back.
 *
 * Mirrors `EndlessRewardTable.Resolve` on the client and `DEFAULT_ENDLESS` on the server;
 * all three carry the same two constants, and a drift between them is what
 * `endless.mjs` in `functions/test` exists to catch.
 */
function readEndless(progression) {
  const DEFAULT_XP_PER_WAVE = 15;
  const DEFAULT_MAX_WAVES = 99990;
  const HARD_MAX_WAVES = 1000000;
  const MAX_XP_PER_WAVE = 1000;

  const endless = progression.endless;

  // Absent is legitimate and means the built-in figures, which is what a client with no block
  // also does — see `EndlessRewardTable.Resolve` for why this one agrees rather than failing
  // closed. Published explicitly so the two halves cannot drift apart on a stale deploy.
  if (!endless) return { xpPerWave: DEFAULT_XP_PER_WAVE, maxWaves: DEFAULT_MAX_WAVES };

  const read = (raw, fallback, max, name) => {
    if (raw === undefined || raw === null || Math.floor(Number(raw)) < 0) return fallback;

    const value = Math.floor(Number(raw));
    if (!Number.isFinite(value)) {
      throw new Error(`endless ${name} is ${raw}, which is not a number`);
    }
    if (value > max) {
      throw new Error(
        `endless ${name} is ${value}, above the supported maximum ${max}; XP is floored ` +
        "(`ProgressionStore`) so a keeper level paid by mistake can never be taken back"
      );
    }
    return value;
  };

  const xpPerWave = read(endless.xpPerWave, DEFAULT_XP_PER_WAVE, MAX_XP_PER_WAVE, "xpPerWave");
  const maxWaves = read(endless.maxWaves, DEFAULT_MAX_WAVES, HARD_MAX_WAVES, "maxWaves");

  // A nought in either field means the lane pays nothing, published as authored and not
  // repaired: `EndlessRewardTable.Resolve` says the same, and the two halves disagreeing about a
  // keeper level is worse than any guess at what a typo meant (invariant 19a). An *unwritten*
  // field is -1 and inherits above, so a rate with no bound at all cannot be expressed.
  return { xpPerWave, maxWaves };
}

function readGolden(progression) {
  const golden = progression.golden;

  if (!golden || !Array.isArray(golden.bands) || golden.bands.length === 0) {
    // Absent is legitimate: every glade then pays exactly what its reward rule says, which
    // is what a client with no golden block also does. Silent agreement, not a failure.
    return null;
  }

  return golden.bands.map((band, index) => {
    const percent = Math.floor(Number(band?.percent));
    const weight = Math.floor(Number(band?.weight));

    if (!Number.isFinite(percent) || percent < 100) {
      throw new Error(
        `golden band ${index} pays ${band?.percent}%; the bonus may only ever add, so a ` +
        "band under 100 would pay a player less for a glade than the reward rule promises"
      );
    }
    if (!Number.isFinite(weight) || weight < 1) {
      throw new Error(
        `golden band ${index} has weight ${band?.weight}; remove the band rather than ` +
        "weighting it to nothing, so the published odds stay a list a player can read"
      );
    }

    return { percent, weight };
  });
}

/**
 * What each rewarded placement pays, published so the server can grant its own figure.
 *
 * Shaped as a map keyed by placement id rather than the array `progression.json` authors,
 * because the server only ever asks about one placement at a time and a map makes that a
 * lookup instead of a scan. Same reasoning as `levelChapters` above.
 *
 * Unlike the daily block this is <em>optional</em>. A deployment with no ad placements
 * configured is a coherent thing — it is what this project was until today — and refusing
 * to seed over it would mean the reward table could not be published without an ad
 * network. What is not coherent is a placement the client offers and the server has never
 * heard of, so anything present is validated strictly.
 */
function readAds(progression) {
  const ads = progression.ads;
  if (!ads || !Array.isArray(ads.placements) || ads.placements.length === 0) return null;

  // `run_continue` is a retired placement id and is deliberately absent — see AD_PLACEMENTS
  // in functions/src/ads.ts. `run_time` stays in the kind list so a stale published config
  // is rejected by the rule below with a sentence naming the real problem, rather than by
  // "unknown reward kind", which reads like a typo.
  const known = ["heart_refill", "coin_bonus", "win_bonus", "hint_refill"];
  const kinds = ["credits", "gems", "hearts", "heart_boost", "run_time", "hints"];

  // Mirrors the same rule on the client (`AdRewardTable.TryReadOffer`). A kind spent inside
  // a run makes sense only on a placement offered from inside one, and nothing is any more:
  // the countdown the continue extended is gone. The failure it prevents is silent on both
  // sides — an offer drawn where no run exists, a video watched, a reward applied to nothing.
  const transient = ["run_time"];
  const placements = {};

  for (const placement of ads.placements) {
    const id = placement?.id;

    if (!known.includes(id)) {
      throw new Error(
        `ads names unknown placement '${id}'. The server grants only placements it knows, ` +
        `so publishing one it does not would make every claim for it fail silently.`
      );
    }

    if (placements[id]) throw new Error(`ads names placement '${id}' twice`);

    if (!kinds.includes(placement.kind)) {
      throw new Error(`ads placement '${id}' names unknown reward kind '${placement.kind}'`);
    }

    if (transient.includes(placement.kind)) {
      throw new Error(
        `ads placement '${id}' pays '${placement.kind}', which is spent inside a run; ` +
        `nothing is offered from inside one`
      );
    }

    const amount = Math.floor(placement.amount ?? 0);
    if (!Number.isFinite(amount) || amount < 1) {
      throw new Error(`ads placement '${id}' pays ${placement.amount}; it must be at least 1`);
    }

    // The daily cap is deliberately not published. It bounds what the client offers, and
    // the server does not enforce it — an ad grant is already bounded by something far
    // stronger, namely a signed callback from the ad network for every single view.
    placements[id] = { kind: placement.kind, amount };
  }

  return { placements, wheel: readWheel(ads, placements) };
}

/**
 * The bonus wheel, published so the server can arrive at the same slice the phone drew.
 *
 * <p>
 * Optional, and its absence means the flat offer rather than a default ladder — a published
 * config that carries no wheel is one whose `adReward` pays exactly the authored amount, and
 * a client that hears no wheel back draws no wheel. That pairing is what removes the
 * deploy-ordering hazard from this feature entirely (invariant 12a): shipping either half
 * first costs a feature nobody has seen, never a payout nobody honours.
 * </p>
 * <p>
 * The rules below are `BonusWheel.Resolve`'s, refused rather than repaired. A seeder that
 * quietly fixed a slice would publish a table the client had already rejected, and the two
 * would disagree about money — which is the one thing this file exists to prevent.
 * </p>
 */
function readWheel(ads, placements) {
  const wheel = ads.wheel;
  if (!wheel) return undefined;

  if (!Array.isArray(wheel.slices) || wheel.slices.length < 4 || wheel.slices.length > 12) {
    throw new Error(
      `ads wheel has ${wheel.slices?.length ?? 0} slices; it must have between 4 and 12. ` +
      `Fewer than four is a coin flip drawn as a wheel, and more than twelve cannot be read ` +
      `while it turns.`
    );
  }

  // A wheel is `win_bonus`'s payout made variable, not a reward of its own. Without that
  // placement there is no amount to multiply and the wheel would silently pay nothing.
  if (!placements.win_bonus) {
    throw new Error(
      `ads authors a wheel but no 'win_bonus' placement for it to multiply.`
    );
  }

  const slices = [];
  let anyBonus = false;
  let total = 0;

  for (let i = 0; i < wheel.slices.length; i++) {
    const percent = Math.floor(wheel.slices[i]?.percent ?? 0);

    if (!Number.isFinite(percent) || percent < 100) {
      throw new Error(
        `ads wheel slice ${i} pays ${wheel.slices[i]?.percent}%; it may never be below 100. ` +
        `The wheel only ever adds — a slice under 100 would pay less than the flat offer the ` +
        `button promised.`
      );
    }

    if (percent > 1000) {
      throw new Error(`ads wheel slice ${i} pays ${percent}%, above the supported 1000%`);
    }

    slices.push({ percent });
    anyBonus ||= percent > 100;
    total += percent;
  }

  if (!anyBonus) {
    throw new Error(
      `ads wheel has no slice paying above the flat offer; every spin would land on the same ` +
      `figure, so the wheel is a spin animation in front of a fixed number.`
    );
  }

  // Printed rather than checked, because what the placement *should* pay is an economy
  // decision and not this script's to make. What it must never be is a surprise: the mean
  // multiplier is the real payout of every `win_bonus` view from the moment this is published.
  const mean = Math.round(total / slices.length);
  const base = placements.win_bonus.amount;
  console.log(`  wheel: ${slices.length} slices, mean ${mean}% — ` +
              `win_bonus really pays ~${Math.floor((base * mean) / 100)} ${placements.win_bonus.kind} a view ` +
              `(flat was ${base})`);

  return { slices };
}

/**
 * The daily chest table, published verbatim so the server can re-roll a chest for
 * itself.
 *
 * This is not optional tuning. `claimAwards` recomputes what a chest was worth rather
 * than believing the client, and it cannot do that without the same weights and bands
 * the client rolled against. A config document missing this block makes the server
 * refuse every award — deliberately, since granting a guess would be inventing money —
 * so it is validated here rather than discovered in production.
 */
function readDaily(progression) {
  const daily = progression.daily;

  if (!daily || !Array.isArray(daily.chests) || daily.chests.length === 0) {
    throw new Error(
      "progression.json has no 'daily' block. The server re-rolls each chest to decide " +
      "what it pays, so seeding without one would make every daily chest fail to grant."
    );
  }

  const chests = daily.chests.map((chest, index) => {
    const guaranteed = (chest.guaranteed ?? []).map((band) => band8(band, index, "guaranteed"));
    if (guaranteed.length === 0) {
      throw new Error(`daily chest ${index} guarantees nothing; every chest must pay something`);
    }

    const options = (chest.options ?? []).map((option) => ({
      ...band8(option, index, "option"),
      weight: Math.max(1, Math.floor(option.weight ?? 1)),
    }));

    return { guaranteed, options };
  });

  return { runsPerChest: Math.max(1, Math.floor(daily.runsPerChest ?? 3)), chests };
}

/**
 * What a *streak* rung may pay.
 *
 * Narrower than what a chest may pay, and the two used to be one set — which meant a chest
 * band naming `hints` threw here as an "unknown reward kind" even though the client has
 * shipped that kind since v19. One set for two questions is how a seeder comes to refuse
 * correct content; see CHEST_KINDS.
 */
const STREAK_KINDS = new Set(["credits", "gems", "hearts", "heart_boost"]);

/**
 * What a *chest* may pay: everything a streak may, plus the kinds that are banked on the
 * phone and never adjudicated.
 *
 * The server does not grant these — `chestCurrencyValue` sums by currency and ignores
 * everything else — but it must still accept them, because the published table is what it
 * re-rolls against and a band it refuses to publish is a band whose *streams* would be
 * missing. That is the one way a non-currency kind can move real money: drop a band from
 * the config and every guaranteed band after it draws on a different stream number, so the
 * server and the client disagree about what a chest paid in credits.
 */
const CHEST_KINDS = new Set([...STREAK_KINDS, "hints", "utility"]);

/** Chest kinds that are incomplete without an `item`. Mirrors `ChestDropKinds.NeedsItem`. */
const KINDS_NEEDING_ITEM = new Set(["utility"]);

/**
 * The streak ladder, published so the server can pay a night without asking the client
 * what a night is worth.
 *
 * <p>Until the ladder paid currency there was nothing here at all, and nothing missed it:
 * hearts and boosts are applied on the phone and the server has no opinion about them.
 * A currency rung changes that completely — `claimAwards` reads this table and grants its
 * own figure — so a ladder retuned in progression.json and not re-seeded means the game
 * shows one number and the wallet receives another, every night, for every player.</p>
 *
 * <p>The order is the whole meaning of the list, so this refuses rather than skips, exactly
 * as `StreakTable.Resolve` does: dropping one rung renumbers every night above it and
 * quietly changes what every player is owed. The ceilings are the client's, restated
 * because a seeder that published a figure the server would clamp differently is a seeder
 * that publishes a disagreement.</p>
 */
/**
 * The task slates and their chest ladder, published verbatim so the server can re-roll a
 * task's chest and bound a period's claims. Refused whole on anything malformed, for
 * `readDaily`'s reason: a config document without a usable block leaves every task claim
 * unconfirmed, deliberately, so it is validated here rather than discovered in production.
 */
function readTasks(progression) {
  const tasks = progression.tasks;

  if (!tasks || !Array.isArray(tasks.tiers) || tasks.tiers.length === 0) {
    throw new Error(
      "progression.json has no 'tasks' block. The server re-rolls each task chest to decide " +
      "what it pays, so seeding without one would leave every task claim unconfirmed."
    );
  }

  const ID = /^[a-z0-9_]{1,32}$/;
  const tierIds = new Set();

  const tiers = tasks.tiers.map((tier, index) => {
    if (!tier || !ID.test(tier.id ?? "")) throw new Error(`tasks tier ${index} has a bad id '${tier?.id}'`);
    if (tierIds.has(tier.id)) throw new Error(`tasks tier '${tier.id}' is listed twice`);
    tierIds.add(tier.id);

    const chest = tier.chest ?? {};
    const guaranteed = (chest.guaranteed ?? []).map((band) => band8(band, `tier ${tier.id}`, "guaranteed"));
    if (guaranteed.length === 0) throw new Error(`tasks tier '${tier.id}' guarantees nothing`);

    const options = (chest.options ?? []).map((option) => ({
      ...band8(option, `tier ${tier.id}`, "option"),
      weight: Math.max(1, Math.floor(option.weight ?? 1)),
    }));

    // `marks` is published for completeness and read by nobody on this side: the season
    // track's pace is a client and content concern, and the server's whole interest in a
    // season is which chest a rung pays. Publishing it keeps the config document a faithful
    // copy of the authored table, which is what makes a support question answerable.
    const marks = Math.max(0, Math.floor(tier.marks ?? 0));
    return { id: tier.id, chest: { guaranteed, options }, ...(marks > 0 ? { marks } : {}) };
  });

  const ids = new Set();
  const slate = (entries, period) => {
    if (!Array.isArray(entries) || entries.length === 0) throw new Error(`tasks block lists no ${period} tasks`);

    return entries.map((entry, index) => {
      if (!entry || !ID.test(entry.id ?? "")) throw new Error(`${period} task ${index} has a bad id '${entry?.id}'`);
      if (ids.has(entry.id)) throw new Error(`task id '${entry.id}' is listed twice`);
      ids.add(entry.id);

      if (!tierIds.has(entry.tier)) throw new Error(`${period} task '${entry.id}' pays unknown tier '${entry.tier}'`);
      const target = Math.floor(entry.target ?? 0);
      if (target < 1) throw new Error(`${period} task '${entry.id}' has target ${target}`);

      const row = { id: entry.id, goal: String(entry.goal ?? ""), target, tier: entry.tier };
      return entry.retired ? { ...row, retired: true } : row;
    });
  };

  return {
    activePerPeriod: Math.max(1, Math.floor(tasks.activePerPeriod ?? 3)),
    tiers,
    daily: slate(tasks.daily, "daily"),
    weekly: slate(tasks.weekly, "weekly"),
  };
}

function readStreak(progression, tierIds) {
  const streak = progression.streak;

  // Absent is legitimate: the client falls back to its built-in ladder, and so does a
  // server with no table — it grants nothing and leaves the claim pending rather than
  // guessing. Worth saying out loud, because "the streak stopped paying" is otherwise a
  // silent symptom of an edit to the wrong file.
  if (!streak || !Array.isArray(streak.rungs) || streak.rungs.length === 0) {
    console.log("  note: progression.json has no 'streak' block, so currency rungs cannot be " +
                "granted by the server. The client's built-in ladder still draws.");
    return null;
  }

  if (streak.rungs.length > MAX_STREAK_RUNGS) {
    throw new Error(
      `streak lists ${streak.rungs.length} rungs, above the supported ${MAX_STREAK_RUNGS}`
    );
  }

  const rungs = streak.rungs.map((rung, index) => {
    const night = index + 1;

    // An empty entry is how a night that pays nothing is authored.
    if (!rung || (!rung.kind && !rung.tier)) return { kind: "", amount: 0 };

    // A chest night. The tier has to exist in the tasks block this same run publishes, or
    // `claimAwards` would leave every claim against it unconfirmed for ever — which is the
    // safe half of invariant 13a and pays nobody.
    if (rung.tier) {
      if (rung.kind) {
        throw new Error(
          `streak night ${night} names both a chest tier '${rung.tier}' and a reward kind ` +
          `'${rung.kind}'; a night pays one or the other`
        );
      }
      if (!tierIds.has(rung.tier)) {
        throw new Error(
          `streak night ${night} pays chest tier '${rung.tier}', which the tasks block does ` +
          `not define`
        );
      }
      return { kind: "", amount: 0, tier: rung.tier };
    }

    if (RETIRED_STREAK_KINDS.has(rung.kind)) {
      throw new Error(
        `streak night ${night} pays '${rung.kind}', which a streak rung may no longer name: ` +
        `the streak pays credits, gems and chests. Name a chest tier instead, so one ` +
        `published disclosure covers every night that pays it.`
      );
    }

    if (!STREAK_KINDS.has(rung.kind)) {
      throw new Error(`streak night ${night} names unknown reward kind '${rung.kind}'`);
    }

    const amount = Math.floor(rung.amount ?? 0);
    if (!Number.isFinite(amount) || amount < 1) {
      throw new Error(
        `streak night ${night} pays ${rung.amount}; leave the kind empty for a night that ` +
        `pays nothing rather than authoring a zero`
      );
    }

    const ceiling = maxStreakAmount(rung.kind);
    if (amount > ceiling) {
      throw new Error(
        `streak night ${night} pays ${amount} ${rung.kind}, above the supported ${ceiling}. ` +
        `The client clamps to the same figure, so publishing this would seed a ladder that ` +
        `disagrees with the one players see — raise StreakRules, streak.ts and this together.`
      );
    }

    return { kind: rung.kind, amount };
  });

  if (!rungs.some((rung) => rung.kind || rung.tier)) {
    throw new Error("the streak ladder pays nothing on any night; refusing to seed it");
  }

  // The shield's two numbers are published for completeness and read by nobody on this
  // side, exactly as a tier's `marks` are. A shield is an ordinary gem spend on the client
  // that stores one date in the save; it grants no currency and gates no payout, and
  // `advances` bounds a protected streak to the same one-night-a-day an unprotected one is
  // held to. Publishing them keeps the config document a faithful copy of the authored
  // table, which is what makes a support question answerable.
  const shieldDays = Math.floor(streak.shieldDays ?? 0);
  const shieldGems = Math.floor(streak.shieldGems ?? 0);

  return {
    rungs,
    ...(shieldDays > 0 ? { shieldDays } : {}),
    ...(shieldGems > 0 ? { shieldGems } : {}),
  };
}

/**
 * Kinds a streak rung may no longer name. Refused by name rather than ignored, for
 * `StreakRules.IsRetiredKind`'s reason: skipping one would renumber every night above it.
 */
const RETIRED_STREAK_KINDS = new Set(["hearts", "heart_boost"]);

/** Mirrors `StreakRules`. See `readStreak`. */
const MAX_STREAK_RUNGS = 30;

/**
 * Refer-a-friend (invariant 51): the milestone chapter, the cap and the two payments.
 *
 * Flat, by the owner's decision on 2026-09-17: every finished invitee pays the referrer
 * `perInvitee` and the invitee is paid `invitee` on finishing. `tierIds` is the published
 * tasks block's own ladder, for `readStreak`'s reason: a payment naming a tier nothing defines
 * is a chest the server can never price. The milestone has to be a chapter the manifest ships
 * *enabled* — `levelChapters` is built from those alone — because a milestone behind a
 * disabled chapter is a payout nobody can reach.
 *
 * Absent, or `withdrawn`, withdraws the feature server-side: every referral callable then
 * answers an empty state and pays nothing. Said out loud, because "the invite page stopped
 * paying" is otherwise a silent symptom of an edit to the wrong file.
 */
function readReferral(progression, tierIds, levelChapters) {
  const block = progression.referral;

  if (!block || block.withdrawn) {
    console.log("  note: progression.json has no live 'referral' block, so the referral callables " +
                "answer an empty state and pay nothing.");
    return null;
  }

  const chapter = String(block.milestoneChapter ?? "");
  if (!chapter) throw new Error("referral block names no milestoneChapter");
  if (!Object.values(levelChapters).includes(chapter)) {
    throw new Error(`referral milestoneChapter '${chapter}' is not an enabled chapter in the manifest; ` +
                    "a milestone nobody can reach is a payout nobody can earn");
  }

  const payment = (raw, role) => {
    const tier = String(raw?.tier ?? "");
    if (!tierIds.includes(tier)) {
      throw new Error(`referral ${role} tier '${tier}' is not a tier the tasks block defines`);
    }
    const count = raw?.count === undefined ? 1 : Math.floor(raw.count);
    if (!Number.isInteger(count) || count < 1 || count > 4) {
      throw new Error(`referral ${role} pays ${raw?.count} chests; it must be 1..4`);
    }
    return { tier, count };
  };

  const invitee = payment(block.invitee, "invitee");
  const perInvitee = payment(block.perInvitee, "perInvitee");

  const maxBound = Math.floor(block.maxBound ?? 0);
  if (!Number.isInteger(maxBound) || maxBound < 1 || maxBound > 500) {
    throw new Error(`referral maxBound is ${block.maxBound}; it must be 1..500`);
  }

  console.log(`  referral: milestone '${chapter}', ${perInvitee.count}x '${perInvitee.tier}' to the referrer ` +
              `per finished invitee up to ${maxBound}, ${invitee.count}x '${invitee.tier}' to the invitee`);

  return { milestoneChapter: chapter, maxBound, invitee, perInvitee };
}

function maxStreakAmount(kind) {
  if (kind === "credits") return 2000;
  if (kind === "gems") return 100;
  return 72;
}

function band8(band, chestIndex, role) {
  // A daily chest is named by its index and a task tier by a label; both read as "chest ...".
  const where = typeof chestIndex === "number" ? `daily chest ${chestIndex}` : `chest ${chestIndex}`;

  if (!band || !CHEST_KINDS.has(band.kind)) {
    throw new Error(`${where} ${role} names unknown reward kind '${band?.kind}'`);
  }

  const min = Math.floor(band.min ?? 0);
  const max = Math.floor(band.max ?? 0);

  if (min < 1 || max < min) {
    throw new Error(`${where} ${role} '${band.kind}' has band ${min}..${max}`);
  }

  // A kind that names a thing and does not name one would be drawn on the panel as a prize
  // and grant nothing. Mirrors `DailyChestTable.TryReadBand`, which refuses the same band.
  const item = typeof band.item === "string" ? band.item : "";

  if (KINDS_NEEDING_ITEM.has(band.kind) && !item) {
    throw new Error(
      `${where} ${role} pays '${band.kind}' and names no item; a chest ` +
      "cannot hand over a utility without saying which"
    );
  }

  // The item is published even though the server never grants one, so `config/daily` is a
  // faithful copy of what the client rolled. A published table that quietly differed from
  // the authored one is the thing this whole function exists to prevent.
  return item ? { kind: band.kind, min, max, item } : { kind: band.kind, min, max };
}

// -------------------------------------------------------- Firestore REST encoding
function encode(value) {
  if (value === null || value === undefined) return { nullValue: null };
  if (typeof value === "boolean") return { booleanValue: value };
  if (typeof value === "number") {
    return Number.isInteger(value) ? { integerValue: String(value) } : { doubleValue: value };
  }
  if (typeof value === "string") return { stringValue: value };
  if (Array.isArray(value)) return { arrayValue: { values: value.map(encode) } };

  const fields = {};
  for (const [k, v] of Object.entries(value)) fields[k] = encode(v);
  return { mapValue: { fields } };
}

function accessToken() {
  try {
    // execSync rather than execFileSync-with-shell: gcloud is a .cmd on Windows and
    // needs a shell, but passing an argument array through one is a quoting hazard
    // Node now warns about. The command is a constant, so a single string is safe.
    return execSync("gcloud auth print-access-token", {
      encoding: "utf8", stdio: ["ignore", "pipe", "pipe"],
    }).trim();
  } catch (e) {
    throw new Error(
      "could not get a token from gcloud. Run 'gcloud auth login' first.\n" + (e.stderr ?? e.message)
    );
  }
}

/**
 * Writes one document.
 *
 * By default only the named fields are touched, which is right for `config/progression`:
 * it is assembled from several readers, and a field one of them declined to produce must
 * not delete what is already published. With `replace`, the update mask is dropped and the
 * document becomes exactly what is passed, which is right for `config/products` — a
 * product deleted from the content file has to stop being sellable, and a merge would
 * leave the server honouring receipts for something the shop no longer offers.
 */
async function writeDoc(token, path, data, options = {}) {
  const fields = {};
  for (const [k, v] of Object.entries(data)) fields[k] = encode(v);

  const mask = options.replace
    ? ""
    : `?${Object.keys(data).map((k) => `updateMask.fieldPaths=${encodeURIComponent(k)}`).join("&")}`;

  const url =
    `https://firestore.googleapis.com/v1/projects/${PROJECT}/databases/(default)/documents/${path}` +
    mask;

  const response = await fetch(url, {
    method: "PATCH",
    headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
    body: JSON.stringify({ fields }),
  });

  if (!response.ok) {
    throw new Error(`writing ${path} failed: ${response.status} ${await response.text()}`);
  }
}

// ------------------------------------------------------------------------- main
const { config, levelCount, products } = buildProgressionConfig();

// A season's pass price is one number in the manifest with nothing on the other side of it
// to drift from, so the cross-file link this used to check is gone. What is left is that a
// season selling one actually names a price the server can enforce — without it,
// `submitSpends` has nothing to compare a pass debit against and the paid column would cost
// whatever a client says.
for (const event of config.events ?? []) {
  const gems = event.passGems ?? 0;
  const sells = (event.milestones ?? []).some((rung) => rung.premiumTier);

  if (sells && !(Number.isSafeInteger(gems) && gems > 0)) {
    throw new Error(`season '${event.id}' pays a pass track but prices no pass; the server ` +
                    "would have nothing to check a pass debit against");
  }
  if (!sells && gems > 0) {
    throw new Error(`season '${event.id}' prices a pass at ${gems} gems but pays nothing on ` +
                    "the pass track");
  }
}
if (process.argv.includes("--check")) {
  // The Infinite lane's figures, printed here as well as by both content gates. It is the only
  // block this seeder publishes that decides *XP*, and the server deriving a keeper level the
  // device does not is a published card that silently drops what that level gated (19a) - so
  // "what did I just agree to send" is worth one line in the one place a deploy reads.
  const lane = config.endless;
  console.log(
    lane.xpPerWave > 0 && lane.maxWaves > 0
      ? `  endless: ${lane.xpPerWave} xp a wave, capped at ${lane.maxWaves.toLocaleString("en-GB")} ` +
        `lifetime wave(s) (${(lane.xpPerWave * lane.maxWaves).toLocaleString("en-GB")} xp)`
      : "  endless: withdrawn - the Infinite lane pays no XP"
  );
  console.log(`Validated ${levelCount} levels, ${Object.keys(products ?? {}).length} store products and season pass prices. No remote writes.`);
  process.exit(0);
}

// **An argument this tool does not recognise stops it, and that guard was bought the hard
// way.** Writing is the default here, so anything that is not `--check` publishes — and a
// run typed as `--help`, `--dry-run` or `-n`, every one of which reads as "show me what you
// would do", silently republished the whole working tree to the live project instead. It is
// the same class of fault as a flood keyer that keeps almost nothing (`make_siege_art`): the
// tool did exactly what it was told and the sentence the operator read was not the sentence
// the tool heard. There is no dry-run flag beyond `--check`, which is what this says.
const KNOWN_FLAGS = new Set(["--check"]);
const unknown = process.argv.slice(2).filter((arg) => !KNOWN_FLAGS.has(arg));

if (unknown.length > 0) {
  console.error(`seed-config: unrecognised argument(s) ${unknown.join(" ")}.`);
  console.error("This tool PUBLISHES to the live project when run with no arguments;");
  console.error("the only flag it takes is --check, which validates and writes nothing.");
  console.error("Nothing was sent.");
  process.exit(2);
}

const token = accessToken();

await writeDoc(token, "config/progression", config);
console.log(
  `config/progression: ${levelCount} level(s), ` +
  `${Object.keys(config.chapterRewards).length} chapter override(s), ` +
  `${config.daily.chests.length} daily chest(s) every ${config.daily.runsPerChest} run(s), ` +
  `${config.tasks.daily.length} daily / ${config.tasks.weekly.length} weekly task(s) over ` +
  `${config.tasks.tiers.length} chest tier(s), ` +
  `${config.ads ? Object.keys(config.ads.placements).length : 0} ad placement(s), ` +
  `endless ${config.endless.xpPerWave} xp/wave capped at ${config.endless.maxWaves} wave(s) ` +
  `(${config.endless.xpPerWave * config.endless.maxWaves} xp), ` +
  `seeds ${config.seeds.credits} credits / ${config.seeds.gems} gems`
);

// The shop, derived from progression.json rather than hand-maintained beside it. See
// `readStore`. Written as a full replacement rather than a merge, deliberately: a product
// removed from the content file must stop being sellable, and a merge would leave the
// server honouring receipts for something the shop no longer offers.
if (products) {
  await writeDoc(token, "config/products", products, { replace: true });

  const ids = Object.keys(products);
  const gemPacks = ids.filter((id) => products[id].gems > 0 && products[id].credits === 0).length;
  const coinPacks = ids.filter((id) => products[id].credits > 0 && products[id].gems === 0).length;
  const vessels = ids.filter((id) => (products[id].capacity ?? 0) > 0).length;

  console.log(
    `config/products: ${ids.length} product(s) — ${gemPacks} gem, ${coinPacks} coin, ` +
    `${vessels} heart container, ` +
    `${ids.length - gemPacks - coinPacks - vessels} bundle`
  );
} else {
  console.log("config/products: skipped, progression.json has no store block — purchases stay inert");
}

// The grove catalog, so the boards can be scored without believing any client. Written as
// a full replacement rather than a merge, for `config/products`' reason: a piece removed
// from the content file must stop being worth anything, and a merge would leave the server
// valuing groves against a catalog nobody ships.
const grove = buildGroveConfig();
await writeDoc(token, "config/grove", grove, { replace: true });

const groveTotal =
  Object.values(grove.pieces).reduce((sum, cost) => sum + cost, 0) +
  Object.values(grove.regions).reduce((sum, cost) => sum + cost, 0) +
  Object.values(grove.companions).reduce((sum, entry) => sum + entry.cost, 0);

console.log(
  `config/grove: v${grove.version} — ${Object.keys(grove.pieces).length} priced piece(s), ` +
  `${Object.keys(grove.regions).length} region(s), ` +
  `${Object.keys(grove.companions).length} companion(s), ` +
  `${Object.keys(grove.dwellings).length} home rung(s) ` +
  `(${Object.keys(grove.dwellingLevels).length} gated), ` +
  `${Object.keys(grove.wards).length} turret(s), ` +
  `${grove.stars.length} star(s) up to ${grove.stars[grove.stars.length - 1].toLocaleString()}, ` +
  `a complete grove worth ${groveTotal.toLocaleString()}`
);

// ------------------------------------------------------------------- keeper names
//
// The word list, so a slur can be added — or a false positive removed — without a deploy.
//
// It is published from the same file the deployment compiles in, which is what makes the
// document an *override* rather than a second source of truth: `blocklist.ts` runs the
// compiled list when this is absent, so a project that has never been seeded still filters,
// and a seeded one runs exactly what was reviewed in the repository. The only thing that ever
// differs between them is a hand edit made in the console during an incident, which is
// precisely what this document exists for and is meant to be reconciled back into
// `Tools/make_name_blocklist.py` afterwards.
//
// Replaced rather than merged, for `config/products`' reason and one of its own: a merge would
// leave a word that had been *deliberately removed* still in the document, which is the one
// direction of this that costs a real player their name.
const blocklistPath = join(HERE, "..", "functions", "src", "name-blocklist.json");
const blocklist = JSON.parse(readFileSync(blocklistPath, "utf8"));

// The seeder refuses rather than warns, because a truncated word list reads from every side
// exactly like a word list with nothing to catch. `blocklist.ts` has the same guard on the
// reading end — this one is here so the mistake never reaches the database at all.
if (!Array.isArray(blocklist.anywhere) || blocklist.anywhere.length < 20
    || !Array.isArray(blocklist.exact) || blocklist.exact.length < 1000) {
  throw new Error(
    "name-blocklist.json looks truncated; run: python Tools/make_name_blocklist.py"
  );
}

await writeDoc(token, "config/names", {
  version: blocklist.version,
  anywhere: blocklist.anywhere,
  exact: blocklist.exact,
  reserved: blocklist.reserved,
  allow: blocklist.allow,

  // How many distinct players must report a name before it comes off the boards on its own.
  // Published here rather than hard-coded so the balance between "an offensive name stands for
  // hours" and "three accounts can hide an innocent one" can be moved at three in the morning.
  // Clamped to 2..100 on the reading end, so this can never be pushed to one.
  reportThreshold: 3,
}, { replace: true });

console.log(
  `config/names: v${blocklist.version} — ${blocklist.anywhere.length} substring, ` +
  `${blocklist.exact.length} whole-word across ${blocklist.languages.length} language(s), ` +
  `${blocklist.reserved.length} reserved, ${blocklist.allow.length} allowed`
);

if (grove.stars[grove.stars.length - 1] > groveTotal) {
  console.log("warning: the top star asks for more than the whole catalog is worth — nobody can reach it");
}

// Sanity: a chapter file on disk that nobody lists is usually a mistake worth naming.
const onDisk = readdirSync(join(CONTENT, "chapters")).filter((f) => f.endsWith(".json")).length;
const listed = (readJson(join(CONTENT, "manifest.json")).chapters ?? []).length;
if (onDisk !== listed) {
  console.log(`note: ${onDisk} chapter file(s) on disk, ${listed} listed in the manifest`);
}
