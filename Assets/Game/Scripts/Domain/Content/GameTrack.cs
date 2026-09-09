using System;
using System.Collections.Generic;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// Which <em>ladder</em> a chapter belongs to inside its mode: the ordinary run of chapters, or
    /// a lane beside it that is played the same way and progresses differently.
    ///
    /// <para>
    /// <b>One level finer than a mode, and it exists because a mode's second lane is not a second
    /// mode.</b> Thornwatch's Infinite is the same board, the same wards, the same raiders and the
    /// same verb; what differs is that its waves never stop and it is graded on how far you got
    /// rather than on what you spent. Filing it as a mode would put it in the switcher as a
    /// stranger, give it its own art, its own validator and its own registry row, and — worst —
    /// its own chapter ladder, so finishing the ordinary game would be the price of opening it.
    /// Filing it as an ordinary chapter of the same mode is worse still: <c>LevelUnlock.GateFor</c>
    /// looks for the chapter <em>before this one in the same mode</em>, so an endless chapter
    /// dropped into that list would gate a real chapter on stars nobody can earn.
    /// </para>
    /// <para>
    /// <b>So a track is a lane, and everything that walks a ladder walks one lane.</b>
    /// <c>CatalogIndex.ChaptersIn(mode)</c> and <c>LevelsIn(mode)</c> answer the <see cref="Main"/>
    /// track and nothing else, which is what keeps <c>Next</c>, <c>Previous</c>, <c>OrderOf</c>,
    /// <c>IsLast</c>, <c>GateFor</c> and <c>NextToPlay</c> correct with no change at all. Anything
    /// that wants a second lane asks for it by name.
    /// </para>
    /// <para>
    /// <b>A permanent string, not an enum</b>, for <see cref="GameMode"/>'s reason: the value
    /// reaches the manifest, analytics and loc keys, so an ordinal would be a second identity
    /// nobody authored. A chapter naming a track this build has never heard of is <b>skipped
    /// whole</b>, exactly as one naming an unknown mode is (invariant 20) — content from the
    /// future is lost rather than filed under a lane it does not belong in.
    /// </para>
    /// <para>
    /// Note what is deliberately absent: nothing here reaches the save file. An endless level is
    /// an ordinary level with its own permanent <see cref="LevelId"/>, so its record, its stars,
    /// its rewards and its merge are the ones every glade already has — invariant 20a's bargain,
    /// collected once more.
    /// </para>
    /// </summary>
    [Serializable]
    public readonly struct GameTrack : IEquatable<GameTrack>, IComparable<GameTrack>
    {
        /// <summary>Short because it is a manifest field, a loc key stem and an analytics dimension.</summary>
        public const int MaxLength = 16;

        readonly string _value;

        GameTrack(string value) => _value = value;

        /// <summary>
        /// The ordinary ladder: chapter one, then two, gated on stars.
        ///
        /// <b>What a chapter naming no track is read as</b>, so every chapter authored before
        /// tracks existed keeps working with its file untouched — <see cref="GameMode.Glade"/>'s
        /// rule, one level down.
        /// </summary>
        public static readonly GameTrack Main = new GameTrack("main");

        /// <summary>
        /// A lane whose waves never stop: one level, played for how far it gets rather than for
        /// finishing it.
        /// </summary>
        public static readonly GameTrack Infinite = new GameTrack("infinite");

        /// <summary>
        /// Every track this build can honour, in the order a switcher offers them.
        ///
        /// <b>A written list rather than a sort</b>, which is invariant 38a's rule for the mode
        /// switcher and for its reason: a control that reorders itself moves the entry somebody
        /// reaches for without looking.
        /// </summary>
        public static readonly GameTrack[] Shipped = { Main, Infinite };

        public string Value => _value ?? Main.Value;

        /// <summary>Whether this is the ordinary ladder.</summary>
        public bool IsMain => Equals(Main);

        /// <summary>
        /// Reads an authored track name.
        ///
        /// <para>
        /// <b>An empty field is the main track and is never an error</b> — that is what lets every
        /// chapter that shipped before this existed keep working. Anything else that is not on
        /// <see cref="Shipped"/> is refused by name, so a chapter written for a newer build is
        /// lost rather than quietly filed on the ordinary ladder where it would gate a real
        /// chapter (invariant 5f's rule about a token this build no longer knows, read forwards).
        /// </para>
        /// </summary>
        public static bool TryParse(string raw, out GameTrack track, out string error)
        {
            track = Main;
            error = null;

            if (string.IsNullOrEmpty(raw)) return true;

            string trimmed = raw.Trim();
            if (trimmed.Length == 0) return true;

            if (trimmed.Length > MaxLength)
            {
                error = $"track '{trimmed}' is longer than {MaxLength} characters";
                return false;
            }

            for (int i = 0; i < Shipped.Length; i++)
                if (string.Equals(Shipped[i].Value, trimmed, StringComparison.Ordinal))
                {
                    track = Shipped[i];
                    return true;
                }

            error = $"'{trimmed}' is not a track this build knows";
            return false;
        }

        /// <summary>The loc key for its name, derived from the id (invariant 5a).</summary>
        public string NameKey => "track." + Value + ".name";

        /// <summary>The loc key for the one line under that name.</summary>
        public string TaglineKey => "track." + Value + ".tagline";

        public bool Equals(GameTrack other)
            => string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is GameTrack other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(GameTrack other) => string.CompareOrdinal(Value, other.Value);
        public override string ToString() => Value;

        public static bool operator ==(GameTrack a, GameTrack b) => a.Equals(b);
        public static bool operator !=(GameTrack a, GameTrack b) => !a.Equals(b);
    }

    /// <summary>
    /// One lane of the catalog: a mode and a track.
    ///
    /// <b>A pair rather than two dictionaries</b>, because everything that walks a ladder needs
    /// both at once — a chapter's neighbours, its gate, its place in the order — and two lookups
    /// keyed separately is two places for one of them to be forgotten.
    /// </summary>
    public readonly struct ModeLane : IEquatable<ModeLane>
    {
        public readonly GameMode Mode;
        public readonly GameTrack Track;

        public ModeLane(GameMode mode, GameTrack track)
        {
            Mode = mode;
            Track = track;
        }

        public bool Equals(ModeLane other) => Mode == other.Mode && Track == other.Track;
        public override bool Equals(object obj) => obj is ModeLane other && Equals(other);

        public override int GetHashCode()
            => unchecked(Mode.GetHashCode() * 397 ^ Track.GetHashCode());

        public override string ToString() => Mode.Value + "/" + Track.Value;
    }
}
