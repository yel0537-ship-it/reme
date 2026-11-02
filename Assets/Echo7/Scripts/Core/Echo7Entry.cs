using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Echo7
{
    /// <summary>
    /// Точка входа. Повесьте на пустой объект в сцене (или создайте сцену через меню «Эхо-7 → Создать сцену игры») —
    /// весь мир, база, юниты и интерфейс собираются кодом. В пустой/безымянной сцене и в SampleScene запускается сам.
    /// </summary>
    public class Echo7Entry : MonoBehaviour
    {
        public GameConfig Config = new GameConfig();

        readonly HashSet<string> _flags = new HashSet<string>();
        static string _pendingLoadJson;
        static bool _forceStart;

        public IEnumerable<string> Flags => _flags;
        public bool HasFlag(string f) => _flags.Contains(f);
        public void SetFlag(string f) { if (_flags.Add(f)) GameEvents.Flag(f); }
        public void ClearFlag(string f) => _flags.Remove(f);

        // ------------------------------------------------------------ автозапуск

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            TryCreate(SceneManager.GetActiveScene());
        }

        static void OnSceneLoaded(Scene s, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single) TryCreate(s);
        }

        static void TryCreate(Scene s)
        {
            if (FindFirstObjectByType<Echo7Entry>() != null) return;
            bool auto = string.IsNullOrEmpty(s.path) || s.name == "SampleScene" || s.name == "Main" || s.name.StartsWith("Echo7");
            if (!auto && !_forceStart) return;
            _forceStart = false;
            new GameObject("Echo7").AddComponent<Echo7Entry>();
        }

        // ------------------------------------------------------------ сборка игры

        void Awake()
        {
            if (Game.Entry != null && Game.Entry != this) { Destroy(gameObject); return; }
            Game.ResetStatics();
            Game.Entry = this;
            Game.Config = Config ?? new GameConfig();
            Time.timeScale = 1f;
        }

        void Start()
        {
            if (Game.Entry != this) return;
            SaveData load = null;
            if (!string.IsNullOrEmpty(_pendingLoadJson))
            {
                load = SaveSystem.Parse(_pendingLoadJson);
                _pendingLoadJson = null;
            }
            Build(load);
        }

        T Child<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(Game.Root, false);
            return go.AddComponent<T>();
        }

        void Build(SaveData load)
        {
            DisableSceneCamerasAndLights();
            var cfg = Game.Config;
            if (load != null) cfg.seed = load.seed;

            Game.Root = new GameObject("Echo7_Game").transform;
            Game.Root.SetParent(transform, false);

            // сервисы
            Game.Log = new MessageLog();
            Game.Sound = Child<SoundManager>("Sound");
            Game.Sound.Init();
            Game.Fx = Child<FxManager>("Fx");
            Game.Fx.Init();
            Child<ProjectileSystem>("Projectiles").Init();
            Game.Rank = new PlayerRankManager();
            Game.Skills = new SkillTree();
            Game.Bank = new ResourceBank();

            // мир
            Game.World = Child<WorldGenerator>("World");
            Game.World.Generate(cfg.seed);
            Game.CameraRig = Child<CameraRig>("CameraRig");
            Game.CameraRig.Init();
            Game.Clock = Child<DayNightCycle>("Clock");
            Game.Clock.Init(cfg.startHour);
            Game.Weather = Child<WeatherSystem>("Weather");
            Game.Weather.Init(cfg.seed);

            // база
            Game.Noise = Child<NoiseSystem>("Noise");
            Game.Enemies = Child<EnemyManager>("Enemies");
            Game.Power = Child<PowerGrid>("Power");
            Game.Power.Init();
            Game.Build = Child<BuildSystem>("Base");
            Game.Build.Init();
            Game.Construction = Child<ConstructionManager>("Construction");
            Game.Defense = Child<DefenseGridController>("Defense");
            Game.Garrison = Child<GarrisonManager>("Garrison");
            Game.Garrison.Init();

            if (load == null)
            {
                PlaceStartingBase();
                StartingResources();
            }
            else SaveSystem.ApplyWorld(load);

            Game.Tasks = new CenterTaskBoard(cfg.seed);
            Game.Supply = Child<ResourceSupplyChain>("Supply");

            // игрок
            var pgo = new GameObject("Player");
            pgo.transform.SetParent(Game.Root, false);
            pgo.AddComponent<CharacterController>();
            Game.Player = pgo.AddComponent<PlayerController>();
            Game.Player.Init();
            var cmd = StructureLookup.FirstOf(StructureKind.Command);
            Game.Player.Teleport(cmd != null ? cmd.transform.position + cmd.transform.forward * 5f + Vector3.right * 2f : Game.World.Center + Vector3.forward * 10f);
            Game.CameraRig.Target = Game.Player.transform;

            // угрозы
            Game.Onslaught = Child<EnemyOnslaughtDirector>("Onslaught");
            Game.Onslaught.Init();
            Game.Fauna = Child<FaunaManager>("Fauna");

            // сюжет
            Game.Dialogue = new DialogueSystem();
            Game.Vip = Child<VIPVisitorManager>("VIP");
            Game.Vip.Init(cfg.seed);
            Game.Map = Child<GlobalMap>("GlobalMap");
            Game.Map.Init();

            Game.Ui = Child<UiManager>("UI");
            Game.Ui.Init();
            Game.Save = new SaveSystem();

            Game.State = GameState.Playing;
            Game.Fauna.Init();

            if (load == null)
            {
                Game.Garrison.SpawnStarting(Game.World.Center + new Vector3(-8f, 0, -9f));
                Game.Tasks.Init();
                Game.Supply.Init();
                Game.Log.Center("Аванпост «Эхо-7», говорит Центр. Специалист, вы назначены командиром. Укрепите периметр до наступления ночи.");
                Game.Log.Info("Подсказка: F1 — справка. B — строительство, клик по залежи — добыча.");
            }
            else
            {
                SaveSystem.ApplyState(load);
                Game.Log.Info("Игра загружена.");
            }
        }

        void DisableSceneCamerasAndLights()
        {
            foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (!cam.transform.IsChildOf(transform)) cam.gameObject.SetActive(false);
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional && !l.transform.IsChildOf(transform)) l.gameObject.SetActive(false);
        }

        void PlaceStartingBase()
        {
            var g = Game.Grid;
            var c = g.WorldToCell(Game.World.Center);
            var b = Game.Build;
            b.PlaceInstant(StructureKind.Reactor, c, 0);
            b.PlaceInstant(StructureKind.Command, c + new Vector2Int(-7, -1), 0);
            b.PlaceInstant(StructureKind.Habitat, c + new Vector2Int(7, 1), 0);
            b.PlaceInstant(StructureKind.Storage, c + new Vector2Int(-6, 5), 0);
            b.PlaceInstant(StructureKind.MgTurret, c + new Vector2Int(-2, 8), 0);
            b.PlaceInstant(StructureKind.Pylon, c + new Vector2Int(5, -5), 0);   // в радиусе реактора (18 м)
            b.PlaceInstant(StructureKind.Pylon, c + new Vector2Int(2, 8), 0);    // питает ворота
            b.PlaceInstant(StructureKind.Solar, c + new Vector2Int(9, -6), 0);
            // кусок периметра — пример модульной стены
            for (int i = -3; i <= 3; i++) b.PlaceInstant(StructureKind.Wall, c + new Vector2Int(i, 12), 0);
            b.PlaceInstant(StructureKind.Gate, c + new Vector2Int(4, 12), 0);
            for (int i = 5; i <= 6; i++) b.PlaceInstant(StructureKind.Wall, c + new Vector2Int(i, 12), 0);
        }

        static void StartingResources()
        {
            var bank = Game.Bank;
            bank.Set(ResourceType.Alloys, 150);
            bank.Set(ResourceType.Rations, 45);
            bank.Set(ResourceType.Ammo, 280);
            bank.Set(ResourceType.Water, 70);
            bank.Set(ResourceType.Silicon, 14);
            bank.Set(ResourceType.Medkits, 3);
            bank.Set(ResourceType.Credits, 20);
        }

        // ------------------------------------------------------------ цикл, отладка, конец игры

        void Update()
        {
            if (Game.Entry != this || Game.State != GameState.Playing) return;
            if (InputBridge.KeyDown(GameKey.F5)) Game.Save.SaveGame();
            if (InputBridge.KeyDown(GameKey.F9) && Game.Save.HasSave) LoadLatest();
            if (!Config.debugKeys) return;
            if (InputBridge.KeyDown(GameKey.F6))
            {
                foreach (var t in new[] { ResourceType.Alloys, ResourceType.Titanium, ResourceType.Crystals, ResourceType.Silicon, ResourceType.Ammo, ResourceType.Rations, ResourceType.Water })
                    Game.Bank.Add(t, 100);
                Game.Bank.Add(ResourceType.Credits, 200);
                Game.Bank.Add(ResourceType.Medkits, 5);
                Game.Log.Info("Отладка: +ресурсы");
            }
            if (InputBridge.KeyDown(GameKey.F7)) Game.Onslaught.DebugWave();
            if (InputBridge.KeyDown(GameKey.F8)) Game.Rank.AddPoints(100, "отладка");
            if (InputBridge.KeyDown(GameKey.F10)) Game.Weather.ForceNext();
            if (InputBridge.KeyDown(GameKey.F1) == false && InputBridge.KeyDown(GameKey.P)) Game.Vip.ForceVisit();
            if (Config.godMode && Game.Player != null) Game.Player.Health.Heal(9999f);
        }

        /// <summary>Вызывается при уничтожении реактора.</summary>
        public void OnReactorLost()
        {
            if (Game.State != GameState.Playing) return;
            Game.State = GameState.GameOver;
            Game.Log?.Alert("Реактор уничтожен. Аванпост потерян.");
            Time.timeScale = 0.25f;
        }

        public void Restart(bool keepLoad)
        {
            if (!keepLoad) _pendingLoadJson = null;
            _forceStart = true;
            Time.timeScale = 1f;
            Game.State = GameState.Booting;
            if (Game.Root != null) Game.Root.gameObject.SetActive(false);
            var scene = SceneManager.GetActiveScene();
            if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
            else
            {
                // сцена не добавлена в Build Settings — пересобираем на месте
                var go = new GameObject("Echo7");
                Destroy(gameObject);
                Game.Entry = null;
                go.AddComponent<Echo7Entry>();
            }
        }

        public void LoadLatest()
        {
            var json = SaveSystem.ReadFile();
            if (string.IsNullOrEmpty(json)) { Game.Log?.Warn("Сохранение не найдено."); return; }
            _pendingLoadJson = json;
            Restart(true);
        }
    }
}
