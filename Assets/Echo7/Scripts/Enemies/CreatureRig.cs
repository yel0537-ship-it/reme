using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    /// <summary>
    /// Процедурные существа-заглушки (хитиновые насекомые, рогачи, охотники, «танк») с кодовой анимацией:
    /// перебирание ногами, выпад при атаке, прыжок, падение при смерти.
    /// </summary>
    public class CreatureRig : MonoBehaviour
    {
        struct Leg { public Transform Hip, Knee; public float Offset; public float Amp; public float Splay; }

        public float Speed;
        readonly List<Leg> _legs = new List<Leg>();
        Transform _body, _head;
        float _phase, _lunge, _deadT = -1f, _stride = 1f;
        Vector3 _bodyBase, _headBase;

        static Material Mat(string key, Color c, float smooth = 0.35f) => MaterialFactory.Cached(key, () => MaterialFactory.Lit(c, 0.1f, smooth));
        static Material Glow(string key, Color c) => MaterialFactory.Cached(key, () => MaterialFactory.Emissive(c, c, 2.5f));

        public static CreatureRig Build(Transform parent, EnemyKind kind, float size)
        {
            var root = new GameObject("Creature").transform;
            root.SetParent(parent, false);
            var rig = root.gameObject.AddComponent<CreatureRig>();
            var mb = new ModelBuilder(root, true);
            switch (kind)
            {
                case EnemyKind.Swarmer: rig.BuildSwarmer(mb); break;
                case EnemyKind.Jumper: rig.BuildJumper(mb); break;
                case EnemyKind.Brute: rig.BuildBrute(mb); break;
                case EnemyKind.Spitter: rig.BuildSpitter(mb); break;
                case EnemyKind.Tank: rig.BuildTank(mb); break;
                case EnemyKind.Grazer: rig.BuildGrazer(mb); break;
                case EnemyKind.Synth: rig.BuildSynth(mb); break;
                default: rig.BuildStalker(mb); break;
            }
            root.localScale = Vector3.one * size;
            rig._stride = 1f / Mathf.Max(0.3f, size);
            if (rig._body != null) rig._bodyBase = rig._body.localPosition;
            if (rig._head != null) rig._headBase = rig._head.localPosition;
            return rig;
        }

        void AddLeg(ModelBuilder mb, Transform parent, Vector3 hipPos, float upper, float lower, float thick, Material m, float splayY, float offset, float amp = 30f, float kneeBend = 50f)
        {
            var hip = mb.Group("hip", hipPos, parent, new Vector3(0, splayY, 0));
            mb.Box(new Vector3(0, -upper * 0.5f, 0), new Vector3(thick, upper, thick), m, thick * 0.3f, hip);
            var knee = mb.Group("knee", new Vector3(0, -upper, 0), hip, new Vector3(kneeBend * 0.3f, 0, 0));
            mb.Box(new Vector3(0, -lower * 0.5f, 0), new Vector3(thick * 0.8f, lower, thick * 0.8f), m, thick * 0.25f, knee);
            _legs.Add(new Leg { Hip = hip, Knee = knee, Offset = offset, Amp = amp, Splay = splayY });
        }

        // Высота «спины» существа ~ 1 при масштабе 1, ноги стоят на нуле.
        void BuildSwarmer(ModelBuilder mb)
        {
            var shell = Mat("ch_swarm", new Color(0.28f, 0.18f, 0.22f));
            var leg = Mat("ch_swarm_leg", new Color(0.18f, 0.12f, 0.14f));
            _body = mb.Group("body", new Vector3(0, 0.55f, 0));
            mb.Sphere(new Vector3(0, 0, -0.15f), new Vector3(0.9f, 0.55f, 1.1f), shell, _body);
            mb.Sphere(new Vector3(0, 0.05f, 0.45f), new Vector3(0.55f, 0.45f, 0.5f), shell, _body);
            _head = mb.Group("head", new Vector3(0, 0.05f, 0.65f), _body);
            mb.Sphere(new Vector3(-0.12f, 0.08f, 0.1f), Vector3.one * 0.12f, Glow("eye_or", new Color(1f, 0.5f, 0.1f)), _head);
            mb.Sphere(new Vector3(0.12f, 0.08f, 0.1f), Vector3.one * 0.12f, Glow("eye_or", new Color(1f, 0.5f, 0.1f)), _head);
            mb.Box(new Vector3(-0.1f, -0.1f, 0.22f), new Vector3(0.05f, 0.05f, 0.3f), leg, 0f, _head, new Vector3(0, 20, 0));
            mb.Box(new Vector3(0.1f, -0.1f, 0.22f), new Vector3(0.05f, 0.05f, 0.3f), leg, 0f, _head, new Vector3(0, -20, 0));
            for (int i = 0; i < 6; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float z = (i / 2 - 1) * 0.32f;
                AddLeg(mb, _body, new Vector3(side * 0.35f, -0.05f, z), 0.35f, 0.45f, 0.07f, leg, side * 70f, i * 1.7f, 35f, 80f);
            }
        }

        void BuildJumper(ModelBuilder mb)
        {
            var shell = Mat("ch_jump", new Color(0.52f, 0.55f, 0.22f));
            var dark = Mat("ch_jump_d", new Color(0.25f, 0.28f, 0.12f));
            _body = mb.Group("body", new Vector3(0, 0.75f, 0));
            mb.Sphere(new Vector3(0, 0.05f, -0.45f), new Vector3(0.5f, 0.45f, 0.95f), shell, _body);
            mb.Sphere(new Vector3(0, 0.1f, 0.15f), new Vector3(0.45f, 0.42f, 0.6f), dark, _body);
            _head = mb.Group("head", new Vector3(0, 0.2f, 0.55f), _body);
            mb.Sphere(Vector3.zero, new Vector3(0.32f, 0.3f, 0.38f), shell, _head);
            mb.Sphere(new Vector3(-0.1f, 0.06f, 0.15f), Vector3.one * 0.08f, Glow("eye_r", new Color(1f, 0.2f, 0.1f)), _head);
            mb.Sphere(new Vector3(0.1f, 0.06f, 0.15f), Vector3.one * 0.08f, Glow("eye_r", new Color(1f, 0.2f, 0.1f)), _head);
            mb.Box(new Vector3(-0.08f, 0.25f, 0.1f), new Vector3(0.02f, 0.5f, 0.02f), dark, 0f, _head, new Vector3(-40, 0, -15));
            mb.Box(new Vector3(0.08f, 0.25f, 0.1f), new Vector3(0.02f, 0.5f, 0.02f), dark, 0f, _head, new Vector3(-40, 0, 15));
            // мощные задние ноги
            AddLeg(mb, _body, new Vector3(-0.3f, 0.1f, -0.35f), 0.7f, 0.85f, 0.1f, dark, -15f, 0f, 25f, -90f);
            AddLeg(mb, _body, new Vector3(0.3f, 0.1f, -0.35f), 0.7f, 0.85f, 0.1f, dark, 15f, Mathf.PI, 25f, -90f);
            for (int i = 0; i < 4; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                AddLeg(mb, _body, new Vector3(side * 0.22f, -0.1f, 0.05f + (i / 2) * 0.25f), 0.35f, 0.4f, 0.05f, dark, side * 55f, i * 1.3f, 35f, 60f);
            }
        }

        void BuildBrute(ModelBuilder mb)
        {
            var shell = Mat("ch_brute", new Color(0.36f, 0.17f, 0.12f), 0.5f);
            var plate = Mat("ch_brute_p", new Color(0.22f, 0.12f, 0.1f), 0.6f);
            _body = mb.Group("body", new Vector3(0, 1.0f, 0));
            mb.Sphere(new Vector3(0, 0.05f, -0.2f), new Vector3(1.3f, 0.9f, 1.6f), shell, _body);
            for (int i = 0; i < 3; i++) mb.Sphere(new Vector3(0, 0.35f, -0.6f + i * 0.45f), new Vector3(1.1f - i * 0.1f, 0.4f, 0.55f), plate, _body);
            _head = mb.Group("head", new Vector3(0, -0.05f, 0.75f), _body);
            mb.Sphere(Vector3.zero, new Vector3(0.75f, 0.6f, 0.6f), plate, _head);
            mb.Frustum(new Vector3(0, 0.35f, 0.3f), 0.15f, 0.02f, 0.6f, Mat("horn", new Color(0.85f, 0.8f, 0.7f)), _head, new Vector3(50, 0, 0), 8);
            mb.Sphere(new Vector3(-0.22f, 0.05f, 0.25f), Vector3.one * 0.1f, Glow("eye_or", new Color(1f, 0.5f, 0.1f)), _head);
            mb.Sphere(new Vector3(0.22f, 0.05f, 0.25f), Vector3.one * 0.1f, Glow("eye_or", new Color(1f, 0.5f, 0.1f)), _head);
            for (int i = 0; i < 4; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float z = i < 2 ? 0.4f : -0.6f;
                AddLeg(mb, _body, new Vector3(side * 0.55f, -0.2f, z), 0.45f, 0.45f, 0.22f, plate, side * 20f, i * 1.57f, 25f, 30f);
            }
        }

        void BuildSpitter(ModelBuilder mb)
        {
            var shell = Mat("ch_spit", new Color(0.3f, 0.32f, 0.22f));
            var sac = MaterialFactory.Cached("ch_spit_sac", () => MaterialFactory.Emissive(new Color(0.45f, 0.75f, 0.15f), new Color(0.3f, 0.6f, 0.05f), 1.2f));
            _body = mb.Group("body", new Vector3(0, 0.75f, 0));
            mb.Sphere(new Vector3(0, 0.15f, -0.4f), new Vector3(0.9f, 0.85f, 1.0f), sac, _body);
            mb.Sphere(new Vector3(0, 0.0f, 0.2f), new Vector3(0.5f, 0.45f, 0.6f), shell, _body);
            _head = mb.Group("head", new Vector3(0, 0.35f, 0.55f), _body);
            mb.Box(new Vector3(0, -0.05f, -0.1f), new Vector3(0.18f, 0.18f, 0.5f), shell, 0.05f, _head, new Vector3(-30, 0, 0));
            mb.Sphere(new Vector3(0, 0.1f, 0.15f), new Vector3(0.3f, 0.25f, 0.35f), shell, _head);
            mb.Frustum(new Vector3(0, 0.08f, 0.35f), 0.1f, 0.05f, 0.2f, sac, _head, new Vector3(90, 0, 0), 8);
            for (int i = 0; i < 4; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                AddLeg(mb, _body, new Vector3(side * 0.3f, -0.1f, i < 2 ? 0.25f : -0.25f), 0.4f, 0.5f, 0.07f, shell, side * 50f, i * 1.6f, 30f, 60f);
            }
        }

        void BuildTank(ModelBuilder mb)
        {
            var plate = Mat("ch_tank", new Color(0.42f, 0.42f, 0.42f), 0.55f);
            var under = Mat("ch_tank_u", new Color(0.2f, 0.2f, 0.21f), 0.4f);
            var eye = Glow("eye_tank", new Color(1f, 0.55f, 0.1f));
            _body = mb.Group("body", new Vector3(0, 0.95f, 0));
            mb.Sphere(new Vector3(0, 0, -0.1f), new Vector3(1.4f, 0.9f, 1.6f), under, _body);
            for (int i = 0; i < 4; i++)
                mb.Box(new Vector3(0, 0.38f - Mathf.Abs(i - 1.5f) * 0.06f, -0.7f + i * 0.42f), new Vector3(1.35f - Mathf.Abs(i - 1.5f) * 0.12f, 0.35f, 0.5f), plate, 0.12f, _body, new Vector3(-8, 0, 0));
            mb.Box(new Vector3(-0.68f, 0.05f, -0.1f), new Vector3(0.2f, 0.6f, 1.4f), plate, 0.08f, _body);
            mb.Box(new Vector3(0.68f, 0.05f, -0.1f), new Vector3(0.2f, 0.6f, 1.4f), plate, 0.08f, _body);
            _head = mb.Group("head", new Vector3(0, 0.0f, 0.85f), _body);
            mb.Box(Vector3.zero, new Vector3(0.9f, 0.6f, 0.6f), plate, 0.15f, _head);
            mb.Box(new Vector3(0, -0.25f, 0.25f), new Vector3(0.7f, 0.2f, 0.4f), under, 0.06f, _head);
            mb.Sphere(new Vector3(-0.25f, 0.05f, 0.3f), new Vector3(0.12f, 0.07f, 0.05f), eye, _head);
            mb.Sphere(new Vector3(0.25f, 0.05f, 0.3f), new Vector3(0.12f, 0.07f, 0.05f), eye, _head);
            mb.Box(new Vector3(0, 0.38f, -0.1f), new Vector3(0.5f, 0.25f, 0.5f), plate, 0.08f, _head, new Vector3(20, 0, 0));
            for (int i = 0; i < 4; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float z = i < 2 ? 0.45f : -0.55f;
                AddLeg(mb, _body, new Vector3(side * 0.7f, -0.25f, z), 0.4f, 0.4f, 0.28f, plate, side * 15f, i * 1.57f, 18f, 25f);
            }
        }

        void BuildSynth(ModelBuilder mb)
        {
            var hull = Palette.HullDark;
            var eye = Glow("eye_synth", new Color(1f, 0.1f, 0.05f));
            _body = mb.Group("body", new Vector3(0, 0.9f, 0));
            mb.Box(Vector3.zero, new Vector3(0.8f, 0.45f, 1.0f), hull, 0.1f, _body);
            mb.Box(new Vector3(0, 0.3f, -0.1f), new Vector3(0.55f, 0.2f, 0.6f), Palette.Metal, 0.06f, _body);
            mb.Strip(new Vector3(0, 0.05f, 0.51f), new Vector3(0.6f, 0.05f, 0.03f), Palette.Red, _body);
            _head = mb.Group("head", new Vector3(0, 0.2f, 0.55f), _body);
            mb.Box(Vector3.zero, new Vector3(0.4f, 0.3f, 0.35f), hull, 0.06f, _head);
            mb.Sphere(new Vector3(0, 0.02f, 0.18f), new Vector3(0.18f, 0.12f, 0.06f), eye, _head);
            mb.Cyl(new Vector3(0.12f, 0.35f, -0.05f), 0.02f, 0.5f, Palette.Metal, _head, null, 6);
            for (int i = 0; i < 4; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                AddLeg(mb, _body, new Vector3(side * 0.42f, -0.1f, i < 2 ? 0.35f : -0.35f), 0.45f, 0.5f, 0.09f, Palette.Metal, side * 35f, i * 1.57f, 28f, 70f);
            }
        }

        void BuildGrazer(ModelBuilder mb)
        {
            var hide = Mat("hide_grazer", new Color(0.45f, 0.38f, 0.32f), 0.2f);
            var dark = Mat("hide_grazer_d", new Color(0.3f, 0.25f, 0.22f), 0.2f);
            _body = mb.Group("body", new Vector3(0, 0.85f, 0));
            mb.Sphere(Vector3.zero, new Vector3(0.9f, 0.75f, 1.5f), hide, _body);
            mb.Sphere(new Vector3(0, 0.25f, 0.2f), new Vector3(0.7f, 0.5f, 0.8f), hide, _body);
            _head = mb.Group("head", new Vector3(0, 0.0f, 0.85f), _body);
            mb.Sphere(new Vector3(0, -0.05f, 0.15f), new Vector3(0.45f, 0.42f, 0.6f), dark, _head);
            mb.Frustum(new Vector3(0, 0.2f, 0.45f), 0.08f, 0.01f, 0.4f, Mat("horn", new Color(0.85f, 0.8f, 0.7f)), _head, new Vector3(40, 0, 0), 6);
            mb.Box(new Vector3(0, 0.05f, -0.8f), new Vector3(0.05f, 0.05f, 0.4f), dark, 0f, _body, new Vector3(30, 0, 0));
            for (int i = 0; i < 4; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                AddLeg(mb, _body, new Vector3(side * 0.3f, -0.2f, i < 2 ? 0.45f : -0.45f), 0.35f, 0.35f, 0.17f, dark, 0f, i < 2 ? (i * Mathf.PI) : (i * Mathf.PI + Mathf.PI * 0.5f), 22f, 10f);
            }
        }

        void BuildStalker(ModelBuilder mb)
        {
            var hide = Mat("hide_stalker", new Color(0.62f, 0.5f, 0.32f), 0.2f);
            var stripe = Mat("hide_stalker_s", new Color(0.3f, 0.22f, 0.14f), 0.2f);
            _body = mb.Group("body", new Vector3(0, 0.75f, 0));
            mb.Sphere(Vector3.zero, new Vector3(0.45f, 0.45f, 1.2f), hide, _body);
            for (int i = 0; i < 3; i++) mb.Box(new Vector3(0, 0.2f, -0.3f + i * 0.3f), new Vector3(0.3f, 0.04f, 0.08f), stripe, 0f, _body);
            _head = mb.Group("head", new Vector3(0, 0.15f, 0.65f), _body);
            mb.Sphere(new Vector3(0, 0, 0.1f), new Vector3(0.32f, 0.3f, 0.45f), hide, _head);
            mb.Box(new Vector3(0, -0.06f, 0.35f), new Vector3(0.16f, 0.1f, 0.3f), stripe, 0.03f, _head);
            mb.Sphere(new Vector3(-0.1f, 0.08f, 0.25f), Vector3.one * 0.06f, Glow("eye_y", new Color(1f, 0.85f, 0.2f)), _head);
            mb.Sphere(new Vector3(0.1f, 0.08f, 0.25f), Vector3.one * 0.06f, Glow("eye_y", new Color(1f, 0.85f, 0.2f)), _head);
            mb.Box(new Vector3(0, 0.1f, -0.75f), new Vector3(0.05f, 0.05f, 0.5f), hide, 0f, _body, new Vector3(-20, 0, 0));
            for (int i = 0; i < 4; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                AddLeg(mb, _body, new Vector3(side * 0.18f, -0.15f, i < 2 ? 0.4f : -0.4f), 0.32f, 0.35f, 0.08f, stripe, 0f, i < 2 ? i * Mathf.PI : i * Mathf.PI + 1.2f, 40f, 30f);
            }
        }

        public void Lunge() => _lunge = 1f;

        public void Die() { if (_deadT < 0f) _deadT = 0f; }

        void LateUpdate()
        {
            if (_deadT >= 0f)
            {
                _deadT += Time.deltaTime;
                float k = Mathf.SmoothStep(0, 1, Mathf.Min(1f, _deadT * 2f));
                transform.localRotation = Quaternion.Euler(0, 0, 100f * k);
                transform.localPosition = new Vector3(0, 0.3f * k - Mathf.Max(0f, _deadT - 1.5f) * 0.4f, 0);
                return;
            }
            float k2 = Mathf.Clamp01(Speed / 3f);
            _phase += Time.deltaTime * (3f + Speed * 2.2f * _stride);
            foreach (var l in _legs)
            {
                float s = Mathf.Sin(_phase + l.Offset);
                l.Hip.localRotation = Quaternion.Euler(0, l.Splay, 0) * Quaternion.Euler(s * l.Amp * k2, 0, 0);
            }
            if (_body != null) _body.localPosition = _bodyBase + Vector3.up * Mathf.Abs(Mathf.Sin(_phase)) * 0.04f * k2;
            if (_head != null)
            {
                _lunge = Mathf.MoveTowards(_lunge, 0f, Time.deltaTime * 3f);
                _head.localPosition = _headBase + Vector3.forward * Mathf.Sin(_lunge * Mathf.PI) * 0.3f;
            }
        }
    }
}
