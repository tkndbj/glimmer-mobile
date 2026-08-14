using System;
using System.Collections.Generic;

namespace GlimmerGrove.Progression
{
    /// <summary>One companion a player can wear on their profile.</summary>
    public readonly struct AvatarDefinition
    {
        /// <summary>
        /// Permanent id. It is written into the save file and will key analytics and,
        /// once the shop exists, purchases — so it is subject to the same rule as a
        /// <c>LevelId</c>: never renamed, never reused, never derived from position.
        /// </summary>
        public readonly string Id;

        /// <summary>
        /// Sprite key under <c>Art/Companions/</c>. Deliberately a separate string from
        /// <see cref="Id"/>: art gets re-cut and re-named between drops, and a save file
        /// must not be holding a path.
        /// </summary>
        public readonly string Portrait;

        /// <summary>
        /// Sprite-set key under <c>Art/Critters/</c> for the few companions that also
        /// appear animated on a board, or empty. A still portrait is all the profile
        /// needs, and it costs about 45 KB against 700 KB for a full flipbook — which is
        /// the difference between a roster that scales and one that does not.
        /// </summary>
        public readonly string Animated;

        /// <summary>Keeper level this unlocks at. 0 means available from the first launch.</summary>
        public readonly int UnlockLevel;

        public AvatarDefinition(string id, string portrait, string animated, int unlockLevel)
        {
            Id = id;
            Portrait = string.IsNullOrEmpty(portrait) ? id : portrait;
            Animated = animated ?? string.Empty;
            UnlockLevel = unlockLevel < 0 ? 0 : unlockLevel;
        }

        public bool IsValid => !string.IsNullOrEmpty(Id);

        public bool HasAnimation => !string.IsNullOrEmpty(Animated);

        /// <summary>
        /// A companion's name is a pure function of its id, with no override — the same
        /// rule as a level's, and for the same reason. Anything holding an avatar id can
        /// name it without reading anything else, which is what lets the top bar, the
        /// picker and the showcase all label a companion from the save file alone.
        /// </summary>
        public string NameKey => DefaultNameKey(Id);

        public static string DefaultNameKey(string id) => "ui.avatar." + id;

        public override string ToString() => Id ?? "(none)";
    }

    /// <summary>
    /// The roster of profile companions, and the rule for which are unlocked.
    ///
    /// <para>
    /// The roster is <b>content</b>: it comes from the manifest, which means a drop can
    /// add, retire or re-tune a companion without an app update. What lives here is the
    /// question-answering — resolve an id, decide what is unlocked — plus a built-in
    /// roster used only when content has not loaded yet or arrived without one. That
    /// fallback is not a placeholder to delete: a client whose CDN fetch failed still
    /// has to draw somebody's profile, and "the companions this build shipped with" is
    /// the right answer.
    /// </para>
    /// <para>
    /// Unlocking is <em>derived</em> from keeper level rather than stored, for the same
    /// reason XP is: a stored "owned" set cannot be merged across devices without
    /// deciding what a missing entry means, cannot be retuned for existing players, and
    /// cannot be recovered when it is lost. A level threshold recomputes, and a retune
    /// that would take a companion away is caught by <see cref="Resolve"/> keeping the
    /// player on whatever they are already wearing.
    /// </para>
    /// </summary>
    public static class AvatarCatalog
    {
        /// <summary>
        /// What ships in the build. Used until content loads, and whenever a manifest
        /// carries no roster at all.
        /// </summary>
        /// <summary>
        /// Ordered by unlock level, because <see cref="Default"/> is simply the first —
        /// and the first is the companion a brand-new player wears. It must agree with
        /// the manifest's roster, or a client that failed to fetch content would greet
        /// somebody with a different character than the one they had.
        /// </summary>
        static readonly AvatarDefinition[] BuiltIn =
        {
            new AvatarDefinition("monarch",  "monarch",  "c5", 0),
            new AvatarDefinition("timber",   "timber",   "c2", 2),
            new AvatarDefinition("sprocket", "sprocket", "c3", 4),
            new AvatarDefinition("thistle",  "thistle",  "c4", 6),
            new AvatarDefinition("puff",     "puff",     "c1", 8),
        };

        static AvatarDefinition[] _roster = BuiltIn;
        static Dictionary<string, int> _byId = IndexOf(BuiltIn);

        /// <summary>Raised when the roster is replaced, so an open screen can redraw.</summary>
        public static event Action Changed;

        public static IReadOnlyList<AvatarDefinition> All => _roster;

        /// <summary>True when the roster came from content rather than the built-in list.</summary>
        public static bool IsFromContent { get; private set; }

        /// <summary>
        /// Installs the roster a manifest carried. An empty or null list restores the
        /// built-in one rather than leaving the game with no companions — a content
        /// mistake must not cost every player their profile.
        /// </summary>
        public static void Publish(IReadOnlyList<AvatarDefinition> roster)
        {
            bool fromContent = roster != null && roster.Count > 0;

            var next = BuiltIn;
            if (fromContent)
            {
                next = new AvatarDefinition[roster.Count];
                for (int i = 0; i < roster.Count; i++) next[i] = roster[i];
            }

            _roster = next;
            _byId = IndexOf(next);
            IsFromContent = fromContent;

            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        static Dictionary<string, int> IndexOf(AvatarDefinition[] roster)
        {
            var map = new Dictionary<string, int>(roster.Length, StringComparer.Ordinal);
            for (int i = 0; i < roster.Length; i++)
                if (roster[i].IsValid) map[roster[i].Id] = i;
            return map;
        }

        /// <summary>What a save with no companion recorded is wearing.</summary>
        public static AvatarDefinition Default => _roster.Length > 0 ? _roster[0] : default;

        public static bool Exists(string id) => !string.IsNullOrEmpty(id) && _byId.ContainsKey(id);

        public static AvatarDefinition Find(string id)
            => !string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out int i) ? _roster[i] : default;

        public static bool IsUnlocked(AvatarDefinition avatar, int keeperLevel)
            => avatar.IsValid && keeperLevel >= avatar.UnlockLevel;

        public static bool IsUnlocked(string id, int keeperLevel) => IsUnlocked(Find(id), keeperLevel);

        /// <summary>
        /// The companion to actually draw for a stored id.
        ///
        /// An unknown id means the save came from a build that shipped a companion this
        /// one does not have — a rollback, or a device a drop ahead — so it falls back
        /// rather than drawing nothing. It deliberately does <em>not</em> check the
        /// unlock level: a player who earned a companion and was then caught by a
        /// retune keeps wearing it, and the id survives in the save either way.
        /// </summary>
        public static AvatarDefinition Resolve(string id)
        {
            var found = Find(id);
            return found.IsValid ? found : Default;
        }

        /// <summary>How many are unlocked at a keeper level, for the "3 of 31" caption.</summary>
        public static int UnlockedCount(int keeperLevel)
        {
            int count = 0;
            foreach (var avatar in _roster)
                if (keeperLevel >= avatar.UnlockLevel) count++;
            return count;
        }

        /// <summary>
        /// The next companion a player has not yet reached, or an invalid one when they
        /// hold the lot. Drives the "next at level 12" line without the screen having to
        /// know the roster is ordered by unlock level.
        /// </summary>
        public static AvatarDefinition NextLocked(int keeperLevel)
        {
            var best = default(AvatarDefinition);
            foreach (var avatar in _roster)
            {
                if (avatar.UnlockLevel <= keeperLevel) continue;
                if (!best.IsValid || avatar.UnlockLevel < best.UnlockLevel) best = avatar;
            }
            return best;
        }
    }
}
