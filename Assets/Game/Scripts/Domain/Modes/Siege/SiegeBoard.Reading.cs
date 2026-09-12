namespace GlimmerGrove.Modes
{
    /// <summary>
    /// What the hill is asking for, in the two forms a player can act on.
    ///
    /// <para>
    /// <b>Both of these exist because reading the hill was too expensive to be worth doing.</b>
    /// The colour lock gives the question stakes; it does not make it cheap to answer. "Which
    /// colour is coming" meant parsing a dozen small moving bodies four hundred points away, in
    /// four colours, mixed — two seconds of work under a clock that gives none, so the eye
    /// correctly refused and the mode played as "take the biggest match on the field". These are
    /// the two readings that turn a parse into a glance: what is on the hill <em>now</em>, and
    /// what is coming <em>next</em>.
    /// </para>
    /// <para>
    /// <b>Both live in Domain so the view decides nothing.</b> A screen that counted raiders
    /// itself would be a second opinion about the one question this mode is about, and the two
    /// would drift the first time either moved (invariant 5b).
    /// </para>
    /// </summary>
    public sealed partial class SiegeBoard
    {
        /// <summary>
        /// How much of what is standing on the hill this ward is the answer to, in raider health.
        ///
        /// <para>
        /// <b>Health rather than a head count</b>, because a brute is two matches and a creeper is
        /// one — a count would tell a player that four creepers matter more than two brutes, which
        /// is the opposite of true. What the view does with it is light the loudest of the four,
        /// so what has to be honest is the ordering.
        /// </para>
        /// <para>
        /// <b>Everything this ward would fire at, which folds a prism's partner in.</b> The
        /// question is "is this turret worth feeding", and for a prism the answer includes the
        /// colour it can reach — asking only about its own would under-report the one turret on
        /// the shelf bought for covering two.
        /// </para>
        /// </summary>
        public int DemandOf(int ward)
        {
            if (ward < 0 || ward >= _wards.Length) return 0;

            var post = _wards[ward];
            int wanted = 0;

            for (int i = 0; i < _raiders.Count; i++)
            {
                var raider = _raiders[i];
                if (!raider.Alive || !raider.OnTheHill) continue;
                if (post.ReachTenths(raider.Colour) <= 0) continue;

                wanted += raider.Health;
            }

            return wanted;
        }

        /// <summary>The largest <see cref="DemandOf"/> on the line, so a view can scale by it.</summary>
        public int Busiest
        {
            get
            {
                int most = 0;

                for (int w = 0; w < _wards.Length; w++)
                {
                    int wanted = DemandOf(w);
                    if (wanted > most) most = wanted;
                }

                return most;
            }
        }

        /// <summary>
        /// Seconds until the next wave musters.
        ///
        /// <b>Narrow on purpose, exactly as <see cref="BeforeFirstWave"/> is.</b> A hill that still
        /// holds something musters on this clock; a cleared one musters as soon as the breather is
        /// up, which may be sooner. <see cref="Resting"/> is the predicate a caller asks first.
        /// </summary>
        public float Rest => _rest > 0f ? _rest : 0f;

        /// <summary>
        /// Whether the hill is clear and something is still to come — the breather.
        ///
        /// <b>This is the moment the whole rhythm is built around.</b> It is when a ward banks
        /// rather than fires (see <c>SiegeBoard.Aim</c>), and so the only moment in a run where a
        /// player can act on what is coming rather than on what has already arrived.
        /// </summary>
        public bool Resting
        {
            get
            {
                if (_wave <= 0 || _wave >= Layout.WaveCount) return false;
                if (_rest <= 0f) return false;

                for (int i = 0; i < _raiders.Count; i++)
                    if (_raiders[i].Alive) return false;

                return true;
            }
        }

        /// <summary>
        /// What the next wave is bringing, by colour.
        ///
        /// <para>
        /// <b>The forecast is what makes a breather worth having.</b> A gap with no information in
        /// it is a pause; a gap that says <em>six blue, three red</em> is a decision — and it is
        /// the one place in this mode a player is ever told something before it happens rather
        /// than after.
        /// </para>
        /// </summary>
        public SiegeForecast Coming => SiegeForecast.Of(Layout, _wave);
    }
}
