using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    /// <summary>
    /// Режим строительства: голограмма-призрак на сетке, поворот (R), протяжка стен мышью, ярус (Q),
    /// проверки (ранг, ресурсы, радиус застройки, рельеф, залежь, опоры второго яруса), установка чертежа.
    /// Здесь же обрушение второго яруса, лишившегося опор.
    /// </summary>
    public class BuildSystem : MonoBehaviour
    {
        public BuildingDef Selected { get; private set; }
        public int Rotation { get; private set; }
        public Reactor Reactor { get; private set; }
        public bool IsPlacing => Selected != null;
        public string LastInvalidReason { get; private set; }
        public int CancelFrame { get; private set; } = -1;
        public bool OutOfPower { get; private set; }
        /// <summary>Уровень, на который сейчас ставится постройка (0 — земля, 1 — второй ярус).</summary>
        public int PlacementLevel => Selected == null ? 0 : Selected.Kind == StructureKind.Deck ? 1 : Selected.AllowUpper && _wantUpper ? 1 : 0;
        public bool CanToggleLevel => Selected != null && Selected.AllowUpper;
        bool _wantUpper;
        readonly List<Structure> _collapse = new List<Structure>();

        readonly List<Transform> _ghosts = new List<Transform>();
        readonly List<ModelBuilder.MaterialSwap> _ghostSwaps = new List<ModelBuilder.MaterialSwap>();
        BuildingDef _ghostDef;
        bool _dragging;
        Vector2Int _dragStart;
        LineRenderer _radiusRing, _powerRing;

        public void Init()
        {
            _radiusRing = MakeRing("BuildRadius", new Color(1f, 0.55f, 0.2f, 0.6f));
            _powerRing = MakeRing("PowerRadius", new Color(0.3f, 0.75f, 1f, 0.6f));
        }

        LineRenderer MakeRing(string name, Color c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = true;
            lr.positionCount = 72;
            lr.widthMultiplier = 0.12f;
            lr.sharedMaterial = MaterialFactory.Unlit(c);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.enabled = false;
            return lr;
        }

        void DrawRing(LineRenderer lr, Vector3 c, float r)
        {
            lr.enabled = true;
            for (int i = 0; i < lr.positionCount; i++)
            {
                float a = i * Mathf.PI * 2f / lr.positionCount;
                var p = c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r;
                p.y = Game.Grid.SampleHeight(p) + 0.25f;
                lr.SetPosition(i, p);
            }
        }

        // ------------------------------------------------------------ доступность

        /// <summary>null — можно строить; иначе причина блокировки (для меню).</summary>
        public string LockReason(BuildingDef def)
        {
            if (!def.Buildable) return "Нельзя построить";
            if (Game.Tutorial != null && !Game.Tutorial.IsBuildUnlocked(def.Kind)) return "Откроется по ходу обучения";
            if (!string.IsNullOrEmpty(def.RequiredFlag) && !Game.Entry.HasFlag(def.RequiredFlag)) return "Нужен уникальный чертёж";
            if (def.RequiredFeature.HasValue && !Game.Rank.IsUnlocked(def.RequiredFeature.Value)) return Game.Rank.LockReason(def.RequiredFeature.Value);
            if (!Game.Rank.IsAtLeast(def.MinRank)) return $"Требуется ранг: {PlayerRankManager.RankName(def.MinRank)}";
            return null;
        }

        public Cost CostOf(BuildingDef def) => Game.Entry.HasFlag("free_" + def.Kind) ? new Cost() : def.Cost;

        public void Begin(BuildingDef def)
        {
            var reason = LockReason(def);
            if (reason != null) { Game.Log?.Warn(reason); Game.Sound?.Play2D(Sfx.UiError); return; }
            Cancel();
            Selected = def;
            _wantUpper = false;
            Game.Sound?.Play2D(Sfx.UiClick);
        }

        public void ToggleLevel()
        {
            if (!CanToggleLevel) return;
            _wantUpper = !_wantUpper;
            Game.Sound?.Play2D(Sfx.UiClick, 0.5f);
        }

        public void Cancel()
        {
            if (Selected != null) CancelFrame = Time.frameCount;
            Selected = null;
            _dragging = false;
            ClearGhosts();
            if (_radiusRing != null) _radiusRing.enabled = false;
            if (_powerRing != null) _powerRing.enabled = false;
        }

        void ClearGhosts()
        {
            foreach (var g in _ghosts) if (g != null) Destroy(g.gameObject);
            _ghosts.Clear();
            _ghostSwaps.Clear();
            _ghostDef = null;
        }

        Transform GetGhost(int i, bool valid)
        {
            if (_ghostDef != Selected) ClearGhosts();
            _ghostDef = Selected;
            while (_ghosts.Count <= i)
            {
                var mb = new ModelBuilder(transform, "Ghost");
                Selected.Model?.Invoke(mb, Selected);
                foreach (var l in mb.Root.GetComponentsInChildren<Light>(true)) l.enabled = false;
                foreach (var r in mb.Root.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _ghostSwaps.Add(ModelBuilder.SwapAll(mb.Root, Palette.HoloValid));
                _ghosts.Add(mb.Root);
            }
            var t = _ghosts[i];
            t.gameObject.SetActive(true);
            _ghostSwaps[i].Apply(valid ? Palette.HoloValid : Palette.HoloInvalid);
            return t;
        }

        // ------------------------------------------------------------ геометрия

        public static Vector2Int SizeRotated(BuildingDef def, int rot) => rot % 2 == 0 ? def.Size : new Vector2Int(def.Size.y, def.Size.x);

        public static RectInt FootprintAt(BuildingDef def, Vector2Int cell, int rot)
        {
            var s = SizeRotated(def, rot);
            return new RectInt(cell.x - s.x / 2, cell.y - s.y / 2, s.x, s.y);
        }

        /// <summary>Постройка на земле не поместится под настил (высокая, турель или лестница).</summary>
        static bool TooTallUnderDeck(BuildingDef d) =>
            !d.SupportsUpper && (d.Height > WorldGrid.LevelHeight - 0.4f || d.Component == typeof(Turret) || d.Kind == StructureKind.Stairs);

        /// <summary>null — место подходит; иначе причина. pendingDecks — клетки настила в той же протяжке.</summary>
        public string ValidatePlacement(BuildingDef def, RectInt r, bool checkCost = true, int level = 0, ICollection<Vector2Int> pendingDecks = null)
        {
            if (def.Kind == StructureKind.Deck) level = 1;
            if (level > 0) return ValidateUpper(def, r, checkCost, pendingDecks);
            var g = Game.Grid;
            bool needDeposit = def.RequiresDeposit;
            bool depositFound = false;
            for (int y = r.yMin; y < r.yMax; y++)
            for (int x = r.xMin; x < r.xMax; x++)
            {
                if (!g.InBounds(x, y)) return "За пределами карты";
                int idx = g.Index(x, y);
                if (g.Occupant[idx] != null) return "Место занято";
                byte b = g.Blocked[idx];
                if (b == 2)
                {
                    if (!needDeposit) return "Мешает залежь ресурсов";
                    var dep = DepositAtCell(x, y);
                    if (dep != null && dep.BoundExtractor == null && !dep.Hidden) depositFound = true;
                    continue;
                }
                if (b != 0) return "Препятствие (скалы, деревья, вода)";
                if (g.DeckLayer[idx] != null && TooTallUnderDeck(def)) return "Над клеткой настил — постройка под ним не поместится";
            }
            if (def.Kind == StructureKind.Stairs)
            {
                Stairs.CellsFor(r, Rotation, out _, out _, out var exit);
                if (!g.InBounds(exit) || g.DeckLayer[g.Index(exit.x, exit.y)] == null) return "Верхний конец лестницы должен упираться в настил (R — поворот)";
                if (g.StairExit[g.Index(exit.x, exit.y)] != null) return "К этому настилу уже ведёт лестница";
            }
            if (needDeposit && !depositFound) return "Экстрактор ставится на залежь ресурса";
            if (g.HeightVariance(r) > 1.3f) return "Слишком неровный рельеф";
            var center = g.RectCenter(r);
            var rc = Reactor != null ? Reactor.transform.position : Game.World.Center;
            float maxR = Game.Rank.BuildRadius;
            if (def.Kind == StructureKind.DecoyBeacon) maxR += 40f;
            if (!needDeposit && Vector2.Distance(new Vector2(center.x, center.z), new Vector2(rc.x, rc.z)) > maxR)
                return $"Вне радиуса застройки ({Mathf.RoundToInt(maxR)} м; растёт с рангом)";
            if (needDeposit && Vector2.Distance(new Vector2(center.x, center.z), new Vector2(rc.x, rc.z)) > maxR + 60f)
                return "Слишком далеко от аванпоста";
            if (checkCost && !Game.Bank.CanAfford(CostOf(def))) return Game.Bank.MissingText(CostOf(def));
            return null;
        }

        /// <summary>Проверки для второго яруса: допуск, опоры, свободное место наверху.</summary>
        string ValidateUpper(BuildingDef def, RectInt r, bool checkCost, ICollection<Vector2Int> pendingDecks)
        {
            if (!Game.Rank.IsUnlocked(Feature.MultiLevel)) return Game.Rank.LockReason(Feature.MultiLevel);
            if (def.Kind != StructureKind.Deck && !def.AllowUpper) return "Эту постройку нельзя ставить на второй ярус";
            var g = Game.Grid;
            bool deck = def.Kind == StructureKind.Deck;
            for (int y = r.yMin; y < r.yMax; y++)
            for (int x = r.xMin; x < r.xMax; x++)
            {
                if (!g.InBounds(x, y)) return "За пределами карты";
                int idx = g.Index(x, y);
                var ground = g.Occupant[idx];
                if (deck)
                {
                    if (g.DeckLayer[idx] != null) return "Здесь уже лежит настил";
                    if (ground != null && TooTallUnderDeck(ground.Def)) return $"Под настилом «{ground.Def.Name}» — не поместится";
                    if (g.StairExit[idx] == null && ground is Stairs) return "Над лестницей настил не кладут";
                    if (!g.DeckSupported(x, y, false, pendingDecks)) return "Нет опоры: настил кладут на стену или колонну, консоль — до 1 клетки от опоры";
                }
                else
                {
                    if (g.UpperLayer[idx] != null) return "Место на втором ярусе занято";
                    if (!g.HasSurface(x, y)) return "Нет опоры: под каждой клеткой нужна стена, колонна или настил";
                }
            }
            if (g.HeightVariance(r) > 0.6f) return "Опоры на разной высоте — выровняйте место";
            var center = g.RectCenter(r);
            var rc = Reactor != null ? Reactor.transform.position : Game.World.Center;
            float maxR = Game.Rank.BuildRadius;
            if (Vector2.Distance(new Vector2(center.x, center.z), new Vector2(rc.x, rc.z)) > maxR)
                return $"Вне радиуса застройки ({Mathf.RoundToInt(maxR)} м; растёт с рангом)";
            if (checkCost && !Game.Bank.CanAfford(CostOf(def))) return Game.Bank.MissingText(CostOf(def));
            return null;
        }

        static ResourceDeposit DepositAtCell(int x, int y)
        {
            foreach (var d in Registry.Deposits)
            {
                if (d == null) continue;
                var c = Game.Grid.WorldToCell(d.transform.position);
                if (c.x == x && c.y == y) return d;
            }
            return null;
        }

        // ------------------------------------------------------------ цикл

        void Update()
        {
            if (!IsPlacing || !Game.IsPlaying) return;
            if (InputBridge.KeyDown(GameKey.Escape) || InputBridge.MouseDown(1)) { Cancel(); return; }
            if (InputBridge.KeyDown(GameKey.R)) Rotation = (Rotation + 1) % 4;
            if (InputBridge.KeyDown(GameKey.Q)) ToggleLevel();
            int level = PlacementLevel;

            var rc = Reactor != null ? Reactor.transform.position : Game.World.Center;
            DrawRing(_radiusRing, rc, Game.Rank.BuildRadius);

            if (!Game.CameraRig.MouseOnGround(out var point)) return;
            if (level > 0)
            {
                // курсор над вторым ярусом: клетка — по пересечению луча с плоскостью яруса, а не с землёй
                var ray = Game.CameraRig.MouseRay();
                var plane = new Plane(Vector3.up, point + Vector3.up * WorldGrid.LevelHeight);
                if (plane.Raycast(ray, out float enter)) point = ray.GetPoint(enter);
            }
            var cell = Game.Grid.WorldToCell(point);

            var hoverCenter = Game.Grid.RectCenter(FootprintAt(Selected, cell, Rotation));
            if (Selected.PowerRadius > 0f) DrawRing(_powerRing, hoverCenter, Selected.PowerRadius);
            else if (Selected.Component == typeof(Turret)) DrawRing(_powerRing, hoverCenter, TurretProfile.For(Selected.Kind).Range * (level > 0 ? 1f + TurretProfile.ElevatedRangeBonus : 1f));
            else _powerRing.enabled = false;
            OutOfPower = Selected.NeedsPower && Game.Power != null && !Game.Power.Covers(hoverCenter);

            var cells = new List<Vector2Int>();
            if (_dragging && Selected.LinePlacement) LineCells(_dragStart, cell, cells);
            else cells.Add(cell);
            // настил кладётся от опоры наружу: сначала клетки над опорами, потом консоли
            if (Selected.Kind == StructureKind.Deck && cells.Count > 1)
                cells.Sort((a, b) => (Game.Grid.IsSupport(b.x, b.y) ? 1 : 0) - (Game.Grid.IsSupport(a.x, a.y) ? 1 : 0));

            int affordable = CountAffordable(Selected);
            int validCount = 0;
            string reason = null;
            _pending.Clear();
            for (int i = 0; i < cells.Count; i++)
            {
                var r = FootprintAt(Selected, cells[i], Rotation);
                var why = ValidatePlacement(Selected, r, false, level, _pending);
                bool ok = why == null && validCount < affordable;
                if (why == null && validCount >= affordable) why = Game.Bank.MissingText(CostOf(Selected));
                if (ok) { validCount++; if (Selected.Kind == StructureKind.Deck) _pending.Add(cells[i]); }
                if (why != null && reason == null) reason = why;
                var ghost = GetGhost(i, ok);
                var pos = Game.Grid.RectCenter(r);
                pos.y = MaxHeight(r) + (level > 0 ? WorldGrid.LevelHeight : 0f);
                ghost.position = pos;
                ghost.rotation = Quaternion.Euler(0, Rotation * 90f, 0);
            }
            for (int i = cells.Count; i < _ghosts.Count; i++) _ghosts[i].gameObject.SetActive(false);
            LastInvalidReason = reason;

            if (InputBridge.PointerOverUi) return;
            if (InputBridge.MouseDown(0))
            {
                if (Selected.LinePlacement) { _dragging = true; _dragStart = cell; }
                else TryPlace(cell);
            }
            if (InputBridge.MouseUp(0) && _dragging)
            {
                _dragging = false;
                int placed = 0;
                foreach (var c in cells) if (TryPlace(c, placed > 0)) placed++;
                if (!InputBridge.KeyHeld(GameKey.LeftShift) && placed > 0 && !Selected.LinePlacement) Cancel();
            }
        }

        int CountAffordable(BuildingDef def)
        {
            var c = CostOf(def);
            if (c.IsEmpty) return 999;
            int n = int.MaxValue;
            for (int i = 0; i < c.Types.Count; i++)
                if (c.Amounts[i] > 0) n = Mathf.Min(n, Game.Bank[c.Types[i]] / c.Amounts[i]);
            return n;
        }

        float MaxHeight(RectInt r)
        {
            float h = float.MinValue;
            for (int y = r.yMin; y < r.yMax; y++)
            for (int x = r.xMin; x < r.xMax; x++)
                if (Game.Grid.InBounds(x, y)) h = Mathf.Max(h, Game.Grid.Height[Game.Grid.Index(x, y)]);
            return h > float.MinValue ? h : 0f;
        }

        static void LineCells(Vector2Int a, Vector2Int b, List<Vector2Int> outCells)
        {
            // прямая по доминирующей оси (как в RTS)
            var d = b - a;
            if (Mathf.Abs(d.x) >= Mathf.Abs(d.y))
            {
                int s = d.x >= 0 ? 1 : -1;
                for (int x = 0; x <= Mathf.Abs(d.x) && outCells.Count < 40; x++) outCells.Add(new Vector2Int(a.x + x * s, a.y));
            }
            else
            {
                int s = d.y >= 0 ? 1 : -1;
                for (int y = 0; y <= Mathf.Abs(d.y) && outCells.Count < 40; y++) outCells.Add(new Vector2Int(a.x, a.y + y * s));
            }
        }

        readonly List<Vector2Int> _pending = new List<Vector2Int>();

        bool TryPlace(Vector2Int cell, bool quiet = false)
        {
            var r = FootprintAt(Selected, cell, Rotation);
            int level = PlacementLevel;
            var why = ValidatePlacement(Selected, r, true, level);
            if (why != null)
            {
                if (!quiet) { Game.Log?.Warn(why); Game.Sound?.Play2D(Sfx.UiError, 0.6f); }
                return false;
            }
            var cost = CostOf(Selected);
            Game.Bank.Spend(cost);
            bool free = Game.Entry.HasFlag("free_" + Selected.Kind);
            if (free) Game.Entry.ClearFlag("free_" + Selected.Kind);
            var s = Structure.Create(Selected, r, Rotation, false, level);
            s.FreeOfCharge = free;
            Game.Sound?.PlayAt(Sfx.Build, s.transform.position, 0.5f);
            Game.Noise?.Emit(s.transform.position, 4f, NoiseSource.Construction);
            if (!Selected.LinePlacement && !InputBridge.KeyHeld(GameKey.LeftShift)) Cancel();
            return true;
        }

        /// <summary>Мгновенная постройка (стартовая база, загрузка сохранения).</summary>
        public Structure PlaceInstant(StructureKind kind, Vector2Int cell, int rot, bool built = true, int level = 0)
        {
            var def = BuildingCatalog.Get(kind);
            var r = FootprintAt(def, cell, rot);
            var layer = Structure.LayerFor(def, level);
            if (layer == GridLayer.Ground)
            {
                for (int y = r.yMin; y < r.yMax; y++)
                for (int x = r.xMin; x < r.xMax; x++)
                    if (Game.Grid.InBounds(x, y) && Game.Grid.Blocked[Game.Grid.Index(x, y)] == 1) Game.Grid.Blocked[Game.Grid.Index(x, y)] = 0;
            }
            if (!Game.Grid.IsAreaFree(r, layer) && !(layer == GridLayer.Ground && def.RequiresDeposit)) return null;
            var s = Structure.Create(def, r, rot, built, level);
            if (s is Reactor reactor) Reactor = reactor;
            return s;
        }

        /// <summary>Цели для поля потока: реактор (главная цель волн).</summary>
        readonly List<RectInt> _targets = new List<RectInt>();
        void LateUpdate()
        {
            if (!Game.IsPlaying) return;
            // второй ярус без опоры обрушается (каскадом — по кадру на «ступень»)
            if (Game.Grid.TakeSupportCheck())
            {
                _collapse.Clear();
                Game.Grid.CollectUnsupported(_collapse, Registry.Structures);
                foreach (var s in _collapse) s.Collapse();
            }
            if (Reactor == null) return;
            _targets.Clear();
            _targets.Add(Reactor.Footprint);
            Game.Grid.UpdateFields(_targets);
        }
    }
}
