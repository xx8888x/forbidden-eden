using System.Collections.Generic;
using UnityEngine;

namespace ForbiddenEden.World
{
    public enum SetKind { SkyPort, Campus, Classroom, Dorm, Library, Arena, Club, Rooftop, Registry, Staffroom, Pool }

    /// <summary>
    /// Builds the 3D backdrop from primitives: the floating campus of 天空都市 and a few
    /// interiors. It is placeholder set dressing in the final layout, sized so the
    /// cameras and UI can be designed against it.
    /// </summary>
    public sealed class Stage : MonoBehaviour
    {
        private Transform _set;
        private Transform _cast;
        private readonly Dictionary<string, GameObject> _actors = new Dictionary<string, GameObject>();
        private Light _sun;

        public SetKind Current { get; private set; } = (SetKind)(-1);
        /// <summary>The player's own figure (HS2 avatar, or the placeholder mannequin when HS2 parts are missing).</summary>
        public GameObject Player { get; private set; }

        public float PlayerEyeHeight { get; private set; } = 1.5f;

        private Mannequin _mannequinPlayer;
        private Hs2Avatar _hs2Player;

        /// <summary>True when the HS2 base bodies and parts catalogue were exported into the project.</summary>
        public static bool HasHs2Parts => Hs2Catalog.Load().Items.Count > 0 && Resources.Load<GameObject>("HS2/FemaleBase") != null;

        public static Stage Create()
        {
            var go = new GameObject("Stage");
            var stage = go.AddComponent<Stage>();
            stage._set = new GameObject("Set").transform;
            stage._set.SetParent(go.transform, false);
            stage._cast = new GameObject("Cast").transform;
            stage._cast.SetParent(go.transform, false);
            stage._sun = FindAnyObjectByType<Light>();
            if (stage._sun == null)
            {
                var lightGo = new GameObject("Sun");
                stage._sun = lightGo.AddComponent<Light>();
                stage._sun.type = LightType.Directional;
            }
            stage._sun.shadows = LightShadows.Soft;
            stage._sun.transform.rotation = Quaternion.Euler(42, -35, 0);
            return stage;
        }

        /// <summary>Picks a set from a scene id / display name such as "scene_dorm" or "3-A 教室".</summary>
        public static SetKind KindFor(string sceneId, string sceneName)
        {
            string s = (sceneId + " " + sceneName).ToLowerInvariant();
            if (s.Contains("登錄") || s.Contains("regist")) return SetKind.Registry;
            if (s.Contains("職員") || s.Contains("教師室") || s.Contains("staff") || s.Contains("faculty")) return SetKind.Staffroom;
            if (s.Contains("泳池") || s.Contains("游泳") || s.Contains("pool")) return SetKind.Pool;
            if (s.Contains("教室") || s.Contains("class")) return SetKind.Classroom;
            if (s.Contains("宿舍") || s.Contains("dorm")) return SetKind.Dorm;
            if (s.Contains("圖書") || s.Contains("library") || s.Contains("研究")) return SetKind.Library;
            if (s.Contains("競技") || s.Contains("arena") || s.Contains("tower") || s.Contains("排名")) return SetKind.Arena;
            if (s.Contains("社團") || s.Contains("club")) return SetKind.Club;
            if (s.Contains("屋頂") || s.Contains("roof") || s.Contains("天空港") || s.Contains("port")) return SetKind.Rooftop;
            return SetKind.Campus;
        }

        public void Build(SetKind kind)
        {
            if (kind == Current)
                return;
            Current = kind;
            for (int i = _set.childCount - 1; i >= 0; i--)
                Destroy(_set.GetChild(i).gameObject);

            bool indoor = kind == SetKind.Classroom || kind == SetKind.Dorm || kind == SetKind.Library || kind == SetKind.Club || kind == SetKind.Registry
                || kind == SetKind.Staffroom;
            SetNightSky(false);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = indoor ? new Color(0.78f, 0.80f, 0.86f) : new Color(0.70f, 0.82f, 0.96f);
            RenderSettings.fogStartDistance = indoor ? 14 : 30;
            RenderSettings.fogEndDistance = indoor ? 40 : 140;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.72f, 0.90f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.58f, 0.66f);
            RenderSettings.ambientGroundColor = new Color(0.30f, 0.30f, 0.34f);
            _sun.color = kind == SetKind.Dorm ? new Color(1f, 0.86f, 0.70f) : new Color(1f, 0.96f, 0.90f);
            _sun.intensity = indoor ? 0.95f : 1.15f;

