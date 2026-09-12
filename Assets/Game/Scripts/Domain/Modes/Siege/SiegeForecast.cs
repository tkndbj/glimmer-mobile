namespace GlimmerGrove.Modes
{
    /// <summary>
    /// What a wave is bringing, counted by colour.
    ///
    /// <para>
    /// <b>A struct of counts rather than the wave itself</b>, because what a forecast is for is
    /// being read in a glance: the player needs <em>mostly blue, and a boss</em> in a quarter of a
    /// second, not a list of raiders in the order they will walk on. Weight is counted in raiders
    /// rather than in health for the same reason — this is a shape, not a sum.
    /// </para>
    /// </summary>
    public readonly struct SiegeForecast
    {
        /// <summary>How many raiders of each of <see cref="SiegeLayout.Letters"/> are coming.</summary>
        public readonly int R, G, B, Y;

        /// <summary>Whether a boss is in it, and what it is. A creeper when there is none.</summary>
        public readonly SiegeKind Boss;

        /// <summary>Whether there is a wave at all. False past the last one.</summary>
        public readonly bool Any;

        SiegeForecast(int r, int g, int b, int y, SiegeKind boss, bool any)
        {
            R = r;
            G = g;
            B = b;
            Y = y;
            Boss = boss;
            Any = any;
        }

        /// <summary>How many of one of <see cref="SiegeLayout.Letters"/> are coming.</summary>
        public int Of(int colour)
        {
            switch (colour)
            {
                case 0:  return R;
                case 1:  return G;
                case 2:  return B;
                default: return colour == 3 ? Y : 0;
            }
        }

        /// <summary>Every raider in the wave, boss included.</summary>
        public int Count => R + G + B + Y;

        /// <summary>The most any one colour sends, so a view can scale a row of pips by it.</summary>
        public int Most
        {
            get
            {
                int most = R;
                if (G > most) most = G;
                if (B > most) most = B;
                if (Y > most) most = Y;
                return most;
            }
        }

        /// <summary>Whether a boss is in this wave.</summary>
        public bool HasBoss => Any && SiegeTuning.IsBoss(Boss);

        /// <summary>Nothing is coming: past the last wave of an authored ladder.</summary>
        public static SiegeForecast None => default;

        /// <summary>
        /// Reads wave <paramref name="wave"/> of <paramref name="layout"/>.
        ///
        /// <b>Through the layout's own readers</b>, so an endless lane's generated wave and an
        /// authored one are the same question asked once — and so a boss riding the head of the
        /// last authored wave is counted exactly where the muster will put it (invariant 37ad).
        /// </summary>
        public static SiegeForecast Of(SiegeLayout layout, int wave)
        {
            if (layout == null || wave < 0 || wave >= layout.WaveCount) return None;

            int size = layout.SizeOf(wave);
            if (size <= 0) return None;

            int r = 0, g = 0, b = 0, y = 0;
            var boss = SiegeKind.Creeper;

            for (int i = 0; i < size; i++)
            {
                switch (layout.ColourAt(wave, i))
                {
                    case 0: r++; break;
                    case 1: g++; break;
                    case 2: b++; break;
                    case 3: y++; break;
                }

                var kind = layout.KindAt(wave, i);
                if (SiegeTuning.IsBoss(kind)) boss = kind;
            }

            return new SiegeForecast(r, g, b, y, boss, true);
        }
    }
}
