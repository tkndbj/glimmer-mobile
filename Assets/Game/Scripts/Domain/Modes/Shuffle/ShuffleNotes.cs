using System;
using System.Collections.Generic;
using GlimmerGrove.Modes;

namespace GlimmerGrove.Shuffle
{
    /// <summary>A figure of a card as its note prints it.</summary>
    public enum ShuffleFigure
    {
        /// <summary>The magnitude as written: "15%" off a magnitude of 15.</summary>
        Magnitude,

        /// <summary>The magnitude in tenths read as a per cent: "40%" off a splash of 4 tenths.</summary>
        MagnitudeTens,

        /// <summary>The extent as written.</summary>
        Extent,

        /// <summary>The two multiplied: spin-up's ceiling, fifteen per shot for ten shots, "150%".</summary>
        MagnitudeByExtent,
    }

    /// <summary>Which of a card's figures its note says in words that do not change with a number.</summary>
    [Flags]
    public enum ShuffleWorded
    {
        None = 0,
        Magnitude = 1,
        Extent = 2,
    }

    /// <summary>
    /// What a card's note says about its figures, so the note can never disagree with a retune
    /// (MODES.md 59l).
    ///
    /// <para>
    /// <b>A figure a note states is either a slot or a word.</b> A per cent is a slot: every
    /// language writes "15%" the same way whatever the number, so the note carries <c>{0}</c> and
    /// the panel fills it from the run's own card (<see cref="Args"/>). A count or a time is a
    /// word - "1 more raider", "half a second", "every 6 seconds", "Every 4th shot", the Arabic
    /// dual - because the sentence around it changes with the number in at least one of the nine
    /// languages, and a sentence that changes with its number is a translation, not a format.
    /// A worded figure is therefore <b>not tunable</b>: <c>ShuffleTuning.TryRead</c> refuses a
    /// document that moves one, naming the card, so the sentence a player reads is always true.
    /// A figure no note mentions is tunable freely within its range.
    /// </para>
    /// <para>
    /// <b>Held by</b> <c>ShuffleTuningTests</c>: every slot here is a <c>{n}</c> in every
    /// language's note and every <c>{n}</c> is a slot here, and the shipped card formats to the
    /// note every build before this one printed.
    /// </para>
    /// </summary>
    public static class ShuffleNotes
    {
        readonly struct Note
        {
            public readonly ShuffleFigure[] Slots;
            public readonly ShuffleWorded Worded;

            public Note(ShuffleWorded worded, params ShuffleFigure[] slots)
            {
                Worded = worded;
                Slots = slots ?? Array.Empty<ShuffleFigure>();
            }
        }

        const ShuffleFigure M = ShuffleFigure.Magnitude, M10 = ShuffleFigure.MagnitudeTens,
                            E = ShuffleFigure.Extent, ME = ShuffleFigure.MagnitudeByExtent;

        const ShuffleWorded None = ShuffleWorded.None, WM = ShuffleWorded.Magnitude,
                            WE = ShuffleWorded.Extent, WB = ShuffleWorded.Magnitude | ShuffleWorded.Extent;

        // Keyed by card id. A card not listed says none of its figures.
        static readonly Dictionary<string, Note> Table = new Dictionary<string, Note>(StringComparer.Ordinal)
        {
            // common
            { "heavy_bolts", new Note(None, M) },
            { "quick_barrels", new Note(None, M) },
            { "lean_burn", new Note(None, M) },
            { "rich_gems", new Note(None, M) },
            { "iron_posts", new Note(None, M) },
            { "wide_tubes", new Note(None, M) },
            { "brace", new Note(WM) },                      // "1 less damage"
            { "keen_eyes", new Note(WE, M) },               // "double damage"
            { "long_rest", new Note(None, M) },

            // rare: an ability
            { "splash_shot", new Note(None, M10) },
            { "chain_arc", new Note(WE, M10) },             // "1 more raider"
            { "frostbite", new Note(None, M10) },
            { "stunning_blows", new Note(None, M) },
            { "charge_master", new Note(WE, M) },           // "1 more"
            { "rally_cry", new Note(None, M10) },
            { "slow_march", new Note(None, M) },
            { "giant_slayer", new Note(None, M) },

            // rare: a way to attack
            { "ricochet", new Note(WE) },                   // "bounces to the nearest raider"

            // epic
            { "second_wind", new Note(WM) },                // "comes back once"
            { "repelling_wards", new Note(None, M) },
            { "execution", new Note(None, M) },
            { "twin_barrels", new Note(WE, M) },            // "half damage"
            { "sure_sight", new Note(WM) },                 // "half damage"
            { "cursed_fire", new Note(None, M) },
            { "spin_up", new Note(None, ME) },
            { "power_gems", new Note(WM) },                 // "Match 4 ... match 5"

            // the pacts: the gain, then the cost
            { "titan_pact", new Note(None, M, E) },
            { "miser_pact", new Note(None, M, E) },
            { "frenzy_pact", new Note(None, M, E) },
            { "blood_pact", new Note(None, M, E) },
            { "reckless_pact", new Note(None, M, E) },

            // the synergies
            { "kinetic_gems", new Note(None, M) },
            { "iron_cannons", new Note(None, M) },
            { "overflow", new Note(None, M) },
            { "chain_fuel", new Note(None, M) },

            // legendary
            { "phoenix", new Note(WM) },                    // "come back once"
            { "overdrive", new Note(None, M, E) },
            { "last_stand", new Note(None, E, M) },
            { "doomsday", new Note(WE, M) },                // "Every 15 kills"
            { "hydra", new Note(WB) },                      // "splits in 2 ... half"
            { "wild_magic", new Note(WM) },                 // "Every shot"
            { "bullet_hell", new Note(WB) },                // "twice as fast ... half the fuel"
            { "grand_hand", new Note(WM) },                 // "5 cards instead of 3"
            { "laser_core", new Note(None, M10) },

            // the schools (MODES.md 59r)
            { "focus_lens", new Note(None, ME) },           // the beam at its fullest
            { "photon_heart", new Note(WE) },               // "2 more raiders"
            { "rot", new Note(None, M) },
            { "blight_heart", new Note(None, M) },
            { "brittle", new Note(None, M) },
        };

