# Glimmer Grove — server

The server half of the economy. Firestore holds the saves, Cloud Functions adjudicate
anything involving money, and security rules are the boundary between them.

Project: **`glimmer-groove-1cd60`** · Firestore: **`eur3`** · Functions: **`europe-west1`**

## Why it is shaped like this

The client cannot be trusted with currency, and no amount of C# can fix that — the
binary is on the player's device. So the design puts each piece of state where it can
actually be defended:

| State | Lives in | Client can |
|---|---|---|
| Level records (stars, moves) | `players/{uid}` | read + write |
| Earned currency | nowhere — derived from the above | — |
| Granted currency (seed, purchases) | `players/{uid}/private/wallet` | read only |
| Spent currency | `players/{uid}/private/wallet` | read only |
| Debit idempotency records | `players/{uid}/spendLog/{id}` | read only |
| Award idempotency records | `players/{uid}/grantLog/{id}` | read only |
| Receipt claims | `receipts/{store}__{txn}` | neither |
| Reward table, product catalog | `config/*` | read only |

The interesting consequence: a player who forges their save can change what their own
progress screen says and **cannot mint a single credit**. Earned currency is re-derived
server-side from the level records, and `functions/src/progression.ts` ignores any
record naming a glade the catalog has never heard of.

That only works because XP and credits are derived rather than accumulated. An
accumulated balance could only be believed or disbelieved; a derived one can be
recomputed and checked.

## The rule that exists twice

Deriving credits in both C# and TypeScript is the price of the client working offline
while the server stays authoritative. Two implementations of one rule drift, so they are
pinned by **`shared/reward-vectors.json`**, which both sides run as a test:

```bash
npm --prefix firebase/functions run test    # the server half
# the client half is Assets/Game/Tests/RewardVectorTests.cs
```

Change the arithmetic on either side without the other and one goes red. Three rules keep
them identical, each for a reason: a glade the catalog cannot vouch for earns nothing (or
an invented level id mints currency), stars clamp to three (or a forged record buys a
fourth), and a level id counts once.

The same file now pins a second rule: the **daily chest generator**, in `daily.ts` here
and `DailyChestTable.cs` there. Those vectors use a synthetic drop table rather than the
shipped one, so retuning real rates does not turn them red — what is under contract is the
arithmetic, and every constant in it is load-bearing. See *Daily chests*.

`config/progression` is what the server treats as the catalog, so **re-run the seed script
after every content drop** or new glades earn nothing. Nobody loses a balance if you
forget — `earnedFloor` on the wallet only ever ratchets up — but the new chapter does not
pay until the server knows it exists.

## The ledger is a map, not an array

`players/{uid}.levels` is keyed by level id. Two things follow, and both matter at scale:
a duplicated record is unrepresentable rather than something the server has to filter,
and a sync can write `levels.c01_first_light` on its own. `SaveDelta.Between` diffs the
merged save against what the server holds, and an unchanged save sends nothing at all —
which is most syncs, since the common trigger is the app being backgrounded.

## Layout

```
firestore.rules          the security boundary. Read this first.
firebase.json            deploy config
functions/src/
  index.ts               getWallet, submitSpends, claimAwards, redeemPurchase,
                         adReward, appleNotification, sweepVoidedPurchases, publishGroveStats
  progression.ts         server-side derivation — mirrors ProgressionLedger.cs
  daily.ts               daily chest generator — mirrors DailyChestTable.cs
  streak.ts              streak ladder and the rule that bounds a night — mirrors StreakTable.cs
  receipts.ts            Apple and Google validation, fails closed
  products.ts            what a product grants — read from config/products, never from the client
  refunds.ts             reversing a purchase a store took back
  wallet.ts              balance arithmetic over the private wallet document
seed/seed-config.mjs     publishes the reward table AND the product catalog, both from
                         the shipped content
```

## Status

Deployed and verified live on 2026-08-15:

- Firestore database in `eur3`, security rules released — including the `streak` key on
  the save document, which had to go out **before** any client that writes it, or every
  push fails `hasOnly` with permission-denied
