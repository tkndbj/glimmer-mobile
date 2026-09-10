using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Dev;
using UnityEditor;
using UnityEngine;

namespace GlimmerGrove.EditorTools
{
    // Lays every projectile in the bought pack out on one sheet, so one can be *chosen* rather
    // than guessed at.
    //
    // <b>Why this exists as a tool and not as a look in the Project window.</b> These are
    // particle prefabs: their thumbnail is a grey cube, their name says a family and not a
    // silhouette, and the only way to know what one looks like at the size a phone draws it is to
    // rasterise it. Invariant 37k records six candidates being baked and compared before one boss
    // spell was picked, and every time this project has skipped that step it has shipped a
    // picture somebody had to redo — a cage that read as firewood, a lantern that read as a
    // crosshair, a spell that read as a magenta thread. Nineteen turrets is nineteen of those
    // decisions, so the comparison is made once, for the whole pack, on one sheet.
    // <b>Ungraded on purpose.</b> The shipping bake pulls a reel onto a ward's colour; this draws
    // what the pack actually painted, because the thing being chosen here is the *silhouette* and
    // the *motion*, and knowing which element a prefab was drawn as is what stops a turret being
    // assigned an effect that has to be fought all the way to its own colour.
    // It is a survey and never a gate: nothing it writes ships, and the sheet lands beside the
    // other contact sheets under <c>Tools/</c>.
    public static partial class SiegeShotBake
    {
        /// <summary>How wide the sheet is, in cells.</summary>
        const int SurveyCols = 6;

        /// <summary>How big one cell is drawn, and therefore how big one render is.</summary>
        const int SurveyCell = 200;

        /// <summary>
        /// How many frames are simulated to find the one worth showing.
        ///
        /// A still picture of a projectile is a lie if it is taken before the trail exists, so the
        /// effect is flown for a while and the <em>brightest</em> frame is what lands on the sheet
        /// — which is the same rule <c>Tools/render_siege.py</c> had to learn (drawn at a fixed
        /// index it caught two muzzles mid-dip and reported a good bake as broken).
        /// </summary>
        const int SurveyFrames = 10;
        const float SurveySeconds = .40f;

        /// <summary>How many head-widths of the effect the cell holds, and where the head sits.</summary>
        const float SurveyHeads = 3.2f;
        const float SurveyHeadAt = .78f;

        [MenuItem("Glimmer Grove/Art/Survey Projectile Pack", false, 33)]
        public static void Survey()
        {
            var names = new List<string>();

            foreach (var guid in AssetDatabase.FindAssets(
                         "t:GameObject", new[] { VfxBench.PackRoot + "/Projectiles" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".prefab")) names.Add(Path.GetFileNameWithoutExtension(path));
            }

            names.Sort(System.StringComparer.Ordinal);
            SurveyThese(names, "siege_pack_survey");
        }

        /// <summary>
        /// Renders one hero frame per named prefab onto a grid and writes it under <c>Tools/</c>.
        ///
        /// <b>Reports rather than throws, and never leaves the stage standing</b> —
        /// <see cref="BuildStage"/>'s camera lives at y = −4000 with <c>HideAndDontSave</c>, and one
        /// left behind renders into whatever is asked to render next.
        /// </summary>
        public static void SurveyThese(IList<string> names, string sheet)
        {
            if (names == null || names.Count == 0)
            {
                Debug.LogWarning("[siege survey] nothing to survey.");
                return;
            }

            GameObject stage = null;
            Camera cam = null;

            int rows = (names.Count + SurveyCols - 1) / SurveyCols;
            var pix = new Color32[SurveyCols * SurveyCell * rows * SurveyCell];

            // The hill, roughly — the same ground the shipping contact sheet composites over, so a
            // wisp that vanishes against grass vanishes here too.
            var ground = new Color32(46, 44, 42, 255);
            for (int i = 0; i < pix.Length; i++) pix[i] = ground;

            try
            {
                stage = BuildStage(out cam);

                for (int i = 0; i < names.Count; i++)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(names[i]));

                    if (prefab == null)
                    {
                        Debug.LogWarning("[siege survey] missing: " + names[i]);
                        continue;
                    }

                    var hero = Hero(stage.transform, cam, prefab);
                    if (hero == null || hero.Sheet == null) continue;

                    try
                    {
                        Place(pix, SurveyCols * SurveyCell, hero, Brightest(hero),
                              (i % SurveyCols) * SurveyCell,
                              (rows - 1 - i / SurveyCols) * SurveyCell);
                    }
                    finally { Object.DestroyImmediate(hero.Sheet); }
                }
            }
            finally
            {
                if (cam != null && cam.targetTexture != null)
                {
                    var rt = cam.targetTexture;
                    cam.targetTexture = null;
                    rt.Release();
                    Object.DestroyImmediate(rt);
                }
                if (stage != null) Object.DestroyImmediate(stage);
            }

