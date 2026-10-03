using System;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// The gravity well (<c>UtilityKind.Gravity</c>): one of four places on the hill opens, every
    /// raider standing on the hill is dragged into it, held there, and let go slowed.
    ///
    /// <para>
    /// <b>All of it happens in the model, and that is the whole design.</b> The view draws a
    /// raider wherever the model says it is, once a frame, so a pull the model performs over
    /// <see cref="SiegeTuning.GravityGather"/> is a pull the drawing follows for free - the
    /// anvil's bargain (<c>SiegeRaider.Heave</c>). It is also what makes the item combine
    /// honestly with everything else: a firepot thrown at the clump, a splash turret firing into
    /// it and a blow landing on the line all read where a body <em>is</em>
    /// (<c>SiegeRaider.Column</c>), which is where it is drawn (invariant 39k).
    /// </para>
    /// <para>
    /// <b>It hurts nothing, so it costs the grade nothing</b> (invariant 39). What it sells is
    /// seconds and a place - a finish, never a grade: damage is still only ever fuel and fuel is
    /// still only ever a match.
    /// </para>
    /// <para>
    /// <b>It draws nothing from either random stream</b>, so a field dealt after a well is the
    /// field that would have been dealt without it (invariant 41: how often a stream is drawn
    /// from is content).
    /// </para>
    /// </summary>
    public sealed partial class SiegeBoard
    {
        /// <summary>
        /// Seconds the open well has left, the gather and the hold together. Nought when no well
        /// is open.
        ///
        /// <b>One clock on the board</b>, for the roar's and the hourglass's reason: no raider
        /// holds a copy, so nothing can be left held after it has run out.
        /// </summary>
        float _sink;

        /// <summary>The hold the well was bought for, in seconds - the tail of <see cref="_sink"/>.</summary>
        float _sinkHold;

        int _well = -1;

        /// <summary>Whether a gravity well is open on the hill right now.</summary>
        public bool Sinking => _sink > 0f;

        /// <summary>Which of the four wells is open, or -1.</summary>
        public int Well => _sink > 0f ? _well : -1;

        /// <summary>Seconds the open well has left, gather and hold together. For a drawing.</summary>
        public float SinkLeft => _sink;

        /// <summary>Whether the open well has finished dragging the hill in and is holding it.</summary>
        public bool Gathered => _sink > 0f && _sink <= _sinkHold;

        /// <summary>
        /// How many bodies a well opened now would take: everything standing on the hill that
        /// is not a boss.
        ///
        /// What <c>SiegeUtility.Would</c> asks before the item is spent - a well over an empty
        /// hill, or over a boss alone, is refused and costs the player a tap.
        /// </summary>
        public int Pullable
        {
            get
            {
                int n = 0;

                for (int i = 0; i < _raiders.Count; i++)
                {
                    var raider = _raiders[i];
                    if (raider.Alive && raider.OnTheHill && !raider.Boss) n++;
                }

                return n;
            }
        }

        /// <summary>
        /// Opens a well, and answers how many bodies it took hold of.
        ///
        /// <para>
        /// <b>Refused while one is already open</b>, rather than extended or moved: two wells
        /// would be two answers to where a body is being dragged, and the one clock this is kept
        /// on is what guarantees every body is let go.
        /// </para>
        /// <para>
        /// <b>It takes who is standing there now and nobody after</b> - the hex's rule
        /// (<c>SiegeRaider.Hexed</c>), and the thing that makes <em>when</em> a decision: opened
        /// over a crowd it takes the crowd, and a wave that steps out a second later walks past
        /// it.
        /// </para>
        /// </summary>
        /// <param name="well">Which of <see cref="SiegeTuning.GravityWells"/>.</param>
        /// <param name="tenths">How long it holds once the hill is in it, in tenths of a second.</param>
        public int Gravity(int well, int tenths)
        {
            if (well < 0 || well >= SiegeTuning.GravityWells || tenths <= 0) return 0;
            if (_sink > 0f) return 0;

            int taken = 0;

            for (int i = 0; i < _raiders.Count; i++)
                if (_raiders[i].Sink()) taken++;

            if (taken <= 0) return 0;

            _well = well;
            _sinkHold = tenths / 10f;
            _sink = SiegeTuning.GravityGather + _sinkHold;

            return taken;
        }

        /// <summary>
        /// Runs the well's clock down by <paramref name="dt"/> and answers what share of the way
        /// still to go a held body closes this step: nought with no well open, one once the hill
        /// is in.
        ///
        /// <para>
        /// <b>A share of what is left rather than a speed</b>, so every body arrives on the same
        /// frame however far it started - a raider at the line and one a step from the well are
        /// both in it when the gather ends, which is what "all of them" has to mean. The share is
        /// taken off a curve that starts slow and ends fast, because that is what falling is.
        /// </para>
        /// <para>
        /// The frame the clock reaches nought is the frame every body is let go
        /// (<see cref="Release"/>), from here rather than from each body's own step, so a body
        /// the walk skips - one already dead - cannot keep the well open.
        /// </para>
        /// </summary>
        float Draw(float dt)
        {
            if (_sink <= 0f || dt <= 0f) return 0f;

            float gather = _sink - _sinkHold;

            _sink -= dt;

            if (_sink <= 0f)
            {
                Release();
                return 0f;
            }

            if (gather <= dt) return 1f;

            float whole = SiegeTuning.GravityGather;
            float was = 1f - gather / whole;
            float now = 1f - (gather - dt) / whole;

            float before = 1f - was * was;
            float after = 1f - now * now;

            return before <= 0f ? 1f : 1f - after / before;
        }

        /// <summary>Moves one held body <paramref name="share"/> of the way it still has to go.</summary>
        void Sink(SiegeRaider raider, float share)
        {
            if (_well < 0 || share <= 0f) return;

            float march = SiegeTuning.WellMarch(_well);
            float drift = SiegeTuning.WellLane(_well) - raider.Lane;

            if (share >= 1f)
            {
                raider.March = march;
                raider.Drift = drift;
                return;
            }

            raider.March += (march - raider.March) * share;
            raider.Drift += (drift - raider.Drift) * share;
        }

        /// <summary>
        /// The well shuts: every body it held stands where it stood and walks on slowed.
        ///
        /// <b>Placed as well as freed</b>, so a step long enough to swallow the whole well - a
        /// resumed app, a fixture - leaves the hill exactly where a frame-by-frame run would
        /// have.
        /// </summary>
        void Release()
        {
            _sink = 0f;

            if (_well >= 0)
            {
                float march = SiegeTuning.WellMarch(_well);
                int lane = SiegeTuning.WellLane(_well);

                for (int i = 0; i < _raiders.Count; i++)
                {
                    var raider = _raiders[i];
                    raider.Surface(march, lane - raider.Lane, SiegeTuning.GravitySlowFor);
                }
            }

            _well = -1;
            _sinkHold = 0f;
        }
    }
}
