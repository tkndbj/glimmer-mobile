using System;

namespace GlimmerGrove
{
    /// <summary>
    /// Where the game gets the time, and how much that time is worth trusting.
    ///
    /// This exists because hearts refill on a schedule and stop play at zero. The
    /// moment elapsed time buys something, the device clock stops being a fact and
    /// becomes an input the player controls: rolling it forward mints hearts, and no
    /// amount of client code can tell the difference between that and a long lunch.
    ///
    /// So time is an injected dependency rather than a call to
    /// <see cref="DateTimeOffset.UtcNow"/> scattered through the codebase. Swapping in
    /// a server-anchored clock later is a change to <see cref="Set"/> and nothing else.
    /// </summary>
    public interface IGameClock
    {
        /// <summary>Seconds since the Unix epoch, UTC.</summary>
        long UtcNowUnix { get; }

        /// <summary>
        /// Whether this time came from somewhere the player cannot set. False for the
        /// device clock. Callers must not gate a *purchase* on untrusted time; gating
        /// play is fine, because the server reconciles on the next sync.
        /// </summary>
        bool IsTrusted { get; }
    }

    /// <summary>The device's own clock. Honest, and completely unauthenticated.</summary>
    public sealed class DeviceClock : IGameClock
    {
        public long UtcNowUnix => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        public bool IsTrusted => false;
    }

    /// <summary>
    /// The device clock corrected by an offset the server told us about.
    ///
    /// The offset is learned on each successful sync and applied to the local clock,
    /// so the reading stays smooth and monotonic between syncs instead of jumping
    /// whenever the network is available. It is trusted because a player who moves
    /// their device clock moves the *reading* and the *offset* together — the
    /// corrected value does not budge.
    /// </summary>
    public sealed class ServerAnchoredClock : IGameClock
    {
        readonly IGameClock _device;
        long _offsetSeconds;

        public ServerAnchoredClock(IGameClock device, long offsetSeconds = 0)
        {
            _device = device ?? new DeviceClock();
            _offsetSeconds = offsetSeconds;
        }

        public long UtcNowUnix => _device.UtcNowUnix + _offsetSeconds;

        public bool IsTrusted => true;

        /// <summary>Re-anchors from a server timestamp observed just now.</summary>
        public void Anchor(long serverUnix) => _offsetSeconds = serverUnix - _device.UtcNowUnix;
    }

    /// <summary>
    /// The clock the game reads, plus the guard that stops it running backwards.
    ///
    /// <see cref="NowUnix"/> never returns a value lower than one it has already
    /// returned. That is not paranoia about cheating — a backwards jump is ordinary on
    /// a real device (an NTP correction, a manual timezone fix) and without the guard
    /// it would strand a refill timer in the future, leaving a player staring at a
    /// countdown that grows. Rolling *forward* is the exploit, and only a trusted
    /// clock can answer that; see <see cref="IGameClock.IsTrusted"/>.
    /// </summary>
    public static class GameClock
    {
        static IGameClock _clock = new DeviceClock();
        static long _highWater;

        public static IGameClock Source => _clock;

        public static bool IsTrusted => _clock.IsTrusted;

        /// <summary>
        /// Installs a clock. Called by the boot path once it knows whether a cloud
        /// backend is available; nothing else should need it outside tests.
        /// </summary>
        public static void Set(IGameClock clock)
        {
            _clock = clock ?? new DeviceClock();
            _highWater = 0;
        }

        public static long NowUnix()
        {
            long now = _clock.UtcNowUnix;
            if (now < _highWater) return _highWater;

            _highWater = now;
            return now;
        }
    }
}
