using GlimmerGrove.AssetPipeline;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The nudge a field gives a player who has stopped finding matches.
    ///
    /// <para>
    /// <b>Idle only, and that is the whole of what makes it safe.</b> Invariant 20l records a
    /// mode whose helpers were withdrawn after play — a halo, graft links, a breathing flower —
    /// because a board that reads itself out to the player has answered the question it exists to
    /// ask. Nothing here fires while somebody is playing: it takes five seconds of a live board
    /// with nothing happening on it, which on a mode with a walking clock is not a pause for
    /// thought but a player who cannot see a move. What it points at is a match that was already
    /// on the board.
    /// </para>
    /// <para>
    /// <b>It can only ever be generous, so it needs no exchange rate.</b> Invariant 39j's rule:
    /// before pricing a new limit on what a player may do, ask which way it can move a run, and
    /// only the generous direction costs a proof. A hint changes no rule, spends nothing, charges
    /// no match and reaches neither <c>SiegeBoard</c> nor the grade — so every run playable with
    /// it was playable without it, and there is nothing for <c>SiegeTuning.PerfectMatch</c> to
    /// price. It is also not the <em>hint pool</em> (<c>RegenLedger</c>): that is a thing a player
    /// asks for and spends, and this is one they are given for not asking.
    /// </para>
    /// <para>
    /// <b>Nothing about it reaches the save file, and both halves of that are rules.</b> How many
    /// are left is a count that goes <em>down</em>, which invariant 11b refuses a merge outright;
    /// and it is per <em>run</em>, because a nudge surviving a restart would make restarting a
    /// thing the board punished, and one surviving a level would make what a board asks depend on
    /// the board before it (29c's objection to a companion that changed what a move does).
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        /// <summary>
        /// How long a live board has to sit untouched before it points at something.
        ///
        /// <para>
        /// <b>Five seconds of <em>playable</em> board, not five seconds of screen.</b> The clock
        /// only runs while <c>Playable</c> — so a cascade, a lesson, the pause menu and a panel
        /// over the run (<c>RunHold.Covered</c>) all hold it at nought rather than counting toward
        /// it. A nudge that arrived because the player had opened the shop would be the board
        /// telling them off for reading a price.
        /// </para>
        /// <para>
        /// <b>It was four, and four was measured against the wrong thing.</b> The opening quiet is
        /// <c>SiegeTuning.FirstWaveAfter</c> — 3.4 seconds — and the board is <c>Playable</c>
        /// throughout it, because the count-in deliberately does not hold the game up
        /// (<c>SiegeView.CountIn</c>: the clock runs underneath it). So the first nudge landed six
        /// tenths of a second after GO!, on a hill the first raider had barely walked onto, which
        /// reads as the game answering a question nobody had asked. <see cref="Opening"/> is the
        /// fix and this is the margin on top of it.
        /// </para>
        /// </summary>
        const float HintAfter = 5f;

        /// <summary>
        /// How long one nudge stays on the board before it gives up and fades.
        ///
        /// It is deliberately shorter than <see cref="HintAfter"/>: a highlight still up when the
        /// next one is due would read as one permanent mark rather than as three separate offers
        /// of help, and a mark that never leaves is the halo invariant 20l took away.
        /// </summary>
        const float HintHolds = 2.6f;

        /// <summary>
        /// The most nudges one run may be given.
        ///
        /// <b>Per run and never stored.</b> Three is the owner's figure. Spending them all leaves
        /// the board silent for the rest of the level, which is the point: a field that pointed at
        /// a match every five seconds for ever would be a field playing itself.
        /// </summary>
        const int MostHints = 3;

        /// <summary>Seconds of playable board since the player last did anything.</summary>
        float _still;

        /// <summary>How many nudges this run has been given.</summary>
        int _nudges;

        /// <summary>
        /// Where the last scan stopped, so a second nudge points somewhere new.
        ///
        /// A field usually holds several legal swaps and <c>SiegeBoard.FindSwap</c> walks them in
        /// a fixed order, so asking again from the same place would ring the same two gems three
        /// times over — which reads as the board repeating itself rather than as it helping.
        /// </summary>
        int _hintFrom;

        int _hintA = -1, _hintB = -1;
        Image _ringA, _ringB;

        /// <summary>
        /// The player did something. Clears the clock and takes any nudge off the board.
        ///
        /// <b>Called from every door input comes through</b> — a tap on a gem, a drag, and a
        /// utility being armed or spent — rather than from one place, because there is no one
        /// place: this board is dragged on, tapped on and aimed at. The list is short and the
        /// failure is mild (a nudge that outstays its welcome by a beat), which is why this is a
        /// handful of call sites rather than a latch nothing can see.
        /// </summary>
        void Stir()
        {
            _still = 0f;
            Unhint();
        }

        /// <summary>Forgets everything about this run's nudges. Called when a board is dealt.</summary>
        void Unhinted()
        {
            _still = 0f;
            _nudges = 0;
            _hintFrom = 0;
            _hintA = _hintB = -1;
            _ringA = _ringB = null;
        }

        /// <summary>
        /// Counts the quiet, and points at something once it has gone on long enough.
        ///
        /// <b>Called before <c>Update</c>'s own <c>Live</c> gate rather than after it</b>, so a
        /// board nobody may touch resets the clock instead of freezing it. The two are not the
        /// same question: <c>Live</c> is "may the run advance" and <c>Playable</c> is "would a
        /// finger mean anything", and it is the second one this is about — see
        /// <c>SiegeView.Advancing</c> for why this mode is the one where they came apart.
        /// </summary>
        void Idle(float seconds)
        {
            if (!Playable || Opening)
            {
                if (_still > 0f || _hintA >= 0) Stir();
                return;
            }

            _still += seconds;

            // A nudge on the board is counting down its own welcome rather than the next one's
            // wait, which is why the clock is put back to nought when one goes up.
            if (_hintA >= 0)
            {
                if (_still >= HintHolds) Unhint();
                return;
            }

            if (_nudges >= MostHints || _still < HintAfter) return;

            Nudge();
        }

        /// <summary>
        /// The quiet before the first wave musters: the countdown, and the empty hill under it.
        ///
        /// <para>
        /// <b>Derived from the board rather than latched by the count-in, so it cannot strand
        /// the feature.</b> A flag set when the count starts and cleared when it ends is the
        /// obvious shape and every version of it has an exit that clears nothing — a flag left
        /// set means a run whose hints never arrive, which is the silent half of invariant 30g's
        /// rule that anything handing out a callback must be unable to strand its caller.
        /// <c>Wave</c> is nought until the first muster and never nought again, which is the same
        /// fact with nothing to keep in step. <c>SiegeView.CountIn</c> has since been made to read
        /// the board for the same reason, one layer along: it now draws its beats off
        /// <see cref="SiegeBoard.BeforeFirstWave"/>, so the two agree by construction.
        /// </para>
        /// <para>
        /// It holds the whole opening quiet rather than just the four beats drawn over it, which
        /// is right for the same reason: an empty hill is not a board anybody is stuck on.
        /// </para>
        /// </summary>
        bool Opening => _board == null || _board.Wave < 1;

        /// <summary>Rings the two gems of a swap that would line something up.</summary>
        void Nudge()
        {
            if (_board == null || _gems.Count == 0) return;

            var swap = _board.FindSwap(_hintFrom);

            // **A field with no swap on it is not a field to be quiet about, it is one about to
            // be dealt again** — `SiegeBoard.Settle` shuffles what may move rather than letting
            // the board lock, because this mode's clock does not stop. Nothing is spent and the
            // wait starts over, so the nudge lands a moment later on the field that replaced it.
            if (!swap.Found) { _still = 0f; return; }

            _hintFrom = swap.At + 1;
            _hintA = swap.A;
            _hintB = swap.B;
            _nudges++;
            _still = 0f;

            _ringA = Ring(_hintA);
            _ringB = Ring(_hintB);
        }

        /// <summary>
        /// One gem's highlight: a ring on it, and the gem itself breathing.
        ///
        /// <para>
        /// <b>A child of the gem rather than a cell of its own</b>, which is the rule
        /// <c>Lock</c> already keeps about a weaver's web: it travels with whatever the gem does
        /// and there is no second list to keep in step. It also means one breath moves both, so
        /// the ring cannot drift out of phase with the thing it is ringing.
        /// </para>
        /// <para>
        /// <b>Cream rather than a colour</b>, because every colour on this board already means
        /// something — four gem hues, gold for a double, violet for a boss's tell — and a fifth
        /// meaning "look here" would be a fifth thing to learn. Cream is this mode's own neutral.
        /// </para>
        /// </summary>
        Image Ring(int cell)
        {
            if (cell < 0 || cell >= _gems.Count) return null;

            var gem = _gems[cell];
            if (gem == null || gem.Img == null) return null;

            var ring = UIKit.Img("Hint", (RectTransform)gem.Img.transform, Art.Ring(128, 9f),
                                 Pal.A(Pal.Cream, .95f),
                                 new Vector2(Cell * 1.04f, Cell * 1.04f));
            ring.raycastTarget = false;

            Tween.Pop(ring.transform, .55f, .28f);
            Tween.Breathe(gem.Img.transform, .07f, .85f);

            return ring;
        }

        /// <summary>
        /// Takes the nudge off the board.
        ///
        /// <b>The breath is killed by channel rather than by a flag</b>, which is invariant 16k's
        /// own finding: <c>Tween.Breathe</c> restores the scale it borrowed when its channel is
        /// abandoned, and a flag here could not see a <c>KillChannel</c> raised by somebody else.
        /// Anything that leaves a gem holding a size that is not its own is a field that visibly
        /// swells.
        /// </summary>
        void Unhint()
        {
            Rest(_hintA);
            Rest(_hintB);

            if (_ringA != null) Destroy(_ringA.gameObject);
            if (_ringB != null) Destroy(_ringB.gameObject);

            _ringA = _ringB = null;
            _hintA = _hintB = -1;
        }

        void Rest(int cell)
        {
            if (cell < 0 || cell >= _gems.Count) return;

            var gem = _gems[cell];
            if (gem == null || gem.Img == null) return;

            Tween.KillChannel(gem.Img.transform, "breathe");
        }
    }
}
