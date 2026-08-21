using GlimmerGrove.Cloud;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The one place that decides to raise <see cref="AccountOverlay"/> without being asked,
    /// and the only place that remembers having done so.
    ///
    /// <para>
    /// <b>The rule is not here.</b> It is <see cref="AccountPromptPolicy"/>, in Domain, holding
    /// no clock and no storage, because every situation it is about — a live session, a real
    /// purchase, a device that has been away two days — is one the Editor never reaches, and a
    /// rule that can only be checked by playing the shipped game is a rule nobody checks. This
    /// class is the half that cannot be tested offline and therefore contains nothing worth
    /// testing: three keys, a clock reading, and a call.
    /// </para>
    /// <para>
    /// <b>The counts live in <c>PlayerPrefs</c> and must never move into the save.</b> What a
    /// device has shown a person is a fact about the installation, exactly like
    /// <c>RunGuard</c>'s marker and <c>GrovePublishPolicy</c>'s fingerprint. Merged across
    /// devices it would arrive on a new phone as a reason to stay quiet — which is precisely
    /// backwards, since a second device is a player with more to lose, not less — and it would
    /// survive a progress wipe as a reason never to ask again.
    /// </para>
    /// </summary>
    public static class AccountPrompts
    {
        // The shipped key, kept exactly as it was. Renaming it would hand every live
        // installation a fresh allowance and ask two more times somebody who has already
        // declined twice — the one outcome this budget exists to prevent.
        const string ChapterKey = "account_prompt_count";
        const string PurchaseKey = "account_prompt_purchases";

        // A string rather than an int, because a unix second passes int.MaxValue in 2038 and
        // PlayerPrefs has no long. Cheap to get right once; impossible to notice going wrong.
        const string LastOfferedKey = "account_prompt_last_unix";

        static readonly AccountPromptPolicy Policy = new AccountPromptPolicy();
        static bool _loaded;

        static void Load()
        {
            if (_loaded) return;
            _loaded = true;

            long last = 0L;
            long.TryParse(PlayerPrefs.GetString(LastOfferedKey, string.Empty), out last);

            Policy.Adopt(PlayerPrefs.GetInt(ChapterKey, 0),
                         PlayerPrefs.GetInt(PurchaseKey, 0),
                         last);
        }

        /// <summary>
        /// Whether a shelf priced in real money should carry its standing "not signed in"
        /// notice. Never rationed — see <see cref="AccountPromptPolicy.ShouldWarn"/>.
        /// </summary>
        public static bool ShouldWarn
            => AccountPromptPolicy.ShouldWarn(CloudSaveService.IsAvailable,
                                              CloudSaveService.IsLinked,
                                              CloudSaveService.AccountMismatched);

        /// <summary>
        /// Whether this trigger may raise the panel. Asked separately from
        /// <see cref="Offer"/> by callers that have to lay a button out before they know
        /// whether tapping it will lead somewhere else.
        /// </summary>
        public static bool ShouldOffer(AccountPromptTrigger trigger)
        {
            Load();
            return Policy.ShouldOffer(trigger, CloudSaveService.IsAvailable,
                                      CloudSaveService.IsLinked,
                                      CloudSaveService.AccountMismatched,
                                      GameClock.NowUnix());
        }

        /// <summary>
        /// Raises the panel if this trigger may, and reports whether it did.
        ///
        /// <para>
        /// The state is written through immediately rather than at the next save. This is one
        /// of the two records in the game whose loss is invisible — nothing shows a player how
        /// many times they have been nudged — so the only way it can be wrong is by being asked
        /// again, and the process most likely to die between the ask and a deferred write is
        /// the one that has just been backgrounded by an OAuth consent screen.
        /// </para>
        /// </summary>
        public static bool Offer(AccountPromptTrigger trigger)
        {
            if (!ShouldOffer(trigger)) return false;

            Policy.NoteOffered(trigger, GameClock.NowUnix());

            PlayerPrefs.SetInt(ChapterKey, Policy.ChapterOffers);
            PlayerPrefs.SetInt(PurchaseKey, Policy.PurchaseOffers);
            PlayerPrefs.SetString(LastOfferedKey, Policy.LastOfferedUnix.ToString());
            PlayerPrefs.Save();

            Flow.Modal<AccountOverlay>(v => v.Reason = trigger);
            return true;
        }
    }
}
