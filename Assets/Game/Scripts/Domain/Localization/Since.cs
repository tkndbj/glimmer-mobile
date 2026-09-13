using System.Globalization;

namespace GlimmerGrove.Localization
{
    /// <summary>
    /// How long ago something happened, in words a player can read at a glance.
    ///
    /// <para>
    /// <b>It exists because a picture of somebody else's grove is never "now".</b> A visited
    /// grove is drawn from a card the server rebuilt the last time its owner's device synced,
    /// which can be minutes or days ago — and a visitor with no way to tell has no way to
    /// distinguish "this keeper has not played since Tuesday" from "this game is showing me the
    /// wrong thing". That second reading is the one that was reported, and it is the expensive
    /// one: a feature a player has decided is broken stops being opened. Every other state on
    /// that screen already renders its own sentence; this is the one that could not, because
    /// nothing said when the picture was taken.
    /// </para>
    /// <para>
    /// <b>Four units and no more.</b> Seconds are noise on something that is rebuilt when a
    /// device happens to sync, and anything past days is a grove nobody is tending — both ends
    /// are answered better by the coarse word than by a precise one.
    /// </para>
    /// <para>
    /// <b>Truncated rather than rounded</b>, which is <see cref="Compact"/>'s rule arriving at
    /// a different subject: the figure can then only ever understate the age, by less than one
    /// unit of whatever it is counting in, and a reading that is off by part of an hour on a
    /// thing rebuilt daily is not a reading anybody acts on. What matters is that the units
    /// never disagree with each other, which truncation guarantees and rounding does not.
    /// </para>
    /// </summary>
    public static class Since
    {
        /// <summary>
        /// Younger than this and it is simply "just now".
        ///
        /// Ninety seconds rather than sixty so the first minute does not tick over while
        /// somebody is looking at it — a readout that changes under the eye reads as a
        /// countdown, and this is a fact rather than a clock.
        /// </summary>
        public const long JustNowSeconds = 90L;

        const long Minute = 60L, Hour = 3600L, Day = 86400L;

        /// <summary>
        /// Player-facing text, so loc keys (invariant 6). Held as constants rather than typed
        /// at the call site because the build gate scans for key-shaped literals and a key
        /// built by concatenation is a key it cannot see.
        /// </summary>
        public const string NowKey = "ui.when.now";

        /// <inheritdoc cref="NowKey"/>
        public const string MinutesKey = "ui.when.minutes";

        /// <inheritdoc cref="NowKey"/>
        public const string HoursKey = "ui.when.hours";

        /// <inheritdoc cref="NowKey"/>
        public const string DaysKey = "ui.when.days";

        /// <summary>
        /// How long ago <paramref name="thenUnix"/> was, or an empty string when there is no
        /// answer worth giving.
        ///
        /// <para>
        /// A stamp of nought and a stamp in the future both answer empty rather than guessing.
        /// The first is a card from before the server recorded when it built one, and the
        /// second is a device whose clock is behind the server's — neither is a state to
        /// describe, and a caller that draws an empty string draws nothing, which is the right
        /// amount to say about a fact nobody has.
        /// </para>
        /// </summary>
        public static string Describe(long thenUnix, long nowUnix)
        {
            if (thenUnix <= 0L || nowUnix <= 0L) return string.Empty;

            long elapsed = nowUnix - thenUnix;
            if (elapsed < 0L) return string.Empty;

            if (elapsed < JustNowSeconds) return Loc.Get(NowKey);

            if (elapsed < Hour) return Count(MinutesKey, elapsed / Minute);
            if (elapsed < Day) return Count(HoursKey, elapsed / Hour);

            return Count(DaysKey, elapsed / Day);
        }

        static string Count(string key, long value)
            => Loc.Format(key, value.ToString(CultureInfo.InvariantCulture));
    }
}
