using System.Collections.Generic;
using System.Text;

namespace GlimmerGrove.Localization
{
    /// <summary>
    /// Turns Arabic as it is stored (logical order, one code point per letter) into Arabic as
    /// uGUI's legacy <c>Text</c> can draw it (visual order, one fixed shape per letter).
    ///
    /// <para>
    /// <b>Why the game does this and not the renderer.</b> The legacy <c>Text</c> draws one glyph
    /// per code point, left to right, and runs no shaper. Arabic joins: each letter has up to four
    /// shapes chosen by its neighbours, lam and alef fuse, and the line reads right to left with
    /// numbers and Latin words still reading left to right inside it. Handed raw, a sentence draws
    /// as isolated letters in reverse. So <see cref="Shape"/> picks each letter's presentation
    /// form (the code points <c>GameFontArabic</c> was cut to draw, from <see cref="ArabicForms"/>)
    /// and <see cref="Reorder"/> lays one line out in visual order.
    /// </para>
    /// <para>
    /// <b>Only where the game itself draws.</b> Strings stay logical everywhere else - in
    /// <c>Loc</c>, in the save, in a notification, in the iOS plist, in the share sheet - because
    /// every one of those is drawn by an OS that shapes Arabic itself, and a pre-shaped string
    /// there would draw backwards. The one caller is the text component (<c>GameText</c>), which
    /// also owns line breaking: lines must be found on the shaped text <em>before</em> each is
    /// reversed, or a wrapped paragraph reads bottom line first.
    /// </para>
    /// <para>
    /// <b>The ordering is the Unicode Bidirectional Algorithm cut to what a one-paragraph,
    /// right-to-left, embedding-free UI line needs</b>: W4 (a lone separator between digits joins
    /// the number), W5 (a currency or percent sign joins it), W7 (digits after Latin are Latin),
    /// N1/N2 (a neutral takes the direction of the strong text on both sides, else the line's),
    /// L2 (reverse) and L4 (mirror brackets). One deliberate departure: a sign directly before a
    /// digit joins the number, so "+20" draws as "+20" and never as "20+".
    /// </para>
    /// <para>
    /// A string with no Arabic in it is never touched (<see cref="Needs"/>), so every other
    /// language draws exactly as it did. <c>Tools/arabic_text.py</c> mirrors this file for the
    /// proof renders; <c>Tools/verify/arabic-vectors.json</c> holds both to the same answers.
    /// </para>
    /// </summary>
    public static class ArabicText
    {
        const int Lam = 0x0644, Tatweel = 0x0640, Zwnj = 0x200C, Zwj = 0x200D;

        /// <summary>Whether <paramref name="text"/> holds anything this class would change.</summary>
        public static bool Needs(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text)
                if (IsArabic(c)) return true;
            return false;
        }

        /// <summary>
        /// Every line of <paramref name="logical"/> shaped and reordered, line breaks kept. For text
        /// that will not be wrapped; a wrapped paragraph must be broken between the two halves.
        /// </summary>
        public static string Visual(string logical, bool richText = false)
        {
            if (!Needs(logical)) return logical ?? string.Empty;

            string shaped = Shape(logical);
            if (shaped.IndexOf('\n') < 0) return Reorder(shaped, richText);

            var lines = shaped.Split('\n');
            for (int i = 0; i < lines.Length; i++) lines[i] = Reorder(lines[i], richText);
            return string.Join("\n", lines);
        }

        // ================================================================= shaping

        enum Joining { None, Right, Dual, Causing, Transparent }

        static Joining JoiningOf(int c)
        {
            if (ArabicForms.Letters.TryGetValue(c, out var forms))
                return forms[2] != 0 ? Joining.Dual : Joining.Right;
            if (c == Tatweel || c == Zwj) return Joining.Causing;
            if (IsMark(c)) return Joining.Transparent;
            return Joining.None;
        }

