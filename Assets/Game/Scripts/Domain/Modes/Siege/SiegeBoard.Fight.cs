using System;

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
    /// that a bulwark soaks it; a boss that cannot be hurt on the walk in, cannot be hurt behind
    /// its guard and cannot be taken past its phase's floor in one blow is three rules, and
    /// three rules in eight places is twenty-four chances to disagree. So <see cref="Wound"/> is
    /// where health comes off, and what it answers is what really came off — which is also what
    /// a utility is charged against (invariant 39), so a firepot dropped on a guarded boss costs
    /// the run nothing because it did nothing.
    /// </para>
    /// <para>
    /// See <see cref="SiegeTuning.BossPhases"/> for the shape of the fight and why it has one.
    /// </para>
    /// </summary>
    public sealed partial class SiegeBoard
    {
        /// <summary>
        /// Takes <paramref name="damage"/> off <paramref name="raider"/>, as far as the rules
        /// allow, and answers how much came off.
        ///
        /// <para>
        /// <b>Nought for a boss that is untouchable</b> — walking on, or behind its guard — and
        /// the caller reports nought, which is the honest picture: nothing landed. The view draws
        /// the guard so a player is never told a bolt vanished.
        /// </para>
        /// <para>
        /// <b>Clamped at the phase's floor</b>, so a boss cannot be taken through two phases in one
        /// blow: what would have crossed the threshold is dropped and the next phase opens, guard
        /// up. The overkill is the price of dumping everything at once, and it is a price a
        /// player can see coming — the bar is drawn in phases.
        /// </para>
        /// <para>
        /// <b>Never below nought</b>, which every caller used to clamp for itself, so a strike
        /// record can say what it took and a kill is one that took the last of it.
        /// </para>
        /// </summary>
        int Wound(SiegeRaider raider, int damage)
        {
            if (raider == null || !raider.Alive || damage <= 0) return 0;
            if (raider.Untouchable) return 0;

            int floor = raider.PhaseFloor;
            int room = raider.Health - floor;
            if (room <= 0) return 0;

            int took = damage < room ? damage : room;

            raider.Health -= took;
            raider.Flash = .18f;

            // Opened the moment the floor is reached rather than on the next step, so the guard
            // is up before the next bolt in this same frame asks — a frame of every ward on the
            // line landing through a threshold is the exact arrangement the guard exists to
            // refuse.
            if (raider.Boss && floor > 0 && raider.Health <= floor)
                OpenPhase(raider, raider.Phase + 1);

            return took;
        }

        /// <summary>
        /// Opens phase <paramref name="phase"/> of a boss's fight: the guard goes up and the
        /// phase-opening spell is queued behind a short wake.
        ///
        /// <b>Called at two moments and nowhere else</b> — the frame a boss reaches its ground
        /// (phase nought) and the frame a blow reaches a threshold (every phase after). Both are
        /// the board's own doing, so a view never has to be told; it reads
        /// <c>SiegeRaider.Phase</c> and <c>Guard</c> as state.
        /// </summary>
        void OpenPhase(SiegeRaider raider, int phase)
        {
            if (raider == null || !raider.Boss) return;

            raider.Phase = phase >= SiegeTuning.BossPhases ? SiegeTuning.BossPhases - 1
                         : phase < 0 ? 0 : phase;
            raider.Guard = SiegeTuning.GuardMost;
            raider.Opening = false;
            raider.Opened = false;
            raider.Spell = SiegeTuning.PhaseWake;

            Attention.PhaseOpened(raider.Phase);
        }

        /// <summary>
        /// Runs a boss's guard down by <paramref name="dt"/> and drops it when it is due: the
        /// opening spell has landed and <see cref="SiegeTuning.GuardLeast"/> has passed, or the
        /// deadline has, whichever is first.
        ///
        /// <b>On the board's clock whatever the boss is doing</b> - stunned, or retrying a cast
        /// that finds nothing to aim at - because a guard is a deadline and a stun is seconds off
        /// the fight, never a wall.
        /// </summary>
        static void Guarding(SiegeRaider raider, float dt)
        {
            if (raider.Guard <= 0f) return;

            raider.Guard = Math.Max(0f, raider.Guard - dt);

            float stood = SiegeTuning.GuardMost - raider.Guard;
            if (raider.Guard <= 0f || (raider.Opened && stood >= SiegeTuning.GuardLeast))
                Unguard(raider);
        }

        /// <summary>
        /// Drops a boss's guard. It is due (<see cref="Guarding"/>), or the boss has nothing left
        /// it could ever throw.
        /// </summary>
        static void Unguard(SiegeRaider raider)
        {
            raider.Guard = 0f;
            raider.Opening = false;
            raider.Opened = true;
        }
    }
}
