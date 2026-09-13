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
    /// Every mode shares the map's art. The <b>perch</b> — the floating tile a level node stands
    /// on — is the one thing that differs, deliberately: one difference is enough to tell two
    /// maps apart at a glance, and a second would start to read as two games rather than one
    /// game played two ways.
    /// </para>
    /// </summary>
    public abstract class ModeLook
    {
        public abstract GameMode Mode { get; }

        /// <summary>The screen that plays it. Resolved by <c>PlayRoute</c>.</summary>
        public abstract Type Screen { get; }

        /// <summary>
        /// The floating tile a level node stands on. The single visual difference between modes,
        /// so it wants to be readable in silhouette rather than only in colour — a tint alone is
        /// a difference only some people can see.
        /// </summary>
        public abstract string Perch { get; }

        /// <summary>
        /// How far above a node's centre the glade disc stands, so that it sits on
        /// <see cref="Perch"/>'s own top face.
        ///
        /// <para>
        /// <b>A fact about the tile, never one number for every tile.</b> A perch is fitted
        /// into a fixed box with its aspect kept, so a sprite that is taller than it is wide
        /// lands smaller and higher than a squat one: measured on the four shipped tiles, the
        /// top face's middle sits anywhere from 16 units below a node's centre to 18 above it.
        /// One offset for all of them therefore plants the disc differently on every mode's
        /// map — it shipped at 2 for a year, which stood the siege's disc over the front edge
        /// of its tile and left the disc hanging in the air below the wood's planks. Nothing
        /// offline can see it, because no gate in this project opens a PNG.
        /// </para>
        /// <para>
        /// Chosen by eye against the real tile (<c>python Tools/render_perch.py --lift N</c>),
        /// which is the only instrument there is, and abstract rather than defaulted because
        /// a mode that picks a new tile has to answer this again — a default would be silently
        /// wrong for exactly the tile nobody has looked at yet.
        /// </para>
        /// </summary>
        public abstract float PerchLift { get; }

        /// <summary>The mode's colour: its trail, its switcher row, the wash over its perches.</summary>
        public abstract Color Accent { get; }

        /// <summary>A gentle tint over the perch, so a strip of nodes reads as one place.</summary>
        public virtual Color Wash => Color.white;
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

        /// <summary>Grassy stone: the grove the game opens in and the one everything else is read against.</summary>
        public override string Perch => "rock_grass";
        public override float PerchLift => 14f;

        public override Color Accent => Pal.Gold;
    }

    sealed class FallLook : ModeLook
    {
        public override GameMode Mode => GameMode.Fall;
        public override Type Screen => typeof(FallScreen);

        /// <summary>
        /// An ice font: a rim with dark water held in it, so the glade disc sits *in* something
        /// rather than on top of it. The only concave perch of the four, which is what tells it
        /// apart from the weave's ice without relying on colour.
        /// </summary>
        public override string Perch => "rock_basin";

        /// <summary>
        /// The shallowest lift of the four, and that is the font rather than a rounding: the
        /// disc is meant to sit <em>in</em> the water, so anything more stands it on the far
        /// rim and the tile stops reading as concave.
        /// </summary>
        public override float PerchLift => 10f;

        public override Color Accent => Pal.Ember;

        // No Wash override, deliberately. The ember tint this mode used to carry was written
        // for bare stone; a wash is a multiply, so over ice it takes the blue straight out of
        // the tile and leaves grey concrete around a murky puddle. The accent still carries
        // the mode's warmth where warmth belongs - the trail and the switcher row - and the
        // perch is left to read as the thing it is.
    }

    /// <summary>
    /// Prismvale. The wooded stone, which is the one tile in the set with growth standing on
    /// it - and this mode is played in a grove where the light has gone out of everything but
    /// the gems, so what a strip of these should read as from across the map is a stand of
    /// woodland.
    ///
    /// <para>
    /// Verdant, and it is the second cold accent among the four modes left. The board itself is dark
    /// ground with bright jewels scattered over it, so the map is where this one says what it
    /// feels like before it is opened, and what it feels like is the green just before dawn. It
    /// shares a perch silhouette with no other mode, which is the rule that matters: a tint
    /// alone is a difference only some people can see (invariant 7c).
    /// </para>
    /// </summary>
    sealed class PrismLook : ModeLook
    {
        public override GameMode Mode => GameMode.Prism;
        public override Type Screen => typeof(PrismScreen);
        public override string Perch => "rock_wood";
        public override float PerchLift => 18f;
        public override Color Accent => Pal.Verdant;
        public override Color Wash => new Color(.90f, 1f, .94f, 1f);
    }

    /// <summary>
    /// Thornwatch. The bare sandy stone, which is the one tile in the set with nothing growing on
    /// it - and this mode is played on a hill the raiders have already walked over, so what a
    /// strip of these should read as from across the map is ground that has been crossed.
    ///
    /// <para>
    /// Rose, and it is the only warning colour among the four modes left. Every other accent says what a
    /// place feels like; this one says what is about to happen there, because it is the only mode
    /// in the game where something is coming at the player while they think. It shares a perch
    /// silhouette with no other mode, which is the rule that matters: a tint alone is a difference
    /// only some people can see (invariant 7c).
    /// </para>
    /// </summary>
    sealed class SiegeLook : ModeLook
    {
        public override GameMode Mode => GameMode.Siege;
        public override Type Screen => typeof(SiegeScreen);
        public override string Perch => "rock_sand";
        public override float PerchLift => 20f;
        public override Color Accent => Pal.Rose;
        public override Color Wash => new Color(1f, .90f, .88f, 1f);
    }
}
