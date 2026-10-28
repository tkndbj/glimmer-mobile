#if GLIMMER_CRASHLYTICS && GLIMMER_ANALYTICS && GLIMMER_FIREBASE
using System;
using System.Globalization;
using Firebase;
using Firebase.Crashlytics;
using GlimmerGrove.Privacy;
using UnityEngine;

namespace GlimmerGrove.Analytics
{
    /// <summary>
    /// Crash reporting, on the analytics answer (<see cref="CrashConsent"/>, invariant 55b).
    ///
    /// <para>
    /// <b>Started with the rest of Firebase</b>: asked only from the Firebase half's consent push,
    /// which runs once the shared dependency check has passed and again on every change. The
    /// manifest and the plist start collection off, so nothing is sent before the answer.
    /// </para>
    /// <para>
    /// <b>Tagged with the build.</b> Crashlytics stamps every report with the app's version name and
    /// code itself; the <c>build</c> and <c>version</c> keys repeat them as this game reads them
    /// (<c>Release.AppVersion.Running</c>, the figure <c>boot_ready</c> and the save's
    /// <c>BuildStamp</c> carry), so a crash can be joined to analytics without translating.
    /// </para>
    /// <para>
    /// Unhandled C# exceptions stay non-fatal (the SDK's default): Unity logs them and the game
    /// carries on, and counting them as crashes would make the crash-free figure meaningless.
    /// </para>
    /// </summary>
    public static partial class AnalyticsSetup
    {
        static bool _crashKeyed;
        static bool? _crashesOn;

        static partial void CrashesConsent(AnalyticsConsent consent)
        {
            bool on = CrashConsent.Allows(consent);
            if (_crashesOn == on) return;

            try
            {
                if (!_crashKeyed)
                {
                    // Crashlytics registers itself with the default app; touching it is harmless
                    // when Auth or Firestore created it already.
                    _ = FirebaseApp.DefaultInstance;
                    Crashlytics.SetCustomKey("build",
                        Release.AppVersion.Running.ToString(CultureInfo.InvariantCulture));
                    Crashlytics.SetCustomKey("version", Application.version);
                    _crashKeyed = true;
                }

                Crashlytics.IsCrashlyticsCollectionEnabled = on;
                _crashesOn = on;
                Debug.Log($"[Crashes] reporting {(on ? "on" : "off")}");
            }
            catch (Exception)
            {
                // No exception message, for the analytics half's reason. Left as the manifest set it.
                Debug.LogWarning("[Crashes] collection could not be switched; the default (off) stands");
            }
        }
    }
}
#endif
