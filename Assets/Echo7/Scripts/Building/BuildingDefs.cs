using System;
using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    public enum BuildCategory { Core, Walls, Power, Base, Industry, Defense, Logistics }

    public enum StructureKind
    {
        Reactor, Command, Fence, Wall, TitaniumWall, Gate, Pylon, Solar, Generator,
        Habitat, MedBay, Storage, Extractor, Condenser, DroneBay,
        MgTurret, LaserTurret, HeavyTurret, Mine, Sensor, SurveyProbe, ShieldProjector,
        LandingPad, Spaceport, DecoyBeacon
    }

    /// <summary>
    /// Описание типа постройки. Значения по умолчанию — в <see cref="BuildingCatalog.CodeDefaults"/>,
    /// геймдизайнер переопределяет их в ассете <see cref="BuildingBalance"/> (Resources/Echo7Balance/Buildings).
    /// </summary>
    public class BuildingDef
    {
        public StructureKind Kind;
        public string Name;
        public string Description;
        public BuildCategory Category;
        public Vector2Int Size = Vector2Int.one;
        public float MaxHp = 300f;
        public float FlatArmor = 2f;
        public Cost Cost = new Cost();
        public float BuildTime = 10f;         // секунды работы одного дрона
        public float EnergyUse;               // постоянное потребление
        public float EnergyProduce;           // выработка
        public float PowerRadius;             // для реактора/узлов
        public Rank MinRank = Rank.Specialist;
        public Feature? RequiredFeature;
        public string RequiredFlag;           // уникальный чертёж (например, от VIP)
        public bool Buildable = true;
        public bool Jumpable;                 // прыгуны перемахивают
        public bool IsSmall;                  // цель мелких тварей
        public bool IsMechanism;              // цель прыгунов (турели, двери)
        public bool PassableForFriendly;      // ворота
        public bool LinePlacement;            // стены — протяжкой
        public bool RequiresDeposit;          // экстрактор
        public bool NeedsPower => EnergyUse > 0f;
        public float Height = 3f;
        public int Beds;
        public int StorageBonus;
        public float Noise;                   // шум в секунду при работе
        public float MoraleBonus;
        public Type Component = typeof(Structure);
        public Action<ModelBuilder, BuildingDef> Model;

        public float FootprintMeters => Mathf.Max(Size.x, Size.y) * WorldGrid.CellSize;
    }

    public static class BuildingCatalog
    {
        static List<BuildingDef> _all;
        static Dictionary<StructureKind, BuildingDef> _map;

        public static List<BuildingDef> All { get { if (_all == null) Build(); return _all; } }

        public static BuildingDef Get(StructureKind k)
        {
            if (_map == null) Build();
            return _map[k];
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _all = null; _map = null; }

        static void Build()
        {
            var defs = CodeDefaults();
            var map = new Dictionary<StructureKind, BuildingDef>();
            foreach (var d in defs) map[d.Kind] = d;
            _all = defs;
            _map = map;
            var asset = BalanceData.Load<BuildingBalance>(BuildingBalance.File);
            if (asset != null) Apply(asset);
        }

        /// <summary>
        /// Перенести значения ассета на описания. Объекты BuildingDef не пересоздаются, поэтому правка во время Play
        /// сразу видна в меню (цены, время стройки, допуски) и в энергосети; прочность и броня — у новых построек.
        /// </summary>
        public static void Apply(BuildingBalance asset)
        {
            if (_map == null || asset == null) return;
            foreach (var e in asset.buildings)
                if (_map.TryGetValue(e.kind, out var d)) e.ApplyTo(d);
            if (Application.isPlaying) Game.Power?.MarkDirty();
        }

        static Cost C(params (ResourceType, int)[] items) => new Cost(items);
        const ResourceType Al = ResourceType.Alloys, Ti = ResourceType.Titanium, Cr = ResourceType.Crystals, Si = ResourceType.Silicon, Am = ResourceType.Ammo;

        /// <summary>Значения из кода: запасной вариант без ассета и источник для генерации ассета.</summary>
        public static List<BuildingDef> CodeDefaults()
        {
            return new List<BuildingDef>
            {
                new BuildingDef { Kind = StructureKind.Reactor, Name = "Центральный реактор", Category = BuildCategory.Core,
                    Description = "Сердце аванпоста. Даёт энергию в радиусе. Если реактор уничтожен — аванпост потерян.",
                    Size = new Vector2Int(4, 4), MaxHp = 3500f, FlatArmor = 8f, EnergyProduce = 45f, PowerRadius = 18f, Buildable = false, Height = 6f,
                    Component = typeof(Reactor), Model = StructureModels.Reactor },
                new BuildingDef { Kind = StructureKind.Command, Name = "Командный модуль", Category = BuildCategory.Core,
                    Description = "Связь с Центром: задачи, поставки, запросы. База строительных дронов.",
                    Size = new Vector2Int(3, 3), MaxHp = 1600f, FlatArmor = 6f, EnergyUse = 2f, Buildable = false, Height = 7f,
                    Component = typeof(CommandModule), Model = StructureModels.Command },

                new BuildingDef { Kind = StructureKind.Fence, Name = "Ограждение", Category = BuildCategory.Walls,
                    Description = "Дешёвый низкий барьер. Задерживает толпу, но прыгуны его перемахивают.",
                    Cost = C((Al, 3)), MaxHp = 220f, BuildTime = 3f, Jumpable = true, LinePlacement = true, Height = 1.4f,
                    Component = typeof(WallSegment), Model = StructureModels.Fence },
                new BuildingDef { Kind = StructureKind.Wall, Name = "Стена", Category = BuildCategory.Walls,
                    Description = "Модульная бетонная стена. Высокая — прыгуны не перепрыгнут.",
                    Cost = C((Al, 7)), MaxHp = 650f, FlatArmor = 4f, BuildTime = 6f, LinePlacement = true, Height = 3.2f,
                    Component = typeof(WallSegment), Model = StructureModels.Wall },
                new BuildingDef { Kind = StructureKind.TitaniumWall, Name = "Титановая стена", Category = BuildCategory.Walls,
                    Description = "Усиленная стена из местного титана. Держит удар «танка».",
                    Cost = C((Al, 5), (Ti, 4)), MaxHp = 1500f, FlatArmor = 10f, BuildTime = 9f, LinePlacement = true, Height = 3.6f,
                    RequiredFeature = Feature.TitaniumWalls, Component = typeof(WallSegment), Model = StructureModels.TitaniumWall },
                new BuildingDef { Kind = StructureKind.Gate, Name = "Ворота", Category = BuildCategory.Walls,
                    Description = "Пропускают своих. Прыгуны могут повредить механизм — тогда ворота заклинит открытыми.",
                    Cost = C((Al, 14), (Si, 1)), MaxHp = 800f, FlatArmor = 5f, BuildTime = 8f, EnergyUse = 1f, IsMechanism = true,
                    PassableForFriendly = true, Height = 3.4f, Component = typeof(Gate), Model = StructureModels.Gate },

                new BuildingDef { Kind = StructureKind.Pylon, Name = "Энергоузел", Category = BuildCategory.Power,
                    Description = "Продлевает энергосеть (радиус 14 м). Мелкие твари перегрызают кабели и узлы.",
                    Cost = C((Al, 5), (Si, 1)), MaxHp = 140f, BuildTime = 4f, PowerRadius = 14f, IsSmall = true, Height = 3.5f,
                    Component = typeof(Structure), Model = StructureModels.Pylon },
                new BuildingDef { Kind = StructureKind.Solar, Name = "Солнечная панель", Category = BuildCategory.Power,
                    Description = "+9 энергии днём. В пекло — больше, в бурю и дождь — почти ничего.",
                    Size = new Vector2Int(2, 2), Cost = C((Al, 10), (Si, 4)), MaxHp = 220f, BuildTime = 7f, EnergyProduce = 9f, IsSmall = true, Height = 2f,
                    Component = typeof(SolarPanel), Model = StructureModels.Solar },
                new BuildingDef { Kind = StructureKind.Generator, Name = "Геотермальный генератор", Category = BuildCategory.Power,
                    Description = "+22 энергии круглосуточно. Гудит — немного шумит.",
                    Size = new Vector2Int(2, 2), Cost = C((Al, 25), (Ti, 6)), MaxHp = 500f, FlatArmor = 4f, BuildTime = 14f, EnergyProduce = 22f, Noise = 0.25f,
                    MinRank = Rank.Lieutenant, Height = 3f, Component = typeof(Structure), Model = StructureModels.Generator },

                new BuildingDef { Kind = StructureKind.Habitat, Name = "Жилой блок", Category = BuildCategory.Base,
                    Description = "4 койки. Тесноту гарнизон переносит плохо — мораль падает. Укрытие от насекомых.",
                    Size = new Vector2Int(3, 3), Cost = C((Al, 28), (Si, 6)), MaxHp = 900f, FlatArmor = 4f, BuildTime = 16f, EnergyUse = 3f, Beds = 4, MoraleBonus = 4f,
                    Height = 3f, Component = typeof(Habitat), Model = StructureModels.Habitat },
                new BuildingDef { Kind = StructureKind.MedBay, Name = "Медблок", Category = BuildCategory.Base,
                    Description = "Лечит раненых рядом, лечит лихорадку (тратит аптечки), раз в 8 ч производит аптечку.",
                    Size = new Vector2Int(3, 3), Cost = C((Al, 24), (Cr, 2), (Si, 2)), MaxHp = 700f, BuildTime = 15f, EnergyUse = 5f,
                    Height = 3f, Component = typeof(MedBay), Model = StructureModels.MedBay },
                new BuildingDef { Kind = StructureKind.Storage, Name = "Склад", Category = BuildCategory.Base,
                    Description = "+300 к вместимости каждого ресурса. Закрытая вода меньше испаряется.",
                    Size = new Vector2Int(2, 2), Cost = C((Al, 16)), MaxHp = 600f, FlatArmor = 3f, BuildTime = 9f, EnergyUse = 1f, StorageBonus = 300,
                    Height = 2.6f, Component = typeof(Structure), Model = StructureModels.Storage },

                new BuildingDef { Kind = StructureKind.Extractor, Name = "Бур-экстрактор", Category = BuildCategory.Industry,
                    Description = "Ставится на залежь и добывает сам. Шумный — привлекает фауну.",
                    Size = new Vector2Int(2, 2), Cost = C((Al, 20), (Si, 2)), MaxHp = 500f, FlatArmor = 3f, BuildTime = 12f, EnergyUse = 6f, Noise = 0.7f,
                    RequiresDeposit = true, IsSmall = true, Height = 4f, Component = typeof(Extractor), Model = StructureModels.Extractor },
                new BuildingDef { Kind = StructureKind.Condenser, Name = "Влагоконденсатор", Category = BuildCategory.Industry,
                    Description = "Добывает воду из воздуха. У озера — вдвое больше. В пекло почти бесполезен.",
                    Size = new Vector2Int(2, 2), Cost = C((Al, 14), (Si, 3)), MaxHp = 350f, BuildTime = 9f, EnergyUse = 3f, IsSmall = true,
                    Height = 3.5f, Component = typeof(Condenser), Model = StructureModels.Condenser },
                new BuildingDef { Kind = StructureKind.DroneBay, Name = "Дронпорт", Category = BuildCategory.Industry,
                    Description = "Два дрона-сборщика летают к залежам и в пещеры. Тише, чем ходить самому.",
                    Size = new Vector2Int(3, 3), Cost = C((Al, 34), (Si, 8), (Cr, 2)), MaxHp = 600f, BuildTime = 16f, EnergyUse = 6f,
                    Height = 2.5f, Component = typeof(DroneBay), Model = StructureModels.DroneBay },

                new BuildingDef { Kind = StructureKind.MgTurret, Name = "Пулемётный узел", Category = BuildCategory.Defense,
                    Description = "Дешёвая скорострельная турель. Эффективна против лёгкой фауны, тратит патроны.",
                    Size = new Vector2Int(2, 2), Cost = C((Al, 18)), MaxHp = 450f, FlatArmor = 4f, BuildTime = 10f, EnergyUse = 2f, IsMechanism = true,
                    Height = 2.6f, Component = typeof(Turret), Model = StructureModels.MgTurret },
                new BuildingDef { Kind = StructureKind.LaserTurret, Name = "Лазерный излучатель", Category = BuildCategory.Defense,
                    Description = "Пробивает тяжёлую броню. Во время стрельбы потребляет много энергии.",
                    Size = new Vector2Int(2, 2), Cost = C((Al, 24), (Cr, 6), (Si, 4)), MaxHp = 400f, FlatArmor = 3f, BuildTime = 14f, EnergyUse = 3f, IsMechanism = true,
                    RequiredFeature = Feature.LaserTurrets, Height = 2.8f, Component = typeof(Turret), Model = StructureModels.LaserTurret },
                new BuildingDef { Kind = StructureKind.HeavyTurret, Name = "Тяжёлая турель", Category = BuildCategory.Defense,
                    Description = "Орудие с осколочными снарядами. Медленное, бьёт по площади. 2 патрона за выстрел.",
                    Size = new Vector2Int(3, 3), Cost = C((Al, 42), (Ti, 14)), MaxHp = 950f, FlatArmor = 8f, BuildTime = 20f, EnergyUse = 5f, IsMechanism = true,
                    RequiredFeature = Feature.HeavyTurrets, Height = 3.4f, Component = typeof(Turret), Model = StructureModels.HeavyTurret },
                new BuildingDef { Kind = StructureKind.Mine, Name = "Мина", Category = BuildCategory.Defense,
                    Description = "Расходуется при срабатывании. Перезарядка: 2 сплава и 2 патрона (дроны делают сами).",
                    Cost = C((Al, 3), (Am, 3)), MaxHp = 40f, FlatArmor = 0f, BuildTime = 2f, IsSmall = true, Height = 0.3f,
                    Component = typeof(Mine), Model = StructureModels.Mine },
                new BuildingDef { Kind = StructureKind.Sensor, Name = "Сенсорная мачта", Category = BuildCategory.Defense,
                    Description = "+15% дальности турелям в радиусе 20 м и раннее предупреждение о волнах.",
                    Cost = C((Al, 10), (Cr, 2)), MaxHp = 160f, BuildTime = 6f, EnergyUse = 2f, IsSmall = true, Height = 5f,
                    MinRank = Rank.Lieutenant, Component = typeof(Structure), Model = StructureModels.Sensor },
                new BuildingDef { Kind = StructureKind.SurveyProbe, Name = "Геологический зонд", Category = BuildCategory.Industry,
                    Description = "Геологоразведка: открывает скрытые залежи в радиусе 50 м.",
                    Cost = C((Al, 10), (Si, 2)), MaxHp = 150f, BuildTime = 6f, EnergyUse = 1f, IsSmall = true, Height = 3f,
                    RequiredFeature = Feature.GeoSurvey, Component = typeof(SurveyProbe), Model = StructureModels.SurveyProbe },
                new BuildingDef { Kind = StructureKind.DecoyBeacon, Name = "Шумовой маяк", Category = BuildCategory.Defense,
                    Description = "Приманка: 3 минуты громко шумит, уводя волны и фауну от базы. Ставится далеко за периметром (до +40 м).",
                    Cost = C((Al, 8), (Cr, 1)), MaxHp = 160f, BuildTime = 4f, IsSmall = true, Height = 2.6f,
                    Component = typeof(DecoyBeacon), Model = StructureModels.DecoyBeacon },
                new BuildingDef { Kind = StructureKind.ShieldProjector, Name = "Генератор щита", Category = BuildCategory.Defense,
                    Description = "Уникальный чертёж. Снижает урон по постройкам в радиусе 12 м на 45%.",
                    Size = new Vector2Int(2, 2), Cost = C((Al, 30), (Cr, 10), (Ti, 8)), MaxHp = 500f, BuildTime = 18f, EnergyUse = 10f, IsMechanism = true,
                    RequiredFlag = "bp_shield", Height = 3.5f, Component = typeof(ShieldProjector), Model = StructureModels.ShieldProjector },

                new BuildingDef { Kind = StructureKind.LandingPad, Name = "Посадочная площадка", Category = BuildCategory.Logistics,
                    Description = "Принимает орбитальные доставки тяжёлых модулей и челноки высоких чинов.",
                    Size = new Vector2Int(4, 4), Cost = C((Al, 40), (Ti, 10)), MaxHp = 1100f, FlatArmor = 6f, BuildTime = 20f, EnergyUse = 2f,
                    RequiredFeature = Feature.OrbitalDelivery, Height = 0.6f, Component = typeof(LandingPad), Model = StructureModels.LandingPad },
                new BuildingDef { Kind = StructureKind.Spaceport, Name = "Космопорт", Category = BuildCategory.Logistics,
                    Description = "Собственный челнок: полёты на орбитальную станцию и высадки в новых районах планеты.",
                    Size = new Vector2Int(5, 5), Cost = C((Al, 110), (Ti, 40), (Cr, 18)), MaxHp = 2600f, FlatArmor = 8f, BuildTime = 40f, EnergyUse = 14f,
                    RequiredFeature = Feature.Spaceport, Height = 8f, Component = typeof(Spaceport), Model = StructureModels.Spaceport },
            };
        }

        public static string CategoryName(BuildCategory c)
        {
            switch (c)
            {
                case BuildCategory.Walls: return "Стены";
                case BuildCategory.Power: return "Энергия";
                case BuildCategory.Base: return "Жизнь";
                case BuildCategory.Industry: return "Добыча";
                case BuildCategory.Defense: return "Оборона";
                case BuildCategory.Logistics: return "Логистика";
            }
            return "Ядро";
        }
    }
}
