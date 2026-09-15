using GlimmerGrove.Analytics;
using GlimmerGrove.Persistence;
using UnityEngine;

namespace GlimmerGrove.Release
{
    /// <summary>
    /// Whether this install is still allowed to be played, and where to go if it is not.
    ///
    /// <para>
    /// <b>The requirement is remembered on the device, so the wall is not a network state.</b>
    /// A gate that lived only in memory would be dismissed by the one gesture every player
    /// already knows — force-quit and reopen, in flight mode if they like — because the read
    /// that raised it would simply fail on the way back. One integer and one link are written
    /// device-locally the moment the server names them, so a cold start with no signal enforces
    /// exactly what the last answered launch was told. That is the whole of what makes this
    /// unavoidable rather than merely usual.
    /// </para>
    /// <para>
    /// <b>And the server's answer governs, which is what makes it reversible.</b> A ratchet
    /// that only ever rose would be safer against a player and catastrophic against a typo: one
    /// mis-seeded minimum and the entire installed base is walled out of a game nobody can
    /// unwall, because the document that would say so is one the client has stopped believing.
    /// So a <em>successful</em> read replaces what is held, in both directions, and a failed one
    /// changes nothing. Offline, the last thing the server said applies; online, what it says
    /// now applies. Deleting <c>config/release</c> lifts every wall in the world on the next
    /// check, and that is the intended emergency stop rather than an oversight.
    /// </para>
    /// <para>
    /// <b>It is device-local and must never be in the save.</b> It is a fact about this install
    /// on this platform, not about the player: merged across devices (invariant 11b), an iOS
    /// minimum would follow somebody onto an Android phone that is perfectly current and wall
    /// them out of it, pointing at a store they are not in. The save also merges monotonically,
    /// which is exactly the ratchet the paragraph above refuses.
    /// </para>
    /// <para>
    /// <b>Nothing here stops the game working underneath.</b> The save still loads, the sync
    /// still runs, a purchase still redeems. A blocked client that could not push would strand
    /// whatever the player did in the session before the wall went up, and there is nothing to
    /// protect the server from in the first place — every write this game makes is an
    /// idempotent monotonic join, which is why an old client pushing is safe by construction.
    /// What the wall takes away is <em>input</em>, and only that.
    /// </para>
    /// </summary>
    public static class ReleaseGate
    {
        /// <summary>
        /// Where the held requirement is written.
        ///
        /// <para>
        /// <b>One key holding both halves, on purpose.</b> Two keys are two writes, and a
        /// process killed between them leaves a build number paired with the previous release's
        /// link. Writing "10402 https://..." makes the pair atomic in the only sense that
        /// matters here — there is no state in which half of it landed.
        /// </para>
        /// <para>
        /// Renaming it forgets every wall on every device, which is a real (if slow-acting)
        /// outage: those devices then run unwalled until their next successful read. Written
        /// down so that the rename is a deliberate act.
        /// </para>
        /// </summary>
        public const string PrefsKey = "glimmer_release_wall";

        /// <summary>The event raised once per process when the wall is first drawn.</summary>
        public const string BlockedEvent = "update_required";

        /// <summary>
        /// Whether the wall standing right now was learnt this session or recovered off the
        /// device. Reported with <see cref="BlockedEvent"/>, because the two are very different
        /// stories: the first is a release going out, and the second is the caching that makes
        /// this feature unavoidable actually doing its job.
        /// </summary>
        static bool _fromCache = true;

        static bool _restored;
        static ReleaseRequirement _held;
        static bool _counted;

        /// <summary>Latches the doorless-requirement error to once per process. See <see cref="Apply"/>.</summary>
        static bool _warnedDoorless;

        /// <summary>What this device believes the deployment requires of it.</summary>
        public static ReleaseRequirement Held
        {
            get
            {
                Restore();
                return _held;
            }
        }

        /// <summary>
        /// Whether this build is walled out right now.
        ///
        /// <para>
        /// Cheap enough to ask every frame, which is what the driver does: after the first call
        /// it is an integer comparison against a field. It has to be asked rather than
        /// announced, for the reason <c>Flow.Covered</c> is — the thing that would consume an
        /// announcement is a panel, and the panel is destroyed and rebuilt by every screen
        /// change (see <c>UpdateGate</c>).
        /// </para>
        /// </summary>
        public static bool IsShut => Held.Shuts(AppVersion.Running);

        /// <summary>Where a walled-out player goes. Empty when nothing is being asked.</summary>
        public static string StoreUrl => Held.IsEnforceable ? Held.StoreUrl : string.Empty;

