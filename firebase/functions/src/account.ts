/**
 * Account deletion — the one operation in this deployment that only ever removes.
 *
 * ## Why it exists at all
 *
 * App Store Review 5.1.1(v) and Google Play both require an app that supports account
 * creation to offer deletion *from inside the app*. The website's `/delete-account` page
 * satisfies Play and does not satisfy Apple, which wants the deletion initiated by the
 * person holding the phone. It is also simply correct: this deployment holds a save ledger,
 * a wallet, a published card and a reserved name for every player, and there was no way to
 * ask for any of it back.
 *
 * ## The ordering is the whole safety property
 *
 * **Data first, the auth user last.** Every step before {@link deleteAuthUser} runs under
 * the caller's own credentials, so if any of them throws — a cold start, a contended
 * transaction, a dropped connection — the client still holds a valid token and simply calls
 * again. Deleting the auth user first would invert that: a crash halfway would leave a pile
 * of documents belonging to a uid nobody can ever authenticate as again, unreachable by the
 * player, by this function and by support. That is the one failure here with no repair, and
 * the ordering is what makes it unreachable.
 *
 * **Every step is idempotent**, so calling again after a partial run is not a special case —
 * it is the ordinary path. Deletes are delete-if-exists; the two conditional steps (the name
 * release and the board scrub) re-read their own precondition inside a transaction and do
 * nothing when it no longer holds.
 *
 * **Visibility goes first.** The published card and the board rows are the only things here
 * a stranger can see, so they are removed before anything else. A run that dies in the middle
 * then leaves a player's data present and *invisible*, which is the safe half to be caught in;
 * the reverse would leave a deleted keeper's name standing on a public leaderboard.
 *
 * ## What is deliberately kept, and why each one would be worse to delete
 *
 * - **`receipts/{store}__{txn}`.** This is the record that one store transaction has been
 *   granted, keyed globally because replaying a single real receipt across many accounts is
 *   the industrialised attack (invariant 18a). Deleting it on account deletion would turn
 *   "buy, redeem, delete, sign up again, redeem again" into an unbounded currency faucet that
 *   costs an attacker one purchase. It holds a store transaction id and a uid that no longer
 *   resolves to anybody, and it is retained for fraud prevention — which is the lawful basis
 *   every store's own guidance names for exactly this document.
 *
 * - **`nameReports/{other}/reporters/{uid}`** — reports this account filed about *other*
 *   people. They are records about somebody else, the count on the parent is denormalised
 *   from them, and removing one would either drift that count or silently un-hide a name three
 *   real players had reported. There is also no index that could find them: the uid is a
 *   document id under an unknown parent, so finding them means a collection-group scan over a
 *   collection that grows for the life of the game.
 *
 * ## What a deleted account leaves behind, deliberately, in one case
 *
 * A **denied** name — one taken off the boards by {@link reportName} — is not released. The
 * reservation is retargeted to {@link TOMBSTONE_UID} instead, so the string stays unclaimable
 * by anybody while belonging to nobody. Releasing it would hand an offensive name that three
 * players reported to the next account that asks for it, which is precisely what `reports.ts`
 * keeps the reservation to prevent; deleting the account is not a reason to undo a takedown.
 * An ordinary name *is* released, because it is the player's own and they are leaving.
 */

import { getAuth } from "firebase-admin/auth";
import { getFirestore, Firestore, Transaction } from "firebase-admin/firestore";
import { logger } from "firebase-functions";

import { PATHS } from "./config";
import { GROVE_PATHS, BOARD_ROWS } from "./grove";
import { NAME_PATHS, NameDoc, heldName, isDenied } from "./names";
import { REPORT_PATHS } from "./reports";

/**
 * Who a retained-but-orphaned name reservation belongs to.
 *
 * A real Firebase uid is 28 characters of base-58 and can never be this, so a reservation
 * carrying it matches no caller: {@link claimName} takes the `existing.uid !== uid` branch and
 * answers "taken", which is exactly the behaviour wanted. Written as a value rather than by
 * deleting the `uid` field because {@link NameDoc} promises a string and a reader that has to
 * cope with an absent one is a reader somebody will forget to write.
 */
export const TOMBSTONE_UID = "__deleted__";

/** Every board `rebuildGroveRanks` publishes. Mirrored here so the scrub can name them. */
const BOARD_IDS = ["global", ...Array.from({ length: 9 }, (_, i) => `l${i}`)];

/** What one deletion actually did. Every field is a fact, so a support case is answerable. */
export interface DeletionReport {
  /** The public card was present and is gone. */
  cardRemoved: boolean;

