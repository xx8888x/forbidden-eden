using System;
using System.Collections.Generic;
using UnityEngine;

namespace ForbiddenEden.World
{
    /// <summary>
    /// Player appearance. Every slider has an adult lower bound (全員 18 歲以上): the
    /// clamps below keep height and head-to-body proportion inside adult ranges, so no
    /// combination of sliders can produce a child-like figure.
    /// </summary>
    [Serializable]
    public sealed class AvatarData
    {
        // ---- 成年下限（驗收數值） ----
        public const float HeightMin = 0.96f;   // ×1.72 m (男) = 1.65 m，×1.62 m (女) = 1.56 m
        public const float HeightMax = 1.10f;
        public const float HeadMin = 0.92f;
        public const float HeadMax = 1.02f;     // 頭身比不低於約 1 : 7
        public const float BuildMin = 0.88f;
        public const float BuildMax = 1.18f;
        public const float ShoulderMin = 0.90f;
        public const float ShoulderMax = 1.15f;

        public bool male = true;
        public float height = 1f;
        public float build = 1f;
        public float shoulder = 1f;
        public float head = 0.97f;
        public float skin = 0.35f;
        public float hairHue = 0.08f;
        public float hairValue = 0.25f;
        public int hairStyle;      // 0 短髮 / 1 長髮 / 2 馬尾 / 3 側分
        public float eyeHue = 0.58f;
        public int outfit;         // 0 制服 / 1 便服
        public float outfitHue = 0.62f;
        public int accessory;      // 0 無 / 1 眼鏡 / 2 髮飾
        public float faceWidth = 1f;
        public float jaw = 1f;
        /// <summary>HS2 parts worn by the player's figure (hair, clothes, accessories and their colours).</summary>
        public Hs2Look look = Hs2Looks.Uniform(false);

        public static readonly string[] HairStyleNames = { "短髮", "長髮", "馬尾", "側分" };
        public static readonly string[] OutfitNames = { "制服", "便服" };
        public static readonly string[] AccessoryNames = { "無", "眼鏡", "髮飾" };

        public static AvatarData Default(bool male) => new AvatarData
        {
            male = male,
            hairStyle = male ? 0 : 1,
            hairHue = male ? 0.07f : 0.05f,
            hairValue = male ? 0.22f : 0.32f,
            look = Hs2Looks.Uniform(!male),
        };

        public void Clamp()
        {
            height = Mathf.Clamp(height, HeightMin, HeightMax);
            head = Mathf.Clamp(head, HeadMin, HeadMax);
            build = Mathf.Clamp(build, BuildMin, BuildMax);
            shoulder = Mathf.Clamp(shoulder, ShoulderMin, ShoulderMax);
            faceWidth = Mathf.Clamp(faceWidth, 0.92f, 1.08f);
            jaw = Mathf.Clamp(jaw, 0.92f, 1.08f);
            skin = Mathf.Clamp01(skin);
            hairStyle = Mathf.Clamp(hairStyle, 0, HairStyleNames.Length - 1);
            outfit = Mathf.Clamp(outfit, 0, OutfitNames.Length - 1);
            accessory = Mathf.Clamp(accessory, 0, AccessoryNames.Length - 1);
            // Older cards have no HS2 look, and a gender change swaps the outfit set.
            if (look == null || look.Female == male)
                look = Hs2Looks.Uniform(!male);
            look.HairColor = HairColor;
            look.EyeColor = EyeColor;
            if (look.Face == null || look.Face.Length != Hs2FaceShape.Count)
                look.Face = Hs2Look.DefaultFace();
            // Adult proportions: eye width and height stay under 0.7.
            look.Face[22] = Mathf.Min(look.Face[22], 0.70f);
            look.Face[23] = Mathf.Min(look.Face[23], 0.70f);
        }

        public AvatarData Clone() => JsonUtility.FromJson<AvatarData>(JsonUtility.ToJson(this));

        public Color SkinColor => Color.Lerp(new Color(1f, 0.87f, 0.78f), new Color(0.62f, 0.43f, 0.32f), skin);
        public Color HairColor => Color.HSVToRGB(hairHue, 0.55f, Mathf.Lerp(0.12f, 0.95f, hairValue));
        public Color EyeColor => Color.HSVToRGB(eyeHue, 0.6f, 0.45f);
        public Color TopColor => outfit == 0 ? new Color(0.93f, 0.94f, 0.97f) : Color.HSVToRGB(outfitHue, 0.45f, 0.75f);
        public Color JacketColor => outfit == 0 ? Color.HSVToRGB(outfitHue, 0.55f, 0.30f) : Color.HSVToRGB(outfitHue, 0.35f, 0.55f);
        public Color BottomColor => outfit == 0 ? Color.HSVToRGB(outfitHue, 0.40f, 0.22f) : new Color(0.25f, 0.27f, 0.33f);

