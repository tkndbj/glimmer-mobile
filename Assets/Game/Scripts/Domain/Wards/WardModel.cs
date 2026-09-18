using System;

namespace GlimmerGrove.Wards
{
    /// <summary>
    /// One turret a player may stand on the line: a permanent id, what it does beyond firing, and
    /// what it costs to own.
    ///
    /// <para>
    /// <b>Its id is permanent and invariant 1 reaches it</b>, because the save keys both halves of
    /// the feature on it — the set of turrets a player has bought, and which one they have put on
    /// each colour. Renaming one confiscates a purchase; reusing one hands somebody a turret they
    /// never bought.
    /// </para>
    /// <para>
    /// <b>Everything a player can see about it is derived from that id</b> — the name, the line
    /// under it, the picture and the reel it fires with are all <c>ward.{id}.name</c>,
    /// <c>ward.{id}.note</c> and <c>Siege/Wards/{id}_{colour}</c>. That is invariant 5a's rule:
    /// anything holding an id can draw the turret without reading the catalog it came from, and no
    /// key is ever built by concatenation at a call site.
    /// </para>
    /// <para>
    /// <b>Two prices and only one of them has a gate, which is the owner's rule and matches the
    /// grove's.</b> Credits are what the game pays out for playing, so a credit price is a
    /// <em>reward</em> and carries a keeper level with it; gems are bought or earned slowly, so a
    /// gem price is a <em>shortcut</em> and asks nothing but the gems. That is invariant 15a's
    /// shape — the level gate is permission to pay rather than a second way to be handed the thing
    /// — and 16j's, where the same floor is priced two ways up one ladder.
    /// </para>
    /// </summary>
    public sealed class WardModel
    {
        /// <summary>Permanent. The save keys on it. Lower case, no spaces.</summary>
        public readonly string Id;

        /// <summary>What it does beyond putting a bolt into a raider.</summary>
        public readonly WardAbility Ability;

        /// <summary>
        /// How strong the ability is, in tenths of whatever it measures: tenths of a baseline bolt
        /// for the ones that deal damage, tenths of a march for frost, tenths of a fuel unit for
        /// siphon, tenths of a capacity for a beacon, tenths of a bolt bitten deeper into plating
        /// for a rend — and for a prism alone it is not tenths at all but a <em>count</em>, of how
        /// many colours besides its own it is strong against.
        ///
        /// <b>Tenths throughout, and never a float.</b> Everything this multiplies ends up in a
        /// graded number, and a threshold decided by a float is a number three code generators
        /// round three ways — this project has paid for that once already
        /// (<c>Mathf.CeilToInt(45 * 1.20f)</c>).
        /// </summary>
        public readonly int Magnitude;

        /// <summary>
        /// A second number the ability needs, or nought: how many extra raiders a chain reaches,
        /// how many seconds a frost or an ember lasts in tenths, how often a pierce comes round.
        ///
        /// <b>One field rather than one per ability</b>, because a per-ability record would make
        /// <c>WardCatalog</c> a union type and every reader a switch. What it means is documented
        /// on the ability, which is where a reader is already looking.
        /// </summary>
        public readonly int Extent;

        /// <summary>
        /// What this turret's bolt is worth, in tenths of the baseline one.
        ///
        /// <para>
        /// <b>Ten is the floor and the data cannot express less</b> — <see cref="Power"/> clamps
        /// it — which is the same rule <see cref="WardAbility"/> is built around, moved onto the
        /// one number that could otherwise break it. A siege's par is the hill's health over
        /// <c>SiegeTuning.PerfectMatch</c>, computed against the <em>baseline</em> bolt, so a
        /// turret that hit softer would need more matches than par assumes and push three stars
        /// out of reach of whoever bought it. That is a grade decided by a purchase, and a grade
        /// reaches a public leaderboard (invariant 19a).
        /// </para>
        /// <para>
        /// <b>So a roster where turrets differ in damage is built upward from the baseline, never
        /// down from an average.</b> What a player reads as "the mortar hits softer than the
        /// cleaver" is the mortar sitting <em>at</em> the floor while the cleaver stands above it
        /// — the same spread on the card, and no level's par moves. Above only ever makes par
        /// over-state what a good run needs, which is the direction invariant 22 says to err in
        /// and what invariant 37w already accepted when cogs shipped.
        /// </para>
        /// </summary>
        public readonly int PowerTenths;

