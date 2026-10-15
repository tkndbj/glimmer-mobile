using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Layout;
using GlimmerGrove.Localization;
using GlimmerGrove.Shuffle;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The Shuffle hub's best run (MODES.md 59o): the player's best-graded run on this level - its
    /// letter, its build score and a chip per stat - on one plate between the best-wave medal and
    /// BATTLE. Laid out by <see cref="ShuffleHubStripLayout"/>; the compact twin of
    /// <see cref="ShuffleReportView"/>, reading the same record, the same stat catalog and the same
    /// words, so the hub, the victory panel and a profile cannot come to say different things.
    ///
    /// <para>
    /// <b>A drawing of the save and nothing else</b> (<c>ShuffleLedger.TopFor</c>): offline it is
    /// the device's own best, and when a sync brings a better one from another device the hub's
    /// column is redrawn because <c>EndlessHub.Reading</c> carries this record's fingerprint. A
    /// keeper with no graded run reads one line saying how to earn one, in the same plate, so the
    /// column is one shape either way.
    /// </para>
    /// <para>
    /// <b>The letter is global art; the chips wear the lane's card pictures</b>, which travel with
    /// the lane's scope (<c>SiegeMode.ShuffleArt</c>) and may still be arriving as the hub is drawn.
    /// So every chip picture starts off and <see cref="Pictures"/> puts each on the frame its sprite
    /// is in hand, then stops - an <c>Image</c> with no sprite is a white rectangle (7b).
    /// </para>
    /// </summary>
    public static class ShuffleHubStrip
    {
        /// <summary>
        /// Builds the plate into <paramref name="column"/>, centred <paramref name="centre"/> down
        /// from the column's top, for <paramref name="record"/> (null for no graded run yet).
        /// </summary>
        public static RectTransform Build(RectTransform column, float centre, ShuffleRecord record, bool arriving)
        {
            var at = ShuffleHubStripLayout.Default;
            float width = EndlessHubLayout.PanelWidth, height = EndlessHubLayout.StatsHeight;

            var panel = UIKit.Img("BestRun", column, Art.S("Ui/Hud/panel"), Color.white,
                                  new Vector2(width, height), new Vector2(.5f, 1f), new Vector2(0f, -centre));
            panel.type = Image.Type.Sliced;
            panel.raycastTarget = false;
            var plate = (RectTransform)panel.transform;

            // A zero-sized origin at the plate's top centre: the layout counts from there, x right
            // and y down as negative numbers (`UIKit.Box` pivots at centre, 44d).
            var origin = UIKit.Box("Origin", plate, Vector2.zero, new Vector2(.5f, 1f), Vector2.zero);

            if (record == null) Empty(origin, at, height);
            else Run(origin, at, record);

            if (arriving)
            {
                // Between the medal's pop and the key's, so the column still lands top to bottom.
                plate.localScale = Vector3.zero;
                Tween.Pop(plate, 0f, .5f, .36f);
            }

            return plate;
        }

        static void Empty(RectTransform origin, ShuffleHubStripLayout at, float height)
        {
            var line = UIKit.Titled("Empty", origin, Loc.Get("ui.shuffle.no_grade"), at.EmptySize,
                                    new Color(1f, .95f, .86f, .78f), TextAnchor.MiddleCenter,
                                    new Vector2(at.EmptyW, at.EmptyH), new Vector2(.5f, .5f),
                                    new Vector2(0f, -height * .5f), 3f, 3f, wrap: true);
            UIKit.Shrinkable(line, at.EmptyFloor);
        }

        static void Run(RectTransform origin, ShuffleHubStripLayout at, ShuffleRecord record)
        {
            var grade = record.Grade;

            // ------------------------------------------------------------ the header row
            // Three pieces side by side - the caption, the letter, the score - centred as one group
            // by the widths they actually draw (`ShuffleHubStripLayout.Centred`), so a short score
            // and a long one both sit in the middle of the plate.
            var caption = UIKit.Titled("Caption", origin, Loc.Get("ui.shuffle.best_run").Upper(), at.CaptionSize,
                                       new Color(1f, .95f, .86f, .78f), TextAnchor.MiddleCenter,
                                       new Vector2(at.HeaderMost, at.HeaderH), new Vector2(.5f, .5f),
                                       new Vector2(0f, at.HeaderY), 2f, 0f);

            var sprite = AssetLibrary.Peek<Sprite>(AssetManifest.ArtRoot + AssetManifest.ShuffleGrade(grade));
            var letter = UIKit.Img("Letter", origin, sprite, Color.white, new Vector2(at.LetterW, at.LetterH),
                                   new Vector2(.5f, .5f), new Vector2(0f, at.HeaderY));
            letter.preserveAspect = true;
            letter.raycastTarget = false;
            letter.enabled = sprite != null;

            // Global and preloaded (`AssetManifest.ShuffleGradeAssets`); set in type in its own
            // colour on the one occasion it could be missing (an Editor session before Sync).
            Text typed = sprite == null
                ? UIKit.Titled("Type", origin, ShuffleGradeLook.Letter(grade), at.LetterSize, ShuffleGradeLook.Ink(grade),
                               TextAnchor.MiddleCenter, new Vector2(at.LetterW, at.LetterH), new Vector2(.5f, .5f),
                               new Vector2(0f, at.HeaderY), 4f, 3f)
                : null;

            string points = record.Points.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            var score = UIKit.Titled("Score", origin, points, at.ScoreSize, Pal.Cream, TextAnchor.MiddleCenter,
                                     new Vector2(at.HeaderMost, at.HeaderH), new Vector2(.5f, .5f),
                                     new Vector2(0f, at.HeaderY), 3f, 3f);

            float letterW = typed != null ? ShuffleReportView.Drawn(typed) : ShuffleReportView.Fitted(sprite, at.LetterW, at.LetterH);
            var xs = at.Centred(ShuffleReportView.Drawn(caption), letterW, ShuffleReportView.Drawn(score));

            ShuffleReportView.Place(caption, xs[0], at.HeaderY);
            ShuffleReportView.Place(score, xs[2], at.HeaderY);
            ShuffleReportView.Place(typed != null ? (Graphic)typed : letter, xs[1], at.HeaderY);

            float light = ShuffleGradeLook.Light(grade);
            if (light > 0f)
            {
                var halo = UIKit.Img("Light", origin, Art.Glow(128, 2.1f), Pal.A(ShuffleGradeLook.Ink(grade), light * .8f),
                                     Vector2.one * (at.LetterW * 1.5f), new Vector2(.5f, .5f), new Vector2(xs[1], at.HeaderY));
                halo.raycastTarget = false;
                halo.transform.SetSiblingIndex(letter.transform.GetSiblingIndex());
                Tween.Breathe(halo.transform, .06f, 2.6f);
            }

            // ------------------------------------------------------------ the chips
            var pictures = new List<(Image image, string address)>(ShuffleStats.All.Length);
            int index = 0;

            foreach (var stat in ShuffleStats.All)
            {
                // Only what the record carries and this build can name, in the catalog's order.
                if (!record.TryGet(stat.Id, out int value)) continue;

                var (x, y) = at.ChipCentre(index++);
                var chip = UIKit.Img("Chip_" + stat.Id, origin, Art.S("Ui/" + RankKit.Row), Color.white,
                                     new Vector2(at.ChipW, at.ChipH), new Vector2(.5f, .5f), new Vector2(x, y));
                chip.raycastTarget = false;
                var rt = (RectTransform)chip.transform;

                var icon = UIKit.Img("Icon", rt, null, Color.white, Vector2.one * at.IconSize,
                                     new Vector2(.5f, .5f), new Vector2(at.IconX, 0f));
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                icon.enabled = false;
                pictures.Add((icon, AssetManifest.ArtRoot + stat.Icon));

                UIKit.Shrinkable(UIKit.Titled("Figure", rt, ShuffleStats.Say(stat.Kind, value), at.FigureSize, Pal.Cream,
                                              TextAnchor.MiddleLeft, new Vector2(at.TextW, at.FigureH),
                                              new Vector2(.5f, .5f), new Vector2(at.TextX, at.FigureY), 3f, 2f),
                                 at.FigureFloor);

                UIKit.Shrinkable(UIKit.Titled("Label", rt, Loc.Get(stat.NameKey), at.LabelSize,
                                              new Color(1f, .95f, .86f, .74f), TextAnchor.MiddleLeft,
                                              new Vector2(at.TextW, at.LabelH), new Vector2(.5f, .5f),
                                              new Vector2(at.TextX, at.LabelY), 2f, 0f),
                                 at.LabelFloor);
            }

            origin.gameObject.AddComponent<Pictures>().Watch(pictures);
        }

        /// <summary>
        /// Puts each chip's picture on the first frame its sprite is in hand, and stops once all are,
        /// or after <see cref="Patience"/> seconds (a scope that never lands draws no picture rather
        /// than polling for the life of the screen). Lives on the strip, so it dies with it.
        /// </summary>
        sealed class Pictures : MonoBehaviour
        {
            const float Patience = 20f;

            List<(Image image, string address)> _waiting;
            float _since;

            public void Watch(List<(Image image, string address)> pictures)
            {
                _waiting = pictures;
                Fill();
            }

            void Update()
            {
                _since += Time.unscaledDeltaTime;
                Fill();
                if (_since > Patience) enabled = false;
            }

            void Fill()
            {
                if (_waiting == null) { enabled = false; return; }

                for (int i = _waiting.Count - 1; i >= 0; i--)
                {
                    var (image, address) = _waiting[i];
                    if (!image) { _waiting.RemoveAt(i); continue; }

                    var sprite = AssetLibrary.Peek<Sprite>(address);
                    if (sprite == null) continue;

                    image.sprite = sprite;
                    image.enabled = true;
                    _waiting.RemoveAt(i);
                }

                if (_waiting.Count == 0) enabled = false;
            }
        }
    }
}
