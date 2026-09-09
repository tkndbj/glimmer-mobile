using System;
using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// The half of the hill that reaches into the <em>field</em>: a weaver locking cells and a
    /// thief taking gems off the board, and the beat where killing one gives it all back.
    ///
    /// <para>
    /// <b>Its own file because it is its own machine.</b> Everything else on the hill costs a ward
    /// health or fuel, so it lives beside the wards; these two cost the player <em>cells</em>, and
    /// the rules about how many, where, and what happens when the raider dies are a coherent set
    /// that nothing else needs to read.
    /// </para>
    /// <para>
    /// <b>Every one of them goes out as a flight and lands later</b>, which is invariant 37s: a
    /// move's effect may not land before its animation does, and on a board whose clock never
    /// stops the only way to guarantee it is a schedule the rules own and the view reads.
    /// </para>
    /// </summary>
    public sealed partial class SiegeBoard
    {
        /// <summary>
        /// Ticks every raider that stops on the hill to work on the field, and books what it
        /// throws.
        ///
        /// <para>
        /// <b>Nothing lands here</b> — see <see cref="Arrive"/>. What this decides is
        /// <em>which cell</em>, and it decides it at the moment of the cast rather than at the
        /// moment of the landing, because the view draws a thread from the raider to that cell for
        /// the whole flight and a target chosen on arrival would be a thread pointing at nothing.
        /// </para>
        /// <para>
        /// <b>A raider at the cap does not cast at all</b>, rather than casting into a refusal.
        /// The difference is what a player sees: a weaver that has spun its six webs visibly
        /// stops, which says the ceiling exists without a word being written about it, where a
        /// throw that fizzles reads as the game dropping something.
        /// </para>
        /// </summary>
        void Meddle(float dt)
        {
            for (int i = 0; i < _raiders.Count; i++)
            {
                var raider = _raiders[i];

                if (!raider.Alive || !SiegeTuning.HoldsTheField(raider.Kind)) continue;
                if (!raider.InPlace) continue;

                raider.Spell -= dt;
                if (raider.Spell > 0f) continue;

                raider.Spell = SiegeTuning.MeddleEveryFor(raider.Kind);

                var craft = raider.Spellcraft;
                if (Standing(craft) >= SiegeTuning.MostOf(craft)) continue;

                int cell = Reach(craft);
                if (cell < 0) continue;

                float lands = SiegeTuning.MeddleTell + SiegeTuning.MeddleFlight;

                _spells.Add(new Flight { Raider = raider.Id, Ward = cell, Craft = craft, In = lands });
                _report.Casts.Add(new SiegeCast(raider.Id, cell, craft, lands));
            }
        }

        /// <summary>How many of this mark are standing on the field.</summary>
        int Standing(SiegeSpell craft)
            => craft == SiegeSpell.Snatch ? SacksStanding() : WebsStanding();

        /// <summary>
        /// Which cell a weaver or a thief reaches for, or -1 when there is nothing to take.
        ///
        /// <para>
        /// <b>Uniform over the cells it could take, and drawn from the hill's stream.</b> Anything
        /// cleverer — the cell in the biggest cluster, the colour the player is short of — is a
        /// rule the player would have to learn in order to play around, and this mechanic's
        /// decision is <em>whether to answer the raider at all</em> rather than which cell it
        /// picked. Uniform also means it cannot be starved into looking broken.
        /// </para>
        /// <para>
        /// <b>A cog is never taken.</b> It is the one thing on the field the player is actively
        /// reaching for, and a thief that could snatch one would be taking back an upgrade that
        /// had already been offered — which is the class of thing invariant 13a calls a refusal
        /// for a reason that will still be true tomorrow, asked of a board.
        /// </para>
        /// </summary>
        int Reach(SiegeSpell craft)
        {
            var open = new List<int>(_cells.Length);

            for (int i = 0; i < _cells.Length; i++)
                if (SiegeLayout.IsGem(_cells[i]) && !_webbed[i]) open.Add(i);

            // Never the last two playable cells: a field with nothing left to swap is a run the
            // player watches themselves lose, which is the one thing this mode may never show.
            if (open.Count <= 2) return -1;

            for (int attempt = 0; attempt < 8; attempt++)
            {
                int cell = open[(int)(Tick() % (uint)open.Count)];
                if (Survivable(cell, craft)) return cell;
            }

            return -1;
        }

        /// <summary>
        /// Whether marking this cell would leave a field that can still be played.
        ///
        /// <b>Asked by trying it</b>, because "would there still be a legal swap" has no cheaper
        /// honest answer and the board is forty cells. The alternative — mark it and re-deal if
        /// the field locks — is worse than it looks: <see cref="Settle"/> keeps the marks where
        /// they are, so a field that locks because of them cannot be shuffled out of it.
        /// </summary>
        bool Survivable(int cell, SiegeSpell craft)
        {
            char keep = _cells[cell];
            bool web = _webbed[cell];

            Mark(cell, craft, true);
            bool alive = AnySwap();

            _cells[cell] = keep;
            _webbed[cell] = web;

            return alive;
        }

        /// <summary>
        /// Puts a mark on a cell or takes it off, and answers the colour now standing there.
        ///
        /// <b>One place, so a web and a sack cannot come to disagree about what "off" means.</b>
        /// Taking a web off leaves the gem it was on; taking a sack off deals a fresh gem, because
        /// the gem it took is gone and a sack carries no memory of it.
        /// </summary>
        int Mark(int cell, SiegeSpell craft, bool on)
        {
            if (craft == SiegeSpell.Snatch)
            {
                if (on) { _cells[cell] = SiegeLayout.Sack; return -1; }

                _cells[cell] = Deal();
                _webbed[cell] = false;
                return SiegeLayout.Letters.IndexOf(_cells[cell]);
            }

            _webbed[cell] = on;
            return SiegeLayout.Letters.IndexOf(_cells[cell]);
        }

        /// <summary>
        /// Lands a web or a theft on the field.
        ///
        /// <para>
        /// <b>The cap and the playability are asked again here rather than trusted from the
        /// cast</b>, because the better part of a second goes by in between and the player has
        /// been swapping throughout: the cell that was safe to lock may since have fallen, been
        /// cleared, or become the last thing on the board worth touching. A throw that arrives at
        /// a cell it may no longer take is <em>dropped</em>, and that is the same bargain
        /// <see cref="Arrive"/> already keeps for a spell aimed at a ward that has since fallen.
        /// </para>
        /// </summary>
        void Landed(Flight spell)
        {
            int cell = spell.Ward;
            if (cell < 0 || cell >= _cells.Length) return;

            if (Standing(spell.Craft) >= SiegeTuning.MostOf(spell.Craft)) return;
            if (!SiegeLayout.IsGem(_cells[cell]) || _webbed[cell]) return;
            if (!Survivable(cell, spell.Craft)) return;

            int colour = Mark(cell, spell.Craft, true);
            _report.Meddles.Add(new SiegeMeddle(spell.Raider, cell, spell.Craft, true, colour));
        }

        /// <summary>
        /// Gives the field back everything a raider had taken from it.
        ///
        /// <para>
        /// <b>Called from one place, and that is the whole reason it is safe.</b> A raider dies in
        /// four ways — a bolt, a firepot, a storm, and a beam that overkills it — and an undo hung
        /// off any one of them is an undo the other three do not do. <see cref="Fell"/> is the one
        /// door.
        /// </para>
        /// <para>
        /// <b>It is the payoff, not the tidy-up</b> (invariant 20m: the event is the reward, so it
        /// gets the biggest drawing). Every sack bursts back into a gem in the same beat, so what
        /// a player sees when a thief goes down is the board they were promised, all at once.
        /// </para>
        /// <para>
        /// <b>Only when the last one of its kind is gone.</b> Two weavers on one hill are twice
        /// the pressure and one answer; freeing the webs on the first death would make the second
        /// weaver worth nothing at all, and a mechanic that rejects no play is decoration
        /// (invariant 5d).
        /// </para>
        /// </summary>
        void Unmeddle(SiegeKind kind)
        {
            var craft = SiegeTuning.SpellOf(kind);

            for (int i = 0; i < _raiders.Count; i++)
            {
                var other = _raiders[i];
                if (other.Alive && other.Kind == kind) return;
            }

            // Anything still in the air from it never lands: a web arriving after the thing that
            // threw it is dead is the game getting the last word (Arrive's own rule for a spell
            // whose caster has fallen, said about the field).
            for (int i = _spells.Count - 1; i >= 0; i--)
                if (_spells[i].Craft == craft) _spells.RemoveAt(i);

            for (int cell = 0; cell < _cells.Length; cell++)
            {
                bool held = craft == SiegeSpell.Snatch
                          ? _cells[cell] == SiegeLayout.Sack
                          : _webbed[cell];

                if (!held) continue;

                int colour = Mark(cell, craft, false);
                _report.Meddles.Add(new SiegeMeddle(-1, cell, craft, false, colour));
            }

            // A board handed back six cells at once can land three alike together, which would go
            // off with nobody having touched it. Settling is what every other refill already does.
            Settle();
        }

        /// <summary>
        /// Takes a raider off the hill: the one door, so nothing can be counted twice and nothing
        /// a raider was holding can be forgotten.
        ///
        /// <para>
        /// It answers whether this was the blow that killed it, so the four callers can report a
        /// kill without each keeping their own copy of the rule.
        /// </para>
        /// </summary>
        bool Fell(SiegeRaider raider)
        {
            if (raider == null || !raider.Alive || raider.Health > 0) return false;

            raider.Alive = false;
            _felled++;

            if (SiegeTuning.HoldsTheField(raider.Kind)) Unmeddle(raider.Kind);

            return true;
        }

        /// <summary>
        /// xorshift32 over the hill's own stream. See <see cref="_hill"/> for why it is not
        /// <see cref="Next"/>.
        /// </summary>
        uint Tick()
        {
            uint x = _hill;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _hill = x == 0u ? 2463534242u : x;
            return _hill;
        }
    }
}
