using UnityEngine;
using UnityEngine.Rendering;

namespace Echo7
{
    /// <summary>
    /// Сутки: днём разведка и стройка, ночью — оборона. Управляет солнцем, небом, туманом и освещением базы.
    /// </summary>
    public class DayNightCycle : MonoBehaviour
    {
        public float Hour { get; private set; }
        public int Day { get; private set; } = 1;
        public Light Sun { get; private set; }
        public bool IsNight => Hour >= 20f || Hour < 5f;
        public float DayLengthSeconds => Mathf.Max(60f, Game.Config.dayLengthMinutes * 60f);
        /// <summary>Скорость хода времени; 0, пока обучение держит время остановленным.</summary>
        public float GameHoursPerSecond => Frozen ? 0f : BaseHoursPerSecond;
        /// <summary>Номинальная скорость (для пересчёта «игровые часы → секунды»).</summary>
        public float BaseHoursPerSecond => 24f / DayLengthSeconds;
        public bool Frozen => Game.Tutorial != null && Game.Tutorial.ClockFrozen;
        /// <summary>Абсолютное игровое время в часах с начала партии.</summary>
        public float TotalHours => (Day - 1) * 24f + Hour;
        /// <summary>0 ночью, 1 в полдень — для солнечных панелей.</summary>
        public float Daylight => Mathf.Clamp01(Mathf.Sin((Hour - 5f) / 15f * Mathf.PI));

        Material _sky;
        int _lastHour = -1;
        bool _wasNight;
        public float WeatherDim = 0f;      // затемнение от бури/дождя 0..1
        public Color WeatherTint = Color.white;
        public float FogBoost;             // доп. плотность тумана (пыль/дождь)

        static readonly Gradient SunColor = MakeGradient(
            (0.00f, new Color(0.25f, 0.30f, 0.50f)),
            (0.22f, new Color(0.30f, 0.35f, 0.55f)),
            (0.25f, new Color(1.00f, 0.50f, 0.25f)),
            (0.32f, new Color(1.00f, 0.80f, 0.60f)),
            (0.50f, new Color(1.00f, 0.96f, 0.88f)),
            (0.72f, new Color(1.00f, 0.78f, 0.55f)),
            (0.80f, new Color(1.00f, 0.45f, 0.22f)),
            (0.84f, new Color(0.35f, 0.32f, 0.55f)),
            (1.00f, new Color(0.25f, 0.30f, 0.50f)));

        static readonly Gradient SkyColor = MakeGradient(
            (0.00f, new Color(0.04f, 0.05f, 0.10f)),
            (0.22f, new Color(0.08f, 0.08f, 0.16f)),
            (0.27f, new Color(0.75f, 0.55f, 0.50f)),
            (0.40f, new Color(0.55f, 0.68f, 0.85f)),
            (0.60f, new Color(0.52f, 0.66f, 0.85f)),
            (0.78f, new Color(0.85f, 0.55f, 0.45f)),
            (0.86f, new Color(0.12f, 0.10f, 0.20f)),
            (1.00f, new Color(0.04f, 0.05f, 0.10f)));

        static readonly Gradient FogColor = MakeGradient(
            (0.00f, new Color(0.06f, 0.07f, 0.12f)),
            (0.25f, new Color(0.80f, 0.55f, 0.40f)),
            (0.40f, new Color(0.78f, 0.80f, 0.82f)),
            (0.62f, new Color(0.80f, 0.80f, 0.78f)),
            (0.78f, new Color(0.92f, 0.58f, 0.40f)),
            (0.86f, new Color(0.10f, 0.09f, 0.16f)),
            (1.00f, new Color(0.06f, 0.07f, 0.12f)));

