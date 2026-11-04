using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    public enum SkillBranch { Engineering, Tactics, Survival }

    public enum SkillId
    {
        // Инженерия
        FastBuild, PowerEfficiency, ReinforcedWalls, FieldRepair,
        // Тактика
        TurretDamage, TurretRange, DroneAI, Fortification,
        // Выживание
        Prospector, FieldMedic, Stealth, Endurance
    }

    public class SkillDef
    {
        public SkillId Id;
        public SkillBranch Branch;
        public string Name;
        public string Description;
        public int MaxLevel = 3;
        public Rank MinRank = Rank.Specialist;
        public SkillId? Requires;
        /// <summary>Эффект за уровень: доля для процентов (0.25 = +25%) или абсолютное число (здоровье).</summary>
        public float PerLevel;
        /// <summary>Шаблон описания эффекта, {0} — суммарное значение с учётом DisplayScale.</summary>
        public string EffectFormat = "{0}";
        /// <summary>Множитель для показа: 100 — проценты, 1 — абсолютные числа.</summary>
        public float DisplayScale = 100f;

        public string Effect(int level) => BalanceData.Format(EffectFormat, Mathf.RoundToInt(level * PerLevel * DisplayScale));
    }

    /// <summary>
    /// Дерево навыков: три ветки по ГДД (Инженерия, Тактика, Выживание).
    /// Описания и сила навыков — в ассете <see cref="SkillBalance"/>; без него — значения из кода (<see cref="CodeDefaults"/>).
    /// </summary>
    public class SkillTree
    {
        public int Points { get; private set; } = StartingPoints;
        readonly Dictionary<SkillId, int> _levels = new Dictionary<SkillId, int>();

        static List<SkillDef> _defs;
        static Dictionary<SkillId, SkillDef> _map;
        static int _startingPoints = 1;

        public static List<SkillDef> Defs { get { if (_defs == null) BuildDefs(); return _defs; } }
        public static int StartingPoints { get { if (_defs == null) BuildDefs(); return _startingPoints; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _defs = null; _map = null; _startingPoints = 1; }

        static void BuildDefs()
        {
            var defs = CodeDefaults();
            var map = new Dictionary<SkillId, SkillDef>();
            foreach (var d in defs) map[d.Id] = d;
            _defs = defs;
            _map = map;
            _startingPoints = 1;
            var asset = BalanceData.Load<SkillBalance>(SkillBalance.File);
            if (asset != null) Apply(asset);
        }

        /// <summary>Перенести значения ассета. Описания навыков общие, поэтому правка во время Play действует сразу.</summary>
        public static void Apply(SkillBalance asset)
        {
            if (_map == null || asset == null) return;
            foreach (var e in asset.skills)
                if (_map.TryGetValue(e.id, out var d)) e.ApplyTo(d);
            _startingPoints = Mathf.Max(0, asset.startingPoints);
        }

        /// <summary>Значения из кода: запасной вариант без ассета и источник для генерации ассета.</summary>
        public static List<SkillDef> CodeDefaults() => new List<SkillDef>
        {
            new SkillDef { Id = SkillId.FastBuild, Branch = SkillBranch.Engineering, Name = "Скоростной монтаж", Description = "Скорость строительства", PerLevel = 0.25f, EffectFormat = "+{0}% к скорости стройки" },
            new SkillDef { Id = SkillId.PowerEfficiency, Branch = SkillBranch.Engineering, Name = "Энергоэффективность", Description = "Экономия энергии", PerLevel = 0.1f, EffectFormat = "−{0}% энергопотребления" },
            new SkillDef { Id = SkillId.ReinforcedWalls, Branch = SkillBranch.Engineering, Name = "Армирование", Description = "Прочность стен", PerLevel = 0.2f, EffectFormat = "+{0}% прочности новых стен", Requires = SkillId.FastBuild },
            new SkillDef { Id = SkillId.FieldRepair, Branch = SkillBranch.Engineering, Name = "Полевой ремонт", Description = "Скорость ремонта дронами", PerLevel = 0.3f, EffectFormat = "+{0}% к ремонту", MinRank = Rank.Lieutenant },

            new SkillDef { Id = SkillId.TurretDamage, Branch = SkillBranch.Tactics, Name = "Калибровка орудий", Description = "Урон автобашен и мин", PerLevel = 0.1f, EffectFormat = "+{0}% урона турелей" },
            new SkillDef { Id = SkillId.TurretRange, Branch = SkillBranch.Tactics, Name = "Дальние сенсоры", Description = "Радиус автобашен", PerLevel = 0.08f, EffectFormat = "+{0}% дальности" },
            new SkillDef { Id = SkillId.DroneAI, Branch = SkillBranch.Tactics, Name = "ИИ охранных дронов", Description = "Боевые и сборочные дроны эффективнее", PerLevel = 0.25f, EffectFormat = "+{0}% к работе дронов", Requires = SkillId.TurretRange },
            new SkillDef { Id = SkillId.Fortification, Branch = SkillBranch.Tactics, Name = "Доктрина обороны", Description = "Бонус к обороне построек", PerLevel = 0.08f, EffectFormat = "−{0}% урона по постройкам", MinRank = Rank.Lieutenant },

            new SkillDef { Id = SkillId.Prospector, Branch = SkillBranch.Survival, Name = "Старатель", Description = "Скорость ручной добычи", PerLevel = 0.3f, EffectFormat = "+{0}% к добыче вручную" },
            new SkillDef { Id = SkillId.FieldMedic, Branch = SkillBranch.Survival, Name = "Полевой медик", Description = "Лечение отряда", PerLevel = 0.25f, EffectFormat = "+{0}% к лечению" },
            new SkillDef { Id = SkillId.Stealth, Branch = SkillBranch.Survival, Name = "Скрытность", Description = "Меньше шума при разведке и добыче — реже случайные встречи", PerLevel = 0.15f, EffectFormat = "−{0}% шума от ваших действий", Requires = SkillId.Prospector },
            new SkillDef { Id = SkillId.Endurance, Branch = SkillBranch.Survival, Name = "Выносливость", Description = "Запас здоровья командира", PerLevel = 25f, DisplayScale = 1f, EffectFormat = "+{0} к здоровью", MinRank = Rank.Lieutenant },
        };

        public static SkillDef Def(SkillId id)
        {
            if (_map == null) BuildDefs();
            return _map.TryGetValue(id, out var d) ? d : null;
        }

        public static string BranchName(SkillBranch b) => b == SkillBranch.Engineering ? "Инженерия" : b == SkillBranch.Tactics ? "Тактика" : "Выживание";

        public int Level(SkillId id) => _levels.TryGetValue(id, out var l) ? l : 0;

        public string LockReason(SkillId id)
        {
            var d = Def(id);
            if (Level(id) >= d.MaxLevel) return "Максимум";
            if (!Game.Rank.IsAtLeast(d.MinRank)) return $"Нужен ранг: {PlayerRankManager.RankName(d.MinRank)}";
            if (d.Requires.HasValue && Level(d.Requires.Value) == 0) return $"Нужен навык «{Def(d.Requires.Value).Name}»";
            if (Points <= 0) return "Нет очков навыков";
            return null;
        }

        public bool TryUpgrade(SkillId id)
        {
            if (LockReason(id) != null) return false;
            Points--;
            _levels[id] = Level(id) + 1;
            Game.Sound?.Play2D(Sfx.Notify, 0.6f);
            if (id == SkillId.Endurance) Game.Player?.ApplySkillBonuses();
            if (id == SkillId.Fortification) ApplyFortification();
            return true;
        }

        public void AddPoints(int n) => Points += n;

        void ApplyFortification()
        {
            foreach (var s in Registry.Structures)
                if (s != null && s.Health.DamageReduction < StructureDamageReduction) s.Health.DamageReduction = StructureDamageReduction;
        }

        public void Restore(int points, Dictionary<SkillId, int> levels)
        {
            Points = points;
            _levels.Clear();
            foreach (var kv in levels) _levels[kv.Key] = kv.Value;
        }

        public Dictionary<SkillId, int> Snapshot() => new Dictionary<SkillId, int>(_levels);

        // Модификаторы, которые читают системы. Сила за уровень — SkillDef.PerLevel (ассет SkillBalance).
        float Value(SkillId id) { int l = Level(id); if (l == 0) return 0f; var d = Def(id); return d != null ? l * d.PerLevel : 0f; }

        public float BuildSpeedMult => 1f + Value(SkillId.FastBuild);
        public float EnergyUseMult => Mathf.Max(0.05f, 1f - Value(SkillId.PowerEfficiency));
        public float WallHpMult => 1f + Value(SkillId.ReinforcedWalls);
        public float RepairMult => 1f + Value(SkillId.FieldRepair);
        public float TurretDamageMult => 1f + Value(SkillId.TurretDamage);
        public float TurretRangeMult => 1f + Value(SkillId.TurretRange);
        public float DroneMult => 1f + Value(SkillId.DroneAI);
        public float StructureDamageReduction => Mathf.Clamp(Value(SkillId.Fortification), 0f, 0.9f);
        public float MiningMult => 1f + Value(SkillId.Prospector);
        public float HealMult => 1f + Value(SkillId.FieldMedic);
        public float NoiseMult => Mathf.Max(0.05f, 1f - Value(SkillId.Stealth));
        public float PlayerHpBonus => Value(SkillId.Endurance);
    }
}
