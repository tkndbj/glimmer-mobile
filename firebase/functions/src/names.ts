import { Firestore, Transaction } from "firebase-admin/firestore";

import { sanitiseName, isNameAllowed, MIN_NAME_LENGTH } from "./grove";
import { PATHS } from "./config";

/**
 * Keeper names, and the one thing about them that cannot be decided on a device:
 * whether somebody else already has one.
 *
 * <h3>Why this is a collection of documents and not a query</h3>
 *
 * Uniqueness is held by the primary key. A name is reserved by creating
 * `names/{nameKey}`, and Firestore refuses a create against an id that exists — so
 * uniqueness is enforced by the database itself, at any concurrency, with no index and no
 * scan. Three properties follow, and all three are why this shape was chosen over the
 * obvious one:
 *
 * - **It cannot race.** `where("name","==",x)` returns empty for two clients a second
 *   apart and both then write. The duplicate that produces is undetectable afterwards and
 *   can only be repaired by hand.
 * - **It costs the same at ten players and at ten million.** Asking whether a name is free
 *   is one document read by id — not a query, not an index over a collection that grows for
 *   the life of the game.
 * - **It has somewhere to put the reservation.** A rename has to release the old name and
 *   take the new one indivisibly, which a query-and-write has no way to do.
 *
 * <h3>Where the cost actually goes</h3>
 *
 * The high-frequency operation is the client's "is this taken" hint while somebody types,
 * and it is deliberately *not* a callable: the client reads `names/{key}` directly under a
 * rule that grants `get` and refuses `list`, so a hint is one document read and no function
 * invocation. Only the claim is a function, because only the claim needs adjudicating — and
 * a claim happens once or twice in the life of an account.
 *
 * <h3>Why the client is not allowed to write the reservation itself</h3>
 *
 * Rules could express most of it: `create` is already create-if-absent, and the uid could be
 * checked against `request.auth`. What rules cannot express is *one name per account* — that
 * needs the old reservation released in the same operation, and a multi-document constraint
 * whose other half is a document the client itself writes is not a constraint at all.
 * Without it a script holds as many names as it likes for the price of the writes.
 *
 * <h3>What a forged claim buys</h3>
 *
 * Nothing. The name on a card is read from `players/{uid}/private/wallet` — server-owned,
 * written only here — rather than from the save, so a modified client that writes any name
 * it likes into its own save gets its own screens changed and the board unmoved. That is
 * invariant 19b's rule ("the server's answer governs") taken one step further than the
 * sanitiser took it: publication no longer trusts the save's string at all.
 *
 * <h3>What is deliberately not here</h3>
 *
 * There is no `releaseName`. Account deletion is the only caller such a function could have and
 * it does not exist yet (`allow delete: if false` on the save, deliberately — it is a support and
 * compliance operation). One was written and deleted rather than shipped, because an exported,
 * untested path that nothing calls is a placeholder wearing a function's name, and the next person
 * to add deletion would have reached for it without knowing it had never run. When deletion is
 * built, releasing the reservation belongs in the same transaction that removes the account, and
 * it must only ever delete a document whose `uid` matches — a stale cache pointing at somebody
 * else's name would otherwise take theirs down.
 *
 * Note that a **withdrawal is not a release**: opting out of the boards takes the row down and
 * keeps the name, because the name is still the player's and handing it to somebody else while
 * they were not looking is not what they asked for.
 */

/**
 * The longest a collision key may be. Mirrors `GroveNames.MaxKeyLength`.
 *
 * Larger than the name limit because compatibility normalisation expands — U+3390 is one
 * character and folds to four. It bounds a document id and nothing else, and it is far below
 * Firestore's own 1,500-byte limit even at three bytes a character.
 */
export const MAX_KEY_LENGTH = 64;

