using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AIChara;
using ForbiddenEden.World;
using UnityEditor;
using UnityEngine;

namespace ForbiddenEden.EditorTools
{
    /// <summary>
    /// The HS2 parts catalogue: base bodies (FemaleBase / MaleBase) plus every general-rated clothes, hair and
    /// accessory item from HS2's chara lists, each saved as its own prefab with an <see cref="Hs2Part"/>, a
    /// thumbnail, and an entry in Resources/HS2/Catalog.json.
    ///   -executeMethod ForbiddenEden.EditorTools.Hs2CharacterExporter.ExportBases
    ///   -executeMethod ForbiddenEden.EditorTools.Hs2CharacterExporter.ExportParts [-slots top,bot] [-limit 5]
    /// </summary>
    public static partial class Hs2CharacterExporter
    {
        private sealed class SlotDef
        {
            public int Category;
            public string Slot;
            public string Sex;
            public string Kind; // cloth / hair / acc
        }

        // Left out on purpose: underwear (242/243) and the adult accessory categories (357 chest, 363 groin).
        private static readonly SlotDef[] SlotDefs =
        {
            new SlotDef { Category = 240, Slot = "top", Sex = "f", Kind = "cloth" },
            new SlotDef { Category = 241, Slot = "bot", Sex = "f", Kind = "cloth" },
            new SlotDef { Category = 244, Slot = "gloves", Sex = "f", Kind = "cloth" },
            new SlotDef { Category = 245, Slot = "panst", Sex = "f", Kind = "cloth" },
            new SlotDef { Category = 246, Slot = "socks", Sex = "f", Kind = "cloth" },
            new SlotDef { Category = 247, Slot = "shoes", Sex = "f", Kind = "cloth" },
            new SlotDef { Category = 140, Slot = "top", Sex = "m", Kind = "cloth" },
            new SlotDef { Category = 141, Slot = "bot", Sex = "m", Kind = "cloth" },
            new SlotDef { Category = 144, Slot = "gloves", Sex = "m", Kind = "cloth" },
            new SlotDef { Category = 147, Slot = "shoes", Sex = "m", Kind = "cloth" },
            new SlotDef { Category = 300, Slot = "hair_b", Sex = "", Kind = "hair" },
            new SlotDef { Category = 301, Slot = "hair_f", Sex = "", Kind = "hair" },
            new SlotDef { Category = 302, Slot = "hair_s", Sex = "", Kind = "hair" },
            new SlotDef { Category = 303, Slot = "hair_o", Sex = "", Kind = "hair" },
            new SlotDef { Category = 351, Slot = "acc_head", Sex = "", Kind = "acc" },
            new SlotDef { Category = 352, Slot = "acc_ear", Sex = "", Kind = "acc" },
            new SlotDef { Category = 353, Slot = "acc_glasses", Sex = "", Kind = "acc" },
            new SlotDef { Category = 354, Slot = "acc_face", Sex = "", Kind = "acc" },
            new SlotDef { Category = 355, Slot = "acc_neck", Sex = "", Kind = "acc" },
            new SlotDef { Category = 356, Slot = "acc_shoulder", Sex = "", Kind = "acc" },
            new SlotDef { Category = 358, Slot = "acc_waist", Sex = "", Kind = "acc" },
            new SlotDef { Category = 359, Slot = "acc_back", Sex = "", Kind = "acc" },
            new SlotDef { Category = 360, Slot = "acc_arm", Sex = "", Kind = "acc" },
            new SlotDef { Category = 361, Slot = "acc_hand", Sex = "", Kind = "acc" },
            new SlotDef { Category = 362, Slot = "acc_leg", Sex = "", Kind = "acc" },
        };

