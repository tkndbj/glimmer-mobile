using System.Collections.Generic;

namespace GlimmerGrove.Privacy
{
    /// <summary>
    /// Lets a developer see the consent form from outside the EEA.
    ///
    /// <para>
    /// <b>Why this has to exist.</b> The form is shown only to players UMP places in the EEA
    /// or the UK, judged from the device's own network. Everywhere else it correctly shows
    /// nothing — which is indistinguishable, from the developer's chair, from an integration
    /// that is broken. Without a way to force the geography, the one feature in this game
    /// whose entire purpose is regulatory compliance would ship having never been seen
    /// working, and the first people to test it would be European players.
    /// </para>
    /// <para>
    /// <b>It cannot reach a player.</b> Everything here is compiled out unless
    /// <c>UNITY_EDITOR</c> or <c>DEVELOPMENT_BUILD</c> is defined, so a release build has no
    /// debug settings to attach and no list of device ids in it. That is a stronger guarantee
    /// than a runtime flag: a flag can be left on, and this cannot be, because in a store
    /// build the code is not there.
    /// </para>
    /// <para>
    /// <b>Using it.</b> Run a development build once with <see cref="Devices"/> empty. UMP
    /// logs a line naming the hashed id of the device it did not recognise — search the log
    /// for <c>gad_has_consent_for_cookies</c> or the SDK's "Use ConsentDebugSettings" warning,
    /// which prints it verbatim. Paste that id below, rebuild, and the form appears as it
    /// would in Frankfurt. The id is a salted hash of the advertising identifier, so it is not
    /// a secret and it identifies a device rather than a person — but it changes when the
    /// advertising id is reset.
    /// </para>
    /// </summary>
    public static class ConsentDebug
    {
        /// <summary>
        /// Hashed ids of the devices that should be treated as test devices.
        ///
        /// Empty means "no test devices", which is the right resting state: the first run on a
        /// new phone is what tells you its id, and leaving somebody else's here would quietly
        /// stop being true the next time their advertising id was reset.
        /// </summary>
        public static readonly IReadOnlyList<string> Devices = new string[]
        {
            // The Galaxy S25 the game is tested on. UMP printed this itself, in logcat, on the
            // first run that did not list it — search for "addTestDeviceHashedId". It changes
            // when the device's advertising id is reset.
            // The Galaxy S25 the game is tested on. UMP prints this itself, in logcat, on any
            // run where no debug list is supplied — search for "addTestDeviceHashedId". It
            // changes when the device's advertising id is reset, and UMP goes silent once any
            // list is given, so re-reading it means emptying this for one run.
            "B0CD5E3D523D6F09BB69C756CCC73A86",
        };

        /// <summary>
        /// Whether a forced geography should be applied at all.
        ///
        /// False by default even in a development build, so a developer inside the EEA sees
        /// exactly what their neighbours see rather than a simulation of it. Turn it on
        /// deliberately, and only while testing the form.
        /// </summary>
        public const bool ForceEea =
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            true;
#else
            false;
#endif

        /// <summary>
        /// Whether to clear UMP's stored consent state before each debug run.
        ///
        /// <para>
        /// <b>Without this the forced geography appears not to work at all</b>, which cost an
        /// evening. UMP caches its decision on the device: the first launch — before any debug
        /// settings existed — evaluated a Turkish network, stored <c>NotRequired</c>, and every
        /// later run was served that cached answer no matter what geography was forced. The
        /// symptom is indistinguishable from a wrong device id or an unpublished message, and
        /// no log says "this came from cache".
        /// </para>
        /// <para>
        /// Only ever true alongside <see cref="IsActive"/>, so it cannot reach a player — and
        /// it must not, because resetting a real player's consent would silently re-prompt
        /// somebody who had already answered.
        /// </para>
        /// </summary>
        public const bool ResetEachRun = true;

        /// <summary>Whether anything here should be applied to a consent request.</summary>
        public static bool IsActive
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return ForceEea && Devices.Count > 0;
#else
                return false;
#endif
            }
        }
    }
}