- Six functions on Node 22 in `europe-west1` as of that date: `getWallet`, `submitSpends`,
  `claimAwards`, `redeemPurchase`, `adReward`, and the `publishGroveStats` schedule.
  **Two more are written and not yet deployed** — `appleNotification` and the hourly
  `sweepVoidedPurchases` — along with a `redeemPurchase` that grants more than one
  currency and reports what it granted. `firebase deploy --only functions`
- `claimAwards` grants daily chests *and* streak nights
- Anonymous authentication enabled
- Android and iOS apps registered for `com.digikeygames.glimmergrove`
- `config/progression` seeded from the shipped content, streak ladder included
- `shared/reward-vectors.json` passes on both sides, so client and server arithmetic agree
- `firebase/e2e/smoke-test.mjs` passes **28/28** against the live project

The smoke test signs in as a fresh anonymous account each run, so anything keyed on the
account id — a glade's golden multiplier, a chest's contents — differs run to run. Assert
against the set the published config permits, never against one number. The earned-credits
case learned this by failing on about a third of runs while looking like a real defect.

Not configured, and deliberately so: **store credentials**. The four secrets hold the
placeholder `UNSET`, so `redeemPurchase` refuses every receipt. That is the correct
state before any real in-app product exists — see *Secrets* below.

## Deploying

```bash
cd firebase
npm --prefix functions install
npm --prefix functions run test     # reward vectors: the server must match the client
firebase deploy --only firestore:rules
node seed/seed-config.mjs           # must run before functions serve traffic
firebase deploy --only functions
node e2e/smoke-test.mjs             # proves the rules still hold
```

**Run the smoke test after every deploy.** The security rules are the only part of this
system nothing else can check: they are evaluated by Firestore, so a mistake in them
cannot fail a compile, cannot fail the Unity tests, and behaves perfectly in the Editor.
It shows up in production as either "cloud save quietly stopped working" or "currency is
free".

`seed-config.mjs` reads `Assets/StreamingAssets/Content/` and writes `config/progression`.
**Re-run it after any content change** — a new chapter's glades earn nothing until the
server knows they exist. It also reads the starting balances straight out of
`CurrencyLedger.cs` and fails loudly if it cannot find them, so the client and server
can never seed different amounts.

## Secrets

In Secret Manager, never in source or in `.env`. All four currently hold the literal
`UNSET`, which `index.ts` reads as "not configured" and turns into a refusal. They exist
only because a declared secret must exist for a deployment to go through — without the
placeholder, cloud save itself would have been blocked on App Store Connect paperwork.

Replacing them is the whole of enabling purchases; no code changes:

```bash
firebase functions:secrets:set APPLE_KEY_ID                # from App Store Connect
firebase functions:secrets:set APPLE_ISSUER_ID
firebase functions:secrets:set APPLE_PRIVATE_KEY           # contents of the .p8
firebase functions:secrets:set GOOGLE_PLAY_SERVICE_ACCOUNT # the whole service-account JSON
```

The Google service account needs the **Android Publisher** role, granted in the Play
Console under *Users and permissions*, not only in Google Cloud IAM. That step is easy
to miss and produces a 401 that looks like a bad key.

## Daily chests

Three chests a day, earned by playing and opened by hand on the home screen. They are the
first thing in the game that gives a player currency they did not *earn* by clearing a
glade, so they are the first thing to go through `claimAwards`.

The shape is the mirror image of `submitSpends`, and for the same reason:

1. The client rolls the chest, shows the reward and counts it locally at once — a chest
   opened on a plane has to be spendable on that plane. It lands in a queue of identified
   entries, never in `grantedBaseline`, which the client may not touch.
2. On the next sync it submits those entries. **The amounts carry no authority.**
   `claimAwards` re-rolls the same chest itself, from the account id, the day and the chest
   index, using the drop table in `config/progression`, and grants its own figure. A client
   that inflates its claim gains nothing.
