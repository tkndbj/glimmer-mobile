using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The control under the map's chapter plaque that swaps which way of playing you are looking
    /// at.
    ///
    /// <para>
    /// <b>A drop-down under the header rather than a drop-up in a corner.</b> It began in the
    /// bottom-right corner, which cost the map no vertical chrome and hid the one control every
    /// other mode is reached through — a pill under the thumb, on a screen whose whole job is a
    /// chain of glades running the other way, that a player has to be <em>taught</em> exists (see
    /// <c>Mechanic.ModeSwitch</c>). Under the plaque it is where the eye already is.
    /// </para>
    /// <para>
    /// <b>The furniture is <see cref="HeaderMenu"/>'s and only the rows are here.</b> That split
    /// arrived with the second switcher (<see cref="TrackSwitch"/>): a mode with two ladders in it
    /// needs the same pill saying a different word, and a second copy of the veil, the squash, the
    /// latch and the row would be a second place every one of those could stop being true.
    /// </para>
    /// <para>
    /// <b>Names and nothing else.</b> Each row carried its mode's generated mark, and a mark is
    /// what a mode looks like on a <em>node</em> — a leaf, a disc, a ring — which says nothing
    /// about how it is played and is one more thing to read in a list whose whole content is two
    /// words. The mode's colour still identifies it, on the selected row's seat and rim.
    /// </para>
    /// <para>
    /// It draws nothing at all when there is one mode, which is what makes it safe to build
    /// unconditionally: a switcher offering a single choice is a control that teaches people their
    /// taps do nothing, and this game has already learned that lesson twice.
    /// </para>
    /// </summary>
    public static class ModeSwitch
    {
        /// <summary>How tall the pill is, for whoever is stacking it.</summary>
        public const float PillHeight = HeaderMenu.PillHeight;

        /// <summary>
        /// Violet, and it is the only pill on this screen.
        ///
        /// <para>
        /// The corner pill was <c>btn_blue</c>, which is this UI's second-action colour — the undo
        /// key, the map key, the pill in a panel that is not the affirmative. That is exactly the
        /// wrong thing to say about the one control that reaches the other half of the game, and
        /// under a brown plaque on a dark teal fade it was also the least visible choice on the
        /// palette. Violet is the furthest thing here from the map's greens and golds and from the
        /// header's own blue and aqua chrome, so it reads as a control rather than as more header.
        /// </para>
        /// </summary>
        const string PillSkin = "btn_violet";

        /// <summary>
        /// Whether the list carries a row that is not a mode at all: the VFX bench
        /// (<c>Dev.VfxDemoScreen</c>), under <c>GLIMMER_BENCH</c> and nowhere else.
        ///
        /// <para>
        /// It hangs off this one constant so a build without that define is the file it was before
        /// — the guard below, the row count, the list height and the row itself all fold away
        /// together, and no shipped build can draw a control that navigates to a screen it does not
        /// contain.
        /// </para>
        /// </summary>
#if GLIMMER_BENCH
        const bool Bench = true;
#else
        const bool Bench = false;
#endif

        /// <summary>
        /// Puts the switcher in <paramref name="host"/>, centred on <paramref name="y"/>, and
        /// hands back the pill it drew — or <c>null</c> when it drew nothing.
        /// </summary>
        /// <remarks>
        /// The return value exists so the map can point a first-run lesson at this control
        /// (<c>Mechanic.ModeSwitch</c>), and it is the pill rather than a bool for the reason
        /// <c>TipOverlay.Target</c> takes a transform: the ring is cut around the real thing on the
        /// real screen, so nothing holds a second copy of where the control is.
        /// </remarks>
        public static RectTransform Build(RectTransform host, CatalogIndex index, GameMode current,
                                          Action<GameMode> choose, float y)
        {
            if (index == null || (!index.HasSeveralModes && !Bench)) return null;

            var modes = index.Modes;
            var rows = new List<HeaderRow>(modes.Count + 1);

            for (int i = 0; i < modes.Count; i++)
            {
                var mode = modes[i];

                rows.Add(new HeaderRow("Mode_" + mode.Value, Loc.Get(mode.NameKey),
                                       Loc.Get(mode.TaglineKey), ModeLooks.Of(mode).Accent,
                                       mode == current, () => choose?.Invoke(mode)));
            }

#if GLIMMER_BENCH
            // Last, always, and never selected: it is a workbench rather than a way of playing, so
            // putting it under the real modes is what keeps the list still reading as the list of
            // games. Its words are literals rather than loc keys deliberately — nothing here is
            // ever seen by a player, and a key would be a string the translators carry for ever.
            rows.Add(new HeaderRow("Mode_demo", "DEMO", "vfx bench", Pal.Bloom, false,
                                   () => Flow.Go<Dev.VfxDemoScreen>()));
#endif

            return HeaderMenu.Build(host, PillSkin, Loc.Get(current.NameKey), rows, y);
        }
    }

    /// <summary>
    /// The control under the mode switcher that swaps which <em>ladder</em> of one mode you are
    /// looking at: the ordinary run of chapters, or a lane beside it whose waves never stop.
    ///
    /// <para>
    /// <b>A second control rather than a widened first one</b>, and the alternative is what says
    /// why. Folding tracks into the mode list would give a player rows like "Thornwatch" and
    /// "Thornwatch · Infinite" — a list whose length is modes times tracks, whose rows repeat a
    /// word, and which reorders itself the day a second mode gets a second ladder. Two controls
    /// each answer one question, and each draws nothing when its own question has one answer
    /// (<see cref="HeaderMenu.Build"/>), so today the map shows exactly one pill.
    /// </para>
    /// <para>
    /// <b>Its colour is the mode's own</b>, because a track is a lane <em>inside</em> a mode: the
    /// two pills stacked read as one control and its subdivision rather than as two unrelated
    /// choices.
    /// </para>
    /// </summary>
    public static class TrackSwitch
    {
        public const float PillHeight = HeaderMenu.PillHeight;

        /// <summary>
        /// Orange, against the mode switcher's violet.
        ///
        /// The two are stacked and answer different questions, so they must not read as one
        /// control drawn twice. Orange is the map's own gold family, which is what a
        /// <em>ladder</em> is already drawn in up there.
        /// </summary>
        const string PillSkin = "btn_orange";

        public static RectTransform Build(RectTransform host, CatalogIndex index, GameMode mode,
                                          GameTrack current, Action<GameTrack> choose, float y)
        {
            if (index == null) return null;

            var tracks = index.TracksIn(mode);
            if (tracks == null || tracks.Count < 2) return null;

            var accent = ModeLooks.Of(mode).Accent;
            var rows = new List<HeaderRow>(tracks.Count);

            for (int i = 0; i < tracks.Count; i++)
            {
                var track = tracks[i];

                rows.Add(new HeaderRow("Track_" + track.Value, Loc.Get(track.NameKey),
                                       Loc.Get(track.TaglineKey), accent, track == current,
                                       () => choose?.Invoke(track)));
            }

            return HeaderMenu.Build(host, PillSkin, Loc.Get(current.NameKey), rows, y);
        }
    }
}
