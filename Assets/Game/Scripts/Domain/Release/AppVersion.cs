using UnityEngine;

namespace GlimmerGrove.Release
{
    /// <summary>
    /// This build's version, as one comparable integer.
    ///
    /// <para>
    /// <b>There is one build number in this game and this is it.</b> Two things compare against
    /// it — a chapter's <c>minAppVersion</c>, which hides content a shipped client cannot draw,
    /// and <see cref="ReleaseGate"/>, which stops an install that is too old to run at all — and
    /// a second copy of the arithmetic would let those two disagree about what version a device
    /// is. The parse used to live as a private helper on <c>Boot</c>, where neither the gate nor
    /// a test could reach it.
    /// </para>
    /// <para>
    /// <b>Two answers, deliberately, because the two callers want opposite defaults.</b>
    /// <see cref="Parse"/> floors an unreadable string at 1, which is the forgiving direction for
    /// content: the worst outcome is a chapter being shown that could have been hidden.
    /// <see cref="Running"/> answers <b>0</b> for the same string, which is the forgiving
    /// direction for the gate: a device whose own version cannot be read must never be walled
    /// out of the game, because there is no build it could install to satisfy a comparison that
    /// is broken on both sides. Both go through <see cref="TryParse"/>, so there is still only
    /// one parser.
    /// </para>
    /// </summary>
    public static class AppVersion
    {
        /// <summary>
        /// The most a minor or patch segment may be before it collides with the segment above.
        ///
        /// <para>
        /// The scheme is <c>major x 10000 + minor x 100 + patch</c>, so <c>1.100.0</c> and
        /// <c>2.0.0</c> are the same integer. Nobody ships a hundredth patch, but a version
        /// that silently sorts as a <em>different release</em> is exactly the class of fault
        /// this project refuses to leave to luck — and the direction it fails in is the bad
        /// one, since a build that reads as newer than it is walks straight through the gate.
        /// Refused rather than clamped: a clamp would make two real versions equal, which is
        /// the same bug wearing a check's clothes.
        /// </para>
        /// </summary>
        public const int SegmentCeiling = 99;

        /// <summary>
        /// Turns "1.4.2" into 10402. False when the string is not a version at all — empty, or
        /// with a major segment that is not a number, or with a segment out of range.
        ///
        /// <para>
        /// A missing minor or patch reads as nought, so "1" and "1.0.0" are the same build, and
        /// a fourth segment is ignored. <b>A segment that is not a plain number is refused
        /// rather than salvaged</b> — "1.4.2-beta" has no answer here, and inventing one would
        /// be this file guessing at a convention nobody has written down. The refusal is loud
        /// (see <see cref="Running"/>) because the thing it disables is a safety gate, and a
        /// gate that cannot fail is not a gate.
        /// </para>
        /// </summary>
        public static bool TryParse(string version, out int build)
        {
            build = 0;
            if (string.IsNullOrEmpty(version)) return false;

            var parts = version.Split('.');

            if (!int.TryParse(parts[0], out int major) || major < 0) return false;

            int minor = Segment(parts, 1);
            int patch = Segment(parts, 2);
            if (minor < 0 || patch < 0) return false;
            if (minor > SegmentCeiling || patch > SegmentCeiling) return false;

            build = major * 10000 + minor * 100 + patch;
            return true;
        }

        /// <summary>
        /// The same, floored at 1 for anything unreadable.
        ///
        /// For content's <c>minAppVersion</c>, whose contract this preserves exactly: a level or
        /// chapter needing a client newer than this one is simply not shown.
        /// </summary>
        public static int Parse(string version) => TryParse(version, out int build) ? build : 1;

        /// <summary>
        /// The build this process is, or <b>0</b> when <see cref="Application.version"/> cannot
        /// be read as one.
        ///
        /// <para>
        /// Sampled once. <see cref="Application.version"/> is baked into the player at build
        /// time and cannot change while the process is alive, so re-reading it would be a
        /// native call on a clock for an answer that is a constant.
        /// </para>
        /// <para>
        /// <b>Nought is reported as an error, once.</b> Everything that compares against this
        /// treats nought as "do not gate this device", which is the only safe answer and is
        /// also completely silent on a phone — the forced-update wall would simply never
        /// appear, on every install, and every gate in this repository would stay green. The
        /// log line is the one thing that can say so.
        /// </para>
        /// </summary>
        public static int Running
        {
            get
            {
                if (_running.HasValue) return _running.Value;

                if (TryParse(Application.version, out int build))
                {
                    _running = build;
                }
                else
                {
                    _running = 0;
                    Debug.LogError(
                        $"[Release] Application.version '{Application.version}' is not a " +
                        "major.minor.patch number, so this build cannot be compared against a " +
                        "published one — the update gate is inert for this install. Fix " +
                        "bundleVersion in Player Settings.");
                }

                return _running.Value;
            }
        }

        static int? _running;

        static int Segment(string[] parts, int index)
        {
            if (parts.Length <= index) return 0;
            return int.TryParse(parts[index], out int value) ? value : -1;
        }
    }
}
