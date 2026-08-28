namespace GlimmerGrove.Privacy
{
    /// <summary>
    /// The three public pages this game is obliged to be able to reach.
    ///
    /// <para>
    /// <b>Why they are in code at all.</b> App Store Review 5.1.1(i) requires the privacy
    /// policy to be reachable *inside* the app as well as named in the store metadata, and a
    /// link only in App Store Connect is a documented rejection. Google Play asks for the same
    /// pages on the listing, and Guideline 1.2 asks that an app carrying user-generated content
    /// — which a public leaderboard of keeper names is — publish a contact route. One row of
    /// three links in Settings answers all three obligations.
    /// </para>
    /// <para>
    /// <b>Why they are constants rather than content.</b> Nearly every tunable in this game is
    /// published through <c>progression.json</c> so it can move without an app update, and
    /// these deliberately are not. A remote-controlled privacy policy URL is a privacy policy
    /// somebody can point somewhere else after review; the value has to be the one the binary
    /// was reviewed with. They also have to work on a launch that has never reached the
    /// network, which is exactly when a player is most likely to be looking for support.
    /// </para>
    /// <para>
    /// <b>The host is <c>www</c>, deliberately.</b> Vercel serves www and 308-redirects the
    /// apex to it, so the apex would work and would spend a redirect on every open — and the
    /// same www host is what belongs in the Developer website field of both store listings, so
    /// that ad crawlers fetch <c>app-ads.txt</c> directly rather than through the hop. One
    /// spelling everywhere is the point.
    /// </para>
    /// <para>
    /// Deliberately <b>not</b> a link to <c>/delete-account</c>. That page exists because Google
    /// Play requires deletion to be reachable without installing the app; Apple requires it
    /// reachable *within* the app, and pointing at a web page there is only permitted for
    /// regulated industries. The in-app control is <c>DeleteAccountOverlay</c>, and offering a
    /// second route that leaves the game would read as the real one being somewhere else.
    /// </para>
    /// </summary>
    public static class LegalLinks
    {
        /// <summary>The canonical origin. Every link below is built from it.</summary>
        public const string Site = "https://www.glimmergroove.app";

        /// <summary>Required in the app by 5.1.1(i), and on both store listings.</summary>
        public const string Privacy = Site + "/privacy";

        /// <summary>The terms the store listings point at.</summary>
        public const string Terms = Site + "/terms";

        /// <summary>
        /// Where a player reaches a person. This is the "published contact information" half of
        /// Guideline 1.2, which the reporting flow does not satisfy on its own — a report is a
        /// way to flag somebody else, not a way to reach us.
        /// </summary>
        public const string Support = Site + "/support";

        /// <summary>
        /// Whether a link is safe to hand to the platform's browser.
        ///
        /// <para>
        /// Every value here is a compile-time constant, so this can only fail if somebody edits
        /// one badly — which is precisely the case worth catching, because the symptom is a
        /// control that silently does nothing on a device and cannot be seen in the Editor.
        /// <c>LegalLinkTests</c> asks it of all three.
        /// </para>
        /// </summary>
        public static bool Usable(string url)
            => !string.IsNullOrEmpty(url)
               && url.StartsWith("https://", System.StringComparison.Ordinal)
               && url.Length > "https://".Length
               && !url.Contains(" ");
    }
}
