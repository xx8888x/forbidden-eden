using System.Collections.Generic;
using UnityEngine;

namespace ForbiddenEden.World
{
    /// <summary>
    /// Runtime behaviour for exported HS2 characters: blinking, a talking mouth, facial expressions and idle
    /// variation through the face blend shapes and the exported ADV idle clips, plus a head turn toward the camera.
    /// </summary>
    public sealed class Hs2Rig : MonoBehaviour
    {
        public bool Talking;
        public string BlinkShape = "e01_close";
        public string[] MouthShapes = { "k18_a", "k19_i", "k20_u", "k21_e", "k22_o" };

        private readonly List<(SkinnedMeshRenderer smr, int index)> _blink = new List<(SkinnedMeshRenderer, int)>();
        private readonly List<(SkinnedMeshRenderer smr, int index)> _mouth = new List<(SkinnedMeshRenderer, int)>();
        private Animator _animator;
        private readonly Dictionary<string, List<(SkinnedMeshRenderer smr, int index)>> _shapes = new Dictionary<string, List<(SkinnedMeshRenderer, int)>>();
        private readonly Dictionary<string, float> _exprTarget = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _exprNow = new Dictionary<string, float>();
        private Transform _head;
        private Transform _neck;
        private float _lookWeight;

        /// <summary>Turns the head toward the main camera (within limits) while true.</summary>
        public bool LookAtCamera = true;

        /// <summary>
        /// HS2 face shapes per server emotion (Plutchik's eight + neutral): e = eyes, g = brows, k = mouth.
        /// Only general-rated shapes are used.
        /// </summary>
        private static readonly Dictionary<string, (string shape, float weight)[]> Expressions = new Dictionary<string, (string, float)[]>
        {
            ["joy"] = new[] { ("e04_warai", 70f), ("g01_warai1", 80f), ("k01_warai1", 80f) },
            ["trust"] = new[] { ("e04_warai", 30f), ("g01_warai1", 40f), ("k01_warai1", 45f) },
            ["anticipation"] = new[] { ("e08_odoroki", 20f), ("g08_up", 50f), ("k01_warai1", 40f) },
            ["surprise"] = new[] { ("e08_odoroki", 90f), ("g08_up", 90f), ("k09_open1", 60f) },
            ["fear"] = new[] { ("e08_odoroki", 50f), ("g06_kanasimi2", 80f), ("k03_kanasimi", 50f) },
            ["sadness"] = new[] { ("e05_damage", 40f), ("g05_kanasimi1", 90f), ("k03_kanasimi", 80f) },
            ["anger"] = new[] { ("e13_ikari", 80f), ("g03_ikari1", 90f), ("k04_ikari", 70f) },
            ["disgust"] = new[] { ("e13_ikari", 40f), ("g04_ikari2", 60f), ("k08_leftup", 60f) },
            ["shy"] = new[] { ("e05_damage", 25f), ("g05_kanasimi1", 30f), ("k05_fukure", 40f) },
        };

        /// <summary>Blends to the face for an emotion tag ("joy", "anger"…); unknown tags and "neutral" relax the face.</summary>
        public void SetExpression(string emotion)
        {
            _exprTarget.Clear();
            if (emotion != null && Expressions.TryGetValue(emotion.ToLowerInvariant(), out (string shape, float weight)[] set))
            {
                foreach ((string shape, float weight) in set)
                    _exprTarget[shape] = weight;
            }
            foreach (string key in new List<string>(_exprNow.Keys))
            {
                if (!_exprTarget.ContainsKey(key))
                    _exprTarget[key] = 0f;
            }
        }
        private float _nextBlink;
        private float _blinkT = -1;
        private float _talk;
        private float _mouthSeed;

        /// <summary>Idle states in the exported controller; the exporter fills this per character.</summary>
        public string[] Idles = { "adv_Idle_00_00", "adv_Idle_01_00", "adv_Idle_02_00", "adv_Idle_03_00" };

