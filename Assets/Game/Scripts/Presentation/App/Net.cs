using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// Whether this device has a route to the network, asked in one place.
    ///
    /// <para>
    /// <b>Why this exists rather than <see cref="Application.internetReachability"/> at each
    /// call site.</b> Three screens now say a different sentence when the phone is offline,
    /// and a fourth refuses a tap because of it. Written out four times that is four chances
    /// to spell one of them as <c>!= NetworkReachability.NotReachable</c> and one as
    /// <c>== ReachableViaLocalAreaNetwork</c> — the second is a mobile player permanently
    /// "offline" on cellular, and it is the kind of fault that is green in every gate here
    /// because nothing in this project can turn a radio off. One reading, one polarity.
    /// </para>
    /// <para>
    /// <b>It is a hint, not a fact, and every caller has to be written that way.</b> The OS
    /// answers about the <em>radio</em>, not about whether anything can be reached: a captive
    /// portal, an airline wifi splash page and a dead backend all report a perfectly good
    /// connection. So this may never be used to <em>refuse</em> anything — the real answer is
    /// always whatever the request came back with. What it is for is choosing which sentence
    /// to print once something has already failed, and that is the one job it is honest at:
    /// "the boards could not be reached" and "you have no connection" are both true when the
    /// radio is off, and only the second tells the player what to do about it.
    /// </para>
    /// <para>
    /// Presentation rather than Domain, deliberately, and it is the split
    /// <c>Boot.Pump</c> already documents from the other end: the policies in Domain are meant
    /// to be runnable in the test suite with no <c>UnityEngine.Application</c> anywhere near
    /// them, so connectivity is read out here and handed in. Nothing in Domain may call this.
    /// </para>
    /// </summary>
    public static class Net
    {
        /// <summary>
        /// True when the device reports no route to the network at all.
        ///
        /// The negative is the one worth having, because every caller is choosing what to say
        /// about a failure: <c>if (Net.Offline)</c> reads as the branch it is, where
        /// <c>if (!Net.Online)</c> is a double negative on a line that is already about
        /// something having gone wrong.
        /// </summary>
        public static bool Offline
            => Application.internetReachability == NetworkReachability.NotReachable;

        /// <summary>True when the device reports a route of any kind. The complement.</summary>
        public static bool Online => !Offline;
    }
}
