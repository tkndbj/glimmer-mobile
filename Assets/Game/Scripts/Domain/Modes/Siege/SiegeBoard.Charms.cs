using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// What a charm does when the gem carrying it goes: the lance's cross, and the stormglass's
    /// volley over the hill.
    ///
    /// <para>
    /// <b>The prism is deliberately absent from this file.</b> It is not a thing that happens when
    /// a gem clears — it is a fact about what lines up at all, so it lives in
    /// <see cref="SiegeLayout.Runs"/> and in one predicate, and the whole of what it costs the
    /// board is that a cleared cell is paid as the colour of the run it joined rather than as its
    /// own letter.
    /// </para>
    /// <para>
    /// <b>Its own file rather than a region of the field</b>, for the reason
    /// <c>SiegeBoard.Field</c> gives about the three machines that share this state: a charm
    /// reaches the field <em>and</em> the hill, and the next one will reach somewhere else again.
    /// </para>
    /// </summary>
    public sealed partial class SiegeBoard
    {
        /// <summary>
        /// A stormglass that has gone off and whose bolts have not landed yet.
        ///
        /// <para>
        /// <b>Booked exactly as a match's fuel is, and for the same reason.</b> Invariant 37s: a
        /// move's effect may not land before its animation does. The model resolves a whole swap in
        /// an instant and the view spends the best part of a second drawing it, so a stormglass
        /// applied where it is sprung would kill raiders before the gem it came out of had burst —
        /// which is precisely the fault that turn-based modes are immune to by construction and
        /// this one is not.
        /// </para>
        /// <para>
        /// <b>It carries its colour and not its cell.</b> The cell it came from is gone by the time
        /// this lands — the field has already collapsed and refilled — so a reader handed a cell
        /// would be pointing at whatever fell into it.
        /// </para>
        /// </summary>
        struct Tempest
        {
            public int Colour;
            public float In;
        }

        readonly List<Tempest> _storms = new List<Tempest>(2);

        /// <summary>
        /// A furnace or an hourglass that has gone off and has not landed yet.
        ///
        /// <b>One list for the two, and <see cref="Tempest"/> is not folded into it on purpose.</b>
        /// A stormglass carries a colour and is resolved by <see cref="Volley"/>; these two carry
        /// the charm they were, because what happens when they land is decided by the kind - a
        /// furnace reaches a ward and an hourglass reaches the whole hill - and a third list per
        /// charm would be the shape <see cref="SiegeCharms.Roster"/> exists to avoid.
        /// </summary>
        struct Booked
        {
            public SiegeCharm Charm;
            public int Colour;
            public float In;
        }

        readonly List<Booked> _booked = new List<Booked>(2);

        /// <summary>What this cell is carrying, or <see cref="SiegeCharm.None"/>.</summary>
        public SiegeCharm CharmAt(int index)
            => index >= 0 && index < _charms.Length ? _charms[index] : SiegeCharm.None;

        /// <summary>
        /// Stands a charm on a cell. <b>Internal, and there is exactly one caller: the fixture.</b>
        ///
        /// <para>
        /// <b>A seam rather than a rule, and it is named for what it is.</b> Nothing in the game
        /// puts a charm anywhere — they are dealt, one a window
        /// (<see cref="SiegeTuning.CharmWithin"/>) — so a fixture proving what a lance does
        /// would otherwise have to deal a few hundred gems and hope, which is a test that measures
        /// the roll rather than the rule and fails on the day the rate is retuned. It is
        /// <c>internal</c> for the same reason <see cref="SiegeLayout.Runs"/> is: the tests can
        /// see it, the game cannot, and <c>compile.py</c> proves the game does not reference it by
        /// building the player assemblies without the fixture.
        /// </para>
        /// </summary>
        /// <summary>
        /// Shuffles the field the way <c>Settle</c> does. <b>Internal, and the fixture is the only
        /// caller</b> — see <see cref="Stand"/> for why a seam like this is named for what it is.
        ///
        /// It exists because the roll that deals a charm has to be asked its question the way a
        /// <em>run</em> asks it: a real board draws from the stream for its shuffles as well as for
        /// its gems, so what reaches the roll is a subsequence — and a roll reading bits that are
        /// not independent of the letter draw passes a clean walk of the stream and fails a board.
        /// </summary>
        internal void Reshuffle()
        {
            for (int i = _cells.Length - 1; i > 0; i--)
                Trade(i, (int)(Next() % (uint)(i + 1)));
        }

        internal void Stand(int cell, SiegeCharm charm)
        {
            if (cell < 0 || cell >= _charms.Length) return;
            _charms[cell] = charm;
        }

        /// <summary>Whether any charm is standing on the field right now.</summary>
        public bool AnyCharm
        {
            get
            {
                for (int i = 0; i < _charms.Length; i++)
                    if (_charms[i] != SiegeCharm.None) return true;

                return false;
            }
        }

        /// <summary>
        /// Sets off every charm this beat took, and folds what they add into the same beat.
        ///
        /// <para>
        /// <b>A queue rather than a second pass, because a lance can take a lance.</b> The cells a
        /// cross adds are cells like any other, so one of them may itself be carrying something —
        /// and a fixed two passes would have been a chain of exactly two, which is a rule nobody
        /// wrote down and a player would find in about a minute. It terminates for the plainest
        /// possible reason: a cell is queued only on the step that adds it to
        /// <paramref name="hit"/>, and a set never adds the same cell twice.
        /// </para>
        /// <para>
        /// <b>Everything is recorded on the beat and nothing is applied to the field here.</b> The
        /// caller clears the cells; this only decides which ones. That split is what keeps the
        /// fuel arithmetic in one loop in one place, rather than half of it here paying for cells
        /// a cross took.
        /// </para>
        /// </summary>
        void Spring(HashSet<int> hit, SiegeBeat beat)
        {
            // The overwhelmingly ordinary case: no charm anywhere near this beat. Asked before
            // anything is allocated, because this runs on every beat of every cascade of every
            // swap in the mode.
            if (!AnyCharm) return;

            var queue = new List<int>(4);

            foreach (int cell in hit)
                if (_charms[cell] != SiegeCharm.None) queue.Add(cell);

            if (queue.Count == 0) return;

            // Sorted for the reason `beat.Cleared` is: a `HashSet<int>` is not promised to
            // enumerate the same way on two runtimes, and the order charms go off in decides the
            // order the view draws them and — through the queue — which cells a chain reaches
            // first.
            queue.Sort();

            for (int at = 0; at < queue.Count; at++)
            {
                int cell = queue[at];
                var charm = _charms[cell];

                // Taken off the cell as it is sprung, so a cross that runs back over its own
                // origin cannot set it off twice.
                _charms[cell] = SiegeCharm.None;

                int colour = SiegeLayout.Letters.IndexOf(
                    _paid[cell] != '\0' ? _paid[cell] : _cells[cell]);

                beat.Sprung.Add(new SiegeSpark(cell, charm, colour));
                Attention.CharmSprung(charm, cell);

                switch (charm)
                {
                    case SiegeCharm.Lance:
                        Cross(cell, colour, hit, queue);
                        break;

                    case SiegeCharm.Storm:
                        _storms.Add(new Tempest
                        {
                            Colour = colour,
                            In = SiegeTuning.FuelLands(beat.Depth - 1),
                        });
                        break;

                    // **Both booked to land with the match's own fuel** (invariant 37s), for the
                    // stormglass's reason: the model resolves a swap in an instant and the view
                    // spends most of a second drawing it, so a charge banked or a hill stopped on
                    // the frame of the swap would be a payoff arriving before the gem that paid
                    // for it had burst.
                    case SiegeCharm.Furnace:
                    case SiegeCharm.Hourglass:
                    case SiegeCharm.Anvil:
                        _booked.Add(new Booked
                        {
                            Charm = charm,
                            Colour = colour,
                            In = SiegeTuning.FuelLands(beat.Depth - 1),
                        });
                        break;

                    // **A prism has nothing to do here and says so out loud.** It is answered in
                    // `SiegeLayout.Runs`, and a `default` that silently did nothing would be the
                    // shape invariant 44e is about — the next charm added would fall through it
                    // and ship as a picture with no rule behind it.
                    case SiegeCharm.Prism:
                    case SiegeCharm.None:
                        break;
                }
            }
        }

        /// <summary>
        /// A lance: its whole row and its whole column, added to what this beat takes.
        ///
        /// <b>Gems only, and holes are skipped rather than counted.</b> Mid-cascade a column can
        /// hold cells that have already gone, and a cross that "cleared" one of them would pay
        /// fuel for a gem that was not there.
        /// </summary>
        void Cross(int cell, int colour, HashSet<int> hit, List<int> queue)
        {
            int cx = cell % Width, cy = cell / Width;

            for (int x = 0; x < Width; x++) Reach(cy * Width + x);
            for (int y = 0; y < Height; y++) Reach(y * Width + cx);

            void Reach(int at)
            {
                if (!SiegeLayout.IsGem(_cells[at])) return;
                if (!hit.Add(at)) return;

                // **A wild a cross takes is paid as the lance's colour, and it is the one cell on
                // this field with no honest answer of its own.** A prism joins a run and is paid as
                // the colour it joined; one swept up by a beam joined nothing, and the letter it is
                // carrying underneath is what the deal happened to hand it — a colour the player
                // cannot see and did not choose. The beam is drawn in the lance's colour, so
                // paying it that is the only answer the board agrees with.
                if (colour >= 0 && _paid[at] == ' ' && SiegeCharms.IsWild(_charms[at]))
                    _paid[at] = SiegeLayout.Letters[colour];

                if (_charms[at] != SiegeCharm.None) queue.Add(at);
            }
        }

        /// <summary>
        /// Lands whatever stormglass has finished crossing the field: <b>every standing ward
        /// throws at every raider on the hill at once</b>, and the ward wearing the charm's own
        /// colour throws twice.
        ///
        /// <para>
        /// <b>It is fired by the line and never by the gem, and that is a rule about the shelf
        /// rather than about the charm.</b> The first version put a flat figure into every raider,
        /// which is the obvious shape and is quietly corrosive: damage that does not scale with
        /// what a player has bought flattens the one ladder in this mode anybody pays for
        /// (invariant 42). Measured, it did exactly that — a chapter authored to need a bought line
        /// came within a few runs of holding on the starter. Thrown by the wards, a stormglass is
        /// worth more to a player who has spent their credits, more to a player who has taken their
        /// cogs, and less to one whose line is half down. Every one of those is a decision it now
        /// pays off rather than papers over.
        /// </para>
        /// <para>
        /// <b>It is also the one moment the colour lock is lifted, which is what makes it the
        /// mode's biggest picture.</b> A ward only ever shoots its own colour (invariant 37bl); for
        /// this volley the whole line reaches everything, at <see cref="SiegeTuning.OffColourTenths"/>
        /// for a colour that is not its own — the same share a boss is answered at, so nothing new
        /// is being invented — and at full weight for one that is. The charm's own colour is what
        /// decides which ward fires twice, so the gem's colour is a decision rather than a picture.
        /// </para>
        /// <para>
        /// <b>It costs no fuel.</b> A charm is free and arrives on its own schedule; taking fuel
        /// for it would be the player's own matches spent on their behalf, which is precisely the
        /// fault a part-weight bolt was given a part-weight price to avoid (invariant 37bq).
        /// </para>
        /// <para>
        /// <b>A shield is read, because these are ward bolts.</b> A bulwark halves everything the
        /// line throws sideways at it and takes its own colour in full — that is what the armour
        /// <em>is</em>, and a storm that ignored it would make the one charm answering the whole
        /// hill the one charm a hill of bulwarks may as well not carry.
        /// </para>
        /// </summary>
        void Break(float dt)
        {
            for (int i = _storms.Count - 1; i >= 0; i--)
            {
                var storm = _storms[i];
                storm.In -= dt;

                if (storm.In > 0f)
                {
                    _storms[i] = storm;
                    continue;
                }

                _storms.RemoveAt(i);
                Volley(storm.Colour);
            }

            for (int i = _booked.Count - 1; i >= 0; i--)
            {
                var booked = _booked[i];
                booked.In -= dt;

                if (booked.In > 0f)
                {
                    _booked[i] = booked;
                    continue;
                }

                _booked.RemoveAt(i);

                if (SiegeCharms.ReachesTheLine(booked.Charm)) Forge(booked.Colour);
                else if (SiegeCharms.StopsTheHill(booked.Charm)) Still();
                else if (SiegeCharms.ShovesTheHill(booked.Charm)) Heave();
            }
        }

        /// <summary>
        /// One furnace landing: the ward of its colour banks a whole charge, or refuses it.
        ///
        /// <para>
        /// <b>Through the same door a full tube goes through and no other.</b> A charge is a
        /// number on the ward (<see cref="SiegeWard.Charges"/>) bounded by
        /// <see cref="SiegeTuning.MostCharges"/>, and a furnace writes exactly what
        /// <see cref="SiegeWard.Fill"/> writes when a tube brims: one more, and never past the
        /// cap. So what a furnace is worth is what a charge is worth - that ward's capacity at
        /// that ward's weight, thrown when the player taps it - which is why it scales with the
        /// shelf and conjures no damage of its own (invariant 39's arithmetic, unchanged).
        /// </para>
        /// <para>
        /// <b>A fallen ward and a full one both refuse it</b>, and the refusal is reported rather
        /// than swallowed: the decision a furnace asks is which colour, and the wrong answer has
        /// to be visible (invariant 26h). Reported on <see cref="SiegeReport.Brimmed"/> as well
        /// when it banks, because that is the edge the overcharge lesson and the glyph's own
        /// announcement already listen on - a charge is a charge whatever poured it.
        /// </para>
        /// </summary>
        void Forge(int colour)
        {
            int at = -1;
            for (int w = 0; w < _wards.Length; w++)
                if (_wards[w].Colour == colour) { at = w; break; }

            if (at < 0) return;

            var ward = _wards[at];
            bool banked = ward.Alive && ward.Charges < SiegeTuning.MostCharges;

            if (banked)
            {
                ward.Charges += SiegeTuning.FurnaceCharges;
                if (ward.Charges > SiegeTuning.MostCharges) ward.Charges = SiegeTuning.MostCharges;
                _report.Brimmed.Add(at);
            }

            _report.Forged.Add(new SiegeForged(at, banked));
        }

        /// <summary>
        /// One hourglass landing: the hill stands still for <see cref="SiegeTuning.HourglassFor"/>.
        ///
        /// <b>Extended rather than stacked</b>, which is the roar's rule (<c>SiegeBoard.Walk</c>)
        /// read the other way: two hourglasses a beat apart are one stop lasting as long as the
        /// later one says, not a stop twice as long - a cascade that sprang two would otherwise
        /// buy six seconds nothing was tuned against.
        /// </summary>
        void Still()
        {
            if (SiegeTuning.HourglassFor > _still) _still = SiegeTuning.HourglassFor;
            _report.Stilled = SiegeTuning.HourglassFor;
        }

        /// <summary>
        /// One anvil landing: everything walking is driven back up the slope by
        /// <see cref="SiegeTuning.AnvilHeave"/> of the hill.
        ///
        /// <para>
        /// <b>The debt is written on the bodies and worked off by the clock</b>
        /// (<see cref="SiegeRaider.Shove"/>, <c>SiegeBoard.Walk</c>), rather than the March being
        /// rewritten here. A shove applied in one frame is a hill that teleports, because the
        /// view draws a raider wherever the model says it is; a shove that is a debt is the same
        /// arithmetic drawn over a quarter of a second, and the hold simulation walks through it
        /// exactly as a player does.
        /// </para>
        /// <para>
        /// <b>A boss is refused by the raider rather than skipped here</b>, so the one rule that
        /// matters is written once (invariant 37di: a boss's whole fight is measured from where
        /// it stands). What this counts is how many bodies it actually moved, because that is
        /// what decides whether the view draws a shove or a refusal - a payoff that silently did
        /// nothing would be a broken gem rather than a wrong choice, which is the furnace's own
        /// rule about a charge that cannot be banked.
        /// </para>
        /// </summary>
        void Heave()
        {
            int shoved = 0;

            for (int i = 0; i < _raiders.Count; i++)
                if (_raiders[i].Shove(SiegeTuning.AnvilHeave)) shoved++;

            _report.Heaved = SiegeTuning.AnvilHeave;
            _report.Shoved = shoved;
        }

        /// <summary>One stormglass going off: the whole line, at everything on the hill.</summary>
        void Volley(int colour)
        {
            for (int w = 0; w < _wards.Length; w++)
            {
                var ward = _wards[w];

                // **A fallen ward throws nothing, and a doused one does.** Losing a ward is what
                // this mode's fail state is made of, so it has to cost here too; a douse is a few
                // seconds of one turret's fire (`SiegeSpell.Douse`) and a stormglass is not fire,
                // it is the field handing the line one free volley.
                if (!ward.Alive) continue;

                // The charm's own colour fires twice. Stated here rather than folded into the
                // damage, because two bolts and one double bolt are the same arithmetic and very
                // different pictures — and what the player has to read is *that ward answered*.
                int bolts = ward.Colour == colour
                          ? SiegeTuning.CharmVolleyOwn
                          : SiegeTuning.CharmVolley;

                for (int shot = 0; shot < bolts; shot++)
                    for (int r = 0; r < _raiders.Count; r++)
                    {
                        var raider = _raiders[r];
                        if (!raider.Alive || raider.Wait > 0f) continue;

                        bool weak = ward.Doubles(raider);

                        int damage = SiegeTuning.DamageTo(raider.Kind, ward.Rank, true, ward.Build);
                        if (!weak)
                        {
                            damage = damage * SiegeTuning.OffColourTenths / 10;
                            if (damage < 1) damage = 1;
                        }

                        // Through the one door (`SiegeBoard.Fight.cs`): a volley over a guarded
                        // boss lands on everything else, and a bolt that took nothing is not
                        // drawn as a hit.
                        damage = Wound(raider, damage);
                        if (damage <= 0) continue;

                        bool killed = Fell(raider);

                        _report.Charmed.Add(new SiegeBolt(w, raider.Id, damage, weak, killed));
                    }
            }
        }
    }
}
