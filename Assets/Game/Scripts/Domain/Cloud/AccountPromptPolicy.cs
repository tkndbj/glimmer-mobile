using GlimmerGrove.Progression;

namespace GlimmerGrove.Cloud
{
    /// <summary>What raised the account panel by itself, rather than the player asking for it.</summary>
    public enum AccountPromptTrigger
    {
        /// <summary>
        /// A chapter was finished. The oldest trigger, and the one that catches the players
        /// who will never spend anything: it fires at the first moment there is a body of
        /// work worth protecting.
        /// </summary>
        Chapter,

        /// <summary>
        /// Real money became currency. The highest-value trigger, and the only one where the
        /// thing at risk cannot be earned back by playing again — see the class comment.
        /// </summary>
        Purchase,
    }

    /// <summary>
    /// When the game may ask an anonymous player to attach a real account to their grove.
    ///
    /// <para>
    /// <b>Why this is a policy and not two if statements.</b> An anonymous account dies with
    /// the installation: the credential lives in the app's own storage and no server can mint
    /// it again, so a reinstall, a wipe or a lost phone takes the uid and everything keyed on
    /// it. For progress that is bad. For <em>money</em> it is worse and differently shaped —
    /// gems and credits granted by <c>redeemPurchase</c> are server-owned currency keyed on
    /// that uid, and a receipt is recorded globally against
    /// <c>receipts/{store}__{transactionId}</c>, so a fresh installation presenting the same
    /// transaction is refused as a replay. Correctly: that global key is what stops one real
    /// receipt being spent across thousands of accounts. The consequence is that a purchase
    /// made on an anonymous account and then lost is not merely un-synced, it is
    /// <b>unrecoverable by any route, including the stores' own restore</b>. Nobody can give
    /// it back. That is the whole reason this trigger exists.
    /// </para>
    /// <para>
    /// <b>It never blocks a purchase, and that is the design rather than a concession.</b>
    /// The shop already argues the general case — the payment sheet <em>is</em> the
    /// confirmation, and a panel of ours in front of it is a tap for a question about to be
    /// asked properly a second later. An OAuth consent screen is worse than a panel: it
    /// backgrounds the app in the middle of a decision, and a player who declines it has been
    /// talked out of a purchase by a dialog whose purpose was to protect that purchase. So
    /// the ask happens <em>after</em> the grant lands, when the sale is banked, the player is
    /// at their most pleased, and "keep what you just bought" is the easiest sentence in the
    /// game to agree with. The standing half of the warning is the notice on the shop's own
    /// money shelves, which costs nobody a tap.
    /// </para>
    /// <para>
    /// <b>Two budgets, one spacing, and the split matters.</b> The counts are per trigger so
    /// that a player who buys gems in their first week cannot burn the chapter nudge's
    /// allowance — those two asks reach different populations and neither should starve the
    /// other. The quiet period is shared, because it answers a different question: not "have
    /// we made this case yet" but "how often may this game interrupt somebody", and the answer
    /// to that cannot depend on which subsystem is doing the interrupting. Without a shared
    /// clock a player who finished a chapter and then bought a coin pack would meet two
    /// account panels inside a minute, which is how a prompt teaches people to dismiss
    /// prompts.
    /// </para>
    /// <para>
    /// <b>All three numbers are content</b>, handed in as an <see cref="AccountPromptRuleTable"/>
    /// rather than read off a facade, so this stays a pure function of its arguments and a test
    /// can pass whatever pacing it wants without touching global state. The caller passes the
    /// live table, which is what makes a config push take effect on the next ask rather than on
    /// the next launch. A budget of zero is legal and switches a trigger off — the lever that
    /// matters if the modal turns out to cost more conversion than it protects.
    /// </para>
    /// <para>
    /// <b>It holds no clock and reaches nothing.</b> Handed the time and the account's state,
    /// exactly as <c>SyncScheduler</c> and <c>RunScreen.Tick</c> are handed theirs, so the whole
    /// policy runs in the offline test suite — which matters here more than usual, because
    /// every state it is about (a live SDK session, a real purchase, a device that has been
    /// away for two days) is one the Editor never reaches. Persisting the counts is the
    /// caller's job, for <c>GrovePublishPolicy</c>'s reason: what a device has shown a person
    /// is a fact about the installation, it only ever rises, and it must never travel to a
    /// second phone through the cloud and arrive there as a reason to stay quiet.
    /// </para>
    /// </summary>
    public sealed class AccountPromptPolicy
    {
        int _chapterOffers;
        int _purchaseOffers;
        long _lastOfferedUnix;

