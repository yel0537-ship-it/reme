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
        public string Description;   // {0} — суммарный эффект
        public int MaxLevel = 3;
        public Rank MinRank = Rank.Specialist;
        public SkillId? Requires;
        public System.Func<int, string> Effect;
    }

    /// <summary>Дерево навыков: три ветки по ГДД (Инженерия, Тактика, Выживание).</summary>
    public class SkillTree
    {
        public int Points { get; private set; } = 1;
        readonly Dictionary<SkillId, int> _levels = new Dictionary<SkillId, int>();
        public static readonly List<SkillDef> Defs = new List<SkillDef>
        {
            new SkillDef { Id = SkillId.FastBuild, Branch = SkillBranch.Engineering, Name = "Скоростной монтаж", Description = "Скорость строительства", Effect = l => $"+{l * 25}% к скорости стройки" },
            new SkillDef { Id = SkillId.PowerEfficiency, Branch = SkillBranch.Engineering, Name = "Энергоэффективность", Description = "Экономия энергии", Effect = l => $"−{l * 10}% энергопотребления" },
            new SkillDef { Id = SkillId.ReinforcedWalls, Branch = SkillBranch.Engineering, Name = "Армирование", Description = "Прочность стен", Effect = l => $"+{l * 20}% прочности новых стен", Requires = SkillId.FastBuild },
            new SkillDef { Id = SkillId.FieldRepair, Branch = SkillBranch.Engineering, Name = "Полевой ремонт", Description = "Скорость ремонта дронами", Effect = l => $"+{l * 30}% к ремонту", MinRank = Rank.Lieutenant },

            new SkillDef { Id = SkillId.TurretDamage, Branch = SkillBranch.Tactics, Name = "Калибровка орудий", Description = "Урон автобашен и мин", Effect = l => $"+{l * 10}% урона турелей" },
            new SkillDef { Id = SkillId.TurretRange, Branch = SkillBranch.Tactics, Name = "Дальние сенсоры", Description = "Радиус автобашен", Effect = l => $"+{l * 8}% дальности" },
            new SkillDef { Id = SkillId.DroneAI, Branch = SkillBranch.Tactics, Name = "ИИ охранных дронов", Description = "Боевые и сборочные дроны эффективнее", Effect = l => $"+{l * 25}% к работе дронов", Requires = SkillId.TurretRange },
            new SkillDef { Id = SkillId.Fortification, Branch = SkillBranch.Tactics, Name = "Доктрина обороны", Description = "Бонус к обороне построек", Effect = l => $"−{l * 8}% урона по постройкам", MinRank = Rank.Lieutenant },

            new SkillDef { Id = SkillId.Prospector, Branch = SkillBranch.Survival, Name = "Старатель", Description = "Скорость ручной добычи", Effect = l => $"+{l * 30}% к добыче вручную" },
            new SkillDef { Id = SkillId.FieldMedic, Branch = SkillBranch.Survival, Name = "Полевой медик", Description = "Лечение отряда", Effect = l => $"+{l * 25}% к лечению" },
            new SkillDef { Id = SkillId.Stealth, Branch = SkillBranch.Survival, Name = "Скрытность", Description = "Меньше шума при разведке и добыче — реже случайные встречи", Effect = l => $"−{l * 15}% шума от ваших действий", Requires = SkillId.Prospector },
            new SkillDef { Id = SkillId.Endurance, Branch = SkillBranch.Survival, Name = "Выносливость", Description = "Запас здоровья командира", Effect = l => $"+{l * 25} к здоровью", MinRank = Rank.Lieutenant },
        };

        public static SkillDef Def(SkillId id) => Defs.Find(d => d.Id == id);
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

        // Модификаторы, которые читают системы
        public float BuildSpeedMult => 1f + 0.25f * Level(SkillId.FastBuild);
        public float EnergyUseMult => 1f - 0.1f * Level(SkillId.PowerEfficiency);
        public float WallHpMult => 1f + 0.2f * Level(SkillId.ReinforcedWalls);
        public float RepairMult => 1f + 0.3f * Level(SkillId.FieldRepair);
        public float TurretDamageMult => 1f + 0.1f * Level(SkillId.TurretDamage);
        public float TurretRangeMult => 1f + 0.08f * Level(SkillId.TurretRange);
        public float DroneMult => 1f + 0.25f * Level(SkillId.DroneAI);
        public float StructureDamageReduction => 0.08f * Level(SkillId.Fortification);
        public float MiningMult => 1f + 0.3f * Level(SkillId.Prospector);
        public float HealMult => 1f + 0.25f * Level(SkillId.FieldMedic);
        public float NoiseMult => 1f - 0.15f * Level(SkillId.Stealth);
        public float PlayerHpBonus => 25f * Level(SkillId.Endurance);
    }
}