        /// <summary>
        /// How much this turret can take before it falls, in tenths of the baseline.
        ///
        /// <para>
        /// <b>This one is allowed below ten, and that asymmetry is the whole trade.</b> Health
        /// reaches nothing that is graded: par is counted in matches, the star lines are multiples
        /// of par, and what a run is worth does not ask whether the line survived comfortably. So
        /// a turret may be bought that hits harder and falls sooner, and a player who stands four
        /// of them has made a choice they can be wrong about — which is what invariant 26h asks of
        /// anything on this shelf.
        /// </para>
        /// <para>
        /// <b>Bounded rather than free, because the one thing it must not do is make a rung
        /// impossible.</b> <c>SiegeTuning.LeastGuardTenths</c> is the floor and
        /// <c>SiegeRuleTests</c> plays the whole chapter with a line of the flimsiest turret in
        /// the roster: measuring says a fragile line is a harder game, and only that says it is
        /// still a game.
        /// </para>
        /// </summary>
        public readonly int GuardTenths;

        /// <summary>What one costs in gems. Nought means it cannot be bought with gems.</summary>
        public readonly int GemPrice;

        /// <summary>What one costs in credits. Nought means it cannot be bought with credits.</summary>
        public readonly int CoinPrice;

        /// <summary>
        /// The keeper level a credit purchase asks for. Ignored by a gem purchase.
        ///
        /// <b>Nought is a real answer</b> and is how a starter says it asks nothing.
        /// </summary>
        public readonly int MinLevel;

        /// <summary>
        /// Whether this one is handed over rather than bought.
        ///
        /// <b>Derived from having no price at all, and read through <see cref="IsStarter"/> rather
        /// than tested at a call site.</b> That is invariant 16j's hard-won correction: "free" was
        /// <c>Cost &lt;= 0</c> for as long as there was one currency, and the day a second one
        /// arrived every gem-priced region read as free and half the world was handed over at
        /// launch. Both prices are asked here, once.
        /// </summary>
        public bool IsStarter => GemPrice <= 0 && CoinPrice <= 0;

        /// <summary>Whether gems can buy it outright, with no keeper level asked.</summary>
        public bool ForGems => GemPrice > 0;

        /// <summary>Whether credits can buy it, once the keeper level is reached.</summary>
        public bool ForCoins => CoinPrice > 0;

        /// <summary>Where it sits on the shelf. Authored, for <c>HomesteadRegion.Order</c>'s reason.</summary>
        public readonly int Order;

        /// <summary>
        /// Whether this turret wears no colour: it stands on any seat, it was bought once rather
        /// than once per seat, and it fires at everything on the hill.
        ///
        /// <para>
        /// <b>Three consequences of one fact, which is why it is one field.</b> Every other turret
        /// is bought for a colour, drawn in that colour and fires only at that colour
        /// (<see cref="WardHolding"/>, <see cref="ArtFor"/>, <c>SiegeWard.ReachTenths</c>). A
        /// legendary is the negation of all three at once, and spelling it three times is three
        /// predicates that can come to disagree — which is precisely how <c>IsStarter</c> was got
        /// wrong (invariant 16j: "free" was <c>Cost &lt;= 0</c> until a second currency arrived).
        /// </para>
        /// <para>
        /// <b>Authored rather than read off the band, and the band is the reason.</b>
        /// <see cref="WardTier"/> says in as many words that a band is not a price and not a stat;
        /// deriving "this turret ignores the colour lock" from "its order is at least twenty-one"
        /// would make the shelf's punctuation into the mode's central rule, and a re-rung shelf
        /// would then silently change what four turrets <em>do</em>. So content says so, and both
        /// content gates refuse a legendary outside the legendary band and anything else inside it
        /// (<see cref="WardCatalog.LadderProblem"/>).
        /// </para>
        /// <para>
        /// <b>It is still strictly an addition, which is what keeps it out of par's way.</b> Par
        /// is the hill's health over a perfect match computed against the baseline bolt
        /// (<see cref="PowerTenths"/>): a turret that reaches every colour only ever fires bolts
        /// it would otherwise not have fired, so a run ends sooner and par over-states what a good
        /// one needs — the direction invariant 22 says to err in. Nothing about a star line moves.
        /// </para>
        /// </summary>
        public readonly bool Legendary;

        /// <summary>
        /// Whether this turret's pictures and reels are cut once rather than once per ward colour.
        ///
        /// <b>Named apart from <see cref="Legendary"/> even though it answers the same today</b>,
        /// because what a reader at an address wants to know is "is this one picture or four" and
        /// what a reader at the hill wants to know is "does the colour lock hold". Two names, one
        /// fact, and the day they stop being one fact there is a place to say so.
        /// </summary>
        public bool Colourless => Legendary;

