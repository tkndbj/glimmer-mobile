using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// The singularity: what happens when three void stones line up
    /// (<see cref="SiegeLayout.Singularity"/>).
    ///
    /// <para>
    /// <b>Two halves on two boards, the curse's shape</b> (<c>SiegeBoard.Obsidian.cs</c>). On the
    /// field, the break swallows <em>every cell</em> into the one beat (<see cref="Devour"/>) -
    /// nothing it takes pays a ward and no charm it takes is sprung, because the field is not
    /// being matched, it is being eaten - and the ordinary collapse then deals a whole new field.
    /// On the hill, a beat later and booked with the break's own flight (invariant 37s), the
    /// line's whole weight falls on everything standing there (<see cref="Annihilate"/>).
    /// </para>
    /// <para>
    /// <b>What it is worth is the line's, with no number of its own</b> (invariant 37cg, the
    /// stormglass's bargain): every standing ward lands <see cref="SiegeTuning.VoidBolts"/> of
    /// its own bolts on every body, at its own rank and build, so the beam is worth nothing to a
    /// fallen line and most to a bought one. It has no colour, so nothing is off-colour to it.
    /// </para>
    /// <para>
    /// <b>It never lets a boss skip a stand.</b> Every point goes through <c>Wound</c>, the one
    /// door (37dj): a boss still walking on takes nothing, and one in place is taken to its
    /// stand's floor and no further.
    /// </para>
    /// <para>
    /// <b>The price is the field.</b> Whatever was standing on it - a charm being saved, a
    /// cursed stone two short of a break - goes into the hole unspent, which is what makes
    /// <em>when</em> a decision (invariant 40i) rather than a button.
    /// </para>
    /// </summary>
    public sealed partial class SiegeBoard
    {
        /// <summary>A singularity that has collapsed on the field and not yet fired on the hill.</summary>
        struct Beam
        {
            public float In;
        }

        readonly List<Beam> _beams = new List<Beam>(2);

        /// <summary>How many void stones are standing on the field right now.</summary>
        public int Singularities
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _cells.Length; i++)
                    if (_cells[i] == SiegeLayout.Singularity) n++;

                return n;
            }
        }

        /// <summary>Whether a collapsed singularity's beam is still on its way to the hill.</summary>
        public bool Beaming => _beams.Count > 0;

        /// <summary>
        /// Folds a collapsed singularity into this beat: if any run on it is a run of void
        /// stones, the whole field goes with it and the beam is booked.
        ///
        /// <para>
        /// <b>Before <c>Spring</c>, and the charms are taken off first</b>, so nothing the hole
        /// swallows goes off inside it: a stormglass, an hourglass and an anvil sprung on one beat
        /// because they happened to be standing there would be three payoffs nobody chose. They
        /// are lost, and that is the stone's price.
        /// </para>
        /// <para>
        /// <b>Every cell is paid as the stone</b>, which no ward burns, so the fuel loop below
        /// reads one answer for the whole field and books nothing - the curse's device
        /// (<see cref="Unbound"/>), used for the opposite reason: there a stone pays nothing
        /// because it is a stone, here a gem pays nothing because it was eaten.
        /// </para>
        /// </summary>
        void Devour(HashSet<int> hit, SiegeBeat beat)
        {
            // Asked before anything is walked, for `Unbound`'s reason: this runs on every beat
            // of every cascade, and a field that deals no void stone has none.
            if (!Layout.Singular) return;

            bool collapsed = false;

            foreach (int cell in hit)
                if (_cells[cell] == SiegeLayout.Singularity
                    && _paid[cell] == SiegeLayout.Singularity)
                {
                    collapsed = true;
                    break;
                }

            if (!collapsed) return;

            // In cell order, for `beat.Cleared`'s reason: the order decides the order the view
            // draws the field going in, and a set is not promised to enumerate one way.
            for (int cell = 0; cell < _cells.Length; cell++)
            {
                if (_cells[cell] == Hole) continue;

                if (_cells[cell] == SiegeLayout.Singularity && hit.Contains(cell)
                    && _paid[cell] == SiegeLayout.Singularity)
                    beat.Eaters.Add(cell);

                _paid[cell] = SiegeLayout.Singularity;
                _charms[cell] = SiegeCharm.None;

                hit.Add(cell);
            }

            beat.Swallowed = true;

            // A curse that broke on this same beat still falls: it was booked by `Unbound`
            // before the field went, and the lists it recorded are its own.
            _beams.Add(new Beam { In = SiegeTuning.BeamLands(beat.Depth - 1) });
        }

        /// <summary>
        /// Fires whatever beam has finished gathering: every standing ward lands
        /// <see cref="SiegeTuning.VoidBolts"/> of its bolts on every raider on the hill.
        ///
        /// <para>
        /// <b>Reported whether or not it touched anything</b> (<c>SiegeReport.Beamed</c>), for
        /// the furnace's rule: a beam over an empty hill is drawn firing at nothing, which is a
        /// picture of a wrong moment rather than of a broken gem.
        /// </para>
        /// <para>
        /// <b>One strike a body, the wards summed</b>, because the picture is one beam: four
        /// figures over each raider would be four bolts, which is the stormglass.
        /// </para>
        /// </summary>
        void Annihilate(float dt)
        {
            for (int i = _beams.Count - 1; i >= 0; i--)
            {
                var beam = _beams[i];
                beam.In -= dt;

                if (beam.In > 0f)
                {
                    _beams[i] = beam;
                    continue;
                }

                _beams.RemoveAt(i);
                _report.Beamed = true;

                for (int r = 0; r < _raiders.Count; r++)
                {
                    var raider = _raiders[r];
                    if (!raider.Alive || raider.Wait > 0f) continue;

                    int took = Wound(raider, SiegeTuning.VoidBolts * Volley(raider));
                    if (took <= 0) continue;

                    bool killed = Fell(raider);

                    _report.Beam.Add(new SiegeStrike(raider.Id, took, killed));
                }
            }
        }

        /// <summary>
        /// One own-colour bolt from every ward still standing, summed, as it would land on
        /// <paramref name="raider"/> before anything multiplies it.
        ///
        /// <b>The unit every free payoff on the hill is counted in</b> (invariant 37cg): the beam
        /// lands <see cref="SiegeTuning.VoidBolts"/> of it and the curse withers
        /// <see cref="SiegeTuning.WitherPercent"/> of it. One spelling, because two would be two
        /// opinions about what "the line's own weight" is, and the day a turret learns something
        /// new about its bolt both payoffs learn it here.
        /// </summary>
        int Volley(SiegeRaider raider)
        {
            int volley = 0;

            for (int w = 0; w < _wards.Length; w++)
            {
                var ward = _wards[w];
                if (!ward.Alive) continue;

                volley += SiegeTuning.DamageTo(raider.Kind, ward.Rank, true, ward.Build);
            }

            return volley;
        }
    }
}
