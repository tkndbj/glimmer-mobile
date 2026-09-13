#if GLIMMER_FIREBASE
using System.Threading.Tasks;
using Firebase;

namespace GlimmerGrove.Cloud
{
    /// <summary>
    /// Resolves Firebase's native dependencies once for the whole app.
    ///
    /// <para>
    /// <b>`CheckAndFixDependenciesAsync` may not be called twice at once, and the SDK's refusal
    /// names the wrong thing.</b> A second caller — or any other Firebase call — made while one
    /// check is in flight fails with <em>"Don't call other Firebase functions while
    /// CheckDependencies is running"</em>, and on this project that arrived as
    /// <c>[Cloud] Firebase failed to initialise</c>: no auth, no Firestore, therefore no sign-in
    /// and no leaderboards, from a change made in the analytics code. The two subsystems had no
    /// visible relationship and the failure named neither of them.
    /// </para>
    /// <para>
    /// It <em>is</em> safe to call sequentially — a later call is handed the settled answer —
    /// which is what makes this the whole fix: one check, one Task, every caller awaiting the
    /// same one. It lives in the cloud assembly rather than in Domain because this is where
    /// Firebase is initialised and where the reference to the SDK already exists.
    /// </para>
    /// <para>
    /// Not thread-safe by design, and it does not need to be: both callers start from
    /// <c>Boot</c> on Unity's main thread, which is also the only thread the SDK may be
    /// initialised from. A lock here would suggest otherwise.
    /// </para>
    /// </summary>
    public static class FirebaseReady
    {
        static Task<DependencyStatus> _check;

        /// <summary>
        /// The one dependency check. Every caller awaits the same Task, so the first one to ask
        /// starts it and the rest are handed its result whenever it settles.
        /// </summary>
        public static Task<DependencyStatus> EnsureAsync()
            => _check ?? (_check = FirebaseApp.CheckAndFixDependenciesAsync());

        /// <summary>Whether the check has finished and reported the SDK usable.</summary>
        public static bool IsAvailable
            => _check != null
            && _check.Status == TaskStatus.RanToCompletion
            && _check.Result == DependencyStatus.Available;
    }
}
#endif