        public WardModel(string id, WardAbility ability, int magnitude, int extent,
                         int gemPrice, int coinPrice, int minLevel, int order,
                         int powerTenths = Baseline, int guardTenths = Baseline,
                         bool legendary = false)
        {
            Id = id ?? string.Empty;
            Ability = ability;
            Magnitude = magnitude < 0 ? 0 : magnitude;
            Extent = extent < 0 ? 0 : extent;
            GemPrice = gemPrice < 0 ? 0 : gemPrice;
            CoinPrice = coinPrice < 0 ? 0 : coinPrice;
            MinLevel = minLevel < 0 ? 0 : minLevel;
            Order = order;
            Legendary = legendary;

            // **Clamped here rather than checked at a call site**, so "no turret hits softer than
            // the baseline" is a fact about the type and not a rule somebody has to remember. A
            // content push that authors nought gets the baseline, which is what an older file and
            // a rolled-back client both mean by leaving the field out.
            PowerTenths = powerTenths < Baseline ? Baseline : powerTenths;
            GuardTenths = guardTenths <= 0 ? Baseline : guardTenths;
        }

        /// <summary>
        /// The tenths a turret that is neither tougher nor harder-hitting than the yardstick
        /// carries. The starter's, and what an unauthored field means.
        /// </summary>
        public const int Baseline = 10;

        /// <summary>What its bolt is worth against the baseline: 1.0 for the starter.</summary>
        public float Power => PowerTenths / (float)Baseline;

        /// <summary>What it can take against the baseline: 1.0 for the starter.</summary>
        public float Guard => GuardTenths / (float)Baseline;

        /// <summary>The loc key for its name. Derived, never authored (invariant 5a).</summary>
        public string NameKey => "ward." + Id + ".name";

        /// <summary>The loc key for the one line saying what it does.</summary>
        public string NoteKey => "ward." + Id + ".note";

        /// <summary>
        /// The address of the turret standing in the line, in this colour.
        ///
        /// <para>
        /// <b>Built rather than written out as a literal, which is the one place this project
        /// allows that and says why.</b> <c>Tools/verify/artnames.py</c> reads literals off a call
        /// site, so a built name is a name it cannot check — and twenty models times four colours
        /// is eighty literals nobody would keep in step with the catalog anyway. What replaces the
        /// literal is a stronger gate rather than a weaker one: the roster is <em>content</em>, so
        /// <c>ContentValidation</c> and <c>content.py</c> both walk it and error on a model whose
        /// addresses <c>AssetManifest</c> cannot name — which catches a missing picture and a
        /// misspelled id at once, where a literal only ever catches the second.
        /// </para>
        /// <para>
        /// <b>A legendary answers one address for every colour</b>, because it wears none
        /// (<see cref="Legendary"/>) — one picture and one recoil rather than four of each. Every
        /// caller here already takes a colour and none of them has to learn about it, which is the
        /// whole reason the decision lives on the model: the board, the shelf, the preview stage
        /// and both content gates all ask this and get the right answer without a branch.
        /// </para>
        /// </summary>
        public string ArtFor(char colour)
            => Colourless ? "Wards/" + Id : "Wards/" + Id + "_" + colour;

        /// <summary>The reel it fires with, in this colour. See <see cref="ArtFor"/>.</summary>
        public string FireFor(char colour)
            => Colourless ? "Wards/" + Id + "_fire" : "Wards/" + Id + "_" + colour + "_fire";

        /// <summary>
        /// The one turret that draws the shared <c>shot_{colour}</c> reels rather than four of its
        /// own — a fireball, in whichever colour its ward burns.
        ///
        /// <para>
        /// <b>It was four different elements and the owner had it made one.</b> The set began as
        /// fire, venom, ice and lightning, one per ward colour, which was right while the
        /// <em>starter</em> threw them: a line of four starters put four silhouettes in the air at
        /// once. Once the set moved onto a bought turret only this model drew it, so the four never
        /// appeared together except on a player who had bought the same turret for all four seats —
        /// where they read as four unrelated weapons wearing one name. Reported off the preview
        /// panel as the red Breaker doing a different animation from the orange one. See
        /// <c>SiegeShotBake.Shots</c>.
        /// </para>
        ///
        /// <para>
        /// <b>It has now moved twice, which is the argument for naming it rather than deriving
        /// it.</b> The owner put the elemental set on `rime` and has since moved it to `breaker`,
        /// giving both frost rungs a white snowball instead - so the turret that draws the four
        /// elements is a <em>rend</em> turret today and nothing about a model says so.
        ///
        /// <b>Named outright, because it stopped being derivable.</b> It used to be read off the
        /// ability — a turret with no ability has nothing to depict, so it fired what the colour
        /// fired — and that was honest for exactly as long as the starter was the only model
        /// without one. The owner moved the elemental set onto a bought turret and gave the
        /// starter a single effect of its own, and no property of a model tells you that: it is a
        /// decision about art, so it is written down as one rather than smuggled into a predicate
        /// that means something else.
        /// </para>
        /// <para>
        /// <b>Never <see cref="IsStarter"/>, whatever the roster looks like on the day.</b> That
        /// is invariant 16j's trap: "free" was <c>Cost &lt;= 0</c> for a year and the day a second
        /// currency arrived every gem-priced thing in the game read as free. Under that spelling a
        /// roster handing out a second free turret would silently take its projectile away — and
        /// it would be wrong today in any case, since the turret with the elemental set is one
        /// somebody pays credits for.
        /// </para>
        /// </summary>
        public const string Elemental = "breaker";

