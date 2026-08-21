using System.Collections.Generic;

namespace GlimmerGrove.Social
{
    /// <summary>What the rename panel should be saying about the name in the field.</summary>
    public enum NameAvailability
    {
        /// <summary>Nothing to say: the field is empty, or an answer never arrived.</summary>
        Unknown = 0,

        /// <summary>Too short, or nothing but punctuation. Refused before anything is asked.</summary>
        TooShort,

        /// <summary>Asked, or about to be. The panel says so rather than guessing.</summary>
        Checking,

        /// <summary>Nobody holds it.</summary>
        Free,

        /// <summary>Somebody else holds it.</summary>
        Taken,

        /// <summary>This account holds it. Saving is still allowed and does nothing.</summary>
        Mine,
    }

    /// <summary>
    /// Decides <em>when</em> to ask the server whether a name is free, and remembers what it
    /// was told. Knows nothing about how to ask.
    ///
    /// <para>
    /// <b>This class is the cost control for the whole feature.</b> A reservation check is one
    /// document read, which is cheap; a check per keystroke, per player, forever, is not — a
    /// sixteen-character name typed straight through is sixteen reads where one will do, and
    /// that single factor is the difference between hundreds of dollars and thousands over the
    /// life of the game. Three rules do the work, and each of them removes reads the naive
    /// version would pay for:
    /// </para>
    /// <list type="bullet">
    /// <item><b>A pause, not a keystroke.</b> Nothing is asked until the field has been still
    /// for <see cref="DebounceSeconds"/>, so a name typed at speed costs one read.</item>
    /// <item><b>An answer is remembered.</b> Deleting three characters and typing them again
    /// asks nothing, and so does typing past a name and coming back to it — which is exactly
    /// what somebody does while they are choosing.</item>
    /// <item><b>A name that cannot be reserved is never asked about.</b> Under two folded
    /// characters is refused here, where it is free, rather than at a database.</item>
    /// </list>
    /// <para>
    /// <b>It is a hint and never the decision.</b> Two players can be typing one name at the
    /// same moment, so what this shows can be out of date by the time somebody presses save —
    /// the claim is adjudicated by a transaction on the server and its answer is the one that
    /// counts. That is why being occasionally optimistic here is acceptable and being
    /// expensive here is not.
    /// </para>
    /// <para>
    /// It holds no clock, no socket and no Unity types: it is handed elapsed time and told what
    /// was typed, which is <c>SyncScheduler</c>'s and <c>GrovePublishPolicy</c>'s bargain and
    /// what lets the whole policy be run offline in the test suite.
    /// </para>
    /// </summary>
    public sealed class NameCheckScheduler
    {
        /// <summary>
        /// How still the field must be before anything is asked.
        ///
        /// Shorter than every other debounce in the game, and it has to be: this one is in
        /// front of somebody who is waiting to find out. Long enough that ordinary typing
        /// produces one read, short enough that a pause reads as an answer rather than a lag.
        /// </summary>
        public const float DebounceSeconds = 0.45f;

        /// <summary>
        /// How many answers are kept. A person auditions a handful of names, not a hundred.
        ///
        /// Bounded rather than unbounded because this object lives as long as the panel and a
        /// panel can be held open indefinitely — an unbounded cache fed by a text field is a
        /// slow leak with a keyboard attached to it.
        /// </summary>
        public const int MaxRemembered = 32;

        readonly Dictionary<string, NameAvailability> _known =
            new Dictionary<string, NameAvailability>();

        readonly List<string> _order = new List<string>();

        string _heldKey = string.Empty;
        string _typedKey = string.Empty;
        string _inFlightKey = string.Empty;
        float _wait;

        /// <summary>What the panel should be saying right now.</summary>
        public NameAvailability Availability { get; private set; } = NameAvailability.Unknown;

        /// <summary>The fold of what is in the field. Empty when it could never be reserved.</summary>
        public string TypedKey => _typedKey;

        /// <summary>True while a read is outstanding.</summary>
        public bool IsAsking => _inFlightKey.Length > 0;

