using UnityEngine;

namespace GlimmerGrove.Progression
{
    /// <summary>
    /// The next thing the player's keeper level will unlock, and how far along they are.
    ///
    /// <para>
    /// <b>Why this is not the rank bar.</b> The hub already draws progress through the
    /// current rank, and that bar has a specific weakness: it empties every time it fills.
    /// A player who levels up is immediately shown a bar at zero, which is the moment the
    /// game is least able to say why the next one is worth having. Worse, it never names a
    /// goal — it measures distance travelled rather than distance to something, and a
    /// player cannot want a number.
    /// </para>
    /// <para>
    /// This measures the span between the unlock just earned and the one coming, so it is
    /// nearly always partly filled and it always has a name attached. That is the
    /// endowed-progress effect used honestly: the player really has done three of the five
    /// ranks, and being shown it that way rather than as "rank 8, 0% into rank 8" is a
    /// framing change, not a fabricated head start. Nothing here is stored and nothing is
    /// seeded — it is a pure function of the keeper level and the roster, which is what
    /// keeps it correct when either is retuned.
    /// </para>
    /// </summary>
    public readonly struct UnlockGoal
    {
        /// <summary>The companion coming next. Empty when the roster holds nothing further.</summary>
        public readonly string AvatarId;

        /// <summary>Its loc key, derived from the id. See <see cref="AvatarDefinition.NameKey"/>.</summary>
        public readonly string NameKey;

        /// <summary>The keeper level that unlocks it.</summary>
        public readonly int AtLevel;

        /// <summary>
        /// Where the span starts: the highest level that has already unlocked something,
        /// or 1 for a player who has not passed one yet.
        /// </summary>
        public readonly int FromLevel;

        /// <summary>How far through the span the player is, 0 to 1.</summary>
        public readonly float Progress01;

        /// <summary>Whole ranks still to climb. At least 1 while this is valid.</summary>
        public readonly int RanksToGo;

        UnlockGoal(string avatarId, string nameKey, int atLevel, int fromLevel,
                   float progress01, int ranksToGo)
        {
            AvatarId = avatarId ?? string.Empty;
            NameKey = nameKey ?? string.Empty;
            AtLevel = atLevel;
            FromLevel = fromLevel;
            Progress01 = Mathf.Clamp01(progress01);
            RanksToGo = ranksToGo < 0 ? 0 : ranksToGo;
        }

        /// <summary>Everything on the roster is already unlocked, so there is nothing to aim at.</summary>
        public static readonly UnlockGoal None = new UnlockGoal(null, null, 0, 0, 1f, 0);

        public bool IsValid => !string.IsNullOrEmpty(AvatarId) && AtLevel > 0;

        /// <summary>
        /// What the player is working toward right now.
        ///
        /// <para>
        /// The span deliberately starts at the <em>previous</em> unlock rather than at the
        /// current rank. Starting at the current rank would make the bar empty on the rank
        /// after every unlock and then again on every rank after that, which is the
        /// behaviour this exists to replace. Starting at the previous unlock means the bar
        /// only empties when something was actually collected — and a player who is four
        /// ranks into a five-rank span sees a bar that is four fifths full, because it is.
        /// </para>
        /// </summary>
        public static UnlockGoal Next(PlayerLevel level)
        {
            // Unheld, not merely unreached. A companion the player bought is already theirs,
            // and aiming this bar at one is worse than showing no goal at all — it tells
            // somebody who just spent 9,000 credits that they have four ranks to climb for
            // the friend they are looking at.
            var target = CompanionLedger.NextUnheld(level.Level);
            if (!target.IsValid) return None;

            int from = PreviousUnlockLevel(level.Level);

            // Guards a roster where two companions share an unlock level, or one is
            // authored at or below the span's floor: the division would be by zero and the
            // bar would read as either empty or complete at random.
            int span = target.UnlockLevel - from;
            if (span < 1) span = 1;

            // The fractional rank counts. Without it the bar advances in visible steps and
            // sits still in between, which on a five-rank span means it moves four times in
            // several days of play.
            float travelled = level.Level - from + Mathf.Clamp01(level.Progress01);

            return new UnlockGoal(target.Id, target.NameKey, target.UnlockLevel, from,
                                  travelled / span, target.UnlockLevel - level.Level);
        }

        /// <summary>
        /// The highest unlock level the player has already passed, or 1 when they have
        /// passed none. The floor of the span.
        /// </summary>
        static int PreviousUnlockLevel(int keeperLevel)
        {
            int best = 1;

            foreach (var avatar in AvatarCatalog.All)
            {
                if (!avatar.IsValid) continue;
                if (avatar.UnlockLevel > keeperLevel) continue;
                if (avatar.UnlockLevel > best) best = avatar.UnlockLevel;
            }

            return best;
        }
    }
}