        /// <summary>
        /// Harakat and Quranic marks. The legacy renderer cannot place a mark over its letter - it
        /// would draw beside it - so they are dropped, which is how everyday Arabic is written anyway.
        /// </summary>
        static bool IsMark(int c)
            => (c >= 0x064B && c <= 0x065F) || c == 0x0670 || (c >= 0x06D6 && c <= 0x06ED && c != 0x06E5 && c != 0x06E6);

        /// <summary>
        /// Each letter replaced by the shape its neighbours ask for, still in logical order. Marks,
        /// ZWJ and ZWNJ are consumed; anything not Arabic passes through untouched.
        /// </summary>
        public static string Shape(string logical)
        {
            if (!Needs(logical)) return logical ?? string.Empty;

            // Marks are transparent to joining, so they are taken out first and the neighbours
            // a letter sees are the letters either side of it.
            var chars = new List<int>(logical.Length);
            foreach (char c in logical)
                if (JoiningOf(c) != Joining.Transparent) chars.Add(c);

            var sb = new StringBuilder(chars.Count);
            for (int i = 0; i < chars.Count; i++)
            {
                int c = chars[i];
                var self = JoiningOf(c);
                var before = i > 0 ? JoiningOf(chars[i - 1]) : Joining.None;
                var after = i + 1 < chars.Count ? JoiningOf(chars[i + 1]) : Joining.None;

                bool joinsBefore = self != Joining.None && (before == Joining.Dual || before == Joining.Causing);

                if (c == Lam && i + 1 < chars.Count && ArabicForms.LamAlef.TryGetValue(chars[i + 1], out var lig))
                {
                    sb.Append((char)(joinsBefore ? lig[1] : lig[0]));
                    i++;
                    continue;
                }

                if (c == Zwj || c == Zwnj) continue;

                if (!ArabicForms.Letters.TryGetValue(c, out var forms))
                {
                    sb.Append((char)c);
                    continue;
                }

                bool joinsAfter = self == Joining.Dual
                               && (after == Joining.Dual || after == Joining.Right || after == Joining.Causing);

                int form = joinsBefore && joinsAfter ? forms[3]
                         : joinsBefore ? forms[1]
                         : joinsAfter ? forms[2]
                         : forms[0];
                sb.Append((char)form);
            }
            return sb.ToString();
        }

        // ================================================================ ordering

        enum Kind { Rtl, Ltr, Number, NumberSign, Separator, Neutral, Tag }

        struct Unit
        {
            public string Text;
            public Kind Kind;
            public int Level;    // 1 = right to left, 2 = left to right inside it
        }

        static bool IsArabic(char c)
            => (c >= 0x0600 && c <= 0x06FF) || (c >= 0x0750 && c <= 0x077F) || (c >= 0x08A0 && c <= 0x08FF)
            || (c >= 0xFB50 && c <= 0xFDFF) || (c >= 0xFE70 && c <= 0xFEFF);

        static Kind KindOf(char c)
        {
            if ((c >= '0' && c <= '9') || (c >= 0x0660 && c <= 0x0669) || (c >= 0x06F0 && c <= 0x06F9)) return Kind.Number;
            if (c == 0x060C) return Kind.Separator;                        // Arabic comma
            if (IsArabic(c)) return Kind.Rtl;
            if (c == '+' || c == '-' || c == 0x2212) return Kind.NumberSign;
            if (c == '%' || c == 0x066A || c == '$' || c == 0x20AC || c == 0x00A3 || c == '#' || c == 0x00B0)
                return Kind.NumberSign;
            if (c == ',' || c == '.' || c == ':' || c == '/' || c == 0x066B || c == 0x066C) return Kind.Separator;
            if (char.IsLetter(c)) return Kind.Ltr;
            return Kind.Neutral;
        }