        /// <summary>
        /// Takes the deployment's current word for it.
        ///
        /// <para>
        /// <b>Call this only for a read that actually succeeded.</b> The one thing this class
        /// cannot tell by looking at a <see cref="ReleaseRequirement"/> is whether
        /// <see cref="ReleaseRequirement.None"/> means "the server says nothing is required" or
        /// "nobody answered", and the two demand opposite behaviour: the first must lift a wall
        /// that is standing, the second must leave it exactly where it is. The caller knows,
        /// because it is holding the <c>CloudResult</c> — see <c>CloudSaveService</c>.
        /// </para>
        /// <para>
        /// <b>A requirement with no door is applied as nothing at all</b> rather than stored and
        /// enforced later. Storing it would put a wall on the device that no build and no
        /// subsequent seed could be proved to open from the player's side, which is the one
        /// failure in this feature with no recovery that does not involve a reinstall. See
        /// <see cref="ReleaseRequirement.IsEnforceable"/>.
        /// </para>
        /// <para>
        /// Idempotent and cheap: a requirement identical to the one held writes nothing, which
        /// matters because this is called on a timer and <c>PlayerPrefs.Save</c> serialises the
        /// whole store synchronously (see <see cref="DevicePrefs"/>).
        /// </para>
        /// </summary>
        public static void Apply(ReleaseRequirement requirement)
        {
            Restore();

            var wanted = requirement.IsEnforceable ? requirement : ReleaseRequirement.None;

            _fromCache = false;

            // Before the equality check, and that is not a style choice. A doorless requirement
            // collapses to None, so on the overwhelmingly common device — one that was never
            // walled — it compares equal to what is already held and the method returns. Left
            // below, the one guard that makes this mistake visible would be silent in exactly
            // the case it exists for. Latched, because this runs on a timer.
            if (requirement.Demands && !requirement.HasDoor && !_warnedDoorless)
            {
                _warnedDoorless = true;

                // Loud, because from every other angle this looks like a release nobody forced:
                // the document parses, the gate reports itself open, every content gate stays
                // green, and the seeder's own guard is the only other thing that would have
                // caught it.
                Debug.LogError(
                    $"[Release] the deployment asks for build {requirement.MinimumBuild} but " +
                    "names no usable store link for this platform, so nothing is being " +
                    "enforced — a wall with no door would be unopenable. Re-seed config/release.");
            }

            if (wanted.MinimumBuild == _held.MinimumBuild && wanted.StoreUrl == _held.StoreUrl) return;

            _held = wanted;
            DevicePrefs.WriteString(PrefsKey, Compose(wanted));
        }

        /// <summary>
        /// Reports the wall once per process, at the moment it is actually drawn.
        ///
        /// <para>
        /// <b>Raised from the drawing rather than from <see cref="Apply"/>, and that is not a
        /// tidying choice.</b> Counted on the read, the one session this feature most needs to
        /// see would be missing from the figures entirely: a device that already holds a wall
        /// and cannot reach the network never calls <see cref="Apply"/> at all, and that is
        /// precisely the player who force-quit to get rid of the panel and found it waiting.
        /// Counting the drawing counts every walled session however the wall got there.
        /// </para>
        /// <para>
        /// Once per <em>process</em>, because a player who is walled out has one session and it
        /// consists of this — and because the panel is re-raised on every screen change, so per
        /// raise would count the same wall as many times as anything happened to navigate. It is
        /// the only way to tell a gate that is working from a gate nobody has ever met, which
        /// for a feature whose success state is invisible is the difference between shipped and
        /// believed.
        /// </para>
        /// </summary>
        public static void NoteShown()
        {
            if (_counted || !IsShut) return;
            _counted = true;

            Telemetry.Track(BlockedEvent,
                            "build", AppVersion.Running,
                            "minimum", _held.MinimumBuild,
                            "from_cache", _fromCache);
        }

        /// <summary>
        /// Reads the held requirement off the device, once.
        ///
        /// A malformed value — a hand edit, a half-written file, a key some future version
        /// wrote differently — reads as nothing required. That is the same direction every
        /// other refusal here takes: this feature may fail to wall somebody out, and may never
        /// wall somebody out by accident.
        /// </summary>
        static void Restore()
        {
            if (_restored) return;
            _restored = true;
            _held = Parse(PlayerPrefs.GetString(PrefsKey, string.Empty));
        }

        internal static string Compose(ReleaseRequirement requirement)
            => requirement.IsEnforceable
                ? requirement.MinimumBuild.ToString(System.Globalization.CultureInfo.InvariantCulture)
                  + " " + requirement.StoreUrl
                : string.Empty;

        internal static ReleaseRequirement Parse(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return ReleaseRequirement.None;

            int split = stored.IndexOf(' ');
            if (split <= 0 || split == stored.Length - 1) return ReleaseRequirement.None;

            if (!int.TryParse(stored.Substring(0, split),
                              System.Globalization.NumberStyles.Integer,
                              System.Globalization.CultureInfo.InvariantCulture,
                              out int minimum)) return ReleaseRequirement.None;

            var requirement = new ReleaseRequirement(minimum, stored.Substring(split + 1));
            return requirement.IsEnforceable ? requirement : ReleaseRequirement.None;
        }

        /// <summary>
        /// Drops everything this remembers, on the device as well as in memory.
        ///
        /// For the test suite, which cannot otherwise reach "this install has never been told
        /// anything" — the Editor's own <c>PlayerPrefs</c> survive between runs, so a fixture
        /// about the unknown case would quietly test whatever the last one wrote
        /// (<c>ChapterChoiceTests</c> records that exact trap).
        /// </summary>
        public static void Forget()
        {
            _restored = false;
            _counted = false;
            _warnedDoorless = false;
            _fromCache = true;
            _held = ReleaseRequirement.None;
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
        }
    }
}