/**
 * How long after a successful rename the next one may be taken.
 *
 * **This is an abuse bound, not a product rule, and that is why it is a constant rather than
 * content.** Every tunable in this game is published through `progression.json` so it can move
 * without an app update — the heart gate, the chest odds, the ad payouts, the difficulty scalar.
 * This one deliberately is not, for `HeartLimits.HardCeiling`'s reason: it is the bound that
 * makes a forged or scripted client unprofitable, so a config push that could relax it is a
 * config push that could remove it. A minute is invisible to a person and ends name-cycling,
 * which is the only way one account can churn reservations.
 *
 * It deliberately does *not* apply to re-claiming the name already held, so a client retrying
 * after a lost reply succeeds rather than being told to wait — the same reasoning that makes
 * `revisionAdvances` in the rules `>=` rather than `>`. If name-squatting ever needs a harder
 * limit, the lever is the per-account reservation count, which is already one, not this number.
 */
export const RENAME_COOLDOWN_SECONDS = 60;

/** Where a reservation lives. Named once so a typo cannot become two collections. */
export const NAME_PATHS = {
  name: (key: string) => `names/${key}`,
};

/**
 * Letters and decimal digits, of any script.
 *
 * **`\p{Nd}` rather than `\p{N}`, and that is a mirror requirement rather than taste.**
 * C#'s `char.IsLetterOrDigit` is `IsLetter || IsDigit`, and `IsDigit` is the decimal digits
 * alone — it excludes the letter-numbers and the other-numbers that `\p{N}` would match
 * here. A key that differed on those would make the client read one document and the server
 * write another.
 *
 * Folding to ASCII would be shorter and would silently leave every name written in Cyrillic,
 * Greek, Arabic or kana with no reservable key at all, which in a game that ships globally is
 * not a corner case.
 */
const LETTER_OR_DIGIT = /[\p{L}\p{Nd}]/u;

/**
 * The characters the two runtimes' Unicode tables disagree about, mapped by hand so that
 * neither side has to be right about them.
 *
 * **Measured in the Unity Editor rather than assumed.** Unity's Mono expands the fullwidth
 * forms, the squared units, the roman numerals and the fractions exactly as this runtime does,
 * and misses two things: the **Latin ligature block** (U+FB00–FB06), which its compatibility
 * tables do not decompose at all, and **U+1E9E**, whose lowercase it leaves alone where this
 * runtime gives ß. Both are ordinary characters a Mac or a German keyboard produces, so both
 * are worth closing rather than merely documenting.
 *
 * **Applied before normalisation, so it is idempotent either way** — this runtime's tables
 * already decompose these, so mapping them first changes nothing here and makes the device
 * agree. A future Unity that gains them stays correct for the same reason, which is what keeps
 * this from becoming a thing somebody has to remember to delete.
 *
 * **What it deliberately does not close:** the Arabic presentation forms (U+FB50–U+FDFF,
 * U+FE70–U+FEFF), which Mono also leaves alone and which are a large block. A name typed in
 * them folds differently on the two sides, and the consequence is the one this whole split is
 * built to tolerate — a wrong hint under the field, corrected by the claim a moment later,
 * never a duplicate name. Ordinary Arabic keyboards emit the base block, which agrees.
 */
const DISAGREED: Record<string, string> = {
  "ﬀ": "ff",
  "ﬁ": "fi",
  "ﬂ": "fl",
  "ﬃ": "ffi",
  "ﬄ": "ffl",
  "ﬅ": "st",
  "ﬆ": "st",

  // The two case mappings, done here so the lowering below cannot disagree.
  "İ": "i",
  "ẞ": "ß",
};

function agree(text: string): string {
  return text.replace(/[ﬀ-ﬆİẞ]/gu, (ch) => DISAGREED[ch] ?? ch);
}

