using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    /// <summary>
    /// Базовая постройка. Жизненный цикл: голограмма-чертёж → стройка (дроны/инженеры/игрок) → работа →
    /// повреждения/ремонт → разрушение. Наследники переопределяют OnBuilt/Tick.
    /// </summary>
    public class Structure : MonoBehaviour, IInteractable
    {
        public BuildingDef Def { get; private set; }
        public RectInt Footprint { get; private set; }
        public int Rotation { get; private set; }
        /// <summary>Уровень: 0 — земля, 1 — второй ярус.</summary>
        public int Level { get; private set; }
        public GridLayer Layer { get; private set; }
        public bool IsElevated => Level > 0;
        public Health Health { get; private set; }
        public bool IsBuilt { get; private set; }
        public float BuildProgress { get; private set; }
        public bool Powered { get; set; }
        public bool CableCut { get; set; }
        /// <summary>Доля удовлетворения энергии 0..1 (ставит PowerGrid).</summary>
        public float Efficiency { get; set; } = 1f;
        public Transform Model { get; private set; }
        public bool IsDestroyed { get; private set; }
        public bool IsTargetCore => Def.Kind == StructureKind.Reactor;
        public float LastRepairTime { get; set; } = -99f;
        public bool FreeOfCharge; // орбитальная доставка

        ModelBuilder.MaterialSwap _swap;
        protected readonly List<Light> NightLights = new List<Light>();
        BoxCollider _collider;
        protected BoxCollider Collider => _collider;

        public virtual bool PassableForFriendly => Def.PassableForFriendly && IsBuilt;
        public virtual bool PassableForEnemies => false;
        public virtual float CurrentEnergyUse => IsBuilt ? Def.EnergyUse : 0f;
        public virtual float CurrentEnergyProduce => IsBuilt && !Health.IsDead ? Def.EnergyProduce : 0f;
        public bool IsOperational => IsBuilt && !IsDestroyed && (!Def.NeedsPower || (Powered && !CableCut));
        public float OperationalFactor => IsOperational ? (Def.NeedsPower ? Efficiency : 1f) : 0f;
        public bool NeedsRepair => IsBuilt && Health.Current < Health.Max - 0.5f;
        public virtual bool NeedsService => CableCut;
        public Vector3 Center => transform.position;
        public float HalfExtent => Mathf.Max(Footprint.width, Footprint.height) * WorldGrid.CellSize * 0.5f;

        public string InteractLabel => IsBuilt ? $"Осмотр: {Def.Name}" : $"Помочь строить: {Def.Name} ({Mathf.RoundToInt(BuildProgress * 100)}%)";
        public Vector3 InteractPoint => ClosestPoint(Game.Player != null ? Game.Player.transform.position : transform.position);
        public float InteractRange => 2.2f;
        public virtual bool CanInteract => !IsDestroyed;

        /// <summary>Можно ли сейчас вести монтаж: у построек второго яруса опора (и настил) должны быть достроены.</summary>
        public bool ReadyToBuild => Level == 0 || Game.Grid == null || Game.Grid.SupportReady(this);

        /// <summary>Склеивать ли статичные меши модели (стены и настил перестраивают секции — не склеиваем).</summary>
        protected virtual bool CombineModel => true;

        /// <summary>Фабрика: создаёт постройку нужного класса с моделью и регистрирует в сетке.</summary>
        public static Structure Create(BuildingDef def, RectInt footprint, int rotation, bool instant, int level = 0)
        {
            var go = new GameObject(def.Name);
            go.transform.SetParent(Game.Build != null ? Game.Build.transform : Game.Root, false);
            go.layer = Layers.Structure;
            var s = (Structure)go.AddComponent(def.Component);
            s.Setup(def, footprint, rotation, instant, level);
            return s;
        }

        /// <summary>Слой сетки для постройки на уровне: настил — свой слой второго яруса.</summary>
        public static GridLayer LayerFor(BuildingDef def, int level) =>
            def.Kind == StructureKind.Deck ? GridLayer.Deck : level > 0 ? GridLayer.Upper : GridLayer.Ground;

        void Setup(BuildingDef def, RectInt footprint, int rotation, bool instant, int level)
        {
            Def = def;
            Footprint = footprint;
            Rotation = rotation;
            Layer = LayerFor(def, level);
            Level = Layer == GridLayer.Ground ? 0 : 1;
            var pos = Game.Grid.RectCenter(footprint);
            pos.y = MaxGroundHeight(footprint) + (Level > 0 ? WorldGrid.LevelHeight : 0f);
            transform.position = pos;
            transform.rotation = Quaternion.Euler(0, rotation * 90f, 0);

            Health = gameObject.AddComponent<Health>();
            float hp = def.MaxHp;
            if (this is WallSegment && Game.Skills != null) hp *= Game.Skills.WallHpMult;
            Health.Init(hp, ArmorClass.Structure, Faction.Player, def.FlatArmor);
            Health.DamageReduction = Game.Skills != null ? Game.Skills.StructureDamageReduction : 0f;
            Health.Died += _ => DestroyStructure();
            Health.Damaged += OnDamaged;

            var mb = new ModelBuilder(transform, "Model");
            def.Model?.Invoke(mb, def);
            Model = mb.Root;
            CollectNightLights();
            if (CombineModel) ModelBuilder.CombineStatic(Model);

            _collider = gameObject.AddComponent<BoxCollider>();
            var size = new Vector3(def.Size.x * WorldGrid.CellSize * 0.96f, Mathf.Max(0.6f, def.Height), def.Size.y * WorldGrid.CellSize * 0.96f);
            _collider.size = size;
            _collider.center = new Vector3(0, size.y * 0.5f, 0);
            ConfigureCollider(_collider);

            Game.Grid.Occupy(this, footprint, Layer);
            Registry.Structures.Add(this);
            OnCreated();

            if (instant) Complete(true);
            else
            {
                Health.Current = Health.Max * 0.1f;
                _swap = ModelBuilder.SwapAll(Model, Palette.HoloBuild);
                SetLights(false);
            }
            GameEvents.Placed(this);
        }

        float MaxGroundHeight(RectInt r)
        {
            float h = float.MinValue;
            for (int y = r.yMin; y < r.yMax; y++)
            for (int x = r.xMin; x < r.xMax; x++)
                h = Mathf.Max(h, Game.Grid.Height[Game.Grid.Index(x, y)]);
            return h > float.MinValue ? h : 0f;
        }

        void CollectNightLights()
        {
            foreach (var l in Model.GetComponentsInChildren<Light>(true)) NightLights.Add(l);
        }

        protected void SetLights(bool on)
        {
            foreach (var l in NightLights) if (l != null) l.enabled = on;
        }

        /// <summary>Подстройка коллайдера под форму (настил — плита, лестница — пандус).</summary>
        protected virtual void ConfigureCollider(BoxCollider c) { }

        protected virtual void OnCreated() { }
        protected virtual void OnBuilt() { }
        protected virtual void OnRemoved() { }

        /// <summary>Вклад в строительство: amount — доля от 0 до 1.</summary>
        public void AddProgress(float amount)
        {
            if (IsBuilt || IsDestroyed || !ReadyToBuild) return;
            BuildProgress = Mathf.Clamp01(BuildProgress + amount);
            Health.Current = Mathf.Max(Health.Current, Health.Max * Mathf.Lerp(0.1f, 1f, BuildProgress));
            if (BuildProgress >= 1f) Complete(false);
        }

        void Complete(bool silent)
        {
            IsBuilt = true;
            BuildProgress = 1f;
            Health.Current = Health.Max;
            _swap?.Restore();
            _swap = null;
            SetLights(true);
            if (Def.StorageBonus > 0 && Game.Bank != null) Game.Bank.ExtraCapacity += Def.StorageBonus;
            OnBuilt();
            Game.Power?.MarkDirty();
            Game.Grid.MarkDirty();
            if (!silent)
            {
                Game.Sound?.PlayAt(Sfx.BuildDone, transform.position, 0.7f);
                Game.Fx?.Emit(FxKind.Glow, transform.position + Vector3.up, 6);
                Game.Log?.Info($"Построено: {Def.Name}");
            }
            GameEvents.Completed(this);
        }

        public void Repair(float hp)
        {
            if (!IsBuilt || IsDestroyed) return;
            Health.Heal(hp);
            LastRepairTime = Time.time;
        }

        /// <summary>Обслуживание (кабели, износ, перезарядка) — переопределяется.</summary>
        public virtual void Service()
        {
            if (CableCut)
            {
                CableCut = false;
                Game.Power?.MarkDirty();
                Game.Log?.Info($"Кабель восстановлен: {Def.Name}");
            }
        }

        public void CutCable()
        {
            if (CableCut || !Def.NeedsPower || !IsBuilt) return;
            CableCut = true;
            Game.Power?.MarkDirty();
            Game.Sound?.PlayAt(Sfx.Chew, transform.position, 0.8f);
            Game.Fx?.Emit(FxKind.Sparks, transform.position + Vector3.up * 0.4f, 10, Vector3.up);
            Game.Log?.Warn($"Перегрызен кабель: {Def.Name} обесточен!");
        }

        void OnDamaged(Health h, DamageInfo d, float amount)
        {
            if (Random.value < 0.35f) Game.Fx?.Emit(FxKind.Sparks, d.Point == Vector3.zero ? transform.position + Vector3.up : d.Point, 3, Vector3.up);
        }

        public void DestroyStructure(bool refund = false)
        {
            if (IsDestroyed) return;
            IsDestroyed = true;
            var pos = transform.position;
            if (!refund)
            {
                Game.Fx?.Explosion(pos + Vector3.up * 0.5f, Mathf.Max(2f, HalfExtent * 1.3f));
                Game.Sound?.PlayAt(Sfx.Explosion, pos, 0.8f);
                Game.Log?.Warn($"Уничтожено: {Def.Name}");
            }
            if (IsBuilt && Def.StorageBonus > 0 && Game.Bank != null) Game.Bank.ExtraCapacity -= Def.StorageBonus;
            OnRemoved();
            Game.Grid.Free(this, Footprint, Layer);
            Registry.Structures.Remove(this);
            Game.Power?.MarkDirty();
            GameEvents.Destroyed(this);
            Destroy(gameObject);
        }

        /// <summary>Снос игроком: 50% возврат за построенное, 100% — за чертёж.</summary>
        public void Demolish()
        {
            if (IsDestroyed || IsTargetCore || !Def.Buildable) return;
            var blocker = Game.Grid.DemolishBlocker(this);
            if (blocker != null) { Game.Log?.Warn(blocker); Game.Sound?.Play2D(Sfx.UiError, 0.6f); return; }
            if (!FreeOfCharge) Game.Bank.Add(Def.Cost.Scaled(IsBuilt ? 0.5f : 1f));
            DestroyStructure(true);
        }

        /// <summary>Обрушение: опора под постройкой второго яруса разрушена.</summary>
        public void Collapse()
        {
            if (IsDestroyed) return;
            var pos = transform.position;
            Game.Fx?.Emit(FxKind.Debris, pos, 18, Vector3.down);
            Game.Fx?.Emit(FxKind.Sparks, pos + Vector3.up * 0.5f, 10, Vector3.down);
            Game.Log?.Alert($"Обрушение: «{Def.Name}» лишился опоры!");
            Game.Tutorial?.ShowTip("collapse", "Обрушение", "Второй ярус держится на стенах и колоннах. Разрушили опору — настил и всё, что на нём, падает. Чините стены под турелями и ставьте колонны с двух сторон пролёта.");
            // падающие обломки бьют тех, кто внизу
            var below = pos + Vector3.down * WorldGrid.LevelHeight;
            foreach (var e in Registry.Enemies.ToArray())
                if (e != null && !e.IsDead && (e.transform.position - below).sqrMagnitude < HalfExtent * HalfExtent + 4f)
                    e.Health.TakeDamage(new DamageInfo(60f, DamageType.Explosive, e.transform.position, Faction.Player, gameObject));
            DestroyStructure(false);
        }

        public Vector3 ClosestPoint(Vector3 p)
        {
            var c = transform.position;
            float hx = Footprint.width * WorldGrid.CellSize * 0.5f, hz = Footprint.height * WorldGrid.CellSize * 0.5f;
            return new Vector3(Mathf.Clamp(p.x, c.x - hx, c.x + hx), c.y, Mathf.Clamp(p.z, c.z - hz, c.z + hz));
        }

        public float DistanceTo(Vector3 p)
        {
            var q = ClosestPoint(p);
            return Vector2.Distance(new Vector2(p.x, p.z), new Vector2(q.x, q.z));
        }

        public virtual void Interact(PlayerController player)
        {
            Game.Ui?.Inspect(this);
        }

        /// <summary>Строка состояния для инспектора.</summary>
        public virtual string StatusText()
        {
            if (!IsBuilt && !ReadyToBuild) return "Ждёт опору: сначала достройте стену, колонну или настил под постройкой";
            if (!IsBuilt) return $"Строится: {Mathf.RoundToInt(BuildProgress * 100)}%";
            if (CableCut) return "Кабель перегрызен — нужен ремонт";
            if (Def.NeedsPower && !Powered) return "Нет подключения к энергосети";
            if (Def.NeedsPower && Efficiency < 0.99f) return $"Нехватка энергии: {Mathf.RoundToInt(Efficiency * 100)}%";
            return "Работает";
        }

        protected virtual void Update()
        {
            if (!IsBuilt || IsDestroyed || !Game.IsPlaying) return;
            if (Def.Noise > 0f && IsOperational) Game.Noise?.EmitContinuous(transform.position, Def.Noise * Time.deltaTime, NoiseSource.Machinery);
            if (NightLights.Count > 0)
            {
                bool want = Game.Clock != null && (Game.Clock.IsNight || Game.Clock.WeatherDim > 0.5f) && (!Def.NeedsPower || IsOperational);
                if (NightLights[0] != null && NightLights[0].enabled != want) SetLights(want);
            }
        }
    }
}
