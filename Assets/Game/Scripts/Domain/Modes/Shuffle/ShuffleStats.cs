using System;
using System.Collections.Generic;

namespace GlimmerGrove.Shuffle
{
    /// <summary>How a stat's figure is written for a player.</summary>
    public enum ShuffleStatKind
    {
        /// <summary>Per cent of the plain line's figure, written as a multiplier: 1240 is "x12.4".</summary>
        Times = 0,

        /// <summary>A chance, per cent: 35 is "35%".</summary>
        Percent = 1,

        /// <summary>A plain count: 14 is "14".</summary>
        Count = 2,
    }

    /// <summary>
    /// One figure a finished Shuffle run reports about its build (MODES.md 59o): on the victory
    /// panel, in the ledger's best run and on the keeper's public card.
    ///
    /// <para>
    /// <b>The id is permanent</b> (invariant 1's rule): it is stored in the save and published,
    /// and a reader that meets an id it does not know carries it and draws nothing for it, so a
    /// stat added by a newer build survives a trip through an older one. The name is derived from
    /// the id (5a), the picture is the card that moves the figure, so the readout looks like the
    /// hand it came from.
    /// </para>
    /// </summary>
    public sealed class ShuffleStat
    {
        public readonly string Id;
        public readonly ShuffleStatKind Kind;

        /// <summary>The picture's address under <c>Art/</c>.</summary>
        public readonly string Icon;

        readonly Func<ShuffleBuild, long> _read;

        ShuffleStat(string id, ShuffleStatKind kind, string icon, Func<ShuffleBuild, long> read)
        {
            Id = id;
            Kind = kind;
            Icon = icon;
            _read = read;
        }

        /// <summary>A stat drawn with the picture of the card that moves it.</summary>
        internal static ShuffleStat OfCard(string id, ShuffleStatKind kind, string card, Func<ShuffleBuild, long> read)
            => new ShuffleStat(id, kind, "Ui/Shuffle/" + card, read);

        /// <summary>A stat drawn with a picture of its own.</summary>
        internal static ShuffleStat Drawn(string id, ShuffleStatKind kind, string icon, Func<ShuffleBuild, long> read)
            => new ShuffleStat(id, kind, icon, read);

        /// <summary>The loc key for its label. Derived, never authored (5a).</summary>
        public string NameKey => NameKeyOf(Id);

        /// <summary>The label key any stat id would have.</summary>
        public static string NameKeyOf(string id) => "shuffle.stat." + id;

        /// <summary>The figure <paramref name="build"/> holds, inside the bound every reader enforces.</summary>
        public int Read(ShuffleBuild build) => build == null ? 0 : ShuffleStats.Clamp(_read(build));

        public override string ToString() => Id;
    }

    /// <summary>
    /// The figures a run reports, in the order they are drawn. <b>Code, never content</b>, like the
    /// cards they read (59d).
    ///
    /// <para>
    /// <b>Adding one</b> is a row here and a <c>shuffle.stat.{id}</c> key in all nine tables; the
    /// save, the wire, the merge and the server carry it with no change, because a run's stats
    /// travel as id/value rows (<see cref="ShuffleRecord"/>). A run banked before the row existed
    /// simply does not carry it, and every reader draws only what a record holds.
    /// </para>
    /// <para>
    /// <b>Effective figures, the way the board reads them</b> (<c>SiegeBoosts</c>): damage is every
    /// additive damage card and the iron synergy, multiplied by every pact; speed is the
    /// <c>EffectiveFirePercent</c> a ward fires at; health the <c>EffectiveGuardPercent</c> it
    /// stands on. A number on this panel is a number the hill felt.
    /// </para>
    /// </summary>
    public static class ShuffleStats
    {
        /// <summary>The most stat rows a record keeps, read or written. A bound, not a goal.</summary>
        public const int MaxRows = 16;

        /// <summary>The largest figure a stat may claim (x1,000,000 as a multiplier).</summary>
        public const int MaxValue = 100000000;

        /// <summary>The longest stat id a reader accepts.</summary>
        public const int MaxIdLength = 24;

