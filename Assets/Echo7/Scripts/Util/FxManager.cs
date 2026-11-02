using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Echo7
{
    public enum FxKind { Muzzle, Sparks, Ichor, Blood, Explosion, Smoke, Dust, Weld, Debris, Heal, Splash, Glow }

    /// <summary>
    /// Все эффекты прототипа: по одной мировой системе частиц на тип (Emit в нужной точке),
    /// пул LineRenderer'ов для трассеров/лучей и пул коротких вспышек света.
    /// </summary>
    public class FxManager : MonoBehaviour
    {
        readonly Dictionary<FxKind, ParticleSystem> _systems = new Dictionary<FxKind, ParticleSystem>();
        readonly List<LineFx> _lines = new List<LineFx>();
        readonly List<FlashFx> _flashes = new List<FlashFx>();
        Texture2D _soft;
        Material _lineMat;

        class LineFx { public LineRenderer R; public float Until; public float Dur; public Color C; public float Width; }
        class FlashFx { public Light L; public float Until; public float Dur; public float Intensity; }

        public void Init()
        {
            _soft = ProceduralTextures.SoftCircle(64, 0.2f);
            _lineMat = MaterialFactory.Unlit(Color.white, _soft);
            _lineMat.mainTexture = null;

            Make(FxKind.Muzzle, 0.06f, 0.1f, 0.45f, 2f, 0f, 60, new Color(1f, 0.8f, 0.4f), new Color(1f, 0.4f, 0.1f, 0f));
            Make(FxKind.Sparks, 0.35f, 6f, 0.08f, 0.15f, 9f, 300, new Color(1f, 0.85f, 0.4f), new Color(1f, 0.4f, 0.1f, 0f));
            Make(FxKind.Ichor, 0.5f, 4f, 0.18f, 0.35f, 9f, 300, new Color(0.55f, 0.75f, 0.15f), new Color(0.3f, 0.45f, 0.05f, 0f));
            Make(FxKind.Blood, 0.5f, 3.5f, 0.15f, 0.3f, 9f, 200, new Color(0.6f, 0.05f, 0.05f), new Color(0.3f, 0f, 0f, 0f));
            Make(FxKind.Explosion, 0.6f, 7f, 1.2f, 3.2f, -0.5f, 400, new Color(1f, 0.75f, 0.3f), new Color(0.4f, 0.12f, 0.05f, 0f));
            Make(FxKind.Smoke, 2.2f, 1.5f, 1.2f, 3.5f, -0.3f, 400, new Color(0.25f, 0.24f, 0.23f, 0.7f), new Color(0.3f, 0.3f, 0.3f, 0f));
            Make(FxKind.Dust, 1.2f, 1.6f, 0.6f, 1.8f, -0.05f, 400, new Color(0.62f, 0.52f, 0.38f, 0.55f), new Color(0.65f, 0.55f, 0.4f, 0f));
            Make(FxKind.Weld, 0.25f, 3.5f, 0.06f, 0.12f, 6f, 300, new Color(0.7f, 0.9f, 1f), new Color(0.3f, 0.6f, 1f, 0f));
            Make(FxKind.Debris, 1.1f, 7f, 0.2f, 0.45f, 14f, 200, new Color(0.35f, 0.34f, 0.33f), new Color(0.3f, 0.3f, 0.3f, 0f));
            Make(FxKind.Heal, 1f, 1.2f, 0.2f, 0.35f, -1.5f, 200, new Color(0.3f, 1f, 0.5f), new Color(0.3f, 1f, 0.5f, 0f));
            Make(FxKind.Splash, 0.6f, 4f, 0.15f, 0.3f, 9f, 300, new Color(0.7f, 0.85f, 1f, 0.8f), new Color(0.7f, 0.85f, 1f, 0f));
            Make(FxKind.Glow, 0.4f, 0.2f, 0.6f, 1.2f, 0f, 100, new Color(0.4f, 0.8f, 1f), new Color(0.4f, 0.8f, 1f, 0f));
        }

        void Make(FxKind kind, float life, float speed, float sizeMin, float sizeMax, float gravity, int max, Color start, Color end)
        {
            var go = new GameObject("fx_" + kind);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;      // система всегда «играет», частицы выпускаются вручную через Emit
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor = start;
            main.gravityModifier = gravity / 9.81f;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            var em = ps.emission;
            em.enabled = false;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.15f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
                      new[] { new GradientAlphaKey(start.a, 0f), new GradientAlphaKey(start.a * 0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, kind == FxKind.Smoke || kind == FxKind.Dust || kind == FxKind.Explosion ? 0.6f : 1f, 1f, kind == FxKind.Smoke || kind == FxKind.Dust ? 1.6f : 0.2f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = MaterialFactory.Unlit(Color.white, _soft);
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            _systems[kind] = ps;
        }

        public void Emit(FxKind kind, Vector3 pos, int count, Vector3? dir = null, float spread = 1f, Color? color = null, float sizeMul = 1f)
        {
            if (!_systems.TryGetValue(kind, out var ps)) return;
            var main = ps.main;
            for (int i = 0; i < count; i++)
            {
                var ep = new ParticleSystem.EmitParams
                {
                    position = pos + Random.insideUnitSphere * 0.1f,
                    applyShapeToPosition = true
                };
                if (dir.HasValue)
                {
                    var d = (dir.Value.normalized + Random.insideUnitSphere * spread).normalized;
                    ep.velocity = d * Random.Range(0.4f, 1f) * main.startSpeed.constantMax;
                }
                if (color.HasValue) ep.startColor = color.Value;
                if (sizeMul != 1f) ep.startSize = Random.Range(main.startSize.constantMin, main.startSize.constantMax) * sizeMul;
                ps.Emit(ep, 1);
            }
        }

        public void Explosion(Vector3 pos, float radius)
        {
            float k = Mathf.Clamp(radius / 4f, 0.4f, 3f);
            Emit(FxKind.Explosion, pos + Vector3.up * 0.5f, Mathf.RoundToInt(28 * k), null, 1f, null, k);
            Emit(FxKind.Smoke, pos + Vector3.up * 0.8f, Mathf.RoundToInt(16 * k), Vector3.up, 0.8f, null, k);
            Emit(FxKind.Debris, pos + Vector3.up * 0.3f, Mathf.RoundToInt(14 * k), Vector3.up, 0.9f);
            Emit(FxKind.Sparks, pos + Vector3.up * 0.5f, Mathf.RoundToInt(20 * k), Vector3.up, 1.2f);
            Flash(pos + Vector3.up * 1.5f, new Color(1f, 0.6f, 0.25f), 6f * k + 6f, 5f, 0.35f);
        }

        /// <summary>Трассер/луч: линия, которая гаснет за duration.</summary>
        public void Line(Vector3 a, Vector3 b, Color c, float width, float duration)
        {
            LineFx lf = null;
            for (int i = 0; i < _lines.Count; i++) if (Time.time > _lines[i].Until) { lf = _lines[i]; break; }
            if (lf == null)
            {
                if (_lines.Count > 96) lf = _lines[Random.Range(0, _lines.Count)];
                else
                {
                    var go = new GameObject("line");
                    go.transform.SetParent(transform, false);
                    var lr = go.AddComponent<LineRenderer>();
                    lr.sharedMaterial = _lineMat;
                    lr.positionCount = 2;
                    lr.useWorldSpace = true;
                    lr.numCapVertices = 2;
                    lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    lr.receiveShadows = false;
                    lf = new LineFx { R = lr };
                    _lines.Add(lf);
                }
            }
            lf.R.enabled = true;
            lf.R.SetPosition(0, a);
            lf.R.SetPosition(1, b);
            lf.Dur = Mathf.Max(0.01f, duration);
            lf.Until = Time.time + lf.Dur;
            lf.C = c;
            lf.Width = width;
            lf.R.startWidth = lf.R.endWidth = width;
            lf.R.startColor = lf.R.endColor = c;
        }

        public void Flash(Vector3 pos, Color c, float range, float intensity, float duration)
        {
            FlashFx f = null;
            for (int i = 0; i < _flashes.Count; i++) if (Time.time > _flashes[i].Until) { f = _flashes[i]; break; }
            if (f == null)
            {
                if (_flashes.Count >= 6) return;
                var go = new GameObject("flash");
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                f = new FlashFx { L = l };
                _flashes.Add(f);
            }
            f.L.transform.position = pos;
            f.L.color = c;
            f.L.range = range;
            f.Intensity = intensity;
            f.L.intensity = intensity;
            f.L.enabled = true;
            f.Dur = duration;
            f.Until = Time.time + duration;
        }

        void Update()
        {
            float now = Time.time;
            foreach (var lf in _lines)
            {
                if (!lf.R.enabled) continue;
                float k = (lf.Until - now) / lf.Dur;
                if (k <= 0f) { lf.R.enabled = false; continue; }
                var c = lf.C; c.a *= k;
                lf.R.startColor = lf.R.endColor = c;
                lf.R.startWidth = lf.R.endWidth = lf.Width * (0.5f + 0.5f * k);
            }
            foreach (var f in _flashes)
            {
                if (!f.L.enabled) continue;
                float k = (f.Until - now) / f.Dur;
                if (k <= 0f) { f.L.enabled = false; continue; }
                f.L.intensity = f.Intensity * k;
            }
        }

        /// <summary>Создать отдельную систему частиц (дождь, рой насекомых, выхлоп челнока).</summary>
        public ParticleSystem CreateLooping(string name, Transform parent, Color color, float rate, float life, float speed, float size, ParticleSystemShapeType shapeType, Vector3 shapeScale, float gravity = 0f, bool stretched = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = life;
            main.startSpeed = speed;
            main.startSize = size;
            main.startColor = color;
            main.gravityModifier = gravity / 9.81f;
            main.maxParticles = 3000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = rate;
            var shape = ps.shape;
            shape.shapeType = shapeType;
            shape.scale = shapeScale;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(color.a, 0.15f), new GradientAlphaKey(color.a, 0.8f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = MaterialFactory.Unlit(Color.white, _soft);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (stretched)
            {
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.06f;
                r.lengthScale = 1f;
            }
            ps.Play();
            return ps;
        }
    }
}
