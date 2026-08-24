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

        /// <summary>
        /// The mark on the switcher. Generated rather than a sprite, for <c>Art.Bloom</c>'s
        /// reason: this is drawn before a chapter's art has necessarily arrived, and an
        /// <c>Image</c> whose sprite has not loaded is a white rectangle rather than a blank.
        /// </summary>
        public abstract Sprite Mark();

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
        public override Sprite Mark() => Art.Leaf(96);
    }

    sealed class FallLook : ModeLook
    {
        public override GameMode Mode => GameMode.Fall;
        public override Type Screen => typeof(FallScreen);

        /// <summary>Bare stone — a well is cut into rock, and it reads as the hardest of the four.</summary>
        public override string Perch => "rock_plain";

        public override Color Accent => Pal.Ember;
        public override Sprite Mark() => Art.Disc(96);
        public override Color Wash => new Color(1f, .84f, .80f, 1f);
    }

    sealed class KeeperLook : ModeLook
    {
        public override GameMode Mode => GameMode.Keeper;
        public override Type Screen => typeof(KeeperScreen);

        /// <summary>Cut timber, for the mode about laying things out.</summary>
        public override string Perch => "rock_wood";

        public override Color Accent => Pal.Mint;
        public override Sprite Mark() => Art.Round(24);
        public override Color Wash => new Color(.84f, 1f, .88f, 1f);
    }

    sealed class WeaveLook : ModeLook
    {
        public override GameMode Mode => GameMode.Weave;
        public override Type Screen => typeof(WeaveScreen);

        /// <summary>Pale sand, so the drawn channels read brightest against it.</summary>
        public override string Perch => "rock_sand";

        public override Color Accent => Pal.Aqua;
        public override Sprite Mark() => Art.Ring(96, 14f);
        public override Color Wash => new Color(.80f, .98f, 1f, 1f);
    }
}
