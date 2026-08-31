#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text;
using GlimmerGrove.Modes;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove.Dev
{
    /// <summary>
    /// Development harness. Plays the chapter's finale grove for real and measures what the
    /// board actually <em>did</em>, frame by frame, against the score <see cref="BudStage"/>
    /// wrote for it.
    ///
    /// <para>
    /// <b>It exists because the score being right is only half of a proof.</b>
    /// <c>BudStageTests</c> holds the four ordering rules over the plan; this holds the view to
    /// the plan — that a piece really is still until its cue, really moves for as long as its
    /// cue says, and really never covers enough ground between two frames to tear. Those are
    /// facts about the tween engine and the frame rate, so no amount of arithmetic in Domain can
    /// answer them and no EditMode test can either: <c>PlayChain</c> is a coroutine, and a
    /// MonoBehaviour does not pump one outside play mode.
    /// </para>
    /// <para>
    /// Editor-only and idle unless asked. <c>SessionState</c> is set by whoever wants a reading,
    /// the probe clears it on the way in, and nothing here reaches a player build.
    /// </para>
    /// <para>
    /// <b>Taking a reading costs a domain reload, and that reload does not always finish.</b>
    /// Entering play mode runs <c>Boot</c>, which starts the cloud backend and its threads, and
    /// those statics survive leaving play mode — measured once at eight and a half minutes of
    /// "Reloading Domain", window still responding, MCP bridge gone with it. So prove everything
    /// that can be proved offline first (<c>BudStageTests</c> holds all four ordering rules with
    /// no Editor at all); come here only for what genuinely needs a pumped coroutine and a
    /// running tween engine, and expect to relaunch the Editor afterwards.
    /// </para>
    /// <para>
    /// <b>What it read on 2026-08-31, on the finale's eight-wave opening tap</b>, against a score
    /// of 8.00s and 191 cues at a squeeze of 0.77: 56 of 56 pieces found, 47 cells took a fall,
    /// <b>none of them began before its cue</b>, none failed to move, and the worst a piece was
    /// late by was .10s against a .022s frame. The Editor drew it at 46fps with the whole game
    /// booted beside it, which puts the fastest instant of the deepest drop at about .23 of a
    /// cell in a frame — inside the quarter-cell bound <c>NoPieceEverFallsFastEnoughToTear</c>
    /// holds, and better than that on a phone, which is not sharing its frame with an Editor.
    /// </para>
    /// </summary>
    public static class BudMotionProbe
    {
        /// <summary>Set this to <c>true</c> and enter play mode to take a reading.</summary>
        public const string Flag = "glimmer.budprobe";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            if (!UnityEditor.SessionState.GetBool(Flag, false)) return;
            UnityEditor.SessionState.SetBool(Flag, false);

            var go = new GameObject("~BudMotionProbe");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>();
        }

        /// <summary>The chapter's finale, whose best opening tap is the deepest chain it ships.</summary>
        static readonly string[] Rows =
        {
            "YoRGCBoR",
            "YCCGBGBR",
            "COGooGOM",
            "BBYYBBRM",
            "MOGooCOB",
            "RYYMYRMY",
            "YoCCRBoY",
        };

        sealed class Runner : MonoBehaviour
        {
            IEnumerator Start()
            {
                // A canvas of its own rather than the game's, so the reading does not depend on
                // Boot, a save file or a route through the map.
                var root = new GameObject("ProbeCanvas", typeof(Canvas), typeof(CanvasScaler),
                                          typeof(GraphicRaycaster));
                DontDestroyOnLoad(root);

                root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

                var scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080, 2340);
                scaler.matchWidthOrHeight = 0f;

                var host = (RectTransform)new GameObject("Host", typeof(RectTransform)).transform;
                host.SetParent(root.transform, false);
                host.anchorMin = Vector2.zero;
                host.anchorMax = Vector2.one;
                host.offsetMin = Vector2.zero;
                host.offsetMax = Vector2.zero;
                Canvas.ForceUpdateCanvases();

                if (!Layout(out var layout)) { Report("the probe board will not parse"); yield break; }

                var view = new GameObject("BudView").AddComponent<BudView>();
                view.transform.SetParent(root.transform, false);
                view.Begin(host, layout, 8);

                // `Held` is the run-hold latch and it starts on, because a real run is not
                // allowed to begin until the lessons are done with (`RunHold`). There is no
                // screen here to release it.
                view.Held = false;
                Canvas.ForceUpdateCanvases();

                yield return null;

                // The tap this board is authored around, found the way the ladder finds it.
                int colour = layout.Deal.At(0), best = -1, deepest = 0;
                for (int i = 0; i < layout.Count; i++)
                {
                    var probe = new BudBoard(layout);
                    if (!probe.CanTap(i, colour)) continue;

                    var chain = probe.Tap(i, colour, null, null, null);
                    if (chain.Waves <= deepest) continue;
                    deepest = chain.Waves;
                    best = i;
                }

                if (best < 0) { Report("no legal opening tap"); yield break; }

                // What the model says, and therefore what the score will be built from.
                var pulses = new List<BudPulse>();
                var washes = new List<BudWash>();
                var drops = new List<BudDrop>();
                var read = new BudBoard(layout).Tap(best, colour, pulses, washes, drops);
                var score = BudStage.Of(read.Waves, pulses.ToArray(), washes.ToArray(),
                                        drops.ToArray(), layout.Width);

                var pieces = Pieces(host, layout.Count);
                var was = new float[pieces.Length];
                for (int i = 0; i < pieces.Length; i++)
                    was[i] = pieces[i] == null ? 0f : pieces[i].anchoredPosition.y;

                float cell = Cell(host);
                float t0 = Time.unscaledTime;

                // The tap itself, through the view's own path rather than around it.
                view.SendMessage("Tap", best, SendMessageOptions.DontRequireReceiver);

                float worst = 0f, worstAt = 0f;
                int worstCell = -1, frames = 0;
                var moved = new float[pieces.Length];
                for (int i = 0; i < moved.Length; i++) moved[i] = -1f;

                float run = score.Length + .5f;
                while (Time.unscaledTime - t0 < run)
                {
                    yield return null;
                    frames++;

                    float now = Time.unscaledTime - t0;

                    for (int i = 0; i < pieces.Length; i++)
                    {
                        if (pieces[i] == null) continue;

                        float y = pieces[i].anchoredPosition.y;
                        float step = Mathf.Abs(y - was[i]);
                        bool falling = Mathf.Abs(y) < Mathf.Abs(was[i]);
                        was[i] = y;

                        if (step < .01f) continue;
                        if (moved[i] < 0f) moved[i] = now;

                        // **Only motion toward the cell counts, and that is not a nicety.** A
                        // fall is *placed* at its start offset in the frame it is dealt — up to
                        // the height of the grove for a flower that grew — so the lift shows up
                        // as one enormous single-frame step. It is not something anybody sees
                        // (it happens above the clip, before the tween's first sample) and
                        // counting it would drown the number this is here to measure. A
                        // placement jumps *away* from the cell; a fall moves toward it.
                        if (!falling) continue;

                        // In cells, because a cell is the one length that means the same thing on
                        // every screen — and because what the eye reads is flower widths.
                        float cells = step / Mathf.Max(1f, cell);
                        if (cells <= worst) continue;

                        worst = cells;
                        worstAt = now;
                        worstCell = i;
                    }
                }

                var sb = new StringBuilder();
                int found = 0;
                for (int i = 0; i < pieces.Length; i++) if (pieces[i] != null) found++;

                sb.AppendLine("[BudMotionProbe] finale, tap " + best + ", " + read.Waves
                              + " waves, " + found + " of " + pieces.Length + " pieces found");
                sb.AppendLine("  score: " + score.Body.ToString("0.00") + "s of chain, "
                              + score.Length.ToString("0.00") + "s in all, squeeze "
                              + score.Squeeze.ToString("0.00") + ", " + score.Cues.Length + " cues");
                sb.AppendLine("  drawn: " + frames + " frames over " + run.ToString("0.00")
                              + "s (" + (frames / run).ToString("0") + " fps)");
                sb.AppendLine("  worst single-frame move: " + worst.ToString("0.000")
                              + " of a cell, at " + worstAt.ToString("0.00") + "s, cell " + worstCell);

                // **The earliest fall into each cell, and only that one.** A cell can receive
                // several falls in one chain — a piece lands, bursts on the next wave, and
                // another comes down into it — and `moved` records the *first* time anything
                // there was seen to move. Comparing a later cue against it would report the
                // piece as having started early when what really happened is that it had
                // already been and gone.
                var owed = new float[moved.Length];
                for (int i = 0; i < owed.Length; i++) owed[i] = -1f;

                foreach (var cue in score.Cues)
                {
                    if (cue.Kind != BudCueKind.Fall) continue;
                    if (cue.Cell < 0 || cue.Cell >= owed.Length) continue;
                    if (owed[cue.Cell] < 0f || cue.At < owed[cue.Cell]) owed[cue.Cell] = cue.At;
                }

                int late = 0, early = 0, still = 0, checked_ = 0;
                float slip = 0f, drift = 0f;

                for (int i = 0; i < owed.Length; i++)
                {
                    if (owed[i] < 0f) continue;

                    checked_++;
                    if (moved[i] < 0f) { still++; continue; }

                    float off = moved[i] - owed[i];

                    // A frame either side is not a slip: the probe samples once a frame, so a
                    // cue landing just after a sample is only seen on the next one.
                    if (off < -.034f) early++;
                    else if (off > .068f) late++;
                    if (Mathf.Abs(off) > Mathf.Abs(slip)) slip = off;
                    drift += off;
                }

                sb.AppendLine("  falls: " + checked_ + " cells, " + early + " began before their cue, "
                              + late + " more than a frame or two after it, " + still
                              + " never seen to move");
                sb.AppendLine("  slip:  worst " + slip.ToString("0.000") + "s, mean "
                              + (checked_ > 0 ? drift / checked_ : 0f).ToString("0.000")
                              + "s, against a " + (1f / Mathf.Max(1f, frames / run)).ToString("0.000")
                              + "s frame");

                Report(sb.ToString());

                UnityEditor.EditorApplication.isPlaying = false;
            }

            static void Report(string what) => Debug.Log(what);

            static bool Layout(out BudLayout layout)
            {
                layout = null;

                if (!BudDeal.TryParse("RGB", out var deal, out _)) return false;
                if (!BudDeal.TryParse("RGBYM", out var strip, out _, pure: false)) return false;
                if (!BudLayout.TryReadRows(Rows, Rows[0].Length, Rows.Length,
                                           out var ground, out var value, out _)) return false;

                layout = new BudLayout(Rows[0].Length, Rows.Length, ground, value, deal, strip);
                return true;
            }

            /// <summary>The transform each cell's contents travel on, reached the way a test does.</summary>
            static RectTransform[] Pieces(Transform under, int count)
            {
                var pieces = new RectTransform[count];
                var field = Find(under, "Buds");
                if (field == null) return pieces;

                for (int i = 0; i < field.childCount && i < count; i++)
                {
                    var piece = field.GetChild(i).Find("Piece");
                    pieces[i] = piece as RectTransform;
                }

                return pieces;
            }

            static Transform Find(Transform at, string name)
            {
                if (at.name == name) return at;

                for (int i = 0; i < at.childCount; i++)
                {
                    var hit = Find(at.GetChild(i), name);
                    if (hit != null) return hit;
                }

                return null;
            }

            static float Cell(Transform under)
            {
                var field = Find(under, "Buds");
                if (field == null || field.childCount < 2) return 1f;

                var a = field.GetChild(0) as RectTransform;
                var b = field.GetChild(1) as RectTransform;
                if (a == null || b == null) return 1f;

                return Mathf.Max(1f, Mathf.Abs(b.anchoredPosition.x - a.anchoredPosition.x));
            }
        }
    }
}
#endif