/**
 * The collision key for a stored name: what two names must share to be the same name.
 *
 * A mirror of `GroveNames.Key`, and the authoritative one. Duplicates get in through
 * normalisation rather than through concurrency — `Fern`, `fern`, `FERN`, `F e r n` and the
 * fullwidth spelling are five documents and one name — so the fold is compatibility
 * normalisation, then a case fold, then everything that is not a letter or a digit dropped.
 *
 * **The fold may only ever be loosened.** Adding confusable folding later (0 for O, Cyrillic
 * a for Latin a) would collapse two names already held onto one key, which needs a repair
 * job rather than a deploy. Removing a rule only ever frees keys up.
 */
export function nameKey(stored: unknown): string {
  const visible = sanitiseName(stored);
  if (visible.length === 0) return "";

  let folded: string;
  try {
    folded = agree(visible).normalize("NFKC");
  } catch {
    // Unreachable for anything `sanitiseName` lets through, which has already dropped the
    // lone surrogates. Folding what we have beats refusing the name.
    folded = agree(visible);
  }

  // The two runtimes' case tables disagree in exactly two places, and both were found by the
  // shared vectors rather than by reading either implementation. This runtime's `toLowerCase`
  // is Unicode's *full* mapping, including SpecialCasing's unconditional and context-sensitive
  // entries; .NET's invariant lowercase is the *simple* one-to-one table.
  //
  //   U+0130 (İ)  the one character whose lowercase is longer than itself. Expanded here to
  //               `i` + U+0307 and left untouched by .NET. Closed in `agree` above, before
  //               normalisation, together with U+1E9E.
  //
  //   U+03C2 (ς)  final sigma, and the one that has to be closed *after* lowering because
  //               lowering is what produces it. Unicode's Final_Sigma condition applies here
  //               and not there, so a Greek name ending in Σ lowers to ς here and to σ on the
  //               device. Folding them together is the right answer anyway: they are one
  //               letter, and a player would not accept that moving a name's last letter makes
  //               it a different name.
  //
  // Everything else in SpecialCasing is conditional on a Turkish or Lithuanian locale, which
  // neither side's locale-independent fold applies.
  folded = folded.toLowerCase().replace(/ς/gu, "σ");

  let out = "";

  for (const ch of folded) {
    if (!LETTER_OR_DIGIT.test(ch)) continue;
    if (out.length >= MAX_KEY_LENGTH) break;

    out += ch;
  }

  return out;
}

/**
 * Whether a stored name is fit to be reserved and shown beside a stranger's.
 *
 * Both measurements, deliberately. A name of punctuation has two visible characters and folds
 * to nothing, so it would be publishable and unreservable, and two keepers would stand on one
 * board under one name with nothing able to tell them apart. Requiring both makes
 * "publishable" and "reservable" one predicate, which is what every caller here quietly
 * assumes.
 */
export function isNameClaimable(stored: unknown): boolean {
  const visible = sanitiseName(stored);
  if (!isNameAllowed(visible)) return false;

  return nameKey(stored).length >= MIN_NAME_LENGTH;
}

/** A reservation. Deliberately two fields: anything else here is a second thing to drift. */
export interface NameDoc {
  uid: string;
  atUnix: number;
}

/**
 * The name this account actually holds, cached on the wallet document.
 *
 * **It lives on the wallet because the wallet is already read by every path that needs it.**
 * `publishGrove` opens it for the affordability clamp, so verifying the name costs nothing
 * extra — where re-reading `names/{key}` on every publish would be a document read per player
 * per publish, for ever, which is exactly the kind of per-player-per-event cost this whole
 * design exists to avoid.
 *
 * It is also the one place a client cannot write, which is what makes the published name
 * unforgeable rather than merely sanitised.
 */
export interface NameHolding {
  /** The reserved key. The id of the document in `names`. */
  key: string;

  /** The public form as it was claimed, which is what a card shows. */
  public: string;

  /** When it was claimed. The cooldown's clock. */
  atUnix: number;
}

/** Every way a claim can end. Two of the five are not failures. */
export type ClaimOutcome =
  | "claimed"
  | "unchanged"
  | "taken"
  | "refused"
  | "cooldown";

