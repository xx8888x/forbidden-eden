using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ForbiddenEden.EditorTools
{
    /// <summary>
    /// Counts what the HS2 install offers per part category (prefabs and textures inside each chara bundle),
    /// the animation clips per animator bundle and the map scenes, to compare against what was exported.
    /// Writes Game\_hs2_inventory.txt.
    /// </summary>
    public static class Hs2Inventory
    {
        private const string Abdata = @"D:\Honey Select\HS2\abdata";

        /// <summary>Every chara list row as tab-separated text (category, id, kind, ZH_TW name, bundle, asset, …) → _hs2_lists.tsv.</summary>
        public static void DumpLists()
        {
            AssetBundle.UnloadAllAssetBundles(true);
            List<Hs2ListEntry> rows = Hs2ListReader.ReadAll(Abdata);
            var sb = new StringBuilder();
            foreach (var g in rows.GroupBy(r => r.Category).OrderBy(g => g.Key))
            {
                sb.AppendLine("## category " + g.Key + " rows=" + g.Count() + " keys=" + string.Join(",", g.First().Info.Keys));
                foreach (Hs2ListEntry r in g.OrderBy(r => r.Id))
                    sb.AppendLine(string.Join("\t", r.Category, r.Distribution, r.Id, r.Get("Kind"), r.Get("Name"), r.Get("ZH_TW"), r.Get("MainAB"), r.Get("MainData"),
                        r.Get("MainTex"), r.Get("ColorMaskTex"), r.Get("Parent"), r.Get("SetHair"), r.Get("NotBra"), r.Get("Coordinate"), r.Get("ThumbAB"), r.Get("ThumbTex")));
            }
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "_hs2_lists.tsv")), sb.ToString(), new UTF8Encoding(false));
        }

        public static void Run()
        {
            var sb = new StringBuilder();
            AssetBundle.UnloadAllAssetBundles(true);

            sb.AppendLine("== chara (category: prefabs / textures / bundles)");
            var prefabs = new SortedDictionary<string, int>();
            var textures = new SortedDictionary<string, int>();
            var bundles = new SortedDictionary<string, int>();
            var samples = new SortedDictionary<string, List<string>>();
            foreach (string file in Directory.GetFiles(Path.Combine(Abdata, "chara"), "*.unity3d", SearchOption.AllDirectories))
            {
                if (file.Contains(@"\thumb\"))
                    continue;
                string cat = System.Text.RegularExpressions.Regex.Replace(Path.GetFileNameWithoutExtension(file), @"_\d+$", "");
                AssetBundle ab = AssetBundle.LoadFromFile(file);
                if (ab == null)
                    continue;
                string[] names = ab.GetAllAssetNames();
                int p = names.Count(n => n.EndsWith(".prefab"));
                int t = names.Count(n => n.EndsWith(".png") || n.EndsWith(".tga") || n.EndsWith(".psd") || n.EndsWith(".asset") && n.Contains("tex"));
                prefabs[cat] = (prefabs.TryGetValue(cat, out int a) ? a : 0) + p;
                textures[cat] = (textures.TryGetValue(cat, out int b) ? b : 0) + t;
                bundles[cat] = (bundles.TryGetValue(cat, out int c) ? c : 0) + 1;
                if (!samples.ContainsKey(cat))
                    samples[cat] = new List<string>();
                int room = 4 - samples[cat].Count;
                if (room > 0)
                    samples[cat].AddRange(names.Where(n => n.EndsWith(".prefab")).Select(Path.GetFileNameWithoutExtension).Take(room).ToList());
                ab.Unload(true);
            }
            foreach (string cat in bundles.Keys)
                sb.AppendLine(cat + ": " + prefabs[cat] + " / " + textures[cat] + " / " + bundles[cat] + "   e.g. " + string.Join(", ", samples[cat]));

            sb.AppendLine("== animator bundles (clips / controllers)");
            foreach (string sub in new[] { "animator", @"adv\animater", @"adv\action", "studio" })
            {
                string dir = Path.Combine(Abdata, sub);
                if (!Directory.Exists(dir))
                    continue;
                foreach (string file in Directory.GetFiles(dir, "*.unity3d", SearchOption.AllDirectories))
                {
                    AssetBundle ab = AssetBundle.LoadFromFile(file);
                    if (ab == null)
                        continue;
                    string[] names = ab.GetAllAssetNames();
                    int clips = names.Count(n => n.EndsWith(".anim"));
                    int ctrls = names.Count(n => n.EndsWith(".controller") || n.EndsWith(".overridecontroller"));
                    if (clips + ctrls > 0)
                        sb.AppendLine(file.Substring(Abdata.Length + 1) + ": " + clips + " clips / " + ctrls + " controllers   e.g. "
                            + string.Join(", ", names.Where(n => n.EndsWith(".controller") || n.EndsWith(".anim")).Select(Path.GetFileNameWithoutExtension).Take(5)));
                    ab.Unload(true);
                }
            }

            sb.AppendLine("== map scenes");
            foreach (string file in Directory.GetFiles(Path.Combine(Abdata, @"map\scene"), "*.unity3d", SearchOption.AllDirectories))
                sb.AppendLine(file.Substring(Abdata.Length + 1) + "  " + new FileInfo(file).Length / (1024 * 1024) + " MB");

            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "_hs2_inventory.txt")), sb.ToString());
        }
    }
}
