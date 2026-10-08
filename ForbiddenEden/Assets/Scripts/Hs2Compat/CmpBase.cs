// Stand-ins for the components HS2 part prefabs carry (one class per file, as Unity requires for MonoScripts).
// Unity binds a bundle's MonoBehaviour to the script with the same assembly (Assembly-CSharp), namespace and
// class name, so these classes let the exporters read HS2's serialized part data: default colours, colourable
// renderers, hair bones. Only the serialized fields matter; their names and types must match HS2's.
using UnityEngine;

namespace AIChara
{
    public class CmpBase : MonoBehaviour
    {
        public Renderer[] rendCheckVisible;
        public int reacquire;
    }
}
