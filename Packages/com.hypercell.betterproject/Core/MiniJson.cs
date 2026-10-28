using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BetterProject.Core
{
    /// <summary>
    /// Minimal strict JSON reader. Returns Dictionary&lt;string, object&gt;, List&lt;object&gt;,
    /// string, double, bool or null. Throws FormatException on any malformed input.
    /// </summary>
    internal static class MiniJson
    {
        const int MaxDepth = 64;

        public static object Parse(string json)
        {
            if (json == null) throw new FormatException("JSON is null.");
            var p = new Parser(json);
            p.SkipWs();
            var v = p.ReadValue(0);
            p.SkipWs();
            if (!p.AtEnd) throw p.Error("Unexpected trailing content");
            return v;
        }

        sealed class Parser
        {
            readonly string _s;
            int _i;

            public Parser(string s) { _s = s; }

            public bool AtEnd => _i >= _s.Length;

            public FormatException Error(string msg) => new FormatException(msg + " at position " + _i + ".");

            public void SkipWs()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') _i++;
                    else break;
                }
            }

            public object ReadValue(int depth)
            {
                if (depth > MaxDepth) throw Error("Nesting too deep");
                if (AtEnd) throw Error("Unexpected end of input");
                char c = _s[_i];
                switch (c)
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return ReadString();
                    case 't': ExpectWord("true"); return true;
                    case 'f': ExpectWord("false"); return false;
                    case 'n': ExpectWord("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw Error("Unexpected character '" + c + "'");
                }
            }

            void ExpectWord(string w)
            {
                if (string.CompareOrdinal(_s, _i, w, 0, w.Length) != 0) throw Error("Invalid literal");
                _i += w.Length;
            }

            object ReadObject(int depth)
            {
                var d = new Dictionary<string, object>();
                _i++; // {
                SkipWs();
                if (!AtEnd && _s[_i] == '}') { _i++; return d; }
                while (true)
                {
                    SkipWs();
                    if (AtEnd || _s[_i] != '"') throw Error("Expected string key");
                    string key = ReadString();
                    SkipWs();
                    if (AtEnd || _s[_i] != ':') throw Error("Expected ':'");
                    _i++;
                    SkipWs();
                    d[key] = ReadValue(depth + 1);
                    SkipWs();
                    if (AtEnd) throw Error("Unterminated object");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == '}') { _i++; return d; }
                    throw Error("Expected ',' or '}'");
                }
            }

            object ReadArray(int depth)
            {
                var l = new List<object>();
                _i++; // [
                SkipWs();
                if (!AtEnd && _s[_i] == ']') { _i++; return l; }
                while (true)
                {
                    SkipWs();
                    l.Add(ReadValue(depth + 1));
                    SkipWs();
                    if (AtEnd) throw Error("Unterminated array");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == ']') { _i++; return l; }
                    throw Error("Expected ',' or ']'");
                }
            }

            string ReadString()
            {
                _i++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw Error("Unterminated string");
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c < 0x20) throw Error("Control character in string");
                    if (c != '\\') { sb.Append(c); continue; }
                    if (AtEnd) throw Error("Unterminated escape");
                    char e = _s[_i++];
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
                            if (_i + 4 > _s.Length) throw Error("Bad unicode escape");
                            int code = 0;
                            for (int k = 0; k < 4; k++)
                            {
                                int h = HexVal(_s[_i + k]);
                                if (h < 0) throw Error("Bad unicode escape");
                                code = code * 16 + h;
                            }
                            _i += 4;
                            sb.Append((char)code);
                            break;
                        default: throw Error("Bad escape '\\" + e + "'");
                    }
                }
            }

            static int HexVal(char c)
            {
                if (c >= '0' && c <= '9') return c - '0';
                if (c >= 'a' && c <= 'f') return c - 'a' + 10;
                if (c >= 'A' && c <= 'F') return c - 'A' + 10;
                return -1;
            }

            object ReadNumber()
            {
                int start = _i;
                if (_s[_i] == '-') _i++;
                while (_i < _s.Length && "0123456789.eE+-".IndexOf(_s[_i]) >= 0) _i++;
                var tok = _s.Substring(start, _i - start);
                if (!double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                    || double.IsInfinity(d) || double.IsNaN(d))
                    throw Error("Bad number");
                return d;
            }
        }
    }
}
