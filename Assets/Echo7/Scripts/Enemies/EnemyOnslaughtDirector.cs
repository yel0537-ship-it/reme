using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Echo7
{
    public enum WaveTrigger { Night, Noise, NoiseSpike, Random, Scripted, Debug }

    /// <summary>Одна волна: состав, направление, прогресс.</summary>
    public class OnslaughtWave
    {
        public int Id;
        public WaveTrigger Trigger;
        public string Label;
        public int Budget;
        public Vector3 Direction;
        public Vector3 SpawnCenter;
        public readonly Queue<EnemyKind> Pending = new Queue<EnemyKind>();
        public readonly Dictionary<EnemyKind, int> Composition = new Dictionary<EnemyKind, int>();
        public int Total, Spawned, Alive, Killed;
        public float ArriveTime;      // когда начнётся спавн
        public float NextSpawn;
        public bool Started, Finished;

        public void OnMemberDied(Enemy e) { Alive--; Killed++; }

        public string CompositionText()
        {
            var sb = new StringBuilder();
            foreach (var kv in Composition)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(EnemyArchetype.Get(kv.Key).Name).Append(" ×").Append(kv.Value);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Директор волн. Анализирует уровень шума игрока (стройка, выстрелы, добыча, взрывы) и время с последней атаки,
    /// копит «давление» и выпускает волну подходящего состава с направления, откуда шумели.
    /// Типы запуска: ночная (по расписанию), шумовая (давление), всплеск шума (взрыв у пещеры), сценарная.
    /// </summary>
    public class EnemyOnslaughtDirector : MonoBehaviour
    {
        public float Pressure { get; private set; }
        public float Threshold => Game.Config.randomWaveThreshold;
        public readonly List<OnslaughtWave> Waves = new List<OnslaughtWave>();
        public int WavesRepelled { get; private set; }
        public OnslaughtWave Incoming { get; private set; }

        float _lastWaveTime = -999f;
        float _lastSpikeTime = -999f;
        int _nightWaveDay = 0;
        int _nextId = 1;
        const float SpikeLevel = 45f;

        public void Init()
        {
            GameEvents.NightStarted += OnNight;
        }

        void OnDestroy() { GameEvents.NightStarted -= OnNight; }

        void OnNight(int day)
        {
            if (day < Game.Config.firstNightWaveDay || _nightWaveDay == day) return;
            _nightWaveDay = day;
            // ночная волна — через пару игровых часов после заката
            float delay = 2f / Game.Clock.GameHoursPerSecond;
            float noiseK = 1f + Mathf.Clamp(Game.Noise.Level / 60f, 0f, 1.5f);
            Schedule(WaveTrigger.Night, Mathf.RoundToInt(BaseBudget() * 1.2f * noiseK), delay, "Ночная волна");
        }

        /// <summary>Базовый бюджет растёт с днями и рангом (чем выше ранг — тем серьёзнее угрозы).</summary>
        public int BaseBudget()
        {
            int day = Game.Clock.Day;
            int rank = (int)Game.Rank.Current;
            return Mathf.Max(4, Mathf.RoundToInt((5f + day * 3.5f + rank * 6f) * Game.Config.difficulty));
        }

        void Update()
        {
            if (!Game.IsPlaying) return;
            float dt = Time.deltaTime;
            var noise = Game.Noise;
            bool night = Game.Clock.IsNight;

            // 1) давление: шум + фон (растёт со временем без атак)
            float sinceLast = Time.time - _lastWaveTime;
            float baseline = (0.04f + Game.Clock.Day * 0.012f) * (night ? 1.8f : 1f);
            Pressure += dt * (noise.Level * 0.035f + baseline * Mathf.Clamp01(sinceLast / 180f));

            // 2) всплеск шума (взрыв у пещеры, канонада) — быстрая реакция фауны
            if (noise.Level > SpikeLevel && Time.time - _lastSpikeTime > 150f && Incoming == null)
            {
                _lastSpikeTime = Time.time;
                int budget = Mathf.RoundToInt(BaseBudget() * Mathf.Clamp(noise.Level / SpikeLevel, 1f, 2.2f) * 0.8f);
                Schedule(WaveTrigger.NoiseSpike, budget, 20f, "Встревоженная фауна");
                Pressure *= 0.5f;
            }

            // 3) накопленное давление → «неожиданная волна»
            if (Pressure >= Threshold && sinceLast > 120f && Incoming == null)
            {
                bool noisy = noise.Level > 8f;
                Schedule(noisy ? WaveTrigger.Noise : WaveTrigger.Random, Mathf.RoundToInt(BaseBudget() * (noisy ? 1f : 0.7f)), 18f,
                    noisy ? $"Волна на шум ({NoiseSystem.SourceName(noise.LoudestSource)})" : "Блуждающая стая");
                Pressure = 0f;
            }

            // 4) спавн и завершение волн
            for (int i = Waves.Count - 1; i >= 0; i--)
            {
                var w = Waves[i];
                if (w.Finished) continue;
                if (!w.Started && Time.time >= w.ArriveTime) Begin(w);
                if (w.Started)
                {
                    if (w.Pending.Count > 0 && Time.time >= w.NextSpawn) SpawnGroup(w);
                    if (w.Pending.Count == 0 && w.Alive <= 0) Finish(w);
                }
            }
            if (Incoming != null && (Incoming.Started || Incoming.Finished)) Incoming = null;
        }

        /// <summary>Запланировать волну. Можно вызывать из сюжета (сценарные волны).</summary>
        public OnslaughtWave Schedule(WaveTrigger trigger, int budget, float delaySeconds, string label, Vector3? direction = null)
        {
            var basePos = Game.Reactor != null ? Game.Reactor.transform.position : Game.World.Center;
            var w = new OnslaughtWave
            {
                Id = _nextId++,
                Trigger = trigger,
                Label = label,
                Budget = budget,
                Direction = direction ?? Game.Noise.DominantDirection(basePos),
            };
            // сенсоры дают больше времени на подготовку
            bool sensors = Game.Defense != null && Game.Defense.HasSensors;
            w.ArriveTime = Time.time + delaySeconds * (sensors ? 1.6f : 1f);
            w.SpawnCenter = EdgePoint(basePos, w.Direction);
            Compose(w);
            Waves.Add(w);
            Incoming = w;
            _lastWaveTime = Time.time;

            string dir = DirectionName(w.Direction);
            string msg = sensors
                ? $"Сенсоры: {label.ToLower()} с направления «{dir}». Состав: {w.CompositionText()}. Подход ~{Mathf.RoundToInt(w.ArriveTime - Time.time)} с."
                : $"Сейсмодатчики: {label.ToLower()} с направления «{dir}», примерно {w.Total} особей.";
            Game.Log?.Alert(msg);
            Game.Sound?.Play2D(Sfx.Alarm, 0.7f);
            return w;
        }

        Vector3 EdgePoint(Vector3 basePos, Vector3 dir)
        {
            float half = WorldGenerator.WorldSize * 0.5f;
            var p = basePos + dir * (half - 10f);
            p.x = Mathf.Clamp(p.x, 8f, WorldGenerator.WorldSize - 8f);
            p.z = Mathf.Clamp(p.z, 8f, WorldGenerator.WorldSize - 8f);
            return p;
        }

        /// <summary>Состав волны под бюджет: учитывает, что построил игрок (много узлов — больше грызунов и т.д.).</summary>
        void Compose(OnslaughtWave w)
        {
            int day = Game.Clock.Day;
            var rank = Game.Rank.Current;
            int smallCount = 0, mechCount = 0, fences = 0;
            foreach (var s in Registry.Structures)
            {
                if (s == null || !s.IsBuilt) continue;
                if (s.Def.IsSmall) smallCount++;
                if (s.Def.IsMechanism) mechCount++;
                if (s.Def.Jumpable) fences++;
            }
            var options = new List<(EnemyKind kind, float weight)>();
            foreach (var a in EnemyArchetype.All)
            {
                if (a.Fauna || day < a.MinDay || rank < a.MinRank) continue;
                if (!string.IsNullOrEmpty(a.RequiredFlag) && !Game.Entry.HasFlag(a.RequiredFlag)) continue;
                float wgt = 1f;
                switch (a.Kind)
                {
                    case EnemyKind.Swarmer: wgt = 3f + smallCount * 0.15f; break;
                    case EnemyKind.Jumper: wgt = 2f + mechCount * 0.2f + fences * 0.05f; break;
                    case EnemyKind.Brute: wgt = 2.5f; break;
                    case EnemyKind.Spitter: wgt = 1.6f; break;
                    case EnemyKind.Tank: wgt = 0.8f; break;
                    case EnemyKind.Synth: wgt = 2.2f; break;
                }
                options.Add((a.Kind, wgt));
            }
            int budget = Mathf.Max(3, w.Budget);
            int tanks = 0;
            var list = new List<EnemyKind>();
            int guard = 0;
            while (budget > 0 && guard++ < 200)
            {
                float total = 0f;
                foreach (var o in options)
                {
                    var a = EnemyArchetype.Get(o.kind);
                    if (a.Cost <= budget && !(o.kind == EnemyKind.Tank && tanks >= 1 + day / 10)) total += o.weight;
                }
                if (total <= 0f) break;
                float r = Random.value * total;
                foreach (var o in options)
                {
                    var a = EnemyArchetype.Get(o.kind);
                    if (a.Cost > budget || (o.kind == EnemyKind.Tank && tanks >= 1 + day / 10)) continue;
                    r -= o.weight;
                    if (r <= 0f)
                    {
                        list.Add(o.kind);
                        budget -= a.Cost;
                        if (o.kind == EnemyKind.Tank) tanks++;
                        break;
                    }
                }
            }
            // мелочь — в начало (бегут быстрее), танк — в конец
            list.Sort((a, b) => EnemyArchetype.Get(a).Cost.CompareTo(EnemyArchetype.Get(b).Cost));
            foreach (var k in list)
            {
                w.Pending.Enqueue(k);
                w.Composition.TryGetValue(k, out int n);
                w.Composition[k] = n + 1;
            }
            w.Total = list.Count;
        }

        void Begin(OnslaughtWave w)
        {
            w.Started = true;
            w.NextSpawn = Time.time;
            Game.Log?.Alert($"{w.Label}: контакт! ({DirectionName(w.Direction)})");
            Game.Sound?.Play2D(Sfx.Alarm, 0.9f);
            Game.Sound?.PlayAt(Sfx.Screech, w.SpawnCenter, 1f);
            GameEvents.WaveBegan(w);
        }

        void SpawnGroup(OnslaughtWave w)
        {
            int n = Random.Range(2, 6);
            var perp = new Vector3(-w.Direction.z, 0, w.Direction.x);
            for (int i = 0; i < n && w.Pending.Count > 0; i++)
            {
                var kind = w.Pending.Dequeue();
                var p = w.SpawnCenter + perp * Random.Range(-18f, 18f) + w.Direction * Random.Range(-4f, 4f);
                p.x = Mathf.Clamp(p.x, 4f, WorldGenerator.WorldSize - 4f);
                p.z = Mathf.Clamp(p.z, 4f, WorldGenerator.WorldSize - 4f);
                Enemy.Spawn(kind, p, w);
                w.Spawned++;
                w.Alive++;
                if (kind == EnemyKind.Tank)
                {
                    Game.Log?.Alert("ТАНК замечен! Он идёт прямо к реактору — сосредоточьте огонь лазеров и тяжёлых турелей.");
                    Game.Sound?.PlayAt(Sfx.Roar, p, 1f);
                }
            }
            w.NextSpawn = Time.time + Random.Range(1.5f, 3f);
        }

        void Finish(OnslaughtWave w)
        {
            w.Finished = true;
            WavesRepelled++;
            int rp = 6 + w.Budget / 2;
            Game.Rank.AddPoints(rp, $"волна отражена ({w.Label.ToLower()})");
            Game.Log?.Info($"Волна отражена: уничтожено {w.Killed}.");
            GameEvents.WaveFinished(w);
        }

        public bool AnyActive
        {
            get
            {
                foreach (var w in Waves) if (w.Started && !w.Finished) return true;
                return false;
            }
        }

        public static string DirectionName(Vector3 d)
        {
            float a = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            if (a < 0) a += 360f;
            string[] names = { "север", "северо-восток", "восток", "юго-восток", "юг", "юго-запад", "запад", "северо-запад" };
            return names[Mathf.RoundToInt(a / 45f) % 8];
        }

        public void DebugWave() => Schedule(WaveTrigger.Debug, BaseBudget(), 5f, "Тестовая волна");
        public void RestorePressure(float p) { Pressure = p; }
    }
}
