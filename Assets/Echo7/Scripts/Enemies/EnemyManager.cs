using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    /// <summary>Контейнер существ + пространственный хэш для расталкивания (без физики).</summary>
    public class EnemyManager : MonoBehaviour
    {
        const float Cell = 3f;
        readonly Dictionary<long, List<Enemy>> _hash = new Dictionary<long, List<Enemy>>();
        readonly Stack<List<Enemy>> _pool = new Stack<List<Enemy>>();
        int _builtFrame = -1;

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        void Rebuild()
        {
            if (_builtFrame == Time.frameCount) return;
            _builtFrame = Time.frameCount;
            foreach (var kv in _hash) { kv.Value.Clear(); _pool.Push(kv.Value); }
            _hash.Clear();
            foreach (var e in Registry.Enemies)
            {
                if (e == null || e.IsDead) continue;
                var p = e.transform.position;
                long k = Key(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell));
                if (!_hash.TryGetValue(k, out var list)) { list = _pool.Count > 0 ? _pool.Pop() : new List<Enemy>(); _hash[k] = list; }
                list.Add(e);
            }
        }

        public Vector3 Separation(Enemy self)
        {
            Rebuild();
            var p = self.transform.position;
            int cx = Mathf.FloorToInt(p.x / Cell), cz = Mathf.FloorToInt(p.z / Cell);
            var push = Vector3.zero;
            float myR = self.Archetype.Size * 0.6f;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!_hash.TryGetValue(Key(cx + dx, cz + dz), out var list)) continue;
                foreach (var o in list)
                {
                    if (o == self) continue;
                    var d = p - o.transform.position; d.y = 0;
                    float min = myR + o.Archetype.Size * 0.6f;
                    float m = d.magnitude;
                    if (m < min && m > 0.001f) push += d / m * (min - m) / min;
                }
            }
            return push;
        }

        public int CountHostile()
        {
            int n = 0;
            foreach (var e in Registry.Enemies) if (e != null && !e.IsDead && e.Wave != null) n++;
            return n;
        }
    }

    public enum NoiseSource { Player, Turret, Construction, Mining, Machinery, Explosion, Event }

    /// <summary>
    /// Шум аванпоста. Каждое действие (стройка, выстрелы, добыча, взрывы) добавляет шум в точке.
    /// Общий уровень экспоненциально затухает; недавние события хранят направление источника.
    /// Директор волн читает Level и DominantDirection.
    /// </summary>
    public class NoiseSystem : MonoBehaviour
    {
        public struct NoiseEvent { public Vector3 Pos; public float Amount; public float Time; public NoiseSource Source; }

        public float Level { get; private set; }
        public float Peak { get; private set; }
        readonly List<NoiseEvent> _recent = new List<NoiseEvent>();
        readonly float[] _bySource = new float[System.Enum.GetValues(typeof(NoiseSource)).Length];

        public float HalfLife => Mathf.Max(5f, Game.Config.noiseHalfLife);

        public void Emit(Vector3 pos, float amount, NoiseSource src)
        {
            if (amount <= 0f) return;
            if (src == NoiseSource.Player || src == NoiseSource.Mining) amount *= Game.Skills != null ? Game.Skills.NoiseMult : 1f;
            Level += amount;
            Peak = Mathf.Max(Peak, Level);
            _bySource[(int)src] += amount;
            if (amount >= 1f) _recent.Add(new NoiseEvent { Pos = pos, Amount = amount, Time = Time.time, Source = src });
            if (amount >= 15f) ScareFauna(pos, amount);
        }

        /// <summary>Ложный шум приманки: влияет на направление волн и пугает/манит фауну, но почти не растит угрозу.</summary>
        public void EmitDecoy(Vector3 pos, float amount)
        {
            Level += amount * 0.05f;
            _recent.Add(new NoiseEvent { Pos = pos, Amount = amount * 3f, Time = Time.time, Source = NoiseSource.Event });
            // хищники идут на звук
            foreach (var e in Registry.Enemies)
                if (e != null && !e.IsDead && e.Wave == null && e.Archetype.Kind == EnemyKind.Stalker && (e.transform.position - pos).sqrMagnitude < 60f * 60f)
                    e.Home = pos;
        }

        /// <summary>Непрерывный шум (работа механизмов): копится без записи отдельных событий.</summary>
        public void EmitContinuous(Vector3 pos, float amount, NoiseSource src)
        {
            if (amount <= 0f) return;
            Level += amount;
            _bySource[(int)src] += amount;
            if (Random.value < 0.01f) _recent.Add(new NoiseEvent { Pos = pos, Amount = amount * 100f, Time = Time.time, Source = src });
        }

        void ScareFauna(Vector3 pos, float amount)
        {
            float r = Mathf.Min(80f, amount * 1.5f);
            foreach (var e in Registry.Enemies)
                if (e != null && !e.IsDead && e.Wave == null && (e.transform.position - pos).sqrMagnitude < r * r) e.Scare(pos);
        }

        void Update()
        {
            float k = Mathf.Pow(0.5f, Time.deltaTime / HalfLife);
            Level *= k;
            Peak = Mathf.Max(Level, Peak * Mathf.Pow(0.5f, Time.deltaTime / 20f));
            for (int i = 0; i < _bySource.Length; i++) _bySource[i] *= k;
            _recent.RemoveAll(e => Time.time - e.Time > 120f);
        }

        /// <summary>Направление (от базы) самого громкого недавнего источника — откуда придёт волна.</summary>
        public Vector3 DominantDirection(Vector3 basePos)
        {
            var sum = Vector3.zero;
            foreach (var e in _recent)
            {
                var d = e.Pos - basePos; d.y = 0;
                float age = Time.time - e.Time;
                float w = e.Amount * Mathf.Pow(0.5f, age / 40f);
                sum += (d.sqrMagnitude > 1f ? d.normalized : Random.insideUnitSphere) * w * (1f + d.magnitude / 40f);
            }
            sum.y = 0;
            if (sum.sqrMagnitude < 0.01f) { var r = Random.insideUnitCircle.normalized; return new Vector3(r.x, 0, r.y); }
            return sum.normalized;
        }

        public float SourceLevel(NoiseSource s) => _bySource[(int)s];

        public NoiseSource LoudestSource
        {
            get
            {
                int best = 0;
                for (int i = 1; i < _bySource.Length; i++) if (_bySource[i] > _bySource[best]) best = i;
                return (NoiseSource)best;
            }
        }

        public static string SourceName(NoiseSource s)
        {
            switch (s)
            {
                case NoiseSource.Player: return "стрельба";
                case NoiseSource.Turret: return "турели";
                case NoiseSource.Construction: return "стройка";
                case NoiseSource.Mining: return "добыча";
                case NoiseSource.Machinery: return "механизмы";
                case NoiseSource.Explosion: return "взрывы";
            }
            return "события";
        }

        public void Restore(float level) { Level = level; }
    }
}
