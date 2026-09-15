import { Firestore, Transaction } from "firebase-admin/firestore";

import { PATHS } from "./config";
import { GROVE_PATHS, fallbackName, heldGrove, isGroveDenied, GroveHolding } from "./grove";
import { NameHolding, heldName } from "./names";

/**
 * Reporting a keeper, and taking down what was reported.
 *
 * ## Why this layer exists at all
 *
 * The word list catches what somebody thought to write down. It will never catch a slur in a
 * language nobody on the team reads, a phrase that is only offensive in one country, or the
 * name that is fine as a word and vicious as a reference. Every large game reaches the same
 * conclusion: the filter buys you the obvious cases, and **reporting is what actually holds
 * the line**. It is also the only part of this that gets better on its own.
 *
 * ## Two subjects, one mechanism
 *
 * A keeper puts two things in front of strangers: a **name**, which is the one piece of free
 * text in this game, and an **arrangement** — 784 tiles of ground sold with walls, gates and
 * fences. `grove.ts`'s header used to argue that the second could not be offensive because
 * every piece is an id from a catalog we ship; that was true of the pre-placed dots the grove
 * shipped with and stopped being true the day land was sold by the region and walls by the
 * bundle. Walls tile. Somebody with enough of them writes whatever they like on the ground, and
 * every gate in this project stays green while they do, because nothing here opens a picture.
 *
 * So a report names a {@link ReportSubject}, and everything else about the mechanism is shared:
 * the pair-keyed idempotency, the distinct-reporter threshold, the daily quota, the audit trail
 * and the review floor. **The two subjects are separate collections and separate counts**,
 * because they are separate judgements — a name three people found offensive says nothing about
 * the benches, and folding them into one tally would hide a grove behind a name's reports.
 *
 * **The quota is shared**, and that is the other way round on purpose: it is an abuse bound on
 * the *reporter's account*, not a budget per subject, so twenty reports a day is twenty reports
 * a day however they are split.
 *
 * ## The shape, and why it is a subcollection rather than a counter
 *
 * A report is `{root}/{target}/reporters/{reporter}` — one document per *pair*. That id is the
 * whole design:
 *
 * - **It is idempotent.** Tapping the button twice is one report, on any device, after any
 *   reinstall, with no client-side state to remember. The same argument that keys an award on
 *   what earned it rather than on a generated id (invariant 10a).
 * - **It cannot be inflated by one account.** Distinct reporters is what the threshold counts,
 *   and it is the only bound that means anything — a raw counter is one person tapping.
 * - **It is auditable.** When a takedown is disputed, who reported it and when is a query
 *   against one small collection rather than a number nobody can explain.
 *
 * The count is denormalised onto the parent so the threshold test is a field read rather than
 * a `count()` over the subcollection. It is written in the same transaction, so it cannot
 * drift from the documents it counts.
 *
 * ## What a takedown does, and what it deliberately does not
 *
 * A **name** takedown sets `deniedUnix` on the account's name holding and rewrites the live
 * card's name to the generated handle. A **grove** takedown sets `deniedUnix` on the account's
 * grove holding and empties the live card's `placed` map. That is all, in both cases. The
 * player keeps their name and their grove on their own screens, keeps their score, their stars,
 * their row and their currency, and is told nothing — because the alternative, a punishment
 * pipeline, needs an appeals process, a support desk and a lawyer before it is honest, and none
 * of that is bought by the thing this actually prevents, which is one row on a board and one
 * picture behind it.
 *
 * **Nothing is released.** A denied name's reservation stays held by the account that took it,
 * which is what stops the reported name being immediately claimed by somebody else; a denied
 * grove keeps every piece in it, because a takedown is not a confiscation and the player has to
 * be able to rearrange their way back.
 *
 * ## Why the auto-hide is safe to run without a human
 *
 * Because it is reversible and cheap. A brigade of three accounts costs a real player a plainer
 * row and nothing else, and a moderator undoes it by clearing one field. Waiting for a human
 * instead means the offensive thing stands for as long as the queue is long, which at launch is
 * the difference between hours and days. `DEFAULT_REPORT_THRESHOLD` is published so the balance
 * can be moved without a deploy, and it is clamped so it can never be moved to one.
 */

