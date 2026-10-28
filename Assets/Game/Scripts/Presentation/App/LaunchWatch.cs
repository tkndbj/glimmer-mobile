using System;
using GlimmerGrove.Analytics;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// Hears the screens open and reports the launch's two milestones (<see cref="BootAnalytics"/>):
    /// <c>boot_ready</c> when the first screen after the splash can be tapped, and
    /// <c>tutorial_started</c> the first time this install shows the tutorial (<c>tutorial_resumed</c> on
    /// a later launch that reopens it unfinished).
    ///
    /// <para>
    /// <b>"Can be tapped" is <see cref="Flow.Presented"/></b>: the iris has opened on a screen that
    /// said it was <c>Ready</c>, which for the tutorial is its board built. The splash is skipped -
    /// it is the wait being measured, not the end of it.
    /// </para>
    /// <para>
    /// <b>The first launch is device-local</b> (<see cref="LaunchedKey"/>), which is what "first
    /// launch of this install" means: a reinstall is a first launch again, as it is a new
    /// <c>first_open</c>. Marked once <c>boot_ready</c> has been sent, not at boot, so a first launch
    /// that dies on the splash is still reported as first by the launch that gets through.
    /// </para>
    /// </summary>
    public static class LaunchWatch
    {
        /// <summary>Set the first time this install sends <c>boot_ready</c>. Never in the save.</summary>
        const string LaunchedKey = "glimmer.boot.launched";

        /// <summary>Set the first time this install sends <c>tutorial_started</c>. Never in the save.</summary>
        const string TutorialKey = "glimmer.tutorial.started";

        static bool _attached;

        public static void Attach()
        {
            if (_attached) return;
            _attached = true;
            Flow.Presented += Heard;
        }

        static void Heard(View screen)
        {
            if (screen == null || screen is SplashScreen) return;

            if (screen is TutorialScreen) NoteTutorial();

            long ms = SinceStart(out string clock);
            bool first = PlayerPrefs.GetInt(LaunchedKey, 0) == 0;

            if (!BootAnalytics.NoteReady(ms, clock, first, Release.AppVersion.Running)) return;

            if (first)
            {
                PlayerPrefs.SetInt(LaunchedKey, 1);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// Once per install, then <c>tutorial_resumed</c>. The flag is written after the event is in
        /// the delivery queue, once per install: one small synchronous write, never per frame.
        /// </summary>
        static void NoteTutorial()
        {
            bool before = PlayerPrefs.GetInt(TutorialKey, 0) != 0;
            if (!BootAnalytics.NoteTutorial(before) || before) return;

            PlayerPrefs.SetInt(TutorialKey, 1);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Milliseconds since this process started. Android says so exactly
        /// (<c>Process.getStartElapsedRealtime</c>, API 24, against the same monotonic clock); iOS and
        /// the Editor have no managed way to ask, so they answer from engine start, which leaves out
        /// the OS's own few hundred milliseconds before Unity runs - reported as <c>engine</c>.
        /// </summary>
        static long SinceStart(out string clock)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var process = new AndroidJavaClass("android.os.Process"))
                using (var system = new AndroidJavaClass("android.os.SystemClock"))
                {
                    long started = process.CallStatic<long>("getStartElapsedRealtime");
                    long now = system.CallStatic<long>("elapsedRealtime");
                    if (started > 0L && now >= started)
                    {
                        clock = "process";
                        return now - started;
                    }
                }
            }
            catch (Exception)
            {
                // Falls through to the engine clock; a lost precision, never a lost event.
            }
#endif
            clock = "engine";
            return (long)(Time.realtimeSinceStartupAsDouble * 1000d);
        }
    }
}
