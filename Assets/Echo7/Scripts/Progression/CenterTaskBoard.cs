using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    public enum TaskType { Build, Collect, SurviveNights, RepelWaves, KillHostiles, Hunt, KeepMorale, EnergySurplus, OpenCave, Survey, Stockpile }

    [System.Serializable]
    public class CenterTask
    {
        public string Title;
        public string Description;
        public TaskType Type;
        public StructureKind BuildKind;
        public bool AnyWall;
        public ResourceType Resource;
        public int Target;
        public int Progress;
        public int DeadlineDay;
        public int RewardRp;
        public int RewardRep;
        public Cost RewardSupplies;
        public bool Done, Failed;
        public float Fraction => Target > 0 ? Mathf.Clamp01((float)Progress / Target) : 0f;
    }

    /// <summary>
    /// Задачи (KPI) от Центра. Выполнение → очки ранга, репутация, внеплановые поставки.
    /// Провал к сроку → минус репутация и урезанные поставки.
    /// </summary>
    public class CenterTaskBoard
    {
        public readonly List<CenterTask> Active = new List<CenterTask>();
        public readonly List<CenterTask> History = new List<CenterTask>();
        public int CompletedCount { get; private set; }
        public int FailedCount { get; private set; }
        public int RecentKpi { get; private set; }   // >0 — Центр доволен (бонус к поставкам)
        int _tutorialStep;
        int _lastDeclineDay = -1;
        int _moraleHours, _energyHours;
        readonly System.Random _rnd;

        public int MaxActive => Game.Rank.Current >= Rank.Captain ? 4 : 3;
        public bool CanDecline => Game.Rank.IsUnlocked(Feature.CenterVoice) && _lastDeclineDay != Game.Clock.Day;

        public CenterTaskBoard(int seed)
        {
            _rnd = new System.Random(seed * 17 + 3);
        }

        public void Init()
        {
            InitEventsOnly();
            NextTutorial();
            NextTutorial();
        }

        bool _eventsHooked;

        /// <summary>
        /// Старт задач после пролога: обучающие KPI (периметр, титан, турель, ночь) уже пройдены в прологе,
        /// поэтому Центр сразу выдаёт задачи на развитие.
        /// </summary>
        public void StartAfterOnboarding()
        {
            if (!_eventsHooked) InitEventsOnly();
            if (_tutorialStep < 4) _tutorialStep = 4;
            if (Active.Count == 0) { NextTutorial(); NextTutorial(); }
            Refill();
        }

        /// <summary>Подписка на события без выдачи стартовых задач (для загрузки).</summary>
        public void InitEventsOnly()
        {
            if (_eventsHooked) return;
            _eventsHooked = true;
            GameEvents.StructureCompleted += s => Progress(t => t.Type == TaskType.Build && (t.AnyWall ? (s.Def.Category == BuildCategory.Walls) : s.Def.Kind == t.BuildKind), 1);
            GameEvents.ResourceMined += (d, n) => Progress(t => t.Type == TaskType.Collect && t.Resource == d.Type, n);
            GameEvents.DayStarted += OnDay;
            GameEvents.WaveEnded += w => Progress(t => t.Type == TaskType.RepelWaves, 1);
            GameEvents.EnemyKilled += e =>
            {
                if (e.Wave != null) Progress(t => t.Type == TaskType.KillHostiles, 1);
                if (e.Archetype.Kind == EnemyKind.Grazer && e.Health.LastDamage.Source == Faction.Player) Progress(t => t.Type == TaskType.Hunt, 1);
            };
            GameEvents.CaveOpened += c => Progress(t => t.Type == TaskType.OpenCave, 1);
            GameEvents.FlagRaised += f => { if (f == "survey_found") Progress(t => t.Type == TaskType.Survey, 1); };
            GameEvents.HourPassed += OnHour;
        }

        void Progress(System.Predicate<CenterTask> match, int amount)
        {
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                var t = Active[i];
                if (t.Done || t.Failed || !match(t)) continue;
                t.Progress = Mathf.Min(t.Target, t.Progress + amount);
                if (t.Progress >= t.Target) Complete(t);
            }
        }

        void OnHour(int h)
        {
            // длительные KPI проверяются раз в игровой час
            bool moraleOk = Game.Garrison != null && Game.Garrison.AverageMorale >= 60f;
            _moraleHours = moraleOk ? _moraleHours + 1 : 0;
            bool energyOk = Game.Power != null && Game.Power.Balance >= 10f;
            _energyHours = energyOk ? _energyHours + 1 : 0;
            foreach (var t in Active.ToArray())
            {
                if (t.Done || t.Failed) continue;
                if (t.Type == TaskType.KeepMorale) { t.Progress = Mathf.Min(t.Target, _moraleHours); if (t.Progress >= t.Target) Complete(t); }
                if (t.Type == TaskType.EnergySurplus) { t.Progress = Mathf.Min(t.Target, _energyHours); if (t.Progress >= t.Target) Complete(t); }
                if (t.Type == TaskType.Stockpile) { t.Progress = Mathf.Min(t.Target, Game.Bank[t.Resource]); if (t.Progress >= t.Target) Complete(t); }
            }
        }

        void OnDay(int day)
        {
            Progress(t => t.Type == TaskType.SurviveNights, 1);
            foreach (var t in Active.ToArray())
            {
                if (t.Done || t.Failed || t.DeadlineDay <= 0 || day <= t.DeadlineDay) continue;
                t.Failed = true;
                FailedCount++;
                RecentKpi = Mathf.Max(-3, RecentKpi - 1);
                Game.Rank.AddReputation(-6, $"провалена задача «{t.Title}»");
                Game.Log?.Center($"Центр: задача «{t.Title}» провалена. Следующая поставка будет урезана.");
                Active.Remove(t);
                History.Add(t);
            }
            Refill();
        }

        void Complete(CenterTask t)
        {
            if (t.Done) return;
            t.Done = true;
            CompletedCount++;
            RecentKpi = Mathf.Min(3, RecentKpi + 1);
            Active.Remove(t);
            History.Add(t);
            Game.Log?.Center($"Центр: задача «{t.Title}» выполнена. Отличная работа, {PlayerRankManager.RankName(Game.Rank.Current).ToLower()}.");
            Game.Rank.AddPoints(t.RewardRp, $"задача «{t.Title}»");
            if (t.RewardRep != 0) Game.Rank.AddReputation(t.RewardRep, "выполнение KPI");
            if (t.RewardSupplies != null && !t.RewardSupplies.IsEmpty)
            {
                Game.Supply?.SendBonusPod(t.RewardSupplies, "внеплановая поставка за выполнение задачи");
            }
            if (CompletedCount % 2 == 0)
            {
                Game.Skills.AddPoints(1);
                Game.Log?.Info("Получено очко навыков.");
            }
            if (_tutorialStep < TutorialCount) NextTutorial();
            Refill();
        }

        void Refill()
        {
            int guard = 0;
            while (Active.Count < MaxActive && guard++ < 6)
            {
                if (_tutorialStep < TutorialCount) { NextTutorial(); continue; }
                Active.Add(Generate());
            }
        }

        public void Decline(CenterTask t)
        {
            if (!CanDecline || !Active.Contains(t)) return;
            _lastDeclineDay = Game.Clock.Day;
            Active.Remove(t);
            Game.Log?.Center($"Центр согласовал отказ от задачи «{t.Title}» (право голоса капитана).");
            Active.Add(Generate());
        }

        // ------------------------------------------------------------ обучение

        const int TutorialCount = 6;

        void NextTutorial()
        {
            int d = Game.Clock != null ? Game.Clock.Day : 1;
            CenterTask t = null;
            switch (_tutorialStep)
            {
                case 0: t = new CenterTask { Title = "Периметр", Description = "Постройте 8 секций стен или ограждений вокруг реактора. [B] — меню, протяжка мышью.", Type = TaskType.Build, AnyWall = true, Target = 8, DeadlineDay = d + 2, RewardRp = 25, RewardRep = 3, RewardSupplies = new Cost((ResourceType.Alloys, 25)) }; break;
                case 1: t = new CenterTask { Title = "Местные ресурсы", Description = "Центр не прислал титан. Добудьте 20 титана вручную (клик по залежи) или дронами.", Type = TaskType.Collect, Resource = ResourceType.Titanium, Target = 20, DeadlineDay = d + 2, RewardRp = 30, RewardRep = 2, RewardSupplies = new Cost((ResourceType.Ammo, 60)) }; break;
                case 2: t = new CenterTask { Title = "Огневая точка", Description = "Постройте ещё один пулемётный узел и назначьте расчёт в окне гарнизона [G].", Type = TaskType.Build, BuildKind = StructureKind.MgTurret, Target = 1, DeadlineDay = d + 2, RewardRp = 25, RewardRep = 2 }; break;
                case 3: t = new CenterTask { Title = "Пережить ночь", Description = "Удержите аванпост до рассвета.", Type = TaskType.SurviveNights, Target = 1, RewardRp = 35, RewardRep = 4, RewardSupplies = new Cost((ResourceType.Rations, 20), (ResourceType.Ammo, 40)) }; break;
                case 4: t = new CenterTask { Title = "Автоматизация", Description = "Постройте дронпорт — дроны сами носят местные ресурсы и шумят меньше бура.", Type = TaskType.Build, BuildKind = StructureKind.DroneBay, Target = 1, DeadlineDay = d + 3, RewardRp = 30, RewardRep = 2 }; break;
                case 5: t = new CenterTask { Title = "Энергобаланс", Description = "Держите профицит энергии ≥10 в течение 6 часов (солнечные панели, узлы).", Type = TaskType.EnergySurplus, Target = 6, DeadlineDay = d + 3, RewardRp = 30, RewardRep = 2 }; break;
            }
            _tutorialStep++;
            if (t != null) { Active.Add(t); Game.Log?.Center($"Новая задача Центра: «{t.Title}»"); }
        }

        // ------------------------------------------------------------ генератор

        CenterTask Generate()
        {
            int d = Game.Clock.Day;
            var rank = Game.Rank.Current;
            int r = (int)rank;
            var pool = new List<System.Func<CenterTask>>
            {
                () => new CenterTask { Title = "Отразить натиск", Description = "Отразите волны фауны.", Type = TaskType.RepelWaves, Target = 2 + r / 2, DeadlineDay = d + 3, RewardRp = 40 + r * 10, RewardRep = 3 },
                () => new CenterTask { Title = "Зачистка", Description = "Уничтожьте враждебных особей.", Type = TaskType.KillHostiles, Target = 25 + r * 10, DeadlineDay = d + 3, RewardRp = 35 + r * 8, RewardRep = 2, RewardSupplies = new Cost((ResourceType.Ammo, 80)) },
                () => new CenterTask { Title = "Охота", Description = "Пайков мало. Добудьте мясо рогачей.", Type = TaskType.Hunt, Target = 3, DeadlineDay = d + 2, RewardRp = 20, RewardRep = 1, RewardSupplies = new Cost((ResourceType.Rations, 15)) },
                () => new CenterTask { Title = "Кристаллы для лабораторий", Description = "Добудьте кристаллы.", Type = TaskType.Collect, Resource = ResourceType.Crystals, Target = 20 + r * 10, DeadlineDay = d + 3, RewardRp = 40 + r * 10, RewardRep = 3, RewardSupplies = new Cost((ResourceType.Credits, 60)) },
                () => new CenterTask { Title = "Титан для верфей", Description = "Добудьте титан.", Type = TaskType.Collect, Resource = ResourceType.Titanium, Target = 30 + r * 10, DeadlineDay = d + 3, RewardRp = 35 + r * 10, RewardRep = 3, RewardSupplies = new Cost((ResourceType.Alloys, 40)) },
                () => new CenterTask { Title = "Боевой дух", Description = "Мораль гарнизона ≥ 60 в течение 8 часов.", Type = TaskType.KeepMorale, Target = 8, DeadlineDay = d + 3, RewardRp = 30, RewardRep = 3 },
                () => new CenterTask { Title = "Запас воды", Description = "Накопите 120 воды перед сезоном пекла.", Type = TaskType.Stockpile, Resource = ResourceType.Water, Target = 120, DeadlineDay = d + 3, RewardRp = 30, RewardRep = 2 },
                () => new CenterTask { Title = "Укрепление", Description = "Постройте ещё стен.", Type = TaskType.Build, AnyWall = true, Target = 10 + r * 4, DeadlineDay = d + 2, RewardRp = 25 + r * 5, RewardRep = 2, RewardSupplies = new Cost((ResourceType.Alloys, 30)) },
                () => new CenterTask { Title = "Глубинная разведка", Description = "Подорвите завал одной из пещер — там богатые жилы.", Type = TaskType.OpenCave, Target = 1, DeadlineDay = d + 4, RewardRp = 50, RewardRep = 3 },
            };
            if (rank >= Rank.Lieutenant)
            {
                pool.Add(() => new CenterTask { Title = "Геологоразведка", Description = "Найдите скрытые залежи геологическим зондом.", Type = TaskType.Survey, Target = 1, DeadlineDay = d + 3, RewardRp = 40, RewardRep = 3 });
                pool.Add(() => new CenterTask { Title = "Лазерный рубеж", Description = "Постройте лазерный излучатель против бронированных тварей.", Type = TaskType.Build, BuildKind = StructureKind.LaserTurret, Target = 1, DeadlineDay = d + 3, RewardRp = 40, RewardRep = 2 });
            }
            if (rank >= Rank.Captain)
                pool.Add(() => new CenterTask { Title = "Посадочная площадка", Description = "Подготовьте приём орбитальных грузов.", Type = TaskType.Build, BuildKind = StructureKind.LandingPad, Target = 1, DeadlineDay = d + 4, RewardRp = 60, RewardRep = 4 });
            if (rank >= Rank.Major)
                pool.Add(() => new CenterTask { Title = "Космопорт", Description = "Постройте космопорт для полётов на орбиту.", Type = TaskType.Build, BuildKind = StructureKind.Spaceport, Target = 1, DeadlineDay = d + 6, RewardRp = 120, RewardRep = 6 });

            // не дублируем уже активные задачи
            for (int i = 0; i < 10; i++)
            {
                var t = pool[_rnd.Next(pool.Count)]();
                if (!Active.Exists(a => a.Title == t.Title)) { Game.Log?.Center($"Новая задача Центра: «{t.Title}»"); return t; }
            }
            return pool[0]();
        }

        public void Restore(List<CenterTask> active, int completed, int failed, int tutorialStep)
        {
            Active.Clear();
            Active.AddRange(active);
            CompletedCount = completed;
            FailedCount = failed;
            _tutorialStep = tutorialStep;
        }

        public int TutorialStep => _tutorialStep;
    }
}
