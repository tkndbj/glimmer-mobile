import { Firestore, Transaction } from "firebase-admin/firestore";

import { PATHS } from "./config";
import { GROVE_PATHS, fallbackName } from "./grove";
import { NameHolding, heldName } from "./names";

/**
 * Reporting a keeper's name, and taking it down.
 *
 * ## Why this layer exists at all
 *
 * The word list catches what somebody thought to write down. It will never catch a slur in a
 * language nobody on the team reads, a phrase that is only offensive in one country, or the
 * name that is fine as a word and vicious as a reference. Every large game reaches the same
 * conclusion: the filter buys you the obvious cases, and **reporting is what actually holds
 * the line**. It is also the only part of this that gets better on its own.
 *
 * ## The shape, and why it is a subcollection rather than a counter
 *
 * A report is `nameReports/{target}/reporters/{reporter}` — one document per *pair*. That id
 * is the whole design:
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
 * It sets `deniedUnix` on the account's name holding and rewrites the live card's name to the
 * generated handle. That is all. The player keeps their name on their own screens, keeps their
 * grove, keeps their currency and is told nothing — because the alternative, a punishment
 * pipeline, needs an appeals process, a support desk and a lawyer before it is honest, and
 * none of that is bought by the thing this actually prevents, which is one row on a board.
 *
 * **The reservation is not released.** The key stays held by the account that took it, which
 * is what stops the reported name being immediately claimed by somebody else — a release would
 * hand a name somebody had just been punished for to the next person to ask for it.
 *
 * ## Why the auto-hide is safe to run without a human
 *
 * Because it is reversible and cheap. A brigade of three accounts costs a real player a
 * plainer row and nothing else, and a moderator undoes it by clearing one field. Waiting for a
 * human instead means the offensive name stands for as long as the queue is long, which at
 * launch is the difference between hours and days. `DEFAULT_REPORT_THRESHOLD` is published so
 * the balance can be moved without a deploy, and it is clamped so it can never be moved to one.
 */

/** Where reports live. Named once so a typo cannot become two collections. */
export const REPORT_PATHS = {
  /** The per-target summary: how many distinct players have reported, and the outcome. */
  summary: (uid: string) => `nameReports/${uid}`,

  /** One document per reporter, so the same player counts once however often they tap. */
  reporter: (uid: string, reporterUid: string) => `nameReports/${uid}/reporters/${reporterUid}`,

  /**
   * How many reports one account has filed today.
   *
   * It lives in the player's own `private` subcollection rather than in `nameReports`, which
   * costs nothing and buys two things: the rule that makes it server-only already exists
   * (`allow write: if false` covers the whole subcollection), and a support request for
   * everything a deployment knows about one account stays a read of one document tree.
   */
  quota: (reporterUid: string) => `players/${reporterUid}/private/reports`,
};

/**
 * How many keepers one account may report in a day.
 *
 * <b>No single account can hide a name</b> — that needs {@link ReportResult} to reach the
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
  /** Counted. The name is still up. */
  | "recorded"
  /** This player had already reported this name. Counted once, and that is not an error. */
  | "duplicate"
  /** This report reached the threshold; the name is off the boards. */
  | "hidden"
  /** Already down. Reporting it again changes nothing and is not a failure. */
  | "already"
  /** There is no published card, or it carries a generated handle rather than a chosen name. */
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
  key: string;
  name: string;
  firstUnix: number;
  lastUnix: number;

  /**
   * The report count as it stood when a moderator last restored this name, or 0.
   *
   * Written by `firebase/seed/moderate-names.mjs` and read here — a protocol between the desk
   * and the deployment, in the shape `config/names` already uses. There is deliberately no
   * `restoreName` in this file: reversing a takedown is an admin act over admin credentials,
   * so a version of it here would be an exported path nothing in the deployment calls, which
   * is a placeholder wearing a function's name.
   *
   * <b>This is what stops a review being undone by the next single tap.</b> The count is never
   * reset — it is the audit trail, and clearing it would let the same three reporters hide the
   * name again with nothing on record to show it had been looked at. So the threshold is
   * measured from here instead: after a restore, hiding the name again needs a further
   * `threshold` *new* reporters, not one.
   */
  reviewedAt: number;

  /** When a moderator last restored the name. Diagnostic; nothing branches on it. */
  reviewedUnix: number;
}

