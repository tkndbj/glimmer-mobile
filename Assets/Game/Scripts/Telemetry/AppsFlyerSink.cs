#if GLIMMER_APPSFLYER
using System.Collections.Generic;
using System.Globalization;
using AppsFlyerSDK;

namespace GlimmerGrove.Analytics
{
    /// <summary>
    /// Forwards a small, named set of this game's events to the attribution partner, which
    /// passes them on to whichever ad network the install came from.
    ///
    /// <para>
    /// <b>An allow list rather than a pass-through, and that is the whole design.</b> The
    /// analytics sink beside this one takes everything, because a report nobody has thought of
    /// yet is worth having and costs nothing. This one is the opposite on both counts. Every
    /// event here is forwarded to third parties for them to optimise against, so the list is a
    /// statement about what leaves the app; and an optimiser handed thirty signals learns from
    /// none of them, because the ones that predict a paying player are drowned by the ones that
    /// happen to everybody. A default case that forwarded the rest would quietly undo both.
    /// </para>
    /// <para>
    /// <b>Mapped onto the vendor's own event names where one fits, and never where it does
    /// not.</b> A standard name is what lets a network recognise a signal it has seen from a
    /// thousand other games; a standard name used for something else is worse than a custom
    /// one, because it is wrong in a vocabulary somebody else is reading.
    /// </para>
    /// <para>
    /// <b>No queue, unlike the analytics sink.</b> That one holds events because the ones it
    /// most needs happen while its SDK is still starting. Nothing on this list can: attribution
    /// starts the moment consent resolves on the splash, and the earliest event here is a
    /// finished level. An event arriving before the start is therefore a real fault rather than
    /// a race, and dropping it is how it stays visible.
    /// </para>
    /// </summary>
    public sealed class AppsFlyerSink : IAnalyticsSink
    {
        /// <summary>
        /// Set once the SDK has been started. Before that, forwarding is a no-op — the SDK
        /// would accept the call and discard it, which is the same outcome with more noise.
        /// </summary>
        public bool Started { get; set; }

        readonly Dictionary<string, string> _values = new Dictionary<string, string>(4);

        public void Track(string eventName, IReadOnlyDictionary<string, object> properties)
        {
            if (!Started || properties == null) return;

            switch (eventName)
            {
                // A finished level is this game's install-quality signal. There is no ordinal
                // in the event and deliberately none invented here — a position is a fact about
                // a catalog that changes under a player (invariant 1 exists because nothing may
                // key on where a level sits), so the level's own permanent id is sent instead
                // and a campaign is targeted at the id it cares about.
                case LevelAnalytics.Completed:
                    Send(AFInAppEvents.LEVEL_ACHIEVED,
                         AFInAppEvents.CONTENT_ID, Text(properties, "level_id"),
                         AFInAppEvents.SCORE, Text(properties, "stars"));
                    return;

                // A lesson met for the first time. These are the early funnel: they happen once
                // in a player's life, in the first sessions, in a fixed order, which is exactly
                // the shape an optimiser can learn from. A repeat is somebody looking a rule up
                // later and says nothing about install quality, so it is dropped here rather
                // than being a parameter the network has to know to ignore.
                case LessonAnalytics.Finished:
                    if (Flag(properties, "repeat")) return;
                    if (Text(properties, "how") != LessonAnalytics.ByButton) return;

                    Send(AFInAppEvents.ACHIEVEMENT_UNLOCKED,
                         AFInAppEvents.DESCRIPTION, Text(properties, "mechanic"));
                    return;

                // Deliberately not forwarded:
                //
                // af_purchase, because it would have to be sent without a revenue figure.
                // `store_purchase_granted` carries the product id and what was granted, and no
                // price: StoreService never sees one — the decimal and the currency code live on
                // StoreProductInfo, which the backend builds and the grant path does not keep.
                // A purchase event with no revenue does not merely omit the number, it reports
                // the sale as worth nothing, so every dashboard computing return on ad spend
                // would read zero and be believed. Silence is the honest reading until the price
                // is plumbed through, and that is a change to the receipt path (invariant 18a)
                // rather than to this file.
                //
                // af_tutorial_completion, because this game has no tutorial to complete. It
                // teaches continuously, one lesson at the moment the board first demonstrates
                // the rule (37bk), so there is no single moment the standard event would name —
                // and pointing it at an arbitrary one of the thirty-five would put a wrong
                // answer into a vocabulary other people read.
            }
        }

        void Send(string name, string keyA, string valueA, string keyB = null, string valueB = null)
        {
            _values.Clear();

            if (!string.IsNullOrEmpty(valueA)) _values[keyA] = valueA;
            if (keyB != null && !string.IsNullOrEmpty(valueB)) _values[keyB] = valueB;

            AppsFlyer.sendEvent(name, _values);
        }

        /// <summary>
        /// A property as the invariant-culture string the SDK's dictionary wants.
        ///
        /// <para>
        /// The culture is the point rather than a formality: a device set to a language that
        /// writes a decimal comma would otherwise send "1,5" where the receiver parses on a
        /// point, and the figure would be silently wrong in exactly the markets a soft launch
        /// is run in.
        /// </para>
        /// </summary>
        static string Text(IReadOnlyDictionary<string, object> properties, string key)
        {
            if (!properties.TryGetValue(key, out object value) || value == null)
                return string.Empty;

            return value is System.IFormattable formattable
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : value.ToString();
        }

        static bool Flag(IReadOnlyDictionary<string, object> properties, string key)
            => properties.TryGetValue(key, out object value) && value is bool flag && flag;
    }
}
#endif
