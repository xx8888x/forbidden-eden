using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spring-bone chain for hair and skirts. The serialized fields match HS2's component, so exported HS2 parts keep
/// their tuned values (damping, elasticity, stiffness, inertia, gravity…); the simulation is this project's own
/// verlet integration: each bone is a particle that keeps its rest offset from its parent, is pulled back by
/// elasticity, limited by stiffness, pushed out of colliders, and finally kept at its bone length.
/// </summary>
public class DynamicBone : MonoBehaviour
{
    public enum UpdateMode { Normal, AnimatePhysics, UnscaledTime }

    public enum FreezeAxis { None, X, Y, Z }

    public string Comment = "";
    public Transform m_Root;
    public float m_UpdateRate = 60f;
    public UpdateMode m_UpdateMode;
    [Range(0, 1)] public float m_Damping = 0.1f;
    public AnimationCurve m_DampingDistrib;
    [Range(0, 1)] public float m_Elasticity = 0.1f;
    public AnimationCurve m_ElasticityDistrib;
    [Range(0, 1)] public float m_Stiffness = 0.1f;
    public AnimationCurve m_StiffnessDistrib;
    [Range(0, 1)] public float m_Inert;
    public AnimationCurve m_InertDistrib;
    public float m_Radius;
    public AnimationCurve m_RadiusDistrib;
    public float m_EndLength;
    public Vector3 m_EndOffset = Vector3.zero;
    public Vector3 m_Gravity = Vector3.zero;
    public Vector3 m_Force = Vector3.zero;
    public List<DynamicBoneColliderBase> m_Colliders;
    public List<Transform> m_Exclusions;
    public FreezeAxis m_FreezeAxis;
    public bool m_DistantDisable;
    public Transform m_ReferenceObject;
    public float m_DistanceToObject = 20f;
    public List<Transform> m_notRolls;

    /// <summary>0 = no simulation (bones stay at their animated pose), 1 = full.</summary>
    [System.NonSerialized] public float Weight = 1f;

    private sealed class Particle
    {
        public Transform T;
        public int Parent = -1;
        public float Damping, Elasticity, Stiffness, Inert, Radius, Depth;
        public Vector3 Pos, Prev, EndOffset, InitLocalPos;
        public Quaternion InitLocalRot;
    }

    private readonly List<Particle> _particles = new List<Particle>();
    private Vector3 _rootPrev;
    private float _scale = 1f;
    private float _time;
    private float _totalDepth;

    private void Start() => Setup();

    private void OnValidate()
    {
        if (Application.isPlaying && _particles.Count > 0)
            UpdateParameters();
    }

    private void OnDisable() => ResetTransforms();

    public void Setup()
    {
        ResetTransforms();
        _particles.Clear();
        if (m_Root == null)
            return;
        _totalDepth = 0f;
        Add(m_Root, -1, 0f);
        UpdateParameters();
        foreach (Particle p in _particles)
            p.Prev = p.Pos = p.T != null ? p.T.position : _particles[p.Parent].T.TransformPoint(p.EndOffset);
        _rootPrev = transform.position;
    }

    private void Add(Transform t, int parent, float depth)
    {
        var p = new Particle { T = t, Parent = parent };
        if (t != null)
        {
            p.InitLocalPos = t.localPosition;
            p.InitLocalRot = t.localRotation;
        }
        else
        {
            // Virtual end particle past the last bone.
            Transform last = _particles[parent].T;
            if (m_EndLength > 0f)
            {
                Transform lp = last.parent;
                p.EndOffset = lp != null ? last.InverseTransformPoint(last.position * 2f - lp.position) * m_EndLength : Vector3.zero;
            }
            else
            {
                p.EndOffset = last.InverseTransformPoint(last.TransformDirection(m_EndOffset) + last.position);
            }
        }
        if (parent >= 0)
        {
            Vector3 a = _particles[parent].T.position;
            Vector3 b = t != null ? t.position : _particles[parent].T.TransformPoint(p.EndOffset);
            depth += (b - a).magnitude;
        }
        p.Depth = depth;
        _totalDepth = Mathf.Max(_totalDepth, depth);
        int index = _particles.Count;
        _particles.Add(p);
        if (t == null)
            return;
        bool any = false;
        foreach (Transform child in t)
        {
            if (m_Exclusions != null && m_Exclusions.Contains(child))
                continue;
            any = true;
            Add(child, index, depth);
        }
        if (!any && (m_EndLength > 0f || m_EndOffset != Vector3.zero))
            Add(null, index, depth);
    }

