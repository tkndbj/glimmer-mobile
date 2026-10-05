namespace GlimmerGrove.Modes
{
    /// <summary>
    /// The fight: what a boss is while it stands, and the one door every point of harm on the
    /// hill goes through.
    ///
    /// <para>
    /// <b>Why a door.</b> Eight places took health off a raider - a bolt, a partner shot, a burn
    /// tick, an overcharge, a firepot, a storm, a bomb and a stormglass - and every one of them
    /// wrote <c>Health -= damage</c> itself. <see cref="Wound"/> is where health comes off, and
    /// what it answers is what really came off - which is also what a utility is charged
    /// against (invariant 39).
    /// </para>
    /// <para>
    /// <b>Three rules, and that is the whole of a boss fight.</b>
    /// <list type="number">
    /// <item>A boss walks on untouchable and is alone on the hill for as long as it lives
    /// (<c>SiegeBoard.Muster</c>).</item>
    /// <item>From the frame it plants, everything lands on it at full weight - every ward on the
    /// line (<see cref="SiegeTuning.EveryWardReaches"/>), every overcharge, every utility - and
    /// nothing holds any of it back.</item>
    /// <item>Its health is read in thirds, and each third it is taken through opens the next
    /// stand: a faster cadence and a fresh opening spell.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>The second rule replaced a floor, and the floor replaced a guard.</b> Both bought a
    /// fight its seconds by refusing the player's damage - the guard by stopping the line, the
    /// floor by resting the bar on a notch until the stand had thrown its spell and held three
    /// seconds - and both came back from play as a game that looked broken: turrets that
    /// stopped, a key that refused, eight banked charges that would not go. The owner's ruling
    /// (2026-10-05) is that a planted boss is attacked with everything, at once, and what
    /// makes a boss last is its health and nothing else.
    /// </para>
    /// </summary>
    public sealed partial class SiegeBoard
    {
        /// <summary>
        /// Runs every standing boss's fight on by <paramref name="dt"/>: the stand's clock, and
        /// the next stand opening when this one's third has been taken.
        ///
        /// <para>
        /// <b>After <c>Shoot</c> and before <c>Conjure</c>, and both halves of that are the
        /// contract.</b> After the line fires, so a stand finished by this frame's bolts turns
        /// on this frame rather than on the next one; before the casting, so the spell a boss
        /// throws is the spell of the stand it is actually in.
        /// </para>
        /// <para>
        /// <b>A phase turns here and nowhere else</b>, as a reading of health against a
        /// threshold - so a tap outside <c>Advance</c> (a firepot, a bomb, an overcharge) never
        /// opens one in the middle of somebody else's call stack. <b>A blow that takes a boss
        /// through two thirds opens the last stand directly</b>: the stands are where its health
        /// is, not a sequence it is owed.
        /// </para>
        /// </summary>
        void Fights(float dt)
        {
            for (int i = 0; i < _raiders.Count; i++)
            {
                var boss = _raiders[i];
                if (!boss.Boss || !boss.Alive || !boss.InPlace) continue;

                boss.InPhase += dt;
                boss.SinceOpener += dt;

                int phase = boss.Phase;
                while (phase < SiegeTuning.BossPhases - 1
                       && boss.Health <= SiegeTuning.PhaseFloor(boss.MaxHealth, phase)) phase++;

                if (phase != boss.Phase) OpenPhase(boss, phase);
            }
        }

        /// <summary>
        /// Takes <paramref name="damage"/> off <paramref name="raider"/>, as far as the rules
        /// allow, and answers how much came off.
        ///
        /// <para>
        /// <b>Nought for a boss that is walking on</b>, and the caller reports nought, which is
        /// the honest picture: nothing landed. It is the only window in the mode where a blow
        /// finds nothing to hurt, and the view draws the walk-in as an arrival so a player is
        /// never told a bolt vanished.
        /// </para>
        /// <para>
        /// <b>Never below nought</b>, which every caller used to clamp for itself, so a strike
        /// record can say what it took and a kill is one that took the last of it.
        /// </para>
        /// </summary>
        int Wound(SiegeRaider raider, int damage)
        {
            if (raider == null || !raider.Alive || damage <= 0) return 0;
            if (raider.Arriving || raider.Health <= 0) return 0;

            // **A hexed body takes more from everything, and this is the only place that says
            // so** (`SiegeLayout.Obsidian`). Integer and rounded down, for the "no float decides
            // a threshold" rule; a point is never lost to the rounding, because the percentage
            // is at least a hundred.
            if (raider.Hexed > 0f) damage = SiegeTuning.Hexing(damage);

            int took = damage < raider.Health ? damage : raider.Health;

            raider.Health -= took;
            raider.Flash = .18f;

            return took;
        }

        /// <summary>
        /// Opens stand <paramref name="phase"/> of a boss's fight: the clock restarts and the
        /// stand's opening spell is queued behind a short wake.
        ///
        /// <b>Called at two moments and nowhere else</b> - the frame a boss reaches its ground
        /// (stand nought) and the frame <see cref="Fights"/> finds a third taken. Both are the
        /// board's own doing, so a view never has to be told; it reads
        /// <c>SiegeRaider.Phase</c> as state.
        /// </summary>
        void OpenPhase(SiegeRaider raider, int phase)
        {
            if (raider == null || !raider.Boss) return;

            raider.Phase = phase >= SiegeTuning.BossPhases ? SiegeTuning.BossPhases - 1
                         : phase < 0 ? 0 : phase;
            raider.InPhase = 0f;
            raider.Opening = false;
            raider.Opened = false;

            // **On its wake, and never sooner than a stand apart from the last opener**
            // (`SiegeTuning.OpenerRest`): a stand taken in a second is not answered with the
            // next stand's spell a second later.
            float rest = SiegeTuning.OpenerRest - raider.SinceOpener;
            raider.Spell = rest > SiegeTuning.PhaseWake ? rest : SiegeTuning.PhaseWake;

            Attention.PhaseOpened(raider.Phase);
        }
    }
}
