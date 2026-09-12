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

            Land(dt);
            Muster(dt);
            Walk(dt);
            Shoot(dt);
            Conjure(dt);
            Smoulder(dt);
            Swing(dt);

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

                // **The ward's own capacity, not the mode's constant.** A beacon holds half again
                // as much (`SiegeTuning.CapacityOf`), and clamping to the constant here would have
                // been a turret that costs credits, says it banks a cascade, and does not - a
                // whole ability that reads as broken with nothing anywhere to say why.
                ward.Fuel = Math.Min(ward.Capacity, ward.Fuel + charge.Fuel);
            }

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

                // A rank is taken *before* the health, so a spell that fells a ward has still
                // taken the rank it came for — and the view is told both in one record rather than
                // having to work out which order they happened in.
                bool sundered = spell.Craft == SiegeSpell.Sunder && ward.Sunder();

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
                int lane = boss ? SiegeTuning.BossLane(i, bosses)
                                : (int)(Next() % SiegeTuning.Lanes);

                _raiders.Add(new SiegeRaider(_minted++, colour, kind, lane,
                                             i * SiegeTuning.RaiderSpacing, surge));
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

        void Walk(float dt)
        {
            // **The roar runs down on the board's own clock and lifts by itself.** A raider never
            // holds a copy of it, so nothing can be left charging after the warbringer that
            // started it is dead — which is the same rule `Arrive` keeps for a spell whose caster
            // has fallen, and for the same reason: a boss that goes on affecting the hill after it
            // is destroyed reads as the game getting the last word.
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
                // meant it to — so the charge is the hill's, and the hill is what walks.
                raider.March += dt * raider.Pace * (raider.Boss ? 1f : charge)
                              / SiegeTuning.MarchOf(raider.Kind);

                if (raider.March < raider.Hold) continue;

                // **Stopped where its kind stops**, which for a warlord is the middle of the hill
                // and for everything else is the line. Nothing here needs to know which: the two
                // differ by one number the raider was minted with.
                raider.March = raider.Hold;

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

                if (!ward.Fuelled) { ward.Cool = 0f; continue; }

                ward.Cool -= dt;
                if (ward.Cool > 0f) continue;

                var target = Aim(ward.Colour);
                if (target == null) { ward.Cool = 0f; continue; }

                ward.Cool = SiegeTuning.FireEvery;

                // **Both halves of a rank are spent here**, and they are the reason a cog is worth
                // more than the sum of its parts: an upgraded ward hits harder *and* gets more
                // bolts out of the same match, so a rank-four turret turns one match into 2.33
                // times the damage a fresh one would.
                ward.Fuel = Math.Max(0f, ward.Fuel - SiegeTuning.FuelShot(ward.Rank));
                ward.Shots++;

                // **A prism turret is strong against two colours rather than one**, which is the
                // one ability that widens the mode's central rule instead of adding to it. It is
                // asked of the ward rather than compared here, so nothing can end up with a
                // second opinion about what "its own colour" means.
                bool weak = ward.StrongAgainst(target.Colour);
                int damage = SiegeTuning.DamageTo(target.Kind, ward.Rank, weak, ward.Build);

                target.Health -= damage;
                target.Flash = .18f;

                bool killed = Fell(target);
                if (killed) Refund(ward);

                _report.Bolts.Add(new SiegeBolt(w, target.Id, damage, weak, killed));

                // **After the bolt has landed at full strength, never instead of it.** See
                // `SiegeBoard.Line.cs` for why nothing in an ability may reduce the primary hit.
                Ability(ward, w, target, damage);
            }
        }

        /// <summary>
        /// What a ward shoots at: the raider of its own colour that is furthest down the hill,
        /// and otherwise whichever raider is furthest down.
        ///
        /// <b>Its own colour first, so the rule is visible in play.</b> A player who has fed the
        /// right ward sees the bolts go to the thing that colour hurts; one who has not sees them
        /// spread. Nothing has to be told about it.
        /// </summary>
        SiegeRaider Aim(int colour)
        {

            SiegeRaider weak = null, near = null;

            for (int i = 0; i < _raiders.Count; i++)
            {
                var raider = _raiders[i];
                if (!raider.Alive || !raider.OnTheHill) continue;

                if (near == null || raider.March > near.March) near = raider;

                if (raider.Colour != colour) continue;
                if (weak == null || raider.March > weak.March) weak = raider;
            }

            return weak ?? near;
        }

        /// <summary>
        /// The warlord's spells: chosen, telegraphed, and thrown at the line from where it stands.
        ///
        /// <para>
        /// <b>Nothing lands here.</b> A cast decides a target and books a
        /// <see cref="Flight"/>; <see cref="Arrive"/> is what takes the ward's health, a whole
        /// <see cref="SiegeTuning.BossTell"/> plus <see cref="SiegeTuning.BossFlight"/> later. That
        /// split is invariant 37s — a move's effect may not land before its animation does, and on
        /// a board whose clock never stops the only way to guarantee it is for the schedule to be
        /// a rule the view reads rather than a duration the view invents.
        /// </para>
        /// </summary>
        void Conjure(float dt)
        {
            for (int i = 0; i < _raiders.Count; i++)
            {
                var boss = _raiders[i];

                // Bosses only. A weaver and a thief cast too, on their own timer and at the field
                // rather than at the line — see `Meddle`.
                if (!boss.Boss || !boss.Alive || !boss.InPlace) continue;

                boss.Spell -= dt;
                if (boss.Spell > 0f) continue;

                var craft = boss.Spellcraft;

                // **A roar is thrown at the hill, so it carries no ward.** Three of the four aim
                // at the line and one does not, and the difference is asked once here rather than
                // by every reader of a ward index nobody set.
                int ward = SiegeTuning.AimsAtAWard(boss.Kind) ? Wanted(craft) : -1;

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
                if (ward < 0 && craft != SiegeSpell.Rally)
                {
                    boss.Spell = SiegeTuning.CastRetry;
                    continue;
                }

                boss.Spell = SiegeTuning.CastEveryFor(boss.Kind);

                float lands = SiegeTuning.BossTell + SiegeTuning.BossFlight;

                _spells.Add(new Flight { Raider = boss.Id, Ward = ward, Craft = craft, In = lands });
                _report.Casts.Add(new SiegeCast(boss.Id, ward, craft, lands));
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
        int Wanted(SiegeSpell craft)
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

                    case SiegeSpell.Sunder:
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

        void Swing(float dt)
        {
            for (int i = 0; i < _raiders.Count; i++)
            {
                var raider = _raiders[i];
                if (!raider.AtTheLine) continue;

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
