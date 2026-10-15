using System.Collections.Generic;
using GlimmerGrove.Layout;
using GlimmerGrove.Localization;
using GlimmerGrove.Shuffle;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The colour a grade is drawn in, and how much light it is given (MODES.md 59o).
    ///
    /// <para>
    /// <b>One switch whose default throws</b> (invariant 44e): a grade appended to the enum falls
    /// to the throw rather than borrowing another grade's colour, and <c>ShuffleGradeTests</c>
    /// walks every grade through here so that is a red test, not a surprise on a device.
    /// The ladder climbs from cold to warm, and only the three top grades are given light: a halo
    /// that breathes on S and S+ and sparks on S++, so the rare thing is the bright thing.
    /// </para>
    /// </summary>
    public static class ShuffleGradeLook
    {
        public static Color Ink(ShuffleGrade grade)
        {
            switch (grade)
            {
                // The body colour of each letter's picture (`Ui/shuffle_grade_{id}`), so the light
                // and the sparks are the letter's own and the fallback type matches it.
                case ShuffleGrade.D: return Pal.Hex("#FFB21E");
                case ShuffleGrade.C: return Pal.Hex("#3FA9FF");
                case ShuffleGrade.B: return Pal.Hex("#B35CFF");
                case ShuffleGrade.A: return Pal.Hex("#FF5AAE");
                case ShuffleGrade.AA: return Pal.Hex("#FF5AAE");
                case ShuffleGrade.S: return Pal.Hex("#5EDC3C");
                case ShuffleGrade.SPlus: return Pal.Hex("#7CE83E");
                case ShuffleGrade.SPlusPlus: return Pal.Hex("#B2F23A");
                default: throw new System.ArgumentOutOfRangeException(nameof(grade), grade, "a grade with no colour");
            }
        }

        /// <summary>How strong the halo behind the emblem is: nought below S.</summary>
        public static float Light(ShuffleGrade grade)
        {
            switch (grade)
            {
                case ShuffleGrade.D:
                case ShuffleGrade.C:
                case ShuffleGrade.B:
                case ShuffleGrade.A:
                case ShuffleGrade.AA: return 0f;
                case ShuffleGrade.S: return .38f;
                case ShuffleGrade.SPlus: return .52f;
                case ShuffleGrade.SPlusPlus: return .68f;
                default: throw new System.ArgumentOutOfRangeException(nameof(grade), grade, "a grade with no light");
            }
        }

        /// <summary>The letter as the active language writes it.</summary>
        public static string Letter(ShuffleGrade grade) => Loc.Get(ShuffleGrades.NameKey(grade));
    }

    /// <summary>
    /// A graded Shuffle run drawn as one block (MODES.md 59o): the grade's letter (the owner's
    /// picture, <c>AssetManifest.ShuffleGrade</c>, global), the build score beside it with a line under it, and a tile per stat with the picture of the
    /// card that moves it. Laid out by <see cref="ShuffleReportLayout"/>; built once by the
    /// victory panel and once by a keeper's public profile, so the two cannot come to disagree
    /// about what a run looks like.
    ///
    /// <para>
    /// <b>Only stats this build knows are drawn</b>, in the catalog's order: a record written by a
    /// newer build carries rows this one has no words or picture for, and those are kept in the
    /// save and left off the screen (an unknown id draws as nothing, never as a white rectangle).
    /// </para>
    /// <para>
    /// <b>Built hidden or shown</b>: the victory panel builds it at scale nought and walks it on
    /// with <see cref="Play"/>; a profile builds it shown. The card pictures are the Shuffle lane's
    /// scope (<c>SiegeMode.ShuffleArt</c>), which a run holds; a host that does not (the profile)
    /// loads <see cref="Art"/> under its own scope and calls <see cref="Repaint"/> when it lands -
    /// every tile picture starts disabled, because an <c>Image</c> with no sprite is a white
    /// rectangle (7b).
    /// </para>
    /// </summary>
    public sealed class ShuffleReportView
    {
        public RectTransform Root { get; private set; }

        readonly ShuffleReportLayout _at = ShuffleReportLayout.Default;
        readonly List<ShuffleStat> _shown = new List<ShuffleStat>(ShuffleStats.All.Length);
        readonly List<RectTransform> _tiles = new List<RectTransform>(ShuffleStats.All.Length);
        readonly List<Image> _icons = new List<Image>(ShuffleStats.All.Length);

        ShuffleRecord _record;
        RectTransform _emblem;
        Image _halo;
        Text _caption, _score, _note;

        /// <summary>The block's height for a record, before it is built - what a host stacks on.</summary>
        public static float HeightFor(ShuffleRecord record) => ShuffleReportLayout.Default.HeightFor(Drawn(record).Count);

        /// <summary>
        /// The card pictures a host must hold for the tiles - the stats whose picture is a Shuffle
        /// card or diamond - for a host outside the lane's own scope.
        /// </summary>
        public static List<AssetPipeline.AssetRequest> Art(ShuffleRecord record)
        {
            var wanted = new List<AssetPipeline.AssetRequest>(ShuffleStats.All.Length);
            foreach (var stat in Drawn(record))
                wanted.Add(AssetPipeline.AssetRequest.Sprite(AssetPipeline.AssetManifest.ArtRoot + stat.Icon));
            return wanted;
        }

        /// <summary>
        /// Builds the block into <paramref name="parent"/> with its top edge at
        /// <paramref name="top"/> (negative, from the parent's top). <paramref name="note"/> is the
        /// line under the score - NEW BEST, the best so far, or nothing.
        /// </summary>
        /// <param name="emblemScale">How large the grade is drawn against the layout's own size - one for the victory panel.</param>
        /// <param name="captionInk">The score's caption colour; null is the layout's own faded cream.</param>
        public static ShuffleReportView Build(Transform parent, float top, ShuffleRecord record,
                                              string note, Color noteInk, bool hidden,
                                              float emblemScale = 1f, Color? captionInk = null)
        {
            var view = new ShuffleReportView();
            view.Assemble(parent, top, record, note, noteInk, hidden, emblemScale, captionInk);
            return view;
        }

        static List<ShuffleStat> Drawn(ShuffleRecord record)
        {
            var drawn = new List<ShuffleStat>(ShuffleStats.All.Length);
            if (record == null) return drawn;

            foreach (var stat in ShuffleStats.All)
                if (record.TryGet(stat.Id, out _)) drawn.Add(stat);

            return drawn;
        }

        void Assemble(Transform parent, float top, ShuffleRecord record, string note, Color noteInk, bool hidden,
                      float emblemScale, Color? captionInk)
        {
            // The grade's box at this host's size. Only ever smaller than the layout's, so the
            // block's height (`HeightFor`) still holds it.
            float scale = Mathf.Clamp(emblemScale, .3f, 1f);
            float emblemW = _at.EmblemW * scale, emblemH = _at.EmblemH * scale;

            _record = record;
            _shown.AddRange(Drawn(record));

            float height = _at.HeightFor(_shown.Count);

            // The block, and inside it a zero-sized origin at its top centre: every figure in the
            // layout is measured from there, x right and y down (negative), so nothing here needs
            // converting. `UIKit.Box` pivots at centre (44d), hence the half height.
            var block = UIKit.Box("ShuffleReport", parent, new Vector2(_at.Width, height),
                                  new Vector2(.5f, 1f), new Vector2(0f, top - height * .5f));
            Root = UIKit.Box("Origin", block, Vector2.zero, new Vector2(.5f, 1f), Vector2.zero);

            var grade = record != null ? record.Grade : ShuffleGrade.D;
            var ink = ShuffleGradeLook.Ink(grade);
            float light = ShuffleGradeLook.Light(grade);

            // ------------------------------------------------------------ the grade
            var emblemPos = new Vector2(_at.EmblemX, _at.EmblemY);
            if (light > 0f)
            {
                _halo = UIKit.Img("Light", Root, global::GlimmerGrove.Art.Glow(128, 2.1f), Pal.A(ink, light),
                                  new Vector2(emblemW * 2.2f, emblemW * 2.2f), new Vector2(.5f, .5f), emblemPos);
                _halo.raycastTarget = false;
            }

            // The owner's letter, aspect kept inside the emblem's box. **Global art, read through
            // `Peek`**: it is in the boot preload, so it is in hand; were it ever not (an Editor run
            // before Sync All Assets), the picture stays off and the letter is set in type in its
            // colour instead - never a white rectangle (7b).
            var emblem = UIKit.Box("Emblem", Root, new Vector2(emblemW, emblemH), new Vector2(.5f, .5f), emblemPos);
            _emblem = emblem;

            var sprite = AssetPipeline.AssetLibrary.Peek<Sprite>(
                AssetPipeline.AssetManifest.ArtRoot + AssetPipeline.AssetManifest.ShuffleGrade(grade));

            var picture = UIKit.Img("Letter", emblem, sprite, Color.white, new Vector2(emblemW, emblemH),
                                    new Vector2(.5f, .5f), Vector2.zero);
            picture.preserveAspect = true;
            picture.raycastTarget = false;
            picture.enabled = sprite != null;

            Text typedLetter = null;
            if (sprite == null)
            {
                var letter = typedLetter = UIKit.Titled("Type", emblem, ShuffleGradeLook.Letter(grade),
                                          Mathf.RoundToInt(_at.LetterSize * scale), ink,
                                          TextAnchor.MiddleCenter, new Vector2(emblemW, emblemH * .7f),
                                          new Vector2(.5f, .5f), new Vector2(0f, _at.LetterLift * scale), 5f, 4f);
                UIKit.Shrinkable(letter, _at.LetterFloor);
            }

            // ------------------------------------------------------------ the score
            _caption = Line("Caption", Loc.Get("ui.shuffle.score").Upper(), _at.CaptionY, _at.CaptionH,
                            _at.CaptionSize, _at.CaptionFloor, captionInk ?? new Color(1f, .95f, .86f, .66f));

            string points = (record != null ? record.Points : 0).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            _score = Line("Score", points, _at.ScoreY, _at.ScoreH, _at.ScoreSize, _at.ScoreFloor, Pal.Cream);

            if (!string.IsNullOrEmpty(note))
                _note = Line("Note", note, _at.NoteY, _at.NoteH, _at.NoteSize, _at.NoteFloor, noteInk);

            // **The letter and the column are centred as one group**, by the widths they actually
            // draw (the owner, 2026-10-15: the score was not centred). The layout's x figures are
            // the envelope the group may use; where it lands inside is measured: the letter's drawn
            // width beside the widest of the three lines, `ReportGap` apart, the pair centred.
            float letterW = typedLetter != null ? Drawn(typedLetter) : Fitted(sprite, emblemW, emblemH);
            float columnW = Mathf.Min(_at.ColumnW, Mathf.Max(Drawn(_caption), Mathf.Max(Drawn(_score), _note ? Drawn(_note) : 0f)));
            var xs = ShuffleReportLayout.CentredPair(_at.GroupGap, letterW, columnW);

            emblem.anchoredPosition = new Vector2(xs[0], _at.EmblemY);
            if (_halo) ((RectTransform)_halo.transform).anchoredPosition = new Vector2(xs[0], _at.EmblemY);

            // Each line keeps its left-aligned box, moved so its left edge is the column's.
            float left = xs[1] - columnW * .5f;
            foreach (var line in new[] { _caption, _score, _note })
                if (line) ((RectTransform)line.transform).anchoredPosition =
                    new Vector2(left + _at.ColumnW * .5f, ((RectTransform)line.transform).anchoredPosition.y);

            // ------------------------------------------------------------ the tiles
            for (int i = 0; i < _shown.Count; i++)
            {
                var stat = _shown[i];
                record.TryGet(stat.Id, out int value);

                var (x, y) = _at.TileCentre(i, _shown.Count);
                var tile = UIKit.Img("Stat_" + stat.Id, Root, global::GlimmerGrove.Art.S("Ui/" + RankKit.Row), Color.white,
                                     new Vector2(_at.TileW, _at.TileH), new Vector2(.5f, .5f), new Vector2(x, y));
                tile.raycastTarget = false;
                var rt = (RectTransform)tile.transform;

                var icon = UIKit.Img("Icon", rt, null, Color.white, new Vector2(_at.IconSize, _at.IconSize),
                                     new Vector2(.5f, .5f), new Vector2(_at.IconX, 0f));
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                icon.enabled = false;
                _icons.Add(icon);

                UIKit.Shrinkable(UIKit.Titled("Figure", rt, ShuffleStats.Say(stat.Kind, value), _at.FigureSize, Pal.Cream,
                                              TextAnchor.MiddleLeft, new Vector2(_at.TextW, _at.FigureH),
                                              new Vector2(.5f, .5f), new Vector2(_at.TextX, _at.FigureY), 3f, 2f),
                                 _at.FigureFloor);

                UIKit.Shrinkable(UIKit.Titled("Label", rt, Loc.Get(stat.NameKey), _at.LabelSize,
                                              new Color(1f, .95f, .86f, .7f), TextAnchor.MiddleLeft,
                                              new Vector2(_at.TextW, _at.LabelH), new Vector2(.5f, .5f),
                                              new Vector2(_at.TextX, _at.LabelY), 2f, 0f),
                                 _at.LabelFloor);

                _tiles.Add(rt);
            }

            Repaint();

            if (!hidden)
            {
                if (_halo) Tween.Breathe(_halo.transform, .06f, 2.6f);
                return;
            }

            _emblem.localScale = Vector3.zero;
            if (_halo) _halo.transform.localScale = Vector3.zero;
            foreach (var t in new[] { _caption, _score, _note }) if (t) t.transform.localScale = Vector3.zero;
            foreach (var tile in _tiles) tile.localScale = Vector3.zero;
        }

        Text Line(string name, string text, float y, float h, int size, int floor, Color ink)
        {
            var t = UIKit.Titled(name, Root, text, size, ink, TextAnchor.MiddleLeft,
                                 new Vector2(_at.ColumnW, h), new Vector2(.5f, .5f),
                                 new Vector2(_at.ColumnX, y), 4f, 3f);
            UIKit.Shrinkable(t, floor);
            return t;
        }

        /// <summary>
        /// How wide <paramref name="text"/> draws at its own size, outline included - what a
        /// centred group is measured by. Its box is a ceiling, never a measure.
        /// </summary>
        public static float Drawn(Text text)
        {
            if (!text) return 0f;

            var outline = text.GetComponent<Outline>();
            float rim = outline != null ? Mathf.Abs(outline.effectDistance.x) * 2f : 0f;
            return Mathf.Min(((RectTransform)text.transform).rect.width, Mathf.Ceil(text.preferredWidth) + rim);
        }

        /// <summary>How wide a picture draws with its aspect kept inside a <paramref name="w"/> x <paramref name="h"/> box.</summary>
        public static float Fitted(Sprite sprite, float w, float h)
        {
            if (sprite == null || sprite.rect.height <= 0f) return w;
            return Mathf.Min(w, h * sprite.rect.width / sprite.rect.height);
        }

        /// <summary>Moves a piece's centre to (<paramref name="x"/>, <paramref name="y"/>), in its parent's layout space.</summary>
        public static void Place(Graphic piece, float x, float y)
        {
            if (piece) ((RectTransform)piece.transform).anchoredPosition = new Vector2(x, y);
        }

        /// <summary>
        /// Puts every tile's picture on once its sprite is in hand, and leaves the rest disabled.
        /// Called after building and again by a host whose scope has just landed.
        /// </summary>
        public void Repaint()
        {
            for (int i = 0; i < _icons.Count && i < _shown.Count; i++)
            {
                var icon = _icons[i];
                if (!icon) continue;

                var sprite = AssetPipeline.AssetLibrary.Peek<Sprite>(AssetPipeline.AssetManifest.ArtRoot + _shown[i].Icon);
                icon.sprite = sprite;
                icon.enabled = sprite != null;
            }
        }

        /// <summary>
        /// Walks a hidden block on, as beats on <paramref name="cue"/>: the score's caption, the
        /// letter stamped down, the score counting up, the line under it, then the tiles one by one.
        /// The top three grades are given their light and a burst as the letter lands.
        /// </summary>
        public void Play(Cue cue, MonoBehaviour owner)
        {
            if (cue == null || Root == null) return;

            var grade = _record != null ? _record.Grade : ShuffleGrade.D;
            var ink = ShuffleGradeLook.Ink(grade);
            int points = _record != null ? _record.Points : 0;

            cue.Then(.10f, () => { if (_caption) Tween.Pop(_caption.transform, 0f, .35f); });

            cue.Then(.12f, () =>
            {
                if (!_score) return;
                Tween.Pop(_score.transform, 0f, .35f);

                var invariant = System.Globalization.CultureInfo.InvariantCulture;
                var score = _score;
                Tween.Value(0f, points, .8f, v =>
                {
                    if (score) score.text = Mathf.RoundToInt(v).ToString("N0", invariant);
                }, Ease.OutCubic, owner);
            });

            // The letter is stamped: down hard from oversize, the gesture the record seal uses.
            cue.Then(.62f, () =>
            {
                if (!_emblem) return;
                _emblem.localScale = Vector3.one * 2f;
                Tween.Scale(_emblem, 1f, .28f, Ease.InCubic).OnDone(() =>
                {
                    if (!_emblem) return;
                    Tween.Punch(_emblem, .16f, .32f);
                    Audio.Sfx(ShuffleGrades.IsTop(grade) ? "unlock" : "tick", ShuffleGrades.IsTop(grade) ? .8f : .5f);

                    if (_halo)
                    {
                        Tween.Pop(_halo.transform, 0f, .5f).OnDone(() => { if (_halo) Tween.Breathe(_halo.transform, .06f, 2.6f); });
                    }

                    if (ShuffleGrades.IsTop(grade))
                    {
                        Burst.Sparks(_emblem, Vector2.zero, ink, grade == ShuffleGrade.SPlusPlus ? 34 : 22, 320f, 26f, .8f);
                        if (grade == ShuffleGrade.SPlusPlus) Flow.Flash(new Color(1f, .97f, .86f), .32f, .6f);
                    }
                });
            });

            if (_note) cue.Then(.36f, () => { if (_note) Tween.Pop(_note.transform, 0f, .4f); });

            for (int i = 0; i < _tiles.Count; i++)
            {
                var tile = _tiles[i];
                cue.Then(i == 0 ? .2f : .08f, () => { if (tile) Tween.Pop(tile, 0f, .34f); });
            }
        }
    }
}
