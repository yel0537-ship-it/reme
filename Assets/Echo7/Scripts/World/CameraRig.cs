using UnityEngine;

namespace Echo7
{
    /// <summary>
    /// Камера в духе Diablo 4: фиксированный изометрический ракурс сверху-сзади, плавное следование
    /// за героем, зум колесом. Q/E... не вращают (как в D4), но угол можно поменять в инспекторе.
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        public Camera Cam { get; private set; }
        public Transform Target;
        public float Yaw = 45f;
        public float Pitch = 52f;
        public float Distance = 26f;
        public float MinDistance = 14f;
        public float MaxDistance = 46f;
        public float FollowSharpness = 8f;
        float _targetDistance;
        Vector3 _shake;
        float _shakeTime, _shakeAmp;

        public void Init()
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(transform, false);
            Cam = camGo.AddComponent<Camera>();
            Cam.fieldOfView = 38f;
            Cam.nearClipPlane = 0.5f;
            Cam.farClipPlane = 600f;
            Cam.clearFlags = CameraClearFlags.Skybox;
            Cam.allowHDR = true;
            camGo.AddComponent<AudioListener>();
            _targetDistance = Distance;
            Apply(true);
        }

        public void SnapTo(Vector3 p)
        {
            transform.position = p;
        }

        public void Shake(float amplitude, float duration)
        {
            _shakeAmp = Mathf.Max(_shakeAmp, amplitude);
            _shakeTime = Mathf.Max(_shakeTime, duration);
        }

        void LateUpdate()
        {
            if (Cam == null) return;
            if (!Game.Ui || !Game.Ui.WantsScroll)
            {
                float scroll = InputBridge.ScrollDelta;
                if (Mathf.Abs(scroll) > 0.01f)
                    _targetDistance = Mathf.Clamp(_targetDistance - scroll * 3f, MinDistance, MaxDistance);
            }
            Distance = Mathf.Lerp(Distance, _targetDistance, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
            Apply(false);
        }

        void Apply(bool snap)
        {
            if (Target != null)
            {
                var p = Target.position;
                transform.position = snap ? p : Vector3.Lerp(transform.position, p, 1f - Mathf.Exp(-FollowSharpness * Time.unscaledDeltaTime));
            }
            transform.rotation = Quaternion.Euler(0f, Yaw, 0f);
            if (_shakeTime > 0f)
            {
                _shakeTime -= Time.unscaledDeltaTime;
                _shake = Random.insideUnitSphere * _shakeAmp * Mathf.Clamp01(_shakeTime * 3f);
                if (_shakeTime <= 0f) _shakeAmp = 0f;
            }
            else _shake = Vector3.zero;
            var rot = Quaternion.Euler(Pitch, 0f, 0f);
            Cam.transform.localRotation = rot;
            Cam.transform.localPosition = rot * new Vector3(0f, 0f, -Distance) + Vector3.up * 1.2f + _shake;
        }

        /// <summary>Луч из камеры через курсор.</summary>
        public Ray MouseRay() => Cam.ScreenPointToRay(InputBridge.MousePosition);

        public bool MouseOnGround(out Vector3 point)
        {
            var ray = MouseRay();
            if (Physics.Raycast(ray, out var hit, 800f, Layers.GroundMask, QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
                return true;
            }
            // запасной вариант — плоскость базы
            var plane = new Plane(Vector3.up, new Vector3(0, Game.World != null ? Game.World.BaseHeight : 0f, 0));
            if (plane.Raycast(ray, out float enter)) { point = ray.GetPoint(enter); return true; }
            point = Vector3.zero;
            return false;
        }
    }
}
