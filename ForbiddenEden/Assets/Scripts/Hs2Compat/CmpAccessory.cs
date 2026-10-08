using UnityEngine;

namespace AIChara
{
    /// <summary>HS2 accessory part data (see CmpBase.cs).</summary>
    [DisallowMultipleComponent]
    public class CmpAccessory : CmpBase
    {
        public bool typeHair;
        public Renderer[] rendNormal;
        public bool useColor01;
        public bool useGloss01 = true;
        public bool useMetallic01 = true;
        public Color defColor01 = Color.white;
        public float defGlossPower01 = 0.5f;
        public float defMetallicPower01 = 0.5f;
        public bool useColor02;
        public bool useGloss02 = true;
        public bool useMetallic02 = true;
        public Color defColor02 = Color.white;
        public float defGlossPower02 = 0.5f;
        public float defMetallicPower02 = 0.5f;
        public bool useColor03;
        public bool useGloss03 = true;
        public bool useMetallic03 = true;
        public Color defColor03 = Color.white;
        public float defGlossPower03 = 0.5f;
        public float defMetallicPower03 = 0.5f;
        public Renderer[] rendAlpha;
        public bool useGloss04 = true;
        public bool useMetallic04 = true;
        public Color defColor04 = Color.white;
        public float defGlossPower04 = 0.5f;
        public float defMetallicPower04 = 0.5f;
        public Transform trfMove01;
        public Transform trfMove02;
        public int setcolor;
    }
}
