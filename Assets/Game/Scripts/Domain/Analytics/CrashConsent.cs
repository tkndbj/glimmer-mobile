using GlimmerGrove.Privacy;

namespace GlimmerGrove.Analytics
{
    /// <summary>
    /// Whether crash reports may leave the device (Crashlytics, <c>AnalyticsSetup.Crashlytics</c>).
    ///
    /// <para>
    /// <b>The analytics answer, exactly</b> (invariant 55b): a crash report carries an installation
    /// id and device facts, which is what <c>analytics_storage</c> governs, so reporting is on only
    /// where that purpose is granted and the install collects at all (not child-directed). Before
    /// the answer - a first launch, an owed form, a UMP that timed out - it is off, as the manifest
    /// and the plist start it. A withdrawal switches it off again; the native switch persists
    /// across launches, as <c>setConsent</c> does.
    /// </para>
    /// </summary>
    public static class CrashConsent
    {
        public static bool Allows(AnalyticsConsent consent) => consent.Collect && consent.AnalyticsStorage;
    }
}