        public int ChapterOffers => _chapterOffers;
        public int PurchaseOffers => _purchaseOffers;
        public long LastOfferedUnix => _lastOfferedUnix;

        /// <summary>
        /// Loads what this installation has already shown somebody.
        ///
        /// Clamps rather than trusting, because the numbers come back from device storage that
        /// a player can edit, and a negative count would hand out an unlimited supply of
        /// prompts. The failure mode is not a lost grove, but it is the most irritating bug
        /// this file could have and it would be blamed on the game rather than on the edit.
        /// </summary>
        public void Adopt(int chapterOffers, int purchaseOffers, long lastOfferedUnix)
        {
            _chapterOffers = chapterOffers > 0 ? chapterOffers : 0;
            _purchaseOffers = purchaseOffers > 0 ? purchaseOffers : 0;
            _lastOfferedUnix = lastOfferedUnix > 0 ? lastOfferedUnix : 0;
        }

        public static int BudgetFor(AccountPromptRuleTable rules, AccountPromptTrigger trigger)
        {
            rules ??= AccountPromptRuleTable.Default;
            return trigger == AccountPromptTrigger.Purchase ? rules.PurchaseBudget
                                                            : rules.ChapterBudget;
        }

        public int OffersMade(AccountPromptTrigger trigger)
            => trigger == AccountPromptTrigger.Purchase ? _purchaseOffers : _chapterOffers;

        /// <summary>
        /// Whether this trigger may raise the panel right now.
        ///
        /// <para>
        /// Silent while the device is caught between two accounts. A player there <em>is</em>
        /// signed in, so the guest copy would be false, and they have a more urgent thing to be
        /// told which the profile card is already saying.
        /// </para>
        /// <para>
        /// Silent when there is no backend at all, because then there is nothing to link to and
        /// the panel would offer two buttons that cannot work.
        /// </para>
        /// </summary>
        public bool ShouldOffer(AccountPromptTrigger trigger, AccountPromptRuleTable rules,
                                bool available, bool linked, bool mismatched, long nowUnix)
        {
            rules ??= AccountPromptRuleTable.Default;

            if (!available || linked || mismatched) return false;
            if (OffersMade(trigger) >= BudgetFor(rules, trigger)) return false;

            // Note the direction test. A clock that has moved backwards — a player changing the
            // device date, or a first launch before network time arrives — leaves a stamp in
            // the future, and a plain subtraction would then be negative, read as "inside the
            // quiet period", and suppress every prompt for the life of the installation.
            // Treating a future stamp as expired heals it: the next offer writes `now` over it,
            // which is smaller.
            if (_lastOfferedUnix > 0 && nowUnix > _lastOfferedUnix
                && nowUnix - _lastOfferedUnix < rules.QuietSeconds) return false;

            return true;
        }

        /// <summary>
        /// Records that the panel was shown: spends this trigger's budget and restarts the
        /// shared quiet period.
        ///
        /// <para>
        /// Called when the panel is <b>raised</b>, never when it is answered — the rule
        /// <c>SyncScheduler.Started</c> and <c>GrovePublishPolicy</c> both follow, for the same
        /// reason. An ask recorded on the reply is an ask that was not recorded when the player
        /// killed the app, backed out, or took a phone call, and a budget that only decrements
        /// on the happy path is not a budget.
        /// </para>
        /// </summary>
        public void NoteOffered(AccountPromptTrigger trigger, long nowUnix)
        {
            if (trigger == AccountPromptTrigger.Purchase) _purchaseOffers++;
            else _chapterOffers++;

            if (nowUnix > 0) _lastOfferedUnix = nowUnix;
        }

        /// <summary>
        /// Whether a shelf priced in money should carry its standing "not signed in" notice.
        ///
        /// <para>
        /// Deliberately <b>not</b> subject to either budget or the quiet period, because it is
        /// not an interruption: it costs no tap, asks no question and takes nothing away, so
        /// the argument for rationing a modal does not reach it. It is also what lets the modal
        /// be as rare as it is — a player who declines every prompt still sees, every single
        /// time they look at a shelf priced in money, that this purchase is tied to one phone.
        /// </para>
        /// </summary>
        public static bool ShouldWarn(bool available, bool linked, bool mismatched)
            => available && !linked && !mismatched;
    }
}