/**
 * What is being reported.
 *
 * <b>Permanent strings, for a board id's reason</b> (invariant 1): each one names a Firestore
 * collection, so renaming one orphans every report filed under the old spelling and resets a
 * threshold somebody had already reached. They also cross the wire to the client, where an
 * older build meeting a newer subject must fall back rather than throw — see `parseSubject`.
 */
export type ReportSubject = "name" | "grove";

/**
 * The collection each subject's reports live in.
 *
 * `nameReports` is the spelling that shipped and is kept exactly as it was, so no report ever
 * filed is orphaned by this change.
 */
export const REPORT_ROOTS: Record<ReportSubject, string> = {
  name: "nameReports",
  grove: "groveReports",
};

/** Every subject, for a caller that has to enumerate them (deletion, moderation tooling). */
export const REPORT_SUBJECTS: ReportSubject[] = ["name", "grove"];

/**
 * A subject as it arrives from a client, or null.
 *
 * <b>Absent reads as `name`</b>, which is what every client written before grove reports
 * existed means and is why `reportKeeper` answers an older client without a branch of its own.
 * A subject this deployment does not recognise is **refused** rather than defaulted: a
 * newer client asking for something this build cannot take down must not be told its report was
 * counted (`NameReportOutcome`'s collapse is about hiding moderation state, not about lying).
 */
export function parseSubject(raw: unknown): ReportSubject | null {
  if (raw === undefined || raw === null || raw === "") return "name";
  if (typeof raw !== "string") return null;

  return (REPORT_SUBJECTS as string[]).includes(raw) ? (raw as ReportSubject) : null;
}

/** Where reports live. Named once so a typo cannot become two collections. */
export const REPORT_PATHS = {
  /** The per-target summary: how many distinct players have reported, and the outcome. */
  summary: (subject: ReportSubject, uid: string) => `${REPORT_ROOTS[subject]}/${uid}`,

  /** One document per reporter, so the same player counts once however often they tap. */
  reporter: (subject: ReportSubject, uid: string, reporterUid: string) =>
    `${REPORT_ROOTS[subject]}/${uid}/reporters/${reporterUid}`,

  /**
   * How many reports one account has filed today, across every subject.
   *
   * It lives in the player's own `private` subcollection rather than beside the reports, which
   * costs nothing and buys two things: the rule that makes it server-only already exists
   * (`allow write: if false` covers the whole subcollection), and a support request for
   * everything a deployment knows about one account stays a read of one document tree.
   */
  quota: (reporterUid: string) => `players/${reporterUid}/private/reports`,
};

/**
 * How many keepers one account may report in a day.
 *
 * <b>No single account can hide anything</b> — that needs {@link ReportResult} to reach the
 * threshold, and the threshold counts *distinct* reporters. So this bound is not what stops a
 * takedown being forged; it is what stops one script filing a million writes, and what makes a
 * coordinated brigade cost real accounts rather than one loop.
 *
 * Twenty is far above anything a person does and far below anything an attack needs. It is a
 * constant rather than published config for `RENAME_COOLDOWN_SECONDS`'s reason: it is an abuse
 * bound, so a push that could relax it is a push that could remove it.
 */
export const MAX_REPORTS_PER_DAY = 20;

const SECONDS_PER_DAY = 86400;

/** Every way a report can end. Only one of them is a failure. */
export type ReportOutcome =
  /** Counted. What was reported is still up. */
  | "recorded"
  /** This player had already reported this. Counted once, and that is not an error. */
  | "duplicate"
  /** This report reached the threshold; what was reported is off the boards. */
  | "hidden"
  /** Already down. Reporting it again changes nothing and is not a failure. */
  | "already"
  /** There is nothing published to report — see `nothingToReport`. */
  | "nothing"
  /** A player reported themselves. */
  | "self"
  /** This account has filed its day's reports. Not counted, and it says so. */
  | "throttled";

export interface ReportResult {
  outcome: ReportOutcome;

  /** Distinct reporters after this call. Never returned to the reporting client. */
  reports: number;
}

export interface ReportSummary {
  reports: number;
  deniedUnix: number;

  /** The reserved name's key. Written on a name report; absent on a grove one. */
  key: string;

