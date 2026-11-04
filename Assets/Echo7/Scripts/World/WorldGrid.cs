using System;
using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    /// <summary>Слой постройки в клетке: земля, настил второго яруса или объект, стоящий на втором ярусе.</summary>
    public enum GridLayer { Ground, Deck, Upper }

    /// <summary>
    /// Логическая сетка карты (2×2 м на клетку). Хранит занятость постройками, блокирующие объекты
    /// (скалы, залежи) и строит поля потоков для волн, атакующих реактор.
    /// Два яруса: земля (Occupant) и второй ярус на высоте <see cref="LevelHeight"/> — настил (DeckLayer)
    /// и то, что на нём стоит (UpperLayer). Второй ярус держится на опорах (стены, колонны); без опоры обрушается.
    /// </summary>
    public class WorldGrid
    {
        public const float CellSize = 2f;
        public const int Levels = 2;
        /// <summary>Высота поверхности второго яруса над рельефом (верх стены с оголовком).</summary>
        public const float LevelHeight = 3.4f;
        public readonly int Size;
        public readonly Vector3 Origin;
        public readonly Structure[] Occupant;     // земля
        public readonly Structure[] DeckLayer;    // настил второго яруса
        public readonly Structure[] UpperLayer;   // постройки на втором ярусе (турели, сенсоры…)
        public readonly Stairs[] StairExit;       // лестница, выходящая на клетку второго яруса
        public readonly byte[] Blocked;   // 1 — скала/залежь/вода, непроходимо и нельзя строить
        public readonly float[] Height;   // высота рельефа в центре клетки
        bool _supportDirty;

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
            DeckLayer = new Structure[size * size];
            UpperLayer = new Structure[size * size];
            StairExit = new Stairs[size * size];
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

        public void Occupy(Structure s, RectInt r) => Occupy(s, r, GridLayer.Ground);
        public void Free(Structure s, RectInt r) => Free(s, r, GridLayer.Ground);

        Structure[] LayerArray(GridLayer l) => l == GridLayer.Deck ? DeckLayer : l == GridLayer.Upper ? UpperLayer : Occupant;

        public void Occupy(Structure s, RectInt r, GridLayer layer)
        {
            var arr = LayerArray(layer);
            for (int y = r.yMin; y < r.yMax; y++)
            for (int x = r.xMin; x < r.xMax; x++)
                if (InBounds(x, y)) arr[Index(x, y)] = s;
            if (layer == GridLayer.Ground) MarkDirty();
        }

        public void Free(Structure s, RectInt r, GridLayer layer)
        {
            var arr = LayerArray(layer);
            for (int y = r.yMin; y < r.yMax; y++)
            for (int x = r.xMin; x < r.xMax; x++)
                if (InBounds(x, y) && arr[Index(x, y)] == s) arr[Index(x, y)] = null;
            if (layer == GridLayer.Ground) MarkDirty();
            // ушла опора или настил — проверить, что держится наверху
            if (layer != GridLayer.Upper) _supportDirty = true;
        }

        public bool IsAreaFree(RectInt r, GridLayer layer)
        {
            if (layer == GridLayer.Ground) return IsAreaFree(r);
            var arr = LayerArray(layer);
            for (int y = r.yMin; y < r.yMax; y++)
            for (int x = r.xMin; x < r.xMax; x++)
                if (!InBounds(x, y) || arr[Index(x, y)] != null) return false;
            return true;
        }

        // ------------------------------------------------------------ второй ярус

        /// <summary>Постройка на уровне: 0 — земля, 1 — второй ярус (объект на ярусе, иначе настил).</summary>
        public Structure StructureAt(int x, int y, int level)
        {
            if (!InBounds(x, y)) return null;
            int i = Index(x, y);
            return level <= 0 ? Occupant[i] : UpperLayer[i] ?? DeckLayer[i];
        }

        public Structure DeckAt(int x, int y) => InBounds(x, y) ? DeckLayer[Index(x, y)] : null;
        public Structure UpperAt(int x, int y) => InBounds(x, y) ? UpperLayer[Index(x, y)] : null;

        /// <summary>Под клеткой опора (стена, колонна), способная держать второй ярус.</summary>
        public bool IsSupport(int x, int y, bool requireBuilt = false)
        {
            var s = StructureAt(x, y);
            return s != null && !s.IsDestroyed && s.Def.SupportsUpper && (!requireBuilt || s.IsBuilt);
        }

        static readonly Vector2Int[] Orth = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };

        /// <summary>
        /// Держится ли настил в клетке: опора прямо под ним или соседний настил, стоящий на опоре
        /// (консоль на одну клетку). extraDirect — клетки, где настил ещё только кладётся (протяжка).
        /// </summary>
        public bool DeckSupported(int x, int y, bool requireBuilt = false, ICollection<Vector2Int> extraDirect = null)
        {
            if (IsSupport(x, y, requireBuilt)) return true;
            foreach (var d in Orth)
            {
                int nx = x + d.x, ny = y + d.y;
                if (!InBounds(nx, ny)) continue;
                var deck = DeckLayer[Index(nx, ny)];
                bool deckThere = deck != null && !deck.IsDestroyed && (!requireBuilt || deck.IsBuilt);
                if (!deckThere && extraDirect != null && extraDirect.Contains(new Vector2Int(nx, ny))) deckThere = true;
                if (deckThere && IsSupport(nx, ny, requireBuilt)) return true;
            }
            return false;
        }

        /// <summary>Есть ли на втором ярусе поверхность для постройки: настил или верх опоры.</summary>
        public bool HasSurface(int x, int y, bool requireBuilt = false)
        {
            if (!InBounds(x, y)) return false;
            var deck = DeckLayer[Index(x, y)];
            if (deck != null && !deck.IsDestroyed && (!requireBuilt || deck.IsBuilt)) return true;
            return IsSupport(x, y, requireBuilt);
        }

        /// <summary>Можно ли пройти по второму ярусу: достроенный настил, сверху ничего не мешает.</summary>
        public bool WalkableUpper(int x, int y)
        {
            if (!InBounds(x, y)) return false;
            int i = Index(x, y);
            var deck = DeckLayer[i];
            if (deck == null || deck.IsDestroyed || !deck.IsBuilt) return false;
            var u = UpperLayer[i];
            return u == null || u.IsDestroyed || u.PassableForFriendly;
        }

        public bool WalkableFriendly(int x, int y, int level) => level <= 0 ? WalkableFriendly(x, y) : WalkableUpper(x, y);

        /// <summary>Высота, на которой стоит юнит в точке: настил, пандус лестницы или рельеф.</summary>
        public float WalkHeight(Vector3 p, int level)
        {
            var c = WorldToCell(p);
            if (!InBounds(c)) return SampleHeight(p);
            int i = Index(c.x, c.y);
            if (level >= 1 && WalkableUpper(c.x, c.y)) return Height[i] + LevelHeight;
            if (Occupant[i] is Stairs st && st.IsBuilt) return st.RampHeight(p);
            return SampleHeight(p);
        }

        /// <summary>На каком уровне точка: 1 — над настилом на высоте яруса, иначе 0.</summary>
        public int LevelAt(Vector3 p)
        {
            var c = WorldToCell(p);
            if (!InBounds(c)) return 0;
            int i = Index(c.x, c.y);
            return DeckLayer[i] != null && p.y > Height[i] + LevelHeight * 0.6f ? 1 : 0;
        }

        /// <summary>Юнит заметно выше земли (на настиле или стене) — пехота без прыжка до него не дотянется.</summary>
        public bool IsElevated(Vector3 p) => p.y > SampleHeight(p) + LevelHeight * 0.5f;

        public void RegisterStair(Stairs s)
        {
            var e = s.ExitCell;
            if (InBounds(e)) StairExit[Index(e.x, e.y)] = s;
        }

        public void UnregisterStair(Stairs s)
        {
            var e = s.ExitCell;
            if (InBounds(e) && StairExit[Index(e.x, e.y)] == s) StairExit[Index(e.x, e.y)] = null;
        }

        /// <summary>Готова ли опора, чтобы начинать монтаж постройки второго яруса (опора и настил достроены).</summary>
        public bool SupportReady(Structure s)
        {
            var r = s.Footprint;
            for (int y = r.yMin; y < r.yMax; y++)
            for (int x = r.xMin; x < r.xMax; x++)
            {
                if (s.Layer == GridLayer.Deck) { if (!DeckSupported(x, y, true)) return false; }
                else if (!HasSurface(x, y, true)) return false;
            }
            return true;
        }

        /// <summary>Флаг «пора проверить, что держится наверху» (сбрасывается при чтении).</summary>
        public bool TakeSupportCheck()
        {
            if (!_supportDirty) return false;
            _supportDirty = false;
            return true;
        }

        public void MarkSupportDirty() => _supportDirty = true;

        /// <summary>Постройки второго яруса, потерявшие опору.</summary>
        public void CollectUnsupported(List<Structure> result, IEnumerable<Structure> all)
        {
            foreach (var s in all)
            {
                if (s == null || s.IsDestroyed || s.Level == 0) continue;
                var r = s.Footprint;
                bool ok = true;
                for (int y = r.yMin; y < r.yMax && ok; y++)
                for (int x = r.xMin; x < r.xMax && ok; x++)
                    ok = s.Layer == GridLayer.Deck ? DeckSupported(x, y) : HasSurface(x, y);
                if (!ok) result.Add(s);
            }
        }

        /// <summary>Почему постройку нельзя снести, не обрушив то, что сверху (null — можно).</summary>
        public string DemolishBlocker(Structure s)
        {
            var r = s.Footprint;
            for (int y = r.yMin; y < r.yMax; y++)
            for (int x = r.xMin; x < r.xMax; x++)
            {
                if (!InBounds(x, y)) continue;
                int i = Index(x, y);
                if (s.Layer == GridLayer.Ground && s.Def.SupportsUpper)
                {
                    var deck = DeckLayer[i];
                    var up = UpperLayer[i];
                    if (up != null && !up.IsDestroyed && (deck == null || deck.IsDestroyed)) return $"На опоре стоит «{up.Def.Name}» — сначала снесите её.";
                    if (deck != null && !deck.IsDestroyed && !DeckSupportedWithout(x, y, s)) return "На опоре лежит настил — сначала снесите его.";
                    foreach (var d in Orth)
                    {
                        int nx = x + d.x, ny = y + d.y;
                        var nd = DeckAt(nx, ny);
                        if (nd != null && !nd.IsDestroyed && !IsSupport(nx, ny) && !DeckSupportedWithout(nx, ny, s)) return "На опору опирается консоль настила — сначала снесите её.";
                    }
                }
                else if (s.Layer == GridLayer.Deck)
                {
                    var up = UpperLayer[i];
                    if (up != null && !up.IsDestroyed && !IsSupport(x, y)) return $"На настиле стоит «{up.Def.Name}» — сначала снесите её.";
                    foreach (var d in Orth)
                    {
                        int nx = x + d.x, ny = y + d.y;
                        var nd = DeckAt(nx, ny);
                        if (nd == null || nd.IsDestroyed || nd == s || IsSupport(nx, ny)) continue;
                        // соседняя консоль держится только за этот настил?
                        bool other = false;
                        foreach (var d2 in Orth)
                        {
                            int ox = nx + d2.x, oy = ny + d2.y;
                            if (ox == x && oy == y) continue;
                            var od = DeckAt(ox, oy);
                            if (od != null && !od.IsDestroyed && IsSupport(ox, oy)) { other = true; break; }
                        }
                        if (!other) return "На этот настил опирается соседняя консоль — сначала снесите её.";
                    }
                }
            }
            return null;
        }

        /// <summary>Держится ли настил в клетке, если убрать опору without.</summary>
        bool DeckSupportedWithout(int x, int y, Structure without)
        {
            bool Sup(int sx, int sy) { var o = StructureAt(sx, sy); return o != null && o != without && !o.IsDestroyed && o.Def.SupportsUpper; }
            if (Sup(x, y)) return true;
            foreach (var d in Orth)
            {
                int nx = x + d.x, ny = y + d.y;
                var nd = DeckAt(nx, ny);
                if (nd != null && !nd.IsDestroyed && Sup(nx, ny)) return true;
            }
            return false;
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
        public Vector2Int NearestWalkable(Vector2Int c, int maxRadius = 12) => NearestWalkable(c, 0, maxRadius, out _);

        /// <summary>Ближайшая проходимая клетка на уровне; found = false, если в радиусе такой нет.</summary>
        public Vector2Int NearestWalkable(Vector2Int c, int level, int maxRadius, out bool found)
        {
            found = true;
            if (WalkableFriendly(c.x, c.y, level)) return c;
            for (int r = 1; r <= maxRadius; r++)
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;
                int x = c.x + dx, y = c.y + dy;
                if (WalkableFriendly(x, y, level)) return new Vector2Int(x, y);
            }
            found = false;
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

    /// <summary>
    /// A* по сетке для игрока и гарнизона: обходит постройки, проходит через свои ворота.
    /// Два уровня: по второму ярусу — по достроенному настилу, между уровнями — только по лестницам.
    /// </summary>
    public class GridPathfinder
    {
        readonly WorldGrid _g;
        readonly float[] _gScore;
        readonly int[] _came;
        readonly int[] _stamp;
        readonly bool[] _closed;
        int _gen;
        readonly BinaryHeap _open;
        readonly int _layer;   // число клеток одного уровня
        static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] Dy = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>Последний поиск дошёл до цели (иначе путь ведёт к ближайшей достижимой клетке).</summary>
        public bool LastReached { get; private set; }

        public GridPathfinder(WorldGrid g)
        {
            _g = g;
            _layer = g.Size * g.Size;
            int n = _layer * WorldGrid.Levels;
            _gScore = new float[n];
            _came = new int[n];
            _stamp = new int[n];
            _closed = new bool[n];
            _open = new BinaryHeap(n * 2);
        }

        /// <summary>Ищет путь; результат — точки в мире (включая конечную). Возвращает false, если пути нет.</summary>
        public bool FindPath(Vector3 from, Vector3 to, List<Vector3> result, int maxExpand = 8000)
        {
            result.Clear();
            LastReached = false;
            var s = _g.WorldToCell(from);
            var t = _g.WorldToCell(to);
            if (!_g.InBounds(s) || !_g.InBounds(t)) return false;
            int sl = _g.LevelAt(from), tl = _g.LevelAt(to);
            if (!_g.WalkableFriendly(t.x, t.y, tl))
            {
                var nt = _g.NearestWalkable(t, tl, 6, out bool ok);
                if (!ok && tl == 1) { tl = 0; nt = _g.NearestWalkable(t, 0, 6, out _); }
                t = nt;
            }
            if (!_g.WalkableFriendly(s.x, s.y, sl))
            {
                var ns = _g.NearestWalkable(s, sl, 3, out bool ok);
                if (!ok && sl == 1) { sl = 0; ns = _g.NearestWalkable(s, 0, 3, out _); }
                s = ns;
            }
            int n = _g.Size;
            _gen++;
            _open.Clear();
            int si = sl * _layer + s.y * n + s.x, ti = tl * _layer + t.y * n + t.x;
            Touch(si);
            _gScore[si] = 0f;
            _came[si] = -1;
            _open.Push(si, H(s.x, s.y, t.x, t.y, sl, tl));
            int expanded = 0;
            int bestIdx = si; float bestH = H(s.x, s.y, t.x, t.y, sl, tl);
            while (_open.Count > 0 && expanded < maxExpand)
            {
                _open.Pop(out int cur, out _);
                if (_closed[cur] && _stamp[cur] == _gen) continue;
                _closed[cur] = true;
                expanded++;
                if (cur == ti) { bestIdx = cur; break; }
                int cl = cur / _layer, ci = cur % _layer;
                int cx = ci % n, cy = ci / n;
                float hh = H(cx, cy, t.x, t.y, cl, tl);
                if (hh < bestH) { bestH = hh; bestIdx = cur; }
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + Dx[k], ny = cy + Dy[k];
                    if (!_g.WalkableFriendly(nx, ny, cl)) continue;
                    if (k >= 4 && (!_g.WalkableFriendly(nx, cy, cl) || !_g.WalkableFriendly(cx, ny, cl))) continue;
                    Relax(cur, cl * _layer + ny * n + nx, k >= 4 ? 1.414f : 1f, nx, ny, cl, t, tl);
                }
                // переходы между уровнями — по лестницам
                if (cl == 0)
                {
                    if (_g.Occupant[ci] is Stairs st && st.IsBuilt && st.TopCell.x == cx && st.TopCell.y == cy && _g.WalkableUpper(st.ExitCell.x, st.ExitCell.y))
                        Relax(cur, _layer + st.ExitCell.y * n + st.ExitCell.x, 1.2f, st.ExitCell.x, st.ExitCell.y, 1, t, tl);
                }
                else
                {
                    var st = _g.StairExit[ci];
                    if (st != null && st.IsBuilt && !st.IsDestroyed && _g.WalkableFriendly(st.TopCell.x, st.TopCell.y, 0))
                        Relax(cur, st.TopCell.y * n + st.TopCell.x, 1.2f, st.TopCell.x, st.TopCell.y, 0, t, tl);
                }
            }
            LastReached = bestIdx == ti;
            // восстановление пути (к цели или к ближайшей достигнутой клетке)
            var cells = new List<int>();
            for (int c = bestIdx; c != -1; c = _came[c]) { cells.Add(c); if (cells.Count > 4000) break; }
            cells.Reverse();
            // упрощение: выкидываем точки на прямой (кроме лестниц и смены уровня)
            for (int i = 1; i < cells.Count; i++)
            {
                if (i < cells.Count - 1)
                {
                    int a = cells[i - 1], b = cells[i], c = cells[i + 1];
                    bool sameLevel = a / _layer == b / _layer && b / _layer == c / _layer;
                    bool stair = _g.Occupant[b % _layer] is Stairs;
                    int ai = a % _layer, bi = b % _layer, cc = c % _layer;
                    int dx1 = (bi % n) - (ai % n), dy1 = (bi / n) - (ai / n);
                    int dx2 = (cc % n) - (bi % n), dy2 = (cc / n) - (bi / n);
                    if (sameLevel && !stair && dx1 == dx2 && dy1 == dy2) continue;
                }
                result.Add(PointOf(cells[i]));
            }
            if (LastReached)
            {
                var end = new Vector3(to.x, 0f, to.z);
                end.y = _g.WalkHeight(end, tl);
                result.Add(end);
            }
            return result.Count > 0;
        }

        void Relax(int cur, int ni, float cost, int nx, int ny, int nl, Vector2Int t, int tl)
        {
            Touch(ni);
            if (_closed[ni]) return;
            float ng = _gScore[cur] + cost;
            if (ng < _gScore[ni])
            {
                _gScore[ni] = ng;
                _came[ni] = cur;
                _open.Push(ni, ng + H(nx, ny, t.x, t.y, nl, tl));
            }
        }

        Vector3 PointOf(int node)
        {
            int l = node / _layer, i = node % _layer, n = _g.Size;
            var p = _g.CellCenterFlat(i % n, i / n);
            p.y = _g.WalkHeight(p, l);
            return p;
        }

        void Touch(int i)
        {
            if (_stamp[i] != _gen) { _stamp[i] = _gen; _gScore[i] = float.MaxValue; _closed[i] = false; _came[i] = -1; }
        }

        static float H(int x0, int y0, int x1, int y1, int l0, int l1)
        {
            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
            return (dx + dy) + (1.414f - 2f) * Math.Min(dx, dy) + (l0 != l1 ? 1f : 0f);
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
