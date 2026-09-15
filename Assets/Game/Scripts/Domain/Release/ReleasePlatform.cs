using UnityEngine;

namespace GlimmerGrove.Release
{
    /// <summary>
    /// Which block of <c>config/release</c> this build is subject to, and the store link to fall
    /// back on when the document names none.
    ///
    /// <para>
    /// <b>Resolved from the build-target defines rather than from
    /// <see cref="Application.platform"/></b>, and that is the difference between a feature that
    /// can be exercised before it ships and one that cannot. <c>Application.platform</c> in the
    /// Editor is the <em>Editor's</em> platform, so a gate keyed on it answers "neither store"
    /// on the one machine anybody can try it on — the wall would first be seen working on a
    /// device, which for a wall is far too late. <c>UNITY_ANDROID</c> and <c>UNITY_IOS</c> are
    /// set by the active build target, in the Editor as well as in the player, so switching the
    /// target is how this gets tested. On a device the two agree by construction.
    /// </para>
    /// <para>
    /// <b>Anything else answers nothing, and nothing means no wall.</b> A desktop build has no
    /// store to be sent to, so a forced update there would be a dead end by definition — the
    /// same reasoning <see cref="ReleaseRequirement.HasDoor"/> applies one layer down.
    /// </para>
    /// </summary>
    public static class ReleasePlatform
    {
        /// <summary>
        /// The document key. These are wire spellings: the seeder writes them and
        /// <c>config/release</c> is keyed on them, so renaming one silently stops every device
        /// on that platform finding its own block — a wall that quietly lifts rather than one
        /// that quietly falls, which is the safe direction and still wrong.
        /// </summary>
        public const string Android = "android";

        /// <summary>See <see cref="Android"/>.</summary>
        public const string Ios = "ios";

        /// <summary>
        /// Which block this build reads, or empty where there is no store behind it.
        /// </summary>
        public static string Current
        {
#if UNITY_ANDROID
            get => Android;
#elif UNITY_IOS
            get => Ios;
#else
            get => string.Empty;
#endif
        }

        /// <summary>
        /// The store link to use when the published block names none.
        ///
        /// <para>
        /// <b>Android has one and iOS cannot.</b> A Play listing is addressed by the bundle id,
        /// which the process already knows, so an Android device is never left facing a wall
        /// with no door even if the document is bare. An App Store listing is addressed by a
        /// numeric Apple id that is minted by App Store Connect and is derivable from nothing
        /// in the build — so on iOS the published link is the only answer there is, and a block
        /// without one is refused rather than guessed at
        /// (<see cref="ReleaseRequirement.IsEnforceable"/>).
        /// </para>
        /// <para>
        /// <c>https</c> rather than <c>market://</c>: both stores claim their own https links on
        /// device, and where that claim fails the fallback is a web page with the install button
        /// on it, where a failed scheme link opens nothing at all. It is also what lets the one
        /// URL predicate this game already has (<c>LegalLinks.Usable</c>) judge it.
        /// </para>
        /// </summary>
        public static string FallbackStoreUrl
        {
#if UNITY_ANDROID
            get => "https://play.google.com/store/apps/details?id=" + Application.identifier;
#else
            get => string.Empty;
#endif
        }
    }
}