            var tex = new Texture2D(SurveyCols * SurveyCell, rows * SurveyCell,
                                    TextureFormat.RGBA32, false, false);
            tex.SetPixels32(pix);
            tex.Apply(false);

            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".",
                                       "Tools", sheet + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            var order = new List<string>();
            for (int r = 0; r < rows; r++)
            {
                var line = new List<string>();
                for (int c = 0; c < SurveyCols; c++)
                {
                    int i = r * SurveyCols + c;
                    line.Add(i < names.Count ? names[i] : "-");
                }
                order.Add("row " + r + ": " + string.Join(" | ", line.ToArray()));
            }

            Debug.Log("[siege survey] " + path + "\n" + string.Join("\n", order.ToArray()));
        }

        /// <summary>
        /// The brightest frame of an effect, captured down the <em>shipping</em> path.
        ///
        /// <para>
        /// <b>The same framing the real bake would give it, and that is the whole point.</b> The
        /// first cut of this tool framed its own square around whatever the renderers measured,
        /// and every candidate in the pack came back as the same hairline — invariant 37k's sliver
        /// arriving inside the instrument built to catch it. A survey that frames differently from
        /// the bake answers a question nobody asked; this one shows what would ship.
        /// </para>
        /// <para>
        /// <b>Graded to one ward colour rather than left as the pack painted it.</b> What is being
        /// chosen here is a silhouette and a motion — the colour is imposed by the bake either
        /// way — so showing each candidate as a red ward would actually fire it is the comparison
        /// that decides.
        /// </para>
        /// </summary>
        static Book Hero(Transform stage, Camera cam, GameObject prefab)
        {
            // **A burst is not a comet and must not be captured as one.** A muzzle flash and an
            // impact happen in place and are over in a fifth of a second; flown down the comet path
            // at a projectile's speed and sampled over its window, they smear off the frame and the
            // sheet comes back empty — which reads as a missing prefab rather than as the wrong
            // question being asked. The pack files them in their own folders, so the name is an
            // honest test.
            if (prefab.name.StartsWith("vfx_Muzzle_") || prefab.name.StartsWith("vfx_Hit_"))
                return Capture(stage, cam, prefab, Pal.Poppy, HitFrames, Burst(prefab), 0f, 0f,
                               .5f, BurstSide, BurstSide, BurstSide, 1f, 1f, comet: false);

            float authored = Mathf.Max(1f, Reflected(prefab, "speed", 30f));
            float warm = WarmFor(TrailOf(prefab));

            var seen = Sample(stage, prefab, authored, warm, ShotSeconds, SiegeView.HeadAt);

            float want = seen.Across * 2f * WantedShot * SiegeView.HeadAt;
            float speed = seen.Behind > .05f
                ? authored * Mathf.Clamp(want / seen.Behind, SlowestBake, FastestBake)
                : authored;

            return Capture(stage, cam, prefab, Pal.Poppy, ShotFrames, ShotSeconds, speed, warm,
                           SiegeView.HeadAt, ShotTall, NarrowestShot, WidestShot,
                           LeanestShot, LongestShot, comet: true);
        }

        /// <summary>Which frame of a reel carries the most light — see <see cref="SurveyFrames"/>.</summary>
        static int Brightest(Book book)
        {
            var all = book.Sheet.GetPixels32();
            int best = 0;
            long peak = -1;

            for (int f = 0; f < book.Frames; f++)
            {
                long sum = 0;
                int at = f * book.Wide * book.Tall;

                for (int i = 0; i < book.Wide * book.Tall; i++)
                    sum += all[at + i].a;

                if (sum > peak) { peak = sum; best = f; }
            }
            return best;
        }

        /// <summary>
        /// Composites one frame of a reel into a square cell, fitted rather than stretched.
        ///
        /// Fitted, because these frames are not all the same shape and stretching them would
        /// hide the one thing this sheet is for: a comet that comes out of the bake as a sliver
        /// has to look like a sliver here.
        /// </summary>
        static void Place(Color32[] into, int wide, Book book, int frame, int ox, int oy)
        {
            var all = book.Sheet.GetPixels32();
            int at = frame * book.Wide * book.Tall;

            float scale = Mathf.Min((float)SurveyCell / book.Wide, (float)SurveyCell / book.Tall);
            int drawW = Mathf.Max(1, Mathf.RoundToInt(book.Wide * scale));
            int drawH = Mathf.Max(1, Mathf.RoundToInt(book.Tall * scale));
            int padX = (SurveyCell - drawW) / 2, padY = (SurveyCell - drawH) / 2;

            for (int y = 0; y < drawH; y++)
                for (int x = 0; x < drawW; x++)
                {
                    var c = all[at + (y * book.Tall / drawH) * book.Wide + x * book.Wide / drawW];

                    float alpha = c.a / 255f;
                    if (alpha <= 0f) continue;

                    int to = (oy + padY + y) * wide + ox + padX + x;
                    var under = into[to];

                    into[to] = new Color32(
                        (byte)(c.r * alpha + under.r * (1f - alpha)),
                        (byte)(c.g * alpha + under.g * (1f - alpha)),
                        (byte)(c.b * alpha + under.b * (1f - alpha)), 255);
                }
        }
    }
}
