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

        /// <summary>
        /// Credits that buy this companion outright, or 0 when it cannot be bought.
        ///
        /// The second path to the same companion, and the only one most of the roster will
        /// ever be reached by: three-starring an entire hundred-glade catalog lands a player
        /// around keeper level 15, so a gate above that is unreachable by play for years.
        /// See <see cref="CompanionLedger"/> for what holding one means and
        /// <c>ManifestCompanionDto.unlockCost</c> for why zero is "not for sale".
        /// </summary>
        public readonly int UnlockCost;

        public AvatarDefinition(string id, string portrait, string animated, int unlockLevel,
                                int unlockCost = 0)
        {
            Id = id;
            Portrait = string.IsNullOrEmpty(portrait) ? id : portrait;
            Animated = animated ?? string.Empty;
            UnlockLevel = unlockLevel < 0 ? 0 : unlockLevel;
            UnlockCost = unlockCost < 0 ? 0 : unlockCost;
        }

        public bool IsValid => !string.IsNullOrEmpty(Id);

        public bool HasAnimation => !string.IsNullOrEmpty(Animated);

        /// <summary>True when credits are a way to get this one. See <see cref="UnlockCost"/>.</summary>
        public bool IsForSale => IsValid && UnlockCost > 0;

        /// <summary>
        /// True when nothing gates this companion — the starter every account begins with.
        ///
        /// Exactly one companion should answer true, and <c>ContentValidation</c> fails the
        /// build when none does: a roster where everything is gated leaves a new player with
        /// nobody to wear.
        /// </summary>
        public bool IsStarter => IsValid && UnlockLevel <= 1;

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
    /// <b>This type answers questions about content, never about a player.</b> A companion
    /// is held either because the keeper level reached its gate or because it was bought,
    /// and the second half is save state — so the composite rule lives in
    /// <see cref="CompanionLedger"/> and every screen asks there.
    /// <see cref="ReachedBy"/> is deliberately named for the narrow question it answers,
    /// because it used to be called <c>IsUnlocked</c> and a call site that reads like the
    /// whole rule while checking half of it is how a paid companion silently stays locked.
    /// </para>
    /// <para>
    /// The level half is <em>derived</em>, for the same reason XP is: it recomputes, it can
    /// be retuned for existing players, and it cannot be lost. The purchased half cannot be
    /// derived from anything — nothing observable implies "this player paid 8,000 credits"
    /// — so it is stored, and it is stored in the one shape invariant 11b permits: a set of
    /// permanent ids that only ever grows, joined by union. A retune that moves a gate above
    /// somebody's level takes nothing away, because <see cref="Resolve"/> keeps a player on
    /// whatever they are already wearing.
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
            new AvatarDefinition("timber",   "timber",   "c2", 3, 1000),
            new AvatarDefinition("sprocket", "sprocket", "c3", 5, 1400),
            new AvatarDefinition("thistle",  "thistle",  "c4", 7, 1800),
            new AvatarDefinition("puff",     "puff",     "c1", 9, 2200),
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

        /// <summary>
        /// Whether the keeper level alone has reached this companion's gate.
        ///
        /// <b>Half of the unlock rule.</b> A player may also have bought it, which this
        /// cannot see — ask <see cref="CompanionLedger.IsHeld(AvatarDefinition, int)"/> for
        /// the question a screen actually has. Named for its narrowness on purpose; see the
        /// type's remarks.
        /// </summary>
        public static bool ReachedBy(AvatarDefinition avatar, int keeperLevel)
            => avatar.IsValid && keeperLevel >= avatar.UnlockLevel;

        public static bool ReachedBy(string id, int keeperLevel) => ReachedBy(Find(id), keeperLevel);

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

        /// <summary>
        /// The starter every account begins wearing — the one companion nothing gates.
        ///
        /// Falls back to <see cref="Default"/> rather than to nothing, because a roster that
        /// gates everything is a content mistake that must not cost a player their profile.
        /// <c>ContentValidation</c> fails the build on it separately.
        /// </summary>
        public static AvatarDefinition Starter
        {
            get
            {
                foreach (var avatar in _roster)
                    if (avatar.IsStarter) return avatar;

                return Default;
            }
        }

        /// <summary>
        /// The cheapest companion the player does not hold, or an invalid one when there is
        /// nothing left to sell. Drives the "next friend" prompt and the shop's default sort.
        ///
        /// Takes the held set as a predicate rather than reading it, so this stays a question
        /// about content that a test can ask without a save file.
        /// </summary>
        public static AvatarDefinition CheapestUnheld(Func<AvatarDefinition, bool> isHeld)
        {
            var best = default(AvatarDefinition);

            foreach (var avatar in _roster)
            {
                if (!avatar.IsForSale) continue;
                if (isHeld != null && isHeld(avatar)) continue;
                if (!best.IsValid || avatar.UnlockCost < best.UnlockCost) best = avatar;
            }

            return best;
        }
    }
}