/**
 * Records one player's report of another's published name, and hides it at the threshold.
 *
 * <b>One transaction, and every read happens before every write</b> — Firestore requires it,
 * and it is also what makes the threshold test safe: two reporters arriving together are
 * serialised by the transaction rather than both reading "two" and both deciding not to hide.
 *
 * <b>Nothing here trusts the reporter for anything but the target's id.</b> The name, the key
 * and the count all come from documents this server owns. The request cannot say what was
 * offensive, cannot say how bad it was, and cannot report a name that is not actually
 * published — which is what keeps the whole surface to "cause one document to be created".
 */
export async function reportName(
  db: Firestore,
  reporterUid: string,
  targetUid: string,
  nowUnix: number,
  threshold: number
): Promise<ReportResult> {
  // Cheapest possible refusal, and it is a real case rather than a theoretical one: the
  // report control is on a grove card, and a player can visit their own.
  if (reporterUid === targetUid) return { outcome: "self", reports: 0 };

  const cardRef = db.doc(GROVE_PATHS.card(targetUid));
  const walletRef = db.doc(PATHS.wallet(targetUid));
  const summaryRef = db.doc(REPORT_PATHS.summary(targetUid));
  const reporterRef = db.doc(REPORT_PATHS.reporter(targetUid, reporterUid));
  const quotaRef = db.doc(REPORT_PATHS.quota(reporterUid));

  const day = Math.floor(nowUnix / SECONDS_PER_DAY);

  return db.runTransaction(async (tx: Transaction) => {
    const [cardSnapshot, walletSnapshot, summarySnapshot, reporterSnapshot, quotaSnapshot] =
      await tx.getAll(cardRef, walletRef, summaryRef, reporterRef, quotaRef);

    const holding: NameHolding | null = heldName(walletSnapshot.data());

    // Nothing to report. Three cases arrive here and all three are the same answer: no card
    // has ever been published, the keeper has never chosen a name, or the name they chose was
    // already refused by the filter and the board is showing a generated handle. Reporting a
    // handle the server invented would be reporting us.
    if (!cardSnapshot.exists || !holding || holding.key.length === 0) {
      return { outcome: "nothing" as ReportOutcome, reports: 0 };
    }

    const alreadyDenied = Math.floor(Number(holding.deniedUnix ?? 0)) > 0;

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

    // Measured from the last review rather than from zero, so restoring a name does not leave
    // it one tap from being hidden again. `reviewedAt` is 0 until a moderator touches it,
    // which makes the ordinary case exactly `reports >= threshold`.
    const floor = Math.floor(Number(previous?.reviewedAt ?? 0));
    const hide = !alreadyDenied && reports - floor >= threshold;

    tx.set(reporterRef, { atUnix: nowUnix });
    tx.set(quotaRef, { day, filed: filed + 1 }, { merge: true });

    tx.set(summaryRef, {
      reports,
      key: holding.key,

      // The name as published, kept so a moderator reviewing a takedown can see what was
      // actually on the board rather than having to reconstruct it from a save.
      name: holding.public,

      firstUnix: Math.floor(Number(previous?.firstUnix ?? 0)) || nowUnix,
      lastUnix: nowUnix,
      deniedUnix: hide ? nowUnix : Math.floor(Number(previous?.deniedUnix ?? 0)),
    }, { merge: true });

    if (hide) {
      // Merged rather than set, because this is the wallet — the document holding the
      // account's granted and spent baselines. A whole-document write here would be the most
      // expensive mistake available in this codebase.
      tx.set(walletRef, { name: { deniedUnix: nowUnix } }, { merge: true });

      // The live card is corrected in the same transaction rather than left for the next
      // publish. A takedown that waits on the player to open their own grove is a takedown
      // that may never happen — and the delay is visible to exactly the people who reported it.
      tx.update(cardRef, { name: fallbackName(targetUid) });
    }

    return { outcome: (hide ? "hidden" : "recorded") as ReportOutcome, reports };
  });
}
