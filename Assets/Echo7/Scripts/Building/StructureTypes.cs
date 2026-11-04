using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    /// <summary>Центральный реактор. Его уничтожение — поражение.</summary>
    public class Reactor : Structure
    {
        public float BonusOutput; // орбитальный модуль «Реактор+»
        Transform _ring, _core;

        public override float CurrentEnergyProduce => IsBuilt && !Health.IsDead ? Def.EnergyProduce + BonusOutput : 0f;

        protected override void OnBuilt()
        {
            _ring = Model.Find("#ring");
            _core = Model.Find("#core");
            Health.Damaged += (h, d, a) =>
            {
                if (h.Fraction < 0.35f && Random.value < 0.15f) Game.Log?.Alert($"РЕАКТОР ПОД УДАРОМ! Прочность {Mathf.RoundToInt(h.Fraction * 100)}%");
            };
        }

        protected override void OnRemoved()
        {
            Game.Entry?.OnReactorLost();
        }

        protected override void Update()
        {
            base.Update();
            if (_ring != null) _ring.Rotate(0, 30f * Time.deltaTime, 0, Space.Self);
            if (_core != null) _core.localScale = Vector3.one * (1f + Mathf.Sin(Time.time * 3f) * 0.04f);
        }
    }

    /// <summary>Командный модуль — связь с Центром.</summary>
    public class CommandModule : Structure
    {
        Transform _beacon;
        protected override void OnBuilt() { _beacon = Model.Find("#beacon"); }

        public override void Interact(PlayerController player) => Game.Ui?.OpenCenterComms();

        protected override void Update()
        {
            base.Update();
            if (_beacon != null) _beacon.gameObject.SetActive(Mathf.Repeat(Time.time, 1.6f) < 0.25f);
        }
    }

    /// <summary>Солнечная панель — выработка зависит от времени суток и погоды.</summary>
    public class SolarPanel : Structure
    {
        public override float CurrentEnergyProduce
        {
            get
            {
                if (!IsBuilt || Health.IsDead) return 0f;
                float day = Game.Clock != null ? Game.Clock.Daylight : 1f;
                float w = Game.Weather != null ? Game.Weather.SolarFactor : 1f;
                return Def.EnergyProduce * day * w;
            }
        }
    }

    /// <summary>Жилой блок: койки, укрытие, мораль.</summary>
    public class Habitat : Structure
    {
        public bool Contains(Vector3 p) => DistanceTo(p) < 1.2f;
    }

    /// <summary>Медблок: лечит раненых и лихорадку, производит аптечки.</summary>
    public class MedBay : Structure
    {
        const float Radius = 7f;
        float _produce;

        public bool InRange(Vector3 p) => DistanceTo(p) < Radius;

        protected override void Update()
        {
            base.Update();
            if (!IsOperational || !Game.IsPlaying) return;
            float heal = 3f * OperationalFactor * (Game.Skills != null ? Game.Skills.HealMult : 1f) * Time.deltaTime;
            foreach (var n in Registry.Npcs)
            {
                if (n == null || n.Health.IsDead || n.IsAway || !InRange(n.transform.position)) continue;
                n.Health.Heal(heal);
                if (n.Sickness > 0f) n.TreatSickness(Time.deltaTime);
            }
            var pl = Game.Player;
            if (pl != null && InRange(pl.transform.position)) { pl.Health.Heal(heal * 1.5f); pl.Sickness = Mathf.Max(0f, pl.Sickness - Time.deltaTime * 0.05f); }

            _produce += Game.Clock.GameHoursPerSecond * Time.deltaTime * OperationalFactor;
            if (_produce >= 8f)
            {
                _produce = 0f;
                var cost = new Cost((ResourceType.Alloys, 3), (ResourceType.Water, 2));
                if (Game.Bank.Spend(cost)) { Game.Bank.Add(ResourceType.Medkits, 1); Game.Log?.Info("Медблок: изготовлена аптечка."); }
            }
        }
    }

    /// <summary>Экстрактор на залежи.</summary>
    public class Extractor : Structure
    {
        public ResourceDeposit Deposit;
        float _acc;
        Transform _drill;

        protected override void OnCreated()
        {
            float best = 3.5f;
            foreach (var d in Registry.Deposits)
            {
                if (d == null || d.Hidden || d.IsDepleted || d.BoundExtractor != null) continue;
                float dist = Vector2.Distance(new Vector2(d.transform.position.x, d.transform.position.z), new Vector2(transform.position.x, transform.position.z));
                if (dist < best) { best = dist; Deposit = d; }
            }
            if (Deposit != null) Deposit.BoundExtractor = this;
        }

        protected override void OnBuilt() { _drill = Model.Find("#drill"); }
        protected override void OnRemoved() { if (Deposit != null) Deposit.BoundExtractor = null; }

        public override string StatusText()
        {
            if (IsBuilt && (Deposit == null || Deposit.IsDepleted)) return "Залежь истощена — снесите экстрактор";
            return base.StatusText() + (Deposit != null ? $"  ·  {ResourceBank.Name(Deposit.Type)}: осталось {Deposit.Amount}" : "");
        }

        protected override void Update()
        {
            base.Update();
            if (!IsOperational || Deposit == null || Deposit.IsDepleted || !Game.IsPlaying) return;
            if (_drill != null) _drill.Rotate(0, 220f * Time.deltaTime * OperationalFactor, 0, Space.Self);
            _acc += Time.deltaTime * OperationalFactor / 5f;
            if (_acc >= 1f)
            {
                _acc -= 1f;
                int got = Deposit.Take(2);
                Game.Bank.Add(Deposit.Type, got);
                if (Random.value < 0.3f) Game.Fx.Emit(FxKind.Dust, transform.position + Vector3.up * 0.3f, 3, Vector3.up, 0.6f);
                if (Deposit == null || Deposit.IsDepleted) Game.Log?.Warn("Экстрактор остановлен: залежь истощена.");
            }
        }
    }

    /// <summary>Влагоконденсатор.</summary>
    public class Condenser : Structure
    {
        float _acc;
        public float Rate
        {
            get
            {
                float lake = 1f;
                var l = Game.World.NearestLake(transform.position, out float d);
                if (l != null && d < 15f && Game.Weather.WaterLevel > 0.2f) lake = 2f;
                return 0.25f * lake * Game.Weather.CondenserFactor;
            }
        }

        public override string StatusText() => base.StatusText() + $"  ·  {Rate * 60f:0.#} воды/мин";

        protected override void Update()
        {
            base.Update();
            if (!IsOperational || !Game.IsPlaying) return;
            _acc += Rate * OperationalFactor * Time.deltaTime;
            if (_acc >= 1f) { int n = Mathf.FloorToInt(_acc); _acc -= n; Game.Bank.Add(ResourceType.Water, n); }
        }
    }

    /// <summary>Геологический зонд — открывает скрытые залежи.</summary>
    public class SurveyProbe : Structure
    {
        protected override void OnBuilt()
        {
            int found = 0;
            foreach (var d in Registry.Deposits)
            {
                if (d == null || !d.Hidden) continue;
                if (Vector3.Distance(d.transform.position, transform.position) < 50f) { d.SetHidden(false); found++; }
            }
            Game.Log?.Info(found > 0 ? $"Геологоразведка: обнаружено залежей — {found}." : "Геологоразведка: в радиусе зонда пусто.");
            if (found > 0) GameEvents.Flag("survey_found");
        }
    }

    /// <summary>Генератор щита — уникальный чертёж за помощь VIP.</summary>
    public class ShieldProjector : Structure
    {
        const float Radius = 12f;
        Transform _dome;
        readonly List<Structure> _shielded = new List<Structure>();
        float _scan;

        protected override void OnBuilt() { _dome = Model.Find("#dome"); }

        protected override void Update()
        {
            base.Update();
            bool on = IsOperational && Efficiency > 0.5f;
            if (_dome != null)
            {
                _dome.gameObject.SetActive(on);
                if (on) _dome.localScale = Vector3.one * Radius * 2f * (1f + Mathf.Sin(Time.time * 2f) * 0.01f);
            }
            _scan -= Time.deltaTime;
            if (_scan > 0f) return;
            _scan = 1f;
            float baseline = Game.Skills != null ? Game.Skills.StructureDamageReduction : 0f;
            foreach (var s in _shielded) if (s != null) s.Health.DamageReduction = baseline;
            _shielded.Clear();
            if (!on) return;
            foreach (var s in Registry.Structures)
            {
                if (s == null || s == this || s.DistanceTo(transform.position) > Radius) continue;
                s.Health.DamageReduction = Mathf.Max(0.45f, baseline);
                _shielded.Add(s);
            }
        }

        protected override void OnRemoved()
        {
            float baseline = Game.Skills != null ? Game.Skills.StructureDamageReduction : 0f;
            foreach (var s in _shielded) if (s != null) s.Health.DamageReduction = baseline;
        }
    }

    /// <summary>
    /// Шумовой маяк — приманка («фауна как оружие» из ARCHITECTURE.md). Создаёт ложные шумовые события:
    /// директор берёт направление волн из шума, а штурмовики поблизости атакуют маяк вместо базы.
    /// Общую «угрозу» почти не поднимает, но через 3 минуты сгорает.
    /// </summary>
    public class DecoyBeacon : Structure
    {
        public float Life = 180f;
        float _pulse;
        Transform _lamp;

        protected override void OnBuilt() { _lamp = Model.Find("#lamp"); }

        public override string StatusText() => IsBuilt ? $"Шумит ещё {Mathf.CeilToInt(Life)} с" : base.StatusText();

        protected override void Update()
        {
            base.Update();
            if (!IsBuilt || IsDestroyed || !Game.IsPlaying) return;
            Life -= Time.deltaTime;
            if (_lamp != null) _lamp.gameObject.SetActive(Mathf.Repeat(Time.time, 0.6f) < 0.3f);
            _pulse -= Time.deltaTime;
            if (_pulse <= 0f)
            {
                _pulse = 1.5f;
                Game.Noise?.EmitDecoy(transform.position, 6f);
                if (Random.value < 0.3f) Game.Sound?.PlayAt(Sfx.Alarm, transform.position, 0.25f, 0.2f, 1f);
            }
            if (Life <= 0f)
            {
                Game.Log?.Info("Шумовой маяк выработал батарею.");
                DestroyStructure();
            }
        }
    }

    /// <summary>Посадочная площадка — точка доставок и визитов.</summary>
    public class LandingPad : Structure
    {
        public Vector3 PadPoint => transform.position + Vector3.up * 0.6f;
    }

    /// <summary>Космопорт — открывает перелёты на глобальной карте.</summary>
    public class Spaceport : Structure
    {
        public override void Interact(PlayerController player) => Game.Ui?.OpenGlobalMap();
    }

    /// <summary>
    /// Модульная стена: центральный столб + секции к соседним стенам/воротам. При изменении соседей
    /// секции перестраиваются (как конструктор на референсах).
    /// </summary>
    public class WallSegment : Structure
    {
        Transform _links;
        protected override bool CombineModel => false;
        static readonly Vector2Int[] Dirs = { new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(-1, 0), new Vector2Int(0, -1) };

        protected override void OnCreated()
        {
            RefreshLinks();
            RefreshNeighbours();
        }

        protected override void OnRemoved()
        {
            // соседи перестроят свои секции после удаления из сетки
            Game.Entry?.StartCoroutine(RefreshNeighboursLater(Footprint.position));
        }

        static System.Collections.IEnumerator RefreshNeighboursLater(Vector2Int cell)
        {
            yield return null;
            foreach (var d in Dirs)
            {
                var n = Game.Grid?.StructureAt(cell.x + d.x, cell.y + d.y) as WallSegment;
                if (n != null) n.RefreshLinks();
            }
        }

        void RefreshNeighbours()
        {
            foreach (var d in Dirs)
            {
                var n = Game.Grid.StructureAt(Footprint.x + d.x, Footprint.y + d.y) as WallSegment;
                if (n != null && n != this) n.RefreshLinks();
            }
        }

        public void RefreshLinks()
        {
            if (_links != null) Destroy(_links.gameObject);
            var mb = new ModelBuilder(Model, "#links");
            _links = mb.Root;
            _links.localRotation = Quaternion.Inverse(transform.rotation); // секции — в мировых осях
            foreach (var d in Dirs)
            {
                var n = Game.Grid.StructureAt(Footprint.x + d.x, Footprint.y + d.y);
                if (n == null || n == this || n.IsDestroyed) continue;
                bool linkable = n is WallSegment || n is Gate;
                if (!linkable) continue;
                StructureModels.WallLink(mb, Def, new Vector3(d.x, 0, d.y));
            }
            if (!IsBuilt) ModelBuilder.SwapAll(_links, Palette.HoloBuild);
        }

        protected override void OnBuilt()
        {
            // после стройки голограмма секций заменяется настоящими материалами
            RefreshLinks();
        }
    }

    /// <summary>Ворота. Механизм могут повредить прыгуны — тогда створки заклинивает открытыми.</summary>
    public class Gate : Structure
    {
        public float MechanismHp = 100f;
        public bool Jammed => MechanismHp <= 0f;
        Transform _door;
        float _open;

        public override bool PassableForFriendly => IsBuilt;
        public override bool PassableForEnemies => Jammed || _open > 0.8f;

        protected override void OnBuilt() { _door = Model.Find("#door"); }

        public void DamageMechanism(float amount)
        {
            if (Jammed) return;
            MechanismHp -= amount;
            if (Jammed)
            {
                Game.Log?.Alert("Механизм ворот повреждён — створки заклинило открытыми!");
                Game.Grid.MarkDirty();
                Game.Fx.Emit(FxKind.Sparks, transform.position + Vector3.up * 2f, 16, Vector3.up);
            }
        }

        public override bool NeedsService => base.NeedsService || MechanismHp < 100f;

        public override void Service()
        {
            base.Service();
            if (MechanismHp < 100f)
            {
                bool wasJammed = Jammed;
                MechanismHp = 100f;
                if (wasJammed) { Game.Log?.Info("Механизм ворот отремонтирован."); Game.Grid.MarkDirty(); }
            }
        }

        public override string StatusText() => Jammed ? "Механизм сломан: ворота открыты для всех!" : base.StatusText() + $"  ·  механизм {Mathf.RoundToInt(MechanismHp)}%";

        protected override void Update()
        {
            base.Update();
            if (!IsBuilt || _door == null) return;
            bool friendlyNear = false;
            var p = transform.position;
            if (Game.Player != null && (Game.Player.transform.position - p).sqrMagnitude < 16f) friendlyNear = true;
            if (!friendlyNear)
                foreach (var n in Registry.Npcs)
                    if (n != null && !n.Health.IsDead && (n.transform.position - p).sqrMagnitude < 12f) { friendlyNear = true; break; }
            bool enemyNear = Registry.NearestEnemy(p, 5f) != null;
            float target = Jammed ? 0.85f : (friendlyNear && (!enemyNear || !IsOperational) ? 1f : 0f);
            if (!IsOperational && !Jammed) target = friendlyNear ? 1f : 0f; // без энергии — ручной привод, медленно
            _open = Mathf.MoveTowards(_open, target, Time.deltaTime * (IsOperational ? 1.8f : 0.5f));
            _door.localPosition = new Vector3(0, Mathf.Lerp(1.6f, 4.4f, _open), 0);
            // открытые ворота физически пропускают игрока (CharacterController)
            if (Collider != null) Collider.enabled = _open < 0.6f;
        }
    }

    /// <summary>
    /// Настил второго яруса: плита 2×2 м на высоте верха стены. Держится на опоре под собой или консолью
    /// на соседнем настиле, который стоит на опоре. По открытым краям — перила (и коллайдеры, чтобы не падать),
    /// у консоли — подкос к опоре. Осмотр/снос — Shift+клик (обычный клик — идти по настилу).
    /// </summary>
    public class Deck : Structure
    {
        Transform _edges;
        readonly List<GameObject> _rails = new List<GameObject>();
        static readonly Vector2Int[] Dirs = { new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(-1, 0), new Vector2Int(0, -1) };

        protected override bool CombineModel => false;
        public override bool CanInteract => !IsDestroyed && (!IsBuilt || InputBridge.KeyHeld(GameKey.LeftShift));

        protected override void ConfigureCollider(BoxCollider c)
        {
            c.size = new Vector3(WorldGrid.CellSize * 0.98f, 0.3f, WorldGrid.CellSize * 0.98f);
            c.center = new Vector3(0, -0.15f, 0);
        }

        protected override void OnCreated() { RefreshEdges(); RefreshNeighbours(); }
        protected override void OnBuilt() { RefreshEdges(); RefreshNeighbours(); }
        protected override void OnRemoved() { Game.Entry?.StartCoroutine(RefreshAround(Footprint.position)); }

        static System.Collections.IEnumerator RefreshAround(Vector2Int cell)
        {
            yield return null;
            if (Game.Grid == null) yield break;
            foreach (var d in Dirs) (Game.Grid.DeckAt(cell.x + d.x, cell.y + d.y) as Deck)?.RefreshEdges();
        }

        void RefreshNeighbours()
        {
            foreach (var d in Dirs)
            {
                var n = Game.Grid.DeckAt(Footprint.x + d.x, Footprint.y + d.y) as Deck;
                if (n != null && n != this && !n.IsDestroyed) n.RefreshEdges();
            }
        }

        public bool DirectlySupported => Game.Grid.IsSupport(Footprint.x, Footprint.y);

        public void RefreshEdges()
        {
            if (IsDestroyed || Model == null) return;
            if (_edges != null) Destroy(_edges.gameObject);
            foreach (var r in _rails) if (r != null) Destroy(r);
            _rails.Clear();
            var mb = new ModelBuilder(Model, "#edges");
            _edges = mb.Root;
            _edges.localRotation = Quaternion.Inverse(transform.rotation); // перила — в мировых осях
            var g = Game.Grid;
            var c = Footprint.position;
            bool direct = DirectlySupported;
            var stair = g.InBounds(c) ? g.StairExit[g.Index(c.x, c.y)] : null;
            foreach (var d in Dirs)
            {
                int nx = c.x + d.x, ny = c.y + d.y;
                var nd = g.DeckAt(nx, ny);
                bool deckNext = nd != null && !nd.IsDestroyed;
                bool stairNext = stair != null && !stair.IsDestroyed && stair.TopCell.x == nx && stair.TopCell.y == ny;
                var nu = g.UpperAt(nx, ny);
                bool upperNext = nu != null && !nu.IsDestroyed;
                if (!deckNext && !stairNext && !upperNext)
                {
                    StructureModels.DeckRail(mb, new Vector3(d.x, 0, d.y));
                    AddRailCollider(d);
                }
                if (!direct && deckNext && g.IsSupport(nx, ny)) StructureModels.DeckBrace(mb, new Vector3(d.x, 0, d.y));
            }
            if (!IsBuilt) ModelBuilder.SwapAll(_edges, Palette.HoloBuild);
        }

        void AddRailCollider(Vector2Int d)
        {
            var go = new GameObject("rail") { layer = Layers.Rail };
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.identity;
            go.transform.position = transform.position + new Vector3(d.x, 0, d.y) * (WorldGrid.CellSize * 0.47f) + Vector3.up * 0.55f;
            var bc = go.AddComponent<BoxCollider>();
            bc.size = d.x != 0 ? new Vector3(0.12f, 1.1f, WorldGrid.CellSize) : new Vector3(WorldGrid.CellSize, 1.1f, 0.12f);
            _rails.Add(go);
        }

        public override string StatusText()
        {
            var b = base.StatusText();
            if (!IsBuilt) return b;
            return b + (DirectlySupported ? "\nОпора — прямо под настилом" : "\nКонсоль: держится за соседний настил на опоре") + "\nShift+клик — осмотр, обычный клик — пройти по настилу";
        }
    }

    /// <summary>
    /// Лестница 1×2: пандус с ступенями от земли до второго яруса. Верхний конец упирается в настил (ExitCell).
    /// Для поиска пути — единственный переход между уровнями. Физически — наклонный коллайдер (игрок поднимается сам).
    /// </summary>
    public class Stairs : Structure
    {
        public Vector2Int LowCell { get; private set; }
        public Vector2Int TopCell { get; private set; }
        public Vector2Int ExitCell { get; private set; }

        public override bool CanInteract => !IsDestroyed && (!IsBuilt || InputBridge.KeyHeld(GameKey.LeftShift));

        /// <summary>Клетки лестницы по прямоугольнику и повороту: нижняя, верхняя и клетка настила, куда она выводит.</summary>
        public static void CellsFor(RectInt fp, int rot, out Vector2Int low, out Vector2Int top, out Vector2Int exit)
        {
            switch (rot & 3)
            {
                case 0: low = new Vector2Int(fp.x, fp.y); top = new Vector2Int(fp.x, fp.y + 1); exit = new Vector2Int(fp.x, fp.y + 2); break;
                case 1: low = new Vector2Int(fp.x, fp.y); top = new Vector2Int(fp.x + 1, fp.y); exit = new Vector2Int(fp.x + 2, fp.y); break;
                case 2: low = new Vector2Int(fp.x, fp.y + 1); top = new Vector2Int(fp.x, fp.y); exit = new Vector2Int(fp.x, fp.y - 1); break;
                default: low = new Vector2Int(fp.x + 1, fp.y); top = new Vector2Int(fp.x, fp.y); exit = new Vector2Int(fp.x - 1, fp.y); break;
            }
        }

        protected override void ConfigureCollider(BoxCollider c)
        {
            // основной коробчатый коллайдер не нужен — его заменяет наклонный пандус
            c.enabled = false;
            float len = WorldGrid.CellSize * 2f, h = WorldGrid.LevelHeight;
            float ang = Mathf.Atan2(h, len) * Mathf.Rad2Deg;
            var go = new GameObject("#ramp") { layer = Layers.Structure };
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0, h * 0.5f - 0.15f / Mathf.Cos(ang * Mathf.Deg2Rad), 0);
            go.transform.localRotation = Quaternion.Euler(-ang, 0, 0);
            var bc = go.AddComponent<BoxCollider>();
            bc.size = new Vector3(WorldGrid.CellSize * 0.9f, 0.3f, Mathf.Sqrt(len * len + h * h));
        }

        protected override void OnCreated()
        {
            CellsFor(Footprint, Rotation, out var low, out var top, out var exit);
            LowCell = low; TopCell = top; ExitCell = exit;
            Game.Grid.RegisterStair(this);
            (Game.Grid.DeckAt(exit.x, exit.y) as Deck)?.RefreshEdges();
        }

        protected override void OnRemoved()
        {
            Game.Grid.UnregisterStair(this);
            Game.Entry?.StartCoroutine(RefreshExitLater(ExitCell));
        }

        static System.Collections.IEnumerator RefreshExitLater(Vector2Int exit)
        {
            yield return null;
            (Game.Grid?.DeckAt(exit.x, exit.y) as Deck)?.RefreshEdges();
        }

        /// <summary>Высота пандуса в точке: 0 у нижнего края, LevelHeight у верхнего.</summary>
        public float RampHeight(Vector3 p)
        {
            var local = Quaternion.Inverse(transform.rotation) * (p - transform.position);
            float t = Mathf.Clamp01((local.z + WorldGrid.CellSize) / (WorldGrid.CellSize * 2f));
            return transform.position.y + t * WorldGrid.LevelHeight;
        }

        public override string StatusText()
        {
            var b = base.StatusText();
            if (!IsBuilt) return b;
            bool ok = Game.Grid.WalkableUpper(ExitCell.x, ExitCell.y);
            return b + (ok ? "\nВыводит на настил второго яруса" : "\nНаверху нет достроенного настила — подняться некуда");
        }
    }

    /// <summary>Склад-подложка для уровней — используется в сохранениях.</summary>
    public static class StructureLookup
    {
        public static Structure FirstOf(StructureKind k)
        {
            foreach (var s in Registry.Structures) if (s != null && s.Def.Kind == k && !s.IsDestroyed) return s;
            return null;
        }

        public static int CountBuilt(StructureKind k)
        {
            int n = 0;
            foreach (var s in Registry.Structures) if (s != null && s.IsBuilt && s.Def.Kind == k) n++;
            return n;
        }

        public static T Nearest<T>(Vector3 p, System.Func<T, bool> filter = null) where T : Structure
        {
            T best = null; float bd = float.MaxValue;
            foreach (var s in Registry.Structures)
            {
                if (!(s is T t) || s.IsDestroyed) continue;
                if (filter != null && !filter(t)) continue;
                float d = (s.transform.position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = t; }
            }
            return best;
        }
    }
}
