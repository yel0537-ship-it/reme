using System;
using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    /// <summary>
    /// Баланс существ. Ключ — <see cref="kind"/>. Размер (модель и хитбокс), признаки «фауна» и «дальний бой»
    /// остаются в коде: они определяют поведение и вид, а не силу существа.
    /// </summary>
    [Serializable]
    public class EnemyEntry
    {
        [Tooltip("Название в подсказках и на полоске здоровья.")]
        public string name;
        [Tooltip("Тип существа — ключ записи.")]
        public EnemyKind kind;
        [TextArea(2, 4)] public string description;

        [Header("Живучесть")]
        [Min(1f)] public float maxHp = 100f;
        [Tooltip("Класс брони: множители урона по типам — в DamageTable (Combat.cs).")]
        public ArmorClass armor = ArmorClass.Light;
        [Tooltip("Плоская броня: вычитается из каждого попадания.")]
        [Min(0f)] public float flatArmor;

        [Header("Атака и движение")]
        [Min(0.1f)] public float speed = 5f;
        [Min(0f)] public float damage = 10f;
        [Min(0.05f)] public float attackInterval = 1f;
        [Min(0.3f)] public float attackRange = 1.6f;
        [Tooltip("Множитель урона по постройкам.")]
        [Min(0f)] public float structureDamageMult = 1f;
        [Tooltip("Радиус, на котором существо замечает игрока и бойцов (0 — не агрится).")]
        [Min(0f)] public float aggro = 8f;

        [Header("Волны")]
        [Tooltip("«Цена» в бюджете волны: чем дороже, тем реже и меньше их в волне.")]
        [Min(1)] public int waveCost = 1;
        [Tooltip("Появляется в волнах с этого ранга командира.")]
        public Rank minRank = Rank.Specialist;
        [Tooltip("Появляется в волнах с этого дня.")]
        [Min(1)] public int minDay = 1;
        [Tooltip("Флаг сюжета, без которого существо не приходит (синты — ai_factory_active). Пусто — не нужен.")]
        public string requiredFlag;

        [Header("Добыча и звук")]
        [Tooltip("Пайки за тушу (охота).")]
        [Min(0)] public int rations;
        public Sfx attackSound = Sfx.Screech;

        public static EnemyEntry From(EnemyArchetype a) => new EnemyEntry
        {
            name = a.Name, kind = a.Kind, description = a.Description,
            maxHp = a.MaxHp, armor = a.Armor, flatArmor = a.FlatArmor,
            speed = a.Speed, damage = a.Damage, attackInterval = a.AttackInterval, attackRange = a.AttackRange,
            structureDamageMult = a.StructureDamageMult, aggro = a.Aggro,
            waveCost = a.Cost, minRank = a.MinRank, minDay = a.MinDay, requiredFlag = a.RequiredFlag ?? "",
            rations = a.Rations, attackSound = a.AttackSound,
        };

        public void ApplyTo(EnemyArchetype a)
        {
            if (!string.IsNullOrEmpty(name)) a.Name = name;
            a.Description = description ?? "";
            a.MaxHp = Mathf.Max(1f, maxHp);
            a.Armor = armor;
            a.FlatArmor = Mathf.Max(0f, flatArmor);
            a.Speed = Mathf.Max(0.1f, speed);
            a.Damage = Mathf.Max(0f, damage);
            a.AttackInterval = Mathf.Max(0.05f, attackInterval);
            a.AttackRange = Mathf.Max(0.3f, attackRange);
            a.StructureDamageMult = Mathf.Max(0f, structureDamageMult);
            a.Aggro = Mathf.Max(0f, aggro);
            a.Cost = Mathf.Max(1, waveCost);
            a.MinRank = minRank;
            a.MinDay = Mathf.Max(1, minDay);
            a.RequiredFlag = string.IsNullOrEmpty(requiredFlag) ? null : requiredFlag;
            a.Rations = Mathf.Max(0, rations);
            a.AttackSound = attackSound;
        }
    }

    [CreateAssetMenu(fileName = "Enemies", menuName = "Эхо-7/Баланс/Существа")]
    public class EnemyBalance : BalanceAsset
    {
        public const string File = "Enemies";
        public override string FileName => File;

        public List<EnemyEntry> enemies = new List<EnemyEntry>();

        public override void ResetToCode()
        {
            enemies.Clear();
            foreach (var a in EnemyArchetype.CodeDefaults()) enemies.Add(EnemyEntry.From(a));
        }

        public override int AddMissingFromCode()
        {
            int added = 0;
            foreach (var a in EnemyArchetype.CodeDefaults())
                if (!enemies.Exists(e => e.kind == a.Kind)) { enemies.Add(EnemyEntry.From(a)); added++; }
            return added;
        }

        public override void Validate(List<string> problems)
        {
            BalanceData.CheckDuplicates(enemies.ConvertAll(e => e.kind), "Существо", problems);
            foreach (EnemyKind k in Enum.GetValues(typeof(EnemyKind)))
                if (!enemies.Exists(e => e.kind == k)) problems.Add($"Нет записи для {k} — используются значения из кода.");
            foreach (var e in enemies)
            {
                string who = $"«{e.name}» ({e.kind})";
                if (e.maxHp <= 0f) problems.Add($"{who}: здоровье должно быть > 0.");
                if (e.speed <= 0f) problems.Add($"{who}: скорость должна быть > 0.");
                if (e.kind == EnemyKind.Tank && e.waveCost < 10) problems.Add($"{who}: «цена» танка в волне меньше 10 — танков станет очень много.");
            }
        }

        public override void ApplyLive() => EnemyArchetype.Apply(this);
    }
}
