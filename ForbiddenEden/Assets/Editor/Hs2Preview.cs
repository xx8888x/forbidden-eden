using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ForbiddenEden.EditorTools
{
    /// <summary>Renders an exported prefab from a few angles to PNG (batch mode with a graphics device).</summary>
    public static class Hs2Preview
    {
        public static void RenderFemale01() => Render("Assets/Resources/HS2/Female01.prefab", "_preview_Female01");

        public static void RenderMale01() => Render("Assets/Resources/HS2/Male01.prefab", "_preview_Male01");

        /// <summary>
        /// Contact sheets of every back / front hair prefab on the male head (grey material, shape only),
        /// in bundle order, so a male hairstyle can be picked by index.
        /// </summary>
        public static void HairGallery()
        {
            const string abdata = @"D:\Honey Select\HS2\abdata";
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/HS2/Male01.prefab"));
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            Animator animator = go.GetComponentInChildren<Animator>();
            animator.runtimeAnimatorController.animationClips[0].SampleAnimation(animator.gameObject, 1.0f);
            Transform hairRoot = null;
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                if (t.name == "N_hair_Root") hairRoot = t;
            for (int i = hairRoot.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(hairRoot.GetChild(i).gameObject);

            var light = new GameObject("Key").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(25, -20, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.55f);
            var cam = new GameObject("Cam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.75f, 0.80f, 0.88f);
            cam.fieldOfView = 30;
            cam.nearClipPlane = 0.01f;
            var grey = new Material(Shader.Find("Standard")) { color = new Color(0.25f, 0.20f, 0.18f) };
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "_preview_Male01"));

            foreach (string kind in new[] { "b", "f" })
            {
                AssetBundle b = AssetBundle.LoadFromFile(Path.Combine(abdata, @"chara\00\so_hair_" + kind + "_00.unity3d"));
                string[] names = b.GetAllAssetNames().Where(n => n.EndsWith(".prefab")).Select(Path.GetFileNameWithoutExtension).ToArray();
                const int tile = 300, cols = 5;
                int rows = (names.Length + cols - 1) / cols;
                var sheet = new Texture2D(tile * cols, tile * rows, TextureFormat.RGB24, false);
                var list = new System.Text.StringBuilder();
                for (int i = 0; i < names.Length; i++)
                {
                    GameObject hair = Object.Instantiate(b.LoadAsset<GameObject>(names[i]), hairRoot, false);
                    foreach (Renderer r in hair.GetComponentsInChildren<Renderer>(true))
                        r.sharedMaterials = r.sharedMaterials.Select(_ => grey).ToArray();
                    Quaternion rot = Quaternion.Euler(4, kind == "b" ? -40 : 0, 0);
                    Vector3 focus = new Vector3(0, 1.58f, 0);
                    cam.transform.position = focus - rot * Vector3.forward * 1.0f;
                    cam.transform.rotation = rot;
                    var rt = new RenderTexture(tile, tile, 24);
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;
                    sheet.ReadPixels(new Rect(0, 0, tile, tile), (i % cols) * tile, (rows - 1 - i / cols) * tile);
                    RenderTexture.active = null;
                    cam.targetTexture = null;
                    Object.DestroyImmediate(rt);
                    Object.DestroyImmediate(hair);
                    list.AppendLine(i + ": " + names[i]);
                }
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(dir, "hair_" + kind + "_sheet.png"), sheet.EncodeToPNG());
                File.WriteAllText(Path.Combine(dir, "hair_" + kind + "_sheet.txt"), list.ToString());
                b.Unload(true);
            }
        }

        /// <summary>Lists every skinned mesh of the male prefab with bounds, bones and material, for debugging.</summary>
        public static void DebugMale01()
        {
            var sb = new System.Text.StringBuilder();
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/HS2/Male01.prefab"));
            foreach (SkinnedMeshRenderer s in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                int nullBones = 0;
                foreach (Transform b in s.bones)
                    if (b == null) nullBones++;
                Material m = s.sharedMaterial;
                sb.AppendLine(s.name + " enabled=" + s.enabled + " active=" + s.gameObject.activeInHierarchy + " mesh=" + (s.sharedMesh ? s.sharedMesh.name + "/" + s.sharedMesh.vertexCount : "null") +
                              " bones=" + s.bones.Length + " null=" + nullBones + " root=" + (s.rootBone ? s.rootBone.name : "-") + " bounds=" + s.bounds +
                              " mat=" + (m ? m.name + "[" + m.shader.name + "] tex=" + (m.mainTexture ? m.mainTexture.name : "-") + " col=" + m.color : "null") +
                              " firstBonePos=" + (s.bones.Length > 0 && s.bones[0] ? s.bones[0].name + s.bones[0].position.ToString("F2") : "-") +
                              (s.sharedMesh ? " tris=" + s.sharedMesh.triangles.Length / 3 + " bindposes=" + s.sharedMesh.bindposes.Length + " meshBounds=" + s.sharedMesh.bounds + " localBounds=" + s.localBounds : ""));
            }
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "_debug_male.txt")), sb.ToString());
        }

        /// <summary>The exported classroom from its centre in four directions, plus the character standing in it.</summary>
        public static void RenderClassroom()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject room = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/HS2/Classroom.prefab"));
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.74f);
            RenderSettings.ambientEquatorColor = new Color(0.52f, 0.50f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.30f, 0.28f, 0.26f);

            Bounds b = new Bounds();
            bool any = false;
            foreach (Renderer r in room.GetComponentsInChildren<Renderer>())
            {
                if (r.bounds.size.magnitude > 60) continue; // sky dome
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            Physics.SyncTransforms();
            float floor = b.min.y;
            if (Physics.Raycast(new Vector3(b.center.x, b.center.y, b.center.z), Vector3.down, out RaycastHit hit, 20f))
                floor = hit.point.y;
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "_preview_Classroom"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "info.txt"), "bounds(no sky) " + b + "\nfloor " + floor);

            var cam = new GameObject("Cam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.70f, 0.82f);
            cam.fieldOfView = 60;
            cam.nearClipPlane = 0.03f;
            Vector3 eye = new Vector3(b.center.x, floor + 1.5f, b.center.z);
            for (int i = 0; i < 4; i++)
                Shot(cam, eye, 0.01f, i * 90, Path.Combine(dir, "view" + i + ".png"), 1600, 900);

            GameObject girl = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/HS2/Female01.prefab"));
            girl.transform.position = new Vector3(b.center.x, floor, b.center.z + 2.5f);
            Animator animator = girl.GetComponentInChildren<Animator>();
            animator.runtimeAnimatorController.animationClips[0].SampleAnimation(animator.gameObject, 1.0f);
            cam.fieldOfView = 34;
            Shot(cam, girl.transform.position + new Vector3(0, 1.25f, 0), 2.6f, 0, Path.Combine(dir, "girl_in_room.png"), 1600, 900);
        }

        public static void Render(string prefabPath, string outName)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.position = Vector3.zero;

            // Sample the default idle clip so the preview shows the animated pose, not the bind pose.
            Animator animator = go.GetComponentInChildren<Animator>();
            if (animator != null && animator.runtimeAnimatorController != null && animator.runtimeAnimatorController.animationClips.Length > 0)
                animator.runtimeAnimatorController.animationClips[0].SampleAnimation(animator.gameObject, 1.0f);

            var lightGo = new GameObject("Key");
            var key = lightGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.1f;
            key.transform.rotation = Quaternion.Euler(30, -20, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);

            var camGo = new GameObject("Cam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.70f, 0.82f);
            cam.fieldOfView = 30;
            cam.nearClipPlane = 0.01f;
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", outName));
            Directory.CreateDirectory(dir);

            // The figure faces -Z; the camera looks along +Z from the -Z side.
            Shot(cam, new Vector3(0, 0.85f, 0), 3.6f, 0, Path.Combine(dir, "full_front.png"));
            Shot(cam, new Vector3(0, 0.85f, 0), 3.6f, -35, Path.Combine(dir, "full_34.png"));
            Shot(cam, new Vector3(0, 1.48f, 0), 0.75f, 0, Path.Combine(dir, "face.png"));
            Shot(cam, new Vector3(0, 1.42f, 0), 1.0f, 150, Path.Combine(dir, "hair_back.png"));
            Shot(cam, new Vector3(0, 0.12f, -0.05f), 0.9f, -30, Path.Combine(dir, "feet.png"));
        }

        /// <summary>Base bodies dressed from the parts catalogue by Hs2Avatar → _preview_Avatar.</summary>
        public static void RenderAvatars()
        {
            ForbiddenEden.World.Hs2Look faceTest = ForbiddenEden.World.Hs2Looks.Uniform(true);
            faceTest.HairB = 2;
            faceTest.HairF = 6;
            faceTest.Face[0] = 0.25f;  // narrower face
            faceTest.Face[22] = 0.70f; // wider eyes (adult cap)
            faceTest.Face[48] = 0.25f; // smaller mouth
            ForbiddenEden.World.Hs2Look glasses = ForbiddenEden.World.Hs2Looks.Uniform(true);
            glasses.Accessories.Add("acc_glasses:1");
            var looks = new[]
            {
                ("f_uniform", ForbiddenEden.World.Hs2Looks.Uniform(true)),
                ("m_uniform", ForbiddenEden.World.Hs2Looks.Uniform(false)),
                ("f_sport", ForbiddenEden.World.Hs2Looks.Sport(true)),
                ("f_npc", ForbiddenEden.World.Hs2Looks.ForNpc("npc_kujo_makoto", true, ForbiddenEden.World.SetKind.Classroom)),
                ("f_face", faceTest),
                ("f_glasses", glasses),
            };
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "_preview_Avatar"));
            Directory.CreateDirectory(dir);
            foreach ((string name, ForbiddenEden.World.Hs2Look look) in looks)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                ForbiddenEden.World.Hs2Avatar avatar = ForbiddenEden.World.Hs2Avatar.Create(null, look, name);
                Animator animator = avatar.GetComponentInChildren<Animator>();
                if (animator != null && animator.runtimeAnimatorController != null && animator.runtimeAnimatorController.animationClips.Length > 0)
                    animator.runtimeAnimatorController.animationClips[0].SampleAnimation(animator.gameObject, 1.0f);
                avatar.GetComponent<ForbiddenEden.World.Hs2FaceShape>()?.ApplyNow();
                var key = new GameObject("Key").AddComponent<Light>();
                key.type = LightType.Directional;
                key.intensity = 1.1f;
                key.transform.rotation = Quaternion.Euler(30, -20, 0);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);
                var cam = new GameObject("Cam").AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.62f, 0.70f, 0.82f);
                cam.fieldOfView = 30;
                cam.nearClipPlane = 0.01f;
                Shot(cam, new Vector3(0, 0.85f, 0), 3.6f, 0, Path.Combine(dir, name + "_front.png"));
                Shot(cam, new Vector3(0, 0.85f, 0), 3.6f, -140, Path.Combine(dir, name + "_back.png"));
                Shot(cam, new Vector3(0, 1.56f, 0), 0.75f, -15, Path.Combine(dir, name + "_face.png"));
            }
        }

        public static void RenderStaffroom() => RenderMap("Staffroom");

        public static void RenderPool() => RenderMap("NightPool");

        /// <summary>A top-down overview plus four eye-level views from the centre of an exported map.</summary>
        private static void RenderMap(string id)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject room = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/HS2/" + id + ".prefab"));
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.58f, 0.66f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.44f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.26f, 0.25f, 0.24f);
            Bounds b = new Bounds();
            bool any = false;
            foreach (Renderer r in room.GetComponentsInChildren<Renderer>())
            {
                if (r.bounds.size.magnitude > 60) continue; // sky domes and ground planes
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            Physics.SyncTransforms();
            float floor = FindFloor(b.center.x, b.center.z) ?? b.min.y;
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "_preview_" + id));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "info.txt"), "bounds(no big) " + b + "\nfloor " + floor);

            var cam = new GameObject("Cam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.2f, 0.22f, 0.28f);
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 500;
            // Top-down orthographic overview with a 1 m grid reference in info.txt.
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(b.extents.x, b.extents.z) * 1.05f;
            cam.transform.position = new Vector3(b.center.x, b.max.y + 5, b.center.z);
            cam.transform.rotation = Quaternion.Euler(90, 0, 0);
            var light = new GameObject("Top").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.6f;
            light.transform.rotation = Quaternion.Euler(70, 30, 0);
            Render(cam, Path.Combine(dir, "top.png"), 1200, 1200);
            Object.DestroyImmediate(light.gameObject);
            cam.orthographic = false;
            cam.fieldOfView = 60;
            Vector3 eye = new Vector3(b.center.x, floor + 1.5f, b.center.z);
            for (int i = 0; i < 4; i++)
                Shot(cam, eye, 0.01f, i * 90, Path.Combine(dir, "view" + i + ".png"), 1600, 900);
        }

        /// <summary>
        /// The walkable floor under (x, z). Every surface with 2 m of clearance above it qualifies, and the one
        /// nearest the map's ground level (HS2 maps put their floor at y≈0) wins. Picking the highest qualifying hit
        /// returned the roof, because nothing is above a roof.
        /// </summary>
        public static float? FindFloor(float x, float z, float top = 60f) => FindFloor(x, z, out _, top);

        public static float? FindFloor(float x, float z, out string allHits, float top = 60f)
        {
            RaycastHit[] hits = Physics.RaycastAll(new Vector3(x, top, z), Vector3.down, top * 2);
            var sorted = hits.OrderByDescending(h => h.point.y).ToList();
            allHits = string.Join(", ", sorted.Select(h => h.collider.name + "@" + h.point.y.ToString("F2")));
            float? best = null;
            for (int i = 0; i < sorted.Count; i++)
            {
                float y = sorted[i].point.y;
                float above = i == 0 ? float.MaxValue : sorted[i - 1].point.y;
                if (above - y >= 2f && (best == null || Mathf.Abs(y) < Mathf.Abs(best.Value)))
                    best = y;
            }
            return best ?? (sorted.Count > 0 ? sorted[sorted.Count - 1].point.y : (float?)null);
        }

        /// <summary>
        /// Stand-spot candidates for every exported map given by -maps A,B,C: grid points inside the map whose
        /// floor is near y = 0 with headroom, spread out, each facing the map centre. → _preview_&lt;id&gt;/cand_*.png
        /// </summary>
        public static void AutoCandidates()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-maps");
            if (i < 0 || i + 1 >= args.Length)
                return;
            foreach (string id in args[i + 1].Split(','))
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/HS2/" + id + ".prefab");
                if (prefab == null)
                    continue;
                GameObject room = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                Physics.SyncTransforms();
                Bounds b = new Bounds();
                bool any = false;
                foreach (Renderer r in room.GetComponentsInChildren<Renderer>())
                {
                    if (r.bounds.size.magnitude > 60) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                var spots = new List<Vector3>();
                for (int gx = 1; gx <= 5; gx++)
                {
                    for (int gz = 1; gz <= 5; gz++)
                    {
                        float x = Mathf.Lerp(b.min.x, b.max.x, 0.2f + 0.6f * (gx - 1) / 4f);
                        float z = Mathf.Lerp(b.min.z, b.max.z, 0.2f + 0.6f * (gz - 1) / 4f);
                        float? floor = FindFloor(x, z);
                        if (floor == null || Mathf.Abs(floor.Value) > 0.6f)
                            continue;
                        // Keep 1.2 m clear around the spot at chest height.
                        Vector3 chest = new Vector3(x, floor.Value + 1.1f, z);
                        if (Physics.CheckSphere(chest, 0.45f))
                            continue;
                        spots.Add(new Vector3(x, floor.Value, z));
                    }
                }
                // Up to six spots, greedily spread apart.
                var chosen = new List<Vector3>();
                Vector3 centre = new Vector3(b.center.x, 0, b.center.z);
                spots.Sort((p, q) => (p - centre).sqrMagnitude.CompareTo((q - centre).sqrMagnitude));
                foreach (Vector3 s in spots)
                {
                    if (chosen.Count < 6 && chosen.All(c => (c - s).sqrMagnitude > 2.0f))
                        chosen.Add(s);
                }
                Object.DestroyImmediate(room);
                var list = chosen.Select(s =>
                {
                    // Camera on the far side of the spot from the centre, so the room fills the background.
                    Vector3 toCentre = centre - new Vector3(s.x, 0, s.z);
                    float yaw = toCentre.sqrMagnitude < 0.01f ? 0 : Quaternion.LookRotation(-toCentre).eulerAngles.y;
                    return new Vector3(s.x, s.z, yaw);
                }).ToArray();
                Candidates(id, list);
            }
        }

        public static void StaffroomCandidates() => Candidates("Staffroom", new[]
        {
            new Vector3(0.2f, 1.0f, 0), new Vector3(0.2f, -0.3f, 180), new Vector3(-0.4f, 0.6f, 90), new Vector3(0.6f, 0.4f, 270)
        });

        public static void PoolCandidates() => Candidates("NightPool", new[]
        {
            new Vector3(1.3f, -2.3f, 0), new Vector3(1.3f, -2.3f, 180), new Vector3(-4f, -5f, 0), new Vector3(6f, -5f, 0),
            new Vector3(1.3f, -7f, 0), new Vector3(1.3f, 3f, 180)
        });

        /// <summary>The chosen pool spot under several light scales, plus the map's light list, to pick a dimming factor.</summary>
        public static void PoolLighting()
        {
            var sb = new System.Text.StringBuilder();
            foreach (float scale in new[] { 0.12f, 0.2f, 0.3f })
            {
                Candidates("NightPool", new[] { new Vector3(6f, -5f, 0) }, scale, "light_" + scale.ToString("F2"));
                if (sb.Length == 0)
                    foreach (Light l in Object.FindObjectsByType<Light>())
                        sb.AppendLine(l.name + " " + l.type + " i=" + l.intensity / scale + " range=" + l.range + " color=" + l.color);
            }
            File.AppendAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "_preview_NightPool", "lights.txt")), sb.ToString());
        }

        /// <summary>
        /// For each (x, z, yaw): the female figure on the floor at (x, z) facing the camera, framed exactly like
        /// the game's dialogue shot (camera 3.4 m away looking along yaw). Writes floor heights to candidates.txt.
        /// </summary>
        private static void Candidates(string id, Vector3[] spots, float lightScale = 0.55f, string prefix = "cand_")
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "_preview_" + id));
            Directory.CreateDirectory(dir);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < spots.Length; i++)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.55f, 0.58f, 0.64f);
                RenderSettings.ambientEquatorColor = new Color(0.45f, 0.44f, 0.42f);
                RenderSettings.ambientGroundColor = new Color(0.26f, 0.25f, 0.24f);
                GameObject room = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/HS2/" + id + ".prefab"));
                foreach (Light l in room.GetComponentsInChildren<Light>(true))
                    l.intensity *= lightScale;
                Physics.SyncTransforms();
                float x = spots[i].x, z = spots[i].y, yaw = spots[i].z;
                float floor = FindFloor(x, z, out string allHits) ?? 0f;
                sb.AppendLine(i + ": x=" + x + " z=" + z + " yaw=" + yaw + " floor=" + floor.ToString("F3") + "  hits: " + allHits);
                GameObject girl = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/HS2/Female01.prefab"));
                girl.transform.position = new Vector3(x, floor, z);
                girl.transform.rotation = Quaternion.Euler(0, yaw, 0);
                Animator animator = girl.GetComponentInChildren<Animator>();
                animator.runtimeAnimatorController.animationClips[0].SampleAnimation(animator.gameObject, 1.0f);
                var cam = new GameObject("Cam").AddComponent<Camera>();
                cam.fieldOfView = 34;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 500;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.1f, 0.12f, 0.18f);
                Shot(cam, new Vector3(x, floor + 1.48f * 0.82f, z), 3.4f, yaw + 4, Path.Combine(dir, prefix + i + ".png"), 1600, 900);
            }
            if (prefix == "cand_")
                File.WriteAllText(Path.Combine(dir, "candidates.txt"), sb.ToString());
        }

        private static void Render(Camera cam, string file, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            File.WriteAllBytes(file, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }

        /// <summary>Same placement as the game's Stage: room turned 180°, offset so the stage spot sits at the origin.</summary>
        public static void RenderClassroomStage()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "_preview_Classroom"));
            float[] spots = { -2.0f, -4.0f, -6.0f };
            foreach (float z in spots)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.74f);
                RenderSettings.ambientEquatorColor = new Color(0.52f, 0.50f, 0.48f);
                RenderSettings.ambientGroundColor = new Color(0.30f, 0.28f, 0.26f);
                GameObject room = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/HS2/Classroom.prefab"));
                var spot = new Vector3(1.03f, 0, z);
                room.transform.rotation = Quaternion.Euler(0, 180, 0);
                room.transform.position = -(Quaternion.Euler(0, 180, 0) * spot);
                GameObject girl = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/HS2/Female01.prefab"));
                girl.transform.position = new Vector3(0, 0, 0.2f);
                Animator animator = girl.GetComponentInChildren<Animator>();
                animator.runtimeAnimatorController.animationClips[0].SampleAnimation(animator.gameObject, 1.0f);
                var cam = new GameObject("Cam").AddComponent<Camera>();
                cam.fieldOfView = 34;
                cam.nearClipPlane = 0.05f;
                Shot(cam, new Vector3(0, 1.48f * 0.82f, 0.2f), 3.4f, 4, Path.Combine(dir, "stage_z" + z + ".png"), 1600, 900);
            }
        }

        private static void Shot(Camera cam, Vector3 focus, float dist, float yaw, string file, int w = 900, int h = 1200)
        {
            Quaternion rot = Quaternion.Euler(4, yaw, 0);
            cam.transform.position = focus - rot * Vector3.forward * dist;
            cam.transform.rotation = rot;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }
    }
}
