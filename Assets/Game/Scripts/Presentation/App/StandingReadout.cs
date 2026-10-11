using System;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Social;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The chapter standing, on the map under the back key (invariant 62): a trophy and one
    /// sentence - "you are in the top N%" once every level of the chapter is cleared, and
    /// "clear every level to see where you stand" until then.
    ///
    /// <para>
    /// <b>A readout, which is to say watched rather than drawn</b> - 44j's rule, for
    /// <see cref="BoostReadout"/>'s reason. The sentence is a reading of two things that change
    /// while the map is standing: the save (a sync lands a clear from another device, and the
    /// screen's <c>OnLearned</c> repaints every reading of the save including this one) and the
    /// published table, which arrives on its own schedule after the map is drawn and is
    /// subscribed to here. The number itself is computed on the handset on every repaint
    /// (<see cref="ChapterStanding.ScoreOf(ChapterIndexEntry)"/>), so an improved clear moves it
    /// the moment the player is back on the map, with no server in the loop.
    /// </para>
    /// <para>
    /// <b>The same furniture as the star count in the other corner</b>, and deliberately so: the
    /// two are both readings of the chapter, one under each corner key, and a map with two
    /// different kinds of pill on it would read as two decisions.
    /// </para>
    /// </summary>
    public sealed class StandingReadout : MonoBehaviour
    {
        /// <summary>
        /// Builds the readout under <paramref name="parent"/> for <paramref name="chapter"/> and
        /// watches it for as long as it lives.
        ///
        /// <b>Safe to call more than once</b>, for <c>WalletWatch.Attach</c>'s reason: a screen
        /// that rebuilds its chrome calls this again from the same place, and a second component
        /// would be a second subscription drawing the same sentence twice. A second call with a
        /// different chapter re-points the one readout.
        /// </summary>
        public static StandingReadout Attach(Component host, RectTransform parent,
                                             Vector2 anchor, Vector2 position, ChapterIndexEntry chapter)
        {
            if (host == null || parent == null || chapter == null) return null;

            var readout = host.GetComponent<StandingReadout>();
            if (readout != null) { readout._chapter = chapter; readout.Repaint(); return readout; }

            readout = host.gameObject.AddComponent<StandingReadout>();
            readout._chapter = chapter;
            readout.Build(parent, anchor, position);

            // AddComponent has already run OnEnable against a null root, so the subscription is
            // standing and this is the paint that matters.
            readout.Hook();
            readout.Repaint();

            return readout;
        }

        // ------------------------------------------------------------------ the parts
        ChapterIndexEntry _chapter;
        RectTransform _root;
        Text _line;
        bool _hooked;

        /// <summary>
        /// The block: the star pill's own plate and rim, a trophy at the left, and one wrapped
        /// sentence beside it that shrinks to fit (<see cref="UIKit.Shrinkable"/>) - the
        /// translations of the locked sentence are twice the length of the English one and a
        /// caption that could grow is measured by <c>Tools/render_standing.py --measure</c>
        /// (19n).
        /// </summary>
        void Build(RectTransform parent, Vector2 anchor, Vector2 position)
        {
            _root = UIKit.Box("StandingReadout", parent, new Vector2(Width, Height), anchor, position);

            var plate = UIKit.Img("Pill", _root, Art.Round(28), Plate);
            UIKit.StretchTo((RectTransform)plate.transform, 0, 0, 0, 0);
            plate.raycastTarget = false;

            var edge = UIKit.Img("Edge", _root, Art.RoundOutline(28, 3f), Rim);
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);
            edge.raycastTarget = false;

            var icon = UIKit.Img("Icon", _root, Art.S("Ui/ic_trophy"), Pal.Cream,
                                 Vector2.one * IconSize, new Vector2(0f, .5f),
                                 new Vector2(Pad + IconSize * .5f, 0f));
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            _line = UIKit.Titled("Line", _root, string.Empty, FontSize, Pal.Cream, TextAnchor.MiddleLeft,
                                 new Vector2(TextWidth, Height - Pad), new Vector2(0f, .5f),
                                 new Vector2(TextLeft + TextWidth * .5f, 0f),
                                 outline: 3f, shadow: 3f, wrap: true);
            UIKit.Shrinkable(_line, FontFloor);
            _line.raycastTarget = false;
        }

        // ------------------------------------------------------------------ the watch
        void OnEnable() { Hook(); Repaint(); }

        void OnDisable()
        {
            if (!_hooked) return;

            ChapterStandings.Changed -= Repaint;
            _hooked = false;
        }

        void Hook()
        {
            if (_hooked) return;

            ChapterStandings.Changed += Repaint;
            _hooked = true;
        }

        /// <summary>
        /// Draws the state: finished or not, and where a finished chapter stands.
        ///
        /// Raised by the subscription and by the screen's own repaint, so it must be cheap and
        /// must never assume it is being called because something changed. The sentence is the
        /// whole of the state, so it is written whole every time.
        /// </summary>
        public void Repaint()
        {
            if (_root == null || _line == null || _chapter == null) return;

            _line.text = CaptionFor(_chapter);
        }

        /// <summary>
        /// The sentence for a chapter, off the save and the published table as they stand now.
        /// Static so the render mirror and the tests ask the same question the readout does.
        /// </summary>
        public static string CaptionFor(ChapterIndexEntry chapter)
        {
            long? score = ChapterStanding.ScoreOf(chapter);
            if (!score.HasValue) return Loc.Get("ui.levels.standing_locked");

            int top = ChapterStanding.Shown(score.Value, ChapterStandings.TableFor(chapter.Id));
            return Loc.Format("ui.levels.standing_top", top);
        }

        // ------------------------------------------------------------------ the layout
        /// <summary>
        /// Written down rather than scattered, for <c>ShopOverlays</c>'s reason, and public where
        /// the map has to place against them: <c>UIKit.Box</c> always pivots at centre, so
        /// whoever seats this needs its size, and a typed copy would go on placing it against
        /// the size it used to be. Narrow enough (260) to stay clear of the plaque and the mode
        /// switcher that share the header with it.
        /// </summary>
        public const float Width = 260f, Height = 112f;

        const float Pad = 14f, IconSize = 54f;
        const float TextLeft = Pad + IconSize + 10f;
        const float TextWidth = Width - TextLeft - Pad;
        const int FontSize = 26, FontFloor = 16;

        /// <summary>The star pill's plate and rim (<c>Scenery.Pill</c>), so the two corners match.</summary>
        static readonly Color Plate = new Color(.06f, .12f, .17f, .72f);
        static readonly Color Rim = new Color(1f, 1f, 1f, .13f);
    }
}
