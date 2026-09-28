using System;

namespace GlimmerGrove.Cloud
{
    /// <summary>
    /// A listener on the signed-in account's wallet document.
    ///
    /// <para>
    /// Separate from <see cref="ICloudSaveBackend"/> on purpose: it is an optimisation a backend
    /// may or may not offer, and a backend without it (the null one, every test fake) falls
    /// back to asking on a timer (<see cref="Ads.AdGrantWatch"/>). The callback carries no state
    /// - it only says "the server wrote something", and the balances are still read through
    /// <see cref="ICloudSaveBackend.ReadWalletAsync"/>, so no server rule is copied onto the
    /// client (44p's reason). It may be called on any thread.
    /// </para>
    /// </summary>
    public interface IWalletFeed
    {
        /// <summary>Starts listening; dispose to stop. Null when no listener could be attached.</summary>
        IDisposable WatchWallet(Action onChanged);
    }
}
