using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ForbiddenEden.EditorTools
{
    /// <summary>
    /// Exports an HS2 map scene as this project's own prefab. Scene bundles only open in
    /// play mode, so the export enters play mode, loads the scene additively, copies the
    /// renderers, saves meshes / textures / Standard materials, and exits the editor.
    ///   Unity.exe -batchmode -projectPath … -executeMethod ForbiddenEden.EditorTools.Hs2SceneExporter.ExportClassroom
    /// (no -quit: the exporter calls EditorApplication.Exit when done)
    /// </summary>
    [InitializeOnLoad]
    public static class Hs2SceneExporter
    {
        private const string Abdata = @"D:\Honey Select\HS2\abdata";
        private const string JobKey = "EDEN_HS2_SCENE_JOB";
        private const float Scale = 0.1f;

        static Hs2SceneExporter()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        public static void ExportClassroom() => Start("map/scene/34/classroom.unity3d", "Classroom");

        public static void ExportStaffroom() => Start("map/scene/34/staffroom.unity3d", "Staffroom");

        public static void ExportPool() => Start("map/scene/50/nightpool.unity3d", "NightPool");

        /// <summary>Any map: -map "map/scene/34/hospitalroom.unity3d|Infirmary".</summary>
        public static void ExportFromArgs()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-map");
            if (i < 0 || i + 1 >= args.Length || !args[i + 1].Contains("|"))
            {
                Debug.LogError("usage: -map \"<bundle>|<Id>\"");
                EditorApplication.Exit(1);
                return;
            }
            string[] job = args[i + 1].Split('|');
            Start(job[0], job[1]);
        }

        private static void Start(string bundle, string id)
        {
            SessionState.SetString(JobKey, bundle + "|" + id);
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayMode(PlayModeStateChange change)
        {
            string job = SessionState.GetString(JobKey, "");
            if (change != PlayModeStateChange.EnteredPlayMode || job.Length == 0)
                return;
            SessionState.EraseString(JobKey);
            string[] parts = job.Split('|');
            var runner = new Runner(parts[0], parts[1]);
            EditorApplication.update += runner.Tick;
        }

        private sealed class Runner
        {
            private readonly string _bundlePath;
            private readonly string _id;
            private readonly StringBuilder _log = new StringBuilder();
            private readonly List<AssetBundle> _bundles = new List<AssetBundle>();
            private AsyncOperation _op;
            private string _scenePath;
            private int _stage;
            private string _dir;

            public Runner(string bundle, string id)
            {
                _bundlePath = bundle;
                _id = id;
            }

            public void Tick()
            {
                try
                {
                    switch (_stage)
                    {
                        case 0:
                            Load();
                            _stage = 1;
                            break;
                        case 1:
                            if (_op != null && !_op.isDone)
                                return;
                            _stage = 2;
                            break;
                        case 2:
                            Export();
                            Finish(0);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _log.AppendLine("EXCEPTION " + ex);
                    Debug.LogError(ex);
                    Finish(1);
                }
            }

            private void Load()
            {
                // Dependencies from every manifest (abdata + add*).
                var manifests = new List<AssetBundleManifest>();
                foreach (string f in Directory.GetFiles(Abdata).Where(f => Path.GetFileName(f) == "abdata" || Path.GetFileName(f).StartsWith("add")))
                {
                    AssetBundle mb = AssetBundle.LoadFromFile(f);
                    if (mb == null)
                        continue;
                    _bundles.Add(mb);
                    AssetBundleManifest m = mb.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
                    if (m != null)
                        manifests.Add(m);
                }
                foreach (string dep in manifests.SelectMany(m => m.GetAllDependencies(_bundlePath)).Distinct())
                {
                    AssetBundle d = AssetBundle.LoadFromFile(Path.Combine(Abdata, dep));
                    if (d != null)
                        _bundles.Add(d);
                    _log.AppendLine("dependency " + dep + (d == null ? " FAILED" : ""));
                }
                AssetBundle b = AssetBundle.LoadFromFile(Path.Combine(Abdata, _bundlePath));
                _bundles.Add(b);
                _scenePath = b.GetAllScenePaths()[0];
                _op = SceneManager.LoadSceneAsync(_scenePath, LoadSceneMode.Additive);
                _log.AppendLine("loading " + _scenePath);
            }

            private void Export()
            {
                Scene scene = SceneManager.GetSceneByPath(_scenePath);
                _log.AppendLine("scene " + scene.name + " roots=" + scene.rootCount);
                _dir = "Assets/HS2Export/" + _id;
                AssetDatabase.DeleteAsset(_dir);
                foreach (string sub in new[] { "", "/Meshes", "/Textures", "/Materials" })
                    Directory.CreateDirectory(_dir + sub);

                var root = new GameObject("HS2_" + _id);
                var pivot = new GameObject("Pivot").transform;
                pivot.SetParent(root.transform, false);
                foreach (GameObject sceneRoot in scene.GetRootGameObjects())
                {
                    GameObject copy = UnityEngine.Object.Instantiate(sceneRoot, pivot, true);
                    copy.name = sceneRoot.name;
                }
                Clean(root);

                var meshMap = new Dictionary<Mesh, Mesh>();
                var matMap = new Dictionary<Material, Material>();
                var texMap = new Dictionary<Texture, string>();
                var pendingTex = new List<(Material mat, string prop, string path, bool normal)>();
                AssetDatabase.StartAssetEditing();
                try
                {
                    ExtractStaticBatches(pivot, meshMap);
                    foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
                        mf.sharedMesh = SaveMesh(mf.sharedMesh, meshMap);
                    foreach (MeshCollider mc in root.GetComponentsInChildren<MeshCollider>(true))
                        mc.sharedMesh = SaveMesh(mc.sharedMesh, meshMap);
                    foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        smr.sharedMesh = SaveMesh(smr.sharedMesh, meshMap);
                    foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        r.sharedMaterials = r.sharedMaterials.Select(m => ConvertMaterial(m, matMap, texMap, pendingTex)).ToArray();
                        r.lightmapIndex = -1;
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }
                AssetDatabase.Refresh();
                foreach (string path in pendingTex.Select(p => p.path).Distinct())
                {
                    var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (ti == null)
                        continue;
                    ti.textureType = pendingTex.Any(p => p.path == path && p.normal) ? TextureImporterType.NormalMap : TextureImporterType.Default;
                    ti.maxTextureSize = 2048;
                    ti.textureCompression = TextureImporterCompression.Compressed;
                    ti.SaveAndReimport();
                }
                foreach ((Material mat, string prop, string path, bool _) in pendingTex)
                {
                    mat.SetTexture(prop, AssetDatabase.LoadAssetAtPath<Texture2D>(path));
                    EditorUtility.SetDirty(mat);
                }
                AssetDatabase.SaveAssets();

                pivot.localScale = Vector3.one * Scale;
                Bounds b = new Bounds(root.transform.position, Vector3.zero);
                bool any = false;
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
                {
                    if (!any) { b = r.bounds; any = true; }
                    else b.Encapsulate(r.bounds);
                }
                _log.AppendLine("bounds after scale " + b);
                _log.AppendLine("renderers=" + root.GetComponentsInChildren<Renderer>(true).Length + " meshes=" + meshMap.Count + " materials=" + matMap.Count + " textures=" + texMap.Count);
                Directory.CreateDirectory("Assets/Resources/HS2");
                PrefabUtility.SaveAsPrefabAsset(root, "Assets/Resources/HS2/" + _id + ".prefab");
                _log.AppendLine("saved Assets/Resources/HS2/" + _id + ".prefab");
                UnityEngine.Object.DestroyImmediate(root);
            }

            private void Clean(GameObject root)
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                var drop = new List<Component>();
                foreach (Component c in root.GetComponentsInChildren<Component>(true))
                {
                    if (c is Camera || c is AudioListener || c is AudioSource || c is Animator || c is Animation || c is ParticleSystem ||
                        c is ParticleSystemRenderer || c is ReflectionProbe || c is LightProbeGroup || c is UnityEngine.EventSystems.EventSystem ||
                        c is Rigidbody || c is Cloth)
                        drop.Add(c);
                }
                foreach (Component c in drop.OrderBy(c => c is ParticleSystem ? 1 : 0))
                {
                    if (c != null)
                        UnityEngine.Object.DestroyImmediate(c);
                }
                foreach (Light l in root.GetComponentsInChildren<Light>(true))
                    _log.AppendLine("light " + l.name + " " + l.type + " " + l.color + " i=" + l.intensity + " mode=" + l.lightmapBakeType);
            }

            /// <summary>
            /// HS2 maps were static-batched when the bundle was built: many renderers share one
            /// non-readable "Combined Mesh" whose vertices are already in world space, each
            /// renderer drawing its own sub-mesh range. The mesh is read back from the GPU into a
            /// readable copy and drawn by a single renderer whose material slots are filled from
            /// the original renderers' ranges.
            /// </summary>
            private void ExtractStaticBatches(Transform pivot, Dictionary<Mesh, Mesh> meshMap)
            {
                var groups = new Dictionary<Mesh, List<(MeshRenderer r, int first, int count)>>();
                foreach (MeshRenderer r in pivot.GetComponentsInChildren<MeshRenderer>(true))
                {
                    MeshFilter mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null || mf.sharedMesh.isReadable || !mf.sharedMesh.name.StartsWith("Combined Mesh"))
                        continue;
                    var so = new SerializedObject(r);
                    SerializedProperty first = so.FindProperty("m_StaticBatchInfo.firstSubMesh");
                    SerializedProperty count = so.FindProperty("m_StaticBatchInfo.subMeshCount");
                    if (first == null || count == null)
                    {
                        _log.AppendLine("static batch info missing on " + r.name);
                        continue;
                    }
                    if (!groups.TryGetValue(mf.sharedMesh, out var list))
                        groups[mf.sharedMesh] = list = new List<(MeshRenderer, int, int)>();
                    list.Add((r, first.intValue, count.intValue));
                }
                int n = 0;
                foreach (var kv in groups)
                {
                    Mesh readable = GpuCopy(kv.Key);
                    if (readable == null)
                        continue;
                    readable.name = "StaticBatch_" + n;
                    AssetDatabase.CreateAsset(readable, _dir + "/Meshes/StaticBatch_" + n + ".asset");
                    meshMap[readable] = readable;
                    var mats = new Material[readable.subMeshCount];
                    foreach ((MeshRenderer r, int first, int count) in kv.Value)
                    {
                        Material[] rm = r.sharedMaterials;
                        for (int i = 0; i < count && first + i < mats.Length; i++)
                            mats[first + i] = rm[Mathf.Min(i, rm.Length - 1)];
                        UnityEngine.Object.DestroyImmediate(r.GetComponent<MeshFilter>());
                        UnityEngine.Object.DestroyImmediate(r);
                    }
                    var go = new GameObject("StaticBatch_" + n);
                    go.transform.SetParent(pivot, false); // vertices are world-space; pivot is still identity here
                    go.AddComponent<MeshFilter>().sharedMesh = readable;
                    go.AddComponent<MeshRenderer>().sharedMaterials = mats;
                    _log.AppendLine("static batch " + n + ": " + readable.vertexCount + " verts, " + readable.subMeshCount + " sub-meshes, " + kv.Value.Count + " renderers, unfilled=" + mats.Count(m => m == null));
                    n++;
                }
            }

            /// <summary>Copies a non-readable mesh by reading its vertex and index buffers back from the GPU.</summary>
            private Mesh GpuCopy(Mesh src)
            {
                try
                {
                    var attrs = src.GetVertexAttributes();
                    int streams = attrs.Max(a => a.stream) + 1;
                    var copy = new Mesh { indexFormat = src.indexFormat };
                    copy.SetVertexBufferParams(src.vertexCount, attrs);
                    for (int s = 0; s < streams; s++)
                    {
                        using (GraphicsBuffer vb = src.GetVertexBuffer(s))
                        {
                            var data = new byte[vb.count * vb.stride];
                            vb.GetData(data);
                            copy.SetVertexBufferData(data, 0, 0, data.Length, s, UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds);
                        }
                    }
                    using (GraphicsBuffer ib = src.GetIndexBuffer())
                    {
                        int indexCount = ib.count;
                        copy.SetIndexBufferParams(indexCount, src.indexFormat);
                        if (src.indexFormat == UnityEngine.Rendering.IndexFormat.UInt16)
                        {
                            var idx = new ushort[indexCount];
                            ib.GetData(idx);
                            copy.SetIndexBufferData(idx, 0, 0, indexCount);
                        }
                        else
                        {
                            var idx = new uint[indexCount];
                            ib.GetData(idx);
                            copy.SetIndexBufferData(idx, 0, 0, indexCount);
                        }
                    }
                    copy.subMeshCount = src.subMeshCount;
                    for (int i = 0; i < src.subMeshCount; i++)
                        copy.SetSubMesh(i, src.GetSubMesh(i), UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds);
                    copy.RecalculateBounds();
                    return copy;
                }
                catch (Exception ex)
                {
                    _log.AppendLine("GPU copy failed for " + src.name + ": " + ex.Message);
                    return null;
                }
            }

            private Mesh SaveMesh(Mesh src, Dictionary<Mesh, Mesh> map)
            {
                if (src == null)
                    return null;
                if (map.TryGetValue(src, out Mesh done))
                    return done;
                if (!src.isReadable)
                    _log.AppendLine("unreadable mesh " + src.name);
                Mesh copy = UnityEngine.Object.Instantiate(src);
                copy.name = src.name;
                AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath(_dir + "/Meshes/" + Safe(src.name) + ".asset"));
                map[src] = copy;
                return copy;
            }

            private Material ConvertMaterial(Material src, Dictionary<Material, Material> map, Dictionary<Texture, string> texMap,
                List<(Material, string, string, bool)> pending)
            {
                if (src == null)
                    return null;
                if (map.TryGetValue(src, out Material done))
                    return done;
                var m = new Material(Shader.Find("Standard")) { name = src.name };
                AssetDatabase.CreateAsset(m, AssetDatabase.GenerateUniqueAssetPath(_dir + "/Materials/" + Safe(src.name) + ".mat"));
                if (src.HasProperty("_Color"))
                    m.color = src.color;
                Tex(src, m, "_MainTex", "_MainTex", false, texMap, pending);
                if (!Tex(src, m, "_BumpMap", "_BumpMap", true, texMap, pending))
                    Tex(src, m, "_NormalMap", "_BumpMap", true, texMap, pending);
                if (Tex(src, m, "_OcclusionMap", "_OcclusionMap", false, texMap, pending))
                    m.EnableKeyword("_OCCLUSIONMAP");
                if (src.HasProperty("_EmissionColor") && src.IsKeywordEnabled("_EMISSION"))
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", src.GetColor("_EmissionColor"));
                    Tex(src, m, "_EmissionMap", "_EmissionMap", false, texMap, pending);
                }
                m.SetFloat("_Glossiness", src.HasProperty("_Glossiness") ? Mathf.Min(0.6f, src.GetFloat("_Glossiness")) : 0.2f);
                int q = src.renderQueue;
                if (q >= 3000)
                {
                    m.SetFloat("_Mode", 2);
                    m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    m.SetInt("_ZWrite", 0);
                    m.EnableKeyword("_ALPHABLEND_ON");
                    m.renderQueue = 3000;
                }
                else if (q >= 2450 || src.IsKeywordEnabled("_ALPHATEST_ON"))
                {
                    m.SetFloat("_Mode", 1);
                    m.EnableKeyword("_ALPHATEST_ON");
                    m.SetFloat("_Cutoff", src.HasProperty("_Cutoff") ? src.GetFloat("_Cutoff") : 0.5f);
                    m.renderQueue = 2450;
                }
                map[src] = m;
                _log.AppendLine("material " + src.name + " [" + src.shader.name + "] q=" + q);
                return m;
            }

            private bool Tex(Material src, Material dst, string from, string to, bool normal, Dictionary<Texture, string> texMap,
                List<(Material, string, string, bool)> pending)
            {
                if (!src.HasProperty(from) || !(src.GetTexture(from) is Texture2D t))
                    return false;
                if (!texMap.TryGetValue(t, out string path))
                {
                    path = AssetDatabase.GenerateUniqueAssetPath(_dir + "/Textures/" + Safe(t.name) + ".png");
                    File.WriteAllBytes(path, Bake(t, normal));
                    texMap[t] = path;
                }
                dst.SetTextureScale(to, src.GetTextureScale(from));
                dst.SetTextureOffset(to, src.GetTextureOffset(from));
                if (normal)
                    dst.EnableKeyword("_NORMALMAP");
                pending.Add((dst, to, path, normal));
                return true;
            }

            private static byte[] Bake(Texture2D src, bool normal)
            {
                int w = Mathf.Min(src.width, 2048), h = Mathf.Min(src.height, 2048);
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
                    bool dxt5nm = src.format == TextureFormat.DXT5 || src.format == TextureFormat.BC7;
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

            private void Finish(int code)
            {
                EditorApplication.update -= Tick;
                foreach (AssetBundle b in _bundles)
                {
                    if (b != null)
                        b.Unload(false);
                }
                File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "_export_" + _id + ".txt")), _log.ToString(), new UTF8Encoding(false));
                EditorApplication.ExitPlaymode();
                if (Application.isBatchMode)
                    EditorApplication.Exit(code);
            }

            private static string Safe(string s)
            {
                foreach (char c in Path.GetInvalidFileNameChars())
                    s = s.Replace(c, '_');
                return s.Replace(' ', '_');
            }
        }
    }
}
