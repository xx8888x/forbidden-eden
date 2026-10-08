#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForbiddenEden.Net
{
    /// <summary>
    /// Minimal JSON reader/writer so the deployed DLL stays self-contained (the lean
    /// profile does not load Newtonsoft.Json, and JsonUtility cannot read null,
    /// dictionaries, or top-level arrays). Objects become
    /// <c>Dictionary&lt;string, object?&gt;</c>, arrays <c>List&lt;object?&gt;</c>, numbers
    /// <c>long</c> when integral and <c>double</c> otherwise.
    /// </summary>
    public static class EdenJson
    {
        public static object? Parse(string text)
        {
            var reader = new Reader(text);
            reader.SkipWhitespace();
            object? value = reader.ReadValue();
            reader.SkipWhitespace();
            if (!reader.AtEnd)
                throw reader.Error("Unexpected trailing characters");
            return value;
        }

        public static bool TryParse(string? text, out object? value)
        {
            value = null;
            if (string.IsNullOrEmpty(text))
                return false;
            try
            {
                value = Parse(text!);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public static Dictionary<string, object?>? TryParseObject(string? text) =>
            TryParse(text, out object? value) ? value as Dictionary<string, object?> : null;

        public static string Serialize(object? value)
        {
            var sb = new StringBuilder();
            Write(sb, value);
            return sb.ToString();
        }

        // ---- typed accessors -------------------------------------------------

        public static string GetString(this Dictionary<string, object?>? obj, string key, string fallback = "")
        {
            if (obj == null || !obj.TryGetValue(key, out object? v) || v == null)
                return fallback;
            return v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture) ?? fallback;
        }

        public static int GetInt(this Dictionary<string, object?>? obj, string key, int fallback = 0)
        {
            if (obj == null || !obj.TryGetValue(key, out object? v) || v == null)
                return fallback;
            switch (v)
            {
                case long l: return (int)l;
                case double d: return (int)d;
                case string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i): return i;
                default: return fallback;
            }
        }

        public static bool GetBool(this Dictionary<string, object?>? obj, string key, bool fallback = false)
        {
            if (obj == null || !obj.TryGetValue(key, out object? v) || v == null)
                return fallback;
            return v is bool b ? b : fallback;
        }

        public static Dictionary<string, object?>? GetObject(this Dictionary<string, object?>? obj, string key) =>
            obj != null && obj.TryGetValue(key, out object? v) ? v as Dictionary<string, object?> : null;

        public static List<object?>? GetList(this Dictionary<string, object?>? obj, string key) =>
            obj != null && obj.TryGetValue(key, out object? v) ? v as List<object?> : null;

        public static List<string> GetStringList(this Dictionary<string, object?>? obj, string key)
        {
            var result = new List<string>();
            List<object?>? list = obj.GetList(key);
            if (list == null)
                return result;
            foreach (object? item in list)
            {
                if (item is string s)
                    result.Add(s);
            }
            return result;
        }

        // ---- writer -----------------------------------------------------------

        private static void Write(StringBuilder sb, object? value)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    return;
                case string s:
                    WriteString(sb, s);
                    return;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    return;
                case int or long or short or byte or uint or ulong:
                    sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
                case float or double or decimal:
                    sb.Append(Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture));
                    return;
                case IDictionary dict:
                {
                    sb.Append('{');
                    bool first = true;
                    foreach (DictionaryEntry entry in dict)
                    {
                        if (!first)
                            sb.Append(',');
                        first = false;
                        WriteString(sb, Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty);
                        sb.Append(':');
                        Write(sb, entry.Value);
                    }
                    sb.Append('}');
                    return;
                }
                case IEnumerable seq:
                {
                    sb.Append('[');
                    bool first = true;
                    foreach (object? item in seq)
                    {
                        if (!first)
                            sb.Append(',');
                        first = false;
                        Write(sb, item);
                    }
                    sb.Append(']');
                    return;
                }
                default:
                    WriteString(sb, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
                    return;
            }
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---- reader -----------------------------------------------------------

        private sealed class Reader
        {
            private readonly string _s;
            private int _i;

            public Reader(string s)
            {
                _s = s;
                // Tolerate a UTF-8 BOM that survived decoding.
                if (_s.Length > 0 && _s[0] == '﻿')
                    _i = 1;
            }

            public bool AtEnd => _i >= _s.Length;

            public FormatException Error(string message) =>
                new FormatException(message + " at position " + _i.ToString(CultureInfo.InvariantCulture));

            public void SkipWhitespace()
            {
                while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\t' || _s[_i] == '\n' || _s[_i] == '\r'))
                    _i++;
            }

            public object? ReadValue()
            {
                if (AtEnd)
                    throw Error("Unexpected end of JSON");
                char c = _s[_i];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                            return ReadNumber();
                        throw Error("Unexpected character '" + c + "'");
                }
            }

            private void Expect(string literal)
            {
                if (string.CompareOrdinal(_s, _i, literal, 0, literal.Length) != 0)
                    throw Error("Expected '" + literal + "'");
                _i += literal.Length;
            }

            private Dictionary<string, object?> ReadObject()
            {
                var obj = new Dictionary<string, object?>(StringComparer.Ordinal);
                _i++; // {
                SkipWhitespace();
                if (!AtEnd && _s[_i] == '}')
                {
                    _i++;
                    return obj;
                }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != '"')
                        throw Error("Expected object key");
                    string key = ReadString();
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != ':')
                        throw Error("Expected ':'");
                    _i++;
                    SkipWhitespace();
                    obj[key] = ReadValue();
                    SkipWhitespace();
                    if (AtEnd)
                        throw Error("Unterminated object");
                    if (_s[_i] == ',')
                    {
                        _i++;
                        continue;
                    }
                    if (_s[_i] == '}')
                    {
                        _i++;
                        return obj;
                    }
                    throw Error("Expected ',' or '}'");
                }
            }

            private List<object?> ReadArray()
            {
                var list = new List<object?>();
                _i++; // [
                SkipWhitespace();
                if (!AtEnd && _s[_i] == ']')
                {
                    _i++;
                    return list;
                }
                while (true)
                {
                    SkipWhitespace();
                    list.Add(ReadValue());
                    SkipWhitespace();
                    if (AtEnd)
                        throw Error("Unterminated array");
                    if (_s[_i] == ',')
                    {
                        _i++;
                        continue;
                    }
                    if (_s[_i] == ']')
                    {
                        _i++;
                        return list;
                    }
                    throw Error("Expected ',' or ']'");
                }
            }

            private string ReadString()
            {
                _i++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd)
                        throw Error("Unterminated string");
                    char c = _s[_i++];
                    if (c == '"')
                        return sb.ToString();
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (AtEnd)
                        throw Error("Unterminated escape");
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
                            if (_i + 4 > _s.Length ||
                                !int.TryParse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                throw Error("Bad \\u escape");
                            sb.Append((char)code);
                            _i += 4;
                            break;
                        default:
                            throw Error("Bad escape '\\" + e + "'");
                    }
                }
            }

            private object ReadNumber()
            {
                int start = _i;
                if (_s[_i] == '-')
                    _i++;
                bool integral = true;
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c >= '0' && c <= '9')
                    {
                        _i++;
                    }
                    else if (c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-')
                    {
                        integral = false;
                        _i++;
                    }
                    else
                    {
                        break;
                    }
                }
                string token = _s.Substring(start, _i - start);
                if (integral && long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
                    return l;
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    return d;
                throw Error("Bad number '" + token + "'");
            }
        }
    }
}
