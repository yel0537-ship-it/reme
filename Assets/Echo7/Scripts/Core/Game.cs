using UnityEngine;

namespace Echo7
{
    public enum GameState { Booting, Playing, GameOver }

    /// <summary>
    /// Простой сервис-локатор: все менеджеры регистрируются здесь при запуске (см. GameBootstrap).
    /// Для прототипа это быстрее и прозрачнее DI-контейнера; при росте проекта можно заменить на Zenject/VContainer.
    /// </summary>
    public static class Game
    {
        public static GameConfig Config;
        public static Echo7Entry Entry;
        public static Transform Root;
        public static GameState State = GameState.Booting;

        // Мир
        public static WorldGenerator World;
        public static WorldGrid Grid;
        public static DayNightCycle Clock;
        public static WeatherSystem Weather;
        public static CameraRig CameraRig;

        // Игрок
        public static PlayerController Player;

        // База и экономика
        public static ResourceBank Bank;
        public static ResourceSupplyChain Supply;
        public static PowerGrid Power;
        public static BuildSystem Build;
        public static ConstructionManager Construction;
        public static DefenseGridController Defense;

        // Угрозы
        public static NoiseSystem Noise;
        public static EnemyManager Enemies;
        public static EnemyOnslaughtDirector Onslaught;
        public static FaunaManager Fauna;

        // Прогрессия и сюжет
        public static PlayerRankManager Rank;
        public static SkillTree Skills;
        public static CenterTaskBoard Tasks;
        public static GarrisonManager Garrison;
        public static DialogueSystem Dialogue;
        public static VIPVisitorManager Vip;
        public static GlobalMap Map;

        // Сервисы
        public static MessageLog Log;
        public static SoundManager Sound;
        public static FxManager Fx;
        public static UiManager Ui;
        public static SaveSystem Save;

        public static bool IsPlaying => State == GameState.Playing;
        public static Reactor Reactor => Build != null ? Build.Reactor : null;

        /// <summary>Сброс статики (нужно при отключённой перезагрузке домена в Enter Play Mode Options).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatics()
        {
            Config = null; Entry = null; Root = null; State = GameState.Booting;
            World = null; Grid = null; Clock = null; Weather = null; CameraRig = null;
            Player = null;
            Bank = null; Supply = null; Power = null; Build = null; Construction = null; Defense = null;
            Noise = null; Enemies = null; Onslaught = null; Fauna = null;
            Rank = null; Skills = null; Tasks = null; Garrison = null; Dialogue = null; Vip = null; Map = null;
            Log = null; Sound = null; Fx = null; Ui = null; Save = null;
            GameEvents.Reset();
            Registry.Reset();
        }
    }
}