        /// <summary>A stable look for an NPC derived from its id, so the same person always looks the same.</summary>
        public static AvatarData ForNpc(string npcId, string gender)
        {
            int h = 17;
            foreach (char c in npcId ?? string.Empty)
                h = unchecked(h * 31 + c);
            var rng = new System.Random(h);
            bool male = gender == "男" || gender == "male" || gender == "M";
            var a = Default(male);
            a.height = Mathf.Lerp(HeightMin + 0.01f, HeightMax - 0.02f, (float)rng.NextDouble());
            a.build = Mathf.Lerp(0.92f, 1.10f, (float)rng.NextDouble());
            a.skin = (float)rng.NextDouble() * 0.45f;
            a.hairHue = (float)rng.NextDouble();
            a.hairValue = (float)rng.NextDouble();
            a.hairStyle = male ? (rng.Next(2) == 0 ? 0 : 3) : rng.Next(1, 4);
            a.eyeHue = (float)rng.NextDouble();
            a.outfitHue = 0.55f + (float)rng.NextDouble() * 0.15f;
            a.accessory = rng.Next(5) == 0 ? 1 : (rng.Next(4) == 0 && !male ? 2 : 0);
            a.Clamp();
            return a;
        }
    }

    /// <summary>
    /// An original stylised figure assembled from primitives. It stands in for the final
    /// character models until the art pipeline (VRM or a commercial kit) is chosen.
    /// Faces -Z, the camera side.
    /// </summary>
    public sealed class Mannequin : MonoBehaviour
    {
        public AvatarData Data = AvatarData.Default(true);
        public bool Talking;
        public string DisplayName = string.Empty;

        private readonly List<GameObject> _parts = new List<GameObject>();
        private Transform _body;
        private Transform _torso;
        private Transform _head;
        private float _phase;
        private float _talk;

        public Transform Head => _head;

        public static Mannequin Create(Transform parent, string name, AvatarData data)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var m = go.AddComponent<Mannequin>();
            m._phase = UnityEngine.Random.value * 10f;
            m.Apply(data);
            return m;
        }

        /// <summary>Height of the eyes above the feet, for framing.</summary>
        public float EyeHeight => (Data.male ? 1.60f : 1.50f) * Data.height;

        public void Apply(AvatarData data)
        {
            Data = data.Clone();
            Data.Clamp();
            Rebuild();
        }

