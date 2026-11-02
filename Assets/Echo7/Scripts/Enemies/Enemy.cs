using UnityEngine;

namespace Echo7
{
    /// <summary>
    /// Существо планеты: фоновая фауна или участник волны. Поведение зависит от типа:
    /// громилы идут по полю потока к реактору и проламывают слабые места, грызуны ищут мелкие постройки и кабели,
    /// прыгуны перемахивают низкие стены и атакуют механизмы, танк прёт прямо на реактор, плевуны стреляют кислотой.
    /// </summary>
    public class Enemy : MonoBehaviour
    {
        public EnemyArchetype Archetype { get; private set; }
        public Health Health { get; private set; }
        public CreatureRig Rig { get; private set; }
        public OnslaughtWave Wave;
        public bool IsDead { get; private set; }
        public bool IsJumping { get; private set; }
        public bool IsAttackingStructure { get; private set; }
        public bool Aggressive;              // фауна разозлена
        public Vector3 Home;                 // центр территории/стада
        public int HerdId;

        public bool IsHostile => Wave != null || (Archetype.Kind == EnemyKind.Stalker && Aggressive);
        public bool IsHostileOrAggressive => IsHostile || Archetype.Kind == EnemyKind.Stalker || (Archetype.Kind == EnemyKind.Grazer && Aggressive);
        public string DisplayName => Archetype.Name;
        public Vector3 AimPoint => transform.position + Vector3.up * Archetype.Size * 0.7f;

        Structure _targetStructure;
        Health _targetUnit;
        Transform _targetUnitT;
        float _think, _attackCd, _deathT, _fleeT, _chewProgress;
        Vector3 _vel, _wanderTarget;
        Vector3 _jumpFrom, _jumpTo;
        float _jumpT;
        Vector3 _lastPos;
        float _stuckT;

        public static Enemy Spawn(EnemyKind kind, Vector3 pos, OnslaughtWave wave)
        {
            var a = EnemyArchetype.Get(kind);
            var go = new GameObject(a.Name);
            go.transform.SetParent(Game.Enemies != null ? Game.Enemies.transform : null, false);
            pos.y = Game.Grid.SampleHeight(pos);
            go.transform.position = pos;
            go.layer = Layers.Enemy;
            var e = go.AddComponent<Enemy>();
            e.Archetype = a;
            e.Wave = wave;
            e.Home = pos;
            e.Health = go.AddComponent<Health>();
            float hp = a.MaxHp * (wave != null ? Mathf.Lerp(1f, 1.6f, Mathf.Clamp01((Game.Clock.Day - 1) / 20f)) * Game.Config.difficulty : 1f);
            e.Health.Init(hp, a.Armor, wave != null ? Faction.Hostile : Faction.Neutral, a.FlatArmor);
            e.Health.Died += _ => e.Die();
            e.Health.Damaged += e.OnDamaged;
            var col = go.AddComponent<CapsuleCollider>();
            col.radius = Mathf.Max(0.3f, a.Size * 0.55f);
            col.height = Mathf.Max(col.radius * 2f, a.Size * 1.2f);
            col.center = new Vector3(0, col.height * 0.5f, 0);
            col.isTrigger = true;
            e.Rig = CreatureRig.Build(go.transform, kind, a.Size);
            e._wanderTarget = pos;
            e._lastPos = pos;
            Registry.Enemies.Add(e);
            return e;
        }

        void OnDamaged(Health h, DamageInfo d, float amount)
        {
            if (Archetype.Fauna) Aggressive = true;
            if (Archetype.Kind == EnemyKind.Grazer) _fleeT = 6f;
            // разворачиваемся к обидчику
            if (d.Source == Faction.Player && d.Instigator != null && _targetUnit == null && Archetype.Kind != EnemyKind.Tank)
            {
                var hp = d.Instigator.GetComponentInParent<Health>();
                if (hp != null && hp.Faction == Faction.Player && !(d.Instigator.GetComponentInParent<Structure>() != null))
                {
                    _targetUnit = hp;
                    _targetUnitT = hp.transform;
                }
            }
        }

