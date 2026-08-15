using GlimmerGrove.Cloud;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Progression
{
    /// <summary>
    /// What every reproducible reward is seeded from.
    ///
    /// <para>
    /// The game now has two rewards that are computed rather than stored — a daily chest's
    /// contents and a glade's golden bonus — and both rest on the same requirement: the
    /// server must arrive at the same answer from the same inputs, or the client is showing
    /// a player a reward the server will overrule. That makes the identity used to seed
    /// them a piece of the wire contract, not an implementation detail, and it is the sort
    /// of expression that gets copied to a second call site and then drifts.
    /// </para>
    /// <para>
    /// So it is written once, here. The account id, because it is the only identifier the
    /// server can also compute from. The device id only when there is no backend
    /// configured at all — where nothing is adjudicated and the seed simply has to be
    /// stable for this installation.
    /// </para>
    /// <para>
    /// Empty before the first sign-in on a build that does have a backend. Callers must
    /// treat that as "no reward yet" rather than substituting something: a chest waits
    /// (see <c>DailyChests.CanOpen</c>) and a golden pays its base. Both are the
    /// conservative direction, which is the only safe one — the client cannot know the
    /// server's seed before it has spoken to the server, and no scheme can invent one.
    /// </para>
    /// </summary>
    public static class RewardSeed
    {
        /// <summary>
        /// The key to seed with, or empty when there is not one yet. See the type summary.
        /// </summary>
        public static string PlayerKey
        {
            get
            {
                if (CloudState.IsSignedIn) return CloudState.UserId;

                // No backend at all: nothing will ever be adjudicated, so a stable local
                // seed is not a disagreement waiting to happen.
                return CloudSaveService.IsAvailable ? string.Empty : CloudState.DeviceId;
            }
        }

        /// <summary>True when a reward that has to be reproducible may be computed at all.</summary>
        public static bool IsReady => !string.IsNullOrEmpty(PlayerKey);
    }
}
