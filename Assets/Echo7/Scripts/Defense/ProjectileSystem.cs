using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    public enum ProjectileLook { Plasma, Bullet, Shell, Acid, Grenade }

    /// <summary>
    /// Все снаряды (плазма игрока, пули гарнизона, снаряды тяжёлых турелей, кислота плевунов, гранаты)
    /// обсчитываются одним менеджером: перемещение, гравитация, рейкаст между кадрами, взрывы по площади.
    /// </summary>
    public class ProjectileSystem : MonoBehaviour
    {
        class P
        {
            public Vector3 Pos, Vel;
            public float Gravity, Damage, Splash, Life;
            public DamageType Type;
            public Faction Faction;
            public int Mask;
            public ProjectileLook Look;
            public LineRenderer Line;
            public Transform Ball;
            public Vector3? Target;   // для гранат — точка подрыва
            public GameObject Owner;
            public bool Active;
        }

        readonly List<P> _list = new List<P>();
        Material _plasma, _bullet, _acid, _shell;

        public static ProjectileSystem Instance { get; private set; }

        public void Init()
        {
            Instance = this;
            _plasma = MaterialFactory.Unlit(new Color(0.45f, 0.9f, 1f, 1f));
            _bullet = MaterialFactory.Unlit(new Color(1f, 0.75f, 0.35f, 1f));
            _acid = MaterialFactory.Unlit(new Color(0.6f, 0.95f, 0.2f, 1f));
            _shell = MaterialFactory.Lit(new Color(0.25f, 0.25f, 0.27f), 0.6f, 0.4f);
        }

        P Get()
        {
            foreach (var p in _list) if (!p.Active) return p;
            var np = new P();
            var go = new GameObject("proj");
            go.transform.SetParent(transform, false);
            np.Line = go.AddComponent<LineRenderer>();
            np.Line.positionCount = 2;
            np.Line.useWorldSpace = true;
            np.Line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            np.Line.receiveShadows = false;
            np.Line.numCapVertices = 2;
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(ball.GetComponent<Collider>());
            ball.transform.SetParent(go.transform, false);
            ball.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            np.Ball = ball.transform;
            _list.Add(np);
            return np;
        }

        /// <summary>Мгновенный снаряд со скоростью speed. Для навесных — gravity > 0.</summary>
        public void Fire(Vector3 from, Vector3 velocity, float damage, DamageType type, Faction faction, ProjectileLook look,
            float splash = 0f, float gravity = 0f, GameObject owner = null, Vector3? detonateAt = null, float life = 3f)
        {
            var p = Get();
            p.Active = true;
            p.Pos = from; p.Vel = velocity; p.Damage = damage; p.Type = type; p.Faction = faction;
            p.Look = look; p.Splash = splash; p.Gravity = gravity; p.Owner = owner; p.Target = detonateAt; p.Life = life;
            // своих не задеваем: выстрелы игрока/гарнизона летят над своими стенами
            p.Mask = faction == Faction.Player
                ? Layers.GroundMask | Layers.EnemyMask
                : Layers.GroundMask | Layers.StructureMask | (1 << Layers.Player) | (1 << Layers.Npc);
            bool line = look == ProjectileLook.Plasma || look == ProjectileLook.Bullet;
            p.Line.enabled = line;
            p.Ball.gameObject.SetActive(!line);
            switch (look)
            {
                case ProjectileLook.Plasma: p.Line.sharedMaterial = _plasma; p.Line.widthMultiplier = 0.09f; break;
                case ProjectileLook.Bullet: p.Line.sharedMaterial = _bullet; p.Line.widthMultiplier = 0.05f; break;
                case ProjectileLook.Acid: p.Ball.GetComponent<MeshRenderer>().sharedMaterial = _acid; p.Ball.localScale = Vector3.one * 0.35f; break;
                case ProjectileLook.Shell: p.Ball.GetComponent<MeshRenderer>().sharedMaterial = _shell; p.Ball.localScale = Vector3.one * 0.3f; break;
                case ProjectileLook.Grenade: p.Ball.GetComponent<MeshRenderer>().sharedMaterial = Palette.HullDark; p.Ball.localScale = Vector3.one * 0.22f; break;
            }
            UpdateVisual(p);
        }

        /// <summary>Скорость для навесного броска в точку за время t.</summary>
        public static Vector3 BallisticVelocity(Vector3 from, Vector3 to, float time, float gravity)
        {
            var d = to - from;
            var v = d / time;
            v.y = (d.y + 0.5f * gravity * time * time) / time;
            return v;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || Game.Grid == null) return;
            foreach (var p in _list)
            {
                if (!p.Active) continue;
                p.Life -= dt;
                var prev = p.Pos;
                p.Vel += Vector3.down * p.Gravity * dt;
                var next = p.Pos + p.Vel * dt;
                var delta = next - prev;
                float len = delta.magnitude;
                bool done = false;
                if (p.Target.HasValue && p.Look == ProjectileLook.Grenade)
                {
                    // граната взрывается по таймеру/касанию земли
                    if (p.Life <= 0f || (next.y < Game.Grid.SampleHeight(next) + 0.15f && p.Vel.y < 0f))
                    {
                        next.y = Game.Grid.SampleHeight(next) + 0.15f;
                        Impact(p, next, null);
                        done = true;
                    }
                }
                else if (len > 0.0001f && Physics.SphereCast(prev, p.Look == ProjectileLook.Acid ? 0.2f : 0.08f, delta / len, out var hit, len, p.Mask, QueryTriggerInteraction.Collide))
                {
                    var h = hit.collider.GetComponentInParent<Health>();
                    if (h != null && h.Faction == p.Faction) { /* свой — пролетаем */ }
                    else
                    {
                        Impact(p, hit.point, h);
                        done = true;
                    }
                }
                if (!done)
                {
                    p.Pos = next;
                    if (p.Life <= 0f) { if (p.Splash > 0f) Impact(p, p.Pos, null); else p.Active = false; }
                }
                UpdateVisual(p);
            }
        }

        void UpdateVisual(P p)
        {
            if (!p.Active)
            {
                p.Line.enabled = false;
                p.Ball.gameObject.SetActive(false);
                return;
            }
            if (p.Line.enabled)
            {
                var dir = p.Vel.normalized;
                p.Line.SetPosition(0, p.Pos - dir * (p.Look == ProjectileLook.Plasma ? 1.4f : 0.9f));
                p.Line.SetPosition(1, p.Pos);
            }
            else p.Ball.position = p.Pos;
        }

        void Impact(P p, Vector3 point, Health direct)
        {
            p.Active = false;
            if (p.Splash > 0f)
            {
                Explode(point, p.Splash, p.Damage, p.Type, p.Faction, p.Owner);
            }
            else if (direct != null)
            {
                direct.TakeDamage(new DamageInfo(p.Damage, p.Type, point, p.Faction, p.Owner));
                HitFx(point, direct);
            }
            else
            {
                Game.Fx.Emit(FxKind.Dust, point, 2, Vector3.up, 0.5f, null, 0.4f);
            }
            if (p.Look == ProjectileLook.Acid) Game.Fx.Emit(FxKind.Ichor, point, 8, Vector3.up, 1f, new Color(0.6f, 0.95f, 0.2f));
        }

        public static void HitFx(Vector3 point, Health h)
        {
            if (h.Armor == ArmorClass.Structure || h.Armor == ArmorClass.Synthetic || h.Armor == ArmorClass.Heavy)
            {
                Game.Fx.Emit(FxKind.Sparks, point, 4, Vector3.up, 1f);
                Game.Sound.PlayAt(Sfx.HitMetal, point, 0.35f, 0.15f, 0.05f);
            }
            else
            {
                bool creature = h.Faction == Faction.Hostile || h.Faction == Faction.Neutral;
                Game.Fx.Emit(creature ? FxKind.Ichor : FxKind.Blood, point, 4, Vector3.up, 1f);
                Game.Sound.PlayAt(Sfx.HitFlesh, point, 0.4f, 0.2f, 0.05f);
            }
        }

        /// <summary>Взрыв по площади: урон с затуханием, эффекты, шум.</summary>
        public static void Explode(Vector3 point, float radius, float damage, DamageType type, Faction source, GameObject owner = null, bool noise = true)
        {
            Game.Fx.Explosion(point, radius);
            Game.Sound.PlayAt(radius > 4f ? Sfx.Explosion : Sfx.Grenade, point, 0.9f);
            if (noise) Game.Noise?.Emit(point, radius * 3f, NoiseSource.Explosion);
            if (Game.CameraRig != null && Game.Player != null && Vector3.Distance(point, Game.Player.transform.position) < 30f)
                Game.CameraRig.Shake(0.25f * radius / 4f, 0.3f);
            float r2 = radius * radius;
            // существа
            for (int i = Registry.Enemies.Count - 1; i >= 0; i--)
            {
                if (i >= Registry.Enemies.Count) continue;
                var e = Registry.Enemies[i];
                if (e == null || e.IsDead) continue;
                if (source == Faction.Hostile && e.IsHostile) continue;
                float d2 = (e.transform.position - point).sqrMagnitude;
                if (d2 > r2) continue;
                float k = 1f - Mathf.Sqrt(d2) / radius * 0.6f;
                e.Health.TakeDamage(new DamageInfo(damage * k, type, e.transform.position, source, owner));
            }
            if (source == Faction.Player) return;
            // враждебные взрывы задевают своих
            for (int i = Registry.Structures.Count - 1; i >= 0; i--)
            {
                if (i >= Registry.Structures.Count) continue;
                var s = Registry.Structures[i];
                if (s == null || s.IsDestroyed) continue;
                float d = s.DistanceTo(point);
                if (d > radius) continue;
                s.Health.TakeDamage(new DamageInfo(damage * (1f - d / radius * 0.6f), type, point, source, owner));
            }
            var pl = Game.Player;
            if (pl != null && (pl.transform.position - point).sqrMagnitude < r2)
                pl.Health.TakeDamage(new DamageInfo(damage * 0.6f, type, point, source, owner));
            for (int i = Registry.Npcs.Count - 1; i >= 0; i--)
            {
                if (i >= Registry.Npcs.Count) continue;
                var n = Registry.Npcs[i];
                if (n != null && !n.Health.IsDead && (n.transform.position - point).sqrMagnitude < r2)
                    n.Health.TakeDamage(new DamageInfo(damage * 0.6f, type, point, source, owner));
            }
        }
    }
}
