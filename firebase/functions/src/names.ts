import { Firestore, Transaction } from "firebase-admin/firestore";

import { logger } from "firebase-functions";

import { sanitiseName, isNameAllowed, MIN_NAME_LENGTH } from "./grove";
import { PreparedBlocklist, judgeName } from "./profanity";
import { builtInBlocklist } from "./blocklist";
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
export function isNameClaimable(stored: unknown, list?: PreparedBlocklist): boolean {
  const visible = sanitiseName(stored);
  if (!isNameAllowed(visible, list)) return false;

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

  /**
   * When this name was taken off the boards, or 0 if it never was.
   *
   * <h4>Why the denial lives on the holding rather than on the reservation</h4>
   *
   * Both are defensible and only one is free. `publishGrove` already opens the wallet -- for
   * the affordability clamp and for this holding -- so a flag here is read at no cost on the
   * one path that has to honour it, where a flag on `names/{key}` would be a second document
   * read per publish per player, for ever, to carry one bit that is almost always zero.
   *
   * It costs nothing in safety, because a denied name's reservation is **not** released: the
   * key stays held by the account that took it, so nobody else can claim it and there is
   * nothing left for a flag over there to protect. See `reports.ts`.
   *
   * <h4>Why it is a date and not a boolean</h4>
   *
   * A moderator reversing a bad takedown needs to know when it happened, and a support case
   * needs to be answerable. A boolean throws that away to save one field. Anything above zero
   * reads as denied, so nothing branches on the value itself.
   */
  deniedUnix?: number;
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
    deniedUnix: Math.floor(Number(raw.deniedUnix ?? 0)),
  };
}

/**
 * Whether a name has been taken off the boards.
 *
 * Written as a predicate so a caller cannot get the sense of it backwards. The obvious
 * `holding.deniedUnix > 0` is correct, and is exactly the expression somebody writes as
 * `!holding.deniedUnix` at the fourth call site. There are four call sites.
 */
export function isDenied(holding: NameHolding | null | undefined): boolean {
  return !!holding && Math.floor(Number(holding.deniedUnix ?? 0)) > 0;
}

/**
 * The name a card should carry, or null when there is none to show.
 *
 * <b>A denied name resolves to null rather than to itself</b>, so the caller falls through to
 * `fallbackName` exactly as it does for a keeper who has never renamed. That fall-through is
 * the entire mechanism of a takedown, and it lives here rather than at the call site because
 * `publishGrove` and the report path both have to agree about it -- a rule with two readers
 * and no home is a rule that ends up with two answers.
 */
export function publishableName(holding: NameHolding | null | undefined): string | null {
  if (!holding || isDenied(holding)) return null;

  return holding.public.length > 0 ? holding.public : null;
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
  nowUnix: number,
  list?: PreparedBlocklist
): Promise<ClaimResult> {
  const visible = sanitiseName(requested);
  const key = nameKey(requested);

  // Resolved once rather than at each use. The parameter is optional so the two callers that
  // have a loaded list can pass it and the tests need not; letting that `undefined` reach two
  // separate defaults would be two places for them to stop agreeing about what was judged.
  const words = list ?? builtInBlocklist();

  const walletRef = db.doc(PATHS.wallet(uid));

  if (!isNameClaimable(requested, words)) {
    // **The only evidence that the word list is the right word list.**
    //
    // A refused name is silent by design: the player keeps it on their own screens and simply
    // appears under a generated handle, which is the proportionate response and is why nothing
    // here is an error. The cost of that is no signal at all — the list can be retuned from a
    // console in minutes, and without this there is no way to learn that it *needs* retuning
    // except somebody complaining. That is exactly how `rape` went on refusing **Grapevine**
    // for a year in a game about a garden.
    //
    // What is logged is the entry that fired and the class it came from, never the name that
    // was typed. The entry is ours; the name is the player's, and a public list of the names
    // people tried to call themselves is not something an operations log should accumulate.
    // `uid` is here because "why can I not rename" is a support question, and it identifies an
    // account without describing anybody.
    const verdict = judgeName(visible, words);

    logger.info("keeper name refused", {
      uid,
      reason: verdict.kind ?? "unusable",
      entry: verdict.word ?? "",

      // Both lengths, because a name refused with no matching entry was refused for shape —
      // too short once folded, or nothing but punctuation — and the two are told apart here
      // and nowhere else.
      visibleLength: visible.length,
      keyLength: key.length,
    });

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
      // A denied name is the one exception, and it has to be handled *here*, because this is
      // the branch every publish takes once a name has settled. Without it, reporting a name
      // would take the card down and the very next publish would put it straight back.
      //
      // Reported as `refused` rather than as an outcome of its own, because from the player's
      // side it is the same thing: the name is still theirs, their own screens keep drawing
      // it, and it is not what strangers see. `refused` is also already the permanent one, so
      // the client stops asking rather than resubmitting for the life of the account
      // (invariant 13a).
      if (isDenied(holding)) return { outcome: "refused" as ClaimOutcome, holding };

      return { outcome: "unchanged" as ClaimOutcome, holding };
    }

    if (existing && existing.uid !== uid) {
      return { outcome: "taken" as ClaimOutcome, holding };
    }

    // Tested after "already ours" on purpose: a retry must never be answered with "wait".
    if (holding && nowUnix - holding.atUnix < RENAME_COOLDOWN_SECONDS) {
      return { outcome: "cooldown" as ClaimOutcome, holding };
    }

    // A fresh claim is never born denied: a denial attaches to a name *this account holds*,
    // and this is a different name. The zero is written explicitly rather than left absent
    // because the wallet write below is a merge -- a field it is not given keeps whatever it
    // had, so an omitted zero would carry the previous name's denial onto its replacement and
    // take the new name down for something the old one did.
    const next: NameHolding = { key, public: visible, atUnix: nowUnix, deniedUnix: 0 };

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
