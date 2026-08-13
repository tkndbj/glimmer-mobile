#if GLIMMER_SHOTS
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GlimmerGrove.Dev
{
    /// <summary>
    /// Development harness. Built only with the GLIMMER_SHOTS define; drives the game
    /// through every screen and writes reference screenshots, so the presentation can
    /// be reviewed without a human at the controls.
    /// </summary>
    public static class ShotDirector
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            string dir = null;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-glimmerShots") dir = args[i + 1];
            if (string.IsNullOrEmpty(dir)) return;

            Directory.CreateDirectory(dir);
            var go = new GameObject("~ShotDirector");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>().Dir = dir;
        }

        sealed class Runner : MonoBehaviour
        {
            public string Dir;
            int _n;

            IEnumerator Start()
            {
                PlayerPrefs.DeleteAll();
                GameSettings.SetMusic(false);
                GameSettings.SetSfx(false);

                yield return new WaitForSecondsRealtime(1.4f);
                yield return Shot("00_splash_early");
                yield return new WaitForSecondsRealtime(1.1f);
                yield return Shot("00b_splash_full");

                // wait for the splash to hand over to the menu
                float guard = 0f;
                while (!(Flow.Current is HomeScreen) && guard < 12f)
                { guard += Time.unscaledDeltaTime; yield return null; }
                yield return new WaitForSecondsRealtime(2.4f);
                yield return Shot("01_home");

                Flow.Modal<ComingSoonOverlay>(v => v.Configure("Shop", "ic_chest",
                    "Chests, critter skins and conduit styles will live here."));
                yield return new WaitForSecondsRealtime(1.4f);
                yield return Shot("01b_comingsoon");
                foreach (var m in Object.FindObjectsByType<ComingSoonOverlay>())
                    Flow.Dismiss(m);
                yield return new WaitForSecondsRealtime(.4f);

                Flow.Modal<SettingsOverlay>();
                yield return new WaitForSecondsRealtime(1.3f);
                yield return Shot("01c_settings");
                foreach (var m in Object.FindObjectsByType<SettingsOverlay>())
                    Flow.Dismiss(m);
                yield return new WaitForSecondsRealtime(.4f);

                Flow.Go<LevelsScreen>();
                yield return new WaitForSecondsRealtime(1.6f);
                yield return Shot("02_levels_bottom");
                yield return new WaitForSecondsRealtime(1.6f);
                yield return Shot("02b_levels_focus");
                var sr = Object.FindAnyObjectByType<ScrollRect>();
                if (sr != null)
                {
                    float f0 = sr.verticalNormalizedPosition;
                    for (float k = 0f; k < 1f; k += Time.unscaledDeltaTime * 1.3f)
                    { sr.verticalNormalizedPosition = Mathf.Lerp(f0, 1f, k); yield return null; }
                    sr.verticalNormalizedPosition = 1f;
                }
                yield return new WaitForSecondsRealtime(.7f);
                yield return Shot("02c_levels_top");

                Flow.Go<PlayScreen>(v => v.LevelId = GameContent.Catalog.At(0)?.Id ?? LevelId.None);
                yield return new WaitForSecondsRealtime(2.6f);
                yield return Shot("03_play_start");

                var play = Flow.Current as PlayScreen;
                var board = play != null ? play.Board : null;

                // click through the real raycaster: proves tiles are actually hittable
                yield return TapCentre(board);
                yield return new WaitForSecondsRealtime(.8f);
                yield return Shot("03b_after_tap");

                // solve about half of the board so the light is mid flow
                if (board != null)
                {
                    var p = board.P;
                    int done = 0, target = 0;
                    for (int i = 0; i < p.C.Length; i++) if (p.CanTurn(i) && !p.Solved(i)) target++;
                    target = Mathf.RoundToInt(target * .62f);
                    for (int i = 0; i < p.C.Length && done < target; i++)
                    {
                        if (!p.CanTurn(i) || p.Solved(i)) continue;
                        int turns = p.TurnsOwed(i);
                        for (int k = 0; k < turns; k++) p.Turn(i, 1);
                        done++;
                    }
                    board.SyncViews();
                    yield return null;
                }
                yield return new WaitForSecondsRealtime(1.4f);
                yield return Shot("04_play_partial");

                Flow.Modal<PauseOverlay>(v => v.Screen = play);
                yield return new WaitForSecondsRealtime(1.3f);
                yield return Shot("05_pause");
                foreach (var m in Object.FindObjectsByType<PauseOverlay>())
                    Flow.Dismiss(m);
                yield return new WaitForSecondsRealtime(.4f);

                // finish it off for the victory sequence
                if (board != null)
                {
                    var p = board.P;
                    for (int i = 0; i < p.C.Length; i++)
                    {
                        if (!p.CanTurn(i)) continue;
                        int turns = p.TurnsOwed(i);
                        for (int k = 0; k < turns; k++) p.Turn(i, 1);
                    }
                    p.Moves = p.Gold - 2;
                    board.SyncViews();
                    yield return null;
                }
                yield return new WaitForSecondsRealtime(1.2f);
                yield return Shot("06_solved");
                yield return new WaitForSecondsRealtime(3.4f);
                yield return Shot("07_win");

                Flow.Go<HomeScreen>();
                yield return new WaitForSecondsRealtime(2.2f);
                Flow.Modal<HowToOverlay>();
                yield return new WaitForSecondsRealtime(1.6f);
                yield return Shot("08_howto");

                Flow.Go<PlayScreen>(v => v.LevelId = GameContent.Catalog.At(2)?.Id ?? LevelId.None);
                yield return new WaitForSecondsRealtime(3.0f);
                yield return Shot("09_level3");

                yield return new WaitForSecondsRealtime(.6f);
                Application.Quit();
            }

            static IEnumerator TapCentre(BoardView board)
            {
                if (board == null || EventSystem.current == null) yield break;
                int before = board.Moves;
                var data = new PointerEventData(EventSystem.current)
                {
                    position = new Vector2(UnityEngine.Screen.width * .5f, UnityEngine.Screen.height * .5f),
                    button = PointerEventData.InputButton.Left
                };
                var hits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(data, hits);
                if (hits.Count == 0) { Debug.Log("[tap] nothing under the cursor"); yield break; }
                var top = hits[0].gameObject;
                Debug.Log($"[tap] top hit = {top.name} (tile: {top.GetComponent<TileView>() != null})");
                ExecuteEvents.Execute(top, data, ExecuteEvents.pointerDownHandler);
                yield return new WaitForSecondsRealtime(.08f);
                ExecuteEvents.Execute(top, data, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(top, data, ExecuteEvents.pointerClickHandler);
                yield return new WaitForSecondsRealtime(.35f);
                Debug.Log($"[tap] moves {before} -> {board.Moves}");
            }

            IEnumerator Shot(string name)
            {
                yield return new WaitForEndOfFrame();
                var path = Path.Combine(Dir, $"{name}.png");
                ScreenCapture.CaptureScreenshot(path);
                Debug.Log("[shot] " + path);
                _n++;
                yield return new WaitForSecondsRealtime(1.0f);
            }
        }
    }
}
#endif
