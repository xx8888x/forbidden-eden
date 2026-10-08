using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ForbiddenEden.World
{
    /// <summary>What an HS2-based character wears: catalogue ids per slot (-1 = nothing) and colours.</summary>
    [Serializable]
    public sealed class Hs2Look
    {
        public bool Female = true;
        public int HairB = -1, HairF = -1, HairS = -1, HairO = -1;
        public int Top = -1, Bot = -1, Socks = -1, Shoes = -1, Gloves = -1, Panst = -1;
        /// <summary>Accessories as "slot:id", e.g. "acc_head:4".</summary>
        public List<string> Accessories = new List<string>();
        public Color HairColor = new Color(0.20f, 0.13f, 0.10f);
        public Color EyeColor = new Color(0.30f, 0.20f, 0.14f);
        /// <summary>HS2's 59 face sliders (0–1, 0.5 = default); see <see cref="Hs2FaceShape.Names"/>.</summary>
        public float[] Face = DefaultFace();

        public static float[] DefaultFace()
        {
            var f = new float[Hs2FaceShape.Count];
            for (int i = 0; i < f.Length; i++)
                f[i] = 0.5f;
            return f;
        }
        /// <summary>Per-slot colours 1–4 overriding the part's defaults ("top" → 4 colours).</summary>
        public List<SlotColors> Colors = new List<SlotColors>();

        [Serializable]
        public sealed class SlotColors
        {
            public string Slot;
            public Color[] Values = new Color[4];
        }

        public Color[] ColorsFor(string slot) => Colors.FirstOrDefault(c => c.Slot == slot)?.Values;

        public void SetColors(string slot, Color[] values)
        {
            Colors.RemoveAll(c => c.Slot == slot);
            if (values != null)
                Colors.Add(new SlotColors { Slot = slot, Values = (Color[])values.Clone() });
        }

        public int Get(string slot)
        {
            switch (slot)
            {
                case "hair_b": return HairB;
                case "hair_f": return HairF;
                case "hair_s": return HairS;
                case "hair_o": return HairO;
                case "top": return Top;
                case "bot": return Bot;
                case "socks": return Socks;
                case "shoes": return Shoes;
                case "gloves": return Gloves;
                case "panst": return Panst;
            }
            string acc = Accessories.FirstOrDefault(a => a.StartsWith(slot + ":"));
            return acc != null && int.TryParse(acc.Substring(slot.Length + 1), out int id) ? id : -1;
        }

        public void Set(string slot, int id)
        {
            switch (slot)
            {
                case "hair_b": HairB = id; return;
                case "hair_f": HairF = id; return;
                case "hair_s": HairS = id; return;
                case "hair_o": HairO = id; return;
                case "top": Top = id; return;
                case "bot": Bot = id; return;
                case "socks": Socks = id; return;
                case "shoes": Shoes = id; return;
                case "gloves": Gloves = id; return;
                case "panst": Panst = id; return;
            }
            Accessories.RemoveAll(a => a.StartsWith(slot + ":"));
            if (id >= 0)
                Accessories.Add(slot + ":" + id);
        }

        public static readonly string[] WearSlots = { "hair_b", "hair_f", "hair_s", "hair_o", "top", "bot", "socks", "shoes", "gloves", "panst" };

        public IEnumerable<(string slot, int id)> All()
        {
            foreach (string s in WearSlots)
            {
                int id = Get(s);
                if (id >= 0)
                    yield return (s, id);
            }
            foreach (string a in Accessories)
            {
                int i = a.IndexOf(':');
                if (i > 0 && int.TryParse(a.Substring(i + 1), out int id))
                    yield return (a.Substring(0, i), id);
            }
        }

        public Hs2Look Clone() => JsonUtility.FromJson<Hs2Look>(JsonUtility.ToJson(this));
    }

    /// <summary>
    /// HS2 ChaControl in miniature: sits on an exported base body (Resources/HS2/FemaleBase|MaleBase) and dresses it
    /// from the parts catalogue. Clothes meshes are re-bound to the body's bones by name; hair and accessories are
    /// parented to their attach node; spring chains get the body's colliders; colours go through property blocks.
    /// </summary>
    public sealed class Hs2Avatar : MonoBehaviour
    {
        public bool Female = true;
        public SkinnedMeshRenderer Body;
        public Mesh BodyFull;
        public Mesh BodyNoFeet;

        private Dictionary<string, Transform> _bones;
        private Transform _meshRoot;
        private readonly Dictionary<string, GameObject> _worn = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, Hs2Part> _wornPart = new Dictionary<string, Hs2Part>();
        private Hs2Look _look = new Hs2Look();
        private MaterialPropertyBlock _block;

        public Hs2Look Look => _look;

        public static Hs2Avatar Create(Transform parent, Hs2Look look, string name)
        {
            var prefab = Resources.Load<GameObject>(look.Female ? "HS2/FemaleBase" : "HS2/MaleBase");
            if (prefab == null)
                return null;
            GameObject go = Instantiate(prefab, parent, false);
            go.name = name;
            var avatar = go.GetComponent<Hs2Avatar>();
            avatar.Apply(look);
            return avatar;
        }

        private void EnsureBones()
        {
            if (_bones != null)
                return;
            _bones = new Dictionary<string, Transform>();
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (!_bones.ContainsKey(t.name))
                    _bones[t.name] = t;
            }
            _meshRoot = _bones.TryGetValue("Meshes", out Transform mr) ? mr : transform;
        }

        /// <summary>Dresses the body; only slots whose part changed are rebuilt.</summary>
        public void Apply(Hs2Look look)
        {
            EnsureBones();
            Hs2Catalog catalog = Hs2Catalog.Load();
            var wanted = new Dictionary<string, Hs2CatalogItem>();
            foreach ((string slot, int id) in look.All())
            {
                Hs2CatalogItem item = catalog.Find(slot, id, Female);
                if (item != null)
                    wanted[slot] = item;
            }
            foreach (string slot in _worn.Keys.ToList())
            {
                if (!wanted.TryGetValue(slot, out Hs2CatalogItem item) || _wornPart[slot] == null || _wornPart[slot].Id != item.Id)
                    Remove(slot);
            }
            foreach (KeyValuePair<string, Hs2CatalogItem> kv in wanted)
            {
                if (!_worn.ContainsKey(kv.Key))
                    Wear(kv.Key, kv.Value);
            }
            // One-piece tops (dresses, swimsuits) take the bottom slot too.
            if (_wornPart.TryGetValue("top", out Hs2Part top) && top != null && top.FullBody)
                Remove("bot");
            _look = look.Clone();
            RefreshColors();
            Hs2FaceShape face = GetComponent<Hs2FaceShape>();
            if (face == null)
            {
                face = gameObject.AddComponent<Hs2FaceShape>();
                if (!Female)
                    face.UseMaleTables();
            }
            face.SetAll(look.Face);
            bool feetHidden = _wornPart.Values.Any(p => p != null && p.HidesFeet);
            if (Body != null && BodyNoFeet != null && BodyFull != null)
                Body.sharedMesh = feetHidden ? BodyNoFeet : BodyFull;
        }

        private void Remove(string slot)
        {
            if (_worn.TryGetValue(slot, out GameObject go) && go != null)
            {
                if (Application.isPlaying)
                    Destroy(go);
                else
                    DestroyImmediate(go); // editor previews
            }
            _worn.Remove(slot);
            _wornPart.Remove(slot);
        }

        private void Wear(string slot, Hs2CatalogItem item)
        {
            var prefab = Resources.Load<GameObject>(item.Prefab);
            if (prefab == null)
                return;
            Hs2Part template = prefab.GetComponent<Hs2Part>();
            GameObject go;
            if (!string.IsNullOrEmpty(template.AttachTo))
            {
                Transform parent = _bones.TryGetValue(template.AttachTo, out Transform t) ? t : transform;
                go = Instantiate(prefab, parent, false);
                foreach (DynamicBone db in go.GetComponentsInChildren<DynamicBone>(true))
                    db.m_Colliders = Colliders();
            }
            else
            {
                go = new GameObject(prefab.name);
                go.transform.SetParent(_meshRoot, false);
                Transform hips = _bones.TryGetValue("cf_J_Hips", out Transform h) ? h : transform;
                foreach (Hs2PartMesh pm in template.Meshes)
                {
                    var child = new GameObject(pm.Name);
                    child.transform.SetParent(go.transform, false);
                    var smr = child.AddComponent<SkinnedMeshRenderer>();
                    smr.bones = pm.Bones.Select(n => _bones.TryGetValue(n, out Transform b) ? b : hips).ToArray();
                    smr.sharedMesh = pm.Mesh;
                    smr.sharedMaterials = pm.Materials;
                    smr.rootBone = hips;
                    // HS2 builds at 10x; a whole-body box around the hips keeps culling stable in any pose.
                    smr.localBounds = new Bounds(new Vector3(0, -2, 0), new Vector3(20, 20, 20));
                    if (pm.BlendWeights != null)
                    {
                        for (int i = 0; i < pm.BlendWeights.Length && i < pm.Mesh.blendShapeCount; i++)
                            smr.SetBlendShapeWeight(i, pm.BlendWeights[i]);
                    }
                }
                for (int i = 0; i < template.Springs.Length; i++)
                {
                    if (!_bones.TryGetValue(template.SpringRoots[i], out Transform root))
                        continue;
                    var db = go.AddComponent<DynamicBone>();
                    JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(template.Springs[i]), db);
                    db.m_Root = root;
                    db.m_Colliders = Colliders();
                    db.m_Exclusions = new List<Transform>();
                    db.enabled = true;
                }
                var info = go.AddComponent<Hs2Part>();
                info.Category = template.Category;
                info.Id = template.Id;
                info.DefaultColors = template.DefaultColors;
                info.UseColor = template.UseColor;
                info.HidesFeet = template.HidesFeet;
                info.FullBody = template.FullBody;
            }
            _worn[slot] = go;
            _wornPart[slot] = go.GetComponent<Hs2Part>();
        }

        private List<DynamicBoneColliderBase> _colliders;

        private List<DynamicBoneColliderBase> Colliders()
        {
            if (_colliders == null)
                _colliders = GetComponentsInChildren<DynamicBoneColliderBase>(true).Where(c => c.transform.IsChildOf(transform)).ToList();
            return new List<DynamicBoneColliderBase>(_colliders);
        }

        /// <summary>Pushes the look's colours (or each part's HS2 defaults) to the worn parts and the hair.</summary>
        public void RefreshColors()
        {
            if (_block == null)
                _block = new MaterialPropertyBlock();
            Color hair = _look.HairColor;
            foreach (KeyValuePair<string, GameObject> kv in _worn)
            {
                if (kv.Value == null)
                    continue;
                Hs2Part part = _wornPart[kv.Key];
                Color[] colors = _look.ColorsFor(kv.Key) ?? part?.DefaultColors;
                foreach (Renderer r in kv.Value.GetComponentsInChildren<Renderer>(true))
                {
                    r.GetPropertyBlock(_block);
                    Material m = r.sharedMaterial;
                    if (m != null && m.shader.name == "EDEN/HS2 Hair")
                    {
                        _block.SetColor("_Color", hair);
                        _block.SetColor("_TopColor", hair * 0.8f);
                        _block.SetColor("_UnderColor", Color.Lerp(hair, Color.white, 0.12f));
                    }
                    else if (colors != null && colors.Length >= 4)
                    {
                        _block.SetColor("_Color", colors[0]);
                        _block.SetColor("_Color2", colors[1]);
                        _block.SetColor("_Color3", colors[2]);
                        _block.SetColor("_Color4", colors[3]);
                    }
                    r.SetPropertyBlock(_block);
                }
            }
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                Material m = r.sharedMaterial;
                if (m == null || m.shader.name != "EDEN/HS2 Eye")
                    continue;
                r.GetPropertyBlock(_block);
                _block.SetColor("_IrisColor", _look.EyeColor * 0.8f);
                _block.SetColor("_IrisLower", Color.Lerp(_look.EyeColor, Color.white, 0.25f));
                r.SetPropertyBlock(_block);
            }
        }

        public Hs2Part PartIn(string slot) => _wornPart.TryGetValue(slot, out Hs2Part p) ? p : null;
    }
}
