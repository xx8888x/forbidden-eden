#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ForbiddenEden.World
{
    /// <summary>
    /// Loads licensed HS2 content straight from an HS2 install's abdata folder:
    /// AssetBundle manifests (abdata, add*), dependency bundles, map scenes and character
    /// prefabs. HS2 shipped with Unity 2018.4; bundles open in Unity 6, but Illusion's
    /// MonoBehaviours come in as missing scripts and some shaders need replacing.
    /// </summary>
    public sealed class Hs2Assets
    {
        public static Hs2Assets? Instance { get; private set; }

        private readonly string _root;
        private readonly List<AssetBundleManifest> _manifests = new List<AssetBundleManifest>();
        private readonly Dictionary<string, AssetBundle> _loaded = new Dictionary<string, AssetBundle>(StringComparer.OrdinalIgnoreCase);
        private Shader? _fallback;

        private Hs2Assets(string root) => _root = root;

        public string Root => _root;
        public string? LoadedMapScene { get; private set; }
        public string? LoadedMapBundle { get; private set; }

        /// <summary>Common install locations; the settings value wins when it exists.</summary>
        public static string? FindAbdata(string? configured)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(configured))
                candidates.Add(configured!);
            candidates.Add(@"D:\Honey Select\HS2\abdata");
            candidates.Add(@"D:\HS2\abdata");
            candidates.Add(Path.Combine(Application.dataPath, "..", "..", "HS2Data", "abdata"));
            foreach (string c in candidates)
            {
                if (File.Exists(Path.Combine(c, "abdata")))
                    return Path.GetFullPath(c);
            }
            return null;
        }

        public static Hs2Assets? Open(string? configured, Action<string>? log)
        {
            if (Instance != null)
                return Instance;
            string? root = FindAbdata(configured);
            if (root == null)
            {
                log?.Invoke("HS2 abdata not found; using built-in placeholder sets.");
                return null;
            }
            var assets = new Hs2Assets(root);
            foreach (string file in Directory.GetFiles(root))
            {
                string name = Path.GetFileName(file);
                if (name != "abdata" && !name.StartsWith("add"))
                    continue;
                AssetBundle mb = AssetBundle.LoadFromFile(file);
                if (mb == null)
                    continue;
                AssetBundleManifest m = mb.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
                if (m != null)
                    assets._manifests.Add(m);
            }
            log?.Invoke("HS2 abdata at " + root + ", manifests=" + assets._manifests.Count);
            Instance = assets;
            return assets;
        }

        // ---- bundles -------------------------------------------------------------------

        public AssetBundle? Load(string relative, Action<string>? log = null)
        {
            relative = relative.Replace('\\', '/');
            if (_loaded.TryGetValue(relative, out AssetBundle existing) && existing != null)
                return existing;
            foreach (string dep in Dependencies(relative))
            {
                if (!_loaded.ContainsKey(dep))
                {
                    AssetBundle d = AssetBundle.LoadFromFile(Path.Combine(_root, dep));
                    if (d != null)
                        _loaded[dep] = d;
                    else
                        log?.Invoke("HS2 dependency failed: " + dep);
                }
            }
            string path = Path.Combine(_root, relative);
            if (!File.Exists(path))
            {
                log?.Invoke("HS2 bundle missing: " + relative);
                return null;
            }
            AssetBundle b = AssetBundle.LoadFromFile(path);
            if (b != null)
                _loaded[relative] = b;
            return b;
        }

        private IEnumerable<string> Dependencies(string relative)
        {
            var result = new List<string>();
            foreach (AssetBundleManifest m in _manifests)
            {
                foreach (string d in m.GetAllDependencies(relative))
                {
                    if (!result.Contains(d))
                        result.Add(d);
                }
            }
            return result;
        }

        // ---- maps ----------------------------------------------------------------------

        /// <summary>Loads a map scene additively, unloading the previous one.</summary>
        public IEnumerator LoadMap(string bundleRelative, Action<bool> done, Action<string>? log)
        {
            if (LoadedMapBundle == bundleRelative)
            {
                done(true);
                yield break;
            }
            yield return UnloadMap();
            AssetBundle? b = Load(bundleRelative, log);
            if (b == null || !b.isStreamedSceneAssetBundle)
            {
                log?.Invoke("HS2 map bundle not usable: " + bundleRelative);
                done(false);
                yield break;
            }
            string scenePath = b.GetAllScenePaths()[0];
            AsyncOperation? op = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);
            if (op == null)
            {
                done(false);
                yield break;
            }
            while (!op.isDone)
                yield return null;
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            if (!scene.IsValid())
                scene = SceneManager.GetSceneByName(Path.GetFileNameWithoutExtension(scenePath));
            LoadedMapScene = scene.name;
            LoadedMapBundle = bundleRelative;
            // The map's own lighting settings (ambient, skybox, fog) apply only to the active scene.
            if (scene.IsValid())
                SceneManager.SetActiveScene(scene);
            int fixedCount = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                // HS2 maps carry their own cameras, lights and event systems; ours drive the game.
                foreach (Camera c in root.GetComponentsInChildren<Camera>(true))
                    c.enabled = false;
                foreach (AudioListener l in root.GetComponentsInChildren<AudioListener>(true))
                    l.enabled = false;
                foreach (UnityEngine.EventSystems.EventSystem es in root.GetComponentsInChildren<UnityEngine.EventSystems.EventSystem>(true))
                    es.gameObject.SetActive(false);
                fixedCount += FixShaders(root);
            }
            log?.Invoke("HS2 map loaded: " + scene.name + " (" + fixedCount + " materials re-shaded)");
            done(true);
        }

        public IEnumerator UnloadMap()
        {
            if (LoadedMapScene == null)
                yield break;
            Scene s = SceneManager.GetSceneByName(LoadedMapScene);
            if (s.IsValid() && s.isLoaded)
            {
                AsyncOperation op = SceneManager.UnloadSceneAsync(s);
                while (op != null && !op.isDone)
                    yield return null;
            }
            if (LoadedMapBundle != null && _loaded.TryGetValue(LoadedMapBundle, out AssetBundle b) && b != null)
            {
                b.Unload(true);
                _loaded.Remove(LoadedMapBundle);
            }
            LoadedMapScene = null;
            LoadedMapBundle = null;
        }

        /// <summary>Bounds of everything rendered in the loaded map, to place the cast and camera.</summary>
        public Bounds? MapBounds()
        {
            if (LoadedMapScene == null)
                return null;
            Scene s = SceneManager.GetSceneByName(LoadedMapScene);
            Bounds? b = null;
            foreach (GameObject root in s.GetRootGameObjects())
            {
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled || r is ParticleSystemRenderer)
                        continue;
                    if (b == null)
                        b = r.bounds;
                    else
                    {
                        Bounds bb = b.Value;
                        bb.Encapsulate(r.bounds);
                        b = bb;
                    }
                }
            }
            return b;
        }

        // ---- characters ----------------------------------------------------------------

        public GameObject? Instantiate(string bundleRelative, string assetName, Transform parent, Action<string>? log)
        {
            AssetBundle? b = Load(bundleRelative, log);
            if (b == null)
                return null;
            GameObject prefab = b.LoadAsset<GameObject>(assetName);
            if (prefab == null)
            {
                log?.Invoke("HS2 asset not found: " + bundleRelative + " :: " + assetName);
                return null;
            }
            GameObject go = UnityEngine.Object.Instantiate(prefab, parent, false);
            FixShaders(go);
            return go;
        }

        // ---- shaders -------------------------------------------------------------------

        /// <summary>Swaps shaders this Unity version cannot run for Standard, keeping the main maps.</summary>
        public int FixShaders(GameObject root)
        {
            _fallback ??= Shader.Find("Standard");
            int count = 0;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (m == null || m.shader == null || (m.shader.isSupported && !m.shader.name.StartsWith("Hidden/InternalErrorShader")))
                        continue;
                    var replacement = new Material(_fallback) { name = m.name + " (std)" };
                    CopyTex(m, replacement, "_MainTex", "_MainTex");
                    CopyTex(m, replacement, "_BumpMap", "_BumpMap");
                    CopyTex(m, replacement, "_NormalMap", "_BumpMap");
                    if (m.HasProperty("_Color"))
                        replacement.color = m.color;
                    if (m.HasProperty("_Cutoff") || m.name.Contains("cutout"))
                    {
                        replacement.SetFloat("_Mode", 1);
                        replacement.EnableKeyword("_ALPHATEST_ON");
                        replacement.renderQueue = 2450;
                    }
                    mats[i] = replacement;
                    changed = true;
                    count++;
                }
                if (changed)
                    r.sharedMaterials = mats;
            }
            return count;
        }

        private static void CopyTex(Material from, Material to, string src, string dst)
        {
            if (from.HasProperty(src) && from.GetTexture(src) != null)
            {
                to.SetTexture(dst, from.GetTexture(src));
                if (dst == "_BumpMap")
                    to.EnableKeyword("_NORMALMAP");
            }
        }
    }
}