            switch (kind)
            {
                case SetKind.SkyPort: BuildSkyPort(); break;
                case SetKind.Classroom:
                    if (!BuildHs2Classroom())
                        BuildClassroom();
                    break;
                case SetKind.Staffroom:
                    // Stand-in until the HS2 export exists: the classroom set is the closest room.
                    if (!BuildHs2Map("HS2/Staffroom", new Vector3(0.2f, 0.007f, 1.0f), 0.55f))
                        BuildClassroom();
                    break;
                case SetKind.Pool:
                    if (BuildHs2Map("HS2/NightPool", new Vector3(6f, 0f, -5f), 0.2f))
                        SetNightSky(true);
                    else
                        BuildCampus();
                    break;
                case SetKind.Dorm: BuildDorm(); break;
                case SetKind.Library: BuildLibrary(); break;
                case SetKind.Arena: BuildArena(); break;
                case SetKind.Club: BuildClub(); break;
                case SetKind.Rooftop: BuildRooftop(); break;
                case SetKind.Registry: BuildRegistry(); break;
                default: BuildCampus(); break;
            }
            // NPCs dress for the place: uniforms at school, gym clothes in the arena, swimwear at the pool.
            foreach (KeyValuePair<string, GameObject> kv in _actors)
            {
                Hs2Avatar avatar = kv.Value != null ? kv.Value.GetComponent<Hs2Avatar>() : null;
                if (avatar != null)
                    avatar.Apply(Hs2Looks.ForNpc(kv.Key, avatar.Female, kind));
            }
        }

        public void SetSunTint(Color c, float intensity)
        {
            _sun.color = c;
            _sun.intensity = intensity;
        }

        // ---- cast ----------------------------------------------------------------------

        private static GameObject _hs2Female;
        private static bool _hs2Checked;

        /// <summary>The exported HS2 female (Resources/HS2/Female01), or null when it was not exported.</summary>
        public static GameObject Hs2Female
        {
            get
            {
                if (!_hs2Checked)
                {
                    _hs2Female = Resources.Load<GameObject>("HS2/Female01");
                    _hs2Checked = true;
                }
                return _hs2Female;
            }
        }

        private static GameObject _hs2Male;
        private static bool _hs2MaleChecked;

        /// <summary>The exported HS2 male (Resources/HS2/Male01), or null when it was not exported.</summary>
        public static GameObject Hs2Male
        {
            get
            {
                if (!_hs2MaleChecked)
                {
                    _hs2Male = Resources.Load<GameObject>("HS2/Male01");
                    _hs2MaleChecked = true;
                }
                return _hs2Male;
            }
        }

        private static bool IsFemale(string gender) => gender == "女" || gender == "female" || gender == "F";
        private static bool IsMale(string gender) => gender == "男" || gender == "male" || gender == "M";

        /// <summary>An NPC figure: the HS2 character for women when available, else the placeholder figure.</summary>
        private GameObject CreateActor(string id, string name, string gender)
        {
            if (HasHs2Parts && (IsFemale(gender) || IsMale(gender)))
            {
                Hs2Avatar avatar = Hs2Avatar.Create(_cast, Hs2Looks.ForNpc(id, IsFemale(gender), Current), "NPC:" + id);
                if (avatar != null)
                {
                    Hs2Rig rig = avatar.GetComponent<Hs2Rig>();
                    int h = 0;
                    foreach (char c in id)
                        h = unchecked(h * 31 + c);
                    if (rig != null)
                        rig.PlayIdle(h & 0x7fffffff);
                    return avatar.gameObject;
                }
            }
            GameObject prefab = IsFemale(gender) ? Hs2Female : IsMale(gender) ? Hs2Male : null;
            if (prefab != null)
            {
                GameObject go = Instantiate(prefab, _cast, false);
                go.name = "NPC:" + id;
                Personalize(go, id, gender);
                return go;
            }
            Mannequin m = Mannequin.Create(_cast, "NPC:" + id, AvatarData.ForNpc(id, gender));
            m.DisplayName = name;
            return m.gameObject;
        }

