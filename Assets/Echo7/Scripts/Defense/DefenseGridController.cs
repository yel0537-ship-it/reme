using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    /// <summary>
    /// Контроллер обороны: выбор целей турелями (без перебора всех по физике), бонусы сенсоров,
    /// назначение расчётов из гарнизона, уровень тревоги базы.
    /// </summary>
    public class DefenseGridController : MonoBehaviour
    {
        public bool Alert { get; private set; }
        public int HostilesNearBase { get; private set; }
        float _scan;
        readonly Dictionary<Enemy, int> _assigned = new Dictionary<Enemy, int>();

        public IReadOnlyList<Turret> Turrets => Registry.Turrets;

        void Update()
        {
            if (!Game.IsPlaying) return;
            _scan -= Time.deltaTime;
            if (_scan > 0f) return;
            _scan = 0.5f;
            _assigned.Clear();
            var c = Game.Reactor != null ? Game.Reactor.transform.position : Game.World.Center;
            float r = (Game.Rank != null ? Game.Rank.BuildRadius : 40f) + 12f;
            int n = 0;
            foreach (var e in Registry.Enemies)
                if (e != null && !e.IsDead && e.IsHostile && (e.transform.position - c).sqrMagnitude < r * r) n++;
            bool was = Alert;
            HostilesNearBase = n;
            Alert = n > 0;
            if (Alert && !was) Game.Sound?.Play2D(Sfx.Alarm, 0.5f);
        }

        /// <summary>
        /// Выбор цели: лазеры и тяжёлые орудия предпочитают бронированных (танков), пулемёты — ближайших лёгких.
        /// Учитывается «перегруз» — сколько турелей уже ведут цель.
        /// </summary>
        public Enemy PickTarget(Turret t)
        {
            float range = t.RangeEffective;
            float r2 = range * range;
            var p = t.transform.position;
            Enemy best = null;
            float bestScore = float.MinValue;
            foreach (var e in Registry.Enemies)
            {
                if (e == null || e.IsDead || !e.IsHostileOrAggressive) continue;
                float d2 = (e.transform.position - p).sqrMagnitude;
                if (d2 > r2) continue;
                float score = -Mathf.Sqrt(d2);
                bool heavy = e.Archetype.Armor == ArmorClass.Heavy;
                if (t.Profile.PreferHeavy && heavy) score += 30f;
                if (!t.Profile.PreferHeavy && e.Archetype.Armor == ArmorClass.Light) score += 6f;
                if (e.IsAttackingStructure) score += 8f;           // прежде всего — тех, кто грызёт базу
                if (e.Archetype.Kind == EnemyKind.Tank) score += 10f; // танк идёт к реактору — фокус огня
                _assigned.TryGetValue(e, out int already);
                score -= already * 4f;
                if (score > bestScore) { bestScore = score; best = e; }
            }
            if (best != null)
            {
                _assigned.TryGetValue(best, out int a);
                _assigned[best] = a + 1;
            }
            return best;
        }

        /// <summary>+15% дальности, если рядом работает сенсорная мачта.</summary>
        public float SensorBonus(Turret t)
        {
            foreach (var s in Registry.Structures)
            {
                if (s == null || s.Def.Kind != StructureKind.Sensor || !s.IsOperational) continue;
                if ((s.transform.position - t.transform.position).sqrMagnitude < 400f) return 1.15f;
            }
            return 1f;
        }

        public bool HasSensors
        {
            get
            {
                foreach (var s in Registry.Structures) if (s != null && s.Def.Kind == StructureKind.Sensor && s.IsOperational) return true;
                return false;
            }
        }

        /// <summary>Назначить NPC расчётом турели.</summary>
        public void AssignCrew(Turret t, GarrisonNpc npc)
        {
            if (t == null || npc == null) return;
            if (t.Crew != null && t.Crew != npc) t.Crew.Unassign();
            npc.AssignTo(t);
            t.Crew = npc;
            t.LostCrew = false;
            Game.Log?.Info($"{npc.DisplayName} назначен(а) расчётом: {t.Def.Name}");
        }

        public void ClearCrew(Turret t)
        {
            if (t?.Crew == null) return;
            t.Crew.Unassign();
            t.Crew = null;
        }

        /// <summary>Расчёт погиб — турель переходит в автономный режим.</summary>
        public void OnCrewDied(Turret t)
        {
            if (t == null) return;
            t.Crew = null;
            t.LostCrew = true;
            Game.Log?.Warn($"{t.Def.Name}: расчёт погиб, турель перешла в автономный (сниженный) режим.");
        }

        public float TotalDps
        {
            get
            {
                float dps = 0f;
                foreach (var t in Registry.Turrets)
                {
                    if (t == null || !t.IsOperational) continue;
                    dps += t.Profile.Damage / t.Profile.FireInterval * t.Profile.Accuracy * t.AccuracyMult * t.FireRateMult;
                }
                return dps;
            }
        }
    }
}
