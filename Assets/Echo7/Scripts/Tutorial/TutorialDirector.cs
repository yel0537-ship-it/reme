using System;
using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    /// <summary>Части HUD, которые открываются по ходу обучения.</summary>
    public enum HudPart { Bio, Objectives, Log, Rank, Clock, Resources, Threat, Energy, MenuButtons, Weapon, Abilities, Radar }

    public enum TutorialMode { Ask, Always, Never }

    /// <summary>Учебное задание пролога.</summary>
    public class TutorialStep
    {
        public string Title;
        public string Briefing;
        public string Objective;
        public string Hint;
        public Func<float> Progress = () => 0f;
        public Func<string> ProgressText;
        public Action Enter;
        public Func<Vector3?> Marker;
        public UiWindow HighlightWindow = UiWindow.None;
        public StructureKind? HighlightKind;
        public int HighlightAbility = -1;
        public HudPart[] Reveal = new HudPart[0];
        public StructureKind[] Unlock = new StructureKind[0];
    }

    /// <summary>Карточка «новое в игре» — постепенное раскрытие механик после обучения.</summary>
    public class TipCard
    {
        public string Id;
        public string Title;
        public string Text;
        public float Shown;
    }

    /// <summary>
    /// Пролог «Высадка»: десять учебных заданий, которые по одной вводят механики (движение, связь, добыча,
    /// энергия, стены, турели, расчёт, бой, снабжение, первая ночь). Пока идёт обучение, время остановлено,
    /// случайные волны, погода, визиты и хищники отключены, интерфейс открывается по частям.
    /// После пролога механики раскрываются карточками-подсказками при первом появлении.
    /// </summary>
    public class TutorialDirector : MonoBehaviour
    {
        public bool Active { get; private set; }
        public bool ModeSelectOpen { get; private set; }
        public bool BriefingOpen { get; private set; }
        public int StepIndex { get; private set; } = -1;
        public TutorialStep Current => Active && StepIndex >= 0 && StepIndex < _steps.Count ? _steps[StepIndex] : null;
        public int StepCount => _steps.Count;
        public TipCard Tip { get; private set; }

        readonly List<TutorialStep> _steps = new List<TutorialStep>();
        readonly HashSet<HudPart> _revealed = new HashSet<HudPart>();
        readonly HashSet<StructureKind> _unlocked = new HashSet<StructureKind>();
        readonly Queue<TipCard> _tipQueue = new Queue<TipCard>();
        TutorialMarker _marker;
        float _stepCounter;       // универсальный счётчик прогресса шага
        bool _stepFlag;
        OnslaughtWave _trainingWave;
        float _pollTimer;
        float _completeTimer = -1f;
        const int NightStep = 9;

        public const string MentorName = "Капитан Левина, инструктор Центра";

        // ------------------------------------------------------------ запросы систем

        /// <summary>Остановлено ли время (все шаги, кроме первой ночи).</summary>
        public bool ClockFrozen => Active && StepIndex < NightStep;
        public bool AllowRandomWaves => !Active;
        public bool AllowNightWaves => !Active || StepIndex >= NightStep;
        public bool AllowWeather => !Active;
        public bool AllowVip => !Active;
        public bool AllowPredators => !Active;
        public bool Modal => ModeSelectOpen || BriefingOpen;

        public bool Shows(HudPart p) => !Active || _revealed.Contains(p);

        public bool IsBuildUnlocked(StructureKind k) => !Active || _unlocked.Contains(k);

        public bool WindowRevealed(UiWindow w)
        {
            if (!Active) return true;
            switch (w)
            {
                case UiWindow.Build: return StepIndex >= 3;
                case UiWindow.Garrison: return StepIndex >= 6;
                case UiWindow.Help: return true;
                default: return false;
            }
        }

        public UiWindow HighlightWindow => Current?.HighlightWindow ?? UiWindow.None;
        public StructureKind? HighlightKind => Current?.HighlightKind;
        public int HighlightAbility => Current?.HighlightAbility ?? -1;
        public Vector3? MarkerPosition => _marker != null && _marker.gameObject.activeSelf && _marker.transform.position.y > -500f ? _marker.transform.position : (Vector3?)null;

        // ------------------------------------------------------------ запуск

        public void Init()
        {
            BuildSteps();
            GameEvents.ResourceMined += (d, n) => { if (Active && StepIndex == 2 && d.Type == ResourceType.Titanium) _stepCounter += n; };
            GameEvents.StructureCompleted += OnStructureCompleted;
            GameEvents.WaveStarted += OnWaveStarted;
            GameEvents.NightStarted += d => { if (!Active) ShowTip("night", "Ночь", "Ночью волны приходят по расписанию — примерно через 2 часа после заката. Готовьте стены, патроны и расчёты турелей ещё днём."); };
            GameEvents.WeatherChanged += OnWeather;
            GameEvents.RankChanged += OnRank;
            GameEvents.FlagRaised += f =>
            {
                if (f == "comms_opened" && Active && StepIndex == 1) _stepFlag = true;
                if (f == "ai_factory_active") ShowTip("synth", "Синтетическая угроза", "Завод ИИ активен: синт-стражи устойчивы к пулям. Против них — лазерные излучатели, тяжёлые турели, гранаты и мины.");
            };
        }

        /// <summary>Окно выбора режима начала (новая игра).</summary>
        public void AskMode() { ModeSelectOpen = true; }

        public void ChooseTutorial()
        {
            ModeSelectOpen = false;
            Begin(0);
            Game.Entry.OnModeChosen();
        }

        public void ChooseClassic()
        {
            ModeSelectOpen = false;
            Game.Entry.StartClassic();
        }

        /// <summary>Начать (или продолжить после загрузки) обучение с шага.</summary>
        public void Begin(int fromStep)
        {
            Active = true;
            _revealed.Clear();
            _unlocked.Clear();
            for (int i = 0; i < fromStep && i < _steps.Count; i++) ApplyRevealAndUnlock(_steps[i]);
            Game.Clock.SetTime(Game.Clock.Day, 7.5f);
            EnterStep(fromStep);
        }

        void ApplyRevealAndUnlock(TutorialStep s)
        {
            foreach (var p in s.Reveal) _revealed.Add(p);
            foreach (var k in s.Unlock) _unlocked.Add(k);
        }

        void EnterStep(int i)
        {
            StepIndex = i;
            _stepCounter = 0f;
            _stepFlag = false;
            if (i >= _steps.Count) { Finish(false); return; }
            var s = _steps[i];
            ApplyRevealAndUnlock(s);
            s.Enter?.Invoke();
            if (s.Marker != null)
            {
                if (_marker == null) _marker = TutorialMarker.Create(transform);
                _marker.Follow(s.Marker);
            }
            else if (_marker != null) _marker.Hide();
            BriefingOpen = true;
            Game.Sound?.Play2D(Sfx.Notify, 0.6f);
            Game.Log?.Center($"Учебное задание {i + 1}/{_steps.Count}: «{s.Title}»");
        }

        public void CloseBriefing()
        {
            BriefingOpen = false;
            Game.Sound?.Play2D(Sfx.UiClick, 0.5f);
        }

        public void ReopenBriefing() { if (Current != null) BriefingOpen = true; }

        public void Skip()
        {
            if (!Active) return;
            Finish(true);
        }

        void Finish(bool skipped)
        {
            Active = false;
            BriefingOpen = false;
            if (_marker != null) _marker.Hide();
            foreach (StructureKind k in Enum.GetValues(typeof(StructureKind))) _unlocked.Add(k);
            Game.Onslaught?.Grace(240f);
            Game.Tasks?.StartAfterOnboarding();
            Game.Log?.Center(skipped
                ? "Обучение пропущено. Центр выходит на связь с задачами — окно [J]."
                : "Обучение завершено. Дальше — сами, командир. Задачи Центра — в окне [J], справка — [F1].");
            Game.Sound?.Play2D(Sfx.RankUp, 0.8f);
            if (!skipped) Game.Rank?.AddPoints(40, "пролог пройден");
            ShowTip("after", "Аванпост ваш", "Теперь время идёт само. Новые механики будут появляться по ходу игры — о каждой расскажет такая карточка. Esc — пауза и скорость времени ×1–×3.");
        }

        // ------------------------------------------------------------ шаги

        static Vector3 CellWorld(int dx, int dy)
        {
            var c = Game.Grid.WorldToCell(Game.World.Center);
            return Game.Grid.CellCenter(c.x + dx, c.y + dy);
        }

        static Vector3? NearestTitanium()
        {
            ResourceDeposit best = null; float bd = float.MaxValue;
            var c = Game.World.Center;
            foreach (var d in Registry.Deposits)
            {
                if (d == null || d.Hidden || d.IsDepleted || d.Type != ResourceType.Titanium) continue;
                float dist = (d.transform.position - c).sqrMagnitude;
                if (dist < bd) { bd = dist; best = d; }
            }
            return best != null ? best.transform.position : (Vector3?)null;
        }

        void BuildSteps()
        {
            _steps.Clear();
            _steps.Add(new TutorialStep
            {
                Title = "Высадка",
                Briefing = "Специалист, добро пожаловать на «Эхо-7». Я капитан Левина, первые сутки буду вести вас по связи.\n\n" +
                           "Аванпост развёрнут, но почти пуст: реактор, командный модуль, жилой блок и склад. Остальное построите сами.\n" +
                           "Пока идёт обучение, время на планете остановлено — спешить некуда.\n\n" +
                           "Начнём с движения: <b>ЛКМ по земле</b> — боец идёт туда, удержание кнопки — идёт за курсором. Можно и <b>WASD</b>. Колесо мыши — зум.",
                Objective = "Подойдите к командному модулю (голубой маркер).",
                Hint = "ЛКМ по земле рядом с маркером",
                Reveal = new[] { HudPart.Bio, HudPart.Objectives, HudPart.Log },
                Marker = () => { var cmd = StructureLookup.FirstOf(StructureKind.Command); return cmd != null ? cmd.transform.position + Vector3.forward * 4.5f : (Vector3?)null; },
                Progress = () =>
                {
                    var cmd = StructureLookup.FirstOf(StructureKind.Command);
                    if (cmd == null || Game.Player == null) return 1f;
                    return cmd.DistanceTo(Game.Player.transform.position) < 4f ? 1f : 0f;
                },
            });
            _steps.Add(new TutorialStep
            {
                Title = "Связь с Центром",
                Briefing = "Командный модуль — ваша связь с Центром колонии. Отсюда приходят приказы, задачи и поставки.\n\n" +
                           "<b>Кликните ЛКМ по командному модулю</b> — боец подойдёт и выйдет на связь.",
                Objective = "Выйдите на связь с Центром.",
                Hint = "ЛКМ по командному модулю",
                Reveal = new[] { HudPart.Rank, HudPart.Clock },
                Marker = () => { var cmd = StructureLookup.FirstOf(StructureKind.Command); return cmd != null ? cmd.transform.position : (Vector3?)null; },
                Progress = () => _stepFlag ? 1f : 0f,
            });
            _steps.Add(new TutorialStep
            {
                Title = "Ресурсы планеты",
                Briefing = "Центр присылает сплавы, пайки и патроны. Но <b>титан, кристаллы и кремний</b> есть только здесь, на планете.\n\n" +
                           "Недалеко от базы — залежь титана (тёмные камни с голубыми прожилками). <b>Кликните по ней</b> — боец начнёт добычу.\n\n" +
                           "Смотрите на шкалу <b>«ШУМ»</b> вверху: добыча, стройка и стрельба шумят, а шум привлекает фауну. Сейчас это неважно, но потом станет главной заботой.",
                Objective = "Добудьте 10 титана.",
                Hint = "ЛКМ по залежи и ждите",
                Reveal = new[] { HudPart.Resources, HudPart.Threat },
                Marker = NearestTitanium,
                Progress = () => Mathf.Clamp01(_stepCounter / 10f),
                ProgressText = () => $"{Mathf.FloorToInt(_stepCounter)}/10",
            });
            _steps.Add(new TutorialStep
            {
                Title = "Энергосеть",
                Briefing = "Механизмы питаются от реактора, но его радиус — всего 18 м. Дальше сеть продлевают <b>энергоузлы</b>.\n\n" +
                           "Нажмите <b>[B]</b> — меню строительства, раздел «Энергия», и поставьте энергоузел у маркера. Голографический призрак станет голубым, если место подходит.\n\n" +
                           "Строят дроны командного модуля. Если встать рядом, стройка пойдёт быстрее.",
                Objective = "Постройте энергоузел.",
                Hint = "[B] → Энергия → Энергоузел → ЛКМ у маркера",
                Reveal = new[] { HudPart.MenuButtons, HudPart.Energy },
                Unlock = new[] { StructureKind.Pylon },
                HighlightWindow = UiWindow.Build,
                HighlightKind = StructureKind.Pylon,
                Marker = () => CellWorld(2, 8),
                Progress = () => _stepCounter >= 1f ? 1f : 0f,
            });
            _steps.Add(new TutorialStep
            {
                Title = "Периметр",
                Briefing = "Фауна всегда идёт к реактору — значит, нужен периметр.\n\n" +
                           "В разделе «Стены» выберите <b>Стену</b> и <b>протяните её мышью</b>: зажмите ЛКМ в начале линии и отпустите в конце. Соседние секции срастаются сами.\n\n" +
                           "Ограждение дешевле, но прыгуны через него перемахивают. Ворота пропускают своих.",
                Objective = "Постройте 6 секций стены у маркера.",
                Hint = "[B] → Стены → Стена → протяжка ЛКМ",
                Unlock = new[] { StructureKind.Wall, StructureKind.Fence, StructureKind.Gate },
                HighlightWindow = UiWindow.Build,
                HighlightKind = StructureKind.Wall,
                Marker = () => CellWorld(0, 12),
                Progress = () => Mathf.Clamp01(_stepCounter / 6f),
                ProgressText = () => $"{Mathf.FloorToInt(_stepCounter)}/6",
            });
            _steps.Add(new TutorialStep
            {
                Title = "Огневая точка",
                Briefing = "Стены задерживают, турели убивают.\n\n" +
                           "<b>Пулемётный узел</b> дешёвый и хорош против мелочи, но тратит патроны. Поставьте его за стеной, у маркера. Кнопка <b>R</b> поворачивает постройку, <b>ПКМ</b> — отмена.\n\n" +
                           "Кольцо вокруг призрака показывает дальность стрельбы.",
                Objective = "Постройте пулемётный узел.",
                Hint = "[B] → Оборона → Пулемётный узел",
                Unlock = new[] { StructureKind.MgTurret },
                HighlightWindow = UiWindow.Build,
                HighlightKind = StructureKind.MgTurret,
                Marker = () => CellWorld(-2, 8),
                Progress = () => _stepCounter >= 1f ? 1f : 0f,
            });
            _steps.Add(new TutorialStep
            {
                Title = "Расчёт",
                Briefing = "Без расчёта турель работает в ослабленном автономном режиме.\n\n" +
                           "Откройте окно гарнизона <b>[G]</b> и нажмите «На турель» у любого бойца.\n\n" +
                           "Запомните: <b>мораль расчёта = точность турели</b>. Сытые, выспавшиеся бойцы в тесноте не живут — им нужны койки, еда и вода.",
                Objective = "Назначьте бойца расчётом турели.",
                Hint = "[G] → «На турель»",
                Reveal = new[] { HudPart.MenuButtons },
                HighlightWindow = UiWindow.Garrison,
                Progress = () =>
                {
                    foreach (var t in Registry.Turrets) if (t != null && t.Crew != null) return 1f;
                    return 0f;
                },
            });
            _steps.Add(new TutorialStep
            {
                Title = "Учебная тревога",
                Briefing = "Сейсмодатчики: с севера идёт стайка <b>грызунов</b> — ваш шум их привлёк. Это разведка, неопасная. Отличный шанс потренироваться.\n\n" +
                           "<b>ЛКМ по врагу</b> — атака, <b>ПКМ</b> — стрельба в точку, <b>Пробел</b> — перекат, <b>[1]</b> — граната.\n" +
                           "Внизу — биомонитор (целостность костюма) и боезапас. Справа — радар: красные точки — враги.\n\n" +
                           "Грызуны любят перегрызать кабели мелких построек — защищайте энергоузел.",
                Objective = "Отразите учебную атаку.",
                Hint = "ЛКМ по врагу или ПКМ в точку",
                Reveal = new[] { HudPart.Weapon, HudPart.Abilities, HudPart.Radar },
                HighlightAbility = 0,
                Enter = () =>
                {
                    var dir = Vector3.forward;
                    _trainingWave = Game.Onslaught.Schedule(WaveTrigger.Scripted, 7, 22f, "Учебная тревога", dir);
                },
                Progress = () =>
                {
                    if (_trainingWave == null) return 0f;
                    if (_trainingWave.Finished) return 1f;
                    return _trainingWave.Total > 0 ? Mathf.Clamp01((float)_trainingWave.Killed / _trainingWave.Total) : 0f;
                },
                ProgressText = () => _trainingWave == null ? "" : !_trainingWave.Started ? "подход…" : $"{_trainingWave.Killed}/{_trainingWave.Total}",
            });
            _steps.Add(new TutorialStep
            {
                Title = "Снабжение",
                Briefing = "Отлично отбились! Центр шлёт поставку. Капсулы приходят <b>каждое утро в 08:00</b>, а их объём зависит от <b>репутации</b>: выполняйте задачи Центра — и поставки будут щедрее.\n\n" +
                           "Теперь вам доступна вся базовая постройка: жильё, склады, влагоконденсаторы, буры, дронпорт, мины, медблок.\n" +
                           "Кремний — дилемма: жилые блоки поднимают мораль, а лазеры и конденсаторы — оборону и воду.",
                Objective = "Встретьте капсулу снабжения.",
                Hint = "Смотрите на небо над маркером",
                Unlock = new[]
                {
                    StructureKind.Habitat, StructureKind.Storage, StructureKind.Condenser, StructureKind.Solar, StructureKind.Extractor,
                    StructureKind.DroneBay, StructureKind.Mine, StructureKind.MedBay, StructureKind.DecoyBeacon
                },
                Enter = () =>
                {
                    var p = Game.Supply.SendCustomPod(new Cost((ResourceType.Alloys, 60), (ResourceType.Ammo, 120), (ResourceType.Rations, 15)), "Учебная поставка", () => _stepFlag = true);
                    _podPoint = p;
                },
                Marker = () => _podPoint,
                Progress = () => _stepFlag ? 1f : 0f,
            });
            _steps.Add(new TutorialStep
            {
                Title = "Первая ночь",
                Briefing = "Последний урок — самый важный. Я запускаю время.\n\n" +
                           "Днём — стройка и разведка, <b>ночью фауна идёт волнами</b>. Первая придёт около 22:00. Продержитесь до рассвета.\n\n" +
                           "Совет: замкните периметр, держите запас патронов, лечитесь аптечкой <b>[3]</b>. <b>Esc</b> — пауза и ускорение времени.",
                Objective = "Продержитесь до рассвета.",
                Hint = "Время идёт: ночь — с 20:00",
                Reveal = (HudPart[])Enum.GetValues(typeof(HudPart)),
                Enter = () => { Game.Clock.SetTime(Game.Clock.Day, 17.5f); },
                Progress = () =>
                {
                    var c = Game.Clock;
                    if (c.Day >= 2 && c.Hour >= 5.5f && c.Hour < 12f) return 1f;
                    float total = 12f; // 17:30 → 05:30
                    float passed = c.Hour >= 17.5f ? c.Hour - 17.5f : c.Hour + 6.5f;
                    return Mathf.Clamp01(passed / total) * 0.99f;
                },
                ProgressText = () => Game.Clock.TimeString,
            });
        }

        Vector3? _podPoint;

        void OnStructureCompleted(Structure s)
        {
            if (!Active) { MaybeTipForStructure(s); return; }
            switch (StepIndex)
            {
                case 3: if (s.Def.Kind == StructureKind.Pylon) _stepCounter += 1f; break;
                case 4: if (s.Def.Kind == StructureKind.Wall || s.Def.Kind == StructureKind.Fence || s.Def.Kind == StructureKind.Gate) _stepCounter += 1f; break;
                case 5: if (s.Def.Kind == StructureKind.MgTurret) _stepCounter += 1f; break;
            }
        }

        // ------------------------------------------------------------ цикл

        void Update()
        {
            if (!Game.IsPlaying) return;
            if (Active && Current != null && !BriefingOpen)
            {
                if (Current.Progress() >= 1f)
                {
                    if (_completeTimer < 0f)
                    {
                        _completeTimer = 1.2f;
                        Game.Sound?.Play2D(Sfx.BuildDone, 0.8f);
                        Game.Log?.Info($"Задание «{Current.Title}» выполнено.");
                    }
                    _completeTimer -= Time.unscaledDeltaTime;
                    if (_completeTimer <= 0f) { _completeTimer = -1f; EnterStep(StepIndex + 1); }
                }
            }
            // если игрок закрыл связь до шага 1 — связь засчитается, когда шаг начнётся
            if (Active && StepIndex == 1 && Game.Entry.HasFlag("comms_opened")) _stepFlag = true;

            // карточки-подсказки
            if (Tip != null && Time.unscaledTime - Tip.Shown > 30f) DismissTip();
            if (Tip == null && _tipQueue.Count > 0 && !Modal)
            {
                Tip = _tipQueue.Dequeue();
                Tip.Shown = Time.unscaledTime;
                Game.Sound?.Play2D(Sfx.Notify, 0.4f);
            }

            _pollTimer -= Time.deltaTime;
            if (_pollTimer > 0f || Active) return;
            _pollTimer = 1f;
            PollTips();
        }

        // ------------------------------------------------------------ подсказки

        public void ShowTip(string id, string title, string text)
        {
            if (Game.Entry.HasFlag("tip_" + id)) return;
            Game.Entry.SetFlag("tip_" + id);
            _tipQueue.Enqueue(new TipCard { Id = id, Title = title, Text = text });
        }

        public void DismissTip() { Tip = null; }

        void PollTips()
        {
            foreach (var s in Registry.Structures)
            {
                if (s == null) continue;
                if (s.CableCut) { ShowTip("cable", "Перегрызенный кабель", "Мелкие твари перегрызают кабели: постройка обесточена, пока дрон или инженер её не починит. Защищайте энергоузлы — они тоже их любимая цель."); break; }
                if (s is Turret t && t.Jammed) { ShowTip("wear", "Износ турелей", "Каждый выстрел изнашивает турель, в пекло — быстрее. Изношенная турель клинит. Дроны обслуживают её сами, но это отвлекает их от ремонта стен."); break; }
            }
            var g = Game.Garrison;
            if (g != null && g.PresentCount > 0 && g.AverageMorale < 35f)
                ShowTip("morale", "Мораль падает", "Нужны койки (жилые блоки), еда и вода. Поговорите с бойцами (клик по ним) — подбодрите или выдайте доппаёк. При низкой морали падает точность турелей, бойцы отказываются от постов и могут дезертировать.");
            if (Game.Bank[ResourceType.Ammo] < 60)
                ShowTip("ammo", "Патроны на исходе", "Турели и бойцы тратят патроны. Центр присылает их по утрам. С ранга Капитан можно выбрать профиль поставок «Боеприпасы». Лазерам патроны не нужны — только энергия.");
            if (Game.Skills.Points > 0)
                ShowTip("skills", "Очки навыков", "У вас есть очки навыков — окно [K]. Три ветки: Инженерия (стройка, энергия, стены), Тактика (турели, дроны), Выживание (добыча, лечение, скрытность).");
            if (Game.Vip != null && Game.Vip.Current != null)
                ShowTip("vip", "Высокий чин на базе", "Гость попросит лучшую броню, медиков, транспорт. Дать — репутация и уникальные награды. Отказать или дать хлам — урезанные поставки. Если чин погибнет из-за плохого обеспечения — трибунал и понижение в звании.");
            if (Game.Power != null && Game.Power.Satisfaction < 0.95f && Game.Power.Demand > 0f)
                ShowTip("power", "Дефицит энергии", "Потребление выше выработки — все механизмы работают вполсилы. Стройте солнечные панели, геотермальные генераторы (Лейтенант) или отключайте лишнее.");
            var pl = Game.Player;
            if (pl != null && Game.World != null)
                foreach (var cave in Game.World.Caves)
                    if (cave != null && !cave.Opened && (cave.transform.position - pl.transform.position).sqrMagnitude < 18f * 18f)
                    { ShowTip("cave", "Заваленная пещера", "Внутри — богатые жилы. Подрывной заряд [4] у завала откроет их, но взрыв слышен на всю саванну: почти наверняка придёт волна. А в глубине пещер бывает кое-что похуже фауны..."); break; }
            foreach (var e in Registry.Enemies)
            {
                if (e == null || e.IsDead || e.Wave == null) continue;
                switch (e.Archetype.Kind)
                {
                    case EnemyKind.Jumper: ShowTip("jumper", "Прыгуны", "Перемахивают ограждения и низкие стены, атакуют турели и ворота. Механизм ворот можно сломать — их заклинит открытыми. Против прыгунов — высокие стены."); break;
                    case EnemyKind.Brute: ShowTip("brute", "Громилы", "Проламывают стены в самом слабом месте — волна идёт туда, где проще пройти. Держите стены целыми: дроны чинят их за сплавы."); break;
                    case EnemyKind.Spitter: ShowTip("spitter", "Плевуны", "Плюются кислотой из-за стен и целятся в турели. Кислота разъедает постройки. Подпускать их близко нельзя — нужна дальность."); break;
                    case EnemyKind.Tank: ShowTip("tank", "Танк!", "Огромная бронированная тварь идёт прямо к реактору. Пули почти бесполезны — лазерные излучатели и тяжёлые турели, сосредоточьте огонь."); break;
                }
            }
        }

        void MaybeTipForStructure(Structure s)
        {
            if (s.Def.Kind == StructureKind.DroneBay) ShowTip("dronebay", "Дронпорт", "Дроны-сборщики сами носят местные ресурсы. В инспекторе дронпорта (клик по нему) можно выбрать приоритет добычи.");
            if (s.Def.Kind == StructureKind.Extractor) ShowTip("extractor", "Бур-экстрактор", "Бур добывает сам, но работает громко — постоянный шум копит угрозу. Ставьте буры там, где готовы отбиваться.");
            if (s.Def.Kind == StructureKind.LandingPad) ShowTip("pad", "Посадочная площадка", "Сюда садятся челноки высоких чинов и капсулы. Через связь с Центром можно заказать орбитальную доставку тяжёлых модулей.");
        }

        void OnWaveStarted(OnslaughtWave w)
        {
            if (Active) return;
            if (w.Trigger == WaveTrigger.Noise || w.Trigger == WaveTrigger.Random)
                ShowTip("noise_wave", "Неожиданная волна", "Шум копит «угрозу» (шкала вверху). Когда она переполнится, придёт внеплановая волна — с той стороны, где шумели больше всего. Стройка, буры, стрельба и взрывы — всё это шум.");
            if (w.Trigger == WaveTrigger.NoiseSpike)
                ShowTip("spike", "Всплеск шума", "Взрыв или канонада мгновенно поднимают фауну. Сенсорная мачта (Лейтенант) даёт больше времени на подготовку и показывает состав волны.");
        }

        void OnWeather(WeatherKind k)
        {
            switch (k)
            {
                case WeatherKind.Heatwave: ShowTip("heat", "Пекло", "Несколько дней жары: вода испаряется (и из озёр, и со склада), саванна выгорает, турели изнашиваются быстрее, мораль падает. Запасайте воду и ставьте конденсаторы у озёр. Зато солнечные панели работают лучше."); break;
                case WeatherKind.DustStorm: ShowTip("dust", "Пылевая буря", "Видимость и дальность турелей снижены, солнечные панели почти не работают. Хорошее время, чтобы не шуметь."); break;
                case WeatherKind.Rain: ShowTip("rain", "Ливни", "Озёра наполняются, луга и деревья возвращаются. Конденсаторы дают вдвое больше воды."); break;
                case WeatherKind.InsectMigration: ShowTip("insects", "Миграция насекомых", "Рои кусают всех, кто снаружи, — лихорадка без лечения смертельна. Медблок лечит (тратит аптечки), жилые блоки — укрытие. Бойцы сами прячутся, если есть где."); break;
            }
        }

        void OnRank(Rank from, Rank to)
        {
            if (to <= from) return;
            var list = new List<string>();
            foreach (Feature f in Enum.GetValues(typeof(Feature)))
                if (PlayerRankManager.RequiredRank(f) == to && f != Feature.BasicConstruction) list.Add(PlayerRankManager.FeatureName(f));
            if (list.Count > 0)
                ShowTip("rank_" + to, $"Звание: {PlayerRankManager.RankName(to)}", "Новые допуски: " + string.Join(", ", list) + ".");
        }

        // ------------------------------------------------------------ сохранение

        public void Restore(bool active, int step)
        {
            if (!active) return;
            Begin(Mathf.Clamp(step, 0, _steps.Count - 1));
        }
    }
}
