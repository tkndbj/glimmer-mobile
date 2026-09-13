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
    /// <summary>
    /// Where everything is: the three bands, a lane's x, a post's x, and how far down the hill a
    /// march reading sits.
    /// </summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ the three bands
        /// <summary>
        /// How the board's height divides between the hill, the ward line and the field.
        ///
        /// <para>
        /// <b>A pure function, so the one thing a picture cannot answer can be pinned.</b> A
        /// render says whether the hill reads and whether a fuel tube has fallen behind the
        /// field's plate, and it says it one screen shape at a time — which is how the line band
        /// came to be able to collapse on a 4:3 while every phone was fine. The shares are
        /// arithmetic over three numbers, so they can be swept instead.
        /// </para>
        /// </summary>
        public readonly struct Bands
        {
            /// <summary>Each band's share of the board's height. They sum to one.</summary>
            public readonly float Gems, Hill, Line;

            /// <summary>
            /// The three lines those shares put on the board, in the field's own coordinates:
            /// the top of the hill, its foot, and where a turret stands.
            ///
            /// <para>
            /// <b>Here rather than in <c>Compose</c>, so that anything measured against them can
            /// be swept.</b> They were three statements inside the build, which is where the
            /// shares used to be too — and the reason those moved is the reason these follow:
            /// a render draws one screen shape at a time, so nothing could see a band collapse
            /// on a 4:3 or two captions sharing a row on all of them. A number a fixture cannot
            /// reach is a number only a picture can check.
            /// </para>
            /// </summary>
            public readonly float HillTop, HillFoot, LineY;

            Bands(float gems, float hill, float line,
                  float hillTop, float hillFoot, float lineY)
            {
                Gems = gems;
                Hill = hill;
                Line = line;
                HillTop = hillTop;
                HillFoot = hillFoot;
                LineY = lineY;
            }

            /// <summary>
            /// The bands for a board <paramref name="span"/> tall drawing
            /// <paramref name="rows"/> rows of <paramref name="cell"/>.
            ///
            /// <para>
            /// <b>The field's height is a fact and the other two divide what is left.</b> The
            /// field is laid out to the <em>width</em> (see <see cref="Fit"/>), so how much
            /// height it needs is decided before this is asked; <c>HillBand</c> and
            /// <c>LineBand</c> are a ratio over the remainder rather than two more shares, which
            /// is what lets one of them move without the other having to.
            /// </para>
            /// <para>
            /// <b>And the line has a floor measured in cells</b>, because what stands on it is
            /// measured in cells: a plinth, a fuel tube, a health bar and a rank badge. A band
            /// expressed only as a share gets squeezed under them on a short display, and what
            /// that looks like is the tube drawn across the turret's own chassis (invariant 37y)
            /// and the plinth behind the field's plate (37g).
            /// </para>
            /// </summary>
            public static Bands Of(float span, float cell, int rows)
            {
                if (span <= 0f) return new Bands(0f, 0f, 0f, 0f, 0f, 0f);

                float gems = Mathf.Clamp(cell * rows / span, .28f, MaxGemBand);
                float rest = 1f - gems;

                float hill = rest * (HillBand / (HillBand + LineBand));
                float line = rest - hill;

                float floor = Mathf.Min(rest, LineFloor * cell / span);
                if (line < floor)
                {
                    line = floor;
                    hill = rest - line;
                }

                // The wards stand *high* on the line, so their heads break into the grass rather
                // than tucking under the field's plate. A render is why: at the middle of the
                // band they were half-hidden behind the plate and read as small.
                float lineY = span * (.5f - hill) - span * line * WardStand;

                return new Bands(gems, hill, line,
                                 span * .5f - cell * .35f, span * (.5f - hill), lineY);
            }
        }

        /// <summary>How far down its own band a turret stands. See <see cref="Bands.LineY"/>.</summary>
        const float WardStand = .30f;

        // ------------------------------------------------------------------ the hill's captions
        /// <summary>
        /// Where the hill's two announcements sit: a cascade, and a wave arriving.
        ///
        /// <para>
        /// <b>A ladder rather than two placements, because whether two things on a screen overlap
        /// is arithmetic</b> — and arithmetic inside a <c>MonoBehaviour</c> is arithmetic nothing
        /// can check (invariant 8a, and <c>ProductCardBadges</c> for the same fault on a shop
        /// card). These two were placed independently and against <em>different anchors</em>: the
        /// chain against the ward line and the wave banner against the hill's foot, which sit
        /// between .45 and .71 of a cell apart depending on the display. Each number was
        /// reasonable and the pair was wrong on every shape — measured, the wave banner floated
        /// up through the chain banner and shared about 1.4 cells with it on a 19.5:9 phone, a
        /// 16:9 sheet and a tablet alike, which is what came back from play as "CHAIN x2" and
        /// "WAVE 1 OF 3" drawn on top of each other.
        /// </para>
        /// <para>
        /// <b>Both are anchored to the ward line</b>, so the air between them is a fixed number
        /// of cells rather than something a band ratio can close. The chain keeps the place it
        /// was given (invariant 37k: the empty run of hill just above the turrets, where the eye
        /// is already going to see what they are shooting) and the wave banner is stacked clear
        /// above it — which is also the right way round to read, since a wave arrives from the
        /// top of the hill and a cascade happened on the field.
        /// </para>
        /// <para>
        /// <b>The ladder may reach above the hill on a short board, and that is stated rather
        /// than clamped.</b> A 4:3 tablet leaves the hill 1.46 cells tall — less than one of
        /// these captions, let alone two — so no arrangement fits, and clamping would put them
        /// back on top of each other, which is the one thing this exists to stop.
        /// <c>SiegeCaptionTests</c> holds the separation at every shape and the fit at the shapes
        /// a phone really produces, which is the same split <c>SiegeBandTests</c> already makes.
        /// </para>
        /// </summary>
        public readonly struct Captions
        {
            /// <summary>Where a caption is drawn, and how far its box reaches from there.</summary>
            public readonly float Chain, ChainLow, ChainHigh;
            public readonly float Wave, WaveLow, WaveHigh;

            Captions(float chain, float chainLow, float chainHigh,
                     float wave, float waveLow, float waveHigh)
            {
                Chain = chain;
                ChainLow = chainLow;
                ChainHigh = chainHigh;
                Wave = wave;
                WaveLow = waveLow;
                WaveHigh = waveHigh;
            }

            /// <summary>
            /// The ladder for a board whose ward line stands at <paramref name="lineY"/> and
            /// whose cell is <paramref name="cell"/>.
            ///
            /// <para>
            /// Each caption's reach counts the drift it is drawn with as well as its box: both
            /// of them rise over their life, so a gap measured between two resting positions is
            /// a gap that closes while the player is watching.
            /// </para>
            /// </summary>
            public static Captions Of(float lineY, float cell)
            {
                float chain = lineY + ChainRise * cell;
                float chainLow = chain - ChainBox * .5f * cell;
                float chainHigh = chain + (ChainBox * .5f + ChainDrift) * cell;

                // Half of the *swollen* box, because a boss arrives at `WaveSwell` and shrinks
                // into place: measuring the settled size would clear the chain a beat after the
                // one frame the banner is at its largest.
                float half = WaveBox * WaveSwell * .5f * cell;

                float wave = chainHigh + CaptionClear * cell + half;

                return new Captions(chain, chainLow, chainHigh,
                                    wave, wave - half, wave + WaveFloat * cell + half);
            }
        }

        /// <summary>Where the chain banner sits above the ward line, its box, and its drift.</summary>
        // The box is a quarter wider than the largest font a chain is ever drawn at (`Chain`
        // ramps it to 1.02 cells at depth six), which is what a single centred line needs and no
        // more. It was 1.6 — harmless on its own, and a third of a cell of nothing that the
        // banner above it would have had to be lifted clear of.
        const float ChainRise = 2.35f, ChainBox = 1.25f, ChainDrift = .30f;

        /// <summary>The wave banner's box, how far a boss swells it, and how far it floats.</summary>
        const float WaveBox = .90f, WaveSwell = 1.6f, WaveFloat = .80f;

        /// <summary>Clear air between one hill caption and the next.</summary>
        const float CaptionClear = .22f;

        /// <summary>This board's caption ladder. A struct of floats, so it is built on demand.</summary>
        Captions Caption => Captions.Of(_lineY, Cell);

        // ------------------------------------------------------------------ geometry
        /// <summary>
        /// The cell, driven by the width and capped by what the hill can spare.
        ///
        /// <b>The width leads.</b> Taking the smaller of the two meant the height always won and
        /// the field was a column in the middle of a full-width plate, which is the one thing on
        /// this screen that had no reason to be inset. See <see cref="MaxGemBand"/> for what caps
        /// it, and `Compose` for how the hill and the line then share what is left.
        /// </summary>
        protected override float Fit(Vector2 room)
        {
            _room = room;

            float wide = (room.x - Margin * 2f) / Width;
            float tall = (room.y - Margin * 2f) * MaxGemBand / Height;
            return Mathf.Min(wide, tall);
        }

        protected override Vector2 Span
            => new Vector2(Mathf.Max(Cell * Width, _room.x - Margin * 2f),
                           Mathf.Max(Cell * Height, _room.y - Margin * 2f));

        protected override Vector2 CentreOf(int index)
        {
            int x = index % Width, y = index / Width;
            return new Vector2((x - (Width - 1) * .5f) * Cell,
                               _gemCentre + ((Height - 1) * .5f - y) * Cell);
        }

        /// <summary>Where a lane sits across the hill.</summary>
        float LaneX(int lane)
        {
            // **Inset the way `PostX` already insets the ward line, and a render is what said so.**
            // Divided by the lane count flat, the outer lanes put a raider's *centre* four tenths
            // of the board from the middle - which is fine for a body drawn in a square and is not
            // what this hill carries: the reels are cut to a fixed height and whatever width the
            // animation's box came out as, so the widest of them is two thirds wider than it is
            // tall. Drawn in lane nought or lane four it hangs over the plate, and the thing that
            // goes over the edge is whatever the pack drew furthest from the body - which on a
            // bulwark is the shield, the one part of it the mechanic is about.
            float wide = Span.x / (SiegeTuning.Lanes + .6f);
            return (lane - (SiegeTuning.Lanes - 1) * .5f) * wide;
        }

        /// <summary>Where a ward stands on the line.</summary>
        float PostX(int index)
        {
            int n = _posts != null ? _posts.Length : 1;
            float wide = Span.x / (n + .6f);
            return (index - (n - 1) * .5f) * wide;
        }

        /// <summary>How far down the hill a raider has come.</summary>
        float MarchY(float march) => Mathf.Lerp(_hillTop, _hillFoot, Mathf.Clamp01(march));

        // ------------------------------------------------------------------ the aiming grid
        /// <summary>
        /// Where the middle of one box of the hill's aiming grid sits.
        ///
        /// <para>
        /// <b>The one arithmetic, and it is written in terms of <see cref="MarchY"/> rather than
        /// beside it.</b> The panes a player taps and the boxes <c>SiegeBoard.Blast</c> reads have
        /// to be the same twenty rectangles — invariant 33g — and the cheapest way to guarantee
        /// that is for the drawing to be a function of the mapping the rule uses, so there is
        /// nothing left to agree about.
        /// </para>
        /// <para>
        /// <b>It was two, and both differences were silent.</b> The panes were laid out from
        /// <c>_hillTop + Cell * .35f</c> downward, where <c>march</c> nought is <c>_hillTop</c>
        /// exactly, so every row boundary on the screen sat up to a third of a cell above the one
        /// the rule read — a raider near a boundary was genuinely in the band above the box it
        /// looked like it was in. And the panes were <c>Span.x / Lanes</c> wide while their centres
        /// were spaced on <see cref="LaneX"/>'s inset pitch of <c>Span.x / (Lanes + .6f)</c>, so
        /// each one overlapped its neighbour by about a tenth of its width and the later sibling —
        /// the higher lane — won every tap in the seam. Reported from play as tapping a raider and
        /// being told nothing was there.
        /// </para>
        /// <para>
        /// <b>The grid is spaced flat and the raiders are inset, deliberately.</b> A raider's x is
        /// pulled in (see <see cref="LaneX"/>) so that a wide reel does not hang off the plate;
        /// the grid is not, because a grid must tile the board it is drawn over — an inset one
        /// would leave a strip down each edge that belongs to no box, and a tap there would fall
        /// through the targeting layer onto the gems underneath. Every lane's inset centre still
        /// lands inside its own flat box, so nothing is misfiled by the difference.
        /// </para>
        /// </summary>
        Vector2 BoxAt(int lane, int row)
            => new Vector2((lane - (SiegeTuning.Lanes - 1) * .5f) * BoxWide,
                           MarchY((row + .5f) / SiegeTuning.BlastRows));

        /// <summary>How wide one box of the aiming grid is. They abut; they do not overlap.</summary>
        float BoxWide => Span.x / SiegeTuning.Lanes;

        /// <summary>How deep one box of the aiming grid is.</summary>
        float BoxTall => (MarchY(0f) - MarchY(1f)) / SiegeTuning.BlastRows;

        /// <summary>
        /// Where a ward's fuel tube sits, as a board coordinate rather than a ward's own.
        ///
        /// <para>
        /// <b>Sitting on the top edge of the field's plate, in the strip under the plinths.</b>
        /// Worked out from the plate rather than typed as an offset from the turret, because that
        /// edge is what a player reads it against — and because both the cell and the way the bands
        /// divide move with the screen (see <c>Compose</c>), so a typed number is right on one
        /// phone and wrong on the next.
        /// </para>
        /// <para>
        /// <b>The plinths and the plate overlap</b>, which is why this cannot be the middle of a
        /// gap: on every screen this mode has been drawn at, the foot of a turret is already behind
        /// the field. What there is instead is the band immediately above the plate's edge, which
        /// is empty on every board and is directly over the gems whose colour fills it — the two
        /// halves of the decision this mode asks, one above the other.
        /// </para>
        /// </summary>
        float TubeY
        {
            get
            {
                float plate = _gemCentre + (Cell * Height + Cell * .34f) * .5f;
                // .15 rather than .22: a device said the bar sat a few pixels high of where it
                // belongs, which is as close to the plate's edge as it can be without the tube's
                // own trough overlapping it.
                return plate + Cell * .15f;
            }
        }
    }
}
