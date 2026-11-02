using UnityEngine;

namespace Echo7
{
    /// <summary>Способность на панели быстрого доступа (1–4).</summary>
    public abstract class Ability
    {
        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract float Cooldown { get; }
        public virtual Cost Cost => null;
        public float CooldownLeft { get; protected set; }
        public float CooldownFraction => Cooldown > 0f ? Mathf.Clamp01(CooldownLeft / Cooldown) : 0f;

        public void Tick(float dt) { if (CooldownLeft > 0f) CooldownLeft -= dt; }

        /// <summary>null — успех, иначе причина отказа.</summary>
        public string TryUse(PlayerController p, Vector3 target)
        {
            if (CooldownLeft > 0f) return $"{Name}: перезарядка {CooldownLeft:0.0} с";
            if (Cost != null && !Game.Bank.CanAfford(Cost)) return $"{Name}: {Game.Bank.MissingText(Cost)}";
            string why = Execute(p, target);
            if (why != null) return why;
            if (Cost != null) Game.Bank.Spend(Cost);
            CooldownLeft = Cooldown;
            return null;
        }

        protected abstract string Execute(PlayerController p, Vector3 target);
    }

    public class GrenadeAbility : Ability
    {
        public override string Name => "Граната";
        public override string Description => "Взрыв по площади (5 м). Громко!";
        public override float Cooldown => 6f;
        public override Cost Cost => new Cost((ResourceType.Ammo, 4), (ResourceType.Alloys, 1));

        protected override string Execute(PlayerController p, Vector3 target)
        {
            var from = p.transform.position + Vector3.up * 1.6f;
            var flat = target - p.transform.position; flat.y = 0;
            if (flat.magnitude > 18f) target = p.transform.position + flat.normalized * 18f;
            target.y = Game.Grid.SampleHeight(target);
            float t = Mathf.Clamp(flat.magnitude / 14f, 0.45f, 1.3f);
            ProjectileSystem.Instance.Fire(from, ProjectileSystem.BallisticVelocity(from, target, t, 16f), 90f, DamageType.Explosive, Faction.Player,
                ProjectileLook.Grenade, 5f, 16f, p.gameObject, target, t + 0.3f);
            Game.Noise?.Emit(p.transform.position, 6f, NoiseSource.Player);
            return null;
        }
    }

    public class CombatDroneAbility : Ability
    {
        public override string Name => "Боевой дрон";
        public override string Description => "Дрон охраны 30 с стреляет по ближайшим врагам. Навык «ИИ дронов» усиливает.";
        public override float Cooldown => 40f;
        public override Cost Cost => new Cost((ResourceType.Ammo, 10));

        protected override string Execute(PlayerController p, Vector3 target)
        {
            CombatDrone.Spawn(p.transform, 30f * (Game.Skills != null ? Game.Skills.DroneMult : 1f));
            return null;
        }
    }

    public class MedkitAbility : Ability
    {
        public override string Name => "Аптечка";
        public override string Description => "+70 здоровья и снимает лихорадку.";
        public override float Cooldown => 8f;
        public override Cost Cost => new Cost((ResourceType.Medkits, 1));

        protected override string Execute(PlayerController p, Vector3 target)
        {
            if (p.Health.Fraction > 0.98f && p.Sickness <= 0f) return "Вы здоровы";
            p.Health.Heal(70f * (Game.Skills != null ? Game.Skills.HealMult : 1f));
            p.Sickness = 0f;
            Game.Fx.Emit(FxKind.Heal, p.transform.position + Vector3.up, 14, Vector3.up);
            Game.Sound.PlayAt(Sfx.Heal, p.transform.position, 0.6f);
            return null;
        }
    }

    public class ChargeAbility : Ability
    {
        public override string Name => "Подрывной заряд";
        public override string Description => "Открывает заваленные пещеры, сносит всё рядом. Очень громко.";
        public override float Cooldown => 20f;
        public override Cost Cost => new Cost((ResourceType.Ammo, 6), (ResourceType.Alloys, 4));

