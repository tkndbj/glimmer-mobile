using System;
using GlimmerGrove.Localization;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The pass offer raised by a reached Keeper's Pass chest on a ladder whose pass is not
    /// held: the chest the player tapped, shining, a sentence saying how many chests the pass
    /// opens, and the violet gem key that buys it.
    ///
    /// <para>
    /// <b>An offer, not a confirmation</b> (<c>ReminderAskOverlay</c>'s standing): closing it
    /// costs nothing, so the scrim dismisses and back closes. The key does not buy anything
    /// itself - it closes the panel and hands back to <see cref="EventScreen"/>, whose
    /// <c>BuyPass</c> is the one place the pass is sold, so its four answers stay in one place.
    /// </para>
    /// <para>
    /// The count and the price are the season's own numbers, handed in by the screen, never
    /// typed here: a retune of the ladder or the price must not leave this panel saying
    /// something the page beside it does not.
    /// </para>
    /// </summary>
    public sealed class SeasonPassOverlay : ModalView
    {
        /// <summary>The tapped chest's closed icon, or null for the kit's plain chest.</summary>
        public string ChestIcon;

        /// <summary>How many chests the paid column pays across the whole ladder.</summary>
        public int Chests;

        /// <summary>The pass's gem price.</summary>
        public int Gems;

        /// <summary>Raised after the panel has closed, when the buy key was pressed.</summary>
        public Action OnBuy;

        const float PanelW = 880f, PanelH = 820f;

        /// <summary>How tall the chest is drawn; the sprite box and lift derive from it (<see cref="ChestPack"/>).</summary>
        const float ChestTall = 200f;

        static readonly Color Ink = new Color(.36f, .25f, .18f);
        static readonly Vector2 Top = new Vector2(.5f, 1f);
        static readonly Vector2 Centre = new Vector2(.5f, .5f);

        bool _answered;

        protected override void Build()
        {
            MakePanel(new Vector2(PanelW, PanelH), Loc.Get("ui.mark.pass").Upper());

            // The chest on its own seat, in the same shine the ladder draws behind a chest that
            // is waiting: turning rays under a warm glow. Seat -250 from the top; the rays reach
            // about -60..-440 and are light, so they fade well before the sentence at -520.
            var seat = UIKit.Box("Chest", Panel, new Vector2(240f, 240f), Top, new Vector2(0f, -250f));

            var rays = UIKit.Img("Rays", seat, Art.Rays(256, 14), Pal.A(Pal.Sun, .42f),
                                 Vector2.one * 380f, Centre, Vector2.zero);
            rays.raycastTarget = false;
            Tween.Run(14f, Ease.Linear,
                      t => { if (rays) rays.transform.localRotation = Quaternion.Euler(0f, 0f, -t * 360f); },
                      rays, "spin").Loop(-1, false);

            UIKit.Img("Glow", seat, Art.Glow(128, 2f), Pal.A(Pal.Gold, .62f),
                      Vector2.one * 320f, Centre, Vector2.zero).raycastTarget = false;

            var chest = UIKit.Img("Icon", seat,
                                  Art.S(string.IsNullOrEmpty(ChestIcon) ? "Ui/ic_chest" : ChestIcon),
                                  Color.white,
                                  new Vector2(ChestTall / ChestPack.Fill * ChestPack.Aspect,
                                              ChestTall / ChestPack.Fill),
                                  Centre, new Vector2(0f, ChestTall * ChestPack.Lift));
            chest.preserveAspect = true;
            Tween.Breathe(chest.transform, .05f, 1.6f);

            UIKit.Shrinkable(
                UIKit.Titled("Why", Panel, Loc.Format("ui.mark.pass_unlock", Chests), 42,
                             Ink, TextAnchor.MiddleCenter, new Vector2(720f, 150f), Top,
                             new Vector2(0f, -520f), outline: 0f, shadow: 0f, wrap: true), 24);

            // Violet is the kit's gem key (`Skins.Gem`), the same one the pass plate sells with;
            // the figure is the caption and the trailing gem is its unit (`Btn.IconTrails`).
            var buy = UIKit.TextButton("Buy", Panel, Skins.Gem,
                                       Loc.Format("ui.mark.buy", Compact.Number(Gems)), 52,
                                       new Vector2(420f, 132f), new Vector2(.5f, 0f),
                                       new Vector2(0f, 120f), Buy,
                                       Art.S("Ui/ic_gem"), iconTrails: true);
            Tween.Breathe(buy.transform, .03f, 1.8f);
        }

        void Buy()
        {
            if (_answered) return;
            _answered = true;
            var then = OnBuy;
            Close(then, quiet: true);
        }

        public override bool OnBack()
        {
            if (!_answered) { _answered = true; Close(); }
            return true;
        }
    }
}
