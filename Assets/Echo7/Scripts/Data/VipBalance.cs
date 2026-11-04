using System;
using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    /// <summary>Что высокий чин просит у аванпоста: «лучшее», «похуже» или отказ.</summary>
    [Serializable]
    public class VipRequestEntry
    {
        [Tooltip("Короткое название запроса (кнопки диалога).")]
        public string name;
        public string description;
        [Tooltip("Цена варианта «выдать лучшее». Количество может расти с рангом командира.")]
        public List<RankedAmount> best = new List<RankedAmount>();
        [Tooltip("Цена варианта «выдать что похуже».")]
        public List<RankedAmount> junk = new List<RankedAmount>();
        [Tooltip("Вариант «лучшее» отправляет бойца гарнизона с миссией (он вернётся или погибнет).")]
        public bool needsNpc;
        public NpcRole npcRole = NpcRole.Soldier;

        public VipRequest Create(int rank) => new VipRequest
        {
            Name = name, Description = description,
            BestCost = BalanceData.ToCost(best, rank), JunkCost = BalanceData.ToCost(junk, rank),
            NeedsNpc = needsNpc, NpcRole = npcRole,
        };
    }

    /// <summary>Визит высокого чина: кто прилетает, зачем и что даёт за хорошее обеспечение.</summary>
    [Serializable]
    public class VipVisitEntry
    {
        [Tooltip("Подпись записи (для удобства).")]
        public string label;
        [Tooltip("Должность: «Генерал», «Инспектор снабжения»…")]
        public string title;
        [Tooltip("Фамилия.")]
        public string personName;
        [Tooltip("Символ на портрете в диалоге.")]
        public string portrait = "?";
        [TextArea(3, 6)] public string mission;

        [Header("Когда прилетает")]
        public Rank minRank = Rank.Specialist;
        [Tooltip("Вес при случайном выборе визита: 2 — вдвое чаще, 0 — никогда.")]
        [Min(0f)] public float weight = 1f;

        [Header("Запросы")]
        public List<VipRequestEntry> requests = new List<VipRequestEntry>();

        [Header("Уникальная награда (успех и обеспечение не хуже порога)")]
        [Tooltip("Флаг сюжета: bp_shield — чертёж щита, market_closed — закрытый рынок станции. Любой новый флаг можно проверять в коде.")]
        public string rewardFlag;
        [Tooltip("Сообщение в журнал при получении награды.")]
        public string rewardMessage;
        [Tooltip("Кредиты вместе с наградой.")]
        [Min(0)] public int rewardCredits;

        public VipVisit Create(int rank)
        {
            var v = new VipVisit
            {
                Title = title, Name = personName, Mission = mission, Portrait = string.IsNullOrEmpty(portrait) ? "?" : portrait,
                RewardFlag = string.IsNullOrEmpty(rewardFlag) ? null : rewardFlag, RewardMessage = rewardMessage, RewardCredits = rewardCredits,
            };
            foreach (var r in requests) if (r != null) v.Requests.Add(r.Create(rank));
            return v;
        }
    }

    /// <summary>
    /// Баланс визитов высоких чинов: расписание, сами визиты и последствия выбора (репутация, шанс успеха, трибунал).
    /// Читается при каждом визите, поэтому правка во время Play действует со следующего события.
    /// </summary>
    [CreateAssetMenu(fileName = "Vips", menuName = "Эхо-7/Баланс/Визиты высоких чинов")]
    public class VipBalance : BalanceAsset
    {
        public const string File = "Vips";
        public override string FileName => File;

        [Header("Расписание")]
        [Min(1)] public int firstVisitDay = 3;
        [Tooltip("Дней между визитами: случайно от «мин» до «макс» включительно.")]
        [Min(1)] public int intervalMinDays = 3;
        [Min(1)] public int intervalMaxDays = 5;
        [Range(0, 23)] public int arrivalHour = 10;
        [Tooltip("Сколько часов гость ждёт встречи, прежде чем улететь (считается отказом).")]
        [Min(1f)] public float patienceHours = 20f;
        [Tooltip("Длительность миссии: от «мин» до «макс» часов, затем приходит исход.")]
        [Min(1f)] public float missionHoursMin = 24f;
        [Min(1f)] public float missionHoursMax = 35f;
        [Tooltip("Первый визит всегда — первая запись списка (знакомство с механикой).")]
        public bool firstVisitIsFirstEntry = true;

        [Header("Отъезд гостя")]
        [Tooltip("Вклад «похуже» в качество обеспечения (лучшее = 1, отказ = 0).")]
        [Range(0f, 1f)] public float junkQuality = 0.3f;
        [Tooltip("Репутация при полном отказе (плюс пропуск следующей поставки).")]
        [Min(0)] public int refuseReputationPenalty = 14;
        [Tooltip("Ниже этого качества гость уезжает недовольным.")]
        [Range(0f, 1f)] public float stingyQuality = 0.5f;
        [Min(0)] public int stingyReputationPenalty = 5;
        [Min(0)] public int satisfiedReputation = 4;

        [Header("Исход миссии")]
        [Tooltip("Шанс успеха = база + за качество × качество обеспечения.")]
        [Range(0f, 1f)] public float successBase = 0.3f;
        [Range(0f, 1f)] public float successPerQuality = 0.65f;
        public int successReputationBase = 6;
        public int successReputationPerQuality = 14;
        public int successRankPointsBase = 30;
        public int successRankPointsPerQuality = 50;
        [Tooltip("Уникальная награда — только при качестве не ниже порога.")]
        [Range(0f, 1f)] public float rewardQuality = 0.7f;
        [Tooltip("Мораль одолженных бойцов после успешной миссии.")]
        public float lentNpcMoraleOnSuccess = 15f;
        [Tooltip("Шанс, что одолженный боец выживет при провале.")]
        [Range(0f, 1f)] public float survivorChance = 0.5f;
        public float survivorMorale = -20f;
        [Tooltip("Провал при качестве ниже порога (или отказе) — трибунал: понижение в звании.")]
        [Range(0f, 1f)] public float tribunalQuality = 0.5f;
        [Min(0)] public int tribunalReputationPenalty = 20;
        [Tooltip("Репутация, если чин погиб, хотя обеспечение было достаточным.")]
        [Min(0)] public int failReputationPenalty = 4;

        [Header("Визиты")]
        public List<VipVisitEntry> visits = new List<VipVisitEntry>();

        public override void ResetToCode()
        {
            firstVisitDay = 3; intervalMinDays = 3; intervalMaxDays = 5; arrivalHour = 10; patienceHours = 20f;
            missionHoursMin = 24f; missionHoursMax = 35f; firstVisitIsFirstEntry = true;
            junkQuality = 0.3f; refuseReputationPenalty = 14; stingyQuality = 0.5f; stingyReputationPenalty = 5; satisfiedReputation = 4;
            successBase = 0.3f; successPerQuality = 0.65f; successReputationBase = 6; successReputationPerQuality = 14;
            successRankPointsBase = 30; successRankPointsPerQuality = 50; rewardQuality = 0.7f; lentNpcMoraleOnSuccess = 15f;
            survivorChance = 0.5f; survivorMorale = -20f; tribunalQuality = 0.5f; tribunalReputationPenalty = 20; failReputationPenalty = 4;

            const ResourceType Ti = ResourceType.Titanium, Al = ResourceType.Alloys, Si = ResourceType.Silicon, Cr = ResourceType.Crystals,
                Am = ResourceType.Ammo, Md = ResourceType.Medkits;
            visits = new List<VipVisitEntry>
            {
                new VipVisitEntry
                {
                    label = "Инспектор снабжения", title = "Инспектор снабжения", personName = "Ворошилова", portrait = "И",
                    mission = "Мне нужно добраться до заброшенного склада экспедиции на северном плато. Там остались документы, которые не должны попасть к конкурентам.",
                    rewardFlag = "market_closed", rewardMessage = "Награда: доступ к закрытому рынку орбитальной станции.",
                    requests = new List<VipRequestEntry>
                    {
                        new VipRequestEntry { name = "Лучшая броня", description = "Комплект усиленной брони",
                            best = new List<RankedAmount> { new RankedAmount(Ti, 15, 5), new RankedAmount(Al, 20) }, junk = new List<RankedAmount> { new RankedAmount(Al, 6) } },
                        new VipRequestEntry { name = "Транспорт", description = "Вездеход с запасом хода",
                            best = new List<RankedAmount> { new RankedAmount(Al, 35), new RankedAmount(Si, 8), new RankedAmount(Cr, 4) }, junk = new List<RankedAmount> { new RankedAmount(Al, 10) } },
                    },
                },
                new VipVisitEntry
                {
                    label = "Генерал", title = "Генерал", personName = "Арсеньев", portrait = "★",
                    mission = "Разведка донесла о гнезде «танков» в каньоне. Я лично поведу ударную группу. Аванпост обязан обеспечить операцию.",
                    rewardFlag = "bp_shield", rewardMessage = "Награда: уникальный чертёж «Генератор щита» (меню строительства → Оборона).",
                    requests = new List<VipRequestEntry>
                    {
                        new VipRequestEntry { name = "Боеприпасы", description = "Полный боекомплект группы",
                            best = new List<RankedAmount> { new RankedAmount(Am, 120, 30) }, junk = new List<RankedAmount> { new RankedAmount(Am, 30) } },
                        new VipRequestEntry { name = "Медик", description = "Лучший медик аванпоста уходит с группой", needsNpc = true, npcRole = NpcRole.Medic,
                            best = new List<RankedAmount> { new RankedAmount(Md, 3) }, junk = new List<RankedAmount> { new RankedAmount(Md, 1) } },
                        new VipRequestEntry { name = "Лучшая броня", description = "Броня для штурмовиков",
                            best = new List<RankedAmount> { new RankedAmount(Ti, 20, 5) }, junk = new List<RankedAmount> { new RankedAmount(Al, 8) } },
                    },
                },
                new VipVisitEntry
                {
                    label = "Научный куратор", title = "Научный куратор", personName = "Делакруа", portrait = "К",
                    mission = "В пещерах найден артефакт неизвестной цивилизации. Его нужно доставить на орбитальную станцию в целости.",
                    rewardFlag = "artifact_research", rewardMessage = "Награда: исследовательский грант — 150 кредитов.", rewardCredits = 150,
                    requests = new List<VipRequestEntry>
                    {
                        new VipRequestEntry { name = "Охрана", description = "Боец сопровождения", needsNpc = true, npcRole = NpcRole.Soldier,
                            best = new List<RankedAmount> { new RankedAmount(Am, 40) }, junk = new List<RankedAmount> { new RankedAmount(Am, 10) } },
                        new VipRequestEntry { name = "Транспорт", description = "Защищённый контейнер и вездеход",
                            best = new List<RankedAmount> { new RankedAmount(Al, 30), new RankedAmount(Cr, 6) }, junk = new List<RankedAmount> { new RankedAmount(Al, 8) } },
                    },
                },
            };
        }

        public override int AddMissingFromCode()
        {
            if (visits.Count > 0) return 0;   // визиты принадлежат дизайнеру — дополняем только пустой ассет
            ResetToCode();
            return visits.Count;
        }

        public override void Validate(List<string> problems)
        {
            if (visits.Count == 0) problems.Add("Список визитов пуст — высокие чины прилетать не будут.");
            else if (!visits.Exists(v => v.weight > 0f && v.minRank == Rank.Specialist) && !firstVisitIsFirstEntry)
                problems.Add("Нет визитов для ранга Специалист с весом > 0 — до повышения никто не прилетит.");
            if (intervalMaxDays < intervalMinDays) problems.Add("Интервал визитов: «макс» меньше «мин».");
            if (missionHoursMax < missionHoursMin) problems.Add("Длительность миссии: «макс» меньше «мин».");
            if (successBase + successPerQuality > 1f) problems.Add($"Шанс успеха при лучшем обеспечении выше 100% ({(successBase + successPerQuality) * 100f:0}%) — миссии никогда не проваливаются.");
            if (tribunalQuality > stingyQuality) problems.Add("Порог трибунала выше порога «недоволен» — трибунал возможен даже у довольного гостя.");
            foreach (var v in visits)
            {
                string who = $"Визит «{v.label}»";
                if (v.requests.Count == 0) problems.Add($"{who}: нет запросов — качество обеспечения всегда 0.");
                if (string.IsNullOrEmpty(v.title) || string.IsNullOrEmpty(v.personName)) problems.Add($"{who}: пустая должность или фамилия.");
                foreach (var r in v.requests)
                {
                    if (r.best.Count == 0 && !r.needsNpc) problems.Add($"{who} · «{r.name}»: «лучшее» ничего не стоит.");
                }
            }
        }

        public override void ApplyLive() { /* менеджер визитов читает ассет при каждом событии */ }
    }
}
