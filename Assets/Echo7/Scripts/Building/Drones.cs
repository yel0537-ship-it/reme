using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    /// <summary>Визуал квадрокоптера (как дроны на референсах): корпус, четыре ротора, огни.</summary>
    public static class DroneVisual
    {
        public static List<Transform> Build(Transform parent, Material accent, float scale = 1f)
        {
            var mb = new ModelBuilder(parent, "DroneModel");
            mb.Root.localScale = Vector3.one * scale;
            mb.Box(Vector3.zero, new Vector3(0.7f, 0.25f, 0.9f), Palette.HullLight, 0.08f);
            mb.Box(new Vector3(0, -0.15f, 0.1f), new Vector3(0.4f, 0.15f, 0.4f), Palette.HullDark, 0.04f);
            mb.Strip(new Vector3(0, 0.02f, 0.46f), new Vector3(0.4f, 0.06f, 0.03f), accent);
            var rotors = new List<Transform>();
            for (int i = 0; i < 4; i++)
            {
                float sx = i % 2 == 0 ? -1f : 1f, sz = i < 2 ? -1f : 1f;
                var arm = new Vector3(sx * 0.6f, 0.05f, sz * 0.6f);
                mb.Box(arm * 0.5f, new Vector3(0.6f, 0.06f, 0.08f), Palette.HullDark, 0f, null, new Vector3(0, sx * sz * 45f, 0));
                var rotor = mb.Group("#rotor" + i, arm + Vector3.up * 0.08f);
                mb.Cyl(Vector3.zero, 0.3f, 0.02f, MaterialFactory.Cached("rotor", () => MaterialFactory.Transparent(new Color(0.15f, 0.15f, 0.15f, 0.45f), 0.3f)), rotor, null, 12);
                mb.Box(Vector3.zero, new Vector3(0.55f, 0.02f, 0.05f), Palette.HullDark, 0f, rotor);
                rotors.Add(rotor);
            }
            return rotors;
        }
    }

    /// <summary>
    /// Строительные дроны командного модуля: возводят чертежи, чинят постройки (тратят сплавы),
    /// восстанавливают кабели, обслуживают изношенные турели и перезаряжают мины.
    /// </summary>
    public class ConstructionManager : MonoBehaviour
    {
        readonly List<BuilderDrone> _drones = new List<BuilderDrone>();
        readonly Dictionary<Structure, BuilderDrone> _jobs = new Dictionary<Structure, BuilderDrone>();
        readonly Dictionary<Structure, float> _serviceCooldown = new Dictionary<Structure, float>();

        public void ServiceFailed(Structure s) { if (s != null) _serviceCooldown[s] = Time.time + 20f; }
        float _check;

        public int DroneCount => _drones.Count;

        void Update()
        {
            if (!Game.IsPlaying) return;
            _check -= Time.deltaTime;
            if (_check > 0f) return;
            _check = 1f;
            int want = Game.Rank.BuilderDrones;
            var home = StructureLookup.FirstOf(StructureKind.Command);
            if (home == null) return;
            while (_drones.Count < want)
            {
                var go = new GameObject("BuilderDrone");
                go.transform.SetParent(transform, false);
                go.transform.position = home.transform.position + Vector3.up * 6f;
                var d = go.AddComponent<BuilderDrone>();
                d.Init(this);
                _drones.Add(d);
            }
        }

        public Vector3 HomePoint
        {
            get
            {
                var home = StructureLookup.FirstOf(StructureKind.Command);
                return home != null ? home.transform.position + Vector3.up * 5.5f : Game.World.Center + Vector3.up * 6f;
            }
        }

        public void Release(Structure s, BuilderDrone d)
        {
            if (s != null && _jobs.TryGetValue(s, out var owner) && owner == d) _jobs.Remove(s);
        }

        public enum JobKind { None, Build, Repair, Service }

        /// <summary>Поиск работы для дрона: стройка → ремонт → обслуживание.</summary>
        public Structure FindJob(BuilderDrone d, out JobKind kind)
        {
            kind = JobKind.None;
            Structure best = null; float bestScore = float.MaxValue;
            var p = d.transform.position;
            bool wave = Game.Defense != null && Game.Defense.Alert;
            foreach (var s in Registry.Structures)
            {
                if (s == null || s.IsDestroyed) continue;
                if (_jobs.TryGetValue(s, out var owner) && owner != d && owner != null) continue;
                JobKind k = JobKind.None;
                float prio = 0f;
                if (!s.IsBuilt) { if (!s.ReadyToBuild) continue; k = JobKind.Build; prio = 0f; }
                else if (s.Health.Current < s.Health.Max * (wave ? 0.6f : 0.97f) && Game.Bank[ResourceType.Alloys] > 0) { k = JobKind.Repair; prio = 30f + s.Health.Fraction * 40f; if (s.IsTargetCore) prio -= 40f; }
                else if (s.NeedsService && !(_serviceCooldown.TryGetValue(s, out var cd) && Time.time < cd)) { k = JobKind.Service; prio = 60f; }
                if (k == JobKind.None) continue;
                // во время волны не летаем чинить то, что прямо под ударом снаружи стен
                if (wave && Registry.NearestEnemy(s.transform.position, 6f) != null) prio += 200f;
                float score = prio + (s.transform.position - p).magnitude * 0.5f;
                if (score < bestScore) { bestScore = score; best = s; kind = k; }
            }
            if (best != null) _jobs[best] = d;
            return best;
        }
    }

    public class BuilderDrone : MonoBehaviour
    {
        ConstructionManager _mgr;
        List<Transform> _rotors;
        Structure _job;
        ConstructionManager.JobKind _kind;
        float _think, _serviceTimer, _alloyDebt;
        Vector3 _vel;

        public void Init(ConstructionManager mgr)
        {
            _mgr = mgr;
            _rotors = DroneVisual.Build(transform, Palette.Blue, 1.1f);
        }

        void Update()
        {
            if (!Game.IsPlaying) return;
            foreach (var r in _rotors) if (r != null) r.Rotate(0, 1800f * Time.deltaTime, 0, Space.Self);

            if (_job == null || _job.IsDestroyed || JobDone())
            {
                if (_job != null) _mgr.Release(_job, this);
                _job = null;
                _think -= Time.deltaTime;
                if (_think <= 0f)
                {
                    _think = 0.8f;
                    _job = _mgr.FindJob(this, out _kind);
                    _serviceTimer = 0f;
                }
            }

            Vector3 target = _job != null ? _job.transform.position + Vector3.up * (_job.Def.Height + 2.2f) : _mgr.HomePoint;
            target += new Vector3(Mathf.Sin(Time.time * 1.3f + GetInstanceID()), Mathf.Sin(Time.time * 2.1f) * 0.3f, Mathf.Cos(Time.time * 1.1f)) * 0.6f;
            var to = target - transform.position;
            float speed = 9f;
            var desired = to.magnitude > 0.5f ? to.normalized * Mathf.Min(speed, to.magnitude * 2f) : Vector3.zero;
            _vel = Vector3.Lerp(_vel, desired, Time.deltaTime * 3f);
            transform.position += _vel * Time.deltaTime;
            var flat = new Vector3(_vel.x, 0, _vel.z);
            if (flat.sqrMagnitude > 0.1f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat) * Quaternion.Euler(Mathf.Clamp(flat.magnitude * 3f, 0, 20), 0, 0), Time.deltaTime * 4f);

            if (_job != null && to.magnitude < 3f) Work();
        }

        bool JobDone()
        {
            switch (_kind)
            {
                case ConstructionManager.JobKind.Build: return _job.IsBuilt;
                case ConstructionManager.JobKind.Repair: return _job.Health.Current >= _job.Health.Max - 0.5f || Game.Bank[ResourceType.Alloys] <= 0;
                case ConstructionManager.JobKind.Service: return !_job.NeedsService;
            }
            return true;
        }

        void Work()
        {
            var weldPoint = _job.transform.position + Vector3.up * Mathf.Min(_job.Def.Height, 2f) + Random.insideUnitSphere * _job.HalfExtent * 0.4f;
            if (Random.value < 0.4f)
            {
                Game.Fx.Line(transform.position + Vector3.down * 0.2f, weldPoint, new Color(0.5f, 0.85f, 1f, 0.9f), 0.04f, 0.08f);
                Game.Fx.Emit(FxKind.Weld, weldPoint, 1, Vector3.up, 1f);
            }
            switch (_kind)
            {
                case ConstructionManager.JobKind.Build:
                    float rate = Time.deltaTime / Mathf.Max(1f, _job.Def.BuildTime) * (Game.Skills != null ? Game.Skills.BuildSpeedMult : 1f);
                    _job.AddProgress(rate);
                    Game.Noise?.EmitContinuous(_job.transform.position, 1.2f * Time.deltaTime, NoiseSource.Construction);
                    if (Random.value < 0.02f) Game.Sound.PlayAt(Sfx.Build, weldPoint, 0.25f, 0.2f, 0.4f);
                    break;
                case ConstructionManager.JobKind.Repair:
                    float hp = 30f * Time.deltaTime * (Game.Skills != null ? Game.Skills.RepairMult : 1f);
                    _alloyDebt += hp / 40f; // 1 сплав на 40 единиц прочности
                    if (_alloyDebt >= 1f)
                    {
                        int pay = Mathf.FloorToInt(_alloyDebt);
                        if (!Game.Bank.Spend(ResourceType.Alloys, pay)) { _job = null; return; }
                        _alloyDebt -= pay;
                    }
                    _job.Repair(hp);
                    break;
                case ConstructionManager.JobKind.Service:
                    _serviceTimer += Time.deltaTime;
                    if (_serviceTimer >= 2.5f)
                    {
                        _job.Service();
                        _serviceTimer = 0f;
                        if (_job.NeedsService) { _mgr.ServiceFailed(_job); _mgr.Release(_job, this); _job = null; }
                    }
                    break;
            }
        }
    }

    /// <summary>Дронпорт: обслуживает двух дронов-сборщиков.</summary>
    public class DroneBay : Structure
    {
        public ResourceType? Priority; // null — самый дефицитный местный ресурс
        readonly List<GathererDrone> _drones = new List<GathererDrone>();

        protected override void OnBuilt()
        {
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("Gatherer");
                go.transform.SetParent(transform, false);
                go.transform.position = transform.position + Vector3.up * 2f + Vector3.right * i;
                var d = go.AddComponent<GathererDrone>();
                d.Init(this);
                _drones.Add(d);
            }
        }

        protected override void OnRemoved()
        {
            foreach (var d in _drones) if (d != null) d.ReleaseReservation();
        }

        public void CyclePriority()
        {
            if (!Priority.HasValue) Priority = ResourceType.Titanium;
            else if (Priority == ResourceType.Titanium) Priority = ResourceType.Crystals;
            else if (Priority == ResourceType.Crystals) Priority = ResourceType.Silicon;
            else if (Priority == ResourceType.Silicon) Priority = ResourceType.Alloys;
            else Priority = null;
        }

        public string PriorityName => Priority.HasValue ? ResourceBank.Name(Priority.Value) : "Самое дефицитное";

        public override string StatusText()
        {
            int busy = 0;
            foreach (var d in _drones) if (d != null && d.Busy) busy++;
            return base.StatusText() + $"\nПриоритет: {PriorityName}  ·  дронов в работе: {busy}/{_drones.Count}";
        }

        public ResourceType WantedType()
        {
            if (Priority.HasValue) return Priority.Value;
            var b = Game.Bank;
            var t = ResourceType.Titanium;
            if (b[ResourceType.Crystals] < b[t]) t = ResourceType.Crystals;
            if (b[ResourceType.Silicon] < b[t]) t = ResourceType.Silicon;
            return t;
        }
    }

    public class GathererDrone : MonoBehaviour
    {
        enum S { Idle, ToDeposit, Mining, Returning }
        DroneBay _bay;
        List<Transform> _rotors;
        S _state = S.Idle;
        ResourceDeposit _dep;
        int _cargo;
        ResourceType _cargoType;
        float _timer;
        Vector3 _vel;
        public bool Busy => _state != S.Idle;

        public void Init(DroneBay bay)
        {
            _bay = bay;
            _rotors = DroneVisual.Build(transform, Palette.Orange, 0.9f);
        }

        public void ReleaseReservation()
        {
            if (_dep != null) _dep.ReservedByDrones--;
            _dep = null;
        }

        void Update()
        {
            if (!Game.IsPlaying || _bay == null) return;
            foreach (var r in _rotors) if (r != null) r.Rotate(0, 1800f * Time.deltaTime, 0, Space.Self);
            float eff = Game.Skills != null ? Game.Skills.DroneMult : 1f;
            switch (_state)
            {
                case S.Idle:
                    _timer -= Time.deltaTime;
                    if (_timer > 0f || !_bay.IsOperational) { Fly(_bay.transform.position + Vector3.up * 2.5f); break; }
                    _timer = 2f;
                    _dep = PickDeposit(_bay.WantedType()) ?? PickDeposit(null);
                    if (_dep != null) { _dep.ReservedByDrones++; _state = S.ToDeposit; }
                    break;
                case S.ToDeposit:
                    if (_dep == null || _dep.IsDepleted) { ReleaseReservation(); _state = S.Idle; break; }
                    if (Fly(_dep.transform.position + Vector3.up * 2.2f)) { _state = S.Mining; _timer = 4f / eff; }
                    break;
                case S.Mining:
                    if (_dep == null) { _state = _cargo > 0 ? S.Returning : S.Idle; break; }
                    _timer -= Time.deltaTime;
                    if (Random.value < 0.3f)
                    {
                        Game.Fx.Line(transform.position, _dep.transform.position + Vector3.up * 0.4f, new Color(1f, 0.6f, 0.2f, 0.8f), 0.03f, 0.06f);
                        Game.Fx.Emit(FxKind.Sparks, _dep.transform.position + Vector3.up * 0.4f, 1, Vector3.up);
                    }
                    Game.Noise?.EmitContinuous(_dep.transform.position, 0.3f * Time.deltaTime, NoiseSource.Mining);
                    if (_timer <= 0f)
                    {
                        _cargoType = _dep.Type;
                        _cargo = _dep.Take(Mathf.RoundToInt(6 * eff));
                        ReleaseReservation();
                        _state = S.Returning;
                    }
                    break;
                case S.Returning:
                    if (Fly(_bay.transform.position + Vector3.up * 2.5f))
                    {
                        if (_cargo > 0) Game.Bank.Add(_cargoType, _cargo);
                        _cargo = 0;
                        _state = S.Idle;
                        _timer = 0.5f;
                    }
                    break;
            }
        }

        ResourceDeposit PickDeposit(ResourceType? type)
        {
            ResourceDeposit best = null; float bd = 140f * 140f;
            foreach (var d in Registry.Deposits)
            {
                if (d == null || d.Hidden || d.IsDepleted || d.BoundExtractor != null || d.ReservedByDrones >= 2) continue;
                if (type.HasValue && d.Type != type.Value) continue;
                float dist = (d.transform.position - _bay.transform.position).sqrMagnitude;
                if (dist < bd) { bd = dist; best = d; }
            }
            return best;
        }

        bool Fly(Vector3 target)
        {
            var to = target - transform.position;
            var desired = to.magnitude > 0.4f ? to.normalized * Mathf.Min(8f, to.magnitude * 2f) : Vector3.zero;
            _vel = Vector3.Lerp(_vel, desired, Time.deltaTime * 3f);
            transform.position += _vel * Time.deltaTime;
            // держим высоту над рельефом
            float ground = Game.Grid.SampleHeight(transform.position) + 2f;
            if (transform.position.y < ground) transform.position = new Vector3(transform.position.x, ground, transform.position.z);
            var flat = new Vector3(_vel.x, 0, _vel.z);
            if (flat.sqrMagnitude > 0.1f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat), Time.deltaTime * 4f);
            return to.magnitude < 0.8f;
        }
    }
}
