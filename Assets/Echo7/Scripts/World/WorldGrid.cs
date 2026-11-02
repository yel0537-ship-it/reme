using System;
using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    /// <summary>
    /// Логическая сетка карты (2×2 м на клетку). Хранит занятость постройками, блокирующие объекты
    /// (скалы, залежи) и строит поля потоков для волн, атакующих реактор.
    /// </summary>
    public class WorldGrid
    {
        public const float CellSize = 2f;
        public readonly int Size;
        public readonly Vector3 Origin;
        public readonly Structure[] Occupant;
        public readonly byte[] Blocked;   // 1 — скала/залежь/вода, непроходимо и нельзя строить
        public readonly float[] Height;   // высота рельефа в центре клетки

        public FlowField GroundField { get; private set; }
        public FlowField JumperField { get; private set; }
        bool _fieldsDirty = true;
        float _lastRebuild = -10f;

        readonly Terrain _terrain;

        public WorldGrid(int size, Vector3 origin, Terrain terrain)
        {
            Size = size;
            Origin = origin;
            _terrain = terrain;
            Occupant = new Structure[size * size];
            Blocked = new byte[size * size];
            Height = new float[size * size];
            RefreshHeights();
            GroundField = new FlowField(this);
            JumperField = new FlowField(this);
        }

        public void RefreshHeights()
        {
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                Height[y * Size + x] = SampleHeight(CellCenterFlat(x, y));
        }

        public float SampleHeight(Vector3 world)
        {
            return _terrain != null ? _terrain.SampleHeight(world) + _terrain.transform.position.y : 0f;
        }

        public int Index(int x, int y) => y * Size + x;
        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Size && y < Size;
        public bool InBounds(Vector2Int c) => InBounds(c.x, c.y);

        public Vector2Int WorldToCell(Vector3 p)
        {
            return new Vector2Int(Mathf.FloorToInt((p.x - Origin.x) / CellSize), Mathf.FloorToInt((p.z - Origin.z) / CellSize));
        }

        public Vector3 CellCenterFlat(int x, int y) => new Vector3(Origin.x + (x + 0.5f) * CellSize, 0f, Origin.z + (y + 0.5f) * CellSize);

        public Vector3 CellCenter(int x, int y)
        {
            var p = CellCenterFlat(x, y);
            p.y = InBounds(x, y) ? Height[Index(x, y)] : SampleHeight(p);
            return p;
        }

        public Vector3 CellCenter(Vector2Int c) => CellCenter(c.x, c.y);

        /// <summary>Центр прямоугольника клеток в мире (на высоте рельефа).</summary>
        public Vector3 RectCenter(RectInt r)
        {
            var p = new Vector3(Origin.x + (r.x + r.width * 0.5f) * CellSize, 0f, Origin.z + (r.y + r.height * 0.5f) * CellSize);
            p.y = SampleHeight(p);
            return p;
        }

        public Structure StructureAt(int x, int y) => InBounds(x, y) ? Occupant[Index(x, y)] : null;
        public Structure StructureAt(Vector3 world) { var c = WorldToCell(world); return StructureAt(c.x, c.y); }

        public bool IsBlocked(int x, int y) => !InBounds(x, y) || Blocked[Index(x, y)] != 0;

        /// <summary>Проходима ли клетка для дружественного юнита (ворота — проходимы).</summary>
        public bool WalkableFriendly(int x, int y)
        {
            if (!InBounds(x, y) || Blocked[Index(x, y)] != 0) return false;
            var s = Occupant[Index(x, y)];
            return s == null || s.PassableForFriendly;
        }

        public bool IsAreaFree(RectInt r)
        {
            for (int y = r.yMin; y < r.yMax; y++)
            for (int x = r.xMin; x < r.xMax; x++)
                if (!InBounds(x, y) || Blocked[Index(x, y)] != 0 || Occupant[Index(x, y)] != null) return false;
            return true;
        }

        public float HeightVariance(RectInt r)
        {
            float min = float.MaxValue, max = float.MinValue;
            for (int y = r.yMin; y < r.yMax; y++)
            for (int x = r.xMin; x < r.xMax; x++)
            {
                if (!InBounds(x, y)) return 999f;
                float h = Height[Index(x, y)];
                min = Mathf.Min(min, h); max = Mathf.Max(max, h);
            }
            return max - min;
        }

        public void Occupy(Structure s, RectInt r)
        {
            for (int y = r.yMin; y < r.yMax; y++)
            for (int x = r.xMin; x < r.xMax; x++)
                if (InBounds(x, y)) Occupant[Index(x, y)] = s;
            MarkDirty();
        }

        public void Free(Structure s, RectInt r)
        {
            for (int y = r.yMin; y < r.yMax; y++)
            for (int x = r.xMin; x < r.xMax; x++)
                if (InBounds(x, y) && Occupant[Index(x, y)] == s) Occupant[Index(x, y)] = null;
            MarkDirty();
        }

        public void SetBlocked(Vector3 world, float radius, bool blocked)
        {
            var c = WorldToCell(world);
            int r = Mathf.CeilToInt(radius / CellSize);
            for (int y = c.y - r; y <= c.y + r; y++)
            for (int x = c.x - r; x <= c.x + r; x++)
            {
                if (!InBounds(x, y)) continue;
                if ((CellCenterFlat(x, y) - new Vector3(world.x, 0, world.z)).magnitude <= radius)
                    Blocked[Index(x, y)] = (byte)(blocked ? 1 : 0);
            }
            MarkDirty();
        }

        public void MarkDirty() => _fieldsDirty = true;

        /// <summary>Перестроить поля потоков (не чаще раза в 0.5 с).</summary>
        public void UpdateFields(IList<RectInt> targets)
        {
            if (!_fieldsDirty || Time.time - _lastRebuild < 0.5f) return;
            _fieldsDirty = false;
            _lastRebuild = Time.time;
            GroundField.Compute(targets, false);
            JumperField.Compute(targets, true);
        }

        /// <summary>Ближайшая свободная проходимая клетка к точке (для спавна и точек назначения).</summary>
        public Vector2Int NearestWalkable(Vector2Int c, int maxRadius = 12)
        {
            if (WalkableFriendly(c.x, c.y)) return c;
            for (int r = 1; r <= maxRadius; r++)
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;
                int x = c.x + dx, y = c.y + dy;
                if (WalkableFriendly(x, y)) return new Vector2Int(x, y);
            }
            return c;
        }
    }

    /// <summary>
    /// Поле потока к реактору (алгоритм Дейкстры по всей сетке). Постройки не непроходимы, а «дороги»:
    /// чем прочнее стена, тем выше стоимость прохода сквозь неё — так волна выбирает слабое место обороны.
    /// </summary>
    public class FlowField
    {
        readonly WorldGrid _g;
        public readonly float[] Dist;
        readonly BinaryHeap _heap;
        static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] Dy = { 0, 0, 1, -1, 1, -1, 1, -1 };
        static readonly float[] Cost = { 1f, 1f, 1f, 1f, 1.414f, 1.414f, 1.414f, 1.414f };

        public FlowField(WorldGrid g)
        {
            _g = g;
            Dist = new float[g.Size * g.Size];
            _heap = new BinaryHeap(g.Size * g.Size * 2);
            for (int i = 0; i < Dist.Length; i++) Dist[i] = float.MaxValue;
        }

        float CellCost(int idx, bool jumper)
        {
            if (_g.Blocked[idx] != 0) return float.MaxValue;
            var s = _g.Occupant[idx];
            if (s == null) return 1f;
            if (s.IsTargetCore || s.PassableForEnemies) return 1f;
            if (jumper && s.Def.Jumpable) return 2.5f;
            // стоимость «прогрызания»: пропорциональна прочности
            return 4f + s.Health.Current / 60f;
        }

        public void Compute(IList<RectInt> targets, bool jumper)
        {
            int n = _g.Size;
            for (int i = 0; i < Dist.Length; i++) Dist[i] = float.MaxValue;
            _heap.Clear();
            if (targets == null) return;
            foreach (var r in targets)
                for (int y = r.yMin; y < r.yMax; y++)
                for (int x = r.xMin; x < r.xMax; x++)
                {
                    if (!_g.InBounds(x, y)) continue;
                    int idx = _g.Index(x, y);
                    Dist[idx] = 0f;
                    _heap.Push(idx, 0f);
                }
            while (_heap.Count > 0)
            {
                _heap.Pop(out int cur, out float d);
                if (d > Dist[cur]) continue;
                int cx = cur % n, cy = cur / n;
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + Dx[k], ny = cy + Dy[k];
                    if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                    int ni = ny * n + nx;
                    float c = CellCost(ni, jumper);
                    if (c == float.MaxValue) continue;
                    if (k >= 4)
                    {
                        // не срезаем углы через заблокированные клетки
                        if (_g.Blocked[cy * n + nx] != 0 || _g.Blocked[ny * n + cx] != 0) continue;
                    }
                    float nd = d + c * Cost[k];
                    if (nd < Dist[ni]) { Dist[ni] = nd; _heap.Push(ni, nd); }
                }
            }
        }

        /// <summary>Соседняя клетка с минимальной дистанцией (куда шагать).</summary>
        public bool NextStep(Vector2Int c, out Vector2Int next)
        {
            next = c;
            int n = _g.Size;
            if (!_g.InBounds(c)) return false;
            float best = Dist[c.y * n + c.x];
            bool found = false;
            for (int k = 0; k < 8; k++)
            {
                int nx = c.x + Dx[k], ny = c.y + Dy[k];
                if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                float d = Dist[ny * n + nx];
                if (d < best) { best = d; next = new Vector2Int(nx, ny); found = true; }
            }
            return found;
        }

        public float DistanceAt(Vector2Int c) => _g.InBounds(c) ? Dist[c.y * _g.Size + c.x] : float.MaxValue;
    }

    /// <summary>A* по сетке для игрока и гарнизона (обходит постройки, проходит через свои ворота).</summary>
    public class GridPathfinder
    {
        readonly WorldGrid _g;
        readonly float[] _gScore;
        readonly int[] _came;
        readonly int[] _stamp;
        readonly bool[] _closed;
        int _gen;
        readonly BinaryHeap _open;
        static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] Dy = { 0, 0, 1, -1, 1, -1, 1, -1 };

        public GridPathfinder(WorldGrid g)
        {
            _g = g;
            int n = g.Size * g.Size;
            _gScore = new float[n];
            _came = new int[n];
            _stamp = new int[n];
            _closed = new bool[n];
            _open = new BinaryHeap(n * 2);
        }

        /// <summary>Ищет путь; результат — точки в мире (включая конечную). Возвращает false, если пути нет.</summary>
        public bool FindPath(Vector3 from, Vector3 to, List<Vector3> result, int maxExpand = 6000)
        {
            result.Clear();
            var s = _g.WorldToCell(from);
            var t = _g.WorldToCell(to);
            if (!_g.InBounds(s) || !_g.InBounds(t)) return false;
            if (!_g.WalkableFriendly(t.x, t.y)) t = _g.NearestWalkable(t, 6);
            if (!_g.WalkableFriendly(s.x, s.y)) s = _g.NearestWalkable(s, 3);
            int n = _g.Size;
            _gen++;
            _open.Clear();
            int si = s.y * n + s.x, ti = t.y * n + t.x;
            Touch(si);
            _gScore[si] = 0f;
            _came[si] = -1;
            _open.Push(si, H(s.x, s.y, t.x, t.y));
            int expanded = 0;
            int bestIdx = si; float bestH = H(s.x, s.y, t.x, t.y);
            while (_open.Count > 0 && expanded < maxExpand)
            {
                _open.Pop(out int cur, out _);
                if (_closed[cur] && _stamp[cur] == _gen) continue;
                _closed[cur] = true;
                expanded++;
                if (cur == ti) { bestIdx = cur; break; }
                int cx = cur % n, cy = cur / n;
                float hh = H(cx, cy, t.x, t.y);
                if (hh < bestH) { bestH = hh; bestIdx = cur; }
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + Dx[k], ny = cy + Dy[k];
                    if (!_g.WalkableFriendly(nx, ny)) continue;
                    if (k >= 4 && (!_g.WalkableFriendly(nx, cy) || !_g.WalkableFriendly(cx, ny))) continue;
                    int ni = ny * n + nx;
                    Touch(ni);
                    if (_closed[ni]) continue;
                    float ng = _gScore[cur] + (k >= 4 ? 1.414f : 1f);
                    if (ng < _gScore[ni])
                    {
                        _gScore[ni] = ng;
                        _came[ni] = cur;
                        _open.Push(ni, ng + H(nx, ny, t.x, t.y));
                    }
                }
            }
            // восстановление пути (к цели или к ближайшей достигнутой клетке)
            var cells = new List<int>();
            for (int c = bestIdx; c != -1; c = _came[c]) { cells.Add(c); if (cells.Count > 4000) break; }
            cells.Reverse();
            // упрощение: выкидываем точки на прямой
            for (int i = 1; i < cells.Count; i++)
            {
                if (i < cells.Count - 1)
                {
                    int a = cells[i - 1], b = cells[i], c = cells[i + 1];
                    int dx1 = (b % n) - (a % n), dy1 = (b / n) - (a / n);
                    int dx2 = (c % n) - (b % n), dy2 = (c / n) - (b / n);
                    if (dx1 == dx2 && dy1 == dy2) continue;
                }
                result.Add(_g.CellCenter(cells[i] % n, cells[i] / n));
            }
            if (bestIdx == ti) result.Add(new Vector3(to.x, _g.SampleHeight(to), to.z));
            return result.Count > 0;
        }

        void Touch(int i)
        {
            if (_stamp[i] != _gen) { _stamp[i] = _gen; _gScore[i] = float.MaxValue; _closed[i] = false; _came[i] = -1; }
        }

        static float H(int x0, int y0, int x1, int y1)
        {
            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
            return (dx + dy) + (1.414f - 2f) * Math.Min(dx, dy);
        }
    }

    /// <summary>Двоичная куча (минимальная) для индексов клеток.</summary>
    public class BinaryHeap
    {
        int[] _items;
        float[] _keys;
        public int Count { get; private set; }

        public BinaryHeap(int capacity)
        {
            _items = new int[capacity];
            _keys = new float[capacity];
        }

        public void Clear() => Count = 0;

        public void Push(int item, float key)
        {
            if (Count == _items.Length)
            {
                Array.Resize(ref _items, _items.Length * 2);
                Array.Resize(ref _keys, _keys.Length * 2);
            }
            int i = Count++;
            _items[i] = item; _keys[i] = key;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (_keys[p] <= _keys[i]) break;
                Swap(i, p);
                i = p;
            }
        }

        public void Pop(out int item, out float key)
        {
            item = _items[0]; key = _keys[0];
            Count--;
            if (Count == 0) return;
            _items[0] = _items[Count]; _keys[0] = _keys[Count];
            int i = 0;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, m = i;
                if (l < Count && _keys[l] < _keys[m]) m = l;
                if (r < Count && _keys[r] < _keys[m]) m = r;
                if (m == i) break;
                Swap(i, m);
                i = m;
            }
        }

        void Swap(int a, int b)
        {
            int ti = _items[a]; _items[a] = _items[b]; _items[b] = ti;
            float tk = _keys[a]; _keys[a] = _keys[b]; _keys[b] = tk;
        }
    }
}