        void Die()
        {
            if (IsDead) return;
            IsDead = true;
            Rig.Die();
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;
            Registry.Enemies.Remove(this);
            Game.Fx.Emit(Archetype.Fauna ? FxKind.Blood : FxKind.Ichor, AimPoint, 10 + Mathf.RoundToInt(Archetype.Size * 6), Vector3.up, 1f);
            Game.Sound.PlayAt(Archetype.Kind == EnemyKind.Tank ? Sfx.Roar : Sfx.HitFlesh, transform.position, 0.6f);
            if (Archetype.Rations > 0 && Health.LastDamage.Source == Faction.Player)
            {
                int got = Game.Bank.Add(ResourceType.Rations, Archetype.Rations);
                if (got > 0) Game.Ui?.FloatText(transform.position + Vector3.up * 2f, $"+{got} пайков (охота)", ResourceBank.ColorOf(ResourceType.Rations));
            }
            Wave?.OnMemberDied(this);
            GameEvents.Killed(this);
        }

        void Update()
        {
            if (!Game.IsPlaying) return;
            float dt = Time.deltaTime;
            if (IsDead)
            {
                _deathT += dt;
                if (_deathT > 4f) Destroy(gameObject);
                return;
            }
            if (IsJumping) { UpdateJump(dt); return; }

            _attackCd -= dt;
            _think -= dt;
            if (_think <= 0f)
            {
                _think = 0.35f + Random.value * 0.15f;
                Think();
            }

            Vector3 desired = Vector3.zero;
            if (Archetype.Fauna && Wave == null) desired = FaunaBehaviour(dt);
            else desired = AssaultBehaviour(dt);

            Steer(desired, dt);
        }

        // ------------------------------------------------------------ решение

        void Think()
        {
            if (_targetUnit != null && (_targetUnit.IsDead || _targetUnitT == null)) { _targetUnit = null; _targetUnitT = null; }
            if (_targetStructure != null && _targetStructure.IsDestroyed) _targetStructure = null;
            var pos = transform.position;

            // юниты рядом — нападаем (кроме танка: он идёт к реактору)
            if (Archetype.Kind != EnemyKind.Tank && Archetype.Aggro > 0f && (Wave != null || Aggressive || Archetype.Kind == EnemyKind.Stalker))
            {
                var u = Registry.NearestFriendlyUnit(pos, Archetype.Aggro, out var t);
                if (u != null) { _targetUnit = u; _targetUnitT = t; if (Archetype.Fauna) Aggressive = true; }
                else if (_targetUnit != null && (_targetUnitT.position - pos).sqrMagnitude > Archetype.Aggro * Archetype.Aggro * 4f) { _targetUnit = null; _targetUnitT = null; }
            }

            if (Wave == null) return;
            // приманка: штурмовики рядом с шумовым маяком атакуют его
            if (_targetStructure == null && _targetUnit == null && Archetype.Kind != EnemyKind.Tank && Archetype.Kind != EnemyKind.Jumper)
            {
                var decoy = FindStructure(pos, 30f, s => s is DecoyBeacon && s.IsBuilt);
                if (decoy != null) { _targetStructure = decoy; return; }
            }
            switch (Archetype.Kind)
            {
                case EnemyKind.Swarmer:
                    if (_targetStructure == null || (_targetStructure.CableCut && !_targetStructure.Def.IsSmall))
                        _targetStructure = FindStructure(pos, 70f, s => s.IsBuilt && (s.Def.IsSmall || (s.Def.NeedsPower && !s.CableCut && !s.IsTargetCore)));
                    break;
                case EnemyKind.Jumper:
                    if (_targetStructure == null)
                        _targetStructure = FindStructure(pos, 80f, s => s.IsBuilt && s.Def.IsMechanism && !(s is Gate g && g.Jammed));
                    break;
                case EnemyKind.Tank:
                    _targetStructure = Game.Reactor;
                    break;
                case EnemyKind.Spitter:
                    _targetStructure = FindStructure(pos, Archetype.AttackRange + 6f, s => s.IsBuilt && (s is Turret || s.IsTargetCore || s.Def.IsMechanism));
                    break;
            }
        }

        static Structure FindStructure(Vector3 p, float radius, System.Func<Structure, bool> filter)
        {
            Structure best = null; float bd = radius * radius;
            foreach (var s in Registry.Structures)
            {
                if (s == null || s.IsDestroyed || !filter(s)) continue;
                float d = (s.transform.position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = s; }
            }
            return best;
        }

        // ------------------------------------------------------------ штурм базы

