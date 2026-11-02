using System;
using UnityEngine;

namespace Echo7
{
    /// <summary>
    /// Настройки партии. Лежат на компоненте Echo7Entry, поэтому их можно крутить прямо в инспекторе.
    /// </summary>
    [Serializable]
    public class GameConfig
    {
        [Header("Мир")]
        [Tooltip("Зерно генерации карты. Одно и то же зерно — одна и та же планета.")]
        public int seed = 7;
        [Tooltip("Длина суток в реальных минутах.")]
        public float dayLengthMinutes = 12f;
        [Range(0, 23)] public int startHour = 7;

        [Header("Сложность")]
        [Tooltip("Общий множитель силы волн.")]
        public float difficulty = 1f;
        [Tooltip("Период полураспада шума (секунды игрового времени).")]
        public float noiseHalfLife = 45f;
        [Tooltip("Сколько «давления» нужно накопить для случайной волны.")]
        public float randomWaveThreshold = 100f;
        [Tooltip("Первая ночь, когда приходит гарантированная волна.")]
        public int firstNightWaveDay = 1;

        [Header("Системы")]
        public bool enableFauna = true;
        public bool enableWeather = true;
        public bool enableVipVisits = true;

        [Header("Отладка")]
        [Tooltip("F6 — ресурсы, F7 — волна, F8 — ранг, F10 — смена погоды, F5/F9 — сохранение/загрузка.")]
        public bool debugKeys = true;
        public bool godMode = false;

        [Header("Интерфейс")]
        [Range(0.6f, 1.6f)] public float uiScale = 1f;
    }
}