    private void UpdateParameters()
    {
        foreach (Particle p in _particles)
        {
            float x = _totalDepth > 0f ? p.Depth / _totalDepth : 0f;
            p.Damping = Mathf.Clamp01(m_Damping * Eval(m_DampingDistrib, x));
            p.Elasticity = Mathf.Clamp01(m_Elasticity * Eval(m_ElasticityDistrib, x));
            p.Stiffness = Mathf.Clamp01(m_Stiffness * Eval(m_StiffnessDistrib, x));
            p.Inert = Mathf.Clamp01(m_Inert * Eval(m_InertDistrib, x));
            p.Radius = Mathf.Max(0f, m_Radius * Eval(m_RadiusDistrib, x));
        }
    }

    private static float Eval(AnimationCurve c, float x) => c != null && c.keys.Length > 0 ? c.Evaluate(x) : 1f;

    private void ResetTransforms()
    {
        foreach (Particle p in _particles)
        {
            if (p.T == null)
                continue;
            p.T.localPosition = p.InitLocalPos;
            p.T.localRotation = p.InitLocalRot;
        }
    }

    private void Update()
    {
        // Back to the rest pose so the animator (or nothing) defines this frame's target pose.
        if (Weight > 0f)
            ResetTransforms();
    }

    private void LateUpdate()
    {
        if (Weight <= 0f || _particles.Count == 0)
            return;
        _scale = Mathf.Abs(transform.lossyScale.x);
        float dt = m_UpdateMode == UpdateMode.UnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        float step = 1f / Mathf.Max(1f, m_UpdateRate);
        _time += dt;
        int loops = 0;
        Vector3 move = transform.position - _rootPrev;
        _rootPrev = transform.position;
        while (_time >= step && loops < 3)
        {
            _time -= step;
            loops++;
            Integrate(loops == 1 ? move : Vector3.zero);
            Constrain();
        }
        if (_time >= step)
            _time = 0f; // drop the backlog after a hitch instead of spiralling
        if (loops == 0)
            Constrain();
        Apply();
    }

    private void Integrate(Vector3 objectMove)
    {
        Vector3 force = (m_Gravity + m_Force) * _scale;
        for (int i = 0; i < _particles.Count; i++)
        {
            Particle p = _particles[i];
            if (p.Parent < 0)
            {
                p.Prev = p.Pos;
                p.Pos = p.T.position;
                continue;
            }
            Vector3 v = p.Pos - p.Prev;
            Vector3 rmove = objectMove * p.Inert;
            p.Prev = p.Pos + rmove;
            p.Pos += v * (1f - p.Damping) + force + rmove;
        }
    }

    private void Constrain()
    {
        for (int i = 1; i < _particles.Count; i++)
        {
            Particle p = _particles[i];
            Particle parent = _particles[p.Parent];
            Vector3 restA = parent.T.position;
            Vector3 restB = p.T != null ? p.T.position : parent.T.TransformPoint(p.EndOffset);
            float restLen = (restB - restA).magnitude;
            // Where the bone would be if it followed its parent rigidly.
            Vector3 target = parent.Pos + (restB - restA);
            Vector3 toTarget = target - p.Pos;
            p.Pos += toTarget * p.Elasticity;
            if (p.Stiffness > 0f)
            {
                Vector3 d = p.Pos - target;
                float max = restLen * (1f - p.Stiffness) * 2f;
                if (d.sqrMagnitude > max * max)
                    p.Pos = target + d.normalized * max;
            }
            if (m_Colliders != null)
            {
                float r = p.Radius * _scale;
                foreach (DynamicBoneColliderBase c in m_Colliders)
                {
                    if (c != null && c.enabled)
                        c.Collide(ref p.Pos, r);
                }
            }
            if (m_FreezeAxis != FreezeAxis.None)
            {
                Vector3 n = m_FreezeAxis == FreezeAxis.X ? parent.T.right : m_FreezeAxis == FreezeAxis.Y ? parent.T.up : parent.T.forward;
                p.Pos -= n * Vector3.Dot(p.Pos - parent.Pos, n);
            }
            Vector3 dir = p.Pos - parent.Pos;
            float len = dir.magnitude;
            if (len > 1e-6f)
                p.Pos = parent.Pos + dir * (restLen / len);
        }
    }

    private void Apply()
    {
        for (int i = 1; i < _particles.Count; i++)
        {
            Particle p = _particles[i];
            Particle parent = _particles[p.Parent];
            Transform pt = parent.T;
            // Only a single-child parent can be rotated without disturbing its siblings.
            if (pt.childCount <= 1 || p.T == null)
            {
                Vector3 local = p.T != null ? p.T.localPosition : p.EndOffset;
                Vector3 from = pt.TransformDirection(local);
                Vector3 to = p.Pos - parent.Pos;
                if (from.sqrMagnitude > 1e-10f && to.sqrMagnitude > 1e-10f)
                {
                    Quaternion r = Quaternion.FromToRotation(from, to);
                    pt.rotation = Quaternion.Slerp(Quaternion.identity, r, Weight) * pt.rotation;
                }
            }
            if (p.T != null)
                p.T.position = Vector3.Lerp(p.T.position, p.Pos, Weight);
        }
    }
}