  /** How many published boards this account's row was scrubbed out of. */
  boardsScrubbed: number;

  /** The keeper name that was released back to the pool, or "" if none was. */
  nameReleased: string;

  /** A denied name that was retained under {@link TOMBSTONE_UID} rather than released. */
  nameRetained: string;

  /** The save document and its subcollections are gone. */
  saveRemoved: boolean;

  /** Reports filed *against* this account, removed with it. */
  reportsRemoved: boolean;

  /** Whether Apple was asked to revoke this account's Sign in with Apple grant. */
  appleRevoked: boolean;

  /** Why Apple was not asked, when it was not. Empty when it was, or when it did not apply. */
  appleSkipped: string;

  /**
   * Whether this deployment holds usable Sign in with Apple credentials at all.
   *
   * <b>Reported on every deletion, including the ones Apple has nothing to do with</b>, and
   * that is the whole reason it exists. {@link revokeAppleGrant} tests for the authorization
   * code before it tests for the keys — correctly, because an anonymous account has no code
   * and saying "keys not configured" about it would be a lie. But it means a deployment
   * missing its credentials reports exactly what a fully configured one reports for every
   * account that is not linked to Apple, so a half-configured deployment stays invisible until
   * a real Apple player deletes their account and the revocation silently does not happen.
   * One boolean on every report makes that state visible immediately instead.
   */
  appleConfigured: boolean;

  /** The Firebase Auth user is gone. False only when it was already absent. */
  authRemoved: boolean;
}

function emptyReport(): DeletionReport {
  return {
    cardRemoved: false,
    boardsScrubbed: 0,
    nameReleased: "",
    nameRetained: "",
    saveRemoved: false,
    reportsRemoved: false,
    appleRevoked: false,
    appleSkipped: "",
    appleConfigured: false,
    authRemoved: false,
  };
}

// --------------------------------------------------------------------- the public half

/**
 * Removes the published card, and takes the account's row off every board it is standing on.
 *
 * <b>The scrub is not what `withdrawGrove` does, and the difference is the point.</b> A
 * withdrawal deletes the card and lets the nightly rebuild drop the row, which is right there:
 * the player still exists, the row is stale for at most a day, and paying ten transactions to
 * shave a few hours off a cache is not worth it. Deletion cannot make that trade. "Your name
 * is off the boards" has to be true when this call returns, because the account it named will
 * not exist to correct it, and a card cannot be withdrawn twice.
 *
 * Ten reads and at most ten small writes, once in the life of an account. It is a transaction
 * per board rather than one batch because `rebuildGroveRanks` rewrites all ten in a batch of
 * its own: read-filter-write under a transaction is what makes a collision with the 04:00 job
 * a retry instead of one of the two writes disappearing.
 */
async function removeFromPublicView(db: Firestore, uid: string): Promise<{
  cardRemoved: boolean;
  boardsScrubbed: number;
}> {
  const cardRef = db.doc(GROVE_PATHS.card(uid));
  const card = await cardRef.get();

  if (card.exists) await cardRef.delete();

  let boardsScrubbed = 0;

  for (const boardId of BOARD_IDS) {
    const ref = db.doc(GROVE_PATHS.board(boardId));

    const changed = await db.runTransaction(async (tx: Transaction) => {
      const snapshot = await tx.get(ref);
      if (!snapshot.exists) return false;

      const entries = snapshot.data()?.entries;
      if (!Array.isArray(entries)) return false;

      // Filtered by uid, never by position or by name. Two players may share a display
      // name — a handle derived from a uid cannot collide, but a claimed one is unique only
      // by fold — and a row index means nothing across a rebuild.
      const kept = entries.filter(
        (row: unknown) => (row as { uid?: string })?.uid !== uid
      );

      if (kept.length === entries.length) return false;

      // Only `entries` is written. `population` and `builtUnix` are the rebuild's to own:
      // decrementing a sampled population by one here would be arithmetic on a number that
      // is an estimate by construction, and rewriting `builtUnix` would tell every client
      // the board is newer than the data in it.
      tx.update(ref, { entries: kept.slice(0, BOARD_ROWS) });
      return true;
    });

    if (changed) boardsScrubbed++;
  }

  return { cardRemoved: card.exists, boardsScrubbed };
}

// ---------------------------------------------------------------------- the keeper name

