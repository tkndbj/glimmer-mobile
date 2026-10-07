using System.Collections.Generic;
using System.Text;
using GlimmerGrove.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The one text component the game draws with (<see cref="UIKit.Label"/> adds it), and
    /// exactly uGUI's <c>Text</c> for any string with no Arabic in it.
    ///
    /// <para>
    /// <b>What it adds is right-to-left Arabic.</b> The legacy <c>Text</c> cannot shape or reorder
    /// (<see cref="ArabicText"/> says why), and it cannot be handed pre-reordered text either when
    /// it wraps: it would break the reversed string into lines and draw the paragraph's
    /// <em>last</em> line on top. So for an Arabic string this does the layout in the order
    /// Arabic needs - shape, find the line breaks on the shaped text at the size the label will
    /// draw at, reverse each line - and hands the generator lines that already fit, with its own
    /// wrapping off. Everything else (alignment, best fit's size, outline, shadow, colour) is the
    /// stock path's.
    /// </para>
    /// <para>
    /// <b><c>text</c> stays logical.</b> Callers read back and compare what they set, and the
    /// string never changes on its way in; only the copy handed to the generator is visual. The
    /// layout sizes (<see cref="preferredWidth"/>, <see cref="preferredHeight"/>) measure that
    /// visual copy, because a shaped word is not the width of its isolated letters.
    /// </para>
    /// </summary>
    public sealed class GameText : Text
    {
        // The last layout, reused while nothing it depends on has changed: a label repopulates
        // on every colour tween and the wrapping pass measures once per word.
        string _laidFor;
        float _laidWidth;
        int _laidSize;
        bool _laidWrap, _laidRich;
        string _laid;
        int _laidAt;

        TextGenerator _measure;

        protected override void OnPopulateMesh(VertexHelper toFill)
        {
            if (font == null || !ArabicText.Needs(m_Text))
            {
                base.OnPopulateMesh(toFill);
                return;
            }

            m_DisableFontTextureRebuiltCallback = true;
            try
            {
                Vector2 extents = rectTransform.rect.size;
                var settings = GetGenerationSettings(extents);
                string visual = Layout(settings, extents.x, out int drawnAt);

                if (settings.horizontalOverflow == HorizontalWrapMode.Wrap)
                {
                    // The lines already fit; the generator must not break them again, and best
                    // fit has already chosen its size on the shaped text.
                    settings.horizontalOverflow = HorizontalWrapMode.Overflow;
                    settings.resizeTextForBestFit = false;
                    settings.fontSize = drawnAt;
                }

                cachedTextGenerator.PopulateWithErrors(visual, settings, gameObject);
                Emit(toFill);
            }
            finally
            {
                m_DisableFontTextureRebuiltCallback = false;
            }
        }

        public override float preferredWidth
        {
            get
            {
                if (!ArabicText.Needs(m_Text)) return base.preferredWidth;
                var settings = GetGenerationSettings(Vector2.zero);
                return cachedTextGeneratorForLayout.GetPreferredWidth(ArabicText.Visual(m_Text, supportRichText), settings)
                     / pixelsPerUnit;
            }
        }

        public override float preferredHeight
        {
            get
            {
                if (!ArabicText.Needs(m_Text)) return base.preferredHeight;
                float width = GetPixelAdjustedRect().size.x;
                var settings = GetGenerationSettings(new Vector2(width, 0f));
                string visual = Layout(settings, width, out int drawnAt);
                if (settings.horizontalOverflow == HorizontalWrapMode.Wrap)
                {
                    settings.horizontalOverflow = HorizontalWrapMode.Overflow;
                    settings.resizeTextForBestFit = false;
                    settings.fontSize = drawnAt;
                }
                return cachedTextGeneratorForLayout.GetPreferredHeight(visual, settings) / pixelsPerUnit;
            }
        }

        /// <summary>
        /// The string handed to the generator: shaped, broken into lines that fit
        /// <paramref name="width"/> when the label wraps, each line in visual order.
        /// </summary>
        string Layout(TextGenerationSettings settings, float width, out int drawnAt)
        {
            bool wrap = settings.horizontalOverflow == HorizontalWrapMode.Wrap && width > 0f;
            bool rich = settings.richText;

            if (_laid != null && _laidFor == m_Text && _laidWrap == wrap && _laidRich == rich
                && (!wrap || (Mathf.Approximately(_laidWidth, width) && _laidSize == SizeKey(settings))))
            {
                drawnAt = _laidAt;
                return _laid;
            }

            string shaped = ArabicText.Shape(m_Text);
            drawnAt = settings.fontSize;
            List<string> lines;

            if (!wrap) lines = new List<string>(shaped.Split('\n'));
            else
            {
                if (settings.resizeTextForBestFit)
                {
                    // Best fit's own answer on the shaped text, with its own wrapping: the size the
                    // stock path would have drawn this label at.
                    Measure.Populate(shaped, settings);
                    drawnAt = Measure.fontSizeUsedForBestFit;
                }
                lines = Wrap(shaped, settings, width, drawnAt);
            }

            var sb = new StringBuilder(shaped.Length + lines.Count);
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(ArabicText.Reorder(lines[i], rich));
            }

            _laidFor = m_Text; _laidWidth = width; _laidSize = SizeKey(settings);
            _laidWrap = wrap; _laidRich = rich; _laidAt = drawnAt;
            _laid = sb.ToString();
            return _laid;
        }

        static int SizeKey(TextGenerationSettings s)
            => (s.resizeTextForBestFit ? -(s.resizeTextMaxSize * 1000 + s.resizeTextMinSize) : s.fontSize) * 8
             + (int)s.fontStyle;

        /// <summary>
        /// Greedy word wrap on the shaped (logical-order) text, measured by the font itself so
        /// fallback glyphs and rich-text tags count exactly as they draw. A line breaks at a
        /// space, as uGUI's own wrap does; a word wider than the line stands alone on it.
        /// </summary>
        List<string> Wrap(string shaped, TextGenerationSettings settings, float width, int size)
        {
            var single = settings;
            single.horizontalOverflow = HorizontalWrapMode.Overflow;
            single.verticalOverflow = VerticalWrapMode.Overflow;
            single.resizeTextForBestFit = false;
            single.fontSize = size;
            single.generationExtents = Vector2.zero;

            // A pixel of slack, so a line measured to fit exactly cannot be broken again by a
            // rounding difference in the second populate.
            float room = width * pixelsPerUnit - 1f;
            var lines = new List<string>();

            foreach (string paragraph in shaped.Split('\n'))
            {
                string[] words = paragraph.Split(' ');
                string line = words[0];
                for (int w = 1; w < words.Length; w++)
                {
                    string candidate = line + " " + words[w];
                    if (Measure.GetPreferredWidth(candidate, single) <= room) line = candidate;
                    else
                    {
                        lines.Add(line);
                        line = words[w];
                    }
                }
                lines.Add(line);
            }
            return lines;
        }

        TextGenerator Measure => _measure ??= new TextGenerator();

        // The stock OnPopulateMesh's vertex pass, verbatim: pixel-snapped from the first vertex.
        readonly UIVertex[] _quad = new UIVertex[4];

        void Emit(VertexHelper toFill)
        {
            IList<UIVertex> verts = cachedTextGenerator.verts;
            float unitsPerPixel = 1f / pixelsPerUnit;
            int count = verts.Count;

            toFill.Clear();
            if (count <= 0) return;

            Vector2 rounding = new Vector2(verts[0].position.x, verts[0].position.y) * unitsPerPixel;
            rounding = PixelAdjustPoint(rounding) - rounding;

            for (int i = 0; i < count; ++i)
            {
                int q = i & 3;
                _quad[q] = verts[i];
                _quad[q].position *= unitsPerPixel;
                _quad[q].position.x += rounding.x;
                _quad[q].position.y += rounding.y;
                if (q == 3) toFill.AddUIVertexQuad(_quad);
            }
        }
    }
}
