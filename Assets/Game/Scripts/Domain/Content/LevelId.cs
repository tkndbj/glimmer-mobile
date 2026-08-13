using System;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// The permanent name of a level, e.g. "c01_first_light".
    ///
    /// This is the single most important type in the content system. Save files,
    /// analytics events and remote config all key on it, so once an id has shipped
    /// it must never change or be reused — reordering, inserting or removing levels
    /// then costs nothing. It is a struct rather than a raw string so the compiler
    /// stops anyone passing an array index where an identity is expected.
    /// </summary>
    [Serializable]
    public readonly struct LevelId : IEquatable<LevelId>, IComparable<LevelId>
    {
        /// <summary>Ids are lowercase ASCII, digits and underscores. Nothing else.</summary>
        public const int MaxLength = 48;

        readonly string _value;

        LevelId(string value) => _value = value;

        public static readonly LevelId None = default;

        public string Value => _value ?? string.Empty;
        public bool IsValid => !string.IsNullOrEmpty(_value);

        /// <summary>Parses an authored id, throwing when it breaks the naming rules.</summary>
        public static LevelId Parse(string raw)
        {
            if (!TryParse(raw, out var id, out var error))
                throw new ArgumentException($"invalid level id '{raw}': {error}", nameof(raw));
            return id;
        }

        /// <summary>
        /// Non-throwing parse. Content arriving from a remote server is untrusted, so
        /// the loader always goes through this and drops levels it cannot name.
        /// </summary>
        public static bool TryParse(string raw, out LevelId id, out string error)
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

            id = new LevelId(raw);
            return true;
        }

        public bool Equals(LevelId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is LevelId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(LevelId other) => string.CompareOrdinal(Value, other.Value);
        public override string ToString() => Value;

        public static bool operator ==(LevelId a, LevelId b) => a.Equals(b);
        public static bool operator !=(LevelId a, LevelId b) => !a.Equals(b);
    }
}
