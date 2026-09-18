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
        /// Which phase of its fight this boss is in, from nought. See
        /// <see cref="SiegeTuning.BossPhases"/> for what a phase is and why there are any.
        ///
        /// <b>State rather than an event, deliberately.</b> A phase can turn on a tap — a firepot,
        /// a bomb, an overcharge — outside <c>Advance</c>, where nothing is reporting, so a view
        /// that wanted to be told would miss half of them. A view compares this with what it last
        /// drew (a repaint is a drawing of a state, invariant 48l).
        /// </summary>
        public int Phase;

        /// <summary>
        /// Seconds this stand has held, from the frame its phase opened.
        ///
        /// <b>Counting up rather than down, because it is read against two deadlines.</b> A stand
        /// settles at <see cref="SiegeTuning.PhaseLeast"/> once its opening spell has landed, and
        /// at <see cref="SiegeTuning.PhaseMost"/> whatever happened — see <see cref="Settled"/>.
        /// A single number that both are compared against is one clock rather than two that can
        /// come apart.
        /// </summary>
        public float InPhase;

        /// <summary>Whether this phase's opening spell has been decided and is on its way.</summary>
        public bool Opening;

        /// <summary>Whether this phase's opening spell has landed. <see cref="Settled"/> waits on it.</summary>
        public bool Opened;

        /// <summary>Seconds this boss has stood on its ground. Measured for the fight gate.</summary>
        public float Stood;

        /// <summary>Spells this boss has thrown. Measured for the fight gate.</summary>
        public int Casts;

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
            // A boss's first spell is decided when its first phase opens (`SiegeBoard.OpenPhase`),
            // which resets this; the value here is only what a boss stood on its ground by hand
            // would read. Nothing else casts, so nothing else reads it.
            Spell = SiegeTuning.PhaseWake;
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

        /// <summary>
        /// Ground this body still owes back to <see cref="SiegeCharm.Anvil"/>, as a share of the
        /// hill, worked off in <c>SiegeBoard.Walk</c> at <c>SiegeTuning.AnvilPace</c>.
        ///
        /// <b>A debt rather than a position, and that is the whole of why it is a field.</b> The
        /// view draws a raider wherever the model says it is, once a frame, so a knock-back
        /// written straight into <see cref="March"/> would move a hill of bodies between two
        /// frames - which reads as a glitch rather than as a blow. Carried here, the shove is
        /// something the model does over a quarter of a second and the drawing follows it for
        /// free, exactly as every other thing on this hill is followed.
        /// </summary>
        public float Heave;

        /// <summary>Whether this body is being driven back up the slope right now.</summary>
        public bool Shoved => Alive && Heave > 0f;

        /// <summary>
        /// Drives this body back up the slope by <paramref name="share"/> of the hill, or as far
        /// as the crest, whichever is less.
        ///
        /// <para>
        /// <b>A boss does not move, and it is refused here rather than at the call site</b> so
        /// that every future way of shoving the hill inherits the rule. A boss's whole fight is
        /// measured from where it stands - <see cref="InPlace"/> opens its first phase, a guard
        /// and a floor run from that moment (invariant 37di) - so a boss driven off its ground
        /// would be a boss whose fight restarts, which is not what anybody matching a gem asked
        /// for. It is also what makes the charm's own decision real (invariant 26h): a boss wave
        /// is a wave of its own (invariant 37dn), so an anvil spent on one buys nothing.
        /// </para>
        /// <para>
        /// <b>Something still in the wings is not on the hill</b>, so it is refused too: a wave
        /// mustered but not yet walked on has no ground to lose, and pushing its <c>Wait</c> back
        /// would be the charm quietly rewriting the level's own schedule.
        /// </para>
        /// </summary>
        public bool Shove(float share)
        {
            if (!Alive || Boss || !OnTheHill || share <= 0f) return false;

            float room = March - Heave;
            if (room <= 0f) return false;

            Heave += room < share ? room : share;
            return true;
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

        /// <summary>
        /// Whether this boss is still walking on, where nothing at all can hurt it.
        ///
        /// <b>The walk-in is the one window with no floor under it</b>, because there is no fight
        /// yet to have a floor: the boss has not reached its ground, has opened no stand and has
        /// thrown nothing. It was never asked before, and that is the whole of why a boss died on
        /// the walk in.
        /// </summary>
        public bool Arriving => Boss && Alive && !InPlace;

        /// <summary>
        /// Whether this stand has done what it promised: its opening spell has landed and it has
        /// held <see cref="SiegeTuning.PhaseLeast"/> — or <see cref="SiegeTuning.PhaseMost"/> has
        /// passed, which is the deadline for a boss that can find nothing to throw at.
        ///
        /// <b>Never true of anything but a boss in place</b>, so every caller can ask it without
        /// first asking what it is talking to.
        /// </summary>
        public bool Settled
            => Boss && Alive && InPlace
            && ((Opened && InPhase >= SiegeTuning.PhaseLeast) || InPhase >= SiegeTuning.PhaseMost);

        /// <summary>
        /// The health this boss may not be taken under <em>right now</em>.
        ///
        /// <para>
        /// <b>The stand's own threshold</b> (<see cref="SiegeTuning.PhaseFloor"/>) — and, in the
        /// last stand, where that threshold is nought, <b>one</b> until the stand has settled. The
        /// last third would otherwise be the one stand a banked line could end the instant it
        /// opened, which is the arrangement <see cref="SiegeTuning.BossPhases"/> exists to reject
        /// and the one that cost three chapters a finale. A boss at one health with an empty bar,
        /// finishing the spell it had already begun, is the honest drawing of it.
        /// </para>
        /// <para>
        /// <b>Asked as a property rather than written into a field</b>, because a floor is a
        /// reading of the boss's own state — the phase it is in and whether that stand has settled
        /// — and a copy of it would be a second opinion that could be a frame stale (invariant
        /// 16x's shape, said about a fight).
        /// </para>
        /// </summary>
        public int Floor
        {
            get
            {
                if (!Boss || !Alive) return 0;

                int keep = SiegeTuning.PhaseFloor(MaxHealth, Phase);
                return keep > 0 || Settled ? keep : 1;
            }
        }

        /// <summary>
        /// Whether nothing on the line or in the player's hand can take anything off it right
        /// now: a boss still walking on, or one already resting on its stand's floor.
        ///
        /// <para>
        /// <b>The one question every source of harm asks</b>, through <c>SiegeBoard.Wound</c>,
        /// and the one question every aim asks, so a ward with nothing else to shoot at
        /// <em>banks</em> rather than pours fuel into a thing it cannot hurt — which is the
        /// ironclad's answer (bank, then dump) made general.
        /// </para>
        /// <para>
        /// <b>It is the *narrow* window now.</b> It used to include every second of a guard,
        /// which was three to four out of every stand; what is left is the walk-in and whatever
        /// seconds a line fast enough to reach the floor early has bought itself. A line that
        /// cannot chew a third of a boss in <see cref="SiegeTuning.PhaseLeast"/> seconds never
        /// meets it at all.
        /// </para>
        /// </summary>
        public bool Impervious => Boss && Alive && (Arriving || Health <= Floor);

        /// <summary>
        /// The health this boss cannot be taken under in its current phase — the next phase's
        /// threshold, or nought in the last. See <see cref="SiegeTuning.PhaseFloor"/>.
        ///
        /// <b>The phase's arithmetic alone</b>, where <see cref="Floor"/> is what the rules will
        /// actually allow this frame. <c>SiegeBoard.Fights</c> reads this to decide when a stand
        /// is spent; everything that takes health reads <see cref="Floor"/>.
        /// </summary>
        public int PhaseFloor => Boss ? SiegeTuning.PhaseFloor(MaxHealth, Phase) : 0;
    }
}
