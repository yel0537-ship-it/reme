using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    public enum EnemyKind { Grazer, Stalker, Swarmer, Jumper, Brute, Spitter, Tank, Synth }

    /// <summary>Описание типа существа. Стоимость Cost — «цена» в бюджете волны у директора.</summary>
    public class EnemyArchetype
    {
        public EnemyKind Kind;
        public string Name;
        public string Description;
        public float MaxHp;
        public ArmorClass Armor = ArmorClass.Light;
        public float FlatArmor;
        public float Speed = 5f;
        public float Damage = 10f;
        public float AttackInterval = 1f;
        public float AttackRange = 1.6f;
        public float StructureDamageMult = 1f;
        public float Size = 1f;
        public int Cost = 1;
        public Rank MinRank = Rank.Specialist;
        public int MinDay = 1;
        public float Aggro = 8f;
        public bool Fauna;
        public int Rations;
        public bool Ranged;
        public Sfx AttackSound = Sfx.Screech;
        public string RequiredFlag;   // например, синты — только после активации завода ИИ

        static Dictionary<EnemyKind, EnemyArchetype> _map;

        public static EnemyArchetype Get(EnemyKind k)
        {
            if (_map == null) Build();
            return _map[k];
        }

        public static IEnumerable<EnemyArchetype> All { get { if (_map == null) Build(); return _map.Values; } }

        static void Build()
        {
            _map = new Dictionary<EnemyKind, EnemyArchetype>();
            void Add(EnemyArchetype a) => _map[a.Kind] = a;
            Add(new EnemyArchetype { Kind = EnemyKind.Grazer, Name = "Рогач-травоед", Description = "Пассивное стадное животное. Мясо — пайки.",
                MaxHp = 130f, Speed = 3.2f, Damage = 18f, AttackInterval = 1.5f, AttackRange = 2.4f, Size = 1.8f, Fauna = true, Rations = 6, Aggro = 0f, AttackSound = Sfx.Roar });
            Add(new EnemyArchetype { Kind = EnemyKind.Stalker, Name = "Саванный охотник", Description = "Агрессивный хищник. Нападает на одиночек.",
                MaxHp = 150f, Speed = 6.8f, Damage = 14f, AttackInterval = 1.1f, AttackRange = 1.9f, Size = 1.0f, Fauna = true, Rations = 3, Aggro = 14f });
            Add(new EnemyArchetype { Kind = EnemyKind.Swarmer, Name = "Грызун", Description = "Мелкая тварь: портит мелкие постройки и перегрызает кабели.",
                MaxHp = 30f, Speed = 7.2f, Damage = 5f, AttackInterval = 0.6f, AttackRange = 1.1f, StructureDamageMult = 1.6f, Size = 0.45f, Cost = 1, Aggro = 5f });
            Add(new EnemyArchetype { Kind = EnemyKind.Jumper, Name = "Прыгун", Description = "Перепрыгивает ограждения и низкие стены, прыгает на механизмы.",
                MaxHp = 75f, Speed = 6.2f, Damage = 12f, AttackInterval = 0.9f, AttackRange = 1.5f, StructureDamageMult = 1.2f, Size = 0.85f, Cost = 3, MinDay = 2, Aggro = 6f });
            Add(new EnemyArchetype { Kind = EnemyKind.Brute, Name = "Громила", Description = "Тяжёлый таран. Проламывает стены в слабых местах.",
                MaxHp = 280f, Armor = ArmorClass.Medium, FlatArmor = 3f, Speed = 3.7f, Damage = 32f, AttackInterval = 1.4f, AttackRange = 2.2f,
                StructureDamageMult = 1.3f, Size = 1.4f, Cost = 5, MinDay = 3, Aggro = 7f, AttackSound = Sfx.Roar });
            Add(new EnemyArchetype { Kind = EnemyKind.Spitter, Name = "Плевун", Description = "Плюётся кислотой из-за стен. Кислота разъедает постройки.",
                MaxHp = 110f, Speed = 4f, Damage = 16f, AttackInterval = 2.3f, AttackRange = 14f, Size = 1.0f, Cost = 4, MinDay = 4, Ranged = true, Aggro = 14f });
            Add(new EnemyArchetype { Kind = EnemyKind.Tank, Name = "Танк", Description = "Огромная бронированная тварь. Медленно идёт прямо к реактору — концентрируйте огонь.",
                MaxHp = 2600f, Armor = ArmorClass.Heavy, FlatArmor = 12f, Speed = 2.1f, Damage = 120f, AttackInterval = 2.5f, AttackRange = 3.6f,
                StructureDamageMult = 2f, Size = 3.2f, Cost = 30, MinRank = Rank.Captain, MinDay = 5, Aggro = 0f, AttackSound = Sfx.Roar });
            Add(new EnemyArchetype { Kind = EnemyKind.Synth, Name = "Синт-страж", Description = "Боевой автомат заброшенного завода ИИ. Пули почти не берут — нужны лазеры и взрывчатка.",
                MaxHp = 220f, Armor = ArmorClass.Synthetic, FlatArmor = 5f, Speed = 4.2f, Damage = 14f, AttackInterval = 1.4f, AttackRange = 12f,
                Size = 1.1f, Cost = 6, Ranged = true, Aggro = 12f, AttackSound = Sfx.Laser, RequiredFlag = "ai_factory_active" });
        }
    }
}
