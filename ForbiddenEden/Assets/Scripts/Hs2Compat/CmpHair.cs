using System;
using UnityEngine;

namespace AIChara
{
    /// <summary>HS2 hair part data (see CmpBase.cs).</summary>
    [DisallowMultipleComponent]
    public class CmpHair : CmpBase
    {
        [Serializable]
        public class BoneInfo
        {
            public Transform trfCorrect;
            public DynamicBone[] dynamicBone;
            public Vector3 basePos = Vector3.zero;
            public Vector3 baseRot = Vector3.zero;
            public Vector3 posMin = Vector3.zero;
            public Vector3 posMax = Vector3.zero;
            public Vector3 rotMin = Vector3.zero;
            public Vector3 rotMax = Vector3.zero;
            public Vector3 moveRate = Vector3.zero;
            public Vector3 rotRate = Vector3.zero;
        }

        public Renderer[] rendHair;
        public bool useTopColor = true;
        public bool useUnderColor = true;
        public bool useSameSkinColorButton;
        public bool useMesh;
        public BoneInfo[] boneInfo;
        public int setdefaultposition;
        public int setdefaultrotation;
        public bool useAcsColor01;
        public bool useAcsColor02;
        public bool useAcsColor03;
        public Renderer[] rendAccessory;
        public Color[] acsDefColor;
        public int setcolor;
    }
}
