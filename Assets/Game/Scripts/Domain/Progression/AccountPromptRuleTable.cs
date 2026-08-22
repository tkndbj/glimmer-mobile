using System.Collections.Generic;
using GlimmerGrove.Content;
using UnityEngine;

namespace GlimmerGrove.Progression
{
    /// <summary>
    /// The bounds that survive any content file, and the numbers used when there is none.
    ///
    /// <para>
    /// <c>HeartLimits</c>' and <c>DifficultyLimits</c>' job for the account prompt: content may
    /// retune how often the game asks, it may not redefine what asking is. Everything here is a
    /// compile-time constant precisely because it is what a published file is checked
    /// <em>against</em> — a limit that could itself be published would not be a limit.
    /// </para>
    /// </summary>
    public static class AccountPromptLimits
    {
        public const int DefaultChapterBudget = 2;
        public const int DefaultPurchaseBudget = 3;
        public const int DefaultQuietHours = 48;

        /// <summary>
        /// The most times a published file may ask, per trigger, for the life of an install.
        ///
        /// <para>
        /// This is the bound whose absence is not a mistuning but a hostile game: a file
        /// pushed on a Friday saying "ask every time" would put a modal in front of every
        /// purchase for every guest in the world, with no app update to roll back and a
        /// plausible route to store review complaints. Ten is far above any honest value and
        /// far below anything anyone would experience as harassment.
        /// </para>
        /// </summary>
        public const int MaxBudget = 10;

        /// <summary>
        /// Zero is a legal budget and is the point of the lever.
        ///
        /// <para>
        /// If the prompt turns out to cost more conversion than the protection is worth, the
        /// fix has to be available in minutes rather than in a store review — so "ask nobody"
        /// is a value a published file can set, for either trigger independently. That is why
        /// the DTO's "unset" sentinel is -1 rather than 0: the difference between an author
        /// writing zero and an author writing nothing has to survive, which is the convention
        /// <see cref="HeartsDto"/> already established.
        /// </para>
        /// </summary>
        public const int MinBudget = 0;

        /// <summary>
        /// Never zero. A quiet period of nothing lets two triggers land back to back, which
        /// is the exact failure the shared clock exists to prevent, and it is reachable by a
        /// typo rather than by a decision.
        /// </summary>
        public const int MinQuietHours = 1;

        /// <summary>Thirty days. Past this the budget is doing the work, not the spacing.</summary>
        public const int MaxQuietHours = 720;
    }

    /// <summary>
    /// How often the game may ask an anonymous player to attach a real account — content, not
    /// code.
    ///
    /// <para>
    /// It is here for the reason the ad caps are here. <c>ads.cooldownSeconds</c> and each
    /// placement's <c>dailyCap</c> are published precisely so the pacing of an offer can be
    /// lowered from a config push, and this is the same class of number: it paces an
    /// interruption, it is not adjudicated by anything, and the right value is discovered from
    /// live link rates rather than known in advance. Shipping it as a <c>const</c> would mean
    /// that finding out the modal costs conversion, or that two asks are not enough, needs a
    /// store review — which is the mistake this project has already recorded against the heart
    /// gate, the chest odds and the clock.
    /// </para>
    /// <para>
    /// It is deliberately <b>not</b> published to <c>config/progression</c> by the seeder, for
    /// <c>difficulty</c>'s reason: nothing about a prompt is adjudicated, so the server has no
    /// opinion to hold and there is nothing to keep in step. It reaches nothing stored, nothing
    /// merged and nothing that pays, so a retune needs no migration and no deploy.
    /// </para>
    /// <para>
    /// Like every other optional block this is not a schema bump — a client that predates it
    /// keeps the built-in pacing.
    /// </para>
    /// </summary>
    public sealed class AccountPromptRuleTable
    {
        AccountPromptRuleTable(int chapterBudget, int purchaseBudget, long quietSeconds)
        {
            ChapterBudget = chapterBudget;
            PurchaseBudget = purchaseBudget;
            QuietSeconds = quietSeconds;
        }

        /// <summary>Times a finished chapter may raise the panel, for the life of an install.</summary>
        public int ChapterBudget { get; }

        /// <summary>Times a completed purchase may raise it. Separate on purpose — see the policy.</summary>
        public int PurchaseBudget { get; }

        /// <summary>The shortest gap between any two automatic asks, whatever raised them.</summary>
        public long QuietSeconds { get; }

        /// <summary>The pacing that ships inside the build.</summary>
        public static readonly AccountPromptRuleTable Default = new AccountPromptRuleTable(
            AccountPromptLimits.DefaultChapterBudget,
            AccountPromptLimits.DefaultPurchaseBudget,
            AccountPromptLimits.DefaultQuietHours * 3600L);

        // ------------------------------------------------------------------ building
        /// <summary>
        /// Reads the optional <c>prompts</c> block. Never throws and never returns null:
        /// anything wrong is named in <paramref name="problems"/> and the built-in pacing
        /// stands, because a content mistake must fail a build and never a session.
        /// </summary>
        public static AccountPromptRuleTable Resolve(PromptsDto dto, List<string> problems)
        {
            problems ??= new List<string>();
            if (dto == null) return Default;                  // absent is not an error

            int chapter = Budget(dto.chapterBudget, AccountPromptLimits.DefaultChapterBudget,
                                 "chapterBudget", problems);
            int purchase = Budget(dto.purchaseBudget, AccountPromptLimits.DefaultPurchaseBudget,
                                  "purchaseBudget", problems);

            int hours = dto.quietHours;
            if (hours < 0) hours = AccountPromptLimits.DefaultQuietHours;   // unset
            else if (hours < AccountPromptLimits.MinQuietHours
                     || hours > AccountPromptLimits.MaxQuietHours)
            {
                problems.Add($"prompts quietHours is {hours}, outside the " +
                             $"{AccountPromptLimits.MinQuietHours}–{AccountPromptLimits.MaxQuietHours} " +
                             "band a published file may ask for; clamped");
                hours = Mathf.Clamp(hours, AccountPromptLimits.MinQuietHours,
                                    AccountPromptLimits.MaxQuietHours);
            }

            return new AccountPromptRuleTable(chapter, purchase, hours * 3600L);
        }

        static int Budget(int written, int fallback, string field, List<string> problems)
        {
            if (written < 0) return fallback;                 // unset, the file's own convention

            if (written > AccountPromptLimits.MaxBudget)
            {
                problems.Add($"prompts {field} is {written}, above the " +
                             $"{AccountPromptLimits.MaxBudget} a published file may ask for; clamped");
                return AccountPromptLimits.MaxBudget;
            }

            return written;                                   // zero is legal: it turns the ask off
        }
    }

    /// <summary>
    /// The live prompt pacing, read the way <c>HeartRules</c> and <c>DifficultyRules</c> are —
    /// a facade over the published table, so a call site reads as it did when these were
    /// constants.
    /// </summary>
    public static class AccountPromptRules
    {
        public static AccountPromptRuleTable Table => ProgressionRules.Table.Prompts;
    }
}