3. The grant is keyed on `players/{uid}/grantLog/daily:{day}:{chest}:{currency}` — an id
   **derived** from what earned it rather than generated. So the same chest submitted from
   two devices, resubmitted after a lost response, or replayed by hand, collides with a
   document that already exists and confirms instead of granting.

Two things to know before touching it:

- **A claim whose config block is missing is left unconfirmed, not refused.** Granting a
  guess would be inventing money, and rejecting would throw away a reward the player earned
  — the client only logs rejections and resubmits, so a permanent refusal is a loop for the
  life of the account. So a drop-table change that has not been seeded stops chests paying
  until it is, and `seed-config.mjs` throws rather than publishing a config without one.
- **The run counter is forgeable and that is accounted for.** It lives in the player's own
  save document. The real bound is one day's chests per day per account, enforced by the
  derived ids plus a check that the claimed day is not in the future. That is what an
  honest player gets anyway; proving somebody played three glades would mean trusting a
  different number the same player writes.

## Streak nights

The streak ladder pays credits and gems as well as hearts, and it laps: night eight pays
night one's rung, for ever. A night is collected by hand on the streak page and reaches the
server the same way a chest does — a claim on `grantLog/streak:{day}:{night}:{currency}`,
with the amount read from `config/progression.streak` and the client's figure ignored.

What is different, and worth understanding before changing anything here, is that **the
server cannot recompute a streak.** A chest is a function of (account, day, index); nothing
about "seven days running" is derivable from anything this server observes. So the claim is
*bounded* instead of recomputed, by two facts no client can write:

1. The id carries the calendar day, and a streak has exactly one night per day. So
   `grantLog` allows at most one streak payout per day per account, for ever.
2. `players/{uid}/private/wallet` records the day and night last paid. `advances` in
   `streak.ts` requires a claim to either continue — night up by exactly the days elapsed —
   or restart, claiming no more nights than those days allow. A save edited to say "night
   seven" every morning satisfies neither.

Between them, a forged streak earns exactly what an honest one does. Three details:

- **A brand-new wallet is seeded with the floor at yesterday**, so a fresh account's first
  claim must be night one, today. An *existing* wallet with no floor is deliberately allowed
  one unbounded claim — that is the migration for players holding a streak this server never
  recorded, and refusing it would take nights the game has already shown them.
- **The player's save is read but never believed.** `saveSupports` compares the claim against
  `streak.startDay` / `lastPlayedDay` and logs a disagreement. It is not a gate: a night
  collected offline before a streak lapsed has a `startDay` that has since moved past it, and
  gating on that would reject a reward genuinely earned — permanently, and therefore for ever.
- **Re-seed after retuning the ladder.** The board draws from the shipped file, the wallet is
  credited from `config/progression`. Skip the seed and those are two different numbers in
  front of the same player. `Validate Content` prints what one lap pays and says so.

## Two invariants, and how they are held

**A debit is charged once, however many times it is submitted.** Each debit carries a
client-generated id. `submitSpends` writes `spendLog/{id}` inside the same transaction
that increments `spent`, so the second attempt sees the document and changes nothing.
This is why the client keeps a list of identified debits rather than a counter: merging
two devices by taking the larger counter forgives a spend, and by summing them charges
twice.

**A store transaction is granted once, to one account, ever.** `redeemPurchase` keys the
grant on `receipts/{store}__{transactionId}` — globally, not per player. Receipt replay
across accounts is an industrialised attack; a per-player key would validate every one
of them. If the same player retries, it reports success and grants nothing.

**A refunded purchase is taken back.** Apple and Google both let a purchase be undone
weeks later, and without something watching for it the loop is buy → spend → refund →
repeat. It needs no exploit and no tooling, which is why it is the most common way a
mobile economy leaks money. `refunds.ts` reverses the grant on both sides; balances
clamp at zero rather than going negative, because a player whose credits silently stop
rising for a month is a player who uninstalls, and repeat abuse is a job for the stores'
own account bans.

## Adding a product

