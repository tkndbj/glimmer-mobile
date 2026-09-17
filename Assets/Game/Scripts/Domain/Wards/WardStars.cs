using GlimmerGrove.Content;

namespace GlimmerGrove.Wards
{
    /// <summary>
    /// How far a turret can be upgraded, what each step costs and what it buys.
    ///
    /// <para>
    /// <b>Stars are the second ladder on a turret and they are nothing like the first.</b> A cog
    /// is earned <em>inside</em> a run and forgotten when it ends (<c>SiegeWard.Rank</c>); a star
    /// is bought with credits and is permanent. The two multiply and are deliberately kept apart:
    /// one is a reward for playing a level well, the other is what a player spends a week's
    /// credits on.
    /// </para>
    /// <para>
    /// <b>Every star only ever adds, which is what keeps it out of par's way.</b> A siege's par is
    /// the hill's health over a match computed against the baseline bolt, so a turret that hit
    /// <em>softer</em> would push three stars out of reach of whoever bought it (invariant 37bb).
    /// Upward only means par over-states what an upgraded player needs — the direction invariant
    /// 22 says to err in, and exactly what invariant 37w accepted when cogs shipped. **No level's
    /// par or star line moves because somebody upgraded a turret.**
    /// </para>
    /// <para>
    /// <b>The price is per <see cref="WardTier"/>, because that is the band a player already reads
    /// the shelf in.</b> A tier-three turret costs five times a tier-one one to take to the top,
    /// which is the same shape as the shelf's own price ladder and needs no second idea explained.
    /// </para>
    /// </summary>
    public static class WardStars
    {
        /// <summary>What a turret is worth the moment it is bought, and the least it can be.</summary>
        public const int Least = 1;

        /// <summary>The top of the ladder.</summary>
        public const int Most = 5;

        /// <summary>
        /// What one star adds to a turret's bolt and to its chassis, in tenths.
        ///
        /// <b>The cog ladder's step, deliberately.</b> A rank is ten per cent
        /// (<c>SiegeTuning.RankDamageTenths</c>) and so is a star, so a player who has learned
        /// what one cog is worth already knows what one star is worth. Ten per cent of a tenths
        /// figure is exact in integer arithmetic at every rung, which is the other half of why:
        /// a float step would round three ways on three code generators.
        /// </summary>
        public const int StepTenths = 1;

        /// <summary>
        /// The credit price of each upgrade, by band and then by the star being bought.
        ///
        /// <para>
        /// <b>Four prices per band, because five stars is four upgrades.</b> Row <c>t</c> holds
        /// the cost of reaching stars two, three, four and five for a band-<c>t</c> turret.
        /// </para>
        /// <para>
        /// <b>And there is a row per band rather than a row per turret</b>, which is what makes
        /// adding a band here two lines instead of thirty: <see cref="WardTier.Count"/> is the
        /// length both this table and the authored one are held to, so a band with no prices is
        /// refused at read time rather than shipping a turret nobody can upgrade.
        /// </para>
        /// <para>
        /// <b>Written out rather than generated from a curve.</b> They are the owner's numbers and
        /// a curve that happened to fit them today would quietly disagree the first time one was
        /// retuned — which is <c>HomesteadRegion</c>'s argument about an authored ladder
        /// (invariant 16j), asked of a price list instead of an order.
        /// </para>
        /// </summary>
        static readonly int[][] Built =
        {
            new[] {  2000,  5000, 12000,  30000 },   // tier I
            new[] {  4000, 10000, 28000,  60000 },   // tier II
            new[] { 10000, 28000, 60000, 150000 },   // tier III
            // **Legendary, and the band that keeps the credit sink open above the gem shelf.**
            // A legendary is bought with gems (`WardCatalog.Default`), so without a row here the
            // one part of the shelf a long-running account can still reach for would be free to
            // take to the top - and the ladder is the largest credit sink in the game precisely
            // because nothing else at this end of it is. Four rungs, the band's own multiple of
            // the one below it, exactly as tiers two and three are of tier one.
            new[] { 25000, 70000, 160000, 400000 },  // tier IV
        };