        /// <summary>
        /// Whether this turret throws one effect of its own in four colours, rather than the four
        /// elemental bolts. See <see cref="Elemental"/>.
        /// </summary>
        public bool OwnShot => Id != Elemental;

        /// <summary>
        /// The bolt this turret puts in the air, as an <c>Fx/Siege</c> key.
        ///
        /// <para>
        /// <b>Per turret <em>and</em> per colour, which is dearer than it looks and was settled by
        /// looking rather than by arithmetic.</b> A bleached reel — white, with all its brightness
        /// in coverage — would cost a quarter as much and could be worn in any colour by one
        /// multiply. That is what this shipped as first, and held up beside the elemental fireball
        /// it was a flat smear: a multiply can only vary <em>value</em>, and what makes these
        /// effects read is variation in <em>hue</em>. Invariant 37l, met from a third direction.
        /// </para>
        /// <para>
        /// <b>What keeps it affordable is invariant 7b's own bargain.</b> A run stands four
        /// turrets, so <c>WardLine.Art</c> scopes twelve reels and never the roster's; the other
        /// fifteen models cost the download and never the device.
        /// </para>
        /// <para>
        /// Derived from the id and never authored, which is invariant 5a's rule: anything holding
        /// a model can name its art without reading the catalog it came from. The colour is
        /// suffixed rather than prefixed so that a turret's four reels sort together, which is the
        /// order somebody reads a contact sheet in.
        /// </para>
        /// </summary>
        public string ShotFor(char colour) => Reel("shot", colour);

        /// <summary>The flash it throws as it lets one go. See <see cref="ShotFor"/>.</summary>
        public string MuzzleFor(char colour) => Reel("muzzle", colour);

        /// <summary>What its bolt does when it arrives. See <see cref="ShotFor"/>.</summary>
        public string HitFor(char colour) => Reel("hit", colour);

        /// <summary>
        /// One of this turret's three reels, in one colour.
        ///
        /// <b>A turret with no ability falls back to the colour's own</b>, which is not a special
        /// case so much as the honest reading: these effects depict what a turret <em>does</em>,
        /// and one that does nothing beyond firing has nothing to depict. Those four are the
        /// elemental bolts the starter has always thrown.
        /// </summary>
        /// <b>And a legendary's three are cut once</b>, for <see cref="ArtFor"/>'s reason: it
        /// wears no ward colour, so a per-colour reel would be the same picture written four
        /// times and four claims on a scope for one thing on the screen (invariant 7b).
        string Reel(string kind, char colour)
            => !OwnShot ? kind + "_" + colour
             : Colourless ? kind + "_" + Id
             : kind + "_" + Id + "_" + colour;

        /// <summary>
        /// The flame a raider wears while an ember turret's fire is on it, as an
        /// <c>Fx/Siege</c> key — one reel per <em>ward colour</em>.
        ///
        /// <para>
        /// <b>Per colour and not per model, which is the one place this family differs from
        /// <see cref="ShotFor"/>.</b> A bolt depicts what a <em>turret</em> does and there are
        /// three ember rungs, so three reels would be three pictures of one thing; a burn is
        /// something the <em>raider</em> is wearing, and what a player has to read off it is
        /// which of their four seats is paying for it. Four reels serve every ember turret that
        /// will ever ship, and a fourth rung of the family costs no art at all.
        /// </para>
        /// <para>
        /// <b>Static because a burn outlives the model that lit it.</b>
        /// <c>SiegeRaider.BurnFrom</c> names a ward, and a ward can fall while its fire is still
        /// on the hill — so the view asks this with a colour in hand rather than with a turret,
        /// which is the only thing it is ever certain of.
        /// </para>
        /// <para>
        /// <b>Not tinted from one white reel</b>, which is the correction invariant 37l records
        /// and this file already pays for at <see cref="ShotFor"/>: <c>Image.color</c> is a
        /// multiply, so it can only vary value, and what makes fire read is variation in
        /// <em>hue</em> across its own body — a cold outer tongue, a hot middle, a white core.
        /// A bleached flame worn in four colours is four flat smears.
        /// </para>
        /// </summary>
        public static string BurnFor(char colour) => "burn_" + colour;

        /// <summary>The one uncoloured picture the shelf browses it with.</summary>
        public string Thumb => "Wards/" + Id;

        public override string ToString()
            => Id + " (" + WardAbilities.NameOf(Ability) + " " + Magnitude + "/" + Extent
             + ", power " + PowerTenths + ", guard " + GuardTenths
             + (Legendary ? ", legendary" : "") + ")";
    }
}