        static Gradient MakeGradient(params (float t, Color c)[] keys)
        {
            var g = new Gradient();
            var ck = new GradientColorKey[keys.Length];
            for (int i = 0; i < keys.Length; i++) ck[i] = new GradientColorKey(keys[i].c, keys[i].t);
            // у Gradient максимум 8 ключей
            if (ck.Length > 8)
            {
                var trimmed = new GradientColorKey[8];
                for (int i = 0; i < 8; i++) trimmed[i] = ck[Mathf.RoundToInt(i * (ck.Length - 1) / 7f)];
                ck = trimmed;
            }
            g.SetKeys(ck, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        public void Init(float startHour)
        {
            Hour = startHour;
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(transform, false);
            Sun = sunGo.AddComponent<Light>();
            Sun.type = LightType.Directional;
            Sun.shadows = LightShadows.Soft;
            Sun.shadowStrength = 0.85f;
            Sun.shadowBias = 0.05f;
            Sun.shadowNormalBias = 0.4f;
            RenderSettings.sun = Sun;

            var skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                _sky = new Material(skyShader);
                if (_sky.HasProperty("_SunSize")) _sky.SetFloat("_SunSize", 0.05f);
                if (_sky.HasProperty("_AtmosphereThickness")) _sky.SetFloat("_AtmosphereThickness", 1.25f);
                RenderSettings.skybox = _sky;
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 70f;
            RenderSettings.fogEndDistance = 260f;
            QualitySettings.shadowDistance = 90f;
            Apply();
        }

        void Update()
        {
            if (!Game.IsPlaying) return;
            Hour += Time.deltaTime * GameHoursPerSecond;
            if (Hour >= 24f)
            {
                Hour -= 24f;
                Day++;
                GameEvents.Day(Day);
            }
            int h = Mathf.FloorToInt(Hour);
            if (h != _lastHour)
            {
                _lastHour = h;
                GameEvents.Hour(h);
                if (h == 6) Game.Log?.Info($"День {Day}. Рассвет над саванной.");
            }
            bool night = IsNight;
            if (night && !_wasNight)
            {
                GameEvents.Night(Day);
                Game.Log?.Warn("Сумерки. Фауна активизируется — держите оборону.");
            }
            _wasNight = night;
            Apply();
        }

        /// <summary>Для загрузки сохранения.</summary>
        public void SetTime(int day, float hour)
        {
            Day = Mathf.Max(1, day);
            Hour = Mathf.Repeat(hour, 24f);
            _lastHour = Mathf.FloorToInt(Hour);
            _wasNight = IsNight;
            Apply();
        }

        void Apply()
        {
            float t = Hour / 24f;
            // Солнце: восход в 5:00, закат в 20:00, азимут медленно вращается
            float dayT = (Hour - 5f) / 15f;
            float elev;
            if (dayT >= 0f && dayT <= 1f) elev = Mathf.Sin(dayT * Mathf.PI) * 62f + 2f;
            else elev = 35f; // «луна»
            float az = -60f + Hour * 7.5f;
            Sun.transform.rotation = Quaternion.Euler(elev, az, 0f);

            float dim = 1f - WeatherDim * 0.55f;
            var sc = SunColor.Evaluate(t) * WeatherTint;
            bool day = dayT >= 0f && dayT <= 1f;
            float intensity = day ? Mathf.Lerp(0.35f, 1.25f, Mathf.Sin(dayT * Mathf.PI)) : 0.22f;
            Sun.color = sc;
            Sun.intensity = intensity * dim;

            var sky = SkyColor.Evaluate(t) * WeatherTint;
            RenderSettings.ambientSkyColor = sky * (0.9f * dim + 0.1f);
            RenderSettings.ambientEquatorColor = Color.Lerp(sky, FogColor.Evaluate(t), 0.5f) * 0.75f * dim;
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.18f, 0.12f) * (day ? 1f : 0.3f) * dim;
            var fog = FogColor.Evaluate(t) * WeatherTint;
            RenderSettings.fogColor = fog * (0.6f + 0.4f * dim);
            RenderSettings.fogStartDistance = Mathf.Lerp(70f, 8f, FogBoost);
            RenderSettings.fogEndDistance = Mathf.Lerp(260f, 70f, FogBoost);

            if (_sky != null)
            {
                if (_sky.HasProperty("_SkyTint")) _sky.SetColor("_SkyTint", Color.Lerp(new Color(0.5f, 0.5f, 0.5f), WeatherTint * 0.5f, WeatherDim));
                if (_sky.HasProperty("_Exposure")) _sky.SetFloat("_Exposure", (day ? 1.25f : 0.25f) * dim);
                if (_sky.HasProperty("_GroundColor")) _sky.SetColor("_GroundColor", new Color(0.35f, 0.30f, 0.25f) * dim);
            }
        }

        public string TimeString => $"{Mathf.FloorToInt(Hour):00}:{Mathf.FloorToInt((Hour % 1f) * 60f):00}";
    }
}
