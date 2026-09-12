using System;
using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// What a bomber leaves behind: a live bomb standing on the hill where it fell, and the tap
    /// that sets it off.
    ///
    /// <para>
    /// <b>Its own file because it is the one thing in this mode the player does to the hill.</b>
    /// Every other input goes into the gem field and reaches the raiders through a ward; a bomb is
    /// found on the enemy's own ground and hit directly, which is what makes the hill something to
    /// watch rather than something to glance at.
    /// </para>
    /// <para>
    /// <b>It replaced a mechanic that reached the other way and was withdrawn whole.</b> A weaver
    /// locking cells and a thief stealing gems put the *hill's* work on the *player's* board, and
    /// the owner's verdict was that a raider has no business standing among the jewels. A bomb is
    /// the same idea with the arrow reversed, and it is the half that survived.
    /// </para>
    /// </summary>
    public sealed partial class SiegeBoard
    {
        readonly List<SiegeBomb> _bombs = new List<SiegeBomb>(4);

        /// <summary>Bombs standing on the hill, in the order they were dropped.</summary>
        public IReadOnlyList<SiegeBomb> Bombs => _bombs;

        /// <summary>
        /// What one bomb takes off everything in its reach.
        ///
        /// <b>Set from the published firepot rather than read from a constant</b>, because how
        /// hard a firepot hits is content (<c>UtilityCatalog</c>) and a bomb has to hit for
        /// exactly the same — the player is told it is a firepot, and two numbers that mean one
        /// thing is two numbers that can drift. <see cref="SiegeTuning.BombDamage"/> is the
        /// fallback for a build that has never seen a store.
        /// </summary>
        public int BombDamage { get; set; } = SiegeTuning.BombDamage;

        /// <summary>
        /// Leaves a bomb where a raider fell, if that raider was carrying one.
        ///
        /// <para>
        /// <b>Called from <see cref="Fell"/> and nowhere else</b>, which is the whole reason it is
        /// safe: a raider dies in four ways — a bolt, a firepot, a storm and a beam that overkills
        /// it — and a drop hung off any one of them is a drop the other three do not do. That is
        /// the rule the retired give-back kept, and it is the only part of that machine worth
        /// keeping.
        /// </para>
        /// <para>
        /// <b>At the box it died in, not at the point.</b> A bomb is tapped, and what a player taps
        /// is one of the twenty boxes the hill is already divided into for a firepot
        /// (<c>SiegeTuning.Lanes</c> by <c>BlastRows</c>) — so it is stored as a box and the view
        /// draws it at the middle of one. Storing a raw march would mean the drawing and the tap
        /// test each doing their own arithmetic on it, which is invariant 39k's own bug waiting to
        /// be written a second time.
        /// </para>
        /// <para>
        /// <b>Silently dropped at the cap</b> rather than replacing an older one: a bomb the player
        /// has been saving vanishing because a bomber died somewhere else is the game taking back
        /// something it gave, and a fourth bomb is worth less than the third was.
        /// </para>
        /// </summary>
        void Drop(SiegeRaider raider)
        {
            if (raider == null || !SiegeTuning.LeavesABomb(raider.Kind)) return;
            if (_bombs.Count >= SiegeTuning.MostBombs) return;

            int row = SiegeTuning.RowOf(raider.March);
            if (row < 0) row = 0;
            if (row >= SiegeTuning.BlastRows) row = SiegeTuning.BlastRows - 1;

            var bomb = new SiegeBomb(_minted++, raider.Lane, row, raider.Colour);

            _bombs.Add(bomb);
            _report.Dropped.Add(bomb);
        }

        /// <summary>
        /// Sets off the bomb standing in this box, and answers what it absorbed.
        ///
        /// <para>
        /// <b>It goes off where it stands, at once.</b> There is nothing to aim: a bomb is already
        /// somewhere, and asking the player to pick a target after tapping it would be asking them
        /// to choose twice for one decision — the decision is <em>when</em>, and the answer is on
        /// the hill in front of them.
        /// </para>
        /// <para>
        /// <b>The same plus a firepot burns</b> (<see cref="SiegeTuning.BombReach"/>), so a player
        /// who has used one knows what the other takes without being told.
        /// </para>
        /// <para>
        /// <b>Nought when nothing was caught, and the bomb stays.</b> A bomb that went off on an
        /// empty stretch of hill and left nothing behind is a thing the player would read as
        /// broken; a firepot is handed back for the same reason (<c>SiegeUtility.Apply</c>).
        /// </para>
        /// </summary>
        public int Detonate(int id, List<SiegeStrike> into)
        {
            int at = IndexOfBomb(id);
            if (at < 0) return 0;

            var bomb = _bombs[at];
            int absorbed = Blast(bomb.Lane, bomb.Row, BombDamage, into);

            if (absorbed <= 0) return 0;

            _bombs.RemoveAt(at);
            return absorbed;
        }

        /// <summary>The bomb standing in this box of the hill, or -1.</summary>
        public int BombAt(int lane, int row)
        {
            for (int i = 0; i < _bombs.Count; i++)
                if (_bombs[i].Lane == lane && _bombs[i].Row == row) return _bombs[i].Id;

            return -1;
        }

        int IndexOfBomb(int id)
        {
            for (int i = 0; i < _bombs.Count; i++)
                if (_bombs[i].Id == id) return i;

            return -1;
        }

        /// <summary>
        /// Takes a raider off the hill: the one door, so nothing can be counted twice and nothing
        /// a raider was carrying can be forgotten.
        ///
        /// <para>
        /// It answers whether this was the blow that killed it, so the four callers can report a
        /// kill without each keeping their own copy of the rule.
        /// </para>
        /// </summary>
        bool Fell(SiegeRaider raider)
        {
            if (raider == null || !raider.Alive || raider.Health > 0) return false;

            raider.Alive = false;
            _felled++;

            Drop(raider);
            Cog(raider);

            return true;
        }
    }
}
