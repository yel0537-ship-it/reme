using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    /// <summary>
    /// Игрок (младший офицер аванпоста). Управление как в Diablo 4: ЛКМ — идти/атаковать/взаимодействовать
    /// (удержание — непрерывное движение за курсором), WASD — прямое управление, ПКМ — стрельба в точку,
    /// Пробел — перекат, 1–4 — способности, E — взаимодействие с ближайшим объектом.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public Health Health { get; private set; }
        public HumanoidRig Rig { get; private set; }
        public float Sickness;            // лихорадка от насекомых
        public float MoveSpeed = 6f;
        public IInteractable Hovered { get; private set; }
        public IInteractable Focus { get; private set; }
        public string HoverLabel { get; private set; }
        public Enemy AttackTarget { get; private set; }
        public bool IsMining => _mining != null;
        public float MiningProgress => _miningT;
        public float RespawnTimer { get; private set; }

        public readonly Ability[] Abilities = new Ability[4];

        CharacterController _cc;
        readonly List<Vector3> _path = new List<Vector3>();
        int _pathIdx;
        GridPathfinder _pf;
        Vector3 _vel;
        float _fireCd, _rollCd, _rollT = -1f, _ammoDebt, _repathTimer, _stepTimer, _helpTimer;
        Vector3 _rollDir;
        ResourceDeposit _mining;
        float _miningT;
        bool _dead;
        Vector3? _aimPoint;

        public bool IsDead => _dead;

        public void Init()
        {
            _cc = GetComponent<CharacterController>();
            _cc.height = 1.8f;
            _cc.radius = 0.38f;
            _cc.center = new Vector3(0, 0.9f, 0);
            _cc.slopeLimit = 50f;
            _cc.stepOffset = 0.45f;
            gameObject.layer = Layers.Player;
            Physics.IgnoreLayerCollision(Layers.Player, Layers.Rail, false);
            Health = gameObject.AddComponent<Health>();
            Health.Init(160f, ArmorClass.Medium, Faction.Player, 2f);
            Health.Died += _ => Die();
            Health.Damaged += (h, d, a) => { if (a > 3f) Game.CameraRig?.Shake(0.12f, 0.15f); };
            Rig = HumanoidRig.Build(transform, HumanoidStyle.Soldier);
            _pf = new GridPathfinder(Game.Grid);
            Abilities[0] = new GrenadeAbility();
            Abilities[1] = new CombatDroneAbility();
            Abilities[2] = new MedkitAbility();
            Abilities[3] = new ChargeAbility();
            ApplySkillBonuses();
            GameEvents.RankChanged += (a, b) => ApplySkillBonuses();
        }

        public void ApplySkillBonuses()
        {
            float max = 160f + (Game.Skills != null ? Game.Skills.PlayerHpBonus : 0f);
            Health.SetMax(max);
        }

        // ------------------------------------------------------------ цикл

        void Update()
        {
            if (!Game.IsPlaying) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (_dead) { UpdateDead(dt); return; }

            foreach (var a in Abilities) a?.Tick(dt);
            UpdateSickness(dt);

            bool uiBlock = InputBridge.PointerOverUi || (Game.Ui != null && Game.Ui.ModalOpen);
            bool building = Game.Build != null && Game.Build.IsPlacing;
            UpdateHover(uiBlock || building);

            if (!uiBlock && !building) HandleMouse();
            if (!(Game.Ui != null && Game.Ui.ModalOpen)) HandleKeys();

            Move(dt);
            Combat(dt);
            Mining(dt);
            HelpConstruction(dt);
            // пассивная регенерация на базе
            if (Time.time - Health.LastDamageTime > 6f && Sickness < 0.3f)
                Health.Heal(dt * (InBase ? 2.5f : 0.6f));
        }

        public bool InBase => Game.Reactor != null && (transform.position - Game.Reactor.transform.position).sqrMagnitude < Game.Rank.BuildRadius * Game.Rank.BuildRadius;

        void UpdateHover(bool blocked)
        {
            Hovered = null;
            HoverLabel = null;
            if (blocked) return;
            var ray = Game.CameraRig.MouseRay();
            if (Physics.Raycast(ray, out var hit, 600f, Layers.PickMask, QueryTriggerInteraction.Collide))
            {
                var e = hit.collider.GetComponentInParent<Enemy>();
                if (e != null && !e.IsDead) { HoverLabel = e.DisplayName; return; }
                var ia = hit.collider.GetComponentInParent<IInteractable>();
                if (ia != null && ia.CanInteract) { Hovered = ia; HoverLabel = ia.InteractLabel; }
            }
        }

        void HandleMouse()
        {
            if (InputBridge.MouseDown(0) || InputBridge.MouseHeld(0))
            {
                bool fresh = InputBridge.MouseDown(0);
                var ray = Game.CameraRig.MouseRay();
                if (Physics.Raycast(ray, out var hit, 600f, Layers.PickMask, QueryTriggerInteraction.Collide))
                {
                    var e = hit.collider.GetComponentInParent<Enemy>();
                    if (e != null && !e.IsDead)
                    {
                        AttackTarget = e;
                        Focus = null;
                        StopMining();
                        _path.Clear();
                        return;
                    }
                    if (fresh)
                    {
                        var ia = hit.collider.GetComponentInParent<IInteractable>();
                        if (ia != null && ia.CanInteract)
                        {
                            Focus = ia;
                            AttackTarget = null;
                            StopMining();
                            SetDestination(ia.InteractPoint);
                            return;
                        }
                    }
                }
                if (Game.CameraRig.MouseOnWalkable(out var ground))
                {
                    if (fresh) { Focus = null; AttackTarget = null; StopMining(); }
                    if (fresh || _repathTimer <= 0f)
                    {
                        _repathTimer = 0.25f;
                        SetDestination(ground);
                    }
                }
            }
            _repathTimer -= Time.deltaTime;

            // ПКМ — стрельба в точку курсора (стоя на месте, как основная атака в D4)
            _aimPoint = null;
            if (InputBridge.MouseHeld(1) && Game.CameraRig.MouseOnGround(out var aim))
            {
                _aimPoint = aim + Vector3.up * 1.2f;
                _path.Clear();
                AttackTarget = null;
                StopMining();
            }
        }

        void HandleKeys()
        {
            if (InputBridge.KeyDown(GameKey.Space)) TryRoll();
            if (InputBridge.KeyDown(GameKey.E)) InteractNearest();
            for (int i = 0; i < 4; i++)
            {
                if (InputBridge.KeyDown(GameKey.Alpha1 + i)) UseAbility(i);
            }
        }

        public void UseAbility(int i)
        {
            var a = Abilities[i];
            if (a == null) return;
            Vector3 target = transform.position + transform.forward * 6f;
            if (Game.CameraRig.MouseOnGround(out var g)) target = g;
            string why = a.TryUse(this, target);
            if (why != null) { Game.Log?.Warn(why); Game.Sound?.Play2D(Sfx.UiError, 0.5f); }
        }

        void InteractNearest()
        {
            IInteractable best = null; float bd = 4.5f;
            foreach (var c in Physics.OverlapSphere(transform.position, 5f, Layers.PickMask & ~Layers.GroundMask, QueryTriggerInteraction.Collide))
            {
                var ia = c.GetComponentInParent<IInteractable>();
                if (ia == null || !ia.CanInteract) continue;
                float d = Vector3.Distance(transform.position, ia.InteractPoint);
                if (d < bd) { bd = d; best = ia; }
            }
            if (best != null) { Focus = best; SetDestination(best.InteractPoint); }
        }

        void SetDestination(Vector3 p)
        {
            _path.Clear();
            _pathIdx = 0;
            // прямая видимость на том же ярусе — без A* (между ярусами — только по лестнице)
            bool sameLevel = Game.Grid.LevelAt(transform.position) == Game.Grid.LevelAt(p);
            if (sameLevel && !Physics.Linecast(transform.position + Vector3.up * 0.8f, p + Vector3.up * 0.8f, Layers.StructureMask | 1, QueryTriggerInteraction.Ignore))
                _path.Add(p);
            else if (!_pf.FindPath(transform.position, p, _path))
                _path.Add(p);
        }

        void TryRoll()
        {
            if (_rollCd > 0f || _rollT >= 0f) return;
            var dir = WasdDir();
            if (dir.sqrMagnitude < 0.01f)
            {
                if (Game.CameraRig.MouseOnGround(out var g)) dir = g - transform.position;
                else dir = transform.forward;
            }
            dir.y = 0;
            _rollDir = dir.normalized;
            _rollT = 0f;
            Physics.IgnoreLayerCollision(Layers.Player, Layers.Rail, true);   // перекат — через перила, вниз с яруса
            _rollCd = 1.1f;
            Health.Invulnerable = true;
            _path.Clear();
            StopMining();
            Game.Sound.PlayAt(Sfx.Jump, transform.position, 0.5f);
        }

        Vector3 WasdDir()
        {
            var v = Vector3.zero;
            if (InputBridge.KeyHeld(GameKey.W)) v.z += 1;
            if (InputBridge.KeyHeld(GameKey.S)) v.z -= 1;
            if (InputBridge.KeyHeld(GameKey.D)) v.x += 1;
            if (InputBridge.KeyHeld(GameKey.A)) v.x -= 1;
            if (v.sqrMagnitude < 0.01f || (Game.Ui != null && Game.Ui.ModalOpen)) return Vector3.zero;
            var yaw = Quaternion.Euler(0, Game.CameraRig.Yaw, 0);
            return yaw * v.normalized;
        }

        void Move(float dt)
        {
            _rollCd -= dt;
            Vector3 desired = Vector3.zero;
            float speed = MoveSpeed * (Sickness > 0.5f ? 0.8f : 1f);

            if (_rollT >= 0f)
            {
                _rollT += dt / 0.4f;
                Rig.RollT = _rollT;
                desired = _rollDir * 13f;
                if (_rollT >= 1f) { _rollT = -1f; Rig.RollT = -1f; Health.Invulnerable = false; Physics.IgnoreLayerCollision(Layers.Player, Layers.Rail, false); }
            }
            else
            {
                var wasd = WasdDir();
                if (wasd.sqrMagnitude > 0.01f)
                {
                    _path.Clear();
                    Focus = null;
                    StopMining();
                    desired = wasd * speed;
                }
                else if (AttackTarget != null && !AttackTarget.IsDead)
                {
                    float d = Vector3.Distance(transform.position, AttackTarget.transform.position);
                    if (d > WeaponRange * 0.9f) desired = (AttackTarget.transform.position - transform.position).normalized * speed;
                }
                else if (_path.Count > 0 && _pathIdx < _path.Count)
                {
                    var wp = _path[_pathIdx];
                    var to = wp - transform.position; to.y = 0;
                    float reach = _pathIdx == _path.Count - 1 ? (Focus != null ? Focus.InteractRange * 0.8f : 0.3f) : 0.6f;
                    if (to.magnitude < reach) _pathIdx++;
                    else desired = to.normalized * speed;
                }
                if (Focus != null && Vector3.Distance(Flat(transform.position), Flat(Focus.InteractPoint)) <= Focus.InteractRange)
                {
                    var f = Focus;
                    Focus = null;
                    _path.Clear();
                    desired = Vector3.zero;
                    FaceTowards(f.InteractPoint);
                    f.Interact(this);
                }
            }

            _vel = Vector3.Lerp(_vel, desired, 1f - Mathf.Exp(-14f * dt));
            var motion = _vel * dt;
            motion.y = -8f * dt; // прижимаем к земле
            _cc.Move(motion);
            // не проваливаемся под рельеф
            float gy = Game.Grid.SampleHeight(transform.position);
            if (transform.position.y < gy - 0.2f) transform.position = new Vector3(transform.position.x, gy, transform.position.z);

            var flat = new Vector3(_vel.x, 0, _vel.z);
            Rig.Speed = flat.magnitude;
            bool aiming = _aimPoint.HasValue || (AttackTarget != null && !AttackTarget.IsDead);
            Rig.Aiming = aiming;
            Rig.Working = IsMining;
            if (aiming)
            {
                var ap = _aimPoint ?? AttackTarget.AimPoint;
                FaceTowards(ap);
            }
            else if (flat.sqrMagnitude > 0.2f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat), 1f - Mathf.Exp(-14f * dt));

            if (flat.magnitude > 1f)
            {
                _stepTimer -= dt * flat.magnitude;
                if (_stepTimer <= 0f) { _stepTimer = 2.2f; Game.Sound.PlayAt(Sfx.Footstep, transform.position, 0.15f, 0.2f); }
            }
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

        void FaceTowards(Vector3 p)
        {
            var d = p - transform.position; d.y = 0;
            if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), 1f - Mathf.Exp(-18f * Time.deltaTime));
        }

        // ------------------------------------------------------------ бой

        public float WeaponRange => 17f;

        void Combat(float dt)
        {
            _fireCd -= dt;
            if (_rollT >= 0f) return;
            Vector3? target = null;
            if (_aimPoint.HasValue) target = _aimPoint;
            else if (AttackTarget != null && !AttackTarget.IsDead && Vector3.Distance(transform.position, AttackTarget.transform.position) <= WeaponRange)
                target = AttackTarget.AimPoint;
            if (AttackTarget != null && AttackTarget.IsDead) AttackTarget = null;
            if (!target.HasValue || _fireCd > 0f) return;
            var toT = target.Value - transform.position; toT.y = 0;
            if (Vector3.Angle(transform.forward, toT) > 25f) return;

            // патроны: 1 на 2 выстрела
            _ammoDebt += 0.5f;
            if (_ammoDebt >= 1f)
            {
                if (!Game.Bank.Spend(ResourceType.Ammo, 1f)) { Game.Log?.Warn("Нет патронов!"); _fireCd = 1f; return; }
                _ammoDebt -= 1f;
            }
            _fireCd = 0.16f;
            var muzzle = Rig.Muzzle.position;
            var dir = (target.Value - muzzle).normalized;
            dir = Quaternion.Euler(Random.Range(-1.2f, 1.2f), Random.Range(-1.5f, 1.5f), 0) * dir;
            ProjectileSystem.Instance.Fire(muzzle, dir * 70f, 15f, DamageType.Kinetic, Faction.Player, ProjectileLook.Plasma, 0f, 0f, gameObject, null, 0.6f);
            Game.Fx.Emit(FxKind.Muzzle, muzzle, 1, null, 1f, new Color(0.5f, 0.9f, 1f));
            Game.Sound.PlayAt(Sfx.Rifle, muzzle, 0.5f, 0.06f, 0.05f);
            Game.Noise?.Emit(transform.position, 0.8f, NoiseSource.Player); // «Скрытность» учитывается в NoiseSystem
        }

        // ------------------------------------------------------------ добыча и стройка

        public void StartMining(ResourceDeposit d)
        {
            if (d == null || d.IsDepleted) return;
            _mining = d;
            _miningT = 0f;
        }

        public void StopMining() { _mining = null; _miningT = 0f; }

        void Mining(float dt)
        {
            if (_mining == null) return;
            if (_mining.IsDepleted || Vector3.Distance(Flat(transform.position), Flat(_mining.transform.position)) > _mining.InteractRange + 1f) { StopMining(); return; }
            FaceTowards(_mining.transform.position);
            float speed = Game.Skills != null ? Game.Skills.MiningMult : 1f;
            _miningT += dt * speed / 1.6f;
            if (Random.value < 0.08f) Game.Fx.Emit(FxKind.Sparks, _mining.transform.position + Vector3.up * 0.6f, 2, Vector3.up);
            if (_miningT >= 1f)
            {
                _miningT = 0f;
                int got = _mining.Take(3);
                var type = _mining != null ? _mining.Type : ResourceType.Alloys;
                if (got > 0)
                {
                    Game.Bank.Add(type, got);
                    Game.Ui?.FloatText(transform.position + Vector3.up * 2.2f, $"+{got} {ResourceBank.Name(type)}", ResourceBank.ColorOf(type));
                }
                Game.Sound.PlayAt(Sfx.Mining, transform.position, 0.6f);
                Game.Noise?.Emit(transform.position, 1.6f, NoiseSource.Mining);
            }
        }

        /// <summary>Игрок рядом с чертежом ускоряет стройку (как в D4 — автоматически).</summary>
        void HelpConstruction(float dt)
        {
            _helpTimer -= dt;
            if (_helpTimer > 0f) return;
            _helpTimer = 0.25f;
            foreach (var s in Registry.Structures)
            {
                if (s == null || s.IsBuilt || s.IsDestroyed) continue;
                if (s.DistanceTo(transform.position) > 4f) continue;
                s.AddProgress(0.25f / Mathf.Max(1f, s.Def.BuildTime) * (Game.Skills != null ? Game.Skills.BuildSpeedMult : 1f));
                if (Random.value < 0.3f) Game.Fx.Emit(FxKind.Weld, s.transform.position + Vector3.up, 2, Vector3.up);
            }
        }

        public void TryPlaceCharge(CaveEntrance cave)
        {
            var a = Abilities[3] as ChargeAbility;
            string why = a != null ? a.TryUse(this, cave.InteractPoint) : "Нет зарядов";
            if (why != null) Game.Log?.Warn(why);
        }

        // ------------------------------------------------------------ болезнь и смерть

        public void ExposeToInsects(float dt)
        {
            if (Random.value < dt * 0.04f && Sickness <= 0f)
            {
                Sickness = 0.4f;
                Game.Log?.Warn("Вас укусили насекомые — лихорадка. Аптечка [3] или медблок помогут.");
            }
        }

        void UpdateSickness(float dt)
        {
            if (Sickness <= 0f) return;
            Sickness = Mathf.Min(1f, Sickness + dt * 0.002f);
            Health.TakeDamage(new DamageInfo(dt * 0.6f * Sickness, DamageType.Acid, transform.position, Faction.Neutral));
        }

        void Die()
        {
            if (_dead) return;
            _dead = true;
            Rig.Dead = true;
            RespawnTimer = 10f;
            _path.Clear();
            AttackTarget = null;
            StopMining();
            GameEvents.PlayerDown();
            Game.Log?.Alert("Вы тяжело ранены! Эвакуация в командный модуль через 10 секунд.");
        }

        void UpdateDead(float dt)
        {
            RespawnTimer -= dt;
            if (RespawnTimer > 0f) return;
            _dead = false;
            Rig.Dead = false;
            Sickness = 0f;
            var cmd = StructureLookup.FirstOf(StructureKind.Command);
            var p = cmd != null ? cmd.transform.position + cmd.transform.forward * 5f : Game.World.Center + Vector3.forward * 8f;
            Teleport(p);
            Health.Revive(0.6f);
            // штраф: часть ресурсов и репутация
            Game.Bank.Evaporate(ResourceType.Rations, 0.1f);
            Game.Rank.AddReputation(-3, "командир аванпоста выведен из строя");
        }

        public void Teleport(Vector3 p)
        {
            if (_rollT >= 0f) { _rollT = -1f; Rig.RollT = -1f; Health.Invulnerable = false; }
            Physics.IgnoreLayerCollision(Layers.Player, Layers.Rail, false);
            p.y = Game.Grid.SampleHeight(p);
            _cc.enabled = false;
            transform.position = p;
            _cc.enabled = true;
            _path.Clear();
            Game.CameraRig?.SnapTo(p);
        }
    }
}
