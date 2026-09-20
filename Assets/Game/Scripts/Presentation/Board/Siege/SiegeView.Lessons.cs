using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Modes;
using GlimmerGrove.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>The one-off lessons this mode shows, and what each of them points at.</summary>
    public sealed partial class SiegeView
    {
        /// <summary>
        /// Whether something outside this board is already pointing at it.
        ///
        /// <para>
        /// <b>It stands the idle nudge down, and it exists for exactly one caller.</b> The nudge
        /// (<c>SiegeView.Hint</c>) rings a swap after five seconds of untouched board, which is
        /// the right answer on a rung and the wrong one under a tutorial: the tutorial is already
        /// ringing a pair and holding a hand over it, so the nudge would put a second highlight
        /// on the same two gems and then take it away again after 2.6 seconds, which reads as the
        /// board changing its mind.
        /// </para>
        /// <para>
        /// <b>A latch rather than a check on who is showing the board</b>, because the board must
        /// not learn about screens. It is false everywhere except <c>TutorialScreen</c>, so every
        /// shipped rung nudges exactly as it did.
        /// </para>
        /// </summary>
        public bool Coached { get; set; }

        /// <summary>
        /// The gem standing in <paramref name="cell"/>, for a scripted lesson to ring or trace
        /// between, or null when the field is not drawn.
        ///
        /// <para>
        /// <b>The gem itself rather than an anchor over it.</b> <c>ProtoView</c> mints empty
        /// anchors for its two declared lessons because those are cells a mode <em>names</em>;
        /// this is a cell a script <em>chose</em>, and the thing it wants ringed is the object the
        /// player is about to drag. A real widget also cannot drift from what is drawn there, and
        /// it is never cached, so a board dealt again hands back the new gem rather than a
        /// destroyed one.
        /// </para>
        /// <para>
        /// Asked at the moment a panel goes up rather than remembered, which is
        /// <see cref="ArmedWard"/>'s rule and for the same reason: a gem mid-cascade is a gem in
        /// the air.
        /// </para>
        /// </summary>
        public RectTransform GemAt(int cell)
        {
            if (cell < 0 || cell >= _gems.Count) return null;

            var gem = _gems[cell];
            return gem == null || gem.Img == null ? null : gem.Img.rectTransform;
        }


        // ------------------------------------------------------------------ the lessons
        /// <summary>The gem a lesson about the verb rings: one whose ward is on the line.</summary>
        public override int VerbCell
        {
            get
            {
                if (_board == null) return 0;

                for (int i = 0; i < Width * Height; i++)
                    if (_layout.WardOf(_board.At(i)) == 0) return i;

                return 0;
            }
        }

        /// <summary>
        /// The lesson about the line points at the field's own top row, which is as close as a
        /// cell anchor can get to the wards standing above it — <c>ProtoView</c>'s anchors are
        /// cells, and a tip pointing at nothing is worse than no tip.
        /// </summary>
        public override int FriendCell => Width / 2;

        /// <summary>
        /// The cog's own picture, for the widget one is drawn as.
        ///
        /// <para>
        /// <b>It was public, and was a lesson's <c>Icon</c>.</b> Pointing at a cog was held to be
        /// unreliable while one was dealt into the gem field at a rate — a rung could open with
        /// none standing, and a lesson is offered once in a player's life — so the ring went on a
        /// turret and the panel <em>drew</em> the cog instead. A cog is dropped by a felled raider
        /// now, so the ring goes on the cog (<see cref="LiveCog"/>) and there is nothing left for a
        /// picture in a panel to say.
        /// </para>
        /// </summary>
        Sprite CogArt => Piece("gem_cog");

        /// <summary>
        /// The live bomb a lesson rings, or null when none is standing.
        ///
        /// <para>
        /// <b>The bomb, not the bomber, and that is the whole of what was wrong.</b> This lesson
        /// used to be raised when a bomber walked on and ringed the raider — so the one sentence
        /// it exists to say, <em>tap this</em>, arrived while the thing to tap did not exist, and
        /// the panel was long gone by the time one landed. Reported from a device exactly that
        /// way. It is raised by <see cref="Bombed"/> now, on the drop.
        /// </para>
        /// <para>
        /// <b>Asked at the moment the tip goes up rather than remembered</b>, because the two are
        /// a beat apart — a lesson is resolved through <c>Lessons</c> every time one is offered,
        /// so a null here is a tip that teaches without pointing rather than a ring drawn round
        /// bare hill. The run is held while a tip is up (<c>RunHold.Teaching</c>) and a bomb only
        /// leaves when it is tapped, so in practice the one that raised this is still there.
        /// </para>
        /// <para>
        /// <b>The newest, and not the one nearest the line.</b> The lesson it serves says a
        /// bomber left this when it died, so what it should ring is the thing the player's last
        /// kill just handed them; ids are minted in order, so the largest is the one that
        /// arrived. Asked of the board for what exists and of the view for whether it is drawn,
        /// so a bomb mid-teardown is never ringed.
        /// </para>
        /// </summary>
        public RectTransform LiveBomb()
        {
            if (_board == null) return null;

            RectTransform found = null;
            int newest = -1;

            var bombs = _board.Bombs;

            for (int i = 0; i < bombs.Count; i++)
            {
                var bomb = bombs[i];
                if (bomb.Id <= newest) continue;

                var fuse = FuseOf(bomb.Id);
                if (fuse == null || fuse.Node == null) continue;

                newest = bomb.Id;
                found = fuse.Node;
            }

            return found;
        }

        /// <summary>
        /// The live cog a lesson rings, or null when none is lying on the hill.
        ///
        /// <para>
        /// <b>The cog itself, where this lesson used to ring the middle of the ward line.</b> That
        /// anchor was the best that could be said while a cog was dealt into the gem field and a
        /// rung might open with none standing — the lesson had to point at the thing a cog is
        /// <em>for</em>, because the cog itself might not exist. A cog is dropped by a felled
        /// raider now (invariant 37bl), so <see cref="Salvaged"/> raises this at the moment one
        /// lands and there is a real object to ring.
        /// </para>
        /// <para>
        /// <b>The newest, and resolved when the tip goes up rather than remembered</b> — see
        /// <see cref="LiveBomb"/>, whose two rules these are. A cog also runs out on its own clock,
        /// which is the one way this differs from a bomb: it can be trampled while the panel is
        /// still opening, and a null here is a tip that teaches without pointing rather than a ring
        /// drawn round bare hill.
        /// </para>
        /// </summary>
        public RectTransform LiveCog()
        {
            if (_board == null) return null;

            RectTransform found = null;
            int newest = -1;

            var cogs = _board.Cogs;

            for (int i = 0; i < cogs.Count; i++)
            {
                var cog = cogs[i];
                if (cog.Id <= newest) continue;

                var gear = GearOf(cog.Id);
                if (gear == null || gear.Node == null) continue;

                newest = cog.Id;
                found = gear.Node;
            }

            return found;
        }

        /// <summary>
        /// A turret holding an overcharge, for a lesson to ring, or null when none is armed.
        ///
        /// <para>
        /// <b>The turret rather than the glyph on it.</b> What the lesson has to say is <em>this
        /// one is full</em>, and the chassis key is a small bright thing sitting inside the ring
        /// that already pulses on its own — ringing the key alone would point at a control without
        /// saying which turret it belongs to, on a line of four that differ only in colour.
        /// </para>
        /// <para>
        /// <b>The real post, not a node made to stand in for one.</b> This used to be
        /// <c>WardAnchor</c>: a rectangle built at the middle of the line, the same size and in the
        /// same place as the post it was covering, because the thing it wanted to ring might not
        /// exist. Both lessons that needed that now have something real to point at, so the stand-in
        /// is gone rather than kept for a caller that no longer wants it.
        /// </para>
        /// <para>
        /// <b>The first armed one, and asked when the tip goes up.</b> Which of four is arbitrary —
        /// the sentence is about the rule and not about that turret — so a fixed order is worth
        /// more than a cleverer choice, and a charge can be spent or a ward can fall between the
        /// hook firing and the panel opening.
        /// </para>
        /// </summary>
        public RectTransform ArmedWard()
        {
            if (_board == null || _posts == null) return null;

            var wards = _board.Wards;

            // **The one the player could actually spend right now**, which is the board's answer
            // and not the tube's (`SiegeBoard.CanOvercharge`): a lesson that rings a turret whose
            // tap would be refused teaches the refusal, which is the opposite of what a lesson is
            // for. `Ready` draws the same reading, so the ring lands on a control that is lit.
            for (int i = 0; i < _posts.Length && i < wards.Count; i++)
            {
                if (!_board.CanOvercharge(i)) continue;

                var post = _posts[i];
                if (post != null && post.Node != null) return post.Node;
            }

            return null;
        }
    }
}
