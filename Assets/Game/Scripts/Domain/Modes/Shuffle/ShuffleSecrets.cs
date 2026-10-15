using System;
using System.Collections.Generic;
using GlimmerGrove.Modes;

namespace GlimmerGrove.Shuffle
{
    /// <summary>Whether a bond is a secret (several cards held together) or an evolution (one card held to its most).</summary>
    public enum ShuffleBondKind
    {
        Secret = 0,
        Evolution = 1,
    }

    /// <summary>
    /// <b>A secret or an evolution</b> (MODES.md 59n, the owner, 2026-10-14: "some combination of
    /// cards should enable unique, strong and fun abilities ... stacking some cards should give a
    /// superior effect ... hard to do"). A bond is unlocked by the cards a build holds and
    /// grants one <see cref="SiegePower"/>, which the board plays (<c>SiegeBoard.Secrets</c>).
    ///
    /// <para>
    /// <b>A secret is two, three or four different cards held together</b> - rare, epic and
    /// legendary by how many - and <b>an evolution is one card held <see cref="Needs"/> times</b>,
    /// which is that card's most, so a run has to commit to it. <b>Both are a function of the
    /// cards held</b> (59e): unlocked the moment the last part is taken, never taken away, and
    /// the same for any order of taking.
    /// </para>
    /// <para>
    /// <b>The id is permanent</b>, for a card's reason: every string is derived from it
    /// (<see cref="NameKey"/>, <see cref="NoteKey"/>) and analytics names what a run unlocked.
    /// A withdrawn bond goes in <see cref="ShuffleBonds.Retired"/>.
    /// </para>
    /// </summary>
    public sealed class ShuffleBond
    {
        public readonly string Id;
        public readonly ShuffleBondKind Kind;
        public readonly ShuffleTier Tier;

        /// <summary>The card ids it is made of: two to four for a secret, one for an evolution.</summary>
        public readonly string[] Parts;

        /// <summary>Copies of each part a build must hold: one for a secret, the card's most for an evolution.</summary>
        public readonly int Needs;

        public readonly SiegePower Power;

        /// <summary>
        /// <b>The colour of its diamond</b>: a hue in degrees and a saturation in percent of the
        /// kit gem's own. <b>Every secret has its own</b> (owner, 2026-10-14), so a green-only
        /// mark that said "some secret" now says which; every evolution shares the one purple.
        /// <c>Tools/make_shuffle_art.py</c> reads these figures from this file and turns the gem
        /// to them (CLAUDE.md 44g), so the picture and the glow drawn around it cannot disagree:
        /// a changed figure fails that tool's <c>--check</c> until the art is re-cut.
        /// </summary>
        public readonly int Hue, Saturation;

        /// <summary>The evolutions' shared diamond colour.</summary>
        public const int EvolutionHue = 276, EvolutionSaturation = 100;

        /// <summary>The diamond's address under <c>Ui/</c>: one per secret, one for every evolution.</summary>
        public const string SecretMarkStem = "shuffle_secret_", EvolutionMark = "shuffle_evolution";

        ShuffleBond(string id, ShuffleBondKind kind, ShuffleTier tier, string[] parts, int needs, SiegePower power,
                    int hue, int saturation)
        {
            Id = id ?? string.Empty;
            Kind = kind;
            Tier = tier;
            Parts = parts ?? Array.Empty<string>();
            Needs = needs < 1 ? 1 : needs;
            Power = power;
            Hue = ((hue % 360) + 360) % 360;
            Saturation = saturation < 0 ? 0 : saturation;
        }

        /// <summary>
        /// A secret: its tier is how many cards it takes (two rare, three epic, four legendary),
        /// its diamond <paramref name="hue"/> and <paramref name="saturation"/> its own.
        /// </summary>
        public static ShuffleBond Secret(string id, SiegePower power, int hue, int saturation, params string[] parts)
        {
            int n = parts != null ? parts.Length : 0;
            var tier = n >= 4 ? ShuffleTier.Legendary : n == 3 ? ShuffleTier.Epic : ShuffleTier.Rare;
            return new ShuffleBond(id, ShuffleBondKind.Secret, tier, parts, 1, power, hue, saturation);
        }