        public static readonly ShuffleStat[] All =
        {
            ShuffleStat.OfCard("damage", ShuffleStatKind.Times, "heavy_bolts",
                b => (long)(b.Boosts.DamagePercent + b.Boosts.IronBonus) * b.Boosts.PactDamage / 1000),

            ShuffleStat.OfCard("speed", ShuffleStatKind.Times, "quick_barrels",
                b => b.Boosts.EffectiveFirePercent),

            ShuffleStat.OfCard("crit", ShuffleStatKind.Percent, "keen_eyes",
                b => b.Boosts.CritChance > 100 ? 100 : b.Boosts.CritChance),

            ShuffleStat.OfCard("health", ShuffleStatKind.Times, "iron_posts",
                b => b.Boosts.EffectiveGuardPercent),

            ShuffleStat.Drawn("cards", ShuffleStatKind.Count, "Ui/ic_deck",
                b => b.Taken.Count),

            ShuffleStat.Drawn("bonds", ShuffleStatKind.Count, "Ui/" + ShuffleBond.EvolutionMark,
                b => b.Bonds.Count),
        };

        /// <summary>The stat with this id, or null for one this build does not draw.</summary>
        public static ShuffleStat Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            for (int i = 0; i < All.Length; i++)
                if (string.Equals(All[i].Id, id, StringComparison.Ordinal)) return All[i];

            return null;
        }

        /// <summary>
        /// Whether <paramref name="id"/> is shaped like a stat id - lower-case letters, digits and
        /// underscores, not too long - whether or not this build knows it. What a reader carries.
        /// </summary>
        public static bool WellFormed(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength) return false;

            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')) return false;
            }

            return true;
        }

        /// <summary>A figure held to <c>0 .. MaxValue</c>.</summary>
        public static int Clamp(long value) => value < 0 ? 0 : value > MaxValue ? MaxValue : (int)value;

        /// <summary>Every stat this build reads off <paramref name="build"/>, in drawing order.</summary>
        public static List<ShuffleStatValue> Read(ShuffleBuild build)
        {
            var read = new List<ShuffleStatValue>(All.Length);
            for (int i = 0; i < All.Length; i++) read.Add(new ShuffleStatValue(All[i].Id, All[i].Read(build)));
            return read;
        }

        /// <summary>
        /// The figure as a player reads it: "x12.4", "35%" or "14". The sign is a loc key
        /// (<c>ui.shuffle.times</c>, the hand's own, and <c>ui.shuffle.percent</c>), so a
        /// language decides where it goes; the number is <see cref="Figure"/>'s.
        /// </summary>
        public static string Say(ShuffleStatKind kind, int value)
        {
            string figure = Figure(kind, value);
            switch (kind)
            {
                case ShuffleStatKind.Times: return Localization.Loc.Format("ui.shuffle.times", figure);
                case ShuffleStatKind.Percent: return Localization.Loc.Format("ui.shuffle.percent", figure);
                case ShuffleStatKind.Count: return figure;
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "a stat kind with no words");
            }
        }

        /// <summary>
        /// The number inside <see cref="Say"/>: a multiplier to one decimal under ten and
        /// whole above it (x2.5, x12, x1,240), every other kind whole and grouped.
        /// Integer arithmetic throughout, so no float decides a digit.
        /// </summary>
        public static string Figure(ShuffleStatKind kind, int value)
        {
            var invariant = System.Globalization.CultureInfo.InvariantCulture;
            if (value < 0) value = 0;

            if (kind != ShuffleStatKind.Times) return value.ToString("N0", invariant);

            if (value >= 1000) return (value / 100).ToString("N0", invariant);

            int tenths = value / 10;
            int whole = tenths / 10, part = tenths % 10;
            return part == 0 ? whole.ToString(invariant) : whole.ToString(invariant) + "." + part.ToString(invariant);
        }
    }

    /// <summary>One stat's figure, as a record carries it.</summary>
    public readonly struct ShuffleStatValue : IEquatable<ShuffleStatValue>
    {
        public readonly string Id;
        public readonly int Value;

        public ShuffleStatValue(string id, int value)
        {
            Id = id ?? string.Empty;
            Value = ShuffleStats.Clamp(value);
        }

        public bool Equals(ShuffleStatValue other)
            => string.Equals(Id, other.Id, StringComparison.Ordinal) && Value == other.Value;

        public override bool Equals(object obj) => obj is ShuffleStatValue other && Equals(other);

        public override int GetHashCode() => (Id?.GetHashCode() ?? 0) * 31 + Value;

        public override string ToString() => Id + "=" + Value;
    }
}
