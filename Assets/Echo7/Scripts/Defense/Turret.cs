using UnityEngine;

namespace Echo7
{
    /// <summary>Параметры турели по типу (баланс — в ассете TurretBalance).</summary>
    public class TurretProfile
    {
        public float Range = 22f;
        public float FireInterval = 0.12f;
        public float Damage = 7f;
        public DamageType Type = DamageType.Kinetic;
        public float Accuracy = 0.75f;
        public float AmmoPerShot = 0.5f;
        public float EnergyFiring;       // доп. потребление при стрельбе (лазер)
        public float Splash;
        public float TurnSpeed = 240f;
        public bool Beam;
        public bool Ballistic;
        public float Noise = 0.35f;
        public float WearPerShot = 0.0012f;
        public Sfx Sound = Sfx.MachineGun;
        public Color Tracer = new Color(1f, 0.75f, 0.35f, 1f);
        public bool PreferHeavy;

        /// <summary>Типы построек-турелей, у которых есть профиль.</summary>
        public static readonly StructureKind[] TurretKinds = { StructureKind.MgTurret, StructureKind.LaserTurret, StructureKind.HeavyTurret };

        static System.Collections.Generic.Dictionary<StructureKind, TurretProfile> _map;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _map = null;

        /// <summary>
        /// Общий профиль для всех турелей этого типа. Значения — из ассета <see cref="TurretBalance"/>, без него — из кода.
        /// Объект общий и не пересоздаётся, поэтому правка ассета во время Play сразу меняет стрельбу всех турелей.
        /// </summary>
        public static TurretProfile For(StructureKind k)
        {
            if (_map == null)
            {
                _map = new System.Collections.Generic.Dictionary<StructureKind, TurretProfile>();
                foreach (var t in TurretKinds) _map[t] = CodeDefault(t);
                var asset = BalanceData.Load<TurretBalance>(TurretBalance.File);
                if (asset != null) Apply(asset);
            }
            if (!_map.TryGetValue(k, out var p)) { p = CodeDefault(k); _map[k] = p; }
            return p;
        }

        public static void Apply(TurretBalance asset)
        {
            if (_map == null || asset == null) return;
            foreach (var e in asset.turrets)
            {
                if (!_map.TryGetValue(e.kind, out var p)) { p = CodeDefault(e.kind); _map[e.kind] = p; }
                e.ApplyTo(p);
            }
        }

        /// <summary>Значения из кода.</summary>
        public static TurretProfile CodeDefault(StructureKind k)
        {
            switch (k)
            {
                case StructureKind.LaserTurret:
                    return new TurretProfile { Range = 24f, FireInterval = 0.1f, Damage = 4.2f, Type = DamageType.Laser, Accuracy = 0.97f, AmmoPerShot = 0f,
                        EnergyFiring = 14f, Beam = true, TurnSpeed = 200f, Noise = 0.15f, WearPerShot = 0.0006f, Sound = Sfx.Laser, Tracer = new Color(0.4f, 0.85f, 1f, 1f), PreferHeavy = true };
                case StructureKind.HeavyTurret:
                    return new TurretProfile { Range = 32f, FireInterval = 2.4f, Damage = 130f, Type = DamageType.Explosive, Accuracy = 0.8f, AmmoPerShot = 2f,
                        Splash = 3.8f, Ballistic = true, TurnSpeed = 70f, Noise = 4f, WearPerShot = 0.01f, Sound = Sfx.Cannon, PreferHeavy = true };
                default:
                    return new TurretProfile();
            }
        }
    }

    /// <summary>
    /// Автобашня. С расчётом (NPC гарнизона) стреляет лучше и точнее (точность зависит от морали расчёта);
    /// без расчёта — автономный режим со сниженной эффективностью. Изнашивается и клинит, особенно в пекло.
    /// </summary>
    public class Turret : Structure
    {
        public TurretProfile Profile { get; private set; }
        public GarrisonNpc Crew;
        public bool LostCrew;          // расчёт погиб — турель «осиротела»
        public float Wear;             // 0..1
        public Enemy Target { get; private set; }
        public bool Firing { get; private set; }
        public int Kills;

