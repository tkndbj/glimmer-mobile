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
        /// raiders walk, then the wards shoot at where the raiders now are, then every standing
        /// boss's fight is brought up to date, then the warlord casts, then whatever reached the
        /// line swings. A ward that has just been fuelled therefore gets its bolt away in the same
        /// step, a stand finished by that bolt turns before the boss decides what to throw, and a
        /// raider killed by it never lands the blow it was about to — nor does a warlord killed by
        /// it ever start the spell it was about to.
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
            Fights(dt);
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
                if (ward.Fill(charge.Fuel, out bool redeemed)) _report.Brimmed.Add(charge.Ward);

                // **A paid seal is reported where the fuel lands and nowhere else.** The toll is
                // counted inside `SiegeWard.Fill` so the two doors fuel comes through cannot
                // disagree about it; what each door still owes is saying so, because a seal that
                // broke in silence would be the player paying for something they never saw
                // happen.
                if (redeemed) _report.Redeemed.Add(charge.Ward);
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

                // **The spell that opened a stand has landed**, which is half of what settles it
                // (`SiegeTuning.PhaseLeast` is the other half) - noted before anything below can
                // `continue` past it, because a spell aimed at a ward that has since fallen is
                // dropped and a stand must not be held open on a spell that was thrown. What the
                // floor promises is one spell thrown, not one spell that found something.
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

                        bool down = Bear(shaken, shook);

                        // One record per ward, so the view draws a hit on each of them from the
                        // same list every other spell arrives on. The roar itself is drawn off the
                        // *cast* (`SiegeView.Roar`), which is where its ring already comes from.
                        _report.Spells.Add(
                            new SiegeSpellLanded(spell.Raider, w, SiegeSpell.Rally, shook, down));
                    }

                    continue;
                }

                // **A wane is aimed at no ward either, and it is settled here for the rally's
                // reason** - `spell.Ward` is -1, so it must not fall through to the indexed path
                // below. What separates the two is who pays: a roar takes the same from all four
                // whatever the player did, and a wane takes nothing at all from a post that has
                // landed a bolt since the last cast (`SiegeBoard._worked`). That is the verb, and
                // it is the whole of invariant 5d's test - there is an arrangement it rejects.
                //
                // **The window is closed here, after it is read**, so the seconds between one
                // cast and the next are billed exactly once and the cadence and the window can
                // never be two numbers (`SiegeTuning.HollowkingCastEvery`).
                if (spell.Craft == SiegeSpell.Wane)
                {
                    int bite = SiegeTuning.CastOf(caster.Kind);

                    for (int w = 0; w < _wards.Length; w++)
                    {
                        var post = _wards[w];
                        if (!post.Alive) continue;

                        bool worked = _worked[w];
                        _worked[w] = false;

                        // **Nothing is reported for a post that was working**, which is what a
                        // player has to be able to see: the fed posts are silent and the hollow
                        // ones flash, so the rule is read off the board rather than off a
                        // caption.
                        if (worked) continue;

                        bool down = Bear(post, bite);

                        _report.Spells.Add(
                            new SiegeSpellLanded(spell.Raider, w, SiegeSpell.Wane, bite, down));
                    }

                    continue;
                }

                // **Two spells that are aimed at the hill rather than at the line** do their work
                // on the hill first, and then smite the freshest ward like any other (37dn): the
                // verb is what tells the bosses apart, the health is what makes each a threat.
                // Their hill half is reported by `Devour` and `Raise`; the smite is reported
                // below as a smite, so the view draws a hit at the post and nothing else.
                if (spell.Craft == SiegeSpell.Devour) Devour(caster);
                if (spell.Craft == SiegeSpell.Raise) Raise(caster);

                if (spell.Ward < 0 || spell.Ward >= _wards.Length) continue;

                var ward = _wards[spell.Ward];
                if (!ward.Alive) continue;

                // **The verb first, then the health, so one record carries both.** A douse empties
                // the tube, a bind chains the ward and takes nothing off the tube, a sunder takes a
                // rank on the phase's opener - and every one of them then takes `CastOf` off the
                // ward, which is what stopped "it attacks and my turrets lose nothing" (37dn).
                if (spell.Craft == SiegeSpell.Douse) ward.Snuff();
                if (spell.Craft == SiegeSpell.Bind) ward.Shackle();
                if (spell.Craft == SiegeSpell.Bury) ward.Bury();
                if (spell.Craft == SiegeSpell.Glare) ward.Glare();

                // **A harrow takes the rank and puts it on the ground in the same breath**, and
                // the two halves are one call so neither can happen without the other: a rank
                // taken with no cog dropped is an overlord's sunder wearing a different name,
                // and a cog dropped with no rank taken is a gift. `Scatter` answers false when
                // the hill is already carrying `SiegeTuning.MostCogs`, in which case the rank
                // stays where it is - the boss still smites below (37dn), so a cast that finds
                // nothing to take is never a cast that does nothing.
                bool harrowed = spell.Craft == SiegeSpell.Harrow && Scatter(caster, spell.Ward);

                // **A seal is refused where it cannot honestly be answered**, and there are two
                // such places: a ward already sealed (a second clock the player could never
                // finish - the boulder's rule, `SiegeWard.Condemn`) and the last ward standing
                // (a verb that could end a run on its own, `SiegeKind.Sunlord`). Both fall
                // through to the smite below, which is the colossus's clause and is here for its
                // reason: a boss that can find nothing to do holds its cast for ever
                // (`SiegeTuning.CastRetry`) and reads as broken.
                if (spell.Craft == SiegeSpell.Doom && OnTheLine > 1) ward.Condemn();

                // **A drain takes the charges first and lands them as its own weight.** Every
                // charge held is `ThundererDrain` more off the ward, so what a player banked
                // through the tell is what the bolt is worth - and one thrown before it landed
                // is on the boss instead.
                int taken = spell.Craft == SiegeSpell.Drain ? ward.Drain() : 0;

                bool sundered = (spell.Craft == SiegeSpell.Sunder && spell.Opens && ward.Sunder())
                             || harrowed;

                var craft = spell.Craft == SiegeSpell.Devour || spell.Craft == SiegeSpell.Raise
                          ? SiegeSpell.Smite : spell.Craft;

                int cast = SiegeTuning.CastOf(caster.Kind) + taken * SiegeTuning.ThundererDrain;
                bool felled = Bear(ward, cast);

                _report.Spells.Add(new SiegeSpellLanded(spell.Raider, spell.Ward, craft,
                                                        cast, felled, sundered, taken));
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

        /// <summary>
        /// Whether a boss is standing on this hill right now — walking on, in place, or mid-fall.
        ///
        /// <b>The one reading behind "a boss is alone"</b> (invariant 37dn), asked by
        /// <see cref="Muster"/> on both sides of a duel: nothing musters onto a hill a boss is
        /// still on, and a boss does not muster onto a hill anything is still on. Written once so
        /// the two halves of the rule cannot come to disagree, and asked of the <em>hill</em>
        /// rather than of the wave table, so it holds for an authored ladder and a derived
        /// endless lane alike without either being taught about it.
        /// </summary>
        public bool BossStanding
        {
            get
            {
                for (int i = 0; i < _raiders.Count; i++)
                    if (_raiders[i].Boss && _raiders[i].Alive) return true;

                return false;
            }
        }

        /// <summary>Whether anything at all of the raid is still alive on this hill.</summary>
        bool HillHolds
        {
            get
            {
                for (int i = 0; i < _raiders.Count; i++)
                    if (_raiders[i].Alive) return true;

                return false;
            }
        }

        void Muster(float dt)
        {
            if (_wave >= Layout.WaveCount) return;

            // **Nothing follows a boss onto the hill while it lives (invariant 37dn).**
            //
            // A duel is the one wave this mode does not stack on the last, and it has two sides:
            // the boss must not arrive over a wave still swinging, and a wave must not arrive over
            // a boss still standing. Only the first was ever written down, which was enough for an
            // authored ladder — a chapter's boss rides its last wave, so nothing was ever coming
            // behind it — and was no rule at all on the Infinite lane, where the schedule carries
            // on regardless and a warbringer spent the back half of its fight inside the next
            // wave's escort.
            //
            // **Asked of the hill rather than of the schedule**, so it is one rule rather than one
            // per lane: `BossStanding` is a fact about what is standing there, which is the same
            // question on a ten-rung chapter, on an endless ramp and on whatever a seventh chapter
            // authors. The clock is held rather than run down (the `return` is above `_rest`), so
            // the quiet after a duel is a whole quiet and not whatever was left of one.
            if (BossStanding) return;

            // **On a clock, or the moment the hill is empty — whichever comes first.**
            //
            // The clock alone was the fix for waiting-on-a-clear, which let a winning player
            // stroll; a clear alone is what it replaced. Both together are what the mode actually
            // wants: the clock is the pressure and never lets up, and the shortcut means a player
            // who is *ahead* of it is rewarded with the next wave rather than made to stand and
            // watch an empty field. Note the guard — the shortcut cannot fire before the first
            // wave, because the hill is legitimately empty at the start of every run.
            // **A boss comes in alone, and it waits for the hill to be cleared (37dn).** The
            // clock does not run for a boss wave while anything is still walking: this is the
            // one wave the mode does not stack on the last, so a duel is never fought over a wave
            // still swinging at the line. The clear-hill shortcut below then brings it on inside
            // a breather, exactly as it brings on any wave the player is ahead of.
            //
            // **`BossesIn` rather than the first raider's kind**, so a pair wave (the endless
            // lane deals two) is held back by the same clause that holds back a lone one. The
            // wave's own index nought is a boss in every shape either lane authors, but reading
            // the whole wave is the question actually being asked.
            if (_wave > 0 && Layout.BossesIn(_wave) > 0 && HillHolds) return;

            _rest -= dt;

            if (_rest > 0f)
            {
                if (_wave == 0) return;

                if (HillHolds) return;

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
                    // Any ward, because a boss wears no colour (37dn): the question is whether
                    // the player banked anything ahead of the duel.
                    bool fuelled = false;
                    for (int w = 0; w < _wards.Length; w++) if (_wards[w].Fuelled) fuelled = true;
                    Attention.BossMet(fuelled);
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
            _rest = Layout.BossesIn(_wave) > 0
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

            // **The hourglass runs down here for the roar's reason**: one number on the board, so
            // nothing can be left standing still after the window has closed, and the frame it
            // reaches nought is the frame the hill walks again.
            if (_still > 0f) _still = Math.Max(0f, _still - dt);

            float charge = _roar > 0f ? SiegeTuning.Rally : 1f;

            for (int i = 0; i < _raiders.Count; i++)
            {
                var raider = _raiders[i];
                if (!raider.Alive) continue;

                if (raider.Flash > 0f) raider.Flash = Math.Max(0f, raider.Flash - dt);

                // **An anvil's shove is worked off before anything else and through a stopped
                // hill** (`SiegeCharm.Anvil`, `SiegeRaider.Shove`). Two reasons, and neither is
                // a preference. It is *before* the march because a body cannot be walking down
                // and being thrown up in the same step, and paying the debt first is what makes
                // the two states one branch rather than two numbers that could disagree. It is
                // *through* the stop because the stop is the hill's clock and the shove is the
                // player's payoff: an hourglass already standing when an anvil lands would
                // otherwise swallow the shove whole and hand it back three seconds later, which
                // is a charm eating a charm.
                if (raider.Heave > 0f)
                {
                    float back = dt * SiegeTuning.AnvilPace;
                    if (back > raider.Heave) back = raider.Heave;

                    raider.Heave -= back;
                    raider.March -= back;
                    if (raider.March < 0f) raider.March = 0f;

                    // **A body being thrown is not a body walking**, so it takes no ground this
                    // step - and it is not at the line either, which is what stops its blows:
                    // `AtTheLine` reads the march, so a raider shoved off the line puts its
                    // weapon down for as long as it takes to walk back, with no second rule
                    // anywhere saying so.
                    continue;
                }

                // **A stopped hill is stopped whole** - the raiders still in the wings as well as
                // the ones walking, so a wave mustered into the window stands at the crest until
                // it opens. What still moves is the flash above and everything the line does.
                if (_still > 0f) continue;

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

                // **A boss reaching its ground is the first stand opening**, which is the frame it
                // stops being untouchable and the frame every fed ward on the line may fire at it
                // - see `SiegeBoard.Fight.cs`. There is no beat of hesitation between the two, and
                // there used to be three and a half seconds of one. It is reported in `Arrived`
                // like anything else reaching where it stops, so the view has one list for
                // "something got there".
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

                // **And a buried one weathers here too, for the third time for the same reason.**
                // A boulder holds a post for `SiegeTuning.ColossusBury` seconds at the outside;
                // `Fuelled` is false while a piece still stands, so the frame the last one slips
                // is the frame the ward may fire again — whether the clock took it or the player
                // did (`SiegeWard.Dig`).
                ward.Weather(dt);

                // **A glare burns down here with the other two, for the other two's reason** -
                // one place, so none of them can ever be a frame ahead of another. It is not
                // read by `Fuelled`, though, and that is the verb: a stone-struck ward is
                // fuelled, does fire and lands nothing (`SiegeWard.Glared`).
                if (ward.Stone > 0f) ward.Stone = Math.Max(0f, ward.Stone - dt);

                // **And the sunlord's seal, which is the one clock here with an ending of its
                // own.** The other four expire and hand the ward back; this one expires and
                // takes it. Settled in the same step it is counted in so the frame it runs out
                // is the frame the ward falls, and reported like any other thing that happens to
                // the line - `SiegeSpellLanded` with the seal's own verb, so the view has one
                // list to read and nothing has to be told twice.
                if (ward.Sealed > 0f)
                {
                    ward.Sealed = Math.Max(0f, ward.Sealed - dt);

                    if (ward.Sealed <= 0f)
                    {
                        ward.Absolve();

                        if (Topple(ward))
                        {
                            ward.Charges = 0;

                            _report.Spells.Add(
                                new SiegeSpellLanded(-1, w, SiegeSpell.Doom, 0, true));
                            continue;
                        }
                    }
                }

                if (!ward.Fuelled) { ward.Cool = 0f; continue; }

                ward.Cool -= dt;
                if (ward.Cool > 0f) continue;

                var target = Aim(ward);
                if (target == null) { ward.Cool = 0f; continue; }

                ward.Cool = SiegeTuning.FireEvery;

                // **A stone-struck ward pays for a shot it does not take** (`SiegeSpell.Glare`),
                // and it pays only when there was something to shoot at - the target is found
                // first for exactly that reason. What the fuel buys is nothing at all, which is
                // what makes the verb's answer *feed another colour* rather than *wait*.
                //
                // **The full shot's fuel, never a share.** A share is what a part-weight bolt
                // costs (`SiegeTuning.FuelShot(rank, share)`, invariant 37bq), and this bolt has
                // no weight at all to be a part of - charging a fraction would be the board
                // deciding the glare was only partly on.
                if (ward.Glared)
                {
                    ward.Fuel = Math.Max(0f, ward.Fuel - SiegeTuning.FuelShot(ward.Rank));
                    ward.Shots++;
                    _report.Stoned.Add(w);
                    continue;
                }

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

                // **The one place a wane's reading is written** (`SiegeBoard._worked`). Beside
                // the booking rather than beside the fuel, because what the verb asks is whether
                // a bolt left the barrel - a stone-struck ward spends the fuel above and never
                // reaches here, which is the answer a glare should give.
                _worked[w] = true;

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
                // ironclad, rather than spending a tube on a body nothing can take anything off.
                // Asked before the colour, because an own-colour bolt at a boss that cannot take
                // it is fuel converted into nothing on the player's behalf (37bq's fault from the
                // other side).
                //
                // **The window this skips is now the narrow one**: the walk in, and whatever
                // seconds a line quick enough to reach the stand's floor early has bought itself.
                // It used to include three to four seconds of guard at every stand, which is what
                // a player saw as their turrets refusing to fire at the boss.
                if (raider.Impervious) continue;

                // **A legendary ward treats every raider on the hill as its own colour**, which
                // is the one turret the lock does not hold (`SiegeWard.Unbound`). It is asked
                // here rather than by comparing colours at the call site for `ReachTenths`'
                // reason: "may this ward fire at this" is one question and lives in one place, so
                // the thing that aims and the thing that weighs the bolt cannot come to disagree.
                //
                // **A boss is still last, and the argument is unchanged.** It holds the middle of
                // the hill while its escort walks at the line, so a line that turned to face it
                // would be a line taken apart by the wave in front of it - true of a legendary
                // exactly as of anything else.
                if (ward.Unbound ? !raider.Boss : raider.Colour == ward.Colour)
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

                // **The stand's own clock is `Fights`', not this one**, and that is deliberate:
                // it runs on the board's clock whatever the boss is doing - stunned, stopped, or
                // retrying a cast that finds nothing to aim at - because a stand is a deadline
                // and a stun is seconds off the fight, never a wall.

                // **A stunned boss does not cast**, which is the decision a stun turret is bought
                // for: a duel is one raider wearing one colour, so standing a stun on *that*
                // colour is the one thing on the shelf that can take seconds off the finale's
                // spell rather than health off the boss (invariant 26h - the player decides, and
                // can be wrong).
                // **Nor does a boss cast into a stopped hill**, and its guard has already run down
                // above (invariant 37dl): an hourglass takes seconds off a fight and never walls it.
                if (boss.Stunned || _still > 0f) continue;

                boss.Spell -= dt;
                if (boss.Spell > 0f) continue;

                var craft = boss.Spellcraft;

                // The first spell of a stand is the one its floor is held for; the stand settles
                // when it lands (`Arrive`) and `PhaseLeast` has passed. Decided before the
                // target, because what an opener wants can differ from what an ordinary cast
                // wants (`Wanted`).
                //
                // **Read off the stand rather than off a guard**: this asked `boss.Guarded`,
                // which meant "the window in which nothing can hurt it", and the two stopped
                // being the same question the moment the window became a floor. What an opener
                // is is the first spell of a stand that has not thrown one - true whether or not
                // the line has already walked the bar down to the notch.
                bool opens = !boss.Opened && !boss.Opening;

                // **A roar is thrown at the hill and a wane at every idle post, so neither
                // carries a ward** (`SiegeTuning.CarriesAWard`). Every other spell carries one
                // (37dn): the aimed ones the ward their verb wants, and a devour or a raise the
                // freshest ward, which is where its smite lands. The view still asks
                // `AimsAtAWard` to decide what to draw crossing the hill. Asked of the rule rather
                // than of the rally by name: this line named the rally alone, so a wane was
                // handed the freshest ward by `Wanted`'s default arm — an index its landing never
                // reads, and a retry it could never hit — while the rule beside it said `Wanted`
                // was never asked about a wane at all.
                int ward = SiegeTuning.CarriesAWard(craft) ? Wanted(craft, boss, opens) : -1;

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
                if (ward < 0 && SiegeTuning.CarriesAWard(craft))
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
                    // **A floor in front of a spell that will never be thrown is a wall**
                    // (invariant 5d), so a bonecaller that has spent its raises settles its stand
                    // at once rather than resting on the notch until the deadline.
                    Settle(boss);
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

                    // **A drain wants the ward holding the most**, which is the one piece of
                    // information a smite, a douse, a sunder and a bind all read past: the
                    // charges are the verb, so the ward with none is the last one it wants.
                    // The freshest breaks a tie, so a line holding nothing is drained like a
                    // smite - and still smitten, because every spell takes health (37dn).
                    case SiegeSpell.Drain:
                        rank = (long)ward.Charges * 64L + ward.Health;
                        break;

                    // **A boulder wants the ward about to fire**, for the douse's reason: what a
                    // burial costs is what the buried ward was about to do, so the fullest tube
                    // is the one worth burying. **A buried ward is never chosen over a clear
                    // one** - the rubble is refused, not stacked (`SiegeWard.Bury`), so a second
                    // boulder on the same post would start no seconds.
                    //
                    // **It falls through to one anyway when every post is buried**, which is the
                    // ironclad's clause and is here for a harder reason than its own: a refusal
                    // here is `CastRetry`, and a boss that can find nothing to aim at while the
                    // line it buried cannot fire is both sides standing still - reported from
                    // play as *I cannot shoot and he does not attack*. A colossus with nowhere to
                    // throw throws at the freshest post and takes its `CastOf` like any other
                    // spell (37dn); the burial is what it cannot repeat, never the blow.
                    case SiegeSpell.Bury:
                        rank = (ward.Buried ? 0L : 1L << 40)
                             + (long)(ward.Fuel * 1000f) * 64L + ward.Health;
                        break;

                    // **A glare wants the fullest tube**, which is the douse's reading with
                    // the opposite meaning: a douse takes the fuel that is there, and a glare
                    // burns whatever arrives - so the ward the player is plainly feeding is the
                    // one worth freezing, because that is the ward the next few matches were
                    // already going to. An already-glared ward is never chosen twice, for the
                    // douse's and the bind's reason: there is nothing further to take and a
                    // second mask reads as the boss doing nothing.
                    case SiegeSpell.Glare:
                        if (ward.Glared) continue;
                        rank = (long)(ward.Fuel * 1000f) * 64L + ward.Health;
                        break;

                    // **A seal wants the ward the hill is *not* wearing**, which is the bind's
                    // reading turned over: the toll has to be paid in the sealed ward's own
                    // colour (invariant 37bl), so the expensive seal is the one on the colour
                    // nothing on the hill is asking for. `Pressing` answers how badly a colour is
                    // wanted, so the seal takes the lowest of it and the freshest breaks a tie.
                    //
                    // **A sealed ward is never chosen twice and the last one standing never at
                    // all**, both refused again at the landing (`Arrive`) so neither rule can be
                    // true in one place and not the other.
                    case SiegeSpell.Doom:
                        if (ward.Doomed) continue;
                        if (OnTheLine <= 1) { rank = ward.Health; break; }
                        rank = (long)(64 - Pressing(ward.Colour)) * 64L + ward.Health;
                        break;

                    // **A harrow wants the best-ranked ward**, which is the sunder's reading
                    // with the sunder's own reason: the rank is the verb, so the post with none
                    // is the last one it wants. What differs is what happens next - the rank is
                    // dropped rather than destroyed (`SiegeSpell.Harrow`) - and that is why an
                    // unranked line is not refused here: it falls through to the freshest and
                    // takes its `CastOf` like any other spell, for the colossus's reason
                    // (`SiegeTuning.CastRetry` over a line with nothing to take is a boss
                    // standing still).
                    case SiegeSpell.Harrow:
                        rank = (long)ward.Rank * 64L + ward.Health;
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

        /// <summary>
        /// What a ward bears, and the only place its health is taken. Answers whether it fell.
        ///
        /// <para>
        /// <b>Three things take a ward's health and every one of them used to do it in full</b> —
        /// a raider's blow, a boss's cast and a warbringer's roar — with the clamp, the flag and
        /// the emptied tube written out three times. They agreed, which is luck rather than
        /// design: it is one operation, so it is one method, and a fourth thing that hurts the
        /// line now inherits the whole of what falling means.
        /// </para>
        /// <para>
        /// <b>And it is the one place <see cref="Sheltered"/> can be honoured.</b> A guarantee
        /// the line cannot fall has to live where a ward is hurt; anywhere else it is a repair
        /// applied after the fact, which is a state the rest of the model has already seen.
        /// </para>
        /// </summary>
        bool Bear(SiegeWard ward, int damage)
        {
            if (Sheltered) return false;

            ward.Health -= damage;
            return ward.Health <= 0 && Topple(ward);
        }

        /// <summary>
        /// Takes a ward off the line: the one place one stops standing, and the one place
        /// <see cref="Sheltered"/> refuses.
        ///
        /// Separate from <see cref="Bear"/> because a sunlord's seal takes a ward without
        /// hurting it — the clock runs out and the ward is simply gone — so "fell by damage" and
        /// "fell" are two different sentences with one consequence.
        /// </summary>
        bool Topple(SiegeWard ward)
        {
            if (Sheltered) return false;

            ward.Health = 0;
            ward.Alive = false;
            ward.Fuel = 0f;
            return true;
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
                // **And it does not swing while the hill stands still**, for the stun's reason one
                // line up: a stop that held the march and left the blows running would cost a
                // raider already at the line nothing at all, which is the half of the hill an
                // hourglass is worth most against.
                if (raider.Stunned || _still > 0f) continue;

                raider.Blow -= dt;
                if (raider.Blow > 0f) continue;

                raider.Blow = SiegeTuning.BlowEvery;

                int w = Nearest(raider.Lane);
                if (w < 0) continue;

                var ward = _wards[w];
                int damage = raider.Surge.Blow(SiegeTuning.BlowOf(raider.Kind));
                bool felled = Bear(ward, damage);

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
