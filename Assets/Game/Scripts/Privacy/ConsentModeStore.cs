using System;
using UnityEngine;

namespace GlimmerGrove.Privacy
{
    /// <summary>
    /// Reads the consent-mode answer the UMP SDK keeps on the device: the four-digit
    /// <c>UMP_consentModeValues</c> local storage key (Google, "Google consent management
    /// solution SDKs"), parsed by <see cref="ConsentModeValues.Parse"/>.
    ///
    /// <para>
    /// <b>Where it lives is the platform's, and Google's samples say which.</b> Android: the
    /// app's default <c>SharedPreferences</c>, the file named after the package with
    /// <c>_preferences</c> appended - opened by that name rather than through the deprecated
    /// <c>PreferenceManager</c>. iOS: <c>[NSUserDefaults standardUserDefaults]</c>, which is
    /// the store Unity's <c>PlayerPrefs</c> reads on iOS, so no plugin is needed. Anywhere
    /// else - the Editor - there is no UMP, and the answer is "not ready".
    /// </para>
    /// <para>
    /// <b>Never throws.</b> A device that cannot answer reads as not ready, which grants
    /// nothing.
    /// </para>
    /// </summary>
    public static class ConsentModeStore
    {
        public const string Key = "UMP_consentModeValues";

        public static ConsentModeValues Read()
        {
            try
            {
                return ConsentModeValues.Parse(Raw());
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Privacy] consent mode values could not be read: {e.Message}");
                return ConsentModeValues.NotReady;
            }
        }

        static string Raw()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                if (activity == null) return null;

                string file = activity.Call<string>("getPackageName") + "_preferences";
                const int Private = 0;

                using (var prefs = activity.Call<AndroidJavaObject>("getSharedPreferences", file, Private))
                    return prefs?.Call<string>("getString", Key, null);
            }
#elif UNITY_IOS && !UNITY_EDITOR
            return PlayerPrefs.HasKey(Key) ? PlayerPrefs.GetString(Key, null) : null;
#else
            return null;
#endif
        }
    }
}