  /**
   * What was on the board, kept so a moderator reviewing a takedown can see what was actually
   * published rather than having to reconstruct it from a save. For a name that is the name; for
   * a grove it is the keeper's name, which is the only thing about an arrangement a line of text
   * can carry — the arrangement itself is on the card, which the desk can read.
   */
  name: string;

  firstUnix: number;
  lastUnix: number;

  /**
   * The report count as it stood when a moderator last restored this subject, or 0.
   *
   * Written by `firebase/seed/moderate-names.mjs` and read here — a protocol between the desk
   * and the deployment, in the shape `config/names` already uses. There is deliberately no
   * `restore` in this file: reversing a takedown is an admin act over admin credentials, so a
   * version of it here would be an exported path nothing in the deployment calls, which is a
   * placeholder wearing a function's name.
   *
   * <b>This is what stops a review being undone by the next single tap.</b> The count is never
   * reset — it is the audit trail, and clearing it would let the same three reporters hide the
   * name again with nothing on record to show it had been looked at. So the threshold is
   * measured from here instead: after a restore, hiding it again needs a further `threshold`
   * *new* reporters, not one.
   */
  reviewedAt: number;

  /** When a moderator last restored it. Diagnostic; nothing branches on it. */
  reviewedUnix: number;

  /**
   * Which subject this summary is about, written out so a document read on its own says what it
   * is. Nothing branches on it — the collection is the authority — and it is absent on every
   * name summary written before grove reports existed, which is exactly what absent means.
   */
  subject?: ReportSubject;
}

/**
 * Whether there is anything published for this subject to be reported.
 *
 * <b>A name</b>: the keeper has to hold a reservation. Three cases arrive at "nothing" and all
 * three are the same answer — no card has ever been published, the keeper has never chosen a
 * name, or the name they chose was already refused by the filter and the board is showing a
 * generated handle. Reporting a handle the server invented would be reporting us.
 *
 * <b>A grove</b>: the card has to carry at least one placement. An empty floor is not something
 * anybody could have taken offence at, and answering "recorded" for one would let somebody bank
 * reports against an account before it had built anything.
 */
function nothingToReport(
  subject: ReportSubject,
  card: Record<string, unknown> | undefined,
  name: NameHolding | null
): boolean {
  if (!card) return true;

  if (subject === "name") return !name || name.key.length === 0;

  const placed = card.placed;
  return !placed || typeof placed !== "object" || Object.keys(placed).length === 0;
}

/**
 * Records one player's report of another, and hides what was reported at the threshold.
 *
 * <b>One transaction, and every read happens before every write</b> — Firestore requires it,
 * and it is also what makes the threshold test safe: two reporters arriving together are
 * serialised by the transaction rather than both reading "two" and both deciding not to hide.
 *
 * <b>Nothing here trusts the reporter for anything but the target's id and which of two subjects
 * they meant.</b> The name, the key, the arrangement and the count all come from documents this
 * server owns. The request cannot say what was offensive, cannot say how bad it was, and cannot
 * report something that is not actually published — which is what keeps the whole surface to
 * "cause one document to be created".
 */