        Vector3 AssaultBehaviour(float dt)
        {
            IsAttackingStructure = false;
            var pos = transform.position;

            // 1) цель-юнит
            if (_targetUnit != null && _targetUnitT != null)
            {
                var to = _targetUnitT.position - pos; to.y = 0;
                float range = Archetype.AttackRange + 0.4f;
                if (Archetype.Ranged && to.magnitude <= Archetype.AttackRange) { Face(to); TryAttackUnit(); return Vector3.zero; }
                if (to.magnitude <= range) { Face(to); TryAttackUnit(); return Vector3.zero; }
                return MoveDirect(_targetUnitT.position);
            }

            // 2) плевун обстреливает постройки из-за стен
            if (Archetype.Ranged && _targetStructure != null)
            {
                var cp = _targetStructure.ClosestPoint(pos);
                var to = cp - pos; to.y = 0;
                if (to.magnitude <= Archetype.AttackRange) { Face(to); TrySpit(_targetStructure.transform.position + Vector3.up * 1.5f, _targetStructure.Health); return Vector3.zero; }
            }

            // 3) конкретная цель-постройка (грызуны, прыгуны, танк)
            if (_targetStructure != null && !Archetype.Ranged)
            {
                float d = _targetStructure.DistanceTo(pos);
                if (d <= Archetype.AttackRange + 0.3f)
                {
                    Face(_targetStructure.transform.position - pos);
                    AttackStructure(_targetStructure);
                    return Vector3.zero;
                }
                return MoveDirect(_targetStructure.ClosestPoint(pos));
            }

            // 4) поле потока к реактору
            var field = Archetype.Kind == EnemyKind.Jumper ? Game.Grid.JumperField : Game.Grid.GroundField;
            var cell = Game.Grid.WorldToCell(pos);
            var reactor = Game.Reactor;
            if (reactor != null && reactor.DistanceTo(pos) <= Archetype.AttackRange + 0.4f)
            {
                Face(reactor.transform.position - pos);
                AttackStructure(reactor);
                return Vector3.zero;
            }
            if (field.NextStep(cell, out var next))
            {
                var blocker = Game.Grid.StructureAt(next.x, next.y);
                if (blocker != null && !blocker.IsDestroyed && !blocker.PassableForEnemies && !blocker.IsTargetCore)
                    return HandleBlocker(blocker, next);
                return (Game.Grid.CellCenter(next) - pos);
            }
            // вне поля — просто к реактору
            return reactor != null ? MoveDirect(reactor.transform.position) : Vector3.zero;
        }

        /// <summary>Прямое движение к точке; если на пути постройка — прыжок или атака.</summary>
        Vector3 MoveDirect(Vector3 target)
        {
            var pos = transform.position;
            var to = target - pos; to.y = 0;
            if (to.sqrMagnitude < 0.01f) return Vector3.zero;
            var ahead = pos + to.normalized * Mathf.Max(1.2f, Archetype.Size * 0.8f + 0.6f);
            var cell = Game.Grid.WorldToCell(ahead);
            var blocker = Game.Grid.StructureAt(cell.x, cell.y);
            if (blocker != null && blocker != _targetStructure && !blocker.IsDestroyed && !blocker.PassableForEnemies)
                return HandleBlocker(blocker, cell);
            return to;
        }

        Vector3 HandleBlocker(Structure blocker, Vector2Int cell)
        {
            var pos = transform.position;
            // прыгун перемахивает низкие стены
            if (Archetype.Kind == EnemyKind.Jumper && blocker.Def.Jumpable && blocker.IsBuilt)
            {
                var dir = Game.Grid.CellCenter(cell) - pos; dir.y = 0;
                StartJump(pos + dir.normalized * (dir.magnitude + WorldGrid.CellSize * 1.2f));
                return Vector3.zero;
            }
            if (blocker.DistanceTo(pos) <= Archetype.AttackRange + 0.4f)
            {
                Face(blocker.transform.position - pos);
                AttackStructure(blocker);
                return Vector3.zero;
            }
            return blocker.ClosestPoint(pos) - pos;
        }

