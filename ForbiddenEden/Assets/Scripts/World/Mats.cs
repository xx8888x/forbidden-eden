using UnityEngine;

namespace ForbiddenEden.World
{
    /// <summary>
    /// Materials for the primitive set dressing. Built from Resources/EdenLit.mat so the
    /// Standard shader is guaranteed to ship in the player (the default primitive material
    /// is stripped from builds, which shows up as magenta).
    /// </summary>
    public static class Mats
    {
        private static Material _base;

        public static Material Make(Color color, float gloss = 0.2f)
        {
            if (_base == null)
            {
                _base = Resources.Load<Material>("EdenLit");
                if (_base == null)
                {
                    Shader shader = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse");
                    _base = new Material(shader);
                }
            }
            var m = new Material(_base) { color = color };
            if (m.HasProperty("_Glossiness"))
                m.SetFloat("_Glossiness", gloss);
            return m;
        }

        public static void Paint(GameObject go, Color color, float gloss = 0.2f)
        {
            Renderer r = go.GetComponent<Renderer>();
            if (r != null)
                r.sharedMaterial = Make(color, gloss);
        }
    }
}