        Transform _head;
        Transform[] _muzzles;
        int _muzzleIdx;
        float _cooldown, _scanTimer, _jamUntil, _beamDamageAcc;
        static bool _noAmmoWarned;

        public bool Jammed => Time.time < _jamUntil;
        public bool Crewed => Crew != null && !Crew.Health.IsDead && Crew.IsManning(this);

        public float RangeEffective
        {
            get
            {
                float r = Profile.Range;
                if (Game.Skills != null) r *= Game.Skills.TurretRangeMult;
                if (Game.Defense != null) r *= Game.Defense.SensorBonus(this);
                if (Game.Weather != null) r *= Game.Weather.VisibilityFactor;
                if (!Crewed) r *= 0.9f;
                return r;
            }
        }

        /// <summary>Множитель точности: мораль расчёта ↔ точность (одна из «фронтирных» механик).</summary>
        public float AccuracyMult => Crewed ? 0.75f + 0.5f * Crew.Morale / 100f : 0.8f;
        public float FireRateMult => (Crewed ? 1.15f : 0.75f) * (Game.Skills != null ? 1f : 1f);

        public override float CurrentEnergyUse => IsBuilt ? Def.EnergyUse + (Firing ? Profile.EnergyFiring : 0f) : 0f;
        public override bool NeedsService => base.NeedsService || Wear > 0.25f;

        protected override void OnCreated()
        {
            Profile = TurretProfile.For(Def.Kind);
            Registry.Turrets.Add(this);
        }

        protected override void OnBuilt()
        {
            _head = Model.Find("#head");
            if (_head != null)
            {
                var list = new System.Collections.Generic.List<Transform>();
                for (int i = 0; i < 4; i++) { var m = _head.Find("muzzle" + i); if (m != null) list.Add(m); }
                _muzzles = list.ToArray();
            }
        }

        protected override void OnRemoved()
        {
            Registry.Turrets.Remove(this);
            if (Crew != null) Crew.Unassign();
        }

        public override void Service()
        {
            base.Service();
            if (Wear > 0.01f) { Wear = 0f; _jamUntil = 0f; }
        }

        public override string StatusText()
        {
            var b = base.StatusText();
            if (!IsBuilt) return b;
            string crew = Crewed ? $"Расчёт: {Crew.DisplayName} (мораль {Mathf.RoundToInt(Crew.Morale)})" : LostCrew ? "Расчёт погиб — автономный режим" : "Автономный режим";
            return $"{b}\n{crew}\nИзнос: {Mathf.RoundToInt(Wear * 100)}%{(Jammed ? " — ЗАКЛИНИЛО" : "")}  ·  Убито: {Kills}";
        }

        protected override void Update()
        {
            base.Update();
            Firing = false;
            if (!IsBuilt || !Game.IsPlaying || _head == null) return;
            if (!IsOperational || Jammed)
            {
                // без энергии/заклинило — голова опускается
                _head.localRotation = Quaternion.Slerp(_head.localRotation, Quaternion.Euler(12f, _head.localEulerAngles.y, 0), Time.deltaTime * 2f);
                return;
            }

            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f || Target == null || Target.IsDead)
            {
                _scanTimer = 0.3f;
                Target = Game.Defense != null ? Game.Defense.PickTarget(this) : null;
            }
            if (Target == null || Target.IsDead)
            {
                _head.Rotate(0, 15f * Time.deltaTime, 0, Space.World); // патрулирование
                return;
            }
            float range = RangeEffective;
            var aimPoint = Target.AimPoint;
            if ((aimPoint - transform.position).sqrMagnitude > range * range * 1.1f) { Target = null; return; }

