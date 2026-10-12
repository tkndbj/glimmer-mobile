using System;
using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// The clocks and distances the arsenal runs on, for one run: <see cref="SiegeArsenal"/>'s
    /// figures as an object a build carries (<see cref="SiegeBoosts.Arsenal"/>) rather than
    /// constants the board reads.
    ///
    /// <para>
    /// <b>The Shuffle lane's alone, like the arsenal itself</b> (MODES.md 59i, 59l). Every reader
    /// is behind the arsenal's <see cref="SiegeBoosts.IsIdentity"/> return, and
    /// <see cref="SiegeBoosts.None"/> carries <see cref="Shipped"/>, so a chapter and the
    /// Infinite lane can neither reach a figure here nor be handed a different one. What moves
    /// these between builds is the Shuffle tuning (<c>ShuffleTuning</c>, read by
    /// <c>ShuffleConfig</c>), snapshotted when a run opens.
    /// </para>
    /// <para>
    /// <b><see cref="Shipped"/> is the constants, not a copy of them</b>, so a run on the shipped
    /// tuning reads exactly the floats it read before this type existed. A figure is written in
    /// a document as an integer - hundredths for what the board holds as a float (no float may
    /// be authored: <c>JsonUtility</c> refuses <c>.5</c> and a fraction is a rounding argument)
    /// - and a document whose figures are all the shipped ones answers <see cref="Shipped"/>
    /// itself (<see cref="FromWritten"/>).
    /// </para>
    /// </summary>
    public sealed class SiegeArsenalTuning
    {
        /// <summary>
        /// One tunable figure: its key in the document, what ships, and the range a document may
        /// move it in. A range is a quarter to four times what ships unless the figure means
        /// something with a ceiling of its own (a share of the slope, a chill in tenths of the
        /// march, a hop count the drawing was cut for).
        /// </summary>
        public readonly struct Figure
        {
            public readonly string Key;
            public readonly int Shipped, Least, Most;

            public Figure(string key, int shipped, int most = 0)
            {
                Key = key;
                Shipped = shipped;
                Least = Math.Max(1, shipped / 4);
                Most = most > 0 ? Math.Min(most, shipped * 4) : shipped * 4;
            }
        }

        // In the order FromWritten and Written take them. A figure is added at the end.
        static readonly Figure[] Table =
        {
            new Figure("teslaEveryHundredths", Hundredths(SiegeArsenal.TeslaEvery)),
            new Figure("teslaFloorHundredths", Hundredths(SiegeArsenal.TeslaFloor), most: 90),
            new Figure("teslaSpanHundredths", Hundredths(SiegeArsenal.TeslaSpan)),
            new Figure("toxicTickHundredths", Hundredths(SiegeArsenal.ToxicTick)),
            new Figure("quakeShoveHundredths", Hundredths(SiegeArsenal.QuakeShove)),
            new Figure("shatterChillTenths", SiegeArsenal.ShatterChillTenths, most: 9),
            new Figure("shatterChillForHundredths", Hundredths(SiegeArsenal.ShatterChillFor)),
            new Figure("wildfireForHundredths", Hundredths(SiegeArsenal.WildfireFor)),
            new Figure("meteorFallHundredths", Hundredths(SiegeArsenal.MeteorFall)),
            new Figure("meteorBurnTenths", SiegeArsenal.MeteorBurnTenths),
            new Figure("meteorBurnForHundredths", Hundredths(SiegeArsenal.MeteorBurnFor)),
            new Figure("vortexForHundredths", Hundredths(SiegeArsenal.VortexFor)),
            new Figure("vortexTickHundredths", Hundredths(SiegeArsenal.VortexTick)),
            new Figure("vortexDragHundredths", Hundredths(SiegeArsenal.VortexDrag)),
            new Figure("vortexGatherHundredths", Hundredths(SiegeArsenal.VortexGather)),
            new Figure("rayForHundredths", Hundredths(SiegeArsenal.RayFor)),
            new Figure("rayTickHundredths", Hundredths(SiegeArsenal.RayTick)),
            new Figure("rayWidthHundredths", Hundredths(SiegeArsenal.RayWidth)),
            new Figure("nukeFallHundredths", Hundredths(SiegeArsenal.NukeFall)),
            new Figure("wildBurnTenths", SiegeArsenal.WildBurnTenths),
            new Figure("wildVenomTenths", SiegeArsenal.WildVenomTenths),
            new Figure("wildChillTenths", SiegeArsenal.WildChillTenths, most: 9),
            new Figure("wildArcTenths", SiegeArsenal.WildArcTenths),
            new Figure("wildArcHops", SiegeArsenal.WildArcHops, most: 4),
            new Figure("wildBurnForHundredths", Hundredths(SiegeArsenal.WildBurnFor)),
            new Figure("wildVenomForHundredths", Hundredths(SiegeArsenal.WildVenomFor)),
            new Figure("wildChillForHundredths", Hundredths(SiegeArsenal.WildChillFor)),
        };

        /// <summary>Every tunable figure, in the order <see cref="FromWritten"/> takes them.</summary>
        public static IReadOnlyList<Figure> Figures => Table;

        /// <summary>The figures every build before tuning existed ran on. See the class note.</summary>
        public static readonly SiegeArsenalTuning Shipped = new SiegeArsenalTuning();

        public readonly float TeslaEvery, TeslaFloor, TeslaSpan, ToxicTick, QuakeShove, ShatterChillFor,
                              WildfireFor, MeteorFall, MeteorBurnFor, VortexFor, VortexTick, VortexDrag,
                              VortexGather, RayFor, RayTick, RayWidth, NukeFall, WildBurnFor, WildVenomFor,
                              WildChillFor;

        public readonly int ShatterChillTenths, MeteorBurnTenths, WildBurnTenths, WildVenomTenths,
                            WildChillTenths, WildArcTenths, WildArcHops;

        readonly int[] _written;

        /// <summary>The shipped figures, read straight off the constants.</summary>
        SiegeArsenalTuning()
        {
            TeslaEvery = SiegeArsenal.TeslaEvery;
            TeslaFloor = SiegeArsenal.TeslaFloor;
            TeslaSpan = SiegeArsenal.TeslaSpan;
            ToxicTick = SiegeArsenal.ToxicTick;
            QuakeShove = SiegeArsenal.QuakeShove;
            ShatterChillTenths = SiegeArsenal.ShatterChillTenths;
            ShatterChillFor = SiegeArsenal.ShatterChillFor;
            WildfireFor = SiegeArsenal.WildfireFor;
            MeteorFall = SiegeArsenal.MeteorFall;
            MeteorBurnTenths = SiegeArsenal.MeteorBurnTenths;
            MeteorBurnFor = SiegeArsenal.MeteorBurnFor;
            VortexFor = SiegeArsenal.VortexFor;
            VortexTick = SiegeArsenal.VortexTick;
            VortexDrag = SiegeArsenal.VortexDrag;
            VortexGather = SiegeArsenal.VortexGather;
            RayFor = SiegeArsenal.RayFor;
            RayTick = SiegeArsenal.RayTick;
            RayWidth = SiegeArsenal.RayWidth;
            NukeFall = SiegeArsenal.NukeFall;
            WildBurnTenths = SiegeArsenal.WildBurnTenths;
            WildVenomTenths = SiegeArsenal.WildVenomTenths;
            WildChillTenths = SiegeArsenal.WildChillTenths;
            WildArcTenths = SiegeArsenal.WildArcTenths;
            WildArcHops = SiegeArsenal.WildArcHops;
            WildBurnFor = SiegeArsenal.WildBurnFor;
            WildVenomFor = SiegeArsenal.WildVenomFor;
            WildChillFor = SiegeArsenal.WildChillFor;

            _written = new int[Table.Length];
            for (int i = 0; i < Table.Length; i++) _written[i] = Table[i].Shipped;
        }

        SiegeArsenalTuning(int[] w)
        {
            _written = (int[])w.Clone();

            int i = 0;
            TeslaEvery = FromHundredths(w[i++]);
            TeslaFloor = FromHundredths(w[i++]);
            TeslaSpan = FromHundredths(w[i++]);
            ToxicTick = FromHundredths(w[i++]);
            QuakeShove = FromHundredths(w[i++]);
            ShatterChillTenths = w[i++];
            ShatterChillFor = FromHundredths(w[i++]);
            WildfireFor = FromHundredths(w[i++]);
            MeteorFall = FromHundredths(w[i++]);
            MeteorBurnTenths = w[i++];
            MeteorBurnFor = FromHundredths(w[i++]);
            VortexFor = FromHundredths(w[i++]);
            VortexTick = FromHundredths(w[i++]);
            VortexDrag = FromHundredths(w[i++]);
            VortexGather = FromHundredths(w[i++]);
            RayFor = FromHundredths(w[i++]);
            RayTick = FromHundredths(w[i++]);
            RayWidth = FromHundredths(w[i++]);
            NukeFall = FromHundredths(w[i++]);
            WildBurnTenths = w[i++];
            WildVenomTenths = w[i++];
            WildChillTenths = w[i++];
            WildArcTenths = w[i++];
            WildArcHops = w[i++];
            WildBurnFor = FromHundredths(w[i++]);
            WildVenomFor = FromHundredths(w[i++]);
            WildChillFor = FromHundredths(w[i++]);
        }

        /// <summary>
        /// The tuning a document's figures describe, in <see cref="Figures"/>' order, or null with
        /// a fault naming the first figure out of its range. All shipped figures answer
        /// <see cref="Shipped"/> itself.
        /// </summary>
        public static SiegeArsenalTuning FromWritten(int[] written, out string fault)
        {
            fault = null;

            if (written == null || written.Length != Table.Length)
            {
                fault = $"the arsenal names {(written == null ? 0 : written.Length)} figures, not {Table.Length}";
                return null;
            }

            bool shipped = true;

            for (int i = 0; i < Table.Length; i++)
            {
                var f = Table[i];

                if (written[i] < f.Least || written[i] > f.Most)
                {
                    fault = $"arsenal.{f.Key} is {written[i]}; it may move from {f.Least} to {f.Most} (ships at {f.Shipped})";
                    return null;
                }

                if (written[i] != f.Shipped) shipped = false;
            }

            return shipped ? Shipped : new SiegeArsenalTuning(written);
        }

        /// <summary>The figures as a document writes them, in <see cref="Figures"/>' order.</summary>
        public int[] Written() => (int[])_written.Clone();

        static float FromHundredths(int hundredths) => hundredths / 100f;

        static int Hundredths(float value) => (int)Math.Round(value * 100.0);
    }
}
