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

  const pieces = {};
  const dwellings = {};

  for (const piece of homestead.pieces ?? []) {
    if (!piece?.id) continue;

    const cost = Math.floor(piece.cost ?? 0);
    if (cost > 0) pieces[piece.id] = cost;

    // Every rung, priced or not: the first is free and still has to be findable, because
    // the hall draws the best rung *held* and a free one is held by everybody.
    if (piece.kind === "dwelling") dwellings[piece.id] = Math.floor(piece.tier ?? 0);
  }

  const regions = {};
  for (const region of homestead.floor?.regions ?? []) {
    if (!region?.id) continue;
    const cost = Math.floor(region.cost ?? 0);
    if (cost > 0) regions[region.id] = cost;
  }

  const companions = {};
  for (const companion of manifest.companions ?? []) {
    if (!companion?.id || companion.disabled) continue;

    const cost = Math.floor(companion.unlockCost ?? 0);
    if (cost <= 0) continue;                      // the starter, and anything else given away

    companions[companion.id] = { cost, level: Math.floor(companion.unlockLevel ?? 0) };
  }

  const stars = (homestead.score?.stars ?? [])
    .map(Math.floor)
    .filter((at) => at > 0)
    .sort((a, b) => a - b);

  if (stars.length === 0) {
    throw new Error("homestead.json has no score ladder; every grove would rank in the bottom league");
  }

  if (Object.keys(dwellings).length === 0) {
    throw new Error("homestead.json has no dwelling; a published card could name no home");
  }

  return {
    version: Math.floor(manifest.groveVersion ?? 1),
    pieces,
    regions,
    companions,
    dwellings,
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
      streak: readStreak(progression),
      golden: readGolden(progression),
      events: readEvents(manifest, levelChapters),
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
  const SHELVES = new Set(["gems", "coins", "bundles"]);
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

    if (!Number.isFinite(credits) || !Number.isFinite(gems) || credits < 0 || gems < 0) {
      throw new Error(`store product '${id}' grants ${entry.credits} credits and ${entry.gems} gems`);
    }

    if (credits === 0 && gems === 0) throw new Error(`store product '${id}' grants nothing`);

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
    if (entry.kind !== "nonconsumable") {
      if (!shelves.has(entry.shelf)) shelves.set(entry.shelf, []);
      shelves.get(entry.shelf).push({ id, credits, gems, cents });
    }

    // Only what the server needs in order to honour a receipt. The shelf, the badge and
    // the reference price are display and validation; publishing them would invite
    // somebody to think the server had an opinion about them.
    products[id] = { credits, gems, kind: entry.kind };
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
 * The event calendar, published so the server can re-derive what a track has paid.
 *
 * Past events are published too, and that is not an oversight: a closed event still pays
 * what it paid, so dropping one from the config would make the server derive less than the
 * game shows for every player who finished it. Nothing here expires.
 *
 * Every rule the client's reader enforces is enforced again here, because the two derive
 * the same number and a config the client would have refused is a config the server would
 * quietly disagree with. A refusal at seed time is a message on a terminal; the same
 * refusal in production is a balance nobody can explain.
 */
function readEvents(manifest, levelChapters) {
  const events = manifest.events;
  if (!Array.isArray(events) || events.length === 0) return null;

  const seen = new Set();
  const published = [];

  for (const entry of events) {
    if (!entry || entry.disabled) continue;

    const id = String(entry.id ?? "");
    if (!/^[a-z0-9_]+$/.test(id)) {
      throw new Error(`event id '${entry.id}' is unusable; ids are lower case letters, ` +
                      "digits and underscores, because earned credits depend on them");
    }
    if (seen.has(id)) throw new Error(`manifest lists event '${id}' twice`);
    seen.add(id);

    const startUnix = Math.floor(Number(entry.startUnix));
    const endUnix = Math.floor(Number(entry.endUnix));
    if (!Number.isFinite(startUnix) || !Number.isFinite(endUnix) || endUnix <= startUnix) {
      throw new Error(`event '${id}' ends at or before it starts`);
    }

    const levels = [...new Set(entry.levels ?? [])];
    if (levels.length === 0) throw new Error(`event '${id}' names no glades`);

    for (const levelId of levels) {
      if (!levelChapters[levelId]) {
        throw new Error(
          `event '${id}' names glade '${levelId}', which no shipped chapter holds. The ` +
          "server counts only glades the catalog vouches for, so it would derive a " +
          "shorter track than the game shows"
        );
      }
    }

    const milestones = [];
    let previousGoal = 0;

    for (const rung of entry.milestones ?? []) {
      const goal = Math.floor(Number(rung?.goal));
      const credits = Math.floor(Number(rung?.credits));

      if (!Number.isFinite(goal) || goal <= previousGoal) {
        throw new Error(`event '${id}' milestone goals must rise: ${rung?.goal} follows ${previousGoal}`);
      }
      if (goal > levels.length) {
        throw new Error(`event '${id}' has a milestone at ${goal} glades but names only ${levels.length}`);
      }
      if (!Number.isFinite(credits) || credits < 0) {
        throw new Error(`event '${id}' milestone at ${goal} pays ${rung?.credits}`);
      }

      milestones.push({ goal, credits });
      previousGoal = goal;
    }

    if (milestones.length === 0) throw new Error(`event '${id}' has no milestones, so it pays nothing`);

    published.push({ id, startUnix, endUnix, levels, milestones });
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

  const known = ["heart_refill", "coin_bonus", "run_continue", "win_bonus", "hint_refill"];
  const kinds = ["credits", "gems", "hearts", "heart_boost", "run_time", "hints"];

  // Mirrors the same rule on the client (`AdRewardTable.TryReadOffer`). A kind spent inside
  // a run only makes sense on the one placement offered from inside one, and the failure it
  // prevents is silent on both sides: an offer drawn where no run exists, a video watched,
  // and a reward applied to nothing.
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

    if (transient.includes(placement.kind) && id !== "run_continue") {
      throw new Error(
        `ads placement '${id}' pays '${placement.kind}', which is spent inside a run; ` +
        `only 'run_continue' is offered from inside one`
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

  return { placements };
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

const DROP_KINDS = new Set(["credits", "gems", "hearts", "heart_boost"]);

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
function readStreak(progression) {
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
    if (!rung || !rung.kind) return { kind: "", amount: 0 };

    if (!DROP_KINDS.has(rung.kind)) {
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

  if (!rungs.some((rung) => rung.kind)) {
    throw new Error("the streak ladder pays nothing on any night; refusing to seed it");
  }

  return { rungs };
}

/** Mirrors `StreakRules`. See `readStreak`. */
const MAX_STREAK_RUNGS = 30;

function maxStreakAmount(kind) {
  if (kind === "credits") return 2000;
  if (kind === "gems") return 100;
  return 72;
}

function band8(band, chestIndex, role) {
  if (!band || !DROP_KINDS.has(band.kind)) {
    throw new Error(`daily chest ${chestIndex} ${role} names unknown reward kind '${band?.kind}'`);
  }

  const min = Math.floor(band.min ?? 0);
  const max = Math.floor(band.max ?? 0);

  if (min < 1 || max < min) {
    throw new Error(`daily chest ${chestIndex} ${role} '${band.kind}' has band ${min}..${max}`);
  }

  return { kind: band.kind, min, max };
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
const token = accessToken();

await writeDoc(token, "config/progression", config);
console.log(
  `config/progression: ${levelCount} level(s), ` +
  `${Object.keys(config.chapterRewards).length} chapter override(s), ` +
  `${config.daily.chests.length} daily chest(s) every ${config.daily.runsPerChest} run(s), ` +
  `${config.ads ? Object.keys(config.ads.placements).length : 0} ad placement(s), ` +
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

  console.log(
    `config/products: ${ids.length} product(s) — ${gemPacks} gem, ${coinPacks} coin, ` +
    `${ids.length - gemPacks - coinPacks} bundle`
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
  `${Object.keys(grove.dwellings).length} home rung(s), ` +
  `${grove.stars.length} star(s) up to ${grove.stars[grove.stars.length - 1].toLocaleString()}, ` +
  `a complete grove worth ${groveTotal.toLocaleString()}`
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