/**
 * Releases the account's name reservation, or retains a denied one under a tombstone.
 *
 * <b>It may only ever touch a document whose `uid` matches the account being deleted.</b>
 * `names.ts` names this hazard where the reservation is defined and it is worth restating:
 * the key comes from a cache on the wallet, and a wallet that is stale — because a claim's
 * reply was lost, or because a moderator edited a holding — points at a key somebody else now
 * owns. Deleting on the strength of the cache alone would take a stranger's name down, and
 * nothing would ever say so. So the reservation is read inside the transaction and compared.
 *
 * Both outcomes are idempotent: a second run finds no holding (the wallet is gone) or finds a
 * reservation that no longer names this uid, and does nothing either way.
 */
async function releaseName(db: Firestore, uid: string): Promise<{
  released: string;
  retained: string;
}> {
  const walletRef = db.doc(PATHS.wallet(uid));

  return db.runTransaction(async (tx: Transaction) => {
    const walletSnapshot = await tx.get(walletRef);
    const holding = heldName(walletSnapshot.data());

    if (!holding || holding.key.length === 0) return { released: "", retained: "" };

    const nameRef = db.doc(NAME_PATHS.name(holding.key));
    const nameSnapshot = await tx.get(nameRef);

    if (!nameSnapshot.exists) return { released: "", retained: "" };

    const existing = nameSnapshot.data() as NameDoc;

    // Somebody else's, or already tombstoned by an earlier run of this same deletion.
    if (existing.uid !== uid) return { released: "", retained: "" };

    if (isDenied(holding)) {
      // Retained rather than released — see the header. The `atUnix` is left exactly as it
      // was: it is when the name was claimed, and rewriting it to now would make a
      // reservation held since last year look like one taken this morning.
      tx.update(nameRef, { uid: TOMBSTONE_UID });
      return { released: "", retained: holding.key };
    }

    tx.delete(nameRef);
    return { released: holding.key, retained: "" };
  });
}

// ------------------------------------------------------------------------ the save itself

/**
 * Deletes `players/{uid}` and everything under it — `private`, `spendLog`, `grantLog`.
 *
 * `recursiveDelete` rather than a hand-rolled walk, because the subcollection *names* must
 * not be a list kept here: a fourth one added next year would otherwise be silently left
 * behind, and nothing would notice — the parent would be gone and the orphan unreachable by
 * every query in the deployment. This walks whatever is actually there.
 */
async function deleteSave(db: Firestore, uid: string): Promise<boolean> {
  const ref = db.doc(PATHS.player(uid));
  const existed = (await ref.get()).exists;

  const bulk = db.bulkWriter();
  bulk.onWriteError((error) => error.failedAttempts < 5);

  await db.recursiveDelete(ref, bulk);

  return existed;
}

/**
 * Deletes the reports filed *against* this account.
 *
 * Kept for a denied name, and that is the same decision the reservation takes: the takedown
 * record is why the string is still held, so throwing it away would leave a tombstone nobody
 * could explain. For an ordinary account they are reports about a name that no longer exists,
 * filed against a player who no longer exists, and there is nothing left for them to protect.
 */
async function deleteReportsAgainst(db: Firestore, uid: string): Promise<boolean> {
  const ref = db.doc(REPORT_PATHS.summary(uid));
  const existed = (await ref.get()).exists;

  if (!existed) return false;

  const bulk = db.bulkWriter();
  bulk.onWriteError((error) => error.failedAttempts < 5);

  await db.recursiveDelete(ref, bulk);

  return true;
}

// ------------------------------------------------------------------------------- the auth

/**
 * Removes the Firebase Auth user. The last thing that happens, for the reason in the header.
 *
 * A missing user is a success, not an error: it means an earlier attempt got this far before
 * its reply was lost, and the client is entitled to be told the account is gone rather than
 * shown a failure for the one thing that had already worked.
 */
async function deleteAuthUser(uid: string): Promise<boolean> {
  try {
    await getAuth().deleteUser(uid);
    return true;
  } catch (error: unknown) {
    const code = (error as { code?: string })?.code ?? "";
    if (code === "auth/user-not-found") return false;
    throw error;
  }
}

/**
 * Whether an account still exists in Firebase Auth.
 *
 * Exported for the two paths a *server* can walk into after a deletion — a refund sweep and
 * an ad network's verification callback. Both write to `players/{uid}/private/wallet`, both
 * are triggered by something outside this deployment, and both would otherwise recreate that
 * document for an account that no longer exists: an orphan nothing reads, nothing cleans up
 * and no query can find.
 *
 * <b>Deliberately not used on the hot client paths.</b> `getWallet`, `submitSpends` and
 * `claimAwards` run on every sync for every player, and one Auth lookup each would be a
 * per-player-per-sync cost, for ever, to catch a window that closes by itself: a deleted
 * account's ID token stays valid for at most an hour, its device has already wiped and signed
 * in as somebody new, and the worst a stale one can do is recreate its own documents under a
 * uid that can never authenticate again.
 */
