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
            new KeeperLook(),
            new WeaveLook(),
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

    sealed class KeeperLook : ModeLook
    {
        public override GameMode Mode => GameMode.Keeper;
        public override Type Screen => typeof(KeeperScreen);

        /// <summary>Cut timber, for the mode about laying things out.</summary>
        public override string Perch => "rock_wood";

        public override Color Accent => Pal.Mint;
        public override Color Wash => new Color(.84f, 1f, .88f, 1f);
    }

    sealed class WeaveLook : ModeLook
    {
        public override GameMode Mode => GameMode.Weave;
        public override Type Screen => typeof(WeaveScreen);

        /// <summary>
        /// A lit face on dark earth — the mode's own subject standing under every glade. It is
        /// also the furthest thing in the set from the glade island: the sand block it replaced
        /// was the same rounded silhouette one tint away, which is no difference at all now that
        /// the Nightloom draws Mill Vale's map.
        /// </summary>
        public override string Perch => "rock_lumen";

        public override Color Accent => Pal.Aqua;
        public override Color Wash => new Color(.80f, .98f, 1f, 1f);
    }
}
