using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ForbiddenEden.EditorTools
{
    /// <summary>
    /// Copies HS2's face/body shape data (abdata/list/customshape.unity3d: bone key tables and slider category
    /// lists) into Resources/HS2/Shape as raw .bytes, for <see cref="ForbiddenEden.World.Hs2FaceShape"/>.
    ///   -executeMethod ForbiddenEden.EditorTools.Hs2ShapeExporter.Export
    /// </summary>
    public static class Hs2ShapeExporter
    {
        private const string Bundle = @"D:\Honey Select\HS2\abdata\list\customshape.unity3d";
        private const string OutDir = "Assets/Resources/HS2/Shape";

        [MenuItem("超禁忌學園/匯出 HS2 臉型體型資料")]
        public static void Export()
        {
            AssetBundle.UnloadAllAssetBundles(true);
            Directory.CreateDirectory(OutDir);
            var log = new StringBuilder();
            // The head key tables ship with the head models (cf_anmShapeHead_00 / cm_anmShapeHead_00).
            foreach (string bundle in new[] { Bundle, @"D:\Honey Select\HS2\abdata\chara\00\fo_head_00.unity3d", @"D:\Honey Select\HS2\abdata\chara\00\mo_head_00.unity3d" })
            {
                AssetBundle ab = AssetBundle.LoadFromFile(bundle);
                try
                {
                    foreach (TextAsset ta in ab.LoadAllAssets<TextAsset>())
                    {
                        if (ta.name.StartsWith("preset_"))
                            continue;
                        File.WriteAllBytes(OutDir + "/" + ta.name + ".bytes", ta.bytes);
                        log.AppendLine(ta.name + " " + ta.bytes.Length + " bytes");
                    }
                }
                finally
                {
                    ab.Unload(true);
                }
            }
            AssetDatabase.Refresh();
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "_export_shape.txt")), log.ToString(), new UTF8Encoding(false));
        }
    }
}
