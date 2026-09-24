using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BlueprintsUi.Editor
{
    /// <summary>
    /// Minimal JSON reader for the spec files: objects become Dictionary&lt;string, object&gt; (key
    /// order kept), arrays List&lt;object&gt;, integers long, other numbers double. Kept in-tree so
    /// the project needs no package beyond uGUI.
    /// </summary>
    public static class Json
    {
        public static object Parse(string text)
        {
            int i = 0;
            object value = ReadValue(text, ref i);
            SkipWhitespace(text, ref i);
            if (i != text.Length)
                throw new FormatException($"trailing data at {i}");
            return value;
        }

        static object ReadValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            switch (s[i])
            {
                case '{': return ReadObject(s, ref i);
                case '[': return ReadArray(s, ref i);
                case '"': return ReadString(s, ref i);
                case 't': Expect(s, ref i, "true"); return true;
                case 'f': Expect(s, ref i, "false"); return false;
                case 'n': Expect(s, ref i, "null"); return null;
                default: return ReadNumber(s, ref i);
            }
        }

        static Dictionary<string, object> ReadObject(string s, ref int i)
        {
            var result = new Dictionary<string, object>();
            i++;
            SkipWhitespace(s, ref i);
            if (s[i] == '}') { i++; return result; }
            while (true)
            {
                SkipWhitespace(s, ref i);
                string key = ReadString(s, ref i);
                SkipWhitespace(s, ref i);
                if (s[i++] != ':')
                    throw new FormatException($"expected ':' at {i - 1}");
                result[key] = ReadValue(s, ref i);
                SkipWhitespace(s, ref i);
                char c = s[i++];
                if (c == '}') return result;
                if (c != ',')
                    throw new FormatException($"expected ',' or '}}' at {i - 1}");
            }
        }

        static List<object> ReadArray(string s, ref int i)
        {
            var result = new List<object>();
            i++;
            SkipWhitespace(s, ref i);
            if (s[i] == ']') { i++; return result; }
            while (true)
            {
                result.Add(ReadValue(s, ref i));
                SkipWhitespace(s, ref i);
                char c = s[i++];
                if (c == ']') return result;
                if (c != ',')
                    throw new FormatException($"expected ',' or ']' at {i - 1}");
            }
        }

        static string ReadString(string s, ref int i)
        {
            if (s[i] != '"')
                throw new FormatException($"expected string at {i}");
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                        i += 4;
                        break;
                    default: throw new FormatException($"bad escape at {i - 1}");
                }
            }
        }

        static object ReadNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0)
                i++;
            string token = s.Substring(start, i - start);
            if (token.IndexOfAny(new[] { '.', 'e', 'E' }) < 0
                && long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
                return l;
            if (token == "")
                throw new FormatException($"unexpected '{s[start]}' at {start}");
            return double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static void Expect(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                throw new FormatException($"expected {word} at {i}");
            i += word.Length;
        }

        static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
                i++;
        }
    }
}
