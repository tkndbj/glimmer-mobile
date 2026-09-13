using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// What a run can say about whether the player ever looked up.
    ///
    /// <para>
    /// <b>The one question this mode cannot answer from the desk.</b> A siege puts two things in
    /// front of one pair of eyes — a gem field the hands live on, and a hill that walks whether
    /// anybody watches it or not — and the fault it came back with was <em>"I barely look up"</em>
    /// (invariant 37bl). Every instrument this project has reads the model: the hold simulation,
    /// <c>ways</c>, a contact sheet, a render. None of them can see where a person is looking, and
    /// no amount of reasoning settles it. So it is measured.
    /// </para>
    /// <para>
    /// <b>Three readings, each a decision the hill offers and the board can score.</b> A bomb is
    /// left standing where a bomber died and goes off where it stands, so the decision is
    /// <em>when</em> (40i) — and how long one lies there unnoticed is the sharpest single number
    /// here. A cog is a rank the player has to reach for before it is trampled, so one that times
    /// out is one nobody saw. And a boss walks in wearing a colour: whether the ward of that colour
    /// was fuelled when it arrived says whether the hill was read ahead of time or reacted to.
    /// </para>
    /// <para>
    /// <b>Counted rather than streamed, and reported once at the end of a run.</b> A telemetry
    /// event per bomb would be a stream nobody reads and a cost that scales with play; what is
    /// wanted is a figure per run, which is also the shape every other reading in this project
    /// takes. Nothing here is a game rule — no code outside the recording sites may branch on it —
    /// so the whole feature is this file plus five one-line calls, and removing it is removing
    /// them.
    /// </para>
    /// </summary>
    public sealed class SiegeAttention
    {
        /// <summary>Bombs left standing on the hill, and how many were spent.</summary>
        public int BombsDropped { get; private set; }
        public int BombsTapped { get; private set; }

        /// <summary>Cogs offered, taken, and lost to the clock.</summary>
        public int CogsDropped { get; private set; }
        public int CogsTaken { get; private set; }
        public int CogsTrampled { get; private set; }

        /// <summary>Bosses that arrived, and how many met a ward of their own colour with fuel in it.</summary>
        public int BossesMet { get; private set; }
        public int BossesMetFuelled { get; private set; }

        /// <summary>The run's own clock, which is the only one a bomb's age can be measured on.</summary>
        public float Elapsed { get; private set; }

        float _waitTotal;

        /// <summary>The longest a single bomb stood before it was tapped.</summary>
        public float LongestWait { get; private set; }

        /// <summary>
        /// When each standing bomb was dropped.
        ///
        /// <para>
        /// Held here rather than on <c>SiegeBomb</c> so the model carries nothing this feature
        /// needs: a bomb is a position and a colour, and the day this reading is not wanted any
        /// more nothing about the board has to change back.
        /// </para>
        /// </summary>
        readonly Dictionary<int, float> _dropped = new Dictionary<int, float>(4);

        /// <summary>
        /// The mean wait across the bombs that were tapped, in seconds.
        ///
        /// <para>
        /// Over the tapped ones alone, deliberately. A bomb still standing when the run ended has
        /// no wait yet — folding it in at its current age would report a shorter average the
        /// longer it was ignored, which inverts the measurement. What that bomb is evidence of is
        /// counted instead, as the gap between <see cref="BombsDropped"/> and
        /// <see cref="BombsTapped"/>.
        /// </para>
        /// </summary>
        public float MeanWait => BombsTapped > 0 ? _waitTotal / BombsTapped : 0f;

        internal void Tick(float dt) => Elapsed += dt;

        internal void BombDropped(int id)
        {
            BombsDropped++;
            _dropped[id] = Elapsed;
        }

        internal void BombTapped(int id)
        {
            BombsTapped++;

            if (!_dropped.TryGetValue(id, out float at)) return;
            _dropped.Remove(id);

            float waited = Elapsed - at;
            if (waited < 0f) waited = 0f;

            _waitTotal += waited;
            if (waited > LongestWait) LongestWait = waited;
        }

        internal void CogDropped() => CogsDropped++;

        internal void CogTaken() => CogsTaken++;

        internal void CogTrampled() => CogsTrampled++;

        /// <summary>
        /// A boss has walked on.
        /// </summary>
        /// <param name="fuelled">
        /// Whether the ward wearing the boss's colour had fuel in it at that moment. Asked at the
        /// muster rather than when the boss reaches its station, because the question is whether
        /// the player banked <em>ahead</em> of the fight rather than whether they reacted to it.
        /// </param>
        internal void BossMet(bool fuelled)
        {
            BossesMet++;
            if (fuelled) BossesMetFuelled++;
        }
    }
}