        protected override string Execute(PlayerController p, Vector3 target)
        {
            // у пещеры — подрыв завала
            foreach (var cave in Game.World.Caves)
            {
                if (cave == null || cave.Opened) continue;
                if (Vector3.Distance(p.transform.position, cave.InteractPoint) < 7f)
                {
                    PlacedCharge.Spawn(cave.InteractPoint, 3f, cave);
                    Game.Log?.Warn("Заряд заложен! 3 секунды — отойдите.");
                    return null;
                }
            }
            var pos = p.transform.position + p.transform.forward * 1.5f;
            pos.y = Game.Grid.SampleHeight(pos);
            PlacedCharge.Spawn(pos, 3f, null);
            Game.Log?.Warn("Заряд заложен! 3 секунды.");
            return null;
        }
    }

    /// <summary>Заложенный заряд с таймером.</summary>
    public class PlacedCharge : MonoBehaviour
    {
        float _t;
        CaveEntrance _cave;
        Transform _lamp;

        public static void Spawn(Vector3 pos, float fuse, CaveEntrance cave)
        {
            var go = new GameObject("Charge");
            go.transform.SetParent(Game.Root, false);
            go.transform.position = pos;
            var c = go.AddComponent<PlacedCharge>();
            c._t = fuse;
            c._cave = cave;
            var mb = new ModelBuilder(go.transform, "m");
            mb.Box(new Vector3(0, 0.15f, 0), new Vector3(0.5f, 0.3f, 0.35f), Palette.Hazard, 0.04f);
            c._lamp = mb.Sphere(new Vector3(0, 0.35f, 0), Vector3.one * 0.12f, Palette.Red);
        }

        void Update()
        {
            _t -= Time.deltaTime;
            if (_lamp != null) _lamp.gameObject.SetActive(Mathf.Repeat(_t * (_t < 1f ? 8f : 3f), 1f) < 0.5f);
            if (_t > 0f) return;
            if (_cave != null) _cave.Blast();
            ProjectileSystem.Explode(transform.position + Vector3.up * 0.3f, 6f, 220f, DamageType.Explosive, Faction.Player, null, false);
            Game.Noise?.Emit(transform.position, _cave != null ? 10f : 40f, NoiseSource.Explosion);
            Destroy(gameObject);
        }
    }

    /// <summary>Временный боевой дрон игрока.</summary>
    public class CombatDrone : MonoBehaviour
    {
        Transform _owner;
        float _life, _cd;
        System.Collections.Generic.List<Transform> _rotors;
        Enemy _target;

        public static void Spawn(Transform owner, float life)
        {
            var go = new GameObject("CombatDrone");
            go.transform.SetParent(Game.Root, false);
            go.transform.position = owner.position + Vector3.up * 3f;
            var d = go.AddComponent<CombatDrone>();
            d._owner = owner;
            d._life = life;
            d._rotors = DroneVisual.Build(go.transform, Palette.Red, 0.8f);
        }

        void Update()
        {
            _life -= Time.deltaTime;
            if (_life <= 0f || _owner == null) { Game.Fx.Emit(FxKind.Smoke, transform.position, 6); Destroy(gameObject); return; }
            foreach (var r in _rotors) r.Rotate(0, 2000f * Time.deltaTime, 0, Space.Self);
            float a = Time.time * 1.2f;
            var want = _owner.position + new Vector3(Mathf.Cos(a) * 2.5f, 3.2f, Mathf.Sin(a) * 2.5f);
            transform.position = Vector3.Lerp(transform.position, want, Time.deltaTime * 3f);
            _cd -= Time.deltaTime;
            if (_target == null || _target.IsDead || (_target.transform.position - transform.position).sqrMagnitude > 400f)
                _target = Registry.NearestEnemy(transform.position, 18f, false);
            if (_target == null) return;
            var d = _target.AimPoint - transform.position;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(new Vector3(d.x, 0, d.z)), Time.deltaTime * 6f);
            if (_cd > 0f) return;
            _cd = 0.25f;
            float dmg = 9f * (Game.Skills != null ? Game.Skills.DroneMult : 1f);
            _target.Health.TakeDamage(new DamageInfo(dmg, DamageType.Laser, _target.AimPoint, Faction.Player, gameObject));
            Game.Fx.Line(transform.position, _target.AimPoint, new Color(1f, 0.4f, 0.3f, 1f), 0.05f, 0.06f);
            Game.Sound.PlayAt(Sfx.Laser, transform.position, 0.2f, 0.1f, 0.1f);
        }
    }
}
