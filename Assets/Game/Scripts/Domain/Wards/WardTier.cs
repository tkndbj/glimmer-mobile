namespace GlimmerGrove.Wards
{
    /// <summary>
    /// Which band of the shelf a turret stands in: four groups a player can name, with a header
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
    /// <b>A band is a wall now, and it was a caption before.</b> The shelf used to be one ladder
    /// climbed a rung at a time — a turret sealed until the one below it was held — so the three
    /// headers were punctuation over an order that was already forced, and this file said in as
    /// many words that nothing else might key on it. The seal is gone at the owner's decision: a
    /// keeper level is the whole of what opens a rung, and a player buys whatever they have
    /// reached in whatever order they like. What that leaves is three bands and three walls, so
    /// the band is what the walls are authored against — <see cref="OpensAtLevel"/>.
    /// </para>
    /// <para>
    /// <b>Still not a price and still not a stat.</b> What a turret does is its ability and its
    /// figures, and what it costs is its own price; the band says only which stretch of keeper
    /// levels its wall must stand in, which is the one thing a header claiming "TIER II" is
    /// actually promising a player who reads it.
    /// </para>
    /// <para>
    /// <b>The boundaries are authored here and the first is not a coincidence.</b> Tier one is
    /// exactly the free turret and the credit ladder; tier two and three split the gem half where
    /// the owner asked. It would be tempting to <em>derive</em> the first from the currency and
    /// leave the second typed — one rule, two spellings, which is worse than two of the same.
    /// </para>
    /// <para>
    /// <b>The fourth band is LEGENDARY, and it is the one band that is also a <em>rule</em> — but
    /// the rule is not read off here.</b> A legendary turret wears no colour, stands on any seat
    /// and fires at anything on the hill (<c>WardModel.Legendary</c>), and that is an authored
    /// flag on the model rather than a reading of its rung: this type has said since it was
    /// written that a band is not a price and not a stat, and a band that silently decided what a
    /// turret <em>does</em> would be exactly that. What the band still owns is the header and the
    /// stretch of keeper levels under it; what holds the two together is
    /// <see cref="WardCatalog.LadderProblem"/>, which refuses a legendary outside this band and
    /// anything else inside it.
    /// </para>
    /// </summary>
    public static class WardTier
    {
        /// <summary>How many bands the shelf is read in.</summary>
        public const int Count = 4;

        /// <summary>
        /// The shelf rung each band <em>starts</em> at, lowest first.
        ///
        /// <b>Starts rather than sizes</b>, so a drop that adds a turret to the middle of a band
        /// widens that band instead of shifting every boundary after it — which is the same reason
        /// a chapter's levels are a list and its gate is a rule.
        /// </summary>
        static readonly int[] Opens = { 1, 11, 18, 21 };

        /// <summary>
        /// The keeper level each band's rungs stand at or above, lowest first.
        ///
        /// <para>
        /// <b>The owner's four stretches: under twenty, twenty to thirty, thirty to forty, and
        /// forty-five up.</b>
        /// A band's rungs may ask for anything from its own opening level up to the level the
        /// band above it opens at, exclusive — and the top band up to <see cref="TopLevel"/>.
        /// </para>
        /// <para>
        /// <b>Written here rather than in the roster, which is the point of it.</b> Every
        /// turret's wall is authored one at a time in <c>progression.json</c>, so twenty numbers
        /// carry a shape nobody stated; said once, the shape is a thing both content gates can
        /// refuse a file for breaking (<c>WardCatalog.LadderProblem</c>). A header reading
        /// "TIER II" over a rung asking for keeper level four is a promise the shelf is not
        /// keeping, and it is exactly the fault no numeric gate could see while the walls were
        /// only ever asked to climb.
        /// </para>
        /// </summary>
        static readonly int[] Gates = { 1, 20, 30, 45 };

        /// <summary>
        /// The highest keeper level any rung of the shelf may ask for.
        ///
        /// The top band's ceiling, and the one number here that is a decision rather than a
        /// boundary: it says how far up the game's own progression the shelf is allowed to
        /// reach, so a retune that put a turret at level ninety would be refused rather than
        /// shipping a padlock nobody alive can open.
        ///
        /// <b>It moved from forty to sixty when the legendary band opened</b>, which is the one
        /// edit a new band costs beyond two array entries — and it is worth saying that the
        /// number has stopped meaning "the top of tier three": band three's ceiling is now one
        /// under band four's gate, exactly as every other boundary already was.
        /// </summary>
        public const int TopLevel = 60;

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

        /// <summary>The lowest keeper level a rung of this band may ask for.</summary>
        public static int OpensAtLevel(int tier)
        {
            if (tier < 1) tier = 1;
            if (tier > Count) tier = Count;

            return Gates[tier - 1];
        }

        /// <summary>
        /// The highest keeper level a rung of this band may ask for.
        ///
        /// <b>One under the band above, so the two stretches cannot overlap.</b> A wall shared by
        /// the last rung of one band and the first of the next is a boundary a player cannot read
        /// off the shelf: two headers, one condition.
        /// </summary>
        public static int ClosesAtLevel(int tier)
        {
            if (tier < 1) tier = 1;
            if (tier > Count) tier = Count;

            return tier == Count ? TopLevel : Gates[tier] - 1;
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
                case 3: return "ui.loadout.tier3";
                default: return "ui.loadout.tier4";
            }
        }
    }
}
