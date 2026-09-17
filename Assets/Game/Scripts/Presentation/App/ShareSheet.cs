using UnityEngine;

#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace GlimmerGrove
{
    /// <summary>
    /// The operating system's share sheet, handed one sentence.
    ///
    /// <para>
    /// <b>The sheet, never the address book.</b> A referral that reads contacts is the one
    /// shape of the feature that costs a permission, a data-safety declaration and a review
    /// question on both stores, and invite spam is a classic pull. The share sheet needs no
    /// permission and tells this game nothing about who the sentence went to — which is
    /// exactly the amount this game wants to know.
    /// </para>
    /// <para>
    /// Android is an <c>ACTION_SEND</c> chooser built through the JNI bridge, so it costs no
    /// plugin; iOS is <c>UIActivityViewController</c> in <c>GlimmerShare.mm</c>, bound the way
    /// the tracking prompt and the sign-in sheets are. The Editor copies to the clipboard,
    /// which is the honest stand-in: the thing shared is a string.
    /// </para>
    /// </summary>
    public static class ShareSheet
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void GlimmerShare(string text);
#endif

        /// <summary>Whether a sheet exists on this platform. The Editor answers false and copies instead.</summary>
        public static bool IsSupported
        {
            get
            {
#if UNITY_EDITOR
                return false;
#elif UNITY_ANDROID || UNITY_IOS
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Opens the sheet with <paramref name="text"/>. <paramref name="title"/> is the
        /// chooser's heading on Android and ignored on iOS, whose sheet has none. Answers
        /// whether a sheet was opened; false means the text was copied instead.
        /// </summary>
        public static bool Share(string text, string title)
        {
            if (string.IsNullOrEmpty(text)) return false;

#if UNITY_EDITOR
            GUIUtility.systemCopyBuffer = text;
            Debug.Log("[Share] (editor) copied to the clipboard: " + text);
            return false;
#elif UNITY_ANDROID
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intentClass = new AndroidJavaClass("android.content.Intent"))
                using (var intent = new AndroidJavaObject("android.content.Intent"))
                {
                    intent.Call<AndroidJavaObject>("setAction", intentClass.GetStatic<string>("ACTION_SEND"));
                    intent.Call<AndroidJavaObject>("setType", "text/plain");
                    intent.Call<AndroidJavaObject>("putExtra", intentClass.GetStatic<string>("EXTRA_TEXT"), text);

                    var chooser = intentClass.CallStatic<AndroidJavaObject>("createChooser", intent, title ?? string.Empty);
                    activity.Call("startActivity", chooser);
                }
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Share] the Android chooser could not be opened: " + e.Message);
                GUIUtility.systemCopyBuffer = text;
                return false;
            }
#elif UNITY_IOS
            GlimmerShare(text);
            return true;
#else
            GUIUtility.systemCopyBuffer = text;
            return false;
#endif
        }

        /// <summary>Puts the text on the clipboard. Every platform, no sheet.</summary>
        public static void Copy(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            GUIUtility.systemCopyBuffer = text;
        }
    }
}
