using System;

namespace GlimmerGrove.Offers
{
    /// <summary>
    /// What the offer engine knows about one player at one moment: a whole number per
    /// <see cref="OfferMeasure"/> (invariant 64). <b>Derived and stored nowhere</b> - built afresh by
    /// <see cref="OfferLedger"/> each time an offer is considered, from the save, this device's
    /// <see cref="OfferHistory"/> and the server's <see cref="PayerLedger"/>. A plain value so the
    /// rules can be held offline with any player a fixture cares to describe.
    /// </summary>
    public sealed class OfferProfile
    {
        /// <summary>What <see cref="OfferMeasure.DaysSincePurchase"/> reads for an account that never bought.</summary>
        public const long Never = 100000L;

        readonly long[] _values = new long[Enum.GetValues(typeof(OfferMeasure)).Length];

        public OfferProfile()
        {
            Set(OfferMeasure.DaysSincePurchase, Never);
        }

        public long Get(OfferMeasure measure)
        {
            int at = (int)measure;
            return at > 0 && at < _values.Length ? _values[at] : 0L;
        }

        public OfferProfile Set(OfferMeasure measure, long value)
        {
            int at = (int)measure;
            if (at > 0 && at < _values.Length) _values[at] = value;
            return this;
        }

        /// <summary>For analytics and logs: every measure as <c>id=value</c>.</summary>
        public override string ToString()
        {
            var parts = new string[OfferMeasures.All.Length];
            for (int i = 0; i < parts.Length; i++)
                parts[i] = OfferMeasures.Id(OfferMeasures.All[i]) + "=" + Get(OfferMeasures.All[i]);
            return string.Join(" ", parts);
        }
    }
}
