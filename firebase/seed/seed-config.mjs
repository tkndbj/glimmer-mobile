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
 * `config/grove` — what is left of it after the Grovement was removed on 2026-09-21.
 *
 * **It is the turret roster now, and the name is the only grove left in it.** The document
 * is `groves/{uid}`'s config and the collection spelling is permanent (invariant 19o), so
 * the document keeps its name; what it holds is `wards`, read by `buildCard` and by nothing
 * else. The piece, bundle, region, dwelling, companion and star tables went with
 * `homestead.json` — they existed so the server could score a grove without believing a
 * client, and there is no grove to score.
 *
 * **`groveWorth` is still deployed and is not being removed here.** It reads these tables and
 * will now find them empty, so every published card scores nought — which is correct and
 * invisible: the finest-groves board has not been drawn since 2026-09-15, and the Endless
 * Watch is ordered on waves. Re-seeding is therefore safe in either order with the client.
 */
function buildGroveConfig() {
  const manifest = readJson(join(CONTENT, "manifest.json"));

  // The shelf is authored in `progression.json`, which is where the roster is read from.
  const progression = readJson(join(CONTENT, "progression.json"));

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
  //
  // `legendary` rides beside it and **nothing on the server reads it any more**. It was
  // published so the copy rule could be derived — a colourless turret was bought outright and
  // bounded by a count of copy rows — and a legendary is bought per seat like every other turret
  // now (invariant 42k), so `ownsWard` answers the seat for the whole roster. It stays because
  // it is true, because `config/grove` describing the roster honestly costs nothing, and because
  // dropping it would be a re-seed bought with no answer that changes. Absent still means false.
  const wards = {};
  for (const model of progression.wards?.models ?? []) {
    if (!model?.id) continue;
    if (typeof model.id !== "string" || model.id.length === 0 || model.id.length > 64) continue;

    const gems = Math.floor(model.gemPrice ?? 0);
    const coins = Math.floor(model.coinPrice ?? 0);

    wards[model.id] = {
      level: Math.max(0, Math.floor(model.minLevel ?? 0)),
      free: gems <= 0 && coins <= 0,
      legendary: model.legendary === true,
    };
  }

  if (Object.keys(wards).length === 0) {
    throw new Error("progression.json has no turret roster; every published card would carry " +
                    "an empty line and every public profile would draw four starters");
  }

  // The tables the Grovement used to fill are written **empty rather than omitted**. An
  // absent key and an empty map read the same to `groveWorth`, but a deployed reader that
  // indexes one without checking would throw on the first and score nought on the second —
  // and scoring nought is the answer this change wants. The same reasoning keeps `version`:
  // it is the only field the deployed code compares, and dropping it would make a re-seeded
  // document look older than the one it replaced.
  return {
    version: 1,
    pieces: {},
    bundles: {},
    regions: {},
    companions: {},
    dwellings: {},
    dwellingLevels: {},
    wards,
    stars: [],
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
      xpBoost: readXpBoost(progression),
      challenges: readChallenges(),
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

      // Read last because it is the only block that has to be proved against the *catalog*
      // this same run derived: a rung scoped to a chapter nobody ships is a line that can
      // never be met, which is a badge nobody can ever earn, and there is no other file in
      // this project that can see both halves.
      ranks: readRanks(progression, levelChapters, manifest),
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
    const GOOD_KINDS = ["hearts", "heart_boost", "xp_boost"];
    if (!GOOD_KINDS.includes(good.kind)) {
      throw new Error(
        `store good '${good.id}' names kind '${good.kind}'. Only ${GOOD_KINDS.join(", ")} can ` +
        "be bought with gems — currency cannot, because only the server may grant it"
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
/**
 * The XP boost's ceiling, published so the server can clamp a stored bonus.
 *
 * **Only `maxPercent` goes over.** The windows and the cooldown are facts about offering a
 * boost, which no server does; this side only answers how much of a stored bonus could honestly
 * have been earned. Mirrors `XpBoostTable` on the client and `DEFAULT_XP_BOOST` on the server.
 */
function readXpBoost(progression) {
  const DEFAULT_MAX_PERCENT = 150;
  const MAX_PERCENT = 1000;

  const boost = progression.xpBoost;

  // Absent means the built-in figure, which is what a client with no block also uses — see
  // `readEndless` below for why this agrees rather than failing closed.
  if (!boost) return { maxPercent: DEFAULT_MAX_PERCENT };

  const raw = boost.maxPercent;
  if (raw === undefined || raw === null || Math.floor(Number(raw)) < 0) {
    return { maxPercent: DEFAULT_MAX_PERCENT };
  }

  const maxPercent = Math.floor(Number(raw));
  if (!Number.isFinite(maxPercent)) {
    throw new Error(`xpBoost maxPercent is ${raw}, which is not a number`);
  }
  if (maxPercent > MAX_PERCENT) {
    throw new Error(
      `xpBoost maxPercent is ${maxPercent}, above the supported maximum ${MAX_PERCENT}; XP is ` +
      "floored (`ProgressionStore`) so a keeper level paid by mistake can never be taken back"
    );
  }

  // A cap under what one window already pays is a window a player is shown and never given.
  // Refused here rather than repaired, because the seeder is the last gate before every account.
  const single = Math.max(
    Math.floor(Number(boost.watchedPercent ?? 0)) || 0,
    Math.floor(Number(boost.boughtPercent ?? 0)) || 0
  );
  if (maxPercent > 0 && single > maxPercent) {
    throw new Error(
      `xpBoost maxPercent is ${maxPercent} but a single window pays ${single}%; the window would ` +
      "be advertised at a figure the rule refuses to honour"
    );
  }

  return { maxPercent };
}

function readEndless(progression) {
  const DEFAULT_XP_PER_WAVE = 15;
  const DEFAULT_MAX_WAVES = 99990;
  const HARD_MAX_WAVES = 1000000;
  const MAX_XP_PER_WAVE = 1000;

  // `EndlessLimits`, the credit half. Tighter ceilings than the XP pair carries, because a
  // credit is spendable and a keeper level is not - and the daily cap is the entire defence
  // behind a payment no server can recompute (`endless.ts`, invariants 13 and 19l).
  const DEFAULT_CREDITS_PER_WAVE = 30;
  const MAX_CREDITS_PER_WAVE = 200;
  const DEFAULT_DAILY_CREDIT_CAP = 10000;
  const MAX_DAILY_CREDIT_CAP = 25000;

  const endless = progression.endless;

  // Absent is legitimate and means the built-in figures, which is what a client with no block
  // also does — see `EndlessRewardTable.Resolve` for why this one agrees rather than failing
  // closed. Published explicitly so the two halves cannot drift apart on a stale deploy.
  if (!endless) {
    return {
      xpPerWave: DEFAULT_XP_PER_WAVE,
      maxWaves: DEFAULT_MAX_WAVES,
      creditsPerWave: DEFAULT_CREDITS_PER_WAVE,
      dailyCreditCap: DEFAULT_DAILY_CREDIT_CAP,
    };
  }

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
  const creditsPerWave = read(endless.creditsPerWave, DEFAULT_CREDITS_PER_WAVE,
                             MAX_CREDITS_PER_WAVE, "creditsPerWave");

  const dailyCreditCap = read(endless.dailyCreditCap, DEFAULT_DAILY_CREDIT_CAP,
                              MAX_DAILY_CREDIT_CAP, "dailyCreditCap");

  // **A rate with no ceiling is the one shape this block may never publish.** Every other
  // field here errs toward paying less; this pair errs toward paying an unbounded amount of
  // spendable currency against a wave count nothing can recompute, so it is refused outright
  // rather than repaired. Withdrawing the payment is `creditsPerWave: 0`, which is authored.
  if (creditsPerWave > 0 && dailyCreditCap <= 0) {
    throw new Error(
      `endless creditsPerWave is ${creditsPerWave} with no dailyCreditCap; a credit rate with ` +
      "no daily ceiling is a wave count nothing can recompute paying unbounded currency " +
      "(invariants 13 and 19l). Set a cap, or set the rate to 0 to withdraw the payment"
    );
  }

  return { xpPerWave, maxWaves, creditsPerWave, dailyCreditCap };
}

/**
 * The daily challenges' block, read out of `challenges.json` rather than `progression.json`
 * because the two files are kept apart on purpose (invariant 56): a challenge retune ships
 * nothing of the reward table's. What is published is only what a claim is priced against —
 * the genre spellings, the free allowance, the deal rows and the two reward rates. The boards
 * never leave the device.
 *
 * Mirrors `ChallengeTable.TryBuild`'s refusals about the blocks and `ChallengeRewardRule`'s
 * about the rates. A file the client would refuse is refused here too, so the two halves can
 * never be seeded apart; a file with no `challenges.json` at all publishes the built-in rates
 * with no deals and no genres, which leaves every coin claim unconfirmed rather than paid.
 */
function readChallenges() {
  // `ChallengeLimits`, mirrored.
  const DEFAULTS = { freePlays: 2, coins: 40, xp: 20, maxClears: 25000 };
  const MAX_FREE_PLAYS = 100, MAX_TIER_PLAYS = 1000, MAX_TIER_GEMS = 100000, MAX_TIER_DAYS = 365;
  const MAX_TIERS = 16, MAX_COINS = 200, MAX_XP = 1000, HARD_MAX_CLEARS = 1000000;
  const MAX_DAILY_COINS = 10000;                       // ChallengeLimits.MaxDailyCoins
  const RETIRED_GENRES = ["sudoku", "mines", "tetris"]; // ChallengeGenres.Retired
  const RETIRED_TIERS = [];                             // ChallengeTable.RetiredTierIds
  const KEY = /^[a-z0-9_]{1,32}$/;
  const VERSION = 2;

  const path = join(CONTENT, "challenges.json");
  if (!existsSync(path)) {
    console.log("  note: challenges.json is missing, so no deal is sold and no challenge claim is paid");
    return { genres: [], freePlays: DEFAULTS.freePlays, tiers: [], coins: DEFAULTS.coins,
             xp: DEFAULTS.xp, maxClears: DEFAULTS.maxClears };
  }

  const file = readJson(path);
  if (file.schemaVersion !== VERSION) {
    throw new Error(`challenges.json is schema v${file.schemaVersion}; this seeder reads v${VERSION}`);
  }

  const whole = (raw, name, max) => {
    if (raw === undefined || raw === null) return 0;
    const value = Math.floor(Number(raw));
    if (!Number.isFinite(value) || value < 0) throw new Error(`challenges ${name} is ${raw}, which is not a whole number`);
    if (value > max) throw new Error(`challenges ${name} is ${value}, above the supported maximum ${max}`);
    return value;
  };

  const genres = [];
  for (const row of file.challenges ?? []) {
    const genre = typeof row?.genre === "string" ? row.genre : "";
    if (!KEY.test(genre)) throw new Error(`challenge '${row?.id}' names genre '${genre}', which is not key-shaped`);
    if (RETIRED_GENRES.includes(genre)) {
      throw new Error(`challenge '${row?.id}' names genre '${genre}', which was withdrawn and may never come back (5f)`);
    }
    if (!genres.includes(genre)) genres.push(genre);
  }
  genres.sort();

  const freePlays = file.allowance?.freePlays > 0
    ? whole(file.allowance.freePlays, "allowance.freePlays", MAX_FREE_PLAYS)
    : DEFAULTS.freePlays;

  // The rewards: an unwritten block inherits, a written one is read as authored — a nought
  // rate withdraws the payment, and a rate beside no ceiling is refused rather than repaired,
  // for the endless block's reason (a rate with no bound is the one shape never published).
  const rewards = file.rewards ?? {};
  const authored = (rewards.coins ?? 0) > 0 || (rewards.xp ?? 0) > 0 || (rewards.maxClears ?? 0) > 0;
  const coins = authored ? whole(rewards.coins, "rewards.coins", MAX_COINS) : DEFAULTS.coins;
  const xp = authored ? whole(rewards.xp, "rewards.xp", MAX_XP) : DEFAULTS.xp;
  const maxClears = authored ? whole(rewards.maxClears, "rewards.maxClears", HARD_MAX_CLEARS) : DEFAULTS.maxClears;
  if (xp > 0 && maxClears <= 0) {
    throw new Error("challenges rewards.xp is set with no maxClears; a rate with no ceiling pays " +
                    "nothing on both sides, which is a typo rather than a decision. Set a ceiling, " +
                    "or set xp to 0 to withdraw the payment");
  }

  const tiers = [];
  const rows = file.tiers ?? [];
  if (rows.length > MAX_TIERS) throw new Error(`challenges lists ${rows.length} deals; at most ${MAX_TIERS} are supported`);
  let lastPlays = freePlays, lastGems = 0;
  for (const row of rows) {
    const id = typeof row?.id === "string" ? row.id : "";
    if (!KEY.test(id)) throw new Error(`a challenge deal has an id that is not key-shaped: '${id}'`);
    if (RETIRED_TIERS.includes(id)) throw new Error(`challenge deal '${id}' is a retired id and may never be re-minted (5f)`);
    if (tiers.some((t) => t.id === id)) throw new Error(`challenge deal '${id}' is listed twice`);
    const gems = whole(row.gems, `deal '${id}' gems`, MAX_TIER_GEMS);
    const plays = whole(row.plays, `deal '${id}' plays`, MAX_TIER_PLAYS);
    const days = whole(row.days, `deal '${id}' days`, MAX_TIER_DAYS);
    if (gems <= 0 || plays <= 0 || days <= 0) throw new Error(`challenge deal '${id}' needs gems, plays and days above nought`);
    if (plays <= lastPlays) throw new Error(`challenge deal '${id}' gives ${plays} plays, which does not beat the ${lastPlays} before it`);
    if (gems <= lastGems) throw new Error(`challenge deal '${id}' costs ${gems} gems, which does not exceed the ${lastGems} before it`);
    lastPlays = plays;
    lastGems = gems;
    tiers.push({ id, gems, plays, days });
  }

  // The economy gate (56k): the largest deal's daily maximum across every genre is held under
  // a ceiling, so a genre or a rate added to the file cannot quietly out-earn the rest of the game.
  const topPlays = tiers.length ? Math.max(...tiers.map((t) => t.plays)) : freePlays;
  const topDay = topPlays * genres.length * coins;
  if (topDay > MAX_DAILY_COINS) {
    throw new Error(`the largest challenge deal could pay ${topDay} credits a day across ${genres.length} genre(s), ` +
                    `above the ${MAX_DAILY_COINS} ceiling (ChallengeLimits.MaxDailyCoins)`);
  }

  console.log(`  challenges: ${genres.length} genre(s), ${freePlays} free play(s) a day, ${tiers.length} deal(s), ` +
              `${coins} credits and ${xp} XP a clear up to ${maxClears} clears; the largest deal pays at most ${topDay} a day`);

  return { genres, freePlays, tiers, coins, xp, maxClears };
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
  const known = ["heart_refill", "coin_bonus", "win_bonus", "hint_refill", "xp_boost"];
  const kinds = ["credits", "gems", "hearts", "heart_boost", "run_time", "hints", "xp_boost"];

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
/**
 * The rank ladder, published so the server can derive a keeper's badge rather than believe one.
 *
 * **Why the server needs this at all.** A rank was a private badge derived on the device and
 * stored nowhere (invariant 52), and nothing here had ever heard of it. It is drawn on a public
 * board now, and invariant 19a is unambiguous about what that changes: a number that goes public
 * stops being derived-and-trusted and becomes adjudicated. `rungOf` in `functions/src/ranks.ts`
 * is the server's own reading, and this is what it reads the ladder from.
 *
 * **Refused rather than degraded, which is the opposite of what the client does with the same
 * block.** `RankLadder.Resolve` drops a rung naming a measure it cannot read, because that is a
 * newer content pack reaching an older client and taking the whole ladder off their screen would
 * be worse. Nothing reaches this script that it did not read off the working tree a moment ago,
 * so an unreadable rung here is an authoring mistake and the seeder's job is to stop it leaving
 * the building — the same stance `readStreak` and `readEvents` take about a chest tier.
 *
 * **What it proves that no other gate can.** A scope names a chapter, and whether that chapter
 * ships is decided by the manifest — which this run has already walked into `levelChapters`. A
 * rung scoped to a withdrawn chapter is a line with no levels behind it: permanently unmeetable,
 * so the rung above it is permanently unreachable, so the top of the ladder quietly stops
 * existing. `content.py`'s `check_ranks` walks the same ladder against the same manifest, and
 * the two are deliberately both there — that one gates the build, this one gates the deploy, and
 * a re-seed from a shadow tree is exactly the path that skips the first.
 */
function readRanks(progression, levelChapters, manifest) {
  const ID = /^[a-z0-9_]{1,32}$/;

  // Mirrors `RankMeasures`' five derived ids. Everything else a rung may name is a counted
  // verb read out of the lifetime tally by its own id, which is what makes a future mode's
  // verb a rank requirement with no code anywhere — so an unknown measure cannot be refused
  // here without refusing that, and is deliberately allowed through.
  const SCOPED_TO_CHAPTER = new Set(["levels_cleared", "stars", "three_stars"]);
  const SCOPED_TO_LEVEL = new Set(["best_wave"]);
  const UNSCOPED = new Set(["keeper_level"]);

  // Mirrors `LifetimeTally.Ceiling`. A target above it could never be met by anybody.
  const CEILING = 999999999;
  const MAX_RUNGS = 24;
  const MAX_REQUIREMENTS = 8;

  const ranks = progression.ranks;
  if (!ranks || !Array.isArray(ranks.rungs) || ranks.rungs.length === 0) {
    // Absent is legal and publishes nothing: no badge on any card, no badge on any row, and
    // every client reads that exactly as it reads a card written before this deployment.
    console.log("  ranks: no ladder in progression.json; no badge will be published");
    return [];
  }

  if (ranks.rungs.length > MAX_RUNGS) {
    throw new Error(`ranks lists ${ranks.rungs.length} rung(s), more than the supported ${MAX_RUNGS}`);
  }

  const chapters = new Set(Object.values(levelChapters));
  const levels = new Set(Object.keys(levelChapters));
  const seen = new Set();
  const out = [];

  for (const [index, rung] of ranks.rungs.entries()) {
    if (!rung || !ID.test(rung.id ?? "")) {
      throw new Error(`ranks rung ${index} has a bad id '${rung?.id}'`);
    }
    if (seen.has(rung.id)) throw new Error(`ranks rung '${rung.id}' is listed twice`);
    seen.add(rung.id);

    if (!Array.isArray(rung.requires) || rung.requires.length === 0) {
      throw new Error(`ranks rung '${rung.id}' asks for nothing; every account would hold it`);
    }
    if (rung.requires.length > MAX_REQUIREMENTS) {
      throw new Error(`ranks rung '${rung.id}' has ${rung.requires.length} requirement(s), ` +
                      `more than the supported ${MAX_REQUIREMENTS}`);
    }

    const requires = rung.requires.map((line, at) => {
      const measure = line?.measure ?? "";
      if (!ID.test(measure)) {
        throw new Error(`ranks rung '${rung.id}' requirement ${at} names no measure`);
      }

      const target = Math.floor(Number(line?.target ?? 0));
      if (!Number.isFinite(target) || target < 1) {
        throw new Error(`ranks rung '${rung.id}' asks for ${line?.target} of '${measure}'; ` +
                        "a target below one is met by every account");
      }
      if (target > CEILING) {
        throw new Error(`ranks rung '${rung.id}' asks for ${target} of '${measure}', above the ` +
                        `${CEILING} a counter may ever reach; it could never be met`);
      }

      const scope = line?.scope ?? "";
      if (scope.length > 0) {
        if (UNSCOPED.has(measure)) {
          throw new Error(`ranks rung '${rung.id}' scopes '${measure}' to '${scope}', and that ` +
                          "measure is about the whole account; a scope it cannot honour would " +
                          "be a sentence the ladder does not mean");
        }
        if (SCOPED_TO_CHAPTER.has(measure) && !chapters.has(scope)) {
          throw new Error(`ranks rung '${rung.id}' scopes '${measure}' to chapter '${scope}', ` +
                          "which this catalog does not ship; the line could never be met and " +
                          "every rung above it would be unreachable");
        }
        if (SCOPED_TO_LEVEL.has(measure) && !levels.has(scope)) {
          throw new Error(`ranks rung '${rung.id}' scopes '${measure}' to level '${scope}', ` +
                          "which this catalog does not ship");
        }
      }

      return scope.length > 0 ? { measure, scope, target } : { measure, target };
    });

    out.push({ id: rung.id, requires });
  }

  // ------------------------------------------------------------- the ladder's own gate
  // **The ladder may not open before the lane it ranks does** (invariant 52i). A rank is drawn
  // on a board row and on a stranger's public profile (52h), and the mode those two are about
  // is the Infinite lane, which stands behind a keeper wall. A badge worn by somebody who
  // cannot yet open that lane is the feature contradicting itself, and it shipped: Cinderling
  // was reachable at keeper level 7 against a lane that opens at 10.
  //
  // The gate itself is one ordinary `keeper_level` line on the first rung, and that is the whole
  // mechanism - `rungOf` and `RankLadder.Held` both walk up from the bottom and stop at the first
  // rung they cannot meet, so one line closes every rung on both sides of the wire with no code
  // that could disagree.
  //
  // **This is the deploy's half of holding the two numbers together.** The wall is authored in
  // `manifest.json` and the gate in `progression.json`, and nothing lets them share one figure,
  // because this server never reads a manifest - it reads what this script publishes. So the pair
  // is checked in all three places that can see both files: here, `check_ranks` in `content.py`,
  // and `ContentValidation.ValidateRanks`. **This one is the gate that matters**, because a
  // re-seed from a shadow tree is exactly the path that skips the build.
  const walls = (manifest?.chapters ?? [])
    .filter((c) => !c?.disabled && c?.track === "infinite")
    .map((c) => Math.floor(Number(c?.minKeeperLevel ?? 0)) || 0)
    .filter((wall) => wall > 0);

  const wall = walls.length > 0 ? Math.min(...walls) : 0;

  if (wall > 0) {
    const first = out[0];
    const opens = first.requires.reduce(
      (high, line) => (line.measure === "keeper_level" && !line.scope && line.target > high
        ? line.target
        : high),
      0);

    if (opens < wall) {
      throw new Error(
        `the Infinite lane opens at keeper level ${wall} and the rank ladder's first rung ` +
        `('${first.id}') opens at ${opens || "nothing"}; a rank is what a board row and a ` +
        "stranger's profile draw, so it may not be worn by somebody who cannot yet open the " +
        `lane it ranks - give that rung a 'keeper_level' line of at least ${wall}, and move it ` +
        "with the wall");
    }

    if (opens > wall) {
      // The other direction. Not refused - a ladder opening *after* its lane hands nobody a
      // badge they should not have - but it has stopped opening *with* the thing it ranks, and
      // a seeder that said nothing would be the last chance anybody had to notice.
      console.log(`  ranks: NOTE the ladder opens at keeper level ${opens} and the Infinite ` +
                  `lane opens at ${wall}, so it no longer opens with the lane it ranks`);
    } else {
      console.log(`  ranks: the ladder opens at keeper level ${opens}, against the ${wall} ` +
                  "the Infinite lane opens at");
    }
  }

  console.log(`  ranks: ${out.length} rung(s), every scope proved against the shipped catalog`);
  return out;
}

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
        `lifetime wave(s) (${(lane.xpPerWave * lane.maxWaves).toLocaleString("en-GB")} xp)` +
        (lane.creditsPerWave > 0 && lane.dailyCreditCap > 0
          ? `\n  endless credits: ${lane.creditsPerWave} a wave, capped at ` +
            `${lane.dailyCreditCap.toLocaleString("en-GB")} a day ` +
            `(${Math.ceil(lane.dailyCreditCap / lane.creditsPerWave)} wave(s) to the cap)`
          : "\n  endless credits: withdrawn")
      : "  endless: withdrawn - the Infinite lane pays no XP"
  );
  console.log(
    config.xpBoost.maxPercent > 0
      ? `  xp boost: a stored bonus is clamped to +${config.xpBoost.maxPercent}% of provable XP`
      : "  xp boost: withdrawn - a stored bonus is worth nothing"
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
  `xp boost capped at +${config.xpBoost.maxPercent}%, ` +
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

// The turret roster the boards publish a line from. Written as a full replacement rather
// than a merge, for `config/products`' reason: a turret removed from the content file must
// stop being vouched for, and a merge would leave the server publishing seats nobody ships.
// Since 2026-09-21 the grove half of this document is written empty — see
// `buildGroveConfig`.
const grove = buildGroveConfig();
await writeDoc(token, "config/grove", grove, { replace: true });

console.log(
  `config/grove: v${grove.version} — ${Object.keys(grove.wards).length} turret(s); ` +
  "the grove tables are empty (the Grovement was removed on 2026-09-21), so every " +
  "published card now scores nought"
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

// Sanity: a chapter file on disk that nobody lists is usually a mistake worth naming.
const onDisk = readdirSync(join(CONTENT, "chapters")).filter((f) => f.endsWith(".json")).length;
const listed = (readJson(join(CONTENT, "manifest.json")).chapters ?? []).length;
if (onDisk !== listed) {
  console.log(`note: ${onDisk} chapter file(s) on disk, ${listed} listed in the manifest`);
}