export async function reportKeeper(
  db: Firestore,
  reporterUid: string,
  targetUid: string,
  subject: ReportSubject,
  nowUnix: number,
  threshold: number
): Promise<ReportResult> {
  // Cheapest possible refusal, and it is a real case rather than a theoretical one: the
  // report control is on a grove card, and a player can visit their own.
  if (reporterUid === targetUid) return { outcome: "self", reports: 0 };

  const cardRef = db.doc(GROVE_PATHS.card(targetUid));
  const walletRef = db.doc(PATHS.wallet(targetUid));
  const summaryRef = db.doc(REPORT_PATHS.summary(subject, targetUid));
  const reporterRef = db.doc(REPORT_PATHS.reporter(subject, targetUid, reporterUid));
  const quotaRef = db.doc(REPORT_PATHS.quota(reporterUid));

  const day = Math.floor(nowUnix / SECONDS_PER_DAY);

  return db.runTransaction(async (tx: Transaction) => {
    const [cardSnapshot, walletSnapshot, summarySnapshot, reporterSnapshot, quotaSnapshot] =
      await tx.getAll(cardRef, walletRef, summaryRef, reporterRef, quotaRef);

    const wallet = walletSnapshot.data();
    const name: NameHolding | null = heldName(wallet);
    const grove: GroveHolding | null = heldGrove(wallet);

    const card = cardSnapshot.exists
      ? (cardSnapshot.data() as Record<string, unknown> | undefined)
      : undefined;

    if (nothingToReport(subject, card, name)) {
      return { outcome: "nothing" as ReportOutcome, reports: 0 };
    }

    const alreadyDenied = subject === "name"
      ? Math.floor(Number(name?.deniedUnix ?? 0)) > 0
      : isGroveDenied(grove);

    const previous = summarySnapshot.exists
      ? (summarySnapshot.data() as Partial<ReportSummary>)
      : undefined;

    const counted = Math.floor(Number(previous?.reports ?? 0));

    // Already reported by this player. The document is not rewritten — its timestamp is when
    // they first reported, which is the useful one — the count is not moved, and this does not
    // spend a day's quota. Tested before the quota deliberately: a player re-tapping a button
    // must never be told they have run out of something.
    if (reporterSnapshot.exists) {
      return {
        outcome: (alreadyDenied ? "already" : "duplicate") as ReportOutcome,
        reports: counted,
      };
    }

    const quota = quotaSnapshot.data() as { day?: unknown; filed?: unknown } | undefined;
    const filed = Math.floor(Number(quota?.day ?? -1)) === day
      ? Math.floor(Number(quota?.filed ?? 0))
      : 0;

    if (filed >= MAX_REPORTS_PER_DAY) {
      return { outcome: "throttled" as ReportOutcome, reports: counted };
    }

    const reports = counted + 1;

    // Measured from the last review rather than from zero, so restoring does not leave it one
    // tap from being hidden again. `reviewedAt` is 0 until a moderator touches it, which makes
    // the ordinary case exactly `reports >= threshold`.
    const floor = Math.floor(Number(previous?.reviewedAt ?? 0));
    const hide = !alreadyDenied && reports - floor >= threshold;

    tx.set(reporterRef, { atUnix: nowUnix });
    tx.set(quotaRef, { day, filed: filed + 1 }, { merge: true });

    tx.set(summaryRef, {
      reports,
      subject,
      key: subject === "name" ? (name?.key ?? "") : "",

      // Who it was, as published. For a name that is the thing complained about; for a grove it
      // is how the desk finds the account whose card it now has to look at.
      name: name?.public ?? "",

      firstUnix: Math.floor(Number(previous?.firstUnix ?? 0)) || nowUnix,
      lastUnix: nowUnix,
      deniedUnix: hide ? nowUnix : Math.floor(Number(previous?.deniedUnix ?? 0)),
    }, { merge: true });

    if (hide) {
      // Merged rather than set, because this is the wallet — the document holding the
      // account's granted and spent baselines. A whole-document write here would be the most
      // expensive mistake available in this codebase.
      //
      // The two subjects write two different fields and neither can reach the other's: a grove
      // takedown must never be able to hide a name, which is the one way a single mechanism
      // serving two judgements could quietly become one judgement.
      tx.set(walletRef, subject === "name"
        ? { name: { deniedUnix: nowUnix } }
        : { grove: { deniedUnix: nowUnix } }, { merge: true });

      // The live card is corrected in the same transaction rather than left for the next
      // publish. A takedown that waits on the player to open their own grove is a takedown that
      // may never happen — and the delay is visible to exactly the people who reported it.
      //
      // `placed: {}` rather than a delete, because a deleted field on a card reads as "written
      // by an older deployment" to every client, and an empty map is unambiguous. It is also
      // precisely what `buildCard` writes from now on for this account, so the corrected card
      // and the next published one are the same card.
      tx.update(cardRef, subject === "name"
        ? { name: fallbackName(targetUid) }
        : { placed: {} });
    }

    return { outcome: (hide ? "hidden" : "recorded") as ReportOutcome, reports };
  });
}

/**
 * The name subject, kept under its old name for the callers that only ever meant it.
 *
 * Not deprecated and not a shim: `deleteAccount` and the moderation desk both genuinely mean
 * "the name", and spelling the subject at those call sites would be three places to get one
 * constant right.
 */
export function reportName(
  db: Firestore,
  reporterUid: string,
  targetUid: string,
  nowUnix: number,
  threshold: number
): Promise<ReportResult> {
  return reportKeeper(db, reporterUid, targetUid, "name", nowUnix, threshold);
}
