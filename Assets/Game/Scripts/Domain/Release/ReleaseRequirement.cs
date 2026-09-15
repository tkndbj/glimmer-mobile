using GlimmerGrove.Privacy;

namespace GlimmerGrove.Release
{
    /// <summary>
    /// What the deployment currently requires of a client on one platform: the oldest build
    /// still allowed to run, and where to get a newer one.
    ///
    /// <para>
    /// <b>The two fields travel together and that is the whole safety property.</b> A minimum
    /// is a wall and a store link is the door through it; a wall with no door is an install
    /// that can never be made to satisfy the rule, on a device whose owner has paid money into
    /// this game. <see cref="IsEnforceable"/> is what every reader asks, and
    /// <see cref="ReleaseGate"/> will neither apply nor <em>cache</em> a requirement that is not
    /// — so a mis-seeded document is inert rather than fatal.
    /// </para>
    /// <para>
    /// <b>Per platform, because the two stores do not ship on the same day.</b> Apple review
    /// takes days the Play rollout does not, so the ordinary state of a release is one store
    /// live and one pending. A single shared minimum would wall every iOS player out of the
    /// game and point them at a build the App Store has not published yet — the worst outcome
    /// this feature can produce, reachable by doing the normal thing.
    /// </para>
    /// <para>
    /// <b>A minimum of nought is the ordinary state</b>, not an absence: it means nothing is
    /// required, which is what the overwhelming majority of releases publish and what an
    /// unseeded project answers. "Nothing is required" and "we have not heard from the server"
    /// are told apart by the <c>CloudResult</c> of the read that produced this, never by a
    /// sentinel in here — see <see cref="ReleaseGate.Apply"/>, where that distinction is the
    /// difference between clearing a wall and keeping one.
    /// </para>
    /// </summary>
    public readonly struct ReleaseRequirement
    {
        /// <summary>Nothing required and nowhere to go. What an absent document means.</summary>
        public static readonly ReleaseRequirement None = new ReleaseRequirement(0, null);

        /// <summary>
        /// The oldest build allowed to run, in <see cref="AppVersion"/>'s integer. Nought means
        /// no forced update.
        /// </summary>
        public readonly int MinimumBuild;

        /// <summary>Where a player goes to satisfy it. Empty when the document named none.</summary>
        public readonly string StoreUrl;

        public ReleaseRequirement(int minimumBuild, string storeUrl)
        {
            MinimumBuild = minimumBuild < 0 ? 0 : minimumBuild;
            StoreUrl = storeUrl ?? string.Empty;
        }

        /// <summary>Whether this requirement walls anybody out at all.</summary>
        public bool Demands => MinimumBuild > 0;

        /// <summary>
        /// Whether the door is one the platform will actually open.
        ///
        /// <see cref="LegalLinks.Usable"/> rather than a second URL check: it is the predicate
        /// this game already uses before handing anything to <c>Application.OpenURL</c>, it is
        /// already pinned by a fixture, and its <c>https</c> requirement is the right one here —
        /// both stores claim their own https links on device through app links, and a device
        /// where that claim fails falls back to a web page with the store button on it, where
        /// a <c>market://</c> or <c>itms-apps://</c> link falls back to nothing at all.
        /// </summary>
        public bool HasDoor => LegalLinks.Usable(StoreUrl);

        /// <summary>
        /// Whether this may be acted on: it asks for something, and it says where to get it.
        ///
        /// A requirement that demands a newer build without naming a store is treated exactly
        /// as one that demands nothing — refused at the door rather than half-applied, because
        /// the half that would survive is the wall.
        /// </summary>
        public bool IsEnforceable => Demands && HasDoor;

        /// <summary>Whether <paramref name="build"/> is walled out by this.</summary>
        /// <remarks>
        /// A build of nought is never walled out: it is <see cref="AppVersion.Running"/>'s way
        /// of saying it does not know what this install is, and comparing against an unknown
        /// would brick every device rather than gate the old ones.
        /// </remarks>
        public bool Shuts(int build) => IsEnforceable && build > 0 && build < MinimumBuild;
    }
}
