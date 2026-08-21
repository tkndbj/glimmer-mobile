using System.Collections.Generic;

namespace GlimmerGrove.Ads
{
    /// <summary>
    /// The mediation dashboard's own identifiers: the app key, and one ad unit per
    /// placement.
    ///
    /// <para>
    /// Deliberately separate from <see cref="AdRewardTable"/>, which is content. These are
    /// not tuning — they are the address of the account the money arrives in, they differ
    /// per platform, and they change roughly never. Putting them in <c>progression.json</c>
    /// would mean a content push could redirect this game's ad revenue to somebody else's
    /// account, which is a considerably worse failure than a mistuned payout.
    /// </para>
    /// <para>
    /// These are live. <see cref="IsConfigured"/> still reads the placeholder state and
    /// <c>Boot</c> still keeps the null provider when it finds one, so a fork of this
    /// project with the identifiers stripped ships dark rather than shipping a button that
    /// cannot work.
    /// </para>
    /// <para>
    /// <b>An ad unit id belongs to one app, and nothing tells you when you have crossed
    /// them.</b> The dashboard lists ad units per app, so a unit created while the iOS app
    /// is selected cannot be loaded by an Android build however correctly it is pasted in
    /// here. The SDK does not error on it — it reports no fill, exactly as it does for a
    /// market with no demand, so the offer sits at "finding a video" for ever and the only
    /// visible symptom is a button that never lights. That shipped here: <c>run_continue</c>
    /// and <c>win_bonus</c> were both created under the iOS app and both were dead on
    /// Android for a day, while <c>heart_refill</c> and <c>coin_bonus</c> — created earlier,
    /// under the right app — worked throughout and made it look like a code fault. When a
    /// single placement never fills, check which app owns its ad unit before anything else.
    /// </para>
    /// </summary>
    public static class AdConfig
    {
        /// <summary>What an unfilled identifier reads as. Never a valid key.</summary>
        public const string Unset = "UNSET";

        /// <summary>
        /// The LevelPlay app key, from the dashboard's Apps page.
        ///
        /// <para>
        /// <b>One per platform, not one per game.</b> LevelPlay registers an Android app and
        /// an iOS app separately — they may sit in the same project, but each gets its own
        /// key. Sharing one across both is the same trap as sharing an ad unit id: nothing
        /// errors, the SDK simply never fills, and it reads as a market with no demand
        /// rather than as a misconfiguration.
        /// </para>
        /// </summary>
        public const string AndroidAppKey = "27a0d017d";
        public const string IosAppKey = "27a0dae15";

        /// <summary>The app key for the platform this build is for.</summary>
        public static string AppKey
        {
            get
            {
#if UNITY_IOS
                return Real(IosAppKey);
#else
                return Real(AndroidAppKey);
#endif
            }
        }

        /// <summary>
        /// Ad unit ids, per placement, from Dashboard ▸ Setup ▸ Ad Units.
        ///
        /// <para>
        /// These are <em>per platform</em>. Android and iOS get different ids for the same
        /// placement, which is the trap: a build that reuses one platform's id on the other
        /// does not error, it simply never fills, and it looks exactly like a market with no
        /// demand. The lookup below picks by platform for that reason and not for tidiness.
        /// </para>
        /// </summary>
        public const string AndroidHeartRefill = "y58wb3ylhgpti287";
        public const string AndroidCoinBonus = "tqzvj9fd70ec87qi";
        public const string AndroidRunContinue = "mbhc7pqqhnp5znd8";
        public const string AndroidWinBonus = "ezx904bb5q89hse4";
        public const string AndroidHintRefill = "1lt4rtqzgvn9zgsf";

        public const string IosHeartRefill = "xwe669e56bzrs6wb";
        public const string IosCoinBonus = "h8rmyy4ol8scwpmy";
        public const string IosRunContinue = "fh2djyoesd3akw86";
        public const string IosWinBonus = "1ijvtsidp55nfekg";
        public const string IosHintRefill = "1b3el4i4h7ga2sl9";

        /// <summary>
        /// Whether real identifiers have been filled in.
        ///
        /// The app key alone is checked: without it nothing initialises at all, so a missing
        /// ad unit is a per-placement problem that resolves as "this offer is never ready",
        /// which the UI already says honestly. Refusing to start over one absent unit would
        /// turn a half-configured dashboard into no ads at all.
        ///
        /// <para>
        /// Because the key is resolved per platform, this is false on an iOS build until the
        /// iOS app is registered — so that build ships with ads dark rather than starting a
        /// mediation SDK against Android's key, which would never fill and would look like a
        /// dead market rather than a missing line of configuration.
        /// </para>
        /// </summary>
        public static bool IsConfigured => !string.IsNullOrEmpty(AppKey);

        /// <summary>
        /// The ad unit for each placement on the platform this build is for.
        ///
        /// Built by <c>Boot</c> and handed to the provider, so the provider never has to
        /// know which platform it is on — one more thing kept out of the SDK-facing half.
        /// </summary>
        public static Dictionary<string, string> AdUnits()
        {
#if UNITY_IOS
            return new Dictionary<string, string>
            {
                { AdPlacement.HeartRefill, Real(IosHeartRefill) },
                { AdPlacement.CoinBonus, Real(IosCoinBonus) },
                { AdPlacement.RunContinue, Real(IosRunContinue) },
                { AdPlacement.WinBonus, Real(IosWinBonus) },
                { AdPlacement.HintRefill, Real(IosHintRefill) },
            };
#else
            return new Dictionary<string, string>
            {
                { AdPlacement.HeartRefill, Real(AndroidHeartRefill) },
                { AdPlacement.CoinBonus, Real(AndroidCoinBonus) },
                { AdPlacement.RunContinue, Real(AndroidRunContinue) },
                { AdPlacement.WinBonus, Real(AndroidWinBonus) },
                { AdPlacement.HintRefill, Real(AndroidHintRefill) },
            };
#endif
        }

        /// <summary>Turns a placeholder into an empty string, which the provider skips.</summary>
        static string Real(string value)
            => string.IsNullOrEmpty(value) || value == Unset ? string.Empty : value;
    }
}
