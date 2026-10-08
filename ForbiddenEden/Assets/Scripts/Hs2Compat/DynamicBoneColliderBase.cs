using UnityEngine;

/// <summary>Stand-in for HS2's collider base (field names match its serialized data). See DynamicBone.cs.</summary>
public class DynamicBoneColliderBase : MonoBehaviour
{
    public enum Direction { X, Y, Z }

    public enum Bound { Outside, Inside }

    public Direction m_Direction = Direction.Y;
    public Vector3 m_Center = Vector3.zero;
    public Bound m_Bound;

    /// <summary>Pushes a particle of the given radius out of (or, for Inside, back into) this collider.</summary>
    public virtual void Collide(ref Vector3 particlePosition, float particleRadius)
    {
    }
}