        /// <summary>An evolution of <paramref name="card"/>, unlocked at <paramref name="needs"/> copies.</summary>
        public static ShuffleBond Evolution(string id, ShuffleTier tier, string card, int needs, SiegePower power)
            => new ShuffleBond(id, ShuffleBondKind.Evolution, tier, new[] { card }, needs, power,
                               EvolutionHue, EvolutionSaturation);

        public bool IsSecret => Kind == ShuffleBondKind.Secret;

        /// <summary>Its diamond's picture, under <c>Ui/</c>. Derived from the id (8c), never authored.</summary>
        public string Mark => IsSecret ? SecretMarkStem + Id : EvolutionMark;

        /// <summary><c>shuffle.secret.{id}.name</c> or <c>shuffle.evolution.{id}.name</c>. Derived (5a).</summary>
        public string NameKey => Stem + ".name";

        /// <summary>The one line saying what it does.</summary>
        public string NoteKey => Stem + ".note";

        string Stem => "shuffle." + (IsSecret ? "secret." : "evolution.") + Id;

        /// <summary>Whether <paramref name="build"/> holds every part, each <see cref="Needs"/> times.</summary>
        public bool Completes(ShuffleBuild build)
        {
            if (build == null || Parts.Length == 0) return false;

            for (int i = 0; i < Parts.Length; i++)
                if (build.CopiesOf(Parts[i]) < Needs) return false;

            return true;
        }

        /// <summary>Whether <paramref name="card"/> is one of this bond's parts.</summary>
        public bool Names(ShuffleCard card)
        {
            if (card == null) return false;

            for (int i = 0; i < Parts.Length; i++)
                if (string.Equals(Parts[i], card.Id, StringComparison.Ordinal)) return true;

            return false;
        }

        public override string ToString() => Kind + " " + Id + " (" + Tier + ")";
    }

    /// <summary>
    /// Every secret and every evolution. <b>Code, never content</b>, for a card's reason
    /// (invariant 20): a bond is a rule about what the board does.
    ///
    /// <para>
    /// <b>Order is the contract for nothing but the order two unlocks on one take are shown</b>
    /// (<see cref="ShuffleBuild.Unlocked"/>, lowest tier first so the grandest is the last thing
    /// seen). What may never change is an id or what a shipped bond is made of.
    /// </para>
    /// </summary>
    public static class ShuffleBonds
    {
        public static readonly ShuffleBond[] All =
        {
            // ------------------------------------------------------------ rare secrets: two cards
            ShuffleBond.Secret("thunderhead", SiegePower.Thunderhead, 210, 105, "chain_arc", "tesla_coil"),
            ShuffleBond.Secret("frostfire", SiegePower.Frostfire, 160, 100, "frostbite", "kindling"),
            ShuffleBond.Secret("blade_orbit", SiegePower.BladeOrbit, 210, 15, "ricochet", "scatter_shot"),
            ShuffleBond.Secret("plague", SiegePower.Plague, 115, 105, "toxic_rounds", "splash_shot"),
            ShuffleBond.Secret("gem_comets", SiegePower.GemComets, 80, 100, "power_gems", "blast_gems"),

            // ------------------------------------------------------------ epic secrets: three cards
            ShuffleBond.Secret("permafrost", SiegePower.Permafrost, 188, 70, "frostbite", "shatter", "slow_march"),
            ShuffleBond.Secret("event_horizon", SiegePower.EventHorizon, 240, 105, "vortex", "execution", "quake_rounds"),
            ShuffleBond.Secret("fire_dragon", SiegePower.FireDragon, 0, 110, "kindling", "wildfire", "meteor_call"),
            ShuffleBond.Secret("golden_dome", SiegePower.GoldenDome, 46, 110, "second_wind", "iron_posts", "field_medic"),
            ShuffleBond.Secret("rainbow_cascade", SiegePower.RainbowCascade, 335, 105, "rich_gems", "chain_fuel", "kinetic_gems"),

            // ------------------------------------------------------------ legendary secrets: four cards
            ShuffleBond.Secret("solar_cannon", SiegePower.SolarCannon, 55, 35, "death_ray", "laser_core", "overdrive", "heavy_bolts"),
            ShuffleBond.Secret("armageddon", SiegePower.Armageddon, 22, 110, "doomsday", "meteor_call", "missile_pod", "chain_reaction"),
            ShuffleBond.Secret("time_stop", SiegePower.TimeStop, 305, 100, "hourglass_heart", "slow_march", "spin_up", "stunning_blows"),

            // ------------------------------------------------------------ evolutions: one card at its most
            ShuffleBond.Evolution("titan_shells", ShuffleTier.Rare, "heavy_bolts", 5, SiegePower.TitanShells),
            ShuffleBond.Evolution("deadeye", ShuffleTier.Rare, "keen_eyes", 5, SiegePower.Deadeye),
            ShuffleBond.Evolution("fortress", ShuffleTier.Rare, "iron_posts", 4, SiegePower.Fortress),
            ShuffleBond.Evolution("cluster_bombs", ShuffleTier.Epic, "splash_shot", 3, SiegePower.ClusterBombs),
            ShuffleBond.Evolution("ball_lightning", ShuffleTier.Epic, "chain_arc", 3, SiegePower.BallLightning),
            ShuffleBond.Evolution("swarm", ShuffleTier.Epic, "missile_pod", 3, SiegePower.Swarm),
            ShuffleBond.Evolution("diamond_dust", ShuffleTier.Epic, "shatter", 2, SiegePower.DiamondDust),
            ShuffleBond.Evolution("comet_storm", ShuffleTier.Legendary, "meteor_call", 2, SiegePower.CometStorm),
            ShuffleBond.Evolution("prism_lance", ShuffleTier.Legendary, "laser_core", 4, SiegePower.PrismLance),
        };

