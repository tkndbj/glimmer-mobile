using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// How a mode looks on the map and which screen plays it.
    ///
    /// <para>
    /// <b>Separate from <see cref="LevelMode"/> because of the layering line.</b> Domain must
    /// never reference Presentation, and a mode's rules are Domain while its screen and its
    /// colours are not. So a mode is declared twice — once for what it <em>is</em> and once for
    /// what it <em>looks like</em> — and that split is honest rather than a compromise: a
    /// re-grade is a client change with no content edit, and a rules change is a content change
    /// with no re-grade.
    /// </para>
    /// <para>
    /// <b>Every mode shares the map's art, and since the nodes came down onto the ground it
    /// shares the nodes too.</b> The one difference used to be the <b>perch</b> — the floating
    /// tile a level node stood on — which was a silhouette and so a difference somebody who
    /// cannot see colour could still read. A node standing on the painting has no tile, so what
    /// is left is nothing at all: with the trail gone too, <see cref="Accent"/> reaches the
    /// switcher and the mode's own screen, and never the map.
    /// </para>
    /// <para>
    /// <b>That is a real cost and it is deliberately unpaid today</b>, because one mode ships
    /// and the map draws no mode switcher at all — there is nothing on any screen in the game
    /// for a perch to tell apart. The day a second mode ships, the tell has to come back as
    /// something other than a colour, and the cheap answer is the one thing a node on the
    /// ground still has that a floating tile did not: the ground. A mode could name its own
    /// <c>GROUND</c> row in <c>Tools/make_map_seats.py</c> and stand on a different part of the
    /// same painting — the road for one, the meadow beside it for another — which costs a
    /// table entry and no art.
    /// </para>
    /// </summary>
    public abstract class ModeLook
    {
        public abstract GameMode Mode { get; }

        /// <summary>The screen that plays it. Resolved by <c>PlayRoute</c>.</summary>
        public abstract Type Screen { get; }

        /// <summary>
        /// The mode's colour: its row on the switcher, and the fireflies over its own screen.
        ///
        /// <para>
        /// It used to be the trail between the map's nodes as well, and that is where it did
        /// the work this rule is about. There is no trail now - the painting draws the road
        /// (<c>LevelsScreen.BuildNodes</c>) - so nothing on a map is drawn in a mode's colour
        /// at all, and two modes' maps are told apart by the chapter they are showing and by
        /// nothing else. See the note above about what that costs and how it comes back.
        /// </para>
        /// </summary>
        public abstract Color Accent { get; }
    }

    /// <summary>
    /// Every mode's look, registered once.
    ///
    /// <para>
    /// Mirrors <see cref="LevelModes"/> on the presentation side, in the same order and for the
    /// same reason, so the two cannot come to disagree about which mode is the front door.
    /// </para>
    /// <para>
    /// A mode missing from here draws as the classic one rather than crashing — a map with an
    /// odd-looking node is a far better failure than a map that will not open. That is looked up
    /// by name rather than taken from index nought, because index nought is now whichever mode
    /// the game leads with and the fallback is meant to be the <em>baseline</em> look, which is
    /// the glade's whether or not the glade is on the map today.
    /// </para>
    /// </summary>
    public static class ModeLooks
    {
        static readonly ModeLook[] _all =
        {
            new SiegeLook(),
            new PrismLook(),
            new GladeLook(),
            new FallLook(),
        };

        public static IReadOnlyList<ModeLook> All => _all;

        public static ModeLook Of(GameMode mode)
        {
            for (int i = 0; i < _all.Length; i++)
                if (_all[i].Mode == mode) return _all[i];

            for (int i = 0; i < _all.Length; i++)
                if (_all[i].Mode == GameMode.Default) return _all[i];

            return _all[0];
        }
    }

    sealed class GladeLook : ModeLook
    {
        public override GameMode Mode => GameMode.Glade;
        public override Type Screen => typeof(PlayScreen);

        public override Color Accent => Pal.Gold;
    }

    sealed class FallLook : ModeLook
    {
        public override GameMode Mode => GameMode.Fall;
        public override Type Screen => typeof(FallScreen);

        public override Color Accent => Pal.Ember;
    }

    /// <summary>
    /// Prismvale.
    ///
    /// <para>
    /// Verdant, and it is the second cold accent among the four modes left. The board itself is dark
    /// ground with bright jewels scattered over it, so the map is where this one says what it
    /// feels like before it is opened, and what it feels like is the green just before dawn. It
    /// is the only thing left that says which mode a map belongs to (see the note on
    /// <see cref="ModeLook"/> about what that costs).
    /// </para>
    /// </summary>
    sealed class PrismLook : ModeLook
    {
        public override GameMode Mode => GameMode.Prism;
        public override Type Screen => typeof(PrismScreen);
        public override Color Accent => Pal.Verdant;
    }

    /// <summary>
    /// Thornwatch.
    ///
    /// <para>
    /// Rose, and it is the only warning colour among the four modes left. Every other accent says what a
    /// place feels like; this one says what is about to happen there, because it is the only mode
    /// in the game where something is coming at the player while they think.
    /// </para>
    /// </summary>
    sealed class SiegeLook : ModeLook
    {
        public override GameMode Mode => GameMode.Siege;
        public override Type Screen => typeof(SiegeScreen);
        public override Color Accent => Pal.Rose;
    }
}