            var dir = aimPoint - _head.position;
            dir.y = Profile.Ballistic ? 0f : dir.y;
            if (dir.sqrMagnitude < 0.01f) return;
            var want = Quaternion.LookRotation(dir.normalized, Vector3.up);
            _head.rotation = Quaternion.RotateTowards(_head.rotation, want, Profile.TurnSpeed * Time.deltaTime);
            float angle = Quaternion.Angle(_head.rotation, want);

            if (Profile.Beam)
            {
                if (angle < 6f) FireBeam(aimPoint);
                return;
            }

            _cooldown -= Time.deltaTime * FireRateMult * OperationalFactor;
            if (_cooldown > 0f || angle > 8f) return;
            _cooldown = Profile.FireInterval;
            Shoot(aimPoint);
        }

        Vector3 Muzzle()
        {
            if (_muzzles == null || _muzzles.Length == 0) return _head.position + _head.forward;
            _muzzleIdx = (_muzzleIdx + 1) % _muzzles.Length;
            return _muzzles[_muzzleIdx].position;
        }

        bool UseAmmo()
        {
            if (Profile.AmmoPerShot <= 0f) return true;
            if (Game.Bank.Spend(ResourceType.Ammo, Profile.AmmoPerShot)) { _noAmmoWarned = false; return true; }
            if (!_noAmmoWarned) { _noAmmoWarned = true; Game.Log?.Alert("Турели молчат: закончились патроны!"); }
            return false;
        }

        void AddWear()
        {
            Wear = Mathf.Clamp01(Wear + Profile.WearPerShot * (Game.Weather != null ? Game.Weather.WearFactor : 1f));
            if (Wear > 0.6f && Random.value < (Wear - 0.6f) * 0.04f)
            {
                _jamUntil = Time.time + 4f;
                Game.Fx.Emit(FxKind.Smoke, _head.position + Vector3.up * 0.3f, 6, Vector3.up, 0.5f, null, 0.4f);
                if (Random.value < 0.3f) Game.Log?.Warn($"{Def.Name}: заклинило от износа! Нужно обслуживание.");
            }
        }

        void Shoot(Vector3 aim)
        {
            if (!UseAmmo()) return;
            var m = Muzzle();
            float acc = Profile.Accuracy * AccuracyMult;
            float dmg = Profile.Damage * (Game.Skills != null ? Game.Skills.TurretDamageMult : 1f);
            Game.Fx.Emit(FxKind.Muzzle, m, 2);
            Game.Sound.PlayAt(Profile.Sound, m, Profile.Ballistic ? 0.9f : 0.45f, 0.1f, Profile.Ballistic ? 0.05f : 0.05f);
            Game.Noise?.Emit(transform.position, Profile.Noise, NoiseSource.Turret);
            AddWear();

            if (Profile.Ballistic)
            {
                var target = Target.PredictPosition(1.0f);
                if (Random.value > acc) target += new Vector3(Random.Range(-3f, 3f), 0, Random.Range(-3f, 3f));
                float g = 18f;
                float t = Mathf.Clamp(Vector3.Distance(m, target) / 28f, 0.5f, 1.6f);
                ProjectileSystem.Instance.Fire(m, ProjectileSystem.BallisticVelocity(m, target, t, g), dmg, Profile.Type, Faction.Player, ProjectileLook.Shell, Profile.Splash, g, gameObject, null, t + 0.5f);
                Game.Fx.Emit(FxKind.Smoke, m, 5, _head.forward, 0.4f, null, 0.6f);
                return;
            }

            // хитскан с трассером
            Vector3 hitPoint;
            if (Random.value <= acc)
            {
                hitPoint = aim + Random.insideUnitSphere * 0.2f;
                float dealt = Target.Health.TakeDamage(new DamageInfo(dmg, Profile.Type, hitPoint, Faction.Player, gameObject));
                if (Target != null && Target.IsDead && dealt > 0f) Kills++;
                if (Random.value < 0.4f) ProjectileSystem.HitFx(hitPoint, Target.Health);
            }
            else
            {
                hitPoint = aim + new Vector3(Random.Range(-2f, 2f), Random.Range(-0.5f, 1f), Random.Range(-2f, 2f));
            }
            Game.Fx.Line(m, hitPoint, Profile.Tracer, 0.05f, 0.06f);
        }

