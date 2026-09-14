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
        /// <summary>Raiders felled, bosses included, and bosses felled on their own. For the tasks.</summary>
        public int RaidersFelled { get; private set; }
        public int BossesFelled { get; private set; }

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

        /// <summary>
        /// Charms dealt and charms sprung, and how long the longest one stood.
        ///
        /// <para>
        /// <b>The stormglass is what this is really for, and it is the same question a bomb
        /// asks.</b> A stormglass is worth what is standing on the hill when it goes, so *when* to
        /// match it is the whole decision (40i, on the player's own board this time) — and a run
        /// where they are sprung the instant they land is a run by somebody who has not met the
        /// mechanic. Counted for all three, because the answer only means anything against the two
        /// whose timing genuinely does not matter.
        /// </para>
        /// <para>
        /// <b>Dealt and sprung are both counted, because they come apart.</b> A charm on the board
        /// when the run ends was never spent, and the gap between the two is exactly the evidence
        /// of that — the same shape <see cref="BombsDropped"/> and <see cref="BombsTapped"/> take,
        /// and for the same reason.
        /// </para>
        /// </summary>
        public int CharmsDealt { get; private set; }
        public int CharmsSprung { get; private set; }

        /// <summary>The longest a single stormglass stood on the field before it was matched.</summary>
        public float LongestHeld { get; private set; }

        float _heldTotal;
        int _heldCount;

        /// <summary>
        /// The mean time a stormglass stood before it was matched, in seconds.
        ///
        /// Over the ones that were sprung alone, for <see cref="MeanWait"/>'s reason: one still
        /// standing when the run ended has no wait yet, and folding it in at its current age would
        /// report a *shorter* average the longer it was ignored.
        /// </summary>
        public float MeanHeld => _heldCount > 0 ? _heldTotal / _heldCount : 0f;

        /// <summary>
        /// When each stormglass now on the field was dealt.
        ///
        /// <b>Keyed on the cell and re-keyed as it falls</b>, because a gem moves: a charm dealt
        /// into the top of a column is several cells lower by the time it is matched, and a map
        /// that did not follow it would report every one of them as never having been spent.
        /// </summary>
        readonly Dictionary<int, float> _held = new Dictionary<int, float>(4);

        internal void CharmDealt(SiegeCharm charm, int cell)
        {
            CharmsDealt++;

            // Only the one whose timing is a decision. A prism and a lance are worth the same
            // whenever they go, so a wait measured on them would be noise averaged into the one
            // reading here that means something.
            if (charm == SiegeCharm.Storm) _held[cell] = Elapsed;
        }

        /// <summary>A charmed cell falling from one cell into another.</summary>
        internal void CharmMoved(int from, int to)
        {
            if (from == to) return;
            if (!_held.TryGetValue(from, out float at)) return;

            _held.Remove(from);
            _held[to] = at;
        }

        /// <summary>
        /// Two cells exchanging everything they carry.
        ///
        /// <b>Its own method rather than two <see cref="CharmMoved"/> calls, and it has to be
        /// exactly self-inverse.</b> <c>SiegeBoard.Trade</c> is called twice by every trial swap —
        /// once to try a move and once to put the field back — so anything it does here is done
        /// and undone on every drag the player rejects. An exchange is its own inverse; a pair of
        /// one-way moves is not, because the first would overwrite what the second needs.
        /// </summary>
        internal void CharmSwapped(int a, int b)
        {
            if (a == b) return;

            bool hasA = _held.TryGetValue(a, out float atA);
            bool hasB = _held.TryGetValue(b, out float atB);

            if (!hasA && !hasB) return;

            if (hasB) _held[a] = atB; else _held.Remove(a);
            if (hasA) _held[b] = atA; else _held.Remove(b);
        }

        internal void CharmSprung(SiegeCharm charm, int cell)
        {
            CharmsSprung++;

            if (!_held.TryGetValue(cell, out float at)) return;
            _held.Remove(cell);

            float waited = Elapsed - at;
            if (waited < 0f) waited = 0f;

            _heldTotal += waited;
            _heldCount++;

            if (waited > LongestHeld) LongestHeld = waited;
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
        internal void RaiderFelled(bool boss)
        {
            RaidersFelled++;
            if (boss) BossesFelled++;
        }

        internal void BossMet(bool fuelled)
        {
            BossesMet++;
            if (fuelled) BossesMetFuelled++;
        }
    }
}
