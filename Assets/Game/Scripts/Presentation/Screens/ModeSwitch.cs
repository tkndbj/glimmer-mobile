using System;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The control under the map's chapter plaque that swaps which way of playing you are
    /// looking at.
    ///
    /// <para>
    /// <b>A drop-down under the header rather than a drop-up in a corner.</b> It began in the
    /// bottom-right corner, which cost the map no vertical chrome and hid the one control every
    /// other mode is reached through — a pill under the thumb, on a screen whose whole job is a
    /// chain of glades running the other way, that a player has to be <em>taught</em> exists
    /// (see <c>Mechanic.ModeSwitch</c>). Under the plaque it is where the eye already is: the
    /// header is what names the place you are in, and which way of playing you are in is the
    /// same kind of fact as which chapter you are in.
    /// </para>
    /// <para>
    /// <b>The caller owns where it sits.</b> The map derives its whole header stack from
    /// <c>BannerY</c> downwards, so a switcher carrying its own corner offset would be a second
    /// copy of that arithmetic and would stop agreeing with the plaque the first time the plaque
    /// was resized — which is exactly what the chapter star count was changed to avoid. So
    /// <see cref="Build"/> takes the centre it should sit on and derives the menu from it.
    /// </para>
    /// <para>
    /// <b>Names and nothing else.</b> Each row carried its mode's generated mark, and a mark is
    /// what a mode looks like on a <em>node</em> — a leaf, a disc, a ring — which says nothing
    /// about how it is played and is one more thing to read in a list whose whole content is two
    /// words. The mode's colour still identifies it, on the selected row's seat and rim, so the
    /// list is still readable by something other than the word.
    /// </para>
    /// <para>
    /// Built as its own file rather than as four more methods on <c>LevelsScreen</c>, which is
    /// a thousand lines already. It knows nothing about chapters, layouts or progress: handed
    /// the catalog, the mode being shown and somewhere to send the answer, it does one thing.
    /// </para>
    /// <para>
    /// It draws nothing at all when there is one mode, which is what makes it safe to build
    /// unconditionally: a switcher offering a single choice is a control that teaches people
    /// their taps do nothing, and this game has already learned that lesson twice.
    /// </para>
    /// </summary>
    public static class ModeSwitch
    {
        /// <summary>
        /// The pill, and the gap between it and the list it opens.
        ///
        /// Bigger than the corner pill it replaced (300x96) because it is no longer chrome
        /// tucked out of the way — it is a header control, sitting under a 476-wide plaque, and
        /// a narrow one under a wide one reads as an afterthought rather than as part of the
        /// same piece of furniture.
        /// </summary>
        const float PillW = 372f, PillH = 116f, MenuGap = 16f;

        /// <summary>
        /// How tall the pill is, for whoever is stacking it.
        ///
        /// The map places this control (see <see cref="Build"/>) and therefore has to know how
        /// much room it takes, and a second copy of the number in the screen is precisely the
        /// drift the derived header constants exist to prevent.
        /// </summary>
        public const float PillHeight = PillH;

        /// <summary>
        /// Violet, and it is the only pill on this screen.
        ///
        /// <para>
        /// The corner pill was <c>btn_blue</c>, which is this UI's second-action colour — the
        /// undo key, the map key, the pill in a panel that is not the affirmative. That is
        /// exactly the wrong thing to say about the one control that reaches the other half of
        /// the game, and under a brown plaque on a dark teal fade it was also the least visible
        /// choice on the palette. Violet is the furthest thing here from the map's greens and
        /// golds and from the header's own blue and aqua chrome, so it reads as a control rather
        /// than as more header.
        /// </para>
        /// </summary>
        const string PillSkin = "btn_violet";

        const float RowW = 500f, RowH = 124f, RowGap = 12f;

        /// <summary>
        /// A row's width inside the plate, and how much of it the text may use — a margin either
        /// side, so a long translation stops short of the rim rather than on it.
        ///
        /// <para>
        /// A row's two lines are centred boxes at the row's own centre, which is what a list with
        /// no mark gutter wants — and it retires the one trap this file kept falling into.
        /// <c>UIKit.Box</c> always pivots at centre whatever it is anchored to, so a text block
        /// placed by its left edge starts outside the plate; with the mark gone there is no left
        /// edge to place anything against and nothing left to get wrong.
        /// </para>
        /// </summary>
        const float RowInner = RowW - 28f;
        const float TextW = RowInner - 56f;

        /// <summary>
        /// How the list arrives and leaves.
        ///
        /// <para>
        /// The exit is deliberately quicker than the entrance and eased the other way. An
        /// entrance is an invitation and can afford an overshoot; an exit is the answer to a tap
        /// that has already been made, so anything slower than about a sixth of a second reads
        /// as the menu arguing about it. <see cref="ExitRise"/> and <see cref="ExitSquash"/> are
        /// the same offset and squash it enters on, which is what makes the two read as one
        /// movement reversed rather than as two effects.
        /// </para>
        /// </summary>
        const float EntryTime = .22f, ExitTime = .15f;
        const float ExitRise = 26f, ExitSquash = .86f;

        /// <summary>
        /// Whether the list carries a row that is not a mode at all: the VFX bench
        /// (<c>Dev.VfxDemoScreen</c>), under <c>GLIMMER_BENCH</c> and nowhere else.
        ///
        /// <para>
        /// It hangs off this one constant so a build without that define is the file it was
        /// before — the guard below, the row count, the list height and the row itself all fold
        /// away together, and no shipped build can draw a control that navigates to a screen it
        /// does not contain.
        /// </para>
        /// <para>
        /// A define rather than <c>UNITY_EDITOR</c>, because the whole point of the bench is to
        /// judge an effect at the size, brightness and frame rate of a real phone. The same
        /// define decides whether the pack's bundle is packed at all — see <c>VfxBenchGroup</c>.
        /// </para>
        /// </summary>
#if GLIMMER_BENCH
        const bool Bench = true;
#else
        const bool Bench = false;
#endif

        /// <summary>
        /// Puts the switcher in <paramref name="host"/>, centred on <paramref name="y"/> measured
        /// down from the host's top edge, and hands back the pill it drew — or <c>null</c> when
        /// it drew nothing.
        ///
        /// <paramref name="host"/> should be the screen's safe-area layer: this is chrome, and a
        /// control under a notch is a control nobody can read.
        /// </summary>
        /// <remarks>
        /// The return value exists so the map can point a first-run lesson at this control
        /// (<c>Mechanic.ModeSwitch</c>), and it is the pill rather than a bool for the reason
        /// <c>TipOverlay.Target</c> takes a transform: the ring is cut around the real thing on
        /// the real screen, so nothing holds a second copy of where the control is — which is
        /// what made moving it out of the corner cost this file and one number in the map.
        /// <b>Null is the answer that matters</b> — it is what says the switcher is not on screen
        /// at all, which is exactly when that lesson must not be spent.
        /// </remarks>
        public static RectTransform Build(RectTransform host, CatalogIndex index, GameMode current,
                                          Action<GameMode> choose, float y)
        {
            if (index == null || (!index.HasSeveralModes && !Bench)) return null;

            var pill = UIKit.Button("ModeSwitch", host, Art.S("Ui/" + PillSkin),
                                    new Vector2(PillW, PillH), new Vector2(.5f, 1f),
                                    new Vector2(0f, y), null);

            float lift = PillH * UIKit.PillFaceLift;

            // Dead centre of the pill, with the chevron out at the rim. A centred name under a
            // centred plaque is the axis the whole header is built on; balancing the word against
            // a glyph beside it would put it off that axis by half the glyph.
            var label = UIKit.Titled("Name", pill.transform, Loc.Get(current.NameKey), 38, Pal.Cream,
                                     TextAnchor.MiddleCenter, new Vector2(PillW - 128f, PillH * .6f),
                                     new Vector2(.5f, .5f), new Vector2(0f, lift), 0f, 3f);
            UIKit.Shrinkable(label);

            // Pointing down, because the list opens downward. It turns rather than being swapped
            // for the other glyph while the menu is open: a mark that turns is the same mark
            // saying the same thing about the same list, where two glyphs are two symbols a
            // player has to notice are related.
            var chevron = UIKit.Titled("Chevron", pill.transform, "▼", 26, Pal.A(Pal.Cream, .78f),
                                       TextAnchor.MiddleCenter, new Vector2(34f, 34f),
                                       new Vector2(.5f, .5f),
                                       new Vector2(PillW * .5f - 40f, lift), 0f, 2f);

            pill.Setup(() => Open(host, index, current, choose, y, (RectTransform)chevron.transform));

            return (RectTransform)pill.transform;
        }

        /// <summary>
        /// Opens the list below the pill.
        ///
        /// <para>
        /// The veil is the whole reason this is one method rather than a small component: it
        /// swallows every tap outside the list, so there is no corner of the screen where a tap
        /// does nothing while a menu is open, and it is the one thing that has to be destroyed
        /// with the list however the list goes away.
        /// </para>
        /// <para>
        /// <b>It leaves the way it arrived.</b> Closing used to hide the veil and destroy it in
        /// the same frame — which is the house rule for a region being <em>replaced</em>, because
        /// <c>Destroy</c> lands at the end of the frame and an outgoing panel would otherwise be
        /// drawn over the one taking its place. Nothing replaces this one: it opens over a map
        /// that is already there and simply stops existing, so hiding it instantly is a list that
        /// vanishes mid-tap, which reads as a dropped frame rather than as a menu closing. It now
        /// falls back into the pill it came out of — the same short move, squash and fade it
        /// entered on, reversed — and is destroyed when that lands.
        /// </para>
        /// <para>
        /// The veil keeps eating taps for those few frames rather than releasing them the moment
        /// the exit starts, and the close is latched. Without both, a second tap during the exit
        /// reaches the pill underneath and opens a second menu over the one still leaving.
        /// </para>
        /// </summary>
        static void Open(RectTransform host, CatalogIndex index, GameMode current,
                         Action<GameMode> choose, float pillY, RectTransform chevron)
        {
            var veil = UIKit.Node("ModeVeil", host);
            UIKit.StretchTo(veil, 0, 0, 0, 0);

            // Invisible, and still the thing that catches every tap outside the list. A modal
            // over a *decision* earns a dim — it is saying "answer this first". This one is a
            // two-item menu under the header, and darkening the whole map to open it made the map
            // look switched off; the list has its own plate and rim, so it reads as raised
            // without the screen behind it having to be pushed down.
            //
            // An Image with no sprite hit-tests its whole rect whatever its alpha, so nothing
            // about swallowing the tap depended on the colour being visible.
            var catcher = veil.gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;

            var modes = index.Modes;
            int rows = modes.Count + (Bench ? 1 : 0);
            float height = rows * RowH + (rows - 1) * RowGap;

            // Hung from the same top edge the pill is, so the two cannot drift: the plaque, the
            // pill and the list are one stack measured downwards from the header.
            float listY = pillY - PillH * .5f - MenuGap - height * .5f;

            var list = UIKit.Box("Modes", veil, new Vector2(RowW, height), new Vector2(.5f, 1f),
                                 new Vector2(0f, listY));

            // One group for the whole list, so the exit is a single fade rather than a fade per
            // plate, rim, seat and line — which would be a dozen tweens racing to the same frame.
            var group = list.gameObject.AddComponent<CanvasGroup>();

            var plate = UIKit.Img("Plate", list, Art.Round(28), new Color(.05f, .11f, .16f, .95f));
            UIKit.StretchTo((RectTransform)plate.transform, -14, -14, -14, -14);
            plate.transform.SetAsFirstSibling();

            var edge = UIKit.Img("Edge", plate.transform, Art.RoundOutline(28, 3f), new Color(1, 1, 1, .16f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            bool closing = false;

            void Close()
            {
                if (closing || !veil) return;
                closing = true;

                if (chevron) Tween.Rotate(chevron, 0f, ExitTime, Ease.OutQuad);

                // Back up into the pill, shrinking and fading as it goes. The move is what makes
                // it read as returning to the control rather than merely disappearing; the fade
                // is what stops the last few frames looking like a list lying on the map.
                Tween.Move(list, new Vector2(0f, listY + ExitRise), ExitTime, Ease.InQuad);
                Tween.Scale(list, new Vector3(1f, ExitSquash, 1f), ExitTime, Ease.InQuad);
                Tween.Fade(group, 0f, ExitTime, Ease.InQuad).OnDone(() =>
                {
                    if (!veil) return;
                    veil.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(veil.gameObject);
                });
            }

            veil.gameObject.AddComponent<Btn>().Setup(Close, silent: true);

            for (int i = 0; i < modes.Count; i++)
            {
                var mode = modes[i];

                // Top-down, so the entry nearest the pill is the first one in the list and the
                // order reads the way the list opens.
                float rowY = height * .5f - RowH * .5f - i * (RowH + RowGap);
                Row(list, "Mode_" + mode.Value, Loc.Get(mode.NameKey), Loc.Get(mode.TaglineKey),
                    ModeLooks.Of(mode).Accent, mode == current, rowY,
                    () => { Close(); choose?.Invoke(mode); });
            }

#if GLIMMER_BENCH
            // Last, always, and never selected: it is a workbench rather than a way of playing,
            // so putting it under the real modes is what keeps the list still reading as the
            // list of games. Its words are literals rather than loc keys deliberately — nothing
            // here is ever seen by a player, and a key would be a string the translators are
            // asked to carry for ever.
            {
                float rowY = height * .5f - RowH * .5f - modes.Count * (RowH + RowGap);
                Row(list, "Mode_demo", "DEMO", "vfx bench", Pal.Bloom, false, rowY,
                    () => { Close(); Flow.Go<Dev.VfxDemoScreen>(); });
            }
#endif

            if (chevron) Tween.Rotate(chevron, 180f, EntryTime, Ease.OutBack);

            group.alpha = 0f;
            list.localScale = new Vector3(1f, ExitSquash, 1f);
            list.anchoredPosition = new Vector2(0f, listY + ExitRise);

            Tween.Move(list, new Vector2(0f, listY), EntryTime, Ease.OutCubic);
            Tween.Scale(list, Vector3.one, EntryTime, Ease.OutBack);
            Tween.Fade(group, 1f, EntryTime * .6f, Ease.OutQuad);

            // No sound here: the button that opened this list already spoke on pointer
            // down, and a second click as the list unrolls is one tap making two noises.
        }

        /// <summary>
        /// One row of the list.
        /// </summary>
        /// <remarks>
        /// Handed a name, a tagline and a colour rather than a <c>GameMode</c>, because the list
        /// carries one row that is not a mode (see <see cref="Bench"/>) and the alternative is a
        /// second copy of the seat, the rim and the two lines that could drift from this one.
        /// The caller resolves its own text, which is what keeps <c>Loc</c> out of a row that
        /// does not have a key.
        /// </remarks>
        static void Row(RectTransform parent, string id, string title, string tagline,
                        Color accent, bool selected, float y, Action tap)
        {
            var row = UIKit.Box(id, parent, new Vector2(RowInner, RowH),
                                new Vector2(.5f, .5f), new Vector2(0f, y));

            // The row's own hit area. UIKit.Img leaves raycastTarget off on everything it
            // builds, so a row made only of pictures is a row no tap ever reaches — invisible
            // in the Editor's hierarchy and obvious the first time somebody presses it.
            var hit = row.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;

            var seat = UIKit.Img("Seat", row, Art.Round(22),
                                 selected ? Pal.A(accent, .18f) : new Color(1f, 1f, 1f, .05f));
            UIKit.StretchTo((RectTransform)seat.transform, 0, 0, 0, 0);

            // The mode's colour, and with the marks gone this is the only thing carrying it. It
            // rings the row the player is already in rather than being decoration on all of them.
            if (selected)
            {
                var rim = UIKit.Img("Rim", row, Art.RoundOutline(22, 3f), Pal.A(accent, .70f));
                UIKit.StretchTo((RectTransform)rim.transform, 0, 0, 0, 0);
            }

            var name = UIKit.Titled("Name", row, title, 36,
                                    selected ? Pal.Cream : Pal.A(Pal.Cream, .82f),
                                    TextAnchor.MiddleCenter, new Vector2(TextW, 42f),
                                    new Vector2(.5f, .5f), new Vector2(0f, 20f), 0f, 2f);
            UIKit.Shrinkable(name);

            // The tagline is the only place the game ever says what a mode *is*, and it is here
            // rather than on a first-run panel because this is where somebody is deciding.
            var tag = UIKit.Label("Tag", row, tagline, 24,
                                  Pal.A(Pal.Cream, .60f), TextAnchor.MiddleCenter,
                                  new Vector2(TextW, 40f), new Vector2(.5f, .5f),
                                  new Vector2(0f, -22f));
            UIKit.Shrinkable(tag, 14);

            row.gameObject.AddComponent<Btn>().Setup(tap);
        }
    }
}