        void AttackStructure(Structure s)
        {
            IsAttackingStructure = true;
            if (_attackCd > 0f) return;
            _attackCd = Archetype.AttackInterval;
            Rig.Lunge();
            float dmg = Archetype.Damage * Archetype.StructureDamageMult * Game.Config.difficulty;
            // прыгун на механизме: двойной урон и порча привода ворот
            if (Archetype.Kind == EnemyKind.Jumper && s.Def.IsMechanism)
            {
                dmg *= 2f;
                if (s is Gate g) g.DamageMechanism(18f);
            }
            // грызун у работающего потребителя — перегрызает кабель
            if (Archetype.Kind == EnemyKind.Swarmer && s.Def.NeedsPower && !s.CableCut && !s.IsTargetCore)
            {
                _chewProgress += 1f;
                Game.Sound.PlayAt(Sfx.Chew, transform.position, 0.4f, 0.2f, 0.2f);
                if (_chewProgress >= 3f) { _chewProgress = 0f; s.CutCable(); _targetStructure = null; }
                if (!s.Def.IsSmall) return;
            }
            s.Health.TakeDamage(new DamageInfo(dmg, DamageType.Melee, s.ClosestPoint(transform.position) + Vector3.up, Faction.Hostile, gameObject));
            if (Random.value < 0.3f) Game.Sound.PlayAt(Archetype.AttackSound, transform.position, 0.35f, 0.2f, 0.3f);
        }

        void TryAttackUnit()
        {
            if (_attackCd > 0f || _targetUnit == null) return;
            _attackCd = Archetype.AttackInterval;
            if (Archetype.Ranged) { TrySpit(_targetUnitT.position + Vector3.up, _targetUnit); return; }
            Rig.Lunge();
            _targetUnit.TakeDamage(new DamageInfo(Archetype.Damage * Game.Config.difficulty, DamageType.Melee, _targetUnitT.position + Vector3.up, Faction.Hostile, gameObject));
            Game.Sound.PlayAt(Archetype.AttackSound, transform.position, 0.4f, 0.2f, 0.2f);
        }

        void TrySpit(Vector3 target, Health h)
        {
            if (_attackCd > 0f) return;
            _attackCd = Archetype.AttackInterval;
            Rig.Lunge();
            var from = AimPoint + transform.forward * 0.5f;
            float t = Mathf.Clamp(Vector3.Distance(from, target) / 14f, 0.5f, 1.2f);
            ProjectileSystem.Instance.Fire(from, ProjectileSystem.BallisticVelocity(from, target, t, 12f), Archetype.Damage * Game.Config.difficulty,
                DamageType.Acid, Faction.Hostile, ProjectileLook.Acid, 0f, 12f, gameObject, null, t + 1f);
            Game.Sound.PlayAt(Sfx.Screech, from, 0.3f, 0.3f, 0.3f);
        }

        // ------------------------------------------------------------ прыжок

        void StartJump(Vector3 to)
        {
            IsJumping = true;
            _jumpFrom = transform.position;
            to.y = Game.Grid.SampleHeight(to);
            _jumpTo = to;
            _jumpT = 0f;
            Game.Sound.PlayAt(Sfx.Jump, transform.position, 0.4f);
        }

