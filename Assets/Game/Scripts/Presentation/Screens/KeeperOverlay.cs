using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using GlimmerGrove.Social;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The two doors into somebody else's game, opened by tapping their row on a board.
    ///
    /// <para>
    /// <b>A chooser rather than a destination, because a row now leads to two places.</b> A tap
    /// used to walk straight into a stranger's grovement, which was the only thing there was to
    /// see; there is a profile now — their keeper level, their companions, how far they have
    /// held the Infinite lane and the line they take into a siege — and a row that silently
    /// picked one of the two would make the other unreachable from the one screen it belongs to.
    /// Two buttons is the cheapest honest answer and it costs a tap nobody minds, because the
    /// panel is also where the row finally says <em>who</em> it is: the same portrait, the same
    /// name, at a size somebody can actually look at.
    /// </para>
    /// <para>
    /// <b>Dismissible on the scrim, where the report panel is not.</b> Nothing here costs
    /// anything or reaches anybody, so a stray tap outside it is a perfectly good "never mind" —
    /// the distinction <c>ModalView</c> draws, and the reason this is not a fourth confirmation.
    /// </para>
    /// <para>
    /// <b>It holds no art of its own.</b> The portrait is drawn out of the roster scope the board
    /// behind it is already holding, which is what makes opening this free: a modal that opened
    /// its own scope would load the whole companion roster to draw one face, and drop it again a
    /// tap later (invariant 7b). The face is simply absent until that scope lands, which is what
    /// a row one line above is doing anyway.
    /// </para>
    /// </summary>
    public sealed class KeeperOverlay : ModalView
    {
        const float Width = 860f;
        const float TitleRow = 150f;
        const float PortraitH = 250f, AfterPortrait = 6f;
        const float NameH = 62f, LineH = 40f, AfterName = 26f;
        const float ButtonH = 132f, BetweenButtons = 20f, FootMargin = 34f;

        /// <summary>Which keeper this is about. Set before the panel is built.</summary>
        public string OwnerId = string.Empty;

        /// <summary>
        /// What the board already knew about them, so the panel says something the moment it
        /// opens rather than after a round trip.
        ///
        /// <b>The row's own figures, never a fetch.</b> Both doors below fetch the card
        /// themselves, and a third fetch here would be a document read for a panel that is on
        /// screen for a second and a half.
        /// </summary>
        public LeaderboardEntry Entry;

        /// <summary>Which figure this keeper's row was ordered on, so the line says the same thing.</summary>
        public bool FromEndlessBoard;

        protected override void Build()
        {
            float height = TitleRow + PortraitH + AfterPortrait + NameH + LineH + AfterName
                         + ButtonH + BetweenButtons + ButtonH + FootMargin;

            MakePanel(new Vector2(Width, height), Loc.Get("ui.keeper.title"));

            float cursor = TitleRow;

            // ------------------------------------------------------------- who
            var medallion = UIKit.Img("Medallion", Panel, Art.Disc(256),
                                      Pal.A(Pal.Hex("#08333C"), .95f),
                                      Vector2.one * (PortraitH - 24f), new Vector2(.5f, 1f),
                                      new Vector2(0f, -(cursor + PortraitH * .5f)));

            var ring = UIKit.Img("Ring", medallion.transform, Art.Ring(256, 11f),
                                 Pal.A(Pal.Gold, .92f));
            UIKit.StretchTo((RectTransform)ring.transform, 0, 0, 0, 0);

            var face = UIKit.Img("Face", medallion.transform, null, Color.white,
                                 Vector2.one * (PortraitH - 100f), new Vector2(.5f, .5f),
                                 new Vector2(0f, 4f));
            face.preserveAspect = true;
            face.raycastTarget = false;
            CompanionArt.Paint(face, AvatarCatalog.Resolve(Entry.AvatarId));

            // The keeper level, in the corner the profile's own medallion puts it in — so the
            // two readings of "who is this" are the same picture at two sizes.
            var badge = UIKit.Img("LevelBadge", medallion.transform, Art.Disc(128), Pal.Gold,
                                  Vector2.one * 78f, new Vector2(1f, 0f), new Vector2(-2f, 2f));
            UIKit.Shrinkable(
                UIKit.Titled("N", badge.transform, Mathf.Max(1, Entry.KeeperLevel).ToString(), 38,
                             new Color(.30f, .20f, .05f), TextAnchor.MiddleCenter,
                             Vector2.one * 70f, new Vector2(.5f, .5f), Vector2.zero,
                             outline: 0f, shadow: 0f), 20);

            cursor += PortraitH + AfterPortrait;

            UIKit.Shrinkable(
                UIKit.Titled("Name", Panel, Entry.Name ?? string.Empty, 46,
                             new Color(.30f, .21f, .14f), TextAnchor.MiddleCenter,
                             new Vector2(Width - 160f, NameH), new Vector2(.5f, 1f),
                             new Vector2(0f, -(cursor + NameH * .5f)),
                             outline: 0f, shadow: 0f), 26);

            cursor += NameH;

            // The figure this keeper's row was ordered on, and only that one. A row drawn with
            // a number it was not ranked by descends by something nobody can see — the rule
            // `LeaderboardScreen.Row` already follows, said again here because the panel is a
            // second drawing of the same row.
            UIKit.Shrinkable(
                UIKit.Titled("Line", Panel,
                             FromEndlessBoard
                                 ? Loc.Format("ui.board.row_wave", Entry.Wave, Entry.KeeperLevel)
                                 : Loc.Format("ui.board.row_worth", Compact.Number(Entry.Score),
                                              Entry.KeeperLevel),
                             28, new Color(.44f, .33f, .24f), TextAnchor.MiddleCenter,
                             new Vector2(Width - 160f, LineH), new Vector2(.5f, 1f),
                             new Vector2(0f, -(cursor + LineH * .5f)),
                             outline: 0f, shadow: 0f), 19);

            cursor += LineH + AfterName;

            // ----------------------------------------------------------- the doors
            // The grovement leads, because it is what a board row has always opened and what
            // somebody tapping a row on a list of *groves* came for. The profile is the orange
            // second key rather than a dimmer one: they are two destinations, not a choice and
            // its alternative.
            Door("Grove", "ui.keeper.see_grove", "btn_green", "ic_nav_grove", cursor,
                 () => Flow.Go<GroveVisitScreen>(v => v.Visit(OwnerId, Entry.Name)));

            cursor += ButtonH + BetweenButtons;

            Door("Profile", "ui.keeper.see_profile", "btn_orange", "ic_nav_profile", cursor,
                 () => Flow.Go<PublicProfileScreen>(v => v.Show(OwnerId, Entry)));
        }

        /// <summary>One way in, sized and captioned the same however many there come to be.</summary>
        void Door(string name, string key, string skin, string icon, float top,
                  System.Action go)
        {
            var button = UIKit.TextButton(name, Panel, skin, Loc.Get(key), 38,
                                          new Vector2(Width - 220f, ButtonH),
                                          new Vector2(.5f, 1f),
                                          new Vector2(0f, -(top + ButtonH * .5f)),
                                          // Closed first, so the screen change lands on a panel
                                          // that is already leaving rather than under one that
                                          // is still up — `Flow.Go` tears the modal layer down
                                          // either way, and a panel yanked mid-animation is the
                                          // flicker every other door in this game avoids.
                                          () => Close(go, quiet: true),
                                          icon);

            UIKit.Shrinkable(button.Label, 22);
            UIKit.FitLabel(button);
        }

        /// <summary>Back closes, because nothing here has been decided.</summary>
        public override bool OnBack() { Close(); return true; }
    }
}
