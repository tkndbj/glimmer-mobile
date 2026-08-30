using System;
using System.Collections.Generic;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// Which way a chapter is played. <see cref="Glade"/> turns conduits until the light
    /// reaches every critter; the rest are their own games entirely.
    ///
    /// <para>
    /// <b>A mode is code, and a chapter names one.</b> That split is the whole design. A mode
    /// brings an interaction, a fail state and a par rule, so content can never add one — but
    /// content decides which glades are played that way, which is what lets a drop ship ten
    /// wisp runs with no app update. The manifest carries the name; this build carries the
    /// list of names it can honour, and a chapter naming a mode it has never heard of is
    /// skipped whole rather than half-read, exactly as <c>minAppVersion</c> is. That is the
    /// forward-compatibility rule the content system already lives by: content ships ahead of
    /// builds, so an older client meeting newer content must lose that content and nothing
    /// else.
    /// </para>
    /// <para>
    /// It is a struct wearing a permanent string rather than an enum for
    /// <see cref="LevelId"/>'s reason: the value reaches the manifest, analytics and loc keys,
    /// so an enum's ordinal would be a second identity nobody authored, and renumbering one
    /// would silently repoint a chapter at a different mode.
    /// </para>
    /// <para>
    /// Note what is deliberately absent: nothing here reaches the save file. A mode-two glade
    /// is an ordinary glade with its own permanent <see cref="LevelId"/>, so its record,
    /// its stars, its rewards and its merge are the ones every other glade already has — see
    /// <c>ProgressionLedger</c>. That is why a second mode cost no schema version and no
    /// server work.
    /// </para>
    /// </summary>
    [Serializable]
    public readonly struct GameMode : IEquatable<GameMode>, IComparable<GameMode>
    {
        /// <summary>Short because it is a manifest field, a loc key stem and an analytics dimension.</summary>
        public const int MaxLength = 16;

        readonly string _value;

        GameMode(string value) => _value = value;

        public static readonly GameMode None = default;

        /// <summary>
        /// Turn the conduits until every critter wakes. The mode the game shipped with, and
        /// the one a chapter that names none is read as — so every chapter authored before
        /// modes existed keeps working with its file untouched.
        /// </summary>
        public static readonly GameMode Glade = new GameMode("glade");

        /// <summary>Motes drop into columns and cook toward white. See <c>FallBoard</c>.</summary>
        public static readonly GameMode Fall = new GameMode("fall");

        /// <summary>Tiles laid out so unlike edges bloom. See <c>KeeperBoard</c>.</summary>
        public static readonly GameMode Keeper = new GameMode("keeper");

        /// <summary>Buds that burst and ripen what is beside them. See <c>BudBoard</c>.</summary>
        public static readonly GameMode Bud = new GameMode("bud");

        // "weave" is a **retired mode id and must never be reused.** Lightweave shipped three
        // chapters and was removed: dragging a channel from a crystal to its critter turned out
        // to reject almost nothing (invariant 5d), and the two rules that did bite — the ring and
        // the hedge — were bought by making the *route* longer rather than by making the decision
        // harder. An id travels into the manifest, analytics and loc keys exactly as a level id
        // does, so re-pointing it at a different way of playing would silently relabel three
        // chapters' worth of history. A chapter file still naming it is simply skipped, which is
        // the same forward-compatibility rule an unknown mode has always had.

        /// <summary>What a chapter with no <c>mode</c> field is played as.</summary>
        public static GameMode Default => Glade;

        /// <summary>
        /// Every mode this build can play, in the order the switcher offers them.
        ///
        /// <para>
        /// Derived from <see cref="LevelModes"/> rather than listed again here, so a mode is
        /// registered in exactly one place and this list cannot come to disagree with what the
        /// game can actually load. The classic mode is first and stays first: it is where a new
        /// player is, and a switcher that reorders itself as modes are added would move the
        /// entry somebody reaches for without looking.
        /// </para>
        /// </summary>
        public static IReadOnlyList<GameMode> Shipped => LevelModes.Ids;

        public string Value => _value ?? string.Empty;

        public bool IsValid => !string.IsNullOrEmpty(_value);

        /// <summary>Whether this build knows how to play it. A chapter's whole membership rests on this.</summary>
        public bool IsPlayable
        {
            get
            {
                var shipped = Shipped;
                for (int i = 0; i < shipped.Count; i++) if (shipped[i].Equals(this)) return true;
                return false;
            }
        }

        /// <summary>
        /// Reads a manifest's <c>mode</c> field. An empty one is <see cref="Default"/> rather
        /// than a rejection, which is what makes the field optional forever.
        ///
        /// <para>
        /// Answering true for a mode this build cannot play is deliberate: the caller has to
        /// tell "this is malformed" (a problem worth reporting) from "this is newer than me"
        /// (a decision reported to nobody). <see cref="IsPlayable"/> is the second question.
        /// </para>
        /// </summary>
        public static bool TryParse(string raw, out GameMode mode, out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(raw)) { mode = Default; return true; }

            if (raw.Length > MaxLength)
            {
                mode = None;
                error = $"longer than {MaxLength} characters";
                return false;
            }

            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_') continue;

                mode = None;
                error = $"illegal character '{c}' at {i}, use a-z 0-9 and underscore";
                return false;
            }

            mode = new GameMode(raw);
            return true;
        }

        /// <summary>The mode's name, derived from its id so a mode names itself once.</summary>
        public string NameKey => "mode." + Value + ".name";

        /// <summary>One line saying what the player does in it, for the switcher.</summary>
        public string TaglineKey => "mode." + Value + ".tagline";

        public bool Equals(GameMode other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is GameMode other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(GameMode other) => string.CompareOrdinal(Value, other.Value);
        public override string ToString() => Value;

        public static bool operator ==(GameMode a, GameMode b) => a.Equals(b);
        public static bool operator !=(GameMode a, GameMode b) => !a.Equals(b);
    }
}
