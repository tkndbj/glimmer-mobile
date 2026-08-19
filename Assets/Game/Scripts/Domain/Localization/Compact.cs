using System.Globalization;

namespace GlimmerGrove.Localization
{
    /// <summary>
    /// Writes a resource figure the way a pill can hold it: 9999 in full, 10K above that.
    ///
    /// <para>
    /// This exists because the width of a number is not a function of the number — it is a
    /// function of how much of the game has been played, and it grows for ever. A balance
    /// starts at four digits and a purse that has been earning for a year is seven, so any
    /// chrome sized around the figure it was built with is chrome that breaks later, on
    /// somebody else's device, in a state nobody screenshotted. <c>UIKit.Shrinkable</c> is
    /// the last line of defence rather than the answer: shrinking a coin count to fit is
    /// the point at which it stops being readable at a glance, which is the only thing a
    /// hub pill is for.
    /// </para>
    /// <para>
    /// The rule is <em>truncation, never rounding</em>, and that is the one decision here
    /// worth not re-litigating. A balance is a promise about what can be spent, so a figure
    /// that reads high is a figure that lies — 12,999 shown as "13K" beside a 13,000 price
    /// is a player tapping BUY and being told they are short. Truncating can only ever
    /// understate, which costs nothing: the buy panel names the exact shortfall anyway.
    /// </para>
    /// <para>
    /// Below <see cref="LargestExact"/> it returns the digits unchanged, so every figure the
    /// game already shows — a chest's payout, a glade's earnings, a companion's price — is
    /// untouched by this and cannot regress. Only the balances that had outgrown their pill
    /// change shape.
    /// </para>
    /// </summary>
    public static class Compact
    {
        /// <summary>
        /// The largest figure written out in full. Anything past it is abbreviated.
        ///
        /// <para>
        /// Four digits is where a pill stops fitting the number at its resting size, not a
        /// round number chosen for tidiness — which is why the threshold is 9,999 rather
        /// than the 10,000 the arithmetic below would otherwise suggest.
        /// </para>
        /// </summary>
        public const long LargestExact = 9_999;

        /// <summary>
        /// The suffixes are loc keys because they are player-facing text (invariant 6), and
        /// they are genuinely not "K" and "M" everywhere: German abbreviates thousands as
        /// "Tsd." and Japanese counts in 万 rather than in thousands at all. Both fall back
        /// to the English form through <see cref="Loc.Get(string,string)"/>, which is the
        /// overload that does not warn — a language halfway through translation should show
        /// "10K" rather than shout in the console on every frame the hub is drawn.
        /// </summary>
        public const string ThousandKey = "ui.num.thousand";

        /// <inheritdoc cref="ThousandKey"/>
        public const string MillionKey = "ui.num.million";

        /// <summary>The figure as the player should see it, in the active language.</summary>
        public static string Number(long value)
            => Number(value, Loc.Get(ThousandKey, "K"), Loc.Get(MillionKey, "M"));

        /// <summary>
        /// The rule itself, with the suffixes handed in.
        ///
        /// <para>
        /// Split out from <see cref="Number(long)"/> so it can be run offline: this is
        /// arithmetic over a value that only ever grows, and the failure it guards against —
        /// a balance reading higher than it is — is invisible in a screenshot taken before
        /// the number got big. That is <c>TweenCycle</c>'s bargain and it is here for the
        /// same reason.
        /// </para>
        /// <para>
        /// It never negates <paramref name="value"/>, which is what keeps
        /// <see cref="long.MinValue"/> from wrapping: the division and the remainder both
        /// truncate toward zero and carry the sign with them, so the only thing needing an
        /// absolute value is the single tenths digit, which is small by construction.
        /// Currency is never negative here — but a sentinel is (<c>wallet.coins</c> is -1 on
        /// a save that has not derived one yet), and a formatter that garbles one is a
        /// formatter that hides the bug rather than showing it.
        /// </para>
        /// </summary>
        public static string Number(long value, string thousand, string million)
        {
            if (value <= LargestExact && value >= -LargestExact)
                return value.ToString(CultureInfo.InvariantCulture);

            long scale = 1_000L;
            string suffix = thousand;

            if (value >= 1_000_000L || value <= -1_000_000L)
            {
                scale = 1_000_000L;
                suffix = million;
            }

            long whole = value / scale;
            int tenth = (int)(value % scale * 10L / scale);
            if (tenth < 0) tenth = -tenth;

            string head = whole.ToString(CultureInfo.InvariantCulture);

            // Invariant rather than the current culture: the decimal separator here is a
            // comma in most of Europe, and "10,1K" beside a "1,250" price reads as a
            // thousands separator — the one misreading a currency display cannot afford.
            return tenth == 0
                ? head + suffix
                : head + "." + tenth.ToString(CultureInfo.InvariantCulture) + suffix;
        }
    }
}
