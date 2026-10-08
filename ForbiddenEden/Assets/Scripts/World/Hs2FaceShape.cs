using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace ForbiddenEden.World
{
    /// <summary>
    /// HS2's face sliders on an exported head. Each slider (0–1, 0.5 = default) samples HS2's bone key table for
    /// its "source" entries; the generated map (Resources/HS2/Shape/head_f_map.txt) then sums source channels into
    /// the real face bones (cf_J_*). Data: <c>Hs2ShapeExporter</c> copies the key table and category list.
    /// </summary>
    public sealed class Hs2FaceShape : MonoBehaviour
    {
        public const int Count = 59;

        /// <summary>繁體中文 slider names in HS2's order.</summary>
        public static readonly string[] Names =
        {
            "臉寬", "臉上部前後", "臉上部上下", "臉下部前後", "臉下部寬", "下顎寬", "下顎上下", "下顎前後", "下顎角度", "下顎下部上下",
            "下巴寬", "下巴上下", "下巴前後", "下頰上下", "下頰前後", "下頰寬", "上頰上下", "上頰前後", "上頰寬", "眼睛上下",
            "眼距", "眼睛前後", "眼寬", "眼高", "眼角度（Z）", "眼角度（Y）", "內眼角左右", "外眼角左右", "內眼角上下", "外眼角上下",
            "眼瞼形狀 1", "眼瞼形狀 2", "鼻子上下", "鼻子前後", "鼻子角度", "鼻子寬", "鼻樑高", "鼻樑寬", "鼻樑形狀", "鼻翼寬",
            "鼻翼上下", "鼻翼前後", "鼻翼角度（X）", "鼻翼角度（Z）", "鼻尖高", "鼻尖角度", "鼻尖大小", "嘴巴上下", "嘴巴寬", "嘴巴高",
            "嘴巴前後", "上唇形狀", "下唇形狀", "嘴角", "耳朵大小", "耳朵角度（Y）", "耳朵角度（Z）", "耳朵上部形狀", "耳朵下部形狀",
        };

        public string KeyTable = "HS2/Shape/cf_anmShapeHead_00";
        public string CategoryTable = "HS2/Shape/cf_customhead";

        /// <summary>Female heads use the cf_ tables, male heads the cm_ ones; the bone map is shared.</summary>
        public void UseMaleTables()
        {
            KeyTable = "HS2/Shape/cm_anmShapeHead_00";
            CategoryTable = "HS2/Shape/cm_customhead";
            _loaded = false;
        }
        public string Map = "HS2/Shape/head_f_map";

        private readonly float[] _values = new float[Count];
        private Dictionary<string, List<(Vector3 pos, Vector3 rot, Vector3 scl)>> _keys;
        private readonly Dictionary<int, List<(string src, bool[] use)>> _categories = new Dictionary<int, List<(string, bool[])>>();
        private readonly Dictionary<string, (Vector3 pos, Vector3 rot, Vector3 scl)> _src = new Dictionary<string, (Vector3, Vector3, Vector3)>();
        private readonly List<(Transform bone, int channel, (string src, int comp)[] terms, float constant)> _map = new List<(Transform, int, (string, int)[], float)>();
        private readonly Dictionary<Transform, Vector3> _euler = new Dictionary<Transform, Vector3>();
        private bool _ready;
        private bool _dirty = true;

        public bool Ready => _ready;

        private bool _loaded;

        public Hs2FaceShape()
        {
            for (int i = 0; i < Count; i++)
                _values[i] = 0.5f;
        }

        /// <summary>Loads the tables on first use (also works in edit mode, where Awake does not run for added components).</summary>
        private void EnsureLoaded()
        {
            if (_loaded)
                return;
            _loaded = true;
            try
            {
                Load();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Hs2FaceShape: " + ex.Message);
                _ready = false;
            }
        }

        public float Get(int index) => _values[index];

        public void Set(int index, float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(_values[index], value))
                return;
            _values[index] = value;
            _dirty = true;
        }

        public void SetAll(float[] values)
        {
            if (values == null)
                return;
            for (int i = 0; i < Count && i < values.Length; i++)
                _values[i] = Mathf.Clamp01(values[i]);
            _dirty = true;
        }

        private void Load()
        {
            var keyAsset = Resources.Load<TextAsset>(KeyTable);
            var catAsset = Resources.Load<TextAsset>(CategoryTable);
            var mapAsset = Resources.Load<TextAsset>(Map);
            if (keyAsset == null || catAsset == null || mapAsset == null)
                return;

            // Key table: int count; per entry: string name, int keys, keys × (int no, pos xyz, rot xyz, scl xyz).
            _keys = new Dictionary<string, List<(Vector3, Vector3, Vector3)>>();
            using (var r = new BinaryReader(new MemoryStream(keyAsset.bytes)))
            {
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    string name = r.ReadString();
                    int k = r.ReadInt32();
                    var list = new List<(Vector3, Vector3, Vector3)>(k);
                    for (int j = 0; j < k; j++)
                    {
                        r.ReadInt32();
                        var pos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        var rot = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        var scl = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        list.Add((pos, rot, scl));
                    }
                    _keys[name] = list;
                }
            }

            // Category list: slider index, source name, then pos/rot/scl × xyz use flags.
            foreach (string line in Encoding.UTF8.GetString(catAsset.bytes).Split('\n'))
            {
                string[] f = line.TrimEnd('\r').Split('\t');
                if (f.Length < 11 || !int.TryParse(f[0], out int cat))
                    continue;
                var use = new bool[9];
                for (int i = 0; i < 9; i++)
                    use[i] = f[2 + i] != "0";
                if (!_categories.TryGetValue(cat, out var entries))
                    _categories[cat] = entries = new List<(string, bool[])>();
                entries.Add((f[1], use));
                _src[f[1]] = (Vector3.zero, Vector3.zero, Vector3.one);
            }

            var bones = new Dictionary<string, Transform>();
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (!bones.ContainsKey(t.name))
                    bones[t.name] = t;
            }
            const string channels = "pxpypzrxryrzsxsysz";
            foreach (string line in mapAsset.text.Split('\n'))
            {
                if (line.StartsWith("#") || line.Trim().Length == 0)
                    continue;
                string[] f = line.TrimEnd('\r').Split('\t');
                if (f.Length < 3 || !bones.TryGetValue(f[0], out Transform bone))
                    continue;
                int channel = channels.IndexOf(f[1], StringComparison.Ordinal) / 2;
                if (float.TryParse(f[2], out float constant))
                {
                    _map.Add((bone, channel, Array.Empty<(string, int)>(), constant));
                    continue;
                }
                string[] parts = f[2].Split('+');
                var terms = new (string, int)[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    int dot = parts[i].LastIndexOf('.');
                    terms[i] = (parts[i].Substring(0, dot), channels.IndexOf(parts[i].Substring(dot + 1), StringComparison.Ordinal) / 2);
                }
                _map.Add((bone, channel, terms, 0f));
            }
            _ready = _keys.Count > 0 && _map.Count > 0;
        }

        private static Vector3 Sample(List<(Vector3 pos, Vector3 rot, Vector3 scl)> keys, float rate, int part)
        {
            float f = (keys.Count - 1) * rate;
            int i = Mathf.Clamp(Mathf.FloorToInt(f), 0, keys.Count - 1);
            int j = Mathf.Min(i + 1, keys.Count - 1);
            float t = f - i;
            switch (part)
            {
                case 0: return Vector3.Lerp(keys[i].pos, keys[j].pos, t);
                case 1:
                    return new Vector3(Mathf.LerpAngle(keys[i].rot.x, keys[j].rot.x, t), Mathf.LerpAngle(keys[i].rot.y, keys[j].rot.y, t),
                        Mathf.LerpAngle(keys[i].rot.z, keys[j].rot.z, t));
                default: return Vector3.Lerp(keys[i].scl, keys[j].scl, t);
            }
        }

        private void Recalculate()
        {
            foreach (KeyValuePair<int, List<(string src, bool[] use)>> cat in _categories)
            {
                if (cat.Key < 0 || cat.Key >= Count)
                    continue;
                float rate = _values[cat.Key];
                foreach ((string src, bool[] use) in cat.Value)
                {
                    if (!_keys.TryGetValue(src, out var keys) || keys.Count == 0)
                        continue;
                    (Vector3 pos, Vector3 rot, Vector3 scl) s = _src[src];
                    Vector3 p = Sample(keys, rate, 0), r = Sample(keys, rate, 1), c = Sample(keys, rate, 2);
                    for (int a = 0; a < 3; a++)
                    {
                        if (use[a]) s.pos[a] = p[a];
                        if (use[3 + a]) s.rot[a] = r[a];
                        if (use[6 + a]) s.scl[a] = c[a];
                    }
                    _src[src] = s;
                }
            }
        }

        private float Term(string src, int comp)
        {
            if (!_src.TryGetValue(src, out (Vector3 pos, Vector3 rot, Vector3 scl) s))
                return comp >= 6 ? 1f : 0f;
            return comp < 3 ? s.pos[comp] : comp < 6 ? s.rot[comp - 3] : s.scl[comp - 6];
        }

        private void LateUpdate() => ApplyNow();

        /// <summary>Writes the current slider values into the face bones.</summary>
        public void ApplyNow()
        {
            EnsureLoaded();
            if (!_ready)
                return;
            if (_dirty)
            {
                Recalculate();
                _dirty = false;
            }
            // Applied every frame after animation so clips never undo the face shape.
            Dictionary<Transform, Vector3> euler = _euler;
            euler.Clear();
            foreach ((Transform bone, int channel, (string src, int comp)[] terms, float constant) in _map)
            {
                float v = constant;
                foreach ((string src, int comp) in terms)
                    v += Term(src, comp);
                if (channel < 3)
                {
                    Vector3 p = bone.localPosition;
                    p[channel] = v;
                    bone.localPosition = p;
                }
                else if (channel < 6)
                {
                    if (!euler.TryGetValue(bone, out Vector3 e))
                        e = Vector3.zero;
                    e[channel - 3] = v;
                    euler[bone] = e;
                }
                else
                {
                    Vector3 s = bone.localScale;
                    s[channel - 6] = v;
                    bone.localScale = s;
                }
            }
            foreach (KeyValuePair<Transform, Vector3> kv in euler)
                kv.Key.localRotation = Quaternion.Euler(kv.Value);
        }
    }
}
