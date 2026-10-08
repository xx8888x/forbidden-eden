using System.IO;
using System.Linq;
using System.Text;
using AIChara;
using UnityEngine;

namespace ForbiddenEden.EditorTools
{
    /// <summary>Checks that HS2 part components bind to the stand-ins in Scripts/Hs2Compat → _hs2_probe.txt.</summary>
    public static class Hs2Probe
    {
        /// <summary>Where the head shape key tables live: the heads' list rows (ShapeAnime column) and their bundles' assets.</summary>
        public static void HeadShape()
        {
            var sb = new StringBuilder();
            AssetBundle.UnloadAllAssetBundles(true);
            foreach (Hs2ListEntry r in Hs2ListReader.ReadAll(@"D:\Honey Select\HS2\abdata").Where(r => r.Category == 210 || r.Category == 110))
                sb.AppendLine(r.Category + " " + r.Id + " " + string.Join(" | ", r.Info.Select(kv => kv.Key + "=" + kv.Value)));
            AssetBundle.UnloadAllAssetBundles(true);
            foreach (string b in new[] { @"chara\00\fo_head_00.unity3d", @"chara\00\mo_head_00.unity3d" })
            {
                AssetBundle ab = AssetBundle.LoadFromFile(@"D:\Honey Select\HS2\abdata\" + b);
                sb.AppendLine("== " + b + ": " + string.Join(", ", ab.GetAllAssetNames().Select(Path.GetFileName)));
                ab.Unload(true);
            }
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "_hs2_headshape.txt")), sb.ToString());
        }

        /// <summary>Face slider check: bone values at slider 0 / 0.5 / 1 for a few sliders → _hs2_faceprobe.txt.</summary>
        public static void FaceShape()
        {
            var sb = new StringBuilder();
            var look = ForbiddenEden.World.Hs2Looks.Uniform(true);
            ForbiddenEden.World.Hs2Avatar avatar = ForbiddenEden.World.Hs2Avatar.Create(null, look, "probe");
            var face = avatar.GetComponent<ForbiddenEden.World.Hs2FaceShape>();
            var bones = avatar.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name, t => t, System.StringComparer.Ordinal);
            foreach ((int slider, string bone) in new[] { (0, "cf_J_FaceBase"), (22, "cf_J_Eye_s_L"), (48, "cf_J_MouthBase_s"), (19, "cf_J_Eye_t_L") })
            {
                foreach (float v in new[] { 0f, 0.5f, 1f })
                {
                    face.Set(slider, v);
                    face.ApplyNow();
                    Transform t = bones.TryGetValue(bone, out Transform b) ? b : null;
                    sb.AppendLine("slider " + slider + "=" + v + " ready=" + face.Ready + " " + bone + (t == null ? " MISSING" : " pos=" + t.localPosition.ToString("F4") + " scl=" + t.localScale.ToString("F3")));
                }
                face.Set(slider, 0.5f);
            }
            Object.DestroyImmediate(avatar.gameObject);
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "_hs2_faceprobe.txt")), sb.ToString());
        }

        public static void Scripts()
        {
            var sb = new StringBuilder();
            AssetBundle.UnloadAllAssetBundles(true);
            string abdata = @"D:\Honey Select\HS2\abdata\";
            foreach ((string bundle, string asset) in new[]
            {
                (@"chara\00\fo_top_00.unity3d", "p_cf_top_sailor1"),
                (@"chara\00\fo_bot_00.unity3d", "p_cf_bot_skirt_pleat1"),
                (@"chara\00\so_hair_b_00.unity3d", "p_c_hair_b_02_00"),
                (@"chara\00\ao_head_00.unity3d", "p_acs_hairpin_00"),
            })
            {
                AssetBundle ab = AssetBundle.LoadFromFile(abdata + bundle);
                var go = ab.LoadAsset<GameObject>(asset);
                sb.AppendLine("== " + asset);
                foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    switch (mb)
                    {
                        case null:
                            sb.AppendLine("  (missing)");
                            break;
                        case CmpClothes c:
                            sb.AppendLine("  CmpClothes col=" + c.defMainColor01 + " " + c.defMainColor02 + " " + c.defMainColor03 + " " + c.defMainColor04
                                + " use=" + c.useColorN01 + c.useColorN02 + c.useColorN03 + " rend=" + c.rendNormal01?.Length + "/" + c.rendNormal02?.Length
                                + " top=" + (c.objTopDef ? c.objTopDef.name : "-") + " half=" + (c.objTopHalf ? c.objTopHalf.name : "-") + " opt=" + c.objOpt01?.Length);
                            break;
                        case CmpHair h:
                            sb.AppendLine("  CmpHair rend=" + h.rendHair?.Length + " acs=" + h.rendAccessory?.Length + " bones=" + h.boneInfo?.Length
                                + " db=" + h.boneInfo?.Sum(b => b.dynamicBone?.Length ?? 0));
                            break;
                        case CmpAccessory a:
                            sb.AppendLine("  CmpAccessory rend=" + a.rendNormal?.Length + " col=" + a.defColor01 + " use=" + a.useColor01 + a.useColor02 + a.useColor03);
                            break;
                        case DynamicBone d:
                            sb.AppendLine("  DynamicBone " + d.Comment + " root=" + (d.m_Root ? d.m_Root.name : "-") + " damp=" + d.m_Damping + " elast=" + d.m_Elasticity
                                + " stiff=" + d.m_Stiffness + " inert=" + d.m_Inert + " r=" + d.m_Radius + " grav=" + d.m_Gravity + " cols=" + d.m_Colliders?.Count(c => c != null) + "/" + d.m_Colliders?.Count);
                            break;
                        default:
                            sb.AppendLine("  " + mb.GetType().FullName);
                            break;
                    }
                }
            }
            AssetBundle.UnloadAllAssetBundles(true);
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "_hs2_probe.txt")), sb.ToString());
        }
    }
}