        /// <summary>
        /// The name this account already has, so retyping it is not reported as taken.
        ///
        /// <para>
        /// An optimisation and not the rule: the reservation carries its holder's id, so
        /// <see cref="Answered"/> can tell "mine" from "somebody else's" exactly. This exists
        /// so the commonest case of all — opening the panel, which starts with the current
        /// name in the field — asks nothing at all.
        /// </para>
        /// </summary>
        public void Hold(string storedName)
        {
            _heldKey = GroveNames.Key(storedName);
        }

        /// <summary>
        /// The field changed. Safe to call on every keystroke; that is what it is for.
        /// </summary>
        public void Typed(string stored)
        {
            _typedKey = string.Empty;
            _wait = 0f;

            // Asked before anything else, because it is the one answer that costs nothing and
            // it covers the empty field, a single character, and a name of pure punctuation
            // whose fold is empty. See GroveNames.IsPublishable for why both measurements.
            if (!GroveNames.IsPublishable(stored))
            {
                Availability = NameAvailability.TooShort;
                return;
            }

            string key = GroveNames.Key(stored);
            _typedKey = key;

            if (_heldKey.Length > 0 && string.Equals(key, _heldKey, System.StringComparison.Ordinal))
            {
                Availability = NameAvailability.Mine;
                return;
            }

            if (_known.TryGetValue(key, out var remembered))
            {
                Availability = remembered;
                return;
            }

            Availability = NameAvailability.Checking;
            _wait = DebounceSeconds;
        }

        /// <summary>
        /// Hands the policy elapsed time. Returns true exactly once per name worth asking about.
        /// </summary>
        public bool Tick(float deltaSeconds, out string key)
        {
            key = string.Empty;

            if (_typedKey.Length == 0) return false;
            if (_inFlightKey.Length > 0) return false;
            if (Availability != NameAvailability.Checking) return false;

            _wait -= deltaSeconds;
            if (_wait > 0f) return false;

            _inFlightKey = _typedKey;
            key = _typedKey;
            return true;
        }

        /// <summary>
        /// The server answered. <paramref name="mine"/> distinguishes a reservation this
        /// account holds from a stranger's, which is what the reservation's owner id is for.
        /// </summary>
        public void Answered(string key, bool taken, bool mine)
        {
            if (string.IsNullOrEmpty(key)) return;

            var verdict = !taken ? NameAvailability.Free
                        : mine ? NameAvailability.Mine
                        : NameAvailability.Taken;

            Remember(key, verdict);

            if (string.Equals(key, _inFlightKey, System.StringComparison.Ordinal))
                _inFlightKey = string.Empty;

            // Only adopted when it is still the name in the field. An answer that arrives after
            // the player has typed on is worth remembering and must not be shown.
            if (string.Equals(key, _typedKey, System.StringComparison.Ordinal))
                Availability = verdict;
        }

        /// <summary>
        /// The read failed.
        ///
        /// <para>
        /// Deliberately not retried and deliberately not remembered. There is nothing here
        /// worth spending a battery on — the claim at the end is the authority and gives the
        /// real answer a moment later — and a policy that retried in front of an open keyboard
        /// would turn one unreachable network into a read every second for as long as the
        /// panel stays up. Typing again asks again, which is the only retry anybody wants.
        /// </para>
        /// </summary>
        public void Failed(string key)
        {
            if (string.Equals(key, _inFlightKey, System.StringComparison.Ordinal))
                _inFlightKey = string.Empty;

            if (string.Equals(key, _typedKey, System.StringComparison.Ordinal))
                Availability = NameAvailability.Unknown;
        }

        /// <summary>
        /// Takes the claim's own answer as the truth for this key.
        ///
        /// The claim is adjudicated where the hint is only a hint, so a name the server refused
        /// must not go on being shown as free — otherwise pressing save twice reports two
        /// different things about one name.
        /// </summary>
        public void Adopt(string key, NameAvailability verdict)
        {
            if (string.IsNullOrEmpty(key)) return;

            Remember(key, verdict);

            if (string.Equals(key, _typedKey, System.StringComparison.Ordinal))
                Availability = verdict;
        }

        void Remember(string key, NameAvailability verdict)
        {
            if (!_known.ContainsKey(key))
            {
                _order.Add(key);

                if (_order.Count > MaxRemembered)
                {
                    _known.Remove(_order[0]);
                    _order.RemoveAt(0);
                }
            }

            _known[key] = verdict;
        }
    }
}