        /// <summary>Names (Japanese, as in HS2's list) of items that do not fit a general-rated school game.</summary>
        private static readonly string[] Excluded =
        {
            "ボンデージ", "拘束", "全裸", "ニプレス", "前貼り", "ハーネス", "首輪", "ギャグ", "目隠し", "手錠", "足枷", "鎖", "バイブ", "ローター",
            "逆バニー", "亀甲縛り", "触手", "溶解粘液", "裸カーディガン", "裸サロペット", "裸パーカー", "セクシー", "ベビードール", "ネグリジェ",
            "縄（", "鞭", "サキュバス尻尾", "オークヘッド", "オークボディ", "バスタオル", "ココナッツブラ",
        };

        /// <summary>Drops catalogue items whose HS2 name is now excluded (prefabs and thumbnails stay unreferenced).</summary>
        public static void PruneCatalog()
        {
            if (!File.Exists(CatalogPath))
                return;
            AssetBundle.UnloadAllAssetBundles(true);
            List<Hs2ListEntry> rows = Hs2ListReader.ReadAll(Abdata);
            var catalog = JsonUtility.FromJson<Hs2Catalog>(File.ReadAllText(CatalogPath));
            int before = catalog.Items.Count;
            var removed = new List<string>();
            catalog.Items.RemoveAll(item =>
            {
                SlotDef def = SlotDefs.FirstOrDefault(d => d.Slot == item.Slot && d.Sex == item.Sex);
                Hs2ListEntry row = def != null ? rows.FirstOrDefault(r => r.Category == def.Category && r.Id == item.Id) : null;
                bool drop = row != null && Excluded.Any(x => row.Get("Name").Contains(x));
                if (drop)
                {
                    removed.Add(item.Prefab);
                    AssetDatabase.DeleteAsset("Assets/Resources/" + item.Prefab + ".prefab");
                }
                return drop;
            });
            Dictionary<string, string> names = LoadNames();
            foreach (Hs2CatalogItem item in catalog.Items)
            {
                SlotDef def = SlotDefs.FirstOrDefault(d => d.Slot == item.Slot && d.Sex == item.Sex);
                Hs2ListEntry row = def != null ? rows.FirstOrDefault(r => r.Category == def.Category && r.Id == item.Id) : null;
                if (row != null && names.TryGetValue(row.Get("Name"), out string zh))
                    item.Name = zh;
            }
            File.WriteAllText(CatalogPath, JsonUtility.ToJson(catalog, true), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(CatalogPath);
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "_prune.txt")),
                "before " + before + " after " + catalog.Items.Count + "\n" + string.Join("\n", removed), new UTF8Encoding(false));
        }

        private const string CatalogPath = "Assets/Resources/HS2/Catalog.json";
        private const string NamesPath = "Assets/Editor/Hs2Names.zh-TW.tsv";

        private static HashSet<string> _bodyBones;

        [MenuItem("超禁忌學園/匯出 HS2 基礎身體（女／男）")]
        public static void ExportBases()
        {
            _keepAnim = true;
            try
            {
                ExportBase("FemaleBase", new Part { Bundle = @"chara\00\fo_head_00.unity3d", Asset = "p_cf_head_00", Kind = "head" }, Female, true);
                ExportBase("MaleBase", new Part { Bundle = @"chara\00\mo_head_00.unity3d", Asset = "p_cm_head_00", Kind = "head" }, Male, false);
            }
            finally
            {
                _keepAnim = false;
            }
        }

        private static void ExportBase(string id, Part head, Profile profile, bool female)
        {
            Export(id, new List<Part> { head }, profile);
            string path = "Assets/Resources/HS2/" + id + ".prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                _log = new StringBuilder();
                _dir = "Assets/HS2Export/" + id;
                var avatar = root.AddComponent<Hs2Avatar>();
                avatar.Female = female;
                avatar.Body = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s => s.name.StartsWith("o_body"));
                if (avatar.Body != null)
                {
                    avatar.BodyFull = avatar.Body.sharedMesh;
                    Mesh noFeet = UnityEngine.Object.Instantiate(avatar.BodyFull);
                    noFeet.name = avatar.BodyFull.name + "_nofeet";
                    HideFeet(noFeet, avatar.Body.bones);
                    AssetDatabase.CreateAsset(noFeet, _dir + "/Meshes/" + Safe(noFeet.name) + ".asset");
                    avatar.BodyNoFeet = noFeet;
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [MenuItem("超禁忌學園/匯出 HS2 零件目錄（全部）")]
        public static void ExportParts()
        {
            string[] args = Environment.GetCommandLineArgs();
            string[] slots = null;
            int limit = int.MaxValue;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-slots")
                    slots = args[i + 1].Split(',');
                if (args[i] == "-limit")
                    limit = int.Parse(args[i + 1]);
            }
            ExportPartsFor(slots, limit);
        }

        private static void ExportPartsFor(string[] slots, int limit)
        {
            AssetBundle.UnloadAllAssetBundles(true);
            _maxTex = 1024;
            var report = new StringBuilder();
            try
            {
                List<Hs2ListEntry> rows = Hs2ListReader.ReadAll(Abdata);
                Dictionary<string, string> names = LoadNames();
                _bodyBones = BodyBoneNames();
                Hs2Catalog catalog = File.Exists(CatalogPath) ? JsonUtility.FromJson<Hs2Catalog>(File.ReadAllText(CatalogPath)) : new Hs2Catalog();
                var untranslated = new SortedSet<string>();

                foreach (IGrouping<string, SlotDef> slotGroup in SlotDefs.Where(d => slots == null || slots.Contains(d.Slot)).GroupBy(d => d.Slot))
                {
                    string slot = slotGroup.Key;
                    _dir = "Assets/HS2Export/Parts/" + slot;
                    string prefabDir = "Assets/Resources/HS2/Parts/" + slot;
                    string thumbDir = "Assets/Resources/HS2/Thumbs/" + slot;
                    AssetDatabase.DeleteAsset(_dir);
                    AssetDatabase.DeleteAsset(prefabDir);
                    AssetDatabase.DeleteAsset(thumbDir);
                    foreach (string d in new[] { _dir + "/Meshes", _dir + "/Textures", _dir + "/Materials", prefabDir, thumbDir })
                        Directory.CreateDirectory(d);
                    catalog.Items.RemoveAll(i => i.Slot == slot);
                    MatCache.Clear();
                    TexCache.Clear();
                    MeshCache.Clear();
                    _log = new StringBuilder();

                    var built = new List<(SlotDef def, Hs2ListEntry row, GameObject root, string thumb)>();
                    AssetDatabase.StartAssetEditing();
                    try
                    {
                        foreach (SlotDef def in slotGroup)
                        {
                            IEnumerable<Hs2ListEntry> entries = rows
                                .Where(r => r.Category == def.Category && r.Get("MainData").Length > 0 && r.Get("MainData") != "p_dummy")
                                .Where(r => !Excluded.Any(x => r.Get("Name").Contains(x)))
                                .OrderBy(r => r.Id)
                                .Take(limit);
                            foreach (Hs2ListEntry row in entries)
                            {
                                try
                                {
                                    GameObject root = BuildPart(def, row);
                                    if (root == null)
                                        continue;
                                    string thumb = ExportThumb(row, thumbDir);
                                    built.Add((def, row, root, thumb));
                                }
                                catch (Exception ex)
                                {
                                    _log.AppendLine("FAILED " + def.Slot + " " + row.Id + " " + row.Get("MainData") + ": " + ex.Message);
                                }
                            }
                        }
                    }
                    finally
                    {
                        AssetDatabase.StopAssetEditing();
                    }
                    AssetDatabase.Refresh();
                    ConfigureImporters();
                    RebindTextures(null);
                    ConfigureThumbs(thumbDir);

                    foreach ((SlotDef def, Hs2ListEntry row, GameObject root, string thumb) in built)
                    {
                        string asset = Safe(row.Get("MainData"));
                        // Several list rows can share one prefab (different textures), so the id is part of the name.
                        string prefabPath = prefabDir + "/" + (def.Sex.Length > 0 ? def.Sex + "_" : "") + row.Id.ToString("000") + "_" + asset + ".prefab";
                        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                        UnityEngine.Object.DestroyImmediate(root);
                        string jp = row.Get("Name");
                        if (!names.TryGetValue(jp, out string zh))
                        {
                            untranslated.Add(jp);
                            zh = jp;
                        }
                        catalog.Items.Add(new Hs2CatalogItem
                        {
                            Slot = def.Slot,
                            Sex = def.Sex,
                            Id = row.Id,
                            Name = zh,
                            Prefab = ResourcePath(prefabPath),
                            Thumb = thumb != null ? ResourcePath(thumb) : "",
                            Tag = TagFor(def.Slot, row.Get("MainData")),
                        });
                    }
                    report.AppendLine(slot + ": " + built.Count + " parts");
                    File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "_export_parts_" + slot + ".txt")), _log.ToString(), new UTF8Encoding(false));
                    foreach (AssetBundle b in Open)
                        b.Unload(true);
                    Open.Clear();
                    ByPath.Clear();
                    AssetDatabase.SaveAssets();
                }

                catalog.Items = catalog.Items.OrderBy(i => i.Slot).ThenBy(i => i.Sex).ThenBy(i => i.Id).ToList();
                File.WriteAllText(CatalogPath, JsonUtility.ToJson(catalog, true), new UTF8Encoding(false));
                AssetDatabase.ImportAsset(CatalogPath);
                report.AppendLine("catalogue items: " + catalog.Items.Count + ", untranslated names: " + untranslated.Count);
                File.WriteAllLines(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "_untranslated_names.txt")), untranslated, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                report.AppendLine("EXCEPTION " + ex);
                Debug.LogError(ex);
            }
            finally
            {
                _maxTex = 2048;
                foreach (AssetBundle b in Open)
                    b.Unload(true);
                Open.Clear();
                ByPath.Clear();
                File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "_export_parts.txt")), report.ToString(), new UTF8Encoding(false));
            }
        }

        /// <summary>
        /// HS2's ADV conversation motions (abdata/adv/animater/female): every clip of the non-H controllers is
        /// re-baked onto the animation skeleton and added as a state to the female idle controller, so existing
        /// prefabs pick them up. Hs2Rig.Gestures lists them. Writes _export_adv.txt with each clip's length.
        /// </summary>
        [MenuItem("超禁忌學園/匯出 HS2 對話動作")]
        public static void ExportAdvGestures()
        {
            AssetBundle.UnloadAllAssetBundles(true);
            _log = new StringBuilder();
            _profile = Female;
            const string dir = "Assets/HS2Export/AnimAdv";
            // Only conversation-friendly motions: emotions, talking gestures, standing idles and chair sits.
            // Bath, shower, toilet, sleeping and "broken" poses are left out.
            var keep = new Regex(@"^(Joy|Anger|Sorrow|Shame|action_0[01]_M|Idle_\d\d|M_Idle|M_Chair_[A-F])$");
            AssetDatabase.DeleteAsset(dir);
            Directory.CreateDirectory(dir);
            try
            {
                var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(Female.AnimDir + "/" + Female.ControllerName + ".controller");
                UnityEditor.Animations.AnimatorStateMachine machine = controller.layers[0].stateMachine;
                foreach (UnityEditor.Animations.ChildAnimatorState old in machine.states)
                {
                    if (!Female.Clips.Contains(old.state.name))
                        machine.RemoveState(old.state);
                }
                GameObject rig = UnityEngine.Object.Instantiate(Bundle(@"chara\oo_base.unity3d").LoadAsset<GameObject>("p_cf_anim"));
                var names = new List<string>();
                foreach (string bundle in new[] { @"adv\animater\female\30.unity3d", @"adv\animater\female\50.unity3d" })
                {
                    foreach (RuntimeAnimatorController src in Bundle(bundle).LoadAllAssets<RuntimeAnimatorController>())
                    {
                        if (src.name.Contains("_h_"))
                            continue; // H-scene variants
                        foreach (AnimationClip clip in src.animationClips)
                        {
                            if (clip == null || names.Contains(clip.name) || Female.Clips.Contains(clip.name) || !keep.IsMatch(clip.name))
                                continue;
                            AnimationClip baked = Bake(clip, rig);
                            baked.name = clip.name;
                            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(baked);
                            settings.loopTime = clip.isLooping;
                            AnimationUtility.SetAnimationClipSettings(baked, settings);
                            string path = dir + "/" + Safe(clip.name) + ".anim";
                            AssetDatabase.DeleteAsset(path);
                            AssetDatabase.CreateAsset(baked, path);
                            if (controller.layers[0].stateMachine.states.All(s => s.state.name != clip.name))
                                controller.layers[0].stateMachine.AddState(clip.name).motion = baked;
                            names.Add(clip.name);
                            _log.AppendLine(src.name + " / " + clip.name + " len=" + clip.length.ToString("F2") + " loop=" + clip.isLooping);
                        }
                    }
                }
                UnityEngine.Object.DestroyImmediate(rig);
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
                foreach (string prefabPath in new[] { "Assets/Resources/HS2/FemaleBase.prefab", "Assets/Resources/HS2/Female01.prefab" })
                {
                    GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
                    var hs2Rig = root.GetComponent<Hs2Rig>();
                    if (hs2Rig != null)
                    {
                        hs2Rig.Idles = Female.Clips.Concat(names.Where(n => n.StartsWith("Idle_"))).ToArray();
                        hs2Rig.Gestures = names.Where(n => !n.StartsWith("Idle_") && !n.Contains("Chair")).ToArray();
                        hs2Rig.Seated = names.Where(n => n.Contains("Chair")).ToArray();
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            catch (Exception ex)
            {
                _log.AppendLine("EXCEPTION " + ex);
                Debug.LogError(ex);
            }
            finally
            {
                foreach (AssetBundle b in Open)
                    b.Unload(true);
                Open.Clear();
                ByPath.Clear();
                File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "_export_adv.txt")), _log.ToString(), new UTF8Encoding(false));
            }
        }

        private static string ResourcePath(string assetPath)
        {
            string p = assetPath.Substring("Assets/Resources/".Length);
            return p.Substring(0, p.Length - Path.GetExtension(p).Length);
        }

        private static Dictionary<string, string> LoadNames()
        {
            var d = new Dictionary<string, string>();
            if (!File.Exists(NamesPath))
                return d;
            foreach (string line in File.ReadAllLines(NamesPath, Encoding.UTF8))
            {
                string[] f = line.Split('\t');
                if (f.Length >= 2 && f[0].Length > 0 && !line.StartsWith("#"))
                    d[f[0]] = f[1];
            }
            return d;
        }

        /// <summary>Rough outfit families so NPCs can be dressed for the scene (uniform at school, swimwear at the pool…).</summary>
        private static string TagFor(string slot, string asset)
        {
            string a = asset.ToLowerInvariant();
            if (a.Contains("sailor") || a.Contains("blazer") || a.Contains("yshirt") || a.Contains("pleat") || a.Contains("uwabaki") || a.Contains("suitzubon") || a.Contains("school") || a.Contains("seifuku") || a.Contains("gakuran"))
                return "uniform";
            if (a.Contains("mizugi") || a.Contains("swim") || a.Contains("bikini") || a.Contains("sukumizu"))
                return "swim";
            if (a.Contains("sport") || a.Contains("jersey") || a.Contains("buruma") || a.Contains("tsyatu") || a.Contains("sneak") || a.Contains("pants_sports"))
                return "sport";
            if (a.Contains("suit") || a.Contains("dress") || a.Contains("jacket"))
                return "formal";
            return "casual";
        }

        private static HashSet<string> BodyBoneNames()
        {
            var set = new HashSet<string>();
            AssetBundle baseBundle = Bundle(@"chara\oo_base.unity3d");
            foreach (string asset in new[] { "p_cf_body_00", "p_cm_body_00", "p_cf_head_bone" })
            {
                GameObject go = baseBundle.LoadAsset<GameObject>(asset);
                if (go == null)
                    continue;
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                    set.Add(t.name);
            }
            return set;
        }

        /// <summary>The body bone a part bone follows: itself when the body has it, else its nearest ancestor the body has.</summary>
        private static string BodyName(Transform b)
        {
            for (Transform t = b; t != null; t = t.parent)
            {
                if (_bodyBones.Contains(t.name))
                    return t.name;
            }
            return "cf_J_Hips";
        }

        private static GameObject BuildPart(SlotDef def, Hs2ListEntry row)
        {
            string ab = row.Get("MainAB").Replace('/', '\\');
            GameObject prefab = Bundle(ab).LoadAsset<GameObject>(row.Get("MainData"));
            if (prefab == null)
            {
                _log.AppendLine("MISSING " + row.Get("MainData") + " in " + ab);
                return null;
            }
            var root = new GameObject(row.Get("MainData"));
            var part = root.AddComponent<Hs2Part>();
            part.Category = def.Slot;
            part.Id = row.Id;
            if (def.Kind == "cloth")
                BuildCloth(def, row, ab, prefab, root, part);
            else
                BuildAttached(def, row, ab, prefab, root, part);
            return root;
        }

        private static void BuildCloth(SlotDef def, Hs2ListEntry row, string ab, GameObject prefab, GameObject root, Hs2Part part)
        {
            GameObject temp = UnityEngine.Object.Instantiate(prefab);
            try
            {
                CmpClothes cmp = temp.GetComponentInChildren<CmpClothes>(true);
                if (cmp != null)
                {
                    part.DefaultColors = new[] { cmp.defMainColor01, cmp.defMainColor02, cmp.defMainColor03, cmp.defMainColor04 };
                    part.UseColor = new[] { cmp.useColorN01, cmp.useColorN02, cmp.useColorN03 };
                }
                var skip = new List<Transform>();
                if (cmp != null)
                {
                    if (cmp.objTopHalf != null) skip.Add(cmp.objTopHalf.transform);
                    if (cmp.objBotHalf != null) skip.Add(cmp.objBotHalf.transform);
                }
                var meshes = new List<Hs2PartMesh>();
                foreach (SkinnedMeshRenderer src in temp.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (src.sharedMesh == null || skip.Any(s => src.transform.IsChildOf(s)) || !WantMesh("cloth", src, temp))
                        continue;
                    var cachePart = new Part { Bundle = ab, Asset = row.Get("MainData"), Kind = "cloth" };
                    meshes.Add(new Hs2PartMesh
                    {
                        Name = src.name,
                        Mesh = SaveMesh(src.sharedMesh),
                        Materials = src.sharedMaterials.Select(m => PartMaterial(m, ab, row, src.name, cachePart)).ToArray(),
                        Bones = src.bones.Select(b => b != null ? BodyName(b) : "cf_J_Hips").ToArray(),
                        BlendWeights = Enumerable.Range(0, src.sharedMesh.blendShapeCount).Select(src.GetBlendShapeWeight).ToArray(),
                    });
                }
                part.Meshes = meshes.ToArray();

                var springs = new List<DynamicBone>();
                var roots = new List<string>();
                foreach (DynamicBone db in temp.GetComponentsInChildren<DynamicBone>(true))
                {
                    if (db.m_Root == null || !_bodyBones.Contains(db.m_Root.name))
                        continue;
                    var tpl = root.AddComponent<DynamicBone>();
                    EditorUtility.CopySerialized(db, tpl);
                    tpl.m_Root = null;
                    tpl.m_Colliders = new List<DynamicBoneColliderBase>();
                    tpl.m_Exclusions = new List<Transform>();
                    tpl.m_ReferenceObject = null;
                    tpl.m_notRolls = new List<Transform>();
                    tpl.enabled = false;
                    springs.Add(tpl);
                    roots.Add(db.m_Root.name);
                }
                part.Springs = springs.ToArray();
                part.SpringRoots = roots.ToArray();
                part.HidesFeet = def.Slot == "shoes";
                // HS2's Coordinate column: 1 = one-piece that also covers the bottom slot.
                part.FullBody = def.Slot == "top" && row.Get("Coordinate") == "1";
                _log.AppendLine(def.Slot + " " + row.Id + " " + row.Get("MainData") + " meshes=" + meshes.Count + " springs=" + springs.Count
                                + " colours=" + string.Join(" ", part.DefaultColors.Select(c => ColorUtility.ToHtmlStringRGB(c))));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(temp);
            }
        }

        private static void BuildAttached(SlotDef def, Hs2ListEntry row, string ab, GameObject prefab, GameObject root, Hs2Part part)
        {
            GameObject go = UnityEngine.Object.Instantiate(prefab, root.transform, false);
            go.name = prefab.name;
            var cachePart = new Part { Bundle = ab, Asset = row.Get("MainData"), Kind = def.Kind };
            CmpHair hair = go.GetComponentInChildren<CmpHair>(true);
            CmpAccessory acc = go.GetComponentInChildren<CmpAccessory>(true);
            var hairAcc = new HashSet<Renderer>(hair?.rendAccessory?.Where(r => r != null) ?? Enumerable.Empty<Renderer>());
            var alphaParts = new HashSet<Renderer>(acc?.rendAlpha?.Where(r => r != null) ?? Enumerable.Empty<Renderer>());
            if (hair != null && hair.acsDefColor != null && hair.acsDefColor.Length > 0)
            {
                Color[] c = hair.acsDefColor;
                part.DefaultColors = new[] { c[0], c.Length > 1 ? c[1] : Color.white, c.Length > 2 ? c[2] : Color.white, Color.white };
                part.UseColor = new[] { hair.useAcsColor01, hair.useAcsColor02, hair.useAcsColor03 };
            }
            if (acc != null)
            {
                part.DefaultColors = new[] { acc.defColor01, acc.defColor02, acc.defColor03, acc.defColor04 };
                part.UseColor = new[] { acc.useColor01, acc.useColor02, acc.useColor03 };
            }
            bool hairShader = def.Kind == "hair" || (acc != null && acc.typeHair);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)
                {
                    UnityEngine.Object.DestroyImmediate(r.gameObject);
                    continue;
                }
                bool asHair = hairShader && !hairAcc.Contains(r);
                bool alpha = alphaParts.Contains(r);
                r.sharedMaterials = r.sharedMaterials.Select(m => asHair ? HairMaterial(m) : PartMaterial(m, ab, row, r.name, cachePart, alpha)).ToArray();
                if (r is SkinnedMeshRenderer smr)
                    smr.sharedMesh = SaveMesh(smr.sharedMesh);
                else if (r.TryGetComponent(out MeshFilter mf))
                    mf.sharedMesh = SaveMesh(mf.sharedMesh);
            }
            foreach (CmpBase c in go.GetComponentsInChildren<CmpBase>(true))
                UnityEngine.Object.DestroyImmediate(c);
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(c);
            foreach (Animator a in go.GetComponentsInChildren<Animator>(true))
                UnityEngine.Object.DestroyImmediate(a);
            int springs = 0;
            foreach (DynamicBone db in go.GetComponentsInChildren<DynamicBone>(true))
            {
                db.m_Colliders = new List<DynamicBoneColliderBase>();
                db.m_Exclusions = db.m_Exclusions?.Where(t => t != null).ToList() ?? new List<Transform>();
                db.m_notRolls = new List<Transform>();
                db.m_ReferenceObject = null;
                springs++;
            }
            part.AttachTo = def.Kind == "hair" ? "N_hair_Root" : row.Get("Parent");
            _log.AppendLine(def.Slot + " " + row.Id + " " + row.Get("MainData") + " attach=" + part.AttachTo + " springs=" + springs
                            + " colours=" + string.Join(" ", part.DefaultColors.Select(c => ColorUtility.ToHtmlStringRGB(c))));
        }

        /// <summary>Clothes / accessory material on the recolourable EDEN/HS2 Cloth shader.</summary>
        private static Material PartMaterial(Material src, string ab, Hs2ListEntry row, string meshName, Part cachePart, bool alpha = false)
        {
            if (src == null)
                return null;
            if (MatCache.TryGetValue(src, out Material done))
                return done;
            Material m = NewMaterial(src.name, alpha ? "EDEN/HS2 Cloth Alpha" : "EDEN/HS2 Cloth");
            Texture main = src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null;
            if (main == null && row.Has("MainTex"))
                main = Bundle(ab).LoadAsset<Texture2D>(row.Get("MainTex"));
            if (main == null)
                main = FindClothTexture(cachePart, meshName);
            Texture mask = src.HasProperty("_ColorMask") ? src.GetTexture("_ColorMask") : null;
            if (mask == null && row.Has("ColorMaskTex"))
                mask = Bundle(ab).LoadAsset<Texture2D>(row.Get("ColorMaskTex"));
            if (mask == null && main != null)
                mask = Bundle(ab).LoadAsset<Texture2D>(Regex.Replace(main.name, "_t$", "") + "_mc");
            Pending(m, "_MainTex", main, false);
            Pending(m, "_ColorMask", mask, false);
            Pending(m, "_BumpMap", src.HasProperty("_BumpMap") ? src.GetTexture("_BumpMap") : null, true);
            Pending(m, "_OcclusionMap", src.HasProperty("_OcclusionMap") ? src.GetTexture("_OcclusionMap") : null, false);
            m.SetFloat("_Glossiness", src.HasProperty("_Glossiness") ? Mathf.Clamp(src.GetFloat("_Glossiness"), 0.1f, 0.6f) : 0.25f);
            if (!alpha)
                m.SetFloat("_Cutoff", main is Texture2D t2 && HasCutout(t2) ? 0.5f : 0f);
            if (main == null && src.HasProperty("_Color"))
                m.SetColor("_Color", src.GetColor("_Color"));
            MatCache[src] = m;
            return m;
        }

        private static string ExportThumb(Hs2ListEntry row, string thumbDir)
        {
            string ab = row.Get("ThumbAB").Replace('/', '\\');
            string name = row.Get("ThumbTex");
            if (ab.Length == 0 || name.Length == 0 || !File.Exists(Path.Combine(Abdata, ab)))
                return null;
            var tex = Bundle(ab).LoadAsset<Texture2D>(name);
            if (tex == null)
                return null;
            string path = thumbDir + "/" + Safe(name) + ".png";
            if (!File.Exists(path))
                File.WriteAllBytes(path, Bake(tex, false));
            return path;
        }

        private static void ConfigureThumbs(string dir)
        {
            foreach (string file in Directory.GetFiles(dir, "*.png"))
            {
                var ti = AssetImporter.GetAtPath(file.Replace('\\', '/')) as TextureImporter;
                if (ti == null)
                    continue;
                ti.textureType = TextureImporterType.Default;
                ti.mipmapEnabled = false;
                ti.maxTextureSize = 256;
                ti.textureCompression = TextureImporterCompression.Compressed;
                ti.SaveAndReimport();
            }
        }
    }
}
