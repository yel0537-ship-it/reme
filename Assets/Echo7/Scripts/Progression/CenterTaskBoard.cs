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
    /// Шаблоны задач и правила доски — в ассете <see cref="TaskBalance"/> (Resources/Echo7Balance/Tasks);
    /// без ассета — значения из кода (TaskBalance.ResetToCode).
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

        TaskBalance _balance;
        /// <summary>Данные задач. Ассет читается при каждой выдаче, поэтому правка во время Play действует со следующей задачи.</summary>
        public TaskBalance Balance => _balance != null ? _balance : (_balance = BalanceData.LoadOrDefault<TaskBalance>(TaskBalance.File));

        public int MaxActive => Balance.MaxActiveFor(Game.Rank.Current);
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
            int skip = Mathf.Min(Balance.skipAfterPrologue, Balance.starter.Count);
            if (_tutorialStep < skip) _tutorialStep = skip;
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
            bool moraleOk = Game.Garrison != null && Game.Garrison.AverageMorale >= Balance.moraleThreshold;
            _moraleHours = moraleOk ? _moraleHours + 1 : 0;
            bool energyOk = Game.Power != null && Game.Power.Balance >= Balance.energySurplusThreshold;
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
                Game.Rank.AddReputation(-Balance.failReputationPenalty, $"провалена задача «{t.Title}»");
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
            int perPoint = Balance.tasksPerSkillPoint;
            if (perPoint > 0 && CompletedCount % perPoint == 0)
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
                var t = Generate();
                if (t == null) break;
                Active.Add(t);
            }
        }

        public void Decline(CenterTask t)
        {
            if (!CanDecline || !Active.Contains(t)) return;
            _lastDeclineDay = Game.Clock.Day;
            Active.Remove(t);
            Game.Log?.Center($"Центр согласовал отказ от задачи «{t.Title}» (право голоса капитана).");
            var n = Generate();
            if (n != null) Active.Add(n);
        }

        // ------------------------------------------------------------ стартовые задачи

        int TutorialCount => Balance.starter.Count;

        void NextTutorial()
        {
            int d = Game.Clock != null ? Game.Clock.Day : 1;
            int r = Game.Rank != null ? (int)Game.Rank.Current : 0;
            var list = Balance.starter;
            CenterTask t = _tutorialStep >= 0 && _tutorialStep < list.Count ? list[_tutorialStep].Create(d, r) : null;
            _tutorialStep++;
            if (t != null) { Active.Add(t); Game.Log?.Center($"Новая задача Центра: «{t.Title}»"); }
        }

        // ------------------------------------------------------------ генератор

        /// <summary>Случайная задача из пула: по весам, с учётом ранга, без повторов уже активных. null — выдать нечего.</summary>
        CenterTask Generate()
        {
            int d = Game.Clock.Day;
            var rank = Game.Rank.Current;
            var candidates = new List<TaskTemplate>();
            float total = 0f;
            foreach (var tpl in Balance.pool)
            {
                if (tpl == null || tpl.weight <= 0f || rank < tpl.minRank) continue;
                if (Active.Exists(a => a.Title == tpl.title)) continue;
                candidates.Add(tpl);
                total += tpl.weight;
            }
            if (candidates.Count == 0) return null;
            double roll = _rnd.NextDouble() * total;
            var pick = candidates[candidates.Count - 1];
            foreach (var c in candidates)
            {
                roll -= c.weight;
                if (roll <= 0) { pick = c; break; }
            }
            var t = pick.Create(d, (int)rank);
            Game.Log?.Center($"Новая задача Центра: «{t.Title}»");
            return t;
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
