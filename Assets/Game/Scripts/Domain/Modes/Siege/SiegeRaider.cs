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

        /// <summary>Seconds until its next spell. A warlord's only, and only once it is in place.</summary>
        public float Spell;

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
            // A boss stands and winds up; a weaver or a thief takes a shorter beat before
            // its first web. Both are the same field, because both are "how long until this
            // one does its thing" and two timers would be two places to forget one.
            Spell = SiegeTuning.HoldsTheField(kind)
                  ? SiegeTuning.MeddleWakes : SiegeTuning.BossWakes;
        }

        public bool Brute => Kind == SiegeKind.Brute;

        /// <summary>How fast it walks right now, as a fraction of its ordinary pace.</summary>
        public float Pace => Chill > 0f ? (10 - ChillTenths) / 10f : 1f;

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