        void FireBeam(Vector3 aim)
        {
            if (Efficiency < 0.35f) return; // лазеру нужна энергия
            Firing = true;
            var m = _muzzles != null && _muzzles.Length > 0 ? _muzzles[0].position : _head.position;
            float dmgPerSec = Profile.Damage / Profile.FireInterval * (Game.Skills != null ? Game.Skills.TurretDamageMult : 1f) * Efficiency;
            _beamDamageAcc += Time.deltaTime;
            float acc = Mathf.Clamp01(Profile.Accuracy * AccuracyMult);
            Game.Fx.Line(m, aim, Profile.Tracer * new Color(1, 1, 1, 0.9f), 0.12f + Mathf.Sin(Time.time * 40f) * 0.03f, 0.05f);
            if (_beamDamageAcc >= 0.1f)
            {
                float dealt = Target.Health.TakeDamage(new DamageInfo(dmgPerSec * _beamDamageAcc * acc, Profile.Type, aim, Faction.Player, gameObject));
                if (Target != null && Target.IsDead && dealt > 0f) Kills++;
                _beamDamageAcc = 0f;
                Game.Fx.Emit(FxKind.Sparks, aim, 1, Vector3.up, 1f, new Color(0.6f, 0.9f, 1f));
                Game.Noise?.Emit(transform.position, Profile.Noise, NoiseSource.Turret);
                AddWear();
            }
            Game.Sound.PlayAt(Sfx.Laser, m, 0.25f, 0.05f, 0.3f);
        }
    }

    /// <summary>Мина: срабатывает от врага рядом, после чего её нужно перезарядить.</summary>
    public class Mine : Structure
    {
        public bool Armed { get; private set; } = true;
        Transform _lamp;
        const float TriggerRadius = 1.9f;

        public override bool NeedsService => base.NeedsService || !Armed;
        // по мине можно пройти: враги наступают на неё, а не пытаются её «прогрызть»
        public override bool PassableForEnemies => true;
        public override bool PassableForFriendly => true;

        protected override void OnCreated()
        {
            if (Collider != null) Collider.isTrigger = true;
        }

        protected override void OnBuilt()
        {
            _lamp = Model.Find("#lamp");
            Armed = true;
        }

        public override void Service()
        {
            base.Service();
            if (Armed) return;
            var cost = new Cost((ResourceType.Alloys, 2), (ResourceType.Ammo, 2));
            if (!Game.Bank.Spend(cost)) return;
            Armed = true;
            if (_lamp != null) _lamp.gameObject.SetActive(true);
        }

        public void Disarm()
        {
            Armed = false;
            if (_lamp != null) _lamp.gameObject.SetActive(false);
        }

        public override string StatusText() => !IsBuilt ? base.StatusText() : Armed ? "Взведена" : "Израсходована — нужна перезарядка (2 сплава, 2 патрона)";

        protected override void Update()
        {
            base.Update();
            if (!IsBuilt || !Armed || !Game.IsPlaying) return;
            if (_lamp != null) _lamp.gameObject.SetActive(Mathf.Repeat(Time.time, 1.2f) < 0.2f);
            var e = Registry.NearestEnemy(transform.position, TriggerRadius);
            if (e == null || e.IsJumping) return;
            Armed = false;
            if (_lamp != null) _lamp.gameObject.SetActive(false);
            float dmg = 150f * (Game.Skills != null ? Game.Skills.TurretDamageMult : 1f);
            ProjectileSystem.Explode(transform.position + Vector3.up * 0.3f, 4.2f, dmg, DamageType.Explosive, Faction.Player, gameObject);
        }
    }
}
