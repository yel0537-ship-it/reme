using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    public enum WeatherKind { Clear, Heatwave, DustStorm, Rain, InsectMigration }

    /// <summary>
    /// Экологический цикл планеты: пекло (вода испаряется, саванна превращается в пустыню),
    /// пылевая буря, ливни (озёра наполняются, луга и деревья возвращаются), миграция насекомых (болезни).
    /// </summary>
    public class WeatherSystem : MonoBehaviour
    {
        public WeatherKind Current { get; private set; } = WeatherKind.Clear;
        /// <summary>0 — пустыня, 1 — зелёная саванна.</summary>
        public float Greenness { get; private set; } = 1f;
        /// <summary>0 — озёра пересохли, 1 — полные.</summary>
        public float WaterLevel { get; private set; } = 1f;
        public float HoursLeft { get; private set; }
        public int NextEventDay { get; private set; } = 3;

        ParticleSystem _rain, _dust;
        readonly List<InsectSwarm> _swarms = new List<InsectSwarm>();
        System.Random _rnd;
        float _paintTimer;

        public bool IsHot => Current == WeatherKind.Heatwave || Current == WeatherKind.DustStorm;
        public bool IsRaining => Current == WeatherKind.Rain;

        /// <summary>Множитель выработки влагоконденсаторов.</summary>
        public float CondenserFactor => IsRaining ? 2f : IsHot ? 0.3f : 1f;
        /// <summary>Множитель солнечных панелей.</summary>
        public float SolarFactor => Current == WeatherKind.DustStorm ? 0.3f : Current == WeatherKind.Rain ? 0.45f : Current == WeatherKind.Heatwave ? 1.4f : 1f;
        /// <summary>Множитель дальности турелей и обзора (буря/ливень).</summary>
        public float VisibilityFactor => Current == WeatherKind.DustStorm ? 0.6f : Current == WeatherKind.Rain ? 0.85f : 1f;
        /// <summary>Множитель износа механизмов (жара).</summary>
        public float WearFactor => IsHot ? 1.6f : 1f;

        public string Label
        {
            get
            {
                switch (Current)
                {
                    case WeatherKind.Heatwave: return "Пекло";
                    case WeatherKind.DustStorm: return "Пылевая буря";
                    case WeatherKind.Rain: return "Ливни";
                    case WeatherKind.InsectMigration: return "Миграция насекомых";
                    default: return "Ясно";
                }
            }
        }

        public void Init(int seed)
        {
            _rnd = new System.Random(seed * 31 + 5);
            var cam = Game.CameraRig != null ? Game.CameraRig.transform : transform;
            _rain = Game.Fx.CreateLooping("Rain", cam, new Color(0.75f, 0.82f, 0.95f, 0.45f), 0f, 1.1f, 22f, 0.06f,
                ParticleSystemShapeType.Box, new Vector3(60f, 1f, 60f), 0f, true);
            _rain.transform.localPosition = new Vector3(0, 18f, 10f);
            _rain.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            _dust = Game.Fx.CreateLooping("Dust", cam, new Color(0.75f, 0.6f, 0.4f, 0.22f), 0f, 4f, 6f, 6f,
                ParticleSystemShapeType.Box, new Vector3(70f, 10f, 70f));
            _dust.transform.localPosition = new Vector3(0, 0, 15f);
            GameEvents.DayStarted += OnDay;
            GameEvents.HourPassed += OnHour;
        }

        void OnDestroy()
        {
            GameEvents.DayStarted -= OnDay;
            GameEvents.HourPassed -= OnHour;
        }

        void OnDay(int day)
        {
            if (!Game.Config.enableWeather || Current != WeatherKind.Clear) return;
            if (Game.Tutorial != null && !Game.Tutorial.AllowWeather) { NextEventDay = Mathf.Max(NextEventDay, day + 2); return; }
            if (day < NextEventDay) return;
            // Цикл: пекло → (буря) → ливни; иногда миграция насекомых
            double r = _rnd.NextDouble();
            if (r < 0.55) Begin(WeatherKind.Heatwave, 24f * (2 + _rnd.Next(2)));
            else if (r < 0.8) Begin(WeatherKind.InsectMigration, 14f);
            else Begin(WeatherKind.Rain, 24f);
        }

        void OnHour(int hour)
        {
            if (Current == WeatherKind.Clear) return;
            HoursLeft -= 1f;
            if (Current == WeatherKind.Heatwave && HoursLeft > 6f && _rnd.NextDouble() < 0.05)
            {
                Begin(WeatherKind.DustStorm, 6f + _rnd.Next(6));
                return;
            }
            if (HoursLeft > 0f) return;
            switch (Current)
            {
                case WeatherKind.Heatwave:
                case WeatherKind.DustStorm:
                    // после пекла через некоторое время начинается дождь
                    Begin(WeatherKind.Rain, 24f + _rnd.Next(24));
                    break;
                default:
                    End();
                    break;
            }
        }

        public void Begin(WeatherKind kind, float hours)
        {
            Current = kind;
            HoursLeft = hours;
            switch (kind)
            {
                case WeatherKind.Heatwave:
                    Game.Log?.Alert("Метеослужба: надвигается пекло. Вода будет испаряться, растительность — выгорать.");
                    break;
                case WeatherKind.DustStorm:
                    Game.Log?.Alert("Пылевая буря! Видимость и дальность турелей снижены, солнечные панели почти не работают.");
                    break;
                case WeatherKind.Rain:
                    Game.Log?.Info("Начались ливни. Озёра наполняются, саванна оживает.");
                    break;
                case WeatherKind.InsectMigration:
                    Game.Log?.Alert("Сезон миграции насекомых. Держите гарнизон под крышей — укусы вызывают лихорадку.");
                    SpawnSwarms();
                    break;
            }
            GameEvents.Weather(kind);
        }

        void End()
        {
            Current = WeatherKind.Clear;
            NextEventDay = (Game.Clock != null ? Game.Clock.Day : 1) + 3 + _rnd.Next(4);
            foreach (var s in _swarms) if (s != null) s.Dissipate();
            _swarms.Clear();
            Game.Log?.Info("Погода стабилизировалась.");
            GameEvents.Weather(Current);
        }

        void SpawnSwarms()
        {
            int count = 3 + _rnd.Next(3);
            for (int i = 0; i < count; i++)
            {
                float a = (float)_rnd.NextDouble() * Mathf.PI * 2f;
                var c = Game.World.Center;
                var start = c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 120f;
                var end = c - new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 120f + new Vector3((float)_rnd.NextDouble() * 40f - 20f, 0, (float)_rnd.NextDouble() * 40f - 20f);
                _swarms.Add(InsectSwarm.Create(transform, start, end, 4f + (float)_rnd.NextDouble() * 3f, i * 2.5f));
            }
        }

        public void ForceNext()
        {
            if (Current == WeatherKind.Clear) { NextEventDay = 0; OnDay(Game.Clock.Day); }
            else { HoursLeft = 0f; OnHour(0); }
        }

        /// <summary>Для загрузки.</summary>
        public void Restore(WeatherKind kind, float hoursLeft, float green, float water, int nextDay)
        {
            Current = kind; HoursLeft = hoursLeft; Greenness = green; WaterLevel = water; NextEventDay = nextDay;
            Game.World.ApplyGreenness(Greenness);
            Game.World.ApplyWater(WaterLevel);
        }

        void Update()
        {
            if (!Game.IsPlaying) return;
            float hps = Game.Clock.GameHoursPerSecond * Time.deltaTime; // игровых часов за кадр

            // Зелень и вода реагируют на погоду
            float greenTarget = IsHot ? 0f : Current == WeatherKind.Rain ? 1f : Greenness;
            float greenRate = IsHot ? 1f / 30f : 1f / 36f; // ~30 ч до пустыни, ~36 ч до лугов
            Greenness = Mathf.MoveTowards(Greenness, greenTarget, greenRate * hps);
            float waterTarget = IsHot ? 0f : Current == WeatherKind.Rain ? 1f : WaterLevel;
            WaterLevel = Mathf.MoveTowards(WaterLevel, waterTarget, (IsHot ? 1f / 26f : 1f / 18f) * hps);

            _paintTimer -= Time.deltaTime;
            if (_paintTimer <= 0f)
            {
                _paintTimer = 1.5f;
                Game.World.ApplyGreenness(Greenness);
                Game.World.ApplyWater(WaterLevel);
            }

            // Испарение запасов воды на складе в пекло
            if (IsHot && Game.Bank != null)
                Game.Bank.Evaporate(ResourceType.Water, 0.012f * hps);

            // Визуал
            var rainEm = _rain.emission;
            rainEm.rateOverTime = IsRaining ? 1800f : 0f;
            var dustEm = _dust.emission;
            dustEm.rateOverTime = Current == WeatherKind.DustStorm ? 140f : Current == WeatherKind.Heatwave ? 6f : 0f;
            var clock = Game.Clock;
            float dimTarget = Current == WeatherKind.DustStorm ? 0.85f : IsRaining ? 0.6f : 0f;
            clock.WeatherDim = Mathf.MoveTowards(clock.WeatherDim, dimTarget, Time.deltaTime * 0.1f);
            var tintTarget = Current == WeatherKind.DustStorm ? new Color(1f, 0.75f, 0.5f) : Current == WeatherKind.Heatwave ? new Color(1f, 0.92f, 0.8f) : Color.white;
            clock.WeatherTint = Color.Lerp(clock.WeatherTint, tintTarget, Time.deltaTime * 0.2f);
            float fogTarget = Current == WeatherKind.DustStorm ? 0.85f : IsRaining ? 0.45f : 0f;
            clock.FogBoost = Mathf.MoveTowards(clock.FogBoost, fogTarget, Time.deltaTime * 0.08f);
            Game.Sound?.SetAmbient(Current == WeatherKind.DustStorm ? 0.5f : 0.08f, IsRaining ? 0.35f : 0f);
        }
    }

    /// <summary>Рой насекомых: облако частиц, медленно пересекающее карту. Заражает тех, кто снаружи.</summary>
    public class InsectSwarm : MonoBehaviour
    {
        Vector3 _from, _to;
        float _t, _duration, _delay;
        float _radius;
        ParticleSystem _ps;
        bool _dissipating;

        public static InsectSwarm Create(Transform parent, Vector3 from, Vector3 to, float radius, float delayHours)
        {
            var go = new GameObject("InsectSwarm");
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<InsectSwarm>();
            s._from = from; s._to = to; s._radius = radius;
            s._duration = 10f; // игровых часов на пересечение карты
            s._delay = delayHours;
            s._ps = Game.Fx.CreateLooping("swarm", go.transform, new Color(0.12f, 0.1f, 0.05f, 0.85f), 160f, 1.4f, 1.2f, 0.12f,
                ParticleSystemShapeType.Sphere, Vector3.one * radius * 2f);
            var main = s._ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            go.transform.position = from;
            return s;
        }

        public void Dissipate()
        {
            _dissipating = true;
            var em = _ps.emission;
            em.rateOverTime = 0f;
            Destroy(gameObject, 3f);
        }

        void Update()
        {
            if (_dissipating || !Game.IsPlaying) return;
            float hps = Game.Clock.GameHoursPerSecond * Time.deltaTime;
            if (_delay > 0f) { _delay -= hps; return; }
            _t += hps / _duration;
            var p = Vector3.Lerp(_from, _to, _t);
            p += new Vector3(Mathf.Sin(Time.time * 0.3f + _from.x), 0, Mathf.Cos(Time.time * 0.27f + _from.z)) * 6f;
            p.y = Game.Grid.SampleHeight(p) + 2f;
            transform.position = p;
            if (_t >= 1f) { Dissipate(); return; }

            // Заражение тех, кто снаружи
            float r2 = (_radius + 2f) * (_radius + 2f);
            foreach (var npc in Registry.Npcs)
            {
                if (npc == null || npc.Health.IsDead || npc.IsSheltered) continue;
                if ((npc.transform.position - p).sqrMagnitude < r2) npc.ExposeToInsects(Time.deltaTime);
            }
            var pl = Game.Player;
            if (pl != null && (pl.transform.position - p).sqrMagnitude < r2) pl.ExposeToInsects(Time.deltaTime);
        }
    }
}