        /// <summary>Ids spent by bonds that were withdrawn, for <see cref="ShuffleCards.Retired"/>'s reason. None yet.</summary>
        public static readonly string[] Retired = Array.Empty<string>();

        /// <summary>The bond with this id, or null.</summary>
        public static ShuffleBond Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            for (int i = 0; i < All.Length; i++)
                if (string.Equals(All[i].Id, id, StringComparison.Ordinal)) return All[i];

            return null;
        }

        /// <summary>
        /// <b>The green diamond</b>: whether taking <paramref name="card"/> would bring a secret
        /// the build has <em>started</em> a part closer. The build holds at least one of that
        /// secret's other cards and not this one yet, and the secret is not already unlocked - so
        /// the mark says "this card goes with one you took", never "every card is part of
        /// something", which on a deck this size would mark nearly every hand.
        /// </summary>
        public static bool Advances(ShuffleCard card, ShuffleBuild build) => Advanced(card, build, null) > 0;

        /// <summary>
        /// Every secret <see cref="Advances"/> answers yes for, in catalog order, added to
        /// <paramref name="into"/> when it is given; the count either way. The hand wears one
        /// diamond per secret, each in that secret's own colour.
        /// </summary>
        public static int Advanced(ShuffleCard card, ShuffleBuild build, List<ShuffleBond> into)
        {
            if (card == null || build == null || build.CopiesOf(card.Id) > 0 || !build.Takeable(card)) return 0;

            int found = 0;
            for (int b = 0; b < All.Length; b++)
            {
                var bond = All[b];
                if (!bond.IsSecret || !bond.Names(card) || bond.Completes(build)) continue;

                for (int p = 0; p < bond.Parts.Length; p++)
                {
                    if (string.Equals(bond.Parts[p], card.Id, StringComparison.Ordinal)
                        || build.CopiesOf(bond.Parts[p]) == 0) continue;

                    found++;
                    into?.Add(bond);
                    break;
                }
            }

            return found;
        }

        /// <summary>
        /// <b>The purple diamond</b>: whether taking <paramref name="card"/> would bring its
        /// evolution a copy closer. The build already holds at least one copy, short of the
        /// evolution, and the run's own card can still reach it (a retune that lowered its most
        /// below the evolution marks nothing it could never keep).
        /// </summary>
        public static bool Evolves(ShuffleCard card, ShuffleBuild build)
        {
            if (card == null || build == null || !build.Takeable(card)) return false;

            int held = build.CopiesOf(card.Id);
            if (held < 1) return false;

            for (int b = 0; b < All.Length; b++)
            {
                var bond = All[b];
                if (bond.IsSecret || !bond.Names(card)) continue;

                if (held < bond.Needs && card.Most >= bond.Needs) return true;
            }

            return false;
        }
    }
}
