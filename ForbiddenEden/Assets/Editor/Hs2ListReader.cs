using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ForbiddenEden.EditorTools
{
    /// <summary>One row of an HS2 chara list (AIChara.ChaListData), keyed by column name (ID, Name, ZH_TW, MainAB…).</summary>
    public sealed class Hs2ListEntry
    {
        public int Category;
        public int Distribution;
        public Dictionary<string, string> Info = new Dictionary<string, string>();

        public int Id => int.TryParse(Get("ID"), out int v) ? v : -1;
        public string Get(string key) => Info.TryGetValue(key, out string v) ? v : "";
        public bool Has(string key) => Get(key).Length > 0 && Get(key) != "0";
    }

    /// <summary>
    /// Reads HS2's chara lists: abdata/list/characustom/*.unity3d hold TextAssets that are MessagePack maps of
    /// ChaListData {mark, categoryNo, distributionNo, filePath, lstKey, dictList}.
    /// </summary>
    public static class Hs2ListReader
    {
        public static List<Hs2ListEntry> ReadAll(string abdata)
        {
            var result = new List<Hs2ListEntry>();
            foreach (string file in Directory.GetFiles(Path.Combine(abdata, @"list\characustom"), "*.unity3d").OrderBy(f => f))
            {
                AssetBundle ab = AssetBundle.LoadFromFile(file);
                if (ab == null)
                    continue;
                try
                {
                    foreach (TextAsset ta in ab.LoadAllAssets<TextAsset>())
                    {
                        try
                        {
                            result.AddRange(Parse(ta.bytes));
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning("HS2 list " + Path.GetFileName(file) + "/" + ta.name + ": " + ex.Message);
                        }
                    }
                }
                finally
                {
                    ab.Unload(true);
                }
            }
            return result;
        }

        public static List<Hs2ListEntry> Parse(byte[] bytes)
        {
            int pos = 0;
            var root = Read(bytes, ref pos) as Dictionary<object, object>;
            if (root == null)
                throw new Exception("not a map");
            int cat = Convert.ToInt32(root["categoryNo"]);
            int dist = Convert.ToInt32(root["distributionNo"]);
            List<string> keys = ((List<object>)root["lstKey"]).Select(k => k as string ?? "").ToList();
            var rows = new List<Hs2ListEntry>();
            foreach (KeyValuePair<object, object> kv in (Dictionary<object, object>)root["dictList"])
            {
                List<object> values = (List<object>)kv.Value;
                var e = new Hs2ListEntry { Category = cat, Distribution = dist };
                for (int i = 0; i < keys.Count && i < values.Count; i++)
                    e.Info[keys[i]] = values[i] as string ?? Convert.ToString(values[i]);
                rows.Add(e);
            }
            return rows;
        }

        // ---- minimal MessagePack reader (maps, arrays, strings, ints, nil, bool, floats) ----

        private static object Read(byte[] b, ref int p)
        {
            byte t = b[p++];
            if (t <= 0x7f) return (long)t;
            if (t >= 0x80 && t <= 0x8f) return Map(b, ref p, t & 0x0f);
            if (t >= 0x90 && t <= 0x9f) return Arr(b, ref p, t & 0x0f);
            if (t >= 0xa0 && t <= 0xbf) return Str(b, ref p, t & 0x1f);
            if (t >= 0xe0) return (long)(sbyte)t;
            switch (t)
            {
                case 0xc0: return null;
                case 0xc2: return false;
                case 0xc3: return true;
                case 0xc4: { int n = b[p++]; p += n; return null; }
                case 0xc5: { int n = U16(b, ref p); p += n; return null; }
                case 0xc6: { int n = (int)U32(b, ref p); p += n; return null; }
                case 0xca: { float f = BitConverter.ToSingle(Rev(b, p, 4), 0); p += 4; return (double)f; }
                case 0xcb: { double d = BitConverter.ToDouble(Rev(b, p, 8), 0); p += 8; return d; }
                case 0xcc: return (long)b[p++];
                case 0xcd: return (long)U16(b, ref p);
                case 0xce: return (long)U32(b, ref p);
                case 0xcf: { long v = BitConverter.ToInt64(Rev(b, p, 8), 0); p += 8; return v; }
                case 0xd0: return (long)(sbyte)b[p++];
                case 0xd1: return (long)(short)U16(b, ref p);
                case 0xd2: return (long)(int)U32(b, ref p);
                case 0xd3: { long v = BitConverter.ToInt64(Rev(b, p, 8), 0); p += 8; return v; }
                case 0xd9: return Str(b, ref p, b[p++]);
                case 0xda: return Str(b, ref p, U16(b, ref p));
                case 0xdb: return Str(b, ref p, (int)U32(b, ref p));
                case 0xdc: return Arr(b, ref p, U16(b, ref p));
                case 0xdd: return Arr(b, ref p, (int)U32(b, ref p));
                case 0xde: return Map(b, ref p, U16(b, ref p));
                case 0xdf: return Map(b, ref p, (int)U32(b, ref p));
            }
            throw new Exception("msgpack type 0x" + t.ToString("x2") + " at " + (p - 1));
        }

        private static Dictionary<object, object> Map(byte[] b, ref int p, int n)
        {
            var d = new Dictionary<object, object>();
            for (int i = 0; i < n; i++)
            {
                object k = Read(b, ref p);
                d[k ?? ""] = Read(b, ref p);
            }
            return d;
        }

        private static List<object> Arr(byte[] b, ref int p, int n)
        {
            var l = new List<object>(n);
            for (int i = 0; i < n; i++)
                l.Add(Read(b, ref p));
            return l;
        }

        private static string Str(byte[] b, ref int p, int n)
        {
            string s = Encoding.UTF8.GetString(b, p, n);
            p += n;
            return s;
        }

        private static int U16(byte[] b, ref int p) { int v = (b[p] << 8) | b[p + 1]; p += 2; return v; }
        private static uint U32(byte[] b, ref int p) { uint v = ((uint)b[p] << 24) | ((uint)b[p + 1] << 16) | ((uint)b[p + 2] << 8) | b[p + 3]; p += 4; return v; }

        private static byte[] Rev(byte[] b, int p, int n)
        {
            var r = new byte[n];
            Array.Copy(b, p, r, 0, n);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(r);
            return r;
        }
    }
}
