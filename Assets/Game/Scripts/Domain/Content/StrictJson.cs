using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// A JSON reader that says what a document <em>does not</em> hold, for a document read off
    /// the network that the game must be able to refuse.
    ///
    /// <para>
    /// <b>Why not <c>JsonUtility</c>.</b> It writes nought into a field the text never named and
    /// drops a key it does not know without a word, so "the figure is nought" and "nobody wrote
    /// the figure" read the same - which is harmless for a file the build gate has already read,
    /// and the whole problem for one an operator publishes to every phone between builds (the
    /// Shuffle tuning, <c>ShuffleTuning.TryRead</c>). This answers absent as absent, a duplicate
    /// key as a fault, a fraction as a fraction, and anything malformed as a fault with the
    /// offset, never as an empty object. It is also not an engine call, so the offline runner
    /// can read with it (invariant 29e's reason).
    /// </para>
    /// <para>
    /// <b>The shape it answers</b>: an object is a <see cref="Dictionary{TKey,TValue}"/> keyed
    /// ordinally, an array a <see cref="List{T}"/>, a number a <see cref="long"/> when written as
    /// a whole number (no fraction, no exponent) and a <see cref="double"/> otherwise, then
    /// <see cref="string"/>, <see cref="bool"/> and null. Bounded in depth so a hostile document
    /// cannot take the stack.
    /// </para>
    /// </summary>
    public static class StrictJson
    {
        /// <summary>The deepest nesting accepted. Far beyond any document this game reads.</summary>
        public const int MostDepth = 32;

        /// <summary>
        /// Parses <paramref name="text"/> whole, answering the value or the first fault. Never
        /// throws.
        /// </summary>
        public static bool TryParse(string text, out object value, out string fault)
        {
            value = null;
            fault = null;

            if (text == null)
            {
                fault = "no text";
                return false;
            }

            var reader = new Reader(text);

            try
            {
                reader.Space();
                value = reader.Value(0);
                reader.Space();

                if (!reader.AtEnd) throw reader.Fail("text after the document");
                return true;
            }
            catch (FormatException e)
            {
                value = null;
                fault = e.Message;
                return false;
            }
        }

        sealed class Reader
        {
            readonly string _text;
            int _at;

            public Reader(string text) => _text = text;

            public bool AtEnd => _at >= _text.Length;

            public FormatException Fail(string what) => new FormatException($"{what} at offset {_at}");

            public void Space()
            {
                while (_at < _text.Length)
                {
                    char c = _text[_at];
                    if (c != ' ' && c != '\t' && c != '\n' && c != '\r') return;
                    _at++;
                }
            }

            public object Value(int depth)
            {
                if (depth > MostDepth) throw Fail("nesting deeper than " + MostDepth);
                if (AtEnd) throw Fail("the document ends where a value was expected");

                char c = _text[_at];

                switch (c)
                {
                    case '{': return Object(depth);
                    case '[': return Array(depth);
                    case '"': return String();
                    case 't': Word("true"); return true;
                    case 'f': Word("false"); return false;
                    case 'n': Word("null"); return null;
                }

                if (c == '-' || (c >= '0' && c <= '9')) return Number();
                throw Fail($"unexpected '{c}'");
            }

            Dictionary<string, object> Object(int depth)
            {
                var found = new Dictionary<string, object>(StringComparer.Ordinal);
                _at++;
                Space();

                if (!AtEnd && _text[_at] == '}') { _at++; return found; }

                while (true)
                {
                    Space();
                    if (AtEnd || _text[_at] != '"') throw Fail("a key was expected");

                    string key = String();
                    if (found.ContainsKey(key)) throw Fail($"the key '{key}' is written twice");

                    Space();
                    Expect(':');
                    Space();
                    found[key] = Value(depth + 1);
                    Space();

                    if (AtEnd) throw Fail("the document ends inside an object");
                    if (_text[_at] == ',') { _at++; continue; }
                    if (_text[_at] == '}') { _at++; return found; }
                    throw Fail("',' or '}' was expected");
                }
            }

            List<object> Array(int depth)
            {
                var found = new List<object>();
                _at++;
                Space();

                if (!AtEnd && _text[_at] == ']') { _at++; return found; }

                while (true)
                {
                    Space();
                    found.Add(Value(depth + 1));
                    Space();

                    if (AtEnd) throw Fail("the document ends inside an array");
                    if (_text[_at] == ',') { _at++; continue; }
                    if (_text[_at] == ']') { _at++; return found; }
                    throw Fail("',' or ']' was expected");
                }
            }

            string String()
            {
                Expect('"');
                var built = new StringBuilder();

                while (true)
                {
                    if (AtEnd) throw Fail("the document ends inside a string");
                    char c = _text[_at++];

                    if (c == '"') return built.ToString();
                    if (c < ' ') throw Fail("a control character inside a string");

                    if (c != '\\')
                    {
                        built.Append(c);
                        continue;
                    }

                    if (AtEnd) throw Fail("the document ends inside an escape");
                    char e = _text[_at++];

                    switch (e)
                    {
                        case '"': built.Append('"'); break;
                        case '\\': built.Append('\\'); break;
                        case '/': built.Append('/'); break;
                        case 'b': built.Append('\b'); break;
                        case 'f': built.Append('\f'); break;
                        case 'n': built.Append('\n'); break;
                        case 'r': built.Append('\r'); break;
                        case 't': built.Append('\t'); break;
                        case 'u':
                            if (_at + 4 > _text.Length) throw Fail("a short \\u escape");
                            if (!int.TryParse(_text.Substring(_at, 4), NumberStyles.AllowHexSpecifier,
                                              CultureInfo.InvariantCulture, out int code))
                                throw Fail("a malformed \\u escape");
                            built.Append((char)code);
                            _at += 4;
                            break;
                        default:
                            throw Fail($"an unknown escape '\\{e}'");
                    }
                }
            }

            object Number()
            {
                int start = _at;
                bool whole = true;

                if (_text[_at] == '-') _at++;
                if (AtEnd || !Digit(_text[_at])) throw Fail("a digit was expected");

                // JSON's own rule: no leading zero on a number of more than one digit.
                if (_text[_at] == '0' && _at + 1 < _text.Length && Digit(_text[_at + 1]))
                    throw Fail("a number with a leading zero");

                while (!AtEnd && Digit(_text[_at])) _at++;

                if (!AtEnd && _text[_at] == '.')
                {
                    whole = false;
                    _at++;
                    if (AtEnd || !Digit(_text[_at])) throw Fail("a digit was expected after '.'");
                    while (!AtEnd && Digit(_text[_at])) _at++;
                }

                if (!AtEnd && (_text[_at] == 'e' || _text[_at] == 'E'))
                {
                    whole = false;
                    _at++;
                    if (!AtEnd && (_text[_at] == '+' || _text[_at] == '-')) _at++;
                    if (AtEnd || !Digit(_text[_at])) throw Fail("a digit was expected in an exponent");
                    while (!AtEnd && Digit(_text[_at])) _at++;
                }

                string written = _text.Substring(start, _at - start);

                if (whole && long.TryParse(written, NumberStyles.AllowLeadingSign,
                                           CultureInfo.InvariantCulture, out long integer))
                    return integer;

                if (double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out double real))
                    return real;

                throw Fail($"the number '{written}' cannot be read");
            }

            void Word(string word)
            {
                if (string.CompareOrdinal(_text, _at, word, 0, word.Length) != 0)
                    throw Fail($"'{word}' was expected");
                _at += word.Length;
            }

            void Expect(char c)
            {
                if (AtEnd || _text[_at] != c) throw Fail($"'{c}' was expected");
                _at++;
            }

            static bool Digit(char c) => c >= '0' && c <= '9';
        }
    }
}