export async function accountExists(uid: string): Promise<boolean> {
  try {
    await getAuth().getUser(uid);
    return true;
  } catch (error: unknown) {
    const code = (error as { code?: string })?.code ?? "";
    if (code === "auth/user-not-found") return false;

    // Anything else — a transport failure, a quota — is *not* evidence the account is gone,
    // and answering "no" on it would make a refund silently skip a live player's wallet.
    // Erring towards "still there" costs at worst an orphan; erring the other way costs money.
    logger.warn("could not confirm whether an account exists", { uid, code });
    return true;
  }
}

// ----------------------------------------------------------------------------- the whole

export interface DeleteRequest {
  uid: string;

  /**
   * A fresh Sign in with Apple authorization code, when the player has just re-authenticated
   * with Apple. Absent for a guest, for a Google account, and on Android — see
   * {@link revokeAppleGrant}.
   */
  appleAuthorizationCode?: string;

  /** The Apple credentials, when the deployment has them. Revocation is skipped without. */
  apple?: AppleRevocationKeys;
}

/**
 * Erases an account. Safe to call twice, safe to call after a crash, safe to call on a uid
 * that has never played.
 *
 * Ordered so that a failure anywhere leaves a state the *same call* repairs — see the header.
 * A revocation failure in particular never stops the deletion: Apple's endpoint being down is
 * not a reason to refuse somebody their own account back, and the alternative is a player who
 * cannot delete until a third party recovers.
 */
export async function deleteAccount(request: DeleteRequest): Promise<DeletionReport> {
  const db = getFirestore();
  const uid = request.uid;
  const report = emptyReport();

  // 1. Stop being visible to anybody else.
  const view = await removeFromPublicView(db, uid);
  report.cardRemoved = view.cardRemoved;
  report.boardsScrubbed = view.boardsScrubbed;

  // 2. The name, before the wallet that holds the key to it is deleted. Doing this after
  //    step 4 would leave a reservation nothing could ever find again: the key lives only on
  //    the wallet, and `names` is not queryable by uid.
  const name = await releaseName(db, uid);
  report.nameReleased = name.released;
  report.nameRetained = name.retained;

  // 3. Reports filed against this account, while the denial that decides their fate is still
  //    readable — `releaseName` has already read it, and this only needs to know the outcome.
  if (name.retained.length === 0) {
    report.reportsRemoved = await deleteReportsAgainst(db, uid);
  }

  // 4. The save, the wallet, the spend log and the grant log.
  report.saveRemoved = await deleteSave(db, uid);

  // 5. Apple. After the data, because it is the one step that depends on a third party, and
  //    before the auth user, because the provider link is what says it applies at all.
  const revocation = await revokeAppleGrant(request.appleAuthorizationCode, request.apple);
  report.appleRevoked = revocation.revoked;
  report.appleSkipped = revocation.skipped;
  report.appleConfigured = !!request.apple;

  // 6. The account itself. Last, so that every failure above is retryable.
  report.authRemoved = await deleteAuthUser(uid);

  logger.info("account deleted", { uid, ...report });

  return report;
}

// ------------------------------------------------------------------- sign in with apple

/**
 * What signing Apple's client secret needs. All four come from Secret Manager.
 *
 * <b>These are not the App Store Server API credentials.</b> `APPLE_KEY_ID` and friends sign
 * requests about *purchases*; Sign in with Apple is a different key, created under a different
 * heading in the developer portal, and using one for the other fails with an
 * `invalid_client` that names neither. Hence the separate names.
 */
export interface AppleRevocationKeys {
  /** The Sign in with Apple key id (the `.p8`'s 10-character id). */
  keyId: string;

  /** The Apple Developer team id. */
  teamId: string;

  /**
   * What Apple calls the client.
   *
   * <b>For a native iOS sign-in this is the app's bundle id</b>, not the Services ID — the
   * Services ID identifies the *web* flow, and passing it for a token minted by
   * `ASAuthorizationAppleIDProvider` is refused as `invalid_client`. Android reaches Apple
   * through Firebase's web flow and so would need the Services ID, which is exactly why
   * nothing on Android calls this: see the note in {@link revokeAppleGrant}.
   */
  clientId: string;

  /** The `.p8` private key, PEM encoded. */
  privateKey: string;
}

/** Whether the deployment holds everything a revocation needs. */
export function usableAppleKeys(
  keys: Partial<AppleRevocationKeys> | undefined
): AppleRevocationKeys | null {
  if (!keys) return null;

  const { keyId, teamId, clientId, privateKey } = keys;
  if (!keyId || !teamId || !clientId || !privateKey) return null;

  return { keyId, teamId, clientId, privateKey };
}

