using System;
using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    /// <summary>
    /// Баланс построек. Ключ записи — <see cref="kind"/>. Модель, компонент поведения, размер и «структурные» флаги
    /// (протяжка, проходимость ворот, установка на залежь) остаются в коде: их смена ломает логику, а не баланс.
    /// </summary>
    [Serializable]
    public class BuildingEntry
    {
        [Tooltip("Название в меню строительства и в инспекторе постройки.")]
        public string name;
        [Tooltip("Тип постройки — ключ записи. Не меняйте у существующей записи.")]
        public StructureKind kind;
        [TextArea(2, 5)] public string description;
        [Tooltip("Вкладка меню строительства.")]
        public BuildCategory category;

        [Header("Прочность")]
        [Min(1f)] public float maxHp = 300f;
        [Tooltip("Плоская броня: вычитается из каждого попадания.")]
        [Min(0f)] public float flatArmor = 2f;

        [Header("Стройка")]
        public List<ResourceAmount> cost = new List<ResourceAmount>();
        [Tooltip("Секунды работы одного строительного дрона.")]
        [Min(0.1f)] public float buildTime = 10f;
        [Tooltip("Минимальный ранг. Действует вместе с допуском ниже.")]
        public Rank minRank = Rank.Specialist;
        [Tooltip("Требовать допуск (Feature). Его ранг задан в PlayerRankManager.")]
        public bool requiresFeature;
        public Feature feature;
        [Tooltip("Флаг сюжета — уникальный чертёж (например, bp_shield от генерала). Пусто — не нужен.")]
        public string requiredFlag;

        [Header("Энергия")]
        [Tooltip("Постоянное потребление. >0 — постройке нужна энергосеть.")]
        [Min(0f)] public float energyUse;
        [Min(0f)] public float energyProduce;
        [Tooltip("Радиус энергосети (реактор, энергоузлы).")]
        [Min(0f)] public float powerRadius;

        [Header("Цели для фауны")]
        [Tooltip("Прыгуны перемахивают постройку.")]
        public bool jumpable;
        [Tooltip("Мелкая постройка — цель грызунов (перегрызают кабели).")]
        public bool isSmall;
        [Tooltip("Механизм — цель прыгунов.")]
        public bool isMechanism;

        [Header("Эффекты")]
        [Min(0)] public int beds;
        [Tooltip("Прибавка к вместимости склада по каждому ресурсу.")]
        [Min(0)] public int storageBonus;
        [Tooltip("Шум в секунду при работе.")]
        [Min(0f)] public float noise;
        [Tooltip("Прибавка к морали гарнизона.")]
        public float moraleBonus;

        public static BuildingEntry From(BuildingDef d) => new BuildingEntry
        {
            name = d.Name, kind = d.Kind, description = d.Description, category = d.Category,
            maxHp = d.MaxHp, flatArmor = d.FlatArmor, cost = BalanceData.FromCost(d.Cost), buildTime = d.BuildTime,
            minRank = d.MinRank, requiresFeature = d.RequiredFeature.HasValue, feature = d.RequiredFeature ?? default, requiredFlag = d.RequiredFlag ?? "",
            energyUse = d.EnergyUse, energyProduce = d.EnergyProduce, powerRadius = d.PowerRadius,
            jumpable = d.Jumpable, isSmall = d.IsSmall, isMechanism = d.IsMechanism,
            beds = d.Beds, storageBonus = d.StorageBonus, noise = d.Noise, moraleBonus = d.MoraleBonus,
        };

        public void ApplyTo(BuildingDef d)
        {
            if (!string.IsNullOrEmpty(name)) d.Name = name;
            d.Description = description ?? "";
            d.Category = category;
            d.MaxHp = Mathf.Max(1f, maxHp);
            d.FlatArmor = Mathf.Max(0f, flatArmor);
            d.Cost = BalanceData.ToCost(cost);
            d.BuildTime = Mathf.Max(0.1f, buildTime);
            d.MinRank = minRank;
            d.RequiredFeature = requiresFeature ? feature : (Feature?)null;
            d.RequiredFlag = string.IsNullOrEmpty(requiredFlag) ? null : requiredFlag;
            d.EnergyUse = Mathf.Max(0f, energyUse);
            d.EnergyProduce = Mathf.Max(0f, energyProduce);
            d.PowerRadius = Mathf.Max(0f, powerRadius);
            d.Jumpable = jumpable;
            d.IsSmall = isSmall;
            d.IsMechanism = isMechanism;
            d.Beds = Mathf.Max(0, beds);
            d.StorageBonus = Mathf.Max(0, storageBonus);
            d.Noise = Mathf.Max(0f, noise);
            d.MoraleBonus = moraleBonus;
        }
    }

    [CreateAssetMenu(fileName = "Buildings", menuName = "Эхо-7/Баланс/Постройки")]
    public class BuildingBalance : BalanceAsset
    {
        public const string File = "Buildings";
        public override string FileName => File;

        public List<BuildingEntry> buildings = new List<BuildingEntry>();

        public override void ResetToCode()
        {
            buildings.Clear();
            foreach (var d in BuildingCatalog.CodeDefaults()) buildings.Add(BuildingEntry.From(d));
        }

        public override int AddMissingFromCode()
        {
            int added = 0;
            foreach (var d in BuildingCatalog.CodeDefaults())
                if (!buildings.Exists(e => e.kind == d.Kind)) { buildings.Add(BuildingEntry.From(d)); added++; }
            return added;
        }

        public override void Validate(List<string> problems)
        {
            BalanceData.CheckDuplicates(buildings.ConvertAll(e => e.kind), "Постройка", problems);
            foreach (StructureKind k in Enum.GetValues(typeof(StructureKind)))
                if (!buildings.Exists(e => e.kind == k)) problems.Add($"Нет записи для {k} — используются значения из кода (меню «Эхо-7 → Баланс → Создать или дополнить»).");
            foreach (var e in buildings)
            {
                string who = $"«{e.name}» ({e.kind})";
                BalanceData.CheckCost(e.cost, who, problems);
                if (e.maxHp <= 0f) problems.Add($"{who}: прочность должна быть > 0.");
                if (e.buildTime <= 0f) problems.Add($"{who}: время стройки должно быть > 0.");
                if (e.energyUse > 0f && e.energyProduce > 0f) problems.Add($"{who}: одновременно потребляет и вырабатывает энергию — проверьте.");
                if (e.kind == StructureKind.Reactor && e.powerRadius < 8f) problems.Add($"{who}: радиус реактора меньше 8 м — стартовые постройки окажутся без энергии.");
                if (e.kind == StructureKind.Pylon && e.powerRadius <= 0f) problems.Add($"{who}: энергоузел без радиуса не продлевает сеть.");
            }
        }

        public override void ApplyLive() => BuildingCatalog.Apply(this);
    }
}
