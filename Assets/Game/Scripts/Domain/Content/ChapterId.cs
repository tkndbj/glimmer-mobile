using System;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// The permanent name of a chapter, e.g. "c01_shallows". Chapters are the unit
    /// of content delivery and the unit of shared art: every level inside one uses
    /// the same backdrop and map strip, which is what keeps a 500 level game from
    /// carrying 500 unique backgrounds.
    /// </summary>
    [Serializable]
    public readonly struct ChapterId : IEquatable<ChapterId>, IComparable<ChapterId>
    {
        public const int MaxLength = 48;

        readonly string _value;

        ChapterId(string value) => _value = value;

        public static readonly ChapterId None = default;

        public string Value => _value ?? string.Empty;
        public bool IsValid => !string.IsNullOrEmpty(_value);

        public static ChapterId Parse(string raw)
        {
            if (!TryParse(raw, out var id, out var error))
                throw new ArgumentException($"invalid chapter id '{raw}': {error}", nameof(raw));
            return id;
        }

        public static bool TryParse(string raw, out ChapterId id, out string error)
        {
            id = None;
            error = null;

            if (string.IsNullOrWhiteSpace(raw)) { error = "empty"; return false; }
            if (raw.Length > MaxLength) { error = $"longer than {MaxLength} characters"; return false; }

            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
                if (ok) continue;
                error = $"illegal character '{c}' at {i}, use a-z 0-9 and underscore";
                return false;
            }

            if (raw[0] == '_' || raw[raw.Length - 1] == '_') { error = "cannot start or end with underscore"; return false; }

            id = new ChapterId(raw);
            return true;
        }

        public bool Equals(ChapterId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is ChapterId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(ChapterId other) => string.CompareOrdinal(Value, other.Value);
        public override string ToString() => Value;

        public static bool operator ==(ChapterId a, ChapterId b) => a.Equals(b);
        public static bool operator !=(ChapterId a, ChapterId b) => !a.Equals(b);
    }
}