        /// <summary>
        /// One line (no <c>\n</c>), already shaped, in the order it is drawn left to right. With
        /// <paramref name="richText"/>, uGUI's tags are moved as whole units and each pair is
        /// re-opened on its new left side, so <c>&lt;b&gt;</c> still precedes <c>&lt;/b&gt;</c>.
        /// </summary>
        public static string Reorder(string line, bool richText = false)
        {
            if (!Needs(line)) return line ?? string.Empty;

            var units = Split(line, richText);
            Resolve(units);

            // L2: levels are only 1 and 2 here, so reverse every run at 2, then the whole line.
            int at = 0;
            while (at < units.Count)
            {
                if (units[at].Level != 2) { at++; continue; }
                int end = at;
                while (end + 1 < units.Count && units[end + 1].Level == 2) end++;
                Reverse(units, at, end);
                at = end + 1;
            }
            Reverse(units, 0, units.Count - 1);

            if (richText) RepairTags(units);

            var sb = new StringBuilder(line.Length);
            foreach (var u in units)
                sb.Append(u.Level == 1 && u.Text.Length == 1 ? Mirror(u.Text[0]).ToString() : u.Text);
            return sb.ToString();
        }

        static List<Unit> Split(string line, bool richText)
        {
            var units = new List<Unit>(line.Length);
            for (int i = 0; i < line.Length; i++)
            {
                int tagEnd = richText && line[i] == '<' ? TagEnd(line, i) : -1;
                if (tagEnd > i)
                {
                    units.Add(new Unit { Text = line.Substring(i, tagEnd - i + 1), Kind = Kind.Tag });
                    i = tagEnd;
                    continue;
                }
                units.Add(new Unit { Text = line[i].ToString(), Kind = KindOf(line[i]) });
            }
            return units;
        }

        /// <summary>The index of the <c>&gt;</c> closing a uGUI tag that opens at <paramref name="at"/>, or -1.</summary>
        static int TagEnd(string line, int at)
        {
            int close = line.IndexOf('>', at + 1);
            if (close < 0) return -1;
            string name = TagName(line.Substring(at, close - at + 1));
            return name == "b" || name == "i" || name == "size" || name == "color" ? close : -1;
        }

        static string TagName(string tag)
        {
            int start = tag.Length > 1 && tag[1] == '/' ? 2 : 1;
            int end = start;
            while (end < tag.Length && tag[end] != '=' && tag[end] != '>') end++;
            return tag.Substring(start, end - start);
        }

        static void Resolve(List<Unit> units)
        {
            var kinds = new Kind[units.Count];
            for (int i = 0; i < units.Count; i++) kinds[i] = units[i].Kind;

            // Tags are invisible to every rule below (X9): each rule looks past them.
            int Prev(int i) { do i--; while (i >= 0 && kinds[i] == Kind.Tag); return i; }
            int Next(int i) { do i++; while (i < kinds.Length && kinds[i] == Kind.Tag); return i; }

            // W4: one separator between two digits is part of the number ("1,240", "3/5", "12:30").
            for (int i = 0; i < kinds.Length; i++)
            {
                if (kinds[i] != Kind.Separator) continue;
                int p = Prev(i), n = Next(i);
                if (p >= 0 && n < kinds.Length && kinds[p] == Kind.Number && kinds[n] == Kind.Number)
                    kinds[i] = Kind.Number;
            }

            // W5 and the sign rule: a run of signs touching a number joins it.
            for (int i = 0; i < kinds.Length; i++)
            {
                if (kinds[i] != Kind.NumberSign) continue;
                int end = i;
                while (Next(end) < kinds.Length && kinds[Next(end)] == Kind.NumberSign) end = Next(end);
                int p = Prev(i), n = Next(end);
                bool touches = (p >= 0 && kinds[p] == Kind.Number) || (n < kinds.Length && kinds[n] == Kind.Number);
                for (int k = i; k <= end; k++)
                    if (kinds[k] == Kind.NumberSign) kinds[k] = touches ? Kind.Number : Kind.Neutral;
                i = end;
            }

            for (int i = 0; i < kinds.Length; i++)
                if (kinds[i] == Kind.Separator) kinds[i] = Kind.Neutral;

            // W7: a number whose nearest strong neighbour behind it is Latin is Latin ("Level 5").
            Kind strong = Kind.Rtl;
            for (int i = 0; i < kinds.Length; i++)
            {
                if (kinds[i] == Kind.Rtl || kinds[i] == Kind.Ltr) strong = kinds[i];
                else if (kinds[i] == Kind.Number && strong == Kind.Ltr) kinds[i] = Kind.Ltr;
            }

            // N1/N2: a run of neutrals between Latin on both sides is Latin; anything else takes
            // the line's direction. Numbers count as right to left for this, as the algorithm says.
            for (int i = 0; i < kinds.Length; i++)
            {
                if (kinds[i] != Kind.Neutral) continue;
                int end = i;
                while (end + 1 < kinds.Length && (kinds[end + 1] == Kind.Neutral || kinds[end + 1] == Kind.Tag)) end++;
                int p = Prev(i), n = Next(end);
                bool ltr = p >= 0 && n < kinds.Length && kinds[p] == Kind.Ltr && kinds[n] == Kind.Ltr;
                for (int k = i; k <= end; k++)
                    if (kinds[k] == Kind.Neutral) kinds[k] = ltr ? Kind.Ltr : Kind.Rtl;
                i = end;
            }

            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (kinds[i] == Kind.Tag)
                {
                    // A tag sits at the level of what it wraps, so it travels with that text.
                    int n = Next(i), p = Prev(i);
                    var at = u.Text[1] == '/' ? (p >= 0 ? kinds[p] : Kind.Rtl) : (n < kinds.Length ? kinds[n] : Kind.Rtl);
                    u.Level = at == Kind.Rtl ? 1 : 2;
                }
                else u.Level = kinds[i] == Kind.Rtl ? 1 : 2;
                units[i] = u;
            }

