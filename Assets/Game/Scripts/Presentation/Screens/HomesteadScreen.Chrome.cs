using GlimmerGrove.AssetPipeline;
using System;
using System.Collections.Generic;
using GlimmerGrove.App;
using GlimmerGrove.Homestead;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The Grovement's furniture: the banner, the two corner controls, the way to the shop
    /// and the one line of text that is silent unless it has news. Construction and nothing else —
    /// which is exactly why it is worth having apart from the screen's behaviour.
    /// </summary>
    public sealed partial class HomesteadScreen
    {
        // ---------------------------------------------------------------- header
        void BuildHeader()
        {
            var fade = UIKit.Img("TopFade", Content, Art.FadeUp(64), new Color(.02f, .06f, .09f, .82f));
            var frt = (RectTransform)fade.transform;
            frt.anchorMin = new Vector2(0f, 1f); frt.anchorMax = new Vector2(1f, 1f);
            frt.pivot = new Vector2(.5f, 1f);
            // Grown by whatever the system has taken from the top, because the fade is what
            // the banner and the summary are read against and both have just moved down by
            // that much. It is the one piece of the header that stays full-bleed — a gradient
            // that stopped at the safe edge would draw a visible horizontal seam across the
            // sky, which is worse than the cutout it was avoiding. Zero on a plain display.
            frt.sizeDelta = new Vector2(0f, 268f + SafeArea.Top);
            frt.anchoredPosition = Vector2.zero;
            frt.localRotation = Quaternion.Euler(0, 0, 180f);

            // Everything from here down is chrome, so it lives in the safe layer: on a phone
            // with a camera cutout the back arrow, the banner and the shop button all sat
            // under it, which is what this screen was reported for. Content stays full-bleed
            // and keeps the sky and the fade above — see View.Safe.
            var chrome = Safe;

            var banner = Scenery.TitleRibbon(chrome, Loc.Get("ui.grove.title").ToUpperInvariant(),
                                             new Vector2(470f, 128f), new Vector2(.5f, 1f),
                                             new Vector2(0f, -106f), 38, 20f);
            banner.transform.localScale = Vector3.zero;
            Tween.Pop(banner.transform, 0f, .6f, .1f);

            // The way out, where the balance used to be. The nav bar is gone from this screen
            // (see BuildField), so the corner needs an exit rather than a readout — and the
            // balance was the wrong thing to put here anyway: nothing on this screen is bought.
            // Land and decor are both bought in the shop, which shows the balance itself.
            UIKit.IconButton("Back", chrome, Skins.Nav, "ic_left", NavSize,
                             new Vector2(0f, 1f), new Vector2(NavX, NavY),
                             () => Flow.Go<HomeScreen>());

            // The line under the banner, and it is silent unless it has news. It used to
            // count what is standing, how much floor is owned and how many kinds are on it —
            // three numbers a player reads off the grove itself by looking at it, on the one
            // screen whose whole surface *is* that answer. What is left are the two states the
            // field cannot draw, because in both of them there is no field yet.
            _summary = UIKit.Shrinkable(
                UIKit.Titled("Summary", chrome, string.Empty, 26, Pal.Cream,
                             TextAnchor.MiddleCenter, new Vector2(720f, 34f),
                             new Vector2(.5f, 1f), new Vector2(0f, -176f), 3f, 0f), 18);

            // The shop is a screen of its own rather than a panel over this one, for
            // CompanionScreen's reason: what it lists is unbounded, and a grid that scrolls
            // inside a scrim is a worse place to browse than a page that owns the display.
            // Placed through UIKit.Corner because Box pivots at centre: passing the margin
            // straight in put half the button past the right edge of the screen.
            var shopSize = new Vector2(230f, 96f);
            var shopAnchor = new Vector2(1f, 1f);
            var shop = UIKit.TextButton("Shop", chrome, "btn_orange", Loc.Get("ui.grove.shop"), 28,
                                        shopSize, shopAnchor,
                                        UIKit.Corner(shopSize, shopAnchor, 28f, 62f),
                                        () => Flow.Go<HomesteadShopScreen>());
            UIKit.Shrinkable(shop.Label, 18);
            UIKit.FitLabel(shop);

            // Kept so the shop lesson can ring the real button rather than describe where it is.
            _shop = (RectTransform)shop.transform;

            // The way to the boards, beside the way out and the same size as it. The two are a
            // pair — both are "leave this screen" — so they read as a row rather than as a
            // control and a smaller afterthought stacked under it.
            //
            // It is deliberately *not* the score box in the corner: that box is a readout with
            // every raycast target switched off, because this screen is panned and pinched and
            // a control there would swallow a drag begun where a right thumb rests. A separate
            // button costs one glyph and leaves the gesture alone.
            UIKit.IconButton("Boards", chrome, Skins.Nav, "ic_trophy", NavSize,
                             new Vector2(0f, 1f), new Vector2(NavX + NavSize.x + NavGap, NavY),
                             () => Flow.Go<LeaderboardScreen>());

            _score = GroveScoreBox.Attach(chrome);
        }

        void PaintSummary()
        {
            if (!_summary) return;

            var catalog = HomesteadCatalog.Current;

            if (!HomesteadCatalog.IsLoaded)
            {
                _summary.text = Loc.Get("ui.grove.loading");
                return;
            }

            if (catalog.Floor.IsEmpty)
            {
                _summary.text = Loc.Get("ui.grove.unavailable");
                return;
            }

            _summary.text = string.Empty;
        }
    }
}