        /// <summary>
        /// The ladder in force: what content authored, or <see cref="Built"/>.
        ///
        /// <b>Content, because a price that needs a store review to change is not a price.</b>
        /// Every other number a live economy turns here is in <c>progression.json</c> — the heart
        /// gate, the chest odds, the ad payouts, the shelf's own prices — and this is the largest
        /// credit sink in the game, so it is the one most likely to be wrong first guess. The
        /// built-in table is the floor rather than a nicety: a malformed block costs a retune and
        /// never a session, which is the bargain <c>WardCatalog.Default</c> already makes.
        /// </summary>
        static int[][] Prices = Built;

        /// <summary>How many upgrades there are, which is one fewer than there are stars.</summary>
        public const int Steps = Most - Least;

        /// <summary>
        /// Reads the authored ladder. Never throws and never leaves a half-applied table: anything
        /// wrong is named and the built-in one stands.
        /// </summary>
        public static void Resolve(WardsDto wards, System.Collections.Generic.List<string> problems)
        {
            Prices = Built;

            // **The block rather than the ladder**, which is `WardCatalog.Resolve`'s shape and is
            // about more than tidiness: a hand-built DTO - which is what every test and every
            // offline caller writes - can leave `wards` null where `JsonUtility` never would, so
            // reaching through it at the call site is a throw nobody would meet until a test ran.
            if (wards == null) return;

            var dto = wards.upgrades;

            if (dto == null || dto.tiers == null || dto.tiers.Length == 0) return;   // absent is not an error

            if (problems == null) problems = new System.Collections.Generic.List<string>();

            if (dto.tiers.Length != WardTier.Count)
            {
                problems.Add($"wards.stars lists {dto.tiers.Length} band(s); the shelf is read in " +
                             $"{WardTier.Count}, so a missing row is a band nobody can upgrade");
                return;
            }

            var read = new int[WardTier.Count][];

            for (int band = 0; band < dto.tiers.Length; band++)
            {
                var row = dto.tiers[band];

                if (row == null || row.prices == null || row.prices.Length != Steps)
                {
                    problems.Add($"wards.stars band {band + 1} needs {Steps} prices, one per " +
                                 "upgrade; using the built-in ladder");
                    return;
                }

                for (int step = 0; step < row.prices.Length; step++)
                {
                    if (row.prices[step] > 0) continue;

                    // A free upgrade is not a cheap one: `WardUpgrade` reads nought as "there is
                    // no next star", so an authored nought would silently top a turret out.
                    problems.Add($"wards.stars band {band + 1} prices star {step + Least + 1} at " +
                                 $"{row.prices[step]}; nought is how this table says a turret is " +
                                 "already at the top, so it may not be a price");
                    return;
                }

                read[band] = (int[])row.prices.Clone();
            }

            Prices = read;
        }

        /// <summary>
        /// What it costs to take <paramref name="model"/> from <paramref name="stars"/> to the
        /// next one, or nought when there is no next one.
        ///
        /// <b>Nought is the answer for a turret already at the top</b>, and every caller reads it
        /// that way rather than testing the star count itself — one question, asked once.
        /// </summary>
        public static int PriceOf(WardModel model, int stars)
        {
            if (model == null) return 0;
            if (stars < Least) stars = Least;
            if (stars >= Most) return 0;

            int band = WardTier.Of(model);
            if (band < 1) band = 1;
            if (band > Prices.Length) band = Prices.Length;

            return Prices[band - 1][stars - Least];
        }

        /// <summary>What every star of <paramref name="model"/> costs together, for a shop line.</summary>
        public static int WholeLadder(WardModel model)
        {
            int whole = 0;
            for (int stars = Least; stars < Most; stars++) whole += PriceOf(model, stars);
            return whole;
        }

        /// <summary>
        /// A star count read back from anywhere at all, brought inside the ladder.
        ///
        /// <b>Clamped rather than trusted</b>, because the number arrives from the save: a file
        /// written by a newer build, a rolled-back client or a hand-edited one must not be able to
        /// stand a turret at nought stars or at fifty. Nought — which is what an absent row and an
        /// older file both mean — reads as <see cref="Least"/>, so a turret bought before this
        /// shipped is a one-star turret rather than a broken one.
        /// </summary>
        public static int Sane(int stars)
        {
            if (stars < Least) return Least;
            return stars > Most ? Most : stars;
        }

        /// <summary>How many tenths a turret at <paramref name="stars"/> is scaled by.</summary>
        public static int ScaleTenths(int stars) => 10 + (Sane(stars) - Least) * StepTenths;
    }
}