            // L1: whitespace at the end of a line returns to the line's level.
            for (int i = units.Count - 1; i >= 0 && (units[i].Text == " " || units[i].Kind == Kind.Tag); i--)
            {
                if (units[i].Kind == Kind.Tag) continue;
                var u = units[i]; u.Level = 1; units[i] = u;
            }
        }

        static void Reverse(List<Unit> units, int from, int to)
        {
            while (from < to)
            {
                var t = units[from]; units[from] = units[to]; units[to] = t;
                from++; to--;
            }
        }

        /// <summary>
        /// After reversal a closing tag can land left of its opener. Tags are paired again by
        /// name, nearest first, in visual order, and of each pair the left one is made the opener.
        /// </summary>
        static void RepairTags(List<Unit> units)
        {
            var stack = new List<int>();
            for (int i = 0; i < units.Count; i++)
            {
                if (units[i].Kind != Kind.Tag) continue;
                string name = TagName(units[i].Text);
                int match = -1;
                for (int s = stack.Count - 1; s >= 0; s--)
                    if (TagName(units[stack[s]].Text) == name) { match = s; break; }

                if (match >= 0 && IsCloser(units[stack[match]].Text) && !IsCloser(units[i].Text))
                {
                    // Visual "</b> ... <b>": swap the two texts so the left one opens.
                    int left = stack[match];
                    var a = units[left]; var b = units[i];
                    string t = a.Text; a.Text = b.Text; b.Text = t;
                    units[left] = a; units[i] = b;
                    stack.RemoveAt(match);
                }
                else if (match >= 0 && !IsCloser(units[stack[match]].Text) && IsCloser(units[i].Text))
                    stack.RemoveAt(match);
                else stack.Add(i);
            }
        }

        static bool IsCloser(string tag) => tag.Length > 1 && tag[1] == '/';

        static char Mirror(char c)
        {
            switch (c)
            {
                case '(': return ')';
                case ')': return '(';
                case '[': return ']';
                case ']': return '[';
                case '{': return '}';
                case '}': return '{';
                case '<': return '>';
                case '>': return '<';
                case '«': return '»';
                case '»': return '«';
                case '‹': return '›';
                case '›': return '‹';
                default: return c;
            }
        }
    }
}
