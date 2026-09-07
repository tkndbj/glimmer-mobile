using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Cloud;

namespace GlimmerGrove.Events
{
    /// <summary>Server-owned purchase and claim state; never inferred from a local save.</summary>
    public sealed class EventPassState
    {
        public string EventId;
        public bool Owned;
        public int CollectedGoal;
        public int Finished;
        public long Credits;
        public long Gems;
    }

    /// <summary>Optional cloud capability so older/offline backends fail closed.</summary>
    public interface IEventPassBackend
    {
        Task<(CloudResult result, List<CloudWalletState> wallets, EventPassState state)> EventPassAsync(
            string userId, string eventId, int goal, CancellationToken cancellation = default);
    }
}
