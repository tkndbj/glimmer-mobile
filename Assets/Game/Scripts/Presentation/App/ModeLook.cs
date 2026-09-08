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

        /// <summary>The mode's colour: its trail, its switcher row, the wash over its perches.</summary>
        public abstract Color Accent { get; }

        /// <summary>A gentle tint over the perch, so a strip of nodes reads as one place.</summary>
        public virtual Color Wash => Color.white;
    }

    /// <summary>
    /// Every mode's look, registered once.
    ///
    /// Mirrors <see cref="LevelModes"/> on the presentation side. A mode missing from here draws
    /// as the classic one rather than crashing — a map with an odd-looking node is a far better
    /// failure than a map that will not open.
    /// </summary>
    public static class ModeLooks
    {
        static readonly ModeLook[] _all =
        {
            new GladeLook(),
            new FallLook(),
            new BudLook(),
            new MarchLook(),
            new EmberLook(),
            new PrismLook(),
            new SiegeLook(),
        };

        public static IReadOnlyList<ModeLook> All => _all;

        public static ModeLook Of(GameMode mode)
        {
            for (int i = 0; i < _all.Length; i++)
                if (_all[i].Mode == mode) return _all[i];
            return _all[0];
        }
    }

    sealed class GladeLook : ModeLook
    {
        public override GameMode Mode => GameMode.Glade;
        public override Type Screen => typeof(PlayScreen);

        /// <summary>Grassy stone: the grove the game opens in and the one everything else is read against.</summary>
        public override string Perch => "rock_grass";

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

        public override Color Accent => Pal.Ember;

        // No Wash override, deliberately. The ember tint this mode used to carry was written
        // for bare stone; a wash is a multiply, so over ice it takes the blue straight out of
        // the tile and leaves grey concrete around a murky puddle. The accent still carries
        // the mode's warmth where warmth belongs - the trail and the switcher row - and the
        // perch is left to read as the thing it is.
    }

    /// <summary>
    /// Budburst. Gold, because the whole mode is light spreading — a chain is a wave of it
    /// crossing the thicket, and the one colour on this board that is not a bud is the flash
    /// where one goes off. The perch is a mossy stump, which is the map tile that reads as
    /// undergrowth rather than as something standing in water or on a lawn.
    /// </summary>
    sealed class BudLook : ModeLook
    {
        public override GameMode Mode => GameMode.Bud;
        public override Type Screen => typeof(BudScreen);
        public override string Perch => "rock_chip";
        public override Color Accent => Pal.Gold;
        public override Color Wash => new Color(1f, .96f, .80f, 1f);
    }

    /// <summary>
    /// Hollowmarch. The one mode set in a raided village rather than the grove, so it takes the
    /// perch that glows rather than sits in something — a lit stone, which reads as a beacon in
    /// a strip of nodes and is the only tile in the set that does not look like ground.
    ///
    /// <para>
    /// Ember, because the mode <em>is</em> the chain: a cold road walking through a dark village
    /// with a line of hot things on it, and the whole payoff is those hot things going off one
    /// after another. The map picks the heat up on the trail and the switcher row, which is the
    /// one place a mode is allowed to say what it feels like before it is opened.
    /// </para>
    /// </summary>
    sealed class MarchLook : ModeLook
    {
        public override GameMode Mode => GameMode.March;
        public override Type Screen => typeof(MarchScreen);
        public override string Perch => "rock_lumen";
        public override Color Accent => Pal.Ember;
        public override Color Wash => new Color(1f, .93f, .86f, 1f);
    }

    /// <summary>
    /// Emberforge. A tall standing stone, which is the one tile in the set that reads as a
    /// chimney rather than as ground - and this mode is played inside the raiders' smelter, so
    /// what the strip of nodes should look like from across the map is a row of stacks.
    ///
    /// <para>
    /// Foxglove, and it is the only cold-bright accent among five modes. Four of them are warm
    /// already (gold, ember, gold, ember), so a fifth warm one would be a difference only some
    /// people can see - which is exactly what the perch rule exists to avoid relying on. The
    /// board itself is furnace-lit; the map is where the mode says what it feels like before it
    /// is opened, and what this one feels like is the cold jewel light coming out of a hot room.
    /// </para>
    /// </summary>
    sealed class EmberLook : ModeLook
    {
        public override GameMode Mode => GameMode.Ember;
        public override Type Screen => typeof(EmberScreen);
        public override string Perch => "rock_tall";
        public override Color Accent => Pal.Foxglove;
        public override Color Wash => new Color(.94f, .92f, 1f, 1f);
    }

    /// <summary>
    /// Prismvale. The wooded stone, which is the one tile in the set with growth standing on
    /// it - and this mode is played in a grove where the light has gone out of everything but
    /// the gems, so what a strip of these should read as from across the map is a stand of
    /// woodland.
    ///
    /// <para>
    /// Verdant, and it is the second cold accent among six modes. The board itself is dark
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
        public override Color Accent => Pal.Verdant;
        public override Color Wash => new Color(.90f, 1f, .94f, 1f);
    }

    /// <summary>
    /// Thornwatch. The bare sandy stone, which is the one tile in the set with nothing growing on
    /// it - and this mode is played on a hill the raiders have already walked over, so what a
    /// strip of these should read as from across the map is ground that has been crossed.
    ///
    /// <para>
    /// Rose, and it is the only warning colour among seven modes. Every other accent says what a
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
        public override Color Accent => Pal.Rose;
        public override Color Wash => new Color(1f, .90f, .88f, 1f);
    }
}