        private void Awake()
        {
            _animator = GetComponentInChildren<Animator>();
            foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh m = smr.sharedMesh;
                if (m == null)
                    continue;
                for (int i = 0; i < m.blendShapeCount; i++)
                {
                    string n = m.GetBlendShapeName(i);
                    if (n.EndsWith(BlinkShape))
                        _blink.Add((smr, i));
                    // "head.g01_warai1" → "g01_warai1"; eyes and brows also live on the lashes mesh.
                    string key = n.Substring(n.IndexOf('.') + 1);
                    if (!_shapes.TryGetValue(key, out var list))
                        _shapes[key] = list = new List<(SkinnedMeshRenderer, int)>();
                    list.Add((smr, i));
                    if (smr.name == "o_head")
                    {
                        foreach (string s in MouthShapes)
                        {
                            if (n.EndsWith(s))
                                _mouth.Add((smr, i));
                        }
                    }
                }
            }
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "cf_J_Head")
                    _head = t;
                else if (t.name == "cf_J_Neck")
                    _neck = t;
            }
            _nextBlink = Time.time + Random.Range(1.5f, 4f);
            _mouthSeed = Random.value * 10f;
            PlayIdle(Random.Range(0, Mathf.Max(1, Idles.Length)));
        }

        /// <summary>HS2 ADV conversation motions in the controller (exporter: ExportAdvGestures).</summary>
        public string[] Gestures = new string[0];

        /// <summary>Seated loops (HS2 ADV chair poses) for scenes with chairs.</summary>
        public string[] Seated = new string[0];

        /// <summary>Body motion per server emotion; names are HS2 ADV clips.</summary>
        private static readonly Dictionary<string, string> EmotionMotion = new Dictionary<string, string>
        {
            ["joy"] = "Joy", ["trust"] = "Joy", ["anger"] = "Anger", ["disgust"] = "Anger",
            ["sadness"] = "Sorrow", ["fear"] = "Shame", ["shy"] = "Shame",
        };

        /// <summary>The emotion's body motion when there is one, otherwise a talking gesture.</summary>
        public void PlayGestureFor(string emotion)
        {
            if (emotion != null && EmotionMotion.TryGetValue(emotion.ToLowerInvariant(), out string clip))
            {
                int i = System.Array.IndexOf(Gestures, clip);
                if (i >= 0)
                {
                    PlayGesture(i);
                    return;
                }
            }
            int talk = System.Array.FindIndex(Gestures, g => g.StartsWith("action_"));
            if (talk >= 0)
                PlayGesture(talk + Random.Range(0, 2));
        }

        private string _idle;
        private float _gestureEnd = -1f;

        /// <summary>Picks one of the exported idle loops.</summary>
        public void PlayIdle(int index)
        {
            if (_animator != null && _animator.runtimeAnimatorController != null && Idles != null && Idles.Length > 0)
            {
                _idle = Idles[Mathf.Abs(index) % Idles.Length];
                _animator.CrossFadeInFixedTime(_idle, 0.4f, 0, Random.value);
            }
        }

        /// <summary>Plays one conversation motion (by index into <see cref="Gestures"/>), then eases back to the idle loop.</summary>
        public void PlayGesture(int index)
        {
            if (_animator == null || _animator.runtimeAnimatorController == null || Gestures == null || Gestures.Length == 0)
                return;
            string name = Gestures[Mathf.Abs(index) % Gestures.Length];
            float length = 2f;
            foreach (AnimationClip clip in _animator.runtimeAnimatorController.animationClips)
            {
                if (clip.name == name)
                {
                    length = clip.length;
                    break;
                }
            }
            _animator.CrossFadeInFixedTime(name, 0.35f, 0, 0f);
            // Looping motions are held for a few seconds; one-shots return when they end.
            _gestureEnd = Time.time + Mathf.Clamp(length, 1.5f, 6f) - 0.35f;
        }

        private void LateUpdate()
        {
            if (_gestureEnd > 0 && Time.time >= _gestureEnd)
            {
                _gestureEnd = -1f;
                if (_idle != null && _animator != null)
                    _animator.CrossFadeInFixedTime(_idle, 0.5f, 0, Random.value);
            }

            // Blink: 0 → 100 → 0 over ~0.18 s.
            if (_blinkT < 0 && Time.time >= _nextBlink)
            {
                _blinkT = 0;
                _nextBlink = Time.time + Random.Range(2.2f, 5.5f);
            }
            float blink = 0;
            if (_blinkT >= 0)
            {
                _blinkT += Time.deltaTime / 0.18f;
                blink = Mathf.Sin(Mathf.Clamp01(_blinkT) * Mathf.PI) * 100f;
                if (_blinkT >= 1)
                    _blinkT = -1;
            }
            foreach ((SkinnedMeshRenderer smr, int index) in _blink)
                smr.SetBlendShapeWeight(index, blink);

            // Expression: ease every shape toward its target; while talking the mouth part is softened.
            foreach (KeyValuePair<string, float> kv in _exprTarget)
            {
                float now = _exprNow.TryGetValue(kv.Key, out float v) ? v : 0f;
                now = Mathf.MoveTowards(now, kv.Value, Time.deltaTime * 250f);
                _exprNow[kv.Key] = now;
                if (!_shapes.TryGetValue(kv.Key, out var targets))
                    continue;
                float w = kv.Key.StartsWith("k") ? now * (1f - 0.6f * _talk) : now;
                foreach ((SkinnedMeshRenderer smr, int index) in targets)
                    smr.SetBlendShapeWeight(index, w);
            }

            // Talking: cycle vowel shapes while text streams.
            _talk = Mathf.MoveTowards(_talk, Talking ? 1 : 0, Time.deltaTime * 4f);
            if (_mouth.Count > 0)
            {
                float t = Time.time * 9f + _mouthSeed;
                int active = Mathf.FloorToInt(t) % _mouth.Count;
                float open = (0.5f + 0.5f * Mathf.Sin(t * 2.3f)) * 70f * _talk;
                for (int i = 0; i < _mouth.Count; i++)
                    _mouth[i].smr.SetBlendShapeWeight(_mouth[i].index, i == active ? open : 0);
            }

            LookAt();
        }

        /// <summary>Turns neck and head (after the animator) toward the camera: at most 45° sideways, 20° up/down.</summary>
        private void LookAt()
        {
            Camera cam = Camera.main;
            _lookWeight = Mathf.MoveTowards(_lookWeight, LookAtCamera && cam != null && _head != null ? 1f : 0f, Time.deltaTime * 2f);
            if (_lookWeight <= 0f || cam == null || _head == null)
                return;
            // Figures face -Z of their root.
            Vector3 forward = -transform.forward;
            Vector3 to = cam.transform.position - _head.position;
            Vector3 local = Quaternion.Inverse(Quaternion.LookRotation(forward, transform.up)) * to;
            float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
            if (Mathf.Abs(yaw) > 100f)
                return; // camera behind the figure: keep the animated pose
            yaw = Mathf.Clamp(yaw, -45f, 45f) * _lookWeight;
            pitch = Mathf.Clamp(pitch, -20f, 20f) * _lookWeight;
            Vector3 right = Vector3.Cross(transform.up, forward).normalized;
            Quaternion turn = Quaternion.AngleAxis(yaw, transform.up) * Quaternion.AngleAxis(pitch, right);
            if (_neck != null)
            {
                _neck.rotation = Quaternion.Slerp(Quaternion.identity, turn, 0.4f) * _neck.rotation;
                _head.rotation = Quaternion.Slerp(Quaternion.identity, turn, 0.6f) * _head.rotation;
            }
            else
            {
                _head.rotation = turn * _head.rotation;
            }
        }
    }
}
