/** Deployment-wide constants. One place to change, so nothing drifts apart. */

/**
 * Where the functions run.
 *
 * Keep this in the same region as Firestore. A function in one continent reading a
 * database in another pays that round trip on every single call, and the economy
 * endpoints are the ones a player waits on.
 */
export const REGION = "europe-west1";

/**
 * Must match `applicationIdentifier` in Unity's Player Settings, for both stores.
 *
 * Receipt validation rejects any transaction belonging to a different bundle, so a
 * mismatch here refuses every genuine purchase. `CloudWireTests` pins this against
 * `Application.identifier` so the two cannot drift apart unnoticed.
 */
export const BUNDLE_ID = "com.arcade.glimmergrove";

/** Firestore paths, named once so a typo cannot become two collections. */
export const PATHS = {
  player: (uid: string) => `players/${uid}`,
  wallet: (uid: string) => `players/${uid}/private/wallet`,
  spend: (uid: string, spendId: string) => `players/${uid}/spendLog/${spendId}`,

  // Awards, keyed by an id derived from what earned them rather than generated. A daily
  // chest uses `daily:{day}:{chest}:{ccy}`; a streak night uses `streak:{day}:{night}:{ccy}`;
  // a rewarded ad uses `ad:{eventId}`, where the event id comes from the network's own
  // verification callback. All three are idempotent for the same reason: the second attempt
  // collides with a document that already exists, on any device, after any reinstall.
  grant: (uid: string, grantId: string) => `players/${uid}/grantLog/${grantId}`,

  receipt: (store: string, transactionId: string) => `receipts/${store}__${transactionId}`,
  progressionConfig: "config/progression",
  productsConfig: "config/products",
};

/** Bounds on a single call, so one request cannot become an unbounded transaction. */
export const MAX_SPENDS_PER_CALL = 50;

/**
 * Awards per call. Generous, because a device that has been offline for a fortnight
 * arrives with every day's chests at once and losing them to a batch limit would be a
 * support case for something the player genuinely earned.
 */
export const MAX_AWARDS_PER_CALL = 100;

/** Currency ids. These are permanent — they key save files and this database. */
export const CURRENCIES = ["credits", "gems"] as const;
export type CurrencyId = (typeof CURRENCIES)[number];
