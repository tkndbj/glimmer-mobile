namespace GlimmerGrove.Wards
{
    /// <summary>
    /// Which band of the shelf a turret stands in: three groups a player can name, with a header
    /// drawn between them.
    ///
    /// <para>
    /// <b>A fact about the rung, so it is keyed on <c>WardModel.Order</c> and lives here rather
    /// than in the screen that draws the headers.</b> The shelf has already been re-rung twice
    /// (invariants 37ax, 37ay) and a table of ids inside a view would have gone stale both times;
    /// read off the order, the bands follow whatever the shelf is today. It is also why
    /// <c>SiegeView.Barrels</c> is keyed the same way — the two are the same kind of question.
    /// </para>
    /// <para>
    /// <b>Purely how the shelf is read, and nothing else may key on it.</b> A tier is not a gate,
    /// a price or a stat: what opens a rung is the one below it plus a keeper level
    /// (<c>WardCatalog.Before</c>), and what a turret does is its ability and its figures. Drawing
    /// a band where one currency ends and another begins is a label on an order that already
    /// exists, so it cannot disagree with anything.
    /// </para>
    /// <para>
    /// <b>The boundaries are authored here and the first is not a coincidence.</b> Tier one is
    /// exactly the free turret and the credit ladder; tier two and three split the gem half where
    /// the owner asked. It would be tempting to <em>derive</em> the first from the currency and
    /// leave the second typed — one rule, two spellings, which is worse than two of the same.
    /// </para>
    /// </summary>
    public static class WardTier
    {
        /// <summary>How many bands the shelf is read in.</summary>
        public const int Count = 3;

        /// <summary>
        /// The shelf rung each band <em>starts</em> at, lowest first.
        ///
        /// <b>Starts rather than sizes</b>, so a drop that adds a turret to the middle of a band
        /// widens that band instead of shifting every boundary after it — which is the same reason
        /// a chapter's levels are a list and its gate is a rule.
        /// </summary>
        static readonly int[] Opens = { 1, 11, 18 };

        /// <summary>
        /// Which band <paramref name="model"/> stands in, one to <see cref="Count"/>.
        ///
        /// <b>Never nought</b>: an order below the first boundary — which content cannot author,
        /// since orders run from one — still reads as the first band rather than as a turret with
        /// no home, because a shelf with an unlabelled cell on it is worse than one whose label is
        /// generous.
        /// </summary>
        public static int Of(WardModel model) => model == null ? 1 : Of(model.Order);

        /// <summary>See <see cref="Of(WardModel)"/>.</summary>
        public static int Of(int order)
        {
            int tier = 1;

            for (int i = 0; i < Opens.Length; i++)
                if (order >= Opens[i]) tier = i + 1;

            return tier;
        }

        /// <summary>The shelf rung this band opens at.</summary>
        public static int OpensAt(int tier)
        {
            if (tier < 1) tier = 1;
            if (tier > Count) tier = Count;

            return Opens[tier - 1];
        }

        /// <summary>
        /// Whether <paramref name="model"/> is the first turret of its band, and so the one a
        /// header is drawn above.
        /// </summary>
        public static bool Starts(WardModel model)
            => model != null && model.Order == OpensAt(Of(model));

        /// <summary>
        /// The loc key for a band's name.
        ///
        /// <b>A switch over literals rather than a key built from the number</b>, which is
        /// invariant 6's rule: a concatenated key is one the loc gate cannot see, and the gate is
        /// the only thing that would notice a band whose name nobody wrote.
        /// </summary>
        public static string NameKey(int tier)
        {
            switch (tier)
            {
                case 1: return "ui.loadout.tier1";
                case 2: return "ui.loadout.tier2";
                default: return "ui.loadout.tier3";
            }
        }
    }
}
