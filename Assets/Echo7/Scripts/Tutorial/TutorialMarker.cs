using System;
using UnityEngine;

namespace Echo7
{
    /// <summary>Голографический маркер цели обучения: световой столб, вращающееся кольцо и стрелка.</summary>
    public class TutorialMarker : MonoBehaviour
    {
        Func<Vector3?> _target;
        Transform _ring, _arrow, _beam;

        public static TutorialMarker Create(Transform parent)
        {
            var go = new GameObject("TutorialMarker");
            go.transform.SetParent(parent, false);
            var m = go.AddComponent<TutorialMarker>();
            var glow = MaterialFactory.Cached("marker_glow", () => MaterialFactory.Transparent(new Color(0.35f, 0.85f, 1f, 0.35f), 0.9f, new Color(0.3f, 0.8f, 1f) * 2f));
            var solid = MaterialFactory.Cached("marker_solid", () => MaterialFactory.Emissive(new Color(0.35f, 0.85f, 1f), new Color(0.35f, 0.85f, 1f), 3f));
            var mb = new ModelBuilder(go.transform, "m") { CastShadows = false };
            m._beam = mb.Cyl(new Vector3(0, 6f, 0), 0.18f, 12f, glow, null, null, 10);
            m._ring = mb.Group("ring", new Vector3(0, 0.25f, 0));
            mb.Torus(Vector3.zero, 1.6f, 0.06f, solid, m._ring);
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f * Mathf.Deg2Rad;
                mb.Box(new Vector3(Mathf.Cos(a) * 1.6f, 0, Mathf.Sin(a) * 1.6f), new Vector3(0.3f, 0.08f, 0.3f), solid, 0f, m._ring, new Vector3(0, -i * 120f, 0));
            }
            m._arrow = mb.Group("arrow", new Vector3(0, 3.2f, 0));
            mb.Frustum(Vector3.zero, 0.02f, 0.55f, 0.9f, solid, m._arrow, null, 4);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            return m;
        }

        public void Follow(Func<Vector3?> target)
        {
            _target = target;
            gameObject.SetActive(true);
            Update();
        }

        public void Hide()
        {
            _target = null;
            gameObject.SetActive(false);
        }

        void Update()
        {
            var p = _target?.Invoke();
            if (!p.HasValue) { if (_target != null) transform.position = new Vector3(0, -1000f, 0); return; }
            var pos = p.Value;
            if (Game.Grid != null) pos.y = Game.Grid.SampleHeight(pos);
            transform.position = pos;
            float t = Time.unscaledTime;
            if (_ring != null)
            {
                _ring.localRotation = Quaternion.Euler(0, t * 60f, 0);
                _ring.localScale = Vector3.one * (1f + Mathf.Sin(t * 3f) * 0.08f);
            }
            if (_arrow != null) _arrow.localPosition = new Vector3(0, 3.2f + Mathf.Sin(t * 4f) * 0.35f, 0);
            if (_arrow != null) _arrow.localRotation = Quaternion.Euler(0, t * 90f, 0); // конус узким концом вниз
        }
    }
}
