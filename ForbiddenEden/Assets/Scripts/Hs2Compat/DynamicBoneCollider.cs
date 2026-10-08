using UnityEngine;

/// <summary>Sphere (height 0) or capsule collider for DynamicBone chains, sized in this transform's scale.</summary>
public class DynamicBoneCollider : DynamicBoneColliderBase
{
    public float m_Radius = 0.5f;
    public float m_Height;

    public override void Collide(ref Vector3 particlePosition, float particleRadius)
    {
        float scale = Mathf.Abs(transform.lossyScale.x);
        float radius = m_Radius * scale;
        float half = Mathf.Max(0f, m_Height * 0.5f - m_Radius) * scale;
        Vector3 c = transform.TransformPoint(m_Center);
        Vector3 axis = m_Direction == Direction.X ? transform.right : m_Direction == Direction.Y ? transform.up : transform.forward;
        // Closest point on the capsule's core segment (a point for spheres).
        Vector3 a = c - axis * half, b = c + axis * half;
        Vector3 ab = b - a;
        float t = half > 0f ? Mathf.Clamp01(Vector3.Dot(particlePosition - a, ab) / ab.sqrMagnitude) : 0f;
        Vector3 core = a + ab * t;
        Vector3 d = particlePosition - core;
        float len = d.magnitude;
        if (m_Bound == Bound.Outside)
        {
            float min = radius + particleRadius;
            if (len < min && len > 1e-6f)
                particlePosition = core + d * (min / len);
        }
        else
        {
            float max = radius - particleRadius;
            if (len > max && len > 1e-6f)
                particlePosition = core + d * (max / len);
        }
    }
}
