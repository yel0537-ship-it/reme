using System;
using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    /// <summary>
    /// Шаблон задачи Центра. Цель и награда растут с рангом: значение + «за ранг» × ранг (Специалист = 0, дробная часть отбрасывается).
    /// Как засчитывается прогресс, определяет <see cref="type"/> (код в CenterTaskBoard).
    /// </summary>
    [Serializable]
    public class TaskTemplate
    {
        [Tooltip("Заголовок задачи. Должен быть уникальным — по нему Центр не выдаёт одну задачу дважды.")]
        public string title;
        [TextArea(2, 4)] public string description;
        [Tooltip("Что засчитывается: постройки, добыча, ночи, волны, убийства, охота, мораль (часы), профицит энергии (часы), пещеры, разведка, запас ресурса.")]
        public TaskType type;

        [Header("Условие")]
        [Tooltip("Для «Build»: какую постройку считать.")]
        public StructureKind buildKind;
        [Tooltip("Для «Build»: считать любую стену/ограждение/ворота вместо конкретной постройки.")]
        public bool anyWall;
        [Tooltip("Для «Collect» и «Stockpile»: ресурс.")]
        public ResourceType resource;
        [Min(1)] public int target = 1;
        [Tooltip("Прибавка к цели за каждый ранг выше Специалиста (можно дробно: 0.5 = +1 за два ранга).")]
        public float targetPerRank;
        [Tooltip("Срок в днях от выдачи (0 — без срока).")]
        [Min(0)] public int deadlineDays = 3;

        [Header("Награда")]
        [Min(0)] public int rewardRp = 30;
        [Tooltip("Прибавка очков ранга за каждый ранг выше Специалиста.")]
        public int rewardRpPerRank;
        [Tooltip("Прибавка репутации в Центре.")]
        public int rewardRep = 2;
        [Tooltip("Внеплановая капсула при выполнении.")]
        public List<ResourceAmount> rewardSupplies = new List<ResourceAmount>();

        [Header("Только для случайных задач")]
        [Tooltip("Задача появляется с этого ранга.")]
        public Rank minRank = Rank.Specialist;
        [Tooltip("Вес при случайном выборе: 2 — вдвое чаще, 0 — никогда.")]
        [Min(0f)] public float weight = 1f;

        public CenterTask Create(int day, int rank) => new CenterTask
        {
            Title = title, Description = description, Type = type, BuildKind = buildKind, AnyWall = anyWall, Resource = resource,
            Target = Mathf.Max(1, target + Mathf.FloorToInt(targetPerRank * rank)),
            DeadlineDay = deadlineDays > 0 ? day + deadlineDays : 0,
            RewardRp = Mathf.Max(0, rewardRp + rewardRpPerRank * rank),
            RewardRep = rewardRep,
            RewardSupplies = BalanceData.ToCost(rewardSupplies),
        };

        public static TaskTemplate Make(string title, string desc, TaskType type, int target, int deadlineDays, int rp, int rep,
            StructureKind buildKind = default, bool anyWall = false, ResourceType resource = default, float targetPerRank = 0f, int rpPerRank = 0,
            Rank minRank = Rank.Specialist, params ResourceAmount[] supplies) => new TaskTemplate
        {
            title = title, description = desc, type = type, target = target, deadlineDays = deadlineDays, rewardRp = rp, rewardRep = rep,
            buildKind = buildKind, anyWall = anyWall, resource = resource, targetPerRank = targetPerRank, rewardRpPerRank = rpPerRank,
            minRank = minRank, rewardSupplies = new List<ResourceAmount>(supplies),
        };
    }

    [CreateAssetMenu(fileName = "Tasks", menuName = "Эхо-7/Баланс/Задачи Центра")]
    public class TaskBalance : BalanceAsset
    {
        public const string File = "Tasks";
        public override string FileName => File;

        [Header("Доска задач")]
        [Tooltip("Сколько задач активно одновременно.")]
        [Min(1)] public int maxActive = 3;
        [Tooltip("Сколько задач активно с ранга Капитан.")]
        [Min(1)] public int maxActiveFromCaptain = 4;
        [Tooltip("Минус репутации за проваленную к сроку задачу.")]
        [Min(0)] public int failReputationPenalty = 6;
        [Tooltip("Каждые N выполненных задач дают очко навыков (0 — не давать).")]
        [Min(0)] public int tasksPerSkillPoint = 2;
        [Tooltip("Порог средней морали для задач типа KeepMorale.")]
        [Range(0f, 100f)] public float moraleThreshold = 60f;
        [Tooltip("Порог профицита энергии для задач типа EnergySurplus.")]
        public float energySurplusThreshold = 10f;

        [Header("Стартовые задачи (выдаются по порядку)")]
        [Tooltip("Сколько первых стартовых задач пропустить после пролога: их механики уже пройдены в обучении.")]
        [Min(0)] public int skipAfterPrologue = 4;
        public List<TaskTemplate> starter = new List<TaskTemplate>();

        [Header("Случайные задачи (после стартовых)")]
        public List<TaskTemplate> pool = new List<TaskTemplate>();

        public int MaxActiveFor(Rank r) => r >= Rank.Captain ? maxActiveFromCaptain : maxActive;

        public override void ResetToCode()
        {
            maxActive = 3; maxActiveFromCaptain = 4; failReputationPenalty = 6; tasksPerSkillPoint = 2;
            moraleThreshold = 60f; energySurplusThreshold = 10f; skipAfterPrologue = 4;
            const ResourceType Al = ResourceType.Alloys, Am = ResourceType.Ammo, Ra = ResourceType.Rations, Cr = ResourceType.Credits;
            starter = new List<TaskTemplate>
            {
                TaskTemplate.Make("Периметр", "Постройте 8 секций стен или ограждений вокруг реактора. [B] — меню, протяжка мышью.", TaskType.Build, 8, 2, 25, 3,
                    anyWall: true, supplies: new[] { new ResourceAmount(Al, 25) }),
                TaskTemplate.Make("Местные ресурсы", "Центр не прислал титан. Добудьте 20 титана вручную (клик по залежи) или дронами.", TaskType.Collect, 20, 2, 30, 2,
                    resource: ResourceType.Titanium, supplies: new[] { new ResourceAmount(Am, 60) }),
                TaskTemplate.Make("Огневая точка", "Постройте ещё один пулемётный узел и назначьте расчёт в окне гарнизона [G].", TaskType.Build, 1, 2, 25, 2,
                    buildKind: StructureKind.MgTurret),
                TaskTemplate.Make("Пережить ночь", "Удержите аванпост до рассвета.", TaskType.SurviveNights, 1, 0, 35, 4,
                    supplies: new[] { new ResourceAmount(Ra, 20), new ResourceAmount(Am, 40) }),
                TaskTemplate.Make("Автоматизация", "Постройте дронпорт — дроны сами носят местные ресурсы и шумят меньше бура.", TaskType.Build, 1, 3, 30, 2,
                    buildKind: StructureKind.DroneBay),
                TaskTemplate.Make("Энергобаланс", "Держите профицит энергии ≥10 в течение 6 часов (солнечные панели, узлы).", TaskType.EnergySurplus, 6, 3, 30, 2),
            };
            pool = new List<TaskTemplate>
            {
                TaskTemplate.Make("Отразить натиск", "Отразите волны фауны.", TaskType.RepelWaves, 2, 3, 40, 3, targetPerRank: 0.5f, rpPerRank: 10),
                TaskTemplate.Make("Зачистка", "Уничтожьте враждебных особей.", TaskType.KillHostiles, 25, 3, 35, 2, targetPerRank: 10, rpPerRank: 8,
                    supplies: new[] { new ResourceAmount(Am, 80) }),
                TaskTemplate.Make("Охота", "Пайков мало. Добудьте мясо рогачей.", TaskType.Hunt, 3, 2, 20, 1,
                    supplies: new[] { new ResourceAmount(Ra, 15) }),
                TaskTemplate.Make("Кристаллы для лабораторий", "Добудьте кристаллы.", TaskType.Collect, 20, 3, 40, 3,
                    resource: ResourceType.Crystals, targetPerRank: 10, rpPerRank: 10, supplies: new[] { new ResourceAmount(Cr, 60) }),
                TaskTemplate.Make("Титан для верфей", "Добудьте титан.", TaskType.Collect, 30, 3, 35, 3,
                    resource: ResourceType.Titanium, targetPerRank: 10, rpPerRank: 10, supplies: new[] { new ResourceAmount(Al, 40) }),
                TaskTemplate.Make("Боевой дух", "Мораль гарнизона ≥ 60 в течение 8 часов.", TaskType.KeepMorale, 8, 3, 30, 3),
                TaskTemplate.Make("Запас воды", "Накопите 120 воды перед сезоном пекла.", TaskType.Stockpile, 120, 3, 30, 2, resource: ResourceType.Water),
                TaskTemplate.Make("Укрепление", "Постройте ещё стен.", TaskType.Build, 10, 2, 25, 2,
                    anyWall: true, targetPerRank: 4, rpPerRank: 5, supplies: new[] { new ResourceAmount(Al, 30) }),
                TaskTemplate.Make("Глубинная разведка", "Подорвите завал одной из пещер — там богатые жилы.", TaskType.OpenCave, 1, 4, 50, 3),
                TaskTemplate.Make("Геологоразведка", "Найдите скрытые залежи геологическим зондом.", TaskType.Survey, 1, 3, 40, 3, minRank: Rank.Lieutenant),
                TaskTemplate.Make("Лазерный рубеж", "Постройте лазерный излучатель против бронированных тварей.", TaskType.Build, 1, 3, 40, 2,
                    buildKind: StructureKind.LaserTurret, minRank: Rank.Lieutenant),
                TaskTemplate.Make("Посадочная площадка", "Подготовьте приём орбитальных грузов.", TaskType.Build, 1, 4, 60, 4,
                    buildKind: StructureKind.LandingPad, minRank: Rank.Captain),
                TaskTemplate.Make("Космопорт", "Постройте космопорт для полётов на орбиту.", TaskType.Build, 1, 6, 120, 6,
                    buildKind: StructureKind.Spaceport, minRank: Rank.Major),
            };
        }

        public override int AddMissingFromCode()
        {
            if (starter.Count > 0 || pool.Count > 0) return 0;   // содержимое задач принадлежит дизайнеру — дополняем только пустой ассет
            ResetToCode();
            return starter.Count + pool.Count;
        }

        public override void Validate(List<string> problems)
        {
            if (pool.Count == 0) problems.Add("Пул случайных задач пуст — после стартовых Центр перестанет выдавать задачи.");
            else if (!pool.Exists(t => t.weight > 0f && t.minRank == Rank.Specialist)) problems.Add("В пуле нет задач для ранга Специалист с весом > 0.");
            if (skipAfterPrologue > starter.Count) problems.Add($"Пропуск после пролога ({skipAfterPrologue}) больше числа стартовых задач ({starter.Count}).");
            if (maxActiveFromCaptain < maxActive) problems.Add("С ранга Капитан задач меньше, чем до него — проверьте.");
            var titles = new HashSet<string>();
            foreach (var t in starter) Check(t, "Стартовая", problems, titles);
            foreach (var t in pool) Check(t, "Случайная", problems, titles);
        }

        static void Check(TaskTemplate t, string kind, List<string> problems, HashSet<string> titles)
        {
            string who = $"{kind} задача «{t.title}»";
            if (string.IsNullOrEmpty(t.title)) problems.Add($"{kind} задача без заголовка.");
            else if (!titles.Add(t.title)) problems.Add($"{who}: заголовок повторяется — Центр может выдать её не так, как задумано.");
            if (t.target < 1) problems.Add($"{who}: цель должна быть ≥ 1.");
            if (t.type == TaskType.Collect && t.resource != ResourceType.Titanium && t.resource != ResourceType.Crystals && t.resource != ResourceType.Silicon)
                problems.Add($"{who}: «Collect» считает только добычу из залежей (титан, кристаллы, кремний). Для запаса используйте «Stockpile».");
            if (t.type == TaskType.SurviveNights && t.deadlineDays > 0 && t.deadlineDays < t.target)
                problems.Add($"{who}: ночей больше, чем дней до срока — задача невыполнима.");
            BalanceData.CheckCost(t.rewardSupplies, who, problems);
        }

        public override void ApplyLive() { /* доска читает ассет при каждой выдаче задачи */ }
    }
}
