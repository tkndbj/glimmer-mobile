namespace GlimmerGrove.Social
{
    /// <summary>
    /// What a player is reporting about another keeper.
    ///
    /// <para>
    /// <b>Two, because a keeper puts two things in front of strangers.</b> A <b>name</b>, which
    /// is the one piece of free text in this game, and a <b>grovement</b> — 784 tiles of ground
    /// sold with walls, gates and fences. <c>GroveCard</c>'s own remarks used to say an
    /// arrangement could not be offensive, because every piece is an id from a catalog we ship;
    /// that was true of the handful of pre-placed dots the grove began as and stopped being true
    /// the day land was sold by the region and walls by the bundle (invariants 16b and 16e).
    /// Walls tile. Somebody with enough of them writes whatever they like on the ground, and
    /// every gate in this project stays green while they do, because nothing here ever opens a
    /// picture (invariant 32b, on somebody else's screen).
    /// </para>
    /// <para>
    /// <b>The ordinals reach analytics and the names reach the wire</b>, so neither may be
    /// renumbered or respelled — a report is recorded against a collection named after the
    /// subject, and changing the string orphans every report filed under the old one and resets
    /// a threshold somebody had already reached. <see cref="ReportSubjects.Wire"/> is the one
    /// place the spelling lives.
    /// </para>
    /// </summary>
    public enum ReportSubject
    {
        /// <summary>The keeper's published name. The subject that shipped first.</summary>
        Name = 0,

        /// <summary>What they have built and arranged, as a visitor sees it.</summary>
        Grove = 1,
    }

    /// <summary>
    /// The wire spelling of a <see cref="ReportSubject"/>, and the only place it is written.
    ///
    /// A method rather than a <c>ToString().ToLower()</c>, because the server keys a collection
    /// on this string: a rename that looked like a tidy-up would file every later report into a
    /// collection nothing reads, silently, with a green build and a green suite.
    /// </summary>
    public static class ReportSubjects
    {
        /// <summary>Every subject this build can report, in the order a panel offers them.</summary>
        public static readonly ReportSubject[] All = { ReportSubject.Name, ReportSubject.Grove };

        /// <summary>What the server calls this subject. Permanent, for invariant 1's reason.</summary>
        public static string Wire(ReportSubject subject)
            => subject == ReportSubject.Grove ? "grove" : "name";

        /// <summary>
        /// The loc key for the button that reports this subject.
        ///
        /// Derived from the member rather than written out at the call site, so a third subject
        /// is one enum member, one string and one row in the table — <c>KeeperTitle.KeyFor</c>'s
        /// shape, and the reason nothing here concatenates a key from anything a player typed.
        ///
        /// <b>One string, not two.</b> Each subject used to carry an explanatory note under its
        /// key as well; the panel says what a report is once, above both, and the captions say
        /// what they are. See <c>ReportOverlay</c>.
        /// </summary>
        public static string ButtonKey(ReportSubject subject)
            => subject == ReportSubject.Grove ? "ui.report.grove" : "ui.report.name";
    }
}
