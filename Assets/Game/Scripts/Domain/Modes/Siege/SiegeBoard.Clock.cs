using System;
using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// The hill: mustering a wave, walking it down, the line shooting back, the bosses casting
    /// and whatever reached the line swinging.
    ///
    /// <para>
    /// <b>The order in <see cref="Advance"/> is the contract</b> — a ward fed on a frame fires on
    /// it, a raider killed by that bolt never lands the blow it was about to, and a boss killed
    /// mid-wind-up never finishes its spell. Every one of those is a decision rather than a
    /// consequence of where a call happened to sit.
    /// </para>
    /// </summary>
    public sealed partial class SiegeBoard
    {
        // ------------------------------------------------------------------ the clock
        /// <summary>
        /// Steps the siege by <paramref name="dt"/> seconds and says what happened.
        ///
        /// <para>
        /// The order matters and is the order a player would want: the wave arrives, then the
        /// raiders walk, then the wards shoot at where the raiders now are, then the warlord casts,
        /// then whatever reached the line swings. A ward that has just been fuelled therefore gets
        /// its bolt away in the same step, and a raider killed by that bolt never lands the blow it
        /// was about to — nor does a warlord killed by it ever start the spell it was about to.
        /// </para>
        /// </summary>
        public SiegeReport Advance(float dt)
        {
            _report.Clear();

            if (dt <= 0f) return _report;
            if (dt > .25f) dt = .25f;      // a resumed app must not teleport a wave into the line

            // After the clamp, so the run's own clock is the one the board actually played on
            // rather than the wall clock a backgrounded app came back holding.
            Attention.Tick(dt);

            Land(dt);
            Muster(dt);
            Walk(dt);
            Shoot(dt);
            Conjure(dt);
            Smoulder(dt);
            Swing(dt);
            Age(dt);

            for (int i = _raiders.Count - 1; i >= 0; i--)
                if (!_raiders[i].Alive) _raiders.RemoveAt(i);

            return _report;
        }

        /// <summary>
        /// Lands whatever fuel has finished crossing the field.
        ///
        /// <b>Before anything else in the step</b>, so a ward fed on this frame may fire on it -
        /// the delay is the flight, not a further beat of hesitation once it has arrived.
        /// </summary>
        void Land(float dt)
        {
            for (int i = _flying.Count - 1; i >= 0; i--)
            {
                var charge = _flying[i];
                charge.In -= dt;

                if (charge.In > 0f)
                {
                    _flying[i] = charge;
                    continue;
                }

                _flying.RemoveAt(i);

                // A fallen ward takes nothing. The gems still went, which is the whole cost of
                // losing one: the colour is still on the field and is worth nothing now.
                var ward = _wards[charge.Ward];
                if (!ward.Alive) continue;

// **Through `Fill`, so a match that tops the tube up banks a charge.** The
                // edge is what is reported rather than the state: a tube that has *just* banked one
                // is the frame the overcharge can be announced on, where asking "is it armed" every
                // frame would announce it for as long as nobody spent it.
                if (ward.Fill(charge.Fuel)) _report.Brimmed.Add(charge.Ward);
            }

            // **After the fuel and never before it.** A stormglass and the motes of the match that
            // sprang it are booked to land on the same beat, and a ward that is about to be fed
            // should have its fuel before the hill is thinned — otherwise a run that kills the
            // last raider of a wave leaves the fuel arriving at a line with nothing to shoot at,
            // which is a different run from the one the player played.
            Break(dt);

            Arrive(dt);
        }

        /// <summary>
        /// Lands whatever spell has finished crossing the hill.
        ///
        /// <para>
        /// <b>A spell whose caster has been destroyed fizzles</b>, and that is a decision rather
        /// than tidiness. The alternative is a ward coming down — and a run being lost — to
        /// something thrown by a warlord the player had already beaten, which reads as the game
        /// getting the last word. It also makes killing a warlord mid-wind-up worth something,
        /// which the tell is long enough to make possible.
        /// </para>
        /// <para>
        /// A spell aimed at a ward that has since fallen is dropped for <see cref="Land"/>'s own
        /// reason: the ward that is asked is the one standing when it gets there.
        /// </para>
        /// </summary>
        void Arrive(float dt)
        {
            for (int i = _spells.Count - 1; i >= 0; i--)
            {
                var spell = _spells[i];
                spell.In -= dt;

                if (spell.In > 0f) { _spells[i] = spell; continue; }

                _spells.RemoveAt(i);

                var caster = Find(spell.Raider);
                if (caster == null || !caster.Alive) continue;

                // **The spell that opened a phase has landed**, which is half of what drops the
                // guard (`Guarding` has the other half) - noted before anything below can
                // `continue` past it, because a spell aimed at a ward that has since fallen is
                // dropped and the guard must not stand on a spell that was thrown. What the guard
                // promised was one spell thrown, not one spell landed.
                if (spell.Opens) caster.Opened = true;

                // **A roar is aimed at no ward and lands on every one of them.** It is settled
                // here rather than falling through to the single-target path below, because that
                // path indexes `spell.Ward` and a roar carries -1. It *restarts* rather than
                // stacks: two warbringers on one hill would otherwise multiply the charge into
                // something no level was tuned against.
                if (spell.Craft == SiegeSpell.Rally)
                {
                    _roar = SiegeTuning.RallyFor;

                    int shook = SiegeTuning.CastOf(caster.Kind);

                    for (int w = 0; w < _wards.Length; w++)
                    {
                        var shaken = _wards[w];
                        if (!shaken.Alive) continue;

                        shaken.Health -= shook;

                        bool down = shaken.Health <= 0;
                        if (down)
                        {
                            shaken.Health = 0;
                            shaken.Alive = false;
                            shaken.Fuel = 0f;
                        }

                        // One record per ward, so the view draws a hit on each of them from the
                        // same list every other spell arrives on. The roar itself is drawn off the
                        // *cast* (`SiegeView.Roar`), which is where its ring already comes from.
                        _report.Spells.Add(
                            new SiegeSpellLanded(spell.Raider, w, SiegeSpell.Rally, shook, down));
                    }

                    continue;
                }

                // **Two spells that are aimed at the hill rather than at the line**, settled
                // here for the reason the roar above is: the path below indexes `spell.Ward`, and
                // both of these carry -1.
                if (spell.Craft == SiegeSpell.Devour) { Devour(caster); continue; }
                if (spell.Craft == SiegeSpell.Raise) { Raise(caster); continue; }

                var ward = _wards[spell.Ward];
                if (!ward.Alive) continue;

                // Douse takes no health at all, which is why `CastOf` answers nought for it rather
                // than the rules carrying a second damage table nobody would keep in step.
                if (spell.Craft == SiegeSpell.Douse)
                {
                    ward.Snuff();
                    _report.Spells.Add(
                        new SiegeSpellLanded(spell.Raider, spell.Ward, SiegeSpell.Douse, 0, false));
                    continue;
                }

                // **A bind settles here beside the douse and takes nothing**, which is the whole
                // of the difference between the two: `Snuff` empties the tube and `Shackle` does
                // not touch it. Reported with a nought exactly as a douse is, because what a view
                // has to draw is a state rather than a number.
                if (spell.Craft == SiegeSpell.Bind)
                {
                    ward.Shackle();
                    _report.Spells.Add(
                        new SiegeSpellLanded(spell.Raider, spell.Ward, SiegeSpell.Bind, 0, false));
                    continue;
                }

                // A rank is taken *before* the health, so a spell that fells a ward has still
                // taken the rank it came for — and the view is told both in one record rather than
                // having to work out which order they happened in.
                bool sundered = spell.Craft == SiegeSpell.Sunder && spell.Opens && ward.Sunder();

                int cast = SiegeTuning.CastOf(caster.Kind);
                ward.Health -= cast;

                bool felled = ward.Health <= 0;
                if (felled)
                {
                    ward.Health = 0;
                    ward.Alive = false;
                    ward.Fuel = 0f;
                }

                _report.Spells.Add(new SiegeSpellLanded(spell.Raider, spell.Ward, spell.Craft,
                                                        cast, felled, sundered));
            }
        }

        /// <summary>
        /// Takes every loose thing off the hill: the cogs nobody has picked up and the bombs
        /// nobody has tapped.
        ///
        /// <para>
        /// <b>What it takes is what the hill owes the player</b>, which is the one thing on this
        /// board that is neither the line nor the field. A cog is a rank somebody earned and a
        /// bomb is a firepot they were given (invariant 40i), and both lie there until a finger
        /// reaches for them — so a gravemaw is a clock on the decision invariant 40i says is the
        /// whole of that raider: <em>when</em>.
        /// </para>
        /// <para>
        /// <b>It never touches a ward</b>, so a rung whose only threat were one could not be lost
        /// — which is why one rides the last authored wave rather than walking on alone, and why
        /// <c>SiegeValidator</c> refuses a rung that sends one with nothing to eat.
        /// </para>
        /// <para>
        /// <b>The ids go into the report</b> rather than being left to the view's poll to notice.
        /// Both lists are polled against the board every frame, so the widgets would come down
        /// either way — what the report buys is that they come down <em>toward the thing that ate
        /// them</em>, which is the difference between a mechanic and a player's cogs quietly
        /// disappearing.
        /// </para>
        /// </summary>
        void Devour(SiegeRaider caster)
        {
            for (int i = _cogs.Count - 1; i >= 0; i--)
            {
                _report.Devoured.Add(_cogs[i].Id);
                _cogs.RemoveAt(i);
            }

            for (int i = _bombs.Count - 1; i >= 0; i--)
            {
                _report.Devoured.Add(_bombs[i].Id);
                _bombs.RemoveAt(i);
            }

            _report.Spells.Add(new SiegeSpellLanded(caster.Id, -1, SiegeSpell.Devour, 0, false));
        }

        /// <summary>
        /// Puts a fresh group of creepers at the top of the hill, in the caster's own colour.
        ///
        /// <para>
        /// <b>Capped, and the cap is what lets this mode keep a par at all</b> — see
        /// <see cref="SiegeTuning.RaiseSize"/>. The counter is on the caster so two bonecallers on
        /// one hill each get their own allowance, which is what the level's par priced.
        /// </para>
        /// <para>
        /// <b>They muster exactly as a wave does</b>: lanes off the field's stream through
        /// <see cref="SiegeLanes.Walk"/>, spaced by <c>RaiderSpacing</c>, at the top of the hill.
        /// Anything else would be a group of raiders that behaved unlike every other group of
        /// raiders in the mode, and the drawn difference would read as a bug rather than as a
        /// spell. Invariant 41 is why the draw is taken here and not skipped: the number of times
        /// the field's stream is drawn from is part of what a level deals.
        /// </para>
        /// </summary>
        void Raise(SiegeRaider caster)
        {
            caster.Raised++;

            var surge = Layout.SurgeOf(_wave > 0 ? _wave - 1 : 0);

            for (int i = 0; i < SiegeTuning.RaiseSize; i++)
            {
                uint roll = Next();

                int lane = SiegeLanes.Walk(Layout.WardOf(SiegeLayout.Letters[caster.Colour]),
                                           Layout.Wards.Length, roll);

                _raiders.Add(new SiegeRaider(_minted++, caster.Colour, SiegeKind.Creeper, lane,
                                             i * SiegeTuning.RaiderSpacing, surge));
            }

            _report.Spells.Add(new SiegeSpellLanded(caster.Id, -1, SiegeSpell.Raise,
                                                    SiegeTuning.RaiseSize, false));
        }

        void Muster(float dt)
        {
            if (_wave >= Layout.WaveCount) return;

            // **On a clock, or the moment the hill is empty — whichever comes first.**
            //
            // The clock alone was the fix for waiting-on-a-clear, which let a winning player
            // stroll; a clear alone is what it replaced. Both together are what the mode actually
            // wants: the clock is the pressure and never lets up, and the shortcut means a player
            // who is *ahead* of it is rewarded with the next wave rather than made to stand and
            // watch an empty field. Note the guard — the shortcut cannot fire before the first
            // wave, because the hill is legitimately empty at the start of every run.
            _rest -= dt;

            if (_rest > 0f)
            {
                if (_wave == 0) return;

                for (int i = 0; i < _raiders.Count; i++)
                    if (_raiders[i].Alive) return;

                // **A cleared hill buys a breather rather than the next wave.** It used to muster
                // at once, which rewarded playing well with more pressure and left the run with
                // no moment in which anything could be planned - see `SiegeTuning.Breather`. This
                // only ever *shortens* a quiet, so the clock still never lets up and being ahead
                // is still worth something: the finding above kept, with the half that made the
                // mode unthinkable taken out.
                if (_rest > SiegeTuning.Breather) _rest = SiegeTuning.Breather;
                return;
            }

            int size = Layout.SizeOf(_wave);
            var surge = Layout.SurgeOf(_wave);

            // **How many bosses, not how big the wave is.** They were the same number for as long
            // as a boss wave held nothing else; a boss that rides the last authored wave has an
            // ordinary wave behind it, and `BossLane` reads this to decide whether it stands in
            // the middle of the hill or beside it.
            int bosses = Layout.BossesIn(_wave);


            for (int i = 0; i < size; i++)
            {
                var kind = Layout.KindAt(_wave, i);
                int colour = Layout.ColourAt(_wave, i);

                // **Asked of the raider rather than of the wave**, which is what an endless lane
                // needed: a pair wave sends two bosses and nothing else, so "is this the boss
                // wave" stopped being a fact that decides where one raider stands.
                bool boss = SiegeTuning.IsBoss(kind);

                // Lanes are dealt from the same stream the field is, so a wave arrives spread out
                // rather than in a column - and spread the same way on every device.
                //
                // **The warlord is the exception and walks down the middle**, because where it
                // stands is not a fact anybody should have to hunt for: it is the largest thing on
                // the board and it stays put for the rest of the run, so a dealt lane would put it
                // over a ward on some devices and off the edge of the hill on others.
                // **Off the field's stream, and that is a decision rather than an oversight.**
                // A lane is drawn once per raider in a sequence fixed by play rather than by
                // frame rate, so sharing the field's stream is deterministic across devices,
                // which is all invariant 37e asks — and ten shipped rungs are tuned against the
                // field this interleaving deals. What genuinely needs a stream of its own is a
                // draw that happens on a *timer* nobody's taps order, which is where a weaver and
                // a thief reach for a cell; see `_hill`.
                // **Where a boss stands is `SiegeTuning.BossLane`** - the middle alone, either
                // side of it as a pair - said once so the view and the render can draw the hill
                // the board is playing. A boss wave sends bosses and nothing else, so the wave's
                // own size is how many of them there are.
                // **A raider walks down its colour's lane, strayed by one.** The draw is taken
                // whatever becomes of it - a boss stands where `BossLane` says - so the field's
                // stream advances exactly the number of times it always did and every shipped
                // seed still deals the board it dealt before (invariant 41). See `SiegeLanes`.
                uint roll = Next();

                int lane = boss
                         ? SiegeTuning.BossLane(i, bosses)
                         : SiegeLanes.Walk(Layout.WardOf(SiegeLayout.Letters[colour]),
                                           Layout.Wards.Length, roll);

                _raiders.Add(new SiegeRaider(_minted++, colour, kind, lane,
                                             i * SiegeTuning.RaiderSpacing, surge));

                // Asked here, at the muster, because the question is whether the player banked
                // the boss's colour *ahead* of the duel rather than whether they reacted once it
                // was standing there. Recorded only; nothing below reads it.
                if (boss)
                {
                    var own = colour >= 0 && colour < _wards.Length ? _wards[colour] : null;
                    Attention.BossMet(own != null && own.Fuelled);
                }
            }

            _report.Wave = _wave;
            _wave++;

            // A boss gets its own quiet in front of it - long for a warlord, short for a
            // warbringer, and `SiegeTuning.RestBefore` says why each. The shortcut above is
            // unaffected, so a player who has cleared the hill still gets the boss at once.
            // The quiet before whatever is next. Asked of the wave that is *coming* rather than
            // of the one just sent, and per kind, because a warlord wants a long one in front of
            // it and a warbringer wants a short one (`SiegeTuning.RestBefore`).
            _rest = Layout.SizeOf(_wave) > 0 && SiegeTuning.IsBoss(Layout.KindAt(_wave, 0))
                  ? SiegeTuning.RestBefore(Layout.KindAt(_wave, 0))
                  : SiegeTuning.BetweenWaves;
        }

        /// <summary>
        /// The hill walks.
        ///
        /// <para>
        /// <b>It slides, and it was stepped for exactly one build.</b> The beat was a real finding
        /// - a body moving continuously is a gradient and a gradient can only be *felt*, where a
        /// body that steps can be *counted* - and the owner withdrew it on sight. What a drum buys
        /// in legibility it spends on feel: a raid that ticks forward reads as a board game rather
        /// than as something coming at you, which is the one thing this hill is for. The
        /// observation survives and is worth having the next time a mode needs a readable clock;
        /// the implementation does not.
        /// </para>
        /// </summary>
        void Walk(float dt)
        {
            // **The roar runs down on the board's own clock and lifts by itself.** A raider never
            // holds a copy of it, so nothing can be left charging after the warbringer that
            // started it is dead - which is the same rule `Arrive` keeps for a spell whose caster
            // has fallen, and for the same reason.
            if (_roar > 0f) _roar = Math.Max(0f, _roar - dt);

            float charge = _roar > 0f ? SiegeTuning.Rally : 1f;

            for (int i = 0; i < _raiders.Count; i++)
            {
                var raider = _raiders[i];
                if (!raider.Alive) continue;

                if (raider.Flash > 0f) raider.Flash = Math.Max(0f, raider.Flash - dt);

                if (raider.Wait > 0f)
                {
                    raider.Wait -= dt;
                    continue;
                }

                if (raider.March >= raider.Hold) continue;

                // **A boss does not answer its own roar.** A warbringer that hurried itself into
                // place would shorten the entrance the roar exists to make frightening, and a
                // warlord hastened by somebody else's roar could reach its ground before the level
                // meant it to - so the charge is the hill's, and the hill is what walks.
                raider.March += dt * raider.Pace * (raider.Boss ? 1f : charge)
                              / SiegeTuning.MarchOf(raider.Kind);

                if (raider.March < raider.Hold) continue;

                // **Stopped where its kind stops**, which for a warlord is the middle of the hill
                // and for everything else is the line. Nothing here needs to know which: the two
                // differ by one number the raider was minted with.
                raider.March = raider.Hold;

                // **A boss reaching its ground is the first phase opening**, which is the frame
                // it stops being untouchable and the frame its guard goes up in the same breath
                // - see `SiegeBoard.Fight.cs`. It is reported in `Arrived` like anything else
                // reaching where it stops, so the view has one list for "something got there".
                if (raider.Boss)
                {
                    OpenPhase(raider, 0);
                    _report.Arrived.Add(raider.Id);
                    continue;
                }

                if (!raider.AtTheLine) continue;

                raider.Blow = SiegeTuning.BlowEvery;
                _report.Arrived.Add(raider.Id);
            }
        }

        void Shoot(float dt)
        {
            for (int w = 0; w < _wards.Length; w++)
            {
                var ward = _wards[w];
                if (!ward.Alive) continue;

                // **A doused ward burns its seconds down here rather than in a step of its own**,
                // because the one thing that must never happen is a ward whose dark has expired
                // waiting a frame to notice: `Fuelled` is false while it is dark, so the frame
                // that clears it is the frame it may fire again.
                if (ward.Dark > 0f) ward.Dark = Math.Max(0f, ward.Dark - dt);

                // **And a chained one burns its seconds in the same place, for the same reason.**
                // `Fuelled` is false while either is running, so the frame that clears one is the
                // frame the ward may fire again — and the two are ticked together so neither can
                // ever be a frame ahead of the other.
                if (ward.Bound > 0f) ward.Bound = Math.Max(0f, ward.Bound - dt);

                if (!ward.Fuelled) { ward.Cool = 0f; continue; }

                ward.Cool -= dt;
                if (ward.Cool > 0f) continue;

                var target = Aim(ward);
                if (target == null) { ward.Cool = 0f; continue; }

                ward.Cool = SiegeTuning.FireEvery;

                // **What this bolt is worth is asked of the ward, about the raider.** A boss is
                // answered by the whole line whatever it wears (`SiegeTuning.EveryWardReaches`),
                // so "may this ward fire at this" and "for how much" are one question and are
                // asked in one place. A call site comparing colours here would be a second opinion
                // about what "its own colour" means.
                //
                // **`weak` is the double and nothing else.** Under the lock every primary bolt at
                // an ordinary raider lands in full, so it is true on every ordinary shot and
                // `SiegeTuning.PerfectMatch` — which has always assumed exactly that — stops being
                // an optimistic reading and becomes an identity. A duel answered with the wrong
                // colour is the one place it is false, which is exactly what the view draws in
                // white rather than gold.
                int share = ward.ReachTenths(target);
                bool weak = ward.Doubles(target);

                int damage = SiegeTuning.DamageTo(target.Kind, ward.Rank, true, ward.Build);
                if (!weak) damage = Math.Max(1, damage * share / 10);

                // **Both halves of a rank are spent here**, and they are the reason a cog is worth
                // more than the sum of its parts: an upgraded ward hits harder *and* gets more
                // bolts out of the same match, so a rank-four turret turns one match into 2.33
                // times the damage a fresh one would.
                //
                // **And a part-weight bolt costs a part of the fuel**, which is what makes a shot
                // this ward would not otherwise have fired genuinely free rather than the player's
                // fuel converted at half rate on their behalf — see `SiegeTuning.FuelShot`. It is
                // spent after the share is known and before anything is reported, so the two can
                // never be read from different answers.
                ward.Fuel = Math.Max(0f, ward.Fuel - SiegeTuning.FuelShot(ward.Rank, share));
                ward.Shots++;

                // Through the one door (`SiegeBoard.Fight.cs`). `Aim` never picks an untouchable
                // boss, so what comes back differs from `damage` only at a phase's floor - and
                // what is reported is what landed.
                damage = Wound(target, damage);

                bool killed = Fell(target);
                if (killed) Refund(ward);

                _report.Bolts.Add(new SiegeBolt(w, target.Id, damage, weak, killed));

                // **After the bolt has landed at full strength, never instead of it.** See
                // `SiegeBoard.Line.cs` for why nothing in an ability may reduce the primary hit.
                Ability(ward, w, target, damage);
            }
        }

        /// <summary>
        /// What a ward will shoot at: the furthest raider of its own colour, and then a boss, only
        /// when its own colour has nothing left standing.
        ///
        /// <para>
        /// <b>A turret only ever attacks its own colour, and that one line is the mode.</b> It
        /// used to prefer its own colour and fall back to whatever was nearest, which meant the
        /// elemental double was a bonus the player received for free: four wards firing at once
        /// eventually landed everything on its own kind whatever anybody matched, so <em>which</em>
        /// colour to feed decided nothing and "take the biggest match on the field" was correctly
        /// the optimal play. <b>A bonus nobody has to earn cannot change behaviour.</b> A lock can.
        /// </para>
        /// <para>
        /// <b>A boss is the one thing the lock does not hold, and the reason is that a duel offers
        /// no choice for it to protect.</b> One raider wearing one colour means three of the four
        /// turrets a player chose have nothing to fire at, so the finale was answered by a quarter
        /// of the line at a quarter of what a match delivers — see
        /// <see cref="SiegeTuning.EveryWardReaches"/>, which owns the rule.
        /// </para>
        /// <para>
        /// <b>And it is what makes fuel a resource rather than a pass-through.</b> A ward with
        /// nothing to fire at holds what it is given, so a colour matched while its raiders are
        /// off the hill <em>banks</em> — which is the half of the loop this mode never had, and
        /// the half a breather is worth having for. The caller arranges none of that: it simply
        /// gets no target and leaves the tube alone.
        /// </para>
        /// <para>
        /// <b>Own colour first, always.</b> Anything a ward reaches beyond it is a shot it would
        /// otherwise not have fired, so reaching for it only when its own colour is clear is what
        /// keeps every such rule strictly additive: picking the furthest of either would let a
        /// part-weight bolt displace an own-colour target at full, which is a turret somebody paid
        /// for making their own bolt weaker (invariant 42).
        /// </para>
        /// <para>
        /// <b>And a boss last, for the same reason and it is the stronger half of it.</b> Every
        /// ward answers a boss whatever colour it wears
        /// (<see cref="SiegeTuning.EveryWardReaches"/>), so a duel is fought by the whole line
        /// rather than by the one turret that happened to match — but a boss holds the middle of
        /// the hill while its escort walks to the wards, and a line that turned to face the boss
        /// would be a line taken apart by the wave standing in front of it. So a boss is what a
        /// ward shoots when it has nothing of its own left to shoot: strictly a bolt it would
        /// otherwise not have fired at all, which is what keeps it out of par's way exactly as a
        /// partner shot is.
        /// </para>
        /// </summary>
        SiegeRaider Aim(SiegeWard ward)
        {
            SiegeRaider own = null, boss = null;

            for (int i = 0; i < _raiders.Count; i++)
            {
                var raider = _raiders[i];
                if (!raider.Alive || !raider.OnTheHill) continue;

                // **A boss that cannot be hurt is not a target**, whatever colour it wears - a
                // ward with nothing else to shoot at banks, exactly as it does against an
                // ironclad, rather than spending a tube on a thing behind a guard. Asked before
                // the colour, because an own-colour bolt at an untouchable boss is fuel converted
                // into nothing on the player's behalf (37bq's fault from the other side).
                if (raider.Untouchable) continue;

                if (raider.Colour == ward.Colour)
                {
                    if (own == null || raider.March > own.March) own = raider;
                    continue;
                }

                if (!SiegeTuning.EveryWardReaches(raider.Kind)) continue;
                if (boss == null || raider.March > boss.March) boss = raider;
            }

            return own ?? boss;
        }

        void Conjure(float dt)
        {
            for (int i = 0; i < _raiders.Count; i++)
            {
                var boss = _raiders[i];

                // Bosses only. A weaver and a thief cast too, on their own timer and at the field
                // rather than at the line — see `Meddle`.
                if (!boss.Boss || !boss.Alive || !boss.InPlace) continue;

                boss.Stood += dt;

                // The guard runs down here, on the board's clock, whatever the boss is doing -
                // see `Guarding` for what drops it and why a stun does not hold it.
                Guarding(boss, dt);

                // **A stunned boss does not cast**, which is the decision a stun turret is bought
                // for: a duel is one raider wearing one colour, so standing a stun on *that*
                // colour is the one thing on the shelf that can take seconds off the finale's
                // spell rather than health off the boss (invariant 26h - the player decides, and
                // can be wrong).
                if (boss.Stunned) continue;

                boss.Spell -= dt;
                if (boss.Spell > 0f) continue;

                var craft = boss.Spellcraft;

                // The first spell of a phase is the one the guard stands in front of; the guard
                // drops when it lands (`Arrive`). Decided before the target, because what an
                // opener wants can differ from what an ordinary cast wants (`Wanted`).
                bool opens = boss.Guarded && !boss.Opening;

                // **A roar is thrown at the hill, so it carries no ward.** Three of the four aim
                // at the line and one does not, and the difference is asked once here rather than
                // by every reader of a ward index nobody set.
                int ward = SiegeTuning.AimsAtAWard(boss.Kind) ? Wanted(craft, boss, opens) : -1;

                // **A cast that found nothing to aim at does not spend its cadence.** The timer
                // used to be re-armed above, before the target was known, so a blightcaller that
                // found every ward already dark - or, until `Wanted` was narrowed, one that found
                // nothing but empty ones - stood in silence for a further
                // `SiegeTuning.CastEveryFor` seconds having done nothing at all. On the one boss
                // whose spell takes no health that is indistinguishable from a boss that does not
                // work, which is exactly how it was reported from play.
                //
                // Re-arming short instead means the spell lands on the frame there is something
                // to take. It can only ever make a cast arrive *sooner than it would have* and
                // never more often than the cadence, because a cast that lands re-arms in full.
                if (ward < 0 && SiegeTuning.AimsAtAWard(boss.Kind))
                {
                    boss.Spell = SiegeTuning.CastRetry;
                    continue;
                }

                // **A bonecaller that has spent its raises stops casting**, and that is invariant
                // 5d rather than tidiness: par counts exactly `RaisesInAll` bodies, so a fourth
                // raise would put raiders on a hill nothing priced — and a cast that went through
                // the tell, the flight and the ring and then raised nothing would be a boss
                // visibly doing nothing, which is the reading `CastRetry`'s note is about.
                if (SiegeTuning.Summons(boss.Kind) && boss.Raised >= SiegeTuning.Raises)
                {
                    // **A guard in front of a spell that will never be thrown is a wall**
                    // (invariant 5d), so a bonecaller that has spent its raises drops it at once
                    // rather than at the deadline.
                    if (boss.Guarded) Unguard(boss);
                    continue;
                }

                // **The cadence is the phase's** (`SiegeTuning.PhasePaceHundredths`), so a boss
                // in its last third casts at half the rate it opened with - which is the ladder
                // the fight climbs.
                boss.Spell = SiegeTuning.CastEveryFor(boss.Kind, boss.Phase);
                boss.Casts++;

                if (opens) boss.Opening = true;

                float lands = SiegeTuning.BossTell + SiegeTuning.BossFlight;

                _spells.Add(new Flight { Raider = boss.Id, Ward = ward, Craft = craft, In = lands,
                                         Opens = opens });
                _report.Casts.Add(new SiegeCast(boss.Id, ward, craft, lands, boss.Phase, opens));
            }
        }

        /// <summary>
        /// Which ward a boss throws at, and each of the three that aim asks a different question.
        ///
        /// <para>
        /// <b>The freshest rather than the weakest, for a smite, and that is what keeps the fight
        /// winnable.</b> A warlord that finished off whatever was nearly down would take the line
        /// apart one ward at a time — and the ward it would reach first is the one whose colour the
        /// player has to feed to answer it, so the mode's own answer would be the thing it
        /// destroyed. Picking the freshest spreads the damage instead: the line comes down evenly,
        /// no colour is ever locked out, and a run that is losing is losing to arithmetic rather
        /// than to a trap. It is also what keeps <see cref="Stranded"/> an honest certainty
        /// (invariant 28f).
        /// </para>
        /// <para>
        /// <b>A douse wants the ward the player is filling</b>, because that is what makes it a
        /// decision rather than a tax: the fuel it takes is fuel somebody just earned, and the
        /// answer — feed a different colour, or spend a surge — is one they choose every few
        /// seconds. An already-dark ward is never chosen twice; there is nothing left to take and
        /// a second one would read as the boss doing nothing. An <em>empty</em> one still is
        /// chosen, and that was measured rather than assumed — see the clause itself. When every
        /// standing ward is already out there is nothing to throw at, and the boss holds its cast
        /// rather than spending it (<see cref="SiegeTuning.CastRetry"/>).
        /// </para>
        /// <para>
        /// <b>A sunder wants the best turret on the line</b>, which is the one thing in this
        /// chapter a player <em>earned</em> (invariant 37w). That makes where the cogs went a
        /// question the finale asks and a player can get wrong in both directions — pile them into
        /// one ward and the overlord can take the pile; spread them and nothing on the line is
        /// strong. Ties go to the freshest, so once the ranks are level it spreads exactly as a
        /// smite does and cannot dismantle the line one ward at a time.
        /// </para>
        /// </summary>
        /// <summary>
        /// How many live raiders of one colour are on the hill.
        ///
        /// <b>Named away from <see cref="Standing"/></b>, which counts everything this run still
        /// has to see off including bodies that have not walked on yet - a different question, and
        /// the compiler is the only thing that would ever have said so.
        ///
        /// <b>The hill rather than the whole list</b>, because a raider that has reached the line
        /// is one the ward is already failing to answer and one still mustering is not yet the
        /// player's problem — <c>OnTheHill</c> is the same window <see cref="Aim"/> shoots into,
        /// so what a shackler reads and what a ward can act on are the same set.
        /// </summary>
        int Pressing(int colour)
        {
            int many = 0;

            for (int i = 0; i < _raiders.Count; i++)
            {
                var raider = _raiders[i];
                if (raider.Alive && raider.OnTheHill && raider.Colour == colour) many++;
            }

            return many;
        }

        int Wanted(SiegeSpell craft, SiegeRaider caster, bool opens)
        {
            int best = -1;
            long most = -1;

            for (int w = 0; w < _wards.Length; w++)
            {
                var ward = _wards[w];
                if (!ward.Alive) continue;

                // Ranked so a single comparison decides, with health as the low half of the key —
                // written this way rather than as three loops because three loops is three places
                // that can come to disagree about what "standing" means.
                long rank;
                switch (craft)
                {
                    // **An empty ward is still worth putting out, and that is not an oversight.**
                    // It reads like one - "take the fire" with no fire to take - and refusing an
                    // empty tube was tried and is strictly worse: a match keeps a ward firing for
                    // about two seconds, so at the instant a boss decides, most tubes are empty
                    // most of the time, and a blightcaller that would only throw at a full one
                    // throws far less often and takes *less*. What a douse really costs is the
                    // five seconds of dark (`SiegeWard.Snuff`), which land whatever was in the
                    // tube. The fuel is the tie-break, not the point.
                    case SiegeSpell.Douse:
                        if (ward.Doused) continue;
                        rank = (long)(ward.Fuel * 1000f) * 64L + ward.Health;
                        break;

                    // **Only the opener sunders** (`SiegeTuning.OverlordSunder`), so only the
                    // opener wants the best turret; every other overlord spell is a smite and
                    // wants what a smite wants, the freshest.
                    case SiegeSpell.Sunder:
                        rank = opens ? (long)ward.Rank * 64L + ward.Health : ward.Health;
                        break;

                    // **A bind wants the ward the hill most needs answered**, which is the one
                    // piece of information no other spell here reads: a smite reads the line, a
                    // douse reads a tube and a sunder reads a badge, and all three are facts about
                    // the player's own side. A chain is only a decision if it takes the colour
                    // that was about to matter — chaining a ward with nothing to shoot at costs
                    // the player exactly nothing, which is invariant 5d wearing six seconds.
                    //
                    // **An already-chained ward is never chosen twice**, for the douse's reason:
                    // there is nothing left to take and a second chain reads as the boss doing
                    // nothing (`SiegeTuning.CastRetry` holds the cast instead).
                    case SiegeSpell.Bind:
                        if (ward.Shackled) continue;
                        rank = (long)Pressing(ward.Colour) * 64L + ward.Health;
                        break;

                    // **An ironclad strikes the one ward that can hurt it**, which is the fight:
                    // the colour the player has to feed to kill it is the colour it is trying to
                    // put out, so a duel against it is a race rather than a grind. It falls
                    // through to the freshest when that ward is already down, because a boss with
                    // nothing left to aim at would otherwise hold its cast for ever
                    // (`CastRetry`) and read as broken.
                    case SiegeSpell.Aegis:
                        rank = (caster != null && ward.Colour == caster.Colour ? 1L << 40 : 0L)
                             + ward.Health;
                        break;

                    default:
                        rank = ward.Health;
                        break;
                }

                if (rank <= most) continue;

                most = rank;
                best = w;
            }

            return best;
        }

        void Swing(float dt)
        {
            for (int i = 0; i < _raiders.Count; i++)
            {
                var raider = _raiders[i];
                if (!raider.AtTheLine) continue;

                // **A stunned raider does not swing, and its wind-up is held rather than lost.**
                // Stopping the march alone would be a stun that costs a raider already at the line
                // nothing at all — which is the half of the hill it is worth most against, because
                // that is where a second of quiet is a blow the line did not take.
                if (raider.Stunned) continue;

                raider.Blow -= dt;
                if (raider.Blow > 0f) continue;

                raider.Blow = SiegeTuning.BlowEvery;

                int w = Nearest(raider.Lane);
                if (w < 0) continue;

                var ward = _wards[w];
                int damage = raider.Surge.Blow(SiegeTuning.BlowOf(raider.Kind));
                ward.Health -= damage;

                bool felled = ward.Health <= 0;
                if (felled)
                {
                    ward.Health = 0;
                    ward.Alive = false;
                    ward.Fuel = 0f;
                }

                _report.Blows.Add(new SiegeBlow(w, raider.Id, damage, felled));
            }
        }

        /// <summary>
        /// The standing ward nearest a lane, or -1 when the line is gone.
        ///
        /// A raider whose own ward has fallen walks along the line to the next one rather than
        /// standing in front of a hole, which is what stops a run being decided by which lane a
        /// raider happened to be dealt.
        /// </summary>
        int Nearest(int lane)
        {
            // Lanes and wards are both spread evenly across the same width, so a lane's place on
            // the line is a fraction rather than an index.
            float want = SiegeTuning.Lanes <= 1 ? 0f : lane / (float)(SiegeTuning.Lanes - 1);

            int best = -1;
            float closest = float.MaxValue;

            for (int w = 0; w < _wards.Length; w++)
            {
                if (!_wards[w].Alive) continue;

                float at = _wards.Length <= 1 ? 0f : w / (float)(_wards.Length - 1);
                float gap = Math.Abs(at - want);

                // **A tie goes to the healthier ward**, and that is what stops five lanes over four
                // wards being an unfair map. Lane two sits exactly between the middle pair, so a
                // first-wins tie-break sent every raider in it at the same turret for the whole
                // run - two lanes' worth of blows on one ward while another took none. Ties are
                // broken toward health for `Wanted`'s reason (invariant 37t): the line comes down
                // evenly, no colour is locked out, and a run that is losing is losing to
                // arithmetic rather than to a map nobody could read.
                bool nearer = gap < closest - .001f;
                bool tied = !nearer && gap <= closest + .001f;

                if (!nearer && !(tied && (best < 0 || _wards[w].Health > _wards[best].Health)))
                    continue;

                closest = gap;
                best = w;
            }

            return best;
        }
    }
}
