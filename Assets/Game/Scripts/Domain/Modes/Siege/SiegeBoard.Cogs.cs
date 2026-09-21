using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// Cogs, which are dropped on the hill and taken with a finger.
    ///
    /// <para>
    /// <b>The whole point of moving them here is that greed is the only reliable way to make
    /// somebody look somewhere.</b> A cog used to be dealt into the gem field and taken by a match
    /// beside it — a good mechanic in the wrong place, because it put one more thing to solve in
    /// the half of the screen the player was already staring at. On the hill it is treasure in the
    /// enemy's half with a clock on it, so the mode's own reward lands where the mode's own
    /// subject is. It is the genre's oldest answer to a two-halved screen and it is the same one
    /// the bomber already uses (invariant 40i): the arrow runs from the player's finger up, never
    /// from the hill down into the board (invariant 40h).
    /// </para>
    /// </summary>
    public sealed partial class SiegeBoard
    {
        readonly List<SiegeCog> _cogs = new List<SiegeCog>(4);

        /// <summary>Every cog lying on the hill, newest last.</summary>
        public IReadOnlyList<SiegeCog> Cogs => _cogs;

        /// <summary>
        /// Rolls a cog for a raider that has just been felled.
        ///
        /// <para>
        /// <b>It draws from the hill's own stream and never the field's</b>, which is invariant
        /// 41 read the right way round. A kill happens when the player makes it happen, so a cog
        /// rolled out of <c>_rng</c> would make <em>which gems the field deals</em> depend on when
        /// somebody killed something — two players on the same level would be dealt different
        /// boards for reasons that have nothing to do with the board. <c>_hill</c> exists for
        /// exactly the draws a player's taps order in time but not in number, and it has been
        /// without a job since the weaver was withdrawn.
        /// </para>
        /// <para>
        /// <b>Nothing is dropped for a ward that cannot use it</b>, which is the one case worth
        /// stating: a cog that pays nothing is a thing the player reaches for and is not thanked
        /// for, and there is no honest second prize to hand out instead — fuel from a kill would
        /// be damage the player never matched for, which is exactly what invariant 39 prices and
        /// nothing here could price.
        /// </para>
        /// </summary>
        void Cog(SiegeRaider raider)
        {
            if (raider == null || Layout.Cogs <= 0) return;
            if (_cogs.Count >= SiegeTuning.MostCogs) return;

            // One draw, always, so the hill's stream advances the same number of times however the
            // roll lands. A stream whose length depends on its own answers is one nothing can
            // reproduce from a seed.
            uint roll = Hill() % 100u;
            if (roll >= (uint)Layout.Cogs) return;

            int ward = Layout.WardOf(SiegeLayout.Letters[raider.Colour]);
            if (ward < 0 || !_wards[ward].Upgradable) return;

            int row = SiegeTuning.RowOf(raider.March);

            var cog = new SiegeCog(_minted++, raider.Lane, row, ward, _wards[ward].Colour);

            _cogs.Add(cog);
            _report.Cogs.Add(cog);

            Attention.CogDropped();
        }

        /// <summary>
        /// Tears a rank off a ward and drops it on the hill as an ordinary cog, where the
        /// harrower is standing. Answers whether it really took one.
        ///
        /// <para>
        /// <b>The rank and the cog are one call, so the two halves can never come apart.</b>
        /// A rank taken with nothing dropped is an overlord's sunder under a second name
        /// (invariant 37z), and a cog dropped with no rank taken is a gift - which is the whole
        /// difference between this verb and that one (<see cref="SiegeSpell.Harrow"/>).
        /// </para>
        /// <para>
        /// <b>It is an ordinary <see cref="SiegeCog"/> and deliberately not a kind of its own.</b>
        /// Everything that already draws, ages, tramples and spends one therefore works on this
        /// without being taught - including <see cref="Take"/>'s re-asked ladder, which is the
        /// clause that matters here: a ward the harrower just robbed is by construction one rung
        /// short, so the cog it dropped is always takeable.
        /// </para>
        /// <para>
        /// <b>It draws from no stream at all</b>, which is the one way it differs from
        /// <see cref="Cog"/>: a drop on a kill is a roll and this is a certainty, so there is
        /// nothing to reproduce and nothing to advance (invariant 41).
        /// </para>
        /// <para>
        /// <b>Refused when the hill is full</b>, for <see cref="Cog"/>'s reason - the cap is what
        /// stops a duel ending under a carpet of cogs nobody can reach - and the rank stays on
        /// the ward when it is. The caller still smites (37dn), so a refusal here is never a
        /// boss that did nothing.
        /// </para>
        /// </summary>
        bool Scatter(SiegeRaider caster, int at)
        {
            if (caster == null || at < 0 || at >= _wards.Length) return false;
            if (_cogs.Count >= SiegeTuning.MostCogs) return false;

            var ward = _wards[at];
            if (!ward.Alive || !ward.Sunder()) return false;

            var cog = new SiegeCog(_minted++, caster.Lane, SiegeTuning.RowOf(caster.March),
                                   at, ward.Colour);

            _cogs.Add(cog);
            _report.Cogs.Add(cog);

            Attention.CogDropped();
            return true;
        }

        /// <summary>
        /// Counts every cog on the hill down, and takes away the ones that ran out.
        ///
        /// <b>Trampled rather than kept</b>, because the deadline is what makes reaching for one a
        /// decision — see <see cref="SiegeTuning.CogLies"/>. Walked backwards so a removal cannot
        /// skip the entry behind it.
        /// </summary>
        void Age(float dt)
        {
            for (int i = _cogs.Count - 1; i >= 0; i--)
            {
                var cog = _cogs[i];
                cog.Left -= dt;

                if (cog.Left > 0f) continue;

                _cogs.RemoveAt(i);
                _report.Trampled.Add(cog.Id);

                Attention.CogTrampled();
            }
        }

        /// <summary>
        /// Takes the cog with this id and spends it on the ward it names.
        ///
        /// <para>
        /// <b>The ladder is re-asked here rather than trusted from the drop</b>, because a cog can
        /// be dropped for a ward that is one rung short and lie there while a second cog rises the
        /// same turret to the top. Refusing is the honest answer and the view says it out loud —
        /// a tap that quietly did nothing would read as a broken control.
        /// </para>
        /// </summary>
        public SiegeTaken Take(int id)
        {
            int at = IndexOfCog(id);
            if (at < 0) return SiegeTaken.Refused;

            var cog = _cogs[at];
            var ward = _wards[cog.Ward];

            if (!ward.Upgradable) return SiegeTaken.Refused;

            ward.Rank++;
            _cogs.RemoveAt(at);

            // After the Upgradable refusal, for the reason Detonate gives: a cog a maxed ward
            // would not take is still lying there and is still a decision nobody has made.
            Attention.CogTaken();

            return new SiegeTaken(cog.Ward, ward.Rank, cog.Colour, cog.Lane, cog.Row);
        }

        /// <summary>The cog lying on this box of the hill, or -1. Mirrors <c>BombAt</c>.</summary>
        public int CogAt(int lane, int row)
        {
            for (int i = 0; i < _cogs.Count; i++)
                if (_cogs[i].Lane == lane && _cogs[i].Row == row) return _cogs[i].Id;

            return -1;
        }

        int IndexOfCog(int id)
        {
            for (int i = 0; i < _cogs.Count; i++)
                if (_cogs[i].Id == id) return i;

            return -1;
        }

        /// <summary>
        /// The hill's own stream: xorshift32, all 32-bit, exactly as <c>Next</c> is.
        ///
        /// <b>Separate from the field's, and narrow on purpose.</b> It carries only the draws that
        /// are ordered by the clock rather than counted by the player — see <see cref="Cog"/>.
        /// </summary>
        uint Hill()
        {
            uint x = _hill;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _hill = x == 0u ? 2463534242u : x;
            return _hill;
        }
    }
}
