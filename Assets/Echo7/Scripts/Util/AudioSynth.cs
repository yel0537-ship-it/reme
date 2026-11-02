using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Echo7
{
    public enum Sfx
    {
        Rifle, MachineGun, Laser, Cannon, Explosion, HitFlesh, HitMetal, Build, BuildDone, Mining,
        Alarm, UiClick, UiError, Notify, RankUp, Screech, Roar, Jump, Pod, Shuttle, Grenade, Footstep, Chew, Heal
    }

    /// <summary>
    /// Синтез звуков-заглушек прямо в коде (никаких файлов). Замените клипы на настоящие через SoundManager.Override.
    /// </summary>
    public static class AudioSynth
    {
        const int Rate = 22050;
        static System.Random _rnd = new System.Random(1234);
        static float N() => (float)(_rnd.NextDouble() * 2.0 - 1.0);

        static AudioClip Make(string name, float seconds, Func<float, float, float> f, bool loop = false)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            float peak = 0.0001f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                data[i] = f(t, t / seconds);
                peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            }
            float norm = 0.9f / peak;
            for (int i = 0; i < n; i++) data[i] *= norm;
            if (loop)
            {
                // плавная склейка концов
                int fade = Mathf.Min(n / 8, 2000);
                for (int i = 0; i < fade; i++)
                {
                    float k = (float)i / fade;
                    data[i] = data[i] * k + data[n - fade + i] * (1f - k);
                }
                Array.Resize(ref data, n - fade);
                n = data.Length;
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Env(float p, float attack, float decayPow) => p < attack ? p / attack : Mathf.Pow(1f - (p - attack) / (1f - attack), decayPow);

        public static Dictionary<Sfx, AudioClip> BuildAll()
        {
            var d = new Dictionary<Sfx, AudioClip>();
            float lp = 0f;
            d[Sfx.Rifle] = Make("rifle", 0.22f, (t, p) =>
            {
                lp = Mathf.Lerp(lp, N(), 0.35f);
                return (lp * 0.9f + Mathf.Sin(t * 2f * Mathf.PI * (900f - 600f * p)) * 0.5f) * Env(p, 0.02f, 3f);
            });
            d[Sfx.MachineGun] = Make("mg", 0.12f, (t, p) =>
            {
                lp = Mathf.Lerp(lp, N(), 0.5f);
                return (lp + Mathf.Sin(t * 2f * Mathf.PI * 160f) * 0.6f) * Env(p, 0.01f, 2.5f);
            });
            d[Sfx.Laser] = Make("laser", 0.35f, (t, p) =>
            {
                float f = Mathf.Lerp(1800f, 350f, Mathf.Sqrt(p));
                return (Mathf.Sin(t * 2f * Mathf.PI * f) + 0.4f * Mathf.Sin(t * 2f * Mathf.PI * f * 1.5f)) * Env(p, 0.02f, 1.5f);
            });
            d[Sfx.Cannon] = Make("cannon", 0.8f, (t, p) =>
            {
                lp = Mathf.Lerp(lp, N(), 0.08f);
                return (lp * 2.5f + Mathf.Sin(t * 2f * Mathf.PI * Mathf.Lerp(90f, 35f, p)) * 0.8f) * Env(p, 0.01f, 2.2f);
            });
            d[Sfx.Explosion] = Make("explosion", 1.4f, (t, p) =>
            {
                lp = Mathf.Lerp(lp, N(), 0.05f + 0.1f * (1f - p));
                return (lp * 3f + Mathf.Sin(t * 2f * Mathf.PI * Mathf.Lerp(60f, 25f, p)) * 0.6f) * Env(p, 0.01f, 1.6f);
            });
            d[Sfx.Grenade] = Make("grenade", 0.9f, (t, p) =>
            {
                lp = Mathf.Lerp(lp, N(), 0.12f);
                return (lp * 2f + Mathf.Sin(t * 2f * Mathf.PI * 70f) * 0.5f) * Env(p, 0.01f, 2f);
            });
            d[Sfx.HitFlesh] = Make("hit_flesh", 0.15f, (t, p) =>
            {
                lp = Mathf.Lerp(lp, N(), 0.2f);
                return (lp + Mathf.Sin(t * 2f * Mathf.PI * 120f) * 0.4f) * Env(p, 0.02f, 2f);
            });
            d[Sfx.HitMetal] = Make("hit_metal", 0.35f, (t, p) =>
                (Mathf.Sin(t * 2f * Mathf.PI * 1310f) * 0.5f + Mathf.Sin(t * 2f * Mathf.PI * 2730f) * 0.3f + Mathf.Sin(t * 2f * Mathf.PI * 4170f) * 0.2f + N() * 0.15f * (1f - p)) * Env(p, 0.005f, 3f));
            d[Sfx.Build] = Make("build", 0.4f, (t, p) =>
            {
                float saw = (t * 220f) % 1f * 2f - 1f;
                return (saw * 0.4f + N() * 0.6f * (0.5f + 0.5f * Mathf.Sin(t * 70f))) * Env(p, 0.05f, 1f);
            });
            d[Sfx.BuildDone] = Make("build_done", 0.5f, (t, p) =>
                (Mathf.Sin(t * 2f * Mathf.PI * (p < 0.4f ? 660f : 990f)) * 0.6f) * Env(p, 0.02f, 1.2f));
            d[Sfx.Mining] = Make("mining", 0.25f, (t, p) =>
                (Mathf.Sin(t * 2f * Mathf.PI * 1900f) * 0.4f + N() * 0.6f) * Env(p, 0.005f, 4f));
            d[Sfx.Alarm] = Make("alarm", 1.6f, (t, p) =>
            {
                float f = (t % 0.8f) < 0.4f ? 620f : 820f;
                return Mathf.Sign(Mathf.Sin(t * 2f * Mathf.PI * f)) * 0.35f * Env(p, 0.02f, 0.4f);
            });
            d[Sfx.UiClick] = Make("ui_click", 0.05f, (t, p) => Mathf.Sin(t * 2f * Mathf.PI * 1400f) * Env(p, 0.05f, 3f));
            d[Sfx.UiError] = Make("ui_error", 0.25f, (t, p) => Mathf.Sign(Mathf.Sin(t * 2f * Mathf.PI * 140f)) * 0.4f * Env(p, 0.02f, 1f));
            d[Sfx.Notify] = Make("notify", 0.45f, (t, p) => Mathf.Sin(t * 2f * Mathf.PI * (p < 0.45f ? 880f : 1320f)) * Env(p, 0.02f, 1.5f));
            d[Sfx.RankUp] = Make("rankup", 1.2f, (t, p) =>
            {
                float[] notes = { 523f, 659f, 784f, 1046f };
                int k = Mathf.Min(3, (int)(p * 5f));
                return (Mathf.Sin(t * 2f * Mathf.PI * notes[k]) + 0.3f * Mathf.Sin(t * 4f * Mathf.PI * notes[k])) * Env(p, 0.02f, 0.8f);
            });
            d[Sfx.Screech] = Make("screech", 0.6f, (t, p) =>
            {
                float f = 900f + 500f * Mathf.Sin(t * 37f) - 400f * p;
                return (Mathf.Sin(t * 2f * Mathf.PI * f + Mathf.Sin(t * 230f) * 3f) * 0.6f + N() * 0.3f) * Env(p, 0.05f, 1.5f);
            });
            d[Sfx.Roar] = Make("roar", 1.6f, (t, p) =>
            {
                lp = Mathf.Lerp(lp, N(), 0.06f);
                float f = 55f + 25f * Mathf.Sin(t * 9f);
                return (Mathf.Sin(t * 2f * Mathf.PI * f + Mathf.Sin(t * 2f * Mathf.PI * f * 0.5f) * 2f) * 0.7f + lp * 1.5f) * Env(p, 0.15f, 1.2f);
            });
            d[Sfx.Jump] = Make("jump", 0.35f, (t, p) => N() * 0.5f * Mathf.Sin(p * Mathf.PI));
            d[Sfx.Pod] = Make("pod", 2.2f, (t, p) =>
            {
                lp = Mathf.Lerp(lp, N(), 0.1f);
                float impact = p > 0.8f ? Mathf.Sin(t * 2f * Mathf.PI * 50f) * (1f - (p - 0.8f) / 0.2f) : 0f;
                return lp * (p < 0.8f ? p : 0.3f) + impact;
            });
            d[Sfx.Shuttle] = Make("shuttle", 3f, (t, p) =>
            {
                lp = Mathf.Lerp(lp, N(), 0.05f);
                return (lp * 1.2f + Mathf.Sin(t * 2f * Mathf.PI * 110f) * 0.3f) * Mathf.Sin(p * Mathf.PI);
            });
            d[Sfx.Footstep] = Make("step", 0.08f, (t, p) => { lp = Mathf.Lerp(lp, N(), 0.15f); return lp * Env(p, 0.05f, 3f); });
            d[Sfx.Chew] = Make("chew", 0.3f, (t, p) => N() * (0.5f + 0.5f * Mathf.Sign(Mathf.Sin(t * 60f))) * Env(p, 0.02f, 1.5f));
            d[Sfx.Heal] = Make("heal", 0.6f, (t, p) => Mathf.Sin(t * 2f * Mathf.PI * (500f + 600f * p)) * Env(p, 0.1f, 1f));
            return d;
        }

        public static AudioClip WindLoop() => Make("wind", 4f, (t, p) =>
        {
            float g = 0.5f + 0.5f * Mathf.Sin(t * 1.3f) * Mathf.Sin(t * 0.7f + 1f);
            return (Mathf.PerlinNoise(t * 400f, 0.5f) - 0.5f) * g + N() * 0.05f;
        }, true);

        public static AudioClip RainLoop() => Make("rain", 3f, (t, p) => N() * 0.6f + (Random.value < 0.002f ? 1f : 0f), true);
    }

    /// <summary>Пул источников звука + эмбиент. Если есть настоящие клипы — вызовите Override(Sfx, clip).</summary>
    public class SoundManager : MonoBehaviour
    {
        Dictionary<Sfx, AudioClip> _clips;
        readonly List<AudioSource> _pool = new List<AudioSource>();
        int _next;
        AudioSource _ui, _wind, _rain;
        readonly Dictionary<Sfx, float> _lastPlay = new Dictionary<Sfx, float>();
        public float MasterVolume = 0.8f;

        public void Init()
        {
            _clips = AudioSynth.BuildAll();
            for (int i = 0; i < 24; i++)
            {
                var src = new GameObject("sfx" + i).AddComponent<AudioSource>();
                src.transform.SetParent(transform);
                src.playOnAwake = false;
                src.spatialBlend = 1f;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 6f;
                src.maxDistance = 90f;
                src.dopplerLevel = 0f;
                _pool.Add(src);
            }
            _ui = gameObject.AddComponent<AudioSource>();
            _ui.playOnAwake = false;
            _ui.spatialBlend = 0f;
            _wind = gameObject.AddComponent<AudioSource>();
            _wind.clip = AudioSynth.WindLoop(); _wind.loop = true; _wind.volume = 0.08f; _wind.spatialBlend = 0f; _wind.Play();
            _rain = gameObject.AddComponent<AudioSource>();
            _rain.clip = AudioSynth.RainLoop(); _rain.loop = true; _rain.volume = 0f; _rain.spatialBlend = 0f; _rain.Play();
        }

        public void Override(Sfx id, AudioClip clip) { if (clip != null) _clips[id] = clip; }

        public void PlayAt(Sfx id, Vector3 pos, float volume = 1f, float pitchJitter = 0.08f, float minInterval = 0.03f)
        {
            if (_clips == null || !_clips.TryGetValue(id, out var clip)) return;
            float now = Time.unscaledTime;
            if (_lastPlay.TryGetValue(id, out var last) && now - last < minInterval) return;
            _lastPlay[id] = now;
            var src = _pool[_next];
            _next = (_next + 1) % _pool.Count;
            src.transform.position = pos;
            src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            src.volume = volume * MasterVolume;
            src.clip = clip;
            src.Play();
        }

        public void Play2D(Sfx id, float volume = 1f)
        {
            if (_clips == null || !_clips.TryGetValue(id, out var clip)) return;
            _ui.PlayOneShot(clip, volume * MasterVolume);
        }

        public void SetAmbient(float wind, float rain)
        {
            if (_wind != null) _wind.volume = Mathf.Lerp(_wind.volume, wind * MasterVolume, Time.unscaledDeltaTime * 0.5f);
            if (_rain != null) _rain.volume = Mathf.Lerp(_rain.volume, rain * MasterVolume, Time.unscaledDeltaTime * 0.5f);
        }
    }
}
