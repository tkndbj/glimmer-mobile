namespace GlimmerGrove.Modes
{
    /// <summary>
    /// What a planting draws on top of its flowers, as a function of how much it opened.
    /// </summary>
    public readonly struct KeeperLayers
    {
        /// <summary>How hard the rest of the grove is knocked as the light passes, 0..1.</summary>
        public readonly float Jolt;

        /// <summary>A ring thrown right across the grove in the colour that was completed.</summary>
        public readonly bool Sweep;

        /// <summary>A slow star lit behind the whole board — the one layer drawn under it.</summary>
        public readonly bool Rays;

        /// <summary>Sparks arcing up out of the grove and going off above it.</summary>
        public readonly bool Fireworks;

        public readonly bool Confetti;

        /// <summary>How many rockets go up. Nought unless <see cref="Fireworks"/>.</summary>
        public readonly int Rockets;

        public KeeperLayers(float jolt, bool sweep, bool rays, bool fireworks, bool confetti,
                            int rockets)
        {
            Jolt = jolt;
            Sweep = sweep;
            Rays = rays;
            Fireworks = fireworks;
            Confetti = confetti;
            Rockets = rockets;
        }

        /// <summary>
        /// How many distinct kinds of thing this planting draws. The reading that matters, and
        /// the one a test can hold a ladder to.
        ///
        /// <para>
        /// Nought for a planting that opened nothing, which is most of them — a tile laid to
        /// reach somewhere is a move and not an event, and counting a flower it never grew would
        /// make the ladder start one rung above the ground.
        /// </para>
        /// </summary>
        public int Kinds
        {
            get
            {
                if (Jolt <= 0f) return 0;

                int n = 2;                       // the flower, and the grove knocked under it
                if (Sweep) n++;
                if (Rays) n++;
                if (Fireworks) n++;
                if (Confetti) n++;
                return n;
            }
        }
    }

    /// <summary>
    /// <b>How a flourish escalates, in <em>kinds</em> of thing rather than in amounts of one
    /// thing.</b> <c>BudSpectacle</c>'s lesson, applied to the mode that needed it most.
    ///
    /// <para>
    /// Groovekeeper's celebration was one picture at five sizes: the same flower, rays, ring and
    /// sparks every time, with a bigger swell and a louder shake as the count went up. That is
    /// precisely the mistake Budburst's first wave ladder made and had to be rewritten out of —
    /// <em>a number going up is not something anybody sees; a thing that was not there before
    /// is.</em> So each rung switches a whole new kind of thing on and keeps the ones below it.
    /// </para>
    /// <para>
    /// <b>The rungs are set against what this mode actually reaches.</b> Five is the ceiling and
    /// it is a fact about the board rather than a taste (<see cref="KeeperFlourish.Most"/>), so
    /// unlike a chain there is nowhere for a ladder to hide: with only five rungs available,
    /// every one of them has to land inside ordinary play. Most plantings that do anything open
    /// one, a good one opens two or three, and the best the rules allow is five.
    /// </para>
    /// <para>
    /// <b>A bed lifts the floor, and that is the one clause that is not about size.</b> A bed
    /// opening is the only thing on this board that is <em>progress</em> — every other bloom is
    /// beautiful and optional — so a single bloom that opened a bed is drawn at the sweep rung
    /// rather than the bare one. Without it the commonest good move in the mode (one tile, one
    /// bed, par advanced) is the quietest thing on the screen, which is the ladder-with-a-bare-
    /// first-rung fault one mode over.
    /// </para>
    /// </summary>
    public static class KeeperSpectacle
    {
        /// <summary>Blooms from which the grove is knocked at all.</summary>
        public const int JoltFrom = 1;

        /// <summary>Blooms from which a ring crosses the whole grove.</summary>
        public const int SweepFrom = 2;

        /// <summary>Blooms from which a star lights behind the board.</summary>
        public const int RaysFrom = 3;

        /// <summary>Blooms from which fireworks leave the grove.</summary>
        public const int FireworksFrom = 4;

        /// <summary>Blooms from which confetti falls. The top of what the rules allow.</summary>
        public const int ConfettiFrom = 5;

        /// <summary>
        /// The layers a planting of this size draws.
        ///
        /// <paramref name="openedABed"/> lifts a lone bloom to the sweep rung — see the class
        /// remarks. It can only ever raise the reading, never lower it.
        /// </summary>
        public static KeeperLayers For(int blooms, bool openedABed)
        {
            if (blooms < 0) blooms = 0;

            int rung = openedABed && blooms < SweepFrom ? SweepFrom : blooms;

            if (rung <= 0) return new KeeperLayers(0f, false, false, false, false, 0);

            bool fireworks = rung >= FireworksFrom;

            float jolt = rung < JoltFrom ? 0f : .18f + (rung - JoltFrom) * .21f;
            if (jolt > 1f) jolt = 1f;

            return new KeeperLayers(
                jolt,
                rung >= SweepFrom,
                rung >= RaysFrom,
                fireworks,
                rung >= ConfettiFrom,
                fireworks ? 4 + (rung - FireworksFrom) * 3 : 0);
        }
    }
}
