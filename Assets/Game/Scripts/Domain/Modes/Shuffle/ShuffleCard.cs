using System;
using System.Collections.Generic;
using GlimmerGrove.Modes;

namespace GlimmerGrove.Shuffle
{
    /// <summary>
    /// How rare a card is, in the order a hand is sorted and a deal is weighted.
    ///
    /// <b>Four tiers and the weights are the mode's</b> (<see cref="ShuffleDeck.Weights"/>), never
    /// content: a card's tier decides its colour, its plate and how often it is dealt, and a
    /// build that could retune rarity would be a second place this lane's difficulty is decided.
    /// </summary>
    public enum ShuffleTier
    {
        Common = 0,
        Rare = 1,
        Epic = 2,
        Legendary = 3,
    }

    /// <summary>
    /// What one card does, as the figure on <see cref="SiegeBoosts"/> it moves. Every member is
    /// one arm in <see cref="ShuffleBuild.Fold"/>; an <c>enum</c> rather than a lambda per card so
    /// a card is a row of data and the fold is one readable switch.
    /// </summary>
    public enum ShuffleEffect
    {
        Damage, Fire, FuelShot, FuelGem, Guard, Capacity, Armour, Regen, Crit, Rest,
        Splash, Chain, Pierce, Frost, Burn, Stun, Siphon, Charges, Thorns,
        WaveFuel, Leech, HillPace, Heavy,
        SecondWind, WaveStill, Repel, Execute, Twin, OffColour, Blast, Hex, WaveCharges,
        Phoenix, Overdrive, Spill, Desperate,

        // The attack cards (`SiegeBoard.Arsenal`): each changes *how* the line attacks.
        Scatter, Ricochet, Missiles, Quake, Toxic, Tesla,
        SpinUp, Shatter, Wildfire, Meteor, Vortex,
        Nuke, Ray, Hydra, Wild, BulletHell,
    }

    /// <summary>
    /// One upgrade a Shuffle hand can offer: a permanent id, a tier, what it does, how strong one
    /// copy is and how many copies a build may hold.
    ///
    /// <para>
    /// <b>The id is permanent and invariant 1 reaches it</b>: analytics names the cards a run
    /// held, and every string a player reads about a card is derived from its id
    /// (<see cref="NameKey"/>, <see cref="NoteKey"/>, <see cref="Icon"/>) - invariant 5a's rule, so
    /// nothing holding a card has to read the catalog it came from. Retiring a card is a line in
    /// <see cref="ShuffleCards.Retired"/>, never a reuse.
    /// </para>
    /// <para>
    /// <b>Magnitude is per copy and the fold is linear in copies</b> except where a card says
    /// otherwise in <see cref="ShuffleBuild.Fold"/>, so <c>Most</c> is the whole of what bounds a
    /// build: a card held three times is three folds of the same row.
    /// </para>
    /// </summary>
    public sealed class ShuffleCard
    {
        public readonly string Id;
        public readonly ShuffleTier Tier;
        public readonly ShuffleEffect Effect;

        /// <summary>What one copy is worth, in the unit the effect reads (per cent, tenths, points, seconds x10).</summary>
        public readonly int Magnitude;

        /// <summary>A second figure some effects need (a reach, a cadence, seconds x10), or nought.</summary>
        public readonly int Extent;

        /// <summary>How many copies a build may hold. One for the cards that only make sense once.</summary>
        public readonly int Most;

        /// <summary>Which of the skill-icon pack's hundred pictures this card wears. See <see cref="Icon"/>.</summary>
        public readonly int Picture;

        public ShuffleCard(string id, ShuffleTier tier, ShuffleEffect effect, int magnitude,
                           int extent, int most, int picture)
        {
            Id = id ?? string.Empty;
            Tier = tier;
            Effect = effect;
            Magnitude = magnitude;
            Extent = extent;
            Most = most < 1 ? 1 : most;
            Picture = picture;
        }

        /// <summary>The loc key for its name. Derived, never authored (invariant 5a).</summary>
        public string NameKey => "shuffle." + Id + ".name";

