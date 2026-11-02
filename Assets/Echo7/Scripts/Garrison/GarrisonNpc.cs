using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    public enum NpcRole { Soldier, Engineer, Medic }

    public enum NpcState { Idle, Manning, Fighting, Repairing, Healing, Resting, ToMedbay, Away, Deserting, Dead }

    /// <summary>
    /// Боец гарнизона. Может стоять расчётом турели (влияет на её точность через мораль), воевать,
    /// чинить (инженер), лечить (медик), отдыхать в жилом блоке. При низкой морали отказывается выходить на стены,
    /// саботирует или дезертирует, забрав оружие.
    /// </summary>
    public class GarrisonNpc : MonoBehaviour, IInteractable
    {
        public string Name;
        public NpcRole Role;
        public Health Health { get; private set; }
        public HumanoidRig Rig { get; private set; }
        public float Morale = 70f;
        public float Personality;            // индивидуальное смещение морали
        public float Skill = 0.6f;
        public float Sickness;
        public Turret Post { get; private set; }
        public NpcState State { get; private set; } = NpcState.Idle;
        public bool IsAway => State == NpcState.Away || State == NpcState.Deserting;
        public float LastTalkDay = -10f;
        public bool Refusing { get; private set; }

        readonly List<Vector3> _path = new List<Vector3>();
        int _pathIdx;
        Vector3 _goal;
        float _think, _fireCd, _repathT, _sickTreatPaid;
        Enemy _enemy;
        Structure _workTarget;
        GarrisonNpc _patient;
        Vector3 _vel;

        public string RoleName => Role == NpcRole.Soldier ? "Рядовой" : Role == NpcRole.Engineer ? "Инженер" : "Медик";
        public string DisplayName => $"{RoleName} {Name}";
        public bool IsSheltered
        {
            get
            {
                if (State != NpcState.Resting) return false;
                foreach (var s in Registry.Structures) if (s is Habitat h && h.IsBuilt && h.DistanceTo(transform.position) < 2f) return true;
                return false;
            }
        }

        public string InteractLabel => $"Поговорить: {DisplayName}";
        public Vector3 InteractPoint => transform.position;
        public float InteractRange => 2.5f;
        public bool CanInteract => !Health.IsDead && !IsAway;

        public static GarrisonNpc Spawn(string name, NpcRole role, Vector3 pos)
        {
            var go = new GameObject("NPC_" + name);
            go.transform.SetParent(Game.Garrison.transform, false);
            pos.y = Game.Grid.SampleHeight(pos);
            go.transform.position = pos;
            go.layer = Layers.Npc;
            var n = go.AddComponent<GarrisonNpc>();
            n.Name = name;
            n.Role = role;
            n.Personality = Random.Range(-10f, 10f);
            n.Skill = Random.Range(0.45f, 0.9f);
            n.Health = go.AddComponent<Health>();
            n.Health.Init(110f, ArmorClass.Medium, Faction.Player, 1f);
            n.Health.Died += _ => n.Die();
            var st = HumanoidStyle.Soldier;
            st.Plates = role == NpcRole.Medic ? Palette.HullLight : Palette.HullMid;
            st.Accent = role == NpcRole.Engineer ? Palette.Hazard : Palette.Orange;
            st.Visor = role == NpcRole.Medic ? Palette.Blue : Palette.Orange;
            st.MedicCross = role == NpcRole.Medic;
            st.Scale = Random.Range(0.94f, 1.04f);
            n.Rig = HumanoidRig.Build(go.transform, st);
            var col = go.AddComponent<CapsuleCollider>();
            col.radius = 0.35f; col.height = 1.8f; col.center = new Vector3(0, 0.9f, 0);
            col.isTrigger = true;
            Registry.Npcs.Add(n);
            return n;
        }

        public bool IsManning(Turret t) => Post == t && State == NpcState.Manning && t != null && t.DistanceTo(transform.position) < 2.5f;

        public void AssignTo(Turret t)
        {
            if (Post != null && Post != t && Post.Crew == this) Post.Crew = null;
            Post = t;
        }

        public void Unassign()
        {
            if (Post != null && Post.Crew == this) Post.Crew = null;
            Post = null;
            if (State == NpcState.Manning) State = NpcState.Idle;
        }

        public void SendAway() { Unassign(); State = NpcState.Away; gameObject.SetActive(false); }
        public void ReturnFromAway(Vector3 at)
        {
            gameObject.SetActive(true);
            at.y = Game.Grid.SampleHeight(at);
            transform.position = at;
            State = NpcState.Idle;
        }

        public void AddMorale(float d) => Morale = Mathf.Clamp(Morale + d, 0f, 100f);

        public void ExposeToInsects(float dt)
        {
            if (Sickness > 0f) return;
            if (Random.value < dt * 0.05f)
            {
                Sickness = 0.3f;
                Game.Log?.Warn($"{DisplayName}: лихорадка от укусов насекомых. Нужен медблок и аптечки.");
            }
        }

        public void TreatSickness(float dt)
        {
            if (Sickness <= 0f) return;
            if (_sickTreatPaid <= 0f)
            {
                if (!Game.Bank.Spend(ResourceType.Medkits, 1)) return; // без аптечек не вылечить
                _sickTreatPaid = 1f;
            }
            Sickness = Mathf.Max(0f, Sickness - dt * 0.08f * (Game.Skills != null ? Game.Skills.HealMult : 1f));
            if (Sickness <= 0f) { _sickTreatPaid = 0f; Game.Log?.Info($"{DisplayName} выздоровел(а)."); }
        }

        void Die()
        {
            if (State == NpcState.Dead) return;
            var post = Post;
            State = NpcState.Dead;
            Rig.Dead = true;
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;
            if (post != null) { Post = null; Game.Defense?.OnCrewDied(post); }
            Registry.Npcs.Remove(this);
            Game.Log?.Alert($"Погиб(ла) {DisplayName}.");
            GameEvents.NpcKilled(this);
            Destroy(gameObject, 8f);
        }

        // ------------------------------------------------------------ поведение

        void Update()
        {
            if (!Game.IsPlaying || State == NpcState.Dead || State == NpcState.Away) return;
            float dt = Time.deltaTime;
            if (Sickness > 0f)
            {
                Sickness = Mathf.Min(1f, Sickness + dt * 0.0015f);
                Health.TakeDamage(new DamageInfo(dt * 0.35f * Sickness, DamageType.Acid, transform.position, Faction.Neutral));
            }
            _think -= dt;
            if (_think <= 0f) { _think = 0.6f + Random.value * 0.3f; Think(); }
            Act(dt);
        }

        void Think()
        {
            if (State == NpcState.Deserting) return;
            var pos = transform.position;
            _enemy = Registry.NearestEnemy(pos, 16f);
            Refusing = false;

            // раненые/больные — в медблок
            var med = StructureLookup.Nearest<MedBay>(pos, m => m.IsOperational);
            if ((Health.Fraction < 0.35f || Sickness > 0.25f) && med != null)
            {
                State = NpcState.ToMedbay;
                Goto(med.ClosestPoint(pos));
                return;
            }

            // расчёт турели
            if (Post != null && !Post.IsDestroyed)
            {
                if (Morale < 20f && (Game.Defense == null || !Game.Defense.Alert || Morale < 12f))
                {
                    Refusing = true;
                    if (Random.value < 0.05f) Game.Log?.Warn($"{DisplayName} отказывается выходить на стены — мораль на нуле.");
                }
                else
                {
                    State = NpcState.Manning;
                    var spot = Post.ClosestPoint(pos) + (pos - Post.transform.position).normalized * 0.8f;
                    if (Post.DistanceTo(pos) > 1.8f) Goto(spot); else _path.Clear();
                    Post.Crew = this;
                    return;
                }
            }

            // бой
            if (_enemy != null && Health.Fraction > 0.3f && !Refusing)
            {
                State = NpcState.Fighting;
                return;
            }

            // работа по специальности
            if (Role == NpcRole.Engineer)
            {
                _workTarget = FindRepairTarget(pos);
                if (_workTarget != null)
                {
                    State = NpcState.Repairing;
                    if (_workTarget.DistanceTo(pos) > 2f) Goto(_workTarget.ClosestPoint(pos));
                    return;
                }
            }
            if (Role == NpcRole.Medic)
            {
                _patient = FindPatient(pos);
                if (_patient != null)
                {
                    State = NpcState.Healing;
                    if ((_patient.transform.position - pos).sqrMagnitude > 4f) Goto(_patient.transform.position);
                    return;
                }
            }

            // ночью и в миграцию насекомых — отдых под крышей
            bool shelter = (Game.Clock.IsNight && !(Game.Defense != null && Game.Defense.Alert)) || (Game.Weather != null && Game.Weather.Current == WeatherKind.InsectMigration);
            var hab = StructureLookup.Nearest<Habitat>(pos, h => h.IsBuilt);
            if (shelter && hab != null)
            {
                State = NpcState.Resting;
                if (hab.DistanceTo(pos) > 1.2f) Goto(hab.ClosestPoint(pos));
                return;
            }

            // днём — прогулка по базе
            if (State != NpcState.Idle || _path.Count == 0 || _pathIdx >= _path.Count)
            {
                State = NpcState.Idle;
                if (Random.value < 0.3f)
                {
                    var c = Game.Reactor != null ? Game.Reactor.transform.position : Game.World.Center;
                    var off = Random.insideUnitCircle * 18f;
                    Goto(c + new Vector3(off.x, 0, off.y));
                }
            }
        }

        static Structure FindRepairTarget(Vector3 p)
        {
            Structure best = null; float bd = 50f * 50f;
            foreach (var s in Registry.Structures)
            {
                if (s == null || s.IsDestroyed) continue;
                bool need = !s.IsBuilt || s.Health.Fraction < 0.8f || s.NeedsService;
                if (!need) continue;
                float d = (s.transform.position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = s; }
            }
            return best;
        }

        GarrisonNpc FindPatient(Vector3 p)
        {
            GarrisonNpc best = null; float worst = 0.75f;
            foreach (var n in Registry.Npcs)
            {
                if (n == null || n == this || n.Health.IsDead || n.IsAway) continue;
                if (n.Health.Fraction < worst) { worst = n.Health.Fraction; best = n; }
            }
            return best;
        }

        void Goto(Vector3 p)
        {
            if ((p - _goal).sqrMagnitude < 1f && _path.Count > 0 && _pathIdx < _path.Count && _repathT > 0f) return;
            _goal = p;
            _repathT = 2f;
            Game.Garrison.Pathfinder.FindPath(transform.position, p, _path);
            _pathIdx = 0;
        }

        void Act(float dt)
        {
            _repathT -= dt;
            _fireCd -= dt;
            var pos = transform.position;
            Vector3 desired = Vector3.zero;
            bool aiming = false, working = false;
            float speed = State == NpcState.Fighting || (Game.Defense != null && Game.Defense.Alert) ? 5.2f : 3.6f;
            if (Morale < 25f) speed *= 0.85f;

            if (State == NpcState.Fighting && _enemy != null && !_enemy.IsDead)
            {
                var to = _enemy.transform.position - pos; to.y = 0;
                if (to.magnitude > 13f) desired = to.normalized * speed;
                else { aiming = true; Shoot(); }
                Face(aiming ? to : desired);
            }
            else if (_path.Count > 0 && _pathIdx < _path.Count)
            {
                var to = _path[_pathIdx] - pos; to.y = 0;
                if (to.magnitude < 0.5f) _pathIdx++;
                else desired = to.normalized * speed;
                Face(desired);
            }
            else
            {
                switch (State)
                {
                    case NpcState.Manning:
                        if (Post != null && Post.Target != null) { aiming = true; Face(Post.Target.transform.position - pos); }
                        break;
                    case NpcState.Repairing:
                        if (_workTarget != null && !_workTarget.IsDestroyed && _workTarget.DistanceTo(pos) < 2.5f)
                        {
                            working = true;
                            Face(_workTarget.transform.position - pos);
                            if (!_workTarget.IsBuilt) _workTarget.AddProgress(dt / Mathf.Max(1f, _workTarget.Def.BuildTime) * 0.8f * Skill * 1.5f);
                            else if (_workTarget.Health.Fraction < 1f && Game.Bank.Spend(ResourceType.Alloys, dt * 0.25f)) _workTarget.Repair(dt * 12f * Skill);
                            else if (_workTarget.NeedsService && Random.value < dt * 0.4f) _workTarget.Service();
                            if (Random.value < dt * 3f) Game.Fx.Emit(FxKind.Weld, _workTarget.ClosestPoint(pos) + Vector3.up, 1, Vector3.up);
                        }
                        break;
                    case NpcState.Healing:
                        if (_patient != null && !_patient.Health.IsDead && (_patient.transform.position - pos).sqrMagnitude < 6f)
                        {
                            working = true;
                            Face(_patient.transform.position - pos);
                            _patient.Health.Heal(dt * 5f * (Game.Skills != null ? Game.Skills.HealMult : 1f));
                            if (Random.value < dt * 2f) Game.Fx.Emit(FxKind.Heal, _patient.transform.position + Vector3.up, 1, Vector3.up);
                        }
                        break;
                }
            }

            _vel = Vector3.Lerp(_vel, desired, 1f - Mathf.Exp(-10f * dt));
            var p = pos + _vel * dt;
            p.y = Game.Grid.SampleHeight(p);
            transform.position = p;
            Rig.Speed = new Vector3(_vel.x, 0, _vel.z).magnitude;
            Rig.Aiming = aiming;
            Rig.Working = working;
        }

        void Face(Vector3 dir)
        {
            dir.y = 0;
            if (dir.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 8f);
        }

        void Shoot()
        {
            if (_fireCd > 0f || _enemy == null) return;
            if (!Game.Bank.Spend(ResourceType.Ammo, 0.5f)) { _fireCd = 1f; return; }
            _fireCd = 0.5f;
            float acc = 0.5f * (0.75f + 0.5f * Morale / 100f) * (0.7f + Skill * 0.5f);
            var muzzle = Rig.Muzzle.position;
            Vector3 hit;
            if (Random.value < acc)
            {
                hit = _enemy.AimPoint;
                _enemy.Health.TakeDamage(new DamageInfo(9f, DamageType.Kinetic, hit, Faction.Player, gameObject));
            }
            else hit = _enemy.AimPoint + Random.insideUnitSphere * 1.5f;
            Game.Fx.Line(muzzle, hit, new Color(1f, 0.7f, 0.3f, 1f), 0.04f, 0.06f);
            Game.Sound.PlayAt(Sfx.Rifle, muzzle, 0.3f, 0.15f, 0.08f);
            Game.Noise?.Emit(pos: transform.position, amount: 0.4f, src: NoiseSource.Player);
        }

        /// <summary>Дезертирство: уходит за край карты, забрав оружие и патроны.</summary>
        public void Desert()
        {
            if (State == NpcState.Dead) return;
            Unassign();
            State = NpcState.Deserting;
            Game.Bank.Consume(ResourceType.Ammo, 20f);
            Game.Log?.Alert($"{DisplayName} дезертировал(а), прихватив оружие и 20 патронов!");
            var c = Game.World.Center;
            var dir = (transform.position - c); dir.y = 0;
            if (dir.sqrMagnitude < 1f) dir = Vector3.forward;
            var exit = c + dir.normalized * 125f;
            Game.Garrison.Pathfinder.FindPath(transform.position, exit, _path);
            _pathIdx = 0;
            Registry.Npcs.Remove(this);
            Destroy(gameObject, 45f);
        }

        /// <summary>Саботаж при крайне низкой морали: перерезает кабель случайной постройки.</summary>
        public void Sabotage()
        {
            foreach (var s in Registry.Structures)
            {
                if (s == null || !s.IsBuilt || !s.Def.NeedsPower || s.CableCut || s.IsTargetCore) continue;
                if (s.DistanceTo(transform.position) > 30f) continue;
                s.CutCable();
                Game.Log?.Alert($"Саботаж! Кто-то из гарнизона повредил систему «{s.Def.Name}».");
                return;
            }
        }

        public void Interact(PlayerController player) => Game.Dialogue?.Start(DialogueLibrary.NpcTalk(this));

        public string StateText
        {
            get
            {
                if (Refusing) return "отказывается от поста";
                switch (State)
                {
                    case NpcState.Manning: return Post != null ? $"расчёт: {Post.Def.Name}" : "на посту";
                    case NpcState.Fighting: return "в бою";
                    case NpcState.Repairing: return "ремонт";
                    case NpcState.Healing: return "лечит";
                    case NpcState.Resting: return "отдыхает";
                    case NpcState.ToMedbay: return "в медблок";
                    case NpcState.Away: return "на задании";
                    case NpcState.Deserting: return "дезертир";
                }
                return "свободен";
            }
        }
    }
}
