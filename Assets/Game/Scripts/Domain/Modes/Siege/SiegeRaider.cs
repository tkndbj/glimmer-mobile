using System;
using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>One raider on the hill.</summary>
    public sealed class SiegeRaider
    {
        public readonly int Id;

        /// <summary>Which of <see cref="SiegeLayout.Letters"/> it wears, as an index.</summary>
        public readonly int Colour;

        public readonly SiegeKind Kind;

        /// <summary>Which lane it walks down, 0 at the left.</summary>
        public readonly int Lane;

        /// <summary>Seconds before it steps out. Not on the hill until this reaches nought.</summary>
        public float Wait;

        /// <summary>How far down the hill it is: 0 at the top, 1 at the ward line.</summary>
        public float March;

        /// <summary>
        /// How far down it comes before it stops. One for everything but a boss.
        ///
        /// <b>Readonly again.</b> It was briefly written by a warbringer's roar, which lunged it
        /// further down the hill each time; that was withdrawn after play (a boss that walks spends
        /// the fight being somewhere else), and with it went the only thing that ever moved a
        /// raider's ground after it was minted.
        /// </summary>
        public readonly float Hold;

        public int Health;
        public readonly int MaxHealth;

        public bool Alive = true;

        /// <summary>Seconds until its next blow, once it has arrived.</summary>
        public float Blow;

        /// <summary>Seconds until its next spell. A boss's only, and only once it is in place.</summary>
        public float Spell;

        /// <summary>
        /// How many times this boss has raised, which is the one boss counter the rules cap.
        ///
        /// <b>On the raider rather than on the board, because the cap is a fact about the
        /// caster.</b> A lane could stand two bonecallers (<c>SiegeEndless</c> pairs bosses), and a
        /// board-wide count would give the pair between them the allowance one was priced at —
        /// which is the half of invariant 5d par could not see: the hill would hold bodies the
        /// level's own par never counted.
        /// </summary>
        public int Raised;

        /// <summary>Set for one frame after it has been hit, so the view can flash it.</summary>
        public float Flash;

        /// <summary>
        /// Seconds left of a frost, and how much slower it walks while it lasts, in tenths.
        ///
        /// <para>
        /// <b>One chill rather than one per ward</b>, and a fresh one <em>replaces</em> rather
        /// than stacks. Two rime turrets on one line would otherwise multiply into a raider that
        /// never arrives, which is a fail state that rejects nothing (invariant 5d) — and the
        /// strongest chill wins rather than the newest, so a player is never punished for a weak
        /// turret firing a moment after a strong one.
        /// </para>
        /// </summary>
        public float Chill;

        public int ChillTenths;

        /// <summary>
        /// Seconds left of a stun: this one is standing still, swinging at nothing and casting
        /// nothing.
        ///
        /// <b>Its own counter rather than a chill of ten tenths</b>, and the reason is what it
        /// stops. A chill is a rate on the march and nothing else — a slowed raider still swings
        /// and a slowed boss still casts — where a stun takes the raider out of the raid, which is
        /// three rules in three different files (<c>SiegeBoard.Walk</c>, <c>Swing</c> and
        /// <c>Conjure</c>) rather than a number the march multiplies by.
        /// </summary>
        public float Stun;

        /// <summary>
        /// Seconds until another stun can take hold. See <see cref="Stagger"/>.
        ///
        /// <b>One field for both halves</b>: it is set past the end of the stun that armed it, so
        /// "still held" and "not yet stunnable again" are one countdown rather than two that could
        /// disagree.
        /// </summary>
        public float Steady;

        /// <summary>
        /// Seconds left of a burn, what it takes per second, and which ward lit it.
        ///
        /// <b>The ward travels with it</b> because the view draws the tick in that ward's colour,
        /// and a burn whose source had to be guessed from the damage would be drawn in whichever
        /// colour happened to be firing.
        /// </summary>
        public float Burn;

        public int BurnRate;

        public int BurnFrom = -1;

        /// <summary>Left over from the last burn tick, so a fractional rate is not lost.</summary>
        public float Smoulder;

        /// <summary>
        /// How much tougher this one is than the level authored it.
        ///
        /// <b>Carried rather than looked up</b>, because it is decided when the raider is minted
        /// and an endless lane's wave forty is not the same wave as its wave four. Nothing but
        /// health and the blow reads it: the march, the hold and the cast rate are what a kind
        /// *is*, and scaling those would make a boss on wave forty a different fight rather than
        /// a tougher one.
        /// </summary>
        public readonly SiegeSurge Surge;

        public SiegeRaider(int id, int colour, SiegeKind kind, int lane, float wait,
                           SiegeSurge surge = default)
        {
            Id = id;
            Colour = colour;
            Kind = kind;
            Lane = lane;
            Wait = wait;
            Hold = SiegeTuning.HoldOf(kind);
            Surge = surge.HealthTenths <= 0 ? SiegeSurge.None : surge;
            MaxHealth = Surge.Health(SiegeTuning.HealthOf(kind));
            Health = MaxHealth;
            Blow = SiegeTuning.BlowEvery * .5f;
            // A boss stands and winds up before its first spell. Nothing else casts, so nothing
            // else reads this.
            Spell = SiegeTuning.BossWakes;
        }

        public bool Brute => Kind == SiegeKind.Brute;

        /// <summary>Whether it is held where it stands. See <see cref="Stun"/>.</summary>
        public bool Stunned => Alive && Stun > 0f;

        /// <summary>
        /// How fast it walks right now, as a fraction of its ordinary pace.
        ///
        /// <b>A stun is nought rather than a tenth of a chill</b>, which is what keeps the two
        /// from ever having to be combined: the strongest chill this mode can author is nine
        /// tenths, deliberately, because a chill is refreshable and a tenth of a pace that never
        /// arrives is a raid that rejects nothing.
        /// </summary>
        public float Pace => Stun > 0f ? 0f
                           : Chill > 0f ? (10 - ChillTenths) / 10f : 1f;

        /// <summary>
        /// Puts a frost on it, keeping the stronger of what it already had.
        ///
        /// See <see cref="Chill"/> for why the stronger wins rather than the newest.
        /// </summary>
        public void Freeze(int tenths, float seconds)
        {
            if (tenths <= 0 || seconds <= 0f) return;
            if (tenths < ChillTenths && Chill > 0f) { Chill = Math.Max(Chill, seconds); return; }

            ChillTenths = tenths > 9 ? 9 : tenths;
            Chill = Math.Max(Chill, seconds);
        }

        /// <summary>
        /// Stops it where it stands for <paramref name="seconds"/>, if it is not still recovering
        /// from the last one.
        ///
        /// <para>
        /// <b>Refused rather than refreshed, which is the opposite of every other lasting state
        /// here and is the whole of what makes a stun safe.</b> A chill and a burn keep the
        /// stronger of what is offered because a raider under either is still walking at the line;
        /// a stun that took the longer of two would be renewed by a ward firing every
        /// <c>SiegeTuning.FireEvery</c> seconds and would never end.
        /// </para>
        /// <para>
        /// <b>The rest is measured from the moment it lands</b> (<c>SiegeTuning.StunRest</c>), so
        /// what a rung buys is the share of the clock it takes: half a second in every second and
        /// a half, or a whole one in every two.
        /// </para>
        /// </summary>
        public void Stagger(float seconds)
        {
            if (!Alive || seconds <= 0f || Steady > 0f) return;

            Stun = seconds;
            Steady = seconds + SiegeTuning.StunRest;
        }

        /// <summary>Sets it burning, keeping the fiercer of what it already had.</summary>
        public void Kindle(int rate, float seconds, int ward)
        {
            if (rate <= 0 || seconds <= 0f) return;

            if (rate >= BurnRate) { BurnRate = rate; BurnFrom = ward; }
            Burn = Math.Max(Burn, seconds);
        }

        /// <summary>Whether this is a boss of any of the four — the thing that stands and casts.</summary>
        public bool Boss => SiegeTuning.IsBoss(Kind);

        /// <summary>Whether this is the greatest of the four.</summary>
        public bool Overlord => Kind == SiegeKind.Overlord;

        /// <summary>What this one's spell does. <see cref="SiegeSpell.Smite"/> for anything that has none.</summary>
        public SiegeSpell Spellcraft => SiegeTuning.SpellOf(Kind);

        public bool OnTheHill => Wait <= 0f;

        /// <summary>
        /// Whether it is standing at the line and swinging.
        ///
        /// <b>A warlord never is</b>, because <see cref="Hold"/> stops it short of it — which is
        /// what makes <see cref="SiegeBoard.Swing"/> need no clause about bosses at all.
        /// </summary>
        public bool AtTheLine => Alive && OnTheHill && Hold >= 1f && March >= 1f;

        /// <summary>Whether it has reached the ground it holds and may start casting.</summary>
        public bool InPlace => Alive && OnTheHill && March >= Hold;
    }
}
