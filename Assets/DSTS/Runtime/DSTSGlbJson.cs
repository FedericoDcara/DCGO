using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DCGO.DSTS
{
    /// <summary>
    /// Tiny JSON reader (objects -> Dictionary&lt;string,object&gt;, arrays -> List&lt;object&gt;,
    /// numbers -> double). Used to read the glTF JSON chunk incl. the DSTS "extras".
    /// Self-contained so the project needs no extra JSON package. Runtime + editor.
    /// </summary>
    public static class DSTSGlbJson
    {
        public static object Parse(string json)
        {
            int i = 0;
            var v = ParseValue(json, ref i);
            return v;
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON");
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't': Expect(s, ref i, "true"); return true;
                case 'f': Expect(s, ref i, "false"); return false;
                case 'n': Expect(s, ref i, "null"); return null;
                default: return ParseNumber(s, ref i);
            }
        }

        static void Expect(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                throw new FormatException($"Expected '{word}' at {i}");
            i += word.Length;
        }

        static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++; // {
            SkipWs(s, ref i);
            if (s[i] == '}') { i++; return d; }
            while (true)
            {
                SkipWs(s, ref i);
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (s[i] != ':') throw new FormatException($"Expected ':' at {i}");
                i++;
                d[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return d; }
                throw new FormatException($"Expected ',' or '}}' at {i}");
            }
        }

        static List<object> ParseArray(string s, ref int i)
        {
            var l = new List<object>();
            i++; // [
            SkipWs(s, ref i);
            if (s[i] == ']') { i++; return l; }
            while (true)
            {
                l.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return l; }
                throw new FormatException($"Expected ',' or ']' at {i}");
            }
        }

        static string ParseString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException($"Expected string at {i}");
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
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
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("Unterminated string");
        }

        static object ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (start == i) throw new FormatException($"Unexpected character '{s[i]}' at {i}");
            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        // ---------------- helpers ----------------
        public static Dictionary<string, object> Obj(object o, string key)
            => (o as Dictionary<string, object>) != null && ((Dictionary<string, object>)o).TryGetValue(key, out var v)
                ? v as Dictionary<string, object> : null;

        public static List<object> Arr(object o, string key)
            => (o as Dictionary<string, object>) != null && ((Dictionary<string, object>)o).TryGetValue(key, out var v)
                ? v as List<object> : null;

        public static string Str(object o, string key, string def = null)
            => (o as Dictionary<string, object>) != null && ((Dictionary<string, object>)o).TryGetValue(key, out var v) && v is string str
                ? str : def;

        public static double Num(object o, string key, double def = 0)
            => (o as Dictionary<string, object>) != null && ((Dictionary<string, object>)o).TryGetValue(key, out var v) && v is double d
                ? d : def;

        public static bool Bool(object o, string key, bool def = false)
            => (o as Dictionary<string, object>) != null && ((Dictionary<string, object>)o).TryGetValue(key, out var v) && v is bool b
                ? b : def;

        public static bool Has(object o, string key)
            => (o as Dictionary<string, object>) != null && ((Dictionary<string, object>)o).ContainsKey(key);

        /// <summary>Reads the JSON chunk of a .glb file.</summary>
        public static Dictionary<string, object> ReadGlbJson(string fullPath)
        {
            using (var fs = File.OpenRead(fullPath))
            using (var br = new BinaryReader(fs))
            {
                uint magic = br.ReadUInt32();
                if (magic != 0x46546C67) throw new InvalidDataException("Not a GLB file: " + fullPath); // "glTF"
                br.ReadUInt32(); // version
                br.ReadUInt32(); // total length
                uint chunkLen = br.ReadUInt32();
                uint chunkType = br.ReadUInt32();
                if (chunkType != 0x4E4F534A) throw new InvalidDataException("First GLB chunk is not JSON"); // "JSON"
                var bytes = br.ReadBytes((int)chunkLen);
                return Parse(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>;
            }
        }
    }
}
