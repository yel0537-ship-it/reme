using System;
using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    public enum Faction { Player, Hostile, Neutral }

    public enum DamageType { Kinetic, Laser, Explosive, Melee, Acid }

    public enum ArmorClass { None, Light, Medium, Heavy, Structure, Synthetic }

    /// <summary>Слои физики. Unity разрешает использовать безымянные слои 8–31 по номеру.</summary>
    public static class Layers
    {
        public const int Ground = 8;
        public const int Structure = 9;
        public const int Enemy = 10;
        public const int Player = 11;
        public const int Npc = 12;
        public const int Deposit = 13;
        public const int Interactable = 14;
        /// <summary>Перила второго яруса: держат игрока на настиле, но перекат перемахивает через них.</summary>
        public const int Rail = 15;

        public static readonly int GroundMask = 1 << Ground;
        public static readonly int StructureMask = 1 << Structure;
        public static readonly int EnemyMask = 1 << Enemy;
        public static readonly int UnitsMask = (1 << Enemy) | (1 << Player) | (1 << Npc);
        public static readonly int PickMask = (1 << Ground) | (1 << Structure) | (1 << Enemy) | (1 << Npc) | (1 << Deposit) | (1 << Interactable);
        public static readonly int ProjectileMask = (1 << Ground) | (1 << Structure) | (1 << Enemy) | (1 << Player) | (1 << Npc) | (1 << Deposit);

        public static void SetRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform t in go.transform) SetRecursive(t.gameObject, layer);
        }
    }

    public struct DamageInfo
    {
        public float Amount;
        public DamageType Type;
        public Vector3 Point;
        public Faction Source;
        public GameObject Instigator;

        public DamageInfo(float amount, DamageType type, Vector3 point, Faction source, GameObject instigator = null)
        {
            Amount = amount; Type = type; Point = point; Source = source; Instigator = instigator;
        }
    }

    /// <summary>Матрица «тип урона × класс брони». Лазеры пробивают тяжёлую броню, пули — нет.</summary>
    public static class DamageTable
    {
        public static float Multiplier(DamageType t, ArmorClass a)
        {
            switch (t)
            {
                case DamageType.Kinetic:
                    switch (a)
                    {
                        case ArmorClass.Light: return 1.25f;
                        case ArmorClass.Heavy: return 0.45f;
                        case ArmorClass.Synthetic: return 0.55f;
                        case ArmorClass.Structure: return 0.6f;
                    }
                    break;
                case DamageType.Laser:
                    switch (a)
                    {
                        case ArmorClass.Light: return 0.9f;
                        case ArmorClass.Heavy: return 1.35f;
                        case ArmorClass.Synthetic: return 1.1f;
                    }
                    break;
                case DamageType.Explosive:
                    switch (a)
                    {
                        case ArmorClass.Heavy: return 0.9f;
                        case ArmorClass.Structure: return 1.2f;
                        case ArmorClass.Synthetic: return 1.3f;
                    }
                    break;
                case DamageType.Acid:
                    switch (a)
                    {
                        case ArmorClass.Structure: return 1.35f;
                        case ArmorClass.Synthetic: return 0.5f;
                    }
                    break;
            }
            return 1f;
        }

        /// <summary>Пробивает ли тип урона плоскую броню (лазер — да).</summary>
        public static bool Piercing(DamageType t) => t == DamageType.Laser;
    }

    /// <summary>Здоровье любого объекта: игрока, NPC, существа, постройки.</summary>
    public class Health : MonoBehaviour
    {
        public float Max = 100f;
        public float Current = 100f;
        public ArmorClass Armor = ArmorClass.None;
        [Tooltip("Плоское снижение урона за попадание (кроме лазеров).")]
        public float FlatArmor;
        [Tooltip("Мультипликативное снижение 0..0.9 (щиты, навыки).")]
        public float DamageReduction;
        public Faction Faction = Faction.Neutral;
        public bool Invulnerable;

        public float LastDamageTime { get; private set; } = -999f;
        public DamageInfo LastDamage { get; private set; }
        public bool IsDead => Current <= 0f;
        public float Fraction => Max > 0 ? Mathf.Clamp01(Current / Max) : 0f;

        public event Action<Health, DamageInfo, float> Damaged;
        public event Action<Health> Died;

        public void Init(float max, ArmorClass armor, Faction faction, float flatArmor = 0f)
        {
            Max = max; Current = max; Armor = armor; Faction = faction; FlatArmor = flatArmor;
        }

        public float TakeDamage(DamageInfo d)
        {
            if (IsDead || Invulnerable || d.Amount <= 0f) return 0f;
            float dmg = d.Amount * DamageTable.Multiplier(d.Type, Armor);
            if (!DamageTable.Piercing(d.Type)) dmg = Mathf.Max(dmg * 0.2f, dmg - FlatArmor);
            dmg *= 1f - Mathf.Clamp(DamageReduction, 0f, 0.9f);
            Current -= dmg;
            LastDamageTime = Time.time;
            LastDamage = d;
            Damaged?.Invoke(this, d, dmg);
            if (Current <= 0f)
            {
                Current = 0f;
                Died?.Invoke(this);
            }
            return dmg;
        }

        public void Heal(float amount)
        {
            if (IsDead) return;
            Current = Mathf.Min(Max, Current + amount);
        }

        public void Revive(float fraction = 1f)
        {
            Current = Max * Mathf.Clamp01(fraction);
        }

        public void SetMax(float max, bool keepFraction = true)
        {
            float f = Fraction;
            Max = max;
            Current = keepFraction ? max * f : Mathf.Min(Current, max);
        }
    }

    /// <summary>Быстрые списки целей без физических запросов (объектов немного, линейный поиск дешевле).</summary>
    public static class Registry
    {
        public static readonly List<Enemy> Enemies = new List<Enemy>();
        public static readonly List<Structure> Structures = new List<Structure>();
        public static readonly List<GarrisonNpc> Npcs = new List<GarrisonNpc>();
        public static readonly List<ResourceDeposit> Deposits = new List<ResourceDeposit>();
        public static readonly List<Turret> Turrets = new List<Turret>();

        public static void Reset()
        {
            Enemies.Clear(); Structures.Clear(); Npcs.Clear(); Deposits.Clear(); Turrets.Clear();
        }

        /// <summary>Ближайший живой враг (Hostile) в радиусе.</summary>
        public static Enemy NearestEnemy(Vector3 p, float radius, bool hostileOnly = true)
        {
            Enemy best = null; float bestD = radius * radius;
            for (int i = 0; i < Enemies.Count; i++)
            {
                var e = Enemies[i];
                if (e == null || e.IsDead) continue;
                if (hostileOnly && !e.IsHostile) continue;
                float d = (e.transform.position - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }

        /// <summary>Ближайшая дружественная цель для существ: игрок, NPC.</summary>
        public static Health NearestFriendlyUnit(Vector3 p, float radius, out Transform target)
        {
            target = null; Health best = null; float bestD = radius * radius;
            var pl = Game.Player;
            if (pl != null && !pl.Health.IsDead)
            {
                float d = (pl.transform.position - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = pl.Health; target = pl.transform; }
            }
            for (int i = 0; i < Npcs.Count; i++)
            {
                var n = Npcs[i];
                if (n == null || n.Health.IsDead || n.IsAway) continue;
                float d = (n.transform.position - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = n.Health; target = n.transform; }
            }
            return best;
        }
    }
}
