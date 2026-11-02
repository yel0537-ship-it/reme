using System;

namespace Echo7
{
    /// <summary>
    /// Глобальная шина событий. Системы подписываются друг на друга через неё, а не напрямую,
    /// поэтому задачи Центра, мораль, статистика и UI не знают о внутренностях друг друга.
    /// </summary>
    public static class GameEvents
    {
        public static event Action<Structure> StructurePlaced;
        public static event Action<Structure> StructureCompleted;
        public static event Action<Structure> StructureDestroyed;
        public static event Action<Enemy> EnemyKilled;
        public static event Action<GarrisonNpc> NpcDied;
        public static event Action<int> DayStarted;
        public static event Action<int> NightStarted;
        public static event Action<int> HourPassed;
        public static event Action<Rank, Rank> RankChanged;
        public static event Action<ResourceType, int> ResourceGained;
        public static event Action<OnslaughtWave> WaveStarted;
        public static event Action<OnslaughtWave> WaveEnded;
        public static event Action<string> FlagRaised;
        public static event Action<WeatherKind> WeatherChanged;
        public static event Action PowerChanged;
        public static event Action PlayerDied;
        public static event Action<ResourceDeposit, int> ResourceMined;
        public static event Action<CaveEntrance> CaveOpened;

        public static void Placed(Structure s) => StructurePlaced?.Invoke(s);
        public static void Completed(Structure s) => StructureCompleted?.Invoke(s);
        public static void Destroyed(Structure s) => StructureDestroyed?.Invoke(s);
        public static void Killed(Enemy e) => EnemyKilled?.Invoke(e);
        public static void NpcKilled(GarrisonNpc n) => NpcDied?.Invoke(n);
        public static void Day(int day) => DayStarted?.Invoke(day);
        public static void Night(int day) => NightStarted?.Invoke(day);
        public static void Hour(int hour) => HourPassed?.Invoke(hour);
        public static void RankUpdate(Rank from, Rank to) => RankChanged?.Invoke(from, to);
        public static void Gained(ResourceType t, int amount) => ResourceGained?.Invoke(t, amount);
        public static void WaveBegan(OnslaughtWave w) => WaveStarted?.Invoke(w);
        public static void WaveFinished(OnslaughtWave w) => WaveEnded?.Invoke(w);
        public static void Flag(string flag) => FlagRaised?.Invoke(flag);
        public static void Weather(WeatherKind k) => WeatherChanged?.Invoke(k);
        public static void Power() => PowerChanged?.Invoke();
        public static void PlayerDown() => PlayerDied?.Invoke();
        public static void Mined(ResourceDeposit d, int amount) => ResourceMined?.Invoke(d, amount);
        public static void Cave(CaveEntrance c) => CaveOpened?.Invoke(c);

        public static void Reset()
        {
            StructurePlaced = null; StructureCompleted = null; StructureDestroyed = null;
            EnemyKilled = null; NpcDied = null; DayStarted = null; NightStarted = null; HourPassed = null;
            RankChanged = null; ResourceGained = null; WaveStarted = null; WaveEnded = null;
            FlagRaised = null; WeatherChanged = null; PowerChanged = null; PlayerDied = null;
            ResourceMined = null; CaveOpened = null;
        }
    }
}