        /// <summary>Gives each HS2 NPC her own hair and eye colours and idle loop, derived from the id.</summary>
        private static void Personalize(GameObject go, string id, string gender)
        {
            AvatarData look = AvatarData.ForNpc(id, gender);
            int seed = 0;
            foreach (char c in id)
                seed = unchecked(seed * 31 + c);
            // Natural school-life hair colours: black, dark brown, chestnut, auburn, ash brown, honey.
            Color[] palette =
            {
                new Color(0.10f, 0.09f, 0.09f), new Color(0.20f, 0.13f, 0.10f), new Color(0.33f, 0.20f, 0.13f),
                new Color(0.38f, 0.16f, 0.12f), new Color(0.36f, 0.31f, 0.28f), new Color(0.62f, 0.45f, 0.27f)
            };
            Color hair = palette[Mathf.Abs(seed) % palette.Length];
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material m in r.materials)
                {
                    if (m.shader.name == "EDEN/HS2 Hair")
                    {
                        m.SetColor("_Color", hair);
                        m.SetColor("_TopColor", hair * 0.8f);
                        m.SetColor("_UnderColor", Color.Lerp(hair, Color.white, 0.12f));
                    }
                    else if (m.shader.name == "EDEN/HS2 Eye")
                    {
                        m.SetColor("_IrisColor", look.EyeColor * 0.8f);
                        m.SetColor("_IrisLower", Color.Lerp(look.EyeColor, Color.white, 0.25f));
                    }
                }
            }
            Hs2Rig rig = go.GetComponent<Hs2Rig>();
            if (rig != null)
            {
                int h = 0;
                foreach (char c in id)
                    h = unchecked(h * 31 + c);
                rig.PlayIdle(Mathf.Abs(h));
            }
        }

        /// <summary>Places the given NPCs; the speaker stands centre stage, the rest to the sides.</summary>
        public void SetCast(IList<(string id, string name, string gender)> people, string speakerId)
        {
            var keep = new HashSet<string>();
            int sideIndex = 0;
            foreach ((string id, string name, string gender) in people)
            {
                if (string.IsNullOrEmpty(id) || keep.Count >= 3)
                    continue;
                keep.Add(id);
                if (!_actors.TryGetValue(id, out GameObject actor) || actor == null)
                {
                    actor = CreateActor(id, name, gender);
                    _actors[id] = actor;
                }
                bool speaker = id == speakerId || (string.IsNullOrEmpty(speakerId) && sideIndex == 0 && keep.Count == 1);
                Vector3 pos;
                if (speaker)
                {
                    pos = new Vector3(0, 0, 0.2f);
                }
                else
                {
                    float side = sideIndex % 2 == 0 ? -1.25f : 1.25f;
                    pos = new Vector3(side, 0, 1.1f + sideIndex * 0.2f);
                    sideIndex++;
                }
                actor.transform.localPosition = pos;
                actor.transform.localRotation = Quaternion.Euler(0, speaker ? 0 : (pos.x < 0 ? 18 : -18), 0);
                actor.SetActive(true);
            }
            foreach (KeyValuePair<string, GameObject> kv in _actors)
            {
                if (!keep.Contains(kv.Key) && kv.Value != null)
                    kv.Value.SetActive(false);
            }
        }

        public Transform ActorTransform(string id) => id != null && _actors.TryGetValue(id, out GameObject a) && a != null ? a.transform : null;

        /// <summary>Eye height above the feet, for framing (HS2 figures are about 1.6 m tall).</summary>
        public float EyeHeightOf(string id)
        {
            if (id == null || !_actors.TryGetValue(id, out GameObject a) || a == null)
                return 1.5f;
            return EyeHeightOf(a);
        }

        public float EyeHeightOf(GameObject figure)
        {
            if (figure == Player)
                return PlayerEyeHeight;
            Mannequin m = figure.GetComponent<Mannequin>();
            if (m != null)
                return m.EyeHeight;
            Hs2Avatar avatar = figure.GetComponent<Hs2Avatar>();
            return (avatar != null && !avatar.Female ? 1.58f : 1.48f) * figure.transform.localScale.y;
        }

        public IEnumerable<Transform> ActiveActors()
        {
            foreach (GameObject a in _actors.Values)
            {
                if (a != null && a.activeInHierarchy)
                    yield return a.transform;
            }
        }

        private static void SetTalk(GameObject a, bool talking)
        {
            if (a == null)
                return;
            Mannequin m = a.GetComponent<Mannequin>();
            if (m != null)
                m.Talking = talking;
            Hs2Rig r = a.GetComponent<Hs2Rig>();
            if (r != null)
                r.Talking = talking;
        }

        public void SetTalking(string id)
        {
            foreach (KeyValuePair<string, GameObject> kv in _actors)
                SetTalk(kv.Value, kv.Key == id);
        }

        /// <summary>The speaker accompanies a new line with one of HS2's conversation motions.</summary>
        public void Gesture(string id)
        {
            if (id != null && _actors.TryGetValue(id, out GameObject a) && a != null && a.TryGetComponent(out Hs2Rig rig))
                rig.PlayGestureFor(null);
        }

        /// <summary>Puts an emotion ("joy", "anger"… or "neutral") on one actor's face and relaxes the others.</summary>
        public void SetExpression(string id, string emotion)
        {
            foreach (KeyValuePair<string, GameObject> kv in _actors)
            {
                Hs2Rig rig = kv.Value != null ? kv.Value.GetComponent<Hs2Rig>() : null;
                if (rig == null)
                    continue;
                rig.SetExpression(kv.Key == id ? emotion : "neutral");
                if (kv.Key == id && !string.IsNullOrEmpty(emotion) && emotion != "neutral")
                    rig.PlayGestureFor(emotion);
            }
        }

        public void StopTalking()
        {
            foreach (GameObject a in _actors.Values)
                SetTalk(a, false);
        }

        public void ClearCast()
        {
            foreach (GameObject a in _actors.Values)
            {
                if (a != null)
                    Destroy(a);
            }
            _actors.Clear();
        }

        /// <summary>The player's own figure, shown in 外觀自訂 and 拍照.</summary>
        public GameObject ShowPlayer(AvatarData data, Vector3 pos)
        {
            data.Clamp();
            if (HasHs2Parts)
            {
                if (_hs2Player != null && _hs2Player.Female == data.male)
                {
                    Destroy(_hs2Player.gameObject);
                    _hs2Player = null;
                }
                if (_hs2Player == null)
                    _hs2Player = Hs2Avatar.Create(_cast, data.look, "Player");
                else
                    _hs2Player.Apply(data.look);
                if (_mannequinPlayer != null)
                    _mannequinPlayer.gameObject.SetActive(false);
                Player = _hs2Player.gameObject;
                // Height is the one body slider HS2 parts allow without re-rigging: a uniform scale with an adult floor.
                Player.transform.localScale = Vector3.one * data.height;
                PlayerEyeHeight = (data.male ? 1.58f : 1.48f) * data.height;
            }
            else
            {
                if (_mannequinPlayer == null)
                    _mannequinPlayer = Mannequin.Create(_cast, "Player", data);
                else
                    _mannequinPlayer.Apply(data);
                Player = _mannequinPlayer.gameObject;
                PlayerEyeHeight = _mannequinPlayer.EyeHeight;
            }
            Player.transform.localPosition = pos;
            Player.transform.localRotation = Quaternion.identity;
            Player.SetActive(true);
            return Player;
        }

        public void HidePlayer()
        {
            if (Player != null)
                Player.SetActive(false);
        }

        public void SetCastVisible(bool visible) => _cast.gameObject.SetActive(visible);

        /// <summary>Hides the placeholder set while an HS2 map provides the scenery.</summary>
        public void SetSetVisible(bool visible) => _set.gameObject.SetActive(visible);

        // ---- sets ----------------------------------------------------------------------

        private GameObject Box(string name, Vector3 pos, Vector3 size, Color color, float rotY = 0, PrimitiveType type = PrimitiveType.Cube)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_set, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.transform.localRotation = Quaternion.Euler(0, rotY, 0);
            Mats.Paint(go, color, 0.15f);
            return go;
        }

        private void Island(float radius)
        {
            Box("Island", new Vector3(0, -0.6f, 6), new Vector3(radius, 0.6f, radius), new Color(0.80f, 0.82f, 0.84f), 0, PrimitiveType.Cylinder);
            Box("IslandRock", new Vector3(0, -3.2f, 6), new Vector3(radius * 0.8f, 2.4f, radius * 0.8f), new Color(0.46f, 0.47f, 0.52f), 0, PrimitiveType.Cylinder);
            Box("Plaza", new Vector3(0, 0.01f, 3), new Vector3(9, 0.01f, 9), new Color(0.88f, 0.86f, 0.80f), 0, PrimitiveType.Cylinder);
            Box("Lawn", new Vector3(-9, 0.005f, 8), new Vector3(10, 0.01f, 10), new Color(0.48f, 0.68f, 0.45f), 0, PrimitiveType.Cylinder);
            Box("Lawn2", new Vector3(10, 0.005f, 9), new Vector3(8, 0.01f, 8), new Color(0.50f, 0.70f, 0.47f), 0, PrimitiveType.Cylinder);
        }

        private void Clouds(int count, float radius, float y)
        {
            var rng = new System.Random(7);
            for (int i = 0; i < count; i++)
            {
                float a = (float)(rng.NextDouble() * Mathf.PI * 2);
                float r = radius * (0.6f + (float)rng.NextDouble() * 0.6f);
                var pos = new Vector3(Mathf.Cos(a) * r, y + (float)rng.NextDouble() * 6f, Mathf.Sin(a) * r + 20);
                float s = 4 + (float)rng.NextDouble() * 6;
                Box("Cloud", pos, new Vector3(s * 1.8f, s * 0.6f, s), new Color(1f, 1f, 1f), 0, PrimitiveType.Sphere);
                Box("Cloud", pos + new Vector3(s * 0.7f, s * 0.15f, 0), new Vector3(s, s * 0.5f, s * 0.8f), new Color(0.97f, 0.98f, 1f), 0, PrimitiveType.Sphere);
            }
        }

        private void Tree(Vector3 pos, float scale)
        {
            Box("Trunk", pos + new Vector3(0, 0.8f * scale, 0), new Vector3(0.25f, 0.8f, 0.25f) * scale, new Color(0.42f, 0.30f, 0.22f), 0, PrimitiveType.Cylinder);
            Box("Leaves", pos + new Vector3(0, 2.1f * scale, 0), new Vector3(1.8f, 1.6f, 1.8f) * scale, new Color(0.36f, 0.60f, 0.38f), 0, PrimitiveType.Sphere);
        }

        private void Building(Vector3 pos, Vector3 size, Color wall, float rot = 0)
        {
            Box("Building", pos + new Vector3(0, size.y / 2, 0), size, wall, rot);
            Box("Roof", pos + new Vector3(0, size.y + 0.15f, 0), new Vector3(size.x + 0.4f, 0.3f, size.z + 0.4f), new Color(0.33f, 0.42f, 0.58f), rot);
            int rows = Mathf.Max(1, Mathf.FloorToInt(size.y / 2.4f));
            for (int i = 0; i < rows; i++)
            {
                Box("Windows", pos + new Vector3(0, 1.4f + i * 2.4f, 0), new Vector3(size.x + 0.02f, 0.9f, size.z + 0.02f), new Color(0.55f, 0.72f, 0.88f), rot);
            }
        }

        private void BuildCampus()
        {
            Island(34);
            Clouds(10, 70, -12);
            Building(new Vector3(0, 0, 18), new Vector3(16, 9, 5), new Color(0.93f, 0.92f, 0.88f));          // 教學樓
            Building(new Vector3(-14, 0, 14), new Vector3(6, 6, 6), new Color(0.90f, 0.86f, 0.80f), 20);     // 社團館
            Box("ArenaTower", new Vector3(14, 8, 22), new Vector3(4, 8, 4), new Color(0.80f, 0.84f, 0.92f), 0, PrimitiveType.Cylinder);
            Box("ArenaRing", new Vector3(14, 12, 22), new Vector3(7, 0.3f, 7), new Color(0.98f, 0.81f, 0.47f), 0, PrimitiveType.Cylinder);
            Box("Clock", new Vector3(0, 10.2f, 15.4f), new Vector3(1.8f, 1.8f, 0.2f), new Color(0.98f, 0.95f, 0.85f), 0, PrimitiveType.Cylinder).transform.localRotation = Quaternion.Euler(90, 0, 0);
            Box("Fountain", new Vector3(0, 0.25f, 5), new Vector3(2.6f, 0.25f, 2.6f), new Color(0.78f, 0.80f, 0.84f), 0, PrimitiveType.Cylinder);
            Box("Water", new Vector3(0, 0.42f, 5), new Vector3(2.2f, 0.05f, 2.2f), new Color(0.45f, 0.70f, 0.95f), 0, PrimitiveType.Cylinder);
            Tree(new Vector3(-5, 0, 6), 1.1f);
            Tree(new Vector3(5.5f, 0, 7), 1f);
            Tree(new Vector3(-9, 0, 10), 1.3f);
            Tree(new Vector3(9, 0, 12), 1.2f);
            Box("Bench", new Vector3(-3, 0.25f, 3.5f), new Vector3(1.6f, 0.1f, 0.5f), new Color(0.55f, 0.40f, 0.30f), 15);
            Box("Bench", new Vector3(3.2f, 0.25f, 3.8f), new Vector3(1.6f, 0.1f, 0.5f), new Color(0.55f, 0.40f, 0.30f), -15);
        }

        private void BuildSkyPort()
        {
            Island(26);
            Clouds(16, 60, -10);
            Box("Pier", new Vector3(0, 0.05f, 2), new Vector3(3, 0.1f, 14), new Color(0.80f, 0.76f, 0.70f));
            Box("Rail", new Vector3(-1.5f, 0.5f, 2), new Vector3(0.06f, 0.06f, 14), new Color(0.98f, 0.81f, 0.47f));
            Box("Rail", new Vector3(1.5f, 0.5f, 2), new Vector3(0.06f, 0.06f, 14), new Color(0.98f, 0.81f, 0.47f));
            Box("Spire", new Vector3(-8, 9, 24), new Vector3(3, 9, 3), new Color(0.86f, 0.88f, 0.94f), 0, PrimitiveType.Cylinder);
            Box("Spire", new Vector3(10, 6, 28), new Vector3(2.5f, 6, 2.5f), new Color(0.86f, 0.88f, 0.94f), 0, PrimitiveType.Cylinder);
            Box("Ring", new Vector3(-8, 14, 24), new Vector3(6, 0.2f, 6), new Color(0.42f, 0.74f, 1f), 0, PrimitiveType.Cylinder);
            Building(new Vector3(0, 0, 20), new Vector3(12, 7, 5), new Color(0.94f, 0.93f, 0.90f));
            Tree(new Vector3(-5, 0, 9), 1.1f);
            Tree(new Vector3(5, 0, 10), 1f);
        }

        private void Interior(Color wall, Color floor, float width = 12, float depth = 10)
        {
            Box("Floor", new Vector3(0, -0.05f, depth / 2 - 2), new Vector3(width, 0.1f, depth), floor);
            Box("BackWall", new Vector3(0, 1.6f, depth - 2), new Vector3(width, 3.4f, 0.2f), wall);
            Box("WallL", new Vector3(-width / 2, 1.6f, depth / 2 - 2), new Vector3(0.2f, 3.4f, depth), wall * 0.95f);
            Box("WallR", new Vector3(width / 2, 1.6f, depth / 2 - 2), new Vector3(0.2f, 3.4f, depth), wall * 0.95f);
            Box("Ceiling", new Vector3(0, 3.35f, depth / 2 - 2), new Vector3(width, 0.1f, depth), new Color(0.95f, 0.95f, 0.96f));
            for (int i = 0; i < 3; i++)
                Box("WindowLight", new Vector3(width / 2 - 0.08f, 1.9f, i * 2.6f), new Vector3(0.05f, 1.4f, 1.8f), new Color(0.80f, 0.90f, 1f));
        }

        /// <summary>
        /// The exported HS2 classroom (Resources/HS2/Classroom). It is turned 180 degrees so the front
        /// blackboard is behind the cast, and offset so the spot beside the teacher's desk sits
        /// at the origin where the speaker stands.
        /// </summary>
        private bool BuildHs2Classroom()
        {
            GameObject prefab = Resources.Load<GameObject>("HS2/Classroom");
            if (prefab == null)
                return false;
            GameObject room = Instantiate(prefab, _set, false);
            Quaternion turn = Quaternion.Euler(0, 180, 0);
            room.transform.localRotation = turn;
            room.transform.localPosition = -(turn * new Vector3(1.03f, 0f, -4.0f));
            // The room carries both its day and evening light rigs; together they over-expose.
            foreach (Light l in room.GetComponentsInChildren<Light>(true))
                l.intensity *= 0.55f;
            RenderSettings.fog = false;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.58f, 0.64f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.44f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.26f, 0.25f, 0.24f);
            _sun.intensity = 0.25f;
            return true;
        }

        /// <summary>
        /// An exported HS2 map placed unrotated, offset so <paramref name="spot"/> (in map coordinates, picked with
        /// Hs2Preview.*Candidates) sits at the origin where the speaker stands. HS2 maps stack many coloured point
        /// lights that HS2 tones down in post; <paramref name="lightScale"/> compensates.
        /// </summary>
        private bool BuildHs2Map(string resource, Vector3 spot, float lightScale)
        {
            GameObject prefab = Resources.Load<GameObject>(resource);
            if (prefab == null)
                return false;
            GameObject room = Instantiate(prefab, _set, false);
            room.transform.localPosition = -spot;
            foreach (Light l in room.GetComponentsInChildren<Light>(true))
                l.intensity *= lightScale;
            RenderSettings.fog = false;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.58f, 0.64f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.44f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.26f, 0.25f, 0.24f);
            _sun.intensity = 0.25f;
            return true;
        }

        private static readonly Color NightSky = new Color(0.10f, 0.12f, 0.20f);

        /// <summary>Night maps show a dark sky; every other set uses the default daytime skybox.</summary>
        private static void SetNightSky(bool night)
        {
            Camera cam = Camera.main;
            if (cam == null)
                return;
            cam.clearFlags = night ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
            cam.backgroundColor = NightSky;
            if (night)
            {
                RenderSettings.ambientSkyColor = new Color(0.30f, 0.34f, 0.48f);
                RenderSettings.ambientEquatorColor = new Color(0.26f, 0.28f, 0.36f);
                RenderSettings.ambientGroundColor = new Color(0.14f, 0.14f, 0.18f);
            }
        }

        private void BuildClassroom()
        {
            Interior(new Color(0.94f, 0.92f, 0.86f), new Color(0.62f, 0.50f, 0.38f));
            Box("Blackboard", new Vector3(0, 1.7f, 7.85f), new Vector3(5.5f, 1.4f, 0.05f), new Color(0.18f, 0.32f, 0.26f));
            for (int r = 0; r < 2; r++)
            {
                for (int c = -2; c <= 2; c++)
                {
                    if (c == 0)
                        continue;
                    Box("Desk", new Vector3(c * 1.6f, 0.72f, 2.5f + r * 1.8f), new Vector3(1.0f, 0.06f, 0.6f), new Color(0.75f, 0.62f, 0.45f));
                    Box("DeskLeg", new Vector3(c * 1.6f, 0.36f, 2.5f + r * 1.8f), new Vector3(0.9f, 0.7f, 0.05f), new Color(0.35f, 0.36f, 0.40f));
                }
            }
        }

        private void BuildDorm()
        {
            Interior(new Color(0.95f, 0.88f, 0.80f), new Color(0.70f, 0.58f, 0.46f), 9, 8);
            Box("Bed", new Vector3(-2.8f, 0.3f, 3.5f), new Vector3(1.6f, 0.5f, 3.0f), new Color(0.85f, 0.88f, 0.95f));
            Box("Pillow", new Vector3(-2.8f, 0.62f, 4.6f), new Vector3(1.2f, 0.18f, 0.5f), Color.white);
            Box("Desk", new Vector3(2.6f, 0.75f, 4.5f), new Vector3(1.6f, 0.08f, 0.8f), new Color(0.72f, 0.58f, 0.42f));
            Box("Lamp", new Vector3(3.1f, 1.0f, 4.6f), new Vector3(0.2f, 0.3f, 0.2f), new Color(1f, 0.9f, 0.6f), 0, PrimitiveType.Cylinder);
            Box("Rug", new Vector3(0, 0.01f, 2.5f), new Vector3(3, 0.01f, 2), new Color(0.62f, 0.45f, 0.55f), 0, PrimitiveType.Cylinder);
        }

        private void BuildLibrary()
        {
            Interior(new Color(0.86f, 0.82f, 0.74f), new Color(0.48f, 0.36f, 0.28f), 14, 12);
            for (int i = -2; i <= 2; i++)
            {
                Box("Shelf", new Vector3(i * 2.6f, 1.3f, 8.5f), new Vector3(2.2f, 2.6f, 0.5f), new Color(0.42f, 0.30f, 0.22f));
                for (int j = 0; j < 4; j++)
                    Box("Books", new Vector3(i * 2.6f, 0.45f + j * 0.6f, 8.2f), new Vector3(2.0f, 0.42f, 0.12f), Color.HSVToRGB((i * 0.13f + j * 0.21f + 1f) % 1f, 0.45f, 0.65f));
            }
            Box("ReadingTable", new Vector3(0, 0.76f, 4), new Vector3(3.2f, 0.08f, 1.2f), new Color(0.62f, 0.48f, 0.34f));
        }

        private void BuildArena()
        {
            Box("ArenaFloor", new Vector3(0, -0.1f, 5), new Vector3(18, 0.2f, 18), new Color(0.82f, 0.84f, 0.88f), 0, PrimitiveType.Cylinder);
            Box("Ring", new Vector3(0, 0.02f, 5), new Vector3(9, 0.02f, 9), new Color(0.42f, 0.74f, 1f), 0, PrimitiveType.Cylinder);
            Box("Inner", new Vector3(0, 0.03f, 5), new Vector3(8.6f, 0.02f, 8.6f), new Color(0.90f, 0.91f, 0.94f), 0, PrimitiveType.Cylinder);
            for (int i = 0; i < 12; i++)
            {
                float a = i / 12f * Mathf.PI * 2;
                Box("Stand", new Vector3(Mathf.Cos(a) * 11, 1.2f, 5 + Mathf.Sin(a) * 11), new Vector3(5.2f, 2.4f, 1.4f), new Color(0.55f, 0.60f, 0.72f), -a * Mathf.Rad2Deg + 90);
                Box("Pillar", new Vector3(Mathf.Cos(a) * 13, 5, 5 + Mathf.Sin(a) * 13), new Vector3(0.6f, 5, 0.6f), new Color(0.92f, 0.92f, 0.96f), 0, PrimitiveType.Cylinder);
            }
            Clouds(8, 60, -14);
        }

        private void BuildClub()
        {
            Interior(new Color(0.88f, 0.90f, 0.94f), new Color(0.56f, 0.52f, 0.48f), 14, 11);
            Box("Board", new Vector3(-3, 1.8f, 8.85f), new Vector3(4, 1.6f, 0.05f), new Color(0.72f, 0.56f, 0.40f));
            for (int i = 0; i < 6; i++)
                Box("Flyer", new Vector3(-4.4f + i * 0.55f, 1.8f + (i % 2) * 0.3f, 8.8f), new Vector3(0.45f, 0.6f, 0.02f), Color.HSVToRGB(i / 6f, 0.35f, 0.95f));
            Box("Table", new Vector3(2.5f, 0.75f, 4), new Vector3(2.4f, 0.08f, 1.2f), new Color(0.66f, 0.52f, 0.38f));
        }

        private void BuildRooftop()
        {
            Box("RoofFloor", new Vector3(0, -0.05f, 5), new Vector3(16, 0.1f, 14), new Color(0.72f, 0.74f, 0.78f));
            Box("Fence", new Vector3(0, 0.6f, 12), new Vector3(16, 1.2f, 0.05f), new Color(0.60f, 0.70f, 0.80f));
            Box("Fence", new Vector3(-8, 0.6f, 5), new Vector3(0.05f, 1.2f, 14), new Color(0.60f, 0.70f, 0.80f));
            Box("Fence", new Vector3(8, 0.6f, 5), new Vector3(0.05f, 1.2f, 14), new Color(0.60f, 0.70f, 0.80f));
            Box("WaterTank", new Vector3(5, 1.2f, 9), new Vector3(1.8f, 1.2f, 1.8f), new Color(0.88f, 0.88f, 0.90f), 0, PrimitiveType.Cylinder);
            Clouds(14, 60, -6);
        }

        private void BuildRegistry()
        {
            Interior(new Color(0.92f, 0.94f, 0.97f), new Color(0.76f, 0.78f, 0.82f), 14, 11);
            Box("Counter", new Vector3(0, 0.55f, 4.5f), new Vector3(5, 1.1f, 0.8f), new Color(0.32f, 0.44f, 0.64f));
            Box("CounterTop", new Vector3(0, 1.12f, 4.5f), new Vector3(5.2f, 0.06f, 1f), new Color(0.95f, 0.95f, 0.98f));
            Box("Emblem", new Vector3(0, 2.3f, 8.85f), new Vector3(1.6f, 1.6f, 0.05f), new Color(0.98f, 0.81f, 0.47f), 0, PrimitiveType.Cylinder).transform.localRotation = Quaternion.Euler(90, 0, 0);
        }
    }
}
