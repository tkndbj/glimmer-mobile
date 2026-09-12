namespace GlimmerGrove.Modes
{
    /// <summary>
    /// Which lane of the hill a raider walks down, given the colour it wears.
    ///
    /// <para>
    /// <b>A raider's lane is its colour's, and that is what makes the hill readable at a
    /// glance.</b> Lanes used to be dealt — one draw per raider, uniform over
    /// <see cref="SiegeTuning.Lanes"/> — which is correct and says nothing: the question this mode
    /// is about is <em>which colour is coming</em>, and answering it meant parsing a dozen small
    /// moving bodies four hundred points away. That is two seconds of work under a clock that
    /// gives none, so the player's eye correctly refused and the mode played as "take the biggest
    /// match". Sorting the hill by colour puts the answer in the shape of the picture rather than
    /// in its detail.
    /// </para>
    /// <para>
    /// <b>Loosely, though, and the jitter is the half that is load-bearing.</b> Strictly sorted
    /// lanes make a splash, a chain and a lance worth nothing — every neighbour of a red raider
    /// would be red, so a multi-target turret could only ever reach the colour it was already
    /// strong against, and the whole dear half of the shelf is priced on reaching across
    /// (invariant 37ax). One lane of slop is what keeps the colours mostly separated and the edges
    /// mixed.
    /// </para>
    /// <para>
    /// <b>It costs the field's deal exactly nothing</b>, which is invariant 41's whole subject: the
    /// hill draws from the same stream the gems do, so changing <em>how</em> a lane is chosen
    /// without changing how many times the stream is drawn from leaves every shipped seed, every
    /// recorded opening-swap count and every dealt field bit-identical. One draw per raider, as
    /// before.
    /// </para>
    /// <para>
    /// Mirrored by <c>Tools/verify/siege.py</c> and by <c>Tools/render_siege.py</c>, because a
    /// picture that draws a different hill from the one being played is the one diagnostic this
    /// mode has lying about its subject (invariant 44d).
    /// </para>
    /// </summary>
    public static class SiegeLanes
    {
        /// <summary>
        /// How far a raider may stray from its colour's own lane, in lanes.
        ///
        /// <b>One.</b> Nought is a hill of four solid columns, where nothing a multi-target turret
        /// does can ever reach a second colour; two is a hill where colour says nothing, because
        /// on five lanes a stray of two reaches almost all of them.
        /// </summary>
        public const int Stray = 1;

        /// <summary>
        /// How many of <see cref="Spread"/> rolls keep a raider in its own lane.
        ///
        /// <para>
        /// <b>Three in five, because an even stray is not a column.</b> The first cut rolled the
        /// three lanes uniformly, so a colour stood in its own lane only a third of the time and
        /// the hill came back from play as <em>too spread apart</em> — which it was: with four
        /// colours each taking three of five lanes, every lane holds every colour and the sorting
        /// says nothing at a glance.
        /// </para>
        /// <para>
        /// <b>It is a bias rather than a rule, and that is deliberate.</b> Straight columns would
        /// make a splash, a chain and a lance worth nothing — every neighbour of a red raider
        /// would be red — and the dear half of the shelf is priced on reaching across (invariant
        /// 37ax). Three in five reads as a column with stragglers, which is what was asked for.
        /// </para>
        /// </summary>
        public const int Home = 3;

        /// <summary>How many ways the stray is rolled. See <see cref="Home"/>.</summary>
        public const int Spread = Home + Stray * 2;

        /// <summary>
        /// The lane a ward's own raiders walk down: its seat on the line, spread over the hill.
        ///
        /// <para>
        /// Spread rather than indexed, because a line may stand two, three or four wards
        /// (<see cref="SiegeLayout.MaxWards"/>) and the hill is always
        /// <see cref="SiegeTuning.Lanes"/> wide. Four wards on five lanes land on 0, 1, 3 and 4 —
        /// which leaves the middle lane belonging to nobody and therefore to everybody, and that
        /// is where <see cref="Stray"/> does its mixing.
        /// </para>
        /// <para>
        /// It is the same proportional arithmetic <c>SiegeBoard.Nearest</c> uses to decide which
        /// ward a raider at the line swings at, so a raider in its own lane hits its own colour's
        /// turret. That is not a coincidence worth hiding: the colour you are failing to feed is
        /// the colour that takes your turret down.
        /// </para>
        /// </summary>
        public static int HomeOf(int ward, int wards)
        {
            if (wards <= 1) return SiegeTuning.Lanes / 2;

            int at = ward < 0 ? 0 : ward >= wards ? wards - 1 : ward;

            // Rounded rather than truncated, so the seats sit symmetrically about the middle.
            return (at * (SiegeTuning.Lanes - 1) * 2 + (wards - 1)) / ((wards - 1) * 2);
        }

        /// <summary>
        /// The lane this raider walks down: its colour's, strayed by <paramref name="roll"/>.
        ///
        /// <para>
        /// <b>The roll is the board's own draw, handed in rather than taken</b>, so this stays a
        /// pure function three runtimes can agree on and the one place that owns the stream stays
        /// <c>SiegeBoard</c>.
        /// </para>
        /// </summary>
        public static int Walk(int ward, int wards, uint roll)
        {
            int home = HomeOf(ward, wards);

            // Three of the five rolls keep it at home and one each side strays it, so a colour
            // reads as a column with stragglers rather than as a band three lanes wide.
            int pick = (int)(roll % Spread);

            int lane = pick < Home ? home
                     : pick == Home ? home - Stray
                                    : home + Stray;

            return lane < 0 ? 0 : lane >= SiegeTuning.Lanes ? SiegeTuning.Lanes - 1 : lane;
        }
    }
}
