namespace GlimmerGrove.Modes
{
    /// <summary>
    /// The fight: what a boss is while it stands, and the one door every point of harm on the
    /// hill goes through.
    ///
    /// <para>
    /// <b>Why a door.</b> Eight places took health off a raider — a bolt, a partner shot, a burn
    /// tick, an overcharge, a firepot, a storm, a bomb and a stormglass — and every one of them
    /// wrote <c>Health -= damage</c> itself. That was fine while the only rule about damage was
    /// that a bulwark soaks it; a boss that cannot be hurt on the walk in and cannot be taken
    /// past its stand's floor is two rules, and two rules in eight places is sixteen chances to
    /// disagree. So <see cref="Wound"/> is where health comes off, and what it answers is what
    /// really came off — which is also what a utility is charged against (invariant 39), so a
    /// firepot dropped on a boss resting on its floor costs the run nothing because it did
    /// nothing.
    /// </para>
    /// <para>
    /// <b>Three rules, and that is the whole of a boss fight.</b>
    /// <list type="number">
    /// <item>A boss walks on untouchable and is alone on the hill for as long as it lives
    /// (<c>SiegeBoard.Muster</c>).</item>
    /// <item>The frame it plants, every ward on the line may fire at it at full weight
    /// (<see cref="SiegeTuning.EveryWardReaches"/>) — there is no window in which a fed line
    /// stands doing nothing.</item>
    /// <item>It cannot be taken past its stand's floor until that stand has settled: its opening
    /// spell landed and <see cref="SiegeTuning.PhaseLeast"/> held. Then the floor slides down and
    /// the next stand opens.</item>
    /// </list>
    /// Everything a chapter adds — a verb, a cast, a surge — hangs off those three and needs no
    /// new clause here, which is what makes a seventh chapter free.
    /// </para>
    /// <para>
    /// See <see cref="SiegeTuning.BossPhases"/> for the shape of the fight, why it has one, and
    /// why the promise is paid out of the boss's health bar rather than out of the player's
    /// turrets.
    /// </para>
    /// </summary>
    public sealed partial class SiegeBoard
    {
        /// <summary>
        /// Runs every standing boss's fight on by <paramref name="dt"/>: the stand's clock, and
        /// the floor lifting when the stand is spent.
        ///
        /// <para>
        /// <b>After <c>Shoot</c> and before <c>Conjure</c>, and both halves of that are the
        /// contract.</b> After the line fires, so a stand that was finished by this frame's bolts
        /// turns on this frame rather than on the next one; before the casting, so the spell a
        /// boss throws is the spell of the stand it is actually in.
        /// </para>
        /// <para>
        /// <b>A phase turns here and nowhere else.</b> It used to turn inside <see cref="Wound"/>,
        /// which meant a tap outside <c>Advance</c> — a firepot, a bomb, an overcharge — opened a
        /// phase in the middle of somebody else's call stack. It cannot any more, because the
        /// floor holds the boss whether or not anything is reporting: the turn is a reading of
        /// health against a threshold, and the one place that reading is taken is here.
        /// </para>
        /// </summary>
        void Fights(float dt)
        {
            for (int i = 0; i < _raiders.Count; i++)
            {
                var boss = _raiders[i];
                if (!boss.Boss || !boss.Alive || !boss.InPlace) continue;

                boss.InPhase += dt;

                // The last stand has no floor to lift and nothing after it to open: what settling
                // buys there is the boss's own death, which `Floor` holds at one until then.
                if (boss.Phase >= SiegeTuning.BossPhases - 1) continue;

                if (boss.Health > boss.PhaseFloor || !boss.Settled) continue;

                OpenPhase(boss, boss.Phase + 1);
            }
        }

        /// <summary>
        /// Takes <paramref name="damage"/> off <paramref name="raider"/>, as far as the rules
        /// allow, and answers how much came off.
        ///
        /// <para>
        /// <b>Nought for a boss that is walking on</b>, and the caller reports nought, which is
        /// the honest picture: nothing landed. It is the only window in the mode where a bolt
        /// finds nothing to hurt, and the view draws the walk-in as an arrival so a player is
        /// never told a bolt vanished.
        /// </para>
        /// <para>
        /// <b>Clamped at the stand's floor</b> (<see cref="SiegeRaider.Floor"/>), so a boss cannot
        /// be taken through a stand it has not yet paid for: what would have crossed is dropped
        /// and the bar rests on the notch until the stand settles. The overkill is the price of
        /// dumping everything at once, and it is a price a player can see coming — the bar is
        /// drawn in thirds and the line is visibly walking it down.
        /// </para>
        /// <para>
        /// <b>Never below nought</b>, which every caller used to clamp for itself, so a strike
        /// record can say what it took and a kill is one that took the last of it.
        /// </para>
        /// </summary>
        int Wound(SiegeRaider raider, int damage)
        {
            if (raider == null || !raider.Alive || damage <= 0) return 0;

            int room = raider.Health - raider.Floor;
            if (raider.Arriving || room <= 0) return 0;

            int took = damage < room ? damage : room;

            raider.Health -= took;
            raider.Flash = .18f;

            return took;
        }

        /// <summary>
        /// Opens stand <paramref name="phase"/> of a boss's fight: the clock restarts and the
        /// stand's opening spell is queued behind a short wake.
        ///
        /// <b>Called at two moments and nowhere else</b> — the frame a boss reaches its ground
        /// (stand nought) and the frame <see cref="Fights"/> finds a spent stand. Both are the
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
            raider.Spell = SiegeTuning.PhaseWake;

            Attention.PhaseOpened(raider.Phase);
        }

        /// <summary>
        /// Settles a stand at once, for a boss that has nothing it could ever throw.
        ///
        /// <b>A floor in front of a spell that will never be cast is a wall</b> (invariant 5d) —
        /// a bonecaller that has spent its raises would otherwise rest on a notch until
        /// <see cref="SiegeTuning.PhaseMost"/>, having decided nothing. The clock is wound past
        /// <see cref="SiegeTuning.PhaseLeast"/> rather than a flag being set beside it, so
        /// <see cref="SiegeRaider.Settled"/> stays one reading of one number.
        /// </summary>
        static void Settle(SiegeRaider raider)
        {
            if (raider == null || !raider.Boss) return;

            raider.Opening = false;
            raider.Opened = true;

            if (raider.InPhase < SiegeTuning.PhaseLeast) raider.InPhase = SiegeTuning.PhaseLeast;
        }
    }
}
