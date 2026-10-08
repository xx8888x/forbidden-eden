using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace ForbiddenEden.EditorTools
{
    /// <summary>
    /// Assembles an HS2 character (female or male) the way HS2's ChaControl does (body skeleton,
    /// head bones under cf_J_Head_s, head meshes re-bound by bone name, hair under
    /// N_hair_Root, clothes re-bound to the body skeleton) and saves everything as this
    /// project's own assets: meshes (.asset), textures (.png), materials and one prefab.
    ///   -executeMethod ForbiddenEden.EditorTools.Hs2CharacterExporter.ExportFemale01
    /// Adult-only body meshes are left out (the Unity edition is fixed to general rating).
    /// </summary>
    public static partial class Hs2CharacterExporter
    {
        private const string Abdata = @"D:\Honey Select\HS2\abdata";
        private const float Scale = 0.1f; // HS2 characters are built at 10x scale

        private static readonly HashSet<string> AdultMeshes = new HashSet<string> { "o_mnpa", "o_mnpb", "cm_o_dan00", "cm_o_dan_f", "o_dan_f" };

        private sealed class Part
        {
            public string Bundle;
            public string Asset;
            public string Kind; // head / hair / cloth
            /// <summary>Clothes colours 1–4 for the _mc colour mask (black, R, G, B regions).</summary>
            public Color[] Colors;
        }

        /// <summary>What differs between the female and male builds.</summary>
        private sealed class Profile
        {
            public string BodyAsset;
            public string BodySkinBundle;
            public string BodySkinTex;
            public string FaceSkinBundle;
            public string FaceSkinTex;
            public string FaceNormalTex;
            public string BrowTex = "c_t_eyebrow_00";
            public Color BrowColor = new Color(0.22f, 0.16f, 0.13f, 1f);
            public string AnimBundle;
            public string[] Clips;
            public string AnimDir;
            public string ControllerName;
        }

        private static readonly Profile Female = new Profile
        {
            BodyAsset = "p_cf_body_00",
            BodySkinBundle = @"chara\00\ft_skin_b_00.unity3d", BodySkinTex = "cf_body_00_t",
            FaceSkinBundle = @"chara\00\ft_skin_f_00.unity3d", FaceSkinTex = "cf_head_00_00_t", FaceNormalTex = "cf_head_00_00_n",
            AnimBundle = @"custom\00\anim_f_00.unity3d",
            Clips = new[] { "adv_Idle_00_00", "adv_Idle_01_00", "adv_Idle_02_00", "adv_Idle_03_00", "adv_Idle_04_00", "adv_Idle_05_00", "Idle_00" },
            AnimDir = "Assets/HS2Export/Anim", ControllerName = "HS2_Female_Idle",
        };

        private static readonly Profile Male = new Profile
        {
            BodyAsset = "p_cm_body_00",
            BodySkinBundle = @"chara\00\mt_skin_b_00.unity3d", BodySkinTex = "cm_body_00_t",
            FaceSkinBundle = @"chara\00\mt_skin_f_00.unity3d", FaceSkinTex = "cm_head_02_00_t", FaceNormalTex = "cm_head_02_00_n",
            BrowColor = new Color(0.12f, 0.10f, 0.09f, 1f),
            AnimBundle = @"custom\00\anim_m_00.unity3d",
            Clips = new[] { "m_Idle_00" },
            AnimDir = "Assets/HS2Export/AnimMale", ControllerName = "HS2_Male_Idle",
        };

        private static Profile _profile = Female;

        /// <summary>Import size cap: whole characters keep 2048, catalogue parts use 1024 to keep the build small.</summary>
        private static int _maxTex = 2048;

        /// <summary>Base-body exports reuse the baked idle clips instead of re-baking them (other prefabs reference them).</summary>
        private static bool _keepAnim;

        private static readonly List<AssetBundle> Open = new List<AssetBundle>();
        private static readonly Dictionary<Texture, Texture2D> TexCache = new Dictionary<Texture, Texture2D>();
        private static readonly Dictionary<Mesh, Mesh> MeshCache = new Dictionary<Mesh, Mesh>();
        private static StringBuilder _log;
        private static string _dir;

        [MenuItem("超禁忌學園/匯出 HS2 女性角色 01")]
        public static void ExportFemale01()
        {
            var parts = new List<Part>
            {
                new Part { Bundle = @"chara\00\fo_head_00.unity3d", Asset = "p_cf_head_00", Kind = "head" },
                new Part { Bundle = @"chara\00\so_hair_b_00.unity3d", Asset = "p_c_hair_b_02_00", Kind = "hair" },
                new Part { Bundle = @"chara\00\so_hair_f_00.unity3d", Asset = "p_c_hair_f_02_00", Kind = "hair" },
                // Sailor uniform: white body, navy collar (R), red scarf (G), white piping (B).
                new Part { Bundle = @"chara\00\fo_top_00.unity3d", Asset = "p_cf_top_sailor1", Kind = "cloth",
                    Colors = new[] { new Color(0.95f, 0.95f, 0.97f), new Color(0.11f, 0.15f, 0.30f), new Color(0.72f, 0.12f, 0.16f), new Color(0.95f, 0.95f, 0.97f) } },
                new Part { Bundle = @"chara\00\fo_bot_00.unity3d", Asset = "p_cf_bot_skirt_pleat1", Kind = "cloth",
                    Colors = new[] { new Color(0.13f, 0.17f, 0.33f), new Color(0.11f, 0.15f, 0.30f), new Color(0.72f, 0.12f, 0.16f), Color.white } },
                new Part { Bundle = @"chara\00\fo_socks_00.unity3d", Asset = "p_cf_socks_high1_00", Kind = "cloth",
                    Colors = new[] { new Color(0.12f, 0.13f, 0.20f), new Color(0.12f, 0.13f, 0.20f), Color.white, Color.white } },
                new Part { Bundle = @"chara\00\fo_shoes_00.unity3d", Asset = "p_cf_shoes_uwabaki", Kind = "cloth",
                    Colors = new[] { new Color(0.96f, 0.96f, 0.96f), new Color(0.22f, 0.36f, 0.72f), new Color(0.22f, 0.36f, 0.72f), Color.white } },
            };
            Export("Female01", parts, Female);
        }

        [MenuItem("超禁忌學園/匯出 HS2 男性角色 01")]
        public static void ExportMale01()
        {
            var parts = new List<Part>
            {
                new Part { Bundle = @"chara\00\mo_head_00.unity3d", Asset = "p_cm_head_00", Kind = "head" },
                new Part { Bundle = @"chara\00\so_hair_b_00.unity3d", Asset = "p_c_hair_b_32_00", Kind = "hair" },
                new Part { Bundle = @"chara\00\so_hair_f_00.unity3d", Asset = "p_c_hair_f_02_00", Kind = "hair" },
                // Summer uniform: white shirt tucked in, dark navy-grey trousers, indoor shoes.
                new Part { Bundle = @"chara\00\mo_top_00.unity3d", Asset = "p_cm_top_yshirt_in", Kind = "cloth",
                    Colors = new[] { new Color(0.95f, 0.95f, 0.96f), new Color(0.90f, 0.91f, 0.93f), new Color(0.13f, 0.17f, 0.33f), new Color(0.80f, 0.81f, 0.84f) } },
                new Part { Bundle = @"chara\00\mo_bot_00.unity3d", Asset = "p_cm_bot_suitzubon", Kind = "cloth",
                    Colors = new[] { new Color(0.17f, 0.19f, 0.27f), new Color(0.12f, 0.13f, 0.18f), new Color(0.12f, 0.13f, 0.18f), Color.white } },
                new Part { Bundle = @"chara\00\mo_shoes_00.unity3d", Asset = "p_cm_shoes_uwabaki", Kind = "cloth",
                    Colors = new[] { new Color(0.96f, 0.96f, 0.96f), new Color(0.22f, 0.36f, 0.72f), new Color(0.22f, 0.36f, 0.72f), Color.white } },
            };
            Export("Male01", parts, Male);
        }

        private static void Export(string id, List<Part> parts, Profile profile)
        {
            _profile = profile;
            MatCache.Clear();
            _log = new StringBuilder();
            _dir = "Assets/HS2Export/" + id;
            // Re-exports start clean so asset names stay stable (no _1, _2 duplicates).
            AssetDatabase.DeleteAsset(_dir);
            if (!_keepAnim)
                AssetDatabase.DeleteAsset(profile.AnimDir);
            foreach (string sub in new[] { "", "/Meshes", "/Textures", "/Materials" })
                Directory.CreateDirectory(_dir + sub);
            Directory.CreateDirectory("Assets/Resources/HS2");
            TexCache.Clear();
            MeshCache.Clear();
            try
            {
                AssetDatabase.StartAssetEditing();
                GameObject root = Build(id, parts);
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
                ConfigureImporters();
                RebindTextures(root);

                // Root stays at unit scale facing the camera (-Z); an inner pivot carries HS2's 10x scale and +Z facing.
                var pivot = new GameObject("Pivot").transform;
                pivot.SetParent(root.transform, false);
                foreach (Transform child in root.transform.Cast<Transform>().ToList())
                {
                    if (child != pivot)
                        child.SetParent(pivot, false);
                }
                pivot.localRotation = Quaternion.Euler(0, 180, 0);
                pivot.localScale = Vector3.one * Scale;

                RuntimeAnimatorController controller = ExportAnimations();
                Transform bodyT = pivot.Find("Body");
                var animator = bodyT.gameObject.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                var rig = root.AddComponent<ForbiddenEden.World.Hs2Rig>();
                rig.Idles = profile.Clips;

                foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.sharedMesh != null && s.sharedMesh.blendShapeCount > 0))
                    _log.AppendLine("blendshapes " + smr.name + ": " + string.Join(", ", Enumerable.Range(0, smr.sharedMesh.blendShapeCount).Select(i => smr.sharedMesh.GetBlendShapeName(i))));
                string prefabPath = "Assets/Resources/HS2/" + id + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                UnityEngine.Object.DestroyImmediate(root);
                _log.AppendLine("Saved " + prefabPath);
            }
            catch (Exception ex)
            {
                _log.AppendLine("EXCEPTION " + ex);
                Debug.LogError(ex);
            }
            finally
            {
                try { AssetDatabase.StopAssetEditing(); } catch { }
                foreach (AssetBundle b in Open)
                    b.Unload(false);
                Open.Clear();
                ByPath.Clear();
                AssetDatabase.SaveAssets();
                File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "_export_" + id + ".txt")), _log.ToString(), new UTF8Encoding(false));
            }
        }

        private static readonly Dictionary<string, AssetBundle> ByPath = new Dictionary<string, AssetBundle>(StringComparer.OrdinalIgnoreCase);

        /// <summary>A bundle can be opened only once, so every load goes through this cache.</summary>
        private static AssetBundle Bundle(string rel)
        {
            if (ByPath.TryGetValue(rel, out AssetBundle cached) && cached != null)
                return cached;
            AssetBundle b = AssetBundle.LoadFromFile(Path.Combine(Abdata, rel));
            if (b == null)
                throw new Exception("Cannot open " + rel);
            Open.Add(b);
            ByPath[rel] = b;
            return b;
        }

        // ---- animations ----------------------------------------------------------------

        /// <summary>Copies the chara-maker idle clips (generic, bound to cf_J_Root paths) and builds a controller.</summary>
        private static RuntimeAnimatorController ExportAnimations()
        {
            string animDir = _profile.AnimDir;
            Directory.CreateDirectory(animDir);
            string controllerPath = animDir + "/" + _profile.ControllerName + ".controller";
            var existing = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
            if (existing != null)
                return existing;
            AssetBundle b = Bundle(_profile.AnimBundle);
            RuntimeAnimatorController src = b.LoadAllAssets<RuntimeAnimatorController>().First();
            // Bundle clips carry only runtime data, so they are re-baked by sampling onto HS2's own animation skeleton.
            GameObject rig = UnityEngine.Object.Instantiate(Bundle(@"chara\oo_base.unity3d").LoadAsset<GameObject>("p_cf_anim"));
            var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            UnityEditor.Animations.AnimatorStateMachine sm = controller.layers[0].stateMachine;
            foreach (string name in _profile.Clips)
            {
                AnimationClip clip = src.animationClips.FirstOrDefault(c => c.name == name);
                if (clip == null)
                {
                    _log.AppendLine("clip missing " + name);
                    continue;
                }
                AnimationClip baked = Bake(clip, rig);
                baked.name = name;
                AssetDatabase.CreateAsset(baked, animDir + "/" + name + ".anim");
                UnityEditor.Animations.AnimatorState state = sm.AddState(name);
                state.motion = baked;
                if (sm.defaultState == null)
                    sm.defaultState = state;
                _log.AppendLine("clip " + name + " len=" + baked.length.ToString("F2") + " bindings=" + AnimationUtility.GetCurveBindings(baked).Length);
            }
            UnityEngine.Object.DestroyImmediate(rig);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static AnimationClip Bake(AnimationClip src, GameObject rig)
        {
            const float fps = 30f;
            Transform[] bones = rig.GetComponentsInChildren<Transform>(true).Where(t => t != rig.transform).ToArray();
            var restRot = bones.Select(t => t.localRotation).ToArray();
            var restPos = bones.Select(t => t.localPosition).ToArray();
            int frames = Mathf.Max(2, Mathf.CeilToInt(src.length * fps) + 1);
            var rot = new Quaternion[bones.Length, frames];
            var pos = new Vector3[bones.Length, frames];
            for (int f = 0; f < frames; f++)
            {
                src.SampleAnimation(rig, Mathf.Min(src.length, f / fps));
                for (int i = 0; i < bones.Length; i++)
                {
                    rot[i, f] = bones[i].localRotation;
                    pos[i, f] = bones[i].localPosition;
                }
            }
            var clip = new AnimationClip { frameRate = fps, legacy = false };
            int animated = 0;
            for (int i = 0; i < bones.Length; i++)
            {
                bool rotates = false, moves = false;
                for (int f = 0; f < frames && !(rotates && moves); f++)
                {
                    rotates |= Quaternion.Angle(rot[i, f], restRot[i]) > 0.05f;
                    moves |= (pos[i, f] - restPos[i]).sqrMagnitude > 1e-6f;
                }
                if (!rotates && !moves)
                    continue;
                animated++;
                string path = AnimationUtility.CalculateTransformPath(bones[i], rig.transform);
                if (rotates)
                {
                    var cx = new AnimationCurve(); var cy = new AnimationCurve(); var cz = new AnimationCurve(); var cw = new AnimationCurve();
                    Quaternion prev = rot[i, 0];
                    for (int f = 0; f < frames; f++)
                    {
                        Quaternion q = rot[i, f];
                        if (Quaternion.Dot(prev, q) < 0)
                            q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                        prev = q;
                        float t = f / fps;
                        cx.AddKey(t, q.x); cy.AddKey(t, q.y); cz.AddKey(t, q.z); cw.AddKey(t, q.w);
                    }
                    clip.SetCurve(path, typeof(Transform), "localRotation.x", cx);
                    clip.SetCurve(path, typeof(Transform), "localRotation.y", cy);
                    clip.SetCurve(path, typeof(Transform), "localRotation.z", cz);
                    clip.SetCurve(path, typeof(Transform), "localRotation.w", cw);
                }
                if (moves)
                {
                    var px = new AnimationCurve(); var py = new AnimationCurve(); var pz = new AnimationCurve();
                    for (int f = 0; f < frames; f++)
                    {
                        float t = f / fps;
                        px.AddKey(t, pos[i, f].x); py.AddKey(t, pos[i, f].y); pz.AddKey(t, pos[i, f].z);
                    }
                    clip.SetCurve(path, typeof(Transform), "localPosition.x", px);
                    clip.SetCurve(path, typeof(Transform), "localPosition.y", py);
                    clip.SetCurve(path, typeof(Transform), "localPosition.z", pz);
                }
            }
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i].localRotation = restRot[i];
                bones[i].localPosition = restPos[i];
            }
            clip.EnsureQuaternionContinuity();
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            _log.AppendLine("bake " + src.name + " frames=" + frames + " animatedBones=" + animated);
            return clip;
        }

        // ---- assembly ------------------------------------------------------------------

        private static GameObject Build(string id, List<Part> parts)
        {
            AssetBundle baseBundle = Bundle(@"chara\oo_base.unity3d");
            var root = new GameObject("HS2_" + id);
            GameObject body = UnityEngine.Object.Instantiate(baseBundle.LoadAsset<GameObject>(_profile.BodyAsset), root.transform, false);
            body.name = "Body";
            Transform headS = Find(body.transform, "cf_J_Head_s");
            GameObject headBone = UnityEngine.Object.Instantiate(baseBundle.LoadAsset<GameObject>("p_cf_head_bone"), headS, false);
            headBone.name = "p_cf_head_bone";
            Transform hairRoot = Find(headBone.transform, "N_hair_Root");

            var bones = new Dictionary<string, Transform>();
            foreach (Transform t in body.GetComponentsInChildren<Transform>(true).Concat(headBone.GetComponentsInChildren<Transform>(true)))
                bones[t.name] = t;

            // Without HS2's body masks the feet poke through shoes, so with shoes on the foot triangles are removed.
            bool hasShoes = parts.Any(p => p.Asset.Contains("shoes"));

            // Body: keep the skin mesh, drop adult-only meshes.
            Transform meshRoot = new GameObject("Meshes").transform;
            meshRoot.SetParent(root.transform, false);
            foreach (SkinnedMeshRenderer smr in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (AdultMeshes.Contains(smr.name) || smr.name == "o_tang")
                {
                    _log.AppendLine("drop body mesh " + smr.name);
                    UnityEngine.Object.DestroyImmediate(smr.gameObject);
                    continue;
                }
                smr.sharedMesh = SaveMesh(smr.sharedMesh, hasShoes && smr.name.StartsWith("o_body") ? smr.bones : null);
                smr.sharedMaterials = smr.sharedMaterials.Select(m => SkinMaterial(m, "body")).ToArray();
            }

            foreach (Part p in parts)
            {
                GameObject prefab = Bundle(p.Bundle).LoadAsset<GameObject>(p.Asset);
                if (prefab == null)
                {
                    _log.AppendLine("MISSING " + p.Asset);
                    continue;
                }
                if (p.Kind == "hair")
                {
                    GameObject hair = UnityEngine.Object.Instantiate(prefab, hairRoot, false);
                    hair.name = p.Asset;
                    foreach (SkinnedMeshRenderer smr in hair.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        smr.sharedMesh = SaveMesh(smr.sharedMesh);
                        smr.sharedMaterials = smr.sharedMaterials.Select(HairMaterial).ToArray();
                    }
                    foreach (MeshRenderer mr in hair.GetComponentsInChildren<MeshRenderer>(true))
                        UnityEngine.Object.DestroyImmediate(mr.gameObject); // shadow proxies
                    _log.AppendLine("hair " + p.Asset);
                    continue;
                }

                // head / cloth: copy each skinned mesh and re-bind its bones by name.
                GameObject temp = UnityEngine.Object.Instantiate(prefab);
                foreach (SkinnedMeshRenderer src in temp.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!WantMesh(p.Kind, src, temp))
                        continue;
                    var go = new GameObject(src.name);
                    go.transform.SetParent(meshRoot, false);
                    var dst = go.AddComponent<SkinnedMeshRenderer>();
                    int missing = 0;
                    dst.bones = src.bones.Select(b =>
                    {
                        if (b != null && bones.TryGetValue(b.name, out Transform t))
                            return t;
                        missing++;
                        return b != null ? NearestKnown(b, bones) : root.transform;
                    }).ToArray();
                    dst.sharedMesh = SaveMesh(src.sharedMesh);
                    dst.sharedMaterials = src.sharedMaterials.Select(m => p.Kind == "head" ? HeadMaterial(m, src.name) : ClothMaterial(m, p, src.name)).ToArray();
                    dst.updateWhenOffscreen = false;
                    if (p.Kind == "head")
                    {
                        dst.rootBone = src.rootBone != null && bones.TryGetValue(src.rootBone.name, out Transform rb) ? rb : bones["cf_J_Root"];
                        dst.localBounds = src.localBounds;
                    }
                    else
                    {
                        // HS2 clothes often use a hand bone as root; once the idle pose lowers the arms the
                        // culling bounds drift off the garment. Use the hips and a whole-body box instead.
                        dst.rootBone = bones["cf_J_Hips"];
                        dst.localBounds = new Bounds(new Vector3(0, -2, 0), new Vector3(20, 20, 20));
                    }
                    for (int i = 0; i < src.sharedMesh.blendShapeCount; i++)
                        dst.SetBlendShapeWeight(i, src.GetBlendShapeWeight(i));
                    _log.AppendLine(p.Kind + " mesh " + src.name + " verts=" + src.sharedMesh.vertexCount + " uv2=" + src.sharedMesh.uv2.Length + (missing > 0 ? " missingBones=" + missing : ""));
                }
                UnityEngine.Object.DestroyImmediate(temp);
            }

            StripRuntimeJunk(root);
            return root;
        }

        private static bool WantMesh(string kind, SkinnedMeshRenderer smr, GameObject prefab)
        {
            string n = smr.name;
            if (kind == "head")
                return n == "o_head" || n.StartsWith("o_eyebase") || n == "o_eyelashes" || n == "o_eyeshadow" || n == "o_tooth" || n == "o_tang";
            // Clothes ship "_a" (worn) and "_b" (half-undressed) states; keep the worn one.
            if (n.EndsWith("_b") && prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(o => o.name == n.Substring(0, n.Length - 2) + "_a"))
                return false;
            if (n.EndsWith("_op"))
                return false;
            return true;
        }

        private static Transform NearestKnown(Transform b, Dictionary<string, Transform> bones)
        {
            for (Transform t = b.parent; t != null; t = t.parent)
            {
                if (bones.TryGetValue(t.name, out Transform known))
                    return known;
            }
            return bones["cf_J_Root"];
        }

        private static void StripRuntimeJunk(GameObject root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(c);
            foreach (Animator a in root.GetComponentsInChildren<Animator>(true))
                UnityEngine.Object.DestroyImmediate(a);
        }

        private static Transform Find(Transform root, string name) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name) ?? throw new Exception("Bone not found: " + name);

        // ---- meshes --------------------------------------------------------------------

        private static Mesh SaveMesh(Mesh src, Transform[] hideFeetBones = null)
        {
            if (src == null)
                return null;
            if (MeshCache.TryGetValue(src, out Mesh done))
                return done;
            Mesh copy = UnityEngine.Object.Instantiate(src);
            copy.name = src.name;
            if (hideFeetBones != null)
                HideFeet(copy, hideFeetBones);
            string path = AssetDatabase.GenerateUniqueAssetPath(_dir + "/Meshes/" + Safe(src.name) + ".asset");
            AssetDatabase.CreateAsset(copy, path);
            MeshCache[src] = copy;
            return copy;
        }

        /// <summary>Drops triangles whose three vertices all follow a foot or toe bone.</summary>
        private static void HideFeet(Mesh mesh, Transform[] bones)
        {
            BoneWeight[] w = mesh.boneWeights;
            bool IsFoot(int bi) => bi >= 0 && bi < bones.Length && bones[bi] != null &&
                                   (bones[bi].name.Contains("Foot") || bones[bi].name.Contains("Toes"));
            var foot = new bool[w.Length];
            for (int i = 0; i < w.Length; i++)
            {
                float f = (IsFoot(w[i].boneIndex0) ? w[i].weight0 : 0) + (IsFoot(w[i].boneIndex1) ? w[i].weight1 : 0) +
                          (IsFoot(w[i].boneIndex2) ? w[i].weight2 : 0) + (IsFoot(w[i].boneIndex3) ? w[i].weight3 : 0);
                foot[i] = f >= 0.5f;
            }
            int removed = 0;
            for (int sm = 0; sm < mesh.subMeshCount; sm++)
            {
                int[] tris = mesh.GetTriangles(sm);
                var keep = new List<int>(tris.Length);
                for (int t = 0; t < tris.Length; t += 3)
                {
                    if (foot[tris[t]] && foot[tris[t + 1]] && foot[tris[t + 2]])
                    {
                        removed++;
                        continue;
                    }
                    keep.Add(tris[t]); keep.Add(tris[t + 1]); keep.Add(tris[t + 2]);
                }
                mesh.SetTriangles(keep, sm);
            }
            _log.AppendLine("hide feet: removed " + removed + " triangles from " + mesh.name);
        }

        // ---- materials -----------------------------------------------------------------

        private static Material NewMaterial(string name, string shader)
        {
            var m = new Material(Shader.Find(shader)) { name = name };
            AssetDatabase.CreateAsset(m, AssetDatabase.GenerateUniqueAssetPath(_dir + "/Materials/" + Safe(name) + ".mat"));
            return m;
        }

        private static readonly Dictionary<Material, Material> MatCache = new Dictionary<Material, Material>();

        private static Material SkinMaterial(Material src, string part)
        {
            if (src == null)
                return null;
            if (MatCache.TryGetValue(src, out Material done))
                return done;
            Material m = NewMaterial(src.name, "EDEN/HS2 Skin");
            Texture2D skin = LoadTex(_profile.BodySkinBundle, _profile.BodySkinTex);
            Pending(m, "_MainTex", skin, false);
            m.SetColor("_Color", new Color(1f, 0.97f, 0.95f));
            MatCache[src] = m;
            return m;
        }

        private static Material HeadMaterial(Material src, string meshName)
        {
            if (src == null)
                return null;
            if (MatCache.TryGetValue(src, out Material done))
                return done;
            Material m;
            switch (meshName)
            {
                case "o_head":
                    m = NewMaterial(src.name, "EDEN/HS2 Skin");
                    Pending(m, "_MainTex", LoadTex(_profile.FaceSkinBundle, _profile.FaceSkinTex), false);
                    Pending(m, "_BumpMap", LoadTex(_profile.FaceSkinBundle, _profile.FaceNormalTex), true);
                    Pending(m, "_BrowTex", LoadTex(@"chara\00\st_eyebrow_00.unity3d", _profile.BrowTex), false);
                    m.SetColor("_BrowColor", _profile.BrowColor);
                    m.SetColor("_Color", new Color(1f, 0.97f, 0.95f));
                    break;
                case "o_eyebase_L":
                case "o_eyebase_R":
                    m = NewMaterial(src.name, "EDEN/HS2 Eye");
                    Pending(m, "_MainTex", src.GetTexture("_MainTex"), false);
                    Pending(m, "_IrisTex", LoadTex(@"chara\00\st_eye_00.unity3d", "c_t_eye_00"), false);
                    Pending(m, "_HighTex", LoadTex(@"chara\00\st_eye_hl_00.unity3d", "c_t_eyehigh_00"), false);
                    break;
                case "o_eyelashes":
                    m = NewMaterial(src.name, "EDEN/HS2 Overlay");
                    Pending(m, "_MainTex", LoadTex(@"chara\00\st_eyelash_00.unity3d", "c_t_eyelash_01"), false);
                    m.SetColor("_Color", new Color(0.10f, 0.08f, 0.08f, 1f));
                    m.SetFloat("_Channel", 0);
                    break;
                case "o_eyeshadow":
                    m = NewMaterial(src.name, "EDEN/HS2 Overlay");
                    Pending(m, "_MainTex", src.GetTexture("_MainTex"), false);
                    m.SetColor("_Color", new Color(0.25f, 0.15f, 0.15f, 0.5f));
                    m.SetFloat("_Channel", 3);
                    break;
                default: // tooth, tongue
                    m = NewMaterial(src.name, "Standard");
                    Pending(m, "_MainTex", src.GetTexture("_MainTex"), false);
                    Pending(m, "_BumpMap", src.GetTexture("_BumpMap"), true);
                    m.SetFloat("_Glossiness", 0.5f);
                    break;
            }
            MatCache[src] = m;
            return m;
        }

        private static Material HairMaterial(Material src)
        {
            if (src == null)
                return null;
            if (MatCache.TryGetValue(src, out Material done))
                return done;
            Material m = NewMaterial(src.name, "EDEN/HS2 Hair");
            Pending(m, "_MainTex", src.GetTexture("_ColorMask"), false);
            Pending(m, "_OcclusionTex", src.GetTexture("_Occlusion"), false);
            Pending(m, "_BumpMap", src.GetTexture("_BumpMap"), true);
            m.SetTextureScale("_BumpMap", src.GetTextureScale("_BumpMap"));
            MatCache[src] = m;
            return m;
        }

        private static Material ClothMaterial(Material src, Part part, string meshName)
        {
            if (src == null)
                return null;
            if (MatCache.TryGetValue(src, out Material done))
                return done;
            Material m = NewMaterial(src.name, "Standard");
            Texture main = src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null;
            if (main == null)
                main = FindClothTexture(part, meshName);
            Texture2D mask = main != null && part.Colors != null ? Bundle(part.Bundle).LoadAsset<Texture2D>(Regex.Replace(main.name, "_t$", "") + "_mc") : null;
            if (main is Texture2D mainT && part.Colors != null)
                PendingColorized(m, mainT, mask, part.Colors);
            else
                Pending(m, "_MainTex", main, false);
            Pending(m, "_BumpMap", src.HasProperty("_BumpMap") ? src.GetTexture("_BumpMap") : null, true);
            Pending(m, "_OcclusionMap", src.HasProperty("_OcclusionMap") ? src.GetTexture("_OcclusionMap") : null, false);
            Color baseColor = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : Color.white;
            if (main == null)
                baseColor = part.Colors != null ? part.Colors[0] : src.HasProperty("_Color") ? src.GetColor("_Color") : new Color(0.85f, 0.85f, 0.88f);
            baseColor.a = 1;
            m.color = main != null ? Color.white : baseColor;
            m.SetFloat("_Glossiness", 0.25f);
            if (main is Texture2D t2 && HasCutout(t2))
            {
                m.SetFloat("_Mode", 1);
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Cutoff", 0.5f);
                m.renderQueue = 2450;
            }
            MatCache[src] = m;
            return m;
        }

        /// <summary>
        /// Some HS2 clothes get their main texture from the item list at runtime, so the material has none.
        /// The bundle's own *_t texture whose name contains the mesh key (o_top_yshirtin → top_yshirtin) is used.
        /// </summary>
        private static Texture2D FindClothTexture(Part part, string meshName)
        {
            string key = Regex.Replace(meshName, "^o_", "");
            key = Regex.Replace(key, "_(a|b|cm|cf)$", "");
            string[] names = Bundle(part.Bundle).GetAllAssetNames()
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => n.EndsWith("_t") && n.Contains(key.ToLowerInvariant()))
                .ToArray();
            if (names.Length == 0)
            {
                _log.AppendLine("no main texture for " + meshName + " (key " + key + ")");
                return null;
            }
            _log.AppendLine("main texture for " + meshName + " → " + names[0]);
            return Bundle(part.Bundle).LoadAsset<Texture2D>(names[0]);
        }

        // ---- textures ------------------------------------------------------------------

        private sealed class TexBinding
        {
            public Material Mat;
            public string Prop;
            public string Path;
            public bool Normal;
        }

        private static readonly List<TexBinding> Bindings = new List<TexBinding>();
        private static readonly Dictionary<string, bool> NormalPaths = new Dictionary<string, bool>();

        private static Texture2D LoadTex(string bundle, string name) => Bundle(bundle).LoadAsset<Texture2D>(name);

        /// <summary>HS2 clothes colouring baked into the albedo: colour 1, then R→2, G→3, B→4 from the _mc mask.</summary>
        private static void PendingColorized(Material m, Texture2D main, Texture2D mask, Color[] colors)
        {
            string path = _dir + "/Textures/" + Safe(main.name) + "_col.png";
            if (!File.Exists(path))
            {
                int w = main.width, h = main.height;
                Color32[] a = Read(main, w, h);
                Color32[] k = mask != null ? Read(mask, w, h) : null;
                var outPx = new Color32[a.Length];
                for (int i = 0; i < a.Length; i++)
                {
                    Color c = colors[0];
                    if (k != null)
                    {
                        c = Color.Lerp(c, colors[1], k[i].r / 255f);
                        c = Color.Lerp(c, colors[2], k[i].g / 255f);
                        c = Color.Lerp(c, colors[3], k[i].b / 255f);
                    }
                    Color b = a[i];
                    outPx[i] = new Color(b.r * c.r, b.g * c.g, b.b * c.b, b.a);
                }
                var t = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
                t.SetPixels32(outPx);
                File.WriteAllBytes(path, t.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(t);
                _log.AppendLine("colorized " + main.name + (mask != null ? " with " + mask.name : " (no mask)"));
            }
            NormalPaths[path] = false;
            Bindings.Add(new TexBinding { Mat = m, Prop = "_MainTex", Path = path, Normal = false });
        }

        private static Color32[] Read(Texture2D src, int w, int h)
        {
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(src, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Color32[] px = t.GetPixels32();
            UnityEngine.Object.DestroyImmediate(t);
            return px;
        }

        /// <summary>Writes the texture as PNG now; the material binding happens after import.</summary>
        private static void Pending(Material m, string prop, Texture tex, bool normal)
        {
            if (!(tex is Texture2D t2) || tex.name == "Default-Particle")
                return;
            string path = _dir + "/Textures/" + Safe(t2.name) + ".png";
            if (!File.Exists(path))
                File.WriteAllBytes(path, Bake(t2, normal));
            NormalPaths[path] = normal || (NormalPaths.TryGetValue(path, out bool was) && was);
            Bindings.Add(new TexBinding { Mat = m, Prop = prop, Path = path, Normal = normal });
            if (normal)
                m.EnableKeyword("_NORMALMAP");
            if (prop == "_OcclusionMap")
                m.EnableKeyword("_OCCLUSIONMAP");
        }

        private static void ConfigureImporters()
        {
            foreach (KeyValuePair<string, bool> kv in NormalPaths)
            {
                var ti = AssetImporter.GetAtPath(kv.Key) as TextureImporter;
                if (ti == null)
                    continue;
                ti.textureType = kv.Value ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.maxTextureSize = kv.Value ? Mathf.Min(_maxTex, 1024) : _maxTex;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.mipmapEnabled = true;
                ti.SaveAndReimport();
            }
        }

        private static void RebindTextures(GameObject root)
        {
            foreach (TexBinding b in Bindings)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(b.Path);
                b.Mat.SetTexture(b.Prop, tex);
                EditorUtility.SetDirty(b.Mat);
            }
            Bindings.Clear();
            NormalPaths.Clear();
            AssetDatabase.SaveAssets();
        }

        /// <summary>GPU copy → PNG. DXT5nm normal maps (X in alpha, Y in green) are rebuilt as RGB normals.</summary>
        private static byte[] Bake(Texture2D src, bool normal)
        {
            int w = src.width, h = src.height;
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(src, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            if (normal)
            {
                Color32[] px = t.GetPixels32();
                bool dxt5nm = src.format == TextureFormat.DXT5 || src.format == TextureFormat.BC7 || src.format == TextureFormat.RGBA32 && AvgRed(px) > 0.9f;
                for (int i = 0; i < px.Length; i++)
                {
                    float x = (dxt5nm ? px[i].a : px[i].r) / 255f * 2 - 1;
                    float y = px[i].g / 255f * 2 - 1;
                    float z = Mathf.Sqrt(Mathf.Max(0, 1 - x * x - y * y));
                    px[i] = new Color32((byte)((x * 0.5f + 0.5f) * 255), (byte)((y * 0.5f + 0.5f) * 255), (byte)((z * 0.5f + 0.5f) * 255), 255);
                }
                t.SetPixels32(px);
            }
            byte[] png = t.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(t);
            return png;
        }

        private static float AvgRed(Color32[] px)
        {
            long s = 0;
            for (int i = 0; i < px.Length; i += 97)
                s += px[i].r;
            return s / (float)(px.Length / 97 + 1) / 255f;
        }

        private static bool HasCutout(Texture2D src)
        {
            if (!(src.format == TextureFormat.DXT5 || src.format == TextureFormat.BC7 || src.format == TextureFormat.RGBA32 || src.format == TextureFormat.ARGB32))
                return false;
            RenderTexture rt = RenderTexture.GetTemporary(256, 256, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var t = new Texture2D(256, 256, TextureFormat.RGBA32, false, true);
            t.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            int holes = t.GetPixels32().Count(c => c.a < 128);
            UnityEngine.Object.DestroyImmediate(t);
            return holes > 256 * 256 / 100;
        }

        private static string Safe(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }
    }
}