export interface ClaimResult {
  outcome: ClaimOutcome;

  /** What the account holds after the call, whatever the outcome. Null when it holds none. */
  holding: NameHolding | null;
}

function readHolding(data: Record<string, unknown> | undefined): NameHolding | null {
  const raw = data?.name as Partial<NameHolding> | undefined;
  if (!raw || typeof raw.key !== "string" || raw.key.length === 0) return null;

  return {
    key: raw.key,
    public: typeof raw.public === "string" ? raw.public : "",
    atUnix: Math.floor(Number(raw.atUnix ?? 0)),
  };
}

/**
 * Reads what this account holds, without attempting anything.
 *
 * Takes the wallet data the caller already has, so it adds no read of its own.
 */
export function heldName(walletData: Record<string, unknown> | undefined): NameHolding | null {
  return readHolding(walletData);
}

/**
 * Reserves a name for an account, releasing whatever it held.
 *
 * **One transaction, and the ordering inside it is the whole safety property.** Both
 * documents are read before either is written — Firestore requires it, and it is also what
 * makes check-then-create atomic: a second caller that read the same absent reservation is
 * retried by the transaction rather than allowed to write over the winner.
 *
 * **Re-claiming the name already held is a success and costs nothing.** That is what makes
 * the call safe to retry after a lost reply, and it is why the offline path in `publishGrove`
 * can attempt a claim on every publish without ever writing twice for one name.
 */
export async function claimName(
  db: Firestore,
  uid: string,
  requested: unknown,
  nowUnix: number
): Promise<ClaimResult> {
  const visible = sanitiseName(requested);
  const key = nameKey(requested);

  const walletRef = db.doc(PATHS.wallet(uid));

  if (!isNameClaimable(requested)) {
    // Refused rather than failed: the player keeps the name on their own screens and is
    // published under a generated handle, which is what `publicName` has always done for a
    // name the filter would not take. Nothing here is worth retrying, so the client is told
    // once and stops (invariant 13a).
    const walletSnapshot = await walletRef.get();
    return { outcome: "refused", holding: readHolding(walletSnapshot.data()) };
  }

  return db.runTransaction(async (tx: Transaction) => {
    const nameRef = db.doc(NAME_PATHS.name(key));

    // One round trip for both, and both before any write — which Firestore requires and which
    // is also what makes check-then-create atomic: a second caller that read the same absent
    // reservation is retried by the transaction rather than allowed to write over the winner.
    const [walletSnapshot, nameSnapshot] = await tx.getAll(walletRef, nameRef);

    const holding = readHolding(walletSnapshot.data());
    const existing = nameSnapshot.exists ? (nameSnapshot.data() as NameDoc) : null;

    // Already ours. Not a write, not a cooldown and not an error — a device retrying after a
    // dropped reply lands here, and so does every publish once the name is settled.
    if (existing && existing.uid === uid && holding?.key === key) {
      return { outcome: "unchanged" as ClaimOutcome, holding };
    }

    if (existing && existing.uid !== uid) {
      return { outcome: "taken" as ClaimOutcome, holding };
    }

    // Tested after "already ours" on purpose: a retry must never be answered with "wait".
    if (holding && nowUnix - holding.atUnix < RENAME_COOLDOWN_SECONDS) {
      return { outcome: "cooldown" as ClaimOutcome, holding };
    }

    const next: NameHolding = { key, public: visible, atUnix: nowUnix };

    // The old reservation goes in the same transaction as the new one. Releasing it
    // afterwards would strand a name for ever on any failure between the two, and there is no
    // scan that could ever find it again.
    if (holding && holding.key !== key) {
      tx.delete(db.doc(NAME_PATHS.name(holding.key)));
    }

    tx.set(nameRef, { uid, atUnix: nowUnix } as NameDoc);
    tx.set(walletRef, { name: next }, { merge: true });

    return { outcome: "claimed" as ClaimOutcome, holding: next };
  });
}