        void UpdateJump(float dt)
        {
            _jumpT += dt / 0.8f;
            float t = Mathf.Clamp01(_jumpT);
            var p = Vector3.Lerp(_jumpFrom, _jumpTo, t);
            p.y += Mathf.Sin(t * Mathf.PI) * 3.6f;
            transform.position = p;
            var flat = _jumpTo - _jumpFrom; flat.y = 0;
            if (flat.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(flat);
            Rig.Speed = 0f;
            if (t >= 1f) { IsJumping = false; Game.Fx.Emit(FxKind.Dust, transform.position, 4, Vector3.up, 0.6f); }
        }

        // ------------------------------------------------------------ фауна

        Vector3 FaunaBehaviour(float dt)
        {
            var pos = transform.position;
            // хищник или разозлённый рогач
            if (_targetUnit != null && _targetUnitT != null && (Aggressive || Archetype.Kind == EnemyKind.Stalker))
            {
                var to = _targetUnitT.position - pos; to.y = 0;
                // поводок: далеко от дома — бросаем погоню
                if ((pos - Home).sqrMagnitude > 55f * 55f) { _targetUnit = null; Aggressive = false; return Home - pos; }
                if (to.magnitude <= Archetype.AttackRange + 0.3f) { Face(to); TryAttackUnit(); return Vector3.zero; }
                return MoveDirect(_targetUnitT.position);
            }
            // травоед бежит от шума и угроз
            if (_fleeT > 0f)
            {
                _fleeT -= dt;
                var threat = Game.Player != null ? Game.Player.transform.position : pos;
                var away = pos - threat; away.y = 0;
                return away.normalized * 2f;
            }
            // блуждание вокруг дома/стада
            if ((pos - _wanderTarget).sqrMagnitude < 4f || Random.value < 0.002f)
            {
                var off = Random.insideUnitCircle * (Archetype.Kind == EnemyKind.Grazer ? 12f : 25f);
                _wanderTarget = Home + new Vector3(off.x, 0, off.y);
                _wanderTarget.x = Mathf.Clamp(_wanderTarget.x, 6f, WorldGenerator.WorldSize - 6f);
                _wanderTarget.z = Mathf.Clamp(_wanderTarget.z, 6f, WorldGenerator.WorldSize - 6f);
            }
            var w = _wanderTarget - pos; w.y = 0;
            return w.normalized * 0.45f; // неспешно
        }

        public void Scare(Vector3 source)
        {
            if (Archetype.Kind == EnemyKind.Grazer) _fleeT = 5f;
        }

        // ------------------------------------------------------------ движение

        void Face(Vector3 dir)
        {
            dir.y = 0;
            if (dir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 8f);
        }

        void Steer(Vector3 desired, float dt)
        {
            desired.y = 0;
            float speedMul = desired.magnitude < 1f ? Mathf.Clamp01(desired.magnitude) : 1f;
            if (_fleeT > 0f) speedMul = 2.2f;
            var dir = desired.sqrMagnitude > 0.0001f ? desired.normalized : Vector3.zero;

            // разделение с соседями
            var sep = Game.Enemies != null ? Game.Enemies.Separation(this) : Vector3.zero;
            var v = dir * Archetype.Speed * speedMul + sep * 2.5f;
            // не заходим в постройки (кроме прыжка) и в непроходимые клетки
            var next = transform.position + v * dt;
            var c = Game.Grid.WorldToCell(next);
            var s = Game.Grid.StructureAt(c.x, c.y);
            if ((s != null && !s.IsDestroyed && !s.PassableForEnemies && s.DistanceTo(next) < 0.05f) || Game.Grid.IsBlocked(c.x, c.y))
            {
                // скольжение вдоль препятствия
                var alt1 = new Vector3(v.x, 0, 0); var alt2 = new Vector3(0, 0, v.z);
                var c1 = Game.Grid.WorldToCell(transform.position + alt1 * dt);
                var c2 = Game.Grid.WorldToCell(transform.position + alt2 * dt);
                bool ok1 = Game.Grid.StructureAt(c1.x, c1.y) == null && !Game.Grid.IsBlocked(c1.x, c1.y);
                bool ok2 = Game.Grid.StructureAt(c2.x, c2.y) == null && !Game.Grid.IsBlocked(c2.x, c2.y);
                v = ok1 ? alt1 : ok2 ? alt2 : Vector3.zero;
            }
            _vel = Vector3.Lerp(_vel, v, 1f - Mathf.Exp(-8f * dt));
            var p = transform.position + _vel * dt;
            p.y = Game.Grid.SampleHeight(p);
            transform.position = p;
            var flat = new Vector3(_vel.x, 0, _vel.z);
            Rig.Speed = flat.magnitude;
            if (flat.sqrMagnitude > 0.05f && !IsAttackingStructure)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat), 1f - Mathf.Exp(-8f * dt));

            // застрял — атакуем ближайшую постройку, чтобы не стоять вечно
            _stuckT = (transform.position - _lastPos).sqrMagnitude < 0.0004f && dir.sqrMagnitude > 0.01f ? _stuckT + dt : 0f;
            _lastPos = transform.position;
            if (_stuckT > 2.5f && Wave != null)
            {
                _stuckT = 0f;
                var nearest = FindStructure(transform.position, 4f, st => !st.IsDestroyed);
                if (nearest != null) _targetStructure = nearest;
            }
        }

        /// <summary>Прогноз позиции для навесной стрельбы.</summary>
        public Vector3 PredictPosition(float seconds) => transform.position + new Vector3(_vel.x, 0, _vel.z) * seconds;
    }
}
