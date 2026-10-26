using System.Collections;
using GlimmerGrove.Analytics;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The first sortie's half of the loadout (invariant 53h): after the shelf's own two lessons,
    /// Sprocket walks on with one sentence, and when he is thanked a BATTLE key rises in his place.
    ///
    /// <para>
    /// <b>A screen opened differently, never a different screen.</b> Everything the shelf does it
    /// still does, in the same order, for everybody; <see cref="Sortie"/> only adds a foot. It is
    /// set by the one caller that slides in from the tutorial's finale and by nothing else, so a
    /// player who has ever reached this screen from the map can never meet the guide, and nothing
    /// about it is stored - the instance is the whole of the state.
    /// </para>
    /// <para>
    /// <b>Nothing in the foot blocks the shelf.</b> The viewport is raised by whatever the foot
    /// takes (<see cref="GuideDock.Band"/>, then <see cref="GuideDock.KeyBand"/>), so every cell
    /// stays reachable by scrolling; the guide catches no taps but his own bubble's; and the
    /// header's back key and the hardware key keep meaning "the map", which is where a player
    /// who never thanks him ends up anyway.
    /// </para>
    /// </summary>
    public sealed partial class LoadoutScreen
    {
        /// <summary>
        /// Whether the tutorial's finale opened this loadout. Set before <c>Build</c>, by
        /// <c>TutorialScreen</c> alone.
        /// </summary>
        public bool Sortie { get; set; }

        /// <summary>How far the shelf's viewport stands off the foot of the safe area with nothing docked.</summary>
        const float ViewportFoot = 40f;

        /// <summary>How long the viewport and its shade take to make room, or give it back.</summary>
        const float FootTime = .36f;

        /// <summary>A beat between the guide being thanked and the key starting up.</summary>
        const float KeyBeat = .16f;

        /// <summary>The longest the guide waits for his reels before walking on without them.</summary>
        const float GuideWait = 2.5f;

        /// <summary>How far the foot's shade reaches past the safe area, to the display's edges.</summary>
        const float Bleed = 240f;

        /// <summary>The shade's ink: the header's own (<c>BuildHeader</c>'s top fade).</summary>
        static readonly Color FootInk = new Color(.02f, .06f, .09f, .82f);

        AssetHold _guideArt;
        System.Threading.Tasks.Task _guideLoad;

        RectTransform _dock;
        Image _footShade;
        SortieGuide _guide;
        Btn _battle;

        /// <summary>What the foot takes now, which a rebuilt viewport has to clear too.</summary>
        float _footBand;

        bool _guided;

        /// <summary>Where the viewport's bottom edge stands: off the foot, or clear of the dock.</summary>
        float FootNow => Mathf.Max(ViewportFoot, _footBand);

        /// <summary>
        /// Starts the guide's reels loading and lays the empty dock. Called from <c>Build</c>, so
        /// the reels have the whole of the lessons to arrive in.
        /// </summary>
        void PrepareSortie()
        {
            _guideArt = AssetLibrary.Hold("sortie_guide");
            _guideLoad = _guideArt.LoadAsync(AssetManifest.GuideAssets(), null, Lifetime);

            // Last under `Safe`, so it draws over the shelf; `Rebuild` re-seats the viewport and
            // the tabs at the front and never touches it.
            _dock = UIKit.Node("Sortie", Safe);
            _dock.SetAsLastSibling();

            // The ground the foot stands on: the header's dark fade, mirrored, so a turret card
            // scrolled under the dock goes into shadow rather than being cut off by a line.
            _footShade = UIKit.Img("Shade", _dock, Art.FadeUp(64), Pal.A(FootInk, 0f));
            var shade = _footShade.rectTransform;
            shade.anchorMin = new Vector2(0f, 0f);
            shade.anchorMax = new Vector2(1f, 0f);
            shade.pivot = new Vector2(.5f, 0f);
            shade.sizeDelta = new Vector2(Bleed * 2f, 0f);
            shade.anchoredPosition = new Vector2(0f, -Bleed);
        }

        /// <summary>
        /// The guide's cue: the shelf's lessons are done (or were never owed).
        ///
        /// <b>Asked once a visit</b>, and refused on any loadout the sortie did not open.
        /// </summary>
        void Guide()
        {
            if (!this || !Sortie || _guided) return;
            _guided = true;

            StartCoroutine(WalkOn());
        }

        IEnumerator WalkOn()
        {
            // The reels have had every second of the lessons to load; the ceiling is for a disk
            // that never answers, and then he walks on as a voice with no body (invariant 7b).
            float until = Time.unscaledTime + GuideWait;
            while (_guideLoad != null && !_guideLoad.IsCompleted && Time.unscaledTime < until)
                yield return null;

            if (!Living || _dock == null) yield break;
            if (_guideLoad != null && _guideLoad.IsFaulted) Debug.LogException(_guideLoad.Exception);

            _guide = SortieGuide.Enter(_dock);
            _guide.Dismissed = Thanked;

            Foot(_guide.Band);
        }

        /// <summary>The guide's key: the BATTLE key follows him out.</summary>
        void Thanked()
        {
            SortieAnalytics.Track(SortieAnalytics.GuideDismissed);

            Foot(GuideDock.KeyBand);
            Tween.After(KeyBeat, RaiseBattle, this);
        }

        /// <summary>
        /// The hub's BATTLE key, risen from below the display into the foot.
        ///
        /// <para>
        /// <b>The hub's key exactly</b> - <c>Skins.Battle</c>, <c>ui.home.battle</c>, the
        /// <c>ic_battle</c> mark grown to the same 112 and the hub's own plain size
        /// (<see cref="GuideDock.KeyW"/>, <see cref="GuideDock.KeyH"/>) - and it goes where the
        /// hub's goes, the map. <b>A sheen and no breath</b>, for the finale key's reason: a
        /// breath writes the scale a press needs, and this is a key somebody is about to press.
        /// </para>
        /// </summary>
        void RaiseBattle()
        {
            if (!Living || _dock == null || _battle != null) return;

            var key = UIKit.TextButton("Battle", _dock, Skins.Battle, Loc.Get("ui.home.battle"), 62,
                                       new Vector2(GuideDock.KeyW, GuideDock.KeyH), new Vector2(.5f, 0f),
                                       new Vector2(0f, GuideDock.KeyY), Battle, "ic_battle");
            _battle = key;

            // `HomeScreen.BuildPlay`'s glyph, for its reason: a painted mark beside the word at
            // the kit's third-of-a-pill is small for the one control the screen is now about.
            if (key.Icon)
            {
                ((RectTransform)key.Icon.transform).sizeDelta = Vector2.one * 112f;
                key.Icon.color = Color.white;
                UIKit.FitLabel(key);
            }

            var rt = (RectTransform)key.transform;
            var seat = rt.anchoredPosition;
            rt.anchoredPosition = seat - new Vector2(0f, GuideDock.KeyY + GuideDock.KeyH + Bleed);

            Audio.Sfx("chime", .45f);

            Tween.Move(rt, seat, .56f, Ease.OutBack).OnDone(() =>
            {
                if (!key) return;

                key.Rehome();
                Sheen.Attach(rt, 3.4f);
            });
        }

        void Battle()
        {
            SortieAnalytics.Track(SortieAnalytics.Battle);
            Flow.Go<LevelsScreen>();
        }

        /// <summary>
        /// Makes room for whatever the foot now holds: the viewport's bottom edge and the shade
        /// under the dock move together, and a rebuilt viewport takes the same answer
        /// (<see cref="FootNow"/>).
        /// </summary>
        void Foot(float band)
        {
            _footBand = band;
            float to = FootNow;

            var viewport = _viewport;
            if (viewport != null)
            {
                float from = viewport.offsetMin.y;
                Tween.Run(FootTime, Ease.OutCubic, t =>
                {
                    if (viewport) viewport.offsetMin = new Vector2(0f, Mathf.Lerp(from, to, t));
                }, viewport, "foot");
            }

            var shade = _footShade;
            if (shade != null)
            {
                var rt = shade.rectTransform;
                float h0 = rt.sizeDelta.y, h1 = band + Bleed + 70f;
                float a0 = shade.color.a, a1 = FootInk.a;

                Tween.Run(FootTime, Ease.OutCubic, t =>
                {
                    if (!shade) return;
                    rt.sizeDelta = new Vector2(Bleed * 2f, Mathf.Lerp(h0, h1, t));
                    shade.color = Pal.A(FootInk, Mathf.Lerp(a0, a1, t));
                }, shade, "foot");
            }
        }
    }
}
