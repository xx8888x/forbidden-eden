using UnityEngine;
using UnityEngine.EventSystems;

namespace ForbiddenEden.World
{
    /// <summary>
    /// Orbit camera around a focus point. In free mode the player rotates with the right
    /// mouse button, pans with the middle button and zooms with the wheel (the same
    /// controls as 角色預覽 in the wireframes); otherwise it eases to the requested shot.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public Camera Cam { get; private set; }
        public bool FreeControl;
        public float Speed = 1f;

        private Vector3 _focus = new Vector3(0, 1.4f, 0);
        private float _yaw;
        private float _pitch = 6f;
        private float _distance = 3f;
        private float _fov = 35f;

        private Vector3 _focusTarget;
        private float _yawTarget;
        private float _pitchTarget = 6f;
        private float _distanceTarget = 3f;
        private float _fovTarget = 35f;
        private float _sway;

        public static CameraRig Attach()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            var rig = cam.gameObject.GetComponent<CameraRig>() ?? cam.gameObject.AddComponent<CameraRig>();
            rig.Cam = cam;
            rig._focusTarget = rig._focus;
            return rig;
        }

        /// <summary>Frames a point. yaw 0 looks along +Z (at a character facing the camera).</summary>
        public void Shot(Vector3 focus, float distance, float yaw = 0, float pitch = 5, float fov = 35, bool snap = false)
        {
            _focusTarget = focus;
            _distanceTarget = distance;
            _yawTarget = yaw;
            _pitchTarget = pitch;
            _fovTarget = fov;
            if (snap)
            {
                _focus = focus;
                _distance = distance;
                _yaw = yaw;
                _pitch = pitch;
                _fov = fov;
                Apply();
            }
        }

        /// <summary>Gentle drift for idle menus (title screen).</summary>
        public void SetSway(float amount) => _sway = amount;

        // Shot sizes of the EDEN camera language (A = 特寫…遠景, angles by id).
        public void ShotById(string shotId, string angleId, Vector3 subjectFeet, float eyeHeight)
        {
            float dist = 2.4f;
            float h = eyeHeight * 0.92f;
            switch (shotId)
            {
                case "A01": dist = 0.9f; h = eyeHeight; break;          // 特寫
                case "A02": dist = 1.5f; h = eyeHeight * 0.95f; break;  // 近景
                case "A03": dist = 2.4f; h = eyeHeight * 0.80f; break;  // 中景
                case "A04": dist = 4.2f; h = eyeHeight * 0.55f; break;  // 全身
                case "R01": dist = 6.5f; h = eyeHeight * 0.6f; break;   // 遠景
                case "R02": dist = 9f; h = eyeHeight * 0.7f; break;
                case "R03": dist = 12f; h = eyeHeight; break;
                case "R04": dist = 3.2f; h = eyeHeight * 0.85f; break;
                case "R05": dist = 5f; h = eyeHeight * 0.9f; break;
            }
            float yaw = 0, pitch = 4;
            switch (angleId)
            {
                case "F3Q": yaw = 32; break;
                case "SIDE": yaw = 85; break;
                case "R3Q": yaw = 145; break;
                case "REAR": yaw = 180; break;
                case "HIGH": pitch = 28; break;
                case "LOW": pitch = -12; break;
                case "TOP": pitch = 70; break;
            }
            Shot(subjectFeet + new Vector3(0, h, 0), dist, yaw, pitch);
        }

        private void LateUpdate()
        {
            if (Cam == null)
                return;
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (FreeControl)
            {
                if (Input.GetMouseButton(1))
                {
                    _yawTarget += Input.GetAxis("Mouse X") * 4f * Speed;
                    _pitchTarget = Mathf.Clamp(_pitchTarget - Input.GetAxis("Mouse Y") * 3f * Speed, -30, 80);
                }
                if (Input.GetMouseButton(2))
                {
                    Vector3 right = Cam.transform.right;
                    Vector3 up = Cam.transform.up;
                    _focusTarget -= (right * Input.GetAxis("Mouse X") + up * Input.GetAxis("Mouse Y")) * 0.02f * _distance;
                }
                if (!overUi)
                {
                    float wheel = Input.mouseScrollDelta.y;
                    if (Mathf.Abs(wheel) > 0.01f)
                        _distanceTarget = Mathf.Clamp(_distanceTarget * (1f - wheel * 0.1f), 0.5f, 20f);
                }
            }
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 5f * Mathf.Max(0.3f, Speed));
            _focus = Vector3.Lerp(_focus, _focusTarget, k);
            _distance = Mathf.Lerp(_distance, _distanceTarget, k);
            _yaw = Mathf.LerpAngle(_yaw, _yawTarget, k);
            _pitch = Mathf.Lerp(_pitch, _pitchTarget, k);
            _fov = Mathf.Lerp(_fov, _fovTarget, k);
            Apply();
        }

        private void Apply()
        {
            float t = Time.time;
            float swayYaw = _sway * Mathf.Sin(t * 0.13f) * 6f;
            float swayPitch = _sway * Mathf.Sin(t * 0.17f) * 2f;
            Quaternion rot = Quaternion.Euler(_pitch + swayPitch, _yaw + swayYaw, 0);
            transform.position = _focus - rot * Vector3.forward * _distance;
            transform.rotation = rot;
            if (Cam != null)
                Cam.fieldOfView = _fov;
        }
    }
}
