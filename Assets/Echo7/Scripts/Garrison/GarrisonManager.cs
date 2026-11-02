using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    /// <summary>
    /// Гарнизон и мораль. Мораль зависит от тесноты (койки), еды и воды, жары, болезней, потерь, побед
    /// и разговоров с командиром. Низкая мораль → отказ от постов, саботаж, дезертирство; у расчётов — падает точность турелей.
    /// </summary>
    public class GarrisonManager : MonoBehaviour
    {
        public GridPathfinder Pathfinder { get; private set; }
        public float AverageMorale { get; private set; } = 70f;
        public int Beds { get; private set; }
        public int PresentCount { get; private set; }
        public float FoodFactor { get; private set; } = 1f;
        public float WaterFactor { get; private set; } = 1f;
        public float Shock { get; private set; }  // временный общий модификатор (победы/потери)

        static readonly string[] Names =
        {
            "Петренко", "Ибрагимова", "Сато", "Мартинес", "Ковалёв", "Ли", "Нгуен", "Орлов", "Кузьмина", "Дюбуа",
            "Окафор", "Шмидт", "Рахими", "Волкова", "Тан", "Андерсен", "Гарсия", "Белова", "Хан", "Морозов"
        };
        int _nameIdx;

        public IReadOnlyList<GarrisonNpc> All => Registry.Npcs;

        public void Init()
        {
            Pathfinder = new GridPathfinder(Game.Grid);
            GameEvents.HourPassed += OnHour;
            GameEvents.DayStarted += OnDay;
            GameEvents.WaveEnded += w => { Shock += 6f; ForAll(n => n.AddMorale(3f)); };
            GameEvents.WaveStarted += w => ForAll(n => n.AddMorale(-2f));
            GameEvents.NpcDied += n => { Shock -= 12f; ForAll(o => o.AddMorale(-6f)); };
            GameEvents.StructureDestroyed += s => { if (s is Habitat) { Shock -= 5f; } };
            GameEvents.PlayerDied += () => ForAll(n => n.AddMorale(-5f));
        }

        void ForAll(System.Action<GarrisonNpc> a)
        {
            foreach (var n in Registry.Npcs.ToArray()) if (n != null && !n.Health.IsDead) a(n);
        }

        public void SpawnStarting(Vector3 near)
        {
            Spawn(NpcRole.Soldier, near + new Vector3(2, 0, 3));
            Spawn(NpcRole.Engineer, near + new Vector3(-2, 0, 3));
            Spawn(NpcRole.Medic, near + new Vector3(0, 0, 4));
        }

        public GarrisonNpc Spawn(NpcRole role, Vector3 pos, string name = null)
        {
            if (name == null) { name = Names[_nameIdx % Names.Length]; _nameIdx++; }
            return GarrisonNpc.Spawn(name, role, pos);
        }

        public void Recruit(int count)
        {
            var at = Game.World.LandingZone;
            var pad = StructureLookup.FirstOf(StructureKind.LandingPad);
            if (pad != null) at = pad.transform.position + Vector3.right * 5f;
            for (int i = 0; i < count; i++)
            {
                var role = Random.value < 0.6f ? NpcRole.Soldier : (Random.value < 0.5f ? NpcRole.Engineer : NpcRole.Medic);
                var n = Spawn(role, at + new Vector3(i * 1.5f, 0, 2f));
                n.Morale = 65f;
                Game.Log?.Info($"Прибыло пополнение: {n.DisplayName}.");
            }
        }

        /// <summary>Сообщение от системы снабжения: доля удовлетворения еды и воды за последний час.</summary>
        public void ReportSupply(float food, float water)
        {
            FoodFactor = Mathf.Lerp(FoodFactor, food, 0.5f);
            WaterFactor = Mathf.Lerp(WaterFactor, water, 0.5f);
            if (food < 0.5f && Random.value < 0.2f) Game.Log?.Warn("Гарнизон голодает — закончились пайки!");
            if (water < 0.5f && Random.value < 0.2f) Game.Log?.Warn("Нет воды! Постройте влагоконденсаторы.");
        }

        void Update()
        {
            int beds = 0;
            foreach (var s in Registry.Structures) if (s != null && s.IsBuilt && s.Def.Beds > 0) beds += s.Def.Beds;
            Beds = beds;
            int present = 0; float sum = 0f;
            foreach (var n in Registry.Npcs)
            {
                if (n == null || n.Health.IsDead || n.IsAway) continue;
                present++;
                sum += n.Morale;
            }
            PresentCount = present;
            AverageMorale = present > 0 ? sum / present : 0f;
        }

        void OnHour(int hour)
        {
            Shock = Mathf.MoveTowards(Shock, 0f, 1.5f);
            int present = PresentCount;
            float housing = present <= Beds ? 8f : -6f - (present - Beds) * 4f;
            float food = FoodFactor > 0.9f ? 4f : FoodFactor < 0.5f ? -20f : -6f;
            float water = WaterFactor > 0.9f ? 2f : WaterFactor < 0.5f ? -20f : -6f;
            float heat = Game.Weather != null && Game.Weather.IsHot ? -6f : 0f;
            float danger = Game.Defense != null && Game.Defense.Alert ? -4f : 0f;
            foreach (var n in Registry.Npcs.ToArray())
            {
                if (n == null || n.Health.IsDead || n.IsAway) continue;
                float sick = n.Sickness > 0.2f ? -15f : 0f;
                float target = 58f + n.Personality + housing + food + water + heat + danger + sick + Shock;
                target = Mathf.Clamp(target, 0f, 100f);
                n.Morale = Mathf.MoveTowards(n.Morale, target, 3f);
                // саботаж от отчаяния
                if (n.Morale < 8f && Random.value < 0.04f) n.Sabotage();
            }
        }

        void OnDay(int day)
        {
            foreach (var n in Registry.Npcs.ToArray())
            {
                if (n == null || n.Health.IsDead || n.IsAway) continue;
                if (n.Morale < 12f && Random.value < 0.35f) n.Desert();
            }
        }

        public List<Turret> FreeTurrets()
        {
            var list = new List<Turret>();
            foreach (var t in Registry.Turrets) if (t != null && t.IsBuilt && (t.Crew == null || t.Crew.Health.IsDead)) list.Add(t);
            return list;
        }

        /// <summary>Назначить на ближайшую свободную турель (кнопка в окне гарнизона).</summary>
        public void AutoAssign(GarrisonNpc n)
        {
            Turret best = null; float bd = float.MaxValue;
            foreach (var t in FreeTurrets())
            {
                float d = (t.transform.position - n.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = t; }
            }
            if (best != null) Game.Defense.AssignCrew(best, n);
            else Game.Log?.Warn("Нет свободных турелей.");
        }

        public string MoraleWord(float m) => m >= 75 ? "высокая" : m >= 50 ? "нормальная" : m >= 25 ? "низкая" : "критическая";
    }
}