        /// <summary>The figures <paramref name="card"/>'s note prints, slot by slot.</summary>
        public static IReadOnlyList<ShuffleFigure> Slots(ShuffleCard card)
            => card != null && Table.TryGetValue(card.Id, out var note) ? note.Slots : Array.Empty<ShuffleFigure>();

        /// <summary>Which of <paramref name="card"/>'s figures its note says in words, and so may not be tuned.</summary>
        public static ShuffleWorded Worded(ShuffleCard card)
            => card != null && Table.TryGetValue(card.Id, out var note) ? note.Worded : ShuffleWorded.None;

        /// <summary>
        /// What fills <paramref name="card"/>'s note: its own figures, so a tuned card reads its
        /// tuned numbers. Empty for a note with no slot.
        /// </summary>
        public static object[] Args(ShuffleCard card) => Args(card, 0);

        /// <summary>
        /// What fills <paramref name="card"/>'s note on a hand dealt to a build already holding
        /// <paramref name="held"/> copies. <b>A note that states a level</b> - a chance, a threshold,
        /// a ceiling - prints the level the build reaches by taking this copy (a second Execution
        /// reads 25%, not 15%), read off <see cref="Total"/>; a note that states a gain ("15% more
        /// damage") prints the gain, which is what another copy adds.
        /// </summary>
        public static object[] Args(ShuffleCard card, int held)
        {
            var slots = Slots(card);
            if (slots.Count == 0) return Array.Empty<object>();

            var args = new object[slots.Count];
            for (int i = 0; i < slots.Count; i++) args[i] = Total(card, slots[i], held + 1);

            return args;
        }

        /// <summary>
        /// The figure a note prints at <paramref name="copies"/> copies. A level is not worked out
        /// here: it is read off the boosts a build holding that many copies folds
        /// (<c>ShuffleBuild.Fold</c>, the one place a card's stacking is written), so the note
        /// cannot disagree with the board. Every other figure prints as one copy.
        /// </summary>
        public static int Total(ShuffleCard card, ShuffleFigure figure, int copies)
        {
            if (copies > 1 && Level(card.Effect) != null)
            {
                var build = new ShuffleBuild(0u);
                for (int i = 0; i < copies; i++) build.Take(card);

                return Level(card.Effect)(build.Boosts);
            }

            return Of(card, figure);
        }

        /// <summary>
        /// The boost a level-stating note reads, in the note's own unit; null for a note that
        /// states a gain, which each copy adds again and so prints as one copy.
        /// </summary>
        static Func<SiegeBoosts, int> Level(ShuffleEffect effect)
        {
            switch (effect)
            {
                case ShuffleEffect.Crit: return b => b.CritChance;
                case ShuffleEffect.Stun: return b => b.StunChance;
                case ShuffleEffect.Twin: return b => b.TwinChance;
                case ShuffleEffect.Hex: return b => b.HexChance;
                case ShuffleEffect.Repel: return b => b.RepelChance;
                case ShuffleEffect.Execute: return b => b.ExecutePercent;
                case ShuffleEffect.Splash: return b => b.SplashTenths * 10;
                case ShuffleEffect.Frost: return b => b.FrostTenths * 10;
                case ShuffleEffect.WaveFuel: return b => b.WaveFuelTenths * 10;
                case ShuffleEffect.SpinUp: return b => b.SpinStep * b.SpinMost;
                default: return null;
            }
        }

        static int Of(ShuffleCard card, ShuffleFigure figure)
        {
            switch (figure)
            {
                case ShuffleFigure.Magnitude: return card.Magnitude;
                case ShuffleFigure.MagnitudeTens: return card.Magnitude * 10;
                case ShuffleFigure.Extent: return card.Extent;
                case ShuffleFigure.MagnitudeByExtent: return card.Magnitude * card.Extent;
                default: throw new ArgumentOutOfRangeException(nameof(figure), figure, "a figure the notes do not know");
            }
        }
    }
}