        private void Rebuild()
        {
            foreach (GameObject p in _parts)
            {
                if (p != null)
                    Destroy(p);
            }
            _parts.Clear();

            AvatarData d = Data;
            float s = (d.male ? 1.72f : 1.62f) / 1.72f * d.height;
            float w = d.build * (d.male ? 1.0f : 0.88f);
            float sh = d.shoulder * (d.male ? 1.0f : 0.86f);

            _body = Group("Body", transform, Vector3.zero);
            _body.localScale = new Vector3(s, s, s);

            // Legs
            Color legColor = d.male || d.outfit == 1 ? d.BottomColor : d.SkinColor;
            Part(PrimitiveType.Capsule, "LegL", _body, new Vector3(-0.09f * w, 0.42f, 0), new Vector3(0.13f * w, 0.42f, 0.13f * w), legColor);
            Part(PrimitiveType.Capsule, "LegR", _body, new Vector3(0.09f * w, 0.42f, 0), new Vector3(0.13f * w, 0.42f, 0.13f * w), legColor);
            Part(PrimitiveType.Cube, "ShoeL", _body, new Vector3(-0.09f * w, 0.03f, -0.04f), new Vector3(0.11f, 0.06f, 0.24f), new Color(0.12f, 0.12f, 0.15f));
            Part(PrimitiveType.Cube, "ShoeR", _body, new Vector3(0.09f * w, 0.03f, -0.04f), new Vector3(0.11f, 0.06f, 0.24f), new Color(0.12f, 0.12f, 0.15f));
            if (!d.male && d.outfit == 0)
            {
                // Pleated uniform skirt, knee length.
                Part(PrimitiveType.Cylinder, "Skirt", _body, new Vector3(0, 0.80f, 0), new Vector3(0.40f * w, 0.14f, 0.34f * w), d.BottomColor);
            }
            else
            {
                Part(PrimitiveType.Cube, "Hips", _body, new Vector3(0, 0.86f, 0), new Vector3(0.32f * w, 0.16f, 0.20f * w), d.BottomColor);
            }

            // Torso
            _torso = Group("Torso", _body, new Vector3(0, 0.92f, 0));
            Part(PrimitiveType.Capsule, "Chest", _torso, new Vector3(0, 0.26f, 0), new Vector3(0.34f * w * sh, 0.30f, 0.21f * w), d.TopColor);
            if (d.outfit == 0)
            {
                // Blazer panels left open over the shirt, and a ribbon/tie.
                Part(PrimitiveType.Cube, "BlazerL", _torso, new Vector3(-0.10f * sh, 0.24f, -0.075f * w), new Vector3(0.12f * sh, 0.46f, 0.07f), d.JacketColor);
                Part(PrimitiveType.Cube, "BlazerR", _torso, new Vector3(0.10f * sh, 0.24f, -0.075f * w), new Vector3(0.12f * sh, 0.46f, 0.07f), d.JacketColor);
                Part(PrimitiveType.Cube, "Tie", _torso, new Vector3(0, 0.38f, -0.105f * w), new Vector3(0.05f, d.male ? 0.18f : 0.06f, 0.02f), new Color(0.75f, 0.18f, 0.25f));
            }
            Part(PrimitiveType.Sphere, "ShoulderL", _torso, new Vector3(-0.19f * sh, 0.46f, 0), Vector3.one * 0.12f, d.outfit == 0 ? d.JacketColor : d.TopColor);
            Part(PrimitiveType.Sphere, "ShoulderR", _torso, new Vector3(0.19f * sh, 0.46f, 0), Vector3.one * 0.12f, d.outfit == 0 ? d.JacketColor : d.TopColor);
            Transform armL = Group("ArmL", _torso, new Vector3(-0.21f * sh, 0.46f, 0));
            Transform armR = Group("ArmR", _torso, new Vector3(0.21f * sh, 0.46f, 0));
            armL.localRotation = Quaternion.Euler(0, 0, -6);
            armR.localRotation = Quaternion.Euler(0, 0, 6);
            Part(PrimitiveType.Capsule, "Sleeve", armL, new Vector3(0, -0.24f, 0), new Vector3(0.09f, 0.26f, 0.09f), d.outfit == 0 ? d.JacketColor : d.TopColor);
            Part(PrimitiveType.Capsule, "Sleeve", armR, new Vector3(0, -0.24f, 0), new Vector3(0.09f, 0.26f, 0.09f), d.outfit == 0 ? d.JacketColor : d.TopColor);
            Part(PrimitiveType.Sphere, "Hand", armL, new Vector3(0, -0.53f, 0), Vector3.one * 0.075f, d.SkinColor);
            Part(PrimitiveType.Sphere, "Hand", armR, new Vector3(0, -0.53f, 0), Vector3.one * 0.075f, d.SkinColor);
            Part(PrimitiveType.Cylinder, "Neck", _torso, new Vector3(0, 0.57f, 0), new Vector3(0.08f, 0.05f, 0.08f), d.SkinColor);

            // Head
            _head = Group("Head", _torso, new Vector3(0, 0.62f, 0));
            float hs = 0.23f * d.head;
            Part(PrimitiveType.Sphere, "Face", _head, new Vector3(0, hs * 0.5f, 0), new Vector3(hs * 0.86f * d.faceWidth, hs * 1.05f, hs * 0.92f), d.SkinColor);
            Part(PrimitiveType.Sphere, "Jaw", _head, new Vector3(0, hs * 0.22f, -hs * 0.06f), new Vector3(hs * 0.62f * d.jaw, hs * 0.5f, hs * 0.7f), d.SkinColor);
            Part(PrimitiveType.Sphere, "EyeL", _head, new Vector3(-hs * 0.19f, hs * 0.52f, -hs * 0.40f), new Vector3(hs * 0.13f, hs * 0.16f, hs * 0.06f), d.EyeColor);
            Part(PrimitiveType.Sphere, "EyeR", _head, new Vector3(hs * 0.19f, hs * 0.52f, -hs * 0.40f), new Vector3(hs * 0.13f, hs * 0.16f, hs * 0.06f), d.EyeColor);
            Part(PrimitiveType.Cube, "Mouth", _head, new Vector3(0, hs * 0.22f, -hs * 0.40f), new Vector3(hs * 0.16f, hs * 0.025f, hs * 0.02f), new Color(0.65f, 0.35f, 0.35f));

            // Hair
            Color hair = d.HairColor;
            Part(PrimitiveType.Sphere, "HairCap", _head, new Vector3(0, hs * 0.66f, hs * 0.05f), new Vector3(hs * 0.95f * d.faceWidth, hs * 0.86f, hs * 0.98f), hair);
            Part(PrimitiveType.Cube, "Bangs", _head, new Vector3(d.hairStyle == 3 ? hs * 0.12f : 0, hs * 0.86f, -hs * 0.36f), new Vector3(hs * 0.80f, hs * 0.22f, hs * 0.12f), hair);
            switch (d.hairStyle)
            {
                case 1:
                    Part(PrimitiveType.Capsule, "LongHair", _head, new Vector3(0, hs * 0.05f, hs * 0.30f), new Vector3(hs * 0.92f, hs * 1.25f, hs * 0.40f), hair);
                    break;
                case 2:
                    Part(PrimitiveType.Sphere, "Knot", _head, new Vector3(0, hs * 0.70f, hs * 0.52f), Vector3.one * hs * 0.32f, hair);
                    Part(PrimitiveType.Capsule, "Tail", _head, new Vector3(0, hs * 0.15f, hs * 0.62f), new Vector3(hs * 0.24f, hs * 0.55f, hs * 0.24f), hair);
                    break;
                case 3:
                    Part(PrimitiveType.Sphere, "Side", _head, new Vector3(-hs * 0.40f, hs * 0.55f, 0), new Vector3(hs * 0.25f, hs * 0.55f, hs * 0.6f), hair);
                    break;
            }
            if (d.accessory == 1)
            {
                Color frame = new Color(0.12f, 0.12f, 0.14f);
                Part(PrimitiveType.Cube, "GlassL", _head, new Vector3(-hs * 0.19f, hs * 0.52f, -hs * 0.45f), new Vector3(hs * 0.26f, hs * 0.18f, hs * 0.01f), new Color(0.75f, 0.85f, 0.95f, 1f));
                Part(PrimitiveType.Cube, "GlassR", _head, new Vector3(hs * 0.19f, hs * 0.52f, -hs * 0.45f), new Vector3(hs * 0.26f, hs * 0.18f, hs * 0.01f), new Color(0.75f, 0.85f, 0.95f, 1f));
                Part(PrimitiveType.Cube, "Bridge", _head, new Vector3(0, hs * 0.55f, -hs * 0.46f), new Vector3(hs * 0.12f, hs * 0.03f, hs * 0.01f), frame);
            }
            else if (d.accessory == 2)
            {
                Part(PrimitiveType.Cube, "Clip", _head, new Vector3(hs * 0.33f, hs * 0.86f, -hs * 0.20f), new Vector3(hs * 0.22f, hs * 0.08f, hs * 0.06f), Color.HSVToRGB(d.outfitHue + 0.4f, 0.6f, 0.95f));
            }
        }

        private Transform Group(string name, Transform parent, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            _parts.Add(go);
            return go.transform;
        }

        private void Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            Mats.Paint(go, color, 0.25f);
            _parts.Add(go);
        }

        private void Update()
        {
            if (_torso == null)
                return;
            float t = Time.time + _phase;
            _talk = Mathf.MoveTowards(_talk, Talking ? 1f : 0f, Time.deltaTime * 3f);
            float breath = Mathf.Sin(t * 1.6f) * 0.012f;
            _torso.localScale = new Vector3(1f + breath * 0.5f, 1f + breath, 1f + breath * 0.5f);
            _torso.localRotation = Quaternion.Euler(0, Mathf.Sin(t * 0.5f) * 2.5f, Mathf.Sin(t * 0.7f) * 1.2f);
            if (_head != null)
            {
                float nod = Mathf.Sin(t * 7f) * 4f * _talk;
                _head.localRotation = Quaternion.Euler(nod + Mathf.Sin(t * 0.9f) * 2f, Mathf.Sin(t * 0.43f) * 6f, 0);
            }
        }
    }
}