The catalog lives in **one place**: the `store` block of
`Assets/StreamingAssets/Content/progression.json`. The game draws its shop from it and
the seeder derives `config/products` from it, so the amount on a card and the amount a
receipt is honoured for cannot disagree.

1. Add the product to the `store.products` array. `id`, `kind`
   (`consumable`/`nonconsumable`), `shelf`, what it grants, and `referenceUsdCents`.
2. Add a name string: `store.product.<id>` in `Content/loc/en.json`.
3. Create the product in **App Store Connect** and the **Play Console**, using exactly
   that id and a price tier near the reference cents. The kind must match: a
   `nonconsumable` here has to be a non-consumable there, because the store is what
   makes a one-time offer one-time.
4. `npm --prefix firebase/functions run build && node firebase/seed/seed-config.mjs`
5. `Glimmer Grove ▸ Validate Content` — it fails the build on a ladder that gets worse
   as it gets bigger, and on a product with no name string.

The amount lives in content and not in the app on purpose: a client that names its own
reward names any number it likes. **A product id is permanent.** Neither store lets one
be reused after deletion, and a receipt redeemed a year from now is looked up against
whatever the table says then — so retune by *adding* a product, never by repointing one.

### What a product may grant

Currency, and nothing else. Hearts and boosts live in the player's save file and are
applied by the phone, so a product that promised them would need the client to apply
half a purchase after the server applied the other half — which means a record in the
save of what has already been applied, merged across devices, whose failure mode is
somebody paying and receiving nothing. Hearts are bought with **gems** instead, through
the ordinary spend path. See `StoreProduct` on the client for the argument in full.

## Store credentials

Four secrets, all fail-closed — an absent one refuses every receipt rather than granting
against a key that cannot validate anything.

```
firebase functions:secrets:set APPLE_KEY_ID              # App Store Connect ▸ Integrations ▸ Keys
firebase functions:secrets:set APPLE_ISSUER_ID           # the issuer id on that same page
firebase functions:secrets:set APPLE_PRIVATE_KEY         # the .p8 contents
firebase functions:secrets:set GOOGLE_PLAY_SERVICE_ACCOUNT   # the service-account JSON
```

The Play service account needs **View financial data** as well as the usual publishing
permission — the voided-purchases sweep is a financial API, and a sweep that returns
nothing for ever is exactly what a missing permission looks like. That is why its count
is logged on every run, including zero.

## Refunds

Two mechanisms, because the stores are genuinely different.

**Apple pushes.** Set the `appleNotification` URL in App Store Connect ▸ App Information
▸ App Store Server Notifications, for **both** the production and the sandbox
environment. The handler deliberately does not verify the notification's JWS chain: it
scrapes transaction ids out of the body, keeps only ones this server has actually
granted, and then asks the App Store Server API about each of them over the same
authenticated channel receipt validation uses. Apple's own answer is what moves money,
so a forged POST can at most make us look something up and be told it is fine.

**Google is polled.** `sweepVoidedPurchases` runs hourly and reads the Voided Purchases
API. A real-time channel exists, but it needs a Pub/Sub topic and a subscription to keep
alive, and a subscription that silently stops delivering would cost a month of refunds
before anyone noticed. The sweep keeps a cursor in `ops/refundSweep` and rewinds it an
hour on every read, so a boundary record cannot be lost to clock skew — `revokeReceipt`
is idempotent, so re-reading costs nothing.

## Worth doing before real money moves

- **App Check.** Currently off (`enforceAppCheck: false` in `index.ts`). It stops the
  callable endpoints being hit by anything that is not a genuine build of the app.
  Enable it in monitor-only mode first — turning it straight on can lock out live
  clients that have not shipped the attestation yet.
- **Budget alert** on the billing account. Functions scale, and so does the bill.
- **A Firestore backup schedule.** `gcloud firestore backups schedules create`.
- **Watch the sandbox flag.** Every receipt document records whether the store called it
  a sandbox or a test purchase. It is deliberately still granted — app review has to be
  able to buy things — but a live economy should know the difference, and nothing
  currently reports on it.