/**
 * Builds the client secret Apple's token endpoints want: a short-lived ES256 JWT.
 *
 * Five minutes rather than Apple's six-month ceiling, deliberately. It is minted for one
 * request and handed straight to Apple, so a long life buys nothing and a leaked one would be
 * a working credential for half a year.
 */
function clientSecret(keys: AppleRevocationKeys, nowSeconds: number): string {
  // Required lazily so that a deployment without the secrets never loads the library on a
  // path that is not going to use it.
  // eslint-disable-next-line @typescript-eslint/no-var-requires
  const jwt = require("jsonwebtoken") as typeof import("jsonwebtoken");

  return jwt.sign(
    {
      iss: keys.teamId,
      iat: nowSeconds,
      exp: nowSeconds + 300,
      aud: "https://appleid.apple.com",
      sub: keys.clientId,
    },
    keys.privateKey.replace(/\\n/g, "\n"),
    { algorithm: "ES256", header: { alg: "ES256", kid: keys.keyId } }
  );
}

/**
 * Asks Apple to revoke this account's Sign in with Apple grant.
 *
 * <b>Why this exists.</b> Apple requires that deleting an account in an app that offers Sign
 * in with Apple also revokes the tokens it was granted, so the app stops appearing under the
 * player's Apple ID settings. Nothing else in the deletion touches Apple, so without this the
 * account is gone from every system except the one the player is most likely to look at.
 *
 * <b>Why it takes an authorization code rather than a stored refresh token.</b> The obvious
 * design captures Apple's refresh token when the account is *linked* and keeps it until it is
 * needed. That works and it is worse in three ways: it stores a live third-party credential
 * for every Apple player for the life of their account, it adds a code path that runs on every
 * link and is exercised by a deletion months later — so a break in it is invisible until the
 * one moment it matters — and it cannot be repaired for accounts linked before it shipped.
 * Re-authenticating at deletion has none of those. It also earns its keep on its own: an
 * irreversible act is worth proving the person holding the phone actually owns the account.
 *
 * <b>Two calls, not one.</b> An authorization code is single-use and expires in five minutes,
 * and `/auth/revoke` will not take one — so it is exchanged at `/auth/token` for a refresh
 * token, and that is what is revoked. Revoking a refresh token invalidates every access token
 * issued under it, which is the whole grant.
 *
 * <b>A failure is logged and swallowed, never thrown.</b> This runs inside a deletion the
 * player asked for; letting Apple's availability decide whether somebody may delete their own
 * account would be the wrong trade in every direction, and the data is already gone by the
 * time this runs.
 */
export async function revokeAppleGrant(
  authorizationCode: string | undefined,
  keys: AppleRevocationKeys | undefined
): Promise<{ revoked: boolean; skipped: string }> {
  if (!authorizationCode) return { revoked: false, skipped: "no authorization code" };
  if (!keys) return { revoked: false, skipped: "apple sign-in keys not configured" };

  try {
    const secret = clientSecret(keys, Math.floor(Date.now() / 1000));

    const tokenReply = await fetch("https://appleid.apple.com/auth/token", {
      method: "POST",
      headers: { "content-type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({
        client_id: keys.clientId,
        client_secret: secret,
        code: authorizationCode,
        grant_type: "authorization_code",
      }),
    });

    if (!tokenReply.ok) {
      logger.warn("apple refused the authorization code", {
        status: tokenReply.status, body: await tokenReply.text(),
      });
      return { revoked: false, skipped: `token exchange failed (${tokenReply.status})` };
    }

    const refreshToken = (await tokenReply.json() as { refresh_token?: string }).refresh_token;
    if (!refreshToken) return { revoked: false, skipped: "apple returned no refresh token" };

    const revokeReply = await fetch("https://appleid.apple.com/auth/revoke", {
      method: "POST",
      headers: { "content-type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({
        client_id: keys.clientId,
        client_secret: secret,
        token: refreshToken,
        token_type_hint: "refresh_token",
      }),
    });

    if (!revokeReply.ok) {
      logger.warn("apple refused the revocation", {
        status: revokeReply.status, body: await revokeReply.text(),
      });
      return { revoked: false, skipped: `revoke failed (${revokeReply.status})` };
    }

    return { revoked: true, skipped: "" };
  } catch (error) {
    logger.warn("apple revocation threw", { error: String(error) });
    return { revoked: false, skipped: "revocation threw" };
  }
}

/** Exported so the offline suite can drive it; see `test/account.mjs`. */
export { BOARD_IDS, clientSecret };