        /// <summary>The loc key for the one line saying what it does.</summary>
        public string NoteKey => "shuffle." + Id + ".note";

        /// <summary>
        /// Its picture's address under <c>Art/</c>: <c>Ui/Shuffle/{id}</c>, cut by
        /// <c>Tools/make_shuffle_art.py</c> from the skill-icon pack at <see cref="Picture"/>.
        ///
        /// <b>Built rather than written out as a literal</b>, which is the one place this project
        /// allows that and says why (<c>WardModel.ArtFor</c>): forty literals nobody keeps in step
        /// with the catalog is a weaker gate than one test walking the catalog against the
        /// manifest and the disk (<c>ShuffleArtTests</c>).
        /// </summary>
        public string Icon => "Ui/Shuffle/" + Id;

        public override string ToString() => Id + " (" + Tier + ", x" + Most + ")";
    }

    /// <summary>
    /// Every card a Shuffle hand can deal. <b>Code, never content</b>: a card is a rule about what
    /// the board does, and a rule is a build (invariant 20).
    ///
    /// <para>
    /// <b>Fifty-two cards in four tiers, and what separates them is <em>what they take</em></b>
    /// (invariant 37z's rule about bosses, asked of upgrades): the common tier moves a number the
    /// player already has - weight, cadence, fuel, health; the rare tier grants the turrets an
    /// ability the shelf sells or a new way to attack; the epic tier changes a <em>rule</em> - a
    /// fallen post stands up, a wave stops the hill, a ward reaches off its colour, a frozen body
    /// shatters; and a legendary changes the shape of a run. Two cards that moved the same figure
    /// by different amounts would be one card at two prices (invariant 5d), which is why there is
    /// one damage card and not three.
    /// </para>
    /// <para>
    /// <b>The attack cards (2026-10-09, the owner's brief: "truly unique upgrade types ... the
    /// mode should get crazy")</b> each change <em>how</em> the line attacks rather than how hard,
    /// and each is drawn as itself (<c>SiegeVia</c>, <c>SiegeView.Arsenal</c>): pellets fanning
    /// off the barrel, a ricochet, missiles, a quake down a row, toxic pools, tesla coils that
    /// fight dry, spin-up to a machine gun, a shatter, wildfire, meteors, a vortex, a nuke, a
    /// fused ray, a hydra's heads, wild magic. Several are built to <em>meet</em> - frost and a
    /// shatter, a burn and wildfire, a vortex gathering a crowd for a quake or a meteor - which
    /// is where the combinations come from. Their rules are <c>SiegeBoard.Arsenal</c>'s, shut on
    /// every board but this lane's.
    /// </para>
    /// <para>
    /// <b>Nothing here touches the hill's ground.</b> The lane deals no cogs and sends no bombers
    /// (the owner's call, 2026-10-09: nothing drops on the hill in Shuffle - the hand is the
    /// whole ladder), so a card about cogs or bombs would be a card that does nothing, and a
    /// card that does nothing is refused by invariant 5d before it is written.
    /// </para>
    /// <para>
    /// <b>Order is the contract for nothing</b>: a hand is dealt by tier and by a roll, so this
    /// list may be reordered freely. What may not change is an id, a tier or a picture of a card
    /// that shipped, because a player's build is named by id in analytics and drawn by its
    /// picture from a scope.
    /// </para>
    /// </summary>
    public static class ShuffleCards
    {
        public static readonly ShuffleCard[] All =
        {
            // ------------------------------------------------------------ common: more of a number
            new ShuffleCard("heavy_bolts",   ShuffleTier.Common, ShuffleEffect.Damage,   15, 0, 5, 84),
            new ShuffleCard("quick_barrels", ShuffleTier.Common, ShuffleEffect.Fire,     12, 0, 4, 63),
            new ShuffleCard("lean_burn",     ShuffleTier.Common, ShuffleEffect.FuelShot, 12, 0, 4, 18),
            new ShuffleCard("rich_gems",     ShuffleTier.Common, ShuffleEffect.FuelGem,  20, 0, 4, 20),
            new ShuffleCard("iron_posts",    ShuffleTier.Common, ShuffleEffect.Guard,    30, 0, 4, 76),
            new ShuffleCard("wide_tubes",    ShuffleTier.Common, ShuffleEffect.Capacity, 25, 0, 3, 72),
            new ShuffleCard("brace",         ShuffleTier.Common, ShuffleEffect.Armour,    1, 0, 2, 68),
            new ShuffleCard("field_medic",   ShuffleTier.Common, ShuffleEffect.Regen,    60, 0, 3, 21),
            new ShuffleCard("keen_eyes",     ShuffleTier.Common, ShuffleEffect.Crit,     10, 200, 5, 56),
            new ShuffleCard("long_rest",     ShuffleTier.Common, ShuffleEffect.Rest,     20, 0, 2, 13),

            // ------------------------------------------------------------ rare: an ability granted
            new ShuffleCard("splash_shot",     ShuffleTier.Rare, ShuffleEffect.Splash,      4, 1, 3, 33),
            new ShuffleCard("chain_arc",       ShuffleTier.Rare, ShuffleEffect.Chain,       5, 1, 3, 70),
            new ShuffleCard("pierce_rounds",   ShuffleTier.Rare, ShuffleEffect.Pierce,      7, 3, 2, 67),
            new ShuffleCard("frostbite",       ShuffleTier.Rare, ShuffleEffect.Frost,       3, 15, 3, 59),
            new ShuffleCard("kindling",        ShuffleTier.Rare, ShuffleEffect.Burn,        4, 30, 3, 31),
            new ShuffleCard("stunning_blows",  ShuffleTier.Rare, ShuffleEffect.Stun,       12, 5, 3, 92),
            new ShuffleCard("siphon",          ShuffleTier.Rare, ShuffleEffect.Siphon,     10, 0, 3, 42),
            new ShuffleCard("charge_master",   ShuffleTier.Rare, ShuffleEffect.Charges,    50, 1, 2, 71),
            new ShuffleCard("thorns",          ShuffleTier.Rare, ShuffleEffect.Thorns,    100, 0, 3, 36),
            new ShuffleCard("rally_cry",       ShuffleTier.Rare, ShuffleEffect.WaveFuel,    3, 0, 3, 28),
            new ShuffleCard("vampire_posts",   ShuffleTier.Rare, ShuffleEffect.Leech,       1, 0, 2, 83),
            new ShuffleCard("slow_march",      ShuffleTier.Rare, ShuffleEffect.HillPace,   15, 0, 2, 41),
            new ShuffleCard("giant_slayer",    ShuffleTier.Rare, ShuffleEffect.Heavy,      50, 0, 2, 97),

            // ------------------------------------------------------------ rare: a new way to attack
            new ShuffleCard("scatter_shot",    ShuffleTier.Rare, ShuffleEffect.Scatter,     4, 2, 3, 61),
            new ShuffleCard("ricochet",        ShuffleTier.Rare, ShuffleEffect.Ricochet,    8, 1, 3, 88),
            new ShuffleCard("missile_pod",     ShuffleTier.Rare, ShuffleEffect.Missiles,   10, 4, 3, 82),
            new ShuffleCard("quake_rounds",    ShuffleTier.Rare, ShuffleEffect.Quake,       6, 5, 3, 34),
            new ShuffleCard("toxic_rounds",    ShuffleTier.Rare, ShuffleEffect.Toxic,       2, 30, 3, 48),
            new ShuffleCard("tesla_coil",      ShuffleTier.Rare, ShuffleEffect.Tesla,       6, 1, 3, 65),

            // ------------------------------------------------------------ epic: a rule changed
            new ShuffleCard("second_wind",     ShuffleTier.Epic, ShuffleEffect.SecondWind,  1, 0, 3, 23),
            new ShuffleCard("hourglass_heart", ShuffleTier.Epic, ShuffleEffect.WaveStill,  20, 0, 2, 74),
            new ShuffleCard("repelling_wards", ShuffleTier.Epic, ShuffleEffect.Repel,      35, 0, 2, 73),
            new ShuffleCard("execution",       ShuffleTier.Epic, ShuffleEffect.Execute,    15, 0, 2, 91),
            new ShuffleCard("twin_barrels",    ShuffleTier.Epic, ShuffleEffect.Twin,       25, 5, 3, 26),
            new ShuffleCard("sure_sight",      ShuffleTier.Epic, ShuffleEffect.OffColour,   5, 0, 1, 38),
            new ShuffleCard("chain_reaction",  ShuffleTier.Epic, ShuffleEffect.Blast,       3, 0, 2, 40),
            new ShuffleCard("cursed_fire",     ShuffleTier.Epic, ShuffleEffect.Hex,         6, 30, 2, 3),
            new ShuffleCard("storm_bank",      ShuffleTier.Epic, ShuffleEffect.WaveCharges, 1, 0, 2, 19),
            new ShuffleCard("spin_up",         ShuffleTier.Epic, ShuffleEffect.SpinUp,     15, 10, 2, 62),
            new ShuffleCard("shatter",         ShuffleTier.Epic, ShuffleEffect.Shatter,     4, 1, 2, 60),
            new ShuffleCard("wildfire",        ShuffleTier.Epic, ShuffleEffect.Wildfire,    3, 1, 2, 32),
            new ShuffleCard("meteor_call",     ShuffleTier.Epic, ShuffleEffect.Meteor,     30, 60, 2, 35),
            new ShuffleCard("vortex",          ShuffleTier.Epic, ShuffleEffect.Vortex,      3, 90, 2, 16),

            // ------------------------------------------------------------ legendary: a run's shape
            new ShuffleCard("phoenix",      ShuffleTier.Legendary, ShuffleEffect.Phoenix,   1, 0, 1, 90),
            new ShuffleCard("overdrive",    ShuffleTier.Legendary, ShuffleEffect.Overdrive, 40, 25, 1, 94),
            new ShuffleCard("rainbow_fuel", ShuffleTier.Legendary, ShuffleEffect.Spill,     25, 0, 1, 6),
            new ShuffleCard("last_stand",   ShuffleTier.Legendary, ShuffleEffect.Desperate, 60, 30, 1, 79),
            new ShuffleCard("doomsday",     ShuffleTier.Legendary, ShuffleEffect.Nuke,      30, 15, 1, 98),
            new ShuffleCard("death_ray",    ShuffleTier.Legendary, ShuffleEffect.Ray,        6, 100, 1, 39),
            new ShuffleCard("hydra",        ShuffleTier.Legendary, ShuffleEffect.Hydra,      5, 2, 1, 93),
            new ShuffleCard("wild_magic",   ShuffleTier.Legendary, ShuffleEffect.Wild,     100, 0, 1, 10),
            new ShuffleCard("bullet_hell",  ShuffleTier.Legendary, ShuffleEffect.BulletHell, 100, 50, 1, 81),
        };

        /// <summary>
        /// Ids spent by cards that were withdrawn. Refused by <see cref="Find"/> and held apart
        /// from <see cref="All"/> by <c>ShuffleCardTests</c>, for the lesson ids' reason: a card id
        /// travels in analytics and must never name a second thing.
        ///
        /// <para>
        /// <b><c>charm_magnet</c></b> (2026-10-09, the owner: "I don't like it") - a card about the
        /// charm window, withdrawn with the figure it moved: <c>SiegeBoosts</c> no longer has a
        /// charm window, so nothing can reach one. Its picture, 15, is spent with it.
        /// </para>
        /// </summary>
        public static readonly string[] Retired = { "charm_magnet" };

        /// <summary>The card with this id, or null for none this build knows.</summary>
        public static ShuffleCard Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            for (int i = 0; i < All.Length; i++)
                if (string.Equals(All[i].Id, id, StringComparison.Ordinal)) return All[i];

            return null;
        }

        /// <summary>Every card of one tier, in catalog order.</summary>
        public static List<ShuffleCard> Of(ShuffleTier tier)
        {
            var found = new List<ShuffleCard>(16);
            for (int i = 0; i < All.Length; i++)
                if (All[i].Tier == tier) found.Add(All[i]);

            return found;
        }
    }
}
