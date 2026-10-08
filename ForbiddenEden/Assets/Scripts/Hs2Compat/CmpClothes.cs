using UnityEngine;

namespace AIChara
{
    /// <summary>HS2 clothes part data (see CmpBase.cs). Colours 01–03 are user colours, 04 is fixed.</summary>
    [DisallowMultipleComponent]
    public class CmpClothes : CmpBase
    {
        public bool useBreak;
        public Renderer[] rendNormal01;
        public Renderer[] rendNormal02;
        public Renderer[] rendNormal03;
        public bool useColorN01;
        public bool useColorN02;
        public bool useColorN03;
        public bool useColorA01;
        public bool useColorA02;
        public bool useColorA03;
        public GameObject objTopDef;
        public GameObject objTopHalf;
        public GameObject objBotDef;
        public GameObject objBotHalf;
        public GameObject[] objOpt01;
        public GameObject[] objOpt02;
        public Vector4 uvScalePattern = new Vector4(1f, 1f, 0f, 0f);
        public Color defMainColor01 = Color.white;
        public Color defMainColor02 = Color.white;
        public Color defMainColor03 = Color.white;
        public int defPtnIndex01;
        public int defPtnIndex02;
        public int defPtnIndex03;
        public Color defPatternColor01 = Color.white;
        public Color defPatternColor02 = Color.white;
        public Color defPatternColor03 = Color.white;
        public float defGloss01;
        public float defGloss02;
        public float defGloss03;
        public float defMetallic01;
        public float defMetallic02;
        public float defMetallic03;
        public Vector4 defLayout01 = new Vector4(10f, 10f, 0f, 0f);
        public Vector4 defLayout02 = new Vector4(10f, 10f, 0f, 0f);
        public Vector4 defLayout03 = new Vector4(10f, 10f, 0f, 0f);
        public float defRotation01;
        public float defRotation02;
        public float defRotation03;
        public Color defMainColor04 = Color.white;
        public float defGloss04;
        public float defMetallic04;
        public int setdefault;
    }
}
